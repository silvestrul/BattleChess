using System;
using BattleChess.Contracts;
using BattleChess.Rules;
using Xunit;

namespace BattleChess.Tests.World
{
    public class Vec2Tests
    {
        private const float Tolerance = 1e-4f;

        private static void AssertClose(Vec2 expected, Vec2 actual, float tolerance = Tolerance) =>
            Assert.True(expected.ApproximatelyEquals(actual, tolerance), $"Expected {expected}, got {actual}.");

        [Fact]
        public void Arithmetic_Composes()
        {
            var a = new Vec2(3f, -4f);
            var b = new Vec2(-1f, 2f);

            AssertClose(new Vec2(2f, -2f), a + b);
            AssertClose(new Vec2(4f, -6f), a - b);
            AssertClose(new Vec2(-3f, 4f), -a);
            AssertClose(new Vec2(6f, -8f), a * 2f);
            AssertClose(new Vec2(6f, -8f), 2f * a);
            AssertClose(new Vec2(1.5f, -2f), a / 2f);
            AssertClose(Vec2.Zero, a - a);
        }

        [Fact]
        public void Length_UsesPythagoras()
        {
            var v = new Vec2(3f, 4f);

            Assert.Equal(25f, v.LengthSquared, 4);
            Assert.Equal(5f, v.Length, 4);
            Assert.Equal(0f, Vec2.Zero.Length, 4);
        }

        [Fact]
        public void Normalised_ProducesUnitLength()
        {
            var rng = new DeterministicRng(11UL);

            for (int i = 0; i < 500; i++)
            {
                var v = new Vec2(rng.NextFloat(-100f, 100f), rng.NextFloat(-100f, 100f));
                if (v.IsNearZero) continue;

                Assert.Equal(1f, v.Normalised().Length, 3);
            }
        }

        [Fact]
        public void Normalised_PreservesDirection()
        {
            var v = new Vec2(6f, 8f);
            AssertClose(new Vec2(0.6f, 0.8f), v.Normalised());
        }

        [Fact]
        public void Normalised_OfNearZero_IsZero()
        {
            // A unit standing still has no direction; returning zero rather than
            // NaN keeps that case from poisoning downstream movement maths.
            Assert.Equal(Vec2.Zero, Vec2.Zero.Normalised());
            Assert.Equal(Vec2.Zero, new Vec2(1e-9f, 1e-9f).Normalised());
        }

        [Fact]
        public void Dot_IsZeroForPerpendicularVectors()
        {
            var rng = new DeterministicRng(22UL);

            for (int i = 0; i < 200; i++)
            {
                var v = new Vec2(rng.NextFloat(-50f, 50f), rng.NextFloat(-50f, 50f));
                Assert.Equal(0f, Vec2.Dot(v, v.Perpendicular), 3);
            }
        }

        [Fact]
        public void Dot_OfAVectorWithItself_IsItsSquaredLength()
        {
            var v = new Vec2(-7f, 2.5f);
            Assert.Equal(v.LengthSquared, Vec2.Dot(v, v), 3);
        }

        [Fact]
        public void Cross_SignIndicatesTurnDirection()
        {
            // Positive when the second vector lies anticlockwise of the first.
            Assert.True(Vec2.Cross(Vec2.East, Vec2.North) > 0f);
            Assert.True(Vec2.Cross(Vec2.North, Vec2.East) < 0f);
            Assert.Equal(0f, Vec2.Cross(Vec2.East, Vec2.East), 4);
            Assert.Equal(0f, Vec2.Cross(Vec2.East, -Vec2.East), 4);
        }

        [Fact]
        public void Perpendicular_TurnsAQuarterAnticlockwise()
        {
            AssertClose(Vec2.North, Vec2.East.Perpendicular);
            AssertClose(-Vec2.East, Vec2.North.Perpendicular);
        }

        [Fact]
        public void Rotated_PreservesLength()
        {
            var v = new Vec2(3f, -4f);

            for (int degrees = 0; degrees < 360; degrees += 15)
                Assert.Equal(v.Length, v.Rotated(degrees * MathF.PI / 180f).Length, 3);
        }

        [Fact]
        public void Rotated_ByFullTurn_IsIdentity()
        {
            var v = new Vec2(2f, 5f);
            AssertClose(v, v.Rotated(2f * MathF.PI), 1e-3f);
        }

        [Fact]
        public void Rotated_ByQuarterTurn_MatchesPerpendicular()
        {
            var v = new Vec2(1.5f, -0.5f);
            AssertClose(v.Perpendicular, v.Rotated(MathF.PI / 2f), 1e-3f);
        }

        [Fact]
        public void Distance_IsSymmetricAndMatchesLength()
        {
            var a = new Vec2(1f, 2f);
            var b = new Vec2(4f, 6f);

            Assert.Equal(5f, Vec2.Distance(a, b), 3);
            Assert.Equal(Vec2.Distance(a, b), Vec2.Distance(b, a), 4);
            Assert.Equal(25f, Vec2.DistanceSquared(a, b), 3);
        }

        [Fact]
        public void Lerp_HitsBothEndsAndTheMiddle()
        {
            var a = new Vec2(0f, 0f);
            var b = new Vec2(10f, 20f);

            AssertClose(a, Vec2.Lerp(a, b, 0f));
            AssertClose(b, Vec2.Lerp(a, b, 1f));
            AssertClose(new Vec2(5f, 10f), Vec2.Lerp(a, b, 0.5f));
        }

        [Fact]
        public void MoveTowards_StopsExactlyOnTarget_RatherThanOvershooting()
        {
            var from = new Vec2(0f, 0f);
            var to = new Vec2(3f, 4f); // 5 m away

            AssertClose(to, Vec2.MoveTowards(from, to, 5f));
            AssertClose(to, Vec2.MoveTowards(from, to, 100f));
        }

        [Fact]
        public void MoveTowards_TravelsExactlyTheGivenDistance()
        {
            var from = new Vec2(1f, 1f);
            var to = new Vec2(11f, 1f);

            Vec2 stepped = Vec2.MoveTowards(from, to, 2.5f);

            AssertClose(new Vec2(3.5f, 1f), stepped);
            Assert.Equal(2.5f, Vec2.Distance(from, stepped), 3);
        }

        [Fact]
        public void MoveTowards_WithNoBudget_StaysPut()
        {
            var from = new Vec2(2f, 3f);

            AssertClose(from, Vec2.MoveTowards(from, new Vec2(50f, 50f), 0f));
            AssertClose(from, Vec2.MoveTowards(from, new Vec2(50f, 50f), -1f));
        }

        [Fact]
        public void MoveTowards_AccumulatedSteps_ReachTheTargetAndStop()
        {
            // This is exactly how per-tick movement integrates, so it is worth
            // asserting the loop terminates on the target rather than jittering.
            var position = new Vec2(0f, 0f);
            var target = new Vec2(10f, 0f);

            for (int tick = 0; tick < 60; tick++)
                position = Vec2.MoveTowards(position, target, 0.5f);

            AssertClose(target, position);
        }

        [Fact]
        public void ApproximatelyEquals_ToleratesSmallDrift()
        {
            var a = new Vec2(1f, 1f);

            Assert.True(a.ApproximatelyEquals(new Vec2(1f + 1e-7f, 1f)));
            Assert.False(a.ApproximatelyEquals(new Vec2(1.1f, 1f)));
        }

        [Fact]
        public void Equality_IsValueBased()
        {
            var a = new Vec2(1.25f, -3.5f);
            var b = new Vec2(1.25f, -3.5f);
            var c = new Vec2(1.25f, -3.6f);

            Assert.True(a == b);
            Assert.False(a != b);
            Assert.Equal(a.GetHashCode(), b.GetHashCode());
            Assert.True(a != c);
        }
    }
}
