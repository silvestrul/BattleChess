using System;
using System.Collections.Generic;
using BattleChess.Contracts;

namespace BattleChess.Rules
{
    /// <summary>
    /// How a march finds its way past something standing in it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>M18</b> rung two, made swappable on purpose. A recorded game had
    /// regiments shouldering through their own on ninety-metre marches with one
    /// friend nearby and open field either side of it — rung <i>three</i>
    /// answering what rung two should have. The cause is not that pushing
    /// through won an argument; it is that the way round was never found, so
    /// there was nothing for it to lose to.
    /// </para>
    /// <para>
    /// There are several plausible repairs and no evidence yet which is right,
    /// which is exactly the situation that produces a confident guess and two
    /// passes of undoing it. So they are written side by side and measured
    /// against the same arrangements instead.
    /// </para>
    /// </remarks>
    public interface IWayRound
    {
        string Name { get; }

        /// <summary>
        /// A line past the obstruction, or null if this way of looking cannot
        /// find one. The first and last points are where the regiment is and
        /// where it was sent.
        /// </summary>
        IReadOnlyList<Vec2>? Round(BattleState battle, UnitInstance unit, Vec2 destination);
    }

    /// <summary>The ways of getting round something, and the one used by default.</summary>
    public static class WaysRound
    {
        /// <summary>Room to spare when aiming past a corner (M19a).</summary>
        internal const float MarginMetres = 8f;

        public static readonly IWayRound PastTheFirstThing = new PastTheFirst();
        public static readonly IWayRound PastEverythingInTheWay = new PastEverything();
        public static readonly IWayRound RoundAndRoundAgain = new RoundTwice();
        public static readonly IWayRound StandOffOrBendAgain = new EitherWay();

        /// <summary>Every way of looking, for the harness that compares them.</summary>
        public static IReadOnlyList<IWayRound> All { get; } =
            new[] { PastTheFirstThing, PastEverythingInTheWay, RoundAndRoundAgain, StandOffOrBendAgain };

        /// <summary>What a march uses when nobody says otherwise.</summary>
        /// <remarks>
        /// <para>
        /// Chosen by measurement, and <b>changed once by better measurement</b>.
        /// Against six invented crowds all three were separated only by how far
        /// the detour ran, so M17 broke the tie and bending twice round two
        /// bodies won on distance — 26 m against 58 m. That was a real
        /// comparison of an arrangement nobody plays in.
        /// </para>
        /// <para>
        /// The arrangement everybody plays in is a <b>formed line</b>: regiments
        /// drawn up shoulder to shoulder, which is what M2 exists to permit, and
        /// where the way round the neighbour on one side is blocked by the
        /// neighbour on the other. Taken from a recording in which every one of
        /// eight press-throughs set off from the same forty metres of ground:
        /// </para>
        /// <code>
        /// gets a regiment out of its own line, out of six:
        ///   past the first thing           0
        ///   past everything in the way     6
        ///   round, and round again         1     &lt;- the old default
        /// </code>
        /// <para>
        /// Standing off further is not a prettier detour, it is the only one
        /// that exists when both flanks are occupied. But swapping to it wholesale
        /// only moved the failure: among <i>scattered</i> bodies, standing off far
        /// enough to clear the first lands on the third, and bending twice is the
        /// better answer there. Picking either is picking which half of the game
        /// to be wrong in — which is what the whole table was built to stop.
        /// </para>
        /// <para>
        /// So the fourth candidate runs both and takes the cheaper, and it is
        /// the only one that is never the worst:
        /// </para>
        /// <code>
        ///                            crowds  pressed  out of a line
        ///   past the first thing        2       4          0
        ///   past everything in the way  4       2          6
        ///   round, and round again      4       2          1
        ///   stand off, or bend again    4       2          6
        /// </code>
        /// <para>
        /// The other three are kept rather than deleted. They are the record of
        /// what was tried, and all three tables that chose between them are
        /// tests that still run.
        /// </para>
        /// </remarks>
        public static IWayRound Default { get; } = StandOffOrBendAgain;

        /// <summary>
        /// Stand off further, or bend again — whichever gets there, and the
        /// cheaper of the two when both do.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Built because the tables said neither of the other two dominates, and
        /// the failures are in different places. Standing off further is the only
        /// answer from inside a formed line, where the way round the neighbour
        /// on one flank is blocked by the neighbour on the other: 6 of 6 against
        /// 1. Bending twice is the better answer among <i>scattered</i> bodies,
        /// where standing off far enough to clear the first one lands on the
        /// third.
        /// </para>
        /// <para>
        /// Picking one is picking which half of the game to be wrong in. Running
        /// both costs a second cast on the rung that is already the uncommon
        /// case, and the cheaper wins by [M22](../../../../docs/DECISIONS.md) as
        /// arching and crabbing already do.
        /// </para>
        /// </remarks>
        private sealed class EitherWay : IWayRound
        {
            public string Name => "stand off, or bend again";

            public IReadOnlyList<Vec2>? Round(BattleState battle, UnitInstance unit, Vec2 destination)
            {
                IReadOnlyList<Vec2>? off = PastEverythingInTheWay.Round(battle, unit, destination);
                IReadOnlyList<Vec2>? bent = RoundAndRoundAgain.Round(battle, unit, destination);

                if (off == null) return bent;
                if (bent == null) return off;

                return Marching.SecondsToWalk(battle, unit, bent) < Marching.SecondsToWalk(battle, unit, off)
                    ? bent
                    : off;
            }
        }

        /// <summary>
        /// The two aiming points either side of a body, and the axis they lie on.
        /// </summary>
        private static (Vec2 Across, float Beside) Tangents(
            UnitInstance unit, UnitInstance blocker, Vec2 travel, float extra)
        {
            Vec2 across = new Vec2(-travel.Y, travel.X).Normalised();

            var body = new OrientedRect(
                unit.Position, Marching.AlongTheLine(Vec2.Zero, travel, unit.Facing), unit.Footprint);

            return (across,
                    blocker.Shape.ProjectedRadius(across)
                    + body.ProjectedRadius(across)
                    + MarginMetres
                    + extra);
        }

        /// <summary>
        /// What the code has always done: aim past the ends of the first thing in
        /// the way, and give up if either half of the detour is not clear.
        /// </summary>
        /// <remarks>
        /// Kept as a candidate rather than deleted, because it is the baseline
        /// the others have to beat and because it may still be the right answer
        /// on open ground where the extra work buys nothing.
        /// </remarks>
        private sealed class PastTheFirst : IWayRound
        {
            public string Name => "past the first thing";

            public IReadOnlyList<Vec2>? Round(BattleState battle, UnitInstance unit, Vec2 destination)
            {
                UnitInstance? blocker =
                    Marching.FirstBodyInTheWay(battle, unit, unit.Position, destination,
                                              Marching.AlongTheLine(unit.Position, destination, unit.Facing),
                                              out _);

                if (blocker == null) return null;

                Vec2 travel = destination - unit.Position;
                if (travel.IsNearZero) return null;

                (Vec2 across, float beside) = Tangents(unit, blocker, travel, 0f);

                IReadOnlyList<Vec2>? best = null;
                float shortest = float.MaxValue;

                for (int side = 0; side < 2; side++)
                {
                    Vec2 through = blocker.Position + (side == 0 ? across : -across) * beside;

                    // M23: each leg checked at the front it will be walked on,
                    // entering on the one before it.
                    if (!Marching.IsClearLeg(battle, unit, unit.Position, through,
                                             Marching.AlongTheLine(unit.Position, through, unit.Facing)))
                        continue;

                    Facing onward = Facing.FromVector(through - unit.Position);
                    if (!Marching.IsClearLeg(battle, unit, through, destination, onward)) continue;

                    var candidate = new[] { unit.Position, through, destination };

                    // M22: seconds, not metres. The two sides of a body are the
                    // same distance round and a very different wheel when the
                    // regiment is already pointing one of the two ways.
                    float cost = Marching.SecondsToWalk(battle, unit, candidate);
                    if (cost >= shortest) continue;

                    shortest = cost;
                    best = candidate;
                }

                return best;
            }
        }

        /// <summary>
        /// Aim past the first thing, and keep pushing further out until both
        /// halves of the detour are clear.
        /// </summary>
        /// <remarks>
        /// The suspicion the recording points at. A start area is crowded, so the
        /// way round the nearest regiment usually clips the next one along — and
        /// the old rule discarded the whole candidate at that point rather than
        /// standing off a little further. Stepping out is what a person does,
        /// and it terminates because the field does.
        /// </remarks>
        private sealed class PastEverything : IWayRound
        {
            /// <summary>How many times to stand off further before giving up.</summary>
            private const int Tries = 6;

            public string Name => "past everything in the way";

            public IReadOnlyList<Vec2>? Round(BattleState battle, UnitInstance unit, Vec2 destination)
            {
                UnitInstance? blocker =
                    Marching.FirstBodyInTheWay(battle, unit, unit.Position, destination,
                                              Marching.AlongTheLine(unit.Position, destination, unit.Facing),
                                              out _);

                if (blocker == null) return null;

                Vec2 travel = destination - unit.Position;
                if (travel.IsNearZero) return null;

                IReadOnlyList<Vec2>? best = null;
                float shortest = float.MaxValue;

                for (int side = 0; side < 2; side++)
                {
                    for (int step = 0; step < Tries; step++)
                    {
                        (Vec2 across, float beside) =
                            Tangents(unit, blocker, travel, step * unit.Footprint.Width * 0.5f);

                        Vec2 through = blocker.Position + (side == 0 ? across : -across) * beside;

                        if (!Marching.IsClearLeg(battle, unit, unit.Position, through,
                                             Marching.AlongTheLine(unit.Position, through, unit.Facing)))
                        continue;

                        Facing onward = Facing.FromVector(through - unit.Position);
                        if (!Marching.IsClearLeg(battle, unit, through, destination, onward)) continue;

                        var candidate = new[] { unit.Position, through, destination };

                        float cost = Marching.SecondsToWalk(battle, unit, candidate);

                        if (cost < shortest)
                        {
                            shortest = cost;
                            best = candidate;
                        }

                        // Standing off further only ever costs more, so the first
                        // one that works on this side is the best on this side.
                        break;
                    }
                }

                return best;
            }
        }

        /// <summary>
        /// Go round the first thing, and if the way round is itself blocked, go
        /// round that too — once.
        /// </summary>
        /// <remarks>
        /// The other reading of the same evidence, and a genuinely different
        /// shape: rather than standing off further from one body, it bends twice
        /// around two. More faithful to what threading a crowd looks like, and
        /// more expensive, and it is why this is a comparison rather than a
        /// choice made in advance.
        /// </remarks>
        private sealed class RoundTwice : IWayRound
        {
            public string Name => "round, and round again";

            public IReadOnlyList<Vec2>? Round(BattleState battle, UnitInstance unit, Vec2 destination)
            {
                UnitInstance? blocker =
                    Marching.FirstBodyInTheWay(battle, unit, unit.Position, destination,
                                              Marching.AlongTheLine(unit.Position, destination, unit.Facing),
                                              out _);

                if (blocker == null) return null;

                Vec2 travel = destination - unit.Position;
                if (travel.IsNearZero) return null;

                (Vec2 across, float beside) = Tangents(unit, blocker, travel, 0f);

                IReadOnlyList<Vec2>? best = null;
                float shortest = float.MaxValue;

                for (int side = 0; side < 2; side++)
                {
                    Vec2 through = blocker.Position + (side == 0 ? across : -across) * beside;

                    var legs = new List<Vec2> { unit.Position };

                    // Each bend appends the point it reaches, so the endpoints
                    // arrive with them. Adding the destination again here put a
                    // duplicate on the end of every route it produced.
                    if (!Bend(battle, unit, unit.Position, through,
                              Marching.AlongTheLine(unit.Position, through, unit.Facing), legs)) continue;

                    Facing onward = Facing.FromVector(through - unit.Position);
                    if (!Bend(battle, unit, through, destination, onward, legs)) continue;

                    // M22: the whole point of this strategy is that it buys
                    // extra bends, so it is the one most in need of being priced
                    // in the thing a bend actually costs.
                    float cost = Marching.SecondsToWalk(battle, unit, legs);

                    if (cost >= shortest) continue;

                    shortest = cost;
                    best = legs.ToArray();
                }

                return best;
            }

            /// <summary>
            /// Adds the points needed to get from one place to another, bending
            /// once around anything in between. False if even that cannot.
            /// </summary>
            private static bool Bend(
                BattleState battle, UnitInstance unit, Vec2 from, Vec2 to, Facing entering, List<Vec2> into)
            {
                if (Marching.IsClearLeg(battle, unit, from, to, entering))
                {
                    if (into.Count == 0 || into[into.Count - 1] != to) into.Add(to);
                    return true;
                }

                UnitInstance? second =
                    Marching.FirstBodyInTheWay(battle, unit, from, to,
                                              Marching.AlongTheLine(from, to, unit.Facing), out _);

                if (second == null) return false;

                Vec2 leg = to - from;
                if (leg.IsNearZero) return false;

                (Vec2 across, float beside) = Tangents(unit, second, leg, 0f);

                for (int side = 0; side < 2; side++)
                {
                    Vec2 via = second.Position + (side == 0 ? across : -across) * beside;

                    if (!Marching.IsClearLeg(battle, unit, from, via, entering)) continue;
                    if (!Marching.IsClearLeg(battle, unit, via, to, Facing.FromVector(via - from))) continue;

                    into.Add(via);
                    into.Add(to);
                    return true;
                }

                return false;
            }
        }
    }
}
