using System.Collections.Generic;
using BattleChess.Contracts;
using BattleChess.Rules;
using Xunit;
using Xunit.Abstractions;

namespace BattleChess.Tests.Battle
{
    /// <summary>
    /// Which way of getting round something actually finds the way round.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A recorded game had regiments shouldering through their own on
    /// ninety-metre marches with one friend nearby and open field either side —
    /// <b>M18</b> rung three answering what rung two should have. Rung three is
    /// only reached when going round has failed, so the question is not which
    /// detour is prettiest but which one is <i>found at all</i>.
    /// </para>
    /// <para>
    /// Three plausible repairs, no evidence which is right, and a strong
    /// temptation to pick one and be confident. So they run side by side against
    /// the same arrangements and the table decides.
    /// </para>
    /// </remarks>
    public sealed class WaysRoundComparisonTests
    {
        private readonly ITestOutputHelper _out;

        public WaysRoundComparisonTests(ITestOutputHelper output) => _out = output;

        /// <summary>A crowded start area, of the kind the recording was made in.</summary>
        private static Battlefield ACrowd(out UnitInstance mover, out Vec2 destination, int howMany)
        {
            var field = new Battlefield("plains", 36000);

            // Strewn across the line of march at varying offsets, close enough
            // that the way round one of them clips the next.
            var about = new (float Along, float Aside)[]
            {
                (90f, 10f), (170f, -35f), (250f, 30f), (330f, -25f), (410f, 15f), (480f, -30f),
            };

            for (int i = 0; i < howMany && i < about.Length; i++)
            {
                UnitInstance near = field.Add(
                    0, "spearmen",
                    field.Centre + new Vec2(about[i].Along - 250f, about[i].Aside),
                    Facing.East);

                Battlefield.Hold(near);
            }

            mover = field.Add(0, "swordsmen", field.Centre - new Vec2(300f, 0f), Facing.East);
            destination = field.Centre + new Vec2(300f, 0f);

            return field;
        }

        [Fact]
        public void TheTableOfWhoFindsAWayRound()
        {
            _out.WriteLine($"{"crowd",-7}{"way round",-30}{"found",-8}{"legs",-6}{"extra m",-9}");
            _out.WriteLine(new string('-', 60));

            var foundBy = new Dictionary<string, int>();

            for (int crowd = 1; crowd <= 6; crowd++)
            {
                foreach (IWayRound way in WaysRound.All)
                {
                    Battlefield field = ACrowd(out UnitInstance mover, out Vec2 destination, crowd);

                    IReadOnlyList<Vec2>? round = way.Round(field.State, mover, destination);

                    string legs = round == null ? "-" : (round.Count - 1).ToString();
                    string extra = "-";

                    if (round != null)
                    {
                        float total = 0f;
                        for (int i = 1; i < round.Count; i++) total += Vec2.Distance(round[i - 1], round[i]);

                        extra = $"{total - Vec2.Distance(mover.Position, destination):0}";

                        foundBy.TryGetValue(way.Name, out int n);
                        foundBy[way.Name] = n + 1;
                    }

                    _out.WriteLine($"{crowd,-7}{way.Name,-30}{(round != null ? "yes" : "NO"),-8}{legs,-6}{extra,-9}");
                }

                _out.WriteLine("");
            }

            _out.WriteLine("found a way round, out of six crowds:");
            foreach (IWayRound way in WaysRound.All)
            {
                foundBy.TryGetValue(way.Name, out int n);
                _out.WriteLine($"  {way.Name,-30} {n}");
            }

            // The baseline is what shipped and what the recording complains
            // about, so the bar is simply that the default beats it. Which of
            // the other two is better is what the table is for.
            foundBy.TryGetValue("past the first thing", out int baseline);
            foundBy.TryGetValue(WaysRound.Default.Name, out int chosen);

            Assert.True(chosen >= baseline,
                $"The way round in use finds one {chosen} times out of six where the old rule managed " +
                $"{baseline}. It is not an improvement.");
        }

        [Fact]
        public void ShoulderingThroughItsOwnGoesBackToBeingRare()
        {
            // The metric that matters, and the complaint the play-test made.
            // Rung three is the last resort; it was answering ordinary marches
            // because rung two never found the way round that was plainly there.
            _out.WriteLine($"{"crowd",-7}{"way round",-30}{"rung",-22}");
            _out.WriteLine(new string('-', 60));

            var pressed = new Dictionary<string, int>();

            for (int crowd = 1; crowd <= 6; crowd++)
            {
                foreach (IWayRound way in WaysRound.All)
                {
                    Battlefield field = ACrowd(out UnitInstance mover, out Vec2 destination, crowd);

                    Plan plan = Marching.PlanTo(
                        field.State, mover, field.Pathfinder, destination, log: null, wayRound: way);

                    string rung =
                        plan.PressedThrough ? "3 — through its own"
                        : plan.Path.CellsExplored > 0 ? "search"
                        : plan.Path.Waypoints.Count > 2 ? "2 — round it"
                        : "1 — straight there";

                    if (plan.PressedThrough)
                    {
                        pressed.TryGetValue(way.Name, out int n);
                        pressed[way.Name] = n + 1;
                    }

                    _out.WriteLine($"{crowd,-7}{way.Name,-30}{rung,-22}");
                }

                _out.WriteLine("");
            }

            _out.WriteLine("shouldered through, out of six crowds:");
            foreach (IWayRound way in WaysRound.All)
            {
                pressed.TryGetValue(way.Name, out int n);
                _out.WriteLine($"  {way.Name,-30} {n}");
            }

            pressed.TryGetValue("past the first thing", out int baseline);
            pressed.TryGetValue(WaysRound.Default.Name, out int now);

            Assert.True(now < baseline,
                $"The way round in use still shoulders through {now} times out of six against the old " +
                $"rule's {baseline}. That is the fault the play-test reported, unfixed.");
        }
    }
}
