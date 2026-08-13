using BattleChess.Contracts;
using BattleChess.Rules;
using Xunit;

namespace BattleChess.Tests.Battle
{
    /// <summary>
    /// A rectangle has four faces, and the face decides how many men answer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Front and rear are both a full frontage: the back rank is exactly as wide
    /// as the first, so every man in it has somewhere to face. The two sides are
    /// the depth — one man per rank, ten for a ten-rank body.
    /// </para>
    /// <para>
    /// This went wrong by being asked as one question instead of two. A single
    /// test for "is the enemy off my front" lumped the rear in with the sides,
    /// so a regiment of eight hundred taken squarely from behind answered with
    /// thirteen men, and once holes had opened in its line it answered with
    /// none. A recorded game had four of eleven engagements open with one side
    /// bringing nothing at all — regiments stood in contact doing nothing to
    /// each other, which reads as the fight having hung rather than as a rule.
    /// </para>
    /// <para>
    /// Asked of the counts directly rather than through who wins. A melee has
    /// too many other rules in it — charge, morale, terrain, armour — for an
    /// outcome to isolate this one, and the last three attempts to pin geometry
    /// through outcomes each pinned something else by accident.
    /// </para>
    /// </remarks>
    public sealed class FourFacesTests
    {
        /// <summary>
        /// How many men a regiment brings when attacked from a given quarter,
        /// with the attacker squared up on that face.
        /// </summary>
        /// <remarks>
        /// The defender always faces east. Where the attacker stands is the
        /// whole of what these tests vary, so it is the only argument.
        /// </remarks>
        private static (int Defender, int Attacker) Facing(Vec2 attackerFrom, Facing attackerFacing)
        {
            var field = new Battlefield("plains", 5150);

            UnitInstance defender = field.Add(0, "swordsmen", field.Centre, Contracts.Facing.East);
            UnitInstance attacker = field.Add(1, "swordsmen", field.Centre + attackerFrom, attackerFacing);

            return (CombatSystem.FightingMen(defender, attacker),
                    CombatSystem.FightingMen(attacker, defender));
        }

        // Clear of the block either way: it is 40 m by 20 m, so 45 m out is
        // beyond the front and 45 m up is beyond the side.
        private static readonly Vec2 DueEast = new Vec2(45f, 0f);
        private static readonly Vec2 DueWest = new Vec2(-45f, 0f);
        private static readonly Vec2 DueNorth = new Vec2(0f, 45f);

        [Fact]
        public void ARegimentTakenFromBehindTurnsItsBackRankNotItsSide()
        {
            (int fromBehind, _) = Facing(DueWest, Contracts.Facing.East);
            (int fromTheSide, _) = Facing(DueNorth, Contracts.Facing.South);

            // The back rank is a hundred men where the side is eight, so the two
            // cannot be within a factor of two of each other. They used to be
            // identical, which is the whole of what this is guarding.
            Assert.True(fromBehind > fromTheSide * 2,
                $"Attacked from behind a regiment answers with {fromBehind} men and from the side with " +
                $"{fromTheSide}. The rear is being treated as a flank — its back rank is as wide as its " +
                "front and should bring far more men than its depth can.");
        }

        [Fact]
        public void TheSideIsStillOnlyAsWideAsTheRegimentIsDeep()
        {
            (int fromTheFront, _) = Facing(DueEast, Contracts.Facing.West);
            (int fromTheSide, _) = Facing(DueNorth, Contracts.Facing.South);

            // The other half of the rule, and the one at risk from the fix: it
            // would be easy to widen the rear by widening everything.
            Assert.True(fromTheSide * 4 < fromTheFront,
                $"A flanked regiment answered with {fromTheSide} men against the {fromTheFront} it brings to " +
                "its front. A side is its depth, and being caught on one has to stay far worse than " +
                "being met square on.");
        }

        [Fact]
        public void HolesInTheLineDoNotEraseANarrowFaceAltogether()
        {
            var field = new Battlefield("plains", 5150);

            UnitInstance defender = field.Add(0, "swordsmen", field.Centre, Contracts.Facing.East);
            UnitInstance attacker = field.Add(1, "swordsmen", field.Centre + DueNorth, Contracts.Facing.South);

            // Twenty holes in a regiment of eight hundred: a fifth of nothing
            // much, and ordinary a few pulses into any real fight.
            defender.FrontRankGaps = 20f;

            int answering = CombatSystem.FightingMen(defender, attacker);

            // Gaps used to come off as a flat count of men. A side is eight men
            // wide, so twenty gaps anywhere in the regiment subtracted more than
            // the entire face and the flank resolved to nothing at all.
            Assert.True(answering > 0,
                "A regiment with a few holes in its line answered a flank attack with nobody. Gaps are " +
                "being charged as a flat number against a face only eight men wide, so any real fight " +
                "erases the side completely.");
        }

        [Fact]
        public void AgainstTheFrontAGapIsStillWorthAboutOneMan()
        {
            var field = new Battlefield("plains", 5150);

            UnitInstance whole = field.Add(0, "swordsmen", field.Centre, Contracts.Facing.East);
            UnitInstance attacker = field.Add(1, "swordsmen", field.Centre + DueEast, Contracts.Facing.West);

            int before = CombatSystem.FightingMen(whole, attacker);

            whole.FrontRankGaps = 20f;

            int after = CombatSystem.FightingMen(whole, attacker);

            // The regression guard for the change above. Charging gaps as a
            // share of the face is only defensible because against a full
            // frontage a share and a flat count are the same thing — twenty
            // gaps in a hundred files still costs exactly twenty files. If this
            // drifts, F13 has been quietly rewritten by a fix aimed at F19.
            //
            // Counted in effective men rather than in files, so the figure is
            // twenty from the front rank plus the two supporting ranks behind
            // them at three tenths each: twenty times 1.6, or thirty-two. The
            // band is wide enough to survive a change to what a supporting rank
            // is worth and narrow enough to fail if the front rank stops losing
            // twenty.
            int cost = before - after;

            Assert.InRange(cost, 26, 38);
        }
    }
}
