using System;
using System.Collections.Generic;
using BattleChess.Contracts;

namespace BattleChess.Rules.GridPlanning
{
    /// <summary>What shape is reserved round every body.</summary>
    public enum HaloShape
    {
        /// <summary>
        /// The mover's circumscribed circle, which fits it at any facing and
        /// therefore needs to know none.
        /// </summary>
        Circle,

        /// <summary>
        /// The mover's own rectangle, squared to the line it is about to walk.
        /// </summary>
        /// <remarks>
        /// <c>M24</c>: every question a route asks is asked of the regiment
        /// squared to the line it is about to walk. Measured and refused - see
        /// M77 - because a rectangle is sized for a facing and the routes that
        /// need this grid are the ones that bend. Kept so it is not re-derived.
        /// </remarks>
        Rectangle,
    }

    /// <summary>What one cell of the grid is, once the bodies have been marked on it.</summary>
    public enum CellState : byte
    {
        /// <summary>The mover could stand here.</summary>
        Clear,

        /// <summary>Partly covered, but there is still free ground in it.</summary>
        Partial,

        /// <summary>A regiment is in the way.</summary>
        Body,

        /// <summary>The going here is impassable to this mover.</summary>
        Ground,
    }

    /// <summary>
    /// A hex grid at regiment scale that knows where the regiments are, and
    /// plain A* over it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The designer's proposal, and the one fact that makes it work.</b>
    /// A regiment collides as a <b>2:1 block</b> - <c>Formation.FootprintFor</c>
    /// with <c>BlockWidthToDepth = 2</c>, which is <c>S2</c>, and is not the
    /// 40 m by 6 m of ground the <c>units.cfg</c> header describes. That is
    /// <c>SpaceFor</c>: the men and their spacing, a different number for a
    /// different purpose. The bounding circle of a 2:1 rectangle is
    /// <b>1,118 times its long side</b>, and measured across three bench
    /// fields it is 1,12x for every unit type without exception.
    /// </para>
    /// <para>
    /// That twelve percent is what buys the whole thing: a cell that holds the
    /// circle holds the regiment however it is turned, so the heading dimension
    /// the lattice pays for disappears. Two dimensions instead of three, and a
    /// dictionary lookup per neighbour instead of a swept rectangle against
    /// every nearby body.
    /// </para>
    /// <para>
    /// This type is now a thin per-mover view onto a <see cref="SharedField"/>
    /// that is built once and kept: see there for why, and for how a mover
    /// subtracts its own body from a field that has everybody on it.
    /// </para>
    /// </remarks>
    public sealed class RegimentGrid
    {
        /// <summary>Cell spacing as a multiple of the mover's own bounding diameter.</summary>
        public static float SpacingMultiple = 1f;

        /// <summary>Room left round a body on top of the mover's own radius, in metres.</summary>
        public static float MarginMetres = 2f;

        /// <summary>
        /// How much of the mover's circumscribed radius is reserved round every
        /// body. 1 is the full circle; below that the halo shrinks toward the
        /// body's own half-depth.
        /// </summary>
        /// <remarks>
        /// <b>0,75 on the measurement.</b> Swept at 1,00 / 0,75 / 0,50 / 0,25 /
        /// 0,00, routes held were 42/45/34/35/33, 52/55/54/50/47 and
        /// 73/73/73/58/43. Shrinking the halo finds more routes and holds fewer,
        /// because <b>A* returns one route and not a menu</b>: an optimistic
        /// grid does not hand back a route that might work, it threads a gap the
        /// regiment cannot use and the gate refuses the whole thing.
        /// </remarks>
        public static float ClearanceFraction = 0.75f;

        /// <summary>Whether the halo is the mover's circle or its rectangle.</summary>
        public static HaloShape Halo = HaloShape.Circle;

        /// <summary>Sample points per cell: 1, 7 or 19.</summary>
        /// <remarks>
        /// <para>
        /// <b>The designer's second proposal.</b> A cell a body is five percent
        /// into should not be as blocked as one it fills. Sampling the cell at
        /// several points rather than only at its centre measures how much of it
        /// is really covered, and two things follow: a cell is refused only when
        /// enough of it is gone (<see cref="FillToBlock"/>), and a cell that
        /// keeps some free ground is <b>entered at that free ground rather than
        /// at its centre</b>.
        /// </para>
        /// <para>
        /// That second half is the important one, and it is what subdividing a
        /// cell would have been for. Splitting a hex into smaller hexes buys
        /// resolution near the edges of bodies, which is exactly where gaps are;
        /// moving the node to the free part of the cell buys the same
        /// resolution without a second graph to search, because what a finer
        /// cell would really have given the route is a place to stand that the
        /// coarse centre could not name.
        /// </para>
        /// </remarks>
        public static int SubSamples = 7;

        /// <summary>
        /// What fraction of a cell must be covered before it is refused
        /// outright. 1 refuses only cells with no free sample left.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>0,35 on the measurement, and it beats one sample on every
        /// field.</b> Swept at seven samples: 40 / 48 / <b>48</b> / 41 / 33
        /// routes held on the Crucible at 75 / 50 / <b>35</b> / 25 / 14
        /// percent, 46 / 51 / <b>57</b> / 50 / 47 on Broken Country and
        /// 71 / 74 / <b>76</b> / 73 / 70 on the Long March. Against 45, 55 and
        /// 73 for the single centre sample, that is <b>181 held against 173</b>
        /// and a win on all three rather than a net one.
        /// </para>
        /// <para>
        /// <b>Note the direction.</b> The proposal was to refuse a cell only
        /// once it was about three-quarters gone; the measurement puts the cut
        /// at a third. Seven samples do two things at once and they pull
        /// opposite ways - a better estimate of coverage than one point can
        /// give, which lets a cell a body barely clips stay open, and a
        /// stricter one for a cell whose centre happens to be free while its
        /// edges are not. A third is where those two balance.
        /// </para>
        /// <para>
        /// Nineteen samples buy nothing over seven - 49 held at half gone
        /// against 48, for half again the cost - so the ring is enough and the
        /// second ring is not.
        /// </para>
        /// </remarks>
        public static float FillToBlock = 0.35f;

        /// <summary>Whether a field once built is kept and shared between orders.</summary>
        public static bool Reuse = true;

        /// <summary>
        /// Whether a kept field whose arrangement has changed is patched rather
        /// than thrown away and raised again.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Why this is the largest saving available.</b> Marking the bodies
        /// was measured at <b>49,5%</b> of everything planning does on the
        /// Crucible once the field is rebuilt each order - 275 ms of 556, the
        /// largest single step on the board by a factor of three. And nearly
        /// all of it is re-answering a question whose answer has not changed:
        /// a tick moves the regiments that are marching and leaves the rest
        /// standing, so of eighty bodies perhaps a dozen are anywhere new, and
        /// the field is rebuilt for all eighty because the stamp is a hash over
        /// the whole army and says only <i>something</i> moved.
        /// </para>
        /// <para>
        /// <b>Why it is exact and not an approximation.</b> Coverage is counted
        /// rather than flagged - see <see cref="SharedField"/> - so marking is
        /// reversible: a body taken off with the rectangle it was put on with
        /// touches the same cells and the same samples and leaves the counts
        /// where a field that had never seen it would have them. The field
        /// remembers each rectangle for exactly this reason, since a body that
        /// has moved can no longer say where it used to stand.
        /// </para>
        /// <para>
        /// A lever rather than a rewrite, so the two can be put side by side on
        /// the same arrangement and shown to answer identically.
        /// </para>
        /// </remarks>
        public static bool MarkIncrementally = true;

        /// <summary>Bodies restamped on kept fields, on this thread.</summary>
        [ThreadStatic] public static int BodiesRestamped;

        /// <summary>
        /// Whether the call in flight is a finer tier than the ordinary one.
        /// </summary>
        /// <remarks>
        /// Measurement only, and per-thread because a wing is planned across
        /// several. It picks which line the field and the search book
        /// themselves to; nothing about the answer depends on it.
        /// </remarks>
        [ThreadStatic] internal static bool OnTheFineTier;

        /// <summary>Cells one search may settle before it gives up.</summary>
        internal static int CellBudget = 40_000;

        /// <summary>
        /// How far off the drawn line the search may wander, as a multiple of
        /// the length of that line. Nought lets it wander anywhere.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>What this is really for: the fine tier.</b> A* over open ground
        /// settles cells in a disc round the start until the heuristic pulls it
        /// to the goal, and the number of cells in a disc goes as the square of
        /// how fine they are. So the fine tier of M87 - a quarter of the coarse
        /// spacing, sixteen times the cells - spends its time settling ground
        /// hundreds of metres to either side of a march that was only ever
        /// going to bend round one regiment. Measured on the Crucible,
        /// <c>GridSearchFine</c> was 5,7 ms a call against
        /// <c>GridSearch</c>'s 0,9.
        /// </para>
        /// <para>
        /// <b>Why it is a bound on the search and not on the field.</b> The
        /// obvious version of this marks only the bodies near the corridor,
        /// which is wrong twice over: it would make the field a function of the
        /// order rather than of the arrangement, so no two regiments could
        /// share one and none could be patched between ticks - and a route is
        /// then only as safe as the guess about where it would go. Refusing the
        /// <i>cells</i> outside the corridor instead leaves the field exactly as
        /// it was, shared and patched, and makes the restriction honest: a
        /// route that stays inside the window was checked against every body,
        /// and one that wanted to leave it is simply not found, which the
        /// cascade already knows how to answer.
        /// </para>
        /// <para>
        /// <b>Off, on the measurement, and this is the interesting part.</b>
        /// Swept unbounded / x2 / x1 / x0,5 / x0,25 of the march over four
        /// fields, and the premise was simply wrong. A* with an admissible
        /// heuristic does <i>not</i> settle a disc round the start - the
        /// heuristic pulls it down the line, and at x2 and x1 the corridor
        /// refuses almost nothing, costing 4% for no saving at all. Below that
        /// it starts refusing cells the route needed, and then the cascade
        /// falls through to the stages the grid exists to avoid: on the
        /// Crucible at x0,5 an order goes from <b>2,1 ms to 13,1</b> - six times
        /// dearer for being given less to search - and on the Sideways Smile
        /// shoulder-throughs go from 1 to 11 and unwalkable routes from 2 to 12
        /// while the marching drops from 39 638 s to 28 953.
        /// <para>
        /// Which is M-W10 twice over: the cheaper number was the worse route,
        /// and then it was not even cheaper. Kept as a lever with its
        /// measurement rather than deleted, so the idea is not had again.
        /// </para>
        /// </para>
        /// </remarks>
        public static float CorridorFraction = 0f;

        /// <summary>The narrowest corridor, in cells, whatever the fraction says.</summary>
        public static float CorridorLeastCells = 6f;

        /// <summary>Cells refused for being outside the corridor, on this thread.</summary>
        [ThreadStatic] public static int CellsOutsideCorridor;

        /// <summary>
        /// Whether the route keeps the node of the cell it starts in and the
        /// node of the cell it ends in, rather than jumping straight from the
        /// regiment's own position to the second cell on the path.
        /// </summary>
        /// <remarks>
        /// <b>M90.</b> <c>Reconstruct</c> used to replace both end cells with
        /// the true start and destination, which reads as tidy and is a hole:
        /// the resulting first leg runs from wherever the regiment stands to a
        /// node <b>two cells away</b>, cutting the corner of the cell in
        /// between - and that corner is very often the body the route was drawn
        /// to avoid, because a regiment ordered to move is usually standing
        /// right beside one. The grid never checked that leg, because it is not
        /// a grid edge. It is what refused every grid route on six of the
        /// nineteen approach angles, at both tiers, at every ceiling.
        /// <para>
        /// Keeping the end nodes makes every consecutive pair of route points
        /// at most one cell apart, so the only hops the grid does not vouch for
        /// are the two sub-cell ones onto and off the path - which are
        /// unavoidable, since the regiment is where it is. Smoothing then takes
        /// the extra points back out wherever the sweep can see past them, so
        /// the cost is a pass over two more waypoints and not a longer route.
        /// </para>
        /// </remarks>
        internal static bool KeepEndCells = true;

        /// <summary>
        /// What a step into a cell held by a body costs, as a multiple of the
        /// same step over open ground. Nought refuses such cells outright,
        /// which is what the grid did before <b>M90</b>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A regiment ordered to move is usually standing <b>touching</b> the
        /// body it has been told to get round, so its own cell is held and so
        /// is every cell within the halo - which reaches
        /// <see cref="ClearanceFraction"/> of its circumscribed radius plus
        /// <see cref="MarginMetres"/>, twenty-one metres for a forty by twenty
        /// regiment. Refusing every held cell therefore walls the search in at
        /// the start and again at the destination, and it does so <b>worse the
        /// finer the cells are</b>: at a regiment's width one step of forty-five
        /// metres happens to clear the halo, at a quarter of it no chain of
        /// eleven-metre steps ever does, and the fine tier returns no route at
        /// all on exactly the arrangements it was added for.
        /// </para>
        /// <para>
        /// <b>M89</b> says the question is whether the body fits the space, so a
        /// regiment that legally stands somewhere must be able to leave it. A
        /// price rather than a refusal says that: the search crosses held ground
        /// only where there is nothing else, which is the escape and the
        /// approach, and never as a short cut, because at this multiple a single
        /// held step costs more than going round nearly always does.
        /// </para>
        /// </remarks>
        /// <remarks>
        /// <b>Sixty on the measurement, because that is where it stops
        /// mattering.</b> Swept at 0 / 8 / 25 / 40 / 60 / 80 / 120 / 200 on the
        /// four bench fields: at eight the search cuts <b>through</b> bodies for
        /// a short cut, the coarse grid holds not one route on the Crucible or
        /// Broken Country, the lattice runs eighteen times on each and the pass
        /// costs 285 and 254 ms - which is
        /// <see cref="ClearanceFraction"/>'s warning about an optimistic grid,
        /// exactly. From sixty upward <b>not one route moves</b>: 57 632,6 and
        /// 71 664,1 s of marching at 60, 80, 120 and 200 alike. So sixty is
        /// where held ground has stopped being a short cut and become only an
        /// escape, and paying more buys nothing. It also leaves the coarse grid
        /// answering <b>more</b> orders than refusal did - 25 and 26 against 23
        /// and 22 - so the fine tier is asked eleven and ten times rather than
        /// fifteen and eighteen, for 0,7% and 1,9% more marching.
        /// </remarks>
        internal static float BlockedStepPenalty = 60f;

        /// <summary>Cells settled by the last search on this thread.</summary>
        [ThreadStatic] public static int LastCellsExplored;

        /// <summary>Cells holding a body on the last grid built on this thread.</summary>
        [ThreadStatic] public static int LastBlockedCells;

        /// <summary>Waypoints in the last raw route, before any smoothing.</summary>
        [ThreadStatic] internal static int LastRawWaypoints;

        /// <summary>Shared fields found already built, on this thread.</summary>
        [ThreadStatic] public static int FieldsReused;

        /// <summary>Shared fields built from nothing, on this thread.</summary>
        [ThreadStatic] public static int FieldsBuilt;

        // One cache per thread: plans are worked out on several at once, and a
        // dictionary shared across them would need a lock on the hot path for
        // the sake of saving a rebuild that costs under a millisecond.
        [ThreadStatic] private static Dictionary<long, SharedField>? _fields;
        [ThreadStatic] private static long _fieldStamp;

        // The search's own books, kept between searches rather than built for
        // each. See CellTable: three managed collections allocated per order
        // were most of what GridExpand measured. Per thread for the same reason
        // the fields are.
        [ThreadStatic] private static CellTable? _cells;
        [ThreadStatic] private static CoordMinHeap? _open;

        private readonly SharedField _field;

        /// <summary>The mover's own coverage, to be taken off the shared field.</summary>
        private readonly Dictionary<Coord, byte[]> _mine;

        private RegimentGrid(SharedField field, Dictionary<Coord, byte[]> mine)
        {
            _field = field;
            _mine = mine;
        }

        /// <summary>The grid itself, for drawing and for turning cells back into ground.</summary>
        public HexLayout Layout => _field.Layout;

        /// <summary>Centre-to-centre spacing, in metres.</summary>
        public float Spacing => _field.Spacing;

        /// <summary>How many cells this mover cannot stand in.</summary>
        public int BlockedCells { get; private set; }

        /// <summary>Throws away every kept field, so the next order rebuilds.</summary>
        public static void Forget()
        {
            _fields?.Clear();
            _fieldStamp = 0;
        }

        /// <summary>
        /// Lays a grid over the field, or finds the one already laid, and
        /// returns the view of it that belongs to this mover.
        /// </summary>
        public static RegimentGrid For(
            BattleState battle, UnitInstance mover, Facing? travellingOn = null,
            float? spacingMultiple = null)
        {
            if (battle == null) throw new ArgumentNullException(nameof(battle));
            if (mover == null) throw new ArgumentNullException(nameof(mover));

            using var _profile = PlanningProfile.Measure(
                OnTheFineTier ? PlanningProfile.Step.GridFieldFine : PlanningProfile.Step.GridField);

            Footprint print = mover.Shape.Footprint;
            float fraction = Math.Clamp(ClearanceFraction, 0f, 1f);
            float reach =
                print.HalfDepth + (print.BoundingRadius - print.HalfDepth) * fraction +
                MathF.Max(0f, MarginMetres);

            // Named by the caller for the fine tier of M87, and the static
            // otherwise. The field cache is keyed by the spacing, so the two
            // tiers keep separate grids and neither evicts the other.
            float spacing = MathF.Max(
                1f, print.BoundingRadius * 2f * (spacingMultiple ?? SpacingMultiple));
            int samples = SubSamples >= 19 ? 19 : SubSamples >= 7 ? 7 : 1;

            OrientedRect? moving =
                Halo == HaloShape.Rectangle && travellingOn.HasValue
                    ? new OrientedRect(mover.Position, travellingOn.Value, print)
                    : (OrientedRect?)null;

            SharedField field = FieldFor(battle, mover, spacing, samples, reach, moving);

            // The mover's own body, worked out fresh. One rectangle over a few
            // dozen cells is nothing beside the field it is subtracted from,
            // and keeping it per unit would be a cache of a cache.
            var mine = new Dictionary<Coord, byte[]>();
            (float across, float along) = ReachOn(mover.Shape, reach, moving);
            field.Mark(mover.Shape, across, along, +1, mine);

            var grid = new RegimentGrid(field, mine);
            grid.CountBlocked();

            LastBlockedCells = grid.BlockedCells;
            return grid;
        }

        /// <summary>How far the mover reaches on a body's two axes.</summary>
        private static (float Across, float Along) ReachOn(
            in OrientedRect body, float reach, OrientedRect? moving)
        {
            if (!moving.HasValue) return (reach, reach);

            float margin = MathF.Max(0f, MarginMetres);

            return (moving.Value.ProjectedRadius(body.Right) + margin,
                    moving.Value.ProjectedRadius(body.Forward) + margin);
        }

        /// <summary>
        /// The shared field for this footprint and this arrangement of bodies,
        /// built if nobody has built it.
        /// </summary>
        private static SharedField FieldFor(
            BattleState battle, UnitInstance mover, float spacing, int samples, float reach,
            OrientedRect? moving)
        {
            long stamp;

            using (PlanningProfile.Measure(PlanningProfile.Step.FieldStamp))
                stamp = StampOf(battle);

            _fields ??= new Dictionary<long, SharedField>();

            // Without patching, a move throws every field away, which is what
            // this did and what made marking half the bill. With it, each field
            // carries its own stamp and is brought up to date when it is asked
            // for - so a field nobody asks about costs nothing to keep stale.
            if (!Reuse || (!MarkIncrementally && stamp != _fieldStamp))
            {
                _fields.Clear();
            }

            _fieldStamp = stamp;

            // A rectangle halo keys on the facing as well, so a long battle
            // could otherwise accumulate a field per degree. Nothing here is
            // worth a leak.
            if (_fields.Count > 32) _fields.Clear();

            // A rectangle halo differs per order rather than per footprint, so
            // a field built with one cannot answer for another. Keyed on the
            // facing as well, which in practice means it is rebuilt each time -
            // one more reason M77 refused it.
            long key = (long)MathF.Round(spacing * 16f) * 1_000_003L
                     + samples * 7919L
                     + (long)mover.Def.Movement * 131L
                     + (long)MathF.Round(reach * 16f) * 17L
                     + (moving.HasValue ? (long)MathF.Round(moving.Value.Facing.Degrees) : -1L);

            // A different battle, or the same battle on different ground, and
            // every field kept here answers for somewhere else. Checked before
            // the stamp, because patching a field onto the wrong map is exactly
            // the failure this guards - see SharedField.RaisedOver.
            if (_fields.Count > 0)
            {
                foreach (SharedField any in _fields.Values)
                {
                    if (any.RaisedOver(battle.Terrain, battle.Movement)) break;

                    _fields.Clear();
                    break;
                }
            }

            if (_fields.TryGetValue(key, out SharedField found))
            {
                if (found.Stamp != stamp)
                {
                    using (PlanningProfile.Measure(PlanningProfile.Step.FieldPatch))
                        Restamp(found, battle, reach, moving);

                    found.Stamp = stamp;
                }

                FieldsReused++;
                return found;
            }

            float fastest = 0f;

            foreach (TerrainDef def in battle.TerrainCatalogue.All)
                fastest = MathF.Max(fastest, battle.Movement.SpeedMultiplier(def.Id, mover.Def.Movement));

            if (fastest <= 0f) fastest = 1f;

            var field = new SharedField(
                HexLayout.FromNeighbourDistance(spacing, battle.Terrain.Bounds.Min),
                battle.Terrain, battle.Movement, mover.Def.Movement, spacing, fastest, samples);

            // Everybody, including the mover. Whoever asks takes themselves off
            // again, which is what lets one field answer for all of them.
            using (PlanningProfile.Measure(PlanningProfile.Step.FieldMark))
            {
                foreach (UnitInstance body in battle.UnitsOnField())
                {
                    (float across, float along) = ReachOn(body.Shape, reach, moving);
                    field.Add(body.Id, body.Shape, across, along);
                }
            }

            field.Stamp = stamp;

            _fields[key] = field;
            FieldsBuilt++;
            return field;
        }

        /// <summary>
        /// Brings a kept field up to date by restamping only what has moved.
        /// </summary>
        /// <remarks>
        /// The walk over the army is the cheap half and is not avoidable: the
        /// stamp says that something moved and never which, so somebody has to
        /// ask each body whether it is where the field thinks. That is a
        /// handful of float comparisons a body against the several hundred
        /// cells and several thousand sample tests that marking one costs.
        /// </remarks>
        private static void Restamp(
            SharedField field, BattleState battle, float reach, OrientedRect? moving)
        {
            HashSet<UnitId> standing = _standing ??= new HashSet<UnitId>();
            standing.Clear();

            int was = field.Restamped;

            foreach (UnitInstance body in battle.UnitsOnField())
            {
                standing.Add(body.Id);

                (float across, float along) = ReachOn(body.Shape, reach, moving);

                if (field.Marked.TryGetValue(body.Id, out SharedField.MarkedBody had) &&
                    had.Matches(body.Shape, across, along))
                    continue;

                using (PlanningProfile.Measure(PlanningProfile.Step.FieldRestamp))
                {
                    field.Remove(body.Id);
                    field.Add(body.Id, body.Shape, across, along);
                }
            }

            // The dead and the routed, who are marked and are no longer there.
            // Collected first: taking one off edits the book being walked.
            List<UnitId>? gone = null;

            foreach (UnitId who in field.Marked.Keys)
                if (!standing.Contains(who))
                    (gone ??= new List<UnitId>()).Add(who);

            if (gone != null)
                for (int i = 0; i < gone.Count; i++) field.Remove(gone[i]);

            BodiesRestamped += field.Restamped - was;
        }

        [ThreadStatic] private static HashSet<UnitId>? _standing;

        /// <summary>
        /// A number that changes whenever anything has moved, so a field built
        /// before it moved is simply not found.
        /// </summary>
        /// <remarks>
        /// Staleness cannot be a matter of discipline. A kept field that has
        /// gone out of date is a route through a regiment that is no longer
        /// there, which is the class of bug this project has spent four
        /// attempts on; a hash over eighty units costs microseconds against the
        /// milliseconds it saves.
        /// </remarks>
        private static long StampOf(BattleState battle)
        {
            long stamp = 17;

            foreach (UnitInstance unit in battle.UnitsOnField())
            {
                unchecked
                {
                    stamp = stamp * 31 + (long)MathF.Round(unit.Position.X * 8f);
                    stamp = stamp * 31 + (long)MathF.Round(unit.Position.Y * 8f);
                    stamp = stamp * 31 + (long)MathF.Round(unit.Facing.Degrees);
                    stamp = stamp * 31 + unit.Shape.Footprint.Width.GetHashCode();
                }
            }

            return stamp;
        }

        /// <summary>How much of a cell is covered, for this mover, from 0 to 1.</summary>
        public float FillAt(Coord cell)
        {
            byte[]? all = _field.CoverAt(cell);
            if (all == null) return 0f;

            _mine.TryGetValue(cell, out byte[] mine);

            int covered = 0;

            for (int i = 0; i < all.Length; i++)
            {
                int count = all[i] - (mine != null ? mine[i] : 0);
                if (count > 0) covered++;
            }

            return covered / (float)all.Length;
        }

        /// <summary>Whether this mover is refused this cell.</summary>
        public bool IsBlocked(Coord cell)
        {
            float fill = FillAt(cell);

            // At one sample there is nothing to be partial about, so any
            // coverage at all is the whole cell - which is what this did before
            // sampling existed, and is still what it does at SubSamples = 1.
            return fill > 0f && fill >= MathF.Min(FillToBlock, 1f);
        }

        /// <summary>
        /// Where in a cell the regiment actually stands: the middle of whatever
        /// of it is free, or the cell's centre when all of it is.
        /// </summary>
        /// <remarks>
        /// This is the half of sampling that earns its keep. A route through a
        /// half-covered cell that aims at the cell's centre aims at ground
        /// inside a regiment; aiming at the free half is what subdividing the
        /// cell would have bought, without a second graph to search.
        /// </remarks>
        public Vec2 NodeAt(Coord cell)
        {
            byte[]? all = _field.CoverAt(cell);
            if (all == null || all.Length <= 1) return _field.Layout.ToWorld(cell);

            _mine.TryGetValue(cell, out byte[] mine);

            float x = 0f, y = 0f;
            int free = 0;

            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] - (mine != null ? mine[i] : 0) > 0) continue;

                Vec2 at = _field.SampleAt(cell, i);
                x += at.X;
                y += at.Y;
                free++;
            }

            return free == 0 ? _field.Layout.ToWorld(cell) : new Vec2(x / free, y / free);
        }

        /// <summary>What a cell is, for drawing.</summary>
        public CellState StateOf(Coord cell)
        {
            if (IsBlocked(cell)) return CellState.Body;
            if (_field.GoingAt(cell) <= 0f) return CellState.Ground;

            return FillAt(cell) > 0f ? CellState.Partial : CellState.Clear;
        }

        private void CountBlocked()
        {
            int blocked = 0;

            foreach (Coord cell in _field.TouchedCells())
                if (IsBlocked(cell))
                    blocked++;

            BlockedCells = blocked;
        }

        /// <summary>Every cell of the field, for drawing the whole grid at once.</summary>
        public void Snapshot(List<Coord> into) =>
            Snapshot(into, _field.Bounds.Centre, float.PositiveInfinity);

        /// <summary>
        /// The cells within <paramref name="radiusMetres"/> of a point, for
        /// drawing a window rather than a whole field.
        /// </summary>
        public void Snapshot(List<Coord> into, Vec2 around, float radiusMetres)
        {
            if (into == null) throw new ArgumentNullException(nameof(into));

            into.Clear();

            MapBounds bounds = _field.Bounds;

            float minX = bounds.Min.X, maxX = bounds.Max.X;
            float minY = bounds.Min.Y, maxY = bounds.Max.Y;

            if (!float.IsInfinity(radiusMetres))
            {
                minX = MathF.Max(minX, around.X - radiusMetres);
                maxX = MathF.Min(maxX, around.X + radiusMetres);
                minY = MathF.Max(minY, around.Y - radiusMetres);
                maxY = MathF.Min(maxY, around.Y + radiusMetres);
            }

            var seen = new HashSet<Coord>();
            float step = _field.Spacing * 0.45f;

            for (float y = minY; y <= maxY + step; y += step)
            for (float x = minX; x <= maxX + step; x += step)
            {
                Coord cell = _field.Layout.ToCoord(new Vec2(x, y));
                if (seen.Add(cell)) into.Add(cell);
            }
        }

        /// <summary>
        /// A* from one point to another over the cells, returning the ground
        /// each cell is entered at with the true ends substituted in.
        /// </summary>
        /// <remarks>
        /// The start and goal cells are always enterable however they are
        /// marked. A regiment about to move is nearly always standing inside
        /// some other body's margin - that is what being in a line means - and
        /// refusing its own cell would refuse every order given in contact.
        /// </remarks>
        public bool TryRoute(Vec2 from, Vec2 to, out List<Vec2> waypoints)
        {
            using var _profile = PlanningProfile.Measure(
                OnTheFineTier ? PlanningProfile.Step.GridSearchFine : PlanningProfile.Step.GridSearch);

            waypoints = null!;
            LastCellsExplored = 0;
            LastRawWaypoints = 0;

            HexLayout layout = _field.Layout;
            Coord start = layout.ToCoord(from);
            Coord goal = layout.ToCoord(to);

            if (start == goal)
            {
                waypoints = new List<Vec2> { from, to };
                LastRawWaypoints = 2;
                return true;
            }

            // The corridor this search is allowed to wander in. Squared, and
            // the ends measured against the same segment the cells are, so the
            // start and the goal are inside it by construction.
            Vec2 travel = to - from;
            float span = travel.Length;
            Vec2 heading = span > 0f ? travel / span : new Vec2(1f, 0f);

            float corridor = CorridorFraction > 0f
                ? MathF.Max(span * CorridorFraction, _field.Spacing * CorridorLeastCells)
                : float.MaxValue;

            float corridorSquared = corridor >= float.MaxValue ? float.MaxValue : corridor * corridor;

            CellTable cells = _cells ??= new CellTable();
            CoordMinHeap open = _open ??= new CoordMinHeap();

            cells.Open();
            open.Clear();

            cells.Reached(start, 0f, start);
            open.Push(start, Heuristic(start, goal));

            int explored = 0;
            Span<Coord> neighbours = stackalloc Coord[HexMath.DirectionCount];

            using var _expanding = PlanningProfile.Measure(PlanningProfile.Step.GridExpand);

            while (open.TryPop(out Coord current))
            {
                if (!cells.Settle(current)) continue;

                explored++;

                // Falls out of the loop and reports no route, which is exactly
                // what an exhausted open set does - so the cascade goes on to
                // the stage it would have gone on to anyway.
                if ((explored & 63) == 0 && Marching.StopNow()) break;

                if (current == goal)
                {
                    LastCellsExplored = explored;
                    using (PlanningProfile.Measure(PlanningProfile.Step.GridPull))
                        waypoints = Reconstruct(cells, start, goal, from, to);
                    LastRawWaypoints = waypoints.Count;
                    return true;
                }

                if (explored >= CellBudget) break;

                cells.TryCost(current, out float cost);
                HexMath.Neighbours(current, neighbours);

                for (int i = 0; i < neighbours.Length; i++)
                {
                    Coord next = neighbours[i];
                    if (cells.IsSettled(next)) continue;

                    if (next != goal &&
                        corridorSquared < float.MaxValue &&
                        OffTheLine(layout.ToWorld(next), from, heading, span) > corridorSquared)
                    {
                        CellsOutsideCorridor++;
                        continue;
                    }

                    // The goal cell is enterable whatever is marked on it, so
                    // it also needs a going the arithmetic can divide by.
                    float going = next == goal
                        ? MathF.Max(_field.GoingAt(next), 0.01f)
                        : _field.GoingAt(next);

                    if (going <= 0f) continue;

                    // Held ground is priced rather than refused - M90. The goal
                    // cell is enterable whatever is on it, as it always was.
                    float penalty = 1f;

                    if (next != goal && IsBlocked(next))
                    {
                        if (BlockedStepPenalty <= 0f) continue;
                        penalty = BlockedStepPenalty;
                    }

                    float tentative = cost + _field.Spacing / going * penalty;

                    if (cells.TryCost(next, out float known) && tentative >= known) continue;

                    cells.Reached(next, tentative, current);
                    open.Push(next, tentative + Heuristic(next, goal));
                }
            }

            LastCellsExplored = explored;
            return false;
        }

        /// <summary>How far a cell's centre lies off the drawn line, squared.</summary>
        private static float OffTheLine(Vec2 at, Vec2 from, Vec2 heading, float length)
        {
            Vec2 offset = at - from;

            float onto = offset.X * heading.X + offset.Y * heading.Y;

            if (onto < 0f) onto = 0f;
            else if (onto > length) onto = length;

            float dx = offset.X - heading.X * onto;
            float dy = offset.Y - heading.Y * onto;

            return dx * dx + dy * dy;
        }

        /// <summary>
        /// Nudges the search toward the goal so that, among the enormous number
        /// of routes that tie on open ground, it prefers the direct one.
        /// </summary>
        private float Heuristic(Coord cell, Coord goal) =>
            Coord.Distance(cell, goal) * _field.Spacing / _field.Fastest * 1.001f;

        private List<Vec2> Reconstruct(
            CellTable reached, Coord start, Coord goal, Vec2 from, Vec2 to)
        {
            var cells = new List<Coord>();

            for (Coord at = goal; at != start; at = reached.CameFrom(at)) cells.Add(at);

            cells.Add(start);
            cells.Reverse();

            // The true ends, and in between, the free part of each cell rather
            // than its middle. Whether the two end cells contribute a node of
            // their own is M90's question - see KeepEndCells.
            var points = new List<Vec2>(cells.Count + 2) { from };

            int first = KeepEndCells ? 0 : 1;
            int last = KeepEndCells ? cells.Count - 1 : cells.Count - 2;

            for (int i = first; i <= last; i++)
            {
                Vec2 node = NodeAt(cells[i]);

                // A node that lands on the point already there is not a leg,
                // and a zero-length leg has no front to hold.
                if (Vec2.Distance(points[points.Count - 1], node) > 0.01f) points.Add(node);
            }

            if (Vec2.Distance(points[points.Count - 1], to) > 0.01f) points.Add(to);

            return points;
        }
    }
}
