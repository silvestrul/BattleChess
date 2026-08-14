using System;
using BattleChess.Contracts;
using Xunit;

namespace BattleChess.Tests.World
{
    /// <summary>
    /// Carrying a rectangle along a line and asking what it runs into.
    /// </summary>
    /// <remarks>
    /// The primitive M10 rests on, so it is tested on its own and hard. Every
    /// question a march asks reduces to this one, and a fault here would show up
    /// as regiments walking through each other or refusing open ground — both of
    /// which read as anything but a geometry bug.
    /// </remarks>
    public sealed class SweepTests
    {
        // A regiment-sized block, 40 m by 20 m, facing east.
        private static OrientedRect Block(float x, float y, float degrees = 0f) =>
            new OrientedRect(new Vec2(x, y), Facing.FromDegrees(degrees), new Footprint(40f, 20f));

        [Fact]
        public void OpenGroundIsOpen()
        {
            // Something 200 m off the line of march is not in the way.
            Assert.False(
                Sweep.FirstTouch(Block(0f, 0f), new Vec2(300f, 0f), Block(150f, 200f), out float distance));

            Assert.Equal(300f, distance, 1);
        }

        [Fact]
        public void SomethingSquarelyAheadStopsIt()
        {
            Assert.True(
                Sweep.FirstTouch(Block(0f, 0f), new Vec2(300f, 0f), Block(100f, 0f), out float distance));

            // Facing east, the block is 20 m deep, so half-depths meet at 20 m
            // short of centre to centre.
            Assert.InRange(distance, 78f, 82f);
        }

        [Fact]
        public void ItIsTheBodyThatHasToFitAndNotTheCentre()
        {
            // The centre line passes 25 m clear of the obstacle's centre — but
            // the two are 40 m wide, so twenty of each meet in the middle. A
            // cast that asked about the centre would call this open ground.
            Assert.True(
                Sweep.FirstTouch(
                    Block(0f, 0f, 90f), new Vec2(0f, 300f), Block(25f, 150f, 90f), out _),
                "Facing north, both blocks are 40 m wide across the line of march and their centres are " +
                "only 25 m apart. The bodies overlap even though the centre line misses.");
        }

        [Fact]
        public void SlippingPastTheSideIsNotAHit()
        {
            // Same pair, moved far enough apart that the two 20 m half-widths
            // genuinely clear each other.
            Assert.False(
                Sweep.FirstTouch(
                    Block(0f, 0f, 90f), new Vec2(0f, 300f), Block(45f, 150f, 90f), out _));
        }

        [Fact]
        public void AShortStepThatDoesNotReachIsNotAHit()
        {
            // The obstacle is genuinely on the line, but 300 m away and the step
            // is 50 m. The distance still has to come back as the whole step.
            Assert.False(
                Sweep.FirstTouch(Block(0f, 0f), new Vec2(50f, 0f), Block(300f, 0f), out float distance));

            Assert.Equal(50f, distance, 1);
        }

        [Fact]
        public void AlreadyTouchingMeansItMayNotMoveAtAll()
        {
            Assert.True(
                Sweep.FirstTouch(Block(0f, 0f), new Vec2(100f, 0f), Block(10f, 0f), out float distance));

            Assert.Equal(0f, distance, 2);
        }

        [Fact]
        public void StandingStillOnlyMeetsWhatItIsAlreadyIn()
        {
            Assert.False(
                Sweep.FirstTouch(Block(0f, 0f), Vec2.Zero, Block(100f, 0f), out _));

            Assert.True(
                Sweep.FirstTouch(Block(0f, 0f), Vec2.Zero, Block(10f, 0f), out _));
        }

        [Fact]
        public void CrabbingFitsThroughAGapMarchingDoesNot()
        {
            // Two blocks facing east at x = 150, one either side of the line,
            // each spanning 40 m of width. Their inner edges sit at y = ±15, so
            // the gap between them is 30 m.
            OrientedRect above = Block(150f, 35f);
            OrientedRect below = Block(150f, -35f);

            Vec2 eastward = new Vec2(300f, 0f);

            // Facing east and going east: the 40 m frontage lies across the
            // line of march, spanning y = ±20. It fouls both.
            OrientedRect marching = new OrientedRect(Vec2.Zero, Facing.East, new Footprint(40f, 20f));

            bool blocked = Sweep.FirstTouch(marching, eastward, above, out _)
                        || Sweep.FirstTouch(marching, eastward, below, out _);

            // Facing north and going east: the same body now presents its 20 m
            // depth across the gap, spanning y = ±10, and goes through. This is
            // M13 and M14 — and note it had to *turn* to do it, which is what
            // "turn to set the crab up" means.
            OrientedRect crabbing = new OrientedRect(Vec2.Zero, Facing.North, new Footprint(40f, 20f));

            bool crabbed = Sweep.FirstTouch(crabbing, eastward, above, out _)
                        || Sweep.FirstTouch(crabbing, eastward, below, out _);

            Assert.True(blocked, "40 m of frontage should not fit through a gap 30 m wide.");
            Assert.False(crabbed, "Turned side-on the same body is 20 m across the gap and should fit.");
        }

        [Fact]
        public void TheNarrowestGapIsFoundAlongTheWholeWayNotOnlyAtTheEnds()
        {
            // The obstacle sits halfway along, off to one side. Measuring only
            // at the start and the finish would report it as far away.
            float gap = Sweep.NarrowestGap(Block(0f, 0f), new Vec2(300f, 0f), Block(150f, 45f));

            Assert.InRange(gap, 0f, 20f);

            float atTheEnds = MathF.Min(
                OrientedRect.GapBetween(Block(0f, 0f), Block(150f, 45f)),
                OrientedRect.GapBetween(Block(300f, 0f), Block(150f, 45f)));

            Assert.True(gap < atTheEnds,
                $"Measured along the way the gap is {gap:0.0} m, but only at the ends it looks like " +
                $"{atTheEnds:0.0} m. A line is judged by its worst moment, not its first and last.");
        }
    }
}
