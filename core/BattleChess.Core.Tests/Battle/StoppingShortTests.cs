using System;
using System.Collections.Generic;
using BattleChess.Contracts;
using BattleChess.Rules;
using Xunit;
using Xunit.Abstractions;

namespace BattleChess.Tests.Battle
{
    /// <summary>
    /// A regiment that cannot get where it was sent goes as near as it can, and
    /// keeps trying from there.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>M127, the designer's rule:</b> <i>"i want always to go to the closest
    /// destination possible"</i>. The planner's half of it is measured on the
    /// bench; this is the half the bench cannot see, because it is not about
    /// what one plan answers but about what happens on the ticks after it.
    /// </para>
    /// <para>
    /// The fault it was written against: <c>KeepTheMarchHonest</c> only ever
    /// watched a march <i>in progress</i>, so a route that stopped short read
    /// exactly like an arrival. The regiment finished its route, stopped
    /// marching, and nothing looked at it again - it would stand there with the
    /// way ahead clearing in front of it for the rest of the battle.
    /// </para>
    /// <para>
    /// Both tests share one arrangement and differ only in whether the way ever
    /// clears, because the rule has two halves that pull against each other: it
    /// has to keep trying, and it has to stop.
    /// </para>
    /// <para>
    /// <b>Both are red, and they are red on the planner rather than on anything
    /// here.</b> Their non-vacuity guard fires: the regiment reaches the goal
    /// through a solid wall of its own. It is refused at the ladder's third rung
    /// and gets there anyway, because <b>refusing to press at rung three does not
    /// refuse the press - it hands the order to the grid and the search, and
    /// neither of those knows the rule</b>. That is [M127c], and it is the thing
    /// M127 still needs before it can ship: the ceiling has to be enforced where a
    /// route is accepted, not where one of several stages proposes it. These two
    /// stay red until it is, because the arrangement is right and the code is not.
    /// </para>
    /// <para>
    /// What they did already prove, before the guard started firing: with the
    /// ladder refusing, a regiment stopped 183 m short of a wall, tried four
    /// times, gave up saying so - and when the wall marched away it saw its way
    /// again and finished the order. The keep-trying machinery in
    /// <c>OrderSystem</c> works; what is missing is the refusal it was written to
    /// follow.
    /// </para>
    /// </remarks>
    /// <remarks>
    /// In the levers collection, and it turns one on: <c>PricePressingHonestly</c>
    /// ships off, because M127c found it contradicts rules already decided. These
    /// two tests are what say the machinery behind it works, so they set it for
    /// their own duration and put it back. Serialised with every other test that
    /// drives a planner lever, because a static switched under a class running
    /// beside this one is the fault PlanningProfile's own remarks describe.
    /// </remarks>
    [Collection(PlannerLevers.Name)]
    public sealed class StoppingShortTests : IDisposable
    {
        private readonly ITestOutputHelper _out;
        private readonly bool _wasPricing = Marching.PricePressingHonestly;

        public StoppingShortTests(ITestOutputHelper output)
        {
            _out = output;
            Marching.PricePressingHonestly = true;
        }

        public void Dispose() => Marching.PricePressingHonestly = _wasPricing;

        /// <summary>
        /// How near the order counts as arrived, matching <c>OrderSystem</c>'s
        /// own figure. One map cell.
        /// </summary>
        private const float Arrived = 25f;

        /// <summary>
        /// A wall of its own right across the field, and the goal a short hop
        /// beyond it.
        /// </summary>
        /// <remarks>
        /// Across the whole field on purpose. A line with an end is a line to be
        /// walked round, and every earlier attempt at this arrangement was
        /// walked round - which the guard below caught, and which is why the
        /// wall reaches both edges. The goal sits only a little way past it so
        /// that any detour is long against the march it replaces.
        /// </remarks>
        private static List<UnitInstance> WalledIn(
            Battlefield field, out UnitInstance mover, out Vec2 goal)
        {
            var wall = new List<UnitInstance>();

            for (float y = 40f; y < 1010f; y += 85f)
            {
                UnitInstance held = field.Add(
                    0, "spearmen", new Vec2(1000f, y), Facing.East, strength: 2000);
                Battlefield.Hold(held);
                wall.Add(held);
            }

            goal = new Vec2(1150f, 500f);

            mover = field.Add(0, "swordsmen", new Vec2(500f, 500f), Facing.East);
            field.March(mover, goal);

            return wall;
        }

        [Fact]
        public void AMarchThatStopsShortTriesAgainAsTheFieldClears()
        {
            var field = new Battlefield("plains", 30260);

            List<UnitInstance> wall = WalledIn(field, out UnitInstance mover, out Vec2 goal);

            field.RunTurns(12);

            float shortBy = Vec2.Distance(mover.Position, goal);

            _out.WriteLine($"walled in: {shortBy:0} m short, marching: {mover.IsMarching}");

            // Non-vacuity. If it got there anyway, this arrangement is measuring
            // nothing and the assert below would pass on any code at all.
            Assert.True(shortBy > Arrived,
                $"It reached the goal with {shortBy:0} m to spare, so the wall never stopped it and " +
                "nothing below is being tested. The arrangement needs to be harder, not the assert " +
                "looser.");

            // The wall walks away north, which is the field clearing round a
            // regiment that has already stopped - the case the rule exists for.
            foreach (UnitInstance held in wall)
                field.March(held, held.Position + new Vec2(0f, 900f));

            field.RunTurns(60);

            float endedUp = Vec2.Distance(mover.Position, goal);

            _out.WriteLine($"way cleared: {endedUp:0} m short");

            Assert.True(endedUp < shortBy - 50f,
                $"It stopped {shortBy:0} m short and was still {endedUp:0} m short sixty turns after " +
                "the regiments in its way had walked off. A march that stops short has finished its " +
                "route and not its order, and nothing was asking it to try again.");
        }

        /// <summary>
        /// And it still ends: a regiment that gains nothing gives up and says so.
        /// </summary>
        /// <remarks>
        /// The other half of the same rule, and what keeps
        /// <c>OrdersAlwaysEndTests</c>' promise. Trying again for ever is not
        /// "closest possible", it is a seizure with better manners.
        /// </remarks>
        [Fact]
        public void AndStopsWhenTryingAgainGainsNothing()
        {
            var field = new Battlefield("plains", 30261);

            WalledIn(field, out UnitInstance mover, out Vec2 goal);

            field.RunTurns(60);

            float shortBy = Vec2.Distance(mover.Position, goal);

            _out.WriteLine(
                $"walled in for sixty turns: {shortBy:0} m short, {mover.FailedReplans} tries, " +
                $"marching: {mover.IsMarching}");

            Assert.True(shortBy > Arrived,
                $"It reached the goal with {shortBy:0} m to spare, so the wall never stopped it and " +
                "nothing below is being tested.");

            Assert.False(mover.IsMarching,
                "It is still marching at ground it has had sixty turns to fail to reach. An order " +
                "that keeps trying for ever is the seizure this rule replaced, not the rule.");

            Assert.True(
                field.TimesSaid("short of where it was sent") > 0 ||
                field.TimesSaid("cannot get to where it was sent") > 0,
                "It stopped without ever saying why, which is the one thing an order that gives up " +
                "must not do.");
        }
    }
}
