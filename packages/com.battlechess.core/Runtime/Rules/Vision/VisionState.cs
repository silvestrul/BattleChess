using System;
using BattleChess.Contracts;

namespace BattleChess.Rules
{
    /// <summary>
    /// What each army can currently see of the other.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Per army, not per unit.</b> Every regiment has its own eyes, but what
    /// any one of them makes out is known to the whole army at once — which is
    /// the whole reason to field scouts. A unit that sees far and cheaply is
    /// buying information for everyone, not for itself.
    /// </para>
    /// <para>
    /// A flat array indexed by army and unit id, never a dictionary. Vision
    /// feeds orders and will feed the AI, so it has to answer the same way on
    /// every run of the same seed, and hash ordering is exactly the sort of
    /// thing that quietly stops that being true.
    /// </para>
    /// </remarks>
    public sealed class VisionState
    {
        private bool[] _seen = Array.Empty<bool>();
        private int _units;

        /// <summary>
        /// Whether anything has ever computed sightings for this battle.
        /// </summary>
        /// <remarks>
        /// A battle run without a <see cref="VisionSystem"/> on its clock has no
        /// fog at all, and everything that consults vision must behave as though
        /// every regiment were plainly in view. The alternative — treating an
        /// uncomputed state as "nobody can see anything" — silently stops every
        /// shooter on the field and looks like a broken combat rule rather than
        /// a missing system.
        ///
        /// This is not the thing that keeps a client honest. That is the
        /// per-player projection, which is built from the fogged view and cannot
        /// be short-circuited by a system being absent.
        /// </remarks>
        public bool InPlay { get; private set; }

        /// <summary>Recomputes every sighting from scratch.</summary>
        public void Recompute(BattleState battle)
        {
            if (battle == null) throw new ArgumentNullException(nameof(battle));

            InPlay = true;

            int armies = battle.Armies.Count;
            int units = battle.AllUnits.Count;

            if (_seen.Length != armies * units)
                _seen = new bool[armies * units];
            else
                Array.Clear(_seen, 0, _seen.Length);

            _units = units;

            for (int a = 0; a < armies; a++)
            {
                PlayerId viewer = battle.Armies[a].Player;

                foreach (UnitInstance target in battle.UnitsOnField())
                {
                    if (target.Owner == viewer) continue;
                    if (_seen[a * units + target.Id.Value]) continue;

                    foreach (UnitInstance observer in battle.UnitsOf(viewer))
                    {
                        if (!observer.IsOnField) continue;

                        if (LineOfSight.CanSee(battle, observer, target))
                        {
                            _seen[a * units + target.Id.Value] = true;
                            break;
                        }
                    }
                }
            }
        }

        /// <summary>Whether an army can currently see a unit.</summary>
        /// <remarks>
        /// Own regiments are always known. A commander does not have to spot his
        /// own men.
        /// </remarks>
        public bool CanSee(BattleState battle, PlayerId viewer, UnitInstance target)
        {
            if (target.Owner == viewer) return true;
            if (!target.IsOnField) return false;
            if (!InPlay) return true;

            int army = IndexOfArmy(battle, viewer);
            if (army < 0 || _units == 0) return false;

            int slot = army * _units + target.Id.Value;

            return slot >= 0 && slot < _seen.Length && _seen[slot];
        }

        /// <summary>
        /// Which of an army's own regiments can make out a given enemy, or
        /// <see cref="UnitId.None"/> if none can.
        /// </summary>
        /// <remarks>
        /// Not used by the rules — this is for answering "why can I see that?"
        /// in the debug console, which is the difference between a fog system
        /// you can tune and one you argue with.
        /// </remarks>
        public static UnitId SpottedBy(BattleState battle, PlayerId viewer, UnitInstance target)
        {
            foreach (UnitInstance observer in battle.UnitsOf(viewer))
            {
                if (!observer.IsOnField) continue;

                if (LineOfSight.CanSee(battle, observer, target))
                    return observer.Id;
            }

            return UnitId.None;
        }

        private static int IndexOfArmy(BattleState battle, PlayerId player)
        {
            for (int i = 0; i < battle.Armies.Count; i++)
            {
                if (battle.Armies[i].Player == player)
                    return i;
            }

            return -1;
        }
    }

    /// <summary>
    /// Keeps the sightings current as the battle moves.
    /// </summary>
    /// <remarks>
    /// Runs first, before orders, so everything downstream this turn acts on
    /// what is visible now rather than on last tick's picture. Recomputed every
    /// few ticks rather than every one: regiments move at most a few metres a
    /// second, so a second-and-a-half-old sighting is indistinguishable from a
    /// fresh one and costs a fifth as much.
    /// </remarks>
    public sealed class VisionSystem : IBattleSystem
    {
        /// <summary>Ticks between recomputes.</summary>
        public const int RefreshIntervalTicks = 5;

        public string Name => "Vision";

        public void Step(BattleState battle, int tick, IBattleLog log)
        {
            if (battle == null) throw new ArgumentNullException(nameof(battle));
            if (tick % RefreshIntervalTicks != 0) return;

            battle.Vision.Recompute(battle);
        }
    }
}
