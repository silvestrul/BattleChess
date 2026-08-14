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
        public void ARegimentSquarelyInTheWayFallsBackToTheSearch()
        {
            var field = new Battlefield("plains", 31100);

            UnitInstance unit = field.Add(0, "swordsmen", field.Centre - new Vec2(250f, 0f), Facing.East);

            // Planted halfway along, square across the line of march.
            field.Add(0, "spearmen", field.Centre, Facing.East);

            PathResult route = Marching.PlanTo(field.State, unit, field.Pathfinder, field.Centre + new Vec2(250f, 0f));

            _out.WriteLine($"{route.Waypoints.Count} waypoints, {route.CellsExplored} cells explored.");

            // The shortcut must not fire. What the search then does about it is
            // the next pass's business — here it only matters that the question
            // got asked of something that can answer it.
            Assert.True(route.CellsExplored > 0,
                "A regiment standing across the line was not noticed, so the march was planned straight " +
                "through it.");
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
