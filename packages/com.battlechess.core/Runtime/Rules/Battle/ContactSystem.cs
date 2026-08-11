using System;
using BattleChess.Contracts;

namespace BattleChess.Rules
{
    /// <summary>
    /// Decides what happens when units come close: who is halted, who forces a
    /// way through, and what it costs them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Nothing physically collides.</b> Units remain points at their centre,
    /// as movement and pathfinding already assume. What creates a front line is
    /// not shapes touching but ground being <i>controlled</i> — a march halts on
    /// entering an enemy's zone of control, well before the formations would
    /// overlap. That keeps one model throughout instead of a physics layer
    /// fighting a routing layer, and it is how most wargames actually work.
    /// </para>
    /// <para>
    /// Control is not absolute. A unit whose breakthrough exceeds the enemy's
    /// stopping power rides straight through — cavalry through swordsmen, never
    /// through a spear wall — at the cost of disorder to both. Passing through
    /// friends costs less, but is not free either, so stacking an army on one
    /// spot quietly ruins it.
    /// </para>
    /// <para>
    /// Runs before movement, so a halt takes effect on the tick it is decided.
    /// </para>
    /// </remarks>
    public sealed class ContactSystem : IBattleSystem
    {
        /// <summary>Organization lost per tick while riding through an enemy formation.</summary>
        /// <remarks>
        /// Halved. At the old figure a breakthrough cost more than a point of
        /// cohesion per turn of contact — more than the entire scale — so
        /// anything that spent a few seconds forcing a line came out the far
        /// side as a rabble regardless of how it went. Charged per tick because
        /// it is the duration of the shoving that matters, which means it needs
        /// to be small.
        /// </remarks>
        private const float EnemyBreakthroughDisorderPerTick = 0.01f;

        /// <summary>Organization lost per tick while pushing through a friendly formation.</summary>
        /// <remarks>
        /// <para>
        /// Charged only while somebody is actually moving. The cost is for
        /// <i>passing through</i> a formation, not for standing near one — units
        /// drawn up shoulder to shoulder in a line overlap constantly, and
        /// draining them for it reduced a stationary regiment to nothing in two
        /// turns of doing absolutely nothing.
        /// </para>
        /// <para>
        /// Cut to a third after a recorded game. At the old rate a regiment
        /// overlapping a friend for one turn lost a quarter of its cohesion,
        /// which put threading your own line on a par with fording a river —
        /// and cavalry that manoeuvred behind its own army for three turns
        /// arrived at the fight with a third of its cohesion and lost a matchup
        /// it wins comfortably. Crowding your own troops should be a real cost
        /// and a minor one.
        /// </para>
        /// </remarks>
        private const float FriendlyOverlapDisorderPerTick = 0.0012f;

        public string Name => "Contact";

        public void Step(BattleState battle, int tick, IBattleLog log)
        {
            if (battle == null) throw new ArgumentNullException(nameof(battle));

            // Ascending unit id throughout, so the same seed resolves contacts
            // in the same order every run.
            foreach (UnitInstance unit in battle.UnitsOnField())
            {
                if (!unit.IsFighting) continue;

                ApplyOverlapDisorder(battle, unit, log);

                if (unit.IsMarching)
                    ApplyZoneOfControl(battle, unit, tick, log);
            }
        }

        /// <summary>
        /// Halts a march that has entered an enemy's zone of control, unless the
        /// unit can force its way through.
        /// </summary>
        private static void ApplyZoneOfControl(BattleState battle, UnitInstance unit, int tick, IBattleLog log)
        {
            // Formation and cohesion both count. A spear wall stops cavalry
            // because it is braced and holding together; the same spearmen in
            // loose order, or shaken, are ridden straight through.
            float breakthrough = unit.EffectiveBreakthrough;

            OrientedRect shape = unit.Shape;

            // A regiment closing the last few metres onto the enemy it has been
            // ordered to fight is not marching past anybody, and nobody else's
            // zone of control has any business stopping it.
            //
            // This is the dead band that kept two lines from ever meeting.
            // Melee reaches 8 m and zones of control reach ten to twenty, so a
            // unit with a second enemy nearby was halted a metre or two short of
            // the fight, had its route cleared, re-planned it, and was halted
            // again — every tick, forever. The recorded game shows spearmen
            // stopped at 9 m from the cavalry they were attacking, by the zone
            // of control of a different regiment 14 m away, for two hundred
            // ticks together.
            if (IsAboutToMakeContact(battle, unit)) return;

            foreach (UnitInstance enemy in battle.UnitsOnField())
            {
                if (enemy.Owner == unit.Owner) continue;
                if (!enemy.IsFighting) continue;

                // A zone of control is a belt of ground around the formation,
                // not a circle around its centre. Measured from the centre it
                // was frequently narrower than the regiment itself: swordsmen
                // stand ninety-seven metres across and reach thirty, so two
                // thirds of their own front lay outside the ground they were
                // supposed to be controlling, and an enemy could march straight
                // through the end of the line without ever entering it.
                if (!OrientedRect.Within(shape, enemy.Shape, enemy.ZoneOfControl)) continue;

                // A unit already inside the zone may still back out of it. Zone
                // of control stops an advance into or through controlled ground;
                // it is not a cage. Without this a unit halted at a spear wall
                // could never be moved again — every new order was cancelled on
                // the tick it was given, whichever way it pointed.
                if (IsWithdrawingFrom(unit, enemy)) continue;

                // Nor does it stop an attack being pressed home against the very
                // unit exerting it. Zone of control reaches 30 m and melee needs
                // 13 m, so halting here would leave two regiments staring at
                // each other from outside sword's length, permanently unable to
                // fight. It prevents marching past an enemy, not closing with one.
                if (unit.Order.Kind == OrderKind.Attack && unit.Order.Target == enemy.Id) continue;

                float stopping = enemy.EffectiveStoppingPower;

                // A cautious unit halts on contact whether or not it could force
                // through. This is the whole difference between Defend and
                // Advance: the same regiment, the same enemy, and a different
                // answer because of what it was told to do about surprises.
                if (unit.Stance == Stance.Defend)
                {
                    // Only say so on the tick it happens. A halt persists for as
                    // long as the enemy stands there, and repeating it every
                    // second would bury everything else.
                    if (unit.HeldUpBy != enemy.Id)
                        log.Blocked("Contact",
                            $"{unit.Def.DisplayName} halts on contact with {enemy.Def.DisplayName} at " +
                            $"{OrientedRect.GapBetween(shape, enemy.Shape):0} m — standing on Defend. " +
                            "It can still be ordered to withdraw.",
                            unit.Id);

                    unit.Route = null;
                    unit.HeldUpBy = enemy.Id;
                    return;
                }

                if (breakthrough > stopping)
                {
                    // Through, but not unscathed — and the line being ridden
                    // through suffers for it too.
                    unit.Organization -= EnemyBreakthroughDisorderPerTick;
                    enemy.Organization -= EnemyBreakthroughDisorderPerTick;

                    if (tick % 10 == 0)
                        log.Decision("Contact",
                            $"{unit.Def.DisplayName} ({unit.FormationOrder.DisplayName}) is forcing through " +
                            $"{enemy.Def.DisplayName} ({enemy.FormationOrder.DisplayName}): " +
                            $"{breakthrough:0.00} against {stopping:0.00} — both losing order.",
                            unit.Id);

                    continue;
                }

                // Say which of the three levers stopped them, so the answer to
                // "why can't I get through" is on screen rather than inferred.
                // Once per hold-up, not once per tick.
                if (unit.HeldUpBy != enemy.Id)
                    log.Blocked("Contact",
                        $"{unit.Def.DisplayName} ({unit.FormationOrder.DisplayName}) halted by {enemy.Def.DisplayName} " +
                        $"({enemy.FormationOrder.DisplayName}, organization {enemy.Organization:0.00}) " +
                        $"at {OrientedRect.GapBetween(shape, enemy.Shape):0} m — " +
                        $"breakthrough {breakthrough:0.00} against stopping power {stopping:0.00}.",
                        unit.Id);

                unit.Route = null;
                unit.HeldUpBy = enemy.Id;
                return;
            }
        }

        /// <summary>
        /// How near a unit must be to its ordered target before other people's
        /// zones of control stop applying to it, in metres.
        /// </summary>
        /// <remarks>
        /// Deliberately short — a stride or two past melee reach, not a licence
        /// to march across a battlefield unimpeded because something on the far
        /// side has been clicked. Wide enough to cover the gap between where a
        /// zone of control halts a unit and where its men can actually reach.
        /// </remarks>
        private const float ClosingReachMetres = OrderSystem.ContactMetres * 2f;

        private static bool IsAboutToMakeContact(BattleState battle, UnitInstance unit)
        {
            if (unit.Order.Kind != OrderKind.Attack || !unit.Order.Target.IsValid) return false;

            UnitInstance target = battle.Get(unit.Order.Target);

            return target.IsFighting
                && target.Owner != unit.Owner
                && OrientedRect.Within(unit.Shape, target.Shape, ClosingReachMetres);
        }

        /// <summary>
        /// Whether this unit's next step takes it further from the enemy holding
        /// it up.
        /// </summary>
        /// <remarks>
        /// Judged on the <i>direction</i> of the next step, not on where the
        /// route eventually ends. Comparing distances to the destination lets a
        /// unit march clean through an enemy and call it a withdrawal, because
        /// the far side is indeed further away — and after smoothing the next
        /// waypoint often <i>is</i> the far destination.
        /// </remarks>
        private static bool IsWithdrawingFrom(UnitInstance unit, UnitInstance enemy)
        {
            if (unit.Route == null || unit.Route.IsComplete) return false;

            Vec2 heading = (unit.Route.Target - unit.Position).Normalised();
            Vec2 away = (unit.Position - enemy.Position).Normalised();

            if (heading.IsNearZero || away.IsNearZero) return false;

            // Any genuine component away from the enemy counts as breaking off.
            // Moving across their front does not — that is exactly the march a
            // zone of control exists to prevent.
            return Vec2.Dot(heading, away) > 0f;
        }

        /// <summary>
        /// Whether two overlapping friends are forcing a way through each
        /// other, as against simply moving together.
        /// </summary>
        /// <remarks>
        /// The distinction the overlap cost turns on, and getting it wrong was
        /// ruinous. Two regiments ordered onto the same enemy path to the same
        /// point, arrive overlapping, and then chase the same routers along the
        /// same line — overlapping every tick of it. Charged as a collision
        /// that is a quarter of their cohesion per turn, so any attack pressed
        /// by more than one regiment arrived with nothing left and fought at a
        /// third of its strength. Sending more troops made the attack weaker.
        ///
        /// Marching together is a crowd, not a collision. Crossing somebody
        /// else's line of march, or shouldering through a formation that is
        /// standing, is the thing that actually costs order.
        /// </remarks>
        private static bool IsPushingThrough(UnitInstance unit, UnitInstance other)
        {
            // Both standing: drawn up shoulder to shoulder, which is free.
            if (!unit.IsMarching && !other.IsMarching) return false;

            // One standing, one moving: shouldering through a formed body.
            if (!unit.IsMarching || !other.IsMarching) return true;

            Vec2 heading = (unit.Route!.Target - unit.Position).Normalised();
            Vec2 otherHeading = (other.Route!.Target - other.Position).Normalised();

            if (heading.IsNearZero || otherHeading.IsNearZero) return false;

            // Roughly the same way is a column. More than about sixty degrees
            // apart and they are genuinely cutting across one another.
            return Vec2.Dot(heading, otherHeading) < 0.5f;
        }

        /// <summary>
        /// How fast two friendly regiments standing in each other shoulder
        /// apart, in metres per second each.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Movement refuses to step a regiment into one of its own, which stops
        /// overlaps happening but cannot undo the ones that already exist —
        /// units deployed on top of each other, a formation widened into its
        /// neighbour by reshaping, two regiments arriving on the same ground in
        /// the same tick. Without a way out those stay welded together for the
        /// whole battle, and the movement rule that forbids the problem becomes
        /// the reason it is permanent.
        /// </para>
        /// <para>
        /// Deliberately a shuffle rather than a shove. Men edge sideways to make
        /// room; they are not repelled like magnets. At this rate a badly
        /// overlapping pair takes the better part of a turn to sort itself out,
        /// which is roughly how long it should look like it takes.
        /// </para>
        /// </remarks>
        private const float ShufflingApartSpeed = 1.5f;

        /// <summary>
        /// Drains organization from units standing on top of one another, and
        /// eases them apart.
        /// </summary>
        /// <remarks>
        /// Uses the real footprints rather than a radius, because this is the
        /// one question where a unit's actual shape is the thing being asked
        /// about — whether these two bodies of men are occupying the same
        /// ground.
        /// </remarks>
        private static void ApplyOverlapDisorder(BattleState battle, UnitInstance unit, IBattleLog log)
        {
            foreach (UnitInstance other in battle.UnitsOnField())
            {
                // Each pair once, in id order.
                if (other.Id.Value <= unit.Id.Value) continue;
                if (!other.IsFighting) continue;
                if (other.Owner != unit.Owner) continue;

                // Read fresh each time round: the pair before this one may have
                // just moved this unit.
                if (!OrientedRect.TryGetSeparation(unit.Shape, other.Shape, out Vec2 apart)) continue;

                // Crowding costs order only when somebody is actually forcing a
                // way through. Regiments drawn up shoulder to shoulder overlap
                // constantly and should not be worn down for standing still.
                if (IsPushingThrough(unit, other))
                {
                    unit.Organization -= FriendlyOverlapDisorderPerTick;
                    other.Organization -= FriendlyOverlapDisorderPerTick;
                }

                // Men running are not making room for anybody, and holding a
                // rout off its own reserves would dam it against them.
                if (unit.State == UnitState.Routing || other.State == UnitState.Routing) continue;

                float step = ShufflingApartSpeed * BattleClock.SecondsPerTick;

                // Half the correction each, along the shortest way out. Moving
                // one alone would let a standing regiment be walked across the
                // field by anything that leaned on it.
                Vec2 push = apart.Normalised() * step;

                unit.Position = KeepInsideTheField(battle, unit, unit.Position + push);
                other.Position = KeepInsideTheField(battle, other, other.Position - push);
            }
        }

        /// <summary>
        /// Holds a shuffle inside the map and off ground the unit cannot stand
        /// on.
        /// </summary>
        /// <remarks>
        /// Making room for a neighbour is not a reason to back into a river, and
        /// a regiment against the edge of the world has nowhere to give. Both
        /// leave it where it was, still overlapping — which is the right answer:
        /// pinned against something with a friend on top of you is a real
        /// position to be in, and one the player can see and fix.
        /// </remarks>
        private static Vec2 KeepInsideTheField(BattleState battle, UnitInstance unit, Vec2 to) =>
            battle.FormationFits(unit, to, unit.Facing) ? to : unit.Position;
    }
}
