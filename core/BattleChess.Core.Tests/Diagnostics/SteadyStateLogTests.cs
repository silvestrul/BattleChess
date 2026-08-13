using System.Collections.Generic;
using BattleChess.Contracts;
using Xunit;

namespace BattleChess.Tests.Diagnostics
{
    /// <summary>
    /// Telling a moment from a condition.
    /// </summary>
    /// <remarks>
    /// One recording of a battle came to four and a half thousand lines out of
    /// thirty-three places in the rules, because a handful of them described a
    /// state that happened to still be true, once every tick. What this has to
    /// get right is that an event still speaks and a state speaks twice — once
    /// when it starts, once to say how long it went on.
    /// </remarks>
    public sealed class SteadyStateLogTests
    {
        private sealed class Sink : IBattleLog
        {
            public readonly List<string> Lines = new List<string>();

            public void Record(in BattleLogEntry entry) => Lines.Add(entry.Message);
        }

        private static readonly UnitId First = new UnitId(1);
        private static readonly UnitId Second = new UnitId(2);

        [Fact]
        public void ARepeatedLineIsSaidOnceAndThenCountedUp()
        {
            var sink = new Sink();
            var log = new SteadyStateLog(sink);

            for (int tick = 0; tick < 200; tick++)
            {
                log.Ticked(tick);
                log.Blocked("Move", "Cavalry cannot get past Cavalry.", First);
            }

            // Once for the beginning. The other 199 are the same sentence.
            Assert.Single(sink.Lines);
            Assert.Equal(199, log.Suppressed);
        }

        [Fact]
        public void AndSaysHowLongItWentOnForOnceItStops()
        {
            var sink = new Sink();
            var log = new SteadyStateLog(sink);

            for (int tick = 0; tick < 40; tick++)
            {
                log.Ticked(tick);
                log.Blocked("Move", "Cavalry is hemmed in.", First);
            }

            // It stops happening, and time keeps passing.
            for (int tick = 40; tick < 80; tick++) log.Ticked(tick);

            Assert.Equal(2, sink.Lines.Count);

            // The duration is the point. Thirty-nine scattered lines read as
            // ordinary traffic; "that held for 40 ticks" reads as a seizure.
            Assert.Contains("40 ticks", sink.Lines[1]);
            Assert.Contains("ticks 0 to 39", sink.Lines[1]);
        }

        [Fact]
        public void SomethingSaidOnceIsNeverGivenAClosingLine()
        {
            var sink = new Sink();
            var log = new SteadyStateLog(sink);

            log.Ticked(0);
            log.Info("Combat", "Cavalry meets Spearmen — square on to its front.", First);

            for (int tick = 1; tick < 100; tick++) log.Ticked(tick);

            // An event happened once and has nothing more to say. A closing
            // line here would double every real event in the recording.
            Assert.Single(sink.Lines);
        }

        [Fact]
        public void TwoRegimentsSayingTheSameThingAreTwoSeparateStates()
        {
            var sink = new Sink();
            var log = new SteadyStateLog(sink);

            for (int tick = 0; tick < 30; tick++)
            {
                log.Ticked(tick);
                log.Blocked("Move", "Blocked by its own side.", First);
                log.Blocked("Move", "Blocked by its own side.", Second);
            }

            // The same sentence from two regiments is two facts, not one said
            // twice — otherwise the second regiment's trouble is invisible for
            // as long as the first one's lasts.
            Assert.Equal(2, sink.Lines.Count);
        }

        [Fact]
        public void APulsedRuleIsNotReportedAsStartingAndStoppingSixTimesATurn()
        {
            var sink = new Sink();
            var log = new SteadyStateLog(sink);

            // Combat runs one tick in ten. Without a grace period a state of
            // its own is renewed nine ticks after it was last mentioned, and
            // would be closed and reopened on every single pulse.
            for (int tick = 0; tick < 120; tick++)
            {
                log.Ticked(tick);

                if (tick % 10 == 0)
                    log.Decision("Combat", "Spearmen and Swordsmen are at grips.", First);
            }

            Assert.Single(sink.Lines);
        }

        [Fact]
        public void AStateThatComesBackIsANewState()
        {
            var sink = new Sink();
            var log = new SteadyStateLog(sink);

            for (int tick = 0; tick < 20; tick++)
            {
                log.Ticked(tick);
                log.Blocked("Move", "Jammed.", First);
            }

            for (int tick = 20; tick < 60; tick++) log.Ticked(tick);

            for (int tick = 60; tick < 80; tick++)
            {
                log.Ticked(tick);
                log.Blocked("Move", "Jammed.", First);
            }

            log.Flush();

            // Began, ended, began again, ended again. A regiment that jams
            // twice has jammed twice, and collapsing that into one entry would
            // hide the second one entirely.
            Assert.Equal(4, sink.Lines.Count);
        }

        [Fact]
        public void FlushClosesWhateverIsStillOpen()
        {
            var sink = new Sink();
            var log = new SteadyStateLog(sink);

            for (int tick = 0; tick < 25; tick++)
            {
                log.Ticked(tick);
                log.Blocked("Move", "Still stuck as the recording ends.", First);
            }

            Assert.Single(sink.Lines);

            log.Flush();

            // The last thing that happened is very often the thing being
            // investigated, and it must not be the one entry with no ending.
            Assert.Equal(2, sink.Lines.Count);
            Assert.Contains("25 ticks", sink.Lines[1]);
        }
    }
}
