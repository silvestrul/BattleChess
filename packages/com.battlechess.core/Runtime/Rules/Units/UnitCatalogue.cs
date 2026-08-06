using System;
using System.Collections.Generic;
using BattleChess.Contracts;

namespace BattleChess.Rules
{
    /// <summary>
    /// An immutable set of unit definitions, with lookup by handle, key and
    /// glyph.
    /// </summary>
    public sealed class UnitCatalogue : IUnitCatalogue
    {
        private readonly UnitDef[] _byIndex;
        private readonly Dictionary<string, UnitDef> _byKey;
        private readonly Dictionary<char, UnitDef> _byGlyph;

        private UnitCatalogue(UnitDef[] byIndex, Dictionary<string, UnitDef> byKey, Dictionary<char, UnitDef> byGlyph)
        {
            _byIndex = byIndex;
            _byKey = byKey;
            _byGlyph = byGlyph;
        }

        public int Count => _byIndex.Length;

        public IReadOnlyList<UnitDef> All => _byIndex;

        public UnitDef Get(UnitTypeId id)
        {
            if (!id.IsValid || id.Index >= _byIndex.Length)
                throw new ArgumentOutOfRangeException(nameof(id), id, "Unit type id does not belong to this catalogue.");

            return _byIndex[id.Index];
        }

        public bool TryGetByKey(string key, out UnitDef def)
        {
            if (key != null) return _byKey.TryGetValue(key, out def!);

            def = null!;
            return false;
        }

        public bool TryGetByGlyph(char glyph, out UnitDef def) => _byGlyph.TryGetValue(glyph, out def!);

        public sealed class Builder
        {
            private readonly List<UnitDef> _defs = new List<UnitDef>();
            private readonly Dictionary<string, UnitDef> _byKey = new Dictionary<string, UnitDef>(StringComparer.OrdinalIgnoreCase);
            private readonly Dictionary<char, UnitDef> _byGlyph = new Dictionary<char, UnitDef>();

            public UnitTypeId NextId => new UnitTypeId(_defs.Count);

            public Builder Add(UnitDef def)
            {
                if (def == null) throw new ArgumentNullException(nameof(def));

                if (def.Id.Index != _defs.Count)
                    throw new ArgumentException($"Unit '{def.Key}' was built with id {def.Id}, but the catalogue expected {NextId}.", nameof(def));

                if (_byKey.ContainsKey(def.Key))
                    throw new ArgumentException($"Duplicate unit key '{def.Key}'.", nameof(def));

                if (_byGlyph.TryGetValue(def.Glyph, out UnitDef? clash))
                    throw new ArgumentException($"Unit '{def.Key}' reuses glyph '{def.Glyph}', already taken by '{clash.Key}'.", nameof(def));

                _defs.Add(def);
                _byKey[def.Key] = def;
                _byGlyph[def.Glyph] = def;
                return this;
            }

            public UnitCatalogue Build()
            {
                if (_defs.Count == 0)
                    throw new InvalidOperationException("A catalogue needs at least one unit type.");

                return new UnitCatalogue(_defs.ToArray(), _byKey, _byGlyph);
            }
        }
    }
}
