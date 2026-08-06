using System;
using BattleChess.Contracts;

namespace BattleChess.Rules
{
    /// <summary>
    /// Resolves shooting: archers and artillery firing at whatever is in reach.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The clock that decides whether a charge across open ground is bold or
    /// suicidal. Archers reach 180 m and shoot every ten ticks, so infantry
    /// closing at 1.6 m/s spends about ten volleys in the open while cavalry at
    /// 4.8 m/s takes three. That difference is the entire argument for cavalry:
    /// not that it hits harder, but that it spends less time being shot at.
    /// </para>
    /// <para>
    /// Unlike melee, every man shoots — volleys arc over the ranks in front, so
    /// depth does not waste a body of archers the way it wastes a body of
    /// spearmen. Line of sight is not checked yet; that arrives with vision in
    /// M4, and until then shooting ignores intervening terrain.
    /// </para>
    /// </remarks>
    public sealed class RangedCombatSystem : IBattleSystem
    {
        /// <summary>Casualties per shooter per volley, before everything else.</summary>
        /// <remarks>
        /// Calibrated so archers shooting an infantry regiment across their full
        /// range cost it roughly a quarter of its strength before contact —
        /// enough that crossing open ground in front of bowmen is a real
        /// decision, not enough that the melee is decided before it begins.
        /// </remarks>
        private const float BaseVolleyRate = 0.03f;

        /// <summary>How much of its power a shot keeps at maximum range.</summary>
        private const float DamageAtMaxRange = 0.5f;

        /// <summary>Spread of the random variation on each volley.</summary>
        private const float VolleyVariance = 0.1f;

        /// <summary>Morale shock per fraction of a regiment shot away.</summary>
        /// <remarks>
        /// Lower than the melee figure, and deliberately. Shooting lands many
        /// small blows where melee lands few large ones, so an equal shock per
        /// casualty accumulated far faster and broke regiments before they ever
        /// reached the enemy — which made crossing open ground impossible rather
        /// than merely expensive.
        /// </remarks>
        private const float CasualtyShockPerFraction = 1.1f;

        /// <summary>Extra shock from being shot at without being able to reply.</summary>
        /// <remarks>
        /// Standing under fire unable to answer wears at men out of proportion
        /// to the casualties, which is why troops charge guns rather than
        /// waiting.
        /// </remarks>
        private const float HelplessnessShock = 0.004f;

        public string Name => "Shooting";

        public void Step(BattleState battle, int tick, IBattleLog log)
        {
            if (battle == null) throw new ArgumentNullException(nameof(battle));

            foreach (UnitInstance shooter in battle.UnitsOnField())
            {
                if (!shooter.IsFighting) continue;

                float range = shooter.Def.Get(UnitAttributes.Range);
                if (range <= 0f) continue;

                if (shooter.ReloadRemaining > 0)
                {
                    shooter.ReloadRemaining--;
                    continue;
                }

                // Nobody shoots with an enemy already among them.
                if (shooter.EnemiesInContact > 0) continue;

                UnitInstance? target = ChooseTarget(battle, shooter, range);
                if (target == null) continue;

                Fire(battle, shooter, target, range, log);
                shooter.ReloadRemaining = Math.Max(1, shooter.Def.Get(UnitAttributes.ReloadTicks));
            }
        }

        /// <summary>
        /// Picks what to shoot at: the nearest enemy in range that the army can
        /// actually see.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Deliberately simple and deterministic. Choosing targets well is a
        /// judgement the AI should make through orders, not something the
        /// shooting rule should quietly do on everyone's behalf.
        /// </para>
        /// <para>
        /// Vision is what makes reach worth anything rather than everything.
        /// Artillery outranges every pair of eyes on the field by a wide margin,
        /// so without this a battery bombards the far side of the map from the
        /// opening minute, hitting regiments nobody has laid eyes on. Gated on
        /// sight, that same reach becomes a reason to push scouts forward and
        /// hold high ground — the guns can hit anything the army can find, and
        /// nothing it cannot.
        /// </para>
        /// </remarks>
        private static UnitInstance? ChooseTarget(BattleState battle, UnitInstance shooter, float range)
        {
            UnitInstance? nearest = null;
            float bestSquared = range * range;

            foreach (UnitInstance other in battle.UnitsOnField())
            {
                if (other.Owner == shooter.Owner) continue;
                if (!other.IsOnField) continue;
                if (!battle.Vision.CanSee(battle, shooter.Owner, other)) continue;

                float squared = Vec2.DistanceSquared(shooter.Position, other.Position);

                if (squared < bestSquared)
                {
                    bestSquared = squared;
                    nearest = other;
                }
            }

            return nearest;
        }

        private static void Fire(BattleState battle, UnitInstance shooter, UnitInstance target, float range, IBattleLog log)
        {
            float distance = Vec2.Distance(shooter.Position, target.Position);

            // Falls away with distance rather than cutting off, so edge-of-range
            // shooting is weak instead of being a cliff one metre wide.
            float falloff = 1f - (1f - DamageAtMaxRange) * Math.Clamp(distance / range, 0f, 1f);

            float attack = shooter.Def.Get(UnitAttributes.RangedAttack)
                           * shooter.Def.AttackMultiplierAgainst(target.Def.Class)
                           * ConditionOf(shooter);

            TerrainDef ground = battle.TerrainAt(target.Position);

            float defence = target.Def.Get(UnitAttributes.Defence)
                            * ConditionOf(target)
                            * (1f + ground.Get(TerrainAttributes.DefenceBonus));

            if (defence <= 0.01f) defence = 0.01f;

            float armour = Math.Clamp(target.Def.Get(UnitAttributes.Armour), 0f, 0.9f);

            float raw = BaseVolleyRate
                        * shooter.Strength
                        * (attack / defence)
                        * (1f - armour)
                        * falloff
                        * target.FormationOrder.RangedVulnerability
                        // How spread out these men are by their own nature,
                        // whatever order they have been told to stand in.
                        * target.Def.Get(UnitAttributes.RangedVulnerability)
                        // Where the target is standing matters as much as how.
                        // Woods hide them; a river crossing leaves them with
                        // nowhere to go and nothing to get behind.
                        * ground.Get(TerrainAttributes.RangedCover)
                        * battle.Rng.NextVariance(VolleyVariance);

            int casualties = Math.Clamp((int)MathF.Round(raw), 0, target.Strength);
            if (casualties <= 0) return;

            int before = target.Strength;
            target.TakeCasualties(casualties);

            float shock = CasualtyShockPerFraction * (casualties / (float)before);

            // Being shot at by something you cannot reach is its own kind of
            // pressure, separate from the men it costs.
            if (target.Def.Get(UnitAttributes.Range) < distance)
                shock += HelplessnessShock;

            target.PendingMoraleShock += shock;

            log.Decision("Shooting",
                $"{shooter.Def.DisplayName} volleys {target.Def.DisplayName} at {distance:0} m " +
                $"({target.FormationOrder.DisplayName}): {casualties} down, {target.Strength} left.",
                shooter.Id);

            if (target.State == UnitState.Destroyed)
                log.Warning("Shooting", $"{target.Def.DisplayName} has been shot to pieces.", target.Id);
        }

        private static float ConditionOf(UnitInstance unit) => 0.35f + 0.65f * unit.Organization;
    }
}
