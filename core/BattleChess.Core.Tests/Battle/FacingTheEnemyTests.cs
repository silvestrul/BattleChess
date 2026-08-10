using System;
using BattleChess.Contracts;
using BattleChess.Rules;
using Xunit;

namespace BattleChess.Tests.Battle
{
    /// <summary>
    /// Regiments turn to face whatever is fighting them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Facing used to be written only while a unit was marching, so a regiment
    /// that halted kept whatever bearing it happened to stop on for the rest of
    /// the battle. Anything that arrived at an angle then fought permanently
    /// flanked, taking up to twice the casualties it should, and nothing on
    /// screen explained it.
    /// </para>
    /// <para>
    /// It also made flanking a lottery rather than a manoeuvre. Getting round
    /// an enemy is supposed to be worth something because you came about faster
    /// than they could — which is what turn rate is for, and why a pike block
    /// at two and a half degrees a second is so much easier to catch than
    /// cavalry at seven.
    /// </para>
    /// </remarks>
    public sealed class FacingTheEnemyTests
    {
        [Fact]
        public void ARegimentAttackedFromBehindComesAbout()
        {
            var field = new Battlefield("plains", 19000);

            // Facing east, with the enemy arriving from due west — squarely in
            // its back.
            UnitInstance ours = field.Add(0, "swordsmen", field.Centre, Facing.East);
            Battlefield.Hold(ours);

            UnitInstance theirs = field.Add(1, "swordsmen", field.Centre - new Vec2(120f, 0f), Facing.East);
            Battlefield.Press(theirs, ours);

            float offAtStart = OffBearing(ours, theirs);

            field.RunTurns(4);

            float offNow = OffBearing(ours, theirs);

            Assert.True(offNow < offAtStart - 30f,
                $"Men fight the enemy in front of them. It was {offAtStart:0}° off the enemy and is now " +
                $"{offNow:0}° — it should have turned to meet them.");
        }

        [Fact]
        public void ComingAboutTakesTimeAndAPikeBlockIsSlowestOfAll()
        {
            float pikes = StillOffBearingAfterTwentySeconds("spearmen");
            float horse = StillOffBearingAfterTwentySeconds("cavalry");

            Assert.True(pikes > horse + 30f,
                $"Turn rate has to decide who gets caught out of position. Twenty seconds after being " +
                $"taken in the back, a pike block was still {pikes:0}° off its enemy and cavalry " +
                $"{horse:0}°.");

            Assert.True(horse < 60f, "Horse should be most of the way round by then.");
        }

        /// <summary>
        /// Spins a regiment to face directly away from the enemy already among
        /// it, then reports how far off it still is twenty seconds later.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Measured as the angle remaining rather than the angle turned. Turned
        /// degrees flatters nobody and reads zero for cavalry, which comes about
        /// so fast it is already facing the enemy before the clock starts.
        /// </para>
        /// <para>
        /// Twenty seconds rather than thirty, because a regiment held facing
        /// the wrong way is now being butchered while it turns — the whole
        /// point of the rule. Given half a minute both of them break and run,
        /// and a routing unit faces where it is going, which measures the rout
        /// rather than the wheel.
        /// </para>
        /// </remarks>
        private static float StillOffBearingAfterTwentySeconds(string key)
        {
            var field = new Battlefield("plains", 19100);

            UnitInstance ours = field.Add(0, key, field.Centre, Facing.East);
            Battlefield.Hold(ours);

            UnitInstance theirs = field.Add(1, "swordsmen", field.Centre - new Vec2(120f, 0f), Facing.East);
            Battlefield.Press(theirs, ours);

            field.RunUntil(() => ours.EnemiesInContact > 0, maxTurns: 6);

            // Squarely in the back, whatever the approach happened to leave.
            ours.Facing = Facing.FromVector(ours.Position - theirs.Position);

            field.RunPulses(2);

            return OffBearing(ours, theirs);
        }

        // ---- The bug this was found through -----------------------------------

        [Fact]
        public void CavalryThatStopsAtAnAngleIsNotDoomedByIt()
        {
            // Cavalry charging home and halting side-on used to fight the whole
            // melee flanked, losing to swordsmen it beats comfortably. It should
            // pay for the bad arrival by spending time coming round, not by
            // losing the battle outright.
            var field = new Battlefield("plains", 19200);

            UnitInstance foot = field.Add(1, "swordsmen", field.Centre, Facing.West);
            Battlefield.Hold(foot);

            // Deliberately started facing ninety degrees off, as an overshooting
            // charge ends up.
            UnitInstance horse = field.Add(0, "cavalry", field.Centre - new Vec2(140f, 0f), Facing.North);
            Battlefield.Press(horse, foot);

            field.RunUntilDecided(14, horse, foot);

            Assert.True(Battlefield.LostPercent(foot) > Battlefield.LostPercent(horse),
                $"Horse lost {Battlefield.LostPercent(horse):0}%, foot {Battlefield.LostPercent(foot):0}%.");
        }

        /// <summary>Degrees between where a unit points and where the enemy is.</summary>
        private static float OffBearing(UnitInstance unit, UnitInstance enemy) =>
            Facing.AbsoluteDelta(unit.Facing, Facing.Towards(unit.Position, enemy.Position)) * 180f / MathF.PI;
    }
}
