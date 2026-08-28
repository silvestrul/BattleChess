using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using BattleChess.Contracts;
using BattleChess.Rules;
using BattleChess.Rules.HybridPlanning;
using Xunit;
using Xunit.Abstractions;

namespace BattleChess.Tests.Battle
{
    /// <summary>
    /// The two orders that froze the game, on the field they froze it on.
    /// </summary>
    /// <remarks>
    /// Recorded in play on the Great Field: one order at <b>1032,8 ms</b> and
    /// another at <b>874,2 ms</b>, both of which ended up pressing through.
    /// The bench never showed this, so the bench is not the reproduction - it
    /// has eighty regiments on a smaller map and its routes are shorter.
    /// </remarks>
    [Collection(PlannerLevers.Name)]
    public sealed class FrozenOrderTests : IDisposable
    {
        private readonly ITestOutputHelper _out;
        public FrozenOrderTests(ITestOutputHelper output) => _out = output;

        public void Dispose()
        {
            RouteSmoothing.SmoothTheRoute = true;
            StagedRoutePlanner.WayRoundCostCeiling = 3f;
            StagedRoutePlanner.PoseExpansionBudget = 20000;
        }

        private static BattleState Load()
        {
            string root = TestContent.Root;
            ITerrainCatalogue terrain = TestContent.Terrain;

            BattleMapDefinition map = AsciiMapReader.Read(
                File.ReadAllText(Path.Combine(root, "maps", "greatfield.map.txt")), terrain);
            BattleSetup setup = BattleSetup.Parse(
                File.ReadAllText(Path.Combine(root, "battles", "greatfield.battle.txt")));

            return setup.Build(map, terrain, TestContent.Units, TestContent.Formations,
                new TerrainMovementModel(terrain));
        }

        // The orders as the recording has them, by where the regiment stood.
        private static readonly (string Who, Vec2 From, Vec2 To)[] Frozen =
        {
            ("U15", new Vec2(263f, 1513f), new Vec2(651f, 1476f)),
            ("U19", new Vec2(263f, 1038f), new Vec2(544f, 1029f)),
            ("U13", new Vec2(263f, 1763f), new Vec2(502f, 1759f)),
        };

        [Fact]
        public void TheOrdersThatFrozeTheGame()
        {
            _out.WriteLine("order      cap                 ms   waypoints  pressed   route s");
            _out.WriteLine(new string('-', 70));

            foreach ((string who, Vec2 from, Vec2 to) in Frozen)
            {
                foreach (int cap in new[] { 20000 })
                {
                    RouteSmoothing.SmoothTheRoute = true;
                    StagedRoutePlanner.WayRoundCostCeiling = 3f;
                    StagedRoutePlanner.PoseExpansionBudget = cap;

                    BattleState battle = Load();
                    IPathfinder pathfinder = new DirectPathfinder(
                        battle.Terrain, new TerrainMovementModel(TestContent.Terrain), TestContent.Terrain);

                    UnitInstance unit = battle.UnitsOnField()
                        .OrderBy(u => Vec2.Distance(u.Position, from)).First();

                    Facing arriveOn = Marching.AlongTheLine(unit.Position, to, unit.Facing);

                    // Warm: tiered compilation is still promoting on a first pass.
                    Marching.PlanTo(battle, unit, pathfinder, to,
                        planner: RoutePlanners.TheStaged, arriveOn: arriveOn);

                    double best = double.MaxValue;
                    Plan plan = Marching.PlanTo(battle, unit, pathfinder, to,
                        planner: RoutePlanners.TheStaged, arriveOn: arriveOn);

                    for (int pass = 0; pass < 3; pass++)
                    {
                        var watch = Stopwatch.StartNew();
                        Plan one = Marching.PlanTo(battle, unit, pathfinder, to,
                            planner: RoutePlanners.TheStaged, arriveOn: arriveOn);
                        watch.Stop();

                        if (watch.Elapsed.TotalMilliseconds < best)
                        {
                            best = watch.Elapsed.TotalMilliseconds;
                            plan = one;
                        }
                    }

                    float priced = plan.Path.Found
                        ? Marching.SecondsToWalk(battle, unit, plan.Path.Waypoints, plan.Hold)
                        : 0f;

                    _out.WriteLine(
                        $"{who,-10} {(cap == 0 ? "none" : cap.ToString()),-14} " +
                        $"{best,7:0.0} {plan.Path.Waypoints.Count,10} " +
                        $"{plan.PressedThrough,8} {priced,9:0.0}");

                    // The regression this exists to catch: with the ceiling on,
                    // no order may cost more than the ceiling times the walk
                    // straight there. Off, these three cost 5,7 to 6,4 times it.
                    if (plan.Path.Found)
                    {
                        float straight = Marching.SecondsToWalk(
                            battle, unit, new[] { unit.Position, to }, null);

                        if (straight > 1f)
                            Assert.True(priced <= straight * 3f * 1.5f,
                                $"{who} cost {priced:0} s against {straight:0} s straight — " +
                                $"{priced / straight:0.0}x, past the ceiling.");
                    }
                }
            }
        }
    }
}
