using System;
using System.Collections.Generic;
using BattleChess.Contracts;
using BattleChess.Rules;
using Xunit;

namespace BattleChess.Tests.Battle
{
    /// <summary>
    /// Getting there: every bearing, and every way an order can be interfered
    /// with on the way.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The other tests around movement each pin one rule. This one sweeps the
    /// combinations, because nearly every movement fault in the recorded games
    /// came from two rules meeting rather than from either being wrong: a detour
    /// taken while wheeling, a second order landing mid-turn, a regiment giving
    /// way to one that was giving way to it.
    /// </para>
    /// <para>
    /// The recurring symptom is worth naming, since several tests here watch for
    /// it specifically. A regiment that cannot resolve its situation does not
    /// stop — it oscillates, stepping one way and then back, forever. On the
    /// field that reads as a fit rather than as a refusal, and it is the reason
    /// an order must always end, one way or the other.
    /// </para>
    /// </remarks>
    public sealed class MarchingScenarioTests
    {
        /// <summary>Close enough to call an order carried out.</summary>
        private const float ArrivedMetres = 25f;

        // ---- The plain case, in every direction --------------------------------

        [Theory]
        [InlineData(0f)]
        [InlineData(45f)]
        [InlineData(90f)]
        [InlineData(135f)]
        [InlineData(180f)]
        [InlineData(225f)]
        [InlineData(270f)]
        [InlineData(315f)]
        public void AMarchOnAnyBearingArrives(float degrees)
        {
            var field = new Battlefield("plains", 26000 + (ulong)degrees);

            UnitInstance foot = field.Add(0, "swordsmen", field.Centre, Facing.East);

            Vec2 destination = field.Centre + Facing.FromDegrees(degrees).ToVector() * 220f;

            field.March(foot, destination);
            field.RunTurns(8);

            Assert.True(Vec2.Distance(foot.Position, destination) < ArrivedMetres,
                $"Sent 220 m on a bearing of {degrees:0}°, it stopped " +
                $"{Vec2.Distance(foot.Position, destination):0} m short.");
        }

        [Fact]
        public void ARegimentThatHasArrivedStaysArrived()
        {
            var field = new Battlefield("plains", 26100);

            UnitInstance foot = field.Add(0, "swordsmen", field.Centre, Facing.East);
            Vec2 destination = field.Centre + new Vec2(150f, 0f);

            field.March(foot, destination);
            field.RunTurns(6);

            Vec2 settled = foot.Position;
            field.RunTurns(6);

            Assert.True(Vec2.Distance(foot.Position, settled) < 3f,
                $"Once an order is finished the regiment should stand. It drifted a further " +
                $"{Vec2.Distance(foot.Position, settled):0} m after arriving.");
        }

        [Fact]
        public void AMarchNeverEndsFurtherOffThanItStarted()
        {
            var field = new Battlefield("plains", 26200);

            UnitInstance horse = field.Add(0, "cavalry", field.Centre - new Vec2(250f, 0f), Facing.East);
            Vec2 destination = field.Centre + new Vec2(250f, 0f);

            float atStart = Vec2.Distance(horse.Position, destination);

            field.March(horse, destination);

            float worst = 0f;

            for (int turn = 0; turn < 10; turn++)
            {
                field.RunTurns(1);
                worst = MathF.Max(worst, Vec2.Distance(horse.Position, destination));
            }

            Assert.True(worst <= atStart + 5f,
                $"It got {worst:0} m from where it was sent, having started {atStart:0} m away. Going round " +
                "something is fair; ending up further away than it began is not.");
        }

        // ---- A second order arriving while the first is being carried out -------

        [Fact]
        public void ANewOrderMidMarchIsObeyedRatherThanQueued()
        {
            var field = new Battlefield("plains", 26300);

            UnitInstance horse = field.Add(0, "cavalry", field.Centre, Facing.East);

            field.March(horse, field.Centre + new Vec2(300f, 0f));
            field.RunTurns(2);

            Vec2 recalled = field.Centre + new Vec2(0f, 200f);
            field.March(horse, recalled);
            field.RunTurns(6);

            Assert.True(Vec2.Distance(horse.Position, recalled) < ArrivedMetres,
                $"Recalled mid-march, it finished {Vec2.Distance(horse.Position, recalled):0} m from where the " +
                "second order sent it.");
        }

        [Fact]
        public void TellingARegimentToStandStopsItWhereItIs()
        {
            var field = new Battlefield("plains", 26400);

            UnitInstance foot = field.Add(0, "swordsmen", field.Centre, Facing.East);

            field.March(foot, field.Centre + new Vec2(400f, 0f));
            field.RunTurns(2);

            Vec2 halted = foot.Position;
            Battlefield.Hold(foot);
            field.RunTurns(4);

            Assert.True(Vec2.Distance(foot.Position, halted) < 5f,
                $"Told to stand, it went a further {Vec2.Distance(foot.Position, halted):0} m.");

            Assert.True(Vec2.Distance(foot.Position, field.Centre + new Vec2(400f, 0f)) > 100f,
                "And the point of the test is that it stopped well short of where it had been sent.");
        }

        [Fact]
        public void AnOrderReversedMidMarchTurnsTheRegimentRound()
        {
            var field = new Battlefield("plains", 26500);

            UnitInstance foot = field.Add(0, "swordsmen", field.Centre, Facing.East);

            field.March(foot, field.Centre + new Vec2(300f, 0f));
            field.RunTurns(2);

            // Called back the way it came — the order a player gives when an
            // advance turns out to be a mistake, and one that has to work
            // promptly to be worth anything.
            Vec2 back = field.Centre - new Vec2(200f, 0f);
            field.March(foot, back);
            field.RunTurns(8);

            Assert.True(Vec2.Distance(foot.Position, back) < ArrivedMetres,
                $"Reversed mid-march it finished {Vec2.Distance(foot.Position, back):0} m from where it was " +
                "recalled to.");
        }

        // ---- Somebody in the way -----------------------------------------------

        [Fact]
        public void GoingRoundAFriendDoesNotTurnIntoRockingBackAndForth()
        {
            var field = new Battlefield("plains", 26600);

            UnitInstance wall = field.Add(0, "spearmen", field.Centre, Facing.East);
            Battlefield.Hold(wall);

            UnitInstance horse = field.Add(0, "cavalry", field.Centre - new Vec2(200f, 0f), Facing.East);
            field.March(horse, field.Centre + new Vec2(200f, 0f));

            // Recorded as cavalry crabbing sideways and then swinging between two
            // positions eight metres apart for seven turns together. The detour
            // direction was being taken from the shortest way out of the overlap,
            // which flips from one face of the obstacle to the other as the two
            // shapes slide past each other.
            var track = new List<Vec2>();

            for (int turn = 0; turn < 12; turn++)
            {
                field.RunTurns(1);
                track.Add(horse.Position);
            }

            // Over the last four turns it should be somewhere, not two places.
            float wander = 0f;
            for (int i = track.Count - 4; i < track.Count; i++)
                wander = MathF.Max(wander, Vec2.Distance(track[i], track[track.Count - 1]));

            Assert.True(wander < 40f,
                $"It is still moving {wander:0} m about between turns at the end of its order, which is what " +
                "swinging between two positions looks like from outside.");
        }

        [Fact]
        public void ARegimentHeldUpByAFriendGoesOnceTheFriendMovesOff()
        {
            var field = new Battlefield("plains", 26700);

            UnitInstance blocker = field.Add(0, "spearmen", field.Centre, Facing.East);
            Battlefield.Hold(blocker);

            UnitInstance behind = field.Add(0, "swordsmen", field.Centre - new Vec2(30f, 0f), Facing.East);
            Vec2 destination = field.Centre + new Vec2(200f, 0f);
            field.March(behind, destination);

            field.RunTurns(2);

            // The obstacle marches away. Whatever the follower had decided about
            // going round it must not outlive the reason for it.
            field.March(blocker, field.Centre + new Vec2(0f, 300f));
            field.RunTurns(8);

            Assert.True(Vec2.Distance(behind.Position, destination) < 60f,
                $"Once the way is clear it should walk through. It is still " +
                $"{Vec2.Distance(behind.Position, destination):0} m off.");
        }

        [Fact]
        public void TwoRegimentsSwappingPlacesDoNotDeadlock()
        {
            var field = new Battlefield("plains", 26800);

            // Each ordered onto the other's ground, head on. Neither can simply
            // wait, because the thing it is waiting for is waiting for it.
            UnitInstance west = field.Add(0, "swordsmen", field.Centre - new Vec2(140f, 0f), Facing.East);
            UnitInstance east = field.Add(0, "swordsmen", field.Centre + new Vec2(140f, 0f), Facing.West);

            Vec2 westGoal = east.Position;
            Vec2 eastGoal = west.Position;

            field.March(west, westGoal);
            field.March(east, eastGoal);

            field.RunTurns(12);

            Assert.True(west.Position.X > field.Centre.X,
                $"The westerly regiment never got past the middle: x={west.Position.X:0}.");

            Assert.True(east.Position.X < field.Centre.X,
                $"The easterly regiment never got past the middle: x={east.Position.X:0}.");

            Assert.False(OrientedRect.Overlaps(west.Shape, east.Shape),
                "And they should have passed each other rather than ending up in the same field.");
        }

        [Fact]
        public void ARegimentThreadsBetweenTwoOfItsOwn()
        {
            var field = new Battlefield("plains", 26900);

            // A gap in the line wide enough to march through, with a regiment
            // drawn up on each side of it.
            UnitInstance left = field.Add(0, "spearmen", field.Centre + new Vec2(0f, 70f), Facing.East);
            UnitInstance right = field.Add(0, "spearmen", field.Centre - new Vec2(0f, 70f), Facing.East);
            Battlefield.Hold(left);
            Battlefield.Hold(right);

            UnitInstance horse = field.Add(0, "cavalry", field.Centre - new Vec2(200f, 0f), Facing.East);
            Vec2 destination = field.Centre + new Vec2(200f, 0f);

            field.March(horse, destination);
            field.RunTurns(10);

            Assert.True(Vec2.Distance(horse.Position, destination) < 60f,
                $"There was a gap and it should have gone through it. It stopped " +
                $"{Vec2.Distance(horse.Position, destination):0} m short.");
        }

        // ---- Ground -------------------------------------------------------------

        [Fact]
        public void BadGroundSlowsAMarchWithoutStoppingIt()
        {
            float overGrass = MetresCoveredInThreeTurns("plains");
            float overSwamp = MetresCoveredInThreeTurns("swamp");

            Assert.True(overSwamp < overGrass,
                $"A swamp should cost something: {overSwamp:0} m against {overGrass:0} m on grass.");

            Assert.True(overSwamp > 20f,
                $"But it should not stop the regiment altogether — it moved {overSwamp:0} m in three turns.");
        }

        private static float MetresCoveredInThreeTurns(string ground)
        {
            var field = new Battlefield(ground, 27000);

            UnitInstance foot = field.Add(0, "swordsmen", field.Centre - new Vec2(300f, 0f), Facing.East);
            Vec2 start = foot.Position;

            field.March(foot, field.Centre + new Vec2(300f, 0f));
            field.RunTurns(3);

            return Vec2.Distance(foot.Position, start);
        }

        // ---- An enemy in the way ------------------------------------------------

        [Fact]
        public void AMarchIsHaltedAtAnEnemysReachAndTheHoldUpIsRecorded()
        {
            var field = new Battlefield("plains", 27100);

            UnitInstance enemy = field.Add(1, "spearmen", field.Centre, Facing.West);
            Battlefield.Hold(enemy);

            UnitInstance foot = field.Add(0, "swordsmen", field.Centre - new Vec2(220f, 0f), Facing.East);

            // On Defend, so it stops at his reach rather than going through him.
            field.March(foot, field.Centre + new Vec2(220f, 0f), Stance.Defend);
            field.RunTurns(5);

            Assert.True(foot.Position.X < field.Centre.X,
                $"It is at x={foot.Position.X:0} and he is at x={field.Centre.X:0}: it should have been stopped " +
                "short of him.");

            Assert.True(foot.HeldUpBy.IsValid,
                "And it should know what stopped it. A regiment halted with nothing recorded about why is one " +
                "that stands there for the rest of the battle with nothing to fight and no march to finish.");

            Assert.Equal(0, enemy.Casualties);
        }

        [Fact]
        public void MarchingAwayAcrossAFormedEnemyAtArmsLengthGetsTheRegimentCutUp()
        {
            var field = new Battlefield("plains", 27120);

            UnitInstance enemy = field.Add(1, "spearmen", field.Centre, Facing.West);
            Battlefield.Hold(enemy);

            UnitInstance foot = field.Add(0, "swordsmen", field.Centre - new Vec2(220f, 0f), Facing.East);

            field.March(foot, field.Centre + new Vec2(220f, 0f), Stance.Defend);
            field.RunTurns(5);

            float gap = OrientedRect.GapBetween(foot.Shape, enemy.Shape);
            Assert.True(gap < 15f, $"The test needs it halted at close quarters, and it is {gap:0} m off.");

            // Now ordered to march round him. It obeys — and is taken apart on
            // the way, because turning a formation across a formed enemy at
            // arm's length presents him a flank and there is no way to do it
            // quickly. This is the right answer rather than a fault: it is
            // exactly why breaking off should be a decision with a price, and it
            // is what the deferred withdrawal rule is meant to put a number on.
            field.March(foot, field.Centre + new Vec2(0f, 260f), Stance.Defend);
            field.RunTurns(6);

            Assert.True(foot.Casualties > 0 || foot.State == UnitState.Routing || !foot.IsOnField,
                "Walking away from spearmen seven metres in front of you should cost something. It got off " +
                "without a scratch.");
        }

        [Fact(Skip = "Withdrawal is designed but not built — a regiment in melee cannot be ordered away yet. " +
                     "The design charges casualties in proportion to how much of the regiment is gripped, and " +
                     "refuses outright above about eighty-five percent.")]
        public void ARegimentInMeleeCanBeOrderedToBreakOffAndPayForIt()
        {
            var field = new Battlefield("plains", 27150);

            UnitInstance enemy = field.Add(1, "spearmen", field.Centre, Facing.West);
            Battlefield.Hold(enemy);

            UnitInstance foot = field.Add(0, "swordsmen", field.Centre - new Vec2(220f, 0f), Facing.East);

            field.March(foot, field.Centre + new Vec2(220f, 0f));
            field.RunTurns(4);

            Assert.True(foot.EnemiesInContact > 0, "It needs to actually be in the fight first.");

            // Currently the order system puts the attack straight back and the
            // regiment stands there until one side breaks. Disengaging should be
            // possible, expensive, and the player's decision.
            Vec2 away = field.Centre - new Vec2(400f, 0f);
            field.March(foot, away, Stance.Evade);
            field.RunTurns(6);

            Assert.True(foot.Position.X < field.Centre.X - 150f,
                $"Ordered to break off, it is at x={foot.Position.X:0}.");
        }
    }
}
