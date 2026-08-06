using System;
using BattleChess.Contracts;
using BattleChess.Rules;

namespace BattleChess.Tests.Battle
{
    /// <summary>What a duel came to.</summary>
    public sealed class DuelResult
    {
        public UnitInstance Attacker { get; init; } = null!;
        public UnitInstance Defender { get; init; } = null!;

        /// <summary>Percentage of its original strength the attacker lost.</summary>
        public float AttackerLost { get; init; }

        /// <summary>Percentage of its original strength the defender lost.</summary>
        public float DefenderLost { get; init; }

        public int Turns { get; init; }

        /// <summary>Whether one side actually left the field, rather than the clock running out.</summary>
        public bool Decided { get; init; }

        public string Winner { get; init; } = string.Empty;

        public bool AttackerWon => Winner == Attacker.Def.DisplayName;
        public bool DefenderWon => Winner == Defender.Def.DisplayName;

        /// <summary>Everything a failing assertion needs to be understood without a debugger.</summary>
        public override string ToString() =>
            $"{Attacker.Def.DisplayName} lost {AttackerLost:0}% ({Attacker.State}, morale {Attacker.Morale:0.00}), " +
            $"{Defender.Def.DisplayName} lost {DefenderLost:0}% ({Defender.State}, morale {Defender.Morale:0.00}) " +
            $"after {Turns} turn(s); winner {Winner}.";
    }

    /// <summary>
    /// Two regiments, one fight, run to a decision.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The workhorse of the combat suite. The attacker stands at the middle of
    /// an empty plain facing east and the defender faces back at it, either
    /// nose to nose or across a stated gap, and both sides mean it unless told
    /// otherwise.
    /// </para>
    /// <para>
    /// Every result is reported as a percentage of each side's starting
    /// strength, never as a headcount, so a test says what it means about the
    /// design and survives anyone changing how big a regiment is.
    /// </para>
    /// </remarks>
    public sealed class Duel
    {
        public string Attacker { get; init; } = "swordsmen";
        public string Defender { get; init; } = "swordsmen";

        /// <summary>Zero means the unit's own default strength.</summary>
        public int AttackerStrength { get; init; }
        public int DefenderStrength { get; init; }

        public string AttackerFormation { get; init; } = "line";
        public string DefenderFormation { get; init; } = "line";

        /// <summary>Terrain covering the whole field. Both sides stand on it.</summary>
        public string Ground { get; init; } = "plains";

        /// <summary>Metres between them at the start. Zero puts them in contact.</summary>
        public float StartDistance { get; init; }

        /// <summary>Whether the attacker closes and then keeps after a beaten enemy.</summary>
        public bool AttackerPresses { get; init; } = true;

        /// <summary>Whether the defender does the same, or simply stands.</summary>
        public bool DefenderPresses { get; init; } = true;

        public int MaxTurns { get; init; } = 15;

        public ulong Seed { get; init; } = 1000;

        public DuelResult Fight()
        {
            var field = new Battlefield(Ground, Seed);

            UnitInstance a = field.Add(0, Attacker, field.Centre, Facing.East, AttackerStrength, AttackerFormation);

            Footprint defenderShape = TestContent.Formation(DefenderFormation)
                .ApplyTo(TestContent.Unit(Defender).NaturalFormation)
                .FootprintFor(DefenderStrength > 0 ? DefenderStrength : TestContent.Unit(Defender).DefaultStrength);

            float gap = StartDistance > 0f
                ? StartDistance
                : a.Footprint.HalfDepth + defenderShape.HalfDepth + 4f;

            UnitInstance b = field.Add(1, Defender, field.Centre + new Vec2(gap, 0f), Facing.West,
                DefenderStrength, DefenderFormation);

            if (AttackerPresses) Battlefield.Press(a, b); else Battlefield.Hold(a);
            if (DefenderPresses) Battlefield.Press(b, a); else Battlefield.Hold(b);

            int turns = field.RunUntilDecided(MaxTurns, a, b);

            float attackerLost = Battlefield.LostPercent(a);
            float defenderLost = Battlefield.LostPercent(b);

            // Driven from the field settles it; short of that, whoever bled less.
            string winner =
                !b.IsOnField ? a.Def.DisplayName :
                !a.IsOnField ? b.Def.DisplayName :
                attackerLost <= defenderLost ? a.Def.DisplayName : b.Def.DisplayName;

            return new DuelResult
            {
                Attacker = a,
                Defender = b,
                AttackerLost = attackerLost,
                DefenderLost = defenderLost,
                Turns = turns,
                Decided = !a.IsOnField || !b.IsOnField,
                Winner = winner,
            };
        }
    }
}
