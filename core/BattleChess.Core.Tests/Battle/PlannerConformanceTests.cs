using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using BattleChess.Contracts;
using BattleChess.Rules;
using Xunit;
using Xunit.Abstractions;

namespace BattleChess.Tests.Battle
{
    /// <summary>
    /// Does a planner satisfy the movement requirements, on the fields the game
    /// is actually played on?
    /// </summary>
    /// <remarks>
    /// <para>
    /// Three things separate this from the bench. It asks <b>every</b> planner
    /// the same question rather than only the default; it passes
    /// <c>arriveOn</c>, which the game always does and the bench never did, so
    /// the problem being measured is the one the game poses; and "routed" here
    /// means <b>walkable</b>, not merely returned — every leg is proved against
    /// the rectangle that will travel it (M12), on the front it will hold
    /// (M23).
    /// </para>
    /// <para>
    /// A plan that comes back <c>Found</c> but whose second leg puts the body
    /// through a regiment is the seizure report: the executor refuses the step
    /// the planner promised, and the regiment stands still. Counting those is
    /// the whole point.
    /// </para>
    /// </remarks>
    public sealed class PlannerConformanceTests
    {
        private readonly ITestOutputHelper _out;
        public PlannerConformanceTests(ITestOutputHelper output) => _out = output;

        [Theory]
        [InlineData("crucible")]
        [InlineData("longmarch")]
        [InlineData("brokencountry")]
        public void EveryPlannerAnsweredTheSameOrders(string key)
        {
            _out.WriteLine($"=== {key} — every planner, arriveOn set as the game sets it ===");
            _out.WriteLine(
                "planner                              ms/order   worst    routed  unwalkable  pressed   route s");
            _out.WriteLine(new string('-', 104));

            // The default planner again, with the probes on, so the table below
            // describes the workload the game actually poses rather than the
            // bench's easier one.
            PlanningProfile.Start();
            Measure(key, RoutePlanners.TheStaged);
            PlanningProfile.Stop();
            _out.WriteLine(PlanningProfile.Report($"where an order goes, {key}"));
            _out.WriteLine(string.Empty);

            foreach (IRoutePlanner planner in RoutePlanners.All)
            {
                Report r = Measure(key, planner);
                if (ReferenceEquals(planner, RoutePlanners.TheStaged))
                    _out.WriteLine(
                        $"    stages: staged {StagedRoutePlanner.Staged}, ladder clean " +
                        $"{StagedRoutePlanner.LadderClean}, tangent clean {StagedRoutePlanner.TangentClean}, " +
                        $"pose asked {StagedRoutePlanner.PoseAsked} widened {StagedRoutePlanner.PoseWidened} won {StagedRoutePlanner.PoseWon}, " +
                        $"too dear {StagedRoutePlanner.PoseTooDear}, " +
                        $"pressed {StagedRoutePlanner.Pressed}");
                _out.WriteLine(
                    $"{planner.Name,-34} {r.MsPerOrder,8:0.00} {r.Worst,8:0.0} {r.Routed,8}/{r.Orders} " +
                    $"{r.Unwalkable,9}  {r.Pressed,8}  {r.Seconds,8:0.0}");
            }
        }

        /// <summary>
        /// The order a player actually gives: box-select the army, click once.
        /// Every regiment is sent to the same place, which is both the commonest
        /// case and the one where planners can share their work.
        /// </summary>
        [Theory]
        [InlineData("crucible")]
        [InlineData("longmarch")]
        [InlineData("brokencountry")]
        public void OneClickSendsTheWholeArmy(string key)
        {
            _out.WriteLine($"=== {key} — one destination for everybody ===");
            _out.WriteLine(
                "planner                              ms/order   worst    routed  unwalkable  pressed   route s");
            _out.WriteLine(new string('-', 104));

            foreach (IRoutePlanner planner in RoutePlanners.All)
            {
                Report r = Measure(key, planner, oneDestination: true);

                _out.WriteLine(
                    $"{planner.Name,-34} {r.MsPerOrder,8:0.00} {r.Worst,8:0.0} {r.Routed,8}/{r.Orders} " +
                    $"{r.Unwalkable,9}  {r.Pressed,8}  {r.Seconds,8:0.0}");
            }
        }

        private sealed record Report(
            int Orders, int Routed, int Unwalkable, int Pressed,
            double MsPerOrder, double Worst, double Seconds);

        private static Report Measure(string key, IRoutePlanner planner, bool oneDestination = false)
        {
            BattleState battle = BenchScenariosTests.Load(key);
            IPathfinder pathfinder = new DirectPathfinder(
                battle.Terrain, new TerrainMovementModel(TestContent.Terrain), TestContent.Terrain);

            var units = battle.UnitsOnField().ToList();

            MapBounds bounds = battle.Terrain.Bounds;
            var everybodyTo = new Vec2(
                bounds.Min.X + (bounds.Max.X - bounds.Min.X) * 0.78f,
                bounds.Min.Y + (bounds.Max.Y - bounds.Min.Y) * 0.5f);

            // Not all onto one point — eighty regiments cannot stand on one
            // point, and a test that asks them to measures the impossibility
            // rather than the planner. Spread into a block the way a wing
            // ordered with one click actually arrives.
            Vec2 Destination(UnitInstance unit)
            {
                if (!oneDestination) return BenchScenariosTests.OrderFor(battle, unit);

                int index = units.IndexOf(unit);
                int across = 10;

                return everybodyTo + new Vec2(
                    (index % across - across * 0.5f) * 55f,
                    (index / across - units.Count / (across * 2f)) * 55f);
            }

            // Warm: tiered compilation is still promoting on a first pass.
            foreach (UnitInstance unit in units)
            {
                Vec2 to = Destination(unit);
                Marching.PlanTo(battle, unit, pathfinder, to, planner: planner,
                    arriveOn: Marching.AlongTheLine(unit.Position, to, unit.Facing));
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            StagedRoutePlanner.ResetCounters();

            int routed = 0, unwalkable = 0, pressed = 0;
            double worst = 0d, seconds = 0d;

            var watch = Stopwatch.StartNew();

            foreach (UnitInstance unit in units)
            {
                Vec2 to = Destination(unit);
                Facing arriveOn = Marching.AlongTheLine(unit.Position, to, unit.Facing);

                long began = Stopwatch.GetTimestamp();
                Plan plan = Marching.PlanTo(
                    battle, unit, pathfinder, to, planner: planner, arriveOn: arriveOn);
                double spent = (Stopwatch.GetTimestamp() - began) * 1000d / Stopwatch.Frequency;

                if (spent > worst) worst = spent;

                if (plan.Path.Found)
                {
                    routed++;
                    if (!StagedRoutePlanner.WalksCleanly(battle, unit, plan)) unwalkable++;

                    float priced = Marching.SecondsToWalk(battle, unit, plan.Path.Waypoints, plan.Hold);
                    if (priced > 0f) seconds += priced;
                }

                if (plan.PressedThrough) pressed++;
            }

            watch.Stop();

            return new Report(
                units.Count, routed, unwalkable, pressed,
                watch.Elapsed.TotalMilliseconds / units.Count, worst,
                routed == 0 ? 0d : seconds / routed);
        }
    }
}
