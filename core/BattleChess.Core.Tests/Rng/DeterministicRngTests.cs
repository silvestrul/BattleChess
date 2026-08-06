using System;
using System.Collections.Generic;
using BattleChess.Rules;
using Xunit;

namespace BattleChess.Tests.Rng
{
    public class DeterministicRngTests
    {
        [Fact]
        public void SameSeed_ProducesSameSequence()
        {
            var a = new DeterministicRng(12345UL);
            var b = new DeterministicRng(12345UL);

            for (int i = 0; i < 1000; i++)
                Assert.Equal(a.NextUInt(), b.NextUInt());
        }

        [Fact]
        public void DifferentSeeds_Diverge()
        {
            var a = new DeterministicRng(1UL);
            var b = new DeterministicRng(2UL);

            bool anyDifference = false;
            for (int i = 0; i < 100; i++)
            {
                if (a.NextUInt() != b.NextUInt())
                {
                    anyDifference = true;
                    break;
                }
            }

            Assert.True(anyDifference, "Distinct seeds produced identical output.");
        }

        [Fact]
        public void DifferentStreams_SameSeed_Diverge()
        {
            var a = new DeterministicRng(7UL, sequence: 1UL);
            var b = new DeterministicRng(7UL, sequence: 2UL);

            bool anyDifference = false;
            for (int i = 0; i < 100; i++)
            {
                if (a.NextUInt() != b.NextUInt())
                {
                    anyDifference = true;
                    break;
                }
            }

            Assert.True(anyDifference, "Distinct streams produced identical output.");
        }

        [Fact]
        public void Snapshot_RoundTrips()
        {
            var rng = new DeterministicRng(99UL);
            for (int i = 0; i < 50; i++) rng.NextUInt();

            RngState saved = rng.Snapshot();
            uint[] expected = new uint[20];
            for (int i = 0; i < expected.Length; i++) expected[i] = rng.NextUInt();

            rng.Restore(saved);
            for (int i = 0; i < expected.Length; i++)
                Assert.Equal(expected[i], rng.NextUInt());
        }

        [Fact]
        public void Snapshot_ReconstructsViaConstructor()
        {
            var rng = new DeterministicRng(4242UL);
            for (int i = 0; i < 10; i++) rng.NextUInt();

            var clone = new DeterministicRng(rng.Snapshot());

            for (int i = 0; i < 100; i++)
                Assert.Equal(rng.NextUInt(), clone.NextUInt());
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(6)]
        [InlineData(100)]
        public void NextInt_StaysInRange(int bound)
        {
            var rng = new DeterministicRng(555UL);

            for (int i = 0; i < 10_000; i++)
            {
                int value = rng.NextInt(bound);
                Assert.InRange(value, 0, bound - 1);
            }
        }

        [Fact]
        public void NextInt_MinMax_StaysInRange()
        {
            var rng = new DeterministicRng(777UL);

            for (int i = 0; i < 10_000; i++)
                Assert.InRange(rng.NextInt(-5, 5), -5, 4);
        }

        [Fact]
        public void NextInt_RejectsNonPositiveBound()
        {
            var rng = new DeterministicRng(1UL);

            Assert.Throws<ArgumentOutOfRangeException>(() => rng.NextInt(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => rng.NextInt(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => rng.NextInt(5, 5));
        }

        [Fact]
        public void NextInt_IsReasonablyUniform()
        {
            // Not a rigorous statistical test, just a tripwire for a badly
            // broken bound implementation (e.g. a plain modulo bias).
            const int buckets = 10;
            const int samples = 200_000;

            var rng = new DeterministicRng(31337UL);
            var counts = new int[buckets];

            for (int i = 0; i < samples; i++)
                counts[rng.NextInt(buckets)]++;

            const int expected = samples / buckets;
            foreach (int count in counts)
                Assert.InRange(count, (int)(expected * 0.9), (int)(expected * 1.1));
        }

        [Fact]
        public void NextFloat_StaysInUnitInterval()
        {
            var rng = new DeterministicRng(2024UL);

            for (int i = 0; i < 10_000; i++)
            {
                float value = rng.NextFloat();
                Assert.True(value >= 0.0f && value < 1.0f, $"NextFloat produced {value}.");
            }
        }

        [Fact]
        public void NextVariance_RespectsSpread()
        {
            var rng = new DeterministicRng(8080UL);

            for (int i = 0; i < 10_000; i++)
            {
                float value = rng.NextVariance(0.1f);
                Assert.True(value >= 0.9f && value < 1.1f, $"NextVariance produced {value}.");
            }
        }

        [Fact]
        public void Shuffle_IsPermutationAndReproducible()
        {
            int[] Build() => new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 };

            var a = Build();
            var b = Build();

            new DeterministicRng(1234UL).Shuffle(a);
            new DeterministicRng(1234UL).Shuffle(b);

            Assert.Equal(a, b);

            var sorted = new List<int>(a);
            sorted.Sort();
            Assert.Equal(Build(), sorted);
        }
    }
}
