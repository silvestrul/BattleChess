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

        /// <summary>Plays a whole turn, tick by tick.</summary>
        /// <remarks>
        /// A loop over <see cref="Tick"/> rather than a second way of moving, so
        /// that what a test measures is what the game does.
        /// </remarks>
        public static Summary Resolve(BattleState battle, IBattleLog? log = null)
        {
            int moved = 0, steps = 0, clashes = 0, arrived = 0;

            var stepped = new HashSet<UnitId>();

            for (int i = 0; i < GridMode.TicksPerTurn; i++)
            {
                Summary one = Tick(battle, log, stepped);

                steps += one.Steps;
                clashes += one.Clashes;
                arrived += one.Arrived;
            }

            moved = stepped.Count;

            return new Summary(moved, steps, clashes, arrived);
        }

        /// <summary>
        /// One tick of the board game: every regiment that has earned a step
        /// takes one, and clashes over ground are settled.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>[M157], and the first draft did this all at once at the end of the
        /// turn.</b> That was correct and looked broken: a regiment jumped its
        /// whole allowance in the instant the turn ended, after the last tick the
        /// view had snapshotted, so the picture stayed a turn behind the battle
        /// while clicking and the nameplate read the battle. Reported as units
        /// not being where their names were.
        /// </para>
        /// <para>
        /// So a turn is spent rather than applied. A regiment earns
        /// <see cref="GridMode.CellsPerTurn"/> over
        /// <see cref="GridMode.TicksPerTurn"/> ticks and takes a whole cell
        /// whenever it has earned one - cavalry every fifth tick, foot every
        /// fifteenth. It still ends every turn on a cell centre, because a step
        /// is still the only thing it can take, and it is now visibly marching
        /// between them.
        /// </para>
        /// </remarks>
        public static Summary Tick(
            BattleState battle, IBattleLog? log = null, HashSet<UnitId>? stepped = null)
        {
            if (battle == null) throw new ArgumentNullException(nameof(battle));

            Board board = Board.For(battle);

            var marchers = new List<Marcher>();
            var claim = new Dictionary<Coord, UnitInstance>();

            foreach (UnitInstance unit in battle.UnitsOnField())
            {
                bool marching = unit.Route != null && !unit.Route.IsComplete;

                if (marching)
                    unit.BoardStepCredit +=
                        GridMode.CellsPerTurn(unit) / (float)Math.Max(1, GridMode.TicksPerTurn);
                else
                    unit.BoardStepCredit = 0f;

                var marcher = new Marcher
                {
                    Unit = unit,
                    Left = marching ? (int)unit.BoardStepCredit : 0,
                    Cells = board.CellsUnder(unit)
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

                    // [M159] The route is a POLYLINE now, not a list of cells.
                    //
                    // The board no longer searches: a route comes from the
                    // continuous planner, which is twenty to seventy times
                    // cheaper because it reasons about the bodies in the way
                    // rather than about every cell of the field. What is left
                    // here is squaring that walk onto cells - one whole cell at
                    // a time, toward the next waypoint, round whatever is
                    // standing in the immediate way.
                    //
                    // The division of labour is the point. The route does the
                    // GLOBAL work: it is already drawn round the terrain and the
                    // bodies that were there when it was planned. This does the
                    // LOCAL work: eight candidates, take the one that closes most
                    // on the waypoint and that the body fits in. Getting round a
                    // regiment that has since moved into the way costs eight
                    // checks rather than another search.
                    Vec2 aim = route.Target;

                    Coord here = board.Of(marcher.Unit.Position);

                    // Close enough to this waypoint: take the next one.
                    if (Vec2.Distance(marcher.Unit.Position, aim) <= board.CellWidth * 0.5f)
                    {
                        route.Advance();

                        if (route.IsComplete)
                        {
                            arrived++;
                            marcher.Left = 0;
                        }

                        continue;
                    }

                    if (!BestStep(
                            battle, board, marcher, claim, here, aim,
                            out Coord next, out Facing front, out List<Coord> wants,
                            out UnitInstance? blocker))
                    {
                        clashes++;
                        marcher.Left = 0;   // stopped this tick; it does not shove

                        // Its credit is kept, so it tries again next tick rather
                        // than losing the rest of the turn to one blocked step.
                        string because = blocker != null
                            ? $"{blocker.Def.DisplayName} is standing on the ground it was stepping onto"
                            : "there is nowhere nearer it can put itself";

                        // Said once, when it starts being held, and not once a
                        // tick for as long as it stays held. A regiment blocked
                        // for a whole turn is thirty ticks, and forty regiments
                        // shuffling round each other wrote hundreds of identical
                        // lines a turn - a recording nobody can read, and a cost
                        // the game pays for the privilege.
                        if (route.HeldItsHandBecause != because)
                        {
                            route.HeldItsHandBecause = because;

                            log.Decision("board", $"held at {here}: {because}", marcher.Unit.Id);
                        }

                        continue;
                    }

                    // Take the ground: drop what it held, claim what it now holds.
                    foreach (Coord cell in marcher.Cells) claim.Remove(cell);

                    marcher.Unit.Position = board.CentreOf(next);
                    marcher.Unit.SettleFrontOn(front);
                    marcher.Cells = wants;

                    foreach (Coord cell in wants) claim[cell] = marcher.Unit;

                    route.HeldItsHandBecause = null;

                    marcher.Left--;
                    marcher.Unit.BoardStepCredit -= 1f;
                    marcher.Stepped = true;
                    stepped?.Add(marcher.Unit.Id);
                    steps++;

                    if (Vec2.Distance(marcher.Unit.Position, aim) <= board.CellWidth * 0.5f)
                        route.Advance();

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
        /// The one cell step that closes most on <paramref name="aim"/> and that
        /// this regiment's whole body fits in.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Eight candidates on a square lattice, six on hexes. A step is taken
        /// only if it gets the regiment nearer than it already is, so a body with
        /// nothing but worse ground around it holds rather than mills. Sideways
        /// steps count as nearer whenever they are, which is what lets a regiment
        /// slide round the corner of something and pick its route up again.
        /// </para>
        /// <para>
        /// <b>It does not search.</b> Getting round something big is the route's
        /// job, and the route was drawn by a planner that is good at it. If this
        /// tried to be clever it would be a second, worse pathfinder disagreeing
        /// with the first - which is the fault [M159] exists to undo.
        /// </para>
        /// </remarks>
        private static bool BestStep(
            BattleState battle, Board board, Marcher marcher,
            IReadOnlyDictionary<Coord, UnitInstance> claim, Coord here, Vec2 aim,
            out Coord next, out Facing front, out List<Coord> wants, out UnitInstance? blocker)
        {
            next = here;
            front = marcher.Unit.Facing;
            wants = marcher.Cells;
            blocker = null;

            int ways = board.Cells.DirectionCount;

            Span<Coord> around = stackalloc Coord[8];

            board.Cells.Neighbours(here, around);

            Vec2 standing = board.CentreOf(here);

            float nearest = Vec2.Distance(standing, aim);

            bool found = false;

            for (int i = 0; i < ways; i++)
            {
                Coord candidate = around[i];

                Vec2 centre = board.CentreOf(candidate);

                float closes = Vec2.Distance(centre, aim);

                if (closes >= nearest) continue;

                Facing facing = board.Snap(Facing.Towards(standing, centre));

                if (!Fits(battle, board, marcher, claim, candidate, facing,
                        out List<Coord> under, out UnitInstance? who))
                {
                    if (blocker == null) blocker = who;
                    continue;
                }

                nearest = closes;
                next = candidate;
                front = facing;
                wants = under;
                found = true;
            }

            return found;
        }

        /// <summary>Whether the whole body fits, standing there facing that way.</summary>
        private static bool Fits(
            BattleState battle, Board board, Marcher marcher,
            IReadOnlyDictionary<Coord, UnitInstance> claim, Coord at, Facing front,
            out List<Coord> under, out UnitInstance? blocker)
        {
            Coord[] shape = board.Stencil(marcher.Unit.Footprint, front);

            under = new List<Coord>(shape.Length);
            blocker = null;

            for (int i = 0; i < shape.Length; i++)
            {
                var cell = new Coord(at.Q + shape[i].Q, at.R + shape[i].R);

                if (!board.OnBoard(cell)) return false;

                if (board.GoingOn(battle, cell, marcher.Unit.Def.Movement) <= 0f) return false;

                if (claim.TryGetValue(cell, out UnitInstance who) && !ReferenceEquals(who, marcher.Unit))
                {
                    blocker = who;
                    return false;
                }

                under.Add(cell);
            }

            return true;
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
