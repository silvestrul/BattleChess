using System;
using System.Globalization;
using System.IO;
using BattleChess.Contracts;
using BattleChess.Rules;

namespace BattleChess.Cli
{
    /// <summary>
    /// Runs one unit under a given stance against a live enemy and reports what
    /// it decided to do.
    /// </summary>
    /// <remarks>
    /// Stances only mean anything when something unexpected happens, so they
    /// cannot be judged from a stat table — they have to be run. This puts the
    /// same unit in the same situation four times and shows four different
    /// outcomes.
    /// </remarks>
    public static class StanceCommand
    {
        public static int Run(string[] args)
        {
            if (args.Length < 4)
            {
                Console.Error.WriteLine("Usage: stance <battle> <unitId> <defend|advance|aggressive|evade> [--target N] [--to col,row] [--turns N]");
                return 1;
            }

            var terrain = TerrainCatalogueReader.Read(File.ReadAllText(ContentLocator.TerrainFile()));
            var unitDefs = UnitCatalogueReader.Read(File.ReadAllText(ContentLocator.UnitsFile()));
            var formations = FormationCatalogueReader.Read(File.ReadAllText(ContentLocator.FormationsFile()));

            BattleSetup setup = BattleSetup.Parse(File.ReadAllText(ContentLocator.BattleFile(args[1])));
            BattleMapDefinition map = AsciiMapReader.Read(
                File.ReadAllText(ContentLocator.MapFile(setup.MapName)), terrain);

            var movement = new TerrainMovementModel(terrain);
            BattleState battle = setup.Build(map, terrain, unitDefs, formations, movement);

            if (!int.TryParse(args[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int unitIndex) ||
                unitIndex < 0 || unitIndex >= battle.AllUnits.Count)
            {
                Console.Error.WriteLine($"Unit id must be 0..{battle.AllUnits.Count - 1}.");
                return 1;
            }

            if (!Enum.TryParse(args[3], ignoreCase: true, out Stance stance))
            {
                Console.Error.WriteLine($"Unknown stance '{args[3]}'. Use defend, advance, aggressive or evade.");
                return 1;
            }

            UnitInstance unit = battle.Get(new UnitId(unitIndex));
            unit.Stance = stance;

            int target = ReadInt(args, "--target", -1);
            int maxTurns = ReadInt(args, "--turns", 10);

            var pathfinder = new DirectPathfinder(map.Terrain, movement, terrain);

            // Orders decide, contact interrupts, movement carries out — in that
            // order, so a decision takes effect on the tick it is made.
            var clock = new BattleClock()
                .Add(new VisionSystem())
                .Add(new OrderSystem(pathfinder))
                .Add(new ContactSystem())
                .Add(new MovementSystem());

            Console.WriteLine();
            Console.WriteLine($"  {unit.Def.DisplayName} ({battle.GetArmy(unit.Owner).Name}) on {stance}");

            if (target >= 0 && target < battle.AllUnits.Count)
            {
                UnitInstance quarry = battle.Get(new UnitId(target));
                unit.GiveOrder(UnitOrder.Attack(quarry.Id), unit.Position);
                Console.WriteLine($"  Ordered to attack {quarry.Def.DisplayName} " +
                                  $"({battle.GetArmy(quarry.Owner).Name}) at {Vec2.Distance(unit.Position, quarry.Position):0} m");
            }
            else
            {
                string? to = ReadText(args, "--to");

                if (to != null)
                {
                    string[] cell = to.Split(',');
                    Vec2 destination = map.Terrain.CellCentre(
                        AttributeParsers.Int(cell[0].Trim()), AttributeParsers.Int(cell[1].Trim()));

                    unit.GiveOrder(UnitOrder.MoveTo(destination), unit.Position);
                    Console.WriteLine($"  Ordered to march {Vec2.Distance(unit.Position, destination):0} m to ({to})");

                    PathResult path = pathfinder.FindPath(unit.Position, destination, unit.Def.Movement);
                    if (path.Found) unit.Route = new MovementRoute(path.Waypoints, false);
                }
                else
                {
                    unit.GiveOrder(UnitOrder.Stand(), unit.Position);
                    Console.WriteLine("  Standing, reacting only to what comes near");
                }
            }

            Vec2 start = unit.Position;
            Console.WriteLine();

            var log = new ConsoleBattleLog();
            int limit = maxTurns * BattleClock.TicksPerTurn;

            while (clock.Tick < limit)
                clock.Advance(battle, log);

            Console.WriteLine();
            Console.WriteLine($"  After {maxTurns} turns: moved {Vec2.Distance(start, unit.Position):0} m, " +
                              $"organization {unit.Organization:0.00}, {(unit.IsMarching ? "still moving" : "stopped")}.");

            UnitInstance? nearest = null;
            float best = float.MaxValue;

            foreach (UnitInstance other in battle.UnitsOnField())
            {
                if (other.Owner == unit.Owner) continue;

                float distance = Vec2.Distance(unit.Position, other.Position);
                if (distance < best) { best = distance; nearest = other; }
            }

            if (nearest != null)
                Console.WriteLine($"  Nearest enemy: {nearest.Def.DisplayName} at {best:0} m.");

            Console.WriteLine();
            return 0;
        }

        private static int ReadInt(string[] args, string flag, int fallback)
        {
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == flag && int.TryParse(args[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
                    return value;

            return fallback;
        }

        private static string? ReadText(string[] args, string flag)
        {
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == flag) return args[i + 1];

            return null;
        }

        /// <summary>Prints the simulation's commentary, skipping repetition.</summary>
        private sealed class ConsoleBattleLog : IBattleLog
        {
            private string _last = string.Empty;

            public void Record(in BattleLogEntry entry)
            {
                if (entry.Level == LogLevel.Info) return;
                if (entry.Message == _last) return;

                _last = entry.Message;
                Console.WriteLine($"    {entry.Category,-8} {entry.Message}");
            }
        }
    }
}
