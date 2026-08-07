using System.Collections.Generic;
using BattleChess.Contracts;
using UnityEngine;

namespace BattleChess.Unity
{
    /// <summary>
    /// Finds the artwork for a unit type, and falls back to something drawable
    /// when there is none.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Keyed on the unit's content key, so <c>spearmen</c> in <c>units.cfg</c>
    /// looks for <c>Assets/Art/Resources/Units/spearmen.png</c>. Adding a new
    /// kind of troops stays what it has always been — a block in a text file —
    /// plus a file dropped in a folder. No registry, no inspector wiring, and
    /// nothing to forget.
    /// </para>
    /// <para>
    /// A missing image is not an error. The unit draws as a plain plate and the
    /// game plays exactly as before, because artwork is decoration over a
    /// simulation that never consults it.
    /// </para>
    /// </remarks>
    public static class UnitArt
    {
        /// <summary>Where the loader looks, relative to any Resources folder.</summary>
        public const string SymbolFolder = "Units";

        private static readonly Dictionary<string, Sprite> _symbols = new Dictionary<string, Sprite>();
        private static Sprite _plate;
        private static Sprite _blank;

        /// <summary>
        /// The symbol drawn on a regiment's plate, or null if that unit type has
        /// no artwork yet.
        /// </summary>
        public static Sprite SymbolFor(UnitDef def)
        {
            if (def == null) return null;

            if (_symbols.TryGetValue(def.Key, out Sprite cached)) return cached;

            // Cached even when nothing is found, so a missing file costs one
            // lookup for the whole session rather than one per unit per load.
            Sprite loaded = Resources.Load<Sprite>($"{SymbolFolder}/{def.Key}");
            _symbols[def.Key] = loaded;

            return loaded;
        }

        /// <summary>
        /// The rectangle a regiment is drawn as: the supplied plate if there is
        /// one, otherwise a plain white square.
        /// </summary>
        /// <remarks>
        /// Shared by every unit on the field and tinted per army, which is why
        /// it wants to be white — a tint can only darken. Anything already
        /// coloured comes out muddy.
        /// </remarks>
        public static Sprite Plate()
        {
            if (_plate != null) return _plate;

            _plate = Resources.Load<Sprite>($"{SymbolFolder}/plate") ?? Blank();
            return _plate;
        }

        /// <summary>A one-pixel white square, scaled to whatever is needed.</summary>
        public static Sprite Blank()
        {
            if (_blank != null) return _blank;

            var texture = new Texture2D(1, 1, TextureFormat.RGBA32, mipChain: false) { name = "Block" };
            texture.SetPixel(0, 0, Color.white);
            texture.Apply();

            _blank = Sprite.Create(texture, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), pixelsPerUnit: 1f);
            return _blank;
        }

        /// <summary>
        /// Drops everything loaded, so new or re-imported artwork appears
        /// without restarting play mode.
        /// </summary>
        public static void Forget()
        {
            _symbols.Clear();
            _plate = null;
        }
    }
}
