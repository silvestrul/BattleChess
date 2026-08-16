using System.Collections.Generic;
using BattleChess.Contracts;

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
            IBattleLog? log = null, IWayRound? wayRound = null);
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

        /// <summary>Every way of planning, for the harness that compares them.</summary>
        public static IReadOnlyList<IRoutePlanner> All { get; } = new[] { TheLadder, TheSearch };

        /// <summary>
        /// What a march uses when nobody says otherwise.
        /// </summary>
        /// <remarks>
        /// Still the ladder. It moves to the search once the search passes the
        /// gate in `ApproachAngleTests`, the three way-round tables, and the
        /// recorded arrangements — not before, and not on the strength of the
        /// reasoning being better, which is what the last four fixes had.
        /// </remarks>
        public static IRoutePlanner Default { get; } = TheLadder;

        private sealed class Search : IRoutePlanner
        {
            public string Name => "over places and fronts";

            public Plan PlanTo(
                BattleState battle, UnitInstance unit, IPathfinder pathfinder, Vec2 destination,
                IBattleLog? log = null, IWayRound? wayRound = null) =>
                RouteSearch.Find(battle, unit, destination, unit.OrderFacing, log);
        }

        private sealed class Ladder : IRoutePlanner
        {
            public string Name => "the ladder";

            public Plan PlanTo(
                BattleState battle, UnitInstance unit, IPathfinder pathfinder, Vec2 destination,
                IBattleLog? log = null, IWayRound? wayRound = null) =>
                Marching.ByTheLadder(battle, unit, pathfinder, destination, log, wayRound);
        }
    }
}
