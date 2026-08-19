using System;
using BattleChess.Contracts;

namespace BattleChess.Rules
{
    /// <summary>
    /// Where on this field a given way of moving cannot go, arranged so that
    /// "is any of it in this rectangle" is four array reads.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Measured, on the Great Field.</b> Terrain was already known to be
    /// about three quarters of what a leg costs to price — it is a field
    /// rather than a shape, so it cannot be swept, only sampled, and
    /// <see cref="BattleState.FormationFits"/> tests the whole footprint over a
    /// grid of points every ten metres along the leg. On the old map that was
    /// tolerable. On a map 1 800 m by 2 400 m, with legs running hundreds of
    /// metres and regiments 229 m across their front, one leg is well over a
    /// thousand terrain lookups, and the default planner came to <b>61
    /// microseconds a leg</b> against the six this project had measured before.
    /// </para>
    /// <para>
    /// Almost all of that work answers "no" on ground that is uniformly open,
    /// which is most of any battlefield. So the question is turned around:
    /// instead of asking every point on the body whether it may stand, ask
    /// once whether the rectangle the whole leg sweeps contains any impassable
    /// ground at all. A summed-area table answers that in constant time
    /// whatever the size of the rectangle, and when the answer is no — the
    /// common case — the entire sampling loop is skipped.
    /// </para>
    /// <para>
    /// <b>This does not change any answer.</b> When the table reports
    /// impassable ground anywhere near the leg, the original check runs
    /// unaltered and settles it. The early-out only fires where there is
    /// provably nothing to find.
    /// </para>
    /// <para>
    /// <b>On resolution, and being honest about it.</b>
    /// <see cref="ITerrainMap"/> is deliberately a field with no grid of its
    /// own — how it stores an answer is nobody's business — so this samples it
    /// at a resolution it picks, exactly as the interface's own remarks say a
    /// pathfinder should. Sampling can in principle miss something finer than
    /// the spacing. That is not a new risk here: the check being short-cut is
    /// itself sampled, at the same spacing, so the table is neither more nor
    /// less exact than the thing it stands in for. A cell counts as impassable
    /// if <i>any</i> of its samples is, which is the conservative direction —
    /// doubt costs a fallback, never a wrong yes.
    /// </para>
    /// </remarks>
    public sealed class PassableGround
    {
        /// <summary>
        /// How finely the field is sampled, in metres — the same spacing
        /// <see cref="BattleState.FormationFits"/> tests a footprint at.
        /// </summary>
        public const float SampleSpacingMetres = 12.5f;

        private readonly MapBounds _bounds;
        private readonly int _columns;
        private readonly int _rows;

        /// <summary>
        /// Impassable cells counted over every rectangle with a corner at the
        /// origin, so any rectangle's own count is four reads and three sums.
        /// </summary>
        private readonly int[] _blockedUpTo;

        private PassableGround(MapBounds bounds, int columns, int rows, int[] blockedUpTo)
        {
            _bounds = bounds;
            _columns = columns;
            _rows = rows;
            _blockedUpTo = blockedUpTo;
        }

        /// <summary>Total cells this field has that the given movement cannot enter.</summary>
        public int BlockedCells => _blockedUpTo[_blockedUpTo.Length - 1];

        public static PassableGround Build(ITerrainMap terrain, IMovementModel movement, MovementType moving)
        {
            if (terrain == null) throw new ArgumentNullException(nameof(terrain));
            if (movement == null) throw new ArgumentNullException(nameof(movement));

            MapBounds bounds = terrain.Bounds;

            int columns = Math.Max(1, (int)MathF.Ceiling(bounds.Width / SampleSpacingMetres));
            int rows = Math.Max(1, (int)MathF.Ceiling(bounds.Height / SampleSpacingMetres));

            // One row and column of zeroes along the top and left, so a query
            // never has to special-case a rectangle that starts at the edge.
            var sums = new int[(columns + 1) * (rows + 1)];

            for (int row = 0; row < rows; row++)
            {
                for (int column = 0; column < columns; column++)
                {
                    var at = new Vec2(
                        bounds.Min.X + (column + 0.5f) * SampleSpacingMetres,
                        bounds.Min.Y + (row + 0.5f) * SampleSpacingMetres);

                    bool blocked = !bounds.Contains(at)
                                   || movement.SpeedMultiplier(terrain.At(at), moving) <= 0f;

                    sums[(row + 1) * (columns + 1) + column + 1] =
                        (blocked ? 1 : 0)
                        + sums[row * (columns + 1) + column + 1]
                        + sums[(row + 1) * (columns + 1) + column]
                        - sums[row * (columns + 1) + column];
                }
            }

            return new PassableGround(bounds, columns, rows, sums);
        }

        /// <summary>
        /// Whether everything inside the given rectangle is ground this
        /// movement can enter — and inside the field at all.
        /// </summary>
        /// <remarks>
        /// A rectangle reaching off the map answers <c>false</c> rather than
        /// clipping. Off the map is ground nobody can stand on, and that is
        /// also what keeps a formation from overhanging the edge, so the
        /// caller's own check has to run.
        /// </remarks>
        public bool NothingInTheWay(Vec2 min, Vec2 max)
        {
            using var _profile = PlanningProfile.Measure(PlanningProfile.Step.PassableTable);

            if (min.X < _bounds.Min.X || min.Y < _bounds.Min.Y ||
                max.X > _bounds.Max.X || max.Y > _bounds.Max.Y)
                return false;

            int left = Cell(min.X - _bounds.Min.X, _columns);
            int right = Cell(max.X - _bounds.Min.X, _columns);
            int bottom = Cell(min.Y - _bounds.Min.Y, _rows);
            int top = Cell(max.Y - _bounds.Min.Y, _rows);

            int stride = _columns + 1;

            int blocked = _blockedUpTo[(top + 1) * stride + right + 1]
                        - _blockedUpTo[bottom * stride + right + 1]
                        - _blockedUpTo[(top + 1) * stride + left]
                        + _blockedUpTo[bottom * stride + left];

            return blocked == 0;
        }

        private static int Cell(float offset, int count) =>
            Math.Clamp((int)MathF.Floor(offset / SampleSpacingMetres), 0, count - 1);
    }
}
