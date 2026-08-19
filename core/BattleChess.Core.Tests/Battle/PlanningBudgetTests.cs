using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using BattleChess.Contracts;
using BattleChess.Rules;
using Xunit;
using Xunit.Abstractions;

namespace BattleChess.Tests.Battle
{
    /// <summary>
    /// What a frame's worth of catching up costs, with the allowance and
    /// without it.
    /// </summary>
    /// <remarks>
    /// Reproduces the shape of the recorded stall rather than describing it: a
    /// frame that has fallen behind runs several ticks at once, and every one
    /// of those ticks does its own re-planning. Measured on the Great Field the
    /// worst such frame planned 41 routes and took 1 652 ms.
    /// </remarks>
    public sealed class PlanningBudgetTests
    {
        private readonly ITestOutputHelper _out;

        public PlanningBudgetTests(ITestOutputHelper output) => _out = output;

        /// <summary>What the host's own catch-up cap is, and so how many ticks a bad frame runs.</summary>
        private const int TicksInACatchUpFrame = 8;

        /// <summary>A log that keeps nothing: this measures cost, not narrative.</summary>
        private static readonly IBattleLog NoLog = NullBattleLog.Instance;

        private static BattleState Load()
        {
            ITerrainCatalogue terrain = TestContent.Terrain;

            BattleMapDefinition map = AsciiMapReader.Read(
                File.ReadAllText(Path.Combine(TestContent.Root, "maps", "greatfield.map.txt")), terrain);

            BattleSetup setup = BattleSetup.Parse(
                File.ReadAllText(Path.Combine(TestContent.Root, "battles", "greatfield.battle.txt")));

            return setup.Build(map, terrain, TestContent.Units, TestContent.Formations,
                new TerrainMovementModel(terrain));
        }

        /// <summary>
        /// Sets both armies on each other, every regiment attacking the
        /// nearest enemy it can see.
        /// </summary>
        /// <remarks>
        /// An attack is what the recording was doing and it is the path the
        /// cost was on: a chase re-plans when its route runs out or when the
        /// ground in front of it changes, and with eighty thousand men closing
        /// on one frontage both happen constantly. A plain march was tried
        /// first and measured nothing — the two lines start about a kilometre
        /// apart at 1,59 m/s, so for six hundred ticks nobody obstructs anybody
        /// and the stall detector, which is what makes a march re-plan, never
        /// has cause to fire.
        /// </remarks>
        private static void OrderEverybodyForward(BattleState battle, IPathfinder pathfinder)
        {
            var everyone = new List<UnitInstance>(battle.UnitsOnField());

            foreach (UnitInstance unit in everyone)
            {
                UnitInstance? nearest = null;
                float best = float.MaxValue;

                foreach (UnitInstance other in everyone)
                {
                    if (other.Owner == unit.Owner) continue;

                    float away = Vec2.DistanceSquared(unit.Position, other.Position);
                    if (away >= best) continue;

                    best = away;
                    nearest = other;
                }

                if (nearest == null) continue;

                unit.GiveOrder(UnitOrder.Attack(nearest.Id), unit.Position);

                // The first route is the host's job — OrderSystem re-plans
                // marches and chases, it does not begin them.
                Plan plan = Marching.PlanTo(battle, unit, pathfinder, nearest.Position);

                if (plan.Path.Found && plan.Path.Waypoints.Count >= 2)
                    unit.Route = plan.ToRoute();
            }
        }

        private (int routes, double milliseconds) OneCatchUpFrame(BattleState battle, BattleClock clock)
        {
            int before = battle.RoutesPlanned;
            long ticksBefore = battle.RoutePlanningTicks;

            for (int i = 0; i < TicksInACatchUpFrame; i++)
                clock.Advance(battle, NoLog);

            return (battle.RoutesPlanned - before,
                    (battle.RoutePlanningTicks - ticksBefore) * 1000.0 / Stopwatch.Frequency);
        }

        [Fact]
        public void AFrameThatCatchesUpPlansNoMoreThanItsAllowance()
        {
            const int frames = 12;

            var loose = Measure(rationed: false, frames);
            var held = Measure(rationed: true, frames);

            _out.WriteLine($"{"frame",6} | {"routes",-18} | {"ms",-18}");
            _out.WriteLine($"{"",6} | {"loose",8} {"held",9} | {"loose",8} {"held",9}");

            for (int i = 0; i < frames; i++)
                _out.WriteLine(
                    $"{i,6} | {loose[i].routes,8} {held[i].routes,9} | " +
                    $"{loose[i].milliseconds,8:0.0} {held[i].milliseconds,9:0.0}");

            int looseWorst = 0, heldWorst = 0;
            double looseMs = 0, heldMs = 0;

            foreach (var f in loose) { looseWorst = Math.Max(looseWorst, f.routes); looseMs = Math.Max(looseMs, f.milliseconds); }
            foreach (var f in held) { heldWorst = Math.Max(heldWorst, f.routes); heldMs = Math.Max(heldMs, f.milliseconds); }

            _out.WriteLine($"\nworst frame: {looseWorst} routes / {looseMs:0} ms loose, " +
                           $"{heldWorst} routes / {heldMs:0} ms held");

            // The whole point: no frame may exceed its own allowance, however
            // many ticks it had to catch up over.
            Assert.True(heldWorst <= PlanningBudget.DefaultRoutesPerFrame,
                $"a rationed frame planned {heldWorst} routes, above its allowance of " +
                $"{PlanningBudget.DefaultRoutesPerFrame}.");

            // And it has to actually be doing something — a budget that never
            // binds proves nothing, which is the non-vacuity guard this suite
            // has been caught needing three times.
            Assert.True(looseWorst > PlanningBudget.DefaultRoutesPerFrame,
                $"the unrationed run only reached {looseWorst} routes in a frame, so this " +
                "arrangement never exercised the allowance and the comparison means nothing.");
        }

        private List<(int routes, double milliseconds)> Measure(bool rationed, int frames)
        {
            BattleState battle = Load();
            var movement = new TerrainMovementModel(TestContent.Terrain);
            IPathfinder pathfinder = new DirectPathfinder(battle.Terrain, movement, TestContent.Terrain);

            BattleClock clock = new BattleClock()
                .Add(new VisionSystem())
                .Add(new OrderSystem(pathfinder))
                .Add(new ContactSystem())
                .Add(new MovementSystem());

            OrderEverybodyForward(battle, pathfinder);

            var seen = new List<(int, double)>();

            for (int frame = 0; frame < frames; frame++)
            {
                if (rationed) battle.Planning.OpenFrame();

                seen.Add(OneCatchUpFrame(battle, clock));
            }

            return seen;
        }

        [Fact]
        public void NobodyIsPutOffForEver()
        {
            var budget = new PlanningBudget();

            var everyone = new List<UnitId>();
            for (int i = 0; i < 40; i++) everyone.Add(new UnitId(i + 1));

            var servedOn = new Dictionary<UnitId, int>();

            for (int frame = 0; frame < 40; frame++)
            {
                budget.OpenFrame();

                // Always asked in the same order, which is the arrangement that
                // starves whoever is last without the deferral queue.
                foreach (UnitId unit in everyone)
                {
                    if (!budget.MayPlan(unit)) continue;

                    budget.Spent(unit, 0);
                    servedOn[unit] = frame;
                }
            }

            var never = new List<UnitId>();
            foreach (UnitId unit in everyone)
                if (!servedOn.ContainsKey(unit)) never.Add(unit);

            _out.WriteLine($"{servedOn.Count} of {everyone.Count} regiments got a turn over 40 frames");

            Assert.True(never.Count == 0,
                $"{never.Count} regiments were never planned at all across 40 frames — " +
                "the ones late in the order are being starved.");
        }

        [Fact]
        public void ARegimentPlansAtMostOncePerFrame()
        {
            var budget = new PlanningBudget();
            var one = new UnitId(1);

            budget.OpenFrame();

            Assert.True(budget.MayPlan(one));
            budget.Spent(one, 0);

            Assert.False(budget.MayPlan(one));
            Assert.False(budget.MayPlan(one));

            budget.OpenFrame();
            Assert.True(budget.MayPlan(one));
        }

        [Fact]
        public void WithoutAHostNothingIsRefused()
        {
            var budget = new PlanningBudget();
            var one = new UnitId(1);

            Assert.False(budget.IsRationing);

            for (int i = 0; i < 100; i++)
            {
                Assert.True(budget.MayPlan(one));
                budget.Spent(one, 0);
            }
        }
    }
}
