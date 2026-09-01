using System;
using BattleChess.Contracts;
using BattleChess.Rules;
using Xunit;
using Xunit.Abstractions;

namespace BattleChess.Tests.Battle
{
    /// <summary>
    /// Orders are drawn, held, and given all at once when the turn ends.
    /// </summary>
    /// <remarks>
    /// <b>[M143].</b> Clicking a regiment plans a route rather than setting it
    /// walking. The interesting requirement is the designer's: each order is
    /// planned against the ones already queued, so a regiment routes through
    /// the gap that <i>will</i> exist rather than the one that does.
    /// </remarks>
    public sealed class TurnOrdersTests
    {
        private readonly ITestOutputHelper _out;

        public TurnOrdersTests(ITestOutputHelper output) => _out = output;

        [Fact]
        public void DrawingAnOrderDoesNotSetTheRegimentWalking()
        {
            var field = new Battlefield("plains", 7001);
            var book = new TurnOrders();

            UnitInstance foot = field.Add(0, "swordsmen", field.Centre - new Vec2(200f, 0f), Facing.East);
            Vec2 goal = field.Centre + new Vec2(200f, 0f);
            Vec2 stood = foot.Position;

            Assert.True(book.Draw(field.State, foot, UnitOrder.MoveTo(goal), field.Pathfinder));

            Assert.Equal(1, book.Count);
            Assert.Null(foot.Route);
            Assert.Equal(OrderKind.Stand, foot.Order.Kind);

            field.RunTurns(2);

            _out.WriteLine($"after two turns of holding: moved {Vec2.Distance(foot.Position, stood):0.0} m");

            Assert.True(Vec2.Distance(foot.Position, stood) < 1f,
                "A drawn order is not a given one. The regiment walked before the turn was ended.");
        }

        [Fact]
        public void EndingTheTurnGivesEveryDrawnOrderAtOnce()
        {
            var field = new Battlefield("plains", 7002);
            var book = new TurnOrders();

            UnitInstance a = field.Add(0, "swordsmen", field.Centre - new Vec2(300f, 100f), Facing.East);
            UnitInstance b = field.Add(0, "spearmen", field.Centre - new Vec2(300f, -100f), Facing.East);

            book.Draw(field.State, a, UnitOrder.MoveTo(field.Centre + new Vec2(300f, 100f)), field.Pathfinder);
            book.Draw(field.State, b, UnitOrder.MoveTo(field.Centre + new Vec2(300f, -100f)), field.Pathfinder);

            Assert.Equal(2, book.Count);

            int given = book.Fire(field.State);

            _out.WriteLine($"{given} orders given; book now holds {book.Count}");

            Assert.Equal(2, given);
            Assert.Equal(0, book.Count);
            Assert.True(a.IsMarching && b.IsMarching, "Both should be walking once the turn is ended.");
        }

        /// <summary>
        /// The designer's requirement: a later order sees where the earlier
        /// ones finish.
        /// </summary>
        /// <remarks>
        /// One regiment is standing squarely on another's road and is ordered
        /// out of the way first. The second order must then be able to go
        /// straight through the ground it is vacating, rather than round the
        /// body that is about to leave it.
        /// </remarks>
        [Fact]
        public void AQueuedOrderIsPlannedAgainstWhereTheEarlierOnesFinish()
        {
            Vec2 goal;

            // What the second order looks like with the first one queued.
            var field = new Battlefield("plains", 7003);
            var book = new TurnOrders();

            UnitInstance blocker = field.Add(0, "spearmen", field.Centre, Facing.North);
            UnitInstance foot = field.Add(0, "swordsmen", field.Centre - new Vec2(260f, 0f), Facing.East);
            goal = field.Centre + new Vec2(260f, 0f);

            // Non-vacuity first: with nobody ordered out of the way, this march
            // has to bend. If it does not, the arrangement proves nothing (W9).
            Assert.True(
                book.Draw(field.State, foot, UnitOrder.MoveTo(goal), field.Pathfinder),
                "The plain march should at least be plannable.");

            int bendsWithHimThere = book.Drawn[0].Plan.Path.Waypoints.Count;

            _out.WriteLine($"with the spearmen standing there: {bendsWithHimThere} waypoints");

            Assert.True(bendsWithHimThere > 2,
                $"The spearmen have to be genuinely in the way for this to test anything, and the route " +
                $"has {bendsWithHimThere} waypoints - it went straight through.");

            // Now order the blocker away first, then redraw the march.
            book.RubEverything();

            Assert.True(book.Draw(
                field.State, blocker,
                UnitOrder.MoveTo(field.Centre + new Vec2(0f, 300f)), field.Pathfinder));

            Assert.True(book.Draw(field.State, foot, UnitOrder.MoveTo(goal), field.Pathfinder));

            int bendsOnceHeLeaves = book.Drawn[1].Plan.Path.Waypoints.Count;

            _out.WriteLine($"with the spearmen ordered away first: {bendsOnceHeLeaves} waypoints");

            Assert.True(bendsOnceHeLeaves < bendsWithHimThere,
                $"The spearmen were ordered off that ground before this march was drawn, so it should " +
                $"route through the gap that will exist. It still has {bendsOnceHeLeaves} waypoints " +
                $"against {bendsWithHimThere} - the queue is not being planned against.");
        }

        /// <summary>
        /// Rubbing out an order redraws the ones that were planned against it.
        /// </summary>
        [Fact]
        public void TakingAnOrderBackRedrawsTheOnesThatFollowedIt()
        {
            var field = new Battlefield("plains", 7004);
            var book = new TurnOrders();

            UnitInstance blocker = field.Add(0, "spearmen", field.Centre, Facing.North);
            UnitInstance foot = field.Add(0, "swordsmen", field.Centre - new Vec2(260f, 0f), Facing.East);
            Vec2 goal = field.Centre + new Vec2(260f, 0f);

            book.Draw(field.State, blocker, UnitOrder.MoveTo(field.Centre + new Vec2(0f, 300f)), field.Pathfinder);
            book.Draw(field.State, foot, UnitOrder.MoveTo(goal), field.Pathfinder);

            int straight = book.Drawn[1].Plan.Path.Waypoints.Count;

            Assert.True(book.Rub(field.State, blocker.Id, field.Pathfinder));

            int bent = book.Drawn[0].Plan.Path.Waypoints.Count;

            _out.WriteLine($"{straight} waypoints while he was leaving, {bent} once that order was rubbed out");

            Assert.Equal(1, book.Count);

            Assert.True(bent > straight,
                $"The order it was planned around has been taken back, so the march has to go round him " +
                $"again. It still has {bent} waypoints against {straight}.");
        }

        /// <summary>
        /// A second order for the same regiment replaces the first.
        /// </summary>
        /// <remarks>
        /// One regiment, one instruction a turn. Stacking them would make the
        /// field show a route the regiment is not going to walk.
        /// </remarks>
        [Fact]
        public void OrderingTheSameRegimentTwiceReplacesRatherThanStacks()
        {
            var field = new Battlefield("plains", 7005);
            var book = new TurnOrders();

            UnitInstance foot = field.Add(0, "swordsmen", field.Centre - new Vec2(200f, 0f), Facing.East);

            book.Draw(field.State, foot, UnitOrder.MoveTo(field.Centre), field.Pathfinder);
            book.Draw(field.State, foot, UnitOrder.MoveTo(field.Centre + new Vec2(200f, 0f)), field.Pathfinder);

            Assert.Equal(1, book.Count);

            _out.WriteLine($"the order kept ends at {book.Drawn[0].Ends}");

            Assert.True(
                Vec2.Distance(book.Drawn[0].Ends, field.Centre + new Vec2(200f, 0f)) < 30f,
                "The second order should be the one kept.");
        }

        /// <summary>
        /// Planning against the queue must leave the battle exactly as it found
        /// it.
        /// </summary>
        /// <remarks>
        /// The queued regiments are stood at their finishing places while a new
        /// order is drawn, which is a write to the battle that [Mx6a] forbids
        /// inside a plan. It is allowed here because it is the phase around the
        /// planner rather than the planner itself - but only if every position
        /// comes back. This is the test that says so.
        /// </remarks>
        [Fact]
        public void DrawingAnOrderLeavesEverybodyStandingWhereTheyWere()
        {
            var field = new Battlefield("plains", 7006);
            var book = new TurnOrders();

            UnitInstance a = field.Add(0, "spearmen", field.Centre, Facing.North);
            UnitInstance b = field.Add(0, "swordsmen", field.Centre - new Vec2(260f, 0f), Facing.East);
            UnitInstance c = field.Add(0, "archers", field.Centre - new Vec2(120f, 200f), Facing.East);

            Vec2 wasA = a.Position, wasB = b.Position, wasC = c.Position;

            book.Draw(field.State, a, UnitOrder.MoveTo(field.Centre + new Vec2(0f, 300f)), field.Pathfinder);
            book.Draw(field.State, b, UnitOrder.MoveTo(field.Centre + new Vec2(260f, 0f)), field.Pathfinder);
            book.Draw(field.State, c, UnitOrder.MoveTo(field.Centre + new Vec2(200f, 200f)), field.Pathfinder);

            _out.WriteLine(
                $"drift: a {Vec2.Distance(a.Position, wasA):0.000} m, " +
                $"b {Vec2.Distance(b.Position, wasB):0.000} m, " +
                $"c {Vec2.Distance(c.Position, wasC):0.000} m");

            Assert.True(Vec2.Distance(a.Position, wasA) < 0.001f, "Drawing orders moved a regiment.");
            Assert.True(Vec2.Distance(b.Position, wasB) < 0.001f, "Drawing orders moved a regiment.");
            Assert.True(Vec2.Distance(c.Position, wasC) < 0.001f, "Drawing orders moved a regiment.");
        }
    }
}
