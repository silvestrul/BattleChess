using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using BattleChess.Contracts;
using BattleChess.Rules;
using Xunit;
using Xunit.Abstractions;

namespace BattleChess.Tests.Battle
{
    /// <summary>
    /// Every route, written out, so two builds can be compared waypoint by
    /// waypoint instead of by counts that agree while the routes differ.
    /// </summary>
    [Collection(PlannerLevers.Name)]
    public sealed class RouteFingerprintTests
    {
        private readonly ITestOutputHelper _out;

        public RouteFingerprintTests(ITestOutputHelper output) => _out = output;

        [Theory]
        [InlineData("crucible")]
        [InlineData("brokencountry")]
        [InlineData("longmarch")]
        public void EveryRouteWrittenOut(string key)
        {
            BattleState battle = BenchScenariosTests.Load(key);

            IPathfinder pathfinder = new DirectPathfinder(
                battle.Terrain, new TerrainMovementModel(TestContent.Terrain), TestContent.Terrain);

            foreach (IRoutePlanner? planner in new IRoutePlanner?[]
                     { RoutePlanners.TheLadder, RoutePlanners.TheSearch, RoutePlanners.TheTangents })
            {
                string name = planner?.Name ?? "default";

                foreach (UnitInstance unit in battle.UnitsOnField())
                {
                    Plan plan = Marching.PlanTo(
                        battle, unit, pathfinder, BenchScenariosTests.OrderFor(battle, unit),
                        planner: planner);

                    var line = new StringBuilder();
                    line.Append(CultureInfo.InvariantCulture, $"{name} u{unit.Id.Value:D3} ");
                    line.Append(plan.Path.Found ? "ok " : "NO ");
                    line.Append(plan.PressedThrough ? "press " : "clean ");
                    line.Append(CultureInfo.InvariantCulture,
                        $"{Marching.SecondsToWalk(battle, unit, plan.Path.Waypoints, plan.Hold):0.0}s ");
                    line.Append(plan.Effort.AskedTheLadder ? "asked " : "kept ");

                    foreach (Vec2 at in plan.Path.Waypoints)
                        line.Append(CultureInfo.InvariantCulture, $"({at.X:0.00},{at.Y:0.00})");

                    _out.WriteLine(line.ToString());
                }
            }
        }

        /// <summary>
        /// The default planner's routes at <see cref="RouteSearch.MostPlaces"/>
        /// 48 against 24, compared one route at a time.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Why aggregate numbers could not settle this.</b> The full profile
        /// (<b>M83</b>) measured halving the visibility graph at 25 to 29% off
        /// three fields with every quality figure identical — same total route
        /// seconds to the second, same pressed-through, same unwalkable. Totals
        /// that agree are strong evidence and not proof: two routes changing in
        /// opposite directions sum the same. This asks each route separately.
        /// </para>
        /// <para>
        /// <b>Against the planner that ships</b>, and on all four fields. The
        /// theory above it runs the ladder, the search and the tangents over
        /// three, which between them is neither the planner a played battle
        /// reaches nor the field that most recently broke.
        /// </para>
        /// <para>
        /// <b>A gate, not a record.</b> It restores the lever in a
        /// <c>finally</c> and asserts rather than printing, so it can sit in the
        /// suite: if a later change makes the graph size matter, this is what
        /// says so.
        /// </para>
        /// </remarks>
        [Theory]
        [InlineData("crucible")]
        [InlineData("longmarch")]
        [InlineData("brokencountry")]
        [InlineData("sidewaysmile")]
        public void HalvingTheGraphChangesNoRoute(string key)
        {
            int wasPlaces = RouteSearch.MostPlaces;

            try
            {
                RouteSearch.MostPlaces = 48;
                var full = Fingerprints(key, out int reachedAt48);

                RouteSearch.MostPlaces = 24;
                var half = Fingerprints(key, out _);

                // Non-vacuity, and it is the whole reason this test is worth
                // keeping. Without it the test passes at MostPlaces of six, or
                // of two — measured — because the tangent stage's answer is
                // refused on every bench field and the cascade answers below
                // it. That would make "the routes did not change" a statement
                // about nothing. This asserts the cap was actually reached, so
                // a pass means the search really was truncated and the routes
                // really did survive it.
                Assert.True(
                    reachedAt48 > 24,
                    $"the search never passed 24 places on {key} (most was {reachedAt48}), " +
                    "so halving the cap truncated nothing and this proves nothing");

                Assert.Equal(full.Count, half.Count);

                // Non-vacuity: a field that routed nothing would agree with
                // itself perfectly and prove nothing at all.
                Assert.True(full.Count >= 40, $"only {full.Count} routes on {key}");

                var differ = new List<string>();

                foreach (var pair in full)
                {
                    if (half[pair.Key] == pair.Value) continue;

                    differ.Add(
                        $"{pair.Key}" +
                        $"\n     48: {pair.Value}" +
                        $"\n     24: {half[pair.Key]}");
                }

                foreach (string line in differ) _out.WriteLine(line);

                Assert.True(
                    differ.Count == 0,
                    $"{differ.Count} of {full.Count} routes on {key} differ at half the graph");
            }
            finally
            {
                RouteSearch.MostPlaces = wasPlaces;
            }
        }

        /// <summary>One line an order, everything a route is made of.</summary>
        private static Dictionary<string, string> Fingerprints(string key, out int mostPlaces)
        {
            BattleState battle = BenchScenariosTests.Load(key);

            IPathfinder pathfinder = new DirectPathfinder(
                battle.Terrain, new TerrainMovementModel(TestContent.Terrain), TestContent.Terrain);

            var byUnit = new Dictionary<string, string>();

            mostPlaces = 0;
            RouteSearch.PlacesHighWater = 0;

            foreach (UnitInstance unit in battle.UnitsOnField())
            {
                Plan plan = Marching.PlanTo(
                    battle, unit, pathfinder, BenchScenariosTests.OrderFor(battle, unit));

                var line = new StringBuilder();

                line.Append(plan.Path.Found ? "ok " : "NO ");
                line.Append(plan.PressedThrough ? "press " : "clean ");
                line.Append(CultureInfo.InvariantCulture,
                    $"{Marching.SecondsToWalk(battle, unit, plan.Path.Waypoints, plan.Hold):0.00}s ");

                foreach (Vec2 at in plan.Path.Waypoints)
                    line.Append(CultureInfo.InvariantCulture, $"({at.X:0.00},{at.Y:0.00})");

                byUnit[$"{key} u{unit.Id.Value:D3}"] = line.ToString();
            }

            mostPlaces = RouteSearch.PlacesHighWater;

            return byUnit;
        }
    }
}
