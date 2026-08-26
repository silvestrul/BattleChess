using System;
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
    /// The order of 25 August, tick 1063: four hundred metres walked side-on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Rebuilt from the <c>Scene</c> block the recording now writes on every
    /// order, which is the whole reason that block exists. Positions, facings
    /// and footprints are the logged ones; the field is shifted 1350 m south to
    /// fit the test map, which moves everything together and so changes no
    /// relative geometry.
    /// </para>
    /// <para>
    /// Only the marching army is raised. <see cref="Marching"/> asks
    /// <c>IsInTheWayOf</c>, and that says an enemy is never an obstacle to a
    /// regiment that was going somewhere else (M15a) — so the eastern army
    /// could not have contributed to this route whatever it did.
    /// </para>
    /// </remarks>
    public sealed class TheSidewaysMileTests
    {
        private readonly ITestOutputHelper _out;

        public TheSidewaysMileTests(ITestOutputHelper output) => _out = output;

        private const float SouthBy = 1350f;

        /// <summary>
        /// Forty thousand men in twenty regiments, which is what the recording
        /// says and what makes a spearman regiment 80 x 40 m rather than the
        /// 40 x 20 a default-strength one would be. Get this wrong and the
        /// bodies are a quarter of the area they were, so every clearance
        /// question is answered about a different field.
        /// </summary>
        private const int Strength = 2000;

        private static Vec2 At(float x, float y) => new Vec2(x, y - SouthBy);

        /// <summary>The arrangement as the recording described it.</summary>
        private static Battlefield TheOrderOfTheTwentyFifth(
            out UnitInstance mover, out Vec2 destination)
        {
            var field = new Battlefield("plains", 20260818);

            void Raise(string key, float x, float y, float degrees)
            {
                Vec2 at = At(x, y);
                if (at.Y < 0f || at.Y > Battlefield.Rows * Battlefield.CellSize) return;

                Battlefield.Hold(field.Add(0, key, at, Facing.FromDegrees(degrees), Strength));
            }

            // #U0 .. #U19, the western army, as logged at tick 1063.
            Raise("cavalry",     362.5f, 2137.5f,   0f);
            Raise("horsearchers",362.5f, 1912.5f,   0f);
            Raise("cavalry",     362.5f,  862.5f,   0f);
            Raise("cavalry",     362.5f,  612.5f,   0f);
            Raise("horsearchers",362.5f,  387.5f,   0f);
            //   #U5 is the mover, raised below.
            Raise("spearmen",    736.7f, 1785.8f,  20.6f);
            Raise("spearmen",    362.5f, 1537.5f,   0f);
            Raise("spearmen",    362.5f, 1437.5f,   0f);
            Raise("spearmen",    362.5f, 1337.5f,   0f);
            Raise("spearmen",    362.5f, 1237.5f,   0f);
            Raise("spearmen",    362.5f, 1137.5f,   0f);
            Raise("spearmen",    362.5f, 1037.5f,   0f);
            Raise("swordsmen",   258.7f, 2325.5f,   3.1f);
            Raise("swordsmen",   262.5f, 1637.5f,   0f);
            Raise("swordsmen",   262.5f, 1512.5f,   0f);
            Raise("swordsmen",   262.5f, 1387.5f,   0f);
            Raise("swordsmen",   262.5f, 1262.5f,   0f);
            Raise("swordsmen",   262.5f, 1162.5f,   0f);
            Raise("swordsmen",   262.5f, 1037.5f,   0f);

            mover = field.Add(
                0, "spearmen", At(312.8f, 1776.4f), Facing.FromDegrees(144.4f), Strength);

            // The point the planner settled on, not the one the mouse hit: the
            // recording says it aimed 25 m off "because the ground there is
            // taken or impassable", and the route it then drew ran to this.
            destination = At(712f, 1713f);

            return field;
        }

        /// <summary>
        /// What the ladder answered, and what it cost against the alternatives.
        /// </summary>
        [Fact]
        public void WhatTheOrderOfTheTwentyFifthActuallyChose()
        {
            Battlefield field = TheOrderOfTheTwentyFifth(out UnitInstance mover, out Vec2 destination);

            Vec2 travel = destination - mover.Position;
            Facing straight = Facing.FromVector(travel);

            _out.WriteLine(
                $"mover at ({mover.Position.X:0},{mover.Position.Y:0}) facing {mover.Facing.Degrees:0}°, " +
                $"{mover.Footprint.Width:0}x{mover.Footprint.Depth:0} m");
            _out.WriteLine(
                $"to ({destination.X:0},{destination.Y:0}), {travel.Length:0} m on a bearing of " +
                $"{straight.Degrees:0}° — {Facing.AbsoluteDelta(mover.Facing, straight) * 180f / MathF.PI:0}° off its front");
            _out.WriteLine(string.Empty);

            bool clearStraight = Marching.IsClearLine(
                field.State, mover, mover.Position, destination, straight, out UnitInstance? blocker);

            _out.WriteLine(
                $"squared to the line: {(clearStraight ? "clear" : "refused by " + Name(blocker))}");

            IReadOnlyList<Vec2>? crab =
                Marching.CrabThrough(field.State, mover, destination, out Facing?[]? hold);

            _out.WriteLine(
                crab == null
                    ? "crab: none offered"
                    : $"crab: {crab.Count} waypoints, holds [{Holds(hold)}]");
            _out.WriteLine(string.Empty);

            float straightCost = Marching.SecondsToWalk(
                field.State, mover, new[] { mover.Position, destination });

            _out.WriteLine($"walking straight there, wheel included: {straightCost:0} s");

            if (crab != null)
                _out.WriteLine(
                    $"the crab as offered:                    " +
                    $"{Marching.SecondsToWalk(field.State, mover, crab, hold):0} s");

            _out.WriteLine(
                $"turning on the spot first, then marching:  {RotateThenMarch(field.State, mover, destination):0} s");
            _out.WriteLine(string.Empty);

            Plan plan = Marching.PlanTo(field.State, mover, field.Pathfinder, destination);

            float chosen = Marching.SecondsToWalk(
                field.State, mover, plan.Path.Waypoints, plan.Hold);

            _out.WriteLine($"the ladder answered at rung {mover.LastRung}:");
            _out.WriteLine(
                $"  {plan.Path.Waypoints.Count} waypoints, holds [{Holds(plan.Hold)}], {chosen:0} s");

            // M81. The gate is the shape, not the price: this order is not
            // allowed to be answered by holding one front across the whole
            // journey. Written against the recording, so it fails on the code
            // as it stood - 2 waypoints, one of them side-on, 645 s.
            Assert.True(
                plan.Path.Found,
                "The order of 25 August has to be answerable at all.");

            bool crabbedTheWholeWay =
                plan.Path.Waypoints.Count <= 2 && plan.Hold != null && plan.Hold.Any(h => h.HasValue);

            Assert.False(
                crabbedTheWholeWay,
                $"404 m held side-on to get past one regiment: {chosen:0} s against " +
                $"{straightCost:0} s for the same line walked front-on. A crab is a " +
                $"manoeuvre at a gap, and this route has no gap in it.");

            Assert.True(
                chosen < straightCost * 2f,
                $"A way round this hop cost {chosen:0} s against {straightCost:0} s straight " +
                $"({chosen / straightCost:0.0}x). The recording's was 645 s.");
        }

        /// <summary>
        /// The same order, loaded from the bench field rather than built in code.
        /// </summary>
        /// <remarks>
        /// <b>W8</b>'s second half. The gate above pins the fault at the exact
        /// metre positions the recording gives; this asks the same question of
        /// <c>sidewaysmile.battle.txt</c>, where the reader has snapped every
        /// regiment to a cell centre. If the fault only exists at one arbitrary
        /// sub-cell offset it is not worth a bench field, and this is what says
        /// which.
        /// </remarks>
        [Fact]
        public void TheBenchFieldHoldsTheSameOrder()
        {
            BattleState battle = BenchScenariosTests.Load("sidewaysmile");

            UnitInstance mover = TheMover(battle);
            Vec2 destination = new Vec2(712f, 1713f);

            IPathfinder pathfinder = new DirectPathfinder(
                battle.Terrain, new TerrainMovementModel(TestContent.Terrain), TestContent.Terrain,
                clearanceMetres: HexPathfinder.DefaultClearanceMetres);

            float straight = Marching.SecondsToWalk(
                battle, mover, new[] { mover.Position, destination });

            Plan plan = Marching.PlanTo(battle, mover, pathfinder, destination);

            float took = Marching.SecondsToWalk(battle, mover, plan.Path.Waypoints, plan.Hold);

            _out.WriteLine(
                $"mover at ({mover.Position.X:0},{mover.Position.Y:0}) facing {mover.Facing.Degrees:0}°, " +
                $"{mover.Footprint.Width:0}x{mover.Footprint.Depth:0} m");
            _out.WriteLine(
                $"rung {mover.LastRung}, {plan.Path.Waypoints.Count} waypoints, " +
                $"holds [{Holds(plan.Hold)}], {took:0} s against {straight:0} s straight " +
                $"({took / straight:0.0}x)");

            Assert.True(plan.Path.Found, "The bench field's order has to be answerable.");

            bool crabbedTheWholeWay =
                plan.Path.Waypoints.Count <= 2 && plan.Hold != null && plan.Hold.Any(h => h.HasValue);

            Assert.False(
                crabbedTheWholeWay,
                $"The bench field crabs the whole way: {took:0} s against {straight:0} s front-on.");
        }

        /// <summary>
        /// The regiment the recording followed: the only one off the column,
        /// found by its pose rather than by an index, because an index into a
        /// deployment order is exactly the sort of thing that silently comes to
        /// mean a different regiment.
        /// </summary>
        private static UnitInstance TheMover(BattleState battle) =>
            battle.UnitsOnField().Single(
                u => u.Def.Key == "spearmen" &&
                     MathF.Abs(u.Facing.Degrees - 144.4f) < 1f);

        /// <summary>
        /// What capping the crabbed share does to this order, and to the bench.
        /// </summary>
        [Fact(Skip = "A record of a measurement, not a check on one — it drives a global lever.")]
        public void CappingHowFarARegimentMayWalkSideOn()
        {
            float was = StagedRoutePlanner.CrabbedShareCeiling;

            try
            {
                _out.WriteLine("=== the order of 25 August, tick 1063 ===");
                _out.WriteLine("share  rung  waypoints  crabbed legs      s   against straight");
                _out.WriteLine(new string('-', 64));

                foreach (float share in new[] { 1f, 0.9f, 0.75f, 0.5f, 0.25f })
                {
                    StagedRoutePlanner.CrabbedShareCeiling = share;
                    TheOneOrder(share);
                }

                _out.WriteLine(string.Empty);
                _out.WriteLine("=== 80 orders a field ===");
                _out.WriteLine("share  declined  crabs  worst   mean s   held");
                _out.WriteLine(new string('-', 52));

                foreach (string field in new[] { "crucible", "brokencountry" })
                {
                    _out.WriteLine($"--- {field} ---");

                    foreach (float share in new[] { 1f, 0.9f, 0.75f, 0.5f, 0.25f })
                    {
                        StagedRoutePlanner.CrabbedShareCeiling = share;
                        Sweep(field, share);
                    }
                }
            }
            finally
            {
                StagedRoutePlanner.CrabbedShareCeiling = was;
                RegimentGrid.Forget();
            }
        }

        private void TheOneOrder(float share)
        {
            Battlefield field = TheOrderOfTheTwentyFifth(out UnitInstance mover, out Vec2 destination);

            Plan plan = Marching.PlanTo(field.State, mover, field.Pathfinder, destination);

            float straight = Marching.SecondsToWalk(
                field.State, mover, new[] { mover.Position, destination });
            float took = Marching.SecondsToWalk(
                field.State, mover, plan.Path.Waypoints, plan.Hold);

            int sideOn = plan.Hold == null
                ? 0
                : plan.Hold.Count(h => h.HasValue);

            _out.WriteLine(
                $"{Share(share),5}  {mover.LastRung,4}  {plan.Path.Waypoints.Count,9}  " +
                $"{sideOn,12}  {took,5:0}   {took / straight,7:0.0}x");
        }

        private void Sweep(string key, float share)
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

            int crabs = 0, held = 0;
            float worst = 0f, total = 0f, counted = 0f;

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

                if (plan.Hold != null && plan.Hold.Any(h => h.HasValue)) crabs++;
                if (StagedRoutePlanner.WalksCleanly(battle, unit, plan)) held++;

                float straight = Marching.SecondsToWalk(
                    battle, unit, new[] { unit.Position, destination }, null);
                if (straight <= 1f) continue;

                float ratio = Marching.SecondsToWalk(battle, unit, plan.Path.Waypoints, plan.Hold) / straight;

                total += ratio;
                counted++;
                if (ratio > worst) worst = ratio;
            }

            _out.WriteLine(
                $"{Share(share),5}  {StagedRoutePlanner.CrabTooLong,8}  {crabs,5}  " +
                $"{worst,5:0.0}x  {(counted > 0 ? total / counted : 0f),7:0.00}x  {held,5}");
        }

        private static string Share(float share) =>
            share >= 1f ? "off" : $"{share:0.00}";

        /// <summary>
        /// What the manoeuvre the recording wanted would have cost: come round
        /// standing still, walk the line at full pace, come round again at the
        /// far end onto the front the order asked for.
        /// </summary>
        private static float RotateThenMarch(BattleState battle, UnitInstance unit, Vec2 destination)
        {
            Vec2 travel = destination - unit.Position;
            Facing straight = Facing.FromVector(travel);

            float pace = MathF.Max(0.1f, battle.SpeedOf(unit));
            float turnRate = MathF.Max(1f, unit.Def.Get(UnitAttributes.TurnRate));

            float onTheSpot = Facing.AbsoluteDelta(unit.Facing, straight) * 180f / MathF.PI / turnRate;

            return onTheSpot + travel.Length / pace;
        }

        private static string Name(UnitInstance? unit) =>
            unit == null ? "the ground" : $"its own {unit.Def.DisplayName}";

        private static string Holds(IReadOnlyList<Facing?>? hold) =>
            hold == null
                ? "none"
                : string.Join(", ", hold.Select(h => h.HasValue ? $"{h.Value.Degrees:0}°" : "-"));
    }
}
