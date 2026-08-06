using System.IO;
using UnityEngine;

namespace BattleChess.Unity
{
    /// <summary>
    /// Finds the repository's <c>content</c> directory from inside Unity.
    /// </summary>
    /// <remarks>
    /// Walks up from the Assets folder, so the same text files drive the editor
    /// and the command-line harness with no copying and no chance of the two
    /// drifting apart.
    ///
    /// Editor and play-mode only. A standalone build has no repository around
    /// it, so shipping will mean copying content into StreamingAssets — a build
    /// step to add when there is something worth building.
    /// </remarks>
    public static class UnityContentLocator
    {
        public static string FindContentDirectory()
        {
            var directory = new DirectoryInfo(Application.dataPath);

            while (directory != null)
            {
                string candidate = Path.Combine(directory.FullName, "content");
                if (Directory.Exists(candidate))
                    return candidate;

                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException(
                $"Could not find a 'content' directory by searching upward from '{Application.dataPath}'.");
        }

        public static string TerrainFile() => Path.Combine(FindContentDirectory(), "terrain.cfg");

        public static string UnitsFile() => Path.Combine(FindContentDirectory(), "units.cfg");

        public static string FormationsFile() => Path.Combine(FindContentDirectory(), "formations.cfg");

        public static string MapFile(string name)
        {
            string maps = Path.Combine(FindContentDirectory(), "maps");
            string direct = Path.Combine(maps, name);

            return File.Exists(direct) ? direct : Path.Combine(maps, name + ".map.txt");
        }

        public static string BattleFile(string name)
        {
            string battles = Path.Combine(FindContentDirectory(), "battles");
            string direct = Path.Combine(battles, name);

            return File.Exists(direct) ? direct : Path.Combine(battles, name + ".battle.txt");
        }
    }
}
