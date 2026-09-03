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

            // [M156] Squares only - see SlotCount. One cell of margin on each
            // side, so that a body whose centre is on the last cell of the field
            // still has its overhanging cells numbered rather than falling off
            // the end of the array and silently taking the dictionary path.
            if (cells is SquareLattice)
            {
                Coord low = cells.Of(bounds.Min);
                Coord high = cells.Of(bounds.Max);

                _lowQ = low.Q - 1;
                _lowR = low.R - 1;
                _spanQ = high.Q - low.Q + 3;
                _spanR = high.R - low.R + 3;
            }
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

            // [M155] The cell answers to the ground, not to the regiment. The
            // widest body is still measured, but only so a log can say what is
            // standing on the board beside how finely it is divided.
            float cell = GridMode.CellMetres;

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

        private readonly int _lowQ, _lowR, _spanQ, _spanR;

        /// <summary>How many cells the board addresses, including any holes.</summary>
        /// <remarks>
        /// <b>[M156].</b> Cells are numbered so that the hot paths can use arrays
        /// instead of dictionaries. On a square lattice the cell coordinates of a
        /// rectangular field are themselves a rectangle, so the numbering is
        /// exact and dense. On hexes they are not - an axial box round a
        /// rectangle either misses corners or wastes a great deal of room - so
        /// hexes keep the dictionaries and <see cref="Slot"/> answers -1. Squares
        /// are the lattice the game is played on; hexes are kept for comparison,
        /// and comparison is allowed to be slower.
        /// </remarks>
        public int SlotCount => _spanQ * _spanR;

        /// <summary>The cell a number belongs to. The inverse of <see cref="Slot"/>.</summary>
        public Coord CellOfSlot(int slot) =>
            new Coord(_lowQ + slot % _spanQ, _lowR + slot / _spanQ);

        /// <summary>This cell's number, or -1 if it has none.</summary>
        public int Slot(Coord cell)
        {
            int q = cell.Q - _lowQ;
            int r = cell.R - _lowR;

            if (q < 0 || r < 0 || q >= _spanQ || r >= _spanR) return -1;

            return r * _spanQ + q;
        }

        private readonly Dictionary<int, float[]> _goingByType = new Dictionary<int, float[]>();

        /// <summary>
        /// How fast the going is on every cell of the board for this movement
        /// type, indexed by <see cref="Slot"/>.
        /// </summary>
        /// <remarks>
        /// Filled once and kept. The ground under a cell does not change during a
        /// battle, and the route search reads the same cells thousands of times -
        /// once for every body that might stand over them, times every front it
        /// might arrive on. About 27 000 floats on the finest board, which is
        /// 110 kB to save tens of milliseconds on every order.
        /// </remarks>
        public float[]? GoingEverywhere(BattleState battle, MovementType moving)
        {
            if (SlotCount <= 0) return null;

            if (_goingByType.TryGetValue((int)moving, out float[] known)) return known;

            var going = new float[SlotCount];

            for (int r = 0; r < _spanR; r++)
            for (int q = 0; q < _spanQ; q++)
            {
                var cell = new Coord(_lowQ + q, _lowR + r);

                going[r * _spanQ + q] = OnBoard(cell)
                    ? battle.Movement.SpeedMultiplier(battle.Terrain.At(CentreOf(cell)), moving)
                    : 0f;
            }

            _goingByType[(int)moving] = going;

            return going;
        }

        private readonly Dictionary<(int Wide, int Deep, int Front), Coord[]> _stencils =
            new Dictionary<(int, int, int), Coord[]>();

        private readonly Dictionary<(int Moving, Coord At), float> _going =
            new Dictionary<(int, Coord), float>();

        /// <summary>
        /// The cells a body of this size and front covers, as offsets from the
        /// cell its centre stands on.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>[M156], and it is the whole of the performance fix.</b> Working out
        /// which cells a body covers is a flood fill over its own footprint - a
        /// few hundred geometry tests and three allocations. The route search
        /// asks that question for every cell of the board at every front it might
        /// arrive on, which on a 12,5 m Great Field is 27 648 x 8 of them: about
        /// thirty-three million geometry tests and six hundred thousand
        /// allocations for one regiment's route. It was unplayable, and reported
        /// as such.
        /// </para>
        /// <para>
        /// <b>The shape does not depend on where it is.</b> Cell centres form a
        /// regular lattice, and whether a body covers a cell depends only on the
        /// offset between the body's centre and that cell's centre - so moving
        /// the body a whole number of cells moves its covered set by the same
        /// whole number of cells, exactly. This is true on hexes as well as
        /// squares, because both map cell coordinates to world by a linear
        /// transform.
        /// </para>
        /// <para>
        /// So the fill runs <b>once per footprint and front</b> - two dozen times
        /// for the whole battle rather than a quarter of a million times per
        /// route - and every later question is a handful of array reads. This is
        /// memory spent to buy clock, which is the trade this project has already
        /// decided it wants.
        /// </para>
        /// </remarks>
        public Coord[] Stencil(Footprint print, Facing front)
        {
            var key = (
                (int)MathF.Round(print.Width * 10f),
                (int)MathF.Round(print.Depth * 10f),
                (int)MathF.Round(front.Degrees * 10f));

            if (_stencils.TryGetValue(key, out Coord[] known)) return known;

            var home = new Coord(0, 0);

            List<Coord> cells = Occupancy.Under(Cells, new OrientedRect(CentreOf(home), front, print));

            var offsets = new Coord[cells.Count];

            for (int i = 0; i < cells.Count; i++)
                offsets[i] = new Coord(cells[i].Q - home.Q, cells[i].R - home.R);

            _stencils[key] = offsets;

            return offsets;
        }

        /// <summary>The cells this regiment covers, as it stands.</summary>
        /// <remarks>
        /// Takes the stencil when the regiment is standing on a cell centre,
        /// which on the board it is at the end of every turn, and falls back to
        /// the fill when it is not - a regiment mid-march in the free game, or one
        /// that has just been dropped somewhere by a test.
        /// </remarks>
        public List<Coord> CellsUnder(UnitInstance unit)
        {
            if (unit == null) throw new ArgumentNullException(nameof(unit));

            Coord home = Of(unit.Position);

            if (Vec2.DistanceSquared(unit.Position, CentreOf(home)) > 0.01f)
                return Occupancy.Under(Cells, unit);

            Coord[] shape = Stencil(unit.Footprint, unit.Facing);

            var cells = new List<Coord>(shape.Length);

            for (int i = 0; i < shape.Length; i++)
                cells.Add(new Coord(home.Q + shape[i].Q, home.R + shape[i].R));

            return cells;
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
        /// <remarks>
        /// <b>[M155]. The mode''' + "'" + '''s business, not the lattice''' + "'" + '''s.</b> How a
        /// regiment may face followed from how a cell had neighbours only while
        /// a regiment had to fit in one cell. It covers a set of cells now, so
        /// the front is free to be as fine as the designer wants it - 15 degrees
        /// - and <see cref="ILattice.Snap"/> is left to the lattice for the
        /// questions that really are about cell directions, such as which way a
        /// shoulder lies.
        /// </remarks>
        public Facing Snap(Facing free)
        {
            float step = 360f / Math.Max(1, GridMode.FacingCount);

            return Facing.FromDegrees(MathF.Round(free.Degrees / step) * step);
        }

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

            // [M156] Remembered per cell and movement type. The ground under a
            // cell does not change during a battle, and the route search asks
            // about the same cells thousands of times - once per body that might
            // stand over them, times every front it might arrive on.
            int slot = Slot(cell);

            if (slot >= 0)
            {
                float[]? everywhere = GoingEverywhere(battle, moving);

                if (everywhere != null) return everywhere[slot];
            }

            var key = ((int)moving, cell);

            if (_going.TryGetValue(key, out float known)) return known;

            float going = battle.Movement.SpeedMultiplier(battle.Terrain.At(CentreOf(cell)), moving);

            _going[key] = going;

            return going;
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

                // [M155] Every cell the body lies over, not the one its centre
                // happens to fall in. On a 25 m grid a regiment is four cells by
                // two, and the three it is not centred on are just as solid.
                foreach (Coord cell in CellsUnder(unit))
                    standing[cell] = unit.Id;
            }

            return standing;
        }

        /// <summary>
        /// Ground that regiments already on the move have spoken for: the cells
        /// each one's body will cover when it arrives.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>[M159], and it is the designer's second suggestion.</b> Where a
        /// regiment is standing now is not the only claim it has on the field. If
        /// it is marching, it has also claimed where it is GOING, and an order
        /// given to a second regiment ought to know that - otherwise two marches
        /// resolve onto the same ground, both are routed there, and they fight
        /// over it on arrival. That is the whole of "they still compete for
        /// location", and no amount of clash resolution afterwards fixes an order
        /// that should never have been drawn.
        /// </para>
        /// <para>
        /// <b>Derived, never stored,</b> which is the same rule the rest of this
        /// class keeps. A reservation is not a thing to be booked, held and
        /// released - that is a lifecycle, and a lifecycle is a thing that leaks.
        /// It is simply the far end of a route that already exists: give the
        /// order and the ground is spoken for, cancel it and it is not, and there
        /// is nothing to get out of step.
        /// </para>
        /// <para>
        /// The arrival front is taken from the route's last leg, because which
        /// cells a body covers depends on which way it is turned and a regiment
        /// arrives facing the way it came in.
        /// </para>
        /// </remarks>
        public Dictionary<Coord, UnitId> SpokenFor(BattleState battle, UnitInstance? mover = null)
        {
            if (battle == null) throw new ArgumentNullException(nameof(battle));

            var claimed = new Dictionary<Coord, UnitId>();

            foreach (UnitInstance unit in battle.UnitsOnField())
            {
                if (mover != null && unit.Id == mover.Id) continue;

                MovementRoute? route = unit.Route;

                if (route == null || route.IsComplete) continue;

                Coord ends = Of(route.Destination);

                foreach (Coord cell in Occupancy.UnderIfItStood(
                             Cells, unit, CentreOf(ends), ArrivingOn(unit, route)))
                    claimed[cell] = unit.Id;
            }

            return claimed;
        }

        /// <summary>Which way a regiment will be facing when it finishes this route.</summary>
        private static Facing ArrivingOn(UnitInstance unit, MovementRoute route)
        {
            IReadOnlyList<Vec2> legs = route.Waypoints;

            if (legs.Count < 2) return unit.Facing;

            Vec2 last = legs[legs.Count - 1];
            Vec2 before = legs[legs.Count - 2];

            return Vec2.Distance(last, before) < 0.01f
                ? unit.Facing
                : Facing.Towards(before, last);
        }

        /// <summary>
        /// How far this regiment can carry itself in one turn of the board game,
        /// in metres.
        /// </summary>
        /// <remarks>
        /// <b>[M159].</b> Whole cells, because whole cells are all it can take -
        /// so this is what a reach marker drawn round a selected regiment should
        /// use rather than speed times the turn, which would promise a distance
        /// it cannot actually stop at.
        /// </remarks>
        public float ReachInOneTurn(UnitInstance unit) => GridMode.CellsPerTurn(unit) * CellWidth;

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

                foreach (Coord cell in CellsUnder(unit))
                    taken[cell] = unit.Id;
            }

            var places = new Vec2[wing.Count];

            for (int i = 0; i < wing.Count; i++)
            {
                Coord asked = Of(wanted[i]);

                if (!NearestFree(battle, wing[i], asked, taken, searchRings, out Coord given))
                    given = asked;

                // [M155] Book the whole body, so the next regiment of the wing
                // is answered against ground this one really occupies. Booking
                // one cell is how a line ended up standing through itself.
                foreach (Coord cell in Occupancy.UnderIfItStood(
                             Cells, wing[i], CentreOf(given), wing[i].Facing))
                    taken[cell] = wing[i].Id;

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
            IReadOnlyDictionary<Coord, UnitId> taken, int searchRings, out Coord free) =>
            NearestFree(battle, unit, wanted, taken, searchRings, unit.Facing, out free);

        /// <summary>
        /// The nearest cell this regiment could stand centred on, facing that
        /// way, with its <b>whole body</b> on free and passable ground.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>[M155], and it is where "they still compete for location" is
        /// actually answered.</b> The old test asked whether one cell was free,
        /// which for an 80 x 40 m regiment on a 25 m grid is one eighth of the
        /// question. Two regiments could each hold a free centre cell and still
        /// be standing through one another.
        /// </para>
        /// <para>
        /// The front matters, so it is a parameter rather than read off the
        /// regiment. Where a body will fit depends on which way it is turned -
        /// a rectangle in a gap is the whole of M27 - and a regiment marching
        /// somewhere is going to arrive facing the way it was sent, not the way
        /// it is standing now.
        /// </para>
        /// </remarks>
        public bool NearestFree(
            BattleState battle, UnitInstance unit, Coord wanted,
            IReadOnlyDictionary<Coord, UnitId> taken, int searchRings, Facing front, out Coord free)
        {
            for (int radius = 0; radius <= searchRings; radius++)
            {
                foreach (Coord cell in Cells.Ring(wanted, radius))
                {
                    if (!CouldStandAt(battle, unit, cell, front, taken)) continue;

                    free = cell;
                    return true;
                }
            }

            free = wanted;
            return false;
        }

        /// <summary>
        /// Whether this regiment could stand centred on that cell facing that
        /// way: every cell of its body free of anybody else, and passable.
        /// </summary>
        /// <summary>
        /// Whether two regiments sharing a cell are judged by their bodies
        /// rather than by the cell. [M161]
        /// </summary>
        public static bool BodiesDecideAClash = true;

        public bool CouldStandAt(
            BattleState battle, UnitInstance unit, Coord cell, Facing front,
            IReadOnlyDictionary<Coord, UnitId> taken)
        {
            if (battle == null) throw new ArgumentNullException(nameof(battle));
            if (unit == null) throw new ArgumentNullException(nameof(unit));

            // [M156] The stencil, not a fresh fill. See Board.Stencil - this is
            // the inner loop of every board route, and it used to allocate.
            Coord[] shape = Stencil(unit.Footprint, front);

            // Cells decide where a regiment may STAND. They must not decide
            // whether two regiments are in each other. [M161]
            //
            // The designer, over a screenshot: "it has space here but doesnt
            // fit". Measured on the arrangement the recording kept - an 80 m
            // frontage between two of its own - the board demands a gap of
            // 152,5 m at 25 m cells and 102,5 m at 12,5 m, which is 36,3 m and
            // 11,3 m of visibly clear ground thrown away at each end.
            //
            // The cause is that a body claims every cell it touches at all, so
            // a body ending at 1227 and one beginning at 1247 both claim the
            // cell running 1225 to 1250 and are declared to be on the same
            // ground. Twenty metres apart is indistinguishable from standing
            // inside one another, and no amount of margin tuning fixes a
            // quantisation error - halving the cell only halves it.
            //
            // So the two questions are separated, and each is asked of the
            // thing that can answer it. WHERE a regiment may stand stays a
            // board question, and is why the stencil is still walked: on the
            // board, on ground it can cross, snapped to a cell and a front.
            // WHETHER it is inside somebody is a question about two rectangles,
            // and is answered by the two rectangles - the same test the
            // continuous planner uses and the same one the player is making
            // with their own eyes.
            //
            // The cells are still doing the work that suits them: they are the
            // broad phase. Only bodies holding one of the stencil's cells are
            // tested exactly, so this costs a handful of rectangle tests rather
            // than a sweep of the field.
            HashSet<UnitId>? near = null;

            for (int i = 0; i < shape.Length; i++)
            {
                var under = new Coord(cell.Q + shape[i].Q, cell.R + shape[i].R);

                if (!OnBoard(under)) return false;

                if (GoingOn(battle, under, unit.Def.Movement) <= 0f) return false;

                if (taken == null) continue;

                if (!taken.TryGetValue(under, out UnitId who) || who == unit.Id) continue;

                if (!BodiesDecideAClash) return false;

                (near ??= new HashSet<UnitId>()).Add(who);
            }

            if (near == null) return true;

            var standing = new OrientedRect(CentreOf(cell), front, unit.Footprint);

            foreach (UnitId id in near)
            {
                UnitInstance other = battle.Get(id);

                if (other == null || !other.IsOnField || other.Id == unit.Id) continue;

                if (OrientedRect.Overlaps(standing, other.Shape)) return false;
            }

            return true;
        }

        public override string ToString() =>
            // [M155] The cell is a SETTING now, not a size derived from the
            // widest body. This line still said "sized to a 89,4 m regiment" in
            // every recording, which is a description of the model that was
            // removed - and the widest body is worth saying, because it is what
            // tells you how many cells a regiment covers.
            $"Board({CellWidth:0} m {Cells.Name}, widest regiment {WidestBody:0.0} m, " +
            $"about {Bounds.Width / CellWidth:0} x {Bounds.Height / CellWidth:0} over {Bounds})";
    }
}
