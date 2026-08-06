using System;
using System.Collections.Generic;
using BattleChess.Contracts;

namespace BattleChess.Rules
{
    /// <summary>
    /// A march a unit is currently carrying out: where it is going, how far
    /// along it is, and how it was told to get there.
    /// </summary>
    /// <remarks>
    /// Deliberately not the full order system — that arrives with stances in
    /// M2 pass 3. This is the minimum a unit needs to walk somewhere, kept
    /// separate so the order system can wrap it rather than replace it.
    /// </remarks>
    public sealed class MovementRoute
    {
        private readonly Vec2[] _waypoints;

        /// <summary>Index of the waypoint currently being marched toward.</summary>
        public int NextWaypoint { get; private set; }

        /// <summary>
        /// Whether to come round onto the bearing before setting off, rather
        /// than turning while under way.
        /// </summary>
        /// <remarks>
        /// The trade is real. Wheeling first costs several seconds standing
        /// still but then marches at full speed in good order; turning while
        /// moving sets off at once but crawls until it comes round. Over a long
        /// march wheeling usually wins; for a short reposition it does not.
        /// </remarks>
        public bool WheelFirst { get; }

        public MovementRoute(IReadOnlyList<Vec2> waypoints, bool wheelFirst)
        {
            if (waypoints == null) throw new ArgumentNullException(nameof(waypoints));
            if (waypoints.Count == 0) throw new ArgumentException("A route needs at least one waypoint.", nameof(waypoints));

            _waypoints = new Vec2[waypoints.Count];
            for (int i = 0; i < waypoints.Count; i++)
                _waypoints[i] = waypoints[i];

            // The first waypoint is where the unit already stands, so the march
            // begins with the second when there is one.
            NextWaypoint = _waypoints.Length > 1 ? 1 : 0;
            WheelFirst = wheelFirst;
        }

        public IReadOnlyList<Vec2> Waypoints => _waypoints;

        public bool IsComplete => NextWaypoint >= _waypoints.Length;

        public Vec2 Destination => _waypoints[_waypoints.Length - 1];

        /// <summary>The point currently being marched toward.</summary>
        public Vec2 Target => _waypoints[Math.Min(NextWaypoint, _waypoints.Length - 1)];

        /// <summary>Moves on to the next waypoint, completing the route at the end.</summary>
        public void Advance() => NextWaypoint++;

        /// <summary>Straight-line distance still to walk, following the remaining legs.</summary>
        public float RemainingDistance(Vec2 from)
        {
            if (IsComplete) return 0f;

            float total = Vec2.Distance(from, Target);

            for (int i = NextWaypoint + 1; i < _waypoints.Length; i++)
                total += Vec2.Distance(_waypoints[i - 1], _waypoints[i]);

            return total;
        }

        public override string ToString() =>
            IsComplete
                ? "route complete"
                : $"waypoint {NextWaypoint + 1}/{_waypoints.Length} toward {Destination}";
    }
}
