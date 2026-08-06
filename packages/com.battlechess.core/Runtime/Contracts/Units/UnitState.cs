namespace BattleChess.Contracts
{
    /// <summary>
    /// What a unit is currently doing, as far as the rules are concerned.
    /// </summary>
    /// <remarks>
    /// Deliberately small. Anything richer — charging, wheeling, reloading —
    /// belongs to the systems that own those behaviours, not to a state
    /// enumeration everything has to agree on.
    /// </remarks>
    public enum UnitState
    {
        /// <summary>Under command and following its orders.</summary>
        Steady = 0,

        /// <summary>
        /// Shaken but still fighting. Penalised, and one more shock away from
        /// breaking.
        /// </summary>
        Wavering = 1,

        /// <summary>
        /// Broken. Ignores orders and flees the field. Survives unless run down,
        /// which is what makes pursuit a decision rather than a formality.
        /// </summary>
        Routing = 2,

        /// <summary>Escaped the field intact. Returns for the next battle.</summary>
        Scattered = 3,

        /// <summary>Caught while routing. Removed permanently.</summary>
        Captured = 4,

        /// <summary>Destroyed in the fighting.</summary>
        Destroyed = 5
    }

    /// <summary>
    /// Extensions for reasoning about unit state without scattering
    /// comparisons across the codebase.
    /// </summary>
    public static class UnitStateExtensions
    {
        /// <summary>Still physically present on the battlefield.</summary>
        public static bool IsOnField(this UnitState state) =>
            state is UnitState.Steady or UnitState.Wavering or UnitState.Routing;

        /// <summary>Still able to fight and take orders.</summary>
        public static bool IsFighting(this UnitState state) =>
            state is UnitState.Steady or UnitState.Wavering;

        /// <summary>Gone for good — not recoverable after the battle.</summary>
        public static bool IsPermanentLoss(this UnitState state) =>
            state is UnitState.Captured or UnitState.Destroyed;

        /// <summary>Left the field but the men come home.</summary>
        public static bool IsRecoverable(this UnitState state) => state == UnitState.Scattered;
    }
}
