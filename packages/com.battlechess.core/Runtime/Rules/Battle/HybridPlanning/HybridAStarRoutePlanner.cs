using System;
using System.Collections.Generic;
using BattleChess.Contracts;
using BattleChess.Rules;

namespace BattleChess.Rules.HybridPlanning
{
    /// <summary>
    /// A route planner over states of (x, y, heading) rather than over
    /// places on a drawn line: <see cref="HybridAStarPlanner"/> wearing the
    /// same <see cref="IRoutePlanner"/> coat as every other way of planning,
    /// so it can be put on the field next to the rest and measured rather
    /// than taken on faith.
    /// </summary>
    /// <remarks>
    /// A prototype, on the record. It answers one block's own route with its
    /// own rotation-aware search; it does not yet do anything about several
    /// blocks stepping on each other's routes in the same tick — that is the
    /// reservation-table layer from the design discussion, and it belongs
    /// above <see cref="IRoutePlanner"/>, not inside one implementation of
    /// it. Wire it in, watch what it actually costs, and let that answer
    /// whether the rest is worth building.
    /// </remarks>
    public sealed class HybridAStarRoutePlanner : IRoutePlanner
    {
        public string Name => "hybrid A* over heading and ground";

        public Plan PlanTo(
            BattleState battle, UnitInstance unit, IPathfinder pathfinder, Vec2 destination,
            IBattleLog? log = null, IWayRound? wayRound = null, Facing? arriveOn = null) =>
            PlanAlong(battle, unit, destination, arriveOn, corridor: null, 0f, log);

        /// <summary>
        /// The same pose search, but allowed to look only within
        /// <paramref name="corridorHalfWidthMetres"/> of a route some cheaper
        /// planner already drew.
        /// </summary>
        /// <remarks>
        /// The lattice's cost is its expansion count, and its expansions go on
        /// ground no sensible route would touch. A cheap planner that has
        /// already answered — even with a route the executor refuses — has said
        /// something true about <i>where</i> the answer lies, and that is worth
        /// more to this search than to the planner that produced it.
        /// </remarks>
        public static Plan PlanAlong(
            BattleState battle, UnitInstance unit, Vec2 destination, Facing? arriveOn,
            IReadOnlyList<Vec2>? corridor, float corridorHalfWidthMetres, IBattleLog? log = null,
            int? expansionBudget = null, float secondsLimit = 0f)
        {
            if (battle == null) throw new ArgumentNullException(nameof(battle));
            if (unit == null) throw new ArgumentNullException(nameof(unit));

            // Only a caller's own explicit arriveOn constrains the finish —
            // falling back to unit.OrderFacing the way the other planners do
            // turned "go to point P" into "go to point P and end up facing
            // whichever way you already were", since a fresh order's
            // OrderFacing is just wherever the unit is standing. Measured:
            // that alone failed ~80% of an open-field, zero-obstacle sweep
            // of distances and approach angles.
            Facing? goalHeading = arriveOn;

            // Two lists, and the difference between them is the point.
            //
            // Clearance must not count the mover against itself, so `obstacles`
            // leaves it out — and that is exactly why no two regiments of one
            // army ever had the same obstacle set, and why the heuristic field
            // was rebuilt eighty times for eighty orders that share a field.
            // `everybody` is the same set for every mover of one owner, so the
            // estimate can be built once and shared. It costs the mover's own
            // cell, which the field answers for by reading a neighbour.
            var obstacles = new List<HybridBox>();
            var everybody = new List<HybridBox>();

            foreach (UnitInstance other in battle.UnitsOnField())
            {
                if (other.Owner != unit.Owner) continue; // M15a: enemies are not obstacles to a plan.

                HybridBox body = HybridBox.For(other.Position, other.Facing, other.Footprint);

                everybody.Add(body);
                if (other.Id != unit.Id) obstacles.Add(body);
            }

            float turnRateDegreesPerSecond = unit.Def.Get(UnitAttributes.TurnRate);
            float topSpeed = unit.BaseSpeed;

            HybridAStarPlanner.Outcome outcome = HybridAStarPlanner.Search(
                unit.Position, unit.Facing, destination, goalHeading,
                unit.Footprint, obstacles, topSpeed, turnRateDegreesPerSecond,
                expansionBudget: expansionBudget, heuristicWeight: null,
                corridor: corridor, corridorHalfWidthMetres: corridorHalfWidthMetres,
                secondsLimit: secondsLimit);

            // Places is left at zero rather than repeating Expansions: this
            // planner has no candidate places, and printing the same number
            // in two columns of a comparison table invites reading it as two
            // findings. Legs carries primitives tried, which is the nearest
            // honest equivalent of a leg priced.
            RouteEffort effort = new RouteEffort(
                places: 0, legs: outcome.PrimitivesTried, expansions: outcome.Expansions,
                rounds: 1, bodies: outcome.Obstacles);

            if (!outcome.Found)
            {
                // Naming the reason, because the two that matter want opposite
                // fixes: no route at all is the arrangement's answer and is
                // final, while giving up on the clock is this planner's answer
                // and says the ceiling in M76 is biting.
                string gaveUp = outcome.Failure == PathFailure.SearchBudgetExhausted
                    ? $" It gave up rather than finished \u2014 the budget is " +
                      $"{HybridAStarPlanner.MillisecondsPerSearch:0} ms a search."
                    : string.Empty;

                log?.Record(new BattleLogEntry(
                    LogLevel.Blocked, "Path",
                    $"{unit.Def.DisplayName} found no lattice route to {destination} " +
                    $"({outcome.Expansions} states expanded, {outcome.Obstacles} bodies avoided).{gaveUp}",
                    unit.Id));

                PathResult failed = PathResult.Failed(outcome.Failure, outcome.Failure.ToString(), outcome.Expansions);
                return new Plan(failed, hold: null, pressedThrough: false, effort: effort);
            }

            // Not smoothed here. The cast-ahead pass belongs to whoever hands
            // a route to the executor - which is StagedRoutePlanner, and which
            // has to do it for every planner's route rather than only this
            // one. See RouteSmoothing.
            IReadOnlyList<Vec2> waypoints = outcome.Waypoints;
            Facing?[] fronts = outcome.Fronts;

            float distance = 0f;
            for (int i = 1; i < waypoints.Count; i++)
                distance += Vec2.Distance(waypoints[i - 1], waypoints[i]);

            // EffectiveDistance is metres-of-equivalent-open-ground, so the
            // seconds the search minimised are converted back rather than
            // passed through as though they were a length. Comparing route
            // quality between planners should go through
            // Marching.SecondsToWalk instead — the executor's own model,
            // which is the only currency all of them share.
            float effective = outcome.Seconds > 0f
                ? outcome.Seconds * topSpeed
                : distance;

            PathResult path = PathResult.Success(
                waypoints, Array.Empty<Coord>(), distance, effective, outcome.Expansions);

            // The fronts go with the points. This planner's whole subject is
            // the heading, and a Plan that dropped it left the walker to
            // infer one from the shape of the line — a different facing than
            // the one the search swept and cleared.
            return new Plan(path, hold: fronts, pressedThrough: false, effort: effort);
        }

    }
}
