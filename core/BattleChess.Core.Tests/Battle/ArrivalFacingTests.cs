using System;
using BattleChess.Contracts;
using BattleChess.Rules;
using Xunit;

namespace BattleChess.Tests.Battle
{
    /// <summary>
    /// Which way a regiment is pointing when it gets there.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two wrong answers were tried before this one. Facing began as a pure
    /// by-product of the march bearing, so moving a line fifty metres to its
    /// left pivoted the whole frontage through a right angle. Correcting that
    /// went too far the other way: a regiment then kept its front through
    /// <i>any</i> move, so one ordered backwards walked backwards, and one sent
    /// off at twenty degrees and then at fifty kept the twenty.
    /// </para>
    /// <para>
    /// What settles it is distance, not angle. A body of men told to go
    /// somewhere turns and marches there; the exception is the short
    /// reposition, where swinging a line round to shuffle forty metres costs
    /// far more than crabbing sideways does. Holding a front across a real
    /// march is still available and still slow — it just has to be asked for.
    /// </para>
    /// </remarks>
    public sealed class ArrivalFacingTests
    {
        [Fact]
        public void AShortRepositionLeavesTheFrontWhereItWas()
        {
            var field = new Battlefield("plains", 22000);

            UnitInstance unit = field.Add(0, "cavalry", field.Centre, Facing.East);
            Facing before = unit.Facing;

            // A nudge to its left, a fraction of the regiment's own width.
            // Coming about for this would spin the whole frontage through a
            // right angle to cover a few strides.
            field.March(unit, field.Centre + new Vec2(0f, unit.Footprint.Width * 0.3f));
            field.RunTurns(3);

            Assert.True(Degrees(Facing.AbsoluteDelta(unit.Facing, before)) < 5f,
                $"It set off facing {before.Degrees:0}° and ended facing {unit.Facing.Degrees:0}°. " +
                "A shuffle sideways is not a change of front.");
        }

        [Fact]
        public void ARealMarchTurnsToFaceTheWayItIsGoing()
        {
            var field = new Battlefield("plains", 22050);

            UnitInstance unit = field.Add(0, "cavalry", field.Centre, Facing.East);

            field.March(unit, field.Centre + new Vec2(0f, 220f));
            field.RunTurns(4);

            Assert.True(Degrees(Facing.AbsoluteDelta(unit.Facing, Facing.North)) < 10f,
                $"Told to march north, it should be marching north rather than crabbing there sideways at " +
                $"a fifth of its pace. It is facing {unit.Facing.Degrees:0}°.");
        }

        [Fact]
        public void ASecondOrderOnANewBearingIsObeyed()
        {
            var field = new Battlefield("plains", 22060);

            UnitInstance unit = field.Add(0, "cavalry", field.Centre, Facing.East);

            // Off at a shallow angle, then redirected to a steeper one. Neither
            // leg is a big enough swing to count as turning about, which is
            // exactly why gating this on angle left the regiment stuck on the
            // bearing it happened to start with.
            field.March(unit, field.Centre + Bearing(20f, 300f));
            field.RunTurns(2);

            field.March(unit, unit.Position + Bearing(50f, 300f));
            field.RunTurns(3);

            Assert.True(Degrees(Facing.AbsoluteDelta(unit.Facing, Facing.FromDegrees(50f))) < 10f,
                $"Sent off at 20° and then at 50°, it is still facing {unit.Facing.Degrees:0}°.");
        }

        private static Vec2 Bearing(float degrees, float metres) =>
            Facing.FromDegrees(degrees).ToVector() * metres;

        [Fact]
        public void AMoveWithABearingComesRoundOnArrival()
        {
            var field = new Battlefield("plains", 22100);

            UnitInstance unit = field.Add(0, "cavalry", field.Centre, Facing.East);

            Vec2 destination = field.Centre + new Vec2(0f, 220f);

            unit.Stance = Stance.Advance;
            unit.GiveOrder(UnitOrder.MoveTo(destination, bearing: Facing.North), unit.Position);
            unit.Route = new MovementRoute(new[] { unit.Position, destination }, wheelFirst: false);

            field.RunTurns(6);

            Assert.True(Degrees(Facing.AbsoluteDelta(unit.Facing, Facing.North)) < 10f,
                $"Asked to arrive facing north, it arrived facing {unit.Facing.Degrees:0}°.");
        }

        [Fact]
        public void HoldingYourFrontWhileYouSidestepIsSlow()
        {
            float forwards = MetresCoveredIn(2, Facing.North);
            float sideways = MetresCoveredIn(2, Facing.East);

            Assert.True(sideways < forwards * 0.6f,
                $"Edging a formation along without changing front has to cost something, or keeping " +
                $"your facing is free: {sideways:0} m sideways against {forwards:0} m forwards.");

            Assert.True(sideways > 0f, "But it still moves.");
        }

        /// <summary>
        /// Marches a regiment due north for a number of turns, holding a given
        /// front the whole way.
        /// </summary>
        /// <remarks>
        /// The front is asked for explicitly, because that is now the only way
        /// to hold one across a march this long — a plain order would turn the
        /// regiment north and there would be no penalty left to measure. Which
        /// is the point: crabbing along is a manoeuvre somebody chooses and
        /// pays for, not something a regiment falls into by accident.
        /// </remarks>
        private static float MetresCoveredIn(int turns, Facing facing)
        {
            var field = new Battlefield("plains", 22200);

            UnitInstance unit = field.Add(0, "cavalry", field.Centre - new Vec2(0f, 300f), facing);
            Vec2 start = unit.Position;

            field.March(unit, field.Centre + new Vec2(0f, 300f), bearing: facing);
            field.RunTurns(turns);

            return Vec2.Distance(unit.Position, start);
        }

        // ---- Attacks are different ---------------------------------------------

        [Fact]
        public void AnAttackStillFacesWhatItIsCharging()
        {
            var field = new Battlefield("plains", 22300);

            UnitInstance quarry = field.Add(1, "swordsmen", field.Centre + new Vec2(0f, 240f), Facing.South);
            Battlefield.Hold(quarry);

            UnitInstance horse = field.Add(0, "cavalry", field.Centre, Facing.East);
            Battlefield.Press(horse, quarry);

            field.RunTurns(5);

            float off = Facing.AbsoluteDelta(horse.Facing, Facing.Towards(horse.Position, quarry.Position));

            Assert.True(Degrees(off) < 25f,
                $"Nobody charges home sideways. It ended {Degrees(off):0}° off its target.");
        }

        [Fact]
        public void RoutersStillTurnAndGo()
        {
            var field = new Battlefield("plains", 22400);

            UnitInstance runner = field.Add(0, "swordsmen", field.Centre, Facing.East);
            UnitInstance chaser = field.Add(1, "cavalry", field.Centre + new Vec2(60f, 0f), Facing.West);

            Battlefield.Press(chaser, runner);

            runner.Morale = 0.05f;
            runner.State = UnitState.Routing;

            field.RunTurns(2);

            Assert.True(runner.Position.X < field.Centre.X,
                "Men running do not hold their front out of politeness — they turn and go.");
        }

        private static float Degrees(float radians) => radians * 180f / MathF.PI;
    }
}
