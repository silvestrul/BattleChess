using System;

namespace BattleChess.Contracts
{
    /// <summary>
    /// Identifies a side in a match. Values are small and stable for the
    /// lifetime of the match so they can be used as array indices.
    /// </summary>
    public readonly struct PlayerId : IEquatable<PlayerId>, IComparable<PlayerId>
    {
        public static readonly PlayerId None = new PlayerId(-1);

        public readonly int Value;

        public PlayerId(int value) => Value = value;

        public bool IsValid => Value >= 0;

        public bool Equals(PlayerId other) => Value == other.Value;
        public override bool Equals(object? obj) => obj is PlayerId other && Equals(other);
        public override int GetHashCode() => Value;
        public int CompareTo(PlayerId other) => Value.CompareTo(other.Value);
        public override string ToString() => IsValid ? $"P{Value}" : "P-";

        public static bool operator ==(PlayerId a, PlayerId b) => a.Value == b.Value;
        public static bool operator !=(PlayerId a, PlayerId b) => a.Value != b.Value;
    }

    /// <summary>
    /// Identifies a unit within a match.
    /// </summary>
    /// <remarks>
    /// Simulation order is defined as ascending <see cref="Value"/>. That is the
    /// contract every system relies on for reproducibility — never iterate units
    /// from a hash-ordered collection.
    /// </remarks>
    public readonly struct UnitId : IEquatable<UnitId>, IComparable<UnitId>
    {
        public static readonly UnitId None = new UnitId(-1);

        public readonly int Value;

        public UnitId(int value) => Value = value;

        public bool IsValid => Value >= 0;

        public bool Equals(UnitId other) => Value == other.Value;
        public override bool Equals(object? obj) => obj is UnitId other && Equals(other);
        public override int GetHashCode() => Value;
        public int CompareTo(UnitId other) => Value.CompareTo(other.Value);
        public override string ToString() => IsValid ? $"U{Value}" : "U-";

        public static bool operator ==(UnitId a, UnitId b) => a.Value == b.Value;
        public static bool operator !=(UnitId a, UnitId b) => a.Value != b.Value;
    }
}
