using BattleChess.Contracts;
using BattleChess.Rules;
using UnityEngine;

namespace BattleChess.Unity
{
    /// <summary>
    /// Draws one regiment as the rectangle of ground it actually occupies.
    /// </summary>
    /// <remarks>
    /// Deliberately a rectangle rather than a token. A regiment is 80-110 m of
    /// frontage against 25 m terrain cells, so a marker would badly misrepresent
    /// how much ground an army holds — and once casualties start narrowing that
    /// frontage, watching a line thin out is the clearest read there is on how a
    /// battle is going.
    /// </remarks>
    public sealed class UnitView : MonoBehaviour
    {
        /// <summary>
        /// How much of a regiment's depth the type symbol may take up.
        /// </summary>
        /// <remarks>
        /// The symbol is drawn square and at a constant size while the plate
        /// beneath it stretches, which is the whole arrangement: a regiment in
        /// line is a hundred metres wide and eight deep, so anything stretched
        /// to fill that is a smear. Held square it stays readable, and it stays
        /// the same size as the regiment is fought down — the narrowing plate
        /// is what tells you it is dying, not a shrinking badge.
        /// </remarks>
        private const float SymbolFillsDepth = 0.8f;

        /// <summary>Never drawn smaller than this, in metres, or it vanishes when zoomed out.</summary>
        private const float MinSymbolMetres = 7f;

        /// <summary>Nor larger, or a deep column wears a badge wider than itself.</summary>
        private const float MaxSymbolMetres = 13f;

        private SpriteRenderer _body;
        private SpriteRenderer _symbol;
        private SpriteRenderer _frontEdge;
        private Color _armyColour;

        // Where the unit was at the previous tick and where it is now. Rendering
        // interpolates between the two rather than chasing the latest value,
        // which is what keeps motion smooth and rotation legible at any time
        // scale instead of blurring into a flick when the clock runs fast.
        private Vec2 _fromPosition;
        private Vec2 _toPosition;
        private Facing _fromFacing;
        private Facing _toFacing;
        private bool _selected;

        public UnitInstance Unit { get; private set; }

        public static UnitView Create(UnitInstance unit, Color armyColour, Transform parent)
        {
            var go = new GameObject($"{unit.Id} {unit.Def.DisplayName}");
            go.transform.SetParent(parent, worldPositionStays: false);

            var view = go.AddComponent<UnitView>();
            view.Unit = unit;
            view._armyColour = armyColour;

            view._body = CreateQuad(go.transform, armyColour, sortingOrder: 10, UnitArt.Plate());

            // A thin bar along the front edge. Facing decides everything about
            // flanking, so it needs to be readable at a glance.
            view._frontEdge = CreateQuad(go.transform, Color.white, sortingOrder: 11);

            // What kind of troops these are, drawn over the plate and above the
            // front bar so a narrow regiment does not have its badge hidden by
            // its own facing marker. Absent until there is artwork for the type,
            // which is not an error — the plate alone plays exactly as before.
            Sprite symbol = UnitArt.SymbolFor(unit.Def);

            if (symbol != null)
                view._symbol = CreateQuad(go.transform, Color.white, sortingOrder: 12, symbol);

            view.SnapToUnit();
            view.Render(1f);
            return view;
        }

        /// <summary>
        /// Takes a snapshot at a simulation tick. Called once per tick, never
        /// per frame.
        /// </summary>
        public void CaptureTick()
        {
            if (Unit == null) return;

            _fromPosition = _toPosition;
            _fromFacing = _toFacing;
            _toPosition = Unit.Position;
            _toFacing = Unit.Facing;
        }

        /// <summary>Snaps both ends of the interpolation to where the unit is now.</summary>
        public void SnapToUnit()
        {
            if (Unit == null) return;

            _fromPosition = _toPosition = Unit.Position;
            _fromFacing = _toFacing = Unit.Facing;
        }

        /// <summary>
        /// Draws the unit somewhere between its last two ticks.
        /// </summary>
        /// <param name="alpha">How far through the current tick, 0 to 1.</param>
        /// <summary>
        /// Whether this regiment is being drawn as something the viewing army
        /// can actually see. Hidden units are still ticked and still fight —
        /// they are simply not shown.
        /// </summary>
        public bool Spotted { get; set; } = true;

        /// <summary>Draw hidden regiments faintly instead of not at all.</summary>
        public bool GhostWhenHidden { get; set; }

        public void Render(float alpha)
        {
            if (Unit == null) return;

            bool visible = Unit.IsOnField && (Spotted || GhostWhenHidden);
            _body.enabled = visible;
            _frontEdge.enabled = visible;
            if (_symbol != null) _symbol.enabled = visible;
            if (!visible) return;

            Footprint footprint = Unit.Footprint;

            Vec2 position = Vec2.Lerp(_fromPosition, _toPosition, alpha);

            // Interpolate the bearing the short way round, so a unit wheeling
            // past due west does not spin the wrong way for half a turn.
            float turn = Facing.SignedDelta(_fromFacing, _toFacing);
            Facing facing = _fromFacing.RotatedBy(turn * alpha);

            transform.position = new Vector3(position.X, position.Y, 0f);

            // A sprite's local X runs along the unit's facing (its depth) and
            // local Y across its frontage, so the bearing maps straight onto a
            // Z rotation.
            transform.rotation = Quaternion.Euler(0f, 0f, facing.Degrees);

            // Drawn at a fraction of the ground it really holds, and no thinner
            // than a shape you can actually see and click — a regiment in line
            // is a hundred metres wide and four deep, which at any sensible zoom
            // is a hairline.
            //
            // Cosmetic only. Every rule still uses the true footprint, so
            // nothing fights, collides or routes differently for how it is
            // drawn; the price is that two regiments make contact with a gap
            // still showing between their plates. F1 draws the true shapes.
            float drawnWidth = footprint.Width * BattlefieldController.DrawnScale;

            float drawnDepth = Mathf.Max(
                footprint.Depth * BattlefieldController.DrawnScale,
                BattlefieldController.ClickableDepthMetres);

            ScaleToMetres(_body, drawnDepth, drawnWidth);

            // Fade with losses, so a mauled regiment reads as mauled even before
            // you notice it has narrowed.
            Color colour = _armyColour;
            colour.a = Mathf.Lerp(0.35f, 1f, Unit.StrengthFraction);
            _body.color = Unit.State == UnitState.Routing ? Color.Lerp(colour, Color.black, 0.5f) : colour;

            // A regiment you are only being shown because the debug view is
            // cheating should look like one — barely there, so the fogged
            // picture and the true one can be told apart at a glance.
            if (!Spotted)
            {
                Color ghost = _body.color;
                ghost.a *= 0.15f;
                _body.color = ghost;
            }

            // How far the unit is still off the bearing it is marching on. This
            // is the whole reason wheeling is expensive, so it needs to be
            // visible rather than buried in the console.
            float offByDegrees = OffBearingDegrees();

            // Thicken and colour the front edge while coming round, so a wheel
            // reads as a deliberate manoeuvre instead of the sprite happening to
            // rotate.
            bool wheeling = offByDegrees > 8f;
            float edgeThickness = wheeling ? 3.5f : 1.5f;

            // Sits on the front of the drawn body, not the true one, or it floats
            // inside a regiment that is being drawn thicker than it is.
            _frontEdge.transform.localScale = new Vector3(edgeThickness, drawnWidth, 1f);
            _frontEdge.transform.localPosition = new Vector3((drawnDepth - edgeThickness) * 0.5f, 0f, 0f);

            _frontEdge.color = wheeling
                ? Color.Lerp(new Color(1f, 0.75f, 0.2f), new Color(1f, 0.35f, 0.1f), Mathf.InverseLerp(8f, 120f, offByDegrees))
                : _selected ? Color.yellow : Color.white;

            DrawSymbol(drawnDepth, drawnWidth);
        }

        /// <summary>
        /// Places the unit-type badge: square, upright on screen, and the same
        /// size however the regiment is standing.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Held square</b> because the plate under it is not. A regiment in
        /// line is a hundred metres of frontage against eight of depth, so a
        /// badge stretched to fill it would be twelve times wider than tall.
        /// </para>
        /// <para>
        /// <b>Upright on screen</b> rather than turning with the regiment. Both
        /// armies spend a battle facing each other, so a badge that rotated
        /// with its unit would be the right way up for one side and upside down
        /// for the whole of the other. It reads as a label on a map, which is
        /// what it is.
        /// </para>
        /// <para>
        /// <b>The same size at any strength.</b> A regiment being fought down
        /// is told by its plate narrowing; shrinking the badge as well would
        /// say the same thing twice and make a battered unit hard to identify
        /// at exactly the moment identifying it matters.
        /// </para>
        /// </remarks>
        private void DrawSymbol(float drawnDepth, float drawnWidth)
        {
            if (_symbol == null) return;

            float size = Mathf.Clamp(drawnDepth * SymbolFillsDepth, MinSymbolMetres, MaxSymbolMetres);

            // Never wider than the regiment itself. A unit fought down to a
            // stub should not wear a badge overhanging its own flanks.
            size = Mathf.Min(size, drawnWidth * 0.9f);

            ScaleToMetres(_symbol, size, size);

            // Square on screen regardless of the parent's bearing.
            _symbol.transform.rotation = Quaternion.identity;

            // Follows the plate's fade, so a ghosted or mauled regiment does
            // not show a solid badge floating over a translucent body.
            Color tint = Color.white;
            tint.a = _body.color.a;
            _symbol.color = tint;
        }

        /// <summary>
        /// Scales a renderer so its sprite covers the given size in metres,
        /// whatever resolution or pixels-per-unit the artwork was imported at.
        /// </summary>
        /// <remarks>
        /// The reason artwork can be dropped in at any size. Setting local
        /// scale directly only gives metres for a one-unit sprite; a 256-pixel
        /// image imported at the usual hundred pixels per unit is 2.56 units
        /// across, and a regiment drawn with that assumption comes out two and
        /// a half times too big.
        /// </remarks>
        private static void ScaleToMetres(SpriteRenderer renderer, float alongFacing, float acrossFront)
        {
            Vector3 spriteSize = renderer.sprite.bounds.size;

            renderer.transform.localScale = new Vector3(
                alongFacing / Mathf.Max(0.0001f, spriteSize.x),
                acrossFront / Mathf.Max(0.0001f, spriteSize.y),
                1f);
        }

        /// <summary>
        /// Degrees between where the unit is pointing and where it is trying to
        /// go, or zero when it is not marching.
        /// </summary>
        private float OffBearingDegrees()
        {
            if (Unit == null || !Unit.IsMarching) return 0f;

            Vec2 toTarget = Unit.Route.Target - Unit.Position;
            if (toTarget.IsNearZero) return 0f;

            return Facing.AbsoluteDelta(Unit.Facing, Facing.FromVector(toTarget)) * Mathf.Rad2Deg;
        }

        public void SetSelected(bool selected)
        {
            _selected = selected;
            if (!_selected) _frontEdge.color = Color.white;
        }

        private static SpriteRenderer CreateQuad(Transform parent, Color colour, int sortingOrder, Sprite sprite = null)
        {
            var go = new GameObject("Quad");
            go.transform.SetParent(parent, worldPositionStays: false);

            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite ?? UnitArt.Blank();
            renderer.color = colour;
            renderer.sortingOrder = sortingOrder;

            // Stretch the supplied image over whatever local scale it is given,
            // rather than tiling or cropping it. A plate is one rectangle drawn
            // once and pulled to the ground the regiment holds.
            renderer.drawMode = SpriteDrawMode.Simple;

            return renderer;
        }
    }
}
