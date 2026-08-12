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

        /// <summary>
        /// How much of the shock reported by everything else actually lands.
        /// </summary>
        /// <remarks>
        /// A single dial rather than thirty percent shaved off a dozen
        /// constants scattered across combat, shooting and morale. Every rule
        /// that frightens men reports what happened and this decides what it
        /// costs, so tuning how brittle the whole army is stays one number
        /// instead of an afternoon of arithmetic.
        ///
        /// Battles were ending too early: regiments broke while they still had
        /// three quarters of their strength and most fights were decided in
        /// five or six turns, which left no room for a reserve to matter.
        /// </remarks>
        private const float ShockScale = 0.7f;

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

        /// <summary>Cohesion regained per pulse while standing clear of the enemy.</summary>
        /// <remarks>
        /// <para>
        /// Men dress their ranks and close up whenever nobody is killing them.
        /// Without this, organization was a one-way ratchet: half a dozen rules
        /// drained it — rough ground, riding through a line, taking a charge,
        /// shouldering past friends at a twentieth of a point a second — and
        /// nothing anywhere put it back.
        /// </para>
        /// <para>
        /// The effect was invisible in a controlled fight and ruinous in a real
        /// one. A regiment that spent three turns manoeuvring behind its own
        /// line arrived with a third of its cohesion, and cohesion multiplies
        /// attack, defence, stopping power and breakthrough all at once — so
        /// cavalry that the balance sweep says beats swordsmen four times out of
        /// five lost to them badly, purely for having taken a long route to the
        /// fight. Nothing on screen explained it.
        /// </para>
        /// <para>
        /// The rate is bounded from both ends and the window is narrow. Too
        /// slow and cohesion is a ratchet again. Too fast and repeated charges
        /// stop accumulating: at twice this figure, a regiment ridden through
        /// three times came out no worse than one ridden through once, because
        /// each charge only undid the previous one's recovery. Roughly a third
        /// of a charge's damage per turn is what leaves both true.
        /// </para>
        /// </remarks>
        private const float CohesionRecoveryPerPulse = 0.006f;

        /// <summary>How much of that a regiment manages while still on the march.</summary>
        /// <remarks>
        /// Ranks can be dressed on the move, but not well. Standing still is
        /// how a broken-up formation is properly put back together, which gives
        /// halting a use beyond waiting.
        /// </remarks>
        private const float CohesionRecoveryWhileMarching = 0.5f;

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

                Reform(battle, unit);
                ApplyPressure(battle, unit, log);
            }
        }

        /// <summary>
        /// Closes the ranks up again while nobody is fighting these men.
        /// </summary>
        /// <remarks>
        /// Gated on melee alone rather than on anything having happened. A
        /// regiment under long-range fire is being killed, not disordered —
        /// morale is what shooting costs, and it is charged separately. What
        /// breaks a formation is crossing bad ground, being ridden through, and
        /// having something arrive at close quarters.
        /// </remarks>
        private static void Reform(BattleState battle, UnitInstance unit)
        {
            if (unit.Organization >= 1f) return;
            if (unit.EnemiesInContact > 0) return;

            // Standing still re-forms a regiment wherever it is standing, bad
            // ground included. Halting in a wood to dress the ranks is a real
            // thing to do with a turn, and blocking it meant a unit that had
            // stopped in a marsh simply stayed a rabble — which reads as the
            // game punishing you for terrain you already paid to cross.
            //
            // On the march it is different: you cannot both wade a river and
            // close your files, and letting recovery run there cancelled most
            // of the crossing.
            if (unit.IsMarching)
            {
                if (battle.WorstDisorderUnder(unit) > 0f) return;

                unit.Organization += CohesionRecoveryPerPulse * CohesionRecoveryWhileMarching;
                return;
            }

            unit.Organization += CohesionRecoveryPerPulse;
        }

        private static void ApplyPressure(BattleState battle, UnitInstance unit, IBattleLog log)
        {
            float pressure = unit.PendingMoraleShock;
            unit.PendingMoraleShock = 0f;

            // Whether anything at all has happened to these men this pulse —
            // shot at as much as fought.
            bool underFire = pressure > 0f;

            // How badly the army as a whole is faring. Men know when they are
            // losing even where they stand.
            pressure += ArmyLossesPressure * ArmyLossFraction(battle, unit.Owner);

            // Panic is contagious, and the sight of the regiment beside you
            // running is worse than anything happening in front of you.
            int breaking = RoutingFriendsNear(battle, unit);
            if (breaking > 0) pressure += NearbyRoutShock * breaking;

            // Everything that frightens men, damped in one place. Recovery is
            // deliberately left alone — this decides how hard they are to
            // break, not how slowly they come back.
            pressure *= ShockScale;

            bool fighting = unit.EnemiesInContact > 0;

            // Left alone, a unit collects itself — and does so against the
            // background gloom of a losing battle rather than being prevented by
            // it. Gating recovery on there being no pressure at all meant a
            // rallied regiment standing safely in the rear still sank, purely
            // because its army was behind, and could never come back.
            //
            // "Left alone" has to mean nobody is killing them, not merely that
            // nobody is within sword's reach. Counting only melee meant a
            // regiment under artillery recovered faster than it was being
            // shelled — a battery took three hundred and seventy men off a
            // body of swordsmen across thirty-two turns and they never so much
            // as wavered, which is the exact opposite of morale deciding
            // fights before casualties do.
            if (!fighting && !underFire)
                pressure -= RecoveryPerPulse;

            if (pressure <= 0f && unit.Morale >= 1f) return;

            // A unit's own steadiness resists all of it, and a formation that has
            // come apart resists none of it.
            float rating = MathF.Max(0.1f, unit.MoraleRating);
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

                    // Rallies where it stands and waits to be told what to do.
                    // Without clearing the order it kept whatever it had been
                    // given before it broke — usually an attack — and marched
                    // straight back into the fight that had just routed it,
                    // shaken, alone and at half strength. Men who have run do
                    // not re-form and charge of their own accord.
                    unit.GiveOrder(UnitOrder.Stand(Stance.Defend), unit.Position);

                    log.Info("Morale",
                        $"{unit.Def.DisplayName} has rallied at {unit.Strength} men, still shaken — " +
                        "holding where it stands until ordered.",
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
