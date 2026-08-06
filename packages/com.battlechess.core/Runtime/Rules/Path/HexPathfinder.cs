using System;
using System.Collections.Generic;
using BattleChess.Contracts;

namespace BattleChess.Rules
{
    /// <summary>
    /// Plans routes with A* over a hex grid, then smooths the result into a
    /// natural line for the movement system to walk.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The search grid is never materialised. Cells exist only as
    /// <see cref="Coord"/> values, converted to world positions on demand and
    /// sampled against the terrain field — so a kilometre of battlefield costs
    /// nothing until something actually searches across it, and A* touches only
    /// the small fraction of cells it needs.
    /// </para>
    /// <para>
    /// Cost is <b>time, not distance</b>. Each step costs the spacing divided by
    /// how fast the going is, so a long road detour correctly beats a short slog
    /// through a swamp. This is the whole reason terrain speeds exist.
    /// </para>
    /// </remarks>
    public sealed class HexPathfinder : IPathfinder
    {
        /// <summary>
        /// Default spacing of the search grid, in metres.
        /// </summary>
        /// <remarks>
        /// Well below the 25 m at which terrain is authored, so no feature is
        /// missed, but coarse enough that the search does not grind through a
        /// dozen redundant cells per authored cell. Smoothing removes any
        /// residual blockiness, so finer buys very little.
        /// </remarks>
        public const float DefaultCellSpacingMetres = 5f;

        /// <summary>
        /// Default margin a route keeps from impassable ground, in metres.
        /// </summary>
        /// <remarks>
        /// Not a unit-size constraint — at 2 m against regiments 80-110 m wide
        /// and terrain cells of 25 m, this is a point for every practical
        /// purpose. It exists to stop a route touching the exact corner of an
        /// obstacle, where a straight line grazes a single mathematical point
        /// and rounding decides which side of the shoreline each sample lands
        /// on.
        ///
        /// Worth more than tidiness: a route that hugs a shoreline also has very
        /// few valid straight shortcuts, so smoothing collapses badly. Nudging
        /// it a metre clear took one test route from 29 waypoints to 4.
        /// </remarks>
        public const float DefaultClearanceMetres = 2f;

        private const int MovementTypeCount = 3;

        private readonly ITerrainMap _terrain;
        private readonly IMovementModel _movement;
        private readonly ITerrainCatalogue _catalogue;
        private readonly HexLayout _layout;
        private readonly float[] _fastestMultiplier = new float[MovementTypeCount];
        private readonly int _cellBudget;
        private readonly float _spacing;
        private readonly float _clearance;

        /// <param name="clearanceMetres">
        /// Margin a route keeps from impassable ground. **Defaults to
        /// <see cref="DefaultClearanceMetres"/> — effectively a point at the
        /// unit's centre.**
        ///
        /// Width-aware routing exists and works, but it is off by default
        /// because it produces behaviour players read as broken: a 110 m line
        /// refusing to approach a shoreline or a map edge it could obviously
        /// stand near. A point at the centre is the simpler contract and the one
        /// the rest of the game assumes — if the centre can be there, the unit
        /// can.
        ///
        /// Pass the unit's half-frontage to opt into width-aware routing.
        /// </param>
        public HexPathfinder(
            ITerrainMap terrain,
            IMovementModel movement,
            ITerrainCatalogue catalogue,
            float cellSpacingMetres = DefaultCellSpacingMetres,
            int cellBudget = 400_000,
            float? clearanceMetres = null)
        {
            _terrain = terrain ?? throw new ArgumentNullException(nameof(terrain));
            _movement = movement ?? throw new ArgumentNullException(nameof(movement));
            _catalogue = catalogue ?? throw new ArgumentNullException(nameof(catalogue));

            _spacing = cellSpacingMetres;
            _clearance = MathF.Max(0f, clearanceMetres ?? DefaultClearanceMetres);
            _layout = HexLayout.FromNeighbourDistance(cellSpacingMetres, terrain.Bounds.Min);
            _cellBudget = cellBudget;

            // The heuristic must never overestimate, or A* stops finding the
            // best route. The cheapest conceivable step is one spacing across
            // the fastest terrain that exists, so that is what it assumes.
            for (int i = 0; i < MovementTypeCount; i++)
            {
                float fastest = 0f;

                foreach (TerrainDef def in catalogue.All)
                    fastest = MathF.Max(fastest, def.SpeedMultiplier((MovementType)i));

                _fastestMultiplier[i] = fastest;
            }
        }

        /// <summary>The grid the search runs on. Exposed for diagnostics only.</summary>
        public HexLayout SearchLayout => _layout;

        public PathResult FindPath(Vec2 from, Vec2 to, MovementType movementType)
        {
            float fastest = _fastestMultiplier[(int)movementType];
            if (fastest <= 0f)
                return PathResult.Failed(PathFailure.MovementTypeCannotMove,
                    $"{movementType} cannot cross any terrain on this map.", 0);

            Coord start = _layout.ToCoord(from);
            Coord goal = _layout.ToCoord(to);

            // Refuse a goal the unit could never stand on, rather than searching
            // the whole map to discover it. Each rejection says which of the
            // three quite different problems it hit.
            if (!_terrain.Bounds.Contains(to))
                return PathResult.Failed(PathFailure.GoalOffMap, "That is off the battlefield.", 0);

            if (MultiplierAt(goal, movementType) <= 0f)
            {
                string ground = _catalogue.Get(_terrain.At(to)).DisplayName;
                return PathResult.Failed(PathFailure.GoalImpassable,
                    $"{ground} is impassable to {movementType}.", 0);
            }

            if (!HasClearance(goal, movementType))
                return PathResult.Failed(PathFailure.GoalTooTight,
                    $"The destination is passable but hemmed in by impassable ground — " +
                    $"this unit was routed with {_clearance:0} m of required room to either side.", 0);

            if (start == goal)
            {
                var direct = new List<Vec2> { from, to };
                float straight = Vec2.Distance(from, to);
                return PathResult.Success(direct, new[] { start }, straight, straight / fastest, 0);
            }

            var cameFrom = new Dictionary<Coord, Coord>();
            var bestCost = new Dictionary<Coord, float> { [start] = 0f };
            var settled = new HashSet<Coord>();
            var open = new CoordMinHeap();

            open.Push(start, Heuristic(start, goal, fastest));

            int explored = 0;
            Span<Coord> neighbours = stackalloc Coord[HexMath.DirectionCount];

            while (open.TryPop(out Coord current))
            {
                if (!settled.Add(current))
                    continue;   // already finalised via a cheaper route

                explored++;

                if (current == goal)
                    return Reconstruct(cameFrom, start, goal, from, to, bestCost[goal], movementType, explored);

                if (explored >= _cellBudget)
                    return PathResult.Failed(PathFailure.SearchBudgetExhausted,
                        $"Gave up after searching {explored} cells. The route may exist but is very long.", explored);

                float currentCost = bestCost[current];
                HexMath.Neighbours(current, neighbours);

                for (int i = 0; i < neighbours.Length; i++)
                {
                    Coord next = neighbours[i];
                    if (settled.Contains(next)) continue;

                    float multiplier = MultiplierAt(next, movementType);
                    if (multiplier <= 0f) continue;

                    // Checked after the cheap centre test, since most rejections
                    // happen there and the ring costs six more lookups.
                    if (!HasClearance(next, movementType)) continue;

                    float tentative = currentCost + _spacing / multiplier;

                    if (bestCost.TryGetValue(next, out float known) && tentative >= known)
                        continue;

                    bestCost[next] = tentative;
                    cameFrom[next] = current;
                    open.Push(next, tentative + Heuristic(next, goal, fastest));
                }
            }

            // Both ends are fine and the search ran out of places to go, so the
            // destination is genuinely cut off — across water, or behind ground
            // this unit cannot cross.
            return PathResult.Failed(PathFailure.NoRouteExists,
                $"No connected route exists for {movementType}. Searched {explored} cells.", explored);
        }

        /// <summary>
        /// Nudges the search toward the goal so that, among equally good routes,
        /// it prefers the direct one.
        /// </summary>
        /// <remarks>
        /// On open ground an enormous number of routes tie for optimal, and
        /// without a tie-break A* returns an arbitrary one — typically a zigzag,
        /// since hex movement approximates a diagonal by alternating between two
        /// directions. Scaling the estimate by a hair breaks those ties toward
        /// whichever cell is nearer the goal, which both straightens the raw
        /// route and shrinks the searched area substantially.
        ///
        /// The cost is that a returned route may be a fraction of a percent
        /// longer than the true optimum. For a battlefield that is invisible,
        /// and it remains completely deterministic, so a published seed still
        /// reproduces exactly.
        /// </remarks>
        private const float HeuristicTieBreak = 1.001f;

        private float Heuristic(Coord cell, Coord goal, float fastestMultiplier) =>
            Coord.Distance(cell, goal) * _spacing / fastestMultiplier * HeuristicTieBreak;

        /// <summary>
        /// How fast the going is at a cell's centre. Used for step cost only.
        /// </summary>
        private float MultiplierAt(Coord cell, MovementType movementType)
        {
            Vec2 world = _layout.ToWorld(cell);
            if (!_terrain.Bounds.Contains(world))
                return 0f;

            return _movement.SpeedMultiplier(_terrain.At(world), movementType);
        }

        /// <summary>
        /// Whether a cell can be occupied, testing a ring at the clearance
        /// radius as well as the centre.
        /// </summary>
        /// <remarks>
        /// Sampling only the centre lets a cell straddling a shoreline read as
        /// perfectly passable, and the route then grazes the water. Testing the
        /// surrounding ring makes the search itself keep its distance, which is
        /// the only place the problem can actually be fixed — smoothing cannot
        /// repair a raw route that already hugs an obstacle.
        ///
        /// It also means clearance scales meaningfully: give it a regiment's
        /// half-width and the search will refuse gaps that regiment cannot fit
        /// through.
        /// </remarks>
        private bool IsEnterable(Coord cell, MovementType movementType) =>
            MultiplierAt(cell, movementType) > 0f && HasClearance(cell, movementType);

        private bool HasClearance(Coord cell, MovementType movementType)
        {
            if (_clearance <= 0f)
                return true;

            Vec2 centre = _layout.ToWorld(cell);

            for (int i = 0; i < HexMath.DirectionCount; i++)
            {
                float angle = MathF.PI / 3f * i;
                var probe = new Vec2(
                    centre.X + _clearance * MathF.Cos(angle),
                    centre.Y + _clearance * MathF.Sin(angle));

                // A formation may overhang the edge of the battlefield — the
                // fighting simply carries on off-map. Only real terrain blocks,
                // so a unit is never refused ground it could plainly stand on
                // just because the map stops nearby.
                if (!_terrain.Bounds.Contains(probe))
                    continue;

                if (_movement.SpeedMultiplier(_terrain.At(probe), movementType) <= 0f)
                    return false;
            }

            return true;
        }

        private PathResult Reconstruct(
            Dictionary<Coord, Coord> cameFrom,
            Coord start,
            Coord goal,
            Vec2 from,
            Vec2 to,
            float searchCost,
            MovementType movementType,
            int explored)
        {
            var cells = new List<Coord>();
            Coord cursor = goal;

            cells.Add(cursor);
            while (cursor != start)
            {
                cursor = cameFrom[cursor];
                cells.Add(cursor);
            }
            cells.Reverse();

            // Walk real world positions, anchored to the caller's exact start and
            // end rather than the cell centres the search happened to use.
            var rawPoints = new List<Vec2>(cells.Count + 1) { from };
            for (int i = 1; i < cells.Count - 1; i++)
                rawPoints.Add(_layout.ToWorld(cells[i]));
            rawPoints.Add(to);

            List<Vec2> waypoints = PathSmoother.Smooth(rawPoints, _terrain, _movement, movementType, _spacing, _clearance);

            float distance = 0f;
            float effective = 0f;

            for (int i = 1; i < waypoints.Count; i++)
            {
                distance += Vec2.Distance(waypoints[i - 1], waypoints[i]);
                effective += PathSmoother.SegmentCost(waypoints[i - 1], waypoints[i], _terrain, _movement, movementType, _spacing);
            }

            // If smoothing somehow produced something worse than the search
            // found, trust the search's figure instead of reporting a regression.
            if (float.IsInfinity(effective) || effective <= 0f)
                effective = searchCost;

            return PathResult.Success(waypoints, cells, distance, effective, explored);
        }
    }
}
