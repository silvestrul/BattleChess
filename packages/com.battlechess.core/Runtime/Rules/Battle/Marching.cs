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
    /// <summary>
    /// A route, and everything the planner decided about how to walk it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The interesting parts of a plan — which legs are crabbed, whether it gave
    /// up on keeping clear — are not properties of a line across ground, so they
    /// cannot live in <see cref="PathResult"/>, which is a Contracts type and
    /// describes exactly that. They travel here instead.
    /// </para>
    /// <para>
    /// They were static properties on the planner for two passes, read on the
    /// next line by whoever had just called it. That is a seam, it was flagged
    /// as one when it was written, and it duly failed: tests run in parallel,
    /// each plan overwrote the last, and a route came back described by
    /// somebody else's decisions. It cost a real test failure that looked like a
    /// pathfinding bug. Shared mutable state does not survive being convenient.
    /// </para>
    /// </remarks>
    /// <summary>
    /// What working out a route actually cost, in the units the work is done in.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Carried on the plan rather than counted in static fields, and that is not
    /// a stylistic preference — see <see cref="Plan"/> for the time decisions
    /// about a route <i>were</i> static and a plan came back described by
    /// somebody else's numbers. Effort is a property of one plan and travels
    /// with it.
    /// </para>
    /// <para>
    /// All zero means the straight line answered and no graph was ever built,
    /// which is <b>M10</b> working and is the common case: measured on a whole
    /// army ordered at once, seven regiments in thirteen never reach the search
    /// at all.
    /// </para>
    /// </remarks>
    public readonly struct RouteEffort
    {
        public RouteEffort(
            int places, int legs, int expansions, int rounds = 0,
            int states = 0, long frontierScans = 0, int cacheHits = 0, int pruned = 0,
            int lineChecks = 0, int standChecks = 0, int turnChecks = 0, bool askedTheLadder = false,
            int bodies = 0, int filteredPlaces = 0)
        {
            AskedTheLadder = askedTheLadder;
            Bodies = bodies;
            FilteredPlaces = filteredPlaces;
            Places = places;
            Legs = legs;
            Expansions = expansions;
            Rounds = rounds;
            States = states;
            FrontierScans = frontierScans;
            CacheHits = cacheHits;
            Pruned = pruned;
            LineChecks = lineChecks;
            StandChecks = standChecks;
            TurnChecks = turnChecks;
        }

        /// <summary>Candidate places the route was allowed to bend at.</summary>
        public readonly int Places;

        /// <summary>
        /// Legs actually priced. Each one costs a swept rectangle along the leg
        /// and a standing test at either end, so this is the number to multiply
        /// when asking what the geometry came to.
        /// </summary>
        public readonly int Legs;

        /// <summary>States taken off the frontier and expanded.</summary>
        public readonly int Expansions;

        /// <summary>
        /// How many times the search gave up, bought more ground to bend at
        /// from whatever had refused it, and started again (<b>M32</b>).
        /// </summary>
        /// <remarks>
        /// One means the first handful of places was enough. A high count on an
        /// ordinary march means the march kept meeting bodies it had not been
        /// told about, which is either a genuinely layered field or a generator
        /// handing out places that do not help.
        /// </remarks>
        public readonly int Rounds;

        /// <summary>
        /// States of (place, front) created. Always at least as many as
        /// <see cref="Places"/>, usually several times more, and the number the
        /// frontier's cost is really measured against.
        /// </summary>
        public readonly int States;

        /// <summary>
        /// States looked at while choosing which to expand next.
        /// </summary>
        /// <remarks>
        /// The frontier is a walk down the whole list, so this grows as the
        /// square of the graph. A <c>long</c> because it is the one counter here
        /// that can reach millions on a single march, which is itself the
        /// finding: it is bookkeeping, not geometry, and nothing about a route
        /// depends on it.
        /// </remarks>
        public readonly long FrontierScans;

        /// <summary>Legs wanted whose answer was already known.</summary>
        public readonly int CacheHits;

        /// <summary>Edges turned back by the bound before any geometry.</summary>
        public readonly int Pruned;

        /// <summary>Swept-rectangle line checks actually run.</summary>
        public readonly int LineChecks;

        /// <summary>Standing checks actually run.</summary>
        public readonly int StandChecks;

        /// <summary>Room-to-turn checks actually run.</summary>
        public readonly int TurnChecks;

        /// <summary>
        /// Whether the ladder was asked for a second opinion (<b>M33</b>).
        /// </summary>
        /// <remarks>
        /// Recorded because the clock and the counters stopped agreeing: a plan
        /// with two places and three legs priced — no search worth the name —
        /// was measured at 13,3 ms in play, while plans plainly larger came in
        /// under four. Legs cannot explain that, and a flag can.
        /// </remarks>
        public readonly bool AskedTheLadder;

        /// <summary>
        /// How many regiments were pulled into the corridor and asked for
        /// candidate places.
        /// </summary>
        /// <remarks>
        /// The number worth arguing about when a plan looks expensive for what
        /// is actually on the field — the graph is built from these, and their
        /// count times up to twenty-four points each is most of the answer to
        /// "why does this cost so much".
        /// </remarks>
        public readonly int Bodies;

        /// <summary>
        /// Of the graph's places, how many actually have their legs pruned by
        /// tangency. The rest are unfiltered by design (<b>M36</b>).
        /// </summary>
        public readonly int FilteredPlaces;

        /// <summary>The same effort, with the ladder recorded as asked.</summary>
        public RouteEffort WithLadder() =>
            new RouteEffort(
                Places, Legs, Expansions, Rounds, States, FrontierScans, CacheHits, Pruned,
                LineChecks, StandChecks, TurnChecks, askedTheLadder: true,
                bodies: Bodies, filteredPlaces: FilteredPlaces);

        /// <summary>Every dear geometric question this plan asked.</summary>
        public int Geometry => LineChecks + StandChecks + TurnChecks;

        /// <summary>Whether a graph of places was built at all.</summary>
        /// <remarks>
        /// <b>Only the graph.</b> The stages below it - the ladder, the ways
        /// round, both grids, the pose search - fill none of these counters, so
        /// this is false for an order that spent three hundred milliseconds
        /// searching hard. It used to print "straight line, nothing searched",
        /// which is how a played session came to report exactly that about all
        /// 640 of its orders including a 381,7 ms one. See [M123], and the line
        /// that now says where the time went.
        /// </remarks>
        public bool Searched => Places > 0;

        /// <summary>
        /// Everything counted, for a bench rather than a game log.
        /// </summary>
        public string Detail =>
            Searched
                ? $"{Places,3} places  {States,5} states  {Rounds} rounds | " +
                  $"geometry {Geometry,6} ({LineChecks} line, {StandChecks} stand, {TurnChecks} turn) | " +
                  $"legs {Legs,5} priced, {CacheHits,7} cached, {Pruned,7} pruned | " +
                  $"frontier {FrontierScans,9}"
                : "no graph of places built";

        public override string ToString() =>
            Searched
                ? $"{Places} places ({Bodies} bodies, {FilteredPlaces} corners filtered), " +
                  $"{Legs} legs priced, {Expansions} expanded, " +
                  $"{Rounds} round{(Rounds == 1 ? string.Empty : "s")}" +
                  (AskedTheLadder ? ", asked the ladder too" : string.Empty)
                : "no graph of places built";
    }

    public readonly struct Plan
    {
        public Plan(PathResult path, Facing?[]? hold, bool pressedThrough, RouteEffort effort = default)
        {
            Path = path;
            Hold = hold;
            PressedThrough = pressedThrough;
            Effort = effort;
        }

        /// <summary>The line itself.</summary>
        public readonly PathResult Path;

        /// <summary>The front to hold on each leg, where a leg asks for one.</summary>
        public readonly Facing?[]? Hold;

        /// <summary>Whether this plan gave up on keeping clear of its own side.</summary>
        public readonly bool PressedThrough;

        /// <summary>What working this route out cost.</summary>
        public readonly RouteEffort Effort;

        public bool Found => Path.Found;

        /// <summary>Turns the plan into the route a regiment actually walks.</summary>
        public MovementRoute ToRoute(bool wheelFirst = false) =>
            new MovementRoute(Path.Waypoints, wheelFirst, Hold) { PressingThrough = PressedThrough };
    }

    public static class Marching
    {
        /// <summary>
        /// Asked periodically while a route is being worked out: has whoever
        /// wanted it stopped wanting it?
        /// </summary>
        /// <remarks>
        /// M80. A player who clicks somewhere else has superseded the order a
        /// search is answering, and finishing it spends a frame on a route
        /// that is thrown away on arrival - which [M79] made as dear as 179 ms
        /// for one regiment. Per-thread, because a wing is planned across
        /// several and each answers for its own regiment. Null means nobody is
        /// asking, which is the ordinary case and costs one null check every
        /// sixty-four expansions of the lattice.
        /// </remarks>
        [ThreadStatic] public static Func<bool>? GiveUpNow;

        /// <summary>
        /// How much wider than the regiment a passage may be and still count as
        /// a passage worth threading. Zero is no ceiling.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>M27 says a gap has an axis of its own, and it is right. What it
        /// never said is what makes two bodies a gap at all.</b> The pairing had
        /// no upper bound: any two regiments with room between them were paired,
        /// so a body on one flank and a body on the other formed a "gap" eight
        /// hundred metres wide, generated a mouth, and were clearance-tested
        /// like any squeeze.
        /// </para>
        /// <para>
        /// Measured across the five bench fields before this existed: 34 bodies
        /// near a march, 587 pairs a call, <b>95% of them passing the width
        /// test</b>, 75 318 passages clearance-tested, and 32 routes found -
        /// two thousand three hundred candidates per answer. On the Crucible
        /// and Great Field it was 66 calls, 27 872 passages and <b>no route at
        /// all</b>, and because nothing succeeded there was never an early exit
        /// either: every mouth was tried, every time.
        /// </para>
        /// <para>
        /// A ceiling states the thing the rule always meant. A passage is a
        /// squeeze; open ground is not a passage, and the straight line and the
        /// arch above already answer it.
        /// </para>
        /// </remarks>
        /// <remarks>
        /// <b>Two, and the sweep says the march does not notice.</b> Swept from
        /// no ceiling down to one and a half across five fields: passages tried
        /// fall from 25 582 to 686 on the Crucible, planning falls 8% to 33% a
        /// field, and <b>every field routes exactly as many orders, refuses
        /// exactly as many, and presses through exactly as many</b>. The march
        /// clock - the seconds the executor is actually handed, which is the
        /// only number that decides whether a cheaper planner made anything
        /// better (W10) - is unchanged on four fields and <i>improves</i> 0,49%
        /// on the fifth.
        /// <para>
        /// So the wide pairs were not merely dear to test. Where they won they
        /// were winning with routes no better than the rung below would have
        /// drawn, and on Long March slightly worse. Threading answers 28 times
        /// there without a ceiling and 24 with one, and those four orders now
        /// walk a shorter march.
        /// </para>
        /// <para>
        /// Two rather than one and a half because cost has flattened by then -
        /// the last half is inside the noise - and a ceiling has to hold for
        /// arrangements no bench contains. It is one word if a play-test ever
        /// shows a real squeeze being missed.
        /// </para>
        /// </remarks>
        internal static float GapWidthCeiling = 2f;

        /// <summary>
        /// How many spaces either side of the blocking body to read off before
        /// giving up. Zero is no limit.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>The remarks on <c>ThreadAGap</c> have always described this and
        /// the code has never done it:</b> "the bodies are projected onto the
        /// axis across the march, sorted, and the spaces between them read
        /// off". Sorted and read off is a sweep - a formed line of ten bodies
        /// has nine spaces in it. What was written instead paired <i>every</i>
        /// body with every other, which on the same ten is forty-five, and most
        /// of them name no space at all because the two bodies have three
        /// others standing between them.
        /// </para>
        /// <para>
        /// The designer, on where to start and when to stop: <i>"if 8 is
        /// between 7 and 9 then verify 1-8 and 8-9 then 6-7 9-10 so on ... and
        /// after a number you just stop"</i>. So the walk begins at the body
        /// that actually blocks the line and works outward in both directions,
        /// which orders the spaces by how likely they are to be the answer -
        /// and then it stops, which is what turns a quadratic into a constant.
        /// </para>
        /// </remarks>
        internal static int GapSpacesEitherSide = 4;

        /// <summary>
        /// How long one plan may search before it settles for what it can
        /// already prove.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>A ceiling on the tail, and the tail is the whole complaint.</b>
        /// Measured in a played session: the median order was 1,5 ms and the
        /// worst 287, and within a single wing of thirteen planned together the
        /// spread was 2 868 to one. What is felt is not the median. It is the
        /// one regiment in thirteen whose arrangement is pathological, and the
        /// clock is held for all of them while it thinks.
        /// </para>
        /// <para>
        /// <b>Running out of time is not a new outcome.</b> It stops the
        /// frontier exactly where emptying it would have, so the cascade
        /// carries on into the stage it would have reached anyway and, in the
        /// last resort, declares a press-through - which <b>M98</b> already
        /// says is a legitimate answer, the defect being an <i>undeclared</i>
        /// one. Nothing here can produce a route nobody agreed to; it can only
        /// produce a worse one, more loudly.
        /// </para>
        /// <para>
        /// Zero switches it off entirely, which is what the benches and the
        /// whole test suite want: a budget makes an answer depend on how fast
        /// the machine was, and a bench that cannot reproduce its own numbers
        /// is not a bench. It is set by the host that draws frames, the same
        /// way <see cref="PlanningBudget"/> is.
        /// </para>
        /// </remarks>
        public static float SearchBudgetMs;

        /// <summary>
        /// Whether the budget is also polled <i>inside</i> the searches, and not
        /// only between the stages of the cascade.
        /// </summary>
        /// <remarks>
        /// <para>
        /// [M114] made the cap bind on the cascade by gating each stage before
        /// it starts, which bounds an order at about twice the cap: a stage that
        /// has begun runs to its end. [M114a] then found the residue is not
        /// spread evenly - it is the ladder, and within the ladder the corner
        /// walk, which is a Dijkstra over two dozen nodes asking a swept
        /// clearance question at every relaxation.
        /// </para>
        /// <para>
        /// So the two places that can overrun are polled from within. Both were
        /// chosen because giving up in them abandons nothing: the corner walk's
        /// predecessor chain holds only legs already proved clear, and
        /// straightening keeps every point it has not reached. <b>Nothing that
        /// caches, stamps or half-raises a field is ever polled</b> - that is
        /// the M104 bug class, and a field left half raised is stamped current
        /// and wrong.
        /// </para>
        /// <para>
        /// <b>And at five milliseconds it buys nothing, because the cap already
        /// binds without it.</b> [M122] measured the worst order at 5,0 ms with
        /// the polls off and 5,0 ms with them on, and both give-up counters at
        /// zero: no order on either crowded field ever reaches the corner walk
        /// or the straightening pass already out of time. They are live - at a
        /// tenth of a millisecond the corner walk gives up 137 times a field -
        /// so a zero here is a real zero and not a dead gate (W9). Kept on
        /// because Mono is where the cap bites, and Mono at five behaves like
        /// this bench at one or two, where they do fire.
        /// </para>
        /// </remarks>
        public static bool StopSearchingWhenOutOfTime = true;

        /// <summary>
        /// When the plan on this thread runs out of time, or zero for never.
        /// </summary>
        /// <remarks>
        /// Per-thread, because a wing is planned across several and each is
        /// answering for its own regiment against its own clock. A shared
        /// deadline would give the wing one budget between them, so the
        /// thirteenth regiment would be cut off for the sins of the first.
        /// </remarks>
        [ThreadStatic] private static long _deadline;

        /// <summary>Opens this thread's allowance, from a stamp already taken.</summary>
        private static void OpenBudget(long began)
        {
            float budget = SearchBudgetMs;

            _deadline = budget > 0f
                ? began + (long)(budget * System.Diagnostics.Stopwatch.Frequency / 1000d)
                : 0L;
        }

        /// <summary>
        /// Whether this plan has spent its allowance.
        /// </summary>
        /// <remarks>
        /// Polled at loop heads rather than at every step, and the callers mask
        /// it to one reading in sixty-four - a timestamp is tens of nanoseconds
        /// and an expansion is not much more, so asking every time would be a
        /// measurable share of the thing it is trying to bound.
        /// </remarks>
        /// <summary>Orders that ran past their budget, on this thread.</summary>
        /// <remarks>
        /// The budget is opened from the same stamp the order is charged
        /// against, so it covers the whole cascade and not the searches alone -
        /// which is what makes "cap everything at ten milliseconds" a thing that
        /// can be asked of it. This says how often the cap actually bit, which
        /// no other counter does: <see cref="StagedRoutePlanner"/>'s counts only
        /// the one place that short-circuits on it.
        /// </remarks>
        [ThreadStatic] public static int OrdersOverBudget;

        /// <summary>Orders planned, on this thread, so the count above has a denominator.</summary>
        [ThreadStatic] public static int OrdersPlanned;

        /// <summary>
        /// Clearance checks, and the ones that found a blocker, charged to the
        /// stage that asked rather than to the check itself.
        /// </summary>
        /// <remarks>
        /// [M119a] found that <b>three quarters of every clearance check in the
        /// game finds a blocker</b>, which says the candidate generators propose
        /// about four legs for every one that survives - but not <i>which</i>
        /// generator. That is the difference between "the arch invents bad arcs"
        /// and "the grid proposes routes that will not walk", and they want
        /// opposite fixes. Filled only while the profiler is running.
        /// </remarks>
        [ThreadStatic] internal static long[]? ChecksBy;

        /// <summary>The refusals among those, by the same stage.</summary>
        [ThreadStatic] internal static long[]? BlockedBy;

        private static void Charge(bool blocked)
        {
            if (!PlanningProfile.Running) return;

            int steps = PlanningProfile.Steps;

            ChecksBy ??= new long[steps];
            BlockedBy ??= new long[steps];

            int who = (int)PlanningProfile.Charged();

            ChecksBy[who]++;
            if (blocked) BlockedBy[who]++;
        }

        internal static bool OutOfTime() =>
            _deadline != 0L && System.Diagnostics.Stopwatch.GetTimestamp() > _deadline;

        /// <summary>
        /// Whether this plan should stop searching, for either reason.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The two reasons are different and the distinction matters to the
        /// caller: out of time means the answer is still wanted and something
        /// has to be handed back, while given up on means nobody is waiting for
        /// it at all. Both stop the search at the same places, so they are
        /// asked together here and told apart where it matters.
        /// </para>
        /// <para>
        /// <b>This is the poll <see cref="GiveUpNow"/> always claimed to have.</b>
        /// The hook was set by the host on every plan and read by nothing in
        /// the default cascade - a gate measured at 0 invocations over 80
        /// orders - so a superseded search ran to completion and the click that
        /// superseded it blocked on it.
        /// </para>
        /// </remarks>
        internal static bool StopNow()
        {
            if (OutOfTime()) return true;

            return Abandoned();
        }

        /// <summary>
        /// Whether the order this plan is answering has been replaced - the
        /// clock left out of it.
        /// </summary>
        /// <remarks>
        /// <b>[M126].</b> <see cref="StopNow"/> asks two questions at once, and
        /// they are not equally settled. Giving up because the budget ran out
        /// costs the player a route, and where that is allowed to happen is a
        /// rule about how the game plays. Giving up because the player has
        /// already clicked somewhere else costs nothing at all: the answer is
        /// thrown away on arrival either way ([M80]), so there is no rule to
        /// decide and nowhere it is wrong to ask.
        /// <para>
        /// So a stage that is not ready to honour a budget can still honour a
        /// supersession, which is what lets the host stop waiting on a plan
        /// nobody wants without any of the budget's open questions being
        /// settled first.
        /// </para>
        /// </remarks>
        internal static bool Abandoned()
        {
            Func<bool>? asked = GiveUpNow;

            return asked != null && asked();
        }

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
        /// <param name="planner">
        /// Which way of planning to use. Null takes
        /// <see cref="RoutePlanners.Default"/>, which is the only thing any
        /// caller in the game passes — the parameter exists so the harness can
        /// put two planners against the same arrangement and print both answers.
        /// </param>
        /// <param name="arriveOn">
        /// The front the regiment is meant to finish on, when the caller knows
        /// it and the unit does not yet. Left out, the planner falls back to
        /// <see cref="UnitInstance.OrderFacing"/>, which is right for anything
        /// re-planning a march already under way and <b>wrong</b> for anything
        /// planning one before giving the order — see
        /// <see cref="RoutePlanners"/> for what that cost.
        /// </param>
        /// <summary>
        /// Whether an order dearer than its own budget says which stage spent
        /// the time.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>[M123].</b> A played session recorded 640 orders, the worst at
        /// 381,7 ms against a 5 ms cap, and every single one of them reported
        /// <c>straight line, nothing searched</c> - a fact about whether the
        /// tangent graph built any places, which for 640 orders in a row it did
        /// not. Nothing in the recording could say whether that 381 ms went to
        /// the ladder, to a way round, to the gate that verifies a route or to
        /// the pass that clears the ground before any of them, and W7 says a
        /// recording answers on its own.
        /// </para>
        /// <para>
        /// So a slow order profiles itself, coarsely: the cascade's stages are
        /// timed and every shared leaf under them is not, which is about a
        /// dozen <see cref="System.Diagnostics.Stopwatch"/> reads an order
        /// rather than the millions the full profile would take. Only orders
        /// that break their own budget say anything, so a quiet field stays
        /// quiet - in that session it would have been 147 lines out of 640.
        /// </para>
        /// <para>
        /// Off unless a host turns it on, and the host that does is the harness.
        /// Left off, this is one static bool read an order.
        /// </para>
        /// </remarks>
        public static bool ExplainSlowPlans;

        /// <summary>
        /// How dear an order has to be before it explains itself, as a multiple
        /// of the search budget.
        /// </summary>
        /// <remarks>
        /// One, so the line appears exactly when the cap was broken - which is
        /// the event worth reading about, and the same threshold the gate's own
        /// message uses. With no budget set at all it falls back to
        /// <see cref="ASlowPlanMs"/>, because "no cap" must not mean "explain
        /// every order".
        /// </remarks>
        public static float ExplainOverBudgets = 1f;

        /// <summary>What counts as a slow plan when no budget is set.</summary>
        private const float ASlowPlanMs = 5f;

        public static Plan PlanTo(
            BattleState battle, UnitInstance unit, IPathfinder pathfinder, Vec2 destination,
            IBattleLog? log = null, IWayRound? wayRound = null, IRoutePlanner? planner = null,
            Facing? arriveOn = null)
        {
            // Never over a profile somebody else opened: a bench measuring the
            // whole tree would have it silently replaced by the coarse one and
            // print a table missing every row it asked for.
            if (!ExplainSlowPlans || PlanningProfile.Running)
                return Planned(battle, unit, pathfinder, destination, log, wayRound, planner, arriveOn);

            PlanningProfile.StartCoarse();

            long opened = System.Diagnostics.Stopwatch.GetTimestamp();

            try
            {
                return Planned(battle, unit, pathfinder, destination, log, wayRound, planner, arriveOn);
            }
            finally
            {
                // Outside the plan's own scope rather than inside it, because a
                // step still open has no inclusive time yet and would read as a
                // stage that cost less than nothing.
                double ms =
                    (System.Diagnostics.Stopwatch.GetTimestamp() - opened) * 1000.0 /
                    System.Diagnostics.Stopwatch.Frequency;

                float over = SearchBudgetMs > 0f ? SearchBudgetMs * ExplainOverBudgets : ASlowPlanMs;

                if (ms >= over)
                    log?.Info("Cost",
                        $"{unit.Def.DisplayName} spent {ms:0.0} ms on that order: " +
                        $"{PlanningProfile.WhereItWent()}.",
                        unit.Id);

                PlanningProfile.Stop();
            }
        }

        private static Plan Planned(
            BattleState battle, UnitInstance unit, IPathfinder pathfinder, Vec2 destination,
            IBattleLog? log, IWayRound? wayRound, IRoutePlanner? planner, Facing? arriveOn)
        {
            // Counted here because this is the one door every plan comes
            // through, whoever opened it (M38) — and timed here for the same
            // reason, so an order given by hand and a re-plan the tick
            // decided on land on the same clock without either caller
            // needing to know it is being timed.
            // Interlocked because a plan may be worked out on a worker while
            // the host reads these for its frame line. A plain ++ is a read, an
            // add and a write, so two workers finishing together lose a count -
            // and a diagnostic that quietly undercounts is worse than none.
            System.Threading.Interlocked.Increment(ref battle.RoutesPlanned);

            using var _profile = PlanningProfile.Measure(PlanningProfile.Step.Plan);

            // Measurement only, and only while the profiler is on: which legs
            // this order has already asked about. See Step.ClearLineRepeat.
            if (PlanningProfile.Running) (_legsSeen ??= new HashSet<long>()).Clear();

            long began = System.Diagnostics.Stopwatch.GetTimestamp();

            // From the same stamp the charge is taken from, so the budget covers
            // exactly what the clock reports and there is no window between them.
            OpenBudget(began);

            try
            {
                return (planner ?? RoutePlanners.Default).PlanTo(
                    battle, unit, pathfinder, destination, log, wayRound, arriveOn);
            }
            finally
            {
                OrdersPlanned++;
                if (OutOfTime()) OrdersOverBudget++;

                // Cleared, or a later plan on this pooled thread would inherit a
                // deadline that expired before it began and give up at once.
                _deadline = 0L;

                long spent = System.Diagnostics.Stopwatch.GetTimestamp() - began;

                System.Threading.Interlocked.Add(ref battle.RoutePlanningTicks, spent);

                // Charged against this frame's allowance whoever asked, so a
                // person's own order still shortens what the tick may spend
                // after it. Orders are never *refused* — see PlanningBudget —
                // but they are not invisible either.
                battle.Planning.Spent(unit.Id, spent);
            }
        }

        /// <summary>
        /// <b>M18</b>'s ladder: straight line, round it, through its own, and the
        /// search when none of those answers.
        /// </summary>
        /// <remarks>
        /// Kept whole while the search that supersedes it is measured against it.
        /// It is reachable only through <see cref="RoutePlanners.TheLadder"/>, and
        /// it goes once the recordings agree.
        /// </remarks>
        public static Plan ByTheLadder(
            BattleState battle, UnitInstance unit, IPathfinder pathfinder, Vec2 destination,
            IBattleLog? log = null, IWayRound? wayRound = null)
        {
            if (battle == null) throw new ArgumentNullException(nameof(battle));
            if (unit == null) throw new ArgumentNullException(nameof(unit));
            if (pathfinder == null) throw new ArgumentNullException(nameof(pathfinder));

            using var _profile = PlanningProfile.Measure(PlanningProfile.Step.Ladder);

            // Rung 1: straight there — asked of the body squared to the line
            // it is about to walk, not the one it is standing in (M24).
            Facing alongIt = AlongTheLine(unit.Position, destination, unit.Facing);

            bool straightThere;

            using (PlanningProfile.Measure(PlanningProfile.Step.Rung1))
                straightThere = IsClearLine(battle, unit, unit.Position, destination, alongIt);

            if (straightThere)
            {
                var line = new[] { unit.Position, destination };

                // W6. The dullest rung, and it still says itself — but only when
                // it is news. A reader working out why a regiment went where it
                // did needs to know the ladder stopped at the first rung,
                // otherwise the absence of a line is ambiguous between "walked
                // straight there" and "the planner was never asked".
                //
                // Once per answer rather than once per plan, because M11 re-plans
                // on a cadence and a march across open ground is re-decided
                // dozens of times without anything having changed. Said
                // unconditionally it was 218 of 297 lines in a twelve-turn
                // battle, and `NoSingleRuleDrownsOutTheRest` failed the build for
                // it — correctly. What is worth writing down is that the answer
                // <i>became</i> the straight line: a regiment that has finished
                // getting round something and can see its destination again is
                // an event, and a regiment still walking is not.
                if (unit.LastRung != 1)
                    log?.Info("Move",
                        $"{unit.Def.DisplayName} is walking straight there — {Route(line)}, " +
                        $"{Vec2.Distance(unit.Position, destination):0} m clear, " +
                        $"{SecondsToWalk(battle, unit, line):0} s.",
                        unit.Id);

                unit.LastRung = 1;

                return Straight(line);
            }

            // Rung 2: round whatever is in the way — but only for a march.
            // Closing with an enemy is O5's business and it says centre first,
            // then sidestep to share the face; arching an attack in would put
            // the two rules in charge of the same approach.
            if (unit.Order.Kind != OrderKind.Attack)
            {
                IReadOnlyList<Vec2>? arch;

                using (PlanningProfile.Measure(PlanningProfile.Step.WayRound))
                    arch = (wayRound ?? WaysRound.Default).Round(battle, unit, destination);

                // M14: full width first, side-on second. Same line, same ground,
                // a body twenty metres across instead of forty.
                IReadOnlyList<Vec2>? threaded;
                Facing?[]? hold;

                using (PlanningProfile.Measure(PlanningProfile.Step.Crab))
                    threaded = CrabThrough(battle, unit, destination, out hold);

                // M22a. Both of these are rung two, and M18 has always said the
                // cheaper of them wins — but the code took the arch whenever it
                // found one and only crabbed if arching had failed, which is not
                // a comparison at all. It is one now that a route can be priced
                // in the thing that actually costs: time.
                //
                // The crab is worked out even when the arch succeeds, which is
                // strictly more work per plan. Affordable because M11 already
                // caps planning to a cadence rather than a tick rate, and
                // because rung two is only reached once the straight line has
                // failed — the common march never gets here at all.
                if (arch != null || threaded != null)
                {
                    float straight = SecondsToWalk(battle, unit, new[] { unit.Position, destination });

                    float arching = arch != null
                        ? SecondsToWalk(battle, unit, arch)
                        : float.MaxValue;

                    float crabbing = threaded != null
                        ? SecondsToWalk(battle, unit, threaded, hold)
                        : float.MaxValue;

                    UnitInstance? blocking = InTheWay(battle, unit, destination);

                    // Said once, when the decision is made, rather than every
                    // tick while it is carried out. Which rung answered is the
                    // whole of what a reader wants: a regiment arching across
                    // the screen looks like a fault unless the log says it chose
                    // to, and one walking through its own looks like a worse one.
                    //
                    // And what it cost, because "the arcs look too wide" is a
                    // judgement, a judgement needs a number, and no rule
                    // anywhere used to record one.
                    // M79, at the rung that actually draws these. Arch and crab
                    // were weighed against each other and never against simply
                    // walking there - and `straight` was computed right here,
                    // three lines up, only to be printed. A recording has one at
                    // 837 s against 109 s, 7,7x: four hundred metres west and
                    // three hundred and forty back east for a hop of a hundred,
                    // with the ratio in its own log line and nothing acting on
                    // it. Declined rather than returned, because returning it
                    // short-circuits everything below: on that same order the
                    // grid had a two-waypoint answer and was never asked.
                    float cheapest = MathF.Min(arching, crabbing);
                    bool wayRoundTooDear =
                        StagedRoutePlanner.StraightLineCostCeiling > 0f &&
                        straight > 1f &&
                        cheapest > straight * StagedRoutePlanner.StraightLineCostCeiling;

                    if (wayRoundTooDear)
                    {
                        StagedRoutePlanner.WayRoundTooDear++;
                    }
                    else if (arching <= crabbing)
                    {
                        Say(log, unit, blocking,
                            "is going round its own {0} rather than through it",
                            arching, straight, arch!, destination,
                            crabbing < float.MaxValue
                                ? $" Threading it side-on would have cost {crabbing:0} s."
                                : " There was no gap to thread.");

                        unit.LastRung = 2;

                        return Straight(arch!);
                    }
                    else
                    {
                        Say(log, unit, blocking,
                            "is turning side-on to thread a gap beside its own {0} — its front will not fit",
                            crabbing, straight, threaded!, destination,
                            arching < float.MaxValue
                                ? $" Arching round would have cost {arching:0} s."
                                : " There was no way round it.",
                            hold);

                        unit.LastRung = 3;

                        return Straight(threaded!, hold);
                    }
                }

                // Rung 3: through its own. Nothing fits, nothing goes round and
                // nothing threads, so the last thing left is to shoulder past
                // them.
                //
                // Two things still have to hold. The ground must be crossable,
                // which is the difference between this and giving up. And the
                // far end must be somewhere the regiment can actually stand:
                // shouldering through men on the way is one thing, coming to
                // rest inside them is another, and it is the placement search's
                // job to find the nearest ground that is free. Without that
                // second test a regiment ordered onto its own troops pressed
                // into them and stopped there, having never said why.
                if (GroundIsClear(battle, unit, unit.Position, destination - unit.Position,
                                  (destination - unit.Position).Length, alongIt) &&
                    NobodyStandingAt(battle, unit, destination))
                {
                    // The one that must never be silent. Two regiments sharing
                    // ground is what M1 spent the whole project forbidding, and
                    // on screen it reads as a collision bug. If it is going to
                    // happen it has to say so, and say that everything else was
                    // tried first.
                    Say(log, unit, InTheWay(battle, unit, destination),
                        "is pushing through its own {0} — no way round it and no gap to thread.",
                        null, null, new[] { unit.Position, destination }, destination,
                        WhatItWillWalkThrough(battle, unit, unit.Position, destination, alongIt));

                    unit.LastRung = 4;

                    return Straight(new[] { unit.Position, destination }, through: true);
                }
            }

            // Rungs 3 and 4 are not built. Until they are, the search is what
            // answers — which is also what answered before any of this.
            PathResult searched = pathfinder.FindPath(unit.Position, destination, unit.Def.Movement);

            // W6. The one rung nobody could see. Everything above says which
            // answer it gave; falling off the bottom of the ladder said nothing
            // at all, so a route that came back bent for reasons the ladder
            // never had looked exactly like one the ladder had chosen.
            //
            // On the change, like rung one and for the same reason. An attack
            // closing the last hundred metres falls here on every re-plan —
            // measured, ten times in a twelve-turn battle for a single pair of
            // swordsmen — and it is the same fact each time.
            if (unit.LastRung != 5)
                log?.Decision("Move",
                    $"{unit.Def.DisplayName} could not walk, bend or shoulder its way there, so the search " +
                    (searched.Found
                        ? $"answered: {Route(searched.Waypoints)}, {searched.Distance:0} m, " +
                          $"{searched.CellsExplored} cells explored."
                        : $"was asked and found nothing. {searched.FailureDetail} [{searched.Failure}]"),
                    unit.Id);

            unit.LastRung = 5;

            return new Plan(searched, null, false);
        }

        /// <summary>
        /// How long a regiment would take to walk a given line, in seconds.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>M22.</b> A route's cost is not its length. Every bend is a wheel,
        /// every wheel is time spent at a fraction of pace, and a planner
        /// choosing on distance will buy a twenty-six metre saving with a right
        /// angle worth forty seconds. Recorded on 14 August: one cavalry
        /// regiment spent <b>1,432 of 2,644 ticks</b> coming round — 54% of the
        /// battle — and no rule with an opinion about where to walk could see any
        /// of it.
        /// </para>
        /// <para>
        /// The wheel is charged as pace lost rather than as time standing still,
        /// because that is what actually happens: a regiment sets off at once
        /// and comes round as it goes. So a leg costs the seconds spent turning,
        /// at whatever the average penalty over the turn is worth, and then the
        /// rest of the leg at the settled pace — which is full for a march and
        /// about two fifths for a crab.
        /// </para>
        /// <para>
        /// It asks <see cref="MovementSystem.AlignmentPenalty"/> rather than
        /// keeping its own copy of the curve. The plan must be priced by the
        /// rule the march will obey, or the two quietly disagree and the routes
        /// look wrong for no findable reason.
        /// </para>
        /// <para>
        /// A model of a wheel, not a simulation of one. It is used to choose
        /// between routes, not to promise a time, and it is held to a quarter
        /// either way against real marches by a property test.
        /// </para>
        /// </remarks>
        /// <param name="hold">
        /// The front to hold on each leg, where a leg asks for one — so a crab
        /// is costed as the crab it is rather than as a march of the same
        /// length.
        /// </param>
        /// <summary>
        /// The same route with every waypoint dropped that the regiment can see
        /// past - the cast-ahead pass every planner's answer goes through.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>M82.</b> Exposed for the route preview, which had no way to reach
        /// it: <c>RouteSmoothing</c> is internal to the rules, and the drawing
        /// code is a Unity assembly outside them. So the preview drew
        /// <see cref="GridPlanning.RegimentGrid.TryRoute"/>'s answer raw, a path
        /// of hex centres, while every route the planner actually hands out has
        /// been straightened - and the grid stage inside the cascade straightens
        /// its own answer before it will even offer it.
        /// </para>
        /// <para>
        /// The gap is not cosmetic. On the field the screenshots were taken
        /// from, at the tenth-of-a-regiment cell size they were taken at, the
        /// preview draws <b>2 864 points across eighteen routes</b> - 159 a
        /// route - where the planner would walk <b>98</b>, five a route, over
        /// <b>8,7% less ground</b>. A picture of a route no planner would ever
        /// return is worse than no picture, because it is read as evidence.
        /// </para>
        /// <para>
        /// Same shape as <b>W5</b> and as the scene report that had to be pulled
        /// out from behind a preview toggle: a diagnostic that reports something
        /// other than what happens sends every investigation that trusts it to
        /// the wrong place.
        /// </para>
        /// </remarks>
        public static IReadOnlyList<Vec2> Straightened(
            BattleState battle, UnitInstance unit, IReadOnlyList<Vec2> waypoints)
        {
            if (battle == null) throw new ArgumentNullException(nameof(battle));
            if (unit == null) throw new ArgumentNullException(nameof(unit));
            if (waypoints == null) throw new ArgumentNullException(nameof(waypoints));
            if (waypoints.Count < 3) return waypoints;

            float length = 0f;
            for (int i = 1; i < waypoints.Count; i++)
                length += Vec2.Distance(waypoints[i - 1], waypoints[i]);

            var plan = new Plan(
                PathResult.Success(
                    waypoints, Array.Empty<Coord>(), length, length, 0),
                hold: null, pressedThrough: false);

            return RouteSmoothing.Applied(battle, unit, plan).Path.Waypoints;
        }

        public static float SecondsToWalk(
            BattleState battle, UnitInstance unit, IReadOnlyList<Vec2> waypoints,
            IReadOnlyList<Facing?>? hold = null)
        {
            if (battle == null) throw new ArgumentNullException(nameof(battle));
            if (unit == null) throw new ArgumentNullException(nameof(unit));
            if (waypoints == null) throw new ArgumentNullException(nameof(waypoints));

            float pace = MathF.Max(0.1f, battle.SpeedOf(unit));
            float turnRate = MathF.Max(1f, unit.Def.Get(UnitAttributes.TurnRate));

            Facing facing = unit.Facing;
            float seconds = 0f;

            for (int i = 1; i < waypoints.Count; i++)
            {
                Vec2 leg = waypoints[i] - waypoints[i - 1];
                float length = leg.Length;

                if (length <= 0f) continue;

                Facing bearing = Facing.FromVector(leg);

                // The front this leg will actually be walked on: whatever it
                // asks for if it is a crab, and the line of march otherwise.
                Facing front = hold != null && i < hold.Count && hold[i].HasValue
                    ? hold[i]!.Value
                    : bearing;

                float toTurn = Degrees(facing, front);
                float settled = Degrees(front, bearing);
                float atTheStart = Degrees(facing, bearing);

                float whileComingRound =
                    pace * MovementSystem.AlignmentPenalty((atTheStart + settled) * 0.5f);

                float onceRound = pace * MovementSystem.AlignmentPenalty(settled);

                // The wheel may outlast the leg, in which case none of it is
                // walked at the settled pace and the second term falls away.
                float covered = MathF.Min(length, toTurn / turnRate * whileComingRound);

                seconds += covered / whileComingRound + (length - covered) / onceRound;

                facing = front;
            }

            return seconds;
        }

        private static float Degrees(Facing from, Facing to) =>
            Facing.AbsoluteDelta(from, to) * 180f / MathF.PI;

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

        /// <summary>Whatever is standing in the way of the straight line.</summary>
        private static UnitInstance? InTheWay(BattleState battle, UnitInstance unit, Vec2 destination) =>
            FirstBodyInTheWay(battle, unit, unit.Position, destination,
                              AlongTheLine(unit.Position, destination, unit.Facing), out _);

        /// <summary>
        /// Reports which rung of the ladder answered, once, as it is decided.
        /// </summary>
        /// <remarks>
        /// Said when the choice is made rather than every tick while it is
        /// carried out — these are decisions, and a decision repeated sixty
        /// times a minute is the noise the whole logging pass was about. Which
        /// rung answered is exactly what a reader needs and cannot otherwise
        /// get: a regiment arching across the screen looks like a fault unless
        /// the recording says it chose to, and one walking through its own looks
        /// like a collision bug rather than the last resort it is.
        /// </remarks>
        /// <param name="seconds">
        /// What the chosen line costs, and what the straight one would have cost
        /// if it had been available. Both null for a decision with nothing to
        /// compare against — pressing through <i>is</i> the straight line, so
        /// "62 s against 62 s straight" says nothing twice.
        /// </param>
        private static void Say(
            IBattleLog? log, UnitInstance unit, UnitInstance? blocker, string what,
            float? seconds, float? straight, IReadOnlyList<Vec2> route, Vec2 destination,
            string alsoConsidered, IReadOnlyList<Facing?>? hold = null)
        {
            if (log == null) return;

            string cost = seconds.HasValue && straight.HasValue
                ? $" — {seconds.Value:0} s against {straight.Value:0} s straight."
                : ".";

            log.Decision("Move",
                $"{unit.Def.DisplayName} " +
                string.Format(what, blocker?.Def.DisplayName ?? "own troops") + cost +
                $" {Route(route, hold)}.{Aside(route, unit.Position, destination)}{alsoConsidered}",
                unit.Id);
        }

        /// <summary>
        /// The route itself, written out, with the front each leg is walked on
        /// where a leg asks for one.
        /// </summary>
        /// <remarks>
        /// <b>W6.</b> Every rung of the ladder used to name itself and none of
        /// them named the line. That is the wrong half: which rung answered is a
        /// one-word summary of a decision whose substance is <i>where it decided
        /// to walk</i>, and without the waypoints a report that "the arcs are too
        /// wide" or "it went through them" could not be checked against anything.
        /// Findings 13 and 14 were both cracked by pulling coordinates out of the
        /// recording; both times the coordinates had to come from a line written
        /// for another purpose.
        /// </remarks>
        private static string Route(IReadOnlyList<Vec2> waypoints, IReadOnlyList<Facing?>? hold = null)
        {
            var line = new System.Text.StringBuilder("by ");

            for (int i = 0; i < waypoints.Count; i++)
            {
                if (i > 0) line.Append(" → ");

                line.Append($"({waypoints[i].X:0},{waypoints[i].Y:0})");

                // Named on the leg that ends here, which is the leg it is the
                // front for.
                if (hold != null && i < hold.Count && hold[i].HasValue)
                    line.Append($" facing {hold[i]!.Value.Degrees:0}°");
            }

            return line.ToString();
        }

        /// <summary>
        /// Which side of the straight line a route passes, and how far off it
        /// goes.
        /// </summary>
        /// <remarks>
        /// The two questions a detour actually raises. "Left, 24 m off" is
        /// checkable against the screen in a way that a list of coordinates is
        /// not, and how far off it swings is the whole of the standing complaint
        /// that routes sit further from the direct line than they need to.
        /// </remarks>
        private static string Aside(IReadOnlyList<Vec2> route, Vec2 from, Vec2 destination)
        {
            Vec2 direct = destination - from;
            float length = direct.Length;

            if (length <= 0f || route.Count <= 2) return string.Empty;

            Vec2 along = direct / length;

            // Positive is left of the line of march, by the usual convention
            // that x turns into y through a quarter turn anticlockwise.
            float furthest = 0f;

            for (int i = 1; i < route.Count - 1; i++)
            {
                Vec2 offset = route[i] - from;
                float side = along.X * offset.Y - along.Y * offset.X;

                if (MathF.Abs(side) > MathF.Abs(furthest)) furthest = side;
            }

            if (MathF.Abs(furthest) < 0.5f) return string.Empty;

            return $" Passing to the {(furthest > 0f ? "left" : "right")}, " +
                   $"{MathF.Abs(furthest):0} m off the straight line.";
        }

        /// <summary>
        /// Who a march is about to walk into, and how far into the first of them
        /// it is already standing.
        /// </summary>
        /// <remarks>
        /// Only asked when rung three has answered — which is the one case where
        /// what gets walked through is the point rather than an aside. The
        /// overlap it is starting from is the part that matters most: a regiment
        /// that sets off already inside somebody is the shape of finding 15, and
        /// it took a play-test, a recording and a reproduction to see it because
        /// no line anywhere reported it.
        /// </remarks>
        private static string WhatItWillWalkThrough(
            BattleState battle, UnitInstance unit, Vec2 from, Vec2 to, Facing facing)
        {
            var body = new OrientedRect(from, facing, unit.Footprint);
            Vec2 travel = to - from;

            int met = 0;
            string lapped = string.Empty;

            foreach (UnitInstance other in battle.UnitsOnField())
            {
                if (!IsInTheWayOf(unit, other)) continue;

                float overlap = OrientedRect.OverlapFraction(body, other.Shape);

                if (overlap > 0f && lapped.Length == 0)
                    lapped = $" It sets off already standing in {other.Def.DisplayName}, " +
                             $"{overlap:0.00} of a body deep.";

                if (Sweep.Touches(body, travel, other.Shape)) met++;
            }

            return $" {met} of its own on that line.{lapped}";
        }


        private static Plan Straight(IReadOnlyList<Vec2> waypoints, Facing?[]? hold = null, bool through = false)
        {
            float total = 0f;

            for (int i = 1; i < waypoints.Count; i++)
                total += Vec2.Distance(waypoints[i - 1], waypoints[i]);

            return new Plan(
                PathResult.Success(waypoints, Array.Empty<Coord>(), total, total, cellsExplored: 0),
                hold, through);
        }

        /// <summary>
        /// Room wanted either side of a regiment threading a gap, in metres.
        /// </summary>
        /// <remarks>
        /// Deliberately much smaller than <see cref="TangentMarginMetres"/>. That
        /// margin is for aiming past a corner from a distance, where drift is the
        /// enemy; this is for a body walking down a corridor it can see the sides
        /// of. Asking eight metres either side would have refused the thirty
        /// metre gap this rule exists to thread.
        /// </remarks>
        private const float RoomEitherSideMetres = 2f;

        /// <summary>
        /// A corridor between two of its own, wide enough to walk down side-on.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>M27</b>, and the fourth report of *"it goes through units"* in a
        /// row. Recorded on 14 August: Archers at (213,213) and Swordsmen at
        /// (263,213), both facing east, both twenty metres deep — <b>thirty
        /// metres of daylight between them</b>. The cavalry was standing in that
        /// gap, was sent straight down it, is twenty metres across side-on, and
        /// shouldered through saying there was no way round and no gap to thread.
        /// </para>
        /// <para>
        /// Every aiming point the planner could generate was tied to <i>one</i>
        /// body: half its depth, plus half the mover's width, plus the margin,
        /// stepping outward from its centre. The point that matters in a formed
        /// line is the <b>midpoint between two neighbours</b>, and nothing ever
        /// produced one. Crabbing was no help either, because it turned side-on
        /// along the line <i>as drawn</i> rather than moving the line into the
        /// space — and the drawn line clipped the Archers by about a metre.
        /// </para>
        /// <para>
        /// So the bodies are projected onto the axis across the march, sorted,
        /// and the spaces between them read off. Any space that admits the
        /// regiment side-on is a corridor, and the nearest such corridor to the
        /// line the player drew wins — because [M4](../../../../docs/DECISIONS.md)
        /// says the line drawn is the order, and a way round should depart from
        /// it as little as it can.
        /// </para>
        /// <para>
        /// Only reached once crabbing along the drawn line has failed, so the
        /// common march never does any of this.
        /// </para>
        /// </remarks>
        private static IReadOnlyList<Vec2>? ThreadAGap(
            BattleState battle, UnitInstance unit, Vec2 destination, out Facing?[]? hold)
        {
            using var _profile = PlanningProfile.Measure(PlanningProfile.Step.ThreadGap);

            hold = null;

            Vec2 travel = destination - unit.Position;
            float length = travel.Length;

            if (length <= 0f) return null;

            Vec2 along = travel / length;
            Facing straight = Facing.FromVector(travel);

            // Ours, near enough to this march to be worth pairing up. Kept as
            // the bodies themselves rather than as projections, because a gap
            // has an axis of its own and projecting onto the march destroys it.
            var near = new List<UnitInstance>();

            foreach (UnitInstance other in battle.UnitsOnField())
            {
                if (!IsInTheWayOf(unit, other)) continue;

                Vec2 offset = other.Position - unit.Position;

                float ahead = Vec2.Dot(offset, along);
                float reach = other.Shape.ProjectedRadius(along);

                if (ahead + reach < 0f || ahead - reach > length) continue;

                near.Add(other);
            }

            PlanningProfile.Tally(PlanningProfile.Step.GapNear, near.Count);

            if (near.Count < 2) return null;

            float wanted = unit.Footprint.Depth + RoomEitherSideMetres * 2f;

            // How much wider than the regiment a passage may be and still be a
            // passage. See GapWidthCeiling - this is the whole of the fix.
            float widest = GapWidthCeiling > 0f ? wanted * GapWidthCeiling : float.MaxValue;

            // Every pair of them, and the passage between the two. Quadratic in
            // a list that is already only the bodies on this march — a handful,
            // and only on the rung the common march never reaches.
            var mouths = new List<(float Off, Vec2 Mouth, Vec2 Far, Facing Front)>();

            // Across the march, so that consecutive bodies are the two a space
            // actually lies between. Signed, because the sort has to put the
            // left flank at one end and the right at the other; an absolute
            // offset interleaves them and makes neighbours of bodies standing
            // on opposite sides of the line.
            var across = new float[near.Count];
            UnitInstance[] order = near.ToArray();

            for (int i = 0; i < order.Length; i++)
            {
                Vec2 offset = order[i].Position - unit.Position;
                across[i] = along.X * offset.Y - along.Y * offset.X;
            }

            Array.Sort(across, order);

            // Where the walk starts: the body that actually stops the straight
            // line. Without one - the caller can reach here for other reasons -
            // the body nearest the drawn line stands in for it, which is the
            // same thing M4 would choose.
            UnitInstance? blocker = InTheWay(battle, unit, destination);

            int at = 0;
            float nearest = float.MaxValue;

            for (int i = 0; i < order.Length; i++)
            {
                if (blocker != null)
                {
                    if (order[i].Id == blocker.Id) { at = i; break; }
                    continue;
                }

                float off = MathF.Abs(across[i]);
                if (off >= nearest) continue;

                nearest = off;
                at = i;
            }

            // The spaces either side of it, outward, alternating: the one to its
            // left, the one to its right, then the next out on each side. A
            // space is numbered by the body on its left, so the blocker at `at`
            // touches spaces `at - 1` and `at`.
            int spaces = order.Length - 1;
            int outward = GapSpacesEitherSide > 0 ? GapSpacesEitherSide : spaces;

            var walk = new List<int>(Math.Min(spaces, outward * 2 + 2));

            for (int out_ = 0; out_ <= outward && walk.Count < spaces; out_++)
            {
                int left = at - 1 - out_;
                int right = at + out_;

                if (0 <= left && left < spaces) walk.Add(left);
                if (right != left && 0 <= right && right < spaces) walk.Add(right);
            }

            PlanningProfile.Tally(PlanningProfile.Step.GapPairs, walk.Count);

            foreach (int space in walk)
            {
                UnitInstance a = order[space];
                UnitInstance b = order[space + 1];

                Vec2 between = b.Position - a.Position;
                float apart = between.Length;

                if (apart <= 0f) continue;

                Vec2 side = between / apart;

                // The clear space between the two, measured along the line that
                // joins them — which is the only direction in which "the gap
                // between these two" means anything.
                float clear = apart - a.Shape.ProjectedRadius(side) - b.Shape.ProjectedRadius(side);

                if (clear < wanted) continue;

                // <b>And not too wide.</b> Two regiments eight hundred metres
                // apart trivially have room between them, and the open country
                // between them is not a gap - it is ground the straight line
                // and the arch already answer. Measured before this existed:
                // 95% of every pair examined passed the width test, 75 318
                // passages were clearance-tested across five fields, and 32 of
                // them became a route. The generator was drawing two thousand
                // three hundred candidates for every answer it found.
                if (clear > widest)
                {
                    PlanningProfile.Tally(PlanningProfile.Step.GapTooWide);
                    continue;
                }

                // A corridor runs square through the gap, not along the march.
                // That distinction is the whole of this rule: at the recorded
                // angle the same thirty metre gap projects onto the line of
                // march as twenty — exactly the regiment's own depth, no room at
                // all — and threading it means going through the way the gap
                // faces, then resuming.
                var axis = new Vec2(-side.Y, side.X);

                if (Vec2.Dot(axis, along) < 0f) axis = new Vec2(-axis.X, -axis.Y);

                // Square to the corridor is side-on to it: the regiment presents
                // its depth to the walls either side.
                Facing front = Facing.FromVector(side);

                Vec2 centre = (a.Position + b.Position) * 0.5f;

                float deep = MathF.Max(a.Shape.ProjectedRadius(axis), b.Shape.ProjectedRadius(axis))
                             + unit.Footprint.Width * 0.5f + TangentMarginMetres;

                Vec2 mouth = centre - axis * deep;
                Vec2 far = centre + axis * deep;

                // How far this passage sits from the line that was drawn, so the
                // nearest one is tried first — M4 again: the drawn line is the
                // order, and a way round departs from it as little as it can.
                Vec2 offset = centre - unit.Position;

                mouths.Add((MathF.Abs(along.X * offset.Y - along.Y * offset.X), mouth, far, front));
            }

            PlanningProfile.Tally(PlanningProfile.Step.GapMouths, mouths.Count);

            if (mouths.Count == 0) return null;

            mouths.Sort((x, y) => x.Off.CompareTo(y.Off));

            foreach ((float _, Vec2 mouth, Vec2 far, Facing front) in mouths)
            {
                // The corridor first, though it is the middle leg of the three.
                // It is the squeeze — the thing most likely to refuse — and it
                // is also much the shortest line of the three, so its scan is
                // the cheapest of them. Asked last, as it was, every mouth the
                // gap itself would have rejected was paid for twice over in
                // full-length scans up to it and away from it. The three are a
                // conjunction of pure tests, so the order cannot change which
                // mouths are taken, only what is spent finding out.
                PlanningProfile.Tally(PlanningProfile.Step.GapMouthsTried);

                if (!IsClearLine(battle, unit, mouth, far, front, leaving: true)) continue;
                if (!IsClearLeg(battle, unit, unit.Position, mouth, unit.Facing)) continue;
                if (!IsClearLeg(battle, unit, far, destination, front)) continue;

                PlanningProfile.Tally(PlanningProfile.Step.GapThreaded);

                // Named on the leg it belongs to, as the drawn-line crab does:
                // square to the corridor going through it, and back onto the
                // line of march for the run out to the destination.
                hold = new Facing?[] { null, null, front, straight };

                return new[] { unit.Position, mouth, far, destination };
            }

            return null;
        }

        /// <summary>
        /// Whether the straight line works for a body turned side-on to it.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Travelling along its own frontage, a regiment presents its depth to
        /// the way it is going — twenty metres rather than forty — so a gap that
        /// refuses a march admits a crab. Setting one up means turning, because
        /// presenting the narrow side and facing square to the line of travel
        /// are the same thing.
        /// </para>
        /// <para>
        /// Both perpendiculars are the same shape, so the one nearer the front
        /// the regiment already holds is taken: the manoeuvre is expensive
        /// enough in pace without turning the long way into it as well.
        /// </para>
        /// </remarks>
        public static IReadOnlyList<Vec2>? CrabThrough(
            BattleState battle, UnitInstance unit, Vec2 destination, out Facing?[]? hold)
        {
            hold = null;

            Vec2 travel = destination - unit.Position;
            float length = travel.Length;
            if (length <= 0f) return null;

            Vec2 along = travel / length;
            Facing straight = Facing.FromVector(travel);

            // Both perpendiculars are the same shape, so take the one nearer the
            // front already held: the manoeuvre is expensive enough in pace
            // without turning the long way into it as well.
            Facing left = Facing.FromRadians(straight.Radians + MathF.PI * 0.5f);
            Facing right = Facing.FromRadians(straight.Radians - MathF.PI * 0.5f);

            bool leftIsNearer =
                Facing.AbsoluteDelta(unit.Facing, left) <= Facing.AbsoluteDelta(unit.Facing, right);

            Facing sideOn = leftIsNearer ? left : right;

            if (!IsClearLine(battle, unit, unit.Position, destination, sideOn))
            {
                sideOn = leftIsNearer ? right : left;

                // M27. The line as drawn does not admit the regiment on either
                // edge — so try moving the line into the space instead of
                // giving up on it.
                if (!IsClearLine(battle, unit, unit.Position, destination, sideOn))
                    return ThreadAGap(battle, unit, destination, out hold);
            }

            // Where the squeeze actually is. The regiment marches up to it
            // facing where it is going, goes through side-on, and comes back
            // onto its march afterwards — "turn round for the crabbing only".
            // Crabbing the whole way would arrive at the far end still side-on
            // and at two fifths pace for a journey that never needed it.
            UnitInstance? tight =
                FirstBodyInTheWay(battle, unit, unit.Position, destination, straight, out float upTo);

            if (tight == null) return null;

            float entry = MathF.Max(0f, upTo - TangentMarginMetres);

            // Walked out to where the march can honestly be resumed, rather than
            // guessed at from the blocker's depth. The guess was short: it
            // allowed for the body it was going round and not for the body doing
            // the going, which is twice as long side-on as it is deep. The
            // regiment was told to face front again while still inside the wall,
            // and every leg after that was a line nobody had checked.
            //
            // Asked of the thing that will actually be true — can the rest of
            // the march be walked from here facing forwards — so it cannot be
            // short by an arithmetic slip a second time.
            float exit = length;

            for (float probe = upTo; probe < length; probe += TangentMarginMetres)
            {
                Vec2 at = unit.Position + along * probe;

                if (!IsClearLine(battle, unit, at, destination, straight)) continue;

                exit = probe;
                break;
            }

            bool theWholeWay = entry <= 0f && exit >= length;

            // M81. How much of this route is actually walked side-on. The rung
            // is a manoeuvre at a gap; past the ceiling it is not threading
            // anything, it is just a slow march, and the cascade below is
            // better placed to answer than a crab nobody priced against it.
            float crabbed = theWholeWay ? length : MathF.Max(0f, exit - entry);

            if (StagedRoutePlanner.CrabbedShareCeiling < 1f &&
                crabbed > length * StagedRoutePlanner.CrabbedShareCeiling)
            {
                StagedRoutePlanner.CrabTooLong++;
                return null;
            }

            // The squeeze runs the whole way, so there is nothing to come back
            // onto and the simple form is the honest one.
            if (theWholeWay)
            {
                hold = new Facing?[] { null, sideOn };
                return new[] { unit.Position, destination };
            }

            // The last leg names the front it ends on rather than leaving it
            // implied, and that is not tidiness. Coming *off* a crab is the same
            // ninety degrees as going onto it, and the stall detector only
            // forgives a regiment for coming round when the leg it is walking
            // says which front it wants. Left as null, a regiment that had just
            // threaded a gap was declared stuck at the far side of it — the
            // fault it had been rescued from, one waypoint later.
            hold = new Facing?[] { null, null, sideOn, straight };

            return new[]
            {
                unit.Position,
                unit.Position + along * entry,
                unit.Position + along * exit,
                destination
            };
        }

        /// <summary>
        /// Somewhere for an index query to put its answer without allocating.
        /// </summary>
        /// <remarks>
        /// One per thread and one per asking method, rather than one shared
        /// between them. Nothing nests today — the body of a clearance check is
        /// pure geometry and asks the index nothing — but a shared buffer would
        /// make the day somebody adds a query inside one of these loops into a
        /// silent wrong answer rather than a compile error, and that is the kind
        /// of bug this sweep has spent its time removing.
        /// </remarks>
        [ThreadStatic]
        private static List<UnitInstance>? _onTheLine;

        [ThreadStatic]
        private static List<UnitInstance>? _atThePlace;

        [ThreadStatic]
        private static List<UnitInstance>? _aheadOnTheLine;

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
        /// <param name="leaving">
        /// Whether this line is a way <i>out</i> of something the regiment is
        /// already inside. A body it laps at the start then stops being an
        /// obstacle, because being inside it is the reason a way round is wanted
        /// — see <see cref="IsClearLeg"/>, which is the only caller that says so.
        /// </param>
        public static bool IsClearLine(
            BattleState battle, UnitInstance unit, Vec2 from, Vec2 to, Facing facing,
            bool leaving = false, bool leavingGrazeOnly = false) =>
            IsClearLine(battle, unit, from, to, facing, out _, leaving, leavingGrazeOnly);

        /// <summary>
        /// The same question, and which regiment answered no.
        /// </summary>
        /// <param name="blocker">
        /// A body that refused the line, or nothing if it was clear or the
        /// <i>ground</i> refused it. Terrain is not something this search can
        /// walk about — that is the pathfinder's job (<b>M10</b>).
        /// <para>
        /// <b>A</b> body, not the nearest one: the loop stops at the first no,
        /// as it always has. Naming the body the march meets first instead was
        /// built and measured, and it cost between a quarter and half again as
        /// much time across the whole scaling bench while changing not one
        /// route — so the extra sweeps bought nothing.
        /// </para>
        /// </param>
        /// <summary>Legs this order has already asked about. Measurement only.</summary>
        [ThreadStatic] private static HashSet<long>? _legsSeen;

        /// <summary>One leg, quantised, as a key. Measurement only.</summary>
        private static long LegKey(Vec2 from, Vec2 to, Facing facing)
        {
            long a = (long)MathF.Round(from.X * 100f) * 73856093L
                   ^ (long)MathF.Round(from.Y * 100f) * 19349663L;
            long b = (long)MathF.Round(to.X * 100f) * 83492791L
                   ^ (long)MathF.Round(to.Y * 100f) * 39916801L;

            return a ^ (b << 1) ^ ((long)MathF.Round(facing.Radians * 1000f) * 2654435761L);
        }

        public static bool IsClearLine(
            BattleState battle, UnitInstance unit, Vec2 from, Vec2 to, Facing facing,
            out UnitInstance? blocker, bool leaving = false, bool leavingGrazeOnly = false)
        {
            blocker = null;

            using var _profile = PlanningProfile.Measure(PlanningProfile.Step.ClearLine);

            if (PlanningProfile.Running && _legsSeen != null && !_legsSeen.Add(LegKey(from, to, facing)))
                PlanningProfile.Tally(PlanningProfile.Step.ClearLineRepeat);

            Vec2 travel = to - from;
            float length = travel.Length;

            if (length <= 0f) return battle.FormationFits(unit, from, facing);

            var body = new OrientedRect(from, facing, unit.Footprint);

            // A safe bound on how far from the segment a body could possibly
            // reach and still be touched: the swept rectangle's own bounding
            // circle at either end, plus the obstacle's. It only has to be
            // provably safe, not tight, so it can never turn a real collision
            // into a missed one — and since M32 it decides candidate places
            // too, by way of the blocker this hands back.
            float reach = unit.Footprint.BoundingRadius;
            Vec2 along = travel / length;

            List<UnitInstance> all = _onTheLine ??= new List<UnitInstance>(32);

            // The query is inside the measured scope, not before it: narrowing
            // the field is now part of what asking this question costs, and a
            // stopwatch that started after it would report the saving without
            // the price of it (W5).
            using (PlanningProfile.Measure(PlanningProfile.Step.BodyScan))
            {
                using (PlanningProfile.Measure(PlanningProfile.Step.NearQuery))
                    battle.WhereEverybodyIs.Near(battle.AllUnits, from, to, reach, all);

                PlanningProfile.Tally(PlanningProfile.Step.NearYield, all.Count);
                if (all.Count == 0) PlanningProfile.Tally(PlanningProfile.Step.NearEmpty);

                // M119's ceiling. A check where nothing was ever near the leg is
                // a check a field lookup could have answered without a query or
                // a scan, because an unmarked cell means no body overlaps a
                // mover standing on it. Counted rather than acted on: what is
                // wanted first is the share, since the share times what the
                // query and the scan cost is the most the idea can ever be
                // worth, and four things in this family have already been built
                // before their ceiling was known.
                bool anybodyNear = false;

                for (int u = 0; u < all.Count; u++)
                {
                    UnitInstance other = all[u];
                    if (!other.IsOnField) continue;
                    if (!IsInTheWayOf(unit, other)) continue;

                    // Measured on a real battle: this alone cut a plan's cost in
                    // half against an army of a hundred regiments none of which
                    // were anywhere near the march, because every clearance check
                    // on every leg was asking Sweep.FirstTouch and OverlapFraction
                    // about bodies the segment could not geometrically reach.
                    // Skipped when the index has already applied it. M118's
                    // first measurement had the sift *added* here rather than
                    // moved, so it paid this projection twice a body and was
                    // reported as the sift costing 15% - which was the cost of
                    // doing the same arithmetic twice, not of doing it earlier
                    // (W5: a measurement reports what actually happened).
                    if (!UnitIndex.SiftAtTheIndex)
                    {
                        float span = reach + other.Footprint.BoundingRadius;
                        if (DistanceSquaredToSegment(other.Position, from, along, length) >
                            span * span)
                            continue;
                    }

                    // Past the distance filter, so this body is genuinely beside
                    // the leg and the check could not have been answered by
                    // "nothing is near here".
                    anybodyNear = true;

                    if (WhereItIsStanding(body, travel, other)) continue;

                    if (leaving && OrientedRect.Overlaps(body, other.Shape))
                    {
                        // Ordinarily any overlap at the start is excused outright:
                        // the regiment already occupies that ground and getting
                        // clear of it is the steering's business (M25). A candidate
                        // the search invented is not ground anybody occupies, so
                        // <paramref name="leavingGrazeOnly"/> narrows that.
                        //
                        // Narrowed to a brush <i>at the start</i>, once, and it was
                        // not enough. A leg from a candidate that grazed one percent
                        // of a body at its near end was excused entirely — not just
                        // at that one point, but for the whole leg, because the
                        // excuse skips this body's <see cref="Sweep.FirstTouch"/>
                        // altogether. Measured: the same leg reached 66% inside that
                        // body a fifth of the way along, unchecked, because nothing
                        // after the first instant was ever asked about again. A
                        // route through it read as clear and, on screen, plainly
                        // was not.
                        //
                        // So a graze has to be a graze along the whole leg, not only
                        // at the door.
                        if (!leavingGrazeOnly || WorstOverlapAlong(body, travel, other.Shape) <= OrderSystem.GrazingTolerance)
                            continue;
                    }

                    // Provably blocked without the sweep, where it can be shown
                    // cheaply. [M124], and it is the other half of [M120]: the
                    // arch refuses 81,5-85,5% of the legs it asks about and the
                    // straightening pass 95,8-97,7%, and every one of those
                    // refusals pays five separating-axis tests over a hexagon
                    // and a rectangle to be told what a distance could have
                    // said.
                    if (ProveBlockedCheaply && SurelyMeets(body, travel, along, length, other.Shape))
                    {
                        ProvedBlocked++;

                        // The proof is only ever asked to agree, never to
                        // disagree: it claims a block, and a claim the sweep
                        // does not confirm is a route wrongly refused (W9 - a
                        // check has to say what would make it fail).
                        if (CheckTheCheapProof && !Sweep.Touches(body, travel, other.Shape))
                            ProofDisagreed++;

                        PlanningProfile.Tally(PlanningProfile.Step.ClearLineBlocked);
                        Charge(blocked: true);
                        blocker = other;
                        return false;
                    }

                    // Touches rather than FirstTouch: M36 wants to know *which*
                    // body refused the line, which is this loop variable, and never
                    // how far along it was met.
                    if (Sweep.Touches(body, travel, other.Shape))
                    {
                        PlanningProfile.Tally(PlanningProfile.Step.ClearLineBlocked);
                        Charge(blocked: true);
                        blocker = other;
                        return false;
                    }
                }

                PlanningProfile.Tally(anybodyNear
                    ? PlanningProfile.Step.ClearLineSomebodyNear
                    : PlanningProfile.Step.ClearLineNobodyNear);

                Charge(blocked: false);
            }

            // The ground last, and it is worth saying why, because it used to be
            // first and that was most of the bill.
            //
            // Terrain is a field rather than a shape, so it cannot be swept — it
            // is sampled, every ten metres along the leg, and every sample tests
            // the whole footprint over a grid of up to twenty-seven points. A
            // leg two hundred metres long is therefore some five hundred terrain
            // lookups, against a handful of rectangle tests for the regiments.
            // Measured on a whole army's orders: dropping this check took
            // planning from 82 ms to 13 for the ladder and 101 to 31 for the
            // search, so it was around three quarters of everything.
            //
            // Nothing about it got cheaper. It simply runs after the cheap
            // questions now, so a leg refused by a regiment standing in it never
            // pays for a terrain scan it was never going to need. Same answer,
            // asked in the order that settles it soonest.
            return GroundIsClear(battle, unit, from, travel, length, facing);
        }

        /// <summary>
        /// Whether a test cheap enough to run before every sweep may refuse a
        /// leg on its own.
        /// </summary>
        /// <remarks>See <see cref="SurelyMeets"/>. Measured in [M124].</remarks>
        internal static bool ProveBlockedCheaply = true;

        /// <summary>Whether every cheap refusal is put to the sweep as well.</summary>
        /// <remarks>
        /// Off in play, on in the bench that proves the two agree. It cannot make
        /// the planner cheaper - it makes it dearer - and its only job is to turn
        /// "the proof is sound" from an argument into a count.
        /// </remarks>
        internal static bool CheckTheCheapProof;

        /// <summary>Legs refused by the proof rather than by the sweep.</summary>
        [ThreadStatic] internal static long ProvedBlocked;

        /// <summary>Times the sweep did not agree. Must stay nought.</summary>
        [ThreadStatic] internal static long ProofDisagreed;

        /// <summary>
        /// Whether a body carried along a line <b>certainly</b> meets an
        /// obstacle - never whether it might.
        /// </summary>
        /// <remarks>
        /// <para>
        /// One-sided on purpose. A rectangle <i>contains</i> the capsule drawn
        /// round its long axis at half its short one: for a regiment 229 m by
        /// 114, that is a 114 m-thick sausage 115 m long, which is most of the
        /// body. Carried along a line without turning, the mover likewise
        /// contains a capsule of its own half-breadth round the line it walks.
        /// Two capsules meet exactly when their spines come within the sum of
        /// their radii, so a segment-to-segment distance under that sum proves a
        /// collision, and proves it in about a dozen multiplications against the
        /// sweep's five separating axes over ten projected corners.
        /// </para>
        /// <para>
        /// It can only ever say <i>yes</i>. A pair it cannot prove goes to the
        /// sweep exactly as before, so a leg is never wrongly allowed - the
        /// direction that matters, because a wrongly allowed leg is a regiment
        /// walking through one of its own and a wrongly refused one is only a
        /// detour. What it does not cover is a meeting corner to corner, which
        /// the sweep still answers.
        /// </para>
        /// </remarks>
        private static bool SurelyMeets(
            in OrientedRect moving, Vec2 travel, Vec2 along, float length, in OrientedRect obstacle)
        {
            // The mover as a disc rather than a spine: it is swept, so its own
            // long axis would make this a parallelogram against a segment and
            // the arithmetic would stop being cheaper than the thing it replaces.
            float mine = MathF.Min(moving.Footprint.Width, moving.Footprint.Depth) * 0.5f;

            float wide = obstacle.Footprint.Width;
            float deep = obstacle.Footprint.Depth;
            float theirs = MathF.Min(wide, deep) * 0.5f;

            float reach = mine + theirs;

            // The obstacle's spine: half its long side less half its short one,
            // either way along whichever axis is the longer.
            float half = MathF.Abs(wide - deep) * 0.5f;

            Vec2 spine = wide >= deep ? obstacle.Right : obstacle.Forward;

            return SegmentsWithin(
                moving.Centre, along, length,
                obstacle.Centre - spine * half, obstacle.Centre + spine * half, reach);
        }

        /// <summary>
        /// Whether two segments come within a distance of one another, without
        /// working out how close they actually get.
        /// </summary>
        /// <remarks>
        /// Sampled along the spine rather than solved. The stations are spaced
        /// at the tolerance itself, so a crossing cannot fall between two of
        /// them, and each station is the same point-to-segment distance the
        /// broad phase already runs. Solving the closest approach of two
        /// segments is exact and is where the parallel and degenerate cases
        /// live; this only has to be safe in one direction, and a station it
        /// misses costs a sweep rather than a wrong answer.
        /// </remarks>
        private static bool SegmentsWithin(
            Vec2 from, Vec2 along, float length, Vec2 tail, Vec2 head, float reach)
        {
            float span = Vec2.Distance(tail, head);
            float squared = reach * reach;

            int stations = span <= reach ? 1 : (int)MathF.Ceiling(span / reach);

            for (int i = 0; i <= stations; i++)
            {
                Vec2 at = Vec2.Lerp(tail, head, (float)i / stations);

                if (DistanceSquaredToSegment(at, from, along, length) <= squared) return true;
            }

            return false;
        }

        /// <summary>
        /// How far a point stands from the nearest point on a segment, squared.
        /// </summary>
        /// <remarks>
        /// Squared, and given the segment's unit direction rather than its far
        /// end, because this is the broad-phase test every body on the field
        /// pays on every leg: the caller already has the direction, and a
        /// comparison against a squared span says the same thing as a distance
        /// without the square root.
        /// </remarks>
        private static float DistanceSquaredToSegment(Vec2 point, Vec2 from, Vec2 along, float length)
        {
            float projected = MathF.Max(0f, MathF.Min(length, Vec2.Dot(point - from, along)));

            return Vec2.DistanceSquared(point, from + along * projected);
        }

        /// <summary>How many points along a leg a graze is checked at.</summary>
        /// <remarks>
        /// Sampled rather than swept properly, on purpose: this only runs for a
        /// leg that already touches the body it is being asked about, on a
        /// front held constant the whole way, which is a much smoother question
        /// than <see cref="Sweep.FirstTouch"/>'s general one. Sixteen points
        /// caught a leg that went from a one percent graze to sixty-six percent
        /// inside a body eleven metres later; doubling it moved the measured
        /// worst point by under a tenth of a percent.
        /// </remarks>
        private const int GrazeSamples = 16;

        /// <summary>
        /// The worst a moving body ever overlaps an obstacle along a straight
        /// leg, as a fraction of the moving body's own area.
        /// </summary>
        /// <remarks>
        /// What a leg starting inside a graze actually needs asked of it. Asking
        /// only at the start answers "is this leg allowed to set off", which is
        /// not the same question as "is this leg allowed to be walked" — a leg
        /// can start touching a body by a hair and be most of the way inside it
        /// eleven metres later, and the first question says nothing about the
        /// second.
        /// </remarks>
        private static float WorstOverlapAlong(in OrientedRect moving, Vec2 travel, in OrientedRect obstacle)
        {
            using var _profile = PlanningProfile.Measure(PlanningProfile.Step.GrazeAlong);

            float length = travel.Length;
            if (length <= 0f) return OrientedRect.OverlapFraction(moving, obstacle);

            Vec2 direction = travel / length;
            float worst = 0f;

            for (int i = 0; i <= GrazeSamples; i++)
            {
                Vec2 at = moving.Centre + direction * (length * i / GrazeSamples);
                var here = new OrientedRect(at, moving.Facing, moving.Footprint);

                float overlap = OrientedRect.OverlapFraction(here, obstacle);
                if (overlap > worst) worst = overlap;
            }

            return worst;
        }

        /// <summary>
        /// Whether a body is something the regiment is already standing in,
        /// rather than something its line runs into.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>M25.</b> The steering has always known this — <i>"already lapping
        /// them, so this step cannot be what did it"</i> — and the planner never
        /// did, which made leaving a formed line impossible to plan.
        /// </para>
        /// <para>
        /// A line stands shoulder to shoulder; that is what a line is and what
        /// [M2](../../../../docs/DECISIONS.md) exists to permit. Square a
        /// regiment onto a new bearing in the middle of one and its rectangle
        /// laps its neighbours before it has moved a metre — a 40 × 20 body
        /// turned 50° reaches 21.7 m along the old axis where it reached 10.
        /// The sweep then reported a collision at distance zero on every
        /// candidate leg, so rung two found nothing on either side, twice over,
        /// and rung three answered.
        /// </para>
        /// <para>
        /// Recorded: eight press-throughs in one game, every one of them setting
        /// off from the same forty metres of ground, every one of them blocked
        /// by a regiment the cavalry was drawn up beside. It reproduces at every
        /// bearing of the compass.
        /// </para>
        /// <para>
        /// Overlap and not proximity, deliberately. A neighbour merely close by
        /// is still an obstacle and still gets gone round; what is excused is
        /// the ground the regiment is already occupying. Getting clear of that
        /// is the steering's job, and since [M20](../../../../docs/DECISIONS.md)
        /// and [M1a](../../../../docs/DECISIONS.md) it is a job with a price and
        /// a rule about who gives way.
        /// </para>
        /// <para>
        /// <b>And overlapping is not enough on its own.</b> Written without the
        /// second half, this excused a body directly in front as readily as one
        /// alongside — so two regiments ordered to swap places met in the middle,
        /// each decided the other was merely where it was standing, and both
        /// planned straight on. Neither routed round, neither yielded, and they
        /// leant on each other for the rest of the game. Caught by
        /// `TwoRegimentsSwappingPlacesDoNotDeadlock`, which is eight months older
        /// than any of this.
        /// </para>
        /// <para>
        /// The line between the two cases is which way the body lies. Abreast or
        /// behind is ground you are leaving. Ahead is a regiment you are walking
        /// into, and no amount of already touching it makes that untrue.
        /// </para>
        /// </remarks>
        private static bool WhereItIsStanding(OrientedRect body, Vec2 along, UnitInstance other)
        {
            if (!OrientedRect.Overlaps(body, other.Shape)) return false;

            return Vec2.Dot(other.Position - body.Centre, along) <= 0f;
        }

        /// <summary>
        /// Whether a leg of a bent route is clear for the body that will walk it.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>M23</b>, and it is the other half of the same fix. A regiment now
        /// comes round onto every leg, so the shape travelling a leg is the one
        /// squared to <i>that</i> leg — not the one it happened to be holding
        /// when the route was planned, which is what every check here used to
        /// ask about. A body forty metres across and twenty deep is a different
        /// obstacle at forty-seven degrees, and checking the wrong one is how a
        /// route that was found clear puts a corner through what it just went
        /// round.
        /// </para>
        /// <para>
        /// Both ends of the turn are checked, because the wheel happens on the
        /// leg rather than before it: the regiment enters still holding the
        /// previous bearing and comes round as it goes. The bulge between the
        /// two — a rectangle sweeps about two metres wider mid-rotation than at
        /// either end — is what <see cref="TangentMarginMetres"/> is for.
        /// </para>
        /// </remarks>
        public static bool IsClearLeg(
            BattleState battle, UnitInstance unit, Vec2 from, Vec2 to, Facing entering)
        {
            Facing holding = AlongTheLine(from, to, entering);

            if (!IsClearLine(battle, unit, from, to, holding, leaving: true)) return false;

            if (Facing.AbsoluteDelta(entering, holding) < 0.01f) return true;

            return IsClearLine(battle, unit, from, to, entering, leaving: true);
        }

        /// <summary>
        /// The front a regiment will hold while walking a line.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>M24.</b> The planner used to ask every one of its questions about
        /// <c>unit.Facing</c> — the front the regiment happens to be standing on
        /// at the moment the order arrives. That is a transient. The regiment is
        /// about to come round onto the line ([M3](../../../../docs/DECISIONS.md),
        /// and [M23](../../../../docs/DECISIONS.md) now makes it true of every
        /// leg), so the shape that travels is the one squared to the line, and
        /// the shape it is standing in has nothing to do with where it can go.
        /// </para>
        /// <para>
        /// Measured: the same regiment, the same line, the same ground, and only
        /// the front it was left on varied. At 0°, 30°, 60°, 120°, 150° and 180°
        /// off the line it walked round its own. At <b>90°</b> — presenting its
        /// whole forty-metre frontage broadside, so the swept corridor is twice
        /// as wide as the one it will actually occupy — rung one failed, rung
        /// two failed, and it shouldered straight through them.
        /// </para>
        /// <para>
        /// The wheel at the start is real and is not the plan's business. It is
        /// the steering's, which is what the steering is for; the plan is about
        /// the line.
        /// </para>
        /// </remarks>
        public static Facing AlongTheLine(Vec2 from, Vec2 to, Facing fallback)
        {
            Vec2 leg = to - from;

            return leg.IsNearZero ? fallback : Facing.FromVector(leg);
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

        /// <summary>
        /// Whether a regiment could come to rest here without sharing ground
        /// with one of its own.
        /// </summary>
        private static bool NobodyStandingAt(BattleState battle, UnitInstance unit, Vec2 at)
        {
            var body = new OrientedRect(at, unit.Facing, unit.Footprint);

            List<UnitInstance> near = _atThePlace ??= new List<UnitInstance>(32);
            battle.WhereEverybodyIs.Near(battle.AllUnits, at, unit.Footprint.BoundingRadius, near);

            for (int i = 0; i < near.Count; i++)
            {
                UnitInstance other = near[i];
                if (!other.IsOnField) continue;
                if (!IsInTheWayOf(unit, other)) continue;

                if (OrientedRect.Overlaps(body, other.Shape)) return false;
            }

            return true;
        }

        private static bool GroundIsClear(
            BattleState battle, UnitInstance unit, Vec2 from, Vec2 travel, float length, Facing facing)
        {
            using var _profile = PlanningProfile.Measure(PlanningProfile.Step.GroundClear);

            // Everything this leg could possibly touch, however the body is
            // turned: both ends, each grown by the footprint's own bounding
            // circle. If none of that rectangle is ground this unit cannot
            // enter, no point inside it can be either, and the sampling below
            // would spend a thousand lookups proving it.
            Vec2 to = from + travel;
            float reach = unit.Footprint.BoundingRadius;

            var min = new Vec2(MathF.Min(from.X, to.X) - reach, MathF.Min(from.Y, to.Y) - reach);
            var max = new Vec2(MathF.Max(from.X, to.X) + reach, MathF.Max(from.Y, to.Y) + reach);

            if (battle.PassableFor(unit.Def.Movement).NothingInTheWay(min, max))
                return true;

            // Asking the same question again per step was built and measured
            // and is deliberately not here. A regiment 229 m across has a
            // bounding circle of 128 m, so a step's own rectangle is over a
            // quarter of a kilometre wide: on a map with mountains in it that
            // rectangle catches one nearly everywhere the whole leg did, the
            // table answers no, and the query is pure overhead on top of the
            // sampling it failed to avoid. Measured: the default planner went
            // from 48,1 ms a plan back up to 52,8, and the ladder from 20,0
            // to 30,4.
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

            List<UnitInstance> near = _aheadOnTheLine ??= new List<UnitInstance>(32);
            battle.WhereEverybodyIs.Near(battle.AllUnits, from, to, unit.Footprint.BoundingRadius, near);

            for (int i = 0; i < near.Count; i++)
            {
                UnitInstance other = near[i];
                if (!other.IsOnField) continue;
                if (!IsInTheWayOf(unit, other)) continue;

                // M25, and asked here too so that "what is in the way" and "is
                // this line clear" cannot come back with different answers —
                // otherwise the planner would aim past a body the clearance
                // check had already excused, or excuse one it was aiming past.
                if (WhereItIsStanding(body, travel, other)) continue;

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
