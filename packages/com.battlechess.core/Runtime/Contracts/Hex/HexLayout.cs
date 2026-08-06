using System;

namespace BattleChess.Contracts
{
    /// <summary>
    /// Converts between hex coordinates and continuous world positions.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The hex grids in this project are invisible internal tools — one is an A*
    /// search space for route planning, another will hold fog-of-war state. A
    /// unit's real position is a <see cref="Vec2"/>, never a <see cref="Coord"/>.
    /// This type is the bridge between the two worlds, used when seeding a path
    /// search from a unit's position and when turning the resulting route back
    /// into waypoints.
    /// </para>
    /// <para>
    /// <b>Pointy-top only, deliberately.</b> The names in
    /// <see cref="HexDirection"/> (east, north-east, …) describe where a
    /// pointy-top hex's six neighbours actually lie. A flat-top grid puts its
    /// neighbours at different bearings — north and south, but no due east — so
    /// supporting it would mean renaming or remapping the direction set. Since
    /// the grid is never drawn, orientation is a pure implementation detail with
    /// no visible payoff, so there is nothing to gain by supporting both.
    /// </para>
    /// <para>
    /// World space is Y-up (+X east, +Y north), which is what makes the direction
    /// names line up with real bearings.
    /// </para>
    /// </remarks>
    public readonly struct HexLayout
    {
        private static readonly float Sqrt3 = MathF.Sqrt(3f);

        // Hex -> world, in units of CellSize.
        private static readonly float ForwardQx = Sqrt3;
        private static readonly float ForwardRx = Sqrt3 * 0.5f;
        private const float ForwardQy = 0f;
        private const float ForwardRy = -1.5f;

        // World -> hex, the inverse of the matrix above.
        private static readonly float InverseXq = Sqrt3 / 3f;
        private const float InverseYq = 1f / 3f;
        private const float InverseXr = 0f;
        private const float InverseYr = -2f / 3f;

        /// <summary>
        /// Distance from a hex's centre to any of its corners, in metres.
        /// Note this is <i>not</i> the spacing between neighbouring hexes — see
        /// <see cref="NeighbourDistance"/>.
        /// </summary>
        public readonly float CellSize;

        /// <summary>World position of hex (0, 0).</summary>
        public readonly Vec2 Origin;

        public HexLayout(float cellSize, Vec2 origin = default)
        {
            if (!(cellSize > 0f) || float.IsInfinity(cellSize))
                throw new ArgumentOutOfRangeException(nameof(cellSize), cellSize, "Cell size must be finite and positive.");

            CellSize = cellSize;
            Origin = origin;
        }

        /// <summary>
        /// Builds a layout from the centre-to-centre spacing of adjacent hexes,
        /// which is usually the more natural thing to specify — "cells are 2 m
        /// apart" is a statement about resolution, whereas the corner radius is
        /// an implementation detail.
        /// </summary>
        public static HexLayout FromNeighbourDistance(float metresBetweenCentres, Vec2 origin = default)
        {
            if (!(metresBetweenCentres > 0f) || float.IsInfinity(metresBetweenCentres))
                throw new ArgumentOutOfRangeException(nameof(metresBetweenCentres), metresBetweenCentres, "Spacing must be finite and positive.");

            return new HexLayout(metresBetweenCentres / Sqrt3, origin);
        }

        /// <summary>Centre-to-centre distance between any two adjacent hexes, in metres.</summary>
        public float NeighbourDistance => Sqrt3 * CellSize;

        /// <summary>Full width of one hex, in metres (flat side to flat side).</summary>
        public float CellWidth => Sqrt3 * CellSize;

        /// <summary>Full height of one hex, in metres (point to point).</summary>
        public float CellHeight => 2f * CellSize;

        /// <summary>The world position of a hex's centre.</summary>
        public Vec2 ToWorld(Coord coord)
        {
            float x = (ForwardQx * coord.Q + ForwardRx * coord.R) * CellSize;
            float y = (ForwardQy * coord.Q + ForwardRy * coord.R) * CellSize;
            return new Vec2(x + Origin.X, y + Origin.Y);
        }

        /// <summary>
        /// The exact, unrounded hex coordinate containing a world position.
        /// Useful when interpolating; most callers want <see cref="ToCoord"/>.
        /// </summary>
        public FractionalCoord ToFractionalCoord(Vec2 world)
        {
            float x = (world.X - Origin.X) / CellSize;
            float y = (world.Y - Origin.Y) / CellSize;

            return new FractionalCoord(
                InverseXq * x + InverseYq * y,
                InverseXr * x + InverseYr * y);
        }

        /// <summary>The hex containing a world position.</summary>
        public Coord ToCoord(Vec2 world) => HexMath.Round(ToFractionalCoord(world));

        /// <summary>
        /// The six corners of a hex, anticlockwise starting from the one at 30°.
        /// </summary>
        public void GetCorners(Coord coord, Span<Vec2> destination)
        {
            if (destination.Length < HexMath.DirectionCount)
                throw new ArgumentException($"Needs room for {HexMath.DirectionCount} corners.", nameof(destination));

            Vec2 centre = ToWorld(coord);

            for (int i = 0; i < HexMath.DirectionCount; i++)
            {
                // Pointy-top corners sit half a step round from the neighbour
                // bearings, hence the 0.5 offset.
                float angle = MathF.PI / 3f * (i + 0.5f);
                destination[i] = new Vec2(
                    centre.X + CellSize * MathF.Cos(angle),
                    centre.Y + CellSize * MathF.Sin(angle));
            }
        }

        public Vec2[] GetCorners(Coord coord)
        {
            var corners = new Vec2[HexMath.DirectionCount];
            GetCorners(coord, corners);
            return corners;
        }

        public override string ToString() => $"HexLayout(cell {CellSize:0.###}m, spacing {NeighbourDistance:0.###}m, origin {Origin})";
    }
}
