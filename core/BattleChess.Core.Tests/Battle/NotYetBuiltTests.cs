using Xunit;

namespace BattleChess.Tests.Battle
{
    /// <summary>
    /// The combat scenarios that cannot be tested yet, written down and
    /// switched off.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every test here names a rule the design calls for and the game does not
    /// have. They are skipped rather than absent so the gap is visible in the
    /// test run instead of living in somebody's head — a skipped test is a
    /// commitment with a name, and it turns "we should test flanking marches
    /// one day" into a line item that argues for itself every time the suite
    /// runs.
    /// </para>
    /// <para>
    /// <b>To bring one back:</b> delete the <c>Skip</c> and write the body. The
    /// name is already the specification.
    /// </para>
    /// </remarks>
    public sealed class NotYetBuiltTests
    {
        // ---- M4: fog of war ---------------------------------------------------

        // ShootersCannotFireAtEnemiesTheyCannotSee and
        // AHillBetweenTwoRegimentsStopsTheVolleys have moved to VisionTests —
        // shooting consults sight now.

        [Fact(Skip = "M4 pass 4 — the battleships mechanic proper. No remembered positions to fire at yet.")]
        public void ArtilleryFiringBlindLearnsOnlyWhetherItHit()
        {
        }

        // ---- Task 16: enveloping ---------------------------------------------

        [Fact(Skip = "Task 16 — a large regiment cannot yet split into bodies to surround a smaller one.")]
        public void ALargeRegimentSplitsToSurroundASmallOne()
        {
        }

        [Fact(Skip = "Task 16 — the payoff for enveloping: frontage stops protecting the smaller unit.")]
        public void BeingSurroundedRemovesTheFrontageAdvantageOfBeingSmall()
        {
        }

        [Fact(Skip = "Task 16 — splitting should cost organization and leave each body weaker alone.")]
        public void SplittingToEnvelopeCostsOrganization()
        {
        }

        // ---- Commander --------------------------------------------------------

        [Fact(Skip = "Commander unit is deferred — there is no such unit type yet.")]
        public void ACommanderSteadiesTheRegimentsAroundHim()
        {
        }

        [Fact(Skip = "Commander unit is deferred. This is the win condition the design turns on.")]
        public void KillingTheCommanderCanCollapseAnArmy()
        {
        }

        // ---- Terrain-specific unit bonuses ------------------------------------

        [Fact(Skip = "Discussed but not built — units have no per-terrain combat bonuses yet, only speed.")]
        public void MountainTroopsFightBetterInTheMountains()
        {
        }

        // ---- Frontage control -------------------------------------------------

        [Fact(Skip = "TODO — the player cannot yet set file width and rank depth to concentrate a unit.")]
        public void NarrowingAUnitConcentratesItsFrontageAtTheCostOfWidth()
        {
        }

        // ---- Reshaping under fire ---------------------------------------------

        [Fact(Skip = "Not built — AdoptFormation is instant. A regiment caught mid-manoeuvre is the classic disaster.")]
        public void ARegimentCaughtChangingFormationIsAtItsMostVulnerable()
        {
        }

        // ---- M5: the match around the battle ----------------------------------

        [Fact(Skip = "M5 — there are no win conditions yet, so a fight has no way to end other than casualties.")]
        public void AnArmyThatHasLostItsWillSurrenders()
        {
        }

        [Fact(Skip = "M5 — WEGO is not built. Movement resolves immediately rather than on committed orders.")]
        public void BothSidesOrdersResolveSimultaneously()
        {
        }

        [Fact(Skip = "M5 — no replay format yet. This is the golden test that locks outcomes against rule changes.")]
        public void ARecordedBattleReplaysToTheSameFinalState()
        {
        }

        // ---- Things the design has ruled out for now --------------------------

        [Fact(Skip = "Not designed — there is no fatigue. Long fights cost morale and men, never wind.")]
        public void MenTireOverALongEngagement()
        {
        }

        [Fact(Skip = "Not designed — volleys cannot hit friendly troops in the way.")]
        public void ShootingPastFriendsRisksHittingThem()
        {
        }
    }
}
