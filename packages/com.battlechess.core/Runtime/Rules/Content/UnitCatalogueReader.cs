using System;
using System.Collections.Generic;
using BattleChess.Contracts;

namespace BattleChess.Rules
{
    /// <summary>
    /// Builds a <see cref="UnitCatalogue"/> from a content file.
    /// </summary>
    /// <remarks>
    /// Understands only what a unit structurally <i>is</i> — its identity, role,
    /// movement type and formation. Every per-man quality passes through the
    /// attribute registry untouched, so the combat stats M3 will need and the
    /// vision stats M4 will need are already loading correctly without this file
    /// knowing they exist.
    /// </remarks>
    public static class UnitCatalogueReader
    {
        private const string SectionName = "unit";
        private const string AttackVsPrefix = "attackVs.";

        private static readonly string[] ReservedSettings =
        {
            "glyph", "name", "class", "movement", "ranks", "fileWidth", "rankDepth"
        };

        public static UnitCatalogue Read(string text, AttributeRegistry? registry = null)
        {
            AttributeRegistry attributes = registry ?? UnitAttributes.Registry;

            ConfigDocument document = ConfigDocument.Parse(text);
            var builder = new UnitCatalogue.Builder();

            foreach (ConfigSection section in document.SectionsNamed(SectionName))
            {
                if (string.IsNullOrWhiteSpace(section.Argument))
                    throw new FormatException($"Line {section.LineNumber}: [unit] needs a key, e.g. [unit spearmen].");

                builder.Add(ReadUnit(section, builder.NextId, attributes));
            }

            return builder.Build();
        }

        private static UnitDef ReadUnit(ConfigSection section, UnitTypeId id, AttributeRegistry registry)
        {
            string key = section.Argument!;

            string glyphText = section.Require("glyph");
            if (glyphText.Length != 1)
                throw new FormatException($"[unit {key}] (line {section.LineNumber}): glyph must be exactly one character, got '{glyphText}'.");

            string classText = section.Require("class");
            if (!Enum.TryParse(classText, ignoreCase: true, out UnitClass unitClass))
                throw new FormatException($"[unit {key}] (line {section.LineNumber}): unknown class '{classText}'.");

            string movementText = section.Require("movement");
            if (!Enum.TryParse(movementText, ignoreCase: true, out MovementType movement))
                throw new FormatException($"[unit {key}] (line {section.LineNumber}): unknown movement type '{movementText}'.");

            Formation formation;
            try
            {
                formation = new Formation(
                    AttributeParsers.Int(section.Require("ranks")),
                    AttributeParsers.Float(section.Require("fileWidth")),
                    AttributeParsers.Float(section.GetOrDefault("rankDepth", "1.5")));
            }
            catch (Exception error) when (error is FormatException or ArgumentOutOfRangeException)
            {
                throw new FormatException($"[unit {key}] (line {section.LineNumber}): {error.Message}");
            }

            var attackVsClass = new Dictionary<UnitClass, float>();

            foreach (KeyValuePair<string, string> setting in section.Values)
            {
                if (!setting.Key.StartsWith(AttackVsPrefix, StringComparison.OrdinalIgnoreCase)) continue;

                string className = setting.Key.Substring(AttackVsPrefix.Length);

                if (!Enum.TryParse(className, ignoreCase: true, out UnitClass target))
                    throw new FormatException($"[unit {key}] (line {section.LineNumber}): unknown unit class '{className}'.");

                attackVsClass[target] = AttributeParsers.Float(setting.Value);
            }

            AttributeSet attributes = AttributeSetReader.Read(
                section, registry, ReservedSettings,
                name => name.StartsWith(AttackVsPrefix, StringComparison.OrdinalIgnoreCase));

            try
            {
                return new UnitDef(id, key, section.GetOrDefault("name", key), glyphText[0], unitClass, movement, formation, attributes, attackVsClass);
            }
            catch (ArgumentException error)
            {
                throw new FormatException($"[unit {key}] (line {section.LineNumber}): {error.Message}");
            }
        }
    }
}
