using System;

namespace BattleChess.Contracts
{
    /// <summary>
    /// A 2D vector in world space, in metres. Used for positions, directions
    /// and offsets alike.
    /// </summary>
    /// <remarks>
    /// World space is <b>Y-up</b>: +X is east, +Y is north. That convention is
    /// load-bearing — it is what makes <see cref="HexDirection.NorthEast"/>
    /// actually point north-east once hex coordinates are converted to world
    /// positions. See <see cref="HexLayout"/>.
    ///
    /// This is the authoritative position type for units. The hex grids in this
    /// project are internal calculation aids and never own a unit's position.
    /// </remarks>
    public readonly struct Vec2 : IEquatable<Vec2>
    {
        /// <summary>
        /// Tolerance for "near enough" comparisons, in metres. Deliberately
        /// coarse relative to float precision: battlefield distances are tens to
        /// thousands of metres, so 10 micrometres is far below anything the
        /// simulation can meaningfully distinguish.
        /// </summary>
        public const float Epsilon = 1e-5f;

        public static readonly Vec2 Zero = new Vec2(0f, 0f);
        public static readonly Vec2 East = new Vec2(1f, 0f);
        public static readonly Vec2 North = new Vec2(0f, 1f);

        public readonly float X;
        public readonly float Y;

        public Vec2(float x, float y)
        {
            X = x;
            Y = y;
        }

        public float LengthSquared => X * X + Y * Y;

        public float Length => MathF.Sqrt(X * X + Y * Y);

        public bool IsNearZero => LengthSquared <= Epsilon * Epsilon;

        /// <summary>
        /// Unit vector in the same direction, or <see cref="Zero"/> if this
        /// vector is too short to have a meaningful direction.
        /// </summary>
        public Vec2 Normalised()
        {
            float lengthSquared = LengthSquared;
            if (lengthSquared <= Epsilon * Epsilon)
                return Zero;

            float inverseLength = 1f / MathF.Sqrt(lengthSquared);
            return new Vec2(X * inverseLength, Y * inverseLength);
        }

        /// <summary>Rotated a quarter turn anticlockwise.</summary>
        public Vec2 Perpendicular => new Vec2(-Y, X);

        /// <summary>Rotated anticlockwise by the given angle in radians.</summary>
        public Vec2 Rotated(float radians)
        {
            float cos = MathF.Cos(radians);
            float sin = MathF.Sin(radians);
            return new Vec2(X * cos - Y * sin, X * sin + Y * cos);
        }

        public static float Dot(Vec2 a, Vec2 b) => a.X * b.X + a.Y * b.Y;

        /// <summary>
        /// The 2D cross product (perp-dot). Positive when <paramref name="b"/>
        /// lies anticlockwise of <paramref name="a"/> — the standard way to ask
        /// "which side of this line am I on", which flanking and path smoothing
        /// both need.
        /// </summary>
        public static float Cross(Vec2 a, Vec2 b) => a.X * b.Y - a.Y * b.X;

        public static float DistanceSquared(Vec2 a, Vec2 b)
        {
            float dx = a.X - b.X;
            float dy = a.Y - b.Y;
            return dx * dx + dy * dy;
        }

        public static float Distance(Vec2 a, Vec2 b) => MathF.Sqrt(DistanceSquared(a, b));

        public static Vec2 Lerp(Vec2 a, Vec2 b, float t) =>
            new Vec2(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);

        /// <summary>
        /// Steps from <paramref name="current"/> toward <paramref name="target"/>
        /// by at most <paramref name="maxDistance"/>, stopping exactly on the
        /// target rather than overshooting. This is the core of per-tick movement
        /// integration.
        /// </summary>
        public static Vec2 MoveTowards(Vec2 current, Vec2 target, float maxDistance)
        {
            if (maxDistance <= 0f)
                return current;

            Vec2 delta = target - current;
            float distanceSquared = delta.LengthSquared;

            if (distanceSquared <= maxDistance * maxDistance)
                return target;

            float distance = MathF.Sqrt(distanceSquared);
            if (distance <= Epsilon)
                return target;

            float scale = maxDistance / distance;
            return new Vec2(current.X + delta.X * scale, current.Y + delta.Y * scale);
        }

        public static Vec2 operator +(Vec2 a, Vec2 b) => new Vec2(a.X + b.X, a.Y + b.Y);
        public static Vec2 operator -(Vec2 a, Vec2 b) => new Vec2(a.X - b.X, a.Y - b.Y);
        public static Vec2 operator -(Vec2 a) => new Vec2(-a.X, -a.Y);
        public static Vec2 operator *(Vec2 a, float k) => new Vec2(a.X * k, a.Y * k);
        public static Vec2 operator *(float k, Vec2 a) => new Vec2(a.X * k, a.Y * k);
        public static Vec2 operator /(Vec2 a, float k) => new Vec2(a.X / k, a.Y / k);

        /// <summary>
        /// Exact component equality. Almost always the wrong test for computed
        /// values — prefer <see cref="ApproximatelyEquals"/>.
        /// </summary>
        public bool Equals(Vec2 other) => X.Equals(other.X) && Y.Equals(other.Y);

        public bool ApproximatelyEquals(Vec2 other, float tolerance = Epsilon) =>
            DistanceSquared(this, other) <= tolerance * tolerance;

        public override bool Equals(object? obj) => obj is Vec2 other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                return (X.GetHashCode() * 397) ^ Y.GetHashCode();
            }
        }

        public override string ToString() => $"({X:0.###}, {Y:0.###})";

        public static bool operator ==(Vec2 a, Vec2 b) => a.Equals(b);
        public static bool operator !=(Vec2 a, Vec2 b) => !a.Equals(b);
    }
}
