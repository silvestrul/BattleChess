using System;
using BattleChess.Contracts;

namespace BattleChess.Rules
{
    /// <summary>
    /// Decides what happens when units come close: who is halted, who forces a
    /// way through, and what it costs them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Nothing physically collides.</b> Units remain points at their centre,
    /// as movement and pathfinding already assume. What creates a front line is
    /// not shapes touching but ground being <i>controlled</i> — a march halts on
    /// entering an enemy's zone of control, well before the formations would
    /// overlap. That keeps one model throughout instead of a physics layer
    /// fighting a routing layer, and it is how most wargames actually work.
    /// </para>
    /// <para>
    /// Control is not absolute. A unit whose breakthrough exceeds the enemy's
    /// stopping power rides straight through — cavalry through swordsmen, never
    /// through a spear wall — at the cost of disorder to both. Passing through
    /// friends costs less, but is not free either, so stacking an army on one
    /// spot quietly ruins it.
    /// </para>
    /// <para>
    /// Runs before movement, so a halt takes effect on the tick it is decided.
    /// </para>
    /// </remarks>
    public sealed class ContactSystem : IBattleSystem
    {
        /// <summary>Organization lost per tick while riding through an enemy formation.</summary>
        private const float EnemyBreakthroughDisorderPerTick = 0.02f;

        /// <summary>Organization lost per tick while pushing through a friendly formation.</summary>
        /// <remarks>
        /// Charged only while somebody is actually moving. The cost is for
        /// <i>passing through</i> a formation, not for standing near one — units
        /// drawn up shoulder to shoulder in a line overlap constantly, and
        /// draining them for it reduced a stationary regiment to nothing in two
        /// turns of doing absolutely nothing.
        /// </remarks>
        private const float FriendlyOverlapDisorderPerTick = 0.004f;

        public string Name => "Contact";

        public void Step(BattleState battle, int tick, IBattleLog log)
        {
            if (battle == null) throw new ArgumentNullException(nameof(battle));

            // Ascending unit id throughout, so the same seed resolves contacts
            // in the same order every run.
            foreach (UnitInstance unit in battle.UnitsOnField())
            {
                if (!unit.IsFighting) continue;

                ApplyOverlapDisorder(battle, unit, log);

                if (unit.IsMarching)
                    ApplyZoneOfControl(battle, unit, tick, log);
            }
        }

        /// <summary>
        /// Halts a march that has entered an enemy's zone of control, unless the
        /// unit can force its way through.
        /// </summary>
        private static void ApplyZoneOfControl(BattleState battle, UnitInstance unit, int tick, IBattleLog log)
        {
            // Formation and cohesion both count. A spear wall stops cavalry
            // because it is braced and holding together; the same spearmen in
            // loose order, or shaken, are ridden straight through.
            float breakthrough = unit.EffectiveBreakthrough;

            foreach (UnitInstance enemy in battle.UnitsOnField())
            {
                if (enemy.Owner == unit.Owner) continue;
                if (!enemy.IsFighting) continue;

                float reach = enemy.ZoneOfControl;
                if (Vec2.DistanceSquared(unit.Position, enemy.Position) > reach * reach) continue;

                // A unit already inside the zone may still back out of it. Zone
                // of control stops an advance into or through controlled ground;
                // it is not a cage. Without this a unit halted at a spear wall
                // could never be moved again — every new order was cancelled on
                // the tick it was given, whichever way it pointed.
                if (IsWithdrawingFrom(unit, enemy)) continue;

                // Nor does it stop an attack being pressed home against the very
                // unit exerting it. Zone of control reaches 30 m and melee needs
                // 13 m, so halting here would leave two regiments staring at
                // each other from outside sword's length, permanently unable to
                // fight. It prevents marching past an enemy, not closing with one.
                if (unit.Order.Kind == OrderKind.Attack && unit.Order.Target == enemy.Id) continue;

                float stopping = enemy.EffectiveStoppingPower;

                // A cautious unit halts on contact whether or not it could force
                // through. This is the whole difference between Defend and
                // Advance: the same regiment, the same enemy, and a different
                // answer because of what it was told to do about surprises.
                if (unit.Stance == Stance.Defend)
                {
                    // Only say so on the tick it happens. A halt persists for as
                    // long as the enemy stands there, and repeating it every
                    // second would bury everything else.
                    if (unit.HeldUpBy != enemy.Id)
                        log.Blocked("Contact",
                            $"{unit.Def.DisplayName} halts on contact with {enemy.Def.DisplayName} at " +
                            $"{Vec2.Distance(unit.Position, enemy.Position):0} m — standing on Defend. " +
                            "It can still be ordered to withdraw.",
                            unit.Id);

                    unit.Route = null;
                    unit.HeldUpBy = enemy.Id;
                    return;
                }

                if (breakthrough > stopping)
                {
                    // Through, but not unscathed — and the line being ridden
                    // through suffers for it too.
                    unit.Organization -= EnemyBreakthroughDisorderPerTick;
                    enemy.Organization -= EnemyBreakthroughDisorderPerTick;

                    if (tick % 10 == 0)
                        log.Decision("Contact",
                            $"{unit.Def.DisplayName} ({unit.FormationOrder.DisplayName}) is forcing through " +
                            $"{enemy.Def.DisplayName} ({enemy.FormationOrder.DisplayName}): " +
                            $"{breakthrough:0.00} against {stopping:0.00} — both losing order.",
                            unit.Id);

                    continue;
                }

                // Say which of the three levers stopped them, so the answer to
                // "why can't I get through" is on screen rather than inferred.
                // Once per hold-up, not once per tick.
                if (unit.HeldUpBy != enemy.Id)
                    log.Blocked("Contact",
                        $"{unit.Def.DisplayName} ({unit.FormationOrder.DisplayName}) halted by {enemy.Def.DisplayName} " +
                        $"({enemy.FormationOrder.DisplayName}, organization {enemy.Organization:0.00}) " +
                        $"at {Vec2.Distance(unit.Position, enemy.Position):0} m — " +
                        $"breakthrough {breakthrough:0.00} against stopping power {stopping:0.00}.",
                        unit.Id);

                unit.Route = null;
                unit.HeldUpBy = enemy.Id;
                return;
            }
        }

        /// <summary>
        /// Whether this unit's next step takes it further from the enemy holding
        /// it up.
        /// </summary>
        /// <remarks>
        /// Judged on the <i>direction</i> of the next step, not on where the
        /// route eventually ends. Comparing distances to the destination lets a
        /// unit march clean through an enemy and call it a withdrawal, because
        /// the far side is indeed further away — and after smoothing the next
        /// waypoint often <i>is</i> the far destination.
        /// </remarks>
        private static bool IsWithdrawingFrom(UnitInstance unit, UnitInstance enemy)
        {
            if (unit.Route == null || unit.Route.IsComplete) return false;

            Vec2 heading = (unit.Route.Target - unit.Position).Normalised();
            Vec2 away = (unit.Position - enemy.Position).Normalised();

            if (heading.IsNearZero || away.IsNearZero) return false;

            // Any genuine component away from the enemy counts as breaking off.
            // Moving across their front does not — that is exactly the march a
            // zone of control exists to prevent.
            return Vec2.Dot(heading, away) > 0f;
        }

        /// <summary>
        /// Drains organization from units standing on top of one another.
        /// </summary>
        /// <remarks>
        /// Uses the real footprints rather than a radius, because this is the
        /// one question where a unit's actual shape is the thing being asked
        /// about — whether these two bodies of men are occupying the same
        /// ground.
        /// </remarks>
        private static void ApplyOverlapDisorder(BattleState battle, UnitInstance unit, IBattleLog log)
        {
            OrientedRect shape = unit.Shape;

            foreach (UnitInstance other in battle.UnitsOnField())
            {
                // Each pair once, in id order.
                if (other.Id.Value <= unit.Id.Value) continue;
                if (!other.IsFighting) continue;
                if (other.Owner != unit.Owner) continue;

                // Standing together is free; forcing a way through is not.
                if (!unit.IsMarching && !other.IsMarching) continue;

                if (!OrientedRect.Overlaps(shape, other.Shape)) continue;

                unit.Organization -= FriendlyOverlapDisorderPerTick;
                other.Organization -= FriendlyOverlapDisorderPerTick;
            }
        }
    }
}
