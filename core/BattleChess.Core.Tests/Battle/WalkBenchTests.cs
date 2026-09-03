using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using BattleChess.Contracts;
using BattleChess.Rules;
using Xunit;
using Xunit.Abstractions;

namespace BattleChess.Tests.Battle
{
    /// <summary>
    /// Whether eighty orders, given at once, are actually <i>walked</i> — and
    /// what the whole march costs in CPU rather than what one plan costs.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>[M160]. The measurement this project does not have.</b> Every bench in
    /// the tree prices <see cref="Marching.PlanTo"/> at tick zero from the
    /// deployment and stops there. `LeverBenchTests` reports 80 of 80 routed,
    /// 0 unwalkable and 0 pressed on all three fields — and the game the
    /// designer plays deadlocks a regiment for eighty-three ticks.
    /// </para>
    /// <para>
    /// Both are true. A plan is a claim about a line at the moment it is drawn;
    /// the field it was drawn on is gone by the second tick, because the other
    /// thirty-nine regiments are moving too. What no bench asks is the only
    /// question the player asks: <b>did they get there?</b>
    /// </para>
    /// <para>
    /// So this runs the clock. Orders to a whole army at once, then ticks until
    /// everybody has arrived or the cap runs out, counting what a player would
    /// count — who arrived, who is standing still, who walked through whom, and
    /// how much CPU the whole march cost including every re-plan.
    /// </para>
    /// </remarks>
    public sealed class WalkBenchTests
    {
        private readonly ITestOutputHelper _out;
        public WalkBenchTests(ITestOutputHelper output) => _out = output;

        public static IEnumerable<object[]> Fields => new[]
        {
            new object[] { "crucible" },
            new object[] { "longmarch" },
            new object[] { "brokencountry" },
        };

        /// <summary>
        /// Ticks a march is given before it is called stuck.
        /// </summary>
        /// <remarks>
        /// Long enough that the clock is never the answer. The Long March is
        /// 2 370 m end to end and these orders are diagonal, so a spearman at
        /// 1,59 m/s wants some 1 900 s; at 900 the bench was reporting a slow
        /// march as a failed one - thirty-two "stuck" regiments that were all
        /// still walking.
        /// </remarks>
        private const int MostTicks = 3000;

        /// <summary>How near a destination counts as arrived, in metres.</summary>
        private const float ArrivedWithin = 30f;

        internal sealed record Walk(
            int Ordered, int Routed, int Arrived, int Marching, int GaveUp, int Overlapping,
            int Ticks, double PlanMs, double TickMs, int Replans, float Left, float Moved,
            int OverlapTicks, float Deepest, int Silent, float DeepestSilent);

        /// <summary>
        /// One army of forty ordered across the field at once, then walked.
        /// </summary>
        internal static Walk Run(
            BattleState battle, IRoutePlanner planner, int cap = MostTicks, TranscriptLog? log = null)
        {
            var pathfinder = new DirectPathfinder(
                battle.Terrain, new TerrainMovementModel(TestContent.Terrain), TestContent.Terrain);

            // One army only. Two armies sent through each other measures contact
            // and morale as much as movement, and the case the designer reported
            // is a wing of its own crossing itself.
            List<UnitInstance> ordered = battle.UnitsOnField()
                .Where(u => u.Owner == battle.UnitsOnField().First().Owner)
                .ToList();

            var sentTo = new Dictionary<UnitId, Vec2>();
            var began = new Dictionary<UnitId, Vec2>();

            var planClock = new Stopwatch();
            int routed = 0;

            foreach (UnitInstance unit in ordered)
            {
                Vec2 to = BenchScenariosTests.OrderFor(battle, unit);

                sentTo[unit.Id] = to;
                began[unit.Id] = unit.Position;

                Facing arriveOn = Marching.AlongTheLine(unit.Position, to, unit.Facing);

                planClock.Start();
                Plan plan = Marching.PlanTo(
                    battle, unit, pathfinder, to, planner: planner, arriveOn: arriveOn);
                planClock.Stop();

                if (plan.Path.Found)
                {
                    routed++;
                    unit.GiveOrder(UnitOrder.MoveTo(to, bearing: arriveOn), unit.Position);
                    unit.Route = plan.ToRoute();
                }
            }

            // Movement only. Combat and morale would take regiments off the
            // field mid-march, and a regiment that died is not a regiment that
            // arrived - it would flatter every number here.
            var clock = new BattleClock()
                .Add(new OrderSystem(pathfinder))
                .Add(new ContactSystem())
                .Add(new MovementSystem());

            int before = battle.RoutesPlanned;

            var tickClock = Stopwatch.StartNew();

            int ticks = 0;

            int overlapping = 0;
            int overlapTicks = 0;
            float deepest = 0f;
            int silent = 0;
            float deepestSilent = 0f;

            for (; ticks < cap; ticks++)
            {
                clock.Advance(battle, log);

                bool anyMarching = false;
                bool anyOverlap = false;

                for (int i = 0; i < ordered.Count; i++)
                {
                    UnitInstance unit = ordered[i];

                    if (unit.Route != null && !unit.Route.IsComplete) anyMarching = true;

                    // Every tick, not only at the end. A regiment standing in
                    // another one for ninety ticks and then stepping out is the
                    // fault the designer reported, and a check that looks once
                    // when the clock stops cannot see it at all.
                    for (int j = i + 1; j < ordered.Count; j++)
                    {
                        if (OrientedRect.Overlaps(unit.Shape, ordered[j].Shape))
                        {
                            overlapping++;
                            anyOverlap = true;

                            float deep = OrientedRect.OverlapFraction(unit.Shape, ordered[j].Shape);
                            if (deep > deepest) deepest = deep;

                            // A press-through is a legitimate answer and says
                            // so on the route [M26/M65]. What the designer
                            // reports as units walking through each other is
                            // the overlap NOBODY declared, so that is counted
                            // apart - it is the only column here that is a bug.
                            bool said =
                                (unit.Route?.PressingThrough ?? false) ||
                                (ordered[j].Route?.PressingThrough ?? false);

                            if (!said)
                            {
                                silent++;
                                if (deep > deepestSilent) deepestSilent = deep;
                            }
                        }
                    }
                }

                if (anyOverlap) overlapTicks++;

                if (!anyMarching) break;
            }

            tickClock.Stop();

            int arrived = 0, marching = 0, gaveUp = 0;
            float left = 0f, moved = 0f;

            foreach (UnitInstance unit in ordered)
            {
                float short_ = Vec2.Distance(unit.Position, sentTo[unit.Id]);

                // Three different endings, and they want three different
                // remedies: arrived, still walking when the clock ran out, and
                // standing with no route left - which is the only real failure.
                if (short_ <= ArrivedWithin) arrived++;
                else if (unit.Route != null && !unit.Route.IsComplete) marching++;
                else gaveUp++;

                float straight = Vec2.Distance(began[unit.Id], sentTo[unit.Id]);

                if (straight > 1f)
                {
                    left += short_ / straight;
                    moved += Vec2.Distance(began[unit.Id], unit.Position) / straight;
                }

            }

            return new Walk(
                ordered.Count, routed, arrived, marching, gaveUp, overlapping, ticks,
                planClock.Elapsed.TotalMilliseconds, tickClock.Elapsed.TotalMilliseconds,
                battle.RoutesPlanned - before,
                ordered.Count > 0 ? left / ordered.Count : 0f,
                ordered.Count > 0 ? moved / ordered.Count : 0f,
                overlapTicks, deepest, silent, deepestSilent);
        }

        /// <summary>
        /// What the march spent fifteen hundred re-plans saying.
        /// </summary>
        /// <remarks>
        /// The walk bench says forty orders cost 1 500 re-plans and 13 seconds
        /// of CPU and get eight to fifteen regiments home. This is the same run
        /// with the recording kept, tallied by what the rules actually said, so
        /// the cause is read rather than guessed at.
        /// </remarks>
        /// <summary>
        /// How much of the overlap is declared, and how much of it was there
        /// before anybody moved.
        /// </summary>
        /// <remarks>
        /// A press-through is a legitimate answer [M26/M65] and a deployment
        /// that starts touching is not a movement fault at all. Until both are
        /// subtracted, a raw overlap count cannot be read as a bug.
        /// </remarks>
        [Fact(Skip = "A record, not a check: three full marches. The answer is M160 - every overlap onset begins at or under the grazing tolerance.")]
        public void HowMuchOfTheOverlapIsDeclared()
        {
            _out.WriteLine(
                "field           at rest  ticks  pairs  declared  silent  worst overlap  deepest pair");
            _out.WriteLine(new string('-', 100));

            foreach (object[] row in Fields)
            {
                string field = (string)row[0];

                BattleState battle = BenchScenariosTests.Load(field);

                var all = battle.UnitsOnField().ToList();

                int standing = 0;

                for (int i = 0; i < all.Count; i++)
                for (int j = i + 1; j < all.Count; j++)
                    if (OrientedRect.Overlaps(all[i].Shape, all[j].Shape)) standing++;

                var pathfinder = new DirectPathfinder(
                    battle.Terrain, new TerrainMovementModel(TestContent.Terrain), TestContent.Terrain);

                List<UnitInstance> ordered = all.Where(u => u.Owner == all[0].Owner).ToList();

                foreach (UnitInstance unit in ordered)
                {
                    Vec2 to = BenchScenariosTests.OrderFor(battle, unit);
                    Facing arriveOn = Marching.AlongTheLine(unit.Position, to, unit.Facing);

                    Plan plan = Marching.PlanTo(
                        battle, unit, pathfinder, to, planner: RoutePlanners.Default, arriveOn: arriveOn);

                    if (!plan.Path.Found) continue;

                    unit.GiveOrder(UnitOrder.MoveTo(to, bearing: arriveOn), unit.Position);
                    unit.Route = plan.ToRoute();
                }

                var clock = new BattleClock()
                    .Add(new OrderSystem(pathfinder))
                    .Add(new ContactSystem())
                    .Add(new MovementSystem());

                int declared = 0, silent = 0, ticks = 0;
                float deepest = 0f;

                for (; ticks < MostTicks; ticks++)
                {
                    clock.Advance(battle);

                    bool anyMarching = false;

                    for (int i = 0; i < ordered.Count; i++)
                    {
                        UnitInstance a = ordered[i];

                        if (a.Route != null && !a.Route.IsComplete) anyMarching = true;

                        for (int j = i + 1; j < ordered.Count; j++)
                        {
                            UnitInstance b = ordered[j];

                            if (!OrientedRect.Overlaps(a.Shape, b.Shape)) continue;

                            float deep = OrientedRect.OverlapFraction(a.Shape, b.Shape);
                            if (deep > deepest) deepest = deep;

                            bool said =
                                (a.Route?.PressingThrough ?? false) ||
                                (b.Route?.PressingThrough ?? false);

                            if (said) declared++; else silent++;
                        }
                    }

                    if (!anyMarching) break;
                }

                _out.WriteLine(
                    $"{field,-14} {standing,7} {ticks,6} {declared + silent,6} {declared,9} " +
                    $"{silent,7}  {deepest,13:0.00}");
            }
        }

        [Fact(Skip = "The record of a measurement rather than a check on one - it walks three armies to arrival with the recording kept. Un-skip to re-take it; the answer is M160 in docs/DECISIONS.md.")]
        public void WhatTheMarchSpendsItsRePlansOn()
        {
            string[] phrases =
            {
                "which was not there when the route was drawn",
                "can see straight to where it was sent again",
                "the planner gave back the same route",
                "would turn back into what it is leaving",
                "the frame had no route left to give",
                "cannot get past",
                "can find nowhere near that point to stand",
                "no way through at any bearing",
                "is wheeling rather than marching",
                "reached its destination",
                "shouldering",
                "has stopped",
            };

            foreach (object[] row in Fields)
            {
                string field = (string)row[0];

                BattleState battle = BenchScenariosTests.Load(field);
                var log = new TranscriptLog();

                Walk walk = Run(battle, RoutePlanners.Default, MostTicks, log);

                _out.WriteLine(
                    $"--- {field}: {walk.Arrived} of {walk.Ordered} arrived, {walk.Replans} re-plans, " +
                    $"{log.Lines.Count} lines");

                foreach (string phrase in phrases)
                {
                    int count = log.Count(phrase);

                    if (count > 0) _out.WriteLine($"    {count,6}  {phrase}");
                }

                _out.WriteLine(string.Empty);
            }
        }

        /// <summary>
        /// The refusal's two knobs swept together: how long a regiment waits,
        /// and whether waiting binds both halves of a meeting or only the one
        /// that must give way.
        /// </summary>
        /// <summary>
        /// Where an overlap actually begins, classified at the tick it starts.
        /// </summary>
        /// <remarks>
        /// Counting pair-ticks says how much overlap there is and nothing about
        /// how it got there, and the refusal rule turned out to be working -
        /// its patience never once ran out - while the overlap barely moved. So
        /// this watches the transition instead: the tick a pair goes from clear
        /// to lapped, and what each of them was doing on it.
        /// </remarks>
        /// <summary>
        /// How much deeper a step may go, swept. [M160]
        /// </summary>
        /// <remarks>
        /// The first setting was 2% of the body a tick, chosen so a manoeuvre
        /// that is working is not refused over rounding. It is also 100% over
        /// fifty ticks, which is exactly the burial it was meant to stop - the
        /// rule was right and the number made it a no-op.
        /// </remarks>
        /// <summary>
        /// How near a marcher has to be before re-drawing around it is worth
        /// the price. [M160]
        /// </summary>
        [Fact(Skip = "The sweep that chose 120 m - eighteen marches. The table is M160 in docs/DECISIONS.md.")]
        public void HowNearAMarcherHasToBeToCount()
        {
            bool wasRefuse = MovementSystem.RefuseToWalkIntoYourOwn;
            bool wasDeeper = MovementSystem.NoDeeperIntoYourOwn;
            float wasWithin = OrderSystem.MarcherIsAWallWithin;

            // The two rules that did not earn their place are off for this, so
            // the column being read is this lever and nothing else.
            MovementSystem.RefuseToWalkIntoYourOwn = false;
            MovementSystem.NoDeeperIntoYourOwn = false;

            _out.WriteLine(
                "a wall within  field           arrived gaveup   overlaps    SILENT  ticks   walk ms  replans");
            _out.WriteLine(new string('-', 100));

            try
            {
                foreach (float within in new[] { 0f, 60f, 120f, 250f, 500f, 99999f })
                {
                    OrderSystem.MarcherIsAWallWithin = within;

                    foreach (object[] row in Fields)
                    {
                        string field = (string)row[0];

                        BattleState battle = BenchScenariosTests.Load(field);

                        Walk walk = Run(battle, RoutePlanners.Default);

                        _out.WriteLine(
                            $"{within,13:0}  {field,-14} {walk.Arrived,7} {walk.GaveUp,6} " +
                            $"{walk.Overlapping,10} {walk.Silent,9} {walk.Ticks,6} " +
                            $"{walk.TickMs,9:0.0} {walk.Replans,8}");
                    }

                    _out.WriteLine(string.Empty);
                }
            }
            finally
            {
                MovementSystem.RefuseToWalkIntoYourOwn = wasRefuse;
                MovementSystem.NoDeeperIntoYourOwn = wasDeeper;
                OrderSystem.MarcherIsAWallWithin = wasWithin;
            }
        }

        [Fact(Skip = "A record of a sweep whose answer was that the lever does nothing. M160.")]
        public void HowMuchDeeperAStepMayGo()
        {
            float wasWalls = OrderSystem.MarcherIsAWallWithin;
            bool wasRefuse = MovementSystem.RefuseToWalkIntoYourOwn;
            bool wasDeeper = MovementSystem.NoDeeperIntoYourOwn;
            float wasTol = MovementSystem.DeepeningTolerance;

            OrderSystem.MarcherIsAWallWithin = 0f;
            MovementSystem.RefuseToWalkIntoYourOwn = true;
            MovementSystem.NoDeeperIntoYourOwn = true;

            _out.WriteLine(
                "deeper/tick  field           arrived gaveup  overlaps/ticks  deepest  ticks   walk ms");
            _out.WriteLine(new string('-', 92));

            try
            {
                foreach (float tol in new[] { 0.02f, 0.005f, 0.001f, 0f })
                {
                    MovementSystem.DeepeningTolerance = tol;

                    foreach (object[] row in Fields)
                    {
                        string field = (string)row[0];

                        BattleState battle = BenchScenariosTests.Load(field);

                        Walk walk = Run(battle, RoutePlanners.Default);

                        _out.WriteLine(
                            $"{tol,11:0.000}  {field,-14} {walk.Arrived,7} {walk.GaveUp,6} " +
                            $"{walk.Overlapping,9}/{walk.OverlapTicks,-6} {walk.Deepest,7:0.00} " +
                            $"{walk.Ticks,6} {walk.TickMs,9:0.0}");
                    }

                    _out.WriteLine(string.Empty);
                }
            }
            finally
            {
                OrderSystem.MarcherIsAWallWithin = wasWalls;
                MovementSystem.RefuseToWalkIntoYourOwn = wasRefuse;
                MovementSystem.NoDeeperIntoYourOwn = wasDeeper;
                MovementSystem.DeepeningTolerance = wasTol;
            }
        }

        [Fact(Skip = "A record: three full marches classifying every overlap onset. The answer is M160.")]
        public void WhereAnOverlapBegins()
        {
            float wasWalls = OrderSystem.MarcherIsAWallWithin;
            bool wasRefuse = MovementSystem.RefuseToWalkIntoYourOwn;

            OrderSystem.MarcherIsAWallWithin = 0f;
            MovementSystem.RefuseToWalkIntoYourOwn = true;

            _out.WriteLine(
                "field           onsets  declared  wheeled  both moved  one moved  neither  " +
                "graze  past the rule");
            _out.WriteLine(new string('-', 106));

            try
            {
                foreach (object[] row in Fields)
                {
                    string field = (string)row[0];

                    BattleState battle = BenchScenariosTests.Load(field);

                    var pathfinder = new DirectPathfinder(
                        battle.Terrain, new TerrainMovementModel(TestContent.Terrain), TestContent.Terrain);

                    List<UnitInstance> ordered = battle.UnitsOnField()
                        .Where(u => u.Owner == battle.UnitsOnField().First().Owner).ToList();

                    foreach (UnitInstance unit in ordered)
                    {
                        Vec2 to = BenchScenariosTests.OrderFor(battle, unit);
                        Facing arriveOn = Marching.AlongTheLine(unit.Position, to, unit.Facing);

                        Plan plan = Marching.PlanTo(
                            battle, unit, pathfinder, to, planner: RoutePlanners.Default, arriveOn: arriveOn);

                        if (!plan.Path.Found) continue;

                        unit.GiveOrder(UnitOrder.MoveTo(to, bearing: arriveOn), unit.Position);
                        unit.Route = plan.ToRoute();
                    }

                    var clock = new BattleClock()
                        .Add(new OrderSystem(pathfinder))
                        .Add(new ContactSystem())
                        .Add(new MovementSystem());

                    var lapped = new HashSet<(int, int)>();
                    var wasAt = new Vec2[ordered.Count];
                    var facedAt = new float[ordered.Count];

                    int onsets = 0, declared = 0, wheeled = 0, bothMoved = 0, oneMoved = 0, neither = 0;
                    int grazes = 0, deepOnsets = 0;

                    for (int tick = 0; tick < MostTicks; tick++)
                    {
                        for (int i = 0; i < ordered.Count; i++)
                        {
                            wasAt[i] = ordered[i].Position;
                            facedAt[i] = ordered[i].Facing.Degrees;
                        }

                        clock.Advance(battle);

                        bool anyMarching = false;

                        for (int i = 0; i < ordered.Count; i++)
                        {
                            if (ordered[i].IsMarching) anyMarching = true;

                            for (int j = i + 1; j < ordered.Count; j++)
                            {
                                bool now = OrientedRect.Overlaps(ordered[i].Shape, ordered[j].Shape);

                                if (!now) { lapped.Remove((i, j)); continue; }

                                if (!lapped.Add((i, j))) continue;

                                onsets++;

                                bool said =
                                    (ordered[i].Route?.PressingThrough ?? false) ||
                                    (ordered[j].Route?.PressingThrough ?? false);

                                float turnedI = MathF.Abs(ordered[i].Facing.Degrees - facedAt[i]);
                                float turnedJ = MathF.Abs(ordered[j].Facing.Degrees - facedAt[j]);

                                bool movedI = Vec2.Distance(ordered[i].Position, wasAt[i]) > 0.01f;
                                bool movedJ = Vec2.Distance(ordered[j].Position, wasAt[j]) > 0.01f;

                                // How deep it starts. The rule that refuses a
                                // step allows a graze - OverlapFraction up to
                                // OrderSystem.GrazingTolerance, 5% - so an
                                // onset shallower than that is the tolerance
                                // working as written, and one deeper than it is
                                // the rule being got round.
                                float deep = OrientedRect.OverlapFraction(
                                    ordered[i].Shape, ordered[j].Shape);

                                if (deep <= OrderSystem.GrazingTolerance) grazes++;
                                else deepOnsets++;

                                if (said) declared++;
                                else if (!movedI && !movedJ && (turnedI > 0.01f || turnedJ > 0.01f)) wheeled++;
                                else if (movedI && movedJ) bothMoved++;
                                else if (movedI || movedJ) oneMoved++;
                                else neither++;
                            }
                        }

                        if (!anyMarching) break;
                    }

                    _out.WriteLine(
                        $"{field,-14} {onsets,7} {declared,9} {wheeled,8} {bothMoved,11} " +
                        $"{oneMoved,10} {neither,8} {grazes,6} {deepOnsets,14}");
                }
            }
            finally
            {
                OrderSystem.MarcherIsAWallWithin = wasWalls;
                MovementSystem.RefuseToWalkIntoYourOwn = wasRefuse;
            }
        }

        [Fact(Skip = "A record, and of a rule that is off: six settings over three marches. The answer is M160 - patience never expires, so the setting is inert.")]
        public void HowLongToWaitAndWhoWaits()
        {
            float wasWalls = OrderSystem.MarcherIsAWallWithin;
            bool wasRefuse = MovementSystem.RefuseToWalkIntoYourOwn;
            int wasPatience = MovementSystem.PatienceTicks;
            bool wasYielder = MovementSystem.OnlyTheYielderWaits;

            OrderSystem.MarcherIsAWallWithin = 0f;
            MovementSystem.RefuseToWalkIntoYourOwn = true;

            _out.WriteLine(
                "waits  patience  field           arrived gaveup  overlaps/ticks  ticks   walk ms  replans");
            _out.WriteLine(new string('-', 96));

            try
            {
                foreach (bool yielderOnly in new[] { false, true })
                foreach (int patience in new[] { 15, 45, 120 })
                {
                    MovementSystem.OnlyTheYielderWaits = yielderOnly;
                    MovementSystem.PatienceTicks = patience;

                    foreach (object[] row in Fields)
                    {
                        string field = (string)row[0];

                        BattleState battle = BenchScenariosTests.Load(field);

                        Walk walk = Run(battle, RoutePlanners.Default);

                        _out.WriteLine(
                            $"{(yielderOnly ? "yielder" : "both"),-6} {patience,8}  {field,-14} " +
                            $"{walk.Arrived,7} {walk.GaveUp,6} {walk.Overlapping,9}/{walk.OverlapTicks,-6} " +
                            $"{walk.Ticks,6} {walk.TickMs,8:0.0} {walk.Replans,8}");
                    }

                    _out.WriteLine(string.Empty);
                }
            }
            finally
            {
                OrderSystem.MarcherIsAWallWithin = wasWalls;
                MovementSystem.RefuseToWalkIntoYourOwn = wasRefuse;
                MovementSystem.PatienceTicks = wasPatience;
                MovementSystem.OnlyTheYielderWaits = wasYielder;
            }
        }

        [Fact]
        public void AWholeArmyOrderedAtOnceActuallyGetsThere()
        {
            _out.WriteLine(
                "field           ordered arrived gaveup   overlaps    SILENT  ticks   plan ms   walk ms  " +
                "replans");
            _out.WriteLine(new string('-', 104));

            var short_ = new List<string>();

            foreach (object[] row in Fields)
            {
                string field = (string)row[0];

                BattleState battle = BenchScenariosTests.Load(field);

                Walk walk = Run(battle, RoutePlanners.Default);

                _out.WriteLine(
                    $"{field,-14} {walk.Ordered,8} {walk.Arrived,7} {walk.GaveUp,6} {walk.Overlapping,10} " +
                    $"{walk.Silent,9} {walk.Ticks,6} {walk.PlanMs,9:0.0} {walk.TickMs,9:0.0} " +
                    $"{walk.Replans,8}");

                // The gate, and it is the question no other bench asks: did
                // they get there? Nine in ten, because the fields deliberately
                // send forty regiments across each other into the other army's
                // deployment and a couple of them genuinely cannot finish.
                if (walk.Arrived * 10 < walk.Ordered * 9)
                    short_.Add($"{field}: {walk.Arrived} of {walk.Ordered} arrived");
            }

            Assert.True(
                short_.Count == 0,
                "an army ordered across the field did not get there - " + string.Join("; ", short_));
        }
    }
}
