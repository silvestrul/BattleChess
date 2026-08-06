using System.Collections.Generic;
using BattleChess.Contracts;
using BattleChess.Rules;
using UnityEngine;

namespace BattleChess.Unity
{
    /// <summary>
    /// Draws the battlefield as a generated texture, one pixel per authored
    /// cell.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Placeholder art on purpose. It exists so the ground can be seen and
    /// judged now; hand-drawn tiles replace this by swapping this one component,
    /// because nothing else knows how terrain is drawn.
    /// </para>
    /// <para>
    /// Colour lives here rather than in <c>terrain.cfg</c> for the same reason
    /// it lives in the text harness rather than the content: it belongs to a
    /// particular view. The two renderers pick their own palettes and cannot
    /// disturb each other.
    /// </para>
    /// </remarks>
    public static class TerrainView
    {
        private static readonly Dictionary<string, Color32> ColourByTerrainKey = new Dictionary<string, Color32>
        {
            ["plains"] = new Color32(122, 158, 84, 255),
            ["road"] = new Color32(196, 178, 140, 255),
            ["desert"] = new Color32(219, 201, 137, 255),
            ["forest"] = new Color32(58, 102, 62, 255),
            ["hill"] = new Color32(150, 140, 92, 255),
            ["jungle"] = new Color32(36, 84, 50, 255),
            ["river"] = new Color32(88, 152, 196, 255),
            ["mountain"] = new Color32(122, 118, 112, 255),
            ["swamp"] = new Color32(94, 104, 74, 255),
            ["deepwater"] = new Color32(44, 90, 148, 255)
        };

        public static GameObject Build(BattleMapDefinition map, ITerrainCatalogue catalogue, Transform parent)
        {
            GridTerrainMap terrain = map.Terrain;

            var texture = new Texture2D(terrain.Columns, terrain.Rows, TextureFormat.RGBA32, mipChain: false)
            {
                name = "Terrain",
                // Point filtering keeps cell edges crisp instead of smearing
                // them, which matters while this is stand-in art being read for
                // information rather than looked at for beauty.
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };

            var pixels = new Color32[terrain.Columns * terrain.Rows];

            for (int row = 0; row < terrain.Rows; row++)
            for (int column = 0; column < terrain.Columns; column++)
            {
                TerrainDef def = catalogue.Get(terrain.AtCell(column, row));

                // Map row 0 is the northern edge, but texture row 0 is the
                // bottom, so the rows are flipped on the way in.
                int textureRow = terrain.Rows - 1 - row;
                pixels[textureRow * terrain.Columns + column] = ColourFor(def);
            }

            texture.SetPixels32(pixels);
            texture.Apply();

            var sprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, terrain.Columns, terrain.Rows),
                new Vector2(0.5f, 0.5f),
                pixelsPerUnit: 1f);

            var go = new GameObject("Terrain");
            go.transform.SetParent(parent, worldPositionStays: false);

            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sortingOrder = 0;

            // One texture pixel is one authored cell, so scaling by the cell
            // size puts the map at its true size in metres.
            go.transform.localScale = new Vector3(terrain.CellSize, terrain.CellSize, 1f);
            go.transform.position = new Vector3(terrain.Bounds.Centre.X, terrain.Bounds.Centre.Y, 0f);

            return go;
        }

        private static Color32 ColourFor(TerrainDef def) =>
            ColourByTerrainKey.TryGetValue(def.Key, out Color32 colour)
                ? colour
                : new Color32(200, 0, 200, 255);   // glaring magenta: an unstyled terrain should be obvious
    }
}
