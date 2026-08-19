using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using BattleChess.Contracts;
using BattleChess.Rules;
using Xunit;
using Xunit.Abstractions;

namespace BattleChess.Tests.Battle
{
    /// <summary>
    /// Forty regiments a side, on three fields built to be expensive in three
    /// different ways, with every step of a plan timed separately.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why three.</b> One scenario tells you a number; three tell you what
    /// the number is made of. They field the identical order of battle so that
    /// the army is never the variable:
    /// </para>
    /// <list type="bullet">
    /// <item><b>The Crucible</b> — 775 m apart on 1.4 x 1.8 km. Other regiments
    /// are the expensive thing.</item>
    /// <item><b>The Long March</b> — 2 370 m apart on 3 x 2.3 km. Distance is
    /// the expensive thing.</item>
    /// <item><b>Broken Country</b> — 1 325 m apart on ground with sixteen
    /// patches of wood, hill and marsh and two winding streams. The going
    /// underfoot is the expensive thing.</item>
    /// </list>
    /// <para>
    /// <b>The orders are deliberately awkward.</b> A general advance is the
    /// cheap case: every regiment walks away from its neighbours and nothing
    /// obstructs. These orders send each regiment diagonally across the field to
    /// the far flank, so eighty marches cross each other and every one of them
    /// has to be planned around the rest. That is the case a player produces by
    /// box-selecting an army and clicking once, and it is the case that was
    /// measured at 1 652 ms a frame.
    /// </para>
    /// </remarks>
    public sealed class BenchScenariosTests
    {
        private readonly ITestOutputHelper _out;
        public BenchScenariosTests(ITestOutputHelper output) => _out = output;

        /// <summary>The three fields, and what each one is built to make dear.</summary>
        public static IEnumerable<object[]> Scenarios => new[]
        {
            new object[] { "crucible", "the crowd" },
            new object[] { "longmarch", "the distance" },
            new object[] { "brokencountry", "the ground" },
        };

        private static BattleState Load(string key)
        {
            string root = TestContent.Root;
            ITerrainCatalogue terrain = TestContent.Terrain;

            BattleMapDefinition map = AsciiMapReader.Read(
                File.ReadAllText(Path.Combine(root, "maps", key + ".map.txt")), terrain);

            BattleSetup setup = BattleSetup.Parse(
                File.ReadAllText(Path.Combine(root, "battles", key + ".battle.txt")));

            return setup.Build(map, terrain, TestContent.Units, TestContent.Formations,
                new TerrainMovementModel(terrain));
        }

        /// <summary>
        /// Where each regiment is sent: diagonally across to the far flank, so
        /// that the eighty marches cross rather than run parallel.
        /// </summary>
        private static Vec2 OrderFor(BattleState battle, UnitInstance unit)
        {
            MapBounds bounds = battle.Terrain.Bounds;

            float middle = (bounds.Min.X + bounds.Max.X) * 0.5f;

            // Across to the other side of the field, and to the opposite end of
            // it north-south, which is what makes the marches interleave.
            float x = unit.Position.X < middle
                ? bounds.Min.X + (bounds.Max.X - bounds.Min.X) * 0.72f
                : bounds.Min.X + (bounds.Max.X - bounds.Min.X) * 0.28f;

            float y = bounds.Min.Y + bounds.Max.Y - unit.Position.Y;

            return new Vec2(x, y);
        }

        // ------------------------------------------------------------ the gate

        /// <summary>
        /// A scenario nobody can deploy in measures nothing, so this is asked
        /// first and separately.
        /// </summary>
        [Theory]
        [MemberData(nameof(Scenarios))]
        public void EveryRegimentDeploysOnGroundItCanHold(string key, string makesDear)
        {
            BattleState battle = Load(key);

            var all = new List<UnitInstance>(battle.UnitsOnField());
            var wrong = new List<string>();

            foreach (UnitInstance unit in all)
            {
                if (!battle.FormationFits(unit, unit.Position, unit.Facing))
                {
                    wrong.Add(
                        $"{unit.Def.DisplayName} at ({unit.Position.X:0},{unit.Position.Y:0}) " +
                        "cannot stand where it is deployed");
                }
            }

            for (int i = 0; i < all.Count; i++)
            for (int j = i + 1; j < all.Count; j++)
            {
                if (!OrientedRect.Overlaps(all[i].Shape, all[j].Shape)) continue;

                wrong.Add(
                    $"{all[i].Def.DisplayName} at ({all[i].Position.X:0},{all[i].Position.Y:0}) " +
                    $"overlaps {all[j].Def.DisplayName} at ({all[j].Position.X:0},{all[j].Position.Y:0})");
            }

            foreach (string line in wrong) _out.WriteLine(line);

            _out.WriteLine(
                $"{key}: {all.Count} regiments, {makesDear} is the dear part, {wrong.Count} problems");

            Assert.Equal(80, all.Count);
            Assert.True(wrong.Count == 0, $"{key}: {wrong.Count} regiments are badly deployed.");
        }

        // ----------------------------------------------------------- the bench

        /// <summary>
        /// Eighty orders given at once, with every major step of every plan
        /// timed and counted.
        /// </summary>
        /// <remarks>
        /// Warmed first and then measured, because an unwarmed pass charges the
        /// whole cost of compiling the planner to the first order it ever made
        /// and the number wandered by a fifth run to run.
        /// </remarks>
        [Theory]
        [MemberData(nameof(Scenarios))]
        public void WhatEightyOrdersAtOnceCost(string key, string makesDear)
        {
            // Warm, unmeasured.
            OrderEverybody(Load(key), null);

            // Three passes with the instrumentation off, median reported. One
            // pass on this machine has wandered by a fifth, which was enough to
            // make the probes look as though they ran faster than no probes at
            // all — a negative overhead, which is only ever noise wearing a
            // number.
            var runs = new List<double>();

            Tally bare = default;

            for (int pass = 0; pass < 3; pass++)
            {
                BattleState plain = Load(key);

                var watch = Stopwatch.StartNew();
                bare = OrderEverybody(plain, null);
                watch.Stop();

                runs.Add(watch.Elapsed.TotalMilliseconds);
            }

            runs.Sort();

            double bareMilliseconds = runs[1];

            // And again with it on, so the report has something to describe.
            BattleState probed = Load(key);
            PlanningProfile.Start();

            var probedWatch = Stopwatch.StartNew();
            Tally measured = OrderEverybody(probed, null);
            probedWatch.Stop();

            PlanningProfile.Stop();

            _out.WriteLine(
                $"=== {key}: forty a side, eighty orders at once, {makesDear} is what this field makes dear ===");
            _out.WriteLine(string.Empty);
            _out.WriteLine(
                $"{measured.Orders} orders   {bareMilliseconds,9:0.0} ms total   " +
                $"{bareMilliseconds / Math.Max(1, measured.Orders),7:0.00} ms an order   " +
                $"{measured.Found} routed, {measured.Failed} refused, {measured.Pressed} pressed through");
            _out.WriteLine(
                $"three bare passes {runs[0],8:0.0} / {runs[1],8:0.0} / {runs[2],8:0.0} ms" +
                $"   spread {(runs[2] - runs[0]) / Math.Max(0.001, runs[0]),6:0.0%}");
            _out.WriteLine(
                $"instrumented pass {probedWatch.Elapsed.TotalMilliseconds,9:0.0} ms " +
                $"({(probedWatch.Elapsed.TotalMilliseconds / Math.Max(0.001, bareMilliseconds)) - 1d,6:0.0%} on the median) " +
                "— the table below describes that pass, the headline above is the uninstrumented median");
            _out.WriteLine(string.Empty);
            _out.WriteLine(PlanningProfile.Report($"where the {bareMilliseconds:0} ms went"));

            // The counters the planner already kept, which say how much work was
            // done where the clock says how long it took.
            _out.WriteLine(
                $"places {measured.Places:N0}   legs priced {measured.Legs:N0}   " +
                $"states {measured.States:N0}   expanded {measured.Expanded:N0}   " +
                $"bodies pulled in {measured.Bodies:N0}");

            // Attribution for StandCheck. Stands() memoises on (place, front-bin)
            // and counts only its misses; FrontsFor calls CanStandHere straight,
            // past the memo. If the two are close the memo is absorbing the
            // volume and the scan itself is the lever; if invocations run far
            // ahead of misses, the memo is being bypassed and that is the lever
            // instead.
            long standInvocations = PlanningProfile.CallsTo(PlanningProfile.Step.StandCheck);
            _out.WriteLine(
                $"stand checks: {standInvocations:N0} invocations   " +
                $"{measured.StandChecks:N0} memo misses   " +
                $"{standInvocations - measured.StandChecks:N0} past the memo   " +
                $"(SmoothRoute, the only caller that bypasses it, " +
                $"{PlanningProfile.InclusiveMilliseconds(PlanningProfile.Step.SmoothRoute):0.0} ms inclusive)");

            Assert.Equal(80, measured.Orders);

            // A bench that stopped routing would still produce a tidy table, and
            // the table would be measuring failure rather than planning.
            Assert.True(measured.Found >= 60,
                $"{key}: only {measured.Found} of 80 orders produced a route — this is measuring refusals.");

            // Both runs must do the same work, or the breakdown describes
            // something other than the headline.
            Assert.Equal(bare.Found, measured.Found);
            Assert.Equal(bare.Legs, measured.Legs);
        }

        /// <summary>
        /// The same eighty orders, put to every planner in turn.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The breakdown above is of <see cref="RoutePlanners.Default"/> and of
        /// nothing else, because <see cref="Marching.PlanTo"/> with no planner
        /// named uses it — which is also what both of <c>OrderSystem</c>'s
        /// re-plan sites do, so it is the only planner a played battle can
        /// reach. That makes it the right default to profile and the wrong one
        /// to profile <i>only</i>: "the crowd is what costs" is a claim about
        /// the whole approach, and it is worth knowing whether it survives
        /// changing the approach.
        /// </para>
        /// <para>
        /// One measured pass each rather than three, because five planners over
        /// three fields is fifteen runs and the hybrid alone is minutes of them.
        /// Read the split, not the third decimal place.
        /// </para>
        /// </remarks>
        [Theory]
        [MemberData(nameof(Scenarios))]
        public void WhatEachPlannerSpendsItOn(string key, string makesDear)
        {
            _out.WriteLine($"=== {key}: eighty orders, every planner, {makesDear} is what this field makes dear ===");
            _out.WriteLine(string.Empty);

            foreach (IRoutePlanner planner in RoutePlanners.All)
            {
                // Warm this planner, then measure it.
                OrderEverybody(Load(key), planner);

                BattleState plain = Load(key);
                var watch = Stopwatch.StartNew();
                Tally bare = OrderEverybody(plain, planner);
                watch.Stop();

                BattleState probed = Load(key);
                PlanningProfile.Start();
                Tally measured = OrderEverybody(probed, planner);
                PlanningProfile.Stop();

                double total = watch.Elapsed.TotalMilliseconds;

                _out.WriteLine(
                    $"{planner.Name,-38} {total,10:0.0} ms   {total / 80d,8:0.00} ms an order   " +
                    $"{bare.Found} routed, {bare.Pressed} pressed");

                _out.WriteLine(
                    $"{string.Empty,-38} " +
                    Share(PlanningProfile.Step.BodyScan) + "  " +
                    Share(PlanningProfile.Step.StandCheck) + "  " +
                    Share(PlanningProfile.Step.GroundClear) + "  " +
                    Share(PlanningProfile.Step.Hunt) + "  " +
                    Share(PlanningProfile.Step.Ladder) + "  " +
                    Share(PlanningProfile.Step.HexSearch));

                _out.WriteLine(
                    $"{string.Empty,-38} legs {measured.Legs,9:N0}   states {measured.States,9:N0}   " +
                    $"clearance checks {PlanningProfile.CallsTo(PlanningProfile.Step.ClearLine),9:N0}");

                _out.WriteLine(string.Empty);

                Assert.Equal(80, bare.Orders);
            }
        }

        /// <summary>One step as a share of self time, for the one-line summaries.</summary>
        private static string Share(PlanningProfile.Step step)
        {
            double total = 0d;

            for (int i = 0; i < (int)PlanningProfile.Step.SweepTest; i++)
                total += PlanningProfile.SelfMilliseconds((PlanningProfile.Step)i);

            double self = PlanningProfile.SelfMilliseconds(step);

            return $"{step} {(total > 0d ? self / total : 0d),6:0.0%}";
        }

        private readonly struct Tally
        {
            public Tally(int orders, int found, int failed, int pressed,
                long places, long legs, long states, long expanded, long bodies, long standChecks)
            {
                Orders = orders;
                Found = found;
                Failed = failed;
                Pressed = pressed;
                Places = places;
                Legs = legs;
                States = states;
                Expanded = expanded;
                Bodies = bodies;
                StandChecks = standChecks;
            }

            public readonly int Orders;
            public readonly int Found;
            public readonly int Failed;
            public readonly int Pressed;
            public readonly long Places;
            public readonly long Legs;
            public readonly long States;
            public readonly long Expanded;
            public readonly long Bodies;
            public readonly long StandChecks;
        }

        private static Tally OrderEverybody(BattleState battle, IRoutePlanner? planner)
        {
            IPathfinder pathfinder = new DirectPathfinder(
                battle.Terrain, new TerrainMovementModel(TestContent.Terrain), TestContent.Terrain);

            int orders = 0, found = 0, failed = 0, pressed = 0;
            long places = 0, legs = 0, states = 0, expanded = 0, bodies = 0, standChecks = 0;

            foreach (UnitInstance unit in battle.UnitsOnField())
            {
                Plan plan = Marching.PlanTo(
                    battle, unit, pathfinder, OrderFor(battle, unit), planner: planner);

                orders++;

                if (plan.Path.Found) found++;
                else failed++;

                if (plan.PressedThrough) pressed++;

                standChecks += plan.Effort.StandChecks;
                places += plan.Effort.Places;
                legs += plan.Effort.Legs;
                states += plan.Effort.States;
                expanded += plan.Effort.Expansions;
                bodies += plan.Effort.Bodies;
            }

            return new Tally(
                orders, found, failed, pressed, places, legs, states, expanded, bodies, standChecks);
        }
    }
}
