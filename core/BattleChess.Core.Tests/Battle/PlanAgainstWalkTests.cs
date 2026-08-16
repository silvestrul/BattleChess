using System;
using System.Collections.Generic;
using BattleChess.Contracts;
using BattleChess.Rules;
using Xunit;
using Xunit.Abstractions;

namespace BattleChess.Tests.Battle
{
    /// <summary>
    /// Whether a regiment walking a route the planner called clear actually
    /// keeps clear.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The question that should have been asked first. Four passes fixed the
    /// <i>planner</i>, each against a test calling <see cref="Marching.PlanTo"/>
    /// in isolation, and each time the game went on reproducing. Counting the
    /// recordings says why: across four battles, <b>14 of 37 collisions happened
    /// on routes that were never press-throughs at all</b> — 6 of 11 in one of
    /// them. Those are lines the planner declared clear and the regiment walked
    /// into somebody anyway.
    /// </para>
    /// <para>
    /// A plan is a claim about a line. Only running the clock tests whether the
    /// claim survives being walked.
    /// </para>
    /// </remarks>
    public sealed class PlanAgainstWalkTests
    {
        private readonly ITestOutputHelper _out;

        public PlanAgainstWalkTests(ITestOutputHelper output) => _out = output;

        /// <summary>
        /// The recorded arrangement: a line of its own across the middle, and one
        /// regiment sent back and forth across it the way the player does.
        /// </summary>
        private static Battlefield ALineAndSomebodyCrossingIt(out UnitInstance mover)
        {
            var field = new Battlefield("plains", 861);

            foreach (float x in new[] { 163f, 213f, 263f, 313f })
                Battlefield.Hold(field.Add(0, "spearmen", new Vec2(x, 213f), Facing.FromDegrees(0f)));

            mover = field.Add(0, "cavalry", new Vec2(245f, 300f), Facing.FromDegrees(0f));

            return field;
        }

        [Fact]
        public void ARouteThePlannerCalledClearIsWalkedClear()
        {
            Battlefield field = ALineAndSomebodyCrossingIt(out UnitInstance mover);

            // Back and forth across the line, which is what every recording
            // shows the player doing and what every reproduction has been about.
            var errands = new[]
            {
                new Vec2(283f, 130f), new Vec2(207f, 300f), new Vec2(300f, 140f),
                new Vec2(220f, 290f), new Vec2(180f, 140f), new Vec2(290f, 300f),
            };

            int planned = 0;
            int walkedIntoSomebody = 0;
            int whilePressing = 0;

            var offenders = new List<string>();

            foreach (Vec2 errand in errands)
            {
                field.March(mover, errand, log: field.Transcript);
                planned++;

                var line = new List<Vec2>(mover.Route!.Waypoints);

                for (int tick = 0; tick < 200 && mover.IsMarching; tick++)
                {
                    field.Clock.Advance(field.State, field.Transcript);

                    bool pressing = mover.Route?.PressingThrough == true;

                    foreach (UnitInstance other in field.State.UnitsOnField())
                    {
                        if (other.Id == mover.Id) continue;

                        if (OrientedRect.OverlapFraction(mover.Shape, other.Shape)
                            <= OrderSystem.GrazingTolerance) continue;

                        if (pressing) whilePressing++;
                        else
                        {
                            walkedIntoSomebody++;

                            if (offenders.Count < 6)
                                offenders.Add(
                                    $"tick {tick}: at ({mover.Position.X:0},{mover.Position.Y:0}) " +
                                    $"facing {mover.Facing.Degrees:0}° into {other.Def.DisplayName} " +
                                    $"at ({other.Position.X:0},{other.Position.Y:0}), " +
                                    $"{OrientedRect.OverlapFraction(mover.Shape, other.Shape):0.00} deep, " +
                                    $"heading for ({mover.Route?.Destination.X ?? 0:0},{mover.Route?.Destination.Y ?? 0:0}), " +
                                    $"{HowFarOffTheLine(mover.Position, line):0.0} m off its planned line; " +
                                    $"leg wants front {(mover.Route?.HoldThisLeg?.Degrees.ToString("0") ?? "the line of march")}, " +
                                    $"it is on {mover.Facing.Degrees:0}°");
                        }

                        break;
                    }
                }
            }

            _out.WriteLine($"{planned} marches; {whilePressing} ticks overlapping on a declared " +
                           $"press-through; {walkedIntoSomebody} ticks overlapping on a route that " +
                           "was supposed to be clear.");

            foreach (string line in offenders) _out.WriteLine(line);

            // Non-vacuity: if it never went anywhere near the line, nothing here
            // measures anything at all.
            Assert.True(whilePressing + walkedIntoSomebody > 0 || planned == errands.Length,
                "No marches ran, so this measures nothing.");

            Assert.Equal(0, walkedIntoSomebody);
        }

        /// <summary>
        /// How far a regiment has strayed from the line that was planned for it.
        /// </summary>
        /// <remarks>
        /// The number that decides which half of the system is at fault. A
        /// regiment lapping somebody while <b>on</b> its planned line means the
        /// clearance check passed a line it should have refused. Lapping somebody
        /// <b>off</b> its line means the plan was fine and the walk left it, and
        /// no amount of work on the planner will ever touch it.
        /// </remarks>
        private static float HowFarOffTheLine(Vec2 at, List<Vec2> line)
        {
            float nearest = float.MaxValue;

            for (int i = 1; i < line.Count; i++)
            {
                Vec2 leg = line[i] - line[i - 1];
                float length = leg.Length;

                if (length <= 0f) continue;

                Vec2 along = leg / length;
                float ahead = MathF.Max(0f, MathF.Min(length, Vec2.Dot(at - line[i - 1], along)));

                nearest = MathF.Min(nearest, Vec2.Distance(at, line[i - 1] + along * ahead));
            }

            return nearest == float.MaxValue ? 0f : nearest;
        }

        [Fact]
        public void TheSweepAgreesWithSteppingAlongTheSameLine()
        {
            var field = new Battlefield("plains", 861);

            UnitInstance blocker =
                field.Add(0, "spearmen", new Vec2(213f, 213f), Facing.FromDegrees(0f));
            Battlefield.Hold(blocker);

            UnitInstance mover =
                field.Add(0, "cavalry", new Vec2(220f, 290f), Facing.FromDegrees(0f));

            var to = new Vec2(180f, 140f);

            Facing front = Marching.AlongTheLine(mover.Position, to, mover.Facing);

            bool sweepSaysClear =
                Marching.IsClearLine(field.State, mover, mover.Position, to, front);

            // The same line, walked a metre at a time, asking the one question
            // the whole model rests on: do these two rectangles share ground?
            float worst = 0f;
            Vec2 worstAt = mover.Position;

            Vec2 travel = to - mover.Position;
            int steps = (int)travel.Length;

            for (int i = 0; i <= steps; i++)
            {
                Vec2 at = mover.Position + travel * (i / (float)steps);
                var body = new OrientedRect(at, front, mover.Footprint);

                float overlap = OrientedRect.OverlapFraction(body, blocker.Shape);

                if (overlap > worst) { worst = overlap; worstAt = at; }
            }

            _out.WriteLine($"sweep says clear: {sweepSaysClear}; " +
                           $"stepping finds worst overlap {worst:0.00} at ({worstAt.X:0},{worstAt.Y:0})");

            Assert.True(worst > 0f || sweepSaysClear,
                "Neither method found anything, so this measures nothing.");

            Assert.False(sweepSaysClear && worst > OrderSystem.GrazingTolerance,
                $"The sweep calls this line clear and walking it a metre at a time laps the blocker by " +
                $"{worst:0.00} of a body at ({worstAt.X:0},{worstAt.Y:0}). Every clearance decision in " +
                "the planner rests on the sweep.");
        }
    }
}
