using BattleChess.Contracts;
using BattleChess.Rules;
using BattleChess.Tests.Battle;
using Xunit;
using Xunit.Abstractions;

namespace BattleChess.Tests.Diagnostics
{
    /// <summary>
    /// Whether a battle writes down the two things [W6] asks it to: every time
    /// two of its own share ground, and where each routing decision decided to
    /// walk.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both were missing, and the cost of that was the whole shape of the last
    /// four play-tests. Every report has been some form of *"it goes through
    /// them"*, and every one had to be reproduced from first principles —
    /// because a recording of the battle in which it happened contained no
    /// evidence that it had. The overlap cost, the shuffle apart and the yield
    /// rule all ran in silence.
    /// </para>
    /// <para>
    /// Guarded by test rather than left to reading, because a log line is
    /// exactly the kind of thing that is quietly deleted or gated behind a
    /// condition that stops being true, and nothing else notices.
    /// </para>
    /// </remarks>
    public sealed class CollisionRecordTests
    {
        private readonly ITestOutputHelper _out;

        public CollisionRecordTests(ITestOutputHelper output) => _out = output;

        /// <summary>
        /// A wall of its own straight across the line, so pressing through is
        /// the only answer and the collision is certain rather than incidental.
        /// </summary>
        private static Battlefield AMarchThroughItsOwn(out UnitInstance mover, out UnitInstance wall)
        {
            var field = new Battlefield("plains", 32000);

            wall = field.Add(0, "spearmen", field.Centre, Facing.East);
            Battlefield.Hold(wall);

            for (int i = 1; i <= 6; i++)
                foreach (float side in new[] { 1f, -1f })
                    Battlefield.Hold(
                        field.Add(0, "spearmen", field.Centre + new Vec2(0f, side * i * 40f), Facing.East));

            // Foot rather than horse, and deliberately. Cavalry crosses a
            // twenty-metre body in eleven ticks, which is too brief to tell a
            // line said once per collision from one said once per tick — the
            // very thing the second test here exists to measure.
            mover = field.Add(0, "spearmen", field.Centre - new Vec2(120f, 0f), Facing.East);

            field.March(mover, field.Centre + new Vec2(120f, 0f), log: field.Transcript);

            return field;
        }

        [Fact]
        public void TwoOfItsOwnSharingGroundIsWrittenDown()
        {
            Battlefield field = AMarchThroughItsOwn(out UnitInstance mover, out UnitInstance wall);

            int overlapping = 0;

            for (int tick = 0; tick < 240; tick++)
            {
                field.Clock.Advance(field.State, field.Transcript);

                if (OrientedRect.OverlapFraction(mover.Shape, wall.Shape) > OrderSystem.GrazingTolerance)
                    overlapping++;
            }

            _out.WriteLine($"{overlapping} ticks overlapping, " +
                           $"{field.TimesSaid("is standing in its own")} collisions recorded.");

            // Non-vacuity first. A test that asserts a collision was reported
            // proves nothing at all if no collision happened — and this
            // arrangement stopped producing one twice already, once when rung 2
            // learnt to go round two-block walls and once when M25a changed
            // which bodies count as being in the way.
            Assert.True(overlapping > 0,
                "Nothing shared any ground, so this measures nothing. The wall is no longer being " +
                "pressed through — check which rung is answering before trusting the assertion below.");

            Assert.True(field.TimesSaid("is standing in its own") > 0,
                $"Two regiments shared ground for {overlapping} ticks and the recording says nothing " +
                "about it. That silence is what made every movement report of this play-test have to be " +
                "reproduced from scratch.");
        }

        [Fact]
        public void ACollisionIsOneParagraphRatherThanOneLineATick()
        {
            Battlefield field = AMarchThroughItsOwn(out UnitInstance mover, out UnitInstance wall);

            int overlapping = 0;

            for (int tick = 0; tick < 240; tick++)
            {
                field.Clock.Advance(field.State, field.Transcript);

                if (OrientedRect.OverlapFraction(mover.Shape, wall.Shape) > OrderSystem.GrazingTolerance)
                    overlapping++;
            }

            int opened = field.TimesSaid("is standing in its own");

            _out.WriteLine($"{overlapping} ticks overlapping, {opened} opening lines.");

            Assert.True(overlapping > 20,
                "Too brief a passage to tell a per-collision line from a per-tick one. This measures nothing.");

            // The rule the whole logging pass turns on. Said once a tick this
            // was 218 of 297 lines in a twelve-turn battle and the volume gate
            // failed the build, which is the correct outcome and not one to
            // rely on catching it a second time.
            Assert.True(opened < overlapping / 4,
                $"{opened} opening lines for {overlapping} ticks of overlap. A collision is an event and " +
                "gets said once; this is being reported per tick, or it is flapping across the grazing " +
                "tolerance and reading as many separate collisions.");
        }

        [Fact]
        public void ARoutingDecisionSaysWhereItDecidedToWalk()
        {
            Battlefield field = AMarchThroughItsOwn(out UnitInstance mover, out _);

            for (int tick = 0; tick < 240; tick++)
                field.Clock.Advance(field.State, field.Transcript);

            // Every rung writes its waypoints with "by (x,y) → …", so counting
            // that counts decisions that named their line. Counting the rungs
            // themselves would be the weaker test: what was missing was never
            // the rung, it was the route.
            int named = field.TimesSaid("by (");

            _out.WriteLine($"{named} decisions named the line they chose.");

            Assert.True(named > 0,
                "Not one routing decision said where it was going to walk. Which rung answered is a " +
                "one-word summary of a decision whose substance is the line — and both times a movement " +
                "fault was cracked this month, it was cracked by pulling coordinates out of the log.");
        }
    }
}
