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
                if (unit.Route == null || unit.Route.IsComplete) continue;

                StepUnit(battle, unit, tick, log);
            }
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

            float speed = terrainSpeed * AlignmentPenalty(offByDegrees);
            float step = speed * BattleClock.SecondsPerTick;

            unit.Position = Vec2.MoveTowards(unit.Position, route.Target, step);

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
