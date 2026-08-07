using System;
using System.Collections.Generic;
using BattleChess.Contracts;

namespace BattleChess.Tests.Battle
{
    /// <summary>
    /// Keeps everything the rules said, so a test can assert on it.
    /// </summary>
    /// <remarks>
    /// Not a substitute for asserting on state, and not used where state will
    /// do. It earns its place where the effect being tested is a multiplier
    /// buried inside a number that a dozen other rules also move — a charge
    /// landing changes a casualty figure that formation, terrain, cohesion and
    /// the dice all change too, so "did it charge" is far better answered by
    /// the sentence the combat rule writes than by trying to solve backwards
    /// for it.
    /// </remarks>
    public sealed class TranscriptLog : IBattleLog
    {
        private readonly List<string> _lines = new List<string>();

        public IReadOnlyList<string> Lines => _lines;

        public void Record(in BattleLogEntry entry) => _lines.Add(entry.Message);

        /// <summary>How many recorded lines contain a phrase.</summary>
        public int Count(string phrase)
        {
            int found = 0;

            for (int i = 0; i < _lines.Count; i++)
            {
                if (_lines[i].IndexOf(phrase, StringComparison.OrdinalIgnoreCase) >= 0)
                    found++;
            }

            return found;
        }

        /// <summary>Every line containing a phrase, for making a failure legible.</summary>
        public IReadOnlyList<string> Matching(string phrase)
        {
            var found = new List<string>();

            for (int i = 0; i < _lines.Count; i++)
            {
                if (_lines[i].IndexOf(phrase, StringComparison.OrdinalIgnoreCase) >= 0)
                    found.Add(_lines[i]);
            }

            return found;
        }
    }
}
