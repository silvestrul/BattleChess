using System;
using BattleChess.Contracts;

namespace BattleChess.Rules
{
    /// <summary>
    /// One regiment standing on the field: who owns it, where it is, which way
    /// it faces, how many men are left, and how willing they are.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Lives in Rules, not Contracts. This is authoritative state, and the
    /// client must never hold it — a client sees only the fogged projection of
    /// whatever it is allowed to know. The assembly split makes that a compile
    /// error rather than a matter of discipline.
    /// </para>
    /// <para>
    /// Mutable by design. A battle turn steps sixty times, and allocating a new
    /// object per unit per tick would be waste for no benefit — nothing outside
    /// the simulation ever holds a reference to one of these.
    /// </para>
    /// </remarks>
    public sealed class UnitInstance
    {
        private int _strength;
        private float _morale;
        private float _organization;

        public UnitId Id { get; }

        public PlayerId Owner { get; }

        /// <summary>What kind of unit this is. Its per-man qualities live here.</summary>
        public UnitDef Def { get; }

        /// <summary>Centre of the formation, in world metres.</summary>
        public Vec2 Position { get; set; }

        /// <summary>Which way the formation is facing.</summary>
        public Facing Facing { get; set; }

        public UnitState State { get; set; }

        /// <summary>
        /// Men still standing. Drives frontage, hitting power and staying power
        /// all at once.
        /// </summary>
        public int Strength
        {
            get => _strength;
            set => _strength = Math.Max(0, value);
        }

        /// <summary>Strength this unit began the battle with.</summary>
        public int InitialStrength { get; }

        /// <summary>
        /// Willingness to keep fighting, 0 to 1, against this unit's own morale
        /// rating. Falls with casualties, shock, a commander's death, and
        /// watching neighbours break. Governs <i>whether</i> a unit routs.
        /// </summary>
        /// <remarks>
        /// Per unit, not per type. Three spearmen regiments are not
        /// interchangeable: one that has been shelled and force-marched is a
        /// different proposition from a fresh one at identical strength, and
        /// that difference is what makes reserves and rotation matter.
        /// </remarks>
        public float Morale
        {
            get => _morale;
            set => _morale = Math.Clamp(value, 0f, 1f);
        }

        /// <summary>
        /// How well the formation is holding together, 0 to 1. Spent by
        /// reshaping, by broken ground, by charging and being charged. Governs
        /// <i>how well</i> a unit fights while it is still fighting.
        /// </summary>
        public float Organization
        {
            get => _organization;
            set => _organization = Math.Clamp(value, MinimumOrganization, 1f);
        }

        /// <summary>
        /// How disordered a regiment can get before it stops getting worse.
        /// </summary>
        /// <remarks>
        /// A body of men that has lost all formation is a mob, and a mob is
        /// what routing represents — so there is no need for cohesion to model
        /// it as well. Left unbounded, the two rules doubled up: regiments that
        /// were still standing and still willing to fight had been ground down
        /// to a state where they could barely do either, and every long battle
        /// ended between two rabbles rather than two armies.
        ///
        /// The floor also puts a ceiling on how much of the game any single
        /// mistake can decide, which matters more here than usual because
        /// cohesion multiplies attack, defence, stopping power and breakthrough
        /// all at once.
        /// </remarks>
        public const float MinimumOrganization = 0.4f;

        /// <summary>The order this unit is currently drawn up in.</summary>
        public FormationDef FormationOrder { get; private set; }

        /// <summary>The march this unit is carrying out, or null if it is standing.</summary>
        public MovementRoute? Route { get; set; }

        /// <summary>Whether the unit is currently under way.</summary>
        public bool IsMarching => Route != null && !Route.IsComplete;

        /// <summary>
        /// This unit's standing instruction for meeting the enemy. Persists
        /// until changed, so a reserve set to hold back stays that way.
        /// </summary>
        /// <remarks>
        /// Advance by default, because that is what a player means by "go
        /// there". Defending by default read as the game ignoring orders: a
        /// regiment told to march would stop at the edge of an enemy's zone of
        /// control — too far to fight, too close to be safe — and stand there
        /// until it was shelled to pieces. Holding back is a decision, so it
        /// should be one somebody made.
        /// </remarks>
        public Stance Stance { get; set; } = Stance.Advance;

        /// <summary>What this unit has been told to do.</summary>
        public UnitOrder Order { get; private set; } = UnitOrder.Stand();

        /// <summary>
        /// The enemy currently holding this unit up, or <see cref="UnitId.None"/>.
        /// </summary>
        /// <remarks>
        /// Kept so a halted unit is not immediately re-issued the same route it
        /// was just stopped from taking. Without it the order system and the
        /// contact system fight each other every tick.
        /// </remarks>
        public UnitId HeldUpBy { get; set; } = UnitId.None;

        /// <summary>
        /// Morale damage reported by combat this pulse, waiting to be applied.
        /// </summary>
        /// <remarks>
        /// Combat knows what happened — who was flanked, who was charged, who
        /// lost the exchange — but not what it should cost. Recording the shock
        /// and letting the morale system decide the consequence keeps the two
        /// rules from being tangled together, and means the thresholds live in
        /// one place.
        /// </remarks>
        public float PendingMoraleShock { get; set; }

        /// <summary>Enemies in contact at the last combat pulse.</summary>
        public int EnemiesInContact { get; set; }

        /// <summary>
        /// Total width, in metres, that every enemy in contact is asking of this
        /// regiment's line — summed at the last combat pulse.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A regiment has one frontage and cannot bring more than it to bear,
        /// however many enemies are on it. This is how much is being asked;
        /// where it exceeds the frontage the regiment actually has, every
        /// enemy's share is scaled down in proportion.
        /// </para>
        /// <para>
        /// It replaces dividing by the number of enemies, which counted the same
        /// thing twice. Two regiments drawn up side by side already overlap only
        /// half a defender's front each — the geometry has divided the line
        /// before any rule does — so halving it again for the pair of them left
        /// the defender answering with a quarter of its strength and made a
        /// second attacker worth far more than it should be. Proportions come
        /// out right on their own: attackers sharing sixty and forty metres of a
        /// hundred-metre front are answered with sixty and forty.
        /// </para>
        /// <para>
        /// The cap is what the divisor was protecting, and it still holds.
        /// Regiments stacked on the same ground each overlap the whole of it, so
        /// the sum runs to several times the frontage and each is scaled back to
        /// its share — three on one spot get a third apiece rather than a full
        /// line each.
        /// </para>
        /// </remarks>
        public float ClaimedFrontage { get; set; }

        /// <summary>Ticks left before this unit can shoot again.</summary>
        public int ReloadRemaining { get; set; }

        /// <summary>
        /// Shots this regiment has left, or -1 if it never runs out.
        /// </summary>
        /// <remarks>
        /// Counted for the whole body rather than per man, because that is the
        /// number the player cares about: how many more volleys is this worth.
        /// </remarks>
        public int ShotsLeft { get; set; } = -1;

        public bool HasAmmunition => ShotsLeft != 0;

        /// <summary>
        /// This regiment's own steadiness, rolled once when it was raised.
        /// </summary>
        /// <remarks>
        /// A multiplier on the book morale figure, so two regiments of the same
        /// troops are not interchangeable. One is steadier than the other and
        /// you find out which under pressure — which is the whole reason to
        /// hold a reserve and to remember which regiment held last time.
        /// </remarks>
        public float Quality { get; set; } = 1f;

        /// <summary>Steadiness actually used by the morale rule.</summary>
        public float MoraleRating => Def.Get(UnitAttributes.Morale) * Quality;

        /// <summary>
        /// The body of regiments this one manoeuvres as part of, or zero if it
        /// is on its own.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A bond is a wing, not a formation. The regiments stay separate
        /// rectangles that fight and break individually; what they share is a
        /// pace and a place in the line, so an order given to any of them moves
        /// all of them without disturbing the shape they stand in.
        /// </para>
        /// <para>
        /// An integer rather than a list of members, so nothing has to be kept
        /// consistent when a regiment is destroyed — a bond is simply whoever is
        /// still carrying the same number.
        /// </para>
        /// </remarks>
        public int Bond { get; set; }

        /// <summary>Where the unit stood when it last received an order.</summary>
        /// <remarks>Anchors pursuit, so an aggressive unit cannot be lured off the field.</remarks>
        public Vec2 OrderAnchor { get; private set; }

        /// <summary>
        /// The front this unit was told to hold, or held when it was ordered.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Remembered rather than read live, because a regiment turns for
        /// reasons that are not a change of plan — coming round to thread a gap
        /// between two woods, most obviously. Reading the current facing would
        /// let each of those quietly become the new intent, so a unit that
        /// squeezed through something would come out the far side permanently
        /// facing whichever way the gap happened to run.
        /// </para>
        /// <para>
        /// Set at muster to the front the regiment was deployed on, and not
        /// merely left at its default. A bearing that has never been written is
        /// due east, and once halted regiments began wheeling to hold the front
        /// they were told to, every unit that had not yet received an order
        /// turned east on the opening tick — so one whole army span about before
        /// the battle started.
        /// </para>
        /// </remarks>
        public Facing OrderFacing { get; private set; }

        /// <summary>
        /// The front this regiment is coming round onto for the last hundred
        /// metres of a charge, or null if it is not dressing on anybody.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Two bodies of men meet properly or they meet badly, and the
        /// difference is decided in the final approach rather than at the moment
        /// of impact. A regiment marches at its enemy by whatever line the
        /// ground allows — often a diagonal — and then, close in, squares up:
        /// it repositions onto the enemy's centre and comes round until the two
        /// rectangles are face to face.
        /// </para>
        /// <para>
        /// Kept here rather than derived, because movement needs to know a
        /// regiment is dressing without re-deciding it. That decision costs a
        /// route plan, and it is made once every few ticks; the facing it
        /// produces is wanted on every one of them.
        /// </para>
        /// </remarks>
        public Facing? DressingBearing { get; set; }

        // ---- Whether a march is getting anywhere -------------------------------

        /// <summary>The closest this regiment has come to its goal on this order.</summary>
        public float NearestApproach { get; set; } = float.MaxValue;

        /// <summary>Ticks since it last got meaningfully closer.</summary>
        public int TicksWithoutProgress { get; set; }

        /// <summary>How many times this order has been re-planned after stalling.</summary>
        public int FailedReplans { get; set; }

        /// <summary>Forgets what the march had achieved, as when a fresh one begins.</summary>
        public void ForgetProgress()
        {
            NearestApproach = float.MaxValue;
            TicksWithoutProgress = 0;
        }

        /// <summary>The friendly regiment this one is currently working its way round.</summary>
        /// <remarks>
        /// Kept so the choice of side is made once and then held. Deciding it
        /// afresh every tick is what produced the thrashing: a regiment sitting
        /// on the line that separates the two answers gets a different one each
        /// time it asks.
        /// </remarks>
        public UnitId GoingRound { get; set; } = UnitId.None;

        /// <summary>
        /// The bearing it committed to stepping off along, to get round
        /// <see cref="GoingRound"/>.
        /// </summary>
        /// <remarks>
        /// A bearing on the world and not a side of the line of march. Storing
        /// it as a side does not work: the perpendicular is taken from the
        /// current heading, and the heading is itself being turned by the
        /// deflection — so "the same side" quietly means a different direction
        /// every tick, and the commitment holds nothing.
        /// </remarks>
        public Facing GoingRoundBearing { get; set; }

        /// <summary>
        /// Shorter than this and an order is not a move at all, in metres.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A guard against a degenerate order rather than a rule about
        /// manoeuvre. A destination on top of the regiment gives no line of
        /// march to face along, and asking for the bearing of a zero-length
        /// vector answers due east — which would swing an army to face east the
        /// first time one of its regiments was told to go where it already is.
        /// That happens routinely in a group order, where every member is
        /// displaced by the same amount and one of them may already be there.
        /// </para>
        /// <para>
        /// There is deliberately no larger threshold than this. Three separate
        /// versions of one tried to say how long a move must be before a
        /// regiment turns to face it — two frontages, then half a frontage —
        /// and each was reported as a regiment ignoring an order. The last
        /// recording settled it: every move that failed to turn was between
        /// nine and eighteen metres, and every one that turned was twenty-three
        /// or more. Fine adjustments are exactly where a player is most
        /// particular about which way a regiment points.
        /// </para>
        /// <para>
        /// It also caused the second half of that report — a regiment stopping
        /// part-way round. A short order taken mid-wheel wrote the <i>current</i>
        /// facing as the front to hold, freezing the regiment at whatever angle
        /// it had reached.
        /// </para>
        /// <para>
        /// Holding a front across a move is still there, and is now the only
        /// thing the drag means: a fighting withdrawal facing the enemy, a line
        /// sidling along its own front. Both are real orders and both should be
        /// ones somebody deliberately gives.
        /// </para>
        /// </remarks>
        private const float NotReallyAMoveMetres = 2f;

        /// <summary>
        /// The front to hold for an order that did not name one.
        /// </summary>
        private Facing FrontFor(UnitOrder order, Vec2 anchor)
        {
            if (order.Kind != OrderKind.Move) return Facing;

            Vec2 toDestination = order.Destination - anchor;

            if (toDestination.Length < NotReallyAMoveMetres) return Facing;

            return Facing.FromVector(toDestination);
        }

        /// <summary>Gives the unit a new instruction, clearing any current march.</summary>
        public void GiveOrder(UnitOrder order, Vec2 anchor)
        {
            Order = order;
            OrderAnchor = anchor;
            OrderFacing = order.Bearing ?? FrontFor(order, anchor);
            Route = null;
            HeldUpBy = UnitId.None;
            DressingBearing = null;

            ForgetProgress();
            FailedReplans = 0;
            GoingRound = UnitId.None;

            if (order.StanceOverride.HasValue)
                Stance = order.StanceOverride.Value;
        }

        public UnitInstance(UnitId id, PlayerId owner, UnitDef def, Vec2 position, Facing facing, int strength, FormationDef formation)
        {
            Def = def ?? throw new ArgumentNullException(nameof(def));
            FormationOrder = formation ?? throw new ArgumentNullException(nameof(formation));

            if (strength <= 0)
                throw new ArgumentOutOfRangeException(nameof(strength), strength, "A unit must be raised with at least one man.");

            Id = id;
            Owner = owner;
            Position = position;
            Facing = facing;
            OrderFacing = facing;
            _strength = strength;
            InitialStrength = strength;
            State = UnitState.Steady;
            _morale = 1f;
            _organization = 1f;
        }

        /// <summary>Fraction of the original body still standing, 0 to 1.</summary>
        public float StrengthFraction => InitialStrength > 0 ? _strength / (float)InitialStrength : 0f;

        /// <summary>Men lost so far.</summary>
        public int Casualties => Math.Max(0, InitialStrength - _strength);

        /// <summary>The unit's current order, its natural one scaled by its formation.</summary>
        public Formation Formation => FormationOrder.ApplyTo(Def.NaturalFormation);

        /// <summary>
        /// The ground this unit currently covers. Computed, never stored — so it
        /// answers to both casualties and formation at once. A mauled regiment
        /// genuinely occupies less frontage than a fresh one, and the same
        /// regiment in column is a third the width it was in line.
        /// </summary>
        public Footprint Footprint => Formation.FootprintFor(Math.Max(1, _strength));

        /// <summary>
        /// Redraws the unit in a different order, paying the organization it
        /// costs.
        /// </summary>
        /// <remarks>
        /// Instant for now. Reshaping ought to take time as well as
        /// organization — a regiment caught mid-manoeuvre is the classic
        /// disaster — but that needs the tick loop, so for the moment the cost
        /// is the whole of the price.
        /// </remarks>
        /// <returns>Organization actually spent, or zero if nothing changed.</returns>
        public float AdoptFormation(FormationDef formation)
        {
            if (formation == null) throw new ArgumentNullException(nameof(formation));
            if (formation.Key == FormationOrder.Key) return 0f;

            float before = Organization;

            FormationOrder = formation;
            Organization -= formation.OrganizationCost;

            return before - Organization;
        }

        /// <summary>This unit's footprint placed where it actually stands.</summary>
        public OrientedRect Shape => new OrientedRect(Position, Facing, Footprint);

        /// <summary>Open-ground speed in metres per second, ignoring terrain.</summary>
        public float BaseSpeed => Def.Speed;

        /// <summary>How far this unit's zone of control reaches, in metres.</summary>
        public float ZoneOfControl => Def.Get(UnitAttributes.ZoneOfControl);

        /// <summary>
        /// General degradation from losing cohesion, applied to everything a
        /// unit does mechanically.
        /// </summary>
        /// <remarks>
        /// Never reaches zero: a broken-up spear wall is a poor wall, not an
        /// absent one.
        /// </remarks>
        private float ConditionFactor => 0.35f + 0.65f * Organization;

        /// <summary>
        /// How much of a formation's advantage the unit is actually getting.
        /// </summary>
        /// <remarks>
        /// A formation is a thing men <i>do</i>, not a label they carry. A square
        /// only resists cavalry while the ranks are locked; men who have stopped
        /// holding it are a crowd standing in roughly that shape, and get none of
        /// its benefit. So the multiplier decays toward 1 as organization falls
        /// — you can absolutely have a very loose square, and it will not save
        /// you.
        ///
        /// This is what makes cohesion the stat that decides charges: it both
        /// degrades the unit and strips the formation it was relying on.
        /// </remarks>
        private float FormationEffect(float multiplier) => 1f + (multiplier - 1f) * Organization;

        /// <summary>
        /// How hard this unit is to ride through, accounting for its formation
        /// and how well it is holding together.
        /// </summary>
        public float EffectiveStoppingPower =>
            Def.Get(UnitAttributes.StoppingPower)
            * FormationEffect(FormationOrder.StoppingMultiplier)
            * ConditionFactor;

        /// <summary>
        /// How well this unit forces through an enemy line, accounting for its
        /// formation and how well it is holding together.
        /// </summary>
        public float EffectiveBreakthrough =>
            Def.Get(UnitAttributes.Breakthrough)
            * FormationEffect(FormationOrder.BreakthroughMultiplier)
            * ConditionFactor;

        /// <summary>Total value of a per-man attribute across the surviving men.</summary>
        public float TotalOf(AttributeKey<float> key) => Def.Get(key) * _strength;

        public bool IsOnField => State.IsOnField();
        public bool IsFighting => State.IsFighting();

        /// <summary>
        /// Applies casualties, destroying the unit if nobody is left.
        /// </summary>
        public void TakeCasualties(int men)
        {
            if (men <= 0) return;

            Strength -= men;

            if (_strength == 0 && State != UnitState.Captured)
                State = UnitState.Destroyed;
        }

        public override string ToString() =>
            $"{Id} {Def.DisplayName} ({Owner}) {_strength}/{InitialStrength} {FormationOrder.Key} " +
            $"morale {Morale:0.00} org {Organization:0.00} {State} at {Position}";
    }
}
