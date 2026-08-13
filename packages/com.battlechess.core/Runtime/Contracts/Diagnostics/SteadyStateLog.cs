using System;
using System.Collections.Generic;

namespace BattleChess.Contracts
{
    /// <summary>
    /// A log that is told when time passes, so it can tell a moment from a
    /// condition.
    /// </summary>
    public interface ITickedLog : IBattleLog
    {
        /// <summary>Called once per tick, before any system speaks.</summary>
        void Ticked(int tick);
    }

    /// <summary>
    /// Collapses a repeated line into the moment it began and the moment it
    /// stopped.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Written after a recording of one battle came to four and a half thousand
    /// lines from thirty-three places in the rules. Nothing was logging too
    /// many <i>kinds</i> of thing; three call sites were describing a condition
    /// that happened to still be true, once every tick. "Cavalry cannot get past
    /// Cavalry and is going through it" appeared one thousand two hundred and
    /// thirty-eight times, and a regiment jammed for the rest of a battle looked
    /// exactly like a regiment jammed for a moment.
    /// </para>
    /// <para>
    /// The distinction being drawn is between an event and a state. An event —
    /// a charge landing, a regiment breaking, an order given — happens once and
    /// says so once. A state is true for a stretch of time, and what is worth
    /// reading about it is when it started, when it ended, and how long it went
    /// on. Repeating it every tick in between says nothing and buries the
    /// events.
    /// </para>
    /// <para>
    /// Nothing has to declare which it is. An identical line from the same
    /// regiment and the same rule <i>is</i> a state, by observation, so the
    /// collapse needs no cooperation from any call site and cannot be forgotten
    /// at a new one.
    /// </para>
    /// <para>
    /// This is what makes a seizure legible. A regiment shuffling against a
    /// neighbour for half a turn used to be thirty scattered lines that read as
    /// ordinary traffic; it is now one line saying it went on for thirty ticks,
    /// which is a number that looks wrong at a glance.
    /// </para>
    /// </remarks>
    public sealed class SteadyStateLog : ITickedLog
    {
        /// <summary>
        /// Ticks a state may go unrepeated before it counts as over.
        /// </summary>
        /// <remarks>
        /// Not zero. Several rules only run on the combat pulse, one tick in
        /// ten, so a state of theirs is renewed nine ticks after it was last
        /// mentioned and would otherwise be reported as ending and beginning
        /// again six times a turn.
        /// </remarks>
        private const int GraceTicks = 12;

        private readonly struct Key : IEquatable<Key>
        {
            public readonly int Unit;
            public readonly string Category;
            public readonly string Message;

            public Key(int unit, string category, string message)
            {
                Unit = unit;
                Category = category;
                Message = message;
            }

            public bool Equals(Key other) =>
                Unit == other.Unit && Category == other.Category && Message == other.Message;

            public override bool Equals(object? obj) => obj is Key other && Equals(other);

            public override int GetHashCode() =>
                unchecked((Unit * 397 ^ Category.GetHashCode()) * 397 ^ Message.GetHashCode());
        }

        private sealed class Held
        {
            public LogLevel Level;
            public int Began;
            public int LastSeen;
            public int Times;
        }

        private readonly IBattleLog _inner;
        private readonly Dictionary<Key, Held> _holding = new Dictionary<Key, Held>();
        private readonly List<Key> _finished = new List<Key>();

        private int _tick;

        public SteadyStateLog(IBattleLog inner) =>
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));

        /// <summary>How many lines this has kept from being written.</summary>
        public int Suppressed { get; private set; }

        public void Record(in BattleLogEntry entry)
        {
            var key = new Key(entry.Unit.Value, entry.Category, entry.Message);

            if (_holding.TryGetValue(key, out Held? held))
            {
                held.LastSeen = _tick;
                held.Times++;
                Suppressed++;
                return;
            }

            _holding[key] = new Held
            {
                Level = entry.Level,
                Began = _tick,
                LastSeen = _tick,
                Times = 1,
            };

            _inner.Record(entry);
        }

        public void Ticked(int tick)
        {
            _tick = tick;

            _finished.Clear();

            foreach (KeyValuePair<Key, Held> pair in _holding)
            {
                if (tick - pair.Value.LastSeen > GraceTicks)
                    _finished.Add(pair.Key);
            }

            // Gathered first, because closing a state writes a line and the
            // dictionary must not be walked while it is being emptied. Sorted so
            // that two states ending on the same tick are always reported in the
            // same order, whatever the dictionary happened to do — the rules are
            // deterministic and a recording of them should be too.
            _finished.Sort(Order);

            for (int i = 0; i < _finished.Count; i++)
            {
                Key key = _finished[i];
                Held held = _holding[key];
                _holding.Remove(key);

                // Said once and never again — an event, not a state. It has
                // already been reported and there is nothing to add.
                if (held.Times <= 1) continue;

                int lasted = held.LastSeen - held.Began + 1;

                _inner.Record(new BattleLogEntry(
                    held.Level,
                    key.Category,
                    $"— and that held for {lasted} ticks ({held.Times} times over), " +
                    $"ticks {held.Began} to {held.LastSeen}.",
                    new UnitId(key.Unit)));
            }
        }

        /// <summary>Closes every open state, whatever its age.</summary>
        /// <remarks>For the end of a recording, so nothing is left unreported.</remarks>
        public void Flush() => Ticked(_tick + GraceTicks + 1);

        private static int Order(Key a, Key b)
        {
            int byUnit = a.Unit.CompareTo(b.Unit);
            if (byUnit != 0) return byUnit;

            int byCategory = string.CompareOrdinal(a.Category, b.Category);
            return byCategory != 0 ? byCategory : string.CompareOrdinal(a.Message, b.Message);
        }
    }
}
