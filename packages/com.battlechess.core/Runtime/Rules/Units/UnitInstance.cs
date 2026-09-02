using System;
using System.Collections.Generic;
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

        private Vec2 _position;
        private Facing _facing;
        private OrientedRect _shape;
        private bool _shapeKnown;

        /// <summary>Centre of the formation, in world metres.</summary>
        /// <remarks>
        /// Backed rather than automatic so that moving a regiment can tell the
        /// battle it has moved. <see cref="BattleState"/> keeps a spatial index
        /// of where everybody is standing, and the only thing that can
        /// invalidate it is this setter. Written this way rather than by
        /// rebuilding the index every time somebody asks, because "who is near
        /// this line" is asked tens of thousands of times per plan and answered
        /// far less often than it changes.
        /// </remarks>
        public Vec2 Position
        {
            get => _position;
            set
            {
                _position = value;
                _shapeKnown = false;
                Home?.NoteUnitsMoved();
            }
        }

        /// <summary>Which way the formation is facing.</summary>
        /// <remarks>
        /// Not indexed on: the index buckets a regiment by its centre and widens
        /// every query by the widest bounding radius on the field, so which way
        /// a body points can never move it into a bucket the query did not
        /// already look in.
        /// </remarks>
        public Facing Facing
        {
            get => _facing;
            set
            {
                _facing = value;
                _shapeKnown = false;
            }
        }

        /// <summary>
        /// The battle this regiment was raised into, so that moving it can
        /// invalidate what the battle has cached about where everybody is.
        /// Null until it is raised.
        /// </summary>
        internal BattleState? Home { get; set; }

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

        /// <summary>Who this unit has already reported being held up by.</summary>
        /// <remarks>
        /// So a regiment standing behind one of its own for two hundred ticks
        /// says so once rather than two hundred times (W7).
        /// </remarks>
        public UnitId SaidItWasHeldUpBy { get; set; } = UnitId.None;

        /// <summary>
        /// Who the first leg of this unit's chase route was planned against, or
        /// <see cref="UnitId.None"/> if that leg was clear when it was planned.
        /// </summary>
        /// <remarks>
        /// <b>M39.</b> A route is worth re-planning when the answer could have
        /// changed, and the cheapest thing that says so is the first leg meeting
        /// somebody other than whoever it was drawn around. One swept rectangle
        /// a tick against a whole plan's several hundred legs.
        /// </remarks>
        public UnitId ChasePlannedAgainst { get; set; } = UnitId.None;

        /// <summary>
        /// The enemy this regiment is closing with, and which is therefore not
        /// an obstacle to it.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Mx2d and M15.</b> An enemy is a wall to everybody except the
        /// regiment sent to break it. An explicit attack order says who that is
        /// through <c>Order.Target</c>; an aggressive regiment closing with
        /// somebody it was never told about has no order to read, so the chase
        /// writes its quarry here before it plans.
        /// </para>
        /// <para>
        /// Cleared whenever a unit is not chasing anybody, because a stale one
        /// is worse than none: it would let a marching regiment walk through an
        /// enemy it fought three orders ago.
        /// </para>
        /// </remarks>
        public UnitId ClosingWith { get; set; } = UnitId.None;

        /// <summary>
        /// The tick a stuck chase may ask for a new route again.
        /// </summary>
        /// <remarks>
        /// Set from the route's own first leg — a quarter of the time that leg
        /// takes to walk — so a regiment shuffling a few metres asks often and
        /// one crossing a field asks rarely, and neither needs a constant
        /// choosing for it.
        /// </remarks>
        public int AskAboutTheChaseOnTick { get; set; }

        /// <summary>Where the quarry stood when this chase was last planned.</summary>
        /// <remarks>
        /// A cadence alone cannot govern a pursuit: hold a chase back five ticks
        /// and a broken enemy walks away, which is the failure that killed the
        /// first attempt at this and killed the second. A quarry that has moved
        /// is the plainest case of the answer having changed, so it re-plans on
        /// the movement and waits on the clock only while its quarry stands
        /// still.
        /// </remarks>
        public Vec2 ChaseAimedAt { get; set; }

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
        /// Whether this regiment is fighting because something got in its way
        /// rather than because it was sent to.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A regiment marching somewhere that meets an enemy on the road turns
        /// and fights it, which is right. What it must not do is treat that as
        /// having been ordered to attack: when the enemy breaks, the regiment
        /// stands where the fight ended rather than streaming off after it.
        /// </para>
        /// <para>
        /// Nor does it pick its march back up. The order was to go somewhere by
        /// a road that is no longer there, through ground that now has a fight
        /// on it and men strewn across it. Deciding whether that order still
        /// makes sense is the player's, and a regiment that quietly resumes a
        /// march the situation has overtaken is worse than one that waits to be
        /// told.
        /// </para>
        /// <para>
        /// Cleared by <see cref="GiveOrder"/>, so any order the player gives —
        /// including an attack on the same enemy — restores the pursuit.
        /// </para>
        /// </remarks>
        public bool ForcedIntoThisFight { get; set; }

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

        /// <summary>
        /// Where this regiment stands in its wing, in the wing's own frame:
        /// <c>X</c> forward of the wing centre along the line of advance,
        /// <c>Y</c> across it.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>[M154], and it is what stops a line collapsing into a knot when it
        /// is told to attack.</b> A chase aims at a stand-off from the quarry
        /// along <i>its own</i> bearing to that quarry. Five regiments in a line
        /// have five slightly different bearings to the same enemy, and all five
        /// aim points sit just in front of it - so the line folds inward as it
        /// advances, which is exactly what a play-test showed.
        /// </para>
        /// <para>
        /// A station is measured once, when the wing is ordered, and held. The
        /// aim point becomes the wing's contact point plus this offset, so the
        /// regiments keep the spacing they set off with: <b>the end position is
        /// close to the starting distance, because it is the starting distance.</b>
        /// </para>
        /// <para>
        /// <b>Measured, not re-derived.</b> Working the slots out afresh from
        /// where everybody currently stands would look identical and drift: a
        /// regiment shoved aside by broken ground moves the centre, which moves
        /// everybody else's slot, which is a line that wanders. Held offsets
        /// let a regiment leave its station to get round something and come back
        /// to it, which is what was asked for.
        /// </para>
        /// <para>
        /// <b>Nothing clears it and nothing needs to.</b> It is read only while
        /// the regiment is in a wing and under an attack order, and a fresh wing
        /// order overwrites it. That is deliberate: an order given through
        /// [M143]'s book is drawn long before <c>GiveOrder</c> is called on it,
        /// so a station cleared by giving orders would be cleared after it was
        /// set.
        /// </para>
        /// </remarks>
        public Vec2? Station { get; set; }

        /// <summary>
        /// Fractions of a board cell this regiment has earned but not yet spent.
        /// </summary>
        /// <remarks>
        /// <b>[M157].</b> A board turn buys a whole number of cells, but it is
        /// spent over the turn's ticks rather than applied at the end of it -
        /// see <see cref="Grid.BoardTurn.Tick"/>. This is what carries the
        /// remainder between ticks, so cavalry steps every fifth one and foot
        /// every fifteenth instead of both jumping at the bell.
        /// </remarks>
        public float BoardStepCredit { get; set; }

        // ---- Whether a march is getting anywhere -------------------------------

        /// <summary>The closest this regiment has come to its goal on this order.</summary>
        public float NearestApproach { get; set; } = float.MaxValue;

        /// <summary>Ticks since it last got meaningfully closer.</summary>
        public int TicksWithoutProgress { get; set; }

        /// <summary>How many times this order has been re-planned after stalling.</summary>
        public int FailedReplans { get; set; }

        /// <summary>
        /// The nearest this regiment has yet got to where it was sent, in
        /// metres, across every march this order has taken.
        /// </summary>
        /// <remarks>
        /// <b>[M127].</b> Distinct from <see cref="NearestApproach"/>, which is
        /// measured along the current route and forgotten whenever a new one is
        /// made - exactly what a regiment creeping forward in stages does every
        /// time. This is measured to the order and survives the routes, so that
        /// <c>FailedReplans</c> can be spent on tries that gain nothing rather
        /// than on tries.
        /// </remarks>
        public float NearestTheOrder { get; set; } = float.MaxValue;

        /// <summary>Forgets what the march had achieved, as when a fresh one begins.</summary>
        public void ForgetProgress()
        {
            NearestApproach = float.MaxValue;
            TicksWithoutProgress = 0;
        }

        // ---- What the march actually cost -------------------------------------

        /// <summary>Ground covered under this order, in metres.</summary>
        /// <remarks>
        /// <b>W5.</b> Kept because the arrival line used to report the pace the
        /// regiment <i>could</i> have made on the ground it finished on — asked
        /// of the same code under suspicion. Every arrival in a recorded battle
        /// read "4,8 m/s"; the achieved pace was 2.6 to 3.6. A line written to
        /// diagnose slow marches reported the one number that could never show
        /// one. These three count the outcome instead.
        /// </remarks>
        public float GroundCovered { get; set; }

        /// <summary>Ticks spent under way on this order.</summary>
        public int MarchingTicks { get; set; }

        /// <summary>Of those, how many were spent sharing ground with its own ([M20]).</summary>
        public int TicksInsideItsOwn { get; set; }

        /// <summary>Forgets what the march cost, as when a fresh one begins.</summary>
        public void ForgetTheReckoning()
        {
            GroundCovered = 0f;
            MarchingTicks = 0;
            TicksInsideItsOwn = 0;
        }

        // ---- Who it is standing in --------------------------------------------

        /// <summary>
        /// One of its own whose ground this regiment is currently sharing, and
        /// the tick it started.
        /// </summary>
        public readonly struct Lap
        {
            public Lap(UnitId other, int since)
            {
                Other = other;
                Since = since;
            }

            public readonly UnitId Other;
            public readonly int Since;
        }

        /// <summary>
        /// The friendly regiments this one was standing in as of last tick.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>W6.</b> Bookkeeping for the log and nothing else — no rule reads
        /// it. Two of its own sharing ground is the single thing M1 spent the
        /// project forbidding and the thing every play-test report has been
        /// about, and it was <b>completely silent</b>: the overlap cost, the
        /// shuffle apart and the yield rule all ran without a word. A collision
        /// could only be inferred from the press-through line, which covers the
        /// one case the planner <i>meant</i> — so every accidental overlap, which
        /// is all the interesting ones, left no trace at all.
        /// </para>
        /// <para>
        /// A list rather than a single id because a regiment in a line laps its
        /// neighbours on both sides, and rather than a set because rules code
        /// does not iterate anything whose order is a hash (the determinism rule
        /// in M0). Built in ascending id order, so it is stable.
        /// </para>
        /// </remarks>
        public List<Lap> StandingIn { get; } = new List<Lap>();

        /// <summary>Where this tick's laps are gathered before being compared with last tick's.</summary>
        public List<Lap> LappingNow { get; } = new List<Lap>();

        /// <summary>
        /// Which rung of M18's ladder last answered for this regiment: 1 straight,
        /// 2 round it, 3 threaded side-on, 4 through its own, 5 the search.
        /// </summary>
        /// <remarks>
        /// For the log and nothing else — no rule reads it, and it must not
        /// become one, because a plan that depended on what the last plan said
        /// would stop being a function of the field. Kept so the ladder reports
        /// its answer when the answer <i>changes</i>: M11 re-plans on a cadence,
        /// so a regiment walking across open ground re-decides "straight there"
        /// dozens of times, and saying so every time buried the recording under
        /// itself.
        /// </remarks>
        public int LastRung { get; set; }

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

        /// <summary>
        /// The three fields the planner reads off an order, set without any of
        /// the rest of what giving an order does.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>[M145].</b> A route depends on the front the order asks to arrive
        /// on - <c>RouteSearch</c> reads <see cref="OrderFacing"/> - so a plan
        /// taken for an order that has not been given comes out different from
        /// the one that order will actually produce. Under [M143] that is the
        /// difference between the line the player is shown and the route the
        /// regiment walks, which is the one thing plan-then-fire must never get
        /// wrong.
        /// </para>
        /// <para>
        /// <b>Deliberately not <see cref="GiveOrder"/>.</b> That clears the
        /// route, the hold-up, the dressing bearing and six counters besides,
        /// which is right when an order is really being given and destructive
        /// when one is only being costed - a regiment part-way through last
        /// turn's march would have it thrown away by the act of drawing a new
        /// order it may never be given.
        /// </para>
        /// </remarks>
        public (UnitOrder Order, Vec2 Anchor, Facing Front) BorrowForPlanning(UnitOrder order, Vec2 anchor)
        {
            (UnitOrder, Vec2, Facing) was = (Order, OrderAnchor, OrderFacing);

            Order = order;
            OrderAnchor = anchor;
            OrderFacing = order.Bearing ?? FrontFor(order, anchor);

            return was;
        }

        /// <summary>
        /// Settles a halted regiment onto a front, as the front it is holding
        /// <i>and</i> the front it was ordered to hold.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>[M152], and it fixes a regiment that turned a few degrees back and
        /// forth for ever.</b> An order's front comes from
        /// <see cref="FrontFor"/>, which is the bearing from where the regiment
        /// stood to where it was sent - a free angle. On the board a regiment
        /// may only hold one of the lattice's facings, so the two disagree by
        /// up to half a step, and the disagreement is a loop:
        /// <c>WheelOnTheSpot</c> turns the halted regiment toward the ordered
        /// front, the end of the turn snaps it back to a board facing, and the
        /// next turn turns it again. Cavalry shows it worst because it has the
        /// highest turn rate.
        /// </para>
        /// <para>
        /// Setting only <see cref="Facing"/> cannot fix it, because the thing
        /// being turned toward is <see cref="OrderFacing"/>. Both have to say
        /// the same thing, which is what this is for: <b>the regiment has
        /// stopped, and this is its front now.</b>
        /// </para>
        /// <para>
        /// Deliberately not <c>GiveOrder</c>, for the reason [M145] gives:
        /// giving an order clears the route and six counters, and a regiment
        /// that has merely finished walking has not been given anything.
        /// </para>
        /// </remarks>
        public void SettleFrontOn(Facing front)
        {
            Facing = front;
            OrderFacing = front;
            DressingBearing = null;
        }

        /// <summary>Puts back what <see cref="BorrowForPlanning"/> took.</summary>
        public void ReturnAfterPlanning((UnitOrder Order, Vec2 Anchor, Facing Front) was)
        {
            Order = was.Order;
            OrderAnchor = was.Anchor;
            OrderFacing = was.Front;
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
            ForgetTheReckoning();
            FailedReplans = 0;
            NearestTheOrder = float.MaxValue;
            GoingRound = UnitId.None;
            ForcedIntoThisFight = false;

            // A fresh order gets the ladder's answer said out loud again, even
            // if it is the same answer. "Still walking straight" is not news
            // within one march; it is on a new one.
            LastRung = 0;

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
        /// The ground this unit holds: its block. Two to one, and the same size
        /// on the last turn of a battle as on the first.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This is what collides, what blocks, what is clicked, what casts a
        /// zone of control and what is drawn. One rectangle answering all of
        /// them, because a regiment that looks flush with its neighbour and is
        /// not is a bug the player cannot see and cannot work around.
        /// </para>
        /// <para>
        /// It used to shrink with casualties, which quietly made the map move
        /// under the player: a line that had been fought hard opened gaps that
        /// nobody had ordered, and a mauled regiment stopped holding the ground
        /// it had been put on. Losses reduce how many men can <i>fight</i> —
        /// see <see cref="FightingFrontage"/> — not how much room a body takes
        /// up. The survivors spread out; they do not huddle.
        /// </para>
        /// </remarks>
        public Footprint Footprint => Formation.FootprintFor(Math.Max(1, InitialStrength));

        /// <summary>
        /// How much of the block still has men in it to fight with, in metres.
        /// </summary>
        /// <remarks>
        /// The half of the old footprint that <i>should</i> answer to
        /// casualties. A regiment at half strength holds its whole block but
        /// fights along the middle of it, its flanks thinned to nothing — which
        /// is what a worn line looks like from the other side.
        /// </remarks>
        public float FightingFrontage => Formation.FrontageFor(Math.Max(1, _strength));

        /// <summary>
        /// The ground this regiment's men really stand on — frontage by ranks,
        /// forty metres by six rather than by twenty.
        /// </summary>
        /// <remarks>
        /// What the fighting rules measure, as against <see cref="Footprint"/>,
        /// which is what the world collides with. A regiment's side is six
        /// metres of men whatever depth the block is drawn at, and combat that
        /// read the block would let a flanked regiment answer with three times
        /// the men it has facing that way.
        /// </remarks>
        public Footprint Space => Formation.SpaceFor(Math.Max(1, _strength));

        /// <summary>Where this regiment's men really are, for the fighting rules.</summary>
        public OrientedRect SpaceShape => new OrientedRect(Position, Facing, Space);

        /// <summary>
        /// Places in the front rank with nobody standing in them, in men.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A man who falls in the front rank is replaced by the man behind him,
        /// and that takes a moment. Until it happens the regiment has a hole in
        /// its line and fights with fewer men than its headcount says — which is
        /// why a charge that kills a great many at once hurts far more than the
        /// same losses spread over a quarter of an hour.
        /// </para>
        /// <para>
        /// The rate has to be slower than a pulse or the rule does nothing at
        /// all: casualties land on one exchange and are counted on the next, ten
        /// ticks later, so anything that fills in under ten ticks is never seen
        /// by anything. See <c>CombatSystem.GapsClosedPerTick</c>.
        /// </para>
        /// <para>
        /// A regiment with nothing behind its front rank cannot fill anything.
        /// That is the real cost of being worn down: not that the survivors
        /// fight worse, but that there is no one left to step over them.
        /// </para>
        /// </remarks>
        public float FrontRankGaps { get; set; }

        /// <summary>
        /// Damage owed to this regiment that has not yet come to a whole man,
        /// always less than one.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Casualties are men, and men are whole numbers, but damage is not.
        /// Rounding each exchange on its own threw the remainder away every
        /// time — and anything under half a man rounded to nobody, so a weak
        /// attack did not merely do little, it did <i>literally nothing</i>, for
        /// as long as it went on. Archers at extreme range, a beaten regiment
        /// still swinging, a handful of men round a flank: all of them free to
        /// ignore.
        /// </para>
        /// <para>
        /// Carried instead. What does not add up to a body this pulse is still
        /// owed and is paid on some later one, so nothing is discarded and no
        /// amount of damage is too small to eventually count. Held per regiment
        /// rather than per pair of them, which is also what makes several weak
        /// attackers add up together rather than each rounding separately to
        /// nothing.
        /// </para>
        /// </remarks>
        public float CasualtyDebt { get; set; }

        /// <summary>Melee exchanges this regiment has taken part in.</summary>
        /// <remarks>
        /// Counted because "has this regiment actually got into the fight?" is
        /// one of the most useful questions to ask of a battle, and there was no
        /// way to ask it except by counting the word "exchange" in the log. That
        /// works until the log stops narrating every exchange — which it now
        /// does, because six lines a turn per pair of regiments is what made a
        /// recording unreadable. A fact worth asserting on deserves to be a
        /// fact, not a turn of phrase that a rewording can silently break.
        /// </remarks>
        public int MeleePulses { get; set; }

        /// <summary>
        /// How many ranks deep this regiment actually stands right now, holding
        /// the frontage it is holding.
        /// </summary>
        public int OccupiedRanks => Formation.OccupiedRanks(_strength, FightingFrontage);

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
            _shapeKnown = false;
            Organization -= formation.OrganizationCost;

            return before - Organization;
        }

        /// <summary>This unit's footprint placed where it actually stands.</summary>
        /// <remarks>
        /// <b>Kept, not rebuilt.</b> This was a fresh <see cref="OrientedRect"/>
        /// on every read, and since that type began caching its own axes the
        /// constructor costs a sine and a cosine — so every read of a standing
        /// body's shape paid for trigonometry that had not changed since the
        /// body last moved. One clearance check reads it once per nearby body
        /// and the bench counts thirty-six thousand of them in eighty orders.
        /// A regiment's shape changes when it moves, when it turns, and when it
        /// re-forms, and those are the three places that clear the flag.
        /// </remarks>
        public OrientedRect Shape
        {
            get
            {
                if (!_shapeKnown)
                {
                    _shape = new OrientedRect(_position, _facing, Footprint);
                    _shapeKnown = true;
                }

                return _shape;
            }
        }

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

            // The men who fall are the men who were fighting, and the men who
            // were fighting are the front rank. Every casualty is a hole in the
            // line until somebody steps into it.
            FrontRankGaps += men;

            Strength -= men;

            if (_strength == 0 && State != UnitState.Captured)
                State = UnitState.Destroyed;
        }

        public override string ToString() =>
            $"{Id} {Def.DisplayName} ({Owner}) {_strength}/{InitialStrength} {FormationOrder.Key} " +
            $"morale {Morale:0.00} org {Organization:0.00} {State} at {Position}";
    }
}
