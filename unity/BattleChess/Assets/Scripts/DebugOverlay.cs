using System.Collections.Generic;
using BattleChess.Contracts;
using BattleChess.Rules.GridPlanning;
using UnityEngine;

namespace BattleChess.Unity
{
    /// <summary>
    /// One regiment's worth of debug geometry.
    /// </summary>
    /// <remarks>
    /// A named struct rather than a tuple because this list gains a field every
    /// time a system lands, and a five-element tuple is unreadable at the call
    /// site and silently reorderable at the definition.
    /// </remarks>
    public struct OverlayUnit
    {
        public OrientedRect Shape;

        /// <summary>Room the router insists on to either side, or zero if not enforced.</summary>
        public float Clearance;

        /// <summary>How far out this regiment halts an enemy advance.</summary>
        public float Zoc;

        /// <summary>How far it can see from where it currently stands.</summary>
        public float Sight;

        /// <summary>How far its weapons reach, or zero for melee troops.</summary>
        public float WeaponRange;
    }

    /// <summary>
    /// Draws debug geometry — footprint outlines, clearance and control radii,
    /// the area a route search covered.
    /// </summary>
    /// <remarks>
    /// Immediate-mode GL rather than <c>Debug.DrawLine</c>, which only appears in
    /// the Scene view. These need to be visible while playing, since that is
    /// when the questions come up.
    /// </remarks>
    public sealed class DebugOverlay : MonoBehaviour
    {
        private static readonly Color FootprintColour = new Color(1f, 1f, 1f, 0.55f);
        private static readonly Color ClearanceColour = new Color(0.4f, 1f, 0.6f, 0.75f);
        private static readonly Color ZocColour = new Color(1f, 0.35f, 0.35f, 0.65f);
        private static readonly Color SightColour = new Color(0.95f, 0.95f, 0.35f, 0.55f);
        private static readonly Color WeaponColour = new Color(1f, 0.5f, 0.15f, 0.70f);
        private static readonly Color SightLineColour = new Color(0.95f, 0.95f, 0.6f, 0.35f);
        private static readonly Color SearchColour = new Color(0.5f, 0.6f, 1f, 0.20f);
        private static readonly Color RawPathColour = new Color(1f, 0.5f, 0.9f, 0.9f);

        /// <summary>Every place <see cref="BattleChess.Rules.RouteSearch"/> considered bending at.</summary>
        private static readonly Color CandidateColour = new Color(0.55f, 0.85f, 1f, 0.9f);

        /// <summary>
        /// One colour per planner, in the order <c>RoutePlanners.All</c> gives
        /// them, so a preview showing five routes can be read without a legend
        /// once you have seen it twice.
        /// </summary>
        /// <remarks>
        /// The first two keep the colours the two-planner preview always used —
        /// orange is the ladder and green is the search — because those are the
        /// ones already in everybody's head and in every screenshot taken so far.
        /// </remarks>
        public static readonly Color[] PlannerColours =
        {
            new Color(1f,    0.70f, 0.25f, 0.95f),   // the ladder
            new Color(0.40f, 1f,    0.55f, 0.95f),   // over places and fronts
            new Color(0.30f, 0.85f, 0.95f, 0.95f),   // over corners
            new Color(1f,    0.90f, 0.30f, 0.95f),   // over tangents — the default
            new Color(0.72f, 0.60f, 1f,    0.95f),   // hybrid A*
        };

        private Material _material;

        private readonly List<OverlayUnit> _units = new List<OverlayUnit>();
        private readonly List<(Vector3 From, Vector3 To)> _sightLines = new List<(Vector3, Vector3)>();

        private readonly List<Vector3> _searchCells = new List<Vector3>();
        private readonly List<Vector3> _rawPath = new List<Vector3>();
        private float _searchCellSize = 5f;

        /// <summary>
        /// The regiment grid: one entry per cell, already turned into the six
        /// corners it is drawn as.
        /// </summary>
        /// <remarks>
        /// Corners rather than centres, unlike the search cells above, because
        /// the whole question this overlay exists to answer is whether the
        /// blocked cells line up with the bodies that blocked them - and a
        /// square drawn at a hex centre cannot answer that. Built once when the
        /// selection changes rather than every frame; at 2 700 cells this is a
        /// few thousand vertices, which is nothing to draw and quite a lot to
        /// recompute sixty times a second.
        /// </remarks>
        private readonly List<GridCell> _gridCells = new List<GridCell>();

        private readonly List<Vector3> _gridRoute = new List<Vector3>();

        /// <summary>What each body reserves against the selected mover.</summary>
        private readonly List<OrientedRect> _reserved = new List<OrientedRect>();

        private struct GridCell
        {
            public Vector3[] Corners;
            public Color Fill;
        }

        /// <summary>Clear ground, faint enough to read the field through.</summary>
        private static readonly Color GridClearColour = new Color(0.35f, 0.75f, 0.95f, 0.10f);

        /// <summary>A cell a body is standing in, or close enough to block.</summary>
        private static readonly Color GridBodyColour = new Color(1f, 0.30f, 0.35f, 0.34f);

        /// <summary>Ground this mover cannot cross at all.</summary>
        private static readonly Color GridGroundColour = new Color(0.55f, 0.40f, 0.20f, 0.30f);

        /// <summary>
        /// Clipped by a body but still passable - the cells the sampling exists
        /// to find, and the ones a route is entered at off-centre.
        /// </summary>
        private static readonly Color GridPartialColour = new Color(1f, 0.80f, 0.25f, 0.26f);

        /// <summary>The line A* found over the cells.</summary>
        private static readonly Color GridRouteColour = new Color(0.30f, 1f, 0.85f, 1f);

        private readonly List<Vector3> _routeCandidates = new List<Vector3>();

        /// <summary>
        /// One drawn route per planner asked. Kept as a list rather than a field
        /// each because the number of planners is <c>RoutePlanners.All.Count</c>
        /// and not a number this file should know.
        /// </summary>
        private readonly List<RoutePreview> _previews = new List<RoutePreview>();

        private struct RoutePreview
        {
            public List<Vector3> Points;
            public Color Colour;

            /// <summary>
            /// How far off the true line to draw it. Planners agree far more
            /// often than they differ, and five identical lines drawn on top of
            /// one another read as one — so each is nudged sideways and the
            /// places they genuinely part company are the places the lines
            /// visibly diverge by more than their spacing.
            /// </summary>
            public float Lift;
        }

        /// <summary>
        /// Wired up by <see cref="BattlefieldController"/> at startup, never by
        /// the inspector — the controller owns the one instance and the overlay
        /// reads it. Marked so explicitly because Unity would otherwise try to
        /// serialize a public field on a component, find no [Serializable] on
        /// the type, and skip it (UAC1001): a warning about a contract this
        /// field was never trying to keep.
        /// </summary>
        [System.NonSerialized] public DebugOptions Options;

        private void Awake()
        {
            // Unity's built-in vertex-coloured shader, always present.
            _material = new Material(Shader.Find("Hidden/Internal-Colored"))
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            _material.SetInt("_ZWrite", 0);
            _material.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
        }

        public void SetUnits(IEnumerable<OverlayUnit> units)
        {
            _units.Clear();
            _units.AddRange(units);
        }

        public void SetSightLines(IEnumerable<(Vector3 From, Vector3 To)> lines)
        {
            _sightLines.Clear();
            _sightLines.AddRange(lines);
        }

        public void SetSearchCells(IReadOnlyList<Coord> cells, HexLayout layout)
        {
            _searchCells.Clear();
            _searchCellSize = layout.NeighbourDistance * 0.5f;

            if (cells == null) return;

            for (int i = 0; i < cells.Count; i++)
            {
                Vec2 world = layout.ToWorld(cells[i]);
                _searchCells.Add(new Vector3(world.X, world.Y, 0f));
            }
        }

        /// <summary>
        /// Hands over a whole grid to draw: the cells, what each one is, and
        /// the layout that turns them into ground.
        /// </summary>
        public void SetRegimentGrid(
            IReadOnlyList<Coord> cells, IReadOnlyList<CellState> states, HexLayout layout)
        {
            _gridCells.Clear();

            if (cells == null || states == null) return;

            var corners = new Vec2[6];

            for (int i = 0; i < cells.Count && i < states.Count; i++)
            {
                layout.GetCorners(cells[i], corners);

                var drawn = new Vector3[6];
                for (int c = 0; c < 6; c++) drawn[c] = new Vector3(corners[c].X, corners[c].Y, 0f);

                Color fill;
                switch (states[i])
                {
                    case CellState.Body:    fill = GridBodyColour;    break;
                    case CellState.Ground:  fill = GridGroundColour;  break;
                    case CellState.Partial: fill = GridPartialColour; break;
                    default:                fill = GridClearColour;   break;
                }

                _gridCells.Add(new GridCell { Corners = drawn, Fill = fill });
            }
        }

        /// <summary>
        /// The rectangles the grid really marks against: each body grown by the
        /// mover's halo.
        /// </summary>
        public void SetReservedAreas(IReadOnlyList<OrientedRect> areas)
        {
            _reserved.Clear();
            if (areas != null) _reserved.AddRange(areas);
        }

        /// <summary>The route the grid search found, or nothing if it found none.</summary>
        public void SetGridRoute(IReadOnlyList<Vec2> points)
        {
            _gridRoute.Clear();

            if (points == null) return;

            foreach (Vec2 point in points) _gridRoute.Add(new Vector3(point.X, point.Y, 0f));
        }

        public void SetRawPath(IReadOnlyList<Coord> cells, HexLayout layout)
        {
            _rawPath.Clear();
            if (cells == null) return;

            for (int i = 0; i < cells.Count; i++)
            {
                Vec2 world = layout.ToWorld(cells[i]);
                _rawPath.Add(new Vector3(world.X, world.Y, 0f));
            }
        }

        /// <summary>Clears every route-preview drawing at once — a fresh click starts clean.</summary>
        public void ClearRoutePreview()
        {
            _routeCandidates.Clear();
            _previews.Clear();
        }

        /// <summary>
        /// Every place <see cref="BattleChess.Rules.RouteSearch"/> would consider
        /// bending the route at, for the arrangement just previewed.
        /// </summary>
        public void SetRouteCandidates(IReadOnlyList<Vec2> places)
        {
            _routeCandidates.Clear();
            if (places == null) return;

            foreach (Vec2 place in places)
                _routeCandidates.Add(new Vector3(place.X, place.Y, 0f));
        }

        /// <summary>
        /// Adds one planner's answer to the drawing. Call
        /// <see cref="ClearRoutePreview"/> first; these accumulate.
        /// </summary>
        /// <param name="which">
        /// Index into <c>RoutePlanners.All</c>, which picks both the colour and
        /// how far the line is nudged off the others.
        /// </param>
        public void AddRoutePreview(int which, IReadOnlyList<Vec2> waypoints)
        {
            if (waypoints == null || waypoints.Count < 2) return;

            var points = new List<Vector3>();
            Fill(points, waypoints);

            _previews.Add(new RoutePreview
            {
                Points = points,
                Colour = PlannerColours[which % PlannerColours.Length],

                // Spread either side of the true line rather than all one way,
                // so no planner's route is systematically the outer one.
                Lift = (which - (PlannerColours.Length - 1) * 0.5f) * 1.6f,
            });
        }

        private static void Fill(List<Vector3> into, IReadOnlyList<Vec2> waypoints)
        {
            into.Clear();
            if (waypoints == null) return;

            foreach (Vec2 point in waypoints)
                into.Add(new Vector3(point.X, point.Y, 0f));
        }

        private void OnRenderObject()
        {
            if (Options == null || !Options.Visible) return;

            _material.SetPass(0);
            GL.PushMatrix();
            GL.MultMatrix(Matrix4x4.identity);

            // Under everything else, because it is a ground covering and not
            // a marking: the bodies and their routes have to stay readable on
            // top of it.
            if (Options.ShowRegimentGrid && _gridCells.Count > 0)
                DrawRegimentGrid();

            if (Options.ShowSearchCells && _searchCells.Count > 0)
                DrawSearchCells();

            GL.Begin(GL.LINES);

            if (Options.ShowFootprintOutline)
            {
                foreach (OverlayUnit unit in _units)
                    DrawRect(unit.Shape, FootprintColour);
            }

            if (Options.ShowClearance)
            {
                foreach (OverlayUnit unit in _units)
                    DrawCircle(unit.Shape.Centre, unit.Clearance, ClearanceColour, BandFor(unit.Clearance));
            }

            // A belt around the formation, not a circle around its centre —
            // because that is what the rule now measures. Drawn as a ring meant
            // the overlay disagreed with the game in the worst possible way: a
            // ninety-seven metre line of swordsmen showed a thirty metre bubble,
            // so an enemy marching through the end of the line looked like it
            // had every right to. The picture you tune by has to be the picture
            // the rules use.
            if (Options.ShowZoneOfControl)
            {
                foreach (OverlayUnit unit in _units)
                    DrawRect(Grown(unit.Shape, unit.Zoc), ZocColour);
            }

            // Drawn from where the unit actually stands, so stepping a regiment
            // onto a hill visibly widens its circle. That is the whole argument
            // for taking one.
            if (Options.ShowSightRange)
            {
                foreach (OverlayUnit unit in _units)
                    DrawCircle(unit.Shape.Centre, unit.Sight, SightColour, BandFor(unit.Sight));
            }

            if (Options.ShowWeaponRange)
            {
                foreach (OverlayUnit unit in _units)
                    DrawCircle(unit.Shape.Centre, unit.WeaponRange, WeaponColour, BandFor(unit.WeaponRange));
            }

            // What each regiment has actually spotted, as against what it could
            // in principle see. A circle states the rule; these state the answer.
            if (Options.ShowSightLines)
            {
                GL.Color(SightLineColour);

                foreach ((Vector3 from, Vector3 to) in _sightLines)
                {
                    GL.Vertex(from);
                    GL.Vertex(to);
                }
            }

            if (Options.ShowRawPath && _rawPath.Count > 1)
            {
                GL.Color(RawPathColour);
                for (int i = 1; i < _rawPath.Count; i++)
                {
                    GL.Vertex(_rawPath[i - 1]);
                    GL.Vertex(_rawPath[i]);
                }
            }

            // Two planners' answers to the same ground, drawn as separate lines
            // so the disagreement itself is the picture — a route only one of
            // them would take is exactly the case worth clicking again to see.
            // Drawn last-first, so the default planner and the ladder sit over
            // the ones that are only there for comparison.
            for (int i = _previews.Count - 1; i >= 0; i--)
                DrawPolyline(_previews[i].Points, _previews[i].Colour, _previews[i].Lift);

            GL.End();

            if (Options.ShowRouteCandidates && _routeCandidates.Count > 0)
                DrawCandidateMarks();

            GL.PopMatrix();
        }

        private static void DrawPolyline(List<Vector3> points, Color colour, float lift)
        {
            if (points.Count < 2) return;

            GL.Color(colour);
            var offset = new Vector3(0f, 0f, lift * 0.01f);

            for (int i = 1; i < points.Count; i++)
            {
                GL.Vertex(points[i - 1] + offset);
                GL.Vertex(points[i] + offset);
            }
        }

        /// <summary>How wide the cross drawn at each candidate place is, in metres.</summary>
        private const float CandidateMarkMetres = 4f;

        private void DrawCandidateMarks()
        {
            GL.Begin(GL.LINES);
            GL.Color(CandidateColour);

            foreach (Vector3 at in _routeCandidates)
            {
                GL.Vertex(at + new Vector3(-CandidateMarkMetres, 0f, 0f));
                GL.Vertex(at + new Vector3(CandidateMarkMetres, 0f, 0f));
                GL.Vertex(at + new Vector3(0f, -CandidateMarkMetres, 0f));
                GL.Vertex(at + new Vector3(0f, CandidateMarkMetres, 0f));
            }

            GL.End();
        }

        /// <summary>
        /// Fills every cell as a hexagonal fan and then outlines the blocked
        /// ones, so the shape of what is in the way reads at a glance.
        /// </summary>
        private void DrawRegimentGrid()
        {
            GL.Begin(GL.TRIANGLES);

            foreach (GridCell cell in _gridCells)
            {
                GL.Color(cell.Fill);

                // A hexagon as four triangles round corner 0. Cheaper than a
                // fan through a centre vertex and there is no centre to hand.
                for (int i = 1; i < 5; i++)
                {
                    GL.Vertex(cell.Corners[0]);
                    GL.Vertex(cell.Corners[i]);
                    GL.Vertex(cell.Corners[i + 1]);
                }
            }

            GL.End();

            GL.Begin(GL.LINES);

            foreach (GridCell cell in _gridCells)
            {
                GL.Color(new Color(cell.Fill.r, cell.Fill.g, cell.Fill.b, cell.Fill.a * 1.6f));

                for (int i = 0; i < 6; i++)
                {
                    GL.Vertex(cell.Corners[i]);
                    GL.Vertex(cell.Corners[(i + 1) % 6]);
                }
            }

            if (Options.ShowReservedAreas)
            {
                GL.Color(new Color(1f, 0.45f, 0.30f, 0.75f));

                foreach (OrientedRect area in _reserved) DrawRectNow(area);
            }

            if (_gridRoute.Count > 1)
            {
                GL.Color(GridRouteColour);

                for (int i = 1; i < _gridRoute.Count; i++)
                {
                    GL.Vertex(_gridRoute[i - 1]);
                    GL.Vertex(_gridRoute[i]);
                }
            }

            GL.End();
        }

        /// <summary>
        /// A rectangle drawn into a GL.LINES block already open, with the
        /// colour already set.
        /// </summary>
        private static void DrawRectNow(in OrientedRect rect)
        {
            Vec2[] corners = rect.GetCorners();

            for (int i = 0; i < corners.Length; i++)
            {
                Vec2 a = corners[i];
                Vec2 b = corners[(i + 1) % corners.Length];

                GL.Vertex(new Vector3(a.X, a.Y, 0f));
                GL.Vertex(new Vector3(b.X, b.Y, 0f));
            }
        }

        private void DrawSearchCells()
        {
            GL.Begin(GL.QUADS);
            GL.Color(SearchColour);

            float half = _searchCellSize;

            foreach (Vector3 centre in _searchCells)
            {
                GL.Vertex(new Vector3(centre.x - half, centre.y - half, 0f));
                GL.Vertex(new Vector3(centre.x + half, centre.y - half, 0f));
                GL.Vertex(new Vector3(centre.x + half, centre.y + half, 0f));
                GL.Vertex(new Vector3(centre.x - half, centre.y + half, 0f));
            }

            GL.End();
        }

        /// <summary>
        /// The same formation with a margin of <paramref name="metres"/> added
        /// on every side.
        /// </summary>
        /// <remarks>
        /// Squared off rather than rounded at the corners, which matches how
        /// the rules measure it: the gap between two formations is the widest
        /// separation over the four candidate axes, so a corner counts as
        /// slightly nearer than true straight-line distance would make it.
        /// </remarks>
        private static OrientedRect Grown(in OrientedRect rect, float metres) =>
            new OrientedRect(
                rect.Centre,
                rect.Facing,
                new Footprint(rect.Footprint.Width + 2f * metres, rect.Footprint.Depth + 2f * metres));

        private static void DrawRect(in OrientedRect rect, Color colour)
        {
            GL.Color(colour);

            Vec2[] corners = rect.GetCorners();

            // Three passes a metre apart, for the same reason the circles are
            // banded: one pixel is invisible at map scale.
            for (int pass = -1; pass <= 1; pass++)
            for (int i = 0; i < corners.Length; i++)
            {
                Vec2 a = corners[i];
                Vec2 b = corners[(i + 1) % corners.Length];
                var lift = new Vector3(0f, pass * 1.2f, 0f);

                GL.Vertex(new Vector3(a.X, a.Y, 0f) + lift);
                GL.Vertex(new Vector3(b.X, b.Y, 0f) + lift);
            }

            // A spur out of the front face, so facing is unambiguous even when
            // a unit is nearly square.
            Vec2 nose = rect.Centre + rect.Forward * (rect.Footprint.HalfDepth + 12f);
            Vec2 chin = rect.Centre + rect.Forward * rect.Footprint.HalfDepth;

            GL.Vertex(new Vector3(chin.X, chin.Y, 0f));
            GL.Vertex(new Vector3(nose.X, nose.Y, 0f));
        }

        /// <summary>
        /// How thick to draw a ring of a given radius.
        /// </summary>
        /// <remarks>
        /// Proportional, with a floor and a ceiling. A fixed width that reads
        /// well on a 450 m artillery circle is a quarter of the radius on a
        /// 30 m zone of control, and swallows it.
        /// </remarks>
        private static float BandFor(float radius) => Mathf.Clamp(radius * 0.035f, 2.5f, 12f);

        /// <summary>Metres between the concentric rings that make up one band.</summary>
        private const float RingSpacing = 1.5f;

        /// <summary>How wide a range band is drawn, in metres.</summary>
        private const float BandWidth = 9f;

        /// <summary>
        /// Draws a circle as a band rather than a hairline.
        /// </summary>
        /// <remarks>
        /// A GL line is one pixel however far away the camera is, and these
        /// circles are drawn on a map two kilometres across — so a sight radius
        /// of 260 m came out as a faint thread you had to hunt for. Thickness
        /// is concentric rings a metre and a half apart, which reads as a band
        /// at any zoom without needing a line shader.
        ///
        /// Segments went up with it: a 450 m artillery circle drawn in forty
        /// steps is visibly a polygon.
        /// </remarks>
        private static void DrawCircle(Vec2 centre, float radius, Color colour, float band = BandWidth, int segments = 72)
        {
            if (radius <= 0f) return;

            GL.Color(colour);

            int rings = Mathf.Max(1, Mathf.RoundToInt(band / RingSpacing));
            float innermost = radius - band * 0.5f;

            for (int ring = 0; ring < rings; ring++)
            {
                float r = innermost + ring * RingSpacing;
                if (r <= 0f) continue;

                for (int i = 0; i < segments; i++)
                {
                    float a = i / (float)segments * Mathf.PI * 2f;
                    float b = (i + 1) / (float)segments * Mathf.PI * 2f;

                    GL.Vertex(new Vector3(centre.X + Mathf.Cos(a) * r, centre.Y + Mathf.Sin(a) * r, 0f));
                    GL.Vertex(new Vector3(centre.X + Mathf.Cos(b) * r, centre.Y + Mathf.Sin(b) * r, 0f));
                }
            }
        }
    }
}
