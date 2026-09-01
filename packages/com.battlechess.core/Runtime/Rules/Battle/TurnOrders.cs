using System;
using System.Collections.Generic;
using BattleChess.Contracts;

namespace BattleChess.Rules
{
    /// <summary>
    /// Orders a player has drawn but not yet given, held until the turn is
    /// ended.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>[M143], and it is the shape the game was always going to take.</b>
    /// Clicking a regiment plans a route rather than setting it walking; the
    /// routes sit here, drawn on the field, until <see cref="Fire"/> hands them
    /// all over at once.
    /// </para>
    /// <para>
    /// <b>Almost none of this is new machinery.</b> [Mx6a] already promised
    /// that planning is pure - <i>"a batch of orders is planned in parallel;
    /// nothing in a plan writes to the battle"</i> - and
    /// <see cref="Marching.PlanTo"/> has always returned a <see cref="Plan"/>
    /// that <c>ToRoute</c> turns into movement as a separate act. What was
    /// missing was somewhere to keep a plan between those two moments.
    /// </para>
    /// <para>
    /// <b>Each order is planned against the ones already queued</b>, which is
    /// the designer's requirement and the only part with any difficulty in it.
    /// A regiment told to go somewhere its neighbour has just been told to
    /// vacate should route through the gap that will exist, not the one that
    /// does. This pass answers that with <i>where the earlier orders end</i>:
    /// the queued units are stood at their destinations, the new order is
    /// planned, and they are put back.
    /// </para>
    /// <para>
    /// <b>What that does not do</b>, and it is worth saying before anybody
    /// plays it: two regiments can plan cleanly against each other's finishing
    /// places and still cross in the middle of the turn. Answering that means
    /// asking where a body will be <i>at each moment</i>, which is [M16] -
    /// built once, working, and reverted for a reason that has since moved on
    /// (open finding 9). The designer has chosen it, provisionally; it is the
    /// next pass rather than this one, because a queue nobody has played yet
    /// is a poor place to re-litigate a reverted feature.
    /// </para>
    /// </remarks>
    public sealed class TurnOrders
    {
        /// <summary>An order drawn, costed and waiting to be given.</summary>
        public readonly struct Pending
        {
            public readonly UnitId Unit;
            public readonly UnitOrder Order;
            public readonly Plan Plan;

            /// <summary>Where the regiment will stand when this order is done.</summary>
            /// <remarks>
            /// Taken from the plan rather than from the order, because a march
            /// that could not reach where it was sent ends where the route
            /// ends - and the orders queued after it must be planned against
            /// the place it will actually be standing, not the place it was
            /// aimed at.
            /// </remarks>
            public readonly Vec2 Ends;

            public Pending(UnitId unit, UnitOrder order, Plan plan, Vec2 ends)
            {
                Unit = unit;
                Order = order;
                Plan = plan;
                Ends = ends;
            }
        }

        private readonly List<Pending> _pending = new List<Pending>();

        /// <summary>Orders waiting to be given, in the order they were drawn.</summary>
        public IReadOnlyList<Pending> Drawn => _pending;

        public int Count => _pending.Count;

        /// <summary>Whether this unit already has an order waiting.</summary>
        public bool Holds(UnitId unit) => IndexOf(unit) >= 0;

        /// <summary>
        /// Draws an order for a unit, planned against everything already
        /// queued, and keeps it.
        /// </summary>
        /// <remarks>
        /// A second order for the same unit replaces the first rather than
        /// stacking. One regiment, one instruction a turn - and the orders
        /// queued after it are redrawn, since they were planned against a
        /// finishing place that has just moved.
        /// </remarks>
        public bool Draw(
            BattleState battle, UnitInstance unit, UnitOrder order,
            IPathfinder pathfinder, IBattleLog? log = null)
        {
            if (battle == null) throw new ArgumentNullException(nameof(battle));
            if (unit == null) throw new ArgumentNullException(nameof(unit));

            int had = IndexOf(unit.Id);
            if (had >= 0) _pending.RemoveAt(had);

            if (!TryPlan(battle, unit, order, pathfinder, log, out Pending drawn)) return false;


            if (had >= 0) _pending.Insert(had, drawn);
            else _pending.Add(drawn);

            // Everything after it was planned against a world this order has
            // just changed, so it is redrawn. Only the tail: orders before it
            // never saw it and are still true.
            RedrawFrom(battle, (had >= 0 ? had : _pending.Count - 1) + 1, pathfinder, log);

            return true;
        }

        /// <summary>Takes one unit's order back out, and redraws what followed it.</summary>
        public bool Rub(BattleState battle, UnitId unit, IPathfinder pathfinder, IBattleLog? log = null)
        {
            int at = IndexOf(unit);
            if (at < 0) return false;

            _pending.RemoveAt(at);

            RedrawFrom(battle, at, pathfinder, log);

            return true;
        }

        /// <summary>Forgets every drawn order without giving any of them.</summary>
        public void RubEverything() => _pending.Clear();

        /// <summary>
        /// Gives every order that was drawn, and empties the book.
        /// </summary>
        /// <remarks>
        /// The routes are handed over as they were planned rather than planned
        /// again here. Re-planning at this moment would quietly discard what
        /// the player was shown on the field, which is the one thing a
        /// plan-then-fire turn must never do: <b>what you saw drawn is what
        /// gets walked</b>.
        /// </remarks>
        public int Fire(BattleState battle, IBattleLog? log = null)
        {
            if (battle == null) throw new ArgumentNullException(nameof(battle));

            int given = 0;

            foreach (Pending pending in _pending)
            {
                UnitInstance unit = battle.Get(pending.Unit);

                if (!unit.IsOnField) continue;

                unit.GiveOrder(pending.Order, unit.Position);

                if (pending.Plan.Path.Found && pending.Plan.Path.Waypoints.Count >= 2)
                    unit.Route = pending.Plan.ToRoute(pending.Order.WheelFirst);

                given++;

                log?.Decision("Order",
                    $"{unit.Def.DisplayName} sets off - {pending.Order}.",
                    unit.Id);
            }

            _pending.Clear();

            return given;
        }

        // ---- Planning against what is already queued ---------------------------

        /// <summary>
        /// Plans one order in a world where every order already queued has
        /// finished.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The queued regiments are stood at their finishing places, the plan
        /// is taken, and they are put back exactly where they were - in reverse
        /// order, so a unit that appears twice ends on its original ground
        /// whatever happened in between.
        /// </para>
        /// <para>
        /// <b>This does write to the battle, and [Mx6a] says planning must
        /// not.</b> The difference is that Mx6a is about a <i>plan</i> - one
        /// call to the planner, which may run on any thread beside a dozen
        /// others, and which must therefore see a world nobody is editing.
        /// This is the phase around it, single-threaded by construction because
        /// each queued order depends on the one before, and every position is
        /// restored before the caller sees anything. What must never happen is
        /// this running while the batch planner is in flight, and nothing does:
        /// drawing an order is a click.
        /// </para>
        /// </remarks>
        private bool TryPlan(
            BattleState battle, UnitInstance unit, UnitOrder order,
            IPathfinder pathfinder, IBattleLog? log, out Pending drawn,
            UnitId standInstead = default, Vec2 standAt = default)
        {
            drawn = default;

            Vec2 destination = DestinationOf(battle, unit, order);

            List<(UnitInstance Unit, Vec2 Was)> moved = _shifted ??= new List<(UnitInstance, Vec2)>(16);
            moved.Clear();

            foreach (Pending pending in _pending)
            {
                if (pending.Unit == unit.Id) continue;

                UnitInstance other = battle.Get(pending.Unit);

                if (!other.IsOnField) continue;

                moved.Add((other, other.Position));

                // [M146]. Normally a queued regiment is stood where its order
                // finishes. When this plan is being taken to get *out of the
                // way* of one of them, that one is stood where it will be at
                // the moment of the crossing instead - which is the place that
                // matters and is nowhere near where it ends up. Without this
                // the second plan sees exactly what the first one saw and comes
                // back identical, which is how the pass looked as though it did
                // nothing at all.
                other.Position = other.Id == standInstead ? standAt : pending.Ends;
            }

            // [M145]. The order has to be on the unit while the route is
            // taken, because the planner reads the front it is to arrive on off
            // OrderFacing. Without this the book drew a different route from
            // the one the order would produce - the player was shown one line
            // and the regiment walked another.
            (UnitOrder, Vec2, Facing) wasOrdered = unit.BorrowForPlanning(order, unit.Position);

            try
            {
                Plan plan = Marching.PlanTo(battle, unit, pathfinder, destination, log);

                if (!plan.Path.Found || plan.Path.Waypoints.Count < 2) return false;

                drawn = new Pending(
                    unit.Id, order, plan, plan.Path.Waypoints[plan.Path.Waypoints.Count - 1]);

                return true;
            }
            finally
            {
                unit.ReturnAfterPlanning(wasOrdered);

                for (int i = moved.Count - 1; i >= 0; i--)
                    moved[i].Unit.Position = moved[i].Was;

                moved.Clear();
            }
        }

        // ---- [M146]: built, measured, and switched off ------------------------
        //
        // Re-routing a queued order around where another regiment *will be* does
        // not work, and the reason is worth keeping the code to show. Two horse
        // regiments crossing at right angles overlap by 46% of a body at the
        // worst moment. Stand the offender at the crossing point, re-plan, and
        // the answer comes back overlapping by 50% - because moving the route
        // changes how long it takes, so the crossing simply happens somewhere
        // else. Three passes chase it round the field.
        //
        // A static snapshot cannot answer a question about time. What can:
        // either the search itself carries time (which is what M16 is), or
        // nobody re-routes at all and one of the two *waits* - a start delay on
        // the later order, which converges because pushing a body later in time
        // removes the overlap without moving it. The second is far cheaper and
        // is what the designer asked for in the first place ("they could just
        // wait behind one another"). Open finding 32.
        //
        // Kept rather than deleted because the timetable below - where any
        // regiment will be at second t, which is exact under plan-then-fire
        // since every route starts at the same instant - is the part both
        // answers need.

        /// <summary>How many times a route is redrawn to get out of somebody else's way.</summary>
        /// <remarks>
        /// Three. Each pass moves one body out of the way and asks again, and
        /// the arrangements that are still crossing after three were not going
        /// to be solved by a fourth - they want somebody to give way [Mx2b],
        /// not a cleverer line.
        /// </remarks>
        private const int TriesToGetOutOfTheWay = 3;

        /// <summary>How far apart two bodies count as having crossed.</summary>
        private const float CrossedFraction = 0.02f;

        /// <summary>Seconds between samples when the turn is walked through.</summary>
        /// <remarks>
        /// Two. Cavalry covers under ten metres in that, which is a quarter of
        /// a regiment's depth, so nothing crosses a body between two samples.
        /// </remarks>
        private const float SampleSeconds = 2f;

        /// <summary>
        /// Redraws this order until it stops crossing the ones already queued.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>[M146], and it is the designer's second reading of "plans for the
        /// previous one too".</b> Planning against where the earlier orders
        /// <i>finish</i> is not enough: two regiments can both end up somewhere
        /// sensible and still walk through each other in the middle of the
        /// turn, which is what a play-test of [M143] showed.
        /// </para>
        /// <para>
        /// <b>The whole turn is known at draw time</b>, which is what makes
        /// this affordable here and made [M16] hard in real time. Every queued
        /// route starts at the same instant and its pace is known, so where any
        /// regiment will be at second <i>t</i> is arithmetic rather than a
        /// prediction.
        /// </para>
        /// <para>
        /// <b>The trick is the same one the endpoint pass uses.</b> When a
        /// crossing is found, the offender is stood where it will be <i>at the
        /// moment of the crossing</i> and the route is drawn again - so the
        /// planner sees a body in the way at the place that actually matters,
        /// without the planner itself having to learn about time. Three passes,
        /// then it is left alone.
        /// </para>
        /// <para>
        /// <b>What it does not promise.</b> Sampling every two seconds and
        /// walking at flat pace ignores what turning costs, so the timetable is
        /// approximate and a regiment held up on the day will be somewhere this
        /// did not expect. It removes the crossings that come from nobody
        /// looking; it cannot remove the ones that come from the world not
        /// going to plan. Those want [Mx2b] - somebody giving way while it
        /// happens - which is still not built.
        /// </para>
        /// </remarks>
        private Pending KeepClearOfTheOthers(
            BattleState battle, UnitInstance unit, UnitOrder order,
            IPathfinder pathfinder, Pending drawn, IBattleLog? log)
        {
            for (int attempt = 0; attempt < TriesToGetOutOfTheWay; attempt++)
            {
                if (!FirstCrossing(battle, unit, drawn, out UnitInstance other, out Vec2 theirPlace))
                    return drawn;

                Vec2 stood = other.Position;
                other.Position = theirPlace;

                try
                {
                    if (!TryPlan(
                            battle, unit, order, pathfinder, log, out Pending again,
                            other.Id, theirPlace))
                        return drawn;

                    drawn = again;
                }
                finally
                {
                    other.Position = stood;
                }
            }

            log?.Info("Order",
                $"{unit.Def.DisplayName} could not be drawn a route that keeps clear of everybody for the " +
                "whole turn. It will have to sort it out on the way.",
                unit.Id);

            return drawn;
        }

        /// <summary>
        /// Walks the turn through and reports the first moment this route puts
        /// its regiment inside one of the others.
        /// </summary>
        private bool FirstCrossing(
            BattleState battle, UnitInstance unit, Pending drawn,
            out UnitInstance other, out Vec2 theirPlace)
        {
            other = null!;
            theirPlace = default;

            float turn = BattleClock.TicksPerTurn * BattleClock.SecondsPerTick;

            for (float t = SampleSeconds; t <= turn; t += SampleSeconds)
            {
                Vec2 ours = Along(battle, unit, drawn.Plan.Path.Waypoints, t);
                OrientedRect us = new OrientedRect(ours, unit.Facing, unit.Footprint);

                foreach (Pending pending in _pending)
                {
                    if (pending.Unit == unit.Id) continue;

                    UnitInstance them = battle.Get(pending.Unit);

                    if (!them.IsOnField) continue;

                    Vec2 theirs = Along(battle, them, pending.Plan.Path.Waypoints, t);

                    if (OrientedRect.OverlapFraction(
                            us, new OrientedRect(theirs, them.Facing, them.Footprint)) <= CrossedFraction)
                        continue;

                    other = them;
                    theirPlace = theirs;

                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Where a regiment walking this route at its own pace will be after
        /// <paramref name="seconds"/>.
        /// </summary>
        /// <remarks>
        /// Flat pace along the drawn line. It ignores what coming round costs,
        /// which makes every estimate optimistic by the same sign - a regiment
        /// is never further along than this says. For finding crossings that is
        /// the safe direction: it looks for trouble slightly ahead of where the
        /// trouble will be.
        /// </remarks>
        private static Vec2 Along(
            BattleState battle, UnitInstance unit, IReadOnlyList<Vec2> way, float seconds)
        {
            if (way.Count == 0) return unit.Position;

            float left = MathF.Max(0.1f, battle.SpeedOf(unit)) * seconds;

            for (int i = 1; i < way.Count; i++)
            {
                float leg = Vec2.Distance(way[i - 1], way[i]);

                if (leg <= Vec2.Epsilon) continue;

                if (left < leg) return Vec2.Lerp(way[i - 1], way[i], left / leg);

                left -= leg;
            }

            return way[way.Count - 1];
        }

        /// <summary>Redraws every order from this position in the book onward.</summary>
        private void RedrawFrom(BattleState battle, int from, IPathfinder pathfinder, IBattleLog? log)
        {
            if (from >= _pending.Count) return;

            List<Pending> tail = new List<Pending>(_pending.Count - from);

            for (int i = from; i < _pending.Count; i++) tail.Add(_pending[i]);

            _pending.RemoveRange(from, _pending.Count - from);

            foreach (Pending was in tail)
            {
                UnitInstance unit = battle.Get(was.Unit);

                if (!unit.IsOnField) continue;

                if (TryPlan(battle, unit, was.Order, pathfinder, log, out Pending again))
                    _pending.Add(again);

                // An order that can no longer be drawn is dropped rather than
                // kept as a route nobody can walk. The player is told by its
                // disappearing from the field, which is the only honest way to
                // show "that is not possible any more".
            }
        }

        private static Vec2 DestinationOf(BattleState battle, UnitInstance unit, UnitOrder order) =>
            order.Kind == OrderKind.Attack && order.Target.IsValid
                ? battle.Get(order.Target).Position
                : order.Destination;

        private int IndexOf(UnitId unit)
        {
            for (int i = 0; i < _pending.Count; i++)
                if (_pending[i].Unit == unit) return i;

            return -1;
        }

        private List<(UnitInstance Unit, Vec2 Was)>? _shifted;
    }
}
