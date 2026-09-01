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
    /// <b>The cell size is derived and not chosen.</b> Every regiment collides
    /// as the same rectangle - 40 m of frontage by 20 m of depth, the equal
    /// ground rule - whose bounding circle is 44,7 m across. A pointy-top hex
    /// contains a circle of that diameter once its flat-to-flat width is as
    /// wide, and <see cref="CellWidthMetres"/> is 50, which clears it with five
    /// metres to spare. Nothing about the board is a taste.
    /// </para>
    /// <para>
    /// <b>What falls out of it.</b> The Great Field is 1800 by 2400 m, so the
    /// board is about 36 by 42 hexes. A turn is sixty battle seconds, so a
    /// regiment's allowance is its pace times sixty over fifty: artillery 1,6
    /// hexes, foot 1,9, horse archers 5,0, cavalry 5,7, scouts 6,6. A three-fold
    /// spread and twenty-odd turns to walk the length of the map - playable
    /// numbers arrived at by dividing rather than by tuning.
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
        /// <summary>Flat-to-flat width of one hex, in metres. See the remarks.</summary>
        public const float CellWidthMetres = 50f;

        /// <summary>
        /// The widest body the cell size is sized to hold, as a diameter.
        /// </summary>
        /// <remarks>
        /// Stated so the derivation can be checked rather than believed. A
        /// regiment 40 by 20 has a half-diagonal of root(20 squared + 10
        /// squared) = 22,36 m, so its bounding circle is 44,72 across, and the
        /// hex's inscribed circle is <see cref="CellWidthMetres"/> across. If
        /// the equal ground rectangle ever grows past this, <see cref="Holds"/>
        /// says no rather than the board quietly permitting an overlap.
        /// </remarks>
        public const float HoldsBodiesUpToMetres = CellWidthMetres;

        private static readonly ConditionalWeakTable<BattleState, Board> Boards =
            new ConditionalWeakTable<BattleState, Board>();

        public readonly HexLayout Layout;
        public readonly MapBounds Bounds;

        private Board(MapBounds bounds)
        {
            Bounds = bounds;
            Layout = HexLayout.FromNeighbourDistance(CellWidthMetres, bounds.Min);
        }

        /// <summary>The board a battle is played on.</summary>
        /// <remarks>
        /// Cached against the battle, since the layout depends only on the map
        /// and a battle's map does not move.
        /// </remarks>
        public static Board For(BattleState battle)
        {
            if (battle == null) throw new ArgumentNullException(nameof(battle));

            return Boards.GetValue(battle, b => new Board(b.Terrain.Bounds));
        }

        /// <summary>Which hex a world position falls in.</summary>
        public Coord Of(Vec2 world) => Layout.ToCoord(world);

        /// <summary>Where a regiment standing on this hex stands.</summary>
        public Vec2 CentreOf(Coord hex) => Layout.ToWorld(hex);

        /// <summary>Whether this hex's centre is on the map at all.</summary>
        public bool OnBoard(Coord hex) => Bounds.Contains(CentreOf(hex));

        /// <summary>Whether a body of this size fits one hex however it is turned.</summary>
        public static bool Holds(Footprint footprint) =>
            2f * footprint.BoundingRadius <= HoldsBodiesUpToMetres + 1e-3f;

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

        /// <summary>The nearest of the six hex bearings to a free facing.</summary>
        /// <remarks>
        /// On the board a regiment faces one of six ways, which is what keeps a
        /// flank a flank: "degrees off the front" becomes a whole number of
        /// sixty-degree steps, and a player can see at a glance which side of a
        /// regiment they stand on. Rounded rather than truncated, so a bearing
        /// is never moved by more than thirty degrees.
        /// </remarks>
        public static Facing Snap(Facing free)
        {
            int step = (int)MathF.Round(free.Degrees / 60f);

            return Facing.FromDegrees(step * 60f);
        }

        /// <summary>Which of the six a snapped facing is.</summary>
        public static HexDirection DirectionOf(Facing snapped)
        {
            int step = (int)MathF.Round(snapped.Degrees / 60f) % HexMath.DirectionCount;

            if (step < 0) step += HexMath.DirectionCount;

            return (HexDirection)step;
        }

        public override string ToString() =>
            $"Board({CellWidthMetres:0} m hexes over {Bounds})";
    }
}
