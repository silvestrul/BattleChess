using System;

namespace BattleChess.Contracts
{
    /// <summary>
    /// How much attention an entry deserves.
    /// </summary>
    public enum LogLevel
    {
        /// <summary>Something happened.</summary>
        Info = 0,

        /// <summary>A choice was made, and this is the reasoning behind it.</summary>
        Decision = 1,

        /// <summary>Something was refused, and this is why.</summary>
        Blocked = 2,

        /// <summary>Something is wrong but the simulation carried on.</summary>
        Warning = 3
    }

    /// <summary>
    /// One line of explanation from the simulation.
    /// </summary>
    public readonly struct BattleLogEntry
    {
        public readonly LogLevel Level;

        /// <summary>Which system spoke — "Path", "Move", "Deploy".</summary>
        public readonly string Category;

        public readonly string Message;

        /// <summary>The unit concerned, or <see cref="UnitId.None"/>.</summary>
        public readonly UnitId Unit;

        /// <summary>Tick within the turn, or -1 outside a turn.</summary>
        public readonly int Tick;

        /// <param name="unit">
        /// Pass <see cref="UnitId.None"/> for entries about nothing in
        /// particular. Note that <c>default(UnitId)</c> is unit zero, a real
        /// unit — which is why callers must be explicit rather than relying on
        /// the default.
        /// </param>
        public BattleLogEntry(LogLevel level, string category, string message, UnitId unit, int tick = -1)
        {
            Level = level;
            Category = category ?? "General";
            Message = message ?? string.Empty;
            Unit = unit;
            Tick = tick;
        }

        public override string ToString() =>
            Tick >= 0 ? $"[{Tick:000}] {Category}: {Message}" : $"{Category}: {Message}";
    }

    /// <summary>
    /// Where the simulation explains itself.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Exists because the interesting question during hand-testing is rarely
    /// "what happened" but "why was that allowed" or "why was that refused".
    /// Systems that make a judgement — a route rejected, an order ignored, a
    /// unit halted — write the reason here rather than discarding it.
    /// </para>
    /// <para>
    /// Passed in rather than reached for, so a headless run, a Unity session and
    /// an eventual battle report can each capture the same commentary
    /// differently. Nothing is obliged to keep it: <see cref="NullBattleLog"/>
    /// costs nothing.
    /// </para>
    /// </remarks>
    public interface IBattleLog
    {
        void Record(in BattleLogEntry entry);
    }

    /// <summary>Discards everything. The default when nobody is listening.</summary>
    public sealed class NullBattleLog : IBattleLog
    {
        public static readonly NullBattleLog Instance = new NullBattleLog();

        private NullBattleLog() { }

        public void Record(in BattleLogEntry entry) { }
    }

    /// <summary>Shorthand for the common cases.</summary>
    /// <remarks>
    /// The unit parameter is nullable rather than defaulted, because
    /// <c>default(UnitId)</c> is unit zero — a perfectly real regiment — so a
    /// plain default would attribute every general remark to whichever unit
    /// happened to be created first.
    /// </remarks>
    public static class BattleLogExtensions
    {
        public static void Info(this IBattleLog log, string category, string message, UnitId? unit = null) =>
            log?.Record(new BattleLogEntry(LogLevel.Info, category, message, unit ?? UnitId.None));

        public static void Decision(this IBattleLog log, string category, string message, UnitId? unit = null) =>
            log?.Record(new BattleLogEntry(LogLevel.Decision, category, message, unit ?? UnitId.None));

        public static void Blocked(this IBattleLog log, string category, string message, UnitId? unit = null) =>
            log?.Record(new BattleLogEntry(LogLevel.Blocked, category, message, unit ?? UnitId.None));

        public static void Warning(this IBattleLog log, string category, string message, UnitId? unit = null) =>
            log?.Record(new BattleLogEntry(LogLevel.Warning, category, message, unit ?? UnitId.None));
    }
}
