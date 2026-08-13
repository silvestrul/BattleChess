using System;
using BattleChess.Contracts;

namespace BattleChess.Rules
{
    /// <summary>
    /// Walks units along their routes, one tick at a time.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Units set off immediately and come round onto their bearing as they go,
    /// but a formation facing the wrong way cannot march properly — speed falls
    /// away the further off it is pointing, down to a shuffle when fully
    /// reversed. So turning is never free, without a unit ever looking like it
    /// ignored an order.
    /// </para>
    /// <para>
    /// This is what gives <c>turnRate</c> its meaning. Spearmen at 20°/s spend
    /// nine seconds wheeling about, crawling the whole time; scouts at 60°/s do
    /// it in three. That difference is precisely why cavalry goes looking for a
    /// pike block's flank rather than its front.
    /// </para>
    /// </remarks>
    public sealed class MovementSystem : IBattleSystem
    {
        /// <summary>
        /// Speed retained when facing directly away from the line of march.
        /// </summary>
        /// <remarks>
        /// Not zero: a body of men wheeling about is still edging round, and a
        /// unit frozen until aligned is what the "wheel first" order is for. A
        /// fifth of normal pace reads as shuffling into position.
        /// </remarks>
        private const float SpeedWhileFullyReversed = 0.2f;

        /// <summary>How close to the bearing counts as aligned, for wheel-first orders.</summary>
        private const float WheelToleranceDegrees = 10f;

        /// <summary>
        /// How much faster a halted unit comes round than one under way.
        /// </summary>
        /// <remarks>
        /// Troops standing still manoeuvre better than troops trying to march
        /// and change front at once — the officers can dress the ranks rather
        /// than chase them. This is what makes "wheel first" worth ordering:
        /// otherwise it is strictly worse, since turning under way at least
        /// covers a little ground while it happens.
        /// </remarks>
        private const float PivotBonusWhileHalted = 1.6f;

        /// <summary>Distance within which a waypoint counts as reached, in metres.</summary>
        private const float ArrivalTolerance = 0.5f;

        /// <summary>
        /// How much faster men run when they have stopped caring about order.
        /// </summary>
        /// <remarks>
        /// Enough that infantry can break contact with infantry, not enough to
        /// outrun cavalry. That gap is precisely what makes horse the arm that
        /// turns a rout into a catastrophe.
        /// </remarks>
        private const float FlightSpeedBonus = 1.3f;

        public string Name => "Movement";

        public void Step(BattleState battle, int tick, IBattleLog log)
        {
            if (battle == null) throw new ArgumentNullException(nameof(battle));

            // Ascending unit id, always — the ordering guarantee the whole
            // simulation's reproducibility rests on.
            foreach (UnitInstance unit in battle.UnitsOnField())
            {
                // Broken units move too — arguably they are the ones most
                // committed to moving. Skipping them left routers standing in
                // place to be cut down where they broke, which is not a rout,
                // it is an execution.
                if (!unit.IsFighting && unit.State != UnitState.Routing) continue;

                // A halted regiment holds the front it was left on. It does not
                // quietly come about to face whoever turns up, because being
                // caught pointing the wrong way is a mistake the player made and
                // ought to keep — the whole value of getting round an enemy is
                // that they are still facing the way you left them.
                //
                // What replaced it is the dressing rule below: an attack ordered
                // deliberately squares up during its final approach, so a charge
                // arrives properly aligned without anything turning on its own
                // after the fact.
                if (unit.Route == null || unit.Route.IsComplete)
                {
                    WheelOnTheSpot(unit);
                    continue;
                }

                StepUnit(battle, unit, tick, log);
            }
        }

        /// <summary>
        /// Brings a halted regiment round to the front it has been told to hold.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Not the automatic turn that used to be here. This one only ever
        /// chases the front the player asked for — a regiment given no new front
        /// stands exactly where it was left, and one taken in the flank stays
        /// flanked until somebody does something about it.
        /// </para>
        /// <para>
        /// What it makes possible is doing something about it. Facing could
        /// previously only be set as a by-product of going somewhere, so a
        /// regiment caught out of position had no way to come about at all
        /// without being marched off and back again.
        /// </para>
        /// <para>
        /// Nothing but the turn rate slows it. In particular no friendly
        /// formation does: men come about within their own frontage and do not
        /// need anybody's permission, and gating this on collisions would mean a
        /// regiment in a crowded line could never face the enemy that had got
        /// round it — which is the one position where turning matters most.
        /// </para>
        /// </remarks>
        private static void WheelOnTheSpot(UnitInstance unit)
        {
            // Men running face where they are going, and that is decided by the
            // route they are running along.
            if (unit.State == UnitState.Routing) return;

            Facing want = unit.DressingBearing ?? unit.OrderFacing;
            if (Facing.AbsoluteDelta(unit.Facing, want) < 0.001f) return;

            float rate = unit.Def.Get(UnitAttributes.TurnRate);

            // Standing troops dress ranks rather than chase them — but not with
            // an enemy already among them, which is the hardest way there is to
            // change front and should feel like it.
            if (unit.EnemiesInContact == 0) rate *= PivotBonusWhileHalted;

            unit.Facing = Facing.RotateTowards(
                unit.Facing, want, rate * BattleClock.SecondsPerTick * MathF.PI / 180f);
        }

        /// <summary>
        /// How much faster a regiment comes round while dressing onto the enemy
        /// it is charging.
        /// </summary>
        /// <remarks>
        /// The final approach is the one moment a body of men is genuinely
        /// hurrying to change front — officers dressing ranks on the run, with
        /// the enemy a minute away. At the ordinary rate a spear block needs the
        /// better part of that minute to come round ninety degrees, which is the
        /// whole approach spent wheeling and none of it spent closing.
        /// </remarks>
        private const float DressingTurnBonus = 5f;

        /// <summary>What a regiment must do about the ground in front of it.</summary>
        private enum Fit
        {
            /// <summary>It fits as it is.</summary>
            Fine,

            /// <summary>It fits at some other bearing, so come round to that.</summary>
            ComeRound,

            /// <summary>It fits at no bearing at all.</summary>
            Blocked
        }

        /// <summary>How far either way a regiment will look for a bearing that fits, in degrees.</summary>
        /// <remarks>
        /// Ninety is the whole of the useful range: at a right angle a line is
        /// presenting its depth instead of its frontage, which for a body a
        /// hundred metres wide and five deep is the narrowest it can possibly
        /// be. Anything beyond that is the same shapes again.
        /// </remarks>
        private const float SqueezeSearchDegrees = 90f;

        /// <summary>Steps the search takes, in degrees.</summary>
        private const float SqueezeStepDegrees = 6f;

        /// <summary>
        /// Decides whether a regiment can carry on as it is, has to turn to get
        /// through, or cannot get through at all.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The nearest fitting bearing wins, so a unit turns as little as it
        /// must. It searches outward in both directions at once and takes the
        /// first success, which means a wide line squeezing between two woods
        /// comes round just far enough to clear them and no further.
        /// </para>
        /// <para>
        /// A regiment already standing somewhere it does not fit is exempt
        /// entirely. That happens — deployed onto bad ground, or left there by
        /// an older rule — and a unit that cannot legally be where it is must
        /// still be able to walk off it, or it is stuck for the whole battle.
        /// </para>
        /// </remarks>
        private static Fit ChooseBearingToFit(
            BattleState battle, UnitInstance unit, MovementRoute route, out Facing bearing)
        {
            bearing = unit.Facing;

            if (!battle.FormationFits(unit, unit.Position, unit.Facing)) return Fit.Fine;

            float reach = battle.SpeedOf(unit) * BattleClock.SecondsPerTick;
            Vec2 probe = KeepOnTheField(battle, unit, Vec2.MoveTowards(unit.Position, route.Target, reach));

            if (battle.FormationFits(unit, probe, unit.Facing)) return Fit.Fine;

            for (float off = SqueezeStepDegrees; off <= SqueezeSearchDegrees; off += SqueezeStepDegrees)
            {
                float radians = off * MathF.PI / 180f;

                Facing left = unit.Facing.RotatedBy(radians);
                if (battle.FormationFits(unit, probe, left))
                {
                    bearing = left;
                    return Fit.ComeRound;
                }

                Facing right = unit.Facing.RotatedBy(-radians);
                if (battle.FormationFits(unit, probe, right))
                {
                    bearing = right;
                    return Fit.ComeRound;
                }
            }

            return Fit.Blocked;
        }

        /// <summary>
        /// Holds a formed regiment inside the map, footprint and all.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Positions are a point and regiments are rectangles, so a centre
        /// legally inside the bounds still leaves half a frontage — fifty
        /// metres of cavalry — hanging over the edge of the world. Inset by
        /// however much of the shape actually points that way, which is exact
        /// for any bearing and costs nothing.
        /// </para>
        /// <para>
        /// Routers are exempt on purpose. Leaving the field is precisely what
        /// they are doing, and holding them on it would trap broken regiments
        /// against the border forever instead of letting them scatter.
        /// </para>
        /// </remarks>
        private static Vec2 KeepOnTheField(BattleState battle, UnitInstance unit, Vec2 position)
        {
            if (unit.State == UnitState.Routing) return position;

            MapBounds bounds = battle.Terrain.Bounds;
            var shape = new OrientedRect(position, unit.Facing, unit.Footprint);

            float halfWidth = shape.ProjectedRadius(new Vec2(1f, 0f));
            float halfHeight = shape.ProjectedRadius(new Vec2(0f, 1f));

            // A regiment wider than the field can only be centred on it.
            return new Vec2(
                Squeeze(position.X, bounds.Min.X, bounds.Max.X, halfWidth),
                Squeeze(position.Y, bounds.Min.Y, bounds.Max.Y, halfHeight));
        }

        private static float Squeeze(float value, float min, float max, float halfExtent)
        {
            if (max - min <= 2f * halfExtent) return (min + max) * 0.5f;

            return Math.Clamp(value, min + halfExtent, max - halfExtent);
        }

        /// <summary>
        /// Keeps a regiment out of its own side, edging it along a friendly
        /// formation rather than through it.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Two bodies of men cannot stand in the same field, and until now
        /// nothing said so — friendly formations interpenetrated freely and paid
        /// only a trickle of cohesion for it. So a line was never really a line:
        /// regiments ordered along the same axis slid into one another and the
        /// front the player had drawn stopped meaning anything.
        /// </para>
        /// <para>
        /// Refusing the step outright would be worse than the disease. An army
        /// is a crowd, and regiments brush past each other constantly at glancing
        /// angles; a hard stop on every touch would jam a line solid the first
        /// time two units converged. So only the part of the step that pushes
        /// into the friend is taken away, and whatever is left along their flank
        /// is kept — which is how men actually get past each other.
        /// </para>
        /// <para>
        /// Marching squarely into somebody's back leaves nothing to slide along,
        /// and that is correct: it stops, and says so. Getting round is then the
        /// player's problem, which is the whole point of the rule.
        /// </para>
        /// </remarks>
        private static Vec2 MakeRoomForFriends(
            BattleState battle, UnitInstance unit, Vec2 next, int tick, IBattleLog log)
        {
            // Men running do not form up and do not politely go round, and
            // damming a rout against its own reserves would turn a withdrawal
            // into a massacre.
            if (unit.State == UnitState.Routing) return next;

            UnitInstance? blocker = FriendInTheWay(battle, unit, next);

            if (blocker == null)
            {
                unit.GoingRound = UnitId.None;
                return next;
            }

            // Whoever has to give way. A regiment already standing where it
            // means to stand is not shoved off it to make room for one still
            // walking, and one at grips with an enemy is not pulled out of the
            // fight — so the mover goes round, and between two movers the higher
            // number yields. Arbitrary, total, and reproducible from the seed,
            // which is what matters: without a rule they either both give way
            // and drift, or neither does and they jam.
            if (!MustGiveWayTo(unit, blocker)) return next;

            var stepped = new OrientedRect(next, unit.Facing, unit.Footprint);

            // Which way is "off them". Overlapping, the shortest way out of the
            // overlap; merely too close, the line from their nearest point to
            // our centre — because a regiment keeping its distance has no
            // overlap to be pushed out of, and asking for one gives no answer
            // at all. That was why the berth found its blocker and then failed
            // to do anything about it.
            Vec2 away = OrientedRect.TryGetSeparation(stepped, blocker.Shape, out Vec2 apart)
                ? apart
                : stepped.Centre - blocker.Shape.ClosestPointTo(stepped.Centre);

            if (away.IsNearZero) away = unit.Position - blocker.Position;
            if (away.IsNearZero) return next;

            away = away.Normalised();

            Vec2 intended = next - unit.Position;
            Vec2 alongside = intended - away * Vec2.Dot(intended, away);

            float reach = intended.Length;
            if (reach <= 0f) return unit.Position;

            // A regiment that cannot legally stand where it already is may go
            // anywhere that gets it out, exactly as with impassable ground.
            bool stuckAlready = !battle.FormationFits(unit, unit.Position, unit.Facing);

            // Glancing contact: keep whatever part of the step runs along their
            // flank and drop the part that pushes into them.
            //
            // Judged as a real fraction of the step rather than merely non-zero.
            // A regiment marching dead into somebody's back leaves a tangential
            // component of about a ten-thousandth of a metre, which is not zero
            // and is not movement either — it took this branch, edged forward by
            // nothing, and did it again every tick forever. From the player's
            // chair that is indistinguishable from the hard stop this was
            // written to avoid.
            if (alongside.Length > reach * SlideIsWorthTaking)
            {
                Vec2 sidestep = KeepOnTheField(battle, unit, unit.Position + alongside);

                if (FriendInTheWay(battle, unit, sidestep) == null &&
                    (stuckAlready || battle.FormationFits(unit, sidestep, unit.Facing)))
                    return sidestep;
            }

            // Squarely into their back, so there is nothing worth sliding along.
            // Go round instead of standing there — a regiment that has been
            // ordered somewhere and simply stops because one of its own is in
            // the way is a regiment that has stopped taking orders, whatever the
            // log says about why.
            //
            // Across the line of march, toward the nearer end of the formation
            // in the way, so the detour is the short way round and gets shorter
            // every tick.
            //
            // Measured against the march rather than against the shortest way
            // out of the overlap, which is the version that did not work. The
            // shortest way out swings between the blocker's faces as the two
            // rectangles slide past each other, so the detour reversed itself
            // every few ticks: a recorded run had cavalry cover thirty-eight
            // metres in its first turn and then rock between two positions eight
            // metres apart for the next seven. The line of march does not swing.
            Vec2 pastTheirFlank;

            // Which way round is settled once and then kept until this blocker
            // is behind us. Deciding it afresh every tick is what produced the
            // seizure: a regiment sitting on the blocker's centreline gets a
            // different answer each time it asks, and thrashes between the two.
            if (unit.GoingRound == blocker.Id)
            {
                pastTheirFlank = unit.GoingRoundBearing.ToVector();
            }
            else
            {
                Vec2 heading = intended / reach;
                pastTheirFlank = new Vec2(-heading.Y, heading.X);

                // Toward whichever end of them we are already nearer, so the
                // detour is the short way round.
                if (Vec2.Dot(unit.Position - blocker.Position, pastTheirFlank) < 0f)
                    pastTheirFlank = -pastTheirFlank;

                unit.GoingRound = blocker.Id;
                unit.GoingRoundBearing = Facing.FromVector(pastTheirFlank);
            }

            for (int attempt = 0; attempt < 2; attempt++)
            {
                // The committed side first; the other only if that way is
                // blocked as well, which is what happens threading a packed
                // line.
                Vec2 thisWay = attempt == 0 ? pastTheirFlank : -pastTheirFlank;

                Vec2 round = KeepOnTheField(battle, unit, unit.Position + thisWay * reach);

                if (FriendInTheWay(battle, unit, round) != null) continue;
                if (!stuckAlready && !battle.FormationFits(unit, round, unit.Facing)) continue;

                // If the far side was the one that worked, that is the new
                // commitment — otherwise the next tick starts from the blocked
                // side again and nothing has been learned.
                unit.GoingRoundBearing = Facing.FromVector(thisWay);

                log.Decision("Move",
                    $"{unit.Def.DisplayName} is working round its own {blocker.Def.DisplayName} " +
                    "rather than through it.",
                    unit.Id);

                return round;
            }

            // Boxed in on both sides as well as in front. Said on a counter
            // rather than every tick: it persists for as long as they all stand
            // there, and a line of it every second would bury everything else.
            log.Blocked("Move",
                $"{unit.Def.DisplayName} is hemmed in by its own {blocker.Def.DisplayName} with no way " +
                "round either flank — move one of them.",
                unit.Id);

            return unit.Position;
        }

        /// <summary>
        /// How much of a step must survive along a friend's flank before edging
        /// past them counts as movement rather than as being stuck.
        /// </summary>
        private const float SlideIsWorthTaking = 0.5f;

        /// <summary>
        /// Whether this regiment is the one that has to make way.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Read top to bottom: a regiment that has arrived is not shoved off its
        /// ground by one still walking; a regiment at grips with an enemy is not
        /// pulled out of the fight to let somebody past; and between two that
        /// are both merely marching, the higher number gives way.
        /// </para>
        /// <para>
        /// The last is arbitrary, and that is the point — it is total and it is
        /// reproducible from the seed, which any rule about who moves has to be.
        /// Without one, two regiments converging on the same ground either both
        /// give way and drift, or neither does and they jam.
        /// </para>
        /// </remarks>
        private static bool MustGiveWayTo(UnitInstance unit, UnitInstance other)
        {
            if (!other.IsMarching) return true;
            if (!unit.IsMarching) return false;

            if (other.EnemiesInContact > 0 && unit.EnemiesInContact == 0) return true;
            if (unit.EnemiesInContact > 0 && other.EnemiesInContact == 0) return false;

            return unit.Id.Value > other.Id.Value;
        }

        /// <summary>
        /// How much clear ground a regiment leaves when it passes one of its
        /// own, in metres.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Charged against passing, never against standing. Regiments drawn up
        /// in line stand flush against each other — that is what a line is, and
        /// it is the whole point of being able to handle a big army regiment by
        /// regiment and then move the result as one body. A berth applied
        /// everywhere would put daylight between every pair of neighbours and
        /// make a proper line impossible to form.
        /// </para>
        /// <para>
        /// While actually going past somebody it is different. Without it a
        /// regiment grazes along its neighbour's flank at under two metres for
        /// the length of the manoeuvre, which is what "they stick to the
        /// infantry" was.
        /// </para>
        /// </remarks>
        private const float BerthWhilePassingMetres = 6f;

        /// <summary>
        /// A friendly formation this step would newly crowd, if there is one.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Corners clipping is not a collision. An army in line brushes
        /// constantly, so anything under a twentieth of a regiment is ignored
        /// outright — the same tolerance the placement search uses, so that
        /// where a regiment is willing to stand and what it is willing to walk
        /// through are one decision rather than two that can disagree.
        /// </para>
        /// <para>
        /// A unit already lapping somebody is exempt for that pair. That happens
        /// — deployed overlapping, or widened into a neighbour by reshaping —
        /// and a regiment that cannot legally be where it is must still be able
        /// to walk off it, or it is stuck for the whole battle. The shuffle in
        /// <see cref="ContactSystem"/> is what resolves those.
        /// </para>
        /// </remarks>
        private static UnitInstance? FriendInTheWay(BattleState battle, UnitInstance unit, Vec2 next)
        {
            var stepped = new OrientedRect(next, unit.Facing, unit.Footprint);
            OrientedRect here = unit.Shape;

            foreach (UnitInstance other in battle.UnitsOnField())
            {
                if (other.Id == unit.Id) continue;
                if (other.Owner != unit.Owner) continue;
                if (!other.IsFighting) continue;
                if (other.State == UnitState.Routing) continue;

                // Already lapping them, so this step cannot be what did it.
                if (OrientedRect.OverlapFraction(here, other.Shape) > OrderSystem.GrazingTolerance) continue;

                if (OrientedRect.OverlapFraction(stepped, other.Shape) > OrderSystem.GrazingTolerance)
                    return other;

                // Not touching them, but closing to within a hand's breadth
                // while going somewhere — which is gluing itself to them rather
                // than passing them. Only while marching: a regiment that has
                // arrived beside a neighbour stands flush.
                if (unit.IsMarching &&
                    !ComingToRestBeside(unit, other) &&
                    OrientedRect.GapBetween(stepped, other.Shape) < BerthWhilePassingMetres &&
                    OrientedRect.GapBetween(here, other.Shape) >= BerthWhilePassingMetres)
                    return other;
            }

            return null;
        }

        /// <summary>
        /// Whether this march <i>ends</i> beside the given friend, rather than
        /// going past them.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The berth is charged against passing. Arriving is not passing, and
        /// the two were being told apart by <c>IsMarching</c> — which is true
        /// right up to the final step, so a regiment could never actually reach
        /// a destination beside a neighbour. It closed to six metres, refused
        /// its own last stride, and stood there.
        /// </para>
        /// <para>
        /// That made the placement search and the march rule disagree about the
        /// same question. The search will happily choose ground with a five
        /// metre gap, having only checked that nothing overlaps; the march would
        /// then decline to walk the last metre onto it. The regiment shuffled
        /// north and south against the berth for half a turn until the stall
        /// detector gave up on its behalf and stopped it short.
        /// </para>
        /// <para>
        /// Asking where the order <i>ends</i> settles it. A destination inside
        /// the berth is an order to stand there, and the berth has nothing to
        /// say about it.
        /// </para>
        /// </remarks>
        private static bool ComingToRestBeside(UnitInstance unit, UnitInstance other)
        {
            if (unit.Route == null) return false;

            var atRest = new OrientedRect(unit.Route.Destination, unit.Facing, unit.Footprint);

            return OrientedRect.GapBetween(atRest, other.Shape) < BerthWhilePassingMetres;
        }

        private static void StepUnit(BattleState battle, UnitInstance unit, int tick, IBattleLog log)
        {
            MovementRoute route = unit.Route!;

            // Skip any waypoints already effectively reached, so a route whose
            // first points sit under the unit does not waste ticks.
            while (!route.IsComplete && Vec2.Distance(unit.Position, route.Target) <= ArrivalTolerance)
                route.Advance();

            if (route.IsComplete)
            {
                Finish(battle, unit, tick, log);
                return;
            }

            Vec2 toTarget = route.Target - unit.Position;
            Facing desired = Facing.FromVector(toTarget);

            // A broken unit is no longer a formation, so none of the rules about
            // wheeling apply to it. Men running do not dress ranks and come
            // round at two and a half degrees a second; they turn and go. Making
            // routers obey the turn rate meant a spear regiment needed most of a
            // minute to face away from the enemy killing it.
            if (unit.State == UnitState.Routing)
            {
                unit.Facing = desired;

                float panicSpeed = battle.SpeedOf(unit) * FlightSpeedBonus;
                unit.Position = Vec2.MoveTowards(unit.Position, route.Target, panicSpeed * BattleClock.SecondsPerTick);

                if (Vec2.Distance(unit.Position, route.Target) <= ArrivalTolerance)
                    route.Advance();

                return;
            }

            // Where it is going and where it is looking are two different
            // questions, and conflating them was what pivoted a line ninety
            // degrees for a fifty-metre sidestep. A march order leaves the
            // front where the player put it unless asked for otherwise; an
            // attack faces what it is charging, which is never in doubt.
            Facing marchBearing = desired;

            desired = unit.Order.Kind == OrderKind.Move
                ? FrontWhileMarching(battle, unit, route, marchBearing)
                : marchBearing;

            // The last hundred metres of a charge. The order system has already
            // aimed this march at a slot squarely off one of the enemy's faces;
            // this is the other half of the same manoeuvre, bringing the front
            // round to match so the two rectangles meet flush rather than at
            // whatever angle the approach happened to run.
            Facing? dressOn = unit.DressingBearing;
            bool dressing = dressOn.HasValue;
            if (dressOn.HasValue) desired = dressOn.Value;

            // A regiment is a shape, and ground it cannot cross is ground it
            // cannot be on. Carrying on as it is may put part of the formation
            // in a wood or a river that the centre misses entirely, so before
            // anything else it asks whether it still fits — and if not, whether
            // there is a bearing it could come round to that does.
            switch (ChooseBearingToFit(battle, unit, route, out Facing threading))
            {
                case Fit.ComeRound:
                    desired = threading;
                    break;

                case Fit.Blocked:
                    // Said every time, not on a tick counter. The march is
                    // abandoned on this same tick, so a throttled message is
                    // one that usually never appears at all — and a regiment
                    // that has silently stopped is indistinguishable from one
                    // that has stopped taking orders.
                    log.Blocked("Move",
                        $"{unit.Def.DisplayName} cannot get its whole frontage past " +
                        $"{battle.TerrainAt(unit.Position).DisplayName} — " +
                        $"{unit.Footprint.Width:0} m of front and no way through at any bearing.",
                        unit.Id);

                    unit.Route = null;
                    return;
            }

            // Decide whether this unit is halting to come round before it turns,
            // so a halted pivot gets the faster rate it has earned.
            float offBefore = Facing.AbsoluteDelta(unit.Facing, desired) * 180f / MathF.PI;
            bool pivotingHalted = route.WheelFirst && offBefore > WheelToleranceDegrees;

            float turnRate = unit.Def.Get(UnitAttributes.TurnRate);
            if (pivotingHalted) turnRate *= PivotBonusWhileHalted;
            if (dressing) turnRate *= DressingTurnBonus;

            float turnThisTick = turnRate * BattleClock.SecondsPerTick;
            unit.Facing = Facing.RotateTowards(unit.Facing, desired, turnThisTick * MathF.PI / 180f);

            float offByDegrees = Facing.AbsoluteDelta(unit.Facing, desired) * 180f / MathF.PI;

            if (pivotingHalted && offByDegrees > WheelToleranceDegrees)
            {
                log.Info("Move", $"{unit.Def.DisplayName} is wheeling rather than marching.", unit.Id);

                return;
            }

            // The pace of the slowest regiment in this one's wing, over the
            // ground it is actually standing on — so a wing whose left is
            // fording a river waits for it rather than arriving in two halves at
            // two different times. For a unit on its own this is just its own
            // speed here.
            float terrainSpeed = battle.PaceOfBond(unit);

            if (terrainSpeed <= 0f)
            {
                log.Blocked("Move",
                    $"{unit.Def.DisplayName} is stuck on {battle.TerrainAt(unit.Position).DisplayName}, which it cannot cross.",
                    unit.Id);

                unit.Route = null;
                return;
            }

            // Charged against the line of march, not against whatever the unit
            // is trying to face. A regiment holding its front while it
            // sidesteps is edging along at a fifth of its pace, and that price
            // is what makes keeping your facing a decision rather than a free
            // option.
            float offTheLineOfMarch = Facing.AbsoluteDelta(unit.Facing, marchBearing) * 180f / MathF.PI;

            float speed = terrainSpeed * AlignmentPenalty(offTheLineOfMarch);
            float step = speed * BattleClock.SecondsPerTick;

            // Bad ground pulls a formation apart as it is crossed. Charged per
            // metre rather than per second, because rough country is already
            // slow — billing by the second would make a regiment pay twice for
            // the same mud, and a long march through open woods would end in a
            // rabble. Crossing a river costs what the river is wide, whether
            // you wade it quickly or slowly.
            //
            // Read under the whole formation and charged at its worst, not
            // sampled at the centre. A hundred-metre line with one flank in a
            // river is in trouble across its whole front, and the man in the
            // middle standing on dry grass does not change that.
            float disorder = battle.WorstDisorderUnder(unit);

            if (disorder > 0f)
                unit.Organization -= disorder * step;

            Vec2 next = KeepOnTheField(battle, unit, Vec2.MoveTowards(unit.Position, route.Target, step));

            // The last word. A regiment part-way through coming round has a
            // bearing that does not fit yet, and stepping forward on it would
            // put the leading flank into exactly the wood it is turning to
            // avoid. It wheels on the spot until the shape clears, which is
            // what threading a gap actually looks like.
            if (battle.FormationFits(unit, next, unit.Facing) ||
                !battle.FormationFits(unit, unit.Position, unit.Facing))
                unit.Position = MakeRoomForFriends(battle, unit, next, tick, log);

            if (Vec2.Distance(unit.Position, route.Target) <= ArrivalTolerance)
            {
                route.Advance();

                if (route.IsComplete)
                    Finish(battle, unit, tick, log);
            }
        }

        /// <summary>
        /// How much of its speed a unit keeps while facing away from its line of
        /// march.
        /// </summary>
        /// <remarks>
        /// Squared cosine falloff: full pace when aligned, about three quarters
        /// at 45°, two fifths at a right angle, and a shuffle when reversed. The
        /// square is what makes small corrections nearly free while a genuine
        /// change of front is expensive.
        /// </remarks>
        private static float AlignmentPenalty(float offByDegrees)
        {
            float halfCosine = (1f + MathF.Cos(offByDegrees * MathF.PI / 180f)) * 0.5f;
            float alignment = halfCosine * halfCosine;

            return SpeedWhileFullyReversed + (1f - SpeedWhileFullyReversed) * alignment;
        }

        /// <summary>
        /// Reports an order carried out, and says enough about how it ended to
        /// diagnose it from the log alone.
        /// </summary>
        /// <remarks>
        /// This used to record only the tick. A recorded game then turned up a
        /// march that took two and a half times as long as three others of the
        /// same length and the same wheel, and nothing in the log could say why
        /// — not the ground it crossed, not the pace it kept, not which way it
        /// finished pointing. Every one of those is a line of text, and without
        /// them a play report can only be answered by guessing or by asking.
        /// </remarks>
        /// <summary>
        /// Roughly what fraction of its pace a regiment keeps while it is coming
        /// round, used only to judge how much room a wheel needs.
        /// </summary>
        private const float PaceWhileWheeling = 0.6f;

        /// <summary>Ground to spare, so the wheel is finished before arrival rather than at it.</summary>
        private const float FormingUpMarginMetres = 10f;

        /// <summary>
        /// The most of a march that may be given over to coming round onto the
        /// front it was asked to arrive on.
        /// </summary>
        /// <remarks>
        /// Reserving whatever the wheel needs is right until the wheel needs
        /// more than the march is long, and then it quietly swallows the whole
        /// order and the regiment crabs from the first step — which is the very
        /// thing this was written to stop. Recorded: 70 m asked for with a 156°
        /// change of front, which wants 74 m of run-up, so nothing was left to
        /// march properly and it covered the ground at a fifth of its pace.
        ///
        /// Short orders with a big change of front are the common case, not a
        /// corner: a regiment being nudged into position is exactly when a
        /// player is particular about which way it ends up pointing. So most of
        /// any march is spent marching, and the front is picked up over what is
        /// left — finishing on the spot after arrival if there was not enough
        /// room, which costs nothing and looks like dressing the ranks.
        /// </remarks>
        private const float MostOfAMarchIsMarching = 0.4f;

        /// <summary>
        /// The front to hold at this moment of a march.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A bearing drawn by the player is the front to <b>arrive on</b>, not
        /// one to hold the whole way there. Until the regiment is close enough
        /// to need it, it marches the way it is going, at its proper pace, and
        /// comes round at the end — which is what an attack has always done on
        /// its last hundred metres, and what everybody expects of the drag.
        /// </para>
        /// <para>
        /// Holding it throughout was a real order that nobody wanted and was
        /// easy to give by accident. Recorded: a body of horse sent 167 m on a
        /// bearing of 171° while holding a front of −80° travelled rear-first
        /// the entire way and took 122 ticks over a march worth 35, at 29% of
        /// its pace. From outside that is not a manoeuvre, it is a regiment
        /// that has gone wrong.
        /// </para>
        /// <para>
        /// The turn is started far enough out to be finished on arrival rather
        /// than at it, scaled by how far there is to come round — a quarter
        /// turn needs a fraction of the room an about-face does, and reserving
        /// the same distance for both would have a regiment crabbing most of a
        /// short march for a bearing it could pick up in twenty metres.
        /// </para>
        /// </remarks>
        private static Facing FrontWhileMarching(
            BattleState battle, UnitInstance unit, MovementRoute route, Facing marchBearing)
        {
            // No bearing was drawn, so the front already is the line of march.
            if (!unit.Order.Bearing.HasValue) return unit.OrderFacing;

            float toTurn = Facing.AbsoluteDelta(marchBearing, unit.OrderFacing) * 180f / MathF.PI;
            float turnRate = MathF.Max(1f, unit.Def.Get(UnitAttributes.TurnRate));
            float pace = MathF.Max(0.1f, battle.SpeedOf(unit));

            float roomToComeRound =
                toTurn / turnRate * pace * PaceWhileWheeling + FormingUpMarginMetres;

            // Never more than a slice of the march, however long the wheel
            // wants. What is left of the turn is finished standing still.
            float wholeMarch = Vec2.Distance(unit.OrderAnchor, route.Destination);
            roomToComeRound = MathF.Min(roomToComeRound, wholeMarch * MostOfAMarchIsMarching);

            return Vec2.Distance(unit.Position, route.Destination) > roomToComeRound
                ? marchBearing
                : unit.OrderFacing;
        }

        private static void Finish(BattleState battle, UnitInstance unit, int tick, IBattleLog log)
        {
            float offOrdered = Facing.AbsoluteDelta(unit.Facing, unit.OrderFacing) * 180f / MathF.PI;
            TerrainDef ground = battle.TerrainAt(unit.Position);

            // A regiment threading ground too narrow for its frontage holds
            // whatever bearing fits rather than the one it was given, and only
            // comes back to it once clear. Ending a march still turned is the
            // one case where that reads as an order gone wrong rather than as
            // an obstacle handled, so it is called out by name.
            string front = offOrdered > 10f
                ? $" — finished {offOrdered:0}° off the front it was given, on ground it may still be threading"
                : string.Empty;

            log.Info("Move",
                $"{unit.Def.DisplayName} reached its destination on {ground.DisplayName} " +
                $"at {battle.SpeedOf(unit):0.0} m/s{front}.",
                unit.Id);

            unit.Route = null;
        }
    }
}
