using BattleChess.Contracts;
using BattleChess.Rules;
using Xunit;
using Xunit.Abstractions;

namespace BattleChess.Tests.Battle
{
    /// <summary>
    /// Finding 18, from `logs/battle-20260814-214159.log` tick 1547.
    /// </summary>
    /// <remarks>
    /// <code>
    /// 1547 > pushing through its own Swordsmen — no way round it and no gap to
    ///        thread. by (247,169) → (219,288). 2 of its own on that line.
    /// 1574 X Cavalry ... standing in its own Archers at (213,213) facing 0°
    /// </code>
    /// Archers and Swordsmen stand 50 m apart, both facing east, so they are
    /// 20 m deep and there is a <b>30 m gap</b> between them. The cavalry starts
    /// inside that gap and is sent straight down it. Side-on it is 20 m across.
    /// It fits. It pressed through anyway.
    /// </remarks>
    public sealed class GapInTheLineTests
    {
        private readonly ITestOutputHelper _out;

        public GapInTheLineTests(ITestOutputHelper output) => _out = output;

        private static Battlefield TheGapItWouldNotThread(
            out UnitInstance cavalry, out Vec2 destination)
        {
            var field = new Battlefield("plains", 214159);

            Battlefield.Hold(field.Add(0, "archers", new Vec2(213f, 213f), Facing.FromDegrees(0f)));
            Battlefield.Hold(field.Add(0, "swordsmen", new Vec2(263f, 213f), Facing.FromDegrees(0f)));

            // Standing in the gap already, pointing back the way it came — the
            // 171° wheel the recording reports on this very order.
            cavalry = field.Add(0, "cavalry", new Vec2(247f, 169f), Facing.FromDegrees(-68f));

            destination = new Vec2(219f, 288f);

            return field;
        }


        /// <summary>
        /// Finding 19, from `logs/battle-20260814-225633.log` tick 2609 — the
        /// same symptom as 18 and a different cause again.
        /// </summary>
        /// <remarks>
        /// <code>
        /// 2609 > pushing through its own Swordsmen — no way round it and no gap
        ///        to thread. by (245,247) → (283,173). 1 of its own on that line.
        /// </code>
        /// An 84 m hop diagonally past <b>one</b> body, with the destination only
        /// 45 m from that body's centre. Measured: the sweep meets the Swordsmen
        /// after <b>6 m</b>, and the second leg of the detour is blocked at
        /// <b>every</b> stand-off from 46 m out to 186 m — while the destination
        /// itself is clear (<c>FormationFits</c> true). The place is fine; only
        /// the approach clips.
        /// </remarks>
        private static Battlefield TheShortHopPastACorner(
            out UnitInstance cavalry, out Vec2 destination)
        {
            var field = new Battlefield("plains", 225633);

            Battlefield.Hold(field.Add(0, "horsearchers", new Vec2(163f, 213f), Facing.FromDegrees(0f)));
            Battlefield.Hold(field.Add(0, "archers", new Vec2(213f, 213f), Facing.FromDegrees(0f)));
            Battlefield.Hold(field.Add(0, "swordsmen", new Vec2(263f, 213f), Facing.FromDegrees(0f)));

            // 179° off the line it is about to walk, as the recording reports.
            cavalry = field.Add(0, "cavalry", new Vec2(245f, 247f), Facing.FromDegrees(117f));

            destination = new Vec2(283f, 173f);

            return field;
        }

        [Fact(Skip = "Finding 19 — reproduced and diagnosed, not yet fixed. A one-waypoint detour " +
                     "cannot express the route this needs, which is a two-bend walk round the body.")]
        public void AShortHopPastOneCornerGoesRoundTheCorner()
        {
            Battlefield field = TheShortHopPastACorner(out UnitInstance cavalry, out Vec2 destination);

            Plan plan = Marching.PlanTo(
                field.State, cavalry, field.Pathfinder, destination, field.Transcript);

            foreach (string said in field.Transcript.Lines) _out.WriteLine(said);

            // The ground it was sent to is fine — so this is about the approach,
            // not the destination, and rung three is not covering for a bad order.
            Assert.True(field.State.FormationFits(cavalry, destination, Facing.FromDegrees(180f)),
                "The destination itself cannot be stood on, so there is nothing to route to and this " +
                "measures nothing.");

            Assert.False(
                Marching.IsClearLine(field.State, cavalry, cavalry.Position, destination,
                                     Marching.AlongTheLine(cavalry.Position, destination, cavalry.Facing)),
                "The straight line is clear, so nothing needed avoiding and this measures nothing.");

            Assert.False(plan.PressedThrough,
                "An 84 m hop past one regiment, with open ground on the far side of it, and the answer " +
                "was to walk through it. The route it needs comes in from due east along the body's " +
                "face, which is two bends — and rung two only ever builds one waypoint.");
        }

        [Fact]
        public void AGapWideEnoughToThreadIsThreaded()
        {
            Battlefield field = TheGapItWouldNotThread(out UnitInstance cavalry, out Vec2 destination);

            Plan plan = Marching.PlanTo(
                field.State, cavalry, field.Pathfinder, destination, field.Transcript);

            foreach (string said in field.Transcript.Lines) _out.WriteLine(said);

            // How much room there actually is, measured rather than asserted
            // from the arrangement — the two bodies are 20 m deep and 50 m apart.
            _out.WriteLine($"gap between them: {263f - 213f - 20f:0} m; " +
                           $"cavalry side-on: {cavalry.Footprint.Depth:0} m; " +
                           $"face-on: {cavalry.Footprint.Width:0} m.");

            // Non-vacuity: the straight line must genuinely be blocked, or there
            // was nothing to thread and this measures nothing.
            Assert.False(
                Marching.IsClearLine(field.State, cavalry, cavalry.Position, destination,
                                     Marching.AlongTheLine(cavalry.Position, destination, cavalry.Facing)),
                "The straight line is clear, so no gap needed threading and this measures nothing.");

            // Between the two of them, not round the outside of the pair. Both
            // avoid a collision and only one of them is threading the gap, so
            // asserting only "did not press through" would pass on the wrong
            // behaviour.
            bool wentBetweenThem = false;

            foreach (Vec2 at in plan.Path.Waypoints)
                if (at.X > 233f && at.X < 243f) wentBetweenThem = true;

            Assert.True(wentBetweenThem,
                "It kept clear, but not by going between them — no waypoint is in the gap. " +
                "Going the long way round the whole pair is a different answer to threading.");

            Assert.False(plan.PressedThrough,
                "There is a 30 m gap between the two of them and the regiment is 20 m across side-on. " +
                "It shouldered through instead. Nothing in rung two ever aims at the middle of a gap — " +
                "both strategies offset from one blocker's centre, and crabbing walks the straight line " +
                "as drawn rather than shifting it into the space.");
        }
    }
}
