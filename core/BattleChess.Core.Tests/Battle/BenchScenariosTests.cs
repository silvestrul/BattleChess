using System;
using System.Collections.Generic;
using System.Text;
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

        /// <summary>The fields, and what each one is built to make dear.</summary>
        /// <remarks>
        /// <b>W8.</b> The first three were designed; <c>sidewaysmile</c> was
        /// not. It is an arrangement that happened in play and produced a wrong
        /// route (<b>M81</b>), kept as a field so that what it costs is swept on
        /// every lever change rather than only passed or failed once. What it
        /// makes dear is the <i>pose</i>: three of its regiments have already
        /// moved and are facing oddly, which is the case
        /// <see cref="StagedRoutePlanner.StraightLineCostCeiling"/> was added
        /// for and the one a deployment can never produce.
        /// </remarks>
        public static IEnumerable<object[]> Scenarios => new[]
        {
            new object[] { "crucible", "the crowd" },
            new object[] { "longmarch", "the distance" },
            new object[] { "brokencountry", "the ground" },
            new object[] { "sidewaysmile", "the pose" },
        };

        /// <summary>How many regiments the battle file asks for.</summary>
        internal static int Authored(string key)
        {
            int deployments = 0;

            foreach (string line in File.ReadAllLines(
                         Path.Combine(TestContent.Root, "battles", key + ".battle.txt")))
            {
                if (line.TrimStart().StartsWith("[deploy", StringComparison.Ordinal)) deployments++;
            }

            return deployments;
        }

        internal static BattleState Load(string key)
        {
            string root = TestContent.Root;
            ITerrainCatalogue terrain = TestContent.Terrain;

            BattleSetup setup = BattleSetup.Parse(
                File.ReadAllText(Path.Combine(root, "battles", key + ".battle.txt")));

            // The battle file names its own map, and that is not always its own
            // key: sidewaysmile is an arrangement recorded in play on
            // greatfield's ground. Assuming the two names match held only
            // because every bench field so far had been authored as a pair.
            BattleMapDefinition map = AsciiMapReader.Read(
                File.ReadAllText(Path.Combine(root, "maps", setup.MapName + ".map.txt")), terrain);

            return setup.Build(map, terrain, TestContent.Units, TestContent.Formations,
                new TerrainMovementModel(terrain));
        }

        /// <summary>
        /// Where each regiment is sent: diagonally across to the far flank, so
        /// that the eighty marches cross rather than run parallel.
        /// </summary>
        internal static Vec2 OrderFor(BattleState battle, UnitInstance unit)
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

            // Every regiment the battle file authored actually reached the
            // field. This was `Assert.Equal(80, ...)`, which was true of the
            // three designed fields and read as a law rather than as a fact
            // about them - a recorded arrangement has whatever strength it had
            // on the day, and sidewaysmile has forty. What is worth checking is
            // that none of them were silently dropped on the way in.
            Assert.Equal(Authored(key), all.Count);
            Assert.Equal(TangledAtDeployment(key), wrong.Count);
        }

        /// <summary>
        /// How many regiments a field is <i>known</i> to start tangled, and why.
        /// </summary>
        /// <remarks>
        /// Zero for a designed field: a scenario nobody can deploy in measures
        /// nothing, which is what this gate has always been for. But
        /// <c>sidewaysmile</c> is not designed, it is recorded, and at the
        /// moment the order was given the mover was <b>already 2,1% inside its
        /// own Horse Archers</b> - checked at the recording's exact metre
        /// positions, not at the cell centres the reader snaps them to. That
        /// overlap is not a flaw in the fixture, it is the case: it is why the
        /// straight line was refused "by its own Horse Archers", and the route
        /// that followed held side-on for 404 m to get clear of eight metres of
        /// corner. Nudging it apart would delete the thing being measured.
        /// <para>
        /// Declared as an exact count rather than allowed for by relaxing the
        /// assert, so that a <i>second</i> tangle appearing in this field still
        /// fails the build.
        /// </para>
        /// <para>
        /// <b>[M133] deleted that overlap, and with it the case.</b> The
        /// recording was taken on a Great Field where cavalry covered
        /// 229 x 114 m, because every regiment there had been given two thousand
        /// <i>men</i> rather than two thousand <i>worth</i>. With the rule
        /// restored the same regiments are 80 x 40 m at the same positions, and
        /// nothing laps anything: this field now deploys clean and the count is
        /// zero.
        /// </para>
        /// <para>
        /// That is a loss and is recorded as one rather than quietly absorbed.
        /// <c>sidewaysmile</c> was kept for a mover starting 2,1% inside its own
        /// Horse Archers, and it no longer contains one, so it no longer
        /// exercises the case its remarks describe. Regiments still lap in play -
        /// the 31 August recording has 106 contact lines - so the fixture wants
        /// re-recording on corrected ground rather than repairing by hand.
        /// Nudging bodies together to recreate an overlap would be authoring the
        /// answer.
        /// </para>
        /// </remarks>
        private static int TangledAtDeployment(string key) => 0;

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

            // Nine passes now, not three. A pass is under a second, so nine cost
            // seconds; and three was few enough that the middle one was still
            // being nudged about by a single unlucky draw.
            List<double> runs = Passes(key, null, out Tally bare, fewest: 9, most: 9);

            var ordered = new List<double>(runs);
            ordered.Sort();

            // The least, not the middle. Noise is one-sided on work like this.
            double bareMilliseconds = ordered[0];

            // And again with it on, so the report has something to describe.
            BattleState probed = Load(key);
            PlanningProfile.Start();

            var probedWatch = Stopwatch.StartNew();
            Tally measured = OrderEverybody(probed, null);
            probedWatch.Stop();

            PlanningProfile.Stop();

            _out.WriteLine(
                $"=== {key}: {Authored(key)} orders at once, {makesDear} is what this field makes dear ===");
            _out.WriteLine(string.Empty);
            _out.WriteLine(
                $"{measured.Orders} orders   {bareMilliseconds,9:0.0} ms total   " +
                $"{bareMilliseconds / Math.Max(1, measured.Orders),7:0.00} ms an order   " +
                $"{measured.Found} routed, {measured.Failed} refused, {measured.Pressed} pressed through");
            _out.WriteLine(
                Spread(runs));
            _out.WriteLine(
                $"dearest single order {bare.SlowestOrderMs,7:0.0} ms — the tail, which is what drops a frame");
            _out.WriteLine(
                $"instrumented pass {probedWatch.Elapsed.TotalMilliseconds,9:0.0} ms " +
                $"({(probedWatch.Elapsed.TotalMilliseconds / Math.Max(0.001, bareMilliseconds)) - 1d,6:0.0%} on the least) " +
                "— the table below describes that pass, the headline above is the uninstrumented least");
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

            Assert.Equal(Authored(key), measured.Orders);

            // A bench that stopped routing would still produce a tidy table, and
            // the table would be measuring failure rather than planning.
            // Three quarters of them, rather than a flat sixty, so that a field
            // with forty regiments is held to the same standard as one with
            // eighty instead of to a threshold it clears by existing.
            int mustRoute = Authored(key) * 3 / 4;

            Assert.True(measured.Found >= mustRoute,
                $"{key}: only {measured.Found} of {Authored(key)} orders produced a route — " +
                "this is measuring refusals.");

            // Both runs must do the same work, or the breakdown describes
            // something other than the headline.
            Assert.Equal(bare.Found, measured.Found);
            Assert.Equal(bare.Legs, measured.Legs);
        }

        /// <summary>
        /// The same orders, put to every planner in turn.
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
            _out.WriteLine(
                $"=== {key}: {Authored(key)} orders, every planner, {makesDear} is what this field makes dear ===");
            _out.WriteLine(string.Empty);

            foreach (IRoutePlanner planner in RoutePlanners.All)
            {
                // Warm this planner, then measure it — repeatedly. Every row of
                // this table used to be a single draw.
                OrderEverybody(Load(key), planner);

                List<double> runs = Passes(key, planner, out Tally bare);

                BattleState probed = Load(key);
                PlanningProfile.Start();
                Tally measured = OrderEverybody(probed, planner);
                PlanningProfile.Stop();

                var sorted = new List<double>(runs);
                sorted.Sort();

                double total = sorted[0];

                _out.WriteLine(
                    $"{planner.Name,-38} {total,10:0.0} ms   {total / Authored(key),8:0.00} ms an order   " +
                    $"{bare.Found} routed, {bare.Pressed} pressed");

                _out.WriteLine($"{string.Empty,-38} {Spread(runs)}");

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

                Assert.Equal(Authored(key), bare.Orders);
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

        internal readonly struct Tally
        {
            public Tally(int orders, int found, int failed, int pressed,
                long places, long legs, long states, long expanded, long bodies, long standChecks,
                double slowestOrderMs)
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
                SlowestOrderMs = slowestOrderMs;
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

            /// <summary>The dearest single order in the pass.</summary>
            /// <remarks>
            /// The total answers "is this implementation faster". It does not
            /// answer "will a burst of orders hold a frame", which is the
            /// question the whole planning budget exists for, and that one is a
            /// tail question — one order of 40 ms drops a frame however good the
            /// mean is.
            /// </remarks>
            public readonly double SlowestOrderMs;
        }

        /// <summary>
        /// One measured pass, and everything that has to happen around it for a
        /// second pass to be a sample of the same thing as the first.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Collected between passes, outside the clock.</b> Every pass builds
        /// a fresh <see cref="BattleState"/> and nothing was collecting between
        /// them, so pass three could be paying for the litter of passes one and
        /// two. That is a drift rather than a spread: it biases every estimator
        /// in the same direction, and taking the minimum does not remove it, it
        /// just always picks the first pass.
        /// </para>
        /// <para>
        /// <b>Reported in the order they ran.</b> The old report sorted the
        /// passes before printing them, which is exactly the information needed
        /// to tell drift from noise — three passes rising is a different fault
        /// from three passes scattered, and sorted output cannot tell them
        /// apart. Sorting is for choosing the median, not for the page.
        /// </para>
        /// </remarks>
        internal static double OnePass(string key, IRoutePlanner? planner, out Tally tally)
        {
            BattleState plain = Load(key);

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var watch = Stopwatch.StartNew();
            tally = OrderEverybody(plain, planner);
            watch.Stop();

            return watch.Elapsed.TotalMilliseconds;
        }

        /// <summary>
        /// Repeats a pass until there are enough of them to say anything, inside
        /// a time budget so that a planner costing three minutes a pass does not
        /// cost an hour.
        /// </summary>
        /// <remarks>
        /// A single draw is not a measurement, and this bench reported one for
        /// every row of its per-planner table — which is how a lone sample
        /// landing 42% off its own median came to be written up as two
        /// measurements disagreeing. Cheap planners now get <paramref
        /// name="most"/> passes; the dear ones get <paramref name="fewest"/> and
        /// the report says how many, so a thin number is visibly thin.
        /// </remarks>
        internal static List<double> Passes(
            string key, IRoutePlanner? planner, out Tally tally,
            int fewest = 3, int most = 9, double budgetMs = 4000d)
        {
            var runs = new List<double>();
            double spent = 0d;

            tally = default;

            while (runs.Count < most)
            {
                runs.Add(OnePass(key, planner, out tally));
                spent += runs[^1];

                if (runs.Count >= fewest && spent >= budgetMs) break;
            }

            return runs;
        }

        /// <summary>
        /// The passes as a line: how many, the least, the middle, the most, and
        /// the order they came in.
        /// </summary>
        /// <remarks>
        /// The least is the headline. The work is deterministic and CPU-bound,
        /// so noise can only ever add time — every pass above the minimum is the
        /// minimum plus something that is not the code. The most is kept beside
        /// it because "is this faster" and "does this hold a frame" are
        /// different questions and only the first one is answered by a floor.
        /// </remarks>
        internal static string Spread(IReadOnlyList<double> runs)
        {
            var sorted = new List<double>(runs);
            sorted.Sort();

            double least = sorted[0];
            double middle = sorted[sorted.Count / 2];
            double most = sorted[^1];

            var order = new StringBuilder();
            for (int i = 0; i < runs.Count; i++)
                order.Append(i == 0 ? string.Empty : " ").Append($"{runs[i]:0}");

            return $"n={runs.Count}   least {least,8:0.0}   median {middle,8:0.0}   most {most,8:0.0} ms   " +
                   $"({(most - least) / Math.Max(0.001, least),0:0.0%} apart)   as they ran: {order}";
        }

        internal static Tally OrderEverybody(BattleState battle, IRoutePlanner? planner)
        {
            IPathfinder pathfinder = new DirectPathfinder(
                battle.Terrain, new TerrainMovementModel(TestContent.Terrain), TestContent.Terrain);

            int orders = 0, found = 0, failed = 0, pressed = 0;
            double slowestOrder = 0d;
            long places = 0, legs = 0, states = 0, expanded = 0, bodies = 0, standChecks = 0;

            foreach (UnitInstance unit in battle.UnitsOnField())
            {
                long began = Stopwatch.GetTimestamp();

                Plan plan = Marching.PlanTo(
                    battle, unit, pathfinder, OrderFor(battle, unit), planner: planner);

                double spent = (Stopwatch.GetTimestamp() - began) * 1000d / Stopwatch.Frequency;
                if (spent > slowestOrder) slowestOrder = spent;

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
                orders, found, failed, pressed, places, legs, states, expanded, bodies, standChecks,
                slowestOrder);
        }
    }
}
