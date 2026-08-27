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
        [Fact(Skip = "A record of a measurement rather than a check on one. It is the one that says the tangent stage answers nothing from either side of the grid - M86.")]
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

        // ------------------------------------------------------------------ the work

        /// <summary>
        /// One planner on one field: warm, several uninstrumented passes for the
        /// headline, then one instrumented pass for the table.
        /// </summary>
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
