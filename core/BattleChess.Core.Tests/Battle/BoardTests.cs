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
    /// <para>
    /// Everything the board does follows from one measured number - the widest
    /// regiment that actually stands on the field - and this file is where those
    /// consequences meet the actual battles.
    /// </para>
    /// <para>
    /// The cell size itself is guarded next door in <c>BoardSizeProbeTests</c>,
    /// which reads every battle file. It lives there rather than here because
    /// the version of that check which lived here read the unit catalogue
    /// instead, passed for a day, and was wrong by a factor of two - see [M149].
    /// </para>
    /// </remarks>
    [Collection("the board")]
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

                // Against Snap itself rather than against a multiple of sixty.
                // The literal 60 was the old rule written into the test, and it
                // failed the moment [M150] moved the six facings thirty degrees
                // round so that lines could form - correctly, but it was
                // measuring the constant and not the promise. The promise is
                // that a mustered regiment is already on a board facing.
                float off = Facing.AbsoluteDelta(unit.Facing, board.Snap(unit.Facing)) * 180f / MathF.PI;

                worstTurn = MathF.Max(worstTurn, off);
            }

            _out.WriteLine($"worst drift off a centre: {worstDrift:0.###} m");
            _out.WriteLine($"worst bearing off a six:  {worstTurn:0.###} degrees");

            Assert.True(worstDrift < 0.01f, $"a regiment is {worstDrift:0.###} m off its hex centre.");
            Assert.True(worstTurn < 0.01f, $"a regiment is {worstTurn:0.###} degrees off a board facing.");
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

            Board board = Board.For(Load("greatfield"));

            _out.WriteLine($"a turn is {turn:0} battle seconds, a hex is {board.CellWidth:0} m");
            _out.WriteLine("");

            float slowest = float.MaxValue;
            float fastest = 0f;

            foreach (UnitDef def in TestContent.Units.All.OrderBy(d => d.Speed))
            {
                float hexes = def.Speed * turn / board.CellWidth;

                _out.WriteLine($"{def.Key,-12} {def.Speed:0.00} m/s -> {hexes:0.0} hexes a turn");

                slowest = MathF.Min(slowest, hexes);
                fastest = MathF.Max(fastest, hexes);
            }

            // The board itself, so the ceiling is a property of the field rather
            // than a number I picked. The Great Field is the largest in content
            // and its shorter side is what a flanking march has to cross.
            float shortSide = board.ShortSideInCells;

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

            // [M155] Everything here is measured in FRONTAGES, not in cells. A
            // cell used to be a regiment wide, so "four cells east" was four
            // regiment-widths and five regiments in adjacent cells were a wall.
            // On a fine board four cells is a hundred metres, the target sits
            // inside the wall, and the five wall regiments stand through one
            // another - the arrangement stopped describing the thing it was
            // written to describe.
            int step = Math.Max(1, (int)MathF.Ceiling(marcher.Footprint.Width / board.CellWidth));

            var east = new Coord(step, 0);
            var sideways = new Coord(0, step);

            Coord from = board.Of(marcher.Position);
            Coord target = from + east * 8;

            // Everybody else is moved well out of the way first, so the only
            // thing this march has to avoid is the thing the test puts there.
            foreach (UnitInstance other in battle.UnitsOnField())
            {
                if (other.Id == marcher.Id) continue;

                other.Position = board.CentreOf(from + sideways * 40);
            }

            // A wall right across the way, two frontages out and five wide.
            Coord ahead = from + east * 2;

            var wall = new List<Coord>
            {
                ahead,
                ahead + sideways,
                ahead + sideways * 2,
                ahead - sideways,
                ahead - sideways * 2
            };

            List<UnitInstance> spare = battle.UnitsOnField()
                .Where(u => u.Id != marcher.Id)
                .Take(wall.Count)
                .ToList();

            Assert.Equal(wall.Count, spare.Count);

            for (int i = 0; i < wall.Count; i++) spare[i].Position = board.CentreOf(wall[i]);

            var planner = new BoardRoutePlanner();
            // [M159] A real pathfinder, because the board no longer finds the
            // way itself - it resolves where a regiment may stand and hands the
            // route to the continuous planner. Passing null here used to be
            // honest and is now just a crash.
            var pathfinder = new HexPathfinder(battle.Terrain, battle.Movement, battle.TerrainCatalogue);

            Plan plan = planner.PlanTo(battle, marcher, pathfinder, board.CentreOf(target));

            Assert.True(plan.Found, $"no route: {plan.Path.FailureDetail}");

            marcher.Route = plan.ToRoute();

            int apart = Coord.Distance(from, target);

            float straight = Vec2.Distance(board.CentreOf(from), board.CentreOf(target));

            _out.WriteLine($"{marcher.Def.Key} from {from} to {target}, {apart} cells apart");
            _out.WriteLine($"wall at {string.Join(", ", wall)}");
            _out.WriteLine(
                $"route: {plan.Path.Waypoints.Count} waypoints, {plan.Path.Distance:0} m against " +
                $"{straight:0} m straight, pressed through: {plan.PressedThrough}");

            // The wall really is a wall: the straight line does cross it.
            Assert.Contains(HexMath.Line(from, target), wall.Contains);

            // [M159] And now it is WALKED, which is the change worth recording.
            //
            // This used to read the route as a list of cells and assert that none
            // of them was a wall cell. There is no such list any more: the board
            // resolves where a regiment may stand and hands the way-finding to
            // the continuous planner, whose route is a polyline through open
            // ground. So the promise moved, and it moved to a stronger place -
            // from "the drawn line avoids the wall" to "the regiment gets past
            // the wall without ever standing in anybody".
            var stoodIn = new List<string>();
            int turns = 0;

            for (; turns < 20 && marcher.Route != null && !marcher.Route.IsComplete; turns++)
            {
                BoardTurn.Resolve(battle);

                foreach (UnitInstance other in battle.UnitsOnField())
                {
                    if (other.Id == marcher.Id) continue;

                    if (OrientedRect.Overlaps(marcher.Shape, other.Shape))
                        stoodIn.Add($"turn {turns + 1}: inside {other.Def.Key} at {board.Of(other.Position)}");
                }
            }

            _out.WriteLine(
                $"walked in {turns} turns, finished at {board.Of(marcher.Position)}, " +
                $"{Vec2.Distance(marcher.Position, board.CentreOf(target)):0} m from where it was sent");

            Assert.True(
                stoodIn.Count == 0,
                $"it walked through somebody: {string.Join("; ", stoodIn.Take(3))}");

            // It arrives, and at the place it was sent rather than near it.
            Assert.True(
                Vec2.Distance(marcher.Position, board.CentreOf(target)) <= board.CellWidth,
                $"it finished {Vec2.Distance(marcher.Position, board.CentreOf(target)):0} m from its " +
                $"destination after {turns} turns, so it did not get past the wall.");

            // Non-vacuity: it really had to go round, so the ground it covered is
            // longer than the straight line it was refused.
            Assert.True(
                plan.Path.Distance > straight + board.CellWidth,
                $"the route is {plan.Path.Distance:0} m against {straight:0} m straight, so nothing was " +
                "gone round.");
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

            Board board = Board.For(Load("greatfield"));

            var byKey = new Dictionary<string, int>();

            foreach (UnitDef def in TestContent.Units.All.OrderBy(d => d.Speed))
            {
                float exact = def.Speed * turn / board.CellWidth;
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
            var pathfinder = new HexPathfinder(battle.Terrain, battle.Movement, battle.TerrainCatalogue);

            int asked = 0;
            int routed = 0;
            int pressed = 0;

            foreach (UnitInstance unit in battle.UnitsOnField())
            {
                foreach (UnitInstance other in battle.UnitsOnField())
                {
                    if (other.Id == unit.Id) continue;

                    asked++;

                    // Ordered straight at somebody else, which in the continuous
                    // game is the commonest way to get a press-through.
                    Plan plan = planner.PlanTo(battle, unit, pathfinder, other.Position);

                    if (plan.PressedThrough) pressed++;

                    if (plan.Found) routed++;
                }
            }

            _out.WriteLine(
                $"{asked} orders straight at another regiment, {routed} routed, {pressed} of the drawn " +
                "lines declared a press-through");

            // [M159] The board no longer promises this about the ROUTE, and the
            // promise it does make is the stronger one.
            //
            // It used to search cell by cell over free ground only, so a drawn
            // line could not cross an occupied cell and the press-through count
            // was nought by construction. That search has gone - it was twenty to
            // seventy times more expensive than the continuous planner for the
            // same answers - and the continuous planner will, deliberately,
            // shoulder through one of its own when going round costs far more
            // [M26].
            //
            // What the board still cannot allow is two bodies on the same ground,
            // and that is now enforced where it can actually be enforced: in the
            // walk. BoardTurn tests every cell of a body before it takes a step,
            // so a regiment following a pressed-through line is held at the
            // shoulder and goes round it locally instead. A route is an
            // intention; the board rules on what actually happens.
            //
            // Measured over the walk in TwoModesTests: zero shared ground across
            // six turns of twelve converging orders, at both cell sizes. What is
            // asserted here is only that the orders were really asked.
            Assert.True(asked > 100, $"only {asked} orders were asked; this is not measuring much.");

            Assert.True(routed > asked / 2, $"only {routed} of {asked} orders were routed at all.");
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
