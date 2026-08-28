using System;
using System.Collections.Generic;
using BattleChess.Contracts;

namespace BattleChess.Rules.GridPlanning
{
    /// <summary>Where the regiment grid sits in the cascade, if anywhere.</summary>
    public enum GridUse
    {
        /// <summary>Not asked at all. The cascade as it shipped.</summary>
        Off,

        /// <summary>
        /// Asked before the lattice. Its route is taken if it walks cleanly,
        /// and the lattice runs unchanged if it does not, so the grid can
        /// supply an answer but can never withhold one.
        /// </summary>
        Stage,

        /// <summary>
        /// Not taken as a route at all, but handed to the lattice as the tube
        /// it may search inside.
        /// </summary>
        Corridor,

        /// <summary>
        /// Asked instead of the lattice, which is not run at all. The fallback
        /// is the press.
        /// </summary>
        Replace,
    }

    /// <summary>
    /// <see cref="RegimentGrid"/> wearing the same <see cref="IRoutePlanner"/>
    /// coat as every other way of planning, so it can be put on the field next
    /// to the rest and measured rather than taken on faith.
    /// </summary>
    public sealed class GridRoutePlanner : IRoutePlanner
    {
        /// <summary>Which of the three arrangements is in force.</summary>
        /// <remarks>
        /// Public rather than internal because the host sets it: this is the
        /// lever a play-test moves, and the whole point of building all three
        /// was that they be switchable while the battle is running. At
        /// <see cref="GridUse.Stage"/> because that is the only one of the
        /// three that cannot make an order worse - it adds a route where there
        /// was none and withholds nothing.
        /// </remarks>
        public static GridUse Use = GridUse.Stage;

        public string Name => "A* over a grid of regiment-sized cells";

        public Plan PlanTo(
            BattleState battle, UnitInstance unit, IPathfinder pathfinder, Vec2 destination,
            IBattleLog? log = null, IWayRound? wayRound = null, Facing? arriveOn = null) =>
            PlanOver(battle, unit, destination, arriveOn, log);

        /// <summary>Grids built, how many found a route, and how many held.</summary>
        /// <remarks>Public for the same reason as <see cref="Use"/>: the host reads them.</remarks>
        public static int Asked, Found, Held;

        /// <summary>The fine tier of M87: asked, found a route, and kept it.</summary>
        public static int FineAsked, FineFound, FineHeld;

        /// <summary>Nodes taken out because the regiment could not stand there - M94.</summary>
        public static int NodesDropped;

        /// <summary>
        /// Whether a node the regiment could not stand at is taken out of the
        /// route. <b>Off</b> - see M94: correct in itself and not shown safe.
        /// </summary>
        public static bool DropUnstandableNodes;

        internal static void ResetCounters() =>
            Asked = Found = Held = FineAsked = FineFound = FineHeld = NodesDropped = 0;

        /// <summary>
        /// Lays a grid, searches it, and hands back the route as a plan the
        /// rest of the cascade can weigh against any other.
        /// </summary>
        /// <remarks>
        /// The route is <b>not</b> checked here against
        /// <c>StagedRoutePlanner.WalksCleanly</c>. A grid cell is coarser than
        /// the swept rectangle that gate uses, so a grid route is a claim about
        /// roughly where to go and not a promise that the regiment fits; the
        /// caller decides what to do with it, and what a stage does with it
        /// differs from what a corridor does. Keeping the gate out of here is
        /// also what lets the corridor use exist at all, since a route being
        /// used as guidance has no business passing a gate about walking.
        /// </remarks>
        public static Plan PlanOver(
            BattleState battle, UnitInstance unit, Vec2 destination, Facing? arriveOn,
            IBattleLog? log = null)
        {
            if (battle == null) throw new ArgumentNullException(nameof(battle));
            if (unit == null) throw new ArgumentNullException(nameof(unit));

            Asked++;

            IReadOnlyList<Vec2>? points = RouteFor(battle, unit, destination);

            if (points == null)
            {
                log?.Record(new BattleLogEntry(
                    LogLevel.Blocked, "Path",
                    $"{unit.Def.DisplayName} found no grid route to {destination} " +
                    $"({RegimentGrid.LastCellsExplored} cells settled, " +
                    $"{RegimentGrid.LastBlockedCells} held by bodies).",
                    unit.Id));

                return new Plan(
                    PathResult.Failed(
                        PathFailure.NoRouteExists,
                        "No route over the regiment grid. Every way round is blocked at cell scale.",
                        RegimentGrid.LastCellsExplored),
                    null, false);
            }

            Found++;

            float length = Length(points);

            return new Plan(
                PathResult.Success(points, Array.Empty<Coord>(), length, length,
                    RegimentGrid.LastCellsExplored),
                null, false);
        }

        /// <summary>
        /// The bare line the grid found, for anything that wants guidance
        /// rather than a plan - the lattice corridor being the one that does.
        /// </summary>
        public static IReadOnlyList<Vec2>? RouteFor(
            BattleState battle, UnitInstance unit, Vec2 destination,
            float? spacingMultiple = null)
        {
            // The line it is about to walk, which is what M24 says every
            // question about clearance should be asked of. Ignored entirely
            // unless the halo is a rectangle.
            // Which tier this call is, so the field and the search below can
            // book themselves to the right line. Restored rather than cleared,
            // because the fine tier reaches this through the coarse one's own
            // call site and a flat reset would mislabel the outer call.
            bool wasFine = RegimentGrid.OnTheFineTier;
            RegimentGrid.OnTheFineTier = spacingMultiple.HasValue;

            RegimentGrid grid;
            List<Vec2> points;

            try
            {
                grid = RegimentGrid.For(
                    battle, unit, Marching.AlongTheLine(unit.Position, destination, unit.Facing),
                    spacingMultiple);

                if (!grid.TryRoute(unit.Position, destination, out points)) return null;
            }
            finally
            {
                RegimentGrid.OnTheFineTier = wasFine;
            }

            if (DropUnstandableNodes) DropNodesTheBodyDoesNotFit(battle, unit, points);

            return points;
        }

        /// <summary>
        /// Takes out any node the regiment could not actually stand at.
        /// </summary>
        /// <remarks>
        /// <b>M94.</b> <see cref="RegimentGrid.NodeAt"/> names a cell's node as
        /// the average of its <i>free sample points</i>, and the average of free
        /// points is not necessarily a place the rectangle fits - a cell free
        /// along one edge and covered along another has its centroid somewhere
        /// in between. That was harmless while the end cells were thrown away,
        /// and stopped being harmless when <b>M90</b> started keeping them,
        /// because the cell a regiment stands in is precisely the one most
        /// likely to be half covered. At the failing approach it put the first
        /// waypoint <b>inside the body the route was drawn to avoid</b>, and the
        /// gate then refused the whole route on its first leg.
        /// <para>
        /// The ends are never dropped: they are where the regiment is and where
        /// it was sent, and neither is the grid's to revise.
        /// </para>
        /// </remarks>
        private static void DropNodesTheBodyDoesNotFit(
            BattleState battle, UnitInstance unit, List<Vec2> points)
        {
            for (int at = points.Count - 2; at >= 1; at--)
            {
                Facing front = Marching.AlongTheLine(points[at - 1], points[at], unit.Facing);

                if (StagedRoutePlanner.CouldStandAt(battle, unit, points[at], front)) continue;

                NodesDropped++;
                points.RemoveAt(at);
            }
        }

        internal static float Length(IReadOnlyList<Vec2> points)
        {
            float total = 0f;

            for (int i = 1; i < points.Count; i++) total += Vec2.Distance(points[i - 1], points[i]);

            return total;
        }
    }
}
