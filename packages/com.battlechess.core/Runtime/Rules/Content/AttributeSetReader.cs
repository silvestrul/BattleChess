using System;
using System.Collections.Generic;
using BattleChess.Contracts;

namespace BattleChess.Rules
{
    /// <summary>
    /// Turns the leftover settings of a content section into an
    /// <see cref="AttributeSet"/>.
    /// </summary>
    /// <remarks>
    /// Shared by every content loader. Each loader consumes the few settings it
    /// structurally understands, declares those as reserved, and hands the rest
    /// here — where the registry decides what is valid and each key parses its
    /// own value. That is why adding an attribute never touches a loader.
    /// </remarks>
    public static class AttributeSetReader
    {
        public static AttributeSet Read(
            ConfigSection section,
            AttributeRegistry registry,
            IReadOnlyCollection<string> reservedSettings,
            Func<string, bool>? isHandledElsewhere = null)
        {
            if (section == null) throw new ArgumentNullException(nameof(section));
            if (registry == null) throw new ArgumentNullException(nameof(registry));

            var values = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

            foreach (KeyValuePair<string, string> setting in section.Values)
            {
                if (IsReserved(setting.Key, reservedSettings)) continue;
                if (isHandledElsewhere != null && isHandledElsewhere(setting.Key)) continue;

                if (!registry.TryFind(setting.Key, out AttributeKey key))
                    throw new FormatException(
                        $"{section} (line {section.LineNumber}): unknown setting '{setting.Key}'. " +
                        "Check the spelling, or declare it in the attribute registry.");

                try
                {
                    values[key.Name] = key.Parse(setting.Value);
                }
                catch (FormatException error)
                {
                    throw new FormatException($"{section} (line {section.LineNumber}): '{setting.Key}' — {error.Message}");
                }
            }

            return new AttributeSet(values);
        }

        private static bool IsReserved(string name, IReadOnlyCollection<string> reserved)
        {
            foreach (string candidate in reserved)
            {
                if (string.Equals(candidate, name, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }
    }
}
