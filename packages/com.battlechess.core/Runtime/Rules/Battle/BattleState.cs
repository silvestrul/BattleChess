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

            _whereEverybodyIs = new UnitIndex(Terrain.Bounds);
        }

        private readonly UnitIndex _whereEverybodyIs;

        /// <summary>
        /// Where everybody is standing, bucketed. Planning asks this tens of
        /// thousands of times a march and it is the difference between a
        /// clearance check costing a walk down the whole army and costing a
        /// lookup.
        /// </summary>
        internal UnitIndex WhereEverybodyIs => _whereEverybodyIs;

        /// <summary>
        /// Called by a regiment when it moves, so the index knows it is stale.
        /// </summary>
        internal void NoteUnitsMoved() => _whereEverybodyIs.Invalidate();

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

            unit.Home = this;
            _whereEverybodyIs.Invalidate();

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
        /// <summary>
        /// Routes planned since the battle began. Whoever reports it takes the
        /// difference across a tick.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>M38.</b> Every plan made inside the simulation was invisible: the
        /// recording's cost lines are written by the Unity controller when an
        /// order is given, and a regiment re-planning a chase writes nothing at
        /// all. One session logged 33 plans while freezing for 608 ms on a
        /// single frame, and the plans that caused it were not among the 33.
        /// </para>
        /// <para>
        /// Counted here rather than in the planner because the planner is static
        /// and a battle is not: two battles in one process — which is every test
        /// run — would otherwise share a counter.
        /// </para>
        /// </remarks>
        public int RoutesPlanned;

        /// <summary>
        /// Stopwatch ticks spent inside <see cref="Marching.PlanTo"/> in this
        /// battle, ever.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A count of plans was never going to answer "why did that frame
        /// stop": measured on the bench, the same leg count cost between six
        /// and nineteen microseconds depending on what else was running, and
        /// a plan is not a fixed price. The harness could already say what a
        /// frame spent in the simulation, but not how much of that was
        /// working out routes — so a frame that stopped while a wing was
        /// marching could not distinguish re-planning from the tick doing
        /// its ordinary work, which is the one split that decides what to
        /// fix.
        /// </para>
        /// <para>
        /// Raw stopwatch ticks rather than milliseconds so accumulating
        /// thousands of short plans does not round each of them to nothing.
        /// On the battle rather than static, for the same reason
        /// <see cref="RoutesPlanned"/> is: two battles in one process is
        /// every test run.
        /// </para>
        /// </remarks>
        public long RoutePlanningTicks;

        /// <summary>
        /// How much route planning one drawn frame may do, and who gets to do
        /// it.
        /// </summary>
        /// <remarks>
        /// Does nothing at all unless a host calls
        /// <see cref="PlanningBudget.OpenFrame"/>, so the CLI, the benches and
        /// the test suite are unaffected. On the battle rather than static for
        /// the same reason <see cref="RoutesPlanned"/> is.
        /// </remarks>
        public readonly PlanningBudget Planning = new PlanningBudget();

        private readonly System.Collections.Concurrent.ConcurrentDictionary<MovementType, PassableGround> _passable =
            new System.Collections.Concurrent.ConcurrentDictionary<MovementType, PassableGround>();

        /// <summary>
        /// Where <paramref name="moving"/> cannot go on this field, for asking
        /// about whole rectangles at once.
        /// </summary>
        /// <remarks>
        /// Built the first time it is wanted and kept: terrain does not move,
        /// so the answer is good for the life of the battle. Per battle rather
        /// than static because two battles in one process is every test run —
        /// the same reason <see cref="RoutesPlanned"/> lives here.
        /// </remarks>
        public PassableGround PassableFor(MovementType moving)
        {
            // Worked out on the first ask and kept, and the table it builds is
            // read by every clearance check on every leg - so when a wing is
            // ordered at once, the first ask comes from several threads
            // together. A plain dictionary read while another thread is
            // resizing it does not throw reliably; it returns nonsense, which
            // is the kind of fault that reappears as "a regiment walked through
            // a wood" a week later. Two threads may both build the table; only
            // one is kept, and they build the same thing.
            return _passable.GetOrAdd(moving, m => PassableGround.Build(Terrain, Movement, m));
        }

        /// <summary>
        /// The scratchpad a march in this battle plans on (<b>M40</b>) - one per
        /// thread that ever plans one.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Per battle, not static: two battles in one process share nothing.
        /// </para>
        /// <para>
        /// It was also one per battle, on the reasoning that a battle only ever
        /// plans one march at a time. That is true of the tick and false of the
        /// order: a player box-selects a wing and clicks, and eighty routes are
        /// wanted at once from eighty independent questions. Every other
        /// scratchpad in planning is already <c>[ThreadStatic]</c>; this one
        /// was the single thing making a plan un-shareable, and sharing it
        /// across threads did not merely slow the search down - it indexed a
        /// list built for one march with an offset belonging to another and
        /// threw.
        /// </para>
        /// </remarks>
        internal RouteSearch.Ledger PlanningScratch => _scratch.Value!;

        private readonly System.Threading.ThreadLocal<RouteSearch.Ledger> _scratch =
            new System.Threading.ThreadLocal<RouteSearch.Ledger>(() => new RouteSearch.Ledger());

        public OnField UnitsOnField() => new OnField(_units);

        /// <summary>
        /// The regiments still on the field, walked without littering.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>M40.</b> This was an iterator method, so every <c>foreach</c> over
        /// it allocated a compiler-generated object — and it is asked once per
        /// clearance test, thousands of times in a single plan. Measured: a plan
        /// allocated 205 kB against a whole simulation tick's 1,1, and a session
        /// churning megabytes through a stop-the-world collector stopped for
        /// forty to fifty milliseconds at a time.
        /// </para>
        /// <para>
        /// A struct enumerator costs nothing, and <c>foreach</c> takes it by
        /// shape rather than through the interface, so all sixty-four callers
        /// are unchanged. <see cref="IEnumerable{T}"/> is still implemented for
        /// anything that genuinely wants it — that path allocates as before, and
        /// nothing in the tree uses it.
        /// </para>
        /// </remarks>
        public readonly struct OnField : IEnumerable<UnitInstance>
        {
            private readonly List<UnitInstance> _units;

            public OnField(List<UnitInstance> units) => _units = units;

            public Enumerator GetEnumerator() => new Enumerator(_units);

            IEnumerator<UnitInstance> IEnumerable<UnitInstance>.GetEnumerator()
            {
                foreach (UnitInstance unit in this) yield return unit;
            }

            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() =>
                ((IEnumerable<UnitInstance>)this).GetEnumerator();

            public struct Enumerator
            {
                private readonly List<UnitInstance> _units;
                private int _at;

                public Enumerator(List<UnitInstance> units)
                {
                    _units = units;
                    _at = -1;
                }

                public UnitInstance Current => _units[_at];

                public bool MoveNext()
                {
                    while (++_at < _units.Count)
                    {
                        if (_units[_at].IsOnField) return true;
                    }

                    return false;
                }
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

        // ---- Bonds ------------------------------------------------------------

        /// <summary>
        /// The pace a whole wing keeps: the speed of whichever of its regiments
        /// is currently moving slowest, terrain and all.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Over the ground rather than on paper. A wing whose left is fording a
        /// river and whose right is on a road is not a wing for very long if
        /// each marches at what its own footing allows — it arrives as two
        /// halves at two different times, which is the thing binding them was
        /// meant to prevent. So the whole body waits for the ford.
        /// </para>
        /// <para>
        /// A regiment that cannot move at all is skipped rather than allowed to
        /// freeze everybody. Being stranded on ground you cannot cross is a
        /// separate problem with its own message, and letting it stop the wing
        /// would make it unrecoverable without unbinding.
        /// </para>
        /// </remarks>
        public float PaceOfBond(UnitInstance unit)
        {
            if (unit == null) throw new ArgumentNullException(nameof(unit));
            if (unit.Bond == 0) return SpeedOf(unit);

            float slowest = float.MaxValue;

            for (int i = 0; i < _units.Count; i++)
            {
                if (_units[i].Bond != unit.Bond) continue;
                if (!_units[i].IsFighting) continue;

                float speed = SpeedOf(_units[i]);
                if (speed <= 0f) continue;

                if (speed < slowest) slowest = speed;
            }

            return slowest < float.MaxValue ? slowest : SpeedOf(unit);
        }

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

        private const int MaxFitSamplesAcross = 9;
        private const int MaxFitSamplesDeep = 3;

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

                    // A regiment standing near the edge has part of its
                    // frontage hanging over the end of the world, and there is
                    // no ground out there to disorder anybody. Sampling it
                    // anyway asked the catalogue about a terrain id that does
                    // not exist, which threw — inside the movement system, on
                    // every tick, so the whole battle stopped advancing and the
                    // army appeared to be stuck against the border.
                    if (!Terrain.Bounds.Contains(point)) continue;

                    float disorder = TerrainAt(point).Get(TerrainAttributes.Disorder);

                    if (disorder > worst) worst = disorder;
                }
            }

            return worst;
        }

        /// <summary>
        /// Whether every part of a formation would be standing on ground it can
        /// cross.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Movement has always asked this of a single point at the regiment's
        /// centre, which for a body a hundred metres wide is barely a question
        /// at all — a line could sit with half its frontage inside a mountain
        /// and the rules saw a centre on open grass. Bodies of men occupy
        /// ground, and ground they cannot cross is ground they cannot be on.
        /// </para>
        /// <para>
        /// Sampled on the same grid as the disorder reading, and deliberately
        /// coarse. The question is whether a formation is broadly on passable
        /// country, not whether one man's boot is on a rock.
        /// </para>
        /// </remarks>
        public bool FormationFits(UnitInstance unit, Vec2 centre, Facing facing)
        {
            if (unit == null) throw new ArgumentNullException(nameof(unit));

            using var _profile = PlanningProfile.Measure(PlanningProfile.Step.FormationFits);

            Footprint footprint = unit.Footprint;
            var shape = new OrientedRect(centre, facing, footprint);

            // Coarser than the disorder reading on purpose. This one is asked
            // several times a tick, including once per candidate bearing while
            // a regiment is looking for a way through, so it has to be cheap.
            int across = SampleCount(footprint.Width, MaxFitSamplesAcross);
            int deep = SampleCount(footprint.Depth, MaxFitSamplesDeep);

            Vec2 right = shape.Right;
            Vec2 forward = shape.Forward;

            for (int d = 0; d < deep; d++)
            {
                float alongDepth = Offset(d, deep, footprint.HalfDepth);

                for (int a = 0; a < across; a++)
                {
                    Vec2 point = shape.Centre
                               + right * Offset(a, across, footprint.HalfWidth)
                               + forward * alongDepth;

                    // Off the map counts as ground nobody can stand on, which
                    // is also what keeps a formation from overhanging the edge.
                    if (!Terrain.Bounds.Contains(point)) return false;

                    if (Movement.SpeedMultiplier(Terrain.At(point), unit.Def.Movement) <= 0f)
                        return false;
                }
            }

            return true;
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
