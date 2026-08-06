using System;
using BattleChess.Contracts;
using Xunit;

namespace BattleChess.Tests.Battle
{
    /// <summary>
    /// The same seed fights the same battle, and luck never decides one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Not merely a testing convenience. Determinism is the fairness guarantee:
    /// a published seed and a published order log let anyone re-run a battle
    /// and get the same answer, which is the whole reason a group running a map
    /// game on Facebook could trust this to referee their fights instead of
    /// arguing about them.
    /// </para>
    /// <para>
    /// The variance band matters for the same reason. With hidden information
    /// and blind orders, high variance reads as unfair rather than exciting —
    /// a player cannot tell being outplayed from being unlucky.
    /// </para>
    /// </remarks>
    public sealed class DeterminismTests
    {
        [Fact]
        public void TheSameSeedFightsTheSameBattle()
        {
            DuelResult first = Fight(4242);
            DuelResult second = Fight(4242);

            Assert.Equal(first.AttackerLost, second.AttackerLost, 5);
            Assert.Equal(first.DefenderLost, second.DefenderLost, 5);
            Assert.Equal(first.Attacker.Strength, second.Attacker.Strength);
            Assert.Equal(first.Defender.Strength, second.Defender.Strength);
            Assert.Equal(first.Attacker.State, second.Attacker.State);
            Assert.Equal(first.Defender.State, second.Defender.State);
            Assert.Equal(first.Turns, second.Turns);
        }

        [Fact]
        public void ADifferentSeedFightsADifferentBattle()
        {
            // Not a tautology worth skipping: if the seed were being ignored,
            // every one of these tests would still pass and nothing would ever
            // vary. This is the check that the RNG is wired in at all.
            //
            // Compared on survivors rather than percentages: a fight this
            // lopsided ends near annihilation on every seed, so the loser's
            // percentage is saturated and hides the variation entirely.
            int baseline = Fight(1).Attacker.Strength;
            bool anyDifference = false;

            foreach (ulong seed in new ulong[] { 2, 3, 4, 5, 99 })
            {
                if (Fight(seed).Attacker.Strength != baseline)
                {
                    anyDifference = true;
                    break;
                }
            }

            Assert.True(anyDifference, "Every seed produced an identical battle — the RNG is not reaching combat.");
        }

        [Fact]
        public void LuckNeverDecidesAFight()
        {
            float lowest = float.MaxValue;
            float highest = float.MinValue;

            foreach (ulong seed in new ulong[] { 1, 2, 3, 99, 12345, 777, 31337 })
            {
                DuelResult fight = Fight(seed);

                Assert.True(fight.AttackerWon,
                    $"Cavalry must beat swordsmen on every seed, not most of them. Seed {seed}: {fight}");

                lowest = MathF.Min(lowest, fight.AttackerLost);
                highest = MathF.Max(highest, fight.AttackerLost);
            }

            Assert.True(highest - lowest <= 10f,
                $"Outcomes should vary a little, not swing: the winner's losses ranged from " +
                $"{lowest:0}% to {highest:0}% across seeds.");
        }

        [Fact]
        public void EveryCounterHoldsOnEverySeed()
        {
            foreach (ulong seed in new ulong[] { 11, 22, 33, 44, 55 })
            {
                Assert.True(new Duel { Attacker = "spearmen", Defender = "cavalry", Seed = seed }.Fight().AttackerWon,
                    $"Spear must beat horse on seed {seed}.");

                Assert.True(new Duel { Attacker = "cavalry", Defender = "archers", Seed = seed }.Fight().AttackerWon,
                    $"Horse must beat bow on seed {seed}.");
            }
        }

        private static DuelResult Fight(ulong seed) =>
            new Duel { Attacker = "cavalry", Defender = "swordsmen", Seed = seed }.Fight();
    }
}
