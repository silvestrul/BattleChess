using System.Collections.Generic;
using System.Linq;
using BattleChess.Contracts;
using BattleChess.Rules;
using BattleChess.Rules.GridPlanning;
using Xunit;
using Xunit.Abstractions;

namespace BattleChess.Tests.Battle
{
    /// <summary>
    /// What the teal line on the screen is, against what the planner would walk.
    /// </summary>
    /// <remarks>
    /// The route preview draws <see cref="RegimentGrid.TryRoute"/> raw. Every
    /// route the planner hands out has been through
    /// <c>RouteSmoothing.Applied</c> first, and the grid stage inside the
    /// cascade smooths its own answer before offering it. So the picture is of
    /// a route no planner would ever return.
    /// </remarks>
    public sealed class GridPreviewTests
    {
        private readonly ITestOutputHelper _out;

        public GridPreviewTests(ITestOutputHelper output) => _out = output;

        /// <summary>
        /// The door the preview reaches the cast-ahead pass through, and that it
        /// materially straightens.
        /// </summary>
        /// <remarks>
        /// <b>M82</b>'s gate. The fault was that the preview drew a path of hex
        /// centres no planner would return, and it could not do otherwise
        /// because <c>RouteSmoothing</c> is internal to the rules and the
        /// drawing code is outside them. This holds the public door open and
        /// checks it does the work; the call site itself is held by the Unity
        /// compile, which is the only thing that can see it.
        /// </remarks>
        [Fact]
        public void ThePreviewCanStraightenARouteTheWayThePlannerDoes()
        {
            float was = RegimentGrid.SpacingMultiple;

            try
            {
                // A tenth of a regiment to a cell: the setting the screenshots
                // were taken at, and the one that makes the raw path worst.
                RegimentGrid.SpacingMultiple = 0.1f;
                RegimentGrid.Forget();

                BattleState battle = BenchScenariosTests.Load("sidewaysmile");

                int raw = 0, smoothed = 0, routes = 0;

                foreach (UnitInstance unit in battle.UnitsOnField().Where(u => u.Owner.Value == 0))
                {
                    RegimentGrid grid = RegimentGrid.For(battle, unit);
                    Vec2 to = BenchScenariosTests.OrderFor(battle, unit);

                    if (!grid.TryRoute(unit.Position, to, out List<Vec2> route)) continue;
                    if (route.Count < 3) continue;

                    IReadOnlyList<Vec2> straight = Marching.Straightened(battle, unit, route);

                    routes++;
                    raw += route.Count;
                    smoothed += straight.Count;

                    Assert.True(
                        Length(straight) <= Length(route) + 1f,
                        $"Straightening lengthened a route: {Length(route):0} m became {Length(straight):0} m.");
                }

                _out.WriteLine($"{routes} routes: {raw} raw points became {smoothed}");

                // Non-vacuity: a field that stopped routing would pass every
                // assertion above by having nothing to assert about.
                Assert.True(routes >= 10, $"Only {routes} routes to measure — this is measuring nothing.");

                Assert.True(
                    smoothed * 5 < raw,
                    $"The cast-ahead pass barely moved the preview: {raw} points became {smoothed}. " +
                    "The raw grid answer is a path of hex centres and should collapse by an order.");
            }
            finally
            {
                RegimentGrid.SpacingMultiple = was;
                RegimentGrid.Forget();
            }
        }

        [Fact(Skip = "A record of a measurement, not a check on one.")]
        public void WhatThePreviewDrawsAgainstWhatThePlannerWouldWalk()
        {
            float was = RegimentGrid.SpacingMultiple;

            try
            {
                _out.WriteLine("cells   asked  routed   raw pts   smoothed   raw m   smoothed m");
                _out.WriteLine(new string('-', 68));

                // 1,0 is the default the first screenshot was taken at; 0,1 is
                // what the second and third were set to.
                foreach (float multiple in new[] { 1f, 0.5f, 0.25f, 0.1f })
                {
                    RegimentGrid.SpacingMultiple = multiple;
                    Measure(multiple);
                }
            }
            finally
            {
                RegimentGrid.SpacingMultiple = was;
                RegimentGrid.Forget();
            }
        }

        private void Measure(float multiple)
        {
            RegimentGrid.Forget();

            BattleState battle = BenchScenariosTests.Load("sidewaysmile");
            List<UnitInstance> units = battle.UnitsOnField().ToList();

            int asked = 0, routed = 0, raw = 0, smoothed = 0;
            float rawMetres = 0f, smoothedMetres = 0f;

            foreach (UnitInstance unit in units.Where(u => u.Owner.Value == 0))
            {
                RegimentGrid grid = RegimentGrid.For(battle, unit);
                Vec2 to = BenchScenariosTests.OrderFor(battle, unit);

                asked++;

                if (!grid.TryRoute(unit.Position, to, out List<Vec2> route)) continue;
                if (route.Count < 2) continue;

                routed++;
                raw += route.Count;
                rawMetres += Length(route);

                // Exactly what the cascade does with a grid route before it
                // will consider offering it.
                var plan = new Plan(
                    PathResult.Success(
                        route, System.Array.Empty<Coord>(), Length(route), Length(route), 0),
                    hold: null, pressedThrough: false);

                Plan straight = RouteSmoothing.Applied(battle, unit, plan);

                smoothed += straight.Path.Waypoints.Count;
                smoothedMetres += Length(straight.Path.Waypoints);
            }

            _out.WriteLine(
                $"{multiple,5:0.00}  {asked,6}  {routed,6}  {raw,8}  {smoothed,9}  " +
                $"{rawMetres,6:0}  {smoothedMetres,10:0}");
        }

        private static float Length(IReadOnlyList<Vec2> points)
        {
            float total = 0f;
            for (int i = 1; i < points.Count; i++) total += Vec2.Distance(points[i - 1], points[i]);
            return total;
        }
    }
}
