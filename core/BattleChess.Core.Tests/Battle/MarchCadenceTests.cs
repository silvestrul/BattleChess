using System;
using BattleChess.Contracts;
using BattleChess.Rules;
using Xunit;
using Xunit.Abstractions;

namespace BattleChess.Tests.Battle
{
    /// <summary>
    /// A march already under way is asked, on a beat, whether it still wants
    /// the route it has.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>M11, as the designer restated it.</b> The cadence that existed only
    /// ever fired for a regiment that had stopped getting anywhere, and a
    /// regiment walking cheerfully along a route that has gone wrong is not
    /// stalled - it is making excellent progress toward the wrong thing. Two
    /// cases, both named by the designer:
    /// </para>
    /// <para>
    /// <b>One:</b> going round something that may since have moved off, so the
    /// long way is no longer needed. <b>Two:</b> the leg it is walking, or the
    /// one after, now meets a body that was not there when the route was drawn.
    /// </para>
    /// <para>
    /// And <b>Mx2e</b>: an enemy that cannot be got round ends the order where
    /// the regiment stands, rather than being walked into.
    /// </para>
    /// </remarks>
    public sealed class MarchCadenceTests
    {
        private readonly ITestOutputHelper _out;

        public MarchCadenceTests(ITestOutputHelper output) => _out = output;

        /// <summary>
        /// A regiment sent round one of its own goes straight again once that
        /// regiment has left.
        /// </summary>
        [Fact]
        public void ADetourIsDroppedOnceTheThingItWentRoundHasGone()
        {
            var field = new Battlefield("plains", 5501);

            UnitInstance inTheWay = field.Add(0, "spearmen", field.Centre, Facing.North);
            Battlefield.Hold(inTheWay);

            UnitInstance foot = field.Add(0, "swordsmen", field.Centre - new Vec2(220f, 0f), Facing.East);

            Vec2 goal = field.Centre + new Vec2(220f, 0f);

            field.March(foot, goal, Stance.Defend);

            int bends = foot.Route!.Waypoints.Count;

            _out.WriteLine($"planned round it: {bends} waypoints");

            // Non-vacuity: if it never had a detour there is nothing to drop,
            // and the assertion below would pass for the wrong reason (W9).
            Assert.True(bends > 2,
                $"The arrangement has to force a way round for this to test anything, and the route has " +
                $"{bends} waypoints. Nothing was gone round.");

            field.RunTurns(2);

            // The obstacle leaves. Taken off the field rather than marched
            // away, because what is being tested is the marcher noticing, not
            // the other regiment's own pathfinding.
            inTheWay.State = UnitState.Destroyed;

            field.RunTurns(4);

            _out.WriteLine(
                $"after it left: {(foot.Route == null ? "arrived" : foot.Route.Waypoints.Count + " waypoints")}");

            // Arriving counts: a regiment that finished the march has plainly
            // not spent the rest of it on a detour.
            int left = foot.Route?.Waypoints.Count ?? 2;

            Assert.True(left == 2,
                "With the way clear it should have dropped the detour and be walking straight there. It is " +
                $"still on a {left}-waypoint route round something that has left.");
        }

        /// <summary>
        /// A regiment whose road is blocked after it set off re-plans rather
        /// than walking into the newcomer.
        /// </summary>
        [Fact]
        public void ARouteIsDrawnAgainWhenSomebodyNewStepsOntoIt()
        {
            var field = new Battlefield("plains", 5502);

            UnitInstance foot = field.Add(0, "swordsmen", field.Centre - new Vec2(300f, 0f), Facing.East);

            Vec2 goal = field.Centre + new Vec2(300f, 0f);

            field.March(foot, goal, Stance.Defend);

            Assert.Equal(2, foot.Route!.Waypoints.Count);

            field.RunTurns(2);

            // Somebody arrives on the line, well ahead of where it has got to.
            // Far enough ahead that it is still to be dealt with when the
            // assertion is made: placed at sixty metres and read three turns
            // later, the regiment had already re-planned round it, walked past
            // and gone straight again, so the check found a two-waypoint route
            // and read a success as the failure it was looking for.
            UnitInstance newcomer = field.Add(0, "spearmen", field.Centre + new Vec2(200f, 0f), Facing.North);
            Battlefield.Hold(newcomer);

            field.RunTurns(1);

            int now = foot.Route?.Waypoints.Count ?? 0;

            _out.WriteLine($"after the newcomer arrived: {now} waypoints");

            Assert.True(now > 2,
                "Spearmen stepped onto the line it was walking, and it is still on the two-waypoint route " +
                "it drew before they got there. The cadence is not looking at the legs it is about to walk.");
        }

        /// <summary>
        /// Mx2e: an enemy that cannot be got round ends the order where the
        /// regiment stands.
        /// </summary>
        /// <remarks>
        /// The regiment is boxed by its own on both sides, so there is no way
        /// round the enemy ahead and no pressing through him either - Mx2c's
        /// press is for friends, and there is no shouldering past a formed
        /// enemy. Walking on is the one answer that must not be given.
        /// </remarks>
        [Fact]
        public void AnEnemyThatCannotBeGotRoundEndsTheOrderWhereItStands()
        {
            var field = new Battlefield("plains", 5503);

            UnitInstance foot = field.Add(0, "swordsmen", field.Centre - new Vec2(200f, 0f), Facing.East);

            // Walled in by its own, north and south, along the whole run.
            for (int i = -1; i <= 3; i++)
            {
                UnitInstance north = field.Add(0, "spearmen", field.Centre + new Vec2(i * 60f, 70f), Facing.East);
                UnitInstance south = field.Add(0, "spearmen", field.Centre + new Vec2(i * 60f, -70f), Facing.East);

                Battlefield.Hold(north);
                Battlefield.Hold(south);
            }

            // And an enemy standing across the corridor.
            UnitInstance enemy = field.Add(1, "spearmen", field.Centre + new Vec2(40f, 0f), Facing.West);
            Battlefield.Hold(enemy);

            field.March(foot, field.Centre + new Vec2(200f, 0f), Stance.Defend);

            field.RunTurns(8);

            _out.WriteLine(
                $"order={foot.Order.Kind}, marching={foot.IsMarching}, " +
                $"gap to the enemy={OrientedRect.GapBetween(foot.Shape, enemy.Shape):0} m, " +
                $"casualties={foot.Casualties}");

            Assert.True(
                foot.Order.Kind == OrderKind.Stand || !foot.IsMarching,
                "There is no way past him and no shouldering through him, so the order should have ended " +
                "where the regiment stood. It is still marching at him.");
        }
    }
}
