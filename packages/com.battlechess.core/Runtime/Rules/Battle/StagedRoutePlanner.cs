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

        /// <summary>
        /// What the pose search may spend before it gives up and the press is
        /// taken. 0 leaves the lattice's own hundred thousand.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The lattice's own cap is a safety valve set where nothing was
        /// expected to reach it. Recorded in play, orders were reaching 31 640
        /// expansions and costing <b>888 to 1080 ms</b> - and then having the
        /// answer refused by [M65] for being five times dearer than the press.
        /// A search whose answer is going to be thrown away should be allowed
        /// to give up.
        /// </para>
        /// <para>
        /// <b>Swept on both fixtures, three fields, and the value is where
        /// quality stops being free.</b> At twenty thousand the Crucible's
        /// one-click order is byte-identical in outcome - 17 unwalkable, 16
        /// pressed, 416,8 s, worst detour 2,7x - at <b>15,93 ms an order
        /// against 55,08</b>; Broken Country likewise at 12,91 against 36,28.
        /// At ten thousand the Long March starts pressing routes it used to
        /// walk (1 unwalkable to 5) and the scattered Crucible goes 0 to 2. So
        /// twenty thousand, which is three and a half times cheaper for one
        /// extra pressed route in eighty.
        /// </para>
        /// <para>
        /// <b>Ten thousand is quality-identical to twenty and half the cost</b>
        /// — same unwalkable, same detour, same 33 294 route-seconds, the
        /// Crucible's worst order 38,2 ms against 67,5. Left at twenty
        /// thousand until the levers it pairs with are chosen, because this is
        /// the only lever that bounds <i>worst-case</i> time and the value
        /// should be picked once, with the rest. Below five thousand it is the
        /// crudest lever there is: a thousand buys 14,5 ms for <b>31
        /// unwalkable against 17</b>. See <c>docs/pathfinding-levers.md</c>.
        /// </para>
        /// <para>
        /// <b>This does not remove the freeze on its own.</b> On the Great
        /// Field the same cap takes the recorded worst order from 140,5 ms to
        /// 101,5, not to nothing. A single order that costs a tenth of a
        /// second is a hitch whatever the cap, and the answer to that is to
        /// stop planning it on the frame that asked - see M64, which already
        /// proved a plan can be worked out off the main thread.
        /// </para>
        /// </remarks>
        /// <remarks>
        /// <para>
        /// <b>4096, and it is the cut rather than a backstop.</b> This used to
        /// be a ceiling nothing reached, with
        /// <see cref="HybridAStarPlanner.MillisecondsPerSearch"/> doing the
        /// real cutting at 15 ms - which stopped the lattice at 128 expansions,
        /// two readings of its clock, and a recording shows it winning
        /// <b>none of eleven searches</b> under that.
        /// </para>
        /// <para>
        /// Swept with the clock off: routes the lattice wins go 1, 3, <b>9</b>,
        /// 9, 9 on the Crucible at 128, 2048, <b>4096</b>, 8192, 16384
        /// expansions, and 6, 13, <b>14</b>, 14, 14 on Broken Country. Four
        /// thousand is where it meets what an unlimited search wins, so beyond
        /// it there is nothing left to buy - 16384 costs 47 ms an order against
        /// 32 and wins not one more route.
        /// </para>
        /// <para>
        /// <b>The price is the worst order</b>, which goes from about 103 ms to
        /// 179 on the Crucible. That is a frame, and a single order cannot be
        /// split across frames while the lattice runs on the one asking - the
        /// mitigation belongs in <c>MayPlan</c>, which charges its ration
        /// before a plan runs instead of after. Halving this to 2048 buys the
        /// worst order back down to 92 ms and costs six of the nine.
        /// </para>
        /// </remarks>
        internal static int PoseExpansionBudget = 4096;

        internal static float CorridorHalfWidthMetres;   // 0 = straight to the unbounded search

        /// <summary>
        /// How many times a press-through's cost a clean way round may cost
        /// and still be preferred to it. 0 turns the ceiling off, which
        /// restores the absolute priority M55 gave the way round.
        /// </summary>
        /// <remarks>
        /// <para>
        /// M55 read Mx2c - &quot;if clean movement is not possible, a press-through
        /// is initiated&quot; - as an ordering with no price attached: any clean
        /// route beats any press. That killed the seizure bug and produced a
        /// worse one. Recorded in play, tick 651: a regiment ordered 239 m
        /// across open ground with <b>one</b> of its own on the line walked
        /// <b>1325 m in 847 s</b> to avoid a press the ladder priced at
        /// <b>151 s</b>. Nobody watching a battle reads a five-fold detour as
        /// good order; they read it as the regiment refusing the order.
        /// </para>
        /// <para>
        /// So the priority holds, but not past a multiple. Below the ceiling
        /// the way round wins however much dearer it is, which is Mx2c intact
        /// for every ordinary order; above it the press is taken and declared,
        /// which is what a press being <i>visible</i> was always for.
        /// </para>
        /// </remarks>
        internal static float WayRoundCostCeiling = 3f;

        /// <summary>
        /// What a way round may cost against simply walking there on an empty
        /// field, when there is no press-through to price it against.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <see cref="WayRoundCostCeiling"/> only ever fires when the ladder
        /// found a press to compare with. An order given from a pose the last
        /// order left behind - tangled in a friendly, facing the wrong way -
        /// has no press to find, so every ceiling stood aside and the terminal
        /// fallback returned whatever the tangent graph drew. A recording has
        /// one at 686 m for a 188 m hop, 3,6x, ten waypoints and a 177 degree
        /// opening wheel, which nothing priced on the way out.
        /// </para>
        /// <para>
        /// <b>Four, not three.</b> A way round is legitimately dearer against
        /// an empty field than against a press, and the bench says how much:
        /// over 240 orders the worst honest route costs 2,8x, 2,7x and 2,9x
        /// its own straight line. Three would refuse those; four clears them
        /// and still catches the recording.
        /// </para>
        /// </remarks>
        internal static float StraightLineCostCeiling = 4f;

        /// <summary>
        /// How much of a route a regiment may walk side-on, as a share of its
        /// length. Above it the crab is not offered at all.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>M81.</b> Crabbing exists to make a body its own depth wide instead
        /// of its own frontage, so that it fits a gap a march does not. That is
        /// a manoeuvre performed <i>at</i> a gap, and
        /// <see cref="Marching.CrabThrough"/> says so itself: it walks up to the
        /// squeeze front-on, threads it side-on, and comes back onto the march
        /// afterwards, because - its words - crabbing the whole way "would
        /// arrive at the far end still side-on and at two fifths pace for a
        /// journey that never needed it".
        /// </para>
        /// <para>
        /// It then offered exactly that as its fallback whenever it could not
        /// find where the squeeze began and ended, and the recording of 25
        /// August has one: a spearman regiment 404 m across the field holding
        /// 81 degrees against a march on -9, the whole way. <b>645 s against 285
        /// s</b> for the same line walked front-on - the planner's own numbers,
        /// in its own log line - and it arrived 90 degrees off its ordered front
        /// having averaged 0,6 m/s of the 1,0 the ground allowed.
        /// </para>
        /// <para>
        /// Nothing priced badly. <b>M22</b> charged the wheel correctly and
        /// <see cref="StraightLineCostCeiling"/> is 4, so 2,26x passed a gate
        /// meant to catch detours of a different order. The fault is the shape:
        /// a crab that runs the whole way is not threading anything, so the
        /// premise of the rung is false and the rung should decline rather than
        /// return. Declined and not returned for the same reason as M79 - a
        /// return short-circuits everything below it, and below it here the
        /// search had a 359 s answer that was never asked for.
        /// </para>
        /// <para>
        /// <b>The share, not the branch.</b> A crab covering 95% of a route is
        /// the same fault as one covering all of it, and one number covers both
        /// shapes <c>CrabThrough</c> can draw.
        /// </para>
        /// <para>
        /// <b>Three quarters, and it costs nothing.</b> The whole-way crab is
        /// rare and ruinous, which is the profile worth refusing: over 160 bench
        /// orders on two fields it fires <i>not once</i>, so 0,90 and 0,75
        /// decline nothing and leave all 29 and 26 genuine crabs standing -
        /// worst 2,8x and 3,0x, mean 1,31x and 1,28x, 63 and 70 held, every
        /// figure unmoved. Only at 0,50 does a real crab start being refused,
        /// and it costs: broken country's worst goes 3,0x to 3,1x and its mean
        /// 1,28x to 1,30x. So the two safe values are indistinguishable by
        /// measurement and the rule picks between them - three quarters of a
        /// journey walked side-on is not a manoeuvre at a gap by any reading,
        /// and the lower of the two catches more of the same fault.
        /// </para>
        /// <para>
        /// On the recorded order: 645 s becomes 348 s, 2,3x becomes 1,2x, the
        /// rung falls from 3 to 5, and no leg is walked side-on at all.
        /// </para>
        /// </remarks>
        internal static float CrabbedShareCeiling = 0.75f;

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
            PoseAsked, PoseWon, PoseWidened, PoseTooDear, Pressed, GridClean, TangentTooDear,
            WayRoundTooDear, CrabTooLong;

        internal static void ResetCounters()
        {
            Staged = LadderClean = LadderBent = TangentClean = CornersClean = RingsClean =
                PoseAsked = PoseWon = PoseWidened = PoseTooDear = Pressed = GridClean =
                    TangentTooDear = WayRoundTooDear = CrabTooLong =
                    BadFirstLeg = BadLaterLeg = BadPressed = BadNoRoute = 0;

            HybridPlanning.HybridAStarPlanner.RanOutOfTime = 0;
            GridPlanning.GridRoutePlanner.ResetCounters();
        }

        public string Name => "staged ladder with tangent recovery";

        public Plan PlanTo(
            BattleState battle, UnitInstance unit, IPathfinder pathfinder, Vec2 destination,
            IBattleLog? log = null, IWayRound? wayRound = null, Facing? arriveOn = null)
        {
            if (battle == null) throw new ArgumentNullException(nameof(battle));
            if (unit == null) throw new ArgumentNullException(nameof(unit));
            if (pathfinder == null) throw new ArgumentNullException(nameof(pathfinder));

            // Every route this planner hands out is cast ahead and straightened,
            // whichever search produced it. A ladder route that walks left, up
            // and left again to a destination one diagonal away is the same
            // defect as a lattice route with 154 sample points in it, and the
            // fix cannot belong to one of them.
            Plan chosen = Choose(battle, unit, pathfinder, destination, log, wayRound, arriveOn);
            Plan straightened = RouteSmoothing.Applied(battle, unit, chosen);

            // Straightening must never turn a route the executor would walk
            // into one it refuses. It is proved against the same gate the
            // route itself had to pass, and the wound route stands if it fails.
            if (ReferenceEquals(chosen.Path.Waypoints, straightened.Path.Waypoints)) return chosen;

            return WalksCleanly(battle, unit, straightened) ? straightened : chosen;
        }

        private static Plan Choose(
            BattleState battle, UnitInstance unit, IPathfinder pathfinder, Vec2 destination,
            IBattleLog? log, IWayRound? wayRound, Facing? arriveOn)
        {

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

            // ---- the regiment grid, M77 -------------------------------------
            //
            // Asked here because this is exactly where the cheap planners have
            // run out and the dear one is about to be asked. A cell holds a
            // whole regiment, so the search is over ground rather than over
            // poses and costs a hex A* across a field of about 2 700 cells -
            // three orders of magnitude under the lattice, whose worst case is
            // set by the arrangement rather than by the map.
            IReadOnlyList<Vec2>? gridRoute = null;

            if (GridPlanning.GridRoutePlanner.Use != GridPlanning.GridUse.Off)
            {
                GridPlanning.GridRoutePlanner.Asked++;
                gridRoute = GridPlanning.GridRoutePlanner.RouteFor(battle, unit, destination);

                if (gridRoute != null) GridPlanning.GridRoutePlanner.Found++;
            }

            // In Corridor the route is guidance and is deliberately not taken,
            // which is what keeps that arrangement a clean test of the tube
            // rather than a test of the tube and the route together.
            if (gridRoute != null && GridPlanning.GridRoutePlanner.Use != GridPlanning.GridUse.Corridor)
            {
                float gridLength = GridPlanning.GridRoutePlanner.Length(gridRoute);

                var gridded = new Plan(
                    PathResult.Success(
                        gridRoute, Array.Empty<Coord>(), gridLength, gridLength,
                        GridPlanning.RegimentGrid.LastCellsExplored),
                    null, false);

                // Straightened before it is judged, unlike every other stage
                // here. A hex route is a chain of cell centres and zigzags by
                // construction, so gating the raw line asks the swept
                // rectangle to walk a staircase - and it refuses one, which is
                // a verdict on the shape of the grid rather than on whether
                // the regiment can get there. Measured: on the Crucible this
                // is 42 routes held against 33, and on Broken Country 52
                // against 49. PlanTo smooths again afterwards and that pass is
                // then a no-op, which is the right kind of waste.
                gridded = RouteSmoothing.Applied(battle, unit, gridded);

                // The same gate every other route in this project has to pass.
                // A grid cell is coarser than the swept rectangle, so a grid
                // route is a claim about roughly where to go and not a promise
                // that the regiment fits; this is where that claim is tested.
                // Priced against the press it exists to avoid, exactly as the
                // lattice's answer is. Without this the grid is the one stage
                // that may hand back any detour at all provided it walks - and
                // it promptly did: U19 on the frozen-orders fixture took a
                // grid route costing 1 316 s against a 177 s straight line,
                // 7,4x, which is the regiment-refuses-the-order failure M65
                // exists to stop. A cheap search is not a licence to skip the
                // ceiling; it is only a licence to reach it sooner.
                bool tooDear =
                    WayRoundCostCeiling > 0f && ladder.Path.Found && ladder.PressedThrough &&
                    CostsMoreThan(battle, unit, gridded, ladder, WayRoundCostCeiling);

                if (!tooDear && WalksCleanly(battle, unit, gridded))
                {
                    GridClean++;
                    GridPlanning.GridRoutePlanner.Held++;

                    log?.Record(new BattleLogEntry(
                        LogLevel.Decision, "Path",
                        $"{unit.Def.DisplayName} took a grid route to {destination} " +
                        $"({gridded.Path.Waypoints.Count} waypoints over " +
                        $"{GridPlanning.RegimentGrid.LastCellsExplored} cells settled, " +
                        $"{GridPlanning.RegimentGrid.LastBlockedCells} cells held by bodies) " +
                        $"- the lattice was not asked.",
                        unit.Id));

                    return gridded;
                }
            }

            // Nothing cheap could route this cleanly, so before shouldering
            // through anybody, ask the one planner that searches poses rather
            // than points.  It is dear — tens of milliseconds — but it is asked
            // only for the orders that would otherwise press, and Mx2c says a
            // press is Priority 3: what a way round costs is not a reason to
            // prefer walking through your own men.
            if (PoseSearchBeforePressing && GridPlanning.GridRoutePlanner.Use != GridPlanning.GridUse.Replace)
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
                // The grid route first, where there is one. The tube was
                // turned off because the cheap route it was drawn round was a
                // press-through on 74 of 94 orders, so it enclosed a line
                // through the middle of a regiment and there was no answer
                // near it to find. A grid route cannot be a press-through:
                // cells holding a body are not enterable, so going round them
                // is the only thing it can express. That is the one defect
                // this fixes, and the reason the tube is worth asking about
                // again at all.
                IReadOnlyList<Vec2>? tube =
                    GridPlanning.GridRoutePlanner.Use == GridPlanning.GridUse.Corridor && gridRoute != null && gridRoute.Count >= 2
                        ? gridRoute
                        : CorridorFromCheapRoute && tangent.Path.Found &&
                          tangent.Path.Waypoints.Count >= 2
                            ? tangent.Path.Waypoints
                            : null;

                // What the way round is allowed to cost, told to the search
                // rather than applied to its answer. M65 was throwing away
                // routes that took 888 to 1080 ms to find; a limit the search
                // knows about turns that into a refusal it can reach in a
                // fraction of the time, because the turn field's estimate is
                // admissible and can rule the whole thing out at the start.
                float limit = 0f;

                if (WayRoundCostCeiling > 0f && ladder.Path.Found && ladder.PressedThrough)
                {
                    float pressed = Marching.SecondsToWalk(
                        battle, unit, ladder.Path.Waypoints, ladder.Hold);

                    if (pressed > 1f) limit = pressed * WayRoundCostCeiling;
                }

                // With no press to price against, the empty field is the
                // yardstick. Told to the search rather than applied to its
                // answer, for the same reason as above: an admissible estimate
                // can rule the whole thing out before the first expansion.
                if (limit <= 0f && StraightLineCostCeiling > 0f)
                {
                    float straight = StraightSeconds(battle, unit, destination);
                    if (straight > 1f) limit = straight * StraightLineCostCeiling;
                }

                Plan posed;

                if (tube != null)
                {
                    posed = HybridAStarRoutePlanner.PlanAlong(
                        battle, unit, destination, arriveOn,
                        tube, CheapCorridorHalfWidthMetres, log, BoundedBudget, limit);
                }
                else if (CorridorHalfWidthMetres > 0f)
                {
                    posed = HybridAStarRoutePlanner.PlanAlong(
                        battle, unit, destination, arriveOn,
                        corridor: null, CorridorHalfWidthMetres, log, BoundedBudget, limit);
                }
                else
                {
                    posed = HybridAStarRoutePlanner.PlanAlong(
                        battle, unit, destination, arriveOn, corridor: null, 0f, log,
                        expansionBudget: PoseExpansionBudget > 0 ? PoseExpansionBudget : null,
                        secondsLimit: limit);
                }

                bool bounded = tube != null || CorridorHalfWidthMetres > 0f;

                if (bounded &&
                    (!posed.Path.Found || posed.PressedThrough || !WalksCleanly(battle, unit, posed)))
                {
                    PoseWidened++;
                    posed = HybridAStarRoutePlanner.PlanAlong(
                        battle, unit, destination, arriveOn, corridor: null, 0f, log,
                        expansionBudget: PoseExpansionBudget > 0 ? PoseExpansionBudget : null,
                        secondsLimit: limit);
                }

                if (posed.Path.Found && !posed.PressedThrough &&
                    WalksCleanly(battle, unit, posed))
                {
                    // Priced against the press it exists to avoid, in the
                    // executor's own currency rather than in metres - the
                    // seconds a route takes are what a player waits through,
                    // and a heading change costs seconds while covering no
                    // ground at all.
                    if (WayRoundCostCeiling > 0f &&
                        ladder.Path.Found && ladder.PressedThrough &&
                        CostsMoreThan(battle, unit, posed, ladder, WayRoundCostCeiling))
                    {
                        PoseTooDear++;
                    }
                    else
                    {
                        PoseWon++;
                        return posed;
                    }
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

            // The terminal fallback, which until now handed back whatever the
            // tangent graph drew without any stage having priced it. It is
            // still handed back - a regiment with no route at all is worse than
            // one with a long one - but it is counted, so a detour of this kind
            // shows up in a recording instead of only in a screenshot.
            if (tangent.Path.Found)
            {
                if (StraightLineCostCeiling > 0f)
                {
                    float straight = StraightSeconds(battle, unit, destination);
                    float around = Marching.SecondsToWalk(
                        battle, unit, tangent.Path.Waypoints, tangent.Hold);

                    if (straight > 1f && around > straight * StraightLineCostCeiling)
                        TangentTooDear++;
                }

                return tangent;
            }

            return ladder;
        }

        /// <summary>
        /// Whether one route costs more than <paramref name="multiple"/> times
        /// another, in the seconds the walker will actually spend.
        /// </summary>
        /// <remarks>
        /// Both sides go through <see cref="Marching.SecondsToWalk"/> with
        /// their own held fronts, because that is the executor's model and the
        /// only currency every planner here shares. A planner's own idea of
        /// what its route cost is not comparable with another's - the lattice
        /// minimises seconds over a heading lattice, the ladder measures
        /// metres - and comparing those directly is how a route that is
        /// cheaper on paper turns out to be five times longer to walk.
        /// </remarks>
        /// <summary>
        /// What this order would take on an empty field, which is the only
        /// yardstick available when nothing else found a route to compare with.
        /// </summary>
        private static float StraightSeconds(
            BattleState battle, UnitInstance unit, Vec2 destination) =>
            Marching.SecondsToWalk(battle, unit, new[] { unit.Position, destination }, null);

        private static bool CostsMoreThan(
            BattleState battle, UnitInstance unit, Plan round, Plan press, float multiple)
        {
            float pressed = Marching.SecondsToWalk(
                battle, unit, press.Path.Waypoints, press.Hold);

            // A press that costs nothing measurable cannot be exceeded by a
            // multiple of itself, so the ceiling stands aside rather than
            // refusing every way round on a division by almost zero.
            if (pressed <= 1f) return false;

            float around = Marching.SecondsToWalk(
                battle, unit, round.Path.Waypoints, round.Hold);

            return around > pressed * multiple;
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
