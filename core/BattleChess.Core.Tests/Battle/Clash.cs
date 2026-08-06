using BattleChess.Contracts;
using BattleChess.Rules;

namespace BattleChess.Tests.Battle
{
    /// <summary>What a fixed exchange came to.</summary>
    public sealed class ClashResult
    {
        public UnitInstance Attacker { get; init; } = null!;
        public UnitInstance Defender { get; init; } = null!;

        public float AttackerLost { get; init; }
        public float DefenderLost { get; init; }

        /// <summary>How much morale the defender was left with, 0 to 1.</summary>
        public float DefenderMorale => Defender.Morale;
        public float AttackerMorale => Attacker.Morale;

        public override string ToString() =>
            $"{Attacker.Def.DisplayName} lost {AttackerLost:0.0}% (morale {Attacker.Morale:0.000}), " +
            $"{Defender.Def.DisplayName} lost {DefenderLost:0.0}% (morale {Defender.Morale:0.000}).";
    }

    /// <summary>
    /// Two regiments nailed in place, trading blows for a fixed number of
    /// pulses.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Where <see cref="Duel"/> asks what happens in a battle, this asks what a
    /// single rule does. Only melee and morale run: nobody marches, nobody
    /// wheels, nobody shoots, and neither side is allowed to walk away. So when
    /// one facing angle is compared against another, the difference is the
    /// flanking rule and nothing else.
    /// </para>
    /// <para>
    /// The defender's facing is the point of it.
    /// <see cref="DefenderFacingDegrees"/> of 180 has them looking straight
    /// back at the attacker; 90 presents a flank; 0 turns their back.
    /// </para>
    /// </remarks>
    public sealed class Clash
    {
        public string Attacker { get; init; } = "swordsmen";
        public string Defender { get; init; } = "swordsmen";

        public UnitDef? AttackerDef { get; init; }
        public UnitDef? DefenderDef { get; init; }

        public int AttackerStrength { get; init; }
        public int DefenderStrength { get; init; }

        public string AttackerFormation { get; init; } = "line";
        public string DefenderFormation { get; init; } = "line";

        public string Ground { get; init; } = "plains";

        /// <summary>180 faces the attacker, 90 presents a flank, 0 turns the back.</summary>
        public float DefenderFacingDegrees { get; init; } = 180f;

        /// <summary>How well each side is holding together at the start, 0 to 1.</summary>
        public float AttackerOrganization { get; init; } = 1f;
        public float DefenderOrganization { get; init; } = 1f;

        /// <summary>
        /// Whether the attacker is treated as having ridden in, rather than
        /// having been stood there when the enemy arrived.
        /// </summary>
        /// <remarks>
        /// Decides whether a charge bonus is collected at all, since a charge is
        /// something a regiment does. On by default because "attacker" is
        /// exactly what the name says; turn it off to measure what a regiment
        /// achieves without the moment of impact.
        /// </remarks>
        public bool AttackerCharges { get; init; } = true;

        public int Pulses { get; init; } = 6;

        public ulong Seed { get; init; } = 7000;

        public ClashResult Run()
        {
            var field = new Battlefield(Ground, Seed, RuleSet.MeleeOnly);

            UnitDef attackerDef = AttackerDef ?? TestContent.Unit(Attacker);
            UnitDef defenderDef = DefenderDef ?? TestContent.Unit(Defender);

            FormationDef attackerForm = TestContent.Formation(AttackerFormation);
            FormationDef defenderForm = TestContent.Formation(DefenderFormation);

            int attackerStrength = AttackerStrength > 0 ? AttackerStrength : attackerDef.DefaultStrength;
            int defenderStrength = DefenderStrength > 0 ? DefenderStrength : defenderDef.DefaultStrength;

            UnitInstance a = field.Add(0, attackerDef, field.Centre, Facing.East, attackerStrength, attackerForm);

            Footprint defenderShape = defenderForm.ApplyTo(defenderDef.NaturalFormation).FootprintFor(defenderStrength);
            float gap = a.Footprint.HalfDepth + defenderShape.HalfDepth + 4f;

            UnitInstance b = field.Add(1, defenderDef, field.Centre + new Vec2(gap, 0f),
                Facing.FromDegrees(DefenderFacingDegrees), defenderStrength, defenderForm);

            a.Organization = AttackerOrganization;
            b.Organization = DefenderOrganization;

            if (AttackerCharges) Battlefield.Press(a, b);
            Battlefield.Hold(b);

            field.RunPulses(Pulses);

            return new ClashResult
            {
                Attacker = a,
                Defender = b,
                AttackerLost = Battlefield.LostPercent(a),
                DefenderLost = Battlefield.LostPercent(b),
            };
        }
    }
}
