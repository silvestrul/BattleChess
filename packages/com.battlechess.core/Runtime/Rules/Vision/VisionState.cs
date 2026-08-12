using System;
using BattleChess.Contracts;

namespace BattleChess.Rules
{
    /// <summary>
    /// What an army saw of a regiment at one moment, frozen.
    /// </summary>
    /// <remarks>
    /// Nothing here is read live. That is the entire contract: a sighting is a
    /// memory, and a memory that updates itself is not fog, it is a spy.
    /// </remarks>
    public readonly struct Sighting
    {
        /// <summary>Where they were standing.</summary>
        public readonly Vec2 Where;

        /// <summary>Which way they were facing.</summary>
        public readonly Facing Facing;

        /// <summary>How many men they had then.</summary>
        public readonly int Strength;

        /// <summary>Ticks since. One turn is sixty.</summary>
        public readonly int AgeTicks;

        public Sighting(Vec2 where, Facing facing, int strength, int ageTicks)
        {
            Where = where;
            Facing = facing;
            Strength = strength;
            AgeTicks = ageTicks;
        }

        public override string ToString() => $"{Strength} men at {Where}, {AgeTicks} ticks ago";
    }

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

        /// <summary>
        /// What each army last saw of each enemy, and when.
        /// </summary>
        /// <remarks>
        /// Strength and facing are kept alongside the position because a
        /// remembered sighting has to be remembered <i>whole</i>. Storing only
        /// where they were and reading everything else off the live unit when
        /// the marker is drawn would give the player a stale position attached
        /// to a current headcount — fog wearing a ghost's clothes.
        /// </remarks>
        private Vec2[] _lastSeenAt = Array.Empty<Vec2>();
        private Facing[] _lastSeenFacing = Array.Empty<Facing>();
        private int[] _lastSeenStrength = Array.Empty<int>();
        private int[] _lastSeenTick = Array.Empty<int>();

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
        public void Recompute(BattleState battle, int tick = 0)
        {
            if (battle == null) throw new ArgumentNullException(nameof(battle));

            InPlay = true;
            LatestTick = tick;

            int armies = battle.Armies.Count;
            int units = battle.AllUnits.Count;

            if (_seen.Length != armies * units)
            {
                _seen = new bool[armies * units];

                var places = new Vec2[armies * units];
                var bearings = new Facing[armies * units];
                var counts = new int[armies * units];
                var whens = new int[armies * units];

                // Anything already remembered survives the resize.
                for (int i = 0; i < Math.Min(_lastSeenAt.Length, places.Length); i++)
                {
                    places[i] = _lastSeenAt[i];
                    bearings[i] = _lastSeenFacing[i];
                    counts[i] = _lastSeenStrength[i];
                    whens[i] = _lastSeenTick[i];
                }

                _lastSeenAt = places;
                _lastSeenFacing = bearings;
                _lastSeenStrength = counts;
                _lastSeenTick = whens;
            }
            else
            {
                Array.Clear(_seen, 0, _seen.Length);
            }

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
                            int slot = a * units + target.Id.Value;

                            _seen[slot] = true;

                            // Note where they were. A sighting that goes stale
                            // is worth more than no sighting at all — it is the
                            // difference between fog you can plan against and
                            // simply forgetting an army exists.
                            _lastSeenAt[slot] = target.Position;
                            _lastSeenFacing[slot] = target.Facing;
                            _lastSeenStrength[slot] = target.Strength;
                            _lastSeenTick[slot] = tick;

                            break;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Where an army last saw a unit, and how long ago in ticks.
        /// </summary>
        /// <remarks>
        /// Returns false if this army has never laid eyes on it. A remembered
        /// position is exactly as reliable as its age: a marker ten seconds old
        /// is almost certainly still right, one from three turns ago is a guess
        /// that the enemy has probably already made wrong.
        /// </remarks>
        public bool TryRecall(BattleState battle, PlayerId viewer, UnitInstance target, out Vec2 where, out int age)
        {
            bool recalled = TryRecall(battle, viewer, target, out Sighting sighting);

            where = sighting.Where;
            age = sighting.AgeTicks;

            return recalled;
        }

        /// <summary>
        /// The whole of what an army last saw of a unit: where, facing which
        /// way, how many, and how long ago.
        /// </summary>
        public bool TryRecall(BattleState battle, PlayerId viewer, UnitInstance target, out Sighting sighting)
        {
            sighting = default;

            int army = IndexOfArmy(battle, viewer);
            if (army < 0 || _units == 0) return false;

            int slot = army * _units + target.Id.Value;
            if (slot < 0 || slot >= _lastSeenAt.Length) return false;
            if (_lastSeenTick[slot] == 0 && _lastSeenAt[slot].IsNearZero) return false;

            sighting = new Sighting(
                _lastSeenAt[slot],
                _lastSeenFacing[slot],
                _lastSeenStrength[slot],
                Math.Max(0, LatestTick - _lastSeenTick[slot]));

            return true;
        }

        /// <summary>The tick the last recompute ran on.</summary>
        public int LatestTick { get; private set; }

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

            battle.Vision.Recompute(battle, tick);
        }
    }
}
