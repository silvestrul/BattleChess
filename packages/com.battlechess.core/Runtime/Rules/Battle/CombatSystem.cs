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
        private const float CasualtyVariance = 0.15f;

        /// <summary>Extra spread on the pulse a charge lands.</summary>
        /// <remarks>
        /// The one moment worth gambling on. A grind between two lines should
        /// be predictable — that is what makes committing to it a calculation —
        /// but the instant of impact is where battles turn, and a charge that
        /// always does exactly what the arithmetic says is a charge nobody ever
        /// holds their breath over. Wide here, narrow everywhere else.
        /// </remarks>
        private const float ChargeVariance = 0.35f;

        /// <summary>Extra hitting power from attacking a flank or the rear.</summary>
        private const float MaxFlankingBonus = 1.0f;

        /// <summary>How much a shaken unit's blows lose their weight.</summary>
        private const float WaveringAttackPenalty = 0.7f;

        // ---- What combat does to a unit's willingness ------------------------

        /// <summary>Morale shock per fraction of the regiment lost in a pulse.</summary>
        private const float CasualtyShockPerFraction = 2.0f;

        /// <summary>Shock from being taken in the flank, scaling to the rear.</summary>
        /// <remarks>
        /// This is what a flank attack is <i>for</i>, now that frontage is
        /// measured honestly. A regiment struck square in the side presents its
        /// depth — five metres against a hundred — so only a handful of men on
        /// either side can reach each other and almost nobody dies. What breaks
        /// a formation taken in the flank was never the casualties; it is that
        /// the men on that end are being killed by something they are not
        /// facing and cannot answer, and the ones who can see it are the ones
        /// who run first.
        ///
        /// Raised fourfold when the geometry stopped pretending a perpendicular
        /// contact was a full-width battle. At the old figure a lone flanking
        /// regiment did nothing at all: no casualties, because there was no
        /// shared front, and no fright either.
        /// </remarks>
        private const float MaxFlankShock = 0.12f;

        /// <summary>One-off shock from a charge landing.</summary>
        private const float ChargeShock = 0.05f;

        /// <summary>
        /// Organization torn out of a formation by a charge landing on it,
        /// scaled by the charger's own weight.
        /// </summary>
        /// <remarks>
        /// What a charge is actually for. Casualties alone never explained why
        /// horsemen were worth three times their number in infantry: the point
        /// of riding into a line is not the men you kill on impact but that the
        /// line stops being a line. Cohesion carries the condition factor, the
        /// formation bonus and the stopping power, so a shaken regiment is
        /// worse at everything at once — including at stopping the next charge.
        /// </remarks>
        private const float ChargeDisorder = 0.12f;

        /// <summary>Shock from losing the exchange, however narrowly.</summary>
        private const float LosingExchangeShock = 0.01f;

        /// <summary>Shock per enemy beyond the first in contact.</summary>
        private const float OutnumberedShock = 0.01f;

        /// <summary>
        /// Organization lost per pulse for each enemy beyond the first.
        /// </summary>
        /// <remarks>
        /// The real cost of being set upon from several directions, and the
        /// reason concentrating force is worth the trouble. Frontage alone
        /// cannot express it: a regiment divides its line among its attackers
        /// and the raw exchange comes out roughly even, which would make
        /// enveloping an enemy pointless.
        ///
        /// What actually happens is that a body of men fighting front, flank
        /// and rear at once stops being a formation. It loses cohesion, and
        /// cohesion is what its condition, its formation bonus and its stopping
        /// power all rest on — so the collapse compounds rather than adding up.
        /// </remarks>
        private const float SurroundedDisorderPerPulse = 0.06f;

        /// <summary>How far a regiment must get clear before it can charge the same enemy again, in metres.</summary>
        /// <remarks>
        /// A charge is a run-up, not a state of mind. Cavalry covers this in
        /// about half a turn at the gallop, which is roughly what it takes to
        /// come round, re-dress the ranks and build to speed again.
        /// </remarks>
        private const float ChargeReformMetres = 150f;

        /// <summary>And how long it must spend out of contact before it counts as re-formed.</summary>
        private const int ChargeReformTicks = 30;

        /// <summary>
        /// Pairs already in contact and the last tick they touched, so a charge
        /// is spent once and re-earned rather than repeated.
        /// </summary>
        /// <remarks>
        /// The tick matters as much as the pair. Forgetting a contact the
        /// moment it broke meant a regiment that rode clean through an enemy
        /// bought a fresh charge on the way out: cavalry overshot, wheeled a
        /// hundred and seventy degrees, came back and landed a full charge
        /// bonus again — four exchanges, two of them charges, and a ten-to-one
        /// result that had nothing to do with the attacker being stronger. It
        /// simply could not be stopped, and anything that cannot be stopped is
        /// permanently charging.
        /// </remarks>
        private readonly Dictionary<long, int> _engaged = new Dictionary<long, int>();

        /// <summary>Scratch list for pruning, so the dictionary is never modified while read.</summary>
        private readonly List<long> _reformed = new List<long>();

        /// <summary>
        /// Pairs that have touched at any point since the last pulse.
        /// </summary>
        /// <remarks>
        /// A list rather than a set, kept in the order contacts were noticed, so
        /// the same seed resolves the same fights in the same order.
        /// </remarks>
        private readonly List<long> _touched = new List<long>();

        public string Name => "Combat";

        public void Step(BattleState battle, int tick, IBattleLog log)
        {
            if (battle == null) throw new ArgumentNullException(nameof(battle));

            // Contact is noticed every tick and settled on the pulse. Sampling
            // only on the pulse meant a fast regiment could cross the whole
            // contact zone in the gap between two of them and come out the far
            // side untouched — cavalry at 4.8 m/s covers 48 m in a pulse
            // against a contact window barely 29 m wide, so charging home a
            // second and third time cost the enemy nothing at all.
            NoteContacts(battle);

            if (tick % PulseIntervalTicks != 0) return;

            // Gather the pairs first, then resolve. Both sides of an exchange
            // must be worked out from the same starting strengths, or whoever
            // happens to be resolved first gets a free advantage.
            var exchanges = new List<(UnitInstance A, UnitInstance B, bool Charge)>();

            foreach (UnitInstance unit in battle.UnitsOnField())
                unit.EnemiesInContact = 0;

            for (int i = 0; i < _touched.Count; i++)
            {
                long pair = _touched[i];

                UnitInstance unit = battle.Get(new UnitId((int)(pair >> 32)));
                UnitInstance other = battle.Get(new UnitId((int)(pair & 0xFFFFFFFF)));

                if (!unit.IsFighting || !other.IsFighting) continue;

                bool fresh = !_engaged.ContainsKey(pair);
                _engaged[pair] = tick;

                unit.EnemiesInContact++;
                other.EnemiesInContact++;

                exchanges.Add((unit, other, fresh));
            }

            _touched.Clear();

            ForgetReformedPairs(battle, tick);

            // Once per unit per pulse, not once per exchange. Charging it per
            // exchange made the cost grow with the square of the attackers and
            // stripped a surrounded regiment of all cohesion inside half a turn.
            foreach (UnitInstance unit in battle.UnitsOnField())
            {
                if (unit.EnemiesInContact > 1)
                    unit.Organization -= SurroundedDisorderPerPulse * (unit.EnemiesInContact - 1);
            }

            foreach ((UnitInstance a, UnitInstance b, bool charge) in exchanges)
                Resolve(battle, a, b, charge, log);
        }

        /// <summary>
        /// Records every enemy pair within reach of each other this tick.
        /// </summary>
        private void NoteContacts(BattleState battle)
        {
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

                    if (!_touched.Contains(pair))
                        _touched.Add(pair);
                }
            }
        }

        private void Resolve(BattleState battle, UnitInstance a, UnitInstance b, bool charge, IBattleLog log)
        {
            int aFighting = FightingMen(a, b);
            int bFighting = FightingMen(b, a);

            if (aFighting <= 0 || bFighting <= 0) return;

            // A charge is something a regiment does, not something that happens
            // to it. Handing the bonus to both sides of a fresh contact meant a
            // cavalry regiment standing still collected its full charge
            // multiplier every time somebody walked into it — and a fresh one
            // against each attacker as they arrived, so the more regiments were
            // sent at it the more charges it received.
            bool aCharges = charge && IsComingOn(a, b);
            bool bCharges = charge && IsComingOn(b, a);

            int lossesToB = Casualties(battle, a, b, aFighting, bFighting, aCharges);
            int lossesToA = Casualties(battle, b, a, bFighting, aFighting, bCharges);

            int strengthA = a.Strength;
            int strengthB = b.Strength;

            b.TakeCasualties(lossesToB);
            a.TakeCasualties(lossesToA);

            // Report what this exchange did to each side's willingness. The
            // morale system decides what it costs them.
            // Each side is shaken by the charge the *other* one landed.
            RecordShock(a, b, lossesToA, lossesToB, strengthA, bCharges);
            RecordShock(b, a, lossesToB, lossesToA, strengthB, aCharges);

            // And a charge tears at the formation it lands on, which is worth
            // more than the men it kills.
            if (aCharges) b.Organization -= ChargeDisorder * a.Def.Get(UnitAttributes.ChargeBonus);
            if (bCharges) a.Organization -= ChargeDisorder * b.Def.Get(UnitAttributes.ChargeBonus);

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

        /// <summary>
        /// Whether this unit closed on the enemy under its own steam, rather
        /// than having them arrive.
        /// </summary>
        /// <remarks>
        /// Either still under way, or under orders to attack this particular
        /// enemy — the second because contact clears the route the moment it is
        /// made, so a regiment that has just charged home is no longer marching
        /// by the time the exchange is worked out.
        /// </remarks>
        private static bool IsComingOn(UnitInstance unit, UnitInstance enemy) =>
            unit.IsMarching || (unit.Order.Kind == OrderKind.Attack && unit.Order.Target == enemy.Id);

        private static void ReportDestruction(UnitInstance unit, IBattleLog log)
        {
            if (unit.State == UnitState.Destroyed)
                log.Warning("Combat", $"{unit.Def.DisplayName} has been destroyed.", unit.Id);
        }

        /// <summary>
        /// How many of a unit's men can actually reach a given enemy.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Limited by the width the two units share, not by how many are
        /// present. A regiment wider than its opponent gains nothing from the
        /// overhang — those men have nobody in front of them.
        /// </para>
        /// <para>
        /// And a regiment has <b>one</b> frontage, which it must divide among
        /// everyone it is fighting. Without that division a defender set upon
        /// by three enemies brought its whole line to bear against each of
        /// them in turn — fighting three full battles at once and coming out
        /// ahead of all three, which inverted the entire point of concentrating
        /// force. Men committed to the regiment on the left are not
        /// simultaneously fighting the one on the right.
        /// </para>
        /// </remarks>
        public static int FightingMen(UnitInstance unit, UnitInstance enemy)
        {
            float contactWidth = EngagedWidth(unit, enemy) / Math.Max(1, unit.EnemiesInContact);
            Formation formation = unit.Formation;

            int frontRank = (int)MathF.Floor(contactWidth / formation.FileWidth);
            frontRank = Math.Min(frontRank, unit.Strength);

            if (frontRank <= 0) return 0;

            int behind = Math.Max(0, unit.Strength - frontRank);
            int canSupport = Math.Min(behind, frontRank * SupportingRanks);

            return frontRank + (int)MathF.Round(canSupport * SupportingRankContribution);
        }

        /// <summary>
        /// How far off a unit's own front a threat is coming from, 0 for
        /// straight ahead and 1 for straight behind.
        /// </summary>
        private static float OffFront(UnitInstance unit, UnitInstance enemy) =>
            Facing.AbsoluteDelta(unit.Facing, Facing.Towards(unit.Position, enemy.Position)) / MathF.PI;

        /// <summary>
        /// Beyond this far off its front, a regiment has no formed line facing
        /// the threat and can be enveloped.
        /// </summary>
        private const float FrontalArc = 0.25f;

        /// <summary>
        /// How much of its width a regiment loses the use of when it is taken
        /// from directly behind.
        /// </summary>
        /// <remarks>
        /// Men attacked from a quarter they are not facing cannot bring their
        /// numbers to bear: the ranks are the wrong way round, the front rank
        /// is at the back, and only those who can physically turn are fighting
        /// at all. This is the "deals very little" half of being caught out of
        /// position.
        /// </remarks>
        private const float OutOfArcPenalty = 0.75f;

        /// <summary>
        /// How wide a front this unit can actually bring against an enemy, in
        /// metres.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Two regiments that are both facing each other fight across the
        /// ground they genuinely share, so meeting square on is a full-width
        /// battle and clipping each other by a corner is a small one. That much
        /// is pure geometry, and it is what stops a position a player worked
        /// for from counting for nothing.
        /// </para>
        /// <para>
        /// An enemy with no formed line toward you is a different matter. Their
        /// flank is a five-metre face, but there is nothing to stop your men
        /// folding round it and piling in along their whole length, so you
        /// bring your own frontage to bear rather than theirs. Measuring the
        /// bare overlap in that case made a flank attack do <i>nothing</i>:
        /// no casualties, because the shared front was five metres wide, which
        /// is not what happens to a regiment taken in the side.
        /// </para>
        /// <para>
        /// The asymmetry is the whole point. The attacker fights with its full
        /// width; the flanked regiment answers with the narrow face it actually
        /// presents, reduced again for being caught facing the wrong way. Many
        /// engaging few who cannot reply — which is what being outmanoeuvred
        /// should feel like.
        /// </para>
        /// </remarks>
        private static float EngagedWidth(UnitInstance unit, UnitInstance enemy)
        {
            float overlap = SharedFrontage(unit, enemy);

            // Their front is turned away from us, so there is nothing holding
            // our line off: we envelop as far as our own width allows, capped
            // by how much of them there is to get at.
            float reach = OffFront(enemy, unit) > FrontalArc
                ? MathF.Min(unit.Footprint.Width, MathF.Max(enemy.Footprint.Width, enemy.Footprint.Depth))
                : overlap;

            // And we are under the same rule in reverse.
            float formed = 1f - OutOfArcPenalty * OffFront(unit, enemy);

            return MathF.Max(0f, reach * formed);
        }

        /// <summary>
        /// How much of two regiments' fronts actually face each other, in
        /// metres.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The geometry the frontage rule was missing. Taking the narrower of
        /// the two widths meant that two regiments clipping each other by a
        /// corner fought across their whole front — a scouting party that had
        /// barely brushed a body of swordsmen traded blows as though it had met
        /// them square on, which is why numbers and position seemed not to
        /// matter at the edges of a line.
        /// </para>
        /// <para>
        /// Measured as the overlap of the two shapes projected onto the axis
        /// across the line joining them: exactly the width where men on one
        /// side have men on the other in front of them, and nothing else.
        /// </para>
        /// </remarks>
        public static float SharedFrontage(UnitInstance unit, UnitInstance enemy)
        {
            Vec2 between = enemy.Position - unit.Position;

            // Sitting on top of one another: no meaningful line between them, so
            // fall back to the narrower front.
            if (between.IsNearZero)
                return MathF.Min(unit.Footprint.Width, enemy.Footprint.Width);

            Vec2 across = new Vec2(-between.Y, between.X).Normalised();

            float centre = Vec2.Dot(unit.Position, across);
            float enemyCentre = Vec2.Dot(enemy.Position, across);

            float reach = unit.Shape.ProjectedRadius(across);
            float enemyReach = enemy.Shape.ProjectedRadius(across);

            float low = MathF.Max(centre - reach, enemyCentre - enemyReach);
            float high = MathF.Min(centre + reach, enemyCentre + enemyReach);

            return MathF.Max(0f, high - low);
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
                        * battle.Rng.NextVariance(charge ? ChargeVariance : CasualtyVariance);

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

        /// <summary>
        /// Releases pairs that have genuinely broken off and re-formed, so
        /// their next meeting counts as a fresh charge.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Both conditions are required, and requiring both is the whole point.
        /// Distance alone lets a fast regiment buy a charge by bouncing out and
        /// straight back in; time alone lets two lines that have been grinding
        /// at each other for half a turn suddenly charge without either of them
        /// going anywhere.
        /// </para>
        /// <para>
        /// The dictionary is read in hash order here, which is normally
        /// forbidden in the rules. It is safe in this one place because the
        /// outcome does not depend on the order: every pair is tested against
        /// the same two conditions and the same set is removed whichever
        /// sequence they are visited in. Nothing ordered is derived from the
        /// walk.
        /// </para>
        /// </remarks>
        private void ForgetReformedPairs(BattleState battle, int tick)
        {
            _reformed.Clear();

            foreach (KeyValuePair<long, int> entry in _engaged)
            {
                if (tick - entry.Value < ChargeReformTicks) continue;

                var first = battle.Get(new UnitId((int)(entry.Key >> 32)));
                var second = battle.Get(new UnitId((int)(entry.Key & 0xFFFFFFFF)));

                // A regiment that has left the field has plainly disengaged.
                if (!first.IsFighting || !second.IsFighting ||
                    OrientedRect.GapBetween(first.Shape, second.Shape) > ChargeReformMetres)
                    _reformed.Add(entry.Key);
            }

            for (int i = 0; i < _reformed.Count; i++)
                _engaged.Remove(_reformed[i]);
        }
    }
}
