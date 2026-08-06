using System;

namespace BattleChess.Contracts
{
    /// <summary>
    /// The rectangle of world space a battlefield occupies, in metres.
    /// </summary>
    public readonly struct MapBounds
    {
        public readonly Vec2 Min;
        public readonly Vec2 Max;

        public MapBounds(Vec2 min, Vec2 max)
        {
            if (!(max.X > min.X) || !(max.Y > min.Y))
                throw new ArgumentException($"Bounds must enclose a positive area; got {min} to {max}.", nameof(max));

            Min = min;
            Max = max;
        }

        public float Width => Max.X - Min.X;
        public float Height => Max.Y - Min.Y;
        public Vec2 Centre => new Vec2((Min.X + Max.X) * 0.5f, (Min.Y + Max.Y) * 0.5f);

        public bool Contains(Vec2 point) =>
            point.X >= Min.X && point.X <= Max.X &&
            point.Y >= Min.Y && point.Y <= Max.Y;

        /// <summary>Nearest point inside the bounds.</summary>
        public Vec2 Clamp(Vec2 point) => new Vec2(
            Math.Clamp(point.X, Min.X, Max.X),
            Math.Clamp(point.Y, Min.Y, Max.Y));

        public override string ToString() => $"[{Min} .. {Max}] ({Width:0.#}×{Height:0.#}m)";
    }

    /// <summary>
    /// Answers one question: which terrain lies at a given point on the field.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Deliberately a <i>field</i> keyed by continuous position rather than a
    /// grid of cells. How the answer is stored — a text-authored grid today,
    /// possibly a painted image or hand-drawn regions later — is nobody else's
    /// business, so none of it leaks into the callers.
    /// </para>
    /// <para>
    /// This is also why the authoring resolution and the pathfinding resolution
    /// can differ freely. Maps are authored coarse enough to be readable by a
    /// human; the pathfinder builds its own much finer grid by sampling this
    /// same field wherever it needs to.
    /// </para>
    /// </remarks>
    public interface ITerrainMap
    {
        MapBounds Bounds { get; }

        /// <summary>
        /// Terrain at a world position, or <see cref="TerrainId.None"/> if the
        /// point lies off the map.
        /// </summary>
        TerrainId At(Vec2 worldPosition);
    }
}
