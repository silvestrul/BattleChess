using BattleChess.Contracts;
using BattleChess.Rules;
using Xunit;

namespace BattleChess.Tests.Battle
{
    /// <summary>
    /// Riding through a formation: what it costs the men who are ridden
    /// through, and what a second and third pass add.
    /// </summary>
    /// <remarks>
    /// Combat settles on a pulse every ten ticks, and cavalry covers 48 m in
    /// that time against a contact window barely 29 m wide. So a charge could
    /// cross a regiment entirely between two pulses and come out the far side
    /// without a blow being struck — three passes at a body of swordsmen cost
    /// them exactly what one did. Contact is now noticed every tick and settled
    /// on the pulse, which is what these guard.
    /// </remarks>
    public sealed class ChargeTests
    {
        [Fact]
        public void RidingThroughARegimentCostsItSomething()
        {
            (float lost, float _, UnitState _) = RideThrough(passes: 1);

            Assert.True(lost > 0f, "Cavalry riding through a line must cost it men.");
        }

        [Fact]
        public void EachChargeCostsThemAgain()
        {
            (float once, float _, UnitState _) = RideThrough(passes: 1);
            (float thrice, float _, UnitState _) = RideThrough(passes: 3);

            Assert.True(thrice >= 2f * once,
                $"Three passes must cost far more than one. Sampling contact only on the pulse let a fast " +
                $"regiment cross the whole contact zone between two of them, so repeat charges were free: " +
                $"one pass cost {once:0.0}%, three cost {thrice:0.0}%.");
        }

        [Fact]
        public void RepeatedChargesBreakARegiment()
        {
            (float _, float organization, UnitState state) = RideThrough(passes: 3);

            Assert.True(organization <= 0.35f,
                $"A regiment ridden through three times should be in pieces, not merely bruised — " +
                $"organization {organization:0.00}.");

            Assert.True(state == UnitState.Routing || state == UnitState.Wavering || state == UnitState.Scattered,
                $"And it should have broken, or be close to it. It is {state}.");
        }

        [Fact]
        public void AChargeTearsAtTheFormationItLandsOn()
        {
            ClashResult charged = new Clash
            { Attacker = "cavalry", Defender = "swordsmen", AttackerCharges = true, Pulses = 2 }.Run();

            Assert.True(charged.Defender.Organization < 1f,
                $"A charge landing must cost cohesion, not only men — casualties alone never explained why " +
                $"horsemen are worth three times their number. Organization was {charged.Defender.Organization:0.00}.");
        }

        [Fact]
        public void ChargingTheRearIsFarWorseThanChargingTheFront()
        {
            float front = new Clash { Attacker = "cavalry", Defender = "swordsmen",
                                      DefenderFacingDegrees = 180f, Pulses = 6 }.Run().DefenderLost;

            float rear = new Clash { Attacker = "cavalry", Defender = "swordsmen",
                                     DefenderFacingDegrees = 0f, Pulses = 6 }.Run().DefenderLost;

            Assert.True(rear >= 2f * front,
                $"Horse taking a regiment from behind should be decisive: front cost it {front:0.0}%, " +
                $"rear {rear:0.0}%.");
        }

        /// <summary>
        /// Marches cavalry back and forth through a standing body of infantry,
        /// as a plain move order rather than an attack.
        /// </summary>
        private static (float Lost, float Organization, UnitState State) RideThrough(int passes)
        {
            var field = new Battlefield("plains", 9800);

            UnitInstance foot = field.Add(1, "swordsmen", field.Centre, Facing.West);
            Battlefield.Hold(foot);

            UnitInstance horse = field.Add(0, "cavalry", field.Centre - new Vec2(200f, 0f), Facing.East);

            for (int pass = 0; pass < passes; pass++)
            {
                float side = pass % 2 == 0 ? 1f : -1f;

                field.March(horse, field.Centre + new Vec2(200f * side, 0f));
                field.RunTurns(3);
            }

            return (Battlefield.LostPercent(foot), foot.Organization, foot.State);
        }
    }
}
