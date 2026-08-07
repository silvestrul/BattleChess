using System;
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

            // Watched across the crossing rather than read at the end of it.
            // The wall is at its worst wading and just after, and a regiment
            // left alone on the far bank puts itself back together — so the
            // moment cavalry wants is while the water is still behind them.
            float weakest = spearmen.EffectiveStoppingPower;
            float lowestOrganization = spearmen.Organization;

            for (int turn = 0; turn < 8; turn++)
            {
                field.RunTurns(1);

                if (spearmen.EffectiveStoppingPower < weakest)
                {
                    weakest = spearmen.EffectiveStoppingPower;
                    lowestOrganization = spearmen.Organization;
                }
            }

            Assert.True(weakest < horse.EffectiveBreakthrough,
                $"A phalanx that has waded a river is a hedge of spears in name only, and horse should " +
                $"ride straight through it — which is precisely why you make it cross first. " +
                $"Stopping {weakest:0.00} against breakthrough " +
                $"{horse.EffectiveBreakthrough:0.00}, at organization {lowestOrganization:0.00}.");
        }

        [Fact]
        public void AFormationReformsOnTheFarBankIfItIsGivenTime()
        {
            var field = new Battlefield("plains", 12200, RuleSet.Full, canvas =>
                canvas.Band(canvas.Columns / 2, canvas.Columns / 2 + 1, "river"));

            UnitInstance unit = field.Add(0, "swordsmen", field.Centre - new Vec2(200f, 0f), Facing.East);

            field.March(unit, field.Centre + new Vec2(150f, 0f));

            float worst = 1f;
            for (int turn = 0; turn < 8; turn++)
            {
                field.RunTurns(1);
                worst = MathF.Min(worst, unit.Organization);
            }

            field.RunTurns(6);

            Assert.True(unit.Organization > worst + 0.05f,
                $"Wading a river must cost order at the time and not for the rest of the battle. It was " +
                $"down to {worst:0.00} crossing and sat at {unit.Organization:0.00} six turns later — men " +
                "dress their ranks once they are out of the water.");
        }

        /// <summary>
        /// Marches a regiment across a two-cell band of the given ground and
        /// reports the worst state it was in along the way.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A band, not a whole map. Real rivers are fifty metres across, and
        /// filling the field with one measured a march no army would ever make.
        /// </para>
        /// <para>
        /// The low-water mark rather than the final figure, because that is what
        /// the rule is actually about: a regiment is at its worst as it comes
        /// out the far side, which is precisely when a waiting enemy hits it.
        /// Reading the number at the end of the march measures how long it was
        /// given to recover instead.
        /// </para>
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

            float worst = unit.Organization;

            for (int turn = 0; turn < 8; turn++)
            {
                field.RunTurns(1);
                worst = MathF.Min(worst, unit.Organization);
            }

            return worst;
        }
    }
}
