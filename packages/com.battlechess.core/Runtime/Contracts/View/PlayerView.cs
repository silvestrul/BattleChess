using System;
using System.Collections.Generic;

namespace BattleChess.Contracts
{
    /// <summary>
    /// One commander's picture of the battle: his own regiments in full, the
    /// enemy's only as far as he can see them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is the only shape a battle may take when it leaves the
    /// authority.</b> Everything else — positions, morale, ammunition, orders —
    /// lives on state the client cannot reference, so a fog bug can make the
    /// picture wrong but cannot make it reveal something. That is the whole
    /// point of building it as a separate type in a separate assembly rather
    /// than as a flag on the real one.
    /// </para>
    /// <para>
    /// It follows that <i>adding a field here is a security decision.</i> Every
    /// property on <see cref="SightedUnit"/> is something a commander can make
    /// out across half a mile of field with a glass, and the fog leak tests
    /// exist to argue about anything that is not.
    /// </para>
    /// </remarks>
    public sealed class PlayerView
    {
        public PlayerView(
            PlayerId viewer,
            string battleName,
            int turnNumber,
            int tick,
            IReadOnlyList<CommandedUnit> own,
            IReadOnlyList<SightedUnit> sighted,
            IReadOnlyList<RememberedUnit> remembered)
        {
            Viewer = viewer;
            BattleName = battleName ?? string.Empty;
            TurnNumber = turnNumber;
            Tick = tick;
            Own = own ?? Array.Empty<CommandedUnit>();
            Sighted = sighted ?? Array.Empty<SightedUnit>();
            Remembered = remembered ?? Array.Empty<RememberedUnit>();
        }

        /// <summary>Whose picture this is.</summary>
        public PlayerId Viewer { get; }

        public string BattleName { get; }

        /// <summary>Turns completed so far.</summary>
        public int TurnNumber { get; }

        /// <summary>The tick this picture was taken on.</summary>
        public int Tick { get; }

        /// <summary>
        /// This commander's own regiments, in id order, including those already
        /// destroyed or scattered — you know what you have lost.
        /// </summary>
        public IReadOnlyList<CommandedUnit> Own { get; }

        /// <summary>Enemy regiments currently in view, in id order.</summary>
        public IReadOnlyList<SightedUnit> Sighted { get; }

        /// <summary>
        /// Enemy regiments seen at some point and not visible now: where they
        /// were and how long ago, and nothing that has happened to them since.
        /// </summary>
        public IReadOnlyList<RememberedUnit> Remembered { get; }

        public override string ToString() =>
            $"{Viewer}: {Own.Count} own, {Sighted.Count} in view, {Remembered.Count} remembered";
    }

    /// <summary>
    /// One of your own regiments, with nothing held back.
    /// </summary>
    /// <remarks>
    /// A commander knows his own men: how many are still standing, how close
    /// they are to breaking, how much they have left to shoot with, and what he
    /// last told them to do.
    /// </remarks>
    public sealed class CommandedUnit
    {
        public CommandedUnit(
            UnitId id,
            UnitTypeId type,
            string name,
            UnitClass unitClass,
            MovementType movement,
            Vec2 position,
            Facing facing,
            Footprint footprint,
            int strength,
            int initialStrength,
            UnitState state,
            float morale,
            float organization,
            int shotsLeft,
            Stance stance,
            UnitOrder order,
            IReadOnlyList<Vec2> route)
        {
            Id = id;
            Type = type;
            Name = name ?? string.Empty;
            Class = unitClass;
            Movement = movement;
            Position = position;
            Facing = facing;
            Footprint = footprint;
            Strength = strength;
            InitialStrength = initialStrength;
            State = state;
            Morale = morale;
            Organization = organization;
            ShotsLeft = shotsLeft;
            Stance = stance;
            Order = order;
            Route = route ?? Array.Empty<Vec2>();
        }

        public UnitId Id { get; }
        public UnitTypeId Type { get; }
        public string Name { get; }
        public UnitClass Class { get; }
        public MovementType Movement { get; }

        public Vec2 Position { get; }
        public Facing Facing { get; }
        public Footprint Footprint { get; }

        public int Strength { get; }
        public int InitialStrength { get; }
        public UnitState State { get; }

        /// <summary>Willingness to keep fighting, 0 to 1.</summary>
        public float Morale { get; }

        /// <summary>How well the formation is holding together, 0 to 1.</summary>
        public float Organization { get; }

        /// <summary>Shots left in the regiment, or -1 for a unit that never runs dry.</summary>
        public int ShotsLeft { get; }

        public Stance Stance { get; }
        public UnitOrder Order { get; }

        /// <summary>The route this regiment is marching, empty if it is standing.</summary>
        public IReadOnlyList<Vec2> Route { get; }

        public bool IsOnField => State.IsOnField();
        public float StrengthFraction => InitialStrength > 0 ? Strength / (float)InitialStrength : 0f;

        public override string ToString() => $"{Name} ({Strength} men, {State})";
    }

    /// <summary>
    /// An enemy regiment you can currently see. Body, not spirit.
    /// </summary>
    /// <remarks>
    /// <para>
    /// You can see where they are, which way they face, what kind of troops
    /// they are, and roughly how many — a regiment's frontage is the one thing
    /// about it that is impossible to hide, so counting files is fair game. You
    /// can also see when they break, because a body of men running is not
    /// something anyone mistakes for anything else.
    /// </para>
    /// <para>
    /// You cannot see morale, cohesion, ammunition, or orders. That omission is
    /// the point of the type: whether the line in front of you is about to
    /// crack is a judgement you buy by pressing the attack, not a number you
    /// read off a bar. Every one of those is a deliberate absence and there is
    /// a test asserting each stays absent.
    /// </para>
    /// </remarks>
    public sealed class SightedUnit
    {
        public SightedUnit(
            UnitId id,
            PlayerId owner,
            UnitTypeId type,
            string name,
            UnitClass unitClass,
            MovementType movement,
            Vec2 position,
            Facing facing,
            Footprint footprint,
            int estimatedStrength,
            bool isBroken)
        {
            Id = id;
            Owner = owner;
            Type = type;
            Name = name ?? string.Empty;
            Class = unitClass;
            Movement = movement;
            Position = position;
            Facing = facing;
            Footprint = footprint;
            EstimatedStrength = estimatedStrength;
            IsBroken = isBroken;
        }

        /// <summary>
        /// The handle to give an order against — "attack that".
        /// </summary>
        public UnitId Id { get; }

        public PlayerId Owner { get; }
        public UnitTypeId Type { get; }
        public string Name { get; }
        public UnitClass Class { get; }
        public MovementType Movement { get; }

        public Vec2 Position { get; }
        public Facing Facing { get; }

        /// <summary>The ground they cover — visible by definition.</summary>
        public Footprint Footprint { get; }

        /// <summary>
        /// How many men, to the nearest band you could plausibly count.
        /// </summary>
        /// <remarks>
        /// Never the exact figure. Frontage shrinks as a regiment is fought
        /// down, so an observer can tell six hundred from four hundred at a
        /// glance and cannot tell six hundred from five hundred and eighty.
        /// Banding rather than adding noise is deliberate: a wrong number that
        /// jitters every turn reads as the game lying to you, where a coarse
        /// one reads as distance.
        /// </remarks>
        public int EstimatedStrength { get; }

        /// <summary>Whether they have broken and are running.</summary>
        public bool IsBroken { get; }

        public override string ToString() => $"{Name} (~{EstimatedStrength} men{(IsBroken ? ", running" : "")})";
    }

    /// <summary>
    /// Where an enemy regiment was the last time anybody laid eyes on it.
    /// </summary>
    /// <remarks>
    /// Every figure here is frozen at the moment of the sighting, including the
    /// headcount. A marker that quietly tracked their present strength would be
    /// a fog leak wearing a ghost's clothes — the whole value of a stale
    /// sighting is that it is stale, and <see cref="AgeTicks"/> is how the
    /// player judges what it is still worth.
    /// </remarks>
    public sealed class RememberedUnit
    {
        public RememberedUnit(
            UnitId id,
            PlayerId owner,
            UnitTypeId type,
            string name,
            UnitClass unitClass,
            Vec2 lastSeenAt,
            Facing lastSeenFacing,
            int estimatedStrength,
            int ageTicks)
        {
            Id = id;
            Owner = owner;
            Type = type;
            Name = name ?? string.Empty;
            Class = unitClass;
            LastSeenAt = lastSeenAt;
            LastSeenFacing = lastSeenFacing;
            EstimatedStrength = estimatedStrength;
            AgeTicks = ageTicks;
        }

        public UnitId Id { get; }
        public PlayerId Owner { get; }
        public UnitTypeId Type { get; }
        public string Name { get; }
        public UnitClass Class { get; }

        public Vec2 LastSeenAt { get; }
        public Facing LastSeenFacing { get; }

        /// <summary>How many men they had then, banded as any sighting is.</summary>
        public int EstimatedStrength { get; }

        /// <summary>Ticks since the sighting. One turn is 60.</summary>
        public int AgeTicks { get; }

        public override string ToString() => $"{Name} last seen at {LastSeenAt}, {AgeTicks} ticks ago";
    }
}
