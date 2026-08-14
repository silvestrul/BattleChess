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

        /// <summary>Every way of looking, for the harness that compares them.</summary>
        public static IReadOnlyList<IWayRound> All { get; } =
            new[] { PastTheFirstThing, PastEverythingInTheWay, RoundAndRoundAgain };

        /// <summary>What a march uses when nobody says otherwise.</summary>
        /// <remarks>
        /// Chosen by measurement, not by argument. Against six crowds of
        /// increasing density, the old rule found a way round <b>once</b>; both
        /// repairs found one four times. What separated them was M17 — how much
        /// the detour costs — and bending twice around two bodies adds about
        /// half what standing off further from one does: 26 m against 58 m on
        /// the densest crowd either could solve. More waypoints, less walking.
        ///
        /// The other two are kept rather than deleted. They are the record of
        /// what was tried, and the table that chose between them is a test that
        /// still runs.
        /// </remarks>
        public static IWayRound Default { get; } = RoundAndRoundAgain;

        /// <summary>
        /// The two aiming points either side of a body, and the axis they lie on.
        /// </summary>
        private static (Vec2 Across, float Beside) Tangents(
            UnitInstance unit, UnitInstance blocker, Vec2 travel, float extra)
        {
            Vec2 across = new Vec2(-travel.Y, travel.X).Normalised();

            var body = new OrientedRect(unit.Position, unit.Facing, unit.Footprint);

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
                    Marching.FirstBodyInTheWay(battle, unit, unit.Position, destination, unit.Facing, out _);

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
                    if (!Marching.IsClearLeg(battle, unit, unit.Position, through, unit.Facing)) continue;

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
                    Marching.FirstBodyInTheWay(battle, unit, unit.Position, destination, unit.Facing, out _);

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

                        if (!Marching.IsClearLeg(battle, unit, unit.Position, through, unit.Facing)) continue;

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
                    Marching.FirstBodyInTheWay(battle, unit, unit.Position, destination, unit.Facing, out _);

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
                    if (!Bend(battle, unit, unit.Position, through, unit.Facing, legs)) continue;

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
                    Marching.FirstBodyInTheWay(battle, unit, from, to, unit.Facing, out _);

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
