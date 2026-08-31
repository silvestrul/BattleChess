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
    /// The largest scenario in content, checked for the things that would make
    /// it useless quietly.
    /// </summary>
    public sealed class GreatFieldTests
    {
        private readonly ITestOutputHelper _out;
        public GreatFieldTests(ITestOutputHelper output) => _out = output;

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

        [Fact]
        public void FortyThousandASideActuallyTakesTheField()
        {
            BattleState battle = Load();

            var strength = new Dictionary<PlayerId, int>();
            var regiments = new Dictionary<PlayerId, int>();
            var byType = new Dictionary<string, int>();

            foreach (UnitInstance unit in battle.UnitsOnField())
            {
                strength.TryGetValue(unit.Owner, out int men);
                strength[unit.Owner] = men + unit.InitialStrength;

                regiments.TryGetValue(unit.Owner, out int count);
                regiments[unit.Owner] = count + 1;

                byType.TryGetValue(unit.Def.Key, out int t);
                byType[unit.Def.Key] = t + unit.InitialStrength;
            }

            foreach (var side in strength)
                _out.WriteLine($"army {side.Key.Value}: {side.Value} men in {regiments[side.Key]} regiments");

            foreach (var type in byType)
                _out.WriteLine($"  {type.Key,-13} {type.Value} men across both sides");

            // **Forty thousand WORTH, not forty thousand men** - [M133]. This
            // test used to assert men, and in doing so locked the bug in: it
            // required every regiment to be two thousand bodies, which for
            // cavalry is 5,72 times the rectangle a regiment is meant to cover
            // and put 229 x 114 m of horse on the field beside 80 x 40 m of
            // foot. The line under it even said so - "the whole point of raising
            // the ceiling: without it every horse regiment would have been
            // silently trimmed to 700 or 800" - which is the catalogue's cap
            // doing exactly its job, read as an obstacle.
            //
            // What the field claims in its own title is forty thousand a side.
            // costPerMan is what makes that a claim about fighting weight rather
            // than about headcount, so that is what is asked for here.
            var worth = new Dictionary<PlayerId, double>();

            foreach (UnitInstance unit in battle.UnitsOnField())
            {
                worth.TryGetValue(unit.Owner, out double had);
                worth[unit.Owner] =
                    had + unit.InitialStrength * unit.Def.Get(UnitAttributes.CostPerMan);
            }

            foreach (var side in worth)
                _out.WriteLine($"army {side.Key.Value}: {side.Value:N0} worth");

            foreach (var side in worth)
            {
                // Within a tenth of a percent: costPerMan is authored to two
                // decimals, so 2,86 against a true 1000/350 leaves cavalry six
                // worth heavy over three regiments.
                Assert.InRange(side.Value, 39_960d, 40_040d);
                Assert.Equal(20, regiments[side.Key]);
            }

            // Each unit type at its own cap, which is 2000 / costPerMan and the
            // one strength at which every regiment is the same rectangle.
            Assert.Equal(4200, byType["cavalry"]);        // 6 x 700
            Assert.Equal(3200, byType["horsearchers"]);   // 4 x 800
            Assert.Equal(32000, byType["spearmen"]);      // 16 x 2000
            Assert.Equal(22400, byType["swordsmen"]);     // 14 x 1600

            // And they really are one rectangle. BattleGroundTests holds this
            // for every field; said again here because it is the property the
            // assertions above exist to protect, and a reader of this file
            // should not have to go and find that out.
            var shapes = new HashSet<string>();

            foreach (UnitInstance unit in battle.UnitsOnField())
                shapes.Add($"{unit.Def.FootprintAt(unit.InitialStrength)}");

            Assert.Single(shapes);
            _out.WriteLine($"every regiment: {shapes.First()}");
        }

        [Fact]
        public void NobodyIsDeployedInsideAnybodyElse()
        {
            BattleState battle = Load();

            var all = new List<UnitInstance>(battle.UnitsOnField());
            var clashes = new List<string>();

            for (int i = 0; i < all.Count; i++)
            for (int j = i + 1; j < all.Count; j++)
            {
                if (!OrientedRect.Overlaps(all[i].Shape, all[j].Shape)) continue;

                clashes.Add(
                    $"{all[i].Def.DisplayName} at ({all[i].Position.X:0},{all[i].Position.Y:0}) " +
                    $"overlaps {all[j].Def.DisplayName} at ({all[j].Position.X:0},{all[j].Position.Y:0})");
            }

            foreach (string clash in clashes) _out.WriteLine(clash);

            Assert.True(clashes.Count == 0, $"{clashes.Count} regiments deployed inside another.");
        }

        [Fact]
        public void EveryRegimentStandsOnGroundItCanHold()
        {
            BattleState battle = Load();
            var stuck = new List<string>();

            foreach (UnitInstance unit in battle.UnitsOnField())
            {
                if (battle.FormationFits(unit, unit.Position, unit.Facing)) continue;

                stuck.Add($"{unit.Def.DisplayName} at ({unit.Position.X:0},{unit.Position.Y:0})");
            }

            foreach (string s in stuck) _out.WriteLine(s);

            Assert.True(stuck.Count == 0, $"{stuck.Count} regiments cannot stand where they are deployed.");
        }
    }
}
