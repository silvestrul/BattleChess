using BattleChess.Contracts;
using Xunit;

namespace BattleChess.Tests.World
{
    /// <summary>
    /// How much of one regiment is standing inside another.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The crowding rules are built on top of this number and cannot be sounder
    /// than it is. "Ignore contact under five percent" is what lets an army form
    /// a line without every neighbour shoving its neighbour, and "a friend is in
    /// the way" is what makes a regiment go round one — both read the same
    /// fraction, so a wrong answer here shows up in play as regiments either
    /// jammed against each other or walking through each other.
    /// </para>
    /// <para>
    /// Worth testing directly rather than through a battle: the clipping is
    /// exact arithmetic with a right answer, and a scenario test that fails
    /// because of it would blame the movement rules instead.
    /// </para>
    /// </remarks>
    public sealed class OverlapGeometryTests
    {
        /// <summary>The shape every regiment wears: forty metres of frontage, six deep.</summary>
        private static readonly Footprint Line = new Footprint(40f, 6f);

        private static OrientedRect At(float x, float y, Facing facing) =>
            new OrientedRect(new Vec2(x, y), facing, Line);

        // ---- The two ends of the scale ----------------------------------------

        [Fact]
        public void ARegimentStandingExactlyOnAnotherIsWhollyInsideIt()
        {
            float fraction = OrientedRect.OverlapFraction(At(0f, 0f, Facing.East), At(0f, 0f, Facing.East));

            Assert.True(fraction > 0.99f, $"Same ground, same bearing: expected all of it, got {fraction:P1}.");
        }

        [Fact]
        public void RegimentsNowhereNearEachOtherOverlapNotAtAll()
        {
            float fraction = OrientedRect.OverlapFraction(At(0f, 0f, Facing.East), At(500f, 500f, Facing.East));

            Assert.Equal(0f, fraction);
        }

        [Fact]
        public void DrawnUpFlushAgainstEachOtherIsNotOverlappingAtAll()
        {
            // Shoulder to shoulder: forty metres of frontage each, centres forty
            // apart, so the edges meet exactly. A line is made of these, and if
            // touching counted as overlapping no line could ever form.
            float fraction = OrientedRect.OverlapFraction(At(0f, 0f, Facing.East), At(0f, 40f, Facing.East));

            Assert.Equal(0f, fraction);
        }

        // ---- Known quantities --------------------------------------------------

        [Fact]
        public void HalfInIsMeasuredAsHalf()
        {
            // Six metres deep, centres three apart along the depth axis: exactly
            // half the ground is shared.
            float fraction = OrientedRect.OverlapFraction(At(0f, 0f, Facing.East), At(3f, 0f, Facing.East));

            Assert.InRange(fraction, 0.48f, 0.52f);
        }

        [Fact]
        public void TwoLinesCrossedAtRightAnglesShareTheSquareWhereTheyMeet()
        {
            // A six-by-six square of shared ground out of a two-hundred-and-forty
            // square metre regiment — fifteen percent.
            float fraction = OrientedRect.OverlapFraction(At(0f, 0f, Facing.East), At(0f, 0f, Facing.North));

            Assert.InRange(fraction, 0.14f, 0.16f);
        }

        [Fact]
        public void ABrushOfOneMetreAlongTheFlankIsWellUnderTheToleranceThatIsIgnored()
        {
            // Side by side, overlapping by a single metre of frontage. This is
            // the case the five percent tolerance exists for: regiments dressing
            // a line touch constantly and must not treat it as a collision.
            float fraction = OrientedRect.OverlapFraction(At(0f, 0f, Facing.East), At(0f, 39f, Facing.East));

            Assert.True(fraction < 0.05f,
                $"One metre of frontage shared should be a graze, not a collision — it reads {fraction:P1}.");

            Assert.True(fraction > 0f, "But it is still contact, and the number should say so.");
        }

        [Fact]
        public void HalfTheFrontageSharedIsFarPastAnythingIgnorable()
        {
            float fraction = OrientedRect.OverlapFraction(At(0f, 0f, Facing.East), At(0f, 20f, Facing.East));

            Assert.True(fraction > 0.05f,
                $"Twenty metres of shared frontage is two regiments standing in each other, not a graze. " +
                $"It reads {fraction:P1}.");
        }

        // ---- Properties the rules rely on --------------------------------------

        [Fact]
        public void ItDoesNotMatterWhichRegimentIsAskedAbout()
        {
            OrientedRect a = At(0f, 0f, Facing.East);
            OrientedRect b = At(2f, 14f, Facing.FromDegrees(35f));

            float forwards = OrientedRect.OverlapFraction(a, b);
            float backwards = OrientedRect.OverlapFraction(b, a);

            Assert.True(forwards > 0f, "The two were placed to overlap; if they do not the test proves nothing.");
            Assert.Equal(forwards, backwards, 4);
        }

        [Fact]
        public void ASmallRegimentInsideALargeOneReadsAsWhollyInsideIt()
        {
            // Measured against the smaller of the two, deliberately. A scout
            // swallowed by a battle line is completely in the way, even though it
            // covers a fraction of the line's ground.
            var large = new OrientedRect(Vec2.Zero, Facing.East, new Footprint(120f, 30f));
            var small = new OrientedRect(Vec2.Zero, Facing.East, new Footprint(20f, 4f));

            float fraction = OrientedRect.OverlapFraction(large, small);

            Assert.True(fraction > 0.99f,
                $"The small one is entirely inside the large one, so it is entirely in the way: {fraction:P1}.");
        }

        [Fact]
        public void OverlapNeverExceedsAllOfIt()
        {
            for (int degrees = 0; degrees < 360; degrees += 15)
            {
                OrientedRect a = At(0f, 0f, Facing.East);
                OrientedRect b = At(1f, 1f, Facing.FromDegrees(degrees));

                float fraction = OrientedRect.OverlapFraction(a, b);

                Assert.InRange(fraction, 0f, 1f);
            }
        }

        [Fact]
        public void AnythingThatOverlapsAtAllReportsSomeOverlap()
        {
            // The two answers must agree. A pair that Overlaps says yes to but
            // OverlapFraction calls zero would be invisible to every rule that
            // reads the fraction while still blocking the ones that read the flag.
            for (int degrees = 0; degrees < 360; degrees += 11)
            {
                OrientedRect a = At(0f, 0f, Facing.East);
                OrientedRect b = At(4f, 18f, Facing.FromDegrees(degrees));

                if (!OrientedRect.Overlaps(a, b)) continue;

                Assert.True(OrientedRect.OverlapFraction(a, b) > 0f,
                    $"At {degrees}° the rectangles overlap but the fraction came back zero.");
            }
        }

        // ---- The gap, which is the other half of the same question -------------

        [Fact]
        public void TheGapBetweenTwoLinesIsMeasuredEdgeToEdgeNotCentreToCentre()
        {
            // Centres a hundred metres apart, but they are facing each other and
            // are only six metres deep apiece, so the ground between them is
            // ninety-four. Centre-to-centre would call this a hundred.
            OrientedRect a = At(0f, 0f, Facing.East);
            OrientedRect b = At(100f, 0f, Facing.West);

            float gap = OrientedRect.GapBetween(a, b);

            Assert.InRange(gap, 93f, 95f);
        }

        [Fact]
        public void TwoRegimentsSharingGroundHaveNoGapAtAll()
        {
            Assert.Equal(0f, OrientedRect.GapBetween(At(0f, 0f, Facing.East), At(2f, 0f, Facing.East)));
        }

        [Fact]
        public void ALineIsWideEnoughThatItsFlankTouchesWhatItsCentreIsFarFrom()
        {
            // The reason nearness is asked of the shape. Two lines drawn up in
            // echelon: their centres are forty metres apart across the front, and
            // their flanks are on top of each other.
            OrientedRect a = At(0f, 0f, Facing.East);
            OrientedRect b = At(0f, 38f, Facing.East);

            Assert.True(Vec2.Distance(a.Centre, b.Centre) > 35f, "Centres well apart.");
            Assert.Equal(0f, OrientedRect.GapBetween(a, b));
        }
    }
}
