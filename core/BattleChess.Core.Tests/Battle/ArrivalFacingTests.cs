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
    /// Facing used to be a by-product of the march bearing: a unit always
    /// turned to point wherever it was going, so moving a line fifty metres to
    /// its left pivoted the whole frontage ninety degrees. For a game about
    /// bodies of men rather than tokens that is the wrong default — almost
    /// every order means "go there", not "go there and change front".
    /// </para>
    /// <para>
    /// Where a regiment looks and where it walks are now separate. The price of
    /// holding your front while moving off your bearing is speed, which the
    /// alignment penalty was already charging and nothing was ever choosing to
    /// pay.
    /// </para>
    /// </remarks>
    public sealed class ArrivalFacingTests
    {
        [Fact]
        public void APlainMoveLeavesTheFrontWhereItWas()
        {
            var field = new Battlefield("plains", 22000);

            UnitInstance unit = field.Add(0, "cavalry", field.Centre, Facing.East);
            Facing before = unit.Facing;

            // Straight up the field — the order that used to spin a hundred and
            // ten metres of frontage through a right angle.
            field.March(unit, field.Centre + new Vec2(0f, 220f));
            field.RunTurns(6);

            Assert.True(Degrees(Facing.AbsoluteDelta(unit.Facing, before)) < 5f,
                $"It set off facing {before.Degrees:0}° and arrived facing {unit.Facing.Degrees:0}°. " +
                "A move order is not a change of front.");
        }

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
        /// Marches a regiment due north for a number of turns, starting from a
        /// given facing and never changing it.
        /// </summary>
        private static float MetresCoveredIn(int turns, Facing facing)
        {
            var field = new Battlefield("plains", 22200);

            UnitInstance unit = field.Add(0, "cavalry", field.Centre - new Vec2(0f, 300f), facing);
            Vec2 start = unit.Position;

            field.March(unit, field.Centre + new Vec2(0f, 300f));
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
