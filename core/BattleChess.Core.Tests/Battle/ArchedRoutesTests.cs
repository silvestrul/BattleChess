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

        // ---- The entering front is held for a stretch, not for a leg ----------

        [Fact]
        public void ARegimentSetOffAcrossItsOwnFrontStillFindsTheWayRound()
        {
            // Taken line for line from `logs/battle-20260814-135004.log`:
            //
            //   Cavalry is pushing through its own Spearmen — no way round it
            //   and no gap to thread.
            //   marching from (313,413) to (169,159) — that line is -120°,
            //   120° off at 3°/s — 40 ticks to come round.
            //
            // Open field, one regiment in the way, and rung two found nothing.
            //
            // The cause is M23's other half, overdone. Each leg is checked at
            // the front it will be walked on *and* at the front it is entered
            // on — but a regiment 120° off its line of march presents its whole
            // forty-metre frontage broadside to it, and asking for that corridor
            // to be clear along the entire leg refuses nearly everything. The
            // entering front is held for the stretch the wheel takes, not for
            // the leg.
            var field = new Battlefield("plains", 49000);

            UnitInstance blocker = field.Add(0, "spearmen", field.Centre, Facing.East);
            Battlefield.Hold(blocker);

            // A start area, not an empty field. One regiment on the line is not
            // enough to reproduce this: the recorded battle had six drawn up
            // together, and it is the crowd that makes a broadside corridor
            // impossible to fit down.
            foreach (Vec2 at in new[]
            {
                new Vec2(-90f, 60f), new Vec2(-40f, -75f), new Vec2(70f, -70f), new Vec2(120f, 40f),
            })
            {
                UnitInstance nearby = field.Add(0, "spearmen", field.Centre + at, Facing.East);
                Battlefield.Hold(nearby);
            }

            // Facing due east and sent south-west: the recorded march is
            // (313,413) to (169,159), so half of that offset either side of the
            // regiment that was in the way.
            var half = new Vec2(72f, 127f);

            UnitInstance mover = field.Add(0, "cavalry", field.Centre + half, Facing.East);
            Vec2 destination = field.Centre - half;

            Facing line = Facing.FromVector(destination - mover.Position);

            _out.WriteLine($"the line is {line.Degrees:0}°.\n");
            _out.WriteLine($"{"facing",-10}{"off the line",-14}{"rung",-22}");
            _out.WriteLine(new string('-', 46));

            // The same regiment, the same line, the same ground — and only the
            // front it happens to be standing on when the order arrives is
            // varied. The planner should not care: it is about to come round
            // onto the line whatever it is pointing at now.
            var pressed = new List<float>();

            for (float off = 0f; off <= 180f; off += 30f)
            {
                mover.Facing = line.RotatedBy(off * MathF.PI / 180f);

                Plan plan = Marching.PlanTo(field.State, mover, field.Pathfinder, destination);

                string rung =
                    plan.PressedThrough ? "3 — through its own"
                    : plan.Path.CellsExplored > 0 ? "search"
                    : plan.Hold != null ? "2 — crabbed"
                    : plan.Path.Waypoints.Count > 2 ? "2 — round it"
                    : "1 — straight there";

                _out.WriteLine($"{mover.Facing.Degrees,-10:0}{off,-14:0}{rung,-22}");

                if (plan.PressedThrough) pressed.Add(off);
            }

            Assert.True(pressed.Count == 0,
                $"It shouldered through its own at {string.Join("°, ", pressed)}° off the line, and walked " +
                "round the same regiments on the same ground when it happened to be pointing the right " +
                "way. Which front it is standing on decides nothing about where it can go — it is about " +
                "to come round onto the line.");
        }

        [Fact]
        public void ARegimentStandingInItsOwnLineCanStillLeaveIt()
        {
            // `logs/battle-20260814-140444.log`, and the arrangement is the
            // whole point. All eight press-throughs in that recording set off
            // from the same forty metres of ground — (156..213, 149..171) — and
            // the thing in the way was always a regiment the cavalry was already
            // standing shoulder to shoulder with:
            //
            //   3230 > Cavalry is pushing through its own Archers — no way round
            //          it and no gap to thread.
            //   3230   marching from (203,164) to (345,333) — that line is 50°,
            //          180° off at 3°/s.
            //
            // A line drawn up flush is not a fault, it is what a line is (M2).
            // The steering has always known that a body you are already touching
            // cannot be what your next step ran into. The planner never learnt
            // it, so from inside its own line every candidate leg reported a
            // collision on the first metre and rung two had nothing to offer.
            var field = new Battlefield("plains", 50000);

            UnitInstance mover = field.Add(0, "cavalry", field.Centre, Facing.East);

            // Its neighbours in the line, flush either side, and one behind —
            // forty-two metres apart for a forty-metre frontage, which is the
            // shoulder-to-shoulder M2 exists to permit.
            foreach (Vec2 at in new[] { new Vec2(0f, 42f), new Vec2(0f, -42f), new Vec2(-24f, 0f) })
            {
                UnitInstance neighbour = field.Add(0, "archers", field.Centre + at, Facing.East);
                Battlefield.Hold(neighbour);
            }

            // Away and clear, up and to the right, exactly as drawn.
            Vec2 destination = field.Centre + new Vec2(142f, 169f);

            _out.WriteLine($"the line is {Facing.FromVector(destination - mover.Position).Degrees:0}°, " +
                           $"and it is standing on {mover.Facing.Degrees:0}°.\n");

            var pressed = new List<float>();

            _out.WriteLine($"{"standing on",-14}{"rung",-22}");
            _out.WriteLine(new string('-', 36));

            for (float off = 0f; off < 360f; off += 45f)
            {
                mover.Facing = Facing.FromDegrees(off);

                Plan plan = Marching.PlanTo(field.State, mover, field.Pathfinder, destination);

                string rung =
                    plan.PressedThrough ? "3 — through its own"
                    : plan.Path.CellsExplored > 0 ? "search"
                    : plan.Hold != null ? "2 — crabbed"
                    : plan.Path.Waypoints.Count > 2 ? "2 — round it"
                    : "1 — straight there";

                _out.WriteLine($"{off,-14:0}{rung,-22}");

                if (plan.PressedThrough) pressed.Add(off);
            }

            Assert.True(pressed.Count == 0,
                $"Standing in its own line it shouldered through them at {string.Join("°, ", pressed)}° " +
                "rather than walking out of it. The neighbours it is drawn up beside are where it is " +
                "standing, not what is in its way.");
        }

        [Fact]
        public void ARegimentAlreadyLappingWhatIsAheadStillGoesRoundIt()
        {
            // `logs/battle-20260814-144010.log`. Five press-throughs, four of
            // them the same regiment against the same Archers, each setting off
            // from ground it had just finished a march on:
            //
            //   3388   reached its destination ... averaging 3,7 m/s
            //   3398 > pushing through its own Archers — no way round it and no
            //          gap to thread.
            //   3398   marching from (140,146) to (306,308) — that line is 44°.
            //
            // A regiment that comes to rest lapping one of its own and is then
            // ordered off past it cannot plan a way round: every candidate leg
            // starts inside the very body it is trying to get round, so the
            // sweep collides on metre zero of all of them.
            //
            // M25 excuses a lapped body abreast or behind, which is what stops
            // two regiments swapping places from walking through each other.
            // Ahead is the case it deliberately does not excuse — and this is
            // the case it left behind.
            var field = new Battlefield("plains", 51000);

            UnitInstance mover = field.Add(0, "cavalry", field.Centre, Facing.East);

            // Lapping, and lying along the line of march rather than beside it.
            var along = new Vec2(MathF.Cos(44f * MathF.PI / 180f), MathF.Sin(44f * MathF.PI / 180f));

            UnitInstance ahead = field.Add(0, "archers", field.Centre + along * 22f, Facing.East);
            Battlefield.Hold(ahead);

            foreach (Vec2 at in new[] { new Vec2(0f, 48f), new Vec2(0f, -48f) })
            {
                UnitInstance beside = field.Add(0, "spearmen", field.Centre + at, Facing.East);
                Battlefield.Hold(beside);
            }

            Vec2 destination = field.Centre + along * 232f;

            _out.WriteLine($"lapping the Archers by " +
                           $"{OrientedRect.OverlapFraction(mover.Shape, ahead.Shape):0.00} of a regiment.");

            Plan plan = Marching.PlanTo(field.State, mover, field.Pathfinder, destination);

            string rung =
                plan.PressedThrough ? "3 — through its own"
                : plan.Path.CellsExplored > 0 ? "search"
                : plan.Hold != null ? "2 — crabbed"
                : plan.Path.Waypoints.Count > 2 ? "2 — round it"
                : "1 — straight there";

            _out.WriteLine($"rung {rung}, {plan.Path.Waypoints.Count} waypoints.");

            Assert.False(plan.PressedThrough,
                "Standing half inside its own Archers and sent off past them, it shouldered through " +
                "rather than stepping out and round. Being already inside a body is the reason a way " +
                "round is needed, not a reason there cannot be one.");
        }

        [Fact]
        public void SqueezingPastACornerIsOneMovementRatherThanAStutter()
        {
            // "They still stutter a bit when they would hit the edges of some
            // units."
            //
            // **This does not reproduce it, and says so rather than pretending.**
            // The suspicion was the berth, which is edge-triggered — it fires on
            // the step that would newly close inside it, so it looks like it
            // should shimmy: refuse the step in, edge out, be clear, step in
            // again. A second threshold to hold the passage open was written and
            // measured, and made this arrangement *worse*: 116 ticks sideways
            // with 5 changes of direction against 78 and 3 without. It was
            // thrown away. See finding 15.
            //
            // Note what the recording actually shows — "and that held for 15
            // ticks (9 times over)" — is a decision *reported* nine times, not
            // taken nine times, and that much is now fixed: the detour says
            // itself on the tick it is settled. Whether anything visible remains
            // is for the next play-test.
            //
            // What this does hold is the outcome: a regiment slaloming past
            // three corners crosses them as movements rather than as a shimmy,
            // and it will catch a regression that makes that untrue.
            var field = new Battlefield("plains", 52000);

            // Two corners, not one. The recorded stutter alternates between
            // two neighbours fifteen ticks apart, and a single body is passed
            // cleanly — the first version of this test measured one corner and
            // read the same 1 change of direction whether the fix was in or out.
            foreach (Vec2 at in new[] { new Vec2(-40f, 34f), new Vec2(40f, -34f), new Vec2(120f, 34f) })
            {
                UnitInstance corner = field.Add(0, "spearmen", field.Centre + at, Facing.East);
                Battlefield.Hold(corner);
            }

            UnitInstance mover = field.Add(0, "cavalry", field.Centre - new Vec2(240f, 0f), Facing.East);
            Vec2 destination = field.Centre + new Vec2(240f, 0f);

            field.March(mover, destination);

            var log = new Quiet();

            // The line of march is due east, so anything across it is sideways.
            Vec2 was = mover.Position;
            int flips = 0;
            int sideways = 0;
            float last = 0f;

            Advance(field, log, turns: 16, () =>
            {
                float across = mover.Position.Y - was.Y;
                was = mover.Position;

                if (MathF.Abs(across) < 0.05f) return;

                sideways++;

                if (last != 0f && MathF.Sign(across) != MathF.Sign(last)) flips++;

                last = across;
            });

            _out.WriteLine($"{sideways} ticks of sideways movement, {flips} changes of direction; " +
                           $"finished {Vec2.Distance(mover.Position, destination):0} m short.");

            Assert.True(sideways > 20,
                $"Only {sideways} ticks moved sideways at all, so it never had to squeeze past anything " +
                "and this proves nothing.");

            // Going round a corner and coming back onto the march is two changes
            // of direction. A shimmy is one every other tick. Generous, because
            // arriving and dressing add a few of their own — the fault this
            // guards against reads in the dozens.
            Assert.True(flips < sideways / 4,
                $"It changed sideways direction {flips} times in {sideways} ticks of sideways movement. " +
                "Squeezing past a corner is one movement, not a shimmy against the berth.");
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

            // A wall right across the field, so rung three is genuinely the
            // only answer. Five blocks is not enough any more: standing off
            // further finds a way round the end of a short wall, and it is
            // right to.
            for (int i = -12; i <= 12; i++)
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
