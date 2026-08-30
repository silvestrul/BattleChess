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
        /// <summary>Corner walks cut short by the order's search budget - M122.</summary>
        [ThreadStatic] internal static int GaveUpWalkingCorners;

        /// <summary>Room to spare when aiming past a corner (M19a).</summary>
        internal const float MarginMetres = 8f;

        public static readonly IWayRound PastTheFirstThing = new PastTheFirst();
        public static readonly IWayRound PastEverythingInTheWay = new PastEverything();
        public static readonly IWayRound RoundAndRoundAgain = new RoundTwice();
        public static readonly IWayRound StandOffOrBendAgain = new EitherWay();
        public static readonly IWayRound RoundTheCornersOfIt = new RoundTheCorners();
        public static readonly IWayRound WhicheverGetsThere = new AnyOfThem();

        /// <summary>Every way of looking, for the harness that compares them.</summary>
        public static IReadOnlyList<IWayRound> All { get; } =
            new[]
            {
                PastTheFirstThing, PastEverythingInTheWay, RoundAndRoundAgain,
                StandOffOrBendAgain, RoundTheCornersOfIt, WhicheverGetsThere
            };

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
        public static IWayRound Default { get; } = WhicheverGetsThere;

        /// <summary>
        /// Try the aiming rules first and the corner walk when they find
        /// nothing; take the cheaper when both do.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>M28.</b> The corner walk is strictly more capable — it can express
        /// routes the aiming rules cannot build at any parameter — but it is not
        /// strictly better, because it prices its search in metres and a short
        /// arc chosen for its wheel can beat a shorter walk that reverses twice.
        /// So they compete, in seconds, exactly as arching and crabbing do.
        /// </para>
        /// <para>
        /// The aiming rules are asked first and their answer used when the corner
        /// walk finds nothing, so the common crowd keeps the routes it already
        /// had and the tables that chose between them still mean what they said.
        /// </para>
        /// </remarks>
        private sealed class AnyOfThem : IWayRound
        {
            public string Name => "aim, or walk the corners";

            public IReadOnlyList<Vec2>? Round(BattleState battle, UnitInstance unit, Vec2 destination)
            {
                IReadOnlyList<Vec2>? aimed = StandOffOrBendAgain.Round(battle, unit, destination);
                IReadOnlyList<Vec2>? walked = RoundTheCornersOfIt.Round(battle, unit, destination);

                if (aimed == null) return walked;
                if (walked == null) return aimed;

                return Marching.SecondsToWalk(battle, unit, walked) <
                       Marching.SecondsToWalk(battle, unit, aimed)
                    ? walked
                    : aimed;
            }
        }

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
        /// Walk the corners of what is in the way, however many bends it takes.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>M28</b>, and it replaces guessing an aiming point with looking at
        /// the shape. Every strategy above aims <i>one</i> point perpendicular to
        /// the line of march, offset from a blocker's centre — and that
        /// construction has now been patched three times ([M19a](../../../../docs/DECISIONS.md)'s
        /// margin, standing off in steps, and [M27](../../../../docs/DECISIONS.md)'s
        /// corridors) with each fix uncovering the next arrangement it cannot
        /// describe.
        /// </para>
        /// <para>
        /// The one that broke it is the simplest arrangement in the game.
        /// Recorded 14 August: <b>a single Spearmen regiment alone in open
        /// ground</b>, sitting almost exactly halfway along a 71 m march, and
        /// every order across it shouldered through. Measured, the second leg of
        /// a one-bend detour was blocked at <i>every</i> stand-off from 46 m out
        /// to 186 m, while the destination itself was perfectly clear. The place
        /// was fine; only the approach clipped. The route that works comes in
        /// along the body's face — which is two bends, and one waypoint cannot
        /// express two bends at any parameter value.
        /// </para>
        /// <para>
        /// So: grow each body by the mover's own reach, take the corners of what
        /// that leaves, and find the cheapest way from here to there through
        /// them. It is a search, which [M10](../../../../docs/DECISIONS.md)
        /// treats as the escape hatch — but over a graph of <b>a few dozen
        /// corners</b> rather than four thousand grid cells, and only on the rung
        /// the ordinary march never reaches. The corners are where a route can
        /// bend; nowhere else is.
        /// </para>
        /// </remarks>
        private sealed class RoundTheCorners : IWayRound
        {
            /// <summary>How many bodies' corners are worth considering.</summary>
            /// <remarks>
            /// A bound on the work rather than a rule about routing. The graph is
            /// four corners a body plus the two ends, so six bodies is 26 nodes —
            /// small enough that the cost is in the leg checks, and those are the
            /// same sweep every other strategy already runs.
            /// </remarks>
            private const int MostBodies = 6;

            public string Name => "round the corners";

            public IReadOnlyList<Vec2>? Round(BattleState battle, UnitInstance unit, Vec2 destination)
            {
                Vec2 travel = destination - unit.Position;
                float length = travel.Length;

                if (length <= 0f) return null;

                var nodes = new List<Vec2> { unit.Position, destination };

                // Grown by the mover's own reach, taken as its half-diagonal so
                // the figure does not depend on which way it happens to be
                // pointing on the leg — the one thing that varies leg by leg and
                // cannot be known while the route is still being chosen.
                float reach = MathF.Sqrt(
                    unit.Footprint.Width * unit.Footprint.Width +
                    unit.Footprint.Depth * unit.Footprint.Depth) * 0.5f + MarginMetres;

                int taken = 0;

                foreach (UnitInstance other in NearestFirst(battle, unit, destination, length))
                {
                    if (taken++ >= MostBodies) break;

                    Facing front = other.Facing;

                    var ahead = new Vec2(MathF.Cos(front.Radians), MathF.Sin(front.Radians));
                    var beside = new Vec2(-ahead.Y, ahead.X);

                    float deep = other.Footprint.Depth * 0.5f + reach;
                    float wide = other.Footprint.Width * 0.5f + reach;

                    for (int i = -1; i <= 1; i += 2)
                    for (int j = -1; j <= 1; j += 2)
                        nodes.Add(other.Position + ahead * (deep * i) + beside * (wide * j));
                }

                if (nodes.Count <= 2) return null;

                return Cheapest(battle, unit, nodes);
            }

            /// <summary>
            /// Ours, nearest the line of march first — so the cap falls on the
            /// bodies least likely to matter.
            /// </summary>
            private static List<UnitInstance> NearestFirst(
                BattleState battle, UnitInstance unit, Vec2 destination, float length)
            {
                Vec2 along = (destination - unit.Position) / length;

                var found = new List<UnitInstance>();
                var away = new List<float>();

                foreach (UnitInstance other in battle.UnitsOnField())
                {
                    if (ReferenceEquals(other, unit)) continue;
                    if (other.Owner != unit.Owner) continue;

                    Vec2 offset = other.Position - unit.Position;

                    // Distance to the line of march, clamped to the stretch of it
                    // that is actually being walked.
                    float ahead = MathF.Max(0f, MathF.Min(length, Vec2.Dot(offset, along)));

                    found.Add(other);
                    away.Add(Vec2.Distance(other.Position, unit.Position + along * ahead));
                }

                // Ascending id within equal distance, because two bodies the same
                // way off must not order themselves by whatever the field
                // happened to return.
                var order = new int[found.Count];
                for (int i = 0; i < order.Length; i++) order[i] = i;

                Array.Sort(order, (a, b) =>
                {
                    int byDistance = away[a].CompareTo(away[b]);
                    return byDistance != 0 ? byDistance : found[a].Id.Value.CompareTo(found[b].Id.Value);
                });

                var sorted = new List<UnitInstance>(found.Count);
                foreach (int i in order) sorted.Add(found[i]);

                return sorted;
            }

            /// <summary>
            /// The cheapest walk from the first node to the second through any of
            /// the rest.
            /// </summary>
            /// <remarks>
            /// Plain Dijkstra over a couple of dozen points, priced in metres.
            /// Metres and not seconds, deliberately: what a leg costs in time
            /// depends on the front the regiment arrives on, which depends on the
            /// leg before it, and a cost that depends on the path taken is not
            /// something Dijkstra may be handed. The route that comes back is
            /// then priced properly in seconds by the ladder, against the other
            /// rungs, which is where the comparison belongs.
            /// </remarks>
            private static IReadOnlyList<Vec2>? Cheapest(
                BattleState battle, UnitInstance unit, List<Vec2> nodes)
            {
                int count = nodes.Count;

                var best = new float[count];
                var cameFrom = new int[count];
                var settled = new bool[count];

                for (int i = 0; i < count; i++)
                {
                    best[i] = float.MaxValue;
                    cameFrom[i] = -1;
                }

                best[0] = 0f;

                // How far each node still is from the finish, in a straight
                // line. Costs here are metres, so this can never overstate what
                // remains — which is what lets it both order the search and
                // prune it without either changing the answer.
                var toGoal = new float[count];
                for (int i = 0; i < count; i++) toGoal[i] = Vec2.Distance(nodes[i], nodes[1]);

                for (int step = 0; step < count; step++)
                {
                    // The dearest loop in the cascade, and the one that makes a
                    // cap on the whole order impossible to honour: [M120]
                    // charged 41-49% of every clearance check in the game to
                    // this walk. Stopping here is safe because nothing is half
                    // built - cameFrom holds only legs IsClearLeg has already
                    // passed, so the route that comes back is a real one that
                    // merely may not be the cheapest. See [M122].
                    if (Marching.StopSearchingWhenOutOfTime && Marching.StopNow())
                    {
                        GaveUpWalkingCorners++;
                        break;
                    }

                    int at = -1;
                    float leading = float.MaxValue;

                    // Nearest the finish rather than nearest the start. Plain
                    // Dijkstra settles outward in every direction at once and
                    // asks the dear clearance question about each of them; the
                    // same search told which way the finish lies reaches it
                    // having settled a fraction as many.
                    for (int i = 0; i < count; i++)
                    {
                        if (settled[i] || best[i] >= float.MaxValue) continue;

                        float reckoned = best[i] + toGoal[i];
                        if (reckoned >= leading) continue;

                        leading = reckoned;
                        at = i;
                    }

                    if (at < 0) break;
                    if (at == 1) break;

                    // Nothing still to come can beat a finish already reached.
                    if (leading >= best[1]) break;

                    settled[at] = true;

                    for (int to = 0; to < count; to++)
                    {
                        if (settled[to] || to == at) continue;

                        float step2 = Vec2.Distance(nodes[at], nodes[to]);
                        float reached = best[at] + step2;

                        if (reached >= best[to]) continue;

                        // Nor can a way through this node, if going on from it
                        // is already dearer than the finish in hand.
                        if (reached + toGoal[to] >= best[1]) continue;

                        // The expensive question, asked last and only when the
                        // answer could change the route.
                        if (!Marching.IsClearLeg(battle, unit, nodes[at], nodes[to], unit.Facing)) continue;

                        best[to] = reached;
                        cameFrom[to] = at;
                    }
                }

                if (cameFrom[1] < 0) return null;

                var back = new List<Vec2>();

                for (int at = 1; at >= 0; at = cameFrom[at])
                {
                    back.Add(nodes[at]);
                    if (at == 0) break;
                }

                back.Reverse();

                // One hop is the straight line, which rung one has already
                // refused. Nothing to report.
                return back.Count > 2 ? back : null;
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
