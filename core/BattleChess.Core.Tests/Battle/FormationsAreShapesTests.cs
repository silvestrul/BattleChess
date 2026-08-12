using BattleChess.Contracts;
using BattleChess.Rules;
using Xunit;

namespace BattleChess.Tests.Battle
{
    /// <summary>
    /// A regiment is a rectangle, and every rule about nearness must ask about
    /// the rectangle.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Three separate rules once measured centre to centre — contact, zone of
    /// control, and the terrain a unit stands on — and all three were wrong for
    /// the same reason. A body of cavalry is a hundred and six metres across and
    /// eight deep. Its centre is nowhere near most of it, so anything asking
    /// "how far apart are these two points" gets an answer with no bearing on
    /// whether the men can reach each other.
    /// </para>
    /// <para>
    /// The symptom was regiments sliding past one another, formations fully
    /// interpenetrated, with the rules insisting they had never met.
    /// </para>
    /// </remarks>
    public sealed class FormationsAreShapesTests
    {
        // ---- The gap between two formations ------------------------------------

        [Fact]
        public void TwoLinesSideBySideAreTouchingEvenThoughTheirCentresAreFarApart()
        {
            var field = new Battlefield("plains", 14000);

            // Both facing east, so each spreads its frontage north to south and
            // is only a few metres deep. Set a little over half a frontage apart
            // across their fronts, which puts them shoulder to shoulder while
            // their centres are that whole distance from each other.
            //
            // Measured off the regiment rather than written in metres, so the
            // arrangement this test is about survives the rectangle being
            // resized — which it has been, and every hard-coded offset in here
            // quietly stopped describing it.
            UnitInstance left = field.Add(0, "cavalry", field.Centre, Facing.East);

            UnitInstance right = field.Add(1, "cavalry",
                field.Centre + new Vec2(0f, left.Footprint.Width * 0.57f), Facing.East);

            float betweenCentres = Vec2.Distance(left.Position, right.Position);
            float betweenFormations = OrientedRect.GapBetween(left.Shape, right.Shape);

            Assert.True(betweenFormations < betweenCentres * 0.2f,
                $"Two regiments {left.Footprint.Width:0} m wide overlap heavily at this spacing: their centres " +
                $"are {betweenCentres:0} m apart and their formations {betweenFormations:0} m.");

            Assert.True(OrderSystem.InContactWith(left, right),
                "And men that close can plainly reach each other. Measured centre to centre they counted " +
                "as strangers.");
        }

        [Fact]
        public void AFormationOverlappingAnotherHasNoGapAtAll()
        {
            var field = new Battlefield("plains", 14100);

            // Crossed at right angles, one riding through the other's front.
            UnitInstance across = field.Add(0, "cavalry", field.Centre, Facing.North);

            UnitInstance line = field.Add(1, "cavalry",
                field.Centre + new Vec2(across.Footprint.Width * 0.38f, 0f), Facing.East);

            Assert.True(OrientedRect.Overlaps(across.Shape, line.Shape),
                "These two are standing in the same field.");

            Assert.Equal(0f, OrientedRect.GapBetween(across.Shape, line.Shape));
        }

        // ---- Marching past ------------------------------------------------------

        [Fact]
        public void ARegimentCannotSlipPastTheFlankOfALineItIsOverlapping()
        {
            var field = new Battlefield("plains", 14200);

            // A spear wall drawn up facing east, spreading ninety metres north
            // and south, holding its ground.
            UnitInstance line = field.Add(1, "spearmen", field.Centre, Facing.East);
            Battlefield.Hold(line);

            // And swordsmen marching north across its front, close enough that
            // the two formations physically overlap. Their centres stay well
            // outside the spearmen's zone of control the whole way, which is
            // exactly how this used to be a free passage — the runner faces
            // north, so it is its own frontage that sweeps through the line.
            UnitInstance runner = field.Add(0, "swordsmen", field.Centre, Facing.North);

            // Set so the runner's near flank laps a good way over the spear
            // wall's, whatever either of them measures.
            float offset = line.Footprint.HalfDepth + runner.Footprint.HalfWidth * 0.6f;

            Vec2 start = field.Centre + new Vec2(offset, -260f);
            Vec2 finish = field.Centre + new Vec2(offset, 260f);

            runner.Position = start;

            Assert.True(runner.Footprint.HalfWidth > offset - line.Footprint.HalfDepth,
                "The two are meant to overlap as it goes by. If they do not, this proves nothing.");
            field.March(runner, finish);

            field.RunTurns(4);

            float travelled = Vec2.Distance(runner.Position, start);
            float total = Vec2.Distance(start, finish);

            Assert.True(travelled < total * 0.75f,
                $"It should have been stopped somewhere along the enemy's front, not waved through: it " +
                $"covered {travelled:0} m of {total:0} m.");

            Assert.True(field.TimesSaid("halted by Spearmen") > 0,
                "And the spear wall should be named as what stopped it, rather than the march simply " +
                "petering out.");
        }

        [Fact]
        public void HorseIsHeldByHorse()
        {
            // Left standing as a skipped test for a long time: cavalry
            // breakthrough of 1.5 beat cavalry stopping power of 1.2, so horse
            // rode clean through horse and could reach an archer line standing
            // behind its own cavalry screen. It was never a number worth tuning
            // — a body of horse does not stand and get shouldered aside, it
            // gives ground and wheels and is in your way again — so the rule
            // says so outright.
            var field = new Battlefield("plains", 14500);

            UnitInstance screen = field.Add(1, "cavalry", field.Centre, Facing.West);
            Battlefield.Hold(screen);

            Vec2 start = field.Centre - new Vec2(300f, 0f);
            Vec2 beyond = field.Centre + new Vec2(300f, 0f);

            UnitInstance charging = field.Add(0, "cavalry", start, Facing.East);
            field.March(charging, beyond);

            field.RunTurns(5);

            Assert.True(charging.Position.X < screen.Position.X,
                $"It should have been stopped on the near side of them, not ridden through: it reached " +
                $"x={charging.Position.X:0} against the screen at x={screen.Position.X:0}.");

            Assert.True(field.TimesSaid("horse is not ridden through") > 0,
                "And the reason should be on screen rather than left to be inferred from a stalled march.");
        }

        [Fact]
        public void HorseStillRidesThroughFootThatIsNotBracedForIt()
        {
            // The other half of it. Blocking horse from riding through horse
            // must not quietly become blocking it from riding through anybody,
            // or the whole point of cavalry goes with it.
            var field = new Battlefield("plains", 14600);

            UnitInstance foot = field.Add(1, "swordsmen", field.Centre, Facing.West);
            Battlefield.Hold(foot);

            Vec2 start = field.Centre - new Vec2(300f, 0f);
            Vec2 beyond = field.Centre + new Vec2(300f, 0f);

            UnitInstance charging = field.Add(0, "cavalry", start, Facing.East);
            charging.Stance = Stance.Advance;
            field.March(charging, beyond);

            field.RunTurns(5);

            Assert.True(charging.Position.X > foot.Position.X,
                $"Swordsmen are not braced for horse and should be ridden through: the cavalry only reached " +
                $"x={charging.Position.X:0} against them at x={foot.Position.X:0}.");
        }

        // ---- Terrain under the whole formation ----------------------------------

        [Fact]
        public void ALineWithOneFlankInARiverIsDisorderedByTheRiver()
        {
            float onDryGround = DisorderCrossing(riverUnderFlank: false);
            float withAFlankWet = DisorderCrossing(riverUnderFlank: true);

            Assert.Equal(0f, onDryGround);

            Assert.True(withAFlankWet > 0f,
                "A hundred-metre line with one flank in the water is in trouble across its whole front. " +
                "Sampling the centre said it was standing on dry grass.");
        }

        /// <summary>
        /// Reads the disorder a regiment is standing in, with a river band
        /// placed either under its flank or well clear of it.
        /// </summary>
        private static float DisorderCrossing(bool riverUnderFlank)
        {
            var field = new Battlefield("plains", 14300, RuleSet.Full, canvas =>
            {
                // A north-south river. Placed either under the unit's flank or
                // far away, never under its centre — which is the whole point of
                // the test.
                //
                // The column immediately west of the centre one, rather than the
                // column east of it. The centre point sits at the western edge
                // of its own cell, so a regiment narrower than a cell reaches
                // into the cell before it and never into the cell after — and
                // once the rectangle was halved, a band placed to the east had
                // nothing standing in it at all.
                int centreColumn = canvas.ColumnAt(canvas.Columns * canvas.CellSize * 0.5f);
                int column = riverUnderFlank ? centreColumn - 1 : centreColumn + 12;

                canvas.Band(column, column, "river");
            });

            // Facing north, so a cavalry regiment spreads its frontage east to
            // west — straight into the river band two cells over.
            UnitInstance line = field.Add(0, "cavalry", field.Centre, Facing.North);

            return field.State.WorstDisorderUnder(line);
        }

        [Fact]
        public void TheWorstGroundUnderALineIsWhatCounts()
        {
            var field = new Battlefield("plains", 14400, RuleSet.Full, canvas =>
            {
                int centre = canvas.ColumnAt(canvas.Columns * canvas.CellSize * 0.5f);

                canvas.Band(centre + 1, centre + 1, "forest");
                canvas.Band(centre - 1, centre - 1, "river");
            });

            UnitInstance line = field.Add(0, "cavalry", field.Centre, Facing.North);

            float river = TestContent.Ground("river").Get(TerrainAttributes.Disorder);

            Assert.Equal(river, field.State.WorstDisorderUnder(line), 5);
        }
    }
}
