using System;
using BattleChess.Contracts;
using BattleChess.Rules;
using Xunit;

namespace BattleChess.Tests.Battle
{
    /// <summary>
    /// There are two ways to meet a regiment, and sending two at one is a
    /// decision about nerve rather than about arithmetic.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Front to front, or square on to a flank. Nothing between the two is
    /// worth having: a corner presented to a corner lets neither side bring its
    /// numbers to bear, and the fight becomes a question of which way the
    /// approach happened to run.
    /// </para>
    /// <para>
    /// Frontal is the default, and getting round to a flank has to be earned by
    /// already being round there. Otherwise a charge aimed squarely at a line
    /// drifts round the corner because it set off at an angle, and flanking
    /// stops being a manoeuvre.
    /// </para>
    /// </remarks>
    public sealed class MeetingTheEnemyTests
    {
        // ---- Two views and no others ------------------------------------------

        [Fact]
        public void AnApproachFromSlightlyOffTheFrontStillSquaresUp()
        {
            var field = new Battlefield("plains", 24000);

            // Facing west, so its front looks toward the attacker's side of the
            // field.
            UnitInstance foot = field.Add(1, "swordsmen", field.Centre, Facing.West);
            Battlefield.Hold(foot);

            // Forty-five degrees off their front — genuinely to one side, but
            // nowhere near round the end of the line.
            UnitInstance horse = field.Add(0, "cavalry",
                field.Centre - new Vec2(200f, 200f), Facing.East);

            Battlefield.Press(horse, foot);
            field.RunUntil(() => OrderSystem.InContactWith(horse, foot), maxTurns: 10);

            Assert.True(SquaredUp(horse, foot),
                $"At forty-five degrees it should commit to the front rather than drift round the corner. " +
                $"The two fronts are {Between(horse, foot):0}° apart, which is neither one thing nor the other.");
        }

        [Fact]
        public void AnApproachFromRoundTheEndOfTheLineTakesTheFlank()
        {
            var field = new Battlefield("plains", 24100);

            // Facing east, so its flanks lie north and south.
            UnitInstance foot = field.Add(1, "swordsmen", field.Centre, Facing.East);
            Battlefield.Hold(foot);

            // Squarely off the southern flank and barely in front at all, which
            // is what being in a flanking position means.
            UnitInstance horse = field.Add(0, "cavalry",
                field.Centre - new Vec2(0f, 240f), Facing.North);

            Battlefield.Press(horse, foot);
            field.RunUntil(() => OrderSystem.InContactWith(horse, foot), maxTurns: 10);

            Assert.True(Perpendicular(horse, foot),
                $"Already round the flank, it should take the flank: the two fronts are " +
                $"{Between(horse, foot):0}° apart.");
        }

        // What the two arrangements are worth once they are reached — a flank
        // fight being brutal, a rear one worse — belongs to the combat rule and
        // is measured from contact in MeleeMechanicsTests and ChargeTests.
        // Measuring it from an approach here only reproduced those tests badly:
        // the flanking regiment has to ride round the end of the line, so it
        // arrives later and lands fewer blows, and the number that comes out is
        // about travel time rather than about the arrangement.

        // ---- Two regiments sent at one ----------------------------------------

        [Fact]
        public void TwoRegimentsSentAtOneBothTakeItsFront()
        {
            var field = new Battlefield("plains", 24300);

            UnitInstance foot = field.Add(1, "spearmen", field.Centre, Facing.West);
            Battlefield.Hold(foot);

            // One squarely in front, one well off to the side — which on its own
            // would go round the flank.
            UnitInstance left = field.Add(0, "swordsmen", field.Centre - new Vec2(240f, 0f), Facing.East);
            UnitInstance right = field.Add(0, "swordsmen", field.Centre - new Vec2(40f, 240f), Facing.East);

            Battlefield.Press(left, foot);
            Battlefield.Press(right, foot);

            field.RunUntil(
                () => OrderSystem.InContactWith(left, foot) && OrderSystem.InContactWith(right, foot),
                maxTurns: 12);

            Assert.True(SquaredUp(left, foot) && SquaredUp(right, foot),
                $"Both were sent at the same regiment and both should meet its front: they are " +
                $"{Between(left, foot):0}° and {Between(right, foot):0}° off it.");

            // Side by side rather than one behind the other.
            Assert.False(OrientedRect.Overlaps(left.Shape, right.Shape),
                "And they should be standing beside each other, not in the same field.");
        }

        [Fact]
        public void BeingSetUponByTwoBreaksNerveFasterThanBeingFoughtByOne()
        {
            float alone = DefenderMoraleAfter(attackers: 1);
            float pair = DefenderMoraleAfter(attackers: 2);

            Assert.True(pair < alone - 0.05f,
                $"Two regiments share a frontage, so each deals about half — the reason to send the second " +
                $"is what it does to the defender's nerve. Fighting one left it at {alone:0.00} morale, " +
                $"fighting two at {pair:0.00}.");
        }

        [Fact]
        public void FightingBesideAFriendSteadiesTheMenDoingIt()
        {
            float alone = AttackerMoraleAfter(attackers: 1);
            float pair = AttackerMoraleAfter(attackers: 2);

            Assert.True(pair > alone,
                $"Men with a friendly regiment at their shoulder, both set on the same enemy, hold better " +
                $"than the same men alone: alone {alone:0.00}, in company {pair:0.00}.");
        }

        private static float DefenderMoraleAfter(int attackers) => TwoOnOne(attackers).Defender;
        private static float AttackerMoraleAfter(int attackers) => TwoOnOne(attackers).Attacker;

        /// <summary>
        /// Stands one or two regiments against a spear wall and reports the
        /// morale on each side after a short fight.
        /// </summary>
        /// <remarks>
        /// Placed in contact rather than marched in, so the two runs differ only
        /// in how many regiments are fighting — an approach would also differ in
        /// how long each spent walking.
        /// </remarks>
        private static (float Attacker, float Defender) TwoOnOne(int attackers)
        {
            var field = new Battlefield("plains", 24400, RuleSet.MeleeOnly);

            UnitInstance foot = field.Add(1, "spearmen", field.Centre, Facing.West);

            Footprint theirs = foot.Footprint;

            UnitInstance first = field.Add(0, "swordsmen",
                Battlefield.ContactPosition(foot, theirs, new Vec2(-1f, 0f)), Facing.East);

            if (attackers > 1)
            {
                // Beside the first, against the same front — the arrangement the
                // dressing rule produces.
                field.Add(0, "swordsmen",
                    Battlefield.ContactPosition(foot, theirs, new Vec2(-1f, 0f))
                        + new Vec2(0f, first.Footprint.Width + 4f),
                    Facing.East);
            }

            field.RunPulses(6);

            return (first.Morale, foot.Morale);
        }

        // ---- Reading the arrangement ------------------------------------------

        /// <summary>Degrees between two regiments' fronts.</summary>
        private static float Between(UnitInstance unit, UnitInstance enemy) =>
            Facing.AbsoluteDelta(unit.Facing, enemy.Facing) * 180f / MathF.PI;

        /// <summary>Front to front: the two bearings are opposed.</summary>
        private static bool SquaredUp(UnitInstance unit, UnitInstance enemy) =>
            MathF.Abs(180f - Between(unit, enemy)) < 25f;

        /// <summary>Square on to a flank: the two bearings are at a right angle.</summary>
        private static bool Perpendicular(UnitInstance unit, UnitInstance enemy) =>
            MathF.Abs(90f - Between(unit, enemy)) < 25f;
    }
}
