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
    /// Two rungs of <b>M18</b>'s ladder are here: the straight line, and going
    /// round one of its own. Crabbing and passing through are not, and until
    /// they are, anything this cannot answer falls through to the search
    /// exactly as before.
    /// </para>
    /// <para>
    /// <b>Enemies are not obstacles to a plan at all</b>, which is a departure
    /// from M15a worth stating. As walls they made every charge arrive by
    /// walking politely round the regiment it had been sent to break — five
    /// tests at once. But the deeper reason is the one M4 already gives for
    /// terrain: a route that quietly goes round an enemy is overruling the line
    /// the player drew, and whether to cross a formed enemy's front is the most
    /// consequential decision in the game to take out of their hands. So an
    /// enemy on the line is marched into, and halting or fighting is settled
    /// where it always was.
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

            // Rung 1: straight there.
            if (IsClearLine(battle, unit, unit.Position, destination, unit.Facing))
                return Straight(new[] { unit.Position, destination });

            // Rung 2: round whatever is in the way — but only for a march.
            // Closing with an enemy is O5's business and it says centre first,
            // then sidestep to share the face; arching an attack in would put
            // the two rules in charge of the same approach.
            if (unit.Order.Kind != OrderKind.Attack)
            {
                IReadOnlyList<Vec2>? arch = ArchAround(battle, unit, destination);
                if (arch != null) return Straight(arch);
            }

            // Rungs 3 and 4 are not built. Until they are, the search is what
            // answers — which is also what answered before any of this.
            return pathfinder.FindPath(unit.Position, destination, unit.Def.Movement);
        }

        /// <summary>
        /// Room left beside whatever is being gone round, in metres.
        /// </summary>
        /// <remarks>
        /// <b>M19a.</b> A line that grazes the corner of what it is going round
        /// is a line that fails on the first metre of drift, and a march drifts
        /// constantly — it is being pushed about by crowding, by terrain and by
        /// its own turning circle. Aim past the corner, not at it.
        /// </remarks>
        private const float TangentMarginMetres = 8f;

        private static PathResult Straight(IReadOnlyList<Vec2> waypoints)
        {
            float total = 0f;

            for (int i = 1; i < waypoints.Count; i++)
                total += Vec2.Distance(waypoints[i - 1], waypoints[i]);

            return PathResult.Success(waypoints, Array.Empty<Coord>(), total, total, cellsExplored: 0);
        }

        /// <summary>
        /// Goes round the first thing in the way, by whichever side costs less.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>M18</b> rung two. Two candidates and no more: past one end of the
        /// obstruction or past the other, aimed at its tangent rather than at
        /// some guessed angle, so the way round hugs what it is avoiding
        /// instead of swinging wide of it. A fan of fixed bearings would have to
        /// be either coarse enough to miss the gap or fine enough to be
        /// expensive, and neither is necessary when the thing to go round is
        /// right there and has a known shape.
        /// </para>
        /// <para>
        /// Scored by total distance, which is <b>M17</b>: among lines that work,
        /// take the one that loses least to going round. Terrain speed is not in
        /// it and must not be — marching into a swamp is a decision the player
        /// made, and a route that quietly took the dry way would be overruling
        /// an order rather than carrying it out.
        /// </para>
        /// <para>
        /// One obstruction deep, deliberately. Going round the thing behind the
        /// thing behind the thing is a search, and there is already a search for
        /// that. This is the cheap answer to the common case: a single body
        /// standing where you wanted to walk.
        /// </para>
        /// </remarks>
        private static IReadOnlyList<Vec2>? ArchAround(BattleState battle, UnitInstance unit, Vec2 destination)
        {
            UnitInstance? blocker =
                FirstBodyInTheWay(battle, unit, unit.Position, destination, unit.Facing, out _);

            // Stopped by ground rather than by anybody. Going round terrain is
            // what the search is genuinely good at, and guessing at a tangent
            // for a shape with no centre would be guessing.
            if (blocker == null) return null;

            Vec2 travel = destination - unit.Position;
            if (travel.IsNearZero) return null;

            Vec2 across = new Vec2(-travel.Y, travel.X).Normalised();

            var body = new OrientedRect(unit.Position, unit.Facing, unit.Footprint);

            float beside = blocker.Shape.ProjectedRadius(across)
                         + body.ProjectedRadius(across)
                         + TangentMarginMetres;

            IReadOnlyList<Vec2>? best = null;
            float shortest = float.MaxValue;

            // One side then the other, and never in an order that depends on
            // where anybody happens to be standing — two regiments equally far
            // either way must both pick the same side every time this is asked,
            // or the route flickers between them at the re-planning cadence.
            for (int side = 0; side < 2; side++)
            {
                Vec2 through = blocker.Position + (side == 0 ? across : -across) * beside;

                if (!IsClearLine(battle, unit, unit.Position, through, unit.Facing)) continue;
                if (!IsClearLine(battle, unit, through, destination, unit.Facing)) continue;

                float cost = Vec2.Distance(unit.Position, through) + Vec2.Distance(through, destination);

                if (cost >= shortest) continue;

                shortest = cost;
                best = new[] { unit.Position, through, destination };
            }

            return best;
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
                if (!IsInTheWayOf(unit, other)) continue;

                if (Sweep.FirstTouch(body, travel, other.Shape, out _)) return false;
            }

            return true;
        }

        /// <summary>
        /// Whether one regiment counts as an obstacle to another when a route is
        /// being planned.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>M15a</b>, and it is load-bearing rather than a refinement. An
        /// enemy is only in the way of a regiment that was going somewhere else.
        /// To one told to advance or to press, an enemy standing on the line is
        /// not an obstruction at all — going through it <i>is</i> the order, and
        /// that is what `TryFightWhatBlocks` exists to carry out.
        /// </para>
        /// <para>
        /// Learnt by breaking five tests at once. Routing round enemies without
        /// this made charges arrive by walking politely round the regiment they
        /// had been sent to break, cavalry decline to be held by cavalry, and a
        /// regiment that had fought its way past somebody never fight at all.
        /// A rule that quietly turns every attack into a detour does not look
        /// like a pathfinding change from the outside.
        /// </para>
        /// </remarks>
        private static bool IsInTheWayOf(UnitInstance unit, UnitInstance other)
        {
            if (ReferenceEquals(other, unit)) return false;

            return other.Owner == unit.Owner;
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
                if (!IsInTheWayOf(unit, other)) continue;

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
