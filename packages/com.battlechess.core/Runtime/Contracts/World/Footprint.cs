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
        /// The width of the gap between two placed footprints in metres, or
        /// zero if they overlap.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>The question every rule about nearness should be asking.</b>
        /// Centre-to-centre distance is the wrong measure for regiments,
        /// because a regiment is a wide, thin rectangle rather than a token: a
        /// body of cavalry a hundred and six metres across and eight deep has
        /// its centre nowhere near most of itself. Two such regiments sliding
        /// past one another twenty metres apart have their formations
        /// completely interpenetrated while their centres are still a hundred
        /// metres away from each other, and any rule measuring centres calls
        /// that "not touching".
        /// </para>
        /// <para>
        /// Computed as the largest separation over the four candidate axes,
        /// which is exact whenever the nearest features are a face and a
        /// corner — the ordinary case for troops drawn up in lines. Two
        /// rectangles meeting corner to corner at an angle report slightly
        /// less than the true gap, so this errs toward calling things close.
        /// For deciding whether men can reach each other with a sword that is
        /// the right way to be wrong.
        /// </para>
        /// </remarks>
        public static float GapBetween(in OrientedRect a, in OrientedRect b)
        {
            Vec2 betweenCentres = b.Centre - a.Centre;

            float widest = float.MinValue;

            Span<Vec2> axes = stackalloc Vec2[4];
            axes[0] = a.Right;
            axes[1] = a.Forward;
            axes[2] = b.Right;
            axes[3] = b.Forward;

            for (int i = 0; i < axes.Length; i++)
            {
                float separation = MathF.Abs(Vec2.Dot(betweenCentres, axes[i]))
                                 - a.ProjectedRadius(axes[i])
                                 - b.ProjectedRadius(axes[i]);

                if (separation > widest) widest = separation;
            }

            return widest > 0f ? widest : 0f;
        }

        /// <summary>
        /// Whether two placed footprints are within <paramref name="metres"/> of
        /// each other, edge to edge.
        /// </summary>
        public static bool Within(in OrientedRect a, in OrientedRect b, float metres) =>
            GapBetween(a, b) <= metres;

        /// <summary>
        /// How much of the smaller of two formations is standing inside the
        /// other, from 0 to 1.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The difference between two regiments whose corners are touching and
        /// two standing in the same field. <see cref="Overlaps"/> answers yes to
        /// both, which makes every glancing brush a collision — and an army
        /// drawn up in line brushes constantly.
        /// </para>
        /// <para>
        /// Exact rather than estimated: the overlapping region of two
        /// rectangles is a convex polygon, found by clipping one against each
        /// edge of the other, and its area comes straight off the shoelace
        /// formula. Measured against the smaller of the two so that a small
        /// regiment wholly inside a large one reads as fully overlapping rather
        /// than as a fraction of the big one.
        /// </para>
        /// </remarks>
        public static float OverlapFraction(in OrientedRect a, in OrientedRect b)
        {
            PlanningProfile.Tally(PlanningProfile.Step.OverlapTest);

            if (!Overlaps(a, b)) return 0f;

            // Two convex quadrilaterals meet in at most an octagon.
            Span<Vec2> polygon = stackalloc Vec2[8];
            Span<Vec2> clipped = stackalloc Vec2[8];

            a.GetCorners(polygon.Slice(0, 4));
            int count = 4;

            Span<Vec2> against = stackalloc Vec2[4];
            b.GetCorners(against);

            for (int edge = 0; edge < 4 && count > 0; edge++)
            {
                Vec2 from = against[edge];
                Vec2 to = against[(edge + 1) & 3];

                count = ClipAgainstEdge(polygon, count, from, to, clipped);
                clipped.Slice(0, count).CopyTo(polygon);
            }

            if (count < 3) return 0f;

            float smaller = MathF.Min(a.Footprint.Area, b.Footprint.Area);
            if (smaller <= 0f) return 0f;

            return Math.Clamp(AreaOf(polygon, count) / smaller, 0f, 1f);
        }

        /// <summary>
        /// Keeps the part of a polygon lying on the inward side of one edge, by
        /// Sutherland–Hodgman.
        /// </summary>
        /// <remarks>
        /// Corners run anticlockwise, so "inside" is the left of the edge and a
        /// point is kept when the cross product is not negative.
        /// </remarks>
        private static int ClipAgainstEdge(
            ReadOnlySpan<Vec2> polygon, int count, Vec2 from, Vec2 to, Span<Vec2> destination)
        {
            Vec2 along = to - from;
            int kept = 0;

            for (int i = 0; i < count; i++)
            {
                Vec2 current = polygon[i];
                Vec2 previous = polygon[(i + count - 1) % count];

                float currentSide = Cross(along, current - from);
                float previousSide = Cross(along, previous - from);

                bool currentIn = currentSide >= 0f;
                bool previousIn = previousSide >= 0f;

                if (currentIn != previousIn)
                {
                    // The edge crosses this boundary, so keep the crossing point.
                    float span = previousSide - currentSide;

                    if (MathF.Abs(span) > Vec2.Epsilon && kept < destination.Length)
                        destination[kept++] = previous + (current - previous) * (previousSide / span);
                }

                if (currentIn && kept < destination.Length)
                    destination[kept++] = current;
            }

            return kept;
        }

        private static float Cross(Vec2 a, Vec2 b) => a.X * b.Y - a.Y * b.X;

        /// <summary>Area of a simple polygon, by the shoelace formula.</summary>
        private static float AreaOf(ReadOnlySpan<Vec2> polygon, int count)
        {
            float twice = 0f;

            for (int i = 0; i < count; i++)
            {
                Vec2 current = polygon[i];
                Vec2 next = polygon[(i + 1) % count];

                twice += current.X * next.Y - next.X * current.Y;
            }

            return MathF.Abs(twice) * 0.5f;
        }

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
