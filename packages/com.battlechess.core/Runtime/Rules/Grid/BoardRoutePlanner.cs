using System;
using System.Collections.Generic;
using BattleChess.Contracts;

namespace BattleChess.Rules.Grid
{
    /// <summary>
    /// Plans a march across the board: A* over hexes, where a hex somebody is
    /// standing on is simply not a hex you may enter.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>[M147], and it is the whole reason the board is worth trying.</b>
    /// Every route planner before this one - the ladder, the search over places
    /// and fronts, the tangents, hybrid A*, the staged planner, five thousand
    /// lines between them - exists to answer one question the continuous game
    /// cannot avoid: <i>can this rectangle get past that rectangle</i>. On a
    /// board the question does not arise. A hex is free or it is held, and
    /// A* walks the free ones.
    /// </para>
    /// <para>
    /// <b>It presses through nothing, ever.</b> <c>PressedThrough</c> is false
    /// on every plan this returns, because there is no such move: a held hex is
    /// not an expensive edge, it is an absent one. Everything downstream that
    /// asks whether a route shoulders through its own side gets a straight no
    /// and needs no other change.
    /// </para>
    /// <para>
    /// <b>Cost is seconds, like every other planner here.</b> A step costs the
    /// hex spacing divided by how fast the going is, so a road detour still
    /// beats a slog through swamp. The heuristic divides the straight-line hex
    /// distance by <see cref="FastestGoingAllowedFor"/> to stay admissible -
    /// the best terrain in <c>terrain.cfg</c> is road at 1,50, so 2,0 is a real
    /// bound with room in it. If a terrain is ever authored faster than double
    /// open ground, this stops being a shortest route and becomes merely a good
    /// one; it does not become wrong.
    /// </para>
    /// <para>
    /// <b>The destination is a hex, and it may be taken.</b> An order pointed at
    /// ground somebody already holds is answered with the nearest free hex
    /// rather than refused, which is what a player clicking near a friend means
    /// and what the continuous game does by shuffling. Refusing outright would
    /// make the board feel broken for the commonest click there is.
    /// </para>
    /// </remarks>
    public sealed class BoardRoutePlanner : IRoutePlanner
    {
        /// <summary>The bound the heuristic assumes on how fast any going can be.</summary>
        /// <inheritdoc cref="BoardRoutePlanner" path="/remarks"/>
        public const float FastestGoingAllowedFor = 2f;

        /// <summary>How far from a taken destination a regiment will settle for.</summary>
        /// <remarks>
        /// Three rings is thirty-six hexes, which on a fifty-metre board is
        /// everywhere within 150 m of where the player pointed. Further than
        /// that and the regiment is no longer going where it was sent, so the
        /// order is better refused than silently rewritten.
        /// </remarks>
        public const int WillSettleWithinRings = 3;

        /// <summary>A ceiling on the search, so a hopeless order cannot hang a turn.</summary>
        /// <remarks>
        /// The Great Field is about 1 500 hexes, so this exhausts any board
        /// this game has and then some. It is a guard against a bug, not a
        /// budget: unlike the lattice, exhausting the whole board is cheap and
        /// is the worst this can ever do.
        /// </remarks>
        public const int MostHexesSearched = 20000;

        public string Name => "over the board";

        public Plan PlanTo(
            BattleState battle, UnitInstance unit, IPathfinder pathfinder, Vec2 destination,
            IBattleLog? log = null, IWayRound? wayRound = null, Facing? arriveOn = null)
        {
            if (battle == null) throw new ArgumentNullException(nameof(battle));
            if (unit == null) throw new ArgumentNullException(nameof(unit));

            Board board = Board.For(battle);
            MovementType moving = unit.Def.Movement;

            Dictionary<Coord, UnitId> taken = board.WhoIsWhere(battle);

            Coord from = board.Of(unit.Position);
            Coord wanted = board.Of(destination);

            if (!board.OnBoard(wanted))
                return NoRoute(PathFailure.GoalOffMap, $"{wanted} is off the board.");

            // The hex asked for, or the nearest one this regiment could stand
            // on. Its own hex never counts as taken, so an order that resolves
            // to where it already is comes back as a route of no legs rather
            // than as a failure.
            if (!board.NearestFree(battle, unit, wanted, taken, WillSettleWithinRings, out Coord goal))
                return NoRoute(
                    PathFailure.GoalTooTight,
                    $"nothing within {WillSettleWithinRings} hexes of {wanted} is free ground for " +
                    $"{unit.Def.DisplayName}.");

            if (board.GoingOn(battle, from, moving) <= 0f && from != goal)
                return NoRoute(PathFailure.GoalImpassable, $"{unit.Def.DisplayName} is standing on ground it cannot leave.");

            if (from == goal)
                return new Plan(
                    PathResult.Success(new[] { unit.Position }, new[] { from }, 0f, 0f, 1),
                    hold: null,
                    pressedThrough: false);

            if (!Search(battle, board, unit, taken, from, goal, moving, out List<Coord> hexes, out float _, out int looked))
                return NoRoute(PathFailure.NoRouteExists, $"no line of free hexes joins {from} to {goal}.", looked);

            return Drawn(battle, board, unit, hexes, looked, arriveOn);
        }

        /// <summary>A* from hex to hex, over free ground only.</summary>
        private static bool Search(
            BattleState battle, Board board, UnitInstance unit, IReadOnlyDictionary<Coord, UnitId> taken,
            Coord from, Coord goal, MovementType moving,
            out List<Coord> hexes, out float seconds, out int looked)
        {
            hexes = null!;
            seconds = 0f;
            looked = 0;

            float pace = MathF.Max(0.1f, unit.Def.Speed);
            float step = Board.CellWidthMetres;

            var cameFrom = new Dictionary<Coord, Coord>();
            var best = new Dictionary<Coord, float> { [from] = 0f };
            var open = new CoordMinHeap();

            open.Push(from, Guess(from, goal, step, pace));

            Span<Coord> neighbours = stackalloc Coord[HexMath.DirectionCount];

            while (open.TryPop(out Coord at))
            {
                if (++looked > MostHexesSearched) return false;

                if (at == goal)
                {
                    hexes = Retrace(cameFrom, from, goal);
                    seconds = best[goal];
                    return true;
                }

                float here = best[at];

                HexMath.Neighbours(at, neighbours);

                for (int i = 0; i < neighbours.Length; i++)
                {
                    Coord next = neighbours[i];

                    // The goal is allowed even when somebody is on it only
                    // because NearestFree already promised it is not; every
                    // other held hex is absent from the graph entirely.
                    if (taken.TryGetValue(next, out UnitId who) && who != unit.Id) continue;

                    float going = board.GoingOn(battle, next, moving);

                    if (going <= 0f) continue;

                    float through = here + step / (pace * going);

                    if (best.TryGetValue(next, out float already) && already <= through) continue;

                    best[next] = through;
                    cameFrom[next] = at;

                    open.Push(next, through + Guess(next, goal, step, pace));
                }
            }

            return false;
        }

        /// <summary>Seconds the rest of the march cannot possibly take less than.</summary>
        private static float Guess(Coord at, Coord goal, float step, float pace) =>
            Coord.Distance(at, goal) * step / (pace * FastestGoingAllowedFor);

        private static List<Coord> Retrace(IReadOnlyDictionary<Coord, Coord> cameFrom, Coord from, Coord goal)
        {
            var back = new List<Coord> { goal };

            Coord at = goal;

            while (at != from)
            {
                at = cameFrom[at];
                back.Add(at);
            }

            back.Reverse();

            return back;
        }

        /// <summary>
        /// Turns a line of hexes into the line a regiment walks.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Not smoothed, deliberately</b>, where every other planner here
        /// smooths hard. Smoothing exists to hide that a search grid is not the
        /// battlefield; on the board the grid <i>is</i> the battlefield, so a
        /// straightened line would cut corners through hexes the route was
        /// careful to avoid - which is the one thing this planner is for.
        /// </para>
        /// <para>
        /// The first waypoint is where the regiment actually stands rather than
        /// its hex's centre, so a regiment caught between hexes still walks from
        /// where it is and not from where it ought to have been.
        /// </para>
        /// <para>
        /// <b>The route stops at the end of this turn's walking, and that is
        /// what makes the board a board.</b> A regiment left mid-leg when the
        /// turn ends is standing between two hexes and holds neither, so the
        /// one promise the whole mode exists to keep - one regiment to a hex -
        /// would hold only for whoever happened to arrive. Truncating means
        /// every regiment ends every turn <i>on</i> a hex, and the drawn line
        /// stops where the regiment will actually be, which is the other thing
        /// a player is owed.
        /// </para>
        /// <para>
        /// The price is that a long march is ordered again each turn rather than
        /// once. That is how a board wargame has always worked, and it is the
        /// same allowance a player is counting in their head: foot 1,9 hexes,
        /// cavalry 5,7.
        /// </para>
        /// <para>
        /// <b>At least one hex, always.</b> Ground slow enough that a single
        /// step costs more than a whole turn would otherwise pin a regiment
        /// where it stands for ever, which reads as a bug however correct the
        /// arithmetic is. It walks the one hex and overruns.
        /// </para>
        /// </remarks>
        private static Plan Drawn(
            BattleState battle, Board board, UnitInstance unit, List<Coord> hexes, int looked, Facing? arriveOn)
        {
            float pace = MathF.Max(0.1f, unit.Def.Speed);
            float turn = BattleClock.TicksPerTurn * BattleClock.SecondsPerTick;
            float step = Board.CellWidthMetres;

            var walked = new List<Coord>(hexes.Count) { hexes[0] };
            var waypoints = new List<Vec2>(hexes.Count) { unit.Position };

            float seconds = 0f;

            for (int i = 1; i < hexes.Count; i++)
            {
                float going = board.GoingOn(battle, hexes[i], unit.Def.Movement);
                float leg = step / (pace * MathF.Max(0.01f, going));

                // The first step is taken whatever it costs; see the remarks.
                if (i > 1 && seconds + leg > turn) break;

                seconds += leg;

                walked.Add(hexes[i]);
                waypoints.Add(board.CentreOf(hexes[i]));
            }

            hexes = walked;

            float metres = 0f;

            for (int i = 1; i < waypoints.Count; i++)
                metres += Vec2.Distance(waypoints[i - 1], waypoints[i]);

            // Effective distance is the route's cost expressed as open ground -
            // the seconds it was priced at times the pace they were priced at.
            // Kept in the same currency as every other planner, so a board route
            // and a continuous one can be put side by side.
            float effective = seconds * pace;

            var hold = new Facing?[waypoints.Count];

            // Only the arrival front is dictated. Each leg runs along one of the
            // six bearings already, so the steering arrives on a hex direction
            // without being told to; the last leg is the one where the player
            // may have asked for something other than the way of travel.
            hold[hold.Length - 1] = Board.Snap(
                arriveOn ?? Facing.Towards(waypoints[waypoints.Count - 2], waypoints[waypoints.Count - 1]));

            return new Plan(
                PathResult.Success(waypoints, hexes, metres, effective, looked),
                hold,
                pressedThrough: false);
        }

        private static Plan NoRoute(PathFailure why, string detail, int looked = 0) =>
            new Plan(PathResult.Failed(why, detail, looked), hold: null, pressedThrough: false);
    }
}
