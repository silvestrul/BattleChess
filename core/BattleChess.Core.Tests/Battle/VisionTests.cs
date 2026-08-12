using BattleChess.Contracts;
using BattleChess.Rules;
using Xunit;

namespace BattleChess.Tests.Battle
{
    /// <summary>
    /// Who can see whom: how far a regiment sees, what stops it, and how far
    /// off it is noticed.
    /// </summary>
    /// <remarks>
    /// Two mechanisms and they are deliberately different. <b>Height</b> is
    /// opaque — an army behind a ridge is simply not there, which is what makes
    /// marching round a range a plan rather than a detour. <b>Density</b> is
    /// not — woods eat the distance you can see, so you make out the regiment
    /// at the treeline and not the one behind it.
    /// </remarks>
    public sealed class VisionTests
    {
        private const float ArmyGap = 200f;

        // ---- How far a regiment sees -----------------------------------------

        [Fact]
        public void HighGroundSeesFurther()
        {
            float onTheFlat = SightFrom("plains");
            float onAHill = SightFrom("hill");
            float onAPeak = SightFrom("mountain");

            Assert.True(onAHill > onTheFlat,
                $"A hill should be worth taking for the view alone: flat {onTheFlat:0} m, hill {onAHill:0} m.");

            Assert.True(onAPeak > onAHill,
                $"And a peak further still: hill {onAHill:0} m, mountain {onAPeak:0} m.");
        }

        private static float SightFrom(string ground)
        {
            var field = new Battlefield(ground, 11000);
            UnitInstance watcher = field.Add(0, "swordsmen", field.Centre, Facing.East);

            return LineOfSight.SightRange(field.State, watcher);
        }

        // ---- Ridges are opaque ------------------------------------------------

        [Fact]
        public void ARidgeBetweenTwoArmiesHidesThem()
        {
            Assert.True(SeesAcross(null), "Two hundred metres of open plain should be no obstacle at all.");

            Assert.False(SeesAcross("mountain"),
                "A mountain range between two armies must hide them completely — this is what makes " +
                "marching round one a plan instead of a detour.");
        }

        [Fact]
        public void AHillIsEnoughToHideBehind()
        {
            Assert.False(SeesAcross("hill"),
                "Dead ground behind a rise should conceal an army just as a mountain does. Height blocks; " +
                "how much height only decides who can see over it.");
        }

        [Fact]
        public void StandingOnHighGroundLooksOverALowerRidge()
        {
            Assert.False(SeesAcross("hill"),
                "From the flat, a rise in between is opaque.");

            Assert.True(SeesAcross("hill", observerGround: "mountain"),
                "From a peak, that same rise is something you look over. Sight grazes the higher end of " +
                "the line, so only ground above it blocks.");
        }

        [Fact]
        public void TwoUnitsOnTheSameHeightSeeEachOtherOverThatHeight()
        {
            Assert.True(SeesAcross("hill", observerGround: "hill", targetGround: "hill"),
                "Hilltop to hilltop across an equal rise should be clear — sight grazes the tops.");
        }

        /// <summary>
        /// Puts two regiments 200 m apart with an optional band of something in
        /// between, and reports whether the first can see the second.
        /// </summary>
        private static bool SeesAcross(string? between, string observerGround = "plains", string targetGround = "plains")
        {
            var field = new Battlefield("plains", 11100, RuleSet.Full, canvas =>
            {
                Vec2 middle = new Vec2(canvas.Columns * canvas.CellSize * 0.5f, 0f);

                int observerColumn = canvas.ColumnAt(middle.X - ArmyGap * 0.5f);
                int targetColumn = canvas.ColumnAt(middle.X + ArmyGap * 0.5f);
                int centreColumn = canvas.ColumnAt(middle.X);

                if (observerGround != "plains") canvas.Band(observerColumn - 1, observerColumn + 1, observerGround);
                if (targetGround != "plains") canvas.Band(targetColumn - 1, targetColumn + 1, targetGround);
                if (between != null) canvas.Band(centreColumn - 1, centreColumn + 1, between);
            });

            UnitInstance watcher = field.Add(0, "scouts", field.Centre - new Vec2(ArmyGap * 0.5f, 0f), Facing.East);
            UnitInstance enemy = field.Add(1, "swordsmen", field.Centre + new Vec2(ArmyGap * 0.5f, 0f), Facing.West);

            return LineOfSight.CanSee(field.State, watcher, enemy);
        }

        // ---- Woods are thick, not opaque --------------------------------------

        [Fact]
        public void YouSeeIntoWoodsButNotThroughThem()
        {
            Assert.True(SeesThroughForest(cellsOfWood: 1),
                "A regiment just inside the treeline should be visible — woods are thick, not opaque.");

            Assert.False(SeesThroughForest(cellsOfWood: 4),
                "One a hundred metres back among the trunks should not be. That gap is the use of a wood.");
        }

        [Fact]
        public void HighGroundSeesFurtherIntoWoods()
        {
            Assert.False(SeesThroughForest(cellsOfWood: 4),
                "From the flat, a hundred metres of wood should be far too much.");

            Assert.True(SeesThroughForest(cellsOfWood: 4, observerGround: "mountain"),
                "Looking down into a wood is easier than looking through it from among the trunks — " +
                "though never free, which is why a canopy still hides an army from a peak.");
        }

        /// <summary>
        /// Observer and target both on open ground, with a belt of forest of a
        /// given depth standing between them. Each cell is 25 m.
        /// </summary>
        private static bool SeesThroughForest(int cellsOfWood, string observerGround = "plains")
        {
            const float gap = 150f;

            var field = new Battlefield("plains", 11200, RuleSet.Full, canvas =>
            {
                int middle = canvas.Columns / 2;
                int first = middle - cellsOfWood / 2;

                canvas.Band(first, first + cellsOfWood - 1, "forest");

                if (observerGround != "plains")
                {
                    // A single column, so the observer's own ground never eats
                    // into the belt of wood the test is measuring.
                    int column = canvas.ColumnAt(canvas.Columns * canvas.CellSize * 0.5f - gap * 0.5f);
                    canvas.Band(column, column, observerGround);
                }
            });

            UnitInstance watcher = field.Add(0, "scouts", field.Centre - new Vec2(gap * 0.5f, 0f), Facing.East);
            UnitInstance enemy = field.Add(1, "swordsmen", field.Centre + new Vec2(gap * 0.5f, 0f), Facing.West);

            return LineOfSight.CanSee(field.State, watcher, enemy);
        }

        // ---- Being noticed ----------------------------------------------------

        [Fact]
        public void MenInWoodsAreOnlySpottedAtCloseRange()
        {
            var field = new Battlefield("plains", 11300, RuleSet.Full, canvas =>
                canvas.Band(canvas.Columns / 2, canvas.Columns / 2 + 2, "forest"));

            UnitInstance inTheOpen = field.Add(1, "swordsmen", field.Centre - new Vec2(400f, 0f), Facing.East);
            UnitInstance inTheTrees = field.Add(1, "swordsmen", field.Centre, Facing.East);

            float open = LineOfSight.DetectionRange(field.State, inTheOpen, 200f);
            float hidden = LineOfSight.DetectionRange(field.State, inTheTrees, 200f);

            Assert.True(hidden <= 0.5f * open,
                $"Woods should hide men, not merely slow them: spotted at {open:0} m in the open " +
                $"against {hidden:0} m under the trees.");
        }

        [Fact]
        public void ScoutsAreNoticedAtHalfTheDistanceAnythingElseIs()
        {
            var field = new Battlefield("plains", 11400);

            UnitInstance scouts = field.Add(1, "scouts", field.Centre, Facing.East);
            UnitInstance swordsmen = field.Add(1, "swordsmen", field.Centre + new Vec2(400f, 0f), Facing.East);

            float forScouts = LineOfSight.DetectionRange(field.State, scouts, 200f);
            float forInfantry = LineOfSight.DetectionRange(field.State, swordsmen, 200f);

            Assert.True(forScouts <= 0.6f * forInfantry,
                $"Getting close unseen is what scouts are actually for: they are noticed at {forScouts:0} m " +
                $"against infantry's {forInfantry:0} m.");
        }

        [Fact]
        public void ABatteryIsNoticedFurtherOffThanAnythingElse()
        {
            var field = new Battlefield("plains", 11500);

            UnitInstance guns = field.Add(1, "artillery", field.Centre, Facing.East);
            UnitInstance swordsmen = field.Add(1, "swordsmen", field.Centre + new Vec2(400f, 0f), Facing.East);

            Assert.True(
                LineOfSight.DetectionRange(field.State, guns, 200f) >
                LineOfSight.DetectionRange(field.State, swordsmen, 200f),
                "Guns are big and smoky. Hiding one should be harder than hiding infantry.");
        }

        [Fact]
        public void ScoutsNoLongerOutseeEverythingByMiles()
        {
            float scoutSight = TestContent.Unit("scouts").Get(UnitAttributes.Vision);
            float cavalrySight = TestContent.Unit("cavalry").Get(UnitAttributes.Vision);

            Assert.True(scoutSight > cavalrySight,
                "Scouts need some edge, or cavalry does their job better and also fights.");

            Assert.True(scoutSight <= 1.3f * cavalrySight,
                $"But it must stay an edge, not the point of them — a large radius makes scouting passive, " +
                $"which is the opposite of what the unit is for. Scouts {scoutSight:0} m, cavalry {cavalrySight:0} m.");
        }

        // ---- Per army, not per regiment ---------------------------------------

        [Fact]
        public void WhatOneRegimentSeesTheWholeArmyKnows()
        {
            var field = new Battlefield("plains", 11600);

            // Blind infantry well to the rear, scouts forward doing the looking.
            UnitInstance rear = field.Add(0, "swordsmen", field.Centre - new Vec2(600f, 0f), Facing.East);
            UnitInstance scouts = field.Add(0, "scouts", field.Centre - new Vec2(150f, 0f), Facing.East);
            UnitInstance enemy = field.Add(1, "swordsmen", field.Centre, Facing.West);

            field.State.Vision.Recompute(field.State);

            Assert.False(LineOfSight.CanSee(field.State, rear, enemy),
                "The rear regiment cannot possibly see that far itself.");

            Assert.True(LineOfSight.CanSee(field.State, scouts, enemy),
                "The scouts should have it.");

            Assert.True(field.State.Vision.CanSee(field.State, rear.Owner, enemy),
                "And the army should therefore know about it. That union is the entire reason to field " +
                "a unit that buys information rather than fights.");
        }

        [Fact]
        public void AnArmyAlwaysKnowsItsOwnRegiments()
        {
            var field = new Battlefield("plains", 11700);

            UnitInstance near = field.Add(0, "swordsmen", field.Centre, Facing.East);
            UnitInstance faraway = field.Add(0, "swordsmen", field.Centre + new Vec2(900f, 0f), Facing.East);

            field.State.Vision.Recompute(field.State);

            Assert.True(field.State.Vision.CanSee(field.State, near.Owner, faraway),
                "A commander does not have to spot his own men.");
        }

        [Fact]
        public void AnArmySeesNothingOfAnEnemyBehindARange()
        {
            var field = new Battlefield("plains", 11800, RuleSet.Full, canvas =>
                canvas.Band(canvas.Columns / 2 - 1, canvas.Columns / 2 + 1, "mountain"));

            UnitInstance ours = field.Add(0, "scouts", field.Centre - new Vec2(100f, 0f), Facing.East);
            UnitInstance theirs = field.Add(1, "swordsmen", field.Centre + new Vec2(100f, 0f), Facing.West);

            field.State.Vision.Recompute(field.State);

            Assert.False(field.State.Vision.CanSee(field.State, ours.Owner, theirs),
                "Two hundred metres apart and utterly invisible to one another, because a range stands " +
                "between them. This is the manoeuvre the whole vision rule exists to make possible.");
        }

        // ---- What sight is actually worth --------------------------------------
        //
        // Vision that only greys out a sprite is decoration. These two are the
        // tests that make it a rule: reach is worth nothing without eyes, and
        // ground you cannot see over is ground you cannot shoot over.

        [Fact]
        public void ShootersCannotFireAtEnemiesTheyCannotSee()
        {
            Assert.Equal(0f, ShellingLosses(withScouts: false));

            Assert.True(ShellingLosses(withScouts: true) > 0f,
                "Guns reach 360 m and see 200. Left to themselves they should be firing at nothing; " +
                "given a scout far enough forward to find the enemy, they should open up. That is the " +
                "whole bargain of a long-ranged weapon — reach is worthless without eyes.");
        }

        /// <summary>
        /// A battery 300 m from an enemy it cannot possibly see, with or without
        /// a scouting party forward to find them for it.
        /// </summary>
        private static float ShellingLosses(bool withScouts)
        {
            var field = new Battlefield("plains", 11900);

            UnitInstance guns = field.Add(0, "artillery", field.Centre - new Vec2(300f, 0f), Facing.East);
            UnitInstance enemy = field.Add(1, "swordsmen", field.Centre, Facing.West);

            Battlefield.Hold(guns);
            Battlefield.Hold(enemy);

            if (withScouts)
            {
                UnitInstance scouts = field.Add(0, "scouts", field.Centre - new Vec2(150f, 0f), Facing.East);
                Battlefield.Hold(scouts);
            }

            field.RunTurns(4);

            return Battlefield.LostPercent(enemy);
        }

        [Fact]
        public void AHillBetweenTwoRegimentsStopsTheVolleys()
        {
            Assert.True(VolleyLosses(ridge: null) > 0f,
                "A hundred and twenty metres of open ground is well inside bowshot.");

            Assert.Equal(0f, VolleyLosses(ridge: "hill"));
        }

        /// <summary>
        /// Two bodies of archers 120 m apart — comfortably inside their 145 m
        /// range — with an optional rise standing between them.
        /// </summary>
        private static float VolleyLosses(string? ridge)
        {
            const float gap = 120f;

            var field = new Battlefield("plains", 12000, RuleSet.Full, canvas =>
            {
                if (ridge == null) return;

                int middle = canvas.ColumnAt(canvas.Columns * canvas.CellSize * 0.5f);
                canvas.Band(middle - 1, middle + 1, ridge);
            });

            UnitInstance ours = field.Add(0, "archers", field.Centre - new Vec2(gap * 0.5f, 0f), Facing.East);
            UnitInstance theirs = field.Add(1, "archers", field.Centre + new Vec2(gap * 0.5f, 0f), Facing.West);

            Battlefield.Hold(ours);
            Battlefield.Hold(theirs);

            field.RunTurns(3);

            return Battlefield.LostPercent(theirs);
        }
    }
}
