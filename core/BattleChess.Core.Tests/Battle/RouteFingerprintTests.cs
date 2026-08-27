using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using BattleChess.Contracts;
using BattleChess.Rules;
using BattleChess.Rules.GridPlanning;
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
        /// <b>With the regiment grid off and the tangent stage on</b>, since
        /// <b>M86</b>. What this gates
        /// is the tangent search's cap, and after the reorder the shipping
        /// cascade answers most orders above that search and some fields never
        /// reach it at all - longmarch draws the graph <i>zero</i> times now,
        /// where before it drew it four. Run as shipped, the non-vacuity
        /// assertion below correctly reported that the cap was never reached
        /// and the comparison proved nothing. Turning the grid off puts the
        /// tangent stage back in the path of every order that the ladder could
        /// not answer, which is the condition under which the cap can matter at
        /// all, and is strictly more orders than a played battle sends through
        /// it.
        /// </para>
        /// <para>
        /// The route-identity claim for the cascade <i>as shipped</i> is the
        /// theory above, which writes out every route for three planners.
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
            GridUse wasGrid = GridRoutePlanner.Use;
            bool wasStage = StagedRoutePlanner.AskTangentStage;

            try
            {
                // See the remarks: the subject here is the search's cap, so the
                // search has to be in the path of the orders being compared.
                // Both of these are off in the cascade that ships, and both for
                // the same reason - the grid answers first and the tangent
                // stage answers nothing - so both have to be put back.
                GridRoutePlanner.Use = GridUse.Off;
                StagedRoutePlanner.AskTangentStage = true;

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
                GridRoutePlanner.Use = wasGrid;
                StagedRoutePlanner.AskTangentStage = wasStage;
            }
        }

        /// <summary>
        /// Every route the cascade that ships actually produces, one line an
        /// order, on all four bench fields.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The theory at the top of this file runs the ladder, the places
        /// search and the tangent search each on its own. None of those is the
        /// planner a played battle reaches, so nothing here wrote down what the
        /// cascade itself returns - which meant a change to the <i>order</i> of
        /// its stages had no record to be checked against. M86 was that change.
        /// </para>
        /// <para>
        /// Asserts only that it routed something, because there is nothing
        /// in-process to compare against: its value is the output, diffed
        /// between two builds. That is how M86 was shown to move no route.
        /// </para>
        /// </remarks>
        [Theory]
        [InlineData("crucible")]
        [InlineData("longmarch")]
        [InlineData("brokencountry")]
        [InlineData("sidewaysmile")]
        public void EveryShippingRouteWrittenOut(string key)
        {
            var routes = Fingerprints(key, out _);

            foreach (var pair in routes) _out.WriteLine($"SHIP {pair.Key} {pair.Value}");

            Assert.True(routes.Count >= 40, $"only {routes.Count} routes on {key}");
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
