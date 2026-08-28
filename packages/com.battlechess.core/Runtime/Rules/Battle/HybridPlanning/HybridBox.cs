using System;
using BattleChess.Contracts;
using BattleChess.Rules;

namespace BattleChess.Rules.HybridPlanning
{
    /// <summary>
    /// A rectangle at a continuous position and bearing, with its own
    /// collision test.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The rest of the codebase already has an oriented rectangle
    /// (<see cref="OrientedRect"/>) and a swept-clearance test
    /// (<see cref="Sweep"/>), and both are exactly what this planner needs.
    /// They are deliberately <b>not</b> used here.
    /// </para>
    /// <para>
    /// This planner exists to be measured against the search that already
    /// walks the field, on an arrangement where the two can disagree. If the
    /// new planner shared the old one's separating-axis test and that test
    /// had a bug — or a bias, or an assumption about tangency — both answers
    /// would carry it, and a difference between them would prove nothing.
    /// Independent geometry is what makes the comparison mean something.
    /// </para>
    /// </remarks>
    internal readonly struct HybridBox
    {
        public readonly Vec2 Centre;
        public readonly Facing Heading;
        public readonly float HalfWidth;
        public readonly float HalfDepth;

        private readonly Vec2 _forward;
        private readonly Vec2 _right;
        private readonly float _circumradius;

        public HybridBox(Vec2 centre, Facing heading, float halfWidth, float halfDepth)
        {
            Centre = centre;
            Heading = heading;
            HalfWidth = halfWidth;
            HalfDepth = halfDepth;

            // Worked out once here rather than on every read.
            //
            // Forward and Right were computed properties, so each was a cosine
            // and a sine every time it was touched — and Overlap touches them
            // four times directly and sixteen more through ProjectedRadius, for
            // some forty trigonometric calls per rectangle pair. A single march
            // runs millions of those pairs. The circumradius was two square
            // roots on the same path, for a number fixed at construction.
            //
            // The same fix the review branch made to OrientedRect, in the one
            // planner that shares none of its code and so never got it.
            float cos = MathF.Cos(heading.Radians);
            float sin = MathF.Sin(heading.Radians);

            _forward = new Vec2(cos, sin);
            _right = new Vec2(sin, -cos);
            _circumradius = MathF.Sqrt(halfWidth * halfWidth + halfDepth * halfDepth);
        }

        /// <summary>The same pose, its edges pushed out all round.</summary>
        /// <remarks>
        /// <b>M96.</b> The two boxes a pose needs share a centre and a heading
        /// and differ only in their half-extents, so the second one's cosine
        /// and sine are the first one's, already computed. This is the cheap
        /// half of the change: one square root instead of a cosine, a sine and
        /// a square root.
        /// </remarks>
        public HybridBox Grown(float clearance) =>
            new HybridBox(
                Centre, Heading, _forward, _right,
                HalfWidth + clearance, HalfDepth + clearance);

        /// <summary>The box that already knows which way it points.</summary>
        private HybridBox(
            Vec2 centre, Facing heading, Vec2 forward, Vec2 right, float halfWidth, float halfDepth)
        {
            Centre = centre;
            Heading = heading;
            HalfWidth = halfWidth;
            HalfDepth = halfDepth;

            _forward = forward;
            _right = right;
            _circumradius = MathF.Sqrt(halfWidth * halfWidth + halfDepth * halfDepth);
        }

        /// <summary>Radius of the circle that fully contains this box.</summary>
        public float Circumradius => _circumradius;

        public static HybridBox For(Vec2 centre, Facing heading, Footprint footprint, float clearance = 0f) =>
            new HybridBox(centre, heading, footprint.HalfWidth + clearance, footprint.HalfDepth + clearance);

        private Vec2 Forward => _forward;
        private Vec2 Right => _right;

        public void Corners(Span<Vec2> into)
        {
            Vec2 forward = Forward * HalfDepth;
            Vec2 right = Right * HalfWidth;

            into[0] = Centre + forward + right;
            into[1] = Centre + forward - right;
            into[2] = Centre - forward - right;
            into[3] = Centre - forward + right;
        }

        private float ProjectedRadius(Vec2 axis) =>
            MathF.Abs(Vec2.Dot(axis, Right)) * HalfWidth + MathF.Abs(Vec2.Dot(axis, Forward)) * HalfDepth;

        /// <summary>Whether a single point falls inside this box. Used by the obstacle grid, which reasons about points, not rectangles.</summary>
        public bool Contains(Vec2 point)
        {
            Vec2 offset = point - Centre;
            float alongWidth = Vec2.Dot(offset, Right);
            float alongDepth = Vec2.Dot(offset, Forward);
            return MathF.Abs(alongWidth) <= HalfWidth && MathF.Abs(alongDepth) <= HalfDepth;
        }

        /// <summary>
        /// Whether two boxes overlap by more than <paramref name="tolerance"/>,
        /// tested by the separating axis theorem — four candidate axes, one
        /// per distinct edge direction between the two rectangles.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Tolerance is a <b>demanded</b> gap, not a forgiven one: separation
        /// is declared when some axis shows a gap of at least
        /// <paramref name="tolerance"/>, so a positive value insists on that
        /// much daylight and calls anything closer an overlap. (This used to
        /// claim the opposite.)
        /// </para>
        /// <para>
        /// Every caller passes the default of zero, deliberately — at zero,
        /// two rectangles exactly edge to edge report separated, which is
        /// what lets a route run tangent to a body rather than being pushed
        /// off it, and tangency being legal is load-bearing here.
        /// </para>
        /// </remarks>
        /// <summary>
        /// How far apart two boxes are along their least-separating axis:
        /// positive is daylight, negative is how deep one is into the other.
        /// </summary>
        /// <remarks>
        /// The same four-axis test <see cref="Overlap"/> runs, reporting the
        /// margin instead of throwing it away. Needed because "is the mover
        /// getting out of this body or further into it" is not a question a
        /// boolean can answer, and that is the question the leaving rule
        /// turns on.
        /// <para>
        /// Not a true penetration depth for a rotating pair — it is the SAT
        /// axis margin, which for two rectangles is exact on the four edge
        /// normals and says nothing about any other direction. That is
        /// enough here: it is used to compare one pose against the next
        /// along a single sweep, where the same axes are being asked both
        /// times.
        /// </para>
        /// </remarks>
        public static float Separation(in HybridBox a, in HybridBox b)
        {
            Vec2 between = b.Centre - a.Centre;

            Span<Vec2> axes = stackalloc Vec2[4];
            axes[0] = a.Right;
            axes[1] = a.Forward;
            axes[2] = b.Right;
            axes[3] = b.Forward;

            float widest = float.NegativeInfinity;

            for (int i = 0; i < 4; i++)
            {
                float gap = MathF.Abs(Vec2.Dot(between, axes[i])) - a.ProjectedRadius(axes[i]) - b.ProjectedRadius(axes[i]);
                if (gap > widest) widest = gap;
            }

            return widest;
        }

        public static bool Overlap(in HybridBox a, in HybridBox b, float tolerance = 0f)
        {
            Vec2 between = b.Centre - a.Centre;

            float reach = a._circumradius + b._circumradius;
            if (between.LengthSquared > (reach + tolerance) * (reach + tolerance))
                return false;

            Span<Vec2> axes = stackalloc Vec2[4];
            axes[0] = a.Right;
            axes[1] = a.Forward;
            axes[2] = b.Right;
            axes[3] = b.Forward;

            for (int i = 0; i < 4; i++)
            {
                float gap = MathF.Abs(Vec2.Dot(between, axes[i])) - a.ProjectedRadius(axes[i]) - b.ProjectedRadius(axes[i]);
                if (gap >= tolerance)
                    return false; // A gap on this axis alone proves separation.
            }

            return true;
        }
    }
}
