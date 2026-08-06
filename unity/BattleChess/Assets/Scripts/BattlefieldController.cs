using System.Collections.Generic;
using System.IO;
using BattleChess.Contracts;
using BattleChess.Rules;
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

        [Tooltip("Battle file to load from content/battles, without the extension.")]
        public string BattleName = "ford";

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

        private BattleClock _clock;
        private float _tickAccumulator;

        private readonly List<UnitView> _views = new List<UnitView>();
        private UnitInstance _selected;
        private Vec2 _lastDestination;
        private bool _hasDestination;
        private string _status = string.Empty;
        private string _error;

        private void Start()
        {
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

            _status = $"{_battle.Name} — click a regiment.";
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

        private void Update()
        {
            if (_battle == null) return;

            if (Input.GetKeyDown(KeyCode.F1))
                _options.Visible = !_options.Visible;

            HandleClockKeys();
            HandleFogKeys();
            AdvanceClock();
            HandleFormationKeys();
            HandleStanceKeys();

            // Render between the last two ticks rather than at the newest one,
            // so motion stays smooth and a wheel stays legible however fast the
            // clock is running.
            float alpha = Mathf.Clamp01(_tickAccumulator / BattleClock.SecondsPerTick);

            ApplyFog();

            foreach (UnitView view in _views)
                view.Render(alpha);

            RefreshOverlayUnits();

            // Clicks on the debug panels must not also fall through to the map.
            if (Input.GetMouseButtonDown(0) && !PointerOverPanels())
                HandleClick(_camera != null ? _camera.MouseWorldPosition() : default);
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
                _options.TimeScale = Mathf.Min(64f, _options.TimeScale * 2f);

            if (Input.GetKeyDown(KeyCode.Minus) || Input.GetKeyDown(KeyCode.KeypadMinus))
                _options.TimeScale = Mathf.Max(1f, _options.TimeScale * 0.5f);
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

            _tickAccumulator += Time.deltaTime * _options.TimeScale;

            // Cap the catch-up so a stall cannot fast-forward the battle.
            int budget = 8;

            while (_tickAccumulator >= BattleClock.SecondsPerTick && budget-- > 0)
            {
                _tickAccumulator -= BattleClock.SecondsPerTick;
                StepOnce();
            }

            if (budget <= 0) _tickAccumulator = 0f;
        }

        private void StepOnce()
        {
            _clock.Advance(_battle, _console);

            // Snapshot after every tick so the view has two states to
            // interpolate between.
            foreach (UnitView view in _views)
                view.CaptureTick();
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
            if (_selected == null || _formations == null) return;

            for (int i = 0; i < _formations.Count && i < 9; i++)
            {
                if (!Input.GetKeyDown(KeyCode.Alpha1 + i)) continue;

                FormationDef target = _formations.All[i];

                if (target.Key == _selected.FormationOrder.Key)
                {
                    _console.Info("Formation", $"{_selected.Def.DisplayName} is already in {target.DisplayName}.", _selected.Id);
                    return;
                }

                float widthBefore = _selected.Footprint.Width;
                float spent = _selected.AdoptFormation(target);
                float widthAfter = _selected.Footprint.Width;

                _console.Decision("Formation",
                    $"{_selected.Def.DisplayName} formed {target.DisplayName}: " +
                    $"frontage {widthBefore:0} m to {widthAfter:0} m, " +
                    $"organization -{spent:0.00} (now {_selected.Organization:0.00}).",
                    _selected.Id);

                if (_selected.Organization < 0.4f)
                    _console.Warning("Formation",
                        $"{_selected.Def.DisplayName} is badly disordered at {_selected.Organization:0.00} — " +
                        "further reshaping will leave it barely able to fight.", _selected.Id);

                _status = $"{_selected.Def.DisplayName} formed {target.DisplayName} — {widthAfter:0} m frontage, organization {_selected.Organization:0.00}.";

                // Re-plan, since the new shape can change what it fits through.
                if (_hasDestination) PlanRoute(_selected, _lastDestination);
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
            if (_selected == null) return;

            Stance? chosen = null;
            if (Input.GetKeyDown(KeyCode.Q)) chosen = Stance.Defend;
            else if (Input.GetKeyDown(KeyCode.E)) chosen = Stance.Advance;
            else if (Input.GetKeyDown(KeyCode.R)) chosen = Stance.Aggressive;
            else if (Input.GetKeyDown(KeyCode.T)) chosen = Stance.Evade;

            if (chosen == null || _selected.Stance == chosen.Value) return;

            _selected.Stance = chosen.Value;
            _selected.HeldUpBy = UnitId.None;

            _console.Decision("Stance",
                $"{_selected.Def.DisplayName} now standing on {chosen.Value} — {DescribeStance(chosen.Value)}",
                _selected.Id);

            _status = $"{_selected.Def.DisplayName}: {chosen.Value}.";
        }

        private static string DescribeStance(Stance stance) => stance switch
        {
            Stance.Defend => "halts on contact, even where it could force through.",
            Stance.Advance => "pushes through an enemy line where it is able to.",
            Stance.Aggressive => "closes with anything that comes near, and pursues.",
            Stance.Evade => "withdraws from anything that comes near.",
            _ => string.Empty
        };

        private void RefreshOverlayUnits()
        {
            var shapes = new List<(OrientedRect, float, float, float)>();

            // Show the width actually used for routing, so the overlay never
            // implies a constraint that is not being applied.
            foreach (UnitInstance unit in _battle.UnitsOnField())
            {
                float routingWidth = _options.RespectUnitWidth ? unit.Footprint.Width * 0.5f : 0f;
                shapes.Add((unit.Shape, routingWidth, unit.ZoneOfControl, LineOfSight.SightRange(_battle, unit)));
            }

            _overlay.SetUnits(shapes);
        }

        private void HandleClick(Vec2 world)
        {
            UnitInstance clicked = UnitAt(world);

            // Clicking an enemy while something is selected is an attack order,
            // which follows the target as it moves rather than marching at where
            // it used to be.
            if (clicked != null && _selected != null && clicked.Owner != _selected.Owner)
            {
                _selected.GiveOrder(UnitOrder.Attack(clicked.Id, _options.WheelBeforeMarching), _selected.Position);

                _console.Decision("Order",
                    $"{_selected.Def.DisplayName} ordered to attack {clicked.Def.DisplayName} " +
                    $"at {Vec2.Distance(_selected.Position, clicked.Position):0} m — will follow it.",
                    _selected.Id);

                _status = $"{_selected.Def.DisplayName} attacking {clicked.Def.DisplayName}.";
                return;
            }

            if (clicked != null)
            {
                Select(clicked);
                return;
            }

            if (_selected == null)
            {
                _console.Blocked("Order", "Nothing selected — click a regiment first.");
                _status = "Click a regiment to select it.";
                return;
            }

            _lastDestination = world;
            _hasDestination = true;
            PlanRoute(_selected, world);
        }

        /// <summary>
        /// Finds a unit under a point. Iterates in id order so an overlap always
        /// resolves the same way.
        /// </summary>
        private UnitInstance UnitAt(Vec2 world)
        {
            foreach (UnitInstance unit in _battle.UnitsOnField())
            {
                if (unit.Shape.ContainsPoint(world))
                    return unit;
            }

            return null;
        }

        private void Select(UnitInstance unit)
        {
            _selected = unit;
            _hasDestination = false;
            _pathLine.positionCount = 0;
            _overlay.SetSearchCells(null, default);
            _overlay.SetRawPath(null, default);

            foreach (UnitView view in _views)
                view.SetSelected(view.Unit == unit);

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

        private void PlanRoute(UnitInstance unit, Vec2 destination)
        {
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
            IPathfinder pathfinder = _options.RouteLikeAi
                ? new HexPathfinder(_map.Terrain, _trueMovement, _terrainCatalogue, clearanceMetres: clearance)
                : new DirectPathfinder(_map.Terrain, _trueMovement, _terrainCatalogue, clearanceMetres: clearance);

            HexLayout searchLayout = pathfinder is DirectPathfinder direct
                ? direct.SearchLayout
                : ((HexPathfinder)pathfinder).SearchLayout;

            PathResult path = pathfinder.FindPath(unit.Position, destination, unit.Def.Movement);

            _overlay.SetSearchCells(path.SearchCells, searchLayout);
            _overlay.SetRawPath(path.SearchCells, searchLayout);

            if (!path.Found)
            {
                _pathLine.positionCount = 0;

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

            DrawPath(path);

            float seconds = path.SecondsAt(unit.BaseSpeed);

            _console.Decision("Path",
                $"{unit.Def.DisplayName} route ({(_options.RouteLikeAi ? "fastest" : "direct")}): " +
                $"{path.Distance:0} m walked, {path.EffectiveDistance:0} m effective, " +
                $"{seconds / 60f:0.0} turns at {unit.BaseSpeed:0.00} m/s. " +
                $"{path.SearchCells.Count} cells reduced to {path.Waypoints.Count} waypoints, " +
                $"{path.CellsExplored} explored, {clearance:0} m clearance.",
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
                unit.GiveOrder(UnitOrder.MoveTo(destination, _options.WheelBeforeMarching), unit.Position);
                unit.Route = new MovementRoute(path.Waypoints, _options.WheelBeforeMarching);

                float offBy = Facing.AbsoluteDelta(unit.Facing, Facing.Towards(unit.Position, path.Waypoints[1])) * Mathf.Rad2Deg;
                float turnRate = unit.Def.Get(UnitAttributes.TurnRate);
                float wheelSeconds = offBy / Mathf.Max(1f, turnRate);

                _console.Info("Move",
                    $"{unit.Def.DisplayName} marching{(_options.WheelBeforeMarching ? " (wheeling first)" : "")}. " +
                    $"{offBy:0}° off the bearing at {turnRate:0}°/s — {wheelSeconds:0} ticks to come round.",
                    unit.Id);

                // A big wheel is the most interesting thing about to happen, and
                // at speed it is over in a second of wall time. Say so, and
                // suggest slowing down rather than leaving it to be missed.
                if (offBy > 45f && _options.TimeScale > 4f)
                    _console.Decision("Move",
                        $"That is a {offBy:0}° wheel taking {wheelSeconds:0} ticks — at x{_options.TimeScale:0} it will be over in " +
                        $"{wheelSeconds / _options.TimeScale:0.0} s. Press '-' or use '.' to step through it.",
                        unit.Id);
            }

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

        private Rect OptionsRect => new Rect(Screen.width - 230, 70, 220, 300);
        private Rect ConsoleRect => new Rect(10, Screen.height - 230, Screen.width - 250, 220);

        private bool PointerOverPanels()
        {
            if (!_options.Visible) return false;

            // GUI space has y growing downward; world clicks use screen space.
            var point = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
            return OptionsRect.Contains(point) || ConsoleRect.Contains(point);
        }

        private void OnGUI()
        {
            if (_error != null)
            {
                GUI.Box(new Rect(10, 10, Screen.width - 20, 110), string.Empty);
                GUI.Label(new Rect(20, 18, Screen.width - 40, 100), $"Could not load the battle:\n\n{_error}");
                return;
            }

            GUI.Box(new Rect(10, 10, Screen.width - 20, 52), string.Empty);

            string clock = _clock != null
                ? $"[turn {_clock.Turn}  tick {_clock.TickInTurn}/{BattleClock.TicksPerTurn}  " +
                  $"x{_options.TimeScale:0}{(_options.Running ? "" : "  PAUSED")}]   "
                : string.Empty;

            GUI.Label(new Rect(20, 16, Screen.width - 40, 20), clock + _status);
            GUI.Label(new Rect(20, 36, Screen.width - 40, 20),
                "Click select / march / attack    1-4 reshape    QERT stance    V fog    G ghost    " +
                "Space pause    . step    +/- speed    Right-drag pan    F1 debug");

            if (!_options.Visible) return;

            if (_options.Draw(OptionsRect) && _selected != null && _hasDestination)
            {
                // Re-plan on any toggle, so the effect of a change is visible at
                // once rather than needing the click repeated.
                PlanRoute(_selected, _lastDestination);
            }

            _console.Draw(ConsoleRect);
        }
    }
}
