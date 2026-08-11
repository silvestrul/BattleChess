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

        /// <summary>
        /// Depth a regiment is drawn and hit-tested at, however thin it really
        /// is. Cosmetic only — the rules always use the true footprint.
        /// </summary>
        public const float ClickableDepthMetres = 18f;

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

        private void Update()
        {
            if (_battle == null) return;

            if (Input.GetKeyDown(KeyCode.F1))
                _options.Visible = !_options.Visible;

            if (Input.GetKeyDown(KeyCode.B)) ToggleBond();

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

            TrackSelection();
            TrackOrders();
        }

        // ---- Mouse ------------------------------------------------------------

        /// <summary>How far the mouse must be dragged before it counts as setting a facing, in metres.</summary>
        private const float BearingDragMetres = 15f;

        /// <summary>How far a left drag must run before it counts as a box rather than a click, in metres.</summary>
        private const float BoxDragMetres = 12f;

        private bool _boxing;
        private Vec2 _boxFrom;

        private bool _ordering;
        private Vec2 _orderAt;

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
            }

            if (!_boxing) return;

            Vec2 here = MouseWorld();
            bool dragged = Vec2.Distance(here, _boxFrom) >= BoxDragMetres;

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
                if (_selection.Count == 0)
                {
                    _console.Blocked("Order", "Nothing selected — left-click a regiment first.");
                    _status = "Left-click a regiment, or drag a box round several.";
                    return;
                }

                Vec2 at = MouseWorld();
                UnitInstance clicked = UnitAt(at);

                // Right-clicking an enemy is an attack, which follows the target
                // as it moves rather than marching at where it used to be.
                if (clicked != null && clicked.Owner != Primary.Owner)
                {
                    AttackWithSelection(clicked);
                    return;
                }

                _ordering = true;
                _orderAt = at;
                return;
            }

            if (!_ordering) return;

            Vec2 drawn = MouseWorld() - _orderAt;
            bool far = drawn.Length >= BearingDragMetres;

            if (Input.GetMouseButton(1))
            {
                _status = far
                    ? $"Facing {Facing.FromVector(drawn).Degrees:0}° on arrival — release to confirm."
                    : "Drag to set the facing, or release to keep the current front.";

                return;
            }

            _ordering = false;

            _lastDestination = _orderAt;
            _hasDestination = true;

            MarchSelection(_orderAt, far ? Facing.FromVector(drawn) : (Facing?)null);
        }

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

            SetSelection(clicked.Bond == 0
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
            _selection.Count > 0 && Primary.Bond != 0 && _selection.TrueForAll(u => u.Bond == Primary.Bond);

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
                if (caught[i].Bond == 0) continue;

                foreach (UnitInstance member in Bond(caught[i].Bond))
                {
                    if (!caught.Contains(member)) caught.Add(member);
                }
            }

            SetSelection(caught);

            _status = caught.Count == 0
                ? "Nothing in the box."
                : $"{caught.Count} regiment(s) selected — right-click to order them.";
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

            Footprint real = unit.Footprint;
            var clickable = new Footprint(real.Width, Mathf.Max(real.Depth, ClickableDepthMetres));
            var shape = new OrientedRect(unit.Position, unit.Facing, clickable);

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

                unit.GiveOrder(UnitOrder.Attack(target.Id, _options.WheelBeforeMarching), unit.Position);

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

            foreach (UnitInstance unit in _selection)
                PlanRoute(unit, destination + (unit.Position - origin), bearing, quiet: true);

            _status = $"{_selection.Count} regiments marching, keeping their formation.";
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

                string wing = unit.Bond == 0 ? string.Empty : $"  [wing {unit.Bond}]";

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
            if (Vec2.Distance(here, _boxFrom) < BoxDragMetres) return;

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
            var lines = new List<(Vector3, Vector3)>();
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

        private void OnDisable() => _console.StopRecording();

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
            // Stamps every line the simulation writes, so a recorded log can be
            // read back against the clock rather than as an undated stream.
            _console.Tick = _clock.Tick;

            // Taken before the tick so "nobody dies" has something to put back.
            // Only gathered when the cheat is on — this runs sixty times a
            // second at speed.
            List<int> before = _options.NoCasualties ? StrengthSnapshot() : EmptyStrengths;

            _clock.Advance(_battle, _console);

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

        private void RefreshOverlayUnits()
        {
            var shapes = new List<OverlayUnit>();

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
                Footprint real = unit.Footprint;
                var clickable = new Footprint(real.Width, Mathf.Max(real.Depth, ClickableDepthMetres));

                if (new OrientedRect(unit.Position, unit.Facing, clickable).ContainsPoint(world))
                    return unit;
            }

            return null;
        }

        private void SetSelection(List<UnitInstance> units)
        {
            _selection.Clear();
            _selection.AddRange(units);

            _hasDestination = false;
            _pathLine.positionCount = 0;
            _overlay.SetSearchCells(null, default);
            _overlay.SetRawPath(null, default);

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

        private void PlanRoute(UnitInstance unit, Vec2 destination, Facing? bearing = null, bool quiet = false)
        {
            if (unit == null) return;

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

            // Aim for the nearest ground the unit could actually stand on. Every
            // map is ringed with impassable country, so a click near the edge —
            // or on the mountains themselves — asked for a goal no route can
            // ever end at. The pathfinder rightly refused, and the regiment just
            // sat there while the player clicked at it. Ordering a march to the
            // sea should walk to the beach, not decline to move.
            destination = OrderSystem.NearestReachable(_battle, unit, destination, unit.Position);

            PathResult path = pathfinder.FindPath(unit.Position, destination, unit.Def.Movement);

            // The route overlay and the drawn line show one march. Ordering a
            // wing would have six of them fight over the same LineRenderer and
            // leave whichever finished last, which is worse than showing none.
            if (!quiet)
            {
                _overlay.SetSearchCells(path.SearchCells, searchLayout);
                _overlay.SetRawPath(path.SearchCells, searchLayout);
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

            if (!quiet)
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
                unit.GiveOrder(
                    UnitOrder.MoveTo(destination, _options.WheelBeforeMarching, bearing: bearing), unit.Position);
                unit.Route = new MovementRoute(path.Waypoints, _options.WheelBeforeMarching);

                float offBy = Facing.AbsoluteDelta(unit.Facing, Facing.Towards(unit.Position, path.Waypoints[1])) * Mathf.Rad2Deg;
                float turnRate = unit.Def.Get(UnitAttributes.TurnRate);
                float wheelSeconds = offBy / Mathf.Max(1f, turnRate);

                if (quiet)
                {
                    _console.Info("Move",
                        $"{unit.Def.DisplayName} marching {path.Distance:0} m with the wing.", unit.Id);
                }
                else
                {
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

        private void OnGUI()
        {
            if (_error != null)
            {
                GUI.Box(new Rect(10, 10, Screen.width - 20, 110), string.Empty);
                GUI.Label(new Rect(20, 18, Screen.width - 40, 100), $"Could not load the battle:\n\n{_error}");
                return;
            }

            // Every panel in the harness shares this, so the whole interface
            // thickens at once rather than the unit labels alone.
            GUI.skin.label.fontStyle = FontStyle.Bold;
            GUI.skin.label.fontSize = 13;
            GUI.skin.toggle.fontStyle = FontStyle.Bold;
            GUI.skin.button.fontStyle = FontStyle.Bold;

            GUI.Box(new Rect(10, 10, Screen.width - 20, 58), string.Empty);

            string clock = _clock != null
                ? $"[turn {_clock.Turn}  tick {_clock.TickInTurn}/{BattleClock.TicksPerTurn}  " +
                  $"x{_options.TimeScale:0}{(_options.Running ? "" : "  PAUSED")}]   "
                : string.Empty;

            GUI.Label(new Rect(20, 16, Screen.width - 40, 22), clock + _status);
            GUI.Label(new Rect(20, 38, Screen.width - 40, 22),
                "L-click/drag select    R-click march, R-drag sets facing, R-click enemy attacks    " +
                "B bind    1-4 reshape    QERT stance    V fog    G ghost    Space pause    . step    " +
                "+/- speed    Middle-drag pan    F1 debug");

            DrawBondButton();

            DrawSelectionBox();
            DrawUnitLabels();
            DrawGhosts();

            if (!_options.Visible) return;

            ApplyUnitActions();

            if (_options.Draw(OptionsRect) && Primary != null && _hasDestination)
            {
                // Re-plan on any toggle, so the effect of a change is visible at
                // once rather than needing the click repeated.
                PlanRoute(Primary, _lastDestination);
            }

            _console.Draw(ConsoleRect);
        }
    }
}
