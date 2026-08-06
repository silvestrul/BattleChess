using System;
using System.Collections.Generic;

namespace BattleChess.Contracts
{
    /// <summary>
    /// Why a route could not be found.
    /// </summary>
    /// <remarks>
    /// A bare "no route" is nearly useless when testing by hand — the useful
    /// question is always whether the destination was water, off the map, or
    /// simply too narrow for that particular regiment's frontage. Those have
    /// completely different fixes, so the search reports which it was.
    /// </remarks>
    public enum PathFailure
    {
        None = 0,

        /// <summary>The destination lies outside the battlefield.</summary>
        GoalOffMap,

        /// <summary>The destination is ground this movement type cannot enter at all.</summary>
        GoalImpassable,

        /// <summary>
        /// The destination is passable, but not with this unit's clearance —
        /// the gap is real, the regiment is simply too wide for it.
        /// </summary>
        GoalTooTight,

        /// <summary>This movement type cannot cross any terrain on this map.</summary>
        MovementTypeCannotMove,

        /// <summary>Passable ground exists at both ends, but nothing connects them.</summary>
        NoRouteExists,

        /// <summary>The search gave up before exhausting the map.</summary>
        SearchBudgetExhausted
    }

    /// <summary>
    /// A route across the battlefield.
    /// </summary>
    public sealed class PathResult
    {
        /// <summary>Whether a route exists at all.</summary>
        public bool Found { get; }

        /// <summary>
        /// The smoothed route, in world space. These are what a unit actually
        /// walks — the search grid never reaches the movement system.
        /// </summary>
        public IReadOnlyList<Vec2> Waypoints { get; }

        /// <summary>
        /// The raw search cells before smoothing. Kept for diagnostics; nothing
        /// in the simulation should consume these.
        /// </summary>
        public IReadOnlyList<Coord> SearchCells { get; }

        /// <summary>Straight-line length of the walked route, in metres.</summary>
        public float Distance { get; }

        /// <summary>
        /// Route length weighted by how slow the going is, in metres of
        /// equivalent open ground. Divide by a unit's base speed for seconds.
        /// </summary>
        /// <remarks>
        /// This, not <see cref="Distance"/>, is what the search minimises — so a
        /// long road detour correctly beats a short slog through swamp.
        /// </remarks>
        public float EffectiveDistance { get; }

        /// <summary>How many cells the search examined. Purely for profiling.</summary>
        public int CellsExplored { get; }

        /// <summary>Why no route was found. <see cref="PathFailure.None"/> on success.</summary>
        public PathFailure Failure { get; }

        /// <summary>A sentence explaining the failure, ready to show a person.</summary>
        public string FailureDetail { get; }

        private PathResult(bool found, IReadOnlyList<Vec2> waypoints, IReadOnlyList<Coord> searchCells,
            float distance, float effectiveDistance, int cellsExplored,
            PathFailure failure, string failureDetail)
        {
            Found = found;
            Waypoints = waypoints;
            SearchCells = searchCells;
            Distance = distance;
            EffectiveDistance = effectiveDistance;
            CellsExplored = cellsExplored;
            Failure = failure;
            FailureDetail = failureDetail ?? string.Empty;
        }

        public static PathResult Success(IReadOnlyList<Vec2> waypoints, IReadOnlyList<Coord> searchCells,
            float distance, float effectiveDistance, int cellsExplored) =>
            new PathResult(true, waypoints, searchCells, distance, effectiveDistance, cellsExplored,
                PathFailure.None, string.Empty);

        public static PathResult Failed(PathFailure reason, string detail, int cellsExplored) =>
            new PathResult(false, Array.Empty<Vec2>(), Array.Empty<Coord>(), 0f, 0f, cellsExplored,
                reason, detail);

        /// <summary>Seconds to walk this route at the given open-ground speed.</summary>
        public float SecondsAt(float baseSpeedMetresPerSecond)
        {
            if (!(baseSpeedMetresPerSecond > 0f))
                throw new ArgumentOutOfRangeException(nameof(baseSpeedMetresPerSecond), baseSpeedMetresPerSecond, "Speed must be positive.");

            return EffectiveDistance / baseSpeedMetresPerSecond;
        }

        public override string ToString() =>
            Found
                ? $"{Waypoints.Count} waypoints, {Distance:0} m ({EffectiveDistance:0} m effective)"
                : $"no route — {FailureDetail}";
    }

    /// <summary>
    /// Plans routes across terrain.
    /// </summary>
    /// <remarks>
    /// Callers depend on this rather than on any particular search, so the
    /// implementation can be swapped — flow fields for crowds, a cheaper search
    /// for the AI's speculative queries — without touching movement or orders.
    /// </remarks>
    public interface IPathfinder
    {
        /// <summary>
        /// Plans a route between two world positions for a given movement type.
        /// Never throws for unreachable goals — check <see cref="PathResult.Found"/>.
        /// </summary>
        PathResult FindPath(Vec2 from, Vec2 to, MovementType movementType);
    }
}
