using System;
using System.Collections.Generic;
using BattleChess.Contracts;

namespace BattleChess.Rules.GridPlanning
{
    /// <summary>
    /// One grid of the whole battlefield with every body marked on it, kept
    /// between orders and shared by every regiment of the same size.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why a shared field at all.</b> Measured, laying the grid and marking
    /// the bodies was <b>about half</b> of what an order cost - 0,9 ms of 2,3 -
    /// and it was being done from scratch for all eighty orders of a wing over
    /// a field that does not move between them. Nothing about the marking
    /// depends on <i>which</i> regiment is asking except its footprint, and
    /// there are only four distinct footprints in the whole order of battle, so
    /// one grid per footprint answers everyone.
    /// </para>
    /// <para>
    /// <b>The mover subtracts itself.</b> A regiment must not be blocked by its
    /// own body, so coverage is counted rather than flagged: each sample point
    /// of each cell records <i>how many</i> bodies cover it, and a mover
    /// recomputes its own contribution - one rectangle, a few dozen cells - and
    /// subtracts it. Counting rather than flagging is what makes that correct
    /// where two bodies overlap the same ground, which is common in a line.
    /// </para>
    /// <para>
    /// <b>Staleness cannot be a matter of discipline.</b> The field is keyed by
    /// a hash of every unit's position and facing, so a grid built before
    /// anything moved is simply not found once something has. A wrong answer
    /// here is a route through a regiment that is no longer there, which is
    /// precisely the class of bug this project has spent four attempts on
    /// before; a hash over eighty units costs microseconds against the
    /// milliseconds it saves.
    /// </para>
    /// </remarks>
    internal sealed class SharedField
    {
        /// <summary>How many sample points stand for one cell.</summary>
        /// <remarks>
        /// One is the cell's own centre, which is the plain thing and what this
        /// did first. Seven adds a ring; nineteen adds a second. See
        /// <see cref="RegimentGrid.FillToBlock"/> for what they are counted for.
        /// </remarks>
        internal readonly int Samples;

        internal readonly HexLayout Layout;
        internal readonly float Spacing;
        internal readonly float Fastest;
        internal readonly MovementType Moving;

        private readonly ITerrainMap _terrain;
        private readonly IMovementModel _movement;

        /// <summary>Sample offsets from a cell's centre, in metres.</summary>
        private readonly Vec2[] _offsets;

        /// <summary>How many bodies cover each sample of each touched cell.</summary>
        /// <remarks>
        /// Only cells something reaches appear. A cell with no entry is free at
        /// every sample, which is the overwhelming majority of a battlefield.
        /// </remarks>
        private readonly Dictionary<Coord, byte[]> _cover = new Dictionary<Coord, byte[]>();

        private readonly Dictionary<Coord, float> _going = new Dictionary<Coord, float>();

        internal SharedField(
            HexLayout layout, ITerrainMap terrain, IMovementModel movement, MovementType moving,
            float spacing, float fastest, int samples)
        {
            Layout = layout;
            _terrain = terrain;
            _movement = movement;
            Moving = moving;
            Spacing = spacing;
            Fastest = fastest;
            Samples = samples;

            _offsets = OffsetsFor(samples, layout.CellSize);
        }

        /// <summary>
        /// Where a cell is sampled, as offsets from its centre.
        /// </summary>
        /// <remarks>
        /// Rings at a fraction of the corner radius rather than at it: a sample
        /// exactly on a corner is shared with two neighbours and answers for
        /// ground that is mostly theirs.
        /// </remarks>
        private static Vec2[] OffsetsFor(int samples, float cellSize)
        {
            if (samples <= 1) return new[] { Vec2.Zero };

            var offsets = new List<Vec2>(samples) { Vec2.Zero };

            void Ring(float radius, int count, float turn)
            {
                for (int i = 0; i < count; i++)
                {
                    float angle = MathF.PI * 2f / count * i + turn;
                    offsets.Add(new Vec2(radius * MathF.Cos(angle), radius * MathF.Sin(angle)));
                }
            }

            Ring(cellSize * 0.55f, 6, 0f);

            if (samples > 7)
            {
                Ring(cellSize * 0.80f, 6, MathF.PI / 6f);
                Ring(cellSize * 0.45f, 6, MathF.PI / 6f);
            }

            return offsets.ToArray();
        }

        /// <summary>The world position of one sample of one cell.</summary>
        internal Vec2 SampleAt(Coord cell, int sample)
        {
            Vec2 centre = Layout.ToWorld(cell);
            Vec2 offset = _offsets[sample];
            return new Vec2(centre.X + offset.X, centre.Y + offset.Y);
        }

        /// <summary>How many bodies cover each sample of a cell, or null if none do.</summary>
        internal byte[]? CoverAt(Coord cell) =>
            _cover.TryGetValue(cell, out byte[] counts) ? counts : null;

        /// <summary>Cells something reaches. Everything else is free at every sample.</summary>
        internal IEnumerable<Coord> TouchedCells() => _cover.Keys;

        /// <summary>
        /// Marks one body onto the field, or off it again when
        /// <paramref name="sign"/> is -1.
        /// </summary>
        internal void Mark(
            in OrientedRect body, float acrossReach, float alongReach, int sign,
            Dictionary<Coord, byte[]>? into = null)
        {
            var grown = new OrientedRect(
                body.Centre, body.Facing,
                new Footprint(
                    body.Footprint.Width + 2f * acrossReach,
                    body.Footprint.Depth + 2f * alongReach));

            // Fine enough that no cell the body reaches can be skipped: the
            // distance from a hex centre to a flat side is half the spacing.
            float step = Spacing * 0.45f;
            float span = grown.Footprint.BoundingRadius + Spacing;
            Vec2 centre = grown.Centre;

            Dictionary<Coord, byte[]> target = into ?? _cover;
            var done = new HashSet<Coord>();

            for (float y = centre.Y - span; y <= centre.Y + span + step; y += step)
            for (float x = centre.X - span; x <= centre.X + span + step; x += step)
            {
                Coord cell = Layout.ToCoord(new Vec2(x, y));
                if (!done.Add(cell)) continue;

                byte[]? counts = null;

                for (int i = 0; i < Samples; i++)
                {
                    if (!grown.ContainsPoint(SampleAt(cell, i))) continue;

                    if (counts == null && !target.TryGetValue(cell, out counts))
                    {
                        if (sign < 0) break;   // nothing here to take away

                        counts = new byte[Samples];
                        target[cell] = counts;
                    }

                    if (counts == null) break;

                    int now = counts[i] + sign;
                    counts[i] = (byte)Math.Clamp(now, 0, 255);
                }
            }
        }

        /// <summary>How fast the going is at a cell's centre, cached.</summary>
        internal float GoingAt(Coord cell)
        {
            if (_going.TryGetValue(cell, out float known)) return known;

            Vec2 world = Layout.ToWorld(cell);

            // A cell on the border has its centre a little outside the map
            // while the cell plainly overlaps it. Sample the nearest ground
            // that exists rather than making a band half a cell wide
            // unreachable all the way round the field.
            if (!_terrain.Bounds.Contains(world))
            {
                Vec2 onMap = _terrain.Bounds.Clamp(world);

                if (Vec2.DistanceSquared(onMap, world) > Layout.CellSize * Layout.CellSize)
                {
                    _going[cell] = 0f;
                    return 0f;
                }

                world = onMap;
            }

            float going = _movement.SpeedMultiplier(_terrain.At(world), Moving);
            _going[cell] = going;
            return going;
        }

        internal MapBounds Bounds => _terrain.Bounds;
    }
}
