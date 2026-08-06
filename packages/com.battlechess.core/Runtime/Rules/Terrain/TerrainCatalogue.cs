using System;
using System.Collections.Generic;
using BattleChess.Contracts;

namespace BattleChess.Rules
{
    /// <summary>
    /// An immutable set of terrain definitions, with lookup by handle, key and
    /// authoring glyph.
    /// </summary>
    public sealed class TerrainCatalogue : ITerrainCatalogue
    {
        private readonly TerrainDef[] _byIndex;
        private readonly Dictionary<string, TerrainDef> _byKey;
        private readonly Dictionary<char, TerrainDef> _byGlyph;

        private TerrainCatalogue(TerrainDef[] byIndex, Dictionary<string, TerrainDef> byKey, Dictionary<char, TerrainDef> byGlyph)
        {
            _byIndex = byIndex;
            _byKey = byKey;
            _byGlyph = byGlyph;
        }

        public int Count => _byIndex.Length;

        public IReadOnlyList<TerrainDef> All => _byIndex;

        public TerrainDef Get(TerrainId id)
        {
            if (!id.IsValid || id.Index >= _byIndex.Length)
                throw new ArgumentOutOfRangeException(nameof(id), id, "Terrain id does not belong to this catalogue.");

            return _byIndex[id.Index];
        }

        public bool TryGetByKey(string key, out TerrainDef def)
        {
            if (key != null) return _byKey.TryGetValue(key, out def!);

            def = null!;
            return false;
        }

        public bool TryGetByGlyph(char glyph, out TerrainDef def) => _byGlyph.TryGetValue(glyph, out def!);

        /// <summary>
        /// Accumulates terrain definitions and assigns their handles.
        /// </summary>
        /// <remarks>
        /// Handles are issued here, in insertion order, which is why a
        /// <see cref="TerrainId"/> is only meaningful against the catalogue that
        /// produced it.
        /// </remarks>
        public sealed class Builder
        {
            private readonly List<TerrainDef> _defs = new List<TerrainDef>();
            private readonly Dictionary<string, TerrainDef> _byKey = new Dictionary<string, TerrainDef>(StringComparer.OrdinalIgnoreCase);
            private readonly Dictionary<char, TerrainDef> _byGlyph = new Dictionary<char, TerrainDef>();

            /// <summary>The handle the next added terrain will receive.</summary>
            public TerrainId NextId => new TerrainId(_defs.Count);

            public Builder Add(TerrainDef def)
            {
                if (def == null) throw new ArgumentNullException(nameof(def));

                if (def.Id.Index != _defs.Count)
                    throw new ArgumentException($"Terrain '{def.Key}' was built with id {def.Id}, but the catalogue expected {NextId}.", nameof(def));

                if (_byKey.ContainsKey(def.Key))
                    throw new ArgumentException($"Duplicate terrain key '{def.Key}'.", nameof(def));

                if (_byGlyph.TryGetValue(def.Glyph, out TerrainDef? clash))
                    throw new ArgumentException($"Terrain '{def.Key}' reuses glyph '{def.Glyph}', already taken by '{clash.Key}'.", nameof(def));

                _defs.Add(def);
                _byKey[def.Key] = def;
                _byGlyph[def.Glyph] = def;
                return this;
            }

            public TerrainCatalogue Build()
            {
                if (_defs.Count == 0)
                    throw new InvalidOperationException("A catalogue needs at least one terrain type.");

                return new TerrainCatalogue(_defs.ToArray(), _byKey, _byGlyph);
            }
        }
    }
}
