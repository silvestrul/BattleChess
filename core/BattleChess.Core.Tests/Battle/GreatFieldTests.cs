using System;
using System.Collections.Generic;
using System.IO;
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

            foreach (var side in strength)
            {
                Assert.Equal(40000, side.Value);
                Assert.Equal(20, regiments[side.Key]);
            }

            // The whole point of raising the ceiling: without it every horse
            // regiment would have been silently trimmed to 700 or 800.
            Assert.Equal(12000, byType["cavalry"]);
            Assert.Equal(8000, byType["horsearchers"]);
            Assert.Equal(32000, byType["spearmen"]);
            Assert.Equal(28000, byType["swordsmen"]);
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
