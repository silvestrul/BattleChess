using BattleChess.Contracts;
using BattleChess.Rules;
using Xunit;

namespace BattleChess.Tests.Battle
{
    /// <summary>
    /// Shooting has to stop eventually.
    /// </summary>
    /// <remarks>
    /// A battery firing without pause for thirty-two turns took three quarters
    /// of a regiment off the field a handful of men at a time, and no amount of
    /// tuning the damage answers that — the fault is the number of volleys, not
    /// the size of them. A quiver turns "shoot at whatever is nearest" into
    /// "when is this worth spending".
    /// </remarks>
    public sealed class AmmunitionTests
    {
        [Fact]
        public void ArchersRunOutOfArrows()
        {
            (int shotsFired, float lost, bool empty) = ShootUntilDry("archers");

            Assert.True(empty, "A regiment of bowmen must eventually shoot itself empty.");

            Assert.True(shotsFired >= 15 && shotsFired <= 60,
                $"And should get a real engagement's worth of volleys out first — fired {shotsFired}.");

            Assert.True(lost > 0f, "The volleys it did fire should have told for something.");
        }

        [Fact]
        public void HorseArchersCarryFarMoreThanFootArchers()
        {
            int foot = TestContent.Unit("archers").Get(UnitAttributes.Ammunition);
            int mounted = TestContent.Unit("horsearchers").Get(UnitAttributes.Ammunition);

            Assert.True(mounted >= 2 * foot,
                $"Carrying the quiver is the horse's problem, so mounted archers should stay in the fight " +
                $"long after the foot have shot themselves empty: {mounted} against {foot}.");
        }

        [Fact]
        public void AnEmptyRegimentStopsShootingAltogether()
        {
            var field = new Battlefield("plains", 4100);

            UnitInstance bows = field.Add(0, "archers", field.Centre, Facing.East);
            UnitInstance target = field.Add(1, "swordsmen", field.Centre + new Vec2(100f, 0f), Facing.West);

            Battlefield.Hold(bows);
            Battlefield.Hold(target);

            bows.ShotsLeft = 0;
            field.RunTurns(4);

            Assert.Equal(0, target.Casualties);
        }

        [Fact]
        public void GunsFireHarderAndFarLessOftenThanBows()
        {
            UnitDef guns = TestContent.Unit("artillery");
            UnitDef bows = TestContent.Unit("archers");

            Assert.True(guns.Get(UnitAttributes.RangedAttack) >= 4f * bows.Get(UnitAttributes.RangedAttack),
                "A gun's shot must land like nothing else on the field.");

            Assert.True(guns.Get(UnitAttributes.ReloadTicks) >= 2 * bows.Get(UnitAttributes.ReloadTicks),
                "And serving it must be far slower than drawing a bow.");
        }

        /// <summary>
        /// Stands a shooter in front of a target and counts the volleys until
        /// the quiver is empty.
        /// </summary>
        private static (int Shots, float Lost, bool Empty) ShootUntilDry(string shooter)
        {
            var field = new Battlefield("plains", 4000);

            UnitInstance bows = field.Add(0, shooter, field.Centre, Facing.East);
            UnitInstance target = field.Add(1, "swordsmen", field.Centre + new Vec2(100f, 0f), Facing.West);

            Battlefield.Hold(bows);
            Battlefield.Hold(target);

            int started = bows.ShotsLeft;
            field.RunTurns(30);

            int fired = (started - bows.ShotsLeft) / bows.InitialStrength;

            return (fired, Battlefield.LostPercent(target), bows.ShotsLeft == 0);
        }
    }
}
