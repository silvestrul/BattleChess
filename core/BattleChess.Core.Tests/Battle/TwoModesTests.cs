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
    /// The two games the designer asked for [M155]: the board, where a regiment
    /// stands on cells and a turn buys whole ones, and the free game, where it
    /// walks but a wing still moves as one.
    /// </summary>
    [Collection("the board")]
    public sealed class TwoModesTests : IDisposable
    {
        private readonly ITestOutputHelper _out;

        private readonly float _cellWas = GridMode.CellMetres;
        private readonly bool _steppedWas = GridMode.StepsOverCells;

        public TwoModesTests(ITestOutputHelper output) => _out = output;

        public void Dispose()
        {
            GridMode.CellMetres = _cellWas;
            GridMode.StepsOverCells = _steppedWas;
            GridMode.TurnOff();
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
        /// Overlapping ground, counted over every cell of every body.
        /// </summary>
        private static List<string> Overlaps(BattleState battle, Board board)
        {
            var owner = new Dictionary<Coord, UnitId>();
            var clashes = new List<string>();

            foreach (UnitInstance unit in battle.UnitsOnField())
                foreach (Coord cell in Occupancy.Under(board.Cells, unit))
                {
                    if (owner.TryGetValue(cell, out UnitId who))
                        clashes.Add($"{cell}: {who} and {unit.Id}");
                    else
                        owner[cell] = unit.Id;
                }

            return clashes;
        }

        /// <summary>
        /// A whole army marching on the board never stands through itself, and a
        /// turn ends with everybody on a cell.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>The gate for mode one.</b> Twelve regiments ordered at the same
        /// piece of ground - deliberately, because converging orders are what
        /// every previous model broke on - marched for six turns, with every cell
        /// of every body checked after each one.
        /// </para>
        /// <para>
        /// Non-vacuity: the steps taken and the distance closed are printed and
        /// asserted. An army that refused every order would keep its formation
        /// perfectly and prove nothing.
        /// </para>
        /// </remarks>
        /// <param name="drawnAtLeast">
        /// How many of the twelve can be given a route at this cell size. A
        /// recording, not a target - see the remarks on the assertion.
        /// </param>
        [Theory]
        [InlineData(25f, 12)]
        [InlineData(12.5f, 12)]
        public void AnArmyMarchingOnTheBoardNeverStandsThroughItself(float cellMetres, int drawnAtLeast)
        {
            GridMode.CellMetres = cellMetres;

            BattleState battle = Load("greatfield");

            // Mustered, but the board planner is NOT installed process-wide.
            //
            // GridMode.TurnOn swaps RoutePlanners.InUse, and xunit runs classes
            // in parallel, so for as long as this test ran every other battle on
            // the machine was being routed over a board it is not standing on.
            // It cost five Crabbing tests and a shifting cast of others, and
            // BoardTests already carries a skipped test recording the same
            // hazard from [M147]. The planner is passed by hand instead.
            GridMode.Muster(battle);

            var overTheBoard = new BoardRoutePlanner();

            Board board = Board.For(battle);

            Assert.Empty(Overlaps(battle, board));

            List<UnitInstance> marching = battle.UnitsOnField().Take(12).ToList();

            // One piece of ground, for all twelve. The worst case on purpose.
            Vec2 onto = battle.UnitsOnField().Last().Position;

            var pathfinder = new HexPathfinder(battle.Terrain, battle.Movement, battle.TerrainCatalogue);

            int drawn = 0;

            foreach (UnitInstance unit in marching)
            {
                unit.GiveOrder(UnitOrder.MoveTo(onto), unit.Position);

                Plan plan = Marching.PlanTo(
                    battle, unit, pathfinder, onto, null, planner: overTheBoard);

                if (!plan.Found)
                {
                    _out.WriteLine($"    {unit.Def.Key} {unit.Id} could not be drawn: " +
                        $"{plan.Path.Failure} - {plan.Path.FailureDetail}");
                    continue;
                }

                unit.Route = plan.ToRoute();
                drawn++;
            }

            _out.WriteLine($"{cellMetres} m cells: {drawn} of {marching.Count} regiments drew a route");


            float wasAway = marching.Sum(u => Vec2.Distance(u.Position, onto));

            int steps = 0, clashes = 0;

            for (int turn = 1; turn <= 6; turn++)
            {
                BoardTurn.Summary played = BoardTurn.Resolve(battle);

                steps += played.Steps;
                clashes += played.Clashes;

                _out.WriteLine($"  turn {turn}: {played}");

                List<string> through = Overlaps(battle, board);

                Assert.True(
                    through.Count == 0,
                    $"after turn {turn}, {through.Count} cells are claimed twice: " +
                    string.Join("; ", through.Take(5)));

                // And nobody is between cells at the end of a turn, which is the
                // designer's "cannot stop midway".
                foreach (UnitInstance unit in battle.UnitsOnField())
                    Assert.True(
                        Vec2.Distance(unit.Position, board.CentreOf(board.Of(unit.Position))) < 0.01f,
                        $"{unit.Def.Key} ended turn {turn} at {unit.Position}, which is not a cell centre.");
            }

            float nowAway = marching.Sum(u => Vec2.Distance(u.Position, onto));

            _out.WriteLine(
                $"  {steps} steps, {clashes} clashes, closed {wasAway - nowAway:0} m of {wasAway:0} m");

            // A recording of what the game manages, not a target.
            //
            // It was 9 of 12 at 25 m while the board searched cell by cell, and
            // the three that failed were exactly the three with a neighbour on
            // both sides - a body 89 m across the diagonal cannot be turned in
            // the Great Field's 100 m deployment corridor once a 25 m grid rounds
            // the clearance away. That was recorded as a real limit of the cell
            // size. It was not: it was a limit of the SEARCH.
            //
            // [M159] hands the way-finding back to the continuous planner, which
            // reasons about the bodies in the way rather than about every cell of
            // the field, and all twelve are routed at both sizes. Worth keeping
            // as a caution: a limit measured through one algorithm is a fact
            // about that algorithm until it has been measured through another.
            Assert.True(
                drawn >= drawnAtLeast,
                $"only {drawn} of {marching.Count} regiments drew a route on {cellMetres} m cells, " +
                $"where {drawnAtLeast} did when this was recorded - something has got worse.");

            Assert.True(
                steps > 20,
                $"only {steps} steps were taken in six turns, so the army did not really march.");

            Assert.True(
                wasAway - nowAway > 500f,
                $"the army closed only {wasAway - nowAway:0} m, so this is not measuring a march.");
        }

        /// <summary>
        /// In the free game a wing ordered together is given places that do not
        /// overlap, and keeps the shape it was drawn in where it can.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>The gate for mode two</b>, and the direct answer to "they still
        /// compete for location". Five regiments in line are marched onto ground
        /// with an outsider standing squarely in the middle of it. The rigid
        /// translation would put one regiment inside that outsider and, before
        /// this, would have routed it there.
        /// </para>
        /// <para>
        /// Non-vacuity: the count of regiments that had to give way is asserted
        /// to be at least one and fewer than all. Nought would mean the
        /// arrangement is not testing the nudge; all five would mean the shape
        /// has not been kept at all.
        /// </para>
        /// </remarks>
        [Fact]
        public void AWingInTheFreeGameIsGivenPlacesThatDoNotOverlap()
        {
            var field = new Battlefield();

            Vec2 centre = field.Centre;

            var wing = new List<UnitInstance>();

            for (int i = 0; i < 5; i++)
                wing.Add(field.Add(
                    0, "spearmen", new Vec2(centre.X - 500f, centre.Y + (i - 2) * 100f), Facing.East));

            Vec2 onto = new Vec2(centre.X + 200f, centre.Y);

            // Standing squarely on the middle regiment's wanted place.
            field.Add(1, "spearmen", onto, Facing.West);

            Vec2 origin = Vec2.Zero;
            foreach (UnitInstance unit in wing) origin += unit.Position;
            origin /= wing.Count;

            var wanted = new Vec2[wing.Count];

            for (int i = 0; i < wing.Count; i++) wanted[i] = onto + (wing[i].Position - origin);

            Vec2[] given = WingFormation.FormUpAt(field.State, wing, wanted, Facing.East);

            int gaveWay = 0;

            for (int i = 0; i < given.Length; i++)
            {
                float moved = Vec2.Distance(given[i], wanted[i]);

                if (moved > 1f) gaveWay++;

                _out.WriteLine(
                    $"  {wing[i].Def.Key} wanted ({wanted[i].X:0},{wanted[i].Y:0}), " +
                    $"given ({given[i].X:0},{given[i].Y:0}), {moved:0} m aside");
            }

            // No two places overlap, and none of them is inside the outsider.
            for (int i = 0; i < given.Length; i++)
            {
                var mine = new OrientedRect(given[i], Facing.East, wing[i].Footprint);

                foreach (UnitInstance other in field.State.UnitsOnField())
                    if (!wing.Contains(other))
                        Assert.False(
                            OrientedRect.Overlaps(mine, other.Shape),
                            $"{wing[i].Def.Key} was sent inside {other.Def.Key}.");

                for (int j = i + 1; j < given.Length; j++)
                    Assert.False(
                        OrientedRect.Overlaps(
                            mine, new OrientedRect(given[j], Facing.East, wing[j].Footprint)),
                        $"{wing[i].Def.Key} and {wing[j].Def.Key} were sent to the same ground.");
            }

            _out.WriteLine($"{gaveWay} of {wing.Count} regiments gave way");

            Assert.True(gaveWay >= 1, "nothing had to give way, so the outsider is not really in the way.");

            Assert.True(
                gaveWay < wing.Count,
                $"all {wing.Count} regiments were moved, so the shape was not kept - this is a re-form, " +
                "not a rigid translation.");
        }
    }
}
