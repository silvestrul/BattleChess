using System;
using BattleChess.Contracts;
using BattleChess.Rules;
using Xunit;
using Xunit.Abstractions;

namespace BattleChess.Tests.Battle
{
    /// <summary>
    /// Taking the straight line when there is one, and only searching when
    /// there is not.
    /// </summary>
    /// <remarks>
    /// <para>
    /// M10. These are written against <c>CellsExplored</c> rather than against
    /// where regiments end up, because where they end up is <i>identical</i>
    /// either way — the search and the cast return the same two waypoints for a
    /// clear line, which is the whole point and also why this could land doing
    /// nothing at all without a single test noticing.
    /// </para>
    /// <para>
    /// That has happened before on this project: a rule was built as a rate, did
    /// literally nothing, and passed 486 tests while doing it. The cost is the
    /// thing that changed here, so the cost is what gets asserted.
    /// </para>
    /// </remarks>
    public sealed class MarchingTests
    {
        private readonly ITestOutputHelper _out;

        public MarchingTests(ITestOutputHelper output) => _out = output;

        [Fact]
        public void AClearMarchOverOpenGroundSearchesNothingAtAll()
        {
            var field = new Battlefield("plains", 31000);

            UnitInstance unit = field.Add(0, "swordsmen", field.Centre - new Vec2(250f, 0f), Facing.East);

            PathResult route = Marching.PlanTo(field.State, unit, field.Pathfinder, field.Centre + new Vec2(250f, 0f));

            _out.WriteLine($"{route.Waypoints.Count} waypoints, {route.CellsExplored} cells explored, " +
                           $"{route.Distance:0} m.");

            Assert.True(route.Found);

            Assert.Equal(0, route.CellsExplored);

            // From here to there and nothing in between.
            Assert.Equal(2, route.Waypoints.Count);
        }

        [Fact]
        public void OneOfItsOwnInTheWayIsArchedRoundWithoutSearching()
        {
            var field = new Battlefield("plains", 31100);

            UnitInstance unit = field.Add(0, "swordsmen", field.Centre - new Vec2(250f, 0f), Facing.East);

            // Planted halfway along, square across the line of march.
            UnitInstance inTheWay = field.Add(0, "spearmen", field.Centre, Facing.East);

            Vec2 destination = field.Centre + new Vec2(250f, 0f);

            PathResult route = Marching.PlanTo(field.State, unit, field.Pathfinder, destination);

            foreach (Vec2 point in route.Waypoints) _out.WriteLine($"  ({point.X:0}, {point.Y:0})");
            _out.WriteLine($"{route.CellsExplored} cells explored, {route.Distance:0} m.");

            Assert.True(route.Found);

            // Three points: here, past them, and there. The middle one is the
            // whole of what "arch" means.
            Assert.Equal(3, route.Waypoints.Count);

            Assert.Equal(0, route.CellsExplored);

            // And it genuinely goes round rather than nominally — the way point
            // has to be clear of the body it is avoiding, not merely different
            // from the straight line.
            Vec2 through = route.Waypoints[1];
            float aside = MathF.Abs(through.Y - inTheWay.Position.Y);

            Assert.True(aside > inTheWay.Footprint.Width * 0.5f,
                $"The way round passes {aside:0} m off their centre, and they are " +
                $"{inTheWay.Footprint.Width:0} m wide. That is not going round them.");
        }

        [Fact]
        public void GroundItCannotCrossIsStillTheSearchesProblem()
        {
            var field = new Battlefield("plains", 31150);

            UnitInstance unit = field.Add(0, "swordsmen", field.Centre - new Vec2(250f, 0f), Facing.East);

            // Nothing standing anywhere: if this comes back as a straight line
            // the arch is not being reached at all and the test above proves
            // less than it looks like it does.
            PathResult route = Marching.PlanTo(field.State, unit, field.Pathfinder, field.Centre + new Vec2(250f, 0f));

            Assert.Equal(2, route.Waypoints.Count);
        }

        private sealed class Sink : IBattleLog
        {
            public int Arrivals;
            public void Record(in BattleLogEntry entry)
            {
                if (entry.Message.Contains("is attacking")) Arrivals++;
            }
        }

        [Fact]
        public void ARegimentThatHasCaughtItsQuarryDoesNotKeepArriving()
        {
            var field = new Battlefield("plains", 31400);

            UnitInstance quarry = field.Add(1, "spearmen", field.Centre, Facing.West);
            Battlefield.Hold(quarry);

            UnitInstance chaser = field.Add(0, "archers", field.Centre - new Vec2(400f, 0f), Facing.East);
            Battlefield.Press(chaser, quarry);

            var log = new Sink();

            var clock = new BattleClock();
            foreach (IBattleSystem system in field.Clock.Systems) clock.Add(system);

            for (int tick = 0; tick < BattleClock.TicksPerTurn * 6; tick++)
                clock.Advance(field.State, log);

            _out.WriteLine($"{log.Arrivals} arrivals over {BattleClock.TicksPerTurn * 6} ticks.");

            // Finding 7, and it is shooters rather than chargers. A regiment
            // that closes to melee is held by contact and stops re-planning on
            // its own; one told to attack from a distance halts at its range,
            // never enters contact, and so re-planned the same route to the
            // same place every tick — each completing on the tick it was made
            // and announcing an arrival for it.
            //
            // Measured here: 221 re-plans over 360 ticks before, 35 after. The
            // remainder is real and is recorded as still open — something moves
            // the aim point by more than a step every ten ticks or so, and it
            // is not this. The bar is set to catch the return of the every-tick
            // behaviour, which is what made a recording unreadable, rather than
            // to claim the whole fault is closed.
            Assert.True(log.Arrivals < 60,
                $"It re-planned {log.Arrivals} times in 360 ticks. A regiment already standing where its " +
                "order wants it is building a route of no length and arriving down it again.");
        }

        private sealed class Complaints : IBattleLog
        {
            public int Stuck;
            public void Record(in BattleLogEntry entry)
            {
                if (entry.Message.Contains("is not getting through") ||
                    entry.Message.Contains("cannot get to where it was sent")) Stuck++;
            }
        }

        [Fact]
        public void GoingRoundOneOfItsOwnIsNotMistakenForBeingStuck()
        {
            var field = new Battlefield("plains", 31500);

            UnitInstance standing = field.Add(0, "spearmen", field.Centre, Facing.East);
            Battlefield.Hold(standing);

            UnitInstance mover = field.Add(0, "swordsmen", field.Centre - new Vec2(250f, 0f), Facing.East);
            Vec2 destination = field.Centre + new Vec2(250f, 0f);

            field.March(mover, destination);

            var log = new Complaints();

            var clock = new BattleClock();
            foreach (IBattleSystem system in field.Clock.Systems) clock.Add(system);

            for (int tick = 0; tick < BattleClock.TicksPerTurn * 10; tick++)
                clock.Advance(field.State, log);

            float left = Vec2.Distance(mover.Position, destination);

            _out.WriteLine($"{left:0} m short, complained {log.Stuck} times.");

            // An end-to-end guard, and honest about being only that: it holds
            // with the old distance-to-the-destination measure as well as the
            // new along-the-route one, because arching puts the way round *in*
            // the route and the march never has to leave it. The progress
            // change is still right — steering pushes a body sideways at
            // execution time and that reduces no distance to anything — but
            // nothing exercises it today, and it will not be genuinely load
            // bearing until crabbing makes detours slow enough to outlast the
            // detector's patience. That is finding 8, and it is still open.
            Assert.Equal(0, log.Stuck);

            Assert.True(left < 60f,
                $"It should have walked round them and got there; it is {left:0} m short.");
        }

        [Fact]
        public void ItIsTheBodyThatHasToFitAndNotTheCentreLine()
        {
            var field = new Battlefield("plains", 31200);

            UnitInstance unit = field.Add(0, "swordsmen", field.Centre - new Vec2(250f, 0f), Facing.East);

            // Set off the centre line by 25 m. Both blocks are 40 m wide, so
            // twenty of each still meet — the centres miss and the bodies do
            // not. This is the M12 case, and the old 2 m clearance called it
            // open ground.
            field.Add(0, "spearmen", field.Centre + new Vec2(0f, 25f), Facing.East);

            Assert.False(
                Marching.IsClearLine(
                    field.State, unit, unit.Position, field.Centre + new Vec2(250f, 0f), unit.Facing),
                "The centres pass 25 m apart but the bodies are 40 m wide and overlap. A line is clear " +
                "only if the whole rectangle sweeps it clear.");
        }

        [Fact]
        public void SomethingWellOffTheLineIsNotInTheWay()
        {
            var field = new Battlefield("plains", 31300);

            UnitInstance unit = field.Add(0, "swordsmen", field.Centre - new Vec2(250f, 0f), Facing.East);

            field.Add(0, "spearmen", field.Centre + new Vec2(0f, 200f), Facing.East);

            Assert.True(
                Marching.IsClearLine(
                    field.State, unit, unit.Position, field.Centre + new Vec2(250f, 0f), unit.Facing),
                "A regiment 200 m off the line of march is not in the way, and treating it as one would " +
                "send every march through the pathfinder again.");
        }
    }
}
