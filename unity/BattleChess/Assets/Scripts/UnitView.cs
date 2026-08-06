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
        private static Sprite _blockSprite;

        private SpriteRenderer _body;
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

            view._body = CreateQuad(go.transform, armyColour, sortingOrder: 10);

            // A thin bar along the front edge. Facing decides everything about
            // flanking, so it needs to be readable at a glance.
            view._frontEdge = CreateQuad(go.transform, Color.white, sortingOrder: 11);

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

            // Drawn no thinner than a shape you can actually see and click. A
            // regiment in line is a hundred metres wide and four deep, which at
            // any sensible zoom is a hairline. Cosmetic only — every rule still
            // uses the true footprint, so nothing fights or collides differently
            // for being drawn thicker.
            float drawnDepth = Mathf.Max(footprint.Depth, BattlefieldController.ClickableDepthMetres);

            _body.transform.localScale = new Vector3(drawnDepth, footprint.Width, 1f);

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
            float edgeThickness = wheeling ? 7f : 3f;

            // Sits on the front of the drawn body, not the true one, or it floats
            // inside a regiment that is being drawn thicker than it is.
            _frontEdge.transform.localScale = new Vector3(edgeThickness, footprint.Width, 1f);
            _frontEdge.transform.localPosition = new Vector3((drawnDepth - edgeThickness) * 0.5f, 0f, 0f);

            _frontEdge.color = wheeling
                ? Color.Lerp(new Color(1f, 0.75f, 0.2f), new Color(1f, 0.35f, 0.1f), Mathf.InverseLerp(8f, 120f, offByDegrees))
                : _selected ? Color.yellow : Color.white;
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

        private static SpriteRenderer CreateQuad(Transform parent, Color colour, int sortingOrder)
        {
            var go = new GameObject("Quad");
            go.transform.SetParent(parent, worldPositionStays: false);

            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = BlockSprite();
            renderer.color = colour;
            renderer.sortingOrder = sortingOrder;

            return renderer;
        }

        /// <summary>A one-unit white square, scaled to whatever is needed.</summary>
        private static Sprite BlockSprite()
        {
            if (_blockSprite != null) return _blockSprite;

            var texture = new Texture2D(1, 1, TextureFormat.RGBA32, mipChain: false) { name = "Block" };
            texture.SetPixel(0, 0, Color.white);
            texture.Apply();

            _blockSprite = Sprite.Create(texture, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), pixelsPerUnit: 1f);
            return _blockSprite;
        }
    }
}
