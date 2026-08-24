using System;
using System.Collections.Generic;
using BattleChess.Contracts;

namespace BattleChess.Rules
{
    /// <summary>
    /// Where everybody is standing, bucketed so that "who could this line
    /// possibly touch" is a lookup rather than a walk down the whole army.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The single thing planning spends its time on.</b> Profiled across
    /// three fields and every planner in the visibility-graph family, the
    /// clearance scan was 59–90% of self time — and measured against its own
    /// counters, roughly two thirds of that was not geometry at all. It was
    /// walking every regiment on the field, allocating an enumerator, and
    /// taking two square roots per body to decide the body was nowhere near.
    /// A plan asks that question tens of thousands of times and the answer
    /// changes only when somebody moves.
    /// </para>
    /// <para>
    /// A regiment is filed under the one bucket its <i>centre</i> falls in,
    /// never under several. That is what makes a query return each body once
    /// without a de-duplicating pass over the results: buckets are disjoint, so
    /// distinct buckets hold distinct bodies. The price is that a query has to
    /// widen itself by the largest bounding radius on the field, since a body
    /// filed one bucket over may still reach in. Cheap, and provably cannot
    /// miss.
    /// </para>
    /// <para>
    /// Deliberately not a quadtree or a BVH. The field is a fixed rectangle a
    /// couple of kilometres across holding a hundred-odd bodies of similar
    /// size, which is the case a uniform grid is best at and the case every
    /// cleverer structure pays overhead to handle.
    /// </para>
    /// </remarks>
    internal sealed class UnitIndex
    {
        /// <summary>
        /// How wide a bucket is, in metres.
        /// </summary>
        /// <remarks>
        /// Comfortably wider than any regiment's bounding radius, so a query's
        /// widening never spans more than a bucket or two, and coarse enough
        /// that a two-kilometre field is a few hundred buckets rather than
        /// hundreds of thousands. The structure is rebuilt whenever anything
        /// moves, so a fine grid would cost more to fill than it saves.
        /// </remarks>
        private const float BucketMetres = 128f;

        private readonly List<UnitInstance>[] _buckets;
        private readonly int _columns;
        private readonly int _rows;
        private readonly Vec2 _origin;

        private float _widestReach;
        private volatile bool _built;

        private readonly object _filing = new object();

        /// <summary>
        /// One thread's marks, so that two queries at once cannot cross out
        /// each other's buckets.
        /// </summary>
        /// <remarks>
        /// The dedup below is a stamp per bucket and a counter per query, which
        /// is the cheapest way to say "already emptied into this answer" and
        /// costs nothing to reset. Shared between threads it is worse than
        /// useless: one query's counter moving marks another query's buckets as
        /// already seen, and a bucket skipped is a body the caller is never told
        /// about. Measured on eighty orders given at once - the routes came back
        /// different from the same eighty given one at a time, and one of them
        /// walked through a regiment.
        /// </remarks>
        private sealed class Marks
        {
            public int[] Visited = System.Array.Empty<int>();
            public int Sweep;
        }

        private readonly System.Threading.ThreadLocal<Marks> _marks =
            new System.Threading.ThreadLocal<Marks>(() => new Marks());

        public UnitIndex(MapBounds bounds)
        {
            _origin = bounds.Min;

            Vec2 span = bounds.Max - bounds.Min;

            _columns = Math.Max(1, (int)MathF.Ceiling(span.X / BucketMetres));
            _rows = Math.Max(1, (int)MathF.Ceiling(span.Y / BucketMetres));

            _buckets = new List<UnitInstance>[_columns * _rows];
            for (int i = 0; i < _buckets.Length; i++)
                _buckets[i] = new List<UnitInstance>();

        }

        /// <summary>Throws away what was filed, so the next query refiles it.</summary>
        public void Invalidate() => _built = false;

        /// <summary>
        /// Every body that could reach within <paramref name="reach"/> of the
        /// segment, and possibly a few that could not.
        /// </summary>
        /// <remarks>
        /// Widened, never narrowed. The caller still runs whatever exact test it
        /// was going to run; this only spares it the ones that could not
        /// possibly matter, so a body returned in error costs a comparison and a
        /// body missed would be a collision nobody saw.
        /// </remarks>
        public void Near(
            IReadOnlyList<UnitInstance> units, Vec2 from, Vec2 to, float reach, List<UnitInstance> into)
        {
            into.Clear();
            Build(units);

            Marks marks = MarksForThisThread();

            // Everything within this of some sample point along the line is
            // caught, and the samples are close enough together that no point on
            // the line is further than half a bucket from one of them.
            float halo = reach + _widestReach + BucketMetres * 0.5f;

            Vec2 travel = to - from;
            float length = travel.Length;

            int samples = length <= 0f
                ? 1
                : 1 + (int)MathF.Ceiling(length / (BucketMetres * 0.5f));

            marks.Sweep++;

            for (int s = 0; s < samples; s++)
            {
                Vec2 at = samples == 1 ? from : Vec2.Lerp(from, to, s / (float)(samples - 1));
                Gather(at, halo, into, marks);
            }
        }

        /// <summary>Every body that could reach within <paramref name="reach"/> of a point.</summary>
        public void Near(IReadOnlyList<UnitInstance> units, Vec2 at, float reach, List<UnitInstance> into)
        {
            into.Clear();
            Build(units);

            Marks marks = MarksForThisThread();

            marks.Sweep++;
            Gather(at, reach + _widestReach, into, marks);
        }

        private Marks MarksForThisThread()
        {
            Marks marks = _marks.Value!;

            if (marks.Visited.Length < _buckets.Length)
                marks.Visited = new int[_buckets.Length];

            return marks;
        }

        private void Gather(Vec2 at, float halo, List<UnitInstance> into, Marks marks)
        {
            int lowColumn = Column(at.X - halo);
            int highColumn = Column(at.X + halo);
            int lowRow = Row(at.Y - halo);
            int highRow = Row(at.Y + halo);

            for (int row = lowRow; row <= highRow; row++)
            for (int column = lowColumn; column <= highColumn; column++)
            {
                int bucket = row * _columns + column;

                // Buckets are disjoint, so a bucket already emptied into this
                // answer has nothing new to say.
                if (marks.Visited[bucket] == marks.Sweep) continue;
                marks.Visited[bucket] = marks.Sweep;

                List<UnitInstance> held = _buckets[bucket];
                for (int i = 0; i < held.Count; i++)
                    into.Add(held[i]);
            }
        }

        /// <summary>
        /// Files every body, once, however many threads ask at the same moment.
        /// </summary>
        /// <remarks>
        /// The gate is taken only on the first query after a move, so the cost
        /// falls on one query a tick rather than on all of them; everything
        /// after it reads a volatile flag and goes straight through.
        /// </remarks>
        private void Build(IReadOnlyList<UnitInstance> units)
        {
            if (_built) return;

            lock (_filing)
            {
                if (_built) return;
                Refile(units);
            }
        }

        private void Refile(IReadOnlyList<UnitInstance> units)
        {
            for (int i = 0; i < _buckets.Length; i++)
                _buckets[i].Clear();

            _widestReach = 0f;

            for (int i = 0; i < units.Count; i++)
            {
                UnitInstance unit = units[i];

                // The dead are filed too. Every caller already asks whether a
                // body is on the field or still fighting, and leaving them in
                // means a regiment being destroyed does not have to be another
                // thing that remembers to invalidate this.
                float radius = unit.Footprint.BoundingRadius;
                if (radius > _widestReach) _widestReach = radius;

                Vec2 at = unit.Position;
                _buckets[Row(at.Y) * _columns + Column(at.X)].Add(unit);
            }

            _built = true;
        }

        private int Column(float x) =>
            Math.Clamp((int)MathF.Floor((x - _origin.X) / BucketMetres), 0, _columns - 1);

        private int Row(float y) =>
            Math.Clamp((int)MathF.Floor((y - _origin.Y) / BucketMetres), 0, _rows - 1);
    }
}
