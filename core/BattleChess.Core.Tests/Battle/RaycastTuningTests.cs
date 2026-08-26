using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using BattleChess.Contracts;
using BattleChess.Rules;
using BattleChess.Rules.HybridPlanning;
using Xunit;
using Xunit.Abstractions;

namespace BattleChess.Tests.Battle
{
    /// <summary>
    /// Tuning the cast from <c>M73</c>, and using it to fence the lattice in.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two questions, measured separately. <b>First</b>, whether the cast's own
    /// parameters — how far past a corner it aims, how close two candidates may
    /// be before one is dropped, and whether edge midpoints are worth adding to
    /// the corners — move the numbers <c>M73</c> recorded. <b>Second</b>, and
    /// the designer's own suggestion, whether the candidate points are a better
    /// fence for the lattice than <see cref="HybridAStarPlanner.StrayMultiple"/>:
    /// the corners of the bodies near the drawn line name the only ground a way
    /// round could plausibly use, and everything else — the map edge, the inside
    /// of a block of regiments — is ground no answer will ever come from.
    /// </para>
    /// <para>
    /// <b>The scoring is M73's.</b> Unwalkable is the only genuine failure. A
    /// press is a legitimate answer and is reported, not judged. The lattice's
    /// answer counts as worth having only when it is a clean way round under
    /// the ceiling, because that is the only case where refusing it costs
    /// anything.
    /// </para>
    /// </remarks>
    public sealed class RaycastTuningTests : IDisposable
    {
        private readonly ITestOutputHelper _out;
        public RaycastTuningTests(ITestOutputHelper output) => _out = output;

        public void Dispose()
        {
            HybridAStarPlanner.MustStayNear = null;
            HybridAStarPlanner.StrayMultiple = 1.5f;
            HybridAStarPlanner.Headings = 0;
        }

        // ---- the cast's parameters, as levers -----------------------------

        private static float Margin = 8f;   // M74
        private static float Merge = 15f;
        private static bool AlsoEdgeMidpoints;

        private static void Restore()
        {
            Margin = 8f;
            Merge = 15f;
            AlsoEdgeMidpoints = false;
            HybridAStarPlanner.MustStayNear = null;
            HybridAStarPlanner.StrayMultiple = 1.5f;
        }

        /// <summary>The candidate points, off the blockers' own axes.</summary>
        private static List<Vec2> Places(BattleState battle, UnitInstance unit, Vec2 destination)
        {
            Vec2 start = unit.Position;

            var near = new List<UnitInstance>(32);
            battle.WhereEverybodyIs.Near(
                battle.AllUnits, start, destination, unit.Footprint.BoundingRadius, near);

            float reach = unit.Footprint.BoundingRadius + Margin;
            var raw = new List<Vec2>(near.Count * 8);

            foreach (UnitInstance other in near)
            {
                if (!other.IsOnField || ReferenceEquals(other, unit)) continue;

                OrientedRect shape = other.Shape;
                Vec2[] corners = shape.GetCorners();

                foreach (Vec2 corner in corners)
                {
                    Vec2 outward = corner - shape.Centre;
                    float forward = Vec2.Dot(outward, shape.Forward) >= 0f ? 1f : -1f;
                    float right = Vec2.Dot(outward, shape.Right) >= 0f ? 1f : -1f;

                    raw.Add(corner + shape.Forward * (forward * reach) + shape.Right * (right * reach));
                }

                if (!AlsoEdgeMidpoints) continue;

                // The middle of each side, pushed straight out. A corner is the
                // right aim for going round a body; the middle of a side is the
                // right aim for the mouth of a gap between two of them.
                for (int i = 0; i < 4; i++)
                {
                    Vec2 middle = (corners[i] + corners[(i + 1) % 4]) * 0.5f;
                    Vec2 outward = middle - shape.Centre;
                    float length = outward.Length;

                    if (length > Vec2.Epsilon) raw.Add(middle + outward / length * reach);
                }
            }

            return Merged(raw, start, destination);
        }

        private static List<Vec2> Merged(List<Vec2> places, Vec2 from, Vec2 to)
        {
            Vec2 line = to - from;
            float length = line.Length;
            Vec2 way = length > Vec2.Epsilon ? line / length : new Vec2(1f, 0f);

            float OffTheLine(Vec2 p)
            {
                Vec2 d = p - from;
                return MathF.Abs(d.X * way.Y - d.Y * way.X);
            }

            places.Sort((a, b) => OffTheLine(a).CompareTo(OffTheLine(b)));

            if (Merge <= 0f) return places;

            var kept = new List<Vec2>(places.Count);

            foreach (Vec2 place in places)
            {
                bool already = false;

                for (int i = 0; i < kept.Count && !already; i++)
                    already = Vec2.Distance(kept[i], place) < Merge;

                if (!already) kept.Add(place);
            }

            return kept;
        }

        private sealed record Cast(
            bool Says, float Seconds, int Casts, int Points, double Ms, Vec2[]? Route);

        private static Cast Ask(
            BattleState battle, UnitInstance unit, Vec2 destination, float limitSeconds,
            IReadOnlyList<Vec2> places)
        {
            var watch = Stopwatch.StartNew();

            Vec2 start = unit.Position;
            Facing along = Marching.AlongTheLine(start, destination, unit.Facing);
            int casts = 1;

            if (Marching.IsClearLine(battle, unit, start, destination, along, leaving: true))
            {
                watch.Stop();
                return new Cast(
                    true, Marching.SecondsToWalk(battle, unit, new[] { start, destination }, null),
                    casts, 0, watch.Elapsed.TotalMilliseconds, new[] { start, destination });
            }

            float best = float.PositiveInfinity;
            Vec2[]? route = null;

            foreach (Vec2 place in places)
            {
                casts++;
                if (!Marching.IsClearLeg(battle, unit, start, place, unit.Facing)) continue;

                casts++;
                if (!Marching.IsClearLeg(
                        battle, unit, place, destination,
                        Marching.AlongTheLine(start, place, unit.Facing))) continue;

                Vec2[] through = { start, place, destination };
                float seconds = Marching.SecondsToWalk(battle, unit, through, null);

                if (seconds >= best) continue;

                best = seconds;
                route = through;
            }

            watch.Stop();
            return new Cast(best <= limitSeconds, best, casts, places.Count,
                watch.Elapsed.TotalMilliseconds, route);
        }

        // ---- the fence ----------------------------------------------------

        /// <summary>
        /// Ground within <paramref name="halfWidth"/> of a candidate point, or
        /// of the drawn line itself, marked on a coarse grid.
        /// </summary>
        /// <remarks>
        /// A grid rather than a distance test per successor: the lattice asks
        /// this question three times an expansion and twenty thousand times an
        /// order, so sixty distance tests each would cost more than the search
        /// it is meant to save. Cells are generous by half a diagonal, so the
        /// fence never refuses ground the exact test would allow.
        /// </remarks>
        private sealed class Fence
        {
            private const float CellMetres = 20f;

            private readonly bool[] _open;
            private readonly int _wide, _high;
            private readonly Vec2 _corner;

            public Fence(IReadOnlyList<Vec2> places, Vec2 from, Vec2 to, float halfWidth)
            {
                float minX = MathF.Min(from.X, to.X), maxX = MathF.Max(from.X, to.X);
                float minY = MathF.Min(from.Y, to.Y), maxY = MathF.Max(from.Y, to.Y);

                foreach (Vec2 place in places)
                {
                    minX = MathF.Min(minX, place.X); maxX = MathF.Max(maxX, place.X);
                    minY = MathF.Min(minY, place.Y); maxY = MathF.Max(maxY, place.Y);
                }

                float pad = halfWidth + CellMetres;
                _corner = new Vec2(minX - pad, minY - pad);
                _wide = (int)((maxX + pad - _corner.X) / CellMetres) + 1;
                _high = (int)((maxY + pad - _corner.Y) / CellMetres) + 1;
                _open = new bool[_wide * _high];

                float room = halfWidth + CellMetres * 0.71f;

                for (int y = 0; y < _high; y++)
                for (int x = 0; x < _wide; x++)
                {
                    var at = new Vec2(
                        _corner.X + (x + 0.5f) * CellMetres,
                        _corner.Y + (y + 0.5f) * CellMetres);

                    if (ToTheSegment(at, from, to) <= room) { _open[y * _wide + x] = true; continue; }

                    foreach (Vec2 place in places)
                    {
                        if (Vec2.Distance(at, place) > room) continue;
                        _open[y * _wide + x] = true;
                        break;
                    }
                }
            }

            public bool Allows(Vec2 at)
            {
                int x = (int)((at.X - _corner.X) / CellMetres);
                int y = (int)((at.Y - _corner.Y) / CellMetres);

                if (x < 0 || y < 0 || x >= _wide || y >= _high) return false;

                return _open[y * _wide + x];
            }

            public int Cells => _open.Length;
            public int OpenCells => _open.Count(o => o);

            private static float ToTheSegment(Vec2 at, Vec2 from, Vec2 to)
            {
                Vec2 span = to - from;
                float length = span.LengthSquared;

                float along = length <= Vec2.Epsilon ? 0f : Vec2.Dot(at - from, span) / length;
                along = MathF.Max(0f, MathF.Min(1f, along));

                return Vec2.Distance(at, from + span * along);
            }
        }

        // ---- the harness ---------------------------------------------------

        private sealed record Row(
            int Index, float Asked, Cast Cast,
            bool Clean, float Seconds, int Expanded, double LatticeMs, float Limit,
            double FenceMs, float OpenShare);

        private static IReadOnlyList<Row> Run(string key, float fenceHalfWidth)
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

            var rows = new List<Row>(units.Count);

            for (int i = 0; i < units.Count; i++)
            {
                UnitInstance unit = units[i];
                const int across = 10;
                Vec2 destination = everybodyTo + new Vec2(
                    (i % across - across * 0.5f) * 55f,
                    (i / across - units.Count / (across * 2f)) * 55f);

                Facing arriveOn = Marching.AlongTheLine(unit.Position, destination, unit.Facing);

                Plan press = Marching.PlanTo(battle, unit, pathfinder, destination,
                    planner: RoutePlanners.TheStaged, arriveOn: arriveOn);

                float pressed = press.Path.Found
                    ? Marching.SecondsToWalk(battle, unit, press.Path.Waypoints, press.Hold)
                    : 0f;
                float limit = pressed > 1f
                    ? pressed * StagedRoutePlanner.WayRoundCostCeiling
                    : float.PositiveInfinity;

                List<Vec2> places = Places(battle, unit, destination);
                Cast cast = Ask(battle, unit, destination, limit, places);

                double fenceMs = 0;
                float openShare = 1f;

                if (fenceHalfWidth > 0f)
                {
                    var built = Stopwatch.StartNew();
                    var fence = new Fence(places, unit.Position, destination, fenceHalfWidth);
                    built.Stop();

                    fenceMs = built.Elapsed.TotalMilliseconds;
                    openShare = fence.Cells == 0 ? 1f : (float)fence.OpenCells / fence.Cells;
                    HybridAStarPlanner.MustStayNear = fence.Allows;
                }

                var watch = Stopwatch.StartNew();
                Plan posed = HybridAStarRoutePlanner.PlanAlong(
                    battle, unit, destination, arriveOn, corridor: null, 0f, log: null,
                    expansionBudget: StagedRoutePlanner.PoseExpansionBudget, secondsLimit: limit);
                watch.Stop();

                HybridAStarPlanner.MustStayNear = null;

                bool clean = posed.Path.Found && !posed.PressedThrough &&
                             StagedRoutePlanner.WalksCleanly(battle, unit, posed);

                rows.Add(new Row(
                    i, Vec2.Distance(unit.Position, destination), cast, clean,
                    posed.Path.Found
                        ? Marching.SecondsToWalk(battle, unit, posed.Path.Waypoints, posed.Hold)
                        : 0f,
                    HybridAStarPlanner.LastExpansions, watch.Elapsed.TotalMilliseconds, limit,
                    fenceMs, openShare));
            }

            return rows;
        }

        // ---- 1. the cast's own parameters -----------------------------------

        [Fact(Skip = "A record of a measurement, not a check on one — it drives global levers.")]
        public void TuningTheCast()
        {
            _out.WriteLine("The cast's parameters. 'Worth having' = clean way round under the M65 ceiling.");
            _out.WriteLine(string.Empty);

            foreach (string field in new[] { "crucible", "brokencountry", "longmarch" })
            {
                _out.WriteLine($"=== {field} — 80 orders ===");
                _out.WriteLine(
                    "margin  merge  midpoints   accepted  false   discarded  <20ms  points  worst ms  total ms");
                _out.WriteLine(new string('-', 100));

                foreach ((float margin, float merge, bool midpoints) in new[]
                {
                    (4f, 15f, false),   // what M73 measured
                    (2f, 15f, false),
                    (8f, 15f, false),
                    (16f, 15f, false),
                    (4f, 0f, false),    // no merging at all
                    (4f, 30f, false),
                    (4f, 15f, true),    // corners and side middles
                    (8f, 25f, true),
                })
                {
                    Restore();
                    Margin = margin; Merge = merge; AlsoEdgeMidpoints = midpoints;

                    IReadOnlyList<Row> rows = Run(field, 0f);
                    Report(margin, merge, midpoints, rows);
                }

                _out.WriteLine(string.Empty);
            }

            Restore();
        }

        private void Report(float margin, float merge, bool midpoints, IReadOnlyList<Row> rows)
        {
            bool Worth(Row r) => r.Clean && r.Seconds <= r.Limit;

            var discarded = rows.Where(r => !r.Cast.Says && Worth(r)).ToList();

            _out.WriteLine(
                $"{margin,6:0} {merge,6:0} {(midpoints ? "yes" : "no"),10}   " +
                $"{rows.Count(r => r.Cast.Says),8} {rows.Count(r => r.Cast.Says && !Worth(r)),6} " +
                $"{discarded.Count,11} {discarded.Count(r => r.LatticeMs < 20.0),6} " +
                $"{rows.Average(r => r.Cast.Points),7:0} " +
                $"{rows.Max(r => r.Cast.Ms),9:0.0} {rows.Sum(r => r.Cast.Ms),9:0.0}");
        }

        private static float Length(IReadOnlyList<Vec2> route)
        {
            float total = 0f;
            for (int i = 1; i < route.Count; i++) total += Vec2.Distance(route[i - 1], route[i]);
            return total;
        }

        // ---- 3. the cast's cost as the ceiling -------------------------------

        /// <summary>
        /// Handing the search what the cast already proved, instead of three
        /// times the press.
        /// </summary>
        /// <remarks>
        /// <para>
        /// [M74] killed the cast's <i>geometry</i> as a bound. Its <i>cost</i>
        /// is a different thing entirely and cannot fail the same way: a clear
        /// pair of casts is a route in hand, so nothing dearer than it is worth
        /// finding, and a ceiling can only refuse routes worse than one already
        /// held. It cannot exclude ground.
        /// </para>
        /// <para>
        /// The ceiling today is <c>press x 3</c> from <c>M65</c>, which on
        /// these fields sits at 1 800 to 3 000 s against routes that come in at
        /// 600 to 1 000. The question is whether closing that gap prunes the
        /// runaway searches without costing anything, given that the fallback
        /// is no longer the press but the cast's own route.
        /// </para>
        /// </remarks>
        [Fact(Skip = "A record of a measurement, not a check on one — it drives global levers.")]
        public void TheCastsCostAsTheCeiling()
        {
            _out.WriteLine("The ceiling handed to the lattice. Fallback is the cast's route, not the press.");
            _out.WriteLine("'kept' = a clean way round in hand, from either. 'better' = the lattice beat the cast.");
            _out.WriteLine(string.Empty);

            foreach (string field in new[] { "crucible", "brokencountry", "longmarch" })
            {
                _out.WriteLine($"=== {field} — 80 orders ===");
                _out.WriteLine(
                    "ceiling         kept  better  worst ms  total ms  expansions  route s  bit on");
                _out.WriteLine(new string('-', 88));

                foreach ((string name, float share) in new[]
                {
                    ("press x 3 (ships)", 0f),
                    ("cast, as found", 1.00f),
                    ("cast, less 5%", 0.95f),
                    ("cast, less 20%", 0.80f),
                })
                {
                    Restore();
                    Ceiling(field, name, share);
                }

                _out.WriteLine(string.Empty);
            }

            Restore();
        }

        private void Ceiling(string key, string name, float share)
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

            int kept = 0, better = 0, bitOn = 0;
            double worstMs = 0, totalMs = 0, seconds = 0;
            long expansions = 0;

            // The claim this is here to test: the cast succeeds on the orders
            // the lattice already answers cheaply, and finds nothing on the
            // ones that run away. If that is so, the two are correlated rather
            // than complementary and no bound taken from one can help the other.
            long whereCastFound = 0, whereItDidNot = 0;
            int found = 0, didNot = 0;
            double msFound = 0, msDidNot = 0;

            for (int i = 0; i < units.Count; i++)
            {
                UnitInstance unit = units[i];
                const int across = 10;
                Vec2 destination = everybodyTo + new Vec2(
                    (i % across - across * 0.5f) * 55f,
                    (i / across - units.Count / (across * 2f)) * 55f);

                Facing arriveOn = Marching.AlongTheLine(unit.Position, destination, unit.Facing);

                Plan press = Marching.PlanTo(battle, unit, pathfinder, destination,
                    planner: RoutePlanners.TheStaged, arriveOn: arriveOn);

                float pressed = press.Path.Found
                    ? Marching.SecondsToWalk(battle, unit, press.Path.Waypoints, press.Hold)
                    : 0f;
                float ceiling = pressed > 1f
                    ? pressed * StagedRoutePlanner.WayRoundCostCeiling
                    : float.PositiveInfinity;

                Cast cast = Ask(battle, unit, destination, ceiling, Places(battle, unit, destination));

                // The cast's route only counts as held if it passes the same
                // gate every other route in the project has to pass.
                bool castHeld = cast.Says && cast.Route != null &&
                                StagedRoutePlanner.WalksCleanly(
                                    battle, unit,
                                    new Plan(
                                        PathResult.Success(
                                            cast.Route, Array.Empty<Coord>(),
                                            Length(cast.Route), Length(cast.Route), 0),
                                        null, false));

                float limit = ceiling;

                if (share > 0f && castHeld && cast.Seconds * share < limit)
                {
                    limit = cast.Seconds * share;
                    bitOn++;
                }

                var watch = Stopwatch.StartNew();
                Plan posed = HybridAStarRoutePlanner.PlanAlong(
                    battle, unit, destination, arriveOn, corridor: null, 0f, log: null,
                    expansionBudget: StagedRoutePlanner.PoseExpansionBudget, secondsLimit: limit);
                watch.Stop();

                double ms = watch.Elapsed.TotalMilliseconds;
                totalMs += ms;
                if (ms > worstMs) worstMs = ms;
                expansions += HybridAStarPlanner.LastExpansions;

                if (castHeld)
                {
                    found++; whereCastFound += HybridAStarPlanner.LastExpansions; msFound += ms;
                }
                else
                {
                    didNot++; whereItDidNot += HybridAStarPlanner.LastExpansions; msDidNot += ms;
                }

                bool latticeHeld = posed.Path.Found && !posed.PressedThrough &&
                                   posed.Path.Waypoints.Count > 1 &&
                                   StagedRoutePlanner.WalksCleanly(battle, unit, posed);

                float latticeSeconds = latticeHeld
                    ? Marching.SecondsToWalk(battle, unit, posed.Path.Waypoints, posed.Hold)
                    : float.PositiveInfinity;

                if (latticeHeld && latticeSeconds > ceiling) latticeHeld = false;

                float held = float.PositiveInfinity;

                if (castHeld) held = cast.Seconds;
                if (latticeHeld && latticeSeconds < held) { held = latticeSeconds; better++; }

                if (!float.IsInfinity(held)) { kept++; seconds += held; }
            }

            _out.WriteLine(
                $"{name,-17} {kept,4} {better,7} {worstMs,9:0.0} {totalMs,9:0.0} " +
                $"{expansions,11} {seconds,8:0} {bitOn,7}" +
                $"   | cast found: {found,3} orders, {(found == 0 ? 0 : whereCastFound / found),6} states, " +
                $"{(found == 0 ? 0 : msFound / found),6:0.0} ms each" +
                $"   | cast found nothing: {didNot,3} orders, " +
                $"{(didNot == 0 ? 0 : whereItDidNot / didNot),6} states, " +
                $"{(didNot == 0 ? 0 : msDidNot / didNot),6:0.0} ms each");
        }

        // ---- 2. the fence ----------------------------------------------------

        [Fact(Skip = "A record of a measurement, not a check on one — it drives global levers.")]
        public void FencingTheLatticeInWithTheCandidatePoints()
        {
            _out.WriteLine("The lattice held to ground the cast's candidates name, against the stray box.");
            _out.WriteLine("unwalkable is the only failure. 'kept' = clean ways round still found.");
            _out.WriteLine(string.Empty);

            foreach (string field in new[] { "crucible", "brokencountry", "longmarch" })
            {
                _out.WriteLine($"=== {field} — 80 orders ===");
                _out.WriteLine(
                    "fence  box   kept  routed  worst ms  total ms  expansions   open %  fence ms  route s");
                _out.WriteLine(new string('-', 96));

                foreach ((float halfWidth, float stray) in new[]
                {
                    (0f, 1.5f),      // what ships
                    (40f, 1.5f), (60f, 1.5f), (90f, 1.5f), (140f, 1.5f),
                    (0f, 0f),        // no bound of any kind
                    (60f, 0f), (90f, 0f), (140f, 0f), (220f, 0f),
                })
                {
                    Restore();
                    HybridAStarPlanner.StrayMultiple = stray;

                    IReadOnlyList<Row> rows = Run(field, halfWidth);
                    bool Worth(Row r) => r.Clean && r.Seconds <= r.Limit;

                    _out.WriteLine(
                        $"{(halfWidth == 0f ? "none" : halfWidth.ToString("0") + " m"),-6}" +
                        $"{(stray > 0f ? "+box" : "    "),-5}" +
                        $"{rows.Count(Worth),5} {rows.Count(r => r.Seconds > 0f),7} " +
                        $"{rows.Max(r => r.LatticeMs),9:0.0} {rows.Sum(r => r.LatticeMs),9:0.0} " +
                        $"{rows.Sum(r => (long)r.Expanded),11} " +
                        $"{rows.Average(r => r.OpenShare) * 100,7:0} " +
                        $"{rows.Sum(r => r.FenceMs),9:0.0} {rows.Sum(r => r.Seconds),8:0}");
                }

                _out.WriteLine(string.Empty);
            }

            Restore();
        }
    }
}
