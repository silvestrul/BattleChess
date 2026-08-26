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
    /// Every way found to make the lattice search less, and what each costs.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The metrics are deliberately not the earlier sweeps'.</b> Those
    /// scored a press-through as damage, which contradicts <c>M65</c> — a press
    /// is the right answer when the way round is too dear — and counting it as
    /// damage made every cheapening lever look worse than it was. Reported
    /// here: <b>worst single order in milliseconds</b>, which is the freeze;
    /// <b>unwalkable</b>, which is the only genuine failure, because it is a
    /// route the executor refuses and a regiment that stands still; and the
    /// <b>worst detour</b>, which is the silly-looking route. Presses are
    /// printed and not judged.
    /// </para>
    /// <para>
    /// <b>Why both fixtures are here.</b> The Great Field orders are the ones
    /// recorded as dear in play, but every one of them presses whatever the
    /// setting — so they measure speed and nothing else, and a lever can look
    /// free there purely by making the search fail sooner. The bench's
    /// one-click orders are where the pose search wins some, so what a cheaper
    /// search <i>costs</i> has somewhere to show up. A lever is only free if it
    /// is free on the second table.
    /// </para>
    /// <para>
    /// <b>The diagnosis these came from.</b> The lattice bins ground at 20 m
    /// and heading into 16, so the Great Field — 1800 x 2400 m — holds
    /// <b>172 800 states</b>. An order of <b>282 m</b> explored <b>841 m
    /// sideways and 666 m past its own ends</b> for 31 640 expansions. Nothing
    /// bounded the search to the ground the order was about, and the route it
    /// returned was the five-fold detour the ceiling then threw away.
    /// </para>
    /// </remarks>
    [Collection(PlannerLevers.Name)]
    public sealed class FewerExpansionsTests : IDisposable
    {
        private readonly ITestOutputHelper _out;
        public FewerExpansionsTests(ITestOutputHelper output) => _out = output;

        public void Dispose() => Restore();

        /// <summary>Back to what the game ships with.</summary>
        private static void Restore()
        {
            HybridAStarPlanner.StrayMultiple = 1.5f;
            HybridAStarPlanner.PositionBin = 0f;
            HybridAStarPlanner.Headings = 0;
            HybridAStarPlanner.Weight = 0f;
            HybridAStarPlanner.SweepSpacing = 0f;
            StagedRoutePlanner.PoseExpansionBudget = 20000;
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

        /// <summary>The orders the recording caught costing 22 to 889 ms.</summary>
        private static readonly (Vec2 From, Vec2 To)[] Recorded =
        {
            (new Vec2(263f, 1763f), new Vec2(658f, 1678f)),
            (new Vec2(263f, 1513f), new Vec2(615f, 1542f)),
            (new Vec2(263f, 1038f), new Vec2(544f, 1029f)),
            (new Vec2(263f, 1388f), new Vec2(617f, 1360f)),
            (new Vec2(263f, 1263f), new Vec2(620f, 1176f)),
            (new Vec2(263f, 1638f), new Vec2(551f, 1609f)),
            (new Vec2(263f, 1163f), new Vec2(544f, 1154f)),
        };

        private sealed record Tally(
            double WorstMs, double TotalMs, int Routed, int Pressed, int Unwalkable,
            double WorstDetour, double Seconds);

        private static Tally Measure(
            BattleState battle, IReadOnlyList<UnitInstance> units, Func<int, Vec2> destination)
        {
            IPathfinder pathfinder = new DirectPathfinder(
                battle.Terrain, new TerrainMovementModel(TestContent.Terrain), TestContent.Terrain);

            // Warm: tiered compilation is still promoting on a first pass.
            for (int i = 0; i < units.Count; i++)
            {
                Vec2 warm = destination(i);
                Marching.PlanTo(battle, units[i], pathfinder, warm, planner: RoutePlanners.TheStaged,
                    arriveOn: Marching.AlongTheLine(units[i].Position, warm, units[i].Facing));
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            double worstMs = 0, totalMs = 0, worstDetour = 0, seconds = 0;
            int routed = 0, pressed = 0, unwalkable = 0;

            for (int i = 0; i < units.Count; i++)
            {
                Vec2 to = destination(i);
                Facing arriveOn = Marching.AlongTheLine(units[i].Position, to, units[i].Facing);

                var watch = Stopwatch.StartNew();
                Plan plan = Marching.PlanTo(battle, units[i], pathfinder, to,
                    planner: RoutePlanners.TheStaged, arriveOn: arriveOn);
                watch.Stop();

                double ms = watch.Elapsed.TotalMilliseconds;
                totalMs += ms;
                if (ms > worstMs) worstMs = ms;

                if (!plan.Path.Found) continue;

                routed++;
                if (plan.PressedThrough) pressed++;
                if (!StagedRoutePlanner.WalksCleanly(battle, units[i], plan)) unwalkable++;

                float priced = Marching.SecondsToWalk(battle, units[i], plan.Path.Waypoints, plan.Hold);
                float straight = Marching.SecondsToWalk(
                    battle, units[i], new[] { units[i].Position, to }, null);

                seconds += priced;
                if (straight > 1f && priced / straight > worstDetour) worstDetour = priced / straight;
            }

            return new Tally(worstMs, totalMs, routed, pressed, unwalkable, worstDetour, seconds);
        }

        private static Tally OnTheGreatField()
        {
            double worstMs = 0, totalMs = 0;

            foreach ((Vec2 from, Vec2 to) in Recorded)
            {
                BattleState battle = GreatField();
                UnitInstance unit = battle.UnitsOnField()
                    .OrderBy(u => Vec2.Distance(u.Position, from)).First();

                Tally one = Measure(battle, new[] { unit }, _ => to);

                totalMs += one.TotalMs;
                if (one.WorstMs > worstMs) worstMs = one.WorstMs;
            }

            return new Tally(worstMs, totalMs, 0, 0, 0, 0, 0);
        }

        private static Tally OnTheBench(string key)
        {
            BattleState battle = BenchScenariosTests.Load(key);
            var units = battle.UnitsOnField().ToList();

            MapBounds bounds = battle.Terrain.Bounds;
            var everybodyTo = new Vec2(
                bounds.Min.X + (bounds.Max.X - bounds.Min.X) * 0.78f,
                bounds.Min.Y + (bounds.Max.Y - bounds.Min.Y) * 0.5f);

            // The same block the conformance harness sends them to, so every
            // table in the project describes one workload.
            return Measure(battle, units, i =>
            {
                const int across = 10;
                return everybodyTo + new Vec2(
                    (i % across - across * 0.5f) * 55f,
                    (i / across - units.Count / (across * 2f)) * 55f);
            });
        }

        private void Row(string name)
        {
            Tally gf = OnTheGreatField();
            Tally cr = OnTheBench("crucible");
            Tally bc = OnTheBench("brokencountry");

            _out.WriteLine(
                $"{name,-24} {gf.WorstMs,7:0.0} {cr.WorstMs,8:0.0} {cr.TotalMs,9:0.0} " +
                $"{cr.Unwalkable,6} {cr.Pressed,7} {cr.WorstDetour,8:0.00}x {cr.Seconds,8:0} " +
                $"{bc.WorstMs,8:0.0} {bc.Unwalkable,6} {bc.WorstDetour,8:0.00}x");
        }

        private void Head(string what)
        {
            _out.WriteLine(string.Empty);
            _out.WriteLine($"-- {what} --");
        }

        /// <summary>
        /// The ground bin, on every map, against the settings now shipped.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The earlier bin sweep ran against the old defaults and only on two
        /// fields. This one runs on all four and on top of <c>M71</c> — stray
        /// 1,5, twelve headings, twenty thousand expansions — because a lever
        /// measured against a different baseline says nothing about this one.
        /// </para>
        /// <para>
        /// <b>The Great Field is the map the game is played on and the one
        /// number here that cannot be read as quality.</b> Every one of its
        /// recorded orders presses whatever the bin, so its column moves only
        /// with speed. It also carries four horse-archer regiments at 200 x
        /// 100 m, two and a half times the headcount <c>units.cfg</c> caps
        /// them at — the battle file raises its own ceiling and says so — so
        /// its obstacles are larger than anywhere else in content.
        /// </para>
        /// </remarks>
        [Fact(Skip = "A record of a measurement, not a check on one — it drives global levers.")]
        public void TheGroundBinOnEveryMap()
        {
            _out.WriteLine("Ground bin, on top of what M71 ships: stray 1,5 + 12 headings + cap 20k.");
            _out.WriteLine("unwalkable is the only failure. pressed is reported, not judged.");
            _out.WriteLine(string.Empty);

            foreach (string field in new[] { "crucible", "brokencountry", "longmarch" })
            {
                _out.WriteLine($"=== {field} — eighty one-click orders ===");
                _out.WriteLine(
                    "bin        worst ms   total ms  routed  unwalk  pressed    detour    route s");
                _out.WriteLine(new string('-', 82));

                foreach (float bin in new[] { 0f, 30f, 40f, 50f, 60f })
                {
                    Restore();
                    HybridAStarPlanner.PositionBin = bin;

                    Tally t = OnTheBench(field);

                    _out.WriteLine(
                        $"{(bin == 0f ? "20 (ships)" : bin.ToString("0") + " m"),-10} " +
                        $"{t.WorstMs,8:0.0} {t.TotalMs,10:0.0} {t.Routed,7}/80 " +
                        $"{t.Unwalkable,7} {t.Pressed,8} {t.WorstDetour,9:0.00}x {t.Seconds,10:0}");
                }

                _out.WriteLine(string.Empty);
            }

            _out.WriteLine("=== Great Field — the seven recorded orders (speed only, all press) ===");
            _out.WriteLine("bin        worst ms   total ms");
            _out.WriteLine(new string('-', 34));

            foreach (float bin in new[] { 0f, 30f, 40f, 50f, 60f })
            {
                Restore();
                HybridAStarPlanner.PositionBin = bin;

                Tally t = OnTheGreatField();

                _out.WriteLine(
                    $"{(bin == 0f ? "20 (ships)" : bin.ToString("0") + " m"),-10} " +
                    $"{t.WorstMs,8:0.0} {t.TotalMs,10:0.0}");
            }

            Restore();
        }

        [Fact(Skip = "A record of a measurement, not a check on one — it drives global levers.")]
        public void EveryWayToSearchLess()
        {
            _out.WriteLine(
                "Great Field: the seven orders recorded as dear — speed only, they all press.");
            _out.WriteLine(
                "Crucible / Broken Country: eighty one-click orders — where the lattice wins some.");
            _out.WriteLine(
                "unwalkable is the only failure. pressed is reported, not judged.");
            _out.WriteLine(string.Empty);
            _out.WriteLine(
                "setting                   GF ms  CR worst  CR total unwalk pressed   detour  route s " +
                " BC worst unwalk   detour");
            _out.WriteLine(new string('-', 126));

            Restore();
            StagedRoutePlanner.PoseExpansionBudget = 100000;
            Row("nothing at all");

            Restore();
            Row("as it ships (cap 20k)");

            Head("1. hold the search near the order — a multiple of the straight line");
            foreach (float stray in new[] { 3f, 2f, 1.5f, 1.25f, 1f, 0.75f, 0.5f, 0.35f })
            {
                Restore();
                HybridAStarPlanner.StrayMultiple = stray;
                Row($"stray x{stray:0.00}");
            }

            Head("2. coarser ground bins — 20 m is what it ships with");
            foreach (float bin in new[] { 25f, 30f, 35f, 40f, 50f, 60f, 80f })
            {
                Restore();
                HybridAStarPlanner.PositionBin = bin;
                Row($"bin {bin:0} m");
            }

            Head("3. fewer headings — 16 is what it ships with");
            foreach (int bins in new[] { 14, 12, 10, 8, 6 })
            {
                Restore();
                HybridAStarPlanner.Headings = bins;
                Row($"headings {bins}");
            }

            Head("4. simply allow fewer expansions");
            foreach (int cap in new[] { 40000, 10000, 5000, 2000, 1000, 500 })
            {
                Restore();
                StagedRoutePlanner.PoseExpansionBudget = cap;
                Row($"cap {cap}");
            }

            Head("5. a greedier heuristic — 2 is what it ships with");
            foreach (float weight in new[] { 2.5f, 3f, 4f, 6f })
            {
                Restore();
                HybridAStarPlanner.Weight = weight;
                Row($"weight {weight:0.0}");
            }

            Head("6. coarser sweep sampling — cheaper expansions rather than fewer");
            foreach (float spacing in new[] { 3f, 4f, 6f })
            {
                Restore();
                HybridAStarPlanner.SweepSpacing = spacing;
                Row($"sweep {spacing:0} m");
            }

            Head("7. combinations");

            Restore();
            HybridAStarPlanner.StrayMultiple = 1.5f;
            HybridAStarPlanner.Headings = 12;
            Row("stray1.5 + h12");

            Restore();
            HybridAStarPlanner.StrayMultiple = 1.5f;
            HybridAStarPlanner.Headings = 12;
            HybridAStarPlanner.PositionBin = 25f;
            Row("stray1.5 + h12 + bin25");

            Restore();
            HybridAStarPlanner.StrayMultiple = 1.5f;
            HybridAStarPlanner.Headings = 12;
            HybridAStarPlanner.PositionBin = 30f;
            Row("stray1.5 + h12 + bin30");

            Restore();
            HybridAStarPlanner.StrayMultiple = 1f;
            HybridAStarPlanner.Headings = 12;
            HybridAStarPlanner.PositionBin = 30f;
            Row("stray1 + h12 + bin30");

            Restore();
            HybridAStarPlanner.StrayMultiple = 1.5f;
            HybridAStarPlanner.Headings = 12;
            StagedRoutePlanner.PoseExpansionBudget = 10000;
            Row("stray1.5 + h12 + cap10k");

            Restore();
            HybridAStarPlanner.StrayMultiple = 1.5f;
            HybridAStarPlanner.Headings = 12;
            HybridAStarPlanner.SweepSpacing = 4f;
            Row("stray1.5 + h12 + sweep4");

            Restore();
        }
    }
}
