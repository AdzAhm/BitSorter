using UnityEngine;

namespace BitSorter.View
{
    /// <summary>
    /// Widens the camera's view until the whole board fits, whatever shape the window is.
    /// </summary>
    /// <remarks>
    /// The camera was set to an orthographic size that shows the board at 16:9 and nothing was
    /// checked at any other shape. At 16:10 -- which is most laptops -- the outermost column of
    /// cells falls outside the view, and at 4:3 a third of the board is gone. A player on the wrong
    /// monitor cannot see the bin they are wiring to, with nothing on screen to suggest why.
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

        [Tooltip("World units of clearance around the outermost cells.")]
        [SerializeField] private float _margin = 1.4f;

        private Camera _camera;
        private float _authoredSize;
        private int _width;
        private int _height;

        private void Awake()
        {
            _camera = GetComponent<Camera>();
            _authoredSize = _camera.orthographicSize;

            if (_grid == null) _grid = FindFirstObjectByType<PlacementGrid>();
        }

        private void OnEnable() => Apply();

        private void Update()
        {
            // Only on an actual resize. The alternative is recomputing every frame forever to
            // discover nothing changed.
            if (Screen.width == _width && Screen.height == _height)
                return;

            Apply();
        }

        private void Apply()
        {
            if (_camera == null || !_camera.orthographic)
                return;

            _width = Screen.width;
            _height = Screen.height;

            float aspect = _camera.aspect;

            if (aspect <= 0f)
                return;

            _camera.orthographicSize = Mathf.Max(_authoredSize, RequiredHalfWidth() / aspect);
        }

        /// <summary>Half the world width the board needs, including its margin.</summary>
        private float RequiredHalfWidth()
        {
            if (_grid == null)
                return _authoredSize * (16f / 9f);

            return _grid.HalfExtents.x * _grid.CellSize + _margin;
        }
    }
}
