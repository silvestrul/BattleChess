using BattleChess.Contracts;
using BattleChess.Rules;
using Xunit;

namespace BattleChess.Tests.Battle
{
    /// <summary>
    /// How far cohesion can fall, and what puts it back.
    /// </summary>
    /// <remarks>
    /// Cohesion multiplies attack, defence, stopping power and breakthrough at
    /// once, so it is the single stat most able to decide a battle quietly.
    /// That argues for bounding it: a regiment still standing and still willing
    /// to fight should never be so disordered that it cannot do either. Men who
    /// have genuinely lost all formation are routing, and routing is its own
    /// rule.
    /// </remarks>
    public sealed class CohesionFloorTests
    {
        [Fact]
        public void CohesionCannotBeDrivenBelowTheFloor()
        {
            var field = new Battlefield("plains", 21000, RuleSet.MeleeOnly);

            UnitInstance unit = field.Add(0, "spearmen", field.Centre, Facing.East);

            unit.Organization = -5f;

            Assert.Equal(UnitInstance.MinimumOrganization, unit.Organization, 3);
        }

        [Fact]
        public void ARegimentGroundDownAllBattleIsStillWorthSomething()
        {
            var field = new Battlefield("plains", 21100, RuleSet.MeleeOnly);

            UnitInstance ours = field.Add(0, "spearmen", field.Centre, Facing.East);
            UnitInstance theirs = field.Add(1, "cavalry", field.Centre + new Vec2(30f, 0f), Facing.West);

            Battlefield.Press(ours, theirs);
            Battlefield.Press(theirs, ours);

            field.RunTurns(12);

            Assert.True(ours.Organization >= UnitInstance.MinimumOrganization,
                "Every long battle used to end between two rabbles rather than two armies.");

            Assert.True(ours.EffectiveStoppingPower > 0.5f,
                $"And a spear wall must still be a spear wall at the end of it — stopping power " +
                $"{ours.EffectiveStoppingPower:0.00}.");
        }

        // ---- Standing still re-forms you anywhere ------------------------------

        [Theory]
        [InlineData("swamp")]
        [InlineData("forest")]
        [InlineData("river")]
        public void HaltingOnBadGroundStillLetsARegimentDressItsRanks(string ground)
        {
            var field = new Battlefield(ground, 21200, RuleSet.Full);

            UnitInstance unit = field.Add(0, "spearmen", field.Centre, Facing.East);
            Battlefield.Hold(unit);

            unit.Organization = 0.5f;

            field.RunTurns(4);

            Assert.True(unit.Organization > 0.5f,
                $"Halting in a {ground} to close the files up is a real thing to do with a turn. " +
                $"Blocking it left a regiment that had stopped in bad country a rabble for good, which " +
                $"reads as being punished twice for terrain it already paid to cross.");
        }

        [Fact]
        public void ButYouStillCannotWadeARiverAndCloseYourFilesAtTheSameTime()
        {
            var field = new Battlefield("river", 21300, RuleSet.Full);

            UnitInstance unit = field.Add(0, "spearmen", field.Centre - new Vec2(200f, 0f), Facing.East);
            unit.Organization = 0.9f;

            field.March(unit, field.Centre + new Vec2(150f, 0f));
            field.RunTurns(3);

            Assert.True(unit.Organization < 0.9f,
                $"Crossing has to cost order while it is being crossed, or terrain stops meaning " +
                $"anything — organization {unit.Organization:0.00}.");
        }
    }
}
