using System;
using System.Collections.Generic;
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
    /// The fine board [M155]: a cell is a piece of ground, and a regiment covers
    /// several of them.
    /// </summary>
    /// <remarks>
    /// The measurements the designer asked for before looking at the game again -
    /// both cell sizes, on the real order of battle rather than on an arrangement
    /// built to pass.
    /// </remarks>
    [Collection("the board")]
    public sealed class FineBoardTests : IDisposable
    {
        private readonly ITestOutputHelper _out;

        private readonly float _cellWas = GridMode.CellMetres;
        private readonly float _turnWas = GridMode.TurnSeconds;
        private readonly LatticeShape _shapeWas = GridMode.Shape;

        public FineBoardTests(ITestOutputHelper output) => _out = output;

        /// <remarks>
        /// The board is cached against the battle and the settings are static, so
        /// everything changed here is put back. A leaked cell size would quietly
        /// re-scale somebody else's board, and this suite has been bitten by
        /// exactly that once already - see the skipped test in BoardTests.
        /// </remarks>
        public void Dispose()
        {
            GridMode.CellMetres = _cellWas;
            GridMode.TurnSeconds = _turnWas;
            GridMode.Shape = _shapeWas;
        }

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

        /// <summary>
        /// The board is as fine as the ground, and a regiment covers many cells.
        /// </summary>
        /// <remarks>
        /// The straight refutation of "the grid isnt fine at all its very
        /// coarse". Before [M155] the Great Field was 20 x 26 cells with one
        /// regiment to a cell. The numbers below are the whole claim, so they are
        /// printed and not only asserted.
        /// </remarks>
        [Theory]
        [InlineData(25f)]
        [InlineData(12.5f)]
        public void TheBoardIsAsFineAsTheGroundAndARegimentCoversManyCells(float cellMetres)
        {
            GridMode.CellMetres = cellMetres;

            BattleState battle = Load("greatfield");

            Board board = Board.For(battle);

            GridMode.Muster(battle);

            UnitInstance biggest = battle.UnitsOnField()
                .OrderByDescending(u => u.Footprint.Area).First();

            List<Coord> under = Occupancy.Under(board.Cells, biggest);

            int across = (int)(board.Bounds.Width / board.CellWidth);
            int up = (int)(board.Bounds.Height / board.CellWidth);

            _out.WriteLine($"{cellMetres} m cells: board is {across} x {up} = {across * up:N0} cells");
            _out.WriteLine($"  {biggest.Def.Key} is {biggest.Footprint}, covering {under.Count} cells");
            _out.WriteLine($"  a turn of {GridMode.TurnSeconds} s buys:");

            foreach (UnitInstance one in battle.UnitsOnField()
                         .GroupBy(u => u.Def.Key).Select(g => g.First()).OrderBy(u => u.Def.Speed))
                _out.WriteLine(
                    $"    {one.Def.Key,-14} {one.Def.Speed:0.00} m/s -> {GridMode.CellsPerTurn(one)} cells");

            // Finer than one regiment to a cell, which is the entire point.
            Assert.True(
                under.Count >= 4,
                $"the widest regiment covers {under.Count} cells, so a cell is still about the size of a " +
                "regiment and nothing has been made finer.");

            // And the board is the ground rather than a coarsening of it.
            Assert.True(
                across * up >= 6000,
                $"the board is {across} x {up}, which is not a fine grid.");
        }

        /// <summary>
        /// Once mustered, no two regiments claim the same ground - measured over
        /// every cell of every body, not over their centres.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>This is the gate the coarse board could only pass by definition.</b>
        /// With one regiment to a cell, "no two overlap" was true because two
        /// regiments could not share a cell: the model made the question
        /// unaskable. Here it is a real question - forty regiments, several cells
        /// each, and the test is whether any cell is claimed twice.
        /// </para>
        /// <para>
        /// Non-vacuity is the count of cells claimed, printed and asserted. A
        /// muster that left everybody off the field, or that measured centres
        /// again, would show it in that number.
        /// </para>
        /// </remarks>
        [Theory]
        [InlineData(25f)]
        [InlineData(12.5f)]
        public void NoTwoRegimentsClaimTheSameGround(float cellMetres)
        {
            GridMode.CellMetres = cellMetres;

            BattleState battle = Load("greatfield");

            Board board = Board.For(battle);

            int crowded = GridMode.Muster(battle);

            var owner = new Dictionary<Coord, UnitId>();
            var clashes = new List<string>();
            int claimed = 0;

            foreach (UnitInstance unit in battle.UnitsOnField())
            {
                foreach (Coord cell in Occupancy.Under(board.Cells, unit))
                {
                    claimed++;

                    if (owner.TryGetValue(cell, out UnitId who))
                        clashes.Add($"{cell} claimed by both {who} and {unit.Id}");
                    else
                        owner[cell] = unit.Id;
                }
            }

            _out.WriteLine(
                $"{cellMetres} m cells: {battle.UnitsOnField().Count()} regiments claim {claimed} cells " +
                $"({owner.Count} distinct), {crowded} could not be placed");

            foreach (string clash in clashes.Take(10)) _out.WriteLine("  " + clash);

            Assert.True(
                claimed > 200,
                $"only {claimed} cells were claimed in all, so the bodies are not being measured.");

            Assert.Empty(clashes);
            Assert.Equal(0, crowded);
        }

        /// <summary>
        /// A regiment may face any of twenty-four fronts, and the ground it
        /// covers changes as it turns through them.
        /// </summary>
        /// <remarks>
        /// The designer asked for rotation on a 15-degree axis. The gate is not
        /// that <c>Snap</c> returns multiples of 15 - that is the constant, and
        /// [M150] is the record of what testing a constant instead of a promise
        /// is worth. The gate is that the <b>ground a regiment covers</b> differs
        /// between fronts, which is the thing that makes a facing mean anything
        /// on a board.
        /// </remarks>
        [Fact]
        public void ARegimentCoversDifferentGroundAtEachOfTwentyFourFronts()
        {
            GridMode.CellMetres = 25f;

            BattleState battle = Load("greatfield");

            Board board = Board.For(battle);

            UnitInstance unit = battle.UnitsOnField().First();

            var shapes = new HashSet<string>();
            var sizes = new List<int>();

            for (int i = 0; i < GridMode.FacingCount; i++)
            {
                Facing front = Facing.FromDegrees(i * 360f / GridMode.FacingCount);

                Assert.True(
                    board.IsABoardFacing(front),
                    $"{front} is not a front this board allows, so 15 degrees is not really the step.");

                List<Coord> under =
                    Occupancy.UnderIfItStood(board.Cells, unit, unit.Position, front);

                sizes.Add(under.Count);
                shapes.Add(string.Join("|", under.OrderBy(c => c.Q).ThenBy(c => c.R)));
            }

            _out.WriteLine($"{unit.Def.Key} {unit.Footprint} on {board.CellWidth} m cells:");
            _out.WriteLine($"  {GridMode.FacingCount} fronts gave {shapes.Count} distinct footprints");
            _out.WriteLine($"  cells covered: from {sizes.Min()} to {sizes.Max()}");

            // A rectangle has two axes of symmetry, so 24 fronts can give at most
            // 12 distinct footprints. Anything near 1 means the body is not
            // really being turned.
            Assert.True(
                shapes.Count >= 8,
                $"turning through {GridMode.FacingCount} fronts gave only {shapes.Count} distinct " +
                "footprints, so the grid is too coarse to tell them apart.");
        }
    }
}
