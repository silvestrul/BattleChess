using System;
using System.IO;
using BattleChess.Contracts;
using BattleChess.Rules;

namespace BattleChess.Tests.Battle
{
    /// <summary>
    /// The game's real content, loaded once for the whole test run.
    /// </summary>
    /// <remarks>
    /// <para>
    /// These tests deliberately fight with the shipped numbers rather than
    /// invented ones. A balance test that made up its own units would prove
    /// only that the formulas compile; the thing actually worth protecting is
    /// that <c>units.cfg</c> still says spearmen break cavalry after somebody
    /// has spent an evening tuning it.
    /// </para>
    /// <para>
    /// The consequence is that these are <b>design-intent</b> tests, not exact
    /// ones. Every threshold is a band wide enough that ordinary tuning passes
    /// through it untouched, and narrow enough that losing a counter fails
    /// loudly. If a test here starts failing, either the content is wrong or
    /// the design has changed — and both are worth stopping for.
    /// </para>
    /// </remarks>
    public static class TestContent
    {
        private static readonly Lazy<string> Directory = new Lazy<string>(FindContentDirectory);

        private static readonly Lazy<ITerrainCatalogue> LazyTerrain =
            new Lazy<ITerrainCatalogue>(() => TerrainCatalogueReader.Read(ReadFile("terrain.cfg")));

        private static readonly Lazy<IUnitCatalogue> LazyUnits =
            new Lazy<IUnitCatalogue>(() => UnitCatalogueReader.Read(ReadFile("units.cfg")));

        private static readonly Lazy<IFormationCatalogue> LazyFormations =
            new Lazy<IFormationCatalogue>(() => FormationCatalogueReader.Read(ReadFile("formations.cfg")));

        /// <summary>The repository's own <c>content</c> directory.</summary>
        /// <remarks>
        /// Exposed for the tests that read a whole battle or map file rather
        /// than a catalogue — the scenario checks, which have to load exactly
        /// what ships rather than a fixture standing in for it.
        /// </remarks>
        public static string Root => Directory.Value;

        public static ITerrainCatalogue Terrain => LazyTerrain.Value;

        public static IUnitCatalogue Units => LazyUnits.Value;

        public static IFormationCatalogue Formations => LazyFormations.Value;

        public static UnitDef Unit(string key) =>
            Units.TryGetByKey(key, out UnitDef def)
                ? def
                : throw new ArgumentException($"No unit '{key}' in units.cfg.", nameof(key));

        public static TerrainDef Ground(string key) =>
            Terrain.TryGetByKey(key, out TerrainDef def)
                ? def
                : throw new ArgumentException($"No terrain '{key}' in terrain.cfg.", nameof(key));

        public static FormationDef Formation(string key) =>
            Formations.TryGetByKey(key, out FormationDef def)
                ? def
                : throw new ArgumentException($"No formation '{key}' in formations.cfg.", nameof(key));

        private static string ReadFile(string name) => File.ReadAllText(Path.Combine(Directory.Value, name));

        /// <summary>
        /// Walks up from the test assembly to find the repository's content
        /// directory, so the suite runs the same from an IDE, from the CLI, and
        /// from its own output folder.
        /// </summary>
        private static string FindContentDirectory()
        {
            foreach (string start in new[] { AppContext.BaseDirectory, System.IO.Directory.GetCurrentDirectory() })
            {
                var directory = new DirectoryInfo(start);

                while (directory != null)
                {
                    string candidate = Path.Combine(directory.FullName, "content");

                    if (System.IO.Directory.Exists(candidate))
                        return candidate;

                    directory = directory.Parent;
                }
            }

            throw new DirectoryNotFoundException(
                $"Could not find the 'content' directory above '{AppContext.BaseDirectory}'.");
        }
    }
}
