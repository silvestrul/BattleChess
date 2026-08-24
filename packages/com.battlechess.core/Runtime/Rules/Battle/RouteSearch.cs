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
        /// How far outside a blocker its candidate places sit, on top of the
        /// mover's own half-span.
        /// </summary>
        /// <remarks>
        /// Was four metres, which hugged the blocker. Measured against the
        /// hybrid — which may bend anywhere — the route the search wants bends a
        /// mean <b>15,8 m outside</b> the nearest place four metres offered, and
        /// forcing the hybrid's own route onto those places threw its whole
        /// advantage away: 100,9 s against its own 88,2, and worse than the
        /// search's own 99,6. So the graph was the limit, not the search over it.
        /// <para>
        /// Swept: 4 m gives 99,6 s a route, 10 gives 99,2, <b>20 gives 92,6</b>,
        /// 30 gives 94,0, 45 gives 93,8 and 60 gives 98,7. Keeping a tight ring
        /// as well as a wide one was tried and is worse than the wide one alone
        /// (97,9), for more places and more time.
        /// </para>
        /// </remarks>


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
        internal static int MostPlaces = 48;

        /// <summary>
        /// How many other places each place is joined to.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>M35.</b> Every place used to be joined to every other on three
        /// fronts, so a march cost places squared: at forty-eight places that is
        /// 6,912 legs, of which a search priced most. Two thirds of a plan's
        /// time went there — the swept-rectangle tests and the loop that walks
        /// them — and nothing else came close once the frontier, the terrain
        /// sampling and the standing checks had each been dealt with and each
        /// bought under a tenth.
        /// </para>
        /// <para>
        /// A route bends at the corner beside it, not at one across the field,
        /// so a place is joined to its nearest neighbours and no further. The
        /// destination is always among them, whatever the distance: that is what
        /// stops the graph falling into pieces, which is the usual way this rule
        /// goes wrong.
        /// </para>
        /// </remarks>
        /// <remarks>
        /// <para>
        /// <b>Set to the place cap, which makes it no limit at all.</b> The rule
        /// is kept because it works and is measured — at ten joins a whole army
        /// ordered at once went 788 ms to 366, better than twice as fast, with
        /// the suite unmoved — but it cost press-throughs, and the designer's
        /// call is that a regiment walking through its own army is worse than a
        /// slow order.
        /// </para>
        /// <para>
        /// Two arrangements that used to route cleanly stopped: going round a
        /// body means stepping between points on <i>its own</i> ring, and on a
        /// crowded field every one of a corner's ten nearest places belongs to
        /// some other body, so the leg round its own was cut. Joining each place
        /// to its whole ring as well fixed one of the two and cost a third of
        /// the speed; the other, an 800 m march through sixteen bodies, needs
        /// the long legs that skip past several at once, and those are exactly
        /// what nearness cuts. Widening the rule again to catch it would be the
        /// fourth patch on one idea, which is the shape this project has learned
        /// means the idea is wrong rather than the number.
        /// </para>
        /// </remarks>
        internal static int MostJoins => MostPlaces;

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
        internal static int MostRounds = 1;

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
        /// <summary>
        /// The shape the overlay draws, which must be the shape a march plans
        /// over or the picture is of something that never happened (<b>W5</b>).
        /// </summary>
        private const Shape Drawn = Shape.Tangents;

        public static IReadOnlyList<Vec2> DebugCandidatePlaces(
            BattleState battle, UnitInstance unit, Vec2 destination)
        {
            var ledger = new Ledger();
            ledger.Reset(MostPlaces);
            ledger.Shape = Drawn;

            List<Vec2> places = Places(
                battle, unit, destination, ledger.Grown, ledger.Rings, ledger.Corners, Drawn);

            Hunt(battle, unit, destination, unit.OrderFacing, places, ledger, float.MaxValue, out _);

            return places;
        }

        /// <summary>
        /// Plans a march, or returns a plan that was not found.
        /// </summary>
        public static Plan Find(
            BattleState battle, UnitInstance unit, Vec2 destination, Facing arriveOn,
            IBattleLog? log = null, IPathfinder? pathfinder = null,
            Shape shape = Shape.Rings)
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

            // Every body along the drawn line offers its places at once (M34);
            // anything further out has to earn them by refusing a leg (M32).
            Ledger ledger = battle.PlanningScratch;

            // Sized for the most this march could reach, so a later round may
            // name legs the first one had no places for (M32).
            ledger.Reset(MostPlaces);
            ledger.Shape = shape;

            List<Vec2> places = Places(
                battle, unit, destination, ledger.Grown, ledger.Rings, ledger.Corners, shape);

            Route? route = Hunt(
                battle, unit, destination, arriveOn, places, ledger, float.MaxValue, out bool pressed);

            int filtered = 0;
            foreach (Corner corner in ledger.Corners)
                if (corner.IsFiltered) filtered++;

            // Named because it answers a question a designer actually asked:
            // "why does this cost so much when there are only a handful of
            // regiments in the way?" Every body pulled into the corridor offers
            // up to twenty-four candidate points and only a third of them are
            // ever filtered by tangency — the rest are unfiltered by design
            // (M36), because filtering them cost real routes. Bodies is the
            // number worth arguing about; the filtered fraction is the reason
            // the graph stays this size regardless.
            var effort = new RouteEffort(
                places.Count, ledger.LegsPriced, ledger.Expanded, ledger.Rounds,
                ledger.States, ledger.FrontierScans, ledger.CacheHits, ledger.Pruned,
                ledger.LineChecks, ledger.StandChecks, ledger.TurnChecks,
                bodies: ledger.Grown.Count, filteredPlaces: filtered);

            // M33. The ladder is a floor under the search: a march takes
            // whichever of the two is cheaper, both priced in the same seconds.
            // Recorded 17 Aug, one click, the ladder 82 m and 28 s against the
            // search's 153 m and 40 s — the search was optimal over the places
            // it had invented and the ladder's bends were not among them, which
            // is a true answer to the wrong question.
            //
            // <b>Asked only when the search's answer looks worth beating.</b>
            // Running it on every plan cost about 0.9 ms each, and measured on
            // sixteen regiments ordered at once that was 7 ms of 17.7 — more
            // than every optimisation in this pass put together had saved. A
            // route that shouldered through, or that wanders half again further
            // than the straight line, is worth a second opinion. One that walks
            // nearly straight to where it was sent is not: there is nothing
            // there for a second planner to win back.
            if (!route.HasValue)
                return FromLadder(battle, unit, pathfinder, destination, arriveOn, effort.WithLadder());

            if (pressed)
            {
                // A clean route beats a press-through whatever it costs: one is
                // a march, the other is walking through your own men.
                RouteEffort asked = effort.WithLadder();
                Plan instead = FromLadder(battle, unit, pathfinder, destination, arriveOn, asked);

                return instead.Path.Found ? instead : Assemble(route.Value, pressed: true, asked);
            }

            Route found = Smooth(battle, unit, route.Value, arriveOn);

            float foundPrice = PriceOf(battle, unit, found.Places, arriveOn);

            // Both sides of this in seconds, which is the only currency the rest
            // of the model uses.
            //
            // It was metres: "did the route wander more than a quarter past the
            // straight line". A route that wanders is dear, but so is one that
            // does not — turning costs seconds and no ground at all, so a bend
            // taken on the spot is free by the old test and can double the time.
            // Measured in play: a march of 160 m straight came back as 169 m and
            // 93 s, sailed through the metres gate at 1,06, and was walked
            // without ever being compared — while the ladder held a 176 m route
            // at 46 s. Across 240 orders on the bench fields the ladder held a
            // cheaper answer than the one walked in a third of them.
            //
            // The reference is the same function on the straight line rather
            // than a distance over a pace, so the two sides are priced by one
            // routine and cannot drift apart (W5) — and so the wheel a route
            // needs is charged on both sides instead of only one.
            Vec2[] asTheCrowFlies = _straightLine ??= new Vec2[2];
            asTheCrowFlies[0] = unit.Position;
            asTheCrowFlies[1] = destination;

            float straightPrice = PriceOf(battle, unit, asTheCrowFlies, arriveOn);

            // A straight line nothing can price — it starts inside somebody, so
            // the pace is nil — is no reference at all, and the safe reading of
            // "no reference" is to ask for the second opinion.
            if (foundPrice >= 0f && straightPrice >= 0f &&
                foundPrice <= straightPrice * DearEnoughToAskAgain)
                return Assemble(found, pressed: false, effort);

            RouteEffort alsoAsked = effort.WithLadder();
            Plan ladder = FromLadder(battle, unit, pathfinder, destination, arriveOn, alsoAsked);

            if (ladder.Path.Found &&
                (foundPrice < 0f || PriceOf(battle, unit, ladder.Path.Waypoints, arriveOn) < foundPrice))
                return ladder;

            return Assemble(found, pressed: false, alsoAsked);
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
            List<Vec2> places, Ledger ledger, float ceiling, out bool pressed)
        {
            using var _profile = PlanningProfile.Measure(PlanningProfile.Step.Hunt);

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
                    battle, unit, places, destination, arriveOn, mayPress: false, ledger, ceiling);

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
            // A finite ceiling means the ladder already holds a clean route, so
            // there is nothing to shoulder through for. The caller takes it.
            if (ceiling < float.MaxValue) return null;

            pressed = true;

            return Cheapest(
                battle, unit, places, destination, arriveOn, mayPress: true, ledger, float.MaxValue);
        }

        /// <summary>
        /// Gives every body that refused a leg its ring of places, and says
        /// whether that added anything new.
        /// </summary>
        private static bool Grow(
            UnitInstance unit, List<Vec2> places, Vec2 destination, Ledger ledger)
        {
            using var _profile = PlanningProfile.Measure(PlanningProfile.Step.GrowPlaces);

            int had = places.Count;

            // Names are fixed when the ledger is built, so a round may not add
            // ground the ledger cannot name. Dormant while MostRounds is 1.
            if (places.Count >= ledger.Stride) return false;

            // In the order the search met them, which is cost order, so when the
            // cap does bite it bites the bodies the march cared about least. The
            // list is built by a deterministic walk, so this is stable (M0).
            foreach (UnitInstance other in ledger.Refused)
            {
                if (!ledger.Grown.Add(other.Id)) continue;

                if (ledger.Shape == Shape.Corners)
                {
                    CornersFor(places, ledger.Rings, ledger.Corners, ledger.Rings.Count, unit, other);
                }
                else
                {
                    RingsFor(
                        places, ledger.Rings, ledger.Corners, ledger.Rings.Count, unit, other,
                        destination);
                }
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
        internal sealed class Ledger
        {
            /// <summary>
            /// Sized to the places this march actually has, not to the most any
            /// march could have.
            /// </summary>
            /// <remarks>
            /// The fixed <see cref="MostPlaces"/> stride cost about 110 kB of
            /// arrays on every plan whatever its size, and that is why cutting
            /// the search's work stopped showing up in the clock: geometry fell
            /// by more than half, states four-fold, and a whole army ordered at
            /// once did not move, because the bill was one allocation per plan
            /// rather than anything the search did. Fifty-six plans in an order
            /// came to some six megabytes of it.
            /// </remarks>
            /// <summary>
            /// Readies the scratchpad for a march over this many places, keeping
            /// whatever it already grew.
            /// </summary>
            /// <remarks>
            /// <para>
            /// <b>M40.</b> A plan allocated 205 kB — against a whole simulation
            /// tick's 1,1 — so an ordinary session churned megabytes through a
            /// stop-the-world collector and stopped for forty to fifty
            /// milliseconds at a time, evenly spread and uncorrelated with
            /// anything on screen. The work was never the problem; the litter
            /// was. So a battle keeps one of these and every march borrows it.
            /// </para>
            /// <para>
            /// Sound because planning is one march at a time within a battle,
            /// and because the scratchpad is per battle rather than static —
            /// two battles in one process, which is every test run, must not
            /// share one.
            /// </para>
            /// </remarks>
            public void Reset(int places)
            {
                Stride = places;

                int legs = places * places * 3;

                Asked = Ready(Asked, legs);
                Front = Ready(Front, legs);
                StandNear = Ready(StandNear, legs);
                StandFar = Ready(StandFar, legs);
                Clear = Ready(Clear, legs);
                RoomToTurn = Ready(RoomToTurn, places);

                Array.Clear(Asked, 0, legs);
                Array.Clear(StandNear, 0, legs);
                Array.Clear(StandFar, 0, legs);
                Array.Clear(Clear, 0, legs);
                Array.Clear(RoomToTurn, 0, places);

                Standing.Clear();
                Refused.Clear();
                Grown.Clear();
                Rings.Clear();
                Corners.Clear();

                Joins = null;

                StateAt.Clear();
                StateOn.Clear();
                Best.Clear();
                CameFrom.Clear();
                Settled.Clear();
                Index.Clear();
                Heap.Clear();

                LegsPriced = 0;
                Expanded = 0;
                Rounds = 0;
                States = 0;
                FrontierScans = 0;
                CacheHits = 0;
                Pruned = 0;
                LineChecks = 0;
                StandChecks = 0;
                TurnChecks = 0;
            }

            /// <summary>Grows a buffer if it is too small, and keeps it if not.</summary>
            private static T[] Ready<T>(T[]? had, int wanted) =>
                had != null && had.Length >= wanted ? had : new T[wanted];

            /// <summary>
            /// How a leg is named. Fixed for the life of the search, so the
            /// answers keep their names — which is what growing the place set
            /// (<b>M32</b>) needed, and why it may not grow past this.
            /// </summary>
            public int Stride;

            public bool[] Asked = Array.Empty<bool>();
            public Facing[] Front = Array.Empty<Facing>();
            public bool[] StandNear = Array.Empty<bool>();
            public bool[] StandFar = Array.Empty<bool>();
            public bool[] Clear = Array.Empty<bool>();

            /// <summary>Standing answers, by place and front modulo half a turn.</summary>
            public readonly Dictionary<long, bool> Standing = new Dictionary<long, bool>();

            /// <summary>Nought means not yet asked, as it is ground, not a leg.</summary>
            public sbyte[] RoomToTurn = Array.Empty<sbyte>();

            // ---- What the search itself borrows ------------------------------

            public readonly List<int> StateAt = new List<int>();
            public readonly List<Facing> StateOn = new List<Facing>();
            public readonly List<float> Best = new List<float>();
            public readonly List<int> CameFrom = new List<int>();
            public readonly List<bool> Settled = new List<bool>();
            public readonly Dictionary<long, int> Index = new Dictionary<long, int>();
            public readonly Frontier Heap = new Frontier();

            public float[] LeftToWalk = Array.Empty<float>();

            /// <summary>Sizes the heuristic table without giving up the last one.</summary>
            public float[] ReadyLeftToWalk(int count) => LeftToWalk = Ready(LeftToWalk, count);

            /// <summary>Who said no this round, in the order they said it.</summary>
            public readonly List<UnitInstance> Refused = new List<UnitInstance>();

            /// <summary>Who has already been given places, so nobody is quarried twice.</summary>
            public readonly HashSet<UnitId> Grown = new HashSet<UnitId>();

            /// <summary>Who each place is joined to. Worked out once for a march.</summary>
            public int[][]? Joins;

            /// <summary>Which body's ring each place sits on, or <see cref="NoRing"/>.</summary>
            public readonly List<int> Rings = new List<int>();

            /// <summary>The sides of each place, for the tangent shape.</summary>
            public readonly List<Corner> Corners = new List<Corner>();

            /// <summary>Which shape of graph this march is planned over.</summary>
            public Shape Shape;

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

        /// <summary>
        /// How much dearer than walking straight there a route may be before a
        /// second opinion is worth the millisecond it costs.
        /// </summary>
        /// <remarks>
        /// Carried over from the metres version it replaced, and it should not
        /// be assumed to be the right number: a quarter more ground and a
        /// quarter more time are not the same tolerance, and this one now fires
        /// on wheels the old one could not see. A designer's number
        /// (<b>W4</b>), left where it was until it is chosen deliberately.
        /// </remarks>
        private const float DearEnoughToAskAgain = 1.25f;

        /// <summary>Two points, reused, so pricing the straight line litters nothing.</summary>
        [ThreadStatic] private static Vec2[]? _straightLine;

        /// <summary>
        /// The ladder's answer, if it has a clean one, as a plan.
        /// </summary>
        /// <summary>
        /// Whether a route can really be walked without meeting one of your own,
        /// asked of the body squared to each leg.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Asked of the ladder's answer rather than taken on trust, and the
        /// difference is the whole reason this search exists.</b> The floor used
        /// to accept any ladder route whose <see cref="Plan.PressedThrough"/>
        /// was false — but the ladder's defining fault, the one measured at the
        /// start of all this, is that it returns routes straight through
        /// friendly regiments <i>without setting that flag</i>. Nine of nineteen
        /// approach angles came back as a two-waypoint line through two bodies,
        /// undeclared, so nothing charged for them and no rule had agreed to
        /// them.
        /// </para>
        /// <para>
        /// Taking the flag at its word imported every one of those into the
        /// search: the nineteen-angle gate fell from 17 clear to 10, which is
        /// barely better than the ladder's own 7. A second opinion is only worth
        /// having if it is checked.
        /// </para>
        /// </remarks>
        private static bool WalksClean(BattleState battle, UnitInstance unit, IReadOnlyList<Vec2> path)
        {
            for (int i = 1; i < path.Count; i++)
            {
                Facing front = Marching.AlongTheLine(path[i - 1], path[i], unit.Facing);

                if (!Marching.IsClearLine(battle, unit, path[i - 1], path[i], front, leaving: true))
                    return false;
            }

            return true;
        }

        private static Plan FromLadder(
            BattleState battle, UnitInstance unit, IPathfinder? pathfinder, Vec2 destination,
            Facing arriveOn, RouteEffort effort)
        {
            if (pathfinder == null)
            {
                return new Plan(
                    PathResult.Failed(PathFailure.NoRouteExists,
                        "nothing reaches that ground, going round or through", 0),
                    null, false, effort);
            }

            Plan ladder = Marching.ByTheLadder(battle, unit, pathfinder, destination);

            if (ladder.Path.Found && !ladder.PressedThrough && WalksClean(battle, unit, ladder.Path.Waypoints) &&
                PriceOf(battle, unit, ladder.Path.Waypoints, arriveOn) >= 0f)
            {
                return Assemble(Straighten(ladder.Path.Waypoints), pressed: false, effort);
            }

            return new Plan(
                PathResult.Failed(PathFailure.NoRouteExists,
                    "nothing reaches that ground, going round or through", 0),
                null, false, effort);
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
        private static List<Vec2> Places(
            BattleState battle, UnitInstance unit, Vec2 destination, HashSet<UnitId> grown,
            List<int> rings, List<Corner> corners, Shape shape)
        {
            using var _profile = PlanningProfile.Measure(PlanningProfile.Step.CandidatePlaces);

            var places = new List<Vec2> { unit.Position, destination };

            // The regiment's own ground and the ordered destination sit on
            // nobody's ring, and are points rather than corners.
            rings.Add(NoRing);
            rings.Add(NoRing);
            corners.Add(Corner.Free);
            corners.Add(Corner.Free);

            float length = Vec2.Distance(unit.Position, destination);

            int ring = 0;

            foreach (UnitInstance other in Nearby(battle, unit, destination, length))
            {
                if (places.Count >= MostPlaces) break;

                // Marked as quarried, so a later round does not spend a second
                // pass rediscovering the ring it already has.
                grown.Add(other.Id);

                if (shape == Shape.Corners) CornersFor(places, rings, corners, ring++, unit, other);
                else RingsFor(places, rings, corners, ring++, unit, other, destination);
            }

            return places;
        }

        /// <summary>The two ends of the march, which belong to no body.</summary>
        private const int NoRing = -1;

        /// <summary>Which shape of graph a march is planned over.</summary>
        public enum Shape
        {
            /// <summary>
            /// <b>M31.</b> Every body's whole ring — corners, and where the ends
            /// of the march project onto each face — joined to everything.
            /// </summary>
            Rings,

            /// <summary>
            /// The corners of M36, joined the old way — the half of the change
            /// that is about which ground exists, with the half about which legs
            /// are worth naming left out.
            /// </summary>
            Corners,

            /// <summary>
            /// <b>M36.</b> Inflated corners only, joined only where the leg is
            /// tangent to the body it leaves and the body it arrives at.
            /// </summary>
            Tangents,
        }

        /// <summary>
        /// A candidate place, and the two places either side of it on its own
        /// body's inflated rectangle.
        /// </summary>
        /// <remarks>
        /// Two neighbours are enough to decide tangency and the third corner is
        /// never needed: from any vertex of a convex shape the whole shape lies
        /// inside the wedge its two edges make, so a line that keeps both
        /// neighbours on one side keeps everything on one side.
        /// </remarks>
        internal readonly struct Corner
        {
            private const byte Anywhere = 0;
            private const byte Vertex = 1;

            /// <summary>An end of the march: a point, which no leg can cut into.</summary>
            public static readonly Corner Free = new Corner(Anywhere, Vec2.Zero, Vec2.Zero);

            /// <summary>A corner of an inflated rectangle, and its two neighbours.</summary>
            public static Corner At(Vec2 left, Vec2 right) => new Corner(Vertex, left, right);

            private Corner(byte kind, Vec2 a, Vec2 b)
            {
                _kind = kind;
                _a = a;
                _b = b;
            }

            private readonly byte _kind;
            private readonly Vec2 _a;
            private readonly Vec2 _b;

            /// <summary>Whether tangency actually filters legs leaving here.</summary>
            public bool IsFiltered => _kind == Vertex;

            /// <summary>Whether a leg leaving here goes away from its own body.</summary>
            public bool Allows(Vec2 from, Vec2 along)
            {
                switch (_kind)
                {
                    case Vertex:
                    {
                        float left = along.X * (_a.Y - from.Y) - along.Y * (_a.X - from.X);
                        float right = along.X * (_b.Y - from.Y) - along.Y * (_b.X - from.X);

                        // Closed sides, so the hull edges themselves — where one
                        // neighbour lies exactly on the line — are kept. Those
                        // are the legs that go round.
                        return left * right >= -TangentSlack;
                    }

                    default:
                        return true;
                }
            }
        }

        /// <summary>
        /// Whether a leg leaves <paramref name="from"/> without cutting into the
        /// body that corner belongs to.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This is the whole of <b>M36</b>. A shortest route among convex bodies
        /// bends only at their corners, and only tangentially: a leg that leaves
        /// a corner into the wedge its own body occupies is inside that body, and
        /// one that leaves across the wedge cuts the body in half. Neither can be
        /// on any route, so neither is worth naming, pricing or sweeping.
        /// </para>
        /// <para>
        /// Of the sixteen legs between two bodies' four corners exactly four
        /// survive — the four common tangents — and that ratio is what the search
        /// is meant to buy. It is a pruning that <i>cannot</i> lose a route, which
        /// is what separates it from cutting each place to its ten nearest, where
        /// the legs round a body's own ring went first and the search shouldered
        /// through the army instead of round it.
        /// </para>
        /// </remarks>
        private static bool Tangent(in Corner corner, Vec2 from, Vec2 to)
        {
            Vec2 along = (to - from).Normalised();

            return along.IsNearZero || corner.Allows(from, along);
        }

        /// <summary>
        /// How far, in square metres, a neighbour may sit the wrong side of a leg
        /// before the leg is called untangent.
        /// </summary>
        private const float TangentSlack = 0.01f;

        /// <summary>
        /// A body's inflated corners, each knowing its two neighbours.
        /// </summary>
        /// <remarks>
        /// Both presentations, for the reason <see cref="RingsFor"/> gives: a
        /// place is only a place for the front it is stood on. What is dropped
        /// against the rings is the projections — where the ends of the march
        /// come onto each face — because tangency makes them unnecessary. The
        /// tangent lines from a point to a rectangle touch it at its corners, so
        /// a projected face point is never where a route bends.
        /// </remarks>
        private static void CornersFor(
            List<Vec2> places, List<int> rings, List<Corner> corners, int ring,
            UnitInstance unit, UnitInstance other)
        {
            if (places.Count >= MostPlaces) return;

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
                {
                    Vec2 at = other.Position + ahead * (deep * i) + beside * (wide * j);

                    ConsiderCorner(places, rings, corners, ring, at, Corner.At(
                        other.Position + ahead * (deep * -i) + beside * (wide * j),
                        other.Position + ahead * (deep * i) + beside * (wide * -j)));
                }
            }
        }

        private static void ConsiderCorner(
            List<Vec2> places, List<int> rings, List<Corner> corners, int ring, Vec2 place,
            in Corner sides)
        {
            if (places.Count >= MostPlaces) return;

            foreach (Vec2 already in places)
            {
                if (Vec2.Distance(already, place) < ApartEnoughMetres) return;
            }

            places.Add(place);
            rings.Add(ring);
            corners.Add(sides);
        }

        /// <summary>
        /// The places each place is joined to: those, and only those, where the
        /// leg is tangent at both ends.
        /// </summary>
        private static int[][] Tangents(List<Vec2> places, List<Corner> corners)
        {
            int count = places.Count;
            var joins = new int[count][];
            var mine = new List<int>(count);

            for (int from = 0; from < count; from++)
            {
                mine.Clear();

                // The destination first, so the search meets the leg that can
                // finish the march before any other.
                if (from != 1 && Tangent(corners[from], places[from], places[1])) mine.Add(1);

                for (int to = 0; to < count; to++)
                {
                    if (to == from || to == 1) continue;

                    if (!Tangent(corners[from], places[from], places[to])) continue;
                    if (!Tangent(corners[to], places[to], places[from])) continue;

                    mine.Add(to);
                }

                joins[from] = mine.ToArray();
            }

            return joins;
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
            List<Vec2> places, List<int> rings, List<Corner> corners, int ring, UnitInstance unit,
            UnitInstance other, Vec2 destination)
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
                {
                    ConsiderCorner(
                        places, rings, corners, ring,
                        other.Position + ahead * (deep * i) + beside * (wide * j),
                        Corner.At(
                            other.Position + ahead * (deep * -i) + beside * (wide * j),
                            other.Position + ahead * (deep * i) + beside * (wide * -j)));
                }

                // Where the ends of the march come onto each face. These are
                // the "walk in along the body's side" points.
                foreach (Vec2 end in new[] { places[0], destination })
                {
                    Vec2 offset = end - other.Position;

                    float onAhead = Vec2.Dot(offset, ahead);
                    float onBeside = Vec2.Dot(offset, beside);

                    for (int s = -1; s <= 1; s += 2)
                    {
                        // <b>Unfiltered, and measured that way.</b> Tangency is
                        // an argument about corners and only about corners: at
                        // the middle of an edge the one tangent line is the edge
                        // itself, so the corner test refuses everything, and even
                        // relaxed to "leaves outward" it cost five of the
                        // nineteen approach angles — 17 clear became 12, and 17
                        // came straight back when face places were let alone.
                        //
                        // The reason is that these rings are not the bodies. They
                        // are the bodies padded by how much room the mover needs,
                        // and padded by more than it needs on the presentation it
                        // is not using, so a leg that cuts the ring can be well
                        // clear of the regiment inside it. A corner keeps its
                        // test because three quarters of the circle is loose
                        // enough that the padding does not matter. Half is not.
                        ConsiderCorner(
                            places, rings, corners, ring,
                            other.Position + ahead * (deep * s) + beside * Clamp(onBeside, wide),
                            Corner.Free);

                        ConsiderCorner(
                            places, rings, corners, ring,
                            other.Position + beside * (wide * s) + ahead * Clamp(onAhead, deep),
                            Corner.Free);
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
        /// <summary>How far apart two candidate places must be to count as two.</summary>
        private const float ApartEnoughMetres = 1f;

        private static float Clamp(float value, float limit) =>
            value < -limit ? -limit : value > limit ? limit : value;

        // ---- What a leg costs -------------------------------------------------

        /// <summary>
        /// Seconds to walk one leg, arriving from a given front, and the front it
        /// ends on. Negative seconds mean the leg cannot be walked.
        /// </summary>
        /// <summary>
        /// The two numbers every leg is priced against, read once for a march.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Half of all the time spent planning was reading these.</b> A
        /// sampled profile of sixty-four regiments ordered at once put 49.9% of
        /// samples in the type check behind an attribute read, against 12.5% in
        /// the swept-rectangle test and 9.1% in the search loop itself. Pace and
        /// turn rate were being fetched inside <see cref="Seconds"/>, which is
        /// asked once per edge relaxed — tens of thousands of times for one
        /// march — and each fetch is a string-keyed dictionary lookup, an unbox
        /// and a type test, with pace also asking the terrain grid where the
        /// regiment is standing.
        /// </para>
        /// <para>
        /// Neither can change while a march is being planned. The regiment does
        /// not move, and a definition's turn rate is a definition's turn rate.
        /// This is why five separate reductions — geometry more than halved,
        /// states cut four-fold, the frontier a hundredfold — moved the clock
        /// barely at all: none of them touched the half of the work that was
        /// never geometry.
        /// </para>
        /// </remarks>
        private readonly struct Going
        {
            public Going(BattleState battle, UnitInstance unit)
            {
                Pace = MathF.Max(0.1f, battle.SpeedOf(unit));
                TurnRate = MathF.Max(1f, unit.Def.Get(UnitAttributes.TurnRate));
            }

            public readonly float Pace;
            public readonly float TurnRate;
        }

        private static float Seconds(
            in Going going, Vec2 from, Vec2 to, Facing arrivingOn, Facing walkOn, bool inside)
        {
            float length = Vec2.Distance(from, to);
            float pace = going.Pace;
            float turnRate = going.TurnRate;

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
        /// <summary>
        /// Whether the regiment fits here on this front, asked once however many
        /// legs want to know.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Half the standing checks in the search were the same question
        /// asked backwards.</b> A leg from A to B is walked on the bearing
        /// between them; the leg from B to A is walked on that bearing turned
        /// about. A regiment's footprint is a rectangle about its centre, so the
        /// ground it covers facing east is the ground it covers facing west —
        /// and <see cref="CanStandHere"/> asks nothing else, no terrain and
        /// nothing that could tell the two apart. The two legs were paying twice
        /// for one answer. The flank presentations pair off the same way: <i>k</i>
        /// = 1 outbound is <i>k</i> = 2 back.
        /// </para>
        /// <para>
        /// So the answer is kept against the place and the front <b>modulo half
        /// a turn</b>, which makes the pair one entry. Standing checks were two
        /// thirds of all the geometry a march asked.
        /// </para>
        /// <para>
        /// The per-leg arrays are still there and still consulted first: they
        /// cost an array index where this costs a hash, and the inner loop reads
        /// them hundreds of thousands of times. This is only reached when a leg
        /// is priced for the first time.
        /// </para>
        /// </remarks>
        private static bool Stands(
            Ledger ledger, BattleState battle, UnitInstance unit, int place, Vec2 at, Facing front)
        {
            long key = StandKey(place, front);

            if (ledger.Standing.TryGetValue(key, out bool known)) return known;

            ledger.StandChecks++;

            bool fits = CanStandHere(battle, unit, at, front);

            ledger.Standing[key] = fits;

            return fits;
        }

        /// <summary>How finely a front is told apart when asking about standing.</summary>
        /// <remarks>
        /// Over half a turn rather than a whole one, which is the point: a front
        /// and its opposite name the same rectangle. Same resolution as
        /// <see cref="FrontSteps"/> across the half, so nothing that used to be
        /// told apart is merged except the pair that was always identical.
        /// </remarks>
        private const int HalfFrontSteps = 2048;

        private static long StandKey(int place, Facing front)
        {
            int step = (int)MathF.Round(front.Radians / MathF.PI * HalfFrontSteps)
                       & (HalfFrontSteps - 1);

            return (long)place * HalfFrontSteps + step;
        }

        private static bool CanStandHere(BattleState battle, UnitInstance unit, Vec2 at, Facing front)
        {
            using var _profile = PlanningProfile.Measure(PlanningProfile.Step.StandCheck);

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
                // polygon clip against every regiment on the field. Squared, so
                // that the compare that turns most bodies back costs no root.
                float span = reach + other.Footprint.BoundingRadius;
                if (Vec2.DistanceSquared(other.Position, at) > span * span)
                    continue;

                if (OrientedRect.OverlapFraction(body, other.Shape) > OrderSystem.GrazingTolerance)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Somewhere for the index to put its answer without allocating. One per
        /// thread and one per asking method: nothing nests today, but a shared
        /// buffer would turn the day somebody adds a query inside one of these
        /// loops into a wrong answer rather than a compile error.
        /// </summary>
        [ThreadStatic]
        private static List<UnitInstance>? _standingNear;

        [ThreadStatic]
        private static List<UnitInstance>? _turningNear;

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
            using var _profile = PlanningProfile.Measure(PlanningProfile.Step.TurnCheck);

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
            using var _profile = PlanningProfile.Measure(PlanningProfile.Step.SmoothRoute);

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

            var going = new Going(battle, unit);

            var cost = new float[legs][];
            var cameFrom = new int[legs][];

            for (int i = 0; i < legs; i++)
            {
                cost[i] = new float[options[i].Count];
                cameFrom[i] = new int[options[i].Count];
            }

            for (int f = 0; f < options[0].Count; f++)
            {
                cost[0][f] = StepCost(
                    battle, unit, going, places[0], places[1], unit.Facing, options[0][f]);
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
                        battle, unit, going, places[i], places[i + 1],
                        options[i - 1][g], options[i][f]);

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

            var going = new Going(battle, unit);

            Facing on = unit.Facing;
            float total = 0f;

            for (int i = 1; i < path.Count; i++)
            {
                Facing walkOn = Marching.AlongTheLine(path[i - 1], path[i], on);

                float step = StepCost(battle, unit, going, path[i - 1], path[i], on, walkOn);
                if (step < 0f) return -1f;

                total += step;
                on = walkOn;
            }

            return total + Degrees(on, arriveOn) / (going.TurnRate * MovementSystem.PivotBonusWhileHalted);
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
            BattleState battle, UnitInstance unit, in Going going,
            Vec2 from, Vec2 to, Facing arrivingOn, Facing walkOn)
        {
            // Turning above the cap means turning on the spot, and there has to
            // be room for it where it stands.
            if (Degrees(arrivingOn, walkOn) > WalkingCapDegrees && !CanTurnHere(battle, unit, from))
                return -1f;

            return Seconds(going, from, to, arrivingOn, walkOn, inside: false);
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
            Facing arriveOn, bool mayPress, Ledger ledger, float ceiling)
        {
            int count = places.Count;

            // A state is a place reached on a front. Fronts are never sampled
            // round the circle: they are the ones some leg actually delivers.
            // Borrowed from the scratchpad, not made fresh: these five and the
            // index below were most of a plan's 205 kB, and a plan's litter was
            // what stopped the frames (M40).
            List<int> stateAt = ledger.StateAt;
            List<Facing> stateOn = ledger.StateOn;
            List<float> best = ledger.Best;
            List<int> cameFrom = ledger.CameFrom;
            List<bool> settled = ledger.Settled;

            stateAt.Clear();
            stateOn.Clear();
            best.Clear();
            cameFrom.Clear();
            settled.Clear();

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
            Dictionary<long, int> index = ledger.Index;
            index.Clear();

            stateAt.Add(0);
            stateOn.Add(unit.Facing);
            best.Add(0f);
            cameFrom.Add(-1);
            settled.Add(false);
            index[Key(0, unit.Facing)] = 0;

            int arrivedFrom = -1;
            Facing arrivedOn = unit.Facing;

            // The best arrival known, which is what every leg is pruned against
            // before a scrap of geometry is asked. Starting it at infinity means
            // nothing prunes until the search has blundered into a whole route
            // by itself — measured, that left it pricing 5,379 of the 6,912 legs
            // a forty-eight place graph holds.
            //
            // So it starts at whatever the ladder's route costs, when there is
            // one. Every leg that cannot beat that route dies unmeasured, and
            // nothing is lost by it: a route the search finds that is dearer
            // than the ladder's would be thrown away at the end anyway (M33).
            //
            // <b>Tried once before and withdrawn, wrongly.</b> It appeared to
            // make the search buy more places and search again — 10 places and
            // 180 legs becoming 34 and 1,260. That was M32's round growth, not
            // this: a search that must *beat* a route cannot stop at the first
            // one it finds, so it grew and retried. With growth off the places
            // are fixed and there is nothing left to inflate.
            float arrivedCost = ceiling;

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
            ledger.Joins ??= ledger.Shape == Shape.Tangents
                ? Tangents(places, ledger.Corners)
                : Joins(places, ledger.Rings);

            var going = new Going(battle, unit);

            float pace = going.Pace;
            float[] leftToWalk = ledger.ReadyLeftToWalk(count);

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
            void Price(int leg, int k, int from, int to, Vec2 here, Vec2 there)
            {
                if (asked[leg])
                {
                    ledger.CacheHits++;
                    return;
                }

                Facing bearing = Marching.AlongTheLine(here, there, unit.Facing);

                Facing walking =
                    k == 0 ? bearing
                    : k == 1 ? Facing.FromRadians(bearing.Radians + MathF.PI * 0.5f)
                    : Facing.FromRadians(bearing.Radians - MathF.PI * 0.5f);

                legFront[leg] = walking;
                legStandNear[leg] = Stands(ledger, battle, unit, from, here, walking);
                legStandFar[leg] = Stands(ledger, battle, unit, to, there, walking);
                ledger.LineChecks++;
                legClear[leg] = Marching.IsClearLine(
                    battle, unit, here, there, walking, out UnitInstance? refused,
                    leaving: true, leavingGrazeOnly: true);

                if (!legClear[leg]) ledger.Refuse(refused);

                asked[leg] = true;
                ledger.LegsPriced++;
            }

            Frontier frontier = ledger.Heap;
            frontier.Clear();
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

                int[] joined = ledger.Joins![from];

                for (int j = 0; j < joined.Length; j++)
                {
                    int to = joined[j];

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
                    int faceOn = (from * ledger.Stride + to) * 3;
                    Price(faceOn, 0, from, to, here, there);

                    int presentations =
                        legClear[faceOn] && legStandNear[faceOn] && legStandFar[faceOn] ? 1 : 3;

                    for (int k = 0; k < presentations; k++)
                    {
                        int leg = faceOn + k;

                        Price(leg, k, from, to, here, there);

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

                        float seconds = Seconds(going, here, there, on, walkOn, inside: !clear);
                        if (seconds < 0f) continue;

                        float total = best[at] + seconds;

                        if (to == 1)
                        {
                            // The last thing a march does is come onto the front
                            // it was ordered to arrive on. Counting it here is
                            // what stops a route that arrives conveniently but
                            // pointing the wrong way from winning on paper.
                            total += Degrees(walkOn, arriveOn)
                                     / (going.TurnRate * MovementSystem.PivotBonusWhileHalted);

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
        internal sealed class Frontier
        {
            private float[] _by = new float[64];
            private int[] _state = new int[64];
            private int _held;

            /// <summary>Empties it without giving up the arrays it grew.</summary>
            public void Clear() => _held = 0;

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

        /// <summary>
        /// The places each place is joined to: its nearest, and the destination
        /// always.
        /// </summary>
        /// <remarks>
        /// Ties break on the lower index so two places equally far off cannot
        /// order themselves by whatever the generator happened to emit (<b>M0</b>).
        /// </remarks>
        private static int[][] Joins(List<Vec2> places, List<int> rings)
        {
            int count = places.Count;
            var joins = new int[count][];

            var order = new int[count];
            var away = new float[count];
            var taken = new bool[count];

            var mine = new List<int>(MostPlaces);

            for (int from = 0; from < count; from++)
            {
                mine.Clear();
                Array.Clear(taken, 0, count);

                taken[from] = true;

                // The destination first, so the search meets the leg that can
                // finish the march before any other.
                if (from != 1)
                {
                    mine.Add(1);
                    taken[1] = true;
                }

                // <b>Its own ring, whatever the distance.</b> Walking round a
                // body means stepping from one point on that body's ring to the
                // next, and those are as far apart as the body is wide — sixty
                // to ninety metres for a regiment forty across. Nearness alone
                // does not keep them: measured on bodies forty-five metres
                // apart, every one of a corner's ten nearest places belonged to
                // *other* bodies, so the leg round its own was cut. The search
                // then found no clean route at all, fell through to the last
                // resort, and shouldered straight through the army. Going round
                // had not become dear; it had stopped existing.
                if (rings[from] != NoRing)
                {
                    for (int to = 0; to < count; to++)
                    {
                        if (taken[to] || rings[to] != rings[from]) continue;

                        mine.Add(to);
                        taken[to] = true;
                    }
                }

                // Then the nearest, which are what cross from one body to the
                // next.
                int found = 0;

                for (int to = 0; to < count; to++)
                {
                    if (taken[to]) continue;

                    order[found] = to;
                    away[found] = Vec2.Distance(places[from], places[to]);
                    found++;
                }

                Array.Sort(away, order, 0, found);

                int take = found < MostJoins ? found : MostJoins;

                for (int i = 0; i < take; i++) mine.Add(order[i]);

                joins[from] = mine.ToArray();
            }

            return joins;
        }

        /// <summary>How many steps the circle is cut into when naming a front.</summary>
        /// <remarks>
        /// Finer than the hundredth of a radian the old walk-the-list match
        /// allowed, so this can only ever keep two fronts apart that used to
        /// merge, never merge two it kept apart. A fifth of a degree is far below
        /// anything a regiment can be said to hold.
        /// </remarks>
        private const int FrontSteps = 36;

        /// <summary>A state's name: the ground it stands on, and its front.</summary>
        private static long Key(int place, Facing front)
        {
            int step = (int)MathF.Round(front.Radians / (MathF.PI * 2f) * FrontSteps) & (FrontSteps - 1);

            return ((long)place << 16) | (uint)step;
        }
    }
}
