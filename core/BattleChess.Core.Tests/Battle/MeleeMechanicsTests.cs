using System.Collections.Generic;
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
            (float alone, _) = SetUpon(attackers: 1);
            (float surrounded, _) = SetUpon(attackers: 2);

            Assert.True(surrounded >= 1.8f * alone,
                $"Being taken by two regiments at once should be far worse than by one: " +
                $"one enemy cost {alone:0.0}%, two cost {surrounded:0.0}%.");
        }

        [Fact]
        public void BeingSurroundedGetsWorseTheLongerItLasts()
        {
            (float earlyAlone, _) = SetUpon(attackers: 1, pulses: 4);
            (float earlySurrounded, _) = SetUpon(attackers: 2, pulses: 4);

            (float lateAlone, _) = SetUpon(attackers: 1, pulses: 12);
            (float lateSurrounded, _) = SetUpon(attackers: 2, pulses: 12);

            float early = earlySurrounded / earlyAlone;
            float late = lateSurrounded / lateAlone;

            Assert.True(late > early,
                $"A regiment fighting on two sides comes apart, and losing cohesion makes it worse at " +
                $"everything — so the disadvantage should compound rather than hold steady: " +
                $"x{early:0.00} after four pulses, x{late:0.00} after twelve.");
        }

        [Fact]
        public void ConcentratingOnOneRegimentBeatsIt()
        {
            (float defenderLost, float attackerLost) = SetUpon(attackers: 3);

            Assert.True(defenderLost > attackerLost,
                $"Three regiments falling on one must come out ahead. A defender has one frontage and has " +
                $"to divide it among everyone it fights — if it can bring its whole line against each of " +
                $"them at once it wins by being outnumbered, which is nonsense. " +
                $"Defender lost {defenderLost:0.0}%, each attacker averaged {attackerLost:0.0}%.");
        }

        [Fact]
        public void ADefenderCannotFightEveryoneAtFullStrength()
        {
            (_, float againstOne) = SetUpon(attackers: 1);
            (_, float againstThree) = SetUpon(attackers: 3);

            Assert.True(againstThree < againstOne,
                $"Splitting its frontage three ways must cost the defender its punch against any one of " +
                $"them: a lone attacker lost {againstOne:0.0}%, each of three lost {againstThree:0.0}%.");
        }

        /// <summary>
        /// Sets one, two or three regiments on a single defender and reports
        /// what the defender lost and what its attackers lost on average.
        /// </summary>
        /// <remarks>
        /// Reports both sides deliberately. Measuring only the defender hides
        /// the failure this is guarding: a defender fighting everyone at full
        /// frontage still takes heavy losses, while quietly dealing several
        /// times what it should.
        /// </remarks>
        private static (float DefenderLost, float AttackerLost) SetUpon(int attackers, int pulses = 8)
        {
            var field = new Battlefield("plains", 7000, RuleSet.MeleeOnly);

            UnitInstance target = field.Add(1, "swordsmen", field.Centre, Facing.East);
            float reach = target.Footprint.Depth + 4f;

            // Front, flank and rear — which is what falling on one regiment with
            // three actually means, and where its advantage is supposed to come
            // from.
            var placements = new[]
            {
                (new Vec2(reach, 0f), Facing.West),
                (new Vec2(0f, reach), Facing.South),
                (new Vec2(-reach, 0f), Facing.East),
            };

            var attacking = new List<UnitInstance>();

            for (int i = 0; i < attackers; i++)
            {
                (Vec2 offset, Facing facing) = placements[i];
                attacking.Add(field.Add(0, "swordsmen", field.Centre + offset, facing));
            }

            field.RunPulses(pulses);

            float attackerLost = 0f;
            foreach (UnitInstance unit in attacking) attackerLost += Battlefield.LostPercent(unit);

            return (Battlefield.LostPercent(target), attackerLost / attackers);
        }

        // ---- Sending more troops must not make an attack weaker --------------

        [Fact]
        public void RegimentsMarchingTogetherKeepTheirOrder()
        {
            var field = new Battlefield("plains", 7100);

            UnitInstance left = field.Add(0, "swordsmen", field.Centre - new Vec2(300f, 30f), Facing.East);
            UnitInstance right = field.Add(0, "swordsmen", field.Centre - new Vec2(300f, -30f), Facing.East);

            // Close enough together that their footprints overlap the whole way,
            // which is exactly what happens when two regiments are ordered onto
            // the same enemy.
            field.March(left, field.Centre + new Vec2(400f, 0f));
            field.March(right, field.Centre + new Vec2(400f, 0f));

            field.RunTurns(6);

            Assert.True(left.Organization >= 0.8f && right.Organization >= 0.8f,
                $"Marching together is a column, not a collision. Charging it as one drained a quarter of " +
                $"their cohesion every turn, so any attack pressed by more than one regiment arrived with " +
                $"nothing left — sending more troops made the attack weaker. " +
                $"Left {left.Organization:0.00}, right {right.Organization:0.00}.");
        }

        [Fact]
        public void SendingMoreRegimentsMakesAnAttackCheaper()
        {
            float alone = CostOfAttacking(regiments: 1);
            float together = CostOfAttacking(regiments: 3);

            Assert.True(together < alone * 0.6f,
                $"Three regiments on one enemy should each pay far less than one paying alone: " +
                $"single attacker lost {alone:0.0}%, each of three lost {together:0.0}%.");
        }

        /// <summary>
        /// Orders a number of regiments onto one standing enemy from 250 m and
        /// reports the average cost per attacker.
        /// </summary>
        private static float CostOfAttacking(int regiments)
        {
            var field = new Battlefield("plains", 4400);

            UnitInstance foe = field.Add(1, "cavalry", field.Centre, Facing.West);
            Battlefield.Hold(foe);

            var pool = new[] { ("cavalry", 0f), ("swordsmen", 130f), ("swordsmen", -130f) };
            var mine = new List<UnitInstance>();

            for (int i = 0; i < regiments; i++)
                mine.Add(field.Add(0, pool[i].Item1, field.Centre - new Vec2(250f, pool[i].Item2), Facing.East));

            foreach (UnitInstance unit in mine) Battlefield.Press(unit, foe);

            field.RunUntilDecided(15, foe);

            float lost = 0f;
            foreach (UnitInstance unit in mine) lost += Battlefield.LostPercent(unit);

            return lost / regiments;
        }

        [Fact]
        public void AStandingRegimentDoesNotGetAChargeBonus()
        {
            // Cavalry has the largest charge bonus on the field, so if a charge
            // can be collected by standing still this is where it shows.
            float whenCharged = FirstPulseAgainst(chargeHome: true);
            float whenStanding = FirstPulseAgainst(chargeHome: false);

            Assert.True(whenStanding < whenCharged,
                $"A charge is something a regiment does, not something that happens to it. Awarding it to " +
                $"both sides of a fresh contact meant a defender collected a full charge every time somebody " +
                $"walked into it — and a fresh one from each attacker as they arrived. " +
                $"Charging home dealt {whenCharged:0.0}%, standing still dealt {whenStanding:0.0}%.");
        }

        /// <summary>
        /// One pulse of cavalry against infantry, with the horsemen either
        /// riding in or already stood there.
        /// </summary>
        private static float FirstPulseAgainst(bool chargeHome)
        {
            var field = new Battlefield("plains", 7200, RuleSet.MeleeOnly);

            UnitInstance horse = field.Add(0, "cavalry", field.Centre, Facing.East);
            UnitInstance foot = field.Add(1, "swordsmen",
                field.Centre + new Vec2(horse.Footprint.Depth + 4f, 0f), Facing.West);

            if (chargeHome) Battlefield.Press(horse, foot); else Battlefield.Hold(horse);
            Battlefield.Hold(foot);

            field.RunPulses(1);

            return Battlefield.LostPercent(foot);
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
