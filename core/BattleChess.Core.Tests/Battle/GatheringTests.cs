using System.Collections.Generic;
using BattleChess.Contracts;
using BattleChess.Rules;
using Xunit;
using Xunit.Abstractions;

namespace BattleChess.Tests.Battle
{
    /// <summary>
    /// Where a selection that is not a wing is sent when it is ordered
    /// somewhere [M164], and in what order the places are handed out [M165].
    /// </summary>
    public sealed class GatheringTests
    {
        private readonly ITestOutputHelper _out;

        public GatheringTests(ITestOutputHelper output) => _out = output;

        /// <summary>
        /// The arrangement out of <c>logs/battle-20260903-144857.log</c> at tick
        /// 2092.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Three Spearmen in a column, a hundred metres apart, all facing west,
        /// ordered together to a point 197,5 m west of the middle one. What the
        /// recording gave back:
        /// </para>
        /// <code>
        /// U25 at y 1737,5  ->  y 1662,5   168 m
        /// U26 at y 1637,5  ->  y 1637,5   200 m
        /// U27 at y 1537,5  ->  y 1662,5   280 m, 2 of its own on that line
        /// </code>
        /// <para>
        /// U27 begins at the south end of the column and is sent to the
        /// north-west slot, so it has to cross both its neighbours. That is the
        /// eighty metres of extra march, the press-through and the bent route
        /// out of the pose search - and none of it is a pathfinding fault. The
        /// places were right; the order they were handed out in was not.
        /// </para>
        /// <para>
        /// <b>Measured in frontages, not metres.</b> Those regiments were 80 m
        /// across because that battle raises them at a strength this field does
        /// not, and a reproduction pinned to absolute metres would quietly be a
        /// looser arrangement than the one that broke. So the spacing is the
        /// recorded 1,25 frontages and the click the recorded 2,47, whatever a
        /// regiment here happens to measure.
        /// </para>
        /// </remarks>
        [Fact]
        public void AColumnGatheringKeepsItsOrderRatherThanCrossingItself()
        {
            var field = new Battlefield();

            Facing west = Facing.FromDegrees(-180f);

            var at = new Vec2(1200f, 500f);

            // The middle one first, because its own frontage is the ruler
            // everything else is laid out with.
            UnitInstance middle = field.Add(0, "spearmen", at, west);

            float frontage = middle.Footprint.Width;

            var column = new List<UnitInstance>
            {
                field.Add(0, "spearmen", at + new Vec2(0f, frontage * 1.25f), west),
                middle,
                field.Add(0, "spearmen", at - new Vec2(0f, frontage * 1.25f), west),
            };

            Vec2 click = at + new Vec2(-frontage * 2.47f, frontage * 0.06f);

            _out.WriteLine(
                $"a regiment is {frontage:0.0} x {middle.Footprint.Depth:0.0} m, " +
                $"the column {frontage * 1.25f:0} m apart, the click {frontage * 2.47f:0} m west");

            Vec2[] given = Gathering.GatherAt(field.State, column, click);

            float longest = 0f;

            for (int i = 0; i < column.Count; i++)
            {
                float walk = Vec2.Distance(column[i].Position, given[i]);

                if (walk > longest) longest = walk;

                _out.WriteLine(
                    $"  y {column[i].Position.Y:0.0} -> ({given[i].X:0.0}, {given[i].Y:0.0}), " +
                    $"{walk:0} m, {Vec2.Distance(given[i], click):0} m off the click");
            }

            _out.WriteLine($"longest march {longest:0} m, {longest / frontage:0.00} frontages");

            // No two answers may be the same ground - that is the promise this
            // whole pass exists to keep.
            for (int i = 0; i < given.Length; i++)
            {
                for (int j = i + 1; j < given.Length; j++)
                {
                    Assert.False(
                        OrientedRect.Overlaps(
                            new OrientedRect(given[i], west, column[i].Footprint),
                            new OrientedRect(given[j], west, column[j].Footprint)),
                        "two of them were sent to the same ground.");
                }
            }

            // Non-vacuity. Exactly one gets the click, so the other two really
            // were pushed off it and really did have to be given somewhere else.
            // Three regiments each handed their own untouched place would
            // measure nothing at all.
            int atTheClick = 0;

            foreach (Vec2 place in given)
                if (Vec2.Distance(place, click) < 1f) atTheClick++;

            Assert.Equal(1, atTheClick);

            // The recorded fault. The column stands north to south, so its
            // places must run north to south too - anything else is a regiment
            // walking across one of its own to reach the far slot.
            Assert.True(
                given[0].Y > given[1].Y && given[1].Y > given[2].Y,
                $"the column crossed itself: y {given[0].Y:0} / {given[1].Y:0} / {given[2].Y:0} " +
                "for regiments standing north to south. This is the recorded fault.");

            // The recording marched 3,50 frontages on an order of 2,47. Crossing
            // is what bought that, so not crossing has to buy it back.
            Assert.True(
                longest < frontage * 3.5f,
                $"the longest march is {longest / frontage:0.00} frontages, no better than the 3,50 " +
                "the recording had.");
        }
    }
}
