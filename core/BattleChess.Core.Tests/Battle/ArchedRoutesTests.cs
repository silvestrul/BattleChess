using System;
using System.Collections.Generic;
using BattleChess.Contracts;
using BattleChess.Rules;
using Xunit;
using Xunit.Abstractions;

namespace BattleChess.Tests.Battle
{
    /// <summary>
    /// Walking a route that bends: the front held, the corners cut, the cost paid.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>M23, M1a, M20.</b> Three faults reported from one play-test, and two of
    /// them are the same fault. A route that bends is walked by a regiment
    /// holding <i>one</i> front for the whole journey — the bearing from where it
    /// started to where it was sent — because the rule that picks a front was
    /// written when every route was a single line and "the line of march" could
    /// only mean one thing. So it wheels once and then crabs the rest.
    /// </para>
    /// <para>
    /// And because the planner checks each leg at the facing the regiment had
    /// when it planned, while the walk holds the start-to-destination bearing,
    /// and the leg's own bearing is a third thing again — three answers to one
    /// question — the shape that was checked is not the shape that travels. That
    /// is what clips the corner of whatever it just went round.
    /// </para>
    /// <para>
    /// Written before the code.
    /// </para>
    /// </remarks>
    public sealed class ArchedRoutesTests
    {
        private readonly ITestOutputHelper _out;

        public ArchedRoutesTests(ITestOutputHelper output) => _out = output;

        private sealed class Quiet : IBattleLog
        {
            public readonly List<string> Lines = new List<string>();
            public void Record(in BattleLogEntry entry) => Lines.Add(entry.Message);
        }

        /// <summary>
        /// One of its own square across a short line of march, so going round is
        /// a sharp two-leg bend rather than a nudge.
        /// </summary>
        /// <remarks>
        /// The distance is the whole of the arrangement. At 220 m either side of
        /// the blocker the detour is 48 m off a 440 m line — a bend of twelve
        /// degrees, which is smaller than the tolerance any of these tests can
        /// reasonably use, so every one of them passed on the first run while the
        /// fault was still in. At seventy the same detour is a bend of thirty-four
        /// each way, which is a manoeuvre a regiment either performs or does not.
        /// </remarks>
        private static Battlefield SomethingToGoRound(
            out UnitInstance mover, out Vec2 destination, out UnitInstance blocker, IBattleLog? log = null)
        {
            var field = new Battlefield("plains", 46000);

            blocker = field.Add(0, "spearmen", field.Centre, Facing.East);
            Battlefield.Hold(blocker);

            mover = field.Add(0, "swordsmen", field.Centre - new Vec2(70f, 0f), Facing.East);
            destination = field.Centre + new Vec2(70f, 0f);

            field.March(mover, destination, log: log);

            return field;
        }

        /// <summary>The sharpest change of bearing this route asks for, in degrees.</summary>
        private static float SharpestBend(UnitInstance unit, IReadOnlyList<Vec2> legs)
        {
            Facing was = unit.Facing;
            float sharpest = 0f;

            for (int i = 1; i < legs.Count; i++)
            {
                Facing bearing = Facing.FromVector(legs[i] - legs[i - 1]);
                float bend = Facing.AbsoluteDelta(was, bearing) * 180f / MathF.PI;

                if (bend > sharpest) sharpest = bend;
                was = bearing;
            }

            return sharpest;
        }

        private static void Advance(Battlefield field, IBattleLog log, int turns, Action each)
        {
            var clock = new BattleClock();
            foreach (IBattleSystem system in field.Clock.Systems) clock.Add(system);

            for (int tick = 0; tick < BattleClock.TicksPerTurn * turns; tick++)
            {
                clock.Advance(field.State, log);
                each();
            }
        }

        // ---- M23: a bend is a wheel ------------------------------------------

        [Fact]
        public void ARouteThatBendsTwiceComesRoundTwice()
        {
            Battlefield field = SomethingToGoRound(
                out UnitInstance mover, out Vec2 destination, out _);

            IReadOnlyList<Vec2> legs = mover.Route!.Waypoints;

            _out.WriteLine($"{legs.Count} waypoints.");
            for (int i = 1; i < legs.Count; i++)
                _out.WriteLine($"   leg {i}: bearing {Facing.FromVector(legs[i] - legs[i - 1]).Degrees:0}°");

            Assert.True(legs.Count > 2, "The arrangement did not produce a bend, so this proves nothing.");

            // The tolerance below is 20°. A route whose bends are smaller than
            // that is satisfied by a regiment that never turns at all, which is
            // exactly how the first version of this test passed while the fault
            // was still in.
            float sharpest = SharpestBend(mover, legs);
            _out.WriteLine($"sharpest bend {sharpest:0}°.");

            Assert.True(sharpest > 25f,
                $"The sharpest bend on this route is {sharpest:0}°, inside the tolerance this test uses. " +
                "It would pass without the regiment turning at all. Fix the arrangement, not the bar.");

            // Every bearing the regiment is ever seen holding.
            var held = new List<float>();
            var log = new Quiet();

            Advance(field, log, turns: 16, () => held.Add(mover.Facing.Degrees));

            // For each leg, did it ever actually point along it?
            int wheeledOnto = 0;

            for (int i = 1; i < legs.Count; i++)
            {
                Facing want = Facing.FromVector(legs[i] - legs[i - 1]);

                foreach (float was in held)
                {
                    if (Facing.AbsoluteDelta(Facing.FromDegrees(was), want) * 180f / MathF.PI > 20f) continue;

                    wheeledOnto++;
                    break;
                }
            }

            _out.WriteLine($"came round onto {wheeledOnto} of {legs.Count - 1} legs; " +
                           $"finished {Vec2.Distance(mover.Position, destination):0} m short.");

            Assert.Equal(legs.Count - 1, wheeledOnto);
        }

        [Fact]
        public void TheSecondLegIsNotWalkedPointingAlongTheFirst()
        {
            // The same fault stated as the thing it looks like on screen: a
            // regiment that bent right and is now travelling left-ish, still
            // pointing right, sliding along at two fifths of pace for the rest of
            // the march. M3 has always said a move faces its line of march.
            Battlefield field = SomethingToGoRound(out UnitInstance mover, out Vec2 destination, out _);

            var log = new Quiet();

            int marching = 0;
            int badlyOff = 0;

            Advance(field, log, turns: 16, () =>
            {
                if (mover.Route == null || mover.Route.IsComplete) return;

                Vec2 toward = mover.Route.Target - mover.Position;
                if (toward.Length < 10f) return;

                marching++;

                if (Facing.AbsoluteDelta(mover.Facing, Facing.FromVector(toward)) * 180f / MathF.PI > 25f)
                    badlyOff++;
            });

            _out.WriteLine($"{badlyOff} of {marching} marching ticks spent more than 25° off the leg " +
                           "actually being walked.");

            Assert.True(marching > 0, "It never marched, so this proves nothing.");

            // Measured as time spent, not as the worst instant. A regiment
            // coming round onto a new leg is legitimately well off it for as
            // long as the wheel takes, so the worst reading is large either way
            // and discriminates nothing. What tells the two apart is whether it
            // ever gets there: with the front pinned to the start-to-destination
            // bearing, every tick of every leg is off and this reads 100%.
            Assert.True(badlyOff < marching / 2,
                $"It spent {badlyOff} of {marching} ticks pointing somewhere other than the leg it was " +
                "walking. A regiment that bends and keeps pointing the old way is crabbing the rest of " +
                "its journey for no reason.");
        }

        // ---- The plan and the body agree about what is travelling ------------

        [Fact]
        public void GoingRoundDoesNotClipWhatItWentRound()
        {
            Battlefield field = SomethingToGoRound(
                out UnitInstance mover, out Vec2 destination, out UnitInstance blocker);

            Assert.True(mover.Route!.Waypoints.Count > 2, "It did not go round anything.");

            var log = new Quiet();

            float worst = 0f;
            float closest = float.MaxValue;

            Advance(field, log, turns: 16, () =>
            {
                float lapped = OrientedRect.OverlapFraction(mover.Shape, blocker.Shape);
                if (lapped > worst) worst = lapped;

                float gap = OrientedRect.GapBetween(mover.Shape, blocker.Shape);
                if (gap < closest) closest = gap;
            });

            _out.WriteLine($"worst overlap {worst:0.000} of a regiment, closest approach {closest:0.0} m; " +
                           $"finished {Vec2.Distance(mover.Position, destination):0} m short.");

            // It has to actually go past the thing. A route that arches so wide
            // it never comes near cannot clip it, and would pass this while
            // proving nothing.
            Assert.True(closest < 30f,
                $"It never came within {closest:0} m of what it went round, so nothing was at risk.");

            // The same grazing tolerance the rest of the rules use. Corners
            // brushing is not a collision; a fifth of a regiment inside another
            // is the route going somewhere it was told was clear.
            Assert.True(worst <= OrderSystem.GrazingTolerance,
                $"It went round its own and still ended up {worst:0.00} of a regiment inside it. The plan " +
                "checked one shape and the march walked another.");
        }

        // ---- M20: the charge covers every tick it applies to ------------------

        [Fact]
        public void EveryTickSpentInsideItsOwnIsCharged()
        {
            // "Sometimes it goes through units at no cost." Asked as a property
            // rather than hunted as a scenario: whatever route got it there, if
            // it is inside somebody it pays. Counting from outside the rule that
            // does the charging is the whole point — a charge that agrees with
            // itself proves nothing.
            var field = new Battlefield("plains", 47000);

            // A wall with no way round and no gap, so rung three is the only
            // answer and the passage is long enough to count.
            for (int i = -2; i <= 2; i++)
            {
                UnitInstance wall = field.Add(0, "spearmen", field.Centre + new Vec2(0f, i * 40f), Facing.East);
                Battlefield.Hold(wall);
            }

            UnitInstance mover = field.Add(0, "cavalry", field.Centre - new Vec2(200f, 0f), Facing.East);
            Vec2 destination = field.Centre + new Vec2(200f, 0f);

            var log = new Quiet();
            field.March(mover, destination, log: log);

            int overlapped = 0;

            Advance(field, log, turns: 16, () =>
            {
                if (mover.Route == null || mover.Route.IsComplete) return;

                foreach (UnitInstance other in field.State.UnitsOnField())
                {
                    if (other.Id == mover.Id) continue;
                    if (OrientedRect.OverlapFraction(mover.Shape, other.Shape) <= OrderSystem.GrazingTolerance)
                        continue;

                    overlapped++;
                    break;
                }
            });

            _out.WriteLine($"inside its own for {overlapped} ticks; charged for {mover.TicksInsideItsOwn}.");

            Assert.True(overlapped > 0, "It never went inside anybody, so this proves nothing.");

            // Not equality: the tick a march finishes is counted by one and not
            // the other, and a router is exempt by design. Anything more than a
            // couple of ticks of daylight is a route being walked through men
            // for free.
            Assert.True(mover.TicksInsideItsOwn >= overlapped - 2,
                $"It was inside one of its own for {overlapped} ticks and charged for " +
                $"{mover.TicksInsideItsOwn}. Some way of getting through men is not being paid for.");
        }

        // ---- M1a, unconditionally --------------------------------------------

        [Fact]
        public void AHoldingRegimentIsNotShovedByAnOverlapItDidNotCause()
        {
            // M1a said "a regiment that was not told to move does not move", and
            // the code said it only about a declared press-through. Every other
            // way of ending up inside somebody — a corner clipped going round, an
            // overlap the steering walked into — still moved the regiment
            // standing still, half a step a tick, for as long as it lasted.
            var field = new Battlefield("plains", 48000);

            var stoodAt = new Dictionary<UnitId, Vec2>();

            UnitInstance holding = field.Add(0, "spearmen", field.Centre, Facing.East);
            Battlefield.Hold(holding);
            stoodAt[holding.Id] = holding.Position;

            // Sent to ground somebody is already standing on. The cast refuses
            // it — a regiment may shoulder through men on the way and may not
            // come to rest inside them — so this falls through to the search,
            // which walks it in and leaves it there overlapping. No press-through
            // is ever declared, so the old exemption never applied and the
            // shuffle ran every tick for the rest of the battle.
            //
            // Two arrangements were tried before this one and both failed their
            // own guard below: a charge at an enemy beyond a wall routes round
            // the wall, and a march into a wall with no way round declares a
            // press-through and is exempt already. The case that bites is the
            // one where nobody ever decided to overlap anybody.
            UnitInstance mover = field.Add(0, "cavalry", field.Centre - new Vec2(180f, 0f), Facing.East);

            // Six metres off its centre, which is as far in as this can be
            // pushed. Sending it to the exact centre is worse, not better: the
            // placement search relocates a destination nobody can stand on, so
            // the regiment halts short and never overlaps at all.
            field.March(mover, field.Centre + new Vec2(6f, 0f));

            var log = new Quiet();
            int lapped = 0;

            Advance(field, log, turns: 16, () =>
            {
                foreach (UnitInstance u in field.State.UnitsOnField())
                {
                    if (!stoodAt.ContainsKey(u.Id)) continue;

                    if (OrientedRect.OverlapFraction(mover.Shape, u.Shape) <= OrderSystem.GrazingTolerance)
                        continue;

                    lapped++;
                    break;
                }
            });

            float worst = 0f;

            foreach (UnitInstance u in field.State.UnitsOnField())
            {
                if (!stoodAt.TryGetValue(u.Id, out Vec2 was)) continue;

                float shifted = Vec2.Distance(u.Position, was);
                if (shifted > worst) worst = shifted;
            }

            _out.WriteLine($"the charge was inside the line for {lapped} ticks; " +
                           $"worst drift on a holding regiment: {worst:0.0} m.");

            // Nobody was ever overlapped means nobody was ever at risk of being
            // shoved, and a zero here would say nothing at all.
            Assert.True(lapped > 0,
                "The charge never ended up inside the line it was sent through, so this proves nothing.");

            // The correction goes wholly to the mover, so this reads exactly
            // zero rather than nearly zero. A metre of slack and no more: the
            // bar is the whole of what tells the two rules apart, and at 5 m
            // this passed under the old shared shuffle too.
            //
            // The margin is thin and said so on purpose. **Verified by
            // disabling:** forcing the shared branch gives 1,5 m here against
            // 0,0 with the rule in. One tick of overlap is all this arrangement
            // can sustain — the mover is pushed clear immediately, which is the
            // rule working — so the drift under the old behaviour is one
            // shuffle's worth. What made it hundreds of metres in play was the
            // overlap lasting hundreds of ticks, and that case is now covered
            // twice over: by the press-through exemption and by this.
            Assert.True(worst < 1f,
                $"A regiment with no orders was moved {worst:0.0} m by one marching into it. Whoever is " +
                "marching takes the whole correction; a body standing still gives no ground at all.");
        }
    }
}
