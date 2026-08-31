using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using BattleChess.Contracts;
using BattleChess.Rules;
using BattleChess.Rules.GridPlanning;
using BattleChess.Rules.HybridPlanning;
using Xunit;
using Xunit.Abstractions;

namespace BattleChess.Tests.Battle
{
    /// <summary>
    /// What A* over regiment-sized cells costs, and how often it answers.
    /// </summary>
    [Collection(PlannerLevers.Name)]
    public sealed class RegimentGridTests
    {
        private readonly ITestOutputHelper _out;

        public RegimentGridTests(ITestOutputHelper output) => _out = output;

        [Fact(Skip = "A record of a measurement, not a check on one — it drives global levers.")]
        public void WhatTheGridCostsAndHowOftenItAnswers()
        {
            float wasSpacing = RegimentGrid.SpacingMultiple;

            try
            {
                _out.WriteLine("A* over cells sized to a regiment, on the same 80 orders a field.");
                _out.WriteLine("'held' = the route passed WalksCleanly, the gate every route must pass.");
                _out.WriteLine(string.Empty);

                foreach (string field in new[] { "crucible", "brokencountry", "longmarch" })
                {
                    _out.WriteLine($"=== {field} ===");
                    _out.WriteLine(
                        "cell      spacing   cells  blocked   found   held  smoothed   points  worst ms  total ms");
                    _out.WriteLine(new string('-', 76));

                    foreach (float multiple in new[] { 0.75f, 1f, 1.5f, 2f })
                    {
                        RegimentGrid.SpacingMultiple = multiple;
                        Run(field, multiple);
                    }

                    _out.WriteLine(string.Empty);
                }
            }
            finally
            {
                RegimentGrid.SpacingMultiple = wasSpacing;
            }
        }

        private void Run(string key, float multiple)
        {
            BattleState battle = BenchScenariosTests.Load(key);
            var units = battle.UnitsOnField().ToList();

            MapBounds bounds = battle.Terrain.Bounds;
            var everybodyTo = new Vec2(
                bounds.Min.X + (bounds.Max.X - bounds.Min.X) * 0.78f,
                bounds.Min.Y + (bounds.Max.Y - bounds.Min.Y) * 0.5f);

            int found = 0, held = 0, cells = 0, blocked = 0, smoothed = 0, waypoints = 0;
            double worst = 0, total = 0;

            for (int i = 0; i < units.Count; i++)
            {
                UnitInstance unit = units[i];
                const int across = 10;
                Vec2 destination = everybodyTo + new Vec2(
                    (i % across - across * 0.5f) * 55f,
                    (i / across - units.Count / (across * 2f)) * 55f);

                var watch = Stopwatch.StartNew();
                RegimentGrid grid = RegimentGrid.For(battle, unit);
                bool got = grid.TryRoute(unit.Position, destination, out List<Vec2> route);
                watch.Stop();

                double ms = watch.Elapsed.TotalMilliseconds;
                total += ms;
                if (ms > worst) worst = ms;

                blocked = grid.BlockedCells;

                if (i == 0)
                {
                    var all = new List<Coord>();
                    grid.Snapshot(all);
                    cells = all.Count;
                }

                if (!got) continue;

                found++;

                float length = GridLength(route);

                var plan = new Plan(
                    PathResult.Success(route, Array.Empty<Coord>(), length, length, 0),
                    null, false);

                if (StagedRoutePlanner.WalksCleanly(battle, unit, plan)) held++;

                // A hex route is a chain of cell centres and zigzags by
                // construction, which is precisely what the cast-ahead pass
                // exists to straighten. Gating the raw line asks the swept
                // rectangle to walk a staircase; gating the smoothed one asks
                // it to walk what the regiment would actually be given.
                Plan straightened = RouteSmoothing.Applied(battle, unit, plan);

                if (StagedRoutePlanner.WalksCleanly(battle, unit, straightened))
                {
                    smoothed++;
                    waypoints += straightened.Path.Waypoints.Count;
                }
            }

            _out.WriteLine(
                $"{multiple,4:0.00}x {RegimentGrid.SpacingMultiple * 40.4f,9:0.#} m {cells,7} " +
                $"{blocked,8} {found,7} {held,6} {smoothed,9} " +
                $"{(smoothed > 0 ? waypoints / (float)smoothed : 0f),7:0.0} " +
                $"{worst,9:0.00} {total,9:0.0}");
        }

        /// <summary>
        /// Why a blob of cells is blocked around one regiment: how far the
        /// nearest blocked cell is from the mover, against how far the nearest
        /// other body is.
        /// </summary>
        [Fact(Skip = "A record of a measurement, not a check on one.")]
        public void WhatIsActuallyBlockingTheCellsRoundOneRegiment()
        {
            BattleState battle = BenchScenariosTests.Load("crucible");
            var units = battle.UnitsOnField().ToList();

            _out.WriteLine($"mover footprint, spacing, blocked cells, nearest blocked cell to the");
            _out.WriteLine($"mover's own centre, and the nearest OTHER body's centre and edge.");
            _out.WriteLine(string.Empty);
            _out.WriteLine("unit          w x d      spacing  blocked   near cell   near body   near edge");
            _out.WriteLine(new string('-', 82));

            for (int i = 0; i < 6 && i < units.Count; i++)
            {
                UnitInstance mover = units[i];
                RegimentGrid grid = RegimentGrid.For(battle, mover);

                var all = new List<Coord>();
                grid.Snapshot(all);

                float nearestCell = float.PositiveInfinity;

                foreach (Coord cell in all)
                {
                    if (grid.StateOf(cell) != CellState.Body) continue;

                    float d = Vec2.Distance(mover.Position, grid.Layout.ToWorld(cell));
                    if (d < nearestCell) nearestCell = d;
                }

                float nearestBody = float.PositiveInfinity;
                float nearestEdge = float.PositiveInfinity;

                foreach (UnitInstance other in battle.UnitsOnField())
                {
                    if (other.Id == mover.Id) continue;

                    float d = Vec2.Distance(mover.Position, other.Shape.Centre);
                    if (d < nearestBody) nearestBody = d;

                    float gap = OrientedRect.GapBetween(mover.Shape, other.Shape);
                    if (gap < nearestEdge) nearestEdge = gap;
                }

                _out.WriteLine(
                    $"{mover.Def.DisplayName,-12} {mover.Shape.Footprint.Width,4:0.#} x " +
                    $"{mover.Shape.Footprint.Depth,-4:0.#} {grid.Spacing,8:0.#} {grid.BlockedCells,8} " +
                    $"{nearestCell,11:0.#} {nearestBody,11:0.#} {nearestEdge,11:0.#}");
            }
        }

        /// <summary>
        /// The ground a regiment really covers, which decides whether a cell
        /// sized to one can ignore its heading.
        /// </summary>
        [Fact(Skip = "A record of a measurement, not a check on one.")]
        public void HowMuchWiderIsARegimentTurnedThanStandingStill()
        {
            _out.WriteLine("field          unit         formation      w x d    circle   circle/w");
            _out.WriteLine(new string('-', 78));

            foreach (string field in new[] { "crucible", "brokencountry", "longmarch" })
            {
                BattleState battle = BenchScenariosTests.Load(field);
                var seen = new HashSet<string>();

                foreach (UnitInstance unit in battle.UnitsOnField())
                {
                    Footprint print = unit.Shape.Footprint;
                    string key = $"{unit.Def.DisplayName}/{unit.Formation.Ranks}r/{print.Width:0.#}x{print.Depth:0.#}";

                    if (!seen.Add(key)) continue;

                    _out.WriteLine(
                        $"{field,-14} {unit.Def.DisplayName,-12} {unit.Formation.Ranks,-10} " +
                        $"{print.Width,6:0.#} x {print.Depth,-5:0.#} {print.BoundingRadius * 2f,7:0.#} " +
                        $"{print.BoundingRadius * 2f / print.Width,9:0.00}x");
                }
            }
        }

        /// <summary>
        /// How much of the mover's circle the halo really needs, given that a
        /// true swept-rectangle gate stands behind this grid anyway.
        /// </summary>
        [Fact(Skip = "A record of a measurement, not a check on one — it drives global levers.")]
        public void HowBigTheHaloRoundEveryBodyHasToBe()
        {
            float wasFraction = RegimentGrid.ClearanceFraction;

            try
            {
                _out.WriteLine("Halo round each body, as a fraction of the mover's circumscribed radius.");
                _out.WriteLine("1,00 keeps a circle that fits it broadside; 0,00 keeps its own half-depth.");
                _out.WriteLine("'held' is after smoothing, through the same gate every route must pass.");
                _out.WriteLine(string.Empty);

                foreach (string field in new[] { "crucible", "brokencountry", "longmarch" })
                {
                    _out.WriteLine($"=== {field} \u2014 80 orders ===");
                    _out.WriteLine("halo    blocked   found    held   worst ms   total ms");
                    _out.WriteLine(new string('-', 58));

                    foreach (float fraction in new[] { 1f, 0.75f, 0.5f, 0.25f, 0f })
                    {
                        RegimentGrid.ClearanceFraction = fraction;
                        Halo(field, fraction);
                    }

                    _out.WriteLine(string.Empty);
                }
            }
            finally
            {
                RegimentGrid.ClearanceFraction = wasFraction;
            }
        }

        private void Halo(string key, float fraction)
        {
            BattleState battle = BenchScenariosTests.Load(key);
            var units = battle.UnitsOnField().ToList();

            MapBounds bounds = battle.Terrain.Bounds;
            var everybodyTo = new Vec2(
                bounds.Min.X + (bounds.Max.X - bounds.Min.X) * 0.78f,
                bounds.Min.Y + (bounds.Max.Y - bounds.Min.Y) * 0.5f);

            int found = 0, held = 0, blocked = 0;
            double worst = 0, total = 0;

            for (int i = 0; i < units.Count; i++)
            {
                UnitInstance unit = units[i];
                const int across = 10;
                Vec2 destination = everybodyTo + new Vec2(
                    (i % across - across * 0.5f) * 55f,
                    (i / across - units.Count / (across * 2f)) * 55f);

                var watch = Stopwatch.StartNew();
                RegimentGrid grid = RegimentGrid.For(battle, unit);
                bool got = grid.TryRoute(unit.Position, destination, out List<Vec2> route);
                watch.Stop();

                double ms = watch.Elapsed.TotalMilliseconds;
                total += ms;
                if (ms > worst) worst = ms;
                blocked = grid.BlockedCells;

                if (!got) continue;

                found++;

                float length = GridLength(route);
                var plan = new Plan(
                    PathResult.Success(route, Array.Empty<Coord>(), length, length, 0),
                    null, false);

                if (StagedRoutePlanner.WalksCleanly(
                        battle, unit, RouteSmoothing.Applied(battle, unit, plan)))
                    held++;
            }

            _out.WriteLine(
                $"{fraction,4:0.00} {blocked,10} {found,7} {held,7} {worst,10:0.00} {total,10:0.0}");
        }

        /// <summary>
        /// The whole cascade with the grid in each of its three places, against
        /// the cascade without it. The only measurement that answers what it is
        /// worth, because every other one here weighs the grid on its own.
        /// </summary>
        [Fact(Skip = "A record of a measurement, not a check on one — it drives global levers.")]
        public void WhatTheGridIsWorthInTheCascade()
        {
            GridUse wasUse = GridRoutePlanner.Use;

            try
            {
                _out.WriteLine("The full staged planner, 80 one-click orders a field.");
                _out.WriteLine("'lattice' = orders that reached the pose search. 'press' = orders that");
                _out.WriteLine("shouldered through. 'route s' = the seconds the walker will spend, so");
                _out.WriteLine("lower is better and a rise means the grid bought speed with quality.");
                _out.WriteLine(string.Empty);

                foreach (string field in new[] { "crucible", "brokencountry", "longmarch" })
                {
                    _out.WriteLine($"=== {field} \u2014 80 orders ===");
                    _out.WriteLine(
                        "grid       worst ms  total ms   lattice  gridwon   press   clean   route s   timeouts");
                    _out.WriteLine(new string('-', 96));

                    foreach (GridUse use in new[]
                             { GridUse.Off, GridUse.Stage, GridUse.Corridor, GridUse.Replace })
                    {
                        GridRoutePlanner.Use = use;
                        EndToEnd(field, use);
                    }

                    _out.WriteLine(string.Empty);
                }
            }
            finally
            {
                GridRoutePlanner.Use = wasUse;
            }
        }

        private void EndToEnd(string key, GridUse use)
        {
            BattleState battle = BenchScenariosTests.Load(key);
            var units = battle.UnitsOnField().ToList();

            MapBounds bounds = battle.Terrain.Bounds;
            var everybodyTo = new Vec2(
                bounds.Min.X + (bounds.Max.X - bounds.Min.X) * 0.78f,
                bounds.Min.Y + (bounds.Max.Y - bounds.Min.Y) * 0.5f);

            IPathfinder pathfinder = new DirectPathfinder(
                battle.Terrain, new TerrainMovementModel(TestContent.Terrain), TestContent.Terrain,
                clearanceMetres: HexPathfinder.DefaultClearanceMetres);

            StagedRoutePlanner.ResetCounters();

            double worst = 0, total = 0;
            float seconds = 0;
            int clean = 0;

            for (int i = 0; i < units.Count; i++)
            {
                UnitInstance unit = units[i];
                const int across = 10;
                Vec2 destination = everybodyTo + new Vec2(
                    (i % across - across * 0.5f) * 55f,
                    (i / across - units.Count / (across * 2f)) * 55f);

                Facing arriveOn = Marching.AlongTheLine(unit.Position, destination, unit.Facing);

                var watch = Stopwatch.StartNew();
                Plan plan = Marching.PlanTo(
                    battle, unit, pathfinder, destination,
                    planner: RoutePlanners.TheStaged, arriveOn: arriveOn);
                watch.Stop();

                double ms = watch.Elapsed.TotalMilliseconds;
                total += ms;
                if (ms > worst) worst = ms;

                if (!plan.Path.Found) continue;

                seconds += Marching.SecondsToWalk(battle, unit, plan.Path.Waypoints, plan.Hold);

                if (!plan.PressedThrough) clean++;
            }

            _out.WriteLine(
                $"{use,-10} {worst,8:0.0} {total,9:0.0} {StagedRoutePlanner.PoseAsked,9} " +
                $"{StagedRoutePlanner.GridClean,8} {StagedRoutePlanner.Pressed,7} {clean,7} " +
                $"{seconds,9:0} {HybridAStarPlanner.RanOutOfTime,10}");
        }

        /// <summary>
        /// How much of the map one grid search actually settles, which is what
        /// decides whether searching the whole field matters.
        /// </summary>
        [Fact(Skip = "A record of a measurement, not a check on one — it drives global levers.")]
        public void HowMuchOfTheMapOneSearchTouches()
        {
            _out.WriteLine("A* is goal-directed, so it settles what it needs rather than the field.");
            _out.WriteLine(string.Empty);
            _out.WriteLine("field           cells   mean settled   worst settled   worst as % of map");
            _out.WriteLine(new string('-', 78));

            foreach (string field in new[] { "crucible", "brokencountry", "longmarch" })
            {
                BattleState battle = BenchScenariosTests.Load(field);
                var units = battle.UnitsOnField().ToList();

                MapBounds bounds = battle.Terrain.Bounds;
                var everybodyTo = new Vec2(
                    bounds.Min.X + (bounds.Max.X - bounds.Min.X) * 0.78f,
                    bounds.Min.Y + (bounds.Max.Y - bounds.Min.Y) * 0.5f);

                int cells = 0, worstSettled = 0;
                long settled = 0;

                for (int i = 0; i < units.Count; i++)
                {
                    UnitInstance unit = units[i];
                    const int across = 10;
                    Vec2 destination = everybodyTo + new Vec2(
                        (i % across - across * 0.5f) * 55f,
                        (i / across - units.Count / (across * 2f)) * 55f);

                    RegimentGrid grid = RegimentGrid.For(battle, unit);
                    grid.TryRoute(unit.Position, destination, out _);

                    settled += RegimentGrid.LastCellsExplored;
                    if (RegimentGrid.LastCellsExplored > worstSettled)
                    {
                        worstSettled = RegimentGrid.LastCellsExplored;

                        var all = new List<Coord>();
                        grid.Snapshot(all);
                        cells = all.Count;
                    }
                }

                _out.WriteLine(
                    $"{field,-14} {cells,7} {settled / (float)units.Count,14:0} " +
                    $"{worstSettled,15} {worstSettled * 100f / MathF.Max(1, cells),18:0.0}%");
            }
        }

        /// <summary>
        /// A circle round every body against the mover's own rectangle squared
        /// to the line of march, and what each half of the work costs.
        /// </summary>
        [Fact(Skip = "A record of a measurement, not a check on one — it drives global levers.")]
        public void ACircleRoundEveryBodyAgainstTheMoversRectangle()
        {
            HaloShape wasHalo = RegimentGrid.Halo;
            float wasFraction = RegimentGrid.ClearanceFraction;
            GridUse wasUse = GridRoutePlanner.Use;

            try
            {
                _out.WriteLine("Halo shape, in the grid alone and then through the whole cascade.");
                _out.WriteLine("'build' lays the grid and marks the bodies; 'search' is the A* itself.");
                _out.WriteLine(string.Empty);

                foreach (string field in new[] { "crucible", "brokencountry", "longmarch" })
                {
                    _out.WriteLine($"=== {field} \u2014 80 orders ===");
                    _out.WriteLine(
                        "halo              blocked  found   held   build ms  search ms   worst ms");
                    _out.WriteLine(new string('-', 82));

                    RegimentGrid.Halo = HaloShape.Circle;
                    RegimentGrid.ClearanceFraction = 1f;
                    Shape(field, "circle, full");

                    RegimentGrid.ClearanceFraction = 0.75f;
                    Shape(field, "circle, 0.75");

                    RegimentGrid.Halo = HaloShape.Rectangle;
                    Shape(field, "rectangle");

                    _out.WriteLine(string.Empty);
                }

                _out.WriteLine("And the same three through the full staged planner:");
                _out.WriteLine(string.Empty);

                GridRoutePlanner.Use = GridUse.Stage;

                foreach (string field in new[] { "crucible", "brokencountry", "longmarch" })
                {
                    _out.WriteLine($"=== {field} \u2014 80 orders ===");
                    _out.WriteLine(
                        "halo         worst ms  total ms   lattice  gridwon   press   clean   route s");
                    _out.WriteLine(new string('-', 88));

                    RegimentGrid.Halo = HaloShape.Circle;
                    RegimentGrid.ClearanceFraction = 0.75f;
                    Cascade(field, "circle 0.75");

                    RegimentGrid.Halo = HaloShape.Rectangle;
                    Cascade(field, "rectangle");

                    _out.WriteLine(string.Empty);
                }
            }
            finally
            {
                RegimentGrid.Halo = wasHalo;
                RegimentGrid.ClearanceFraction = wasFraction;
                GridRoutePlanner.Use = wasUse;
            }
        }

        private void Shape(string key, string name)
        {
            BattleState battle = BenchScenariosTests.Load(key);
            var units = battle.UnitsOnField().ToList();

            MapBounds bounds = battle.Terrain.Bounds;
            var everybodyTo = new Vec2(
                bounds.Min.X + (bounds.Max.X - bounds.Min.X) * 0.78f,
                bounds.Min.Y + (bounds.Max.Y - bounds.Min.Y) * 0.5f);

            int found = 0, held = 0, blocked = 0;
            double build = 0, search = 0, worst = 0;

            for (int i = 0; i < units.Count; i++)
            {
                UnitInstance unit = units[i];
                const int across = 10;
                Vec2 destination = everybodyTo + new Vec2(
                    (i % across - across * 0.5f) * 55f,
                    (i / across - units.Count / (across * 2f)) * 55f);

                Facing along = Marching.AlongTheLine(unit.Position, destination, unit.Facing);

                var watch = Stopwatch.StartNew();
                RegimentGrid grid = RegimentGrid.For(battle, unit, along);
                watch.Stop();
                double built = watch.Elapsed.TotalMilliseconds;
                build += built;

                watch.Restart();
                bool got = grid.TryRoute(unit.Position, destination, out List<Vec2> route);
                watch.Stop();
                search += watch.Elapsed.TotalMilliseconds;

                if (built + watch.Elapsed.TotalMilliseconds > worst)
                    worst = built + watch.Elapsed.TotalMilliseconds;

                blocked = grid.BlockedCells;

                if (!got) continue;

                found++;

                float length = GridLength(route);
                var plan = new Plan(
                    PathResult.Success(route, Array.Empty<Coord>(), length, length, 0),
                    null, false);

                if (StagedRoutePlanner.WalksCleanly(
                        battle, unit, RouteSmoothing.Applied(battle, unit, plan)))
                    held++;
            }

            _out.WriteLine(
                $"{name,-16} {blocked,8} {found,6} {held,6} {build,10:0.0} {search,10:0.0} {worst,10:0.00}");
        }

        private void Cascade(string key, string name)
        {
            BattleState battle = BenchScenariosTests.Load(key);
            var units = battle.UnitsOnField().ToList();

            MapBounds bounds = battle.Terrain.Bounds;
            var everybodyTo = new Vec2(
                bounds.Min.X + (bounds.Max.X - bounds.Min.X) * 0.78f,
                bounds.Min.Y + (bounds.Max.Y - bounds.Min.Y) * 0.5f);

            IPathfinder pathfinder = new DirectPathfinder(
                battle.Terrain, new TerrainMovementModel(TestContent.Terrain), TestContent.Terrain,
                clearanceMetres: HexPathfinder.DefaultClearanceMetres);

            StagedRoutePlanner.ResetCounters();

            double worst = 0, total = 0;
            float seconds = 0;
            int clean = 0;

            for (int i = 0; i < units.Count; i++)
            {
                UnitInstance unit = units[i];
                const int across = 10;
                Vec2 destination = everybodyTo + new Vec2(
                    (i % across - across * 0.5f) * 55f,
                    (i / across - units.Count / (across * 2f)) * 55f);

                Facing arriveOn = Marching.AlongTheLine(unit.Position, destination, unit.Facing);

                var watch = Stopwatch.StartNew();
                Plan plan = Marching.PlanTo(
                    battle, unit, pathfinder, destination,
                    planner: RoutePlanners.TheStaged, arriveOn: arriveOn);
                watch.Stop();

                double ms = watch.Elapsed.TotalMilliseconds;
                total += ms;
                if (ms > worst) worst = ms;

                if (!plan.Path.Found) continue;

                seconds += Marching.SecondsToWalk(battle, unit, plan.Path.Waypoints, plan.Hold);
                if (!plan.PressedThrough) clean++;
            }

            _out.WriteLine(
                $"{name,-12} {worst,8:0.0} {total,9:0.0} {StagedRoutePlanner.PoseAsked,9} " +
                $"{StagedRoutePlanner.GridClean,8} {StagedRoutePlanner.Pressed,7} {clean,7} {seconds,9:0}");
        }

        /// <summary>
        /// One field kept and shared against one built per order, and cells
        /// sampled at several points against only at their centre.
        /// </summary>
        [Fact(Skip = "A record of a measurement, not a check on one — it drives global levers.")]
        public void KeepingTheFieldAndSamplingTheCells()
        {
            bool wasReuse = RegimentGrid.Reuse;
            int wasSamples = RegimentGrid.SubSamples;
            float wasFill = RegimentGrid.FillToBlock;
            GridUse wasUse = GridRoutePlanner.Use;

            try
            {
                _out.WriteLine("Per order: what the grid costs, and what the whole cascade costs.");
                _out.WriteLine("'built' is fields laid from nothing; 'reused' is orders that found one.");
                _out.WriteLine(string.Empty);

                GridRoutePlanner.Use = GridUse.Stage;

                foreach (string field in new[] { "crucible", "brokencountry", "longmarch" })
                {
                    _out.WriteLine($"=== {field} \u2014 80 orders ===");
                    _out.WriteLine(
                        "arrangement          built reused   found  held   grid ms/order  order ms  worst ms");
                    _out.WriteLine(new string('-', 96));

                    RegimentGrid.Reuse = false;
                    RegimentGrid.SubSamples = 1;
                    RegimentGrid.FillToBlock = 1f;
                    Both(field, "rebuild, 1 sample");

                    RegimentGrid.Reuse = true;
                    Both(field, "keep, 1 sample");

                    RegimentGrid.SubSamples = 7;

                    foreach (float fill in new[] { 1f, 0.86f, 0.75f, 0.5f, 0.35f, 0.25f, 0.14f })
                    {
                        RegimentGrid.FillToBlock = fill;
                        Both(field, $"keep, 7, {fill * 100f:0}% gone");
                    }

                    RegimentGrid.SubSamples = 19;

                    foreach (float fill in new[] { 0.5f, 0.25f, 0.1f })
                    {
                        RegimentGrid.FillToBlock = fill;
                        Both(field, $"keep, 19, {fill * 100f:0}% gone");
                    }

                    _out.WriteLine(string.Empty);
                }
            }
            finally
            {
                RegimentGrid.Reuse = wasReuse;
                RegimentGrid.SubSamples = wasSamples;
                RegimentGrid.FillToBlock = wasFill;
                GridRoutePlanner.Use = wasUse;
                RegimentGrid.Forget();
            }
        }

        private void Both(string key, string name)
        {
            BattleState battle = BenchScenariosTests.Load(key);
            var units = battle.UnitsOnField().ToList();

            MapBounds bounds = battle.Terrain.Bounds;
            var everybodyTo = new Vec2(
                bounds.Min.X + (bounds.Max.X - bounds.Min.X) * 0.78f,
                bounds.Min.Y + (bounds.Max.Y - bounds.Min.Y) * 0.5f);

            IPathfinder pathfinder = new DirectPathfinder(
                battle.Terrain, new TerrainMovementModel(TestContent.Terrain), TestContent.Terrain,
                clearanceMetres: HexPathfinder.DefaultClearanceMetres);

            RegimentGrid.Forget();
            RegimentGrid.FieldsBuilt = RegimentGrid.FieldsReused = 0;
            StagedRoutePlanner.ResetCounters();

            int found = 0, held = 0;
            double gridMs = 0, orderMs = 0, worst = 0;

            for (int i = 0; i < units.Count; i++)
            {
                UnitInstance unit = units[i];
                const int across = 10;
                Vec2 destination = everybodyTo + new Vec2(
                    (i % across - across * 0.5f) * 55f,
                    (i / across - units.Count / (across * 2f)) * 55f);

                Facing arriveOn = Marching.AlongTheLine(unit.Position, destination, unit.Facing);

                var watch = Stopwatch.StartNew();
                RegimentGrid grid = RegimentGrid.For(battle, unit, arriveOn);
                bool got = grid.TryRoute(unit.Position, destination, out List<Vec2> route);
                watch.Stop();
                gridMs += watch.Elapsed.TotalMilliseconds;

                if (got)
                {
                    found++;

                    float length = GridLength(route);
                    var plan = new Plan(
                        PathResult.Success(route, Array.Empty<Coord>(), length, length, 0),
                        null, false);

                    if (StagedRoutePlanner.WalksCleanly(
                            battle, unit, RouteSmoothing.Applied(battle, unit, plan)))
                        held++;
                }

                watch.Restart();
                Marching.PlanTo(
                    battle, unit, pathfinder, destination,
                    planner: RoutePlanners.TheStaged, arriveOn: arriveOn);
                watch.Stop();

                orderMs += watch.Elapsed.TotalMilliseconds;
                if (watch.Elapsed.TotalMilliseconds > worst) worst = watch.Elapsed.TotalMilliseconds;
            }

            _out.WriteLine(
                $"{name,-20} {RegimentGrid.FieldsBuilt,5} {RegimentGrid.FieldsReused,6} " +
                $"{found,7} {held,5} {gridMs / units.Count,14:0.000} " +
                $"{orderMs / units.Count,9:0.0} {worst,9:0.0}");
        }

        /// <summary>
        /// What "fill" actually measures, and how many cells are ever partly
        /// filled at all.
        /// </summary>
        [Fact]
        public void HowMuchOfACellIsEverPartlyCovered()
        {
            int wasSamples = RegimentGrid.SubSamples;

            try
            {
                RegimentGrid.SubSamples = 7;

                _out.WriteLine("Fill is coverage by the body PLUS the mover's halo, not by the");
                _out.WriteLine("drawn rectangle. The halo is what decides where a centre may go.");
                _out.WriteLine(string.Empty);

                foreach (string field in new[] { "crucible", "brokencountry" })
                {
                    BattleState battle = BenchScenariosTests.Load(field);
                    UnitInstance mover = battle.UnitsOnField().ToList()[0];

                    Footprint print = mover.Shape.Footprint;
                    float reach =
                        print.HalfDepth +
                        (print.BoundingRadius - print.HalfDepth) * RegimentGrid.ClearanceFraction +
                        RegimentGrid.MarginMetres;

                    RegimentGrid grid = RegimentGrid.For(battle, mover);

                    var all = new List<Coord>();
                    grid.Snapshot(all);

                    var buckets = new int[8];
                    int touched = 0;

                    foreach (Coord cell in all)
                    {
                        float fill = grid.FillAt(cell);
                        if (fill <= 0f) continue;

                        touched++;
                        buckets[Math.Min(7, (int)(fill * 7.999f))]++;
                    }

                    _out.WriteLine($"=== {field} \u2014 {mover.Def.DisplayName} ===");
                    _out.WriteLine(
                        $"body {print.Width:0.#} x {print.Depth:0.#} m, halo {reach:0.#} m, " +
                        $"so the reserved rectangle is {print.Width + 2 * reach:0.#} x " +
                        $"{print.Depth + 2 * reach:0.#} m against a {grid.Spacing:0.#} m cell.");
                    _out.WriteLine(
                        $"{all.Count} cells on the field, {touched} with any coverage at all.");
                    _out.WriteLine(string.Empty);
                    _out.WriteLine("fill      cells");

                    for (int i = 0; i < buckets.Length; i++)
                        _out.WriteLine($"{i / 8f:0.00}-{(i + 1) / 8f:0.00} {buckets[i],8}");

                    int partial = 0;
                    for (int i = 0; i < 7; i++) partial += buckets[i];

                    _out.WriteLine(
                        $"fully covered {buckets[7]}, partly {partial} " +
                        $"({partial * 100f / MathF.Max(1, touched):0}% of what is touched).");
                    _out.WriteLine(string.Empty);
                }
            }
            finally
            {
                RegimentGrid.SubSamples = wasSamples;
            }
        }

        /// <summary>
        /// What the halo refuses that a body alone would not, and how much of
        /// that refusal an exact fit test would give back.
        /// </summary>
        [Fact]
        public void WhatTheHaloRefusesThatABodyWouldNot()
        {
            _out.WriteLine("Three ways to ask whether a cell may hold a regiment's centre:");
            _out.WriteLine("  body   - is the sample inside a body as drawn (measure fill, no halo)");
            _out.WriteLine("  halo   - inside a body grown by one radius every way (what ships)");
            _out.WriteLine("  fit    - would the mover's own rectangle, put there at its");
            _out.WriteLine("           travelling front, actually overlap a body (exact)");
            _out.WriteLine(string.Empty);

            foreach (string field in new[] { "crucible", "brokencountry", "longmarch" })
            {
                BattleState battle = BenchScenariosTests.Load(field);
                List<UnitInstance> units = battle.UnitsOnField().ToList();
                UnitInstance mover = units[0];

                Footprint print = mover.Shape.Footprint;
                float reach =
                    print.HalfDepth +
                    (print.BoundingRadius - print.HalfDepth) * RegimentGrid.ClearanceFraction +
                    RegimentGrid.MarginMetres;

                RegimentGrid grid = RegimentGrid.For(battle, mover);

                var cells = new List<Coord>();
                grid.Snapshot(cells);

                Vec2[] offsets = SampleOffsets(grid.Layout.CellSize);

                var raw = new List<OrientedRect>();
                var fat = new List<OrientedRect>();

                foreach (UnitInstance body in units)
                {
                    if (body.Id == mover.Id) continue;

                    OrientedRect at = body.Shape;
                    raw.Add(at);
                    fat.Add(new OrientedRect(
                        at.Centre, at.Facing,
                        new Footprint(
                            at.Footprint.Width + 2f * reach,
                            at.Footprint.Depth + 2f * reach)));
                }

                int byBody = 0, byHalo = 0, byFit = 0, opened = 0, wrongly = 0;

                foreach (Coord cell in cells)
                {
                    Vec2 centre = grid.Layout.ToWorld(cell);
                    int inBody = 0, inHalo = 0, noFit = 0;

                    foreach (Vec2 offset in offsets)
                    {
                        var at = new Vec2(centre.X + offset.X, centre.Y + offset.Y);
                        var me = new OrientedRect(at, mover.Facing, print);

                        bool body = false, halo = false, fit = false;

                        for (int i = 0; i < raw.Count && !(body && halo && fit); i++)
                        {
                            body |= raw[i].ContainsPoint(at);
                            halo |= fat[i].ContainsPoint(at);
                            fit |= OrientedRect.Overlaps(me, raw[i]);
                        }

                        if (body) inBody++;
                        if (halo) inHalo++;
                        if (fit) noFit++;
                    }

                    float n = offsets.Length;
                    bool blockedBody = inBody / n >= RegimentGrid.FillToBlock;
                    bool blockedHalo = inHalo / n >= RegimentGrid.FillToBlock;
                    bool blockedFit  = noFit  / n >= RegimentGrid.FillToBlock;

                    if (blockedBody) byBody++;
                    if (blockedHalo) byHalo++;
                    if (blockedFit) byFit++;

                    // Cells measuring bodies alone would walk into.
                    if (!blockedBody && blockedFit) wrongly++;

                    // Cells the exact test gives back that the halo refused.
                    if (blockedHalo && !blockedFit) opened++;
                }

                _out.WriteLine($"=== {field} — {cells.Count} cells, mover {print.Width:0.#} x {print.Depth:0.#} m, halo {reach:0.#} m ===");
                _out.WriteLine($"  refused by body alone : {byBody,5}");
                _out.WriteLine($"  refused by the halo   : {byHalo,5}   (what you see red)");
                _out.WriteLine($"  refused by exact fit  : {byFit,5}");
                _out.WriteLine($"  halo over-refuses     : {opened,5} cells an exact fit would open");
                _out.WriteLine($"  body alone under-refuses: {wrongly,3} cells where a centre placed there overlaps a regiment");
                _out.WriteLine(string.Empty);
            }
        }

        /// <summary>The same offsets the field samples a cell at.</summary>
        private static Vec2[] SampleOffsets(float cellSize)
        {
            var offsets = new List<Vec2> { Vec2.Zero };

            for (int i = 0; i < 6; i++)
            {
                float angle = MathF.PI * 2f / 6f * i;
                offsets.Add(new Vec2(
                    cellSize * 0.55f * MathF.Cos(angle),
                    cellSize * 0.55f * MathF.Sin(angle)));
            }

            return offsets.ToArray();
        }

        /// <summary>
        /// What every order actually costs against just walking there, which is
        /// the thing no stage of the cascade currently prices on the way out.
        /// </summary>
        [Fact]
        public void HowManyOrdersCostFarMoreThanWalkingStraightThere()
        {
            _out.WriteLine("Planned seconds against the seconds the same walk would take on an");
            _out.WriteLine("empty field. A way round is honest at 1-2x; past 3x the regiment is");
            _out.WriteLine("refusing the order in all but name.");
            _out.WriteLine(string.Empty);

            foreach (string field in new[] { "crucible", "brokencountry", "longmarch" })
            {
                BattleState battle = BenchScenariosTests.Load(field);
                List<UnitInstance> units = battle.UnitsOnField().ToList();

                MapBounds bounds = battle.Terrain.Bounds;
                var everybodyTo = new Vec2(
                    bounds.Min.X + (bounds.Max.X - bounds.Min.X) * 0.78f,
                    bounds.Min.Y + (bounds.Max.Y - bounds.Min.Y) * 0.5f);

                IPathfinder pathfinder = new DirectPathfinder(
                    battle.Terrain, new TerrainMovementModel(TestContent.Terrain), TestContent.Terrain,
                    clearanceMetres: HexPathfinder.DefaultClearanceMetres);

                int over2 = 0, over3 = 0, over5 = 0, priced = 0;
                float worst = 0f;
                string worstAt = string.Empty;

                for (int i = 0; i < units.Count; i++)
                {
                    UnitInstance unit = units[i];
                    const int across = 10;
                    Vec2 destination = everybodyTo + new Vec2(
                        (i % across - across * 0.5f) * 55f,
                        (i / across - units.Count / (across * 2f)) * 55f);

                    Facing arriveOn = Marching.AlongTheLine(unit.Position, destination, unit.Facing);

                    Plan plan = Marching.PlanTo(
                        battle, unit, pathfinder, destination,
                        planner: RoutePlanners.TheStaged, arriveOn: arriveOn);

                    if (!plan.Path.Found) continue;

                    float straight = Marching.SecondsToWalk(
                        battle, unit, new[] { unit.Position, destination }, null);

                    if (straight <= 1f) continue;

                    float took = Marching.SecondsToWalk(battle, unit, plan.Path.Waypoints, plan.Hold);
                    float ratio = took / straight;

                    priced++;
                    if (ratio > 2f) over2++;
                    if (ratio > 3f) over3++;
                    if (ratio > 5f) over5++;

                    if (ratio > worst)
                    {
                        worst = ratio;
                        worstAt =
                            $"{unit.Def.DisplayName} ({unit.Position.X:0},{unit.Position.Y:0}) " +
                            $"to ({destination.X:0},{destination.Y:0}): " +
                            $"{took:0} s against {straight:0} s straight, " +
                            $"{plan.Path.Waypoints.Count} waypoints" +
                            (plan.PressedThrough ? ", pressing through." : ".");
                    }
                }

                _out.WriteLine($"=== {field} — {priced} orders priced ===");
                _out.WriteLine($"  over 2x straight : {over2,4}");
                _out.WriteLine($"  over 3x straight : {over3,4}");
                _out.WriteLine($"  over 5x straight : {over5,4}");
                _out.WriteLine($"  worst {worst:0.0}x — {worstAt}");
                _out.WriteLine(string.Empty);
            }
        }

        /// <summary>
        /// The arrangement the bench never produces and play constantly does: a
        /// regiment starting an order from where the last one left it, tangled
        /// in a friendly and facing the wrong way.
        /// </summary>
        [Fact]
        public void WhatAnOrderCostsFromAPoseTheLastOrderLeftBehind()
        {
            BattleState battle = BenchScenariosTests.Load("crucible");
            List<UnitInstance> units = battle.UnitsOnField().ToList();

            UnitInstance mover = units[0];
            UnitInstance friend = units
                .Where(u => u.Id != mover.Id && u.Owner == mover.Owner)
                .OrderBy(u => Vec2.Distance(u.Position, mover.Position))
                .First();

            Vec2 wasAt = mover.Position;
            Facing wasFacing = mover.Facing;

            IPathfinder pathfinder = new DirectPathfinder(
                battle.Terrain, new TerrainMovementModel(TestContent.Terrain), TestContent.Terrain,
                clearanceMetres: HexPathfinder.DefaultClearanceMetres);

            _out.WriteLine($"{mover.Def.DisplayName} ordered 188 m back past its own " +
                           $"{friend.Def.DisplayName}, from poses of rising wreckage.");
            _out.WriteLine(string.Empty);
            _out.WriteLine("overlap  front off   seconds  straight   ratio  ways  lattice  answered by");
            _out.WriteLine(new string('-', 84));

            try
            {
                foreach (float overlap in new[] { 0f, 0.05f, 0.20f })
                foreach (float turned in new[] { 0f, 90f, 177f })
                {
                    Vec2 side = friend.Facing.RightVector();
                    float apart = (friend.Shape.Footprint.HalfWidth + mover.Shape.Footprint.HalfWidth)
                                * (1f - overlap);

                    mover.Position = new Vec2(
                        friend.Position.X + side.X * apart,
                        friend.Position.Y + side.Y * apart);

                    // Back past the friendly, so the order cannot be answered
                    // by simply walking away from the crowd.
                    Vec2 destination = new Vec2(
                        mover.Position.X - side.X * 188f,
                        mover.Position.Y - side.Y * 188f);

                    Facing along = Facing.Towards(mover.Position, destination);
                    mover.Facing = Facing.FromDegrees(along.Degrees + turned);

                    StagedRoutePlanner.ResetCounters();
                    HybridAStarPlanner.LastExpansions = 0;

                    Facing arriveOn = Marching.AlongTheLine(mover.Position, destination, mover.Facing);
                    Plan plan = Marching.PlanTo(
                        battle, mover, pathfinder, destination,
                        planner: RoutePlanners.TheStaged, arriveOn: arriveOn);

                    float straight = Marching.SecondsToWalk(
                        battle, mover, new[] { mover.Position, destination }, null);
                    float took = plan.Path.Found
                        ? Marching.SecondsToWalk(battle, mover, plan.Path.Waypoints, plan.Hold)
                        : 0f;

                    string by =
                        StagedRoutePlanner.LadderClean > 0 ? "ladder" :
                        StagedRoutePlanner.LadderBent > 0 ? "bent ladder" :
                        StagedRoutePlanner.GridClean > 0 ? "grid" :
                        StagedRoutePlanner.TangentClean > 0 ? "tangents" :
                        StagedRoutePlanner.PoseWon > 0 ? "lattice" :
                        StagedRoutePlanner.Pressed > 0 ? "pressed through" :
                        plan.Path.Found ? "tangents, unpriced" : "nothing";

                    _out.WriteLine(
                        $"{overlap * 100f,6:0}% {turned,9:0}° {took,9:0} {straight,9:0} " +
                        $"{(straight > 1f ? took / straight : 0f),7:0.0}x {plan.Path.Waypoints.Count,5} " +
                        $"{HybridAStarPlanner.LastExpansions,8}  {by}  [{HybridAStarPlanner.LastStop}]");
                }
            }
            finally
            {
                mover.Position = wasAt;
                mover.Facing = wasFacing;
            }
        }

        /// <summary>
        /// What M76's wall clock costs in route quality. A recording has the
        /// lattice failing 11 times out of 11, 7 of them giving up on the
        /// clock at 128 expansions - two checks of it.
        /// </summary>
        [Fact]
        public void WhatTheWallClockOnEachSearchBuysAndWhatItCosts()
        {
            float wasClock = HybridAStarPlanner.MillisecondsPerSearch;
            int wasBudget = StagedRoutePlanner.PoseExpansionBudget;

            try
            {
                _out.WriteLine("The lattice is the last resort. What the clock on it is worth:");
                _out.WriteLine(string.Empty);

                foreach (string field in new[] { "crucible", "brokencountry", "longmarch" })
                {
                    _out.WriteLine($"=== {field} — 80 orders ===");
                    _out.WriteLine(
                        "  clock   asked   won  pressed  dear   over2x  mean ms  worst ms  all ms");
                    _out.WriteLine(new string('-', 78));

                    foreach (float clock in new[] { 15f, 60f })
                    {
                        HybridAStarPlanner.MillisecondsPerSearch = clock;
                        StagedRoutePlanner.PoseExpansionBudget = 20000;
                        ClockRun(field, $"{clock:0}ms");
                    }

                    // The same cut made on a count instead of a clock, which is
                    // the only kind two threads can agree on.
                    HybridAStarPlanner.MillisecondsPerSearch = 0f;

                    foreach (int budget in new[] { 2048, 4096, 8192, 16384 })
                    {
                        StagedRoutePlanner.PoseExpansionBudget = budget;
                        ClockRun(field, $"{budget}x");
                    }

                    StagedRoutePlanner.PoseExpansionBudget = 20000;

                    _out.WriteLine(string.Empty);
                }
            }
            finally
            {
                HybridAStarPlanner.MillisecondsPerSearch = wasClock;
                StagedRoutePlanner.PoseExpansionBudget = wasBudget;
                RegimentGrid.Forget();
            }
        }

        private void ClockRun(string key, string what)
        {
            BattleState battle = BenchScenariosTests.Load(key);
            List<UnitInstance> units = battle.UnitsOnField().ToList();

            MapBounds bounds = battle.Terrain.Bounds;
            var everybodyTo = new Vec2(
                bounds.Min.X + (bounds.Max.X - bounds.Min.X) * 0.78f,
                bounds.Min.Y + (bounds.Max.Y - bounds.Min.Y) * 0.5f);

            IPathfinder pathfinder = new DirectPathfinder(
                battle.Terrain, new TerrainMovementModel(TestContent.Terrain), TestContent.Terrain,
                clearanceMetres: HexPathfinder.DefaultClearanceMetres);

            RegimentGrid.Forget();
            StagedRoutePlanner.ResetCounters();

            double all = 0d, worst = 0d;
            int over2 = 0;

            for (int i = 0; i < units.Count; i++)
            {
                UnitInstance unit = units[i];
                const int across = 10;
                Vec2 destination = everybodyTo + new Vec2(
                    (i % across - across * 0.5f) * 55f,
                    (i / across - units.Count / (across * 2f)) * 55f);

                Facing arriveOn = Marching.AlongTheLine(unit.Position, destination, unit.Facing);

                var watch = Stopwatch.StartNew();
                Plan plan = Marching.PlanTo(
                    battle, unit, pathfinder, destination,
                    planner: RoutePlanners.TheStaged, arriveOn: arriveOn);
                watch.Stop();

                double ms = watch.Elapsed.TotalMilliseconds;
                all += ms;
                if (ms > worst) worst = ms;

                if (!plan.Path.Found) continue;

                float straight = Marching.SecondsToWalk(
                    battle, unit, new[] { unit.Position, destination }, null);

                if (straight > 1f &&
                    Marching.SecondsToWalk(battle, unit, plan.Path.Waypoints, plan.Hold)
                        > straight * 2f)
                    over2++;
            }

            _out.WriteLine(
                $"{what,7} " +
                $"{StagedRoutePlanner.PoseAsked,7} {StagedRoutePlanner.PoseWon,5} " +
                $"{StagedRoutePlanner.Pressed,8} {StagedRoutePlanner.TangentTooDear,5} " +
                $"{over2,8} {all / units.Count,8:0.0} {worst,9:0.0} {all,7:0}");
        }

        /// <summary>
        /// M80: a search whose order has been superseded stops at its next
        /// poll instead of spending a frame on a route nobody will take.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Red since [M133], and left red on purpose.</b> Its own non-vacuity
        /// guard is what fails: the unsuperseded run reaches <b>nought</b>
        /// expansions, so there is nothing for the poll to cut short and the
        /// assertion underneath would pass on any code at all.
        /// </para>
        /// <para>
        /// The cause is content, not the poll. This arrangement squeezes the
        /// mover past its nearest neighbour and sends it 188 m, and it used to
        /// fall through to the lattice because the Crucible's regiments were
        /// uneven - spearmen at 0,80 of the base rectangle, cavalry at 1,72.
        /// With every regiment the same rectangle the cascade answers it two
        /// stages earlier and the lattice never runs.
        /// </para>
        /// <para>
        /// Four repairs were tried and all are recorded here rather than one
        /// being kept because it went green: standing the grid down reaches 5
        /// expansions, the Crucible at two thousand worth reaches 2 (which is
        /// [M132] - a big body dies sooner rather than working harder), sending
        /// the mover 800 m instead of 188 still reaches 5, and the crowded wing
        /// reaches 0. **What this needs is an arrangement built for it**, the
        /// way <c>StoppingShortTests</c> builds a wall, rather than a bench
        /// field scavenged until the number goes over sixty-four. Fitting an
        /// arrangement to an assertion by trial is how a test stops measuring
        /// what it claims to.
        /// </para>
        /// </remarks>
        [Fact]
        public void ASearchNobodyIsWaitingForStopsAtItsNextLook()
        {
            BattleState battle = BenchScenariosTests.Load("crucible");
            List<UnitInstance> units = battle.UnitsOnField().ToList();

            UnitInstance mover = units[0];
            UnitInstance friend = units
                .Where(u => u.Id != mover.Id && u.Owner == mover.Owner)
                .OrderBy(u => Vec2.Distance(u.Position, mover.Position))
                .First();

            Vec2 wasAt = mover.Position;
            Facing wasFacing = mover.Facing;

            IPathfinder pathfinder = new DirectPathfinder(
                battle.Terrain, new TerrainMovementModel(TestContent.Terrain), TestContent.Terrain,
                clearanceMetres: HexPathfinder.DefaultClearanceMetres);

            try
            {
                Vec2 side = friend.Facing.RightVector();
                float apart = (friend.Shape.Footprint.HalfWidth + mover.Shape.Footprint.HalfWidth) * 0.8f;

                mover.Position = new Vec2(
                    friend.Position.X + side.X * apart, friend.Position.Y + side.Y * apart);

                var destination = new Vec2(
                    mover.Position.X - side.X * 188f, mover.Position.Y - side.Y * 188f);

                Facing along = Facing.Towards(mover.Position, destination);
                mover.Facing = Facing.FromDegrees(along.Degrees + 90f);
                Facing arriveOn = Marching.AlongTheLine(mover.Position, destination, mover.Facing);

                (double ms, int expansions) Run()
                {
                    HybridAStarPlanner.LastExpansions = 0;
                    var watch = Stopwatch.StartNew();
                    Marching.PlanTo(battle, mover, pathfinder, destination,
                                    planner: RoutePlanners.TheStaged, arriveOn: arriveOn);
                    watch.Stop();
                    return (watch.Elapsed.TotalMilliseconds, HybridAStarPlanner.LastExpansions);
                }

                Marching.GiveUpNow = null;
                (double wantedMs, int wantedExpansions) = Run();

                Marching.GiveUpNow = () => true;
                (double droppedMs, int droppedExpansions) = Run();

                _out.WriteLine($"still wanted : {wantedExpansions,6} expansions, {wantedMs:0.0} ms");
                _out.WriteLine($"superseded   : {droppedExpansions,6} expansions, {droppedMs:0.0} ms");

                Assert.True(
                    wantedExpansions > 64,
                    $"The arrangement only reached {wantedExpansions} expansions, so there was " +
                    "nothing for the poll to cut short. This measures nothing.");

                Assert.True(
                    droppedExpansions <= 64,
                    $"A superseded search ran {droppedExpansions} expansions against a poll every 64. " +
                    "It is not being asked, or the answer is not being read.");
            }
            finally
            {
                Marching.GiveUpNow = null;
                mover.Position = wasAt;
                mover.Facing = wasFacing;
            }
        }

        /// <summary>
        /// That the ceiling on a way round actually bites, and what it does to
        /// the routes when it does.
        /// </summary>
        [Fact]
        public void SqueezingTheCeilingOnAWayRoundUntilItBites()
        {
            float was = StagedRoutePlanner.StraightLineCostCeiling;

            try
            {
                _out.WriteLine("ceiling  refused  over2x  worst   mean s   by rung 2 or 3");
                _out.WriteLine(new string('-', 62));

                foreach (string field in new[] { "crucible", "brokencountry" })
                {
                    _out.WriteLine($"=== {field} — 80 orders ===");

                    foreach (float ceiling in new[] { 4f, 3f, 2.5f, 2f, 1.5f })
                    {
                        StagedRoutePlanner.StraightLineCostCeiling = ceiling;
                        Squeeze(field, ceiling);
                    }
                }
            }
            finally
            {
                StagedRoutePlanner.StraightLineCostCeiling = was;
                RegimentGrid.Forget();
            }
        }

        private void Squeeze(string key, float ceiling)
        {
            BattleState battle = BenchScenariosTests.Load(key);
            List<UnitInstance> units = battle.UnitsOnField().ToList();

            MapBounds bounds = battle.Terrain.Bounds;
            var everybodyTo = new Vec2(
                bounds.Min.X + (bounds.Max.X - bounds.Min.X) * 0.78f,
                bounds.Min.Y + (bounds.Max.Y - bounds.Min.Y) * 0.5f);

            IPathfinder pathfinder = new DirectPathfinder(
                battle.Terrain, new TerrainMovementModel(TestContent.Terrain), TestContent.Terrain,
                clearanceMetres: HexPathfinder.DefaultClearanceMetres);

            RegimentGrid.Forget();
            StagedRoutePlanner.ResetCounters();

            int over2 = 0, straightAway = 0;
            float worst = 0f, total = 0f;

            for (int i = 0; i < units.Count; i++)
            {
                UnitInstance unit = units[i];
                const int across = 10;
                Vec2 destination = everybodyTo + new Vec2(
                    (i % across - across * 0.5f) * 55f,
                    (i / across - units.Count / (across * 2f)) * 55f);

                Facing arriveOn = Marching.AlongTheLine(unit.Position, destination, unit.Facing);
                Plan plan = Marching.PlanTo(
                    battle, unit, pathfinder, destination,
                    planner: RoutePlanners.TheStaged, arriveOn: arriveOn);

                if (!plan.Path.Found) continue;

                float straight = Marching.SecondsToWalk(
                    battle, unit, new[] { unit.Position, destination }, null);
                if (straight <= 1f) continue;

                float took = Marching.SecondsToWalk(battle, unit, plan.Path.Waypoints, plan.Hold);
                float ratio = took / straight;

                total += ratio;
                if (ratio > 2f) over2++;
                if (ratio > worst) worst = ratio;
                if (unit.LastRung == 2 || unit.LastRung == 3) straightAway++;
            }

            _out.WriteLine(
                $"{ceiling,7:0.0} {StagedRoutePlanner.WayRoundTooDear,8} {over2,7} " +
                $"{worst,6:0.0}x {total / units.Count,8:0.00} {straightAway,10}");
        }

        /// <summary>
        /// The regime the bench never enters and the recording is full of: a
        /// short hop with one of its own beside it, where going round the end
        /// of a body is out of all proportion to the walk.
        /// </summary>
        [Fact]
        public void AShortHopPastOneOfItsOwn()
        {
            float was = StagedRoutePlanner.StraightLineCostCeiling;

            BattleState battle = BenchScenariosTests.Load("crucible");
            List<UnitInstance> units = battle.UnitsOnField().ToList();
            UnitInstance mover = units[0];
            UnitInstance friend = units
                .Where(u => u.Id != mover.Id && u.Owner == mover.Owner)
                .OrderBy(u => Vec2.Distance(u.Position, mover.Position))
                .First();

            Vec2 wasAt = mover.Position, friendWasAt = friend.Position;
            Facing wasFacing = mover.Facing, friendWasFacing = friend.Facing;

            IPathfinder pathfinder = new DirectPathfinder(
                battle.Terrain, new TerrainMovementModel(TestContent.Terrain), TestContent.Terrain,
                clearanceMetres: HexPathfinder.DefaultClearanceMetres);

            try
            {
                _out.WriteLine($"mover {mover.Shape.Footprint.Width:0} x {mover.Shape.Footprint.Depth:0} m, " +
                               $"its own {friend.Def.DisplayName} laid across the hop.");
                _out.WriteLine(string.Empty);
                _out.WriteLine("  hop  ceiling  refused  rung   ratio   waypoints");
                _out.WriteLine(new string('-', 52));

                foreach (float hop in new[] { 60f, 75f, 90f })
                foreach (float ceiling in new[] { 0f, 4f, 2f, 1.5f })
                {
                    StagedRoutePlanner.StraightLineCostCeiling = ceiling;

                    mover.Position = wasAt;
                    mover.Facing = wasFacing;

                    Vec2 forward = mover.Facing.ToVector();
                    var destination = new Vec2(
                        wasAt.X + forward.X * hop, wasAt.Y + forward.Y * hop);

                    // Squarely across the middle of the hop, side-on, so going
                    // round means going round its whole frontage.
                    friend.Position = new Vec2(
                        wasAt.X + forward.X * hop * 0.5f, wasAt.Y + forward.Y * hop * 0.5f);
                    friend.Facing = Facing.FromDegrees(mover.Facing.Degrees + 90f);

                    StagedRoutePlanner.ResetCounters();
                    mover.LastRung = 0;

                    Facing arriveOn = Marching.AlongTheLine(mover.Position, destination, mover.Facing);
                    Plan plan = Marching.PlanTo(battle, mover, pathfinder, destination,
                                                planner: RoutePlanners.TheStaged, arriveOn: arriveOn);

                    float straight = Marching.SecondsToWalk(
                        battle, mover, new[] { mover.Position, destination }, null);
                    float took = plan.Path.Found
                        ? Marching.SecondsToWalk(battle, mover, plan.Path.Waypoints, plan.Hold)
                        : 0f;

                    _out.WriteLine(
                        $"{hop,5:0} {(ceiling > 0f ? $"{ceiling:0.0}" : "off"),8} " +
                        $"{StagedRoutePlanner.WayRoundTooDear,8} {mover.LastRung,5} " +
                        $"{(straight > 1f ? took / straight : 0f),7:0.0}x {plan.Path.Waypoints.Count,10}");
                }
            }
            finally
            {
                StagedRoutePlanner.StraightLineCostCeiling = was;
                mover.Position = wasAt; mover.Facing = wasFacing;
                friend.Position = friendWasAt; friend.Facing = friendWasFacing;
                RegimentGrid.Forget();
            }
        }

        private static float GridLength(IReadOnlyList<Vec2> points)
        {
            float total = 0f;

            for (int i = 1; i < points.Count; i++) total += Vec2.Distance(points[i - 1], points[i]);

            return total;
        }
    }
}
