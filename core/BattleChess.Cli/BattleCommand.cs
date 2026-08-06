using System;
using System.Collections.Generic;
using System.IO;
using BattleChess.Contracts;
using BattleChess.Rules;

namespace BattleChess.Cli
{
    /// <summary>
    /// Loads a battle setup and shows the armies drawn up on the field.
    /// </summary>
    public static class BattleCommand
    {
        private static readonly ConsoleColor[] ArmyColours =
        {
            ConsoleColor.Cyan,
            ConsoleColor.Red,
            ConsoleColor.Magenta,
            ConsoleColor.Yellow
        };

        public static int Run(string[] args)
        {
            string name = args.Length > 1 ? args[1] : "ford";
            bool useColour = !Array.Exists(args, a => a == "--no-colour" || a == "--no-color");

            var terrainCatalogue = TerrainCatalogueReader.Read(File.ReadAllText(ContentLocator.TerrainFile()));
            var unitCatalogue = UnitCatalogueReader.Read(File.ReadAllText(ContentLocator.UnitsFile()));
            var formationCatalogue = FormationCatalogueReader.Read(File.ReadAllText(ContentLocator.FormationsFile()));

            BattleSetup setup = BattleSetup.Parse(File.ReadAllText(ContentLocator.BattleFile(name)));

            BattleMapDefinition map = AsciiMapReader.Read(
                File.ReadAllText(ContentLocator.MapFile(setup.MapName)), terrainCatalogue);

            var movement = new TerrainMovementModel(terrainCatalogue);
            BattleState battle = setup.Build(map, terrainCatalogue, unitCatalogue, formationCatalogue, movement);

            Console.WriteLine();
            Console.WriteLine($"  {battle.Name}");
            Console.WriteLine($"  {map.Name} — seed {battle.Seed}");
            Console.WriteLine();

            MapRenderer.Draw(map, terrainCatalogue, useColour, BuildOverlay(battle, map.Terrain));

            PrintOrdersOfBattle(battle, useColour);
            PrintDeploymentNotes(battle, map.Terrain);

            return 0;
        }

        /// <summary>
        /// Draws each unit's actual footprint rather than a single marker, by
        /// testing every map cell against the unit's oriented rectangle.
        /// </summary>
        /// <remarks>
        /// Worth the extra work: a regiment is 80-110 m of frontage against 25 m
        /// cells, so a single dot would badly misrepresent how much ground an
        /// army covers — and once casualties start shrinking frontage, seeing
        /// the line thin out is the clearest signal there is.
        /// </remarks>
        private static MapRenderer.OverlayCell[] BuildOverlay(BattleState battle, GridTerrainMap terrain)
        {
            var overlay = new MapRenderer.OverlayCell[terrain.Columns * terrain.Rows];

            foreach (UnitInstance unit in battle.UnitsOnField())
            {
                ConsoleColor colour = ColourFor(unit.Owner);

                foreach ((int column, int row) in CoveredCells(unit, terrain))
                    overlay[row * terrain.Columns + column] = new MapRenderer.OverlayCell(unit.Def.Glyph, colour);
            }

            return overlay;
        }

        /// <summary>
        /// Every map cell a unit's footprint actually covers.
        /// </summary>
        /// <remarks>
        /// Shared by drawing and by validation, so what you see on the map is
        /// exactly what the checks reasoned about.
        /// </remarks>
        private static IEnumerable<(int Column, int Row)> CoveredCells(UnitInstance unit, GridTerrainMap terrain)
        {
            OrientedRect shape = unit.Shape;

            // Only sweep the cells the unit could possibly reach.
            float reach = unit.Footprint.BoundingRadius;

            int minColumn = CellColumn(terrain, unit.Position.X - reach);
            int maxColumn = CellColumn(terrain, unit.Position.X + reach);
            int minRow = CellRow(terrain, unit.Position.Y + reach);
            int maxRow = CellRow(terrain, unit.Position.Y - reach);

            for (int row = minRow; row <= maxRow; row++)
            for (int column = minColumn; column <= maxColumn; column++)
            {
                if (shape.ContainsPoint(terrain.CellCentre(column, row)))
                    yield return (column, row);
            }
        }

        private static int CellColumn(GridTerrainMap terrain, float x) =>
            Math.Clamp((int)MathF.Floor((x - terrain.Bounds.Min.X) / terrain.CellSize), 0, terrain.Columns - 1);

        private static int CellRow(GridTerrainMap terrain, float y) =>
            Math.Clamp((int)MathF.Floor((terrain.Bounds.Max.Y - y) / terrain.CellSize), 0, terrain.Rows - 1);

        private static ConsoleColor ColourFor(PlayerId player) =>
            ArmyColours[Math.Abs(player.Value) % ArmyColours.Length];

        private static void PrintOrdersOfBattle(BattleState battle, bool useColour)
        {
            ConsoleColor original = Console.ForegroundColor;

            foreach (Army army in battle.Armies)
            {
                if (useColour)
                    Console.ForegroundColor = ColourFor(army.Player);

                Console.WriteLine();
                Console.WriteLine($"  {army.Name}");

                if (useColour)
                    Console.ForegroundColor = original;

                Console.WriteLine("     Unit           Men   Formation   Frontage   Morale    Org   Standing on");
                Console.WriteLine("     ----------------------------------------------------------------------------");

                int total = 0;
                float cost = 0f;

                foreach (UnitInstance unit in battle.UnitsOf(army.Player))
                {
                    total += unit.Strength;
                    cost += unit.TotalOf(UnitAttributes.CostPerMan);

                    TerrainDef ground = battle.TerrainAt(unit.Position);

                    Console.WriteLine(
                        $"  {unit.Def.Glyph}  {unit.Def.DisplayName,-13} {unit.Strength,5}   " +
                        $"{unit.FormationOrder.DisplayName,-9}   {unit.Footprint.Width,6:0} m   " +
                        $"{unit.Morale,6:0.00} {unit.Organization,6:0.00}   {ground.DisplayName}");
                }

                Console.WriteLine($"     {"",-13}    {total,5} men, cost {cost:0}");
            }

            if (useColour)
                Console.ForegroundColor = original;
        }

        /// <summary>
        /// Points out anything about the deployment worth knowing before a shot
        /// is fired — chiefly units standing somewhere that will hurt them.
        /// </summary>
        private static void PrintDeploymentNotes(BattleState battle, GridTerrainMap terrain)
        {
            var notes = new List<string>();

            foreach (UnitInstance unit in battle.UnitsOnField())
            {
                string who = $"{unit.Def.DisplayName} ({battle.GetArmy(unit.Owner).Name})";

                TerrainDef ground = battle.TerrainAt(unit.Position);
                float multiplier = battle.Movement.SpeedMultiplier(battle.Terrain.At(unit.Position), unit.Def.Movement);

                if (multiplier <= 0f)
                {
                    notes.Add($"{who} is stranded on {ground.DisplayName} it cannot cross.");
                    continue;
                }

                if (multiplier < 0.5f)
                    notes.Add($"{who} starts in {ground.DisplayName}, at {multiplier:0%} speed.");

                float defence = ground.Get(TerrainAttributes.DefenceBonus);
                if (defence < 0f)
                    notes.Add($"{who} is exposed in {ground.DisplayName} ({defence:+0%;-0%} defence).");

                // A regiment is 80-110 m wide against 25 m cells, so its flanks
                // routinely stand on ground its centre never touches. Checking
                // only the centre would call a line half-buried in a mountain
                // perfectly well deployed.
                var blocked = new SortedDictionary<string, int>(StringComparer.Ordinal);

                foreach ((int column, int row) in CoveredCells(unit, terrain))
                {
                    TerrainId id = terrain.AtCell(column, row);
                    if (battle.Movement.IsPassable(id, unit.Def.Movement)) continue;

                    string name = battle.TerrainCatalogue.Get(id).DisplayName;
                    blocked.TryGetValue(name, out int running);
                    blocked[name] = running + 1;
                }

                foreach (KeyValuePair<string, int> entry in blocked)
                    notes.Add($"{who} has part of its line in impassable {entry.Value * (int)terrain.CellSize} m of {entry.Key}.");
            }

            Console.WriteLine();

            if (notes.Count == 0)
            {
                Console.WriteLine("  Both armies are drawn up on good ground.");
            }
            else
            {
                Console.WriteLine("  Deployment notes");
                foreach (string note in notes)
                    Console.WriteLine($"    - {note}");
            }

            Console.WriteLine();
        }
    }
}
