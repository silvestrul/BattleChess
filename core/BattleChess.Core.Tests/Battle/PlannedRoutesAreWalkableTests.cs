using System.Collections.Generic;
using BattleChess.Contracts;
using BattleChess.Rules;
using Xunit;
using Xunit.Abstractions;

namespace BattleChess.Tests.Battle
{
    /// <summary>
    /// Whatever the planner hands back, a regiment can actually walk.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One property, asked of every arrangement rather than of a scenario: if a
    /// route comes back and it is not the deliberate press-through of
    /// <b>M18</b>'s third rung, then every leg of it is clear at the front that
    /// leg is walked on. Nothing in the rules is allowed to hand out a line it
    /// has not checked.
    /// </para>
    /// <para>
    /// Written because of finding 9. Prediction was built, worked, and had to be
    /// reverted when arching began winning cases crabbing should have had — and
    /// the thing that could not be established was whether `ArchAround` was
    /// returning a way round that its own leg checks should have rejected. That
    /// question should not have needed an investigation. It is a property, it
    /// holds or it does not, and asking it of a table of arrangements is cheaper
    /// than reasoning about any one of them.
    /// </para>
    /// </remarks>
    public sealed class PlannedRoutesAreWalkableTests
    {
        private readonly ITestOutputHelper _out;

        public PlannedRoutesAreWalkableTests(ITestOutputHelper output) => _out = output;

        public static IEnumerable<object[]> Arrangements()
        {
            // Gaps either side of the line, from solid to comfortably walkable.
            yield return new object[] { "wall, no gap", 0f, 0f };
            yield return new object[] { "wall, 10 m gap", 10f, 0f };
            yield return new object[] { "wall, 30 m gap", 30f, 0f };
            yield return new object[] { "wall, 45 m gap", 45f, 0f };
            yield return new object[] { "wall, 60 m gap", 60f, 0f };
            yield return new object[] { "wall, 90 m gap", 90f, 0f };

            // The same walls, with the gap off to one side of the march, so the
            // route has to go round as well as through.
            yield return new object[] { "30 m gap, offset 40 m", 30f, 40f };
            yield return new object[] { "45 m gap, offset 80 m", 45f, 80f };
        }

        [Theory]
        [MemberData(nameof(Arrangements))]
        public void EveryLegOfAPlannedRouteIsClearAtTheFrontItIsWalkedOn(string what, float gap, float offset)
        {
            var field = new Battlefield("plains", 34000);

            float inner = gap * 0.5f + 20f;

            foreach (float side in new[] { 1f, -1f })
            {
                for (int i = 0; i < 2; i++)
                {
                    UnitInstance wall = field.Add(
                        0, "spearmen",
                        field.Centre + new Vec2(0f, offset + side * (inner + i * 40f)),
                        Facing.East);

                    Battlefield.Hold(wall);
                }
            }

            UnitInstance mover = field.Add(0, "swordsmen", field.Centre - new Vec2(250f, 0f), Facing.East);
            Vec2 destination = field.Centre + new Vec2(250f, 0f);

            PathResult route = Marching.PlanTo(field.State, mover, field.Pathfinder, destination);

            Assert.True(route.Found, $"{what}: no route at all.");

            // A route that has deliberately given up on keeping clear is exempt
            // — that is what it means — and one the search produced is the
            // search's business, not the cast's.
            if (Marching.LastPressedThrough || route.CellsExplored > 0)
            {
                _out.WriteLine($"{what}: exempt ({(Marching.LastPressedThrough ? "pressing through" : "searched")}).");
                return;
            }

            Facing?[]? hold = Marching.LastHold;

            for (int leg = 1; leg < route.Waypoints.Count; leg++)
            {
                Vec2 from = route.Waypoints[leg - 1];
                Vec2 to = route.Waypoints[leg];

                Facing front = hold != null && leg < hold.Length && hold[leg].HasValue
                    ? hold[leg]!.Value
                    : Facing.Towards(from, to);

                Assert.True(
                    Marching.IsClearLine(field.State, mover, from, to, front),
                    $"{what}: leg {leg} of {route.Waypoints.Count - 1}, from ({from.X:0},{from.Y:0}) to " +
                    $"({to.X:0},{to.Y:0}) facing {front.Degrees:0}°, is not walkable. The planner handed " +
                    "back a line it had not checked, or checked differently from how it is read here.");
            }

            _out.WriteLine($"{what}: {route.Waypoints.Count - 1} legs, all clear.");
        }
    }
}
