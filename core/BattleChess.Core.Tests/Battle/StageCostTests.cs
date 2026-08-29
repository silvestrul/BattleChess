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

        /// <summary>
        /// How many regiments are moved between one order and the next, so that
        /// the field the next order asks for is out of date.
        /// </summary>
        /// <remarks>
        /// Twelve of eighty, which is roughly what a wing under orders looks
        /// like. The number matters less than that it is neither nought - a
        /// bench, where the cache always hits - nor all of them, which is what
        /// throwing the field away amounts to and is what the old mode measured.
        /// </remarks>
        private static int _shove;

        /// <summary>The cascade, stage by stage, and then its worst order alone.</summary>
        [Fact(Skip = "A record of a measurement rather than a check on one - it orders every " +
                     "bench field twenty times over and profiles eight of those passes.")]
        public void WhereEveryStageGoes()
        {
            float was = Marching.SearchBudgetMs;
            bool wasReuse = RegimentGrid.Reuse;
            bool wasIncremental = RegimentGrid.MarkIncrementally;

            // Off, or the report describes a ceiling rather than the planner.
            Marching.SearchBudgetMs = 0f;

            try
            {
                // Four modes, because no single one of them is a battle and
                // the differences between them are the whole finding.
                //
                // A kept field is found again only while nothing has moved, and
                // on a bench nothing ever does: the same arrangement is loaded
                // for every pass, so the stamp never changes, every call after
                // the first is a cache hit and FieldMark is never entered at
                // all. Read on its own that table says raising the field costs
                // 120 us, which is the price of *not* raising it.
                //
                // Reuse off was the stand-in for a battle and it is the wrong
                // stand-in now: it says "throw the field away every order",
                // which is the thing being removed, not "a dozen regiments
                // marched". So the last two modes shove part of the army
                // between orders and differ only in whether the field is
                // patched or raised again - same arrangements, same orders,
                // same routes, one lever apart.
                foreach ((string title, bool reuse, bool incremental, int shove) in new[]
                {
                    ("fields kept between orders — a bench, where nothing moves", true, true, 0),
                    ("fields rebuilt every order — a battle, where everything does", false, true, 0),
                    ("a battle: twelve regiments move between orders, the field patched", true, true, 12),
                    ("a battle: twelve regiments move between orders, the field raised again", true, false, 12),
                })
                {
                    RegimentGrid.Reuse = reuse;
                    RegimentGrid.MarkIncrementally = incremental;
                    _shove = shove;

                    _out.WriteLine(string.Empty);
                    _out.WriteLine($"######## {title} ########");

                    foreach (string field in Fields) Diagnose(field);
                }
            }
            finally
            {
                Marching.SearchBudgetMs = was;
                RegimentGrid.Reuse = wasReuse;
                RegimentGrid.MarkIncrementally = wasIncremental;
                _shove = 0;
                RegimentGrid.Forget();
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

            // Bytes as well as milliseconds, because they are different
            // questions and only one of them survives the noise on this
            // machine. A managed allocation is nearly free to make and is paid
            // for later, all at once, by whichever frame the collector happens
            // to land on - so litter is what turns a planner that averages four
            // milliseconds into a game that hitches. And the editor runs Mono,
            // where it is dearer than it is here.
            long litterBefore = GC.GetAllocatedBytesForCurrentThread();
            Time(field, only: null, out _, out _);
            long litter = GC.GetAllocatedBytesForCurrentThread() - litterBefore;

            _out.WriteLine(string.Empty);
            _out.WriteLine($"=== {field}: {orders} orders ===");
            _out.WriteLine(
                $"    {litter / 1024d / 1024d,7:0.00} MB allocated     " +
                $"{litter / 1024d / Math.Max(1, orders),7:0.0} kB an order");
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

            // Every field starts from nothing, or the first order of a pass
            // inherits whatever the pass before it left cached and the mode
            // being measured is not the mode that ran.
            RegimentGrid.Forget();

            var army = new List<UnitInstance>();
            foreach (UnitInstance unit in battle.UnitsOnField()) army.Add(unit);

            int shoved = 0;

            long began = Stopwatch.GetTimestamp();

            foreach (UnitInstance unit in battle.UnitsOnField())
            {
                if (only.HasValue && unit.Id != only.Value) continue;

                // Outside the order's own clock below, but inside the pass
                // total, which is right: moving is the battle's business and
                // not the planner's, and what it costs the planner is the
                // patching it forces, which is inside.
                for (int i = 0; i < _shove && army.Count > 0; i++)
                {
                    UnitInstance moved = army[shoved++ % army.Count];

                    moved.Position = new Vec2(moved.Position.X + 1.3f, moved.Position.Y + 0.7f);
                    moved.Facing = Facing.FromDegrees(moved.Facing.Degrees + 0.9f);
                }

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
