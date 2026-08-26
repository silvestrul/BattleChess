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
    /// How dear a way round may be before the press it avoids is the better
    /// order.
    /// </summary>
    /// <remarks>
    /// <para>
    /// M55 gave the way round an absolute priority: any clean route beat any
    /// press, at any price. Recorded in play at tick 651, that bought a
    /// <b>1325 m, 847 s</b> detour to avoid a press the ladder priced at
    /// <b>151 s</b> — on an order 239 m long with one regiment in the way.
    /// </para>
    /// <para>
    /// So the question is not whether to price it but where to put the line,
    /// and that is a measurement rather than an opinion. Skipped for the same
    /// reason <see cref="LeverBenchTests"/> is: it is the record of a sweep,
    /// not a check on one, and it costs minutes.
    /// </para>
    /// </remarks>
    [Collection(PlannerLevers.Name)]
    public sealed class WayRoundCeilingTests : IDisposable
    {
        private readonly ITestOutputHelper _out;
        public WayRoundCeilingTests(ITestOutputHelper output) => _out = output;

        // The levers are global statics and xUnit runs classes side by side.
        public void Dispose()
        {
            StagedRoutePlanner.WayRoundCostCeiling = 3f;
            StagedRoutePlanner.PoseExpansionBudget = 20000;
        }

        private static readonly float[] Ceilings = { 0f, 1.25f, 1.5f, 2f, 3f, 5f, 10f };
        private static readonly string[] Fields = { "crucible", "longmarch", "brokencountry" };

        [Fact(Skip = "The record of a measurement rather than a check on one — minutes, and a sweep.")]
        public void WhereTheLineGoes()
        {
            foreach (bool oneClick in new[] { true, false })
            foreach (string field in Fields)
            {
                _out.WriteLine(
                    $"=== {field} — {(oneClick ? "one click, the whole wing to one block" : "scattered orders")} ===");
                _out.WriteLine(
                    "ceiling   ms/order   routed  unwalk  press  too dear   route s   worst detour");
                _out.WriteLine(new string('-', 82));

                foreach (float ceiling in Ceilings)
                {
                    StagedRoutePlanner.WayRoundCostCeiling = ceiling;
                    Report r = Measure(field, oneClick);

                    _out.WriteLine(
                        $"{(ceiling == 0f ? "off" : ceiling.ToString("0.00")),-9} {r.MsPerOrder,8:0.00} " +
                        $"{r.Routed,7}/{r.Orders} {r.Unwalkable,5} {r.Pressed,6} {r.TooDear,9} " +
                        $"{r.Seconds,9:0.0} {r.WorstDetour,13:0.0}x");
                }

                _out.WriteLine(string.Empty);
            }
        }

        [Fact(Skip = "The record of a measurement rather than a check on one — minutes, and a sweep.")]
        public void WhatTheLatticeMaySpend()
        {
            foreach (bool oneClick in new[] { true, false })
            foreach (string field in Fields)
            {
                _out.WriteLine(
                    $"=== {field} — {(oneClick ? "one click" : "scattered")} — lattice expansion cap ===");
                _out.WriteLine(
                    "cap        ms/order   routed  unwalk  press  too dear   route s   worst detour");
                _out.WriteLine(new string('-', 82));

                foreach (int cap in new[] { 0, 40000, 20000, 10000, 5000, 2000 })
                {
                    StagedRoutePlanner.WayRoundCostCeiling = 3f;
                    StagedRoutePlanner.PoseExpansionBudget = cap;
                    Report r = Measure(field, oneClick);

                    _out.WriteLine(
                        $"{(cap == 0 ? "none" : cap.ToString()),-10} {r.MsPerOrder,8:0.00} " +
                        $"{r.Routed,7}/{r.Orders} {r.Unwalkable,5} {r.Pressed,6} {r.TooDear,9} " +
                        $"{r.Seconds,9:0.0} {r.WorstDetour,13:0.0}x");
                }

                StagedRoutePlanner.PoseExpansionBudget = 0;
                _out.WriteLine(string.Empty);
            }
        }

        private sealed record Report(
            int Orders, int Routed, int Unwalkable, int Pressed, int TooDear,
            double MsPerOrder, double Seconds, double WorstDetour);

        private static Report Measure(string key, bool oneClick)
        {
            BattleState battle = BenchScenariosTests.Load(key);
            IPathfinder pathfinder = new DirectPathfinder(
                battle.Terrain, new TerrainMovementModel(TestContent.Terrain), TestContent.Terrain);
            var units = battle.UnitsOnField().ToList();

            MapBounds bounds = battle.Terrain.Bounds;
            var everybodyTo = new Vec2(
                bounds.Min.X + (bounds.Max.X - bounds.Min.X) * 0.78f,
                bounds.Min.Y + (bounds.Max.Y - bounds.Min.Y) * 0.5f);

            // The same block the conformance harness sends them to, so the two
            // tables are describing one workload rather than two.
            Vec2 Destination(UnitInstance unit)
            {
                if (!oneClick) return BenchScenariosTests.OrderFor(battle, unit);

                int index = units.IndexOf(unit);
                const int across = 10;

                return everybodyTo + new Vec2(
                    (index % across - across * 0.5f) * 55f,
                    (index / across - units.Count / (across * 2f)) * 55f);
            }

            foreach (UnitInstance unit in units)
            {
                Vec2 warm = Destination(unit);
                Marching.PlanTo(battle, unit, pathfinder, warm, planner: RoutePlanners.TheStaged,
                    arriveOn: Marching.AlongTheLine(unit.Position, warm, unit.Facing));
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            StagedRoutePlanner.ResetCounters();

            int routed = 0, unwalkable = 0, pressed = 0;
            double seconds = 0d, worstDetour = 0d;

            var watch = Stopwatch.StartNew();

            foreach (UnitInstance unit in units)
            {
                Vec2 to = Destination(unit);
                Facing arriveOn = Marching.AlongTheLine(unit.Position, to, unit.Facing);

                Plan plan = Marching.PlanTo(
                    battle, unit, pathfinder, to, planner: RoutePlanners.TheStaged, arriveOn: arriveOn);

                if (!plan.Path.Found) continue;

                routed++;
                if (!StagedRoutePlanner.WalksCleanly(battle, unit, plan)) unwalkable++;
                if (plan.PressedThrough) pressed++;

                float priced = Marching.SecondsToWalk(battle, unit, plan.Path.Waypoints, plan.Hold);
                if (priced > 0f) seconds += priced;

                // What the complaint was actually about: not the average, but
                // the one route that goes five times round the houses and is
                // the only one anybody watching will remember.
                float straight = Marching.SecondsToWalk(
                    battle, unit, new[] { unit.Position, to }, null);
                if (straight > 1f && priced / straight > worstDetour) worstDetour = priced / straight;
            }

            watch.Stop();

            return new Report(
                units.Count, routed, unwalkable, pressed, StagedRoutePlanner.PoseTooDear,
                watch.Elapsed.TotalMilliseconds / units.Count,
                routed == 0 ? 0d : seconds / routed, worstDetour);
        }
    }
}
