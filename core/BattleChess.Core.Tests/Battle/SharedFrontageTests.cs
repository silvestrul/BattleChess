using System;
using BattleChess.Contracts;
using BattleChess.Rules;
using Xunit;

namespace BattleChess.Tests.Battle
{
    /// <summary>
    /// One frontage, divided by the ground each enemy actually shares.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The rule the whole two-per-face arrangement exists to serve. A face is
    /// worth one full frontage of fighting however many regiments press it,
    /// because men are killed across the ground two bodies genuinely share and
    /// two attackers divide that ground between them. Attackers on sixty and
    /// forty metres of a hundred-metre front deal and take sixty and forty.
    /// </para>
    /// <para>
    /// What a second regiment buys is the defender's nerve, not his blood — it
    /// is set upon by two, which is frightening out of proportion to the
    /// casualties, and each attacker is steadier for having company. Those are
    /// tested next door in <see cref="MeetingTheEnemyTests"/>. Here the question
    /// is only whether the arithmetic of frontage adds up.
    /// </para>
    /// <para>
    /// Nothing pinned any of this until it was found to be badly wrong in two
    /// separate ways at once, so the numbers are asserted as totals rather than
    /// through who wins — a fight has too many other rules in it to isolate one.
    /// </para>
    /// </remarks>
    public sealed class SharedFrontageTests
    {
        /// <summary>
        /// What everyone brings and what the defender answers with, caught at
        /// the moment the attackers are all in contact.
        /// </summary>
        private static (int Attackers, int Defender) AtContact(ulong seed, params float[] offsets)
        {
            var field = new Battlefield("plains", seed);

            UnitInstance quarry = field.Add(1, "spearmen", field.Centre, Facing.West);
            Battlefield.Hold(quarry);

            var attackers = new UnitInstance[offsets.Length];

            for (int i = 0; i < offsets.Length; i++)
            {
                attackers[i] = field.Add(0, "swordsmen", field.Centre - new Vec2(240f, offsets[i]), Facing.East);
                Battlefield.Press(attackers[i], quarry);
            }

            field.RunUntil(() => quarry.EnemiesInContact >= offsets.Length, maxTurns: 12);

            Assert.True(quarry.EnemiesInContact >= offsets.Length,
                $"Only {quarry.EnemiesInContact} of {offsets.Length} ever reached it, so there is nothing to measure.");

            // These are questions about frontage, and frontage alone. By the
            // time everyone has closed, the regiments have also been fighting —
            // so they are carrying holes in their front ranks, and a defender
            // set upon by two is carrying twice as many. That is F13 working
            // exactly as it should, and it has nothing to do with how a front is
            // shared out. Left in, it turns every measurement here into two
            // things added together and neither of them legible.
            quarry.FrontRankGaps = 0f;
            foreach (UnitInstance unit in attackers) unit.FrontRankGaps = 0f;

            int bringing = 0, answering = 0;

            foreach (UnitInstance unit in attackers)
            {
                bringing += CombatSystem.FightingMen(unit, quarry);
                answering += CombatSystem.FightingMen(quarry, unit);
            }

            return (bringing, answering);
        }

        [Fact]
        public void TwoRegimentsOnOneFrontBringBetweenThemWhatOneBringsAlone()
        {
            (int alone, _) = AtContact(41000, 0f);
            (int pair, _) = AtContact(41010, 60f, -60f);

            // Sharing a front is sharing it. Two regiments each overlapping half
            // of it put the same number of men in reach as one covering all of
            // it — which is why the second regiment is sent for the fright it
            // causes rather than for the extra sword arms.
            Assert.InRange(pair, alone * 0.85f, alone * 1.15f);
        }

        [Fact]
        public void EachOfThePairBringsAboutHalf()
        {
            var field = new Battlefield("plains", 41020);

            UnitInstance quarry = field.Add(1, "spearmen", field.Centre, Facing.West);
            Battlefield.Hold(quarry);

            UnitInstance left = field.Add(0, "swordsmen", field.Centre - new Vec2(240f, 60f), Facing.East);
            UnitInstance right = field.Add(0, "swordsmen", field.Centre - new Vec2(240f, -60f), Facing.East);

            Battlefield.Press(left, quarry);
            Battlefield.Press(right, quarry);

            field.RunUntil(() => quarry.EnemiesInContact >= 2, maxTurns: 12);

            int leftBrings = CombatSystem.FightingMen(left, quarry);
            int rightBrings = CombatSystem.FightingMen(right, quarry);

            Assert.True(MathF.Abs(leftBrings - rightBrings) < leftBrings * 0.25f,
                $"They took equal halves of the front, so they should bring equal numbers: {leftBrings} " +
                $"against {rightBrings}.");
        }

        [Fact]
        public void TheDefenderAnswersTwoWithItsWholeLineRatherThanAFraction()
        {
            (_, int againstOne) = AtContact(41100, 0f);
            (_, int againstTwo) = AtContact(41110, 60f, -60f);

            // A regiment fighting two still has all the men it had. Dividing its
            // frontage by the number of enemies on top of dividing it by the
            // ground they each covered charged it twice for the same crowding
            // and left it answering with a quarter of its strength.
            Assert.InRange(againstTwo, againstOne * 0.85f, againstOne * 1.15f);
        }

        [Fact]
        public void ARegimentNeverBringsMoreThanTheFrontageItHas()
        {
            var field = new Battlefield("plains", 41200);

            UnitInstance quarry = field.Add(1, "spearmen", field.Centre, Facing.West);
            Battlefield.Hold(quarry);

            // Front and flank at once, which is where the claims genuinely
            // overlap and the cap has to do its work.
            UnitInstance ahead = field.Add(0, "swordsmen", field.Centre - new Vec2(240f, 0f), Facing.East);
            UnitInstance beside = field.Add(0, "swordsmen", field.Centre + new Vec2(0f, 240f), Facing.South);

            Battlefield.Press(ahead, quarry);
            Battlefield.Press(beside, quarry);

            field.RunUntil(() => quarry.EnemiesInContact >= 2, maxTurns: 14);

            int answering = CombatSystem.FightingMen(quarry, ahead) + CombatSystem.FightingMen(quarry, beside);

            (_, int alone) = AtContact(41210, 0f);

            Assert.True(answering <= alone * 1.15f,
                $"Set upon from two quarters it answered with {answering} men where it can only ever bring " +
                $"{alone}. A regiment has one line and cannot fight two full battles across it.");
        }

        [Fact]
        public void ANeighbourAlongTheSameFrontIsNotAFlanker()
        {
            // Two regiments abreast on the front, against one on the front and
            // one genuinely round the side. Both pairs bring two regiments; only
            // the second has anybody where the defender's line is not.
            var frontal = new Battlefield("plains", 41300);

            UnitInstance a = frontal.Add(1, "spearmen", frontal.Centre, Facing.West);
            Battlefield.Hold(a);

            UnitInstance ahead1 = frontal.Add(0, "swordsmen", frontal.Centre - new Vec2(240f, 60f), Facing.East);
            UnitInstance ahead2 = frontal.Add(0, "swordsmen", frontal.Centre - new Vec2(240f, -60f), Facing.East);
            Battlefield.Press(ahead1, a);
            Battlefield.Press(ahead2, a);

            frontal.RunUntilDecided(16, a);

            var flanked = new Battlefield("plains", 41310);

            UnitInstance b = flanked.Add(1, "spearmen", flanked.Centre, Facing.West);
            Battlefield.Hold(b);

            UnitInstance front = flanked.Add(0, "swordsmen", flanked.Centre - new Vec2(240f, 0f), Facing.East);
            UnitInstance side = flanked.Add(0, "swordsmen", flanked.Centre + new Vec2(0f, 240f), Facing.South);
            Battlefield.Press(front, b);
            Battlefield.Press(side, b);

            flanked.RunUntilDecided(16, b);

            Assert.True(Battlefield.LostPercent(b) > Battlefield.LostPercent(a),
                $"Being taken in the flank has to be worse than being pushed at from the front by the same " +
                $"number of regiments: {Battlefield.LostPercent(b):0}% lost against {Battlefield.LostPercent(a):0}%. " +
                "If a neighbour standing along the same front counts as a flanker, manoeuvre stops meaning " +
                "anything.");
        }
    }
}
