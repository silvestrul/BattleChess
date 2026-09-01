using System;
using BattleChess.Contracts;

namespace BattleChess.Rules
{
    /// <summary>
    /// Whether one regiment can see another, and how far a regiment can see at
    /// all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two quite different things stop sight, and keeping them apart is the
    /// whole of this rule. <b>Height</b> is opaque: ground higher than both ends
    /// of the line blocks it outright, which is why an army can hide behind a
    /// ridge and why marching round a range rather than over it is a real plan.
    /// <b>Density</b> is not: woods merely eat the distance you can see, so you
    /// make out the regiment at the treeline and not the one behind it.
    /// </para>
    /// <para>
    /// Being seen is separate again. How far off a regiment is noticed depends
    /// on the regiment — light horse are spotted at half the distance anything
    /// else is, a battery at rather more — which is what makes reconnaissance a
    /// thing you do by riding forward rather than a radius you own.
    /// </para>
    /// <para>
    /// No vision grid and no fog map. Fog hides units, not ground, so the only
    /// question ever asked is whether these two regiments can see each other,
    /// and that is a walk along a line.
    /// </para>
    /// </remarks>
    public static class LineOfSight
    {
        /// <summary>How finely the line between two units is sampled, in metres.</summary>
        /// <remarks>
        /// Well under the 25 m authoring cell, so a single cell of ridge cannot
        /// be stepped over — and coarse enough that a 400 m sight line is forty
        /// terrain lookups rather than four hundred.
        /// </remarks>
        public const float SampleStep = 10f;

        /// <summary>
        /// How close a unit standing in concealing terrain must be before it is
        /// picked up at all, as a fraction of the usual detection range.
        /// </summary>
        public const float ConcealedDetectionFraction = 0.35f;

        /// <summary>
        /// How far a unit can see from where it stands, in metres.
        /// </summary>
        public static float SightRange(BattleState battle, UnitInstance observer)
        {
            if (battle == null) throw new ArgumentNullException(nameof(battle));
            if (observer == null) throw new ArgumentNullException(nameof(observer));

            float bonus = battle.TerrainAt(observer.Position).Get(TerrainAttributes.VisionBonus);

            // [M141]. The catalogue's vision is a *proportion*, not a distance:
            // what turns it into metres is how far the fastest unit marches in
            // the window a simultaneous turn is committed blind for. Ground
            // still has the last word on top of it.
            float range = SightHorizon.InUse
                ? SightHorizon.BaseRangeOf(battle.UnitCatalogue, observer.Def)
                : observer.Def.Get(UnitAttributes.Vision);

            return range * (1f + bonus);
        }

        /// <summary>
        /// How far off this particular unit would be noticed by an observer with
        /// the given sight, in metres.
        /// </summary>
        /// <remarks>
        /// A regiment is not equally visible from every trade. Scouts are half
        /// as conspicuous as anyone else and guns rather more, and standing in
        /// woods cuts it further still.
        /// </remarks>
        public static float DetectionRange(BattleState battle, UnitInstance target, float observerSight)
        {
            float range = observerSight * target.Def.Get(UnitAttributes.Visibility);

            if (battle.TerrainAt(target.Position).Get(TerrainAttributes.Conceals))
                range *= ConcealedDetectionFraction;

            return range;
        }

        /// <summary>Whether one regiment can currently make out another.</summary>
        public static bool CanSee(BattleState battle, UnitInstance observer, UnitInstance target)
        {
            if (battle == null) throw new ArgumentNullException(nameof(battle));
            if (!observer.IsOnField || !target.IsOnField) return false;

            float sight = SightRange(battle, observer);

            // Centre to centre. A regiment is a hundred metres wide and this
            // ignores that, exactly as contact does — one simplification used
            // consistently beats two different notions of where a unit is.
            float distance = Vec2.Distance(observer.Position, target.Position);

            if (distance > DetectionRange(battle, target, sight)) return false;

            return HasLineOfSight(battle, observer.Position, target.Position, sight);
        }

        /// <summary>
        /// Whether the ground between two points lets sight through, given how
        /// far the observer can see.
        /// </summary>
        public static bool HasLineOfSight(BattleState battle, Vec2 from, Vec2 to, float sight)
        {
            float distance = Vec2.Distance(from, to);
            if (distance <= SampleStep) return true;

            int fromElevation = battle.TerrainAt(from).Get(TerrainAttributes.Elevation);
            int toElevation = battle.TerrainAt(to).Get(TerrainAttributes.Elevation);

            // Sight grazes the higher of the two ends. Ground above that line is
            // a ridge between them; ground below it is something they both look
            // over.
            int clears = Math.Max(fromElevation, toElevation);

            int steps = (int)MathF.Ceiling(distance / SampleStep);
            Vec2 stride = (to - from) / steps;

            float spent = 0f;
            float metresPerStep = distance / steps;

            for (int i = 1; i < steps; i++)
            {
                TerrainDef ground = battle.TerrainAt(from + stride * i);

                if (ground.Get(TerrainAttributes.Elevation) > clears)
                    return false;

                spent += metresPerStep * SightCostFrom(ground, fromElevation);

                if (spent > sight)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// What a metre of this ground costs an observer standing at a given
        /// height.
        /// </summary>
        /// <remarks>
        /// Looking down into woods is easier than looking through them from
        /// among the trunks, so height thins the canopy without ever clearing
        /// it: at a cost of 4, a hill sees more than twice as far into a forest
        /// as the flat does, and a mountain further again. It never becomes free
        /// — a regiment under a canopy is hard to count from any height.
        /// </remarks>
        private static float SightCostFrom(TerrainDef ground, int observerElevation)
        {
            float cost = ground.Get(TerrainAttributes.SightCost);
            if (cost <= 1f) return cost;

            int advantage = Math.Max(0, observerElevation - ground.Get(TerrainAttributes.Elevation));
            if (advantage <= 0) return cost;

            return 1f + (cost - 1f) / (1f + advantage);
        }
    }
}
