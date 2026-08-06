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
        /// Stance for this order only, leaving the unit's standing stance alone.
        /// Null uses whatever the unit already holds.
        /// </summary>
        public readonly Stance? StanceOverride;

        private UnitOrder(OrderKind kind, Vec2 destination, UnitId target, bool wheelFirst, Stance? stanceOverride)
        {
            Kind = kind;
            Destination = destination;
            Target = target;
            WheelFirst = wheelFirst;
            StanceOverride = stanceOverride;
        }

        public static UnitOrder Stand(Stance? stance = null) =>
            new UnitOrder(OrderKind.Stand, Vec2.Zero, UnitId.None, false, stance);

        public static UnitOrder MoveTo(Vec2 destination, bool wheelFirst = false, Stance? stance = null) =>
            new UnitOrder(OrderKind.Move, destination, UnitId.None, wheelFirst, stance);

        public static UnitOrder Attack(UnitId target, bool wheelFirst = false, Stance? stance = null) =>
            new UnitOrder(OrderKind.Attack, Vec2.Zero, target, wheelFirst, stance);

        public override string ToString() => Kind switch
        {
            OrderKind.Move => $"move to {Destination}",
            OrderKind.Attack => $"attack {Target}",
            _ => "stand"
        };
    }
}
