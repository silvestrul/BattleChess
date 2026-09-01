using BattleChess.Rules.GridPlanning;
using UnityEngine;

namespace BattleChess.Unity
{
    /// <summary>
    /// Toggles for hand-testing.
    /// </summary>
    /// <remarks>
    /// Lives entirely in the view layer. Options that change how the simulation
    /// behaves do so by swapping an implementation — <see cref="IgnoreTerrain"/>
    /// substitutes a different movement model rather than setting a flag the
    /// core has to check. That keeps debugging concerns out of the rules
    /// completely, which matters because a flag the core respects is a flag that
    /// can be wrong in a shipped build.
    /// </remarks>
    public sealed class DebugOptions
    {
        /// <summary>Jump a unit to its destination instead of walking it there.</summary>
        public bool MoveInstantly;

        /// <summary>Order units to come round onto the bearing before setting off.</summary>
        public bool WheelBeforeMarching;

        /// <summary>
        /// Battle seconds per real second at <c>x1</c>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>[M128], and it was settled by playing rather than by argument.</b>
        /// The old x1 was one battle second per real second, and the remark that
        /// used to sit here argued from a single manoeuvre: a pike block needs
        /// nine seconds to wheel about, so anything quick loses the wheel. That
        /// is true of the wheel and wrong about the battle. A regiment walks at
        /// 1,59 m/s, so crossing four hundred metres of field is four minutes of
        /// watching nothing happen - and the designer played every session at
        /// x32 instead, which is a measurement and outranks the argument.
        /// </para>
        /// <para>
        /// So x1 <i>is</i> the old x32, and the slow end is still reachable: the
        /// band runs down to a thirty-second, which is the old x1 exactly.
        /// Nothing that could be watched before has stopped being watchable, and
        /// the wheel is still there at <c>x1/8</c> where it always was.
        /// </para>
        /// </remarks>
        public const float NormalBattleSecondsPerSecond = 32f;

        /// <summary>Slowest and fastest the clock may be set to, as multiples of x1.</summary>
        /// <remarks>
        /// The reachable band is deliberately the same one as before [M128] -
        /// one to sixty-four battle seconds per real second - so this is a
        /// relabelling of the ladder rather than an extension of it. Sixty-four
        /// is where it stops because that is a tick a frame at sixty frames, and
        /// past it the catch-up cap in <c>AdvanceClock</c> begins quietly eating
        /// the ticks it is being asked for.
        /// </remarks>
        public const float SlowestScale = 1f / 32f;

        /// <inheritdoc cref="SlowestScale"/>
        public const float FastestScale = 2f;

        /// <summary>How fast the clock runs, as a multiple of x1.</summary>
        public float TimeScale = 1f;

        /// <summary>Battle seconds per real second, as the clock actually reads it.</summary>
        public float BattleSecondsPerSecond => TimeScale * NormalBattleSecondsPerSecond;

        /// <summary>
        /// The speed as it is written on the screen.
        /// </summary>
        /// <remarks>
        /// The ladder is exact powers of two, so below x1 a fraction reads far
        /// better than the decimals that would otherwise print x1/32 as "x0.03",
        /// a number nobody can tell from nought at a glance.
        /// </remarks>
        public string SpeedLabel =>
            TimeScale >= 1f
                ? $"x{TimeScale:0.##}"
                : $"x1/{1f / TimeScale:0.##}";

        /// <summary>Whether the clock is running.</summary>
        /// <summary>
        /// Clicking a regiment draws a route rather than setting it walking,
        /// and nothing moves until the turn is ended.
        /// </summary>
        /// <remarks>
        /// [M143]. On by default, because this is the game rather than a debug
        /// aid - the harness toggles around it are for looking at the
        /// simulation, and this decides what the simulation is.
        /// </remarks>
        public bool PlanThenFire = true;

        /// <summary>Play the board game: one regiment to a hex ([M147]).</summary>
        /// <remarks>
        /// <para>
        /// Read once, at load, and never again - it decides which route planner
        /// the rules are given and whether the army is mustered onto hexes. The
        /// simulation itself has no idea which game it is running, which is the
        /// point: there is no rule with a board branch and a continuous branch
        /// to keep in step.
        /// </para>
        /// <para>
        /// Consequently it does nothing if flipped mid-battle. Changing games
        /// halfway is not a thing a game does.
        /// </para>
        /// </remarks>
        public bool GridMode = true;

        /// <summary>Battle seconds one board turn lasts.</summary>
        /// <remarks>
        /// The knob only. The value itself lives on
        /// <see cref="BattleChess.Rules.Grid.GridMode.TurnSeconds"/>, because how
        /// long a turn lasts is a rule of the board game rather than a debugging
        /// preference, and having two copies of it meant the rules once measured
        /// a sixty-second turn while the game ran a hundred and twenty.
        /// </remarks>
        public float GridTurnSeconds
        {
            get => BattleChess.Rules.Grid.GridMode.TurnSeconds;
            set => BattleChess.Rules.Grid.GridMode.TurnSeconds = value;
        }

        public bool Running = true;

        /// <summary>
        /// Route the way the AI will: minimising travel time rather than
        /// distance, so slow ground is worth going around.
        /// </summary>
        /// <remarks>
        /// Off by default, because this is not how a player's orders should
        /// behave — it second-guesses the line you drew and takes a longer way
        /// round when the going is better. Useful for seeing what the AI would
        /// choose, and for confirming terrain costs are doing their job.
        /// </remarks>
        public bool RouteLikeAi;

        /// <summary>
        /// Route as though the unit were as wide as it looks, rather than a
        /// point at its centre.
        /// </summary>
        /// <remarks>
        /// Off by default. Width-aware routing is correct but reads as broken:
        /// a 110 m line refuses to approach a shoreline or map edge it could
        /// obviously stand beside, and arcs around obstacles it could plainly
        /// walk past. The point model is the contract the game now assumes.
        /// </remarks>
        public bool RespectUnitWidth;

        /// <summary>Shade the raw route cells the search returned, before smoothing.</summary>
        public bool ShowSearchCells;

        /// <summary>Ring each unit at the clearance its routes must respect.</summary>
        public bool ShowClearance;

        /// <summary>Ring each unit at the distance it exerts control over.</summary>
        public bool ShowZoneOfControl;

        /// <summary>Outline the exact rectangle each unit occupies.</summary>
        /// <remarks>
        /// Off. It was the one piece of debug geometry left on by default, and
        /// the argument for it - that a rectangle you cannot identify is just a
        /// coloured smear - belongs to the nameplates, which are drawn by the
        /// game and are still there. This is the router's rectangle, which is a
        /// question about the router.
        /// </remarks>
        public bool ShowFootprintOutline;

        /// <summary>Draw the raw search cells of the route rather than the smoothed line.</summary>
        public bool ShowRawPath;

        /// <summary>
        /// Draws the regiment-sized hex grid the selected unit would be routed
        /// over, with the cells bodies are standing in picked out.
        /// </summary>
        public bool ShowRegimentGrid;

        /// <summary>
        /// Draws the rectangle each body actually reserves - itself grown by
        /// the mover's halo - which is the thing the grid marks cells against
        /// and is very much larger than the body drawn on screen.
        /// </summary>
        /// <remarks>
        /// Off, and it costs nothing either way - it is only ever drawn inside
        /// the regiment grid, which is itself off. On by default it was a
        /// setting that did nothing until you turned another one on, which is
        /// the worst kind: it cannot be judged from where it sits.
        /// </remarks>
        public bool ShowReservedAreas;

        /// <summary>
        /// Cell spacing as a multiple of the regiment's own bounding diameter.
        /// One means a cell holds exactly one regiment at any facing.
        /// </summary>
        public float RegimentGridSpacingMultiple = 1f;

        /// <summary>
        /// Right-click computes and draws a route instead of giving the order.
        /// </summary>
        /// <remarks>
        /// For working out why a planner chose what it chose, without spending a
        /// real order to find out — the regiment never moves, so the same ground
        /// can be tried at as many angles as it takes.
        /// </remarks>
        public bool PreviewRouteMode;


        /// <summary>
        /// Draw every planner in <c>RoutePlanners.All</c>, each in its own
        /// colour, rather than only the ladder and the search.
        /// </summary>
        /// <remarks>
        /// Off. It only fires in preview mode, so it cost nothing while that
        /// was off - but it means the first preview anybody takes silently runs
        /// four planners instead of the one the game uses, and reads as the
        /// game being slow rather than as four answers being drawn.
        /// </remarks>
        public bool PreviewEveryPlanner;

        /// <summary>
        /// Include the hybrid A* prototype in that. Off by default and worth
        /// leaving off: it is measured at a second and more for a single order,
        /// which a preview pays in one frame — the editor visibly stops.
        /// </summary>
        public bool PreviewTheHybrid;

        /// <summary>
        /// Draw the same comparison on a real order, not only on a previewed
        /// one — because "why did it go that way" is asked about marches that
        /// happened far more often than about ones being rehearsed.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Single orders only. Four extra plans is nothing for one regiment and
        /// forty times that for a wing.
        /// </para>
        /// <para>
        /// <b>Off by default, because it was not nothing.</b> Recorded in play,
        /// every frame over 90 ms that the game itself caused was a frame that
        /// planned five routes for one regiment - 547 ms at tick 651, of which
        /// 520 was planning. The comparison is a tool for reading a route, not
        /// a thing to leave switched on while playing, and leaving it on made
        /// the planner look like it cost four times what it costs.
        /// </para>
        /// </remarks>
        public bool ComparePlannersOnOrders;

        /// <summary>
        /// Work out a wing's routes all at once rather than one after another.
        /// </summary>
        /// <remarks>
        /// A plan reads the battle and writes nothing to it, so the regiments of
        /// a wing are independent questions and a click that asks eighty of them
        /// need not ask them in a queue. Measured on the bench fields: a hundred
        /// orders in 96, 95 and 36 ms against 305, 439 and 220 one at a time,
        /// and the routes come back identical to the decimal.
        /// <para>
        /// Here as a switch because it is the kind of change that is right until
        /// it is not: if a wing order ever produces a route a single order would
        /// not, this is the first thing to turn off, and the difference is then
        /// one toggle rather than one build.
        /// </para>
        /// </remarks>
        public bool PlanTheWingTogether = true;

        /// <summary>
        /// In preview mode, mark every place
        /// <see cref="BattleChess.Rules.RouteSearch"/> considered bending the
        /// route at — the corners and face projections its candidate generator
        /// built, not just the ones the winning route used.
        /// </summary>
        /// <remarks>
        /// Off. Same reason as the reserved areas: it is drawn only alongside a
        /// route preview, so on by default it was a tick that did nothing you
        /// could see until you turned something else on.
        /// </remarks>
        public bool ShowRouteCandidates;

        /// <summary>Whose eyes the field is shown through. -1 shows everything.</summary>
        /// <remarks>
        /// The debug view has to be able to cheat, or fog is impossible to tune:
        /// you cannot tell "correctly hidden" from "wrongly hidden" without
        /// being able to look at both pictures.
        /// </remarks>
        public int ViewingArmy = -1;

        /// <summary>Draw hidden enemies faintly rather than not at all.</summary>
        public bool GhostHiddenUnits;

        /// <summary>Ring each unit at how far it can see from where it stands.</summary>
        public bool ShowSightRange;

        /// <summary>Ring each shooter at the range its weapons reach.</summary>
        public bool ShowWeaponRange;

        /// <summary>Draw a line from each unit to every enemy it can personally see.</summary>
        /// <remarks>
        /// The one overlay that makes vision legible. A circle says how far a
        /// regiment <i>could</i> see; a line says what it actually has, which is
        /// the difference between a rule you can read and a rule you can debug.
        /// </remarks>
        public bool ShowSightLines;

        /// <summary>Print strength, morale and organization over each regiment.</summary>
        public bool ShowUnitLabels;

        // ---- Cheats -----------------------------------------------------------
        //
        // Applied by the harness after each tick rather than by any rule. A flag
        // the core respects is a flag that can be wrong in a shipped build, so
        // the core never learns these exist.

        /// <summary>Undo casualties every tick, so a fight runs forever.</summary>
        public bool NoCasualties;

        /// <summary>Hold every regiment steady, so nothing ever breaks or routs.</summary>
        public bool NoRouting;

        /// <summary>Clear reload timers every tick, so shooters fire continuously.</summary>
        public bool InstantReload;

        // ---- One-shot actions on the selected regiment -------------------------

        /// <summary>Set when the panel's button was pressed this frame.</summary>
        public bool BreakSelected;
        public bool RestoreSelected;
        public bool DestroySelected;

        /// <summary>
        /// Show the frame counter in the top bar.
        /// </summary>
        /// <remarks>
        /// On by default and in the always-visible bar, not behind F1. The
        /// harness exists to be watched, and a cost you only see when you go
        /// looking for it is a cost that gets found by somebody saying it felt
        /// slow.
        /// </remarks>
        public bool ShowFrameRate = true;

        /// <summary>
        /// Break the frame into sim, views, tracking, gui and the rest.
        /// </summary>
        /// <remarks>
        /// The rate on its own says a frame is dear and not one word about why,
        /// and the slow-frame line in the console that would say why only fires
        /// over 33 ms - so a steady 85 a second, which is a frame two thirds
        /// spent on something, reports nothing at all. This is that line, on
        /// screen, without the threshold.
        /// </remarks>
        public bool ExplainTheFrame;

        /// <summary>
        /// Whether the harness does anything at all beyond running the game.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>The master switch, and it exists because of a measurement.</b> A
        /// recording of thirty slow frames put the median at 92 ms and 262 at
        /// the worst, and <c>tracking</c> - the harness's own per-frame work -
        /// was 2 568 ms of the 3 930 spent. The simulation's share was a median
        /// of nought. What was slow was the debugger, not the game.
        /// </para>
        /// <para>
        /// So this turns that work off rather than merely hiding it: the
        /// overlay is not refreshed, the grid is not built, the heap is not
        /// asked how big it is, and the console keeps nothing. It is the only
        /// honest way to answer "how fast is the game" from inside a harness
        /// that is itself the most expensive thing on the frame.
        /// </para>
        /// <para>
        /// What deliberately stays on: the frame counter, because it is the
        /// instrument you are reading the answer off, and it costs a ring write
        /// a frame. And a recording already started keeps writing - stopping a
        /// file somebody explicitly asked for would be a trap.
        /// </para>
        /// </remarks>
        public bool Harness = true;

        /// <summary>Whether the options panel and console are drawn.</summary>
        /// <remarks>
        /// Starts hidden. The panel is fifty-odd GUILayout controls and the
        /// console re-formats every entry it holds, both of them twice a frame
        /// - measured as the whole of the <c>gui</c> column - and none of it is
        /// the game. The top bar says F1, which is the only thing you need to
        /// know to get it back.
        /// </remarks>
        public bool Visible;

        private Vector2 _scroll;

        /// <summary>
        /// Draws the panel and returns true if anything changed.
        /// </summary>
        /// <remarks>
        /// Inside a scroll view, and deliberately. The panel grows every time a
        /// system lands, and a fixed-height area silently clips whatever is at
        /// the bottom — which looks exactly like the feature having been
        /// removed. Scrolling makes it impossible for adding an option to hide
        /// an existing one.
        /// </remarks>
        public bool Draw(Rect area)
        {
            int before = Fingerprint();

            GUILayout.BeginArea(area, GUI.skin.box);
            GUILayout.Label("Debug options   (F1 hides)");

            _scroll = GUILayout.BeginScrollView(_scroll);

            // First, and on its own, because everything under it is a thing
            // this switch stops paying for.
            Harness = GUILayout.Toggle(Harness,
                Harness
                    ? " Harness ON — overlay, grid and console are being kept up (F2)"
                    : " Harness OFF — only the game is running (F2)");

            if (!Harness)
                GUILayout.Label("   Nothing below is being computed. The frame counter still is.");

            GUILayout.Space(6);

            GUILayout.Label(
                $"Clock   {SpeedLabel}   ({BattleSecondsPerSecond:0} battle s/s)   " +
                $"{(Running ? "running" : "PAUSED")}");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(Running ? "Pause" : "Run")) Running = !Running;
            if (GUILayout.Button("Slower")) TimeScale = Mathf.Max(SlowestScale, TimeScale * 0.5f);
            if (GUILayout.Button("Faster")) TimeScale = Mathf.Min(FastestScale, TimeScale * 2f);
            GUILayout.EndHorizontal();

            GUILayout.Space(6);
            GUILayout.Label("Orders");
            MoveInstantly = GUILayout.Toggle(MoveInstantly, " Move instantly (skip the march)");
            WheelBeforeMarching = GUILayout.Toggle(WheelBeforeMarching, " Wheel before marching");
            RouteLikeAi = GUILayout.Toggle(RouteLikeAi, " Route like the AI (fastest)");
            RespectUnitWidth = GUILayout.Toggle(RespectUnitWidth, " Route by unit width");

            GUILayout.Space(6);
            GUILayout.Label("Circles and outlines");
            ShowFootprintOutline = GUILayout.Toggle(ShowFootprintOutline, " Footprint outline");
            ShowZoneOfControl = GUILayout.Toggle(ShowZoneOfControl, " Zone of control");
            ShowSightRange = GUILayout.Toggle(ShowSightRange, " How far it can see");
            ShowWeaponRange = GUILayout.Toggle(ShowWeaponRange, " How far it can shoot");
            ShowClearance = GUILayout.Toggle(ShowClearance, " Width used for routing");

            GUILayout.Space(6);
            GUILayout.Label($"Route preview   {(PreviewRouteMode ? "ON — right-click plans, does not move" : "off")}");
            PreviewRouteMode = GUILayout.Toggle(PreviewRouteMode, " Preview route (no move)");
            PreviewEveryPlanner = GUILayout.Toggle(PreviewEveryPlanner, " Show every planner, one colour each");
            ComparePlannersOnOrders = GUILayout.Toggle(ComparePlannersOnOrders, " ...on real orders too, not just previews");
            PlanTheWingTogether = GUILayout.Toggle(PlanTheWingTogether, " Plan a wing's routes all at once");

            if (PreviewEveryPlanner)
                PreviewTheHybrid = GUILayout.Toggle(PreviewTheHybrid, " ...including hybrid A* (slow — freezes a frame)");
            ShowRouteCandidates = GUILayout.Toggle(ShowRouteCandidates, " Mark the search's candidate places");

            GUILayout.Space(6);
            GUILayout.Label("Cost");
            ShowFrameRate = GUILayout.Toggle(ShowFrameRate,
                $" Frame counter in the top bar (worst is over {FrameRate.SlowMs:0} ms)");

            if (ShowFrameRate)
                ExplainTheFrame = GUILayout.Toggle(ExplainTheFrame, " ...and where the frame goes");

            GUILayout.Space(6);
            GUILayout.Label("Lines and labels");
            ShowSightLines = GUILayout.Toggle(ShowSightLines, " Who is looking at whom");
            ShowUnitLabels = GUILayout.Toggle(ShowUnitLabels, " Strength / morale / order");
            ShowRawPath = GUILayout.Toggle(ShowRawPath, " Raw route line");
            ShowSearchCells = GUILayout.Toggle(ShowSearchCells, " Raw route cells");

            GUILayout.Space(6);
            GUILayout.Label("Regiment grid (M77)");
            ShowRegimentGrid = GUILayout.Toggle(ShowRegimentGrid, " Draw it for the selected regiment");
            GUILayout.Label($"   cell {RegimentGridSpacingMultiple:0.##} x the regiment");
            RegimentGridSpacingMultiple = GUILayout.HorizontalSlider(RegimentGridSpacingMultiple, 0.10f, 3f);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("0.1x")) RegimentGridSpacingMultiple = 0.10f;
            if (GUILayout.Button("0.25x")) RegimentGridSpacingMultiple = 0.25f;
            if (GUILayout.Button("0.5x")) RegimentGridSpacingMultiple = 0.5f;
            if (GUILayout.Button("1x")) RegimentGridSpacingMultiple = 1f;
            GUILayout.EndHorizontal();

            GUILayout.Label($"   halo {RegimentGrid.ClearanceFraction:0.00} x the regiment's radius");
            RegimentGrid.ClearanceFraction =
                GUILayout.HorizontalSlider(RegimentGrid.ClearanceFraction, 0f, 1f);

            GUILayout.Label($"   {RegimentGrid.SubSamples} sample(s) a cell, blocked once " +
                            $"{RegimentGrid.FillToBlock * 100f:0}% of them are inside a RESERVED");
            GUILayout.Label("   area (a body plus the mover's halo), not inside a body");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("1")) RegimentGrid.SubSamples = 1;
            if (GUILayout.Button("7")) RegimentGrid.SubSamples = 7;
            if (GUILayout.Button("19")) RegimentGrid.SubSamples = 19;
            GUILayout.EndHorizontal();
            RegimentGrid.FillToBlock = GUILayout.HorizontalSlider(RegimentGrid.FillToBlock, 0.1f, 1f);

            RegimentGrid.Reuse = GUILayout.Toggle(RegimentGrid.Reuse, " Keep the field between orders");
            ShowReservedAreas = GUILayout.Toggle(ShowReservedAreas, " Outline what each body reserves");

            GUILayout.Label($"   in the cascade: {GridRoutePlanner.Use}");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Off")) GridRoutePlanner.Use = GridUse.Off;
            if (GUILayout.Button("Stage")) GridRoutePlanner.Use = GridUse.Stage;
            if (GUILayout.Button("Tube")) GridRoutePlanner.Use = GridUse.Corridor;
            if (GUILayout.Button("Only")) GridRoutePlanner.Use = GridUse.Replace;
            GUILayout.EndHorizontal();

            GUILayout.Space(6);
            GUILayout.Label($"Fog   {(ViewingArmy < 0 ? "off — you see everything" : $"through army {ViewingArmy}'s eyes")}");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Both")) ViewingArmy = -1;
            if (GUILayout.Button("Army 0")) ViewingArmy = 0;
            if (GUILayout.Button("Army 1")) ViewingArmy = 1;
            GUILayout.EndHorizontal();
            GhostHiddenUnits = GUILayout.Toggle(GhostHiddenUnits, " Ghost what is hidden");

            GUILayout.Space(6);
            GUILayout.Label("Cheats");
            NoCasualties = GUILayout.Toggle(NoCasualties, " Nobody dies");
            NoRouting = GUILayout.Toggle(NoRouting, " Nobody breaks");
            InstantReload = GUILayout.Toggle(InstantReload, " Shoot every tick");

            GUILayout.Space(6);
            GUILayout.Label("Do this to the selected regiment");
            if (GUILayout.Button("Break it")) BreakSelected = true;
            if (GUILayout.Button("Restore it")) RestoreSelected = true;
            if (GUILayout.Button("Wipe it out")) DestroySelected = true;

            GUILayout.EndScrollView();
            GUILayout.EndArea();

            return Fingerprint() != before;
        }

        /// <summary>
        /// Cheap change detection, so a toggle can trigger a re-plan without
        /// every option needing its own comparison. One bit each, because
        /// combining them any other way lets two simultaneous changes cancel.
        /// </summary>
        private int Fingerprint() =>
            (MoveInstantly ? 1 : 0)
            | (RouteLikeAi ? 1 << 1 : 0)
            | (RespectUnitWidth ? 1 << 2 : 0)
            | (ShowSearchCells ? 1 << 3 : 0)
            | (ShowClearance ? 1 << 4 : 0)
            | (ShowZoneOfControl ? 1 << 5 : 0)
            | (ShowFootprintOutline ? 1 << 6 : 0)
            | (ShowRawPath ? 1 << 7 : 0)
            | (ShowRegimentGrid ? 1 << 20 : 0)
            | ((int)GridRoutePlanner.Use << 21)
            | (WheelBeforeMarching ? 1 << 8 : 0)
            | (GhostHiddenUnits ? 1 << 9 : 0)
            | (ShowSightRange ? 1 << 10 : 0)
            | (ShowWeaponRange ? 1 << 11 : 0)
            | (ShowSightLines ? 1 << 12 : 0)
            | (ShowUnitLabels ? 1 << 13 : 0)
            | (NoCasualties ? 1 << 14 : 0)
            | (NoRouting ? 1 << 15 : 0)
            | (InstantReload ? 1 << 16 : 0)
            | (PreviewRouteMode ? 1 << 24 : 0)
            | (PreviewEveryPlanner ? 1 << 27 : 0)
            | (PreviewTheHybrid ? 1 << 28 : 0)
            | (ComparePlannersOnOrders ? 1 << 29 : 0)
            | (PlanTheWingTogether ? 1 << 30 : 0)
            | (ShowRouteCandidates ? 1 << 26 : 0)
            | ((ViewingArmy + 2) << 17);
    }
}
