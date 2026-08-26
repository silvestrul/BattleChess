using System;
using System.Collections.Generic;
using BattleChess.Contracts;

namespace BattleChess.Rules.HybridPlanning
{
    /// <summary>
    /// Seconds still to spend, for a point that has a facing — a coarse
    /// solution of the whole problem, used as the lattice's heuristic.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="HybridObstacleField"/> answers in hops and then guesses the
    /// turning by asking which way the grid leaves the cell the mover stands
    /// in. Its own remarks admit the cost of that: it "charges one change of
    /// front where a route round two bodies needs three". For these units a 90°
    /// change of front is about twenty seconds and ninety metres of marching is
    /// nineteen, so turning is not a correction to the estimate — it is most of
    /// it, and an estimate blind to all but the first turn is wrong by a factor
    /// rather than by a margin. Measured: about <b>18 300 states expanded an
    /// order</b>, and pure Dijkstra at 181–261 ms against 76–126 with that
    /// heuristic — a search buying 2 to 3× where A* over a space this size
    /// should buy one to two orders of magnitude.
    /// </para>
    /// <para>
    /// So this solves the relaxed problem properly instead. A state is
    /// (cell, direction of travel) over eight directions; an edge is either one
    /// cell of march at top speed, or a pivot onto an adjacent direction priced
    /// through <see cref="HybridPrimitives.SecondsToPivot"/> so the estimate and
    /// the lattice agree about what turning costs. Dijkstra outward from the
    /// goal then gives every state its true cost under that relaxation, turns
    /// included.
    /// </para>
    /// <para>
    /// It is a <b>point</b> robot carrying a swollen body, so it stays
    /// optimistic about a rectangle that must also fit — which is the direction
    /// a heuristic is allowed to be wrong in.
    /// </para>
    /// </remarks>
    internal sealed class HybridTurnField
    {
        /// <summary>Directions of travel, and so the layers of the grid.</summary>
        private const int Directions = 8;

        /// <summary>
        /// Coarser than <see cref="HybridObstacleField"/>'s, deliberately. This
        /// grid carries eight layers and a priority queue where that one
        /// carries a single layer and a plain queue, so a cell costs an order
        /// more here — and an estimate does not need to see a four-metre
        /// feature.
        /// </summary>
        /// <summary>
        /// The floor on cell size, and a measurement lever since M84.
        /// </summary>
        /// <remarks>
        /// The fill settles <c>columns * rows * 8</c> states, so its cost is
        /// quadratic in this: halving the cell size quadruples the work. It had
        /// never been swept, and the field is 52 to 88% of the hybrid planner's
        /// self time and 6 to 26% of the staged planner's — the largest single
        /// number in the profile that nobody had put a dial on.
        /// </remarks>
        /// <remarks>
        /// <b>Twenty, on the sweep</b>, from ten. Least of three in Release on
        /// the planner a battle uses: the Crucible <b>-16%</b>, the sideways
        /// mile -5%, Broken Country -3%, the Long March -2%, and — the half that
        /// decides it under <see href="../../../docs/DECISIONS.md">W10</see> —
        /// <b>not one press-through, unwalkable route or second of marching
        /// moved on any field</b>. The estimate is coarser and the routes are
        /// the same, which is what a heuristic being over-resolved looks like.
        /// <b>Forty was faster still and is refused</b>: -16%, -10%, -4%, but it
        /// costs two press-throughs and two unwalkable routes on Broken
        /// Country, and a number that improves while contact worsens is a trade
        /// and not a win.
        /// </remarks>
        internal static float MinCellMetres = 20f;

        /// <summary>
        /// Cells kept along the straight run, before the margin. A measurement
        /// lever since M84, for the same reason as <see cref="MinCellMetres"/>.
        /// </summary>
        internal static float TargetCellsAcross = 48f;

        /// <summary>
        /// Grid extents are rounded up to this, so two movers a little apart
        /// ask for the same field rather than two that differ by a metre.
        /// </summary>
        private const float RadiusStepMetres = 128f;
        private const float DetourRoomFraction = 0.5f;

        /// <summary>
        /// Whether the fill is ordered by a bucket queue rather than by a
        /// binary heap. A measurement lever.
        /// </summary>
        /// <remarks>
        /// The relaxation takes one of three values — a cell of march, a
        /// diagonal cell, or an eighth turn — so the frontier's spread of costs
        /// is bounded by the largest of them, and a ring of buckets one edge
        /// deep can serve every key that will ever be in the queue. That makes
        /// take and put O(1) each where the heap pays a sift over a hundred
        /// thousand entries. Ordering inside a bucket is arbitrary, which can
        /// settle a state a hair early; the ordinary relax check catches it and
        /// re-queues, so what comes out is still exact.
        /// </remarks>
        /// <remarks>
        /// <b>On, on the measurement</b>, and it is the rare change that costs
        /// nothing to be wrong about: every route came back identical to the
        /// decimal on all three fields. Least of three passes an order - the
        /// Long March 24,8 to 19,2, the Crucible 9,6 to 7,4, Broken Country 5,7
        /// to 3,6.
        /// </remarks>
        internal static bool DialQueue = true;

        /// <summary>
        /// How far past the straight run the fill is carried, as a multiple of
        /// it. Nought fills the whole grid. A measurement lever.
        /// </summary>
        /// <remarks>
        /// The fill settles every cell of every layer, and most of those cells
        /// are ground no route would touch: a state whose own cost already
        /// exceeds what the whole march will cost cannot lie on the answer, so
        /// solving it exactly buys the search nothing. Past the bound the
        /// estimate falls back to the straight run at top speed, which is
        /// weaker guidance but still an underestimate, so what it can cost is
        /// expansions and not correctness.
        /// </remarks>
        /// <remarks>
        /// <b>Nought, on the measurement</b>, and the sweep says why the idea
        /// was wrong rather than merely unprofitable. At 1,3 the Crucible went
        /// 6,5 to 14,9 ms an order and Broken Country 3,4 to 14,1; at 2,0 the
        /// bound never binds and nothing changes. The fill is not overhead that
        /// happens to sit beside the search - it <i>is</i> the search's
        /// guidance, and the ground it is cut from is exactly the ground the
        /// lattice then has to grope over itself, at ten times the price a cell
        /// of the fill costs.
        /// </remarks>
        internal static float FillMultiple;

        private static readonly int[] StepColumn = { 1, 1, 0, -1, -1, -1, 0, 1 };
        private static readonly int[] StepRow = { 0, 1, 1, 1, 0, -1, -1, -1 };

        private readonly float _cellMetres;
        private readonly float _minX;
        private readonly float _minY;
        private readonly int _columns;
        private readonly int _rows;
        private readonly float _turnRateDegreesPerSecond;

        /// <summary>Seconds to cross one cell at top speed.</summary>
        private readonly float _stepSeconds;

        /// <summary>Seconds to the goal from (cell, direction), by cell then by direction.</summary>
        private readonly float[] _seconds;

        private HybridTurnField(
            float cellMetres, float minX, float minY, int columns, int rows,
            float turnRateDegreesPerSecond, float stepSeconds, float[] seconds)
        {
            _stepSeconds = stepSeconds;
            _cellMetres = cellMetres;
            _minX = minX;
            _minY = minY;
            _columns = columns;
            _rows = rows;
            _turnRateDegreesPerSecond = turnRateDegreesPerSecond;
            _seconds = seconds;
        }

        /// <summary>
        /// <b>Sharing was tried and does not pay.</b> A field is a function of
        /// the goal, the mover's distance from it and the arrangement of every
        /// body — and a mover must be left out of its own clearance set, so no
        /// two regiments of one army ever had the same set. Including the mover
        /// to make the sets match, and centring the grid on the goal so that
        /// different starts could share, was measured on eighty orders three
        /// ways: <b>built 80, reused 0</b> when the orders cross the field,
        /// <b>80 and 0</b> when a wing is sent to one block on two fields, and
        /// <b>76 and 4</b> on the third — while the goal-centred grid it needed
        /// cost Broken Country 5,9 ms an order against 17,5. Kept as a remark
        /// rather than as code, so it is not re-derived.
        /// </summary>
        public static HybridTurnField Build(
            Vec2 start, Vec2 goal, IReadOnlyList<HybridBox> obstacles, float inflateByMetres,
            float topSpeedMetresPerSecond, float turnRateDegreesPerSecond)
        {
            float straight = Vec2.Distance(start, goal);
            float cellMetres = MathF.Max(MinCellMetres, straight / TargetCellsAcross);

            float margin = MathF.Max(cellMetres * 4f, straight * DetourRoomFraction);
            float minX = MathF.Min(start.X, goal.X) - margin;
            float minY = MathF.Min(start.Y, goal.Y) - margin;
            float maxX = MathF.Max(start.X, goal.X) + margin;
            float maxY = MathF.Max(start.Y, goal.Y) + margin;

            int columns = Math.Max(1, (int)MathF.Ceiling((maxX - minX) / cellMetres));
            int rows = Math.Max(1, (int)MathF.Ceiling((maxY - minY) / cellMetres));

            bool[] blocked;

            using (PlanningProfile.Measure(PlanningProfile.Step.HybridRaster))
                blocked = Raster(obstacles, inflateByMetres, cellMetres, minX, minY, columns, rows);

            // Borrowed rather than allocated. A field is a hundred thousand
            // floats and a plan builds one, so eighty orders churned some tens
            // of megabytes through the collector for arrays that die at the end
            // of the search — which is exactly the shape M40 found and fixed
            // everywhere else in planning.
            int wanted = columns * rows * Directions;
            float[] seconds = _spare != null && _spare.Length >= wanted ? _spare : new float[wanted];
            _spare = null;

            for (int i = 0; i < wanted; i++) seconds[i] = float.PositiveInfinity;

            int goalColumn = Math.Clamp((int)((goal.X - minX) / cellMetres), 0, columns - 1);
            int goalRow = Math.Clamp((int)((goal.Y - minY) / cellMetres), 0, rows - 1);
            int goalCell = goalRow * columns + goalColumn;

            // Arriving is free however the mover is pointing: an arrival front
            // is the caller's business, and the lattice charges it itself.
            float straightSeconds = cellMetres / topSpeedMetresPerSecond;
            float diagonalSeconds = straightSeconds * 1.41421356f;

            float eighth = 2f * MathF.PI / Directions;
            float pivotSeconds = HybridPrimitives.SecondsToPivot(eighth, turnRateDegreesPerSecond);

            IFrontier open;
            if (DialQueue)
            {
                Dial dial = _dial ??= new Dial();
                dial.Reset(MathF.Max(diagonalSeconds, pivotSeconds));
                open = dial;
            }
            else
            {
                Cheapest heap = _queue ??= new Cheapest();
                heap.Clear();
                open = heap;
            }

            for (int d = 0; d < Directions; d++)
            {
                seconds[goalCell * Directions + d] = 0f;
                open.Enqueue(goalCell * Directions + d, 0f);
            }

            float far = FillMultiple > 0f
                ? Vec2.Distance(start, goal) / topSpeedMetresPerSecond * FillMultiple
                : float.PositiveInfinity;

            while (open.TryTake(out int state, out float value))
            {
                // Everything still in the queue costs at least this much, so
                // there is nothing cheaper left to find.
                if (value > far) break;

                // A stale copy of a state already settled more cheaply.
                if (value > seconds[state]) continue;

                int cell = state / Directions;
                int direction = state - cell * Directions;

                int column = cell % columns;
                int row = cell / columns;

                // Whoever could have marched into this cell facing this way.
                int fromColumn = column - StepColumn[direction];
                int fromRow = row - StepRow[direction];

                if (fromColumn >= 0 && fromColumn < columns && fromRow >= 0 && fromRow < rows)
                {
                    int from = fromRow * columns + fromColumn;
                    if (!blocked[from])
                    {
                        bool diagonal = StepColumn[direction] != 0 && StepRow[direction] != 0;
                        Relax(seconds, open, from * Directions + direction,
                            value + (diagonal ? diagonalSeconds : straightSeconds));
                    }
                }

                // Or stood here and turned onto it. Only the two neighbouring
                // directions: a wider turn is those, chained, at the same price.
                Relax(seconds, open, cell * Directions + (direction + 1) % Directions,
                    value + pivotSeconds);
                Relax(seconds, open, cell * Directions + (direction + Directions - 1) % Directions,
                    value + pivotSeconds);
            }

            return new HybridTurnField(
                cellMetres, minX, minY, columns, rows, turnRateDegreesPerSecond,
                straightSeconds, seconds);
        }

        /// <summary>
        /// One field's worth of working memory, handed back when the next build
        /// asks. One is enough: a plan builds a field, reads it and drops it.
        /// </summary>
        [ThreadStatic] private static float[]? _spare;

        /// <summary>Hands the working memory back for the next build to borrow.</summary>
        public void Release()
        {
            if (_spare == null || _spare.Length < _seconds.Length) _spare = _seconds;
        }

        private static void Relax(float[] seconds, IFrontier open, int state, float value)
        {
            if (value >= seconds[state]) return;

            seconds[state] = value;
            open.Enqueue(state, value);
        }

        private static bool[] Raster(
            IReadOnlyList<HybridBox> obstacles, float inflateByMetres, float cellMetres,
            float minX, float minY, int columns, int rows)
        {
            int wanted = columns * rows;
            bool[] blocked = _rough != null && _rough.Length >= wanted ? _rough : new bool[wanted];
            _rough = null;
            Array.Clear(blocked, 0, wanted);

            for (int o = 0; o < obstacles.Count; o++)
            {
                HybridBox body = obstacles[o];
                var swollen = new HybridBox(
                    body.Centre, body.Heading,
                    body.HalfWidth + inflateByMetres, body.HalfDepth + inflateByMetres);

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

                    var centre = new Vec2(
                        minX + (column + 0.5f) * cellMetres, minY + (row + 0.5f) * cellMetres);

                    if (swollen.Contains(centre)) blocked[index] = true;
                }
            }

            _rough = blocked;
            return blocked;
        }

        [ThreadStatic] private static bool[]? _rough;
        [ThreadStatic] private static Cheapest? _queue;
        [ThreadStatic] private static Dial? _dial;

        /// <summary>Whatever the fill takes its next state from.</summary>
        private interface IFrontier
        {
            void Enqueue(int state, float value);
            bool TryTake(out int state, out float value);
        }

        /// <summary>
        /// A ring of buckets, one edge weight deep, indexed by cost.
        /// </summary>
        /// <remarks>
        /// Everything in the queue at any moment lies between the value just
        /// taken and that value plus one edge, because every entry was made by
        /// relaxing a settled state. So the ring need only be as long as one
        /// edge is wide, and it can be walked forward and never searched. A
        /// state improved after its own bucket has gone by is put in the
        /// current one, which is why the value travels with the state rather
        /// than being read back from the array.
        /// </remarks>
        private sealed class Dial : IFrontier
        {
            /// <summary>Buckets to a widest edge. Finer than the costs differ by.</summary>
            private const int PerEdge = 64;

            private readonly List<(int state, float value)>[] _buckets =
                new List<(int, float)>[PerEdge + 2];

            private float _unit;
            private int _at;
            private int _held;

            public Dial()
            {
                for (int i = 0; i < _buckets.Length; i++)
                    _buckets[i] = new List<(int, float)>(64);
            }

            public void Reset(float widestEdge)
            {
                _unit = widestEdge <= 0f ? 1f : widestEdge / PerEdge;
                _at = 0;
                _held = 0;

                for (int i = 0; i < _buckets.Length; i++) _buckets[i].Clear();
            }

            public void Enqueue(int state, float value)
            {
                int wanted = (int)(value / _unit);
                if (wanted < _at) wanted = _at;

                _buckets[wanted % _buckets.Length].Add((state, value));
                _held++;
            }

            public bool TryTake(out int state, out float value)
            {
                while (_held > 0)
                {
                    List<(int state, float value)> bucket = _buckets[_at % _buckets.Length];

                    if (bucket.Count == 0)
                    {
                        _at++;
                        continue;
                    }

                    (state, value) = bucket[bucket.Count - 1];
                    bucket.RemoveAt(bucket.Count - 1);
                    _held--;
                    return true;
                }

                state = 0;
                value = 0f;
                return false;
            }
        }

        /// <summary>
        /// The smallest binary heap that will do. netstandard2.1 has no
        /// <c>PriorityQueue</c>, and the one in the lattice is private to it.
        /// </summary>
        private sealed class Cheapest : IFrontier
        {
            private readonly List<(int state, float value)> _items =
                new List<(int, float)>(1024);

            public void Clear() => _items.Clear();

            public void Enqueue(int state, float value)
            {
                _items.Add((state, value));

                int i = _items.Count - 1;
                while (i > 0)
                {
                    int parent = (i - 1) / 2;
                    if (_items[parent].value <= _items[i].value) break;
                    (_items[parent], _items[i]) = (_items[i], _items[parent]);
                    i = parent;
                }
            }

            public bool TryTake(out int state, out float value)
            {
                if (_items.Count == 0)
                {
                    state = 0;
                    value = 0f;
                    return false;
                }

                (state, value) = _items[0];

                int last = _items.Count - 1;
                _items[0] = _items[last];
                _items.RemoveAt(last);

                int i = 0;
                while (true)
                {
                    int left = i * 2 + 1;
                    int right = left + 1;
                    int smallest = i;

                    if (left < _items.Count && _items[left].value < _items[smallest].value) smallest = left;
                    if (right < _items.Count && _items[right].value < _items[smallest].value) smallest = right;
                    if (smallest == i) break;

                    (_items[smallest], _items[i]) = (_items[i], _items[smallest]);
                    i = smallest;
                }

                return true;
            }
        }

        /// <summary>
        /// Seconds still to spend from here, facing this way — or
        /// <paramref name="fallback"/> where the grid never reached.
        /// </summary>
        public float SecondsFrom(Vec2 from, Facing heading, float fallback)
        {
            int column = (int)MathF.Floor((from.X - _minX) / _cellMetres);
            int row = (int)MathF.Floor((from.Y - _minY) / _cellMetres);

            if (column < 0 || column >= _columns || row < 0 || row >= _rows) return fallback;

            float best = OnwardFrom(column, row, heading, 0f);

            // The estimate is built from every body of the army, the mover's
            // own among them, so the cell it is standing in is blocked and the
            // fill never reached it. One ring out is the ground it is about to
            // step onto, plus the step.
            if (float.IsPositiveInfinity(best))
            {
                float step = _stepSeconds;

                for (int dr = -1; dr <= 1 && float.IsPositiveInfinity(best); dr++)
                for (int dc = -1; dc <= 1; dc++)
                {
                    if (dc == 0 && dr == 0) continue;

                    int nc = column + dc;
                    int nr = row + dr;
                    if (nc < 0 || nc >= _columns || nr < 0 || nr >= _rows) continue;

                    float near = OnwardFrom(nc, nr, heading, step);
                    if (near < best) best = near;
                }
            }

            return float.IsPositiveInfinity(best) ? fallback : best;
        }

        /// <summary>The cheapest way onward from one cell, given a facing.</summary>
        private float OnwardFrom(int column, int row, Facing heading, float extra)
        {
            int cell = (row * _columns + column) * Directions;

            float best = float.PositiveInfinity;
            float eighth = 2f * MathF.PI / Directions;

            for (int d = 0; d < Directions; d++)
            {
                float onward = _seconds[cell + d];
                if (float.IsPositiveInfinity(onward)) continue;

                onward += extra;

                // What it costs to come round onto that direction of travel
                // first — a cost the lattice would also have to pay.
                float offBy = Facing.AbsoluteDelta(heading, Facing.FromRadians(d * eighth));
                float total = onward + HybridPrimitives.SecondsToPivot(offBy, _turnRateDegreesPerSecond);

                if (total < best) best = total;
            }

            return best;
        }
    }
}
