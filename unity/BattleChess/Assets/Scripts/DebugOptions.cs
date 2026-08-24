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
        /// Battle seconds per real second while running.
        /// </summary>
        /// <remarks>
        /// Deliberately modest. Manoeuvres worth watching happen over seconds of
        /// battle time — a pike block needs nine of them to wheel about — and at
        /// x8 that is over before the eye registers it as a turn at all.
        /// </remarks>
        public float TimeScale = 3f;

        /// <summary>Whether the clock is running.</summary>
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
        public bool ShowFootprintOutline = true;

        /// <summary>Draw the raw search cells of the route rather than the smoothed line.</summary>
        public bool ShowRawPath;

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
        public bool PreviewEveryPlanner = true;

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
        /// Single orders only. Four extra plans is nothing for one regiment and
        /// forty times that for a wing.
        /// </remarks>
        public bool ComparePlannersOnOrders = true;

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
        public bool ShowRouteCandidates = true;

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

        public bool Visible = true;

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

            GUILayout.Label($"Clock   x{TimeScale:0}   {(Running ? "running" : "PAUSED")}");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(Running ? "Pause" : "Run")) Running = !Running;
            if (GUILayout.Button("Slower")) TimeScale = Mathf.Max(1f, TimeScale * 0.5f);
            if (GUILayout.Button("Faster")) TimeScale = Mathf.Min(64f, TimeScale * 2f);
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
            GUILayout.Label("Lines and labels");
            ShowSightLines = GUILayout.Toggle(ShowSightLines, " Who is looking at whom");
            ShowUnitLabels = GUILayout.Toggle(ShowUnitLabels, " Strength / morale / order");
            ShowRawPath = GUILayout.Toggle(ShowRawPath, " Raw route line");
            ShowSearchCells = GUILayout.Toggle(ShowSearchCells, " Raw route cells");

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
