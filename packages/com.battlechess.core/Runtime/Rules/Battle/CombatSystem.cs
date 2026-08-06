using System;
using System.Collections.Generic;
using BattleChess.Contracts;

namespace BattleChess.Rules
{
    /// <summary>
    /// Resolves melee between units in contact, in pulses.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The rule that makes everything else matter. Only men who can actually
    /// reach the enemy fight — the front rank across the width the two units
    /// share, plus a fraction of the ranks immediately behind. Everyone else is
    /// standing in a field.
    /// </para>
    /// <para>
    /// The consequence is deliberate and severe: six hundred men meeting two
    /// hundred head-on fight <b>evenly</b>, because both present the same
    /// thirty metres of contact. Numbers are worth nothing until manoeuvre
    /// brings them to bear, which is why flanking, formation and envelopment
    /// exist at all.
    /// </para>
    /// </remarks>
    public sealed class CombatSystem : IBattleSystem
    {
        /// <summary>Ticks between combat pulses. Six per turn.</summary>
        public const int PulseIntervalTicks = 10;

        /// <summary>
        /// Casualties per fighting man per pulse in an otherwise even fight.
        /// </summary>
        /// <remarks>
        /// Calibrated so an even line fight — roughly 240 fighting men a side —
        /// costs about five or six men per pulse, reaching a third of a
        /// regiment over five turns. Morale should break someone before that,
        /// which lands the intended four-to-six turn engagement.
        /// </remarks>
        private const float BaseCasualtyRate = 0.04f;

        /// <summary>How much the ranks behind the front contribute.</summary>
        /// <remarks>
        /// They push, fill gaps and step over the fallen rather than fighting
        /// outright. Enough that depth is worth something; not so much that
        /// frontage stops deciding fights.
        /// </remarks>
        private const float SupportingRankContribution = 0.3f;

        /// <summary>How many ranks behind the front can contribute at all.</summary>
        private const int SupportingRanks = 2;

        /// <summary>Spread of the random variation on each exchange.</summary>
        /// <remarks>
        /// Small on purpose. With hidden information and blind orders, high
        /// variance reads as unfair rather than exciting — the player cannot
        /// tell being outplayed from being unlucky.
        /// </remarks>
        private const float CasualtyVariance = 0.1f;

        /// <summary>Extra hitting power from attacking a flank or the rear.</summary>
        private const float MaxFlankingBonus = 1.0f;

        /// <summary>How much a shaken unit's blows lose their weight.</summary>
        private const float WaveringAttackPenalty = 0.7f;

        // ---- What combat does to a unit's willingness ------------------------

        /// <summary>Morale shock per fraction of the regiment lost in a pulse.</summary>
        private const float CasualtyShockPerFraction = 2.0f;

        /// <summary>Shock from being taken in the flank, scaling to the rear.</summary>
        private const float MaxFlankShock = 0.03f;

        /// <summary>One-off shock from a charge landing.</summary>
        private const float ChargeShock = 0.05f;

        /// <summary>Shock from losing the exchange, however narrowly.</summary>
        private const float LosingExchangeShock = 0.01f;

        /// <summary>Shock per enemy beyond the first in contact.</summary>
        private const float OutnumberedShock = 0.01f;

        /// <summary>Units already in contact, so a charge is only spent once.</summary>
        private readonly HashSet<long> _engaged = new HashSet<long>();

        public string Name => "Combat";

        public void Step(BattleState battle, int tick, IBattleLog log)
        {
            if (battle == null) throw new ArgumentNullException(nameof(battle));
            if (tick % PulseIntervalTicks != 0) return;

            // Gather the pairs first, then resolve. Both sides of an exchange
            // must be worked out from the same starting strengths, or whoever
            // happens to be resolved first gets a free advantage.
            var exchanges = new List<(UnitInstance A, UnitInstance B, bool Charge)>();

            foreach (UnitInstance unit in battle.UnitsOnField())
                unit.EnemiesInContact = 0;

            foreach (UnitInstance unit in battle.UnitsOnField())
            {
                if (!unit.IsFighting) continue;

                foreach (UnitInstance other in battle.UnitsOnField())
                {
                    // Each pair once, in ascending id order.
                    if (other.Id.Value <= unit.Id.Value) continue;
                    if (!other.IsFighting) continue;
                    if (other.Owner == unit.Owner) continue;

                    if (!OrderSystem.InContactWith(unit, other)) continue;

                    long pair = PairKey(unit.Id, other.Id);
                    bool fresh = _engaged.Add(pair);

                    unit.EnemiesInContact++;
                    other.EnemiesInContact++;

                    exchanges.Add((unit, other, fresh));
                }
            }

            // Forget contacts that have broken, so re-engaging charges again.
            _engaged.RemoveWhere(pair => !StillTouching(battle, pair));

            foreach ((UnitInstance a, UnitInstance b, bool charge) in exchanges)
                Resolve(battle, a, b, charge, log);
        }

        private void Resolve(BattleState battle, UnitInstance a, UnitInstance b, bool charge, IBattleLog log)
        {
            int aFighting = FightingMen(a, b);
            int bFighting = FightingMen(b, a);

            if (aFighting <= 0 || bFighting <= 0) return;

            int lossesToB = Casualties(battle, a, b, aFighting, bFighting, charge);
            int lossesToA = Casualties(battle, b, a, bFighting, aFighting, charge);

            int strengthA = a.Strength;
            int strengthB = b.Strength;

            b.TakeCasualties(lossesToB);
            a.TakeCasualties(lossesToA);

            // Report what this exchange did to each side's willingness. The
            // morale system decides what it costs them.
            RecordShock(a, b, lossesToA, lossesToB, strengthA, charge);
            RecordShock(b, a, lossesToB, lossesToA, strengthB, charge);

            log.Decision("Combat",
                $"{a.Def.DisplayName} ({aFighting} fighting) and {b.Def.DisplayName} ({bFighting} fighting) " +
                $"exchange: {a.Def.DisplayName} loses {lossesToA}, {b.Def.DisplayName} loses {lossesToB}." +
                (charge ? " Charge lands." : string.Empty),
                a.Id);

            ReportDestruction(a, log);
            ReportDestruction(b, log);
        }

        /// <summary>
        /// Notes what an exchange did to a unit's willingness to keep at it.
        /// </summary>
        /// <remarks>
        /// Losses matter most, but not only losses. Being taken from the side is
        /// alarming out of proportion to the men it kills, a charge landing is a
        /// shock in itself, and simply coming off worse wears at a regiment even
        /// when the casualties are light.
        /// </remarks>
        private static void RecordShock(
            UnitInstance unit,
            UnitInstance enemy,
            int lossesTaken,
            int lossesDealt,
            int strengthBefore,
            bool charge)
        {
            if (strengthBefore <= 0) return;

            float shock = CasualtyShockPerFraction * (lossesTaken / (float)strengthBefore);

            // Scales with how far round the attack came, so a flank is bad and
            // the rear is worse.
            Facing approach = Facing.Towards(unit.Position, enemy.Position);
            float offFront = Facing.AbsoluteDelta(unit.Facing, approach) / MathF.PI;
            shock += MaxFlankShock * offFront;

            if (charge && enemy.Def.Get(UnitAttributes.ChargeBonus) > 0f)
                shock += ChargeShock;

            if (lossesTaken > lossesDealt)
                shock += LosingExchangeShock;

            if (unit.EnemiesInContact > 1)
                shock += OutnumberedShock * (unit.EnemiesInContact - 1);

            // Reach keeps the horror at arm's length. Men killing at the end of
            // a pike are doing something less appalling than men wrestling, and
            // the ranks behind the points see less of it still.
            //
            // Only where the weapons point, though: the benefit fades to
            // nothing as the attack comes round the flank, because a spear wall
            // holds one direction and no others.
            float resistance = unit.Def.Get(UnitAttributes.MeleeShockResistance);
            shock *= resistance + (1f - resistance) * offFront;

            unit.PendingMoraleShock += shock;
        }

        private static void ReportDestruction(UnitInstance unit, IBattleLog log)
        {
            if (unit.State == UnitState.Destroyed)
                log.Warning("Combat", $"{unit.Def.DisplayName} has been destroyed.", unit.Id);
        }

        /// <summary>
        /// How many of a unit's men can actually reach the enemy.
        /// </summary>
        /// <remarks>
        /// Limited by the width the two units share, not by how many are
        /// present. A regiment wider than its opponent gains nothing from the
        /// overhang — those men have nobody in front of them.
        /// </remarks>
        public static int FightingMen(UnitInstance unit, UnitInstance enemy)
        {
            float contactWidth = MathF.Min(unit.Footprint.Width, enemy.Footprint.Width);
            Formation formation = unit.Formation;

            int frontRank = (int)MathF.Floor(contactWidth / formation.FileWidth);
            frontRank = Math.Min(frontRank, unit.Strength);

            if (frontRank <= 0) return 0;

            int behind = Math.Max(0, unit.Strength - frontRank);
            int canSupport = Math.Min(behind, frontRank * SupportingRanks);

            return frontRank + (int)MathF.Round(canSupport * SupportingRankContribution);
        }

        private static int Casualties(
            BattleState battle,
            UnitInstance attacker,
            UnitInstance defender,
            int attackerFighting,
            int defenderFighting,
            bool charge)
        {
            float attack = attacker.Def.Get(UnitAttributes.Attack)
                           * attacker.Def.AttackMultiplierAgainst(defender.Def.Class)
                           * ConditionOf(attacker)
                           * FlankingMultiplier(attacker, defender);

            if (charge)
                attack *= 1f + attacker.Def.Get(UnitAttributes.ChargeBonus);

            // A shaken regiment fights, but not with its heart in it.
            if (attacker.State == UnitState.Wavering)
                attack *= WaveringAttackPenalty;

            float defence = defender.Def.Get(UnitAttributes.Defence)
                            * ConditionOf(defender)
                            * (1f + battle.TerrainAt(defender.Position).Get(TerrainAttributes.DefenceBonus));

            if (defence <= 0.01f) defence = 0.01f;

            float armour = Math.Clamp(defender.Def.Get(UnitAttributes.Armour), 0f, 0.9f);

            float raw = BaseCasualtyRate
                        * attackerFighting
                        * (attack / defence)
                        * (1f - armour)
                        * battle.Rng.NextVariance(CasualtyVariance);

            // A pulse cannot kill more men than are actually standing in the
            // fight, however lopsided the odds.
            return Math.Clamp((int)MathF.Round(raw), 0, defenderFighting);
        }

        /// <summary>
        /// How much harder an attack lands for coming in off the enemy's front.
        /// </summary>
        /// <remarks>
        /// Continuous in the angle rather than banded, so there is no sudden
        /// cliff where one degree of wheel doubles the damage taken. Head-on is
        /// unmodified, a right angle is half again, and straight into the back
        /// is double.
        /// </remarks>
        private static float FlankingMultiplier(UnitInstance attacker, UnitInstance defender)
        {
            Facing approach = Facing.Towards(defender.Position, attacker.Position);
            float offFront = Facing.AbsoluteDelta(defender.Facing, approach);

            return 1f + MaxFlankingBonus * (offFront / MathF.PI);
        }

        private static float ConditionOf(UnitInstance unit) => 0.35f + 0.65f * unit.Organization;

        private static long PairKey(UnitId a, UnitId b) => ((long)a.Value << 32) | (uint)b.Value;

        private static bool StillTouching(BattleState battle, long pair)
        {
            var a = new UnitId((int)(pair >> 32));
            var b = new UnitId((int)(pair & 0xFFFFFFFF));

            UnitInstance first = battle.Get(a);
            UnitInstance second = battle.Get(b);

            return first.IsFighting && second.IsFighting && OrderSystem.InContactWith(first, second);
        }
    }
}
