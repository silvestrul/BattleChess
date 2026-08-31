using System;
using BattleChess.Contracts;
using BattleChess.Rules;
using BattleChess.Rules.GridPlanning;
using Xunit;
using Xunit.Abstractions;

namespace BattleChess.Tests.Battle
{
    /// <summary>
    /// That an order nobody wants any more actually stops being worked out.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>M80 documented a behaviour the code did not have.</b>
    /// <see cref="Marching.GiveUpNow"/> says of itself that it costs "one null
    /// check every sixty-four expansions of the lattice", and the host sets it
    /// on every plan it starts. Nothing in the default cascade ever read it:
    /// the only consumer in the whole assembly was the hybrid A* prototype,
    /// which is off by default and unreachable from a played battle.
    /// </para>
    /// <para>
    /// So a player who clicked somewhere else did not abandon the search they
    /// had superseded. It ran to completion, and the click that was meant to
    /// cancel it blocked on it instead - measured at up to 372 ms of dead
    /// input on the very gesture asking for something else.
    /// </para>
    /// </remarks>
    [Collection("PlannerLevers")]
    public sealed class SupersedingAnOrderTests
    {
        private readonly ITestOutputHelper _out;

        public SupersedingAnOrderTests(ITestOutputHelper outp) => _out = outp;

        /// <summary>
        /// The staging scan asks it too, and it is the one that had to.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>[M126], and it is the gate on a bug that was real.</b> A recording
        /// of 30 August has a frame that planned nothing and cost 293 ms, of
        /// which 280 was the host waiting on a plan the player had already
        /// replaced. [M80]'s machinery for abandoning such a plan existed and
        /// was called after the wait rather than before it - but even in the
        /// right order it buys nothing unless the stage the time is actually in
        /// looks at the hook, and <see cref="StagedRoutePlanner"/>'s staging
        /// scan did not. [M123] measured that stage at 76 to 103 ms of a single
        /// order, the dearest thing an order does and the only one with no gate
        /// in front of it.
        /// </para>
        /// <para>
        /// Poses rather than milliseconds, for the reason the test below states:
        /// a timing gate on a planner this uneven passes or fails on which
        /// machine ran it. <c>thecrowdedwing</c> and not the crucible, because
        /// the scan only runs for a regiment standing inside one of its own and
        /// nothing on a start line is (see [M123]) - on the crucible this would
        /// compare nought against nought and assert nothing at all.
        /// </para>
        /// </remarks>
        [Fact]
        public void TheStagingScanAsksWhetherItIsStillWanted()
        {
            BattleState battle = BenchScenariosTests.Load("thecrowdedwing");

            IPathfinder pathfinder = new DirectPathfinder(
                battle.Terrain, new TerrainMovementModel(TestContent.Terrain), TestContent.Terrain);

            long worstWanted = 0L, worstUnwanted = 0L;

            foreach (UnitInstance unit in battle.UnitsOnField())
            {
                Vec2 to = BenchScenariosTests.OrderFor(battle, unit);

                Marching.GiveUpNow = null;
                StagedRoutePlanner.StagingSamples = 0L;
                Marching.PlanTo(battle, unit, pathfinder, to);
                worstWanted = Math.Max(worstWanted, StagedRoutePlanner.StagingSamples);

                Marching.GiveUpNow = () => true;
                StagedRoutePlanner.StagingSamples = 0L;

                try
                {
                    Marching.PlanTo(battle, unit, pathfinder, to);
                    worstUnwanted = Math.Max(worstUnwanted, StagedRoutePlanner.StagingSamples);
                }
                finally
                {
                    Marching.GiveUpNow = null;
                }
            }

            _out.WriteLine(
                $"worst order's staging poses: {worstWanted:N0} wanted, {worstUnwanted:N0} " +
                "given up on");

            // The arrangement has to make the scan run at all, or everything
            // below is nought against nought and asserts nothing.
            Assert.True(worstWanted > 100L,
                $"The dearest order on this field walked {worstWanted:N0} staging poses, which " +
                "is too few for this to be measuring anything. The field is meant to be one " +
                "where large bodies lap.");

            // <b>The worst order, not the total.</b> A total falls when an
            // abandoned order gives up in some *other* stage and the second pass
            // of the cascade never runs - which it does, and which is why the
            // sum passed this before the poll existed. What the poll is for is
            // that no single order walks a long scan nobody wants, so that is
            // what is asserted.
            Assert.True(worstUnwanted <= AFewPoses,
                $"The dearest abandoned order still walked {worstUnwanted:N0} staging poses " +
                $"against a wanted worst of {worstWanted:N0}. The scan is not asking whether " +
                "anybody still wants the answer, which is the 280 ms the host spent waiting " +
                "for exactly that in the recording of 30 August.");
        }

        /// <summary>
        /// How many poses an abandoned order may still walk before the scan
        /// notices.
        /// </summary>
        /// <remarks>
        /// The hook is asked at the head of every step, so an order abandoned
        /// before it began should walk none at all - and measured, it walks
        /// none. The allowance is slack against a future scan that looks a
        /// little less often, not a figure anything was fitted to: without the
        /// poll the same order walks 482.
        /// </remarks>
        private const long AFewPoses = 32L;

        /// <summary>
        /// The planner asks whether the answer is still wanted.
        /// </summary>
        /// <remarks>
        /// Counting invocations rather than timing the plan, because a timing
        /// gate on a planner this uneven would pass or fail on which machine
        /// ran it. What is being asserted is a contract - that the hook is
        /// consulted at all - and the count says exactly that and nothing else.
        /// </remarks>
        [Fact]
        public void APlanAsksWhetherItIsStillWanted()
        {
            BattleState battle = BenchScenariosTests.Load("crucible");

            IPathfinder pathfinder = new DirectPathfinder(
                battle.Terrain, new TerrainMovementModel(TestContent.Terrain), TestContent.Terrain);

            int asked = 0;

            Marching.GiveUpNow = () => { asked++; return false; };

            try
            {
                foreach (UnitInstance unit in battle.UnitsOnField())
                    Marching.PlanTo(battle, unit, pathfinder, BenchScenariosTests.OrderFor(battle, unit));
            }
            finally
            {
                Marching.GiveUpNow = null;
            }

            _out.WriteLine($"the hook was consulted {asked:N0} time(s) over 80 orders");

            Assert.True(asked > 0,
                "Nothing in the default cascade ever asked whether the order was still wanted, " +
                "so a superseded search runs to completion and the click that superseded it waits.");
        }

        /// <summary>
        /// An order given up on comes back at once rather than finishing first.
        /// </summary>
        /// <remarks>
        /// The counterpart to the gate above: consulting the hook is worth
        /// nothing if the answer is ignored. Asserted against the work done
        /// rather than against a clock, for the same reason - the ladder still
        /// runs, so this is not a claim that nothing happens, only that the
        /// dear stages below it are not entered.
        /// <para>
        /// <b>With the grid off, and that is not a dodge.</b> Written first
        /// with the cascade as it ships, this counted 0 legs priced either way
        /// and passed nothing: the regiment grid answers every Crucible order,
        /// so the lattice is never reached and <c>Effort.Legs</c> is zero
        /// whatever the hook says. A gate that cannot fail is not a gate. The
        /// grid is switched off here so that the lattice actually runs, which
        /// is the stage this is making a claim about; the grid's own polling is
        /// what the test above covers, since with the grid on it is the only
        /// site an order reaches.
        /// </para>
        /// </remarks>
        [Fact]
        public void GivingUpSkipsTheDearStages()
        {
            GridUse was = GridRoutePlanner.Use;
            GridRoutePlanner.Use = GridUse.Off;

            try
            {
                TheLatticeStopsWhenToldTo();
            }
            finally
            {
                GridRoutePlanner.Use = was;
            }
        }

        private void TheLatticeStopsWhenToldTo()
        {
            BattleState battle = BenchScenariosTests.Load("crucible");

            IPathfinder pathfinder = new DirectPathfinder(
                battle.Terrain, new TerrainMovementModel(TestContent.Terrain), TestContent.Terrain);

            long wanted = 0, unwanted = 0;

            foreach (UnitInstance unit in battle.UnitsOnField())
            {
                Vec2 to = BenchScenariosTests.OrderFor(battle, unit);

                Marching.GiveUpNow = null;
                wanted += Marching.PlanTo(battle, unit, pathfinder, to).Effort.Legs;

                Marching.GiveUpNow = () => true;

                try
                {
                    unwanted += Marching.PlanTo(battle, unit, pathfinder, to).Effort.Legs;
                }
                finally
                {
                    Marching.GiveUpNow = null;
                }
            }

            _out.WriteLine($"legs priced: {wanted:N0} wanted, {unwanted:N0} given up on");

            Assert.True(unwanted < wanted,
                $"Giving up priced {unwanted:N0} legs against {wanted:N0} for the same orders " +
                "wanted, so the search carried on after the answer had been abandoned.");
        }
    }
}
