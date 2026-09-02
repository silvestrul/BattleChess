using System;
using System.Runtime.CompilerServices;
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
    /// onto a cell. That is the entire mode. Combat, morale, vision, contact, the
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
        /// <summary>How far a mustering regiment will be shuffled to find a free cell.</summary>
        /// <remarks>
        /// Six rings is six regiment-widths of ground, which is enough to unpack
        /// a deployment line where several regiments share a cell and not enough
        /// to move one to a different part of the field. Beyond it the regiment
        /// is left where it stands, sharing a cell, and <see cref="Muster"/> says
        /// so in its count rather than pretending it succeeded.
        /// </remarks>
        public const int ShufflesWithinRings = 24;

        /// <summary>Which shape of cell the board is made of.</summary>
        /// <remarks>
        /// <para>
        /// <b>Squares [M151], on the designer's call after playing the hex
        /// board.</b> A rectangle's frontage runs perpendicular to its facing,
        /// and only on a square lattice is the perpendicular of an axis another
        /// axis - so only there can a regiment march straight ahead <i>and</i>
        /// stand shoulder to shoulder with its neighbours. On a hex board one of
        /// those has to be given up; [M150] gave up straight marching, which
        /// worked and left every advance weaving.
        /// </para>
        /// <para>
        /// Read when a board is first built for a battle, so it must be set
        /// before the battle starts. Changing it afterwards does nothing to a
        /// board that already exists, which is deliberate: a board that changed
        /// shape underneath a battle would move every regiment on it.
        /// </para>
        /// </remarks>
        public static LatticeShape Shape { get; set; } = LatticeShape.Square;

        /// <summary>How many metres a cell is across, flat to flat.</summary>
        /// <remarks>
        /// <para>
        /// <b>[M155]. A setting, and no longer a derivation.</b> It was worked
        /// out from the widest regiment on the field, because one cell had to
        /// hold one regiment however it was turned - which for an 80 x 40 m body
        /// means 90 m, and gave the Great Field a 20 x 26 board. A regiment now
        /// covers several cells, so the cell answers to the ground instead of to
        /// the regiment.
        /// </para>
        /// <para>
        /// <b>Twenty-five metres, because that is what the maps are drawn at.</b>
        /// The Great Field is authored as 72 x 96 terrain cells of 25 m. Matching
        /// it exactly means the going under a cell is one terrain cell's going
        /// rather than a sample of several, and it means there is no third
        /// coordinate system in the codebase to keep in step with the other two.
        /// </para>
        /// <para>
        /// 12,5 m is the other size to measure, and it is deliberately a half
        /// rather than some other fraction: halving keeps every cell boundary on
        /// a terrain boundary, so the two are comparable and the finer one is not
        /// quietly also testing a misalignment.
        /// </para>
        /// </remarks>
        public static float CellMetres { get; set; } = 25f;

        /// <summary>How many fronts a regiment may hold, evenly round the circle.</summary>
        /// <remarks>
        /// <para>
        /// <b>[M155]. Twenty-four, which is a front every 15 degrees</b>, as
        /// asked for. It is deliberately not the lattice's own count any more:
        /// the number of directions a cell has neighbours in decided how a
        /// regiment could face only while a regiment had to fit inside one cell.
        /// A body that covers a set of cells can be turned to any angle the set
        /// can be computed for.
        /// </para>
        /// <para>
        /// 24 is chosen over 12 or 8 because it is a superset of both lattices'
        /// own facings - 45 is three steps of 15, and the hex's 30-degree-offset
        /// 60s are two steps - so nothing that was a legal front before has
        /// stopped being one, and the M150 and M152 recordings still mean what
        /// they meant.
        /// </para>
        /// </remarks>
        public static int FacingCount { get; set; } = 24;

        /// <summary>
        /// Whether movement on the board is taken in whole cells at the end of a
        /// turn rather than walked metre by metre through the ticks.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>[M155]. On, and it is what makes the board game turn-based rather
        /// than a continuous game played on squared paper.</b> The designer's
        /// requirement was that a regiment cannot stop midway, and a walker that
        /// covers 143 m in a 90 s turn stops wherever 143 m happens to land. A
        /// stepped regiment ends every turn on a cell centre because a step is
        /// the only thing it can take.
        /// </para>
        /// <para>
        /// Movement only. Fighting, morale, sighting and the clock all still run
        /// through the ticks exactly as they do in the free game - the board was
        /// always meant to constrain <i>where a regiment may stand</i> and
        /// nothing else, and a second combat model would be a second thing that
        /// can disagree with the first.
        /// </para>
        /// </remarks>
        public static bool StepsOverCells { get; set; } = true;

        /// <summary>
        /// How many cells a regiment of this kind covers in one turn of the
        /// board game.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>[M155], and it is derived rather than tabled.</b> The designer
        /// guessed "swordsman advances 2, cavalry 6, artillery 1", and that guess
        /// turns out to be a <b>30-second turn on a 25 m grid</b> almost exactly:
        /// artillery 1,3 m/s is 1,6 cells, foot 1,59 is 1,9, cavalry 4,76 is 5,7.
        /// So the turn length was set to the designer's numbers rather than the
        /// numbers being written down beside a turn length that disagreed with
        /// them - which is the M148 mistake, and it is not being made twice.
        /// </para>
        /// <para>
        /// Rounded, with a floor of one: a regiment that cannot move at all in a
        /// turn is not slow, it is broken, and the going underfoot already has a
        /// multiplier for being slow.
        /// </para>
        /// </remarks>
        public static int CellsPerTurn(UnitInstance unit)
        {
            if (unit == null) throw new ArgumentNullException(nameof(unit));

            float metres = unit.Def.Speed * TurnSeconds;

            return Math.Max(1, (int)MathF.Round(metres / CellMetres));
        }

        /// <summary>How many battle seconds one board turn lasts.</summary>
        /// <remarks>
        /// <para>
        /// <b>Reported from play: "the units move too little".</b> A turn buys a
        /// regiment its pace times this, so at the continuous game's sixty
        /// seconds a line of foot walks 95 m - barely a cell - and the board
        /// reads as glue.
        /// </para>
        /// <para>
        /// <b>Ninety.</b> Seconds times pace is metres whatever a cell is worth,
        /// so this buys foot 143 m, artillery 117, cavalry 428 and scouts 495 -
        /// which on the Great Field's 90 m cells is 1,6 cells for foot and 5,5
        /// for scouts, against a short side of 20. The slow end is a move worth
        /// making and the fast end is a quarter of the board.
        /// </para>
        /// <para>
        /// <b>Here rather than in the harness toggles, and that is a correction.</b>
        /// It was written as a view option first, which left the rules measuring
        /// a sixty-second turn while the game ran a hundred-and-twenty-second
        /// one - so the test that prints what a turn buys printed a number
        /// nobody was playing. How long a turn lasts is a rule of the board
        /// game. It stays settable, because it is a feel question and feel
        /// questions are settled by playing.
        /// </para>
        /// <para>
        /// It does not touch <see cref="BattleClock"/>, so the continuous game
        /// keeps its own sixty and every test counting on them stays correct.
        /// </para>
        /// </remarks>
        public static float TurnSeconds { get; set; } = 30f;

        /// <summary>The same, in ticks, which is what the clock counts in.</summary>
        public static int TicksPerTurn =>
            Math.Max(1, (int)MathF.Round(TurnSeconds / BattleClock.SecondsPerTick));

        /// <summary>Whether the board game is the one being played.</summary>
        public static bool On { get; private set; }

        /// <summary>What a march is planned with while the board is off.</summary>
        private static IRoutePlanner? _wasPlanning;

        /// <summary>
        /// Puts a battle on the board: musters everybody onto a cell and makes
        /// the board planner the one that draws routes.
        /// </summary>
        /// <returns>How many regiments could not be given a cell of their own.</returns>
        public static int TurnOn(BattleState battle)
        {
            if (battle == null) throw new ArgumentNullException(nameof(battle));

            if (!On)
            {
                _wasPlanning = RoutePlanners.InUse;
                RoutePlanners.InUse = new BoardRoutePlanner();
                On = true;
            }

            // [M156] And remembered against THIS battle, not only as a flag on
            // the process. See IsBoard.
            Boarded.Remove(battle);
            Boarded.Add(battle, Yes);

            return Muster(battle);
        }

        /// <summary>Gives the continuous game back.</summary>
        /// <remarks>
        /// Regiments are left standing on their cells rather than being put back
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

        private static readonly ConditionalWeakTable<BattleState, object> Boarded =
            new ConditionalWeakTable<BattleState, object>();

        private static readonly object Yes = new object();

        /// <summary>Whether this particular battle is being played on a board.</summary>
        /// <remarks>
        /// <para>
        /// <b>[M156], and it is a bug I wrote and the suite caught.</b> [M155]
        /// had <c>MovementSystem.Step</c> stand aside while <see cref="On"/> was
        /// set - a flag on the process. The suite runs test classes in parallel,
        /// so the moment one test mustered a board, <b>every other battle running
        /// beside it stopped walking</b>: seventeen tests failed across marching,
        /// charging, closing, stopping short and collision recording, none of
        /// which has anything to do with the board.
        /// </para>
        /// <para>
        /// This is the second time a process-wide switch has done this - the
        /// first was <c>RoutePlanners.InUse</c> in [M147], and there is a skipped
        /// test in BoardTests kept as the record of it. The lesson taken this
        /// time is the stronger one: <b>which game a battle is playing is a fact
        /// about the battle</b>, so it is stored against the battle. The flag
        /// stays only for the planner swap, which has nowhere better to live yet.
        /// </para>
        /// </remarks>
        public static bool IsBoard(BattleState battle) =>
            battle != null && Boarded.TryGetValue(battle, out _);

        /// <summary>
        /// Stands every regiment on the centre of a cell of its own, on one of
        /// the facings the board allows.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Deployments are authored in metres for the continuous game, so
        /// several regiments in a line can land in the same cell. They are
        /// shuffled outward one ring at a time, in the order the battle holds
        /// them, which keeps a deployment line recognisably a line: the first of
        /// a pair keeps the cell it wanted and the second steps aside.
        /// </para>
        /// <para>
        /// <b>It reports what it could not do.</b> A regiment with no free cell
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
                // [M155] The front is settled BEFORE the place is looked for,
                // because which cells a body covers depends on which way it is
                // turned. Snapping the facing afterwards - which is what this did
                // while a cell held a whole regiment however it was turned - can
                // rotate a body straight into the neighbour whose ground was just
                // checked as clear.
                Facing front = board.Snap(unit.Facing);

                Coord wanted = board.Of(unit.Position);

                if (!board.NearestFree(battle, unit, wanted, taken, ShufflesWithinRings, front, out Coord cell))
                    crowded++;

                unit.Position = board.CentreOf(cell);
                unit.SettleFrontOn(front);

                foreach (Coord under in Occupancy.Under(board.Cells, unit))
                    taken[under] = unit.Id;
            }

            return crowded;
        }

        /// <summary>
        /// Stands every regiment that has finished marching back on the centre
        /// of a cell of its own. Regiments still on the road are left alone.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>What the board actually promises, stated exactly.</b> Not "every
        /// regiment is on a hex at every instant" - that was the first draft of
        /// [M147], and enforcing it meant truncating every route to one turn's
        /// walking, which made a foot march a 50 m stub and had to be ordered
        /// again every turn. The promise is <b>a regiment that has stopped
        /// stands on a hex of its own</b>. One in the middle of a march is
        /// between hexes, which is what marching looks like.
        /// </para>
        /// <para>
        /// That is enough to keep the mode's real guarantee, because everything
        /// that reads occupancy - the planner above all - is asking about
        /// regiments that are standing somewhere, and the ones that are not are
        /// on their way through rather than settled in.
        /// </para>
        /// <para>
        /// <b>And it is what stops the drift.</b> A regiment walks continuously
        /// and so finishes a leg within a metre or two of a centre rather than
        /// on it; without this the error accumulates over a dozen turns until
        /// regiments stand visibly off the board they are playing on. Snapping
        /// the front at the same moment is what makes the lattice's facings
        /// hold: the steering turns freely on the way, and arriving is when it
        /// settles.
        /// </para>
        /// <para>
        /// <b>The front settled is the ordered front too [M152]</b>, through
        /// <c>UnitInstance.SettleFrontOn</c>. Setting only the facing left the
        /// regiment turning back toward the free bearing its order carried, and
        /// being snapped again at the end of the next turn - a few degrees back
        /// and forth for ever, worst on cavalry because cavalry turns fastest.
        /// </para>
        /// </remarks>
        public static void SettleThoseWhoHaveStopped(BattleState battle)
        {
            if (battle == null) throw new ArgumentNullException(nameof(battle));

            Board board = Board.For(battle);

            // Regiments still marching are not standing anywhere, but they are
            // still somewhere, so the hexes they are crossing are spoken for as
            // far as anybody settling is concerned.
            var taken = new Dictionary<Coord, UnitId>();

            foreach (UnitInstance unit in battle.UnitsOnField())
            {
                if (!HasStopped(unit)) taken[board.Of(unit.Position)] = unit.Id;
            }

            foreach (UnitInstance unit in battle.UnitsOnField())
            {
                if (!HasStopped(unit)) continue;

                if (!board.NearestFree(
                        battle, unit, board.Of(unit.Position), taken, ShufflesWithinRings, out Coord hex))
                    continue;

                taken[hex] = unit.Id;

                unit.Position = board.CentreOf(hex);
                unit.SettleFrontOn(board.Snap(unit.Facing));
            }
        }

        private static bool HasStopped(UnitInstance unit) =>
            unit.Route == null || unit.Route.IsComplete;

        /// <summary>
        /// Puts one regiment back on the centre of the cell it has stopped in.
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
            unit.SettleFrontOn(board.Snap(unit.Facing));
        }
    }
}
