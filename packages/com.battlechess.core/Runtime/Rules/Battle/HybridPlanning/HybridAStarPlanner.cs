using System;
using System.Collections.Generic;
using System.Linq;
using BattleChess.Contracts;
using BattleChess.Rules;

namespace BattleChess.Rules.HybridPlanning
{
    /// <summary>
    /// A search over states of (x, y, heading) rather than over places on a
    /// drawn line — the "piano mover's problem" for one rectangle, answered
    /// approximately by a state lattice, in the spirit of Hybrid A*.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Deliberately independent of <see cref="RouteSearch"/>. Nothing here
    /// touches <c>Sweep</c>, <c>OrientedRect</c>'s own overlap test, or the
    /// existing search's frontier, ledger or corner filtering — see
    /// <see cref="HybridBox"/> for why that separation matters. This file
    /// owns its own open list, its own closed set and its own collision
    /// oracle from the ground up.
    /// </para>
    /// <para>
    /// What it is not: there is no cost field here (this battlefield has no
    /// polygonal terrain-cost regions to build one from), no Reeds-Shepp
    /// lookup table for the heuristic, no anytime widening, and no
    /// space-time reservation across units — those are the multi-agent and
    /// terrain layers from the design discussion, and they sit above one
    /// planner's answer to one block's own route rather than inside it. What
    /// is here is real: motion primitives with a genuine turning radius,
    /// rotation priced as time rather than decorated onto a straight line,
    /// and a lattice closed set so the search terminates.
    /// </para>
    /// </remarks>
    internal static class HybridAStarPlanner
    {
        /// <summary>Position bin edge, in metres, for the closed set.</summary>
        private const float PositionBinMetres = 20f;

        /// <summary>How many equal slices the heading circle is split into for the closed set.</summary>
        /// <remarks>
        /// <para>
        /// Kept finer than <see cref="GoalHeadingToleranceRadians"/>. At 24
        /// bins a bin spans 15° against an 11,5° tolerance, so a state that
        /// would have satisfied the goal could be discarded in favour of a
        /// cheaper bin-mate that would not — the goal test being finer than
        /// the grid that decides which states survive to be tested.
        /// </para>
        /// <para>
        /// <b>Twelve since M71, and the size of the effect is not understood.</b>
        /// Sixteen bins to twelve takes the orders recorded as dear in play
        /// from <b>66 ms to 4,2</b>, with the Crucible's unwalkable count
        /// unmoved at 17 and Broken Country's improving from 11 to 10. A
        /// sixteen-fold cut for four bins is not what a state-space argument
        /// predicts; the likely cause is that sixteen aliases badly against the
        /// primitive set, so nearly every state lands in a bin of its own and
        /// the dominance check never fires. Twelve still spans 30° against the
        /// 11,5° goal tolerance, so the paragraph above still holds.
        /// </para>
        /// <para>
        /// <b>Do not interpolate.</b> Broken Country's unwalkable count runs
        /// 13, 10, <b>20</b>, 12, 16 as bins fall from 14 to 6. Any other value
        /// wants measuring, not reasoning about — see
        /// <c>docs/pathfinding-levers.md</c> and the <see cref="Headings"/>
        /// lever.
        /// </para>
        /// </remarks>
        private const int HeadingBins = 12;

        /// <summary>Nodes popped and expanded before the search gives up.</summary>
        /// <remarks>
        /// <para>
        /// This number is the planner's honest problem, and it is set where
        /// correctness needs it rather than where performance would like it.
        /// At 20 000 the project's approach-angle gate scores <b>nothing</b>;
        /// at 100 000 it scores thirteen of nineteen, and the six that
        /// remain are not budget failures. So the cap is not a safety valve
        /// here, it is the difference between a planner that works and one
        /// that does not, and a too-small one fails silently as "no route"
        /// rather than loudly as "too slow".
        /// </para>
        /// <para>
        /// What it costs is measured and not hidden: 56,1 s to order
        /// sixty-four regiments, against 2,2 s for the tangent search. Any
        /// decision to adopt this planner is a decision about that number,
        /// not about the bug list.
        /// </para>
        /// </remarks>
        private const int MaxExpansions = 100000;

        /// <summary>Widest gap, in metres, allowed between checked points along one primitive's sweep.</summary>
        private const float MaxSweepSpacingMetres = 2f;

        /// <summary>Overrides <see cref="MaxSweepSpacingMetres"/>. A measurement lever.</summary>
        /// <remarks>
        /// <b>Nought - the two metres above - though four is quicker.</b>
        /// Swept against the weight: at four metres and weight two the Long
        /// March is 3,7 ms an order against 4,1, the Crucible 5,8 against 6,3,
        /// Broken Country 3,1 against 3,3, with routes unmoved and nothing
        /// unwalkable across all two hundred and forty orders. It is left at
        /// two anyway. What it buys is eight percent; what it spends is the
        /// margin between the poses the planner checks and the ones the body
        /// actually occupies, and the reason nothing unwalkable came out is
        /// that the staged planner proves every route afterwards - a guard,
        /// not a licence. Eight percent is not worth being closer to the edge
        /// of it when the batch below buys three to five times as much.
        /// </remarks>
        internal static float SweepSpacing;

        /// <summary>Overrides <see cref="HeuristicWeight"/>. A measurement lever.</summary>
        /// <remarks>
        /// <b>Nought - the two above.</b> Three and four were tried again now
        /// that the estimate knows what turning costs, and the answer is that
        /// the weight has stopped being one number: three costs the Long March
        /// 4,1 ms an order to 7,0 while buying the Crucible 6,3 to 5,7, and
        /// four makes that split wider still. A weight that helps one field and
        /// hurts another by the same factor is not a setting, it is two
        /// different problems, and two is the only value that is not badly
        /// wrong on either.
        /// </remarks>
        internal static float Weight;

        /// <summary>
        /// Where the search actually went, for one search. Diagnosis only.
        /// </summary>
        /// <remarks>
        /// <c>OverlapTests / Expansions</c> already separates "explores too
        /// much" from "each expansion is too dear". These say <i>where</i> the
        /// exploring went, which is the question neither of those answers: a
        /// search that stays in the corridor between start and goal and still
        /// spends twenty thousand expansions wants a different fix from one
        /// that wanders half the map.
        /// </remarks>
        /// <summary>
        /// How far the search may leave the stretch of ground the order is
        /// about, as a multiple of the straight-line distance. 0 is unbounded,
        /// which is what it was before M71.
        /// </summary>
        /// <remarks>
        /// Measured on the Great Field: an order of <b>282 m</b> explored
        /// <b>841 m sideways and 666 m past its own ends</b> - a box some 1700 m
        /// across - for 31 640 expansions. Nothing held it near the order at
        /// all, and the route it came back with was the five-fold detour the
        /// cost ceiling then threw away. The wandering and the silly route are
        /// the same fault seen twice.
        /// </remarks>
        /// <remarks>
        /// <b>Free down to 1,25; at 1,00 quality starts going</b> — the
        /// Crucible's unwalkable count goes 17 to 20, and falls apart below
        /// that. At 1,50 the recorded orders drop from 66 to 52 ms and the
        /// Crucible's total from 1114 to 1030 with nothing else moved. Note it
        /// barely touches the <i>worst</i> order on its own: bounding where the
        /// search may go does not bound how long it may take, because the worst
        /// order runs to exhaustion inside the bound. What it does is make
        /// <see cref="StagedRoutePlanner.PoseExpansionBudget"/> cheap.
        /// See <c>docs/pathfinding-levers.md</c> for the full table.
        /// </remarks>
        internal static float StrayMultiple = 1.5f;

        /// <summary>A floor under the stray bound, so short orders keep room.</summary>
        internal static float StrayFloorMetres = 60f;

        /// <summary>Overrides <see cref="PositionBinMetres"/>. A measurement lever.</summary>
        /// <remarks>
        /// <b>The cliff is between 30 and 35 metres.</b> At 30 the Crucible is
        /// 16 unwalkable against 17 and its total falls 1114 to 856; at 35 it
        /// is 24, and Broken Country 11 to 22. Two things worth knowing before
        /// trusting a finer sweep here: 25 m is <i>slower</i> than the 20 it
        /// replaces (1164 against 1114), so the curve is not monotonic; and 60
        /// and 80 give byte-identical answers, so the bin has stopped meaning
        /// anything by then. See <c>docs/pathfinding-levers.md</c>.
        /// </remarks>
        internal static float PositionBin;

        /// <summary>Overrides <see cref="HeadingBins"/>. A measurement lever.</summary>
        /// <remarks>
        /// <para>
        /// <b>The largest single effect measured, and the least understood.</b>
        /// Sixteen bins to fourteen takes the orders recorded as dear in play
        /// from <b>66 ms to 4,1</b>. A sixteen-fold cut for one bin is not what
        /// a state-space argument predicts, and the likely cause is that
        /// sixteen aliases badly against the primitive set — nearly every state
        /// landing in a bin of its own, so the dominance check never fires.
        /// </para>
        /// <para>
        /// <b>Do not interpolate from the table.</b> Broken Country's
        /// unwalkable count runs 13, 10, <b>20</b>, 12, 16 as bins fall from 14
        /// to 6. Twelve is the best value measured; the spike at ten says the
        /// mechanism is not understood, so any other value wants measuring
        /// rather than reasoning about. See <c>docs/pathfinding-levers.md</c>.
        /// </para>
        /// </remarks>
        internal static int Headings;

        [ThreadStatic] internal static float StrayedSideways;

        /// <summary>How far past the goal, or behind the start, the search reached.</summary>
        [ThreadStatic] internal static float StrayedAlong;

        /// <summary>The straight-line distance the order asked for.</summary>
        [ThreadStatic] internal static float AskedFor;

        /// <summary>States popped on the last search, however it ended.</summary>
        /// <remarks>
        /// Instrumentation only. <see cref="Outcome.Expansions"/> is the same
        /// number, but a harness that calls <c>Marching.PlanTo</c> sees a
        /// <c>Plan</c> and never the outcome that produced it, and the orders
        /// worth measuring are the ones the staged cascade throws away.
        /// </remarks>
        [ThreadStatic] internal static int LastExpansions;

        /// <summary>The ceiling the caller put on the last search, in seconds.</summary>
        [ThreadStatic] internal static float LastLimit;

        private const float GoalPositionToleranceMetres = 20f;
        private const float GoalHeadingToleranceRadians = 0.2f; // ~11 degrees

        /// <summary>Extra clearance kept from every obstacle body, in metres.</summary>
        private const float ClearanceMarginMetres = 0.75f;

        /// <summary>
        /// How often the search stops lattice-crawling and just tries
        /// driving at the destination, in expansions.
        /// </summary>
        /// <remarks>
        /// The analytic expansion every write-up of Hybrid A* has and this
        /// one did not. A lattice of fixed-length steps has to discover an
        /// empty field one 10 m primitive at a time, and each of those steps
        /// is a full sweep against every nearby body; the shot answers the
        /// whole remaining distance in one move when nothing is in the way,
        /// which is the ordinary case for most orders. It is attempted on
        /// the first expansion and then at this interval, because it is not
        /// free — its own sweep is as long as whatever remains.
        /// </remarks>
        private const int ShootAtTheGoalEvery = 20;

        /// <summary>Overrides <see cref="ShootAtTheGoalEvery"/>. A measurement lever.</summary>
        /// <remarks>
        /// <b>Nought - the twenty above</b>, and the lever exists because the
        /// shot was a suspect and had to be cleared rather than argued about.
        /// It is not timed by <see cref="Step.HybridSearch"/>'s children, so
        /// its cost was hiding in that step's self time, where it could have
        /// been anything. Measured: 27,3 ms of an order's 496 on the Long
        /// March, 21,1 of 633 on the Crucible, 2,2 of 299 on Broken Country -
        /// three to six percent, and it ends searches early. Not a lever.
        /// </remarks>
        internal static int ShootEvery;

        /// <summary>
        /// How much the estimate of what remains is leaned on, against what
        /// has already been spent.
        /// </summary>
        /// <remarks>
        /// Weighted A*. At 1 the search is ordinary A*; above it the
        /// frontier is pushed at the goal harder and the answer may cost
        /// more than the best one available. That is a trade this planner is
        /// already in no position to refuse — see <see cref="Heuristic"/>,
        /// which is not admissible in the first place, so there is no
        /// optimality here to protect. What the weight buys is measured, and
        /// what it costs is measured with it.
        /// </remarks>
        /// <remarks>
        /// Swept on the approach-angle gate, at a budget generous enough
        /// that every weight got to finish. Expansions on the worst angle,
        /// then the route's own cost: 1 → 159 000 at 94,0 s; 1,5 → 64 000 at
        /// 95,1 s; <b>2 → 35 000 at 98,2 s</b>; 3 → 37 000 at 98,5 s; 5 →
        /// 38 000 at 160,8 s. Two is where the curve bottoms out: four and a
        /// half times fewer states for four percent more route, and past it
        /// the states stop coming down while the routes start getting
        /// visibly worse.
        /// </remarks>
        private const float HeuristicWeight = 2f;

        /// <summary>
        /// Whether the estimate comes from <see cref="HybridTurnField"/>, which
        /// solves the relaxed problem with turning in it, rather than from hop
        /// counts plus a guess at the first turn.
        /// </summary>
        internal static bool TurnAwareHeuristic = true;

        public readonly struct Outcome
        {
            public readonly bool Found;
            public readonly IReadOnlyList<Vec2> Waypoints;

            /// <summary>
            /// The front held on the leg arriving at each waypoint, one per
            /// waypoint.
            /// </summary>
            /// <remarks>
            /// A route of bare points loses most of what this planner knows.
            /// Its whole subject is (x, y, heading), and handing back only
            /// the first two leaves whoever walks the route to guess the
            /// third — <see cref="Marching.AlongTheLine"/> guesses it from
            /// the direction of the chord, which is not the front the search
            /// swept and cleared. Measured on the approach-angle gate: three
            /// angles came back as walking through a body on a route where
            /// every pose the planner actually checked was clear, purely
            /// because the checker was asking about a different facing than
            /// the planner had planned.
            /// </remarks>
            public readonly Facing?[] Fronts;
            public readonly int Expansions;
            public readonly int PrimitivesTried;
            public readonly int Obstacles;
            public readonly PathFailure Failure;

            /// <summary>
            /// What the search actually minimised, in seconds — the goal
            /// node's own accumulated cost.
            /// </summary>
            /// <remarks>
            /// Carried out because it was being computed and thrown away.
            /// Without it the wrapper had nothing to report but a polyline
            /// length, so the one planner here that prices in seconds handed
            /// the comparison harness metres, and any ranking by "cost" was
            /// reading a different quantity from this planner than from the
            /// others. Note this is <i>this planner's own</i> seconds, which
            /// is not the same currency the executor charges — see the
            /// remarks on <see cref="HybridPrimitives"/>.
            /// </remarks>
            public readonly float Seconds;

            /// <summary>Poses generated by every sweep, over the whole plan.</summary>
            /// <remarks>
            /// Instrumentation, not bookkeeping. Expansions and
            /// PrimitivesTried say how much searching happened; these two say
            /// what each unit of searching cost, and
            /// <c>OverlapTests / Expansions</c> is the number that separates
            /// "the search explores too much" from "each expansion is too
            /// expensive". Those want opposite fixes, so the counters come
            /// before the fixing.
            /// </remarks>
            public readonly int SweepSamples;

            /// <summary>Calls to <see cref="HybridBox.Overlap"/>, over the whole plan.</summary>
            public readonly int OverlapTests;

            private Outcome(
                bool found, IReadOnlyList<Vec2> waypoints, Facing?[] fronts,
                int expansions, int primitivesTried,
                int obstacles, PathFailure failure, float seconds, int sweepSamples, int overlapTests)
            {
                Found = found;
                Waypoints = waypoints;
                Fronts = fronts;
                Expansions = expansions;
                PrimitivesTried = primitivesTried;
                Obstacles = obstacles;
                Failure = failure;
                Seconds = seconds;
                SweepSamples = sweepSamples;
                OverlapTests = overlapTests;
            }

            public static Outcome Success(
                Route route, int expansions, int primitivesTried, int obstacles, float seconds,
                Tally tally) =>
                new Outcome(true, route.Waypoints, route.Fronts, expansions, primitivesTried, obstacles,
                            PathFailure.None, seconds, tally.SweepSamples, tally.OverlapTests);

            public static Outcome Failed(
                PathFailure failure, int expansions, int primitivesTried, int obstacles, Tally tally) =>
                new Outcome(false, Array.Empty<Vec2>(), Array.Empty<Facing?>(), expansions, primitivesTried,
                            obstacles, failure, 0f, tally.SweepSamples, tally.OverlapTests);
        }

        /// <summary>Points to walk, and the front to hold arriving at each of them.</summary>
        internal readonly struct Route
        {
            public readonly IReadOnlyList<Vec2> Waypoints;
            public readonly Facing?[] Fronts;

            public Route(IReadOnlyList<Vec2> waypoints, Facing?[] fronts)
            {
                Waypoints = waypoints;
                Fronts = fronts;
            }
        }

        /// <summary>Running counts of the geometry work one plan actually did.</summary>
        internal sealed class Tally
        {
            public int SweepSamples;
            public int OverlapTests;
        }

        private struct Node
        {
            public Vec2 Position;
            public Facing Heading;
            public float G;
            public int Parent;

            /// <summary>Which primitive was walked to get here, or -1 at the start.</summary>
            /// <remarks>
            /// Kept so the finished route can be handed back as the poses
            /// that were actually swept rather than as the lattice states
            /// they joined — see <see cref="Reconstruct"/>.
            /// </remarks>
            public int Via;
        }

        public static Outcome Search(
            Vec2 start, Facing startHeading, Vec2 goal, Facing? goalHeading,
            Footprint moverFootprint, IReadOnlyList<HybridBox> obstacles,
            float topSpeedMetresPerSecond, float turnRateDegreesPerSecond,
            int? expansionBudget = null, float? heuristicWeight = null,
            IReadOnlyList<Vec2>? corridor = null, float corridorHalfWidthMetres = 0f,
            float secondsLimit = 0f)
        {
            using var _profile = PlanningProfile.Measure(PlanningProfile.Step.HybridSearch);

            int budget = expansionBudget ?? MaxExpansions;
            float weight = heuristicWeight ?? (Weight > 0f ? Weight : HeuristicWeight);
            int shootEvery = ShootEvery > 0 ? ShootEvery : ShootAtTheGoalEvery;

            IReadOnlyList<HybridPrimitive> primitives =
                HybridPrimitives.For(topSpeedMetresPerSecond, turnRateDegreesPerSecond);
            // The in-radius is what the field inflates its second layer by:
            // the estimate has to know that a wide block cannot use every
            // gap a point could. See HybridObstacleField's own remarks for
            // why both layers are kept and the larger answer taken.
            float inRadius = MathF.Min(moverFootprint.HalfWidth, moverFootprint.HalfDepth);
            // Two fields answer the same question and only one of them is
            // asked. The hop-count field is still built when the estimate comes
            // from it, or when a caller wants a corridor traced — and skipped
            // outright otherwise, because at about six milliseconds a build it
            // was a quarter of an order that never read it.
            bool wantsTrace = corridor == null && corridorHalfWidthMetres > 0f;
            bool wantsHops = !TurnAwareHeuristic || wantsTrace;

            HybridObstacleField? field = null;
            HybridTurnField? turnField = null;

            using (PlanningProfile.Measure(PlanningProfile.Step.HybridField))
            {
                if (wantsHops)
                    field = HybridObstacleField.Build(start, goal, obstacles, inRadius);

                if (TurnAwareHeuristic)
                    turnField = HybridTurnField.Build(
                        start, goal, obstacles, inRadius,
                        topSpeedMetresPerSecond, turnRateDegreesPerSecond);
            }

            // A caller that asked to be bounded but named no route gets the
            // grid's own way round, which knows the topology the lattice would
            // otherwise pay tens of thousands of expansions to rediscover.
            if (wantsTrace) corridor = field!.TraceTo(start, goal);

            // How far any one primitive can carry the mover's centre. Used
            // to cull the obstacle list once per state rather than once per
            // sampled pose — see Standing.
            float reach = 0f;
            foreach (HybridPrimitive primitive in primitives)
                reach = MathF.Max(reach, MathF.Abs(primitive.Advance));

            var standing = new Standing(obstacles);

            var nodes = new List<Node>
            {
                new Node { Position = start, Heading = startHeading, G = 0f, Parent = -1, Via = -1 },
            };
            var bestAtBin = new Dictionary<long, float>();
            var open = new MinHeap();
            float startHeuristic = turnField != null
                ? turnField.SecondsFrom(
                    start, startHeading, Vec2.Distance(start, goal) / topSpeedMetresPerSecond)
                : Heuristic(
                    start, startHeading, goal, goalHeading,
                    topSpeedMetresPerSecond, turnRateDegreesPerSecond, field!);
            // The caller may already know what it is willing to pay - see M65,
            // where a way round dearer than three times the press is going to
            // be thrown away whatever it is. The turn field is an admissible
            // estimate, so a start whose *lower bound* is past the limit has
            // no answer under it, and the search is refused rather than run to
            // an answer nobody will take. Measured on the recorded orders: the
            // dear ones cost 888 to 1080 ms and were all discarded.
            float limit = secondsLimit > 0f ? secondsLimit : float.PositiveInfinity;

            LastLimit = limit;

            if (startHeuristic > limit)
            {
                LastExpansions = 0;
                return Outcome.Failed(PathFailure.NoRouteExists, 0, 0, obstacles.Count, new Tally());
            }

            open.Push(0, weight * startHeuristic, startHeuristic);
            bestAtBin[BinOf(start, startHeading)] = 0f;

            var tally = new Tally();
            int expansions = 0;
            int primitivesTried = 0;

            StrayedSideways = 0f;
            StrayedAlong = 0f;
            AskedFor = Vec2.Distance(start, goal);

            Vec2 alongTheOrder = goal - start;
            float orderLength = alongTheOrder.Length;
            Vec2 orderUnit = orderLength > Vec2.Epsilon ? alongTheOrder / orderLength : Vec2.Zero;

            while (open.Count > 0 && expansions < budget)
            {
                int at = open.Pop();
                Node node = nodes[at];

                // The bin this node lives in may since have been reached
                // more cheaply by another path — a stale queue entry rather
                // than a wrong one, so it is skipped rather than trusted.
                long bin = BinOf(node.Position, node.Heading);
                if (bestAtBin.TryGetValue(bin, out float recorded) && recorded < node.G - 1e-4f)
                    continue;

                // Costs never fall, so a path already past the limit cannot
                // come back under it. Pruned rather than expanded.
                if (node.G > limit) continue;

                expansions++;

                if (orderLength > Vec2.Epsilon)
                {
                    Vec2 fromStart = node.Position - start;
                    float along = Vec2.Dot(fromStart, orderUnit);
                    float sideways = MathF.Abs(fromStart.X * orderUnit.Y - fromStart.Y * orderUnit.X);

                    // Past the goal, or behind the start: both are the search
                    // leaving the stretch of ground the order is about.
                    float beyond = along < 0f ? -along : along - orderLength;

                    // Held to a box round the order rather than turned loose on
                    // the map. Pruned at the pop rather than at the push so the
                    // bound is asked once a state, not once an edge.
                    if (StrayMultiple > 0f)
                    {
                        float room = orderLength * StrayMultiple;
                        if (room < StrayFloorMetres) room = StrayFloorMetres;

                        if (sideways > room || beyond > room) continue;
                    }

                    if (sideways > StrayedSideways) StrayedSideways = sideways;
                    if (beyond > StrayedAlong) StrayedAlong = beyond;
                }

                if (AtGoal(node.Position, node.Heading, goal, goalHeading))
                {
                    // The last hop onto the exact destination is walked like
                    // any other, so it is checked like any other. It used to
                    // be appended unconditionally by Reconstruct, which let
                    // every plan finish with up to GoalPositionToleranceMetres
                    // of travel that no primitive proposed and no sweep ever
                    // looked at.
                    Route route = Reconstruct(
                        nodes, at, goal,
                        snapIsClear: SnapToGoalIsClear(
                            node.Position, node.Heading, goal, moverFootprint, obstacles,
                            standing, tally),
                        lastLegFront: node.Heading,
                        primitives, moverFootprint.BoundingRadius + ClearanceMarginMetres);

                    // Handed back on every way out, not only the one that
                    // failed. The field is a hundred thousand floats and most
                    // orders leave through here, so releasing it only when the
                    // search gave up meant the pool was almost never warm.
                    turnField?.Release();
                    LastExpansions = expansions;
                    return Outcome.Success(route, expansions, primitivesTried, obstacles.Count, node.G, tally);
                }

                // Drive straight at it, now and then. A shot that lands is a
                // finished route, so this is checked before the primitives
                // rather than after them.
                if (expansions == 1 || expansions % shootEvery == 0)
                {
                    float shot;
                    using (PlanningProfile.Measure(PlanningProfile.Step.HybridShot))
                    {
                        shot = ShootAtGoal(
                            node.Position, node.Heading, goal, goalHeading, moverFootprint, obstacles,
                            topSpeedMetresPerSecond, turnRateDegreesPerSecond, standing, tally);
                    }

                    if (shot >= 0f)
                    {
                        // The shot turns onto the bearing before it walks, so
                        // that is the front the last leg is held on — not
                        // whatever the lattice happened to be facing.
                        Route straight = Reconstruct(
                            nodes, at, goal, snapIsClear: true,
                            lastLegFront: Facing.Towards(node.Position, goal),
                            primitives, moverFootprint.BoundingRadius + ClearanceMarginMetres);

                        turnField?.Release();
                        LastExpansions = expansions;
                        return Outcome.Success(
                            straight, expansions, primitivesTried, obstacles.Count, node.G + shot, tally);
                    }
                }

                TakeStock(node.Position, node.Heading, moverFootprint, obstacles, reach, standing, tally);

                for (int p = 0; p < primitives.Count; p++)
                {
                    HybridPrimitive primitive = primitives[p];
                    primitivesTried++;

                    if (!IsClear(primitive, node.Position, node.Heading, moverFootprint, obstacles,
                                 standing, tally))
                        continue;

                    (Vec2 position, Facing heading) landed = primitive.ApplyTo(node.Position, node.Heading);

                    // Outside the tube a cheaper planner already proved
                    // walkable, there is nothing worth expanding. The corridor
                    // is guidance, not truth — it only bounds *where* the
                    // lattice may look, and every pose inside it is still
                    // proved against the bodies exactly as before.
                    if (corridor != null &&
                        FarFromCorridor(landed.position, corridor) > corridorHalfWidthMetres)
                        continue;

                    float g = node.G + primitive.Seconds;

                    long landedBin = BinOf(landed.position, landed.heading);
                    if (bestAtBin.TryGetValue(landedBin, out float already) && already <= g)
                        continue; // Somebody reached this bin as cheaply or cheaper already.

                    bestAtBin[landedBin] = g;
                    nodes.Add(new Node
                    {
                        Position = landed.position, Heading = landed.heading, G = g, Parent = at, Via = p,
                    });

                    using var _estimate = PlanningProfile.Measure(PlanningProfile.Step.HybridHeuristic);

                    float h = turnField != null
                        ? turnField.SecondsFrom(
                            landed.position, landed.heading,
                            Vec2.Distance(landed.position, goal) / topSpeedMetresPerSecond)
                        : Heuristic(
                            landed.position, landed.heading, goal, goalHeading,
                            topSpeedMetresPerSecond, turnRateDegreesPerSecond, field!);
                    open.Push(nodes.Count - 1, g + weight * h, h);
                }
            }

            turnField?.Release();

            LastExpansions = expansions;

            PathFailure why = expansions >= budget ? PathFailure.SearchBudgetExhausted : PathFailure.NoRouteExists;
            return Outcome.Failed(why, expansions, primitivesTried, obstacles.Count, tally);
        }

        /// <summary>
        /// An estimate of the seconds still needed. <b>Not admissible</b>,
        /// and not ε-bounded either — a greedy heuristic tuned to keep the
        /// expansion count down, which is the honest description of it.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Two terms, added: the ground still to cover, and the change of
        /// front needed before covering it. They are added rather than
        /// maxed because a regiment does both in series — it halts, turns,
        /// then walks — so the sum is what the walk actually costs, and the
        /// larger of the two is an estimate of neither.
        /// </para>
        /// <para>
        /// <b>Ground.</b> The shortest way there — straight-line in the
        /// open, or <see cref="HybridObstacleField"/>'s grid-hop distance
        /// where that has to bend round a body — over top speed.
        /// </para>
        /// <para>
        /// <b>Turning.</b> The pivot cost of coming round onto the direction
        /// the grid says the way out of this cell runs, priced through
        /// <see cref="HybridPrimitives.SecondsToPivot"/> so it agrees with
        /// what a chain of pivot primitives would really be charged. This is
        /// the term that decides whether the search finishes at all on a
        /// tight arrangement, and the reason is arithmetic: for the units in
        /// this build a 90° change of front costs about twenty seconds,
        /// while ninety metres of marching costs nineteen. A heuristic
        /// silent about turning is therefore not slightly optimistic on a
        /// route needing three such turns — it is out by a factor of four,
        /// and A* answers that by expanding every state cheaper than the
        /// truth. Measured on the project's approach-angle gate at 0°, where
        /// the mover starts jammed square against a body and has to back
        /// off before it can even rotate: without this term the search had
        /// not finished after four hundred thousand expansions.
        /// </para>
        /// <para>
        /// An earlier version of this term asked how far the heading was
        /// from the straight bearing to the goal, and was provably zero:
        /// with a step back among the primitives, "within 90° of forwards"
        /// and "within 90° of backwards" between them cover every heading
        /// there is. Asking the grid instead fixes that, because the grid's
        /// own direction is the way <i>round</i>, which a mover facing the
        /// wall is genuinely not pointing along.
        /// </para>
        /// <para>
        /// The error still runs both ways and neither bounds the other. The
        /// grid term is an 8-connected hop count, which overshoots true
        /// Euclidean by up to ~8% on a diagonal; dividing by top speed
        /// undershoots any remainder walked at
        /// <c>WheelPaceFraction</c>; and the turning term charges one change
        /// of front where a route round two bodies needs three. Kept because
        /// it is what makes the expansion counts survivable, and stated
        /// plainly because the alternative is a comment that lies about it.
        /// </para>
        /// </remarks>
        private static float Heuristic(
            Vec2 from, Facing heading, Vec2 goal, Facing? goalHeading,
            float topSpeedMetresPerSecond, float turnRateDegreesPerSecond,
            HybridObstacleField field)
        {
            float straight = Vec2.Distance(from, goal);
            float distanceMetres = MathF.Max(straight, field.EstimateMetres(from, fallback: straight));
            float byDistance = distanceMetres / topSpeedMetresPerSecond;

            Vec2 towards = field.DirectionAt(from, out Vec2 downhill)
                ? downhill
                : goal - from;

            if (towards.LengthSquared <= 1e-6f)
                return byDistance;

            float offBy = Facing.AbsoluteDelta(heading, Facing.FromVector(towards));
            float turn = HybridPrimitives.SecondsToPivot(offBy, turnRateDegreesPerSecond);

            // The front the goal was asked for, which this used to ignore
            // entirely — while AtGoal insists on it. Every state near the goal
            // therefore scored the same whichever way it pointed, so the search
            // had to expand all forty-eight heading bins around the finish to
            // discover the turn it would need. Told about it, it can prefer the
            // ones already coming round.
            //
            // Charged only as the travel runs out. Far away there is ground left
            // to turn during and the term would be a fiction; within a few
            // seconds of arriving there is not.
            if (goalHeading.HasValue)
            {
                float toFinish = Facing.AbsoluteDelta(heading, goalHeading.Value);
                float closing = 1f - MathF.Min(1f, straight / MathF.Max(1f, topSpeedMetresPerSecond * 4f));

                turn = MathF.Max(
                    turn,
                    HybridPrimitives.SecondsToPivot(toFinish, turnRateDegreesPerSecond) * closing);
            }

            return byDistance + turn;
        }

        private static bool AtGoal(Vec2 position, Facing heading, Vec2 goal, Facing? goalHeading)
        {
            if (Vec2.Distance(position, goal) > GoalPositionToleranceMetres)
                return false;

            return goalHeading == null || Facing.AbsoluteDelta(heading, goalHeading.Value) <= GoalHeadingToleranceRadians;
        }

        /// <summary>How one body is to be treated, judged from the state the mover is standing in.</summary>
        private enum Contact : byte
        {
            /// <summary>Clear of it, with room to keep the clearance margin, so the margin is kept.</summary>
            Apart = 0,

            /// <summary>
            /// Inside the margin but not inside the body. The margin is
            /// waived, the body is not — a regiment standing shoulder to
            /// shoulder with another is ordinary in close order, and the
            /// margin exists to keep a healthy distance where there is room
            /// for one, not to declare a legal position illegal.
            /// </summary>
            Touching = 1,

            /// <summary>
            /// Already inside it. Legal only while getting out — see
            /// <see cref="IsClear"/>.
            /// </summary>
            Inside = 2,
        }

        /// <summary>
        /// What the mover can see of the field from one state: which bodies
        /// are near enough for any primitive to reach, and how it stands
        /// against each of them.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Read once per expansion and reused by every primitive tried from
        /// it. Two things were being redone per sampled pose that only ever
        /// depend on the state: the cull, and the contact classification.
        /// The cull is the bigger of the two against a real army — with
        /// nineteen friendly regiments on the field, every sample of every
        /// primitive of every expansion was asking the separating-axis test
        /// about bodies no primitive could geometrically reach.
        /// </para>
        /// <para>
        /// Allocated once per plan and refilled in place, because it is
        /// touched tens of thousands of times per plan and this is not a
        /// place to be handing the collector work.
        /// </para>
        /// </remarks>
        private sealed class Standing
        {
            /// <summary>Indices into the obstacle list that any primitive from here could touch.</summary>
            public readonly List<int> Nearby = new List<int>();

            /// <summary>How the mover stands against each of them.</summary>
            public readonly Contact[] How;

            /// <summary>Separation at this state, for bodies the mover is <see cref="Contact.Inside"/>.</summary>
            public readonly float[] Depth;

            /// <summary>Separation at the previous sampled pose, while one primitive is being walked.</summary>
            public readonly float[] Behind;

            /// <summary>
            /// Each body's circumscribed radius, worked out once for the whole
            /// search rather than per pose. It was a square root inside the
            /// broad phase, taken millions of times for a number that cannot
            /// change while the plan runs.
            /// </summary>
            public readonly float[] Circum;

            /// <summary>
            /// Which bodies one primitive could touch, as against which ones any
            /// primitive from this state could. <see cref="Nearby"/> is chosen
            /// once an expansion and has to bound the furthest-reaching
            /// primitive there is — while a pivot moves the centre nowhere and a
            /// step back eight metres, and both were paying the long march's
            /// bill at every sample.
            /// </summary>
            public readonly List<int> Along = new List<int>();

            public Standing(IReadOnlyList<HybridBox> obstacles)
            {
                How = new Contact[obstacles.Count];
                Depth = new float[obstacles.Count];
                Behind = new float[obstacles.Count];
                Circum = new float[obstacles.Count];

                for (int i = 0; i < obstacles.Count; i++)
                {
                    HybridBox body = obstacles[i];
                    Circum[i] = MathF.Sqrt(
                        body.HalfWidth * body.HalfWidth + body.HalfDepth * body.HalfDepth);
                }
            }
        }

        /// <summary>
        /// The most a body a plan may already be inside is allowed to close
        /// up again over one sampled step, in metres — float noise, not
        /// licence.
        /// </summary>
        private const float EscapeToleranceMetres = 1e-3f;

        private static void TakeStock(
            Vec2 at, Facing heading, Footprint moverFootprint, IReadOnlyList<HybridBox> obstacles,
            float reach, Standing standing, Tally tally)
        {
            using var _profile = PlanningProfile.Measure(PlanningProfile.Step.HybridStock);

            standing.Nearby.Clear();

            HybridBox trueSize = HybridBox.For(at, heading, moverFootprint);
            HybridBox withMargin = HybridBox.For(at, heading, moverFootprint, ClearanceMarginMetres);

            float mine = moverFootprint.BoundingRadius + ClearanceMarginMetres;

            for (int i = 0; i < obstacles.Count; i++)
            {
                HybridBox body = obstacles[i];
                float theirs = standing.Circum[i];

                // Every primitive from here moves the mover's centre by at
                // most `reach`, and every pose it strikes lies within `mine`
                // of wherever its centre is. Anything further off than that
                // plus the body's own circumradius cannot be touched by any
                // of them, so it is not asked about again until the mover
                // has moved.
                float span = reach + mine + theirs;
                if (Vec2.DistanceSquared(at, body.Centre) > span * span)
                    continue;

                standing.Nearby.Add(i);

                tally.OverlapTests++;
                float separation = HybridBox.Separation(trueSize, body);
                standing.Depth[i] = separation;

                if (separation < 0f)
                {
                    standing.How[i] = Contact.Inside;
                    continue;
                }

                tally.OverlapTests++;
                standing.How[i] = HybridBox.Overlap(withMargin, body) ? Contact.Touching : Contact.Apart;
            }
        }

        /// <summary>
        /// Whether one primitive can be carried out from the state
        /// <paramref name="standing"/> was taken at, without meeting one of
        /// the mover's own.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>The leaving rule.</b> A body the mover is already inside is not
        /// an obstacle to getting out of it — the rest of this codebase has
        /// said so since M25, and <see cref="Marching.IsClearLine"/> takes a
        /// <c>leaving</c> flag for exactly this. This planner had no such
        /// rule, and it cost eleven of the project's nineteen approach
        /// angles: measured, the mover's own opening pose overlaps a
        /// spearmen block at every angle from 5° to 55°, so every primitive
        /// was refused at the very first state and the answer came back
        /// "no route exists" after one expansion.
        /// </para>
        /// <para>
        /// Stricter than the incumbent's version, deliberately.
        /// <see cref="Marching.IsClearLine"/> excuses such a body for the
        /// whole leg outright; here it is excused only while the mover is
        /// <i>getting out</i> — separation may hold or widen across the
        /// sweep, never narrow. That is what stops the excuse becoming a
        /// licence to march straight through: entering any deeper is
        /// refused, and you cannot come out the far side of a convex body
        /// without going deeper in first.
        /// </para>
        /// <para>
        /// One hole, stated rather than papered over: separation that holds
        /// exactly constant is allowed, so a mover already inside a body can
        /// slide along it lengthwise without ever getting deeper. That is
        /// still tighter than the leg-wide excuse the incumbent grants, and
        /// time is what the search minimises, so loitering inside somebody
        /// is priced — but it is not proved impossible.
        /// </para>
        /// </remarks>
        private static bool IsClear(
            HybridPrimitive primitive, Vec2 from, Facing heading, Footprint moverFootprint,
            IReadOnlyList<HybridBox> obstacles, Standing standing, Tally tally)
        {
            using var _profile = PlanningProfile.Measure(PlanningProfile.Step.HybridClear);

            float circumradius = moverFootprint.BoundingRadius + ClearanceMarginMetres;

            List<int> nearby = standing.Nearby;
            for (int n = 0; n < nearby.Count; n++)
            {
                int i = nearby[n];
                standing.Behind[i] = standing.Depth[i];
            }

            // Walked here rather than with foreach over Sweep, which allocates
            // an iterator for every one of the sixty thousand primitives a
            // single march tries.
            int samples = primitive.SampleCount(SweepSpacing > 0f ? SweepSpacing : MaxSweepSpacingMetres, circumradius);

            // Which bodies this one primitive could touch, decided for the whole
            // sweep before a single pose is built.
            //
            // The mover's centre travels a circular arc, so the chord from the
            // first pose to the last, widened by how far the arc leans off it,
            // contains every position the sweep will visit. A body further from
            // that chord than the two circumscribed radii cannot be reached by
            // any pose along it, whichever way either of them is pointing — so
            // it is dropped once here rather than asked about at every sample.
            (Vec2 last, Facing _) = primitive.PoseAt(from, heading, 1f);
            (Vec2 middle, Facing __) = primitive.PoseAt(from, heading, 0.5f);

            // A circular arc leans furthest off its chord at the midpoint.
            float bulge = MathF.Sqrt(FarFromSegment(middle, from, last));

            List<int> along = standing.Along;
            along.Clear();

            for (int n = 0; n < nearby.Count; n++)
            {
                int i = nearby[n];
                float apart = circumradius + standing.Circum[i] + bulge;

                if (FarFromSegment(obstacles[i].Centre, from, last) > apart * apart &&
                    standing.How[i] == Contact.Apart)
                    continue;

                along.Add(i);
            }

            if (along.Count == 0) return true;

            for (int i = 1; i <= samples; i++)
            {
                (Vec2 position, Facing sampledHeading) = primitive.PoseAt(from, heading, (float)i / samples);

                tally.SweepSamples++;

                if (!PoseIsClear(position, sampledHeading, moverFootprint, obstacles, standing, tally))
                    return false;
            }

            return true;
        }

        private static bool PoseIsClear(
            Vec2 position, Facing heading, Footprint moverFootprint,
            IReadOnlyList<HybridBox> obstacles, Standing standing, Tally tally)
        {
            List<int> nearby = standing.Along;
            if (nearby.Count == 0) return true;

            PlanningProfile.Tally(PlanningProfile.Step.HybridPose);

            HybridBox trueSize = HybridBox.For(position, heading, moverFootprint);
            HybridBox withMargin = HybridBox.For(position, heading, moverFootprint, ClearanceMarginMetres);

            // The mover's own circumscribed radius, which bounds every corner of
            // both boxes above however they point.
            float mine = moverFootprint.BoundingRadius + ClearanceMarginMetres;

            for (int n = 0; n < nearby.Count; n++)
            {
                int i = nearby[n];
                HybridBox body = obstacles[i];

                // Two circles before two rectangles.
                //
                // Nearby is chosen once per expansion and bounds what any
                // primitive from there *could* reach, so at any one pose most of
                // it is nowhere near. Every one of them was still getting a full
                // separating-axis test: measured at play scale, 6.621 expansions
                // and 59.587 primitives an order, each primitive sampled six to
                // ten times against a dozen bodies — some five million rectangle
                // tests for one march.
                //
                // Circumscribed circles bound both boxes whichever way they
                // point, so a pair too far apart to touch is turned back by one
                // subtraction and a compare, and the answer is unchanged.
                float apart = mine + standing.Circum[i];
                float between = Vec2.DistanceSquared(position, body.Centre);

                if (between > apart * apart && standing.How[i] == Contact.Apart)
                    continue;

                // The other side of the same coin — inside the sum of the two
                // inscribed radii the boxes overlap however either one is turned
                // — was built and measured and does not pay. It fires almost
                // never, because a lattice does not often propose a pose buried
                // in a body, and it costs a compare at every pose that isn't.
                // Long March 46,8 ms an order without it against 50,5 with,
                // Crucible 13,7 against 13,4, Broken Country 6,1 against 5,7.

                tally.OverlapTests++;
                PlanningProfile.Tally(PlanningProfile.Step.HybridOverlap);

                switch (standing.How[i])
                {
                    case Contact.Apart:
                        if (HybridBox.Overlap(withMargin, body)) return false;
                        break;

                    case Contact.Touching:
                        if (HybridBox.Overlap(trueSize, body)) return false;
                        break;

                    default:
                    {
                        float separation = HybridBox.Separation(trueSize, body);
                        if (separation < standing.Behind[i] - EscapeToleranceMetres) return false;
                        standing.Behind[i] = separation;
                        break;
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// What it would cost to turn towards the destination, walk straight
        /// to it and turn onto the ordered front — or a negative number if
        /// something is in the way of doing that.
        /// </summary>
        /// <remarks>
        /// Three moves, each swept and each priced in the same currency the
        /// primitives use, so a shot that wins wins on its merits rather
        /// than on being costed by a different rule. Not a Dubins or
        /// Reeds-Shepp curve — this planner's mover halts to change front
        /// rather than sweeping through one, so pivot-march-pivot is the
        /// shape its own repertoire can actually walk, and a curve would be
        /// a route it could not.
        /// </remarks>
        private static float ShootAtGoal(
            Vec2 from, Facing heading, Vec2 goal, Facing? goalHeading, Footprint moverFootprint,
            IReadOnlyList<HybridBox> obstacles, float topSpeedMetresPerSecond, float turnRateDegreesPerSecond,
            Standing standing, Tally tally)
        {
            float distance = Vec2.Distance(from, goal);
            float circumradius = moverFootprint.BoundingRadius + ClearanceMarginMetres;

            Facing bearing = distance > 0.05f ? Facing.Towards(from, goal) : heading;

            float ontoTheLine = Facing.SignedDelta(heading, bearing);
            float ontoTheFront = goalHeading == null ? 0f : Facing.SignedDelta(bearing, goalHeading.Value);

            // The turn away from here has to clear the ground the mover is
            // standing on, so its stock is taken here; the march and the
            // final turn are read from the goal end, where the mover will be.
            TakeStock(from, heading, moverFootprint, obstacles, distance, standing, tally);

            var swing = new HybridPrimitive(0f, ontoTheLine, 0f, "shot: onto the line");
            if (!IsClear(swing, from, heading, moverFootprint, obstacles, standing, tally))
                return -1f;

            var run = new HybridPrimitive(distance, 0f, 0f, "shot: the run in");
            if (!IsClear(run, from, bearing, moverFootprint, obstacles, standing, tally))
                return -1f;

            if (goalHeading != null)
            {
                TakeStock(goal, bearing, moverFootprint, obstacles, 0f, standing, tally);

                var dress = new HybridPrimitive(0f, ontoTheFront, 0f, "shot: onto the front");
                if (!IsClear(dress, goal, bearing, moverFootprint, obstacles, standing, tally))
                    return -1f;
            }

            return HybridPrimitives.SecondsToPivot(ontoTheLine, turnRateDegreesPerSecond)
                 + distance / topSpeedMetresPerSecond
                 + HybridPrimitives.SecondsToPivot(ontoTheFront, turnRateDegreesPerSecond);
        }

        /// <summary>
        /// Whether the short hop from the last lattice state onto the exact
        /// destination can actually be walked.
        /// </summary>
        private static bool SnapToGoalIsClear(
            Vec2 from, Facing heading, Vec2 goal, Footprint moverFootprint,
            IReadOnlyList<HybridBox> obstacles, Standing standing, Tally tally)
        {
            float distance = Vec2.Distance(from, goal);
            if (distance <= 0.05f) return true;

            // The cull and the contact reading are taken afresh here rather
            // than inherited: this hop is not one of the primitives, so the
            // reach it needs is its own length.
            TakeStock(from, heading, moverFootprint, obstacles, distance, standing, tally);

            List<int> nearby = standing.Nearby;
            for (int n = 0; n < nearby.Count; n++)
            {
                int i = nearby[n];
                standing.Behind[i] = standing.Depth[i];
            }

            int samples = Math.Max(2, (int)MathF.Ceiling(distance / MaxSweepSpacingMetres));

            for (int i = 1; i <= samples; i++)
            {
                Vec2 at = Vec2.Lerp(from, goal, (float)i / samples);
                tally.SweepSamples++;

                if (!PoseIsClear(at, heading, moverFootprint, obstacles, standing, tally))
                    return false;
            }

            return true;
        }

        /// <summary>Square of the metres from a point to the nearest point of a segment.</summary>
        private static float FarFromSegment(Vec2 at, Vec2 from, Vec2 to)
        {
            Vec2 span = to - from;
            float length = span.LengthSquared;

            float along = length <= Vec2.Epsilon ? 0f : Vec2.Dot(at - from, span) / length;

            if (along < 0f) along = 0f;
            else if (along > 1f) along = 1f;

            return Vec2.DistanceSquared(at, from + span * along);
        }

        /// <summary>Metres from a point to the nearest point of a polyline.</summary>
        private static float FarFromCorridor(Vec2 at, IReadOnlyList<Vec2> line)
        {
            float nearest = float.MaxValue;

            for (int i = 1; i < line.Count; i++)
            {
                Vec2 from = line[i - 1];
                Vec2 span = line[i] - from;
                float length = span.LengthSquared;

                float along = length <= Vec2.Epsilon
                    ? 0f
                    : Vec2.Dot(at - from, span) / length;

                if (along < 0f) along = 0f;
                else if (along > 1f) along = 1f;

                float gap = Vec2.DistanceSquared(at, from + span * along);
                if (gap < nearest) nearest = gap;
            }

            return nearest == float.MaxValue ? 0f : MathF.Sqrt(nearest);
        }

        /// <summary>
        /// The lattice cell a pose falls in, packed into one integer.
        /// </summary>
        /// <remarks>
        /// It was a <c>(int, int, int)</c> tuple used as a dictionary key, which
        /// hashes three fields and compares three fields on every probe — and
        /// the search probes twice per successor over tens of thousands of
        /// expansions. Measured, the loop around the geometry was <b>27 to 30%
        /// of an order</b>, more than the obstacle field and the whole ladder
        /// together. The three parts fit in a long with room to spare: headings
        /// are one of 48, and 21 bits of position at the bin size in use spans
        /// far more ground than any battlefield.
        /// </remarks>
        private static long BinOf(Vec2 position, Facing heading)
        {
            float bin = PositionBin > 0f ? PositionBin : PositionBinMetres;

            long ix = (long)MathF.Floor(position.X / bin);
            long iy = (long)MathF.Floor(position.Y / bin);

            int bins = Headings > 0 ? Headings : HeadingBins;

            float turn = 2f * MathF.PI / bins;
            int ith = (int)MathF.Round(heading.Radians / turn) % bins;
            if (ith < 0) ith += bins;

            return (((ix & 0x1FFFFF) << 42) | ((iy & 0x1FFFFF) << 21) | (uint)ith);
        }

        /// <summary>
        /// The finished route, as points to walk and the front to hold
        /// arriving at each.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A wheel is walked as an arc, and a <see cref="Plan"/> has no way
        /// to say "arc" — its legs are straight and each holds one front.
        /// Handing back one waypoint per lattice state and one front per leg
        /// therefore describes a route the planner never checked: the chord
        /// of the arc, held at a single facing. Measured on the
        /// approach-angle gate, both halves of that mismatch cost angles.
        /// Naming no front at all made three angles read as walking through
        /// a body, because the walker guessed the facing from the chord;
        /// naming the arriving front made five, because a fixed front across
        /// an arc is a different wrong answer.
        /// </para>
        /// <para>
        /// So a turning primitive is handed back as the chain of poses its
        /// own sweep struck — the poses the collision test actually cleared.
        /// The route gets longer and every leg in it is straight, at a
        /// front, which is what the rest of this codebase can walk. This is
        /// the same reasoning the design notes reached when they dropped
        /// curves outright: a plan has to be expressed in the terms the walk
        /// uses, or the two disagree about what was agreed.
        /// </para>
        /// </remarks>
        private static Route Reconstruct(
            List<Node> nodes, int at, Vec2 goal, bool snapIsClear, Facing lastLegFront,
            IReadOnlyList<HybridPrimitive> primitives, float circumradius)
        {
            var chain = new List<int>();
            for (int i = at; i >= 0; i = nodes[i].Parent)
                chain.Add(i);
            chain.Reverse();

            var raw = new List<(Vec2 position, Facing heading)>
            {
                (nodes[chain[0]].Position, nodes[chain[0]].Heading),
            };

            for (int step = 1; step < chain.Count; step++)
            {
                Node before = nodes[chain[step - 1]];
                Node after = nodes[chain[step]];

                HybridPrimitive primitive = primitives[after.Via];

                if (MathF.Abs(primitive.TurnRadians) > 1e-4f && MathF.Abs(primitive.Advance) > 1e-4f)
                {
                    foreach ((Vec2 position, Facing heading) pose in
                             primitive.Sweep(before.Position, before.Heading, MaxSweepSpacingMetres, circumradius))
                        raw.Add(pose);
                }
                else
                {
                    raw.Add((after.Position, after.Heading));
                }
            }

            // A pivot moves the heading but not the position, so a chain of
            // them reconstructs as the same point repeated — a zero-length
            // leg nothing downstream expects. Collapsed here rather than
            // left for whoever walks the route to trip over.
            var waypoints = new List<Vec2> { raw[0].position };
            var fronts = new List<Facing?> { raw[0].heading };

            for (int i = 1; i < raw.Count; i++)
            {
                if (Vec2.Distance(raw[i].position, waypoints[waypoints.Count - 1]) > 0.01f)
                {
                    waypoints.Add(raw[i].position);
                    fronts.Add(raw[i].heading);
                }
                // A pivot lands on the point it started from. The front it
                // turned to belongs to the leg *leaving* here, which is the
                // next one along and names its own; overwriting the front of
                // the leg that arrived would label a march already walked
                // with a facing only taken up once it had finished.

            }

            // Snap the finish onto the exact destination rather than
            // whichever lattice sample happened to land inside tolerance —
            // the same courtesy a straight-line cast gives for free, but
            // only when that last hop was checked and found walkable.
            // Otherwise the route stops on the last state actually proven
            // legal, inside the goal tolerance, rather than claiming ground
            // nothing verified.
            Vec2 last = waypoints[waypoints.Count - 1];
            if (snapIsClear && Vec2.Distance(last, goal) > 0.05f)
            {
                waypoints.Add(goal);
                fronts.Add(lastLegFront);
            }

            return new Route(waypoints, fronts.ToArray());
        }

        /// <summary>
        /// A small binary min-heap over (nodeIndex, f, h), built for this
        /// planner alone.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Ordered on <c>f</c> first and <c>h</c> second, and the second key
        /// is not a tidiness: it is the single largest saving in this file.
        /// A march primitive costs its own length over top speed and buys
        /// back exactly that much distance-to-goal, so on open ground
        /// <c>f</c> does not move at all along a march. Every route made of
        /// marches therefore ties on <c>f</c>, and a heap that breaks those
        /// ties arbitrarily works its way across the whole plateau breadth
        /// first before it will finish any one route to the end.
        /// </para>
        /// <para>
        /// Preferring the smaller <c>h</c> among equals — equivalently the
        /// larger <c>g</c>, since they sum to the same <c>f</c> — sends the
        /// search down one route to the goal instead. Measured on the
        /// project's own approach-angle gate: see the note in
        /// <c>docs/DECISIONS.md</c>.
        /// </para>
        /// </remarks>
        private sealed class MinHeap
        {
            private readonly List<(int node, float f, float h)> _items = new List<(int, float, float)>();

            public int Count => _items.Count;

            private bool Before(int a, int b)
            {
                if (_items[a].f != _items[b].f) return _items[a].f < _items[b].f;
                return _items[a].h < _items[b].h;
            }

            public void Push(int node, float f, float h)
            {
                _items.Add((node, f, h));
                int i = _items.Count - 1;
                while (i > 0)
                {
                    int parent = (i - 1) / 2;
                    if (!Before(i, parent)) break;
                    (_items[parent], _items[i]) = (_items[i], _items[parent]);
                    i = parent;
                }
            }

            public int Pop()
            {
                (int node, float f, float h) top = _items[0];
                int last = _items.Count - 1;
                _items[0] = _items[last];
                _items.RemoveAt(last);

                int i = 0;
                while (true)
                {
                    int left = i * 2 + 1;
                    int right = i * 2 + 2;
                    int smallest = i;

                    if (left < _items.Count && Before(left, smallest)) smallest = left;
                    if (right < _items.Count && Before(right, smallest)) smallest = right;
                    if (smallest == i) break;

                    (_items[smallest], _items[i]) = (_items[i], _items[smallest]);
                    i = smallest;
                }

                return top.node;
            }
        }
    }
}
