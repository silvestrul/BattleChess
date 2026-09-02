using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using BattleChess.Contracts;
using BattleChess.Rules;
using BattleChess.Rules.Grid;
using Xunit;
using Xunit.Abstractions;

namespace BattleChess.Tests.Battle
{
    /// <summary>
    /// What an order costs, board planner against the continuous one, on the
    /// same battle and the same destinations.
    /// </summary>
    /// <remarks>
    /// The designer reported twice that the board is slow and said it was "way
    /// faster when we had our algorithm in place". I have guessed at the cause
    /// twice. This measures it instead.
    /// </remarks>
    [Collection("the board")]
    public sealed class PlannerCostProbe : IDisposable
    {
        private readonly ITestOutputHelper _out;

        private readonly float _cellWas = GridMode.CellMetres;

        public PlannerCostProbe(ITestOutputHelper output) => _out = output;

        public void Dispose() => GridMode.CellMetres = _cellWas;

        private static BattleState Load(string name)
        {
            string root = TestContent.Root;
            ITerrainCatalogue terrain = TestContent.Terrain;

            BattleSetup setup = BattleSetup.Parse(
                File.ReadAllText(Path.Combine(root, "battles", $"{name}.battle.txt")));

            BattleMapDefinition map = AsciiMapReader.Read(
                File.ReadAllText(Path.Combine(root, "maps", $"{setup.MapName}.map.txt")), terrain);

            return setup.Build(map, terrain, TestContent.Units, TestContent.Formations,
                new TerrainMovementModel(terrain));
        }

        [Fact]
        public void WhatAnOrderCostsOnTheBoardAgainstTheContinuousGame()
        {
            foreach (float cell in new[] { 25f, 12.5f })
            {
                GridMode.CellMetres = cell;

                BattleState battle = Load("greatfield");

                GridMode.Muster(battle);

                var pathfinder = new HexPathfinder(battle.Terrain, battle.Movement, battle.TerrainCatalogue);

                List<UnitInstance> ordered = battle.UnitsOnField().Take(20).ToList();

                Vec2 onto = battle.UnitsOnField().Last().Position;

                foreach ((string name, IRoutePlanner planner) in new (string, IRoutePlanner)[]
                {
                    ("over the board", new BoardRoutePlanner()),
                    ("the continuous one", RoutePlanners.Default),
                })
                {
                    // One warm pass, so neither is charged for the first-touch
                    // cost of a table the other one has already filled.
                    foreach (UnitInstance unit in ordered)
                        Marching.PlanTo(battle, unit, pathfinder, onto, null, planner: planner);

                    var watch = Stopwatch.StartNew();

                    int found = 0, explored = 0;

                    foreach (UnitInstance unit in ordered)
                    {
                        Plan plan = Marching.PlanTo(battle, unit, pathfinder, onto, null, planner: planner);

                        if (plan.Found) found++;

                        explored += plan.Path.CellsExplored;
                    }

                    watch.Stop();

                    _out.WriteLine(
                        $"{cell,5} m  {name,-20} {watch.Elapsed.TotalMilliseconds,8:0.0} ms for " +
                        $"{ordered.Count} orders  ({watch.Elapsed.TotalMilliseconds / ordered.Count,6:0.00} ms " +
                        $"each), {found} routed, {explored:N0} cells explored");
                }
            }
        }
    }
}
