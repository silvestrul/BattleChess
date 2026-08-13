using System;
using BattleChess.Contracts;
using BattleChess.Rules;
using Xunit;

namespace BattleChess.Tests.Battle
{
    /// <summary>
    /// Coming about: every angle, both ways round, and with the field crowded.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Turning is the manoeuvre the recorded games kept catching out, and always
    /// in the same shape: the regiment obeyed large orders and quietly ignored
    /// small ones. Three separate rules tried to say how big a change was worth
    /// making before one was finally reported that had nothing under eighteen
    /// metres turning at all.
    /// </para>
    /// <para>
    /// So the angles are swept rather than sampled. A rule that works at ninety
    /// degrees and fails at five is not a rule that works, and a single test at a
    /// convenient angle is exactly how the last three got through.
    /// </para>
    /// </remarks>
    public sealed class RotationTests
    {
        // ---- Turning on the spot, through the whole circle ---------------------

        [Theory]
        [InlineData(5f)]
        [InlineData(10f)]
        [InlineData(20f)]
        [InlineData(45f)]
        [InlineData(90f)]
        [InlineData(135f)]
        [InlineData(179f)]
        [InlineData(-5f)]
        [InlineData(-30f)]
        [InlineData(-90f)]
        [InlineData(-150f)]
        public void ARegimentToldToFaceAnyBearingGetsThere(float degrees)
        {
            var field = new Battlefield("plains", 23000 + (ulong)MathF.Abs(degrees));

            UnitInstance foot = field.Add(0, "swordsmen", field.Centre, Facing.East);
            Battlefield.Hold(foot);

            Facing target = Facing.FromDegrees(degrees);
            foot.GiveOrder(UnitOrder.Face(target), foot.Position);

            field.RunTurns(3);

            Assert.True(Degrees(Facing.AbsoluteDelta(foot.Facing, target)) < 2f,
                $"Told to face {degrees:0}°, it settled on {foot.Facing.Degrees:0}°.");
        }

        [Fact]
        public void ARegimentComesRoundTheShortWay()
        {
            var field = new Battlefield("plains", 23100);

            UnitInstance foot = field.Add(0, "swordsmen", field.Centre, Facing.FromDegrees(10f));
            Battlefield.Hold(foot);

            // Twenty degrees clockwise, across the point where the bearing wraps.
            // Going the other way is a three-hundred-and-forty degree turn and
            // several times as slow, and wrap-around is where angle arithmetic
            // usually gets it wrong.
            foot.GiveOrder(UnitOrder.Face(Facing.FromDegrees(350f)), foot.Position);

            field.RunPulses(2);

            float turned = Degrees(Facing.AbsoluteDelta(foot.Facing, Facing.FromDegrees(10f)));

            Assert.True(turned <= 21f,
                $"It should have come round twenty degrees the short way and has moved {turned:0}° instead — " +
                "which means it set off the long way round the circle.");
        }

        [Fact]
        public void ATurnStopsWhenItArrivesRatherThanSwingingPast()
        {
            var field = new Battlefield("plains", 23200);

            UnitInstance foot = field.Add(0, "swordsmen", field.Centre, Facing.East);
            Battlefield.Hold(foot);

            foot.GiveOrder(UnitOrder.Face(Facing.North), foot.Position);

            // Long after the turn is finished. A regiment that overshoots and
            // corrects reads on the field as one that will not settle.
            field.RunTurns(6);

            Assert.True(Degrees(Facing.AbsoluteDelta(foot.Facing, Facing.North)) < 1f,
                $"It should be sitting on north and is at {foot.Facing.Degrees:0}°.");
        }

        [Fact]
        public void ABiggerTurnTakesLonger()
        {
            float quarter = TurnsToComeRound(90f);
            float about = TurnsToComeRound(180f);

            Assert.True(about > quarter,
                $"Turning about should cost more than a quarter turn: {about:0.0} against {quarter:0.0} turns.");
        }

        /// <summary>How far round a regiment gets in a single pulse.</summary>
        private static float TurnsToComeRound(float degrees)
        {
            var field = new Battlefield("plains", 23300);

            UnitInstance foot = field.Add(0, "swordsmen", field.Centre, Facing.East);
            Battlefield.Hold(foot);

            Facing target = Facing.FromDegrees(degrees);
            foot.GiveOrder(UnitOrder.Face(target), foot.Position);

            for (int pulse = 1; pulse <= 20; pulse++)
            {
                field.RunPulses(1);

                if (Degrees(Facing.AbsoluteDelta(foot.Facing, target)) < 1f)
                    return pulse;
            }

            return 20;
        }

        // ---- Turning with the field against it ---------------------------------

        [Fact]
        public void AFlankedRegimentCanTurnToMeetWhatGotRoundIt()
        {
            var field = new Battlefield("plains", 23400);

            UnitInstance foot = field.Add(0, "swordsmen", field.Centre, Facing.East);
            Battlefield.Hold(foot);

            // Cavalry hard against its flank. This is the whole reason turning
            // ignores collision: a regiment that cannot come about while gripped
            // can never answer an attack that got round it, and being flanked
            // would be a death sentence rather than a mistake.
            UnitInstance horse = field.Add(1, "cavalry",
                Battlefield.ContactPosition(foot, foot.Footprint, new Vec2(0f, 1f)), Facing.South);
            Battlefield.Press(horse, foot);

            field.RunPulses(1);

            foot.GiveOrder(UnitOrder.Face(Facing.North), foot.Position);
            field.RunTurns(2);

            Assert.True(Degrees(Facing.AbsoluteDelta(foot.Facing, Facing.North)) < 25f,
                $"Taken in the flank and told to come about, it is facing {foot.Facing.Degrees:0}° instead of " +
                "north. Men being killed from the side turn to face it.");
        }

        [Fact]
        public void ARegimentSurroundedOnAllFourSidesCanStillComeAbout()
        {
            var field = new Battlefield("plains", 23500);

            UnitInstance foot = field.Add(0, "swordsmen", field.Centre, Facing.East);
            Battlefield.Hold(foot);

            // Boxed in by its own on every side, overlapping outright.
            field.Add(0, "swordsmen", field.Centre + new Vec2(5f, 0f), Facing.East);
            field.Add(0, "swordsmen", field.Centre - new Vec2(5f, 0f), Facing.East);
            field.Add(0, "swordsmen", field.Centre + new Vec2(0f, 30f), Facing.East);
            field.Add(0, "swordsmen", field.Centre - new Vec2(0f, 30f), Facing.East);

            foot.GiveOrder(UnitOrder.Face(Facing.West), foot.Position);
            field.RunTurns(3);

            Assert.True(Degrees(Facing.AbsoluteDelta(foot.Facing, Facing.West)) < 5f,
                $"Hemmed in on all sides it is still allowed to turn where it stands. It is facing " +
                $"{foot.Facing.Degrees:0}°.");
        }

        [Fact]
        public void TurningOnTheSpotDoesNotDriftOffTheSpot()
        {
            var field = new Battlefield("plains", 23600);

            UnitInstance foot = field.Add(0, "swordsmen", field.Centre, Facing.East);
            Battlefield.Hold(foot);

            foot.GiveOrder(UnitOrder.Face(Facing.South), foot.Position);
            field.RunTurns(3);

            Assert.True(Vec2.Distance(foot.Position, field.Centre) < 2f,
                $"A regiment wheeling in place pivots on its centre. It has wandered " +
                $"{Vec2.Distance(foot.Position, field.Centre):0} m.");
        }

        // ---- Turning as part of marching ---------------------------------------

        [Theory]
        [InlineData(8f)]
        [InlineData(15f)]
        [InlineData(30f)]
        [InlineData(60f)]
        [InlineData(120f)]
        [InlineData(180f)]
        public void AMarchOnANewBearingComesRoundToItWhateverTheAngle(float degrees)
        {
            var field = new Battlefield("plains", 24000 + (ulong)degrees);

            UnitInstance horse = field.Add(0, "cavalry", field.Centre, Facing.East);

            Facing target = Facing.FromDegrees(degrees);
            field.March(horse, field.Centre + target.ToVector() * 250f);

            field.RunTurns(3);

            Assert.True(Degrees(Facing.AbsoluteDelta(horse.Facing, target)) < 15f,
                $"Sent off on a bearing of {degrees:0}° it is facing {horse.Facing.Degrees:0}°.");
        }

        [Theory]
        [InlineData(6f)]
        [InlineData(12f)]
        [InlineData(25f)]
        [InlineData(60f)]
        public void EvenAShortMoveComesRoundToTheWayItIsGoing(float metres)
        {
            var field = new Battlefield("plains", 24500 + (ulong)metres);

            UnitInstance horse = field.Add(0, "cavalry", field.Centre, Facing.East);

            // The exact complaint from the recordings: everything between nine
            // and eighteen metres was ignored while everything past twenty-three
            // was obeyed. Fine adjustments are where a player is most particular
            // about which way a regiment ends up pointing.
            field.March(horse, field.Centre + new Vec2(0f, metres));
            field.RunTurns(3);

            Assert.True(Degrees(Facing.AbsoluteDelta(horse.Facing, Facing.North)) < 15f,
                $"Sent {metres:0} m north, it is facing {horse.Facing.Degrees:0}°.");
        }

        [Fact]
        public void ASuccessionOfSmallCorrectionsEndsUpWhereTheLastOnePointed()
        {
            var field = new Battlefield("plains", 24900);

            UnitInstance horse = field.Add(0, "cavalry", field.Centre, Facing.East);

            // Nudged round in twenty-degree steps, which is how a player lines a
            // regiment up by eye. Each step is short enough to have fallen inside
            // every dead zone the rules have ever had.
            foreach (float degrees in new[] { 20f, 40f, 60f, 80f })
            {
                field.March(horse, horse.Position + Facing.FromDegrees(degrees).ToVector() * 15f);
                field.RunTurns(1);
            }

            field.RunTurns(2);

            Assert.True(Degrees(Facing.AbsoluteDelta(horse.Facing, Facing.FromDegrees(80f))) < 20f,
                $"Walked round in four steps to eighty degrees, it is facing {horse.Facing.Degrees:0}°.");
        }

        [Fact]
        public void AWheelIsNotAbandonedHalfwayByTheNextOrder()
        {
            var field = new Battlefield("plains", 25000);

            UnitInstance foot = field.Add(0, "swordsmen", field.Centre, Facing.East);

            field.March(foot, field.Centre + new Vec2(-200f, 0f));
            field.RunPulses(1);

            float partWay = foot.Facing.Degrees;

            Assert.True(Degrees(Facing.AbsoluteDelta(foot.Facing, Facing.West)) > 0.1f,
                "The test needs it still turning when the next order lands.");

            // A second order on the same bearing, arriving mid-wheel.
            field.March(foot, foot.Position + new Vec2(-40f, 0f));
            field.RunTurns(3);

            Assert.True(Degrees(Facing.AbsoluteDelta(foot.Facing, Facing.West)) < 10f,
                $"It was part-way round to west at {partWay:0}° when a second westward order landed, and " +
                $"finished at {foot.Facing.Degrees:0}°. Both orders pointed the same way.");
        }

        private static float Degrees(float radians) => radians * 180f / MathF.PI;
    }
}
