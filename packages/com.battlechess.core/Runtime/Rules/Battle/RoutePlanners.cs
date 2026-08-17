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

        /// <summary>Every way of planning, for the harness that compares them.</summary>
        public static IReadOnlyList<IRoutePlanner> All { get; } = new[] { TheLadder, TheSearch };

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
        /// </remarks>
        public static IRoutePlanner Default { get; } = TheSearch;

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
                RouteSearch.Find(battle, unit, destination, arriveOn ?? unit.OrderFacing, log);
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
