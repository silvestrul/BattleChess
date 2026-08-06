using System;
using BattleChess.Contracts;

namespace BattleChess.Rules
{
    /// <summary>
    /// Routes the way a commander expects: straight to the destination, going
    /// around only what genuinely cannot be crossed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is what a <b>player's</b> orders use. <see cref="HexPathfinder"/>
    /// with real terrain costs minimises travel <i>time</i>, which means it will
    /// quietly refuse the line you drew and take a longer way round because the
    /// going is better — correct for an AI weighing options, and infuriating
    /// from a human's point of view. You pointed at a spot; the unit should go
    /// to that spot. If you march it through a swamp, that was your decision to
    /// make.
    /// </para>
    /// <para>
    /// Achieved by routing against <see cref="FlatMovementModel"/>, so every
    /// passable terrain costs the same and the search minimises distance rather
    /// than time. The straight line is then always the cheapest route, and
    /// smoothing collapses to it whenever it is walkable. Obstacles are still
    /// avoided, by the shortest way round rather than the fastest.
    /// </para>
    /// <para>
    /// Terrain still slows the unit down once it is marching — only the choice
    /// of route ignores cost, never the consequences of it. The reported travel
    /// time is costed against real terrain so the estimate stays honest.
    /// </para>
    /// </remarks>
    public sealed class DirectPathfinder : IPathfinder
    {
        private readonly HexPathfinder _search;
        private readonly ITerrainMap _terrain;
        private readonly IMovementModel _trueMovement;

        public DirectPathfinder(
            ITerrainMap terrain,
            IMovementModel movement,
            ITerrainCatalogue catalogue,
            float cellSpacingMetres = HexPathfinder.DefaultCellSpacingMetres,
            float? clearanceMetres = null)
        {
            _terrain = terrain ?? throw new ArgumentNullException(nameof(terrain));
            _trueMovement = movement ?? throw new ArgumentNullException(nameof(movement));

            _search = new HexPathfinder(
                terrain,
                new FlatMovementModel(movement),
                catalogue,
                cellSpacingMetres,
                clearanceMetres: clearanceMetres);
        }

        /// <summary>The grid the search runs on. Exposed for diagnostics only.</summary>
        public HexLayout SearchLayout => _search.SearchLayout;

        public PathResult FindPath(Vec2 from, Vec2 to, MovementType movementType)
        {
            PathResult route = _search.FindPath(from, to, movementType);
            if (!route.Found) return route;

            // The route was chosen ignoring terrain speed, but the march along it
            // is not. Re-cost against real terrain so "about two turns" means it.
            float spacing = _search.SearchLayout.NeighbourDistance;
            float effective = 0f;

            for (int i = 1; i < route.Waypoints.Count; i++)
            {
                effective += PathSmoother.SegmentCost(
                    route.Waypoints[i - 1], route.Waypoints[i],
                    _terrain, _trueMovement, movementType, spacing);
            }

            if (float.IsInfinity(effective) || effective <= 0f)
                effective = route.EffectiveDistance;

            return PathResult.Success(
                route.Waypoints, route.SearchCells, route.Distance, effective, route.CellsExplored);
        }
    }
}
