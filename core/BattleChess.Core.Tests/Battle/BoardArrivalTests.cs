using System;
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
    /// A waypoint that falls near a cell corner is still reached. [M162]
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>W8, and the recording had the numbers.</b>
    /// `logs/battle-20260903-135252.log`, the designer's play-test, two
    /// regiments frozen where they stood:
    /// </para>
    /// <code>
    /// 131 &gt; board  U1  held at (20,73): there is nowhere nearer it can put itself
    /// 194 &gt; board  U5  held at (23,70): there is nowhere nearer it can put itself
    /// 329   Scene      Spearmen #U5 at (587,5, 1762,5) facing 135° — unmoved
    /// </code>
    /// <para>
    /// U5 stands on the centre of cell (23,70), which is (587,5, 1762,5). Its
    /// next waypoint is (596, 1773), <b>13,5 m away</b>. `BoardTurn` advances a
    /// waypoint when it is within <c>CellWidth * 0,5</c> — <b>12,5 m</b> — and
    /// takes a step only to a cell that gets it <i>nearer</i>. Every one of the
    /// eight neighbouring centres is further from that waypoint: 16,8 m, 19,6 m,
    /// 22,0 m. So nothing advances and nothing closes, and the regiment stands
    /// there for the rest of the battle.
    /// </para>
    /// <para>
    /// <b>The two numbers do not meet.</b> On a 25 m square lattice a point can
    /// be up to <c>25 * sqrt(2) / 2 = 17,68 m</c> from the nearest cell centre —
    /// at a cell corner — and the arrival test allows 12,5. Every waypoint
    /// landing in that band is unreachable by construction.
    /// </para>
    /// </remarks>
    [Collection("the board")]
    public sealed class BoardArrivalTests : IDisposable
    {
        private readonly ITestOutputHelper _out;

        private readonly float _cellWas = GridMode.CellMetres;

        public BoardArrivalTests(ITestOutputHelper output) => _out = output;

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
        public void AWaypointNearACellCornerIsStillReached(float cell)
        {
            GridMode.CellMetres = cell;

            BattleState battle = Load("greatfield");

            Board board = Board.For(battle);

            UnitInstance marcher = battle.UnitsOnField().First(u => u.Def.Key == "spearmen");

            // Nobody else anywhere near, so the only thing under test is the
            // arithmetic.
            foreach (UnitInstance other in battle.UnitsOnField())
                if (other.Id != marcher.Id) other.Position = new Vec2(3000f, 3000f);

            Coord stoodAt = board.Of(new Vec2(600f, 1200f));

            marcher.Position = board.CentreOf(stoodAt);
            marcher.Facing = Facing.FromDegrees(0f);

            // The worst case the lattice admits, and the recorded one: a
            // waypoint offset toward a cell CORNER, further than half a cell
            // and nearer than half a diagonal.
            float offset = cell * 0.5f * 0.75f;

            var waypoint = new Vec2(
                marcher.Position.X + offset, marcher.Position.Y + offset);

            float away = Vec2.Distance(marcher.Position, waypoint);

            // Non-vacuity, and it is the whole finding: the waypoint really is
            // in the band between the arrival test and the nearest cell centre,
            // and no neighbour of this cell is any nearer to it.
            Span<Coord> ring = stackalloc Coord[8];

            board.Cells.Neighbours(stoodAt, ring);

            int nearer = 0;

            for (int i = 0; i < board.Cells.DirectionCount; i++)
                if (Vec2.Distance(board.CentreOf(ring[i]), waypoint) < away) nearer++;

            _out.WriteLine(
                $"cell {cell} m: standing on {stoodAt}, waypoint {away:0.0} m away against an arrival " +
                $"test of {cell * 0.5f:0.0} m; {nearer} of {board.Cells.DirectionCount} neighbours are nearer.");

            Assert.True(
                away > cell * 0.5f,
                $"the waypoint is {away:0.0} m off, inside the {cell * 0.5f:0.0} m arrival test, so it " +
                "would be reached by the old rule too and this measures nothing.");

            Assert.Equal(0, nearer);

            marcher.Route = new MovementRoute(new[] { marcher.Position, waypoint }, false);

            int turns = 0;

            for (; turns < 10 && marcher.Route != null && !marcher.Route.IsComplete; turns++)
                BoardTurn.Resolve(battle);

            _out.WriteLine(
                $"  after {turns} turns the route is " +
                $"{(marcher.Route == null || marcher.Route.IsComplete ? "finished" : "still running")}, " +
                $"and it stands at {board.Of(marcher.Position)}.");

            Assert.True(
                marcher.Route == null || marcher.Route.IsComplete,
                $"the march never finished: standing on {board.Of(marcher.Position)} with a waypoint " +
                $"{Vec2.Distance(marcher.Position, waypoint):0.0} m away that no neighbouring cell is " +
                "nearer to. This is the recorded freeze.");
        }
    }
}
