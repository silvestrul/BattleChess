using BattleChess.Contracts;
using BattleChess.Rules;
using Xunit;

namespace BattleChess.Tests.Battle
{
    /// <summary>
    /// Regiments at the edge of the map.
    /// </summary>
    /// <remarks>
    /// Every battlefield is ringed with country nothing can cross, so the
    /// border is not an exotic case — it is where flanks rest, where routers
    /// run, and where a pursued regiment ends up. It is also where a regiment's
    /// footprint starts hanging over the end of the world, which is the thing
    /// that kept breaking.
    /// </remarks>
    public sealed class EdgeOfTheWorldTests
    {
        /// <summary>A field walled in on all four sides, as the real maps are.</summary>
        private static Battlefield Walled(ulong seed) =>
            new Battlefield("plains", seed, RuleSet.Full, canvas =>
            {
                canvas.Rect(0, 0, canvas.Columns - 1, 1, "mountain");
                canvas.Rect(0, canvas.Rows - 2, canvas.Columns - 1, canvas.Rows - 1, "mountain");
                canvas.Band(0, 1, "mountain");
                canvas.Band(canvas.Columns - 2, canvas.Columns - 1, "mountain");
            });

        [Theory]
        [InlineData("along the wall")]
        [InlineData("back toward the middle")]
        [InlineData("into the corner")]
        public void ARegimentAgainstTheBorderCanStillBeMarched(string where)
        {
            Battlefield field = Walled(18000);

            // Hard against the northern wall, close enough that its hundred
            // metres of frontage overhangs the map edge.
            var start = new Vec2(field.Centre.X, field.Map.Bounds.Max.Y - 60f);

            UnitInstance unit = field.Add(0, "swordsmen", start, Facing.East);

            Vec2 want = where switch
            {
                "along the wall" => new Vec2(field.Centre.X + 300f, start.Y),
                "back toward the middle" => field.Centre,
                _ => new Vec2(field.Map.Bounds.Max.X - 10f, field.Map.Bounds.Max.Y - 10f),
            };

            field.March(unit, OrderSystem.NearestReachable(field.State, unit, want, unit.Position));
            field.RunTurns(4);

            Assert.True(Vec2.Distance(unit.Position, start) > 100f,
                $"Ordered {where}, it moved {Vec2.Distance(unit.Position, start):0} m.");
        }

        [Fact]
        public void ReadingTheGroundUnderARegimentThatOverhangsTheMapDoesNotThrow()
        {
            Battlefield field = Walled(18100);

            // Centre inside the map, frontage over the edge — the ordinary
            // state of any regiment resting its flank on the border.
            UnitInstance unit = field.Add(
                0, "cavalry", new Vec2(field.Map.Bounds.Max.X - 20f, field.Centre.Y), Facing.North);

            float disorder = field.State.WorstDisorderUnder(unit);

            Assert.True(disorder >= 0f,
                "There is no ground beyond the border and therefore nothing out there to disorder " +
                "anybody. Asking about it used to throw.");
        }

        [Fact]
        public void ARuleThatThrowsDoesNotStopTheClock()
        {
            var field = new Battlefield("plains", 18200);

            field.Clock.Add(new AlwaysThrowsSystem());

            UnitInstance unit = field.Add(0, "swordsmen", field.Centre - new Vec2(200f, 0f), Facing.East);
            field.March(unit, field.Centre);

            field.RunTurns(2);

            Assert.True(Vec2.Distance(unit.Position, field.Centre) < 100f,
                "A rule that throws must not be able to freeze the battle. One did, and the army " +
                "appeared to be stuck against the map border with nothing said about why.");

            Assert.True(field.TimesSaid("was skipped") > 0,
                "And it must say so, or the next one is just as hard to find.");
        }

        private sealed class AlwaysThrowsSystem : IBattleSystem
        {
            public string Name => "Broken";

            public void Step(BattleState battle, int tick, IBattleLog log) =>
                throw new System.InvalidOperationException("this rule is broken");
        }
    }
}
