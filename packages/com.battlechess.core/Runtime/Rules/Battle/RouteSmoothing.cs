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

        /// <summary>Stretches left unsmoothed because the order ran out of time.</summary>
        [System.ThreadStatic] internal static long GaveUpSmoothing;

        /// <summary>
        /// What the furthest-first scan costs, against what a scan that
        /// extended outwards while clear would have cost.
        /// </summary>
        /// <remarks>
        /// <para>
        /// [M119a] charged three quarters of every clearance check in the game
        /// to a refusal, and [M120] charged 36-42% of all checks to this pass
        /// with a <b>97% refusal rate</b>. The shape of the loop explains it:
        /// furthest-first walks back from the last waypoint and takes the first
        /// clear cast, so when the achievable shortcut is short it pays a failed
        /// clearance check for every point it walks past.
        /// </para>
        /// <para>
        /// Counted rather than changed, because the alternative - extending
        /// outwards and stopping at the first failure - gives a different answer
        /// wherever clearance is not monotone along the route, and how often
        /// that happens is a fact about arrangements rather than about loops.
        /// <c>Reached</c> against <c>Tried</c> is the whole question.
        /// </para>
        /// </remarks>
        [System.ThreadStatic] internal static long CastsTried;

        /// <summary>How many of those casts were clear and cheaper.</summary>
        [System.ThreadStatic] internal static long CastsTaken;

        /// <summary>
        /// How many points a taken shortcut skipped, summed - which is also
        /// what an outward scan would have paid in casts.
        /// </summary>
        [System.ThreadStatic] internal static long Reached;

        /// <summary>Stretches where no cast at all was clear.</summary>
        [System.ThreadStatic] internal static long NothingClear;

        /// <summary>
        /// Whether the scan extends outwards from the near point and stops at
        /// the first refusal, instead of walking back from the last waypoint
        /// and taking the first success.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The two find the same point wherever clearance is monotone along the
        /// route - which is the ordinary case, because a route that bent did so
        /// round something, and past that something nothing gets clearer again.
        /// Where clearance is <i>not</i> monotone they differ, and the outward
        /// scan keeps a waypoint the furthest-first one would have dropped: a
        /// slightly wobblier route for a much cheaper pass.
        /// </para>
        /// <para>
        /// <b>Off, and measured off.</b> [M121] built it and counted both sides.
        /// It cuts the casts to 34-40% exactly as predicted, and <b>the clock
        /// does not move</b>: 0,425 against 0,419 ms an order on the crucible,
        /// 0,501 against 0,491 on Broken Country. The prediction was made from
        /// a share of clearance <i>checks</i> and this pass's checks are the
        /// cheap kind - long casts down open ground, where the near query hands
        /// back nothing to sweep.
        /// </para>
        /// <para>
        /// And it is not free. It changes 26 routes in 80 on the crucible, 21
        /// in 80 on Broken Country, and 21 of those 26 are dearer to walk: +1,2%
        /// of the field's marching, +3,2% on Sideways Mile, with one route there
        /// <b>160% dearer</b>. Kept as a lever because the measurement is worth
        /// more than the code is, and because a future arrangement may make the
        /// casts dear enough to be worth a wobble.
        /// </para>
        /// </remarks>
        internal static bool ExtendOutwards;

        /// <summary>
        /// Whether the pass stops looking for shortcuts once the order has spent
        /// its search budget.
        /// </summary>
        /// <remarks>
        /// Safe in a way most gates are not: it abandons no half-built state and
        /// discards nothing already proved. Every point it has not reached is
        /// kept exactly as the search left it, which is a route that already
        /// passed the gate - so the worst this can do is hand back the wound
        /// route, which is the route that would have been walked had this pass
        /// never existed at all.
        /// </remarks>
        internal static bool StopSmoothingWhenOutOfTime = true;

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

                bool tookOne = false;

                // Nothing below is load-bearing: every point this pass does not
                // reach is kept as the search left it, so giving up here hands
                // back a route that has already passed the gate.
                bool spent = Marching.StopSearchingWhenOutOfTime &&
                             StopSmoothingWhenOutOfTime && Marching.StopNow();

                if (spent)
                {
                    GaveUpSmoothing++;
                }
                else if (ExtendOutwards)
                {
                    // Outwards, stopping at the first refusal - so a stretch
                    // with no shortcut in it costs one cast rather than the
                    // whole rest of the route.
                    for (int to = at + 2; to < points.Count; to++)
                    {
                        CastsTried++;

                        Facing front = Marching.AlongTheLine(
                            points[at], points[to], unit.Facing);

                        if (!Marching.IsClearLine(
                                battle, unit, points[at], points[to], front, leaving: at == 0))
                            break;

                        if (!CheaperStraight(battle, unit, points, fronts, at, to, front))
                            break;

                        furthest = to;
                        reached = front;
                        tookOne = true;
                    }
                }
                else
                {
                    // Furthest first: the point of the pass is the long cast, and
                    // stopping at the first one that happens to be clear would
                    // keep most of the wobble it exists to remove.
                    for (int to = points.Count - 1; to > at + 1; to--)
                    {
                        CastsTried++;

                        Facing front = Marching.AlongTheLine(
                            points[at], points[to], unit.Facing);

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
                        tookOne = true;
                        break;
                    }
                }

                if (tookOne)
                {
                    CastsTaken++;

                    // What an outward scan would have spent to reach the same
                    // point: one cast a step, plus the one that failed.
                    Reached += furthest - at;
                }
                else
                {
                    NothingClear++;
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
