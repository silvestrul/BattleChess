using System;
using BattleChess.Contracts;

namespace BattleChess.Rules
{
    /// <summary>
    /// A binary min-heap of hex cells keyed by float priority.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Hand-written because <c>System.Collections.Generic.PriorityQueue</c>
    /// arrived in .NET 6 and does not exist on netstandard2.1, which is what
    /// Unity compiles this against.
    /// </para>
    /// <para>
    /// Equal priorities break ties on insertion order rather than arbitrarily.
    /// Two cells reached by equally good routes are common, and without a
    /// defined tie-break the search could return different-but-equal paths
    /// between runs — which would show up later as replays that drift.
    /// </para>
    /// </remarks>
    internal sealed class CoordMinHeap
    {
        private struct Entry
        {
            public Coord Cell;
            public float Priority;
            public int Sequence;
        }

        private Entry[] _entries;
        private int _count;
        private int _nextSequence;

        public CoordMinHeap(int capacity = 256)
        {
            _entries = new Entry[Math.Max(4, capacity)];
        }

        public int Count => _count;

        public void Clear()
        {
            _count = 0;
            _nextSequence = 0;
        }

        public void Push(Coord cell, float priority)
        {
            if (_count == _entries.Length)
                Array.Resize(ref _entries, _entries.Length * 2);

            _entries[_count] = new Entry { Cell = cell, Priority = priority, Sequence = _nextSequence++ };
            SiftUp(_count);
            _count++;
        }

        public bool TryPop(out Coord cell)
        {
            if (_count == 0)
            {
                cell = Coord.Zero;
                return false;
            }

            cell = _entries[0].Cell;
            _count--;

            if (_count > 0)
            {
                _entries[0] = _entries[_count];
                SiftDown(0);
            }

            return true;
        }

        private void SiftUp(int index)
        {
            Entry item = _entries[index];

            while (index > 0)
            {
                int parent = (index - 1) / 2;
                if (!IsBefore(item, _entries[parent])) break;

                _entries[index] = _entries[parent];
                index = parent;
            }

            _entries[index] = item;
        }

        private void SiftDown(int index)
        {
            Entry item = _entries[index];

            while (true)
            {
                int left = index * 2 + 1;
                if (left >= _count) break;

                int right = left + 1;
                int best = right < _count && IsBefore(_entries[right], _entries[left]) ? right : left;

                if (!IsBefore(_entries[best], item)) break;

                _entries[index] = _entries[best];
                index = best;
            }

            _entries[index] = item;
        }

        private static bool IsBefore(in Entry a, in Entry b) =>
            a.Priority != b.Priority ? a.Priority < b.Priority : a.Sequence < b.Sequence;
    }
}
