using System;
using System.Collections.Generic;
using BattleChess.Contracts;

namespace BattleChess.Rules.Grid
{
    /// <summary>
    /// Which cells a body covers, and whether two bodies want the same ground.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>[M155].</b> The whole of the fine-grid model is here. A regiment does
    /// not stand on a cell; it lies over the cells its rectangle touches. Two
    /// regiments clash exactly when those sets meet - which is a fact that can be
    /// checked rather than a distance that has to be judged, and judging it from
    /// points is what M152, M153 and M154 were each an attempt at.
    /// </para>
    /// <para>
    /// <b>Found by flood fill, not by scanning a box.</b> A rectangle is convex,
    /// so the cells it covers are connected on any lattice worth the name: start
    /// at the cell holding its centre and walk outward through neighbours,
    /// keeping what is covered. That works on hexes and squares without knowing
    /// which one it is on, where scanning a bounding box in cell coordinates only
    /// works on squares.
    /// </para>
    /// </remarks>
    public static class Occupancy
    {
        /// <summary>
        /// Air kept around a body when its cells are worked out, in metres.
        /// </summary>
        /// <remarks>
        /// A metre. Not spacing - spacing between regiments is a formation
        /// question and lives in <c>RegimentGrid</c>. This exists so that two
        /// bodies drawn exactly edge to edge, which is what a rigid translation
        /// produces, do not both claim the cell their shared edge runs through
        /// and report a clash that is really a rounding error.
        /// </remarks>
        public const float BreathingRoomMetres = 1f;

        /// <summary>How far the fill will walk before it gives up, in cells.</summary>
        /// <remarks>
        /// A guard against a lattice or a body that disagree about scale, not a
        /// limit anybody should reach: a 80 x 40 m regiment on 12,5 m cells is
        /// about 7 x 4, so a hundred thousand is four orders of magnitude of
        /// room. If it ever trips, something is wrong with the cell size rather
        /// than with the regiment.
        /// </remarks>
        public const int MostCellsCovered = 100_000;

        /// <summary>The cells this body lies over.</summary>
        public static List<Coord> Under(ILattice lattice, in OrientedRect body, float margin = BreathingRoomMetres)
        {
            if (lattice == null) throw new ArgumentNullException(nameof(lattice));

            var covered = new List<Coord>();
            var seen = new HashSet<Coord>();
            var queue = new Queue<Coord>();

            Coord start = lattice.Of(body.Centre);

            seen.Add(start);
            queue.Enqueue(start);

            Span<Coord> neighbours = stackalloc Coord[Math.Max(6, lattice.DirectionCount)];

            while (queue.Count > 0)
            {
                Coord cell = queue.Dequeue();

                if (!lattice.Covers(cell, body, margin)) continue;

                covered.Add(cell);

                if (covered.Count > MostCellsCovered)
                    throw new InvalidOperationException(
                        $"A {body.Footprint} body covered more than {MostCellsCovered} cells of " +
                        $"{lattice.Name}. The cell size and the regiment size disagree.");

                lattice.Neighbours(cell, neighbours);

                for (int i = 0; i < lattice.DirectionCount; i++)
                    if (seen.Add(neighbours[i]))
                        queue.Enqueue(neighbours[i]);
            }

            // The centre cell always counts, even where a body is so much smaller
            // than a cell that no cell centre falls inside it. A regiment that
            // occupied nothing could be walked through.
            if (covered.Count == 0) covered.Add(start);

            return covered;
        }

        /// <summary>The cells this regiment lies over, as it stands.</summary>
        public static List<Coord> Under(ILattice lattice, UnitInstance unit, float margin = BreathingRoomMetres)
        {
            if (unit == null) throw new ArgumentNullException(nameof(unit));

            return Under(lattice, unit.Shape, margin);
        }

        /// <summary>
        /// The cells this regiment would lie over if it stood there facing that
        /// way, without moving it.
        /// </summary>
        public static List<Coord> UnderIfItStood(
            ILattice lattice, UnitInstance unit, Vec2 at, Facing front, float margin = BreathingRoomMetres)
        {
            if (unit == null) throw new ArgumentNullException(nameof(unit));

            // Footprint, and never Space. Space is 80 x 6 m - where the MEN are,
            // which the fighting rules read - and Footprint is 80 x 40 m, the
            // block the world collides with, which is what UnitInstance.Shape is
            // built from. The first draft of this used Space, so the muster
            // booked a body forty metres deep and then checked one six metres
            // deep, and cheerfully stood regiments twenty metres apart on a
            // twenty-five metre grid.
            return Under(lattice, new OrientedRect(at, front, unit.Footprint), margin);
        }
    }
}
