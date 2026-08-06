using BattleChess.Contracts;
using BattleChess.Rules;
using Xunit;

namespace BattleChess.Tests.Battle
{
    /// <summary>
    /// Formations: what standing differently actually buys, and what it costs.
    /// </summary>
    /// <remarks>
    /// The point of the whole system is that no order is simply better. A
    /// square refuses cavalry the line and is the easiest mark on the field for
    /// archers; loose order is the answer to being shot at and is ridden
    /// straight through. Every test here is one half of a trade.
    /// </remarks>
    public sealed class FormationTests
    {
        // ---- Refusing cavalry the line ---------------------------------------

        [Fact]
        public void HorseRidesThroughALine()
        {
            Assert.True(RodeThrough("line"),
                "Cavalry should ride straight through infantry standing in ordinary line.");
        }

        [Fact]
        public void HorseRidesThroughLooseOrder()
        {
            Assert.True(RodeThrough("loose"),
                "Open ranks are no obstacle at all — that is the price of them.");
        }

        [Fact]
        public void ASquareRefusesThemThePassage()
        {
            Assert.False(RodeThrough("square"),
                "A square is what stops cavalry getting past. If horse rides through one, it has no purpose.");
        }

        /// <summary>
        /// Sends cavalry on a march <i>past</i> a body of infantry — not at
        /// them — and reports whether it got by.
        /// </summary>
        private static bool RodeThrough(string formation)
        {
            var field = new Battlefield("plains", 9000);

            UnitInstance infantry = field.Add(1, "swordsmen", field.Centre, Facing.West, formation: formation);
            UnitInstance horse = field.Add(0, "cavalry", field.Centre - new Vec2(150f, 0f), Facing.East);

            Battlefield.Hold(infantry);
            field.March(horse, field.Centre + new Vec2(200f, 0f));

            field.RunTurns(4);

            return horse.Position.X > field.Centre.X;
        }

        [Fact]
        public void ASquareStopsCavalryWithoutEverBeatingIt()
        {
            DuelResult fight = new Duel { Attacker = "cavalry", Defender = "swordsmen", DefenderFormation = "square" }.Fight();

            Assert.True(fight.AttackerWon,
                $"A square repels cavalry rather than beating it — horse cannot break in, and cannot easily " +
                $"be reached either. Forced to fight one, the horsemen should still win. {fight}");
        }

        // ---- What each order costs -------------------------------------------

        [Fact]
        public void LooseOrderTradesTheLineForCoverFromArrows()
        {
            FormationDef loose = TestContent.Formation("loose");
            FormationDef line = TestContent.Formation("line");

            Assert.True(loose.RangedVulnerability < line.RangedVulnerability,
                "Loose order must be harder to shoot at than line, or nobody would ever adopt it.");

            Assert.True(loose.StoppingMultiplier < line.StoppingMultiplier,
                "And it must hold ground worse, or it would simply be better.");
        }

        [Fact]
        public void ASquareTradesBeingShotAtForRefusingTheLine()
        {
            FormationDef square = TestContent.Formation("square");
            FormationDef line = TestContent.Formation("line");

            Assert.True(square.StoppingMultiplier > line.StoppingMultiplier,
                "A square must hold ground better than a line.");

            Assert.True(square.RangedVulnerability > line.RangedVulnerability,
                "And must be an easier mark, or it would simply be better.");
        }

        [Fact]
        public void EveryOrderExceptTheNaturalOneCostsSomethingToAdopt()
        {
            foreach (FormationDef formation in TestContent.Formations.All)
            {
                if (formation.Key == TestContent.Formations.Default.Key) continue;

                Assert.True(formation.OrganizationCost > 0f,
                    $"Reshaping into {formation.DisplayName} is free — changing formation has to have a price, " +
                    "or a player simply adopts the perfect order for every moment.");
            }
        }

        [Fact]
        public void ReshapingSpendsTheOrganizationItSaysItDoes()
        {
            var field = new Battlefield("plains", 9100, RuleSet.MeleeOnly);
            UnitInstance unit = field.Add(0, "swordsmen", field.Centre, Facing.East);

            FormationDef square = TestContent.Formation("square");
            float spent = unit.AdoptFormation(square);

            Assert.Equal(square.OrganizationCost, spent, 4);
            Assert.Equal(1f - square.OrganizationCost, unit.Organization, 4);
        }

        [Fact]
        public void AColumnIsNarrowerThanTheLineItCameFrom()
        {
            UnitDef swordsmen = TestContent.Unit("swordsmen");
            int strength = swordsmen.DefaultStrength;

            float lineWidth = TestContent.Formation("line").ApplyTo(swordsmen.NaturalFormation).FootprintFor(strength).Width;
            float columnWidth = TestContent.Formation("column").ApplyTo(swordsmen.NaturalFormation).FootprintFor(strength).Width;

            Assert.True(columnWidth < lineWidth,
                $"A column should present far less frontage: line {lineWidth:0} m, column {columnWidth:0} m.");
        }

        // ---- Cohesion is what a formation is actually made of ----------------

        [Fact]
        public void AFormationIsWorthNothingOnceItHasComeApart()
        {
            var field = new Battlefield("plains", 9200, RuleSet.MeleeOnly);

            UnitInstance square = field.Add(0, "swordsmen", field.Centre, Facing.East, formation: "square");
            UnitInstance loose = field.Add(0, "swordsmen", field.Centre + new Vec2(300f, 0f), Facing.East, formation: "loose");

            Assert.True(square.EffectiveStoppingPower > loose.EffectiveStoppingPower,
                "Fresh, a square must hold ground far better than open order.");

            square.Organization = 0f;
            loose.Organization = 0f;

            Assert.Equal(loose.EffectiveStoppingPower, square.EffectiveStoppingPower, 3);
        }

        [Fact]
        public void ADisorderedSquareStopsLettingCavalryBeRefused()
        {
            var field = new Battlefield("plains", 9300, RuleSet.MeleeOnly);

            UnitInstance square = field.Add(0, "swordsmen", field.Centre, Facing.East, formation: "square");
            UnitInstance horse = field.Add(1, "cavalry", field.Centre + new Vec2(300f, 0f), Facing.West);

            Assert.True(square.EffectiveStoppingPower > horse.EffectiveBreakthrough,
                "A square holding together must refuse cavalry the line.");

            // You can absolutely have a very loose square, and it will not save you.
            square.Organization = 0.3f;

            Assert.True(square.EffectiveStoppingPower < horse.EffectiveBreakthrough,
                $"A square whose ranks have come apart should be ridden through: " +
                $"stopping {square.EffectiveStoppingPower:0.00} against breakthrough {horse.EffectiveBreakthrough:0.00}.");
        }

        [Fact]
        public void SpearmenRefuseCavalryTheLineWithoutNeedingASquare()
        {
            var field = new Battlefield("plains", 9400, RuleSet.MeleeOnly);

            UnitInstance spearmen = field.Add(0, "spearmen", field.Centre, Facing.East);
            UnitInstance horse = field.Add(1, "cavalry", field.Centre + new Vec2(300f, 0f), Facing.West);

            Assert.True(spearmen.EffectiveStoppingPower > horse.EffectiveBreakthrough,
                $"Spearmen in ordinary line must stop horse dead — that counter is expressed as movement, " +
                $"not as a combat special case: stopping {spearmen.EffectiveStoppingPower:0.00} " +
                $"against breakthrough {horse.EffectiveBreakthrough:0.00}.");
        }
    }
}
