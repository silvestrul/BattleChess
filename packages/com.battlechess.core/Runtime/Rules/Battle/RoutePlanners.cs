using System.Collections.Generic;
using BattleChess.Contracts;
using BattleChess.Rules.HybridPlanning;

namespace BattleChess.Rules
{
    /// <summary>
    /// A way of working out how a regiment gets somewhere.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The same shape as <see cref="IWayRound"/>, and for the same reason. Two
    /// planners that answer the same question can be put against the same
    /// arrangement and both answers printed, which is the difference between
    /// believing the new one is better and knowing it. That harness is what
    /// settled which way round to go, and it is the single thing most likely to
    /// stop this rewrite going the way the last four fixes went.
    /// </para>
    /// </remarks>
    public interface IRoutePlanner
    {
        string Name { get; }

        Plan PlanTo(
            BattleState battle, UnitInstance unit, IPathfinder pathfinder, Vec2 destination,
            IBattleLog? log = null, IWayRound? wayRound = null, Facing? arriveOn = null);
    }

    /// <summary>The ways of planning a march, and the one used by default.</summary>
    public static class RoutePlanners
    {
        /// <summary>
        /// <b>M18</b>'s ladder: four things tried in order, and only in order.
        /// </summary>
        public static readonly IRoutePlanner TheLadder = new Ladder();

        /// <summary>
        /// <b>M31</b>: one search over places and fronts, everything priced in
        /// seconds.
        /// </summary>
        public static readonly IRoutePlanner TheSearch = new Search();

        /// <summary>
        /// <b>M36</b>: the same search over a reduced visibility graph — inflated
        /// corners, joined only where the leg is tangent to both.
        /// </summary>
        public static readonly IRoutePlanner TheTangents = new Tangents();

        /// <summary>
        /// Half of <b>M36</b>: the corners, joined the old way. Here to say which
        /// half of the change any difference belongs to.
        /// </summary>
        public static readonly IRoutePlanner TheCorners =
            new Shaped("over corners", RouteSearch.Shape.Corners);

        /// <summary>
        /// A prototype: Hybrid A* over states of (x, y, heading), independent
        /// of everything above. See
        /// <see cref="HybridPlanning.HybridAStarRoutePlanner"/>.
        /// Not the default — put here to be measured, not trusted yet.
        /// </summary>
        public static readonly IRoutePlanner TheHybridAStar = new HybridAStarRoutePlanner();

        /// <summary>
        /// Ladder-first planner that proves a route can be walked, stages a
        /// regiment out of an opening overlap, and only then asks tangents to
        /// recover a route the ladder cannot make safe.
        /// </summary>
        public static readonly IRoutePlanner TheStaged = new StagedRoutePlanner();

        /// <summary>Every way of planning, for the harness that compares them.</summary>
        public static IReadOnlyList<IRoutePlanner> All { get; } =
            new[] { TheLadder, TheSearch, TheCorners, TheTangents, TheHybridAStar, TheStaged };

        /// <summary>
        /// What a march uses when nobody says otherwise.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>The search</b>, on the designer's call, once it cleared the gate
        /// the ladder fails: nineteen approach angles across one 30 m gap, of
        /// which the ladder routes seven.
        /// </para>
        /// <para>
        /// Switched before the remaining gates rather than after them, and for a
        /// good reason: every real fault in this sweep was found by playing, not
        /// by testing. Four fixes in a row passed their own tests and failed in
        /// the next recording. The ladder stays in the tree, one line away, until
        /// the recordings agree.
        /// </para>
        /// <para>
        /// <b>Over tangents since M36</b>, which is the same search over the same
        /// places with the legs that cannot be on any route left unnamed. Taken
        /// on the numbers: identical on the angle gate at 17 of 19, identical on
        /// the suite at 563 passing and 11 failing over two runs, and 2.2 times
        /// quicker on sixty-four regiments ordered at once for 2.9 times fewer
        /// legs priced. A pruning that changes no answer is worth having whether
        /// or not the rest of the pass lands.
        /// </para>
        /// </remarks>
        /// <summary>
        /// <b><see cref="TheStaged"/>, and the trial is over.</b>
        /// </summary>
        /// <remarks>
        /// <para>
        /// Named here rather than behind a debug toggle on purpose. Both re-plan
        /// sites in <c>OrderSystem</c> call <c>Marching.PlanTo</c> without
        /// naming a planner, so a toggle on the <i>order</i> would give a
        /// regiment one planner's route and then re-plan it with another's the
        /// moment it was held up — which is not a trial of anything.
        /// </para>
        /// <para>
        /// <b>What to expect, measured least of three in Release, eighty
        /// regiments an order.</b> One at a time: <b>3,1 ms</b> an order on the
        /// Long March, <b>4,5</b> on the Crucible, <b>2,4</b> on Broken
        /// Country. Planned as a wing on twelve cores: <b>1,1 / 1,8 / 1,2</b>
        /// including the placement search a real click also pays for. It routes
        /// <b>80 of 80</b> on all three fields, and — the number that decides
        /// whether the route is worth anything — <b>0 unwalkable and 0
        /// press-throughs</b>, where the ladder and the tangent search each
        /// leave 28 to 34 routes the executor will refuse. See
        /// <see cref="StagedRoutePlanner"/> for how it gets there and
        /// <c>docs/DECISIONS.md</c> M55 to M64 for what it cost to.
        /// </para>
        /// <para>
        /// <b>An attack is still planned over tangents</b>, from inside
        /// <see cref="StagedRoutePlanner"/> itself, because the chase cadence
        /// re-plans against a target that moves and wants a stable route shape.
        /// So a march is a test of this and an attack is not.
        /// </para>
        /// </remarks>
        public static IRoutePlanner Default { get; } = TheStaged;

        /// <summary>The same thing. Kept while callers that name it are tidied up.</summary>
        public static IRoutePlanner SettledDefault { get; } = TheStaged;

        /// <summary>
        /// The planner a march that names none actually gets.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>[M147].</b> <see cref="Default"/> says what the continuous game
        /// settled on and never changes; this says which game is being played.
        /// The grid mode swaps it for <c>BoardRoutePlanner</c> and swaps it back,
        /// which is the whole of how a board game and a continuous one live in
        /// one codebase.
        /// </para>
        /// <para>
        /// One seam and not two, for the reason recorded on <see cref="Default"/>
        /// itself: both re-plan sites in <c>OrderSystem</c> ask without naming a
        /// planner, so a regiment given a board route and re-planned by the
        /// staged planner the moment it was held up would be playing neither
        /// game.
        /// </para>
        /// </remarks>
        public static IRoutePlanner InUse { get; set; } = TheStaged;

        private sealed class Search : IRoutePlanner
        {
            public string Name => "over places and fronts";

            /// <remarks>
            /// <paramref name="arriveOn"/> falls back to the unit's own
            /// <see cref="UnitInstance.OrderFacing"/>, which is only right once
            /// the order exists. Planning a march <i>before</i> giving the order
            /// — which is what the Unity harness does, and what a route preview
            /// does — reads the front of the <b>previous</b> order, and the
            /// search then buys that front with ground: measured from a recorded
            /// click, eight waypoints and a hook out past the destination and
            /// back, against five when told the front the order was about to
            /// set. Callers who know the pending front must say so.
            /// </remarks>
            public Plan PlanTo(
                BattleState battle, UnitInstance unit, IPathfinder pathfinder, Vec2 destination,
                IBattleLog? log = null, IWayRound? wayRound = null, Facing? arriveOn = null) =>
                RouteSearch.Find(battle, unit, destination, arriveOn ?? unit.OrderFacing, log, pathfinder);
        }

        private sealed class Tangents : IRoutePlanner
        {
            public string Name => "over tangents";

            public Plan PlanTo(
                BattleState battle, UnitInstance unit, IPathfinder pathfinder, Vec2 destination,
                IBattleLog? log = null, IWayRound? wayRound = null, Facing? arriveOn = null) =>
                RouteSearch.Find(
                    battle, unit, destination, arriveOn ?? unit.OrderFacing, log, pathfinder,
                    RouteSearch.Shape.Tangents);
        }

        /// <summary>The same search, over whichever shape of graph it is given.</summary>
        private sealed class Shaped : IRoutePlanner
        {
            private readonly RouteSearch.Shape _shape;

            public Shaped(string name, RouteSearch.Shape shape)
            {
                Name = name;
                _shape = shape;
            }

            public string Name { get; }

            public Plan PlanTo(
                BattleState battle, UnitInstance unit, IPathfinder pathfinder, Vec2 destination,
                IBattleLog? log = null, IWayRound? wayRound = null, Facing? arriveOn = null) =>
                RouteSearch.Find(
                    battle, unit, destination, arriveOn ?? unit.OrderFacing, log, pathfinder, _shape);
        }

        private sealed class Ladder : IRoutePlanner
        {
            public string Name => "the ladder";

            /// <remarks>
            /// The ladder plans a line and leaves the wheel to the steering
            /// (<b>M24</b>), so it has no use for an arrival front.
            /// </remarks>
            public Plan PlanTo(
                BattleState battle, UnitInstance unit, IPathfinder pathfinder, Vec2 destination,
                IBattleLog? log = null, IWayRound? wayRound = null, Facing? arriveOn = null) =>
                Marching.ByTheLadder(battle, unit, pathfinder, destination, log, wayRound);
        }
    }
}
