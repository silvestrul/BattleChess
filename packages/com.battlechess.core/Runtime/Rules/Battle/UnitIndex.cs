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
    /// <b>M109. A regiment is filed under every bucket its own circle reaches</b>,
    /// which is the trade the designer authorised - <i>"if an algorythm can be
    /// optimised for speed rather than memory then its a good idea"</i>. It used
    /// to be filed under the one bucket its <b>centre</b> sat in, so that
    /// buckets held disjoint sets and a query needed no de-duplicating pass.
    /// That saved a stamp per body and cost far more than it saved, because a
    /// body filed one bucket over can still reach in - so <b>every query had to
    /// widen itself by the widest bounding radius on the whole field</b>, and
    /// then by half a bucket diagonal on top, which at 128 m buckets is 181 m of
    /// slack around a reach of about ninety. [M95] measured that slack, called
    /// it the price of the arrangement and swept the bucket size instead; the
    /// arrangement itself was the thing to change.
    /// </para>
    /// <para>
    /// Filing by reach makes the query exact in the only way that matters: it
    /// asks for bodies within <c>reach</c> of the line and no longer for bodies
    /// within <c>reach + widest reach</c>. A body whose circle comes near the
    /// line covers some point near the line, that point is in some bucket, and
    /// the body is filed there - so nothing can be missed, and the corridor
    /// stops carrying somebody else's radius. The dedup it costs is a stamp per
    /// body per query against an array indexed by the body's place in the order
    /// of battle.
    /// </para>
    /// <para>
    /// It also unlocks the bucket size, which was pinned by the widening. See
    /// <see cref="BucketMetres"/>.
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
        /// <para>
        /// <b>A lever, and measured — [M95].</b> The obvious complaint about
        /// this number is that it makes the halo enormous: a query widens by
        /// <c>reach + widest reach</c>, takes a bucket whose centre is within
        /// half a diagonal of that, and a body may then sit half a diagonal
        /// outside the bucket it was filed in — <b>181 m of slack around a
        /// reach of about ninety</b>. That is true and it is not a cost. Swept
        /// from 32 m to 768, the bodies a query hands back fall from 37 to 8
        /// while the buckets it opens rise from 2 to 59, and the second is the
        /// dearer of the two: <c>NearQuery</c> is flat from 512 down to 128 and
        /// then <b>doubles</b> by 32, while <c>BodyScan</c> falls only a
        /// quarter over the same range. Everything between 128 and 512 is one
        /// flat basin with no reading outside the noise; both ends are worse.
        /// So this stays where it is, now for a measured reason.
        /// </para>
        /// </remarks>
        internal static float BucketMetres = 128f;

        private readonly List<int>[] _buckets;

        /// <summary>The order of battle as it was filed, indexed by bucket entry.</summary>
        private IReadOnlyList<UnitInstance> _filed = System.Array.Empty<UnitInstance>();
        private readonly int _columns;
        private readonly int _rows;
        private readonly Vec2 _origin;

        /// <summary>
        /// The bucket width this index was actually built at.
        /// </summary>
        /// <remarks>
        /// Read once, in the constructor. <see cref="BucketMetres"/> is a lever
        /// and a lever moved between two queries of one index would have the
        /// second query walk the first one's grid with the wrong arithmetic.
        /// </remarks>
        private readonly float _bucketMetres;

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

            /// <summary>
            /// Which query last handed back each body, so that a body filed in
            /// four buckets is still returned once.
            /// </summary>
            public int[] Handed = System.Array.Empty<int>();

            public int Sweep;
        }

        private readonly System.Threading.ThreadLocal<Marks> _marks =
            new System.Threading.ThreadLocal<Marks>(() => new Marks());

        public UnitIndex(MapBounds bounds)
        {
            _origin = bounds.Min;
            _bucketMetres = MathF.Max(1f, BucketMetres);

            Vec2 span = bounds.Max - bounds.Min;

            _columns = Math.Max(1, (int)MathF.Ceiling(span.X / _bucketMetres));
            _rows = Math.Max(1, (int)MathF.Ceiling(span.Y / _bucketMetres));

            _buckets = new List<int>[_columns * _rows];
            for (int i = 0; i < _buckets.Length; i++)
                _buckets[i] = new List<int>();

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

            // The corridor this query is really asking about. Since M109 a body
            // is filed under every bucket it reaches, so this is the caller's
            // own reach and carries nobody else's radius.
            float corridor = reach;

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
            float half = _bucketMetres * 0.5f;
            float diagonal = half * 1.41421356f;
            float span = corridor + diagonal;

            int lowRow = Row(MathF.Min(from.Y, to.Y) - span);
            int highRow = Row(MathF.Max(from.Y, to.Y) + span);

            Vec2 along = travel / length;
            float reachSquared = span * span;

            for (int row = lowRow; row <= highRow; row++)
            {
                // M106. The columns this row can possibly want, rather than the
                // columns the whole leg's bounding box wants.
                //
                // A march is nearly always diagonal, and a rectangle drawn round
                // a diagonal line is mostly not near the line: measured, a query
                // looked at forty buckets to keep eleven, and the thirty it
                // threw away each cost a bucket centre, a projection onto the
                // segment and a squared distance. Clipping the segment to the
                // row's own band first and taking the columns of *that* leaves
                // the arithmetic per bucket exactly as it was and asks it of far
                // fewer.
                //
                // Still a superset, which is the only thing that matters here: a
                // bucket whose centre is within span of the segment has its
                // nearest point on the segment within span in y as well, so that
                // point lies inside the band this clips to, so its x is inside
                // the range taken - and the centre is then within span of that x.
                float bandLow = _origin.Y + row * _bucketMetres - span;
                float bandHigh = bandLow + _bucketMetres + 2f * span;

                float leftX, rightX;

                if (MathF.Abs(travel.Y) < 1e-4f)
                {
                    leftX = MathF.Min(from.X, to.X);
                    rightX = MathF.Max(from.X, to.X);
                }
                else
                {
                    float first = (bandLow - from.Y) / travel.Y;
                    float last = (bandHigh - from.Y) / travel.Y;

                    if (first > last) { (first, last) = (last, first); }

                    first = Math.Clamp(first, 0f, 1f);
                    last = Math.Clamp(last, 0f, 1f);

                    float atFirst = from.X + travel.X * first;
                    float atLast = from.X + travel.X * last;

                    leftX = MathF.Min(atFirst, atLast);
                    rightX = MathF.Max(atFirst, atLast);
                }

                int lowColumn = Column(leftX - span);
                int highColumn = Column(rightX + span);

                for (int column = lowColumn; column <= highColumn; column++)
                {
                    int bucket = row * _columns + column;

                    PlanningProfile.Tally(PlanningProfile.Step.NearBucketsSeen);

                    if (marks.Visited[bucket] == marks.Sweep) continue;

                    // The bucket's own square is what has to reach the
                    // corridor, so its centre is allowed to sit a half-diagonal
                    // further off.
                    var centre = new Vec2(
                        _origin.X + (column + 0.5f) * _bucketMetres,
                        _origin.Y + (row + 0.5f) * _bucketMetres);

                    if (FarFromSegment(centre, from, along, length) > reachSquared) continue;

                    marks.Visited[bucket] = marks.Sweep;
                    PlanningProfile.Tally(PlanningProfile.Step.NearBuckets);

                    Hand(bucket, into, marks, from, along, length, corridor);
                }
            }
        }

        /// <summary>
        /// Empties one bucket into an answer, once per body however many buckets
        /// it is filed in.
        /// </summary>
        private void Hand(
            int bucket, List<UnitInstance> into, Marks marks,
            Vec2 from, Vec2 along, float length, float reach)
        {
            List<int> held = _buckets[bucket];

            for (int i = 0; i < held.Count; i++)
            {
                int who = held[i];

                if (marks.Handed[who] == marks.Sweep) continue;

                // Stamped before the test, not after. A body rejected against
                // this line is rejected against it from every bucket it is filed
                // in, so the stamp saves the repeats as well as the duplicates.
                marks.Handed[who] = marks.Sweep;

                UnitInstance body = _filed[who];

                if (SiftAtTheIndex)
                {
                    // The bucket test admits a body because its *bucket* reaches
                    // the corridor; this asks whether the body does. Conservative
                    // by construction - the bounding radius circumscribes the
                    // footprint, so nothing the sweep would have touched is
                    // refused here.
                    float allowed = reach + body.Footprint.BoundingRadius;

                    if (FarFromSegment(body.Position, from, along, length) > allowed * allowed)
                    {
                        PlanningProfile.Tally(PlanningProfile.Step.NearSifted);
                        continue;
                    }
                }

                into.Add(body);
            }
        }

        /// <summary>
        /// Whether a body is tested against the line before being handed back,
        /// rather than only its bucket being tested.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>M118.</b> The designer: <i>"but bodyscan doesnt have to be used
        /// against 300 bodies, only the ones in the radius right?"</i>. It is not
        /// used against three hundred - measured, a query hands back <b>12,8</b>
        /// - but only <b>1,48</b> of those are worth a sweep test, so seven in
        /// eight are handed back and then thrown away by the caller.
        /// </para>
        /// <para>
        /// The gap is the bucket. A bucket is 128 m and a query keeps it if its
        /// <i>square</i> reaches the corridor, so a body sitting in the far
        /// corner of a kept bucket comes back although it is nowhere near the
        /// line. Testing the body itself costs a projection and a squared
        /// distance - the same arithmetic already spent on the bucket centre -
        /// and saves the caller a list entry and a rejection.
        /// </para>
        /// <para>
        /// A lever rather than a constant, because the honest question is
        /// whether moving a rejection earlier is cheaper than doing it later.
        /// </para>
        /// <para>
        /// <b>Measured, and it loses. Off.</b> It does exactly what it was built
        /// to do - the yield falls from 12,80 bodies a query to <b>3,35</b>, and
        /// every route on every field is unchanged - and an order costs
        /// <b>9% to 16% more</b> on all five fields. The rejection it moves
        /// earlier was cheaper where it was: the caller refuses a body on a
        /// bounding test of a few compares, and this refuses it on a projection
        /// onto the segment, a clamp and a squared distance, asked 171 537 times
        /// a field. <b>Kept behind the switch as a measurement rather than
        /// deleted, so the idea is not had again.</b>
        /// </para>
        /// <para>
        /// Third time the same lesson: [M111] and [M116] both lost by trying to
        /// make the clearance query more precise. The list was never the cost.
        /// </para>
        /// </remarks>
        public static bool SiftAtTheIndex;

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
            Gather(at, reach, into, marks);
        }

        private Marks MarksForThisThread()
        {
            Marks marks = _marks.Value!;

            if (marks.Visited.Length < _buckets.Length)
                marks.Visited = new int[_buckets.Length];

            if (marks.Handed.Length < _filed.Count)
                marks.Handed = new int[Math.Max(64, _filed.Count * 2)];

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

                // A bucket already emptied into this answer has nothing new to
                // say; a body it shares with another bucket is caught by Hand.
                if (marks.Visited[bucket] == marks.Sweep) continue;
                marks.Visited[bucket] = marks.Sweep;
                PlanningProfile.Tally(PlanningProfile.Step.NearBuckets);

                Hand(bucket, into, marks, at, new Vec2(1f, 0f), 0f, halo);
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
            _filed = units;

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

                // Every bucket the body's own circle reaches. Its square, not
                // the circle - a bucket that only the corner of the bounding
                // box touches holds the body needlessly, which costs one
                // comparison in one query and saves the arithmetic here on
                // every refile.
                int lowColumn = Column(at.X - radius);
                int highColumn = Column(at.X + radius);
                int lowRow = Row(at.Y - radius);
                int highRow = Row(at.Y + radius);

                for (int row = lowRow; row <= highRow; row++)
                for (int column = lowColumn; column <= highColumn; column++)
                    _buckets[row * _columns + column].Add(i);
            }

            _built = true;
        }

        private int Column(float x) =>
            Math.Clamp((int)MathF.Floor((x - _origin.X) / _bucketMetres), 0, _columns - 1);

        private int Row(float y) =>
            Math.Clamp((int)MathF.Floor((y - _origin.Y) / _bucketMetres), 0, _rows - 1);
    }
}
