using System;
using System.Collections.Generic;

namespace BattleChess.Rules
{
    /// <summary>
    /// One <c>[section]</c> of a content file.
    /// </summary>
    public sealed class ConfigSection
    {
        /// <summary>Section kind, e.g. "terrain" in <c>[terrain plains]</c>.</summary>
        public string Name { get; }

        /// <summary>Optional qualifier, e.g. "plains" in <c>[terrain plains]</c>.</summary>
        public string? Argument { get; }

        /// <summary>Line number the section header appeared on, for error messages.</summary>
        public int LineNumber { get; }

        public IReadOnlyDictionary<string, string> Values { get; }

        /// <summary>Lines kept verbatim, used by block sections such as map tiles.</summary>
        public IReadOnlyList<string> RawLines { get; }

        internal ConfigSection(string name, string? argument, int lineNumber,
            Dictionary<string, string> values, List<string> rawLines)
        {
            Name = name;
            Argument = argument;
            LineNumber = lineNumber;
            Values = values;
            RawLines = rawLines;
        }

        public string Require(string key) =>
            Values.TryGetValue(key, out string? value)
                ? value
                : throw new FormatException($"[{Name} {Argument}] (line {LineNumber}) is missing required setting '{key}'.");

        public string GetOrDefault(string key, string fallback) =>
            Values.TryGetValue(key, out string? value) ? value : fallback;

        public override string ToString() => Argument == null ? $"[{Name}]" : $"[{Name} {Argument}]";
    }

    /// <summary>
    /// A minimal line-based content format: <c># comments</c>,
    /// <c>key = value</c> settings, and <c>[section]</c> headers.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Hand-rolled rather than JSON, for two reasons. It removes a dependency
    /// that would otherwise have to be resolved identically by both the .NET
    /// build and Unity's package manager. And more importantly it is
    /// open-ended: the parser has no schema, so a content file can introduce a
    /// setting the parser has never heard of and it arrives intact for whoever
    /// does understand it. That is what lets a new terrain attribute be added
    /// without touching any loading code.
    /// </para>
    /// <para>
    /// Sections named in <c>rawSectionNames</c> keep their lines verbatim
    /// instead of being read as settings, which is how map tile blocks survive
    /// containing arbitrary characters.
    /// </para>
    /// </remarks>
    public sealed class ConfigDocument
    {
        /// <summary>Settings that appeared before any section header.</summary>
        public IReadOnlyDictionary<string, string> Root { get; }

        public IReadOnlyList<ConfigSection> Sections { get; }

        private ConfigDocument(Dictionary<string, string> root, List<ConfigSection> sections)
        {
            Root = root;
            Sections = sections;
        }

        public string RequireRoot(string key) =>
            Root.TryGetValue(key, out string? value)
                ? value
                : throw new FormatException($"Missing required setting '{key}'.");

        public string RootOrDefault(string key, string fallback) =>
            Root.TryGetValue(key, out string? value) ? value : fallback;

        public IEnumerable<ConfigSection> SectionsNamed(string name)
        {
            foreach (ConfigSection section in Sections)
            {
                if (string.Equals(section.Name, name, StringComparison.OrdinalIgnoreCase))
                    yield return section;
            }
        }

        public ConfigSection? FirstSectionNamed(string name)
        {
            foreach (ConfigSection section in SectionsNamed(name))
                return section;

            return null;
        }

        public static ConfigDocument Parse(string text, params string[] rawSectionNames)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));

            var root = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var sections = new List<ConfigSection>();

            string currentName = string.Empty;
            string? currentArgument = null;
            int currentLineNumber = 0;
            bool inSection = false;
            bool currentIsRaw = false;

            var currentValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var currentRawLines = new List<string>();

            void CloseSection()
            {
                if (inSection)
                    sections.Add(new ConfigSection(currentName, currentArgument, currentLineNumber, currentValues, currentRawLines));
            }

            string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

            for (int i = 0; i < lines.Length; i++)
            {
                string rawLine = lines[i];
                int lineNumber = i + 1;

                // Inside a raw block, only a section header ends the block. Tile
                // rows are kept exactly as written, leading spaces and all.
                bool isHeader = rawLine.TrimStart().StartsWith("[", StringComparison.Ordinal);

                if (currentIsRaw && inSection && !isHeader)
                {
                    if (rawLine.TrimStart().StartsWith("#", StringComparison.Ordinal)) continue;
                    if (rawLine.Trim().Length == 0) continue;

                    currentRawLines.Add(rawLine.TrimEnd());
                    continue;
                }

                string line = rawLine.Trim();

                if (line.Length == 0) continue;
                if (line.StartsWith("#", StringComparison.Ordinal)) continue;

                if (isHeader)
                {
                    if (!line.EndsWith("]", StringComparison.Ordinal))
                        throw new FormatException($"Line {lineNumber}: section header '{line}' is missing its closing bracket.");

                    CloseSection();

                    string header = line.Substring(1, line.Length - 2).Trim();
                    if (header.Length == 0)
                        throw new FormatException($"Line {lineNumber}: section header is empty.");

                    int space = header.IndexOf(' ');
                    currentName = space < 0 ? header : header.Substring(0, space);
                    currentArgument = space < 0 ? null : header.Substring(space + 1).Trim();
                    currentLineNumber = lineNumber;
                    inSection = true;

                    currentIsRaw = false;
                    foreach (string rawName in rawSectionNames)
                    {
                        if (string.Equals(currentName, rawName, StringComparison.OrdinalIgnoreCase))
                        {
                            currentIsRaw = true;
                            break;
                        }
                    }

                    currentValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    currentRawLines = new List<string>();
                    continue;
                }

                int separator = line.IndexOf('=');
                if (separator < 0)
                    throw new FormatException($"Line {lineNumber}: expected 'key = value' or a [section] header, got '{line}'.");

                string key = line.Substring(0, separator).Trim();
                string value = line.Substring(separator + 1).Trim();

                if (key.Length == 0)
                    throw new FormatException($"Line {lineNumber}: setting has no name.");

                Dictionary<string, string> target = inSection ? currentValues : root;

                if (target.ContainsKey(key))
                    throw new FormatException($"Line {lineNumber}: '{key}' is set more than once in the same scope.");

                target[key] = value;
            }

            CloseSection();

            return new ConfigDocument(root, sections);
        }
    }
}
