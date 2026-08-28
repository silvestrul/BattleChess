using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using BattleChess.Contracts;
using BattleChess.Rules;
using BattleChess.Rules.HybridPlanning;
using Xunit;
using Xunit.Abstractions;

namespace BattleChess.Tests.Battle
{
    /// <summary>
    /// The seven orders of the recording of 2026-08-24 18:24, as given.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why this file exists at all.</b> The Great Field fixture in
    /// <see cref="FewerExpansionsTests"/> held the destinations of an
    /// <i>earlier</i> recording, and every lever in <c>M70</c> and <c>M71</c>
    /// was tuned against them. It reported 4 ms worst while the game the
    /// levers shipped into reported <b>387 ms</b> on the same map, the same
    /// roster and the same seven clicks. The fixture was not slow or
    /// approximate; it was measuring different orders, and so said nothing
    /// about the ones that froze.
    /// </para>
    /// <para>
    /// The orders below are copied from the log lines themselves, start and
    /// destination both, so the arrangement here is the arrangement that
    /// froze. The recorded cost of each is in the table, and a run that does
    /// not roughly reproduce those costs is a broken fixture, not a fast
    /// planner.
    /// </para>
    /// </remarks>
    [Collection(PlannerLevers.Name)]
    public sealed class TheFrozenSevenTests : IDisposable
    {
        private readonly ITestOutputHelper _out;
        public TheFrozenSevenTests(ITestOutputHelper output) => _out = output;

        public void Dispose() { }

        /// <summary>Given in the order they were clicked, with the tick and what each cost.</summary>
        private static readonly (int Tick, string Unit, Vec2 From, Vec2 To, double Ms, int Expanded)[] Recorded =
        {
            ( 14, "U16", new Vec2(263f, 1388f), new Vec2(888f, 1396f),  93.0,    29),
            (114, "U14", new Vec2(263f, 1638f), new Vec2(768f, 1607f),  19.8,    52),
            (175, "U15", new Vec2(263f, 1513f), new Vec2(507f, 1518f), 208.0,  7845),
            (335, "U17", new Vec2(263f, 1263f), new Vec2(623f, 1239f),   9.6,     4),
            (380, "U18", new Vec2(263f, 1163f), new Vec2(640f, 1119f),  17.6,    38),
            (435, "U19", new Vec2(263f, 1038f), new Vec2(570f, 1228f),  20.8,    49),
            (549, "U13", new Vec2(263f, 1763f), new Vec2(520f, 1653f), 387.5, 11947),
        };

        private static BattleState GreatField()
        {
            string root = TestContent.Root;
            ITerrainCatalogue terrain = TestContent.Terrain;

            BattleMapDefinition map = AsciiMapReader.Read(
                File.ReadAllText(Path.Combine(root, "maps", "greatfield.map.txt")), terrain);
            BattleSetup setup = BattleSetup.Parse(
                File.ReadAllText(Path.Combine(root, "battles", "greatfield.battle.txt")));

            return setup.Build(map, terrain, TestContent.Units, TestContent.Formations,
                new TerrainMovementModel(terrain));
        }

        [Fact(Skip = "A record of a measurement, not a check on one — it drives global levers.")]
        public void WhatTheSevenClicksActuallyCost() => Replay("as it ships");

        [Fact(Skip = "A record of a measurement, not a check on one — it drives global levers.")]
        public void TheSameSevenWithTheLeversOff()
        {
            try
            {
                HybridAStarPlanner.StrayMultiple = 0f;
                HybridAStarPlanner.Headings = 16;
                Replay("levers off: unbounded, sixteen headings");
            }
            finally
            {
                HybridAStarPlanner.StrayMultiple = 1.5f;
                HybridAStarPlanner.Headings = 0;
            }
        }

        /// <summary>Where the milliseconds of a dear order actually go.</summary>
        /// <remarks>
        /// The question the recording cannot answer: the <c>Cost</c> line times
        /// the whole of <c>Marching.PlanTo</c>, which is the ladder, the bent
        /// ladder, the tangent graph, the lattice and the proof of whatever
        /// came back. Any of those could be the freeze. This splits them.
        /// </remarks>
        [Fact(Skip = "A record of a measurement, not a check on one — it drives global levers.")]
        public void WhichStageTheMillisecondsAreIn()
        {
            try
            {
                HybridAStarPlanner.StrayMultiple = 0f;
                HybridAStarPlanner.Headings = 16;

                BattleState battle = GreatField();
                var clock = new BattleClock();
                IPathfinder pathfinder = new DirectPathfinder(
                    battle.Terrain, new TerrainMovementModel(TestContent.Terrain), TestContent.Terrain,
                    clearanceMetres: HexPathfinder.DefaultClearanceMetres);

                _out.WriteLine("Where a dear order's time goes, levers off so the dear ones are dear.");
                _out.WriteLine(string.Empty);
                _out.WriteLine("tick  unit    whole ms   lattice   its field    tangents    ladder   " +
                               "smoothing   lattice %");
                _out.WriteLine(new string('-', 100));

                int at = 0;

                foreach ((int tick, string name, Vec2 from, Vec2 to, double was, int expanded) in Recorded)
                {
                    for (; at < tick; at++) clock.Advance(battle);

                    UnitInstance unit = battle.UnitsOnField()
                        .OrderBy(u => Vec2.Distance(u.Position, from)).First();

                    Vec2 destination = OrderSystem.TryFindPlacement(
                        battle, unit, to, unit.Facing, out Vec2 stand)
                            ? stand
                            : OrderSystem.NearestReachable(battle, unit, to, unit.Position);

                    Facing arriveOn = Marching.AlongTheLine(unit.Position, destination, unit.Facing);

                    PlanningProfile.Reset();
                    PlanningProfile.Start();

                    var watch = Stopwatch.StartNew();
                    Plan plan = Marching.PlanTo(battle, unit, pathfinder, destination,
                        planner: RoutePlanners.TheStaged, arriveOn: arriveOn);
                    watch.Stop();

                    PlanningProfile.Stop();

                    double whole = watch.Elapsed.TotalMilliseconds;
                    double lattice = PlanningProfile.InclusiveMilliseconds(PlanningProfile.Step.HybridSearch);
                    double field = PlanningProfile.InclusiveMilliseconds(PlanningProfile.Step.HybridField);
                    double hunt = PlanningProfile.InclusiveMilliseconds(PlanningProfile.Step.Hunt);
                    double ladder = PlanningProfile.InclusiveMilliseconds(PlanningProfile.Step.Ladder);
                    double smooth = PlanningProfile.InclusiveMilliseconds(PlanningProfile.Step.SmoothRoute);

                    _out.WriteLine(
                        $"{tick,-5} {name,-6} {whole,9:0.0} {lattice,9:0.0} {field,11:0.0} " +
                        $"{hunt,11:0.0} {ladder,9:0.0} {smooth,11:0.0} " +
                        $"{(whole > 0 ? lattice / whole * 100 : 0),10:0}%");

                    unit.GiveOrder(
                        UnitOrder.MoveTo(destination, wheelFirst: false, bearing: arriveOn), unit.Position);
                    unit.Route = plan.ToRoute(wheelFirst: false);
                }
            }
            finally
            {
                HybridAStarPlanner.StrayMultiple = 1.5f;
                HybridAStarPlanner.Headings = 0;
            }
        }

        private void Replay(string what)
        {
            BattleState battle = GreatField();
            var clock = new BattleClock();
            // Exactly what the controller builds, defaults and all: neither
            // "route like the AI" nor "route by unit width" is ticked, so it is
            // a direct pathfinder at the default clearance.
            IPathfinder pathfinder = new DirectPathfinder(
                battle.Terrain, new TerrainMovementModel(TestContent.Terrain), TestContent.Terrain,
                clearanceMetres: HexPathfinder.DefaultClearanceMetres);

            _out.WriteLine($"The seven orders of battle-20260824-182441.log, {what}.");
            _out.WriteLine(string.Empty);
            _out.WriteLine("tick  unit    asked m   in play   here ms   expanded  in play   " +
                           "sideways    along    limit s  pressed");
            _out.WriteLine(new string('-', 110));

            int at = 0;

            foreach ((int tick, string name, Vec2 from, Vec2 to, double was, int wasExpanded) in Recorded)
            {
                // Forward to the moment the click happened. This is the whole
                // point of the fixture: the two orders that froze were given
                // while other regiments were mid-march, and on a pristine
                // deployment neither of them is dear at all.
                for (; at < tick; at++) clock.Advance(battle);

                UnitInstance unit = battle.UnitsOnField()
                    .OrderBy(u => Vec2.Distance(u.Position, from)).First();

                // The click is snapped to ground the regiment could stand on
                // before anything is planned, so the planner is asked about a
                // different point from the one the log's Move line prints.
                Vec2 destination = OrderSystem.TryFindPlacement(
                    battle, unit, to, unit.Facing, out Vec2 stand)
                        ? stand
                        : OrderSystem.NearestReachable(battle, unit, to, unit.Position);

                Facing arriveOn = Marching.AlongTheLine(unit.Position, destination, unit.Facing);

                var watch = Stopwatch.StartNew();
                Plan plan = Marching.PlanTo(battle, unit, pathfinder, destination,
                    planner: RoutePlanners.TheStaged, arriveOn: arriveOn);
                watch.Stop();

                _out.WriteLine(
                    $"{tick,-5} {name,-6} {Vec2.Distance(from, to),8:0} {was,9:0.0} " +
                    $"{watch.Elapsed.TotalMilliseconds,9:0.0} " +
                    $"{HybridAStarPlanner.LastExpansions,10} {wasExpanded,9} " +
                    $"{HybridAStarPlanner.StrayedSideways,10:0} {HybridAStarPlanner.StrayedAlong,8:0} " +
                    $"{HybridAStarPlanner.LastLimit,10:0} {plan.PressedThrough,8}");

                _out.WriteLine(
                    $"        it is at ({unit.Position.X:0},{unit.Position.Y:0}) facing " +
                    $"{unit.Facing.Degrees:0}, {battle.UnitsOnField().Count(u => u.Route != null)} marching, " +
                    $"{battle.UnitsOnField().Count()} on the field.");

                // And it marches, so the next order sees the field this one left.
                unit.GiveOrder(
                    UnitOrder.MoveTo(destination, wheelFirst: false, bearing: arriveOn), unit.Position);
                unit.Route = plan.ToRoute(wheelFirst: false);
            }
        }

    }
}
