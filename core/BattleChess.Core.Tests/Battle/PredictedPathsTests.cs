using BattleChess.Contracts;
using BattleChess.Rules;
using Xunit;
using Xunit.Abstractions;

namespace BattleChess.Tests.Battle
{
    /// <summary>
    /// Asking where a regiment will be, not where it is.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>M16.</b> A body that will have walked on by the time you arrive is not
    /// in your way, and one nowhere near you now may be squarely in it in twenty
    /// seconds. Planning against where everybody happens to be standing at the
    /// moment the order is given makes an army step around ghosts and walk into
    /// each other.
    /// </para>
    /// <para>
    /// Only friendly regiments are predicted, and that is <b>M16a</b> holding
    /// rather than being ignored. An enemy's orders are not ours to read — the
    /// rules layer has both armies' true state, so predicting one from its
    /// actual orders would have a regiment dodge a manoeuvre its own player
    /// cannot see, and no fog leak test would catch it because nothing crosses
    /// the wire. It happens to cost nothing here: enemies are not planning
    /// obstacles at all, so there is nothing to predict them for.
    /// </para>
    /// </remarks>
    public sealed class PredictedPathsTests
    {
        private readonly ITestOutputHelper _out;

        public PredictedPathsTests(ITestOutputHelper output) => _out = output;

        [Fact(Skip = "M16 not built — see finding 9.")]
        public void OneOfItsOwnThatWillHaveMarchedOnIsNotGoneRound()
        {
            var field = new Battlefield("plains", 33000);

            // Squarely on the line, and leaving at four times the marcher's
            // pace. Long before the swordsmen get there it is a hundred metres
            // north and the straight line is open.
            UnitInstance crossing = field.Add(0, "cavalry", field.Centre, Facing.North);
            field.March(crossing, field.Centre + new Vec2(0f, 300f));

            UnitInstance mover = field.Add(0, "swordsmen", field.Centre - new Vec2(300f, 0f), Facing.East);

            PathResult route = Marching.PlanTo(
                field.State, mover, field.Pathfinder, field.Centre + new Vec2(300f, 0f));

            foreach (Vec2 w in route.Waypoints) _out.WriteLine($"  ({w.X:0},{w.Y:0})");

            Assert.Equal(2, route.Waypoints.Count);
        }

        [Fact]
        public void OneOfItsOwnStandingStillIsStillGoneRound()
        {
            var field = new Battlefield("plains", 33100);

            // The same regiment in the same place, with nowhere to be. Nothing
            // about prediction should make a body that is not moving invisible.
            UnitInstance sitting = field.Add(0, "cavalry", field.Centre, Facing.North);
            Battlefield.Hold(sitting);

            UnitInstance mover = field.Add(0, "swordsmen", field.Centre - new Vec2(300f, 0f), Facing.East);

            PathResult route = Marching.PlanTo(
                field.State, mover, field.Pathfinder, field.Centre + new Vec2(300f, 0f));

            Assert.Equal(3, route.Waypoints.Count);
        }

        [Fact(Skip = "M16 not built — see finding 9.")]
        public void OneOfItsOwnThatWillArriveInTheWayIsGoneRoundBeforeItGetsThere()
        {
            var field = new Battlefield("plains", 33200);

            // Nowhere near the line now — two hundred metres south of it — and
            // marching to sit squarely across it. Planning against where it
            // stands at this instant says the way is clear, and it is not.
            UnitInstance closing = field.Add(0, "cavalry", field.Centre - new Vec2(0f, 200f), Facing.North);
            field.March(closing, field.Centre);

            UnitInstance mover = field.Add(0, "swordsmen", field.Centre - new Vec2(300f, 0f), Facing.East);

            PathResult route = Marching.PlanTo(
                field.State, mover, field.Pathfinder, field.Centre + new Vec2(300f, 0f));

            foreach (Vec2 w in route.Waypoints) _out.WriteLine($"  ({w.X:0},{w.Y:0})");

            Assert.True(route.Waypoints.Count > 2,
                "It planned straight through ground one of its own is on its way to, and which it will " +
                "be standing on well before the march arrives.");
        }
    }
}
