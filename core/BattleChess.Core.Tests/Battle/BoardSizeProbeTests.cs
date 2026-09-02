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
                    $"{board.CellWidth:0} m hex.");
            }

            _out.WriteLine(
                $"{field,-16} widest {widestOne}: {widestShape.Width:0.0} x {widestShape.Depth:0.0} m, " +
                $"{widest:0.0} m across -> {board.CellWidth:0} m hexes, " +
                $"{board.ShortSideInHexes:0} across the short side");
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
    }
}
