using System;
using BattleChess.Contracts;

namespace BattleChess.Rules
{
    /// <summary>
    /// Decides who is still willing to fight, and what happens to those who
    /// are not.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Morale answers to two quite different things. What is happening to this
    /// regiment — its losses, whether it is being taken in the flank, whether it
    /// is losing the exchange — and what is happening to the battle: how badly
    /// the army as a whole is doing, and whether the units either side have just
    /// broken. Without the second, regiments fight to the last man in isolation
    /// and a line never collapses; with it, one unit breaking can take the line
    /// with it.
    /// </para>
    /// <para>
    /// Runs after combat, on the same pulse, so shock is applied against the
    /// casualties that caused it.
    /// </para>
    /// </remarks>
    public sealed class MoraleSystem : IBattleSystem
    {
        // ---- What breaks a unit ---------------------------------------------

        /// <summary>Morale lost per fraction of the regiment killed in a pulse.</summary>
        private const float CasualtyShock = 2.0f;

        /// <summary>Extra pressure from the army's overall state, at total loss.</summary>
        private const float ArmyLossesPressure = 0.02f;

        /// <summary>Pressure from each friendly regiment visibly breaking nearby.</summary>
        private const float NearbyRoutShock = 0.012f;

        /// <summary>How far panic spreads, in metres.</summary>
        private const float RoutContagionRadius = 200f;

        /// <summary>Morale regained per pulse while out of contact.</summary>
        private const float RecoveryPerPulse = 0.012f;

        // ---- Thresholds ------------------------------------------------------

        /// <summary>Below this a unit is shaken and fights worse.</summary>
        public const float WaveringThreshold = 0.5f;

        /// <summary>Below this a unit breaks and runs.</summary>
        public const float RoutingThreshold = 0.2f;

        /// <summary>A broken unit that recovers this far comes back, shaken.</summary>
        private const float RallyThreshold = 0.4f;

        /// <summary>How far from an enemy a broken unit must get before it can rally.</summary>
        private const float RallySafeRadius = 150f;

        /// <summary>Fraction of its remaining men a routing unit loses per pulse while being run down.</summary>
        /// <remarks>
        /// This is where most of a battle's casualties come from, historically:
        /// not the fighting but the pursuit afterwards. Men caught while running
        /// are taken, not killed, which is what makes chasing a broken enemy a
        /// decision rather than a formality.
        /// </remarks>
        private const float PursuitLossPerPulse = 0.12f;

        public string Name => "Morale";

        public void Step(BattleState battle, int tick, IBattleLog log)
        {
            if (battle == null) throw new ArgumentNullException(nameof(battle));
            if (tick % CombatSystem.PulseIntervalTicks != 0) return;

            foreach (UnitInstance unit in battle.UnitsOnField())
            {
                if (unit.State == UnitState.Routing)
                {
                    StepRouting(battle, unit, log);
                    continue;
                }

                if (!unit.IsFighting) continue;

                ApplyPressure(battle, unit, log);
            }
        }

        private static void ApplyPressure(BattleState battle, UnitInstance unit, IBattleLog log)
        {
            float pressure = unit.PendingMoraleShock;
            unit.PendingMoraleShock = 0f;

            // How badly the army as a whole is faring. Men know when they are
            // losing even where they stand.
            pressure += ArmyLossesPressure * ArmyLossFraction(battle, unit.Owner);

            // Panic is contagious, and the sight of the regiment beside you
            // running is worse than anything happening in front of you.
            int breaking = RoutingFriendsNear(battle, unit);
            if (breaking > 0) pressure += NearbyRoutShock * breaking;

            bool fighting = unit.EnemiesInContact > 0;

            // Left alone, a unit collects itself — and does so against the
            // background gloom of a losing battle rather than being prevented by
            // it. Gating recovery on there being no pressure at all meant a
            // rallied regiment standing safely in the rear still sank, purely
            // because its army was behind, and could never come back.
            if (!fighting)
                pressure -= RecoveryPerPulse;

            if (pressure <= 0f && unit.Morale >= 1f) return;

            // A unit's own steadiness resists all of it, and a formation that has
            // come apart resists none of it.
            float rating = MathF.Max(0.1f, unit.Def.Get(UnitAttributes.Morale));
            float brittleness = 2f - unit.Organization;

            unit.Morale -= pressure / rating * (pressure > 0f ? brittleness : 1f);

            UpdateState(unit, breaking, log);
        }

        private static void UpdateState(UnitInstance unit, int breakingNearby, IBattleLog log)
        {
            if (unit.Morale < RoutingThreshold && unit.State != UnitState.Routing)
            {
                unit.State = UnitState.Routing;
                unit.Route = null;
                unit.HeldUpBy = UnitId.None;

                log.Warning("Morale",
                    $"{unit.Def.DisplayName} has broken and is running" +
                    (breakingNearby > 0 ? $" — {breakingNearby} regiment(s) nearby had already gone." : "."),
                    unit.Id);

                return;
            }

            if (unit.Morale < WaveringThreshold && unit.State == UnitState.Steady)
            {
                unit.State = UnitState.Wavering;
                log.Warning("Morale", $"{unit.Def.DisplayName} is wavering ({unit.Morale:0.00}).", unit.Id);
                return;
            }

            if (unit.Morale >= WaveringThreshold && unit.State == UnitState.Wavering)
            {
                unit.State = UnitState.Steady;
                log.Info("Morale", $"{unit.Def.DisplayName} has steadied ({unit.Morale:0.00}).", unit.Id);
            }
        }

        /// <summary>
        /// Runs down, rallies or lets go of a broken unit.
        /// </summary>
        private static void StepRouting(BattleState battle, UnitInstance unit, IBattleLog log)
        {
            UnitInstance? pursuer = NearestEnemyWithin(battle, unit, RallySafeRadius);

            // Caught while running. Men taken here are captured, not killed —
            // and that distinction is the whole difference between an army that
            // can fight next week and one that cannot.
            if (pursuer != null && OrderSystem.InContactWith(unit, pursuer))
            {
                int taken = Math.Max(1, (int)MathF.Round(unit.Strength * PursuitLossPerPulse));
                unit.TakeCasualties(taken);

                if (unit.Strength <= 0)
                {
                    unit.State = UnitState.Captured;
                    log.Warning("Pursuit", $"{unit.Def.DisplayName} has been ridden down and taken.", unit.Id);
                    return;
                }

                log.Decision("Pursuit",
                    $"{pursuer.Def.DisplayName} is cutting down {unit.Def.DisplayName} — {taken} taken, {unit.Strength} still running.",
                    unit.Id);

                return;
            }

            // Clear of the enemy, a broken unit collects itself.
            if (pursuer == null)
            {
                unit.Morale += RecoveryPerPulse * 2f;

                if (unit.Morale >= RallyThreshold)
                {
                    unit.State = UnitState.Wavering;
                    unit.Route = null;

                    log.Info("Morale",
                        $"{unit.Def.DisplayName} has rallied at {unit.Strength} men, still shaken.",
                        unit.Id);
                }
            }
        }

        /// <summary>Fraction of a side's original strength already lost.</summary>
        private static float ArmyLossFraction(BattleState battle, PlayerId player)
        {
            int started = 0;
            int standing = 0;

            foreach (UnitInstance unit in battle.UnitsOf(player))
            {
                started += unit.InitialStrength;
                if (unit.IsOnField) standing += unit.Strength;
            }

            return started <= 0 ? 0f : 1f - standing / (float)started;
        }

        private static int RoutingFriendsNear(BattleState battle, UnitInstance unit)
        {
            int count = 0;

            foreach (UnitInstance other in battle.UnitsOnField())
            {
                if (other.Id == unit.Id) continue;
                if (other.Owner != unit.Owner) continue;
                if (other.State != UnitState.Routing) continue;

                if (Vec2.DistanceSquared(unit.Position, other.Position) <= RoutContagionRadius * RoutContagionRadius)
                    count++;
            }

            return count;
        }

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
