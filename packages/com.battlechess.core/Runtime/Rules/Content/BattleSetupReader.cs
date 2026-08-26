using System;
using System.Collections.Generic;
using BattleChess.Contracts;

namespace BattleChess.Rules
{
    /// <summary>One side declared in a battle setup.</summary>
    public sealed class ArmySetup
    {
        public PlayerId Player { get; }
        public string Name { get; }

        /// <summary>
        /// The most men this army will put in one regiment, or null to take
        /// the scenario's own ceiling and, failing that, the unit type's.
        /// </summary>
        /// <remarks>
        /// See <see cref="BattleSetup.MaxStrength"/> for why the ceiling is
        /// a property of the battle rather than only of the type.
        /// </remarks>
        public int? MaxStrength { get; }

        public ArmySetup(PlayerId player, string name, int? maxStrength = null)
        {
            Player = player;
            Name = name;
            MaxStrength = maxStrength;
        }
    }

    /// <summary>One regiment placed on the field by a battle setup.</summary>
    public sealed class DeploymentSetup
    {
        public PlayerId Player { get; }
        public string UnitKey { get; }

        /// <summary>Requested strength, or null to raise the unit at its default.</summary>
        public int? Strength { get; }

        public int Column { get; }
        public int Row { get; }
        public Facing Facing { get; }

        /// <summary>Formation key, or null to raise the unit in its natural order.</summary>
        public string? FormationKey { get; }

        public DeploymentSetup(PlayerId player, string unitKey, int? strength, int column, int row, Facing facing, string? formationKey)
        {
            Player = player;
            UnitKey = unitKey;
            Strength = strength;
            Column = column;
            Row = row;
            Facing = facing;
            FormationKey = formationKey;
        }
    }

    /// <summary>
    /// A battle described in content: which map, which armies, what they field
    /// and where it stands.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Parsing and building are separate steps on purpose. Parsing turns text
    /// into a description and touches no files; building resolves that against a
    /// loaded map and catalogues. So the format can be read without a map to
    /// hand, and a setup can be built against a substituted map for testing.
    /// </para>
    /// <para>
    /// This file is also the unit of exchange for the map-game audience. Two
    /// people send each other a setup and a seed, and both get identical
    /// results — which is the whole point of settling battles here rather than
    /// by argument.
    /// </para>
    /// </remarks>
    public sealed class BattleSetup
    {
        public string Name { get; }
        public string MapName { get; }
        public ulong Seed { get; }

        /// <summary>
        /// The most men any regiment in this battle may hold, or null to
        /// leave every type at its own <c>maxStrength</c>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A ceiling belongs to the battle being fought, not only to the
        /// kind of troops fighting it. What a swordsmen regiment normally is
        /// stays declared in <c>units.cfg</c>; how big a body <i>this</i>
        /// engagement organises men into is a property of the engagement,
        /// and a scenario built round 2 000-man regiments should not have to
        /// edit shared content — and so change every other battle and the
        /// balance harness with it — to say so.
        /// </para>
        /// <para>
        /// Three scopes, tightest wins: a regiment is held to its army's
        /// ceiling if it has one, otherwise the scenario's, otherwise the
        /// type's own.
        /// </para>
        /// </remarks>
        public int? MaxStrength { get; }

        public IReadOnlyList<ArmySetup> Armies { get; }
        public IReadOnlyList<DeploymentSetup> Deployments { get; }

        private BattleSetup(string name, string mapName, ulong seed, int? maxStrength,
            IReadOnlyList<ArmySetup> armies, IReadOnlyList<DeploymentSetup> deployments)
        {
            Name = name;
            MapName = mapName;
            Seed = seed;
            MaxStrength = maxStrength;
            Armies = armies;
            Deployments = deployments;
        }

        public static BattleSetup Parse(string text)
        {
            ConfigDocument document = ConfigDocument.Parse(text);

            string name = document.RootOrDefault("name", "Unnamed battle");
            string mapName = document.RequireRoot("map");

            string seedText = document.RootOrDefault("seed", "0");
            if (!ulong.TryParse(seedText, out ulong seed))
                throw new FormatException($"seed must be a whole number, got '{seedText}'.");

            int? maxStrength = ReadCeiling(document.RootOrDefault("maxStrength", string.Empty), "maxStrength");

            var armies = new List<ArmySetup>();
            foreach (ConfigSection section in document.SectionsNamed("army"))
            {
                PlayerId player = ReadPlayer(section);
                armies.Add(new ArmySetup(
                    player,
                    section.GetOrDefault("name", $"Army {player.Value}"),
                    ReadCeiling(section.GetOrDefault("maxStrength", string.Empty), "army maxStrength")));
            }

            if (armies.Count == 0)
                throw new FormatException("A battle needs at least one [army].");

            var deployments = new List<DeploymentSetup>();
            foreach (ConfigSection section in document.SectionsNamed("deploy"))
                deployments.Add(ReadDeployment(section));

            if (deployments.Count == 0)
                throw new FormatException("A battle needs at least one [deploy].");

            return new BattleSetup(name, mapName, seed, maxStrength, armies, deployments);
        }

        /// <summary>Reads an optional regiment ceiling, insisting it be a positive whole number if present.</summary>
        private static int? ReadCeiling(string text, string what)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;

            if (!int.TryParse(text, out int ceiling) || ceiling <= 0)
                throw new FormatException($"{what} must be a whole number above zero, got '{text}'.");

            return ceiling;
        }

        /// <summary>
        /// Turns this description into a live battle against a loaded map.
        /// </summary>
        public BattleState Build(
            BattleMapDefinition map,
            ITerrainCatalogue terrainCatalogue,
            IUnitCatalogue unitCatalogue,
            IFormationCatalogue formationCatalogue,
            IMovementModel movement)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (formationCatalogue == null) throw new ArgumentNullException(nameof(formationCatalogue));

            var battle = new BattleState(Name, map.Terrain, terrainCatalogue, unitCatalogue, formationCatalogue, movement, Seed);

            var ceilingFor = new Dictionary<PlayerId, int?>();

            foreach (ArmySetup army in Armies)
            {
                battle.AddArmy(army.Player, army.Name);
                ceilingFor[army.Player] = army.MaxStrength ?? MaxStrength;
            }

            foreach (DeploymentSetup deployment in Deployments)
            {
                if (!unitCatalogue.TryGetByKey(deployment.UnitKey, out UnitDef def))
                    throw new FormatException($"No unit type called '{deployment.UnitKey}'.");

                if (deployment.Column < 0 || deployment.Column >= map.Terrain.Columns ||
                    deployment.Row < 0 || deployment.Row >= map.Terrain.Rows)
                    throw new FormatException(
                        $"{def.DisplayName} is deployed at ({deployment.Column},{deployment.Row}), " +
                        $"outside the {map.Terrain.Columns}x{map.Terrain.Rows} map.");

                FormationDef formation = formationCatalogue.Default;

                if (deployment.FormationKey != null &&
                    !formationCatalogue.TryGetByKey(deployment.FormationKey, out formation))
                    throw new FormatException($"No formation called '{deployment.FormationKey}'.");

                // Tightest scope wins, and a request over the ceiling is an
                // error rather than a quiet trim. It used to clamp in
                // silence, which meant a battle file could state a 2 000-man
                // cavalry regiment, deploy 700, and read as though it had
                // fielded the army it described — the order of battle on the
                // page and the one on the field disagreeing with nothing to
                // say so.
                int ceiling = (ceilingFor.TryGetValue(deployment.Player, out int? own) ? own : null)
                              ?? def.MaxStrength;

                int wanted = deployment.Strength ?? def.DefaultStrength;

                if (wanted > ceiling)
                    throw new FormatException(
                        $"{def.DisplayName} at ({deployment.Column},{deployment.Row}) asks for {wanted} men, " +
                        $"above the {ceiling} this battle allows one regiment. Raise maxStrength on the " +
                        $"[army] or on the battle, or ask for fewer.");

                // Below the type's minimum still rounds up rather than
                // throwing: a body too small to be a regiment is a different
                // problem, and content already leans on that.
                int strength = Math.Clamp(wanted, def.MinStrength, ceiling);
                Vec2 position = map.Terrain.CellCentre(deployment.Column, deployment.Row);

                battle.AddUnit(deployment.Player, def, position, deployment.Facing, strength, formation);
            }

            AssignRetreatDirections(battle, map);
            return battle;
        }

        /// <summary>
        /// Works out which way each army's broken units will run.
        /// </summary>
        /// <remarks>
        /// Taken from where the army actually deployed rather than declared in
        /// content: an army drawn up on the west bank obviously retreats west,
        /// and making every battle file state the obvious would be one more
        /// thing to get wrong. A battle can still override it.
        /// </remarks>
        private static void AssignRetreatDirections(BattleState battle, BattleMapDefinition map)
        {
            Vec2 field = map.Terrain.Bounds.Centre;

            foreach (Army army in battle.Armies)
            {
                Vec2 sum = Vec2.Zero;
                int count = 0;

                foreach (UnitInstance unit in battle.UnitsOf(army.Player))
                {
                    sum += unit.Position;
                    count++;
                }

                if (count == 0) continue;

                Vec2 away = (sum / count) - field;

                // Snap to the nearest edge, so routers stream off one side of
                // the field rather than drifting diagonally into a corner.
                army.RetreatDirection = MathF.Abs(away.X) >= MathF.Abs(away.Y)
                    ? new Vec2(MathF.Sign(away.X), 0f)
                    : new Vec2(0f, MathF.Sign(away.Y));

                if (army.RetreatDirection.IsNearZero)
                    army.RetreatDirection = new Vec2(-1f, 0f);
            }
        }

        private static PlayerId ReadPlayer(ConfigSection section)
        {
            if (string.IsNullOrWhiteSpace(section.Argument))
                throw new FormatException($"Line {section.LineNumber}: [{section.Name}] needs a player number, e.g. [{section.Name} 0].");

            if (!int.TryParse(section.Argument, out int index) || index < 0)
                throw new FormatException($"Line {section.LineNumber}: '{section.Argument}' is not a player number.");

            return new PlayerId(index);
        }

        private static DeploymentSetup ReadDeployment(ConfigSection section)
        {
            PlayerId player = ReadPlayer(section);
            string type = section.Require("type");

            int? strength = null;
            if (section.Values.TryGetValue("strength", out string? strengthText))
            {
                strength = AttributeParsers.Int(strengthText);
                if (strength <= 0)
                    throw new FormatException($"Line {section.LineNumber}: strength must be positive.");
            }

            string atText = section.Require("at");
            string[] parts = atText.Split(',');
            if (parts.Length != 2)
                throw new FormatException($"Line {section.LineNumber}: 'at' must be 'column,row', got '{atText}'.");

            int column = AttributeParsers.Int(parts[0].Trim());
            int row = AttributeParsers.Int(parts[1].Trim());

            Facing facing = ParseFacing(section.GetOrDefault("facing", "east"), section.LineNumber);

            section.Values.TryGetValue("formation", out string? formationKey);

            return new DeploymentSetup(player, type, strength, column, row, facing, formationKey);
        }

        /// <summary>
        /// Accepts either a bearing in degrees or a compass name, because
        /// "facing = north" is much easier to write and read than "facing = 90".
        /// </summary>
        private static Facing ParseFacing(string raw, int lineNumber)
        {
            switch (raw.Trim().ToLowerInvariant())
            {
                case "east": return Facing.East;
                case "northeast": case "north-east": return Facing.FromDegrees(45f);
                case "north": return Facing.North;
                case "northwest": case "north-west": return Facing.FromDegrees(135f);
                case "west": return Facing.West;
                case "southwest": case "south-west": return Facing.FromDegrees(225f);
                case "south": return Facing.South;
                case "southeast": case "south-east": return Facing.FromDegrees(315f);
            }

            try
            {
                return Facing.FromDegrees(AttributeParsers.Float(raw));
            }
            catch (FormatException)
            {
                throw new FormatException(
                    $"Line {lineNumber}: '{raw}' is not a facing. Use a compass point (north, south-east) or degrees.");
            }
        }
    }
}
