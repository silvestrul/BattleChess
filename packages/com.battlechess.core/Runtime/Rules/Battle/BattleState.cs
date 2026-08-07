using System;
using System.Collections.Generic;
using BattleChess.Contracts;

namespace BattleChess.Rules
{
    /// <summary>One side in a battle.</summary>
    public sealed class Army
    {
        public PlayerId Player { get; }
        public string Name { get; }

        /// <summary>
        /// The way broken units run — toward this army's own rear.
        /// </summary>
        /// <remarks>
        /// Routers streaming to the rear is what makes pursuit a decision rather
        /// than a scramble: they run somewhere predictable, so cavalry can be
        /// sent to cut them off. Inferred from where the army deployed unless
        /// the battle file says otherwise.
        /// </remarks>
        public Vec2 RetreatDirection { get; set; } = new Vec2(-1f, 0f);

        public Army(PlayerId player, string name)
        {
            Player = player;
            Name = string.IsNullOrWhiteSpace(name) ? player.ToString() : name;
        }

        public override string ToString() => $"{Name} ({Player})";
    }

    /// <summary>
    /// The complete, authoritative state of a battle in progress.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Never leaves the authority. Clients receive a fogged projection instead,
    /// and the assembly split means a client cannot even reference this type.
    /// </para>
    /// <para>
    /// Units are stored in a flat list indexed by id and always iterated in
    /// ascending id order. That is not incidental tidiness — it is what makes a
    /// battle reproducible. Iterating a dictionary would let hash ordering leak
    /// into the simulation and quietly produce different outcomes from the same
    /// seed, which would break both replay testing and the promise that a
    /// published seed can be independently verified.
    /// </para>
    /// </remarks>
    public sealed class BattleState
    {
        private readonly List<UnitInstance> _units = new List<UnitInstance>();
        private readonly List<Army> _armies = new List<Army>();

        public string Name { get; }

        public ITerrainMap Terrain { get; }

        public ITerrainCatalogue TerrainCatalogue { get; }

        public IUnitCatalogue UnitCatalogue { get; }

        public IFormationCatalogue FormationCatalogue { get; }

        public IMovementModel Movement { get; }

        /// <summary>
        /// The battle's random source. The seed it was created from is what
        /// makes an outcome independently verifiable.
        /// </summary>
        public DeterministicRng Rng { get; }

        public ulong Seed { get; }

        /// <summary>
        /// What each army can currently see of the other.
        /// </summary>
        /// <remarks>
        /// Lives on the authoritative state and never leaves it. A client is
        /// given the <i>result</i> of applying this — a view containing only the
        /// regiments it is allowed to know about — and the assembly split means
        /// it could not hold the rest even if the projection were wrong.
        /// </remarks>
        public VisionState Vision { get; } = new VisionState();

        /// <summary>Turns completed so far.</summary>
        public int TurnNumber { get; set; }

        public BattleState(
            string name,
            ITerrainMap terrain,
            ITerrainCatalogue terrainCatalogue,
            IUnitCatalogue unitCatalogue,
            IFormationCatalogue formationCatalogue,
            IMovementModel movement,
            ulong seed)
        {
            Name = string.IsNullOrWhiteSpace(name) ? "Unnamed battle" : name;
            Terrain = terrain ?? throw new ArgumentNullException(nameof(terrain));
            TerrainCatalogue = terrainCatalogue ?? throw new ArgumentNullException(nameof(terrainCatalogue));
            UnitCatalogue = unitCatalogue ?? throw new ArgumentNullException(nameof(unitCatalogue));
            FormationCatalogue = formationCatalogue ?? throw new ArgumentNullException(nameof(formationCatalogue));
            Movement = movement ?? throw new ArgumentNullException(nameof(movement));
            Seed = seed;
            Rng = new DeterministicRng(seed);
        }

        /// <summary>Every unit ever raised, in id order, including the dead.</summary>
        public IReadOnlyList<UnitInstance> AllUnits => _units;

        public IReadOnlyList<Army> Armies => _armies;

        public UnitInstance Get(UnitId id)
        {
            if (!id.IsValid || id.Value >= _units.Count)
                throw new ArgumentOutOfRangeException(nameof(id), id, "No such unit in this battle.");

            return _units[id.Value];
        }

        public Army GetArmy(PlayerId player)
        {
            foreach (Army army in _armies)
            {
                if (army.Player == player)
                    return army;
            }

            throw new ArgumentOutOfRangeException(nameof(player), player, "No such army in this battle.");
        }

        public Army AddArmy(PlayerId player, string name)
        {
            foreach (Army existing in _armies)
            {
                if (existing.Player == player)
                    throw new ArgumentException($"Army {player} already exists.", nameof(player));
            }

            var army = new Army(player, name);
            _armies.Add(army);
            return army;
        }

        /// <summary>
        /// Raises a unit and hands back its handle. Ids are issued in creation
        /// order and never reused, which is what the simulation's ordering
        /// guarantee rests on.
        /// </summary>
        public UnitInstance AddUnit(PlayerId owner, UnitDef def, Vec2 position, Facing facing, int strength, FormationDef? formation = null)
        {
            var unit = new UnitInstance(
                new UnitId(_units.Count), owner, def, position, facing, strength,
                formation ?? FormationCatalogue.Default);

            // Rolled off the battle seed at muster, so a published seed still
            // replays exactly while no two regiments are quite alike.
            float spread = def.Get(UnitAttributes.QualitySpread);
            if (spread > 0f) unit.Quality = Rng.NextVariance(spread);

            int perMan = def.Get(UnitAttributes.Ammunition);
            unit.ShotsLeft = perMan > 0 ? perMan * strength : -1;

            _units.Add(unit);
            return unit;
        }

        /// <summary>Units belonging to a side, in id order.</summary>
        public IEnumerable<UnitInstance> UnitsOf(PlayerId player)
        {
            for (int i = 0; i < _units.Count; i++)
            {
                if (_units[i].Owner == player)
                    yield return _units[i];
            }
        }

        /// <summary>Units still physically on the battlefield, in id order.</summary>
        public IEnumerable<UnitInstance> UnitsOnField()
        {
            for (int i = 0; i < _units.Count; i++)
            {
                if (_units[i].IsOnField)
                    yield return _units[i];
            }
        }

        /// <summary>Units still able to fight and take orders, in id order.</summary>
        public IEnumerable<UnitInstance> FightingUnitsOf(PlayerId player)
        {
            for (int i = 0; i < _units.Count; i++)
            {
                if (_units[i].Owner == player && _units[i].IsFighting)
                    yield return _units[i];
            }
        }

        /// <summary>Men still standing under a given side.</summary>
        public int StrengthOf(PlayerId player)
        {
            int total = 0;

            for (int i = 0; i < _units.Count; i++)
            {
                if (_units[i].Owner == player && _units[i].IsOnField)
                    total += _units[i].Strength;
            }

            return total;
        }

        /// <summary>Terrain under a world position.</summary>
        public TerrainDef TerrainAt(Vec2 position) => TerrainCatalogue.Get(Terrain.At(position));

        /// <summary>
        /// How far apart the sample points are when reading terrain under a
        /// whole formation, in metres.
        /// </summary>
        /// <remarks>
        /// Half a map cell, so a band of bad ground cannot slip between two
        /// samples. The caps that follow matter more than the spacing: they
        /// bound the work per unit per tick regardless of how wide a regiment
        /// grows.
        /// </remarks>
        private const float TerrainSampleSpacing = 12.5f;

        private const int MaxSamplesAcross = 16;
        private const int MaxSamplesDeep = 8;

        /// <summary>
        /// The worst disorder inflicted by any ground the formation is standing
        /// on.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The worst rather than the average, and read across the whole
        /// footprint rather than at the centre. A regiment a hundred metres
        /// across with one flank in a river is a regiment in trouble, and
        /// sampling the middle said it was on dry grass — the same
        /// point-for-a-rectangle mistake that let formations march through one
        /// another.
        /// </para>
        /// <para>
        /// Taking the worst is what makes bad ground something to steer around
        /// rather than something to clip the corner of. Averaging would let a
        /// commander put a quarter of his line in a swamp and pay a quarter of
        /// the price, which is precisely the move the rule exists to
        /// discourage.
        /// </para>
        /// </remarks>
        public float WorstDisorderUnder(UnitInstance unit)
        {
            if (unit == null) throw new ArgumentNullException(nameof(unit));

            OrientedRect shape = unit.Shape;
            Footprint footprint = shape.Footprint;

            int across = SampleCount(footprint.Width, MaxSamplesAcross);
            int deep = SampleCount(footprint.Depth, MaxSamplesDeep);

            Vec2 right = shape.Right;
            Vec2 forward = shape.Forward;

            float worst = 0f;

            // Fixed iteration order, as everywhere else in the rules — this
            // feeds organization, and organization decides fights.
            for (int d = 0; d < deep; d++)
            {
                float alongDepth = Offset(d, deep, footprint.HalfDepth);

                for (int a = 0; a < across; a++)
                {
                    Vec2 point = shape.Centre
                               + right * Offset(a, across, footprint.HalfWidth)
                               + forward * alongDepth;

                    float disorder = TerrainAt(point).Get(TerrainAttributes.Disorder);

                    if (disorder > worst) worst = disorder;
                }
            }

            return worst;
        }

        private static int SampleCount(float extent, int cap) =>
            Math.Clamp((int)MathF.Ceiling(extent / TerrainSampleSpacing) + 1, 2, cap);

        /// <summary>
        /// Spreads <paramref name="count"/> samples evenly from one edge to the
        /// other, both edges included.
        /// </summary>
        private static float Offset(int index, int count, float halfExtent) =>
            count <= 1 ? 0f : -halfExtent + 2f * halfExtent * index / (count - 1);

        /// <summary>
        /// How fast a unit moves where it currently stands, in metres per
        /// second, after terrain. Zero means it is stuck.
        /// </summary>
        public float SpeedOf(UnitInstance unit)
        {
            float multiplier = Movement.SpeedMultiplier(Terrain.At(unit.Position), unit.Def.Movement);
            return unit.BaseSpeed * multiplier;
        }

        public override string ToString() => $"{Name} — turn {TurnNumber}, {_units.Count} units, seed {Seed}";
    }
}
