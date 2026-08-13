using System;
using BattleChess.Contracts;
using BattleChess.Rules;
using Xunit;

namespace BattleChess.Tests.Battle
{
    /// <summary>
    /// Where a regiment points is the player's business, and an attack squares
    /// itself up on the way in.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two rules that used to be one. Regiments once came about on their own to
    /// face whoever was fighting them, which fixed a real problem — cavalry that
    /// overshot a charge and halted side-on fought the whole melee flanked — at
    /// the cost of making position something the simulation quietly repaired.
    /// Getting round an enemy was worth very little, because they simply turned
    /// round.
    /// </para>
    /// <para>
    /// So the automatic turn is gone and the fix moved to where the mistake was
    /// actually being made: the approach. A regiment ordered to attack marches
    /// at its enemy by whatever line the ground allows, and then, inside a
    /// hundred metres, dresses — it repositions onto the enemy's centre and
    /// comes round at five times its usual rate until the two rectangles are
    /// face to face. A charge you ordered arrives properly; a flank you were
    /// caught on is a flank you keep.
    /// </para>
    /// </remarks>
    public sealed class FacingTheEnemyTests
    {
        // ---- Nothing turns on its own -----------------------------------------

        [Fact]
        public void ARegimentAttackedFromBehindDoesNotComeAboutByItself()
        {
            var field = new Battlefield("plains", 19000);

            // Facing east, with the enemy arriving from due west — squarely in
            // its back, and it is standing on Defend with no orders of its own.
            UnitInstance ours = field.Add(0, "swordsmen", field.Centre, Facing.East);
            Battlefield.Hold(ours);

            UnitInstance theirs = field.Add(1, "swordsmen", field.Centre - new Vec2(220f, 0f), Facing.East);
            Battlefield.Press(theirs, ours);

            Facing pointedAtStart = ours.Facing;

            // Measured while it is still a formation. A regiment that has broken
            // faces wherever it is running, which is a different rule entirely
            // and would make this test pass or fail on the rout.
            field.RunUntil(() => ours.EnemiesInContact > 0, maxTurns: 8);
            field.RunPulses(2);

            Assert.True(ours.IsFighting,
                "It broke before the measurement, so this proves nothing — shorten the fight.");

            Assert.True(Facing.AbsoluteDelta(ours.Facing, pointedAtStart) < 0.05f,
                $"It was left facing {pointedAtStart.Degrees:0}° and is now facing {ours.Facing.Degrees:0}°. Being caught " +
                "out of position is a mistake the player made and should keep — the whole value of getting " +
                "behind an enemy is that they are still facing the way you left them.");
        }

        [Fact]
        public void BeingTakenInTheBackCostsThemDearly()
        {
            var field = new Battlefield("plains", 19050);

            UnitInstance ours = field.Add(0, "swordsmen", field.Centre, Facing.East);
            Battlefield.Hold(ours);

            UnitInstance theirs = field.Add(1, "swordsmen", field.Centre - new Vec2(220f, 0f), Facing.East);
            Battlefield.Press(theirs, ours);

            field.RunUntilDecided(12, ours, theirs);

            Assert.True(Battlefield.LostPercent(ours) > Battlefield.LostPercent(theirs) * 1.5f,
                $"Identical regiments, and the one taken from behind should be losing badly: it lost " +
                $"{Battlefield.LostPercent(ours):0}% against {Battlefield.LostPercent(theirs):0}%.");
        }

        // ---- Dressing on the final approach -----------------------------------

        [Fact]
        public void AChargeOrderedFromTheFrontArrivesSquare()
        {
            var field = new Battlefield("plains", 19100);

            UnitInstance foot = field.Add(1, "swordsmen", field.Centre, Facing.West);
            Battlefield.Hold(foot);

            // Set off from the south-west, so the straight line in is a diagonal
            // and nothing about the approach would leave it aligned.
            UnitInstance horse = field.Add(0, "cavalry", field.Centre - new Vec2(300f, 260f), Facing.North);
            Battlefield.Press(horse, foot);

            field.RunUntil(() => OrderSystem.InContactWith(horse, foot), maxTurns: 8);

            Assert.True(OffSquare(horse, foot) < 20f,
                $"It should have squared up during the final approach: the two fronts are {OffSquare(horse, foot):0}° " +
                "apart. It came in on a diagonal, so nothing but dressing could have aligned it.");
        }

        [Fact]
        public void TheTwoRegimentsEndUpCentreOnCentre()
        {
            var field = new Battlefield("plains", 19200);

            UnitInstance foot = field.Add(1, "swordsmen", field.Centre, Facing.West);
            Battlefield.Hold(foot);

            UnitInstance horse = field.Add(0, "cavalry", field.Centre - new Vec2(300f, 260f), Facing.North);
            Battlefield.Press(horse, foot);

            field.RunUntil(() => OrderSystem.InContactWith(horse, foot), maxTurns: 8);

            // How far the attacker's centre sits along the defender's frontage
            // from directly opposite. Two equal rectangles meeting properly have
            // this at nearly zero.
            float slippedAlongTheFront = MathF.Abs(
                Vec2.Dot(horse.Position - foot.Position, foot.Shape.Right));

            Assert.True(slippedAlongTheFront < foot.Footprint.Width * 0.25f,
                $"Centres should line up: the attacker is {slippedAlongTheFront:0} m off to one side of a " +
                $"{foot.Footprint.Width:0} m front.");
        }

        [Fact]
        public void AChargeOrderedFromTheFlankSquaresUpOnTheFlank()
        {
            var field = new Battlefield("plains", 19300);

            // Facing east, so its flanks lie north and south.
            UnitInstance foot = field.Add(1, "swordsmen", field.Centre, Facing.East);
            Battlefield.Hold(foot);

            // Coming from due south, off the end of the line.
            UnitInstance horse = field.Add(0, "cavalry", field.Centre - new Vec2(0f, 280f), Facing.North);
            Battlefield.Press(horse, foot);

            field.RunUntil(() => OrderSystem.InContactWith(horse, foot), maxTurns: 8);

            // It should be facing north, into the defender's southern flank —
            // which is at a right angle to the defender's own front.
            float offTheirFront = Facing.AbsoluteDelta(horse.Facing, foot.Facing) * 180f / MathF.PI;

            Assert.True(offTheirFront > 60f,
                $"Attacking a line from the side should end up square on its flank, at a right angle to its " +
                $"front. The two are {offTheirFront:0}° apart.");

            float slippedAlongTheirDepth = MathF.Abs(
                Vec2.Dot(horse.Position - foot.Position, foot.Shape.Forward));

            Assert.True(slippedAlongTheirDepth < foot.Footprint.Width * 0.25f,
                $"And centre on centre even there: {slippedAlongTheirDepth:0} m off.");
        }

        // ---- The bug this was found through -----------------------------------

        [Fact]
        public void CavalryThatSetsOffAtAnAngleIsNotDoomedByIt()
        {
            // Cavalry charging home and halting side-on used to fight the whole
            // melee flanked, losing to swordsmen it beats comfortably. Dressing
            // is what fixes it now: the bad bearing is paid for in the seconds
            // spent coming round on the approach, not in losing the battle.
            var field = new Battlefield("plains", 19400);

            UnitInstance foot = field.Add(1, "swordsmen", field.Centre, Facing.West);
            Battlefield.Hold(foot);

            // Deliberately started facing ninety degrees off its line of march.
            UnitInstance horse = field.Add(0, "cavalry", field.Centre - new Vec2(220f, 0f), Facing.North);
            Battlefield.Press(horse, foot);

            field.RunUntilDecided(14, horse, foot);

            Assert.True(Battlefield.LostPercent(foot) > Battlefield.LostPercent(horse),
                $"Horse lost {Battlefield.LostPercent(horse):0}%, foot {Battlefield.LostPercent(foot):0}%.");
        }

        // ---- Shooters do not charge -------------------------------------------

        [Fact]
        public void ArchersToldToAttackStopAndShoot()
        {
            var field = new Battlefield("plains", 19500);

            UnitInstance foot = field.Add(1, "spearmen", field.Centre, Facing.West);
            Battlefield.Hold(foot);

            UnitInstance bows = field.Add(0, "archers", field.Centre - new Vec2(400f, 0f), Facing.East);
            Battlefield.Press(bows, foot);

            field.RunTurns(6);

            float reach = bows.Def.Get(UnitAttributes.Range);
            float closed = Vec2.Distance(bows.Position, foot.Position);

            Assert.True(closed <= reach && closed > reach * 0.6f,
                $"Bowmen told to attack are being told to shoot. They should have halted inside their " +
                $"{reach:0} m reach and stayed there; they are {closed:0} m away.");

            Assert.False(OrderSystem.InContactWith(bows, foot),
                "And they should never have crossed swords with a spear wall at all.");

            // Asked of the arrows rather than of the log. The shooting rule
            // reports that a regiment is shooting, once, rather than narrating
            // every volley — so counting lines counts nothing, whereas a spent
            // quiver is proof either way.
            Assert.True(bows.ShotsLeft < bows.Def.Get(UnitAttributes.Ammunition) * bows.InitialStrength,
                "They should have been shooting the whole time.");
        }

        /// <summary>
        /// Degrees between two regiments' fronts once they are drawn up facing
        /// each other. Zero means flush.
        /// </summary>
        private static float OffSquare(UnitInstance unit, UnitInstance enemy) =>
            MathF.Abs(180f - Facing.AbsoluteDelta(unit.Facing, enemy.Facing) * 180f / MathF.PI);
    }
}
