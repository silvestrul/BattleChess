using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using BattleChess.Contracts;
using BattleChess.Rules;
using Xunit;
using Xunit.Abstractions;

namespace BattleChess.Tests.Battle
{
    /// <summary>
    /// A wing box-selected and sent with one click, planned the way the
    /// interface plans it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The bench beside this one asks whether <see cref="Marching.PlanTo"/> can
    /// be called from several threads. That is not the whole of what a click
    /// does: before planning, the interface asks
    /// <see cref="OrderSystem.TryFindPlacement"/> where the regiment can
    /// actually stand, and after planning it gives the order. So this measures
    /// and compares the whole of the working-out, and separately checks the one
    /// assumption the split rests on — that an order given to one regiment
    /// changes nothing about the next regiment's route.
    /// </para>
    /// <para>
    /// It compares rather than asserts about threads. A shared scratchpad, a
    /// shared set of index marks or a shared lazily-built table would all show
    /// up here as a different destination or a different number of seconds, and
    /// two of them did.
    /// </para>
    /// </remarks>
    public sealed class WingOrderTests
    {
        private readonly ITestOutputHelper _out;
        public WingOrderTests(ITestOutputHelper output) => _out = output;

        [Theory]
        [InlineData("crucible")]
        [InlineData("longmarch")]
        [InlineData("brokencountry")]
        public void OneClickPlansTheSameRoutesWhetherOrNotItPlansThemAtOnce(string key)
        {
            BattleState battle = BenchScenariosTests.Load(key);

            var wing = battle.UnitsOnField().ToList();
            var wanted = Spread(battle, wing);

            Worked[] alone = null!, together = null!;
            double aloneMs = double.MaxValue, togetherMs = double.MaxValue;

            Plan(battle, wing, wanted, atOnce: false);
            Plan(battle, wing, wanted, atOnce: true);

            for (int pass = 0; pass < 3; pass++)
            {
                var watch = Stopwatch.StartNew();
                Worked[] one = Plan(battle, wing, wanted, atOnce: false);
                watch.Stop();

                if (watch.Elapsed.TotalMilliseconds < aloneMs)
                {
                    aloneMs = watch.Elapsed.TotalMilliseconds;
                    alone = one;
                }

                watch.Restart();
                Worked[] all = Plan(battle, wing, wanted, atOnce: true);
                watch.Stop();

                if (watch.Elapsed.TotalMilliseconds < togetherMs)
                {
                    togetherMs = watch.Elapsed.TotalMilliseconds;
                    together = all;
                }
            }

            _out.WriteLine(
                $"{key}: {wing.Count} regiments — one after another {aloneMs:0.0} ms " +
                $"({aloneMs / wing.Count:0.00} an order), all at once {togetherMs:0.0} ms " +
                $"({togetherMs / wing.Count:0.00} an order), {aloneMs / togetherMs:0.0}× on " +
                $"{Environment.ProcessorCount} cores.");

            var differed = new List<string>();

            for (int i = 0; i < wing.Count; i++)
            {
                if (Vec2.Distance(alone[i].Destination, together[i].Destination) > 0.001f)
                    differed.Add($"{wing[i].Def.DisplayName} aimed at {alone[i].Destination} alone " +
                                 $"and {together[i].Destination} together");

                else if (alone[i].Found != together[i].Found)
                    differed.Add($"{wing[i].Def.DisplayName} routed {alone[i].Found} alone " +
                                 $"and {together[i].Found} together");

                else if (MathF.Abs(alone[i].Seconds - together[i].Seconds) > 0.01f)
                    differed.Add($"{wing[i].Def.DisplayName} took {alone[i].Seconds:0.00} s alone " +
                                 $"and {together[i].Seconds:0.00} s together");
            }

            Assert.True(
                differed.Count == 0,
                $"{differed.Count} of {wing.Count} routes changed when the wing was planned at once. " +
                "Something on the planning path is shared between threads.\n  " +
                string.Join("\n  ", differed.Take(5)));

            // Non-vacuity: a comparison of two empty answers is not a finding.
            Assert.Equal(wing.Count, alone.Count(w => w.Found));
        }

        /// <summary>
        /// The assumption the split rests on: the interface gives every order
        /// after every route is worked out, where it used to give each one
        /// before the next was planned.
        /// </summary>
        /// <remarks>
        /// It holds because a regiment is an obstacle by whose side it is on and
        /// where its body is, and an order changes neither — but "it holds
        /// because I read the code" is how the last four of these went, so it is
        /// measured instead.
        /// </remarks>
        [Theory]
        [InlineData("crucible")]
        [InlineData("brokencountry")]
        public void GivingTheOrdersAfterwardsPlansTheSameRoutes(string key)
        {
            BattleState battle = BenchScenariosTests.Load(key);

            var wing = battle.UnitsOnField().ToList();
            var wanted = Spread(battle, wing);

            Worked[] pure = Plan(battle, wing, wanted, atOnce: false);

            // The old order of operations: plan one, give it its order, plan the
            // next against a field where the first is already under orders.
            IPathfinder pathfinder = Finder(battle);
            var interleaved = new Worked[wing.Count];

            for (int i = 0; i < wing.Count; i++)
            {
                interleaved[i] = WorkOut(battle, wing[i], wanted[i], pathfinder);

                if (interleaved[i].Found)
                {
                    wing[i].GiveOrder(
                        UnitOrder.MoveTo(interleaved[i].Destination, wheelFirst: false), wing[i].Position);
                }
            }

            for (int i = 0; i < wing.Count; i++)
            {
                Assert.True(
                    Vec2.Distance(pure[i].Destination, interleaved[i].Destination) < 0.001f &&
                    pure[i].Found == interleaved[i].Found &&
                    MathF.Abs(pure[i].Seconds - interleaved[i].Seconds) < 0.01f,
                    $"{wing[i].Def.DisplayName} planned differently when the regiments before it were " +
                    $"already under orders: {pure[i].Seconds:0.00} s against {interleaved[i].Seconds:0.00} s.");
            }
        }

        private readonly struct Worked
        {
            public readonly Vec2 Destination;
            public readonly bool Found;
            public readonly float Seconds;

            public Worked(Vec2 destination, bool found, float seconds)
            {
                Destination = destination;
                Found = found;
                Seconds = seconds;
            }
        }

        private static IPathfinder Finder(BattleState battle) => new DirectPathfinder(
            battle.Terrain, new TerrainMovementModel(TestContent.Terrain), TestContent.Terrain);

        /// <summary>Where a wing sent with one click actually goes: a block, keeping its shape.</summary>
        private static Vec2[] Spread(BattleState battle, IReadOnlyList<UnitInstance> wing)
        {
            MapBounds bounds = battle.Terrain.Bounds;

            var middle = new Vec2(
                bounds.Min.X + (bounds.Max.X - bounds.Min.X) * 0.72f,
                bounds.Min.Y + (bounds.Max.Y - bounds.Min.Y) * 0.5f);

            Vec2 origin = Vec2.Zero;
            foreach (UnitInstance unit in wing) origin += unit.Position;
            origin /= wing.Count;

            var wanted = new Vec2[wing.Count];
            for (int i = 0; i < wing.Count; i++) wanted[i] = middle + (wing[i].Position - origin);

            return wanted;
        }

        private static Worked[] Plan(
            BattleState battle, IReadOnlyList<UnitInstance> wing, Vec2[] wanted, bool atOnce)
        {
            var worked = new Worked[wing.Count];

            // As the interface does it before a batch: every shape settled while
            // one thread still owns them all.
            foreach (UnitInstance unit in battle.UnitsOnField()) _ = unit.Shape;

            if (atOnce)
            {
                Parallel.ForEach(
                    Partitioner.Create(0, wing.Count, 1), () => Finder(battle),
                    (range, _, pathfinder) =>
                    {
                        for (int i = range.Item1; i < range.Item2; i++)
                            worked[i] = WorkOut(battle, wing[i], wanted[i], pathfinder);

                        return pathfinder;
                    },
                    _ => { });
            }
            else
            {
                IPathfinder pathfinder = Finder(battle);

                for (int i = 0; i < wing.Count; i++)
                    worked[i] = WorkOut(battle, wing[i], wanted[i], pathfinder);
            }

            return worked;
        }

        /// <summary>The controller's own working-out, in the same order it does it.</summary>
        private static Worked WorkOut(
            BattleState battle, UnitInstance unit, Vec2 asked, IPathfinder pathfinder)
        {
            Vec2 destination = OrderSystem.TryFindPlacement(battle, unit, asked, unit.Facing, out Vec2 stand)
                ? stand
                : OrderSystem.NearestReachable(battle, unit, asked, unit.Position);

            Facing arriveOn = Marching.AlongTheLine(unit.Position, destination, unit.Facing);

            Plan plan = Marching.PlanTo(
                battle, unit, pathfinder, destination, log: null, arriveOn: arriveOn);

            float seconds = plan.Path.Found
                ? Marching.SecondsToWalk(battle, unit, plan.Path.Waypoints, plan.Hold)
                : 0f;

            return new Worked(destination, plan.Path.Found, seconds);
        }
    }
}
