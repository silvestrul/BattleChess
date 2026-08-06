using System;
using System.Collections.Generic;
using System.Globalization;

namespace BattleChess.Contracts
{
    /// <summary>
    /// Base for a named, typed content attribute. Non-generic so loaders can
    /// hold a mixed list of keys without knowing any of their types.
    /// </summary>
    public abstract class AttributeKey
    {
        /// <summary>The name used in content files.</summary>
        public string Name { get; }

        protected AttributeKey(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Attribute name is required.", nameof(name));

            Name = name;
        }

        /// <summary>
        /// Turns a raw text value into the attribute's real type. Each key parses
        /// its own value, which is what lets loaders stay ignorant of every
        /// attribute that exists.
        /// </summary>
        public abstract object Parse(string raw);

        public override string ToString() => Name;
    }

    /// <summary>
    /// A strongly typed content attribute with a default for definitions that
    /// do not declare it.
    /// </summary>
    /// <remarks>
    /// The middle path between two worse options. Fixed fields cannot be
    /// extended without editing every consumer; a raw string dictionary can be
    /// extended freely but turns typos into silent wrong answers. A typed key
    /// gives extension <i>and</i> compile-time checking.
    /// </remarks>
    public sealed class AttributeKey<T> : AttributeKey
    {
        private readonly Func<string, T> _parse;

        /// <summary>Value used when a definition does not declare this attribute.</summary>
        public T Default { get; }

        public AttributeKey(string name, T defaultValue, Func<string, T> parse)
            : base(name)
        {
            Default = defaultValue;
            _parse = parse ?? throw new ArgumentNullException(nameof(parse));
        }

        public override object Parse(string raw) => _parse(raw)!;
    }

    /// <summary>
    /// The attributes one kind of content may declare.
    /// </summary>
    /// <remarks>
    /// Registries are handed to loaders explicitly rather than reached through
    /// hidden global state, so a mod or a test can supply extra keys without
    /// mutating anything shared.
    /// </remarks>
    public sealed class AttributeRegistry
    {
        private readonly List<AttributeKey> _keys = new List<AttributeKey>();

        public IReadOnlyList<AttributeKey> All => _keys;

        public AttributeKey<T> Define<T>(string name, T defaultValue, Func<string, T> parse)
        {
            if (TryFind(name, out _))
                throw new ArgumentException($"Attribute '{name}' is already defined.", nameof(name));

            var key = new AttributeKey<T>(name, defaultValue, parse);
            _keys.Add(key);
            return key;
        }

        public bool TryFind(string name, out AttributeKey key)
        {
            for (int i = 0; i < _keys.Count; i++)
            {
                if (string.Equals(_keys[i].Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    key = _keys[i];
                    return true;
                }
            }

            key = null!;
            return false;
        }
    }

    /// <summary>
    /// The attribute values a single definition declares, with lookup falling
    /// back to each key's default.
    /// </summary>
    public sealed class AttributeSet
    {
        public static readonly AttributeSet Empty = new AttributeSet(new Dictionary<string, object>());

        private readonly IReadOnlyDictionary<string, object> _values;

        public AttributeSet(IReadOnlyDictionary<string, object> values)
        {
            _values = values ?? throw new ArgumentNullException(nameof(values));
        }

        public T Get<T>(AttributeKey<T> key)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));

            return _values.TryGetValue(key.Name, out object? value) && value is T typed
                ? typed
                : key.Default;
        }

        /// <summary>Whether this definition explicitly declares the given attribute.</summary>
        public bool Declares(AttributeKey key) => key != null && _values.ContainsKey(key.Name);
    }

    /// <summary>
    /// Parsers for content values.
    /// </summary>
    /// <remarks>
    /// Deliberately strict. Content files are authored by hand, so a malformed
    /// value should fail loudly at load rather than silently becoming a default
    /// and producing a subtly wrong battlefield.
    ///
    /// Always invariant culture: content is written with '.' as the decimal
    /// point, and must load identically on a machine whose locale disagrees.
    /// </remarks>
    public static class AttributeParsers
    {
        public static int Int(string raw) =>
            int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
                ? value
                : throw new FormatException($"'{raw}' is not a whole number.");

        public static float Float(string raw) =>
            float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out float value)
                ? value
                : throw new FormatException($"'{raw}' is not a number.");

        public static bool Bool(string raw) =>
            bool.TryParse(raw, out bool value)
                ? value
                : throw new FormatException($"'{raw}' is not true or false.");

        public static string Text(string raw) => raw;
    }
}
