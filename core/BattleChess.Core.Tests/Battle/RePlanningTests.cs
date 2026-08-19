using BattleChess.Contracts;
using BattleChess.Rules;
using Xunit;
using Xunit.Abstractions;

namespace BattleChess.Tests.Battle
{
    /// <summary>
    /// How often a march is planned again, and why.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Written after the fault, because the suite could not have caught it.</b>
    /// Every test in this project asks where regiments ended up. Re-planning
    /// moves nobody: a regiment that plans the same route sixty times a second
    /// stands in exactly the place a regiment that planned it once would stand,
    /// so 574 passing tests said nothing at all while a single frame took 608
    /// milliseconds.
    /// </para>
    /// <para>
    /// What makes these possible is that plans are now counted
    /// (<see cref="BattleState.RoutesPlanned"/>). The assertions below are on
    /// that count, which is the quantity that was wrong — not on positions,
    /// which were right throughout.
    /// </para>
    /// <para>
    /// The rule under test is <b>M39</b>: ask again when the answer could have
    /// changed, and otherwise on a cadence the route sets for itself.
    /// </para>
    /// </remarks>
    public sealed class RePlanningTests
    {
        private readonly ITestOutputHelper _out;

        public RePlanningTests(ITestOutputHelper output) => _out = output;

        // ---- The fault itself ----------------------------------------------
        //
        // Each of these was checked against the fault put back, which is the
        // only thing that makes a test like this worth having. Three earlier
        // drafts passed either way: a regiment in *contact* plans nothing at all
        // — the attack path returns before it gets that far — so the arrangement
        // that looks most like the freeze does not reproduce it. What does is a
        // chase that keeps nearly arriving: a broken enemy running, or a
        // regiment shuffling behind one of its own.

        [Fact]
        public void ChasingABrokenEnemyDoesNotPlanARouteEveryTick()
        {
            var field = new Battlefield("plains", 44001);

            UnitInstance quarry = field.Add(1, "archers", field.Centre + new Vec2(120f, 0f), Facing.East);
            UnitInstance horse = field.Add(0, "cavalry", field.Centre, Facing.East);

            Battlefield.Press(horse, quarry);
            field.RunTurns(1);

            // Break it, so it runs and the chase has to be re-aimed as it goes.
            // This is the case the recording named: worst after a line breaks.
            quarry.TakeCasualties((int)(quarry.Strength * 0.8f));
            field.RunTurns(1);

            const int ticks = 120;
            int planned = field.RunTicksCountingRoutes(ticks);

            _out.WriteLine($"{planned} routes over {ticks} ticks chasing a rout.");

            // Measured: 51 with the fault, 25 with the rule. The bar sits
            // between them and near the fault, so it catches the fault without
            // pinning the rule to a number it has no reason to hold.
            Assert.True(planned < 40,
                $"A chase after a broken enemy planned {planned} routes over {ticks} ticks. " +
                "Its quarry moves, so some of those are real — but not one a tick.");
        }

        [Fact]
        public void ARegimentShufflingBehindOneOfItsOwnAsksOnACadence()
        {
            var field = new Battlefield("plains", 51003);

            UnitInstance quarry = field.Add(1, "spearmen", field.Centre + new Vec2(200f, 0f), Facing.West);
            Battlefield.Hold(quarry);

            // One of ours gets there first, so the second has to wait behind it
            // with its aim point permanently just out of reach.
            UnitInstance first = field.Add(0, "swordsmen", field.Centre + new Vec2(120f, 0f), Facing.East);
            Battlefield.Press(first, quarry);

            UnitInstance behind = field.Add(0, "swordsmen", field.Centre, Facing.East);
            Battlefield.Press(behind, quarry);

            field.RunTurns(3);

            const int ticks = 120;
            int planned = field.RunTicksCountingRoutes(ticks);

            _out.WriteLine($"{planned} routes over {ticks} ticks, one regiment stuck behind another.");

            // Two attackers, so the ceiling the cadence allows is two every five
            // ticks. The fault would be two a tick.
            Assert.True(planned <= 2 * ticks / LeastTicksBetweenAsking,
                $"Two regiments queueing planned {planned} routes over {ticks} ticks. The cadence " +
                $"allows at most {2 * ticks / LeastTicksBetweenAsking}.");
        }

        /// <summary>Mirrors the rule's own floor, so the bar moves when it does.</summary>
        private const int LeastTicksBetweenAsking = 5;

        // ---- The rule's two halves -----------------------------------------

        [Fact]
        public void AMarchAcrossOpenGroundIsPlannedOnceAndNotAgain()
        {
            var field = new Battlefield("plains", 44004);

            UnitInstance foot = field.Add(0, "swordsmen", field.Centre, Facing.East);

            field.March(foot, field.Centre + new Vec2(400f, 0f));

            const int ticks = 120;
            int planned = field.RunTicksCountingRoutes(ticks);

            _out.WriteLine($"{planned} routes over {ticks} ticks walking open grass.");

            // Nothing is in the way and nothing changes, so there is nothing to
            // ask about. This is the case the cadence must never touch.
            Assert.True(planned == 0,
                $"A march across empty ground planned {planned} further routes. Nothing had happened " +
                "that could change the answer.");
        }

        [Fact]
        public void AFriendStoppingAcrossTheNextLegGetsTheRoutePlannedAgain()
        {
            var field = new Battlefield("plains", 44005);

            UnitInstance quarry = field.Add(1, "archers", field.Centre + new Vec2(420f, 0f), Facing.West);
            Battlefield.Hold(quarry);

            UnitInstance horse = field.Add(0, "cavalry", field.Centre, Facing.East);
            Battlefield.Press(horse, quarry);

            // Two ticks: enough to be walking, early enough that the whole
            // 392 m leg is still ahead of it.
            field.RunTicksCountingRoutes(2);

            Assert.True(horse.IsMarching, "The chase must still be walking for this to mean anything.");

            int settled = field.RunTicksCountingRoutes(20);

            // Eighty metres ahead on the leg it is walking. Well beyond the
            // leaving grace — a body it is already touching when it sets off is
            // deliberately not a blocker, which is what caught the first draft
            // of this test.
            Vec2 along = (horse.Route!.Target - horse.Position).Normalised();
            Vec2 across = horse.Position + along * 80f;

            UnitInstance friend = field.Add(0, "spearmen", across, Facing.North);
            Battlefield.Hold(friend);

            int after = field.RunTicksCountingRoutes(5);

            _out.WriteLine($"{settled} routes over 20 quiet ticks, {after} over the 5 after a friend " +
                           $"stopped 80 m up the leg. Friend on field: {friend.IsOnField}.");

            Assert.True(after > 0,
                "One of its own came to stand across the leg it was walking. That is exactly the " +
                "event a route should be planned again for, and it planned nothing.");
        }

        [Fact]
        public void NoChaseAsksOnAClock()
        {
            // The same arrangement at two distances, ninety ticks each. The two
            // counts are deliberately NOT compared with each other: over a fixed
            // window the far chase is still walking and finishing legs while the
            // near one has long since arrived, so one being larger says nothing.
            // What both must show is that neither is asking on a clock — a
            // cadence of one tick in five would put eighteen here.
            int near = RoutesWhileChasing(44006, 70f);
            int far = RoutesWhileChasing(44007, 420f);

            _out.WriteLine($"short approach: {near} routes.  long approach: {far} routes, over 90 ticks.");

            Assert.True(near <= 5 && far <= 5,
                $"A short approach planned {near} routes and a long one {far} over ninety ticks. " +
                "A chase plans when it sets out and when something changes, and neither of those " +
                "happens often enough to reach this many.");
        }

        private static int RoutesWhileChasing(ulong seed, float away)
        {
            var field = new Battlefield("plains", seed);

            UnitInstance quarry = field.Add(1, "archers", field.Centre + new Vec2(away, 0f), Facing.West);
            Battlefield.Hold(quarry);

            UnitInstance horse = field.Add(0, "cavalry", field.Centre, Facing.East);
            Battlefield.Press(horse, quarry);

            return field.RunTicksCountingRoutes(90);
        }
    }
}
