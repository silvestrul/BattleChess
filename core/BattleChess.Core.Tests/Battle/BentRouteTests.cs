using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BattleChess.Contracts;
using BattleChess.Rules;
using Xunit;
using Xunit.Abstractions;

namespace BattleChess.Tests.Battle
{
    /// <summary>
    /// The routes that come back as an L where a diagonal was asked for.
    /// </summary>
    /// <remarks>
    /// Recorded in play on the Great Field, three of them in one order:
    /// a regiment sent up and to the right that walked <b>327 m straight
    /// down</b> first; one that went <b>89 m the wrong way</b> before turning;
    /// and one that <b>overshot its destination by 150 m and came back</b>.
    /// Each is priced at 1,2 to 2,0 times the straight line, so none of them
    /// trips the ceiling from M65 - the cost is not what is wrong with them.
    /// </remarks>
    public sealed class BentRouteTests
    {
        private readonly ITestOutputHelper _out;
        public BentRouteTests(ITestOutputHelper output) => _out = output;

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
        private static readonly (string Who, Vec2 From, Vec2 To)[] Bent =
        {
            ("U12 down-then-up", new Vec2(363f, 1038f), new Vec2(819f, 963f)),
            ("U13 wrong-way", new Vec2(263f, 1763f), new Vec2(479f, 1273f)),
            ("U14 down-then-hook", new Vec2(263f, 1638f), new Vec2(479f, 1148f)),
            ("U1 overshoot", new Vec2(363f, 1913f), new Vec2(579f, 1498f)),
            ("U9 side-on", new Vec2(363f, 1338f), new Vec2(819f, 1263f)),
        };

        [Fact]
        public void WhyTheRouteBends()
        {
            foreach ((string who, Vec2 from, Vec2 to) in Bent)
            {
                BattleState battle = Load();
                IPathfinder pathfinder = new DirectPathfinder(
                    battle.Terrain, new TerrainMovementModel(TestContent.Terrain), TestContent.Terrain);

                UnitInstance unit = battle.UnitsOnField()
                    .OrderBy(u => Vec2.Distance(u.Position, from)).First();

                Facing arriveOn = Marching.AlongTheLine(unit.Position, to, unit.Facing);

                Plan plan = Marching.PlanTo(battle, unit, pathfinder, to,
                    planner: RoutePlanners.TheStaged, arriveOn: arriveOn);

                float straight = Marching.SecondsToWalk(
                    battle, unit, new[] { unit.Position, to }, null);
                float priced = plan.Path.Found
                    ? Marching.SecondsToWalk(battle, unit, plan.Path.Waypoints, plan.Hold)
                    : 0f;

                _out.WriteLine(
                    $"=== {who} — {unit.Def.DisplayName} from ({unit.Position.X:0},{unit.Position.Y:0}) " +
                    $"to ({to.X:0},{to.Y:0}) ===");
                _out.WriteLine(
                    $"    {plan.Path.Waypoints.Count} waypoints, {priced:0} s against {straight:0} s " +
                    $"straight ({(straight > 0 ? priced / straight : 0):0.00}x), " +
                    $"pressed {plan.PressedThrough}");
                _out.WriteLine(
                    "    " + string.Join(" -> ", plan.Path.Waypoints.Select(w => $"({w.X:0},{w.Y:0})")));

                // Where the smoothing pass could not help, and why. Each
                // waypoint the route keeps is a point nothing could see past;
                // saying which of the two tests refused it is the difference
                // between a blocked line and a costing that will not admit one.
                IReadOnlyList<Vec2> points = plan.Path.Waypoints;

                for (int at = 0; at < points.Count - 2; at++)
                {
                    for (int to2 = points.Count - 1; to2 > at + 1; to2--)
                    {
                        Facing front = Marching.AlongTheLine(points[at], points[to2], unit.Facing);
                        bool clear = Marching.IsClearLine(
                            battle, unit, points[at], points[to2], front, out UnitInstance? blocker);

                        var wound = new List<Vec2>();
                        for (int i = at; i <= to2; i++) wound.Add(points[i]);

                        float around = Marching.SecondsToWalk(battle, unit, wound, null);
                        float direct = Marching.SecondsToWalk(
                            battle, unit, new[] { points[at], points[to2] }, null);

                        _out.WriteLine(
                            $"    cast {at}->{to2}: " +
                            $"{(clear ? "clear" : "blocked by " + (blocker?.Def.DisplayName ?? "?"))}, " +
                            $"{direct:0} s direct against {around:0} s round " +
                            $"{(clear && direct <= around ? " <- would shorten" : string.Empty)}");
                    }
                }

                _out.WriteLine(string.Empty);
            }
        }
    }
}
