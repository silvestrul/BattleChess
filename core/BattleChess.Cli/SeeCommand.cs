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
                Report(battle, army, map, terrain, units, useColour);

            return 0;
        }

        /// <summary>
        /// Prints one army's picture, then — separately and clearly labelled —
        /// the referee's explanation of it.
        /// </summary>
        /// <remarks>
        /// The split is the whole point of this command. Everything above the
        /// line is rendered from a <see cref="PlayerView"/> and from nothing
        /// else, so if the projection is wrong the picture is visibly wrong; if
        /// the projection is too thin to draw a battlefield from, that shows up
        /// here first and cheaply, long before a Unity client is built on it.
        /// Everything below the line is the authority talking, and a player
        /// would never see it.
        /// </remarks>
        private static void Report(
            BattleState battle,
            Army army,
            BattleMapDefinition map,
            ITerrainCatalogue terrain,
            IUnitCatalogue units,
            bool useColour)
        {
            PlayerView view = PlayerViewProjector.Project(battle, army.Player);

            ConsoleColor original = Console.ForegroundColor;

            Console.WriteLine();

            if (useColour) Console.ForegroundColor = ColourFor(army.Player);
            Console.WriteLine($"  AS SEEN BY {army.Name.ToUpperInvariant()}");
            if (useColour) Console.ForegroundColor = original;

            Console.WriteLine();

            MapRenderer.Draw(map, terrain, useColour, BuildOverlay(view, units, map.Terrain));

            Console.WriteLine();
            Console.WriteLine($"     {"Own regiments",-16}{"men",8}{"morale",10}{"doing",20}");
            Console.WriteLine("     " + new string('-', 60));

            foreach (CommandedUnit unit in view.Own)
            {
                if (!unit.IsOnField) continue;

                Console.WriteLine(
                    $"     {unit.Name,-16}{unit.Strength,8}{unit.Morale,10:0.00}" +
                    $"{Describe(unit),20}");
            }

            Console.WriteLine();
            Console.WriteLine($"     {"In sight",-16}{"about",10}{"distance",12}{"note",14}");
            Console.WriteLine("     " + new string('-', 60));

            foreach (SightedUnit enemy in view.Sighted)
            {
                Console.WriteLine(
                    $"     {enemy.Name,-16}{enemy.EstimatedStrength,8} men{NearestOwn(view, enemy.Position),10:0} m" +
                    $"{(enemy.IsBroken ? "  running" : ""),14}");
            }

            if (view.Sighted.Count == 0)
                Console.WriteLine("     (nothing)");

            if (view.Remembered.Count > 0)
            {
                Console.WriteLine();
                Console.WriteLine($"     {"Last seen",-16}{"about",10}{"where",14}{"how long ago",16}");
                Console.WriteLine("     " + new string('-', 60));

                foreach (RememberedUnit ghost in view.Remembered)
                {
                    Console.WriteLine(
                        $"     {ghost.Name,-16}{ghost.EstimatedStrength,8} men" +
                        $"{$"{ghost.LastSeenAt.X:0}, {ghost.LastSeenAt.Y:0}",14}" +
                        $"{$"{ghost.AgeTicks / 60f:0.0} turns",16}");
                }
            }

            Console.WriteLine();
            Console.WriteLine($"     {view.Sighted.Count} regiment(s) in sight, " +
                              $"{view.Remembered.Count} last seen somewhere, " +
                              $"{Unaccounted(battle, army.Player, view)} never laid eyes on.");

            Explain(battle, army, view, useColour);
        }

        /// <summary>
        /// The referee's note: why each enemy is or is not in the picture above.
        /// </summary>
        /// <remarks>
        /// Reads the authoritative state directly, which is exactly why it is
        /// fenced off and labelled. "I cannot see them" is not a useful answer
        /// on its own, but the answer is not something the player is entitled
        /// to either — so it is printed as the umpire speaking, not as part of
        /// the view.
        /// </remarks>
        private static void Explain(BattleState battle, Army army, PlayerView view, bool useColour)
        {
            ConsoleColor original = Console.ForegroundColor;

            Console.WriteLine();
            if (useColour) Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("     referee's note — not part of the view above");
            Console.WriteLine("     " + new string('-', 60));

            foreach (UnitInstance enemy in battle.UnitsOnField())
            {
                if (enemy.Owner == army.Player) continue;

                UnitId spotter = VisionState.SpottedBy(battle, army.Player, enemy);
                UnitInstance nearest = Nearest(battle, army.Player, enemy);
                float distance = Vec2.Distance(nearest.Position, enemy.Position);

                string reason = spotter.IsValid
                    ? $"seen by {battle.Get(spotter).Def.DisplayName}"
                    : "— " + WhyNot(battle, nearest, enemy);

                Console.WriteLine($"     {enemy.Def.DisplayName,-16}{distance,8:0} m  {reason,-28}");
            }

            if (useColour) Console.ForegroundColor = original;
        }

        /// <summary>How many enemy regiments this army has never once spotted.</summary>
        private static int Unaccounted(BattleState battle, PlayerId viewer, PlayerView view)
        {
            int known = view.Sighted.Count + view.Remembered.Count;
            int total = 0;

            foreach (UnitInstance unit in battle.UnitsOnField())
                if (unit.Owner != viewer) total++;

            return Math.Max(0, total - known);
        }

        private static string Describe(CommandedUnit unit) => unit.State switch
        {
            UnitState.Routing => "running",
            _ => unit.Order.Kind switch
            {
                OrderKind.Attack => "attacking",
                OrderKind.Move => "marching",
                _ => unit.Stance.ToString().ToLowerInvariant()
            }
        };

        /// <summary>Distance from the closest of our own regiments — computed from the view alone.</summary>
        private static float NearestOwn(PlayerView view, Vec2 place)
        {
            float best = float.MaxValue;

            foreach (CommandedUnit unit in view.Own)
            {
                if (!unit.IsOnField) continue;

                best = MathF.Min(best, Vec2.Distance(unit.Position, place));
            }

            return best == float.MaxValue ? 0f : best;
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
        /// Draws the map from the view and from nothing else.
        /// </summary>
        /// <remarks>
        /// Stale sightings go down first and in dark grey, so a fresh sighting
        /// of the same regiment paints over its own ghost rather than leaving
        /// the player two markers for one enemy.
        /// </remarks>
        private static MapRenderer.OverlayCell[] BuildOverlay(
            PlayerView view, IUnitCatalogue units, GridTerrainMap terrain)
        {
            var overlay = new MapRenderer.OverlayCell[terrain.Columns * terrain.Rows];

            foreach (RememberedUnit ghost in view.Remembered)
            {
                Paint(overlay, terrain,
                    ghost.LastSeenAt, ghost.LastSeenFacing,
                    units.Get(ghost.Type).FootprintAt(ghost.EstimatedStrength),
                    char.ToLowerInvariant(units.Get(ghost.Type).Glyph),
                    ConsoleColor.DarkGray);
            }

            foreach (SightedUnit enemy in view.Sighted)
            {
                Paint(overlay, terrain,
                    enemy.Position, enemy.Facing, enemy.Footprint,
                    units.Get(enemy.Type).Glyph,
                    ColourFor(enemy.Owner));
            }

            foreach (CommandedUnit unit in view.Own)
            {
                if (!unit.IsOnField) continue;

                Paint(overlay, terrain,
                    unit.Position, unit.Facing, unit.Footprint,
                    units.Get(unit.Type).Glyph,
                    ColourFor(view.Viewer));
            }

            return overlay;
        }

        private static void Paint(
            MapRenderer.OverlayCell[] overlay,
            GridTerrainMap terrain,
            Vec2 position,
            Facing facing,
            Footprint footprint,
            char glyph,
            ConsoleColor colour)
        {
            foreach ((int column, int row) in CoveredCells(position, facing, footprint, terrain))
                overlay[row * terrain.Columns + column] = new MapRenderer.OverlayCell(glyph, colour);
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

        private static IEnumerable<(int Column, int Row)> CoveredCells(
            Vec2 position, Facing facing, Footprint footprint, GridTerrainMap terrain)
        {
            var shape = new OrientedRect(position, facing, footprint);
            float reach = footprint.BoundingRadius;

            int minColumn = CellColumn(terrain, position.X - reach);
            int maxColumn = CellColumn(terrain, position.X + reach);
            int minRow = CellRow(terrain, position.Y + reach);
            int maxRow = CellRow(terrain, position.Y - reach);

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
