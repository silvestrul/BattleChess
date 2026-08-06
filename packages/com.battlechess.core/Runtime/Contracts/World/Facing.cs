using System;

namespace BattleChess.Contracts
{
    /// <summary>
    /// A free continuous bearing, stored as radians anticlockwise from east and
    /// always normalised to [-π, π).
    /// </summary>
    /// <remarks>
    /// Facing is a free angle rather than one of the six hex directions: units
    /// move continuously, so snapping their facing to 60° increments would look
    /// wrong, and flanking is far more expressive as "degrees off the front"
    /// than as a bucket comparison.
    ///
    /// Wrapping it in a struct rather than passing raw floats around is
    /// deliberate — un-normalised angles are one of the most common sources of
    /// subtle bugs in this kind of code (an angle of 370° comparing as "far
    /// from" 10°), and normalising once at construction makes that class of bug
    /// unrepresentable.
    /// </remarks>
    public readonly struct Facing : IEquatable<Facing>
    {
        private const float TwoPi = 2f * MathF.PI;
        private const float DegreesPerRadian = 180f / MathF.PI;
        private const float RadiansPerDegree = MathF.PI / 180f;

        public static readonly Facing East = new Facing(0f);
        public static readonly Facing North = FromDegrees(90f);
        public static readonly Facing West = FromDegrees(180f);
        public static readonly Facing South = FromDegrees(-90f);

        /// <summary>Bearing in radians, anticlockwise from east, in [-π, π).</summary>
        public readonly float Radians;

        private Facing(float normalisedRadians)
        {
            Radians = normalisedRadians;
        }

        public float Degrees => Radians * DegreesPerRadian;

        public static Facing FromRadians(float radians) => new Facing(Normalise(radians));

        public static Facing FromDegrees(float degrees) => new Facing(Normalise(degrees * RadiansPerDegree));

        /// <summary>
        /// The bearing of a direction vector. A zero-length vector has no
        /// meaningful bearing and yields <see cref="East"/>.
        /// </summary>
        public static Facing FromVector(Vec2 direction) =>
            direction.IsNearZero ? East : new Facing(Normalise(MathF.Atan2(direction.Y, direction.X)));

        /// <summary>The bearing from one point toward another.</summary>
        public static Facing Towards(Vec2 from, Vec2 to) => FromVector(to - from);

        /// <summary>Bridges to the hex direction set, for code that still works in hex terms.</summary>
        public static Facing FromHexDirection(HexDirection direction) =>
            FromDegrees((int)direction * 60f);

        /// <summary>Unit vector pointing along this bearing.</summary>
        public Vec2 ToVector() => new Vec2(MathF.Cos(Radians), MathF.Sin(Radians));

        /// <summary>Unit vector pointing to this bearing's right (clockwise a quarter turn).</summary>
        public Vec2 RightVector() => new Vec2(MathF.Sin(Radians), -MathF.Cos(Radians));

        public Facing Opposite() => new Facing(Normalise(Radians + MathF.PI));

        public Facing RotatedBy(float radians) => new Facing(Normalise(Radians + radians));

        /// <summary>
        /// The signed turn needed to get from <paramref name="from"/> to
        /// <paramref name="to"/>, in radians in [-π, π]. Positive is anticlockwise.
        /// Always the shorter way round.
        /// </summary>
        public static float SignedDelta(Facing from, Facing to) => Normalise(to.Radians - from.Radians);

        /// <summary>
        /// The unsigned angle between two bearings, in radians in [0, π].
        /// This is the basis of flanking: 0 is head-on, π is from directly behind.
        /// </summary>
        public static float AbsoluteDelta(Facing a, Facing b) => MathF.Abs(SignedDelta(a, b));

        /// <summary>
        /// Turns toward <paramref name="target"/> by at most
        /// <paramref name="maxRadians"/>, stopping exactly on target rather than
        /// overshooting. The rotational counterpart of
        /// <see cref="Vec2.MoveTowards"/>, for when units gain a turn rate.
        /// </summary>
        public static Facing RotateTowards(Facing current, Facing target, float maxRadians)
        {
            if (maxRadians <= 0f)
                return current;

            float delta = SignedDelta(current, target);
            if (MathF.Abs(delta) <= maxRadians)
                return target;

            return current.RotatedBy(MathF.Sign(delta) * maxRadians);
        }

        /// <summary>Wraps any angle into [-π, π).</summary>
        private static float Normalise(float radians)
        {
            if (float.IsNaN(radians) || float.IsInfinity(radians))
                throw new ArgumentOutOfRangeException(nameof(radians), radians, "Bearing must be finite.");

            radians %= TwoPi;

            if (radians >= MathF.PI) radians -= TwoPi;
            else if (radians < -MathF.PI) radians += TwoPi;

            // The subtraction above can land exactly on +π for inputs very close
            // to it, which is outside the half-open range we promise.
            if (radians >= MathF.PI) radians -= TwoPi;

            return radians;
        }

        public bool Equals(Facing other) => Radians.Equals(other.Radians);

        public bool ApproximatelyEquals(Facing other, float toleranceRadians = 1e-5f) =>
            AbsoluteDelta(this, other) <= toleranceRadians;

        public override bool Equals(object? obj) => obj is Facing other && Equals(other);

        public override int GetHashCode() => Radians.GetHashCode();

        public override string ToString() => $"{Degrees:0.#}°";

        public static bool operator ==(Facing a, Facing b) => a.Equals(b);
        public static bool operator !=(Facing a, Facing b) => !a.Equals(b);
    }
}
