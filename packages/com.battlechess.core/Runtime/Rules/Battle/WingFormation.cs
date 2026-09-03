using System;
using System.Collections.Generic;
using BattleChess.Contracts;

namespace BattleChess.Rules
{
    /// <summary>
    /// Where each regiment of a wing is sent, in the free-moving game, so that
    /// the wing keeps its shape without fighting itself for ground [M155].
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The continuous twin of <c>Board.FormUpAt</c>.</b> On a board the rigid
    /// translation is exact - everybody sits on a cell centre and one vector
    /// moves them all by the same whole number of cells - and only water and
    /// outsiders spoil it. Off the board there are no cells to be exact about, so
    /// two regiments can be sent to places that overlap by a metre and both
    /// planners will happily route them there.
    /// </para>
    /// <para>
    /// <b>Rigid, approximately, which is what the designer asked for.</b> The
    /// shape drawn is the shape aimed at: every regiment keeps its own offset
    /// from the wing centre. Where a place is unreachable or already spoken for,
    /// that one regiment gives a little - the shape bends around the obstacle
    /// rather than the wing re-forming itself into something nobody ordered.
    /// </para>
    /// <para>
    /// <b>Decided in one pass, in a fixed order, before any route is asked
    /// for.</b> That is the whole of "they should not conflict over where they
    /// are going": the second regiment is answered against the place the first
    /// was actually given, not against the place it was standing when the order
    /// was drawn. Planning a wing in parallel against one snapshot is what made
    /// them collide.
    /// </para>
    /// </remarks>
    public static class WingFormation
    {
        /// <summary>How far a regiment will be nudged from its place, as a share of its own frontage.</summary>
        /// <remarks>
        /// Two frontages. Far enough to get out from behind a regiment standing
        /// squarely in the wanted place, and near enough that a line asked to
        /// form up in an impossible spot comes back visibly bent rather than
        /// scattered across the field - a wing whose shape has been destroyed
        /// should look destroyed, so the designer can see it and pick somewhere
        /// else.
        /// </remarks>
        public const float NudgedAtMostFrontages = 2f;

        /// <summary>Steps taken while searching outward for a free place.</summary>
        public const int LooksBeforeGivingUp = 48;

        /// <summary>Metres of air kept between two regiments of a wing.</summary>
        /// <remarks>
        /// Small on purpose. This is clearance, not spacing: how close two
        /// regiments of a line should stand is a formation question and belongs
        /// to whoever drew the line, not to the thing that stops them standing in
        /// the same place.
        /// </remarks>
        public const float ElbowRoomMetres = 2f;

        /// <summary>
        /// The place each regiment of the wing should march to, keeping the
        /// shape the wing is standing in.
        /// </summary>
        /// <param name="wanted">
        /// The rigid translation - one wanted place per regiment, in the same
        /// order as <paramref name="wing"/>. Passed in rather than worked out
        /// here so that a caller which has already applied a facing, or a board,
        /// keeps the answer it had.
        /// </param>
        /// <param name="front">
        /// The front the wing will arrive on, if it has been given one. Which
        /// ground a body covers depends on which way it is turned, so a wing
        /// ordered to arrive facing north must be checked facing north.
        /// </param>
        public static Vec2[] FormUpAt(
            BattleState battle, IReadOnlyList<UnitInstance> wing, IReadOnlyList<Vec2> wanted,
            Facing? front = null)
        {
            if (battle == null) throw new ArgumentNullException(nameof(battle));
            if (wing == null) throw new ArgumentNullException(nameof(wing));
            if (wanted == null) throw new ArgumentNullException(nameof(wanted));

            if (wanted.Count != wing.Count)
                throw new ArgumentException("One wanted place per regiment.", nameof(wanted));

            var inTheWing = new HashSet<UnitId>();

            foreach (UnitInstance unit in wing) inTheWing.Add(unit.Id);

            // Everybody outside the wing holds their ground. The wing is left
            // out: they are all setting off together, so where they stand now is
            // not where they will be [M152].
            var standing = new List<OrientedRect>();

            foreach (UnitInstance unit in battle.UnitsOnField())
                if (!inTheWing.Contains(unit.Id))
                    standing.Add(unit.Shape);

            var given = new Vec2[wing.Count];
            var booked = new List<OrientedRect>();

            Vec2 along = LineOfTheWing(wanted);

            for (int i = 0; i < wing.Count; i++)
            {
                UnitInstance unit = wing[i];

                Facing arriving = front ?? unit.Facing;

                given[i] = NearestClearPlace(
                    battle, unit, wanted[i], arriving, along, standing, booked);

                booked.Add(new OrientedRect(given[i], arriving, Grown(unit)));
            }

            return given;
        }

        /// <summary>
        /// The regiment's block with a little air round it, so that two answers
        /// which merely touch are not called a clash.
        /// </summary>
        private static Footprint Grown(UnitInstance unit) =>
            new Footprint(
                unit.Footprint.Width + ElbowRoomMetres,
                unit.Footprint.Depth + ElbowRoomMetres);

        /// <summary>
        /// The wanted place, or the nearest one to it this regiment could stand
        /// in, searched outward in a widening spiral.
        /// </summary>
        /// <remarks>
        /// A spiral rather than a ring of fixed radius, because the direction the
        /// obstruction lies in is not known and a wing is usually blocked from
        /// one side - so most of the time the first few looks answer it.
        /// </remarks>
        private static Vec2 NearestClearPlace(
            BattleState battle, UnitInstance unit, Vec2 wanted, Facing front, Vec2 along,
            IReadOnlyList<OrientedRect> standing, IReadOnlyList<OrientedRect> booked)
        {
            Footprint grown = Grown(unit);

            float reach = unit.Footprint.Width * NudgedAtMostFrontages;

            // The place it was actually sent, before any of the giving way
            // below. Tried here rather than left to look 0 of the spiral,
            // because the whole point of what follows is that it only happens
            // when this fails - a wing whose places are all free must come back
            // exactly as it was drawn.
            if (Clear(battle, unit, new OrientedRect(wanted, front, grown), standing, booked))
                return wanted;

            // [M164]. Along the wing's own line before anywhere else. The
            // designer asked for a wing that has to bend to still look a bit
            // organized, and the spiral below cannot give that: it is even in
            // every direction, so a column shoved out of place comes back as a
            // blob rather than as a longer column. A line that gives way along
            // itself is still recognisably the line that was drawn.
            //
            // Tried alternately either way and outward, so this stays a
            // nearest-first search - it is a preference between places the same
            // distance off, not a licence to go further.
            if (!along.IsNearZero)
            {
                float stride = unit.Footprint.Width * 0.25f;

                for (float off = stride; off <= reach; off += stride)
                {
                    for (int side = 0; side < 2; side++)
                    {
                        float step = side == 0 ? off : -off;

                        var beside = new Vec2(wanted.X + along.X * step, wanted.Y + along.Y * step);

                        if (Clear(battle, unit, new OrientedRect(beside, front, grown),
                                  standing, booked))
                            return beside;
                    }
                }
            }

            for (int look = 0; look <= LooksBeforeGivingUp; look++)
            {
                // Golden-angle spiral: even coverage without the corners a square
                // scan puts nearest-first order wrong on.
                float turn = look * 2.39996323f;
                float out_ = reach * MathF.Sqrt(look / (float)LooksBeforeGivingUp);

                Vec2 at = look == 0
                    ? wanted
                    : new Vec2(wanted.X + MathF.Cos(turn) * out_, wanted.Y + MathF.Sin(turn) * out_);

                var here = new OrientedRect(at, front, grown);

                if (Clear(battle, unit, here, standing, booked)) return at;
            }

            // Nothing was free within reach. The wanted place is returned rather
            // than a compromise nobody asked for: the planner will fail on it and
            // say so, which is a better answer than a regiment quietly standing
            // somewhere it was never sent.
            return wanted;
        }

        /// <summary>
        /// The line the wing is drawn up on: the direction between its two
        /// furthest-apart places. [M164]
        /// </summary>
        /// <remarks>
        /// The furthest pair rather than a fitted axis, because it is exact for
        /// the arrangement this actually has to serve - a line or a column - and
        /// because it degrades honestly: a wing standing in a blob has no
        /// furthest pair worth the name, and the direction it returns is
        /// arbitrary, which is the right answer for a shape with no line in it.
        /// Nought vectors mean there is nothing to give way along, and the
        /// spiral takes it from there.
        /// </remarks>
        private static Vec2 LineOfTheWing(IReadOnlyList<Vec2> wanted)
        {
            if (wanted.Count < 2) return Vec2.Zero;

            float widest = 0f;
            Vec2 line = Vec2.Zero;

            for (int i = 0; i < wanted.Count; i++)
            {
                for (int j = i + 1; j < wanted.Count; j++)
                {
                    float apart = Vec2.Distance(wanted[i], wanted[j]);

                    if (apart <= widest) continue;

                    widest = apart;
                    line = wanted[j] - wanted[i];
                }
            }

            return line.Normalised();
        }

        private static bool Clear(
            BattleState battle, UnitInstance unit, in OrientedRect here,
            IReadOnlyList<OrientedRect> standing, IReadOnlyList<OrientedRect> booked)
        {
            if (battle.Movement.SpeedMultiplier(battle.Terrain.At(here.Centre), unit.Def.Movement) <= 0f)
                return false;

            for (int i = 0; i < standing.Count; i++)
                if (OrientedRect.Overlaps(here, standing[i])) return false;

            for (int i = 0; i < booked.Count; i++)
                if (OrientedRect.Overlaps(here, booked[i])) return false;

            return true;
        }
    }
}
