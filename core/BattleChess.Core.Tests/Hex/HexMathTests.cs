using System;
using System.Collections.Generic;
using System.Linq;
using BattleChess.Contracts;
using BattleChess.Rules;
using Xunit;

namespace BattleChess.Tests.Hex
{
    /// <summary>
    /// Property-based coverage of the hex primitives. Everything downstream —
    /// pathfinding, line of sight, flanking, artillery — inherits any bug in
    /// here, so these are deliberately exhaustive over small neighbourhoods
    /// rather than spot-checked.
    /// </summary>
    public class HexMathTests
    {
        /// <summary>Every coord within the given radius of the origin.</summary>
        private static IEnumerable<Coord> Sample(int radius = 8) => HexMath.Disc(Coord.Zero, radius);

        private static IEnumerable<Coord> RandomCoords(int count, int spread = 500)
        {
            var rng = new DeterministicRng(20260804UL);
            for (int i = 0; i < count; i++)
                yield return new Coord(rng.NextInt(-spread, spread), rng.NextInt(-spread, spread));
        }

        // ---- Cube invariant -------------------------------------------------

        [Fact]
        public void S_IsAlwaysTheNegatedSumOfQAndR()
        {
            foreach (Coord c in Sample())
                Assert.Equal(0, c.Q + c.R + c.S);
        }

        // ---- Distance -------------------------------------------------------

        [Fact]
        public void Distance_ToSelf_IsZero()
        {
            foreach (Coord c in Sample())
                Assert.Equal(0, Coord.Distance(c, c));
        }

        [Fact]
        public void Distance_IsSymmetric()
        {
            var coords = Sample(5).ToArray();

            foreach (Coord a in coords)
            foreach (Coord b in coords)
                Assert.Equal(Coord.Distance(a, b), Coord.Distance(b, a));
        }

        [Fact]
        public void Distance_SatisfiesTriangleInequality()
        {
            var coords = Sample(4).ToArray();

            foreach (Coord a in coords)
            foreach (Coord b in coords)
            foreach (Coord c in coords)
                Assert.True(
                    Coord.Distance(a, c) <= Coord.Distance(a, b) + Coord.Distance(b, c),
                    $"Triangle inequality broken for {a}, {b}, {c}.");
        }

        [Fact]
        public void Distance_IsTranslationInvariant()
        {
            var offset = new Coord(17, -23);

            foreach (Coord a in Sample(5))
            foreach (Coord b in Sample(3))
                Assert.Equal(Coord.Distance(a, b), Coord.Distance(a + offset, b + offset));
        }

        // ---- Neighbours -----------------------------------------------------

        [Fact]
        public void Neighbours_AreAllExactlyOneAway()
        {
            foreach (Coord c in Sample(3))
            foreach (Coord n in HexMath.Neighbours(c))
                Assert.Equal(1, Coord.Distance(c, n));
        }

        [Fact]
        public void Neighbours_AreSixDistinctHexes()
        {
            foreach (Coord c in Sample(3))
                Assert.Equal(6, HexMath.Neighbours(c).Distinct().Count());
        }

        [Fact]
        public void Neighbour_RoundTripsThroughOppositeDirection()
        {
            foreach (Coord c in Sample(3))
            foreach (HexDirection d in Enum.GetValues(typeof(HexDirection)).Cast<HexDirection>())
            {
                Coord there = c.Neighbour(d);
                Coord back = there.Neighbour(HexMath.Opposite(d));
                Assert.Equal(c, back);
            }
        }

        [Fact]
        public void Neighbours_SpanOverload_MatchesAllocatingOverload()
        {
            Span<Coord> buffer = stackalloc Coord[6];

            foreach (Coord c in Sample(3))
            {
                HexMath.Neighbours(c, buffer);
                Coord[] expected = HexMath.Neighbours(c);

                for (int i = 0; i < 6; i++)
                    Assert.Equal(expected[i], buffer[i]);
            }
        }

        // ---- Rings and discs ------------------------------------------------

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(7)]
        [InlineData(30)]
        public void Ring_HasSixTimesRadiusMembers(int radius)
        {
            List<Coord> ring = HexMath.Ring(new Coord(3, -4), radius);
            Assert.Equal(radius == 0 ? 1 : radius * 6, ring.Count);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(9)]
        public void Ring_MembersAreAllAtExactlyThatRadius(int radius)
        {
            var centre = new Coord(-6, 2);

            foreach (Coord c in HexMath.Ring(centre, radius))
                Assert.Equal(radius, Coord.Distance(centre, c));
        }

        [Theory]
        [InlineData(1)]
        [InlineData(4)]
        [InlineData(12)]
        public void Ring_MembersAreDistinct(int radius)
        {
            List<Coord> ring = HexMath.Ring(Coord.Zero, radius);
            Assert.Equal(ring.Count, ring.Distinct().Count());
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(5)]
        [InlineData(20)]
        public void Disc_CountMatchesClosedForm(int radius)
        {
            Assert.Equal(HexMath.HexCount(radius), HexMath.Disc(new Coord(2, 2), radius).Count);
            Assert.Equal(1 + 3 * radius * (radius + 1), HexMath.HexCount(radius));
        }

        [Theory]
        [InlineData(1)]
        [InlineData(6)]
        public void Disc_IsExactlyTheUnionOfItsRings(int radius)
        {
            var centre = new Coord(4, -1);

            var fromDisc = HexMath.Disc(centre, radius).ToHashSet();
            var fromRings = Enumerable.Range(0, radius + 1)
                .SelectMany(r => HexMath.Ring(centre, r))
                .ToHashSet();

            Assert.Equal(fromRings, fromDisc);
        }

        [Theory]
        [InlineData(3)]
        [InlineData(8)]
        public void Disc_ContainsExactlyTheHexesWithinRadius(int radius)
        {
            var centre = new Coord(-2, 5);
            var disc = HexMath.Disc(centre, radius).ToHashSet();

            foreach (Coord c in HexMath.Disc(centre, radius + 3))
                Assert.Equal(Coord.Distance(centre, c) <= radius, disc.Contains(c));
        }

        [Fact]
        public void Disc_RejectsNegativeRadius()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => HexMath.Disc(Coord.Zero, -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => HexMath.Ring(Coord.Zero, -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => HexMath.HexCount(-1));
        }

        // ---- Lines ----------------------------------------------------------

        [Fact]
        public void Line_StartsAndEndsAtItsEndpoints()
        {
            foreach (Coord b in Sample(6))
            {
                var a = new Coord(1, 1);
                List<Coord> line = HexMath.Line(a, b);

                Assert.Equal(a, line[0]);
                Assert.Equal(b, line[^1]);
            }
        }

        [Fact]
        public void Line_HasOneMoreHexThanTheDistance()
        {
            foreach (Coord b in Sample(6))
            {
                var a = new Coord(-3, 2);
                Assert.Equal(Coord.Distance(a, b) + 1, HexMath.Line(a, b).Count);
            }
        }

        [Fact]
        public void Line_StepsThroughAdjacentHexesOnly()
        {
            foreach (Coord b in Sample(7))
            {
                var a = new Coord(2, -5);
                List<Coord> line = HexMath.Line(a, b);

                for (int i = 1; i < line.Count; i++)
                    Assert.Equal(1, Coord.Distance(line[i - 1], line[i]));
            }
        }

        [Fact]
        public void Line_IsSymmetric()
        {
            // Line of sight must not depend on who is looking. If this ever
            // fails, one unit can see another that cannot see it back.
            foreach (Coord b in Sample(9))
            {
                var a = new Coord(-4, 6);

                List<Coord> forward = HexMath.Line(a, b);
                List<Coord> backward = HexMath.Line(b, a);
                backward.Reverse();

                Assert.Equal(forward, backward);
            }
        }

        [Fact]
        public void Line_OverLongDistances_RemainsWellFormed()
        {
            foreach (Coord b in RandomCoords(200))
            {
                var a = new Coord(0, 0);
                List<Coord> line = HexMath.Line(a, b);

                Assert.Equal(a, line[0]);
                Assert.Equal(b, line[^1]);
                Assert.Equal(Coord.Distance(a, b) + 1, line.Count);

                for (int i = 1; i < line.Count; i++)
                    Assert.Equal(1, Coord.Distance(line[i - 1], line[i]));
            }
        }

        [Fact]
        public void Round_LandsOnAValidCubeCoordinate()
        {
            var rng = new DeterministicRng(4242UL);

            for (int i = 0; i < 5_000; i++)
            {
                var fractional = new FractionalCoord(rng.NextFloat(-50f, 50f), rng.NextFloat(-50f, 50f));
                Coord rounded = HexMath.Round(fractional);

                Assert.Equal(0, rounded.Q + rounded.R + rounded.S);
            }
        }

        // ---- Rotation and facing -------------------------------------------

        [Fact]
        public void Rotate_SixTimes_IsTheIdentity()
        {
            foreach (Coord c in Sample(5))
            {
                Coord rotated = c;
                for (int i = 0; i < 6; i++)
                    rotated = HexMath.Rotate(rotated);

                Assert.Equal(c, rotated);
            }
        }

        [Fact]
        public void Rotate_AndRotateInverse_CancelOut()
        {
            foreach (Coord c in Sample(5))
                Assert.Equal(c, HexMath.RotateInverse(HexMath.Rotate(c)));
        }

        [Fact]
        public void Rotate_PreservesDistanceFromOrigin()
        {
            foreach (Coord c in Sample(6))
                Assert.Equal(c.Length, HexMath.Rotate(c).Length);
        }

        [Fact]
        public void RotateAround_PreservesDistanceFromCentre()
        {
            var centre = new Coord(7, -3);

            foreach (Coord c in Sample(5))
            for (int steps = -8; steps <= 8; steps++)
                Assert.Equal(
                    Coord.Distance(centre, c),
                    Coord.Distance(centre, HexMath.RotateAround(c, centre, steps)));
        }

        [Fact]
        public void RotateAround_BySixSteps_IsTheIdentity()
        {
            var centre = new Coord(-2, 9);

            foreach (Coord c in Sample(4))
            {
                Assert.Equal(c, HexMath.RotateAround(c, centre, 6));
                Assert.Equal(c, HexMath.RotateAround(c, centre, 0));
                Assert.Equal(c, HexMath.RotateAround(c, centre, -6));
            }
        }

        [Fact]
        public void Opposite_IsSelfInverseAndThreeStepsAway()
        {
            foreach (HexDirection d in Enum.GetValues(typeof(HexDirection)).Cast<HexDirection>())
            {
                Assert.Equal(d, HexMath.Opposite(HexMath.Opposite(d)));
                Assert.Equal(3, HexMath.TurnsBetween(d, HexMath.Opposite(d)));
                Assert.Equal(Coord.Zero, HexMath.Offset(d) + HexMath.Offset(HexMath.Opposite(d)));
            }
        }

        [Fact]
        public void TurnsBetween_IsSymmetricAndBounded()
        {
            var directions = Enum.GetValues(typeof(HexDirection)).Cast<HexDirection>().ToArray();

            foreach (HexDirection a in directions)
            foreach (HexDirection b in directions)
            {
                int turns = HexMath.TurnsBetween(a, b);

                Assert.Equal(turns, HexMath.TurnsBetween(b, a));
                Assert.InRange(turns, 0, 3);
                Assert.Equal(a == b, turns == 0);
            }
        }

        [Fact]
        public void DirectionTo_RecoversEachDirectionAlongItsSpoke()
        {
            foreach (HexDirection d in Enum.GetValues(typeof(HexDirection)).Cast<HexDirection>())
            for (int k = 1; k <= 20; k++)
                Assert.Equal(d, HexMath.DirectionTo(Coord.Zero, HexMath.Offset(d) * k));
        }

        [Fact]
        public void DirectionTo_IsTranslationInvariant()
        {
            var offset = new Coord(-11, 4);

            foreach (Coord target in Sample(6))
            {
                if (target == Coord.Zero) continue;

                Assert.Equal(
                    HexMath.DirectionTo(Coord.Zero, target),
                    HexMath.DirectionTo(offset, target + offset));
            }
        }

        [Fact]
        public void DirectionTo_ForZeroVector_IsStable()
        {
            Assert.Equal(HexDirection.East, HexMath.DirectionTo(Coord.Zero, Coord.Zero));
            Assert.Equal(HexDirection.East, HexMath.DirectionTo(new Coord(5, 5), new Coord(5, 5)));
        }

        [Fact]
        public void Offset_RejectsDirectionsOutsideTheSix()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => HexMath.Offset((HexDirection)6));
            Assert.Throws<ArgumentOutOfRangeException>(() => HexMath.Offset((HexDirection)(-1)));
        }

        // ---- Coord plumbing -------------------------------------------------

        [Fact]
        public void Equality_AndHashing_AgreeWithValueSemantics()
        {
            var a = new Coord(3, -7);
            var b = new Coord(3, -7);
            var c = new Coord(3, -6);

            Assert.True(a == b);
            Assert.False(a != b);
            Assert.True(a.Equals(b));
            Assert.Equal(a.GetHashCode(), b.GetHashCode());

            Assert.True(a != c);
            Assert.False(a.Equals(c));
        }

        [Fact]
        public void CompareTo_GivesAStableTotalOrder()
        {
            var coords = Sample(4).ToList();
            coords.Sort();

            for (int i = 1; i < coords.Count; i++)
                Assert.True(coords[i - 1].CompareTo(coords[i]) < 0, "Sort order is not strict.");
        }

        [Fact]
        public void ArithmeticOperators_Compose()
        {
            var a = new Coord(2, -3);
            var b = new Coord(-5, 1);

            Assert.Equal(new Coord(-3, -2), a + b);
            Assert.Equal(new Coord(7, -4), a - b);
            Assert.Equal(new Coord(-2, 3), -a);
            Assert.Equal(new Coord(6, -9), a * 3);
            Assert.Equal(new Coord(6, -9), 3 * a);
            Assert.Equal(Coord.Zero, a - a);
        }
    }
}
