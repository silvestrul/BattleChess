using System;
using System.Collections.Generic;
using System.Diagnostics;
using BattleChess.Contracts;
using BattleChess.Rules;
using BattleChess.Rules.GridPlanning;
using Xunit;
using Xunit.Abstractions;

namespace BattleChess.Tests.Battle
{
    /// <summary>
    /// What each stage of the cascade costs, and what the dearest single order
    /// spent it on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why this exists beside the bench that already reports steps.</b> The
    /// bench answers "where did the field's time go", which is a question about
    /// the median. Every complaint about this planner has been about the tail:
    /// in a played session the median order was 1,5 ms and the worst 287, and
    /// within one wing of thirteen the spread was 2 868 to one. A breakdown
    /// averaged over eighty orders describes the seventy-nine that were already
    /// fine.
    /// </para>
    /// <para>
    /// So this reports the cascade stage by stage <i>and then profiles the
    /// dearest order on its own</i> - the same instrument turned on one
    /// regiment, which is the only way to see which stage a pathological
    /// arrangement actually fell into.
    /// </para>
    /// <para>
    /// The stages are inclusive wrappers and they nest, so their percentages do
    /// not sum to a hundred and are not meant to: <c>Rung1</c> is inside
    /// <c>Ladder</c>, <c>GridField</c> and <c>GridSearch</c> are inside both
    /// <c>GridCoarse</c> and <c>GridFine</c>. Read the inclusive column down
    /// the cascade in order, not across.
    /// </para>
    /// </remarks>
    [Collection("PlannerLevers")]
    public sealed class StageCostTests
    {
        private readonly ITestOutputHelper _out;

        public StageCostTests(ITestOutputHelper outp) => _out = outp;

        private static readonly string[] Fields =
            { "crucible", "brokencountry", "longmarch", "greatfield", "sidewaysmile" };

        /// <summary>The cascade, stage by stage, and then its worst order alone.</summary>
        [Fact(Skip = "A record of a measurement rather than a check on one - it orders every " +
                     "bench field ten times over and profiles four of those passes.")]
        public void WhereEveryStageGoes()
        {
            float was = Marching.SearchBudgetMs;

            // Off, or the report describes a ceiling rather than the planner.
            Marching.SearchBudgetMs = 0f;

            try
            {
                // Both, because the difference between them is the finding.
                //
                // A kept field is found again only while nothing has moved, and
                // on a bench nothing ever does: the same arrangement is loaded
                // for every pass, so the stamp never changes, every call after
                // the first is a cache hit and FieldMark is never entered at
                // all. Read on its own that table says raising the field costs
                // 120 us, which is the price of *not* raising it.
                //
                // A played battle moves regiments every tick, so the stamp
                // changes and the field is built again. Reuse off is that, and
                // there FieldMark is the largest single step on the board.
                foreach (bool reuse in new[] { true, false })
                {
                    RegimentGrid.Reuse = reuse;

                    _out.WriteLine(string.Empty);
                    _out.WriteLine(reuse
                        ? "######## fields kept between orders — a bench, where nothing moves ########"
                        : "######## fields rebuilt every order — a battle, where everything does ########");

                    foreach (string field in Fields) Diagnose(field);
                }
            }
            finally
            {
                Marching.SearchBudgetMs = was;
                RegimentGrid.Reuse = true;
            }
        }

        private void Diagnose(string field)
        {
            // Warm. Every row of this used to be a single draw.
            Time(field, only: null, out _, out _);

            // Uninstrumented, so the mean and the tail are the planner's own
            // and not the profiler's. Least of three, because the dearest order
            // moves several milliseconds between runs on the same arrangement.
            double total = double.MaxValue;
            UnitId dearest = default;
            double worst = 0d;
            int orders = 0;

            for (int pass = 0; pass < 3; pass++)
            {
                double ms = Time(field, only: null, out UnitId slowest, out double slowestMs);

                if (ms >= total) continue;

                total = ms;
                dearest = slowest;
                worst = slowestMs;
                orders = Orders(field);
            }

            _out.WriteLine(string.Empty);
            _out.WriteLine($"=== {field}: {orders} orders ===");
            _out.WriteLine(
                $"    {total,7:0.0} ms total     {total / Math.Max(1, orders),6:0.00} ms an order     " +
                $"dearest {worst,6:0.0} ms  ({dearest})     " +
                $"the tail is {worst / Math.Max(0.001, total / Math.Max(1, orders)),4:0}x the mean");
            _out.WriteLine(string.Empty);

            PlanningProfile.Start();
            Time(field, only: null, out _, out _);
            PlanningProfile.Stop();

            _out.WriteLine(PlanningProfile.Report("every order on the field"));

            PlanningProfile.Start();
            Time(field, only: dearest, out _, out _);
            PlanningProfile.Stop();

            _out.WriteLine(PlanningProfile.Report($"the dearest order alone ({dearest}, {worst:0.0} ms)"));
        }

        private static int Orders(string field)
        {
            int orders = 0;
            foreach (UnitInstance _ in BenchScenariosTests.Load(field).UnitsOnField()) orders++;
            return orders;
        }

        /// <summary>
        /// Plans every order on a field, or just one, and says which cost most.
        /// </summary>
        private static double Time(
            string field, UnitId? only, out UnitId slowest, out double slowestMs)
        {
            BattleState battle = BenchScenariosTests.Load(field);

            IPathfinder pathfinder = new DirectPathfinder(
                battle.Terrain, new TerrainMovementModel(TestContent.Terrain), TestContent.Terrain);

            slowest = default;
            slowestMs = 0d;

            long began = Stopwatch.GetTimestamp();

            foreach (UnitInstance unit in battle.UnitsOnField())
            {
                if (only.HasValue && unit.Id != only.Value) continue;

                long at = Stopwatch.GetTimestamp();

                Marching.PlanTo(battle, unit, pathfinder, BenchScenariosTests.OrderFor(battle, unit));

                double spent = (Stopwatch.GetTimestamp() - at) * 1000d / Stopwatch.Frequency;

                if (spent > slowestMs)
                {
                    slowestMs = spent;
                    slowest = unit.Id;
                }
            }

            return (Stopwatch.GetTimestamp() - began) * 1000d / Stopwatch.Frequency;
        }
    }
}
