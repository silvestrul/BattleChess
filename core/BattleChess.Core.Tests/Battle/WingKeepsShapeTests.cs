using System;
using System.Collections.Generic;
using System.Linq;
using BattleChess.Contracts;
using BattleChess.Rules;
using Xunit;
using Xunit.Abstractions;

namespace BattleChess.Tests.Battle
{
    /// <summary>
    /// A line ordered to attack arrives as a line [M154].
    /// </summary>
    /// <remarks>
    /// <para>
    /// Reported from play with a picture: five regiments in line, told to attack
    /// the line opposite, folded inward into a diagonal knot as they advanced.
    /// The cause is in the chase and not in the board - it is there on open
    /// ground with no grid at all - so this runs the continuous game, where the
    /// fault is at its plainest and nothing else can be blamed for it.
    /// </para>
    /// <para>
    /// <b>What "keeps its shape" is measured as.</b> Not the absolute geometry -
    /// a wing wheels as it closes, and a regiment that has to go round something
    /// leaves its place and comes back. The thing the designer asked for is the
    /// <i>spacing</i>: "the end position should be close to starting distance,
    /// like they are still linked". So neighbour-to-neighbour distance along the
    /// line, at the start and at the end, is what these compare.
    /// </para>
    /// </remarks>
    public sealed class WingKeepsShapeTests
    {
        private readonly ITestOutputHelper _out;

        public WingKeepsShapeTests(ITestOutputHelper output) => _out = output;

        /// <summary>How many regiments stand in the line under test.</summary>
        private const int InTheLine = 5;

        /// <summary>Metres between neighbours when the line forms up.</summary>
        /// <remarks>
        /// A hundred, against a 40 m frontage at the test strength, so there is
        /// real air between them - a line packed shoulder to shoulder could not
        /// collapse any further and would pass this test without the fix.
        /// </remarks>
        private const float ApartMetres = 100f;

        private sealed class Line
        {
            public Battlefield Field = null!;
            public List<UnitInstance> Wing = null!;
            public UnitInstance Quarry = null!;
        }

        /// <summary>
        /// Five regiments abreast, facing east, with an enemy line ahead of them.
        /// </summary>
        private Line FormUp(bool asAWing)
        {
            var field = new Battlefield();

            Vec2 centre = field.Centre;

            var wing = new List<UnitInstance>();

            for (int i = 0; i < InTheLine; i++)
            {
                float across = (i - (InTheLine - 1) * 0.5f) * ApartMetres;

                wing.Add(field.Add(
                    0, "spearmen", new Vec2(centre.X - 400f, centre.Y + across), Facing.East));
            }

            // One enemy, dead ahead of the middle of the line, which is the
            // arrangement from the play-test: everybody is sent at the same
            // regiment.
            UnitInstance quarry = field.Add(1, "spearmen", new Vec2(centre.X + 150f, centre.Y), Facing.West);

            if (asAWing)
            {
                foreach (UnitInstance unit in wing) unit.Bond = -1;

                OrderSystem.TakeStations(wing, quarry.Position);
            }

            foreach (UnitInstance unit in wing)
                unit.GiveOrder(UnitOrder.Attack(quarry.Id), unit.Position);

            return new Line { Field = field, Wing = wing, Quarry = quarry };
        }

        /// <summary>Neighbour-to-neighbour distances along the line, in order.</summary>
        private static List<float> Spacings(IReadOnlyList<UnitInstance> wing) =>
            wing.Zip(wing.Skip(1), (a, b) => Vec2.Distance(a.Position, b.Position)).ToList();

        private float Advance(Line line, int turns)
        {
            List<float> before = Spacings(line.Wing);

            float wasAway = Vec2.Distance(line.Wing[0].Position, line.Quarry.Position);

            line.Field.RunTurns(turns);

            List<float> after = Spacings(line.Wing);

            float nowAway = Vec2.Distance(line.Wing[0].Position, line.Quarry.Position);

            _out.WriteLine($"  closed from {wasAway:0} m to {nowAway:0} m from the quarry");

            // Non-vacuity on the run itself. A line that never set off keeps its
            // spacing perfectly, and would pass the gate below while proving
            // nothing at all.
            Assert.True(
                wasAway - nowAway > 150f,
                $"the wing only closed {wasAway - nowAway:0} m, so nothing was measured about advancing.");

            // And it must still be an advance rather than a melee: five
            // regiments crowding onto one enemy at contact is correct behaviour,
            // not the fault being tested.
            Assert.True(
                nowAway > OrderSystem.DressingRangeForTests,
                $"the wing is {nowAway:0} m from the quarry, inside dressing range, so this is measuring " +
                "contact rather than the advance.");

            _out.WriteLine($"  before: {string.Join(", ", before.Select(d => $"{d:0} m"))}");
            _out.WriteLine($"  after:  {string.Join(", ", after.Select(d => $"{d:0} m"))}");

            float worst = 0f;

            for (int i = 0; i < before.Count; i++)
                worst = MathF.Max(worst, MathF.Abs(after[i] - before[i]));

            _out.WriteLine($"  worst change in spacing: {worst:0} m of {ApartMetres:0}");

            return worst;
        }

        /// <summary>
        /// A wing told to attack arrives with the spacing it set off with.
        /// </summary>
        /// <remarks>
        /// The gate, and it measures the ADVANCE. Three turns is about 290 m of
        /// the 550 m to the enemy, so the line is still well outside dressing
        /// range - which matters, because five regiments crowding onto one enemy
        /// at contact is correct behaviour and would mask the fault. The
        /// play-test picture was of a line still in the open, folding.
        /// </remarks>
        [Fact]
        public void ALineOrderedToAttackKeepsItsSpacing()
        {
            _out.WriteLine("as a wing, keeping station:");

            float worst = Advance(FormUp(asAWing: true), turns: 3);

            Assert.True(
                worst < ApartMetres * 0.5f,
                $"the line closed up by {worst:0} m of {ApartMetres:0} - it is folding into a knot rather " +
                "than advancing as a line.");
        }

        /// <summary>
        /// The same five regiments, not held together, do collapse - which is
        /// what makes the test above mean something.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>The non-vacuity, and it is a measurement rather than an
        /// assertion about the code.</b> Five regiments sent at one enemy
        /// without a wing between them each aim at a stand-off along their own
        /// bearing to it, and those bearings converge. If this ever stops
        /// collapsing, the test above has stopped proving anything and both
        /// need rewriting.
        /// </para>
        /// <para>
        /// It asserts the collapse rather than merely printing it, for the
        /// reason four tests this sweep have needed: a guard that is not
        /// asserted is a guard nobody reads.
        /// </para>
        /// </remarks>
        [Fact]
        public void WithoutAWingTheSameLineCollapses()
        {
            _out.WriteLine("not a wing, every regiment for itself:");

            float worst = Advance(FormUp(asAWing: false), turns: 3);

            Assert.True(
                worst > ApartMetres * 0.5f,
                $"regiments sent at one enemy with no wing between them changed spacing by only " +
                $"{worst:0} m, so there is no collapse here for the wing to be preventing.");
        }
    }
}
