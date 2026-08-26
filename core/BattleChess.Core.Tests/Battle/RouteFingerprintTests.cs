using System;
using System.Globalization;
using System.Text;
using BattleChess.Contracts;
using BattleChess.Rules;
using Xunit;
using Xunit.Abstractions;

namespace BattleChess.Tests.Battle
{
    /// <summary>
    /// Every route, written out, so two builds can be compared waypoint by
    /// waypoint instead of by counts that agree while the routes differ.
    /// </summary>
    public sealed class RouteFingerprintTests
    {
        private readonly ITestOutputHelper _out;

        public RouteFingerprintTests(ITestOutputHelper output) => _out = output;

        [Theory]
        [InlineData("crucible")]
        [InlineData("brokencountry")]
        [InlineData("longmarch")]
        public void EveryRouteWrittenOut(string key)
        {
            BattleState battle = BenchScenariosTests.Load(key);

            IPathfinder pathfinder = new DirectPathfinder(
                battle.Terrain, new TerrainMovementModel(TestContent.Terrain), TestContent.Terrain);

            foreach (IRoutePlanner? planner in new IRoutePlanner?[]
                     { RoutePlanners.TheLadder, RoutePlanners.TheSearch, RoutePlanners.TheTangents })
            {
                string name = planner?.Name ?? "default";

                foreach (UnitInstance unit in battle.UnitsOnField())
                {
                    Plan plan = Marching.PlanTo(
                        battle, unit, pathfinder, BenchScenariosTests.OrderFor(battle, unit),
                        planner: planner);

                    var line = new StringBuilder();
                    line.Append(CultureInfo.InvariantCulture, $"{name} u{unit.Id.Value:D3} ");
                    line.Append(plan.Path.Found ? "ok " : "NO ");
                    line.Append(plan.PressedThrough ? "press " : "clean ");
                    line.Append(CultureInfo.InvariantCulture,
                        $"{Marching.SecondsToWalk(battle, unit, plan.Path.Waypoints, plan.Hold):0.0}s ");
                    line.Append(plan.Effort.AskedTheLadder ? "asked " : "kept ");

                    foreach (Vec2 at in plan.Path.Waypoints)
                        line.Append(CultureInfo.InvariantCulture, $"({at.X:0.00},{at.Y:0.00})");

                    _out.WriteLine(line.ToString());
                }
            }
        }
    }
}
