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
            marks.Sweep++;

            // The corridor this query is really asking about: everything whose
            // own reach can touch the line.
            float corridor = reach + _widestReach;

            Vec2 travel = to - from;
            float length = travel.Length;

            if (length <= 0f)
            {
                Gather(from, corridor, into, marks);
                return;
            }

            // M84. One pass over the buckets the corridor covers, rather than a
            // square of buckets gathered at each of a line of sample points.
            //
            // The old shape sampled every half bucket and gathered a square of
            // side twice the halo at each — and the halo carried half a bucket
            // of its own so that no point on the line fell between samples. At
            // 128 m buckets and a regiment's 45 m reach that is a four-by-four
            // square taken eight times down a 400 m leg: **128 bucket visits to
            // cover about fourteen distinct buckets**. The marks array made the
            // repeats cheap rather than wrong, so nothing was ever incorrect —
            // it just did nine times the bounds arithmetic it needed to.
            //
            // The single pass is also *tighter*: a bucket is taken only if its
            // own square can reach the corridor, where the old halo carried the
            // sampling slack whether or not the geometry needed it. So this
            // hands back a smaller set as well as building it in one sweep, and
            // every body the old one could return is still in it.
            float half = BucketMetres * 0.5f;
            float diagonal = half * 1.41421356f;
            float span = corridor + diagonal;

            int lowColumn = Column(MathF.Min(from.X, to.X) - span);
            int highColumn = Column(MathF.Max(from.X, to.X) + span);
            int lowRow = Row(MathF.Min(from.Y, to.Y) - span);
            int highRow = Row(MathF.Max(from.Y, to.Y) + span);

            Vec2 along = travel / length;
            float reachSquared = span * span;

            for (int row = lowRow; row <= highRow; row++)
            for (int column = lowColumn; column <= highColumn; column++)
            {
                int bucket = row * _columns + column;

                if (marks.Visited[bucket] == marks.Sweep) continue;

                // The bucket's own square is what has to reach the corridor,
                // so its centre is allowed to sit a half-diagonal further off.
                var centre = new Vec2(
                    _origin.X + (column + 0.5f) * BucketMetres,
                    _origin.Y + (row + 0.5f) * BucketMetres);

                if (FarFromSegment(centre, from, along, length) > reachSquared) continue;

                marks.Visited[bucket] = marks.Sweep;

                List<UnitInstance> held = _buckets[bucket];
                for (int i = 0; i < held.Count; i++)
                    into.Add(held[i]);
            }
        }

        /// <summary>How far a point lies off a segment, squared.</summary>
        private static float FarFromSegment(Vec2 point, Vec2 from, Vec2 along, float length)
        {
            Vec2 offset = point - from;

            float onto = offset.X * along.X + offset.Y * along.Y;

            if (onto < 0f) onto = 0f;
            else if (onto > length) onto = length;

            float dx = offset.X - along.X * onto;
            float dy = offset.Y - along.Y * onto;

            return dx * dx + dy * dy;
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
