using BattleChess.Contracts;
using UnityEngine;

namespace BattleChess.Unity
{
    /// <summary>
    /// Top-down camera with drag-to-pan and scroll-to-zoom.
    /// </summary>
    /// <remarks>
    /// World space is Y-up and the battlefield lies on the XY plane, which maps
    /// exactly onto Unity's 2D convention — so an orthographic camera looking
    /// down -Z needs no coordinate conversion anywhere.
    /// </remarks>
    [RequireComponent(typeof(Camera))]
    public sealed class CameraRig : MonoBehaviour
    {
        private Camera _camera;
        private MapBounds _bounds;
        private bool _hasBounds;
        private Vector3 _dragOrigin;
        private bool _dragging;

        public float MinHeight = 60f;
        public float MaxHeight = 900f;
        public float PanSpeed = 400f;

        private void Awake()
        {
            _camera = GetComponent<Camera>();
            _camera.orthographic = true;
            _camera.backgroundColor = new Color(0.08f, 0.09f, 0.11f);
            _camera.clearFlags = CameraClearFlags.SolidColor;
        }

        /// <summary>Frames the whole battlefield with a little margin.</summary>
        public void FrameOn(MapBounds bounds)
        {
            _bounds = bounds;
            _hasBounds = true;

            transform.position = new Vector3(bounds.Centre.X, bounds.Centre.Y, -100f);

            float byHeight = bounds.Height * 0.5f;
            float byWidth = bounds.Width * 0.5f / Mathf.Max(0.1f, _camera.aspect);

            _camera.orthographicSize = Mathf.Max(byHeight, byWidth) * 1.08f;
            MaxHeight = _camera.orthographicSize * 1.5f;
        }

        private void Update()
        {
            HandleZoom();
            HandlePan();
            ClampToBounds();
        }

        private void HandleZoom()
        {
            float scroll = Input.mouseScrollDelta.y;
            if (Mathf.Approximately(scroll, 0f)) return;

            // Zoom toward the cursor rather than the screen centre, so the thing
            // being examined stays under the pointer.
            Vector3 before = _camera.ScreenToWorldPoint(Input.mousePosition);

            _camera.orthographicSize = Mathf.Clamp(
                _camera.orthographicSize * (1f - scroll * 0.12f),
                MinHeight,
                MaxHeight);

            Vector3 after = _camera.ScreenToWorldPoint(Input.mousePosition);
            transform.position += new Vector3(before.x - after.x, before.y - after.y, 0f);
        }

        private void HandlePan()
        {
            // Middle drag only. Left belongs to selection and right to orders —
            // both buttons now mean something on the field, and a pan that also
            // issued an order would be unusable. WASD is the everyday way to get
            // about.
            if (Input.GetMouseButtonDown(2))
            {
                _dragOrigin = _camera.ScreenToWorldPoint(Input.mousePosition);
                _dragging = true;
            }

            if (Input.GetMouseButtonUp(2))
                _dragging = false;

            if (_dragging && Input.GetMouseButton(2))
            {
                Vector3 current = _camera.ScreenToWorldPoint(Input.mousePosition);
                transform.position += new Vector3(_dragOrigin.x - current.x, _dragOrigin.y - current.y, 0f);
            }

            var keyboard = new Vector3(
                (Input.GetKey(KeyCode.D) ? 1f : 0f) - (Input.GetKey(KeyCode.A) ? 1f : 0f),
                (Input.GetKey(KeyCode.W) ? 1f : 0f) - (Input.GetKey(KeyCode.S) ? 1f : 0f),
                0f);

            if (keyboard != Vector3.zero)
            {
                float scale = _camera.orthographicSize / 250f;
                transform.position += keyboard.normalized * (PanSpeed * scale * Time.deltaTime);
            }
        }

        private void ClampToBounds()
        {
            if (!_hasBounds) return;

            // Keep the field on screen without pinning the camera to it exactly,
            // so there is room to look at the edges.
            float margin = _camera.orthographicSize;

            transform.position = new Vector3(
                Mathf.Clamp(transform.position.x, _bounds.Min.X - margin, _bounds.Max.X + margin),
                Mathf.Clamp(transform.position.y, _bounds.Min.Y - margin, _bounds.Max.Y + margin),
                -100f);
        }

        /// <summary>The world point under the mouse, on the battlefield plane.</summary>
        public Vec2 MouseWorldPosition()
        {
            Vector3 point = _camera.ScreenToWorldPoint(Input.mousePosition);
            return new Vec2(point.x, point.y);
        }
    }
}
