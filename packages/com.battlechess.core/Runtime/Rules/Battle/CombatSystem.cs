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
        /// <remarks>
        /// Tripled, because it is carrying the whole point of concentrating two
        /// regiments on one. Sharing a frontage means each attacker deals and
        /// takes about half what it would alone, so the raw exchange comes out
        /// very nearly even and sending the second regiment bought nothing.
        /// What it actually buys is the defender's nerve: men fighting two
        /// bodies at once are losing whether or not the casualty figures say so,
        /// and morale is where that has to show up.
        /// </remarks>
        private const float OutnumberedShock = 0.03f;

        /// <summary>
        /// How much of its own fright a regiment is spared for each friendly
        /// regiment fighting the same enemy alongside it.
        /// </summary>
        /// <remarks>
        /// The other half of the same idea. Men with a friendly regiment at
        /// their shoulder, both set on the same enemy, hold far better than the
        /// same men alone — so two attackers should not merely hurt the defender
        /// more, they should each be steadier for having company. At a half, a
        /// second regiment cuts what the first feels by a third.
        /// </remarks>
        private const float ShoulderToShoulderRelief = 0.5f;

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
        private readonly Dictionary<long, Engagement> _engaged = new Dictionary<long, Engagement>();

        /// <summary>
        /// A fight between two regiments, from the moment they met to the moment
        /// they came apart.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Kept so that a fight can be reported as a fight, rather than as one
        /// line per exchange for as long as it lasts. Six pulses a turn for
        /// every pair in contact is where a recording of a battle went from
        /// readable to four thousand lines, and the per-pulse figures were the
        /// least informative part of it: what a reader wants is who met whom,
        /// from which quarter, and what it cost by the end.
        /// </para>
        /// <para>
        /// The exchanges themselves are not silent — a charge landing and a
        /// pulse that takes a fifth of a regiment still speak, because those are
        /// events inside the fight rather than the fight going on.
        /// </para>
        /// </remarks>
        private sealed class Engagement
        {
            public int LastTick;
            public int Began;
            public int Pulses;
            public int LostByFirst;
            public int LostBySecond;
        }

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
            CloseTheGaps(battle);

            if (tick % PulseIntervalTicks != 0) return;

            // Gather the pairs first, then resolve. Both sides of an exchange
            // must be worked out from the same starting strengths, or whoever
            // happens to be resolved first gets a free advantage.
            var exchanges = new List<(UnitInstance A, UnitInstance B, bool Charge, Engagement Fight)>();

            foreach (UnitInstance unit in battle.UnitsOnField())
            {
                unit.EnemiesInContact = 0;
                unit.ClaimedFrontage = 0f;
            }

            for (int i = 0; i < _touched.Count; i++)
            {
                long pair = _touched[i];

                UnitInstance unit = battle.Get(new UnitId((int)(pair >> 32)));
                UnitInstance other = battle.Get(new UnitId((int)(pair & 0xFFFFFFFF)));

                if (!unit.IsFighting || !other.IsFighting) continue;

                bool fresh = !_engaged.TryGetValue(pair, out Engagement? fight);

                if (fresh)
                {
                    fight = new Engagement { Began = tick };
                    _engaged[pair] = fight;
                }

                fight!.LastTick = tick;
                fight.Pulses++;

                unit.EnemiesInContact++;
                other.EnemiesInContact++;

                // Both sides of the same contact, from each one's own side of
                // it: how wide a front each is trying to use against the other.
                // Totalled here so that every exchange this pulse is worked out
                // against the same picture of how crowded each regiment is.
                unit.ClaimedFrontage += EngagedWidth(unit, other);
                other.ClaimedFrontage += EngagedWidth(other, unit);

                exchanges.Add((unit, other, fresh, fight));
            }

            _touched.Clear();

            // Announced only once every pair has added its claim above. How many
            // men a regiment brings depends on how crowded its frontage is, and
            // that is not known until the whole loop has run — so reporting a
            // meeting as it was found printed figures worked out from a half
            // built picture, and the line said one thing while the exchange four
            // lines later did another. A log that quietly disagrees with the
            // rules it is describing is worse than no log, because it is
            // believed.
            for (int i = 0; i < exchanges.Count; i++)
            {
                (UnitInstance a, UnitInstance b, bool fresh, _) = exchanges[i];

                if (fresh) ReportTheMeeting(a, b, tick, log);
            }

            ForgetReformedPairs(battle, tick, log);

            // Once per unit per pulse, not once per exchange. Charging it per
            // exchange made the cost grow with the square of the attackers and
            // stripped a surrounded regiment of all cohesion inside half a turn.
            foreach (UnitInstance unit in battle.UnitsOnField())
            {
                if (unit.EnemiesInContact > 1)
                    unit.Organization -= SurroundedDisorderPerPulse * (unit.EnemiesInContact - 1);
            }

            foreach ((UnitInstance a, UnitInstance b, bool charge, Engagement fight) in exchanges)
                Resolve(battle, a, b, charge, fight, log);
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

        private void Resolve(
            BattleState battle, UnitInstance a, UnitInstance b, bool charge, Engagement fight, IBattleLog log)
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

            fight.LostByFirst += lossesToA;
            fight.LostBySecond += lossesToB;

            a.MeleePulses++;
            b.MeleePulses++;

            // The fight itself was announced when it began and is tallied when
            // it ends. What is left to say here is only what an ordinary
            // exchange does not do: a charge landing, or a pulse that takes a
            // real bite out of somebody. Reporting every exchange filled a
            // recording with six lines a turn per pair, all of them saying that
            // two regiments were still fighting.
            bool worthSaying =
                aCharges || bCharges ||
                BitOffAWhat(lossesToA, strengthA) || BitOffAWhat(lossesToB, strengthB);

            if (worthSaying)
            {
                log.Decision("Combat",
                    $"{a.Def.DisplayName} ({aFighting} fighting) and {b.Def.DisplayName} ({bFighting} fighting) " +
                    $"exchange: {a.Def.DisplayName} loses {lossesToA}, {b.Def.DisplayName} loses {lossesToB}." +
                    (aCharges ? $" Charge lands: {a.Def.DisplayName} rides in." : string.Empty) +
                    (bCharges ? $" Charge lands: {b.Def.DisplayName} rides in." : string.Empty),
                    a.Id);
            }

            ReportDestruction(a, log);
            ReportDestruction(b, log);
        }

        /// <summary>A pulse that took a real bite out of a regiment.</summary>
        /// <remarks>
        /// A twentieth in one exchange. Ordinary line fighting costs a few men
        /// out of hundreds, so anything at this scale is a charge, a flank, or a
        /// fight that has gone badly wrong for somebody — all worth a line.
        /// </remarks>
        /// <remarks>
        /// The comparison is multiplied out rather than divided. Written as
        /// <c>lost >= had / 20</c> it is integer division, so for a regiment
        /// under twenty men the right-hand side is zero and a pulse in which
        /// <i>nobody died</i> counted as a heavy one. A regiment ground down to
        /// a handful is how most long fights end, so that is not a corner — it
        /// would have put the per-exchange chatter back exactly where the fight
        /// is most drawn out.
        /// </remarks>
        private static bool BitOffAWhat(int lost, int had) => lost > 0 && lost * 20 >= had;

        /// <summary>
        /// Which quarter one regiment is fighting another from, in words.
        /// </summary>
        private static string Quarter(UnitInstance defender, UnitInstance attacker)
        {
            float off = OffFront(defender, attacker);

            if (off <= FrontalArc) return "square on to its front";
            return off >= 0.75f ? "into its rear" : "into its flank";
        }

        /// <summary>
        /// Says that two regiments have met, and from which quarter.
        /// </summary>
        /// <remarks>
        /// The line a reader actually wants out of a battle, and the one that
        /// was missing: whether a fight is a frontal grind or somebody being
        /// taken in the side is the single fact that decides how it will go, and
        /// it was nowhere in a recording. It had to be inferred from casualty
        /// figures several lines later, by which point the manoeuvre that earned
        /// it has scrolled past.
        /// </remarks>
        private static void ReportTheMeeting(UnitInstance a, UnitInstance b, int tick, IBattleLog log)
        {
            string aOn = Quarter(b, a);
            string bOn = Quarter(a, b);

            // Both square on to each other is the ordinary case and needs no
            // remarking beyond the fact of it. Anything else is a manoeuvre that
            // somebody earned, and says so.
            bool plain = aOn == "square on to its front" && bOn == "square on to its front";

            string how = plain
                ? "front to front"
                : $"{a.Def.DisplayName} {aOn}, {b.Def.DisplayName} {bOn}";

            log.Info("Combat",
                $"{a.Def.DisplayName} ({a.Strength} men) meets {b.Def.DisplayName} ({b.Strength} men) " +
                $"at tick {tick} — {how}. Bringing {FightingMen(a, b)} against {FightingMen(b, a)}.",
                a.Id);
        }

        /// <summary>
        /// Says what a fight cost, once it is over.
        /// </summary>
        private static void ReportTheTally(
            UnitInstance a, UnitInstance b, Engagement fight, int tick, IBattleLog log)
        {
            if (fight.LostByFirst == 0 && fight.LostBySecond == 0) return;

            log.Info("Combat",
                $"{a.Def.DisplayName} and {b.Def.DisplayName} are out of contact after " +
                $"{tick - fight.Began} ticks and {fight.Pulses} exchanges — " +
                $"{a.Def.DisplayName} lost {fight.LostByFirst}, {b.Def.DisplayName} lost {fight.LostBySecond}.",
                a.Id);
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

            // Everybody else in on this enemy is somebody standing with us. The
            // count is taken from the enemy's side of the fight, which is
            // exactly "how many of us are on them", and it is already final by
            // the time any exchange is resolved.
            int alongside = enemy.EnemiesInContact - 1;

            if (alongside > 0)
                shock /= 1f + ShoulderToShoulderRelief * alongside;

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
        /// <para>
        /// The division is <b>by shared ground, not by heads</b>. Each enemy is
        /// answered in proportion to the width it is actually engaging, and the
        /// shares are only scaled down where they add up to more line than the
        /// regiment has — see <see cref="UnitInstance.ClaimedFrontage"/>.
        /// Attackers on sixty and forty metres of a hundred-metre front are met
        /// with sixty and forty.
        /// </para>
        /// <para>
        /// Dividing by the number of enemies instead counted the geometry twice.
        /// Two regiments side by side each overlap about half a defender's
        /// front, so halving again gave the defender a quarter of its line
        /// against each and an eighth of the fight it should have had. Measured
        /// before the correction: two attackers brought 220 men to bear against
        /// 108, and turned an even exchange into better than six to one.
        /// </para>
        /// </remarks>
        public static int FightingMen(UnitInstance unit, UnitInstance enemy)
        {
            float contactWidth = MathF.Min(
                EngagedWidth(unit, enemy) * CrowdingShare(unit),
                MenTurnedThatWay(unit, enemy));

            Formation formation = unit.Formation;

            int frontRank = (int)MathF.Floor(contactWidth / formation.FileWidth);
            frontRank = Math.Min(frontRank, unit.Strength);

            // Holes in the line that nobody has stepped into yet.
            frontRank -= (int)MathF.Round(unit.FrontRankGaps);

            if (frontRank <= 0) return 0;

            int behind = Math.Max(0, unit.Strength - frontRank);
            int canSupport = Math.Min(behind, frontRank * SupportingRanks);

            return frontRank + (int)MathF.Round(canSupport * SupportingRankContribution);
        }

        /// <summary>
        /// What fraction of the width it wants a regiment actually gets, once
        /// every enemy on it has asked for a share.
        /// </summary>
        /// <remarks>
        /// One whenever there is line enough to go round, which is the ordinary
        /// case for enemies drawn up beside each other — the geometry has
        /// already divided them and nothing further is owed. Below one only
        /// where the claims genuinely overlap.
        ///
        /// A regiment nobody has measured yet — asked about outside a combat
        /// pulse, which the tests do — has claimed nothing, and is treated as
        /// having its whole front to itself.
        /// </remarks>
        private static float CrowdingShare(UnitInstance unit)
        {
            float claimed = unit.ClaimedFrontage;
            float own = unit.Footprint.Width;

            return claimed > own && own > 0f ? own / claimed : 1f;
        }

        /// <summary>
        /// How far off a unit's own front a threat is coming from, 0 for
        /// straight ahead, 0.5 for square on a flank and 1 for straight behind.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Every rule about being caught out of position reads this one number —
        /// how much of a line can answer, how much harder the blow lands, how
        /// frightening it is — so getting it wrong is wrong three times over,
        /// and it was.
        /// </para>
        /// <para>
        /// It used to be the bearing from one centre to the other. For a
        /// rectangle forty metres wide and six deep that says almost nothing:
        /// two regiments drawn up shoulder to shoulder against a front stand
        /// twenty-two metres either side of its middle and nine metres ahead of
        /// it, which reads as sixty-eight degrees round — past the flanking arc.
        /// So a pair attacking a front squarely were each scored as flankers,
        /// each collected the envelop bonus and a thirty-eight per cent flanking
        /// multiplier, and each had its own line discounted by a quarter for
        /// facing the wrong way. All four rectangles were square on to each
        /// other at the time.
        /// </para>
        /// <para>
        /// Now taken from the nearest point of the enemy's shape — where the
        /// fighting actually is — and measured against how far this regiment
        /// reaches each way, so that the angle is judged on a body that is
        /// mostly front rather than on a circle. Ask the rectangle, not its
        /// middle.
        /// </para>
        /// </remarks>
        /// <summary>
        /// How long a hole in the front rank waits for the man behind it, in
        /// ticks.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A <b>latency</b>, and deliberately not a rate. Filling gaps at so
        /// many men a tick sets a constant refill racing a constant rate of
        /// loss, and one of them always wins: above the loss rate every gap
        /// closes before anything looks at it and the rule is dead weight that
        /// reads as though it works; below it the holes accumulate for the whole
        /// battle with nothing to stop them, so a regiment in a long fight ends
        /// up with a front rank of nobody. Measured, at a fiftieth of a rank per
        /// tick: gaps never once left zero. At a five-hundredth: they climbed
        /// past a fifth of the rank and were still climbing.
        /// </para>
        /// <para>
        /// There is no figure between those that behaves, because the shape is
        /// wrong. A gap is not a stock being drained — it is a casualty waiting
        /// on the man behind him, and what governs it is how long he takes to
        /// step up. Expressed that way the holes settle at the rate men are
        /// falling times how long each takes to replace: small in a grind, ugly
        /// after a charge, and self-limiting in both cases.
        /// </para>
        /// <para>
        /// Ten ticks — the ranks close between one exchange and the next.
        /// </para>
        /// </remarks>
        private const float StepUpTicks = 10f;

        /// <summary>
        /// Steps men up from the ranks behind into the holes in front of them.
        /// </summary>
        /// <remarks>
        /// Every tick rather than every pulse, because filling a gap is the fast
        /// thing here and the exchange is the slow one. Walked in ascending unit
        /// id like everything else in the rules.
        /// </remarks>
        private static void CloseTheGaps(BattleState battle)
        {
            foreach (UnitInstance unit in battle.UnitsOnField())
            {
                if (unit.FrontRankGaps <= 0f) continue;

                float inTheFrontRank = unit.FightingFrontage / unit.Formation.FileWidth;

                // A regiment cannot be missing more of its front rank than it
                // has front rank, however badly it has been handled.
                if (unit.FrontRankGaps > inTheFrontRank)
                    unit.FrontRankGaps = inTheFrontRank;

                // Nothing behind the front rank, so nothing to fill with. This
                // is what being worn down actually costs: not that the survivors
                // fight worse, but that nobody steps over them, so every hole
                // torn in the line stays torn.
                if (unit.OccupiedRanks <= 1) continue;

                unit.FrontRankGaps -= unit.FrontRankGaps / StepUpTicks;

                if (unit.FrontRankGaps < 0.5f) unit.FrontRankGaps = 0f;
            }
        }

        /// <summary>
        /// How wide a line of men this regiment actually has pointing at that
        /// enemy, in metres — the ceiling on what any amount of contact is
        /// worth.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Two separate things cap it, and they are easy to confuse. Contact is
        /// measured between blocks, because blocks are what touch. What that
        /// contact is <i>worth</i> is measured in men, because men are what
        /// fight — and the block is two to one so that a regiment can be seen,
        /// which is a statement about the screen and not about the field.
        /// </para>
        /// <para>
        /// Facing the enemy, the ceiling is the frontage still manned: a
        /// regiment at half strength fights along half its block and its flanks
        /// are thin air. Caught side-on, the ceiling is its <i>ranks</i> — ten
        /// men for a ten-rank body, four metres of them, against the hundred it
        /// would have brought facing the right way.
        /// </para>
        /// <para>
        /// Reading the block for this instead let a flanked regiment answer with
        /// twenty metres of side rather than six, because the block is drawn
        /// deep enough to click on. That made being caught out of position three
        /// times less bad than it should be, from a decision about drawing.
        /// </para>
        /// </remarks>
        private static float MenTurnedThatWay(UnitInstance unit, UnitInstance enemy)
        {
            Formation formation = unit.Formation;

            return OffFront(unit, enemy) > FrontalArc
                ? formation.Ranks * formation.FileWidth
                : unit.FightingFrontage;
        }

        private static float OffFront(UnitInstance unit, UnitInstance enemy)
        {
            OrientedRect ours = unit.Shape;
            Vec2 offset = enemy.Shape.ClosestPointTo(ours.Centre) - ours.Centre;

            float ahead = Vec2.Dot(offset, ours.Forward)
                        / MathF.Max(0.01f, ours.Footprint.HalfDepth);

            float aside = MathF.Abs(Vec2.Dot(offset, ours.Right))
                        / MathF.Max(0.01f, ours.Footprint.HalfWidth);

            return MathF.Atan2(aside, ahead) / MathF.PI;
        }

        /// <summary>
        /// Beyond this far off its front, a regiment has no formed line facing
        /// the threat and can be enveloped.
        /// </summary>
        /// <remarks>
        /// A quarter turn: past forty-five degrees round the shape, which is a
        /// genuine flank rather than a neighbour standing along the same front.
        /// </remarks>
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

            // Along the enemy's own line, rather than square to the line joining
            // the two centres. The old axis swung with position: two regiments
            // set against one front stand off to either side, so the line to
            // each of them ran at forty-odd degrees and the frontage was
            // measured on the diagonal. Every attacker that was not exactly
            // opposite its enemy was credited with more front than it owns, and
            // the error grew with how far out it had to stand.
            //
            // A regiment's frontage lies along its front. That is not a
            // function of where anybody else is standing.
            Vec2 across = enemy.Shape.Right;

            float centre = Vec2.Dot(unit.Position, across);
            float enemyCentre = Vec2.Dot(enemy.Position, across);

            // Measured off the ground the men really stand on, not the block.
            // Projecting a rectangle onto a skewed axis picks up its depth as
            // well as its width, so a body meeting another at an angle claimed a
            // frontage wider than it owns — a forty-metre regiment shared 42.7 m
            // with a forty-metre enemy, and a pair attacking one front brought
            // 320 men where one alone brought 158. Frontage is a width. Nothing
            // about how deep a regiment stands belongs in it, and least of all
            // the two-to-one shape it is drawn at so it can be clicked on.
            float reach = unit.SpaceShape.ProjectedRadius(across);
            float enemyReach = enemy.SpaceShape.ProjectedRadius(across);

            float low = MathF.Max(centre - reach, enemyCentre - enemyReach);
            float high = MathF.Min(centre + reach, enemyCentre + enemyReach);

            return MathF.Max(0f, high - low);
        }

        /// <summary>
        /// Turns damage into whole men, keeping the change.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Rounding each exchange on its own discarded whatever did not come to
        /// a whole body, and everything under half a man discarded all of it. A
        /// weak attack therefore did not do a little damage slowly — it did none
        /// at all, for ever, and could be ignored outright by whoever it was
        /// aimed at. That is not a rounding nicety; it is a hole an opponent can
        /// stand in.
        /// </para>
        /// <para>
        /// The remainder is carried on the regiment being hit and paid off on a
        /// later pulse. Nothing is thrown away, the long-run total is exactly
        /// the damage dealt, and several attackers too weak to kill anybody on
        /// their own now add up with each other instead of each rounding
        /// separately to nothing.
        /// </para>
        /// <para>
        /// What the cap refuses is written off rather than carried. A pulse
        /// cannot kill more men than stand in the fight, and holding the
        /// overspill would be the fairer answer — but only if it can be paid.
        /// Against a regiment whose front rank is momentarily empty there is
        /// nothing to pay it with, so the debt would climb pulse after pulse and
        /// then fall due all at once as a regiment dying in a single exchange
        /// for damage dealt minutes earlier. The carry stays under one man,
        /// always, and cannot ambush anybody.
        /// </para>
        /// </remarks>
        internal static int BodiesOwed(UnitInstance defender, float raw, int mostThatCanFall)
        {
            if (raw > 0f) defender.CasualtyDebt += raw;

            int whole = (int)MathF.Floor(defender.CasualtyDebt);

            if (whole <= 0) return 0;

            defender.CasualtyDebt -= whole;

            return Math.Min(whole, Math.Max(0, mostThatCanFall));
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
                        // A man who costs three footmen is three times the
                        // trouble to put down — he is better armoured, better
                        // trained, and quite possibly on a horse.
                        //
                        // Without this, every regiment covering the same ground
                        // meant expensive troops were strictly worse: a body of
                        // three hundred and fifty riders and one of eight
                        // hundred swordsmen took the same casualties in men, so
                        // the riders lost twice the fraction and broke first.
                        // Cavalry lost to a square it is supposed to be unable
                        // to break. Costing casualties in what the men are
                        // worth rather than in bodies makes a rectangle a
                        // rectangle whatever is standing in it.
                        / MathF.Max(0.1f, defender.Def.Get(UnitAttributes.CostPerMan))
                        * battle.Rng.NextVariance(charge ? ChargeVariance : CasualtyVariance);

            // A pulse cannot kill more men than are actually standing in the
            // fight, however lopsided the odds. What it could not collect stays
            // owed rather than being written off.
            return BodiesOwed(defender, raw, defenderFighting);
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
        private void ForgetReformedPairs(BattleState battle, int tick, IBattleLog log)
        {
            _reformed.Clear();

            foreach (KeyValuePair<long, Engagement> entry in _engaged)
            {
                if (tick - entry.Value.LastTick < ChargeReformTicks) continue;

                var first = battle.Get(new UnitId((int)(entry.Key >> 32)));
                var second = battle.Get(new UnitId((int)(entry.Key & 0xFFFFFFFF)));

                // A regiment that has left the field has plainly disengaged.
                if (!first.IsFighting || !second.IsFighting ||
                    OrientedRect.GapBetween(first.Shape, second.Shape) > ChargeReformMetres)
                    _reformed.Add(entry.Key);
            }

            // Sorted before anything is said. The walk above is in hash order,
            // which was harmless while this method only removed entries — the
            // same set goes whatever sequence they are visited in. Reporting a
            // fight as it closes changes that: log lines are ordered output, and
            // two fights ending on the same tick would be written in whichever
            // order the dictionary felt like. A recording of a deterministic
            // battle has to be deterministic too, or comparing two runs of the
            // same seed turns up differences that are not there.
            _reformed.Sort();

            for (int i = 0; i < _reformed.Count; i++)
            {
                long key = _reformed[i];
                Engagement fight = _engaged[key];

                ReportTheTally(
                    battle.Get(new UnitId((int)(key >> 32))),
                    battle.Get(new UnitId((int)(key & 0xFFFFFFFF))),
                    fight, tick, log);

                _engaged.Remove(key);
            }
        }
    }
}
