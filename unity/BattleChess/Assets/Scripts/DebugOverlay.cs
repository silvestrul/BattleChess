using System.Collections.Generic;
using BattleChess.Contracts;
using UnityEngine;

namespace BattleChess.Unity
{
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
        private static readonly Color ClearanceColour = new Color(0.4f, 1f, 0.6f, 0.5f);
        private static readonly Color ZocColour = new Color(1f, 0.4f, 0.4f, 0.35f);
        private static readonly Color SightColour = new Color(0.9f, 0.9f, 0.4f, 0.22f);
        private static readonly Color SearchColour = new Color(0.5f, 0.6f, 1f, 0.20f);
        private static readonly Color RawPathColour = new Color(1f, 0.5f, 0.9f, 0.9f);

        private Material _material;

        private readonly List<(OrientedRect Shape, float Clearance, float Zoc, float Sight)> _units =
            new List<(OrientedRect, float, float, float)>();

        private readonly List<Vector3> _searchCells = new List<Vector3>();
        private readonly List<Vector3> _rawPath = new List<Vector3>();
        private float _searchCellSize = 5f;

        public DebugOptions Options;

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

        public void SetUnits(IEnumerable<(OrientedRect Shape, float Clearance, float Zoc, float Sight)> units)
        {
            _units.Clear();
            _units.AddRange(units);
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

        private void OnRenderObject()
        {
            if (Options == null || !Options.Visible) return;

            _material.SetPass(0);
            GL.PushMatrix();
            GL.MultMatrix(Matrix4x4.identity);

            if (Options.ShowSearchCells && _searchCells.Count > 0)
                DrawSearchCells();

            GL.Begin(GL.LINES);

            if (Options.ShowFootprintOutline)
            {
                foreach ((OrientedRect shape, _, _, _) in _units)
                    DrawRect(shape, FootprintColour);
            }

            if (Options.ShowClearance)
            {
                foreach ((OrientedRect shape, float clearance, _, _) in _units)
                    DrawCircle(shape.Centre, clearance, ClearanceColour);
            }

            if (Options.ShowZoneOfControl)
            {
                foreach ((OrientedRect shape, _, float zoc, _) in _units)
                    DrawCircle(shape.Centre, zoc, ZocColour);
            }

            // Drawn from where the unit actually stands, so stepping a regiment
            // onto a hill visibly widens its circle. That is the whole argument
            // for taking one.
            if (Options.ShowSightRange)
            {
                foreach ((OrientedRect shape, _, _, float sight) in _units)
                    DrawCircle(shape.Centre, sight, SightColour);
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

            GL.End();
            GL.PopMatrix();
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

        private static void DrawRect(in OrientedRect rect, Color colour)
        {
            GL.Color(colour);

            Vec2[] corners = rect.GetCorners();

            for (int i = 0; i < corners.Length; i++)
            {
                Vec2 a = corners[i];
                Vec2 b = corners[(i + 1) % corners.Length];

                GL.Vertex(new Vector3(a.X, a.Y, 0f));
                GL.Vertex(new Vector3(b.X, b.Y, 0f));
            }

            // A spur out of the front face, so facing is unambiguous even when
            // a unit is nearly square.
            Vec2 nose = rect.Centre + rect.Forward * (rect.Footprint.HalfDepth + 12f);
            Vec2 chin = rect.Centre + rect.Forward * rect.Footprint.HalfDepth;

            GL.Vertex(new Vector3(chin.X, chin.Y, 0f));
            GL.Vertex(new Vector3(nose.X, nose.Y, 0f));
        }

        private static void DrawCircle(Vec2 centre, float radius, Color colour, int segments = 40)
        {
            if (radius <= 0f) return;

            GL.Color(colour);

            for (int i = 0; i < segments; i++)
            {
                float a = i / (float)segments * Mathf.PI * 2f;
                float b = (i + 1) / (float)segments * Mathf.PI * 2f;

                GL.Vertex(new Vector3(centre.X + Mathf.Cos(a) * radius, centre.Y + Mathf.Sin(a) * radius, 0f));
                GL.Vertex(new Vector3(centre.X + Mathf.Cos(b) * radius, centre.Y + Mathf.Sin(b) * radius, 0f));
            }
        }
    }
}
