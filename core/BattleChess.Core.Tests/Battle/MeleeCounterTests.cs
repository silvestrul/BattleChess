using BattleChess.Contracts;
using Xunit;

namespace BattleChess.Tests.Battle
{
    /// <summary>
    /// The counters: which unit beats which, and by roughly how much.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every fight here is nose to nose on open plains with both sides
    /// committed, which is the fairest possible test of a matchup — no terrain,
    /// no flank, no shooting first, nothing but the two regiments. If a counter
    /// does not hold under those conditions it does not hold at all.
    /// </para>
    /// <para>
    /// Thresholds are bands, not targets. They are set well clear of the
    /// current numbers so that ordinary tuning passes through untouched, and
    /// tight enough that losing a counter altogether fails loudly.
    /// </para>
    /// </remarks>
    public sealed class MeleeCounterTests
    {
        // ---- Spear beats horse: the counter the whole design rests on --------

        [Fact]
        public void SpearmenBreakCavalryHeadOn()
        {
            DuelResult fight = new Duel { Attacker = "cavalry", Defender = "spearmen" }.Fight();

            Assert.True(fight.DefenderWon, $"Spearmen must beat cavalry head-on. {fight}");
            Assert.True(fight.AttackerLost >= 25f, $"Cavalry should be badly mauled charging spears. {fight}");
            Assert.True(fight.DefenderLost <= 20f, $"Spearmen should come through a frontal charge cheaply. {fight}");
        }

        [Fact]
        public void CavalryLosesFarMoreThanTheSpearmenDo()
        {
            DuelResult fight = new Duel { Attacker = "cavalry", Defender = "spearmen" }.Fight();

            Assert.True(fight.AttackerLost >= 2f * fight.DefenderLost,
                $"Charging a spear wall should be lopsided, not merely a loss. {fight}");
        }

        // ---- Horse beats everything that cannot brace ------------------------

        [Fact]
        public void CavalryBeatsSwordsmen()
        {
            DuelResult fight = new Duel { Attacker = "cavalry", Defender = "swordsmen" }.Fight();

            Assert.True(fight.AttackerWon, $"Cavalry must beat swordsmen one to one. {fight}");
            Assert.True(fight.DefenderLost >= 50f, $"Swordsmen caught by horse should be wrecked. {fight}");
        }

        [Fact]
        public void CavalryRidesDownArchers()
        {
            DuelResult fight = new Duel { Attacker = "cavalry", Defender = "archers" }.Fight();

            Assert.True(fight.AttackerWon, $"Archers caught in the open must lose to horse. {fight}");
            Assert.True(fight.DefenderLost >= 60f, $"Cavalry reaching archers should destroy them. {fight}");
            Assert.True(fight.AttackerLost <= 20f, $"Riding down archers should be cheap. {fight}");
        }

        [Fact]
        public void CavalryOverrunsArtillery()
        {
            DuelResult fight = new Duel { Attacker = "cavalry", Defender = "artillery" }.Fight();

            Assert.True(fight.AttackerWon, $"Guns reached by horse must be lost. {fight}");
            Assert.True(fight.DefenderLost >= 80f, $"An overrun battery should not survive. {fight}");
            Assert.True(fight.AttackerLost <= 20f, $"Taking a battery should cost the horsemen little. {fight}");
        }

        // ---- Sword beats spear once it is inside the points ------------------

        [Fact]
        public void SwordsmenBeatSpearmenAtCloseQuarters()
        {
            DuelResult fight = new Duel { Attacker = "swordsmen", Defender = "spearmen" }.Fight();

            Assert.True(fight.AttackerWon,
                $"A pike block is unwieldy once anything is inside the points — swordsmen should win. {fight}");
        }

        // ---- Shooters and scouts are helpless in a melee ---------------------

        [Theory]
        [InlineData("spearmen")]
        [InlineData("swordsmen")]
        [InlineData("cavalry")]
        [InlineData("scouts")]
        public void AnythingThatReachesArchersBeatsThem(string attacker)
        {
            DuelResult fight = new Duel { Attacker = attacker, Defender = "archers" }.Fight();

            Assert.True(fight.AttackerWon,
                $"An archer carries a knife and no shield — anything that closes with them must win. " +
                $"Bowmen were beating light horse and gun crews hand to hand, which made reaching them " +
                $"an optional plan rather than the answer to them. {fight}");

            Assert.True(fight.DefenderLost >= 2f * fight.AttackerLost,
                $"And should lose at least twice what the attacker does. {fight}");
        }

        [Theory]
        [InlineData("spearmen")]
        [InlineData("swordsmen")]
        public void ArtilleryIsHelplessInMelee(string infantry)
        {
            DuelResult fight = new Duel { Attacker = infantry, Defender = "artillery" }.Fight();

            Assert.True(fight.AttackerWon, $"Guns must lose a melee against {infantry}. {fight}");
            Assert.True(fight.AttackerLost <= 10f, $"Taking a battery in melee should be nearly free. {fight}");
        }

        [Theory]
        [InlineData("spearmen")]
        [InlineData("swordsmen")]
        public void ScoutsCannotFightTheBattleLine(string infantry)
        {
            DuelResult fight = new Duel { Attacker = "scouts", Defender = infantry }.Fight();

            Assert.True(fight.DefenderWon, $"Scouts must lose to {infantry}. {fight}");
            Assert.True(fight.AttackerLost >= 15f, $"Scouts picking a fight should pay for it. {fight}");
            Assert.True(fight.DefenderLost <= 10f, $"Scouts should barely scratch a line regiment. {fight}");
        }

        // ---- No unit answers everything -------------------------------------

        [Fact]
        public void NothingWinsEveryMatchup()
        {
            foreach (UnitDef unit in TestContent.Units.All)
            {
                int fought = 0;
                int won = 0;

                foreach (UnitDef other in TestContent.Units.All)
                {
                    if (other.Key == unit.Key) continue;

                    DuelResult fight = new Duel { Attacker = unit.Key, Defender = other.Key }.Fight();

                    fought++;
                    if (fight.AttackerWon) won++;
                }

                Assert.True(won < fought,
                    $"{unit.DisplayName} wins all {fought} of its melees — nothing on the field answers it.");
            }
        }

        [Theory]
        [InlineData("spearmen")]
        [InlineData("swordsmen")]
        [InlineData("cavalry")]
        public void EveryLineUnitBeatsSomething(string key)
        {
            bool wonSomething = false;

            foreach (UnitDef other in TestContent.Units.All)
            {
                if (other.Key == key) continue;

                if (new Duel { Attacker = key, Defender = other.Key }.Fight().AttackerWon)
                {
                    wonSomething = true;
                    break;
                }
            }

            Assert.True(wonSomething, $"{key} loses every melee it fights — nobody would ever field it.");
        }

        // ---- The triangle actually closes ------------------------------------

        [Fact]
        public void SpearSwordHorseFormACircle()
        {
            Assert.True(new Duel { Attacker = "spearmen", Defender = "cavalry" }.Fight().AttackerWon,
                "Spear should beat horse.");

            Assert.True(new Duel { Attacker = "cavalry", Defender = "swordsmen" }.Fight().AttackerWon,
                "Horse should beat sword.");

            Assert.True(new Duel { Attacker = "swordsmen", Defender = "spearmen" }.Fight().AttackerWon,
                "Sword should beat spear.");
        }
    }
}
