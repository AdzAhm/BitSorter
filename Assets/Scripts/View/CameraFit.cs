using UnityEngine;

namespace BitSorter.View
{
    /// <summary>
    /// Frames the board in the part of the screen the interface leaves free, whatever shape the
    /// window is.
    /// </summary>
    /// <remarks>
    /// The camera was set to an orthographic size that shows the board at 16:9 and nothing was
    /// checked at any other shape. At 16:10 -- which is most laptops -- the outermost column of
    /// cells falls outside the view, and at 4:3 a third of the board is gone. A player on the wrong
    /// monitor cannot see the bin they are wiring to, with nothing on screen to suggest why.
    ///
    /// It then fitted the board to the whole screen, and the parts list sits over the screen's left
    /// edge, where the board's leftmost column is. Four shipped levels keep a source in that column,
    /// and in Carry the one the list covered source B. The board is now framed between whatever
    /// covers the left and right edges; the arithmetic is <see cref="CameraFraming"/>.
    ///
    /// The requirement is read off <see cref="PlacementGrid"/> rather than restated here, so a board
    /// that grows a column stays visible without anyone remembering to retune a number.
    ///
    /// Only ever zooms out. The authored size stays the floor, so a wide window shows the board at
    /// the framing it was designed with rather than filling the screen with it.
    /// </remarks>
    [RequireComponent(typeof(Camera))]
    public sealed class CameraFit : MonoBehaviour
    {
        [SerializeField] private PlacementGrid _grid;

        [Tooltip("The parts list, which covers the screen's left edge.")]
        [SerializeField] private GatePaletteView _palette;

        [Tooltip("World units of clearance around the outermost cells.")]
        [SerializeField] private float _margin = 1.4f;

        [Tooltip("Pixels of clearance between the parts list and the board.")]
        [SerializeField] private float _insetGap = 8f;

        private Camera _camera;
        private float _authoredSize;
        private int _width;
        private int _height;
        private float _left = -1f;
        private float _right = -1f;

        private void Awake()
        {
            _camera = GetComponent<Camera>();
            _authoredSize = _camera.orthographicSize;

            if (_grid == null) _grid = FindFirstObjectByType<PlacementGrid>();
            if (_palette == null) _palette = FindFirstObjectByType<GatePaletteView>();
        }

        private void OnEnable() => Apply(LeftInset(), RightInset());

        private void Update()
        {
            float left = LeftInset();
            float right = RightInset();

            // Only on an actual change. The alternative is re-framing every frame forever to discover
            // nothing moved.
            if (Screen.width == _width && Screen.height == _height &&
                Mathf.Approximately(left, _left) && Mathf.Approximately(right, _right))
            {
                return;
            }

            Apply(left, right);
        }

        private void Apply(float left, float right)
        {
            if (_camera == null || !_camera.orthographic)
                return;

            _width = Screen.width;
            _height = Screen.height;
            _left = left;
            _right = right;

            Framing framing = CameraFraming.Fit(
                RequiredHalfWidth(), _authoredSize, _width, _height, left, right);

            _camera.orthographicSize = framing.OrthographicSize;

            Vector3 position = transform.position;
            transform.position = new Vector3(framing.CameraX, position.y, position.z);
        }

        /// <summary>Pixels the parts list takes along the left edge, gap included.</summary>
        private float LeftInset()
        {
            float edge = _palette != null ? _palette.ScreenRightEdge : 0f;
            return edge > 0f ? edge + _insetGap : 0f;
        }

        /// <summary>Pixels taken along the right edge. Nothing takes any yet.</summary>
        private float RightInset() => 0f;

        /// <summary>Half the world width the board needs, including its margin.</summary>
        private float RequiredHalfWidth()
        {
            if (_grid == null)
                return _authoredSize * (16f / 9f);

            return _grid.HalfExtents.x * _grid.CellSize + _margin;
        }
    }
}
