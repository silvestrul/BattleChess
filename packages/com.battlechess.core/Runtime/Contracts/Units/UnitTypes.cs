using System;

namespace BattleChess.Contracts
{
    /// <summary>
    /// What role a unit plays, and therefore how the matchup rules treat it.
    /// </summary>
    /// <remarks>
    /// Kept as an enum rather than data because the counter relationships are
    /// rules, not content — combat needs to reason about "is this cavalry
    /// charging a spear wall", and that cannot be expressed by numbers alone.
    /// </remarks>
    public enum UnitClass
    {
        /// <summary>Spears and pikes. Beat cavalry frontally, vulnerable when flanked.</summary>
        Spear = 0,

        /// <summary>Close-quarters infantry. The dependable line-holder.</summary>
        Sword = 1,

        /// <summary>Fast, devastating in the charge, poor in rough country.</summary>
        Cavalry = 2,

        /// <summary>Missile infantry. Dangerous at range, helpless in melee.</summary>
        Archer = 3,

        /// <summary>Long range, road-bound, near-defenceless up close.</summary>
        Artillery = 4,

        /// <summary>Fast and far-seeing, negligible in a fight.</summary>
        Scout = 5
    }

    /// <summary>
    /// A handle to a unit type, valid only against the catalogue that issued it.
    /// </summary>
    public readonly struct UnitTypeId : IEquatable<UnitTypeId>, IComparable<UnitTypeId>
    {
        public static readonly UnitTypeId None = new UnitTypeId(-1);

        public readonly int Index;

        public UnitTypeId(int index) => Index = index;

        public bool IsValid => Index >= 0;

        public bool Equals(UnitTypeId other) => Index == other.Index;
        public override bool Equals(object? obj) => obj is UnitTypeId other && Equals(other);
        public override int GetHashCode() => Index;
        public int CompareTo(UnitTypeId other) => Index.CompareTo(other.Index);
        public override string ToString() => IsValid ? $"UT{Index}" : "UT-";

        public static bool operator ==(UnitTypeId a, UnitTypeId b) => a.Index == b.Index;
        public static bool operator !=(UnitTypeId a, UnitTypeId b) => a.Index != b.Index;
    }

    /// <summary>
    /// How a regiment arranges itself on the ground.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The reason a unit's <see cref="Footprint"/> is computed rather than
    /// stored. Men stand in ranks; the number of files is the headcount divided
    /// by the number of ranks; frontage is files times the width each man needs.
    /// </para>
    /// <para>
    /// This falls out of two decisions at once. Regiments can be raised at any
    /// size, and a regiment that has taken casualties covers proportionally less
    /// ground — both are the same formula, so a 250-man regiment and a 500-man
    /// one at half strength occupy identical frontage and, since combat values
    /// are per man, hit equally hard.
    /// </para>
    /// </remarks>
    public readonly struct Formation
    {
        /// <summary>How many ranks deep the unit forms up.</summary>
        public readonly int Ranks;

        /// <summary>Frontage each man occupies, in metres.</summary>
        public readonly float FileWidth;

        /// <summary>Spacing between ranks, in metres.</summary>
        public readonly float RankDepth;

        public Formation(int ranks, float fileWidth, float rankDepth)
        {
            if (ranks <= 0)
                throw new ArgumentOutOfRangeException(nameof(ranks), ranks, "A formation needs at least one rank.");
            if (!(fileWidth > 0f) || float.IsInfinity(fileWidth))
                throw new ArgumentOutOfRangeException(nameof(fileWidth), fileWidth, "File width must be finite and positive.");
            if (!(rankDepth > 0f) || float.IsInfinity(rankDepth))
                throw new ArgumentOutOfRangeException(nameof(rankDepth), rankDepth, "Rank depth must be finite and positive.");

            Ranks = ranks;
            FileWidth = fileWidth;
            RankDepth = rankDepth;
        }

        /// <summary>
        /// This order with its depth and spacing scaled — how a named formation
        /// is derived from a unit's natural order.
        /// </summary>
        /// <remarks>
        /// Multipliers rather than absolute ranks, so one formation definition
        /// works for every unit type. "Form column" means the same thing to
        /// four-rank spearmen and three-rank cavalry, without either needing its
        /// own entry.
        /// </remarks>
        public Formation Scaled(float rankMultiplier, float fileWidthMultiplier, float rankDepthMultiplier)
        {
            int ranks = Math.Max(1, (int)MathF.Round(Ranks * rankMultiplier));

            return new Formation(
                ranks,
                FileWidth * fileWidthMultiplier,
                RankDepth * rankDepthMultiplier);
        }

        /// <summary>
        /// The ground a body of this many men covers.
        /// </summary>
        /// <remarks>
        /// A unit reduced below one full rank keeps a single rank and simply
        /// narrows, rather than collapsing to nothing.
        /// </remarks>
        public Footprint FootprintFor(int strength)
        {
            if (strength <= 0)
                throw new ArgumentOutOfRangeException(nameof(strength), strength, "A unit with no men has no footprint.");

            int usedRanks = Math.Min(Ranks, strength);
            int files = (int)MathF.Ceiling(strength / (float)usedRanks);

            return new Footprint(files * FileWidth, usedRanks * RankDepth);
        }

        public override string ToString() => $"{Ranks} ranks, {FileWidth:0.##}m per file";
    }
}
