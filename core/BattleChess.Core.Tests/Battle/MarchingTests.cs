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
