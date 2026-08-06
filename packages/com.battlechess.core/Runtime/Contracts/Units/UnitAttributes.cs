namespace BattleChess.Contracts
{
    /// <summary>
    /// The attributes a unit type may declare.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Everything here is <b>per man</b>, not per regiment. That is what makes
    /// regiment size a free parameter: a 250-man unit hits half as hard as a
    /// 500-man one without a second set of numbers existing anywhere, and a
    /// regiment that has lost half its strength weakens by exactly the same
    /// rule.
    /// </para>
    /// <para>
    /// Combat and vision attributes are declared now but not yet consumed —
    /// they are read by M3 and M4 respectively. Declaring them early costs
    /// nothing and means those milestones add no loading code.
    /// </para>
    /// </remarks>
    public static class UnitAttributes
    {
        public static readonly AttributeRegistry Registry = new AttributeRegistry();

        // ---- Movement (used from M2) ---------------------------------------

        /// <summary>Open-ground speed in metres per second, before terrain.</summary>
        public static readonly AttributeKey<float> Speed =
            Registry.Define("speed", 1.59f, AttributeParsers.Float);

        /// <summary>
        /// How fast the formation can wheel, in degrees per second.
        /// </summary>
        /// <remarks>
        /// Small numbers by design. Changing front is one of the slowest things
        /// a body of men does — a real battalion took a minute or more to wheel
        /// ninety degrees in good order — and that slowness is what makes flanks
        /// worth attacking rather than a stat nobody notices.
        /// </remarks>
        public static readonly AttributeKey<float> TurnRate =
            Registry.Define("turnRate", 4f, AttributeParsers.Float);

        /// <summary>
        /// How far out an enemy exerts control, in metres — the range at which
        /// an advance is halted and a front line forms.
        /// </summary>
        public static readonly AttributeKey<float> ZoneOfControl =
            Registry.Define("zoneOfControl", 30f, AttributeParsers.Float);

        /// <summary>
        /// How well this unit forces its way through an enemy line, against
        /// their <see cref="StoppingPower"/>.
        /// </summary>
        /// <remarks>
        /// A pair of numbers rather than a table of which class beats which, so
        /// the rule stays content. Cavalry at 1.5 rides through swordsmen at
        /// 1.0 but is stopped dead by spearmen at 2.0 — the spear-beats-cavalry
        /// counter expressed as movement instead of as a special case.
        /// </remarks>
        public static readonly AttributeKey<float> Breakthrough =
            Registry.Define("breakthrough", 0.5f, AttributeParsers.Float);

        /// <summary>How well this unit halts an enemy trying to come through it.</summary>
        public static readonly AttributeKey<float> StoppingPower =
            Registry.Define("stoppingPower", 1f, AttributeParsers.Float);

        // ---- Combat (declared now, consumed in M3) -------------------------

        /// <summary>Offensive power per man.</summary>
        public static readonly AttributeKey<float> Attack =
            Registry.Define("attack", 1f, AttributeParsers.Float);

        /// <summary>Defensive power per man.</summary>
        public static readonly AttributeKey<float> Defence =
            Registry.Define("defence", 1f, AttributeParsers.Float);

        /// <summary>Damage reduction from armour.</summary>
        public static readonly AttributeKey<float> Armour =
            Registry.Define("armour", 0f, AttributeParsers.Float);

        /// <summary>Reach in metres. Zero means melee only.</summary>
        public static readonly AttributeKey<float> Range =
            Registry.Define("range", 0f, AttributeParsers.Float);

        /// <summary>
        /// Offensive power per man at range, kept separate from melee attack.
        /// </summary>
        /// <remarks>
        /// Archers are dangerous at two hundred metres and nearly useless at
        /// two. One number could never express both, and tying them together
        /// would mean every adjustment to their shooting made them better with a
        /// knife.
        /// </remarks>
        public static readonly AttributeKey<float> RangedAttack =
            Registry.Define("rangedAttack", 0f, AttributeParsers.Float);

        /// <summary>Ticks between shots.</summary>
        /// <remarks>
        /// Ten is one shot per combat pulse. Guns are far slower to serve, which
        /// is most of what separates a battery from a body of archers.
        /// </remarks>
        public static readonly AttributeKey<int> ReloadTicks =
            Registry.Define("reloadTicks", 10, AttributeParsers.Int);

        /// <summary>
        /// Extra hitting power on the pulse a charge lands, as a multiplier of
        /// <c>1 + chargeBonus</c>.
        /// </summary>
        /// <remarks>
        /// Spent once per contact, not every pulse. Cavalry's whole value is the
        /// moment of impact; denied it, they are mediocre.
        /// </remarks>
        public static readonly AttributeKey<float> ChargeBonus =
            Registry.Define("chargeBonus", 0f, AttributeParsers.Float);

        /// <summary>Willingness to keep fighting as casualties mount.</summary>
        public static readonly AttributeKey<float> Morale =
            Registry.Define("morale", 1f, AttributeParsers.Float);

        /// <summary>
        /// How much of a melee's terror actually reaches the men, as a
        /// multiplier on morale shock taken in close combat. Lower is steadier.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Reach, not courage. A spear block fights at the length of a pike:
        /// the killing happens two metres away and the ranks behind the points
        /// see far less of it than men wrestling at arm's reach do. The same
        /// casualties simply frighten them less.
        /// </para>
        /// <para>
        /// Deliberately separate from <see cref="Morale"/>, which is what a
        /// regiment is <i>made</i> of — how it holds up when the army is
        /// losing, how quickly it collects itself afterwards. This is only
        /// about how appalling the fighting in front of them is, so it works in
        /// the direction the weapons point and nowhere else: the benefit fades
        /// away for an attack coming round the flank, and it does not apply to
        /// shooting at all. A pike is no comfort against arrows.
        /// </para>
        /// </remarks>
        public static readonly AttributeKey<float> MeleeShockResistance =
            Registry.Define("meleeShockResistance", 1f, AttributeParsers.Float);

        /// <summary>Tie-break ordering when two units contest the same ground.</summary>
        public static readonly AttributeKey<int> Initiative =
            Registry.Define("initiative", 5, AttributeParsers.Int);

        // ---- Vision (declared now, consumed in M4) -------------------------

        /// <summary>How far this unit can see, in metres.</summary>
        public static readonly AttributeKey<float> Vision =
            Registry.Define("vision", 200f, AttributeParsers.Float);

        /// <summary>How readily this unit is spotted. Lower is stealthier.</summary>
        public static readonly AttributeKey<float> Visibility =
            Registry.Define("visibility", 1f, AttributeParsers.Float);

        /// <summary>
        /// Multiplier on casualties this unit takes from shooting. Lower is
        /// harder to hit.
        /// </summary>
        /// <remarks>
        /// The unit's own dispersion, as against the dispersion of the order it
        /// happens to be standing in — a formation already has its own
        /// vulnerability and the two multiply. Light horse ride spread out
        /// whatever the formation says, which is most of how they survive doing
        /// a job that keeps them in front of the army.
        ///
        /// Kept apart from <see cref="Visibility"/> because being hard to
        /// <i>see</i> and being hard to <i>hit</i> are different problems, and a
        /// unit can easily be one without the other: a battery is conspicuous
        /// and hard to miss, a skirmish screen is neither.
        /// </remarks>
        public static readonly AttributeKey<float> RangedVulnerability =
            Registry.Define("rangedVulnerability", 1f, AttributeParsers.Float);

        // ---- Raising a unit -------------------------------------------------

        /// <summary>Strength a unit is raised at when nothing else is specified.</summary>
        public static readonly AttributeKey<int> DefaultStrength =
            Registry.Define("defaultStrength", 500, AttributeParsers.Int);

        /// <summary>Smallest strength this unit may be raised at.</summary>
        public static readonly AttributeKey<int> MinStrength =
            Registry.Define("minStrength", 20, AttributeParsers.Int);

        /// <summary>Largest strength this unit may be raised at.</summary>
        public static readonly AttributeKey<int> MaxStrength =
            Registry.Define("maxStrength", 2000, AttributeParsers.Int);

        /// <summary>Cost per man, for army-building budgets.</summary>
        public static readonly AttributeKey<float> CostPerMan =
            Registry.Define("costPerMan", 1f, AttributeParsers.Float);
    }
}
