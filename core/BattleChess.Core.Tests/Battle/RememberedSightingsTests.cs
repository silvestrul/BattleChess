using BattleChess.Contracts;
using BattleChess.Rules;
using Xunit;

namespace BattleChess.Tests.Battle
{
    /// <summary>
    /// What an army remembers of an enemy it can no longer see.
    /// </summary>
    /// <remarks>
    /// The difference between fog and amnesia. A regiment that walks out of
    /// sight used simply to cease to exist, which leaves nothing to plan
    /// against; a stale marker lets you act on what you knew and find out
    /// whether it was still true. It is worth exactly as much as its age.
    /// </remarks>
    public sealed class RememberedSightingsTests
    {
        [Fact]
        public void AGhostMarkerStaysWhereTheEnemyWasLastSeen()
        {
            var field = new Battlefield("plains", 4500);

            UnitInstance watcher = field.Add(0, "scouts", field.Centre, Facing.East);
            UnitInstance enemy = field.Add(1, "swordsmen", field.Centre + new Vec2(120f, 0f), Facing.West);

            Battlefield.Hold(watcher);
            Battlefield.Hold(enemy);
            field.RunTurns(1);

            Assert.True(field.State.Vision.CanSee(field.State, watcher.Owner, enemy),
                "The scouts should have them at 120 m to begin with.");

            // Marched clean out of sight. They stay tracked until they pass the
            // edge of the scouts' vision, so the marker lands there rather than
            // where they started — which is the point of it.
            field.March(enemy, field.Centre + new Vec2(900f, 0f), bearing: Facing.East);
            field.RunTurns(8);

            Assert.False(field.State.Vision.CanSee(field.State, watcher.Owner, enemy),
                "And should lose them once they are well away.");

            Assert.True(field.State.Vision.TryRecall(field.State, watcher.Owner, enemy, out Vec2 where, out int age),
                "But must still remember having seen them.");

            float sight = LineOfSight.SightRange(field.State, watcher);

            Assert.True(Vec2.Distance(where, watcher.Position) <= sight + 30f,
                $"The marker belongs at the edge of sight, where they were genuinely last made out — " +
                $"remembered {where}, watcher sees {sight:0} m.");

            Assert.True(Vec2.Distance(where, enemy.Position) > 300f,
                $"And emphatically not where they are now: remembered {where}, actually at {enemy.Position}. " +
                $"A marker that tracks a unit you cannot see is not fog, it is a map hack.");

            Assert.True(age > 0, "And it should be visibly stale.");
        }

        [Fact]
        public void AnEnemyNeverSeenIsNotRemembered()
        {
            var field = new Battlefield("plains", 4600);

            UnitInstance watcher = field.Add(0, "spearmen", field.Centre, Facing.East);
            UnitInstance stranger = field.Add(1, "swordsmen", field.Centre + new Vec2(900f, 0f), Facing.West);

            Battlefield.Hold(watcher);
            Battlefield.Hold(stranger);
            field.RunTurns(2);

            Assert.False(field.State.Vision.TryRecall(field.State, watcher.Owner, stranger, out _, out _),
                "An army cannot remember what it has never laid eyes on.");
        }

        [Fact]
        public void AMarkerGoesStaleRatherThanVanishing()
        {
            var field = new Battlefield("plains", 4700);

            UnitInstance watcher = field.Add(0, "scouts", field.Centre, Facing.East);
            UnitInstance enemy = field.Add(1, "swordsmen", field.Centre + new Vec2(120f, 0f), Facing.West);

            Battlefield.Hold(watcher);
            Battlefield.Hold(enemy);
            field.RunTurns(1);

            field.March(enemy, field.Centre + new Vec2(900f, 0f), bearing: Facing.East);

            field.RunTurns(5);
            field.State.Vision.TryRecall(field.State, watcher.Owner, enemy, out _, out int early);

            field.RunTurns(5);
            field.State.Vision.TryRecall(field.State, watcher.Owner, enemy, out _, out int late);

            Assert.True(late > early,
                $"A sighting is worth exactly as much as its age, so the marker has to keep ageing: " +
                $"{early} ticks old, then {late}.");
        }
    }
}
