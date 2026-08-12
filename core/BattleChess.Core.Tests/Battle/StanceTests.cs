using BattleChess.Contracts;
using BattleChess.Rules;
using Xunit;

namespace BattleChess.Tests.Battle
{
    /// <summary>
    /// What a stance actually promises when a march meets an enemy.
    /// </summary>
    /// <remarks>
    /// The gap between "go there" and what the simulation did was the worst
    /// bug the harness has produced. A zone of control reaches thirty to forty
    /// metres and a melee needs thirteen, so a regiment halted by one stopped
    /// where it could neither fight nor withdraw — and simply stood there. In
    /// a recorded game that meant spearmen and cavalry taking fifty-seven
    /// volleys of artillery without ever drawing a sword.
    /// </remarks>
    public sealed class StanceTests
    {
        [Fact]
        public void AdvanceIsTheDefault()
        {
            var field = new Battlefield("plains", 3300);
            UnitInstance unit = field.Add(0, "spearmen", field.Centre, Facing.East);

            Assert.Equal(Stance.Advance, unit.Stance);
        }

        [Fact]
        public void AMarchBlockedByAnEnemyTurnsIntoAFight()
        {
            (UnitInstance mine, UnitInstance enemy) = MarchPast(Stance.Advance);

            Assert.True(Battlefield.LostPercent(enemy) > 0f,
                $"Told to advance, a regiment stopped by an enemy must go through it. It ended " +
                $"{Vec2.Distance(mine.Position, enemy.Position):0} m away having done nothing.");
        }

        [Fact]
        public void DefendStillMeansStopWhenYouMeetSomebody()
        {
            (UnitInstance mine, UnitInstance enemy) = MarchPast(Stance.Defend);

            Assert.Equal(0, enemy.Casualties);

            // Between the formations, not between their centres. A zone of
            // control is a belt around the shape, and two eighty-metre lines
            // halted a few metres apart still have their centres most of a
            // frontage away from each other.
            Assert.True(OrientedRect.GapBetween(mine.Shape, enemy.Shape) <= enemy.ZoneOfControl * 1.5f,
                "A regiment on Defend should halt on contact rather than press on — that is the whole " +
                "difference between telling it to hold and telling it to advance.");
        }

        [Fact]
        public void AnAdvancingRegimentDoesNotLoiterInsideTheEnemysReach()
        {
            (UnitInstance mine, UnitInstance enemy) = MarchPast(Stance.Advance);

            bool closed = Vec2.Distance(mine.Position, enemy.Position) < enemy.ZoneOfControl;
            bool movedOn = !enemy.IsOnField || Battlefield.LostPercent(enemy) > 0f;

            Assert.True(closed || movedOn,
                "Standing at the edge of an enemy's zone of control is the one place a regiment must " +
                "never settle: too far to fight, too close to be safe, and under guns it is simply a " +
                "slower way of being killed.");
        }

        /// <summary>
        /// Orders a regiment to march to a point beyond a standing enemy, and
        /// reports where everyone ended up.
        /// </summary>
        private static (UnitInstance Mine, UnitInstance Enemy) MarchPast(Stance stance)
        {
            var field = new Battlefield("plains", 3300);

            UnitInstance enemy = field.Add(1, "swordsmen", field.Centre, Facing.West);
            Battlefield.Hold(enemy);

            UnitInstance mine = field.Add(0, "spearmen", field.Centre - new Vec2(250f, 0f), Facing.East);

            field.March(mine, field.Centre + new Vec2(150f, 0f), stance);
            field.RunTurns(10);

            return (mine, enemy);
        }
    }
}
