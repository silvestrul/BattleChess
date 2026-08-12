using BattleChess.Contracts;
using BattleChess.Rules;
using Xunit;

namespace BattleChess.Tests.Battle
{
    /// <summary>
    /// Formations come apart, and formations put themselves back together.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Organization was a one-way ratchet for most of this project's life. Half
    /// a dozen rules drained it — rough ground, riding through a line, taking a
    /// charge, being surrounded, shouldering past friends — and nothing put any
    /// of it back. Every battle therefore ground down to a field of rabble, and
    /// the longer a regiment had been on the field the worse it fought for
    /// reasons nothing on screen explained.
    /// </para>
    /// <para>
    /// It hid well, too. A controlled fight starts both sides at full cohesion
    /// and settles before the drains matter, so the balance sweep saw nothing
    /// wrong at all. It only showed up in play, as regiments losing matchups
    /// they are supposed to win.
    /// </para>
    /// </remarks>
    public sealed class CohesionTests
    {
        [Fact]
        public void ARegimentLeftAloneClosesItsRanksUpAgain()
        {
            var field = new Battlefield("plains", 17000);

            UnitInstance unit = field.Add(0, "cavalry", field.Centre, Facing.East);
            Battlefield.Hold(unit);

            unit.Organization = 0.4f;

            field.RunTurns(3);

            Assert.True(unit.Organization > 0.4f,
                "Men dress their ranks whenever nobody is killing them. Cohesion that only ever falls " +
                "means every battle ends in a field of rabble.");
        }

        [Fact]
        public void ReformingIsSlowerThanBeingBroken()
        {
            var field = new Battlefield("plains", 17100);

            UnitInstance unit = field.Add(0, "cavalry", field.Centre, Facing.East);
            Battlefield.Hold(unit);

            unit.Organization = 0.2f;

            field.RunTurns(1);

            Assert.True(unit.Organization < 0.5f,
                $"A turn should not undo a battle's worth of disorder — it reached " +
                $"{unit.Organization:0.00}. Marching through your own line has to keep costing something.");
        }

        [Fact]
        public void MenReformFasterStandingStillThanOnTheMarch()
        {
            Assert.True(Regained(marching: false) > Regained(marching: true),
                "Ranks can be dressed on the move, but not well. Halting has to be worth something " +
                "beyond waiting.");
        }

        private static float Regained(bool marching)
        {
            var field = new Battlefield("plains", 17200);

            UnitInstance unit = field.Add(0, "swordsmen", field.Centre - new Vec2(300f, 0f), Facing.East);
            unit.Organization = 0.3f;

            if (marching) field.March(unit, field.Centre + new Vec2(200f, 0f));
            else Battlefield.Hold(unit);

            field.RunTurns(2);

            return unit.Organization - 0.3f;
        }

        [Fact]
        public void ARegimentInAMeleeCannotReform()
        {
            var field = new Battlefield("plains", 17300);

            UnitInstance ours = field.Add(0, "swordsmen", field.Centre, Facing.East);
            UnitInstance theirs = field.Add(1, "swordsmen", field.Centre + new Vec2(60f, 0f), Facing.West);

            Battlefield.Press(ours, theirs);
            Battlefield.Press(theirs, ours);

            field.RunTurns(1);
            ours.Organization = 0.5f;
            float before = ours.Organization;

            field.RunTurns(2);

            Assert.True(ours.Organization <= before,
                $"Nobody dresses ranks with an enemy among them. It went from {before:0.00} to " +
                $"{ours.Organization:0.00}.");
        }

        // ---- The bug this was found through -----------------------------------

        [Fact]
        public void CavalryStillBeatsSwordsmenAfterALongApproach()
        {
            // Cavalry that has spent three turns manoeuvring should arrive tired,
            // not ruined. Before regiments could re-form, an approach like this
            // cost roughly two thirds of a unit's cohesion — and cohesion
            // multiplies attack, defence, stopping power and breakthrough at
            // once, so the counter simply inverted.
            var field = new Battlefield("plains", 17400);

            UnitInstance foot = field.Add(1, "swordsmen", field.Centre + new Vec2(300f, 0f), Facing.West);
            Battlefield.Hold(foot);

            UnitInstance horse = field.Add(0, "cavalry", field.Centre - new Vec2(300f, 260f), Facing.East);

            // A friendly regiment drawn up across the ground it has to cross.
            // Regiments cannot walk through their own any more, so the horse
            // brushes down its flank and round the end of the line — which is
            // the manoeuvre that costs the cohesion this test is about.
            UnitInstance friend = field.Add(0, "spearmen", field.Centre - new Vec2(150f, 120f), Facing.East);
            Battlefield.Hold(friend);

            field.March(horse, field.Centre);
            field.RunTurns(3);

            float onArrival = horse.Organization;

            Battlefield.Press(horse, foot);
            field.RunUntilDecided(12, horse, foot);

            Assert.True(onArrival > 0.5f,
                $"Three turns of manoeuvring past a friendly regiment left it at {onArrival:0.00} " +
                "cohesion. Arriving tired is right; arriving wrecked is not.");

            Assert.True(Battlefield.LostPercent(foot) > Battlefield.LostPercent(horse),
                $"And it must still win the fight it is built to win: horse lost " +
                $"{Battlefield.LostPercent(horse):0}%, foot {Battlefield.LostPercent(foot):0}%.");
        }
    }
}
