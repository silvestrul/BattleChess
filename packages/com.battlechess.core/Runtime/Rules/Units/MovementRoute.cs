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

        private readonly Facing?[] _hold;

        /// <summary>
        /// Whether this route gives up on keeping clear of its own side.
        /// </summary>
        /// <remarks>
        /// <b>M18</b> rung three, and the reason [M1](DECISIONS.md) is a strong
        /// preference rather than an invariant. Set only when nothing else
        /// worked — no line, no way round, no gap to thread — because a regiment
        /// walled in by its own with an absolute rule against sharing ground
        /// stands there until the battle ends.
        /// </remarks>
        public bool PressingThrough { get; set; }

        /// <summary>
        /// Who the legs this route is about to walk were drawn around, or
        /// <see cref="UnitId.None"/> if they were clear when it was checked.
        /// </summary>
        /// <remarks>
        /// <b>M11.</b> Kept on the route rather than on the unit so that
        /// replacing the route replaces the answer with it - there is no way to
        /// leave a stale one behind, and no creation site has to remember to
        /// clear it.
        /// </remarks>
        public UnitId LegsPlannedAgainst { get; set; } = UnitId.None;

        /// <summary>
        /// The last reason the cadence gave for leaving this route alone with
        /// something in front of it, so the same reason is not written twice.
        /// </summary>
        public string? HeldItsHandBecause { get; set; }

        /// <summary>
        /// The body this route was drawn around, or <see cref="UnitId.None"/>
        /// if it was not drawn to avoid anybody.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>[M140], and it is what makes [M21]'s commitment survive a
        /// re-plan.</b> Distinct from <see cref="LegsPlannedAgainst"/>, which
        /// records what the first look at this route happened to <i>see</i> -
        /// usually nobody, because a fresh way round starts clear. That is why
        /// the latch kept re-arming: the detour's first leg was clear, so the
        /// blocker read as news again three ticks later, and the regiment drew
        /// a new and slightly worse way round every time.
        /// </para>
        /// <para>
        /// This one records what the route is <i>for</i>. A route drawn to get
        /// past a body is not redrawn because of that same body.
        /// </para>
        /// </remarks>
        public UnitId DrawnAround { get; set; } = UnitId.None;

        /// <summary>Where the regiment stood when the cadence last looked at this route.</summary>
        public Vec2 LastLookedFrom { get; set; }

        /// <summary>The tick the cadence last looked at this route.</summary>
        public int LastLookedTick { get; set; } = int.MinValue;

        /// <summary>Whether <see cref="LegsPlannedAgainst"/> has been taken yet.</summary>
        /// <remarks>
        /// The first look records rather than reacts. Without it every fresh
        /// route reads its own first blocker as news and re-plans once for
        /// nothing.
        /// </remarks>
        public bool LegsLookedAt { get; set; }

        public MovementRoute(IReadOnlyList<Vec2> waypoints, bool wheelFirst)
            : this(waypoints, wheelFirst, null)
        {
        }

        /// <summary>
        /// A route that asks for a particular front on some of its legs.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>M14.</b> Threading a gap narrower than a regiment is means going
        /// through it side-on, and going side-on means <i>facing</i> square to
        /// the way you are travelling. So the front to hold is a property of the
        /// leg, not of the march, and it has to be carried from where it was
        /// decided to where it is walked.
        /// </para>
        /// <para>
        /// Carried rather than re-derived on purpose. Working out at execution
        /// time whether this leg "looks like" one that needs crabbing would mean
        /// the movement rule re-deciding what the planner already decided, with
        /// nothing to check it against — the same shape as a log line that
        /// reports a bearing by asking the code under suspicion what the bearing
        /// is. If the two ever disagreed, the regiment would turn without a
        /// reason anybody could find.
        /// </para>
        /// </remarks>
        public MovementRoute(IReadOnlyList<Vec2> waypoints, bool wheelFirst, IReadOnlyList<Facing?>? hold)
        {
            if (waypoints == null) throw new ArgumentNullException(nameof(waypoints));
            if (waypoints.Count == 0) throw new ArgumentException("A route needs at least one waypoint.", nameof(waypoints));

            _waypoints = new Vec2[waypoints.Count];
            for (int i = 0; i < waypoints.Count; i++)
                _waypoints[i] = waypoints[i];

            _hold = new Facing?[waypoints.Count];
            if (hold != null)
            {
                for (int i = 0; i < _hold.Length && i < hold.Count; i++)
                    _hold[i] = hold[i];
            }

            // The first waypoint is where the unit already stands, so the march
            // begins with the second when there is one.
            NextWaypoint = _waypoints.Length > 1 ? 1 : 0;
            WheelFirst = wheelFirst;
        }

        public IReadOnlyList<Vec2> Waypoints => _waypoints;

        /// <summary>
        /// The front to hold while walking the leg now under way, if this leg
        /// asks for one. Null means face the line of march, as a march does.
        /// </summary>
        public Facing? HoldThisLeg =>
            NextWaypoint < _hold.Length ? _hold[NextWaypoint] : null;

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
