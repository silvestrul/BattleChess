using System;
using System.Linq;
using BattleChess.Contracts;
using BattleChess.Rules;
using Xunit;

namespace BattleChess.Tests.Hex
{
    /// <summary>
    /// The hex grid is an invisible internal tool, but the bridge between it and
    /// continuous world space still has to be exact: path waypoints are produced
    /// here, and a systematic error would bend every route the units walk.
    /// </summary>
    public class HexLayoutTests
    {
        private const float Tolerance = 1e-3f;

        private static readonly HexLayout Layout = HexLayout.FromNeighbourDistance(2f);

        // ---- Construction ---------------------------------------------------

        [Fact]
        public void FromNeighbourDistance_GivesTheRequestedSpacing()
        {
            foreach (float spacing in new[] { 0.5f, 2f, 8f, 37.5f })
            {
                HexLayout layout = HexLayout.FromNeighbourDistance(spacing);
                Assert.Equal(spacing, layout.NeighbourDistance, 3);
            }
        }

        [Fact]
        public void RejectsDegenerateCellSizes()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new HexLayout(0f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new HexLayout(-1f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new HexLayout(float.PositiveInfinity));
            Assert.Throws<ArgumentOutOfRangeException>(() => HexLayout.FromNeighbourDistance(0f));
        }

        [Fact]
        public void CellDimensionsFollowFromTheCornerRadius()
        {
            var layout = new HexLayout(cellSize: 1f);

            Assert.Equal(2f, layout.CellHeight, 3);                    // point to point
            Assert.Equal(MathF.Sqrt(3f), layout.CellWidth, 3);         // flat to flat
            Assert.Equal(MathF.Sqrt(3f), layout.NeighbourDistance, 3);
        }

        // ---- The Y-up convention -------------------------------------------

        [Fact]
        public void Origin_MapsToTheLayoutOrigin()
        {
            Assert.True(Layout.ToWorld(Coord.Zero).ApproximatelyEquals(Vec2.Zero, Tolerance));

            var shifted = HexLayout.FromNeighbourDistance(2f, new Vec2(100f, -50f));
            Assert.True(shifted.ToWorld(Coord.Zero).ApproximatelyEquals(new Vec2(100f, -50f), Tolerance));
        }

        [Fact]
        public void EachHexDirection_PointsWhereItsNameSays()
        {
            // The single most important test in this file. World space is Y-up,
            // and the axial offsets were chosen so that HexDirection.NorthEast
            // genuinely bears north-east once converted. A sign error here would
            // silently mirror the whole battlefield.
            foreach (HexDirection direction in Enum.GetValues(typeof(HexDirection)).Cast<HexDirection>())
            {
                Vec2 offset = Layout.ToWorld(Coord.Zero.Neighbour(direction)) - Layout.ToWorld(Coord.Zero);

                Facing actual = Facing.FromVector(offset);
                Facing expected = Facing.FromHexDirection(direction);

                Assert.True(actual.ApproximatelyEquals(expected, 1e-3f),
                    $"{direction} bore {actual} but should bear {expected}.");
            }
        }

        [Fact]
        public void NorthIsUpAndEastIsRight()
        {
            Vec2 northEast = Layout.ToWorld(Coord.Zero.Neighbour(HexDirection.NorthEast));
            Vec2 southWest = Layout.ToWorld(Coord.Zero.Neighbour(HexDirection.SouthWest));
            Vec2 east = Layout.ToWorld(Coord.Zero.Neighbour(HexDirection.East));

            Assert.True(northEast.Y > 0f, "North-east should have a positive northing.");
            Assert.True(northEast.X > 0f, "North-east should have a positive easting.");
            Assert.True(southWest.Y < 0f, "South-west should have a negative northing.");
            Assert.Equal(0f, east.Y, 3);
            Assert.True(east.X > 0f);
        }

        // ---- Spacing --------------------------------------------------------

        [Fact]
        public void AllSixNeighbours_AreEquidistant()
        {
            // The whole reason for choosing hexes over squares: no diagonals that
            // are secretly longer than orthogonals.
            foreach (Coord centre in HexMath.Disc(Coord.Zero, 4))
            {
                Vec2 centreWorld = Layout.ToWorld(centre);

                foreach (Coord neighbour in HexMath.Neighbours(centre))
                    Assert.Equal(Layout.NeighbourDistance, Vec2.Distance(centreWorld, Layout.ToWorld(neighbour)), 3);
            }
        }

        [Fact]
        public void StraightRuns_ScaleWithHexDistance()
        {
            foreach (HexDirection direction in Enum.GetValues(typeof(HexDirection)).Cast<HexDirection>())
            for (int steps = 1; steps <= 20; steps++)
            {
                Coord target = Coord.Zero + HexMath.Offset(direction) * steps;
                float worldDistance = Vec2.Distance(Layout.ToWorld(Coord.Zero), Layout.ToWorld(target));

                Assert.Equal(steps * Layout.NeighbourDistance, worldDistance, 2);
            }
        }

        [Fact]
        public void DistinctHexes_NeverShareAWorldPosition()
        {
            var seen = HexMath.Disc(Coord.Zero, 6)
                .Select(c => Layout.ToWorld(c))
                .ToList();

            for (int i = 0; i < seen.Count; i++)
            for (int j = i + 1; j < seen.Count; j++)
                Assert.False(seen[i].ApproximatelyEquals(seen[j], 1e-2f), $"{seen[i]} collided with {seen[j]}.");
        }

        // ---- Round trips ----------------------------------------------------

        [Fact]
        public void HexCentres_RoundTripExactly()
        {
            foreach (Coord coord in HexMath.Disc(Coord.Zero, 12))
                Assert.Equal(coord, Layout.ToCoord(Layout.ToWorld(coord)));
        }

        [Fact]
        public void HexCentres_RoundTripUnderAnOffsetOrigin()
        {
            var shifted = HexLayout.FromNeighbourDistance(7.5f, new Vec2(-321.5f, 88.25f));

            foreach (Coord coord in HexMath.Disc(new Coord(30, -12), 8))
                Assert.Equal(coord, shifted.ToCoord(shifted.ToWorld(coord)));
        }

        [Fact]
        public void PointsNearAHexCentre_ResolveToThatHex()
        {
            var rng = new DeterministicRng(2121UL);

            // Anything within the inradius is unambiguously inside the hex.
            float inradius = Layout.NeighbourDistance * 0.5f;

            foreach (Coord coord in HexMath.Disc(Coord.Zero, 5))
            {
                Vec2 centre = Layout.ToWorld(coord);

                for (int i = 0; i < 20; i++)
                {
                    float angle = rng.NextFloat(0f, 2f * MathF.PI);
                    float radius = rng.NextFloat(0f, inradius * 0.95f);
                    var probe = new Vec2(centre.X + radius * MathF.Cos(angle), centre.Y + radius * MathF.Sin(angle));

                    Assert.Equal(coord, Layout.ToCoord(probe));
                }
            }
        }

        [Fact]
        public void EveryWorldPoint_ResolvesToSomeNearbyHex()
        {
            var rng = new DeterministicRng(2222UL);

            for (int i = 0; i < 2_000; i++)
            {
                var point = new Vec2(rng.NextFloat(-500f, 500f), rng.NextFloat(-500f, 500f));

                Coord coord = Layout.ToCoord(point);
                float distanceToCentre = Vec2.Distance(point, Layout.ToWorld(coord));

                // No point can be further from its own hex centre than the
                // corner radius, or the mapping has a hole in it.
                Assert.True(distanceToCentre <= Layout.CellSize + 1e-3f,
                    $"{point} resolved to {coord}, which is {distanceToCentre:0.###} m away.");
            }
        }

        [Fact]
        public void ToCoord_PicksTheNearestHexCentre()
        {
            var rng = new DeterministicRng(2323UL);

            for (int i = 0; i < 500; i++)
            {
                var point = new Vec2(rng.NextFloat(-40f, 40f), rng.NextFloat(-40f, 40f));

                Coord chosen = Layout.ToCoord(point);
                float chosenDistance = Vec2.Distance(point, Layout.ToWorld(chosen));

                foreach (Coord neighbour in HexMath.Neighbours(chosen))
                    Assert.True(Vec2.Distance(point, Layout.ToWorld(neighbour)) >= chosenDistance - 1e-3f,
                        $"{point} was assigned {chosen} but {neighbour} is nearer.");
            }
        }

        [Fact]
        public void FractionalCoord_AgreesWithTheRoundedCoord()
        {
            var rng = new DeterministicRng(2424UL);

            for (int i = 0; i < 500; i++)
            {
                var point = new Vec2(rng.NextFloat(-100f, 100f), rng.NextFloat(-100f, 100f));

                Assert.Equal(Layout.ToCoord(point), HexMath.Round(Layout.ToFractionalCoord(point)));
            }
        }

        [Fact]
        public void FractionalCoord_OfAHexCentre_IsWhole()
        {
            foreach (Coord coord in HexMath.Disc(Coord.Zero, 6))
            {
                FractionalCoord fractional = Layout.ToFractionalCoord(Layout.ToWorld(coord));

                Assert.Equal(coord.Q, fractional.Q, 2);
                Assert.Equal(coord.R, fractional.R, 2);
            }
        }

        // ---- Corners --------------------------------------------------------

        [Fact]
        public void Corners_AreSixPointsAtTheCornerRadius()
        {
            foreach (Coord coord in HexMath.Disc(Coord.Zero, 3))
            {
                Vec2 centre = Layout.ToWorld(coord);
                Vec2[] corners = Layout.GetCorners(coord);

                Assert.Equal(6, corners.Length);

                foreach (Vec2 corner in corners)
                    Assert.Equal(Layout.CellSize, Vec2.Distance(centre, corner), 3);
            }
        }

        [Fact]
        public void Corners_AverageBackToTheCentre()
        {
            var coord = new Coord(2, -3);

            Vec2 sum = Layout.GetCorners(coord).Aggregate(Vec2.Zero, (running, corner) => running + corner);

            Assert.True((sum / 6f).ApproximatelyEquals(Layout.ToWorld(coord), Tolerance));
        }

        [Fact]
        public void AdjacentHexes_ShareExactlyTwoCorners()
        {
            // If neighbouring hexes did not share an edge, the grid would have
            // gaps or overlaps and area-based reasoning over it would be wrong.
            var centre = new Coord(1, 1);
            Vec2[] centreCorners = Layout.GetCorners(centre);

            foreach (Coord neighbour in HexMath.Neighbours(centre))
            {
                Vec2[] neighbourCorners = Layout.GetCorners(neighbour);

                int shared = centreCorners.Count(a => neighbourCorners.Any(b => a.ApproximatelyEquals(b, 1e-2f)));

                Assert.Equal(2, shared);
            }
        }

        [Fact]
        public void Corners_RejectUndersizedBuffers()
        {
            Assert.Throws<ArgumentException>(() =>
            {
                Span<Vec2> tooSmall = new Vec2[5];
                Layout.GetCorners(Coord.Zero, tooSmall);
            });
        }
    }
}
