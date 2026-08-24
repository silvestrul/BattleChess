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
    /// A two-leg visibility test, asked before the lattice, to decide whether
    /// asking the lattice is worth the time.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The proposal, as given.</b> Take the bodies the drawn line runs into.
    /// Cast to the corners of each — pushed out far enough that the regiment
    /// could stand there — and from every corner that can be reached, cast on
    /// to the destination. Any pair of clear casts is a witness that a way
    /// round exists and roughly what it costs. If no pair is clear, or the
    /// cheapest is dearer than the press it would replace, do not run the
    /// lattice at all.
    /// </para>
    /// <para>
    /// <b>What it can and cannot prove.</b> A clear pair is a genuine
    /// <i>witness</i>: that route exists, and its cost is an upper bound on the
    /// best way round. The other direction is only a guess — a shortest path
    /// among rectangles is a taut string that may bend three or four times, and
    /// no two-leg cast can see one of those. So this is a rule of thumb about
    /// what the lattice is <i>likely</i> to find, and it is measured here as
    /// one: what it saves when it says no, and what it throws away when it is
    /// wrong.
    /// </para>
    /// <para>
    /// <b>Deliberately not in the package.</b> Nothing here ships until the
    /// tables below say it should.
    /// </para>
    /// </remarks>
    public sealed class TwoLegGateTests : IDisposable
    {
        private readonly ITestOutputHelper _out;
        public TwoLegGateTests(ITestOutputHelper output) => _out = output;

        public void Dispose()
        {
            HybridAStarPlanner.StrayMultiple = 1.5f;
            HybridAStarPlanner.Headings = 0;
        }

        /// <summary>How far past a body's own corner the regiment's centre is put.</summary>
        /// <remarks>
        /// The corner is a point on the blocker. A regiment standing there
        /// would be half inside it, so the cast aims at the corner pushed out
        /// along both of the blocker's own axes by the mover's circumscribed
        /// radius. Conservative on purpose: a candidate that is too far out
        /// still yields a clear pair, one that is too close yields none.
        /// </remarks>
        private const float CornerMarginMetres = 4f;

        private sealed record Verdict(
            bool Says, float Seconds, int Casts, int Corners, double Ms);

        /// <summary>The gate itself.</summary>
        private static Verdict Ask(
            BattleState battle, UnitInstance unit, Vec2 destination, Facing arriveOn, float limitSeconds)
        {
            var watch = Stopwatch.StartNew();

            Vec2 start = unit.Position;
            Facing along = Marching.AlongTheLine(start, destination, unit.Facing);
            int casts = 0;

            // Nothing in the way: the ladder would have taken this already, but
            // the gate has to answer the same question the lattice was asked.
            casts++;
            if (Marching.IsClearLine(battle, unit, start, destination, along, leaving: true))
            {
                watch.Stop();
                float direct = Marching.SecondsToWalk(
                    battle, unit, new[] { start, destination }, null);
                return new Verdict(true, direct, casts, 0, watch.Elapsed.TotalMilliseconds);
            }

            // Step 1 of the proposal: the shapes are already on the field, so
            // there is no mesh to build. Every regiment near the drawn line is
            // a rectangle with a centre, two axes and four corners.
            var near = new List<UnitInstance>(32);
            battle.WhereEverybodyIs.Near(
                battle.AllUnits, start, destination, unit.Footprint.BoundingRadius, near);

            float reach = unit.Footprint.BoundingRadius + CornerMarginMetres;
            var aimingAt = new List<Vec2>(near.Count * 4);

            foreach (UnitInstance other in near)
            {
                if (!other.IsOnField || ReferenceEquals(other, unit)) continue;

                OrientedRect shape = other.Shape;
                Vec2[] corners = shape.GetCorners();

                foreach (Vec2 corner in corners)
                {
                    // Out along the blocker's own axes rather than along the
                    // line of march, which is the whole point of taking the
                    // candidates off the obstacle: the answer does not change
                    // as the approach goes diagonal.
                    Vec2 outward = corner - shape.Centre;
                    float alongForward = Vec2.Dot(outward, shape.Forward) >= 0f ? 1f : -1f;
                    float alongRight = Vec2.Dot(outward, shape.Right) >= 0f ? 1f : -1f;

                    aimingAt.Add(
                        corner + shape.Forward * (alongForward * reach)
                               + shape.Right * (alongRight * reach));
                }
            }

            float best = float.PositiveInfinity;

            foreach (Vec2 corner in aimingAt)
            {
                // Step 3 and 4: out to the corner, then on to the destination.
                // IsClearLeg sweeps the regiment's own rectangle down the leg,
                // so "is the gap wide enough" is answered by the same oracle
                // the executor uses rather than by a width comparison.
                casts++;
                if (!Marching.IsClearLeg(battle, unit, start, corner, unit.Facing)) continue;

                casts++;
                if (!Marching.IsClearLeg(
                        battle, unit, corner, destination,
                        Marching.AlongTheLine(start, corner, unit.Facing))) continue;

                float seconds = Marching.SecondsToWalk(
                    battle, unit, new[] { start, corner, destination }, null);

                if (seconds < best) best = seconds;
            }

            watch.Stop();

            bool says = best <= limitSeconds;
            return new Verdict(
                says, best, casts, aimingAt.Count, watch.Elapsed.TotalMilliseconds);
        }

        /// <summary>
        /// The same gate with one more bend allowed, and the candidate points
        /// merged first.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Depth one throws away a third of the routes the lattice would have
        /// found, and in every one of those cases <b>no</b> two-leg pair was
        /// clear at all — the cheapest way round genuinely bends three times.
        /// So the question is whether one more bend recovers them at a cost
        /// still worth paying.
        /// </para>
        /// <para>
        /// Two prunings make that affordable, and both are steps in the
        /// proposal. Candidate points within <see cref="MergeWithinMetres"/> of
        /// one already kept are dropped, which is the "merge the positions"
        /// step; and the search stops at the <i>first</i> route under the
        /// ceiling rather than the cheapest, because a gate only has to answer
        /// whether one exists. Candidates are tried nearest-the-drawn-line
        /// first so that the first answer is usually a sensible one.
        /// </para>
        /// </remarks>
        private const float MergeWithinMetres = 15f;

        private const int MostCasts = 400;

        private static Verdict AskDeeper(
            BattleState battle, UnitInstance unit, Vec2 destination, Facing arriveOn, float limitSeconds)
        {
            var watch = Stopwatch.StartNew();

            Vec2 start = unit.Position;
            Facing along = Marching.AlongTheLine(start, destination, unit.Facing);
            int casts = 1;

            if (Marching.IsClearLine(battle, unit, start, destination, along, leaving: true))
            {
                watch.Stop();
                float direct = Marching.SecondsToWalk(
                    battle, unit, new[] { start, destination }, null);
                return new Verdict(true, direct, casts, 0, watch.Elapsed.TotalMilliseconds);
            }

            IReadOnlyList<Vec2> places = Merged(Corners(battle, unit, destination), start, destination);

            float best = float.PositiveInfinity;
            var reachable = new List<Vec2>(places.Count);

            foreach (Vec2 place in places)
            {
                if (casts >= MostCasts) break;

                casts++;
                if (!Marching.IsClearLeg(battle, unit, start, place, unit.Facing)) continue;

                reachable.Add(place);

                casts++;
                if (!Marching.IsClearLeg(
                        battle, unit, place, destination,
                        Marching.AlongTheLine(start, place, unit.Facing))) continue;

                float twoLegs = Marching.SecondsToWalk(
                    battle, unit, new[] { start, place, destination }, null);

                if (twoLegs < best) best = twoLegs;

                if (best <= limitSeconds)
                {
                    watch.Stop();
                    return new Verdict(true, best, casts, places.Count, watch.Elapsed.TotalMilliseconds);
                }
            }

            foreach (Vec2 first in reachable)
            {
                Facing onward = Marching.AlongTheLine(start, first, unit.Facing);

                foreach (Vec2 second in places)
                {
                    if (casts >= MostCasts) break;
                    if (Vec2.Distance(first, second) < MergeWithinMetres) continue;

                    casts++;
                    if (!Marching.IsClearLeg(battle, unit, first, second, onward)) continue;

                    casts++;
                    if (!Marching.IsClearLeg(
                            battle, unit, second, destination,
                            Marching.AlongTheLine(first, second, onward))) continue;

                    float threeLegs = Marching.SecondsToWalk(
                        battle, unit, new[] { start, first, second, destination }, null);

                    if (threeLegs < best) best = threeLegs;

                    if (best <= limitSeconds)
                    {
                        watch.Stop();
                        return new Verdict(true, best, casts, places.Count, watch.Elapsed.TotalMilliseconds);
                    }
                }

                if (casts >= MostCasts) break;
            }

            watch.Stop();
            return new Verdict(false, best, casts, places.Count, watch.Elapsed.TotalMilliseconds);
        }

        private static List<Vec2> Corners(BattleState battle, UnitInstance unit, Vec2 destination)
        {
            var near = new List<UnitInstance>(32);
            battle.WhereEverybodyIs.Near(
                battle.AllUnits, unit.Position, destination, unit.Footprint.BoundingRadius, near);

            float reach = unit.Footprint.BoundingRadius + CornerMarginMetres;
            var places = new List<Vec2>(near.Count * 4);

            foreach (UnitInstance other in near)
            {
                if (!other.IsOnField || ReferenceEquals(other, unit)) continue;

                OrientedRect shape = other.Shape;

                foreach (Vec2 corner in shape.GetCorners())
                {
                    Vec2 outward = corner - shape.Centre;
                    float forward = Vec2.Dot(outward, shape.Forward) >= 0f ? 1f : -1f;
                    float right = Vec2.Dot(outward, shape.Right) >= 0f ? 1f : -1f;

                    places.Add(
                        corner + shape.Forward * (forward * reach) + shape.Right * (right * reach));
                }
            }

            return places;
        }

        /// <summary>Nearest the drawn line first, and nothing twice.</summary>
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

            var kept = new List<Vec2>(places.Count);

            foreach (Vec2 place in places)
            {
                bool already = false;

                for (int i = 0; i < kept.Count && !already; i++)
                    already = Vec2.Distance(kept[i], place) < MergeWithinMetres;

                if (!already) kept.Add(place);
            }

            return kept;
        }

        // ------------------------------------------------------------------

        private sealed record Row(
            string Where, int Index, float Asked, Verdict Gate, Verdict Deeper,
            bool HybridFound, bool HybridClean, float HybridSeconds, int Expanded, double HybridMs,
            float Limit);

        private static IEnumerable<Row> Sweep(
            string where, BattleState battle, IReadOnlyList<UnitInstance> units, Func<int, Vec2> to)
        {
            IPathfinder pathfinder = new DirectPathfinder(
                battle.Terrain, new TerrainMovementModel(TestContent.Terrain), TestContent.Terrain,
                clearanceMetres: HexPathfinder.DefaultClearanceMetres);

            for (int i = 0; i < units.Count; i++)
            {
                UnitInstance unit = units[i];
                Vec2 destination = to(i);
                Facing arriveOn = Marching.AlongTheLine(unit.Position, destination, unit.Facing);

                // The ceiling the staged planner would have handed the search:
                // the press it exists to avoid, times what M65 will pay.
                Plan press = Marching.PlanTo(battle, unit, pathfinder, destination,
                    planner: RoutePlanners.TheStaged, arriveOn: arriveOn);

                float pressed = press.Path.Found
                    ? Marching.SecondsToWalk(battle, unit, press.Path.Waypoints, press.Hold)
                    : 0f;
                float limit = pressed > 1f
                    ? pressed * StagedRoutePlanner.WayRoundCostCeiling
                    : float.PositiveInfinity;

                Verdict gate = Ask(battle, unit, destination, arriveOn, limit);
                Verdict deeper = AskDeeper(battle, unit, destination, arriveOn, limit);

                var watch = Stopwatch.StartNew();
                Plan posed = HybridAStarRoutePlanner.PlanAlong(
                    battle, unit, destination, arriveOn, corridor: null, 0f, log: null,
                    expansionBudget: StagedRoutePlanner.PoseExpansionBudget,
                    secondsLimit: limit);
                watch.Stop();

                bool clean = posed.Path.Found && !posed.PressedThrough &&
                             StagedRoutePlanner.WalksCleanly(battle, unit, posed);

                float seconds = posed.Path.Found
                    ? Marching.SecondsToWalk(battle, unit, posed.Path.Waypoints, posed.Hold)
                    : 0f;

                yield return new Row(
                    where, i, Vec2.Distance(unit.Position, destination), gate, deeper,
                    posed.Path.Found, clean, seconds,
                    HybridAStarPlanner.LastExpansions, watch.Elapsed.TotalMilliseconds, limit);
            }
        }

        private void Score(string where, IReadOnlyList<Row> rows)
        {
            Tally(where + " — one bend", rows, r => r.Gate);
            Tally(where + " — two bends, merged", rows, r => r.Deeper);
        }

        private void Tally(string where, IReadOnlyList<Row> rows, Func<Row, Verdict> which)
        {
            // The lattice's answer is only worth having when it is a clean way
            // round *under the ceiling* — anything else the staged planner
            // throws away, so a gate that refuses it has cost nothing.
            bool Worth(Row r) => r.HybridClean && r.HybridSeconds <= r.Limit;

            var saved = rows.Where(r => !which(r).Says && !Worth(r)).ToList();
            var discarded = rows.Where(r => !which(r).Says && Worth(r)).ToList();
            var admitted = rows.Where(r => which(r).Says && Worth(r)).ToList();
            var wasted = rows.Where(r => which(r).Says && !Worth(r)).ToList();

            double gateMs = rows.Sum(r => which(r).Ms);
            double hybridMs = rows.Sum(r => r.HybridMs);
            double savedMs = saved.Sum(r => r.HybridMs);
            var quick = discarded.Where(r => r.HybridMs < 20.0).ToList();

            _out.WriteLine($"=== {where} — {rows.Count} orders ===");
            _out.WriteLine(
                $"  gate said no, lattice had nothing worth having : {saved.Count,4}  " +
                $"({savedMs,8:0.0} ms of searching not done)");
            _out.WriteLine(
                $"  gate said no, lattice had a route worth having : {discarded.Count,4}  " +
                $"<-- WRONGLY DISCARDED, {quick.Count} of them found in under 20 ms");
            _out.WriteLine(
                $"  gate said yes, and it was worth having         : {admitted.Count,4}");
            _out.WriteLine(
                $"  gate said yes, and it was not                  : {wasted.Count,4}  " +
                $"({wasted.Sum(r => r.HybridMs),8:0.0} ms still spent)");
            _out.WriteLine(
                $"  the gate itself cost {gateMs,8:0.0} ms against {hybridMs,8:0.0} ms of lattice, " +
                $"worst gate {(rows.Count == 0 ? 0 : rows.Max(r => which(r).Ms)),6:0.0} ms, " +
                $"worst casts {(rows.Count == 0 ? 0 : rows.Max(r => which(r).Casts)),4}");

                        var both = admitted.Where(r => !float.IsInfinity(which(r).Seconds) && r.HybridSeconds > 1f)
                .ToList();

            if (both.Count > 0)
            {
                var ratios = both.Select(r => which(r).Seconds / r.HybridSeconds).OrderBy(x => x).ToList();

                _out.WriteLine(
                    $"  where both had one: the cast route is {ratios.Average(),5:0.00}x the lattice route " +
                    $"on average, worst {ratios[ratios.Count - 1],5:0.00}x, " +
                    $"cheaper of the two on {both.Count(r => which(r).Seconds <= r.HybridSeconds),3} of {both.Count,3}");
            }
            foreach (Row r in discarded.OrderByDescending(x => x.HybridMs).Take(6))
                _out.WriteLine(
                    $"    thrown away: order {r.Index,3}, asked {r.Asked,5:0} m, lattice found it in " +
                    $"{r.HybridMs,7:0.0} ms at {r.HybridSeconds,6:0} s against a {r.Limit,6:0} s ceiling; " +
                    $"gate's best two-leg was {(float.IsInfinity(which(r).Seconds) ? -1 : which(r).Seconds),6:0} s");

            foreach (Row r in saved.OrderByDescending(x => x.HybridMs).Take(4))
                _out.WriteLine(
                    $"    saved      : order {r.Index,3}, asked {r.Asked,5:0} m, lattice spent " +
                    $"{r.HybridMs,7:0.0} ms over {r.Expanded,6} states for nothing");

            _out.WriteLine(string.Empty);
        }

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

        private IReadOnlyList<Row> OnTheBench(string key)
        {
            BattleState battle = BenchScenariosTests.Load(key);
            var units = battle.UnitsOnField().ToList();

            MapBounds bounds = battle.Terrain.Bounds;
            var everybodyTo = new Vec2(
                bounds.Min.X + (bounds.Max.X - bounds.Min.X) * 0.78f,
                bounds.Min.Y + (bounds.Max.Y - bounds.Min.Y) * 0.5f);

            return Sweep(key, battle, units, i =>
            {
                const int across = 10;
                return everybodyTo + new Vec2(
                    (i % across - across * 0.5f) * 55f,
                    (i / across - units.Count / (across * 2f)) * 55f);
            }).ToList();
        }

        [Fact(Skip = "A record of a measurement, not a check on one — it drives global levers.")]
        public void IsTheTwoLegCastAGoodGate()
        {
            _out.WriteLine("A two-leg visibility cast, asked before the lattice.");
            _out.WriteLine("'Worth having' = a clean way round, under the M65 ceiling.");
            _out.WriteLine(string.Empty);

            foreach (string field in new[] { "crucible", "brokencountry", "longmarch" })
                Score(field, OnTheBench(field));

            // And the map that froze, with the levers off, which is the only
            // arrangement measured so far where the lattice runs away.
            HybridAStarPlanner.StrayMultiple = 0f;
            HybridAStarPlanner.Headings = 16;

            foreach (string field in new[] { "crucible", "brokencountry" })
                Score(field + " (levers off)", OnTheBench(field));

            HybridAStarPlanner.StrayMultiple = 1.5f;
            HybridAStarPlanner.Headings = 0;
        }
    }
}
