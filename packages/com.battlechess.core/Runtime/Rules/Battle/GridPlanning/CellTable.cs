using System;
using BattleChess.Contracts;

namespace BattleChess.Rules.GridPlanning
{
    /// <summary>
    /// The three books an A* over hex cells keeps - where it came from, what it
    /// cost, and whether it is settled - in flat arrays that are never cleared
    /// and never thrown away.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>What this replaces, and why it was the second-largest step.</b> The
    /// search kept a <c>Dictionary&lt;Coord, Coord&gt;</c>, a
    /// <c>Dictionary&lt;Coord, float&gt;</c> and a <c>HashSet&lt;Coord&gt;</c>,
    /// and built all three <b>afresh on every call</b>. Measured on the
    /// Crucible with fields rebuilt, <c>GridExpand</c> was 16,4% of planning,
    /// and almost none of that is the arithmetic of A*: it is three managed
    /// allocations per order, and then a hash, a modulo and a bucket walk per
    /// cell per book, three or four times per neighbour, six neighbours to a
    /// cell.
    /// </para>
    /// <para>
    /// <b>Why open addressing rather than an array over the map.</b> A flat
    /// array indexed by cell is the obvious answer and it needs the cell range
    /// to be bounded, which it is not: <see cref="HexLayout.ToCoord"/> is happy
    /// to name a cell off the edge of the map, the search reaches for
    /// neighbours before it asks whether the going there is anything, and the
    /// fine tier is sixteen times denser than the coarse one, so any fixed
    /// extent is either wrong at one tier or enormous at the other. A probe
    /// table keyed by the coordinate has the same cost per lookup - one mix,
    /// one mask, one comparison - and cannot be out of range.
    /// </para>
    /// <para>
    /// <b>The generation counter is what makes it free between calls.</b>
    /// Emptying the table would be a pass over every slot, which is the cost
    /// this is trying to avoid. Instead each slot carries the number of the
    /// sweep that wrote it, and a slot from an older sweep is empty ground: it
    /// reads as absent, and the first write over it takes it. So a search
    /// begins by incrementing one integer.
    /// </para>
    /// <para>
    /// One per thread, held by the caller. A wing is planned across several and
    /// two searches sharing a table would read each other's cells.
    /// </para>
    /// </remarks>
    internal sealed class CellTable
    {
        // Assigned by Allocate in the constructor; the compiler cannot see
        // through it, so they start empty rather than null.
        private int[] _sweepOf = Array.Empty<int>();
        private int[] _q = Array.Empty<int>();
        private int[] _r = Array.Empty<int>();
        private float[] _cost = Array.Empty<float>();
        private int[] _fromQ = Array.Empty<int>();
        private int[] _fromR = Array.Empty<int>();
        private bool[] _settled = Array.Empty<bool>();

        private int _mask;
        private int _live;
        private int _sweep;

        internal CellTable(int capacity = 1024)
        {
            Allocate(RoundUp(Math.Max(16, capacity)));
        }

        private static int RoundUp(int wanted)
        {
            int size = 16;
            while (size < wanted) size <<= 1;
            return size;
        }

        private void Allocate(int size)
        {
            _sweepOf = new int[size];
            _q = new int[size];
            _r = new int[size];
            _cost = new float[size];
            _fromQ = new int[size];
            _fromR = new int[size];
            _settled = new bool[size];
            _mask = size - 1;
        }

        /// <summary>Forgets everything, in constant time.</summary>
        internal void Open()
        {
            _live = 0;

            // Wrapping is the one case the generation trick has to be told
            // about: at that point a stale slot could carry the number the new
            // sweep is about to use, so the arrays are wiped once every two
            // billion searches rather than never.
            if (++_sweep != int.MinValue) return;

            Array.Clear(_sweepOf, 0, _sweepOf.Length);
            _sweep = 1;
        }

        private static int Mix(int q, int r)
        {
            unchecked
            {
                // Fibonacci mixing on the pair. The plain (q * 397) ^ r that
                // Coord hashes with clusters badly here, because the cells one
                // search touches are a contiguous blob and the low bits of q
                // and r march together.
                uint h = (uint)(q * 0x9E3779B1) ^ (uint)(r * 0x85EBCA77);
                h ^= h >> 15;
                return (int)(h & 0x7FFFFFFF);
            }
        }

        /// <summary>
        /// The slot for a cell, taking a free one if the cell is new.
        /// </summary>
        /// <remarks>
        /// Never fails: the table grows rather than filling, so a probe always
        /// finds either the cell or empty ground.
        /// </remarks>
        private int SlotFor(Coord cell, out bool fresh)
        {
            while (true)
            {
                int at = Mix(cell.Q, cell.R) & _mask;
                bool full = false;

                while (true)
                {
                    if (_sweepOf[at] != _sweep)
                    {
                        // Room for it, or grow and start the probe again.
                        if ((_live + 1) * 2 > _mask + 1) { full = true; break; }

                        _sweepOf[at] = _sweep;
                        _q[at] = cell.Q;
                        _r[at] = cell.R;
                        _settled[at] = false;
                        _cost[at] = 0f;
                        _live++;
                        fresh = true;
                        return at;
                    }

                    if (_q[at] == cell.Q && _r[at] == cell.R)
                    {
                        fresh = false;
                        return at;
                    }

                    at = (at + 1) & _mask;
                }

                if (full) Grow();
            }
        }

        /// <summary>The slot a cell is already in, or -1.</summary>
        private int Find(Coord cell)
        {
            int at = Mix(cell.Q, cell.R) & _mask;

            while (_sweepOf[at] == _sweep)
            {
                if (_q[at] == cell.Q && _r[at] == cell.R) return at;
                at = (at + 1) & _mask;
            }

            return -1;
        }

        private void Grow()
        {
            int[] wasSweep = _sweepOf;
            int[] wasQ = _q, wasR = _r, wasFromQ = _fromQ, wasFromR = _fromR;
            float[] wasCost = _cost;
            bool[] wasSettled = _settled;
            int sweep = _sweep;

            Allocate((_mask + 1) << 1);

            _sweep = sweep;
            _live = 0;

            for (int i = 0; i < wasSweep.Length; i++)
            {
                if (wasSweep[i] != sweep) continue;

                int at = SlotFor(new Coord(wasQ[i], wasR[i]), out _);

                _cost[at] = wasCost[i];
                _fromQ[at] = wasFromQ[i];
                _fromR[at] = wasFromR[i];
                _settled[at] = wasSettled[i];
            }
        }

        /// <summary>What the best route here has cost so far, if it has been reached.</summary>
        internal bool TryCost(Coord cell, out float cost)
        {
            int at = Find(cell);

            if (at < 0) { cost = 0f; return false; }

            cost = _cost[at];
            return true;
        }

        /// <summary>Records a better way of reaching a cell.</summary>
        internal void Reached(Coord cell, float cost, Coord from)
        {
            int at = SlotFor(cell, out _);

            _cost[at] = cost;
            _fromQ[at] = from.Q;
            _fromR[at] = from.R;
        }

        /// <summary>Whether a cell has already been settled.</summary>
        internal bool IsSettled(Coord cell)
        {
            int at = Find(cell);
            return at >= 0 && _settled[at];
        }

        /// <summary>Settles a cell, saying whether it was not already.</summary>
        internal bool Settle(Coord cell)
        {
            int at = SlotFor(cell, out _);

            if (_settled[at]) return false;

            _settled[at] = true;
            return true;
        }

        /// <summary>The cell a settled cell was reached from.</summary>
        internal Coord CameFrom(Coord cell)
        {
            int at = Find(cell);

            if (at < 0) throw new InvalidOperationException($"{cell} was never reached.");

            return new Coord(_fromQ[at], _fromR[at]);
        }
    }
}
