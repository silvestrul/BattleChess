using System;
using System.Collections.Generic;
using BattleChess.Contracts;

namespace BattleChess.Rules.Grid
{
    /// <summary>
    /// One turn of the board game: everybody moves at once, and where two
    /// regiments want the same ground it is settled here [M155].
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Simultaneous, with clashes resolved, chosen by the designer over
    /// reserve-then-move.</b> The alternative was to refuse a conflicting order
    /// when it was drawn, which cannot produce a clash at all but means the game
    /// tells you no before you have seen why. This way both regiments set off and
    /// the ground decides. The cost is real and is stated in the log: an order
    /// you committed to can fail after the fact.
    /// </para>
    /// <para>
    /// <b>Which is why the rule is fixed rather than fair.</b> A resolution that
    /// depended on iteration order, or on a coin, would make the same turn play
    /// out differently twice and there would be nothing to debug from a
    /// recording. So: the regiment with further still to go takes the ground,
    /// because that is the one for whom stopping costs most, and an exact tie
    /// goes to the lower id. Both are arbitrary; neither is random.
    /// </para>
    /// <para>
    /// <b>Stepping, not walking.</b> The continuous game hands a route to
    /// <c>MovementSystem</c> and lets it walk metres per tick. On the board a
    /// route is a list of cell centres and a turn buys a whole number of them
    /// (<see cref="GridMode.CellsPerTurn"/>), so a regiment is never between two
    /// cells when the turn ends - which is the designer's requirement that a
    /// unit not stop midway, and it is also what makes the occupancy check above
    /// exhaustive rather than a sample.
    /// </para>
    /// </remarks>
    public static class BoardTurn
    {
        /// <summary>What one turn of the board game did.</summary>
        public readonly struct Summary
        {
            /// <summary>Regiments that took at least one step.</summary>
            public readonly int Moved;

            /// <summary>Steps taken in all, over every regiment.</summary>
            public readonly int Steps;

            /// <summary>Times a regiment was stopped by ground somebody else took.</summary>
            public readonly int Clashes;

            /// <summary>Regiments that finished their route this turn.</summary>
            public readonly int Arrived;

            public Summary(int moved, int steps, int clashes, int arrived)
            {
                Moved = moved;
                Steps = steps;
                Clashes = clashes;
                Arrived = arrived;
            }

            public override string ToString() =>
                $"{Moved} moved, {Steps} steps, {Clashes} clashes, {Arrived} arrived";
        }

        /// <summary>Everything one regiment is carrying into the resolution.</summary>
        private sealed class Marcher
        {
            public UnitInstance Unit = null!;
            public int Left;                 // steps still owed this turn
            public List<Coord> Cells = null!; // ground it currently claims
            public bool Stepped;
        }

        /// <summary>Plays one turn: everybody advances, clashes are settled.</summary>
        public static Summary Resolve(BattleState battle, IBattleLog? log = null)
        {
            if (battle == null) throw new ArgumentNullException(nameof(battle));

            Board board = Board.For(battle);

            var marchers = new List<Marcher>();
            var claim = new Dictionary<Coord, UnitInstance>();

            foreach (UnitInstance unit in battle.UnitsOnField())
            {
                var marcher = new Marcher
                {
                    Unit = unit,
                    Left = unit.Route == null || unit.Route.IsComplete ? 0 : GridMode.CellsPerTurn(unit),
                    Cells = Occupancy.Under(board.Cells, unit)
                };

                marchers.Add(marcher);

                // Everybody holds their ground to begin with, movers included.
                // A regiment that has not stepped yet is still standing where it
                // stands, and the regiment behind it may not walk through.
                foreach (Coord cell in marcher.Cells) claim[cell] = unit;
            }

            // Furthest still to go goes first, so that a clash is decided the
            // same way whichever order the field happens to be stored in.
            marchers.Sort((a, b) =>
            {
                int byLeft = b.Left.CompareTo(a.Left);

                return byLeft != 0 ? byLeft : a.Unit.Id.Value.CompareTo(b.Unit.Id.Value);
            });

            int clashes = 0;
            int steps = 0;
            int arrived = 0;

            // Sub-steps rather than one regiment at a time: within a sub-step
            // everybody moves one cell, so a column follows the regiment in front
            // of it into ground that regiment has just left, which is the whole
            // reason simultaneous movement is worth the trouble.
            bool anyLeft = true;

            while (anyLeft)
            {
                anyLeft = false;

                foreach (Marcher marcher in marchers)
                {
                    if (marcher.Left <= 0) continue;

                    MovementRoute? route = marcher.Unit.Route;

                    if (route == null || route.IsComplete)
                    {
                        marcher.Left = 0;
                        continue;
                    }

                    anyLeft = true;

                    Vec2 to = route.Target;

                    Coord next = board.Of(to);
                    Facing front = board.Snap(Facing.Towards(marcher.Unit.Position, to));

                    List<Coord> wants =
                        Occupancy.UnderIfItStood(board.Cells, marcher.Unit, board.CentreOf(next), front);

                    UnitInstance? blocker = WhoIsInTheWay(claim, wants, marcher.Unit);

                    if (blocker != null)
                    {
                        clashes++;
                        marcher.Left = 0;   // it has been stopped; it does not shove

                        route.HeldItsHandBecause =
                            $"{blocker.Def.DisplayName} is standing on the ground it was stepping onto";

                        log.Decision(
                            "board",
                            $"held at {board.Of(marcher.Unit.Position)}: {blocker.Def.DisplayName} " +
                            $"holds {next}",
                            marcher.Unit.Id);

                        continue;
                    }

                    // Take the ground: drop what it held, claim what it now holds.
                    foreach (Coord cell in marcher.Cells) claim.Remove(cell);

                    marcher.Unit.Position = board.CentreOf(next);
                    marcher.Unit.SettleFrontOn(front);
                    marcher.Cells = wants;

                    foreach (Coord cell in wants) claim[cell] = marcher.Unit;

                    route.Advance();

                    marcher.Left--;
                    marcher.Stepped = true;
                    steps++;

                    if (route.IsComplete)
                    {
                        arrived++;
                        marcher.Left = 0;
                    }
                }
            }

            int moved = 0;

            foreach (Marcher marcher in marchers) if (marcher.Stepped) moved++;

            return new Summary(moved, steps, clashes, arrived);
        }

        /// <summary>
        /// Who, if anybody, is standing on ground this regiment wants.
        /// </summary>
        private static UnitInstance? WhoIsInTheWay(
            IReadOnlyDictionary<Coord, UnitInstance> claim, List<Coord> wants, UnitInstance mover)
        {
            foreach (Coord cell in wants)
                if (claim.TryGetValue(cell, out UnitInstance? who) && !ReferenceEquals(who, mover))
                    return who;

            return null;
        }
    }
}
