using System;
using System.IO;

namespace BattleChess.Cli
{
    /// <summary>
    /// Finds the repository's <c>content</c> directory.
    /// </summary>
    /// <remarks>
    /// Walks up from the running assembly rather than assuming a working
    /// directory, so the harness behaves the same whether it is launched from
    /// the repository root, from its own output folder, or from an IDE.
    /// </remarks>
    public static class ContentLocator
    {
        public static string FindContentDirectory()
        {
            foreach (string start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
            {
                var directory = new DirectoryInfo(start);

                while (directory != null)
                {
                    string candidate = Path.Combine(directory.FullName, "content");
                    if (Directory.Exists(candidate))
                        return candidate;

                    directory = directory.Parent;
                }
            }

            throw new DirectoryNotFoundException(
                "Could not find the 'content' directory by searching upward from " +
                $"'{AppContext.BaseDirectory}' or '{Directory.GetCurrentDirectory()}'.");
        }

        public static string TerrainFile() => Path.Combine(FindContentDirectory(), "terrain.cfg");

        public static string UnitsFile() => Path.Combine(FindContentDirectory(), "units.cfg");

        public static string FormationsFile() => Path.Combine(FindContentDirectory(), "formations.cfg");

        /// <summary>Resolves a battle argument, accepting a path or a bare name.</summary>
        public static string BattleFile(string nameOrPath)
        {
            if (File.Exists(nameOrPath))
                return nameOrPath;

            string battles = Path.Combine(FindContentDirectory(), "battles");

            foreach (string candidate in new[]
                     {
                         Path.Combine(battles, nameOrPath),
                         Path.Combine(battles, nameOrPath + ".battle.txt")
                     })
            {
                if (File.Exists(candidate))
                    return candidate;
            }

            throw new FileNotFoundException($"No battle found for '{nameOrPath}'. Looked in '{battles}'.");
        }

        /// <summary>
        /// Resolves a map argument, accepting either a path or a bare name such
        /// as "valley".
        /// </summary>
        public static string MapFile(string nameOrPath)
        {
            if (File.Exists(nameOrPath))
                return nameOrPath;

            string maps = Path.Combine(FindContentDirectory(), "maps");

            foreach (string candidate in new[]
                     {
                         Path.Combine(maps, nameOrPath),
                         Path.Combine(maps, nameOrPath + ".map.txt")
                     })
            {
                if (File.Exists(candidate))
                    return candidate;
            }

            throw new FileNotFoundException($"No map found for '{nameOrPath}'. Looked in '{maps}'.");
        }
    }
}
