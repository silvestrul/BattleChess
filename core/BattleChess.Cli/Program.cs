using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using BattleChess.Contracts;
using BattleChess.Rules;

namespace BattleChess.Cli
{
    /// <summary>
    /// Headless harness. This is deliberately the first place the game becomes
    /// playable — an ASCII board and typed orders let the mechanics be felt and
    /// verified long before Unity is involved.
    /// </summary>
    public static class Program
    {
        public static int Main(string[] args)
        {
            // Content files are authored with '.' as the decimal point, and the
            // parsers read them as invariant. Printing numbers in the machine's
            // locale would show '1,00' for a value written as '1.00', which is a
            // needless trap when comparing output against a map file.
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

            string command = args.Length > 0 ? args[0] : "help";

            try
            {
                switch (command)
                {
                    case "map":
                        return ShowMap(args);

                    case "terrain":
                        return ShowTerrain();

                    case "path":
                        return PathCommand.Run(args);

                    case "units":
                        return UnitsCommand.Run(args);

                    case "battle":
                        return BattleCommand.Run(args);

                    case "see":
                        return SeeCommand.Run(args);

                    case "formations":
                        return FormationsCommand.Run(args);

                    case "march":
                        return MarchCommand.Run(args);

                    case "balance":
                        return BalanceCommand.Run(args);

                    case "stance":
                        return StanceCommand.Run(args);

                    case "fight":
                        return FightCommand.Run(args);

                    case "sweep":
                        return SweepCommand.Run(args);

                    case "help":
                        PrintHelp();
                        return 0;

                    default:
                        Console.Error.WriteLine($"Unknown command '{command}'.");
                        PrintHelp();
                        return 1;
                }
            }
            catch (Exception error) when (error is FormatException or FileNotFoundException or DirectoryNotFoundException)
            {
                // Content errors are the user's problem to fix, not a crash, so
                // report them plainly rather than with a stack trace.
                Console.Error.WriteLine($"Error: {error.Message}");
                return 1;
            }
        }

        private static int ShowMap(string[] args)
        {
            string name = args.Length > 1 ? args[1] : "valley";
            bool useColour = !Array.Exists(args, a => a == "--no-colour" || a == "--no-color");

            TerrainCatalogue catalogue = LoadCatalogue();
            string mapPath = ContentLocator.MapFile(name);
            BattleMapDefinition map = AsciiMapReader.Read(File.ReadAllText(mapPath), catalogue);

            GridTerrainMap terrain = map.Terrain;

            Console.WriteLine();
            Console.WriteLine($"  {map.Name}");
            Console.WriteLine($"  {terrain.Columns} x {terrain.Rows} cells at {terrain.CellSize:0.#} m " +
                              $"= {terrain.Bounds.Width:0} x {terrain.Bounds.Height:0} m");
            Console.WriteLine();

            MapRenderer.Draw(map, catalogue, useColour);
            MapRenderer.DrawLegend(catalogue, useColour);

            PrintComposition(map, catalogue);
            PrintCrossingTimes(map, catalogue);

            return 0;
        }

        private static int ShowTerrain()
        {
            TerrainCatalogue catalogue = LoadCatalogue();

            Console.WriteLine();
            Console.WriteLine($"  {catalogue.Count} terrain types loaded from {ContentLocator.TerrainFile()}");

            MapRenderer.DrawLegend(catalogue, useColour: true);
            Console.WriteLine();

            return 0;
        }

        private static TerrainCatalogue LoadCatalogue() =>
            TerrainCatalogueReader.Read(File.ReadAllText(ContentLocator.TerrainFile()));

        private static void PrintComposition(BattleMapDefinition map, ITerrainCatalogue catalogue)
        {
            GridTerrainMap terrain = map.Terrain;
            var counts = new int[catalogue.Count];

            for (int row = 0; row < terrain.Rows; row++)
            for (int column = 0; column < terrain.Columns; column++)
                counts[terrain.AtCell(column, row).Index]++;

            int total = terrain.Columns * terrain.Rows;

            Console.WriteLine();
            Console.WriteLine("  Composition");

            for (int i = 0; i < counts.Length; i++)
            {
                if (counts[i] == 0) continue;

                TerrainDef def = catalogue.Get(new TerrainId(i));
                float share = counts[i] / (float)total;
                var bar = new string('#', (int)MathF.Round(share * 40f));

                Console.WriteLine($"    {def.DisplayName,-12} {share,6:0.0%}  {bar}");
            }
        }

        /// <summary>
        /// Sanity check on the speed figures: how long each movement type takes
        /// to cross the map on its best and worst going.
        /// </summary>
        private static void PrintCrossingTimes(BattleMapDefinition map, ITerrainCatalogue catalogue)
        {
            // Calibrated in the plan: cavalry crosses 1 km of open ground in
            // about 3.5 turns of 60 seconds, infantry at a third of that pace.
            const float SecondsPerTurn = 60f;

            var baseSpeeds = new Dictionary<MovementType, float>
            {
                [MovementType.Foot] = 1.59f,
                [MovementType.Horse] = 4.76f,
                [MovementType.Wheeled] = 1.30f
            };

            var movementModel = new TerrainMovementModel(catalogue);
            float distance = map.Terrain.Bounds.Width;

            Console.WriteLine();
            Console.WriteLine($"  Crossing {distance:0} m east-west, in turns of {SecondsPerTurn:0}s");

            foreach (KeyValuePair<MovementType, float> entry in baseSpeeds)
            {
                float best = 0f;
                float worst = float.MaxValue;

                foreach (TerrainDef def in catalogue.All)
                {
                    float multiplier = movementModel.SpeedMultiplier(def.Id, entry.Key);
                    if (multiplier <= 0f) continue;

                    best = MathF.Max(best, multiplier);
                    worst = MathF.Min(worst, multiplier);
                }

                float fastest = distance / (entry.Value * best) / SecondsPerTurn;
                float slowest = distance / (entry.Value * worst) / SecondsPerTurn;

                Console.WriteLine($"    {entry.Key,-8} {entry.Value,5:0.00} m/s   " +
                                  $"{fastest,5:0.0} turns on its best going, {slowest,5:0.0} on its worst");
            }

            Console.WriteLine();
        }

        private static void PrintHelp()
        {
            Console.WriteLine("Battle Chess headless harness");
            Console.WriteLine();
            Console.WriteLine("  help                Show this text");
            Console.WriteLine("  terrain             List terrain types and their effects");
            Console.WriteLine("  units [key]         List the roster, or one unit in detail");
            Console.WriteLine("      --strength N    Show that unit at a specific strength");
            Console.WriteLine("  formations          Formation orders and the shapes they produce");
            Console.WriteLine("  map [name]          Render a map (default: valley)");
            Console.WriteLine("      --no-colour     Plain text output");
            Console.WriteLine();
            Console.WriteLine("  path <map> <col,row> <col,row> [--type foot|horse|wheeled]");
            Console.WriteLine("                      Plan and draw a route (direct, as a player would order)");
            Console.WriteLine("      --ai            Route like the AI: fastest, not shortest");
            Console.WriteLine("      --clearance N   Require N metres of room to either side");
            Console.WriteLine();
            Console.WriteLine("  battle [name]       Draw up a battle (default: ford)");
            Console.WriteLine();
            Console.WriteLine("  see [battle]        Draw the field as each army actually sees it");
            Console.WriteLine("      --turns N       Fight N turns first, then look");
            Console.WriteLine();
            Console.WriteLine("Planned:");
            Console.WriteLine("  play <map>               Hotseat match         (M5)");
            Console.WriteLine("  replay <path>            Play back a match     (M5)");
            Console.WriteLine("  soak <n>                 Headless AI matches   (M6)");
        }
    }
}
