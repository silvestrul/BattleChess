using System;
using System.Collections.Generic;
using System.Diagnostics;
using BattleChess.Contracts;
using BattleChess.Rules;
using Xunit;
using Xunit.Abstractions;

namespace BattleChess.Tests.Battle
{
    /// <summary>
    /// What one march costs as the field gets busier and the march gets longer.
    /// </summary>
    public sealed class ScalingTests
    {
        private readonly ITestOutputHelper _out;

        public ScalingTests(ITestOutputHelper output) => _out = output;

        /// <summary>
        /// Scatters bodies through a band the march has to cross, deterministically.
        /// </summary>
        private static Battlefield Crowd(int bodies, float run, out UnitInstance mover, out Vec2 destination)
        {
            var field = new Battlefield("plains", 4242ul);

            Vec2 centre = field.Centre;

            // Laid across the line of march in a lattice, jittered off a fixed
            // seed so the arrangement is awkward but repeatable.
            var rng = new Random(7);

            for (int i = 0; i < bodies; i++)
            {
                float alongFraction = (i + 0.5f) / bodies;
                float beside = ((i % 5) - 2) * 45f;

                Vec2 at = centre
                          + new Vec2(0f, (alongFraction - 0.5f) * run * 0.8f)
                          + new Vec2(beside + (float)(rng.NextDouble() * 20.0 - 10.0), 0f);

                Battlefield.Hold(
                    field.Add(0, "spearmen", at, Facing.FromDegrees((float)(rng.NextDouble() * 180.0))));
            }

            mover = field.Add(0, "cavalry", centre - new Vec2(0f, run * 0.5f), Facing.FromDegrees(90f));
            destination = centre + new Vec2(0f, run * 0.5f);

            return field;
        }

        private void Row(string label, int bodies, float run, IRoutePlanner planner)
        {
            Battlefield field = Crowd(bodies, run, out UnitInstance mover, out Vec2 destination);

            // Once to warm anything lazy, then timed.
            Plan plan = Marching.PlanTo(
                field.State, mover, field.Pathfinder, destination, planner: planner);

            var watch = Stopwatch.StartNew();

            const int repeats = 20;
            for (int i = 0; i < repeats; i++)
            {
                plan = Marching.PlanTo(
                    field.State, mover, field.Pathfinder, destination, planner: planner);
            }

            watch.Stop();

            double ms = watch.Elapsed.TotalMilliseconds / repeats;

            string outcome = plan.Path.Found
                ? $"{plan.Path.Waypoints.Count} waypoints, {plan.Path.Distance:0} m" +
                  (plan.PressedThrough ? "  *** PRESSED ***" : string.Empty)
                : $"NO ROUTE ({plan.Path.Failure})";

            _out.WriteLine($"{label,-8}{bodies,3} bodies {run,4:0} m {ms,8:0.00} ms  {outcome}");

            if (plan.Effort.Searched)
            {
                RouteEffort e = plan.Effort;

                // Microseconds per dear question, which is the only way to tell a
                // counter that is large from a counter that is expensive.
                double perGeometry = e.Geometry > 0 ? ms * 1000.0 / e.Geometry : 0.0;

                _out.WriteLine($"        {e.Detail}");
                _out.WriteLine(
                    $"        states/place {(double)e.States / e.Places,6:0.0}   " +
                    $"geometry/leg {(double)e.Geometry / MathF.Max(1, e.Legs),5:0.00}   " +
                    $"cache hit {(double)e.CacheHits / MathF.Max(1, e.CacheHits + e.Legs),6:0.0%}   " +
                    $"frontier/expansion {(double)e.FrontierScans / MathF.Max(1, e.Expansions),8:0}   " +
                    $"{perGeometry,5:0.00} us/geometry");
            }
        }

        [Fact]
        public void HowTheCostGrowsWithTheCrowd()
        {
            _out.WriteLine("--- density: same 400 m march, more bodies in the way ---");

            foreach (int bodies in new[] { 2, 4, 8, 16, 32, 64 })
                Row("search", bodies, 400f, RoutePlanners.TheSearch);

            _out.WriteLine(string.Empty);
            _out.WriteLine("--- distance: same 16 bodies, longer march ---");

            foreach (float run in new[] { 100f, 200f, 400f, 800f })
                Row("search", 16, run, RoutePlanners.TheSearch);

            _out.WriteLine(string.Empty);
            _out.WriteLine("--- the ladder, for scale ---");

            foreach (int bodies in new[] { 2, 8, 32, 64 })
                Row("ladder", bodies, 400f, RoutePlanners.TheLadder);
        }

        /// <summary>
        /// Dense, but with a lane nothing stands in, so a clean route certainly
        /// exists. Anything that presses through here is failing, not deciding.
        /// </summary>
        [Fact]
        public void ADenseFieldWithAnOpenLane()
        {
            foreach (int bodies in new[] { 4, 8, 16, 32 })
            {
                var field = new Battlefield("plains", 555ul);
                Vec2 centre = field.Centre;

                const float run = 400f;
                const float laneX = 220f;

                // A wall of bodies down the middle, and nothing at all within
                // 100 m of the lane, which is 5 times the mover's beam.
                for (int i = 0; i < bodies; i++)
                {
                    float along = ((i + 0.5f) / bodies - 0.5f) * run * 0.8f;
                    float beside = ((i % 4) - 2) * 45f;

                    Battlefield.Hold(
                        field.Add(0, "spearmen", centre + new Vec2(beside, along), Facing.FromDegrees(0f)));
                }

                UnitInstance mover = field.Add(
                    0, "cavalry", centre - new Vec2(0f, run * 0.5f), Facing.FromDegrees(90f));

                Vec2 destination = centre + new Vec2(0f, run * 0.5f);

                Plan plan = Marching.PlanTo(
                    field.State, mover, field.Pathfinder, destination,
                    planner: RoutePlanners.TheSearch);

                // Is the lane actually open? Ask the same geometry the search does.
                Vec2 viaOut = centre + new Vec2(laneX, -run * 0.5f);
                Vec2 viaBack = centre + new Vec2(laneX, run * 0.5f);

                bool laneIsOpen =
                    Marching.IsClearLine(field.State, mover, mover.Position, viaOut,
                        Marching.AlongTheLine(mover.Position, viaOut, mover.Facing), leaving: true) &&
                    Marching.IsClearLine(field.State, mover, viaOut, viaBack,
                        Marching.AlongTheLine(viaOut, viaBack, mover.Facing), leaving: true) &&
                    Marching.IsClearLine(field.State, mover, viaBack, destination,
                        Marching.AlongTheLine(viaBack, destination, mover.Facing), leaving: true);

                _out.WriteLine(
                    $"{bodies,3} bodies, lane open: {laneIsOpen,-5}   " +
                    $"{plan.Effort}   -> " +
                    (plan.Path.Found
                        ? $"{plan.Path.Waypoints.Count} waypoints, {plan.Path.Distance:0} m" +
                          (plan.PressedThrough ? "  *** PRESSED THROUGH ***" : string.Empty)
                        : $"NO ROUTE ({plan.Path.Failure})"));
            }
        }

        /// <summary>A whole army ordered at once, which is what the player does.</summary>
        [Fact]
        public void WhatAWholeArmyCostsToOrder()
        {
            foreach (int regiments in new[] { 8, 16, 32, 64 })
            {
                var field = new Battlefield("plains", 99ul);
                Vec2 centre = field.Centre;

                var army = new List<UnitInstance>();

                for (int i = 0; i < regiments; i++)
                {
                    Vec2 at = centre + new Vec2((i % 8) * 60f - 240f, (i / 8) * 40f - 200f);
                    army.Add(field.Add(0, "spearmen", at, Facing.FromDegrees(90f)));
                }

                foreach (UnitInstance unit in army) Battlefield.Hold(unit);

                var watch = Stopwatch.StartNew();

                long legs = 0;
                long searched = 0;

                foreach (UnitInstance unit in army)
                {
                    Plan plan = Marching.PlanTo(
                        field.State, unit, field.Pathfinder,
                        unit.Position + new Vec2(0f, 350f), planner: RoutePlanners.TheSearch);

                    legs += plan.Effort.Legs;
                    if (plan.Effort.Searched) searched++;
                }

                watch.Stop();

                _out.WriteLine(
                    $"{regiments,3} regiments ordered   {watch.Elapsed.TotalMilliseconds,8:0.0} ms   " +
                    $"{searched} searched   {legs} legs in total");
            }
        }
    }
}
