using System;
using BattleChess.Contracts;
using BattleChess.Rules;
using Xunit;

namespace BattleChess.Tests.Battle
{
    /// <summary>
    /// Getting two regiments to actually fight each other.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The rules have three separate distances and they have to agree: melee
    /// reaches 8 m, a zone of control halts a march somewhere further out, and
    /// an attack order aims at a point it hopes is inside contact. Any gap
    /// between them is a dead band where regiments stand looking at each other
    /// and nothing happens — which is what a player sees as "I cannot make them
    /// fight", and is far more damaging than any balance problem.
    /// </para>
    /// <para>
    /// Every test here approaches from a different angle on purpose. Head-on is
    /// the case that works by accident; a flank approach is the one that
    /// exposed a stand-off computed from half-depths.
    /// </para>
    /// </remarks>
    public sealed class ClosingTests
    {
        // ---- An attack order must end in a fight, from any angle ---------------

        [Theory]
        [InlineData(0f, "head on")]
        [InlineData(45f, "obliquely")]
        [InlineData(90f, "on the flank")]
        [InlineData(135f, "on the rear quarter")]
        [InlineData(180f, "from behind")]
        public void OrderingAnAttackEndsInAFightFromAnyAngle(float bearingDegrees, string how)
        {
            var field = new Battlefield("plains", 15000);

            UnitInstance defender = field.Add(1, "cavalry", field.Centre, Facing.East);
            Battlefield.Hold(defender);

            // Placed 220 m out on the given bearing, well beyond any zone of
            // control, and told to attack.
            Vec2 offset = Facing.FromDegrees(bearingDegrees).ToVector() * 220f;

            UnitInstance attacker = field.Add(0, "cavalry", field.Centre + offset, Facing.West);
            Battlefield.Press(attacker, defender);

            // Caught the moment it arrives rather than measured at the end.
            // One of the two regiments is usually running by then, and a test
            // that reads the gap after a rout is measuring the flight, not the
            // approach.
            float closest = float.MaxValue;

            field.RunUntil(() =>
            {
                closest = MathF.Min(closest, OrientedRect.GapBetween(attacker.Shape, defender.Shape));
                return closest <= OrderSystem.ContactMetres;
            }, maxTurns: 8);

            Assert.True(closest <= OrderSystem.ContactMetres,
                $"Told to attack {how}, it should have closed: it got no nearer than {closest:0} m, and " +
                $"men can only reach each other within {OrderSystem.ContactMetres:0} m.");

            // Arriving is not the same as fighting, and the run above stops on
            // the tick of arrival. Give the pulse time to land.
            field.RunTurns(2);

            Assert.True(Battlefield.LostPercent(defender) > 0f,
                $"And closing must mean fighting. Attacking {how} cost the defender nothing at all.");
        }

        // ---- A regiment coming at a line from the side ------------------------

        [Fact]
        public void ClosingOnALineFromItsFlankDoesNotAimInsideIt()
        {
            var field = new Battlefield("plains", 15100);

            // A line facing east, so its hundred metres of frontage runs north
            // to south and its flank is the short end.
            UnitInstance line = field.Add(1, "cavalry", field.Centre, Facing.East);
            Battlefield.Hold(line);

            // Coming from due north — straight at the end of the line, where
            // the ground to cross is half the FRONTAGE rather than half the
            // depth. Aiming with half-depths put the goal fifty metres inside
            // the enemy formation, somewhere no unit can ever stand, so the
            // attacker marched at it forever and never arrived.
            UnitInstance attacker = field.Add(0, "cavalry", field.Centre + new Vec2(0f, 240f), Facing.South);
            Battlefield.Press(attacker, line);

            float closest = float.MaxValue;

            field.RunUntil(() =>
            {
                closest = MathF.Min(closest, OrientedRect.GapBetween(attacker.Shape, line.Shape));
                return closest <= OrderSystem.ContactMetres;
            }, maxTurns: 8);

            Assert.True(closest <= OrderSystem.ContactMetres,
                $"A flank attack has to cross half a frontage, not half a depth. It stalled {closest:0} m out.");
        }

        // ---- The dead band between halting and fighting ------------------------

        [Fact]
        public void AZoneOfControlHaltsCloseEnoughThatPressingTheAttackIsOneMoreOrder()
        {
            foreach (string key in new[] { "spearmen", "swordsmen", "cavalry", "archers", "artillery", "scouts", "horsearchers" })
            {
                float zone = TestContent.Unit(key).Get(UnitAttributes.ZoneOfControl);

                Assert.True(zone <= 3f * OrderSystem.ContactMetres,
                    $"{key} holds ground {zone:0} m beyond its own edge, against a melee reach of " +
                    $"{OrderSystem.ContactMetres:0} m. Anything wider leaves a band where a march stops " +
                    "and no fight ever starts — the player orders an advance and watches nothing happen.");
            }
        }

        [Fact]
        public void TwoLinesAdvancingOnEachOtherActuallyMeet()
        {
            var field = new Battlefield("plains", 15200);

            // Both in line abreast, facing each other, 300 m apart, ordered to
            // march onto the other's ground rather than to attack. Advance
            // stance means fight whatever blocks the way.
            UnitInstance ours = field.Add(0, "swordsmen", field.Centre - new Vec2(150f, 0f), Facing.East);
            UnitInstance theirs = field.Add(1, "swordsmen", field.Centre + new Vec2(150f, 0f), Facing.West);

            field.March(ours, theirs.Position, Stance.Advance);
            Battlefield.Hold(theirs);

            field.RunTurns(8);

            Assert.True(field.TimesSaid("exchange") > 0,
                "A line ordered forward onto an enemy line must end up fighting it. Halting at the edge " +
                "of a zone of control and stopping there is the whole failure this guards.");
        }

        // ---- The edge of the world --------------------------------------------

        [Fact]
        public void ARegimentBackedAgainstImpassableGroundCanStillBeOrderedAbout()
        {
            // Mountains along the eastern edge, as on the real maps — every
            // battlefield in the game is ringed with ground nothing can cross.
            var field = new Battlefield("plains", 15300, RuleSet.Full, canvas =>
                canvas.Band(canvas.Columns - 3, canvas.Columns - 1, "mountain"));

            float edgeX = field.Map.Bounds.Max.X;

            // Pinned in the corner, right up against the impassable band.
            UnitInstance cornered = field.Add(0, "swordsmen", new Vec2(edgeX - 90f, field.Map.Bounds.Min.Y + 60f), Facing.East);

            Vec2 start = cornered.Position;

            field.March(cornered, field.Centre);
            field.RunTurns(4);

            Assert.True(Vec2.Distance(cornered.Position, start) > 50f,
                $"A regiment in the corner must still be able to march out of it. It moved " +
                $"{Vec2.Distance(cornered.Position, start):0} m.");
        }

        [Fact]
        public void ChasingAnEnemyPinnedAgainstTheEdgeStillFindsARoute()
        {
            var field = new Battlefield("plains", 15400, RuleSet.Full, canvas =>
                canvas.Band(canvas.Columns - 3, canvas.Columns - 1, "mountain"));

            float edgeX = field.Map.Bounds.Max.X;

            // The quarry stands with its back to the mountains, so the natural
            // aim point — beyond it, on the far side — is inside impassable
            // ground. Clamping to the map bounds landed there too, and the
            // pathfinder rightly refused a goal in a mountain range, which left
            // the pursuer standing still with no explanation on screen.
            UnitInstance quarry = field.Add(1, "archers", new Vec2(edgeX - 95f, field.Centre.Y), Facing.West);
            Battlefield.Hold(quarry);

            UnitInstance hunter = field.Add(0, "cavalry", new Vec2(edgeX - 400f, field.Centre.Y), Facing.East);
            Battlefield.Press(hunter, quarry);

            field.RunTurns(6);

            Assert.True(Battlefield.LostPercent(quarry) > 0f,
                "Cornered troops must still be reachable. Standing against impassable ground was total " +
                "immunity from pursuit.");
        }
    }
}
