using System;
using System.Collections.Generic;

namespace BattleChess.Contracts
{
    /// <summary>
    /// What one kind of terrain <i>is</i> — its identity, its effect on movement,
    /// and whatever else has been declared about it.
    /// </summary>
    /// <remarks>
    /// Knows nothing about where it appears. A map says which terrain is where;
    /// this says what that terrain means. Keeping those apart is what lets the
    /// map be swapped (hand-drawn, generated, painted) without touching rules,
    /// and rules to change without touching maps.
    /// </remarks>
    public sealed class TerrainDef
    {
        private const int MovementTypeCount = 3;

        private readonly float[] _speedByMovementType;

        /// <summary>Everything declared about this terrain beyond its identity and speeds.</summary>
        public AttributeSet Attributes { get; }

        public TerrainId Id { get; }

        /// <summary>Stable key used in content files, e.g. "plains".</summary>
        public string Key { get; }

        /// <summary>Human-readable name for the interface.</summary>
        public string DisplayName { get; }

        /// <summary>Character used to author and render this terrain in text maps.</summary>
        public char Glyph { get; }

        public TerrainDef(
            TerrainId id,
            string key,
            string displayName,
            char glyph,
            IReadOnlyDictionary<MovementType, float> speeds,
            AttributeSet? attributes)
        {
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("Terrain key is required.", nameof(key));
            if (speeds == null) throw new ArgumentNullException(nameof(speeds));

            Id = id;
            Key = key;
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? key : displayName;
            Glyph = glyph;
            Attributes = attributes ?? AttributeSet.Empty;

            _speedByMovementType = new float[MovementTypeCount];
            foreach (KeyValuePair<MovementType, float> entry in speeds)
            {
                int index = (int)entry.Key;
                if ((uint)index >= MovementTypeCount)
                    throw new ArgumentOutOfRangeException(nameof(speeds), entry.Key, "Unknown movement type.");

                if (entry.Value < 0f || float.IsNaN(entry.Value))
                    throw new ArgumentOutOfRangeException(nameof(speeds), entry.Value, $"Speed for {entry.Key} must be zero or positive.");

                _speedByMovementType[index] = entry.Value;
            }
        }

        /// <summary>
        /// Multiplier applied to a unit's base speed here. Zero means the terrain
        /// cannot be entered by that movement type at all.
        /// </summary>
        public float SpeedMultiplier(MovementType movementType)
        {
            int index = (int)movementType;
            if ((uint)index >= MovementTypeCount)
                throw new ArgumentOutOfRangeException(nameof(movementType), movementType, "Unknown movement type.");

            return _speedByMovementType[index];
        }

        public bool IsPassable(MovementType movementType) => SpeedMultiplier(movementType) > 0f;

        /// <summary>
        /// Reads an attribute, falling back to the key's default when this
        /// terrain does not declare it.
        /// </summary>
        public T Get<T>(AttributeKey<T> key) => Attributes.Get(key);

        /// <summary>Whether this terrain explicitly declares the given attribute.</summary>
        public bool Declares(AttributeKey key) => Attributes.Declares(key);

        public override string ToString() => $"{DisplayName} ('{Glyph}')";
    }
}
