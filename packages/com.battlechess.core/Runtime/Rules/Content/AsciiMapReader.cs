using System;
using System.Collections.Generic;
using BattleChess.Contracts;

namespace BattleChess.Rules
{
    /// <summary>
    /// A battlefield as loaded from content: its name and its terrain.
    /// </summary>
    public sealed class BattleMapDefinition
    {
        public string Name { get; }
        public GridTerrainMap Terrain { get; }

        public BattleMapDefinition(string name, GridTerrainMap terrain)
        {
            Name = name;
            Terrain = terrain ?? throw new ArgumentNullException(nameof(terrain));
        }

        public override string ToString() => $"{Name} ({Terrain.Columns}×{Terrain.Rows} @ {Terrain.CellSize:0.#}m)";
    }

    /// <summary>
    /// Reads a battlefield written as a block of characters.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Maps are authored coarse — one character covers <c>cellSize</c> metres,
    /// typically 25 — so a kilometre of frontage is forty characters wide and
    /// can be read and edited by a person. The pathfinder later samples this at
    /// its own far finer resolution; the two numbers are independent.
    /// </para>
    /// <para>
    /// Characters are resolved through the terrain catalogue's glyphs, so the
    /// map file carries no legend of its own and cannot drift out of step with
    /// the terrain definitions.
    /// </para>
    /// </remarks>
    public static class AsciiMapReader
    {
        private const string TilesSection = "tiles";

        public static BattleMapDefinition Read(string text, ITerrainCatalogue catalogue)
        {
            if (catalogue == null) throw new ArgumentNullException(nameof(catalogue));

            ConfigDocument document = ConfigDocument.Parse(text, TilesSection);

            string name = document.RootOrDefault("name", "Unnamed battlefield");
            float cellSize = AttributeParsers.Float(document.RequireRoot("cellSize"));

            if (!(cellSize > 0f))
                throw new FormatException($"cellSize must be positive, got {cellSize}.");

            ConfigSection tiles = document.FirstSectionNamed(TilesSection)
                ?? throw new FormatException("Map is missing its [tiles] block.");

            IReadOnlyList<string> rows = tiles.RawLines;
            if (rows.Count == 0)
                throw new FormatException("[tiles] block is empty.");

            int columns = rows[0].Length;
            for (int row = 0; row < rows.Count; row++)
            {
                if (rows[row].Length != columns)
                    throw new FormatException(
                        $"Map rows must all be the same width. Row 1 is {columns} characters, row {row + 1} is {rows[row].Length}.");
            }

            var cells = new TerrainId[columns * rows.Count];

            for (int row = 0; row < rows.Count; row++)
            {
                string line = rows[row];

                for (int column = 0; column < columns; column++)
                {
                    char glyph = line[column];

                    if (!catalogue.TryGetByGlyph(glyph, out TerrainDef def))
                        throw new FormatException(
                            $"Row {row + 1}, column {column + 1}: no terrain uses the character '{glyph}'.");

                    cells[row * columns + column] = def.Id;
                }
            }

            return new BattleMapDefinition(name, new GridTerrainMap(columns, rows.Count, cellSize, cells));
        }
    }
}
