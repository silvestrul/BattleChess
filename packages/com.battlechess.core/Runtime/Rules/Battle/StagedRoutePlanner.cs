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

        /// <summary>How many orders left the cascade early on the clock.</summary>
        /// <remarks>
        /// Kept because a budget that fires constantly is not a safety net, it
        /// is the planner - and the only way to tell the two apart is to count.
        /// </remarks>
        internal static int OutOfTimeAtTheGrid;

        /// <summary>
        /// How many orders were already out of time when they reached the
        /// escape, whether or not they had an answer to leave with.
        /// </summary>
        internal static int OutOfTimeReachedTheGrid;

        /// <summary>
        /// Orders that ran out of time holding nothing, and so carried on.
        /// </summary>
        /// <remarks>
        /// <b>The remaining hole in the cap, kept visible on purpose.</b> A gate
        /// can only leave with an answer somebody proved, and an order whose
        /// ladder found neither a route nor a press has none - so it carries on
        /// past every gate and is bounded by nothing. Refusing it instead would
        /// leave a regiment standing still because planning was slow, which is a
        /// rule about what the game does rather than about what it costs, and
        /// [W-ask] says that is the designer's to settle. Counted so the size of
        /// the hole is a number rather than a worry.
        /// </remarks>
        internal static int OutOfTimeWithNothing;

        /// <summary>Which gate the clock stopped an order at, for the record.</summary>
        internal static int StoppedBeforeCoarse, StoppedBeforeFine, StoppedBeforeGraphs,
            StoppedBeforePose;

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
        /// <para>
        /// <b>Three and a tenth, and it is the knee - [M88a].</b> The designer
        /// set three and rejected two and a half as too little; measured over
        /// both order patterns on all four fields, they are right on both
        /// counts and the tenth is not decoration. Sweeping 2 to 4 with every
        /// wing sent to one block, <b>3,1 is the smallest ceiling at which no
        /// way round anywhere is refused as too dear</b> - at 3,0 the Crucible
        /// still refuses one - and it takes the Crucible from 16 presses to 15
        /// and Broken Country from 10 to 8. Above it nothing further is bought:
        /// 3,5 is identical, and 4,0 buys two more routes at the price of a
        /// worst detour of 3,9x. At 2,5 the two fields press 18 and 13 times
        /// with two and three way rounds refused outright, which is the
        /// designer's objection in numbers.
        /// </para>
        /// </remarks>
        internal static float WayRoundCostCeiling = ShippedWayRoundCostCeiling;

        /// <summary>The value <see cref="WayRoundCostCeiling"/> ships at.</summary>
        /// <remarks>
        /// Named because four separate places restore the lever by writing the
        /// number out again, and a default that lives in five places drifts the
        /// first time it moves. It moved - [M88a] - and all four of the others
        /// were still on the old value.
        /// </remarks>
        internal const float ShippedWayRoundCostCeiling = 3.1f;

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

        /// <summary>
        /// Whether the tangent graph is asked as a stage of its own, between
        /// the grid and the lattice.
        /// </summary>
        /// <remarks>
        /// Separate from whether the tangent search is reachable at all: the
        /// plan it draws is still the terminal fallback at the bottom of this
        /// cascade, and the local <c>Tangents()</c> draws it at most once an
        /// order however many places below want it. Turning this off removes
        /// the stage, not the search.
        /// </remarks>
        /// <remarks>
        /// <b>Off, on the measurement.</b> From above the grid the stage won
        /// <b>0 of 280</b> bench orders. Moved below the grid it is reached by
        /// only 27 of them - 9, 0, 10 and 8 on the four fields - and wins
        /// <b>0 of those too</b>. Turned off: <b>not one of the 280 routes
        /// moves</b>, total marching time is identical to the tenth of a
        /// second on every field, and no order falls through to the terminal
        /// fallback, because the lattice answers every one the grid could not.
        /// That is the whole case: the orders reaching this stage are exactly
        /// the ones it was always going to refuse.
        /// <b>What still draws the graph.</b> An attack order, which goes
        /// straight to it and never enters the cascade; and the terminal
        /// fallback, which has not fired on any bench field but is what stands
        /// between a regiment and no route at all. Neither is removed by this,
        /// which is why it is a lever on the stage and not a deletion of the
        /// search.
        /// </remarks>
        internal static bool AskTangentStage;

        /// <summary>
        /// The cell size of the second, finer grid, as a multiple of a
        /// regiment's own width. Nought turns the second tier off.
        /// </summary>
        /// <remarks>
        /// <b>M87.</b> A cell holds a whole regiment, so the coarse grid cannot
        /// express a way round narrower than one — and the orders it fails on
        /// are exactly the ones that fell to the lattice at tens of milliseconds
        /// each. Measured globally at a quarter, the grid answered <b>all</b>
        /// thirty-two asked orders on the Crucible and Broken Country and the
        /// lattice ran on none, with the routes coming out <b>13,9% and 9,2%
        /// shorter in marching time</b> — the fine grid does not merely match
        /// the lattice, it beats it. Globally it is still a loss, because a
        /// quarter cell is sixteen times the cells and all thirty-two orders pay
        /// for it. Hence two tiers: the coarse grid answers what it can, and
        /// only what it cannot reaches the fine one, so the sixteen-fold field
        /// is paid on nine or ten orders rather than on thirty-two.
        /// </remarks>
        internal static float[] FineSpacings = { 0.5f, 0.25f };

        /// <summary>Measurement counters: how many orders reach each stage.</summary>
        internal static int Staged, LadderClean, LadderBent, TangentClean, CornersClean, RingsClean,
            PoseAsked, PoseWon, PoseWidened, PoseTooDear, Pressed, GridClean, TangentTooDear,
            WayRoundTooDear, CrabTooLong;

        /// <summary>
        /// How many orders actually drew the tangent graph, as against how many
        /// reached a stage that wanted it. The gap between those two numbers is
        /// the whole of what M86 bought.
        /// </summary>
        internal static int TangentAsked;

        /// <summary>When a grid route is bent enough to be worth asking the pose search about.</summary>
        /// <remarks>
        /// [M131]. <see cref="SecondOpinion.Always"/> is the upper bound on what
        /// the rule can buy rather than a shipping candidate: it runs the pose
        /// search on every order the grid answers.
        /// </remarks>
        internal enum SecondOpinion
        {
            /// <summary>Take the grid's route whatever shape it is, as before [M131].</summary>
            Off,

            /// <summary>Ask when a waypoint turns through more than <see cref="SecondOpinionTurnDegrees"/>.</summary>
            ByTurn,

            /// <summary>Ask when the route is longer than <see cref="SecondOpinionDetour"/> times its straight line.</summary>
            ByDetour,

            /// <summary>Ask when either fires.</summary>
            ByEither,

            /// <summary>Ask on every grid route.</summary>
            Always,
        }

        /// <inheritdoc cref="SecondOpinion"/>
        internal static SecondOpinion AskAgainWhenBent = SecondOpinion.ByTurn;

        /// <summary>The turn a grid route may contain before it is worth a second opinion.</summary>
        internal static float SecondOpinionTurnDegrees = 90f;

        /// <summary>What a grid route may cost against its own straight line before the same.</summary>
        internal static float SecondOpinionDetour = 1.25f;

        internal static int SecondOpinionAsked, SecondOpinionTook, SecondOpinionRefused;

        /// <summary>Why a second opinion was refused. [M131a].</summary>
        internal static int SecondOpinionNoRoute, SecondOpinionPressed,
            SecondOpinionTooDear, SecondOpinionDirty;

        /// <summary>Refusals by <see cref="PathFailure"/>, and states expanded before them.</summary>
        internal static readonly int[] SecondOpinionWhy = new int[16];

        /// <inheritdoc cref="SecondOpinionWhy"/>
        internal static long SecondOpinionExpansions;

        /// <summary>How many second opinions were asked by a mover already lapping its own.</summary>
        internal static int SecondOpinionStartedLapping;

        /// <summary>Primitives tried and bodies avoided, across the refusals.</summary>
        internal static long SecondOpinionPrimitives, SecondOpinionBodies;

        /// <summary>Footprints on each side of the verdict. [M132].</summary>
        internal static float SecondOpinionTookArea, SecondOpinionNoRouteArea,
            SecondOpinionTookWidest, SecondOpinionNoRouteNarrowest = float.MaxValue;

        /// <summary>Where the lattice stopped, by <see cref="StopSlot"/>.</summary>
        internal static readonly int[] SecondOpinionStops = new int[6];

        /// <summary>The names of those slots, in order.</summary>
        internal static readonly string[] StopNames =
        {
            "found it", "past the limit at the start", "nothing left to expand",
            "the expansion budget", "the clock", "superseded",
        };

        private static int StopSlot(string? stop) => stop switch
        {
            null => 0,
            "refused at the start: even the best case is past the limit" => 1,
            "nothing left to expand" => 2,
            "nothing left to expand (bounded)" => 2,
            "the expansion budget" => 3,
            "the clock" => 4,
            _ => 5,
        };

        /// <summary>Whether this route is bent enough to be worth asking again about.</summary>
        /// <remarks>
        /// Measured on the route as it will be walked, after smoothing and after
        /// the fronts are settled - not on the raw chain of cell centres, which
        /// zigzags by construction and would fire on everything.
        /// </remarks>
        private static bool WorthASecondOpinion(IReadOnlyList<Vec2> way)
        {
            if (AskAgainWhenBent == SecondOpinion.Always) return true;
            if (way == null || way.Count < 2) return false;

            bool byTurn =
                AskAgainWhenBent == SecondOpinion.ByTurn ||
                AskAgainWhenBent == SecondOpinion.ByEither;

            bool byDetour =
                AskAgainWhenBent == SecondOpinion.ByDetour ||
                AskAgainWhenBent == SecondOpinion.ByEither;

            if (byTurn && SecondOpinionTurnDegrees > 0f)
            {
                for (int i = 1; i < way.Count - 1; i++)
                {
                    Vec2 into = way[i] - way[i - 1];
                    Vec2 outOf = way[i + 1] - way[i];

                    if (into.IsNearZero || outOf.IsNearZero) continue;

                    float turn = MathF.Abs(
                        Facing.FromVector(outOf).Radians - Facing.FromVector(into).Radians);

                    if (turn > MathF.PI) turn = 2f * MathF.PI - turn;

                    if (turn * 180f / MathF.PI > SecondOpinionTurnDegrees) return true;
                }
            }

            if (byDetour && SecondOpinionDetour > 0f)
            {
                float walked = 0f;
                for (int i = 1; i < way.Count; i++) walked += Vec2.Distance(way[i - 1], way[i]);

                float straight = Vec2.Distance(way[0], way[way.Count - 1]);

                if (straight > 1f && walked > straight * SecondOpinionDetour) return true;
            }

            return false;
        }

        /// <summary>Why the pose search did not win: no route, a press, a route that will not walk.</summary>
        /// <remarks>[M130]. <see cref="PoseTooDear"/> is the fourth door and was already counted.</remarks>
        internal static int PoseNoRoute, PosePressed, PoseDirty;

        internal static void ResetCounters()
        {
            Staged = LadderClean = LadderBent = TangentClean = CornersClean = RingsClean =
                PoseAsked = PoseWon = PoseWidened = PoseTooDear = Pressed = GridClean =
                    SidewalkAsked = SidewalkTook = ArrivalAsked = ArrivalTook = SmoothingRefused =
                    TangentTooDear = WayRoundTooDear = CrabTooLong =
                    TangentAsked = BadFirstLeg = BadLaterLeg = BadPressed = BadNoRoute = 0;

            PoseNoRoute = PosePressed = PoseDirty = 0;
            SecondOpinionAsked = SecondOpinionTook = SecondOpinionRefused = 0;
            SecondOpinionNoRoute = SecondOpinionPressed =
                SecondOpinionTooDear = SecondOpinionDirty = 0;
            SecondOpinionExpansions = 0L;
            SecondOpinionStartedLapping = 0;
            SecondOpinionPrimitives = SecondOpinionBodies = 0L;
            SecondOpinionTookArea = SecondOpinionNoRouteArea = SecondOpinionTookWidest = 0f;
            SecondOpinionNoRouteNarrowest = float.MaxValue;
            Array.Clear(SecondOpinionWhy, 0, SecondOpinionWhy.Length);
            Array.Clear(SecondOpinionStops, 0, SecondOpinionStops.Length);
            OutOfTimeAtTheGrid = OutOfTimeReachedTheGrid = OutOfTimeWithNothing = 0;
            StoppedBeforeCoarse = StoppedBeforeFine = StoppedBeforeGraphs =
                StoppedBeforePose = 0;
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

            // The licence to finish in contact is withheld on the first ask -
            // M94a, and it is the designer's own rule for crabbing applied to the
            // arrival: always verify whether the option that needs no licence
            // is possible, take it if it is, and only then price the one that
            // does. Measured over the nineteen approach angles, granting it
            // outright cost three angles a clean arrival and bought two others
            // a route four times dearer than the press it replaced.
            Plan withheld = Cast(battle, unit, pathfinder, destination, log, wayRound, arriveOn);

            if (!LicenceOnArrival) return withheld;

            // Anything that already answers without the licence keeps its
            // answer, whatever the licence might have found instead.
            if (withheld.Path.Found && !withheld.PressedThrough &&
                WalksCleanly(battle, unit, withheld))
                return withheld;

            ArrivalAsked++;

            Plan licensed;
            bool walks;

            _arrivalLicensed = true;

            try
            {
                licensed = Cast(battle, unit, pathfinder, destination, log, wayRound, arriveOn);

                // Asked while the licence is in force, because the licence is
                // precisely what makes the last leg legal.
                walks = licensed.Path.Found && !licensed.PressedThrough &&
                        WalksCleanly(battle, unit, licensed);
            }
            finally
            {
                _arrivalLicensed = false;
            }

            if (!walks) return withheld;

            // And it does not escape the ceiling, nor may it make an answer
            // already in hand dearer. A press-through is a real answer priced
            // at [M88]'s multiple, and a way round that exists only because the
            // arrival was licensed is still a way round; against anything else
            // the licence has to actually win on the clock, because the pass
            // that found it was the pass that could not find anything better.
            if (withheld.Path.Found &&
                CostsMoreThan(
                    battle, unit, licensed, withheld,
                    withheld.PressedThrough ? WayRoundCostCeiling : 1f))
                return withheld;

            ArrivalTook++;

            return licensed;
        }

        /// <summary>
        /// One pass of the cascade, cast ahead and straightened.
        /// </summary>
        /// <remarks>
        /// Every route this planner hands out is straightened, whichever search
        /// produced it. A ladder route that walks left, up and left again to a
        /// destination one diagonal away is the same defect as a lattice route
        /// with 154 sample points in it, and the fix cannot belong to one of
        /// them.
        /// </remarks>
        private static Plan Cast(
            BattleState battle, UnitInstance unit, IPathfinder pathfinder, Vec2 destination,
            IBattleLog? log, IWayRound? wayRound, Facing? arriveOn)
        {
            Plan chosen = Choose(battle, unit, pathfinder, destination, log, wayRound, arriveOn);
            Plan straightened = RouteSmoothing.Applied(battle, unit, chosen);

            // Straightening must never turn a route the executor would walk
            // into one it refuses. It is proved against the same gate the
            // route itself had to pass, and the wound route stands if it fails.
            if (!ReferenceEquals(chosen.Path.Waypoints, straightened.Path.Waypoints) &&
                !WalksCleanly(battle, unit, straightened))
                straightened = chosen;

            // M99. The fronts, last, and on whichever route survived - the
            // question "which way is this regiment going" can only be asked of
            // the legs it is actually going to walk. Same guard as above and
            // for the same reason: this pass moves no waypoint, but it does
            // change the shape the sweep is taken at, so it has to answer to
            // the gate the route already passed.
            Plan facing;

            using (PlanningProfile.Measure(PlanningProfile.Step.Fronts))
                facing = RouteFronts.Applied(battle, unit, straightened);

            if (ReferenceEquals(straightened.Hold, facing.Hold)) return straightened;

            return WalksCleanly(battle, unit, facing) ? facing : straightened;
        }

        private static Plan Choose(
            BattleState battle, UnitInstance unit, IPathfinder pathfinder, Vec2 destination,
            IBattleLog? log, IWayRound? wayRound, Facing? arriveOn)
        {
            // The tangent graph, drawn at most once an order however many
            // places below want it: the attack path above, the stage below the
            // grid, the tube the lattice may be bounded to, and the terminal
            // fallback. Four callers for one search, and until M86 it ran
            // eagerly whether or not any of them was reached - which on the
            // bench meant it ran 83 times and answered nothing.
            Plan? drawn = null;

            Plan Tangents()
            {
                if (drawn == null)
                {
                    using var _tangents = PlanningProfile.Measure(PlanningProfile.Step.TangentGraph);

                    TangentAsked++;

                    drawn = RouteSearch.Find(
                        battle, unit, destination, arriveOn ?? unit.OrderFacing, log, pathfinder,
                        RouteSearch.Shape.Tangents);
                }

                return drawn.Value;
            }

            // Attacks have an approach planner and a moving target.  Their
            // repeated short plans are deliberately governed by OrderSystem's
            // chase cadence; staging a terrain march here would make that
            // cadence observe a different route shape every time the target
            // moves.  Keep the established tangent behaviour until attacks get
            // their own reservation-aware approach phase.
            if (unit.Order.Kind == OrderKind.Attack)
            {
                return Tangents();
            }

            // An egress is only useful when it is a real staging manoeuvre:
            // clear the bodies currently lapped, then leave on a leg that can
            // reach the order directly.  More complicated detours remain the
            // ladder/tangent planners' job, where their candidates are richer.
            bool stages;
            Plan staged;

            using (PlanningProfile.Measure(PlanningProfile.Step.Staging))
                stages = TryStageForDirectRun(battle, unit, destination, out staged);

            if (stages)
            {
                Staged++;
                return staged;
            }

            Plan ladder = Marching.ByTheLadder(battle, unit, pathfinder, destination, log, wayRound);

            // M114. The clock, asked at every stage boundary rather than once.
            //
            // [M113] measured the single door this cascade used to have and
            // found it never opened: the count of orders arriving at it already
            // out of time was <b>zero at every budget</b>, because it sat after
            // the cheap coarse grid while the milliseconds go in the dear stages
            // past it. A cap checked before the expensive work cannot bound the
            // expensive work, so it is now checked before each piece of it.
            //
            // What a gate hands back is what is already in hand and already
            // proved: the ladder's route, or the press it declared. M98 - a
            // declared press is a legitimate answer. What is never returned here
            // is a route no stage checked.
            //
            // The clock is asked between stages and never inside one. A field is
            // cached and thereafter patched, so a raise abandoned half way would
            // be stamped current and read wrong by every later order - which is
            // exactly the fault [M104] shipped and [M113]'s gate must not
            // reintroduce. A stage either runs or does not start.
            bool NoTimeLeft(ref int gate, string before)
            {
                if (!Marching.StopNow()) return false;

                OutOfTimeReachedTheGrid++;

                // Nothing proved to leave with. Carrying on is the conservative
                // reading: the alternative is refusing the order outright, and a
                // regiment standing still because planning was slow is a change
                // to what the game does, not to what it costs.
                if (!ladder.Path.Found)
                {
                    OutOfTimeWithNothing++;
                    return false;
                }

                OutOfTimeAtTheGrid++;
                gate++;

                // Said only when the answer is still wanted. A superseded order
                // is thrown away on arrival, so a line explaining what it
                // settled for would be a line about a route nobody walks - and
                // M80's whole point is that the click which superseded it is
                // the event worth reading about.
                if (Marching.OutOfTime())
                    log?.Info("Path",
                        $"{unit.Def.DisplayName} ran out of its search budget " +
                        $"({Marching.SearchBudgetMs:0} ms) before {before} — taking the ladder's " +
                        $"{(ladder.PressedThrough ? "press-through" : "route")}.",
                        unit.Id);

                return true;
            }

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

            // ---- the regiment grid, M77 -------------------------------------
            //
            // Asked here because this is exactly where the cheap planners have
            // run out and the dear one is about to be asked. A cell holds a
            // whole regiment, so the search is over ground rather than over
            // poses and costs a hex A* across a field of about 2 700 cells -
            // three orders of magnitude under the lattice, whose worst case is
            // set by the arrangement rather than by the map.
            if (NoTimeLeft(ref StoppedBeforeCoarse, "the regiment grid")) return ladder;

            IReadOnlyList<Vec2>? gridRoute = null;

            if (GridPlanning.GridRoutePlanner.Use != GridPlanning.GridUse.Off)
            {
                using var _coarse = PlanningProfile.Measure(PlanningProfile.Step.GridCoarse);

                GridPlanning.GridRoutePlanner.Asked++;
                gridRoute = GridPlanning.GridRoutePlanner.RouteFor(battle, unit, destination);

                if (gridRoute != null) GridPlanning.GridRoutePlanner.Found++;
            }

            // In Corridor the route is guidance and is deliberately not taken,
            // which is what keeps that arrangement a clean test of the tube
            // rather than a test of the tube and the route together.
            bool takeIt =
                GridPlanning.GridRoutePlanner.Use != GridPlanning.GridUse.Corridor;

            if (takeIt && TookGridRoute(gridRoute, fine: false, out Plan overGrid))
                return overGrid;

            // ---- out of time ------------------------------------------------
            //
            // Everything below here escalates: the fine tier asks grids of four
            // and sixteen times the field, the tangent graph draws a visibility
            // set, and the pose search is tens of milliseconds by design. A
            // budget that merely interrupted the coarse grid would hand the
            // order to all three, which is how the first measurement of this
            // made the Crucible slower rather than faster - 81 ms to 98, and the
            // worst order from 6 to 23.
            if (NoTimeLeft(ref StoppedBeforeFine, "the finer grids")) return ladder;

            // ---- the fine tier, M87 -----------------------------------------
            //
            // Reached only where the coarse grid could not answer, which is the
            // whole design: a quarter cell is sixteen times the field, and it is
            // affordable precisely because nine or ten orders a field get here
            // rather than thirty-two. What they would otherwise cost is the
            // lattice, at tens of milliseconds each.
            if (takeIt && GridPlanning.GridRoutePlanner.Use != GridPlanning.GridUse.Off)
            {
                // Finest last. A half cell is four times the field and a quarter
                // is sixteen, so asking the cheaper one first costs a quarter of
                // the dear one's field on the orders that need the dear one, and
                // saves the whole of it on the orders that do not. Measured, the
                // two fields want different tiers - the sideways mile is
                // answered at a half and the Crucible needs a quarter - which is
                // the argument for a ladder here rather than one number.
                using var _fine = PlanningProfile.Measure(PlanningProfile.Step.GridFine);

                foreach (float finer in FineSpacings)
                {
                    if (finer <= 0f) continue;

                    // Asked per spacing, not per tier. Half and quarter are four
                    // and sixteen times the field, so an order that has time for
                    // one of them has not thereby got time for the other.
                    if (NoTimeLeft(ref StoppedBeforeFine, $"the grid at {finer:0.##}"))
                        return ladder;

                    GridPlanning.GridRoutePlanner.FineAsked++;

                    IReadOnlyList<Vec2>? fineRoute = GridPlanning.GridRoutePlanner.RouteFor(
                        battle, unit, destination, finer);

                    if (fineRoute != null) GridPlanning.GridRoutePlanner.FineFound++;

                    if (TookGridRoute(fineRoute, fine: true, out Plan overFine)) return overFine;
                }
            }

            // The body of that gate, written once and asked twice.
            bool TookGridRoute(IReadOnlyList<Vec2>? route, bool fine, out Plan taken)
            {
                taken = default;

                if (route == null) return false;

                float gridLength = GridPlanning.GridRoutePlanner.Length(route);

                var gridded = new Plan(
                    PathResult.Success(
                        route, Array.Empty<Coord>(), gridLength, gridLength,
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
                // M91. A grid route names places, never fronts, so every leg of
                // it would otherwise be walked on the line of march whether or
                // not the regiment can turn onto it. Asked before smoothing as
                // well as after, because the fronts it settles are what the
                // smoother then has to preserve.
                gridded = Sidewalked(battle, unit, gridded);

                // M94. Smoothing may shorten a route; it may not break one.
                // It casts with `leaving: true` on the first leg unconditionally
                // while the gate is stricter, so a long cast it believes clear
                // can be one the executor refuses - and the route it replaced
                // walked. Measured at the failing approach, the half-cell route
                // walks in full and its smoothed form fails on leg one.
                Plan straightened = Sidewalked(
                    battle, unit, RouteSmoothing.Applied(battle, unit, gridded));

                if (!RefuseSmoothingThatBreaks || WalksCleanly(battle, unit, straightened))
                    gridded = straightened;
                else SmoothingRefused++;

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

                if (tooDear || !WalksCleanly(battle, unit, gridded)) return false;

                GridClean++;

                if (fine) GridPlanning.GridRoutePlanner.FineHeld++;
                else GridPlanning.GridRoutePlanner.Held++;

                log?.Record(new BattleLogEntry(
                    LogLevel.Decision, "Path",
                    $"{unit.Def.DisplayName} took a {(fine ? "fine " : string.Empty)}grid " +
                    $"route to {destination} " +
                    $"({gridded.Path.Waypoints.Count} waypoints over " +
                    $"{GridPlanning.RegimentGrid.LastCellsExplored} cells settled, " +
                    $"{GridPlanning.RegimentGrid.LastBlockedCells} cells held by bodies) " +
                    $"- the lattice was not asked.",
                    unit.Id));

                // [M131]. The grid buys coverage with shape: it answers one
                // order in nine that nothing else can walk, and charges a mean
                // detour of 1,43 and twenty-one routes of eighty turning through
                // more than ninety degrees [M130]. Until here nothing asked
                // whether the shape it bought was worth it.
                //
                // So a bent grid route gets a second opinion, and the pose
                // search's answer is taken only if it walks. **The grid's route
                // stays in hand as the fallback, so coverage cannot drop by
                // construction** - which is the property that makes this safe to
                // turn on at all.
                if (AskAgainWhenBent != SecondOpinion.Off &&
                    WorthASecondOpinion(gridded.Path.Waypoints) &&
                    !Marching.StopNow())
                {
                    SecondOpinionAsked++;

                    using var _again = PlanningProfile.Measure(PlanningProfile.Step.PoseSearch);

                    HybridPlanning.HybridAStarPlanner.LastStop = null;

                    Plan better = PoseSearched();

                    // Where the lattice stopped, in its own words. Tallied into
                    // fixed slots rather than a dictionary because planning runs
                    // off the main thread and a diagnostic must not be the thing
                    // that introduces a race.
                    SecondOpinionStops[StopSlot(HybridPlanning.HybridAStarPlanner.LastStop)]++;

                    // [M132]. Whether the mover was lapping one of its own when
                    // it asked. The lattice's leaving rule is stricter than
                    // Marching.IsClearLine's - it excuses a body only while the
                    // mover is getting out of it, never for the whole leg - so a
                    // regiment that starts in contact is the case where the two
                    // planners most plainly disagree about what is walkable.
                    if (StartsInsideOwn(battle, unit, unit.Facing)) SecondOpinionStartedLapping++;

                    bool dearer =
                        WayRoundCostCeiling > 0f && ladder.Path.Found && ladder.PressedThrough &&
                        CostsMoreThan(battle, unit, better, ladder, WayRoundCostCeiling);

                    if (better.Path.Found && !better.PressedThrough && !dearer &&
                        WalksCleanly(battle, unit, better))
                    {
                        SecondOpinionTook++;
                        SecondOpinionTookArea += unit.Footprint.Width * unit.Footprint.Depth;
                        SecondOpinionTookWidest =
                            MathF.Max(SecondOpinionTookWidest, unit.Footprint.Width);
                        GridClean++;

                        log?.Record(new BattleLogEntry(
                            LogLevel.Decision, "Path",
                            $"{unit.Def.DisplayName} had a grid route that bent too far, so the " +
                            "pose search was asked for a second opinion and its route was taken " +
                            "instead.",
                            unit.Id));

                        taken = better;
                        return true;
                    }

                    // Split by door, for the same reason the pose stage's are
                    // [M130]: "refused" without a cause is a fact with nothing
                    // to act on, and [M131a] found thirteen of these on four
                    // fields where the rule never once succeeds.
                    if (!better.Path.Found)
                    {
                        SecondOpinionNoRoute++;

                        // Which door the lattice came back through, and how far
                        // it got before it did. "No route" covers an arrangement
                        // with no answer, a budget spent, and a cost limit that
                        // ruled everything out before the first expansion - and
                        // those three want three different fixes.
                        int why = (int)better.Path.Failure;

                        if (why >= 0 && why < SecondOpinionWhy.Length) SecondOpinionWhy[why]++;

                        SecondOpinionNoRouteArea += unit.Footprint.Width * unit.Footprint.Depth;
                        SecondOpinionNoRouteNarrowest =
                            MathF.Min(SecondOpinionNoRouteNarrowest, unit.Footprint.Width);

                        SecondOpinionExpansions += better.Path.CellsExplored;
                        SecondOpinionPrimitives += better.Effort.Legs;
                        SecondOpinionBodies += better.Effort.Bodies;
                    }
                    else if (better.PressedThrough) SecondOpinionPressed++;
                    else if (dearer) SecondOpinionTooDear++;
                    else SecondOpinionDirty++;

                    SecondOpinionRefused++;
                }

                taken = gridded;
                return true;
            }

            // The pose search itself, written once and asked from two places
            // since [M131] - the stage below, and the second opinion inside
            // TookGridRoute above. Extracted rather than copied because the two
            // callers must ask the *same* planner the same way: a second opinion
            // that searched differently from the stage would make the comparison
            // between them meaningless.
            Plan PoseSearched()
            {
                // The tangent route is already computed and already known to be
                // the wrong answer - but it is the right neighbourhood, and
                // bounding the lattice to a tube around it is what makes a
                // pose search affordable on an ordinary order.
                // The grid route first, where there is one. The tube was turned
                // off because the cheap route it was drawn round was a
                // press-through on 74 of 94 orders, so it enclosed a line
                // through the middle of a regiment and there was no answer near
                // it to find. A grid route cannot be a press-through: cells
                // holding a body are not enterable, so going round them is the
                // only thing it can express.
                IReadOnlyList<Vec2>? tube = null;

                if (GridPlanning.GridRoutePlanner.Use == GridPlanning.GridUse.Corridor &&
                    gridRoute != null && gridRoute.Count >= 2)
                {
                    tube = gridRoute;
                }
                else if (CorridorFromCheapRoute)
                {
                    Plan cheap = Tangents();

                    if (cheap.Path.Found && cheap.Path.Waypoints.Count >= 2)
                        tube = cheap.Path.Waypoints;
                }

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
                // answer, for the same reason as above.
                if (limit <= 0f && StraightLineCostCeiling > 0f)
                {
                    float straight = StraightSeconds(battle, unit, destination);
                    if (straight > 1f) limit = straight * StraightLineCostCeiling;
                }

                Plan found;

                if (tube != null)
                {
                    found = HybridAStarRoutePlanner.PlanAlong(
                        battle, unit, destination, arriveOn,
                        tube, CheapCorridorHalfWidthMetres, log, BoundedBudget, limit);
                }
                else if (CorridorHalfWidthMetres > 0f)
                {
                    found = HybridAStarRoutePlanner.PlanAlong(
                        battle, unit, destination, arriveOn,
                        corridor: null, CorridorHalfWidthMetres, log, BoundedBudget, limit);
                }
                else
                {
                    found = HybridAStarRoutePlanner.PlanAlong(
                        battle, unit, destination, arriveOn, corridor: null, 0f, log,
                        expansionBudget: PoseExpansionBudget > 0 ? PoseExpansionBudget : null,
                        secondsLimit: limit);
                }

                bool bounded = tube != null || CorridorHalfWidthMetres > 0f;

                if (bounded &&
                    (!found.Path.Found || found.PressedThrough || !WalksCleanly(battle, unit, found)))
                {
                    PoseWidened++;
                    found = HybridAStarRoutePlanner.PlanAlong(
                        battle, unit, destination, arriveOn, corridor: null, 0f, log,
                        expansionBudget: PoseExpansionBudget > 0 ? PoseExpansionBudget : null,
                        secondsLimit: limit);
                }

                return found;
            }

            if (NoTimeLeft(ref StoppedBeforeGraphs, "the tangent graph")) return ladder;

            // ---- the tangent graph, M86 ------------------------------------
            //
            // Below the grid since M86, and drawn only if this line is reached.
            // From above the grid it answered 0 of 280 bench orders while
            // costing 731 us a call - 23,4 ms of a 128 ms field - sitting
            // directly on top of a grid costing 219 us that answered 23 of
            // them. Tangents name only the legs that could lie on a shortest
            // route, which is a pruning about cost and not about clearance, so
            // a leg it declined to name can still be the one that walks. That
            // is why the grid, which reasons about ground rather than about
            // shortest paths, answers what this refuses - and it is why the
            // order between them was the wrong way round.
            if (AskTangentStage)
            {
                Plan tangent = Tangents();

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
            }

            // The two richer graphs cost a fraction of a millisecond each and
            // stand between an order and a lattice search costing tens.
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
            if (NoTimeLeft(ref StoppedBeforePose, "the pose search")) return ladder;

            if (PoseSearchBeforePressing && GridPlanning.GridRoutePlanner.Use != GridPlanning.GridUse.Replace)
            {
                using var _pose = PlanningProfile.Measure(PlanningProfile.Step.PoseSearch);

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
                Plan posed = PoseSearched();

                // [M130]. The stage is asked six times in three hundred and
                // sixty orders and wins none of them, and until now the four
                // ways it can lose were indistinguishable from outside: no
                // route, a route that presses, a route that does not walk, and
                // a route refused by the ceiling. Only the last was counted, so
                // "the pose search never wins" was a fact with no cause
                // attached to it.
                if (!posed.Path.Found) PoseNoRoute++;
                else if (posed.PressedThrough) PosePressed++;
                else if (!WalksCleanly(battle, unit, posed)) PoseDirty++;

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
            Plan lastResort = Tangents();

            if (lastResort.Path.Found)
            {
                if (StraightLineCostCeiling > 0f)
                {
                    float straight = StraightSeconds(battle, unit, destination);
                    float around = Marching.SecondsToWalk(
                        battle, unit, lastResort.Path.Waypoints, lastResort.Hold);

                    if (straight > 1f && around > straight * StraightLineCostCeiling)
                        TangentTooDear++;
                }

                return lastResort;
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
        /// <remarks>
        /// Timed since [M123]. It is asked up to three times an order, over
        /// every leg of the answer, and it is one of the two places the budget
        /// deliberately never interrupts - so it is a candidate for a slow order
        /// that no clock could see.
        /// </remarks>
        internal static bool WalksCleanly(BattleState battle, UnitInstance unit, Plan plan)
        {
            using var _profile = PlanningProfile.Measure(PlanningProfile.Step.WalkCheck);

            return FirstBadLeg(battle, unit, plan) == 0;
        }

        /// <summary>Whether a leg that would not wheel was walked held instead.</summary>
        internal static bool AllowSidewalk = true;

        /// <summary>Legs asked to sidewalk, and legs that then walked.</summary>
        internal static int SidewalkAsked, SidewalkTook;

        /// <summary>Routes where smoothing produced something that would not walk - M94.</summary>
        internal static int SmoothingRefused;

        /// <summary>
        /// Whether a smoothed route that will not walk is thrown away in favour
        /// of the unsmoothed one. <b>Off</b> - see M94.
        /// </summary>
        internal static bool RefuseSmoothingThatBreaks;

        /// <summary>
        /// The same route with a front held on any leg that will not walk on the
        /// line of march - <b>M91</b>.
        /// </summary>
        /// <remarks>
        /// Written as a repair against <see cref="FirstBadLeg"/> rather than as
        /// a second opinion about clearance, so that what decides a sidewalk is
        /// the same code that decides whether the executor will walk it. Asked
        /// only of legs the gate has already refused, which is what makes this
        /// an ordering and not a comparison: a regiment that can face where it
        /// is going does.
        /// </remarks>
        internal static Plan Sidewalked(BattleState battle, UnitInstance unit, Plan plan)
        {
            if (!AllowSidewalk || !plan.Path.Found) return plan;

            IReadOnlyList<Vec2> points = plan.Path.Waypoints;
            if (points.Count < 2) return plan;

            Facing?[]? hold = plan.Hold;
            Plan working = plan;

            // One repair a leg at most, and the legs only go forwards, so this
            // cannot turn round on itself however the gate answers.
            for (int attempt = 1; attempt < points.Count; attempt++)
            {
                int bad = FirstBadLeg(battle, unit, working);

                if (bad <= 0) break;
                if (hold != null && bad < hold.Length && hold[bad].HasValue) break;

                SidewalkAsked++;

                hold = hold == null ? new Facing?[points.Count] : (Facing?[])hold.Clone();

                Facing inHand = FrontInHandBefore(unit, points, hold, bad);

                // M94. On the last leg the front is not only about walking the
                // leg, it decides whether the regiment can stand where it is
                // sent at all - and at a destination touching one of its own,
                // very few fronts serve. The line of march is almost never one
                // of them, so they are tried, nearest turn first.
                Facing? arriving = bad == points.Count - 1
                    ? FrontToArriveOn(battle, unit, points[bad], inHand)
                    : null;

                hold[bad] = arriving ?? inHand;

                working = new Plan(working.Path, hold, working.PressedThrough, working.Effort);
            }

            if (ReferenceEquals(working.Hold, plan.Hold)) return plan;

            if (FirstBadLeg(battle, unit, working) == 0) SidewalkTook++;

            return working;
        }

        /// <summary>How finely the fronts a destination will accept are looked for.</summary>
        internal static float ArrivalFrontStepDegrees = 15f;

        /// <summary>
        /// The front to walk the last leg on: whichever the destination will
        /// accept that costs the least turning from the one already in hand.
        /// </summary>
        /// <remarks>
        /// <b>M94.</b> Measured over twenty-four fronts at the failing approach,
        /// a destination touching one of its own accepted <b>two</b> of them -
        /// and <c>Marching.AlongTheLine</c> hands the last leg whichever its
        /// direction implies, which is one of the other twenty-two nearly every
        /// time. Nothing anywhere chose between them.
        /// </remarks>
        private static Facing? FrontToArriveOn(
            BattleState battle, UnitInstance unit, Vec2 goal, Facing inHand)
        {
            if (CouldStandAt(battle, unit, goal, inHand)) return inHand;

            float step = MathF.Max(1f, ArrivalFrontStepDegrees);

            Facing? best = null;
            float least = float.MaxValue;

            for (float turn = 0f; turn < 360f; turn += step)
            {
                var front = Facing.FromDegrees(inHand.Degrees + turn);

                if (!CouldStandAt(battle, unit, goal, front)) continue;

                float cost = Facing.AbsoluteDelta(inHand, front);

                if (cost >= least) continue;

                least = cost;
                best = front;
            }

            return best;
        }

        /// <summary>The front the regiment is on as it begins <paramref name="leg"/>.</summary>
        private static Facing FrontInHandBefore(
            UnitInstance unit, IReadOnlyList<Vec2> points, Facing?[]? hold, int leg)
        {
            if (leg <= 1) return unit.Facing;

            if (hold != null && leg - 1 < hold.Length && hold[leg - 1].HasValue)
                return hold[leg - 1]!.Value;

            return Marching.AlongTheLine(points[leg - 2], points[leg - 1], unit.Facing);
        }

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

                // M94. The mirror of the above, and only on the last leg: the
                // destination is the order and cannot be moved, while every
                // waypoint before it is the planner's own and it can choose
                // again. Both may apply on a two-point route, and then both
                // must pass.
                // Only where the arrival pose is one the regiment could stand
                // in. Contact is licensed; ending inside a body is not, and
                // without this the backwards sweep would happily allow it,
                // because it takes the arrival overlap as its baseline.
                bool endsInsideOwn =
                    _arrivalLicensed && leg == points.Count - 1 &&
                    InsideOwnAt(battle, unit, points[leg], front) &&
                    CouldStandAt(battle, unit, points[leg], front);

                if (startsInsideOwn || endsInsideOwn)
                {
                    if (startsInsideOwn &&
                        !EscapesWithoutDeepening(battle, unit, points[leg - 1], points[leg], front))
                        return leg;

                    if (endsInsideOwn &&
                        !ArrivesWithoutDeepening(battle, unit, points[leg - 1], points[leg], front))
                        return leg;
                }
                else if (!Marching.IsClearLine(battle, unit, points[leg - 1], points[leg], front))
                {
                    return leg;
                }
            }

            return 0;
        }

        /// <summary>
        /// Whether the staging scan walks outward once instead of restarting at
        /// every stand-off it tries.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>[M125], and the stage it is about is [M123]'s.</b>
        /// <see cref="TryStageForDirectRun"/> tries stand-offs outward in
        /// two-metre steps to twice the mover's bounding radius, and asks
        /// <see cref="EscapesWithoutDeepening"/> about each - which re-walks the
        /// whole leg <i>from where the regiment stands</i> every time. The
        /// samples are a triangular sum: 1 + 2 + ... + n. For an 80x40 spearman
        /// n is 45 and for 229x114 cavalry it is 128, so a cavalry order walks
        /// 8 256 samples a push direction against a spearman's 1 035, each one a
        /// <c>FormationFits</c> and an overlap fraction against every friendly
        /// regiment on the field. Measured in play, that stage is 76 to 103 ms of
        /// a single order and it runs before the cascade's first gate, so the
        /// search budget cannot touch it.
        /// </para>
        /// <para>
        /// Every stand-off on one push lies on the same ray and is walked on the
        /// same front, so the leg to the second is the leg to the first with more
        /// on the end. One outward walk carrying the overlaps forward answers all
        /// of them: about 130 samples where there are 8 256.
        /// </para>
        /// <para>
        /// <b>It is not free, and the reason is the sample grid.</b> The scan
        /// today spaces a leg's samples by dividing <i>that leg</i> into
        /// two-metre pieces, so a stand-off at 7,3 m and one at 9,3 m sample
        /// different ground. A single walk has one grid for all of them. It also
        /// makes the refusal monotone, which the continuous rule already is - a
        /// walk that may not deepen an overlap cannot be refused at nine metres
        /// and allowed at eleven - so a stand-off that escapes today after a
        /// nearer one failed is a sampling artefact rather than an answer.
        /// </para>
        /// </remarks>
        internal static bool WalkTheStagingOnce = true;

        /// <summary>Poses the staging scan tested. Measurement only.</summary>
        [ThreadStatic] internal static long StagingSamples;

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

            if (WalkTheStagingOnce)
                return StagesWalkingOutwardsOnce(battle, unit, destination, pushes, out plan);

            foreach (Vec2 push in pushes)
            {
                if (push.IsNearZero) continue;

                Vec2 direction = push.Normalised();
                float least = MathF.Max(EgressSpacingMetres, push.Length + EgressSpacingMetres);
                float furthest = unit.Footprint.BoundingRadius * 2f + least;

                for (float distance = least; distance <= furthest; distance += EgressSpacingMetres)
                {
                    // Not the budget - only whether anybody still wants this.
                    // See Marching.Abandoned, and [M123] for why this stage in
                    // particular: it is the dearest thing an order does and no
                    // gate stands in front of it.
                    if (Marching.Abandoned()) return false;

                    Vec2 stage = unit.Position + direction * distance;
                    Facing escapeFront = Facing.Towards(unit.Position, stage);
                    Facing runFront = Facing.Towards(stage, destination);

                    if (!EscapesWithoutDeepening(
                            battle, unit, unit.Position, stage, escapeFront, staging: true))
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

        /// <summary>
        /// The same stand-offs, tried in the same order, over one walk out
        /// instead of one walk for each. See <see cref="WalkTheStagingOnce"/>.
        /// </summary>
        private static bool StagesWalkingOutwardsOnce(
            BattleState battle, UnitInstance unit, Vec2 destination, List<Vec2> pushes, out Plan plan)
        {
            plan = default;

            var own = new List<UnitInstance>();

            foreach (UnitInstance other in battle.UnitsOnField())
            {
                if (other.Id == unit.Id || other.Owner != unit.Owner) continue;
                own.Add(other);
            }

            var lapping = new float[own.Count];

            foreach (Vec2 push in pushes)
            {
                if (push.IsNearZero) continue;

                Vec2 direction = push.Normalised();
                float least = MathF.Max(EgressSpacingMetres, push.Length + EgressSpacingMetres);
                float furthest = unit.Footprint.BoundingRadius * 2f + least;

                Vec2 first = unit.Position + direction;
                Facing escapeFront = Facing.Towards(unit.Position, first);

                // The carry starts where the regiment is standing, exactly as
                // each separate walk starts it today.
                var standing = new OrientedRect(unit.Position, escapeFront, unit.Footprint);

                for (int i = 0; i < own.Count; i++)
                    lapping[i] = OrientedRect.OverlapFraction(standing, own[i].Shape);

                bool clear = true;

                // The ground short of the first stand-off, which every stand-off
                // on this push has to cross and which none of them is.
                for (float before = EgressSpacingMetres; before < least - 0.001f;
                     before += EgressSpacingMetres)
                {
                    if (Marching.Abandoned()) return false;

                    if (!StillEscaping(battle, unit, unit.Position + direction * before,
                                       escapeFront, own, lapping))
                    {
                        clear = false;
                        break;
                    }
                }

                if (!clear) continue;

                for (float distance = least; distance <= furthest; distance += EgressSpacingMetres)
                {
                    if (Marching.Abandoned()) return false;

                    Vec2 stage = unit.Position + direction * distance;

                    // Refusal is monotone along the ray, so the first stand-off
                    // that cannot be reached ends this push rather than skipping
                    // one candidate.
                    if (!StillEscaping(battle, unit, stage, escapeFront, own, lapping)) break;

                    Facing runFront = Facing.Towards(stage, destination);

                    if (!Marching.IsClearLine(battle, unit, stage, destination, runFront))
                        continue;

                    float walked = Vec2.Distance(unit.Position, stage) + Vec2.Distance(stage, destination);
                    PathResult path = PathResult.Success(
                        new[] { unit.Position, stage, destination }, Array.Empty<Coord>(),
                        walked, walked, 0);

                    plan = new Plan(path, new Facing?[] { null, escapeFront, runFront }, false);
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// One pose along the walk out: whether it stands, and whether it has
        /// entered or deepened anything since the last one.
        /// </summary>
        /// <remarks>
        /// <paramref name="lapping"/> is carried and written through, which is
        /// the whole saving: it is what the repeated walk recomputes from the
        /// beginning every time.
        /// </remarks>
        private static bool StillEscaping(
            BattleState battle, UnitInstance unit, Vec2 at, Facing front,
            List<UnitInstance> own, float[] lapping)
        {
            StagingSamples++;

            if (!battle.FormationFits(unit, at, front)) return false;

            var pose = new OrientedRect(at, front, unit.Footprint);

            for (int i = 0; i < own.Count; i++)
            {
                float now = OrientedRect.OverlapFraction(pose, own[i].Shape);
                float before = lapping[i];

                if (before > AllowedContactFraction)
                {
                    if (now > before + SeparationTolerance) return false;
                }
                else if (now > AllowedContactFraction)
                {
                    return false;
                }

                lapping[i] = now;
            }

            return true;
        }

        /// <summary>
        /// Whether a regiment merely <b>touching</b> one of its own is granted
        /// the first leg's leaving licence, as one overlapping it already is.
        /// </summary>
        /// <remarks>
        /// <b>M92, and it is the third cause in open finding 24.</b>
        /// <c>RouteSmoothing</c> asks the first leg with <c>leaving: true</c>
        /// unconditionally; this gate grants that only where the regiment laps
        /// one of its own by more than <c>AllowedContactFraction</c>. A regiment
        /// shoulder to shoulder - which is what [M2] exists to permit, and what
        /// a regiment under orders very often is - laps it by <b>less</b> than
        /// that, so it got a route smoothed under one rule and refused under a
        /// stricter one. Measured over the nineteen approach angles, the six
        /// that fail lap body 0 by 0,0% to 4,4%, all under the 5% allowance, and
        /// every one of them has a first leg that is clear with the licence and
        /// blocked without it.
        /// <para>
        /// Extending the licence does not wave the leg through:
        /// <see cref="EscapesWithoutDeepening"/> still refuses a leg that enters
        /// a body it was clear of or deepens one it was lapping. It only stops
        /// contact itself being the refusal.
        /// </para>
        /// </remarks>
        internal static bool LicenceOnContact = true;

        /// <summary>
        /// Whether the <b>last</b> leg of a route may finish in contact with one
        /// of its own, on the same terms as the first may begin in it.
        /// </summary>
        /// <remarks>
        /// <b>M94, and it is the fourth cause in open finding 24.</b> There was
        /// a licence to leave contact and none to arrive in it, and the
        /// measurement of that is unusually clean: over twenty-four fronts at
        /// the failing approach, the regiment can <b>stand</b> at its
        /// destination on 2, the final leg walks on <b>0</b> - and the
        /// <b>same leg walked backwards</b> clears on 7. Same rectangle, same
        /// front, same bodies, same endpoints. The only thing direction changes
        /// is which end the leaving licence lands on, and the contact is at the
        /// end.
        /// <para>
        /// So a regiment that may stand touching one of its own, and may leave
        /// contact, but may never enter it, is [M2] and [M89] contradicting each
        /// other - and this is which way the contradiction is resolved. The last
        /// leg only, because the destination is the order and cannot be moved,
        /// while every waypoint before it is the planner's own choice and it can
        /// choose again.
        /// </para>
        /// </remarks>
        /// <remarks>
        /// <b>Shipped off, and M94b is why.</b> Built, corrected and made safe
        /// - and then measured, and it earns nothing anywhere it has been
        /// asked. Over the nineteen approach angles it changes not one route:
        /// at 0°, 5°, 20° and 25° the licensed pass finds nothing <i>at any
        /// price</i>, and at 15° and 30° it finds a way round costing 3,51x and
        /// 4,09x the press, which is over the ceiling. On the bench it fires
        /// twice in four fields, keeps neither, and costs sidewaysmile between
        /// a fifth and a half again for it. So it stays here, off, as a lever
        /// with its price written down rather than as a default nobody
        /// measured.
        /// </remarks>
        internal static bool LicenceOnArrival;

        /// <summary>
        /// Whether the pass running on this thread right now is the licensed
        /// one.
        /// </summary>
        /// <remarks>
        /// Per thread rather than global because orders are planned several at
        /// once, and a flag one order sets while it retries would otherwise
        /// decide what a different order on a different thread is allowed to
        /// do. That is the same fault <c>UnitIndex.Marks</c> was built to avoid
        /// and it produced routes that differed depending on how many orders
        /// were given together.
        /// </remarks>
        [ThreadStatic] private static bool _arrivalLicensed;

        /// <summary>Orders that ran the licensed second pass, and that kept it.</summary>
        internal static int ArrivalAsked, ArrivalTook;

        private static bool StartsInsideOwn(BattleState battle, UnitInstance unit, Facing front) =>
            InsideOwnAt(battle, unit, unit.Position, front);

        /// <summary>
        /// Whether the regiment, stood at <paramref name="place"/> on
        /// <paramref name="front"/>, is lapping or touching one of its own.
        /// </summary>
        private static bool InsideOwnAt(
            BattleState battle, UnitInstance unit, Vec2 place, Facing front)
        {
            var at = new OrientedRect(place, front, unit.Footprint);

            foreach (UnitInstance other in battle.UnitsOnField())
            {
                if (other.Id == unit.Id || other.Owner != unit.Owner) continue;
                if (OrientedRect.OverlapFraction(at, other.Shape) > AllowedContactFraction)
                    return true;

                if (LicenceOnContact && OrientedRect.GapBetween(at, other.Shape) <= 0f)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Whether the regiment could legally stand at <paramref name="place"/>
        /// on <paramref name="front"/> - touching one of its own is allowed,
        /// overlapping it is not.
        /// </summary>
        internal static bool CouldStandAt(
            BattleState battle, UnitInstance unit, Vec2 place, Facing front)
        {
            var pose = new OrientedRect(place, front, unit.Footprint);

            foreach (UnitInstance other in battle.UnitsOnField())
            {
                if (other.Id == unit.Id || other.Owner != unit.Owner) continue;
                if (OrientedRect.OverlapFraction(pose, other.Shape) > AllowedContactFraction)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Whether the last leg may finish in the contact it finishes in -
        /// <b>M94</b>, and it is <see cref="EscapesWithoutDeepening"/> walked
        /// backwards, which is exactly what it means.
        /// </summary>
        /// <remarks>
        /// Bodies the regiment is lapping <b>where it arrives</b> may only be
        /// less lapped the further back along the leg you look, and bodies it is
        /// clear of at the destination may not be entered anywhere on the way
        /// in. So it may finish touching what it is ordered to finish touching,
        /// and may not barge through anything else to get there.
        /// </remarks>
        private static bool ArrivesWithoutDeepening(
            BattleState battle, UnitInstance unit, Vec2 from, Vec2 to, Facing front) =>
            EscapesWithoutDeepening(battle, unit, to, from, front, ArrivalContactFraction);

        /// <summary>
        /// What the <b>arriving</b> licence forgives in a body the regiment is
        /// clear of where it stops: nothing at all.
        /// </summary>
        /// <remarks>
        /// <b>The tolerance is the whole of M94a.</b> M94 built the arriving
        /// licence as <see cref="EscapesWithoutDeepening"/> walked backwards and
        /// inherited its allowance with it, and that allowance is
        /// <c>AllowedContactFraction</c> - five per cent of a body. But a leg
        /// that is not granted a licence is judged by
        /// <c>Marching.IsClearLine</c>, and that refuses on
        /// <c>Sweep.Touches</c>, which is any overlap whatever. So granting the
        /// licence moved the last leg from the stricter test to the looser one,
        /// and the looser one let it barge five per cent into somebody on the
        /// way in. Measured: turned on with the inherited allowance it took
        /// three of seven sampled angles to <i>walks through somebody</i>, and
        /// one angle from clear to a 68 s route that clipped.
        /// <para>
        /// The two ends are not symmetrical, and this is why. Where the
        /// regiment <b>starts</b>, the contact is ground it already occupies and
        /// nobody chose; where it <b>stops</b>, the contact is a place the
        /// planner picked. So the leaving licence keeps its allowance and the
        /// arriving one gets none: a body lapped at the destination may only be
        /// less lapped the further back you look, and a body clear at the
        /// destination may not be entered anywhere on the way in. That is
        /// exactly what <c>IsClearLine</c> asks. All the licence now forgives is
        /// the contact <b>at the destination itself</b>, which is the one thing
        /// it was built to forgive.
        /// </para>
        /// </remarks>
        private const float ArrivalContactFraction = 0f;

        /// <summary>
        /// Sweeps a first leg in small steps.  Bodies already lapped may only
        /// become less overlapped; bodies initially clear may never be entered.
        /// </summary>
        /// <param name="staging">
        /// Whether this walk is the staging scan's rather than the walk check's.
        /// Measurement only, and it exists because the two share this helper:
        /// counting both under <see cref="StagingSamples"/> charged the gate that
        /// verifies a route to the stage that clears the ground before one, and
        /// an abandoned order read as walking three hundred poses it never
        /// walked.
        /// </param>
        private static bool EscapesWithoutDeepening(
            BattleState battle, UnitInstance unit, Vec2 from, Vec2 to, Facing front,
            float? allowedContact = null, bool staging = false)
        {
            float allowed = allowedContact ?? AllowedContactFraction;

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
                if (staging) StagingSamples++;

                Vec2 at = Vec2.Lerp(from, to, (float)i / samples);
                if (!battle.FormationFits(unit, at, front)) return false;

                var pose = new OrientedRect(at, front, unit.Footprint);

                for (int other = 0; other < own.Count; other++)
                {
                    float overlap = OrientedRect.OverlapFraction(pose, own[other].Shape);
                    float before = previousOverlap[other];

                    if (before > allowed)
                    {
                        if (overlap > before + SeparationTolerance) return false;
                    }
                    else if (overlap > allowed)
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
