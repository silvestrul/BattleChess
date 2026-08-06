using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using BattleChess.Contracts;
using BattleChess.Rules;

namespace BattleChess.Cli
{
    /// <summary>
    /// Fights every matchup and reports who won and at what cost.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The static balance tables say what the numbers are; this says what
    /// actually happens when they collide. With melee, morale, rout, pursuit and
    /// shooting all interacting, no amount of reading the formulas predicts the
    /// outcome — a unit that looks strong on paper can break early and lose
    /// every fight, and only running it shows that.
    /// </para>
    /// <para>
    /// Every fight is on flat open plains from a fixed seed, so terrain and luck
    /// are held still and any difference in the results comes from the units.
    /// </para>
    /// </remarks>
    public static class SweepCommand
    {
        private const int MapColumns = 40;
        private const int MapRows = 20;
        private const float CellSize = 25f;
        private const int TurnCap = 15;

        private sealed class Outcome
        {
            public string Winner = string.Empty;
            public float AttackerLost;
            public float DefenderLost;
            public int Turns;
            public bool Resolved;
            public UnitState AttackerState;
            public UnitState DefenderState;
        }

        public static int Run(string[] args)
        {
            var terrain = TerrainCatalogueReader.Read(File.ReadAllText(ContentLocator.TerrainFile()));
            var units = UnitCatalogueReader.Read(File.ReadAllText(ContentLocator.UnitsFile()));
            var formations = FormationCatalogueReader.Read(File.ReadAllText(ContentLocator.FormationsFile()));

            if (!terrain.TryGetByKey("plains", out TerrainDef plains))
            {
                Console.Error.WriteLine("No 'plains' terrain to fight on.");
                return 1;
            }

            var warnings = new List<string>();

            Console.WriteLine();
            Console.WriteLine("  BALANCE SWEEP — every matchup fought on open plains, fixed seed");

            MeleeMatrix(terrain, units, formations, plains, warnings);
            ApproachMatrix(terrain, units, formations, plains, warnings);
            CrossingSweep(terrain, units, formations, plains);
            FormationSweep(terrain, units, formations, plains);

            Console.WriteLine();
            Console.WriteLine("  ---------------------------------------------------------------------");
            Console.WriteLine($"  {warnings.Count} concern(s)");
            Console.WriteLine();

            foreach (string warning in warnings)
                Console.WriteLine($"    - {warning}");

            Console.WriteLine();
            return 0;
        }

        // ---- Melee ----------------------------------------------------------

        private static void MeleeMatrix(
            ITerrainCatalogue terrain, IUnitCatalogue units, IFormationCatalogue formations,
            TerrainDef plains, List<string> warnings)
        {
            Console.WriteLine();
            Console.WriteLine("  MELEE — nose to nose, both fighting to a decision");
            Console.WriteLine();
            Console.WriteLine($"    {"Matchup",-26}{"Winner",-12}{"loser lost",12}{"winner lost",13}{"turns",7}");
            Console.WriteLine("    " + new string('-', 70));

            var wins = new Dictionary<string, int>();
            var fights = new Dictionary<string, int>();

            foreach (UnitDef a in units.All)
            foreach (UnitDef b in units.All)
            {
                // Each unordered pair once; a unit fighting itself proves nothing.
                if (string.CompareOrdinal(a.Key, b.Key) >= 0) continue;

                Outcome result = Fight(terrain, formations, plains, a, formations.Default, b, formations.Default,
                    startDistance: 0f, seed: 1000);

                Tally(wins, fights, a.DisplayName, b.DisplayName, result.Winner);

                bool attackerWon = result.Winner == a.DisplayName;
                float loserLost = attackerWon ? result.DefenderLost : result.AttackerLost;
                float winnerLost = attackerWon ? result.AttackerLost : result.DefenderLost;

                Console.WriteLine($"    {Abbrev(a.DisplayName) + " v " + Abbrev(b.DisplayName),-26}" +
                                  $"{Abbrev(result.Winner),-12}{loserLost,11:0}%{winnerLost,12:0}%{result.Turns,7}");

                if (!result.Resolved)
                    warnings.Add($"{a.DisplayName} against {b.DisplayName} was still undecided after {TurnCap} turns — that fight has no answer.");
            }

            foreach (UnitDef unit in units.All)
            {
                int won = wins.TryGetValue(unit.DisplayName, out int w) ? w : 0;
                int fought = fights.TryGetValue(unit.DisplayName, out int f) ? f : 0;

                if (fought == 0) continue;

                if (won == fought)
                    warnings.Add($"{unit.DisplayName} wins every melee it fights — nothing on the field answers it.");
                else if (won == 0 && !IsShooter(unit))
                    warnings.Add($"{unit.DisplayName} loses every melee it fights — it needs a role, or nobody will field it.");
            }
        }

        // ---- Crossing under fire --------------------------------------------

        private static void ApproachMatrix(
            ITerrainCatalogue terrain, IUnitCatalogue units, IFormationCatalogue formations,
            TerrainDef plains, List<string> warnings)
        {
            Console.WriteLine();
            Console.WriteLine("  APPROACH — closing 250 m against a shooter, then fighting it out");
            Console.WriteLine();
            Console.WriteLine($"    {"Attacker",-14}{"Shooter",-12}{"attacker lost",15}{"shooter lost",14}{"winner",12}");
            Console.WriteLine("    " + new string('-', 70));

            foreach (UnitDef shooter in units.All)
            {
                if (shooter.Get(UnitAttributes.Range) <= 0f) continue;

                foreach (UnitDef attacker in units.All)
                {
                    if (attacker.Key == shooter.Key) continue;

                    Outcome result = Fight(terrain, formations, plains, attacker, formations.Default,
                        shooter, formations.Default, startDistance: 250f, seed: 2000);

                    Console.WriteLine($"    {Abbrev(attacker.DisplayName),-14}{Abbrev(shooter.DisplayName),-12}" +
                                      $"{result.AttackerLost,14:0}%{result.DefenderLost,13:0}%{Abbrev(result.Winner),12}");

                    // Only worth flagging for units that ought to be able to
                    // press an attack. Guns and scouts being slaughtered for
                    // charging archers is the design working, not failing.
                    if (result.AttackerLost > 60f && !IsShooter(attacker) && !IsSkirmisher(attacker))
                        warnings.Add($"{attacker.DisplayName} loses {result.AttackerLost:0}% closing on {shooter.DisplayName} — " +
                                     "crossing open ground against that is close to impossible.");
                }
            }
        }

        // ---- Crossing bad ground under fire ---------------------------------

        /// <summary>
        /// The same approach, but with a band of difficult terrain in the way.
        /// </summary>
        /// <remarks>
        /// Two effects compound and it is worth seeing them together: the ground
        /// slows the attacker so it spends longer under fire, and it also leaves
        /// them exposed while they are in it. A river crossing is both of those
        /// at once, which is what makes a defended ford so expensive.
        /// </remarks>
        private static void CrossingSweep(
            ITerrainCatalogue terrain, IUnitCatalogue units, IFormationCatalogue formations, TerrainDef plains)
        {
            if (!units.TryGetByKey("archers", out UnitDef archers)) return;

            Console.WriteLine();
            Console.WriteLine("  CROSSING UNDER FIRE — closing 250 m on archers, over different ground");
            Console.WriteLine();
            Console.Write($"    {"Attacker",-14}");

            var grounds = new List<TerrainDef> { plains };
            foreach (string key in new[] { "forest", "swamp", "river" })
                if (terrain.TryGetByKey(key, out TerrainDef found)) grounds.Add(found);

            foreach (TerrainDef ground in grounds)
                Console.Write($"{ground.DisplayName,11}");

            Console.WriteLine();
            Console.WriteLine("    " + new string('-', 14 + grounds.Count * 11));

            foreach (UnitDef attacker in units.All)
            {
                if (attacker.Key == archers.Key) continue;

                Console.Write($"    {Abbrev(attacker.DisplayName),-14}");

                foreach (TerrainDef ground in grounds)
                {
                    Outcome result = Fight(terrain, formations, ground, attacker, formations.Default,
                        archers, formations.Default, startDistance: 250f, seed: 4000);

                    Console.Write($"{result.AttackerLost,10:0}%");
                }

                Console.WriteLine();
            }

            Console.WriteLine();
            Console.WriteLine("    Losses closing. Bad ground costs twice — longer under fire, and more exposed while in it.");
        }

        // ---- Formation effects ----------------------------------------------

        private static void FormationSweep(
            ITerrainCatalogue terrain, IUnitCatalogue units, IFormationCatalogue formations, TerrainDef plains)
        {
            if (!units.TryGetByKey("swordsmen", out UnitDef swordsmen)) return;
            if (!units.TryGetByKey("cavalry", out UnitDef cavalry)) return;
            if (!units.TryGetByKey("archers", out UnitDef archers)) return;

            Console.WriteLine();
            Console.WriteLine("  FORMATIONS — the same swordsmen, standing differently");
            Console.WriteLine();
            Console.WriteLine($"    {"Formation",-12}{"horse rides through?",22}{"if it fights",16}{"under archery",16}");
            Console.WriteLine("    " + new string('-', 66));

            // Cavalry's breakthrough against this formation's stopping power,
            // both fresh. This is what a square is actually for — not winning
            // the melee, but refusing to let horse through the line at all.
            float charge = cavalry.Get(UnitAttributes.Breakthrough);

            foreach (FormationDef formation in formations.All)
            {
                float stopping = swordsmen.Get(UnitAttributes.StoppingPower) * formation.StoppingMultiplier;
                string ridesThrough = charge > stopping ? "YES" : "held";

                Outcome versusHorse = Fight(terrain, formations, plains, cavalry, formations.Default,
                    swordsmen, formation, startDistance: 0f, seed: 3000);

                Outcome versusBows = Fight(terrain, formations, plains, swordsmen, formation,
                    archers, formations.Default, startDistance: 250f, seed: 3000);

                Console.WriteLine($"    {formation.DisplayName,-12}{ridesThrough,14} ({stopping:0.00}){versusHorse.DefenderLost,13:0}% lost{versusBows.AttackerLost,14:0}% lost");
            }

            Console.WriteLine();
            Console.WriteLine("    A square repels cavalry rather than beating it: horse cannot break in, and");
            Console.WriteLine("    cannot easily be reached either. Forced to fight one, the horsemen still win —");
            Console.WriteLine("    the square's worth is denying them the line, at the price of being an easy mark.");
        }

        // ---- Running a fight -------------------------------------------------

        private static Outcome Fight(
            ITerrainCatalogue terrain, IFormationCatalogue formations, TerrainDef plains,
            UnitDef attackerDef, FormationDef attackerFormation,
            UnitDef defenderDef, FormationDef defenderFormation,
            float startDistance, ulong seed)
        {
            var cells = new TerrainId[MapColumns * MapRows];
            for (int i = 0; i < cells.Length; i++) cells[i] = plains.Id;

            var map = new GridTerrainMap(MapColumns, MapRows, CellSize, cells);
            var movement = new TerrainMovementModel(terrain);

            var battle = new BattleState("sweep", map, terrain, EmptyUnits.Instance, formations, movement, seed);
            battle.AddArmy(new PlayerId(0), "Attacker");
            battle.AddArmy(new PlayerId(1), "Defender");

            Vec2 centre = map.Bounds.Centre;

            UnitInstance a = battle.AddUnit(new PlayerId(0), attackerDef, centre, Facing.East,
                attackerDef.DefaultStrength, attackerFormation);

            float gap = startDistance > 0f
                ? startDistance
                : a.Footprint.HalfDepth + defenderFormation.ApplyTo(defenderDef.NaturalFormation)
                      .FootprintFor(defenderDef.DefaultStrength).HalfDepth + 4f;

            UnitInstance b = battle.AddUnit(new PlayerId(1), defenderDef, centre + new Vec2(gap, 0f), Facing.West,
                defenderDef.DefaultStrength, defenderFormation);

            // Both sides mean it, so neither stands off waiting to be attacked.
            a.Stance = Stance.Aggressive;
            b.Stance = Stance.Aggressive;
            a.GiveOrder(UnitOrder.Attack(b.Id), a.Position);
            b.GiveOrder(UnitOrder.Attack(a.Id), b.Position);

            var clock = new BattleClock()
                .Add(new OrderSystem(new DirectPathfinder(map, movement, terrain)))
                .Add(new ContactSystem())
                .Add(new MovementSystem())
                .Add(new RangedCombatSystem())
                .Add(new CombatSystem())
                .Add(new MoraleSystem());

            var outcome = new Outcome();

            for (int turn = 1; turn <= TurnCap; turn++)
            {
                clock.AdvanceTurn(battle);
                outcome.Turns = turn;

                if (!a.IsOnField || !b.IsOnField) { outcome.Resolved = true; break; }
            }

            outcome.AttackerLost = 100f * (attackerDef.DefaultStrength - a.Strength) / attackerDef.DefaultStrength;
            outcome.DefenderLost = 100f * (defenderDef.DefaultStrength - b.Strength) / defenderDef.DefaultStrength;
            outcome.AttackerState = a.State;
            outcome.DefenderState = b.State;

            // Driven from the field decides it; otherwise whoever bled less.
            outcome.Winner = !b.IsOnField ? attackerDef.DisplayName
                : !a.IsOnField ? defenderDef.DisplayName
                : outcome.AttackerLost <= outcome.DefenderLost ? attackerDef.DisplayName : defenderDef.DisplayName;

            return outcome;
        }

        private static void Tally(Dictionary<string, int> wins, Dictionary<string, int> fights, string a, string b, string winner)
        {
            fights[a] = fights.TryGetValue(a, out int fa) ? fa + 1 : 1;
            fights[b] = fights.TryGetValue(b, out int fb) ? fb + 1 : 1;
            wins[winner] = wins.TryGetValue(winner, out int w) ? w + 1 : 1;
        }

        /// <summary>A unit whose business is shooting, not closing.</summary>
        private static bool IsShooter(UnitDef unit) =>
            unit.Get(UnitAttributes.RangedAttack) > unit.Get(UnitAttributes.Attack);

        /// <summary>A unit meant to scout and screen rather than fight anything.</summary>
        private static bool IsSkirmisher(UnitDef unit) => unit.Class == UnitClass.Scout;

        private static string Abbrev(string name) => name.Length <= 11 ? name : name.Substring(0, 11);

        /// <summary>
        /// A catalogue the sweep never consults — units are supplied directly
        /// rather than looked up by key.
        /// </summary>
        private sealed class EmptyUnits : IUnitCatalogue
        {
            public static readonly EmptyUnits Instance = new EmptyUnits();

            public int Count => 0;
            public IReadOnlyList<UnitDef> All => Array.Empty<UnitDef>();
            public UnitDef Get(UnitTypeId id) => throw new NotSupportedException();
            public bool TryGetByKey(string key, out UnitDef def) { def = null!; return false; }
            public bool TryGetByGlyph(char glyph, out UnitDef def) { def = null!; return false; }
        }
    }
}
