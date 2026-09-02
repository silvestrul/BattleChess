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
        /// Three rings is three regiment-widths of ground in every direction,
        /// since a cell is sized to a regiment. Further than that and the
        /// regiment is no longer going where it was sent, so the order is better
        /// refused than silently rewritten.
        /// </remarks>
        public const int WillSettleWithinRings = 24;

        /// <summary>A ceiling on the search, so a hopeless order cannot hang a turn.</summary>
        /// <remarks>
        /// The Great Field is a few hundred cells at the size its regiments
        /// ask for, so this exhausts any board this game has and then some. It
        /// is a guard against a bug, not a budget: exhausting the whole board is
        /// cheap, and it is the worst this can ever do.
        /// </remarks>
        public const int MostHexesSearched = 120_000;

        /// <summary>The planner that actually finds the way.</summary>
        /// <remarks>
        /// <b>[M159].</b> Settable so the board can be measured against any of
        /// them, but never itself - that would recurse. It defaults to whatever
        /// the continuous game settled on, so the board inherits every
        /// improvement made to routing rather than forking it.
        /// </remarks>
        public IRoutePlanner Beneath { get; set; } = RoutePlanners.Default;

        public string Name => $"over the board, {Beneath.Name} beneath";

        public Plan PlanTo(
            BattleState battle, UnitInstance unit, IPathfinder pathfinder, Vec2 destination,
            IBattleLog? log = null, IWayRound? wayRound = null, Facing? arriveOn = null)
        {
            if (battle == null) throw new ArgumentNullException(nameof(battle));
            if (unit == null) throw new ArgumentNullException(nameof(unit));

            Board board = Board.For(battle);
            MovementType moving = unit.Def.Movement;

            // What is in the way, and what holds ground, are two different
            // questions on a board [M153].
            //
            // The mover's own wing is not in the way of it [M152]: a bond sets
            // off as one body, so a wing-mate's cell is one it is leaving, and
            // treating it as held is what made a line come apart as it advanced.
            Dictionary<Coord, UnitId> blockingTheWay = board.WhoIsWhere(battle, unit);

            // Where a march may FINISH is a different matter, and getting it
            // wrong is what broke group attacks. A wing that is marching has
            // already been handed a cell apiece by Board.FormUpAt, so leaving
            // its mates out costs nothing - their goals are distinct by
            // construction. A wing that is ATTACKING has been handed nothing:
            // every regiment works out its own aim point from its own bearing to
            // the quarry, and re-works it every time the chase re-plans. Leave
            // the mates out there and they all resolve to the same cell in front
            // of the target and fight each other for it.
            Dictionary<Coord, UnitId> holdingGround =
                HasAPlaceOfItsOwn(unit) ? blockingTheWay : board.WhoIsWhere(battle);

            // [M159] And ground that somebody already marching has spoken for.
            //
            // Standing bodies are only half the claim on a field. A regiment
            // under orders has also claimed where it is GOING, and two marches
            // resolved against standing bodies alone will happily pick the same
            // destination and then fight over it on arrival. Settling that here,
            // when the order is drawn, is worth more than any amount of clash
            // resolution afterwards - it is the difference between an order that
            // is refused a place and an order that is given one twice.
            foreach (KeyValuePair<Coord, UnitId> booked in board.SpokenFor(battle, unit))
                if (!holdingGround.ContainsKey(booked.Key))
                    holdingGround[booked.Key] = booked.Value;

            Coord from = board.Of(unit.Position);
            Coord wanted = board.Of(destination);

            if (!board.OnBoard(wanted))
                return NoRoute(PathFailure.GoalOffMap, $"{wanted} is off the board.");

            // The cell asked for, or the nearest one this regiment could stand
            // on. Its own cell never counts as taken, so an order that resolves
            // to where it already is comes back as a route of no legs rather
            // than as a failure. For an attack this is also what spreads a wing
            // around its quarry: each regiment in turn is pushed a ring further
            // out, which is a line forming up against an enemy rather than a
            // queue behind one cell.
            if (!board.NearestFree(battle, unit, wanted, holdingGround, WillSettleWithinRings, out Coord goal))
                return NoRoute(
                    PathFailure.GoalTooTight,
                    $"nothing within {WillSettleWithinRings} cells of {wanted} is free ground for " +
                    $"{unit.Def.DisplayName}.");

            if (board.GoingOn(battle, from, moving) <= 0f && from != goal)
                return NoRoute(PathFailure.GoalImpassable, $"{unit.Def.DisplayName} is standing on ground it cannot leave.");

            if (from == goal)
                return new Plan(
                    PathResult.Success(new[] { unit.Position }, new[] { from }, 0f, 0f, 1),
                    hold: null,
                    pressedThrough: false);

            // [M159] The route itself is not this planner's business.
            //
            // It used to be: an A* from cell to cell over the whole board. That
            // is the wrong algorithm and it was measured as such - 20 orders on
            // the Great Field cost 351 ms over the board against 17 ms through
            // the continuous planner, because the board search explored 68 880
            // cells where the continuous one explored 915. A grid search asks
            // every cell of the field about ground; the planner this project
            // spent five milestones on asks the handful of bodies actually in
            // the way. At 12,5 m it was 1,3 seconds against 18 ms.
            //
            // So the board keeps the one thing it was ever for - WHERE A
            // REGIMENT MAY STAND, resolved above - and hands the question of how
            // to get there back to the algorithm that already answers it well.
            // The walk is squared onto cells afterwards by BoardTurn.
            return Beneath.PlanTo(battle, unit, pathfinder, board.CentreOf(goal), log, wayRound, arriveOn);
        }

        /// <summary>A* from hex to hex, over free ground only.</summary>
        /// <summary>Which way a regiment faces while taking step <paramref name="which"/>.</summary>
        /// <remarks>
        /// A lattice's steps are evenly spaced round the circle - 45 degrees on
        /// squares, 60 on hexes - so the step index is the bearing. Every one of
        /// them is also a legal front under the 24-front rule [M155], because 24
        /// is a multiple of both 8 and 6.
        /// </remarks>
        private static bool HasAPlaceOfItsOwn(UnitInstance unit) =>
            unit.Order.Kind == OrderKind.Move || unit.Station.HasValue;

        private static Plan NoRoute(PathFailure why, string detail, int looked = 0) =>
            new Plan(PathResult.Failed(why, detail, looked), hold: null, pressedThrough: false);
    }
}
