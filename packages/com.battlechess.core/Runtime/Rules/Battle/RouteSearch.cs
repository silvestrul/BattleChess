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

            // Clean first, shouldering through only if nothing else reaches.
            //
            // Pricing a press-through as one edge among many is the goal and is
            // deliberately *not* done yet: measured, at M20's pace of 0.6 it wins
            // almost everywhere, which is the same result the M26 experiment gave
            // twice. That is a designer's number, not a planner's, so until it is
            // settled the last resort stays a last resort — which is what M18
            // always said and what this rung of it got right.
            Route? clean = Cheapest(battle, unit, places, destination, arriveOn, mayPress: false);

            if (clean.HasValue) return Assemble(clean.Value, pressed: false);

            Route? forced = Cheapest(battle, unit, places, destination, arriveOn, mayPress: true);

            return forced.HasValue
                ? Assemble(forced.Value, pressed: true)
                : new Plan(
                    PathResult.Failed(PathFailure.NoRouteExists,
                        "nothing reaches that ground, going round or through", 0),
                    null, false);
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
            Facing walkOn, bool inside)
        {
            float length = Vec2.Distance(from, to);
            float pace = MathF.Max(0.1f, battle.SpeedOf(unit));
            float turnRate = MathF.Max(1f, unit.Def.Get(UnitAttributes.TurnRate));

            Facing bearing = Marching.AlongTheLine(from, to, walkOn);

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
        private static bool CanStandHere(BattleState battle, UnitInstance unit, Vec2 at, Facing front)
        {
            var body = new OrientedRect(at, front, unit.Footprint);

            foreach (UnitInstance other in battle.UnitsOnField())
            {
                if (ReferenceEquals(other, unit)) continue;
                if (other.Owner != unit.Owner) continue;
                if (!other.IsFighting) continue;

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

            foreach (UnitInstance other in battle.UnitsOnField())
            {
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

        private static Plan Assemble(Route route, bool pressed)
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
                hold, pressed);
        }

        private static Route? Cheapest(
            BattleState battle, UnitInstance unit, List<Vec2> places, Vec2 destination,
            Facing arriveOn, bool mayPress)
        {
            int count = places.Count;

            // A state is a place reached on a front. Fronts are never sampled
            // round the circle: they are the ones some leg actually delivers.
            var stateAt = new List<int>();
            var stateOn = new List<Facing>();
            var best = new List<float>();
            var cameFrom = new List<int>();
            var settled = new List<bool>();

            stateAt.Add(0);
            stateOn.Add(unit.Facing);
            best.Add(0f);
            cameFrom.Add(-1);
            settled.Add(false);

            int arrivedFrom = -1;
            Facing arrivedOn = unit.Facing;
            float arrivedCost = float.MaxValue;

            while (true)
            {
                int at = -1;

                for (int i = 0; i < stateAt.Count; i++)
                    if (!settled[i] && (at < 0 || best[i] < best[at])) at = i;

                if (at < 0 || best[at] >= arrivedCost) break;

                settled[at] = true;

                Vec2 here = places[stateAt[at]];
                Facing on = stateOn[at];

                for (int to = 0; to < count; to++)
                {
                    if (to == stateAt[at]) continue;

                    Vec2 there = places[to];
                    if (Vec2.Distance(here, there) < 1f) continue;

                    Facing bearing = Marching.AlongTheLine(here, there, on);

                    // Face-on, or side-on either way. Three fronts, because those
                    // are the three a body of men actually walks a line on: front
                    // first, or presenting a flank to thread something narrow.
                    foreach (Facing walkOn in new[]
                             {
                                 bearing,
                                 Facing.FromRadians(bearing.Radians + MathF.PI * 0.5f),
                                 Facing.FromRadians(bearing.Radians - MathF.PI * 0.5f),
                             })
                    {
                        // Full leaving grace only where the regiment is truly
                        // standing (state 0, unit.Position on unit.Facing) —
                        // that is M25's case, a formed line the regiment is
                        // already inside. Every other place in this graph is a
                        // candidate the search invented, so a blanket leaving
                        // there excused a body genuinely ahead in the travel
                        // direction just because the candidate already touched
                        // it, and a leg cut straight through a wall on the
                        // strength of that (measured:
                        // EveryLegOfAPlannedRouteIsClearAtTheFrontItIsWalkedOn,
                        // "wall, no gap"). Restricting it outright to state 0
                        // was tried and cost the angle gate more than it
                        // fixed — corner-hugging needs the same grace across
                        // more than one hop beside the same body. Narrowing it
                        // to a graze, using the tolerance the rest of the
                        // codebase already treats as no obstacle at all
                        // (OrderSystem.GrazingTolerance) rather than dropping
                        // it, keeps both: a true graze is still excused on
                        // every hop, a body the leg genuinely walks through is
                        // not.
                        // A place the generator invented is only a place if the
                        // regiment could stand in it. Asked before the leg is
                        // costed, because a waypoint it cannot occupy makes
                        // every question about the legs either side of it moot.
                        //
                        // Both ends, and on this leg's own front. The regiment
                        // comes round onto that front where it stands before it
                        // sets off (M23), so the place it leaves has to hold the
                        // turned rectangle, not the one it arrived in — checking
                        // only the far end left the route bending through a
                        // waypoint it could arrive at side-on and not turn in.
                        if (at != 0 && !CanStandHere(battle, unit, here, walkOn)) continue;
                        if (to != 1 && !CanStandHere(battle, unit, there, walkOn)) continue;

                        bool clear = Marching.IsClearLine(
                            battle, unit, here, there, walkOn, leaving: true, leavingGrazeOnly: at != 0);

                        if (!clear && !mayPress) continue;

                        // Turning above the cap means turning on the spot, and
                        // there has to be room for it where it stands.
                        if (Degrees(on, walkOn) > WalkingCapDegrees && !CanTurnHere(battle, unit, here))
                            continue;

                        float seconds = Seconds(battle, unit, here, there, on, walkOn, inside: !clear);
                        if (seconds < 0f) continue;

                        float total = best[at] + seconds;

                        if (to == 1)
                        {
                            // The last thing a march does is come onto the front
                            // it was ordered to arrive on. Counting it here is
                            // what stops a route that arrives conveniently but
                            // pointing the wrong way from winning on paper.
                            float turnRate = MathF.Max(1f, unit.Def.Get(UnitAttributes.TurnRate));

                            total += Degrees(walkOn, arriveOn)
                                     / (turnRate * MovementSystem.PivotBonusWhileHalted);

                            // Recorded rather than added as a state. Added, the
                            // goal is expandable, so the search walks *out* of
                            // the destination and back in, growing the graph as
                            // it goes. That is the other half of why the suite
                            // stopped finishing.
                            if (total < arrivedCost)
                            {
                                arrivedCost = total;
                                arrivedFrom = at;
                                arrivedOn = walkOn;
                            }

                            continue;
                        }

                        int state = Find(stateAt, stateOn, to, walkOn);

                        if (state < 0)
                        {
                            Remember(stateAt, stateOn, best, cameFrom, settled, to, walkOn, total, at);
                        }
                        else if (total < best[state] && !settled[state])
                        {
                            best[state] = total;
                            cameFrom[state] = at;
                        }
                    }
                }
            }

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

        private static int Find(List<int> stateAt, List<Facing> stateOn, int place, Facing front)
        {
            for (int i = 0; i < stateAt.Count; i++)
                if (stateAt[i] == place && Facing.AbsoluteDelta(stateOn[i], front) < 0.01f)
                    return i;

            return -1;
        }

        private static int Remember(
            List<int> stateAt, List<Facing> stateOn, List<float> best, List<int> cameFrom,
            List<bool> settled, int place, Facing front, float cost, int from)
        {
            stateAt.Add(place);
            stateOn.Add(front);
            best.Add(cost);
            cameFrom.Add(from);
            settled.Add(false);

            return stateAt.Count - 1;
        }
    }
}
