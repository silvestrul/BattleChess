using System;
using System.Globalization;
using System.IO;
using BattleChess.Contracts;
using BattleChess.Rules;

namespace BattleChess.Cli
{
    /// <summary>
    /// Runs a melee between two units and reports it turn by turn.
    /// </summary>
    /// <remarks>
    /// A damage formula cannot be judged from the formula. What matters is how
    /// many men are left after five turns and whether that felt like the right
    /// answer, which means running it and reading the numbers.
    /// </remarks>
    public static class FightCommand
    {
        public static int Run(string[] args)
        {
            if (args.Length < 4)
            {
                Console.Error.WriteLine("Usage: fight <battle> <unitA> <unitB> [--turns N]");
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

            if (!TryUnit(battle, args[2], out UnitInstance a) || !TryUnit(battle, args[3], out UnitInstance b))
                return 1;

            int maxTurns = ReadInt(args, "--turns", 8);

            // Clear the field of everyone else unless asked not to. A test file
            // holding several pairs has them all within archer range of one
            // another, so a "duel" was really one unit against every shooter on
            // the map — which made cavalry look three times as fragile as it is.
            if (!Array.Exists(args, arg => arg == "--all"))
            {
                foreach (UnitInstance other in battle.AllUnits)
                {
                    if (other.Id != a.Id && other.Id != b.Id)
                        other.State = UnitState.Scattered;
                }
            }

            bool approach = Array.Exists(args, arg => arg == "--approach");

            if (approach)
            {
                // Leave them where they were deployed and make the first unit
                // walk in. What is being measured is the cost of crossing the
                // ground, which teleporting into contact would skip entirely.
                a.Stance = Stance.Aggressive;
                a.GiveOrder(UnitOrder.Attack(b.Id), a.Position);
            }
            else
            {
                // Set them nose to nose, so what is measured is the damage rule
                // rather than how long the march took. Map cells are 25 m and
                // melee contact is about 13, so no deployment written by hand
                // can start a fight.
                Vec2 axis = (b.Position - a.Position).Normalised();
                if (axis.IsNearZero) axis = Vec2.East;

                b.Position = a.Position + axis * (a.Footprint.HalfDepth + b.Footprint.HalfDepth + 4f);
                a.Facing = Facing.FromVector(axis);
                b.Facing = Facing.FromVector(-axis);
            }

            // Without this both sides simply stand where they are, so a broken
            // enemy walks away untouched. Pursuit is the difference between men
            // scattered and men taken, so it needs to be testable.
            if (Array.Exists(args, arg => arg == "--pursue"))
            {
                a.Stance = Stance.Aggressive;
                b.Stance = Stance.Aggressive;
                a.GiveOrder(UnitOrder.Attack(b.Id), a.Position);
                b.GiveOrder(UnitOrder.Attack(a.Id), b.Position);
            }

            var clock = new BattleClock()
                .Add(new VisionSystem())
                .Add(new OrderSystem(new DirectPathfinder(map.Terrain, movement, terrain)))
                .Add(new ContactSystem())
                .Add(new MovementSystem())
                .Add(new RangedCombatSystem())
                .Add(new CombatSystem())
                .Add(new MoraleSystem());

            int aFighting = CombatSystem.FightingMen(a, b);
            int bFighting = CombatSystem.FightingMen(b, a);

            Console.WriteLine();
            Console.WriteLine($"  {a.Def.DisplayName} ({a.Strength} men, {a.Footprint.Width:0} m) " +
                              $"against {b.Def.DisplayName} ({b.Strength} men, {b.Footprint.Width:0} m)");
            Console.WriteLine($"  Contact width {MathF.Min(a.Footprint.Width, b.Footprint.Width):0} m — " +
                              $"{aFighting} fighting against {bFighting}");
            Console.WriteLine();
            Console.WriteLine($"    turn   {Head(a),-26}{Head(b),-26}");
            Console.WriteLine("    ----------------------------------------------------------");

            int startA = a.Strength;
            int startB = b.Strength;

            IBattleLog log = Array.Exists(args, arg => arg == "--verbose")
                ? new VerboseLog()
                : NullBattleLog.Instance;

            for (int turn = 1; turn <= maxTurns; turn++)
            {
                for (int i = 0; i < BattleClock.TicksPerTurn; i++)
                    clock.Advance(battle, log);

                Console.WriteLine($"    {turn,4}   {Row(a, startA),-26}{Row(b, startB),-26}");

                if (!a.IsOnField || !b.IsOnField) break;
                if (a.State == UnitState.Routing && b.State == UnitState.Routing) break;
            }

            Console.WriteLine();
            Console.WriteLine($"  {a.Def.DisplayName}: {a.Strength}/{startA} ({(startA - a.Strength) * 100f / startA:0}% lost), " +
                              $"morale {a.Morale:0.00}, {a.State}");
            Console.WriteLine($"  {b.Def.DisplayName}: {b.Strength}/{startB} ({(startB - b.Strength) * 100f / startB:0}% lost), " +
                              $"morale {b.Morale:0.00}, {b.State}");
            Console.WriteLine();

            return 0;
        }

        /// <summary>Prints every decision the simulation makes, for diagnosing a fight.</summary>
        private sealed class VerboseLog : IBattleLog
        {
            public void Record(in BattleLogEntry entry)
            {
                if (entry.Level == LogLevel.Info) return;
                Console.WriteLine($"      {entry.Category,-9} {entry.Message}");
            }
        }

        private static string Head(UnitInstance unit) => $"{unit.Def.DisplayName} (men/morale)";

        /// <summary>Strength, morale and state together, since morale is the point now.</summary>
        private static string Row(UnitInstance unit, int started)
        {
            string state = unit.State switch
            {
                UnitState.Wavering => " wavering",
                UnitState.Routing => " ROUTING",
                UnitState.Scattered => " scattered",
                UnitState.Captured => " TAKEN",
                UnitState.Destroyed => " destroyed",
                _ => string.Empty
            };

            return $"{unit.Strength,4} {unit.Morale,5:0.00}{state}";
        }

        private static bool TryUnit(BattleState battle, string text, out UnitInstance unit)
        {
            unit = null!;

            if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index) ||
                index < 0 || index >= battle.AllUnits.Count)
            {
                Console.Error.WriteLine($"Unit id must be 0..{battle.AllUnits.Count - 1}.");
                return false;
            }

            unit = battle.Get(new UnitId(index));
            return true;
        }

        private static int ReadInt(string[] args, string flag, int fallback)
        {
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == flag && int.TryParse(args[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
                    return value;

            return fallback;
        }
    }
}
