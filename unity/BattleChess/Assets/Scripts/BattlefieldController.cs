using System.Collections.Generic;
using System.IO;
using BattleChess.Contracts;
using BattleChess.Rules;
using BattleChess.Rules.GridPlanning;
using UnityEngine;

namespace BattleChess.Unity
{
    /// <summary>
    /// Loads a battle from content and puts it on screen, with click-to-select,
    /// click-to-plan-a-route, a debug console and hand-testing toggles.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The hands-on harness. It runs the same rules assembly the command-line
    /// tool does, against the same text files, so anything seen here is the real
    /// simulation rather than a Unity-side reimplementation of it.
    /// </para>
    /// <para>
    /// This references <c>BattleChess.Rules</c>, which the eventual networked
    /// client must not. That is correct for now — offline play makes this
    /// machine the host, and the host does run the rules. The split into a view
    /// that sees only fogged data arrives with <c>IMatchAuthority</c> in M5.
    /// </para>
    /// </remarks>
    public sealed class BattlefieldController : MonoBehaviour
    {
        private static readonly Color[] ArmyColours =
        {
            new Color(0.35f, 0.70f, 0.95f),
            new Color(0.92f, 0.42f, 0.38f),
            new Color(0.80f, 0.55f, 0.90f),
            new Color(0.95f, 0.82f, 0.35f)
        };

        /// <summary>
        /// Depth a regiment is drawn and hit-tested at, however thin it really
        /// is. Cosmetic only — the rules always use the true footprint.
        /// </summary>
        /// <remarks>
        /// Kept only as a floor for anything that somehow ends up narrower than
        /// it should be. It is no longer doing any work in the ordinary case:
        /// the rules hand over a block already two to one, and a forty-metre
        /// regiment is twenty metres deep.
        /// </remarks>
        public const float ClickableDepthMetres = 9f;

        /// <summary>The depth a regiment is drawn and hit-tested at, in metres.</summary>
        /// <remarks>
        /// <para>
        /// Now barely a function. The two-to-one shape moved into the rules,
        /// where <c>Formation.BlockWidthToDepth</c> owns it, because the drawn
        /// rectangle <b>is</b> the collider — what you see is what blocks, what
        /// is clicked and what holds ground.
        /// </para>
        /// <para>
        /// It used to be decided here, and that was the bug. A regiment was
        /// drawn twenty metres deep and collided at six, so two lines that
        /// looked flush had fourteen metres of open ground between them, a
        /// regiment could sit visually inside another one without either
        /// noticing, and every flank was a six-metre sliver the eye said was
        /// twenty. Three separate rules were reading a shape the player could
        /// not see.
        /// </para>
        /// </remarks>
        public static float DrawnDepthOf(Footprint footprint) =>
            Mathf.Max(footprint.Depth, ClickableDepthMetres);

        [Tooltip("Battle file to load from content/battles, without the extension.")]
        public string BattleName = "ford";

        [Tooltip("Routes the tick may work out in one frame before the rest wait their turn.")]
        public int RoutesPerFrame = PlanningBudget.DefaultRoutesPerFrame;

        [Tooltip("Milliseconds one frame may spend working out routes before the rest wait their turn.")]
        public float MillisecondsPerFrame = PlanningBudget.DefaultMillisecondsPerFrame;

        [Tooltip("Milliseconds one plan may search before it settles for the ladder's answer. " +
                 "Zero is no ceiling.")]
        /// <summary>
        /// What a single order may spend searching, here rather than in the rules.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Set by the host that draws frames, exactly as
        /// <see cref="PlanningBudget"/> is, and for the same reason: the rules
        /// default to no ceiling so the benches, the CLI and the whole test
        /// suite keep giving the answer they always gave. A budget makes a
        /// route depend on how fast the machine was, and a bench that cannot
        /// reproduce its own numbers is not a bench.
        /// </para>
        /// <para>
        /// <b>Five, and it is a gameplay setting rather than a performance one.</b>
        /// At ten it never fired on the bench at all - the dearest order there
        /// is five or six milliseconds - so it was a ceiling over an empty
        /// room. At five it binds: [M122] measured the worst order at exactly
        /// 5,0 ms on both crowded fields, against 6 to 18 ms uncapped, and the
        /// bound held across repeats where the uncapped tail swung by a factor
        /// of two.
        /// </para>
        /// <para>
        /// <b>What it costs is one route in eighty.</b> Of 80 orders on the
        /// crucible the cap turns one into a declared press-through and leaves
        /// one the executor will not walk. That price is not linear: at 2 ms it
        /// is four of eighty and at half a millisecond it is nine. In a played
        /// session under Mono, with a wing planned across twelve threads, the
        /// dearest order measured 287 ms - so it fires far more often there
        /// than here, and the four-in-eighty row is the better guide to what a
        /// player sees than the one-in-eighty row measured on CoreCLR.
        /// </para>
        /// </remarks>
        public float SearchBudgetMs = 5f;

        private BattleState _battle;
        private BattleMapDefinition _map;
        private TerrainCatalogue _terrainCatalogue;
        private FormationCatalogue _formations;
        private IMovementModel _trueMovement;

        private CameraRig _camera;
        private LineRenderer _pathLine;
        private DebugOverlay _overlay;

        private readonly DebugOptions _options = new DebugOptions();
        private readonly DebugConsole _console = new DebugConsole();

        /// <summary>
        /// What the simulation actually writes to: the console, with repeated
        /// lines collapsed into the stretch of time they covered.
        /// </summary>
        /// <remarks>
        /// Wrapped here rather than inside the console so that the console stays
        /// a plain sink and the collapsing can be reasoned about — and tested —
        /// on its own. Everything the player types goes to <c>_console</c>
        /// directly, because a line the player caused is an event by definition
        /// and should never be swallowed as a repeat.
        /// </remarks>
        /// <remarks>
        /// Built in <c>Start</c>. Not a field initializer, which cannot
        /// reference another instance field, and not a constructor: Unity builds
        /// a MonoBehaviour through its parameterless constructor during
        /// deserialization, off the main thread, and this class had none before.
        /// </remarks>
        private SteadyStateLog _quietened;

        private BattleClock _clock;
        private float _tickAccumulator;

        /// <summary>Orders drawn this turn and not yet given ([M143]).</summary>
        private readonly TurnOrders _book = new TurnOrders();

        /// <summary>Ticks still to run of the turn that was set going, or nought while planning.</summary>
        private int _resolving;

        private readonly List<UnitView> _views = new List<UnitView>();

        /// <summary>
        /// Every regiment currently under command, in the order they were
        /// picked up.
        /// </summary>
        /// <remarks>
        /// A list rather than one unit because an army is commanded in wings,
        /// not one regiment at a time. Ordering six regiments into a line was
        /// six selections and six orders, which is micromanagement rather than
        /// command — and it is the thing that most made this feel like a debug
        /// harness instead of a game.
        /// </remarks>
        private readonly List<UnitInstance> _selection = new List<UnitInstance>();

        /// <summary>
        /// The regiment the detail panels talk about — the first one picked.
        /// </summary>
        private UnitInstance Primary => _selection.Count > 0 ? _selection[0] : null;

        private Vec2 _lastDestination;
        private bool _hasDestination;
        private string _status = string.Empty;
        private string _error;
        private GUIStyle _nameplate;
        private GUIStyle _ghostPlate;

        private void Start()
        {
            _quietened = new SteadyStateLog(_console);

            // Recording is on from the first tick, not from whenever somebody
            // remembers to press the button. Every fault in this project has
            // been found in a recording and several were found late because the
            // session that showed them was not being written down — a log that
            // has to be asked for is a log that is missing exactly when it is
            // wanted. The button now stops it rather than starting it.
            _console.StartRecording(DebugConsole.DefaultLogDirectory());

            try
            {
                LoadBattle();
            }
            catch (System.Exception failure)
            {
                _error = failure.Message;
                Debug.LogException(failure);
            }
        }

        private void LoadBattle()
        {
            _terrainCatalogue = TerrainCatalogueReader.Read(File.ReadAllText(UnityContentLocator.TerrainFile()));
            UnitCatalogue units = UnitCatalogueReader.Read(File.ReadAllText(UnityContentLocator.UnitsFile()));
            _formations = FormationCatalogueReader.Read(File.ReadAllText(UnityContentLocator.FormationsFile()));

            BattleSetup setup = BattleSetup.Parse(File.ReadAllText(UnityContentLocator.BattleFile(BattleName)));
            _map = AsciiMapReader.Read(File.ReadAllText(UnityContentLocator.MapFile(setup.MapName)), _terrainCatalogue);

            _trueMovement = new TerrainMovementModel(_terrainCatalogue);
            _battle = setup.Build(_map, _terrainCatalogue, units, _formations, _trueMovement);

            TerrainView.Build(_map, _terrainCatalogue, transform);

            foreach (UnitInstance unit in _battle.UnitsOnField())
                _views.Add(UnitView.Create(unit, ColourFor(unit.Owner), transform));

            _camera = Camera.main != null ? Camera.main.GetComponent<CameraRig>() : null;
            if (_camera != null)
                _camera.FrameOn(_map.Terrain.Bounds);

            _overlay = gameObject.AddComponent<DebugOverlay>();
            _overlay.Options = _options;

            // Orders decide, contact interrupts, movement carries out — in that
            // order, so a decision takes effect on the tick it is made rather
            // than a tick late.
            var pathfinder = new DirectPathfinder(_map.Terrain, _trueMovement, _terrainCatalogue);

            _clock = new BattleClock()
                .Add(new VisionSystem())
                .Add(new OrderSystem(pathfinder))
                .Add(new ContactSystem())
                .Add(new MovementSystem())
                .Add(new RangedCombatSystem())
                .Add(new CombatSystem())
                .Add(new MoraleSystem());

            BuildPathLine();
            ReportSetup();

            _status = $"{_battle.Name} — left-click a regiment, or drag a box round several.";
        }

        /// <summary>
        /// Walks the console through what was loaded and anything questionable
        /// about it, so the first thing on screen already answers "what am I
        /// looking at".
        /// </summary>
        private void ReportSetup()
        {
            _console.Info("Battle", $"Loaded '{_battle.Name}' on '{_map.Name}', seed {_battle.Seed}.");
            _console.Info("Battle",
                $"Map {_map.Terrain.Columns}x{_map.Terrain.Rows} cells at {_map.Terrain.CellSize:0} m " +
                $"= {_map.Terrain.Bounds.Width:0} x {_map.Terrain.Bounds.Height:0} m.");

            foreach (Army army in _battle.Armies)
            {
                int men = _battle.StrengthOf(army.Player);
                int count = 0;
                foreach (UnitInstance _ in _battle.UnitsOf(army.Player)) count++;

                _console.Info("Battle", $"{army.Name}: {count} regiments, {men} men.");
            }

            foreach (UnitInstance unit in _battle.UnitsOnField())
            {
                TerrainDef ground = _battle.TerrainAt(unit.Position);
                float multiplier = _trueMovement.SpeedMultiplier(_battle.Terrain.At(unit.Position), unit.Def.Movement);

                if (multiplier <= 0f)
                {
                    _console.Blocked("Deploy",
                        $"{unit.Def.DisplayName} is stranded on {ground.DisplayName}, which {unit.Def.Movement} cannot cross.",
                        unit.Id);
                }
                else if (multiplier < 0.5f)
                {
                    _console.Decision("Deploy",
                        $"{unit.Def.DisplayName} starts in {ground.DisplayName} at {multiplier:0%} speed.", unit.Id);
                }
            }
        }

        private void BuildPathLine()
        {
            var go = new GameObject("Planned route");
            go.transform.SetParent(transform, worldPositionStays: false);

            _pathLine = go.AddComponent<LineRenderer>();
            _pathLine.material = new Material(Shader.Find("Sprites/Default"));
            _pathLine.startColor = _pathLine.endColor = new Color(1f, 0.9f, 0.3f);
            _pathLine.startWidth = _pathLine.endWidth = 6f;
            _pathLine.useWorldSpace = true;
            _pathLine.sortingOrder = 20;
            _pathLine.positionCount = 0;
        }

        /// <summary>
        /// A frame slower than this is worth a line in the recording.
        /// </summary>
        /// <remarks>
        /// Two frames' worth at sixty. Below that nobody feels anything; above
        /// it, something stopped for long enough to see.
        /// </remarks>
        private const float ASlowFrameMs = FrameRate.SlowMs;

        private readonly System.Diagnostics.Stopwatch _frame = new System.Diagnostics.Stopwatch();

        /// <summary>
        /// The on-screen counter, fed from the same stopwatch as the line above.
        /// </summary>
        /// <remarks>
        /// Deliberately the same source. A counter reading its own clock would
        /// be a second answer to "what did that frame cost", and the first time
        /// the two disagreed the investigation would be about the counter.
        /// </remarks>
        private readonly FrameRate _frameRate = new FrameRate();

        private int _collectionsLastFrame;
        private long _heapLastFrame;

        // Where a frame goes. Fifty-two of sixty-eight slow frames ran no
        // collection at all, so the litter was real and was not the whole
        // story — and a frame that only knows its own total cannot say which
        // part of itself is slow (W5).
        private readonly System.Diagnostics.Stopwatch _simClock = new System.Diagnostics.Stopwatch();
        private readonly System.Diagnostics.Stopwatch _viewClock = new System.Diagnostics.Stopwatch();
        private readonly System.Diagnostics.Stopwatch _trackClock = new System.Diagnostics.Stopwatch();
        private readonly System.Diagnostics.Stopwatch _guiClock = new System.Diagnostics.Stopwatch();

        private int _ticksThisFrame;

        // Where the battle's own planning clock stood at the top of the last
        // frame, so the frame line can report the difference rather than the
        // running total.
        private long _planningTicksLastFrame;
        private int _routesPlannedLastFrame;

        // The same splits, named for the Unity profiler, so a capture and the
        // frame line in the console answer the same question the same way. A
        // profiler that only knows Update() cannot say which part of a frame
        // is slow, which is the whole reason the console line exists — but a
        // capture is what gets taken when something is felt rather than
        // reproduced, so both have to work.
        private static readonly global::Unity.Profiling.ProfilerMarker SimMarker =
            new global::Unity.Profiling.ProfilerMarker("BattleChess.Simulation");

        private static readonly global::Unity.Profiling.ProfilerMarker ViewMarker =
            new global::Unity.Profiling.ProfilerMarker("BattleChess.Views");

        private static readonly global::Unity.Profiling.ProfilerMarker TrackMarker =
            new global::Unity.Profiling.ProfilerMarker("BattleChess.Tracking");

        private static readonly global::Unity.Profiling.ProfilerMarker GuiMarker =
            new global::Unity.Profiling.ProfilerMarker("BattleChess.Interface");

        private static readonly global::Unity.Profiling.ProfilerMarker PlanMarker =
            new global::Unity.Profiling.ProfilerMarker("BattleChess.PlanRoute");

        private void Update()
        {
            if (_battle == null) return;

            // What the last frame cost, reported at the top of this one, because
            // a frame cannot time itself: the drawing that makes it slow happens
            // after Update returns. So the clock runs across the whole frame and
            // is read at the next.
            if (_frame.IsRunning)
            {
                _frame.Stop();

                float ms = (float)_frame.Elapsed.TotalMilliseconds;

                // Every frame, not only the slow ones - an average over the
                // frames that were bad enough to log is not a frame rate. The
                // same four clocks the slow-frame line below reads, and read at
                // the same point in the frame, so the counter and the recording
                // cannot disagree about where a frame went either.
                _frameRate.Record(
                    ms,
                    (float)_simClock.Elapsed.TotalMilliseconds,
                    (float)_viewClock.Elapsed.TotalMilliseconds,
                    (float)_trackClock.Elapsed.TotalMilliseconds,
                    (float)_guiClock.Elapsed.TotalMilliseconds);

                if (_options.Harness && ms >= ASlowFrameMs)
                {
                    int marching = 0;
                    int onField = 0;

                    foreach (UnitInstance unit in _battle.UnitsOnField())
                    {
                        onField++;
                        if (unit.IsMarching) marching++;
                    }

                    // The two numbers together are the whole question: a slow
                    // frame with nobody marching is drawing, and one that only
                    // arrives when a wing is on the move is the movement.
                    // <b>Whether it collected.</b> Measured on the bench, one
                    // route plan allocates 205 kB against a whole simulation
                    // tick's 1,1 — so the frames that stop are far more likely
                    // to be the collector running than anything the battle did,
                    // and a frame line that cannot say which sends the next
                    // investigation the same way this one went.
                    int collections = System.GC.CollectionCount(0);
                    long heap = System.GC.GetTotalMemory(forceFullCollection: false);

                    // Planning is inside sim, not beside it, so it is
                    // reported as a share of that rather than as another
                    // column that would not add up.
                    double planningMs =
                        (_battle.RoutePlanningTicks - _planningTicksLastFrame)
                        * 1000.0 / System.Diagnostics.Stopwatch.Frequency;

                    int planned = _battle.RoutesPlanned - _routesPlannedLastFrame;

                    _console.Info("Frame",
                        $"{ms:0} ms on one frame — sim {_simClock.Elapsed.TotalMilliseconds:0.0} " +
                        $"({_ticksThisFrame} ticks, of which planning {planningMs:0.0} for " +
                        $"{planned} routes, {_battle.Planning.Waiting} waiting), " +
                        $"views {_viewClock.Elapsed.TotalMilliseconds:0.0}, " +
                        $"tracking {_trackClock.Elapsed.TotalMilliseconds:0.0}, " +
                        $"gui {_guiClock.Elapsed.TotalMilliseconds:0.0}, " +
                        $"rest {ms - _simClock.Elapsed.TotalMilliseconds - _viewClock.Elapsed.TotalMilliseconds - _trackClock.Elapsed.TotalMilliseconds - _guiClock.Elapsed.TotalMilliseconds:0.0}. " +
                        $"{marching} of {onField} marching, {_views.Count} drawn, " +
                        $"{_selection.Count} selected, " +
                        $"{collections - _collectionsLastFrame} collections, heap " +
                        $"{heap / 1048576.0:0.0} MB ({(heap - _heapLastFrame) / 1024.0:+0;-0} kB).");
                }
            }

            // Reset here rather than in OnGUI: OnGUI runs more than once a
            // frame (a layout pass and a repaint pass at least), and what is
            // wanted is what all of them together cost.
            _guiClock.Reset();

            // Asking the runtime how big the heap is, twice a frame, is
            // harness work like any other - and it is the only line here that
            // walks anything.
            if (_options.Harness)
            {
                _collectionsLastFrame = System.GC.CollectionCount(0);
                _heapLastFrame = System.GC.GetTotalMemory(forceFullCollection: false);
            }
            _planningTicksLastFrame = _battle.RoutePlanningTicks;
            _routesPlannedLastFrame = _battle.RoutesPlanned;

            // A fresh allowance for this frame. This is the call that turns
            // rationing on at all — without a host doing it every request is
            // granted, which is how the CLI and the tests stay as they were.
            // It belongs here rather than beside AdvanceClock because it is a
            // *frame's* allowance: a frame that has fallen behind runs up to
            // eight ticks to catch up, and the whole point is that those eight
            // share one allowance instead of taking one each.
            _battle.Planning.OpenFrame(RoutesPerFrame, MillisecondsPerFrame);

            // Written every frame rather than once, so it can be turned in the
            // inspector while the battle runs - which is the only way to judge
            // a number whose whole effect is on how an order feels.
            Marching.SearchBudgetMs = SearchBudgetMs;

            // [M123]. With the harness, an order dearer than its own cap says
            // which stage spent the time. Harness work like any other, and it
            // is the harness that reads recordings.
            Marching.ExplainSlowPlans = _options.Harness;

            _frame.Restart();

            if (Input.GetKeyDown(KeyCode.F1))
                _options.Visible = !_options.Visible;

            if (Input.GetKeyDown(KeyCode.F2)) _options.Harness = !_options.Harness;

            // Reconciled rather than acted on where it is flipped, because it
            // is flipped in two places - F2 here and the checkbox at the top of
            // the panel - and a switch with two doors must not have the tidying
            // up behind only one of them.
            ReconcileHarness();

            if (Input.GetKeyDown(KeyCode.B)) ToggleBond();

            HandleClockKeys();
            HandleFogKeys();

            // Taken before the clock is asked to move, because taking it is
            // what lets the clock move again.
            CollectFinishedRoutes();

            _simClock.Restart();
            SimMarker.Begin();
            int before = _clock?.Tick ?? 0;
            AdvanceClock();
            _ticksThisFrame = (_clock?.Tick ?? 0) - before;
            SimMarker.End();
            _simClock.Stop();

            // Both of these can change a regiment's shape or stance, which is a
            // write to the field a worker may be reading, so both settle first
            // — but each settles inside itself, once a key has actually been
            // pressed. Settling here instead waited on the worker every single
            // frame, which is the whole of the off-thread pass undone.
            HandleFormationKeys();
            HandleStanceKeys();

            // Render between the last two ticks rather than at the newest one,
            // so motion stays smooth and a wheel stays legible however fast the
            // clock is running.
            float alpha = Mathf.Clamp01(_tickAccumulator / BattleClock.SecondsPerTick);

            _viewClock.Restart();
            ViewMarker.Begin();

            ApplyFog();

            foreach (UnitView view in _views)
                view.Render(alpha);

            ViewMarker.End();
            _viewClock.Stop();

            _trackClock.Restart();
            TrackMarker.Begin();

            // The measured cost of the harness, and the whole reason F2
            // exists. Both of these walk every regiment on the field every
            // frame and neither is the game.
            if (_options.Harness)
            {
                RefreshOverlayUnits();
                RefreshRegimentGrid();
            }

            ForgetAFinishedRoute();

            TrackSelection();
            TrackOrders();

            TrackMarker.End();
            _trackClock.Stop();
        }

        /// <summary>
        /// Takes the planned-route line down once the march it describes is
        /// over.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The line is a picture of an order <i>in progress</i>. It was only
        /// ever cleared when the selection changed, so a regiment that arrived
        /// went on trailing a six-metre amber bar, drawn above everything else,
        /// pointing at ground it was already standing on.
        /// </para>
        /// <para>
        /// This hid itself for as long as orders kept coming: each new march
        /// replaced the line, so it always looked current. Only the <i>last</i>
        /// order of a session was left showing — which is exactly how it was
        /// reported, as every manoeuvre looking right except the final one. The
        /// regiment had come round correctly and was sitting under a stale
        /// drawing of where it had been sent.
        /// </para>
        /// </remarks>
        private void ForgetAFinishedRoute()
        {
            if (_pathLine.positionCount == 0) return;

            foreach (UnitInstance unit in _selection)
            {
                if (unit.IsMarching) return;
            }

            _pathLine.positionCount = 0;
            _hasDestination = false;
        }

        // ---- Mouse ------------------------------------------------------------

        /// <summary>
        /// How far the mouse must be dragged before it counts as setting a
        /// facing, in screen pixels.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Pixels, not metres.</b> This was fifteen metres of ground, which
        /// is not a gesture — it is a gesture divided by the zoom. At the
        /// default view of a thousand-metre field in a small window the ground
        /// runs about a metre and a half to the pixel, so fifteen metres is a
        /// ten-pixel wobble: an ordinary unsteady click became an order to
        /// form line on a bearing perpendicular to the wobble, which is to say
        /// a facing chosen at random. Zoom out and it took less still.
        /// </para>
        /// <para>
        /// What the player is doing is a hand movement on a screen, so the
        /// threshold belongs in the units the hand works in. Twenty-four pixels
        /// is a deliberate drag at any zoom and on any window.
        /// </para>
        /// </remarks>
        private const float BearingDragPixels = 24f;

        /// <summary>
        /// How far a left drag must run before it counts as a box rather than a
        /// click, in screen pixels. Same reasoning as
        /// <see cref="BearingDragPixels"/>, and it was the same fault.
        /// </summary>
        private const float BoxDragPixels = 20f;

        private bool _boxing;
        private Vec2 _boxFrom;
        private Vector3 _boxFromScreen;

        private bool _ordering;
        private Vec2 _orderAt;
        private Vector3 _orderAtScreen;

        private Vec2 MouseWorld() => _camera != null ? _camera.MouseWorldPosition() : default;

        /// <summary>
        /// Left button: click to pick up a regiment, drag a box to pick up
        /// several.
        /// </summary>
        /// <remarks>
        /// Left says what you are commanding and right says what it should do.
        /// Left used to mean select, march, attack and set a facing all at once,
        /// which was already overloaded and becomes impossible with groups —
        /// there is no way to tell "drag a box round these four" apart from
        /// "march there facing that way" if both are the same button.
        /// </remarks>
        private void TrackSelection()
        {
            if (Input.GetMouseButtonDown(0) && !PointerOverPanels())
            {
                _boxing = true;
                _boxFrom = MouseWorld();
                _boxFromScreen = Input.mousePosition;
            }

            if (!_boxing) return;

            Vec2 here = MouseWorld();
            bool dragged = DraggedABox();

            if (Input.GetMouseButton(0))
            {
                if (dragged)
                    _status = $"Selecting {CountWithin(_boxFrom, here)} regiment(s) — release to confirm.";

                return;
            }

            _boxing = false;

            if (dragged) SelectWithin(_boxFrom, here);
            else SelectAt(_boxFrom);
        }

        /// <summary>Whether the left button has been dragged far enough to mean a box.</summary>
        private bool DraggedABox() =>
            (Input.mousePosition - _boxFromScreen).magnitude >= BoxDragPixels;

        /// <summary>
        /// Right button: click to march or attack, drag to march and arrive on
        /// the bearing you drew.
        /// </summary>
        /// <remarks>
        /// Click to march and each regiment keeps its front exactly where it is,
        /// edging sideways if it must. Drag, and they come round to the drawn
        /// bearing on arrival. Without the drag there is no way to change front
        /// at all except by attacking somebody; with it, a plain reposition
        /// stops spinning a hundred metres of frontage through a right angle
        /// every time.
        /// </remarks>
        private void TrackOrders()
        {
            if (Input.GetMouseButtonDown(1) && !PointerOverPanels())
            {
                // Every branch below either gives an order or asks for a plan,
                // and both write to the field a worker may still be reading.
                // Settling here covers the attack, the preview and the march in
                // one place rather than three.
                //
                // <b>Superseded first, and that ordering is the whole of [M126].</b>
                // [M80] built the machinery to abandon a plan whose order has
                // been replaced - Supersede marks it, the worker polls it - and
                // it was called from inside WorkOutRoutes, which runs *after*
                // this settle. So the click that made a plan unwanted waited for
                // it to finish before saying so. Measured in the recording of
                // 30 August: a frame that planned nothing at all and cost 293 ms,
                // of which 280 was this wait, against an order that took 381,7 ms
                // in total. Marking them first turns the wait into whatever is
                // left before the worker next looks.
                //
                // By the current selection, because that is who the order below
                // is for, whichever branch it takes. A plan out for somebody
                // else is still waited for: it is reading a field this order is
                // about to write.
                if (WorkingOutARoute)
                {
                    Supersede(_selection);
                    SettleRoutes();
                }

                if (_selection.Count == 0)
                {
                    _console.Blocked("Order", "Nothing selected — left-click a regiment first.");
                    _status = "Left-click a regiment, or drag a box round several.";
                    return;
                }

                Vec2 at = MouseWorld();
                UnitInstance clicked = UnitAt(at);

                // Right-clicking an enemy is ordinarily an attack. In preview
                // mode nothing is ordered at all, so the point clicked is just
                // ground to plan against, enemy regiment or not.
                if (!_options.PreviewRouteMode && clicked != null && clicked.Owner != Primary.Owner)
                {
                    AttackWithSelection(clicked);
                    return;
                }

                _ordering = true;
                _orderAt = at;
                _orderAtScreen = Input.mousePosition;
                return;
            }

            if (!_ordering) return;

            Vec2 drawn = MouseWorld() - _orderAt;
            bool far = (Input.mousePosition - _orderAtScreen).magnitude >= BearingDragPixels;

            if (Input.GetMouseButton(1))
            {
                _status = _options.PreviewRouteMode
                    ? "Previewing — release to compute the route, nothing will move."
                    : far
                        ? $"Forming a line on that bearing, facing {FrontOfDrawnLine(drawn).Degrees:0}° — " +
                          "release to confirm, drag the other way to face about."
                        : "Drag out the line they should form on, or release to keep the current front.";

                return;
            }

            _ordering = false;

            _lastDestination = _orderAt;
            _hasDestination = true;

            // Said before the order goes in, while the field is still the one
            // the grid was drawn on. Afterwards the regiment has been given a
            // route and the bodies it was routed round may already be moving.
            ReportRegimentGrid();

            if (_options.PreviewRouteMode)
                PreviewRoute(Primary, _orderAt);
            else
                MarchSelection(_orderAt, far ? FrontOfDrawnLine(drawn) : (Facing?)null);
        }

        /// <summary>
        /// The front a regiment holds when it forms along a line the player has
        /// dragged out.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The drag traces the <i>rank</i>, not the gaze — you draw the line the
        /// men stand in and they face square out of it. Dragging along the
        /// direction a regiment already faces therefore turns it a quarter
        /// circle, which is correct and was the whole confusion when the drag
        /// meant the direction to look: an order to keep the current front read
        /// as a right-angle wheel.
        /// </para>
        /// <para>
        /// Which of the two square-out directions is settled by handedness: the
        /// drag runs from the regiment's left flank to its right. So the same
        /// line dragged the other way faces them about, and both are reachable
        /// without a modifier.
        /// </para>
        /// <para>
        /// Length sets nothing. Frontage comes from how many men are standing
        /// and how deep they are drawn up, so a longer drag only makes the
        /// bearing easier to aim.
        /// </para>
        /// </remarks>
        private static Facing FrontOfDrawnLine(Vec2 drawn) =>
            Facing.FromVector(new Vec2(-drawn.Y, drawn.X));

        // ---- Selection --------------------------------------------------------

        /// <summary>Picks up whatever single regiment is under a point, or clears the selection.</summary>
        /// <remarks>
        /// Clicking one regiment of a bound wing picks up the whole wing. A bond
        /// exists precisely so the player stops thinking about its members
        /// individually, and having to re-box them every time would undo the
        /// entire point of tying them together.
        /// </remarks>
        private void SelectAt(Vec2 world)
        {
            UnitInstance clicked = UnitAt(world);

            if (clicked == null)
            {
                SetSelection(new List<UnitInstance>());
                _status = "Nothing there. Left-click a regiment, or drag a box round several.";
                return;
            }

            // Only a wing tied by hand pulls its fellows in. A transient
            // grouping is a product of selecting, not a reason to select.
            SetSelection(clicked.Bond <= 0
                ? new List<UnitInstance> { clicked }
                : Bond(clicked.Bond));
        }

        /// <summary>Every regiment still standing that carries a given bond.</summary>
        private List<UnitInstance> Bond(int bond)
        {
            var members = new List<UnitInstance>();

            foreach (UnitInstance unit in _battle.UnitsOnField())
            {
                if (unit.Bond == bond) members.Add(unit);
            }

            return members;
        }

        // ---- Binding ----------------------------------------------------------

        private int _nextBond = 1;

        /// <summary>Whether every selected regiment already shares one bond.</summary>
        private bool SelectionIsBound =>
            _selection.Count > 0 && Primary.Bond > 0 && _selection.TrueForAll(u => u.Bond == Primary.Bond);

        /// <summary>
        /// Ties the selection into one wing, or unties it if it already is one.
        /// </summary>
        /// <remarks>
        /// Bound regiments stay separate rectangles — they fight, take losses and
        /// break individually. What they share is a pace and a place in the line:
        /// an order to any of them moves all of them without disturbing the shape
        /// they stand in, and an attack sends the whole wing in abreast with its
        /// centre against the enemy's centre.
        /// </remarks>
        private void ToggleBond()
        {
            if (SelectionIsBound)
            {
                int was = Primary.Bond;

                foreach (UnitInstance unit in _selection) unit.Bond = 0;

                _console.Decision("Bond", $"Wing {was} untied — {_selection.Count} regiments back on their own.");
                _status = $"{_selection.Count} regiments unbound.";
                return;
            }

            if (_selection.Count < 2)
            {
                _console.Blocked("Bond", "Select at least two regiments to bind them into a wing.");
                _status = "Drag a box round two or more regiments first.";
                return;
            }

            int bond = _nextBond++;
            float pace = float.MaxValue;

            foreach (UnitInstance unit in _selection)
            {
                unit.Bond = bond;
                if (unit.BaseSpeed < pace) pace = unit.BaseSpeed;
            }

            _console.Decision("Bond",
                $"{_selection.Count} regiments bound as wing {bond} — they march together at " +
                $"{pace:0.00} m/s, the pace of the slowest, and attack abreast.");

            _status = $"Wing {bond}: {_selection.Count} regiments, {pace:0.00} m/s.";
        }

        /// <summary>
        /// Picks up every regiment of one side inside a dragged box.
        /// </summary>
        /// <remarks>
        /// One side only, decided by whichever regiment in the box has the lowest
        /// id. A box drawn across a melee catches both armies, and a mixed
        /// selection cannot be given a coherent order — so the enemy regiments
        /// are simply left out rather than the drag being refused.
        /// </remarks>
        private void SelectWithin(Vec2 from, Vec2 to)
        {
            var caught = new List<UnitInstance>();
            PlayerId? side = null;

            foreach (UnitInstance unit in _battle.UnitsOnField())
            {
                if (!Touches(from, to, unit)) continue;

                if (side == null) side = unit.Owner;
                if (unit.Owner != side.Value) continue;

                caught.Add(unit);
            }

            // A box that catches part of a wing catches all of it. Half a bond
            // cannot be given a coherent order — the other half would sit there
            // while its own centre walked off without it.
            for (int i = caught.Count - 1; i >= 0; i--)
            {
                if (caught[i].Bond <= 0) continue;

                foreach (UnitInstance member in Bond(caught[i].Bond))
                {
                    if (!caught.Contains(member)) caught.Add(member);
                }
            }

            SetSelection(caught);

            if (caught.Count == 0)
            {
                _status = "Nothing in the box.";
                return;
            }

            if (caught.Count == 1)
            {
                _status = $"{Primary.Def.DisplayName} selected.";
                return;
            }

            float pace = float.MaxValue;
            foreach (UnitInstance unit in caught) pace = Mathf.Min(pace, unit.BaseSpeed);

            _status = $"{caught.Count} regiments moving as one at {pace:0.00} m/s — B to keep them together.";
        }

        private int CountWithin(Vec2 from, Vec2 to)
        {
            int count = 0;
            PlayerId? side = null;

            foreach (UnitInstance unit in _battle.UnitsOnField())
            {
                if (!Touches(from, to, unit)) continue;

                if (side == null) side = unit.Owner;
                if (unit.Owner != side.Value) continue;

                count++;
            }

            return count;
        }

        /// <summary>
        /// Whether a regiment falls inside a dragged box.
        /// </summary>
        /// <remarks>
        /// Any part of it counts, and so does a box drawn entirely within one —
        /// a regiment is a hundred metres wide, so requiring the whole rectangle
        /// would mean most drags caught nothing at all.
        /// </remarks>
        private static bool Touches(Vec2 from, Vec2 to, UnitInstance unit)
        {
            float minX = Mathf.Min(from.X, to.X), maxX = Mathf.Max(from.X, to.X);
            float minY = Mathf.Min(from.Y, to.Y), maxY = Mathf.Max(from.Y, to.Y);

            OrientedRect shape = Clickable(unit);

            foreach (Vec2 corner in shape.GetCorners())
            {
                if (corner.X >= minX && corner.X <= maxX && corner.Y >= minY && corner.Y <= maxY)
                    return true;
            }

            return shape.ContainsPoint(new Vec2((minX + maxX) * 0.5f, (minY + maxY) * 0.5f));
        }

        // ---- Orders to a whole selection --------------------------------------

        /// <summary>
        /// Sends every selected regiment at one enemy.
        /// </summary>
        private void AttackWithSelection(UnitInstance target)
        {
            foreach (UnitInstance unit in _selection)
            {
                if (unit.Owner == target.Owner) continue;

                UnitOrder drawn = UnitOrder.Attack(target.Id, _options.WheelBeforeMarching);

                if (_options.PlanThenFire) _book.Draw(_battle, unit, drawn, PathfinderFor(unit), _console);
                else unit.GiveOrder(drawn, unit.Position);

                _console.Decision("Order",
                    $"{unit.Def.DisplayName} ordered to attack {target.Def.DisplayName} " +
                    $"at {Vec2.Distance(unit.Position, target.Position):0} m — will follow it.",
                    unit.Id);
            }

            _status = _selection.Count == 1
                ? $"{Primary.Def.DisplayName} attacking {target.Def.DisplayName}."
                : $"{_selection.Count} regiments attacking {target.Def.DisplayName}.";
        }

        /// <summary>
        /// Marches the whole selection, keeping the shape it is already standing
        /// in.
        /// </summary>
        /// <remarks>
        /// Every regiment moves by the same displacement rather than to the same
        /// point. Sending five regiments to one spot would have them arrive on
        /// top of one another — and now that friends cannot occupy the same
        /// ground, they would arrive jammed against each other instead. Moving
        /// the formation as it stands is also simply what the player meant: a
        /// line dragged fifty metres forward is still that line.
        /// </remarks>
        private void MarchSelection(Vec2 destination, Facing? bearing)
        {
            if (_selection.Count == 0) return;

            if (_selection.Count == 1)
            {
                PlanRoute(Primary, destination, bearing);
                return;
            }

            Vec2 origin = Vec2.Zero;
            foreach (UnitInstance unit in _selection) origin += unit.Position;
            origin /= _selection.Count;

            // One line for the wing rather than one per regiment, and the
            // regiments themselves go quiet — but the wing has to say something,
            // or a group order is the one kind of order that leaves no trace in
            // a recording at all. Which is unfortunate, because a wing walking
            // into itself is the failure most worth catching and the hardest to
            // reconstruct after the fact.
            _console.Info("Group",
                $"{_selection.Count} regiments ordered together from ({origin.X:0},{origin.Y:0}) to " +
                $"({destination.X:0},{destination.Y:0}) — {Vec2.Distance(origin, destination):0} m, " +
                $"keeping their shape" +
                (bearing.HasValue ? $", to arrive facing {bearing.Value.Degrees:0}°." : "."));

            // The whole wing on one clock. A regiment's own plan is a few
            // milliseconds and nobody would feel it; a wing of them lands inside
            // a single frame, and that is what a stutter on a group order is.
            var wing = new List<UnitInstance>(_selection);
            var wanted = new Vec2[wing.Count];

            for (int i = 0; i < wing.Count; i++)
                wanted[i] = destination + (wing[i].Position - origin);

            // Handed to a worker and applied when it lands. The clock is held
            // meanwhile, so the field the wing is planned against is the field
            // the player clicked on and not one a tick has moved underneath it.
            WorkOutRoutes(
                wing, wanted, bearing,
                quiet: true, asAWing: true,
                status: $"{wing.Count} regiments marching, keeping their formation.");
        }

        /// <summary>
        /// Prints each regiment's condition over the top of it.
        /// </summary>
        /// <remarks>
        /// Strength, morale and organization are the three numbers that explain
        /// almost every outcome, and reading them off the console means matching
        /// a line of text to a shape on screen while both are moving. Over the
        /// unit they belong to, a line collapsing is something you watch rather
        /// than reconstruct.
        /// </remarks>
        private void DrawUnitLabels()
        {
            if (Camera.main == null) return;

            // The name is always on — a rectangle you cannot identify is just a
            // coloured smear. The rest is detail, and lives behind the toggle.
            bool detailed = _options.ShowUnitLabels;

            if (_nameplate == null)
            {
                _nameplate = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 13,
                    fontStyle = FontStyle.Bold,
                };

                // Unity's default label is thin and pale, which over a map of
                // greens and browns is close to unreadable — and this text sits
                // on top of terrain, not on a panel.
                _nameplate.normal.textColor = Color.white;
            }

            Color original = GUI.color;

            foreach (UnitInstance unit in _battle.UnitsOnField())
            {
                if (_options.ViewingArmy >= 0 &&
                    !_battle.Vision.CanSee(_battle, new PlayerId(_options.ViewingArmy), unit))
                    continue;

                Vector3 screen = Camera.main.WorldToScreenPoint(new Vector3(unit.Position.X, unit.Position.Y, 0f));
                if (screen.z < 0f) continue;

                // Morale is the number that decides fights, so it is the one that
                // changes colour as it approaches the thresholds that matter.
                Color ink = unit.Morale < MoraleSystem.RoutingThreshold ? new Color(1f, 0.45f, 0.45f)
                    : unit.Morale < MoraleSystem.WaveringThreshold ? new Color(1f, 0.87f, 0.4f)
                    : Color.white;

                string wing = unit.Bond > 0 ? $"  [wing {unit.Bond}]" : string.Empty;

                string text = detailed
                    ? $"{unit.Def.DisplayName}{wing}\n" +
                      $"{unit.Strength}/{unit.InitialStrength}  {unit.FormationOrder.DisplayName}\n" +
                      $"mor {unit.Morale:0.00}  org {unit.Organization:0.00}\n" +
                      $"{unit.Stance}{(unit.State == UnitState.Steady ? string.Empty : "  " + unit.State)}"
                    : unit.Def.DisplayName + wing;

                float height = detailed ? 72f : 20f;
                var area = new Rect(screen.x - 80f, Screen.height - screen.y - height * 0.5f, 160f, height);

                DrawOutlined(area, text, ink);
            }

            GUI.color = original;
        }

        /// <summary>
        /// A bind/unbind button, on the main bar rather than behind the debug
        /// panel.
        /// </summary>
        /// <remarks>
        /// Tying a wing together is a command decision, not a diagnostic, so it
        /// belongs where the player is already looking. Only shown when there is
        /// something it could do — a button that reports "select two regiments
        /// first" is a button the player has to click to learn it was the wrong
        /// one.
        /// </remarks>
        private void DrawBondButton()
        {
            bool bound = SelectionIsBound;

            if (!bound && _selection.Count < 2) return;

            var area = new Rect(Screen.width - 150, 14, 120, 24);

            if (GUI.Button(area, bound ? $"Unbind wing {Primary.Bond}" : $"Bind {_selection.Count}"))
                ToggleBond();
        }

        /// <summary>
        /// Draws the box currently being dragged out, and an outline round every
        /// regiment it would catch.
        /// </summary>
        /// <remarks>
        /// The outlines matter more than the box. A regiment is a hundred metres
        /// wide and drawn as a thin bar, so at a distance a box either obviously
        /// contains one or is genuinely ambiguous — and letting go to find out
        /// is how a selection gets made twice.
        /// </remarks>
        private void DrawSelectionBox()
        {
            if (!_boxing || Camera.main == null) return;

            Vec2 here = MouseWorld();
            if (!DraggedABox()) return;

            Vector3 a = Camera.main.WorldToScreenPoint(new Vector3(_boxFrom.X, _boxFrom.Y, 0f));
            Vector3 b = Camera.main.WorldToScreenPoint(new Vector3(here.X, here.Y, 0f));

            var box = Rect.MinMaxRect(
                Mathf.Min(a.x, b.x), Screen.height - Mathf.Max(a.y, b.y),
                Mathf.Max(a.x, b.x), Screen.height - Mathf.Min(a.y, b.y));

            Color original = GUI.color;

            GUI.color = new Color(0.95f, 0.95f, 0.6f, 0.12f);
            GUI.DrawTexture(box, Texture2D.whiteTexture);

            GUI.color = new Color(0.95f, 0.95f, 0.6f, 0.9f);
            DrawEdges(box, 2f);

            foreach (UnitInstance unit in _battle.UnitsOnField())
            {
                if (!Touches(_boxFrom, here, unit)) continue;

                Vector3 centre = Camera.main.WorldToScreenPoint(new Vector3(unit.Position.X, unit.Position.Y, 0f));
                if (centre.z < 0f) continue;

                float radius = unit.Footprint.BoundingRadius / Camera.main.orthographicSize * Screen.height * 0.5f;

                DrawEdges(new Rect(centre.x - radius, Screen.height - centre.y - radius, radius * 2f, radius * 2f), 2f);
            }

            GUI.color = original;
        }

        private static void DrawEdges(Rect area, float thickness)
        {
            GUI.DrawTexture(new Rect(area.x, area.y, area.width, thickness), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(area.x, area.yMax - thickness, area.width, thickness), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(area.x, area.y, thickness, area.height), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(area.xMax - thickness, area.y, thickness, area.height), Texture2D.whiteTexture);
        }

        /// <summary>
        /// Draws a label with a dark outline behind it.
        /// </summary>
        /// <remarks>
        /// Text sitting on the map has no panel behind it, and the map is a
        /// patchwork of greens, browns and greys — so any single ink colour is
        /// invisible somewhere. An outline costs eight extra draws and makes the
        /// label readable over all of it, which no amount of choosing a better
        /// colour can.
        /// </remarks>
        private void DrawOutlined(Rect area, string text, Color ink)
        {
            GUI.color = new Color(0f, 0f, 0f, 0.85f);

            for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0) continue;

                GUI.Label(new Rect(area.x + dx, area.y + dy, area.width, area.height), text, _nameplate);
            }

            GUI.color = ink;
            GUI.Label(area, text, _nameplate);
        }

        /// <summary>
        /// Every pair of regiments where the first can actually make out the
        /// second, as line segments.
        /// </summary>
        /// <remarks>
        /// Only drawn on request — it is O(units squared) walks along terrain,
        /// which is fine for a dozen regiments and would not be for a hundred.
        /// </remarks>
        private List<(Vector3, Vector3)> BuildSightLines()
        {
            List<(Vector3, Vector3)> lines = _overlaySightLines;
            lines.Clear();

            if (!_options.ShowSightLines) return lines;

            foreach (UnitInstance watcher in _battle.UnitsOnField())
            foreach (UnitInstance target in _battle.UnitsOnField())
            {
                if (target.Owner == watcher.Owner) continue;
                if (!LineOfSight.CanSee(_battle, watcher, target)) continue;

                lines.Add((
                    new Vector3(watcher.Position.X, watcher.Position.Y, 0f),
                    new Vector3(target.Position.X, target.Position.Y, 0f)));
            }

            return lines;
        }

        /// <summary>
        /// Applies the harness cheats after a tick has been resolved.
        /// </summary>
        /// <remarks>
        /// Deliberately here and not in the rules. "Nobody dies" is not a rule
        /// the simulation should know about — it is the harness undoing what the
        /// simulation correctly did, which is the only arrangement where a debug
        /// flag cannot possibly be wrong in a shipped build.
        /// </remarks>
        private void ApplyCheats(IReadOnlyList<int> strengthBeforeTick)
        {
            if (_options.NoCasualties)
            {
                foreach (UnitInstance unit in _battle.AllUnits)
                {
                    if (unit.Id.Value >= strengthBeforeTick.Count) continue;
                    if (unit.Strength >= strengthBeforeTick[unit.Id.Value]) continue;

                    unit.Strength = strengthBeforeTick[unit.Id.Value];

                    if (unit.State == UnitState.Destroyed || unit.State == UnitState.Captured)
                        unit.State = UnitState.Steady;
                }
            }

            if (_options.NoRouting)
            {
                foreach (UnitInstance unit in _battle.UnitsOnField())
                {
                    if (unit.State != UnitState.Routing && unit.State != UnitState.Wavering) continue;

                    unit.State = UnitState.Steady;
                    unit.Morale = 1f;
                }
            }

            if (_options.InstantReload)
            {
                foreach (UnitInstance unit in _battle.UnitsOnField())
                    unit.ReloadRemaining = 0;
            }
        }

        /// <summary>
        /// Carries out whichever button the panel had pressed this frame.
        /// </summary>
        private void ApplyUnitActions()
        {
            bool wanted = _options.BreakSelected || _options.RestoreSelected || _options.DestroySelected;
            if (!wanted) return;

            if (_selection.Count == 0)
            {
                _options.BreakSelected = _options.RestoreSelected = _options.DestroySelected = false;
                _console.Blocked("Debug", "Nothing is selected — click a regiment first.");
                return;
            }

            foreach (UnitInstance unit in _selection)
            {
                if (_options.BreakSelected)
                {
                    unit.Morale = 0f;
                    unit.State = UnitState.Routing;
                    unit.Route = null;

                    _console.Warning("Debug",
                        $"{unit.Def.DisplayName} broken by hand — watch what it does to the regiments beside it.",
                        unit.Id);
                }

                if (_options.RestoreSelected)
                {
                    unit.Strength = unit.InitialStrength;
                    unit.Morale = 1f;
                    unit.Organization = 1f;
                    unit.State = UnitState.Steady;

                    _console.Info("Debug", $"{unit.Def.DisplayName} restored to full.", unit.Id);
                }

                if (_options.DestroySelected)
                {
                    unit.TakeCasualties(unit.Strength);

                    _console.Warning("Debug", $"{unit.Def.DisplayName} wiped out by hand.", unit.Id);
                }
            }

            if (_options.DestroySelected) SetSelection(new List<UnitInstance>());

            _options.BreakSelected = _options.RestoreSelected = _options.DestroySelected = false;
        }

        /// <summary>
        /// Cycles whose eyes the field is shown through, and whether hidden
        /// regiments are ghosted or gone.
        /// </summary>
        private void HandleFogKeys()
        {
            if (Input.GetKeyDown(KeyCode.V))
            {
                _options.ViewingArmy = _options.ViewingArmy + 1 >= _battle.Armies.Count ? -1 : _options.ViewingArmy + 1;

                _console.Info("Vision", _options.ViewingArmy < 0
                    ? "Fog off — showing both armies."
                    : $"Showing the field as {_battle.Armies[_options.ViewingArmy].Name} sees it.");
            }

            if (Input.GetKeyDown(KeyCode.G))
            {
                _options.GhostHiddenUnits = !_options.GhostHiddenUnits;

                _console.Info("Vision", _options.GhostHiddenUnits
                    ? "Ghosting what is hidden, so the fogged picture can be checked against the true one."
                    : "Hiding what is hidden.");
            }
        }

        /// <summary>
        /// Tells each view whether the army being looked through can see it.
        /// </summary>
        /// <remarks>
        /// Hiding happens only in the view. The units are still there, still
        /// ticked and still fighting — which is exactly the arrangement the real
        /// client will have, where the authority holds everything and hands out
        /// a fogged picture. Doing it any other way here would teach the harness
        /// habits the network layer cannot keep.
        /// </remarks>
        private void ApplyFog()
        {
            bool fogged = _options.ViewingArmy >= 0;
            var viewer = new PlayerId(_options.ViewingArmy);

            foreach (UnitView view in _views)
            {
                view.GhostWhenHidden = _options.GhostHiddenUnits;

                view.Spotted = !fogged || _battle.Vision.CanSee(_battle, viewer, view.Unit);
            }
        }

        /// <summary>
        /// Draws a faint marker wherever the viewing army last laid eyes on an
        /// enemy it can no longer see.
        /// </summary>
        /// <remarks>
        /// What turns fog from amnesia into intelligence. Without it a regiment
        /// that walks out of sight simply ceases to exist and there is nothing
        /// to plan against; with it you act on what you knew and find out
        /// whether it was still true. Deliberately faint, and fainter as it
        /// ages — the marker is exactly as trustworthy as it is old.
        /// </remarks>
        private void DrawGhosts()
        {
            if (_options.ViewingArmy < 0 || Camera.main == null) return;

            var viewer = new PlayerId(_options.ViewingArmy);

            if (_ghostPlate == null)
                _ghostPlate = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 12,
                    fontStyle = FontStyle.Italic,
                };

            Color original = GUI.color;

            foreach (UnitInstance unit in _battle.AllUnits)
            {
                if (unit.Owner == viewer) continue;
                if (_battle.Vision.CanSee(_battle, viewer, unit)) continue;

                if (!_battle.Vision.TryRecall(_battle, viewer, unit, out Vec2 where, out int age)) continue;

                Vector3 screen = Camera.main.WorldToScreenPoint(new Vector3(where.X, where.Y, 0f));
                if (screen.z < 0f) continue;

                // Fades out over about five turns, so a stale marker stops
                // shouting long before it stops being shown.
                float trust = Mathf.Clamp01(1f - age / (5f * BattleClock.TicksPerTurn));

                GUI.color = new Color(1f, 1f, 1f, 0.15f + 0.35f * trust);

                int turnsAgo = age / BattleClock.TicksPerTurn;

                GUI.Label(new Rect(screen.x - 60f, Screen.height - screen.y - 12f, 120f, 24f),
                    $"? {unit.Def.DisplayName}" + (turnsAgo > 0 ? $"  ({turnsAgo}t ago)" : string.Empty),
                    _ghostPlate);
            }

            GUI.color = original;
        }

        private void OnDisable()
        {
            // Anything still going when the recording stops is closed out first,
            // or the last thing that happened — which is very often the thing
            // being investigated — is left with no end and no duration.
            _quietened?.Flush();
            _console.StopRecording();
        }

        private void HandleClockKeys()
        {
            if (Input.GetKeyDown(KeyCode.Space))
            {
                _options.Running = !_options.Running;
                _console.Info("Clock", _options.Running ? "Running." : "Paused.");
            }

            // Stepping one tick at a time is how anything that looks wrong gets
            // diagnosed, so it works whether the clock is running or not.
            if (Input.GetKeyDown(KeyCode.Period))
            {
                _options.Running = false;
                StepOnce();
            }

            if (Input.GetKeyDown(KeyCode.Equals) || Input.GetKeyDown(KeyCode.KeypadPlus))
                _options.TimeScale =
                    Mathf.Min(DebugOptions.FastestScale, _options.TimeScale * 2f);

            if (Input.GetKeyDown(KeyCode.Minus) || Input.GetKeyDown(KeyCode.KeypadMinus))
                _options.TimeScale =
                    Mathf.Max(DebugOptions.SlowestScale, _options.TimeScale * 0.5f);
        }

        /// <summary>
        /// Converts real time into whole simulation ticks.
        /// </summary>
        /// <remarks>
        /// The clock only ever advances in fixed steps, however fast wall time
        /// is running — the time scale changes how often a tick happens, never
        /// how big one is. That is what keeps a battle reproducible regardless
        /// of framerate or how impatient the viewer was.
        /// </remarks>
        private void AdvanceClock()
        {
            if (!_options.Running) return;

            // [M143]. Under plan-then-fire the battle is still between turns.
            // Nothing accumulates while the player is drawing, or the moment a
            // turn is ended it would run every tick it had banked.
            if (_options.PlanThenFire && _resolving <= 0)
            {
                _tickAccumulator = 0f;
                return;
            }

            // A plan is out with a worker reading positions, shapes and the
            // spatial index. A tick writes to all three, so the battle waits -
            // which is the trade this whole arrangement makes: the clock loses
            // a fraction of a tick, the frame loses nothing.
            if (WorkingOutARoute)
            {
                // Not accumulated while held, or the moment the plan lands the
                // battle would fast-forward through every tick it owed and
                // undo the point of waiting.
                _tickAccumulator = 0f;
                return;
            }

            _tickAccumulator += Time.deltaTime * _options.BattleSecondsPerSecond;

            // Cap the catch-up so a stall cannot fast-forward the battle.
            int budget = 8;

            while (_tickAccumulator >= BattleClock.SecondsPerTick && budget-- > 0)
            {
                _tickAccumulator -= BattleClock.SecondsPerTick;
                StepOnce();

                if (_options.PlanThenFire && --_resolving <= 0)
                {
                    // The turn is over. Stop on the tick rather than letting the
                    // accumulator carry into the next one, or a slow frame would
                    // spend part of a turn nobody has ordered yet.
                    _resolving = 0;
                    _tickAccumulator = 0f;
                    break;
                }
            }

            if (budget <= 0) _tickAccumulator = 0f;
        }

        private void StepOnce()
        {
            // Stamps every line the simulation writes, so a recorded log can be
            // read back against the clock rather than as an undated stream.
            _console.Tick = _clock.Tick;

            // Taken before the tick so "nobody dies" has something to put back.
            // Only gathered when the cheat is on — this runs sixty times a
            // second at speed.
            List<int> before = _options.NoCasualties ? StrengthSnapshot() : EmptyStrengths;

            _clock.Advance(_battle, _quietened);

            ApplyCheats(before);

            // Snapshot after every tick so the view has two states to
            // interpolate between.
            foreach (UnitView view in _views)
                view.CaptureTick();
        }

        /// <summary>Stands in for "not gathered", so nothing has to be nullable.</summary>
        private static readonly List<int> EmptyStrengths = new List<int>();

        private List<int> StrengthSnapshot()
        {
            var strengths = new List<int>(_battle.AllUnits.Count);

            foreach (UnitInstance unit in _battle.AllUnits)
                strengths.Add(unit.Strength);

            return strengths;
        }

        /// <summary>
        /// Number keys reshape the selected regiment.
        /// </summary>
        /// <remarks>
        /// The shape change is immediate and visible, which is the whole point —
        /// watching cavalry go from a 110 m line to a 37 m column makes "form
        /// column to get through the gap" an obvious idea rather than a stat on
        /// a sheet. The organization it costs is what stops it being free.
        /// </remarks>
        private void HandleFormationKeys()
        {
            if (_selection.Count == 0 || _formations == null) return;

            for (int i = 0; i < _formations.Count && i < 9; i++)
            {
                if (!Input.GetKeyDown(KeyCode.Alpha1 + i)) continue;

                // A change of formation rewrites the footprint a worker may be
                // reading, so the plan lands before the shape moves under it.
                if (WorkingOutARoute) SettleRoutes();

                FormationDef target = _formations.All[i];

                foreach (UnitInstance unit in _selection)
                {
                    if (target.Key == unit.FormationOrder.Key)
                    {
                        _console.Info("Formation", $"{unit.Def.DisplayName} is already in {target.DisplayName}.", unit.Id);
                        continue;
                    }

                    float widthBefore = unit.Footprint.Width;
                    float spent = unit.AdoptFormation(target);
                    float widthAfter = unit.Footprint.Width;

                    _console.Decision("Formation",
                        $"{unit.Def.DisplayName} formed {target.DisplayName}: " +
                        $"frontage {widthBefore:0} m to {widthAfter:0} m, " +
                        $"organization -{spent:0.00} (now {unit.Organization:0.00}).",
                        unit.Id);

                    if (unit.Organization < 0.4f)
                        _console.Warning("Formation",
                            $"{unit.Def.DisplayName} is badly disordered at {unit.Organization:0.00} — " +
                            "further reshaping will leave it barely able to fight.", unit.Id);
                }

                _status = _selection.Count == 1
                    ? $"{Primary.Def.DisplayName} formed {target.DisplayName} — " +
                      $"{Primary.Footprint.Width:0} m frontage, organization {Primary.Organization:0.00}."
                    : $"{_selection.Count} regiments formed {target.DisplayName}.";

                // Re-plan, since the new shape can change what it fits through.
                if (_hasDestination && _selection.Count == 1) PlanRoute(Primary, _lastDestination);
                return;
            }
        }

        /// <summary>
        /// Q/W/E/R set the selected unit's standing stance.
        /// </summary>
        /// <remarks>
        /// Persistent, not per-order: a reserve set to Defend stays that way
        /// without being told again, which is the point of a standing
        /// instruction.
        /// </remarks>
        private void HandleStanceKeys()
        {
            if (_selection.Count == 0) return;

            Stance? chosen = null;
            if (Input.GetKeyDown(KeyCode.Q)) chosen = Stance.Defend;
            else if (Input.GetKeyDown(KeyCode.E)) chosen = Stance.Advance;
            else if (Input.GetKeyDown(KeyCode.R)) chosen = Stance.Aggressive;
            else if (Input.GetKeyDown(KeyCode.T)) chosen = Stance.Evade;

            if (chosen == null) return;

            // Same reason as the formation keys: the plan lands before the
            // stance it was planned under changes.
            if (WorkingOutARoute) SettleRoutes();

            foreach (UnitInstance unit in _selection)
            {
                if (unit.Stance == chosen.Value) continue;

                unit.Stance = chosen.Value;
                unit.HeldUpBy = UnitId.None;

                _console.Decision("Stance",
                    $"{unit.Def.DisplayName} now standing on {chosen.Value} — {DescribeStance(chosen.Value)}",
                    unit.Id);
            }

            _status = _selection.Count == 1
                ? $"{Primary.Def.DisplayName}: {chosen.Value}."
                : $"{_selection.Count} regiments: {chosen.Value}.";
        }

        private static string DescribeStance(Stance stance) => stance switch
        {
            Stance.Defend => "halts on contact, even where it could force through.",
            Stance.Advance => "pushes through an enemy line where it is able to.",
            Stance.Aggressive => "closes with anything that comes near, and pursues.",
            Stance.Evade => "withdraws from anything that comes near.",
            _ => string.Empty
        };

        /// <summary>The cells last drawn, and what each of them was.</summary>
        /// <remarks>
        /// The cell list is kept between frames because it only changes when
        /// the spacing does - walking the whole field to enumerate it is the
        /// expensive half, and the bodies moving about on it is the cheap half.
        /// </remarks>
        private readonly List<Coord> _gridCells = new List<Coord>();

        private readonly List<CellState> _gridStates = new List<CellState>();

        private float _gridSpacingDrawn;

        private Vec2 _gridDrawnAround;

        private readonly List<OrientedRect> _reserved = new List<OrientedRect>();

        /// <summary>Roughly how many cells the overlay will draw at once.</summary>
        /// <remarks>
        /// Four thousand hexes is twenty-four thousand triangles, which draws
        /// in well under a millisecond and still covers the whole Great Field
        /// at one regiment to a cell. At a tenth of one it becomes a window
        /// about 150 m across, which is the right size for the thing that
        /// resolution is for: checking the blocked cells against the
        /// rectangles that blocked them.
        /// </remarks>
        private const int DrawnCellCap = 4000;

        /// <summary>
        /// Lays the regiment grid for whichever regiment is selected and draws
        /// it, with the route it would take to wherever the mouse is.
        /// </summary>
        /// <remarks>
        /// Routed to the mouse rather than to the last order, so the grid can
        /// be interrogated by pointing at things: hover across a gap and watch
        /// whether the line goes through it or round. That is the question this
        /// whole overlay exists to answer, and a static picture of the last
        /// order cannot answer it.
        /// </remarks>
        // What the drawn grid route was last asked for. The overlay keeps the
        // line itself; these only decide whether the question has changed.
        private Vec2 _gridRouteTo;
        private Vec2 _gridRouteFrom;
        private bool _gridRouteFresh;

        private void RefreshRegimentGrid()
        {
            if (_options == null || !_options.Harness || !_options.ShowRegimentGrid ||
                _battle == null || Primary == null)
            {
                _gridRouteFresh = false;

                if (_gridCells.Count > 0)
                {
                    _gridCells.Clear();
                    _gridSpacingDrawn = 0f;
                    _gridDrawnAround = default;
                    _overlay.SetRegimentGrid(null, null, default);
                    _overlay.SetGridRoute(null);
                    _overlay.SetReservedAreas(null);
                }

                return;
            }

            UnitInstance unit = Primary;

            RegimentGrid.SpacingMultiple = _options.RegimentGridSpacingMultiple;
            RegimentGrid grid = RegimentGrid.For(_battle, unit);

            // A whole field is a couple of thousand cells at one regiment to a
            // cell, and two hundred thousand at a tenth of one. So the drawing
            // is windowed to whatever radius holds about DrawnCellCap cells,
            // centred on the regiment being looked at - which is also where
            // anyone checking the marking against the rectangles is looking.
            // The search is never windowed; only the picture is.
            float hexArea = 0.866f * grid.Spacing * grid.Spacing;
            float radius = Mathf.Sqrt(DrawnCellCap * hexArea / Mathf.PI);

            bool moved = Vec2.Distance(unit.Position, _gridDrawnAround) > grid.Spacing;
            bool rebuilt = false;

            if (_gridCells.Count == 0 || moved ||
                Mathf.Abs(grid.Spacing - _gridSpacingDrawn) > 0.01f)
            {
                grid.Snapshot(_gridCells, unit.Position, radius);
                _gridSpacingDrawn = grid.Spacing;
                _gridDrawnAround = unit.Position;
                rebuilt = true;
            }

            _gridStates.Clear();

            for (int i = 0; i < _gridCells.Count; i++)
                _gridStates.Add(grid.StateOf(_gridCells[i]));

            _overlay.SetRegimentGrid(_gridCells, _gridStates, grid.Layout);

            Vec2 to = MouseWorld();

            // <b>Only when the cursor has actually moved a cell.</b> This is a
            // whole grid search and it was being run every single frame, at
            // whatever the mouse happened to be pointing at - which a recording
            // caught costing a median of 92 ms a frame and 262 at the worst,
            // while the simulation's own share of those frames was nought.
            //
            // The rule is the one the snapshot above already uses on the
            // regiment: a move of less than one cell cannot change which cells
            // a route goes through, so it cannot change the answer. Same
            // threshold, same reason, and the picture is identical.
            bool sameQuestion =
                _gridRouteFresh && !rebuilt &&
                Vec2.Distance(to, _gridRouteTo) <= grid.Spacing &&
                Vec2.Distance(unit.Position, _gridRouteFrom) <= grid.Spacing;

            if (!sameQuestion)
            {
                _gridRouteTo = to;
                _gridRouteFrom = unit.Position;
                _gridRouteFresh = true;

                // M82. Straightened with the same cast-ahead pass every
                // planner's answer goes through, because the raw grid answer is
                // a path of hex centres and nothing would ever walk it. At a
                // tenth of a regiment to a cell the difference is 159 points a
                // route against five.
                _overlay.SetGridRoute(
                    grid.TryRoute(unit.Position, to, out List<Vec2> route)
                        ? Marching.Straightened(_battle, unit, route)
                        : null);
            }

            // What the grid is really marking against. The body drawn on screen
            // is not it: a cell is refused where this regiment's own circle,
            // placed at that cell, would overlap somebody - so the shape that
            // matters is every other body grown by that circle.
            _reserved.Clear();

            if (_options.ShowReservedAreas)
            {
                Footprint print = unit.Shape.Footprint;
                float reach =
                    print.HalfDepth +
                    (print.BoundingRadius - print.HalfDepth) * RegimentGrid.ClearanceFraction +
                    RegimentGrid.MarginMetres;

                foreach (UnitInstance body in _battle.UnitsOnField())
                {
                    if (body.Id == unit.Id) continue;
                    if (Vec2.Distance(body.Position, unit.Position) > 600f) continue;

                    _reserved.Add(new OrientedRect(
                        body.Shape.Centre, body.Shape.Facing,
                        new Footprint(
                            body.Shape.Footprint.Width + 2f * reach,
                            body.Shape.Footprint.Depth + 2f * reach)));
                }
            }

            _overlay.SetReservedAreas(_reserved);
        }

        /// <summary>
        /// Says what the grid made of the order just given, next to what the
        /// cascade actually did with it.
        /// </summary>
        private void ReportRegimentGrid()
        {
            if (_options == null || !_options.ShowRegimentGrid || _battle == null || Primary == null)
                return;

            UnitInstance unit = Primary;
            Vec2 to = _hasDestination ? _lastDestination : MouseWorld();

            RegimentGrid.SpacingMultiple = _options.RegimentGridSpacingMultiple;

            var watch = System.Diagnostics.Stopwatch.StartNew();
            RegimentGrid grid = RegimentGrid.For(_battle, unit);
            double built = watch.Elapsed.TotalMilliseconds;

            watch.Restart();
            bool found = grid.TryRoute(unit.Position, to, out List<Vec2> route);
            double searched = watch.Elapsed.TotalMilliseconds;

            _console.Info("Grid",
                $"{unit.Def.DisplayName}: cells {grid.Spacing:0.#} m " +
                $"({unit.Shape.Footprint.Width:0.#} x {unit.Shape.Footprint.Depth:0.#} m regiment, " +
                $"bounding circle {unit.Shape.Footprint.BoundingRadius * 2f:0.#} m), " +
                $"{RegimentGrid.LastBlockedCells} cells held by bodies. " +
                (found
                    ? $"Route in {route.Count} waypoints over {RegimentGrid.LastCellsExplored} cells settled."
                    : $"No route: {RegimentGrid.LastCellsExplored} cells settled and every way round blocked.") +
                $" Built {built:0.00} ms, searched {searched:0.00} ms. " +
                $"In the cascade it is {GridRoutePlanner.Use} " +
                $"(asked {GridRoutePlanner.Asked}, found {GridRoutePlanner.Found}, " +
                $"held {GridRoutePlanner.Held}).");
        }

        // Filled afresh every frame and handed straight to the overlay, which
        // copies out of them. Kept as fields rather than made on the spot
        // because two lists a frame at sixty a second is the steady litter that
        // was showing up in the recording as a collection on the frames that
        // stopped - and the collector, not the drawing, is what stopped them.
        private readonly List<OverlayUnit> _overlayShapes = new List<OverlayUnit>();
        private readonly List<(Vector3, Vector3)> _overlaySightLines =
            new List<(Vector3, Vector3)>();

        /// <summary>What the harness switch was last seen at.</summary>
        private bool _harnessWas = true;

        /// <summary>
        /// Notices the harness being switched, and clears what it had drawn.
        /// </summary>
        /// <remarks>
        /// Cleared rather than left up, because geometry that stops being
        /// refreshed but carries on being drawn is worse than no geometry at
        /// all: it is a picture of where things were when you stopped looking,
        /// and nothing on screen says so.
        /// </remarks>
        private void ReconcileHarness()
        {
            if (_options.Harness == _harnessWas) return;

            _harnessWas = _options.Harness;

            // Said before the console stops listening, and after it starts, so
            // the line explaining the switch survives the switch either way.
            if (_options.Harness)
            {
                _console.Listening = true;
                _console.Info("Harness",
                    "Harness back on — the overlay, the grid and this console are being kept up again.");
                return;
            }

            _console.Info("Harness",
                "Harness off — the overlay, the regiment grid and this console stop being kept up. " +
                "The frame counter does not, and a recording already started keeps writing. " +
                "F2 again to bring it back.");

            _console.Listening = false;

            _overlayShapes.Clear();
            _overlaySightLines.Clear();

            _overlay.SetUnits(_overlayShapes);
            _overlay.SetSightLines(_overlaySightLines);
            _overlay.SetRegimentGrid(null, null, default);
            _overlay.SetGridRoute(null);
            _overlay.SetReservedAreas(null);
            _overlay.SetSearchCells(null, default);
            _overlay.SetRawPath(null, default);
            _overlay.ClearRoutePreview();

            _gridCells.Clear();
            _gridSpacingDrawn = 0f;
            _gridDrawnAround = default;
            _gridRouteFresh = false;
        }

        private void RefreshOverlayUnits()
        {
            List<OverlayUnit> shapes = _overlayShapes;
            shapes.Clear();

            // Show the width actually used for routing, so the overlay never
            // implies a constraint that is not being applied.
            foreach (UnitInstance unit in _battle.UnitsOnField())
            {
                shapes.Add(new OverlayUnit
                {
                    Shape = unit.Shape,
                    Clearance = _options.RespectUnitWidth ? unit.Footprint.Width * 0.5f : 0f,
                    Zoc = unit.ZoneOfControl,
                    Sight = LineOfSight.SightRange(_battle, unit),
                    WeaponRange = unit.Def.Get(UnitAttributes.Range),
                });
            }

            _overlay.SetUnits(shapes);
            _overlay.SetSightLines(BuildSightLines());
        }

        /// <summary>
        /// The regiment under a world point, with a generous allowance for how
        /// thin a line actually is. Iterates in id order so an overlap always
        /// resolves the same way.
        /// </summary>
        /// <remarks>
        /// A regiment in line is a hundred metres wide and four deep. Hit-testing
        /// its true rectangle means asking the player to click a hairline, so the
        /// target is fattened to a depth you can reasonably hit. This is purely
        /// about the mouse — every rule still uses the real footprint, so nothing
        /// fights or collides differently because it became easier to select.
        /// </remarks>
        private UnitInstance UnitAt(Vec2 world)
        {
            foreach (UnitInstance unit in _battle.UnitsOnField())
            {
                if (Clickable(unit).ContainsPoint(world))
                    return unit;
            }

            return null;
        }

        /// <summary>
        /// The shape the mouse tests against: what the player can see, floored
        /// at a depth thick enough to hit.
        /// </summary>
        /// <remarks>
        /// Matches the drawn plate rather than the true footprint, so clicking
        /// picks up the regiment you were pointing at rather than one whose
        /// frontage reaches further than it looks.
        /// </remarks>
        private static OrientedRect Clickable(UnitInstance unit)
        {
            Footprint real = unit.Footprint;

            return new OrientedRect(unit.Position, unit.Facing,
                new Footprint(real.Width, DrawnDepthOf(real)));
        }

        /// <summary>
        /// The bond number handed to whatever is selected, when the player has
        /// not tied them together deliberately.
        /// </summary>
        /// <remarks>
        /// Negative, so it can never collide with a wing bound by hand — those
        /// count up from one.
        /// </remarks>
        private const int TransientBond = -1;

        /// <summary>
        /// Makes the current selection manoeuvre as one body, and lets the last
        /// one go.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Picking several regiments up is already a statement that you mean to
        /// handle them together, so they behave like a wing for as long as you
        /// hold them: same pace, keeping their shape. Pressing B is what makes
        /// that survive letting go of them.
        /// </para>
        /// <para>
        /// It costs something and is worth saying plainly: a wing marches at
        /// its slowest regiment, so boxing cavalry together with foot slows the
        /// cavalry until the selection is dropped. That is what moving as one
        /// body means, and it is why the binding is transient — let go and the
        /// horse is quick again.
        /// </para>
        /// </remarks>
        private void HoldTogether(List<UnitInstance> units)
        {
            int wasGrouped = 0;

            foreach (UnitInstance unit in _battle.AllUnits)
            {
                if (unit.Bond != TransientBond) continue;

                unit.Bond = 0;
                wasGrouped++;
            }

            int nowGrouped = 0;
            float pace = float.MaxValue;
            string slowest = string.Empty;

            if (units.Count >= 2)
            {
                foreach (UnitInstance unit in units)
                {
                    // Never over a wing the player tied by hand — that one is
                    // theirs, and it already moves as one.
                    if (unit.Bond != 0) continue;

                    unit.Bond = TransientBond;
                    nowGrouped++;

                    if (unit.BaseSpeed >= pace) continue;

                    pace = unit.BaseSpeed;
                    slowest = unit.Def.DisplayName;
                }
            }

            // Said because it is invisible and it costs something. Boxing horse
            // together with foot silently slows the horse to the foot's pace for
            // as long as the selection is held, and a player watching cavalry
            // crawl has no way to know that a box drawn several orders ago is
            // the reason.
            if (nowGrouped >= 2)
            {
                _console.Decision("Group",
                    $"{nowGrouped} regiments picked out together — they move as one body at " +
                    $"{pace:0.00} m/s, the pace of the {slowest}. Let go and they are on their own again.");
            }
            else if (wasGrouped >= 2)
            {
                _console.Decision("Group",
                    $"{wasGrouped} regiments let go — each back to its own pace and its own orders.");
            }
        }

        private void SetSelection(List<UnitInstance> units)
        {
            HoldTogether(units);

            _selection.Clear();
            _selection.AddRange(units);

            _hasDestination = false;
            _pathLine.positionCount = 0;
            _overlay.SetSearchCells(null, default);
            _overlay.SetRawPath(null, default);
            _overlay.ClearRoutePreview();

            foreach (UnitView view in _views)
                view.SetSelected(_selection.Contains(view.Unit));

            // The full read-out only when there is one regiment to read out. A
            // wing of six would be six paragraphs of console for one click,
            // which buries whatever the player was actually watching.
            if (_selection.Count != 1) return;

            UnitInstance unit = _selection[0];
            TerrainDef ground = _battle.TerrainAt(unit.Position);

            _console.Info("Select",
                $"{unit.Def.DisplayName} — {unit.Strength}/{unit.InitialStrength} men, " +
                $"{unit.FormationOrder.DisplayName} ({unit.Footprint.Width:0} x {unit.Footprint.Depth:0} m), " +
                $"morale {unit.Morale:0.00}, organization {unit.Organization:0.00}, facing {unit.Facing}, " +
                $"on {ground.DisplayName} at {_battle.SpeedOf(unit):0.00} m/s.", unit.Id);

            _status =
                $"{unit.Def.DisplayName}: {unit.Strength} men, {unit.FormationOrder.DisplayName}, " +
                $"org {unit.Organization:0.00}, stance {unit.Stance}.";
        }

        /// <summary>
        /// Works out what would be ordered, and draws it, without ordering it.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Everything <see cref="PlanRoute"/> does up to the point it would call
        /// <c>GiveOrder</c> — the same clearance rule, the same placement search
        /// when the exact point is taken or impassable — reused rather than
        /// duplicated, so a preview can never show a route the real order
        /// wouldn't. It stops before touching the unit at all.
        /// </para>
        /// <para>
        /// Draws the ladder's answer and the search's answer as two separate
        /// lines when <see cref="DebugOptions.PreviewEveryPlanner"/> is on,
        /// because the disagreement between them is usually the thing worth
        /// looking at, and marks every place <see cref="RouteSearch"/> considered
        /// bending at — not only the ones its winning route used — since a
        /// candidate that <i>wasn't</i> picked is exactly what explains a route
        /// that looks wrong.
        /// </para>
        /// </remarks>
        /// <summary>
        /// A pathfinder set up the way this regiment's routes are worked out.
        /// </summary>
        /// <remarks>
        /// The same construction `PreviewRoute` does, lifted out because
        /// [M143]'s order book needs one too and building a second copy by hand
        /// is how the two quietly stop agreeing.
        /// </remarks>
        private IPathfinder PathfinderFor(UnitInstance unit)
        {
            float clearance = _options.RespectUnitWidth
                ? unit.Footprint.Width * 0.5f
                : HexPathfinder.DefaultClearanceMetres;

            return _options.RouteLikeAi
                ? new HexPathfinder(_map.Terrain, _trueMovement, _terrainCatalogue, clearanceMetres: clearance)
                : (IPathfinder)new DirectPathfinder(
                    _map.Terrain, _trueMovement, _terrainCatalogue, clearanceMetres: clearance);
        }

        private void PreviewRoute(UnitInstance unit, Vec2 destination)
        {
            if (unit == null) return;

            float clearance = _options.RespectUnitWidth
                ? unit.Footprint.Width * 0.5f
                : HexPathfinder.DefaultClearanceMetres;

            IPathfinder pathfinder = _options.RouteLikeAi
                ? new HexPathfinder(_map.Terrain, _trueMovement, _terrainCatalogue, clearanceMetres: clearance)
                : new DirectPathfinder(_map.Terrain, _trueMovement, _terrainCatalogue, clearanceMetres: clearance);

            if (OrderSystem.TryFindPlacement(_battle, unit, destination, unit.Facing, out Vec2 stand))
                destination = stand;
            else
                destination = OrderSystem.NearestReachable(_battle, unit, destination, unit.Position);

            _overlay.ClearRoutePreview();

            // The one thing a screenshot cannot give back: enough to rebuild
            // the exact arrangement later without guessing pixel positions off
            // a picture. Written once per preview, not per planner, and before
            // either plan runs, so it is here even if something below throws.
            ReportScene(unit, destination);

            if (_options.ShowRouteCandidates)
                _overlay.SetRouteCandidates(RouteSearch.DebugCandidatePlaces(_battle, unit, destination));

            // What GiveOrder would set for the click being previewed: a plain
            // right-click draws no bearing, so the front is the line of march.
            // Left to the planner's own fallback it reads the *previous*
            // order's front instead, and the search pays for that one in
            // ground.
            Facing arriveOn = Marching.AlongTheLine(unit.Position, destination, unit.Facing);

            string key = DrawEveryPlanner(unit, destination, pathfinder, arriveOn, report: true);

            _status = $"Previewing {unit.Def.DisplayName}'s route — nothing moved. " + key +
                       (_options.ShowRouteCandidates ? "  Cyan crosses: candidate places." : string.Empty);
        }

        /// <summary>
        /// Plans the same march with every planner asked for and draws each in
        /// its own colour. Returns the colour key, for the status line.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Called for a previewed click <i>and</i> for a real order, because the
        /// question "why did it go that way" is asked about marches that
        /// happened far more often than about ones being rehearsed. The route
        /// actually walked is drawn by the ordinary route overlay; these are
        /// what the other planners would have answered.
        /// </para>
        /// <para>
        /// Not for group orders. Four extra plans is nothing for one regiment
        /// and is forty times that for a wing, which is the stutter the whole
        /// planning budget exists to avoid — and it would be a stutter this
        /// debug view invented.
        /// </para>
        /// </remarks>
        /// <param name="already">
        /// The default planner's answer, when the caller has already worked it
        /// out. Without this a real order plans the default twice — which was
        /// merely wasteful at a millisecond and is unusable while the default is
        /// the hybrid at a second and more.
        /// </param>
        private string DrawEveryPlanner(
            UnitInstance unit, Vec2 destination, IPathfinder pathfinder, Facing arriveOn, bool report,
            Plan? already = null)
        {
            // Which planners to ask. The default is always asked and always
            // drawn, because it is the only one a real order will use — a
            // preview that leaves it out is a preview of something the game
            // does not do, which is what this used to be: it drew TheSearch
            // while orders went through TheTangents.
            var asking = new List<int>();

            for (int i = 0; i < RoutePlanners.All.Count; i++)
            {
                IRoutePlanner planner = RoutePlanners.All[i];

                if (ReferenceEquals(planner, RoutePlanners.Default))
                {
                    asking.Add(i);
                    continue;
                }

                if (!_options.PreviewEveryPlanner) continue;

                // The hybrid is a second and more for a single order, and a
                // preview pays that inside one frame. Asked only by name.
                if (ReferenceEquals(planner, RoutePlanners.TheHybridAStar) && !_options.PreviewTheHybrid)
                    continue;

                asking.Add(i);
            }

            var key = new System.Text.StringBuilder();

            foreach (int i in asking)
            {
                IRoutePlanner planner = RoutePlanners.All[i];
                bool isDefault = ReferenceEquals(planner, RoutePlanners.Default);

                bool reused = isDefault && already.HasValue;

                var spent = System.Diagnostics.Stopwatch.StartNew();

                Plan plan = reused
                    ? already!.Value
                    : Marching.PlanTo(
                        _battle, unit, pathfinder, destination, planner: planner, arriveOn: arriveOn);

                spent.Stop();

                _overlay.AddRoutePreview(i, plan.Path.Waypoints);

                if (report)
                {
                    _console.Info("Preview",
                        $"{planner.Name} — {ColourName(i)} — " +
                        (plan.Path.Found
                            ? $"{plan.Path.Waypoints.Count} waypoints, {plan.Path.Distance:0} m, " +
                              $"{Marching.SecondsToWalk(_battle, unit, plan.Path.Waypoints, plan.Hold):0} s" +
                              (plan.PressedThrough ? ", pressing through its own" : string.Empty)
                            : $"no route — {plan.Path.FailureDetail}") +
                        (reused
                            ? "  <- this is the one it will walk."
                            : $" — worked out in {spent.Elapsed.TotalMilliseconds:0.0} ms" +
                              (isDefault ? "  <- this is the one it will walk." : ".")),
                        unit.Id);
                }

                if (key.Length > 0) key.Append("  ");
                key.Append(ColourName(i)).Append(": ").Append(planner.Name);

                if (isDefault) key.Append(" (walked)");
            }

            return key.ToString();
        }

        /// <summary>
        /// What to call each planner's colour in the console and the status
        /// line, in the order <c>RoutePlanners.All</c> gives them.
        /// </summary>
        /// <remarks>
        /// Named rather than swatched because the console is text, and a line
        /// saying "green" is findable in a log file six days later while a
        /// coloured pixel is not. Kept in step with
        /// <see cref="DebugOverlay.PlannerColours"/> by hand — there are five of
        /// them and they change about once a year.
        /// </remarks>
        private static readonly string[] PlannerColourNames =
            { "orange", "green", "cyan", "yellow", "violet" };

        private static string ColourName(int which) =>
            PlannerColourNames[which % PlannerColourNames.Length];

        /// <summary>
        /// Everything about the arrangement a preview was asked against: the
        /// mover, the resolved destination, and every friendly regiment on the
        /// field that a route could conceivably bend around.
        /// </summary>
        /// <remarks>
        /// Friendly rather than nearby-filtered, and deliberately: the point of
        /// this line is rebuilding the scene from the log alone, and a
        /// corridor filter tuned for planning is exactly the kind of thing that
        /// might itself be the bug being chased.
        /// </remarks>
        /// <summary>
        /// The arrangement an order was given in, written once for the field
        /// and once per regiment ordered.
        /// </summary>
        /// <remarks>
        /// The whole field rather than what is near the route, deliberately and
        /// for the same reason <see cref="ReportScene"/> gives: a filter tuned
        /// for planning is exactly the kind of thing that turns out to be the
        /// bug being chased, and one that hid the cause would be worse than no
        /// line at all.
        /// </remarks>
        private void ReportOrderedScene(IReadOnlyList<UnitInstance> wing, Vec2[] wanted)
        {
            if (_battle == null || wing == null || wing.Count == 0) return;

            // Gated on the harness rather than on a switch of its own, and
            // that is the whole argument for where it sits. W7 says a
            // diagnostic has to work from the recording alone, so this stays on
            // for an ordinary session - a bug report that arrives without the
            // arrangement it happened in is a bug report nobody can act on.
            //
            // But it is forty-six formatted lines and forty-six writes on the
            // very frame an order is given, which is already the worst frame in
            // the session, and it was sixty-one per cent of one recording. So
            // when the harness is off - which is exactly when somebody is
            // timing orders rather than diagnosing them - it goes too.
            if (!_options.Harness) return;

            for (int i = 0; i < wing.Count && i < wanted.Length; i++)
            {
                UnitInstance unit = wing[i];
                if (unit == null) continue;

                _console.Info("Scene",
                    $"ordered {unit.Def.DisplayName} at ({unit.Position.X:0.0},{unit.Position.Y:0.0}) " +
                    $"facing {unit.Facing.Degrees:0.0}°, " +
                    $"{unit.Footprint.Width:0}x{unit.Footprint.Depth:0} m, " +
                    $"to ({wanted[i].X:0.0},{wanted[i].Y:0.0}).",
                    unit.Id);
            }

            foreach (UnitInstance other in _battle.UnitsOnField())
            {
                _console.Info("Scene",
                    $"  {other.Def.DisplayName} #{other.Id} at " +
                    $"({other.Position.X:0.0},{other.Position.Y:0.0}) " +
                    $"facing {other.Facing.Degrees:0.0}°, " +
                    $"{other.Footprint.Width:0}x{other.Footprint.Depth:0} m, " +
                    $"{(other.Owner == wing[0]!.Owner ? "ours" : "theirs")}.");
            }
        }

        private void ReportScene(UnitInstance unit, Vec2 destination)
        {
            _console.Info("Preview",
                $"{unit.Def.DisplayName} at ({unit.Position.X:0.0},{unit.Position.Y:0.0}) " +
                $"facing {unit.Facing.Degrees:0.0}°, {unit.Footprint.Width:0}x{unit.Footprint.Depth:0} m, " +
                $"to ({destination.X:0.0},{destination.Y:0.0}).");

            foreach (UnitInstance other in _battle.UnitsOnField())
            {
                if (ReferenceEquals(other, unit)) continue;
                if (other.Owner != unit.Owner) continue;

                _console.Info("Preview",
                    $"  {other.Def.DisplayName} at ({other.Position.X:0.0},{other.Position.Y:0.0}) " +
                    $"facing {other.Facing.Degrees:0.0}°, {other.Footprint.Width:0}x{other.Footprint.Depth:0} m.");
            }
        }

        private void ReportPreview(string name, UnitInstance unit, Plan plan)
        {
            PathResult path = plan.Path;

            if (!path.Found)
            {
                _console.Info("Preview", $"{unit.Def.DisplayName} — {name}: {path.FailureDetail} [{path.Failure}].");
                return;
            }

            float seconds = Marching.SecondsToWalk(_battle, unit, path.Waypoints, plan.Hold);

            var waypoints = new System.Text.StringBuilder();
            for (int i = 0; i < path.Waypoints.Count; i++)
            {
                if (i > 0) waypoints.Append(" -> ");

                Vec2 point = path.Waypoints[i];
                waypoints.Append($"({point.X:0},{point.Y:0})");

                if (plan.Hold != null && i < plan.Hold.Length && plan.Hold[i].HasValue)
                    waypoints.Append($"[{plan.Hold[i]!.Value.Degrees:0}°]");
            }

            _console.Info("Preview",
                $"{unit.Def.DisplayName} — {name}: {path.Waypoints.Count} waypoints, " +
                $"{path.Distance:0} m, {seconds:0} s" +
                (plan.PressedThrough ? ", presses through its own." : ".") +
                $"  cost to work out: {plan.Effort}." +
                $"  {waypoints}");
        }

        /// <summary>
        /// How near a regiment's own ground a right-drag has to land before it
        /// means "change front" rather than "march", in metres.
        /// </summary>
        private const float TurnInPlaceMetres = 30f;

        /// <summary>
        /// One regiment's route, worked out and not yet acted on.
        /// </summary>
        /// <remarks>
        /// The split exists so that a wing can be planned all at once. Working
        /// a route out reads the battle and writes nothing to it; giving the
        /// order, writing the route, moving the overlay and saying any of it are
        /// all changes to something shared. Keeping them in one method meant the
        /// only way to plan a wing was in a queue.
        /// </remarks>
        private readonly struct Worked
        {
            public readonly UnitInstance Unit;

            /// <summary>Where the player pointed.</summary>
            public readonly Vec2 Asked;

            /// <summary>Where it can actually stand, which is not always the same.</summary>
            public readonly Vec2 Destination;

            public readonly Facing? Bearing;

            /// <summary>A change of front where it stands, with no march in it.</summary>
            public readonly bool TurningOnly;

            /// <summary>Whether the placement search found the ground, or the fallback did.</summary>
            public readonly bool Placed;

            public readonly float Clearance;
            public readonly HexLayout SearchLayout;
            public readonly IPathfinder Pathfinder;
            public readonly Facing WillArriveOn;
            public readonly Plan Plan;
            public readonly double Milliseconds;

            /// <summary>What the planner said while nobody could listen.</summary>
            public readonly HeldBattleLog Said;

            public Worked(
                UnitInstance unit, Vec2 asked, Vec2 destination, Facing? bearing, bool turningOnly,
                bool placed, float clearance, HexLayout searchLayout, IPathfinder pathfinder,
                Facing willArriveOn, Plan plan, double milliseconds, HeldBattleLog said)
            {
                Unit = unit;
                Asked = asked;
                Destination = destination;
                Bearing = bearing;
                TurningOnly = turningOnly;
                Placed = placed;
                Clearance = clearance;
                SearchLayout = searchLayout;
                Pathfinder = pathfinder;
                WillArriveOn = willArriveOn;
                Plan = plan;
                Milliseconds = milliseconds;
                Said = said;
            }
        }

        /// <summary>An order being worked out while the game carries on drawing.</summary>
        /// <remarks>
        /// <para>
        /// <b>Why the simulation stops while this runs.</b> Working a route out
        /// reads the whole battle - every position, every shape, the spatial
        /// index - and a tick would be writing to all three underneath it. The
        /// wing order got away without a rule because it began and finished
        /// inside one frame, with nothing in between to move anybody. Across
        /// frames that is no longer true, so the clock is held until the plan
        /// lands.
        /// </para>
        /// <para>
        /// What that buys is the whole point: the frame does not stop. Drawing,
        /// the camera, the panel and the selection all keep their sixty a
        /// second while a dear order is worked out, where before the game hung
        /// for a second on the frame that ordered it. The battle stands still
        /// for a tenth of a second instead.
        /// </para>
        /// </remarks>
        private sealed class RouteWork
        {
            public System.Threading.Tasks.Task Task = null!;
            public Worked[] Results = null!;

            /// <summary>Who each result is for, so a later order can find it.</summary>
            public UnitInstance[] Units = null!;

            /// <summary>
            /// Set when a newer order re-targets that regiment. M80: the route
            /// being worked out is no longer the one anybody asked for, so it
            /// is abandoned where it has not started and thrown away where it
            /// has. Written on the drawing thread, read on the workers, so
            /// <c>volatile</c> - an ordinary bool may never be re-read inside
            /// the loop that polls it.
            /// </summary>
            public volatile bool[] Dropped = null!;
            public bool Quiet;
            public bool AsAWing;
            public bool Together;
            /// <summary>Null unless the order carried a status line.</summary>
            public string Status;
            public System.Diagnostics.Stopwatch Spent = null!;
            public int Frames;
        }

        /// <summary>Null unless a plan is out with a worker.</summary>
        private RouteWork _working;

        /// <summary>Whether a plan is out with a worker and the clock is held.</summary>
        private bool WorkingOutARoute => _working != null;

        /// <summary>
        /// Takes delivery of a finished plan, if one has finished.
        /// </summary>
        /// <remarks>
        /// Applying is main-thread work by construction - it gives the order,
        /// writes the overlay and replays what the planner said into the
        /// console - so it happens here rather than on the worker, in the
        /// order the regiments were given, which is what keeps a recording
        /// readable.
        /// </remarks>
        private void CollectFinishedRoutes()
        {
            if (_working == null) return;

            _working.Frames++;

            if (!_working.Task.IsCompleted) return;

            RouteWork work = _working;

            // Cleared before applying rather than after. ApplyRoute gives an
            // order, and an order is a thing that could ask for another plan.
            _working = null;

            work.Spent.Stop();

            // A faulted worker must not be swallowed: a plan that threw is a
            // regiment that will never move, and the recording is the only
            // place anybody would find out.
            if (work.Task.IsFaulted)
            {
                _console.Blocked("Path",
                    $"Working out {work.Results.Length} route(s) threw: " +
                    $"{work.Task.Exception?.GetBaseException().Message}");
                return;
            }

            int thrownAway = 0;

            for (int i = 0; i < work.Results.Length; i++)
            {
                // M80. Either it was never started or it stopped part way, and
                // either way the order it was answering has been replaced.
                if (work.Dropped[i]) { thrownAway++; continue; }

                ApplyRoute(work.Results[i], work.Quiet);
            }

            if (thrownAway > 0)
                _console.Info("Path",
                    $"{thrownAway} of {work.Results.Length} route(s) thrown away - " +
                    "a newer order asked those regiments for somewhere else.");

            if (work.AsAWing)
            {
                _console.Info("Group",
                    $"{work.Results.Length} routes worked out in " +
                    $"{work.Spent.Elapsed.TotalMilliseconds:0.0} ms " +
                    $"({(work.Together ? $"all at once on {System.Environment.ProcessorCount} cores" : "one after another")}), " +
                    $"off the drawing thread over {work.Frames} frame(s) - the clock waited, the picture did not.");
            }

            if (work.Status != null) _status = work.Status;
        }

        /// <summary>
        /// Waits for an outstanding plan, because what comes next would write
        /// to the battle the worker is reading.
        /// </summary>
        /// <remarks>
        /// This is the one place the old freeze can still happen, and it is the
        /// right place for it: a second order given while the first is still
        /// being worked out. Rare by hand, and the alternative - planning
        /// against a field another order is changing - is the class of bug that
        /// took four attempts to find last time.
        /// </remarks>
        /// <summary>
        /// Marks anything still being worked out for these regiments as no
        /// longer wanted. M80.
        /// </summary>
        /// <remarks>
        /// By regiment rather than wholesale: a click that orders one wing must
        /// not silently drop an order given to a different one a moment ago.
        /// What supersedes a plan is a newer order for that same regiment.
        /// </remarks>
        private void Supersede(IReadOnlyList<UnitInstance> wing)
        {
            if (_working == null || wing == null) return;

            for (int i = 0; i < _working.Units.Length; i++)
            {
                for (int w = 0; w < wing.Count; w++)
                {
                    if (wing[w] == null || _working.Units[i].Id != wing[w].Id) continue;

                    _working.Dropped[i] = true;
                    break;
                }
            }
        }

        private void SettleRoutes()
        {
            if (_working == null) return;

            try { _working.Task.Wait(); }
            catch (System.AggregateException) { /* said by CollectFinishedRoutes */ }

            CollectFinishedRoutes();
        }

        /// <summary>
        /// Hands a set of orders to a worker and returns without waiting.
        /// </summary>
        private void WorkOutRoutes(
            IReadOnlyList<UnitInstance> wing, Vec2[] wanted, Facing? bearing,
            bool quiet, bool asAWing, string status)
        {
            // Written on every real order, not only on previews behind a
            // toggle, and this is why: the 7,7x route of 25 August was given in
            // ordinary play with the comparison off, so the recording had the
            // route and its cost and no way to rebuild the arrangement that
            // produced it. A diagnostic that needs somebody to still have the
            // app open and reproduce on demand is not a diagnostic. It costs
            // one line per regiment per order against logs already running to
            // hundreds of kilobytes, which is nothing set against being unable
            // to answer "what happened?" at all.
            ReportOrderedScene(wing, wanted);

            // M80. Anything still being worked out for a regiment this order
            // re-targets is no longer wanted: the player has asked for
            // somewhere else. Marked before the join, so a search already
            // running sees it at its next poll and stops instead of spending a
            // frame on an answer that is discarded on arrival.
            Supersede(wing);

            // A plan already out is still joined - not because its answer is
            // wanted, but because the worker reads the battle that what comes
            // next writes to. Two of them reading while one may finish and give
            // an order is the same hazard the clock is held for.
            SettleRoutes();

            // Every body's rectangle worked out before the routes rather than
            // during them. It is cached behind a flag that a move clears, and
            // nothing moves while a plan is out - but a lazy write to a struct
            // being read on another thread is a torn read, so the write is made
            // to happen first and then never again.
            foreach (UnitInstance unit in _battle.UnitsOnField()) _ = unit.Shape;

            var units = new UnitInstance[wing.Count];
            for (int i = 0; i < wing.Count; i++) units[i] = wing[i];

            var results = new Worked[units.Length];
            var dropped = new bool[units.Length];

            // Instant movement teleports each regiment as its order is applied,
            // so a later plan in the same wing is made against a field the
            // earlier ones have already moved on. That is a real dependency
            // between the orders and it is left alone.
            bool together = _options.PlanTheWingTogether && !_options.MoveInstantly && units.Length > 1;

            var work = new RouteWork
            {
                Results = results,
                Units = units,
                Dropped = dropped,
                Quiet = quiet,
                AsAWing = asAWing,
                Together = together,
                Status = status,
                Spent = System.Diagnostics.Stopwatch.StartNew(),
            };

            work.Task = System.Threading.Tasks.Task.Run(() =>
            {
                PlanMarker.Begin();

                if (together)
                {
                    // One regiment per chunk. What an order costs is wildly
                    // uneven - most are a fraction of a millisecond and a few
                    // are sixty - so handing a worker a contiguous block of the
                    // wing lets one worker draw several of the dear ones while
                    // the rest finish early with nothing left to take.
                    System.Threading.Tasks.Parallel.ForEach(
                        System.Collections.Concurrent.Partitioner.Create(0, units.Length, 1),
                        range =>
                        {
                            for (int i = range.Item1; i < range.Item2; i++)
                            {
                                if (dropped[i]) continue;

                                int mine = i;
                                Marching.GiveUpNow = () => dropped[mine];
                                try { results[i] = WorkOutRoute(units[i], wanted[i], bearing); }
                                finally { Marching.GiveUpNow = null; }
                            }
                        });
                }
                else
                {
                    for (int i = 0; i < units.Length; i++)
                    {
                        if (dropped[i]) continue;

                        int mine = i;
                        Marching.GiveUpNow = () => dropped[mine];
                        try { results[i] = WorkOutRoute(units[i], wanted[i], bearing); }
                        finally { Marching.GiveUpNow = null; }
                    }
                }

                PlanMarker.End();
            });

            _working = work;

            // A plan that is already done - most of them are - is taken now
            // rather than a frame later, so an ordinary order still lands on
            // the frame that gave it and the common case is unchanged.
            if (work.Task.IsCompleted) CollectFinishedRoutes();
        }

        private void PlanRoute(UnitInstance unit, Vec2 destination, Facing? bearing = null, bool quiet = false)
        {
            if (unit == null) return;

            if (_options.MoveInstantly)
            {
                // Teleporting is a change to the field, so it cannot be worked
                // out beside anything. Left exactly as it was.
                SettleRoutes();
                ApplyRoute(WorkOutRoute(unit, destination, bearing), quiet);
                return;
            }

            WorkOutRoutes(
                new[] { unit }, new[] { destination }, bearing,
                quiet, asAWing: false, status: null);
        }

        /// <summary>
        /// Works out where a regiment can stand and how it gets there, and
        /// changes nothing.
        /// </summary>
        /// <remarks>
        /// Safe to call on several threads at once, and that is the whole point
        /// of it, so the rules it obeys are worth stating. It reads positions,
        /// shapes and terrain; it builds its own pathfinder rather than sharing
        /// one; it is given its own log to talk into rather than the console;
        /// and it does not touch the overlay, the status line or the order.
        /// </remarks>
        private Worked WorkOutRoute(UnitInstance unit, Vec2 asked, Facing? bearing)
        {
            // Right-drag on a regiment where it already stands is how you change
            // front without going anywhere. Without this there is no way to
            // order it at all: the route from a point to itself is empty, the
            // pathfinder rightly refuses it, and the order was thrown away with
            // the march — so a regiment caught in the flank could never be told
            // to come about.
            if (bearing.HasValue && Vec2.Distance(unit.Position, asked) <= TurnInPlaceMetres)
            {
                return new Worked(
                    unit, asked, asked, bearing, turningOnly: true, placed: true, clearance: 0f,
                    searchLayout: default, pathfinder: null, willArriveOn: bearing.Value,
                    plan: default, milliseconds: 0d, said: new HeldBattleLog());
            }

            // A unit is a point at its centre by default: if the centre can be
            // there, the unit can. Ticking 'Route by unit width' opts into
            // width-aware routing, which refuses gaps the regiment could not
            // physically fit down — correct, but it reads as broken when a line
            // declines to approach a shoreline it could obviously stand beside.
            float clearance = _options.RespectUnitWidth
                ? unit.Footprint.Width * 0.5f
                : HexPathfinder.DefaultClearanceMetres;

            // Player orders go where they were pointed. Only the AI is allowed
            // to decide a longer way round is worth it because the going is
            // better — a human who marches into a swamp meant to.
            //
            // One each rather than one shared: a pathfinder carries its own
            // working memory, and a wing planned at once would have several
            // regiments writing into it together.
            IPathfinder pathfinder = _options.RouteLikeAi
                ? new HexPathfinder(_map.Terrain, _trueMovement, _terrainCatalogue, clearanceMetres: clearance)
                : new DirectPathfinder(_map.Terrain, _trueMovement, _terrainCatalogue, clearanceMetres: clearance);

            HexLayout searchLayout = pathfinder is DirectPathfinder direct
                ? direct.SearchLayout
                : ((HexPathfinder)pathfinder).SearchLayout;

            // Aim for the best ground near the click that the regiment could
            // actually stand on. Every map is ringed with impassable country and
            // half the good ground already has somebody on it, so a click asks
            // for a goal no route can end at more often than not. The pathfinder
            // rightly refused, and the regiment sat there while the player
            // clicked at it. Ordering a march to the sea should walk to the
            // beach, not decline to move.
            //
            // Same search the rules use when a march stalls, so a click and a
            // re-plan agree about where "there" is.
            bool placed = OrderSystem.TryFindPlacement(
                _battle, unit, asked, bearing ?? unit.Facing, out Vec2 stand);

            Vec2 destination = placed
                ? stand
                : OrderSystem.NearestReachable(_battle, unit, asked, unit.Position);

            // M10: the straight line first, the search only if something is
            // genuinely in the way. Most player orders across open ground now
            // never reach the pathfinder at all.
            //
            // The front is handed over rather than left to the planner to guess,
            // because the order does not exist yet: GiveOrder is called later,
            // and until it is, unit.OrderFacing still holds the *previous*
            // order's front. The search prices the wheel onto the front it is
            // given, so guessing it wrong buys the wrong wheel with real ground.
            // The same expression GiveOrder itself will use — the drawn bearing
            // if there is one, otherwise the line of march.
            Facing willArriveOn = bearing ?? Marching.AlongTheLine(unit.Position, destination, unit.Facing);

            // Timed, because "it lags when you move several at once" is a
            // question about milliseconds and the recording could only ever
            // answer it in legs. Legs were a stand-in for a clock, and a bad one:
            // measured on the bench, the same leg count cost between six and
            // nineteen microseconds depending on what else was running (W5).
            //
            // The clock is per regiment and the marker is not: a profiler marker
            // begun on a worker thread has no scope to close on the main one, so
            // the wing is marked once around the whole batch by whoever ordered
            // it.
            var said = new HeldBattleLog();
            var spent = System.Diagnostics.Stopwatch.StartNew();

            Plan plan = Marching.PlanTo(
                _battle, unit, pathfinder, destination, said, arriveOn: willArriveOn);

            spent.Stop();

            return new Worked(
                unit, asked, destination, bearing, turningOnly: false, placed: placed,
                clearance: clearance, searchLayout: searchLayout, pathfinder: pathfinder,
                willArriveOn: willArriveOn, plan: plan,
                milliseconds: spent.Elapsed.TotalMilliseconds, said: said);
        }

        /// <summary>
        /// Does something about a route that has been worked out: says it, draws
        /// it, and gives the order.
        /// </summary>
        /// <remarks>
        /// The main thread only. Everything here touches something shared — the
        /// console, the overlay, the status line, and the regiment's own order.
        /// </remarks>
        private void ApplyRoute(in Worked worked, bool quiet)
        {
            UnitInstance unit = worked.Unit;
            if (unit == null) return;

            Facing? bearing = worked.Bearing;
            Vec2 destination = worked.Destination;

            if (worked.TurningOnly)
            {
                unit.GiveOrder(UnitOrder.Face(bearing!.Value), unit.Position);

                float toTurn = Facing.AbsoluteDelta(unit.Facing, bearing.Value) * Mathf.Rad2Deg;
                float rate = unit.Def.Get(UnitAttributes.TurnRate);

                _console.Decision("Move",
                    $"{unit.Def.DisplayName} changing front to {bearing.Value.Degrees:0}° where it stands — " +
                    $"{toTurn:0}° at {rate:0}°/s" +
                    (unit.EnemiesInContact > 0 ? ", and slower with the enemy among it." : "."),
                    unit.Id);

                if (!quiet) _status = $"{unit.Def.DisplayName} coming about — {toTurn:0}°.";
                return;
            }

            if (worked.Placed && Vec2.Distance(destination, worked.Asked) > 1f)
                _console.Info("Path",
                    $"{unit.Def.DisplayName} is aiming {Vec2.Distance(destination, worked.Asked):0} m off that " +
                    $"point — the ground there is taken or impassable. It will face " +
                    $"{Facing.Towards(unit.Position, destination).Degrees:0}° for the ground it can stand on, " +
                    $"not {Facing.Towards(unit.Position, worked.Asked).Degrees:0}° for the point clicked.",
                    unit.Id);

            // Whatever the planner said while it was working, said now — in its
            // own order, and in the wing's order rather than in whichever order
            // the threads happened to finish.
            worked.Said.ReplayInto(_console);

            Plan plan = worked.Plan;

            // The same comparison the preview draws, on a march that is actually
            // happening. Single orders only — see DrawEveryPlanner.
            if (!quiet && _options.ComparePlannersOnOrders)
            {
                _overlay.ClearRoutePreview();

                if (_options.ShowRouteCandidates)
                    _overlay.SetRouteCandidates(RouteSearch.DebugCandidatePlaces(_battle, unit, destination));

                DrawEveryPlanner(
                    unit, destination, worked.Pathfinder, worked.WillArriveOn, report: true, already: plan);
            }

            PathResult path = plan.Path;

            // The route overlay and the drawn line show one march. Ordering a
            // wing would have six of them fight over the same LineRenderer and
            // leave whichever finished last, which is worse than showing none.
            if (!quiet)
            {
                _overlay.SetSearchCells(path.SearchCells, worked.SearchLayout);
                _overlay.SetRawPath(path.SearchCells, worked.SearchLayout);
            }

            if (!path.Found)
            {
                if (!quiet) _pathLine.positionCount = 0;

                // The whole point of the failure reasons: each of these has a
                // completely different fix, and "no route" would hide which.
                _console.Blocked("Path",
                    $"{unit.Def.DisplayName} cannot reach that point. {path.FailureDetail} [{path.Failure}]",
                    unit.Id);

                if (path.Failure == PathFailure.GoalTooTight)
                    _console.Decision("Path",
                        "Untick 'Route by unit width' to treat the unit as a point at its centre.", unit.Id);

                _status = $"No route: {path.FailureDetail}";
                return;
            }

            if (!quiet) DrawPath(path);

            float seconds = path.SecondsAt(unit.BaseSpeed);

            // Every order, not only previews, and quiet ones too. What a plan
            // cost to work out is the one thing a recording could never answer,
            // so it was measured by hand four times over in one afternoon
            // before it was simply written down.
            _console.Info("Cost",
                $"{unit.Def.DisplayName} planned by {RoutePlanners.Default.Name} " +
                $"in {worked.Milliseconds:0.0} ms: {plan.Effort}.", unit.Id);

            // What the same order would have cost on an empty field. A route
            // is only judgeable against the walk it replaces, and a recording
            // that omits it cannot answer "was that detour reasonable?" - which
            // is the question every screenshot of a bad route actually asks.
            float straight = path.Waypoints.Count > 0
                ? Marching.SecondsToWalk(
                    _battle, unit, new[] { unit.Position, path.Waypoints[path.Waypoints.Count - 1] }, null)
                : 0f;
            float took = Marching.SecondsToWalk(_battle, unit, path.Waypoints, plan.Hold);

            if (!quiet)
                _console.Decision("Path",
                    $"{unit.Def.DisplayName} route ({(_options.RouteLikeAi ? "fastest" : "direct")}): " +
                    $"{path.Distance:0} m walked, {path.EffectiveDistance:0} m effective, " +
                    $"{seconds / 60f:0.0} turns at {unit.BaseSpeed:0.00} m/s. " +
                    (straight > 1f
                        ? $"{took / straight:0.0}x what walking straight there would cost. "
                        : string.Empty) +
                    $"{path.SearchCells.Count} cells reduced to {path.Waypoints.Count} waypoints, " +
                    $"{path.CellsExplored} explored, {worked.Clearance:0} m clearance.",
                    unit.Id);

            if (_options.MoveInstantly)
            {
                MoveInstantly(unit, path);
            }
            else
            {
                // Record it as an order, not just a route, so the order system
                // can keep it honest — following a target, or reacting to what
                // turns up on the way.
                UnitOrder drawn =
                    UnitOrder.MoveTo(destination, _options.WheelBeforeMarching, bearing: bearing);

                if (_options.PlanThenFire)
                {
                    // [M143]. The route goes in the book and on the field; the
                    // regiment does not move until the turn is ended. Planned
                    // there rather than reusing the plan just taken, because the
                    // book plans against the orders already drawn and this one
                    // did not.
                    _book.Draw(_battle, unit, drawn, PathfinderFor(unit), _console);
                }
                else
                {
                    unit.GiveOrder(drawn, unit.Position);
                    unit.Route = plan.ToRoute(_options.WheelBeforeMarching);
                }

                // Against the front it will actually hold, not against the line
                // of march. Those are different whenever a bearing was drawn,
                // and reporting the wrong one had the console promising a wheel
                // that was never going to happen.
                float offBy = Facing.AbsoluteDelta(unit.Facing, unit.OrderFacing) * Mathf.Rad2Deg;
                float turnRate = unit.Def.Get(UnitAttributes.TurnRate);
                float wheelSeconds = offBy / Mathf.Max(1f, turnRate);

                if (quiet)
                {
                    _console.Info("Move",
                        $"{unit.Def.DisplayName} marching {path.Distance:0} m with the wing.", unit.Id);
                }
                else
                {
                    // The front it is heading for, in map degrees, as well as how
                    // far round that is from here. Only the second was recorded,
                    // and it is measured from wherever the regiment happens to be
                    // pointing — so two identical orders given a moment apart
                    // print different numbers, and a report of "it took a
                    // different bearing" could not be checked against the log at
                    // all.
                    // The raw geometry as well as the conclusion. Printing only
                    // OrderFacing meant the log was quoting the very code under
                    // suspicion back at itself — if the front is being worked
                    // out wrongly, a line that reports the front cannot show it.
                    // From here, to there, and the bearing those two imply.
                    _console.Info("Move",
                        $"{unit.Def.DisplayName} marching{(_options.WheelBeforeMarching ? " (wheeling first)" : "")} " +
                        $"from ({unit.Position.X:0},{unit.Position.Y:0}) to ({destination.X:0},{destination.Y:0}) — " +
                        $"that line is {Facing.Towards(unit.Position, destination).Degrees:0}°, " +
                        $"and it will face {unit.OrderFacing.Degrees:0}° " +
                        $"({(bearing.HasValue ? "bearing drawn" : "the way it is going")}). " +
                        $"{offBy:0}° off at {turnRate:0}°/s — {wheelSeconds:0} ticks to come round.",
                        unit.Id);

                    // A big wheel is the most interesting thing about to happen, and
                    // at speed it is over in a second of wall time. Say so, and
                    // suggest slowing down rather than leaving it to be missed.
                    // An eighth is the old x4, which is where this threshold
                    // was set and where a wheel stops being watchable. [M128]
                    // moved the numbers under it, not the judgement.
                    if (offBy > 45f && _options.TimeScale > 0.125f)
                        _console.Decision("Move",
                            $"That is a {offBy:0}° wheel taking {wheelSeconds:0} ticks — at " +
                            $"{_options.SpeedLabel} it will be over in " +
                            $"{wheelSeconds / _options.BattleSecondsPerSecond:0.0} s. " +
                            "Press '-' or use '.' to step through it.",
                            unit.Id);
                }
            }

            if (!quiet)
                _status = $"{unit.Def.DisplayName}: {path.Distance:0} m, {seconds / 60f:0.0} turns.";
        }


        private void DrawPath(PathResult path)
        {
            _pathLine.positionCount = path.Waypoints.Count;

            for (int i = 0; i < path.Waypoints.Count; i++)
                _pathLine.SetPosition(i, new Vector3(path.Waypoints[i].X, path.Waypoints[i].Y, -1f));
        }

        /// <summary>
        /// Teleports a unit along its route, turning it to face the way it
        /// arrived.
        /// </summary>
        /// <remarks>
        /// Not a substitute for the tick loop — it is how deployments, terrain
        /// effects and clearance can be tried by hand before movement exists.
        /// Once units genuinely walk, this becomes "skip the animation".
        /// </remarks>
        private void MoveInstantly(UnitInstance unit, PathResult path)
        {
            Vec2 arrival = path.Waypoints[path.Waypoints.Count - 1];

            Facing facing = unit.Facing;
            if (path.Waypoints.Count >= 2)
            {
                Vec2 previous = path.Waypoints[path.Waypoints.Count - 2];
                if (!(arrival - previous).IsNearZero)
                    facing = Facing.Towards(previous, arrival);
            }

            unit.Position = arrival;
            unit.Facing = facing;

            TerrainDef ground = _battle.TerrainAt(arrival);

            _console.Info("Move",
                $"{unit.Def.DisplayName} jumped to the end of its route, now on {ground.DisplayName} facing {facing}.",
                unit.Id);
        }

        private static Color ColourFor(PlayerId player) => ArmyColours[Mathf.Abs(player.Value) % ArmyColours.Length];

        /// <summary>Reads back the formation keys, e.g. "1 Line, 2 Column".</summary>
        private string FormationKeyHint()
        {
            if (_formations == null) return "number keys";

            var parts = new List<string>();
            for (int i = 0; i < _formations.Count && i < 9; i++)
                parts.Add($"{i + 1} {_formations.All[i].DisplayName}");

            return string.Join(", ", parts);
        }

        // ---- Interface ------------------------------------------------------

        /// <summary>
        /// The options panel, sized to whatever room is left above the console.
        /// </summary>
        /// <remarks>
        /// It was a fixed 300 px, which meant every option added past that
        /// height silently vanished off the bottom — indistinguishable from the
        /// feature having been deleted. Between this and the scroll view inside,
        /// adding an option can no longer hide an existing one.
        /// </remarks>
        private Rect OptionsRect =>
            new Rect(Screen.width - 290, 76, 280, Mathf.Max(220f, Screen.height - 316f));
        private Rect ConsoleRect => new Rect(10, Screen.height - 230, Screen.width - 310, 220);

        /// <summary>The status and hint bar, which now carries a button.</summary>
        private Rect TopBarRect => new Rect(10, 10, Screen.width - 20, 58);

        private bool PointerOverPanels()
        {
            // GUI space has y growing downward; world clicks use screen space.
            var point = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);

            // The top bar is always up, and pressing Bind should not also drag a
            // selection box out from underneath the button.
            if (TopBarRect.Contains(point)) return true;

            if (!_options.Visible) return false;

            return OptionsRect.Contains(point) || ConsoleRect.Contains(point);
        }

        /// <summary>
        /// The one control that is not part of the harness: end the turn and
        /// let every drawn order go.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>[M143].</b> Drawn before the harness panels and outside every
        /// toggle they answer to, because F1 and F2 turn the debug interface on
        /// and off and this is not debug interface - it is how the game is
        /// played. A player who has hidden the harness must still be able to
        /// end their turn.
        /// </para>
        /// <para>
        /// Bottom centre, wide, and it says what is waiting. Its own corner of
        /// the screen rather than a row in the bar, so nothing the harness does
        /// to its own layout can move it.
        /// </para>
        /// </remarks>
        private void DrawEndTurn()
        {
            if (!_options.PlanThenFire || _battle == null) return;

            const float width = 280f;
            const float height = 46f;

            var area = new Rect((Screen.width - width) * 0.5f, Screen.height - height - 24f, width, height);

            bool resolving = _resolving > 0;

            GUI.enabled = !resolving;

            string face = resolving
                ? $"resolving... {_resolving} ticks left"
                : _book.Count == 0
                    ? "End turn (no orders)"
                    : $"End turn - {_book.Count} order{(_book.Count == 1 ? string.Empty : "s")}";

            if (GUI.Button(area, face)) EndTheTurn();

            GUI.enabled = true;

            // A drawn order can be taken back until the turn is ended, and a
            // player needs to be told that without reading a manual.
            if (!resolving && _book.Count > 0)
            {
                var clear = new Rect(area.x + area.width + 8f, area.y, 110f, height);

                if (GUI.Button(clear, "Clear orders"))
                {
                    _book.RubEverything();
                    _status = "Orders cleared.";
                }
            }
        }

        /// <summary>Gives every drawn order and runs exactly one turn.</summary>
        /// <remarks>
        /// Exactly one. A turn that ran until the orders were finished would be
        /// a different length every time and would make a simultaneous turn
        /// impossible to reason about - both sides have to be resolving the
        /// same window.
        /// </remarks>
        private void EndTheTurn()
        {
            int given = _book.Fire(_battle, _console);

            _resolving = BattleClock.TicksPerTurn;
            _tickAccumulator = 0f;
            _options.Running = true;

            _console.Decision("Turn",
                $"Turn {_clock.Turn} ends - {given} order{(given == 1 ? string.Empty : "s")} given, " +
                $"resolving {BattleClock.TicksPerTurn} ticks.",
                UnitId.None);

            _status = given == 0
                ? "Turn ended with no orders."
                : $"Turn ended - {given} regiment{(given == 1 ? string.Empty : "s")} on the move.";
        }

        private void OnGUI()
        {
            _guiClock.Start();
            GuiMarker.Begin();

            try
            {
                DrawTheInterface();
            }
            finally
            {
                GuiMarker.End();
                _guiClock.Stop();
            }
        }

        private void DrawTheInterface()
        {
            if (_error != null)
            {
                GUI.Box(new Rect(10, 10, Screen.width - 20, 110), string.Empty);
                GUI.Label(new Rect(20, 18, Screen.width - 40, 100), $"Could not load the battle:\n\n{_error}");
                return;
            }

            DrawEndTurn();

            // Every panel in the harness shares this, so the whole interface
            // thickens at once rather than the unit labels alone.
            GUI.skin.label.fontStyle = FontStyle.Bold;
            GUI.skin.label.fontSize = 13;
            GUI.skin.toggle.fontStyle = FontStyle.Bold;
            GUI.skin.button.fontStyle = FontStyle.Bold;

            GUI.Box(new Rect(10, 10, Screen.width - 20, 58), string.Empty);

            string clock = _clock != null
                ? $"[turn {_clock.Turn}  tick {_clock.TickInTurn}/{BattleClock.TicksPerTurn}  " +
                  $"{_options.SpeedLabel}{(_options.Running ? "" : "  PAUSED")}]   "
                : string.Empty;

            // Right of the status line rather than in the debug panel, because
            // the question it answers - is it running badly right now - is
            // asked while watching the battle, not while reading the panel.
            // The status line yields the width so a long status cannot run
            // underneath the digits.
            if (_options.ShowFrameRate)
            {
                _frameRate.Draw(new Rect(
                    Screen.width - 30f - FrameRate.Width, 16, FrameRate.Width, 22));
            }

            // Under the bar rather than in it: the bar's two rows are spoken
            // for, and a row that only appears when a toggle is on must not
            // reflow the two that are always there.
            if (_options.ShowFrameRate && _options.ExplainTheFrame)
            {
                _frameRate.DrawSplit(new Rect(
                    Screen.width - 10f - FrameRate.SplitWidth, 72f, FrameRate.SplitWidth, 22f));
            }

            float statusWidth = Screen.width - 40f
                - (_options.ShowFrameRate ? FrameRate.Width + 20f : 0f);

            GUI.Label(new Rect(20, 16, statusWidth, 22), clock + _status);
            GUI.Label(new Rect(20, 38, Screen.width - 40, 22),
                (_options.PreviewRouteMode
                    ? "PREVIEWING — R-click plans a route and draws it, nothing moves. Untick to give real orders.    "
                    : "L-click/drag select    R-click march, R-drag sets facing, R-click enemy attacks    ") +
                "B bind    1-4 reshape    QERT stance    V fog    G ghost    Space pause    . step    " +
                "+/- speed    Middle-drag pan    F1 debug    F2 harness" +
                (_options.Harness ? string.Empty : "  — OFF, game only"));

            DrawBondButton();

            DrawSelectionBox();
            DrawUnitLabels();
            DrawGhosts();

            if (!_options.Visible) return;

            ApplyUnitActions();

            if (_options.Draw(OptionsRect) && Primary != null && _hasDestination)
            {
                // Re-plan on any toggle, so the effect of a change is visible at
                // once rather than needing the click repeated. A toggle flipped
                // while previewing must re-preview, never re-order — the whole
                // point of the mode is that ticking boxes cannot move a unit.
                if (_options.PreviewRouteMode)
                    PreviewRoute(Primary, _lastDestination);
                else
                    PlanRoute(Primary, _lastDestination);
            }

            _console.Draw(ConsoleRect);
        }
    }
}
