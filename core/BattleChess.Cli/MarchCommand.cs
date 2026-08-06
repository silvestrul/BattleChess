using System;
using System.Globalization;
using System.IO;
using BattleChess.Contracts;
using BattleChess.Rules;

namespace BattleChess.Cli
{
    /// <summary>
    /// Orders one unit somewhere and ticks the battle until it arrives,
    /// reporting progress.
    /// </summary>
    /// <remarks>
    /// Runs the identical clock and movement system Unity does, with nothing
    /// rendering. That is the point: the same path has to work headless for the
    /// auto-resolve mode, and a tick-by-tick trace is far easier to reason about
    /// than watching sprites.
    /// </remarks>
    public static class MarchCommand
    {
        public static int Run(string[] args)
        {
            if (args.Length < 4)
            {
                Console.Error.WriteLine("Usage: march <battle> <unitId> <col,row> [--wheel] [--turns N]");
                return 1;
            }

            var terrainCatalogue = TerrainCatalogueReader.Read(File.ReadAllText(ContentLocator.TerrainFile()));
            var unitCatalogue = UnitCatalogueReader.Read(File.ReadAllText(ContentLocator.UnitsFile()));
            var formations = FormationCatalogueReader.Read(File.ReadAllText(ContentLocator.FormationsFile()));

            BattleSetup setup = BattleSetup.Parse(File.ReadAllText(ContentLocator.BattleFile(args[1])));
            BattleMapDefinition map = AsciiMapReader.Read(
                File.ReadAllText(ContentLocator.MapFile(setup.MapName)), terrainCatalogue);

            var movement = new TerrainMovementModel(terrainCatalogue);
            BattleState battle = setup.Build(map, terrainCatalogue, unitCatalogue, formations, movement);

            if (!int.TryParse(args[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int unitIndex) ||
                unitIndex < 0 || unitIndex >= battle.AllUnits.Count)
            {
                Console.Error.WriteLine($"Unit id must be 0..{battle.AllUnits.Count - 1}.");
                return 1;
            }

            UnitInstance unit = battle.Get(new UnitId(unitIndex));

            string[] cell = args[3].Split(',');
            if (cell.Length != 2)
            {
                Console.Error.WriteLine("Destination must be 'column,row'.");
                return 1;
            }

            Vec2 destination = map.Terrain.CellCentre(
                AttributeParsers.Int(cell[0].Trim()),
                AttributeParsers.Int(cell[1].Trim()));

            bool wheelFirst = Array.Exists(args, a => a == "--wheel");
            int maxTurns = ReadTurns(args);

            // This command is about movement and breakthrough, so the unit needs
            // to be willing to force through. On the default Defend stance it
            // would halt on contact whatever its breakthrough — correct
            // behaviour, but it would make every charge test read the same.
            unit.Stance = ReadStance(args);

            var pathfinder = new DirectPathfinder(map.Terrain, movement, terrainCatalogue);
            PathResult path = pathfinder.FindPath(unit.Position, destination, unit.Def.Movement);

            Console.WriteLine();
            Console.WriteLine($"  {unit.Def.DisplayName} ({battle.GetArmy(unit.Owner).Name}), " +
                              $"{unit.FormationOrder.DisplayName}, {unit.Strength} men");
            Console.WriteLine($"  {unit.Def.Get(UnitAttributes.TurnRate):0}°/s turn rate, {unit.BaseSpeed:0.00} m/s open ground");

            if (!path.Found)
            {
                Console.WriteLine($"  No route: {path.FailureDetail}");
                return 0;
            }

            Facing bearing = Facing.Towards(unit.Position, path.Waypoints[1]);
            float offBy = Facing.AbsoluteDelta(unit.Facing, bearing) * 180f / MathF.PI;

            Console.WriteLine($"  Route {path.Distance:0} m, starting {offBy:0}° off the bearing" +
                              $"{(wheelFirst ? ", wheeling first" : ", turning under way")}");
            Console.WriteLine();
            Console.WriteLine("   tick    facing   off    speed      moved   remaining");
            Console.WriteLine("  --------------------------------------------------------");

            unit.Route = new MovementRoute(path.Waypoints, wheelFirst);

            var clock = new BattleClock()
                .Add(new ContactSystem())
                .Add(new MovementSystem());
            Vec2 previous = unit.Position;
            float travelled = 0f;

            int limit = maxTurns * BattleClock.TicksPerTurn;

            while (unit.IsMarching && clock.Tick < limit)
            {
                clock.Advance(battle);

                float moved = Vec2.Distance(previous, unit.Position);
                travelled += moved;
                previous = unit.Position;

                // Every tick early on, where the wheeling happens, then sparsely.
                bool interesting = clock.Tick <= 12 || clock.Tick % 15 == 0 || !unit.IsMarching;
                if (!interesting) continue;

                float stillOff = unit.IsMarching
                    ? Facing.AbsoluteDelta(unit.Facing, Facing.Towards(unit.Position, unit.Route!.Target)) * 180f / MathF.PI
                    : 0f;

                Console.WriteLine(
                    $"  {clock.Tick,5}   {unit.Facing.Degrees,7:0}° {stillOff,5:0}°   " +
                    $"{moved,5:0.00} m/s {travelled,9:0} m {(unit.IsMarching ? unit.Route!.RemainingDistance(unit.Position) : 0f),10:0} m");
            }

            Console.WriteLine();

            if (unit.IsMarching)
                Console.WriteLine($"  Still marching after {maxTurns} turns — stopped early.");
            else if (Vec2.Distance(unit.Position, destination) > 25f)
                Console.WriteLine($"  Stopped {Vec2.Distance(unit.Position, destination):0} m short at tick {clock.Tick} " +
                                  $"— halted by an enemy zone of control.");
            else
                Console.WriteLine($"  Arrived at tick {clock.Tick} " +
                                  $"({clock.Tick / (float)BattleClock.TicksPerTurn:0.0} turns), {travelled:0} m walked.");

            Console.WriteLine($"  Organization {unit.Organization:0.00}, morale {unit.Morale:0.00}.");

            Console.WriteLine();
            return 0;
        }

        private static Stance ReadStance(string[] args)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "--stance" && Enum.TryParse(args[i + 1], ignoreCase: true, out Stance stance))
                    return stance;
            }

            return Stance.Advance;
        }

        private static int ReadTurns(string[] args)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "--turns" &&
                    int.TryParse(args[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int turns) &&
                    turns > 0)
                    return turns;
            }

            return 30;
        }
    }
}
