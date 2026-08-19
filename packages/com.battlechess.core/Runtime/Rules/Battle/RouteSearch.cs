using System;
using System.Collections.Generic;
using BattleChess.Contracts;

namespace BattleChess.Rules
{
    /// <summary>
    /// A route as a search over places <i>and fronts</i>, priced in seconds.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>M31</b>, and it supersedes <b>M18</b>'s ladder. The ladder tried four
    /// things in strict order because rung three had no price, so no comparison
    /// between them was possible. Everything here is an edge with a cost, and the
    /// cost includes the wheel.
    /// </para>
    /// <para>
    /// <b>The state is (place, front), not place.</b> That is the whole of it.
    /// For a body 40 m one way and 20 m the other, the front is not decoration
    /// hung on a position, it is half of where you are: the same point admits the
    /// regiment on one bearing and refuses it on another. Every generator before
    /// this searched in two dimensions and treated the facing as something to
    /// work out afterwards, which is why
    /// <see cref="WaysRound.RoundTheCornersOfIt"/> had to price itself in metres
    /// — what a leg costs in time depends on the front it is entered on, and a
    /// cost that depends on the path taken cannot be given to Dijkstra. With the
    /// front in the state, time is a valid edge cost and the approximation goes
    /// away.
    /// </para>
    /// <para>
    /// <b>Candidates come from the blockers' own edges</b>, never from the line of
    /// march. That is what fixes the diagonal. Measured on the ladder: the same
    /// 30 m gap, crossed by a regiment 20 m across side-on, was walked straight
    /// through at every approach angle from 5° to 55° — twelve of nineteen —
    /// because every aiming rule in it measured against the march and degraded as
    /// the march went diagonal to the bodies. A body's own axes do not care which
    /// way anybody is going.
    /// </para>
    /// </remarks>
    public static class RouteSearch
    {
        /// <summary>
        /// How far off its line of march a regiment will walk before it would
        /// rather turn, in degrees.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The designer's soft cap, kept as a parameter. Below it the regiment
        /// walks while it comes round, which is what <see cref="MovementSystem.AlignmentPenalty"/>
        /// already charges for. Above it, walking means sliding sideways or
        /// backwards, which no body of men does, so it turns instead.
        /// </para>
        /// <para>
        /// Measured across 421 recorded orders, only <b>3.6%</b> fall between 30°
        /// and 45°, so the choice between those two is nearly free. <b>84% exceed
        /// 60°</b>, which is the number that matters: the cap fires on four
        /// orders in five, so what happens above it decides how the game feels.
        /// </para>
        /// </remarks>
        public const float WalkingCapDegrees = 45f;

        /// <summary>Room left beside a body when aiming past it, in metres.</summary>
        private const float MarginMetres = 4f;

        /// <summary>
        /// How many times a blocked leg may be broken down further.
        /// </summary>
        /// <remarks>The designer's figure. A bound on the work, not a rule.</remarks>
        private const int Depth = 5;

        /// <summary>
        /// How many candidate places the search will consider at most.
        /// </summary>
        /// <remarks>
        /// Raised from 26 when the generator stopped repeating itself and
        /// started offering both presentations. It is a bound on the work, and
        /// the work is what matters: the cap used to be reached by duplicates,
        /// which is the worst way to spend it — bodies further down the list
        /// contributed nothing and the search planned as though they were not
        /// there.
        /// </remarks>
        private const int MostPlaces = 48;

        /// <summary>
        /// Every candidate place the search would consider for this march, for a
        /// debug overlay to draw. Not used by <see cref="Find"/> itself, which
        /// calls the private generator directly — this exists so a view layer
        /// can show the same points the planner actually reasoned over, rather
        /// than a reconstruction of them.
        /// </summary>
        public static IReadOnlyList<Vec2> DebugCandidatePlaces(
            BattleState battle, UnitInstance unit, Vec2 destination) =>
            Places(battle, unit, destination);

        /// <summary>
        /// Plans a march, or returns a plan that was not found.
        /// </summary>
        public static Plan Find(
            BattleState battle, UnitInstance unit, Vec2 destination, Facing arriveOn, IBattleLog? log = null)
        {
            if (battle == null) throw new ArgumentNullException(nameof(battle));
            if (unit == null) throw new ArgumentNullException(nameof(unit));

            // M10, and its absence was measured the hard way: without this the
            // suite went from nine seconds to over ten minutes, because every
            // march across open grass built a graph to rediscover that nothing
            // was in the way. The question a regiment actually has is "can I
            // walk straight there", and across open ground the answer is yes.
            Facing alongIt = Marching.AlongTheLine(unit.Position, destination, unit.Facing);

            if (Marching.IsClearLine(battle, unit, unit.Position, destination, alongIt))
                return Straight(unit.Position, destination);

            List<Vec2> places = Places(battle, unit, destination);

            int legs = 0;
            int expanded = 0;

            // What a leg is, geometrically, is asked once and remembered.
            // Smoothing asks about the same legs the search already priced, and
            // before the press was priced there was a second whole search that
            // did too.
            var known = new LegCache(places.Count);

            // One search, with shouldering through priced into it rather than
            // held back as a last resort.
            //
            // What M18 could not do was compare "go round" against "go
            // through", because going through had no price — so it sat as a
            // rung below everything else and the planner ran twice, once
            // forbidding it and once permitting it. Two searches over one graph
            // for one answer.
            //
            // Why it stayed that way is worth stating, because it is not that
            // nobody tried. Pricing the press by pace alone was measured twice
            // and won almost everywhere, and the shape of the cost is why:
            // MovementSystem.PaceWhileInsideItsOwn is a *rate*. It charges only
            // for the ground spent inside somebody, so clipping a corner for
            // five metres costs about three metres of penalty — nothing against
            // a detour of tens of metres and a wheel. What makes shouldering
            // through bad is not that it is slow. It is the shoving.
            //
            // So the rate keeps its job and ShoulderTollSeconds is charged on
            // top, once, for a leg that genuinely presses. A toll rather than a
            // rate is what lets a long way round lose to it and a short one beat
            // it, which is the rule as the designer stated it.
            Route? found = Cheapest(
                battle, unit, places, destination, arriveOn, known, ref legs, ref expanded);

            var effort = new RouteEffort(places.Count, legs, expanded);

            if (!found.HasValue)
                return new Plan(
                    PathResult.Failed(PathFailure.NoRouteExists,
                        "nothing reaches that ground, going round or through", 0),
                    null, false, effort);

            Route route = Smooth(battle, unit, found.Value, arriveOn);

            return Assemble(route, pressed: Presses(battle, unit, route), effort);
        }

        /// <summary>
        /// How dear it is to shoulder through one of your own, in seconds.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>The designer's number, and the one that decides how often a
        /// regiment walks through its own men.</b> Everything else about a press
        /// is measured; this says what the shoving is worth.
        /// </para>
        /// <para>
        /// Forty-five seconds, chosen against what a detour costs rather than
        /// out of the air: a way round an ordinary body runs thirty to eighty
        /// metres of extra ground plus a wheel, which at a regiment's pace comes
        /// to twenty to sixty seconds. Set here, an ordinary detour is worth
        /// taking and a long way round something distant is not — which is the
        /// rule as stated. Raise it and the line holds M1 harder at the price of
        /// longer marches; lower it and regiments start shoving.
        /// </para>
        /// <para>
        /// Charged once per pressing leg, not per metre and not per body. Per
        /// metre is the rate, which already exists and is not what was wrong.
        /// Per body would be finer and costs an <see cref="Marching.IsClearLine"/>
        /// that counts rather than one that stops at the first thing it meets —
        /// worth doing if one number turns out to be too blunt a control.
        /// </para>
        /// </remarks>
        public const float ShoulderTollSeconds = 120f;

        /// <summary>Whether a finished route shoulders through anything.</summary>
        /// <remarks>
        /// Asked of the route that won rather than carried through the search,
        /// because it is wanted once and the answer is a handful of legs. What
        /// it drives is the log line and the flag the movement system reads,
        /// both of which are about the march that is actually going to happen.
        /// </remarks>
        private static bool Presses(BattleState battle, UnitInstance unit, Route route)
        {
            for (int i = 1; i < route.Places.Count; i++)
            {
                if (!Marching.IsClearLine(
                        battle, unit, route.Places[i - 1], route.Places[i], route.Fronts[i],
                        leaving: true, leavingGrazeOnly: i > 1))
                    return true;
            }

            return false;
        }

        private static Plan Straight(Vec2 from, Vec2 to)
        {
            float length = Vec2.Distance(from, to);

            return new Plan(
                PathResult.Success(new[] { from, to }, Array.Empty<Coord>(), length, length, 0),
                null, false);
        }

        // ---- Where a route may bend ------------------------------------------

        /// <summary>
        /// Every place worth turning at: the two ends, and the corners and faces
        /// of what stands in the way.
        /// </summary>
        /// <remarks>
        /// <para>
        /// For each body, its rectangle grown by the mover's own reach, taken in
        /// <b>the body's</b> axes. Four corners, and the points where the mover's
        /// start and finish project onto each of the four faces.
        /// </para>
        /// <para>
        /// The face projections are the part the corner walk never had, and they
        /// are what finding 19 needed: a destination sitting close beside a body
        /// can only be approached <i>along</i> that body's face, and no corner is
        /// on that line. Generation is permissive on purpose; every candidate is
        /// then made to prove itself against a swept rectangle, so a loose
        /// generator costs work and never correctness.
        /// </para>
        /// </remarks>
        private static List<Vec2> Places(BattleState battle, UnitInstance unit, Vec2 destination)
        {
            var places = new List<Vec2> { unit.Position, destination };

            Vec2 line = destination - unit.Position;
            float length = line.Length;
            Vec2 along = length > 0f ? line / length : new Vec2(1f, 0f);

            // Both presentations, because a place is only a place for the front
            // it is stood on. The narrow half is what threads a gap side-on; the
            // wide half is what a regiment needs if it arrives face-on, and
            // generating only the narrow ring left every face-on approach with
            // nowhere it could legally stand — so either the search found no
            // route at all and shouldered through, or it took a place it did not
            // fit in and the route clipped what it went round.
            foreach (UnitInstance other in Nearby(battle, unit, destination, length))
            {
                if (places.Count >= MostPlaces) break;

                Vec2 ahead = other.Facing.ToVector();
                Vec2 beside = other.Facing.RightVector();

                foreach (float reach in new[]
                         {
                             unit.Footprint.HalfDepth + MarginMetres,
                             unit.Footprint.HalfWidth + MarginMetres,
                         })
                {
                    float deep = other.Footprint.HalfDepth + reach;
                    float wide = other.Footprint.HalfWidth + reach;

                    for (int i = -1; i <= 1; i += 2)
                    for (int j = -1; j <= 1; j += 2)
                        Consider(places, other.Position + ahead * (deep * i) + beside * (wide * j));

                    // Where the ends of the march come onto each face. These are
                    // the "walk in along the body's side" points.
                    foreach (Vec2 end in new[] { unit.Position, destination })
                    {
                        Vec2 offset = end - other.Position;

                        float onAhead = Vec2.Dot(offset, ahead);
                        float onBeside = Vec2.Dot(offset, beside);

                        for (int s = -1; s <= 1; s += 2)
                        {
                            Consider(places, other.Position + ahead * (deep * s) + beside * Clamp(onBeside, wide));
                            Consider(places, other.Position + beside * (wide * s) + ahead * Clamp(onAhead, deep));
                        }
                    }
                }
            }

            return places;
        }

        /// <summary>
        /// Adds a place unless one is already standing there.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The generator repeats itself heavily and by construction: a face
        /// projection whose clamp saturates lands on the corner the corner loop
        /// already produced, and both ends of the march project to the same
        /// point whenever they sit the same side of a body. Measured on four
        /// bodies in a line, twenty-six places held about ten distinct ones.
        /// </para>
        /// <para>
        /// That was not merely wasted work. <see cref="MostPlaces"/> is a hard
        /// cap, so the duplicates <b>crowded genuine candidates out</b>: in the
        /// same arrangement the two outer bodies contributed nothing at all and
        /// the search planned as though they were not on the field. A route that
        /// ignores half the obstacles is the "went somewhere far on the fields"
        /// report, and no amount of better costing fixes it.
        /// </para>
        /// <para>
        /// One metre, matching the separation the search already demands between
        /// the ends of a leg, so a place too near another to be a distinct leg is
        /// too near to be a distinct place.
        /// </para>
        /// </remarks>
        private static void Consider(List<Vec2> places, Vec2 place)
        {
            if (places.Count >= MostPlaces) return;

            foreach (Vec2 already in places)
            {
                if (Vec2.Distance(already, place) < ApartEnoughMetres) return;
            }

            places.Add(place);
        }

        /// <summary>How far apart two candidate places must be to count as two.</summary>
        private const float ApartEnoughMetres = 1f;

        private static float Clamp(float value, float limit) =>
            value < -limit ? -limit : value > limit ? limit : value;

        /// <summary>
        /// Ours, near enough this march to matter, nearest the line first so the
        /// cap falls on the bodies least likely to change the answer.
        /// </summary>
        private static List<UnitInstance> Nearby(
            BattleState battle, UnitInstance unit, Vec2 destination, float length)
        {
            Vec2 along = length > 0f ? (destination - unit.Position) / length : new Vec2(1f, 0f);

            var found = new List<UnitInstance>();
            var away = new List<float>();

            foreach (UnitInstance other in battle.UnitsOnField())
            {
                if (ReferenceEquals(other, unit)) continue;
                if (other.Owner != unit.Owner) continue;
                if (!other.IsFighting) continue;

                Vec2 offset = other.Position - unit.Position;
                float ahead = MathF.Max(0f, MathF.Min(length, Vec2.Dot(offset, along)));
                float off = Vec2.Distance(other.Position, unit.Position + along * ahead);

                // Only what a route might actually meet. Everything within a
                // corridor of the drawn line the width of both bodies, and
                // nothing further: a regiment two hundred metres to the flank
                // changes no answer and costs a place, three fronts and a sweep
                // per place already in the graph.
                if (off > other.Footprint.BoundingRadius + unit.Footprint.BoundingRadius + MarginMetres * 2f)
                    continue;

                found.Add(other);
                away.Add(off);
            }

            var order = new int[found.Count];
            for (int i = 0; i < order.Length; i++) order[i] = i;

            // Ascending id breaks ties, so two bodies equally off the line cannot
            // order themselves by whatever the field happened to return (M0).
            Array.Sort(order, (a, b) =>
            {
                int byDistance = away[a].CompareTo(away[b]);
                return byDistance != 0 ? byDistance : found[a].Id.Value.CompareTo(found[b].Id.Value);
            });

            var sorted = new List<UnitInstance>(found.Count);
            foreach (int i in order) sorted.Add(found[i]);

            return sorted;
        }

        // ---- What a leg costs -------------------------------------------------

        /// <summary>
        /// Seconds to walk one leg, arriving from a given front, and the front it
        /// ends on. Negative seconds mean the leg cannot be walked.
        /// </summary>
        private static float Seconds(
            BattleState battle, UnitInstance unit, Vec2 from, Vec2 to, Facing arrivingOn,
            Facing walkOn, bool inside) =>
            Seconds(
                Vec2.Distance(from, to),
                Marching.AlongTheLine(from, to, walkOn),
                MathF.Max(0.1f, battle.SpeedOf(unit)),
                MathF.Max(1f, unit.Def.Get(UnitAttributes.TurnRate)),
                arrivingOn, walkOn, inside);

        /// <summary>
        /// The same leg, priced by a caller that already knows the four things
        /// the convenience overload works out for itself.
        /// </summary>
        /// <remarks>
        /// <para>
        /// None of the four depends on the leg being priced. The pace is a
        /// terrain sample under the regiment's own feet and the turn rate is a
        /// look-up in its definition — both fixed for the whole plan — while the
        /// length and the bearing are properties of two places the search has
        /// already measured and cached.
        /// </para>
        /// <para>
        /// Measured on a hundred and sixty regiments planning at once: this was
        /// asked <b>2,827,980 times</b> in one pass, so the plan was taking that
        /// many terrain samples, that many attribute look-ups, and that many
        /// arc-tangents to re-derive numbers it was holding in a local variable.
        /// </para>
        /// </remarks>
        private static float Seconds(
            float length, Facing bearing, float pace, float turnRate,
            Facing arrivingOn, Facing walkOn, bool inside)
        {
            float toTurn = Degrees(arrivingOn, walkOn);
            float settled = Degrees(walkOn, bearing);

            float seconds = 0f;

            if (toTurn > WalkingCapDegrees)
            {
                // Above the cap it turns rather than sliding. Dead time, and the
                // halted pivot bonus is why "wheel first" is worth ordering.
                seconds += toTurn / (turnRate * MovementSystem.PivotBonusWhileHalted);
                toTurn = 0f;
            }

            float onceRound = pace * MovementSystem.AlignmentPenalty(settled);

            if (inside) onceRound *= MovementSystem.PaceWhileInsideItsOwn;
            if (onceRound <= 0f) return -1f;

            if (toTurn > 0f)
            {
                float whileComingRound =
                    pace * MovementSystem.AlignmentPenalty((Degrees(arrivingOn, bearing) + settled) * 0.5f);

                if (inside) whileComingRound *= MovementSystem.PaceWhileInsideItsOwn;
                if (whileComingRound <= 0f) return -1f;

                float covered = MathF.Min(length, toTurn / turnRate * whileComingRound);

                return seconds + covered / whileComingRound + (length - covered) / onceRound;
            }

            return seconds + length / onceRound;
        }

        private static float Degrees(Facing from, Facing to) =>
            Facing.AbsoluteDelta(from, to) * 180f / MathF.PI;

        /// <summary>
        /// Whether the regiment could stand at a place, on a given front,
        /// without sharing ground with one of its own.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The constraint the search was missing entirely. Every check it made
        /// was about a <i>leg</i> — can this line be walked — and none about a
        /// <i>place</i>, so it would happily route through a waypoint the
        /// regiment could not occupy, then let the leg out of it pass because
        /// leaving-grace excuses a body you are already touching.
        /// </para>
        /// <para>
        /// Measured: the "wall, 30 m gap" arrangement bent the route through
        /// (976, 569) on a front of 35°, which puts a corner of a forty by
        /// twenty body inside the regiment standing at (1000, 575). The leg out
        /// of it was walked away from that body and so looked clean by itself.
        /// The place was never the leg's fault and no amount of leg checking
        /// finds it.
        /// </para>
        /// <para>
        /// Only asked of places the generator invented. The regiment's own
        /// starting ground is exempt because it is where it actually is —
        /// standing in a formed line laps your neighbours by definition
        /// (<b>M2</b>), and getting clear of that is the steering's business
        /// (<b>M25</b>). The destination is exempt because the placement search
        /// has already chosen ground that fits.
        /// </para>
        /// </remarks>
        [ThreadStatic]
        private static List<UnitInstance>? _standingNear;

        [ThreadStatic]
        private static List<UnitInstance>? _turningNear;

        private static bool CanStandHere(BattleState battle, UnitInstance unit, Vec2 at, Facing front)
        {
            var body = new OrientedRect(at, front, unit.Footprint);
            float reach = unit.Footprint.BoundingRadius;

            List<UnitInstance> all = _standingNear ??= new List<UnitInstance>(32);
            battle.WhereEverybodyIs.Near(battle.AllUnits, at, reach, all);

            for (int u = 0; u < all.Count; u++)
            {
                UnitInstance other = all[u];
                if (ReferenceEquals(other, unit)) continue;
                if (other.Owner != unit.Owner) continue;
                if (!other.IsFighting) continue;

                // Called for both ends of every leg tried, so this is asked far
                // more than once per plan — a body too far off to possibly
                // overlap costs one subtraction and a compare instead of a
                // polygon clip against every regiment on the field.
                float span = reach + other.Footprint.BoundingRadius;
                if (Vec2.DistanceSquared(other.Position, at) > span * span)
                    continue;

                if (OrientedRect.OverlapFraction(body, other.Shape) > OrderSystem.GrazingTolerance)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Whether a regiment could turn where it stands without lapping one of
        /// its own.
        /// </summary>
        /// <remarks>
        /// A rectangle turning about its centre sweeps its circumscribed circle,
        /// which for a 40 by 20 body reaches 22.4 m where the standing shape
        /// reaches 20. That difference is the "rotates and collides a bit"
        /// report. Conservative on purpose: a circle refuses some turns a swept
        /// rectangle would allow, and refusing a legal turn costs a detour while
        /// permitting an illegal one costs a collision.
        /// </remarks>
        private static bool CanTurnHere(BattleState battle, UnitInstance unit, Vec2 at)
        {
            float reach = unit.Footprint.BoundingRadius;

            List<UnitInstance> all = _turningNear ??= new List<UnitInstance>(32);
            battle.WhereEverybodyIs.Near(battle.AllUnits, at, reach, all);

            for (int u = 0; u < all.Count; u++)
            {
                UnitInstance other = all[u];
                if (ReferenceEquals(other, unit)) continue;
                if (other.Owner != unit.Owner) continue;
                if (!other.IsFighting) continue;

                if (Vec2.Distance(other.Shape.ClosestPointTo(at), at) < reach) return false;
            }

            return true;
        }

        // ---- The search -------------------------------------------------------

        private readonly struct Route
        {
            public Route(List<Vec2> places, List<Facing> fronts, float seconds)
            {
                Places = places;
                Fronts = fronts;
                Seconds = seconds;
            }

            public readonly List<Vec2> Places;
            public readonly List<Facing> Fronts;
            public readonly float Seconds;
        }

        /// <summary>
        /// Drops every waypoint the regiment can simply see past.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The search bends at candidate places, and a candidate place is a
        /// corner of something — so a route that had to get round one body
        /// comes back threaded through every corner it considered on the way,
        /// whether or not it still needs them once the whole line is known.
        /// Reported from a play-test as the plain thing it is: <i>"it uses too
        /// many points in the route when less points would have been more
        /// optimal"</i>.
        /// </para>
        /// <para>
        /// Two halves, and the split matters. String-pulling reaches as far
        /// along the route as one leg can honestly carry, asking only whether a
        /// leg is possible — <b>not</b> which front to walk it on. Then the
        /// shortened line is priced as a whole, its fronts chosen together.
        /// </para>
        /// <para>
        /// Written the obvious way first, choosing each leg's cheapest front as
        /// it went, and it lost to the route it was shortening: six waypoints
        /// at 64.8 s became four at 106.5 s. A front that is cheap to walk can
        /// be dear to leave, so the fronts are not independent and picking them
        /// one at a time is simply wrong. Over a line rather than a graph the
        /// exact answer is a short dynamic program, so there is no reason to
        /// approximate it.
        /// </para>
        /// <para>
        /// Every kept leg is put through exactly the checks the search itself
        /// used, and the result is taken only if it is genuinely cheaper in
        /// seconds — a straight line is never longer in metres, but the wheel
        /// it forces can cost more than the two legs it replaced, and the clock
        /// is what a plan is priced in (<b>M31</b>).
        /// </para>
        /// </remarks>
        private static Route Smooth(BattleState battle, UnitInstance unit, Route route, Facing arriveOn)
        {
            if (route.Places.Count <= 2) return route;

            int last = route.Places.Count - 1;

            var places = new List<Vec2> { route.Places[0] };

            int at = 0;

            while (at < last)
            {
                int next = at + 1;

                // Furthest first: the point of this is to skip as much as one
                // leg can carry.
                for (int j = last; j > at + 1; j--)
                {
                    if (FrontsFor(battle, unit, route.Places[at], route.Places[j],
                            leavingItsOwn: at == 0, arriving: j == last).Count == 0)
                        continue;

                    next = j;
                    break;
                }

                places.Add(route.Places[next]);
                at = next;
            }

            if (places.Count >= route.Places.Count) return route;

            if (!CheapestFronts(battle, unit, places, arriveOn, out List<Facing> fronts, out float seconds))
                return route;

            return seconds < route.Seconds ? new Route(places, fronts, seconds) : route;
        }

        /// <summary>Every front that can carry the regiment along one straight leg.</summary>
        private static List<Facing> FrontsFor(
            BattleState battle, UnitInstance unit, Vec2 from, Vec2 to, bool leavingItsOwn, bool arriving)
        {
            Facing bearing = Marching.AlongTheLine(from, to, unit.Facing);
            var fronts = new List<Facing>(3);

            foreach (Facing front in new[]
                     {
                         bearing,
                         Facing.FromRadians(bearing.Radians + MathF.PI * 0.5f),
                         Facing.FromRadians(bearing.Radians - MathF.PI * 0.5f),
                     })
            {
                if (!leavingItsOwn && !CanStandHere(battle, unit, from, front)) continue;
                if (!arriving && !CanStandHere(battle, unit, to, front)) continue;

                if (!Marching.IsClearLine(battle, unit, from, to, front,
                        leaving: true, leavingGrazeOnly: !leavingItsOwn))
                    continue;

                fronts.Add(front);
            }

            return fronts;
        }

        /// <summary>
        /// The cheapest way to walk a fixed line of places, choosing every front
        /// together, and the seconds it comes to.
        /// </summary>
        /// <remarks>
        /// A leg costs what it costs given both the front it is walked on and
        /// the front it was entered on, so the fronts down a route are not
        /// independent. Over a line of places rather than a graph that is a
        /// short dynamic program: at most three fronts to a leg, each looking
        /// back only at the three before it.
        /// </remarks>
        private static bool CheapestFronts(
            BattleState battle, UnitInstance unit, List<Vec2> places, Facing arriveOn,
            out List<Facing> chosen, out float seconds)
        {
            chosen = new List<Facing>();
            seconds = float.MaxValue;

            int legs = places.Count - 1;
            var options = new List<Facing>[legs];

            for (int i = 0; i < legs; i++)
            {
                options[i] = FrontsFor(battle, unit, places[i], places[i + 1], i == 0, i == legs - 1);
                if (options[i].Count == 0) return false;
            }

            var cost = new float[legs][];
            var cameFrom = new int[legs][];

            for (int i = 0; i < legs; i++)
            {
                cost[i] = new float[options[i].Count];
                cameFrom[i] = new int[options[i].Count];
            }

            for (int f = 0; f < options[0].Count; f++)
            {
                cost[0][f] = StepCost(battle, unit, places[0], places[1], unit.Facing, options[0][f]);
                cameFrom[0][f] = -1;

                if (cost[0][f] < 0f) cost[0][f] = float.MaxValue;
            }

            for (int i = 1; i < legs; i++)
            for (int f = 0; f < options[i].Count; f++)
            {
                cost[i][f] = float.MaxValue;
                cameFrom[i][f] = -1;

                for (int g = 0; g < options[i - 1].Count; g++)
                {
                    if (cost[i - 1][g] >= float.MaxValue) continue;

                    float step = StepCost(
                        battle, unit, places[i], places[i + 1], options[i - 1][g], options[i][f]);

                    if (step < 0f) continue;

                    float total = cost[i - 1][g] + step;
                    if (total >= cost[i][f]) continue;

                    cost[i][f] = total;
                    cameFrom[i][f] = g;
                }
            }

            float turnRate = MathF.Max(1f, unit.Def.Get(UnitAttributes.TurnRate));
            int best = -1;

            for (int f = 0; f < options[legs - 1].Count; f++)
            {
                if (cost[legs - 1][f] >= float.MaxValue) continue;

                // The wheel onto the ordered front, counted once at the end,
                // exactly as the search counts it on arrival.
                float total = cost[legs - 1][f]
                              + Degrees(options[legs - 1][f], arriveOn)
                                / (turnRate * MovementSystem.PivotBonusWhileHalted);

                if (total >= seconds) continue;

                seconds = total;
                best = f;
            }

            if (best < 0) return false;

            var backwards = new List<Facing>();

            for (int i = legs - 1; i >= 0; i--)
            {
                backwards.Add(options[i][best]);
                best = cameFrom[i][best];
            }

            backwards.Reverse();

            // A front is recorded against the waypoint its leg ends at, which is
            // the convention Route and MovementRoute both already read.
            chosen.Add(unit.Facing);
            chosen.AddRange(backwards);

            return true;
        }

        /// <summary>One leg's cost, or negative if it cannot be walked that way.</summary>
        private static float StepCost(
            BattleState battle, UnitInstance unit, Vec2 from, Vec2 to, Facing arrivingOn, Facing walkOn)
        {
            // Turning above the cap means turning on the spot, and there has to
            // be room for it where it stands.
            if (Degrees(arrivingOn, walkOn) > WalkingCapDegrees && !CanTurnHere(battle, unit, from))
                return -1f;

            return Seconds(battle, unit, from, to, arrivingOn, walkOn, inside: false);
        }

        private static Plan Assemble(Route route, bool pressed, RouteEffort effort = default)
        {
            float walked = 0f;

            for (int i = 1; i < route.Places.Count; i++)
                walked += Vec2.Distance(route.Places[i - 1], route.Places[i]);

            // The front each leg is walked on, on the leg that ends at that
            // waypoint, which is the convention MovementRoute already reads.
            var hold = new Facing?[route.Places.Count];

            for (int i = 1; i < route.Places.Count; i++)
            {
                Facing walkOn = route.Fronts[i];
                Facing bearing = Marching.AlongTheLine(route.Places[i - 1], route.Places[i], walkOn);

                hold[i] = Facing.AbsoluteDelta(walkOn, bearing) < 0.01f ? (Facing?)null : walkOn;
            }

            return new Plan(
                PathResult.Success(route.Places.ToArray(), Array.Empty<Coord>(), walked, walked, 0),
                hold, pressed, effort);
        }

        /// <summary>
        /// Dijkstra over states of (place, front), returning the cheapest route
        /// or nothing.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>A leg is asked about once, not once per state standing at its near
        /// end.</b> The front a leg is walked on falls out of the two places and
        /// which of the three presentations is being tried. It does not depend on
        /// the front the regiment arrived at the near place on, because the
        /// bearing between two points is a property of the points. So the three
        /// dear questions about a leg (can it stand at either end on that front,
        /// is the line clear) have the same answer for every state sharing that
        /// place, and there are many of those: measured, twenty places carried
        /// nearly three hundred states.
        /// </para>
        /// <para>
        /// Measured on one plan before this: 22,941 standing checks and 10,310
        /// swept-rectangle line checks against 1,200 distinct legs. Nine tenths
        /// of the geometry was the same question asked again.
        /// </para>
        /// </remarks>
        /// <summary>
        /// What has already been worked out about a leg, kept across both
        /// searches over the same places.
        /// </summary>
        private sealed class LegCache
        {
            public LegCache(int places)
            {
                Asked = new bool[places * places * 3];
                Front = new Facing[Asked.Length];
                StandNear = new bool[Asked.Length];
                StandFar = new bool[Asked.Length];
                Clear = new bool[Asked.Length];

                // Room to turn is a property of the ground, so it is asked once
                // for each place. Nought means not yet asked.
                RoomToTurn = new sbyte[places];
            }

            public readonly bool[] Asked;
            public readonly Facing[] Front;
            public readonly bool[] StandNear;
            public readonly bool[] StandFar;
            public readonly bool[] Clear;
            public readonly sbyte[] RoomToTurn;
        }

        private static Route? Cheapest(
            BattleState battle, UnitInstance unit, List<Vec2> places, Vec2 destination,
            Facing arriveOn, LegCache known, ref int legsPriced, ref int expanded)
        {
            int count = places.Count;

            // A local tally rather than the ref parameter, which a local
            // function may not touch. Added to the caller's on the way out.
            int priced = 0;

            // A state is a place reached on a front. Fronts are never sampled
            // round the circle: they are the ones some leg actually delivers.
            var stateAt = new List<int>();
            var stateOn = new List<Facing>();
            var best = new List<float>();
            var cameFrom = new List<int>();
            var settled = new List<bool>();

            bool[] asked = known.Asked;
            Facing[] legFront = known.Front;
            bool[] legStandNear = known.StandNear;
            bool[] legStandFar = known.StandFar;
            bool[] legClear = known.Clear;
            sbyte[] roomToTurn = known.RoomToTurn;

            // Finding a state was a walk down the whole list, which came to three
            // quarters of a million list elements on one plan.
            var index = new Dictionary<long, int>();

            stateAt.Add(0);
            stateOn.Add(unit.Facing);
            best.Add(0f);
            cameFrom.Add(-1);
            settled.Add(false);
            index[Key(0, unit.Facing)] = 0;

            int arrivedFrom = -1;
            Facing arrivedOn = unit.Facing;
            float arrivedCost = float.MaxValue;

            // What is left to walk, at best, from each place — the shortest a
            // march from there could possibly take.
            //
            // This turns the search from Dijkstra into A*, and it is honest
            // rather than a fudge: Seconds prices every leg at this same pace
            // reduced by an alignment penalty of at most one and by the pace
            // inside its own, both of which only ever make a leg slower. So the
            // straight line at full pace is a bound no route can beat, the
            // estimate never overshoots, and the cheapest route found is still
            // the cheapest route there is.
            float pace = MathF.Max(0.1f, battle.SpeedOf(unit));
            float turnRate = MathF.Max(1f, unit.Def.Get(UnitAttributes.TurnRate));
            var leftToWalk = new float[count];

            for (int i = 0; i < count; i++)
                leftToWalk[i] = Vec2.Distance(places[i], destination) / pace;

            var frontier = new List<(float Reckoned, int State)>();
            void Offer(int state, float total) => Push(frontier, (total + leftToWalk[stateAt[state]], state));

            Offer(0, 0f);

            while (true)
            {
                int at = -1;
                float nearest = float.MaxValue;

                while (frontier.Count > 0)
                {
                    (float Reckoned, int State) top = Pop(frontier);

                    if (settled[top.State]) continue;
                    if (top.Reckoned > best[top.State] + leftToWalk[stateAt[top.State]] + 1e-4f) continue;

                    at = top.State;
                    nearest = top.Reckoned;
                    break;
                }

                if (at < 0 || nearest >= arrivedCost) break;

                settled[at] = true;
                expanded++;

                int from = stateAt[at];
                Vec2 here = places[from];
                Facing on = stateOn[at];

                void Ask(int leg)
                {
                    if (asked[leg]) return;

                    int k = leg % 3;
                    int a = leg / 3 / count;
                    int b = leg / 3 % count;

                    Vec2 one = places[a];
                    Vec2 other = places[b];

                    // unit.Facing only stands in for a leg of no length, and
                    // those are skipped below, so this bearing and everything
                    // drawn from it is a property of the two places alone.
                    Facing bearing = Marching.AlongTheLine(one, other, unit.Facing);

                    Facing walking =
                        k == 0 ? bearing
                        : k == 1 ? Facing.FromRadians(bearing.Radians + MathF.PI * 0.5f)
                        : Facing.FromRadians(bearing.Radians - MathF.PI * 0.5f);

                    legFront[leg] = walking;
                    legStandNear[leg] = CanStandHere(battle, unit, one, walking);
                    legStandFar[leg] = CanStandHere(battle, unit, other, walking);
                    legClear[leg] = Marching.IsClearLine(
                        battle, unit, one, other, walking, leaving: true, leavingGrazeOnly: true);

                    asked[leg] = true;
                    priced++;
                }

                for (int to = 0; to < count; to++)
                {
                    if (to == from) continue;

                    Vec2 there = places[to];

                    float apart = Vec2.Distance(here, there);
                    if (apart < ApartEnoughMetres) continue;

                    // The least this leg, and everything after it, could
                    // possibly cost — asked before any geometry, because
                    // geometry is the whole bill and this is one subtraction
                    // and a square root.
                    //
                    // Same bound as the frontier's, for the same reason it is
                    // sound: no leg beats its own straight line at full pace.
                    // So a leg that cannot better the best arrival already
                    // found cannot be on the answer, and never needs a swept
                    // rectangle run against it.
                    //
                    // Measured: the search pops only forty-one states across a
                    // whole army's orders and still costed 1,863 legs, because
                    // every pop priced every place it could see. This is what
                    // makes a focused search actually cheap rather than merely
                    // well-aimed.
                    if (best[at] + apart / pace + leftToWalk[to] >= arrivedCost) continue;

                    // Face-on, or side-on either way. Three fronts, because those
                    // are the three a body of men actually walks a line on: front
                    // first, or presenting a flank to thread something narrow.
                    int first = (from * count + to) * 3;

                    Ask(first);

                    // Face-on serves, so the flanks are not asked about.
                    //
                    // The two side-on presentations exist for one purpose: to
                    // put the regiment's narrow dimension across a gap its front
                    // will not fit through. Where the front fits, they are not a
                    // cheaper way of doing the same thing — walking sideways
                    // costs the alignment penalty every metre — they are only
                    // two more fronts to reach the same place on, and each one
                    // is a state, three geometry questions, and every leg
                    // leading out of it.
                    //
                    // Measured before this: 1,964 candidate places carrying
                    // 9,018 states, four and a half fronts to a place, on fields
                    // where the overwhelming majority of legs run across open
                    // ground. This is the width-or-length question asked of the
                    // leg, where it is a property of two points and some
                    // geometry, instead of asked of the search, where it is a
                    // dimension.
                    int presentations =
                        legClear[first] && legStandNear[first] && legStandFar[first] ? 1 : 3;

                    for (int k = 0; k < presentations; k++)
                    {
                        int leg = first + k;

                        Ask(leg);

                        Facing walkOn = legFront[leg];

                        // A place the generator invented is only a place if the
                        // regiment could stand in it, at both ends and on this
                        // leg's own front, since it comes round where it stands
                        // before it sets off (M23). The regiment's real ground and
                        // the ordered destination are exempt: one is where it
                        // actually is, and the placement search already chose the
                        // other.
                        if (at != 0 && !legStandNear[leg]) continue;
                        if (to != 1 && !legStandFar[leg]) continue;

                        // The one state standing where the regiment really is gets
                        // M25 grace in full rather than the graze: a formed line
                        // laps its neighbours by definition, and getting clear of
                        // that is the steering's business.
                        bool clear = at == 0
                            ? Marching.IsClearLine(battle, unit, here, there, walkOn, leaving: true)
                            : legClear[leg];

                        // Not a gate any more. A leg that presses is one the
                        // search may take; what stops it taking one lightly is
                        // that it costs.
                        float toll = clear ? 0f : ShoulderTollSeconds;

                        // Turning above the cap means turning on the spot, and
                        // there has to be room for it where it stands.
                        if (Degrees(on, walkOn) > WalkingCapDegrees)
                        {
                            if (roomToTurn[from] == 0)
                                roomToTurn[from] = (sbyte)(CanTurnHere(battle, unit, here) ? 1 : -1);

                            if (roomToTurn[from] < 0) continue;
                        }

                        float seconds = Seconds(
                            apart, legFront[first], pace, turnRate, on, walkOn, inside: !clear);
                        if (seconds < 0f) continue;

                        float total = best[at] + seconds + toll;

                        if (to == 1)
                        {
                            // The last thing a march does is come onto the front
                            // it was ordered to arrive on. Counting it here is
                            // what stops a route that arrives conveniently but
                            // pointing the wrong way from winning on paper.
                            total += Degrees(walkOn, arriveOn)
                                     / (turnRate * MovementSystem.PivotBonusWhileHalted);

                            // Recorded rather than added as a state. Added, the
                            // goal is expandable, so the search walks out of the
                            // destination and back in, growing the graph as it
                            // goes.
                            if (total < arrivedCost)
                            {
                                arrivedCost = total;
                                arrivedFrom = at;
                                arrivedOn = walkOn;
                            }

                            continue;
                        }

                        long key = Key(to, walkOn);

                        if (!index.TryGetValue(key, out int state))
                        {
                            state = stateAt.Count;
                            index[key] = state;

                            stateAt.Add(to);
                            stateOn.Add(walkOn);
                            best.Add(total);
                            cameFrom.Add(at);
                            settled.Add(false);

                            Offer(state, total);
                        }
                        else if (total < best[state] && !settled[state])
                        {
                            best[state] = total;
                            cameFrom[state] = at;

                            Offer(state, total);
                        }
                    }
                }
            }

            legsPriced += priced;

            if (arrivedFrom < 0) return null;

            var backwards = new List<Vec2> { destination };
            var fronts = new List<Facing> { arrivedOn };

            for (int state = arrivedFrom; state >= 0; state = cameFrom[state])
            {
                backwards.Add(places[stateAt[state]]);
                fronts.Add(stateOn[state]);
            }

            backwards.Reverse();
            fronts.Reverse();

            return backwards.Count < 2 ? null : new Route(backwards, fronts, arrivedCost);
        }

        private static void Push(List<(float Reckoned, int State)> heap, (float Reckoned, int State) item)
        {
            heap.Add(item);
            int i = heap.Count - 1;

            while (i > 0)
            {
                int parent = (i - 1) / 2;
                if (!Before(heap[i], heap[parent])) break;
                (heap[i], heap[parent]) = (heap[parent], heap[i]);
                i = parent;
            }
        }

        private static (float Reckoned, int State) Pop(List<(float Reckoned, int State)> heap)
        {
            (float Reckoned, int State) top = heap[0];
            int last = heap.Count - 1;
            heap[0] = heap[last];
            heap.RemoveAt(last);

            int i = 0;
            while (true)
            {
                int left = i * 2 + 1;
                if (left >= heap.Count) break;

                int right = left + 1;
                int best = right < heap.Count && Before(heap[right], heap[left]) ? right : left;

                if (!Before(heap[best], heap[i])) break;

                (heap[i], heap[best]) = (heap[best], heap[i]);
                i = best;
            }

            return top;
        }

        private static bool Before((float Reckoned, int State) a, (float Reckoned, int State) b) =>
            a.Reckoned != b.Reckoned ? a.Reckoned < b.Reckoned : a.State < b.State;

        /// <summary>How many steps the circle is cut into when naming a front.</summary>
        /// <remarks>
        /// Finer than the hundredth of a radian the old walk-the-list match
        /// allowed, so this can only ever keep two fronts apart that used to
        /// merge, never merge two it kept apart. A fifth of a degree is far below
        /// anything a regiment can be said to hold.
        /// </remarks>
        private const int FrontSteps = 2048;

        /// <summary>A state's name: the ground it stands on, and its front.</summary>
        private static long Key(int place, Facing front)
        {
            int step = (int)MathF.Round(front.Radians / (MathF.PI * 2f) * FrontSteps) & (FrontSteps - 1);

            return ((long)place << 16) | (uint)step;
        }
    }
}
