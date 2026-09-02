using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using BattleChess.Contracts;

namespace BattleChess.Rules.Grid
{
    /// <summary>
    /// The hex board of the grid game: one regiment to a hex, and a hex big
    /// enough to hold one however it is turned.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>[M147].</b> The designer's parallel game, taken as a mode rather than
    /// a fork. Nothing here replaces the continuous simulation - a regiment
    /// still walks, still fights on the way, still answers to the same clock.
    /// What the board takes away is the one freedom that has cost this project
    /// five sessions: <b>where a regiment may stand</b>. It may stand on a hex,
    /// and only one may stand on each. Two bodies cannot overlap because there
    /// is nowhere for the overlap to happen.
    /// </para>
    /// <para>
    /// <b>The cell size is measured off the battle, and [M149] is why it has
    /// to be.</b> It was a constant at first - 50 m, derived from a regiment of
    /// 40 by 20 m, which is what <c>UnitDef.FootprintAt(DefaultStrength)</c>
    /// reports. No battle file fields a regiment at that strength. The Great
    /// Field fields them at two thousand worth, which is <b>80 by 40 m and 89,4
    /// m across the diagonal</b> - so every regiment on the board was nearly two
    /// hexes wide, they overlapped freely, and the muster cheerfully reported
    /// that everybody had a hex of their own. A cell size derived from a number
    /// nobody plays is not derived from anything.
    /// </para>
    /// <para>
    /// So the cell is the widest body that actually stands on <i>this</i> field,
    /// rounded up. A hex contains a body of any orientation when its inscribed
    /// circle - which for a pointy-top hex is its flat-to-flat width - is at
    /// least the body's bounding diameter.
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
        /// <summary>The smallest hex the board will use, in metres.</summary>
        /// <remarks>
        /// A floor rather than a size. It matters only for a battle with nothing
        /// on the field, or one whose regiments are so small that a hex sized to
        /// them would make the board finer than the terrain is authored at.
        /// </remarks>
        public const float SmallestCellMetres = 45f;

        /// <summary>Metres the cell size is rounded up to.</summary>
        /// <remarks>
        /// Five. Enough to keep the number readable in a log and to leave a
        /// little air between a body and the hex holding it, and small enough
        /// that rounding never costs a meaningful share of the board.
        /// </remarks>
        public const float CellSizeStepMetres = 5f;

        private static readonly ConditionalWeakTable<BattleState, Board> Boards =
            new ConditionalWeakTable<BattleState, Board>();

        public readonly HexLayout Layout;
        public readonly MapBounds Bounds;

        /// <summary>Flat-to-flat width of one hex on this board, in metres.</summary>
        public readonly float CellWidth;

        /// <summary>The widest body the cell was sized to hold, as a diameter.</summary>
        /// <remarks>
        /// Kept so a log can state the derivation rather than the result: "90 m
        /// hexes, because the widest regiment here is 89,4 m across" is a
        /// sentence somebody can check.
        /// </remarks>
        public readonly float WidestBody;

        private Board(MapBounds bounds, float cellWidth, float widestBody)
        {
            Bounds = bounds;
            CellWidth = cellWidth;
            WidestBody = widestBody;
            Layout = HexLayout.FromNeighbourDistance(cellWidth, bounds.Min);
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

            return new Board(battle.Terrain.Bounds, CellFor(widest), widest);
        }

        /// <summary>The smallest hex on the size ladder that holds a body this wide.</summary>
        public static float CellFor(float widestBodyMetres)
        {
            float wanted = MathF.Max(SmallestCellMetres, widestBodyMetres);

            return MathF.Ceiling(wanted / CellSizeStepMetres) * CellSizeStepMetres;
        }

        /// <summary>Which hex a world position falls in.</summary>
        public Coord Of(Vec2 world) => Layout.ToCoord(world);

        /// <summary>Where a regiment standing on this hex stands.</summary>
        public Vec2 CentreOf(Coord hex) => Layout.ToWorld(hex);

        /// <summary>Whether this hex's centre is on the map at all.</summary>
        public bool OnBoard(Coord hex) => Bounds.Contains(CentreOf(hex));

        /// <summary>Whether a body of this size fits one hex however it is turned.</summary>
        public bool Holds(Footprint footprint) =>
            2f * footprint.BoundingRadius <= CellWidth + 1e-3f;

        /// <summary>How many hexes across the shorter side of the field is.</summary>
        /// <remarks>
        /// The number that decides whether there is any manoeuvre in the game: a
        /// regiment that crosses this in a few turns cannot be gone round.
        /// </remarks>
        public float ShortSideInHexes => MathF.Min(Bounds.Width, Bounds.Height) / CellWidth;

        /// <summary>
        /// How fast the going is on a hex, as a multiple of open ground, or
        /// zero where this movement type cannot go at all.
        /// </summary>
        /// <remarks>
        /// Sampled at the centre and nowhere else, which is the whole point of
        /// putting a game on a board: a hex is one kind of ground, and a
        /// regiment standing on it is standing on that ground. The continuous
        /// game samples a whole rectangle because a rectangle can straddle a
        /// shoreline. A hex cannot straddle anything.
        /// </remarks>
        public float GoingOn(BattleState battle, Coord hex, MovementType moving)
        {
            if (!OnBoard(hex)) return 0f;

            return battle.Movement.SpeedMultiplier(battle.Terrain.At(CentreOf(hex)), moving);
        }

        /// <summary>Who is standing on each hex, read off the units themselves.</summary>
        public Dictionary<Coord, UnitId> WhoIsWhere(BattleState battle)
        {
            if (battle == null) throw new ArgumentNullException(nameof(battle));

            var standing = new Dictionary<Coord, UnitId>();

            foreach (UnitInstance unit in battle.UnitsOnField())
                standing[Of(unit.Position)] = unit.Id;

            return standing;
        }

        /// <summary>
        /// The nearest hex to <paramref name="wanted"/> this regiment could
        /// stand on, searched outward ring by ring.
        /// </summary>
        /// <remarks>
        /// Used when two regiments muster into the same hex, and when an order
        /// is given to a hex somebody already holds. Rings rather than a search,
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
                foreach (Coord hex in HexMath.Ring(wanted, radius))
                {
                    if (taken.TryGetValue(hex, out UnitId who) && who != unit.Id) continue;
                    if (GoingOn(battle, hex, unit.Def.Movement) <= 0f) continue;

                    free = hex;
                    return true;
                }
            }

            free = wanted;
            return false;
        }

        /// <summary>
        /// Where the six board facings sit, in degrees: the offset from a hex
        /// bearing, and the step between them.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Thirty, and [M150] is the whole argument.</b> A regiment on the
        /// board faces one of six ways - that is what keeps a flank a flank -
        /// but <i>which</i> six is not free. A rectangle's frontage runs
        /// perpendicular to its facing, and the perpendicular of a hex bearing
        /// is never a hex bearing, because the six are multiples of sixty and
        /// ninety plus a multiple of sixty is not one of them.
        /// </para>
        /// <para>
        /// So a hex board can align <b>marching</b> with the grid or <b>lines</b>
        /// with it, never both, and the first draft picked marching without
        /// noticing there was a choice. Measured on the Great Field, four 80 m
        /// regiments meant to stand shoulder to shoulder came out 77,9 m apart
        /// with <b>45 m of stagger between each and the next</b> - a staircase,
        /// not a line. Facing the corner instead of the edge puts frontage on a
        /// hex axis and the same four measure <b>90,0 m apart with no stagger at
        /// all</b>: a true line with ten metres of air in it.
        /// </para>
        /// <para>
        /// <b>What it costs.</b> Straight ahead is no longer a hex step, so a
        /// march to the front weaves between the two bearings either side of the
        /// facing. That is cosmetic rather than structural: a regiment already
        /// turns onto each leg as it walks it, so nothing crabs - it only
        /// settles back onto its ordered front on arrival.
        /// </para>
        /// </remarks>
        public const float FacingOffsetDegrees = 30f;

        /// <inheritdoc cref=FacingOffsetDegrees/>
        public const float DegreesBetweenFacings = 360f / HexMath.DirectionCount;

        /// <summary>The nearest of the six board facings to a free bearing.</summary>
        /// <remarks>
        /// Rounded rather than truncated, so a bearing is never moved by more
        /// than half a step.
        /// </remarks>
        public static Facing Snap(Facing free)
        {
            float step = MathF.Round(
                (free.Degrees - FacingOffsetDegrees) / DegreesBetweenFacings);

            return Facing.FromDegrees(FacingOffsetDegrees + step * DegreesBetweenFacings);
        }

        /// <summary>Whether a bearing is one of the six a regiment may hold.</summary>
        public static bool IsABoardFacing(Facing facing, float toleranceDegrees = 0.01f) =>
            Facing.AbsoluteDelta(facing, Snap(facing)) * 180f / MathF.PI <= toleranceDegrees;

        /// <summary>
        /// The hex axis a regiment's frontage lies along, which is the axis a
        /// line of regiments is drawn up on.
        /// </summary>
        /// <remarks>
        /// The point of <see cref=FacingOffsetDegrees/>, made available rather
        /// than merely true: given a regiment, this is the direction to step to
        /// put the next one at its shoulder.
        /// </remarks>
        public static HexDirection ShoulderDirectionOf(Facing snapped)
        {
            float alongTheLine = snapped.Degrees + 90f;

            int step = (int)MathF.Round(alongTheLine / DegreesBetweenFacings) % HexMath.DirectionCount;

            if (step < 0) step += HexMath.DirectionCount;

            return (HexDirection)step;
        }

        public override string ToString() =>
            $"Board({CellWidth:0} m hexes, sized to a {WidestBody:0.0} m regiment, " +
            $"about {Bounds.Width / CellWidth:0} x {Bounds.Height / (Layout.CellHeight * 0.75f):0} over {Bounds})";
    }
}
