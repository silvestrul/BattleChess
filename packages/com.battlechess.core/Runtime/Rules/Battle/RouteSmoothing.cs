using System.Collections.Generic;
using BattleChess.Contracts;

namespace BattleChess.Rules
{
    /// <summary>
    /// Drops every waypoint the regiment can see past, keeping the fronts with
    /// the points that survive.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why this is not inside a planner.</b> It was, at first - inside the
    /// lattice, because the lattice was where the 154-waypoint route came
    /// from. Then a recording showed a four-waypoint ladder route walking
    /// <i>left, up, left</i> to a destination one diagonal away, and the pass
    /// that would have straightened it was not asked, because that route came
    /// from a different planner. A route's shape is not the property of
    /// whichever search happened to produce it. It belongs to whoever hands it
    /// to the executor.
    /// </para>
    /// <para>
    /// A shortcut is taken only when the rectangle that will travel it is
    /// proved clear on the front it will be held on - the same question
    /// <see cref="Marching.IsClearLine"/> answers for every other planner,
    /// asked with the <i>new</i> front rather than the one the search swept,
    /// because dropping a point drops its heading too.
    /// </para>
    /// </remarks>
    internal static class RouteSmoothing
    {
        /// <summary>
        /// The same route with every waypoint dropped that the regiment can
        /// see past, or the route unchanged where nothing can be.
        /// </summary>
        /// <remarks>
        /// A press-through is never smoothed. It is two points and a declared
        /// decision, and casting ahead through a body the regiment has already
        /// been given leave to walk into would only re-derive it.
        /// </remarks>
        internal static Plan Applied(BattleState battle, UnitInstance unit, Plan plan)
        {
            if (!SmoothTheRoute || plan.PressedThrough || !plan.Path.Found) return plan;
            if (plan.Path.Waypoints.Count < 3) return plan;

            using (PlanningProfile.Measure(PlanningProfile.Step.SmoothRoute))
            {
                Smooth(battle, unit, plan.Path.Waypoints, plan.Hold,
                       out IReadOnlyList<Vec2> points, out Facing?[]? fronts);

                if (points.Count == plan.Path.Waypoints.Count) return plan;

                float distance = 0f;
                for (int i = 1; i < points.Count; i++)
                    distance += Vec2.Distance(points[i - 1], points[i]);

                // EffectiveDistance is metres-of-equivalent-open-ground. The
                // straightened route is re-priced rather than carrying the
                // wound one's figure, which would report ground the regiment
                // no longer covers.
                float seconds = Marching.SecondsToWalk(battle, unit, points, fronts);
                float topSpeed = unit.BaseSpeed;
                float effective = seconds > 0f && topSpeed > 0f ? seconds * topSpeed : distance;

                PathResult path = PathResult.Success(
                    points, System.Array.Empty<Coord>(), distance, effective,
                    plan.Path.CellsExplored);

                return new Plan(path, hold: fronts, pressedThrough: false, effort: plan.Effort);
            }
        }

        /// <summary>Whether the cast-ahead pass runs. A measurement lever.</summary>
        internal static bool SmoothTheRoute = true;

        /// <summary>
        /// Drops every waypoint the regiment can see past, keeping the fronts
        /// with the points that survive.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A shortcut is taken only when the rectangle that will travel it is
        /// proved clear on the front it will be held on - the same question
        /// <see cref="Marching.IsClearLine"/> answers for every other planner,
        /// asked with the <i>new</i> front rather than the one the lattice
        /// swept, because dropping a point drops its heading too.
        /// </para>
        /// <para>
        /// <b>The first leg is never touched.</b> A regiment that begins
        /// lapping one of its own is allowed out of the overlap it starts in,
        /// and that licence belongs to the leg the search planned, not to a
        /// longer one drawn through the same crowd afterwards.
        /// </para>
        /// <para>
        /// And a shortcut must be cheaper in seconds, not merely shorter. A
        /// straight line is always the shorter distance and can still be the
        /// dearer walk, because turning onto it costs time over no ground -
        /// which is the whole reason this planner searches headings at all.
        /// </para>
        /// </remarks>
        private static void Smooth(
            BattleState battle, UnitInstance unit,
            IReadOnlyList<Vec2> points, Facing?[]? fronts,
            out IReadOnlyList<Vec2> smoothed, out Facing?[]? held)
        {
            if (!SmoothTheRoute || points.Count < 3)
            {
                smoothed = points;
                held = fronts;
                return;
            }

            // The first leg is exempt only when there is something to be
            // exempt for. A regiment that begins lapping one of its own is
            // allowed out of the overlap it starts in, and a longer line drawn
            // through the same crowd afterwards does not inherit that licence.
            // A regiment standing clear has no such claim, and holding the
            // first leg fixed for it meant a three-point route could never be
            // straightened at all: its only shortcut is the first leg, so the
            // pass was refusing the one cast it had.
            bool holdTheFirstLeg = StartsInsideItsOwn(battle, unit);

            var keptPoints = new List<Vec2>(points.Count) { points[0] };
            var keptFronts = new List<Facing?>(points.Count) { FrontAt(fronts, 0) };

            if (holdTheFirstLeg)
            {
                keptPoints.Add(points[1]);
                keptFronts.Add(FrontAt(fronts, 1));
            }

            int at = holdTheFirstLeg ? 1 : 0;

            while (at < points.Count - 1)
            {
                int furthest = at + 1;
                Facing reached = FrontAt(fronts, furthest) ?? Marching.AlongTheLine(
                    points[at], points[furthest], unit.Facing);

                // Furthest first: the point of the pass is the long cast, and
                // stopping at the first one that happens to be clear would
                // keep most of the wobble it exists to remove.
                for (int to = points.Count - 1; to > at + 1; to--)
                {
                    Facing front = Marching.AlongTheLine(points[at], points[to], unit.Facing);

                    // A cast that starts where the regiment stands is asked
                    // with the leaving rule, the same one IsClearLeg uses, so
                    // the body it is already touching does not refuse a line
                    // that walks away from it.
                    if (!Marching.IsClearLine(
                            battle, unit, points[at], points[to], front, leaving: at == 0))
                        continue;

                    if (!CheaperStraight(battle, unit, points, fronts, at, to, front))
                        continue;

                    furthest = to;
                    reached = front;
                    break;
                }

                keptPoints.Add(points[furthest]);
                keptFronts.Add(reached);
                at = furthest;
            }

            smoothed = keptPoints;
            held = keptFronts.ToArray();
        }

        /// <summary>
        /// Whether the regiment is already overlapping one of its own where it
        /// stands, which is what the first leg's licence exists for.
        /// </summary>
        private static bool StartsInsideItsOwn(BattleState battle, UnitInstance unit)
        {
            foreach (UnitInstance other in battle.UnitsOnField())
            {
                if (other.Id == unit.Id) continue;
                if (other.Owner != unit.Owner) continue;
                if (!other.IsFighting) continue;

                if (OrientedRect.Overlaps(unit.Shape, other.Shape)) return true;
            }

            return false;
        }

        private static Facing? FrontAt(Facing?[]? fronts, int index) =>
            fronts != null && index < fronts.Length ? fronts[index] : null;

        /// <summary>
        /// Whether one straight leg costs fewer seconds than the stretch of
        /// route it would replace, both priced by the executor's own model.
        /// </summary>
        private static bool CheaperStraight(
            BattleState battle, UnitInstance unit,
            IReadOnlyList<Vec2> points, Facing?[]? fronts, int from, int to, Facing front)
        {
            var wound = new Vec2[to - from + 1];
            var woundFronts = new Facing?[to - from + 1];

            for (int i = 0; i <= to - from; i++)
            {
                wound[i] = points[from + i];
                woundFronts[i] = FrontAt(fronts, from + i);
            }

            float around = Marching.SecondsToWalk(battle, unit, wound, woundFronts);
            float straight = Marching.SecondsToWalk(
                battle, unit,
                new[] { points[from], points[to] },
                new Facing?[] { FrontAt(fronts, from), front });

            return straight <= around;
        }
    }
}
