namespace BattleChess.Contracts
{
    /// <summary>
    /// The attributes terrain may declare.
    /// </summary>
    /// <remarks>
    /// <b>To add one:</b> add a single <c>Define(...)</c> line here and set it on
    /// whichever terrains want it in the content file. Terrains that omit it get
    /// the default. Nothing else changes — not the loader, not the map, not any
    /// existing consumer.
    /// </remarks>
    public static class TerrainAttributes
    {
        public static readonly AttributeRegistry Registry = new AttributeRegistry();

        /// <summary>Height band, feeding line of sight and defensive advantage.</summary>
        /// <remarks>
        /// The whole of how hills and mountains block sight. Ground higher than
        /// both ends of a line of sight is opaque, which is why an army can be
        /// hidden behind a ridge and why going round a range is a real plan.
        /// </remarks>
        public static readonly AttributeKey<int> Elevation =
            Registry.Define("elevation", 0, AttributeParsers.Int);

        /// <summary>
        /// Metres of sight range consumed per metre of this terrain crossed.
        /// </summary>
        /// <remarks>
        /// <para>
        /// How woods block, as against how hills do. A ridge is opaque and you
        /// go round it; a wood is merely thick, and how far you can see into
        /// one depends on how good your eyes were to begin with. At a cost of
        /// 4, infantry seeing 180 m makes out 45 m of forest — the regiment at
        /// the treeline, not the one behind it.
        /// </para>
        /// <para>
        /// Graded rather than a yes/no so "partially blocks" is a number that
        /// can be tuned instead of a decision that cannot.
        /// </para>
        /// </remarks>
        public static readonly AttributeKey<float> SightCost =
            Registry.Define("sightCost", 1f, AttributeParsers.Float);

        /// <summary>
        /// Proportional bonus to the sight of a unit standing on this ground.
        /// </summary>
        /// <remarks>
        /// Kept as its own attribute rather than derived from
        /// <see cref="Elevation"/> so that a watchtower, a spire or a rooftop
        /// can see far without pretending to be a mountain.
        /// </remarks>
        public static readonly AttributeKey<float> VisionBonus =
            Registry.Define("visionBonus", 0f, AttributeParsers.Float);

        /// <summary>Whether units standing in it are harder to spot.</summary>
        public static readonly AttributeKey<bool> Conceals =
            Registry.Define("conceals", false, AttributeParsers.Bool);

        /// <summary>Proportional defensive bonus for a unit standing in it.</summary>
        public static readonly AttributeKey<float> DefenceBonus =
            Registry.Define("defenceBonus", 0f, AttributeParsers.Float);

        /// <summary>
        /// Multiplier on damage taken from shooting while in this terrain.
        /// </summary>
        /// <remarks>
        /// Separate from <see cref="DefenceBonus"/> because cover and defence
        /// are not the same thing. Woods hide men from arrows far more than they
        /// help in a melee; a river does nothing to blunt a sword but leaves men
        /// floundering in the open with nowhere to go.
        /// </remarks>
        public static readonly AttributeKey<float> RangedCover =
            Registry.Define("rangedCover", 1f, AttributeParsers.Float);
    }
}
