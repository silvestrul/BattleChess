using System.Collections.Generic;

namespace BattleChess.Contracts
{
    /// <summary>
    /// The set of terrain types in play, and the only way to turn a
    /// <see cref="TerrainId"/> back into what it means.
    /// </summary>
    /// <remarks>
    /// Its single job is resolution. It does not know where terrain sits, how
    /// fast anything moves through it, or how it was loaded.
    /// </remarks>
    public interface ITerrainCatalogue
    {
        int Count { get; }

        IReadOnlyList<TerrainDef> All { get; }

        /// <summary>Resolves a handle. Throws if the id did not come from this catalogue.</summary>
        TerrainDef Get(TerrainId id);

        /// <summary>Looks up by content-file key, e.g. "plains".</summary>
        bool TryGetByKey(string key, out TerrainDef def);

        /// <summary>Looks up by map-authoring character.</summary>
        bool TryGetByGlyph(char glyph, out TerrainDef def);
    }

    /// <summary>
    /// Answers what terrain does to movement.
    /// </summary>
    /// <remarks>
    /// Split out from the catalogue and the map on purpose. Pathfinding and the
    /// per-tick movement step depend on this interface alone, so movement rules
    /// can be replaced — a scenario where rain has mired every road, a different
    /// game mode — without touching either of the other two.
    /// </remarks>
    public interface IMovementModel
    {
        /// <summary>
        /// Multiplier on a unit's base speed for this terrain. Zero means
        /// impassable.
        /// </summary>
        float SpeedMultiplier(TerrainId terrain, MovementType movementType);

        bool IsPassable(TerrainId terrain, MovementType movementType);
    }
}
