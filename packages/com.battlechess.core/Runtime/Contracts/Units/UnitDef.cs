using System;
using System.Collections.Generic;

namespace BattleChess.Contracts
{
    /// <summary>
    /// What one kind of unit <i>is</i>: its role, how it forms up, and its
    /// per-man qualities.
    /// </summary>
    /// <remarks>
    /// Holds no strength of its own. A definition describes spearmen in general;
    /// how many spearmen are standing in a particular regiment belongs to the
    /// living unit, and everything that scales — frontage, hitting power,
    /// casualties — is computed from that number against these per-man values.
    /// </remarks>
    public sealed class UnitDef
    {
        public UnitTypeId Id { get; }

        /// <summary>Stable key used in content files, e.g. "spearmen".</summary>
        public string Key { get; }

        public string DisplayName { get; }

        /// <summary>Character used to draw this unit in the text harness.</summary>
        public char Glyph { get; }

        public UnitClass Class { get; }

        public MovementType Movement { get; }

        /// <summary>
        /// How this unit draws up when left alone. Formation orders scale this
        /// rather than replacing it, so a unit's identity survives reshaping.
        /// </summary>
        public Formation NaturalFormation { get; }

        /// <summary>Per-man qualities: speed, combat values, vision, and so on.</summary>
        public AttributeSet Attributes { get; }

        /// <summary>
        /// Attack multipliers against particular classes, from
        /// <c>attackVs.cavalry</c> and the like.
        /// </summary>
        /// <remarks>
        /// Explicit rather than emergent. Spearmen beat cavalry because the
        /// content says so, not because their armour and defence happen to sum
        /// that way — which means wanting spears 20% better against horse is one
        /// number, not an afternoon solving for values that also disturb every
        /// other matchup.
        /// </remarks>
        private readonly float[] _attackVsClass;

        public UnitDef(
            UnitTypeId id,
            string key,
            string displayName,
            char glyph,
            UnitClass unitClass,
            MovementType movement,
            Formation formation,
            AttributeSet? attributes,
            IReadOnlyDictionary<UnitClass, float>? attackVsClass = null)
        {
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("Unit key is required.", nameof(key));

            Id = id;
            Key = key;
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? key : displayName;
            Glyph = glyph;
            Class = unitClass;
            Movement = movement;
            NaturalFormation = formation;
            Attributes = attributes ?? AttributeSet.Empty;

            int classCount = Enum.GetValues(typeof(UnitClass)).Length;
            _attackVsClass = new float[classCount];

            for (int i = 0; i < classCount; i++)
                _attackVsClass[i] = 1f;

            if (attackVsClass != null)
            {
                foreach (KeyValuePair<UnitClass, float> entry in attackVsClass)
                {
                    if (entry.Value < 0f)
                        throw new ArgumentOutOfRangeException(nameof(attackVsClass), entry.Value, $"Multiplier against {entry.Key} cannot be negative.");

                    _attackVsClass[(int)entry.Key] = entry.Value;
                }
            }

            if (MinStrength > MaxStrength)
                throw new ArgumentException($"Unit '{key}' has minStrength {MinStrength} above maxStrength {MaxStrength}.", nameof(attributes));
        }

        public T Get<T>(AttributeKey<T> key) => Attributes.Get(key);

        public int DefaultStrength => Get(UnitAttributes.DefaultStrength);
        public int MinStrength => Get(UnitAttributes.MinStrength);
        public int MaxStrength => Get(UnitAttributes.MaxStrength);

        /// <summary>Open-ground speed in metres per second, before terrain.</summary>
        public float Speed => Get(UnitAttributes.Speed);

        /// <summary>The ground this unit covers at a given strength in its natural order.</summary>
        public Footprint FootprintAt(int strength) => NaturalFormation.FootprintFor(strength);

        /// <summary>How much harder this unit hits a given class. One means no special edge.</summary>
        public float AttackMultiplierAgainst(UnitClass target) => _attackVsClass[(int)target];

        /// <summary>
        /// Clamps a requested strength into what this unit may be raised at.
        /// </summary>
        public int ClampStrength(int requested) => Math.Clamp(requested, MinStrength, MaxStrength);

        /// <summary>
        /// Total value of an attribute across a whole body of men. This is how
        /// per-man numbers become regiment numbers.
        /// </summary>
        public float TotalOf(AttributeKey<float> key, int strength) => Get(key) * strength;

        public override string ToString() => $"{DisplayName} ({Class}, {Movement})";
    }

    /// <summary>
    /// The set of unit types in play, and the only way to turn a
    /// <see cref="UnitTypeId"/> back into what it means.
    /// </summary>
    public interface IUnitCatalogue
    {
        int Count { get; }

        IReadOnlyList<UnitDef> All { get; }

        UnitDef Get(UnitTypeId id);

        bool TryGetByKey(string key, out UnitDef def);

        bool TryGetByGlyph(char glyph, out UnitDef def);
    }
}
