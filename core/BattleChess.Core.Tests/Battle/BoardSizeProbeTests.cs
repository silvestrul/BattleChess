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
    /// How big a regiment actually is on a real field, and whether the board
    /// built for that field can hold one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This exists because the catalogue lied to me and I believed it
    /// [M149].</b> The check it replaces measured
    /// <c>def.FootprintAt(def.DefaultStrength)</c>, got 40 x 20 m for every unit
    /// type, and the fixed 50 m cell size of [M147] was derived from that. But a
    /// regiment's frontage follows its <i>strength</i>, and battle files raise
    /// strength to two thousand worth - so a regiment on the Great Field is
    /// <b>80 x 40 m and 89,4 m across the diagonal</b>, which its own header
    /// states in so many words and which no test was reading. Every regiment in
    /// the played build was nearly two hexes wide.
    /// </para>
    /// <para>
    /// So these run over <b>every battle file in content</b>, at the strengths
    /// those files ask for, and they check the two things a board must be true
    /// about: a regiment fits its hex, and after mustering no two bodies
    /// overlap. The second is the one the play-test saw fail, and the first
    /// draft did not ask it - it asked only whether regiments were on distinct
    /// hexes, which was true the whole time and told nobody anything.
    /// </para>
    /// </remarks>
    public sealed class BoardSizeProbeTests
    {
        private readonly ITestOutputHelper _out;

        public BoardSizeProbeTests(ITestOutputHelper output) => _out = output;

        public static IEnumerable<object[]> EveryBattleFile()
        {
            foreach (string path in Directory.EnumerateFiles(
                         Path.Combine(TestContent.Root, "battles"), "*.battle.txt"))
                yield return new object[] { Path.GetFileName(path).Replace(".battle.txt", string.Empty) };
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
        /// The board built for a field holds every regiment standing on it.
        /// </summary>
        /// <remarks>
        /// Non-vacuity: the cell size is derived from the widest body on the
        /// field, so this can only fail if the derivation itself is wrong - and
        /// it did fail, loudly, on six of fourteen fields while the cell was a
        /// constant. It is kept as the guard on <c>Board.CellFor</c>.
        /// </remarks>
        [Theory]
        [MemberData(nameof(EveryBattleFile))]
        public void TheBoardHoldsEveryRegimentThatStandsOnIt(string field)
        {
            BattleState battle = Load(field);
            Board board = Board.For(battle);

            var widest = 0f;
            string widestOne = "nothing on the field";
            Footprint widestShape = default;

            foreach (UnitInstance unit in battle.UnitsOnField())
            {
                float across = 2f * unit.Footprint.BoundingRadius;

                if (across > widest)
                {
                    widest = across;
                    widestOne = $"{unit.Def.Key} at {unit.Strength}";
                    widestShape = unit.Footprint;
                }

                Assert.True(
                    board.Holds(unit.Footprint),
                    $"{field}: {unit.Def.Key} at {unit.Strength} is " +
                    $"{2f * unit.Footprint.BoundingRadius:0.0} m across the diagonal and will not fit a " +
                    $"{board.CellWidth:0} m cell.");
            }

            _out.WriteLine(
                $"{field,-16} widest {widestOne}: {widestShape.Width:0.0} x {widestShape.Depth:0.0} m, " +
                $"{widest:0.0} m across -> {board.CellWidth:0} m {board.Cells.Name}, " +
                $"{board.ShortSideInCells:0} across the short side");
        }

        /// <summary>
        /// After mustering, no two regiments on any field are inside each other.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>The board's whole reason for existing, asked directly.</b> The
        /// play-test that found [M149] showed regiments plainly overlapping
        /// while the muster reported that all forty had a hex of their own -
        /// both true at once, because a body nearly twice the width of its hex
        /// spills into six neighbours. Distinct hexes only implies distinct
        /// bodies when a body fits its hex.
        /// </para>
        /// <para>
        /// Measured with the same <c>OverlapFraction</c> the collision rules
        /// use, so this is the simulation's own opinion of whether two bodies
        /// are in the same place rather than a second geometry written for the
        /// test.
        /// </para>
        /// </remarks>
        [Theory]
        [MemberData(nameof(EveryBattleFile))]
        public void NoTwoRegimentsOverlapOnceMustered(string field)
        {
            BattleState battle = Load(field);

            GridMode.Muster(battle);

            List<UnitInstance> standing = battle.UnitsOnField().ToList();

            var worst = 0f;
            string worstPair = "none";

            for (int a = 0; a < standing.Count; a++)
            for (int b = a + 1; b < standing.Count; b++)
            {
                float overlap = OrientedRect.OverlapFraction(standing[a].Shape, standing[b].Shape);

                if (overlap <= worst) continue;

                worst = overlap;
                worstPair = $"{standing[a].Def.Key} and {standing[b].Def.Key}";
            }

            _out.WriteLine(
                $"{field,-16} {standing.Count,3} regiments, worst overlap {worst:0.000} ({worstPair})");

            Assert.True(
                worst <= 0.001f,
                $"{field}: {worstPair} overlap by {worst:0.000} of a body after mustering, so the board " +
                "is not holding one regiment to a hex.");
        }

        /// <summary>
        /// Regiments in adjacent hexes along their shoulder axis stand in a
        /// line: evenly spaced, no stagger, and not inside each other.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>[M150], and it is the test the whole board should have had from
        /// the first day.</b> The designer put it as a question - a hex map
        /// "disallows for line formation where units are lined up" - and it is
        /// provable rather than a matter of taste. Frontage runs perpendicular
        /// to facing; the six hex bearings are multiples of sixty; ninety plus a
        /// multiple of sixty is never a multiple of sixty. So a hex board can
        /// align marching with the grid or lines with it, and the first draft
        /// picked marching without noticing there was a choice.
        /// </para>
        /// <para>
        /// <b>Non-vacuity, and it is unusually strong here</b> because the
        /// failing arrangement is known and measured: with facing snapped to a
        /// hex bearing this same test reports 77,9 m of spacing and <b>45 m of
        /// stagger</b>. Setting <c>Board.FacingOffsetDegrees</c> back to zero
        /// fails it on the stagger, loudly, with the number in the message.
        /// </para>
        /// </remarks>
        [Theory]
        [InlineData("greatfield")]
        [InlineData("crucible")]
        public void RegimentsInAdjacentHexesStandInALine(string field)
        {
            BattleState battle = Load(field);

            GridMode.Muster(battle);

            Board board = Board.For(battle);

            UnitInstance first = battle.UnitsOnField().First();

            Facing front = board.Snap(first.Facing);
            Coord shoulder = board.ShoulderStep(front);

            // Four regiments drawn up from one hex along the shoulder axis,
            // every one on the same front. Taken off the field so this measures
            // the geometry and not whatever the deployment happens to be.
            List<UnitInstance> drawnUp = battle.UnitsOnField().Take(4).ToList();

            Assert.Equal(4, drawnUp.Count);

            Coord at = board.Of(first.Position);

            foreach (UnitInstance unit in drawnUp)
            {
                unit.Position = board.CentreOf(at);
                unit.Facing = front;

                at += shoulder;
            }

            // Along the line and across it, in the regiments' own frame.
            Vec2 alongTheLine = front.RightVector();
            Vec2 acrossIt = front.ToVector();

            _out.WriteLine($"{field}: {drawnUp.Count} regiments on {front}, shouldering {shoulder}");
            _out.WriteLine($"a cell is {board.CellWidth:0} m of {board.Cells.Name}; " +
                           $"a regiment is {first.Footprint.Width:0} m wide");

            var worstStagger = 0f;
            var worstOverlap = 0f;

            for (int i = 1; i < drawnUp.Count; i++)
            {
                Vec2 step = drawnUp[i].Position - drawnUp[i - 1].Position;

                float along = Vec2.Dot(step, alongTheLine);
                float across = Vec2.Dot(step, acrossIt);

                _out.WriteLine($"  next regiment {MathF.Abs(along):0.0} m along, {across:0.0} m fore or aft");

                worstStagger = MathF.Max(worstStagger, MathF.Abs(across));

                worstOverlap = MathF.Max(
                    worstOverlap, OrientedRect.OverlapFraction(drawnUp[i - 1].Shape, drawnUp[i].Shape));

                Assert.True(
                    MathF.Abs(MathF.Abs(along) - board.CellWidth) < 0.01f,
                    $"regiments a cell apart are {MathF.Abs(along):0.0} m apart along the line, not " +
                    $"{board.CellWidth:0}.");
            }

            _out.WriteLine($"worst stagger {worstStagger:0.0} m, worst overlap {worstOverlap:0.000}");

            // The line is a line: shoulder to shoulder, nobody ahead of anybody.
            Assert.True(
                worstStagger < 0.01f,
                $"{field}: regiments in a line are staggered by {worstStagger:0.0} m, so this is a " +
                "staircase and not a line. A hex board can align marching or lines and never both, " +
                "which is what the square lattice exists to answer - see ILattice.");

            // And they are beside each other rather than inside each other.
            Assert.True(worstOverlap <= 0.001f, $"{field}: neighbours in the line overlap by {worstOverlap:0.000}.");

            // Non-vacuity on the arrangement itself: a line of one proves nothing,
            // and a frontage wider than the hex could not be a line at all.
            Assert.True(first.Footprint.Width <= board.CellWidth, "the regiment is wider than its cell.");
        }

        /// <summary>
        /// A settled regiment is not still trying to turn: the front it holds
        /// and the front it was ordered to hold are the same.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>[M152], reported from play as cavalry that "kept rotating a bit
        /// for no reason".</b> It was a loop, and a permanent one. An order's
        /// front is the bearing from where a regiment stood to where it was sent
        /// - a free angle. On the board a regiment may hold only one of the
        /// lattice's facings. So <c>WheelOnTheSpot</c> turned the halted
        /// regiment toward the free bearing, the end of the turn snapped it back
        /// to a board facing, and the next turn turned it again: a few degrees
        /// back and forth for ever, worst on cavalry because cavalry turns
        /// fastest.
        /// </para>
        /// <para>
        /// <b>Non-vacuity, and it is exact.</b> The order below is given with a
        /// destination deliberately off any board bearing, so before the fix
        /// <c>OrderFacing</c> and <c>Facing</c> differ by up to half a step and
        /// the assertion fails with that gap in the message. Snapping only the
        /// facing - which is what the code did - does not satisfy it, because
        /// the thing being turned <i>toward</i> is the ordered front.
        /// </para>
        /// </remarks>
        [Fact]
        public void ASettledRegimentIsNotStillTryingToTurn()
        {
            BattleState battle = Load("greatfield");

            GridMode.Muster(battle);

            Board board = Board.For(battle);

            UnitInstance unit = battle.UnitsOnField().First(u => u.Def.Key == "cavalry");

            // A bearing that is on no board facing, whatever the lattice: 22,5
            // degrees is half a step on squares and off every step on hexes.
            Vec2 awkward = unit.Position + Facing.FromDegrees(22.5f).ToVector() * 400f;

            unit.GiveOrder(UnitOrder.MoveTo(awkward), unit.Position);

            _out.WriteLine($"ordered on {unit.OrderFacing}, holding {unit.Facing}");

            // It arrives and the turn ends.
            unit.Position = awkward;
            unit.Route = null;

            GridMode.SettleThoseWhoHaveStopped(battle);

            float apart = Facing.AbsoluteDelta(unit.Facing, unit.OrderFacing) * 180f / MathF.PI;

            _out.WriteLine($"settled on {unit.Facing}, ordered front now {unit.OrderFacing}, {apart:0.###} deg apart");

            Assert.True(
                board.IsABoardFacing(unit.Facing),
                $"a settled regiment holds {unit.Facing}, which is not a facing this board allows.");

            Assert.True(
                apart < 0.01f,
                $"a settled regiment holds {unit.Facing} but was ordered onto {unit.OrderFacing}, " +
                $"{apart:0.##} degrees apart - so it will turn toward the order, be snapped back at the " +
                "end of the turn, and turn again for ever.");
        }

        /// <summary>
        /// A wing ordered together is given a cell apiece, and no two the same.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>[M152], reported from play as regiments conflicting over where
        /// they were going.</b> A wing is ordered by translating the shape it
        /// stands in, which on a board is exact - until a wanted cell is water
        /// or held by somebody outside the wing, and that regiment is shoved
        /// aside. The wing is planned in parallel against one snapshot, so the
        /// next regiment knows nothing of the shove and can be sent to the same
        /// ground.
        /// </para>
        /// <para>
        /// Non-vacuity: the wing here is marched onto ground <b>already held by
        /// regiments outside it</b>, so shoving is forced rather than
        /// hypothetical - the count of members that had to give way is printed,
        /// and the test would be measuring nothing if it were nought.
        /// </para>
        /// </remarks>
        [Fact]
        public void AWingOrderedTogetherGetsACellApiece()
        {
            BattleState battle = Load("greatfield");

            GridMode.Muster(battle);

            Board board = Board.For(battle);

            List<UnitInstance> everybody = battle.UnitsOnField().ToList();

            List<UnitInstance> wing = everybody.Take(6).ToList();
            List<UnitInstance> others = everybody.Skip(6).Take(6).ToList();

            foreach (UnitInstance unit in wing) unit.Bond = -1;

            // Marched right on top of six regiments outside the wing, so several
            // of them must give way.
            Vec2 origin = Vec2.Zero;
            foreach (UnitInstance unit in wing) origin += unit.Position;
            origin /= wing.Count;

            Vec2 onto = others[0].Position;

            var wanted = new Vec2[wing.Count];
            for (int i = 0; i < wing.Count; i++) wanted[i] = onto + (wing[i].Position - origin);

            Vec2[] formed = board.FormUpAt(battle, wing, wanted, GridMode.ShufflesWithinRings);

            var cells = new List<Coord>();
            int gaveWay = 0;

            for (int i = 0; i < formed.Length; i++)
            {
                Coord asked = board.Of(wanted[i]);
                Coord given = board.Of(formed[i]);

                if (asked != given) gaveWay++;

                cells.Add(given);
            }

            _out.WriteLine($"{wing.Count} regiments formed up, {gaveWay} had to give way");
            _out.WriteLine($"cells: {string.Join(", ", cells)}");

            // Every one of them has ground of its own.
            Assert.Equal(cells.Count, cells.Distinct().Count());

            // And none of them was put on top of somebody outside the wing.
            var outsiders = new HashSet<Coord>(
                everybody.Where(u => u.Bond != -1).Select(u => board.Of(u.Position)));

            foreach (Coord cell in cells)
                Assert.DoesNotContain(cell, outsiders);

            Assert.True(gaveWay > 0, "nothing had to give way, so this arrangement is not testing the shove.");
        }
    }
}
