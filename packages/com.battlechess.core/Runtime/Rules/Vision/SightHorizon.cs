using System;
using System.Runtime.CompilerServices;
using BattleChess.Contracts;

namespace BattleChess.Rules
{
    /// <summary>
    /// How far every unit must be able to see for orders to be worth
    /// committing, derived from the catalogue rather than written into it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>[M141], and it exists because turns will be taken simultaneously.</b>
    /// Two players committing orders at the same time are both acting on
    /// information a turn old. That is only fair if nothing can cross from
    /// unseen ground into contact inside the window they were blind for - so
    /// sight has to be sized against <see cref="TurnsOfWarning"/> turns of
    /// marching, not chosen by feel.
    /// </para>
    /// <para>
    /// <b>The designer's rule, in their own words:</b> vision stays
    /// <i>proportional</i> to what the catalogue already says, the unit with
    /// the sharpest eyes gets two turns of the fastest unit's march, and
    /// anything with naturally poorer sight gets proportionally less - with a
    /// floor of two turns of its <i>own</i> march underneath.
    /// </para>
    /// <para>
    /// <b>Derived, never written down.</b> The whole point is that it follows
    /// the catalogue: change a speed or a vision in <c>units.cfg</c> and every
    /// horizon moves with it. A table of metres would be right on the day it
    /// was written and quietly wrong afterwards, which is the failure the
    /// equal-ground rule already cost this project once.
    /// </para>
    /// <para>
    /// <b>What it does not promise.</b> The floor is each unit's own march, not
    /// the fastest march on the field, so a spearman does not see far enough to
    /// be untouchable by cavalry - he sees far enough to have about a turn and
    /// a half of warning. That is the designer's call and it is the interesting
    /// one: being outrun is a real thing that should happen, being ambushed
    /// from nowhere is not.
    /// </para>
    /// </remarks>
    public static class SightHorizon
    {
        /// <summary>
        /// Whether line of sight actually uses these horizons yet.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Off, deliberately, and it is not a half-finished switch.</b> The
        /// rule exists to make a <i>simultaneous</i> turn fair, and turns are
        /// not simultaneous yet. Turning it on now would raise every horizon by
        /// two and a half times in a game where nobody is committing orders
        /// blind, which buys nothing and costs the six vision arrangements that
        /// were built against the old metres - a hundred metres of wood is
        /// thick cover at 260 m of sight and thin at 660.
        /// </para>
        /// <para>
        /// Those six want re-recording <i>with</i> the feature they are for, so
        /// that what they measure and what the game does move together. Flip
        /// this the same day simultaneous turns land, not before.
        /// </para>
        /// </remarks>
        public static bool InUse;

        /// <summary>
        /// How many turns of marching a unit must be able to see across.
        /// </summary>
        /// <remarks>
        /// Two, because a simultaneous turn is committed blind and then
        /// resolved: one turn to see it coming and one to have already ordered
        /// something about it.
        /// </remarks>
        public const int TurnsOfWarning = 2;

        /// <summary>How far a unit walks in <see cref="TurnsOfWarning"/> turns, unobstructed.</summary>
        public static float WarningDistance(float metresPerSecond) =>
            metresPerSecond * BattleClock.SecondsPerTick * BattleClock.TicksPerTurn * TurnsOfWarning;

        /// <summary>
        /// What every unit's catalogue vision is multiplied by, so that the
        /// sharpest-eyed unit sees exactly <see cref="TurnsOfWarning"/> turns of
        /// the fastest unit's march.
        /// </summary>
        /// <remarks>
        /// Cached against the catalogue instance rather than recomputed, since
        /// it is asked once per line of sight and a catalogue never changes
        /// after it is built. A table keyed on the instance rather than a static
        /// field, because tests build catalogues of their own and two of them
        /// must not share an answer.
        /// </remarks>
        public static float ScaleFor(IUnitCatalogue catalogue)
        {
            if (catalogue == null) throw new ArgumentNullException(nameof(catalogue));

            if (Scales.TryGetValue(catalogue, out object cached)) return (float)cached;

            float fastest = 0f;
            float sharpest = 0f;

            foreach (UnitDef def in catalogue.All)
            {
                float speed = def.Get(UnitAttributes.Speed);
                float vision = def.Get(UnitAttributes.Vision);

                if (speed > fastest) fastest = speed;
                if (vision > sharpest) sharpest = vision;
            }

            // A catalogue with nobody in it, or one whose units are all blind,
            // leaves the numbers alone rather than dividing by nothing.
            float scale = sharpest > 0f && fastest > 0f
                ? WarningDistance(fastest) / sharpest
                : 1f;

            Scales.Add(catalogue, scale);

            return scale;
        }

        /// <summary>
        /// How far this unit sees on flat open ground, before the terrain it is
        /// standing on has its say.
        /// </summary>
        public static float BaseRangeOf(IUnitCatalogue catalogue, UnitDef def)
        {
            if (def == null) throw new ArgumentNullException(nameof(def));

            float proportional = def.Get(UnitAttributes.Vision) * ScaleFor(catalogue);

            // The floor. Currently it never binds for any unit in the shipped
            // catalogue - the proportional share is larger for every one of
            // them - and that is said out loud rather than left to be
            // discovered, because a guard that cannot fire looks exactly like a
            // guard that works (W9). It is here for a catalogue that gives
            // something fast the eyes of a mole.
            float ownMarch = WarningDistance(def.Get(UnitAttributes.Speed));

            return proportional > ownMarch ? proportional : ownMarch;
        }

        private static readonly ConditionalWeakTable<IUnitCatalogue, object> Scales =
            new ConditionalWeakTable<IUnitCatalogue, object>();
    }
}
