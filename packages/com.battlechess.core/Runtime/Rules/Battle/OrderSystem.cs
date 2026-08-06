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

                    case Stance.Aggressive:
                        if (TryEngageNearby(battle, unit, tick, log)) continue;
                        break;
                }

                if (unit.Order.Kind == OrderKind.Attack)
                    FollowTarget(battle, unit, tick, log);
            }
        }

        /// <summary>
        /// Forgets a hold-up once the enemy responsible is gone or out of range,
        /// so the unit can be given fresh orders.
        /// </summary>
        private static void ReleaseIfClear(BattleState battle, UnitInstance unit)
        {
            if (!unit.HeldUpBy.IsValid) return;

            UnitInstance blocker = battle.Get(unit.HeldUpBy);

            if (!blocker.IsFighting ||
                Vec2.Distance(unit.Position, blocker.Position) > blocker.ZoneOfControl * 1.2f)
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

            Vec2 retreat = battle.Terrain.Bounds.Clamp(unit.Position + away * RetreatDistanceMetres);

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
            // Aim for contact rather than the enemy's centre, or the route ends
            // inside them and the unit spends the fight trying to reach a point
            // it can never stand on.
            float standOff = unit.Footprint.HalfDepth + quarry.Footprint.HalfDepth;
            Vec2 approach = (unit.Position - quarry.Position).Normalised();
            if (approach.IsNearZero) approach = unit.Facing.Opposite().ToVector();

            Vec2 aim = battle.Terrain.Bounds.Clamp(quarry.Position + approach * standOff);

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

        /// <summary>Whether two units are close enough to be considered in contact.</summary>
        public static bool InContactWith(UnitInstance unit, UnitInstance other)
        {
            float contact = unit.Footprint.HalfDepth + other.Footprint.HalfDepth + 8f;
            return Vec2.DistanceSquared(unit.Position, other.Position) <= contact * contact;
        }

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

                float squared = Vec2.DistanceSquared(unit.Position, other.Position);
                if (squared > alarm * alarm) continue;

                if (squared < bestSquared)
                {
                    bestSquared = squared;
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
                float squared = Vec2.DistanceSquared(unit.Position, other.Position);

                if (squared > reach * reach) continue;

                if (squared < bestSquared)
                {
                    bestSquared = squared;
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
