using System;

namespace BattleChess.Rules
{
    /// <summary>
    /// Serializable snapshot of a <see cref="DeterministicRng"/>. Saving this
    /// alongside match state is what makes a match resumable and a replay
    /// reproducible.
    /// </summary>
    public readonly struct RngState : IEquatable<RngState>
    {
        public readonly ulong State;
        public readonly ulong Inc;

        public RngState(ulong state, ulong inc)
        {
            State = state;
            Inc = inc;
        }

        public bool Equals(RngState other) => State == other.State && Inc == other.Inc;
        public override bool Equals(object? obj) => obj is RngState other && Equals(other);
        public override int GetHashCode() => unchecked((int)(State ^ (State >> 32) ^ Inc));
        public override string ToString() => $"Rng({State:X16}/{Inc:X16})";
    }

    /// <summary>
    /// PCG32 (permuted congruential generator). Small, fast, statistically
    /// sound, and — unlike <see cref="System.Random"/> — guaranteed to produce
    /// the identical sequence on every runtime and platform forever.
    /// </summary>
    /// <remarks>
    /// The whole simulation depends on this. <c>System.Random</c> is explicitly
    /// unsuitable: its algorithm is an implementation detail that has already
    /// changed once between .NET Framework and .NET Core, which would silently
    /// invalidate every golden replay.
    ///
    /// Reproducibility requires that calls happen in a defined order, so systems
    /// must consume randomness in unit-id order and never from a hash-ordered
    /// iteration.
    /// </remarks>
    public sealed class DeterministicRng
    {
        private const ulong Multiplier = 6364136223846793005UL;

        private ulong _state;
        private ulong _inc;

        /// <param name="seed">Match seed. The same seed and the same call order reproduce the same match.</param>
        /// <param name="sequence">
        /// Stream selector. Two generators with the same seed but different
        /// sequences produce independent streams, which is useful for keeping
        /// (say) combat rolls from perturbing map generation.
        /// </param>
        public DeterministicRng(ulong seed, ulong sequence = 1UL)
        {
            _state = 0UL;
            _inc = (sequence << 1) | 1UL;
            NextUInt();
            _state += seed;
            NextUInt();
        }

        public DeterministicRng(RngState state)
        {
            _state = state.State;
            _inc = state.Inc | 1UL;
        }

        public RngState Snapshot() => new RngState(_state, _inc);

        public void Restore(RngState state)
        {
            _state = state.State;
            _inc = state.Inc | 1UL;
        }

        /// <summary>Uniform in [0, 2^32).</summary>
        public uint NextUInt()
        {
            ulong old = _state;
            _state = unchecked(old * Multiplier + _inc);

            uint xorshifted = (uint)(((old >> 18) ^ old) >> 27);
            int rot = (int)(old >> 59);
            return (xorshifted >> rot) | (xorshifted << ((-rot) & 31));
        }

        /// <summary>Uniform in [0, maxExclusive). Rejection-sampled, so unbiased.</summary>
        public int NextInt(int maxExclusive)
        {
            if (maxExclusive <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxExclusive), maxExclusive, "Bound must be positive.");

            uint bound = (uint)maxExclusive;

            // Discard the leading partial bucket rather than taking a plain
            // modulo, which would over-represent low values.
            uint threshold = (uint)((0x1_0000_0000UL - bound) % bound);

            while (true)
            {
                uint r = NextUInt();
                if (r >= threshold)
                    return (int)(r % bound);
            }
        }

        /// <summary>Uniform in [minInclusive, maxExclusive).</summary>
        public int NextInt(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive)
                throw new ArgumentOutOfRangeException(nameof(maxExclusive), maxExclusive, "Range must be non-empty.");

            long span = (long)maxExclusive - minInclusive;
            if (span > int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(maxExclusive), "Range exceeds int width.");

            return minInclusive + NextInt((int)span);
        }

        /// <summary>Uniform in [0, 1). 24 bits of mantissa, matching float precision.</summary>
        public float NextFloat() => (NextUInt() >> 8) * (1.0f / 16777216.0f);

        /// <summary>Uniform in [min, max).</summary>
        public float NextFloat(float min, float max) => min + NextFloat() * (max - min);

        /// <summary>True with the given probability in [0, 1].</summary>
        public bool NextBool(float probability) => NextFloat() < probability;

        /// <summary>
        /// A multiplier in [1 - spread, 1 + spread). This is the intended way to
        /// add the deliberately small combat variance the design calls for.
        /// </summary>
        public float NextVariance(float spread) => 1.0f + NextFloat(-spread, spread);

        /// <summary>
        /// Fisher-Yates over a span. Order is fully determined by the generator
        /// state, so shuffles replay exactly.
        /// </summary>
        public void Shuffle<T>(T[] items)
        {
            if (items == null) throw new ArgumentNullException(nameof(items));

            for (int i = items.Length - 1; i > 0; i--)
            {
                int j = NextInt(i + 1);
                (items[i], items[j]) = (items[j], items[i]);
            }
        }
    }
}
