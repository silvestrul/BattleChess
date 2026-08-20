using System;
using System.Collections.Generic;
using BattleChess.Contracts;

namespace BattleChess.Rules.HybridPlanning
{
    /// <summary>
    /// One legal maneuver a block can make from any state: march a fixed
    /// distance, wheel through a fixed arc, or pivot on the spot.
    /// </summary>
    /// <remarks>
    /// Motion primitives, in the state-lattice sense — a small, fixed set of
    /// local moves, applied identically everywhere on the lattice, so the
    /// search only ever asks "which of these few moves is legal and cheapest
    /// here" rather than reasoning about arbitrary curvature.
    /// </remarks>
    internal readonly struct HybridPrimitive
    {
        /// <summary>Forward travel of this maneuver's own body, in metres.</summary>
        public readonly float Advance;

        /// <summary>Change of bearing this maneuver produces, in radians, anticlockwise.</summary>
        public readonly float TurnRadians;

        /// <summary>
        /// Fixed seconds this maneuver costs, independent of the mover's own
        /// speed — filled in per unit at generation time so faster and slower
        /// blocks share the same primitive shapes but not the same price.
        /// </summary>
        public readonly float Seconds;

        public readonly string Kind;

        public HybridPrimitive(float advance, float turnRadians, float seconds, string kind)
        {
            Advance = advance;
            TurnRadians = turnRadians;
            Seconds = seconds;
            Kind = kind;
        }

        /// <summary>Where this maneuver ends, applied at <paramref name="from"/>.</summary>
        public (Vec2 position, Facing heading) ApplyTo(Vec2 from, Facing heading) =>
            (PoseAt(from, heading, 1f).position, heading.RotatedBy(TurnRadians));

        /// <summary>
        /// The pose a fraction <paramref name="t"/> of the way through this
        /// maneuver.
        /// </summary>
        /// <remarks>
        /// Constant advance at a constant turn rate traces a circular arc,
        /// which has a closed form — so this is exact, not approximated.
        /// It used to step along the chord at the half-turn heading, which
        /// was wrong twice over: the endpoint drifted (~25 cm on the long
        /// wheel), and because the sweep used the same formula, the
        /// collision check was clearing poses the body never actually
        /// occupies. Error grew with turn angle, which is exactly where the
        /// wheel primitives live.
        /// </remarks>
        public (Vec2 position, Facing heading) PoseAt(Vec2 from, Facing heading, float t)
        {
            float turn = TurnRadians * t;
            float advance = Advance * t;
            Facing turned = heading.RotatedBy(turn);

            if (advance == 0f)
                return (from, turned);

            // In the mover's own frame: x along its front, y to its left.
            float alongFront, toLeft;

            if (MathF.Abs(turn) < 1e-4f)
            {
                alongFront = advance;
                toLeft = 0f;
            }
            else
            {
                float radius = advance / turn;
                alongFront = radius * MathF.Sin(turn);
                toLeft = radius * (1f - MathF.Cos(turn));
            }

            Vec2 forward = heading.ToVector();
            Vec2 left = -heading.RightVector();

            return (from + forward * alongFront + left * toLeft, turned);
        }

        /// <summary>
        /// Every point the mover's own body passes through while carrying
        /// out this maneuver, for a collision check that looks at the whole
        /// sweep and not just where it lands.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Spacing is measured against <b>the furthest-travelling part of
        /// the body</b>, not the centre. Two ways that used to go wrong at
        /// once. A pivot advances the centre zero metres, so deriving the
        /// count from advance alone gave it a single sample — the final
        /// heading, nothing in between — while a 25 m half-width formation
        /// swings its outer corner through roughly 8,7 m of arc per pivot
        /// step, unchecked. And on a wheel the centre covers the step while
        /// the corner covers <c>|turn| × circumradius</c>, which for a wide
        /// block is much further, so the spacing promise held only for a
        /// point robot.
        /// </para>
        /// <para>
        /// Still point sampling rather than a true analytic sweep — see
        /// <see cref="HybridBox"/> — so this bounds the gap rather than
        /// closing it.
        /// </para>
        /// </remarks>
        /// <summary>
        /// How many poses <see cref="Sweep"/> would strike, so a caller can walk
        /// them itself with <see cref="PoseAt"/>.
        /// </summary>
        /// <remarks>
        /// <b>Why the caller does the walking.</b> <c>Sweep</c> is an iterator
        /// method, so every <c>foreach</c> over it allocates a compiler-built
        /// object — and the collision check calls it once per primitive, some
        /// sixty thousand times for a single march. That is the same litter
        /// <b>M40</b> found in <c>UnitsOnField</c>, in the one planner M40 never
        /// looked at because it shares no code with the others.
        /// </remarks>
        public int SampleCount(float maxSpacingMetres, float circumradiusMetres)
        {
            float cornerArc = MathF.Abs(TurnRadians) * circumradiusMetres;
            float travelled = MathF.Max(MathF.Abs(Advance), cornerArc);

            return Math.Max(2, (int)MathF.Ceiling(travelled / maxSpacingMetres));
        }

        public IEnumerable<(Vec2 position, Facing heading)> Sweep(
            Vec2 from, Facing heading, float maxSpacingMetres, float circumradiusMetres)
        {
            int samples = SampleCount(maxSpacingMetres, circumradiusMetres);

            for (int i = 1; i <= samples; i++)
                yield return PoseAt(from, heading, (float)i / samples);
        }
    }

    /// <summary>
    /// Builds the fixed primitive set a block chooses from, sized to one
    /// unit's own speed and how fast it wheels.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Everything here is independent of <see cref="MovementSystem"/>'s
    /// costing — no <c>AlignmentPenalty</c>, no <c>PaceWhileInsideItsOwn</c>,
    /// no shared pivot bonus. The numbers below (the wheel slowdown, the
    /// pivot's own rate and its penalty) are this planner's own judgement
    /// calls, chosen to feel right rather than copied, and worth revisiting
    /// once there is a route to look at.
    /// </para>
    /// <para>
    /// Six primitives per state carry the forward repertoire: march, wheel
    /// left, wheel right, at two lengths each — a long step for open ground
    /// and a short one so the lattice can still thread something the long
    /// step would overshoot. Pivoting is its own pair, in place, heavily
    /// priced in time so the search only reaches for it when wheeling
    /// genuinely cannot answer — which is the cap from the design notes,
    /// arrived at by cost rather than by a hard-coded angle.
    /// </para>
    /// <para>
    /// One more: a short step <b>back</b>. Everything above only ever moves
    /// the body forward, which leaves no legal move at all for a mover
    /// planted nose-to-tail against a body directly ahead — marching goes
    /// straight into it, the short curves aren't sharp enough to clear it in
    /// one step, and pivoting about the body's own centre swings the front
    /// corners into it regardless of which way it turns. Confirmed on the
    /// project's own approach-angle gate, which places the mover touching a
    /// body on purpose: every primitive rejected, nothing to expand, at the
    /// very first state. A pace back is the one maneuver a real regiment in
    /// that spot actually has, so it is the one thing here allowed to move
    /// against its own front.
    /// </para>
    /// </remarks>
    internal static class HybridPrimitives
    {
        private const float LongStepMetres = 10f;
        private const float ShortStepMetres = 4f;

        /// <summary>How far a step back reaches, in metres — deliberately short: a hop clear, not a retreat.</summary>
        private const float BackStepMetres = 4f;

        /// <summary>How much slower a block walks while wheeling, as a fraction of marching speed.</summary>
        private const float WheelPaceFraction = 0.6f;

        /// <summary>How much slower a block walks backing up than marching forward.</summary>
        private const float BackPaceFraction = 0.4f;

        /// <summary>How much faster a block turns on the spot than it turns while walking.</summary>
        private const float PivotRateMultiplier = 1.4f;

        /// <summary>
        /// Flat extra time charged for choosing to pivot rather than wheel,
        /// once per pivot step — <b>not</b> per radian, which is what this
        /// used to claim. Over the 20° slice below it works out at about
        /// 4,3 s per radian.
        /// </summary>
        private const float PivotPenaltySeconds = 1.5f;

        /// <summary>The widest turn a regiment will walk through rather than halting to pivot.</summary>
        private static readonly float WalkingCapRadians = RouteSearch.WalkingCapDegrees * (MathF.PI / 180f);

        /// <summary>
        /// The fastest rate anything here ever turns at — shared with the
        /// heuristic (<see cref="HybridObstacleField"/> and the turn-lower-bound
        /// in <see cref="HybridAStarPlanner"/>) so the two can never disagree
        /// about what "as fast as possible" means.
        /// </summary>
        public static float PivotRateRadiansFor(float turnRateDegreesPerSecond) =>
            turnRateDegreesPerSecond * (MathF.PI / 180f) * PivotRateMultiplier;

        /// <summary>How much arc one pivot step turns through — about 20°.</summary>
        public const float PivotSliceRadians = 0.35f;

        /// <summary>
        /// What a change of front of <paramref name="radians"/>, made on the
        /// spot, costs in this planner's own seconds.
        /// </summary>
        /// <remarks>
        /// Priced as the chain of pivot steps the search would have had to
        /// walk to get there, penalty included, rather than as a bare
        /// rate × angle. The analytic shot at the goal turns through an
        /// arbitrary angle in one move, and if it were charged less per
        /// radian than the primitives are, it would win by being cheaper to
        /// <i>price</i> rather than cheaper to walk — and the route the
        /// search returned would cost more than the number attached to it.
        /// </remarks>
        public static float SecondsToPivot(float radians, float turnRateDegreesPerSecond)
        {
            radians = MathF.Abs(radians);
            if (radians <= 1e-4f) return 0f;

            int steps = Math.Max(1, (int)MathF.Ceiling(radians / PivotSliceRadians));
            return radians / PivotRateRadiansFor(turnRateDegreesPerSecond) + steps * PivotPenaltySeconds;
        }

        public static IReadOnlyList<HybridPrimitive> For(float topSpeedMetresPerSecond, float turnRateDegreesPerSecond)
        {
            float turnRateRadians = turnRateDegreesPerSecond * (MathF.PI / 180f);
            float pivotRateRadians = PivotRateRadiansFor(turnRateDegreesPerSecond);

            var set = new List<HybridPrimitive>(10);

            foreach (float step in new[] { LongStepMetres, ShortStepMetres })
            {
                float marchSeconds = step / topSpeedMetresPerSecond;
                set.Add(new HybridPrimitive(step, 0f, marchSeconds, "march"));

                float wheelSeconds = step / (topSpeedMetresPerSecond * WheelPaceFraction);

                // Capped at the same angle the executor refuses to walk past
                // (RouteSearch.WalkingCapDegrees): above it a regiment halts
                // and pivots rather than crabbing round. Uncapped, the turn
                // is just rate × time, and for a slow unit that turns well —
                // archers, at 1,59 m/s and 5°/s — the long step works out at
                // 52° in one primitive, past a cap the walker then enforces
                // by doing something else entirely. Planning a wheel nobody
                // will walk is the same class of mistake as W5 warns about
                // in a log line: the plan has to be costed in the terms the
                // execution actually uses.
                float wheelTurn = MathF.Min(turnRateRadians * wheelSeconds, WalkingCapRadians);

                set.Add(new HybridPrimitive(step, wheelTurn, wheelSeconds, "wheel left"));
                set.Add(new HybridPrimitive(step, -wheelTurn, wheelSeconds, "wheel right"));
            }

            // Pivoting turns through a fixed slice of arc rather than a fixed
            // time, so the search can chain several to reach a large change
            // of front, each one costed and none of them free.
            float pivotSeconds = PivotSliceRadians / pivotRateRadians + PivotPenaltySeconds;

            set.Add(new HybridPrimitive(0f, PivotSliceRadians, pivotSeconds, "pivot left"));
            set.Add(new HybridPrimitive(0f, -PivotSliceRadians, pivotSeconds, "pivot right"));

            float backSeconds = BackStepMetres / (topSpeedMetresPerSecond * BackPaceFraction);
            set.Add(new HybridPrimitive(-BackStepMetres, 0f, backSeconds, "step back"));

            return set;
        }
    }
}
