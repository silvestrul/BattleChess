using System;
using BattleChess.Contracts;
using BattleChess.Rules;
using Xunit;

namespace BattleChess.Tests.Battle
{
    /// <summary>
    /// An army is a crowd, and a regiment has to be able to get through it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Forbidding friendly formations from sharing ground fixed lines that were
    /// not lines, and immediately created the opposite problem: a regiment
    /// ordered somewhere would meet one of its own, stop, and stay stopped. In a
    /// recorded game cavalry sent to attack got caught behind one friendly
    /// regiment, was re-ordered, and got caught behind the next.
    /// </para>
    /// <para>
    /// A regiment that stops because a friend is in the way is a regiment that
    /// has stopped taking orders, whatever the log says about why.
    /// </para>
    /// </remarks>
    public sealed class GettingPastYourOwnTests
    {
        [Fact]
        public void ARegimentWorksItsWayRoundAFriendDrawnUpAcrossItsPath()
        {
            var field = new Battlefield("plains", 21000);

            // A spear wall standing squarely between the horse and where it has
            // been told to go — the exact arrangement that used to stop it dead.
            UnitInstance wall = field.Add(0, "spearmen", field.Centre, Facing.East);
            Battlefield.Hold(wall);

            Vec2 start = field.Centre - new Vec2(260f, 0f);
            Vec2 finish = field.Centre + new Vec2(260f, 0f);

            UnitInstance horse = field.Add(0, "cavalry", start, Facing.East);
            field.March(horse, finish);

            field.RunTurns(8);

            Assert.True(horse.Position.X > wall.Position.X + wall.Footprint.HalfDepth,
                $"It should have got round and past. It is at x={horse.Position.X:0} and the regiment in its " +
                $"way is at x={wall.Position.X:0}.");

            Assert.False(OrientedRect.Overlaps(horse.Shape, wall.Shape),
                "And it should have gone round rather than through.");
        }

        [Fact]
        public void AWingMarchesWithoutJammingItselfUp()
        {
            var field = new Battlefield("plains", 21100);

            // Three regiments abreast, tied together, ordered forward as one.
            UnitInstance left = field.Add(0, "swordsmen", field.Centre - new Vec2(0f, 130f), Facing.East);
            UnitInstance middle = field.Add(0, "swordsmen", field.Centre, Facing.East);
            UnitInstance right = field.Add(0, "swordsmen", field.Centre + new Vec2(0f, 130f), Facing.East);

            foreach (UnitInstance unit in new[] { left, middle, right })
                unit.Bond = 1;

            var move = new Vec2(300f, 0f);

            field.March(left, left.Position + move);
            field.March(middle, middle.Position + move);
            field.March(right, right.Position + move);

            field.RunTurns(6);

            foreach (UnitInstance unit in new[] { left, middle, right })
            {
                Assert.True(unit.Position.X > field.Centre.X + 200f,
                    $"{unit.Id} only reached x={unit.Position.X:0} of the {field.Centre.X + 300f:0} it was sent to.");
            }

            // And still abreast: a wing that arrives strung out is not a wing.
            float spread = MathF.Abs(left.Position.X - right.Position.X);

            Assert.True(spread < 20f,
                $"They should have arrived together — their fronts are {spread:0} m apart along the march.");
        }

        [Fact]
        public void AWingKeepsThePaceOfWhicheverRegimentIsOnTheWorstGround()
        {
            // Half the wing in a swamp. The regiments on grass should wait for
            // it rather than walking away and leaving it behind.
            var field = new Battlefield("plains", 21200, RuleSet.Full, canvas =>
            {
                int centre = canvas.ColumnAt(canvas.Columns * canvas.CellSize * 0.5f);
                canvas.Rect(centre - 2, 0, centre + 2, canvas.Rows / 2, "swamp");
            });

            UnitInstance bogged = field.Add(0, "swordsmen", field.Centre - new Vec2(0f, 150f), Facing.East);
            UnitInstance dry = field.Add(0, "swordsmen", field.Centre + new Vec2(0f, 150f), Facing.East);

            bogged.Bond = dry.Bond = 1;

            field.March(bogged, bogged.Position + new Vec2(200f, 0f));
            field.March(dry, dry.Position + new Vec2(200f, 0f));

            field.RunTurns(2);

            float apart = MathF.Abs(bogged.Position.X - dry.Position.X);

            Assert.True(apart < 25f,
                $"The wing should keep the pace of the slowest of it. They are {apart:0} m apart.");
        }

        // ---- Changing front ---------------------------------------------------

        [Fact]
        public void ARegimentCanBeToldToComeAboutWhereItStands()
        {
            var field = new Battlefield("plains", 21300);

            UnitInstance foot = field.Add(0, "swordsmen", field.Centre, Facing.East);
            Battlefield.Hold(foot);

            foot.GiveOrder(UnitOrder.Face(Facing.West), foot.Position);

            field.RunTurns(2);

            Assert.True(Facing.AbsoluteDelta(foot.Facing, Facing.West) < 0.05f,
                $"It was told to face west and is facing {foot.Facing.Degrees:0}°.");

            Assert.True(Vec2.Distance(foot.Position, field.Centre) < 1f,
                "And it should not have gone anywhere to do it.");
        }

        [Fact]
        public void ComingAboutIsNotBlockedByFriendsPressedAgainstIt()
        {
            var field = new Battlefield("plains", 21400);

            UnitInstance foot = field.Add(0, "swordsmen", field.Centre, Facing.East);
            Battlefield.Hold(foot);

            // Regiments crowded onto it front and back, overlapping outright.
            field.Add(0, "swordsmen", field.Centre + new Vec2(6f, 0f), Facing.East);
            field.Add(0, "swordsmen", field.Centre - new Vec2(6f, 0f), Facing.East);

            foot.GiveOrder(UnitOrder.Face(Facing.North), foot.Position);

            field.RunTurns(2);

            Assert.True(Facing.AbsoluteDelta(foot.Facing, Facing.North) < 0.05f,
                $"Men come about within their own frontage and need nobody's permission. It is facing " +
                $"{foot.Facing.Degrees:0}° instead of north — which would mean a regiment in a crowded line " +
                "could never turn to meet an enemy that got round it.");
        }

        [Fact]
        public void ARegimentSentBackwardsTurnsRoundRatherThanWalkingBackwards()
        {
            var field = new Battlefield("plains", 21500);

            UnitInstance foot = field.Add(0, "swordsmen", field.Centre, Facing.East);

            // Told to fall back the way it came, with no bearing drawn.
            field.March(foot, field.Centre - new Vec2(250f, 0f));

            field.RunTurns(1);

            Assert.True(Facing.AbsoluteDelta(foot.Facing, Facing.West) < 0.3f,
                $"It should have come about to march properly. It is facing {foot.Facing.Degrees:0}°, which " +
                "means it is walking backwards at a fifth of its pace.");
        }

        [Fact]
        public void ASidestepStillHoldsTheFrontWhereItWasPut()
        {
            var field = new Battlefield("plains", 21600);

            UnitInstance foot = field.Add(0, "swordsmen", field.Centre, Facing.East);

            // Fifty metres to its left. Turning to face that would swing a
            // hundred metres of frontage through a right angle for a shuffle.
            field.March(foot, field.Centre + new Vec2(0f, 50f));

            field.RunTurns(1);

            Assert.True(Facing.AbsoluteDelta(foot.Facing, Facing.East) < 0.05f,
                $"A short move across the front should keep the front: it is facing {foot.Facing.Degrees:0}°.");
        }
    }
}
