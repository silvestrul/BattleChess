using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace BattleChess.Contracts
{
    /// <summary>
    /// A stopwatch on every major step a plan goes through, so that "planning
    /// costs ten seconds" can be answered with <i>which part of it</i>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why this exists.</b> The counters already on <c>RouteEffort</c> say
    /// how much work was done — legs priced, states expanded, bodies pulled in.
    /// They cannot say what any of it cost, and the two do not track each other:
    /// a step taken a thousand times may be free and one taken twice may be the
    /// whole bill. Every performance finding in this project so far came from
    /// guessing which of those it was and then measuring to check.
    /// </para>
    /// <para>
    /// <b>Inclusive and self time.</b> Steps nest — a plan contains a search,
    /// which contains clearance checks, which contain terrain lookups — so a
    /// plain total would count the same microsecond a dozen times. Each step
    /// therefore records both: <i>inclusive</i>, everything under it, and
    /// <i>self</i>, inclusive minus whatever its own children reported. Self
    /// time is what sums to the whole, and it is the column to read when asking
    /// where the time actually went.
    /// </para>
    /// <para>
    /// <b>Counted-only steps.</b> The innermost geometry runs millions of times
    /// a plan, and two <see cref="Stopwatch.GetTimestamp"/> calls around each
    /// would cost more than the work being measured. Those are counted rather
    /// than timed — see <see cref="Tally"/> — and their cost appears as self
    /// time in whichever timed step contains them.
    /// </para>
    /// <para>
    /// <b>Off by default, and nearly free when off.</b> Disabled, every probe is
    /// a static bool read and a branch not taken. Nothing in a played battle
    /// turns it on; the benches do, and they report what the instrumentation
    /// itself cost by running the same work with it off.
    /// </para>
    /// <para>
    /// <b>Per thread, and it had to be.</b> This was first written with one
    /// shared set of counters and a global switch, on the reasoning that one
    /// battle steps on one thread. That is true of a battle and false of the
    /// test runner, which runs classes in parallel: a bench turning the switch
    /// on instrumented every unrelated test running beside it, and the shared
    /// depth counter raced until an `IndexOutOfRangeException` came out of the
    /// middle of an unrelated search. Seven tests failed that had nothing to do
    /// with it. So the switch and the counters are both thread-local — a thread
    /// measures only its own work, and a thread that never asked pays a single
    /// static bool read.
    /// </para>
    /// </remarks>
    public static class PlanningProfile
    {
        /// <summary>Every step worth a separate line in the report.</summary>
        /// <remarks>
        /// Ordered roughly outermost first, so the nesting can be read down the
        /// enum. Everything from <see cref="Step.SweepTest"/> on is counted
        /// rather than timed.
        /// </remarks>
        public enum Step
        {
            /// <summary>A whole plan, from the order to the route — the top of the tree.</summary>
            Plan,

            /// <summary>Working out which points the search is allowed to consider at all.</summary>
            CandidatePlaces,

            /// <summary>The search over those places, all rounds of it.</summary>
            Hunt,

            /// <summary>Growing the candidate set after a round found nothing.</summary>
            GrowPlaces,

            /// <summary>Dropping waypoints the sweep can see past.</summary>
            SmoothRoute,

            /// <summary>The four-rung ladder, as the planner or as a second opinion.</summary>
            Ladder,

            /// <summary>One of the older way-round strategies - the ladder's arch.</summary>
            WayRound,

            /// <summary>
            /// The ladder's other rung-two candidate: the same line taken
            /// side-on, so a body twenty metres across instead of forty.
            /// </summary>
            /// <remarks>
            /// Split from <see cref="Ladder"/> because rung two computes both
            /// candidates and prices them against each other, and the crab is
            /// worked out <i>even when the arch succeeded</i> - deliberately,
            /// so that the cheaper of the two wins rather than the first one
            /// found. Whether that deliberate extra is a rounding error or a
            /// sixth of the ladder could not be read while both were one line.
            /// </remarks>
            Crab,

            /// <summary>Can this body travel this line without meeting anything.</summary>
            ClearLine,

            /// <summary>Walking every body on the field to see if it is in the way.</summary>
            BodyScan,

            /// <summary>
            /// Asking the spatial index which bodies lie near one line, inside
            /// <see cref="BodyScan"/>.
            /// </summary>
            /// <remarks>
            /// The same split, for the same reason. <see cref="BodyScan"/> is
            /// the heaviest step in the whole planner on every field and every
            /// planner but one, and "make it faster" means nothing until it is
            /// known whether the time is in finding the candidates or in
            /// testing them.
            /// </remarks>
            NearQuery,

            /// <summary>How deep a leg laps a body it started inside, sampled along the leg.</summary>
            GrazeAlong,

            /// <summary>The terrain half of that question.</summary>
            GroundClear,

            /// <summary>The summed-area early-out over impassable ground.</summary>
            PassableTable,

            /// <summary>Does the whole footprint fit on the ground here.</summary>
            FormationFits,

            /// <summary>Can the regiment stand at this point on this front.</summary>
            StandCheck,

            /// <summary>Is there room to turn on the spot here.</summary>
            TurnCheck,

            /// <summary>A* over the hex grid.</summary>
            HexSearch,

            /// <summary>String-pulling the hex route back into a few waypoints.</summary>
            PathSmooth,

            /// <summary>
            /// Raising the regiment-sized hex field — sizing the cells and
            /// marking which of them a body holds.
            /// </summary>
            /// <remarks>
            /// Separate from <see cref="HexSearch"/>, which is the terrain
            /// pathfinder over 25 m cells. This is the grid sized to the mover's
            /// own bounding circle, and the two answer different questions on
            /// different cells; sharing a line would have made both unreadable.
            /// </remarks>
            GridField,

            /// <summary>A* over that field, and the string-pull after it.</summary>
            GridSearch,

            /// <summary>The whole hybrid lattice search — the top of its own tree.</summary>
            HybridSearch,

            /// <summary>Flood-filling the obstacle field the hybrid steers by.</summary>
            HybridField,

            /// <summary>
            /// Marking which of that field's cells a body holds, before the
            /// fill runs over them.
            /// </summary>
            /// <remarks>
            /// Split out from <see cref="HybridField"/> because the two halves
            /// have different lifetimes and only one of them can be shared: the
            /// raster depends on where the bodies are, which is the same for
            /// every order given on one tick, while the fill counts seconds to
            /// <i>this</i> order's goal. Whether sharing the raster is worth
            /// building depends entirely on which half the time is in, and
            /// nothing could answer that while they were one line.
            /// </remarks>
            /// <remarks>
            /// It measures <see cref="HybridPlanning.HybridTurnField"/>'s
            /// raster, not <c>HybridObstacleField</c>'s. That was worth finding
            /// out: the obstacle field is built only when the turn-aware
            /// heuristic is off or a corridor was asked for, so in the shipped
            /// configuration it is never built at all and every millisecond
            /// under <see cref="HybridField"/> belongs to the turn field.
            /// </remarks>
            HybridRaster,

            /// <summary>Working out which bodies are near, once per expansion.</summary>
            HybridStock,

            /// <summary>Sweeping one motion primitive against the bodies near it.</summary>
            HybridClear,

            /// <summary>Estimating what is left to travel, from one state.</summary>
            HybridHeuristic,

            /// <summary>Driving straight at the destination from one state, to see if it lands.</summary>
            HybridShot,

            /// <summary>Swept-rectangle first contact. Counted only.</summary>
            SweepTest,

            /// <summary>Rectangle against rectangle. Counted only.</summary>
            OverlapTest,

            /// <summary>One terrain lookup at one point. Counted only.</summary>
            TerrainLookup,

            /// <summary>One pose of the mover, checked against everything near. Counted only.</summary>
            HybridPose,

            /// <summary>One rectangle against one rectangle, by separating axis. Counted only.</summary>
            HybridOverlap,

            /// <summary>
            /// One body handed back by the spatial index. Counted only, and
            /// counted in bulk rather than one at a time.
            /// </summary>
            /// <remarks>
            /// The question it exists to answer: the clearance path asks the
            /// index once a leg and the hybrid asks once a node, so hoisting the
            /// query to once an order is the obvious saving - but only if the
            /// list it hands back does not grow faster than the queries shrink.
            /// This is the numerator of that trade.
            /// </remarks>
            NearYield,

            /// <summary>One call to the index that returned nothing at all. Counted only.</summary>
            NearEmpty,

            /// <summary>
            /// One bucket the index opened to answer a query. Counted only.
            /// </summary>
            /// <remarks>
            /// The denominator to <see cref="NearYield"/>'s numerator. A query
            /// costs bounds arithmetic per bucket and a comparison per body, so
            /// which of the two dominates decides whether the grid wants to be
            /// finer or coarser - and nothing had ever counted the buckets.
            /// </remarks>
            NearBuckets,

            /// <summary>
            /// A clearance check on a leg this order has already checked, on the
            /// same front. Counted only.
            /// </summary>
            /// <remarks>
            /// One order runs five hundred and fifty clearance checks on the
            /// Crucible, and the cascade proves the same route more than once by
            /// construction: the ladder's route is proved, the grid's route is
            /// smoothed and proved, the lattice's route is proved, and then the
            /// winner is smoothed and proved again. Whether that is most of the
            /// five hundred and fifty or a rounding error on it decides whether
            /// a per-order memo is the largest saving left or a waste of a
            /// hash lookup.
            /// </remarks>
            ClearLineRepeat,

            /// <summary>Not a step. The number of them.</summary>
            Count,
        }

        /// <summary>The first step that is counted rather than timed.</summary>
        private const Step FirstCountedOnly = Step.SweepTest;

        private const int MaxDepth = 64;

        /// <summary>
        /// Whether <i>any</i> thread is measuring. Plain and static so that the
        /// disabled case — every shipped battle — stays one static read and a
        /// branch not taken, which a thread-local read would not be.
        /// </summary>
        private static volatile bool _anyone;

        /// <summary>Whether <i>this</i> thread asked to be measured.</summary>
        [ThreadStatic] private static bool _mine;

        [ThreadStatic] private static long[]? _inclusive;
        [ThreadStatic] private static long[]? _inChildren;
        [ThreadStatic] private static long[]? _calls;
        [ThreadStatic] private static int[]? _stack;
        [ThreadStatic] private static int _depth;

        /// <summary>Whether this thread is measuring.</summary>
        public static bool Enabled => _anyone && _mine;

        /// <summary>Forgets everything this thread has measured.</summary>
        public static void Reset()
        {
            if (_calls == null) return;

            Array.Clear(_inclusive!, 0, _inclusive!.Length);
            Array.Clear(_inChildren!, 0, _inChildren!.Length);
            Array.Clear(_calls, 0, _calls.Length);

            _depth = 0;
        }

        /// <summary>Turns measuring on for the calling thread, from nothing.</summary>
        public static void Start()
        {
            _inclusive ??= new long[(int)Step.Count];
            _inChildren ??= new long[(int)Step.Count];
            _calls ??= new long[(int)Step.Count];
            _stack ??= new int[MaxDepth];

            Reset();

            _mine = true;
            _anyone = true;
        }

        /// <summary>
        /// Stops measuring on the calling thread, keeping what it measured.
        /// </summary>
        /// <remarks>
        /// <see cref="_anyone"/> stays set: it is a fast gate rather than a
        /// count, and leaving it on costs a thread-local read on threads that
        /// are not measuring, where clearing it while another thread is still
        /// measuring would silently stop that thread instead.
        /// </remarks>
        public static void Stop() => _mine = false;

        /// <summary>Records that a counted-only step happened once.</summary>
        /// <summary>Whether this thread is measuring. For measurement-only bookkeeping.</summary>
        public static bool Running => _anyone && _mine;

        public static void Tally(Step step)
        {
            if (!_anyone || !_mine) return;

            _calls![(int)step]++;
        }

        /// <summary>The same, by more than one at a time.</summary>
        public static void Tally(Step step, int many)
        {
            if (!_anyone || !_mine) return;

            _calls![(int)step] += many;
        }

        /// <summary>
        /// Times everything until the returned scope is disposed. Meant for
        /// <c>using</c>, which is what makes it safe against an exception
        /// unwinding through the middle of a step.
        /// </summary>
        public static Scope Measure(Step step) => new Scope(step);

        /// <summary>How many times a step was entered.</summary>
        public static long CallsTo(Step step) => _calls == null ? 0L : _calls[(int)step];

        /// <summary>Milliseconds spent in a step and everything under it.</summary>
        public static double InclusiveMilliseconds(Step step) =>
            _inclusive == null ? 0d : ToMilliseconds(_inclusive[(int)step]);

        /// <summary>
        /// Milliseconds spent in a step's own code, its children's time taken
        /// out. These are the numbers that sum to the whole.
        /// </summary>
        public static double SelfMilliseconds(Step step) =>
            _inclusive == null ? 0d : ToMilliseconds(_inclusive[(int)step] - _inChildren![(int)step]);

        private static double ToMilliseconds(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;

        /// <summary>The whole measurement as a table, heaviest self time first.</summary>
        /// <param name="title">What was being measured, printed above the table.</param>
        public static string Report(string title)
        {
            var timed = new List<Step>();
            var counted = new List<Step>();

            for (int i = 0; i < (int)Step.Count; i++)
            {
                if (CallsTo((Step)i) == 0) continue;

                if ((Step)i < FirstCountedOnly) timed.Add((Step)i);
                else counted.Add((Step)i);
            }

            timed.Sort((a, b) => SelfMilliseconds(b).CompareTo(SelfMilliseconds(a)));

            double totalSelf = 0d;
            foreach (Step step in timed) totalSelf += SelfMilliseconds(step);

            var text = new StringBuilder();

            text.AppendLine(title);
            text.AppendLine(
                $"{"step",-17}{"calls",12}{"self ms",10}{"self %",9}{"incl ms",10}{"us/call",10}");
            text.AppendLine(new string('-', 68));

            foreach (Step step in timed)
            {
                double self = SelfMilliseconds(step);
                long calls = CallsTo(step);

                text.AppendLine(
                    $"{step,-17}{calls,12:N0}{self,10:0.0}" +
                    $"{(totalSelf > 0d ? self / totalSelf : 0d),9:0.0%}" +
                    $"{InclusiveMilliseconds(step),10:0.0}" +
                    $"{InclusiveMilliseconds(step) * 1000d / Math.Max(1L, calls),10:0.00}");
            }

            text.AppendLine(new string('-', 68));
            text.AppendLine($"{"total self",-17}{string.Empty,12}{totalSelf,10:0.0}");

            if (counted.Count > 0)
            {
                text.AppendLine();
                text.AppendLine("counted, not timed — their cost is self time in the steps above");

                foreach (Step step in counted)
                    text.AppendLine($"{step,-17}{CallsTo(step),12:N0}");
            }

            return text.ToString();
        }

        /// <summary>
        /// One step being timed. A <c>ref struct</c> so it cannot outlive the
        /// stack frame it was opened on, which is what keeps the depth honest.
        /// </summary>
        /// <remarks>
        /// A step entered inside itself — recursion — double-counts its own
        /// inclusive time, because the inner call's total is charged to the
        /// outer as a child. Self time stays right, which is the column that
        /// matters, and nothing timed here currently recurses.
        /// </remarks>
        public readonly ref struct Scope
        {
            private readonly int _step;
            private readonly long _began;
            private readonly bool _measuring;

            internal Scope(Step step)
            {
                if (!_anyone || !_mine || _depth >= MaxDepth)
                {
                    _step = 0;
                    _began = 0L;
                    _measuring = false;

                    return;
                }

                _step = (int)step;
                _began = Stopwatch.GetTimestamp();
                _measuring = true;

                _stack![_depth++] = _step;
            }

            public void Dispose()
            {
                if (!_measuring) return;

                long spent = Stopwatch.GetTimestamp() - _began;

                _inclusive![_step] += spent;
                _calls![_step]++;

                _depth--;

                // Charged to whoever opened the step above this one, so that
                // its self time comes out net of this.
                if (_depth > 0) _inChildren![_stack![_depth - 1]] += spent;
            }
        }
    }
}
