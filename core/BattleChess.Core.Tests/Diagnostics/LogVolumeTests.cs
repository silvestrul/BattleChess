using System.Collections.Generic;
using BattleChess.Contracts;
using BattleChess.Rules;
using BattleChess.Tests.Battle;
using Xunit;
using Xunit.Abstractions;

namespace BattleChess.Tests.Diagnostics
{
    /// <summary>
    /// How much a battle says about itself.
    /// </summary>
    /// <remarks>
    /// A recording is only useful if it can be read end to end, and the way it
    /// stops being readable is one line at a time — a rule that adds a number to
    /// a message it repeats, a guard that flaps, a state described once a tick.
    /// None of those look like anything in a diff. A budget catches them,
    /// because the count is the thing that matters and nothing else measures it.
    /// </remarks>
    public sealed class LogVolumeTests
    {
        private readonly ITestOutputHelper _out;

        public LogVolumeTests(ITestOutputHelper output) => _out = output;

        private sealed class Sink : IBattleLog
        {
            public readonly List<BattleLogEntry> Entries = new List<BattleLogEntry>();

            public void Record(in BattleLogEntry entry) => Entries.Add(entry);
        }

        private static Battlefield AMessyLittleBattle(
            out UnitInstance a1, out UnitInstance a2, out UnitInstance a3,
            out UnitInstance d1, out UnitInstance d2)
        {
            var field = new Battlefield("plains", 4242);

            // Three against two, so there is crowding, contact, a flank, a rout
            // and a pursuit — the busiest thing that fits in a test.
            a1 = field.Add(0, "swordsmen", field.Centre - new Vec2(200f, 40f), Facing.East);
            a2 = field.Add(0, "swordsmen", field.Centre - new Vec2(200f, -40f), Facing.East);
            a3 = field.Add(0, "cavalry", field.Centre - new Vec2(240f, 120f), Facing.East);

            d1 = field.Add(1, "spearmen", field.Centre, Facing.West);
            d2 = field.Add(1, "archers", field.Centre + new Vec2(60f, 60f), Facing.West);

            return field;
        }

        private Sink RunIt(bool collapsed)
        {
            Battlefield field = AMessyLittleBattle(
                out UnitInstance a1, out UnitInstance a2, out UnitInstance a3,
                out UnitInstance d1, out UnitInstance d2);

            var sink = new Sink();
            var quiet = new SteadyStateLog(sink);
            IBattleLog log = collapsed ? quiet : sink;

            Battlefield.Press(a1, d1);
            Battlefield.Press(a2, d1);
            Battlefield.Press(a3, d2);
            Battlefield.Hold(d1);
            Battlefield.Hold(d2);

            var clock = new BattleClock();
            foreach (IBattleSystem system in field.Clock.Systems) clock.Add(system);

            for (int tick = 0; tick < BattleClock.TicksPerTurn * 12; tick++)
                clock.Advance(field.State, log);

            if (collapsed) quiet.Flush();

            return sink;
        }

        [Fact]
        public void TwelveTurnsOfBattleFitOnAPageOrTwo()
        {
            Sink collapsed = RunIt(collapsed: true);
            Sink raw = RunIt(collapsed: false);

            foreach (BattleLogEntry entry in collapsed.Entries)
                _out.WriteLine($"{entry.Category,-9} {entry.Unit,-4} {entry.Message}");

            _out.WriteLine($"\n{raw.Entries.Count} lines raw, {collapsed.Entries.Count} collapsed.");

            // Twelve turns of five regiments fighting. The figure to defend is
            // "a person will read this", not any particular number — but a
            // recording that has quietly gone back to hundreds of lines is one
            // nobody will read, and that is worth failing a build over.
            Assert.True(collapsed.Entries.Count < 120,
                $"A twelve-turn battle now takes {collapsed.Entries.Count} lines to describe. Something has " +
                "started repeating itself — most likely a message with a number in it that changes every " +
                "tick, which can never collapse.");
        }

        [Fact]
        public void NoSingleRuleDrownsOutTheRest()
        {
            Sink collapsed = RunIt(collapsed: true);

            var byCategory = new Dictionary<string, int>();

            foreach (BattleLogEntry entry in collapsed.Entries)
            {
                byCategory.TryGetValue(entry.Category, out int seen);
                byCategory[entry.Category] = seen + 1;
            }

            foreach (KeyValuePair<string, int> pair in byCategory)
                _out.WriteLine($"{pair.Key,-10} {pair.Value}");

            // Every previous round of this went wrong the same way: one rule
            // finding something to say every tick and burying everything else.
            // Melee narrating each exchange, shooting each volley, movement each
            // arrival. A share of the whole catches the next one without caring
            // which rule it turns out to be.
            foreach (KeyValuePair<string, int> pair in byCategory)
            {
                Assert.True(pair.Value <= collapsed.Entries.Count * 0.6f,
                    $"{pair.Key} is {pair.Value} of {collapsed.Entries.Count} lines. One rule talking over " +
                    "all the others is how every previous recording became unreadable.");
            }
        }

        [Fact]
        public void AStateThatKeepsRetriggeringIsVisibleAsOneLine()
        {
            Sink collapsed = RunIt(collapsed: true);

            // The point of the closing line. A guard meant to fire once per
            // hold-up that instead fires hundreds of times shows up here as a
            // single entry saying how many times over — which is readable, and
            // is how the flapping was noticed at all.
            int summaries = 0;

            foreach (BattleLogEntry entry in collapsed.Entries)
            {
                if (!entry.Message.StartsWith("— and that held for")) continue;

                summaries++;
                _out.WriteLine($"{entry.Category,-9} {entry.Unit,-4} {entry.Message}");
            }

            Assert.True(summaries > 0,
                "A twelve-turn battle with five regiments in it has states that last. If none is being " +
                "reported, the collapse is not running at all and every line is being taken as an event.");
        }
    }
}
