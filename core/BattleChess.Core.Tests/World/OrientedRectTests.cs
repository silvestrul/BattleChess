using System;
using BattleChess.Contracts;
using BattleChess.Rules;
using Xunit;

namespace BattleChess.Tests.World
{
    public class FootprintTests
    {
        [Fact]
        public void HalfExtentsAndAreaFollowFromTheDimensions()
        {
            var footprint = new Footprint(width: 60f, depth: 12f);

            Assert.Equal(30f, footprint.HalfWidth, 4);
            Assert.Equal(6f, footprint.HalfDepth, 4);
            Assert.Equal(720f, footprint.Area, 3);
        }

        [Fact]
        public void BoundingRadius_ReachesTheCorners()
        {
            var footprint = new Footprint(width: 8f, depth: 6f);

            // Half-diagonal of a 8x6 rectangle: sqrt(4^2 + 3^2) = 5.
            Assert.Equal(5f, footprint.BoundingRadius, 4);
        }

        [Fact]
        public void RejectsDegenerateDimensions()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new Footprint(0f, 5f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new Footprint(5f, 0f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new Footprint(-1f, 5f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new Footprint(5f, float.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => new Footprint(float.PositiveInfinity, 5f));
        }
    }

    public class OrientedRectTests
    {
        private const float Tolerance = 1e-3f;

        /// <summary>A regiment in line: 10 m of frontage, 4 m deep.</summary>
        private static readonly Footprint Line = new Footprint(width: 10f, depth: 4f);

        private static OrientedRect At(float x, float y, float facingDegrees = 0f, Footprint? footprint = null) =>
            new OrientedRect(new Vec2(x, y), Facing.FromDegrees(facingDegrees), footprint ?? Line);

        // ---- Frame ----------------------------------------------------------

        [Fact]
        public void ForwardAndRight_AreUnitLengthAndPerpendicular()
        {
            var rng = new DeterministicRng(1212UL);

            for (int i = 0; i < 200; i++)
            {
                OrientedRect rect = At(0f, 0f, rng.NextFloat(-360f, 360f));

                Assert.Equal(1f, rect.Forward.Length, 3);
                Assert.Equal(1f, rect.Right.Length, 3);
                Assert.Equal(0f, Vec2.Dot(rect.Forward, rect.Right), 3);
            }
        }

        [Fact]
        public void FacingEast_PutsFrontageNorthSouth()
        {
            // A unit facing east presents its frontage across the north-south
            // axis; its depth runs east-west. Getting this backwards would make
            // every flank attack land on the wrong side.
            OrientedRect rect = At(0f, 0f, facingDegrees: 0f);

            Assert.True(rect.Forward.ApproximatelyEquals(new Vec2(1f, 0f), Tolerance));
            Assert.True(rect.ContainsPoint(new Vec2(0f, 4.9f)), "Frontage should extend north.");
            Assert.False(rect.ContainsPoint(new Vec2(0f, 5.1f)), "Frontage should stop at half-width.");
            Assert.True(rect.ContainsPoint(new Vec2(1.9f, 0f)), "Depth should extend east.");
            Assert.False(rect.ContainsPoint(new Vec2(2.1f, 0f)), "Depth should stop at half-depth.");
        }

        // ---- Corners --------------------------------------------------------

        [Fact]
        public void Corners_AreAllAtTheBoundingRadius()
        {
            var rng = new DeterministicRng(1313UL);

            for (int i = 0; i < 100; i++)
            {
                OrientedRect rect = At(rng.NextFloat(-50f, 50f), rng.NextFloat(-50f, 50f), rng.NextFloat(-360f, 360f));

                foreach (Vec2 corner in rect.GetCorners())
                    Assert.Equal(rect.Footprint.BoundingRadius, Vec2.Distance(rect.Centre, corner), 3);
            }
        }

        [Fact]
        public void Corners_AverageBackToTheCentre()
        {
            OrientedRect rect = At(7f, -3f, facingDegrees: 37f);

            Vec2 sum = Vec2.Zero;
            foreach (Vec2 corner in rect.GetCorners())
                sum += corner;

            Assert.True((sum / 4f).ApproximatelyEquals(rect.Centre, Tolerance));
        }

        [Fact]
        public void Corners_RejectUndersizedBuffers()
        {
            Assert.Throws<ArgumentException>(() =>
            {
                Span<Vec2> tooSmall = new Vec2[3];
                At(0f, 0f).GetCorners(tooSmall);
            });
        }

        // ---- Point queries --------------------------------------------------

        [Fact]
        public void ContainsPoint_AcceptsTheCentreAndRejectsDistantPoints()
        {
            OrientedRect rect = At(0f, 0f);

            Assert.True(rect.ContainsPoint(Vec2.Zero));
            Assert.False(rect.ContainsPoint(new Vec2(100f, 100f)));
        }

        [Fact]
        public void ContainsPoint_HoldsUnderRotation()
        {
            // The same physical point, described in a rotated frame, must give
            // the same answer.
            var rng = new DeterministicRng(1414UL);

            for (int i = 0; i < 200; i++)
            {
                float degrees = rng.NextFloat(-360f, 360f);
                OrientedRect rect = At(0f, 0f, degrees);

                float alongWidth = rng.NextFloat(-6f, 6f);
                float alongDepth = rng.NextFloat(-3f, 3f);

                Vec2 point = rect.Centre + rect.Right * alongWidth + rect.Forward * alongDepth;

                bool expected = MathF.Abs(alongWidth) <= Line.HalfWidth && MathF.Abs(alongDepth) <= Line.HalfDepth;

                // Skip points sitting right on the boundary, where float error
                // can legitimately fall either way.
                if (MathF.Abs(MathF.Abs(alongWidth) - Line.HalfWidth) < 1e-3f) continue;
                if (MathF.Abs(MathF.Abs(alongDepth) - Line.HalfDepth) < 1e-3f) continue;

                Assert.Equal(expected, rect.ContainsPoint(point));
            }
        }

        [Fact]
        public void ClosestPointTo_ReturnsInteriorPointsUnchanged()
        {
            OrientedRect rect = At(0f, 0f);
            var inside = new Vec2(1f, 2f);

            Assert.True(rect.ClosestPointTo(inside).ApproximatelyEquals(inside, Tolerance));
        }

        [Fact]
        public void ClosestPointTo_ClampsToTheEdge()
        {
            OrientedRect rect = At(0f, 0f); // spans x in [-2,2], y in [-5,5]

            Assert.True(rect.ClosestPointTo(new Vec2(10f, 0f)).ApproximatelyEquals(new Vec2(2f, 0f), Tolerance));
            Assert.True(rect.ClosestPointTo(new Vec2(0f, -20f)).ApproximatelyEquals(new Vec2(0f, -5f), Tolerance));
            Assert.True(rect.ClosestPointTo(new Vec2(10f, 20f)).ApproximatelyEquals(new Vec2(2f, 5f), Tolerance));
        }

        [Fact]
        public void ClosestPointTo_AlwaysLandsOnOrInsideTheRectangle()
        {
            var rng = new DeterministicRng(1515UL);
            OrientedRect rect = At(3f, -2f, facingDegrees: 25f);

            for (int i = 0; i < 500; i++)
            {
                var point = new Vec2(rng.NextFloat(-60f, 60f), rng.NextFloat(-60f, 60f));
                Vec2 closest = rect.ClosestPointTo(point);

                Assert.True(rect.ContainsPoint(closest) || Vec2.Distance(closest, rect.ClosestPointTo(closest)) < 1e-2f,
                    $"Closest point {closest} fell outside the rectangle.");
                Assert.True(Vec2.Distance(point, closest) <= Vec2.Distance(point, rect.Centre) + 1e-3f);
            }
        }

        // ---- Overlap --------------------------------------------------------

        [Fact]
        public void Overlaps_IsTrueForARectangleWithItself()
        {
            OrientedRect rect = At(4f, 4f, facingDegrees: 33f);
            Assert.True(OrientedRect.Overlaps(rect, rect));
        }

        [Fact]
        public void Overlaps_IsFalseForDistantRectangles()
        {
            Assert.False(OrientedRect.Overlaps(At(0f, 0f), At(500f, 500f)));
        }

        [Fact]
        public void Overlaps_IsSymmetric()
        {
            var rng = new DeterministicRng(1616UL);

            for (int i = 0; i < 1_000; i++)
            {
                OrientedRect a = At(rng.NextFloat(-15f, 15f), rng.NextFloat(-15f, 15f), rng.NextFloat(-360f, 360f));
                OrientedRect b = At(rng.NextFloat(-15f, 15f), rng.NextFloat(-15f, 15f), rng.NextFloat(-360f, 360f));

                Assert.Equal(OrientedRect.Overlaps(a, b), OrientedRect.Overlaps(b, a));
            }
        }

        [Fact]
        public void Overlaps_DetectsAHeadOnApproachAtTheRightDistance()
        {
            // Both face east, so their 4 m depths meet along the x axis: they
            // touch when 4 m apart, and overlap any closer.
            OrientedRect a = At(0f, 0f);

            Assert.True(OrientedRect.Overlaps(a, At(3.9f, 0f)));
            Assert.False(OrientedRect.Overlaps(a, At(4.1f, 0f)));
        }

        [Fact]
        public void Overlaps_TreatsFlushContactAsClear()
        {
            // Units drawn up exactly edge to edge are legal, not colliding.
            Assert.False(OrientedRect.Overlaps(At(0f, 0f), At(4f, 0f)));
        }

        [Fact]
        public void Overlaps_DetectsAFlankApproachAtTheRightDistance()
        {
            // Approaching the 10 m frontage instead of the 4 m depth, so contact
            // happens 10 m out rather than 4.
            OrientedRect a = At(0f, 0f);

            Assert.True(OrientedRect.Overlaps(a, At(0f, 9.9f)));
            Assert.False(OrientedRect.Overlaps(a, At(0f, 10.1f)));
        }

        [Fact]
        public void Overlaps_HandlesPerpendicularRectangles()
        {
            // a spans x in [-2,2], y in [-5,5]. b faces north, so it spans
            // x in [-5,5] and y in [centre-2, centre+2].
            OrientedRect a = At(0f, 0f, facingDegrees: 0f);

            Assert.True(OrientedRect.Overlaps(a, At(0f, 4.9f, facingDegrees: 90f)));
            Assert.False(OrientedRect.Overlaps(a, At(0f, 7.1f, facingDegrees: 90f)));
        }

        [Fact]
        public void Overlaps_CatchesTheRotatedCaseABoundingCircleWouldMiss()
        {
            // Two long thin rectangles crossing at right angles overlap at the
            // centre even though neither centre is inside the other.
            var thin = new Footprint(width: 40f, depth: 2f);

            OrientedRect a = At(0f, 0f, facingDegrees: 0f, footprint: thin);
            OrientedRect b = At(0f, 0f, facingDegrees: 90f, footprint: thin);

            Assert.True(OrientedRect.Overlaps(a, b));
        }

        [Fact]
        public void Overlaps_SeparatesTwoDiagonallyOrientedRectangles()
        {
            // Two thin lines at 45 degrees, offset perpendicular to their length:
            // clearly apart, but their axis-aligned bounding boxes overlap
            // heavily. A cheaper AABB test would report a false collision here.
            var thin = new Footprint(width: 20f, depth: 1f);

            OrientedRect a = At(0f, 0f, facingDegrees: 45f, footprint: thin);

            // 10 m along a's facing, which is perpendicular to its 20 m frontage.
            Vec2 offset = a.Forward * 10f;
            var b = new OrientedRect(a.Centre + offset, a.Facing, thin);

            Assert.False(OrientedRect.Overlaps(a, b));

            // Guard the premise: if the bounding boxes did not overlap, this test
            // would pass trivially and prove nothing about oriented collision.
            Assert.True(BoundingBoxesOverlap(a, b), "Test premise broken: the AABBs no longer overlap.");
        }

        private static bool BoundingBoxesOverlap(in OrientedRect a, in OrientedRect b)
        {
            (Vec2 minA, Vec2 maxA) = BoundingBox(a);
            (Vec2 minB, Vec2 maxB) = BoundingBox(b);

            return minA.X <= maxB.X && maxA.X >= minB.X
                && minA.Y <= maxB.Y && maxA.Y >= minB.Y;
        }

        private static (Vec2 Min, Vec2 Max) BoundingBox(in OrientedRect rect)
        {
            Vec2[] corners = rect.GetCorners();

            float minX = corners[0].X, maxX = corners[0].X;
            float minY = corners[0].Y, maxY = corners[0].Y;

            foreach (Vec2 corner in corners)
            {
                minX = MathF.Min(minX, corner.X);
                maxX = MathF.Max(maxX, corner.X);
                minY = MathF.Min(minY, corner.Y);
                maxY = MathF.Max(maxY, corner.Y);
            }

            return (new Vec2(minX, minY), new Vec2(maxX, maxY));
        }

        [Fact]
        public void Overlaps_IsTrueWhenOneRectangleSwallowsAnother()
        {
            OrientedRect big = At(0f, 0f, facingDegrees: 20f, footprint: new Footprint(100f, 100f));
            OrientedRect small = At(3f, -4f, facingDegrees: 70f, footprint: new Footprint(2f, 2f));

            Assert.True(OrientedRect.Overlaps(big, small));
            Assert.True(OrientedRect.Overlaps(small, big));
        }

        // ---- Separation -----------------------------------------------------

        [Fact]
        public void TryGetSeparation_ReportsNoPushWhenAlreadyClear()
        {
            Assert.False(OrientedRect.TryGetSeparation(At(0f, 0f), At(100f, 0f), out Vec2 push));
            Assert.Equal(Vec2.Zero, push);
        }

        [Fact]
        public void TryGetSeparation_PushesOverlappingRectanglesApart()
        {
            var rng = new DeterministicRng(1717UL);
            int tested = 0;

            for (int i = 0; i < 2_000 && tested < 300; i++)
            {
                OrientedRect a = At(rng.NextFloat(-8f, 8f), rng.NextFloat(-8f, 8f), rng.NextFloat(-360f, 360f));
                OrientedRect b = At(rng.NextFloat(-8f, 8f), rng.NextFloat(-8f, 8f), rng.NextFloat(-360f, 360f));

                if (!OrientedRect.TryGetSeparation(a, b, out Vec2 push))
                    continue;

                tested++;

                var moved = new OrientedRect(a.Centre + push, a.Facing, a.Footprint);

                // Applying the push should leave them exactly touching, so any
                // residual overlap must be vanishingly small.
                if (OrientedRect.TryGetSeparation(moved, b, out Vec2 residual))
                    Assert.True(residual.Length < 1e-2f,
                        $"Push {push} left {residual.Length:0.####} m of overlap.");
            }

            Assert.True(tested > 50, $"Only {tested} overlapping pairs were generated; the test is not exercising much.");
        }

        [Fact]
        public void TryGetSeparation_ChoosesTheShortestWayOut()
        {
            // Overlapping slightly along the depth axis: the cheapest escape is
            // straight backwards, not sideways along the much longer frontage.
            OrientedRect a = At(0f, 0f);
            OrientedRect b = At(3.5f, 0f);

            Assert.True(OrientedRect.TryGetSeparation(a, b, out Vec2 push));

            Assert.Equal(0.5f, push.Length, 3);
            Assert.True(push.X < 0f, "Should be pushed west, away from b.");
            Assert.Equal(0f, push.Y, 3);
        }

        [Fact]
        public void TryGetSeparation_PushesAwayFromTheOtherRectangle()
        {
            var rng = new DeterministicRng(1818UL);
            int tested = 0;

            for (int i = 0; i < 2_000 && tested < 200; i++)
            {
                OrientedRect a = At(rng.NextFloat(-6f, 6f), rng.NextFloat(-6f, 6f), rng.NextFloat(-360f, 360f));
                OrientedRect b = At(rng.NextFloat(-6f, 6f), rng.NextFloat(-6f, 6f), rng.NextFloat(-360f, 360f));

                if (a.Centre.ApproximatelyEquals(b.Centre, 1e-2f)) continue;
                if (!OrientedRect.TryGetSeparation(a, b, out Vec2 push)) continue;

                tested++;

                float before = Vec2.Distance(a.Centre, b.Centre);
                float after = Vec2.Distance(a.Centre + push, b.Centre);

                Assert.True(after >= before - 1e-3f, $"Push {push} moved a toward b.");
            }

            Assert.True(tested > 50, $"Only {tested} overlapping pairs were generated.");
        }

        [Fact]
        public void TryGetSeparation_IsDeterministicForCoincidentCentres()
        {
            OrientedRect a = At(5f, 5f, facingDegrees: 15f);
            OrientedRect b = At(5f, 5f, facingDegrees: 75f);

            Assert.True(OrientedRect.TryGetSeparation(a, b, out Vec2 first));
            Assert.True(OrientedRect.TryGetSeparation(a, b, out Vec2 second));

            Assert.Equal(first, second);
            Assert.False(first.IsNearZero, "A push out of a total overlap must be non-zero.");
        }

        [Fact]
        public void TryGetSeparation_AgreesWithOverlaps()
        {
            var rng = new DeterministicRng(1919UL);

            for (int i = 0; i < 2_000; i++)
            {
                OrientedRect a = At(rng.NextFloat(-12f, 12f), rng.NextFloat(-12f, 12f), rng.NextFloat(-360f, 360f));
                OrientedRect b = At(rng.NextFloat(-12f, 12f), rng.NextFloat(-12f, 12f), rng.NextFloat(-360f, 360f));

                bool overlaps = OrientedRect.Overlaps(a, b);
                bool separates = OrientedRect.TryGetSeparation(a, b, out _);

                Assert.Equal(overlaps, separates);
            }
        }
    }
}
