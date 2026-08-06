using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using BattleChess.Contracts;
using BattleChess.Rules;

namespace BattleChess.Cli
{
    /// <summary>
    /// Shows the battlefield as one army actually sees it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The gate for fog of war, and the only honest way to judge it. A vision
    /// rule reads perfectly reasonable as a formula and turns out to hand one
    /// side the whole map or nothing at all; the way to find that out is to
    /// print both pictures side by side and look at them.
    /// </para>
    /// <para>
    /// It also prints <i>why</i> — which regiment made the sighting, how far
    /// off, and for the ones it missed, whether they were out of range or
    /// behind something. "I cannot see them" is not a useful answer on its own.
    /// </para>
    /// </remarks>
    public static class SeeCommand
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
            int turns = ReadInt(args, "--turns", 0);

            var terrain = TerrainCatalogueReader.Read(File.ReadAllText(ContentLocator.TerrainFile()));
            var units = UnitCatalogueReader.Read(File.ReadAllText(ContentLocator.UnitsFile()));
            var formations = FormationCatalogueReader.Read(File.ReadAllText(ContentLocator.FormationsFile()));

            BattleSetup setup = BattleSetup.Parse(File.ReadAllText(ContentLocator.BattleFile(name)));
            BattleMapDefinition map = AsciiMapReader.Read(
                File.ReadAllText(ContentLocator.MapFile(setup.MapName)), terrain);

            var movement = new TerrainMovementModel(terrain);
            BattleState battle = setup.Build(map, terrain, units, formations, movement);

            var clock = new BattleClock()
                .Add(new VisionSystem())
                .Add(new OrderSystem(new DirectPathfinder(map.Terrain, movement, terrain)))
                .Add(new ContactSystem())
                .Add(new MovementSystem())
                .Add(new RangedCombatSystem())
                .Add(new CombatSystem())
                .Add(new MoraleSystem());

            for (int turn = 0; turn < turns; turn++)
                clock.AdvanceTurn(battle);

            battle.Vision.Recompute(battle);

            Console.WriteLine();
            Console.WriteLine($"  {battle.Name} — {map.Name}, turn {battle.TurnNumber}");

            foreach (Army army in battle.Armies)
                Report(battle, army, map, terrain, useColour);

            return 0;
        }

        private static void Report(
            BattleState battle, Army army, BattleMapDefinition map, ITerrainCatalogue terrain, bool useColour)
        {
            ConsoleColor original = Console.ForegroundColor;

            Console.WriteLine();

            if (useColour) Console.ForegroundColor = ColourFor(army.Player);
            Console.WriteLine($"  AS SEEN BY {army.Name.ToUpperInvariant()}");
            if (useColour) Console.ForegroundColor = original;

            Console.WriteLine();

            MapRenderer.Draw(map, terrain, useColour, BuildOverlay(battle, army.Player, map.Terrain));

            Console.WriteLine();
            Console.WriteLine($"     {"Own regiments",-16}{"sees",8}{"standing on",16}");
            Console.WriteLine("     " + new string('-', 56));

            foreach (UnitInstance unit in battle.UnitsOf(army.Player))
            {
                if (!unit.IsOnField) continue;

                Console.WriteLine(
                    $"     {unit.Def.DisplayName,-16}{LineOfSight.SightRange(battle, unit),6:0} m" +
                    $"{battle.TerrainAt(unit.Position).DisplayName,16}");
            }

            Console.WriteLine();
            Console.WriteLine($"     {"Enemy",-16}{"distance",10}{"spotted by",18}");
            Console.WriteLine("     " + new string('-', 56));

            int seen = 0;
            int hidden = 0;

            foreach (UnitInstance enemy in battle.UnitsOnField())
            {
                if (enemy.Owner == army.Player) continue;

                UnitId spotter = VisionState.SpottedBy(battle, army.Player, enemy);
                UnitInstance nearest = Nearest(battle, army.Player, enemy);
                float distance = Vec2.Distance(nearest.Position, enemy.Position);

                if (spotter.IsValid)
                {
                    seen++;

                    Console.WriteLine(
                        $"     {enemy.Def.DisplayName,-16}{distance,8:0} m  {battle.Get(spotter).Def.DisplayName,-18}");
                }
                else
                {
                    hidden++;

                    Console.WriteLine(
                        $"     {enemy.Def.DisplayName,-16}{distance,8:0} m  {"— " + WhyNot(battle, nearest, enemy),-18}");
                }
            }

            Console.WriteLine();
            Console.WriteLine($"     {seen} regiment(s) in sight, {hidden} unaccounted for.");
        }

        /// <summary>
        /// Says which of the two rules hid a unit, in the words the design uses.
        /// </summary>
        private static string WhyNot(BattleState battle, UnitInstance observer, UnitInstance enemy)
        {
            float sight = LineOfSight.SightRange(battle, observer);
            float distance = Vec2.Distance(observer.Position, enemy.Position);
            float detection = LineOfSight.DetectionRange(battle, enemy, sight);

            if (distance > detection)
            {
                TerrainDef ground = battle.TerrainAt(enemy.Position);

                return ground.Get(TerrainAttributes.Conceals)
                    ? $"hidden in {ground.DisplayName.ToLowerInvariant()}"
                    : "too far off";
            }

            return "behind cover";
        }

        /// <summary>
        /// Draws the map with the army's own regiments and only the enemies it
        /// can actually see.
        /// </summary>
        private static MapRenderer.OverlayCell[] BuildOverlay(BattleState battle, PlayerId viewer, GridTerrainMap terrain)
        {
            var overlay = new MapRenderer.OverlayCell[terrain.Columns * terrain.Rows];

            foreach (UnitInstance unit in battle.UnitsOnField())
            {
                if (!battle.Vision.CanSee(battle, viewer, unit)) continue;

                ConsoleColor colour = ColourFor(unit.Owner);

                foreach ((int column, int row) in CoveredCells(unit, terrain))
                    overlay[row * terrain.Columns + column] = new MapRenderer.OverlayCell(unit.Def.Glyph, colour);
            }

            return overlay;
        }

        private static UnitInstance Nearest(BattleState battle, PlayerId viewer, UnitInstance target)
        {
            UnitInstance? best = null;
            float bestSquared = float.MaxValue;

            foreach (UnitInstance unit in battle.UnitsOf(viewer))
            {
                if (!unit.IsOnField) continue;

                float squared = Vec2.DistanceSquared(unit.Position, target.Position);

                if (squared < bestSquared)
                {
                    bestSquared = squared;
                    best = unit;
                }
            }

            return best ?? target;
        }

        private static IEnumerable<(int Column, int Row)> CoveredCells(UnitInstance unit, GridTerrainMap terrain)
        {
            OrientedRect shape = unit.Shape;
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

        private static int ReadInt(string[] args, string flag, int fallback)
        {
            int at = Array.IndexOf(args, flag);

            return at >= 0 && at + 1 < args.Length &&
                   int.TryParse(args[at + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
                ? value
                : fallback;
        }
    }
}
