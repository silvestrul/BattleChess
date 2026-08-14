using System;
using System.Collections.Generic;
using BattleChess.Contracts;

namespace BattleChess.Rules
{
    /// <summary>
    /// Works out how a regiment is to get somewhere.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>M10.</b> A march is a cast, not a search. The question a regiment
    /// actually has is <i>can I walk straight there</i>, and across open ground
    /// the answer is yes — so it is asked first, cheaply, and the pathfinder is
    /// never troubled. Only when something is genuinely in the way does the
    /// search get called, and then it is called to do what it is good at.
    /// </para>
    /// <para>
    /// The saving is not small. A recorded battle planned a 464 m march by
    /// exploring <b>4,138 cells</b>, over open grass, in a straight line, for a
    /// route that came back as two waypoints — which is the search rediscovering
    /// that nothing is in the way, one cell at a time. Every route in that
    /// recording reduced to two waypoints. The search was answering a question
    /// nobody had.
    /// </para>
    /// <para>
    /// Two rungs of <b>M18</b>'s ladder are here: the straight line, and going
    /// round one of its own. Crabbing and passing through are not, and until
    /// they are, anything this cannot answer falls through to the search
    /// exactly as before.
    /// </para>
    /// <para>
    /// <b>Enemies are not obstacles to a plan at all</b>, which is a departure
    /// from M15a worth stating. As walls they made every charge arrive by
    /// walking politely round the regiment it had been sent to break — five
    /// tests at once. But the deeper reason is the one M4 already gives for
    /// terrain: a route that quietly goes round an enemy is overruling the line
    /// the player drew, and whether to cross a formed enemy's front is the most
    /// consequential decision in the game to take out of their hands. So an
    /// enemy on the line is marched into, and halting or fighting is settled
    /// where it always was.
    /// </para>
    /// </remarks>
    /// <summary>
    /// A route, and everything the planner decided about how to walk it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The interesting parts of a plan — which legs are crabbed, whether it gave
    /// up on keeping clear — are not properties of a line across ground, so they
    /// cannot live in <see cref="PathResult"/>, which is a Contracts type and
    /// describes exactly that. They travel here instead.
    /// </para>
    /// <para>
    /// They were static properties on the planner for two passes, read on the
    /// next line by whoever had just called it. That is a seam, it was flagged
    /// as one when it was written, and it duly failed: tests run in parallel,
    /// each plan overwrote the last, and a route came back described by
    /// somebody else's decisions. It cost a real test failure that looked like a
    /// pathfinding bug. Shared mutable state does not survive being convenient.
    /// </para>
    /// </remarks>
    public readonly struct Plan
    {
        public Plan(PathResult path, Facing?[]? hold, bool pressedThrough)
        {
            Path = path;
            Hold = hold;
            PressedThrough = pressedThrough;
        }

        /// <summary>The line itself.</summary>
        public readonly PathResult Path;

        /// <summary>The front to hold on each leg, where a leg asks for one.</summary>
        public readonly Facing?[]? Hold;

        /// <summary>Whether this plan gave up on keeping clear of its own side.</summary>
        public readonly bool PressedThrough;

        public bool Found => Path.Found;

        /// <summary>Turns the plan into the route a regiment actually walks.</summary>
        public MovementRoute ToRoute(bool wheelFirst = false) =>
            new MovementRoute(Path.Waypoints, wheelFirst, Hold) { PressingThrough = PressedThrough };
    }

    public static class Marching
    {
        /// <summary>
        /// How finely the ground under a straight line is checked, in metres.
        /// </summary>
        /// <remarks>
        /// Terrain is the one thing the sweep cannot answer, because it is a
        /// field rather than a shape — so it is sampled, and the sampling has to
        /// be fine enough that no impassable patch hides between two probes. Ten
        /// metres against a map whose smallest features are cells a few metres
        /// across, and against bodies twenty metres deep that would have to fit
        /// through any gap they crossed.
        /// </remarks>
        private const float GroundStepMetres = 10f;

        /// <summary>
        /// Plans a march, taking the straight line whenever there is one.
        /// </summary>
        /// <remarks>
        /// Returns a <see cref="PathResult"/> so that every caller keeps the
        /// error handling it already has — a straight line is reported as a
        /// route of two waypoints having explored no cells, which is also how it
        /// shows up in a recording and makes the saving legible there.
        /// </remarks>
        public static Plan PlanTo(
            BattleState battle, UnitInstance unit, IPathfinder pathfinder, Vec2 destination,
            IBattleLog? log = null, IWayRound? wayRound = null)
        {
            if (battle == null) throw new ArgumentNullException(nameof(battle));
            if (unit == null) throw new ArgumentNullException(nameof(unit));
            if (pathfinder == null) throw new ArgumentNullException(nameof(pathfinder));


            // Rung 1: straight there — asked of the body squared to the line
            // it is about to walk, not the one it is standing in (M24).
            Facing alongIt = AlongTheLine(unit.Position, destination, unit.Facing);

            if (IsClearLine(battle, unit, unit.Position, destination, alongIt))
                return Straight(new[] { unit.Position, destination });

            // Rung 2: round whatever is in the way — but only for a march.
            // Closing with an enemy is O5's business and it says centre first,
            // then sidestep to share the face; arching an attack in would put
            // the two rules in charge of the same approach.
            if (unit.Order.Kind != OrderKind.Attack)
            {
                IReadOnlyList<Vec2>? arch = (wayRound ?? WaysRound.Default).Round(battle, unit, destination);

                // M14: full width first, side-on second. Same line, same ground,
                // a body twenty metres across instead of forty.
                IReadOnlyList<Vec2>? threaded =
                    CrabThrough(battle, unit, destination, out Facing?[]? hold);

                // M22a. Both of these are rung two, and M18 has always said the
                // cheaper of them wins — but the code took the arch whenever it
                // found one and only crabbed if arching had failed, which is not
                // a comparison at all. It is one now that a route can be priced
                // in the thing that actually costs: time.
                //
                // The crab is worked out even when the arch succeeds, which is
                // strictly more work per plan. Affordable because M11 already
                // caps planning to a cadence rather than a tick rate, and
                // because rung two is only reached once the straight line has
                // failed — the common march never gets here at all.
                if (arch != null || threaded != null)
                {
                    float straight = SecondsToWalk(battle, unit, new[] { unit.Position, destination });

                    float arching = arch != null
                        ? SecondsToWalk(battle, unit, arch)
                        : float.MaxValue;

                    float crabbing = threaded != null
                        ? SecondsToWalk(battle, unit, threaded, hold)
                        : float.MaxValue;

                    UnitInstance? blocking = InTheWay(battle, unit, destination);

                    // Said once, when the decision is made, rather than every
                    // tick while it is carried out. Which rung answered is the
                    // whole of what a reader wants: a regiment arching across
                    // the screen looks like a fault unless the log says it chose
                    // to, and one walking through its own looks like a worse one.
                    //
                    // And what it cost, because "the arcs look too wide" is a
                    // judgement, a judgement needs a number, and no rule
                    // anywhere used to record one.
                    if (arching <= crabbing)
                    {
                        Say(log, unit, blocking,
                            "is going round its own {0} rather than through it",
                            arching, straight);

                        return Straight(arch!);
                    }

                    Say(log, unit, blocking,
                        "is turning side-on to thread a gap beside its own {0} — its front will not fit",
                        crabbing, straight);

                    return Straight(threaded!, hold);
                }

                // Rung 3: through its own. Nothing fits, nothing goes round and
                // nothing threads, so the last thing left is to shoulder past
                // them.
                //
                // Two things still have to hold. The ground must be crossable,
                // which is the difference between this and giving up. And the
                // far end must be somewhere the regiment can actually stand:
                // shouldering through men on the way is one thing, coming to
                // rest inside them is another, and it is the placement search's
                // job to find the nearest ground that is free. Without that
                // second test a regiment ordered onto its own troops pressed
                // into them and stopped there, having never said why.
                if (GroundIsClear(battle, unit, unit.Position, destination - unit.Position,
                                  (destination - unit.Position).Length, alongIt) &&
                    NobodyStandingAt(battle, unit, destination))
                {
                    // The one that must never be silent. Two regiments sharing
                    // ground is what M1 spent the whole project forbidding, and
                    // on screen it reads as a collision bug. If it is going to
                    // happen it has to say so, and say that everything else was
                    // tried first.
                    Say(log, unit, InTheWay(battle, unit, destination),
                        "is pushing through its own {0} — no way round it and no gap to thread.",
                        null, null);

                    return Straight(new[] { unit.Position, destination }, through: true);
                }
            }

            // Rungs 3 and 4 are not built. Until they are, the search is what
            // answers — which is also what answered before any of this.
            return new Plan(pathfinder.FindPath(unit.Position, destination, unit.Def.Movement), null, false);
        }

        /// <summary>
        /// How long a regiment would take to walk a given line, in seconds.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>M22.</b> A route's cost is not its length. Every bend is a wheel,
        /// every wheel is time spent at a fraction of pace, and a planner
        /// choosing on distance will buy a twenty-six metre saving with a right
        /// angle worth forty seconds. Recorded on 14 August: one cavalry
        /// regiment spent <b>1,432 of 2,644 ticks</b> coming round — 54% of the
        /// battle — and no rule with an opinion about where to walk could see any
        /// of it.
        /// </para>
        /// <para>
        /// The wheel is charged as pace lost rather than as time standing still,
        /// because that is what actually happens: a regiment sets off at once
        /// and comes round as it goes. So a leg costs the seconds spent turning,
        /// at whatever the average penalty over the turn is worth, and then the
        /// rest of the leg at the settled pace — which is full for a march and
        /// about two fifths for a crab.
        /// </para>
        /// <para>
        /// It asks <see cref="MovementSystem.AlignmentPenalty"/> rather than
        /// keeping its own copy of the curve. The plan must be priced by the
        /// rule the march will obey, or the two quietly disagree and the routes
        /// look wrong for no findable reason.
        /// </para>
        /// <para>
        /// A model of a wheel, not a simulation of one. It is used to choose
        /// between routes, not to promise a time, and it is held to a quarter
        /// either way against real marches by a property test.
        /// </para>
        /// </remarks>
        /// <param name="hold">
        /// The front to hold on each leg, where a leg asks for one — so a crab
        /// is costed as the crab it is rather than as a march of the same
        /// length.
        /// </param>
        public static float SecondsToWalk(
            BattleState battle, UnitInstance unit, IReadOnlyList<Vec2> waypoints,
            IReadOnlyList<Facing?>? hold = null)
        {
            if (battle == null) throw new ArgumentNullException(nameof(battle));
            if (unit == null) throw new ArgumentNullException(nameof(unit));
            if (waypoints == null) throw new ArgumentNullException(nameof(waypoints));

            float pace = MathF.Max(0.1f, battle.SpeedOf(unit));
            float turnRate = MathF.Max(1f, unit.Def.Get(UnitAttributes.TurnRate));

            Facing facing = unit.Facing;
            float seconds = 0f;

            for (int i = 1; i < waypoints.Count; i++)
            {
                Vec2 leg = waypoints[i] - waypoints[i - 1];
                float length = leg.Length;

                if (length <= 0f) continue;

                Facing bearing = Facing.FromVector(leg);

                // The front this leg will actually be walked on: whatever it
                // asks for if it is a crab, and the line of march otherwise.
                Facing front = hold != null && i < hold.Count && hold[i].HasValue
                    ? hold[i]!.Value
                    : bearing;

                float toTurn = Degrees(facing, front);
                float settled = Degrees(front, bearing);
                float atTheStart = Degrees(facing, bearing);

                float whileComingRound =
                    pace * MovementSystem.AlignmentPenalty((atTheStart + settled) * 0.5f);

                float onceRound = pace * MovementSystem.AlignmentPenalty(settled);

                // The wheel may outlast the leg, in which case none of it is
                // walked at the settled pace and the second term falls away.
                float covered = MathF.Min(length, toTurn / turnRate * whileComingRound);

                seconds += covered / whileComingRound + (length - covered) / onceRound;

                facing = front;
            }

            return seconds;
        }

        private static float Degrees(Facing from, Facing to) =>
            Facing.AbsoluteDelta(from, to) * 180f / MathF.PI;

        /// <summary>
        /// Room left beside whatever is being gone round, in metres.
        /// </summary>
        /// <remarks>
        /// <b>M19a.</b> A line that grazes the corner of what it is going round
        /// is a line that fails on the first metre of drift, and a march drifts
        /// constantly — it is being pushed about by crowding, by terrain and by
        /// its own turning circle. Aim past the corner, not at it.
        /// </remarks>
        private const float TangentMarginMetres = 8f;

        /// <summary>Whatever is standing in the way of the straight line.</summary>
        private static UnitInstance? InTheWay(BattleState battle, UnitInstance unit, Vec2 destination) =>
            FirstBodyInTheWay(battle, unit, unit.Position, destination,
                              AlongTheLine(unit.Position, destination, unit.Facing), out _);

        /// <summary>
        /// Reports which rung of the ladder answered, once, as it is decided.
        /// </summary>
        /// <remarks>
        /// Said when the choice is made rather than every tick while it is
        /// carried out — these are decisions, and a decision repeated sixty
        /// times a minute is the noise the whole logging pass was about. Which
        /// rung answered is exactly what a reader needs and cannot otherwise
        /// get: a regiment arching across the screen looks like a fault unless
        /// the recording says it chose to, and one walking through its own looks
        /// like a collision bug rather than the last resort it is.
        /// </remarks>
        /// <param name="seconds">
        /// What the chosen line costs, and what the straight one would have cost
        /// if it had been available. Both null for a decision with nothing to
        /// compare against — pressing through <i>is</i> the straight line, so
        /// "62 s against 62 s straight" says nothing twice.
        /// </param>
        private static void Say(
            IBattleLog? log, UnitInstance unit, UnitInstance? blocker, string what,
            float? seconds, float? straight)
        {
            if (log == null) return;

            string cost = seconds.HasValue && straight.HasValue
                ? $" — {seconds.Value:0} s against {straight.Value:0} s straight."
                : string.Empty;

            log.Decision("Move",
                $"{unit.Def.DisplayName} " + string.Format(what, blocker?.Def.DisplayName ?? "own troops") + cost,
                unit.Id);
        }


        private static Plan Straight(IReadOnlyList<Vec2> waypoints, Facing?[]? hold = null, bool through = false)
        {
            float total = 0f;

            for (int i = 1; i < waypoints.Count; i++)
                total += Vec2.Distance(waypoints[i - 1], waypoints[i]);

            return new Plan(
                PathResult.Success(waypoints, Array.Empty<Coord>(), total, total, cellsExplored: 0),
                hold, through);
        }

        /// <summary>
        /// Whether the straight line works for a body turned side-on to it.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Travelling along its own frontage, a regiment presents its depth to
        /// the way it is going — twenty metres rather than forty — so a gap that
        /// refuses a march admits a crab. Setting one up means turning, because
        /// presenting the narrow side and facing square to the line of travel
        /// are the same thing.
        /// </para>
        /// <para>
        /// Both perpendiculars are the same shape, so the one nearer the front
        /// the regiment already holds is taken: the manoeuvre is expensive
        /// enough in pace without turning the long way into it as well.
        /// </para>
        /// </remarks>
        public static IReadOnlyList<Vec2>? CrabThrough(
            BattleState battle, UnitInstance unit, Vec2 destination, out Facing?[]? hold)
        {
            hold = null;

            Vec2 travel = destination - unit.Position;
            float length = travel.Length;
            if (length <= 0f) return null;

            Vec2 along = travel / length;
            Facing straight = Facing.FromVector(travel);

            // Both perpendiculars are the same shape, so take the one nearer the
            // front already held: the manoeuvre is expensive enough in pace
            // without turning the long way into it as well.
            Facing left = Facing.FromRadians(straight.Radians + MathF.PI * 0.5f);
            Facing right = Facing.FromRadians(straight.Radians - MathF.PI * 0.5f);

            bool leftIsNearer =
                Facing.AbsoluteDelta(unit.Facing, left) <= Facing.AbsoluteDelta(unit.Facing, right);

            Facing sideOn = leftIsNearer ? left : right;

            if (!IsClearLine(battle, unit, unit.Position, destination, sideOn))
            {
                sideOn = leftIsNearer ? right : left;
                if (!IsClearLine(battle, unit, unit.Position, destination, sideOn)) return null;
            }

            // Where the squeeze actually is. The regiment marches up to it
            // facing where it is going, goes through side-on, and comes back
            // onto its march afterwards — "turn round for the crabbing only".
            // Crabbing the whole way would arrive at the far end still side-on
            // and at two fifths pace for a journey that never needed it.
            UnitInstance? tight =
                FirstBodyInTheWay(battle, unit, unit.Position, destination, straight, out float upTo);

            if (tight == null) return null;

            float entry = MathF.Max(0f, upTo - TangentMarginMetres);

            // Walked out to where the march can honestly be resumed, rather than
            // guessed at from the blocker's depth. The guess was short: it
            // allowed for the body it was going round and not for the body doing
            // the going, which is twice as long side-on as it is deep. The
            // regiment was told to face front again while still inside the wall,
            // and every leg after that was a line nobody had checked.
            //
            // Asked of the thing that will actually be true — can the rest of
            // the march be walked from here facing forwards — so it cannot be
            // short by an arithmetic slip a second time.
            float exit = length;

            for (float probe = upTo; probe < length; probe += TangentMarginMetres)
            {
                Vec2 at = unit.Position + along * probe;

                if (!IsClearLine(battle, unit, at, destination, straight)) continue;

                exit = probe;
                break;
            }

            // The squeeze runs the whole way, so there is nothing to come back
            // onto and the simple form is the honest one.
            if (entry <= 0f && exit >= length)
            {
                hold = new Facing?[] { null, sideOn };
                return new[] { unit.Position, destination };
            }

            // The last leg names the front it ends on rather than leaving it
            // implied, and that is not tidiness. Coming *off* a crab is the same
            // ninety degrees as going onto it, and the stall detector only
            // forgives a regiment for coming round when the leg it is walking
            // says which front it wants. Left as null, a regiment that had just
            // threaded a gap was declared stuck at the far side of it — the
            // fault it had been rescued from, one waypoint later.
            hold = new Facing?[] { null, null, sideOn, straight };

            return new[]
            {
                unit.Position,
                unit.Position + along * entry,
                unit.Position + along * exit,
                destination
            };
        }

        /// <summary>
        /// Whether a regiment's whole body can travel from one point to another
        /// in a straight line without meeting anything.
        /// </summary>
        /// <remarks>
        /// <b>M12</b>: the rectangle is what travels, so it is the rectangle
        /// that is asked. Both halves matter and they fail differently — ground
        /// stops a body dead, and another regiment is only in the way if the
        /// two shapes genuinely meet, which a centre-line test cannot tell.
        /// </remarks>
        public static bool IsClearLine(
            BattleState battle, UnitInstance unit, Vec2 from, Vec2 to, Facing facing)
        {
            Vec2 travel = to - from;
            float length = travel.Length;

            if (length <= 0f) return battle.FormationFits(unit, from, facing);

            if (!GroundIsClear(battle, unit, from, travel, length, facing)) return false;

            var body = new OrientedRect(from, facing, unit.Footprint);

            foreach (UnitInstance other in battle.UnitsOnField())
            {
                if (!IsInTheWayOf(unit, other)) continue;
                if (WhereItIsStanding(body, travel, other)) continue;

                if (Sweep.FirstTouch(body, travel, other.Shape, out _)) return false;
            }

            return true;
        }

        /// <summary>
        /// Whether a body is something the regiment is already standing in,
        /// rather than something its line runs into.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>M25.</b> The steering has always known this — <i>"already lapping
        /// them, so this step cannot be what did it"</i> — and the planner never
        /// did, which made leaving a formed line impossible to plan.
        /// </para>
        /// <para>
        /// A line stands shoulder to shoulder; that is what a line is and what
        /// [M2](../../../../docs/DECISIONS.md) exists to permit. Square a
        /// regiment onto a new bearing in the middle of one and its rectangle
        /// laps its neighbours before it has moved a metre — a 40 × 20 body
        /// turned 50° reaches 21.7 m along the old axis where it reached 10.
        /// The sweep then reported a collision at distance zero on every
        /// candidate leg, so rung two found nothing on either side, twice over,
        /// and rung three answered.
        /// </para>
        /// <para>
        /// Recorded: eight press-throughs in one game, every one of them setting
        /// off from the same forty metres of ground, every one of them blocked
        /// by a regiment the cavalry was drawn up beside. It reproduces at every
        /// bearing of the compass.
        /// </para>
        /// <para>
        /// Overlap and not proximity, deliberately. A neighbour merely close by
        /// is still an obstacle and still gets gone round; what is excused is
        /// the ground the regiment is already occupying. Getting clear of that
        /// is the steering's job, and since [M20](../../../../docs/DECISIONS.md)
        /// and [M1a](../../../../docs/DECISIONS.md) it is a job with a price and
        /// a rule about who gives way.
        /// </para>
        /// <para>
        /// <b>And overlapping is not enough on its own.</b> Written without the
        /// second half, this excused a body directly in front as readily as one
        /// alongside — so two regiments ordered to swap places met in the middle,
        /// each decided the other was merely where it was standing, and both
        /// planned straight on. Neither routed round, neither yielded, and they
        /// leant on each other for the rest of the game. Caught by
        /// `TwoRegimentsSwappingPlacesDoNotDeadlock`, which is eight months older
        /// than any of this.
        /// </para>
        /// <para>
        /// The line between the two cases is which way the body lies. Abreast or
        /// behind is ground you are leaving. Ahead is a regiment you are walking
        /// into, and no amount of already touching it makes that untrue.
        /// </para>
        /// </remarks>
        private static bool WhereItIsStanding(OrientedRect body, Vec2 along, UnitInstance other)
        {
            if (!OrientedRect.Overlaps(body, other.Shape)) return false;

            return Vec2.Dot(other.Position - body.Centre, along) <= 0f;
        }

        /// <summary>
        /// Whether a leg of a bent route is clear for the body that will walk it.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>M23</b>, and it is the other half of the same fix. A regiment now
        /// comes round onto every leg, so the shape travelling a leg is the one
        /// squared to <i>that</i> leg — not the one it happened to be holding
        /// when the route was planned, which is what every check here used to
        /// ask about. A body forty metres across and twenty deep is a different
        /// obstacle at forty-seven degrees, and checking the wrong one is how a
        /// route that was found clear puts a corner through what it just went
        /// round.
        /// </para>
        /// <para>
        /// Both ends of the turn are checked, because the wheel happens on the
        /// leg rather than before it: the regiment enters still holding the
        /// previous bearing and comes round as it goes. The bulge between the
        /// two — a rectangle sweeps about two metres wider mid-rotation than at
        /// either end — is what <see cref="TangentMarginMetres"/> is for.
        /// </para>
        /// </remarks>
        public static bool IsClearLeg(
            BattleState battle, UnitInstance unit, Vec2 from, Vec2 to, Facing entering)
        {
            Facing holding = AlongTheLine(from, to, entering);

            if (!IsClearLine(battle, unit, from, to, holding)) return false;

            if (Facing.AbsoluteDelta(entering, holding) < 0.01f) return true;

            return IsClearLine(battle, unit, from, to, entering);
        }

        /// <summary>
        /// The front a regiment will hold while walking a line.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>M24.</b> The planner used to ask every one of its questions about
        /// <c>unit.Facing</c> — the front the regiment happens to be standing on
        /// at the moment the order arrives. That is a transient. The regiment is
        /// about to come round onto the line ([M3](../../../../docs/DECISIONS.md),
        /// and [M23](../../../../docs/DECISIONS.md) now makes it true of every
        /// leg), so the shape that travels is the one squared to the line, and
        /// the shape it is standing in has nothing to do with where it can go.
        /// </para>
        /// <para>
        /// Measured: the same regiment, the same line, the same ground, and only
        /// the front it was left on varied. At 0°, 30°, 60°, 120°, 150° and 180°
        /// off the line it walked round its own. At <b>90°</b> — presenting its
        /// whole forty-metre frontage broadside, so the swept corridor is twice
        /// as wide as the one it will actually occupy — rung one failed, rung
        /// two failed, and it shouldered straight through them.
        /// </para>
        /// <para>
        /// The wheel at the start is real and is not the plan's business. It is
        /// the steering's, which is what the steering is for; the plan is about
        /// the line.
        /// </para>
        /// </remarks>
        public static Facing AlongTheLine(Vec2 from, Vec2 to, Facing fallback)
        {
            Vec2 leg = to - from;

            return leg.IsNearZero ? fallback : Facing.FromVector(leg);
        }

        /// <summary>
        /// Whether one regiment counts as an obstacle to another when a route is
        /// being planned.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>M15a</b>, and it is load-bearing rather than a refinement. An
        /// enemy is only in the way of a regiment that was going somewhere else.
        /// To one told to advance or to press, an enemy standing on the line is
        /// not an obstruction at all — going through it <i>is</i> the order, and
        /// that is what `TryFightWhatBlocks` exists to carry out.
        /// </para>
        /// <para>
        /// Learnt by breaking five tests at once. Routing round enemies without
        /// this made charges arrive by walking politely round the regiment they
        /// had been sent to break, cavalry decline to be held by cavalry, and a
        /// regiment that had fought its way past somebody never fight at all.
        /// A rule that quietly turns every attack into a detour does not look
        /// like a pathfinding change from the outside.
        /// </para>
        /// </remarks>
        private static bool IsInTheWayOf(UnitInstance unit, UnitInstance other)
        {
            if (ReferenceEquals(other, unit)) return false;

            return other.Owner == unit.Owner;
        }

        /// <summary>
        /// Whether a regiment could come to rest here without sharing ground
        /// with one of its own.
        /// </summary>
        private static bool NobodyStandingAt(BattleState battle, UnitInstance unit, Vec2 at)
        {
            var body = new OrientedRect(at, unit.Facing, unit.Footprint);

            foreach (UnitInstance other in battle.UnitsOnField())
            {
                if (!IsInTheWayOf(unit, other)) continue;

                if (OrientedRect.Overlaps(body, other.Shape)) return false;
            }

            return true;
        }

        private static bool GroundIsClear(
            BattleState battle, UnitInstance unit, Vec2 from, Vec2 travel, float length, Facing facing)
        {
            int steps = Math.Max(1, (int)MathF.Ceiling(length / GroundStepMetres));

            for (int i = 0; i <= steps; i++)
            {
                Vec2 at = from + travel * (i / (float)steps);

                if (!battle.FormationFits(unit, at, facing)) return false;
            }

            return true;
        }

        /// <summary>
        /// What a march would meet on the way, and how far it would get first.
        /// </summary>
        /// <remarks>
        /// Not used to plan yet — this is what the arching pass will fan over,
        /// and it is here now because it is the same question <see
        /// cref="IsClearLine"/> asks and answering it twice in two places is how
        /// the two answers come to disagree.
        /// </remarks>
        public static UnitInstance? FirstBodyInTheWay(
            BattleState battle, UnitInstance unit, Vec2 from, Vec2 to, Facing facing, out float distance)
        {
            Vec2 travel = to - from;
            distance = travel.Length;

            if (distance <= 0f) return null;

            var body = new OrientedRect(from, facing, unit.Footprint);

            UnitInstance? nearest = null;
            float closest = float.MaxValue;

            foreach (UnitInstance other in battle.UnitsOnField())
            {
                if (!IsInTheWayOf(unit, other)) continue;

                // M25, and asked here too so that "what is in the way" and "is
                // this line clear" cannot come back with different answers —
                // otherwise the planner would aim past a body the clearance
                // check had already excused, or excuse one it was aiming past.
                if (WhereItIsStanding(body, travel, other)) continue;

                if (!Sweep.FirstTouch(body, travel, other.Shape, out float reach)) continue;

                if (reach < closest)
                {
                    closest = reach;
                    nearest = other;
                }
            }

            if (nearest != null) distance = closest;

            return nearest;
        }
    }
}
