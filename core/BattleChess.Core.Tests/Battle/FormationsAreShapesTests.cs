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
            // is only a few metres deep. Sixty metres apart across their fronts
            // puts them shoulder to shoulder while their centres are sixty
            // metres away from each other.
            UnitInstance left = field.Add(0, "cavalry", field.Centre, Facing.East);
            UnitInstance right = field.Add(1, "cavalry", field.Centre + new Vec2(0f, 60f), Facing.East);

            float betweenCentres = Vec2.Distance(left.Position, right.Position);
            float betweenFormations = OrientedRect.GapBetween(left.Shape, right.Shape);

            Assert.True(betweenFormations < betweenCentres * 0.2f,
                $"Two regiments a hundred metres wide overlap heavily at this spacing: their centres are " +
                $"{betweenCentres:0} m apart and their formations {betweenFormations:0} m.");

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
            UnitInstance line = field.Add(1, "cavalry", field.Centre + new Vec2(40f, 0f), Facing.East);

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
            // the two formations physically overlap. Their centres stay forty-
            // five metres apart the whole way — outside the spearmen's thirty-
            // metre zone of control — which is exactly how this used to be a
            // free passage.
            Vec2 start = field.Centre + new Vec2(45f, -260f);
            Vec2 finish = field.Centre + new Vec2(45f, 260f);

            UnitInstance runner = field.Add(0, "swordsmen", start, Facing.North);
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

        [Fact(Skip = "Reported and deliberately left standing — cavalry breakthrough 1.5 still beats " +
                     "cavalry stopping power 1.2, so horse rides through horse. One number in units.cfg.")]
        public void HorseIsHeldByHorse()
        {
            // Worth keeping visible rather than fixing quietly. Riding through
            // no longer *pays* — a charge is spent on impact and has to be
            // re-earned — but the pass-through itself is still there, and it is
            // the reason cavalry can reach an archer line standing behind its
            // own horsemen.
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
                // A north-south river. Placed either just off the unit's flank
                // or far away, never under its centre — which is the whole
                // point of the test.
                int centreColumn = canvas.ColumnAt(canvas.Columns * canvas.CellSize * 0.5f);
                int column = riverUnderFlank ? centreColumn + 1 : centreColumn + 12;

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
