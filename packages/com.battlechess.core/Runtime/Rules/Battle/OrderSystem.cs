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

            // Never a shooter. Their answer to something in the way is to shoot
            // it, and sending them at it instead was ruinous — a regiment of
            // archers ordered to march was charging spear walls it could not
            // scratch, losing seven men a pulse while dealing literally none,
            // until it broke. Judged on what the unit is armed for rather than
            // on its name, so a new missile unit inherits it for free.
            if (unit.Def.Get(UnitAttributes.RangedAttack) > unit.Def.Get(UnitAttributes.Attack))
                return false;

            UnitInstance blocker = battle.Get(unit.HeldUpBy);
            if (!blocker.IsFighting) return false;

            // Clears the hold-up as a side effect, which is what lets contact
            // stop treating this as a march past and start treating it as an
            // attack pressed home.
            unit.GiveOrder(UnitOrder.Attack(blocker.Id), unit.Position);

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
                if (tick % 20 == 0)
                    log.Blocked("Order",
                        $"{unit.Def.DisplayName} is trying to evade {threat.Def.DisplayName} but has nowhere to go.",
                        unit.Id);

                return true;
            }

            unit.Route = new MovementRoute(path.Waypoints, wheelFirst: false);
            unit.HeldUpBy = UnitId.None;

            if (tick % 20 == 0)
                log.Decision("Order",
                    $"{unit.Def.DisplayName} is evading {threat.Def.DisplayName} at " +
                    $"{Vec2.Distance(unit.Position, threat.Position):0} m.",
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
                if (tick % 30 == 0)
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

            bool stale = !unit.IsMarching ||
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

            // How much of each formation lies between the two centres along the
            // line of approach.
            //
            // Adding half-DEPTHS was right only for a head-on meeting. A
            // regiment coming at a line from the side has to cross half its
            // frontage — fifty-three metres for cavalry, not four — so the aim
            // point landed deep inside the enemy formation, at a place the unit
            // could never stand. It marched at that point, never arrived, and
            // re-planned the same impossible route every few ticks. From the
            // player's chair that reads as regiments refusing to engage unless
            // aimed dead at each other's centres.
            float standOff = quarry.Shape.ProjectedRadius(approach)
                           + unit.Shape.ProjectedRadius(approach);

            // Then aim slightly inside contact rather than exactly at its edge,
            // so arriving means fighting rather than stopping a metre short of
            // a fight and waiting to be told again.
            standOff -= ContactMetres * 0.5f;

            Vec2 aim = NearestReachable(battle, unit, quarry.Position + approach * standOff, unit.Position);

            PathResult path = _pathfinder.FindPath(unit.Position, aim, unit.Def.Movement);

            if (!path.Found || path.Waypoints.Count < 2)
            {
                if (tick % 30 == 0)
                    log.Blocked("Order",
                        $"{unit.Def.DisplayName} cannot reach {quarry.Def.DisplayName}: {path.FailureDetail}",
                        unit.Id);

                return false;
            }

            unit.Route = new MovementRoute(path.Waypoints, unit.Order.WheelFirst);

            if (tick % 20 == 0)
                log.Decision("Order",
                    $"{unit.Def.DisplayName} is {verb} {quarry.Def.DisplayName} at " +
                    $"{Vec2.Distance(unit.Position, quarry.Position):0} m.",
                    unit.Id);

            return true;
        }

        // ---- Helpers ---------------------------------------------------------

        /// <summary>How close two formations must come before their men can reach each other.</summary>
        public const float ContactMetres = 8f;

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
