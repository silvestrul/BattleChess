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

        public bool Visible = true;

        /// <summary>Draws the panel and returns true if anything changed.</summary>
        public bool Draw(Rect area)
        {
            int before = Fingerprint();

            GUILayout.BeginArea(area, GUI.skin.box);
            GUILayout.Label("Debug options   (F1 hides)");
            GUILayout.Space(4);

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
            GUILayout.Label("Overlays");
            ShowFootprintOutline = GUILayout.Toggle(ShowFootprintOutline, " Footprint outline");
            ShowSearchCells = GUILayout.Toggle(ShowSearchCells, " Raw route cells");
            ShowRawPath = GUILayout.Toggle(ShowRawPath, " Raw route line");
            ShowClearance = GUILayout.Toggle(ShowClearance, " Width used for routing");
            ShowZoneOfControl = GUILayout.Toggle(ShowZoneOfControl, " Zone of control");
            ShowSightRange = GUILayout.Toggle(ShowSightRange, " How far each unit sees");

            GUILayout.Space(6);
            GUILayout.Label($"Fog   {(ViewingArmy < 0 ? "off — you see everything" : $"through army {ViewingArmy}'s eyes")}");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Both")) ViewingArmy = -1;
            if (GUILayout.Button("Army 0")) ViewingArmy = 0;
            if (GUILayout.Button("Army 1")) ViewingArmy = 1;
            GUILayout.EndHorizontal();
            GhostHiddenUnits = GUILayout.Toggle(GhostHiddenUnits, " Ghost what is hidden");

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
            | ((ViewingArmy + 2) << 11);
    }
}
