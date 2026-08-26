using System;
using System.Collections.Generic;
using BattleChess.Contracts;

namespace BattleChess.Rules.HybridPlanning
{
    /// <summary>
    /// A coarse, obstacle-aware distance-to-goal estimate: a point robot's
    /// shortest hop count across a grid marking the same obstacles the real
    /// search avoids, built once per plan and read in O(1) per node after.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is "h2" from the design discussion — holonomic (no heading, no
    /// footprint) but obstacle-aware, which is exactly what a plain
    /// straight-line heuristic cannot see: a march that has to go round a
    /// body gets credited with the ground that actually costs, instead of
    /// the ground through it. Without this, the search had no way to prefer
    /// "heading toward the gap" over "heading straight at the wall" until it
    /// had already tried both — which measured at ~9,400 expansions to route
    /// round a single body in an otherwise empty field.
    /// </para>
    /// <para>
    /// A grid can only ever approximate the true shortest way round — an
    /// 8-connected hop count runs a little long compared to the continuous
    /// path it stands in for — so this was never a certificate of anything,
    /// matching the project's own read of the piano mover's problem
    /// ("'close to optimal' is the right target").
    /// </para>
    /// <para>
    /// <b>Two layers, and the estimate is the larger of them.</b> One marks
    /// the bodies at true size; the other inflates them by the mover's own
    /// in-radius — the smaller of its two half-extents, which is the least
    /// clearance any orientation of it could possibly need, so a channel the
    /// inflated layer closes is a channel no facing could have used.
    /// </para>
    /// <para>
    /// The true-size layer alone was ruinous, on the reasoning that an
    /// under-inflated obstacle can only make the estimate more optimistic,
    /// which is the safe direction for a lower bound. Sound, and expensive:
    /// a point robot walks a 30 m gap that a regiment 40 m across its front
    /// cannot use at all, so on the project's own approach-angle gate the
    /// estimate read about 25 s for a route that really costs several times
    /// that, and A* dutifully expanded every state cheaper than the truth.
    /// </para>
    /// <para>
    /// The inflated layer alone was not the answer either, and the failure
    /// is worth recording because it is not obvious: inflation swallows the
    /// mover's <i>own starting ground</i> when it begins close to a body, so
    /// exactly the states that needed guidance most got none — the flood
    /// fill never reached them and the estimate fell back to the straight
    /// line. Measured, on the gate at 0°: worse than no inflation at all.
    /// Keeping both layers and taking the larger of the two answers keeps
    /// the sharper estimate wherever the inflated layer has one, and never
    /// gives up the coarser one where it does not.
    /// </para>
    /// </remarks>
    internal sealed class HybridObstacleField
    {
        private const float MinCellMetres = 4f;

        /// <summary>Cells kept along the longer of the start-to-goal axes, roughly.</summary>
        private const float TargetCellsAcross = 150f;

        /// <summary>How far outside the start-to-goal box a detour is still seen, as a fraction of the straight line.</summary>
        private const float DetourRoomFraction = 0.5f;

        private readonly float _cellMetres;
        private readonly float _minX;
        private readonly float _minY;
        private readonly int _columns;
        private readonly int _rows;
        private readonly float[] _hopsThroughEveryGap;
        private readonly float[] _hopsThroughGapsThatFit;

        private HybridObstacleField(
            float cellMetres, float minX, float minY, int columns, int rows,
            float[] hopsThroughEveryGap, float[] hopsThroughGapsThatFit)
        {
            _cellMetres = cellMetres;
            _minX = minX;
            _minY = minY;
            _columns = columns;
            _rows = rows;
            _hopsThroughEveryGap = hopsThroughEveryGap;
            _hopsThroughGapsThatFit = hopsThroughGapsThatFit;
        }

        public static HybridObstacleField Build(
            Vec2 start, Vec2 goal, IReadOnlyList<HybridBox> obstacles, float inflateByMetres)
        {
            float straight = Vec2.Distance(start, goal);
            float cellMetres = MathF.Max(MinCellMetres, straight / TargetCellsAcross);

            // Wide enough to hold a real detour, not just the corridor
            // between the two ends. A margin of a few cells left any way
            // round that stepped outside the start-to-goal box invisible:
            // those cells stayed unreached by the flood fill, EstimateMetres
            // fell back to the straight line, and the search lost its
            // guidance in precisely the places it was most needed — which is
            // the same shape as the failure that made a single body cost
            // 9.428 expansions.
            float margin = MathF.Max(cellMetres * 4f, straight * DetourRoomFraction);
            float minX = MathF.Min(start.X, goal.X) - margin;
            float minY = MathF.Min(start.Y, goal.Y) - margin;
            float maxX = MathF.Max(start.X, goal.X) + margin;
            float maxY = MathF.Max(start.Y, goal.Y) + margin;

            int columns = Math.Max(1, (int)MathF.Ceiling((maxX - minX) / cellMetres));
            int rows = Math.Max(1, (int)MathF.Ceiling((maxY - minY) / cellMetres));

            int goalColumn = Math.Clamp((int)((goal.X - minX) / cellMetres), 0, columns - 1);
            int goalRow = Math.Clamp((int)((goal.Y - minY) / cellMetres), 0, rows - 1);

            float[] everyGap = FloodFill(
                obstacles, 0f, cellMetres, minX, minY, columns, rows, goalColumn, goalRow);
            float[] gapsThatFit = FloodFill(
                obstacles, inflateByMetres, cellMetres, minX, minY, columns, rows, goalColumn, goalRow);

            return new HybridObstacleField(
                cellMetres, minX, minY, columns, rows, everyGap, gapsThatFit);
        }

        /// <summary>
        /// Hop counts out from the goal cell across one layer, with every
        /// body swollen by <paramref name="inflateByMetres"/>.
        /// </summary>
        private static float[] FloodFill(
            IReadOnlyList<HybridBox> obstacles, float inflateByMetres, float cellMetres,
            float minX, float minY, int columns, int rows, int goalColumn, int goalRow)
        {
            var blocked = new bool[columns * rows];


            // Only the cells a body could possibly cover, not every cell on the
            // field for every body.
            //
            // This walked the whole grid once per obstacle. The grid is about
            // 300 cells across — a hundred and fifty over the straight line and
            // as much again in margin — so ninety thousand cells, times every
            // regiment, times the two fills below. Thirteen regiments came to
            // over two million point-in-box tests, and they were paid before
            // the search ran at all: a march whose answer was the straight line
            // cost 19 ms and tried not one primitive.
            //
            // A regiment is forty metres by twenty and a cell is four, so it
            // covers about fifty cells. Bounding the sweep by the body's own
            // extent is the same answer for about a thousandth of the work.
            for (int o = 0; o < obstacles.Count; o++)
            {
                HybridBox body = obstacles[o];
                var swollen = new HybridBox(
                    body.Centre, body.Heading,
                    body.HalfWidth + inflateByMetres, body.HalfDepth + inflateByMetres);

                // Circumscribed radius: the widest the swollen box can reach
                // from its own centre, whichever way it points. Provably safe
                // rather than tight, which is what a broad phase has to be.
                float reach = MathF.Sqrt(
                    swollen.HalfWidth * swollen.HalfWidth + swollen.HalfDepth * swollen.HalfDepth);

                int fromColumn = Math.Max(0, (int)MathF.Floor((body.Centre.X - reach - minX) / cellMetres));
                int toColumn = Math.Min(columns - 1, (int)MathF.Ceiling((body.Centre.X + reach - minX) / cellMetres));
                int fromRow = Math.Max(0, (int)MathF.Floor((body.Centre.Y - reach - minY) / cellMetres));
                int toRow = Math.Min(rows - 1, (int)MathF.Ceiling((body.Centre.Y + reach - minY) / cellMetres));

                for (int row = fromRow; row <= toRow; row++)
                for (int column = fromColumn; column <= toColumn; column++)
                {
                    int index = row * columns + column;
                    if (blocked[index]) continue;

                    var centre = new Vec2(minX + (column + 0.5f) * cellMetres, minY + (row + 0.5f) * cellMetres);
                    if (swollen.Contains(centre)) blocked[index] = true;
                }
            }

            var hops = new float[columns * rows];
            for (int i = 0; i < hops.Length; i++) hops[i] = float.PositiveInfinity;

            // The goal's own cell is exempt from being blocked — the search
            // is being asked to reach exactly this point, not to avoid it,
            // even if some obstacle's edge happens to graze the same cell.
            hops[goalRow * columns + goalColumn] = 0f;

            var queue = new Queue<(int column, int row)>();
            queue.Enqueue((goalColumn, goalRow));

            while (queue.Count > 0)
            {
                (int column, int row) at = queue.Dequeue();
                float here = hops[at.row * columns + at.column];

                for (int dc = -1; dc <= 1; dc++)
                for (int dr = -1; dr <= 1; dr++)
                {
                    if (dc == 0 && dr == 0) continue;

                    int nc = at.column + dc;
                    int nr = at.row + dr;
                    if (nc < 0 || nc >= columns || nr < 0 || nr >= rows) continue;

                    int index = nr * columns + nc;
                    if (blocked[index]) continue;

                    float step = (dc != 0 && dr != 0) ? 1.41421356f : 1f;
                    float candidate = here + step;

                    if (candidate < hops[index])
                    {
                        hops[index] = candidate;
                        queue.Enqueue((nc, nr));
                    }
                }
            }

            return hops;
        }

        /// <summary>
        /// Grid-hop distance from <paramref name="from"/> to the goal this
        /// field was built for, in metres — or <paramref name="fallback"/>
        /// when the point falls outside the grid or in a cell the flood fill
        /// never reached (surrounded, as far as the coarse grid can tell).
        /// </summary>
        public float EstimateMetres(Vec2 from, float fallback)
        {
            int column = (int)((from.X - _minX) / _cellMetres);
            int row = (int)((from.Y - _minY) / _cellMetres);

            // Read the nearest edge cell rather than giving up on the grid
            // entirely: a state out past the margin is at least as far from
            // the goal as the boundary it would have to cross, so the
            // boundary's own estimate still says something, where the
            // straight-line fallback says nothing about the bodies in
            // between.
            int edgeColumn = Math.Clamp(column, 0, _columns - 1);
            int edgeRow = Math.Clamp(row, 0, _rows - 1);
            int index = edgeRow * _columns + edgeColumn;

            float toEdge = (edgeColumn == column && edgeRow == row)
                ? 0f
                : Vec2.Distance(from, new Vec2(
                    _minX + (edgeColumn + 0.5f) * _cellMetres,
                    _minY + (edgeRow + 0.5f) * _cellMetres));

            float best = fallback;

            float fitting = _hopsThroughGapsThatFit[index];
            if (!float.IsPositiveInfinity(fitting))
                best = MathF.Max(best, toEdge + fitting * _cellMetres);

            float any = _hopsThroughEveryGap[index];
            if (!float.IsPositiveInfinity(any))
                best = MathF.Max(best, toEdge + any * _cellMetres);

            return best;
        }

        /// <summary>
        /// Which way the shortest way round leaves this cell — a unit
        /// vector, or nothing where the flood fill never arrived.
        /// </summary>
        /// <remarks>
        /// The grid already knows the way round; until this it could only
        /// say how far, and a planner whose expensive move is <i>turning</i>
        /// needs to be told which way to face as much as how far to walk.
        /// Read straight off the hop counts, downhill, so it costs eight
        /// array lookups and stores nothing.
        /// </remarks>
        /// <summary>
        /// The grid's own way round, start to goal, as a polyline of cell
        /// centres. Follows <see cref="DirectionAt"/> downhill.
        /// </summary>
        /// <remarks>
        /// This is a point robot's route, so it is not walkable by a rectangle
        /// and is never returned as one. What it is good for is saying
        /// <i>which way round</i> — the topology the lattice would otherwise
        /// spend tens of thousands of expansions rediscovering.
        /// </remarks>
        public List<Vec2> TraceTo(Vec2 from, Vec2 goal)
        {
            var line = new List<Vec2> { from };

            Vec2 at = from;
            int steps = (_columns + _rows) * 2;

            for (int i = 0; i < steps; i++)
            {
                if (Vec2.DistanceSquared(at, goal) <= _cellMetres * _cellMetres * 4f) break;
                if (!DirectionAt(at, out Vec2 towards)) break;

                at += towards * _cellMetres;
                line.Add(at);
            }

            line.Add(goal);
            return line;
        }

        public bool DirectionAt(Vec2 from, out Vec2 towards)
        {
            towards = default;

            int column = Math.Clamp((int)((from.X - _minX) / _cellMetres), 0, _columns - 1);
            int row = Math.Clamp((int)((from.Y - _minY) / _cellMetres), 0, _rows - 1);

            float[] layer = float.IsPositiveInfinity(_hopsThroughGapsThatFit[row * _columns + column])
                ? _hopsThroughEveryGap
                : _hopsThroughGapsThatFit;

            float here = layer[row * _columns + column];
            if (float.IsPositiveInfinity(here)) return false;

            float best = here;
            int bestColumn = column, bestRow = row;

            for (int dc = -1; dc <= 1; dc++)
            for (int dr = -1; dr <= 1; dr++)
            {
                if (dc == 0 && dr == 0) continue;

                int nc = column + dc;
                int nr = row + dr;
                if (nc < 0 || nc >= _columns || nr < 0 || nr >= _rows) continue;

                float there = layer[nr * _columns + nc];
                if (there < best)
                {
                    best = there;
                    bestColumn = nc;
                    bestRow = nr;
                }
            }

            if (bestColumn == column && bestRow == row) return false;

            var step = new Vec2(bestColumn - column, bestRow - row);
            towards = step / step.Length;
            return true;
        }
    }
}
