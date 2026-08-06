using System;
using BattleChess.Contracts;

namespace BattleChess.Rules
{
    /// <summary>
    /// A terrain field backed by a rectangular grid of authored cells.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The backing grid is <b>square, not hex</b>, and that is not an
    /// inconsistency. This grid is only an authoring convenience — a readable
    /// way to write a battlefield down as text. The hex grid used for
    /// pathfinding is a separate structure that samples this field at its own,
    /// much finer resolution. Nothing outside this class knows the shape of
    /// either.
    /// </para>
    /// <para>
    /// Row 0 is the northern edge, matching how a map reads on the page, so
    /// world Y decreases as the row index grows.
    /// </para>
    /// </remarks>
    public sealed class GridTerrainMap : ITerrainMap
    {
        private readonly TerrainId[] _cells;
        private readonly float _cellSize;

        public int Columns { get; }
        public int Rows { get; }
        public MapBounds Bounds { get; }

        /// <summary>Edge length of one authored cell, in metres.</summary>
        public float CellSize => _cellSize;

        public GridTerrainMap(int columns, int rows, float cellSize, TerrainId[] cells, Vec2 southWestCorner = default)
        {
            if (columns <= 0) throw new ArgumentOutOfRangeException(nameof(columns), columns, "Need at least one column.");
            if (rows <= 0) throw new ArgumentOutOfRangeException(nameof(rows), rows, "Need at least one row.");
            if (!(cellSize > 0f) || float.IsInfinity(cellSize))
                throw new ArgumentOutOfRangeException(nameof(cellSize), cellSize, "Cell size must be finite and positive.");
            if (cells == null) throw new ArgumentNullException(nameof(cells));
            if (cells.Length != columns * rows)
                throw new ArgumentException($"Expected {columns * rows} cells for a {columns}×{rows} map, got {cells.Length}.", nameof(cells));

            Columns = columns;
            Rows = rows;
            _cellSize = cellSize;
            _cells = cells;

            Bounds = new MapBounds(
                southWestCorner,
                new Vec2(southWestCorner.X + columns * cellSize, southWestCorner.Y + rows * cellSize));
        }

        public TerrainId At(Vec2 worldPosition)
        {
            if (!Bounds.Contains(worldPosition))
                return TerrainId.None;

            int column = (int)MathF.Floor((worldPosition.X - Bounds.Min.X) / _cellSize);
            int row = (int)MathF.Floor((Bounds.Max.Y - worldPosition.Y) / _cellSize);

            // A point exactly on the northern or eastern edge lands one past the
            // last cell; treat it as belonging to that cell.
            column = Math.Clamp(column, 0, Columns - 1);
            row = Math.Clamp(row, 0, Rows - 1);

            return _cells[row * Columns + column];
        }

        /// <summary>Terrain at an authored cell, by grid position.</summary>
        public TerrainId AtCell(int column, int row)
        {
            if ((uint)column >= Columns) throw new ArgumentOutOfRangeException(nameof(column), column, "Outside the map.");
            if ((uint)row >= Rows) throw new ArgumentOutOfRangeException(nameof(row), row, "Outside the map.");

            return _cells[row * Columns + column];
        }

        /// <summary>World position of an authored cell's centre.</summary>
        public Vec2 CellCentre(int column, int row) => new Vec2(
            Bounds.Min.X + (column + 0.5f) * _cellSize,
            Bounds.Max.Y - (row + 0.5f) * _cellSize);
    }
}
