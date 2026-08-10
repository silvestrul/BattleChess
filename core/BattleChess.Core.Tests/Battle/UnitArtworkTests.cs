using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using BattleChess.Contracts;
using Xunit;

namespace BattleChess.Tests.Battle
{
    /// <summary>
    /// That every kind of troops has a picture, and that the picture exists.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Artwork is the one thing in this project a wrong answer for is silent.
    /// A mistyped icon name does not throw, does not warn, and does not stop
    /// anything — the regiment simply draws as a blank plate, and the only way
    /// to notice is to be looking at that unit at the time.
    /// </para>
    /// <para>
    /// So the reference is checked here instead, against the sprite sheet's own
    /// import data. It costs a file read and turns "why is that one blank"
    /// into a failing test with the name in it.
    /// </para>
    /// </remarks>
    public sealed class UnitArtworkTests
    {
        [Fact]
        public void EveryUnitTypeSaysWhichPictureItWears()
        {
            string[] missing = TestContent.Units.All
                .Where(def => string.IsNullOrWhiteSpace(def.Get(UnitAttributes.Icon)))
                .Select(def => def.Key)
                .ToArray();

            Assert.True(missing.Length == 0,
                "A regiment with no icon draws as a blank plate and nothing anywhere says why: " +
                string.Join(", ", missing));
        }

        [Fact]
        public void EveryIconNamedInContentExistsInTheArtwork()
        {
            IReadOnlyCollection<string> available = SlicedSpriteNames();

            string[] dangling = TestContent.Units.All
                .Select(def => new { def.Key, Icon = def.Get(UnitAttributes.Icon) })
                .Where(entry => !string.IsNullOrWhiteSpace(entry.Icon) && !available.Contains(entry.Icon))
                .Select(entry => $"{entry.Key} wants '{entry.Icon}'")
                .ToArray();

            Assert.True(dangling.Length == 0,
                $"Content names artwork that does not exist. The sheets hold: " +
                $"{string.Join(", ", available.OrderBy(n => n))}. Dangling: {string.Join("; ", dangling)}");
        }

        [Fact]
        public void NoTwoKindsOfTroopsWearTheSamePicture()
        {
            string[] shared = TestContent.Units.All
                .Select(def => def.Get(UnitAttributes.Icon))
                .Where(icon => !string.IsNullOrWhiteSpace(icon))
                .GroupBy(icon => icon)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .ToArray();

            Assert.True(shared.Length == 0,
                "Telling regiments apart at a glance is most of what the picture is for, so two kinds " +
                "of troops must not wear the same one: " + string.Join(", ", shared));
        }

        /// <summary>
        /// Every named slice in every sprite sheet under the Unity art folder,
        /// read straight from the import data.
        /// </summary>
        /// <remarks>
        /// Reading the meta file is unlovely, but the alternative is no check at
        /// all: these tests cannot load a Unity asset, and the reference being
        /// tested is exactly the one nothing else will catch.
        /// </remarks>
        private static IReadOnlyCollection<string> SlicedSpriteNames()
        {
            var names = new HashSet<string>(StringComparer.Ordinal);

            foreach (string meta in Directory.EnumerateFiles(ArtDirectory(), "*.png.meta", SearchOption.AllDirectories))
            {
                foreach (Match match in Regex.Matches(File.ReadAllText(meta), @"(?m)^\s+name:\s*(\S+)\s*$"))
                    names.Add(match.Groups[1].Value);

                // A one-image file has no slices and is referenced by its own
                // file name instead.
                names.Add(Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(meta)));
            }

            return names;
        }

        private static string ArtDirectory()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);

            while (directory != null)
            {
                string candidate = Path.Combine(directory.FullName, "unity", "BattleChess", "Assets", "Art");

                if (Directory.Exists(candidate)) return candidate;

                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException("Could not find the Unity art folder.");
        }
    }
}
