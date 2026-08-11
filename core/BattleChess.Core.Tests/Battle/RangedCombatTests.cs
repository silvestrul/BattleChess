using System;
using BattleChess.Contracts;
using BattleChess.Rules;
using Xunit;

namespace BattleChess.Tests.Battle
{
    /// <summary>
    /// Shooting: what it costs to cross open ground, and what changes that
    /// price.
    /// </summary>
    /// <remarks>
    /// The clock the whole design runs on. Archers reach 180 m, so infantry
    /// closing at 1.6 m/s spends about ten volleys in the open while cavalry at
    /// 4.8 m/s takes three. Everything here is a test of that gap or of
    /// something that widens or narrows it.
    /// </remarks>
    public sealed class RangedCombatTests
    {
        // ---- The price of crossing open ground -------------------------------

        [Theory]
        [InlineData("spearmen")]
        [InlineData("swordsmen")]
        public void ArchersBleedInfantryCrossingTheirRange(string infantry)
        {
            DuelResult fight = Approach(infantry, "plains");

            Assert.True(fight.AttackerLost >= 5f,
                $"Crossing 250 m in front of archers should cost something real. {fight}");

            Assert.True(fight.AttackerLost <= 35f,
                $"It should be expensive, not impossible — the melee must still be worth having. {fight}");
        }

        [Fact]
        public void SpeedIsWhatCavalryIsActuallyBuying()
        {
            float onFoot = Approach("swordsmen", "plains").AttackerLost;
            float mounted = Approach("cavalry", "plains").AttackerLost;

            Assert.True(mounted < onFoot,
                $"Cavalry's whole argument is spending less time under fire: foot lost {onFoot:0}%, horse {mounted:0}%.");
        }

        [Fact]
        public void InfantryStillReachesTheArchersAndBeatsThem()
        {
            DuelResult fight = Approach("swordsmen", "plains");

            Assert.True(fight.AttackerWon,
                $"Archers must not be able to win a battle on their own. {fight}");

            Assert.True(fight.DefenderLost >= 30f,
                $"Bowmen caught by infantry that got across should pay for it. {fight}");
        }

        // ---- Distance --------------------------------------------------------

        /// <summary>How far bowmen actually reach, read from content.</summary>
        private static float BowRange => TestContent.Unit("archers").Get(UnitAttributes.Range);

        [Fact]
        public void ShootingFallsAwayWithDistance()
        {
            float pointBlank = LossesShotAt(40f);
            float longRange = LossesShotAt(BowRange * 0.95f);

            Assert.True(pointBlank >= 1.3f * longRange,
                $"Volleys should weaken with range: 40 m cost {pointBlank:0.0}%, 175 m cost {longRange:0.0}%.");
        }

        [Fact]
        public void RangeIsAnEdgeNotACliff()
        {
            float justInside = LossesShotAt(BowRange * 0.95f);
            float justOutside = LossesShotAt(BowRange * 1.15f);

            Assert.True(justInside > 0f, "Archers should still do something at the edge of their range.");
            Assert.Equal(0f, justOutside);
        }

        private static float LossesShotAt(float distance)
        {
            var field = new Battlefield("plains", 5000);

            UnitInstance target = field.Add(0, "swordsmen", field.Centre, Facing.East);
            UnitInstance shooter = field.Add(1, "archers", field.Centre + new Vec2(distance, 0f), Facing.West);

            Battlefield.Hold(target);
            Battlefield.Hold(shooter);

            field.RunTurns(2);

            return Battlefield.LostPercent(target);
        }

        // ---- Nobody shoots with an enemy among them --------------------------

        [Fact]
        public void ArchersInContactStopShooting()
        {
            var field = new Battlefield("plains", 5100);

            UnitInstance archers = field.Add(0, "archers", field.Centre, Facing.East);

            // One regiment in their faces, another well within bowshot behind it.
            UnitInstance attacker = field.Add(1, "swordsmen",
                field.Centre + new Vec2(archers.Footprint.Depth + 4f, 0f), Facing.West);

            UnitInstance reserve = field.Add(1, "swordsmen", field.Centre + new Vec2(140f, 0f), Facing.West);

            Battlefield.Hold(archers);
            Battlefield.Hold(attacker);
            Battlefield.Hold(reserve);

            field.RunTurns(3);

            Assert.Equal(0, reserve.Casualties);
        }

        // ---- Ground ----------------------------------------------------------

        [Fact]
        public void WoodsBluntArchery()
        {
            float inTheOpen = Approach("swordsmen", "plains").AttackerLost;
            float underTrees = Approach("swordsmen", "forest").AttackerLost;

            Assert.True(underTrees <= 0.95f * inTheOpen,
                $"Trees stop arrows far better than they stop swords: open {inTheOpen:0}%, forest {underTrees:0}%.");
        }

        [Fact]
        public void ACrossingUnderFireIsTheMostExpensiveGroundOnTheMap()
        {
            float inTheOpen = Approach("cavalry", "plains").AttackerLost;
            float inTheWater = Approach("cavalry", "river").AttackerLost;

            Assert.True(inTheWater >= 2f * inTheOpen,
                $"A defended crossing should be brutal: plains {inTheOpen:0}%, river {inTheWater:0}%.");
        }

        [Fact]
        public void BadGroundCostsTwiceOverForEveryone()
        {
            foreach (string attacker in new[] { "spearmen", "swordsmen", "cavalry" })
            {
                float inTheOpen = Approach(attacker, "plains").AttackerLost;
                float inTheSwamp = Approach(attacker, "swamp").AttackerLost;

                Assert.True(inTheSwamp > inTheOpen,
                    $"{attacker} should suffer more wading than marching: plains {inTheOpen:0}%, swamp {inTheSwamp:0}%.");
            }
        }

        // ---- Formation -------------------------------------------------------

        [Fact]
        public void LooseOrderIsTheAnswerToArchery()
        {
            float inLine = ApproachInFormation("line");
            float spreadOut = ApproachInFormation("loose");

            Assert.True(spreadOut <= 0.85f * inLine,
                $"Opening the ranks should be the obvious answer to being shot at: " +
                $"line {inLine:0}%, loose {spreadOut:0}%.");
        }

        [Fact]
        public void ASquareIsTheEasiestMarkOnTheField()
        {
            float inLine = ApproachInFormation("line");
            float inSquare = ApproachInFormation("square");

            Assert.True(inSquare >= 1.15f * inLine,
                $"The price of a square is being shot at: line {inLine:0}%, square {inSquare:0}%.");
        }

        /// <summary>
        /// What crossing 250 m of open ground in a given formation costs, read
        /// at the moment the two lines meet.
        /// </summary>
        /// <remarks>
        /// Stopped at contact rather than fought to a decision. Both of these
        /// tests are about what a formation costs you <i>under fire</i>, and
        /// running on into the melee measured the melee as well — which for a
        /// square is a quite different and much larger number pulling the other
        /// way. It happened to come out right while the approach was long enough
        /// to dominate, and stopped the moment the rectangle shrank and
        /// regiments had to close further before fighting.
        /// </remarks>
        private static float ApproachInFormation(string formation)
        {
            var field = new Battlefield("plains", 3000);

            UnitInstance bows = field.Add(1, "archers", field.Centre, Facing.West);
            Battlefield.Hold(bows);

            UnitInstance foot = field.Add(0, "swordsmen", field.Centre - new Vec2(250f, 0f), Facing.East,
                formation: formation);

            Battlefield.Press(foot, bows);

            field.RunUntil(() => OrderSystem.InContactWith(foot, bows), maxTurns: 8);

            return Battlefield.LostPercent(foot);
        }

        // ---- Artillery -------------------------------------------------------

        [Fact]
        public void ArtilleryCannotShootWhatNobodyHasSeen()
        {
            var field = new Battlefield("plains", 5200);

            UnitInstance guns = field.Add(0, "artillery", field.Centre, Facing.East);
            UnitInstance archers = field.Add(1, "archers", field.Centre + new Vec2(GunRange * 0.9f, 0f), Facing.West);

            Battlefield.Hold(guns);
            Battlefield.Hold(archers);

            field.RunTurns(4);

            Assert.Equal(0, archers.Casualties);
        }

        /// <summary>How far guns actually reach, read from content.</summary>
        private static float GunRange => TestContent.Unit("artillery").Get(UnitAttributes.Range);

        [Fact]
        public void ArtilleryReachesAnythingItsArmyCanFind()
        {
            var field = new Battlefield("plains", 5200);

            UnitInstance guns = field.Add(0, "artillery", field.Centre, Facing.East);
            UnitInstance archers = field.Add(1, "archers", field.Centre + new Vec2(GunRange * 0.9f, 0f), Facing.West);

            // The same guns, the same range — and a scout pushed forward far
            // enough to lay eyes on the target. That is the whole difference,
            // and it is what turns reach into something you have to work for.
            UnitInstance scouts = field.Add(0, "scouts", field.Centre + new Vec2(GunRange * 0.45f, 0f), Facing.East);

            Battlefield.Hold(guns);
            Battlefield.Hold(archers);
            Battlefield.Hold(scouts);

            field.RunTurns(4);

            Assert.True(Battlefield.LostPercent(archers) > 0f,
                "With a spotter forward, guns should be able to work on archers who cannot reach back.");

            Assert.Equal(0, guns.Casualties);
        }

        [Fact]
        public void GunsReachFurtherThanAnyPairOfEyesOnTheField()
        {
            float gunRange = TestContent.Unit("artillery").Get(UnitAttributes.Range);
            float bestSight = 0f;

            foreach (UnitDef unit in TestContent.Units.All)
                bestSight = MathF.Max(bestSight, unit.Get(UnitAttributes.Vision));

            Assert.True(gunRange > bestSight,
                $"Artillery must outrange sight, or spotting for it is pointless: guns reach {gunRange:0} m, " +
                $"the sharpest eyes on the field see {bestSight:0} m.");
        }

        [Fact]
        public void GunsFireFarLessOftenThanBows()
        {
            int bowReload = TestContent.Unit("archers").Get(UnitAttributes.ReloadTicks);
            int gunReload = TestContent.Unit("artillery").Get(UnitAttributes.ReloadTicks);

            Assert.True(gunReload >= 2 * bowReload,
                $"Serving a gun should be far slower than drawing a bow: bows {bowReload} ticks, guns {gunReload}.");
        }

        [Fact]
        public void AGunCrewLandsFarMorePerShotThanAnArcherDoes()
        {
            float perGunner = TestContent.Unit("artillery").Get(UnitAttributes.RangedAttack);
            float perArcher = TestContent.Unit("archers").Get(UnitAttributes.RangedAttack);

            Assert.True(perGunner >= 3f * perArcher,
                $"A battery is few men landing heavy blows: {perGunner} per gunner against {perArcher} per archer.");
        }

        // ---- Shared scaffolding ---------------------------------------------

        private static DuelResult Approach(string attacker, string ground) =>
            new Duel
            {
                Attacker = attacker,
                Defender = "archers",
                StartDistance = 250f,
                Ground = ground,
                Seed = 2000,
            }.Fight();
    }
}
