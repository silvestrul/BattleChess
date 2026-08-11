using System;
using BattleChess.Contracts;
using BattleChess.Rules;
using Xunit;

namespace BattleChess.Tests.Battle
{
    /// <summary>
    /// Ground a regiment cannot cross is ground it cannot be on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Movement asked about a single point at the regiment's centre, which for
    /// a body a hundred metres wide is barely a question: a line could sit with
    /// half its frontage buried in a mountain and the rules saw a centre on
    /// open grass. Everything else about this game treats regiments as shapes,
    /// and this was the last place that did not.
    /// </para>
    /// <para>
    /// It brings a manoeuvre with it. A wide line meeting a gap narrower than
    /// its frontage comes round until it is presenting its depth instead, edges
    /// through at the price the alignment penalty already charges, and returns
    /// to the front it was given once clear.
    /// </para>
    /// </remarks>
    public sealed class ThreadingGapsTests
    {
        /// <summary>
        /// A wall of deep water across the field with a gap of a given width cut
        /// through the middle of it.
        /// </summary>
        private static Battlefield WallWithGap(ulong seed, int gapCells)
        {
            return new Battlefield("plains", seed, RuleSet.Full, canvas =>
            {
                int wall = canvas.Columns / 2;
                int middle = canvas.Rows / 2;

                canvas.Rect(wall, 0, wall + 1, canvas.Rows - 1, "deepwater");
                canvas.Rect(wall, middle - gapCells / 2, wall + 1, middle + gapCells / 2, "plains");
            });
        }

        // ---- The footprint is what has to fit ----------------------------------

        [Fact]
        public void AFormationCannotStandWithItsFlankInTheSea()
        {
            var field = new Battlefield("plains", 23000, RuleSet.Full, canvas =>
                canvas.Band(canvas.Columns / 2, canvas.Columns - 1, "deepwater"));

            UnitInstance unit = field.Add(0, "cavalry", field.Centre, Facing.North);

            // Facing north puts its hundred and ten metres of frontage east to
            // west, straight into the water beside it.
            Assert.False(field.State.FormationFits(unit, unit.Position, Facing.North),
                "Half a regiment standing in deep water is not a regiment on open ground.");

            Assert.True(field.State.FormationFits(unit, field.Centre - new Vec2(300f, 0f), Facing.North),
                "Well clear of it, the same regiment is plainly fine.");
        }

        [Fact]
        public void ARegimentDoesNotWalkItsFlankIntoImpassableGround()
        {
            var field = new Battlefield("plains", 23100, RuleSet.Full, canvas =>
                canvas.Band(canvas.Columns / 2, canvas.Columns - 1, "deepwater"));

            // Facing north, so its frontage runs east-west and will meet the
            // range side-on as it marches up the field.
            UnitInstance unit = field.Add(0, "cavalry", field.Centre - new Vec2(260f, 200f), Facing.North);

            field.March(unit, field.Centre - new Vec2(260f, -200f));
            field.RunTurns(8);

            Assert.True(field.State.FormationFits(unit, unit.Position, unit.Facing),
                $"It ended at {unit.Position} facing {unit.Facing.Degrees:0}°, with part of the " +
                "formation on ground it cannot cross.");
        }

        // ---- Coming round to get through ---------------------------------------

        [Fact]
        public void AWideLineComesRoundToThreadAGapNarrowerThanItsFrontage()
        {
            // Three cells is 75 m, against a hundred and ten metres of cavalry
            // frontage. Straight on it cannot fit; turned, it presents eight
            // metres of depth and goes through easily.
            Battlefield field = WallWithGap(23200, gapCells: 3);

            UnitInstance unit = field.Add(0, "cavalry", field.Centre - new Vec2(250f, 0f), Facing.East);

            field.March(unit, field.Centre + new Vec2(250f, 0f));
            field.RunTurns(14);

            Assert.True(unit.Position.X > field.Centre.X,
                $"It should have got through the gap: it reached x={unit.Position.X:0} against a wall " +
                $"at x={field.Centre.X:0}.");

            Assert.True(field.State.FormationFits(unit, unit.Position, unit.Facing),
                "And be standing somewhere it is allowed to stand.");
        }

        [Fact]
        public void AGapWideEnoughIsWalkedStraightThroughWithoutTurning()
        {
            // Nine cells is 225 m — room to spare, so there is nothing to solve
            // and the regiment should not start pirouetting for no reason.
            Battlefield field = WallWithGap(23300, gapCells: 9);

            UnitInstance unit = field.Add(0, "cavalry", field.Centre - new Vec2(250f, 0f), Facing.East);
            Facing before = unit.Facing;

            field.March(unit, field.Centre + new Vec2(250f, 0f));
            field.RunTurns(10);

            Assert.True(unit.Position.X > field.Centre.X, "It should be through.");

            Assert.True(Degrees(Facing.AbsoluteDelta(unit.Facing, before)) < 10f,
                $"And should not have turned to do it — it ended {Degrees(Facing.AbsoluteDelta(unit.Facing, before)):0}° " +
                "off the front it started with.");
        }

        [Fact]
        public void ARegimentReturnsToTheFrontItWasGivenOnceItIsClear()
        {
            Battlefield field = WallWithGap(23400, gapCells: 3);

            UnitInstance unit = field.Add(0, "cavalry", field.Centre - new Vec2(250f, 0f), Facing.East);
            Facing ordered = unit.Facing;

            field.March(unit, field.Centre + new Vec2(250f, 0f));
            field.RunTurns(20);

            Assert.True(Degrees(Facing.AbsoluteDelta(unit.Facing, ordered)) < 20f,
                $"Turning to squeeze through something is not a change of plan. It came out the far side " +
                $"{Degrees(Facing.AbsoluteDelta(unit.Facing, ordered)):0}° off the front it was given.");
        }

        [Fact]
        public void ARegimentThatCannotFitAnywhereHaltsAndSaysSo()
        {
            // A wall with no gap at all. The pathfinder would refuse this
            // outright, so the route is laid by hand — which is precisely the
            // case the check exists for: a march planned for a point, handed to
            // a shape that cannot take it.
            var field = new Battlefield("plains", 23500, RuleSet.Full, canvas =>
                canvas.Band(canvas.Columns / 2, canvas.Columns / 2 + 1, "deepwater"));

            UnitInstance unit = field.Add(0, "cavalry", field.Centre - new Vec2(200f, 0f), Facing.East);

            SendWithoutAskingTheRouter(unit, field.Centre + new Vec2(200f, 0f));
            field.RunTurns(8);

            Assert.True(unit.Position.X < field.Centre.X,
                $"Nothing on land crosses deep water at any bearing — it reached x={unit.Position.X:0} " +
                $"against water starting at x={field.Centre.X:0}.");

            Assert.True(field.TimesSaid("cannot get its whole frontage past") > 0,
                "And the reason has to be on screen, or this is indistinguishable from a unit that has " +
                "stopped taking orders.");
        }

        /// <summary>
        /// Lays a straight route by hand, bypassing the pathfinder.
        /// </summary>
        /// <remarks>
        /// The router already refuses goals it cannot reach, so a route into
        /// something impassable can only be built deliberately. That makes it
        /// the right way to test the movement rule's own last line of defence
        /// rather than the router's first.
        /// </remarks>
        private static void SendWithoutAskingTheRouter(UnitInstance unit, Vec2 destination)
        {
            unit.Stance = Stance.Advance;
            unit.GiveOrder(UnitOrder.MoveTo(destination), unit.Position);
            unit.Route = new MovementRoute(new[] { unit.Position, destination }, wheelFirst: false);
        }

        // ---- Not at the cost of everything else --------------------------------

        [Fact]
        public void ARegimentAlreadyStandingOnBadGroundCanStillWalkOffIt()
        {
            var field = new Battlefield("plains", 23600, RuleSet.Full, canvas =>
                canvas.Band(canvas.Columns / 2, canvas.Columns - 1, "deepwater"));

            // Centre on dry land but part of the frontage in the water, which is
            // the state an older rule or a deployment could easily leave. A
            // centre actually in the water is a different matter — movement has
            // always halted for that, and rightly.
            //
            // Set back by a fraction of the regiment's own frontage rather than
            // a fixed thirty metres, so it stays half in the water whatever size
            // the rectangle is.
            UnitInstance unit = field.Add(0, "cavalry", field.Centre, Facing.North);

            unit.Position = field.Centre - new Vec2(unit.Footprint.HalfWidth * 0.55f, 0f);

            Assert.False(field.State.FormationFits(unit, unit.Position, unit.Facing),
                "It is standing somewhere it should not be.");

            Vec2 start = unit.Position;

            // By hand again: the router will not plan from a cell nothing can
            // stand in, which is exactly the position this unit is in.
            SendWithoutAskingTheRouter(unit, field.Centre - new Vec2(300f, 0f));
            field.RunTurns(6);

            Assert.True(Vec2.Distance(unit.Position, start) > 100f,
                $"A unit that cannot legally be where it is must still be able to leave, or it is stuck " +
                $"for the whole battle. It moved {Vec2.Distance(unit.Position, start):0} m.");
        }

        private static float Degrees(float radians) => radians * 180f / MathF.PI;
    }
}
