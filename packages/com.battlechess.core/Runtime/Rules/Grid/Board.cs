using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using BattleChess.Contracts;

namespace BattleChess.Rules.Grid
{
    /// <summary>
    /// The board of the grid game: one regiment to a cell, and a cell big
    /// enough to hold one however it is turned.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>[M147].</b> The designer's parallel game, taken as a mode rather than
    /// a fork. Nothing here replaces the continuous simulation - a regiment
    /// still walks, still fights on the way, still answers to the same clock.
    /// What the board takes away is the one freedom that has cost this project
    /// five sessions: <b>where a regiment may stand</b>. It may stand on a cell,
    /// and only one may stand on each. Two bodies cannot overlap because there
    /// is nowhere for the overlap to happen.
    /// </para>
    /// <para>
    /// <b>The shape of a cell is <see cref="ILattice"/>'s business [M151].</b>
    /// Squares by default, because a rectangle's frontage runs perpendicular to
    /// its facing and only on a square lattice is the perpendicular of an axis
    /// another axis - so only there can a regiment march straight ahead
    /// <i>and</i> stand shoulder to shoulder with its neighbours. Hexes are kept
    /// beside them for comparison.
    /// </para>
    /// <para>
    /// <b>The cell size is measured off the battle, and [M149] is why it has to
    /// be.</b> It was a constant at first - 50 m, derived from a regiment of 40
    /// by 20 m, which is what <c>UnitDef.FootprintAt(DefaultStrength)</c>
    /// reports. No battle file fields a regiment at that strength. The Great
    /// Field fields them at two thousand worth, which is <b>80 by 40 m and 89,4
    /// m across the diagonal</b> - so every regiment on the board was nearly two
    /// cells wide, they overlapped freely, and the muster cheerfully reported
    /// that everybody had a cell of their own. A cell size derived from a number
    /// nobody plays is not derived from anything.
    /// </para>
    /// <para>
    /// So the cell is the widest body that actually stands on <i>this</i> field,
    /// rounded up. A cell contains a body at any orientation when its inscribed
    /// circle is at least the body's bounding diameter.
    /// </para>
    /// <para>
    /// <b>A board is derived, never stored.</b> Occupancy is read off the units
    /// each time it is asked for rather than kept in step with them, because a
    /// second copy of where everybody is standing is a second thing that can be
    /// wrong, and in this codebase that kind of copy has been wrong before.
    /// Forty regiments is a loop nobody can measure.
    /// </para>
    /// </remarks>
    public sealed class Board
    {
        /// <summary>The smallest cell the board will use, in metres.</summary>
        /// <remarks>
        /// A floor rather than a size. It matters only for a battle with nothing
        /// on the field, or one whose regiments are so small that a cell sized to
        /// them would make the board finer than the terrain is authored at.
        /// </remarks>
        public const float SmallestCellMetres = 45f;

        /// <summary>Metres the cell size is rounded up to.</summary>
        /// <remarks>
        /// Five. Enough to keep the number readable in a log and to leave a
        /// little air between a body and the cell holding it, and small enough
        /// that rounding never costs a meaningful share of the board.
        /// </remarks>
        public const float CellSizeStepMetres = 5f;

        private static readonly ConditionalWeakTable<BattleState, Board> Boards =
            new ConditionalWeakTable<BattleState, Board>();

        /// <summary>The shape of cell this board is made of.</summary>
        public readonly ILattice Cells;

        public readonly MapBounds Bounds;

        /// <summary>Flat-to-flat width of one cell on this board, in metres.</summary>
        public float CellWidth => Cells.CellWidth;

        /// <summary>The widest body the cell was sized to hold, as a diameter.</summary>
        /// <remarks>
        /// Kept so a log can state the derivation rather than the result: "90 m
        /// cells, because the widest regiment here is 89,4 m across" is a
        /// sentence somebody can check.
        /// </remarks>
        public readonly float WidestBody;

        private Board(MapBounds bounds, ILattice cells, float widestBody)
        {
            Bounds = bounds;
            Cells = cells;
            WidestBody = widestBody;
        }

        /// <summary>The board a battle is played on.</summary>
        /// <remarks>
        /// Cached against the battle. The cell size is fixed at the moment the
        /// board is first asked for and does not follow the battle afterwards -
        /// a regiment loses men and narrows as it does, and a board that
        /// re-sized itself mid-battle would move every regiment on it.
        /// </remarks>
        public static Board For(BattleState battle)
        {
            if (battle == null) throw new ArgumentNullException(nameof(battle));

            return Boards.GetValue(battle, Build);
        }

        private static Board Build(BattleState battle)
        {
            float widest = 0f;

            foreach (UnitInstance unit in battle.UnitsOnField())
                widest = MathF.Max(widest, 2f * unit.Footprint.BoundingRadius);

            float cell = CellFor(widest);
            Vec2 origin = battle.Terrain.Bounds.Min;

            ILattice cells = GridMode.Shape == LatticeShape.Hex
                ? new HexLattice(cell, origin)
                : (ILattice)new SquareLattice(cell, origin);

            return new Board(battle.Terrain.Bounds, cells, widest);
        }

        /// <summary>The smallest cell on the size ladder that holds a body this wide.</summary>
        public static float CellFor(float widestBodyMetres)
        {
            float wanted = MathF.Max(SmallestCellMetres, widestBodyMetres);

            return MathF.Ceiling(wanted / CellSizeStepMetres) * CellSizeStepMetres;
        }

        /// <summary>Which cell a world position falls in.</summary>
        public Coord Of(Vec2 world) => Cells.Of(world);

        /// <summary>Where a regiment standing on this cell stands.</summary>
        public Vec2 CentreOf(Coord cell) => Cells.CentreOf(cell);

        /// <summary>Whether this cell's centre is on the map at all.</summary>
        public bool OnBoard(Coord cell) => Bounds.Contains(CentreOf(cell));

        /// <summary>Whether a body of this size fits one cell however it is turned.</summary>
        public bool Holds(Footprint footprint) =>
            2f * footprint.BoundingRadius <= CellWidth + 1e-3f;

        /// <summary>How many cells across the shorter side of the field is.</summary>
        /// <remarks>
        /// The number that decides whether there is any manoeuvre in the game: a
        /// regiment that crosses this in a few turns cannot be gone round.
        /// </remarks>
        public float ShortSideInCells => MathF.Min(Bounds.Width, Bounds.Height) / CellWidth;

        /// <summary>The nearest facing a regiment may hold to a free bearing.</summary>
        public Facing Snap(Facing free) => Cells.Snap(free);

        /// <summary>Whether a bearing is one of the ones a regiment may hold.</summary>
        public bool IsABoardFacing(Facing facing, float toleranceDegrees = 0.01f) =>
            Facing.AbsoluteDelta(facing, Snap(facing)) * 180f / MathF.PI <= toleranceDegrees;

        /// <summary>The step that puts the next regiment at this one's shoulder.</summary>
        public Coord ShoulderStep(Facing front) => Cells.ShoulderStep(front);

        /// <summary>
        /// How fast the going is on a cell, as a multiple of open ground, or
        /// zero where this movement type cannot go at all.
        /// </summary>
        /// <remarks>
        /// Sampled at the centre and nowhere else, which is the whole point of
        /// putting a game on a board: a cell is one kind of ground, and a
        /// regiment standing on it is standing on that ground. The continuous
        /// game samples a whole rectangle because a rectangle can straddle a
        /// shoreline. A cell cannot straddle anything.
        /// </remarks>
        public float GoingOn(BattleState battle, Coord cell, MovementType moving)
        {
            if (!OnBoard(cell)) return 0f;

            return battle.Movement.SpeedMultiplier(battle.Terrain.At(CentreOf(cell)), moving);
        }

        /// <summary>Who is standing on each cell, read off the units themselves.</summary>
        /// <param name="mover">
        /// The regiment the answer is for, if there is one. Its own wing is left
        /// out - see the remarks.
        /// </param>
        /// <remarks>
        /// <para>
        /// <b>A wing is not an obstacle to itself [M152].</b> Regiments picked up
        /// together are bonded, and a bond marches as one body: they all set off
        /// at once, so the cell a wing-mate is standing on now is a cell it is
        /// about to leave. Counting it as held is what made a wing fight itself
        /// for ground - the second regiment of a line ordered forward would find
        /// the first one''' + "'" + '''s ground taken, get shoved a cell sideways, and the
        /// line would come apart over a few turns.
        /// </para>
        /// <para>
        /// Only bond-mates, and only when a mover is named. Everybody else on
        /// the field holds their cell, including friends outside the wing.
        /// </para>
        /// </remarks>
        public Dictionary<Coord, UnitId> WhoIsWhere(BattleState battle, UnitInstance? mover = null)
        {
            if (battle == null) throw new ArgumentNullException(nameof(battle));

            var standing = new Dictionary<Coord, UnitId>();

            foreach (UnitInstance unit in battle.UnitsOnField())
            {
                if (mover != null && unit.Id != mover.Id && InTheSameWing(mover, unit)) continue;

                standing[Of(unit.Position)] = unit.Id;
            }

            return standing;
        }

        /// <summary>Whether two regiments are being handled as one body.</summary>
        public static bool InTheSameWing(UnitInstance a, UnitInstance b) =>
            a.Bond != 0 && a.Bond == b.Bond;

        /// <summary>
        /// Gives every regiment of a wing a cell of its own to march to, decided
        /// once for the whole wing rather than one regiment at a time.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>[M152].</b> A wing is ordered by translating the shape it already
        /// stands in, which on a board is exact: every regiment sits on a cell
        /// centre, so shifting them all by one vector shifts them all by the same
        /// whole number of cells and they land distinct. That holds right up
        /// until one of the wanted cells is water, or held by somebody outside
        /// the wing - and then that regiment is shoved to the nearest free cell,
        /// which the <i>next</i> regiment knows nothing about, because the wing
        /// is planned in parallel against one snapshot.
        /// </para>
        /// <para>
        /// So the shoving is done here, once, in order, with each regiment''' + "'" + '''s
        /// answer added to what the next one must avoid. What comes back is a
        /// cell centre apiece and no two the same - which is the whole of "they
        /// should not conflict over where they are going".
        /// </para>
        /// <para>
        /// It decides destinations and nothing else. Whether a regiment can
        /// actually walk to the cell it has been given is the planner''' + "'" + '''s
        /// question, asked separately and allowed to fail.
        /// </para>
        /// </remarks>
        public Vec2[] FormUpAt(
            BattleState battle, IReadOnlyList<UnitInstance> wing, IReadOnlyList<Vec2> wanted, int searchRings)
        {
            if (battle == null) throw new ArgumentNullException(nameof(battle));
            if (wing == null) throw new ArgumentNullException(nameof(wing));
            if (wanted == null) throw new ArgumentNullException(nameof(wanted));

            if (wanted.Count != wing.Count)
                throw new ArgumentException("One wanted place per regiment.", nameof(wanted));

            var inTheWing = new HashSet<UnitId>();

            foreach (UnitInstance unit in wing) inTheWing.Add(unit.Id);

            // Everybody outside the wing holds their ground. The wing itself is
            // left out: they are all setting off together, so where they stand
            // now is not where they will be.
            var taken = new Dictionary<Coord, UnitId>();

            foreach (UnitInstance unit in battle.UnitsOnField())
            {
                if (inTheWing.Contains(unit.Id)) continue;

                taken[Of(unit.Position)] = unit.Id;
            }

            var places = new Vec2[wing.Count];

            for (int i = 0; i < wing.Count; i++)
            {
                Coord asked = Of(wanted[i]);

                if (!NearestFree(battle, wing[i], asked, taken, searchRings, out Coord given))
                    given = asked;

                taken[given] = wing[i].Id;
                places[i] = CentreOf(given);
            }

            return places;
        }

        /// <summary>
        /// The nearest cell to <paramref name="wanted"/> this regiment could
        /// stand on, searched outward ring by ring.
        /// </summary>
        /// <remarks>
        /// Used when two regiments muster into the same cell, and when an order
        /// is given to a cell somebody already holds. Rings rather than a search,
        /// because the question is "somewhere near here" and not "how do I get
        /// there" - the route is asked for separately, and answering it here
        /// would make where a regiment is put quietly depend on whether it can
        /// walk to it.
        /// </remarks>
        public bool NearestFree(
            BattleState battle, UnitInstance unit, Coord wanted,
            IReadOnlyDictionary<Coord, UnitId> taken, int searchRings, out Coord free)
        {
            for (int radius = 0; radius <= searchRings; radius++)
            {
                foreach (Coord cell in Cells.Ring(wanted, radius))
                {
                    if (taken.TryGetValue(cell, out UnitId who) && who != unit.Id) continue;
                    if (GoingOn(battle, cell, unit.Def.Movement) <= 0f) continue;

                    free = cell;
                    return true;
                }
            }

            free = wanted;
            return false;
        }

        public override string ToString() =>
            $"Board({CellWidth:0} m {Cells.Name}, sized to a {WidestBody:0.0} m regiment, " +
            $"about {Bounds.Width / CellWidth:0} x {Bounds.Height / CellWidth:0} over {Bounds})";
    }
}
