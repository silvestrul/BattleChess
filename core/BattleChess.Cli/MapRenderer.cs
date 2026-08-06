using System;
using System.Collections.Generic;
using BattleChess.Contracts;
using BattleChess.Rules;

namespace BattleChess.Cli
{
    /// <summary>
    /// Draws a battlefield as coloured text.
    /// </summary>
    /// <remarks>
    /// Colour lives here rather than in the terrain content, because it is a
    /// property of <i>this</i> view. Unity will render the same terrain
    /// completely differently, and neither view should be able to disturb the
    /// other by editing shared data.
    /// </remarks>
    public static class MapRenderer
    {
        private static readonly Dictionary<string, ConsoleColor> ColourByTerrainKey =
            new Dictionary<string, ConsoleColor>(StringComparer.OrdinalIgnoreCase)
            {
                ["plains"] = ConsoleColor.Green,
                ["road"] = ConsoleColor.White,
                ["desert"] = ConsoleColor.Yellow,
                ["forest"] = ConsoleColor.DarkGreen,
                ["hill"] = ConsoleColor.DarkYellow,
                ["jungle"] = ConsoleColor.DarkGreen,
                ["river"] = ConsoleColor.Cyan,
                ["mountain"] = ConsoleColor.Gray,
                ["swamp"] = ConsoleColor.DarkMagenta,
                ["deepwater"] = ConsoleColor.Blue
            };

        /// <summary>
        /// A glyph drawn over the terrain, with its own colour. Lets routes and
        /// units be shown without the map or the terrain knowing either exists.
        /// </summary>
        public readonly struct OverlayCell
        {
            public readonly char Glyph;
            public readonly ConsoleColor Colour;

            public OverlayCell(char glyph, ConsoleColor colour)
            {
                Glyph = glyph;
                Colour = colour;
            }

            public bool IsEmpty => Glyph == '\0';
        }

        /// <summary>
        /// Draws the battlefield. An optional overlay of the same dimensions
        /// replaces terrain glyphs wherever it has entries.
        /// </summary>
        public static void Draw(BattleMapDefinition map, ITerrainCatalogue catalogue, bool useColour, OverlayCell[]? overlay = null)
        {
            GridTerrainMap terrain = map.Terrain;
            ConsoleColor original = Console.ForegroundColor;

            for (int row = 0; row < terrain.Rows; row++)
            {
                Console.Write("  ");

                for (int column = 0; column < terrain.Columns; column++)
                {
                    int index = row * terrain.Columns + column;
                    OverlayCell marker = overlay != null && index < overlay.Length ? overlay[index] : default;

                    if (!marker.IsEmpty)
                    {
                        if (useColour)
                            Console.ForegroundColor = marker.Colour;

                        Console.Write(marker.Glyph);
                        continue;
                    }

                    TerrainDef def = catalogue.Get(terrain.AtCell(column, row));

                    if (useColour)
                        Console.ForegroundColor = ColourFor(def);

                    Console.Write(def.Glyph);
                }

                if (useColour)
                    Console.ForegroundColor = original;

                Console.WriteLine();
            }

            if (useColour)
                Console.ForegroundColor = original;
        }

        public static void DrawLegend(ITerrainCatalogue catalogue, bool useColour)
        {
            ConsoleColor original = Console.ForegroundColor;

            Console.WriteLine();
            Console.WriteLine("  Terrain              foot   horse   wheel   notes");
            Console.WriteLine("  ---------------------------------------------------------");

            foreach (TerrainDef def in catalogue.All)
            {
                if (useColour)
                    Console.ForegroundColor = ColourFor(def);

                Console.Write($"  {def.Glyph}  ");

                if (useColour)
                    Console.ForegroundColor = original;

                Console.Write($"{def.DisplayName,-17}");
                Console.Write($"{Describe(def, MovementType.Foot),6}  ");
                Console.Write($"{Describe(def, MovementType.Horse),6}  ");
                Console.Write($"{Describe(def, MovementType.Wheeled),6}   ");
                Console.WriteLine(Notes(def));
            }

            if (useColour)
                Console.ForegroundColor = original;
        }

        private static ConsoleColor ColourFor(TerrainDef def) =>
            ColourByTerrainKey.TryGetValue(def.Key, out ConsoleColor colour) ? colour : ConsoleColor.Gray;

        private static string Describe(TerrainDef def, MovementType movementType)
        {
            float speed = def.SpeedMultiplier(movementType);
            return speed > 0f ? speed.ToString("0.00") : "--";
        }

        private static string Notes(TerrainDef def)
        {
            var notes = new List<string>();

            int elevation = def.Get(TerrainAttributes.Elevation);
            if (elevation != 0) notes.Add($"elevation {elevation}");

            float visionBonus = def.Get(TerrainAttributes.VisionBonus);
            if (visionBonus > 0f) notes.Add($"sees {visionBonus:+0%} further");

            float sightCost = def.Get(TerrainAttributes.SightCost);
            if (sightCost > 1f) notes.Add($"see {1f / sightCost:0%} as far into");

            if (def.Get(TerrainAttributes.Conceals)) notes.Add("conceals");

            float defence = def.Get(TerrainAttributes.DefenceBonus);
            if (Math.Abs(defence) > 0.0001f) notes.Add($"defence {defence:+0%;-0%}");

            return string.Join(", ", notes);
        }
    }
}
