using System;
using System.Linq;
using BattleChess.Contracts;
using BattleChess.Rules;
using Xunit;

namespace BattleChess.Tests.World
{
    public class FacingTests
    {
        private const float Tolerance = 1e-4f;

        [Fact]
        public void Construction_NormalisesIntoHalfOpenRange()
        {
            var rng = new DeterministicRng(33UL);

            for (int i = 0; i < 2_000; i++)
            {
                float raw = rng.NextFloat(-50f, 50f);
                Facing facing = Facing.FromRadians(raw);

                Assert.True(facing.Radians >= -MathF.PI, $"{facing.Radians} below -π.");
                Assert.True(facing.Radians < MathF.PI, $"{facing.Radians} at or above π.");
            }
        }

        [Fact]
        public void Construction_WrapsFullTurnsToTheSameBearing()
        {
            for (int turns = -3; turns <= 3; turns++)
            {
                Facing wrapped = Facing.FromDegrees(45f + turns * 360f);
                Assert.True(wrapped.ApproximatelyEquals(Facing.FromDegrees(45f), Tolerance), $"{turns} turns gave {wrapped}.");
            }
        }

        [Fact]
        public void Construction_RejectsNonFiniteAngles()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Facing.FromRadians(float.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => Facing.FromRadians(float.PositiveInfinity));
        }

        [Fact]
        public void CardinalBearings_PointWhereTheirNamesSay()
        {
            // World space is Y-up: +X east, +Y north.
            Assert.True(Facing.East.ToVector().ApproximatelyEquals(new Vec2(1f, 0f), Tolerance));
            Assert.True(Facing.North.ToVector().ApproximatelyEquals(new Vec2(0f, 1f), Tolerance));
            Assert.True(Facing.West.ToVector().ApproximatelyEquals(new Vec2(-1f, 0f), Tolerance));
            Assert.True(Facing.South.ToVector().ApproximatelyEquals(new Vec2(0f, -1f), Tolerance));
        }

        [Fact]
        public void ToVector_IsAlwaysUnitLength()
        {
            var rng = new DeterministicRng(44UL);

            for (int i = 0; i < 500; i++)
                Assert.Equal(1f, Facing.FromRadians(rng.NextFloat(-20f, 20f)).ToVector().Length, 3);
        }

        [Fact]
        public void FromVector_AndToVector_RoundTrip()
        {
            var rng = new DeterministicRng(55UL);

            for (int i = 0; i < 500; i++)
            {
                var direction = new Vec2(rng.NextFloat(-100f, 100f), rng.NextFloat(-100f, 100f));
                if (direction.IsNearZero) continue;

                Facing facing = Facing.FromVector(direction);

                Assert.True(
                    facing.ToVector().ApproximatelyEquals(direction.Normalised(), 1e-3f),
                    $"{direction} round-tripped to {facing.ToVector()}.");
            }
        }

        [Fact]
        public void FromVector_OfZero_IsStable()
        {
            Assert.Equal(Facing.East, Facing.FromVector(Vec2.Zero));
        }

        [Fact]
        public void Towards_PointsFromOnePlaceToAnother()
        {
            Assert.True(Facing.Towards(new Vec2(5f, 5f), new Vec2(10f, 5f)).ApproximatelyEquals(Facing.East, Tolerance));
            Assert.True(Facing.Towards(new Vec2(5f, 5f), new Vec2(5f, 9f)).ApproximatelyEquals(Facing.North, Tolerance));
        }

        [Fact]
        public void RightVector_IsPerpendicularAndClockwise()
        {
            var rng = new DeterministicRng(66UL);

            for (int i = 0; i < 200; i++)
            {
                Facing facing = Facing.FromRadians(rng.NextFloat(-10f, 10f));

                Vec2 forward = facing.ToVector();
                Vec2 right = facing.RightVector();

                Assert.Equal(1f, right.Length, 3);
                Assert.Equal(0f, Vec2.Dot(forward, right), 3);

                // Right must be clockwise of forward, not anticlockwise.
                Assert.True(Vec2.Cross(forward, right) < 0f, $"Right vector is on the wrong side for {facing}.");
            }
        }

        [Fact]
        public void RightVector_FacingNorth_PointsEast()
        {
            Assert.True(Facing.North.RightVector().ApproximatelyEquals(new Vec2(1f, 0f), Tolerance));
            Assert.True(Facing.East.RightVector().ApproximatelyEquals(new Vec2(0f, -1f), Tolerance));
        }

        [Fact]
        public void Opposite_IsSelfInverseAndHalfATurnAway()
        {
            var rng = new DeterministicRng(77UL);

            for (int i = 0; i < 200; i++)
            {
                Facing facing = Facing.FromRadians(rng.NextFloat(-10f, 10f));

                Assert.True(facing.ApproximatelyEquals(facing.Opposite().Opposite(), 1e-3f));
                Assert.Equal(MathF.PI, Facing.AbsoluteDelta(facing, facing.Opposite()), 3);
            }
        }

        [Fact]
        public void SignedDelta_TakesTheShorterWayRound()
        {
            // 350° to 10° is a 20° turn anticlockwise, not 340° clockwise.
            float delta = Facing.SignedDelta(Facing.FromDegrees(350f), Facing.FromDegrees(10f));
            Assert.Equal(20f, delta * 180f / MathF.PI, 2);

            float reverse = Facing.SignedDelta(Facing.FromDegrees(10f), Facing.FromDegrees(350f));
            Assert.Equal(-20f, reverse * 180f / MathF.PI, 2);
        }

        [Fact]
        public void SignedDelta_IsAntisymmetric()
        {
            var rng = new DeterministicRng(88UL);

            for (int i = 0; i < 500; i++)
            {
                Facing a = Facing.FromRadians(rng.NextFloat(-10f, 10f));
                Facing b = Facing.FromRadians(rng.NextFloat(-10f, 10f));

                float forward = Facing.SignedDelta(a, b);
                float backward = Facing.SignedDelta(b, a);

                // Exactly π is its own negation under this wrapping, so allow it.
                if (MathF.Abs(MathF.Abs(forward) - MathF.PI) < 1e-3f) continue;

                Assert.Equal(forward, -backward, 3);
            }
        }

        [Fact]
        public void SignedDelta_IsAlwaysWithinHalfATurn()
        {
            var rng = new DeterministicRng(99UL);

            for (int i = 0; i < 1_000; i++)
            {
                Facing a = Facing.FromRadians(rng.NextFloat(-20f, 20f));
                Facing b = Facing.FromRadians(rng.NextFloat(-20f, 20f));

                Assert.InRange(Facing.SignedDelta(a, b), -MathF.PI - 1e-4f, MathF.PI + 1e-4f);
            }
        }

        [Fact]
        public void AbsoluteDelta_IsSymmetricAndZeroForIdenticalBearings()
        {
            var rng = new DeterministicRng(101UL);

            for (int i = 0; i < 300; i++)
            {
                Facing a = Facing.FromRadians(rng.NextFloat(-10f, 10f));
                Facing b = Facing.FromRadians(rng.NextFloat(-10f, 10f));

                Assert.Equal(Facing.AbsoluteDelta(a, b), Facing.AbsoluteDelta(b, a), 4);
                Assert.InRange(Facing.AbsoluteDelta(a, b), 0f, MathF.PI + 1e-4f);
                Assert.Equal(0f, Facing.AbsoluteDelta(a, a), 4);
            }
        }

        [Fact]
        public void AbsoluteDelta_ExpressesFlankingAngles()
        {
            // The eventual flanking rule reads straight off this: head-on is 0,
            // a clean flank is a quarter turn, from behind is a half turn.
            Facing defender = Facing.North;

            Assert.Equal(0f, Facing.AbsoluteDelta(defender, Facing.North) * 180f / MathF.PI, 2);
            Assert.Equal(90f, Facing.AbsoluteDelta(defender, Facing.East) * 180f / MathF.PI, 2);
            Assert.Equal(90f, Facing.AbsoluteDelta(defender, Facing.West) * 180f / MathF.PI, 2);
            Assert.Equal(180f, Facing.AbsoluteDelta(defender, Facing.South) * 180f / MathF.PI, 2);
        }

        [Fact]
        public void RotateTowards_StopsExactlyOnTarget()
        {
            Facing from = Facing.FromDegrees(0f);
            Facing to = Facing.FromDegrees(30f);

            Assert.True(Facing.RotateTowards(from, to, 30f * MathF.PI / 180f).ApproximatelyEquals(to, 1e-3f));
            Assert.True(Facing.RotateTowards(from, to, MathF.PI).ApproximatelyEquals(to, 1e-3f));
        }

        [Fact]
        public void RotateTowards_TurnsTheShortWay()
        {
            Facing from = Facing.FromDegrees(350f);
            Facing to = Facing.FromDegrees(10f);

            Facing stepped = Facing.RotateTowards(from, to, 5f * MathF.PI / 180f);

            Assert.True(stepped.ApproximatelyEquals(Facing.FromDegrees(355f), 1e-3f), $"Turned to {stepped}.");
        }

        [Fact]
        public void RotateTowards_WithNoBudget_StaysPut()
        {
            Facing from = Facing.FromDegrees(42f);
            Assert.Equal(from, Facing.RotateTowards(from, Facing.North, 0f));
        }

        [Fact]
        public void RotateTowards_AccumulatedSteps_SettleOnTheTarget()
        {
            Facing current = Facing.FromDegrees(0f);
            Facing target = Facing.FromDegrees(170f);
            float step = 10f * MathF.PI / 180f;

            for (int tick = 0; tick < 60; tick++)
                current = Facing.RotateTowards(current, target, step);

            Assert.True(current.ApproximatelyEquals(target, 1e-3f), $"Settled on {current}.");
        }

        [Fact]
        public void FromHexDirection_MatchesTheHexDirectionBearings()
        {
            Assert.True(Facing.FromHexDirection(HexDirection.East).ApproximatelyEquals(Facing.FromDegrees(0f), Tolerance));
            Assert.True(Facing.FromHexDirection(HexDirection.NorthEast).ApproximatelyEquals(Facing.FromDegrees(60f), Tolerance));
            Assert.True(Facing.FromHexDirection(HexDirection.NorthWest).ApproximatelyEquals(Facing.FromDegrees(120f), Tolerance));
            Assert.True(Facing.FromHexDirection(HexDirection.West).ApproximatelyEquals(Facing.FromDegrees(180f), Tolerance));
            Assert.True(Facing.FromHexDirection(HexDirection.SouthWest).ApproximatelyEquals(Facing.FromDegrees(240f), Tolerance));
            Assert.True(Facing.FromHexDirection(HexDirection.SouthEast).ApproximatelyEquals(Facing.FromDegrees(300f), Tolerance));
        }

        [Fact]
        public void FromHexDirection_OppositesAgreeWithHexMath()
        {
            foreach (HexDirection direction in Enum.GetValues(typeof(HexDirection)).Cast<HexDirection>())
            {
                Facing fromHex = Facing.FromHexDirection(HexMath.Opposite(direction));
                Facing fromAngle = Facing.FromHexDirection(direction).Opposite();

                Assert.True(fromHex.ApproximatelyEquals(fromAngle, 1e-3f), $"{direction} disagreed.");
            }
        }
    }
}
