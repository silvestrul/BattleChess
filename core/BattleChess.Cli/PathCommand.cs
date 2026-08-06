using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using BattleChess.Contracts;
using BattleChess.Rules;

namespace BattleChess.Cli
{
    /// <summary>
    /// Plans a route and draws it over the map, so route quality can be judged
    /// by eye rather than by reading coordinates.
    /// </summary>
    public static class PathCommand
    {
        /// <summary>Open-ground speeds, matching the calibration in the plan.</summary>
        private static readonly Dictionary<MovementType, float> BaseSpeeds = new Dictionary<MovementType, float>
        {
            [MovementType.Foot] = 1.59f,
            [MovementType.Horse] = 4.76f,
            [MovementType.Wheeled] = 1.30f
        };

        private const float SecondsPerTurn = 60f;

        public static int Run(string[] args)
        {
            if (args.Length < 4)
            {
                Console.Error.WriteLine("Usage: path <map> <fromCol,fromRow> <toCol,toRow> [--type foot|horse|wheeled] [--no-colour]");
                return 1;
            }

            bool useColour = !Array.Exists(args, a => a == "--no-colour" || a == "--no-color");
            MovementType movementType = ReadMovementType(args);

            TerrainCatalogue catalogue = TerrainCatalogueReader.Read(File.ReadAllText(ContentLocator.TerrainFile()));
            BattleMapDefinition map = AsciiMapReader.Read(File.ReadAllText(ContentLocator.MapFile(args[1])), catalogue);
            GridTerrainMap terrain = map.Terrain;

            (int fromColumn, int fromRow) = ReadCell(args[2], terrain);
            (int toColumn, int toRow) = ReadCell(args[3], terrain);

            Vec2 from = terrain.CellCentre(fromColumn, fromRow);
            Vec2 to = terrain.CellCentre(toColumn, toRow);

            float? clearance = ReadClearance(args);
            bool routeLikeAi = Array.Exists(args, a => a == "--ai");

            var movement = new TerrainMovementModel(catalogue);

            // A player's order goes where it was pointed and only detours around
            // what cannot be crossed. The AI is the one allowed to decide a
            // longer way round is worth it because the going is better.
            IPathfinder pathfinder = routeLikeAi
                ? new HexPathfinder(terrain, movement, catalogue, clearanceMetres: clearance)
                : new DirectPathfinder(terrain, movement, catalogue, clearanceMetres: clearance);

            PathResult path = pathfinder.FindPath(from, to, movementType);

            Console.WriteLine();
            Console.WriteLine($"  {map.Name} — {movementType} from ({fromColumn},{fromRow}) to ({toColumn},{toRow})" +
                              $"   [{(routeLikeAi ? "AI: fastest route" : "direct: shortest route")}]");
            Console.WriteLine();

            if (!path.Found)
            {
                Console.WriteLine($"  No route.  [{path.Failure}]");
                Console.WriteLine($"  {path.FailureDetail}");
                Console.WriteLine();
                Console.WriteLine($"  Start terrain: {catalogue.Get(terrain.At(from)).DisplayName}");
                Console.WriteLine($"  Goal terrain:  {catalogue.Get(terrain.At(to)).DisplayName}");

                if (path.Failure == PathFailure.GoalTooTight)
                    Console.WriteLine("  Try --clearance 0 to confirm a narrower unit could get there.");

                Console.WriteLine();
                return 0;
            }

            MapRenderer.OverlayCell[] overlay = BuildOverlay(path, terrain, fromColumn, fromRow, toColumn, toRow);
            MapRenderer.Draw(map, catalogue, useColour, overlay);

            PrintSummary(path, movementType, catalogue, terrain);
            return 0;
        }

        /// <summary>
        /// Marks every authored cell the smoothed route passes through, by
        /// walking the route itself rather than the search grid — so what is
        /// drawn is what a unit would actually walk.
        /// </summary>
        private static MapRenderer.OverlayCell[] BuildOverlay(PathResult path, GridTerrainMap terrain, int fromColumn, int fromRow, int toColumn, int toRow)
        {
            var overlay = new MapRenderer.OverlayCell[terrain.Columns * terrain.Rows];

            float step = MathF.Max(terrain.CellSize * 0.25f, 1f);

            for (int i = 1; i < path.Waypoints.Count; i++)
            {
                Vec2 a = path.Waypoints[i - 1];
                Vec2 b = path.Waypoints[i];

                int samples = Math.Max(1, (int)MathF.Ceiling(Vec2.Distance(a, b) / step));

                for (int s = 0; s <= samples; s++)
                {
                    Vec2 point = Vec2.Lerp(a, b, s / (float)samples);
                    if (!terrain.Bounds.Contains(point)) continue;

                    int column = (int)MathF.Floor((point.X - terrain.Bounds.Min.X) / terrain.CellSize);
                    int row = (int)MathF.Floor((terrain.Bounds.Max.Y - point.Y) / terrain.CellSize);

                    column = Math.Clamp(column, 0, terrain.Columns - 1);
                    row = Math.Clamp(row, 0, terrain.Rows - 1);

                    overlay[row * terrain.Columns + column] = new MapRenderer.OverlayCell('*', ConsoleColor.Red);
                }
            }

            overlay[fromRow * terrain.Columns + fromColumn] = new MapRenderer.OverlayCell('A', ConsoleColor.White);
            overlay[toRow * terrain.Columns + toColumn] = new MapRenderer.OverlayCell('B', ConsoleColor.White);

            return overlay;
        }

        private static void PrintSummary(PathResult path, MovementType movementType, ITerrainCatalogue catalogue, GridTerrainMap terrain)
        {
            float baseSpeed = BaseSpeeds[movementType];
            float seconds = path.SecondsAt(baseSpeed);

            Console.WriteLine();
            Console.WriteLine($"  Route      {path.Distance,7:0} m walked");
            Console.WriteLine($"  Effort     {path.EffectiveDistance,7:0} m of equivalent open ground");
            Console.WriteLine($"  Time       {seconds,7:0} s  =  {seconds / SecondsPerTurn:0.0} turns at {baseSpeed:0.00} m/s");
            Console.WriteLine();
            Console.WriteLine($"  Search     {path.CellsExplored} cells explored, {path.SearchCells.Count} on the raw route");
            Console.WriteLine($"  Smoothed   {path.Waypoints.Count} waypoints " +
                              $"({(path.SearchCells.Count > 0 ? 100f * (1f - path.Waypoints.Count / (float)path.SearchCells.Count) : 0f):0}% fewer)");

            // Which terrain the route actually spends its time in — the quickest
            // way to see whether it is behaving sensibly.
            var timeByTerrain = new Dictionary<string, float>();
            float step = 2f;

            for (int i = 1; i < path.Waypoints.Count; i++)
            {
                Vec2 a = path.Waypoints[i - 1];
                Vec2 b = path.Waypoints[i];
                int samples = Math.Max(1, (int)MathF.Ceiling(Vec2.Distance(a, b) / step));
                float sampleLength = Vec2.Distance(a, b) / samples;

                for (int s = 0; s < samples; s++)
                {
                    Vec2 point = Vec2.Lerp(a, b, (s + 0.5f) / samples);
                    if (!terrain.Bounds.Contains(point)) continue;

                    TerrainDef def = catalogue.Get(terrain.At(point));
                    timeByTerrain.TryGetValue(def.DisplayName, out float running);
                    timeByTerrain[def.DisplayName] = running + sampleLength;
                }
            }

            if (timeByTerrain.Count > 0)
            {
                Console.WriteLine();
                Console.WriteLine("  Ground covered");

                var names = new List<string>(timeByTerrain.Keys);
                names.Sort(StringComparer.Ordinal);

                foreach (string name in names)
                    Console.WriteLine($"    {name,-12} {timeByTerrain[name],6:0} m");
            }

            Console.WriteLine();
        }

        private static float? ReadClearance(string[] args)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] != "--clearance") continue;

                if (!float.TryParse(args[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out float metres) || metres < 0f)
                    throw new FormatException($"Clearance must be a number of metres, got '{args[i + 1]}'.");

                return metres;
            }

            return null;
        }

        private static MovementType ReadMovementType(string[] args)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] != "--type") continue;

                if (!Enum.TryParse(args[i + 1], ignoreCase: true, out MovementType parsed))
                    throw new FormatException($"Unknown movement type '{args[i + 1]}'. Use foot, horse or wheeled.");

                return parsed;
            }

            return MovementType.Foot;
        }

        private static (int Column, int Row) ReadCell(string text, GridTerrainMap terrain)
        {
            string[] parts = text.Split(',');
            if (parts.Length != 2)
                throw new FormatException($"Expected a cell as 'column,row', got '{text}'.");

            if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int column) ||
                !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int row))
                throw new FormatException($"Expected whole numbers in '{text}'.");

            if (column < 0 || column >= terrain.Columns || row < 0 || row >= terrain.Rows)
                throw new FormatException($"Cell ({column},{row}) is outside the {terrain.Columns}x{terrain.Rows} map.");

            return (column, row);
        }
    }
}
