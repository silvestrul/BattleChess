using System.Collections.Generic;
using BattleChess.Contracts;
using UnityEngine;

namespace BattleChess.Unity
{
    /// <summary>
    /// Collects the simulation's commentary and draws it as a scrolling console.
    /// </summary>
    /// <remarks>
    /// Implements <see cref="IBattleLog"/>, so it is simply handed to whatever
    /// wants to explain itself. The simulation has no idea Unity exists.
    /// </remarks>
    public sealed class DebugConsole : IBattleLog
    {
        private const int Capacity = 400;

        private readonly List<BattleLogEntry> _entries = new List<BattleLogEntry>(Capacity);
        private Vector2 _scroll;
        private bool _stickToBottom = true;

        public bool ShowInfo = true;
        public bool ShowDecisions = true;

        public void Record(in BattleLogEntry entry)
        {
            // A ring by trimming: a long session should not grow without bound,
            // and the recent past is what matters when testing by hand.
            if (_entries.Count >= Capacity)
                _entries.RemoveRange(0, Capacity / 4);

            _entries.Add(entry);
            _stickToBottom = true;
        }

        public void Clear() => _entries.Clear();

        public void Draw(Rect area)
        {
            GUILayout.BeginArea(area, GUI.skin.box);

            GUILayout.BeginHorizontal();
            GUILayout.Label($"Console  ({_entries.Count})", GUILayout.Width(110));
            ShowInfo = GUILayout.Toggle(ShowInfo, "info", GUILayout.Width(50));
            ShowDecisions = GUILayout.Toggle(ShowDecisions, "decisions", GUILayout.Width(80));
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("clear", GUILayout.Width(50))) Clear();
            GUILayout.EndHorizontal();

            _scroll = GUILayout.BeginScrollView(_scroll);

            Color original = GUI.contentColor;

            for (int i = 0; i < _entries.Count; i++)
            {
                BattleLogEntry entry = _entries[i];

                if (entry.Level == LogLevel.Info && !ShowInfo) continue;
                if (entry.Level == LogLevel.Decision && !ShowDecisions) continue;

                GUI.contentColor = ColourFor(entry.Level);

                string unit = entry.Unit.IsValid ? $"{entry.Unit} " : string.Empty;
                GUILayout.Label($"{Prefix(entry.Level)} {entry.Category,-8} {unit}{entry.Message}");
            }

            GUI.contentColor = original;

            GUILayout.EndScrollView();

            // Follow new output unless the reader has scrolled away to look at
            // something.
            if (_stickToBottom && Event.current.type == EventType.Repaint)
            {
                _scroll.y = float.MaxValue;
                _stickToBottom = false;
            }

            GUILayout.EndArea();
        }

        private static string Prefix(LogLevel level) => level switch
        {
            LogLevel.Blocked => "X",
            LogLevel.Warning => "!",
            LogLevel.Decision => ">",
            _ => " "
        };

        private static Color ColourFor(LogLevel level) => level switch
        {
            LogLevel.Blocked => new Color(1f, 0.55f, 0.5f),
            LogLevel.Warning => new Color(1f, 0.85f, 0.4f),
            LogLevel.Decision => new Color(0.7f, 0.9f, 1f),
            _ => new Color(0.8f, 0.8f, 0.8f)
        };
    }
}
