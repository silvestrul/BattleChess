using BattleChess.Contracts;
using BattleChess.Rules;
using Xunit;

namespace BattleChess.Tests.Battle
{
    /// <summary>
    /// What bad ground does to a formation that crosses it.
    /// </summary>
    /// <remarks>
    /// Rough country is not merely slow. Men wade at their own pace, files break
    /// to go round obstacles, and what comes out the far side is a crowd facing
    /// roughly the right way. The units that live by cohesion suffer most, and
    /// that falls out with no special case at all — a spear wall's stopping
    /// power is its base figure times its formation times its condition, and the
    /// last two both answer to organization.
    /// </remarks>
    public sealed class TerrainDisorderTests
    {
        [Fact]
        public void CrossingOpenGroundCostsNoOrder()
        {
            Assert.Equal(1f, OrganizationAfterCrossing("plains"), 2);
            Assert.Equal(1f, OrganizationAfterCrossing("road"), 2);
        }

        [Theory]
        [InlineData("river")]
        [InlineData("mountain")]
        [InlineData("swamp")]
        [InlineData("forest")]
        public void BrokenGroundDisordersAFormationThatCrossesIt(string ground)
        {
            float after = OrganizationAfterCrossing(ground);

            Assert.True(after < 0.97f,
                $"Marching through {ground} should pull a formation out of shape — organization {after:0.00}.");
        }

        [Fact]
        public void ARiverIsWorseForOrderThanAWood()
        {
            float wood = OrganizationAfterCrossing("forest");
            float water = OrganizationAfterCrossing("river");

            Assert.True(water < wood,
                $"Wading costs more order than picking through trees: forest {wood:0.00}, river {water:0.00}.");
        }

        [Fact]
        public void APhalanxThatHasCrossedARiverStopsBeingAPhalanx()
        {
            var field = new Battlefield("plains", 12000, RuleSet.Full, canvas =>
                canvas.Band(canvas.Columns / 2 - 1, canvas.Columns / 2, "river"));

            UnitInstance spearmen = field.Add(0, "spearmen", field.Centre - new Vec2(250f, 0f), Facing.East);
            UnitInstance horse = field.Add(1, "cavalry", field.Centre + new Vec2(400f, 0f), Facing.West);

            Assert.True(spearmen.EffectiveStoppingPower > horse.EffectiveBreakthrough,
                "Fresh, a spear wall must refuse cavalry the line.");

            field.March(spearmen, field.Centre + new Vec2(150f, 0f));
            field.RunTurns(8);

            Assert.True(spearmen.EffectiveStoppingPower < horse.EffectiveBreakthrough,
                $"A phalanx that has waded a river is a hedge of spears in name only, and horse should " +
                $"ride straight through it — which is precisely why you make it cross first. " +
                $"Stopping {spearmen.EffectiveStoppingPower:0.00} against breakthrough " +
                $"{horse.EffectiveBreakthrough:0.00}, at organization {spearmen.Organization:0.00}.");
        }

        /// <summary>
        /// Marches a regiment across a two-cell band of the given ground and
        /// reports what it had left afterwards.
        /// </summary>
        /// <remarks>
        /// A band, not a whole map. Real rivers are fifty metres across, and
        /// filling the field with one measured a march no army would ever make.
        /// </remarks>
        private static float OrganizationAfterCrossing(string ground)
        {
            var field = new Battlefield("plains", 12100, RuleSet.Full, canvas =>
            {
                if (ground != "plains" && ground != "road")
                    canvas.Band(canvas.Columns / 2, canvas.Columns / 2 + 1, ground);
            });

            UnitInstance unit = field.Add(0, "swordsmen", field.Centre - new Vec2(200f, 0f), Facing.East);

            field.March(unit, field.Centre + new Vec2(150f, 0f));
            field.RunTurns(8);

            return unit.Organization;
        }
    }
}
