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

            /// <summary>
            /// Rung one on its own: is the straight line clear.
            /// </summary>
            /// <remarks>
            /// Split out of <see cref="Ladder"/> because the two rungs answer
            /// different questions at wildly different prices - one swept
            /// rectangle against a whole way-round search - and a single line
            /// covering both says which cascade stage was entered without ever
            /// saying which of them cost anything.
            /// </remarks>
            Rung1,

            /// <summary>
            /// The regiment grid at its ordinary spacing, inclusive.
            /// </summary>
            /// <remarks>
            /// A wrapper around the call site rather than a new measurement:
            /// <see cref="GridField"/> and <see cref="GridSearch"/> are what it
            /// contains, and they are shared with the tier below, so without
            /// this and <see cref="GridFine"/> there is no way to tell a coarse
            /// grid answering cheaply from a fine one answering dearly.
            /// </remarks>
            GridCoarse,

            /// <summary>The finer tiers, M87, inclusive of every spacing tried.</summary>
            GridFine,

            /// <summary>Drawing the tangent visibility graph, M86.</summary>
            TangentGraph,

            /// <summary>The lattice over poses, asked before anybody is pressed through.</summary>
            PoseSearch,

            /// <summary>Raising the field at a finer spacing than the ordinary one.</summary>
            /// <remarks>
            /// Separate from <see cref="GridField"/> rather than added to it,
            /// because the two tiers differ by sixteen times the cells and
            /// averaging them together answers neither question. The coarse
            /// tier runs on every order that gets past the ladder; the fine one
            /// runs on a handful and is the tail.
            /// </remarks>
            GridFieldFine,

            /// <summary>A* over a finer field.</summary>
            GridSearchFine,

            /// <summary>
            /// Hashing every regiment's place and front, to know whether a kept
            /// field is stale.
            /// </summary>
            /// <remarks>
            /// Its own line because it is paid on every call whether or not a
            /// field is built, so it is the floor under the grid rather than
            /// part of its cost - and because "a hash over eighty units costs
            /// microseconds" is a claim in the code that nothing measured.
            /// </remarks>
            FieldStamp,

            /// <summary>Marking which cells the bodies hold. The sampling itself.</summary>
            FieldMark,

            /// <summary>
            /// Bringing a kept field up to date: unmarking the bodies that have
            /// moved and marking them again where they now stand.
            /// </summary>
            /// <remarks>
            /// Its own line rather than folded into <see cref="FieldMark"/>,
            /// because the two answer different questions. FieldMark is what a
            /// field costs to raise; this is what it costs to <i>keep</i>, and
            /// the second is the one a battle pays every tick.
            /// </remarks>
            FieldPatch,

            /// <summary>
            /// The restamping itself, inside <see cref="FieldPatch"/>, so that
            /// what is left as FieldPatch's own time is the walk over the army
            /// that finds who moved.
            /// </summary>
            /// <remarks>
            /// Split because the two have completely different answers. If the
            /// walk is most of it, a moved-set on the battle removes it; if the
            /// marking is most of it, a moved-set buys nothing and the lever is
            /// the cost of marking one body.
            /// </remarks>
            FieldRestamp,

            /// <summary>The A* loop over cells, without the string-pull after it.</summary>
            GridExpand,

            /// <summary>Turning settled cells back into a line, and pulling it straight.</summary>
            GridPull,

            /// <summary>Threading a regiment through a gap side-on, M27.</summary>
            ThreadGap,

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

            /// <summary>
            /// Clearing the bodies a regiment is standing in before it sets
            /// off, and the direct run out of them.
            /// </summary>
            /// <remarks>
            /// Added by [M123]. It runs before the cascade's first gate and was
            /// the only stage in the whole order that no clock was on, which is
            /// precisely what made a 381 ms plan in a played game unattributable
            /// (W7 - a recording has to answer on its own).
            /// </remarks>
            Staging,

            /// <summary>
            /// Walking a finished route leg by leg to see whether it is fit to
            /// hand to the executor.
            /// </summary>
            /// <remarks>
            /// The gate every stage's answer goes through. Deliberately never
            /// cut short by the budget - returning "clean" early would claim a
            /// verification nobody performed - so if it is dear it has to be
            /// made cheaper rather than gated, and that argument cannot be had
            /// until it has a number.
            /// </remarks>
            WalkCheck,

            /// <summary>Working out the front to hold on each leg of a route.</summary>
            Fronts,

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
            /// Bodies a bucket offered that the line itself refused - M118.
            /// </summary>
            NearSifted,

            /// <summary>
            /// Clearance checks where no body was near the leg at all, so the
            /// whole query and scan produced the answer "clear" from nothing -
            /// M119's ceiling.
            /// </summary>
            ClearLineNobodyNear,

            /// <summary>
            /// Clearance checks where a body was near enough to test but none
            /// blocked. Together with the above and the refusals, these are
            /// every clearance check there is.
            /// </summary>
            ClearLineSomebodyNear,

            /// <summary>Clearance checks that found a blocker.</summary>
            ClearLineBlocked,

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

            /// <summary>
            /// Bodies M27 walked the whole field to find, summed over calls.
            /// </summary>
            /// <remarks>
            /// These four say why threading a gap costs what it does, which the
            /// timer alone cannot: a quadratic over a handful is nothing and a
            /// quadratic over thirty is nine hundred, and the difference is
            /// invisible in a millisecond figure.
            /// </remarks>
            GapNear,

            /// <summary>Pairs of those bodies examined for a passage between them.</summary>
            GapPairs,

            /// <summary>
            /// Buckets a query looked at, against <c>NearBuckets</c>, which is
            /// the ones it kept.
            /// </summary>
            /// <remarks>
            /// The two together are the only way to see the shape of the waste:
            /// a query that keeps eleven buckets out of eleven is tight and one
            /// that keeps eleven out of forty is scanning a rectangle round a
            /// diagonal line.
            /// </remarks>
            NearBucketsSeen,

            /// <summary>Passages wide enough to keep.</summary>
            GapMouths,

            /// <summary>Passages actually put to the three clearance tests.</summary>
            GapMouthsTried,

            /// <summary>Passages that passed all three, and became a route.</summary>
            GapThreaded,

            /// <summary>Pairs with room between them, but far too much of it.</summary>
            GapTooWide,

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

        /// <summary>Whether this thread is measuring only the outer stages.</summary>
        /// <remarks>
        /// <para>
        /// <b>[M123].</b> The full profile puts two <see cref="Stopwatch"/>
        /// reads around every clearance check and every terrain lookup, which
        /// is millions a plan: fine on a bench that is measuring, ruinous in a
        /// played game that only wants to know which <i>stage</i> spent the
        /// time. Coarse mode times the cascade's stages and skips every shared
        /// leaf, so the cost is about a dozen scopes an order instead of
        /// millions.
        /// </para>
        /// <para>
        /// A skipped leaf is not lost, it is absorbed: its time lands in the
        /// self time of whichever stage contains it, which is exactly the
        /// column the question is asked in. Counts are still kept - a tally is
        /// one increment and the sweep count is worth having beside the
        /// milliseconds.
        /// </para>
        /// </remarks>
        [ThreadStatic] private static bool _coarse;

        /// <summary>The steps coarse mode still times.</summary>
        /// <remarks>
        /// Every stage the cascade can spend an order in, and nothing that is
        /// asked from more than one of them. The list is the answer to "which
        /// stage was it", so a step belongs here exactly when naming it would
        /// tell somebody where to look next.
        /// </remarks>
        private static readonly bool[] Coarsely = BuildCoarse();

        private static bool[] BuildCoarse()
        {
            var timed = new bool[(int)Step.Count];

            foreach (Step step in new[]
            {
                Step.Plan, Step.Staging, Step.Ladder, Step.WayRound, Step.Crab, Step.Rung1,
                Step.ThreadGap, Step.CandidatePlaces, Step.Hunt, Step.GrowPlaces,
                Step.TangentGraph, Step.PoseSearch, Step.HexSearch, Step.PathSmooth,
                Step.GridCoarse, Step.GridFine, Step.GridExpand, Step.GridPull,
                Step.HybridSearch, Step.SmoothRoute, Step.WalkCheck, Step.Fronts,
            })
                timed[(int)step] = true;

            return timed;
        }

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

            _coarse = false;
            _mine = true;
            _anyone = true;
        }

        /// <summary>
        /// Turns measuring on for the calling thread, over the cascade's stages
        /// only. See <see cref="_coarse"/>.
        /// </summary>
        public static void StartCoarse()
        {
            Start();
            _coarse = true;
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

        /// <summary>
        /// The innermost open step that is a stage rather than a shared leaf.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A clearance check is asked from a dozen places and charged, in the
        /// ordinary profile, to none of them: <c>ClearLine</c> is one row however
        /// it was reached. This walks out through the shared leaves to whichever
        /// stage actually asked, so a count can be attributed to the arch, the
        /// grid, the smoother or the gate that verifies a route.
        /// </para>
        /// <para>
        /// Measurement only. It reads a stack the profiler already keeps, and
        /// returns <see cref="Step.Plan"/> when nothing else is open.
        /// </para>
        /// </remarks>
        public static Step Charged()
        {
            if (_stack == null) return Step.Plan;

            for (int i = _depth - 1; i >= 0; i--)
            {
                var step = (Step)_stack[i];

                if (step == Step.ClearLine || step == Step.BodyScan ||
                    step == Step.NearQuery || step == Step.GroundClear ||
                    step == Step.PassableTable)
                    continue;

                return step;
            }

            return Step.Plan;
        }

        /// <summary>How many steps there are, so a caller can size a snapshot.</summary>
        public static int Steps => (int)Step.Count;

        /// <summary>Milliseconds, from a difference of two snapshots.</summary>
        public static double Milliseconds(long ticks) => ToMilliseconds(ticks);

        /// <summary>
        /// Copies this thread's running self time, in ticks, into the caller's
        /// array, so that two snapshots either side of a piece of work give what
        /// that work alone cost.
        /// </summary>
        /// <remarks>
        /// The profile is otherwise cumulative over a whole run, which answers
        /// "where did the time go" and cannot answer "which order was the worst
        /// and what was it doing" - a distribution question that needs the run
        /// cut into orders. Ticks rather than milliseconds because a difference
        /// of two conversions rounds twice.
        /// </remarks>
        public static void SelfTicks(long[] into)
        {
            if (into == null) throw new ArgumentNullException(nameof(into));

            if (_inclusive == null || _inChildren == null)
            {
                Array.Clear(into, 0, into.Length);
                return;
            }

            for (int i = 0; i < (int)Step.Count && i < into.Length; i++)
                into[i] = _inclusive[i] - _inChildren[i];
        }

        /// <summary>The same for call counts.</summary>
        public static void Calls(long[] into)
        {
            if (into == null) throw new ArgumentNullException(nameof(into));

            if (_calls == null)
            {
                Array.Clear(into, 0, into.Length);
                return;
            }

            for (int i = 0; i < (int)Step.Count && i < into.Length; i++)
                into[i] = _calls[i];
        }

        /// <summary>
        /// Where an order's time went, dearest first, as one line for a game
        /// log rather than a table for a bench.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>[M123], and it is the line that was missing.</b> A played session
        /// recorded six hundred and forty orders, the dearest at 381,7 ms, and
        /// every one of them reported <c>straight line, nothing searched</c> -
        /// which is <c>RouteEffort.Places &gt; 0</c>, a fact about the tangent
        /// graph and about nothing else. The recording could not name the stage
        /// that spent the time, so the next step had to be guessed at. W7 says
        /// a recording answers on its own.
        /// </para>
        /// <para>
        /// Self time, because that is the column that sums to the whole: a
        /// stage's own code with its children's time taken out. A stage nobody
        /// entered contributes nothing and is not printed, and the tail below
        /// <paramref name="atLeastShare"/> is summed into "rest" rather than
        /// listed, so the line stays readable when a plan touched everything.
        /// </para>
        /// </remarks>
        /// <param name="most">How many stages to name before the rest.</param>
        /// <param name="atLeastShare">
        /// The share of the whole a stage must reach to be worth naming.
        /// </param>
        public static string WhereItWent(int most = 4, double atLeastShare = 0.02)
        {
            if (_inclusive == null || _inChildren == null) return "not measured";

            int count = (int)Step.Count;
            double whole = 0d;

            for (int i = 0; i < count; i++)
                whole += ToMilliseconds(_inclusive[i] - _inChildren[i]);

            if (whole <= 0d) return "nothing measured";

            var order = new int[count];
            for (int i = 0; i < count; i++) order[i] = i;

            Array.Sort(order, (a, b) =>
                (_inclusive[b] - _inChildren[b]).CompareTo(_inclusive[a] - _inChildren[a]));

            var said = new StringBuilder();
            double rest = 0d;
            int named = 0;

            for (int i = 0; i < count; i++)
            {
                int step = order[i];
                double self = ToMilliseconds(_inclusive[step] - _inChildren[step]);

                if (self <= 0d) continue;

                if (named >= most || self < whole * atLeastShare)
                {
                    rest += self;
                    continue;
                }

                if (named > 0) said.Append(", ");

                said.Append((Step)step).Append(' ').Append(self.ToString("0.0"));

                // The call count only where it explains the milliseconds: one
                // dear visit and a thousand cheap ones are the same number of
                // milliseconds and completely different problems.
                long calls = _calls![step];
                if (calls > 1L) said.Append('x').Append(calls);

                named++;
            }

            if (rest > 0.05d)
                said.Append(named > 0 ? ", " : string.Empty).Append("rest ").Append(rest.ToString("0.0"));

            long sweeps = _calls![(int)Step.SweepTest];
            if (sweeps > 0L) said.Append(" | ").Append(sweeps).Append(" sweeps");

            return said.ToString();
        }

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
                if (!_anyone || !_mine || _depth >= MaxDepth ||
                    (_coarse && !Coarsely[(int)step]))
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
