using System;
using BattleChess.Contracts;
using BattleChess.Rules;
using Xunit;
using Xunit.Abstractions;

namespace BattleChess.Tests.Battle
{
    /// <summary>
    /// An enemy is a wall to everybody except the regiment sent to break it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Mx2d and M15.</b> Until now only friends were ever planning
    /// obstacles, so a regiment told to march somewhere drew its line straight
    /// through an enemy line and walked into it. The player saw regiments
    /// refusing to go round something plainly in the way.
    /// </para>
    /// <para>
    /// The exemption is what makes the rule safe to turn on, and it has two
    /// halves. <b>The quarry:</b> a regiment told to attack somebody must not
    /// route politely round the regiment it was sent to break - the fault that
    /// broke five tests the first time this was tried. <b>The stance:</b>
    /// Advance is defined in the player's own words as forcing through an enemy
    /// line, so routing it round contradicts the order it was given.
    /// </para>
    /// <para>
    /// Deliberately the quarry alone and not every enemy on the field while an
    /// attack order stands: a regiment sent at the enemy left still wants to
    /// walk round the enemy centre on the way, which is what Mx2d asks for in
    /// as many words.
    /// </para>
    /// </remarks>
    public sealed class EnemiesAreObstaclesTests
    {
        private readonly ITestOutputHelper _out;

        public EnemiesAreObstaclesTests(ITestOutputHelper output) => _out = output;

        /// <summary>How far off the straight line a route has to bend to count as going round.</summary>
        private const float WentRoundMetres = 12f;

        /// <summary>
        /// Builds a regiment 220 m short of an enemy standing on the line to a
        /// destination 220 m beyond him, and returns how far the planned route
        /// leaves that line.
        /// </summary>
        private float HowFarItBends(Stance stance, bool attackHim, out bool found)
        {
            var field = new Battlefield("plains", 4711);

            UnitInstance enemy = field.Add(1, "spearmen", field.Centre, Facing.West);
            Battlefield.Hold(enemy);

            UnitInstance foot = field.Add(0, "swordsmen", field.Centre - new Vec2(220f, 0f), Facing.East);
            foot.Stance = stance;

            Vec2 goal = field.Centre + new Vec2(220f, 0f);

            foot.GiveOrder(
                attackHim ? UnitOrder.Attack(enemy.Id) : UnitOrder.MoveTo(goal),
                foot.Position);

            Plan plan = Marching.PlanTo(field.State, foot, field.Pathfinder, goal, null);

            found = plan.Path.Found;
            if (!found) return 0f;

            float worst = 0f;

            foreach (Vec2 at in plan.Path.Waypoints)
            {
                float off = MathF.Abs(at.Y - field.Centre.Y);
                if (off > worst) worst = off;
            }

            _out.WriteLine(
                $"{stance}{(attackHim ? ", attacking him" : string.Empty)}: " +
                $"{plan.Path.Waypoints.Count} waypoints, bends {worst:0} m off the line");

            return worst;
        }

        [Fact]
        public void ARegimentOnDefendPlansRoundAnEnemyOnItsLine()
        {
            float bend = HowFarItBends(Stance.Defend, attackHim: false, out bool found);

            Assert.True(found, "It should still find a way there - going round is a route, not a refusal.");

            Assert.True(bend > WentRoundMetres,
                $"On Defend the spearmen are a wall, so the route must leave the straight line to get past " +
                $"them. It bends only {bend:0} m, which is the old behaviour of walking straight in.");
        }

        [Fact]
        public void ARegimentOnAdvancePlansStraightThroughHim()
        {
            float bend = HowFarItBends(Stance.Advance, attackHim: false, out bool found);

            Assert.True(found);

            Assert.True(bend <= WentRoundMetres,
                $"Advance is 'carry out the order, forcing through an enemy line where the unit is able to'. " +
                $"Routing it {bend:0} m round the enemy is the opposite of the stance the player chose.");
        }

        [Fact]
        public void ARegimentSentToBreakHimDoesNotWalkRoundHim()
        {
            float bend = HowFarItBends(Stance.Defend, attackHim: true, out bool found);

            Assert.True(found);

            Assert.True(bend <= WentRoundMetres,
                $"It was sent to break these spearmen and it has planned {bend:0} m round them. This is the " +
                "fault that broke five tests the first time enemies were made obstacles: a charge that " +
                "arrives by walking politely past the regiment it was aimed at.");
        }

        /// <summary>
        /// And the exemption stops at the quarry: a third party is still a wall
        /// while an attack order stands.
        /// </summary>
        [Fact]
        public void AnAttackOrderDoesNotExemptTheWholeEnemyArmy()
        {
            var field = new Battlefield("plains", 4712);

            UnitInstance inTheWay = field.Add(1, "spearmen", field.Centre, Facing.West);
            Battlefield.Hold(inTheWay);

            UnitInstance quarry = field.Add(1, "archers", field.Centre + new Vec2(420f, 0f), Facing.West);
            Battlefield.Hold(quarry);

            UnitInstance foot = field.Add(0, "swordsmen", field.Centre - new Vec2(220f, 0f), Facing.East);
            foot.Stance = Stance.Defend;
            foot.GiveOrder(UnitOrder.Attack(quarry.Id), foot.Position);

            // Short of the archers rather than on top of them. Aimed at their
            // centre, every candidate route fails at the destination because
            // nothing can stand there, and the planner falls to a press - which
            // measures the fixture, not the rule.
            Vec2 aim = quarry.Position - new Vec2(120f, 0f);

            Plan plan = Marching.PlanTo(field.State, foot, field.Pathfinder, aim, null);

            Assert.True(plan.Path.Found);

            float worst = 0f;
            foreach (Vec2 at in plan.Path.Waypoints)
            {
                float off = MathF.Abs(at.Y - field.Centre.Y);
                if (off > worst) worst = off;
            }

            _out.WriteLine($"attacking the archers past the spearmen: bends {worst:0} m");

            Assert.True(worst > WentRoundMetres,
                $"Sent at the archers, it must still go round the spearmen standing between - it bends only " +
                $"{worst:0} m. Exempting the whole enemy army would make 'attack' mean 'walk through " +
                "anything', which is the rule this replaces rather than a version of it.");
        }
    }
}
