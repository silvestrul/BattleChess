using System;
using BattleChess.Contracts;
using BattleChess.Rules;
using Xunit;
using Xunit.Abstractions;

namespace BattleChess.Tests.Battle
{
    /// <summary>
    /// Everyone can see across the window a simultaneous turn is committed
    /// blind for.
    /// </summary>
    /// <remarks>
    /// <b>[M141].</b> Two players taking their turns at once are both acting on
    /// information a turn old, which is only fair if nothing can cross from
    /// unseen ground into contact inside that window. The designer's rule:
    /// vision stays proportional to what the catalogue already says, the
    /// sharpest-eyed unit gets two turns of the fastest unit's march, and
    /// nothing sees less than two turns of its own.
    /// </remarks>
    public sealed class SightHorizonTests
    {
        private readonly ITestOutputHelper _out;

        public SightHorizonTests(ITestOutputHelper output) => _out = output;

        [Fact]
        public void EveryUnitSeesAcrossTwoTurnsOfItsOwnMarch()
        {
            IUnitCatalogue catalogue = TestContent.Units;

            _out.WriteLine(
                $"a turn is {BattleClock.TicksPerTurn * BattleClock.SecondsPerTick:0} s; " +
                $"scale x{SightHorizon.ScaleFor(catalogue):0.000}");
            _out.WriteLine($"{"unit",-14}{"speed",7}{"catalogue",11}{"horizon",9}{"own 2 turns",13}{"warning",9}");

            float fastest = 0f;
            foreach (UnitDef def in catalogue.All)
                fastest = MathF.Max(fastest, def.Get(UnitAttributes.Speed));

            foreach (UnitDef def in catalogue.All)
            {
                float speed = def.Get(UnitAttributes.Speed);
                float horizon = SightHorizon.BaseRangeOf(catalogue, def);
                float ownMarch = SightHorizon.WarningDistance(speed);

                // How long a unit has between first seeing the fastest thing on
                // the field and being reached by it. The number the rule is
                // really about.
                float warningTurns = horizon / fastest
                                   / (BattleClock.TicksPerTurn * BattleClock.SecondsPerTick);

                _out.WriteLine(
                    $"{def.DisplayName,-14}{speed,7:0.00}{def.Get(UnitAttributes.Vision),11:0}" +
                    $"{horizon,9:0}{ownMarch,13:0}{warningTurns,9:0.0}");

                Assert.True(horizon >= ownMarch - 0.5f,
                    $"{def.DisplayName} marches {ownMarch:0} m in {SightHorizon.TurnsOfWarning} turns and " +
                    $"sees {horizon:0} m. A unit that can walk further than it can see commits its orders " +
                    "into ground it has never looked at.");
            }
        }

        /// <summary>
        /// And the sharpest-eyed unit gets exactly the fastest unit's two-turn
        /// march, which is what fixes the scale.
        /// </summary>
        [Fact]
        public void TheSharpestEyesSeeTwoTurnsOfTheFastestMarch()
        {
            IUnitCatalogue catalogue = TestContent.Units;

            float fastest = 0f;
            float sharpest = 0f;
            UnitDef? eyes = null;

            foreach (UnitDef def in catalogue.All)
            {
                fastest = MathF.Max(fastest, def.Get(UnitAttributes.Speed));

                if (def.Get(UnitAttributes.Vision) > sharpest)
                {
                    sharpest = def.Get(UnitAttributes.Vision);
                    eyes = def;
                }
            }

            float want = SightHorizon.WarningDistance(fastest);
            float got = SightHorizon.BaseRangeOf(catalogue, eyes!);

            _out.WriteLine($"{eyes!.DisplayName} sees {got:0} m; two turns of the fastest march is {want:0} m");

            Assert.True(MathF.Abs(got - want) < 1f,
                $"{eyes.DisplayName} has the sharpest eyes in the catalogue and should see exactly two " +
                $"turns of the fastest march, {want:0} m. It sees {got:0} m.");
        }

        /// <summary>
        /// The order the catalogue puts units in is the order they keep.
        /// </summary>
        /// <remarks>
        /// The designer asked for proportional and not flat, so a scout must
        /// still out-see a spearman afterwards. A rule that raised everybody to
        /// the same horizon would satisfy the two-turn floor and quietly delete
        /// the reason to raise scouts at all.
        /// </remarks>
        [Fact]
        public void SharperEyesStaySharper()
        {
            IUnitCatalogue catalogue = TestContent.Units;

            UnitDef? sharper = null;
            UnitDef? duller = null;

            foreach (UnitDef def in catalogue.All)
            {
                if (sharper == null || def.Get(UnitAttributes.Vision) > sharper.Get(UnitAttributes.Vision))
                    sharper = def;

                if (duller == null || def.Get(UnitAttributes.Vision) < duller.Get(UnitAttributes.Vision))
                    duller = def;
            }

            // Non-vacuity: a catalogue where everybody has the same eyes cannot
            // fail this and is not evidence of anything (W9).
            Assert.True(
                sharper!.Get(UnitAttributes.Vision) > duller!.Get(UnitAttributes.Vision),
                "Every unit in the catalogue has the same vision, so this proves nothing about proportion.");

            float sharpHorizon = SightHorizon.BaseRangeOf(catalogue, sharper);
            float dullHorizon = SightHorizon.BaseRangeOf(catalogue, duller);

            _out.WriteLine(
                $"{sharper.DisplayName} {sharpHorizon:0} m against {duller.DisplayName} {dullHorizon:0} m");

            Assert.True(sharpHorizon > dullHorizon,
                $"{sharper.DisplayName} should still out-see {duller.DisplayName} after scaling, and sees " +
                $"{sharpHorizon:0} m against {dullHorizon:0} m.");
        }

        /// <summary>
        /// Change the catalogue and every horizon moves with it.
        /// </summary>
        /// <remarks>
        /// The point of deriving this rather than writing metres into
        /// <c>units.cfg</c>. Without this the rule is right on the day it is
        /// written and quietly wrong after the first balance pass, which is
        /// exactly how the equal-ground rule was broken on every field in the
        /// project.
        /// </remarks>
        [Fact]
        public void TheHorizonFollowsTheCatalogueRatherThanAFixedTable()
        {
            IUnitCatalogue catalogue = TestContent.Units;

            float before = SightHorizon.ScaleFor(catalogue);

            float fastest = 0f;
            foreach (UnitDef def in catalogue.All)
                fastest = MathF.Max(fastest, def.Get(UnitAttributes.Speed));

            // What the scale would be if the fastest unit were twice as quick.
            float sharpest = 0f;
            foreach (UnitDef def in catalogue.All)
                sharpest = MathF.Max(sharpest, def.Get(UnitAttributes.Vision));

            float ifTwiceAsFast = SightHorizon.WarningDistance(fastest * 2f) / sharpest;

            _out.WriteLine($"scale now x{before:0.000}; with a doubled fastest march x{ifTwiceAsFast:0.000}");

            Assert.True(MathF.Abs(ifTwiceAsFast - before * 2f) < 0.001f,
                "Doubling the fastest march should double every horizon. It does not, so the rule is not " +
                "actually derived from the catalogue.");
        }
    }
}
