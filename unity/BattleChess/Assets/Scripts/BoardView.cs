using System.Collections.Generic;
using BattleChess.Contracts;
using BattleChess.Rules.Grid;
using UnityEngine;

namespace BattleChess.Unity
{
    /// <summary>
    /// Draws the hex board as a single wireframe mesh.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>[M147].</b> A board game whose board cannot be seen is not a board
    /// game - a player has to be able to count hexes to a target before
    /// committing a move, which is most of what makes the mode worth trying at
    /// all.
    /// </para>
    /// <para>
    /// <b>One mesh, built once, of line topology.</b> A <c>LineRenderer</c> per
    /// cell would be several hundred objects redrawn every frame for a thing
    /// that never changes. Edges are shared between neighbours and drawn once,
    /// keyed by their rounded endpoints, which very nearly halves the segment
    /// count.
    /// </para>
    /// <para>
    /// <b>It asks the lattice for corners [M151]</b> rather than knowing what
    /// shape a cell is, so the same mesh builder draws a hex board and a square
    /// one and there is no second drawing path to keep in step with the first.
    /// </para>
    /// <para>
    /// Faint on purpose, and under the units. The board is a reference the eye
    /// should be able to ignore while looking at the battle; a crisp grid over
    /// the top competes with the regiments for attention and wins.
    /// </para>
    /// </remarks>
    public static class BoardView
    {
        /// <summary>How strongly the grid is drawn over the ground.</summary>
        private static readonly Color LineColour = new Color(1f, 1f, 1f, 0.16f);

        /// <summary>Metres a corner is rounded to when deciding two edges are the same.</summary>
        /// <remarks>
        /// A tenth of a metre against fifty-metre hexes. Corners of neighbouring
        /// hexes are the same point up to floating-point noise, so they have to
        /// be compared with a tolerance or every interior edge is drawn twice.
        /// </remarks>
        private const float SameCornerWithin = 0.1f;

        public static GameObject Build(Board board, Transform parent)
        {
            var vertices = new List<Vector3>();
            var indices = new List<int>();
            var drawn = new HashSet<long>();

            var corners = new Vec2[board.Cells.CornerCount];

            // The board is walked in world space rather than in cell space, so
            // the sweep covers the map whatever the origin happens to be. The
            // margin either way is what stops the board stopping short of the
            // map edge, and it is generous because a hex lattice's coordinates
            // are skewed and a plain rectangle of them does not cover a
            // rectangle of ground.
            Coord min = board.Of(new Vec2(board.Bounds.Min.X, board.Bounds.Min.Y));
            Coord max = board.Of(new Vec2(board.Bounds.Max.X, board.Bounds.Max.Y));

            int lowQ = Mathf.Min(min.Q, max.Q) - 3;
            int highQ = Mathf.Max(min.Q, max.Q) + 3;
            int lowR = Mathf.Min(min.R, max.R) - 3;
            int highR = Mathf.Max(min.R, max.R) + 3;

            for (int q = lowQ; q <= highQ; q++)
            for (int r = lowR; r <= highR; r++)
            {
                var hex = new Coord(q, r);

                if (!board.OnBoard(hex)) continue;

                board.Cells.CornersOf(hex, corners);

                for (int i = 0; i < corners.Length; i++)
                {
                    Vec2 a = corners[i];
                    Vec2 b = corners[(i + 1) % corners.Length];

                    if (!drawn.Add(EdgeKey(a, b))) continue;

                    indices.Add(vertices.Count);
                    vertices.Add(new Vector3(a.X, a.Y, -0.5f));

                    indices.Add(vertices.Count);
                    vertices.Add(new Vector3(b.X, b.Y, -0.5f));
                }
            }

            var mesh = new Mesh { name = "Board" };

            // A full board is well past the sixteen-bit vertex limit.
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(vertices);
            mesh.SetIndices(indices, MeshTopology.Lines, submesh: 0);
            mesh.RecalculateBounds();

            var go = new GameObject("Board");
            go.transform.SetParent(parent, worldPositionStays: false);

            go.AddComponent<MeshFilter>().sharedMesh = mesh;

            var renderer = go.AddComponent<MeshRenderer>();
            var material = new Material(Shader.Find("Sprites/Default")) { color = LineColour };

            renderer.sharedMaterial = material;
            renderer.sortingOrder = 1;

            return go;
        }

        /// <summary>
        /// A key naming an edge regardless of which of its two hexes drew it,
        /// and regardless of which way round its ends came out.
        /// </summary>
        private static long EdgeKey(Vec2 a, Vec2 b)
        {
            long first = PointKey(a);
            long second = PointKey(b);

            if (first > second) (first, second) = (second, first);

            return (first << 32) | second;
        }

        /// <summary>A corner as one number, exactly and without collision.</summary>
        /// <remarks>
        /// Each axis is a tenth of a metre over a map of at most 6 553 m, which
        /// is sixteen bits with the origin biased to the middle, so a point
        /// packs into thirty-two and an edge into sixty-four. Exact rather than
        /// a hash: a collision here would silently drop an edge of the board,
        /// and a board with a hole in it is a bug nobody would think to look for
        /// in a hash function.
        /// </remarks>
        private static long PointKey(Vec2 point)
        {
            long x = Mathf.RoundToInt(point.X / SameCornerWithin) + 32768L;
            long y = Mathf.RoundToInt(point.Y / SameCornerWithin) + 32768L;

            return ((x & 0xFFFF) << 16) | (y & 0xFFFF);
        }
    }
}
