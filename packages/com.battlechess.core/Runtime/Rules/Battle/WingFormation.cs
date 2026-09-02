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

            for (int i = 0; i < wing.Count; i++)
            {
                UnitInstance unit = wing[i];

                Facing arriving = front ?? unit.Facing;

                given[i] = NearestClearPlace(
                    battle, unit, wanted[i], arriving, standing, booked);

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
            BattleState battle, UnitInstance unit, Vec2 wanted, Facing front,
            IReadOnlyList<OrientedRect> standing, IReadOnlyList<OrientedRect> booked)
        {
            Footprint grown = Grown(unit);

            float reach = unit.Footprint.Width * NudgedAtMostFrontages;

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
