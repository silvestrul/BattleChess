using System;
using System.Collections.Generic;
using BattleChess.Contracts;

namespace BattleChess.Rules
{
    /// <summary>
    /// Builds a <see cref="TerrainCatalogue"/> from a content file.
    /// </summary>
    /// <remarks>
    /// Knows about exactly three things: the section shape, the <c>glyph</c> and
    /// <c>name</c> settings, and the <c>speed.*</c> prefix. Every other setting
    /// is handed to whichever attribute claims that name and parsed by the key
    /// itself — so adding an attribute never touches this file.
    /// </remarks>
    public static class TerrainCatalogueReader
    {
        private const string SectionName = "terrain";
        private const string SpeedPrefix = "speed.";

        private static readonly string[] ReservedSettings = { "glyph", "name" };

        public static TerrainCatalogue Read(string text, AttributeRegistry? registry = null)
        {
            AttributeRegistry attributes = registry ?? TerrainAttributes.Registry;

            ConfigDocument document = ConfigDocument.Parse(text);
            var builder = new TerrainCatalogue.Builder();

            foreach (ConfigSection section in document.SectionsNamed(SectionName))
            {
                if (string.IsNullOrWhiteSpace(section.Argument))
                    throw new FormatException($"Line {section.LineNumber}: [terrain] needs a key, e.g. [terrain plains].");

                builder.Add(ReadTerrain(section, builder.NextId, attributes));
            }

            return builder.Build();
        }

        private static TerrainDef ReadTerrain(ConfigSection section, TerrainId id, AttributeRegistry registry)
        {
            string key = section.Argument!;

            string glyphText = section.Require("glyph");
            if (glyphText.Length != 1)
                throw new FormatException($"[terrain {key}] (line {section.LineNumber}): glyph must be exactly one character, got '{glyphText}'.");

            var speeds = new Dictionary<MovementType, float>();

            foreach (KeyValuePair<string, string> setting in section.Values)
            {
                if (!setting.Key.StartsWith(SpeedPrefix, StringComparison.OrdinalIgnoreCase)) continue;

                string movementName = setting.Key.Substring(SpeedPrefix.Length);

                if (!Enum.TryParse(movementName, ignoreCase: true, out MovementType movementType))
                    throw new FormatException($"[terrain {key}] (line {section.LineNumber}): unknown movement type '{movementName}'.");

                speeds[movementType] = ParseSpeed(key, section.LineNumber, setting.Key, setting.Value);
            }

            AttributeSet attributes = AttributeSetReader.Read(
                section,
                registry,
                ReservedSettings,
                name => name.StartsWith(SpeedPrefix, StringComparison.OrdinalIgnoreCase));

            // An undeclared speed means impassable, which lets terrain that
            // simply cannot take wheels stay silent about it.
            return new TerrainDef(
                id,
                key,
                section.GetOrDefault("name", key),
                glyphText[0],
                speeds,
                attributes);
        }

        private static float ParseSpeed(string terrainKey, int lineNumber, string settingName, string raw)
        {
            float value;
            try
            {
                value = AttributeParsers.Float(raw);
            }
            catch (FormatException error)
            {
                throw new FormatException($"[terrain {terrainKey}] (line {lineNumber}): '{settingName}' — {error.Message}");
            }

            if (value < 0f)
                throw new FormatException($"[terrain {terrainKey}] (line {lineNumber}): '{settingName}' cannot be negative.");

            return value;
        }
    }
}
