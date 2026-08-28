using System;
using System.Collections.Generic;
using BattleChess.Contracts;
using BattleChess.Rules;
using BattleChess.Rules.GridPlanning;
using Xunit;
using Xunit.Abstractions;

namespace BattleChess.Tests.Battle
{
    /// <summary>
    /// The same gap, crossed at every angle.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Written before the model that is meant to pass it</b>, which is the
    /// point of it. Every previous movement fix was measured against an
    /// arrangement reconstructed after the fact from a recording, and each one
    /// looked right until the next arrangement broke it. This one is a gate: two
    /// bodies, one gap, and the only variable is the angle the march crosses at.
    /// </para>
    /// <para>
    /// Two spearmen 50 m apart, both 20 m deep, so <b>30 m of daylight</b>
    /// between them. A cavalry regiment is 40 m across its front and 20 m
    /// side-on, so the gap admits it at every angle, threading if nothing else.
    /// A correct planner routes all nineteen.
    /// </para>
    /// <para>
    /// <b>Baseline on the ladder, measured 16 Aug:</b>
    /// </para>
    /// <code>
    ///    0°          pressed through, declared
    ///    5° to 55°   fell to the search: two waypoints, straight through both
    ///   60° to 90°   went round correctly
    /// </code>
    /// <para>
    /// Twelve of nineteen come back as a straight line through two friendly
    /// regiments, and because the search answered rather than rung three, the
    /// route is not flagged as pressing through — so nothing charges it and no
    /// rule ever agreed to it. That is the designer's *"goes through units
    /// without colliding"*, and the angular band is why *"it is bugged at
    /// diagonals"* was the right diagnosis.
    /// </para>
    /// <para>
    /// The cause is structural rather than a missing case. Every candidate
    /// generator in the ladder measures against the <i>line of march</i>, so its
    /// accuracy falls away as the march goes diagonal to the bodies; and beneath
    /// them sits a pathfinder that takes no unit and no battle state, and so
    /// cannot see a friendly regiment at all.
    /// </para>
    /// </remarks>
    /// <remarks>
    /// <b>Serialised with every other class that moves a planner lever.</b>
    /// It sweeps the cascade nineteen times and judges the default planner, so any
    /// lever another class moves mid-sweep changes what is being measured.
    /// </remarks>
    [Collection(PlannerLevers.Name)]
    public sealed class ApproachAngleTests
    {
        private readonly ITestOutputHelper _out;

        public ApproachAngleTests(ITestOutputHelper output) => _out = output;

        /// <summary>How far apart the two bodies stand, centre to centre.</summary>
        private const float ApartMetres = 50f;

        /// <summary>
        /// How far out the march begins and ends.
        /// </summary>
        /// <remarks>
        /// Short on purpose. The same sweep with 110 m of open run-up either side
        /// routes cleanly at every angle, on the ladder, today — so the fault
        /// needs the short hop and the tight gap together, and every recorded
        /// failure had both.
        /// </remarks>
        private const float RunMetres = 45f;

        private static Battlefield TheGapAt(
            float degrees, out UnitInstance mover, out Vec2 destination)
        {
            var field = new Battlefield("plains", 7000ul + (ulong)degrees);

            Vec2 centre = field.Centre;

            foreach (float beside in new[] { -ApartMetres * 0.5f, ApartMetres * 0.5f })
                Battlefield.Hold(
                    field.Add(0, "spearmen", centre + new Vec2(beside, 0f), Facing.FromDegrees(0f)));

            float radians = degrees * MathF.PI / 180f;
            var along = new Vec2(MathF.Cos(radians), MathF.Sin(radians));

            mover = field.Add(0, "cavalry", centre - along * RunMetres, Facing.FromVector(along));
            destination = centre + along * RunMetres;

            return field;
        }

        /// <summary>
        /// Whether a route can be walked without meeting one of its own, asked of
        /// the body squared to each leg.
        /// </summary>
        private static bool EveryLegIsClear(
            Battlefield field, UnitInstance mover, IReadOnlyList<Vec2> waypoints, IReadOnlyList<Facing?>? hold)
        {
            for (int i = 1; i < waypoints.Count; i++)
            {
                Facing front = hold != null && i < hold.Count && hold[i].HasValue
                    ? hold[i]!.Value
                    : Marching.AlongTheLine(waypoints[i - 1], waypoints[i], mover.Facing);

                if (!Marching.IsClearLine(
                        field.State, mover, waypoints[i - 1], waypoints[i], front, leaving: true))
                    return false;
            }

            return true;
        }

        [Fact]
        public void EveryApproachAngleFindsAWayThroughTheGap()
        {
            var failures = new Dictionary<IRoutePlanner, List<string>>();

            // Every planner is swept before any is judged, so one planner's
            // failure does not stop the others being measured.
            foreach (IRoutePlanner planner in RoutePlanners.All)
            {
                _out.WriteLine($"--- {planner.Name} ---");
                failures[planner] = Sweep(planner);
            }

            // The gate guards whatever a march actually uses, so switching the
            // default cannot quietly switch off the test that made it credible.
            List<string> failed = failures[RoutePlanners.Default];

            Assert.True(failed.Count == 0,
                $"{failed.Count} of 19 approach angles cannot cross a 30 m gap with a regiment 20 m " +
                $"across side-on: {string.Join(", ", failed)}. The gap admits it at every angle, so " +
                "any angle that fails is the planner measuring against the line of march rather than " +
                "against the bodies.");
        }


        /// <summary>
        /// The same nineteen angles, against the ceiling that decides whether a
        /// way round is affordable.
        /// </summary>
        /// <remarks>
        /// The six failures all come back <c>pressed through</c> at about 19 s,
        /// while the angles either side of them come back clear at about 60 -
        /// which is a hair over three times as much. <see cref="M88"/> fixes the
        /// ceiling at three, so the question this asks is whether those six are
        /// the planner failing to find a route or the ceiling refusing one it
        /// found. Sweeping the ceiling separates them: if a high ceiling clears
        /// them, the search is fine and the price is the fault; if it does not,
        /// nothing is finding a way round at all.
        /// </remarks>
        [Fact(Skip = "A record of a measurement rather than a check on one - open finding 24.")]
        public void WhatTheCeilingCostsTheGap()
        {
            float was = StagedRoutePlanner.WayRoundCostCeiling;

            try
            {
                _out.WriteLine($"{"ceiling",9}{"clear",8}{"pressed",9}{"through",9}{"none",7}   angles that fail");
                _out.WriteLine(new string('-', 96));

                foreach (float ceiling in new[] { 3f, 3.5f, 4f, 6f, 100f, 0f })
                {
                    StagedRoutePlanner.WayRoundCostCeiling = ceiling;

                    int clear = 0, pressed = 0, through = 0, none = 0;
                    var bad = new List<string>();

                    for (int degrees = 0; degrees <= 90; degrees += 5)
                    {
                        Battlefield field = TheGapAt(degrees, out UnitInstance mover, out Vec2 destination);

                        Plan plan = Marching.PlanTo(
                            field.State, mover, field.Pathfinder, destination,
                            planner: RoutePlanners.Default);

                        if (!plan.Found) { none++; bad.Add($"{degrees}° none"); }
                        else if (plan.PressedThrough) { pressed++; bad.Add($"{degrees}° press"); }
                        else if (!EveryLegIsClear(field, mover, plan.Path.Waypoints, plan.Hold))
                        { through++; bad.Add($"{degrees}° through"); }
                        else clear++;
                    }

                    _out.WriteLine(
                        $"{(ceiling <= 0f ? "off" : ceiling.ToString("0.0")),9}{clear,8}{pressed,9}" +
                        $"{through,9}{none,7}   {string.Join(" ", bad)}");
                }
            }
            finally
            {
                StagedRoutePlanner.WayRoundCostCeiling = was;
            }
        }

        /// <summary>
        /// What the six failing angles are actually being asked to pay: the cost
        /// of the route that presses, and of the cheapest clear one found.
        /// </summary>
        [Fact(Skip = "A record of a measurement rather than a check on one - open finding 27.")]
        public void ThePriceOfEveryAngle()
        {
            float was = StagedRoutePlanner.WayRoundCostCeiling;

            try
            {
                _out.WriteLine(
                    $"{"angle",7}{"at 3x",24}{"pressed s",11}{"unlimited",24}{"round s",10}{"ratio",8}");
                _out.WriteLine(new string('-', 92));

                for (int degrees = 0; degrees <= 90; degrees += 5)
                {
                    StagedRoutePlanner.WayRoundCostCeiling = 3f;
                    (string atThree, double pressedSeconds) = OneAngle(degrees);

                    StagedRoutePlanner.WayRoundCostCeiling = 0f;
                    (string unlimited, double roundSeconds) = OneAngle(degrees);

                    double ratio = pressedSeconds > 0.01 ? roundSeconds / pressedSeconds : 0d;

                    _out.WriteLine(
                        $"{degrees,6}°{atThree,24}{pressedSeconds,11:0.0}{unlimited,24}" +
                        $"{roundSeconds,10:0.0}{ratio,8:0.00}");
                }
            }
            finally
            {
                StagedRoutePlanner.WayRoundCostCeiling = was;
            }
        }

        /// <summary>One angle at whatever the ceiling currently is: verdict and seconds.</summary>
        private static (string Verdict, double Seconds) OneAngle(int degrees)
        {
            Battlefield field = TheGapAt(degrees, out UnitInstance mover, out Vec2 destination);

            Plan plan = Marching.PlanTo(
                field.State, mover, field.Pathfinder, destination, planner: RoutePlanners.Default);

            string verdict = !plan.Found ? "no route"
                : plan.PressedThrough ? "pressed through"
                : !EveryLegIsClear(field, mover, plan.Path.Waypoints, plan.Hold) ? "walks through somebody"
                : "clear";

            return (verdict,
                Marching.SecondsToWalk(field.State, mover, plan.Path.Waypoints, plan.Hold));
        }


        /// <summary>
        /// Which stage of the cascade answers each angle, and what the ones that
        /// did not answer were asked.
        /// </summary>
        /// <remarks>
        /// The ceiling sweep ruled out price: with the way-round ceiling off
        /// entirely the same six angles still press. So either no stage produces
        /// a clear route at those angles, or one produces a route that will not
        /// walk. This tells them apart by stage.
        /// </remarks>
        [Fact]
        public void WhichStageAnswersEachAngle()
        {
            _out.WriteLine(
                $"{"angle",7}{"verdict",24}{"g ask",7}{"g ok",6}{"g held",8}{"f ask",7}{"f ok",6}" +
                $"{"f held",8}{"clean",7}{"pose",6}{"won",5}{"dear",6}{"press",7}{"wpts",6}");
            _out.WriteLine(new string('-', 108));

            for (int degrees = 0; degrees <= 90; degrees += 5)
            {
                Battlefield field = TheGapAt(degrees, out UnitInstance mover, out Vec2 destination);

                StagedRoutePlanner.ResetCounters();
                GridRoutePlanner.ResetCounters();

                Plan plan = Marching.PlanTo(
                    field.State, mover, field.Pathfinder, destination, planner: RoutePlanners.Default);

                string verdict = !plan.Found ? "no route"
                    : plan.PressedThrough ? "pressed through"
                    : !EveryLegIsClear(field, mover, plan.Path.Waypoints, plan.Hold) ? "walks through somebody"
                    : "clear";

                _out.WriteLine(
                    $"{degrees,6}°{verdict,24}{GridRoutePlanner.Asked,7}" +
                    $"{GridRoutePlanner.Found,6}{GridRoutePlanner.Held,8}{GridRoutePlanner.FineAsked,7}" +
                    $"{GridRoutePlanner.FineFound,6}{GridRoutePlanner.FineHeld,8}" +
                    $"{StagedRoutePlanner.GridClean,7}{StagedRoutePlanner.PoseAsked,6}" +
                    $"{StagedRoutePlanner.PoseWon,5}{StagedRoutePlanner.PoseTooDear,6}" +
                    $"{StagedRoutePlanner.Pressed,7}{plan.Path.Waypoints.Count,6}");
            }
        }


        /// <summary>
        /// One failing angle, opened up: the route the grid found, leg by leg,
        /// and the body that refuses the leg the executor would refuse.
        /// </summary>
        [Fact(Skip = "A record of a measurement rather than a check on one - open finding 24.")]
        public void OneFailingAngleLegByLeg()
        {
            foreach (int degrees in new[] { 0, 20, 10 })
            {
                Battlefield field = TheGapAt(degrees, out UnitInstance mover, out Vec2 destination);

                _out.WriteLine($"=== {degrees}° ===");
                _out.WriteLine($"  mover at {Show(mover.Position)} facing {mover.Facing.Degrees:0.0}°, " +
                               $"{mover.Shape.Footprint.Width:0.0} x {mover.Shape.Footprint.Depth:0.0} m, " +
                               $"bounding radius {mover.Shape.Footprint.BoundingRadius:0.0} m");
                _out.WriteLine($"  destination {Show(destination)}");

                foreach (UnitInstance other in field.State.UnitsOnField())
                {
                    if (other.Id == mover.Id) continue;
                    _out.WriteLine($"  body {other.Id.Value} at {Show(other.Position)} " +
                                   $"facing {other.Facing.Degrees:0.0}°, " +
                                   $"{other.Shape.Footprint.Width:0.0} x {other.Shape.Footprint.Depth:0.0} m");
                }

                foreach (float? spacing in new float?[] { null, 0.5f, 0.25f })
                {
                    IReadOnlyList<Vec2>? route = GridRoutePlanner.RouteFor(
                        field.State, mover, destination, spacing);

                    string at = spacing.HasValue ? spacing.Value.ToString("0.00") : "coarse";

                    if (route == null) { _out.WriteLine($"  grid {at}: no route"); continue; }

                    float length = GridRoutePlanner.Length(route);

                    var raw = new Plan(
                        PathResult.Success(route, Array.Empty<Coord>(), length, length, 0), null, false);

                    Plan smoothed = RouteSmoothing.Applied(field.State, mover, raw);

                    ReportRoute(field, mover, $"  grid {at} raw      ", raw);
                    ReportRoute(field, mover, $"  grid {at} raw+side  ",
                        StagedRoutePlanner.Sidewalked(field.State, mover, raw));
                    ReportRoute(field, mover, $"  grid {at} smoothed  ", smoothed);
                    ReportRoute(field, mover, $"  grid {at} smooth+side",
                        StagedRoutePlanner.Sidewalked(field.State, mover, smoothed));
                }

                _out.WriteLine(string.Empty);
            }
        }

        private static string Show(Vec2 at) =>
            System.FormattableString.Invariant($"({at.X:0.0},{at.Y:0.0})");

        private void ReportRoute(Battlefield field, UnitInstance mover, string label, Plan plan)
        {
            int bad = StagedRoutePlanner.FirstBadLeg(field.State, mover, plan);

            var line = new System.Text.StringBuilder();
            foreach (Vec2 at in plan.Path.Waypoints) line.Append(Show(at)).Append(' ');

            _out.WriteLine($"{label}: {plan.Path.Waypoints.Count} wpts, bad leg {bad}   {line}");

            if (bad <= 0) return;

            IReadOnlyList<Vec2> pts = plan.Path.Waypoints;
            Vec2 from = pts[bad - 1], to = pts[bad];

            Facing front = plan.Hold != null && bad < plan.Hold.Length && plan.Hold[bad].HasValue
                ? plan.Hold[bad]!.Value
                : Marching.AlongTheLine(from, to, mover.Facing);

            Marching.IsClearLine(field.State, mover, from, to, front, out UnitInstance? blocker);

            _out.WriteLine(
                $"       leg {bad}: {Show(from)} -> {Show(to)} on front {front.Degrees:0.0}°, " +
                $"blocked by {(blocker == null ? "nobody" : blocker.Id.Value.ToString())}");

            // And the same leg squared to the bodies rather than to the march,
            // which is what M89 says the question should be asked of.
            foreach (float front2 in new[] { 0f, 90f })
            {
                bool clear = Marching.IsClearLine(
                    field.State, mover, from, to, Facing.FromDegrees(front2), out UnitInstance? who);

                _out.WriteLine(
                    $"       same leg held at {front2,3:0}°: " +
                    $"{(clear ? "CLEAR" : "blocked by " + (who == null ? "nobody" : who.Id.Value.ToString()))}");
            }
        }


        /// <summary>
        /// Whether the grid's route walks if the legs are allowed a front other
        /// than the line of march - which is to say, if it is allowed to crab.
        /// </summary>
        /// <remarks>
        /// At the failing angles the regiment starts <b>touching</b> the body it
        /// has to get round, so every rotation sweeps a corner into it and the
        /// only clear first move is a pure sideways translation holding the
        /// front it already has. A grid route comes back with <c>Hold</c> null,
        /// so every leg is costed and checked on <c>AlongTheLine</c> and that
        /// move is never on the menu. This asks what happens if it is.
        /// </remarks>
        [Fact(Skip = "A record of a measurement rather than a check on one - the grid route with a front it may hold.")]
        public void TheGridRouteIfItMayHoldItsFront()
        {
            _out.WriteLine($"{"angle",7}{"as drawn",12}{"may hold",12}   fronts held");
            _out.WriteLine(new string('-', 96));

            for (int degrees = 0; degrees <= 90; degrees += 5)
            {
                Battlefield field = TheGapAt(degrees, out UnitInstance mover, out Vec2 destination);

                IReadOnlyList<Vec2>? route = GridRoutePlanner.RouteFor(field.State, mover, destination);

                if (route == null) { _out.WriteLine($"{degrees,6}°  no grid route"); continue; }

                bool asDrawn = WalksOn(field, mover, route, null, out _);
                bool mayHold = WalksOn(field, mover, route, mover.Facing, out string fronts);

                _out.WriteLine(
                    $"{degrees,6}°{(asDrawn ? "walks" : "refused"),12}" +
                    $"{(mayHold ? "walks" : "refused"),12}   {fronts}");
            }
        }

        /// <summary>
        /// Walks a route leg by leg, taking the line of march where it is clear
        /// and any front already in hand where it is not.
        /// </summary>
        private static bool WalksOn(
            Battlefield field, UnitInstance mover, IReadOnlyList<Vec2> route,
            Facing? mayAlsoHold, out string fronts)
        {
            var told = new System.Text.StringBuilder();
            Facing carried = mover.Facing;
            bool all = true;

            for (int leg = 1; leg < route.Count; leg++)
            {
                Facing along = Marching.AlongTheLine(route[leg - 1], route[leg], carried);

                // M89: a leg shorter than the regiment's own depth is a
                // shuffle, not a wheel, so the front already in hand is tried
                // first on it and the line of march only after.
                float span = Vec2.Distance(route[leg - 1], route[leg]);
                bool shuffle = span < mover.Shape.Footprint.Depth;

                var tries = shuffle
                    ? new List<Facing> { carried, along }
                    : new List<Facing> { along };

                if (mayAlsoHold.HasValue && !shuffle) { tries.Add(carried); tries.Add(mayAlsoHold.Value); }
                else if (mayAlsoHold.HasValue) tries.Add(mayAlsoHold.Value);

                bool found = false;

                foreach (Facing front in tries)
                {
                    if (!Marching.IsClearLine(
                            field.State, mover, route[leg - 1], route[leg], front, out _,
                            leaving: leg == 1))
                        continue;

                    told.Append(System.FormattableString.Invariant($"{front.Degrees:0}° "));
                    carried = front;
                    found = true;
                    break;
                }

                if (!found) { told.Append($"[leg {leg} X] "); all = false; break; }
            }

            fronts = told.ToString();
            return all;
        }


        /// <summary>
        /// How close the regiment stands to the body it must get round, and
        /// whether the first leg is granted the leaving licence.
        /// </summary>
        /// <remarks>
        /// The smoother asks the first leg with <c>leaving: true</c>; the gate
        /// grants that only where the regiment is <b>overlapping</b> one of its
        /// own by more than the allowed contact. A regiment merely <b>touching</b>
        /// therefore gets a route smoothed under one rule and refused under a
        /// stricter one.
        /// </remarks>
        [Fact(Skip = "A record of a measurement rather than a check on one - open finding 24.")]
        public void WhoIsGrantedTheLeavingLicence()
        {
            _out.WriteLine(
                $"{"angle",7}{"gap to body 0",16}{"overlap",10}{"leg1 leaving",14}{"leg1 strict",13}");
            _out.WriteLine(new string('-', 62));

            for (int degrees = 0; degrees <= 90; degrees += 5)
            {
                Battlefield field = TheGapAt(degrees, out UnitInstance mover, out Vec2 destination);

                UnitInstance nearest = null!;
                float gap = float.MaxValue;
                float lap = 0f;

                foreach (UnitInstance other in field.State.UnitsOnField())
                {
                    if (other.Id == mover.Id) continue;

                    float apart = OrientedRect.GapBetween(mover.Shape, other.Shape);

                    if (apart >= gap) continue;

                    gap = apart;
                    nearest = other;
                    lap = OrientedRect.OverlapFraction(mover.Shape, other.Shape);
                }

                IReadOnlyList<Vec2>? route = GridRoutePlanner.RouteFor(
                    field.State, mover, destination);

                string loose = "-", strict = "-";

                if (route != null && route.Count > 1)
                {
                    Facing along = Marching.AlongTheLine(route[0], route[1], mover.Facing);

                    loose = Marching.IsClearLine(
                        field.State, mover, route[0], route[1], along, out _, leaving: true)
                        ? "clear" : "blocked";

                    strict = Marching.IsClearLine(
                        field.State, mover, route[0], route[1], along, out _, leaving: false)
                        ? "clear" : "blocked";
                }

                _out.WriteLine(
                    $"{degrees,6}°{gap,16:0.00}{lap,10:0.000}{loose,14}{strict,13}");
            }
        }


        /// <summary>
        /// The destination itself, swept: which fronts the regiment could stand
        /// on there, and which fronts the final leg into it will walk on.
        /// </summary>
        /// <remarks>
        /// Open finding 24 (d) was written from three fronts, which is not a
        /// sweep. If some front walks, the fault is that nothing chooses the
        /// arrival front; if none does, the fault is the side the route
        /// approaches from, and those want opposite fixes.
        /// </remarks>
        [Fact(Skip = "A record of a measurement rather than a check on one - the arrival, swept. Open finding 24 (d).")]
        public void WhatTheArrivalWillAccept()
        {
            foreach (int degrees in new[] { 0, 20 })
            {
                Battlefield field = TheGapAt(degrees, out UnitInstance mover, out Vec2 destination);

                IReadOnlyList<Vec2>? route = GridRoutePlanner.RouteFor(
                    field.State, mover, destination, 0.25f);

                if (route == null || route.Count < 2) { _out.WriteLine($"{degrees}: no route"); continue; }

                Vec2 from = route[route.Count - 2];

                _out.WriteLine($"=== {degrees}°  final leg {Show(from)} -> {Show(destination)} " +
                               $"({Vec2.Distance(from, destination):0.0} m) ===");
                _out.WriteLine(
                    $"{"front",7}{"stand at goal",16}{"strict",12}{"leaving",10}" +
                    $"{"graze",9}{"reversed",11}{"by",8}");

                var standing = new List<int>();
                var walking = new List<int>();

                for (int front = 0; front < 360; front += 15)
                {
                    var facing = Facing.FromDegrees(front);

                    var pose = new OrientedRect(destination, facing, mover.Shape.Footprint);

                    float worst = 0f;
                    foreach (UnitInstance other in field.State.UnitsOnField())
                    {
                        if (other.Id == mover.Id) continue;
                        worst = MathF.Max(worst, OrientedRect.OverlapFraction(pose, other.Shape));
                    }

                    bool stands = worst <= 0.05f;

                    bool walks = Marching.IsClearLine(
                        field.State, mover, from, destination, facing, out UnitInstance? who);

                    bool lenient = Marching.IsClearLine(
                        field.State, mover, from, destination, facing, out _, leaving: true);

                    bool graze = Marching.IsClearLine(
                        field.State, mover, from, destination, facing, out _,
                        leaving: true, leavingGrazeOnly: true);

                    // And the reverse leg, which says whether the side it is
                    // approached from is what refuses it.
                    bool backwards = Marching.IsClearLine(
                        field.State, mover, destination, from, facing, out _, leaving: true);

                    if (stands) standing.Add(front);
                    if (walks) walking.Add(front);

                    if (front % 45 == 0 || walks || stands)
                        _out.WriteLine(
                            $"{front,6}°{(stands ? "yes" : $"no {worst:0.00}"),16}" +
                            $"{(walks ? "WALKS" : "no"),12}" +
                            $"{(lenient ? "LEAVING" : "no"),10}" +
                            $"{(graze ? "GRAZE" : "no"),9}" +
                            $"{(backwards ? "REVERSED" : "no"),11}" +
                            $"{(who == null ? "-" : who.Id.Value.ToString()),8}");
                }

                _out.WriteLine($"  can stand on {standing.Count} of 24 fronts; " +
                               $"final leg walks on {walking.Count} of 24.");
                _out.WriteLine(string.Empty);
            }
        }


        /// <summary>
        /// How far into one of its own the plan actually goes, sampled finely
        /// along every leg on the front that leg says it will be walked on.
        /// </summary>
        /// <remarks>
        /// The verdict <c>walks through somebody</c> is produced by
        /// <see cref="EveryLegIsClear"/>, which does not model the leaving or
        /// arriving licences, so it flags a licensed leg exactly as it flags a
        /// real intrusion. This tells them apart by measuring the overlap: at or
        /// under the allowed contact of 5% the licence is doing its job; well
        /// over it, the gate has been loosened too far and the route is going
        /// through a regiment.
        /// </remarks>
        [Fact(Skip = "A record of a measurement rather than a check on one - open finding 24.")]
        public void HowDeepThePlanGoesIntoItsOwn()
        {
            _out.WriteLine($"{"angle",7}{"verdict",24}{"worst overlap",15}{"where",10}   legs");
            _out.WriteLine(new string('-', 78));

            for (int degrees = 0; degrees <= 90; degrees += 5)
            {
                Battlefield field = TheGapAt(degrees, out UnitInstance mover, out Vec2 destination);

                Plan plan = Marching.PlanTo(
                    field.State, mover, field.Pathfinder, destination, planner: RoutePlanners.Default);

                string verdict = !plan.Found ? "no route"
                    : plan.PressedThrough ? "pressed through"
                    : !EveryLegIsClear(field, mover, plan.Path.Waypoints, plan.Hold) ? "walks through somebody"
                    : "clear";

                IReadOnlyList<Vec2> pts = plan.Path.Waypoints;

                float worst = 0f;
                int worstLeg = 0;

                for (int leg = 1; leg < pts.Count; leg++)
                {
                    Facing front = plan.Hold != null && leg < plan.Hold.Length && plan.Hold[leg].HasValue
                        ? plan.Hold[leg]!.Value
                        : Marching.AlongTheLine(pts[leg - 1], pts[leg], mover.Facing);

                    float span = Vec2.Distance(pts[leg - 1], pts[leg]);
                    int steps = Math.Max(2, (int)MathF.Ceiling(span / 0.5f));

                    for (int i = 0; i <= steps; i++)
                    {
                        Vec2 at = Vec2.Lerp(pts[leg - 1], pts[leg], (float)i / steps);
                        var pose = new OrientedRect(at, front, mover.Shape.Footprint);

                        foreach (UnitInstance other in field.State.UnitsOnField())
                        {
                            if (other.Id == mover.Id) continue;

                            float lap = OrientedRect.OverlapFraction(pose, other.Shape);

                            if (lap <= worst) continue;

                            worst = lap;
                            worstLeg = leg;
                        }
                    }
                }

                _out.WriteLine(
                    $"{degrees,6}°{verdict,24}{worst,15:0.000}{("leg " + worstLeg),10}   {pts.Count - 1}");
            }
        }


        /// <summary>
        /// The same arrangement, planned twice, with another planner having run
        /// on a separate battlefield in between.
        /// </summary>
        /// <remarks>
        /// <b>Open finding 27, written red.</b> Two isolated runs of one build
        /// were seen to answer 0° differently - pressed through at 19 s from
        /// one call site, and a 56 s way round from another - and the only
        /// difference between the call sites was that one swept every planner
        /// before judging the default. A planner is supposed to be a function of
        /// its arrangement. If it is not, a bench figure depends on the order
        /// the bench ran and a play-test cannot be rebuilt from its recording,
        /// which is <b>W7</b>.
        /// <para>
        /// The battlefields are built fresh each time from the same seed, so
        /// they are equal and separate - which is exactly the case a cache keyed
        /// on a <i>stamp of the arrangement</i> cannot tell apart.
        /// </para>
        /// </remarks>
        [Fact]
        public void ThePlannerAnswersTheSameArrangementTheSameWay()
        {
            var moved = new List<string>();
            int compared = 0;

            // With M94's levers, because that is the arrangement the fault was
            // seen in and the gate M94 has to pass before it can ship. The
            // shipping default agrees with itself; this asks whether it would
            // still agree once the arriving licence is on.
            bool wasArrival = StagedRoutePlanner.LicenceOnArrival;
            bool wasSmoothing = StagedRoutePlanner.RefuseSmoothingThatBreaks;
            bool wasNodes = GridRoutePlanner.DropUnstandableNodes;

            try
            {
                StagedRoutePlanner.LicenceOnArrival = true;
                StagedRoutePlanner.RefuseSmoothingThatBreaks = true;
                GridRoutePlanner.DropUnstandableNodes = true;

            for (int degrees = 0; degrees <= 90; degrees += 5)
            {
                string alone = RouteAt(degrees, warmed: false);
                string afterOthers = RouteAt(degrees, warmed: true);

                if (alone.Length == 0) continue;

                compared++;

                if (alone == afterOthers) continue;

                moved.Add($"{degrees}°");

                if (moved.Count == 1)
                {
                    _out.WriteLine($"  {degrees}° alone        {alone}");
                    _out.WriteLine($"  {degrees}° after others {afterOthers}");
                }
            }

            }
            finally
            {
                StagedRoutePlanner.LicenceOnArrival = wasArrival;
                StagedRoutePlanner.RefuseSmoothingThatBreaks = wasSmoothing;
                GridRoutePlanner.DropUnstandableNodes = wasNodes;
            }

            // Non-vacuity: a sweep that compared nothing proves nothing.
            Assert.True(compared >= 15, $"only {compared} of 19 angles produced a route to compare");

            Assert.True(moved.Count == 0,
                $"{moved.Count} of {compared} arrangements are planned differently depending on " +
                $"what ran before them: {string.Join(", ", moved)}. The battlefields are built " +
                "fresh from the same seed and are never shared, so a planner that answers them " +
                "differently is reading something left behind by the last order.");
        }

        /// <summary>
        /// The default planner's route at one angle, optionally after another
        /// planner has run over its own separate battlefields first.
        /// </summary>
        private static string RouteAt(int degrees, bool warmed)
        {
            if (warmed)
            {
                foreach (IRoutePlanner other in RoutePlanners.All)
                {
                    if (ReferenceEquals(other, RoutePlanners.Default)) continue;

                    Battlefield warm = TheGapAt(degrees, out UnitInstance mover, out Vec2 to);

                    Marching.PlanTo(warm.State, mover, warm.Pathfinder, to, planner: other);
                }
            }

            Battlefield field = TheGapAt(degrees, out UnitInstance unit, out Vec2 destination);

            Plan plan = Marching.PlanTo(
                field.State, unit, field.Pathfinder, destination, planner: RoutePlanners.Default);

            if (!plan.Found) return string.Empty;

            var line = new System.Text.StringBuilder();

            foreach (Vec2 at in plan.Path.Waypoints)
                line.Append(System.FormattableString.Invariant($"({at.X:0.00},{at.Y:0.00})"));

            return line.ToString();
        }


        /// <summary>Both call patterns, back to back, in one process.</summary>
        [Fact(Skip = "A record of a measurement rather than a check on one - open finding 27.")]
        public void TheTwoCallPatternsSideBySide()
        {
            foreach ((string name, bool arrival, bool smoothing, bool nodes) in new[]
            {
                ("none of the three", false, false, false),
                ("arriving licence", true, false, false),
                ("smoothing refused", false, true, false),
                ("nodes dropped", false, false, true),
                ("all three", true, true, true),
            })
            {
                bool was = StagedRoutePlanner.LicenceOnArrival;
                bool wasS = StagedRoutePlanner.RefuseSmoothingThatBreaks;
                bool wasN = GridRoutePlanner.DropUnstandableNodes;

                try
                {
                    StagedRoutePlanner.LicenceOnArrival = arrival;
                    StagedRoutePlanner.RefuseSmoothingThatBreaks = smoothing;
                    GridRoutePlanner.DropUnstandableNodes = nodes;

                    _out.WriteLine($"=== {name} ===");
                    _out.WriteLine($"{"angle",7}{"cold",26}{"s",8}{"after a full sweep",26}{"s",8}");

                    for (int degrees = 0; degrees <= 30; degrees += 5)
                    {
                        (string cold, double coldS) = Verdict(degrees, sweepFirst: false);
                        (string warm, double warmS) = Verdict(degrees, sweepFirst: true);

                        _out.WriteLine($"{degrees,6}°{cold,26}{coldS,8:0.0}{warm,26}{warmS,8:0.0}");
                    }

                    _out.WriteLine(string.Empty);
                }
                finally
                {
                    StagedRoutePlanner.LicenceOnArrival = was;
                    StagedRoutePlanner.RefuseSmoothingThatBreaks = wasS;
                    GridRoutePlanner.DropUnstandableNodes = wasN;
                }
            }
        }

        /// <summary>One angle, optionally after every planner has swept every angle.</summary>
        private (string Verdict, double Seconds) Verdict(int degrees, bool sweepFirst)
        {
            if (sweepFirst)
                foreach (IRoutePlanner other in RoutePlanners.All)
                    for (int sweep = 0; sweep <= 90; sweep += 5)
                    {
                        Battlefield warm = TheGapAt(sweep, out UnitInstance who, out Vec2 where);
                        Marching.PlanTo(warm.State, who, warm.Pathfinder, where, planner: other);
                    }

            Battlefield field = TheGapAt(degrees, out UnitInstance mover, out Vec2 destination);

            Plan plan = Marching.PlanTo(
                field.State, mover, field.Pathfinder, destination, planner: RoutePlanners.Default);

            string verdict = !plan.Found ? "no route"
                : plan.PressedThrough ? "pressed through"
                : !EveryLegIsClear(field, mover, plan.Path.Waypoints, plan.Hold) ? "walks through somebody"
                : "clear";

            return (verdict, Marching.SecondsToWalk(field.State, mover, plan.Path.Waypoints, plan.Hold));
        }

        private List<string> Sweep(IRoutePlanner planner)
        {
            var failed = new List<string>();

            for (int degrees = 0; degrees <= 90; degrees += 5)
            {
                Battlefield field = TheGapAt(degrees, out UnitInstance mover, out Vec2 destination);

                Plan plan = Marching.PlanTo(
                    field.State, mover, field.Pathfinder, destination, planner: planner);

                bool clear = plan.Found
                             && !plan.PressedThrough
                             && EveryLegIsClear(field, mover, plan.Path.Waypoints, plan.Hold);

                string verdict = !plan.Found ? "no route"
                    : plan.PressedThrough ? "pressed through"
                    : !EveryLegIsClear(field, mover, plan.Path.Waypoints, plan.Hold) ? "walks through somebody"
                    : "clear";

                _out.WriteLine($"  {degrees,3}°  {verdict,-22} {plan.Path.Waypoints.Count} waypoints, " +
                               $"{Marching.SecondsToWalk(field.State, mover, plan.Path.Waypoints, plan.Hold):0} s");

                if (!clear) failed.Add($"{degrees}° ({verdict})");
            }

            _out.WriteLine($"  {19 - failed.Count} of 19 clear.");

            return failed;
        }
    }
}
