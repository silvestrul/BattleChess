using System;
using System.Collections.Generic;
using BattleChess.Contracts;
using BattleChess.Rules;
using Xunit;
using Xunit.Abstractions;

namespace BattleChess.Tests.Battle
{
    /// <summary>
    /// The hybrid finds faster routes than the search. This asks which of the
    /// two reasons it is: the good route is not in the search's graph at all
    /// (coverage), or it is and the search did not return it (costing).
    /// </summary>
    /// <remarks>
    /// By snapping the hybrid's own bends onto the nearest place the search
    /// would have considered. If the snapped route is still fast, the search
    /// could have expressed it and did not — a costing fault. If snapping ruins
    /// it, the places are the problem.
    /// </remarks>
    public sealed class CoverageOrCostingTests
    {
        private readonly ITestOutputHelper _out;

        public CoverageOrCostingTests(ITestOutputHelper output) => _out = output;

        [Fact]
        public void IsItTheGraphOrTheSearch()
        {
            var field = new Battlefield();

            UnitInstance mover = field.Add(0, "cavalry", new Vec2(200f, 200f), Facing.FromDegrees(0f));

            for (int i = 0; i < 12; i++)
                field.Add(0, "spearmen",
                    new Vec2(260f + (i % 4) * 90f, 140f + (i / 4) * 110f), Facing.FromDegrees(90f));

            BattleState battle = field.State;

            IPathfinder pathfinder = new DirectPathfinder(
                battle.Terrain, new TerrainMovementModel(TestContent.Terrain), TestContent.Terrain);

            double sumSearch = 0, sumHybrid = 0, sumSnapped = 0, sumOffset = 0;
            int bends = 0, orders = 0, snappedBeat = 0;

            for (int i = 0; i < 12; i++)
            {
                Vec2 to = new Vec2(200f + (i % 4) * 180f, 200f + (i / 4) * 160f + 60f);

                Plan search = Marching.PlanTo(
                    battle, mover, pathfinder, to, planner: RoutePlanners.TheTangents);
                Plan hybrid = Marching.PlanTo(
                    battle, mover, pathfinder, to, planner: RoutePlanners.TheHybridAStar);

                if (!search.Path.Found || !hybrid.Path.Found) continue;

                orders++;

                float searchSeconds =
                    Marching.SecondsToWalk(battle, mover, search.Path.Waypoints, search.Hold);
                float hybridSeconds =
                    Marching.SecondsToWalk(battle, mover, hybrid.Path.Waypoints, hybrid.Hold);

                IReadOnlyList<Vec2> places = RouteSearch.DebugCandidatePlaces(battle, mover, to);

                // The hybrid's own shape, but bending only where the search
                // could have bent. Duplicates collapse, so a smooth curve
                // through one place becomes one waypoint.
                var snapped = new List<Vec2> { mover.Position };

                for (int w = 1; w < hybrid.Path.Waypoints.Count - 1; w++)
                {
                    Vec2 at = hybrid.Path.Waypoints[w];

                    Vec2 nearest = at;
                    float closest = float.MaxValue;

                    foreach (Vec2 place in places)
                    {
                        float d = Vec2.DistanceSquared(place, at);
                        if (d < closest) { closest = d; nearest = place; }
                    }

                    sumOffset += MathF.Sqrt(closest);
                    bends++;

                    if (Vec2.Distance(snapped[^1], nearest) > 1f) snapped.Add(nearest);
                }

                snapped.Add(to);

                float snappedSeconds = Marching.SecondsToWalk(battle, mover, snapped, null);

                sumSearch += searchSeconds;
                sumHybrid += hybridSeconds;
                sumSnapped += snappedSeconds;

                if (snappedSeconds < searchSeconds - 0.5f) snappedBeat++;

                _out.WriteLine(
                    $"order {i,2}: search {searchSeconds,6:0.0}s   hybrid {hybridSeconds,6:0.0}s   " +
                    $"hybrid snapped to the search's places {snappedSeconds,6:0.0}s   " +
                    $"({places.Count} places, {hybrid.Path.Waypoints.Count} hybrid waypoints)");
            }

            _out.WriteLine(string.Empty);
            _out.WriteLine(
                $"{orders} orders — search {sumSearch / orders:0.0}s   hybrid {sumHybrid / orders:0.0}s   " +
                $"snapped {sumSnapped / orders:0.0}s");
            _out.WriteLine(
                $"the hybrid bends a mean {sumOffset / Math.Max(1, bends):0.0} m from the nearest place " +
                $"the search would have considered");
            _out.WriteLine(
                $"snapped route still beats the search's own: {snappedBeat} of {orders}");
        }
    }
}
