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
            IPathfinder pathfinder, IBattleLog? log, out Pending drawn)
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
                other.Position = pending.Ends;
            }

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
                for (int i = moved.Count - 1; i >= 0; i--)
                    moved[i].Unit.Position = moved[i].Was;

                moved.Clear();
            }
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
