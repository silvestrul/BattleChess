using System;
using System.Collections.Generic;
using BattleChess.Contracts;
using BattleChess.Rules.HybridPlanning;

namespace BattleChess.Rules
{
    /// <summary>
    /// The production experiment: keep the ladder's cheap, intentional route
    /// choices, but refuse to walk a route whose rectangle has not been proved
    /// clear.  A tangent search is a recovery tool, not the normal answer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The first leg receives special treatment.  A regiment that begins
    /// lapping one of its own must first get into a clear pose; treating the
    /// whole first leg as &quot;leaving&quot; made an excuse for its first metre into a
    /// licence to walk through the same regiment for all of it.  The egress
    /// check below allows overlap only while it monotonically decreases.
    /// </para>
    /// <para>
    /// This is deliberately a bounded local planner.  It considers the few
    /// separating directions supplied by the bodies the mover actually laps,
    /// then asks whether one can stage the regiment on a clear straight run to
    /// its destination.  It does not introduce a second graph or a heading
    /// lattice into the ordinary order path.
    /// </para>
    /// </remarks>
    public sealed class StagedRoutePlanner : IRoutePlanner
    {
        private const float AllowedContactFraction = 0.05f;
        private const float SeparationTolerance = 1e-3f;
        private const float EgressSpacingMetres = 2f;

        /// <summary>
        /// Whether a pose search is asked before a press-through is accepted.
        /// A measurement lever: turning it off restores the planner to the
        /// ladder-and-tangents form it had before.
        /// </summary>
        internal static bool PoseSearchBeforePressing = true;

        /// <summary>How far the pose search may stray from the route guiding it.</summary>
        /// <summary>What a corridor-bounded pose search may spend before widening.</summary>
        internal static int BoundedBudget = 4000;

        internal static float CorridorHalfWidthMetres;   // 0 = straight to the unbounded search

        /// <summary>
        /// Whether every cheap graph is asked before the lattice is, rather
        /// than only the straight cast and the tangents. A measurement lever.
        /// </summary>
        /// <remarks>
        /// The lattice is two orders of magnitude dearer than any of these, so
        /// what decides an order's cost is not how fast the lattice is but how
        /// often it is reached. A ladder detour with bends was refused unasked
        /// - refused for the shape of its route rather than for anything
        /// measured about it - and the rings graph was never asked at all.
        /// Both are a fraction of a millisecond to try, and both are proved
        /// walkable before they are taken, so refusing them buys nothing.
        /// </remarks>
        /// <remarks>
        /// <b>On, on the measurement.</b> The Long March fell from 24,8 ms an
        /// order to 5,1 and its routes got <i>shorter</i> - 1 232,9 s to
        /// 1 155,8 - because thirty-six orders that were being handed to a
        /// pose search now walk the route the ladder had already drawn. The
        /// other two fields neither gain nor lose: the proof is a walk of a
        /// route already in hand.
        /// </remarks>
        internal static bool AcceptBentLadder = true;

        /// <summary>Whether the corners graph is asked before the lattice is.</summary>
        /// <remarks>
        /// <b>Off, on the measurement.</b> It pays only where the ladder is
        /// already the answer: on its own the Long March fell 24,8 to 9,0 - but
        /// the bent ladder above gets that same field to 5,1 for nothing, and
        /// on top of it corners is pure loss. On the two crowded fields it is a
        /// loss outright, 9,6 to 12,4 and 5,7 to 8,9, because a richer graph
        /// over a field of eighty bodies costs more to price than it saves.
        /// </remarks>
        internal static bool AskCorners;

        /// <summary>Whether the whole-ring graph is asked before the lattice is.</summary>
        /// <remarks>
        /// <b>Off, on the measurement.</b> Worse than corners everywhere it was
        /// tried, and worse for route quality too: 1 178,8 s against 1 155,8.
        /// </remarks>
        internal static bool AskRings;

        /// <summary>
        /// Whether the lattice is bounded to a tube around the cheap route that
        /// already failed. A measurement lever.
        /// </summary>
        /// <remarks>
        /// A route the executor refuses is still a true statement about
        /// <i>where</i> the answer lies - which side of which body - and that
        /// is the expensive half of what the lattice spends its expansions
        /// rediscovering. The tube is guidance and never truth: every pose
        /// inside it is proved against the bodies exactly as before, and a
        /// search that finds nothing walkable inside it is run again unbounded.
        /// </remarks>
        /// <remarks>
        /// <b>Off, on the measurement.</b> Swept at half-widths of 45, 90 and
        /// 150 m and budgets of 4 000, 20 000 and 40 000 expansions: the
        /// bounded search had to be re-run unbounded on 92 of the 94 orders it
        /// was asked, and the field-wide cost went the wrong way at every
        /// setting - the Crucible 6,5 to 11,2 ms an order, Broken Country 3,4
        /// to 7,8. The reason is in the counters beside it: on 74 of those 94
        /// orders the cheap route was a press-through, so the tube was drawn
        /// round a line that goes through the middle of a regiment. There is no
        /// answer near it to find. Kept as a lever, at nought, so the idea is
        /// not re-derived.
        /// </remarks>
        internal static bool CorridorFromCheapRoute;

        /// <summary>How far the lattice may stray from the cheap route guiding it.</summary>
        internal static float CheapCorridorHalfWidthMetres = 45f;

        /// <summary>Measurement counters: how many orders reach each stage.</summary>
        internal static int Staged, LadderClean, LadderBent, TangentClean, CornersClean, RingsClean,
            PoseAsked, PoseWon, PoseWidened, Pressed;

        internal static void ResetCounters() =>
            Staged = LadderClean = LadderBent = TangentClean = CornersClean = RingsClean =
                PoseAsked = PoseWon = PoseWidened = Pressed =
                    BadFirstLeg = BadLaterLeg = BadPressed = BadNoRoute = 0;

        public string Name => "staged ladder with tangent recovery";

        public Plan PlanTo(
            BattleState battle, UnitInstance unit, IPathfinder pathfinder, Vec2 destination,
            IBattleLog? log = null, IWayRound? wayRound = null, Facing? arriveOn = null)
        {
            if (battle == null) throw new ArgumentNullException(nameof(battle));
            if (unit == null) throw new ArgumentNullException(nameof(unit));
            if (pathfinder == null) throw new ArgumentNullException(nameof(pathfinder));

            // Attacks have an approach planner and a moving target.  Their
            // repeated short plans are deliberately governed by OrderSystem's
            // chase cadence; staging a terrain march here would make that
            // cadence observe a different route shape every time the target
            // moves.  Keep the established tangent behaviour until attacks get
            // their own reservation-aware approach phase.
            if (unit.Order.Kind == OrderKind.Attack)
            {
                return RouteSearch.Find(
                    battle, unit, destination, arriveOn ?? unit.OrderFacing, log, pathfinder,
                    RouteSearch.Shape.Tangents);
            }

            // An egress is only useful when it is a real staging manoeuvre:
            // clear the bodies currently lapped, then leave on a leg that can
            // reach the order directly.  More complicated detours remain the
            // ladder/tangent planners' job, where their candidates are richer.
            if (TryStageForDirectRun(battle, unit, destination, out Plan staged))
            {
                Staged++;
                return staged;
            }

            Plan ladder = Marching.ByTheLadder(battle, unit, pathfinder, destination, log, wayRound);

            // A direct cast is already the exact shape the executor will walk.
            // A ladder detour is only a coarse topology proposal: its bends
            // leave the mover to arrive on a new front while other regiments
            // continue moving, which is where the remaining seizures came
            // from.  Hand those few non-direct orders to tangents, whose state
            // includes the front for every leg.
            if (!ladder.PressedThrough && ladder.Path.Waypoints.Count == 2 &&
                WalksCleanly(battle, unit, ladder))
            {
                LadderClean++;
                return ladder;
            }

            // The same route with bends in it, once it has been proved rather
            // than assumed.  What the paragraph above says about bends is true
            // of a route nobody checked; WalksCleanly checks every leg on the
            // front it will be held on, which is the whole of the objection.
            if (AcceptBentLadder && ladder.Path.Found && !ladder.PressedThrough &&
                ladder.Path.Waypoints.Count > 2 && WalksCleanly(battle, unit, ladder))
            {
                LadderBent++;
                return ladder;
            }

            // The tangent graph is expensive enough to earn its use.  It is
            // asked only after the ladder failed to provide a clean route, or
            // explicitly chose its press-through last resort.
            Plan tangent = RouteSearch.Find(
                battle, unit, destination, arriveOn ?? unit.OrderFacing, log, pathfinder,
                RouteSearch.Shape.Tangents);

            int badLeg = tangent.PressedThrough ? -1 : FirstBadLeg(battle, unit, tangent);

            if (tangent.Path.Found && !tangent.PressedThrough && badLeg == 0)
            {
                TangentClean++;
                return tangent;
            }

            if (tangent.PressedThrough) BadPressed++;
            else if (badLeg < 0) BadNoRoute++;
            else if (badLeg == 1) BadFirstLeg++;
            else BadLaterLeg++;

            // Tangents name only the legs that could lie on a shortest route,
            // which is a pruning about cost and not about clearance - so a leg
            // it declined to name can still be the one that walks.  The two
            // richer graphs cost a fraction of a millisecond each and stand
            // between an order and a lattice search costing tens.
            if (AskCorners)
            {
                Plan corners = RouteSearch.Find(
                    battle, unit, destination, arriveOn ?? unit.OrderFacing, log, pathfinder,
                    RouteSearch.Shape.Corners);

                if (corners.Path.Found && !corners.PressedThrough &&
                    WalksCleanly(battle, unit, corners))
                {
                    CornersClean++;
                    return corners;
                }
            }

            if (AskRings)
            {
                Plan rings = RouteSearch.Find(
                    battle, unit, destination, arriveOn ?? unit.OrderFacing, log, pathfinder,
                    RouteSearch.Shape.Rings);

                if (rings.Path.Found && !rings.PressedThrough &&
                    WalksCleanly(battle, unit, rings))
                {
                    RingsClean++;
                    return rings;
                }
            }

            // Nothing cheap could route this cleanly, so before shouldering
            // through anybody, ask the one planner that searches poses rather
            // than points.  It is dear — tens of milliseconds — but it is asked
            // only for the orders that would otherwise press, and Mx2c says a
            // press is Priority 3: what a way round costs is not a reason to
            // prefer walking through your own men.
            if (PoseSearchBeforePressing)
            {
                PoseAsked++;

                // The tangent route is already computed and already known to be
                // the wrong answer — but it is the right neighbourhood, and
                // bounding the lattice to a tube around it is what makes a
                // pose search affordable on an ordinary order.
                // Bounded first and cheaply: the grid's way round with a small
                // budget answers most of these, and the ones it cannot answer
                // must be found out about quickly rather than by exhausting a
                // hundred thousand expansions inside a tube.
                IReadOnlyList<Vec2>? tube =
                    CorridorFromCheapRoute && tangent.Path.Found && tangent.Path.Waypoints.Count >= 2
                        ? tangent.Path.Waypoints
                        : null;

                Plan posed;

                if (tube != null)
                {
                    posed = HybridAStarRoutePlanner.PlanAlong(
                        battle, unit, destination, arriveOn,
                        tube, CheapCorridorHalfWidthMetres, log, BoundedBudget);
                }
                else if (CorridorHalfWidthMetres > 0f)
                {
                    posed = HybridAStarRoutePlanner.PlanAlong(
                        battle, unit, destination, arriveOn,
                        corridor: null, CorridorHalfWidthMetres, log, BoundedBudget);
                }
                else
                {
                    posed = HybridAStarRoutePlanner.PlanAlong(
                        battle, unit, destination, arriveOn, corridor: null, 0f, log);
                }

                bool bounded = tube != null || CorridorHalfWidthMetres > 0f;

                if (bounded &&
                    (!posed.Path.Found || posed.PressedThrough || !WalksCleanly(battle, unit, posed)))
                {
                    PoseWidened++;
                    posed = HybridAStarRoutePlanner.PlanAlong(
                        battle, unit, destination, arriveOn, corridor: null, 0f, log);
                }

                if (posed.Path.Found && !posed.PressedThrough &&
                    WalksCleanly(battle, unit, posed))
                {
                    PoseWon++;
                    return posed;
                }
            }

            // The ladder's press-through remains an explicit, visible last
            // resort.  A terrain-only fallback which is not clear of friendly
            // bodies is never silently upgraded into a clean route.
            if (ladder.Path.Found && ladder.PressedThrough)
            {
                Pressed++;
                return ladder;
            }

            return tangent.Path.Found ? tangent : ladder;
        }

        /// <summary>
        /// Where the orders that reach the lattice actually fail: on the first
        /// leg, which is a regiment that cannot get out of its own crowd, or on
        /// a later one, which is a route that goes the wrong way round.
        /// </summary>
        /// <remarks>
        /// The two want opposite fixes, so which of them dominates decides
        /// whether there is a cheap answer at all. Counted on the tangent route
        /// only — the last cheap opinion before the dear one.
        /// </remarks>
        internal static int BadFirstLeg, BadLaterLeg, BadPressed, BadNoRoute;

        /// <summary>Whether the executor can walk every leg the plan claims.</summary>
        internal static bool WalksCleanly(BattleState battle, UnitInstance unit, Plan plan) =>
            FirstBadLeg(battle, unit, plan) == 0;

        /// <summary>
        /// The first leg the executor would refuse, or nought if it would walk
        /// the whole route. Minus one where there is no route to walk.
        /// </summary>
        internal static int FirstBadLeg(BattleState battle, UnitInstance unit, Plan plan)
        {
            IReadOnlyList<Vec2> points = plan.Path.Waypoints;
            if (!plan.Path.Found || points.Count < 2) return -1;

            for (int leg = 1; leg < points.Count; leg++)
            {
                Facing front = plan.Hold != null && leg < plan.Hold.Length && plan.Hold[leg].HasValue
                    ? plan.Hold[leg]!.Value
                    : Marching.AlongTheLine(points[leg - 1], points[leg], unit.Facing);

                bool startsInsideOwn = leg == 1 && StartsInsideOwn(battle, unit, front);

                if (startsInsideOwn)
                {
                    if (!EscapesWithoutDeepening(battle, unit, points[leg - 1], points[leg], front))
                        return leg;
                }
                else if (!Marching.IsClearLine(battle, unit, points[leg - 1], points[leg], front))
                {
                    return leg;
                }
            }

            return 0;
        }

        private static bool TryStageForDirectRun(
            BattleState battle, UnitInstance unit, Vec2 destination, out Plan plan)
        {
            plan = default;

            var pushes = new List<Vec2>();
            Vec2 totalPush = Vec2.Zero;

            foreach (UnitInstance other in battle.UnitsOnField())
            {
                if (other.Id == unit.Id || other.Owner != unit.Owner) continue;

                if (!OrientedRect.TryGetSeparation(unit.Shape, other.Shape, out Vec2 push))
                    continue;

                if (push.IsNearZero) continue;
                pushes.Add(push);
                totalPush += push;
            }

            if (pushes.Count == 0) return false;

            if (!totalPush.IsNearZero)
                pushes.Insert(0, totalPush);

            // A pose in the direction of the desired run is useful as a last
            // candidate only when the overlap directions cancel exactly.
            Vec2 towardsGoal = destination - unit.Position;
            if (!towardsGoal.IsNearZero)
                pushes.Add(towardsGoal * -1f);

            foreach (Vec2 push in pushes)
            {
                if (push.IsNearZero) continue;

                Vec2 direction = push.Normalised();
                float least = MathF.Max(EgressSpacingMetres, push.Length + EgressSpacingMetres);
                float furthest = unit.Footprint.BoundingRadius * 2f + least;

                for (float distance = least; distance <= furthest; distance += EgressSpacingMetres)
                {
                    Vec2 stage = unit.Position + direction * distance;
                    Facing escapeFront = Facing.Towards(unit.Position, stage);
                    Facing runFront = Facing.Towards(stage, destination);

                    if (!EscapesWithoutDeepening(battle, unit, unit.Position, stage, escapeFront))
                        continue;

                    if (!Marching.IsClearLine(battle, unit, stage, destination, runFront))
                        continue;

                    float distanceWalked = Vec2.Distance(unit.Position, stage) + Vec2.Distance(stage, destination);
                    PathResult path = PathResult.Success(
                        new[] { unit.Position, stage, destination }, Array.Empty<Coord>(),
                        distanceWalked, distanceWalked, 0);

                    plan = new Plan(path, new Facing?[] { null, escapeFront, runFront }, false);
                    return true;
                }
            }

            return false;
        }

        private static bool StartsInsideOwn(BattleState battle, UnitInstance unit, Facing front)
        {
            var at = new OrientedRect(unit.Position, front, unit.Footprint);

            foreach (UnitInstance other in battle.UnitsOnField())
            {
                if (other.Id == unit.Id || other.Owner != unit.Owner) continue;
                if (OrientedRect.OverlapFraction(at, other.Shape) > AllowedContactFraction)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Sweeps a first leg in small steps.  Bodies already lapped may only
        /// become less overlapped; bodies initially clear may never be entered.
        /// </summary>
        private static bool EscapesWithoutDeepening(
            BattleState battle, UnitInstance unit, Vec2 from, Vec2 to, Facing front)
        {
            Vec2 travel = to - from;
            float length = travel.Length;
            if (length <= Vec2.Epsilon) return false;

            var own = new List<UnitInstance>();
            var previousOverlap = new List<float>();
            var firstPose = new OrientedRect(from, front, unit.Footprint);

            foreach (UnitInstance other in battle.UnitsOnField())
            {
                if (other.Id == unit.Id || other.Owner != unit.Owner) continue;
                own.Add(other);
                previousOverlap.Add(OrientedRect.OverlapFraction(firstPose, other.Shape));
            }

            int samples = Math.Max(2, (int)MathF.Ceiling(length / EgressSpacingMetres));

            for (int i = 1; i <= samples; i++)
            {
                Vec2 at = Vec2.Lerp(from, to, (float)i / samples);
                if (!battle.FormationFits(unit, at, front)) return false;

                var pose = new OrientedRect(at, front, unit.Footprint);

                for (int other = 0; other < own.Count; other++)
                {
                    float overlap = OrientedRect.OverlapFraction(pose, own[other].Shape);
                    float before = previousOverlap[other];

                    if (before > AllowedContactFraction)
                    {
                        if (overlap > before + SeparationTolerance) return false;
                    }
                    else if (overlap > AllowedContactFraction)
                    {
                        return false;
                    }

                    previousOverlap[other] = overlap;
                }
            }

            return true;
        }
    }
}
