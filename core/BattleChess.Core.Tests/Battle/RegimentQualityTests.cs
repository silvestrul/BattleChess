using BattleChess.Contracts;
using BattleChess.Rules;
using Xunit;

namespace BattleChess.Tests.Battle
{
    /// <summary>
    /// No two regiments of the same troops are quite alike.
    /// </summary>
    public sealed class RegimentQualityTests
    {
        [Fact]
        public void TwoRegimentsOfTheSameTroopsDifferInSteadiness()
        {
            var field = new Battlefield("plains", 4200);

            UnitInstance first = field.Add(0, "spearmen", field.Centre, Facing.East);
            UnitInstance second = field.Add(0, "spearmen", field.Centre + new Vec2(200f, 0f), Facing.East);
            UnitInstance third = field.Add(0, "spearmen", field.Centre + new Vec2(400f, 0f), Facing.East);

            Assert.True(first.MoraleRating != second.MoraleRating || second.MoraleRating != third.MoraleRating,
                "Regiments should roll their own steadiness at muster, so a reserve is worth holding and " +
                "the regiment that held last time is worth remembering.");
        }

        [Fact]
        public void QualityStaysWithinTheDeclaredSpread()
        {
            var field = new Battlefield("plains", 4300);
            float spread = TestContent.Unit("spearmen").Get(UnitAttributes.QualitySpread);

            for (int i = 0; i < 20; i++)
            {
                UnitInstance unit = field.Add(0, "spearmen", field.Centre + new Vec2(i * 150f, 0f), Facing.East);

                Assert.InRange(unit.Quality, 1f - spread - 0.001f, 1f + spread + 0.001f);
            }
        }

        [Fact]
        public void TheSameSeedMustersTheSameRegiments()
        {
            float[] First() 
            {
                var field = new Battlefield("plains", 4400);
                var q = new float[4];
                for (int i = 0; i < 4; i++)
                    q[i] = field.Add(0, "spearmen", field.Centre + new Vec2(i * 150f, 0f), Facing.East).Quality;
                return q;
            }

            Assert.Equal(First(), First());
        }
    }
}
