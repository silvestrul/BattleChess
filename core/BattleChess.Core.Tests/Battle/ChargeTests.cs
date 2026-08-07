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
            (float lost, float _, float _) = RideThrough(passes: 1);

            Assert.True(lost > 0f, "Cavalry riding through a line must cost it men.");
        }

        [Fact]
        public void EachChargeCostsThemAgain()
        {
            (float once, float _, float _) = RideThrough(passes: 1);
            (float thrice, float _, float _) = RideThrough(passes: 3);

            Assert.True(thrice >= 2f * once,
                $"Three passes must cost far more than one. Sampling contact only on the pulse let a fast " +
                $"regiment cross the whole contact zone between two of them, so repeat charges were free: " +
                $"one pass cost {once:0.0}%, three cost {thrice:0.0}%.");
        }

        [Fact]
        public void RepeatedChargesBreakARegiment()
        {
            (float _, float organization, float morale) = RideThrough(passes: 3);

            Assert.True(organization <= 0.35f,
                $"A regiment ridden through three times should be in pieces, not merely bruised — " +
                $"organization {organization:0.00}.");

            // Asserted on morale rather than state. Damping shock by 30% was a
            // deliberate choice and it pulls directly against three charges
            // being enough to break a regiment outright — the cohesion collapse
            // above is the part that is genuinely the charge's doing.
            Assert.True(morale <= 0.8f,
                $"And it should be badly shaken by it — morale {morale:0.00}.");
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

            // Bands relaxed when morale shock was damped 30% on purpose. Men who
            // break later bleed longer, which compresses every ratio that ends
            // in somebody running away. Still plainly decisive.
            Assert.True(rear >= 1.8f * front,
                $"Horse taking a regiment from behind should be decisive: front cost it {front:0.0}%, " +
                $"rear {rear:0.0}%.");
        }

        // ---- A charge is spent, and has to be re-earned -------------------------
        //
        // The charge bonus was already meant to land once per engagement, and
        // did — but a contact was forgotten the instant it broke, so a regiment
        // that rode clean through bought a fresh one on the way out. Cavalry
        // overshot by fifty metres, wheeled a hundred and seventy degrees, came
        // back and charged again: four exchanges, two of them charges, and a
        // ten-to-one result that had nothing to do with the attacker being
        // stronger.

        [Fact]
        public void WheelingRoundAndComingStraightBackDoesNotBuyAnotherCharge()
        {
            int charges = ChargesLandedBouncing(overshootMetres: 60f, passes: 3);

            Assert.Equal(1, charges);
        }

        [Fact]
        public void BreakingOffProperlyAndReformingEarnsAFreshCharge()
        {
            Assert.True(ChargesLandedBouncing(overshootMetres: 260f, passes: 3) > 1,
                "A charge has to be re-earnable or cavalry becomes a one-shot weapon. Riding clear, " +
                "turning about and building to a gallop again is precisely what it should cost — the " +
                "fault was ever getting it for a fifty-metre bounce.");
        }

        /// <summary>
        /// Rides cavalry through a standing line and back again, overshooting
        /// by a given distance each time, and counts the charges that landed.
        /// </summary>
        private static int ChargesLandedBouncing(float overshootMetres, int passes)
        {
            var field = new Battlefield("plains", 9900);

            UnitInstance foot = field.Add(1, "swordsmen", field.Centre, Facing.West);
            Battlefield.Hold(foot);

            UnitInstance horse = field.Add(0, "cavalry", field.Centre - new Vec2(overshootMetres, 0f), Facing.East);

            for (int pass = 0; pass < passes; pass++)
            {
                float side = pass % 2 == 0 ? 1f : -1f;

                field.March(horse, field.Centre + new Vec2(overshootMetres * side, 0f));

                // Long enough to get there and settle, whichever distance it is.
                field.RunTurns(4);
            }

            return field.TimesSaid("Charge lands");
        }

        /// <summary>
        /// Marches cavalry back and forth through a standing body of infantry,
        /// as a plain move order rather than an attack.
        /// </summary>
        private static (float Lost, float Organization, float Morale) RideThrough(int passes)
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

            return (Battlefield.LostPercent(foot), foot.Organization, foot.Morale);
        }
    }
}
