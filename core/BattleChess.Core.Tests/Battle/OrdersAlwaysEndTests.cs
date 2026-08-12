using System;
using BattleChess.Contracts;
using BattleChess.Rules;
using Xunit;

namespace BattleChess.Tests.Battle
{
    /// <summary>
    /// An order either gets carried out or gives up saying so. It never runs
    /// forever.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every stall found so far had its own cause — a destination inside a
    /// friendly regiment, a detour that reversed itself every tick, a goal on
    /// ground nothing can stand on — and each was fixed on its own terms. This
    /// is the rule that does not care why: a regiment which has not got closer
    /// to where it is going in fifteen seconds is not going to, and something
    /// has to change.
    /// </para>
    /// <para>
    /// The reproduction these were written from: swordsmen ordered onto ground
    /// their own spearmen were standing on pressed to within a metre and then
    /// thrashed north and south, twelve reversals of direction in twenty-five
    /// ticks, for the rest of the battle.
    /// </para>
    /// </remarks>
    public sealed class OrdersAlwaysEndTests
    {
        [Fact]
        public void ARegimentSentOntoItsOwnTroopsSettlesInsteadOfThrashing()
        {
            var field = new Battlefield("plains", 30200);

            UnitInstance sitting = field.Add(0, "spearmen", field.Centre, Facing.East);
            Battlefield.Hold(sitting);

            UnitInstance mover = field.Add(0, "swordsmen", field.Centre - new Vec2(150f, 0f), Facing.East);
            field.March(mover, field.Centre);

            field.RunTurns(2);

            int reversals = CountReversals(field, mover, ticks: 120);

            Assert.True(reversals <= 2,
                $"It changed direction {reversals} times while trying to reach ground it can never stand on. " +
                "That is the seizure this rule exists to end.");

            Assert.False(mover.IsMarching,
                "And it should have stopped rather than still be trying.");
        }

        [Fact]
        public void ItStopsSomewhereSensibleRatherThanOnTopOfThem()
        {
            var field = new Battlefield("plains", 30210);

            UnitInstance sitting = field.Add(0, "spearmen", field.Centre, Facing.East);
            Battlefield.Hold(sitting);

            UnitInstance mover = field.Add(0, "swordsmen", field.Centre - new Vec2(150f, 0f), Facing.East);
            field.March(mover, field.Centre);

            field.RunTurns(6);

            float overlap = OrientedRect.OverlapFraction(mover.Shape, sitting.Shape);

            Assert.True(overlap <= OrderSystem.GrazingTolerance,
                $"It settled {overlap:P0} inside its own spearmen.");

            Assert.True(Vec2.Distance(mover.Position, field.Centre) < 120f,
                $"But it should still have got near where it was sent, not given up at the start line: " +
                $"it is {Vec2.Distance(mover.Position, field.Centre):0} m away.");
        }

        [Fact]
        public void ItSaysWhyItStopped()
        {
            var field = new Battlefield("plains", 30220);

            UnitInstance sitting = field.Add(0, "spearmen", field.Centre, Facing.East);
            Battlefield.Hold(sitting);

            UnitInstance mover = field.Add(0, "swordsmen", field.Centre - new Vec2(150f, 0f), Facing.East);
            field.March(mover, field.Centre);

            field.RunTurns(8);

            Assert.True(
                field.TimesSaid("Something is standing on that ground") > 0 ||
                field.TimesSaid("trying for ground") > 0,
                "A regiment that has quietly stopped is indistinguishable from one that has stopped " +
                "taking orders. It has to say which.");
        }

        // ---- The placement search ----------------------------------------------

        [Fact]
        public void AnOrderIntoAWoodFindsTheNearestGroundOutsideIt()
        {
            var field = new Battlefield("plains", 30230, RuleSet.Full, canvas =>
            {
                int centre = canvas.ColumnAt(canvas.Columns * canvas.CellSize * 0.5f);
                canvas.Band(centre - 1, centre + 3, "deepwater");
            });

            UnitInstance foot = field.Add(0, "swordsmen", field.Centre - new Vec2(250f, 0f), Facing.East);

            bool found = OrderSystem.TryFindPlacement(
                field.State, foot, field.Centre, Facing.East, out Vec2 placement);

            Assert.True(found, "There is dry land either side of the water; it should have found some.");

            Assert.True(field.State.FormationFits(foot, placement, Facing.East),
                "And what it found has to be ground the regiment can actually occupy.");
        }

        [Fact]
        public void ThePlacementSearchPrefersGroundNoEnemyCommands()
        {
            var field = new Battlefield("plains", 30240);

            // A spear wall whose zone of control covers one side of the point
            // being aimed at.
            UnitInstance spears = field.Add(1, "spearmen", field.Centre + new Vec2(0f, 30f), Facing.South);
            Battlefield.Hold(spears);

            UnitInstance foot = field.Add(0, "swordsmen", field.Centre - new Vec2(250f, 0f), Facing.East);

            OrderSystem.TryFindPlacement(
                field.State, foot, field.Centre, Facing.East, out Vec2 placement);

            var stood = new OrientedRect(placement, Facing.East, foot.Footprint);

            Assert.False(OrientedRect.Within(stood, spears.Shape, spears.ZoneOfControl),
                "A march is not an attack. Told to reposition, it should not park inside a spear wall's " +
                "reach when there is equally near ground that is clear.");
        }

        /// <summary>
        /// Runs the clock and counts how many times the unit reversed direction —
        /// the signature of a regiment fighting itself rather than moving.
        /// </summary>
        private static int CountReversals(Battlefield field, UnitInstance unit, int ticks)
        {
            Vec2 was = unit.Position;
            Vec2 lastStep = Vec2.Zero;
            int reversals = 0;

            for (int t = 0; t < ticks; t++)
            {
                field.Clock.Advance(field.State, field.Transcript);

                Vec2 step = unit.Position - was;
                was = unit.Position;

                if (step.IsNearZero) continue;

                if (!lastStep.IsNearZero &&
                    Vec2.Dot(step.Normalised(), lastStep.Normalised()) < -0.3f)
                    reversals++;

                lastStep = step;
            }

            return reversals;
        }
    }
}
