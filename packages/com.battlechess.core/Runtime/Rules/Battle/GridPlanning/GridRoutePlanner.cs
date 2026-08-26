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

        internal static void ResetCounters() => Asked = Found = Held = 0;

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
            BattleState battle, UnitInstance unit, Vec2 destination)
        {
            // The line it is about to walk, which is what M24 says every
            // question about clearance should be asked of. Ignored entirely
            // unless the halo is a rectangle.
            RegimentGrid grid = RegimentGrid.For(
                battle, unit, Marching.AlongTheLine(unit.Position, destination, unit.Facing));

            return grid.TryRoute(unit.Position, destination, out List<Vec2> points) ? points : null;
        }

        internal static float Length(IReadOnlyList<Vec2> points)
        {
            float total = 0f;

            for (int i = 1; i < points.Count; i++) total += Vec2.Distance(points[i - 1], points[i]);

            return total;
        }
    }
}
