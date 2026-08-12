using UnityEditor;
using UnityEngine;

namespace BattleChess.Unity.EditorTools
{
    /// <summary>
    /// Sets the import settings for unit artwork, so a dropped PNG just works.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The project's own default is the first line of defence — it is set to
    /// 2D, so any texture imports as a sprite. This is the second, and it
    /// exists because the failure is silent: an image that imports as a plain
    /// texture never loads as a sprite, the regiment simply draws with no
    /// badge, and nothing anywhere says why. Cheaper to guarantee than to
    /// diagnose.
    /// </para>
    /// <para>
    /// Applies to every import rather than only the first. An earlier version
    /// bailed out unless the settings were missing, meaning any file that had
    /// ever been imported under the wrong defaults stayed wrong for good and
    /// could only be fixed by deleting its meta file — which is exactly the
    /// obscure ritual this class exists to abolish.
    /// </para>
    /// </remarks>
    public sealed class UnitArtImporter : AssetPostprocessor
    {
        private const string ArtFolder = "Assets/Art/Resources/Units/";

        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(ArtFolder)) return;

            var importer = (TextureImporter)assetImporter;

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;

            // Smooth rather than blocky: these are drawn at map scale and the
            // camera zooms a long way in and out.
            importer.filterMode = FilterMode.Bilinear;
            importer.wrapMode = TextureWrapMode.Clamp;

            // The pivot every unit is positioned and rotated about is its
            // centre, matching how the rules place a regiment.
            TextureImporterSettings settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteAlignment = (int)SpriteAlignment.Center;
            importer.SetTextureSettings(settings);
        }
    }
}
