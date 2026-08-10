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

                if (unit.Route == null || unit.Route.IsComplete)
                {
                    TurnToFaceTheFight(battle, unit);
                    continue;
                }

                StepUnit(battle, unit, tick, log);
            }
        }

        /// <summary>
        /// Brings a halted regiment round to face whatever is fighting it.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Facing used to be set only while marching, so a unit that stopped
        /// kept whatever bearing it happened to halt on — for the rest of the
        /// battle. A regiment that arrived at an angle then fought permanently
        /// flanked, taking up to twice the casualties it should, and nothing
        /// anywhere said why. Cavalry charging home, overshooting slightly and
        /// stopping side-on was losing to swordsmen it beats comfortably.
        /// </para>
        /// <para>
        /// This is also what makes flanking a manoeuvre rather than a lottery.
        /// A flank attack should be worth something because you got round them
        /// faster than they could come about — which is exactly what turn rate
        /// is for, and why a pike block at two and a half degrees a second is
        /// so much easier to catch than cavalry at seven. Without it, the bonus
        /// went to whoever happened to stop on a lucky bearing.
        /// </para>
        /// <para>
        /// At the ordinary turn rate, not the halted pivot bonus. Coming about
        /// with an enemy already among you is the hardest way to do it.
        /// </para>
        /// </remarks>
        private static void TurnToFaceTheFight(BattleState battle, UnitInstance unit)
        {
            if (unit.EnemiesInContact <= 0) return;

            UnitInstance? enemy = NearestEnemyInContact(battle, unit);
            if (enemy == null) return;

            Vec2 toEnemy = enemy.Position - unit.Position;
            if (toEnemy.IsNearZero) return;

            float turnThisTick = unit.Def.Get(UnitAttributes.TurnRate) * BattleClock.SecondsPerTick;

            unit.Facing = Facing.RotateTowards(
                unit.Facing, Facing.FromVector(toEnemy), turnThisTick * MathF.PI / 180f);
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

        private static UnitInstance? NearestEnemyInContact(BattleState battle, UnitInstance unit)
        {
            UnitInstance? nearest = null;
            float bestSquared = float.MaxValue;

            foreach (UnitInstance other in battle.UnitsOnField())
            {
                if (other.Owner == unit.Owner) continue;
                if (!other.IsFighting) continue;
                if (!OrderSystem.InContactWith(unit, other)) continue;

                float squared = Vec2.DistanceSquared(unit.Position, other.Position);

                if (squared < bestSquared)
                {
                    bestSquared = squared;
                    nearest = other;
                }
            }

            return nearest;
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

            desired = unit.Order.Kind == OrderKind.Move
                ? unit.Order.Bearing ?? unit.Facing
                : marchBearing;

            // Decide whether this unit is halting to come round before it turns,
            // so a halted pivot gets the faster rate it has earned.
            float offBefore = Facing.AbsoluteDelta(unit.Facing, desired) * 180f / MathF.PI;
            bool pivotingHalted = route.WheelFirst && offBefore > WheelToleranceDegrees;

            float turnRate = unit.Def.Get(UnitAttributes.TurnRate);
            if (pivotingHalted) turnRate *= PivotBonusWhileHalted;

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

            unit.Position = KeepOnTheField(battle, unit, Vec2.MoveTowards(unit.Position, route.Target, step));

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
