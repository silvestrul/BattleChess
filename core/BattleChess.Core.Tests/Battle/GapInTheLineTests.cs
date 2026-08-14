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

        [Fact(Skip = "Finding 18 — reproduced, not yet fixed. Kept red-in-waiting rather than " +
                     "deleted: it fails for the right reason, first try, off the recorded coordinates.")]
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

            Assert.False(plan.PressedThrough,
                "There is a 30 m gap between the two of them and the regiment is 20 m across side-on. " +
                "It shouldered through instead. Nothing in rung two ever aims at the middle of a gap — " +
                "both strategies offset from one blocker's centre, and crabbing walks the straight line " +
                "as drawn rather than shifting it into the space.");
        }
    }
}
