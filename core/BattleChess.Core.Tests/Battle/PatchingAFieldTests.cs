using System;
using System.Collections.Generic;
using BattleChess.Contracts;
using BattleChess.Rules;
using BattleChess.Rules.GridPlanning;
using Xunit;
using Xunit.Abstractions;

namespace BattleChess.Tests.Battle
{
    /// <summary>
    /// A field brought up to date by restamping what moved must be the same
    /// field as one raised from nothing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why this gate and not a timing.</b> Patching is worth roughly half of
    /// everything planning does, and the way it goes wrong is silent: a body
    /// unmarked at the wrong place leaves coverage on ground nobody is standing
    /// on, or takes it off ground somebody is, and the grid then hands back a
    /// route through a regiment. That is the class of bug this project has
    /// spent four attempts on, and it would not show up as a failure anywhere
    /// near here - it would show up as a play-test saying cavalry went through
    /// a unit.
    /// </para>
    /// <para>
    /// So the check is cell by cell against the thing it claims to be equal to,
    /// over the whole field, at both tiers, after the kind of move a tick
    /// actually makes.
    /// </para>
    /// </remarks>
    [Collection("PlannerLevers")]
    public sealed class PatchingAFieldTests
    {
        private readonly ITestOutputHelper _out;

        public PatchingAFieldTests(ITestOutputHelper outp) => _out = outp;

        /// <summary>Every cell of a patched field reads as a rebuilt one does.</summary>
        [Theory]
        [InlineData("crucible", 1)]
        [InlineData("crucible", 12)]
        [InlineData("brokencountry", 12)]
        [InlineData("longmarch", 40)]
        [InlineData("greatfield", 40)]
        public void APatchedFieldReadsTheSameAsARebuiltOne(string field, int movers)
        {
            bool was = RegimentGrid.MarkIncrementally;

            try
            {
                RegimentGrid.MarkIncrementally = true;

                BattleState battle = BenchScenariosTests.Load(field);
                UnitInstance mover = First(battle);

                // Raised once, on the arrangement as it loads.
                RegimentGrid.Forget();
                RegimentGrid.For(battle, mover);

                Shove(battle, movers);

                // Asked again, which patches what is kept. The counter runs for
                // the life of the thread, so what this test wants is the step
                // across the one call - xUnit puts several cases on the same
                // thread and the raw figure is every case so far added up.
                int before = RegimentGrid.BodiesRestamped;
                RegimentGrid patched = RegimentGrid.For(battle, mover);
                int restamped = RegimentGrid.BodiesRestamped - before;

                // And the same arrangement from nothing.
                RegimentGrid.Forget();
                RegimentGrid fresh = RegimentGrid.For(battle, mover);

                var cells = new List<Coord>();
                fresh.Snapshot(cells);

                int differed = 0;
                Coord first = default;

                foreach (Coord cell in cells)
                {
                    if (MathF.Abs(patched.FillAt(cell) - fresh.FillAt(cell)) < 1e-6f &&
                        patched.IsBlocked(cell) == fresh.IsBlocked(cell) &&
                        Vec2.Distance(patched.NodeAt(cell), fresh.NodeAt(cell)) < 1e-3f)
                        continue;

                    if (differed == 0) first = cell;
                    differed++;
                }

                _out.WriteLine(
                    $"{field}: {movers} moved, {restamped} bodies restamped, " +
                    $"{cells.Count} cells compared, {patched.BlockedCells} held " +
                    $"against {fresh.BlockedCells}");

                // The check that says this test can fail: unless the shove
                // actually changed the field, comparing two identical fields
                // proves nothing at all.
                Assert.True(
                    restamped > 0,
                    $"Nothing was restamped on {field}, so the patch was never exercised.");

                Assert.True(
                    differed == 0,
                    $"{differed} of {cells.Count} cells read differently on {field} " +
                    $"after {movers} moved - first at {first}.");

                Assert.Equal(fresh.BlockedCells, patched.BlockedCells);
            }
            finally
            {
                RegimentGrid.MarkIncrementally = was;
                RegimentGrid.Forget();
            }
        }

        /// <summary>
        /// A body that leaves the field is taken off it, and one that arrives is
        /// put on.
        /// </summary>
        /// <remarks>
        /// The walk over the army says which bodies have moved; it cannot say
        /// which have stopped existing, because they are no longer in it to be
        /// asked. That is a separate pass over what the field believes, and this
        /// is what would catch its absence: coverage left behind by a regiment
        /// that has been destroyed is ground the grid will not route over for
        /// the rest of the battle.
        /// </remarks>
        [Fact]
        public void ARegimentThatLeavesTheFieldIsTakenOffIt()
        {
            bool was = RegimentGrid.MarkIncrementally;

            try
            {
                RegimentGrid.MarkIncrementally = true;

                BattleState battle = BenchScenariosTests.Load("crucible");
                UnitInstance mover = First(battle);

                RegimentGrid.Forget();
                RegimentGrid.For(battle, mover);

                int destroyed = 0;

                foreach (UnitInstance unit in battle.UnitsOnField())
                {
                    if (unit.Id == mover.Id) continue;
                    if (++destroyed > 8) break;

                    unit.State = UnitState.Destroyed;
                }

                // Something has to have gone, or the pass being tested for is
                // never reached.
                Assert.True(destroyed > 1);

                RegimentGrid patched = RegimentGrid.For(battle, mover);

                RegimentGrid.Forget();
                RegimentGrid fresh = RegimentGrid.For(battle, mover);

                var cells = new List<Coord>();
                fresh.Snapshot(cells);

                int differed = 0;

                foreach (Coord cell in cells)
                    if (patched.IsBlocked(cell) != fresh.IsBlocked(cell))
                        differed++;

                _out.WriteLine(
                    $"{destroyed} destroyed, {patched.BlockedCells} held against " +
                    $"{fresh.BlockedCells}, {differed} of {cells.Count} cells differed");

                Assert.Equal(0, differed);
                Assert.Equal(fresh.BlockedCells, patched.BlockedCells);
            }
            finally
            {
                RegimentGrid.MarkIncrementally = was;
                RegimentGrid.Forget();
            }
        }

        /// <summary>The first regiment on the field, whoever it is.</summary>
        private static UnitInstance First(BattleState battle)
        {
            foreach (UnitInstance unit in battle.UnitsOnField()) return unit;

            throw new InvalidOperationException("An empty field.");
        }

        /// <summary>
        /// Moves the first <paramref name="movers"/> regiments the way a tick
        /// would: a few metres on, and a degree or two round.
        /// </summary>
        private static void Shove(BattleState battle, int movers)
        {
            int moved = 0;

            foreach (UnitInstance unit in battle.UnitsOnField())
            {
                if (moved >= movers) break;

                unit.Position = new Vec2(unit.Position.X + 3.7f, unit.Position.Y - 1.9f);
                unit.Facing = Facing.FromDegrees(unit.Facing.Degrees + 2.5f);
                moved++;
            }
        }
    }
}
