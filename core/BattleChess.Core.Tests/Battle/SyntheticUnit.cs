using System.Collections.Generic;
using BattleChess.Contracts;

namespace BattleChess.Tests.Battle
{
    /// <summary>
    /// Builds a unit type that exists only for a test.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The counter tests fight with the shipped content, because that is what
    /// is worth protecting. But a question like "does armour actually reduce
    /// losses?" cannot be answered that way — no two real units differ in
    /// armour alone, so any comparison between them is measuring five things at
    /// once.
    /// </para>
    /// <para>
    /// So single-rule tests build a pair of units identical in every respect
    /// but the one under examination. Everything undeclared falls back to the
    /// attribute's own default, which keeps these definitions to the two or
    /// three lines that actually matter.
    /// </para>
    /// </remarks>
    public sealed class SyntheticUnit
    {
        private readonly Dictionary<string, object> _values = new Dictionary<string, object>();
        private readonly Dictionary<UnitClass, float> _versus = new Dictionary<UnitClass, float>();

        private UnitClass _class = UnitClass.Sword;
        private MovementType _movement = MovementType.Foot;
        private Formation _formation = new Formation(3, 0.6f, 1.5f);

        public SyntheticUnit With(AttributeKey<float> key, float value)
        {
            _values[key.Name] = value;
            return this;
        }

        public SyntheticUnit With(AttributeKey<int> key, int value)
        {
            _values[key.Name] = value;
            return this;
        }

        public SyntheticUnit OfClass(UnitClass unitClass)
        {
            _class = unitClass;
            return this;
        }

        public SyntheticUnit Moving(MovementType movement)
        {
            _movement = movement;
            return this;
        }

        /// <summary>Sets this unit's edge against a particular class.</summary>
        public SyntheticUnit Against(UnitClass target, float multiplier)
        {
            _versus[target] = multiplier;
            return this;
        }

        public SyntheticUnit FormedUp(int ranks, float fileWidth, float rankDepth)
        {
            _formation = new Formation(ranks, fileWidth, rankDepth);
            return this;
        }

        /// <summary>
        /// Finishes the definition. Anything not set takes the attribute
        /// default, so two units built from the same calls plus one difference
        /// differ in exactly that one thing.
        /// </summary>
        public UnitDef Build(string key, int strength = 500)
        {
            _values[UnitAttributes.DefaultStrength.Name] = strength;
            _values[UnitAttributes.MinStrength.Name] = 1;
            _values[UnitAttributes.MaxStrength.Name] = 100000;

            return new UnitDef(
                new UnitTypeId(0), key, key, key[0], _class, _movement, _formation,
                new AttributeSet(new Dictionary<string, object>(_values)),
                new Dictionary<UnitClass, float>(_versus));
        }
    }
}
