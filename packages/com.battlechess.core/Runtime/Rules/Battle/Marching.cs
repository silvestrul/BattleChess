using System;
using System.Collections.Generic;
using BattleChess.Contracts;

namespace BattleChess.Rules
{
    /// <summary>
    /// Works out how a regiment is to get somewhere.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>M10.</b> A march is a cast, not a search. The question a regiment
    /// actually has is <i>can I walk straight there</i>, and across open ground
    /// the answer is yes — so it is asked first, cheaply, and the pathfinder is
    /// never troubled. Only when something is genuinely in the way does the
    /// search get called, and then it is called to do what it is good at.
    /// </para>
    /// <para>
    /// The saving is not small. A recorded battle planned a 464 m march by
    /// exploring <b>4,138 cells</b>, over open grass, in a straight line, for a
    /// route that came back as two waypoints — which is the search rediscovering
    /// that nothing is in the way, one cell at a time. Every route in that
    /// recording reduced to two waypoints. The search was answering a question
    /// nobody had.
    /// </para>
    /// <para>
    /// Deliberately conservative for now: the shortcut fires <b>only</b> when
    /// the line is completely clear, and anything at all in the way — ground or
    /// men, friendly or not — falls through to exactly the code that ran before.
    /// Arching, crabbing and the rest of <see href="M18">the ladder</see> are
    /// the next pass. This one changes nothing except how a clear march is
    /// planned, which is what makes it safe to land on its own.
    /// </para>
    /// </remarks>
    public static class Marching
    {
        /// <summary>
        /// How finely the ground under a straight line is checked, in metres.
        /// </summary>
        /// <remarks>
        /// Terrain is the one thing the sweep cannot answer, because it is a
        /// field rather than a shape — so it is sampled, and the sampling has to
        /// be fine enough that no impassable patch hides between two probes. Ten
        /// metres against a map whose smallest features are cells a few metres
        /// across, and against bodies twenty metres deep that would have to fit
        /// through any gap they crossed.
        /// </remarks>
        private const float GroundStepMetres = 10f;

        /// <summary>
        /// Plans a march, taking the straight line whenever there is one.
        /// </summary>
        /// <remarks>
        /// Returns a <see cref="PathResult"/> so that every caller keeps the
        /// error handling it already has — a straight line is reported as a
        /// route of two waypoints having explored no cells, which is also how it
        /// shows up in a recording and makes the saving legible there.
        /// </remarks>
        public static PathResult PlanTo(
            BattleState battle, UnitInstance unit, IPathfinder pathfinder, Vec2 destination)
        {
            if (battle == null) throw new ArgumentNullException(nameof(battle));
            if (unit == null) throw new ArgumentNullException(nameof(unit));
            if (pathfinder == null) throw new ArgumentNullException(nameof(pathfinder));

            if (IsClearLine(battle, unit, unit.Position, destination, unit.Facing))
            {
                float straight = Vec2.Distance(unit.Position, destination);

                return PathResult.Success(
                    new[] { unit.Position, destination },
                    Array.Empty<Coord>(),
                    straight,
                    straight,
                    cellsExplored: 0);
            }

            return pathfinder.FindPath(unit.Position, destination, unit.Def.Movement);
        }

        /// <summary>
        /// Whether a regiment's whole body can travel from one point to another
        /// in a straight line without meeting anything.
        /// </summary>
        /// <remarks>
        /// <b>M12</b>: the rectangle is what travels, so it is the rectangle
        /// that is asked. Both halves matter and they fail differently — ground
        /// stops a body dead, and another regiment is only in the way if the
        /// two shapes genuinely meet, which a centre-line test cannot tell.
        /// </remarks>
        public static bool IsClearLine(
            BattleState battle, UnitInstance unit, Vec2 from, Vec2 to, Facing facing)
        {
            Vec2 travel = to - from;
            float length = travel.Length;

            if (length <= 0f) return battle.FormationFits(unit, from, facing);

            if (!GroundIsClear(battle, unit, from, travel, length, facing)) return false;

            var body = new OrientedRect(from, facing, unit.Footprint);

            foreach (UnitInstance other in battle.UnitsOnField())
            {
                if (ReferenceEquals(other, unit)) continue;

                if (Sweep.FirstTouch(body, travel, other.Shape, out _)) return false;
            }

            return true;
        }

        private static bool GroundIsClear(
            BattleState battle, UnitInstance unit, Vec2 from, Vec2 travel, float length, Facing facing)
        {
            int steps = Math.Max(1, (int)MathF.Ceiling(length / GroundStepMetres));

            for (int i = 0; i <= steps; i++)
            {
                Vec2 at = from + travel * (i / (float)steps);

                if (!battle.FormationFits(unit, at, facing)) return false;
            }

            return true;
        }

        /// <summary>
        /// What a march would meet on the way, and how far it would get first.
        /// </summary>
        /// <remarks>
        /// Not used to plan yet — this is what the arching pass will fan over,
        /// and it is here now because it is the same question <see
        /// cref="IsClearLine"/> asks and answering it twice in two places is how
        /// the two answers come to disagree.
        /// </remarks>
        public static UnitInstance? FirstBodyInTheWay(
            BattleState battle, UnitInstance unit, Vec2 from, Vec2 to, Facing facing, out float distance)
        {
            Vec2 travel = to - from;
            distance = travel.Length;

            if (distance <= 0f) return null;

            var body = new OrientedRect(from, facing, unit.Footprint);

            UnitInstance? nearest = null;
            float closest = float.MaxValue;

            foreach (UnitInstance other in battle.UnitsOnField())
            {
                if (ReferenceEquals(other, unit)) continue;

                if (!Sweep.FirstTouch(body, travel, other.Shape, out float reach)) continue;

                if (reach < closest)
                {
                    closest = reach;
                    nearest = other;
                }
            }

            if (nearest != null) distance = closest;

            return nearest;
        }
    }
}
