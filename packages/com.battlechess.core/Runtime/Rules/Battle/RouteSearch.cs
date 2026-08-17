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
        /// How many times the search may stop, grow the ground it is allowed to
        /// bend at, and start again.
        /// </summary>
        /// <remarks>
        /// A bound on the work rather than a rule. Each round costs a restart
        /// and buys the rings of every body that refused a leg in the round
        /// before, so the rounds go as deep as the obstacles are layered, and
        /// six layers of regiment between a mover and its destination is a
        /// battle line, not a march.
        /// </remarks>
        private const int MostRounds = 1;

        /// <summary>
        /// Every candidate place the search considered for this march, for a
        /// debug overlay to draw.
        /// </summary>
        /// <remarks>
        /// This runs the search rather than the generator, because since the
        /// generator went lazy there is no such thing as the places a march
        /// <i>would</i> consider — only the ones it was driven to invent. Drawing
        /// anything else would be drawing a reconstruction, and the overlay
        /// exists to show what actually happened (<b>W5</b>).
        /// </remarks>
        public static IReadOnlyList<Vec2> DebugCandidatePlaces(
            BattleState battle, UnitInstance unit, Vec2 destination)
        {
            var ledger = new Ledger();

            List<Vec2> places = Places(battle, unit, destination, ledger);

            Hunt(battle, unit, destination, unit.OrderFacing, places, ledger, out _);

            return places;
        }

        /// <summary>
        /// Plans a march, or returns a plan that was not found.
        /// </summary>
        public static Plan Find(
            BattleState battle, UnitInstance unit, Vec2 destination, Facing arriveOn,
            IBattleLog? log = null, IPathfinder? pathfinder = null)
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

            // M33. The ladder answers in about a millisecond, and when its
            // answer is clean it is a real route, so a march may as well have
            // the cheaper of the two.
            //
            // Recorded 17 Aug, one click: the ladder 82 m and 28 s, the search
            // 153 m and 40 s. The search was optimal over the places it had
            // invented and the ladder's bends were not among them — a true
            // answer to the wrong question. The player wanted the shorter march.
            //
            // <b>Held as a floor, and deliberately not as a ceiling.</b> Feeding
            // this price in as the search's starting bound was built and
            // measured: it is sound, and it costs. The bound is what lets the
            // search stop, so a search told to *beat* a route cannot stop at the
            // first one it finds and keeps buying places to try again — one
            // arrangement went from 10 places and 180 legs to 34 and 1,260, and
            // the whole-army order lost a fifth of its speed. The pruning it was
            // meant to buy never arrived either, because the fields where the
            // search is dear are exactly the fields where the ladder presses
            // through and offers no price at all. So: compare at the end, never
            // during.
            float fromLadderPrice = float.MaxValue;
            IReadOnlyList<Vec2>? fromLadder = null;

            if (pathfinder != null)
            {
                Plan ladder = Marching.ByTheLadder(battle, unit, pathfinder, destination);

                if (ladder.Path.Found && !ladder.PressedThrough)
                {
                    float priced = PriceOf(battle, unit, ladder.Path.Waypoints, arriveOn);

                    if (priced >= 0f)
                    {
                        fromLadderPrice = priced;
                        fromLadder = ladder.Path.Waypoints;
                    }
                }
            }

            var ledger = new Ledger();

            // Every body along the drawn line offers its places at once (M34);
            // anything further out has to earn them by refusing a leg (M32).
            List<Vec2> places = Places(battle, unit, destination, ledger);

            Route? route = Hunt(
                battle, unit, destination, arriveOn, places, ledger, out bool pressed);

            var effort = new RouteEffort(
                places.Count, ledger.LegsPriced, ledger.Expanded, ledger.Rounds,
                ledger.States, ledger.FrontierScans, ledger.CacheHits, ledger.Pruned,
                ledger.LineChecks, ledger.StandChecks, ledger.TurnChecks);

            if (!route.HasValue)
            {
                return fromLadder != null
                    ? Assemble(Straighten(fromLadder), pressed: false, effort)
                    : new Plan(
                        PathResult.Failed(PathFailure.NoRouteExists,
                            "nothing reaches that ground, going round or through", 0),
                        null, false, effort);
            }

            if (pressed)
            {
                // A clean route beats a press-through whatever it costs: one of
                // them is a march and the other is walking through your own men.
                return fromLadder != null
                    ? Assemble(Straighten(fromLadder), pressed: false, effort)
                    : Assemble(route.Value, pressed: true, effort);
            }

            Route found = Smooth(battle, unit, route.Value, arriveOn);

            // Priced the same way as the ladder's, after smoothing, so the two
            // numbers are the same question asked of two answers.
            float foundPrice = PriceOf(battle, unit, found.Places, arriveOn);

            if (fromLadder != null && (foundPrice < 0f || fromLadderPrice < foundPrice))
                return Assemble(Straighten(fromLadder), pressed: false, effort);

            return Assemble(found, pressed: false, effort);
        }

        /// <summary>
        /// Searches, and when the search finds nothing, grows the candidate
        /// places from whatever refused it and searches again.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>M32.</b> Every body near the line used to be quarried for candidate
        /// places before a single leg was tried. Measured on a field of sixteen
        /// regiments with a lane through it that nothing stood in: the forty-eight
        /// places allowed were spent on bodies the march never met, the lane's own
        /// points were never generated, and the search — finding nothing clean in
        /// a graph that could not describe the way through — shouldered four
        /// hundred metres through its own army instead. The cap was not costing
        /// speed. It was costing the answer.
        /// </para>
        /// <para>
        /// So a body earns its places by refusing a leg the search actually wanted
        /// to walk, and by nothing else. Bodies stood to one side of the march are
        /// never asked about, however near the line they sit, because a bend
        /// around something that was not in the way can only ever lengthen a
        /// route — which is the visibility-graph result, and the reason this is
        /// sound rather than merely cheaper.
        /// </para>
        /// <para>
        /// <b>Restarting is close to free, and that is what makes rounds
        /// affordable.</b> Every dear question about a leg is cached in the
        /// <see cref="Ledger"/> under the two places and the presentation, and
        /// places are only ever appended, so their indices never shift. A second
        /// round re-prices nothing the first round already asked; it only reaches
        /// the ground the first round could not see.
        /// </para>
        /// <para>
        /// Each round is a whole search over its own set of places, so each is
        /// correct on its own terms and the last one's answer is the cheapest
        /// route over the places that exist by then. Growing the set <i>during</i>
        /// a search would not be: a place added late can offer a cheaper way to
        /// somewhere already settled, and settled is meant to mean finished.
        /// </para>
        /// </remarks>
        private static Route? Hunt(
            BattleState battle, UnitInstance unit, Vec2 destination, Facing arriveOn,
            List<Vec2> places, Ledger ledger, out bool pressed)
        {
            pressed = false;

            for (int round = 0; round < MostRounds; round++)
            {
                ledger.Rounds++;

                // Clean first, shouldering through only if nothing else reaches.
                //
                // Pricing a press-through as one edge among many is the goal and
                // is deliberately *not* done yet: measured, at M20's pace of 0.6
                // it wins almost everywhere, which is the same result the M26
                // experiment gave twice. That is a designer's number, not a
                // planner's, so until it is settled the last resort stays a last
                // resort — which is what M18 always said and what this rung of it
                // got right.
                Route? clean = Cheapest(
                    battle, unit, places, destination, arriveOn, mayPress: false, ledger);

                if (clean.HasValue) return clean;

                // Only worth buying more ground if another round will use it.
                // Growing on the last pass and then falling through left the
                // press-through search walking an enlarged graph for nothing:
                // measured at 2,868 legs across sixteen regiments against 1,842
                // for the same orders without it.
                if (round + 1 >= MostRounds) break;

                if (!Grow(unit, places, destination, ledger)) break;
            }

            // Nothing goes round it, so the last resort — over every place the
            // rounds managed to find, since those are the best description of the
            // ground this march has.
            pressed = true;

            return Cheapest(battle, unit, places, destination, arriveOn, mayPress: true, ledger);
        }

        /// <summary>
        /// Gives every body that refused a leg its ring of places, and says
        /// whether that added anything new.
        /// </summary>
        private static bool Grow(
            UnitInstance unit, List<Vec2> places, Vec2 destination, Ledger ledger)
        {
            int had = places.Count;

            // In the order the search met them, which is cost order, so when the
            // cap does bite it bites the bodies the march cared about least. The
            // list is built by a deterministic walk, so this is stable (M0).
            foreach (UnitInstance other in ledger.Refused)
            {
                if (!ledger.Grown.Add(other.Id)) continue;

                RingsFor(places, unit, other, destination);
            }

            ledger.Refused.Clear();

            return places.Count > had;
        }

        /// <summary>
        /// Everything a search needs to remember between rounds.
        /// </summary>
        /// <remarks>
        /// Sized by <see cref="MostPlaces"/> rather than by however many places
        /// exist right now, which is the whole trick: a leg's name is fixed the
        /// moment its two places exist, so the answers survive the set growing
        /// under them.
        /// </remarks>
        private sealed class Ledger
        {
            private const int Legs = MostPlaces * MostPlaces * 3;

            public readonly bool[] Asked = new bool[Legs];
            public readonly Facing[] Front = new Facing[Legs];
            public readonly bool[] StandNear = new bool[Legs];
            public readonly bool[] StandFar = new bool[Legs];
            public readonly bool[] Clear = new bool[Legs];

            /// <summary>Nought means not yet asked, as it is ground, not a leg.</summary>
            public readonly sbyte[] RoomToTurn = new sbyte[MostPlaces];

            /// <summary>Who said no this round, in the order they said it.</summary>
            public readonly List<UnitInstance> Refused = new List<UnitInstance>();

            /// <summary>Who has already been given places, so nobody is quarried twice.</summary>
            public readonly HashSet<UnitId> Grown = new HashSet<UnitId>();

            public int LegsPriced;
            public int Expanded;
            public int Rounds;

            // Counted so a bench can say where a march's time went. Nothing
            // here changes a route; all of it is reported through RouteEffort,
            // which travels with the plan rather than sitting in a static.
            public int States;
            public long FrontierScans;
            public int CacheHits;
            public int Pruned;
            public int LineChecks;
            public int StandChecks;
            public int TurnChecks;

            public void Refuse(UnitInstance? blocker)
            {
                // Terrain refuses without being a body, and there is nothing to
                // route around: that is the pathfinder's ground, not this one's.
                if (blocker != null) Refused.Add(blocker);
            }
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
        /// <summary>
        /// The places every body near the drawn line offers, taken before a
        /// single leg is tried.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>M34, and it is a retreat from <see cref="M32">M32</see>'s pure
        /// form.</b> Growing places only from bodies that had refused a leg was
        /// measured against this and lost badly: a whole army ordered at once
        /// went from 14 ms to 115, because each round restarts the search and
        /// the frontier — unlike the legs — is not carried over. The corridor
        /// filter this restores was never the waste it looked like. It is good
        /// pruning, cheap, and it puts the useful bodies in first.
        /// </para>
        /// <para>
        /// What survives of M32 is the part that answered a question this could
        /// not: bodies <i>outside</i> the corridor are still never quarried up
        /// front, and can still earn their places by refusing a leg. So the
        /// first round is as wide as the old generator and the later ones reach
        /// past it, which is the hybrid neither half was on its own.
        /// </para>
        /// </remarks>
        private static List<Vec2> Places(BattleState battle, UnitInstance unit, Vec2 destination, Ledger ledger)
        {
            var places = new List<Vec2> { unit.Position, destination };

            float length = Vec2.Distance(unit.Position, destination);

            foreach (UnitInstance other in Nearby(battle, unit, destination, length))
            {
                if (places.Count >= MostPlaces) break;

                // Marked as quarried, so a later round does not spend a second
                // pass rediscovering the ring it already has.
                ledger.Grown.Add(other.Id);

                RingsFor(places, unit, other, destination);
            }

            return places;
        }

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

        private static void RingsFor(
            List<Vec2> places, UnitInstance unit, UnitInstance other, Vec2 destination)
        {
            if (places.Count >= MostPlaces) return;

            Vec2 ahead = other.Facing.ToVector();
            Vec2 beside = other.Facing.RightVector();

            // Both presentations, because a place is only a place for the front
            // it is stood on. The narrow half is what threads a gap side-on; the
            // wide half is what a regiment needs if it arrives face-on, and
            // generating only the narrow ring left every face-on approach with
            // nowhere it could legally stand — so either the search found no
            // route at all and shouldered through, or it took a place it did not
            // fit in and the route clipped what it went round.
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
                foreach (Vec2 end in new[] { places[0], destination })
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
            float reach = unit.Footprint.BoundingRadius;

            foreach (UnitInstance other in battle.UnitsOnField())
            {
                if (ReferenceEquals(other, unit)) continue;
                if (other.Owner != unit.Owner) continue;
                if (!other.IsFighting) continue;

                // Called for both ends of every leg tried, so this is asked far
                // more than once per plan — a body too far off to possibly
                // overlap costs one subtraction and a compare instead of a
                // polygon clip against every regiment on the field.
                if (Vec2.Distance(other.Position, at) > reach + other.Footprint.BoundingRadius)
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
        /// <summary>
        /// What walking a finished line of waypoints costs, in the same seconds
        /// the search prices its own legs in. Negative means it cannot be walked.
        /// </summary>
        /// <remarks>
        /// The fronts are the ones the walk would really hold — square to each
        /// leg, coming round on every one (<b>M24</b>) — so this is a cost some
        /// regiment can actually pay, which is what makes it safe as a ceiling.
        /// A ceiling below the true best would prune the best away.
        /// </remarks>
        private static float PriceOf(
            BattleState battle, UnitInstance unit, IReadOnlyList<Vec2> path, Facing arriveOn)
        {
            if (path == null || path.Count < 2) return -1f;

            Facing on = unit.Facing;
            float total = 0f;

            for (int i = 1; i < path.Count; i++)
            {
                Facing walkOn = Marching.AlongTheLine(path[i - 1], path[i], on);

                float step = StepCost(battle, unit, path[i - 1], path[i], on, walkOn);
                if (step < 0f) return -1f;

                total += step;
                on = walkOn;
            }

            float turnRate = MathF.Max(1f, unit.Def.Get(UnitAttributes.TurnRate));

            return total + Degrees(on, arriveOn) / (turnRate * MovementSystem.PivotBonusWhileHalted);
        }

        /// <summary>A finished list of waypoints, as a route the assembler can take.</summary>
        private static Route Straighten(IReadOnlyList<Vec2> path)
        {
            var places = new List<Vec2>(path.Count);
            var fronts = new List<Facing>(path.Count);

            Facing on = default;

            for (int i = 0; i < path.Count; i++)
            {
                if (i > 0) on = Marching.AlongTheLine(path[i - 1], path[i], on);

                places.Add(path[i]);
                fronts.Add(on);
            }

            return new Route(places, fronts, 0f);
        }

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
        private static Route? Cheapest(
            BattleState battle, UnitInstance unit, List<Vec2> places, Vec2 destination,
            Facing arriveOn, bool mayPress, Ledger ledger)
        {
            int count = places.Count;

            // A state is a place reached on a front. Fronts are never sampled
            // round the circle: they are the ones some leg actually delivers.
            var stateAt = new List<int>();
            var stateOn = new List<Facing>();
            var best = new List<float>();
            var cameFrom = new List<int>();
            var settled = new List<bool>();

            // Named by place and presentation alone, so every answer outlives
            // the round that paid for it.
            bool[] asked = ledger.Asked;
            Facing[] legFront = ledger.Front;
            bool[] legStandNear = ledger.StandNear;
            bool[] legStandFar = ledger.StandFar;
            bool[] legClear = ledger.Clear;

            // Room to turn is a property of the ground, so it is asked once for
            // each place, and stays asked.
            sbyte[] roomToTurn = ledger.RoomToTurn;

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
            var leftToWalk = new float[count];

            for (int i = 0; i < count; i++)
                leftToWalk[i] = Vec2.Distance(places[i], destination) / pace;

            // Choosing what to expand next used to be a walk down every state
            // there was. Measured on one 800 m march through sixteen bodies:
            // 3.4 million of those steps against 17,182 pieces of geometry, so
            // the search was spending its time deciding what to look at rather
            // than looking. A heap makes the same choice in a few comparisons.
            //
            // Stale entries are left in rather than found and removed — a state
            // whose cost improves is pushed again, and the older, dearer copy is
            // recognised on the way out. That is what keeps this a heap of plain
            // numbers with no index to maintain.
            var frontier = new Frontier();
            frontier.Push(0, leftToWalk[0], ledger);

            while (true)
            {
                if (!frontier.TryPop(out int at, out float nearest, ledger)) break;

                if (settled[at]) continue;

                // A copy left over from before this state got cheaper.
                if (nearest > best[at] + leftToWalk[stateAt[at]]) continue;

                if (nearest >= arrivedCost) break;

                settled[at] = true;
                ledger.Expanded++;

                int from = stateAt[at];
                Vec2 here = places[from];
                Facing on = stateOn[at];

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
                    if (best[at] + apart / pace + leftToWalk[to] >= arrivedCost)
                    {
                        ledger.Pruned++;
                        continue;
                    }

                    // Face-on, or side-on either way. Three fronts, because those
                    // are the three a body of men actually walks a line on: front
                    // first, or presenting a flank to thread something narrow.
                    for (int k = 0; k < 3; k++)
                    {
                        // The stride is the cap, not however many places
                        // there are just now, so a leg keeps its name when the
                        // set grows in a later round.
                        int leg = (from * MostPlaces + to) * 3 + k;

                        if (asked[leg])
                        {
                            ledger.CacheHits++;
                        }
                        else
                        {
                            // unit.Facing only stands in for a leg of no length,
                            // and those are skipped above, so this bearing and
                            // everything drawn from it is a property of the two
                            // places alone.
                            Facing bearing = Marching.AlongTheLine(here, there, unit.Facing);

                            Facing walking =
                                k == 0 ? bearing
                                : k == 1 ? Facing.FromRadians(bearing.Radians + MathF.PI * 0.5f)
                                : Facing.FromRadians(bearing.Radians - MathF.PI * 0.5f);

                            legFront[leg] = walking;
                            legStandNear[leg] = CanStandHere(battle, unit, here, walking);
                            legStandFar[leg] = CanStandHere(battle, unit, there, walking);
                            ledger.StandChecks += 2;
                            ledger.LineChecks++;
                            legClear[leg] = Marching.IsClearLine(
                                battle, unit, here, there, walking, out UnitInstance? refused,
                                leaving: true, leavingGrazeOnly: true);

                            // The refusal is the whole reason this body will get
                            // places to bend at. Recorded only when the leg is
                            // freshly priced, which is exactly when the search
                            // wanted to walk it: the bound above turns back every
                            // leg that could not have been on the answer, before
                            // any geometry, so nothing here is asked idly.
                            if (!legClear[leg]) ledger.Refuse(refused);

                            asked[leg] = true;
                            ledger.LegsPriced++;
                        }

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
                        bool clear;

                        if (at == 0)
                        {
                            ledger.LineChecks++;

                            clear = Marching.IsClearLine(
                                battle, unit, here, there, walkOn, out UnitInstance? inTheWay,
                                leaving: true);

                            if (!clear) ledger.Refuse(inTheWay);
                        }
                        else
                        {
                            clear = legClear[leg];
                        }

                        if (!clear && !mayPress) continue;

                        // Turning above the cap means turning on the spot, and
                        // there has to be room for it where it stands.
                        if (Degrees(on, walkOn) > WalkingCapDegrees)
                        {
                            if (roomToTurn[from] == 0)
                            {
                                ledger.TurnChecks++;
                                roomToTurn[from] = (sbyte)(CanTurnHere(battle, unit, here) ? 1 : -1);
                            }

                            if (roomToTurn[from] < 0) continue;
                        }

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
                            index[key] = stateAt.Count;

                            ledger.States++;

                            frontier.Push(stateAt.Count, total + leftToWalk[to], ledger);

                            stateAt.Add(to);
                            stateOn.Add(walkOn);
                            best.Add(total);
                            cameFrom.Add(at);
                            settled.Add(false);
                        }
                        else if (total < best[state] && !settled[state])
                        {
                            best[state] = total;
                            cameFrom[state] = at;

                            frontier.Push(state, total + leftToWalk[to], ledger);
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

        /// <summary>
        /// The states waiting to be expanded, cheapest reckoning first.
        /// </summary>
        /// <remarks>
        /// A plain binary heap over pairs of (reckoning, state). Deliberately not
        /// a general priority queue with an index: this one never needs to find a
        /// state it already holds, because a state that gets cheaper is pushed
        /// again and the dearer copy is discarded when it surfaces. Two words per
        /// entry, no bookkeeping, and the same answers.
        /// </remarks>
        private sealed class Frontier
        {
            private float[] _by = new float[64];
            private int[] _state = new int[64];
            private int _held;

            public void Push(int state, float reckoning, Ledger ledger)
            {
                if (_held == _by.Length)
                {
                    Array.Resize(ref _by, _held * 2);
                    Array.Resize(ref _state, _held * 2);
                }

                int at = _held++;

                _by[at] = reckoning;
                _state[at] = state;

                while (at > 0)
                {
                    int up = (at - 1) / 2;

                    ledger.FrontierScans++;

                    if (_by[up] <= _by[at]) break;

                    Swap(up, at);
                    at = up;
                }
            }

            public bool TryPop(out int state, out float reckoning, Ledger ledger)
            {
                if (_held == 0)
                {
                    state = -1;
                    reckoning = 0f;
                    return false;
                }

                state = _state[0];
                reckoning = _by[0];

                _held--;

                if (_held > 0)
                {
                    _by[0] = _by[_held];
                    _state[0] = _state[_held];

                    int at = 0;

                    while (true)
                    {
                        int left = at * 2 + 1;
                        if (left >= _held) break;

                        int small = left;
                        int right = left + 1;

                        ledger.FrontierScans++;

                        if (right < _held && _by[right] < _by[left]) small = right;
                        if (_by[at] <= _by[small]) break;

                        Swap(at, small);
                        at = small;
                    }
                }

                return true;
            }

            private void Swap(int a, int b)
            {
                (_by[a], _by[b]) = (_by[b], _by[a]);
                (_state[a], _state[b]) = (_state[b], _state[a]);
            }
        }

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
