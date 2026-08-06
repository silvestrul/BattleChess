using System;
using BattleChess.Contracts;

namespace BattleChess.Rules
{
    /// <summary>
    /// Turns terrain into speed. The default implementation simply reads what
    /// the content declares.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Does its own precomputation rather than asking the catalogue on every
    /// call: speeds are flattened into a small lookup table once, at
    /// construction. Movement queries this per unit per tick, and pathfinding
    /// queries it across thousands of cells per route, so the difference between
    /// an array index and a dictionary walk is worth having.
    /// </para>
    /// <para>
    /// Because callers depend on <see cref="IMovementModel"/> rather than on
    /// this class, a scenario can substitute a different model — mud after rain,
    /// frozen rivers in winter — without any change to pathfinding or movement.
    /// </para>
    /// </remarks>
    public sealed class TerrainMovementModel : IMovementModel
    {
        private const int MovementTypeCount = 3;

        private readonly float[] _speeds;
        private readonly int _terrainCount;

        public TerrainMovementModel(ITerrainCatalogue catalogue)
        {
            if (catalogue == null) throw new ArgumentNullException(nameof(catalogue));

            _terrainCount = catalogue.Count;
            _speeds = new float[_terrainCount * MovementTypeCount];

            for (int terrainIndex = 0; terrainIndex < _terrainCount; terrainIndex++)
            {
                TerrainDef def = catalogue.Get(new TerrainId(terrainIndex));

                for (int movementIndex = 0; movementIndex < MovementTypeCount; movementIndex++)
                    _speeds[terrainIndex * MovementTypeCount + movementIndex] = def.SpeedMultiplier((MovementType)movementIndex);
            }
        }

        public float SpeedMultiplier(TerrainId terrain, MovementType movementType)
        {
            // Off-map counts as impassable rather than throwing: pathfinding and
            // movement both probe beyond the edge routinely, and an exception
            // there would be noise, not information.
            if (!terrain.IsValid || terrain.Index >= _terrainCount)
                return 0f;

            int movementIndex = (int)movementType;
            if ((uint)movementIndex >= MovementTypeCount)
                throw new ArgumentOutOfRangeException(nameof(movementType), movementType, "Unknown movement type.");

            return _speeds[terrain.Index * MovementTypeCount + movementIndex];
        }

        public bool IsPassable(TerrainId terrain, MovementType movementType) =>
            SpeedMultiplier(terrain, movementType) > 0f;
    }
}
