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
    /// Why an order 315 m long spends twenty thousand expansions.
    /// </summary>
    /// <remarks>
    /// The lattice bins ground at 20 m and heading into 16, so the Great
    /// Field - 1800 x 2400 m - holds <b>172 800 states</b>. Twenty thousand
    /// expansions is an eighth of the whole map. Nothing bounds the search to
    /// the stretch of ground the order is about, so the first question is
    /// whether it is wandering or whether each expansion is simply dear.
    /// </remarks>
    public sealed class WhereTheSearchGoesTests
    {
        private readonly ITestOutputHelper _out;
        public WhereTheSearchGoesTests(ITestOutputHelper output) => _out = output;

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

        // The orders recorded as dear, by where the regiment stood.
        private static readonly (string Who, Vec2 From, Vec2 To)[] Dear =
        {
            ("U13 487ms", new Vec2(263f, 1763f), new Vec2(658f, 1678f)),
            ("U15 565ms", new Vec2(263f, 1513f), new Vec2(615f, 1542f)),
            ("U19 889ms", new Vec2(263f, 1038f), new Vec2(544f, 1029f)),
            ("U16 22ms", new Vec2(263f, 1388f), new Vec2(617f, 1360f)),
            ("U17 32ms", new Vec2(263f, 1263f), new Vec2(620f, 1176f)),
        };

        [Fact(Skip = "A record of a measurement, not a check on one — it drives global levers.")]
        public void WhereTwentyThousandExpansionsGo()
        {
            _out.WriteLine(
                "order          asked   expansions   ms    sideways   along   found   overlaps/exp");
            _out.WriteLine(new string('-', 92));

            foreach ((string who, Vec2 from, Vec2 to) in Dear)
            {
                BattleState battle = Load();
                IPathfinder pathfinder = new DirectPathfinder(
                    battle.Terrain, new TerrainMovementModel(TestContent.Terrain), TestContent.Terrain);

                UnitInstance unit = battle.UnitsOnField()
                    .OrderBy(u => Vec2.Distance(u.Position, from)).First();

                Facing arriveOn = Marching.AlongTheLine(unit.Position, to, unit.Facing);

                // Asked of the lattice directly, so the numbers describe the
                // search rather than whichever stage the cascade settled on.
                HybridAStarRoutePlanner.PlanAlong(
                    battle, unit, to, arriveOn, corridor: null, 0f, log: null);

                var watch = Stopwatch.StartNew();
                Plan plan = HybridAStarRoutePlanner.PlanAlong(
                    battle, unit, to, arriveOn, corridor: null, 0f, log: null);
                watch.Stop();

                _out.WriteLine(
                    $"{who,-14} {HybridAStarPlanner.AskedFor,6:0} " +
                    $"{plan.Path.CellsExplored,11} " +
                    $"{watch.Elapsed.TotalMilliseconds,7:0.0} " +
                    $"{HybridAStarPlanner.StrayedSideways,9:0} " +
                    $"{HybridAStarPlanner.StrayedAlong,8:0} " +
                    $"{plan.Path.Found,8} ");
            }
        }
    }
}
