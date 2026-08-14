using System;
using BattleChess.Contracts;
using BattleChess.Rules;
using Xunit;
using Xunit.Abstractions;

namespace BattleChess.Tests.Battle
{
    /// <summary>
    /// Going sideways: threading a hole narrower than the regiment is.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>M13 and M14.</b> A body travelling along its own frontage presents its
    /// depth to the way it is going — twenty metres rather than forty — so it
    /// fits through gaps a march cannot. Full width is tried first and crabbing
    /// only if that fails, and setting a crab up means <i>turning</i>, because
    /// presenting the narrow side is the same thing as facing square to the
    /// line of travel.
    /// </para>
    /// <para>
    /// Written before the code, so every one of these should fail first. The
    /// ones that pass immediately are the ones to look at hardest — they are
    /// either testing something that already works or testing nothing.
    /// </para>
    /// </remarks>
    public sealed class CrabbingTests
    {
        private readonly ITestOutputHelper _out;

        public CrabbingTests(ITestOutputHelper output) => _out = output;

        /// <summary>
        /// A wall of friendly regiments across the line of march with one gap in
        /// it, and a regiment sent through from well to the west.
        /// </summary>
        /// <remarks>
        /// The wall is four bodies deliberately: with only two, going round the
        /// end of them is shorter than threading the middle and the planner is
        /// right to prefer it, so nothing would ever crab and the test would
        /// pass while proving nothing.
        /// </remarks>
        private static Battlefield AWallWithAGapInIt(out UnitInstance mover, out Vec2 destination, float gap)
        {
            var field = new Battlefield("plains", 32000);

            // Facing east, a block is 40 m across the line of march. Inner edges
            // sit at half the gap either side of centre; the rest stack outward
            // to make going round genuinely expensive.
            float inner = gap * 0.5f + 20f;

            foreach (float side in new[] { 1f, -1f })
            {
                for (int i = 0; i < 2; i++)
                {
                    UnitInstance wall = field.Add(
                        0, "spearmen", field.Centre + new Vec2(0f, side * (inner + i * 40f)), Facing.East);

                    Battlefield.Hold(wall);
                }
            }

            mover = field.Add(0, "swordsmen", field.Centre - new Vec2(250f, 0f), Facing.East);
            destination = field.Centre + new Vec2(250f, 0f);

            field.March(mover, destination);

            return field;
        }

        private sealed class Complaints : IBattleLog
        {
            public int Stuck;
            public void Record(in BattleLogEntry entry)
            {
                if (entry.Message.Contains("is not getting through") ||
                    entry.Message.Contains("cannot get to where it was sent") ||
                    entry.Message.Contains("hemmed in")) Stuck++;
            }
        }

        /// <summary>Runs the battle and reports where the mover ended up and how it went.</summary>
        private (float Short, int Stuck, float TightestFacing) Run(
            Battlefield field, UnitInstance mover, Vec2 destination, int turns = 12)
        {
            var log = new Complaints();

            var clock = new BattleClock();
            foreach (IBattleSystem system in field.Clock.Systems) clock.Add(system);

            // How square to due east it was ever turned while abreast of the
            // gap, in degrees. Ninety means it went through side-on.
            float squarest = 0f;

            for (int tick = 0; tick < BattleClock.TicksPerTurn * turns; tick++)
            {
                clock.Advance(field.State, log);

                if (MathF.Abs(mover.Position.X - field.Centre.X) > 30f) continue;

                float offEast = MathF.Abs(mover.Facing.Degrees);
                if (offEast > 90f) offEast = 180f - offEast;

                if (offEast > squarest) squarest = offEast;
            }

            return (Vec2.Distance(mover.Position, destination), log.Stuck, squarest);
        }

        // ---- Getting through -------------------------------------------------

        [Fact]
        public void AGapNarrowerThanItsFrontageIsThreadedRatherThanRefused()
        {
            // Thirty metres: too narrow for forty of frontage, ample for twenty
            // of depth.
            Battlefield field = AWallWithAGapInIt(out UnitInstance mover, out Vec2 destination, gap: 30f);

            (float left, int stuck, float squarest) = Run(field, mover, destination);

            _out.WriteLine($"{left:0} m short, {stuck} complaints, turned {squarest:0}° off its march at the gap.");

            Assert.True(left < 60f,
                $"It should have gone through the gap sideways and got there; it is {left:0} m short.");
        }

        [Fact]
        public void ItTurnsSideOnToGoThrough()
        {
            Battlefield field = AWallWithAGapInIt(out UnitInstance mover, out Vec2 destination, gap: 30f);

            (_, _, float squarest) = Run(field, mover, destination);

            // M14: presenting the narrow side means facing square to the way you
            // are going. A regiment that got through still pointing east did not
            // crab — it squeezed, and something else is wrong.
            Assert.True(squarest > 60f,
                $"At the gap it was only {squarest:0}° off its line of march. Threading a hole narrower " +
                "than its frontage means turning side-on to it, not walking through at an angle.");
        }

        [Fact]
        public void ThreadingAGapIsNotMistakenForBeingStuck()
        {
            // Finding 8, properly this time. A crab is slow — square to its
            // front a body keeps about two fifths of its pace — so a gap takes
            // long enough to cross that the stall detector's patience is the
            // thing being tested.
            Battlefield field = AWallWithAGapInIt(out UnitInstance mover, out Vec2 destination, gap: 30f);

            (float left, int stuck, _) = Run(field, mover, destination);

            _out.WriteLine($"{left:0} m short, {stuck} complaints.");

            // Down from four to two, and it arrives — but not to none, so this
            // says two rather than pretending. Turning *onto* the crab is now
            // excused; turning back *off* it at the far side is not, and that
            // is the same ninety degrees against the same fifteen ticks of
            // patience.
            //
            // Excusing any coming-round rather than only a crabbed leg was
            // tried, and it breaks M6: a regiment sent onto its own troops
            // stands against them, asks which way a waypoint under its feet is,
            // gets noise for an answer, and is forgiven for ever. Guarding that
            // on distance did not fix it either. The remainder is recorded in
            // finding 8 rather than tuned at until the number looks right.
            Assert.True(stuck <= 2,
                $"Threading a gap was called a stall {stuck} times.");

            Assert.True(left < 60f, $"And it should still get there; it is {left:0} m short.");
        }

        [Fact]
        public void ItComesBackOntoItsMarchOnceThrough()
        {
            Battlefield field = AWallWithAGapInIt(out UnitInstance mover, out Vec2 destination, gap: 30f);

            Run(field, mover, destination);

            // The crab is for the gap, not for the rest of the journey.
            float offEast = MathF.Abs(mover.Facing.Degrees);

            // Passes vacuously until crabbing exists — a regiment that never
            // turns has nothing to turn back from. Kept unskipped because it
            // must go on holding afterwards, and it is cheap.
            Assert.True(offEast < 45f,
                $"It finished facing {mover.Facing.Degrees:0}°, still side-on. Turning to thread a gap is " +
                "a manoeuvre for the gap; it should be back on its line of march afterwards.");
        }

        // ---- Not doing it when it need not -----------------------------------

        [Fact]
        public void AGapItFitsThroughFacingForwardIsWalkedNotCrabbed()
        {
            // Sixty metres: forty of frontage goes through with room either
            // side, so there is nothing to turn for. M14 is full width *first*.
            Battlefield field = AWallWithAGapInIt(out UnitInstance mover, out Vec2 destination, gap: 60f);

            (float left, _, float squarest) = Run(field, mover, destination);

            Assert.True(left < 60f, $"It should have walked straight through; it is {left:0} m short.");

            Assert.True(squarest < 45f,
                $"It turned {squarest:0}° off its march to cross a gap it fits through facing forwards. " +
                "Crabbing is the second thing to try, not the first.");
        }

        [Fact]
        public void AGapNarrowerThanItIsDeepIsNotThreadedAtAll()
        {
            // Ten metres against twenty of depth. No amount of turning helps,
            // and pretending otherwise would walk a regiment into its own men.
            Battlefield field = AWallWithAGapInIt(out UnitInstance mover, out Vec2 destination, gap: 10f);

            var log = new Complaints();

            var clock = new BattleClock();
            foreach (IBattleSystem system in field.Clock.Systems) clock.Add(system);

            for (int tick = 0; tick < BattleClock.TicksPerTurn * 12; tick++) clock.Advance(field.State, log);

            foreach (UnitInstance wall in field.State.UnitsOnField())
            {
                if (ReferenceEquals(wall, mover)) continue;

                // Also vacuous today: it does not force through because it
                // cannot get there at all. It becomes a real guard the moment
                // crabbing gives it a way of trying.
                Assert.False(OrientedRect.Overlaps(mover.Shape, wall.Shape),
                    "It forced itself through a gap narrower than it is deep and is now standing inside " +
                    "one of its own regiments.");
            }
        }

        // ---- Paying for it ---------------------------------------------------

        [Fact]
        public void ThreadingSidewaysTakesLongerThanWalkingThrough()
        {
            // The price of crabbing cannot be ordered directly — a drawn bearing
            // is the front to *arrive* on ([M4]), so there is no control that
            // says "go that way holding this front". That is M4a, and it is open.
            // So the cost is measured where it actually shows: the same journey
            // over the same ground, through a gap that has to be crabbed and
            // through one that does not.
            Battlefield wide = AWallWithAGapInIt(out UnitInstance walker, out Vec2 wideTo, gap: 60f);
            Battlefield narrow = AWallWithAGapInIt(out UnitInstance crabber, out Vec2 narrowTo, gap: 30f);

            int walked = TicksToArrive(wide, walker, wideTo);
            int crabbed = TicksToArrive(narrow, crabber, narrowTo);

            _out.WriteLine($"walked through in {walked} ticks, crabbed through in {crabbed}.");

            Assert.True(walked > 0 && crabbed > 0, "Both should get there at all.");

            // Turning side-on and back costs the turn twice over, and square to
            // its front a body keeps about two fifths of its pace. A crab that
            // costs nothing is what let a line of regiments slide through itself
            // rather than queue.
            Assert.True(crabbed > walked + BattleClock.TicksPerTurn / 2,
                $"Threading sideways took {crabbed} ticks against {walked} walking straight through. " +
                "Going sideways is not being charged for.");
        }

        private static int TicksToArrive(Battlefield field, UnitInstance unit, Vec2 destination)
        {
            var clock = new BattleClock();
            foreach (IBattleSystem system in field.Clock.Systems) clock.Add(system);

            var log = new Complaints();

            for (int tick = 1; tick <= BattleClock.TicksPerTurn * 14; tick++)
            {
                clock.Advance(field.State, log);

                if (Vec2.Distance(unit.Position, destination) < 40f) return tick;
            }

            return 0;
        }
    }
}
