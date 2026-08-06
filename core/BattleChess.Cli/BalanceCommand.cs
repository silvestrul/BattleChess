using System;
using System.Collections.Generic;
using System.IO;
using BattleChess.Contracts;
using BattleChess.Rules;

namespace BattleChess.Cli
{
    /// <summary>
    /// Sweeps the numbers across every matchup, formation and cohesion level,
    /// and flags anything that looks broken.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Builds real <see cref="UnitInstance"/> objects and reads their actual
    /// effective values rather than recomputing the formulas here. A balance
    /// report that quietly disagrees with the simulation is worse than no
    /// report, and duplicating the maths is exactly how that happens.
    /// </para>
    /// <para>
    /// The automatic warnings at the end matter more than the tables. A human
    /// scanning a grid will miss that some formation is dominated on every axis;
    /// a rule that checks it will not.
    /// </para>
    /// </remarks>
    public static class BalanceCommand
    {
        private static readonly float[] CohesionLevels = { 1.0f, 0.8f, 0.6f, 0.4f, 0.2f };

        public static int Run(string[] args)
        {
            var terrain = TerrainCatalogueReader.Read(File.ReadAllText(ContentLocator.TerrainFile()));
            var units = UnitCatalogueReader.Read(File.ReadAllText(ContentLocator.UnitsFile()));
            var formations = FormationCatalogueReader.Read(File.ReadAllText(ContentLocator.FormationsFile()));

            var warnings = new List<string>();

            Console.WriteLine();
            Console.WriteLine("  BALANCE REVIEW");
            Console.WriteLine($"  {units.Count} units, {formations.Count} formations, {terrain.Count} terrains");

            BreakthroughMatrix(units, formations, warnings);
            CohesionThresholds(units, formations);
            MovementTimes(units, terrain, warnings);
            WheelingTimes(units, warnings);
            FormationTradeoffs(units, formations, warnings);
            CheckDominatedFormations(units, formations, warnings);
            CheckHoldingGround(units, formations, warnings);

            Console.WriteLine();
            Console.WriteLine("  ---------------------------------------------------------------------");
            Console.WriteLine($"  {warnings.Count} concern(s)");
            Console.WriteLine();

            foreach (string warning in warnings)
                Console.WriteLine($"    - {warning}");

            Console.WriteLine();
            return 0;
        }

        /// <summary>Builds a unit at a given formation and cohesion, to read its real values.</summary>
        private static UnitInstance Make(UnitDef def, FormationDef formation, float organization)
        {
            var unit = new UnitInstance(
                new UnitId(0), new PlayerId(0), def, Vec2.Zero, Facing.East, def.DefaultStrength, formation);

            unit.Organization = organization;
            return unit;
        }

        // ---- Who gets through whom ------------------------------------------

        private static void BreakthroughMatrix(IUnitCatalogue units, IFormationCatalogue formations, List<string> warnings)
        {
            Console.WriteLine();
            Console.WriteLine("  BREAKTHROUGH — can the attacker ride through? (both fresh, defender in line)");
            Console.WriteLine();
            Console.Write($"    {"Attacker",-12}");

            foreach (UnitDef defender in units.All)
                Console.Write($"{Abbrev(defender.DisplayName),9}");

            Console.WriteLine();
            Console.WriteLine("    " + new string('-', 12 + units.Count * 9));

            foreach (UnitDef attackerDef in units.All)
            {
                UnitInstance attacker = Make(attackerDef, formations.Default, 1f);
                Console.Write($"    {Abbrev(attackerDef.DisplayName),-12}");

                int through = 0;

                foreach (UnitDef defenderDef in units.All)
                {
                    UnitInstance defender = Make(defenderDef, formations.Default, 1f);
                    bool passes = attacker.EffectiveBreakthrough > defender.EffectiveStoppingPower;

                    if (passes && defenderDef.Key != attackerDef.Key) through++;
                    Console.Write($"{(passes ? "through" : "  --"),9}");
                }

                Console.WriteLine();

                if (through >= units.Count - 1)
                    warnings.Add($"{attackerDef.DisplayName} rides through EVERY other unit in line — nothing on the field holds it.");
            }
        }

        // ---- Where the line breaks ------------------------------------------

        private static void CohesionThresholds(IUnitCatalogue units, IFormationCatalogue formations)
        {
            if (!units.TryGetByKey("cavalry", out UnitDef cavalryDef)) return;

            UnitInstance cavalry = Make(cavalryDef, formations.Default, 1f);
            float charge = cavalry.EffectiveBreakthrough;

            Console.WriteLine();
            Console.WriteLine($"  COHESION — lowest organization at which a defender still stops a {charge:0.00} charge");
            Console.WriteLine("  ('never' = holds even when shattered, '-' = cannot stop it even fresh)");
            Console.WriteLine();
            Console.Write($"    {"Defender",-12}");

            foreach (FormationDef formation in formations.All)
                Console.Write($"{formation.DisplayName,10}");

            Console.WriteLine();
            Console.WriteLine("    " + new string('-', 12 + formations.Count * 10));

            foreach (UnitDef defenderDef in units.All)
            {
                Console.Write($"    {Abbrev(defenderDef.DisplayName),-12}");

                foreach (FormationDef formation in formations.All)
                {
                    string threshold = "   -";

                    // Walk cohesion down and find where it stops holding.
                    for (int step = 0; step <= 20; step++)
                    {
                        float organization = 1f - step * 0.05f;
                        UnitInstance defender = Make(defenderDef, formation, organization);

                        if (charge > defender.EffectiveStoppingPower)
                        {
                            threshold = step == 0 ? "   -" : $"{organization + 0.05f:0.00}";
                            break;
                        }

                        if (step == 20) threshold = "never";
                    }

                    Console.Write($"{threshold,10}");
                }

                Console.WriteLine();
            }
        }

        // ---- Getting about ---------------------------------------------------

        private static void MovementTimes(IUnitCatalogue units, ITerrainCatalogue terrain, List<string> warnings)
        {
            var movement = new TerrainMovementModel(terrain);
            const float distance = 1000f;

            // Crossing a full kilometre of swamp is not a scenario anyone will
            // meet — terrain comes in patches. Warn against a realistic patch
            // instead, or the report drowns in complaints about terrain
            // correctly being slow.
            const float patch = 200f;
            const float patchTurnsBeforeConcern = 6f;

            Console.WriteLine();
            Console.WriteLine($"  MOVEMENT — turns to cross {distance:0} m ('-' = impassable)");
            Console.WriteLine();
            Console.Write($"    {"Terrain",-12}");

            foreach (UnitDef unit in units.All)
                Console.Write($"{Abbrev(unit.DisplayName),9}");

            Console.WriteLine();
            Console.WriteLine("    " + new string('-', 12 + units.Count * 9));

            foreach (TerrainDef ground in terrain.All)
            {
                Console.Write($"    {Abbrev(ground.DisplayName),-12}");

                foreach (UnitDef unit in units.All)
                {
                    float multiplier = movement.SpeedMultiplier(ground.Id, unit.Movement);

                    if (multiplier <= 0f)
                    {
                        Console.Write($"{"-",9}");
                        continue;
                    }

                    float turns = distance / (unit.Speed * multiplier) / BattleClock.TicksPerTurn;
                    Console.Write($"{turns,9:0.0}");

                    float patchTurns = patch / (unit.Speed * multiplier) / BattleClock.TicksPerTurn;

                    if (patchTurns > patchTurnsBeforeConcern)
                        warnings.Add($"{unit.DisplayName} needs {patchTurns:0.0} turns to cross even {patch:0} m of {ground.DisplayName} — " +
                                     "passable on paper, unusable in a battle.");
                }

                Console.WriteLine();
            }
        }

        // ---- Changing front --------------------------------------------------

        private static void WheelingTimes(IUnitCatalogue units, List<string> warnings)
        {
            Console.WriteLine();
            Console.WriteLine("  WHEELING — seconds to change front (a turn is 60)");
            Console.WriteLine();
            Console.WriteLine($"    {"Unit",-12}{"deg/s",8}{"90 deg",9}{"180 deg",10}{"turns",8}");
            Console.WriteLine("    " + new string('-', 47));

            foreach (UnitDef unit in units.All)
            {
                float rate = unit.Get(UnitAttributes.TurnRate);
                float reverse = 180f / rate;

                Console.WriteLine($"    {Abbrev(unit.DisplayName),-12}{rate,8:0.0}{90f / rate,9:0}{reverse,10:0}" +
                                  $"{reverse / BattleClock.TicksPerTurn,8:0.0}");

                if (reverse > BattleClock.TicksPerTurn * 1.5f)
                    warnings.Add($"{unit.DisplayName} needs {reverse / BattleClock.TicksPerTurn:0.0} turns to reverse — a facing mistake is close to unrecoverable.");
            }
        }

        // ---- What formations cost and buy ------------------------------------

        private static void FormationTradeoffs(IUnitCatalogue units, IFormationCatalogue formations, List<string> warnings)
        {
            Console.WriteLine();
            Console.WriteLine("  FORMATIONS — frontage, and effective stopping power for spearmen");
            Console.WriteLine();
            Console.WriteLine($"    {"Formation",-10}{"cost",7}{"stop x",8}{"break x",9}{"spear frontage",16}{"spear stopping",16}");
            Console.WriteLine("    " + new string('-', 66));

            units.TryGetByKey("spearmen", out UnitDef spearmen);

            foreach (FormationDef formation in formations.All)
            {
                string frontage = "-";
                string stopping = "-";

                if (spearmen != null)
                {
                    // Cohesion after paying to adopt it, which is the state a
                    // unit is actually in when the charge arrives.
                    UnitInstance unit = Make(spearmen, formation, 1f - formation.OrganizationCost);
                    frontage = $"{unit.Footprint.Width:0} x {unit.Footprint.Depth:0} m";
                    stopping = $"{unit.EffectiveStoppingPower:0.00}";
                }

                Console.WriteLine($"    {formation.DisplayName,-10}{formation.OrganizationCost,7:0%}" +
                                  $"{formation.StoppingMultiplier,8:0.00}{formation.BreakthroughMultiplier,9:0.00}" +
                                  $"{frontage,16}{stopping,16}");
            }

            Console.WriteLine();
            Console.WriteLine("    (stopping shown after paying the organization cost to adopt the order)");
        }

        /// <summary>
        /// Finds formations that are worse than another on every axis, and so
        /// would never rationally be chosen.
        /// </summary>
        private static void CheckDominatedFormations(IUnitCatalogue units, IFormationCatalogue formations, List<string> warnings)
        {
            foreach (FormationDef candidate in formations.All)
            {
                foreach (FormationDef rival in formations.All)
                {
                    if (ReferenceEquals(candidate, rival)) continue;

                    bool cheaper = rival.OrganizationCost <= candidate.OrganizationCost;
                    bool stopsBetter = rival.StoppingMultiplier >= candidate.StoppingMultiplier;
                    bool breaksBetter = rival.BreakthroughMultiplier >= candidate.BreakthroughMultiplier;
                    bool strictly = rival.OrganizationCost < candidate.OrganizationCost
                                    || rival.StoppingMultiplier > candidate.StoppingMultiplier
                                    || rival.BreakthroughMultiplier > candidate.BreakthroughMultiplier;

                    if (cheaper && stopsBetter && breaksBetter && strictly)
                    {
                        warnings.Add($"'{candidate.DisplayName}' is beaten by '{rival.DisplayName}' on cost, stopping and breakthrough — " +
                                     "it needs a benefit those numbers do not capture, or nobody will ever take it.");
                        break;
                    }
                }
            }
        }

        /// <summary>Finds units that cannot hold ground against anything.</summary>
        private static void CheckHoldingGround(IUnitCatalogue units, IFormationCatalogue formations, List<string> warnings)
        {
            foreach (UnitDef defenderDef in units.All)
            {
                UnitInstance defender = Make(defenderDef, formations.Default, 1f);
                bool stopsAnyone = false;

                foreach (UnitDef attackerDef in units.All)
                {
                    UnitInstance attacker = Make(attackerDef, formations.Default, 1f);

                    if (attacker.EffectiveBreakthrough <= defender.EffectiveStoppingPower)
                    {
                        stopsAnyone = true;
                        break;
                    }
                }

                if (!stopsAnyone)
                    warnings.Add($"{defenderDef.DisplayName} stops nobody at all in line — it cannot hold ground against any unit on the field.");
            }
        }

        private static string Abbrev(string name) => name.Length <= 11 ? name : name.Substring(0, 11);
    }
}
