using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
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

        private StreamWriter _file;
        private int _written;

        public bool ShowInfo = true;
        public bool ShowDecisions = true;

        /// <summary>
        /// Whether anything said is kept on screen.
        /// </summary>
        /// <remarks>
        /// Turned off with the harness. It saves the ring - the appends, and
        /// the trim of a quarter of it every four hundred entries - but it is
        /// worth being exact about what it does <i>not</i> save: the message
        /// was interpolated at the call site, before this was ever asked, so
        /// the string was built either way. Only a change in the rules could
        /// save that, and it is not worth making the simulation pass around
        /// closures to avoid formatting a line.
        /// </remarks>
        public bool Listening = true;

        /// <summary>Where the current recording is being written, if any.</summary>
        public string RecordingPath { get; private set; }

        public bool IsRecording => _file != null;

        /// <summary>Turns the clock number into the log, so entries can be placed in time.</summary>
        public int Tick { get; set; }

        public void Record(in BattleLogEntry entry)
        {
            // Written through as it happens rather than dumped at the end. The
            // on-screen console is a trimmed ring — it drops the oldest quarter
            // once it fills — and the interesting part of a movement problem is
            // usually what happened before anybody noticed.
            if (_file != null)
            {
                string unit = entry.Unit.IsValid ? entry.Unit.ToString() : "-";
                _file.WriteLine($"{Tick,6} {Prefix(entry.Level)} {entry.Category,-9} {unit,-5} {entry.Message}");
                _written++;
            }

            // A recording is deliberate and goes on regardless, which is why
            // this sits below the file write and not above it: switching the
            // harness off must not silently kill a file somebody asked for.
            if (!Listening) return;

            // A ring by trimming: a long session should not grow without bound,
            // and the recent past is what matters when testing by hand.
            if (_entries.Count >= Capacity)
                _entries.RemoveRange(0, Capacity / 4);

            _entries.Add(entry);
            _stickToBottom = true;
        }

        public void Clear() => _entries.Clear();

        /// <summary>
        /// Starts writing everything the simulation says to a file next to the
        /// project.
        /// </summary>
        /// <remarks>
        /// The console explains itself perfectly well on screen and not at all
        /// anywhere else, which makes "here is what happened, what went wrong?"
        /// impossible to answer without sitting at the same machine. A file can
        /// be read by anyone.
        /// </remarks>
        public void StartRecording(string directory)
        {
            StopRecording();

            try
            {
                Directory.CreateDirectory(directory);

                RecordingPath = Path.Combine(directory, $"battle-{DateTime.Now:yyyyMMdd-HHmmss}.log");

                // UTF-8 with a byte-order mark. The rules talk in em-dashes and
                // degree signs, and a log without a mark is guessed at by
                // whatever opens it — on Windows that guess is usually the ANSI
                // codepage, which turns "87°" into "87Â°" and makes a recording
                // harder to read at exactly the moment somebody is trying to
                // work out what went wrong from it.
                _file = new StreamWriter(RecordingPath, append: false, new UTF8Encoding(true))
                {
                    AutoFlush = true,
                };
                _written = 0;

                _file.WriteLine($"# Battle Chess log, started {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                _file.WriteLine("#  tick  level  category  unit  message");
                _file.WriteLine();

                Record(new BattleLogEntry(LogLevel.Info, "Log", $"Recording to {RecordingPath}", UnitId.None));
            }
            catch (Exception failure)
            {
                _file = null;
                RecordingPath = null;

                Record(new BattleLogEntry(LogLevel.Warning, "Log", $"Could not record: {failure.Message}", UnitId.None));
            }
        }

        public void StopRecording()
        {
            if (_file == null) return;

            _file.WriteLine($"# {_written} entries.");
            _file.Flush();
            _file.Dispose();
            _file = null;
        }

        public void Draw(Rect area)
        {
            GUILayout.BeginArea(area, GUI.skin.box);

            GUILayout.BeginHorizontal();
            GUILayout.Label($"Console  ({_entries.Count})", GUILayout.Width(110));
            ShowInfo = GUILayout.Toggle(ShowInfo, "info", GUILayout.Width(50));
            ShowDecisions = GUILayout.Toggle(ShowDecisions, "decisions", GUILayout.Width(80));
            GUILayout.FlexibleSpace();

            if (GUILayout.Button(IsRecording ? "stop recording" : "record to file", GUILayout.Width(120)))
            {
                if (IsRecording) StopRecording();
                else StartRecording(DefaultLogDirectory());
            }

            if (GUILayout.Button("clear", GUILayout.Width(50))) Clear();
            GUILayout.EndHorizontal();

            if (IsRecording)
                GUILayout.Label($"writing {_written} entries to {RecordingPath}");

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

        /// <summary>
        /// A <c>logs</c> folder beside the repository, next to <c>content</c>.
        /// </summary>
        /// <remarks>
        /// Not inside <c>Assets</c>, or Unity imports every log as an asset and
        /// reimports the project each time one is written.
        /// </remarks>
        public static string DefaultLogDirectory()
        {
            var directory = new DirectoryInfo(Application.dataPath);

            while (directory != null)
            {
                if (Directory.Exists(Path.Combine(directory.FullName, "content")))
                    return Path.Combine(directory.FullName, "logs");

                directory = directory.Parent;
            }

            return Path.Combine(Application.persistentDataPath, "logs");
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
