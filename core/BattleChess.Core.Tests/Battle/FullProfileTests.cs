using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using BattleChess.Contracts;
using BattleChess.Rules;

using BattleChess.Rules.GridPlanning;
using BattleChess.Rules.HybridPlanning;
using Xunit;
using Xunit.Abstractions;

namespace BattleChess.Tests.Battle
{
    /// <summary>
    /// The whole cost, broken down: every planner on every field with all
    /// twenty-three timed steps, and then every lever that can change the bill
    /// swept against the clock.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why this exists beside the benches that already measure.</b>
    /// <c>BenchScenariosTests.WhatEachPlannerSpendsItOn</c> runs the same six
    /// planners over the same four fields, but prints six of the steps as a
    /// one-line summary — enough to see that the crowd costs and not enough to
    /// see <i>which</i> of the crowd's twenty-three steps it is.
    /// <c>LeverBenchTests</c> sweeps ten settings, but only the ones that were
    /// open questions when it was written, and only on the default planner.
    /// This asks both questions in full and in one place.
    /// </para>
    /// <para>
    /// <b>Read self time, not inclusive.</b> Steps nest, so inclusive counts the
    /// same microsecond at every level above it; self is inclusive net of
    /// children, and self is what sums to the whole. The <c>us/call</c> column
    /// is inclusive over calls, which is the honest per-visit figure for a leaf
    /// and a misleading one for a step that contains a search.
    /// </para>
    /// <para>
    /// <b>The instrumented pass is not the headline.</b> Every probe is two
    /// timestamp reads, and the innermost geometry runs millions of times an
    /// order — which is why it is counted rather than timed. The headline
    /// milliseconds come from uninstrumented passes, least of several; the table
    /// describes a separate instrumented pass and the overhead is printed beside
    /// it so the difference is visible rather than assumed.
    /// </para>
    /// <para>
    /// <b>Both are records of a measurement rather than checks on one</b>, and
    /// the lever sweep drives global planner settings while it runs, so both are
    /// skipped by default. Un-skip to re-take. Run them in <b>Release</b>: Debug
    /// changes the ranking, not just the totals.
    /// </para>
    /// </remarks>
    [Collection(PlannerLevers.Name)]
    public sealed class FullProfileTests
    {
        private readonly ITestOutputHelper _out;

        public FullProfileTests(ITestOutputHelper output) => _out = output;

        private static readonly string[] Fields =
            { "crucible", "longmarch", "brokencountry", "sidewaysmile" };

        // ------------------------------------------------ every step, every planner

        [Fact]
        public void EveryPlannerOnEveryFieldStepByStep()
        {
            foreach (string field in Fields)
            {
                _out.WriteLine(new string('=', 78));
                _out.WriteLine(
                    $"{field} — {BenchScenariosTests.Authored(field)} regiments, all ordered at once");
                _out.WriteLine(new string('=', 78));

                foreach (IRoutePlanner planner in RoutePlanners.All)
                {
                    Report(field, planner, planner.Name);
                }
            }
        }

        // ------------------------------------------------- every lever, against the clock

        [Fact(Skip = "record")]
        public void EveryLeverAgainstTheClock()
        {
            Defaults saved = Defaults.Capture();

            try
            {
                // A whole discard round before the table, not just the per-row
                // warm inside Measure. The first row otherwise pays for JIT of
                // everything the sweep touches and reads about a fifth dear —
                // which matters more than usual here, because the first row is
                // "defaults" and every other row is read against it.
                foreach (string warm in Fields) Measure(warm, planner: null, passes: 1);

                _out.WriteLine(
                    $"{"lever",-34}{"field",-16}{"ms/order",10}{"worst ms",10}" +
                    $"{"routed",8}{"pressed",9}{"unwalk",8}{"route s",10}");
                _out.WriteLine(new string('-', 105));

                foreach ((string name, Action apply) in Levers())
                {
                    saved.Restore();
                    apply();

                    foreach (string field in Fields)
                    {
                        Row row = Measure(field, planner: null, passes: 3);

                        _out.WriteLine(
                            $"{name,-34}{field,-16}{row.MsPerOrder,10:0.000}{row.Worst,10:0.0}" +
                            $"{row.Routed,8}{row.Pressed,9}{row.Unwalkable,8}{row.Seconds,10:0}");
                    }

                    _out.WriteLine(string.Empty);
                }
            }
            finally
            {
                saved.Restore();
            }
        }


        /// <summary>
        /// What bounding the grid search to a corridor costs, and what it saves.
        /// </summary>
        /// <remarks>
        /// The gate is the right-hand columns, not the left. A corridor that
        /// makes an order cheaper by refusing to find its route has not made
        /// anything better - M-W10 - so what has to hold across the sweep is
        /// <c>routed</c>, <c>pressed</c>, <c>unwalk</c> and above all
        /// <c>route s</c>, the seconds of marching the plans actually buy. The
        /// clock is only worth reading where those have not moved.
        /// </remarks>
        [Fact(Skip = "A record of a measurement rather than a check on one - it is what sized " +
                     "the corridor the grid search is allowed to wander in, and what refused it.")]
        public void WhatBoundingTheGridSearchCosts()
        {
            float wasFraction = RegimentGrid.CorridorFraction;
            bool wasIncremental = RegimentGrid.MarkIncrementally;

            try
            {
                foreach (string warm in Fields) Measure(warm, planner: null, passes: 1);

                _out.WriteLine(
                    $"{"corridor",-22}{"field",-16}{"ms/order",10}{"worst ms",10}" +
                    $"{"routed",8}{"pressed",9}{"unwalk",8}{"route s",12}{"off-line",12}");
                _out.WriteLine(new string('-', 109));

                foreach (float fraction in new[] { 0f, 2f, 1f, 0.5f, 0.25f })
                {
                    RegimentGrid.CorridorFraction = fraction;

                    foreach (string field in Fields)
                    {
                        RegimentGrid.CellsOutsideCorridor = 0;

                        Row row = Measure(field, planner: null, passes: 3);

                        _out.WriteLine(
                            $"{(fraction <= 0f ? "unbounded" : $"x{fraction:0.00} of the march"),-22}" +
                            $"{field,-16}{row.MsPerOrder,10:0.000}{row.Worst,10:0.0}" +
                            $"{row.Routed,8}{row.Pressed,9}{row.Unwalkable,8}{row.Seconds,12:0.0}" +
                            $"{RegimentGrid.CellsOutsideCorridor,12:N0}");
                    }

                    _out.WriteLine(string.Empty);
                }
            }
            finally
            {
                RegimentGrid.CorridorFraction = wasFraction;
                RegimentGrid.MarkIncrementally = wasIncremental;
            }
        }

        /// <summary>
        /// What the outward scan changes, in routes and not only in casts.
        /// </summary>
        /// <remarks>
        /// <para>
        /// [M120] measured what furthest-first costs - 43 casts to make one
        /// shortcut on the crucible - and predicted an outward scan at a third
        /// of it. That is the cheap half of the question. The dear half is
        /// [W10]: a cheaper number is not a better route, and the two scans
        /// differ wherever clearance is not monotone along a route.
        /// </para>
        /// <para>
        /// So both arms plan the same eighty orders against the same
        /// arrangement, and the routes are compared point by point. The budget
        /// is off throughout, because a cap makes an answer depend on how fast
        /// the machine was and this is a comparison of answers.
        /// </para>
        /// </remarks>
        [Fact(Skip = "A record of a measurement rather than a check on one.")]
        public void WhatTheOutwardScanChanges()
        {
            float wasBudget = Marching.SearchBudgetMs;
            bool wasOutward = RouteSmoothing.ExtendOutwards;

            try
            {
                Marching.SearchBudgetMs = 0f;

                _out.WriteLine(
                    $"{"field",-15}{"orders",7}{"same",6}{"changed",9}{"dearer",8}" +
                    $"{"points",8}{"seconds",12}{"worst one",11}{"unwalk",8}" +
                    $"{"casts",9}{"of them",9}");
                _out.WriteLine(new string('-', 104));

                foreach (string field in new[]
                         { "crucible", "brokencountry", "greatfield", "longmarch", "sidewaysmile" })
                {
                    List<Route> back = RoutesUnder(field, outward: false, out long backCasts);
                    List<Route> ahead = RoutesUnder(field, outward: true, out long aheadCasts);

                    int same = 0, changed = 0, extraPoints = 0, dearer = 0;
                    double extraSeconds = 0d, worstShare = 0d, walked = 0d;
                    int backUnwalkable = 0, aheadUnwalkable = 0;

                    for (int i = 0; i < back.Count; i++)
                    {
                        if (!back[i].Walks) backUnwalkable++;
                        if (!ahead[i].Walks) aheadUnwalkable++;

                        walked += back[i].Seconds;

                        if (SameRoute(back[i].Points, ahead[i].Points)) { same++; continue; }

                        changed++;
                        extraPoints += ahead[i].Points.Length - back[i].Points.Length;

                        double delta = ahead[i].Seconds - back[i].Seconds;
                        extraSeconds += delta;

                        if (delta > 0d) dearer++;

                        if (back[i].Seconds > 0d)
                            worstShare = Math.Max(worstShare, delta / back[i].Seconds);
                    }

                    _out.WriteLine(
                        $"{field,-15}{back.Count,7}{same,6}{changed,9}" +
                        $"{$"{dearer}/{changed}",8}{extraPoints,8:+0;-0;0}" +
                        $"{$"{extraSeconds / Math.Max(1d, walked):+0.0%;-0.0%;0.0%}",12}" +
                        $"{worstShare,11:+0.0%;-0.0%;0.0%}" +
                        $"{$"{backUnwalkable}/{aheadUnwalkable}",8}" +
                        $"{backCasts,9:N0}{(double)aheadCasts / Math.Max(1L, backCasts),9:0.0%}");
                }

                // And what it is worth on the clock, which is the half [M120]
                // already predicted and this only has to confirm.
                _out.WriteLine(string.Empty);
                _out.WriteLine($"{"field",-15}{"scan",-12}{"ms/order",10}{"worst ms",10}" +
                               $"{"routed",8}{"pressed",9}{"unwalk",8}{"seconds",10}");
                _out.WriteLine(new string('-', 82));

                // Warmed under both scans first. The first row of a table is
                // otherwise charged with compiling what the rest of it reuses,
                // and it reads as a difference between the arms: one unwarmed
                // pass here reported the furthest-first crucible at 1,800
                // ms/order against 0,441, a fourfold gap that was not there.
                foreach (bool warm in new[] { false, true, false, true })
                {
                    RouteSmoothing.ExtendOutwards = warm;
                    foreach (string field in new[] { "crucible", "brokencountry", "greatfield" })
                        Measure(field, planner: null, passes: 2);
                }

                foreach (string field in new[] { "crucible", "brokencountry", "greatfield" })
                {
                    foreach (bool outward in new[] { false, true })
                    {
                        RouteSmoothing.ExtendOutwards = outward;

                        double perOrder = double.MaxValue, worst = 0d;
                        Row last = null!;

                        for (int repeat = 0; repeat < 3; repeat++)
                        {
                            last = Measure(field, planner: null, passes: 2);
                            perOrder = Math.Min(perOrder, last.MsPerOrder);
                            worst = Math.Max(worst, last.Worst);
                        }

                        _out.WriteLine(
                            $"{field,-15}{(outward ? "outward" : "furthest first"),-12}" +
                            $"{perOrder,10:0.000}{worst,10:0.0}" +
                            $"{last.Routed,8}{last.Pressed,9}{last.Unwalkable,8}{last.Seconds,10:0}");
                    }

                    _out.WriteLine(string.Empty);
                }
            }
            finally
            {
                Marching.SearchBudgetMs = wasBudget;
                RouteSmoothing.ExtendOutwards = wasOutward;
            }
        }

        private readonly struct Route
        {
            public Route(Vec2[] points, double seconds, bool walks)
            {
                Points = points;
                Seconds = seconds;
                Walks = walks;
            }

            public Vec2[] Points { get; }
            public double Seconds { get; }
            public bool Walks { get; }
        }

        /// <summary>Every route on a field, planned under one scan.</summary>
        private static List<Route> RoutesUnder(string field, bool outward, out long casts)
        {
            RouteSmoothing.ExtendOutwards = outward;

            BattleState battle = BenchScenariosTests.Load(field);
            IPathfinder pathfinder = new DirectPathfinder(
                battle.Terrain, new TerrainMovementModel(TestContent.Terrain), TestContent.Terrain);

            // Warm, then count - the first order through pays for compiling the
            // planner and would swamp a cast count as it swamps a clock.
            foreach (UnitInstance warm in battle.UnitsOnField())
                Marching.PlanTo(battle, warm, pathfinder, BenchScenariosTests.OrderFor(battle, warm));

            RouteSmoothing.CastsTried = 0L;

            var routes = new List<Route>();

            foreach (UnitInstance unit in battle.UnitsOnField())
            {
                Plan plan = Marching.PlanTo(
                    battle, unit, pathfinder, BenchScenariosTests.OrderFor(battle, unit));

                routes.Add(plan.Path.Found
                    ? new Route(
                        plan.Path.Waypoints.ToArray(),
                        Marching.SecondsToWalk(battle, unit, plan.Path.Waypoints, plan.Hold),
                        StagedRoutePlanner.WalksCleanly(battle, unit, plan))
                    : new Route(Array.Empty<Vec2>(), 0d, walks: true));
            }

            casts = RouteSmoothing.CastsTried;

            return routes;
        }

        /// <summary>
        /// Whether two routes are the same one, to within the tolerance a
        /// waypoint is worth.
        /// </summary>
        /// <remarks>
        /// A centimetre. The two scans either return the same waypoint or a
        /// different one from the same list, so nothing here turns on where the
        /// threshold sits - it is a guard against float arithmetic, not a
        /// judgement about how close is close enough.
        /// </remarks>
        private static bool SameRoute(Vec2[] one, Vec2[] other)
        {
            if (one.Length != other.Length) return false;

            for (int i = 0; i < one.Length; i++)
                if (Vec2.Distance(one[i], other[i]) > 0.01f) return false;

            return true;
        }

        /// <summary>
        /// Whether a cap on the whole order is honoured once the searches poll
        /// it from within, and what that costs in routes.
        /// </summary>
        /// <remarks>
        /// [M114] made the cap bind on the cascade and bounded an order at about
        /// twice it, because a gate can only stop a stage <i>starting</i>. This
        /// asks whether polling inside the two places that overrun - the corner
        /// walk and the straightening pass - closes that factor of two, and what
        /// it costs the routes to close it.
        /// </remarks>
        [Fact(Skip = "A record of a measurement rather than a check on one.")]
        public void WhetherTheCapBindsAtFive()
        {
            float wasBudget = Marching.SearchBudgetMs;
            bool wasInner = Marching.StopSearchingWhenOutOfTime;

            string[] fields = { "crucible", "brokencountry" };

            try
            {
                foreach (string warm in fields) Measure(warm, planner: null, passes: 1);

                _out.WriteLine(
                    $"{"cap",-8}{"inner",-8}{"field",-15}{"worst ms",9}{"ms/order",9}{"over",6}" +
                    $"{"routed",8}{"pressed",8}{"unwalk",7}{"seconds",9}" +
                    $"{"corners",9}{"smoothing",11}");
                _out.WriteLine(new string('-', 107));

                foreach (float cap in new[] { 0f, 10f, 5f, 2f, 1f, 0.5f })
                {
                    Marching.SearchBudgetMs = cap;

                    foreach (bool inner in new[] { false, true })
                    {
                        Marching.StopSearchingWhenOutOfTime = inner;

                        foreach (string field in fields)
                        {
                            // Worst of the repeats, least of the means - [M113].
                            // A claim that a cap *bounds* an order is a claim
                            // about the tail, so it is tested against the worst
                            // seen and never the kindest.
                            double worst = 0d, perOrder = double.MaxValue;
                            int over = 0;
                            long corners = 0L, gaveUp = 0L;
                            Row last = null!;

                            for (int repeat = 0; repeat < 3; repeat++)
                            {
                                Marching.OrdersOverBudget = 0;
                                WaysRound.GaveUpWalkingCorners = 0;
                                RouteSmoothing.GaveUpSmoothing = 0L;

                                last = Measure(field, planner: null, passes: 2);

                                worst = Math.Max(worst, last.Worst);
                                perOrder = Math.Min(perOrder, last.MsPerOrder);
                                over = Math.Max(over, Marching.OrdersOverBudget);
                                corners = Math.Max(corners, WaysRound.GaveUpWalkingCorners);
                                gaveUp = Math.Max(gaveUp, RouteSmoothing.GaveUpSmoothing);
                            }

                            _out.WriteLine(
                                $"{(cap <= 0f ? "off" : $"{cap:0.##} ms"),-8}" +
                                $"{(inner ? "polled" : "gates"),-8}{field,-15}" +
                                $"{worst,9:0.0}{perOrder,9:0.000}{over,6}" +
                                $"{last.Routed,8}{last.Pressed,8}{last.Unwalkable,7}" +
                                $"{last.Seconds,9:0}{corners,9:N0}{gaveUp,11:N0}");
                        }
                    }

                    _out.WriteLine(string.Empty);
                }
            }
            finally
            {
                Marching.SearchBudgetMs = wasBudget;
                Marching.StopSearchingWhenOutOfTime = wasInner;
            }
        }

        /// <summary>
        /// Which stage asks the clearance checks, and which stage's are refused.
        /// </summary>
        /// <remarks>
        /// [M119a] found three quarters of every clearance check finds a
        /// blocker, and could not say whose. That is the difference between
        /// <i>the arch invents bad arcs</i> and <i>the grid proposes routes that
        /// will not walk</i>, and the two want opposite fixes - so the checks are
        /// charged to the innermost open stage rather than to the check.
        /// </remarks>
        [Fact(Skip = "A record of a measurement rather than a check on one.")]
        public void WhichStageAsksTheChecksThatFail()
        {
            float wasBudget = Marching.SearchBudgetMs;

            try
            {
                Marching.SearchBudgetMs = 0f;

                foreach (string field in new[] { "crucible", "brokencountry", "greatfield" })
                {
                    BenchScenariosTests.OrderEverybody(BenchScenariosTests.Load(field), null);

                    Marching.ChecksBy = null;
                    Marching.BlockedBy = null;
                    RouteSmoothing.CastsTried = 0L;
                    RouteSmoothing.CastsTaken = 0L;
                    RouteSmoothing.Reached = 0L;
                    RouteSmoothing.NothingClear = 0L;

                    BattleState probed = BenchScenariosTests.Load(field);

                    PlanningProfile.Start();
                    BenchScenariosTests.OrderEverybody(probed, null);
                    PlanningProfile.Stop();

                    long[] checks = Marching.ChecksBy ?? Array.Empty<long>();

                    _out.WriteLine(string.Empty);
                    _out.WriteLine(
                        $"    smoothing: {RouteSmoothing.CastsTried:N0} casts tried, " +
                        $"{RouteSmoothing.CastsTaken:N0} taken, " +
                        $"{RouteSmoothing.NothingClear:N0} stretches with nothing clear");
                    _out.WriteLine(
                        $"    an outward scan reaching the same points would cost " +
                        $"{RouteSmoothing.Reached + RouteSmoothing.CastsTaken:N0} casts " +
                        $"({(double)(RouteSmoothing.Reached + RouteSmoothing.CastsTaken) / Math.Max(1L, RouteSmoothing.CastsTried):0.0%} of them)");
                    long[] blocked = Marching.BlockedBy ?? Array.Empty<long>();

                    long allChecks = 0L, allBlocked = 0L;

                    foreach (long n in checks) allChecks += n;
                    foreach (long n in blocked) allBlocked += n;

                    _out.WriteLine(string.Empty);
                    _out.WriteLine(
                        $"=== {field}: {allChecks:N0} checks, {allBlocked:N0} refused " +
                        $"({(allChecks > 0L ? (double)allBlocked / allChecks : 0d):0.0%}) ===");
                    _out.WriteLine(
                        $"{"asked by",-18}{"checks",10}{"share",8}{"refused",10}" +
                        $"{"refusal rate",14}{"of all refusals",17}");
                    _out.WriteLine(new string('-', 77));

                    var order = new List<int>();
                    for (int i = 0; i < checks.Length; i++)
                        if (checks[i] > 0L) order.Add(i);

                    order.Sort((a, b) => checks[b].CompareTo(checks[a]));

                    foreach (int i in order)
                        _out.WriteLine(
                            $"{(PlanningProfile.Step)i,-18}{checks[i],10:N0}" +
                            $"{(double)checks[i] / allChecks,8:0.0%}{blocked[i],10:N0}" +
                            $"{(double)blocked[i] / checks[i],14:0.0%}" +
                            $"{(allBlocked > 0L ? (double)blocked[i] / allBlocked : 0d),17:0.0%}");
                }
            }
            finally
            {
                Marching.SearchBudgetMs = wasBudget;
            }
        }

        /// <summary>
        /// The most a field-answered clearance check could ever be worth.
        /// </summary>
        /// <remarks>
        /// <para>
        /// [M119]. Before building a fifth thing in the family that [M95],
        /// [M111], [M116] and [M118] all lost, measure what it could win at
        /// best.
        /// </para>
        /// <para>
        /// A clearance check where <b>no body was ever near the leg</b> is one a
        /// field lookup could answer without a query or a scan, because an
        /// unmarked cell means no body overlaps a mover standing on it. So the
        /// ceiling is that share of checks times what the query and the scan
        /// cost, and the cell walk that would replace them has to come out of
        /// it.
        /// </para>
        /// <para>
        /// <b>The share of checks is not the share of time.</b> A check with
        /// nobody near still pays a full <c>NearQuery</c> and a walk over
        /// whatever the buckets offered, so it is not a cheap check - but it is
        /// not the dear one either, since it does no sweep. Both columns are
        /// reported and the honest ceiling is between them.
        /// </para>
        /// </remarks>
        [Fact(Skip = "A record of a measurement rather than a check on one.")]
        public void WhatAFieldAnsweredClearanceCheckCouldBeWorth()
        {
            float wasBudget = Marching.SearchBudgetMs;

            try
            {
                Marching.SearchBudgetMs = 0f;

                _out.WriteLine(
                    $"{"field",-16}{"checks",10}{"nobody near",13}{"share",8}" +
                    $"{"somebody",10}{"blocked",9}{"query+scan",12}{"ceiling",9}");
                _out.WriteLine(new string('-', 87));

                foreach (string field in AllFields)
                {
                    BenchScenariosTests.OrderEverybody(BenchScenariosTests.Load(field), null);

                    BattleState probed = BenchScenariosTests.Load(field);

                    PlanningProfile.Start();
                    BenchScenariosTests.OrderEverybody(probed, null);
                    PlanningProfile.Stop();

                    long none = PlanningProfile.CallsTo(PlanningProfile.Step.ClearLineNobodyNear);
                    long some = PlanningProfile.CallsTo(PlanningProfile.Step.ClearLineSomebodyNear);
                    long hit = PlanningProfile.CallsTo(PlanningProfile.Step.ClearLineBlocked);

                    long checks = none + some + hit;

                    // What the two steps a field lookup would replace cost, as a
                    // share of everything timed.
                    double whole = 0d;
                    for (int i = 0; i < PlanningProfile.Steps; i++)
                        whole += PlanningProfile.SelfMilliseconds((PlanningProfile.Step)i);

                    double scan =
                        PlanningProfile.SelfMilliseconds(PlanningProfile.Step.NearQuery) +
                        PlanningProfile.SelfMilliseconds(PlanningProfile.Step.BodyScan);

                    double share = checks > 0L ? (double)none / checks : 0d;

                    _out.WriteLine(
                        $"{field,-16}{checks,10:N0}{none,13:N0}{share,8:0.0%}" +
                        $"{some,10:N0}{hit,9:N0}" +
                        $"{(whole > 0d ? scan / whole : 0d),12:0.0%}" +
                        $"{(whole > 0d ? share * scan / whole : 0d),9:0.0%}");
                }

                _out.WriteLine(string.Empty);
                _out.WriteLine(
                    "    ceiling = share of checks with nobody near x what the query and");
                _out.WriteLine(
                    "    scan cost. The cell walk that would replace them comes out of it,");
                _out.WriteLine(
                    "    so the real figure is below this and never above it.");
            }
            finally
            {
                Marching.SearchBudgetMs = wasBudget;
            }
        }

        /// <summary>
        /// What testing a body against the line, rather than only its bucket,
        /// costs and saves.
        /// </summary>
        /// <remarks>
        /// <para>
        /// [M118]. The designer: <i>"but bodyscan doesnt have to be used against
        /// 300 bodies, only the ones in the radius right?"</i>. It never was
        /// against three hundred - a query hands back 12,8 - but only 1,48 of
        /// those earn a sweep test, so seven in eight are handed back and thrown
        /// away by the caller.
        /// </para>
        /// <para>
        /// <b>This is a question about where a rejection happens, not whether
        /// it happens.</b> The caller already refuses those bodies; the index
        /// can refuse them a step earlier for the price of a projection and a
        /// squared distance. Whether that is cheaper is exactly the kind of
        /// thing that cannot be reasoned out, because [M111] lost the same
        /// argument from the other side.
        /// </para>
        /// <para>
        /// Both directions are run in one process and alternated, because [W12]
        /// says two builds across two processes cannot be trusted on this
        /// machine at margins under about three per cent.
        /// </para>
        /// </remarks>
        [Fact(Skip = "A record of a measurement rather than a check on one.")]
        public void WhatSiftingAtTheIndexCosts()
        {
            bool was = UnitIndex.SiftAtTheIndex;
            float wasBudget = Marching.SearchBudgetMs;

            try
            {
                Marching.SearchBudgetMs = 0f;

                foreach (string warm in AllFields) Measure(warm, planner: null, passes: 1);

                _out.WriteLine(
                    $"{"field",-16}{"sift",-8}{"ms/order",10}{"worst ms",10}" +
                    $"{"yield/query",13}{"sifted",10}{"routed",8}{"pressed",9}{"route s",10}");
                _out.WriteLine(new string('-', 94));

                foreach (string field in AllFields)
                {
                    for (int pass = 0; pass < 2; pass++)
                    {
                        // Alternated within the field so a warming or thermal
                        // drift falls on both arms rather than on one.
                        foreach (bool sift in pass == 0
                            ? new[] { false, true }
                            : new[] { true, false })
                        {
                            if (pass == 1) continue;

                            UnitIndex.SiftAtTheIndex = sift;

                            double worst = 0d, perOrder = double.MaxValue;
                            Row last = null!;
                            long yielded = 0L, asked = 0L, sifted = 0L;

                            for (int repeat = 0; repeat < 3; repeat++)
                            {
                                last = Measure(field, planner: null, passes: 2);

                                worst = Math.Max(worst, last.Worst);
                                perOrder = Math.Min(perOrder, last.MsPerOrder);
                            }

                            // Counted on one profiled pass, since a counter is
                            // deterministic and does not need the least of three.
                            BattleState probed = BenchScenariosTests.Load(field);
                            PlanningProfile.Start();
                            BenchScenariosTests.OrderEverybody(probed, null);
                            PlanningProfile.Stop();

                            yielded = PlanningProfile.CallsTo(PlanningProfile.Step.NearYield);
                            asked = PlanningProfile.CallsTo(PlanningProfile.Step.NearQuery);
                            sifted = PlanningProfile.CallsTo(PlanningProfile.Step.NearSifted);

                            _out.WriteLine(
                                $"{field,-16}{(sift ? "on" : "off"),-8}" +
                                $"{perOrder,10:0.000}{worst,10:0.0}" +
                                $"{(double)yielded / Math.Max(1L, asked),13:0.00}" +
                                $"{sifted,10:N0}{last.Routed,8}{last.Pressed,9}" +
                                $"{last.Seconds,10:0}");
                        }
                    }
                }
            }
            finally
            {
                UnitIndex.SiftAtTheIndex = was;
                Marching.SearchBudgetMs = wasBudget;
            }
        }

        /// <summary>
        /// What bounding the grid search to a corridor round the straight line
        /// costs, retried generously and after the gates.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The designer: <i>"limiting search radius (every) to 4x straight line
        /// length on all sides"</i>. [M105] measured this and refused it, but it
        /// was tried only <b>tight</b> - a quarter and one times the span - and
        /// it lost for a reason that has since been fixed: a bounded search that
        /// fails does not end the order, it hands it to the stages below, and
        /// everything below the grid is dearer. That is the escalation [M114]'s
        /// gates now catch.
        /// </para>
        /// <para>
        /// So it is worth asking again, and worth asking wide. [M115] says
        /// <c>GridExpand</c> is 56% of the worst order against 36% of the
        /// average, which makes a bound on it the one lever aimed at the tail
        /// rather than at the mean.
        /// </para>
        /// <para>
        /// The corridor is a <b>half-width</b>, so a fraction of 1 already
        /// admits a square of side twice the march. Read the pressed and
        /// unwalkable columns as hard as the clock ones: a corridor that shaves
        /// the tail by refusing routes has not made anything faster, it has
        /// made the regiment walk through somebody (W10).
        /// </para>
        /// </remarks>
        [Fact(Skip = "A record of a measurement rather than a check on one.")]
        public void WhatBoundingTheGridToACorridorCostsNow()
        {
            float wasFraction = RegimentGrid.CorridorFraction;
            float wasBudget = Marching.SearchBudgetMs;

            string[] fields = { "crucible", "brokencountry", "greatfield" };

            try
            {
                Marching.SearchBudgetMs = 0f;

                foreach (string warm in fields) Measure(warm, planner: null, passes: 1);

                _out.WriteLine(
                    $"{"corridor",-12}{"field",-16}{"ms/order",10}{"worst ms",10}" +
                    $"{"routed",8}{"pressed",9}{"unwalk",8}{"outside",12}{"route s",10}");
                _out.WriteLine(new string('-', 95));

                foreach (float fraction in new[] { 0f, 4f, 2f, 1f, 0.5f })
                {
                    RegimentGrid.CorridorFraction = fraction;

                    foreach (string field in fields)
                    {
                        double worst = 0d, perOrder = double.MaxValue;
                        Row last = null!;

                        for (int repeat = 0; repeat < 3; repeat++)
                        {
                            RegimentGrid.CellsOutsideCorridor = 0;

                            last = Measure(field, planner: null, passes: 2);

                            worst = Math.Max(worst, last.Worst);
                            perOrder = Math.Min(perOrder, last.MsPerOrder);
                        }

                        _out.WriteLine(
                            $"{(fraction <= 0f ? "unbounded" : $"x{fraction:0.##}"),-12}{field,-16}" +
                            $"{perOrder,10:0.000}{worst,10:0.0}" +
                            $"{last.Routed,8}{last.Pressed,9}{last.Unwalkable,8}" +
                            $"{RegimentGrid.CellsOutsideCorridor,12:N0}{last.Seconds,10:0}");
                    }

                    _out.WriteLine(string.Empty);
                }
            }
            finally
            {
                RegimentGrid.CorridorFraction = wasFraction;
                Marching.SearchBudgetMs = wasBudget;
            }
        }

        /// <summary>
        /// Every step of the search in the order it runs, for the average order
        /// and for the dearest single one.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The designer: <i>"if you run from start to finish, just all the
        /// orders, can you show me in order every single step of search, both
        /// for average and for worst order, the cost in ms?"</i>.
        /// </para>
        /// <para>
        /// Two things the ordinary profile cannot do. It sorts by cost, which
        /// answers <i>what is dear</i> and hides <i>what runs when</i>; and it is
        /// cumulative over a whole pass, so there is no such thing in it as an
        /// order, let alone a worst one. So the steps are listed here in cascade
        /// order by hand, and the run is cut into orders with a snapshot either
        /// side of each.
        /// </para>
        /// <para>
        /// <b>The worst column is one real order, not a maximum per step.</b>
        /// Taking each step's worst separately would give a column that sums to
        /// far more than any order ever cost and describes no order that
        /// happened. This is the single dearest order by self time, broken down
        /// - so the column sums to its total, and the shape of it is the shape
        /// of the stutter a player would actually feel.
        /// </para>
        /// <para>
        /// The last group is not a stage. <c>ClearLine</c>, <c>BodyScan</c> and
        /// <c>NearQuery</c> are asked by every stage above and sit at no one
        /// point in the order, which is the whole reason [M114c] found them to be
        /// the largest thing in the profile without their being a step anybody
        /// had thought of as a stage.
        /// </para>
        /// </remarks>
        [Fact(Skip = "A record of a measurement rather than a check on one.")]
        public void EveryStepInTheOrderItRuns()
        {
            float was = Marching.SearchBudgetMs;

            try
            {
            // Uncapped and then capped at five, because the two answer different
            // questions. Uncapped says where an order's time goes; capped says
            // where the time goes that the cap failed to stop, which is the only
            // thing that can be shortened to make the cap bind (W9).
            //
            // Set out loud rather than inherited: the budget is a static shared
            // with an order-sensitive suite.
            foreach (float budget in new[] { 0f, 5f })
            {
            Marching.SearchBudgetMs = budget;

            foreach (string field in AllFields)
            {
                BattleState battle = BenchScenariosTests.Load(field);

                int steps = PlanningProfile.Steps;

                long[] before = new long[steps], after = new long[steps];
                long[] callsBefore = new long[steps], callsAfter = new long[steps];

                double[] total = new double[steps];
                long[] calls = new long[steps];

                double[] worst = new double[steps];
                long[] worstCalls = new long[steps];
                double worstTotal = 0d;
                string worstUnit = string.Empty;

                var pathfinder = new DirectPathfinder(
                    battle.Terrain, new TerrainMovementModel(TestContent.Terrain),
                    TestContent.Terrain);

                // Warm, so the first order is not charged with compiling the
                // planner it is the first ever to use.
                foreach (UnitInstance warm in battle.UnitsOnField())
                    Marching.PlanTo(
                        battle, warm, pathfinder, BenchScenariosTests.OrderFor(battle, warm));

                int orders = 0;

                PlanningProfile.Start();

                foreach (UnitInstance unit in battle.UnitsOnField())
                {
                    PlanningProfile.SelfTicks(before);
                    PlanningProfile.Calls(callsBefore);

                    Marching.PlanTo(
                        battle, unit, pathfinder, BenchScenariosTests.OrderFor(battle, unit));

                    PlanningProfile.SelfTicks(after);
                    PlanningProfile.Calls(callsAfter);

                    orders++;

                    double spent = 0d;

                    for (int i = 0; i < steps; i++)
                    {
                        double ms = PlanningProfile.Milliseconds(after[i] - before[i]);

                        total[i] += ms;
                        calls[i] += callsAfter[i] - callsBefore[i];
                        spent += ms;
                    }

                    if (spent <= worstTotal) continue;

                    worstTotal = spent;
                    worstUnit = unit.Def.DisplayName;

                    for (int i = 0; i < steps; i++)
                    {
                        worst[i] = PlanningProfile.Milliseconds(after[i] - before[i]);
                        worstCalls[i] = callsAfter[i] - callsBefore[i];
                    }
                }

                PlanningProfile.Stop();

                double average = 0d;
                foreach (double ms in total) average += ms;
                average /= Math.Max(1, orders);

                _out.WriteLine(string.Empty);
                _out.WriteLine(
                    $"=== {field}: {orders} orders, " +
                    $"{(budget <= 0f ? "uncapped" : $"capped at {budget:0} ms")} ===");
                _out.WriteLine(
                    $"    the average order {average,7:0.000} ms" +
                    $"          the worst {worstTotal,7:0.000} ms  ({worstUnit})");
                _out.WriteLine(string.Empty);
                _out.WriteLine(
                    $"{"step",-18}{"calls/order",12}{"avg ms",9}{"avg %",8}" +
                    $"{"worst ms",10}{"worst %",9}{"calls",8}");
                _out.WriteLine(new string('-', 74));

                foreach ((string heading, PlanningProfile.Step[] group) in InCascadeOrder)
                {
                    _out.WriteLine($"-- {heading}");

                    foreach (PlanningProfile.Step step in group)
                    {
                        int i = (int)step;

                        // A step nothing entered is left out rather than printed
                        // as a row of noughts: on any one field several stages
                        // are never reached, and a table of noughts hides the
                        // rows that matter.
                        if (calls[i] == 0L) continue;

                        double mean = total[i] / Math.Max(1, orders);

                        _out.WriteLine(
                            $"{step,-18}{(double)calls[i] / Math.Max(1, orders),12:0.0}" +
                            $"{mean,9:0.000}{(average > 0d ? mean / average : 0d),8:0.0%}" +
                            $"{worst[i],10:0.000}" +
                            $"{(worstTotal > 0d ? worst[i] / worstTotal : 0d),9:0.0%}" +
                            $"{worstCalls[i],8:N0}");
                    }
                }

                _out.WriteLine(new string('-', 74));
                _out.WriteLine(
                    $"{"total",-18}{string.Empty,12}{average,9:0.000}{1d,8:0.0%}" +
                    $"{worstTotal,10:0.000}{1d,9:0.0%}");

                long asked = calls[(int)PlanningProfile.Step.NearQuery];
                long askedWorst = worstCalls[(int)PlanningProfile.Step.NearQuery];

                _out.WriteLine(string.Empty);
                _out.WriteLine(
                    $"{"counted, not timed",-18}{"per order",12}{"per query",11}" +
                    $"{"worst order",13}{"per query",11}");

                foreach (PlanningProfile.Step step in Counted)
                {
                    int i = (int)step;
                    if (calls[i] == 0L) continue;

                    _out.WriteLine(
                        $"{step,-18}{(double)calls[i] / Math.Max(1, orders),12:N1}" +
                        $"{(double)calls[i] / Math.Max(1L, asked),11:0.00}" +
                        $"{worstCalls[i],13:N0}" +
                        $"{(double)worstCalls[i] / Math.Max(1L, askedWorst),11:0.00}");
                }
            }
            }
            }
            finally
            {
                Marching.SearchBudgetMs = was;
            }
        }

        /// <summary>
        /// The timed steps in the order the cascade runs them, which is not the
        /// order they are declared in and is nowhere else written down.
        /// </summary>
        private static readonly (string, PlanningProfile.Step[])[] InCascadeOrder =
        {
            ("the whole order", new[] { PlanningProfile.Step.Plan }),

            ("1. the ladder", new[]
            {
                PlanningProfile.Step.Ladder,
                PlanningProfile.Step.Rung1,
                PlanningProfile.Step.WayRound,
                PlanningProfile.Step.Crab,
                PlanningProfile.Step.ThreadGap,
            }),

            ("2. the coarse regiment grid", new[]
            {
                PlanningProfile.Step.GridCoarse,
                PlanningProfile.Step.GridField,
                PlanningProfile.Step.FieldStamp,
                PlanningProfile.Step.FieldMark,
                PlanningProfile.Step.FieldPatch,
                PlanningProfile.Step.FieldRestamp,
                PlanningProfile.Step.GridSearch,
                PlanningProfile.Step.GridExpand,
                PlanningProfile.Step.GridPull,
            }),

            ("3. the fine grids", new[]
            {
                PlanningProfile.Step.GridFine,
                PlanningProfile.Step.GridFieldFine,
                PlanningProfile.Step.GridSearchFine,
            }),

            ("4. the tangent graph", new[] { PlanningProfile.Step.TangentGraph }),

            ("5. the pose search", new[] { PlanningProfile.Step.PoseSearch }),

            ("6. straightening, on whatever answered", new[]
            {
                PlanningProfile.Step.SmoothRoute,
            }),

            ("asked from every stage above, at no one point in the order", new[]
            {
                PlanningProfile.Step.ClearLine,
                PlanningProfile.Step.BodyScan,
                PlanningProfile.Step.NearQuery,
                PlanningProfile.Step.GroundClear,
                PlanningProfile.Step.PassableTable,
            }),
        };

        /// <summary>
        /// What the clearance steps found to do, counted rather than timed.
        /// </summary>
        /// <remarks>
        /// <c>NearYield</c> against <c>NearQuery</c> is the question the
        /// designer asked - <i>"bodyscan doesnt have to be used against 300
        /// bodies, only the ones in the radius right?"</i> - and it can only be
        /// answered by dividing one by the other. <c>SweepTest</c> against
        /// <c>NearYield</c> then says how many of the bodies handed back were
        /// worth the expensive test, which is the difference between a query
        /// that is too wide and a scan that is too slow.
        /// </remarks>
        private static readonly PlanningProfile.Step[] Counted =
        {
            PlanningProfile.Step.NearBucketsSeen,
            PlanningProfile.Step.NearBuckets,
            PlanningProfile.Step.NearYield,
            PlanningProfile.Step.NearEmpty,
            PlanningProfile.Step.OverlapTest,
            PlanningProfile.Step.SweepTest,
            PlanningProfile.Step.ClearLineRepeat,
        };

        /// <summary>
        /// What the floor under the cap is actually made of.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The designer, after [M114a]: <i>"so the ladder itself is 2ms?"</i>.
        /// [M114a] measured a floor and called it the ladder without taking it
        /// apart, which is one step short of an answer. This profiles a pass with
        /// the budget set to a hundredth of a millisecond, so every gate fires at
        /// the first opportunity and what remains on the clock is, by
        /// construction, only the work no gate can prevent.
        /// </para>
        /// <para>
        /// Read the average and the worst as different questions. A floor that
        /// matters to a frame is the average; a floor that matters to a stutter
        /// is the worst, and [M113] says one sample of a worst is not a
        /// measurement, so the worst here is the worst of several passes.
        /// </para>
        /// </remarks>
        [Fact(Skip = "A record of a measurement rather than a check on one.")]
        public void WhatTheFloorIsMadeOf()
        {
            float was = Marching.SearchBudgetMs;

            try
            {
                foreach (string field in new[] { "crucible", "brokencountry" })
                {
                    Measure(field, planner: null, passes: 1);

                    Marching.SearchBudgetMs = 0.01f;

                    Row floor = Measure(field, planner: null, passes: 3);

                    Marching.SearchBudgetMs = was;
                    Row whole = Measure(field, planner: null, passes: 3);

                    _out.WriteLine(string.Empty);
                    _out.WriteLine($"=== {field} ===");
                    _out.WriteLine(
                        $"    uncapped {whole.MsPerOrder,7:0.000} ms an order, " +
                        $"worst {whole.Worst,6:0.0} ms");
                    _out.WriteLine(
                        $"    the floor{floor.MsPerOrder,7:0.000} ms an order, " +
                        $"worst {floor.Worst,6:0.0} ms   " +
                        $"({floor.MsPerOrder / whole.MsPerOrder:0%} of an ordinary order)");

                    // Uncapped first, so the two tables below are a whole order
                    // and then the part of it no gate can prevent.
                    Marching.SearchBudgetMs = was;

                    BattleState open = BenchScenariosTests.Load(field);
                    PlanningProfile.Start();
                    BenchScenariosTests.OrderEverybody(open, null);
                    PlanningProfile.Stop();

                    _out.WriteLine(string.Empty);
                    _out.WriteLine(PlanningProfile.Report(
                        $"    a whole order - {field}, no cap"));

                    Marching.SearchBudgetMs = 0.01f;

                    BattleState probed = BenchScenariosTests.Load(field);
                    PlanningProfile.Start();
                    BenchScenariosTests.OrderEverybody(probed, null);
                    PlanningProfile.Stop();

                    _out.WriteLine(string.Empty);
                    _out.WriteLine(PlanningProfile.Report(
                        $"    what the floor is made of - {field} at a 0,01 ms budget"));
                }
            }
            finally
            {
                Marching.SearchBudgetMs = was;
            }
        }

        /// <summary>
        /// Why a tighter cap makes the worst order dearer rather than cheaper.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The designer, reading [M112]: <i>"how is it that the cap at 10 ms has
        /// worst order 43 but cap at 20 ms has worst order at 32 - if the cap is
        /// 10 ms then worst is 10 ms isnt it?"</i>. It is the right question and
        /// the number is not a misprint.
        /// </para>
        /// <para>
        /// The cascade has exactly one door out on the clock, after the coarse
        /// grid, and it is barred unless the ladder already holds an answer:
        /// <c>if (Marching.StopNow() &amp;&amp; ladder.Path.Found)</c>. An order
        /// whose ladder found nothing walks straight past it into the fine tier,
        /// where the quarter-spacing grid is <b>sixteen times the field</b> and
        /// raising it is un-polled from the first cell to the last. So the cap
        /// does not bound the order; it decides <i>which stage the order is
        /// standing in when the clock runs out</i>.
        /// </para>
        /// <para>
        /// That gives the counter-intuitive direction its mechanism, and this
        /// table is here to confirm or refute it: if a tighter cap is demoting
        /// orders rather than ending them, <b>fine asked must rise as the cap
        /// falls</b>. If it does not rise, the mechanism is something else and
        /// this entry is wrong.
        /// </para>
        /// </remarks>
        [Fact(Skip = "A record of a measurement rather than a check on one - " +
                     "it orders two bench fields a hundred and eight times over " +
                     "at nine budgets.")]
        public void WhyATighterCapCostsMore()
        {
            float was = Marching.SearchBudgetMs;

            string[] fields = { "crucible", "brokencountry" };

            try
            {
                // Warmed under both arms and at a cap, because the first row of a
                // sweep is otherwise charged with compiling everything the rest
                // of it reuses - which lands on whichever arm happens to be
                // measured first and reads as a difference between the arms.
                Marching.SearchBudgetMs = 5f;

                foreach (bool arm in new[] { false, true, false, true })
                {
                    Marching.StopSearchingWhenOutOfTime = arm;

                    foreach (string warm in fields) Measure(warm, planner: null, passes: 2);
                }

                _out.WriteLine(
                    $"{"cap",-8}{"field",-15}{"worst ms",9}{"ms/order",9}{"over",6}" +
                    $"{"routed",8}{"pressed",8}{"unwalk",7}" +
                    $"{"coarse",7}{"fine",6}{"graphs",7}{"pose",6}{"nothing",8}" +
                    $"{"2nd cast",10}");
                _out.WriteLine(new string('-', 108));

                foreach (float cap in new[] { 0f, 20f, 10f, 5f, 2f, 1f, 0.5f, 0.1f, 0.01f })
                {
                    Marching.SearchBudgetMs = cap;

                    foreach (string field in fields)
                    {
                        // The worst of the repeats, not the least. [M113]: a
                        // maximum is one sample of a tail and swings by half its
                        // own value, so a claim that the cap *bounds* an order
                        // has to be tested against the worst seen and not the
                        // kindest.
                        double worst = 0d, perOrder = double.MaxValue;
                        int over = 0;
                        Row last = null!;

                        for (int repeat = 0; repeat < 3; repeat++)
                        {
                            Marching.OrdersOverBudget = 0;

                            last = Measure(field, planner: null, passes: 2);

                            worst = Math.Max(worst, last.Worst);
                            perOrder = Math.Min(perOrder, last.MsPerOrder);
                            over = Math.Max(over, Marching.OrdersOverBudget);
                        }

                        _out.WriteLine(
                            $"{(cap <= 0f ? "off" : $"{cap:0.##} ms"),-8}{field,-15}" +
                            $"{worst,9:0.0}{perOrder,9:0.000}{over,6}" +
                            $"{last.Routed,8}{last.Pressed,8}{last.Unwalkable,7}" +
                            $"{StagedRoutePlanner.StoppedBeforeCoarse,7}" +
                            $"{StagedRoutePlanner.StoppedBeforeFine,6}" +
                            $"{StagedRoutePlanner.StoppedBeforeGraphs,7}" +
                            $"{StagedRoutePlanner.StoppedBeforePose,6}" +
                            $"{StagedRoutePlanner.OutOfTimeWithNothing,8}" +
                            $"{StagedRoutePlanner.ArrivalAsked,10}");
                    }

                    _out.WriteLine(string.Empty);
                }
            }
            finally
            {
                Marching.SearchBudgetMs = was;
            }
        }

        /// <summary>
        /// What capping a whole order at a few milliseconds does to the routes.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The designer: <i>"experiment how many press throughs if we cap
        /// everything at 10ms ... also verify for 5ms"</i>. The budget is opened
        /// from the stamp the order is charged against, so it caps the cascade
        /// end to end - ladder, crab, grid, tangents, lattice - and not the
        /// searches alone.
        /// </para>
        /// <para>
        /// <b>Read the pressed column, and then read the spread beside it.</b> A
        /// wall-clock cap makes planning <i>non-deterministic</i>: the same
        /// order on the same arrangement can finish under the wire on one pass
        /// and be cut off on the next, so what a cap costs is a distribution and
        /// not a number. Three quality passes per row, and both ends reported.
        /// </para>
        /// <para>
        /// <b>And this machine is not the game.</b> The bench runs on CoreCLR
        /// and the editor on Mono, which is two to four times slower on this
        /// kind of arithmetic - so ten milliseconds here buys two to four times
        /// the work ten milliseconds buys in the editor. The 5 ms row is the
        /// better guide to what a 10 ms cap will feel like in play, and the 2 ms
        /// row is not idle curiosity either.
        /// </para>
        /// </remarks>
        [Fact(Skip = "A record of a measurement rather than a check on one - it plans every " +
                     "bench field forty-five times over and drives a global lever while it runs.")]
        public void WhatCappingAWholeOrderCosts()
        {
            float was = Marching.SearchBudgetMs;

            try
            {
                foreach (string warm in AllFields) Measure(warm, planner: null, passes: 1);

                _out.WriteLine(
                    $"{"cap",-8}{"field",-16}{"ms/order",10}{"worst ms",10}" +
                    $"{"routed",14}{"pressed",14}{"unwalkable",14}{"over budget",14}{"route s",12}");
                _out.WriteLine(new string('-', 112));

                foreach (float cap in new[] { 0f, 20f, 10f, 5f, 2f })
                {
                    Marching.SearchBudgetMs = cap;

                    foreach (string field in AllFields)
                    {
                        var rows = new List<Row>();
                        var overrun = new List<int>();

                        for (int pass = 0; pass < 3; pass++)
                        {
                            Marching.OrdersOverBudget = 0;
                            Marching.OrdersPlanned = 0;

                            rows.Add(Measure(field, planner: null, passes: 3));

                            overrun.Add(Marching.OrdersOverBudget);
                        }

                        _out.WriteLine(
                            $"{(cap <= 0f ? "off" : $"{cap:0} ms"),-8}{field,-16}" +
                            $"{Least(rows, r => r.MsPerOrder),10:0.000}" +
                            $"{Least(rows, r => r.Worst),10:0.0}" +
                            $"{Spread(rows, r => r.Routed),14}" +
                            $"{Spread(rows, r => r.Pressed),14}" +
                            $"{Spread(rows, r => r.Unwalkable),14}" +
                            $"{Both(overrun),14}" +
                            $"{Least(rows, r => r.Seconds),12:0}");
                    }

                    _out.WriteLine(string.Empty);
                }
            }
            finally
            {
                Marching.SearchBudgetMs = was;
            }
        }

        private static readonly string[] AllFields =
            { "crucible", "brokencountry", "longmarch", "greatfield", "sidewaysmile" };

        /// <summary>
        /// What an order costs by the size of the body being ordered, and which
        /// stage the dear ones spend it in.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>[M123], and it is a reproduction rather than a sweep.</b> A played
        /// session on The Great Field recorded 640 orders. Split by regiment:
        /// Spearmen (80x40 m) averaged 1,6 ms, Swordsmen (100x50) 2,6, and
        /// <b>Cavalry (229x114) fifty point seven</b> - twelve per cent of the
        /// orders and seventy-seven per cent of all the planning in the session,
        /// with a worst order of 381,7 ms against a 5 ms cap. The obvious
        /// confounder was ruled out in the recording: cavalry cost 50,7 ms when
        /// the click landed on taken ground and 50,6 ms when it did not, so the
        /// placement search is not it.
        /// </para>
        /// <para>
        /// <c>greatfield</c> is that same order of battle, so the question here
        /// is whether CoreCLR shows the same shape. If it does, the play problem
        /// is reproducible on a bench and can be profiled without the editor
        /// (W3); if it does not, the cause is Mono's and this says so before
        /// anybody optimises against the wrong machine.
        /// </para>
        /// </remarks>
        [Fact(Skip = "A record of a measurement rather than a check on one.")]
        public void WhatABiggerBodyCostsToOrder()
        {
            bool wasExplaining = Marching.ExplainSlowPlans;
            float wasBudget = Marching.SearchBudgetMs;

            Marching.ExplainSlowPlans = false;
            Marching.SearchBudgetMs = 0f;

            try
            {
                // Both fields warmed before either is read, and the whole sweep
                // taken three times with the least kept. The first row of an
                // unwarmed table has been a fourfold lie twice this session.
                foreach (string warm in new[] { "thecrowdedwing", "greatfield" })
                    ByFootprint(warm);

                foreach (string field in new[] { "thecrowdedwing", "greatfield", "crucible" })
                {
                    _out.WriteLine(string.Empty);
                    _out.WriteLine(field);
                    _out.WriteLine(
                        $"{"regiment",-16}{"footprint",12}{"orders",8}{"mean ms",10}" +
                        $"{"worst",9}{"share",8}{"sweeps/order",14}");
                    _out.WriteLine(new string('-', 77));

                    List<Sized> least = ByFootprint(field);

                    for (int again = 0; again < 2; again++)
                    {
                        List<Sized> next = ByFootprint(field);

                        for (int i = 0; i < least.Count; i++)
                            if (next[i].Milliseconds < least[i].Milliseconds)
                                least[i] = next[i];
                    }

                    var byKind = new Dictionary<string, List<Sized>>();

                    foreach (Sized one in least)
                    {
                        if (!byKind.TryGetValue(one.Kind, out List<Sized>? some) || some == null)
                            byKind[one.Kind] = some = new List<Sized>();

                        some.Add(one);
                    }

                    double whole = 0d;
                    foreach (Sized one in least) whole += one.Milliseconds;

                    foreach (KeyValuePair<string, List<Sized>> kind in
                             byKind.OrderBy(k => k.Value[0].Width))
                    {
                        double spent = 0d, worst = 0d;
                        long sweeps = 0L;

                        foreach (Sized one in kind.Value)
                        {
                            spent += one.Milliseconds;
                            worst = Math.Max(worst, one.Milliseconds);
                            sweeps += one.Sweeps;
                        }

                        _out.WriteLine(
                            $"{kind.Key,-16}" +
                            $"{$"{kind.Value[0].Width:0}x{kind.Value[0].Depth:0} m",12}" +
                            $"{kind.Value.Count,8}{spent / kind.Value.Count,10:0.00}" +
                            $"{worst,9:0.0}{spent / Math.Max(1e-9d, whole),8:0.0%}" +
                            $"{sweeps / (double)kind.Value.Count,14:N0}");
                    }
                }
            }
            finally
            {
                Marching.SearchBudgetMs = wasBudget;
                Marching.ExplainSlowPlans = wasExplaining;
            }
        }

        /// <summary>
        /// Which stage the dearest orders on a field actually spend their time
        /// in, read the way a played recording now reads it.
        /// </summary>
        /// <remarks>
        /// The same coarse profile [M123] put behind
        /// <see cref="Marching.ExplainSlowPlans"/>, so this measures the line a
        /// player's log will print rather than a separate one that might not
        /// agree with it (W5).
        /// </remarks>
        [Fact(Skip = "A record of a measurement rather than a check on one.")]
        public void WhereTheDearestOrdersSpendIt()
        {
            foreach (string field in new[] { "thecrowdedwing", "greatfield", "crucible" })
            {
                BattleState battle = BenchScenariosTests.Load(field);
                IPathfinder pathfinder = new DirectPathfinder(
                    battle.Terrain, new TerrainMovementModel(TestContent.Terrain), TestContent.Terrain);

                foreach (UnitInstance warm in battle.UnitsOnField())
                    Marching.PlanTo(battle, warm, pathfinder, BenchScenariosTests.OrderFor(battle, warm));

                var said = new List<(double Ms, string Who, string Where)>();

                foreach (UnitInstance unit in battle.UnitsOnField())
                {
                    Vec2 to = BenchScenariosTests.OrderFor(battle, unit);

                    PlanningProfile.StartCoarse();

                    long began = Stopwatch.GetTimestamp();
                    Marching.PlanTo(battle, unit, pathfinder, to);
                    double ms = PlanningProfile.Milliseconds(Stopwatch.GetTimestamp() - began);

                    said.Add((ms, unit.Def.DisplayName, PlanningProfile.WhereItWent(5, 0.01d)));
                    PlanningProfile.Stop();
                }

                said.Sort((a, b) => b.Ms.CompareTo(a.Ms));

                _out.WriteLine(string.Empty);
                _out.WriteLine(field);

                for (int i = 0; i < 5 && i < said.Count; i++)
                    _out.WriteLine($"  {said[i].Ms,7:0.0} ms  {said[i].Who,-14} {said[i].Where}");
            }
        }

        /// <summary>
        /// What refusing a leg by distance rather than by sweep saves, and
        /// whether the two ever disagree.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>[M124], the other half of [M120].</b> The arch refuses 81,5-85,5%
        /// of the legs it asks about and the straightening pass 95,8-97,7%, and
        /// each refusal pays five separating-axis tests over a hexagon and a
        /// rectangle. <see cref="Marching.ProveBlockedCheaply"/> tries a
        /// capsule-to-capsule distance first, which can prove a collision but
        /// never rule one out.
        /// </para>
        /// <para>
        /// Two questions, and the second is the one that matters: what it saves,
        /// and whether it is sound. The soundness check runs the sweep anyway
        /// behind every cheap refusal and counts the disagreements, which must
        /// be nought - and the routes are compared point by point, which must be
        /// identical, because a test that only ever agrees with the sweep cannot
        /// change an answer.
        /// </para>
        /// </remarks>
        [Fact(Skip = "A record of a measurement rather than a check on one.")]
        public void WhatRefusingByDistanceSaves()
        {
            bool wasProving = Marching.ProveBlockedCheaply;
            bool wasChecking = Marching.CheckTheCheapProof;
            float wasBudget = Marching.SearchBudgetMs;

            Marching.SearchBudgetMs = 0f;

            try
            {
                // Soundness first, and on every field: with the sweep run behind
                // every cheap refusal, how often the two disagree.
                Marching.ProveBlockedCheaply = true;
                Marching.CheckTheCheapProof = true;

                _out.WriteLine(
                    $"{"field",-16}{"orders",8}{"proved",10}{"disagreed",12}{"sweeps",12}");
                _out.WriteLine(new string('-', 58));

                foreach (string field in AllProvingFields)
                {
                    Marching.ProvedBlocked = 0L;
                    Marching.ProofDisagreed = 0L;

                    PlanningProfile.Start();
                    PlanEveryOrder(field);
                    long sweeps = PlanningProfile.CallsTo(PlanningProfile.Step.SweepTest);
                    PlanningProfile.Stop();

                    _out.WriteLine(
                        $"{field,-16}{OrdersOn(field),8}{Marching.ProvedBlocked,10:N0}" +
                        $"{Marching.ProofDisagreed,12:N0}{sweeps,12:N0}");
                }

                Marching.CheckTheCheapProof = false;

                // Then the clock, both arms warmed before either is read.
                foreach (string warm in AllProvingFields)
                {
                    Marching.ProveBlockedCheaply = false;
                    PlanEveryOrder(warm);
                    Marching.ProveBlockedCheaply = true;
                    PlanEveryOrder(warm);
                }

                _out.WriteLine(string.Empty);
                _out.WriteLine(
                    $"{"field",-16}{"by sweep",12}{"by distance",14}{"change",10}" +
                    $"{"sweeps left",14}{"same routes",14}");
                _out.WriteLine(new string('-', 80));

                foreach (string field in AllProvingFields)
                {
                    double bySweep = double.MaxValue, byDistance = double.MaxValue;
                    long sweptSweeps = 0L, provedSweeps = 0L;

                    List<Vec2[]> swept = null!, proved = null!;

                    for (int pass = 0; pass < 3; pass++)
                    {
                        Marching.ProveBlockedCheaply = false;
                        bySweep = Math.Min(bySweep, Clocked(field, out swept, out sweptSweeps));

                        Marching.ProveBlockedCheaply = true;
                        byDistance = Math.Min(byDistance, Clocked(field, out proved, out provedSweeps));
                    }

                    int same = 0;
                    for (int i = 0; i < swept.Count; i++)
                        if (SameRoute(swept[i], proved[i])) same++;

                    _out.WriteLine(
                        $"{field,-16}{bySweep,12:0.000}{byDistance,14:0.000}" +
                        $"{byDistance / bySweep - 1d,10:+0.0%;-0.0%;0.0%}" +
                        $"{provedSweeps / (double)Math.Max(1L, sweptSweeps),14:0.0%}" +
                        $"{$"{same}/{swept.Count}",14}");
                }
            }
            finally
            {
                Marching.SearchBudgetMs = wasBudget;
                Marching.CheckTheCheapProof = wasChecking;
                Marching.ProveBlockedCheaply = wasProving;
            }
        }

        /// <summary>
        /// What walking the staging scan out once instead of once per stand-off
        /// saves, and which routes it changes.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>[M125].</b> [M123] found the staging scan to be 76 to 103 ms of a
        /// single order in play, on a stage that runs before the cascade's first
        /// gate. <see cref="StagedRoutePlanner.WalkTheStagingOnce"/> collapses
        /// its triangular sum into one walk.
        /// </para>
        /// <para>
        /// The routes are the question, not the clock, and the reason is
        /// stated plainly: the two scans sample different ground. Today's spaces
        /// a leg's samples by dividing <i>that leg</i> into two-metre pieces, so
        /// consecutive stand-offs test different points; one walk has one grid.
        /// So this reports what changed and what the change was worth to walk,
        /// exactly as [M121] did.
        /// </para>
        /// </remarks>
        [Fact(Skip = "A record of a measurement rather than a check on one.")]
        public void WhatWalkingTheStagingOnceChanges()
        {
            bool wasOnce = StagedRoutePlanner.WalkTheStagingOnce;
            float wasBudget = Marching.SearchBudgetMs;

            Marching.SearchBudgetMs = 0f;

            try
            {
                _out.WriteLine(
                    $"{"field",-16}{"orders",8}{"same",6}{"changed",9}{"dearer",8}" +
                    $"{"walking",10}{"worst one",11}{"unwalk",8}{"samples",11}{"of them",9}");
                _out.WriteLine(new string('-', 96));

                foreach (string field in AllProvingFields)
                {
                    // Both arms warmed on this field before either is read.
                    StagedRoutePlanner.WalkTheStagingOnce = false;
                    StagedWalk(field, out _);
                    StagedRoutePlanner.WalkTheStagingOnce = true;
                    StagedWalk(field, out _);

                    StagedRoutePlanner.WalkTheStagingOnce = false;
                    List<Route> back = StagedWalk(field, out long backSamples);

                    StagedRoutePlanner.WalkTheStagingOnce = true;
                    List<Route> ahead = StagedWalk(field, out long aheadSamples);

                    int same = 0, changed = 0, dearer = 0;
                    int backUnwalkable = 0, aheadUnwalkable = 0;
                    double extra = 0d, walked = 0d, worst = 0d;

                    for (int i = 0; i < back.Count; i++)
                    {
                        if (!back[i].Walks) backUnwalkable++;
                        if (!ahead[i].Walks) aheadUnwalkable++;

                        walked += back[i].Seconds;

                        if (SameRoute(back[i].Points, ahead[i].Points)) { same++; continue; }

                        changed++;

                        double delta = ahead[i].Seconds - back[i].Seconds;
                        extra += delta;

                        if (delta > 0d) dearer++;

                        if (back[i].Seconds > 0d)
                            worst = Math.Max(worst, delta / back[i].Seconds);
                    }

                    _out.WriteLine(
                        $"{field,-16}{back.Count,8}{same,6}{changed,9}{$"{dearer}/{changed}",8}" +
                        $"{$"{extra / Math.Max(1d, walked):+0.0%;-0.0%;0.0%}",10}" +
                        $"{worst,11:+0.0%;-0.0%;0.0%}{$"{backUnwalkable}/{aheadUnwalkable}",8}" +
                        $"{backSamples,11:N0}" +
                        $"{aheadSamples / (double)Math.Max(1L, backSamples),9:0.0%}");
                }

                // And the clock, both arms warmed, least of three.
                _out.WriteLine(string.Empty);
                _out.WriteLine($"{"field",-16}{"per stand-off",15}{"once",10}{"change",10}");
                _out.WriteLine(new string('-', 51));

                foreach (string field in AllProvingFields)
                {
                    double each = double.MaxValue, once = double.MaxValue;

                    for (int pass = 0; pass < 3; pass++)
                    {
                        StagedRoutePlanner.WalkTheStagingOnce = false;
                        each = Math.Min(each, Clocked(field, out _, out _));

                        StagedRoutePlanner.WalkTheStagingOnce = true;
                        once = Math.Min(once, Clocked(field, out _, out _));
                    }

                    _out.WriteLine(
                        $"{field,-16}{each,15:0.000}{once,10:0.000}{once / each - 1d,10:+0.0%;-0.0%;0.0%}");
                }
            }
            finally
            {
                Marching.SearchBudgetMs = wasBudget;
                StagedRoutePlanner.WalkTheStagingOnce = wasOnce;
            }
        }

        /// <summary>Every order on a field, with the staging samples counted.</summary>
        private static List<Route> StagedWalk(string field, out long samples)
        {
            BattleState battle = BenchScenariosTests.Load(field);
            IPathfinder pathfinder = new DirectPathfinder(
                battle.Terrain, new TerrainMovementModel(TestContent.Terrain), TestContent.Terrain);

            foreach (UnitInstance warm in battle.UnitsOnField())
                Marching.PlanTo(battle, warm, pathfinder, BenchScenariosTests.OrderFor(battle, warm));

            StagedRoutePlanner.StagingSamples = 0L;

            var routes = new List<Route>();

            foreach (UnitInstance unit in battle.UnitsOnField())
            {
                Plan plan = Marching.PlanTo(
                    battle, unit, pathfinder, BenchScenariosTests.OrderFor(battle, unit));

                routes.Add(plan.Path.Found
                    ? new Route(
                        plan.Path.Waypoints.ToArray(),
                        Marching.SecondsToWalk(battle, unit, plan.Path.Waypoints, plan.Hold),
                        StagedRoutePlanner.WalksCleanly(battle, unit, plan))
                    : new Route(Array.Empty<Vec2>(), 0d, walks: true));
            }

            samples = StagedRoutePlanner.StagingSamples;

            return routes;
        }

        /// <summary>
        /// What a press-through really costs once it is priced at the pace it is
        /// walked, and what pricing it changes.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>[M127], and it is the number the ceiling has to be set from.</b>
        /// The ratio reported is the same route priced twice - with
        /// <see cref="Marching.PricePressingHonestly"/> on and off - so it is
        /// exactly the slowdown the executor will charge and nothing else. It
        /// cannot exceed 1/0,6 = 1,67, which is a march pressed end to end.
        /// </para>
        /// <para>
        /// The routes are the other half: pricing changes every comparison in
        /// the cascade, so what the planner *chooses* moves as well as what it
        /// says a choice costs.
        /// </para>
        /// </remarks>
        [Fact(Skip = "A record of a measurement rather than a check on one.")]
        public void WhatAPressReallyCosts()
        {
            bool wasPricing = Marching.PricePressingHonestly;
            float wasBudget = Marching.SearchBudgetMs;

            Marching.SearchBudgetMs = 0f;

            try
            {
                _out.WriteLine(
                    $"{"field",-16}{"orders",8}{"same",6}{"changed",9}" +
                    $"{"pressed",10}{"worst",8}{"1,0-1,1",9}{"-1,25",7}{"-1,4",7}{"1,4+",7}");
                _out.WriteLine(new string('-', 87));

                foreach (string field in AllProvingFields)
                {
                    Marching.PricePressingHonestly = false;
                    Pressed(field, out _, out _);
                    Marching.PricePressingHonestly = true;
                    Pressed(field, out _, out _);

                    Marching.PricePressingHonestly = false;
                    List<Vec2[]> before = Pressed(field, out _, out _);

                    Marching.PricePressingHonestly = true;
                    List<Vec2[]> after = Pressed(field, out List<double> ratios, out int presses);

                    int same = 0;
                    for (int i = 0; i < before.Count; i++)
                        if (SameRoute(before[i], after[i])) same++;

                    double worst = 0d;
                    int[] band = new int[4];

                    foreach (double r in ratios)
                    {
                        worst = Math.Max(worst, r);

                        if (r <= 1.001d) continue;
                        if (r < 1.1d) band[0]++;
                        else if (r < 1.25d) band[1]++;
                        else if (r < 1.4d) band[2]++;
                        else band[3]++;
                    }

                    _out.WriteLine(
                        $"{field,-16}{before.Count,8}{same,6}{before.Count - same,9}" +
                        $"{presses,10}{worst,8:0.000}{band[0],9}{band[1],7}{band[2],7}{band[3],7}");
                }

                // Every route that is charged anything at all, so the ceiling is
                // set against the real spread rather than against four buckets.
                _out.WriteLine(string.Empty);
                _out.WriteLine("every route charged for pressing, dearest first:");

                var all = new List<(string Field, double Ratio)>();

                Marching.PricePressingHonestly = true;

                foreach (string field in AllProvingFields)
                {
                    Pressed(field, out List<double> ratios, out _);

                    foreach (double r in ratios)
                        if (r > 1.001d) all.Add((field, r));
                }

                all.Sort((a, b) => b.Ratio.CompareTo(a.Ratio));

                for (int i = 0; i < all.Count && i < 30; i++)
                    _out.WriteLine($"  {all[i].Ratio,7:0.000}  {all[i].Field}");

                _out.WriteLine($"  ... {all.Count} routes charged in all");

                // What each ceiling does: how many presses become a march that
                // stops short, and how far short it stops.
                _out.WriteLine(string.Empty);
                _out.WriteLine(
                    $"{"pressed <=",-10}{"presses",10}{"stopped short",15}{"routes moved",14}");
                _out.WriteLine(new string('-', 49));

                float wasCeiling = Marching.MostMetresPressed;

                foreach (float ceiling in new[] { 9999f, 100f, 50f, 25f, 15f, 8f, 2f })
                {
                    Marching.MostMetresPressed = ceiling;

                    int pressed = 0, moved = 0, stopped = 0;

                    foreach (string field in AllProvingFields)
                    {
                        Marching.PricePressingHonestly = false;
                        List<Vec2[]> flat = Pressed(field, out _, out _, out _);

                        Marching.PricePressingHonestly = true;
                        List<Vec2[]> now = Pressed(field, out _, out int these, out int shy);

                        pressed += these;
                        stopped += shy;

                        for (int i = 0; i < flat.Count; i++)
                            if (!SameRoute(flat[i], now[i])) moved++;
                    }

                    _out.WriteLine(
                        $"{(ceiling > 9000f ? "off" : ceiling.ToString("0") + " m"),-10}{pressed,10}" +
                        $"{stopped,15}{moved,14}");
                }

                Marching.MostMetresPressed = wasCeiling;

                // And the clock, because this is asked on every comparison the
                // cascade makes.
                _out.WriteLine(string.Empty);
                _out.WriteLine($"{"field",-16}{"as before",12}{"priced",10}{"change",10}");
                _out.WriteLine(new string('-', 48));

                foreach (string field in AllProvingFields)
                {
                    double flat = double.MaxValue, priced = double.MaxValue;

                    for (int pass = 0; pass < 3; pass++)
                    {
                        Marching.PricePressingHonestly = false;
                        flat = Math.Min(flat, Clocked(field, out _, out _));

                        Marching.PricePressingHonestly = true;
                        priced = Math.Min(priced, Clocked(field, out _, out _));
                    }

                    _out.WriteLine(
                        $"{field,-16}{flat,12:0.000}{priced,10:0.000}" +
                        $"{priced / flat - 1d,10:+0.0%;-0.0%;0.0%}");
                }
            }
            finally
            {
                Marching.SearchBudgetMs = wasBudget;
                Marching.PricePressingHonestly = wasPricing;
            }
        }

        /// <summary>
        /// Every order on a field, with each route priced twice - for the
        /// pressing and without it - so the ratio is the slowdown alone.
        /// </summary>
        /// <summary>How far short of the order counts as having stopped short.</summary>
        /// <remarks>
        /// Twenty-five metres, which is a cell. The placement search already
        /// moves an order by tens of metres when the ground is taken ([M32]), so
        /// anything under a cell is that and not this.
        /// </remarks>
        private const float StoppedShortMetres = 25f;

        private static List<Vec2[]> Pressed(
            string field, out List<double> ratios, out int presses) =>
            Pressed(field, out ratios, out presses, out _);

        private static List<Vec2[]> Pressed(
            string field, out List<double> ratios, out int presses, out int shortOf)
        {
            shortOf = 0;

            BattleState battle = BenchScenariosTests.Load(field);
            IPathfinder pathfinder = new DirectPathfinder(
                battle.Terrain, new TerrainMovementModel(TestContent.Terrain), TestContent.Terrain);

            foreach (UnitInstance warm in battle.UnitsOnField())
                Marching.PlanTo(battle, warm, pathfinder, BenchScenariosTests.OrderFor(battle, warm));

            var routes = new List<Vec2[]>();
            ratios = new List<double>();
            presses = 0;

            foreach (UnitInstance unit in battle.UnitsOnField())
            {
                Plan plan = Marching.PlanTo(
                    battle, unit, pathfinder, BenchScenariosTests.OrderFor(battle, unit));

                routes.Add(plan.Path.Found ? plan.Path.Waypoints.ToArray() : Array.Empty<Vec2>());

                if (!plan.Path.Found) continue;
                if (plan.PressedThrough) presses++;

                float flat = Marching.SecondsToWalk(battle, unit, plan.Path.Waypoints, plan.Hold);
                float charged =
                    Marching.SecondsToWalkPressing(battle, unit, plan.Path.Waypoints, plan.Hold);

                if (flat > 0.001f) ratios.Add(charged / (double)flat);

                // Short of where it was sent, which is what the ceiling turns a
                // press into. Measured against the order rather than inferred
                // from the rung, because the rung is the same either way.
                Vec2 asked = BenchScenariosTests.OrderFor(battle, unit);
                Vec2 ended = plan.Path.Waypoints[plan.Path.Waypoints.Count - 1];

                if (Vec2.Distance(ended, asked) > StoppedShortMetres) shortOf++;
            }

            return routes;
        }

        private static readonly string[] AllProvingFields =
            { "thecrowdedwing", "crucible", "brokencountry", "greatfield", "longmarch", "sidewaysmile" };


        /// <summary>
        /// What shape the routes actually come out, and which stage drew them.
        /// </summary>
        /// <remarks>
        /// The measurement behind [M129]. Three questions the designer asked
        /// about the grid, each of which has an arithmetic answer rather than an
        /// architectural one: how sharp the turns are, how much of the field the
        /// halo bans, and how many orders the grid answers at all.
        /// </remarks>
        [Fact(Skip = "A record of a measurement rather than a check on one.")]
        public void WhatShapeTheRoutesComeOut()
        {
            float wasBudget = Marching.SearchBudgetMs;
            Marching.SearchBudgetMs = 0f;

            try
            {
                // The halo, as arithmetic. Every regiment on the bench is the
                // same rectangle at full strength, so one line covers them all.
                var print = new Footprint(40f, 20f);
                float fraction = Math.Clamp(RegimentGrid.ClearanceFraction, 0f, 1f);
                float reach =
                    print.HalfDepth + (print.BoundingRadius - print.HalfDepth) * fraction +
                    Math.Max(0f, RegimentGrid.MarginMetres);
                float spacing = Math.Max(1f, print.BoundingRadius * 2f * RegimentGrid.SpacingMultiple);

                _out.WriteLine($"regiment          {print.Width:0}x{print.Depth:0} m, " +
                               $"bounding radius {print.BoundingRadius:0.0} m");
                _out.WriteLine($"coarse cell       {spacing:0.0} m across");
                _out.WriteLine("fine cells        " + string.Join(", ",
                    StagedRoutePlanner.FineSpacings.Select(f => $"{spacing * f:0.0} m")));
                _out.WriteLine($"terrain hex       {HexPathfinder.DefaultCellSpacingMetres:0.0} m");
                _out.WriteLine($"halo per body     {reach:0.0} m ({RegimentGrid.Halo})");
                _out.WriteLine($"gap needed        {2f * reach:0.0} m of clear air between two bodies " +
                               "before any point in it is unbanned");
                _out.WriteLine(string.Empty);

                _out.WriteLine(
                    $"{"field",-16}{"orders",8}{"legs",7}{"detour",9}{"worst",8}" +
                    $"{">60deg",8}{">90deg",8}{"worst turn",12}");
                _out.WriteLine(new string('-', 76));

                foreach (string field in AllProvingFields)
                {
                    BattleState battle = BenchScenariosTests.Load(field);
                    IPathfinder pathfinder = new DirectPathfinder(
                        battle.Terrain, new TerrainMovementModel(TestContent.Terrain),
                        TestContent.Terrain);

                    int orders = 0, legs = 0, sharp = 0, reversed = 0;
                    double detour = 0d, worstDetour = 0d, worstTurn = 0d;

                    foreach (UnitInstance unit in battle.UnitsOnField())
                    {
                        Plan plan = Marching.PlanTo(
                            battle, unit, pathfinder, BenchScenariosTests.OrderFor(battle, unit));

                        if (!plan.Path.Found) continue;

                        IReadOnlyList<Vec2> way = plan.Path.Waypoints;
                        if (way.Count < 2) continue;

                        orders++;
                        legs += way.Count - 1;

                        float walked = 0f;
                        for (int i = 1; i < way.Count; i++)
                            walked += Vec2.Distance(way[i - 1], way[i]);

                        float straight = Vec2.Distance(way[0], way[way.Count - 1]);

                        if (straight > 1f)
                        {
                            double over = walked / (double)straight;
                            detour += over;
                            worstDetour = Math.Max(worstDetour, over);
                        }

                        // The turn at each interior waypoint, which is what the
                        // eye reads as a corner.
                        bool anySharp = false, anyReversed = false;

                        for (int i = 1; i < way.Count - 1; i++)
                        {
                            Vec2 into = way[i] - way[i - 1];
                            Vec2 outOf = way[i + 1] - way[i];

                            if (into.IsNearZero || outOf.IsNearZero) continue;

                            double turn = Math.Abs(
                                Facing.FromVector(outOf).Radians - Facing.FromVector(into).Radians);

                            if (turn > Math.PI) turn = 2d * Math.PI - turn;

                            double degrees = turn * 180d / Math.PI;
                            worstTurn = Math.Max(worstTurn, degrees);

                            if (degrees > 60d) anySharp = true;
                            if (degrees > 90d) anyReversed = true;
                        }

                        if (anySharp) sharp++;
                        if (anyReversed) reversed++;
                    }

                    _out.WriteLine(
                        $"{field,-16}{orders,8}{legs / (double)Math.Max(1, orders),7:0.0}" +
                        $"{detour / Math.Max(1, orders),9:0.000}{worstDetour,8:0.00}" +
                        $"{sharp,8}{reversed,8}{worstTurn,12:0}");
                }

                _out.WriteLine(string.Empty);
                _out.WriteLine("Which stage drew them");
                _out.WriteLine(
                    $"{"field",-16}{"staged",8}{"ladder",8}{"bent",7}{"grid",7}" +
                    $"{"fine",7}{"tangent",9}{"pose",7}{"press",7}");
                _out.WriteLine(new string('-', 76));

                foreach (string field in AllProvingFields)
                {
                    BattleState battle = BenchScenariosTests.Load(field);
                    IPathfinder pathfinder = new DirectPathfinder(
                        battle.Terrain, new TerrainMovementModel(TestContent.Terrain),
                        TestContent.Terrain);

                    foreach (UnitInstance warm in battle.UnitsOnField())
                        Marching.PlanTo(
                            battle, warm, pathfinder, BenchScenariosTests.OrderFor(battle, warm));

                    StagedRoutePlanner.ResetCounters();

                    foreach (UnitInstance unit in battle.UnitsOnField())
                        Marching.PlanTo(
                            battle, unit, pathfinder, BenchScenariosTests.OrderFor(battle, unit));

                    _out.WriteLine(
                        $"{field,-16}{StagedRoutePlanner.Staged,8}" +
                        $"{StagedRoutePlanner.LadderClean,8}{StagedRoutePlanner.LadderBent,7}" +
                        $"{GridRoutePlanner.Found,7}" +
                        $"{GridRoutePlanner.FineFound,7}" +
                        $"{StagedRoutePlanner.TangentClean,9}{StagedRoutePlanner.PoseWon,7}" +
                        $"{StagedRoutePlanner.Pressed,7}");
                }
            }
            finally
            {
                Marching.SearchBudgetMs = wasBudget;
            }
        }

        private static int OrdersOn(string field)
        {
            int many = 0;
            foreach (UnitInstance unit in BenchScenariosTests.Load(field).UnitsOnField()) many++;
            return many;
        }

        private static void PlanEveryOrder(string field)
        {
            BattleState battle = BenchScenariosTests.Load(field);
            IPathfinder pathfinder = new DirectPathfinder(
                battle.Terrain, new TerrainMovementModel(TestContent.Terrain), TestContent.Terrain);

            foreach (UnitInstance unit in battle.UnitsOnField())
                Marching.PlanTo(battle, unit, pathfinder, BenchScenariosTests.OrderFor(battle, unit));
        }

        /// <summary>Every order on a field, timed as one, with the routes kept.</summary>
        private static double Clocked(string field, out List<Vec2[]> routes, out long sweeps)
        {
            BattleState battle = BenchScenariosTests.Load(field);
            IPathfinder pathfinder = new DirectPathfinder(
                battle.Terrain, new TerrainMovementModel(TestContent.Terrain), TestContent.Terrain);

            routes = new List<Vec2[]>();

            var watch = Stopwatch.StartNew();

            foreach (UnitInstance unit in battle.UnitsOnField())
            {
                Plan plan = Marching.PlanTo(
                    battle, unit, pathfinder, BenchScenariosTests.OrderFor(battle, unit));

                routes.Add(plan.Path.Found ? plan.Path.Waypoints.ToArray() : Array.Empty<Vec2>());
            }

            watch.Stop();

            // Counted on a separate instrumented pass, because a tally inside
            // the timed one is a branch on every sweep and this is a clock.
            PlanningProfile.Start();
            PlanEveryOrder(field);
            sweeps = PlanningProfile.CallsTo(PlanningProfile.Step.SweepTest);
            PlanningProfile.Stop();

            return watch.Elapsed.TotalMilliseconds / Math.Max(1, routes.Count);
        }

        /// <summary>One order, with the body that was ordered.</summary>
        private readonly struct Sized
        {
            public Sized(string kind, float width, float depth, double milliseconds, long sweeps)
            {
                Kind = kind;
                Width = width;
                Depth = depth;
                Milliseconds = milliseconds;
                Sweeps = sweeps;
            }

            public readonly string Kind;
            public readonly float Width;
            public readonly float Depth;
            public readonly double Milliseconds;
            public readonly long Sweeps;
        }

        /// <summary>Every order on a field, timed one at a time.</summary>
        /// <remarks>
        /// The profile is on only for the sweep count, which is a tally rather
        /// than a clock - but a tally still costs a branch a call, so the
        /// milliseconds here are an instrumented figure and are only ever
        /// compared against each other.
        /// </remarks>
        private static List<Sized> ByFootprint(string field)
        {
            BattleState battle = BenchScenariosTests.Load(field);
            IPathfinder pathfinder = new DirectPathfinder(
                battle.Terrain, new TerrainMovementModel(TestContent.Terrain), TestContent.Terrain);

            foreach (UnitInstance warm in battle.UnitsOnField())
                Marching.PlanTo(battle, warm, pathfinder, BenchScenariosTests.OrderFor(battle, warm));

            var orders = new List<Sized>();

            foreach (UnitInstance unit in battle.UnitsOnField())
            {
                Vec2 to = BenchScenariosTests.OrderFor(battle, unit);

                PlanningProfile.Start();

                long began = Stopwatch.GetTimestamp();
                Marching.PlanTo(battle, unit, pathfinder, to);
                long spent = Stopwatch.GetTimestamp() - began;

                long sweeps = PlanningProfile.CallsTo(PlanningProfile.Step.SweepTest);
                PlanningProfile.Stop();

                orders.Add(new Sized(
                    unit.Def.DisplayName, unit.Footprint.Width, unit.Footprint.Depth,
                    PlanningProfile.Milliseconds(spent), sweeps));
            }

            return orders;
        }

        private static double Least(List<Row> rows, Func<Row, double> of)
        {
            double least = double.MaxValue;
            foreach (Row row in rows) least = Math.Min(least, of(row));
            return least;
        }

        /// <summary>Both ends of a count across the passes, or the one if they agree.</summary>
        private static string Spread(List<Row> rows, Func<Row, int> of)
        {
            int low = int.MaxValue, high = int.MinValue;

            foreach (Row row in rows)
            {
                low = Math.Min(low, of(row));
                high = Math.Max(high, of(row));
            }

            return low == high ? low.ToString() : $"{low}-{high}";
        }

        private static string Both(List<int> counts)
        {
            int low = int.MaxValue, high = int.MinValue;

            foreach (int count in counts)
            {
                low = Math.Min(low, count);
                high = Math.Max(high, count);
            }

            return low == high ? low.ToString() : $"{low}-{high}";
        }

        /// <summary>
        /// How many candidate places a march actually reaches, against the cap
        /// that sizes the scratchpad for it.
        /// </summary>
        [Fact(Skip = "A record of a measurement rather than a check on one. It is what " +
                     "found that the tangent stage wins nothing on any bench field — see " +
                     "open finding 22 — and it resets global counters while it runs.")]
        public void HowManyPlacesAMarchActuallyReaches()
        {
            foreach (string field in Fields)
            {
                BattleState battle = BenchScenariosTests.Load(field);
                IPathfinder pathfinder = new DirectPathfinder(
                    battle.Terrain, new TerrainMovementModel(TestContent.Terrain), TestContent.Terrain);

                var counts = new List<long>();

                StagedRoutePlanner.ResetCounters();

                foreach (UnitInstance unit in battle.UnitsOnField())
                {
                    Plan plan = Marching.PlanTo(
                        battle, unit, pathfinder, BenchScenariosTests.OrderFor(battle, unit));

                    counts.Add(RouteSearch.PlacesHighWater);
                    RouteSearch.PlacesHighWater = 0;
                }

                _out.WriteLine(
                    $"{field,-16} rungs: ladder {StagedRoutePlanner.LadderClean} clean + " +
                    $"{StagedRoutePlanner.LadderBent} bent   grid {StagedRoutePlanner.GridClean}   " +
                    $"tangent {StagedRoutePlanner.TangentClean} " +
                    $"(too dear {StagedRoutePlanner.TangentTooDear})   " +
                    $"lattice asked {StagedRoutePlanner.PoseAsked} won {StagedRoutePlanner.PoseWon}   " +
                    $"pressed {StagedRoutePlanner.Pressed}");

                _out.WriteLine(
                    $"{field,-16} why the tangent answer was refused: " +
                    $"first leg {StagedRoutePlanner.BadFirstLeg}   " +
                    $"later leg {StagedRoutePlanner.BadLaterLeg}   " +
                    $"pressed {StagedRoutePlanner.BadPressed}   " +
                    $"no route at all {StagedRoutePlanner.BadNoRoute}");

                counts.Sort();

                _out.WriteLine(
                    $"{field,-16} cap {RouteSearch.MostPlaces,3}   " +
                    $"most {counts[^1],4}   median {counts[counts.Count / 2],4}   " +
                    $"searched {counts.FindAll(c => c > 0).Count,3} of {counts.Count}   " +
                    $"over 24: {counts.FindAll(c => c > 24).Count,3}   " +
                    $"at the cap: {counts.FindAll(c => c >= RouteSearch.MostPlaces).Count,3}");
            }
        }


        /// <summary>
        /// What an order costs broken down by the stage that answered it,
        /// rather than averaged over all of them.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The designer, on M84's tables: <i>"shouldnt worst single order be
        /// some microseconds which is ladder's straight-line -&gt; if it works it
        /// works?"</i> Two different things were tangled in the numbers being
        /// read, and only one of them was reported. The <b>worst</b> order is by
        /// construction never the straight-line case — it is the order where the
        /// straight line failed and the whole cascade ran. But the question
        /// underneath it is fair and had no answer anywhere: <b>what does an
        /// order cost when the cheap rung answers it?</b> Mean and worst cannot
        /// say, because the mean is smeared across populations that differ by
        /// three orders of magnitude.
        /// </para>
        /// <para>
        /// Attributed by which stage counter moved, not by
        /// <c>UnitInstance.LastRung</c>: that records the ladder's own rung, and
        /// the staged planner has stages the ladder knows nothing about.
        /// </para>
        /// <para>
        /// Timed with a plain stopwatch and the profiler off. Every probe is two
        /// timestamp reads, and at the scale of a straight-line answer the
        /// instrumentation would be a visible share of what it claims to
        /// measure.
        /// </para>
        /// </remarks>
        [Fact(Skip = "A record of a measurement rather than a check on one. It is the one that says the straight line answers half the orders for a tenth of a per cent of the time — M85, re-taken after M86.")]
        public void WhatAnOrderCostsByTheStageThatAnsweredIt()
        {
            foreach (string field in Fields)
            {
                BattleState battle = BenchScenariosTests.Load(field);
                IPathfinder pathfinder = new DirectPathfinder(
                    battle.Terrain, new TerrainMovementModel(TestContent.Terrain), TestContent.Terrain);

                // Warm, unmeasured. An unwarmed first order carries the whole
                // cost of compiling the planner.
                foreach (UnitInstance warm in battle.UnitsOnField())
                    Marching.PlanTo(
                        battle, warm, pathfinder, BenchScenariosTests.OrderFor(battle, warm));

                var byStage = new Dictionary<string, List<double>>();

                foreach (UnitInstance unit in battle.UnitsOnField())
                {
                    StagedRoutePlanner.ResetCounters();

                    long began = Stopwatch.GetTimestamp();

                    Marching.PlanTo(
                        battle, unit, pathfinder, BenchScenariosTests.OrderFor(battle, unit));

                    double spent =
                        (Stopwatch.GetTimestamp() - began) * 1_000_000d / Stopwatch.Frequency;

                    if (!byStage.TryGetValue(WhoAnswered(), out List<double>? kept))
                        byStage[WhoAnswered()] = kept = new List<double>();

                    kept.Add(spent);
                }

                _out.WriteLine($"=== {field} ===");
                _out.WriteLine(
                    $"{"answered by",-22}{"orders",8}{"median us",12}{"p90 us",10}" +
                    $"{"worst us",10}{"share of total",16}");

                double whole = 0d;
                foreach (List<double> kept in byStage.Values)
                    foreach (double one in kept) whole += one;

                foreach (KeyValuePair<string, List<double>> pair in
                         byStage.OrderByDescending(p => p.Value.Sum()))
                {
                    List<double> kept = pair.Value;
                    kept.Sort();

                    double sum = kept.Sum();

                    _out.WriteLine(
                        $"{pair.Key,-22}{kept.Count,8}{kept[kept.Count / 2],12:0.0}" +
                        $"{kept[(int)(kept.Count * 0.9)],10:0.0}{kept[^1],10:0.0}" +
                        $"{sum / whole,15:0.0%}");
                }

                _out.WriteLine(string.Empty);
            }
        }

        /// <summary>Which stage of the cascade returned the route just planned.</summary>
        private static string WhoAnswered()
        {
            if (StagedRoutePlanner.LadderClean > 0) return "straight line";
            if (StagedRoutePlanner.LadderBent > 0) return "bent ladder";
            if (StagedRoutePlanner.Staged > 0) return "staged egress";
            if (StagedRoutePlanner.TangentClean > 0) return "tangents";
            if (StagedRoutePlanner.CornersClean > 0) return "corners";
            if (StagedRoutePlanner.RingsClean > 0) return "rings";
            if (StagedRoutePlanner.GridClean > 0) return "regiment grid";
            if (StagedRoutePlanner.PoseWon > 0) return "pose lattice";
            if (StagedRoutePlanner.Pressed > 0) return "pressed through";

            return "fell through";
        }


        /// <summary>
        /// Whether the tangent stage earns its place now that the grid is asked
        /// before it, and what it costs when it does not.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Two questions, and they are not the same one. <b>Is the stage
        /// reached?</b> - after M86 the grid answers above it, so an order only
        /// draws the tangent graph if the grid could not route it. <b>Does the
        /// stage win anything when it is reached?</b> - from its old position
        /// it won nothing at all on 280 bench orders, and the interesting
        /// possibility is that the nine-odd orders which now reach it are
        /// exactly the hard ones it was always going to refuse.
        /// </para>
        /// <para>
        /// The search is <i>not</i> removed when the stage is turned off: its
        /// plan is still the terminal fallback, so an order that falls past the
        /// lattice and past the press still draws it. That is the honest
        /// comparison - stage against no stage, not search against no search.
        /// </para>
        /// </remarks>
        [Fact(Skip = "A record of a measurement rather than a check on one - what M90's two corrections cost.")]
        public void DoWeNeedTheTangentStage()
        {
            bool wasStage = StagedRoutePlanner.AskTangentStage;

            try
            {
                _out.WriteLine(
                    $"{"field",-15}{"stage",7}{"orders",8}{"drew",6}{"won",5}{"grid",6}" +
                    $"{"pose",6}{"press",7}{"fell",6}{"walk s",11}{"plan ms",10}");

                foreach (string field in Fields)
                {
                    StagedRoutePlanner.AskTangentStage = true;
                    Dictionary<string, string> on = WithTheStage(field, "on");

                    StagedRoutePlanner.AskTangentStage = false;
                    Dictionary<string, string> off = WithTheStage(field, "off");

                    var moved = new List<string>();

                    foreach (KeyValuePair<string, string> pair in on)
                        if (off[pair.Key] != pair.Value)
                            moved.Add($"    {pair.Key}\n      on:  {pair.Value}\n      off: {off[pair.Key]}");

                    _out.WriteLine(
                        $"{string.Empty,15}{moved.Count} of {on.Count} routes move when the stage goes");

                    foreach (string line in moved.Take(6)) _out.WriteLine(line);

                    _out.WriteLine(string.Empty);
                }
            }
            finally
            {
                StagedRoutePlanner.AskTangentStage = wasStage;
            }
        }

        /// <summary>One field, one setting: a row of counters and every route.</summary>
        private Dictionary<string, string> WithTheStage(string field, string label)
        {
            BattleState battle = BenchScenariosTests.Load(field);

            IPathfinder pathfinder = new DirectPathfinder(
                battle.Terrain, new TerrainMovementModel(TestContent.Terrain), TestContent.Terrain);

            // Warm, unmeasured.
            foreach (UnitInstance warm in battle.UnitsOnField())
                Marching.PlanTo(
                    battle, warm, pathfinder, BenchScenariosTests.OrderFor(battle, warm));

            var routes = new Dictionary<string, string>();

            StagedRoutePlanner.ResetCounters();

            double walked = 0d;
            long began = Stopwatch.GetTimestamp();

            foreach (UnitInstance unit in battle.UnitsOnField())
            {
                Plan plan = Marching.PlanTo(
                    battle, unit, pathfinder, BenchScenariosTests.OrderFor(battle, unit));

                walked += Marching.SecondsToWalk(battle, unit, plan.Path.Waypoints, plan.Hold);

                var line = new System.Text.StringBuilder();
                line.Append(plan.Path.Found ? "ok " : "NO ");
                line.Append(plan.PressedThrough ? "press " : "clean ");

                foreach (Vec2 at in plan.Path.Waypoints)
                    line.Append(System.FormattableString.Invariant($"({at.X:0.00},{at.Y:0.00})"));

                routes[$"u{unit.Id.Value:D3}"] = line.ToString();
            }

            double spent = (Stopwatch.GetTimestamp() - began) * 1000d / Stopwatch.Frequency;

            // What fell all the way past the press to the terminal fallback: the
            // orders for which the tangent plan is the only thing standing
            // between a regiment and no route at all.
            int answered =
                StagedRoutePlanner.Staged + StagedRoutePlanner.LadderClean +
                StagedRoutePlanner.LadderBent + StagedRoutePlanner.GridClean +
                StagedRoutePlanner.TangentClean + StagedRoutePlanner.CornersClean +
                StagedRoutePlanner.RingsClean + StagedRoutePlanner.PoseWon +
                StagedRoutePlanner.Pressed;

            _out.WriteLine(
                $"{field,-15}{label,7}{routes.Count,8}{StagedRoutePlanner.TangentAsked,6}" +
                $"{StagedRoutePlanner.TangentClean,5}{StagedRoutePlanner.GridClean,6}" +
                $"{StagedRoutePlanner.PoseWon,6}{StagedRoutePlanner.Pressed,7}" +
                $"{routes.Count - answered,6}{walked,11:0.0}{spent,10:0.0}");

            return routes;
        }


        /// <summary>
        /// The turn field's own geometry against the clock: how much room it is
        /// given beyond the straight line, and how many cells it is cut into.
        /// </summary>
        /// <remarks>
        /// <c>DetourRoomFraction</c> was the largest number in the profile that
        /// had never been swept - a <c>private const</c> with no remark, applied
        /// on all four sides, so the field is about 2,9x the area of the tight
        /// box round start and goal and the fill goes as the square of it.
        /// Outside the field <c>SecondsFrom</c> hands back the caller's fallback
        /// rather than failing, so a smaller field is not wrong, only worse
        /// advised - and a worse-advised lattice expands more. Which way that
        /// nets out cannot be argued from the geometry, only clocked.
        /// </remarks>
        [Fact]
        public void TheTurnFieldsOwnGeometry()
        {
            float wasRoom = HybridTurnField.DetourRoomFraction;
            float wasAcross = HybridTurnField.TargetCellsAcross;

            try
            {
                foreach (string field in Fields)
                {
                    _out.WriteLine($"=== {field} ===");
                    _out.WriteLine(
                        $"{"room",6}{"across",8}{"ms",10}{"field ms",10}{"expanded",10}" +
                        $"{"walk s",12}{"press",7}{"moved",7}");

                    Dictionary<string, string>? baseline = null;

                    foreach (float room in new[] { 0.5f, 0.4f, 0.3f, 0.2f })
                    foreach (float across in new[] { 48f, 32f })
                    {
                        if (room != 0.5f && across != 48f) continue;

                        HybridTurnField.DetourRoomFraction = room;
                        HybridTurnField.TargetCellsAcross = across;

                        Dictionary<string, string> routes = OneSetting(
                            field, room, across, baseline, out _);

                        baseline ??= routes;
                    }

                    _out.WriteLine(string.Empty);
                }
            }
            finally
            {
                HybridTurnField.DetourRoomFraction = wasRoom;
                HybridTurnField.TargetCellsAcross = wasAcross;
            }
        }

        /// <summary>One field at one setting: least of three, then the quality.</summary>
        private Dictionary<string, string> OneSetting(
            string field, float room, float across,
            Dictionary<string, string>? baseline, out double least)
        {
            BattleState battle = BenchScenariosTests.Load(field);

            IPathfinder pathfinder = new DirectPathfinder(
                battle.Terrain, new TerrainMovementModel(TestContent.Terrain), TestContent.Terrain);

            var routes = new Dictionary<string, string>();
            double walked = 0d;
            int pressed = 0;

            least = double.MaxValue;

            // Warm, then three passes and the least of them. Noise on work like
            // this is one-sided, so the least is the honest figure.
            const int passes = 6;

            for (int pass = 0; pass < passes; pass++)
            {
                routes.Clear();
                walked = 0d;
                pressed = 0;

                long began = Stopwatch.GetTimestamp();

                foreach (UnitInstance unit in battle.UnitsOnField())
                {
                    Plan plan = Marching.PlanTo(
                        battle, unit, pathfinder, BenchScenariosTests.OrderFor(battle, unit));

                    // The quality is taken on the LAST pass, not the first.
                    // Taken on the first it was cleared again by every pass
                    // after it, and the table reported nought walked, nought
                    // pressed and nought routes moved on every row - which read
                    // exactly like a clean result and was an empty dictionary
                    // compared with an empty dictionary.
                    if (pass < passes - 1) continue;

                    walked += Marching.SecondsToWalk(battle, unit, plan.Path.Waypoints, plan.Hold);
                    if (plan.PressedThrough) pressed++;

                    var line = new System.Text.StringBuilder();
                    foreach (Vec2 at in plan.Path.Waypoints)
                        line.Append(System.FormattableString.Invariant($"({at.X:0.00},{at.Y:0.00})"));

                    routes[$"u{unit.Id.Value:D3}"] = line.ToString();
                }

                double spent = (Stopwatch.GetTimestamp() - began) * 1000d / Stopwatch.Frequency;

                if (pass > 0 && spent < least) least = spent;

                // Non-vacuity: a row whose routes were never captured would
                // agree with any baseline perfectly and prove nothing.
                if (pass == passes - 1 && routes.Count == 0)
                    throw new InvalidOperationException("no routes captured on the measured pass");
            }

            // And one instrumented pass, for the field's own share.
            BattleState probed = BenchScenariosTests.Load(field);
            PlanningProfile.Start();
            BenchScenariosTests.OrderEverybody(probed, null);
            PlanningProfile.Stop();

            double fieldMs = PlanningProfile.InclusiveMilliseconds(PlanningProfile.Step.HybridField);
            long expanded = PlanningProfile.CallsTo(PlanningProfile.Step.HybridPose);

            int moved = 0;

            if (baseline != null)
                foreach (KeyValuePair<string, string> pair in routes)
                    if (baseline[pair.Key] != pair.Value)
                        moved++;

            _out.WriteLine(
                $"{room,6:0.0}{across,8:0}{least,10:0.0}{fieldMs,10:0.0}{expanded,10:N0}" +
                $"{walked,12:0.0}{pressed,7}{(baseline == null ? "-" : moved.ToString()),7}");

            return routes;
        }


        /// <summary>
        /// Whether the coarse route is there to seed the fine one: how often the
        /// grid <i>found</i> a route that was then refused, on the orders the
        /// lattice had to answer from nothing.
        /// </summary>
        /// <remarks>
        /// The lattice runs unbounded on every order the grid could not settle,
        /// rediscovering topology from scratch. If the grid <i>found</i> a route
        /// on those orders and it was merely refused by the walk gate, that
        /// route is still a true statement about which side of which body the
        /// answer lies - and bounding the lattice to a tube round it is the one
        /// version of the tube idea that has never been tried. The tube from the
        /// tangent search failed because 74 of 94 cheap routes were
        /// press-throughs; a grid route cannot be one.
        /// </remarks>
        [Fact]
        public void IsThereACoarseRouteToSeedTheFineOne()
        {
            _out.WriteLine(
                $"{"field",-16}{"orders",8}{"asked",7}{"found",7}{"held",6}" +
                $"{"refused",9}{"lattice",9}{"seedable",10}");

            foreach (string field in Fields)
            {
                BattleState battle = BenchScenariosTests.Load(field);

                IPathfinder pathfinder = new DirectPathfinder(
                    battle.Terrain, new TerrainMovementModel(TestContent.Terrain), TestContent.Terrain);

                foreach (UnitInstance warm in battle.UnitsOnField())
                    Marching.PlanTo(battle, warm, pathfinder, BenchScenariosTests.OrderFor(battle, warm));

                StagedRoutePlanner.ResetCounters();

                int orders = 0;

                foreach (UnitInstance unit in battle.UnitsOnField())
                {
                    Marching.PlanTo(battle, unit, pathfinder, BenchScenariosTests.OrderFor(battle, unit));
                    orders++;
                }

                int refused = GridRoutePlanner.Found - GridRoutePlanner.Held;

                _out.WriteLine(
                    $"{field,-16}{orders,8}{GridRoutePlanner.Asked,7}{GridRoutePlanner.Found,7}" +
                    $"{GridRoutePlanner.Held,6}{refused,9}{StagedRoutePlanner.PoseWon,9}" +
                    $"{Math.Min(refused, StagedRoutePlanner.PoseWon),10}");
            }
        }


        /// <summary>
        /// The grid at finer cells: does a smaller cell answer the orders the
        /// regiment-sized one cannot, and take them off the lattice?
        /// </summary>
        /// <remarks>
        /// The one live form of "coarse route seeds fine route". The seed
        /// version is dead - measured, the grid <i>found</i> nothing at all on
        /// every order the lattice had to answer on three of four fields, so
        /// there is no coarse route to bound a tube around. What is left is the
        /// other direction: the orders reaching the lattice are the ones a cell
        /// the size of a regiment cannot express, and a cell half or a quarter
        /// that size might. Each lattice order costs about 26 ms and each grid
        /// order about 6, so moving even half of them is worth more than every
        /// micro-optimisation in the profile put together.
        /// </remarks>
        [Fact]
        public void TheGridAtFinerCells()
        {
            float was = RegimentGrid.SpacingMultiple;

            try
            {
                _out.WriteLine(
                    $"{"field",-16}{"cells",7}{"asked",7}{"found",7}{"grid",6}{"lattice",9}" +
                    $"{"press",7}{"ms",9}{"walk s",11}{"moved",7}");

                foreach (string field in Fields)
                {
                    Dictionary<string, string>? baseline = null;

                    foreach (float multiple in new[] { 1f, 0.5f, 0.25f })
                    {
                        RegimentGrid.SpacingMultiple = multiple;
                        baseline = AtThisSpacing(field, multiple, baseline);
                    }

                    _out.WriteLine(string.Empty);
                }
            }
            finally
            {
                RegimentGrid.SpacingMultiple = was;
            }
        }

        private Dictionary<string, string> AtThisSpacing(
            string field, float multiple, Dictionary<string, string>? baseline)
        {
            BattleState battle = BenchScenariosTests.Load(field);

            IPathfinder pathfinder = new DirectPathfinder(
                battle.Terrain, new TerrainMovementModel(TestContent.Terrain), TestContent.Terrain);

            foreach (UnitInstance warm in battle.UnitsOnField())
                Marching.PlanTo(battle, warm, pathfinder, BenchScenariosTests.OrderFor(battle, warm));

            var routes = new Dictionary<string, string>();
            double walked = 0d;
            int pressed = 0;
            double least = double.MaxValue;

            const int passes = 4;

            for (int pass = 0; pass < passes; pass++)
            {
                routes.Clear();
                walked = 0d;
                pressed = 0;

                StagedRoutePlanner.ResetCounters();

                long began = Stopwatch.GetTimestamp();

                foreach (UnitInstance unit in battle.UnitsOnField())
                {
                    Plan plan = Marching.PlanTo(
                        battle, unit, pathfinder, BenchScenariosTests.OrderFor(battle, unit));

                    if (pass < passes - 1) continue;

                    walked += Marching.SecondsToWalk(battle, unit, plan.Path.Waypoints, plan.Hold);
                    if (plan.PressedThrough) pressed++;

                    var line = new System.Text.StringBuilder();
                    foreach (Vec2 at in plan.Path.Waypoints)
                        line.Append(System.FormattableString.Invariant($"({at.X:0.00},{at.Y:0.00})"));

                    routes[$"u{unit.Id.Value:D3}"] = line.ToString();
                }

                double spent = (Stopwatch.GetTimestamp() - began) * 1000d / Stopwatch.Frequency;
                if (spent < least) least = spent;
            }

            if (routes.Count == 0) throw new InvalidOperationException("no routes captured");

            int moved = 0;

            if (baseline != null)
                foreach (KeyValuePair<string, string> pair in routes)
                    if (baseline[pair.Key] != pair.Value) moved++;

            _out.WriteLine(
                $"{field,-16}{multiple,7:0.00}{GridRoutePlanner.Asked,7}{GridRoutePlanner.Found,7}" +
                $"{StagedRoutePlanner.GridClean,6}{StagedRoutePlanner.PoseWon,9}{pressed,7}" +
                $"{least,9:0.0}{walked,11:0.0}{(baseline == null ? "-" : moved.ToString()),7}");

            return baseline ?? routes;
        }


        /// <summary>
        /// The two-tier grid as it would ship: the coarse grid asked first, and
        /// only the orders it cannot answer paying for a finer one.
        /// </summary>
        /// <remarks>
        /// <b>M87.</b> <see cref="TheGridAtFinerCells"/> asked what one finer
        /// spacing does when every order pays for it. This asks the shipping
        /// question instead: coarse first, then the tiers in
        /// <c>FineSpacings</c> only where the coarse grid found nothing, so the
        /// sixteen-fold field is paid on the eight or ten orders a field that
        /// reach it rather than on all thirty-two. The columns that decide it
        /// are <c>press</c> and <c>walk s</c> together with <c>ms</c>: a field
        /// may pay wall clock to buy press-throughs back, which
        /// <see cref="StagedRoutePlanner.WayRoundCostCeiling"/> sanctions up to
        /// three times the pressed route (<b>W12</b>).
        /// </remarks>
        [Fact]
        public void TheTwoTierGridAsShipped()
        {
            float[] was = StagedRoutePlanner.FineSpacings;

            try
            {
                _out.WriteLine(
                    $"{"field",-16}{"tiers",-16}{"coarse",8}{"fine ask",9}{"fine won",9}" +
                    $"{"lattice",9}{"press",7}{"side",6}{"ms",9}{"walk s",11}{"moved",7}");
                _out.WriteLine(new string('-', 108));

                foreach (string field in Fields)
                {
                    Dictionary<string, string>? baseline = null;

                    foreach ((string name, float[] tiers) in new[]
                    {
                        ("coarse only", Array.Empty<float>()),
                        ("+ half", new[] { 0.5f }),
                        ("+ half, quarter", new[] { 0.5f, 0.25f }),
                        ("+ quarter", new[] { 0.25f }),
                    })
                    {
                        StagedRoutePlanner.FineSpacings = tiers;
                        baseline = AtTheseTiers(field, name, baseline);
                    }

                    _out.WriteLine(string.Empty);
                }
            }
            finally
            {
                StagedRoutePlanner.FineSpacings = was;
            }
        }

        /// <summary>One field at one set of tiers: least of four, then the quality.</summary>
        private Dictionary<string, string> AtTheseTiers(
            string field, string name, Dictionary<string, string>? baseline)
        {
            BattleState battle = BenchScenariosTests.Load(field);

            IPathfinder pathfinder = new DirectPathfinder(
                battle.Terrain, new TerrainMovementModel(TestContent.Terrain), TestContent.Terrain);

            foreach (UnitInstance warm in battle.UnitsOnField())
                Marching.PlanTo(battle, warm, pathfinder, BenchScenariosTests.OrderFor(battle, warm));

            var routes = new Dictionary<string, string>();
            double walked = 0d;
            int pressed = 0;
            double least = double.MaxValue;

            const int passes = 4;

            for (int pass = 0; pass < passes; pass++)
            {
                routes.Clear();
                walked = 0d;
                pressed = 0;

                StagedRoutePlanner.ResetCounters();

                long began = Stopwatch.GetTimestamp();

                foreach (UnitInstance unit in battle.UnitsOnField())
                {
                    Plan plan = Marching.PlanTo(
                        battle, unit, pathfinder, BenchScenariosTests.OrderFor(battle, unit));

                    if (pass < passes - 1) continue;

                    walked += Marching.SecondsToWalk(battle, unit, plan.Path.Waypoints, plan.Hold);
                    if (plan.PressedThrough) pressed++;

                    var line = new System.Text.StringBuilder();
                    foreach (Vec2 at in plan.Path.Waypoints)
                        line.Append(System.FormattableString.Invariant($"({at.X:0.00},{at.Y:0.00})"));

                    routes[$"u{unit.Id.Value:D3}"] = line.ToString();
                }

                double spent = (Stopwatch.GetTimestamp() - began) * 1000d / Stopwatch.Frequency;
                if (spent < least) least = spent;
            }

            if (routes.Count == 0) throw new InvalidOperationException("no routes captured");

            int moved = 0;

            if (baseline != null)
                foreach (KeyValuePair<string, string> pair in routes)
                    if (baseline[pair.Key] != pair.Value) moved++;

            _out.WriteLine(
                $"{field,-16}{name,-16}{GridRoutePlanner.Held,8}{GridRoutePlanner.FineAsked,9}" +
                $"{GridRoutePlanner.FineHeld,9}{StagedRoutePlanner.PoseWon,9}{pressed,7}" +
                $"{StagedRoutePlanner.SidewalkTook,6}" +
                $"{least,9:0.0}{walked,11:0.0}{(baseline == null ? "-" : moved.ToString()),7}");

            return baseline ?? routes;
        }


        /// <summary>
        /// The two holes M90 found in the grid, priced one at a time.
        /// </summary>
        /// <remarks>
        /// <c>KeepEndCells</c> stops the route jumping from where the regiment
        /// stands to a node a cell and a half away across ground nobody checked.
        /// <c>BlockedStepPenalty</c> stops the search being walled in by the
        /// halo of the body it is standing against. Both are corrections rather
        /// than optimisations, so what this asks is only what they cost.
        /// </remarks>
        [Fact]
        public void WhatTheTwoGridCorrectionsCost()
        {
            bool wasEnds = RegimentGrid.KeepEndCells;
            float wasPenalty = RegimentGrid.BlockedStepPenalty;

            try
            {
                _out.WriteLine(
                    $"{"field",-16}{"ends",-6}{"penalty",-9}{"coarse",8}{"fine ask",9}{"fine won",9}" +
                    $"{"lattice",9}{"press",7}{"ms",9}{"walk s",11}{"moved",7}");
                _out.WriteLine(new string('-', 102));

                foreach (string field in Fields)
                {
                    Dictionary<string, string>? baseline = null;

                    foreach ((bool ends, float penalty) in new[]
                    {
                        (false, 0f), (true, 0f), (true, 8f), (true, 25f), (true, 60f), (true, 200f),
                    })
                    {
                        RegimentGrid.KeepEndCells = ends;
                        RegimentGrid.BlockedStepPenalty = penalty;

                        baseline = AtTheseTiers(
                            field, $"{(ends ? "keep" : "drop"),-6}{penalty,-9:0}", baseline);
                    }

                    _out.WriteLine(string.Empty);
                }
            }
            finally
            {
                RegimentGrid.KeepEndCells = wasEnds;
                RegimentGrid.BlockedStepPenalty = wasPenalty;
            }
        }

        // ------------------------------------------------------------------ the work

        /// <summary>
        /// One planner on one field: warm, several uninstrumented passes for the
        /// headline, then one instrumented pass for the table.
        /// </summary>
        /// <summary>
        /// What the arriving licence costs on the bench, and how often it
        /// actually fires.
        /// </summary>
        /// <remarks>
        /// <b>M94b.</b> The licence is withheld on the first ask and only a
        /// second pass may have it, so an order that already answers pays
        /// nothing and an order that presses or fails is planned twice. This is
        /// what that second pass costs where it really happens, against how
        /// many orders keep its answer.
        /// </remarks>
        [Fact(Skip = "A record of a measurement rather than a check on one - what M94b's "  +
                     "second pass costs, and it drives global levers while it runs.")]
        public void WhatTheArrivingLicenceCosts()
        {
            bool was = StagedRoutePlanner.LicenceOnArrival;

            try
            {
                _out.WriteLine(
                    $"{"field",-16}{"licence",-10}{"ms/order",10}{"total ms",10}{"worst ms",10}" +
                    $"{"pressed",9}{"unwalk",8}{"route s",10}{"2nd pass",10}{"kept",7}");
                _out.WriteLine(new string('-', 100));

                foreach (string field in Fields)
                {
                    foreach (bool granted in new[] { false, true })
                    {
                        StagedRoutePlanner.LicenceOnArrival = granted;

                        Row row = Measure(field, planner: null, passes: 3);

                        // The counters describe one more uninstrumented pass,
                        // because Measure's timed passes reset them per run.
                        StagedRoutePlanner.ResetCounters();
                        BenchScenariosTests.OrderEverybody(BenchScenariosTests.Load(field), null);

                        _out.WriteLine(
                            $"{field,-16}{(granted ? "granted" : "withheld"),-10}" +
                            $"{row.MsPerOrder,10:0.000}{row.Total,10:0.0}{row.Worst,10:0.0}" +
                            $"{row.Pressed,9}{row.Unwalkable,8}{row.Seconds,10:0}" +
                            $"{StagedRoutePlanner.ArrivalAsked,10}{StagedRoutePlanner.ArrivalTook,7}");
                    }

                    _out.WriteLine(string.Empty);
                }
            }
            finally
            {
                StagedRoutePlanner.LicenceOnArrival = was;
            }
        }

        /// <summary>
        /// The spatial index at five bucket widths: what a query costs, what it
        /// hands back, and what the whole order costs.
        /// </summary>
        /// <remarks>
        /// <b>Open finding 23.</b> The halo the clearance path asks with is
        /// <c>reach + widest reach + half a bucket diagonal</c>, and a body may
        /// then sit half a diagonal outside the bucket it was found in - so at
        /// 128 m buckets there is <b>181 m of pure slack</b> around a reach of
        /// about ninety. That is why a query hands back 14,1 bodies. Narrowing
        /// it is not free: fewer bodies come at the price of more buckets, and
        /// which of the two dominates is what this asks.
        /// </remarks>
        [Fact(Skip = "A record of a measurement rather than a check on one - it is what closed " +
                     "the halo half of open finding 23 and then what re-opened it for M109, and " +
                     "it rebuilds the spatial index at seven widths while it runs.")]
        public void WhatTheIndexCostsAtEachBucketWidth()
        {
            float was = UnitIndex.BucketMetres;

            try
            {
                // A whole discard round first. The first row otherwise pays JIT
                // for everything the sweep touches and reads four times dear -
                // which matters more than usual here, because the first row is
                // the shipping width and every other row is read against it.
                foreach (string warm in Fields) Measure(warm, planner: null, passes: 1);

                _out.WriteLine(
                    $"{"field",-16}{"bucket",8}{"ms/order",10}{"BodyScan",10}{"NearQuery",11}" +
                    $"{"queries",10}{"bodies",10}{"per query",11}{"buckets",10}{"per query",11}");
                _out.WriteLine(new string('-', 107));

                foreach (string field in Fields)
                {
                    foreach (float bucket in new[] { 512f, 256f, 128f, 64f, 32f, 16f, 8f })
                    {
                        UnitIndex.BucketMetres = bucket;

                        Row row = Measure(field, planner: null, passes: 3);

                        // The counters and the step times come from one more
                        // pass, instrumented; the millisecond column above is
                        // the least of three uninstrumented ones.
                        BattleState probed = BenchScenariosTests.Load(field);
                        PlanningProfile.Start();
                        BenchScenariosTests.OrderEverybody(probed, null);
                        PlanningProfile.Stop();

                        long queries = PlanningProfile.CallsTo(PlanningProfile.Step.NearQuery);
                        long bodies = PlanningProfile.CallsTo(PlanningProfile.Step.NearYield);
                        long buckets = PlanningProfile.CallsTo(PlanningProfile.Step.NearBuckets);

                        _out.WriteLine(
                            $"{field,-16}{bucket,8:0}{row.MsPerOrder,10:0.000}" +
                            $"{PlanningProfile.SelfMilliseconds(PlanningProfile.Step.BodyScan),10:0.0}" +
                            $"{PlanningProfile.SelfMilliseconds(PlanningProfile.Step.NearQuery),11:0.0}" +
                            $"{queries,10}{bodies,10}{bodies / (double)Math.Max(1, queries),11:0.0}" +
                            $"{buckets,10}{buckets / (double)Math.Max(1, queries),11:0.0}");
                    }

                    _out.WriteLine(string.Empty);
                }
            }
            finally
            {
                UnitIndex.BucketMetres = was;
            }
        }

        /// <summary>
        /// How much of the lattice is left to save on, after M87.
        /// </summary>
        /// <remarks>
        /// Todo 04 proposes building the mover's two boxes lazily, on the
        /// strength of 221 127 poses producing 203 611 overlap tests. That
        /// count was taken when the lattice answered orders. It mostly does not
        /// any more, so the first question is how big the numerator still is -
        /// asked of the cascade as it ships and of the lattice asked directly.
        /// <para>
        /// <b>Answered — [M96].</b> The cascade reaches the lattice on one
        /// field of four and spends 124 poses there, so todo 04 is worth
        /// nothing where the game actually plans. Asked directly it is worth
        /// <b>2% to 9%</b>, winning eleven of twelve paired readings, with
        /// poses and overlaps identical to the digit.
        /// </para>
        /// </remarks>
        [Fact(Skip = "A record of a measurement rather than a check on one - it sized todo 04 " +
                     "and then weighed it, and it drives a global lever while it runs.")]
        public void HowMuchLatticeIsLeft()
        {
            bool was = HybridAStarPlanner.LazyBoxes;

            try
            {
                // Both planners warmed, not just the lattice. Warming only one
                // of them leaves the other's first row paying for its JIT, and
                // that row read 60% dear.
                foreach (string warm in Fields)
                {
                    Measure(warm, new HybridAStarRoutePlanner(), passes: 1);
                    Measure(warm, planner: null, passes: 1);
                }

                _out.WriteLine(
                    $"{"field",-16}{"asked",-14}{"boxes",-10}{"ms/order",10}{"poses",10}" +
                    $"{"overlaps",10}{"per pose",10}{"stock ms",10}{"worst ms",10}");
                _out.WriteLine(new string('-', 100));

                foreach (string field in Fields)
                {
                    foreach (bool lattice in new[] { false, true })
                    foreach (bool lazily in new[] { false, true })
                    {
                        HybridAStarPlanner.LazyBoxes = lazily;

                        IRoutePlanner? planner = lattice ? new HybridAStarRoutePlanner() : null;

                        Row row = Measure(field, planner, passes: 3);

                        BattleState probed = BenchScenariosTests.Load(field);
                        PlanningProfile.Start();
                        BenchScenariosTests.OrderEverybody(probed, planner);
                        PlanningProfile.Stop();

                        long poses = PlanningProfile.CallsTo(PlanningProfile.Step.HybridPose);
                        long overlaps = PlanningProfile.CallsTo(PlanningProfile.Step.HybridOverlap);

                        _out.WriteLine(
                            $"{field,-16}{(lattice ? "the lattice" : "the cascade"),-14}" +
                            $"{(lazily ? "lazy" : "both"),-10}{row.MsPerOrder,10:0.000}" +
                            $"{poses,10}{overlaps,10}" +
                            $"{overlaps / (double)Math.Max(1, poses),10:0.00}" +
                            $"{PlanningProfile.SelfMilliseconds(PlanningProfile.Step.HybridStock),10:0.0}" +
                            $"{row.Worst,10:0.0}");
                    }

                    _out.WriteLine(string.Empty);
                }
            }
            finally
            {
                HybridAStarPlanner.LazyBoxes = was;
            }
        }

        /// <summary>
        /// How far round a regiment has to wheel to start its march, and what
        /// the alternative would cost.
        /// </summary>
        /// <remarks>
        /// <b>The measurement the 179-degree question wants.</b>
        /// <c>Marching.AlongTheLine</c> gives every leg the front its direction
        /// implies, with no cap and no option to reverse - so an order to a
        /// place behind the regiment is an about-face, and [T2] recorded twelve
        /// in one game at 121 to 179 degrees with wheeling taking 54% of the
        /// whole recording.
        /// <para>
        /// The movement model already prices the alternative:
        /// <c>MovementSystem.AlignmentPenalty</c> is 1,00 on the line of march,
        /// <b>0,40 at a right angle and 0,20 fully reversed</b>. So a regiment
        /// <i>can</i> walk backwards; nothing ever asks it to. This counts how
        /// often that matters and what each way costs.
        /// </para>
        /// </remarks>
        [Fact(Skip = "A record of a measurement rather than a check on one - it is what the " +
                     "opening-wheel question was decided against, and it plans every order on " +
                     "every bench field.")]
        public void HowFarRoundEveryOrderHasToWheel()
        {
            _out.WriteLine(
                $"{"field",-16}{"orders",8}{"0-30",7}{"30-60",7}{"60-90",7}{"90-120",8}" +
                $"{"120-150",9}{"150-180",9}{"mean",8}{"over 90",9}");
            _out.WriteLine(new string('-', 90));

            var overNinety = new List<(string, float, float, float, float, float, float)>();

            foreach (string field in Fields)
            {
                BattleState battle = BenchScenariosTests.Load(field);
                IPathfinder pathfinder = new DirectPathfinder(
                    battle.Terrain, new TerrainMovementModel(TestContent.Terrain), TestContent.Terrain);

                var bucket = new int[6];
                float total = 0f;
                int orders = 0;

                foreach (UnitInstance unit in battle.UnitsOnField())
                {
                    Vec2 to = BenchScenariosTests.OrderFor(battle, unit);
                    Plan plan = Marching.PlanTo(battle, unit, pathfinder, to);

                    IReadOnlyList<Vec2> points = plan.Path.Waypoints;
                    if (!plan.Path.Found || points.Count < 2) continue;

                    Facing first = plan.Hold != null && 1 < plan.Hold.Length && plan.Hold[1].HasValue
                        ? plan.Hold[1]!.Value
                        : Marching.AlongTheLine(points[0], points[1], unit.Facing);

                    float wheel = Facing.AbsoluteDelta(unit.Facing, first) * 180f / MathF.PI;

                    orders++;
                    total += wheel;
                    bucket[Math.Min(5, (int)(wheel / 30f))]++;

                    if (wheel <= 90f) continue;

                    // What the two answers cost on this leg: wheel onto it and
                    // walk, against holding the front in hand and walking off
                    // the line of march at the penalty that implies.
                    float legMetres = Vec2.Distance(points[0], points[1]);
                    float wholeRoute = GridRoutePlanner.Length(points);

                    // And the front the whole march is going in, as against the
                    // one the first leg implies - which is the designer's rule.
                    Facing overall = Marching.AlongTheLine(points[0], points[points.Count - 1], unit.Facing);
                    float toOverall = Facing.AbsoluteDelta(unit.Facing, overall) * 180f / MathF.PI;

                    float turnSeconds = wheel / MathF.Max(0.1f, unit.Def.Get(UnitAttributes.TurnRate));
                    float pace = MathF.Max(0.1f, unit.Def.Speed);

                    float wheeling = turnSeconds + legMetres / pace;
                    float holding = legMetres / (pace * MovementSystem.AlignmentPenalty(wheel));

                    overNinety.Add((field, wheel, legMetres, wheeling, holding, wholeRoute, toOverall));
                }

                _out.WriteLine(
                    $"{field,-16}{orders,8}{bucket[0],7}{bucket[1],7}{bucket[2],7}{bucket[3],8}" +
                    $"{bucket[4],9}{bucket[5],9}{total / Math.Max(1, orders),8:0.0}" +
                    $"{bucket[3] + bucket[4] + bucket[5],9}");
            }

            _out.WriteLine(string.Empty);
            _out.WriteLine("every opening wheel over 90 degrees, and what each way of starting costs");
            _out.WriteLine(
                $"{"field",-16}{"wheel",7}{"leg m",8}{"route m",9}{"leg/route",11}" +
                $"{"to overall",12}{"wheel+walk",12}{"hold front",12}{"cheaper",9}");
            _out.WriteLine(new string('-', 96));

            int holdWins = 0;
            int firstLegTiny = 0;

            foreach ((string field, float wheel, float leg, float wheeling, float holding,
                      float route, float overall) in overNinety)
            {
                bool hold = holding < wheeling;
                if (hold) holdWins++;

                float share = leg / MathF.Max(1f, route);
                if (share < 0.1f) firstLegTiny++;

                _out.WriteLine(
                    $"{field,-16}{wheel,7:0}{leg,8:0}{route,9:0}{share,11:0.00}{overall,12:0}" +
                    $"{wheeling,12:0.0}{holding,12:0.0}{(hold ? "hold" : "wheel"),9}");
            }

            _out.WriteLine(string.Empty);
            _out.WriteLine(
                $"{overNinety.Count} opening wheels over 90 degrees; holding the front would be " +
                $"cheaper on {holdWins}; the first leg is under a tenth of the route on {firstLegTiny}.");
        }

        /// <summary>
        /// What taking the front from the march rather than the first waypoint
        /// changes: every route, every opening wheel, and the bill.
        /// </summary>
        /// <remarks>
        /// <b>M99, measured both ways in one process.</b> The claim that has to
        /// be checked is not that the wheel comes down - that is arithmetic -
        /// but that <b>no waypoint moves</b>. The front is an argument to
        /// <c>Marching.IsClearLine</c>, so a pass that touched fronts could
        /// quietly change which routes exist, and that is exactly what the
        /// rejected 90-degree cap would have done.
        /// </remarks>
        [Fact(Skip = "A record of a measurement rather than a check on one - it is the sweep " +
                     "M99 chose its threshold from, and it plans every order on every bench " +
                     "field eleven times over.")]
        public void WhatTakingTheFrontFromTheMarchChanges()
        {
            bool was = RouteFronts.FrontFromTheMarch;
            float bodies = RouteFronts.StubLegBodies;

            try
            {
                _out.WriteLine(
                    $"{"field",-15}{"stub is",12}{"metres",8}{"fronts",8}{"over90",8}" +
                    $"{"turned",9}{"vs off",8}{"march s",10}{"vs off",8}{"crabbed m",11}");
                _out.WriteLine(new string('-', 97));

                float[] sweepBodies = { 0f, 1f, 2f, 4f, 8f, 1000f };

                foreach (string field in Fields)
                {
                    float turnedOff = 0f, secondsOff = 0f;

                    foreach (float many in sweepBodies)
                    {
                        RouteFronts.FrontFromTheMarch = 0f < many;
                        RouteFronts.StubLegBodies = many;

                        List<(IReadOnlyList<Vec2> points, float wheel, float route,
                              float turned, float seconds, float crabbed)> orders = Opening(field);

                        int moved = 0, ninety = 0;
                        float turned = 0f, seconds = 0f, crabbed = 0f, stubMetres = 0f;

                        foreach ((IReadOnlyList<Vec2> points, float wheel, float route,
                                  float legTurned, float legSeconds, float legCrabbed) in orders)
                        {
                            turned += legTurned;
                            seconds += legSeconds;
                            crabbed += legCrabbed;

                            if (90f < wheel) ninety++;

                            stubMetres = MathF.Max(stubMetres, 0f);
                        }

                        if (many <= 0f)
                        {
                            turnedOff = turned;
                            secondsOff = seconds;
                        }

                        // How long a leg this setting actually calls a stub, on
                        // the deepest body on the field - the reader wants
                        // metres, not multiples.
                        float deepest = 0f;
                        foreach (UnitInstance u in BenchScenariosTests.Load(field).UnitsOnField())
                            deepest = MathF.Max(deepest, u.Footprint.Depth);

                        stubMetres = many >= 1000f ? -1f : deepest * many;

                        moved = FrontsThatMoved(field, many);

                        _out.WriteLine(
                            $"{field,-15}" +
                            $"{(many <= 0f ? "off" : many >= 1000f ? "a tenth of route" : $"{many:0} body"),12}" +
                            $"{(stubMetres < 0f ? "" : $"{stubMetres:0}"),8}{moved,8}{ninety,8}" +
                            $"{turned,9:0}{(turned - turnedOff) / MathF.Max(1f, turnedOff),8:+0.0%;-0.0%;0.0%}" +
                            $"{seconds,10:0}" +
                            $"{(seconds - secondsOff) / MathF.Max(1f, secondsOff),8:+0.0%;-0.0%;0.0%}" +
                            $"{crabbed,11:0}");
                    }

                    _out.WriteLine(string.Empty);
                }
            }
            finally
            {
                RouteFronts.FrontFromTheMarch = was;
                RouteFronts.StubLegBodies = bodies;
            }
        }

        /// <summary>
        /// How many opening fronts this setting moves against the pass off.
        /// </summary>
        private static int FrontsThatMoved(string field, float many)
        {
            if (many <= 0f) return 0;

            RouteFronts.FrontFromTheMarch = false;
            List<(IReadOnlyList<Vec2> points, float wheel, float route,
                  float turned, float seconds, float crabbed)> off = Opening(field);

            RouteFronts.FrontFromTheMarch = true;
            RouteFronts.StubLegBodies = many;
            List<(IReadOnlyList<Vec2> points, float wheel, float route,
                  float turned, float seconds, float crabbed)> on = Opening(field);

            int moved = 0;

            for (int i = 0; i < off.Count && i < on.Count; i++)
            {
                Assert.Equal(off[i].points.Count, on[i].points.Count);

                if (0.01f < MathF.Abs(off[i].wheel - on[i].wheel)) moved++;
            }

            return moved;
        }

        /// <summary>Every order on a field, with the front it opens on.</summary>
        private static List<(IReadOnlyList<Vec2> points, float wheel, float route,
                             float turned, float seconds, float crabbed)>
            Opening(string field)
        {
            BattleState battle = BenchScenariosTests.Load(field);
            IPathfinder pathfinder = new DirectPathfinder(
                battle.Terrain, new TerrainMovementModel(TestContent.Terrain), TestContent.Terrain);

            var opened = new List<(IReadOnlyList<Vec2>, float, float, float, float, float)>();

            foreach (UnitInstance unit in battle.UnitsOnField())
            {
                Vec2 to = BenchScenariosTests.OrderFor(battle, unit);
                Plan plan = Marching.PlanTo(battle, unit, pathfinder, to);

                IReadOnlyList<Vec2> points = plan.Path.Waypoints;
                if (!plan.Path.Found || points.Count < 2) continue;

                Facing first = plan.Hold != null && 1 < plan.Hold.Length && plan.Hold[1].HasValue
                    ? plan.Hold[1]!.Value
                    : Marching.AlongTheLine(points[0], points[1], unit.Facing);

                // Every degree the regiment turns walking this route, opening
                // wheel included, and what the walk costs at those fronts.
                float turned = 0f;
                float crabbed = 0f;
                Facing on = unit.Facing;

                for (int leg = 1; leg < points.Count; leg++)
                {
                    Facing front = plan.Hold != null && leg < plan.Hold.Length && plan.Hold[leg].HasValue
                        ? plan.Hold[leg]!.Value
                        : Marching.AlongTheLine(points[leg - 1], points[leg], on);

                    turned += Facing.AbsoluteDelta(on, front) * 180f / MathF.PI;
                    on = front;

                    // Ground covered with the front well off the way it is
                    // going. This is the column the eye reads: a regiment
                    // sliding sideways is what the designer called weird, and
                    // it is the price of not wheeling.
                    Facing along = Marching.AlongTheLine(points[leg - 1], points[leg], front);

                    if (45f < Facing.AbsoluteDelta(front, along) * 180f / MathF.PI)
                        crabbed += Vec2.Distance(points[leg - 1], points[leg]);
                }

                opened.Add((points, Facing.AbsoluteDelta(unit.Facing, first) * 180f / MathF.PI,
                            GridRoutePlanner.Length(points), turned,
                            Marching.SecondsToWalk(battle, unit, points, plan.Hold), crabbed));
            }

            return opened;
        }

        private static bool SamePoints(IReadOnlyList<Vec2> a, IReadOnlyList<Vec2> b)
        {
            if (a.Count != b.Count) return false;

            for (int i = 0; i < a.Count; i++)
                if (0.01f < Vec2.Distance(a[i], b[i])) return false;

            return true;
        }

        /// <summary>What the M99 front pass costs on the clock it runs on.</summary>
        /// <remarks>
        /// Least of three uninstrumented passes each way, after a whole warm
        /// round that is thrown away - the pass adds one
        /// <c>Marching.IsClearLine</c> per stub leg, and <c>ClearLine</c> is
        /// 9,3% of the Crucible, so it is not obviously free.
        /// </remarks>
        [Fact(Skip = "A record of a measurement rather than a check on one - it is what M99 " +
                     "was costed against, and it orders every bench field eight times.")]
        public void WhatTheMarchFrontPassCosts()
        {
            bool was = RouteFronts.FrontFromTheMarch;

            try
            {
                // A discard round, warming both settings before a single figure
                // is written down. Without it the first row reads dear and the
                // table says the wrong thing about the first field in it.
                foreach (string field in Fields)
                {
                    foreach (bool on in new[] { false, true })
                    {
                        RouteFronts.FrontFromTheMarch = on;
                        BenchScenariosTests.OrderEverybody(BenchScenariosTests.Load(field), null);
                    }
                }

                _out.WriteLine($"{"field",-16}{"off ms",10}{"on ms",10}{"change",10}");
                _out.WriteLine(new string('-', 46));

                foreach (string field in Fields)
                {
                    RouteFronts.FrontFromTheMarch = false;
                    float off = Least(field, passes: 3);

                    RouteFronts.FrontFromTheMarch = true;
                    float on = Least(field, passes: 3);

                    _out.WriteLine(
                        $"{field,-16}{off,10:0.00}{on,10:0.00}" +
                        $"{(on - off) / MathF.Max(0.001f, off),10:+0.0%;-0.0%;0.0%}");
                }
            }
            finally
            {
                RouteFronts.FrontFromTheMarch = was;
            }
        }

        /// <summary>The least of several uninstrumented passes over a field.</summary>
        private static float Least(string field, int passes)
        {
            float least = float.MaxValue;

            for (int pass = 0; pass < passes; pass++)
            {
                BattleState battle = BenchScenariosTests.Load(field);

                var watch = Stopwatch.StartNew();
                BenchScenariosTests.OrderEverybody(battle, null);
                watch.Stop();

                least = MathF.Min(least, (float)watch.Elapsed.TotalMilliseconds);
            }

            return least;
        }

        private void Report(string field, IRoutePlanner? planner, string name)
        {
            Row row = Measure(field, planner, passes: 3);

            BattleState probed = BenchScenariosTests.Load(field);
            PlanningProfile.Start();

            var watch = Stopwatch.StartNew();
            BenchScenariosTests.OrderEverybody(probed, planner);
            watch.Stop();

            PlanningProfile.Stop();

            _out.WriteLine(string.Empty);
            _out.WriteLine($"--- {name} ---");
            _out.WriteLine(
                $"    {row.MsPerOrder,8:0.000} ms an order   {row.Total,9:0.0} ms for all " +
                $"{row.Orders}   worst single order {row.Worst,7:0.0} ms");
            _out.WriteLine(
                $"    {row.Routed} routed, {row.Refused} refused, {row.Pressed} pressed through, " +
                $"{row.Unwalkable} unwalkable   {row.Seconds:0} s of marching planned");
            _out.WriteLine($"    {row.Spread}");
            _out.WriteLine(
                $"    instrumented pass {watch.Elapsed.TotalMilliseconds,8:0.0} ms " +
                $"({(watch.Elapsed.TotalMilliseconds / Math.Max(0.001, row.Total)) - 1d,6:0.0%} over " +
                "the least) — the table describes that pass");
            _out.WriteLine(string.Empty);
            _out.WriteLine(PlanningProfile.Report(
                $"    where {row.Total:0} ms went — {name} on {field}"));
        }

        /// <summary>Uninstrumented passes, least of several, plus the quality counters.</summary>
        private static Row Measure(string field, IRoutePlanner? planner, int passes)
        {
            // Warm first. An unwarmed pass charges the whole cost of compiling
            // the planner to the first order it ever made.
            BenchScenariosTests.OrderEverybody(BenchScenariosTests.Load(field), planner);

            StagedRoutePlanner.ResetCounters();

            List<double> runs = BenchScenariosTests.Passes(
                field, planner, out var tally, fewest: passes, most: passes);

            var sorted = new List<double>(runs);
            sorted.Sort();

            // Quality is asked of one more pass, because walking every route to
            // see whether it can be walked costs more than the planning did and
            // has no business inside a timed pass.
            BattleState judged = BenchScenariosTests.Load(field);
            IPathfinder pathfinder = new DirectPathfinder(
                judged.Terrain, new TerrainMovementModel(TestContent.Terrain), TestContent.Terrain);

            int unwalkable = 0;
            double seconds = 0d;

            foreach (UnitInstance unit in judged.UnitsOnField())
            {
                Plan plan = Marching.PlanTo(
                    judged, unit, pathfinder,
                    BenchScenariosTests.OrderFor(judged, unit), planner: planner);

                if (plan.Path.Found)
                {
                    // Priced by the same model the walk uses, not by the path's
                    // own distance — W5, and the difference is the whole point
                    // of M22: a wheel costs pace, which a length cannot show.
                    seconds += Marching.SecondsToWalk(
                        judged, unit, plan.Path.Waypoints, plan.Hold);

                    if (!StagedRoutePlanner.WalksCleanly(judged, unit, plan)) unwalkable++;
                }
            }

            return new Row
            {
                Orders = tally.Orders,
                Routed = tally.Found,
                Refused = tally.Failed,
                Pressed = tally.Pressed,
                Unwalkable = unwalkable,
                Seconds = seconds,
                Total = sorted[0],
                Worst = tally.SlowestOrderMs,
                MsPerOrder = sorted[0] / Math.Max(1, tally.Orders),
                Spread = BenchScenariosTests.Spread(runs),
            };
        }

        private sealed class Row
        {
            public int Orders, Routed, Refused, Pressed, Unwalkable;
            public double Seconds, Total, Worst, MsPerOrder;
            public string Spread = string.Empty;
        }

        // ------------------------------------------------------------- the levers

        /// <summary>
        /// Every setting that can change what a plan costs, one at a time
        /// against the defaults.
        /// </summary>
        /// <remarks>
        /// One at a time on purpose. Combinations are what
        /// <c>LeverBenchTests</c> is for, and interactions found there were
        /// worth having; this table answers the prior question of which levers
        /// move the clock at all, which is the one that says what is worth
        /// combining.
        /// </remarks>
        private static IEnumerable<(string, Action)> Levers()
        {
            yield return ("defaults", () => { });

            // --- the cascade: which rungs are allowed to answer
            yield return ("no bent ladder", () => StagedRoutePlanner.AcceptBentLadder = false);
            yield return ("ask the tangent stage",
                () => StagedRoutePlanner.AskTangentStage = true);
            yield return ("ask corners", () => StagedRoutePlanner.AskCorners = true);
            yield return ("ask rings", () => StagedRoutePlanner.AskRings = true);
            yield return ("press before pose search",
                () => StagedRoutePlanner.PoseSearchBeforePressing = false);

            // --- the hex grid, which is the newest rung and the least swept
            yield return ("grid cells x0.25", () => RegimentGrid.SpacingMultiple = 0.25f);
            yield return ("grid cells x0.5", () => RegimentGrid.SpacingMultiple = 0.5f);
            yield return ("grid cells x2", () => RegimentGrid.SpacingMultiple = 2f);
            yield return ("grid corridor unbounded", () => RegimentGrid.CorridorFraction = 0f);
            yield return ("grid corridor x0.25", () => RegimentGrid.CorridorFraction = 0.25f);
            yield return ("grid corridor x1", () => RegimentGrid.CorridorFraction = 1f);
            yield return ("grid marked from scratch",
                () => RegimentGrid.MarkIncrementally = false);

            yield return ("grid budget 5k", () => RegimentGrid.CellBudget = 5_000);
            yield return ("grid budget 160k", () => RegimentGrid.CellBudget = 160_000);

            // --- the pose lattice, which M68 showed was most of the tail
            yield return ("lattice budget 1k", () => StagedRoutePlanner.PoseExpansionBudget = 1_024);
            yield return ("lattice budget 16k", () => StagedRoutePlanner.PoseExpansionBudget = 16_384);
            yield return ("corridor 45 m", () => StagedRoutePlanner.CorridorHalfWidthMetres = 45f);
            yield return ("corridor 90 m", () => StagedRoutePlanner.CorridorHalfWidthMetres = 90f);
            yield return ("corridor from cheap route",
                () => StagedRoutePlanner.CorridorFromCheapRoute = true);
            yield return ("bounded budget 1k", () => StagedRoutePlanner.BoundedBudget = 1_000);
            yield return ("bounded budget 16k", () => StagedRoutePlanner.BoundedBudget = 16_000);

            // --- the ceilings, which decide how often the dear rungs are reached
            yield return ("way-round ceiling 1.5", () => StagedRoutePlanner.WayRoundCostCeiling = 1.5f);
            yield return ("way-round ceiling 6", () => StagedRoutePlanner.WayRoundCostCeiling = 6f);
            yield return ("straight ceiling 2", () => StagedRoutePlanner.StraightLineCostCeiling = 2f);
            yield return ("straight ceiling 8", () => StagedRoutePlanner.StraightLineCostCeiling = 8f);
            yield return ("crab ceiling off", () => StagedRoutePlanner.CrabbedShareCeiling = 1f);
            yield return ("crab ceiling 0.5", () => StagedRoutePlanner.CrabbedShareCeiling = 0.5f);

            // --- the visibility graph the tangent search walks
            yield return ("most places 24", () => RouteSearch.MostPlaces = 24);
            yield return ("most places 96", () => RouteSearch.MostPlaces = 96);

            // --- the hybrid lattice's own dials. Zero means "the built-in
            //     default", so each of these is an override rather than a value.
            yield return ("hybrid 8 headings", () => HybridAStarPlanner.Headings = 8);
            yield return ("hybrid 32 headings", () => HybridAStarPlanner.Headings = 32);
            yield return ("hybrid bin 5 m", () => HybridAStarPlanner.PositionBin = 5f);
            yield return ("hybrid bin 20 m", () => HybridAStarPlanner.PositionBin = 20f);
            yield return ("hybrid weight 1 (optimal)", () => HybridAStarPlanner.Weight = 1f);
            yield return ("hybrid weight 4", () => HybridAStarPlanner.Weight = 4f);
            yield return ("hybrid shot every 1", () => HybridAStarPlanner.ShootEvery = 1);
            yield return ("hybrid shot every 16", () => HybridAStarPlanner.ShootEvery = 16);
            yield return ("hybrid plain heuristic",
                () => HybridAStarPlanner.TurnAwareHeuristic = false);
            yield return ("hybrid full primitives", () => HybridPrimitives.LeanPrimitives = false);
            yield return ("hybrid heap not dial", () => HybridTurnField.DialQueue = false);

            // --- the turn field's own grid, M84. It is 52 to 88% of the hybrid
            //     planner and had no dial on it at all. The fill settles
            //     columns * rows * 8 states, so this is quadratic.
            yield return ("turn cells 5 m floor", () => HybridTurnField.MinCellMetres = 5f);
            yield return ("turn cells 20 m floor", () => HybridTurnField.MinCellMetres = 20f);
            yield return ("turn cells 40 m floor", () => HybridTurnField.MinCellMetres = 40f);
            yield return ("turn 24 across", () => HybridTurnField.TargetCellsAcross = 24f);
            yield return ("turn 96 across", () => HybridTurnField.TargetCellsAcross = 96f);
            yield return ("turn 20 m floor + 24 across", () =>
            {
                HybridTurnField.MinCellMetres = 20f;
                HybridTurnField.TargetCellsAcross = 24f;
            });
        }

        /// <summary>
        /// Every lever's value as it was found, so one sweep cannot leak into
        /// the next.
        /// </summary>
        /// <remarks>
        /// Written out by hand rather than by reflection on purpose: a lever
        /// added later and not added here will show up as a sweep that does not
        /// come back to its defaults, which is a loud failure. Reflection would
        /// have made it a silent one.
        /// </remarks>
        private sealed class Defaults
        {
            private bool _bent, _corners, _rings, _poseFirst, _cheapCorridor, _turnAware, _lean, _dial;
            private bool _tangentStage;
            private float _turnCell, _turnAcross;
            private int _poseBudget, _bounded, _cellBudget, _places, _headings, _shootEvery;
            private float _spacing, _corridor, _wayRound, _straight, _crab, _bin, _weight;
            private float _gridCorridor;
            private bool _incremental;

            public static Defaults Capture() => new Defaults
            {
                _bent = StagedRoutePlanner.AcceptBentLadder,
                _tangentStage = StagedRoutePlanner.AskTangentStage,
                _corners = StagedRoutePlanner.AskCorners,
                _rings = StagedRoutePlanner.AskRings,
                _poseFirst = StagedRoutePlanner.PoseSearchBeforePressing,
                _cheapCorridor = StagedRoutePlanner.CorridorFromCheapRoute,
                _poseBudget = StagedRoutePlanner.PoseExpansionBudget,
                _bounded = StagedRoutePlanner.BoundedBudget,
                _corridor = StagedRoutePlanner.CorridorHalfWidthMetres,
                _wayRound = StagedRoutePlanner.WayRoundCostCeiling,
                _straight = StagedRoutePlanner.StraightLineCostCeiling,
                _crab = StagedRoutePlanner.CrabbedShareCeiling,
                _spacing = RegimentGrid.SpacingMultiple,
                _cellBudget = RegimentGrid.CellBudget,
                _gridCorridor = RegimentGrid.CorridorFraction,
                _incremental = RegimentGrid.MarkIncrementally,
                _places = RouteSearch.MostPlaces,
                _headings = HybridAStarPlanner.Headings,
                _bin = HybridAStarPlanner.PositionBin,
                _weight = HybridAStarPlanner.Weight,
                _shootEvery = HybridAStarPlanner.ShootEvery,
                _turnAware = HybridAStarPlanner.TurnAwareHeuristic,
                _lean = HybridPrimitives.LeanPrimitives,
                _dial = HybridTurnField.DialQueue,
                _turnCell = HybridTurnField.MinCellMetres,
                _turnAcross = HybridTurnField.TargetCellsAcross,
            };

            public void Restore()
            {
                StagedRoutePlanner.AcceptBentLadder = _bent;
                StagedRoutePlanner.AskTangentStage = _tangentStage;
                StagedRoutePlanner.AskCorners = _corners;
                StagedRoutePlanner.AskRings = _rings;
                StagedRoutePlanner.PoseSearchBeforePressing = _poseFirst;
                StagedRoutePlanner.CorridorFromCheapRoute = _cheapCorridor;
                StagedRoutePlanner.PoseExpansionBudget = _poseBudget;
                StagedRoutePlanner.BoundedBudget = _bounded;
                StagedRoutePlanner.CorridorHalfWidthMetres = _corridor;
                StagedRoutePlanner.WayRoundCostCeiling = _wayRound;
                StagedRoutePlanner.StraightLineCostCeiling = _straight;
                StagedRoutePlanner.CrabbedShareCeiling = _crab;
                RegimentGrid.SpacingMultiple = _spacing;
                RegimentGrid.CorridorFraction = _gridCorridor;
                RegimentGrid.MarkIncrementally = _incremental;
                RegimentGrid.CellBudget = _cellBudget;
                RouteSearch.MostPlaces = _places;
                HybridAStarPlanner.Headings = _headings;
                HybridAStarPlanner.PositionBin = _bin;
                HybridAStarPlanner.Weight = _weight;
                HybridAStarPlanner.ShootEvery = _shootEvery;
                HybridAStarPlanner.TurnAwareHeuristic = _turnAware;
                HybridPrimitives.LeanPrimitives = _lean;
                HybridTurnField.DialQueue = _dial;
                HybridTurnField.MinCellMetres = _turnCell;
                HybridTurnField.TargetCellsAcross = _turnAcross;
            }
        }
    }
}
