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
                if (unit.Route == null || unit.Route.IsComplete) continue;

                StepUnit(battle, unit, tick, log);
            }
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
            if (blocker == null) return next;

            if (!OrientedRect.TryGetSeparation(
                    new OrientedRect(next, unit.Facing, unit.Footprint), blocker.Shape, out Vec2 apart))
                return next;

            Vec2 away = apart.Normalised();
            Vec2 intended = next - unit.Position;
            Vec2 alongside = intended - away * Vec2.Dot(intended, away);

            Vec2 sidestep = alongside.IsNearZero
                ? unit.Position
                : KeepOnTheField(battle, unit, unit.Position + alongside);

            // A regiment that cannot legally stand where it already is may go
            // anywhere that gets it out, exactly as with impassable ground.
            bool stuckAlready = !battle.FormationFits(unit, unit.Position, unit.Facing);

            bool sideways = !alongside.IsNearZero
                            && FriendInTheWay(battle, unit, sidestep) == null
                            && (stuckAlready || battle.FormationFits(unit, sidestep, unit.Facing));

            if (sideways) return sidestep;

            // Said on a counter rather than every tick: a friend in the way
            // persists for as long as they stand there, and a line of it every
            // second would bury everything else.
            if (tick % 20 == 0)
                log.Blocked("Move",
                    $"{unit.Def.DisplayName} cannot get past its own {blocker.Def.DisplayName} and is " +
                    "waiting behind it — move one of them, or take it round.",
                    unit.Id);

            return unit.Position;
        }

        /// <summary>
        /// A friendly formation this step would newly stand inside, if there is
        /// one.
        /// </summary>
        /// <remarks>
        /// A unit already inside somebody is exempt for that pair. That happens
        /// — deployed overlapping, or widened into a neighbour by reshaping —
        /// and a regiment that cannot legally be where it is must still be able
        /// to walk off it, or it is stuck for the whole battle. The shuffle in
        /// <see cref="ContactSystem"/> is what resolves those.
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

                if (!OrientedRect.Overlaps(stepped, other.Shape)) continue;
                if (OrientedRect.Overlaps(here, other.Shape)) continue;

                return other;
            }

            return null;
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
                Finish(unit, tick, log);
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

            desired = unit.Order.Kind == OrderKind.Move ? unit.OrderFacing : marchBearing;

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
                if (tick % 5 == 0)
                    log.Info("Move", $"{unit.Def.DisplayName} is wheeling — {offByDegrees:0}° to come round.", unit.Id);

                return;
            }

            float terrainSpeed = battle.SpeedOf(unit);

            // Regiments bound into a wing keep the pace of the slowest of them.
            // Without it "they move together" lasts about ten seconds — cavalry
            // is three times an infantryman's speed, so a mixed wing tears
            // itself apart on the first march and arrives as separate regiments
            // at separate times, which is exactly what binding them was meant
            // to prevent.
            terrainSpeed *= battle.PaceOfBond(unit) / MathF.Max(0.01f, unit.BaseSpeed);

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
                    Finish(unit, tick, log);
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

        private static void Finish(UnitInstance unit, int tick, IBattleLog log)
        {
            log.Info("Move", $"{unit.Def.DisplayName} reached its destination at tick {tick}.", unit.Id);
            unit.Route = null;
        }
    }
}
