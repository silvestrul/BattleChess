using System;
using System.Collections.Generic;
using BattleChess.Contracts;
using BattleChess.Rules;
using Xunit;
using Xunit.Abstractions;

namespace BattleChess.Tests.Battle
{
    /// <summary>
    /// What a march costs, and whether the planner and the walker agree about it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>M20, M21, M22.</b> Three faults from the 14 August recording, and they
    /// are one fault wearing three coats: nothing in the rules could say what
    /// anything cost. Routes were chosen on distance while the expense was the
    /// wheel; shouldering through your own men was free; and the arrival line
    /// reported the pace the regiment <i>could</i> have made rather than the one
    /// it did, so none of it showed.
    /// </para>
    /// <para>
    /// The measurement that started it: across 36 marches by one cavalry
    /// regiment, the average change of front was 125° costing 40 ticks, and
    /// wheeling took <b>1,432 of 2,644 ticks</b>. Fifty-four per cent of a battle
    /// spent turning round, invisible to every rule that had an opinion about
    /// where to walk.
    /// </para>
    /// <para>
    /// Written before the code. The ones that pass immediately are the ones to
    /// look at hardest.
    /// </para>
    /// </remarks>
    public sealed class MarchCostTests
    {
        private readonly ITestOutputHelper _out;

        public MarchCostTests(ITestOutputHelper output) => _out = output;

        private sealed class Heard : IBattleLog
        {
            public readonly List<string> Lines = new List<string>();
            public void Record(in BattleLogEntry entry) => Lines.Add(entry.Message);

            public string? FirstSaying(string fragment)
            {
                foreach (string line in Lines) if (line.Contains(fragment)) return line;
                return null;
            }
        }

        private static void Run(Battlefield field, IBattleLog log, int turns)
        {
            var clock = new BattleClock();
            foreach (IBattleSystem system in field.Clock.Systems) clock.Add(system);

            for (int tick = 0; tick < BattleClock.TicksPerTurn * turns; tick++)
                clock.Advance(field.State, log);
        }

        // ---- M22: a route is costed in seconds -------------------------------

        [Fact]
        public void ABendCostsMoreThanTheSameDistanceStraight()
        {
            var field = new Battlefield("plains", 41000);

            UnitInstance unit = field.Add(0, "swordsmen", field.Centre - new Vec2(200f, 0f), Facing.East);

            // Two routes of near-identical length. One runs dead ahead; the other
            // does the same ground as a right angle, so it buys two wheels.
            var straight = new[] { unit.Position, unit.Position + new Vec2(400f, 0f) };
            var bent = new[]
            {
                unit.Position,
                unit.Position + new Vec2(200f, 0f),
                unit.Position + new Vec2(200f, 200f)
            };

            float direct = Marching.SecondsToWalk(field.State, unit, straight);
            float corner = Marching.SecondsToWalk(field.State, unit, bent);

            _out.WriteLine($"400 m straight: {direct:0} s. 400 m round a right angle: {corner:0} s.");

            Assert.True(corner > direct,
                $"A right-angle bend costs the same {corner:0} s as walking the distance straight " +
                $"({direct:0} s). Every bend is a wheel and every wheel is time at a fraction of pace; " +
                "a planner that cannot see that will buy a short cut with a turn worth more than it saves.");
        }

        [Fact]
        public void TheCostOfALineIsWhatWalkingItActuallyTakes()
        {
            // The property that matters, and the one a scenario cannot give: the
            // planner's arithmetic is only worth having if it predicts the
            // walker. Open ground, one regiment, nothing in the way — so any
            // disagreement is the model, not the traffic.
            foreach (float turn in new[] { 0f, 45f, 90f, 180f })
            {
                var field = new Battlefield("plains", 42000ul + (ulong)turn);

                Vec2 from = field.Centre - new Vec2(150f, 0f);
                UnitInstance unit = field.Add(
                    0, "swordsmen", from, Facing.FromRadians(turn * MathF.PI / 180f));

                Vec2 to = from + new Vec2(300f, 0f);

                float predicted = Marching.SecondsToWalk(field.State, unit, new[] { from, to });

                var log = new Heard();
                field.March(unit, to, log: log);

                int took = 0;
                var clock = new BattleClock();
                foreach (IBattleSystem system in field.Clock.Systems) clock.Add(system);

                for (int tick = 1; tick <= BattleClock.TicksPerTurn * 20; tick++)
                {
                    clock.Advance(field.State, log);

                    if (Vec2.Distance(unit.Position, to) > 1f) continue;

                    took = tick;
                    break;
                }

                _out.WriteLine($"{turn,3}° off: predicted {predicted:0} s, took {took} s.");

                Assert.True(took > 0, $"It never arrived at all from {turn}° off.");

                // A quarter either way. This is a model of a wheel, not a
                // simulation of one, and it is used to choose between routes
                // rather than to promise a time — but a model that can be out by
                // a factor is a model that will choose wrong.
                Assert.InRange(predicted, took * 0.75f, took * 1.25f);
            }
        }

        // ---- M22a: rung two compares its two answers -------------------------

        /// <summary>
        /// A wall of friendly regiments with one gap in it, offset so that going
        /// round the end is a real alternative to threading the middle.
        /// </summary>
        /// <param name="ranks">
        /// How deep the wall is. One rank is 20 m of men, which a march is
        /// inside for eight seconds — too brief to measure anything against a
        /// five-hundred-metre journey. Three is a real passage.
        /// </param>
        private static Battlefield AWallWithAGap(
            out UnitInstance mover, out Vec2 destination, float gap, int blocks,
            IBattleLog? log = null, int ranks = 1)
        {
            var field = new Battlefield("plains", 43000);

            float inner = gap * 0.5f + 20f;

            foreach (float side in new[] { 1f, -1f })
            {
                for (int i = 0; i < blocks; i++)
                {
                    for (int rank = 0; rank < ranks; rank++)
                    {
                        UnitInstance wall = field.Add(
                            0, "spearmen",
                            field.Centre + new Vec2(rank * 25f, side * (inner + i * 40f)),
                            Facing.East);

                        Battlefield.Hold(wall);
                    }
                }
            }

            mover = field.Add(0, "swordsmen", field.Centre - new Vec2(250f, 0f), Facing.East);
            destination = field.Centre + new Vec2(250f, 0f);

            field.March(mover, destination, log: log);

            return field;
        }

        [Fact]
        public void AShortCrabBeatsALongArchAndALongCrabDoesNot()
        {
            // M18 rung two is "arching or crabbing, whichever costs less by M17",
            // and until now it was "arching, and crabbing only if that fails" —
            // which is not a comparison at all. One wall is deep enough that
            // going round it is a long way, so the crab should win; the other is
            // two blocks, so going round the end is quicker and should.
            Battlefield deep = AWallWithAGap(out UnitInstance threader, out Vec2 farSide, gap: 30f, blocks: 4);
            Battlefield thin = AWallWithAGap(out UnitInstance archer, out Vec2 pastIt, gap: 30f, blocks: 1);

            Plan threading = Marching.PlanTo(deep.State, threader, deep.Pathfinder, farSide);
            Plan arching = Marching.PlanTo(thin.State, archer, thin.Pathfinder, pastIt);

            bool crabbed = threading.Hold != null;
            bool arched = arching.Hold == null && arching.Path.Waypoints.Count > 2;

            _out.WriteLine($"deep wall: {threading.Path.Waypoints.Count} waypoints, " +
                           $"{(crabbed ? "crabbed" : "not crabbed")}, " +
                           $"{Marching.SecondsToWalk(deep.State, threader, threading.Path.Waypoints, threading.Hold):0} s.");
            _out.WriteLine($"thin wall: {arching.Path.Waypoints.Count} waypoints, " +
                           $"{(arched ? "arched" : "not arched")}, " +
                           $"{Marching.SecondsToWalk(thin.State, archer, arching.Path.Waypoints, arching.Hold):0} s.");

            Assert.True(crabbed,
                "Four blocks either side of a 30 m gap, and it chose to walk round the whole wall rather " +
                "than turn side-on and go through the middle. Rung two is meant to weigh the two.");

            Assert.True(arched,
                "One block either side, and it turned side-on to thread a gap it could have stepped round " +
                "in a fraction of the time. The comparison runs the wrong way.");
        }

        // ---- M20: being inside your own men costs pace ------------------------

        [Fact]
        public void ShoulderingThroughItsOwnIsSlowerThanOpenGround()
        {
            // Recorded: cavalry walked clean through an Archers regiment three
            // times at full pace. Rung three is the last resort and cost less
            // than the detour above it.
            Battlefield clear = AWallWithAGap(out UnitInstance walker, out Vec2 openTo, gap: 60f, blocks: 2, ranks: 3);
            Battlefield solid = AWallWithAGap(out UnitInstance presser, out Vec2 throughTo, gap: 0f, blocks: 2, ranks: 3);

            int walked = TicksToReach(clear, walker, openTo);
            int pressed = TicksToReach(solid, presser, throughTo);

            _out.WriteLine($"walked through a gap in {walked} ticks; shouldered through the wall in {pressed}.");

            Assert.True(walked > 0 && pressed > 0, "Both should get there at all.");

            // Anchored to what the manoeuvre is worth rather than to a round
            // number. Seventy metres of men crossed at 1.59 m/s takes 44 s at
            // full pace and 73 at six tenths, so about thirty seconds on a march
            // of some three hundred — a tenth, and it measures near that.
            //
            // The bar sits below the measurement because what is being guarded
            // is that the charge exists at all. Free, rung three was cheaper
            // than the rungs above it, which inverts the whole ladder: verified
            // by setting the pace back to 1.0, where the two journeys come out
            // at 290 ticks each and this fails.
            Assert.True(pressed > walked * 1.06f,
                $"Shouldering through its own took {pressed} ticks against {walked} walking a clear gap. " +
                "Being inside a body of men is not being charged for, so the last rung of the ladder is " +
                "cheaper than the ones above it.");
        }

        private static int TicksToReach(Battlefield field, UnitInstance unit, Vec2 destination)
        {
            var clock = new BattleClock();
            foreach (IBattleSystem system in field.Clock.Systems) clock.Add(system);

            var log = new Heard();

            for (int tick = 1; tick <= BattleClock.TicksPerTurn * 20; tick++)
            {
                clock.Advance(field.State, log);

                if (Vec2.Distance(unit.Position, destination) < 40f) return tick;
            }

            return 0;
        }

        // ---- M21: a detour is committed until it is behind you ---------------

        /// <summary>
        /// A charge that has to get through its own line to reach the enemy.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Finding it took two wrong arrangements, and the wrong ones are worth
        /// recording. A wall of stationary friends cannot reach the steering at
        /// all: the planner casts, sees them, and routes round before
        /// <c>MakeRoomForFriends</c> is consulted. Neither can crossing traffic
        /// — regiments passing at an angle are handled entirely by the sliding
        /// branch, which keeps the part of the step that runs along a flank and
        /// never has to choose a side. Both versions reported "0 detours" and
        /// would have passed on a flat zero, which is also what a working
        /// commitment gives.
        /// </para>
        /// <para>
        /// What reaches it is an <b>attack</b>. Closing with an enemy skips rung
        /// two on purpose — O5 says centre first and then sidestep to share the
        /// face, and arching a charge in would put two rules in charge of one
        /// approach — so a charge is planned straight through whatever is in the
        /// way and the steering is the only thing that deals with it. Which is
        /// exactly what the recorded battle was full of.
        /// </para>
        /// </remarks>
        private static Battlefield ACharge(out UnitInstance mover, out UnitInstance target)
        {
            var field = new Battlefield("plains", 45000);

            mover = field.Add(0, "cavalry", field.Centre - new Vec2(260f, 0f), Facing.East);

            // Two of its own drawn up square across the line of the charge, so
            // the horse comes into a back rather than along a flank — the case
            // with nothing worth sliding along, which is the branch that has to
            // choose a side.
            foreach (float y in new[] { -20f, 20f })
            {
                UnitInstance ours = field.Add(0, "spearmen", field.Centre + new Vec2(-60f, y), Facing.East);
                Battlefield.Hold(ours);
            }

            target = field.Add(1, "archers", field.Centre + new Vec2(200f, 0f), Facing.West);
            Battlefield.Hold(target);

            Battlefield.Press(mover, target);

            return field;
        }

        [Fact]
        public void TheSideOfADetourIsNotReDecidedMidManoeuvre()
        {
            // The seizure, as a property rather than as a scenario. The old
            // release was "the first tick nobody is touching us": a sidestep
            // succeeds, the commitment is dropped, the same regiment is in the
            // way again next tick, and the side is derived afresh from a
            // slightly different position. Whether that reverses depends on
            // which side of a centreline the regiment happens to be sitting, so
            // asking for the outcome would be asking for luck.
            Battlefield field = ACharge(out UnitInstance mover, out _);

            var clock = new BattleClock();
            foreach (IBattleSystem system in field.Clock.Systems) clock.Add(system);

            var log = new Heard();

            Facing? committed = null;
            int reversals = 0;
            int commitments = 0;
            float worst = 0f;

            for (int tick = 0; tick < BattleClock.TicksPerTurn * 12; tick++)
            {
                clock.Advance(field.State, log);

                if (mover.GoingRound == UnitId.None)
                {
                    committed = null;
                    continue;
                }

                if (committed.HasValue)
                {
                    float swung = Facing.AbsoluteDelta(committed.Value, mover.GoingRoundBearing)
                                  * 180f / MathF.PI;

                    if (swung > worst) worst = swung;
                    if (swung > 90f) reversals++;
                }
                else
                {
                    commitments++;
                }

                committed = mover.GoingRoundBearing;
            }

            _out.WriteLine($"{commitments} detours committed to; the side swung {worst:0}° at worst; " +
                           $"reversed {reversals} times.");

            // Says out loud that it measured something. The first version of
            // this test used a wall of stationary friends, never reached the
            // steering at all, and passed with a flat zero — which is the same
            // number a working commitment gives.
            Assert.True(commitments > 0,
                "Nobody ever had to get round anybody, so this proved nothing. The arrangement is wrong, " +
                "not the rule.");

            Assert.Equal(0, reversals);
        }

        [Fact]
        public void ADetourIsForgottenOnceWhatItWentRoundIsBehind()
        {
            // The other half. A commitment that is never released is a regiment
            // sidling forever, which is the opposite failure and just as bad.
            Battlefield field = ACharge(out UnitInstance mover, out UnitInstance target);

            var clock = new BattleClock();
            foreach (IBattleSystem system in field.Clock.Systems) clock.Add(system);

            var log = new Heard();

            int everCommitted = 0;
            int held = 0;
            int longest = 0;

            for (int tick = 0; tick < BattleClock.TicksPerTurn * 12; tick++)
            {
                clock.Advance(field.State, log);

                if (mover.GoingRound == UnitId.None)
                {
                    held = 0;
                    continue;
                }

                if (held == 0) everCommitted++;

                held++;
                if (held > longest) longest = held;
            }

            float left = OrientedRect.GapBetween(mover.Shape, target.Shape);

            _out.WriteLine($"{everCommitted} detours, longest held {longest} ticks; " +
                           $"still committed at the end: {mover.GoingRound != UnitId.None}; " +
                           $"finished {left:0} m from the enemy it was sent at.");

            Assert.True(everCommitted > 0, "It never had to get round anybody, so this proved nothing.");

            // The failure a commitment that is never released actually produces:
            // a regiment sidling along a bearing it settled on half a minute ago
            // instead of closing with what it was sent at.
            Assert.True(left < 30f,
                $"The charge finished {left:0} m from its target. A commitment that outlives what it was " +
                "made about is a regiment sidling for ever.");
        }

        // ---- W5: the log reports what happened -------------------------------

        [Fact]
        public void TheArrivalLineReportsThePaceItActuallyMade()
        {
            // Every arrival in the recording read "4,8 m/s" — the pace the
            // regiment could have made on the ground it finished on, asked of
            // the same code under suspicion. What it actually made was 2.6 to
            // 3.6. A line written to diagnose slow marches reported the one
            // number that could never show one.
            var field = new Battlefield("plains", 44000);

            Vec2 from = field.Centre - new Vec2(100f, 0f);

            // Facing due west, sent due east: the whole march is paid for at the
            // start with an about-face, so nominal and achieved cannot agree.
            UnitInstance unit = field.Add(0, "swordsmen", from, Facing.West);
            Vec2 to = from + new Vec2(200f, 0f);

            var log = new Heard();
            field.March(unit, to, log: log);

            Run(field, log, turns: 20);

            string? arrival = log.FirstSaying("reached its destination");

            _out.WriteLine(arrival ?? "(never arrived)");

            Assert.NotNull(arrival);

            Assert.Contains("averaging", arrival!);

            // Nominal on plains is 4.8 for swordsmen; a 180° wheel at the start
            // of a 200 m march cannot come out anywhere near it.
            Assert.DoesNotContain("averaging 4,8 m/s", arrival!);
            Assert.DoesNotContain("averaging 4.8 m/s", arrival!);
        }

        [Fact]
        public void GoingRoundSaysWhatTheDetourCost()
        {
            // "It might try to find a path a bit too distanced from the closest
            // efficient path" — a suspicion nothing could answer, because no
            // rule anywhere recorded what a detour was worth. Whether the arcs
            // are too wide is a judgement, and a judgement needs a number.
            var log = new Heard();
            AWallWithAGap(out UnitInstance mover, out Vec2 destination, gap: 30f, blocks: 1, log);

            string? said = null;
            for (int i = 0; i < log.Lines.Count && said == null; i++)
                if (log.Lines[i].Contains("is going round its own")) said = log.Lines[i];

            _out.WriteLine(said ?? "(never said)");

            Assert.NotNull(said);

            Assert.True(said!.Contains(" s against "),
                $"The detour was announced without saying what it cost: \"{said}\". A route chosen on " +
                "time has to report the time, or the choice cannot be argued with.");
        }
    }
}
