using System;

namespace BattleChess.Contracts
{
    /// <summary>
    /// A hex coordinate in axial form (q, r), with the third cube axis derived
    /// as <c>s = -q - r</c>.
    /// </summary>
    /// <remarks>
    /// Axial storage keeps the struct at 8 bytes while cube arithmetic keeps
    /// distance and rotation trivially correct — the standard trick, and the
    /// reason hexes are less painful to work with than they first appear.
    ///
    /// Orientation (pointy-top vs flat-top) is purely a rendering concern and
    /// lives in the view layer. Nothing here depends on it.
    /// </remarks>
    public readonly struct Coord : IEquatable<Coord>, IComparable<Coord>
    {
        public static readonly Coord Zero = new Coord(0, 0);

        public readonly int Q;
        public readonly int R;

        public Coord(int q, int r)
        {
            Q = q;
            R = r;
        }

        /// <summary>The implied third cube axis. Always <c>-Q - R</c>.</summary>
        public int S => -Q - R;

        /// <summary>Distance from the origin, in hexes.</summary>
        public int Length => (Math.Abs(Q) + Math.Abs(R) + Math.Abs(Q + R)) / 2;

        public static Coord operator +(Coord a, Coord b) => new Coord(a.Q + b.Q, a.R + b.R);
        public static Coord operator -(Coord a, Coord b) => new Coord(a.Q - b.Q, a.R - b.R);
        public static Coord operator -(Coord a) => new Coord(-a.Q, -a.R);
        public static Coord operator *(Coord a, int k) => new Coord(a.Q * k, a.R * k);
        public static Coord operator *(int k, Coord a) => new Coord(a.Q * k, a.R * k);

        /// <summary>Distance between two hexes, in hexes.</summary>
        public static int Distance(Coord a, Coord b) => (a - b).Length;

        /// <summary>The adjacent hex in the given direction.</summary>
        public Coord Neighbour(HexDirection direction) => this + HexMath.Offset(direction);

        public bool Equals(Coord other) => Q == other.Q && R == other.R;
        public override bool Equals(object? obj) => obj is Coord other && Equals(other);

        public override int GetHashCode()
        {
            // Cheap, well-spread mix. Coords are used as dictionary keys in
            // tooling and tests; the simulation itself indexes flat arrays.
            unchecked
            {
                return (Q * 397) ^ R;
            }
        }

        /// <summary>
        /// Total order by (Q, R). Exists so collections of coords can be sorted
        /// into a stable sequence rather than relying on hash iteration order,
        /// which reproducibility forbids.
        /// </summary>
        public int CompareTo(Coord other)
        {
            int byQ = Q.CompareTo(other.Q);
            return byQ != 0 ? byQ : R.CompareTo(other.R);
        }

        public override string ToString() => $"({Q},{R})";

        public static bool operator ==(Coord a, Coord b) => a.Q == b.Q && a.R == b.R;
        public static bool operator !=(Coord a, Coord b) => a.Q != b.Q || a.R != b.R;
    }

    /// <summary>
    /// The six hex directions, ordered counter-clockwise from east.
    /// Also used as unit facing.
    /// </summary>
    public enum HexDirection
    {
        East = 0,
        NorthEast = 1,
        NorthWest = 2,
        West = 3,
        SouthWest = 4,
        SouthEast = 5
    }

    /// <summary>
    /// A hex coordinate with fractional components, produced by interpolation
    /// and converted back to a whole hex by <see cref="HexMath.Round"/>.
    /// </summary>
    public readonly struct FractionalCoord
    {
        public readonly float Q;
        public readonly float R;

        public FractionalCoord(float q, float r)
        {
            Q = q;
            R = r;
        }

        public float S => -Q - R;

        public override string ToString() => $"({Q:0.###},{R:0.###})";
    }
}
