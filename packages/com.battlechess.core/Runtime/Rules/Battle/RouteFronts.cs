using System;
using System.Collections.Generic;
using BattleChess.Contracts;

namespace BattleChess.Rules
{
    /// <summary>
    /// Gives every leg the front of the march it belongs to, rather than the
    /// front its own direction implies.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>M99, and the designer's words are the rule:</b> <i>"it should face
    /// the direction it moves in the most even if it has to turn back after"</i>.
    /// <see cref="Marching.AlongTheLine"/> already faces the way it travels -
    /// that half was never wrong. What was wrong is where it reads <i>the way
    /// it travels</i> from: the first waypoint, whatever that waypoint is.
    /// </para>
    /// <para>
    /// Measured over 280 orders on the four bench fields. <b>62 open with a
    /// wheel of more than 90 degrees</b> and 40 with more than 150, and on
    /// nineteen of the twenty where that wheel is plainly wrong the regiment is
    /// <i>already</i> within 1 to 26 degrees of where the march is going
    /// overall. The worst is a <b>107 degree wheel to walk one metre</b>, on a
    /// march of 1494 m whose bearing is 2 degrees off the front it was standing
    /// on. Then it turns back. That is not a regiment facing its line of march;
    /// it is a regiment obeying a one-metre lie about where the march goes.
    /// </para>
    /// <para>
    /// <b>Why the stub is not simply removed instead.</b> It is a real
    /// waypoint. A one-metre sidestep exists precisely because the straight
    /// line is refused, so the long cast in <see cref="RouteSmoothing"/> fails
    /// and correctly keeps the point. The route is right and only the front on
    /// it is wrong, which is why this pass moves <b>no waypoint whatsoever</b>.
    /// </para>
    /// <para>
    /// <b>And why not a cap on the wheel instead.</b> That was considered and
    /// dropped, by the designer and by the measurement both. A cap makes the
    /// regiment walk with its frontage across the line of march, which the
    /// <b>M24</b> note on <see cref="Marching.AlongTheLine"/> records as the
    /// arrangement that broke routing outright: held broadside a body sweeps
    /// its full width, rung one and rung two both failed, and it shouldered
    /// through its own. The front is an argument to
    /// <see cref="Marching.IsClearLine"/>, so capping it would change which
    /// routes exist. This changes none.
    /// </para>
    /// <para>
    /// <b>Turning in place is already paid for.</b> The designer asked that a
    /// regiment be allowed to halt and come round rather than wheel while
    /// walking. It already may: <c>MovementSystem.PivotBonusWhileHalted</c>
    /// prices a halted pivot, and <b>M30</b> holds the ground and keeps turning
    /// on the step that would have hit. Nothing here is needed for that.
    /// </para>
    /// </remarks>
    internal static class RouteFronts
    {
        /// <summary>Whether the pass runs at all. A measurement lever.</summary>
        internal static bool FrontFromTheMarch = true;

        /// <summary>
        /// How short a leg has to be, against the longest leg of the same
        /// route, before it stops counting as a direction of march.
        /// </summary>
        /// <remarks>
        /// A tenth, because that is where the measurement puts the join: of the
        /// 62 opening wheels over 90 degrees, <b>56 have a first leg under a
        /// tenth of the whole route</b>. This is the guard against calling a
        /// leg a stub on a route that is all stubs; <see cref="StubLegBodies"/>
        /// is what actually decides.
        /// </remarks>
        internal static float StubLegFraction = 0.1f;

        /// <summary>
        /// How many of its own body-lengths a regiment may cover before the leg
        /// counts as a direction of march.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>The threshold has to be absolute, and this was measured the hard
        /// way.</b> Sized by the fraction alone it caught legs of 39 m to 120 m
        /// on routes of 1800 m to 2800 m - stubs by share, and nothing like
        /// stubs on the ground. Holding the march front across one of those
        /// means crabbing it, and crabbing is charged at
        /// <c>MovementSystem.AlignmentPenalty</c>: total turning fell 13% to
        /// 30% and <b>the march clock went up 1,0% to 1,6%</b>. Trading walking
        /// time for turning time is not what M99 is for, and a hundred-metre
        /// sidestep held square is the same thing the designer called weird.
        /// </para>
        /// <para>
        /// So the measure is the regiment itself. <b>A leg it cannot clear its
        /// own length on is not a direction of march</b> - it is a shuffle, and
        /// a body that deep sidestepping less than its own depth never leaves
        /// the ground it was standing on. That is a statement about the game
        /// rather than a number tuned against a bench, which is why it is the
        /// one that decides.
        /// </para>
        /// <para>
        /// <b>Two, and the sweep is why.</b> The trade is monotone and there is
        /// no free lunch in it - every degree not turned is walked crabwise
        /// instead. Per field, against the pass off:
        /// </para>
        /// <para>
        /// <c>1 body  (35 m)   turning -3,5% to -4,7%   clock free      crabbed 23-62 m</c><br/>
        /// <c>2 bodies (70 m)  turning -10% to -17%     clock +0,2%     crabbed 713-834 m</c><br/>
        /// <c>4 bodies (141 m) turning -20% to -25%     clock +0,8-1,5% crabbed 1522-2216 m</c>
        /// </para>
        /// <para>
        /// One body is nearly free and nearly nothing: it leaves 23 of the 23
        /// opening wheels over 90 degrees on the Crucible exactly where they
        /// were. Four buys most of the turning back but crabs twenty to
        /// twenty-eight metres per order, which is a regiment visibly sliding.
        /// <b>Two is the knee</b> - a third to a half of the available turning
        /// for two tenths of one per cent on the clock and about nine metres of
        /// crabbing per order, well under a body length and so not something
        /// the eye can pick out. It also states cleanly: a leg shorter than two
        /// regiments end to end is a shuffle, not a march.
        /// </para>
        /// </remarks>
        internal static float StubLegBodies = 2f;

        /// <summary>
        /// The same route, with any leg too short to be a direction of march
        /// holding the front of the leg that is.
        /// </summary>
        internal static Plan Applied(BattleState battle, UnitInstance unit, Plan plan)
        {
            if (!FrontFromTheMarch || plan.PressedThrough || !plan.Path.Found) return plan;

            IReadOnlyList<Vec2> points = plan.Path.Waypoints;

            // Two points is one leg, and one leg is the direction of march by
            // definition. There is nothing for it to inherit from.
            if (points.Count < 3) return plan;

            Facing?[] fronts = Fronts(unit, points, plan.Hold);

            float longest = 0f;
            for (int leg = 1; leg < points.Count; leg++)
                longest = MathF.Max(longest, Vec2.Distance(points[leg - 1], points[leg]));

            if (longest <= 0f) return plan;

            // Both, and the body is the one that decides. The fraction only
            // stops a short route made entirely of short legs from having every
            // leg called a shuffle.
            float stub = MathF.Min(
                longest * MathF.Max(0f, StubLegFraction),
                unit.Footprint.Depth * MathF.Max(0f, StubLegBodies));

            bool moved = false;

            // The last leg is never touched. M94 chooses the arrival front by
            // asking which fronts the destination will actually accept, and
            // that is a stronger claim than this one - a stub at the end is the
            // arrival shuffle, not a lie about the march.
            for (int leg = 1; leg < points.Count - 1; leg++)
            {
                if (Vec2.Distance(points[leg - 1], points[leg]) >= stub) continue;

                Facing? march = MarchAhead(points, fronts, leg, stub);
                if (!march.HasValue) continue;

                if (Facing.AbsoluteDelta(fronts[leg]!.Value, march.Value) < 0.01f) continue;

                // M24: the front is what the sweep is taken at, so a front the
                // regiment did not walk this leg on proves nothing about it.
                // Asked with the leaving rule on the first leg, as every other
                // check of that leg is, so the body it already touches does not
                // refuse a line walking away from it.
                if (!Marching.IsClearLine(
                        battle, unit, points[leg - 1], points[leg], march.Value, leaving: leg == 1))
                    continue;

                fronts[leg] = march.Value;
                moved = true;
            }

            if (!moved) return plan;

            // Re-priced rather than carrying the old figure, which was costed
            // against turns the regiment no longer makes. W5: the plan reports
            // the march it is actually asking for.
            float seconds = Marching.SecondsToWalk(battle, unit, points, fronts);
            float topSpeed = unit.BaseSpeed;
            float effective = seconds > 0f && topSpeed > 0f
                ? seconds * topSpeed
                : plan.Path.EffectiveDistance;

            PathResult path = PathResult.Success(
                points, Array.Empty<Coord>(), plan.Path.Distance, effective,
                plan.Path.CellsExplored);

            return new Plan(path, hold: fronts, pressedThrough: false, effort: plan.Effort);
        }

        /// <summary>
        /// The front of the first leg after <paramref name="leg"/> that is long
        /// enough to be a direction of march.
        /// </summary>
        /// <remarks>
        /// Forwards only, and that is the rule rather than an economy. The
        /// question is which way the regiment is <i>going</i>, so a run of stubs
        /// takes the front of the march they open, not of the one they closed.
        /// A stub with nothing substantial after it keeps its own front,
        /// because there is no march for it to face.
        /// </remarks>
        private static Facing? MarchAhead(
            IReadOnlyList<Vec2> points, Facing?[] fronts, int leg, float stub)
        {
            for (int ahead = leg + 1; ahead < points.Count; ahead++)
            {
                if (Vec2.Distance(points[ahead - 1], points[ahead]) < stub) continue;

                return fronts[ahead];
            }

            return null;
        }

        /// <summary>
        /// The route's fronts as an array with every leg named, filling in from
        /// the line where the planner left one implied.
        /// </summary>
        private static Facing?[] Fronts(
            UnitInstance unit, IReadOnlyList<Vec2> points, Facing?[]? hold)
        {
            var fronts = new Facing?[points.Count];

            for (int leg = 1; leg < points.Count; leg++)
            {
                fronts[leg] = hold != null && leg < hold.Length && hold[leg].HasValue
                    ? hold[leg]
                    : Marching.AlongTheLine(points[leg - 1], points[leg], unit.Facing);
            }

            return fronts;
        }
    }
}
