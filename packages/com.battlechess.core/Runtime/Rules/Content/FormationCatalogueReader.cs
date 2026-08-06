using System;
using BattleChess.Contracts;

namespace BattleChess.Rules
{
    /// <summary>
    /// Builds a <see cref="FormationCatalogue"/> from a content file.
    /// </summary>
    public static class FormationCatalogueReader
    {
        private const string SectionName = "formation";

        public static FormationCatalogue Read(string text)
        {
            ConfigDocument document = ConfigDocument.Parse(text);
            var builder = new FormationCatalogue.Builder();

            foreach (ConfigSection section in document.SectionsNamed(SectionName))
            {
                if (string.IsNullOrWhiteSpace(section.Argument))
                    throw new FormatException($"Line {section.LineNumber}: [formation] needs a key, e.g. [formation column].");

                builder.Add(ReadFormation(section, builder.NextId));
            }

            return builder.Build();
        }

        private static FormationDef ReadFormation(ConfigSection section, FormationId id)
        {
            string key = section.Argument!;

            string glyphText = section.GetOrDefault("glyph", key.Substring(0, 1).ToUpperInvariant());
            if (glyphText.Length != 1)
                throw new FormatException($"[formation {key}] (line {section.LineNumber}): glyph must be exactly one character, got '{glyphText}'.");

            try
            {
                return new FormationDef(
                    id,
                    key,
                    section.GetOrDefault("name", key),
                    glyphText[0],
                    AttributeParsers.Float(section.GetOrDefault("rankMultiplier", "1")),
                    AttributeParsers.Float(section.GetOrDefault("fileWidthMultiplier", "1")),
                    AttributeParsers.Float(section.GetOrDefault("rankDepthMultiplier", "1")),
                    AttributeParsers.Float(section.GetOrDefault("organizationCost", "0")),
                    AttributeParsers.Float(section.GetOrDefault("stoppingMultiplier", "1")),
                    AttributeParsers.Float(section.GetOrDefault("breakthroughMultiplier", "1")),
                    AttributeParsers.Float(section.GetOrDefault("rangedVulnerability", "1")),
                    section.GetOrDefault("description", string.Empty));
            }
            catch (Exception error) when (error is FormatException or ArgumentException)
            {
                throw new FormatException($"[formation {key}] (line {section.LineNumber}): {error.Message}");
            }
        }
    }
}
