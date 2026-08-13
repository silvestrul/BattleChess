using System;
using System.Collections.Generic;
using BattleChess.Contracts;
using BattleChess.Rules;
using Xunit;

namespace BattleChess.Tests.Battle
{
    /// <summary>
    /// Wings: several regiments taking one order and behaving like one body.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The point of tying regiments together is being able to command a large
    /// army without commanding every regiment in it — to micro-manage when it
    /// matters and to move a whole wing by its centre when it does not. A wing
    /// that arrives strung out, or that jams itself against its own members, has
    /// given up the only thing it was for.
    /// </para>
    /// <para>
    /// A bond is a plain integer on each regiment. Zero means it is on its own,
    /// a positive number means it is hand-tied to everything sharing that
    /// number, and minus one is the temporary bond a box-selection makes. The
    /// rules do not distinguish the temporary one from the rest, deliberately:
    /// selecting a wing and dragging it should behave exactly like a wing that
    /// was bound on purpose.
    /// </para>
    /// </remarks>
    public sealed class GroupManoeuvreTests
    {
        /// <summary>The bond a box-selection makes, as the Unity client sets it.</summary>
        private const int TransientBond = -1;

        /// <summary>Ties regiments into one wing and gives them all the same displacement.</summary>
        private static void MarchTogether(
            Battlefield field, IReadOnlyList<UnitInstance> wing, Vec2 by, int bond = 1, Facing? bearing = null)
        {
            foreach (UnitInstance unit in wing)
                unit.Bond = bond;

            foreach (UnitInstance unit in wing)
                field.March(unit, unit.Position + by, bearing: bearing);
        }

        private static UnitInstance[] ThreeAbreast(Battlefield field, Vec2 centre, string kind = "swordsmen")
        {
            return new[]
            {
                field.Add(0, kind, centre - new Vec2(0f, 60f), Facing.East),
                field.Add(0, kind, centre, Facing.East),
                field.Add(0, kind, centre + new Vec2(0f, 60f), Facing.East),
            };
        }

        /// <summary>The largest distance between any two members of a wing, along one axis.</summary>
        private static float SpreadAlong(IReadOnlyList<UnitInstance> wing, Func<Vec2, float> axis)
        {
            float low = float.MaxValue, high = float.MinValue;

            foreach (UnitInstance unit in wing)
            {
                float value = axis(unit.Position);
                low = MathF.Min(low, value);
                high = MathF.Max(high, value);
            }

            return high - low;
        }

        // ---- Moving as one -----------------------------------------------------

        [Fact]
        public void AWingKeepsItsShapeAcrossALongMarch()
        {
            var field = new Battlefield("plains", 28000);

            UnitInstance[] wing = ThreeAbreast(field, field.Centre - new Vec2(250f, 0f));

            var spacingBefore = new List<float>
            {
                Vec2.Distance(wing[0].Position, wing[1].Position),
                Vec2.Distance(wing[1].Position, wing[2].Position),
            };

            MarchTogether(field, wing, new Vec2(400f, 0f));
            field.RunTurns(10);

            var spacingAfter = new List<float>
            {
                Vec2.Distance(wing[0].Position, wing[1].Position),
                Vec2.Distance(wing[1].Position, wing[2].Position),
            };

            for (int i = 0; i < spacingBefore.Count; i++)
            {
                Assert.True(MathF.Abs(spacingAfter[i] - spacingBefore[i]) < 35f,
                    $"The wing set off {spacingBefore[i]:0} m between neighbours and arrived {spacingAfter[i]:0} m " +
                    "apart. Regiments tied together should hold their intervals.");
            }
        }

        [Fact]
        public void AWingArrivesTogetherRatherThanInOrderOfSpeed()
        {
            var field = new Battlefield("plains", 28100);

            // Horse and foot in the same wing. Left to themselves the cavalry
            // would be there and back before the swordsmen arrived, and a wing
            // whose fast regiments run ahead is not a wing.
            var wing = new[]
            {
                field.Add(0, "cavalry", field.Centre - new Vec2(250f, 60f), Facing.East),
                field.Add(0, "swordsmen", field.Centre - new Vec2(250f, 0f), Facing.East),
                field.Add(0, "spearmen", field.Centre - new Vec2(250f, -60f), Facing.East),
            };

            MarchTogether(field, wing, new Vec2(350f, 0f));
            field.RunTurns(6);

            float spread = SpreadAlong(wing, p => p.X);

            Assert.True(spread < 60f,
                $"They are {spread:0} m apart along the line of march. A wing moves at the pace of its " +
                "slowest regiment or it is not moving as a wing.");
        }

        [Fact]
        public void ATemporaryBondFromABoxSelectionBehavesLikeAPermanentOne()
        {
            var field = new Battlefield("plains", 28200);

            UnitInstance[] wing = ThreeAbreast(field, field.Centre - new Vec2(200f, 0f));

            // Minus one rather than a positive number: what the client writes
            // when the player drags a box round several regiments. Nothing in the
            // rules should care which it is.
            MarchTogether(field, wing, new Vec2(300f, 0f), TransientBond);
            field.RunTurns(8);

            float spread = SpreadAlong(wing, p => p.X);

            Assert.True(spread < 60f,
                $"A box-selected wing should move as one exactly as a bound one does. They are {spread:0} m " +
                "apart along the march.");

            foreach (UnitInstance unit in wing)
            {
                Assert.True(unit.Position.X > field.Centre.X + 40f,
                    $"{unit.Id} only reached x={unit.Position.X:0}.");
            }
        }

        [Fact]
        public void ARegimentCutLooseFromAWingGoesAtItsOwnPace()
        {
            var field = new Battlefield("plains", 28300);

            UnitInstance horse = field.Add(0, "cavalry", field.Centre - new Vec2(250f, 60f), Facing.East);
            UnitInstance foot = field.Add(0, "swordsmen", field.Centre - new Vec2(250f, -60f), Facing.East);

            // Unbound. The cavalry should now leave the infantry behind, which is
            // the whole reason a player unbinds anything.
            horse.Bond = 0;
            foot.Bond = 0;

            // Far enough that the horse is still marching at the end of it. Sent
            // somewhere both can reach inside the run, they simply both arrive
            // and the difference the test is looking for disappears.
            field.March(horse, horse.Position + new Vec2(700f, 0f));
            field.March(foot, foot.Position + new Vec2(700f, 0f));

            field.RunTurns(4);

            Assert.True(horse.Position.X - foot.Position.X > 40f,
                $"Cut loose, the horse should be well ahead. It is {horse.Position.X - foot.Position.X:0} m " +
                "in front, which is barely ahead at all.");
        }

        // ---- Turning as one -----------------------------------------------------

        [Fact]
        public void AWingOrderedOnANewBearingAllComesRoundToIt()
        {
            var field = new Battlefield("plains", 28400);

            UnitInstance[] wing = ThreeAbreast(field, field.Centre - new Vec2(150f, 0f));

            MarchTogether(field, wing, new Vec2(0f, 300f));
            field.RunTurns(4);

            foreach (UnitInstance unit in wing)
            {
                Assert.True(Degrees(Facing.AbsoluteDelta(unit.Facing, Facing.North)) < 20f,
                    $"{unit.Id} was sent north with the rest of its wing and is facing {unit.Facing.Degrees:0}°.");
            }
        }

        [Fact]
        public void AWingToldToHoldItsFrontSidestepsWithoutTurning()
        {
            var field = new Battlefield("plains", 28500);

            UnitInstance[] wing = ThreeAbreast(field, field.Centre - new Vec2(150f, 0f));

            // The whole wing shuffling to its left while keeping its face to the
            // enemy — a real order, and one a player has to ask for by drawing
            // the bearing.
            MarchTogether(field, wing, new Vec2(0f, 120f), bearing: Facing.East);
            field.RunTurns(3);

            foreach (UnitInstance unit in wing)
            {
                Assert.True(Degrees(Facing.AbsoluteDelta(unit.Facing, Facing.East)) < 10f,
                    $"{unit.Id} was told to keep its front and is facing {unit.Facing.Degrees:0}°.");
            }
        }

        [Fact]
        public void OneRegimentAlreadyStandingOnItsNewGroundDoesNotSwingTheWingEast()
        {
            var field = new Battlefield("plains", 28600);

            UnitInstance[] wing = ThreeAbreast(field, field.Centre, "swordsmen");

            foreach (UnitInstance unit in wing)
                unit.Bond = 1;

            // Every member displaced by the same amount, and the middle one told
            // to go exactly where it already is. Without a guard for the
            // degenerate order that regiment reads the bearing of a zero-length
            // vector, which answers due east, and the wing loses a regiment to a
            // ninety-degree turn nobody asked for.
            foreach (UnitInstance unit in wing)
                unit.Facing = Facing.North;

            wing[0].GiveOrder(UnitOrder.MoveTo(wing[0].Position), wing[0].Position);
            wing[1].GiveOrder(UnitOrder.MoveTo(wing[1].Position), wing[1].Position);
            wing[2].GiveOrder(UnitOrder.MoveTo(wing[2].Position), wing[2].Position);

            field.RunTurns(2);

            foreach (UnitInstance unit in wing)
            {
                Assert.True(Degrees(Facing.AbsoluteDelta(unit.Facing, Facing.North)) < 5f,
                    $"{unit.Id} was facing north and swung to {unit.Facing.Degrees:0}°.");
            }
        }

        // ---- Getting in each other's way ---------------------------------------

        [Fact]
        public void AWingDoesNotJamItselfAgainstItsOwnMembers()
        {
            var field = new Battlefield("plains", 28700);

            // Packed tight — neighbours barely a frontage apart, so any berth
            // taken against a friend applies to everybody at once.
            var wing = new List<UnitInstance>();
            for (int i = 0; i < 4; i++)
                wing.Add(field.Add(0, "swordsmen", field.Centre + new Vec2(0f, (i - 2) * 45f), Facing.East));

            MarchTogether(field, wing, new Vec2(300f, 0f));
            field.RunTurns(8);

            foreach (UnitInstance unit in wing)
            {
                Assert.True(unit.Position.X > field.Centre.X + 200f,
                    $"{unit.Id} only reached x={unit.Position.X:0} of the {field.Centre.X + 300f:0} it was sent to. " +
                    "A wing that blocks itself never arrives.");
            }

            for (int i = 0; i < wing.Count; i++)
            for (int j = i + 1; j < wing.Count; j++)
            {
                Assert.False(OrientedRect.Overlaps(wing[i].Shape, wing[j].Shape),
                    $"{wing[i].Id} and {wing[j].Id} finished standing in the same field.");
            }
        }

        [Fact]
        public void AWingGetsRoundAnObstacleInItsPathAndClosesUpAgain()
        {
            var field = new Battlefield("plains", 28800);

            // A regiment not in the wing, planted squarely in front of it.
            UnitInstance rock = field.Add(0, "spearmen", field.Centre, Facing.East);
            Battlefield.Hold(rock);

            UnitInstance[] wing = ThreeAbreast(field, field.Centre - new Vec2(250f, 0f));

            MarchTogether(field, wing, new Vec2(450f, 0f));
            field.RunTurns(14);

            foreach (UnitInstance unit in wing)
            {
                Assert.True(unit.Position.X > rock.Position.X,
                    $"{unit.Id} never got past the obstacle: it is at x={unit.Position.X:0} and the obstacle " +
                    $"is at x={rock.Position.X:0}.");

                Assert.False(OrientedRect.Overlaps(unit.Shape, rock.Shape),
                    $"{unit.Id} ended up standing on the obstacle rather than going round it.");
            }
        }

        [Fact]
        public void TwoWingsCrossingDoNotWalkThroughEachOther()
        {
            var field = new Battlefield("plains", 28900);

            var north = new List<UnitInstance>();
            var south = new List<UnitInstance>();

            for (int i = 0; i < 2; i++)
            {
                north.Add(field.Add(0, "swordsmen", field.Centre + new Vec2(-180f + i * 60f, 130f), Facing.East));
                south.Add(field.Add(0, "swordsmen", field.Centre + new Vec2(-180f + i * 60f, -130f), Facing.East));
            }

            // Ordered across each other's front.
            MarchTogether(field, north, new Vec2(320f, -260f), bond: 1);
            MarchTogether(field, south, new Vec2(320f, 260f), bond: 2);

            field.RunTurns(12);

            var everyone = new List<UnitInstance>(north);
            everyone.AddRange(south);

            for (int i = 0; i < everyone.Count; i++)
            for (int j = i + 1; j < everyone.Count; j++)
            {
                Assert.False(OrientedRect.Overlaps(everyone[i].Shape, everyone[j].Shape),
                    $"{everyone[i].Id} and {everyone[j].Id} are standing in the same field after crossing.");
            }

            foreach (UnitInstance unit in everyone)
            {
                Assert.True(unit.Position.X > field.Centre.X,
                    $"{unit.Id} is still at x={unit.Position.X:0} — the two wings jammed rather than passing.");
            }
        }

        // ---- Attacking as one ---------------------------------------------------

        [Fact]
        public void AWingSentAtOneEnemyAllGetsIntoTheFight()
        {
            var field = new Battlefield("plains", 29000);

            UnitInstance quarry = field.Add(1, "spearmen", field.Centre + new Vec2(220f, 0f), Facing.West);
            Battlefield.Hold(quarry);

            UnitInstance[] wing = ThreeAbreast(field, field.Centre - new Vec2(60f, 0f));

            foreach (UnitInstance unit in wing)
            {
                unit.Bond = 1;
                Battlefield.Press(unit, quarry);
            }

            // Counted turn by turn. Judged at the end it reads as nobody having
            // arrived, because the defender breaks under three regiments and the
            // whole wing is strung out chasing it by then.
            int mostArrived = 0;

            for (int turn = 0; turn < 10; turn++)
            {
                field.RunTurns(1);

                int arrived = 0;
                foreach (UnitInstance unit in wing)
                    if (unit.EnemiesInContact > 0 || OrientedRect.GapBetween(unit.Shape, quarry.Shape) < 20f)
                        arrived++;

                mostArrived = Math.Max(mostArrived, arrived);
            }

            Assert.True(mostArrived >= 2,
                $"Only {mostArrived} of the three got to the enemy they were all sent at. A wing ordered onto " +
                "one regiment should bring what will fit against it, not queue up behind itself.");

            Assert.True(quarry.Casualties > 0, "And the enemy should be taking losses from it.");
        }

        [Fact]
        public void AMixedWingSentAtAnEnemyLetsTheArchersStopAtTheirOwnRange()
        {
            var field = new Battlefield("plains", 29100);

            UnitInstance quarry = field.Add(1, "spearmen", field.Centre + new Vec2(260f, 0f), Facing.West);
            Battlefield.Hold(quarry);

            UnitInstance foot = field.Add(0, "swordsmen", field.Centre - new Vec2(0f, 60f), Facing.East);
            UnitInstance bows = field.Add(0, "archers", field.Centre + new Vec2(0f, 60f), Facing.East);

            foot.Bond = bows.Bond = 1;

            Battlefield.Press(foot, quarry);
            Battlefield.Press(bows, quarry);

            // Watched turn by turn rather than judged at the end. The spearmen
            // break under the combined attack and run, and a pursuing regiment
            // measured after that reads as one that never closed at all.
            bool swordsmenClosed = false;
            float nearestTheArchersCame = float.MaxValue;

            for (int turn = 0; turn < 8; turn++)
            {
                field.RunTurns(1);

                swordsmenClosed |= foot.EnemiesInContact > 0;

                if (quarry.State != UnitState.Routing)
                {
                    nearestTheArchersCame =
                        MathF.Min(nearestTheArchersCame, OrientedRect.GapBetween(bows.Shape, quarry.Shape));
                }
            }

            Assert.True(nearestTheArchersCame > 20f,
                $"The archers closed to {nearestTheArchersCame:0} m. Told to attack, shooters shoot — they do " +
                "not charge home alongside the swordsmen.");

            Assert.True(swordsmenClosed,
                "And the swordsmen in the same wing should still have gone in. Binding archers to infantry must " +
                "not stop the infantry fighting.");
        }

        private static float Degrees(float radians) => radians * 180f / MathF.PI;
    }
}
