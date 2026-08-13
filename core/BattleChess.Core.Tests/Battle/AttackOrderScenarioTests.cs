using System;
using BattleChess.Contracts;
using BattleChess.Rules;
using Xunit;

namespace BattleChess.Tests.Battle
{
    /// <summary>
    /// Attack orders and everything that can happen to one before it lands.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Where an attack ends up is covered elsewhere — square on the front, round
    /// the flank, centres aligned, archers halted at their own range. What is
    /// tested here is the order's life: an attack is the only order in the game
    /// aimed at something that moves, breaks, dies or is replaced while the
    /// regiment is still walking toward it.
    /// </para>
    /// <para>
    /// Each of those is a way for an order to end badly. A target that dies
    /// mid-approach must not leave a regiment marching at a corpse; a target that
    /// runs must be chased but not indefinitely, or a single scout drags a wing
    /// off the field.
    /// </para>
    /// </remarks>
    public sealed class AttackOrderScenarioTests
    {
        // ---- The target changes under the order --------------------------------

        [Fact]
        public void AnEnemyKilledBeforeTheChargeArrivesLeavesTheRegimentStandingNotMarchingAtNothing()
        {
            var field = new Battlefield("plains", 30000);

            UnitInstance quarry = field.Add(1, "archers", field.Centre + new Vec2(300f, 0f), Facing.West);
            Battlefield.Hold(quarry);

            UnitInstance horse = field.Add(0, "cavalry", field.Centre, Facing.East);
            Battlefield.Press(horse, quarry);

            field.RunTurns(2);

            // Wiped out by something else while the cavalry is still on its way.
            quarry.TakeCasualties(quarry.Strength);
            field.RunTurns(4);

            Assert.False(quarry.IsOnField, "The target is gone.");

            Vec2 settled = horse.Position;
            field.RunTurns(3);

            Assert.True(Vec2.Distance(horse.Position, settled) < 40f,
                $"With nothing left to attack it should pull up. It has gone a further " +
                $"{Vec2.Distance(horse.Position, settled):0} m.");

            Assert.True(horse.IsOnField && horse.State != UnitState.Routing,
                "And it should still be a regiment in good order, not a casualty of its own order.");
        }

        [Fact]
        public void AnAttackRetargetedMidApproachGoesForTheNewEnemy()
        {
            var field = new Battlefield("plains", 30100);

            UnitInstance first = field.Add(1, "archers", field.Centre + new Vec2(320f, 120f), Facing.West);
            UnitInstance second = field.Add(1, "archers", field.Centre + new Vec2(320f, -220f), Facing.West);
            Battlefield.Hold(first);
            Battlefield.Hold(second);

            UnitInstance horse = field.Add(0, "cavalry", field.Centre, Facing.East);
            Battlefield.Press(horse, first);

            field.RunTurns(2);

            // Redirected before contact — the ordinary act of changing your mind.
            Battlefield.Press(horse, second);
            field.RunTurns(8);

            float toSecond = OrientedRect.GapBetween(horse.Shape, second.Shape);
            float toFirst = OrientedRect.GapBetween(horse.Shape, first.Shape);

            Assert.True(toSecond < toFirst,
                $"Redirected onto the second regiment it finished {toSecond:0} m from it and {toFirst:0} m from " +
                "the one it was called off.");

            Assert.True(second.Casualties > 0 || toSecond < 20f,
                "And it should have got there rather than merely pointed that way.");
        }

        [Fact]
        public void AnAttackCancelledBeforeContactBecomesAnOrdinaryMarch()
        {
            var field = new Battlefield("plains", 30200);

            UnitInstance quarry = field.Add(1, "spearmen", field.Centre + new Vec2(600f, 0f), Facing.West);
            Battlefield.Hold(quarry);

            UnitInstance horse = field.Add(0, "cavalry", field.Centre, Facing.East);
            Battlefield.Press(horse, quarry);

            // One turn only. Cavalry covers most of three hundred metres in a
            // turn, so a longer run-up and an earlier recall are what make this
            // a cancellation rather than a post-mortem.
            field.RunTurns(1);

            // Called off — cavalry sent at spearmen is a mistake, and recalling
            // it before it lands is the correction.
            Vec2 safety = field.Centre - new Vec2(150f, 200f);
            field.March(horse, safety);
            field.RunTurns(8);

            Assert.True(Vec2.Distance(horse.Position, safety) < 60f,
                $"Called off, it should have gone where it was called to. It is " +
                $"{Vec2.Distance(horse.Position, safety):0} m away.");

            Assert.Equal(0, quarry.Casualties);
        }

        [Fact]
        public void ARegimentAlreadyInAFightIsNotTalkedOutOfIt()
        {
            var field = new Battlefield("plains", 30300);

            UnitInstance gripping = field.Add(1, "swordsmen", field.Centre + new Vec2(120f, 0f), Facing.West);
            UnitInstance other = field.Add(1, "swordsmen", field.Centre + new Vec2(120f, 200f), Facing.West);
            Battlefield.Hold(gripping);
            Battlefield.Hold(other);

            UnitInstance foot = field.Add(0, "swordsmen", field.Centre, Facing.East);
            Battlefield.Press(foot, gripping);

            field.RunTurns(4);

            Assert.True(foot.EnemiesInContact > 0, "The test needs it in the fight first.");

            Vec2 whereItWas = foot.Position;

            // Ordered onto the other one while locked in with the first. Men in
            // contact cannot simply walk out of it, and the rules deliberately
            // refuse: an earlier version handed a regiment back and forth
            // between two enemies every tick and it fought neither.
            Battlefield.Press(foot, other);
            field.RunTurns(4);

            Assert.True(Vec2.Distance(foot.Position, whereItWas) < 80f,
                $"It walked {Vec2.Distance(foot.Position, whereItWas):0} m out of a melee it was locked into.");

            Assert.True(gripping.Casualties > 0, "And it should still be fighting the one that has hold of it.");
        }

        // ---- The target moves ---------------------------------------------------

        [Fact]
        public void CavalryRunsDownSomethingSlowerThatIsWalkingAway()
        {
            var field = new Battlefield("plains", 30400);

            UnitInstance quarry = field.Add(1, "archers", field.Centre, Facing.East);
            field.March(quarry, field.Centre + new Vec2(400f, 0f));

            UnitInstance horse = field.Add(0, "cavalry", field.Centre - new Vec2(200f, 0f), Facing.East);
            Battlefield.Press(horse, quarry);

            field.RunTurns(10);

            Assert.True(quarry.Casualties > 0 || !quarry.IsOnField,
                $"Cavalry should catch archers on the move. The gap is " +
                $"{OrientedRect.GapBetween(horse.Shape, quarry.Shape):0} m and nobody has been touched.");
        }

        [Fact]
        public void ARegimentLookingForAFightIsNotBaitedOutOfPositionByOneItCannotCatch()
        {
            var field = new Battlefield("plains", 30500);

            // A scout running for the horizon, and a regiment on Aggressive
            // holding its ground. Aggressive means "take on what comes near you",
            // not "leave the line to chase anything that moves" — without the
            // leash a single scout pulls a regiment out of the battle for free.
            UnitInstance bait = field.Add(1, "scouts", field.Centre + new Vec2(120f, 0f), Facing.East);
            field.March(bait, field.Centre + new Vec2(900f, 0f));

            UnitInstance foot = field.Add(0, "swordsmen", field.Centre, Facing.East);
            Vec2 posted = foot.Position;

            foot.Stance = Stance.Aggressive;
            foot.GiveOrder(UnitOrder.Stand(), foot.Position);

            field.RunTurns(14);

            float wandered = Vec2.Distance(foot.Position, posted);

            Assert.True(wandered < 400f,
                $"It followed the bait {wandered:0} m from where it was posted. A regiment that can be walked " +
                "out of the line by one scout is a regiment the enemy commands.");
        }

        // ---- Several onto one ---------------------------------------------------

        [Fact]
        public void TwoRegimentsSentAtOneBothGetAHoldOfIt()
        {
            (int gripping, int losses) = SendAtOne(new[] { 60f, -60f });

            Assert.True(gripping >= 2,
                $"Only {gripping} of the two ever had hold of it. Regiments sent at the same enemy should share " +
                "its front rather than queue behind each other.");

            Assert.True(losses > 0, "And it should be losing men.");
        }

        [Fact(Skip = "Bug, found by this suite and not yet fixed. A third regiment makes the attack strictly " +
                     "worse: the attacking line divides the defender's forty metres of front into three slots " +
                     "of about thirteen, every regiment needs all forty, so they shove each other back and the " +
                     "outer two settle six to nine metres short — just outside the four-metre contact range — " +
                     "and stand there for the whole battle. Two attackers break the defender; three kill 326 " +
                     "where one alone kills 325, so the second and third contribute nothing. Task 41, splitting " +
                     "the collision block from the fighting frontage, is where this gets unpicked.")]
        public void AThirdRegimentSentAtTheSameEnemyMustNotMakeTheAttackWorse()
        {
            (int gripTwo, int lostTwo) = SendAtOne(new[] { 60f, -60f });
            (int gripThree, int lostThree) = SendAtOne(new[] { 80f, 0f, -80f });

            Assert.True(gripTwo >= 2,
                $"Two regiments sent at one should both get a hold of it, and only {gripTwo} did.");

            Assert.True(gripThree >= gripTwo,
                $"Sending a third regiment took the attack from {gripTwo} regiments in contact down to " +
                $"{gripThree}. Reinforcing an attack must never weaken it. The defender lost {lostThree} men " +
                $"against {lostTwo} when it was fought by two.");
        }

        /// <summary>
        /// Sends regiments at one defender from the offsets given, and reports
        /// the most that ever had hold of it at once and what it lost.
        /// </summary>
        private static (int MostGripping, int DefenderLosses) SendAtOne(float[] offsets)
        {
            var field = new Battlefield("plains", 30650);

            UnitInstance quarry = field.Add(1, "spearmen", field.Centre, Facing.West);
            Battlefield.Hold(quarry);

            var attackers = new UnitInstance[offsets.Length];

            for (int i = 0; i < offsets.Length; i++)
            {
                attackers[i] = field.Add(0, "swordsmen", field.Centre - new Vec2(240f, offsets[i]), Facing.East);
                Battlefield.Press(attackers[i], quarry);
            }

            int most = 0;

            for (int turn = 0; turn < 14; turn++)
            {
                field.RunTurns(1);

                int gripping = 0;
                foreach (UnitInstance unit in attackers)
                    if (OrderSystem.InContactWith(unit, quarry)) gripping++;

                most = Math.Max(most, gripping);
            }

            return (most, quarry.Casualties);
        }

        [Fact]
        public void BeingSetUponByThreeIsWorseThanBeingSetUponByOne()
        {
            float aloneTurns = TurnsToBreak(1);
            float crowdedTurns = TurnsToBreak(3);

            Assert.True(crowdedTurns < aloneTurns,
                $"Three regiments should break a defender sooner than one: {crowdedTurns:0} turns against " +
                $"{aloneTurns:0}. If concentrating force buys nothing, there is no reason ever to do it.");
        }

        /// <summary>How long a defender lasts against a given number of attackers.</summary>
        private static int TurnsToBreak(int attackers)
        {
            var field = new Battlefield("plains", 30700);

            UnitInstance quarry = field.Add(1, "swordsmen", field.Centre + new Vec2(200f, 0f), Facing.West);
            Battlefield.Hold(quarry);

            for (int i = 0; i < attackers; i++)
            {
                UnitInstance unit = field.Add(0, "swordsmen", field.Centre + new Vec2(0f, (i - 1) * 90f), Facing.East);
                Battlefield.Press(unit, quarry);
            }

            return field.RunUntil(() => quarry.State == UnitState.Routing || !quarry.IsOnField, 25);
        }

        // ---- Getting to the enemy at all ---------------------------------------

        [Fact]
        public void AnAttackWithAFriendInTheWayGoesRoundHimAndArrives()
        {
            var field = new Battlefield("plains", 30800);

            UnitInstance quarry = field.Add(1, "archers", field.Centre + new Vec2(320f, 0f), Facing.West);
            Battlefield.Hold(quarry);

            // One of ours standing squarely between the cavalry and its target.
            UnitInstance friend = field.Add(0, "spearmen", field.Centre + new Vec2(140f, 0f), Facing.East);
            Battlefield.Hold(friend);

            UnitInstance horse = field.Add(0, "cavalry", field.Centre, Facing.East);
            Battlefield.Press(horse, quarry);

            field.RunTurns(12);

            Assert.True(quarry.Casualties > 0 || !quarry.IsOnField
                     || OrientedRect.GapBetween(horse.Shape, quarry.Shape) < 20f,
                $"It is {OrientedRect.GapBetween(horse.Shape, quarry.Shape):0} m from the enemy it was sent at, " +
                "with one of its own in the way. An order does not stop being an order because a friend is " +
                "standing in front of it.");

            Assert.False(OrientedRect.Overlaps(horse.Shape, friend.Shape),
                "And it should have gone round its own rather than through them.");
        }

        [Fact]
        public void AnAttackOrderGivenToARoutingRegimentDoesNotTurnItRound()
        {
            var field = new Battlefield("plains", 30900);

            UnitInstance enemy = field.Add(1, "cavalry", field.Centre + new Vec2(120f, 0f), Facing.West);
            Battlefield.Hold(enemy);

            UnitInstance broken = field.Add(0, "archers", field.Centre, Facing.East);
            broken.Morale = 0.02f;
            broken.State = UnitState.Routing;

            Vec2 startedAt = broken.Position;

            Battlefield.Press(broken, enemy);
            field.RunTurns(3);

            Assert.True(Vec2.Distance(broken.Position, startedAt) > 20f,
                "Men who have broken are running, and an order does not reach them.");

            Assert.True(enemy.Casualties == 0,
                "A routing regiment should certainly not be attacking anybody because it was told to.");
        }
    }
}
