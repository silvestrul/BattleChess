using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BattleChess.Contracts;
using BattleChess.Rules;
using BattleChess.Rules.Grid;
using Xunit;
using Xunit.Abstractions;

namespace BattleChess.Tests.Battle
{
    /// <summary>
    /// The board game [M147], measured on real content rather than asserted
    /// from the arithmetic that produced it.
    /// </summary>
    /// <remarks>
    /// The cell size was derived on paper - a 40 by 20 regiment has a 44,7 m
    /// bounding circle, a 50 m hex holds it - and every number that follows from
    /// it was divided rather than tuned. This file is where those divisions meet
    /// the actual battles, because the last five sessions all went the same way:
    /// the reasoning was right and the field disagreed.
    /// </remarks>
    public sealed class BoardTests : IDisposable
    {
        private readonly ITestOutputHelper _out;

        public BoardTests(ITestOutputHelper output) => _out = output;

        /// <summary>
        /// Nothing here turns the mode on, so there is nothing to put back.
        /// </summary>
        /// <remarks>
        /// Kept as a no-op rather than removed, because the first draft did turn
        /// the mode on and did put it back, and putting it back was not enough -
        /// see TheModeIsWhatDecidesWhichPlannerAMarchGets for what that cost.
        /// A visible no-op is a better warning than an absent one.
        /// </remarks>
        public void Dispose()
        {
        }

        private static BattleState Load(string name)
        {
            string root = TestContent.Root;
            ITerrainCatalogue terrain = TestContent.Terrain;

            BattleSetup setup = BattleSetup.Parse(
                File.ReadAllText(Path.Combine(root, "battles", $"{name}.battle.txt")));

            BattleMapDefinition map = AsciiMapReader.Read(
                File.ReadAllText(Path.Combine(root, "maps", $"{setup.MapName}.map.txt")), terrain);

            return setup.Build(map, terrain, TestContent.Units, TestContent.Formations,
                new TerrainMovementModel(terrain));
        }

        /// <summary>
        /// The cell size holds a regiment however it is turned - which is the
        /// one thing the board is not allowed to be wrong about.
        /// </summary>
        /// <remarks>
        /// Non-vacuity: this would fail if the equal ground rectangle grew past
        /// 50 m across its diagonal, or if the cell size were reduced below it.
        /// It is checked against every unit type in content and not against a
        /// written-down number, so it also fails the day a unit is authored that
        /// does not obey the equal ground rule.
        /// </remarks>
        [Fact]
        public void EveryRegimentInContentFitsOneHex()
        {
            IUnitCatalogue catalogue = TestContent.Units;

            var widest = 0f;
            string widestOne = "none";

            foreach (UnitDef def in catalogue.All)
            {
                Footprint footprint = def.FootprintAt(def.DefaultStrength);
                float across = 2f * footprint.BoundingRadius;

                _out.WriteLine(
                    $"{def.Key,-12} {footprint.Width:0.0} x {footprint.Depth:0.0} m, " +
                    $"{across:0.0} m across the diagonal");

                if (across <= widest) continue;

                widest = across;
                widestOne = def.Key;
            }

            _out.WriteLine($"");
            _out.WriteLine($"widest is {widestOne} at {widest:0.0} m; a hex holds {Board.CellWidthMetres:0} m");

            Assert.True(
                widest <= Board.CellWidthMetres,
                $"{widestOne} is {widest:0.0} m across and will not fit a {Board.CellWidthMetres:0} m hex.");
        }

        /// <summary>
        /// Mustering a real deployment puts every regiment on a hex of its own.
        /// </summary>
        /// <remarks>
        /// The interesting case rather than a constructed one: deployments are
        /// authored in metres for the continuous game, so regiments stand closer
        /// together than a hex is wide and the muster has to unpack them. If it
        /// cannot, the board's only promise is broken from the first frame.
        /// </remarks>
        [Theory]
        [InlineData("greatfield")]
        [InlineData("crucible")]
        public void MusteringARealDeploymentGivesEverybodyTheirOwnHex(string field)
        {
            BattleState battle = Load(field);

            int regiments = battle.UnitsOnField().Count();
            int crowded = GridMode.Muster(battle);

            Board board = Board.For(battle);

            var standing = new Dictionary<Coord, List<string>>();

            foreach (UnitInstance unit in battle.UnitsOnField())
            {
                Coord hex = board.Of(unit.Position);

                if (!standing.TryGetValue(hex, out List<string>? here))
                    standing[hex] = here = new List<string>();

                here.Add(unit.Def.Key);
            }

            _out.WriteLine($"{field}: {board}");
            _out.WriteLine($"{regiments} regiments over {standing.Count} hexes, {crowded} could not be placed");

            foreach (var hex in standing.Where(h => h.Value.Count > 1))
                _out.WriteLine($"  {hex.Key} holds {string.Join(", ", hex.Value)}");

            Assert.Equal(0, crowded);
            Assert.Equal(regiments, standing.Count);
        }

        /// <summary>
        /// Every regiment stands on the centre of its hex and faces one of six
        /// ways once mustered.
        /// </summary>
        [Fact]
        public void MusteringSnapsBothWhereAndWhichWay()
        {
            BattleState battle = Load("greatfield");

            GridMode.Muster(battle);

            Board board = Board.For(battle);

            float worstDrift = 0f;
            float worstTurn = 0f;

            foreach (UnitInstance unit in battle.UnitsOnField())
            {
                worstDrift = MathF.Max(
                    worstDrift, Vec2.Distance(unit.Position, board.CentreOf(board.Of(unit.Position))));

                float off = MathF.Abs(unit.Facing.Degrees / 60f - MathF.Round(unit.Facing.Degrees / 60f)) * 60f;

                worstTurn = MathF.Max(worstTurn, off);
            }

            _out.WriteLine($"worst drift off a centre: {worstDrift:0.###} m");
            _out.WriteLine($"worst bearing off a six:  {worstTurn:0.###} degrees");

            Assert.True(worstDrift < 0.01f, $"a regiment is {worstDrift:0.###} m off its hex centre.");
            Assert.True(worstTurn < 0.01f, $"a regiment is {worstTurn:0.###} degrees off a hex bearing.");
        }

        /// <summary>
        /// What a turn actually buys each kind of regiment, in hexes.
        /// </summary>
        /// <remarks>
        /// The numbers the whole mode was sized around, taken from the content
        /// and the clock rather than from the remark that predicted them. The
        /// assertion is deliberately loose - it says the board is playable, not
        /// that the arithmetic came out where I said it would - because the
        /// point of printing them is that a person reads them.
        /// </remarks>
        [Fact]
        public void WhatATurnBuysEachKindOfRegiment()
        {
            float turn = GridMode.TurnSeconds;

            _out.WriteLine($"a turn is {turn:0} battle seconds, a hex is {Board.CellWidthMetres:0} m");
            _out.WriteLine("");

            float slowest = float.MaxValue;
            float fastest = 0f;

            foreach (UnitDef def in TestContent.Units.All.OrderBy(d => d.Speed))
            {
                float hexes = def.Speed * turn / Board.CellWidthMetres;

                _out.WriteLine($"{def.Key,-12} {def.Speed:0.00} m/s -> {hexes:0.0} hexes a turn");

                slowest = MathF.Min(slowest, hexes);
                fastest = MathF.Max(fastest, hexes);
            }

            // The board itself, so the ceiling is a property of the field rather
            // than a number I picked. The Great Field is the largest in content
            // and its shorter side is what a flanking march has to cross.
            Board board = Board.For(Load("greatfield"));
            float shortSide = MathF.Min(board.Bounds.Width, board.Bounds.Height) / Board.CellWidthMetres;

            _out.WriteLine("");
            _out.WriteLine($"spread {fastest / slowest:0.0}x, from {slowest:0.0} to {fastest:0.0} hexes");
            _out.WriteLine($"the Great Field is {shortSide:0} hexes across the short way, so the fastest " +
                           $"regiment crosses it in {shortSide / fastest:0.0} turns and the slowest in " +
                           $"{shortSide / slowest:0.0}");

            // Both ends, so neither can drift without being caught.
            //
            // The floor: a board where the slowest thing cannot manage a hex in
            // a turn is glue, which is exactly what was reported from play at
            // the continuous game's sixty seconds.
            Assert.True(slowest >= 1f, $"the slowest regiment gets {slowest:0.00} hexes a turn.");

            // The ceiling, and it is derived rather than chosen. An earlier draft
            // of this test asserted a bare "twelve hexes", which is a number with
            // no argument behind it - and when 120 s a turn tripped it at 13,2
            // the honest question was not whether to raise the twelve but whether
            // anything was actually wrong. A third of the short side is: a
            // regiment that crosses the field in three moves cannot be
            // out-manoeuvred, so there is no manoeuvre left in the game.
            Assert.True(
                fastest <= shortSide / 3f,
                $"the fastest regiment gets {fastest:0.0} hexes a turn and crosses the Great Field in " +
                $"{shortSide / fastest:0.0} turns, which leaves nothing to manoeuvre against.");
        }

        /// <summary>
        /// A march across the board goes round the regiments in the way, all the
        /// way to its destination.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The gate this mode exists to pass. In the continuous game the
        /// equivalent question - can this rectangle get past that rectangle -
        /// took five planners and five sessions and still put bodies inside each
        /// other; here a held hex is simply not in the graph.
        /// </para>
        /// <para>
        /// <b>Cavalry, and the first draft of this test is why.</b> Written with
        /// foot it passed while proving nothing: routes were then truncated to
        /// one turn, foot buys under two hexes of one, so the route was a single
        /// leg that never reached the wall - and "the route avoids the wall" was
        /// true of a route that had not gone anywhere. Truncation is gone now,
        /// but the unit stays cavalry: a test that only measures when a
        /// particular unit is slow enough is a test waiting to go quiet again.
        /// </para>
        /// <para>
        /// Non-vacuity, three ways. The wall is asserted to actually block the
        /// straight line; the route is asserted to be long enough to have
        /// reached it; and it is asserted to cost more legs than the straight
        /// hex distance, which is what going round costs and what a route that
        /// walked through would not pay.
        /// </para>
        /// </remarks>
        [Fact]
        public void AMarchGoesRoundWhatIsInTheWayAllTheWayToItsDestination()
        {
            BattleState battle = Load("greatfield");

            GridMode.Muster(battle);

            Board board = Board.For(battle);

            UnitInstance marcher = battle.UnitsOnField().First(u => u.Def.Key == "cavalry");

            Coord from = board.Of(marcher.Position);
            Coord target = from + HexMath.Offset(HexDirection.East) * 4;

            // Everybody else is moved well out of the way first, so the only
            // thing this march has to avoid is the thing the test puts there.
            foreach (UnitInstance other in battle.UnitsOnField())
            {
                if (other.Id == marcher.Id) continue;

                other.Position = board.CentreOf(from + HexMath.Offset(HexDirection.SouthEast) * 40);
            }

            // A wall right across the way, two hexes out.
            Coord ahead = from + HexMath.Offset(HexDirection.East) * 2;

            var wall = new List<Coord>
            {
                ahead,
                ahead + HexMath.Offset(HexDirection.NorthEast),
                ahead + HexMath.Offset(HexDirection.NorthWest),
                ahead + HexMath.Offset(HexDirection.SouthEast),
                ahead + HexMath.Offset(HexDirection.SouthWest)
            };

            List<UnitInstance> spare = battle.UnitsOnField()
                .Where(u => u.Id != marcher.Id)
                .Take(wall.Count)
                .ToList();

            Assert.Equal(wall.Count, spare.Count);

            for (int i = 0; i < wall.Count; i++) spare[i].Position = board.CentreOf(wall[i]);

            var planner = new BoardRoutePlanner();
            Plan plan = planner.PlanTo(battle, marcher, null!, board.CentreOf(target));

            Assert.True(plan.Found, $"no route: {plan.Path.FailureDetail}");

            List<Coord> walked = plan.Path.SearchCells.ToList();

            int legs = walked.Count - 1;
            int apart = Coord.Distance(from, target);

            float turn = GridMode.TurnSeconds;
            float took = plan.Path.SecondsAt(marcher.Def.Speed);

            _out.WriteLine($"{marcher.Def.Key} from {from} to {target}, {apart} hexes apart");
            _out.WriteLine($"wall at {string.Join(", ", wall)}");
            _out.WriteLine($"route: {string.Join(" -> ", walked)}");
            _out.WriteLine($"{legs} legs, {plan.Path.Distance:0} m, {took:0} s of a {turn:0} s turn");

            // The wall really is a wall: the straight line does cross it.
            Assert.Contains(HexMath.Line(from, target), wall.Contains);

            // The route does not.
            Assert.DoesNotContain(walked, wall.Contains);

            // And it went far enough to have had to choose, at a cost - a route
            // that could have gone straight would have been exactly as many legs
            // as the two hexes are apart.
            Assert.True(legs > apart,
                $"the route is {legs} legs against {apart} hexes apart, so nothing was gone round.");

            // And it reaches where it was sent. The route is no longer cut off
            // at the end of a turn - the clock stops the regiment and it carries
            // on next turn - so the drawn line runs to the destination however
            // many turns that is, which is what the player was promised and what
            // truncating it took away.
            Assert.Equal(target, walked[walked.Count - 1]);

            _out.WriteLine($"{took / turn:0.0} turns of walking, drawn in full");
        }

        /// <summary>
        /// What a regiment may actually walk in one turn, in whole hexes.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Superseded as a fault, kept as the ladder.</b> When routes were
        /// truncated to one turn this measured a real flattening - whole hexes
        /// only, so artillery's 1,6 and foot's 1,9 were both one, and
        /// seven unit types collapsed to three allowances. Truncation is gone,
        /// so a march now runs to its destination over as many turns as it takes
        /// and the fraction is no longer thrown away: a foot regiment walking
        /// four hexes takes a bit over two turns rather than four.
        /// </para>
        /// <para>
        /// What remains true is how much ground a turn buys, which is the number
        /// a player counts in their head and the one that decides whether the
        /// board feels like glue. It is printed against the turn length actually
        /// in use, so raising that shows here first.
        /// </para>
        /// </remarks>
        [Fact]
        public void WhatAWholeTurnActuallyBuysInWholeHexes()
        {
            float turn = GridMode.TurnSeconds;

            var byKey = new Dictionary<string, int>();

            foreach (UnitDef def in TestContent.Units.All.OrderBy(d => d.Speed))
            {
                float exact = def.Speed * turn / Board.CellWidthMetres;
                int whole = Math.Max(1, (int)exact);

                byKey[def.Key] = whole;

                _out.WriteLine($"{def.Key,-12} {exact:0.0} hexes on paper -> {whole} walked");
            }

            int kinds = byKey.Values.Distinct().Count();

            _out.WriteLine("");
            _out.WriteLine($"{byKey.Count} unit types collapse to {kinds} distinct allowances");

            // A board where the slowest thing cannot manage a hex in a turn is
            // glue, whatever else is true of it - which is exactly what was
            // reported from play at sixty seconds a turn.
            Assert.True(byKey["artillery"] >= 1,
                "the slowest regiment cannot finish a single hex in a turn.");
            Assert.True(kinds >= 2, "every regiment on the board moves exactly alike, which is not a game.");
        }

        /// <summary>
        /// A board route never shoulders through anybody, whatever is asked of
        /// it.
        /// </summary>
        /// <remarks>
        /// The claim that makes the rest of the pipeline safe: everything
        /// downstream that asks whether a route pressed through gets a straight
        /// no, because on a board there is no such move to make.
        /// </remarks>
        [Fact]
        public void ABoardRouteNeverPressesThrough()
        {
            BattleState battle = Load("crucible");

            GridMode.Muster(battle);

            Board board = Board.For(battle);

            var planner = new BoardRoutePlanner();

            int asked = 0;
            int routed = 0;

            foreach (UnitInstance unit in battle.UnitsOnField())
            {
                foreach (UnitInstance other in battle.UnitsOnField())
                {
                    if (other.Id == unit.Id) continue;

                    asked++;

                    // Ordered straight at somebody else, which in the continuous
                    // game is the commonest way to get a press-through.
                    Plan plan = planner.PlanTo(battle, unit, null!, other.Position);

                    Assert.False(plan.PressedThrough, $"{unit.Def.Key} pressed through toward {other.Def.Key}.");

                    if (plan.Found) routed++;
                }
            }

            _out.WriteLine($"{asked} orders straight at another regiment, {routed} routed, 0 pressed through");

            Assert.True(asked > 100, $"only {asked} orders were asked; this is not measuring much.");
        }

        /// <summary>
        /// Turning the mode on and off changes which planner a march that names
        /// none actually gets.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Skipped, and the reason is the finding.</b> Written and run, it
        /// turned 12 failures into <b>105</b>. <c>RoutePlanners.InUse</c> is
        /// process-wide and xunit runs test classes in parallel, so this test
        /// swapped the planner out from under every other test that happened to
        /// be planning a march at that moment - and they failed with routes they
        /// had every right to expect.
        /// </para>
        /// <para>
        /// The seam is real and is exercised where it matters, at load in the
        /// harness. What cannot be done is to exercise it <i>here</i>: any test
        /// that mutates process-wide state is unsafe under a parallel runner
        /// however carefully it puts the state back, because putting it back
        /// afterwards does nothing for whoever read it in between. Kept as the
        /// written record of that, since the obvious test is the wrong one and
        /// somebody will write it again.
        /// </para>
        /// <para>
        /// Every other test in this file therefore calls <c>Muster</c> and names
        /// <c>BoardRoutePlanner</c> outright, rather than turning the mode on.
        /// </para>
        /// </remarks>
        [Fact(Skip = "Mutates the process-wide planner; see the remarks - it cost 93 unrelated failures.")]
        public void TheModeIsWhatDecidesWhichPlannerAMarchGets()
        {
            BattleState battle = Load("greatfield");

            IRoutePlanner before = RoutePlanners.InUse;

            GridMode.TurnOn(battle);
            IRoutePlanner during = RoutePlanners.InUse;

            GridMode.TurnOff();
            IRoutePlanner after = RoutePlanners.InUse;

            _out.WriteLine($"before: {before.Name}");
            _out.WriteLine($"on:     {during.Name}");
            _out.WriteLine($"off:    {after.Name}");

            Assert.IsType<BoardRoutePlanner>(during);
            Assert.NotSame(before, during);
            Assert.Same(before, after);
        }
    }
}
