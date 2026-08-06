using System;

namespace BattleChess.Contracts
{
    /// <summary>
    /// The ground a regiment physically occupies, in metres: its frontage
    /// (<see cref="Width"/>, across the line) and its depth (along the direction
    /// it faces).
    /// </summary>
    /// <remarks>
    /// This is a shape rather than a point from the outset. Units are rectangles
    /// on the field, not tokens on cells, and a formation in line (wide, shallow)
    /// is meaningfully different ground from a column (narrow, deep).
    /// </remarks>
    public readonly struct Footprint : IEquatable<Footprint>
    {
        /// <summary>Frontage in metres, measured across the unit's facing.</summary>
        public readonly float Width;

        /// <summary>Depth in metres, measured along the unit's facing.</summary>
        public readonly float Depth;

        public Footprint(float width, float depth)
        {
            if (!(width > 0f) || float.IsInfinity(width))
                throw new ArgumentOutOfRangeException(nameof(width), width, "Frontage must be finite and positive.");
            if (!(depth > 0f) || float.IsInfinity(depth))
                throw new ArgumentOutOfRangeException(nameof(depth), depth, "Depth must be finite and positive.");

            Width = width;
            Depth = depth;
        }

        public float HalfWidth => Width * 0.5f;
        public float HalfDepth => Depth * 0.5f;
        public float Area => Width * Depth;

        /// <summary>Radius of the circle that fully contains this footprint.</summary>
        public float BoundingRadius => MathF.Sqrt(HalfWidth * HalfWidth + HalfDepth * HalfDepth);

        public bool Equals(Footprint other) => Width.Equals(other.Width) && Depth.Equals(other.Depth);
        public override bool Equals(object? obj) => obj is Footprint other && Equals(other);
        public override int GetHashCode() { unchecked { return (Width.GetHashCode() * 397) ^ Depth.GetHashCode(); } }
        public override string ToString() => $"{Width:0.#}×{Depth:0.#}m";
    }

    /// <summary>
    /// A <see cref="Footprint"/> placed on the field: an oriented rectangle at a
    /// continuous position with a free bearing.
    /// </summary>
    /// <remarks>
    /// Collision between units is an overlap test between two of these, computed
    /// entirely in continuous space. No grid is consulted, and none of this
    /// depends on hex geometry.
    /// </remarks>
    public readonly struct OrientedRect
    {
        public readonly Vec2 Centre;
        public readonly Facing Facing;
        public readonly Footprint Footprint;

        public OrientedRect(Vec2 centre, Facing facing, Footprint footprint)
        {
            Centre = centre;
            Facing = facing;
            Footprint = footprint;
        }

        /// <summary>Unit vector along the unit's depth axis, pointing where it faces.</summary>
        public Vec2 Forward => Facing.ToVector();

        /// <summary>Unit vector along the unit's frontage axis, pointing to its right.</summary>
        public Vec2 Right => Facing.RightVector();

        /// <summary>
        /// The four corners, anticlockwise from the front-right. Mainly for
        /// rendering and for making test failures legible.
        /// </summary>
        public void GetCorners(Span<Vec2> destination)
        {
            if (destination.Length < 4)
                throw new ArgumentException("Needs room for 4 corners.", nameof(destination));

            Vec2 alongDepth = Forward * Footprint.HalfDepth;
            Vec2 alongWidth = Right * Footprint.HalfWidth;

            destination[0] = Centre + alongDepth + alongWidth; // front right
            destination[1] = Centre + alongDepth - alongWidth; // front left
            destination[2] = Centre - alongDepth - alongWidth; // rear left
            destination[3] = Centre - alongDepth + alongWidth; // rear right
        }

        public Vec2[] GetCorners()
        {
            var corners = new Vec2[4];
            GetCorners(corners);
            return corners;
        }

        public bool ContainsPoint(Vec2 point)
        {
            Vec2 offset = point - Centre;
            float alongWidth = Vec2.Dot(offset, Right);
            float alongDepth = Vec2.Dot(offset, Forward);

            return MathF.Abs(alongWidth) <= Footprint.HalfWidth
                && MathF.Abs(alongDepth) <= Footprint.HalfDepth;
        }

        /// <summary>
        /// The point on or inside this rectangle nearest to
        /// <paramref name="point"/>. Clamping in the rectangle's own frame makes
        /// this exact regardless of orientation.
        /// </summary>
        public Vec2 ClosestPointTo(Vec2 point)
        {
            Vec2 offset = point - Centre;
            Vec2 right = Right;
            Vec2 forward = Forward;

            float alongWidth = Math.Clamp(Vec2.Dot(offset, right), -Footprint.HalfWidth, Footprint.HalfWidth);
            float alongDepth = Math.Clamp(Vec2.Dot(offset, forward), -Footprint.HalfDepth, Footprint.HalfDepth);

            return Centre + right * alongWidth + forward * alongDepth;
        }

        /// <summary>
        /// Half-extent of this rectangle's shadow when projected onto a unit
        /// <paramref name="axis"/>.
        /// </summary>
        public float ProjectedRadius(Vec2 axis) =>
            MathF.Abs(Vec2.Dot(axis, Right)) * Footprint.HalfWidth
          + MathF.Abs(Vec2.Dot(axis, Forward)) * Footprint.HalfDepth;

        /// <summary>
        /// Whether two placed footprints overlap, by the separating axis theorem.
        /// </summary>
        /// <remarks>
        /// Two convex shapes are disjoint exactly when some axis exists on which
        /// their projections do not overlap. For rectangles only four candidate
        /// axes need checking — each rectangle's two edge normals — because
        /// opposite edges are parallel. Finding any gap proves separation
        /// immediately, so the common non-touching case exits early.
        ///
        /// Touching exactly edge-to-edge counts as <b>not</b> overlapping, so
        /// units drawn up flush against each other are legal.
        /// </remarks>
        public static bool Overlaps(in OrientedRect a, in OrientedRect b)
        {
            Vec2 betweenCentres = b.Centre - a.Centre;

            // Cheap reject before the full test: if the bounding circles miss,
            // the rectangles certainly do.
            float reach = a.Footprint.BoundingRadius + b.Footprint.BoundingRadius;
            if (betweenCentres.LengthSquared > reach * reach)
                return false;

            return !IsSeparatedAlong(a.Right, a, b, betweenCentres)
                && !IsSeparatedAlong(a.Forward, a, b, betweenCentres)
                && !IsSeparatedAlong(b.Right, a, b, betweenCentres)
                && !IsSeparatedAlong(b.Forward, a, b, betweenCentres);
        }

        private static bool IsSeparatedAlong(Vec2 axis, in OrientedRect a, in OrientedRect b, Vec2 betweenCentres) =>
            MathF.Abs(Vec2.Dot(betweenCentres, axis)) >= a.ProjectedRadius(axis) + b.ProjectedRadius(axis);

        /// <summary>
        /// If <paramref name="a"/> and <paramref name="b"/> overlap, yields the
        /// shortest translation that would move <paramref name="a"/> clear of
        /// <paramref name="b"/>.
        /// </summary>
        /// <remarks>
        /// This is the same separating-axis search as <see cref="Overlaps"/>, but
        /// instead of stopping at the first gap it measures the overlap on every
        /// candidate axis and keeps the smallest — the least violent way to pull
        /// two units apart.
        ///
        /// Axes are tested in a fixed order and ties resolve to the first axis
        /// tested, so the result is reproducible. If the two centres coincide
        /// exactly the push direction is arbitrary but still deterministic.
        /// </remarks>
        public static bool TryGetSeparation(in OrientedRect a, in OrientedRect b, out Vec2 pushForA)
        {
            pushForA = Vec2.Zero;

            Vec2 betweenCentres = b.Centre - a.Centre;

            Span<Vec2> axes = stackalloc Vec2[4];
            axes[0] = a.Right;
            axes[1] = a.Forward;
            axes[2] = b.Right;
            axes[3] = b.Forward;

            float smallestOverlap = float.MaxValue;
            Vec2 smallestAxis = Vec2.Zero;

            for (int i = 0; i < axes.Length; i++)
            {
                Vec2 axis = axes[i];
                float gap = MathF.Abs(Vec2.Dot(betweenCentres, axis));
                float combinedReach = a.ProjectedRadius(axis) + b.ProjectedRadius(axis);
                float overlap = combinedReach - gap;

                if (overlap <= 0f)
                    return false;

                if (overlap < smallestOverlap)
                {
                    smallestOverlap = overlap;
                    smallestAxis = axis;
                }
            }

            // Push along the chosen axis, away from b.
            float directedGap = Vec2.Dot(betweenCentres, smallestAxis);
            float sign = directedGap >= 0f ? -1f : 1f;

            pushForA = smallestAxis * (smallestOverlap * sign);
            return true;
        }

        public override string ToString() => $"[{Footprint} at {Centre} facing {Facing}]";
    }
}
