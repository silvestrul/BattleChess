using System;
using System.Collections.Generic;
using BattleChess.Contracts;

namespace BattleChess.Rules
{
    /// <summary>
    /// Pulls a grid-shaped route straight, so units walk a natural line rather
    /// than a visible zigzag between cell centres.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The obvious danger with smoothing is cutting corners: a straight line
    /// between two waypoints can pass through ground the original route
    /// carefully went around. Two rules prevent that.
    /// </para>
    /// <para>
    /// First, a shortcut is only taken if every point along it is passable,
    /// checked by sampling at half the search spacing — fine enough that
    /// nothing narrower than the grid itself can be missed.
    /// </para>
    /// <para>
    /// Second, and less obvious: a shortcut must not be <i>slower</i>. A
    /// straight line across a swamp may be perfectly passable while being far
    /// worse than the road the search chose. So each candidate is costed against
    /// the stretch of route it would replace, and rejected if it is worse. In
    /// open ground a straight line always wins, which is exactly where smoothing
    /// should apply.
    /// </para>
    /// </remarks>
    public static class PathSmoother
    {
        /// <summary>
        /// How far ahead to look for a shortcut. Bounds the work per waypoint;
        /// longer shortcuts still emerge because smoothing runs repeatedly along
        /// the route.
        /// </summary>
        private const int LookaheadWindow = 32;

        /// <summary>
        /// How much worse a shortcut may be and still be accepted. A little
        /// slack absorbs the discretisation error between sampling a straight
        /// line and summing hex steps, without ever allowing a meaningfully
        /// slower route.
        /// </summary>
        private const float CostTolerance = 1.05f;

        /// <summary>
        /// Travel cost of a straight run, in metres of equivalent open ground,
        /// or <see cref="float.PositiveInfinity"/> if anything blocks it.
        /// </summary>
        /// <param name="clearance">
        /// How far to either side of the line must also be clear, in metres.
        /// Without it a route may graze an obstacle's corner exactly, which
        /// looks like a unit clipping through scenery. Later this should be the
        /// unit's own half-width, so a wide regiment keeps a wide berth.
        /// </param>
        public static float SegmentCost(
            Vec2 from,
            Vec2 to,
            ITerrainMap terrain,
            IMovementModel movement,
            MovementType movementType,
            float searchSpacing,
            float clearance = 0f)
        {
            float length = Vec2.Distance(from, to);
            if (length <= Vec2.Epsilon)
                return 0f;

            // Fine enough that no gap the search could resolve slips between two
            // samples, and capped at a metre so a route cannot clip the corner
            // of a lake by grazing between sample points. Sampling can never be
            // exact — a straight line can always cut a corner by less than one
            // step — but a sub-metre clip is beneath anything the simulation or
            // the eye can tell apart.
            float step = MathF.Min(MathF.Max(searchSpacing * 0.5f, 0.25f), 1f);
            int samples = Math.Max(1, (int)MathF.Ceiling(length / step));
            float sampleLength = length / samples;

            Vec2 sideways = (to - from).Normalised().Perpendicular * clearance;
            bool checkSides = clearance > 0f && !sideways.IsNearZero;

            float cost = 0f;

            for (int i = 0; i < samples; i++)
            {
                // Sample at the middle of each stretch, which represents it
                // better than either end and avoids double-counting joins.
                float t = (i + 0.5f) / samples;
                Vec2 point = Vec2.Lerp(from, to, t);

                // The centre must stay on the map: that is where the unit is.
                if (!terrain.Bounds.Contains(point))
                    return float.PositiveInfinity;

                float multiplier = MultiplierAt(point, terrain, movement, movementType);
                if (multiplier <= 0f)
                    return float.PositiveInfinity;

                // The flanks need not. A formation may overhang the edge of the
                // battlefield, so only real terrain blocks to the sides.
                if (checkSides &&
                    (BlockedBeside(point + sideways, terrain, movement, movementType) ||
                     BlockedBeside(point - sideways, terrain, movement, movementType)))
                    return float.PositiveInfinity;

                // Cost comes from the centre line only. The flanking samples
                // decide whether the route is allowed, not what it costs.
                cost += sampleLength / multiplier;
            }

            return cost;
        }

        private static float MultiplierAt(Vec2 point, ITerrainMap terrain, IMovementModel movement, MovementType movementType)
        {
            if (!terrain.Bounds.Contains(point))
                return 0f;

            return movement.SpeedMultiplier(terrain.At(point), movementType);
        }

        /// <summary>
        /// Whether a point beside the line of march blocks the route. Off the
        /// map does not: a formation is allowed to overhang the battlefield's
        /// edge, and only impassable terrain stops it.
        /// </summary>
        private static bool BlockedBeside(Vec2 point, ITerrainMap terrain, IMovementModel movement, MovementType movementType)
        {
            if (!terrain.Bounds.Contains(point))
                return false;

            return movement.SpeedMultiplier(terrain.At(point), movementType) <= 0f;
        }

        public static List<Vec2> Smooth(
            IReadOnlyList<Vec2> points,
            ITerrainMap terrain,
            IMovementModel movement,
            MovementType movementType,
            float searchSpacing,
            float clearance = 0f)
        {
            if (points == null) throw new ArgumentNullException(nameof(points));

            var result = new List<Vec2>();
            if (points.Count == 0) return result;

            result.Add(points[0]);
            if (points.Count <= 2)
            {
                if (points.Count == 2) result.Add(points[1]);
                return result;
            }

            // Cost of the original route up to each point, so the cost of any
            // stretch is one subtraction.
            // Costed without clearance: this is the route the search already
            // proved walkable, and it is only the yardstick a shortcut has to
            // beat. Applying clearance here could make it infinite and let any
            // shortcut through unchallenged.
            var cumulative = new float[points.Count];
            for (int i = 1; i < points.Count; i++)
            {
                cumulative[i] = cumulative[i - 1] +
                    SegmentCost(points[i - 1], points[i], terrain, movement, movementType, searchSpacing);
            }

            int last = points.Count - 1;

            // Try to replace the entire route with one straight line before
            // anything else.
            //
            // Without this, a long journey is straightened only in windows, so a
            // route that could obviously be walked in a single straight line
            // comes out as a chain of slightly-angled chords — while clicking
            // the same direction in short hops produces the straight line the
            // player expected. Same destination, different answer, which is
            // indefensible.
            //
            // The cost comparison still applies, so a straight line across a
            // swamp is rejected in favour of the road the search found. This
            // only ever collapses a route that was already effectively straight.
            float wholeSpan = SegmentCost(points[0], points[last], terrain, movement, movementType, searchSpacing, clearance);

            if (!float.IsInfinity(wholeSpan) && wholeSpan <= cumulative[last] * CostTolerance)
            {
                result.Add(points[last]);
                return result;
            }

            int current = 0;

            while (current < points.Count - 1)
            {
                int chosen = current + 1;
                int furthest = Math.Min(points.Count - 1, current + LookaheadWindow);

                // Try the boldest shortcut first and settle for less only if it
                // fails, so each step removes as many waypoints as it safely can.
                for (int candidate = furthest; candidate >= current + 2; candidate--)
                {
                    float shortcut = SegmentCost(points[current], points[candidate], terrain, movement, movementType, searchSpacing, clearance);
                    if (float.IsInfinity(shortcut)) continue;

                    float original = cumulative[candidate] - cumulative[current];
                    if (float.IsInfinity(original) || shortcut <= original * CostTolerance)
                    {
                        chosen = candidate;
                        break;
                    }
                }

                result.Add(points[chosen]);
                current = chosen;
            }

            return result;
        }
    }
}
