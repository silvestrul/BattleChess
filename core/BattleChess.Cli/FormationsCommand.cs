using System;
using System.IO;
using BattleChess.Contracts;
using BattleChess.Rules;

namespace BattleChess.Cli
{
    /// <summary>
    /// Shows the formation orders and what each does to every unit's shape.
    /// </summary>
    /// <remarks>
    /// The table is the point of the whole feature: seeing cavalry go from 110 m
    /// of frontage to under 40 m makes "form column to get through the gap" an
    /// obvious idea rather than an abstract one.
    /// </remarks>
    public static class FormationsCommand
    {
        public static int Run(string[] args)
        {
            FormationCatalogue formations = FormationCatalogueReader.Read(File.ReadAllText(ContentLocator.FormationsFile()));
            UnitCatalogue units = UnitCatalogueReader.Read(File.ReadAllText(ContentLocator.UnitsFile()));

            Console.WriteLine();
            Console.WriteLine($"  {formations.Count} formation orders  (raised in '{formations.Default.Key}')");
            Console.WriteLine();

            foreach (FormationDef formation in formations.All)
            {
                Console.WriteLine($"  {formation.DisplayName}   ranks x{formation.RankMultiplier:0.##}, " +
                                  $"spacing x{formation.FileWidthMultiplier:0.##}, " +
                                  $"stopping x{formation.StoppingMultiplier:0.##}, " +
                                  $"breakthrough x{formation.BreakthroughMultiplier:0.##}, " +
                                  $"costs {formation.OrganizationCost:0%} organization");

                if (!string.IsNullOrWhiteSpace(formation.Description))
                    Console.WriteLine($"    {formation.Description}");

                Console.WriteLine();
            }

            Console.WriteLine("  Frontage x depth at full strength");
            Console.Write($"    {"Unit",-12}");

            foreach (FormationDef formation in formations.All)
                Console.Write($"{formation.DisplayName,16}");

            Console.WriteLine();
            Console.WriteLine("    " + new string('-', 12 + formations.Count * 16));

            foreach (UnitDef unit in units.All)
            {
                Console.Write($"    {unit.DisplayName,-12}");

                foreach (FormationDef formation in formations.All)
                {
                    Footprint shape = formation.ApplyTo(unit.NaturalFormation).FootprintFor(unit.DefaultStrength);
                    Console.Write($"{shape.Width,8:0} x{shape.Depth,5:0} m");
                }

                Console.WriteLine();
            }

            // The question this whole system exists to answer: can cavalry get
            // through, and what would have to be true for it to.
            if (units.TryGetByKey("cavalry", out UnitDef cavalry))
            {
                Console.WriteLine();
                Console.WriteLine("  Can fresh cavalry ride through? (its breakthrough against their stopping power)");
                Console.Write($"    {"Defender",-12}");

                foreach (FormationDef formation in formations.All)
                    Console.Write($"{formation.DisplayName,16}");

                Console.WriteLine();
                Console.WriteLine("    " + new string('-', 12 + formations.Count * 16));

                float charge = cavalry.Get(UnitAttributes.Breakthrough) * formations.Default.BreakthroughMultiplier;

                foreach (UnitDef defender in units.All)
                {
                    Console.Write($"    {defender.DisplayName,-12}");

                    foreach (FormationDef formation in formations.All)
                    {
                        float stopping = defender.Get(UnitAttributes.StoppingPower) * formation.StoppingMultiplier;
                        string verdict = charge > stopping ? "through" : "STOPPED";

                        Console.Write($"{verdict,10} {stopping,5:0.00}");
                    }

                    Console.WriteLine();
                }

                Console.WriteLine();
                Console.WriteLine($"    Cavalry charges at {charge:0.00} in line, fresh.");

                // Cohesion is the axis that actually decides charges, so show it
                // as a gradient rather than a footnote.
                if (units.TryGetByKey("spearmen", out UnitDef spearmen))
                {
                    Console.WriteLine();
                    Console.WriteLine("  Spearmen stopping power as cohesion falls  (X = cavalry gets through)");
                    Console.Write($"    {"Organization",-14}");

                    foreach (FormationDef formation in formations.All)
                        Console.Write($"{formation.DisplayName,16}");

                    Console.WriteLine();
                    Console.WriteLine("    " + new string('-', 14 + formations.Count * 16));

                    foreach (float organization in new[] { 1.0f, 0.8f, 0.6f, 0.4f, 0.2f, 0.0f })
                    {
                        Console.Write($"    {organization,-14:0.0}");

                        foreach (FormationDef formation in formations.All)
                        {
                            float condition = 0.35f + 0.65f * organization;
                            float effect = 1f + (formation.StoppingMultiplier - 1f) * organization;
                            float stopping = spearmen.Get(UnitAttributes.StoppingPower) * effect * condition;

                            Console.Write($"{(charge > stopping ? "X" : " "),8} {stopping,6:0.00}");
                        }

                        Console.WriteLine();
                    }

                    Console.WriteLine();
                    Console.WriteLine("    A square held at full cohesion is impassable; the same square shaken");
                    Console.WriteLine("    is a crowd standing in roughly that shape, and cavalry rides through.");
                }
            }

            Console.WriteLine();
            return 0;
        }
    }
}
