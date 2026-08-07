using System;
using System.Collections.Generic;
using BattleChess.Contracts;
using BattleChess.Rules;

namespace BattleChess.Tests.Battle
{
    /// <summary>Which rules are running.</summary>
    public enum RuleSet
    {
        /// <summary>Orders, contact, movement, shooting, melee and morale — a real battle.</summary>
        Full,

        /// <summary>
        /// Melee and morale only. Nothing moves and nothing shoots, so a single
        /// rule can be measured without the rest of the battle shifting
        /// underneath it.
        /// </summary>
        MeleeOnly,
    }

    /// <summary>
    /// Paints terrain onto a field before the battle is built.
    /// </summary>
    /// <remarks>
    /// Vision is the first system whose whole point is what lies <i>between</i>
    /// two units, so it is the first that cannot be tested on a uniform field.
    /// A ridge has to actually be somewhere.
    /// </remarks>
    public sealed class TerrainCanvas
    {
        private readonly TerrainId[] _cells;

        internal TerrainCanvas(TerrainId[] cells, int columns, int rows, float cellSize)
        {
            _cells = cells;
            Columns = columns;
            Rows = rows;
            CellSize = cellSize;
        }

        public int Columns { get; }
        public int Rows { get; }
        public float CellSize { get; }

        /// <summary>Paints a rectangle of cells, inclusive of both corners.</summary>
        public TerrainCanvas Rect(int fromColumn, int fromRow, int toColumn, int toRow, string terrainKey)
        {
            TerrainId id = TestContent.Ground(terrainKey).Id;

            for (int row = Math.Max(0, fromRow); row <= Math.Min(Rows - 1, toRow); row++)
            for (int column = Math.Max(0, fromColumn); column <= Math.Min(Columns - 1, toColumn); column++)
                _cells[row * Columns + column] = id;

            return this;
        }

        /// <summary>Paints a north-south band across the whole field — a ridge, or a treeline.</summary>
        public TerrainCanvas Band(int fromColumn, int toColumn, string terrainKey) =>
            Rect(fromColumn, 0, toColumn, Rows - 1, terrainKey);

        /// <summary>The column a world x-coordinate falls in.</summary>
        public int ColumnAt(float x) => Math.Clamp((int)(x / CellSize), 0, Columns - 1);
    }

    /// <summary>
    /// A flat one-terrain battlefield with whatever units a test wants to put
    /// on it.
    /// </summary>
    /// <remarks>
    /// The primitive the combat tests are built from. Everything is fixed
    /// except what the test is measuring — one terrain over the whole map, a
    /// fixed seed, and a fixed system order — so any difference between two
    /// runs comes from the one thing that was changed between them.
    /// </remarks>
    public sealed class Battlefield
    {
        public const int Columns = 80;
        public const int Rows = 40;
        public const float CellSize = 25f;

        private readonly List<int> _armies = new List<int>();
        private readonly IPathfinder _pathfinder;

        public GridTerrainMap Map { get; }

        public BattleState State { get; }

        public BattleClock Clock { get; }

        public Battlefield(
            string ground = "plains",
            ulong seed = 1000,
            RuleSet rules = RuleSet.Full,
            Action<TerrainCanvas>? paint = null)
        {
            TerrainDef terrain = TestContent.Ground(ground);

            var cells = new TerrainId[Columns * Rows];
            for (int i = 0; i < cells.Length; i++) cells[i] = terrain.Id;

            paint?.Invoke(new TerrainCanvas(cells, Columns, Rows, CellSize));

            Map = new GridTerrainMap(Columns, Rows, CellSize, cells);

            var movement = new TerrainMovementModel(TestContent.Terrain);

            State = new BattleState(
                "test", Map, TestContent.Terrain, TestContent.Units, TestContent.Formations, movement, seed);

            _pathfinder = new DirectPathfinder(Map, movement, TestContent.Terrain);

            Clock = rules == RuleSet.Full
                ? new BattleClock()
                    .Add(new VisionSystem())
                    .Add(new OrderSystem(_pathfinder))
                    .Add(new ContactSystem())
                    .Add(new MovementSystem())
                    .Add(new RangedCombatSystem())
                    .Add(new CombatSystem())
                    .Add(new MoraleSystem())
                : new BattleClock()
                    .Add(new CombatSystem())
                    .Add(new MoraleSystem());
        }

        public Vec2 Centre => Map.Bounds.Centre;

        /// <summary>Raises a unit by content key, at its default strength unless told otherwise.</summary>
        public UnitInstance Add(
            int player, string unitKey, Vec2 at, Facing facing, int strength = 0, string formation = "line") =>
            Add(player, TestContent.Unit(unitKey), at, facing, strength, TestContent.Formation(formation));

        /// <summary>Raises a unit from a definition the test built itself.</summary>
        public UnitInstance Add(
            int player, UnitDef def, Vec2 at, Facing facing, int strength = 0, FormationDef? formation = null)
        {
            var owner = new PlayerId(player);

            if (!_armies.Contains(player))
            {
                State.AddArmy(owner, $"Army {player}");
                _armies.Add(player);
            }

            return State.AddUnit(owner, def, at, facing,
                strength > 0 ? strength : def.DefaultStrength,
                formation ?? TestContent.Formations.Default);
        }

        /// <summary>Sends a unit at an enemy and keeps it after them.</summary>
        public static void Press(UnitInstance unit, UnitInstance target)
        {
            unit.Stance = Stance.Aggressive;
            unit.GiveOrder(UnitOrder.Attack(target.Id), unit.Position);
        }

        /// <summary>
        /// Marches a unit to a place rather than at an enemy — which is the
        /// order a zone of control exists to stop.
        /// </summary>
        /// <remarks>
        /// The route has to be laid here rather than left to the order system,
        /// which paths only for attack orders. A march is something the player
        /// asks for and the pathfinder answers once; the systems then carry it
        /// out or refuse to.
        /// </remarks>
        public void March(UnitInstance unit, Vec2 destination, Stance stance = Stance.Advance)
        {
            unit.Stance = stance;
            unit.GiveOrder(UnitOrder.MoveTo(destination), unit.Position);

            PathResult path = _pathfinder.FindPath(unit.Position, destination, unit.Def.Movement);

            if (!path.Found || path.Waypoints.Count < 2)
                throw new InvalidOperationException(
                    $"No route for {unit.Def.DisplayName} to {destination}: {path.Failure} {path.FailureDetail}");

            unit.Route = new MovementRoute(path.Waypoints, wheelFirst: false);
        }

        /// <summary>Leaves a unit standing where it is: it will fight, but never follow.</summary>
        public static void Hold(UnitInstance unit)
        {
            unit.Stance = Stance.Defend;
            unit.GiveOrder(UnitOrder.Stand(), unit.Position);
        }

        /// <summary>
        /// Everything the rules said while this battle ran.
        /// </summary>
        /// <remarks>
        /// Some behaviour is far easier to assert on through what the rules
        /// <i>reported</i> than through the state they left behind. Whether a
        /// charge landed is the clearest case: the effect is a multiplier
        /// buried inside a casualty figure that a dozen other things also move,
        /// but the combat rule says "Charge lands" in as many words, so
        /// counting those is both exact and legible in a failure message.
        /// </remarks>
        public TranscriptLog Transcript { get; } = new TranscriptLog();

        /// <summary>How many times a line contains a given phrase.</summary>
        public int TimesSaid(string phrase) => Transcript.Count(phrase);

        public void RunTurns(int turns)
        {
            for (int i = 0; i < turns; i++)
                Clock.AdvanceTurn(State, Transcript);
        }

        /// <summary>Runs combat pulses only — ten ticks each.</summary>
        public void RunPulses(int pulses)
        {
            for (int i = 0; i < pulses * CombatSystem.PulseIntervalTicks; i++)
                Clock.Advance(State, Transcript);
        }

        /// <summary>
        /// Runs a turn at a time until something is true, or the cap runs out.
        /// Returns turns elapsed.
        /// </summary>
        /// <remarks>
        /// How the tests catch a moment rather than an ending — the strength a
        /// regiment still had when it broke, say, which is gone by the time the
        /// fight is over.
        /// </remarks>
        public int RunUntil(Func<bool> condition, int maxTurns)
        {
            for (int turn = 1; turn <= maxTurns; turn++)
            {
                Clock.AdvanceTurn(State, Transcript);

                if (condition())
                    return turn;
            }

            return maxTurns;
        }

        /// <summary>
        /// Runs until one of the named units is off the field, or the cap runs
        /// out. Returns turns elapsed.
        /// </summary>
        public int RunUntilDecided(int maxTurns, params UnitInstance[] watch)
        {
            for (int turn = 1; turn <= maxTurns; turn++)
            {
                Clock.AdvanceTurn(State, Transcript);

                foreach (UnitInstance unit in watch)
                {
                    if (!unit.IsOnField)
                        return turn;
                }
            }

            return maxTurns;
        }

        /// <summary>
        /// What a unit has lost, as a percentage of what it started with.
        /// </summary>
        /// <remarks>
        /// Percentages throughout, never headcounts. A test asserting that
        /// cavalry kills 240 archers would have to be rewritten the moment
        /// anybody changes how big a regiment is; one asserting it kills 60% of
        /// them says the same thing and survives.
        /// </remarks>
        public static float LostPercent(UnitInstance unit) =>
            unit.InitialStrength <= 0 ? 0f : 100f * unit.Casualties / unit.InitialStrength;

        /// <summary>
        /// Places a unit just close enough to another to count as in contact.
        /// </summary>
        public static Vec2 ContactPosition(UnitInstance from, Footprint other, Vec2 direction) =>
            from.Position + direction.Normalised() * (from.Footprint.HalfDepth + other.HalfDepth + 4f);
    }
}
