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
    /// Every regiment on a field covers the same ground.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>[M133], and it is the designer's oldest rule about content:</b> a
    /// regiment is a rectangle, and a regiment of cavalry is never physically
    /// bigger than a regiment of spearmen. <c>units.cfg</c> has said so in its
    /// own header since it was written - <i>"EVERY REGIMENT COVERS THE SAME
    /// GROUND"</i> - and encodes it as <c>costPerMan</c>: a horseman is worth
    /// 2,86 footmen, so seven hundred riders fill the rectangle two thousand
    /// spearmen fill. Each unit's <c>maxStrength</c> is exactly
    /// <c>2000 / costPerMan</c>.
    /// </para>
    /// <para>
    /// <b>Nothing checked it, and every battle file in the project broke it.</b>
    /// The Great Field asked for two thousand of everything and raised its own
    /// <c>maxStrength</c> to be allowed to, which put <b>229 x 114 m</b> of
    /// cavalry on the field beside <b>80 x 40 m</b> of spearmen - horse at
    /// 2,86 times the frontage of foot. The bench trio was quieter and just as
    /// wrong at 2,19x. It was found by the designer looking at the map, which is
    /// the one place it was visible, and not by anything here.
    /// </para>
    /// <para>
    /// That matters beyond the look of it. Footprint drives the routing halo,
    /// the grid's cell size, the pose lattice's swept rectangle and the whole of
    /// [M132] - which concluded "the lattice cannot route a big regiment" while
    /// measuring bodies that were only big because of this.
    /// </para>
    /// </remarks>
    public sealed class BattleGroundTests
    {
        private readonly ITestOutputHelper _out;

        public BattleGroundTests(ITestOutputHelper output) => _out = output;

        /// <summary>
        /// Arrangements deliberately built out of more than one regiment size,
        /// where the rule has nothing to say.
        /// </summary>
        /// <remarks>
        /// Named rather than pattern-matched, so adding a field puts it under
        /// the rule by default and taking it out is a deliberate act with a
        /// reason beside it.
        /// </remarks>
        private static readonly HashSet<string> NotAnOrderOfBattle = new HashSet<string>
        {
            // Two spearmen regiments at 200 and 600, whose whole point is that
            // one is smaller than the other.
            "melee",

            // Carries a sixty-strong scout screen, and says why in its own
            // margin: "raised under strength to show sizes are free". An
            // arrangement demonstrating that a regiment may be raised small is
            // not an arrangement that forgot the rule.
            "ford",
        };

        public static IEnumerable<object[]> EveryBattleFile()
        {
            string battles = Path.Combine(TestContent.Root, "battles");

            foreach (string path in Directory.EnumerateFiles(battles, "*.battle.txt").OrderBy(p => p))
            {
                string key = Path.GetFileName(path).Replace(".battle.txt", string.Empty);

                if (!NotAnOrderOfBattle.Contains(key)) yield return new object[] { key };
            }
        }

        [Theory]
        [MemberData(nameof(EveryBattleFile))]
        public void EveryRegimentCoversTheSameGround(string field)
        {
            BattleState battle = BenchScenariosTests.Load(field);

            var byKind = new Dictionary<string, Footprint>();

            // The unit's *natural* rectangle, not the one its current formation
            // gives it. Formation is meant to change the shape - a square is
            // 14 m across where a line is 40 - and `charge` and `volley` exist
            // precisely to put the same regiment in different ones. Reading
            // UnitInstance.Footprint here failed all three of them and would
            // have been a test enforcing the opposite of a rule that is working.
            foreach (UnitInstance unit in battle.AllUnits)
                byKind[unit.Def.DisplayName] = unit.Def.FootprintAt(unit.InitialStrength);

            if (byKind.Count == 0) return;

            float widest = byKind.Values.Max(f => f.Width);
            float narrowest = byKind.Values.Min(f => f.Width);

            _out.WriteLine(field);

            foreach (KeyValuePair<string, Footprint> kind in byKind.OrderBy(k => k.Value.Width))
                _out.WriteLine($"  {kind.Key,-16}{kind.Value.Width,7:0.#} x {kind.Value.Depth:0.#} m");

            // A per-kind footprint is a single number for the whole kind, so a
            // field with one unit type cannot fail this and is not evidence of
            // anything. Said out loud, because a green row that could not have
            // gone red is the failure mode this whole file exists against (W9).
            if (byKind.Count < 2)
            {
                _out.WriteLine("  (one kind only - this row cannot fail)");
                return;
            }

            Assert.True(
                widest <= narrowest * 1.01f,
                $"On {field} the widest regiment is {widest:0} m across and the narrowest " +
                $"{narrowest:0} m, a spread of {widest / narrowest:0.00}x. Every regiment covers " +
                "the same ground: a battle file sets strength per unit type as " +
                "scale x (1000 / costPerMan) - spearmen 1000, swordsmen 800, archers 700, " +
                "scouts 500, horse archers 400, cavalry 350, artillery 175 - and never one flat " +
                "number for every regiment, which silently inflates the dear units.");
        }

        /// <summary>
        /// And the catalogue's own caps say the same thing, so the rule has one
        /// source rather than two that can drift apart.
        /// </summary>
        [Fact]
        public void EveryUnitsCapIsTheSameGroundToo()
        {
            var byKind = new Dictionary<string, Footprint>();

            foreach (UnitDef def in TestContent.Units.All)
                byKind[def.DisplayName] = def.FootprintAt(def.MaxStrength);

            foreach (KeyValuePair<string, Footprint> kind in byKind.OrderBy(k => k.Key))
                _out.WriteLine(
                    $"  {kind.Key,-16}{kind.Value.Width,7:0.#} x {kind.Value.Depth:0.#} m");

            float widest = byKind.Values.Max(f => f.Width);
            float narrowest = byKind.Values.Min(f => f.Width);

            Assert.True(byKind.Count > 1, "One unit type in the catalogue proves nothing.");

            Assert.True(
                widest <= narrowest * 1.01f,
                $"At their own maxStrength the widest unit is {widest:0} m across and the " +
                $"narrowest {narrowest:0} m, {widest / narrowest:0.00}x. maxStrength is meant to " +
                "be 2000 / costPerMan for every unit, so that a regiment at its cap is both the " +
                "same rectangle and the same worth as every other.");
        }
    }
}
