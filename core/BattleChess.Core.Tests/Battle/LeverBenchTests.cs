using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using BattleChess.Contracts;
using BattleChess.Rules;
using BattleChess.Rules.HybridPlanning;
using Xunit;
using Xunit.Abstractions;

namespace BattleChess.Tests.Battle
{
    /// <summary>
    /// Ways of making an order cheaper, each on its own and then in
    /// combination, on the three fields the game is played on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One process and one sweep, deliberately. Eight combinations measured by
    /// eight test runs would pay the harness's start-up and the runtime's
    /// tiering eight times over and would compare numbers taken in eight
    /// different states of the machine; measured together, everything after the
    /// first combination runs warm and the differences between rows are the
    /// differences between the levers.
    /// </para>
    /// <para>
    /// Least of three passes, per the project's estimator: planning is
    /// deterministic and CPU-bound, so the spread between passes is
    /// interference and the smallest number is the one carrying least of it.
    /// </para>
    /// </remarks>
    [Collection(PlannerLevers.Name)]
    public sealed class LeverBenchTests : IDisposable
    {
        private readonly ITestOutputHelper _out;
        public LeverBenchTests(ITestOutputHelper output) => _out = output;

        /// <summary>
        /// Every lever back where it was found, whatever the test did.
        /// </summary>
        /// <remarks>
        /// These are static settings on the planners, and xUnit runs test
        /// classes beside each other - so a sweep that left one of them turned
        /// over would be answering a different question in somebody else's
        /// test. The sweep itself is skipped for the same reason, and because
        /// it is a minute of measurement whose answer is already written down.
        /// </remarks>
        public void Dispose() => Apply(Best());

        private static readonly string[] Fields = { "longmarch", "crucible", "brokencountry" };

        private const int Passes = 3;

        private sealed class Setting
        {
            public string Name = string.Empty;
            public bool Bent, Corners, Rings, Dial, Tube;
            public float TubeWidth = 45f;
            public int TubeBudget = 4000;
            public int Rounds = 1;
            public int Places = 48;
            public float Fill = 0f;
            public float Run = 0f;
            public float Spacing = 0f;
            public float Weight = 0f;
        }

        private static IEnumerable<Setting> Combinations()
        {
            // The three levers separately and in every combination, and then
            // the two graphs that were the first shape of the first lever, kept
            // in the table because "we tried it and it was worse" is only worth
            // anything with the number beside it.
            foreach (bool bent in new[] { false, true })
            foreach (bool dial in new[] { false, true })
            foreach (bool tube in new[] { false, true })
            {
                var parts = new List<string>();
                if (bent) parts.Add("bent");
                if (dial) parts.Add("dial");
                if (tube) parts.Add("tube");

                yield return new Setting
                {
                    Name = parts.Count == 0 ? "none" : string.Join("+", parts),
                    Bent = bent, Dial = dial, Tube = tube,
                    TubeWidth = 90f, TubeBudget = 20000,
                };
            }

            yield return new Setting { Name = "dial+corners", Dial = true, Corners = true };
            yield return new Setting { Name = "dial+rings", Dial = true, Rings = true };
        }

        [Fact(Skip = "The record of a measurement rather than a check on one: it sweeps ten " +
                     "settings over three fields three times, and it turns global planner " +
                     "settings over while it does. Un-skip to re-take it; the answers are " +
                     "M59 to M63 in docs/DECISIONS.md.")]
        public void LeversSeparatelyAndTogether()
        {
            var loaded = new Dictionary<string, BattleState>();
            foreach (string field in Fields) loaded[field] = BenchScenariosTests.Load(field);

            // Everything warm before anything is believed.
            Apply(new Setting { Name = "warm", Bent = true, Corners = true, Rings = true, Dial = true, Tube = true });
            foreach (string field in Fields) Measure(loaded[field]);

            _out.WriteLine(
                "setting                      field            ms/order   worst   route s  " +
                "bent corn ring  pose wide  press unwalk  badpress");
            _out.WriteLine(new string('-', 118));

            foreach (Setting setting in Combinations())
            {
                Apply(setting);

                foreach (string field in Fields)
                {
                    Report best = null!;

                    for (int pass = 0; pass < Passes; pass++)
                    {
                        Report r = Measure(loaded[field]);
                        if (best == null || r.MsPerOrder < best.MsPerOrder) best = r;
                    }

                    _out.WriteLine(
                        $"{setting.Name,-28} {field,-14} {best.MsPerOrder,8:0.00} {best.Worst,7:0.0} " +
                        $"{best.Seconds,9:0.0}  {best.LadderBent,4} {best.CornersClean,4} {best.RingsClean,4} " +
                        $"{best.PoseAsked,5} {best.PoseWidened,4} {best.Pressed,6} {best.Unwalkable,6} {StagedRoutePlanner.BadPressed,9}");

                    Assert.Equal(best.Orders, best.Routed);
                }

                _out.WriteLine(string.Empty);
            }

        }

        /// <summary>
        /// Where the remaining cost sits, for the combination the sweep chose —
        /// asked rather than guessed at, and asked again after each change,
        /// because a ranking taken before a change describes the old shape.
        /// </summary>
        [Fact]
        public void WhereTheRemainingCostSits()
        {
            var loaded = new Dictionary<string, BattleState>();
            foreach (string field in Fields) loaded[field] = BenchScenariosTests.Load(field);

            Apply(Best());
            foreach (string field in Fields) Measure(loaded[field]);

            foreach (string field in Fields)
            {
                PlanningProfile.Start();
                Report r = Measure(loaded[field]);
                PlanningProfile.Stop();

                _out.WriteLine(PlanningProfile.Report($"where an order goes — {field}"));
                _out.WriteLine(
                    $"    {field}: of the orders no cheap graph could route, " +
                    $"{StagedRoutePlanner.BadFirstLeg} failed getting out on the first leg, " +
                    $"{StagedRoutePlanner.BadLaterLeg} on a later one, " +
                    $"{StagedRoutePlanner.BadPressed} pressed through and {StagedRoutePlanner.BadNoRoute} found nothing at all; " +
                    $"lattice asked {r.PoseAsked}.");
                _out.WriteLine(string.Empty);
            }
        }

        /// <summary>
        /// The same eighty orders given at once rather than one after another.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This is the shape of the requirement, not a trick: a player
        /// box-selects a wing and clicks, and eighty routes are wanted before
        /// the next frame. Nothing in a plan writes to the battle - every
        /// planner reads positions and shapes and returns a route - so the
        /// eighty are independent, and the only question is whether the code
        /// believes that too.
        /// </para>
        /// <para>
        /// Answered by comparing, not by asserting: the routes are priced and
        /// counted exactly as the serial pass prices and counts them, and a
        /// shared scratch list or a shared ledger would show up as a different
        /// number of seconds or a route that no longer walks.
        /// </para>
        /// </remarks>
        [Fact]
        public void TheWholeWingOrderedAtOnce()
        {
            var loaded = new Dictionary<string, BattleState>();
            foreach (string field in Fields) loaded[field] = BenchScenariosTests.Load(field);

            Apply(Best());
            foreach (string field in Fields)
            {
                Measure(loaded[field]);
                Measure(loaded[field], together: true);
            }

            _out.WriteLine(
                $"cores {Environment.ProcessorCount}");
            _out.WriteLine(
                "field           one at a time    all at once   speed-up   " +
                "route s (one/all)   routed  unwalk  press");
            _out.WriteLine(new string('-', 108));

            foreach (string field in Fields)
            {
                Report alone = null!, together = null!;

                for (int pass = 0; pass < Passes; pass++)
                {
                    Report a = Measure(loaded[field]);
                    if (alone == null || a.MsPerOrder < alone.MsPerOrder) alone = a;

                    Report t = Measure(loaded[field], together: true);
                    if (together == null || t.MsPerOrder < together.MsPerOrder) together = t;
                }

                _out.WriteLine(
                    $"{field,-14} {alone.MsPerOrder,10:0.00} ms {together.MsPerOrder,11:0.00} ms " +
                    $"{alone.MsPerOrder / together.MsPerOrder,9:0.0}x   " +
                    $"{alone.Seconds,8:0.0} /{together.Seconds,8:0.0}   " +
                    $"{together.Routed,5}/{together.Orders} {together.Unwalkable,6} {together.Pressed,6}");

                Assert.Equal(alone.Routed, together.Routed);
                Assert.Equal(0, together.Unwalkable);
            }
        }

        /// <summary>The combination the sweep chose, in one place.</summary>
        private static Setting Best() =>
            new Setting { Name = "bent+dial", Bent = true, Dial = true };

        private static void Apply(Setting setting)
        {
            StagedRoutePlanner.AcceptBentLadder = setting.Bent;
            StagedRoutePlanner.AskCorners = setting.Corners;
            StagedRoutePlanner.AskRings = setting.Rings;
            HybridTurnField.DialQueue = setting.Dial;
            StagedRoutePlanner.CorridorFromCheapRoute = setting.Tube;
            StagedRoutePlanner.CheapCorridorHalfWidthMetres = setting.TubeWidth;
            StagedRoutePlanner.BoundedBudget = setting.TubeBudget;
            RouteSearch.MostRounds = setting.Rounds;
            RouteSearch.MostPlaces = setting.Places;
            HybridTurnField.FillMultiple = setting.Fill;
            HybridPrimitives.RunStepMetres = setting.Run;
            HybridAStarPlanner.SweepSpacing = setting.Spacing;
            HybridAStarPlanner.Weight = setting.Weight;
        }

        private sealed record Report(
            int Orders, int Routed, int Unwalkable, int Pressed,
            double MsPerOrder, double Worst, double Seconds,
            int LadderBent, int CornersClean, int RingsClean, int PoseAsked, int PoseWidened);

        private static Report Measure(BattleState battle, bool together = false)
        {
            var units = battle.UnitsOnField().ToList();

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            StagedRoutePlanner.ResetCounters();

            var plans = new Plan[units.Count];
            var spent = new double[units.Count];

            // Every body's rectangle worked out before the batch rather than
            // during it. It is cached behind a flag that a move clears, and
            // nothing moves while a batch is planned - but a lazy write to a
            // struct read by another thread is a torn read, so the write is
            // made to happen first and never again.
            for (int i = 0; i < units.Count; i++) _ = units[i].Shape;

            // A pathfinder each, because one shared between threads would be
            // measuring its own locking rather than the planners.
            IPathfinder Finder() => new DirectPathfinder(
                battle.Terrain, new TerrainMovementModel(TestContent.Terrain), TestContent.Terrain);

            void Order(int i, IPathfinder pathfinder)
            {
                UnitInstance unit = units[i];
                Vec2 to = BenchScenariosTests.OrderFor(battle, unit);
                Facing arriveOn = Marching.AlongTheLine(unit.Position, to, unit.Facing);

                long began = Stopwatch.GetTimestamp();
                plans[i] = Marching.PlanTo(
                    battle, unit, pathfinder, to, planner: RoutePlanners.TheStaged, arriveOn: arriveOn);
                spent[i] = (Stopwatch.GetTimestamp() - began) * 1000d / Stopwatch.Frequency;
            }

            var watch = Stopwatch.StartNew();

            if (together)
            {
                // One order per chunk. The cost of an order is wildly uneven -
                // most are a fraction of a millisecond and a few are eighty -
                // so handing each worker a contiguous block of the list lets
                // one worker draw several of the dear ones and the rest finish
                // early with nothing left to take.
                Parallel.ForEach(
                    Partitioner.Create(0, units.Count, 1), Finder,
                    (range, _, pathfinder) =>
                    {
                        for (int i = range.Item1; i < range.Item2; i++) Order(i, pathfinder);
                        return pathfinder;
                    },
                    _ => { });
            }
            else
            {
                IPathfinder pathfinder = Finder();
                for (int i = 0; i < units.Count; i++) Order(i, pathfinder);
            }

            watch.Stop();

            int routed = 0, unwalkable = 0, pressed = 0;
            double worst = 0d, seconds = 0d;

            for (int i = 0; i < units.Count; i++)
            {
                Plan plan = plans[i];
                if (spent[i] > worst) worst = spent[i];

                if (plan.Path.Found)
                {
                    routed++;
                    if (!StagedRoutePlanner.WalksCleanly(battle, units[i], plan)) unwalkable++;

                    float priced = Marching.SecondsToWalk(battle, units[i], plan.Path.Waypoints, plan.Hold);
                    if (priced > 0f) seconds += priced;
                }

                if (plan.PressedThrough) pressed++;
            }

            return new Report(
                units.Count, routed, unwalkable, pressed,
                watch.Elapsed.TotalMilliseconds / units.Count, worst,
                routed == 0 ? 0d : seconds / routed,
                StagedRoutePlanner.LadderBent, StagedRoutePlanner.CornersClean,
                StagedRoutePlanner.RingsClean, StagedRoutePlanner.PoseAsked,
                StagedRoutePlanner.PoseWidened);
        }
    }
}
