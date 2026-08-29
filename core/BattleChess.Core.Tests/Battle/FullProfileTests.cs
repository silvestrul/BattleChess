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
                     "it orders two bench fields thirty-two times over at four budgets.")]
        public void WhyATighterCapCostsMore()
        {
            float was = Marching.SearchBudgetMs;

            string[] fields = { "crucible", "brokencountry" };

            try
            {
                foreach (string warm in fields) Measure(warm, planner: null, passes: 1);

                _out.WriteLine(
                    $"{"cap",-8}{"field",-16}{"worst ms",10}{"ms/order",10}" +
                    $"{"spent",8}{"escaped",9}{"coarse won",12}{"fine asked",12}" +
                    $"{"fine won",10}{"pose asked",12}");
                _out.WriteLine(new string('-', 99));

                foreach (float cap in new[] { 0f, 20f, 10f, 5f })
                {
                    Marching.SearchBudgetMs = cap;

                    foreach (string field in fields)
                    {
                        Row row = Measure(field, planner: null, passes: 3);

                        _out.WriteLine(
                            $"{(cap <= 0f ? "off" : $"{cap:0} ms"),-8}{field,-16}" +
                            $"{row.Worst,10:0.0}{row.MsPerOrder,10:0.000}" +
                            $"{StagedRoutePlanner.OutOfTimeReachedTheGrid,8}" +
                            $"{StagedRoutePlanner.OutOfTimeAtTheGrid,9}" +
                            $"{GridRoutePlanner.Found,12}" +
                            $"{GridRoutePlanner.FineAsked,12}" +
                            $"{GridRoutePlanner.FineFound,10}" +
                            $"{StagedRoutePlanner.PoseAsked,12}");
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
