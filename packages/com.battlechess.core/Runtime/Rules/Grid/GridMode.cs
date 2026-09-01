using System;
using System.Collections.Generic;
using BattleChess.Contracts;

namespace BattleChess.Rules.Grid
{
    /// <summary>
    /// The board game, switched on and off.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>[M147], and it is a mode behind the seams rather than a fork.</b> The
    /// designer's instruction was that the grid idea "might be a different game,
    /// so it should be a parallel branch and each change should be applied to
    /// both". A second branch is two codebases and a merge; a mode is one
    /// codebase where every fix lands on both games because there is only one
    /// set of rules. What separates the two games is <i>which planner draws a
    /// route</i> and <i>where a regiment is allowed to stand</i>, and both of
    /// those are already single points.
    /// </para>
    /// <para>
    /// <b>What turning it on does, in full.</b> Two things. The route planner
    /// becomes <see cref="BoardRoutePlanner"/>, and every regiment is mustered
    /// onto a hex. That is the entire mode. Combat, morale, vision, contact, the
    /// clock, plan-then-fire, the drawn lines and every test in the suite are
    /// untouched and stay correct, because none of them ever asked where a
    /// regiment may stand - they ask where it <i>is</i>, and it is still a
    /// <see cref="Vec2"/> with a <see cref="Facing"/>.
    /// </para>
    /// <para>
    /// <b>Why this is not a flag the rules consult.</b> There is exactly one
    /// boolean here and nothing inside the simulation reads it; it swaps an
    /// implementation, in the same way <c>DebugOptions.IgnoreTerrain</c> does.
    /// A rule that had to ask "are we on a board?" would be a rule with two
    /// behaviours to keep right, and this codebase has enough of those.
    /// </para>
    /// </remarks>
    public static class GridMode
    {
        /// <summary>How far a mustering regiment will be shuffled to find a free hex.</summary>
        /// <remarks>
        /// Six rings is 300 m, which is enough to unpack a deployment line where
        /// several regiments share a hex and not enough to move one to a
        /// different part of the field. Beyond it the regiment is left where it
        /// stands, sharing a hex, and <see cref="Muster"/> says so in its count
        /// rather than pretending it succeeded.
        /// </remarks>
        public const int ShufflesWithinRings = 6;

        /// <summary>Whether the board game is the one being played.</summary>
        public static bool On { get; private set; }

        /// <summary>What a march is planned with while the board is off.</summary>
        private static IRoutePlanner? _wasPlanning;

        /// <summary>
        /// Puts a battle on the board: musters everybody onto a hex and makes
        /// the board planner the one that draws routes.
        /// </summary>
        /// <returns>How many regiments could not be given a hex of their own.</returns>
        public static int TurnOn(BattleState battle)
        {
            if (battle == null) throw new ArgumentNullException(nameof(battle));

            if (!On)
            {
                _wasPlanning = RoutePlanners.InUse;
                RoutePlanners.InUse = new BoardRoutePlanner();
                On = true;
            }

            return Muster(battle);
        }

        /// <summary>Gives the continuous game back.</summary>
        /// <remarks>
        /// Regiments are left standing on their hexes rather than being put back
        /// where they were. Where they were is gone - a mustered regiment moved,
        /// and the continuous game is perfectly happy with a tidy deployment.
        /// </remarks>
        public static void TurnOff()
        {
            if (!On) return;

            RoutePlanners.InUse = _wasPlanning ?? RoutePlanners.Default;
            _wasPlanning = null;
            On = false;
        }

        /// <summary>
        /// Stands every regiment on the centre of a hex of its own, facing one
        /// of the six ways.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Deployments are authored in metres for the continuous game, so
        /// several regiments in a line can land in the same hex - a fifty-metre
        /// hex against regiments authored forty metres apart. They are shuffled
        /// outward one ring at a time, in the order the battle holds them, which
        /// keeps a deployment line recognisably a line: the first of a pair
        /// keeps the hex it wanted and the second steps aside.
        /// </para>
        /// <para>
        /// <b>It reports what it could not do.</b> A regiment with no free hex
        /// within <see cref="ShufflesWithinRings"/> is left where it stands and
        /// counted, because a muster that quietly leaves two bodies in one hex
        /// has broken the board's only promise, and a silent count of nought
        /// would be the most misleading thing this could return.
        /// </para>
        /// </remarks>
        public static int Muster(BattleState battle)
        {
            if (battle == null) throw new ArgumentNullException(nameof(battle));

            Board board = Board.For(battle);

            var taken = new Dictionary<Coord, UnitId>();
            int crowded = 0;

            foreach (UnitInstance unit in battle.UnitsOnField())
            {
                Coord wanted = board.Of(unit.Position);

                if (!board.NearestFree(battle, unit, wanted, taken, ShufflesWithinRings, out Coord hex))
                    crowded++;

                taken[hex] = unit.Id;

                unit.Position = board.CentreOf(hex);
                unit.Facing = Board.Snap(unit.Facing);
            }

            return crowded;
        }

        /// <summary>
        /// Puts one regiment back on the centre of the hex it has stopped in.
        /// </summary>
        /// <remarks>
        /// Called when a march ends. A regiment walks continuously between hexes
        /// and so finishes a leg within a metre or two of a centre rather than
        /// on it; without this the drift accumulates over a dozen turns until a
        /// regiment is standing visibly off its hex. Snapping the front at the
        /// same moment is what makes the six bearings hold: the steering turns
        /// freely on the way, and arriving is when it settles.
        /// </remarks>
        public static void Settle(BattleState battle, UnitInstance unit)
        {
            if (battle == null) throw new ArgumentNullException(nameof(battle));
            if (unit == null) throw new ArgumentNullException(nameof(unit));

            Board board = Board.For(battle);

            unit.Position = board.CentreOf(board.Of(unit.Position));
            unit.Facing = Board.Snap(unit.Facing);
        }
    }
}
