using System;
using System.Collections.Generic;
using BattleChess.Contracts;

namespace BattleChess.Rules
{
    /// <summary>
    /// An immutable set of formation orders.
    /// </summary>
    public sealed class FormationCatalogue : IFormationCatalogue
    {
        private readonly FormationDef[] _byIndex;
        private readonly Dictionary<string, FormationDef> _byKey;

        private FormationCatalogue(FormationDef[] byIndex, Dictionary<string, FormationDef> byKey, FormationDef fallback)
        {
            _byIndex = byIndex;
            _byKey = byKey;
            Default = fallback;
        }

        public int Count => _byIndex.Length;

        public IReadOnlyList<FormationDef> All => _byIndex;

        public FormationDef Default { get; }

        public FormationDef Get(FormationId id)
        {
            if (!id.IsValid || id.Index >= _byIndex.Length)
                throw new ArgumentOutOfRangeException(nameof(id), id, "Formation id does not belong to this catalogue.");

            return _byIndex[id.Index];
        }

        public bool TryGetByKey(string key, out FormationDef def)
        {
            if (key != null) return _byKey.TryGetValue(key, out def!);

            def = null!;
            return false;
        }

        public sealed class Builder
        {
            private readonly List<FormationDef> _defs = new List<FormationDef>();
            private readonly Dictionary<string, FormationDef> _byKey = new Dictionary<string, FormationDef>(StringComparer.OrdinalIgnoreCase);

            public FormationId NextId => new FormationId(_defs.Count);

            public Builder Add(FormationDef def)
            {
                if (def == null) throw new ArgumentNullException(nameof(def));

                if (def.Id.Index != _defs.Count)
                    throw new ArgumentException($"Formation '{def.Key}' was built with id {def.Id}, but the catalogue expected {NextId}.", nameof(def));

                if (_byKey.ContainsKey(def.Key))
                    throw new ArgumentException($"Duplicate formation key '{def.Key}'.", nameof(def));

                _defs.Add(def);
                _byKey[def.Key] = def;
                return this;
            }

            public FormationCatalogue Build()
            {
                if (_defs.Count == 0)
                    throw new InvalidOperationException("A catalogue needs at least one formation.");

                // The first formation declared is the one units are raised in.
                // Content order therefore matters, which is why the natural
                // order is written first in formations.cfg.
                return new FormationCatalogue(_defs.ToArray(), _byKey, _defs[0]);
            }
        }
    }
}
