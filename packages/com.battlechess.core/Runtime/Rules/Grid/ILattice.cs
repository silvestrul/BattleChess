using System;
using System.Collections.Generic;
using BattleChess.Contracts;

namespace BattleChess.Rules.Grid
{
    /// <summary>
    /// The shape of the cells a board is made of.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>[M151].</b> Everything the board game does - one regiment to a cell,
    /// A* over free cells, mustering, the drawn outline - is indifferent to
    /// whether those cells are hexes or squares. What is <i>not</i> indifferent
    /// is the one thing [M150] proved: a rectangle's frontage runs perpendicular
    /// to its facing, so whether regiments can stand in a line depends entirely
    /// on whether the perpendicular of a cell axis is another cell axis.
    /// </para>
    /// <para>
    /// <b>On a hex it never is</b> - the six bearings are multiples of sixty and
    /// ninety plus a multiple of sixty is not one - so a hex board aligns
    /// marching or lines and never both. <b>On a square it always is</b>: the
    /// eight bearings are multiples of forty-five, and ninety plus a multiple of
    /// forty-five is another multiple of forty-five. Squares are why nearly
    /// every game with line formations uses them, and this interface is where
    /// that difference lives instead of being spread through the board.
    /// </para>
    /// <para>
    /// <b>Cells are addressed by <see cref="Coord"/> in both.</b> On a hex that
    /// is its usual axial (q, r); on a square it is plainly (column, row), and
    /// the cube axis and hex distance on that struct are simply not asked for.
    /// Sharing the type is what lets <c>PathResult.SearchCells</c>,
    /// <c>CoordMinHeap</c> and the rest carry over untouched. Ask this interface
    /// for distances rather than <see cref="Coord"/>, which only knows the hex
    /// answer.
    /// </para>
    /// </remarks>
    public interface ILattice
    {
        /// <summary>What to call this shape in a log.</summary>
        string Name { get; }

        /// <summary>Flat-to-flat width of one cell, in metres.</summary>
        float CellWidth { get; }

        /// <summary>How many cells touch one cell.</summary>
        int DirectionCount { get; }

        /// <summary>How many facings a regiment may hold.</summary>
        int FacingCount { get; }

        /// <summary>Which cell a world position falls in.</summary>
        Coord Of(Vec2 world);

        /// <summary>Where a regiment standing on this cell stands.</summary>
        Vec2 CentreOf(Coord cell);

        /// <summary>Every cell touching this one, in direction order.</summary>
        void Neighbours(Coord cell, Span<Coord> into);

        /// <summary>The cells exactly this many steps away. Radius nought is the centre.</summary>
        IEnumerable<Coord> Ring(Coord centre, int radius);

        /// <summary>Ground covered walking between two touching cells, in metres.</summary>
        /// <remarks>
        /// Not always <see cref="CellWidth"/>: a square lattice's diagonal steps
        /// are longer than its orthogonal ones, and pricing them alike would
        /// make a staircase look cheaper than the straight line it approximates.
        /// </remarks>
        float StepMetres(Coord from, Coord to);

        /// <summary>
        /// The least ground any route between two cells could cover, in metres.
        /// </summary>
        /// <remarks>
        /// The A* heuristic's basis, so it must never overstate. On a square
        /// that is the octile distance rather than the straight line, because a
        /// route may only move on the eight bearings.
        /// </remarks>
        float LeastMetresBetween(Coord a, Coord b);

        /// <summary>The nearest facing a regiment may hold to a free bearing.</summary>
        Facing Snap(Facing free);

        /// <summary>
        /// The step that puts the next regiment at this one's shoulder.
        /// </summary>
        /// <remarks>
        /// The whole point of [M150] and [M151], made available rather than
        /// merely true: a line of regiments is drawn up by repeating this.
        /// </remarks>
        Coord ShoulderStep(Facing front);

        /// <summary>How many corners one cell is drawn with.</summary>
        int CornerCount { get; }

        /// <summary>The corners of a cell, for drawing it.</summary>
        void CornersOf(Coord cell, Span<Vec2> into);
    }

    /// <summary>Six-sided cells, pointy-top. The board as [M147] first built it.</summary>
    /// <remarks>
    /// Kept whole beside the square lattice rather than deleted, because the
    /// difference between them is a design question the designer may want to
    /// look at twice, and a shape that has to be reconstructed to be compared is
    /// a shape that never gets compared.
    /// </remarks>
    public sealed class HexLattice : ILattice
    {
        /// <summary>Where the six facings sit: a corner rather than an edge.</summary>
        /// <remarks>
        /// <b>[M150].</b> Thirty degrees round from the hex bearings, so that a
        /// regiment's frontage lies along a hex axis and a line of them is a
        /// line rather than a staircase. See <see cref="ILattice"/>.
        /// </remarks>
        public const float FacingOffsetDegrees = 30f;

        private readonly HexLayout _layout;

        public HexLattice(float cellWidth, Vec2 origin)
        {
            CellWidth = cellWidth;
            _layout = HexLayout.FromNeighbourDistance(cellWidth, origin);
        }

        public string Name => "hexes";
        public float CellWidth { get; }
        public int DirectionCount => HexMath.DirectionCount;
        public int FacingCount => HexMath.DirectionCount;
        public int CornerCount => HexMath.DirectionCount;

        /// <summary>The layout itself, for the few callers that want hex geometry.</summary>
        public HexLayout Layout => _layout;

        public Coord Of(Vec2 world) => _layout.ToCoord(world);

        public Vec2 CentreOf(Coord cell) => _layout.ToWorld(cell);

        public void Neighbours(Coord cell, Span<Coord> into) => HexMath.Neighbours(cell, into);

        public IEnumerable<Coord> Ring(Coord centre, int radius) => HexMath.Ring(centre, radius);

        public float StepMetres(Coord from, Coord to) => CellWidth;

        public float LeastMetresBetween(Coord a, Coord b) => Coord.Distance(a, b) * CellWidth;

        public Facing Snap(Facing free)
        {
            float step = 360f / FacingCount;
            float turns = MathF.Round((free.Degrees - FacingOffsetDegrees) / step);

            return Facing.FromDegrees(FacingOffsetDegrees + turns * step);
        }

        public Coord ShoulderStep(Facing front)
        {
            int which = (int)MathF.Round((front.Degrees + 90f) / 60f) % HexMath.DirectionCount;

            if (which < 0) which += HexMath.DirectionCount;

            return HexMath.Offset((HexDirection)which);
        }

        public void CornersOf(Coord cell, Span<Vec2> into) => _layout.GetCorners(cell, into);
    }

    /// <summary>
    /// Four-sided cells with eight ways out, which is what lets a line and a
    /// march both lie on the grid.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>[M151], and it is the answer to the fault [M150] could only work
    /// around.</b> Eight bearings, every one a multiple of forty-five, and the
    /// perpendicular of a multiple of forty-five is another one. So a regiment
    /// marches straight ahead along a real step <i>and</i> stands shoulder to
    /// shoulder with its neighbours along a real step, on any of the four axes.
    /// The hex board had to give up one of those; this gives up neither.
    /// </para>
    /// <para>
    /// <b>Diagonals cost what they are.</b> A diagonal step is the cell width
    /// times root two, priced as such, or a route would prefer a staircase to
    /// the straight line it is imitating and every march would come out
    /// bent.
    /// </para>
    /// <para>
    /// <b>Coordinates are (column, row) with the row running north</b>, which is
    /// the natural reading and differs from the hex layout, where r runs south.
    /// Nothing outside this class looks at either.
    /// </para>
    /// </remarks>
    public sealed class SquareLattice : ILattice
    {
        private static readonly float Root2 = MathF.Sqrt(2f);

        /// <summary>
        /// The eight steps, anticlockwise from east, so that step
        /// <c>i</c> lies on a bearing of <c>45 * i</c> degrees.
        /// </summary>
        private static readonly Coord[] Steps =
        {
            new Coord(+1, 0),   //   0  east
            new Coord(+1, +1),  //  45  north-east
            new Coord(0, +1),   //  90  north
            new Coord(-1, +1),  // 135  north-west
            new Coord(-1, 0),   // 180  west
            new Coord(-1, -1),  // 225  south-west
            new Coord(0, -1),   // 270  south
            new Coord(+1, -1)   // 315  south-east
        };

        private readonly Vec2 _origin;

        public SquareLattice(float cellWidth, Vec2 origin)
        {
            CellWidth = cellWidth;
            _origin = origin;
        }

        public string Name => "squares";
        public float CellWidth { get; }
        public int DirectionCount => Steps.Length;
        public int FacingCount => Steps.Length;
        public int CornerCount => 4;

        public Coord Of(Vec2 world) => new Coord(
            (int)MathF.Floor((world.X - _origin.X) / CellWidth),
            (int)MathF.Floor((world.Y - _origin.Y) / CellWidth));

        public Vec2 CentreOf(Coord cell) => new Vec2(
            _origin.X + (cell.Q + 0.5f) * CellWidth,
            _origin.Y + (cell.R + 0.5f) * CellWidth);

        public void Neighbours(Coord cell, Span<Coord> into)
        {
            if (into.Length < Steps.Length)
                throw new ArgumentException($"Needs room for {Steps.Length} cells.", nameof(into));

            for (int i = 0; i < Steps.Length; i++) into[i] = cell + Steps[i];
        }

        /// <summary>The square ring at this many steps, walked once round.</summary>
        public IEnumerable<Coord> Ring(Coord centre, int radius)
        {
            if (radius < 0) throw new ArgumentOutOfRangeException(nameof(radius));

            if (radius == 0)
            {
                yield return centre;
                yield break;
            }

            for (int q = -radius; q <= radius; q++)
            {
                yield return new Coord(centre.Q + q, centre.R - radius);
                yield return new Coord(centre.Q + q, centre.R + radius);
            }

            // The corners are already given out by the rows above.
            for (int r = -radius + 1; r <= radius - 1; r++)
            {
                yield return new Coord(centre.Q - radius, centre.R + r);
                yield return new Coord(centre.Q + radius, centre.R + r);
            }
        }

        public float StepMetres(Coord from, Coord to)
        {
            bool diagonal = from.Q != to.Q && from.R != to.R;

            return diagonal ? CellWidth * Root2 : CellWidth;
        }

        /// <summary>Octile distance: the shortest walk on eight bearings.</summary>
        public float LeastMetresBetween(Coord a, Coord b)
        {
            int across = Math.Abs(a.Q - b.Q);
            int up = Math.Abs(a.R - b.R);

            int straight = Math.Max(across, up);
            int diagonal = Math.Min(across, up);

            return CellWidth * ((straight - diagonal) + diagonal * Root2);
        }

        public Facing Snap(Facing free)
        {
            float step = 360f / FacingCount;

            return Facing.FromDegrees(MathF.Round(free.Degrees / step) * step);
        }

        public Coord ShoulderStep(Facing front)
        {
            float step = 360f / Steps.Length;

            int which = (int)MathF.Round((front.Degrees + 90f) / step) % Steps.Length;

            if (which < 0) which += Steps.Length;

            return Steps[which];
        }

        public void CornersOf(Coord cell, Span<Vec2> into)
        {
            if (into.Length < 4)
                throw new ArgumentException("Needs room for 4 corners.", nameof(into));

            Vec2 centre = CentreOf(cell);
            float half = CellWidth * 0.5f;

            into[0] = new Vec2(centre.X - half, centre.Y - half);
            into[1] = new Vec2(centre.X + half, centre.Y - half);
            into[2] = new Vec2(centre.X + half, centre.Y + half);
            into[3] = new Vec2(centre.X - half, centre.Y + half);
        }
    }

    /// <summary>Which shape of cell the board game is played on.</summary>
    public enum LatticeShape
    {
        /// <summary>Six-sided. Lines and marching cannot both lie on the grid.</summary>
        Hex = 0,

        /// <summary>Four-sided with eight ways out. Both lie on the grid.</summary>
        Square = 1
    }
}
