using System;

namespace BattleChess.Contracts
{
    /// <summary>
    /// A unit's standing instruction for what to do when it meets the enemy.
    /// </summary>
    /// <remarks>
    /// The answer to the central problem of committing orders against stale
    /// information: you cannot foresee what your regiment will run into, so you
    /// tell it in advance how to behave when it does. Persistent per unit, so a
    /// reserve set to <see cref="Defend"/> stays that way without being told
    /// again every turn.
    /// </remarks>
    public enum Stance
    {
        /// <summary>
        /// Halt on entering an enemy's zone of control, even when strong enough
        /// to force through. Cautious, and the default.
        /// </summary>
        Defend = 0,

        /// <summary>
        /// Carry out the order, forcing through an enemy line where the unit is
        /// able to. Halts only when genuinely blocked.
        /// </summary>
        Advance = 1,

        /// <summary>
        /// As <see cref="Advance"/>, and additionally closes with any enemy that
        /// comes near, without waiting for an order. Pursues, but on a leash.
        /// </summary>
        Aggressive = 2,

        /// <summary>
        /// Withdraw from any enemy that comes close, and never close willingly.
        /// What scouts and artillery want most of the time.
        /// </summary>
        Evade = 3
    }

    /// <summary>What a unit has been told to do.</summary>
    public enum OrderKind
    {
        /// <summary>Stay where it is.</summary>
        Stand = 0,

        /// <summary>March to a place.</summary>
        Move = 1,

        /// <summary>Close with a particular enemy, following it as it moves.</summary>
        Attack = 2
    }

    /// <summary>
    /// An instruction issued to one unit.
    /// </summary>
    /// <remarks>
    /// A value type the client can build and hand over without touching
    /// simulation state — the shape a networked order will eventually travel in.
    /// </remarks>
    public readonly struct UnitOrder
    {
        public readonly OrderKind Kind;

        /// <summary>Where to march, for <see cref="OrderKind.Move"/>.</summary>
        public readonly Vec2 Destination;

        /// <summary>Who to close with, for <see cref="OrderKind.Attack"/>.</summary>
        public readonly UnitId Target;

        /// <summary>Come round onto the bearing before setting off.</summary>
        public readonly bool WheelFirst;

        /// <summary>
        /// Which way to be facing on arrival, or null to keep the front where
        /// it already points.
        /// </summary>
        /// <remarks>
        /// The difference between moving a body of men and moving a token.
        /// Without it, facing was purely a by-product of the march bearing and
        /// a regiment always turned to point wherever it was going — so
        /// sidestepping a line fifty metres pivoted its whole frontage ninety
        /// degrees, and there was no way to ask for anything else. Keeping the
        /// front where the player put it is the default because that is what
        /// almost every order means; the cost of moving off your bearing is
        /// already charged as speed.
        /// </remarks>
        public readonly Facing? Bearing;

        /// <summary>
        /// Stance for this order only, leaving the unit's standing stance alone.
        /// Null uses whatever the unit already holds.
        /// </summary>
        public readonly Stance? StanceOverride;

        private UnitOrder(
            OrderKind kind, Vec2 destination, UnitId target, bool wheelFirst, Stance? stanceOverride, Facing? bearing)
        {
            Kind = kind;
            Destination = destination;
            Target = target;
            WheelFirst = wheelFirst;
            StanceOverride = stanceOverride;
            Bearing = bearing;
        }

        public static UnitOrder Stand(Stance? stance = null) =>
            new UnitOrder(OrderKind.Stand, Vec2.Zero, UnitId.None, false, stance, null);

        /// <summary>
        /// Marches to a place. Pass <paramref name="bearing"/> to say which way
        /// to face on arrival; leave it out to keep the current front.
        /// </summary>
        public static UnitOrder MoveTo(
            Vec2 destination, bool wheelFirst = false, Stance? stance = null, Facing? bearing = null) =>
            new UnitOrder(OrderKind.Move, destination, UnitId.None, wheelFirst, stance, bearing);

        public static UnitOrder Attack(UnitId target, bool wheelFirst = false, Stance? stance = null) =>
            new UnitOrder(OrderKind.Attack, Vec2.Zero, target, wheelFirst, stance, null);

        public override string ToString() => Kind switch
        {
            OrderKind.Move => $"move to {Destination}",
            OrderKind.Attack => $"attack {Target}",
            _ => "stand"
        };
    }
}
