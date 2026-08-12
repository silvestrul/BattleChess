using System;
using System.Collections.Generic;
using BattleChess.Contracts;

namespace BattleChess.Rules
{
    /// <summary>
    /// Turns the authoritative battle into one commander's picture of it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The single door between the simulation and everybody outside it. There
    /// is deliberately no other way out: <see cref="BattleState"/> lives in this
    /// assembly and <see cref="PlayerView"/> in the public one, so any future
    /// client, replay writer or network layer has to come through here, and the
    /// fog leak tests only have one place to watch.
    /// </para>
    /// <para>
    /// Reads state and writes none, so it can be called at any point in a tick
    /// without disturbing the battle — including partway through, which is what
    /// the eventual replay stream will want.
    /// </para>
    /// </remarks>
    public static class PlayerViewProjector
    {
        /// <summary>
        /// The coarsest band an observer can be expected to count, as a
        /// fraction of what a full regiment of that type musters.
        /// </summary>
        /// <remarks>
        /// Five percent of six hundred spearmen is thirty men, which is about
        /// two files off a ninety-metre frontage — roughly the difference you
        /// could genuinely notice from across the field, and comfortably below
        /// the difference that changes a decision.
        /// </remarks>
        private const float CountingBand = 0.05f;

        /// <summary>The smallest band, so tiny units are not rounded into nothing.</summary>
        private const int MinimumBand = 10;

        public static PlayerView Project(BattleState battle, PlayerId viewer, int tick = 0)
        {
            if (battle == null) throw new ArgumentNullException(nameof(battle));

            var own = new List<CommandedUnit>();
            var sighted = new List<SightedUnit>();
            var remembered = new List<RememberedUnit>();

            // Ascending id order, as everywhere else in the rules. A view built
            // in hash order would differ between runs of the same seed and take
            // the replay tests down with it.
            foreach (UnitInstance unit in battle.AllUnits)
            {
                if (unit.Owner == viewer)
                {
                    own.Add(Command(unit));
                    continue;
                }

                if (battle.Vision.CanSee(battle, viewer, unit))
                {
                    sighted.Add(Sight(unit));
                    continue;
                }

                // Out of sight. Everything the player gets now is what he was
                // holding before they disappeared — including the headcount,
                // which is why the sighting had to be stored whole.
                if (battle.Vision.TryRecall(battle, viewer, unit, out Sighting memory))
                    remembered.Add(Recall(unit, memory));
            }

            return new PlayerView(
                viewer, battle.Name, battle.TurnNumber, tick, own, sighted, remembered);
        }

        private static CommandedUnit Command(UnitInstance unit) =>
            new CommandedUnit(
                unit.Id,
                unit.Def.Id,
                unit.Def.DisplayName,
                unit.Def.Class,
                unit.Def.Movement,
                unit.Position,
                unit.Facing,
                unit.Footprint,
                unit.Strength,
                unit.InitialStrength,
                unit.State,
                unit.Morale,
                unit.Organization,
                unit.ShotsLeft,
                unit.Stance,
                unit.Order,
                RemainingRoute(unit));

        private static SightedUnit Sight(UnitInstance unit) =>
            new SightedUnit(
                unit.Id,
                unit.Owner,
                unit.Def.Id,
                unit.Def.DisplayName,
                unit.Def.Class,
                unit.Def.Movement,
                unit.Position,
                unit.Facing,
                unit.Footprint,
                Estimate(unit.Def, unit.Strength),
                unit.State == UnitState.Routing);

        private static RememberedUnit Recall(UnitInstance unit, Sighting memory) =>
            new RememberedUnit(
                unit.Id,
                unit.Owner,
                unit.Def.Id,
                unit.Def.DisplayName,
                unit.Def.Class,
                memory.Where,
                memory.Facing,
                Estimate(unit.Def, memory.Strength),
                memory.AgeTicks);

        /// <summary>
        /// Rounds a headcount to the nearest band an observer could plausibly
        /// distinguish.
        /// </summary>
        /// <remarks>
        /// Deliberately not random. Noise on top of fog leaves the player
        /// unable to tell being misinformed from being outplayed, which reads as
        /// the game cheating; a coarse figure that is always coarse in the same
        /// way reads as distance, and is something a commander can reason about.
        /// </remarks>
        public static int Estimate(UnitDef def, int strength)
        {
            if (strength <= 0) return 0;

            int band = Math.Max(MinimumBand, (int)MathF.Round(def.DefaultStrength * CountingBand));
            int estimate = (int)MathF.Round(strength / (float)band) * band;

            // A regiment you can see has at least somebody in it. Rounding the
            // last few men down to nothing would report an empty field where
            // there is plainly a body of troops standing.
            return Math.Max(band, estimate);
        }

        /// <summary>The part of a march still ahead, empty if the unit is standing.</summary>
        private static IReadOnlyList<Vec2> RemainingRoute(UnitInstance unit)
        {
            if (unit.Route == null || unit.Route.IsComplete)
                return Array.Empty<Vec2>();

            var ahead = new List<Vec2>();

            for (int i = unit.Route.NextWaypoint; i < unit.Route.Waypoints.Count; i++)
                ahead.Add(unit.Route.Waypoints[i]);

            return ahead;
        }
    }
}
