using System;
using BattleChess.Contracts;

namespace BattleChess.Rules
{
    /// <summary>
    /// Treats every passable terrain as open ground, ignoring how slow it
    /// actually is. Impassable stays impassable.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A debugging aid. With terrain costs removed, routes become the shortest
    /// path rather than the fastest, which is the quickest way to tell whether
    /// an odd-looking detour is a pathfinding bug or the terrain costs doing
    /// exactly their job.
    /// </para>
    /// <para>
    /// Worth noticing that this needed no changes anywhere else. Nothing in the
    /// simulation depends on <see cref="TerrainMovementModel"/> — only on
    /// <see cref="IMovementModel"/> — so a debug toggle swaps the implementation
    /// instead of threading an "ignore terrain" flag through pathfinding,
    /// movement and everything downstream. The same seam is what will later
    /// carry weather, or a scenario where rain has mired every road.
    /// </para>
    /// </remarks>
    public sealed class FlatMovementModel : IMovementModel
    {
        private readonly IMovementModel _underlying;

        public FlatMovementModel(IMovementModel underlying)
        {
            _underlying = underlying ?? throw new ArgumentNullException(nameof(underlying));
        }

        public float SpeedMultiplier(TerrainId terrain, MovementType movementType) =>
            _underlying.IsPassable(terrain, movementType) ? 1f : 0f;

        public bool IsPassable(TerrainId terrain, MovementType movementType) =>
            _underlying.IsPassable(terrain, movementType);
    }
}
