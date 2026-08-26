using System.Collections.Generic;
using BattleChess.Contracts;
using BattleChess.Rules;
using Xunit;
using Xunit.Abstractions;

namespace BattleChess.Tests.Battle
{
    /// <summary>
    /// The frame allowance holds whichever way of planning is behind it.
    /// </summary>
    /// <remarks>
    /// The allowance is asked before <see cref="Marching.PlanTo"/> chooses a
    /// planner, so it cannot see one and ought not to care. That is an argument
    /// rather than a measurement, and this is the measurement.
    /// </remarks>
    public sealed class PlanningBudgetPerPlannerTests
    {
        private readonly ITestOutputHelper _out;
        public PlanningBudgetPerPlannerTests(ITestOutputHelper output) => _out = output;

        [Fact]
        public void EveryPlannerIsHeldToTheSameAllowance()
        {
            foreach (IRoutePlanner planner in RoutePlanners.All)
            {
                var field = new Battlefield("plains", 4242ul);
                Vec2 centre = field.Centre;

                var movers = new List<UnitInstance>();
                for (int i = 0; i < 6; i++)
                {
                    movers.Add(field.Add(
                        0, "spearmen", centre + new Vec2(-300f, (i - 3) * 60f), Facing.FromDegrees(0f)));
                }

                field.State.Planning.OpenFrame(routesPerFrame: 2, millisecondsPerFrame: 10_000f);

                int granted = 0;
                foreach (UnitInstance unit in movers)
                {
                    if (!field.State.Planning.MayPlan(unit.Id)) continue;

                    granted++;
                    Marching.PlanTo(
                        field.State, unit, field.Pathfinder, centre + new Vec2(300f, 0f),
                        planner: planner);
                }

                // Asked again in the same frame, nobody gets a second turn.
                int seconds = 0;
                foreach (UnitInstance unit in movers)
                    if (field.State.Planning.MayPlan(unit.Id)) seconds++;

                _out.WriteLine(
                    $"{planner.Name,-38} granted {granted}, " +
                    $"{field.State.Planning.RoutesThisFrame} recorded, " +
                    $"{field.State.Planning.MillisecondsThisFrame,7:0.0} ms, " +
                    $"{field.State.Planning.Waiting} waiting, {seconds} second turns");

                Assert.Equal(2, granted);
                Assert.Equal(2, field.State.Planning.RoutesThisFrame);
                Assert.Equal(4, field.State.Planning.Waiting);
                Assert.Equal(0, seconds);

                // The clock has to have run, or the millisecond half of the
                // allowance is being carried by nothing.
                Assert.True(field.State.Planning.MillisecondsThisFrame > 0f,
                    $"{planner.Name} recorded no time at all against the frame.");
            }
        }
    }
}
