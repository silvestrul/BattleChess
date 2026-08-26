using System;
using System.Collections.Generic;
using System.Diagnostics;
using BattleChess.Contracts;
using BattleChess.Rules;
using BattleChess.Rules.HybridPlanning;
using Xunit;
using Xunit.Abstractions;

namespace BattleChess.Tests.Battle
{
    /// <summary>
    /// What the hybrid costs at the scale it is actually played at — a dozen
    /// regiments and marches of a couple of hundred metres — rather than the
    /// eighty-a-side bench, whose figures do not transfer.
    /// </summary>
    public sealed class HybridCostTests
    {
        private readonly ITestOutputHelper _out;

        public HybridCostTests(ITestOutputHelper output) => _out = output;

        [Fact]
        public void WhatTheHybridCostsAtPlayScale()
        {
            var field = new Battlefield();

            // Thirteen regiments, which is the size in the recordings.
            var mover = field.Add(0, "cavalry", new Vec2(200f, 200f), Facing.FromDegrees(0f));

            for (int i = 0; i < 12; i++)
            {
                float x = 260f + (i % 4) * 90f;
                float y = 140f + (i / 4) * 110f;
                field.Add(0, "spearmen", new Vec2(x, y), Facing.FromDegrees(90f));
            }

            BattleState battle = field.State;

            IPathfinder pathfinder = new DirectPathfinder(
                battle.Terrain, new TerrainMovementModel(TestContent.Terrain), TestContent.Terrain);

            // A spread of marches: short hops and cross-field, some clear and
            // some with the whole block in the way.
            var orders = new List<Vec2>();
            for (int i = 0; i < 12; i++)
                orders.Add(new Vec2(200f + (i % 4) * 180f, 200f + (i / 4) * 160f + 60f));

            foreach (IRoutePlanner planner in RoutePlanners.All)
            {
                // Warm.
                foreach (Vec2 to in orders)
                    Marching.PlanTo(battle, mover, pathfinder, to, planner: planner, arriveOn: Marching.AlongTheLine(mover.Position, to, mover.Facing));

                var each = new List<double>();

                for (int pass = 0; pass < 5; pass++)
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();

                    var watch = Stopwatch.StartNew();
                    foreach (Vec2 to in orders)
                        Marching.PlanTo(battle, mover, pathfinder, to, planner: planner, arriveOn: Marching.AlongTheLine(mover.Position, to, mover.Facing));
                    watch.Stop();

                    each.Add(watch.Elapsed.TotalMilliseconds / orders.Count);
                }

                each.Sort();

                int found = 0;
                long expansions = 0, legs = 0;
                double seconds = 0;

                foreach (Vec2 to in orders)
                {
                    Plan p = Marching.PlanTo(battle, mover, pathfinder, to, planner: planner, arriveOn: Marching.AlongTheLine(mover.Position, to, mover.Facing));
                    if (p.Path.Found) found++;
                    expansions += p.Effort.Expansions;
                    legs += p.Effort.Legs;
                    if (p.Path.Found)
                        seconds += Marching.SecondsToWalk(battle, mover, p.Path.Waypoints, p.Hold);
                }

                PlanningProfile.Start();

                foreach (Vec2 to in orders)
                    Marching.PlanTo(battle, mover, pathfinder, to, planner: planner, arriveOn: Marching.AlongTheLine(mover.Position, to, mover.Facing));

                PlanningProfile.Stop();
                _out.WriteLine(PlanningProfile.Report($"where {planner.Name}'s {orders.Count} orders went"));

                _out.WriteLine(
                    $"{planner.Name,-38} {each[0],8:0.00} ms an order   " +
                    $"(median {each[each.Count / 2]:0.00}, most {each[^1]:0.00})   " +
                    $"{found}/{orders.Count} routed   " +
                    $"{expansions / orders.Count:N0} expansions   {seconds / Math.Max(1, found):0.0} s a route");
            }
        }
    }
}
