using System;
using System.Collections.Generic;
using BattleChess.Contracts;

namespace BattleChess.Rules
{
    /// <summary>
    /// Where each regiment of a selection is sent when the selection is not a
    /// wing: everybody to the same place, as close as each of them can get
    /// [M164].
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The other half of <see cref="WingFormation"/>.</b> A wing keeps the
    /// shape it is standing in, because a line dragged fifty metres forward is
    /// still that line. A selection is not a line - it is several regiments
    /// given one order at one time - and translating its shape rigidly is what
    /// sent seven spearmen in a column to seven places nobody could stand on.
    /// The designer's words for what should happen instead: they just try to get
    /// as close as they can.
    /// </para>
    /// <para>
    /// <b>Nearest picks first.</b> The regiment already closest to the click
    /// reserves the best place, then the next, and so on outward. That is the
    /// designer's rule, and it is also the only ordering that does not shuffle:
    /// rank by anything else and a far regiment reserves the middle while a near
    /// one walks round it.
    /// </para>
    /// <para>
    /// <b>Every place is decided before a single route is asked for</b>, in one
    /// pass, in that order, with each answer added to what the next regiment
    /// must avoid - the same discipline <see cref="WingFormation"/> keeps, and
    /// for the same reason. Planning a selection in parallel against one
    /// snapshot is what made them collide.
    /// </para>
    /// </remarks>
    public static class Gathering
    {
        /// <summary>Metres of air kept between two regiments of a gathering.</summary>
        /// <remarks>
        /// The same two metres <see cref="WingFormation"/> keeps, and it means
        /// the same thing: clearance, not spacing. How close a rally ought to
        /// stand is a formation question, and this is not the formation.
        /// </remarks>
        public const float ElbowRoomMetres = 2f;

        /// <summary>Places tried before a regiment is sent where it was pointed to fail there.</summary>
        /// <remarks>
        /// Generous, because unlike a wing's nudge this search has no natural
        /// ceiling - the fortieth regiment of a gathering genuinely does belong a
        /// long way out. At the sampling below that reaches about eleven
        /// frontages from the click, which holds well over a hundred regiments.
        /// </remarks>
        public const int LooksBeforeGivingUp = 512;

        /// <summary>
        /// A place apiece, packed outward from <paramref name="at"/>, nearest
        /// regiment first.
        /// </summary>
        /// <param name="at">Where the player clicked. Everybody is aiming at this one point.</param>
        /// <param name="front">
        /// The front they will arrive on, if the order carried one. Which ground
        /// a body covers depends on which way it is turned, so the packing has
        /// to be done on the front they will actually stand on.
        /// </param>
        /// <param name="alreadyTaken">
        /// Ground spoken for by an earlier part of the same order - the wings of
        /// a mixed selection, which are rigid and are placed first. Passed in
        /// rather than read off the field because those regiments have not moved
        /// yet: the places are where they are GOING.
        /// </param>
        /// <returns>One place per regiment, in the order they were passed in.</returns>
        public static Vec2[] GatherAt(
            BattleState battle, IReadOnlyList<UnitInstance> gathering, Vec2 at, Facing? front = null,
            IReadOnlyList<OrientedRect>? alreadyTaken = null)
        {
            if (battle == null) throw new ArgumentNullException(nameof(battle));
            if (gathering == null) throw new ArgumentNullException(nameof(gathering));

            var given = new Vec2[gathering.Count];

            if (gathering.Count == 0) return given;

            var inTheGathering = new HashSet<UnitId>();

            foreach (UnitInstance unit in gathering) inTheGathering.Add(unit.Id);

            // Everybody outside holds their ground; the gathering itself is left
            // out, because they are all setting off together and where they
            // stand now is not where they will be.
            var standing = new List<OrientedRect>();

            foreach (UnitInstance unit in battle.UnitsOnField())
                if (!inTheGathering.Contains(unit.Id))
                    standing.Add(unit.Shape);

            // One front for the whole gathering rather than one apiece. Bodies
            // of the same size at the same angle tile; bodies at seventeen
            // different angles leave slivers between them that nothing fits in,
            // and the pack comes out a third wider for no reason the player can
            // see. Where the order carries no front, it is the bearing the
            // gathering is travelling on - which is the way a rally point is
            // approached from, and the way they would end up facing anyway.
            Facing arriving = front ?? BearingOfApproach(gathering, at);

            // Nearest picks first. Sorted through a copy of the indices, so the
            // answer still comes back in the caller's order.
            var order = new int[gathering.Count];
            var away = new float[gathering.Count];

            for (int i = 0; i < gathering.Count; i++)
            {
                order[i] = i;
                away[i] = Vec2.Distance(gathering[i].Position, at);
            }

            Array.Sort(away, order);

            var booked = new List<OrientedRect>();

            if (alreadyTaken != null) booked.AddRange(alreadyTaken);

            for (int k = 0; k < order.Length; k++)
            {
                int i = order[k];
                UnitInstance unit = gathering[i];

                given[i] = NearestFreePlace(battle, unit, at, arriving, standing, booked);

                booked.Add(new OrientedRect(given[i], arriving, Grown(unit)));
            }

            return given;
        }

        /// <summary>
        /// Where several whole bodies should put their centres, packed around
        /// one point, nearest body first. [M164]
        /// </summary>
        /// <remarks>
        /// <para>
        /// The designer's rule for bound armies, said for more than one of them
        /// at a time: each selects its centre and tries to get that centre as
        /// close as it can to where the click landed. With one bound wing the
        /// answer is the click. With two it cannot be, and aiming both at it
        /// puts one column through the other.
        /// </para>
        /// <para>
        /// Circles rather than the wings' real outlines, on their bounding
        /// radius. A wing is not a convex block - it is a scatter of rectangles
        /// with air between them - so an exact test would let two wings
        /// interleave, which is worse than leaving a little room unused.
        /// </para>
        /// </remarks>
        /// <param name="at">Where the player clicked.</param>
        /// <param name="centres">Where each body stands now, one per body.</param>
        /// <param name="radii">How far each body reaches from its own centre, one per body.</param>
        /// <returns>A centre per body, in the order they were passed in.</returns>
        public static Vec2[] PlaceBodiesAt(
            Vec2 at, IReadOnlyList<Vec2> centres, IReadOnlyList<float> radii)
        {
            if (centres == null) throw new ArgumentNullException(nameof(centres));
            if (radii == null) throw new ArgumentNullException(nameof(radii));

            if (radii.Count != centres.Count)
                throw new ArgumentException("One radius per body.", nameof(radii));

            var given = new Vec2[centres.Count];

            if (centres.Count == 0) return given;

            var order = new int[centres.Count];
            var away = new float[centres.Count];

            for (int i = 0; i < centres.Count; i++)
            {
                order[i] = i;
                away[i] = Vec2.Distance(centres[i], at);
            }

            Array.Sort(away, order);

            var placed = new List<int>();

            for (int k = 0; k < order.Length; k++)
            {
                int i = order[k];

                // The click itself unless something is found, so a body that
                // runs out of looks stands where it was sent and the planner
                // says so. Left unset it would be Vec2.Zero, which is the corner
                // of the map and would read as a wing teleporting.
                given[i] = at;

                // The nearest body gets the click itself, always. Everything
                // after it is searched outward from there.
                float step = radii[i] * 0.5f;

                // Outward towards where this body already stands, before
                // anywhere else. Two columns either side of a click should end
                // up either side of it; a spiral starting at an arbitrary angle
                // would as happily send the right-hand one across the front of
                // the left, which is a whole column crossing a whole column.
                float home = MathF.Atan2(centres[i].Y - at.Y, centres[i].X - at.X);

                for (int look = 0; look <= LooksBeforeGivingUp; look++)
                {
                    float turn = home + (look - 1) * 2.39996323f;
                    float reach = step * MathF.Sqrt(look);

                    Vec2 place = look == 0
                        ? at
                        : new Vec2(at.X + MathF.Cos(turn) * reach, at.Y + MathF.Sin(turn) * reach);

                    bool clear = true;

                    for (int j = 0; j < placed.Count && clear; j++)
                    {
                        int was = placed[j];
                        clear = Vec2.Distance(place, given[was]) >= radii[i] + radii[was];
                    }

                    if (!clear) continue;

                    given[i] = place;
                    break;
                }

                placed.Add(i);
            }

            return given;
        }

        /// <summary>
        /// The way the gathering is travelling: from where it stands, to where
        /// it was sent.
        /// </summary>
        /// <remarks>
        /// Falls back to the first regiment's own front when the click lands on
        /// top of the gathering, because a bearing over nought metres is not a
        /// bearing and would come back as due east for everybody.
        /// </remarks>
        private static Facing BearingOfApproach(IReadOnlyList<UnitInstance> gathering, Vec2 at)
        {
            Vec2 origin = Vec2.Zero;

            foreach (UnitInstance unit in gathering) origin += unit.Position;

            origin /= gathering.Count;

            return Vec2.Distance(origin, at) < 1f ? gathering[0].Facing : Facing.Towards(origin, at);
        }

        /// <summary>
        /// The regiment's block with a little air round it, so two answers that
        /// merely touch are not called a clash.
        /// </summary>
        private static Footprint Grown(UnitInstance unit) =>
            new Footprint(
                unit.Footprint.Width + ElbowRoomMetres,
                unit.Footprint.Depth + ElbowRoomMetres);

        /// <summary>
        /// The place nearest <paramref name="at"/> that this regiment's whole
        /// body can stand in, searched outward in rings.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A golden-angle spiral whose radius grows as the square root of the
        /// look. That is the arrangement with constant density per unit area, so
        /// looks are not wasted crowding the middle, and the ring reached at look
        /// N is the ring that holds N places - which is what makes "nearest
        /// first" mean what it says.
        /// </para>
        /// <para>
        /// The step is a quarter of the regiment's own frontage, sampling a place
        /// about every 300 m2 for an 80x40 m block. Fine enough not to step over
        /// a gap that would have held it; coarse enough that a gathering of forty
        /// is a few thousand rectangle tests rather than a few hundred thousand.
        /// </para>
        /// </remarks>
        private static Vec2 NearestFreePlace(
            BattleState battle, UnitInstance unit, Vec2 at, Facing front,
            IReadOnlyList<OrientedRect> standing, IReadOnlyList<OrientedRect> booked)
        {
            Footprint grown = Grown(unit);

            float step = unit.Footprint.Width * 0.25f;

            for (int look = 0; look <= LooksBeforeGivingUp; look++)
            {
                float turn = look * 2.39996323f;
                float reach = step * MathF.Sqrt(look);

                Vec2 place = look == 0
                    ? at
                    : new Vec2(at.X + MathF.Cos(turn) * reach, at.Y + MathF.Sin(turn) * reach);

                var here = new OrientedRect(place, front, grown);

                if (Clear(battle, unit, here, standing, booked)) return place;
            }

            // Nothing free anywhere near. The click itself is returned rather
            // than a compromise nobody asked for: the planner will fail on it and
            // say so, which is a better answer than a regiment quietly standing
            // somewhere it was never sent.
            return at;
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
