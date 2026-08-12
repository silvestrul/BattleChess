using System;
using System.Collections.Generic;
using BattleChess.Contracts;

namespace BattleChess.Rules
{
    /// <summary>
    /// One part of the simulation, advanced once per tick.
    /// </summary>
    /// <remarks>
    /// Systems are ordered and independent, so combat, morale and vision can be
    /// added in later milestones without the clock or any existing system
    /// changing. Each is handed the log so it can explain its own decisions
    /// rather than leaving the player to guess.
    /// </remarks>
    public interface IBattleSystem
    {
        string Name { get; }

        void Step(BattleState battle, int tick, IBattleLog log);
    }

    /// <summary>
    /// Advances a battle in fixed steps.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The spine of the whole simulation, and deliberately built before it is
    /// strictly needed. Moving units by frame time in the view would have been
    /// quicker, but it would be non-deterministic, tied to framerate, and — the
    /// real objection — it would not be the code the game eventually runs.
    /// </para>
    /// <para>
    /// Free-running play calls <see cref="Advance"/> continuously. Simultaneous
    /// turns will call it exactly <see cref="TicksPerTurn"/> times and stop.
    /// Auto-resolve for the map-game audience will call it as fast as it can
    /// with nothing rendering at all. One path, three uses, and a fixed step is
    /// what lets a published seed reproduce the same battle anywhere.
    /// </para>
    /// </remarks>
    public sealed class BattleClock
    {
        /// <summary>Battle time advanced by a single tick.</summary>
        public const float SecondsPerTick = 1f;

        /// <summary>Ticks in one turn of simultaneous orders.</summary>
        public const int TicksPerTurn = 60;

        private readonly List<IBattleSystem> _systems = new List<IBattleSystem>();

        /// <summary>Ticks elapsed since the battle began.</summary>
        public int Tick { get; private set; }

        /// <summary>Battle time elapsed, in seconds.</summary>
        public float ElapsedSeconds => Tick * SecondsPerTick;

        /// <summary>Whole turns elapsed.</summary>
        public int Turn => Tick / TicksPerTurn;

        /// <summary>Ticks completed within the current turn.</summary>
        public int TickInTurn => Tick % TicksPerTurn;

        public IReadOnlyList<IBattleSystem> Systems => _systems;

        public BattleClock Add(IBattleSystem system)
        {
            _systems.Add(system ?? throw new ArgumentNullException(nameof(system)));
            return this;
        }

        /// <summary>Advances the battle one tick.</summary>
        public void Advance(BattleState battle, IBattleLog? log = null)
        {
            if (battle == null) throw new ArgumentNullException(nameof(battle));

            IBattleLog sink = log ?? NullBattleLog.Instance;

            // Fixed order, every tick, so the same seed produces the same battle.
            for (int i = 0; i < _systems.Count; i++)
            {
                try
                {
                    _systems[i].Step(battle, Tick, sink);
                }
                catch (Exception failure)
                {
                    // A rule that throws must not be able to stop the clock in
                    // silence. One did: sampling terrain under a regiment
                    // standing near the map edge asked about ground that was
                    // off the map, the catalogue threw, and the exception came
                    // out of the movement system on every tick. The battle
                    // simply stopped advancing, and from the player's chair the
                    // army looked stuck against the border with nothing said
                    // anywhere about why.
                    //
                    // Reported once per system per tick and then stepped over.
                    // A battle missing one system's turn is wrong; a battle
                    // that has quietly frozen is unfixable and undiagnosable.
                    sink.Warning("Clock",
                        $"The {_systems[i].Name} rule failed on tick {Tick} and was skipped: {failure.Message}");
                }
            }

            Tick++;
            battle.TurnNumber = Turn;
        }

        /// <summary>Advances a whole turn's worth of ticks.</summary>
        public void AdvanceTurn(BattleState battle, IBattleLog? log = null)
        {
            for (int i = 0; i < TicksPerTurn; i++)
                Advance(battle, log);
        }

        public override string ToString() => $"tick {Tick} (turn {Turn}, {TickInTurn}/{TicksPerTurn})";
    }
}
