using System;
using System.Collections.Generic;

namespace BattleChess.Contracts
{
    /// <summary>
    /// Pure hex geometry: neighbours, rings, discs, lines, rotation and facing.
    /// No allocation on the hot paths that the simulation uses per tick.
    /// </summary>
    public static class HexMath
    {
        public const int DirectionCount = 6;

        /// <summary>
        /// Axial offsets for the six directions, indexed by <see cref="HexDirection"/>
        /// and ordered counter-clockwise from east.
        /// </summary>
        private static readonly Coord[] Offsets =
        {
            new Coord(+1,  0), // East
            new Coord(+1, -1), // NorthEast
            new Coord( 0, -1), // NorthWest
            new Coord(-1,  0), // West
            new Coord(-1, +1), // SouthWest
            new Coord( 0, +1)  // SouthEast
        };

        public static Coord Offset(HexDirection direction)
        {
            int index = (int)direction;
            if ((uint)index >= DirectionCount)
                throw new ArgumentOutOfRangeException(nameof(direction), direction, "Not one of the six hex directions.");

            return Offsets[index];
        }

        /// <summary>All six neighbours, in direction order.</summary>
        public static void Neighbours(Coord centre, Span<Coord> destination)
        {
            if (destination.Length < DirectionCount)
                throw new ArgumentException($"Needs room for {DirectionCount} coords.", nameof(destination));

            for (int i = 0; i < DirectionCount; i++)
                destination[i] = centre + Offsets[i];
        }

        /// <summary>Allocating convenience overload. Prefer the <c>Span</c> form in simulation code.</summary>
        public static Coord[] Neighbours(Coord centre)
        {
            var result = new Coord[DirectionCount];
            for (int i = 0; i < DirectionCount; i++)
                result[i] = centre + Offsets[i];

            return result;
        }

        public static bool AreAdjacent(Coord a, Coord b) => Coord.Distance(a, b) == 1;

        // ---- Rotation -------------------------------------------------------

        /// <summary>
        /// Rotates 60° about the origin, in the direction of increasing
        /// <see cref="HexDirection"/>. Six applications are the identity.
        /// </summary>
        public static Coord Rotate(Coord c) => new Coord(-c.R, c.Q + c.R);

        /// <summary>Rotates 60° in the opposite sense.</summary>
        public static Coord RotateInverse(Coord c) => new Coord(c.Q + c.R, -c.Q);

        /// <summary>Rotates about an arbitrary centre.</summary>
        public static Coord RotateAround(Coord c, Coord centre, int steps)
        {
            Coord relative = c - centre;
            int turns = ((steps % DirectionCount) + DirectionCount) % DirectionCount;

            for (int i = 0; i < turns; i++)
                relative = Rotate(relative);

            return centre + relative;
        }

        public static HexDirection Opposite(HexDirection direction) =>
            (HexDirection)(((int)direction + 3) % DirectionCount);

        /// <summary>
        /// How many 60° steps separate two facings, ignoring which way round.
        /// 0 is head-on, 3 is directly from behind — the basis for flanking.
        /// </summary>
        public static int TurnsBetween(HexDirection a, HexDirection b)
        {
            int diff = Math.Abs((int)a - (int)b) % DirectionCount;
            return Math.Min(diff, DirectionCount - diff);
        }

        /// <summary>
        /// The direction that best points from <paramref name="from"/> toward
        /// <paramref name="to"/>. Returns <c>East</c> for a zero vector.
        /// </summary>
        public static HexDirection DirectionTo(Coord from, Coord to)
        {
            Coord delta = to - from;
            if (delta == Coord.Zero) return HexDirection.East;

            // Compare against each direction by projecting onto it; the largest
            // cube-space dot product wins. Cheap and exact for six candidates.
            int bestIndex = 0;
            int bestScore = int.MinValue;

            for (int i = 0; i < DirectionCount; i++)
            {
                Coord d = Offsets[i];
                int score = delta.Q * d.Q + delta.R * d.R + delta.S * d.S;
                if (score > bestScore)
                {
                    bestScore = score;
                    bestIndex = i;
                }
            }

            return (HexDirection)bestIndex;
        }

        // ---- Rings and discs ------------------------------------------------

        /// <summary>
        /// The hexes exactly <paramref name="radius"/> away from
        /// <paramref name="centre"/>. A radius of 0 yields the centre alone.
        /// </summary>
        public static List<Coord> Ring(Coord centre, int radius)
        {
            if (radius < 0)
                throw new ArgumentOutOfRangeException(nameof(radius), radius, "Radius cannot be negative.");

            var results = new List<Coord>(radius == 0 ? 1 : radius * DirectionCount);

            if (radius == 0)
            {
                results.Add(centre);
                return results;
            }

            // Start on the south-west spoke, then walk each of the six edges.
            Coord current = centre + Offsets[(int)HexDirection.SouthWest] * radius;

            for (int direction = 0; direction < DirectionCount; direction++)
            {
                for (int step = 0; step < radius; step++)
                {
                    results.Add(current);
                    current += Offsets[direction];
                }
            }

            return results;
        }

        /// <summary>
        /// Every hex within <paramref name="radius"/> of <paramref name="centre"/>,
        /// inclusive. Ordered by (Q, R) so iteration is reproducible.
        /// </summary>
        public static List<Coord> Disc(Coord centre, int radius)
        {
            if (radius < 0)
                throw new ArgumentOutOfRangeException(nameof(radius), radius, "Radius cannot be negative.");

            var results = new List<Coord>(HexCount(radius));

            for (int dq = -radius; dq <= radius; dq++)
            {
                int lower = Math.Max(-radius, -dq - radius);
                int upper = Math.Min(radius, -dq + radius);

                for (int dr = lower; dr <= upper; dr++)
                    results.Add(new Coord(centre.Q + dq, centre.R + dr));
            }

            return results;
        }

        /// <summary>Number of hexes in a filled disc of the given radius.</summary>
        public static int HexCount(int radius)
        {
            if (radius < 0)
                throw new ArgumentOutOfRangeException(nameof(radius), radius, "Radius cannot be negative.");

            return 1 + 3 * radius * (radius + 1);
        }

        // ---- Lines ----------------------------------------------------------

        /// <summary>
        /// Snaps a fractional coordinate to the nearest whole hex, correcting the
        /// axis with the largest rounding error so the cube constraint holds.
        /// </summary>
        public static Coord Round(FractionalCoord fractional)
        {
            float fq = fractional.Q;
            float fr = fractional.R;
            float fs = fractional.S;

            int q = (int)Math.Round(fq);
            int r = (int)Math.Round(fr);
            int s = (int)Math.Round(fs);

            float dq = Math.Abs(q - fq);
            float dr = Math.Abs(r - fr);
            float ds = Math.Abs(s - fs);

            if (dq > dr && dq > ds) q = -r - s;
            else if (dr > ds) r = -q - s;

            return new Coord(q, r);
        }

        /// <summary>
        /// The hexes along the straight line from <paramref name="a"/> to
        /// <paramref name="b"/>, inclusive of both ends. The backbone of
        /// line of sight and of artillery trajectories.
        /// </summary>
        public static List<Coord> Line(Coord a, Coord b)
        {
            int steps = Coord.Distance(a, b);
            var results = new List<Coord>(steps + 1);

            if (steps == 0)
            {
                results.Add(a);
                return results;
            }

            // A tiny nudge keeps the interpolated point off exact hex edges,
            // where rounding would otherwise pick a side arbitrarily and make
            // line-of-sight asymmetric.
            const float Nudge = 1e-6f;

            float aq = a.Q + Nudge;
            float ar = a.R + Nudge;
            float bq = b.Q + Nudge;
            float br = b.R + Nudge;

            float inverseSteps = 1.0f / steps;

            for (int i = 0; i <= steps; i++)
            {
                float t = i * inverseSteps;
                results.Add(Round(new FractionalCoord(aq + (bq - aq) * t, ar + (br - ar) * t)));
            }

            return results;
        }
    }
}
