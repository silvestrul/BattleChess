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
    /// How much clear ground the board throws away, in metres. [M161]
    /// </summary>
    /// <remarks>
    /// <para>
    /// The designer, over a screenshot of a regiment refused a place with
    /// visible room on both sides: <i>"it has space here but doesnt fit"</i>.
    /// Reconstructed from `logs/battle-20260903-113843.log`, which recorded the
    /// arrangement: Spearmen at (312, 1287) and (312, 1087), both 80 x 40 m
    /// facing 0 degrees, so their frontages run north-south and the clear gap
    /// between them is y 1127 to 1247 - <b>120 m of open ground for an 80 m
    /// body, 20 m to spare at each end</b>.
    /// </para>
    /// <para>
    /// The board refuses it, and the reason is that a body claims every cell it
    /// touches at all: a body ending at 1227 and one starting at 1247 both claim
    /// the cell running 1225 to 1250, and the board cannot tell twenty metres
    /// apart from standing in each other.
    /// </para>
    /// </remarks>
    [Collection("the board")]
    public sealed class BoardClearanceTests : IDisposable
    {
        private readonly ITestOutputHelper _out;

        private readonly float _cellWas = GridMode.CellMetres;

        public BoardClearanceTests(ITestOutputHelper output) => _out = output;

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

        [Theory]
        [InlineData(25f)]
        [InlineData(12.5f)]
        public void TheNarrowestGapTheBoardWillLetARegimentStandIn(float cell)
        {
            GridMode.CellMetres = cell;

            BattleState battle = Load("greatfield");

            Board board = Board.For(battle);

            List<UnitInstance> all = battle.UnitsOnField().ToList();

            UnitInstance mover = all[0];
            UnitInstance above = all[1];
            UnitInstance below = all[2];

            foreach (UnitInstance other in all)
                if (other.Id != mover.Id) other.Position = new Vec2(4000f, 4000f);

            var front = Facing.FromDegrees(0f);

            mover.Facing = front;
            above.Facing = front;
            below.Facing = front;

            float frontage = mover.Footprint.Width;

            _out.WriteLine(
                $"cell {cell} m, a {mover.Footprint.Width:0} x {mover.Footprint.Depth:0} m regiment " +
                $"facing 0 degrees, so {frontage:0} m of it lies across the gap.");
            _out.WriteLine(string.Empty);
            _out.WriteLine("  clear gap   spare each end   the board says      bodies really clear");
            _out.WriteLine("  " + new string('-', 66));

            float narrowest = -1f;
            float narrowestByCells = -1f;

            for (float gap = frontage; gap <= frontage + 4f * cell; gap += 2.5f)
            {
                const float middle = 1200f;

                above.Position = new Vec2(400f, middle + gap * 0.5f + frontage * 0.5f);
                below.Position = new Vec2(400f, middle - gap * 0.5f - frontage * 0.5f);
                mover.Position = new Vec2(400f, middle);

                Coord at = board.Of(new Vec2(400f, middle));

                var taken = new Dictionary<Coord, UnitId>();

                foreach (Coord c in board.CellsUnder(above)) taken[c] = above.Id;
                foreach (Coord c in board.CellsUnder(below)) taken[c] = below.Id;

                bool fits = board.CouldStandAt(battle, mover, at, front, taken);

                Board.BodiesDecideAClash = false;
                bool byCells = board.CouldStandAt(battle, mover, at, front, taken);
                Board.BodiesDecideAClash = true;

                if (byCells && narrowestByCells < 0f) narrowestByCells = gap;

                // What the continuous game says about the same arrangement,
                // which is the answer the player can see with their own eyes.
                bool reallyClear =
                    !OrientedRect.Overlaps(mover.Shape, above.Shape) &&
                    !OrientedRect.Overlaps(mover.Shape, below.Shape);

                if (fits && narrowest < 0f) narrowest = gap;

                _out.WriteLine(
                    $"  {gap,8:0.0} m {(gap - frontage) * 0.5f,14:0.0} m   " +
                    $"{(fits ? "fits" : "will not fit"),-18} {reallyClear}   " +
                    $"by cells alone: {(byCells ? "fits" : "will not fit")}");
            }

            _out.WriteLine(string.Empty);
            _out.WriteLine(
                $"  narrowest gap the board accepts: {narrowest:0.0} m for an {frontage:0} m body - " +
                $"{(narrowest - frontage) * 0.5f:0.0} m of clear ground thrown away at each end.");
            _out.WriteLine(
                $"  judged by cells alone it was {narrowestByCells:0.0} m - " +
                $"{(narrowestByCells - frontage) * 0.5f:0.0} m at each end.");

            // The bound, and it is exact rather than generous. A regiment
            // stands on a cell CENTRE, so the nearest place it may stand can be
            // half a cell off the middle of the gap, at each end. That is the
            // board's own rule and it is the whole of what the board may
            // honestly cost - a cell's width in total, no more.
            //
            // Measured here: 152,5 -> 105,0 m at 25 m cells and 102,5 -> 92,5
            // at 12,5 m, which is exactly frontage plus one cell in both cases.
            // What was thrown away beyond that was quantisation, not geometry.
            Assert.True(
                narrowest <= frontage + board.CellWidth + 0.01f,
                $"the board wants {narrowest:0.0} m for an {frontage:0} m body, which is more than the " +
                $"{frontage + board.CellWidth:0.0} m that standing on a cell centre can honestly cost.");

            Assert.True(
                narrowestByCells > narrowest,
                "judging by cells alone is no worse here, so this measures nothing.");

            Assert.True(narrowest > 0f, "the board refused every gap up to four cells wider than the body.");
        }
    }
}
