using System;
using System.Globalization;
using System.IO;
using BattleChess.Contracts;
using BattleChess.Rules;

namespace BattleChess.Cli
{
    /// <summary>
    /// Lists the unit roster, and shows how a unit changes as its strength does.
    /// </summary>
    public static class UnitsCommand
    {
        public static int Run(string[] args)
        {
            UnitCatalogue catalogue = UnitCatalogueReader.Read(File.ReadAllText(ContentLocator.UnitsFile()));

            if (args.Length > 1)
                return ShowOne(catalogue, args[1], args);

            ShowRoster(catalogue);
            return 0;
        }

        private static void ShowRoster(IUnitCatalogue catalogue)
        {
            Console.WriteLine();
            Console.WriteLine($"  {catalogue.Count} unit types");
            Console.WriteLine();
            Console.WriteLine("     Unit          Class      Move     Men   Frontage  Depth   Speed   Range  Vision");
            Console.WriteLine("     ----------------------------------------------------------------------------------");

            foreach (UnitDef def in catalogue.All)
            {
                int strength = def.DefaultStrength;
                Footprint footprint = def.FootprintAt(strength);

                Console.WriteLine(
                    $"  {def.Glyph}  {def.DisplayName,-13} {def.Class,-10} {def.Movement,-7} " +
                    $"{strength,5}   {footprint.Width,6:0} m  {footprint.Depth,4:0} m  " +
                    $"{def.Speed,5:0.00}  {def.Get(UnitAttributes.Range),5:0} m  {def.Get(UnitAttributes.Vision),5:0} m");
            }

            Console.WriteLine();
            Console.WriteLine("  Use 'units <key>' for one unit in detail, e.g. 'units cavalry'.");
            Console.WriteLine();
        }

        private static int ShowOne(IUnitCatalogue catalogue, string key, string[] args)
        {
            if (!catalogue.TryGetByKey(key, out UnitDef def))
            {
                Console.Error.WriteLine($"No unit called '{key}'. Run 'units' to see the roster.");
                return 1;
            }

            Console.WriteLine();
            Console.WriteLine($"  {def.DisplayName}  ({def.Class}, {def.Movement}, glyph '{def.Glyph}')");
            Console.WriteLine();
            Console.WriteLine($"    Natural order  {def.NaturalFormation}");
            Console.WriteLine($"    Raised at      {def.DefaultStrength} men  (range {def.MinStrength}-{def.MaxStrength})");
            Console.WriteLine();
            Console.WriteLine("    Per man        attack {0:0.00}   defence {1:0.00}   armour {2:0.00}   morale {3:0.00}",
                def.Get(UnitAttributes.Attack), def.Get(UnitAttributes.Defence),
                def.Get(UnitAttributes.Armour), def.Get(UnitAttributes.Morale));
            Console.WriteLine("    Movement       {0:0.00} m/s   turn {1:0} deg/s   ZoC {2:0} m",
                def.Speed, def.Get(UnitAttributes.TurnRate), def.Get(UnitAttributes.ZoneOfControl));
            Console.WriteLine("    Reach          range {0:0} m   vision {1:0} m   visibility {2:0.00}",
                def.Get(UnitAttributes.Range), def.Get(UnitAttributes.Vision), def.Get(UnitAttributes.Visibility));

            float charge = def.Get(UnitAttributes.ChargeBonus);
            if (charge > 0f)
                Console.WriteLine($"    Charge bonus   +{charge:0.00} per man on impact");

            // The point of the per-man model: everything that matters scales
            // with headcount, so this table is the whole design in one view.
            Console.WriteLine();
            Console.WriteLine("    Strength      Frontage   Depth    Total attack   Total defence      Cost");
            Console.WriteLine("    ----------------------------------------------------------------------------");

            int requested = ReadStrength(args);
            int[] samples = requested > 0
                ? new[] { requested }
                : new[] { def.MaxStrength, def.DefaultStrength, def.DefaultStrength / 2, def.DefaultStrength / 4, def.MinStrength };

            foreach (int raw in samples)
            {
                int strength = def.ClampStrength(raw);
                Footprint footprint = def.FootprintAt(strength);

                string marker = strength == def.DefaultStrength ? " <- raised at" : string.Empty;

                Console.WriteLine(
                    $"    {strength,8}      {footprint.Width,5:0} m  {footprint.Depth,4:0} m   " +
                    $"{def.TotalOf(UnitAttributes.Attack, strength),12:0}   " +
                    $"{def.TotalOf(UnitAttributes.Defence, strength),13:0}   " +
                    $"{def.TotalOf(UnitAttributes.CostPerMan, strength),7:0}{marker}");
            }

            Console.WriteLine();
            return 0;
        }

        private static int ReadStrength(string[] args)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] != "--strength") continue;

                if (!int.TryParse(args[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int strength) || strength <= 0)
                    throw new FormatException($"Strength must be a positive whole number, got '{args[i + 1]}'.");

                return strength;
            }

            return 0;
        }
    }
}
