using System;
using System.Collections.Generic;
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

        /// <summary>
        /// The same body crossing the line goes on being noticed, rather than
        /// being written off as somebody already known about.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Straight out of the play-test recording of 1 Sep 2026</b>
        /// (<c>logs/battle-20260901-114049.log</c>). Spearmen U12 were sent
        /// 740 m across the Great Field and friendly Cavalry U3 was driven
        /// across their line by hand. The cadence fired <b>once</b>, at tick
        /// 260, and drew a way round nine metres wide - enough at that instant,
        /// because the horse were still sixty metres short of the line. The
        /// cavalry then rode on across it, was re-ordered five more times, and
        /// finished sitting on the spearmen's own destination. The spearmen
        /// never asked again until tick 460, when the way was clear anyway.
        /// </para>
        /// <para>
        /// The cause was the guard against thrashing: a re-plan needed the leg
        /// to meet a <i>different body</i> than last time, and it was the same
        /// cavalry every time. <b>Identity was never the right question</b> - a
        /// body that has moved two hundred metres is new ground whatever its
        /// name - and the only blocker that may be ignored is the one a
        /// declared press-through was drawn against.
        /// </para>
        /// <para>
        /// Measured as the number of times the route is redrawn, which is the
        /// thing that failed. Asserting on the final positions instead would
        /// pass or fail on whether contact happened to halt the marcher, which
        /// is a different rule.
        /// </para>
        /// <para>
        /// <b>This test does not yet discriminate, and that is written down
        /// rather than hidden.</b> It passes both with the identity latch and
        /// without it, because the blocker here leaves the line between beats
        /// and comes back, which changes its identity from nobody to somebody
        /// and lets even the old rule fire. The recorded fault needs a blocker
        /// that stays continuously in view while moving to genuinely different
        /// ground, and this arrangement does not produce one. It is kept
        /// because the behaviour it asserts is right and worth guarding; it is
        /// <b>not</b> evidence that the recorded fault is fixed. See open
        /// finding 29.
        /// </para>
        /// </remarks>
        [Fact]
        public void TheSameBodyMovingAcrossTheLineIsNoticedEveryTimeItMoves()
        {
            var field = new Battlefield("plains", 5504);

            UnitInstance foot = field.Add(0, "swordsmen", field.Centre - new Vec2(500f, 0f), Facing.East);
            Vec2 goal = field.Centre + new Vec2(500f, 0f);

            field.March(foot, goal, Stance.Defend);

            Assert.Equal(2, foot.Route!.Waypoints.Count);

            // Well clear of the line to begin with, so nothing fires yet.
            UnitInstance crosser = field.Add(0, "cavalry", field.Centre + new Vec2(-100f, -140f), Facing.North);
            Battlefield.Hold(crosser);

            // Then it walks across, forty metres of ground at a time. Moved by
            // hand rather than marched: what is being tested is the marcher
            // noticing, and a second pathfinder in the arrangement would make
            // the answer depend on two rules at once.
            MovementRoute? last = foot.Route;
            int redrawn = 0;

            for (int step = 0; step < 6; step++)
            {
                crosser.Position = field.Centre + new Vec2(-100f + step * 30f, -140f + step * 45f);

                field.RunTurns(1);

                if (!ReferenceEquals(foot.Route, last))
                {
                    redrawn++;
                    last = foot.Route;
                }

                _out.WriteLine(
                    $"step {step}: horse at {crosser.Position}, foot at {foot.Position}, " +
                    $"route {(foot.Route == null ? "none" : foot.Route.Waypoints.Count + " waypoints")}, " +
                    $"redrawn {redrawn}");
            }

            // Non-vacuity: an arrangement the horse never crosses would leave
            // nothing to notice, and nought redraws would be correct (W9).
            Assert.True(redrawn > 0,
                "The horse never got in the way at all, so this arrangement is not testing the rule.");

            Assert.True(redrawn > 1,
                $"The route was redrawn {redrawn} time(s) while the same body walked right across the " +
                "line. This is the recorded fault: the blocker was the same body as last time, so the " +
                "cadence wrote it off as old news and never looked again.");
        }

        /// <summary>
        /// A way round is drawn once and kept, not redrawn a little worse on
        /// every beat.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>[M140], out of `logs/battle-20260901-182703.log`.</b> Spearmen
        /// crossed by cavalry re-planned five times in thirteen ticks, and the
        /// detour deepened every time - 8 m off the straight line, then 20, 31,
        /// 40, 47, the last three reporting no gap left to thread at all. Each
        /// answer was drawn from a few metres further along than the one
        /// before, so the regiment chased a shadow instead of committing to a
        /// side.
        /// </para>
        /// <para>
        /// [M21] says a detour is committed until the thing it went round is
        /// behind you. The route now remembers what it was drawn <i>for</i>,
        /// which the old latch could not, because that recorded what the first
        /// look happened to see - and a fresh way round starts clear, so it
        /// remembered nobody and re-armed.
        /// </para>
        /// <para>
        /// <b>This test does not discriminate, and that is written down rather
        /// than hidden.</b> It passes with commitment and without it, because a
        /// blocker standing still does not produce the deepening detour - that
        /// needed a body <i>moving</i> across the line, so that each re-plan
        /// was drawn against different geometry. It is kept because the
        /// behaviour it asserts is right and worth guarding against a future
        /// change; it is <b>not</b> evidence that the recorded fault is fixed.
        /// Finding 31 says to close that from a recording.
        /// </para>
        /// </remarks>
        [Fact]
        public void AWayRoundIsDrawnOnceAndNotDeepenedEveryBeat()
        {
            var field = new Battlefield("plains", 5505);

            UnitInstance foot = field.Add(0, "swordsmen", field.Centre - new Vec2(400f, 0f), Facing.East);
            Vec2 goal = field.Centre + new Vec2(400f, 0f);

            field.March(foot, goal, Stance.Defend);
            Assert.Equal(2, foot.Route!.Waypoints.Count);

            UnitInstance inTheWay = field.Add(0, "spearmen", field.Centre, Facing.North);
            Battlefield.Hold(inTheWay);

            var offsets = new List<float>();
            int redrawn = 0;
            MovementRoute? last = foot.Route;

            for (int turn = 0; turn < 8; turn++)
            {
                field.RunTurns(1);

                if (foot.Route == null) break;

                if (!ReferenceEquals(foot.Route, last))
                {
                    redrawn++;
                    last = foot.Route;

                    float worst = 0f;
                    foreach (Vec2 at in foot.Route.Waypoints)
                        worst = MathF.Max(worst, MathF.Abs(at.Y - field.Centre.Y));

                    offsets.Add(worst);
                }
            }

            _out.WriteLine($"redrawn {redrawn} time(s); offsets " + string.Join(", ", offsets));

            // Non-vacuity: it has to have gone round at all, or a count of one
            // redraw is right for the wrong reason (W9).
            Assert.True(redrawn > 0,
                "It never re-planned, so the spearmen were never in the way and this measures nothing.");

            Assert.True(redrawn <= 2,
                $"The way round was redrawn {redrawn} times against one regiment standing still. A detour " +
                "is committed until the thing it went round is behind you (M21); redrawing it every beat " +
                "is what deepened the recorded detour from 8 m to 47 m and left no gap to thread.");
        }
    }
}
