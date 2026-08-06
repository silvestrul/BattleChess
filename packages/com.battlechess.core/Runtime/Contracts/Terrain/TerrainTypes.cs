using System;

namespace BattleChess.Contracts
{
    /// <summary>
    /// How a unit gets about, which decides how each terrain treats it.
    /// </summary>
    /// <remarks>
    /// Adding a movement type (boats, pack mules, motorised) means adding a
    /// value here and a speed line to each terrain in the content file. No
    /// component needs changing — the movement model reads whatever speeds the
    /// content declares.
    /// </remarks>
    public enum MovementType
    {
        /// <summary>Infantry. The reliable all-rounder.</summary>
        Foot = 0,

        /// <summary>Cavalry. Fast in the open, poor in swamp, jungle and mountains.</summary>
        Horse = 1,

        /// <summary>Artillery and wagons. Road-bound, helpless in rough country.</summary>
        Wheeled = 2
    }

    /// <summary>
    /// A handle to a terrain type, valid only against the catalogue that issued it.
    /// </summary>
    /// <remarks>
    /// Deliberately an integer index rather than a string. Terrain is looked up
    /// constantly — once per unit per tick during movement, and across whole
    /// regions during pathfinding — so the lookup has to be an array index, not
    /// a string hash. The human-readable key ("plains") exists in the content
    /// file and is resolved to an index once, at load.
    /// </remarks>
    public readonly struct TerrainId : IEquatable<TerrainId>, IComparable<TerrainId>
    {
        /// <summary>Off-map, or terrain that failed to resolve.</summary>
        public static readonly TerrainId None = new TerrainId(-1);

        public readonly int Index;

        public TerrainId(int index) => Index = index;

        public bool IsValid => Index >= 0;

        public bool Equals(TerrainId other) => Index == other.Index;
        public override bool Equals(object? obj) => obj is TerrainId other && Equals(other);
        public override int GetHashCode() => Index;
        public int CompareTo(TerrainId other) => Index.CompareTo(other.Index);
        public override string ToString() => IsValid ? $"T{Index}" : "T-";

        public static bool operator ==(TerrainId a, TerrainId b) => a.Index == b.Index;
        public static bool operator !=(TerrainId a, TerrainId b) => a.Index != b.Index;
    }
}
