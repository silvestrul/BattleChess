using System;
using System.Collections.Generic;
using BattleChess.Contracts;
using BattleChess.Rules;
using Xunit;
using Xunit.Abstractions;

namespace BattleChess.Tests.Battle
{
    /// <summary>
    /// The same gap, crossed at every angle.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Written before the model that is meant to pass it</b>, which is the
    /// point of it. Every previous movement fix was measured against an
    /// arrangement reconstructed after the fact from a recording, and each one
    /// looked right until the next arrangement broke it. This one is a gate: two
    /// bodies, one gap, and the only variable is the angle the march crosses at.
    /// </para>
    /// <para>
    /// Two spearmen 50 m apart, both 20 m deep, so <b>30 m of daylight</b>
    /// between them. A cavalry regiment is 40 m across its front and 20 m
    /// side-on, so the gap admits it at every angle, threading if nothing else.
    /// A correct planner routes all nineteen.
    /// </para>
    /// <para>
    /// <b>Baseline on the ladder, measured 16 Aug:</b>
    /// </para>
    /// <code>
    ///    0°          pressed through, declared
    ///    5° to 55°   fell to the search: two waypoints, straight through both
    ///   60° to 90°   went round correctly
    /// </code>
    /// <para>
    /// Twelve of nineteen come back as a straight line through two friendly
    /// regiments, and because the search answered rather than rung three, the
    /// route is not flagged as pressing through — so nothing charges it and no
    /// rule ever agreed to it. That is the designer's *"goes through units
    /// without colliding"*, and the angular band is why *"it is bugged at
    /// diagonals"* was the right diagnosis.
    /// </para>
    /// <para>
    /// The cause is structural rather than a missing case. Every candidate
    /// generator in the ladder measures against the <i>line of march</i>, so its
    /// accuracy falls away as the march goes diagonal to the bodies; and beneath
    /// them sits a pathfinder that takes no unit and no battle state, and so
    /// cannot see a friendly regiment at all.
    /// </para>
    /// </remarks>
    public sealed class ApproachAngleTests
    {
        private readonly ITestOutputHelper _out;

        public ApproachAngleTests(ITestOutputHelper output) => _out = output;

        /// <summary>How far apart the two bodies stand, centre to centre.</summary>
        private const float ApartMetres = 50f;

        /// <summary>
        /// How far out the march begins and ends.
        /// </summary>
        /// <remarks>
        /// Short on purpose. The same sweep with 110 m of open run-up either side
        /// routes cleanly at every angle, on the ladder, today — so the fault
        /// needs the short hop and the tight gap together, and every recorded
        /// failure had both.
        /// </remarks>
        private const float RunMetres = 45f;

        private static Battlefield TheGapAt(
            float degrees, out UnitInstance mover, out Vec2 destination)
        {
            var field = new Battlefield("plains", 7000ul + (ulong)degrees);

            Vec2 centre = field.Centre;

            foreach (float beside in new[] { -ApartMetres * 0.5f, ApartMetres * 0.5f })
                Battlefield.Hold(
                    field.Add(0, "spearmen", centre + new Vec2(beside, 0f), Facing.FromDegrees(0f)));

            float radians = degrees * MathF.PI / 180f;
            var along = new Vec2(MathF.Cos(radians), MathF.Sin(radians));

            mover = field.Add(0, "cavalry", centre - along * RunMetres, Facing.FromVector(along));
            destination = centre + along * RunMetres;

            return field;
        }

        /// <summary>
        /// Whether a route can be walked without meeting one of its own, asked of
        /// the body squared to each leg.
        /// </summary>
        private static bool EveryLegIsClear(
            Battlefield field, UnitInstance mover, IReadOnlyList<Vec2> waypoints, IReadOnlyList<Facing?>? hold)
        {
            for (int i = 1; i < waypoints.Count; i++)
            {
                Facing front = hold != null && i < hold.Count && hold[i].HasValue
                    ? hold[i]!.Value
                    : Marching.AlongTheLine(waypoints[i - 1], waypoints[i], mover.Facing);

                if (!Marching.IsClearLine(
                        field.State, mover, waypoints[i - 1], waypoints[i], front, leaving: true))
                    return false;
            }

            return true;
        }

        [Fact]
        public void EveryApproachAngleFindsAWayThroughTheGap()
        {
            foreach (IRoutePlanner planner in RoutePlanners.All)
            {
                _out.WriteLine($"--- {planner.Name} ---");
                Sweep(planner);
            }
        }

        private void Sweep(IRoutePlanner planner)
        {
            var failed = new List<string>();

            for (int degrees = 0; degrees <= 90; degrees += 5)
            {
                Battlefield field = TheGapAt(degrees, out UnitInstance mover, out Vec2 destination);

                Plan plan = Marching.PlanTo(
                    field.State, mover, field.Pathfinder, destination, planner: planner);

                bool clear = plan.Found
                             && !plan.PressedThrough
                             && EveryLegIsClear(field, mover, plan.Path.Waypoints, plan.Hold);

                string verdict = !plan.Found ? "no route"
                    : plan.PressedThrough ? "pressed through"
                    : !EveryLegIsClear(field, mover, plan.Path.Waypoints, plan.Hold) ? "walks through somebody"
                    : "clear";

                _out.WriteLine($"  {degrees,3}°  {verdict,-22} {plan.Path.Waypoints.Count} waypoints, " +
                               $"{Marching.SecondsToWalk(field.State, mover, plan.Path.Waypoints, plan.Hold):0} s");

                if (!clear) failed.Add($"{degrees}° ({verdict})");
            }

            _out.WriteLine($"  {19 - failed.Count} of 19 clear.");

            if (planner != RoutePlanners.TheSearch) return;

            Assert.True(failed.Count == 0,
                $"{failed.Count} of 19 approach angles cannot cross a 30 m gap with a regiment 20 m " +
                $"across side-on: {string.Join(", ", failed)}. The gap admits it at every angle, so " +
                "any angle that fails is the planner measuring against the line of march rather than " +
                "against the bodies.");
        }
    }
}
