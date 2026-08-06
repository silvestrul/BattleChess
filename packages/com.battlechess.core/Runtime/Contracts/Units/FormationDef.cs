using System;
using System.Collections.Generic;

namespace BattleChess.Contracts
{
    /// <summary>
    /// A handle to a formation order, valid only against the catalogue that
    /// issued it.
    /// </summary>
    public readonly struct FormationId : IEquatable<FormationId>, IComparable<FormationId>
    {
        public static readonly FormationId None = new FormationId(-1);

        public readonly int Index;

        public FormationId(int index) => Index = index;

        public bool IsValid => Index >= 0;

        public bool Equals(FormationId other) => Index == other.Index;
        public override bool Equals(object? obj) => obj is FormationId other && Equals(other);
        public override int GetHashCode() => Index;
        public int CompareTo(FormationId other) => Index.CompareTo(other.Index);
        public override string ToString() => IsValid ? $"F{Index}" : "F-";

        public static bool operator ==(FormationId a, FormationId b) => a.Index == b.Index;
        public static bool operator !=(FormationId a, FormationId b) => a.Index != b.Index;
    }

    /// <summary>
    /// A way of drawing a regiment up — line, column, square, loose order.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Expressed as multipliers on a unit's <i>natural</i> order rather than
    /// absolute numbers, so a single definition applies to every unit type.
    /// "Form column" means the same thing to four-rank spearmen and to
    /// three-rank cavalry without either needing an entry of its own.
    /// </para>
    /// <para>
    /// Reshaping is never free. <see cref="OrganizationCost"/> is what stops a
    /// unit simply adopting whichever shape currently suits — a regiment that
    /// forms square under fire has paid for the privilege, and one that has
    /// reshaped twice already is not the regiment it was.
    /// </para>
    /// </remarks>
    public sealed class FormationDef
    {
        public FormationId Id { get; }

        /// <summary>Stable key used in content files, e.g. "column".</summary>
        public string Key { get; }

        public string DisplayName { get; }

        /// <summary>Short character for the text harness.</summary>
        public char Glyph { get; }

        /// <summary>Scales how many ranks deep the unit forms up.</summary>
        public float RankMultiplier { get; }

        /// <summary>Scales the frontage each man occupies.</summary>
        public float FileWidthMultiplier { get; }

        /// <summary>Scales the spacing between ranks.</summary>
        public float RankDepthMultiplier { get; }

        /// <summary>
        /// Organization spent adopting this order, as a fraction. Zero for a
        /// unit's natural formation.
        /// </summary>
        public float OrganizationCost { get; }

        /// <summary>
        /// Scales how well the unit holds ground against an enemy trying to
        /// come through it.
        /// </summary>
        /// <remarks>
        /// Stopping a charge is about being <i>braced</i>, not merely present.
        /// Men in open order have no wall to present, so the same spearmen who
        /// stop cavalry dead in square are ridden straight through in loose
        /// order. This is what makes formation a decision rather than a
        /// preference.
        /// </remarks>
        public float StoppingMultiplier { get; }

        /// <summary>
        /// Scales how much damage the unit takes from shooting.
        /// </summary>
        /// <remarks>
        /// The other half of the formation decision, and the reason loose order
        /// exists at all. Men packed shoulder to shoulder cannot be missed;
        /// men spread out mostly can. It sets up the bind the whole system was
        /// built for — close up against cavalry and the guns tear you apart,
        /// spread out against the guns and the cavalry rides you down.
        /// </remarks>
        public float RangedVulnerability { get; }

        /// <summary>Scales how well the unit forces its way through an enemy line.</summary>
        /// <remarks>
        /// Mass concentrated on a narrow front punches through; men spread out
        /// do not. It gives cavalry a reason to form column before a charge, at
        /// the cost of the organization that reshaping spends.
        /// </remarks>
        public float BreakthroughMultiplier { get; }

        /// <summary>Free-text note shown in the interface.</summary>
        public string Description { get; }

        public FormationDef(
            FormationId id,
            string key,
            string displayName,
            char glyph,
            float rankMultiplier,
            float fileWidthMultiplier,
            float rankDepthMultiplier,
            float organizationCost,
            float stoppingMultiplier,
            float breakthroughMultiplier,
            float rangedVulnerability,
            string description)
        {
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("Formation key is required.", nameof(key));
            if (!(rankMultiplier > 0f)) throw new ArgumentOutOfRangeException(nameof(rankMultiplier), rankMultiplier, "Must be positive.");
            if (!(fileWidthMultiplier > 0f)) throw new ArgumentOutOfRangeException(nameof(fileWidthMultiplier), fileWidthMultiplier, "Must be positive.");
            if (!(rankDepthMultiplier > 0f)) throw new ArgumentOutOfRangeException(nameof(rankDepthMultiplier), rankDepthMultiplier, "Must be positive.");
            if (organizationCost < 0f) throw new ArgumentOutOfRangeException(nameof(organizationCost), organizationCost, "Cannot be negative.");
            if (!(stoppingMultiplier > 0f)) throw new ArgumentOutOfRangeException(nameof(stoppingMultiplier), stoppingMultiplier, "Must be positive.");
            if (!(breakthroughMultiplier > 0f)) throw new ArgumentOutOfRangeException(nameof(breakthroughMultiplier), breakthroughMultiplier, "Must be positive.");
            if (!(rangedVulnerability > 0f)) throw new ArgumentOutOfRangeException(nameof(rangedVulnerability), rangedVulnerability, "Must be positive.");

            Id = id;
            Key = key;
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? key : displayName;
            Glyph = glyph;
            RankMultiplier = rankMultiplier;
            FileWidthMultiplier = fileWidthMultiplier;
            RankDepthMultiplier = rankDepthMultiplier;
            OrganizationCost = organizationCost;
            StoppingMultiplier = stoppingMultiplier;
            BreakthroughMultiplier = breakthroughMultiplier;
            RangedVulnerability = rangedVulnerability;
            Description = description ?? string.Empty;
        }

        /// <summary>Applies this order to a unit's natural formation.</summary>
        public Formation ApplyTo(Formation natural) =>
            natural.Scaled(RankMultiplier, FileWidthMultiplier, RankDepthMultiplier);

        public override string ToString() => DisplayName;
    }

    /// <summary>The formation orders available in a battle.</summary>
    public interface IFormationCatalogue
    {
        int Count { get; }

        IReadOnlyList<FormationDef> All { get; }

        /// <summary>The order a unit is raised in — its natural shape, free to adopt.</summary>
        FormationDef Default { get; }

        FormationDef Get(FormationId id);

        bool TryGetByKey(string key, out FormationDef def);
    }
}
