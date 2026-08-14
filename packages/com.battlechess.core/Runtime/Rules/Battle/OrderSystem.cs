using System;
using BattleChess.Contracts;

namespace BattleChess.Rules
{
    /// <summary>
    /// Turns orders and stances into marches: follows a target that moves,
    /// closes with enemies that wander near, and backs away from ones that
    /// should be avoided.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The place where a plan meets what actually happened. A move order says
    /// where to go; the stance says what to do about whatever turns up on the
    /// way, and that is what makes committing orders on incomplete information
    /// tolerable rather than a gamble.
    /// </para>
    /// <para>
    /// Runs before contact and movement, so a decision made here takes effect on
    /// the same tick.
    /// </para>
    /// </remarks>
    public sealed class OrderSystem : IBattleSystem
    {
        /// <summary>How often a chase re-plans its route, in ticks.</summary>
        /// <remarks>
        /// Not every tick: pathfinding is the most expensive thing here, and a
        /// target cannot outrun its own footprint in five seconds.
        /// </remarks>
        private const int RepathIntervalTicks = 5;

        /// <summary>How far a target must move before a chase bothers re-planning.</summary>
        private const float RepathThresholdMetres = 20f;

        /// <summary>
        /// How far an aggressive unit will chase from where it was last ordered.
        /// </summary>
        /// <remarks>
        /// A leash, so a regiment cannot be baited off the field by a scout it
        /// will never catch. Anchored to the last order rather than to the
        /// unit's current position, or it would creep indefinitely.
        /// </remarks>
        private const float PursuitLeashMetres = 200f;

        /// <summary>How far a unit backs off when evading.</summary>
        private const float RetreatDistanceMetres = 150f;

        /// <summary>
        /// How many seconds of the enemy's approach counts as too close for
        /// comfort, when deciding whether to withdraw.
        /// </summary>
        /// <remarks>
        /// Fleeing is a question of time, not of zones of control. Artillery has
        /// a 15 m zone of control and moves at 1.3 m/s; keying its alarm to that
        /// would have it notice a scout at 5.5 m/s only once escape was already
        /// impossible. Measuring how long the threat needs to arrive makes a
        /// unit run from fast things earlier and slow things later, which is
        /// what anyone would actually do.
        /// </remarks>
        private const float ReactionSeconds = 12f;

        /// <summary>How far out an aggressive unit will go looking for a fight.</summary>
        private const float EngagementReachFactor = 2.5f;

        private readonly IPathfinder _pathfinder;

        public OrderSystem(IPathfinder pathfinder)
        {
            _pathfinder = pathfinder ?? throw new ArgumentNullException(nameof(pathfinder));
        }

        public string Name => "Orders";

        public void Step(BattleState battle, int tick, IBattleLog log)
        {
            if (battle == null) throw new ArgumentNullException(nameof(battle));

            foreach (UnitInstance unit in battle.UnitsOnField())
            {
                if (unit.State == UnitState.Routing)
                {
                    RunAway(battle, unit, tick, log);
                    continue;
                }

                if (!unit.IsFighting) continue;

                ReleaseIfClear(battle, unit);

                KeepTheMarchHonest(battle, unit, tick, log);

                switch (unit.Stance)
                {
                    case Stance.Evade:
                        if (TryEvade(battle, unit, tick, log)) continue;
                        break;

                    case Stance.Advance:
                        TryFightWhatBlocks(battle, unit, log);
                        break;

                    case Stance.Aggressive:
                        if (TryFightWhatBlocks(battle, unit, log)) break;
                        if (TryEngageNearby(battle, unit, tick, log)) continue;
                        break;
                }

                if (unit.Order.Kind == OrderKind.Attack)
                    FollowTarget(battle, unit, tick, log);
            }
        }

        // ---- Marches that are getting nowhere ---------------------------------

        /// <summary>How long a march may make no headway before it is re-planned, in ticks.</summary>
        /// <remarks>
        /// Fifteen seconds. Long enough to cover a regiment coming round onto
        /// its bearing, edging along a friendly flank, or picking its way round
        /// a wood; short enough that a genuinely stuck one is noticed before the
        /// player is.
        /// </remarks>
        private const int StallTicks = 15;

        /// <summary>How much closer counts as having got somewhere, in metres.</summary>
        private const float ProgressMetres = 1f;

        /// <summary>How many times a stalled march is re-planned before it is given up on.</summary>
        private const int ReplansBeforeGivingUp = 3;

        /// <summary>
        /// Notices a march that is no longer getting anywhere, and either
        /// re-plans it or admits it cannot be done.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The rule that makes an order always terminate. Every stall found so
        /// far had its own cause — a destination inside a friendly regiment, a
        /// detour that reversed itself every tick, a goal on ground nothing can
        /// stand on — and each was fixed on its own terms. This does not care
        /// why. A regiment that has not got closer to where it is going in
        /// fifteen seconds is not going to, and something has to change.
        /// </para>
        /// <para>
        /// A recorded game had swordsmen ordered onto ground their own spearmen
        /// were standing on. They pressed to within a metre and then thrashed
        /// north and south — twelve reversals of direction in twenty-five ticks
        /// — for the rest of the battle.
        /// </para>
        /// <para>
        /// Only for marches. A unit halted by a zone of control has had its
        /// route cleared and is no longer marching at all, which is a different
        /// rule with its own answer.
        /// </para>
        /// </remarks>
        private void KeepTheMarchHonest(BattleState battle, UnitInstance unit, int tick, IBattleLog log)
        {
            if (unit.Order.Kind != OrderKind.Move || !unit.IsMarching)
            {
                unit.ForgetProgress();
                return;
            }

            float toGoal = Vec2.Distance(unit.Position, unit.Route!.Destination);

            if (toGoal < unit.NearestApproach - ProgressMetres)
            {
                unit.NearestApproach = toGoal;
                unit.TicksWithoutProgress = 0;
                return;
            }

            if (++unit.TicksWithoutProgress < StallTicks) return;

            unit.TicksWithoutProgress = 0;

            if (unit.FailedReplans >= ReplansBeforeGivingUp)
            {
                log.Blocked("Move",
                    $"{unit.Def.DisplayName} cannot get to where it was sent and has stopped " +
                    $"{toGoal:0} m short of it. Something is standing on that ground.",
                    unit.Id);

                unit.Route = null;
                return;
            }

            unit.FailedReplans++;

            // Aim at the best ground near the order rather than at the order
            // itself, which may be somewhere no regiment can be.
            if (!TryFindPlacement(battle, unit, unit.Order.Destination, unit.OrderFacing, out Vec2 placement))
            {
                log.Blocked("Move",
                    $"{unit.Def.DisplayName} can find nowhere near that point to stand.", unit.Id);

                unit.Route = null;
                return;
            }

            PathResult path = _pathfinder.FindPath(unit.Position, placement, unit.Def.Movement);

            if (!path.Found || path.Waypoints.Count < 2)
            {
                unit.Route = null;
                return;
            }

            unit.Route = new MovementRoute(path.Waypoints, wheelFirst: false);
            unit.ForgetProgress();

            log.Decision("Move",
                $"{unit.Def.DisplayName} is not getting through and is trying for ground " +
                $"{Vec2.Distance(placement, unit.Order.Destination):0} m from where it was sent.",
                unit.Id);
        }

        /// <summary>
        /// Turns a unit that has been stopped by an enemy into one that is
        /// attacking it.
        /// </summary>
        /// <remarks>
        /// <para>
        /// What "fight if blocked" has to mean, and without it neither Advance
        /// nor Aggressive meant anything. A zone of control reaches thirty to
        /// forty metres and a melee needs thirteen, so a regiment halted by one
        /// stopped in a place where it could neither fight nor leave — and then
        /// stood there. Under guns that is simply a slower way of being killed:
        /// a recorded game had spearmen and cavalry halt at 39 m and take
        /// fifty-seven volleys of artillery without ever drawing a sword.
        /// </para>
        /// <para>
        /// Only for the stances that mean to press on. A regiment on Defend is
        /// <i>supposed</i> to stop when it meets somebody, which is the whole
        /// difference between telling it to hold and telling it to advance.
        /// </para>
        /// </remarks>
        private static bool TryFightWhatBlocks(BattleState battle, UnitInstance unit, IBattleLog log)
        {
            if (!unit.HeldUpBy.IsValid) return false;

            // Already going for this one, so there is nothing to redirect.
            if (unit.Order.Kind == OrderKind.Attack && unit.Order.Target == unit.HeldUpBy) return false;

            // Nor talked out of a fight it has already joined. Two enemies
            // standing near each other used to hand a regiment back and forth
            // every single tick: it was told to attack the first, contact
            // exempted that one and found the second, and next tick the second
            // became the order and the first became the blocker again. The
            // logs show a body of spearmen doing this two hundred times in a
            // row — re-planning a march, having it cancelled, and re-planning
            // it — while the enemy it was supposedly fighting walked away. Two
            // combat pulses landed in two full minutes.
            //
            // A regiment that has closed with somebody keeps fighting them.
            // Choosing between two enemies already at sword's length is not a
            // decision the order rule should be making every second.
            if (unit.Order.Kind == OrderKind.Attack && unit.Order.Target.IsValid)
            {
                UnitInstance current = battle.Get(unit.Order.Target);

                // Judged on being at grips or nearly so, not on strict contact.
                // Contact is 8 m and a zone of control halts a unit at ten to
                // twenty, so a regiment stopped one metre short of the enemy it
                // was attacking counted as not engaged at all — and was handed
                // straight back to whichever other enemy happened to be
                // nearest.
                if (current.IsFighting &&
                    OrientedRect.Within(unit.Shape, current.Shape,
                        MathF.Max(ContactMetres, current.ZoneOfControl)))
                    return false;
            }

            // Never a shooter. Their answer to something in the way is to shoot
            // it, and sending them at it instead was ruinous — a regiment of
            // archers ordered to march was charging spear walls it could not
            // scratch, losing seven men a pulse while dealing literally none,
            // until it broke. Judged on what the unit is armed for rather than
            // on its name, so a new missile unit inherits it for free.
            if (ShootsRatherThanCharges(unit)) return false;

            UnitInstance blocker = battle.Get(unit.HeldUpBy);
            if (!blocker.IsFighting) return false;

            // Clears the hold-up as a side effect, which is what lets contact
            // stop treating this as a march past and start treating it as an
            // attack pressed home.
            unit.GiveOrder(UnitOrder.Attack(blocker.Id), unit.Position);

            // It fell into this fight rather than being sent to it, which is
            // what decides whether it chases when the other side breaks.
            unit.ForcedIntoThisFight = true;

            log.Decision("Orders",
                $"{unit.Def.DisplayName} cannot get past {blocker.Def.DisplayName} and is going through it.",
                unit.Id);

            return true;
        }

        /// <summary>
        /// Forgets a hold-up once the enemy responsible is gone or out of range,
        /// so the unit can be given fresh orders.
        /// </summary>
        private static void ReleaseIfClear(BattleState battle, UnitInstance unit)
        {
            if (!unit.HeldUpBy.IsValid) return;

            UnitInstance blocker = battle.Get(unit.HeldUpBy);

            // Between the formations, matching the rule that set the hold-up in
            // the first place. Measured centre to centre this released a unit
            // the instant it was halted — two lines a hundred metres wide stop
            // twelve metres apart with their centres still a hundred and twelve
            // metres away, which read as "clear" and wiped the hold-up before
            // anything could act on it. The regiment then stood there for the
            // rest of the battle: too close to be given a fresh march, and with
            // nothing recorded to tell it what to fight.
            if (!blocker.IsFighting ||
                OrientedRect.GapBetween(unit.Shape, blocker.Shape) > blocker.ZoneOfControl * 1.2f)
                unit.HeldUpBy = UnitId.None;
        }

        // ---- Rout ------------------------------------------------------------

        /// <summary>
        /// Sends a broken unit streaming toward its own rear, and off the field
        /// if it gets there.
        /// </summary>
        /// <remarks>
        /// Toward the army's own edge rather than merely away from whoever
        /// frightened it. Routers running somewhere predictable is what makes
        /// pursuit a decision — cavalry can be sent to cut them off — and it is
        /// how men who get clear come home rather than scattering into enemy
        /// country.
        /// </remarks>
        private void RunAway(BattleState battle, UnitInstance unit, int tick, IBattleLog log)
        {
            Vec2 home = battle.GetArmy(unit.Owner).RetreatDirection;
            MapBounds bounds = battle.Terrain.Bounds;

            // Men running are not dressing a line onto anybody.
            unit.DressingBearing = null;

            // Off the edge and away. The men are gone from this battle but not
            // from the army — that is the whole point of routing rather than
            // dying.
            float toEdge = DistanceToEdge(unit.Position, home, bounds);

            if (toEdge <= 5f)
            {
                unit.State = UnitState.Scattered;
                unit.Route = null;

                log.Info("Rout",
                    $"{unit.Def.DisplayName} has quit the field with {unit.Strength} men — scattered, not lost.",
                    unit.Id);

                return;
            }

            if (unit.IsMarching && tick % RepathIntervalTicks != 0) return;

            Vec2 flight = bounds.Clamp(unit.Position + home * MathF.Min(toEdge, RetreatDistanceMetres));
            PathResult path = _pathfinder.FindPath(unit.Position, flight, unit.Def.Movement);

            if (path.Found && path.Waypoints.Count >= 2)
                unit.Route = new MovementRoute(path.Waypoints, wheelFirst: false);
        }

        /// <summary>How far the map edge is in a given direction.</summary>
        private static float DistanceToEdge(Vec2 from, Vec2 direction, MapBounds bounds)
        {
            if (direction.X < -0.5f) return from.X - bounds.Min.X;
            if (direction.X > 0.5f) return bounds.Max.X - from.X;
            if (direction.Y < -0.5f) return from.Y - bounds.Min.Y;

            return bounds.Max.Y - from.Y;
        }

        // ---- Evade -----------------------------------------------------------

        /// <summary>Backs away from the nearest enemy inside this unit's own zone of control.</summary>
        private bool TryEvade(BattleState battle, UnitInstance unit, int tick, IBattleLog log)
        {
            // An explicit attack order wins over the stance. A stance is a
            // standing answer to contact you did not plan for; being told to
            // attack a named enemy is not that, and a unit that turns and runs
            // from an order you deliberately gave is just baffling.
            if (unit.Order.Kind == OrderKind.Attack) return false;

            UnitInstance? threat = NearestClosingThreat(battle, unit);
            if (threat == null) return false;

            // Already backing off — let the current retreat finish rather than
            // re-planning every tick and never actually moving.
            if (unit.IsMarching && tick % RepathIntervalTicks != 0) return true;

            Vec2 away = (unit.Position - threat.Position).Normalised();
            if (away.IsNearZero) away = unit.Facing.Opposite().ToVector();

            // Backed against the edge of the world, a unit evading has to settle
            // for the furthest ground it can actually reach. Clamping alone
            // aimed it into the mountains that ring the map, which failed to
            // path and left it standing where it was — pinned by geography it
            // had every right to run along.
            Vec2 retreat = NearestReachable(
                battle, unit, unit.Position + away * RetreatDistanceMetres, unit.Position);

            PathResult path = _pathfinder.FindPath(unit.Position, retreat, unit.Def.Movement);

            if (!path.Found || path.Waypoints.Count < 2)
            {
                log.Blocked("Order",
                    $"{unit.Def.DisplayName} is trying to evade {threat.Def.DisplayName} but has nowhere to go.",
                    unit.Id);

                return true;
            }

            unit.Route = new MovementRoute(path.Waypoints, wheelFirst: false);
            unit.HeldUpBy = UnitId.None;

            log.Decision("Order",
                $"{unit.Def.DisplayName} is evading {threat.Def.DisplayName}.",
                unit.Id);

            return true;
        }

        // ---- Aggressive ------------------------------------------------------

        /// <summary>Closes with an enemy that has come near, without being told to.</summary>
        private bool TryEngageNearby(BattleState battle, UnitInstance unit, int tick, IBattleLog log)
        {
            // Already doing something about someone.
            if (unit.Order.Kind == OrderKind.Attack) return false;
            if (unit.HeldUpBy.IsValid) return true;

            // Routing enemies count: running them down is the point of being
            // aggressive.
            UnitInstance? quarry = NearestThreat(battle, unit, EngagementReachFactor, includeRouting: true);
            if (quarry == null) return false;

            // Do not be baited away from where the order was given.
            if (Vec2.Distance(unit.OrderAnchor, quarry.Position) > PursuitLeashMetres)
            {
                log.Decision("Order",
                    $"{unit.Def.DisplayName} is holding — {quarry.Def.DisplayName} is beyond its pursuit leash.",
                    unit.Id);

                return false;
            }

            if (unit.IsMarching && tick % RepathIntervalTicks != 0) return true;

            if (ChaseToward(battle, unit, quarry, tick, log, "closing with"))
                return true;

            return false;
        }

        // ---- Attack orders ---------------------------------------------------

        /// <summary>Keeps a chase pointed at its target as the target moves.</summary>
        private void FollowTarget(BattleState battle, UnitInstance unit, int tick, IBattleLog log)
        {
            if (!unit.Order.Target.IsValid) return;

            UnitInstance target = battle.Get(unit.Order.Target);

            // A broken enemy is still a target — the most worthwhile one there
            // is. Standing down the moment they rout meant nobody was ever
            // pursued, and a routed regiment always walked away intact.
            if (!target.IsOnField)
            {
                log.Info("Order", $"{unit.Def.DisplayName} has no target left to attack.", unit.Id);
                unit.GiveOrder(UnitOrder.Stand(), unit.Position);
                return;
            }

            // Unless nobody sent it. A regiment that was marching somewhere and
            // had to fight its way past has done what the situation asked of it;
            // running down the survivors is a separate decision, and so is
            // whether the march it was on still makes any sense. It holds the
            // ground it won and waits to be told.
            if (unit.ForcedIntoThisFight && target.State == UnitState.Routing)
            {
                log.Decision("Order",
                    $"{unit.Def.DisplayName} has driven off {target.Def.DisplayName} and holds its ground — " +
                    "it was marching, not attacking, and will not pursue unless told to.",
                    unit.Id);

                unit.GiveOrder(UnitOrder.Stand(), unit.Position);
                return;
            }

            // Bowmen told to attack are being told to shoot, not to charge. Far
            // enough in that the volleys land is where the order has been
            // carried out — walking the last hundred and eighty metres to cross
            // swords is the opposite of what archers are for, and it was what
            // made a regiment of them charge a spear wall it could not scratch.
            if (ShootsRatherThanCharges(unit) && InShootingPosition(unit, target))
            {
                unit.Route = null;
                return;
            }

            if (InContactWith(unit, target))
            {
                unit.Route = null;
                return;
            }

            // Held up by anyone at all, including the target itself: stop
            // re-planning. Otherwise the order system re-issues the march every
            // tick and the contact system halts it again, which burns work and
            // fills the console with the same line forever.
            if (unit.HeldUpBy.IsValid) return;

            // Crossing into dressing range changes what the march is aiming at,
            // and a target standing still never trips the distance check — so
            // without this the regiment would walk the whole way in on its
            // original bearing and arrive at whatever angle it set off at,
            // which is the entire thing dressing exists to prevent.
            bool shouldDress = WillDressOn(unit, target);

            bool stale = !unit.IsMarching ||
                         shouldDress != unit.DressingBearing.HasValue ||
                         (tick % RepathIntervalTicks == 0 &&
                          Vec2.Distance(unit.Route!.Destination, target.Position) > RepathThresholdMetres);

            if (stale)
                ChaseToward(battle, unit, target, tick, log, "attacking");
        }

        /// <summary>Plans a march that stops just short of a target.</summary>
        private bool ChaseToward(BattleState battle, UnitInstance unit, UnitInstance quarry, int tick, IBattleLog log, string verb)
        {
            Vec2 approach = (unit.Position - quarry.Position).Normalised();
            if (approach.IsNearZero) approach = unit.Facing.Opposite().ToVector();

            Vec2 want;

            if (ShootsRatherThanCharges(unit))
            {
                // Bowmen and guns stop where their reach begins. Measured centre
                // to centre because that is what the shooting rule measures, so
                // arriving here means the volleys land.
                want = quarry.Position + approach * ShootingReach(unit);
                unit.DressingBearing = null;
            }
            else if (OrientedRect.Within(unit.Shape, quarry.Shape, DressingRangeMetres))
            {
                want = DressingSlot(battle, unit, quarry, out Facing square);
                unit.DressingBearing = square;
            }
            else
            {
                // How much of each formation lies between the two centres along
                // the line of approach.
                //
                // Adding half-DEPTHS was right only for a head-on meeting. A
                // regiment coming at a line from the side has to cross half its
                // frontage — fifty-three metres for cavalry, not four — so the
                // aim point landed deep inside the enemy formation, at a place
                // the unit could never stand. It marched at that point, never
                // arrived, and re-planned the same impossible route every few
                // ticks. From the player's chair that reads as regiments
                // refusing to engage unless aimed dead at each other's centres.
                float standOff = quarry.Shape.ProjectedRadius(approach)
                               + unit.Shape.ProjectedRadius(approach);

                // Then aim slightly inside contact rather than exactly at its
                // edge, so arriving means fighting rather than stopping a metre
                // short of a fight and waiting to be told again.
                standOff -= ContactMetres * 0.5f;

                want = quarry.Position + approach * standOff;
                unit.DressingBearing = null;
            }

            Vec2 aim = NearestReachable(battle, unit, want, unit.Position);

            PathResult path = Marching.PlanTo(battle, unit, _pathfinder, aim);

            if (!path.Found || path.Waypoints.Count < 2)
            {
                log.Blocked("Order",
                    $"{unit.Def.DisplayName} cannot reach {quarry.Def.DisplayName}: {path.FailureDetail}",
                    unit.Id);

                return false;
            }

            unit.Route = new MovementRoute(path.Waypoints, unit.Order.WheelFirst);

            log.Decision("Order",
                $"{unit.Def.DisplayName} is {verb} {quarry.Def.DisplayName}.",
                unit.Id);

            return true;
        }

        // ---- Helpers ---------------------------------------------------------

        /// <summary>How close two formations must come before their men can reach each other.</summary>
        /// <remarks>
        /// Halved along with the rectangle. Reach is a question of how many
        /// ranks separate the front two, and ranks now stand half as far apart
        /// in metres — so at the old figure two regiments six metres deep would
        /// have fought across a gap wider than either of them was thick, which
        /// looks exactly as wrong as it sounds.
        /// </remarks>
        public const float ContactMetres = 4f;

        // ---- Squaring up -----------------------------------------------------

        /// <summary>
        /// How near a regiment comes before it starts dressing its line onto the
        /// enemy, in metres.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Far enough out that there is room to come round and sidestep before
        /// the two bodies meet, near enough that the manoeuvre is committed
        /// rather than speculative. A hundred metres is about a minute for
        /// infantry and twenty seconds for horse.
        /// </para>
        /// <para>
        /// Deliberately not the whole march. Aiming at the aligned slot from the
        /// far side of the field would have a hundred-metre line crabbing
        /// sideways across the entire battlefield at a fifth of its pace to
        /// arrive on the correct axis, which is both slower and stupider than
        /// walking straight at the enemy and tidying up at the end.
        /// </para>
        /// </remarks>
        private const float DressingRangeMetres = 100f;

        /// <summary>
        /// Where a regiment must stand to meet its target squarely, and the
        /// front it must hold to do it.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Two equal regiments should end up rectangle on rectangle, centre
        /// opposite centre. So the slot sits on the normal through the target's
        /// centre, off whichever of its four faces this unit is currently
        /// nearest — attack a line from in front and you draw up in front of it;
        /// get round to its side first and you draw up against its depth, which
        /// is what a flank attack is.
        /// </para>
        /// <para>
        /// That makes flanking something you manoeuvre for rather than something
        /// the approach angle hands you by accident. It also means the choice of
        /// face is self-reinforcing: marching toward the chosen slot increases
        /// this unit's offset along that face's normal, so a regiment starting
        /// near the 45° boundary commits rather than dithering between two.
        /// </para>
        /// <para>
        /// Each attacker chooses its own face, from where it was standing when
        /// the order was given — see <see cref="ChooseFace"/>. Regiments sent
        /// from the same quarter therefore agree on a face without being made
        /// to and stand beside each other along it, while one deliberately sent
        /// round the flank keeps its own and is not dragged back into line.
        /// </para>
        /// <para>
        /// Those sharing a face divide it: see
        /// <see cref="PlaceInTheAttackingLine"/> for how many fit and where the
        /// rest wait.
        /// </para>
        /// </remarks>
        private static Vec2 DressingSlot(
            BattleState battle, UnitInstance unit, UnitInstance quarry, out Facing square)
        {
            OrientedRect theirs = quarry.Shape;

            Vec2 outward = ChooseFace(battle, unit, quarry);

            square = Facing.FromVector(-outward);

            // Half of each formation lies between the two centres along that
            // normal: theirs as it stands, and ours as it will stand once it has
            // come round — which is its depth, not its frontage.
            var dressed = new OrientedRect(unit.Position, square, unit.Footprint);

            float standOff = theirs.ProjectedRadius(outward)
                           + dressed.ProjectedRadius(outward)
                           - ContactMetres * 0.5f;

            Vec2 alongTheFace = new Vec2(-outward.Y, outward.X);

            float along = PlaceInTheAttackingLine(battle, unit, quarry, out int rank);

            // Ranks past the first are the reserve. They form up in the
            // attacking line's own rear and wait for a place rather than
            // shoving into one that does not exist.
            standOff += rank * (unit.Footprint.Depth + ReserveRankGapMetres);

            return quarry.Position
                 + outward * standOff
                 + alongTheFace * along;
        }

        /// <summary>
        /// How far a regiment must have got round an enemy before it stops
        /// squaring up to them and goes for the flank instead.
        /// </summary>
        /// <remarks>
        /// A regiment meets an enemy one of two ways: front to front, or square
        /// on to its flank. There is no third arrangement worth having, because
        /// anything between the two presents a corner to a corner and neither
        /// side can bring its numbers to bear.
        /// <para>
        /// Frontal is the default and this decides how far off the front you
        /// must already be to earn the other. At two, a regiment has to be twice
        /// as far round the side as it is in front — past sixty degrees off
        /// their bearing — before it goes for the flank. Anything less committed
        /// than that squares up, which stops a charge aimed at the front from
        /// drifting round the corner because it happened to set off at an angle.
        /// </para>
        /// <para>
        /// Both choices hold once made: marching toward a frontal slot reduces
        /// how far round the side you are, and marching toward a flanking slot
        /// reduces how far in front. So a regiment commits rather than dithering
        /// on the boundary.
        /// </para>
        /// </remarks>
        private const float FlankingBias = 2f;

        /// <summary>
        /// The outward normal of the enemy face this regiment will form on.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Judged from where <i>this</i> regiment was standing when it was sent,
        /// and from nowhere else. Two other versions were tried and both were
        /// wrong in instructive ways.
        /// </para>
        /// <para>
        /// Read from a regiment's live position, the choice feeds back on
        /// itself: an attacker takes its slot beside the enemy's front, which
        /// leaves it a few metres in front and twenty to one side, and on the
        /// next re-plan that reads exactly like standing off the flank — so the
        /// attack hauls itself round the corner one re-plan at a time.
        /// </para>
        /// <para>
        /// Averaged over every attacker, it stops feeding back but starts
        /// outvoting people. A regiment deliberately sent round the flank while
        /// another engaged the front was dragged to the front by its partner:
        /// alone it took the flank at ninety degrees, and in company it came in
        /// at a hundred and eighty. That destroyed the hammer and anvil, which
        /// is most of the reason to have two regiments in the first place.
        /// </para>
        /// <para>
        /// Where <i>you</i> set off from cannot move and cannot be outvoted.
        /// Regiments sent from the same side reach the same answer on their own
        /// and share a face without being made to; one sent from round the
        /// flank keeps its own. It also hands the player the choice without
        /// needing a control for it — the face follows from where a regiment
        /// stood when the order was given.
        /// </para>
        /// <para>
        /// Deliberately still read against the enemy's <i>current</i> bearing.
        /// A regiment that turns to receive an attack coming at its flank
        /// converts it into a frontal one, which is precisely what turning to
        /// receive it is for — and holding the face fixed instead had attackers
        /// grinding away at a flank the defender no longer presented, breaking
        /// regiments that should have held.
        /// </para>
        /// </remarks>
        private static Vec2 ChooseFace(BattleState battle, UnitInstance unit, UnitInstance quarry) =>
            FaceFrom(quarry, unit.OrderAnchor);

        /// <summary>
        /// The outward normal of the face a regiment standing at
        /// <paramref name="from"/> would form on.
        /// </summary>
        private static Vec2 FaceFrom(UnitInstance quarry, Vec2 from)
        {
            OrientedRect theirs = quarry.Shape;
            Vec2 offset = from - quarry.Position;

            float offTheFront = Vec2.Dot(offset, theirs.Forward);
            float offTheFlank = Vec2.Dot(offset, theirs.Right);

            // Signed separately from the comparison, so a body sitting exactly
            // on one of the axes still gets a definite answer.
            return MathF.Abs(offTheFlank) > MathF.Abs(offTheFront) * FlankingBias
                ? theirs.Right * (offTheFlank >= 0f ? 1f : -1f)
                : theirs.Forward * (offTheFront >= 0f ? 1f : -1f);
        }

        /// <summary>
        /// Whether a regiment counts as one of the ones going for this target.
        /// </summary>
        /// <remarks>
        /// <paramref name="asking"/> is always included whether or not its order
        /// has been written yet. A unit closing under an aggressive stance has
        /// not been told to attack anybody by name, but it is plainly one of the
        /// regiments about to arrive.
        /// </remarks>
        private static bool IsGoingFor(UnitInstance other, UnitInstance asking, UnitInstance quarry)
        {
            if (other.Owner != asking.Owner) return false;
            if (!other.IsFighting) return false;
            if (other.Id == asking.Id) return true;

            return other.Order.Kind == OrderKind.Attack && other.Order.Target == quarry.Id;
        }

        /// <summary>Elbow room left between two regiments drawn up side by side.</summary>
        private const float ShoulderRoomMetres = 4f;

        /// <summary>The most regiments that may press one face of an enemy at once.</summary>
        /// <remarks>
        /// <para>
        /// Two, and two is not double: a face is worth one full frontage of
        /// fighting however many regiments are pushed onto it, because damage is
        /// dealt across the ground two bodies genuinely share and two attackers
        /// divide that ground between them. So a pair on the front deal together
        /// what one would deal alone, each taking half — and buy the defender's
        /// nerve rather than his blood, which is what concentrating force is
        /// actually for.
        /// </para>
        /// <para>
        /// Four faces at two apiece is therefore six or eight regiments landing
        /// four frontages of damage. What it is <b>not</b> is a third regiment
        /// added to a face that already holds two, which was measured doing
        /// worse than nothing: three attackers were given thirteen metres of
        /// slot each on a forty-metre front, every one of them needed forty, and
        /// the outer two shoved each other to six metres short of contact and
        /// stood there for the whole battle. Three killed 326 where one alone
        /// killed 325.
        /// </para>
        /// </remarks>
        public const int MostOnOneFace = 2;

        /// <summary>
        /// The least share of its own frontage a regiment will take a place in
        /// the line for.
        /// </summary>
        /// <remarks>
        /// <para>
        /// What stops the cap of two being applied to a face too narrow to hold
        /// two. A regiment's front and rear are its full forty metres and hold a
        /// pair; its flanks are six metres of end-on file, and a second regiment
        /// sent at one would be standing in mid-air beside the first, touching
        /// nothing. One flanker already fights across the enemy's whole width —
        /// there is no formed line to hold it off, so it folds round — which is
        /// exactly the full frontage that face is worth.
        /// </para>
        /// <para>
        /// Measured against the ground both regiments covered when they were
        /// mustered, never against what is left of them. A live frontage
        /// narrows as men are killed, and a yes-or-no answer fed by a sliding
        /// number crosses its own threshold sooner or later: the first version
        /// tipped from two to one at the defender's first casualty, and raising
        /// the margin only moved the cliff to about seventy per cent — an
        /// ordinary mid-fight regiment, at which point one of the two attackers
        /// already in contact was recomputed into the reserve and walked
        /// backwards out of a fight it was winning.
        /// </para>
        /// <para>
        /// A face that held two at the start holds two at the end.
        /// </para>
        /// </remarks>
        private const float MinimumUsefulShare = 0.35f;

        /// <summary>Ground left between the attacking line and the rank behind it.</summary>
        private const float ReserveRankGapMetres = 6f;

        /// <summary>
        /// How many regiments can usefully stand against one face of an enemy.
        /// </summary>
        /// <remarks>
        /// Two abreast take half the face each, so they are only worth placing
        /// if that half still engages a fair share of a regiment's own frontage.
        /// </remarks>
        private static int FaceCapacity(UnitInstance unit, UnitInstance quarry, Vec2 alongTheFace)
        {
            float faceWidth = quarry.Shape.ProjectedRadius(alongTheFace) * 2f;
            float eachWouldGet = faceWidth / MostOnOneFace;

            return eachWouldGet >= unit.Footprint.Width * MinimumUsefulShare
                ? MostOnOneFace
                : 1;
        }

        /// <summary>
        /// How far along the enemy's face this regiment stands, and how many
        /// ranks back it waits if the face is already full.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Attackers on the same face queue in the order they already stand
        /// along it, so every regiment works out the same arrangement without
        /// being told. The first <see cref="MostOnOneFace"/> take the face
        /// shoulder to shoulder, centred as a body on the target; the rest form
        /// up behind them.
        /// </para>
        /// <para>
        /// Queuing by id instead looks tidier and is wrong. It hands the
        /// left-hand slot to whichever regiment was raised first rather than to
        /// the one already on the left, so three regiments abreast cross each
        /// other on the way in, shove, and arrive as one — measured at a single
        /// regiment in contact where two had managed it. Men take the place
        /// nearest them.
        /// </para>
        /// <para>
        /// The queue is rebuilt on every re-plan out of the regiments still
        /// fighting, so a reserve steps into the line of its own accord the
        /// moment the regiment in front of it breaks, dies or is called away.
        /// Nothing has to notice the vacancy and hand it on.
        /// </para>
        /// <para>
        /// A regiment is never moved to a different face to find room. The face
        /// follows from where it was standing when the order was given and from
        /// nowhere else, which is what hands the player the choice without a
        /// control for it — and what stops the game marching a regiment across a
        /// formed enemy's front to reach the far side, which the tests show gets
        /// it cut up and broken. Going round is an order somebody gives.
        /// </para>
        /// </remarks>
        private static float PlaceInTheAttackingLine(
            BattleState battle, UnitInstance unit, UnitInstance quarry, out int rank)
        {
            Vec2 ourFace = ChooseFace(battle, unit, quarry);
            Vec2 alongTheFace = new Vec2(-ourFace.Y, ourFace.X);

            int capacity = FaceCapacity(unit, quarry, alongTheFace);

            float ourStation = Vec2.Dot(unit.OrderAnchor - quarry.Position, alongTheFace);

            int aheadOfUs = 0;
            int allOfUs = 0;

            foreach (UnitInstance other in battle.UnitsOnField())
            {
                if (!IsGoingFor(other, unit, quarry)) continue;

                // Only the regiments coming in on the same face share a line
                // with us. One sent round the flank is fighting its own battle
                // over there and must not be given a slot in this one, or the
                // frontal line is spaced out around a gap left for somebody who
                // is never going to fill it.
                if (Vec2.Dot(FaceFrom(quarry, other.OrderAnchor), ourFace) < 0.9f) continue;

                allOfUs++;

                if (other.Id == unit.Id) continue;

                float theirStation = Vec2.Dot(other.OrderAnchor - quarry.Position, alongTheFace);

                // Id breaks the tie, so two regiments ordered from exactly the
                // same spot still reach one answer between them.
                bool theirsFirst = theirStation != ourStation
                    ? theirStation < ourStation
                    : other.Id.Value < unit.Id.Value;

                if (theirsFirst) aheadOfUs++;
            }

            rank = aheadOfUs / capacity;

            int place = aheadOfUs % capacity;
            int besideUs = Math.Min(capacity, allOfUs - rank * capacity);

            float berth = unit.Footprint.Width + ShoulderRoomMetres;

            // The middle of our own stretch, against the middle of our rank.
            // Alone, this is exactly zero.
            return (place - (besideUs - 1) * 0.5f) * berth;
        }

        /// <summary>Whether this unit would be dressing on that one if it re-planned now.</summary>
        private static bool WillDressOn(UnitInstance unit, UnitInstance quarry) =>
            !ShootsRatherThanCharges(unit) &&
            OrientedRect.Within(unit.Shape, quarry.Shape, DressingRangeMetres);

        // ---- Shooting rather than closing ------------------------------------

        /// <summary>
        /// How much of its reach a shooter closes to before it stops, as a
        /// fraction.
        /// </summary>
        /// <remarks>
        /// Not the whole of it. A volley at the very edge of the range does half
        /// damage and is the least accurate shot the unit has, and stopping
        /// exactly at the limit means any drift by either side breaks the
        /// engagement off. Coming a little inside costs a few seconds of being
        /// shot at and buys a shot worth firing.
        /// </remarks>
        private const float ShootingStandoffFraction = 0.85f;

        /// <summary>
        /// Whether this unit's answer to an enemy is to shoot it rather than
        /// close with it.
        /// </summary>
        /// <remarks>
        /// Judged on what the unit is armed for rather than on its name, so a
        /// new missile unit inherits the behaviour without anything being told
        /// about it.
        /// </remarks>
        public static bool ShootsRatherThanCharges(UnitInstance unit) =>
            unit.Def.Get(UnitAttributes.Range) > 0f &&
            unit.Def.Get(UnitAttributes.RangedAttack) > unit.Def.Get(UnitAttributes.Attack);

        /// <summary>How far out a shooter means to stand from what it is shooting, in metres.</summary>
        private static float ShootingReach(UnitInstance unit) =>
            unit.Def.Get(UnitAttributes.Range) * ShootingStandoffFraction;

        /// <summary>
        /// Whether a shooter is already near enough that its volleys land, and
        /// so has nothing left to do about its orders.
        /// </summary>
        /// <remarks>
        /// Anything closer counts too. A regiment charged by cavalry is well
        /// inside its own reach and should stand and shoot, not back away to a
        /// tidier distance — withdrawing from a threat is what the Evade stance
        /// is for, and it should stay a decision somebody made.
        /// </remarks>
        private static bool InShootingPosition(UnitInstance unit, UnitInstance quarry) =>
            Vec2.Distance(unit.Position, quarry.Position) <= ShootingReach(unit);

        // ---- Finding somewhere to actually stand ------------------------------

        /// <summary>
        /// How much of a formation may lie inside another before it counts as
        /// standing in the same field rather than merely brushing past.
        /// </summary>
        /// <remarks>
        /// An army drawn up in line touches constantly — corners clip, flanks
        /// graze, and treating every one of those as a collision is what makes a
        /// line impossible to form. A twentieth of a regiment is a brush.
        /// </remarks>
        public const float GrazingTolerance = 0.05f;

        /// <summary>How far apart the rings of the placement search are, in metres.</summary>
        private const float PlacementRingMetres = 25f;

        /// <summary>How many rings out it will look before giving up.</summary>
        private const int PlacementRings = 6;

        /// <summary>How many bearings are tried on each ring.</summary>
        private const int PlacementBearings = 8;

        /// <summary>
        /// The best ground near an ordered point that this regiment could
        /// actually stand on.
        /// </summary>
        /// <remarks>
        /// <para>
        /// An order names a point; a regiment needs a <i>placement</i>. Those are
        /// not the same thing once a body of men is a rectangle rather than a
        /// token — the point may be inside a wood, inside a friendly regiment,
        /// or half a frontage from the edge of the world, and in every one of
        /// those cases the honest answer is "as near as I can decently get"
        /// rather than marching at it forever.
        /// </para>
        /// <para>
        /// Searched in rings outward from the ordered point, so the nearest
        /// acceptable ground always wins and the score only decides between
        /// candidates that are equally close. Rings and bearings are walked in a
        /// fixed order, which is what makes the answer reproducible from the
        /// seed.
        /// </para>
        /// </remarks>
        public static bool TryFindPlacement(
            BattleState battle, UnitInstance unit, Vec2 want, Facing front, out Vec2 placement)
        {
            placement = want;

            float best = float.MaxValue;
            bool found = false;

            for (int ring = 0; ring <= PlacementRings; ring++)
            {
                float radius = ring * PlacementRingMetres;
                int bearings = ring == 0 ? 1 : PlacementBearings;

                for (int i = 0; i < bearings; i++)
                {
                    Facing outward = Facing.FromRadians(2f * MathF.PI * i / bearings);
                    Vec2 candidate = want + outward.ToVector() * radius;

                    if (!CanStandThere(battle, unit, candidate, front)) continue;

                    // Distance from the order is a cost like any other rather
                    // than a gate. Gating on it — taking the first ring with
                    // anything acceptable on it — reads well but means the
                    // preferences below can never actually move a regiment: the
                    // ordered point itself always wins if it is merely legal,
                    // however bad it is to stand on.
                    float score = radius
                                + ScorePlacement(battle, unit, candidate, front)
                                + Vec2.Distance(candidate, unit.Position) * WalkingIsCheap;

                    if (score >= best) continue;

                    best = score;
                    placement = candidate;
                    found = true;
                }
            }

            return found;
        }

        /// <summary>
        /// What a metre of extra walking is worth against the other preferences.
        /// </summary>
        /// <remarks>
        /// Small, and it exists to break ties rather than to decide anything.
        /// Every candidate on a ring sits the same distance from the order, so
        /// without this the choice between them falls to whichever bearing
        /// happened to be tried first — which put regiments on the far side of
        /// the very thing they were going round.
        /// </remarks>
        private const float WalkingIsCheap = 0.1f;

        /// <summary>Whether a regiment could legally stand at a placement at all.</summary>
        private static bool CanStandThere(BattleState battle, UnitInstance unit, Vec2 where, Facing front)
        {
            if (!battle.FormationFits(unit, where, front)) return false;

            var shape = new OrientedRect(where, front, unit.Footprint);

            foreach (UnitInstance other in battle.UnitsOnField())
            {
                if (other.Id == unit.Id) continue;
                if (!other.IsFighting) continue;

                if (OrientedRect.OverlapFraction(shape, other.Shape) > GrazingTolerance)
                    return false;
            }

            return true;
        }

        /// <summary>How much this regiment would rather not stand somewhere. Lower is better.</summary>
        /// <remarks>
        /// Only ever compares placements at the same distance from the ordered
        /// point, so every term here is a tie-break: bad ground, ground an enemy
        /// commands, and ground that would need the front swung round to reach.
        /// </remarks>
        private static float ScorePlacement(BattleState battle, UnitInstance unit, Vec2 where, Facing front)
        {
            float score = 0f;

            // Standing in a swamp is worse than standing beside one.
            score += battle.TerrainAt(where).Get(TerrainAttributes.Disorder) * 100f;

            // Parking inside a spear wall's reach is not a reposition, it is an
            // attack nobody ordered. Only for marches — an attack means to be
            // there, and that is the entire point of it.
            if (unit.Order.Kind != OrderKind.Attack)
            {
                var shape = new OrientedRect(where, front, unit.Footprint);

                foreach (UnitInstance enemy in battle.UnitsOnField())
                {
                    if (enemy.Owner == unit.Owner) continue;
                    if (!enemy.IsFighting) continue;

                    if (OrientedRect.Within(shape, enemy.Shape, enemy.ZoneOfControl))
                        score += 40f;
                }
            }

            return score;
        }

        /// <summary>Step size when hunting back from an unreachable goal, in metres.</summary>
        private const float ReachableProbeStep = 12f;

        /// <summary>
        /// The furthest point along the way to <paramref name="want"/> that this
        /// unit could actually stand on.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Clamping a goal to the map bounds puts it exactly on the boundary,
        /// and the boundary is where the impassable ground lives — the valley
        /// map is ringed with mountains and has deep water along one side. So
        /// any order that reached for the edge produced a goal no route could
        /// ever end at, the pathfinder correctly refused it, and the regiment
        /// stood still. In the corner, where two impassable edges meet, nothing
        /// could get out again.
        /// </para>
        /// <para>
        /// Walks back toward <paramref name="from"/> until the ground is
        /// passable to this unit. Falls back to where the unit already stands,
        /// which is by definition somewhere it can be.
        /// </para>
        /// </remarks>
        public static Vec2 NearestReachable(BattleState battle, UnitInstance unit, Vec2 want, Vec2 from)
        {
            Vec2 goal = battle.Terrain.Bounds.Clamp(want);

            if (CanStandAt(battle, unit, goal)) return goal;

            Vec2 back = from - goal;
            float distance = back.Length;

            if (distance < 0.01f) return from;

            Vec2 step = back / distance * ReachableProbeStep;
            int probes = (int)(distance / ReachableProbeStep);

            for (int i = 1; i <= probes; i++)
            {
                Vec2 probe = goal + step * i;

                if (CanStandAt(battle, unit, probe)) return probe;
            }

            return from;
        }

        private static bool CanStandAt(BattleState battle, UnitInstance unit, Vec2 point) =>
            battle.Terrain.Bounds.Contains(point) &&
            battle.Movement.SpeedMultiplier(battle.Terrain.At(point), unit.Def.Movement) > 0f;

        /// <summary>Whether two units are close enough to be considered in contact.</summary>
        /// <remarks>
        /// Measured between the two formations, not between their centres.
        /// Adding half-depths and comparing centres assumed the regiments were
        /// standing nose to nose, which is the one arrangement where it happens
        /// to be right. A body of cavalry is a hundred and six metres wide and
        /// eight deep, so that sum let it count as fighting only within sixteen
        /// metres — and two such regiments could slide past one another twenty
        /// metres apart, formations fully interpenetrated, with the rules
        /// insisting they had never met.
        /// </remarks>
        public static bool InContactWith(UnitInstance unit, UnitInstance other) =>
            OrientedRect.Within(unit.Shape, other.Shape, ContactMetres);

        /// <summary>
        /// The nearest enemy close enough to be worth running from.
        /// </summary>
        /// <remarks>
        /// Judged against whichever reach is longer — the evader's or the
        /// threat's. A scout with a 20 m zone of control that only fled when
        /// cavalry came within 20 m would already be dead; what matters is how
        /// far the thing chasing it reaches.
        /// </remarks>
        /// <summary>
        /// The nearest enemy close enough to be worth withdrawing from, judged
        /// by how quickly it could arrive rather than by zone of control.
        /// </summary>
        private static UnitInstance? NearestClosingThreat(BattleState battle, UnitInstance unit)
        {
            UnitInstance? nearest = null;
            float bestSquared = float.MaxValue;

            foreach (UnitInstance other in battle.UnitsOnField())
            {
                if (other.Owner == unit.Owner) continue;
                if (!other.IsFighting) continue;

                float alarm = MathF.Max(unit.ZoneOfControl, other.ZoneOfControl)
                              + other.BaseSpeed * ReactionSeconds;

                // Between the formations, because that is what a zone of
                // control now means. Comparing a belt width against a
                // centre-to-centre distance is comparing two different
                // quantities: a hundred-metre line would have to be sitting on
                // top of this unit before the numbers agreed anything was near.
                float gap = OrientedRect.GapBetween(unit.Shape, other.Shape);
                if (gap > alarm) continue;

                if (gap < bestSquared)
                {
                    bestSquared = gap;
                    nearest = other;
                }
            }

            return nearest;
        }

        private static UnitInstance? NearestThreat(BattleState battle, UnitInstance unit, float reachFactor, bool includeRouting = false)
        {
            UnitInstance? nearest = null;
            float bestSquared = float.MaxValue;

            foreach (UnitInstance other in battle.UnitsOnField())
            {
                if (other.Owner == unit.Owner) continue;
                if (!other.IsFighting && !(includeRouting && other.State == UnitState.Routing)) continue;

                float reach = MathF.Max(unit.ZoneOfControl, other.ZoneOfControl) * reachFactor;
                float gap = OrientedRect.GapBetween(unit.Shape, other.Shape);

                if (gap > reach) continue;

                if (gap < bestSquared)
                {
                    bestSquared = gap;
                    nearest = other;
                }
            }

            return nearest;
        }

        /// <summary>Nearest living enemy within a radius, in unit-id order for ties.</summary>
        private static UnitInstance? NearestEnemyWithin(BattleState battle, UnitInstance unit, float radius)
        {
            UnitInstance? nearest = null;
            float bestSquared = radius * radius;

            foreach (UnitInstance other in battle.UnitsOnField())
            {
                if (other.Owner == unit.Owner) continue;
                if (!other.IsFighting) continue;

                float squared = Vec2.DistanceSquared(unit.Position, other.Position);

                if (squared < bestSquared)
                {
                    bestSquared = squared;
                    nearest = other;
                }
            }

            return nearest;
        }
    }
}
