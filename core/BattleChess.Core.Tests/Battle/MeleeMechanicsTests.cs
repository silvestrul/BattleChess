using BattleChess.Contracts;
using BattleChess.Rules;
using Xunit;

namespace BattleChess.Tests.Battle
{
    /// <summary>
    /// The rules underneath the counters: frontage, flanking, the charge,
    /// armour, ground and cohesion.
    /// </summary>
    /// <remarks>
    /// These use <see cref="Clash"/> — two regiments nailed in place with only
    /// melee and morale running — and, where the question needs it, units built
    /// for the test that differ in exactly one stat. Comparing two real units
    /// would be measuring five things at once.
    /// </remarks>
    public sealed class MeleeMechanicsTests
    {
        // ---- Frontage: the rule that makes manoeuvre worth anything ---------

        [Fact]
        public void NumbersCountUntilTheyRunOutOfRoom()
        {
            // Below the shared contact width, men translate into damage as you
            // would expect: twice the regiment, twice the killing.
            float small = DamageDealtBy(200);
            float medium = DamageDealtBy(400);

            Assert.True(medium >= 1.7f * small,
                $"Under the frontage cap numbers should still tell: 200 men dealt {small:0.0}%, 400 dealt {medium:0.0}%.");
        }

        [Fact]
        public void FrontageCapsWhatExtraNumbersAreWorth()
        {
            // Past it they stop. A regiment two and a half times the size has
            // no more room to fight in, so the overhang stands in a field.
            float medium = DamageDealtBy(400);
            float huge = DamageDealtBy(1000);

            Assert.True(huge <= 1.3f * medium,
                $"2.5x the men should not be 2.5x the damage once frontage is full: " +
                $"400 men dealt {medium:0.0}%, 1000 dealt {huge:0.0}%.");
        }

        private static float DamageDealtBy(int strength) =>
            new Clash
            {
                Attacker = "swordsmen",
                AttackerStrength = strength,
                Defender = "spearmen",
                DefenderStrength = 600,
                Pulses = 4,
            }.Run().DefenderLost;

        // ---- Flanking --------------------------------------------------------

        [Fact]
        public void AttackingTheRearHurtsFarMoreThanTheFront()
        {
            float front = LossesFacing(180f);
            float rear = LossesFacing(0f);

            Assert.True(rear >= 1.5f * front,
                $"Taking a regiment in the back should be decisive: front cost it {front:0.0}%, rear {rear:0.0}%.");
        }

        [Fact]
        public void AFlankSitsBetweenTheFrontAndTheRear()
        {
            float front = LossesFacing(180f);
            float flank = LossesFacing(90f);
            float rear = LossesFacing(0f);

            Assert.True(front < flank && flank < rear,
                $"Damage should rise smoothly round the angle, with no cliff: " +
                $"front {front:0.0}%, flank {flank:0.0}%, rear {rear:0.0}%.");
        }

        [Fact]
        public void BeingFlankedIsFrighteningBeyondTheCasualties()
        {
            ClashResult front = new Clash { DefenderFacingDegrees = 180f }.Run();
            ClashResult rear = new Clash { DefenderFacingDegrees = 0f }.Run();

            float moraleLostToTheFront = 1f - front.DefenderMorale;
            float moraleLostToTheRear = 1f - rear.DefenderMorale;
            float casualtyRatio = rear.DefenderLost / front.DefenderLost;
            float moraleRatio = moraleLostToTheRear / moraleLostToTheFront;

            Assert.True(moraleRatio > casualtyRatio,
                $"A rear attack should cost more morale than its casualties alone explain: " +
                $"casualties x{casualtyRatio:0.00}, morale x{moraleRatio:0.00}.");
        }

        private static float LossesFacing(float degrees) =>
            new Clash { Attacker = "swordsmen", Defender = "swordsmen", DefenderFacingDegrees = degrees }.Run().DefenderLost;

        // ---- The charge ------------------------------------------------------

        [Fact]
        public void AChargeLandsHarderThanTheGrindThatFollows()
        {
            UnitDef charging = Line().With(UnitAttributes.ChargeBonus, 1.5f).Build("charging");
            UnitDef plodding = Line().Build("plodding");

            float withCharge = OnePulseAgainstAStandard(charging);
            float without = OnePulseAgainstAStandard(plodding);

            Assert.True(withCharge >= 1.3f * without,
                $"The moment of impact should be worth something: charge dealt {withCharge:0.0}%, " +
                $"the same unit without one dealt {without:0.0}%.");
        }

        [Fact]
        public void AChargeIsSpentOnceNotEveryPulse()
        {
            UnitDef charging = Line().With(UnitAttributes.ChargeBonus, 1.5f).Build("charging");

            float firstPulse = OnePulseAgainstAStandard(charging);
            float sixPulses = PulsesAgainstAStandard(charging, 6);

            Assert.True(sixPulses < 6f * firstPulse,
                $"Cavalry denied its charge is meant to be mediocre — the bonus cannot repeat every pulse: " +
                $"one pulse {firstPulse:0.0}%, six pulses {sixPulses:0.0}%.");
        }

        // ---- Armour, ground and class edges ----------------------------------

        [Fact]
        public void ArmourReducesLosses()
        {
            UnitDef bare = Line().With(UnitAttributes.Armour, 0f).Build("bare");
            UnitDef mailed = Line().With(UnitAttributes.Armour, 0.5f).Build("mailed");

            float unarmoured = LossesDefendedBy(bare);
            float armoured = LossesDefendedBy(mailed);

            Assert.True(armoured <= 0.7f * unarmoured,
                $"Half armour should show plainly: bare lost {unarmoured:0.0}%, mailed {armoured:0.0}%.");
        }

        [Fact]
        public void AClassEdgeMakesTheDifferenceItSays()
        {
            UnitDef plain = Line().Build("plain");
            UnitDef specialist = Line().Against(UnitClass.Sword, 2f).Build("specialist");

            float ordinary = OnePulseAgainstAStandard(plain);
            float edged = OnePulseAgainstAStandard(specialist);

            Assert.True(edged >= 1.6f * ordinary,
                $"Doubling the multiplier against a class should roughly double the damage: " +
                $"plain {ordinary:0.0}%, specialist {edged:0.0}%.");
        }

        [Fact]
        public void HighGroundIsWorthHolding()
        {
            float onTheFlat = new Clash { Ground = "plains" }.Run().DefenderLost;
            float onTheHill = new Clash { Ground = "hill" }.Run().DefenderLost;

            Assert.True(onTheHill < onTheFlat,
                $"Defending a hill should cost less than defending a field: hill {onTheHill:0.0}%, plains {onTheFlat:0.0}%.");
        }

        [Fact]
        public void BeingCaughtMidCrossingIsTheWorstPlaceOnTheField()
        {
            float onTheFlat = new Clash { Ground = "plains" }.Run().DefenderLost;
            float inTheWater = new Clash { Ground = "river" }.Run().DefenderLost;

            Assert.True(inTheWater > onTheFlat,
                $"A river should be worse to be caught in than open ground: river {inTheWater:0.0}%, plains {onTheFlat:0.0}%.");
        }

        // ---- Cohesion --------------------------------------------------------

        [Fact]
        public void ADisorderedRegimentFightsWorse()
        {
            float fresh = new Clash { AttackerOrganization = 1f, Pulses = 4 }.Run().DefenderLost;
            float shaken = new Clash { AttackerOrganization = 0.2f, Pulses = 4 }.Run().DefenderLost;

            Assert.True(shaken <= 0.65f * fresh,
                $"Losing formation should cost real hitting power: fresh dealt {fresh:0.0}%, disordered {shaken:0.0}%.");
        }

        [Fact]
        public void ADisorderedRegimentIsAlsoEasierToKill()
        {
            float fresh = new Clash { DefenderOrganization = 1f, Pulses = 4 }.Run().DefenderLost;
            float shaken = new Clash { DefenderOrganization = 0.2f, Pulses = 4 }.Run().DefenderLost;

            Assert.True(shaken >= 1.3f * fresh,
                $"A formation that has come apart should be cut up: fresh lost {fresh:0.0}%, disordered {shaken:0.0}%.");
        }

        [Fact]
        public void AShakenRegimentHitsSofter()
        {
            float steady = DamageFromAWaveringAttacker(steadyInstead: true);
            float wavering = DamageFromAWaveringAttacker(steadyInstead: false);

            Assert.True(wavering < steady,
                $"Men who have half broken should not fight as hard: steady dealt {steady:0.0}%, wavering {wavering:0.0}%.");
        }

        private static float DamageFromAWaveringAttacker(bool steadyInstead)
        {
            var field = new Battlefield("plains", 8100, RuleSet.MeleeOnly);

            UnitInstance attacker = field.Add(0, "swordsmen", field.Centre, Facing.East);
            UnitInstance defender = field.Add(1, "swordsmen",
                field.Centre + new Vec2(attacker.Footprint.Depth + 4f, 0f), Facing.West);

            if (!steadyInstead)
            {
                // Just above the routing threshold, so the state holds for the
                // whole exchange instead of the morale system tidying it away.
                attacker.State = UnitState.Wavering;
                attacker.Morale = 0.45f;
            }

            field.RunPulses(3);

            return Battlefield.LostPercent(defender);
        }

        // ---- Concentration ---------------------------------------------------

        [Fact]
        public void TwoRegimentsOnOneIsWorseThanTheNumbersSuggest()
        {
            float alone = new Clash { Pulses = 4 }.Run().DefenderLost;
            float surrounded = LossesWhenSetUpon();

            Assert.True(surrounded >= 1.8f * alone,
                $"Being taken by two regiments at once should be more than twice as bad — the second one " +
                $"comes in off the flank: one enemy cost {alone:0.0}%, two cost {surrounded:0.0}%.");
        }

        private static float LossesWhenSetUpon()
        {
            var field = new Battlefield("plains", 7000, RuleSet.MeleeOnly);

            UnitInstance target = field.Add(1, "swordsmen", field.Centre, Facing.East);
            float reach = target.Footprint.Depth + 4f;

            // One in front, one on the flank — which is what two attackers on
            // one defender actually means, and why it is so much worse.
            field.Add(0, "swordsmen", field.Centre + new Vec2(reach, 0f), Facing.West);
            field.Add(0, "swordsmen", field.Centre + new Vec2(0f, reach), Facing.South);

            field.RunPulses(4);

            return Battlefield.LostPercent(target);
        }

        // ---- Shared scaffolding ---------------------------------------------

        /// <summary>A plain infantry regiment with nothing special about it.</summary>
        private static SyntheticUnit Line() =>
            new SyntheticUnit()
                .With(UnitAttributes.Attack, 1f)
                .With(UnitAttributes.Defence, 1f)
                .With(UnitAttributes.Armour, 0f)
                .With(UnitAttributes.Morale, 1f);

        private static float OnePulseAgainstAStandard(UnitDef attacker) => PulsesAgainstAStandard(attacker, 1);

        private static float PulsesAgainstAStandard(UnitDef attacker, int pulses) =>
            new Clash
            {
                AttackerDef = attacker,
                DefenderDef = Line().Build("standard"),
                Pulses = pulses,
                Seed = 8000,
            }.Run().DefenderLost;

        private static float LossesDefendedBy(UnitDef defender) =>
            new Clash
            {
                AttackerDef = Line().Build("standard"),
                DefenderDef = defender,
                Pulses = 3,
                Seed = 8000,
            }.Run().DefenderLost;
    }
}
