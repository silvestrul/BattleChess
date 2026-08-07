using System;
using BattleChess.Contracts;
using BattleChess.Rules;
using Xunit;

namespace BattleChess.Tests.Battle
{
    /// <summary>
    /// What a regiment does when more than one enemy is within reach.
    /// </summary>
    /// <remarks>
    /// The interesting case and the one that broke. A regiment held up by an
    /// enemy is told to fight it; contact then exempts the one it is attacking
    /// and reports the <i>next</i> enemy as the thing blocking the way, so the
    /// following tick redirects the attack to that one and the first becomes
    /// the blocker again. Left alone, that oscillates forever at one tick per
    /// swap, and the regiment spends the battle planning marches that are
    /// cancelled before a step is taken.
    /// </remarks>
    public sealed class CrowdedFightTests
    {
        /// <summary>
        /// Our regiment with two enemies drawn up close together in front of
        /// it — the ordinary shape of a line meeting a line.
        /// </summary>
        private static Battlefield TwoEnemiesAbreast(
            out UnitInstance ours, out UnitInstance first, out UnitInstance second)
        {
            var field = new Battlefield("plains", 20000);

            ours = field.Add(0, "spearmen", field.Centre - new Vec2(160f, 0f), Facing.East);

            first = field.Add(1, "swordsmen", field.Centre, Facing.West);
            second = field.Add(1, "cavalry", field.Centre + new Vec2(20f, 95f), Facing.West);

            Battlefield.Hold(first);
            Battlefield.Hold(second);

            return field;
        }

        [Fact]
        public void ARegimentDoesNotSwapTargetsEveryTick()
        {
            Battlefield field = TwoEnemiesAbreast(out UnitInstance ours, out UnitInstance _, out UnitInstance _);

            field.March(ours, field.Centre, Stance.Advance);
            field.RunTurns(6);

            int redirects = field.TimesSaid("cannot get past");

            Assert.True(redirects <= 6,
                $"It changed its mind {redirects} times. Two enemies at sword's length used to hand a " +
                "regiment back and forth once a tick, and it never landed a blow on either.");
        }

        [Fact]
        public void ARegimentWithTwoEnemiesInFrontOfItActuallyFights()
        {
            Battlefield field = TwoEnemiesAbreast(
                out UnitInstance ours, out UnitInstance first, out UnitInstance second);

            field.March(ours, field.Centre, Stance.Advance);
            field.RunTurns(8);

            int pulses = field.TimesSaid("exchange");

            Assert.True(pulses >= 10,
                $"Eight turns is forty-eight combat pulses and only {pulses} landed. A regiment that has " +
                "reached the enemy must fight rather than re-plan a march it never walks.");

            Assert.True(
                Battlefield.LostPercent(first) > 0f || Battlefield.LostPercent(second) > 0f,
                "And somebody must be losing men.");
        }

        [Fact]
        public void OnceEngagedARegimentStaysWithTheEnemyItIsFighting()
        {
            Battlefield field = TwoEnemiesAbreast(out UnitInstance ours, out UnitInstance _, out UnitInstance _);

            field.March(ours, field.Centre, Stance.Advance);
            field.RunUntil(() => ours.EnemiesInContact > 0, maxTurns: 6);

            UnitId engaged = ours.Order.Target;
            Assert.True(engaged.IsValid, "It should have picked somebody.");

            field.RunTurns(3);

            Assert.Equal(engaged, ours.Order.Target);
        }

        // ---- Staying on the map ------------------------------------------------

        [Theory]
        [InlineData("cavalry")]
        [InlineData("spearmen")]
        public void ARegimentNeverHangsOverTheEdgeOfTheWorld(string key)
        {
            var field = new Battlefield("plains", 20100);

            UnitInstance unit = field.Add(0, key, field.Centre, Facing.East);

            // Marched hard at the north-east corner, which is where a centre
            // legally inside the bounds still leaves half a frontage outside.
            field.March(unit, new Vec2(field.Map.Bounds.Max.X, field.Map.Bounds.Max.Y));
            field.RunTurns(10);

            foreach (Vec2 corner in unit.Shape.GetCorners())
            {
                Assert.True(field.Map.Bounds.Contains(corner),
                    $"A regiment is a rectangle, not a point: {key} ended with a corner at {corner}, " +
                    $"outside {field.Map.Bounds}.");
            }
        }

        [Fact]
        public void BrokenRegimentsAreStillAllowedToLeave()
        {
            var field = new Battlefield("plains", 20200);

            UnitInstance runner = field.Add(0, "swordsmen", field.Centre - new Vec2(380f, 0f), Facing.East);
            UnitInstance chaser = field.Add(1, "cavalry", field.Centre - new Vec2(300f, 0f), Facing.West);

            Battlefield.Press(chaser, runner);

            runner.Morale = 0.05f;
            runner.State = UnitState.Routing;

            field.RunUntil(() => !runner.IsOnField, maxTurns: 20);

            Assert.False(runner.IsOnField,
                "Holding formations on the map must not trap broken ones against the border. Leaving is " +
                "the whole of what a router is doing.");
        }
    }
}
