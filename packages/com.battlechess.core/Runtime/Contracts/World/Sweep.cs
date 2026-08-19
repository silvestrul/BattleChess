using System;

namespace BattleChess.Contracts
{
    /// <summary>
    /// Moves a rectangle along a line and reports what it runs into.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The primitive underneath <b>M10</b>. A regiment's real question about
    /// going anywhere is not "which cells are cheapest" but <i>from here, along
    /// this bearing, how far can my body go before it hits something</i> — a
    /// cast, not a search. Asked of the whole rectangle rather than of its
    /// centre, which is <b>M12</b>: a line is only usable if the body sweeps it
    /// clear, and a 40 m regiment planning at 2 m of clearance finds gaps that
    /// do not exist.
    /// </para>
    /// <para>
    /// Deliberately geometry and nothing else. It knows about rectangles and
    /// distances; it does not know what a regiment is, which side it is on, or
    /// whether the thing in the way will move. Those are rules, they live in the
    /// rules assembly, and keeping them out of here is what makes this testable
    /// exhaustively and cheap to reason about.
    /// </para>
    /// </remarks>
    public static class Sweep
    {
        /// <summary>
        /// How close two rectangles have to come before the sweep calls it a
        /// touch, in metres.
        /// </summary>
        /// <remarks>
        /// A hair, and its only job is to stop a body that is already exactly
        /// flush with something from reporting a first touch at some distance
        /// further on because floating point put the two a nanometre apart.
        /// </remarks>
        private const float Touching = 0.001f;

        /// <summary>
        /// How finely the first touch is pinned down, in metres.
        /// </summary>
        /// <remarks>
        /// Ten centimetres, against regiments forty metres wide. Finer buys
        /// nothing a marching body could act on and costs bisection steps.
        /// </remarks>
        private const float CloseEnough = 0.1f;

        /// <summary>
        /// Whether <paramref name="moving"/>, carried <paramref name="travel"/>
        /// without turning, ever meets <paramref name="obstacle"/> — and if so,
        /// how far along it gets first.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Answered in two steps, because they answer two different questions
        /// and the cheap one settles most cases. First: does the swept body
        /// touch the obstacle <i>at all</i>? That is one overlap test against
        /// the hull of where the rectangle starts and where it ends, and for a
        /// march across open ground it says no and there is nothing more to do.
        /// </para>
        /// <para>
        /// Only when something is genuinely in the way is the distance worth
        /// finding, and then it is found by halving rather than by solving.
        /// </para>
        /// <para>
        /// Halving <i>what</i> is the part worth stating, because the obvious
        /// choice is wrong. Asking "is the body overlapping at this distance"
        /// is not something a bisection may be run on: a sweep long enough to
        /// pass an obstacle and come out the far side overlaps in the middle
        /// and not at either end, so a probe past the far side reports clear
        /// and the search throws away the half containing the answer. Measured,
        /// that returned 299.9 m for a block sitting squarely at 80 m.
        /// </para>
        /// <para>
        /// The hull, on the other hand, only ever grows: if the ground covered
        /// on the way to here touches something, so does the ground covered on
        /// the way to anywhere further. So the question halved is "does the
        /// swept region reach it yet", which is monotone by construction, and
        /// the boundary it converges on is the first touch. Solving for the
        /// exact time of impact is faster and is where this would go if casts
        /// ever showed up in a profile; it is also where the sign errors live.
        /// </para>
        /// </remarks>
        /// <param name="distance">
        /// How far the body may travel before touching. Zero if it is already
        /// touching; the full length of <paramref name="travel"/> if it never
        /// touches at all.
        /// </param>
        /// <returns>Whether anything was hit.</returns>
        public static bool FirstTouch(
            in OrientedRect moving, Vec2 travel, in OrientedRect obstacle, out float distance)
        {
            PlanningProfile.Tally(PlanningProfile.Step.SweepTest);

            float length = travel.Length;

            // Standing still. Only the position it is already in can be in the
            // way, and a body cannot travel into something it is inside.
            if (length <= Touching)
            {
                bool hereAndNow = OrientedRect.Overlaps(moving, obstacle);
                distance = 0f;
                return hereAndNow;
            }

            distance = length;

            if (!SweptHullTouches(moving, travel, obstacle)) return false;

            // Already inside it before setting off. Nothing to bisect: the
            // answer is that it may not move at all.
            if (OrientedRect.Overlaps(moving, obstacle))
            {
                distance = 0f;
                return true;
            }

            Vec2 direction = travel / length;

            float clear = 0f;
            float touched = length;

            while (touched - clear > CloseEnough)
            {
                float middle = (clear + touched) * 0.5f;

                if (SweptHullTouches(moving, direction * middle, obstacle))
                    touched = middle;
                else
                    clear = middle;
            }

            distance = clear;
            return true;
        }

        /// <summary>
        /// Whether <paramref name="moving"/>, carried <paramref name="travel"/>
        /// without turning, ever meets <paramref name="obstacle"/> — without
        /// working out where.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <see cref="FirstTouch"/> answers this on the way to the distance and
        /// then spends about ten more separating-axis tests halving down to a
        /// tenth of a metre. Every clearance check in the planner threw that
        /// distance away: a leg that meets anything is refused, and where along
        /// the leg it was met changes nothing about the refusal.
        /// </para>
        /// <para>
        /// Measured on a hundred and sixty regiments planning at once: 828,643
        /// sweeps ran 5,410,144 axis tests, of which 4,581,501 — <b>eighty-five
        /// per cent</b> — were halving steps refining an answer nobody read.
        /// </para>
        /// </remarks>
        public static bool Touches(in OrientedRect moving, Vec2 travel, in OrientedRect obstacle)
        {
            PlanningProfile.Tally(PlanningProfile.Step.SweepTest);

            if (travel.Length <= Touching)
                return OrientedRect.Overlaps(moving, obstacle);

            return SweptHullTouches(moving, travel, obstacle);
        }

        /// <summary>
        /// The gap left between a body and an obstacle as it goes past, at its
        /// narrowest.
        /// </summary>
        /// <remarks>
        /// What <b>M17</b> needs to tell a good line from a merely legal one.
        /// Two bearings that both get through are not equally good if one of
        /// them shaves a friendly regiment by half a metre, and a line that
        /// grazes a corner is one that fails on the first metre of drift —
        /// which is the whole of why <b>M19a</b> asks for room to spare when
        /// aiming at a tangent.
        /// </remarks>
        public static float NarrowestGap(
            in OrientedRect moving, Vec2 travel, in OrientedRect obstacle, int samples = 16)
        {
            if (samples < 2) samples = 2;

            float length = travel.Length;
            if (length <= Touching) return OrientedRect.GapBetween(moving, obstacle);

            Vec2 direction = travel / length;
            float narrowest = float.MaxValue;

            for (int i = 0; i <= samples; i++)
            {
                float along = length * i / samples;
                float gap = OrientedRect.GapBetween(At(moving, direction, along), obstacle);

                if (gap < narrowest) narrowest = gap;
                if (narrowest <= 0f) return 0f;
            }

            return narrowest;
        }

        private static OrientedRect At(in OrientedRect rect, Vec2 direction, float along) =>
            new OrientedRect(rect.Centre + direction * along, rect.Facing, rect.Footprint);

        /// <summary>
        /// Whether the ground a rectangle covers on its way meets an obstacle,
        /// ignoring where along the way that happens.
        /// </summary>
        /// <remarks>
        /// Separating-axis, over the hull of the start and end positions. The
        /// hull of two rectangles that differ only by a translation is a
        /// hexagon, and a hexagon and a rectangle are separated only if they are
        /// separated on one of five axes: the two of each rectangle, and the one
        /// square to the travel. Test all five, and a single gap on any of them
        /// means the two never meet.
        /// </remarks>
        private static bool SweptHullTouches(in OrientedRect moving, Vec2 travel, in OrientedRect obstacle)
        {
            Span<Vec2> axes = stackalloc Vec2[5];

            axes[0] = moving.Right;
            axes[1] = moving.Forward;
            axes[2] = obstacle.Right;
            axes[3] = obstacle.Forward;

            // Square to the line of travel: the axis on which a body slipping
            // cleanly past the side of something is plainly clear of it, and the
            // only one the four rectangle axes can miss.
            axes[4] = new Vec2(-travel.Y, travel.X);

            for (int i = 0; i < axes.Length; i++)
            {
                Vec2 axis = axes[i];
                if (axis.IsNearZero) continue;

                axis = axis.Normalised();

                float movingCentre = Vec2.Dot(moving.Centre, axis);
                float shifted = movingCentre + Vec2.Dot(travel, axis);

                float reach = moving.ProjectedRadius(axis);

                // The swept body spans from wherever it starts to wherever it
                // ends, widened by its own half-extent at both ends.
                float low = MathF.Min(movingCentre, shifted) - reach;
                float high = MathF.Max(movingCentre, shifted) + reach;

                float theirCentre = Vec2.Dot(obstacle.Centre, axis);
                float theirReach = obstacle.ProjectedRadius(axis);

                if (theirCentre + theirReach < low - Touching) return false;
                if (theirCentre - theirReach > high + Touching) return false;
            }

            return true;
        }
    }
}
