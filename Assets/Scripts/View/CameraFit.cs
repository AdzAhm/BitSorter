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

        [Tooltip("Free play's setup panel, docked on the right. Absent on a scene without one.")]
        [SerializeField] private SandboxPanel _sandbox;

        [Tooltip("The help panel, on the right while it is open.")]
        [SerializeField] private HelpPanel _help;

        [Tooltip("The level's banner, over the middle of the top edge.")]
        [SerializeField] private StatusBanner _banner;

        [Tooltip("The timing diagram, along the bottom while it is open (F8, or its badge).")]
        [SerializeField] private WaveformPanel _waveform;

        [Tooltip("The level, which says whether a bin sits in the board's top-right cell.")]
        [SerializeField] private LevelSession _session;

        [Tooltip("The canvas the badges are on, whose scale turns the corner into pixels.")]
        [SerializeField] private Canvas _canvas;

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
        private float _top = -1f;
        private float _bottom = -1f;
        private Vector2 _corner = new Vector2(-1f, -1f);

        private void Awake()
        {
            _camera = GetComponent<Camera>();
            _authoredSize = _camera.orthographicSize;

            if (_grid == null) _grid = FindFirstObjectByType<PlacementGrid>();
            if (_palette == null) _palette = FindFirstObjectByType<GatePaletteView>();
            if (_sandbox == null) _sandbox = FindFirstObjectByType<SandboxPanel>();
            if (_help == null) _help = FindFirstObjectByType<HelpPanel>();
            if (_banner == null) _banner = FindFirstObjectByType<StatusBanner>();
            if (_waveform == null) _waveform = FindFirstObjectByType<WaveformPanel>();
            if (_session == null) _session = FindFirstObjectByType<LevelSession>();
            if (_canvas == null) _canvas = FindFirstObjectByType<Canvas>();
        }

        private void OnEnable()
        {
            if (_grid != null)
                _grid.Resized += Refit;

            Apply(LeftInset(), RightInset(), TopInset(), BottomInset(), Corner());
        }

        private void OnDisable()
        {
            if (_grid != null)
                _grid.Resized -= Refit;
        }

        /// <summary>The board changed size, from the call that changed it: frame the new one now.</summary>
        private void Refit() => Apply(LeftInset(), RightInset(), TopInset(), BottomInset(), Corner());

        private void Update()
        {
            float left = LeftInset();
            float right = RightInset();
            float top = TopInset();
            float bottom = BottomInset();
            Vector2 corner = Corner();

            // Only on an actual change. The alternative is re-framing every frame forever to discover
            // nothing moved.
            if (Screen.width == _width && Screen.height == _height &&
                Mathf.Approximately(left, _left) && Mathf.Approximately(right, _right) &&
                Mathf.Approximately(top, _top) && Mathf.Approximately(bottom, _bottom) &&
                corner == _corner)
            {
                return;
            }

            Apply(left, right, top, bottom, corner);
        }

        private void Apply(float left, float right, float top, float bottom, Vector2 corner)
        {
            if (_camera == null || !_camera.orthographic)
                return;

            _width = Screen.width;
            _height = Screen.height;
            _left = left;
            _right = right;
            _top = top;
            _bottom = bottom;
            _corner = corner;

            Framing framing = CameraFraming.Fit(
                RequiredHalfWidth(), RequiredHalfHeight(), RequiredTop(), _authoredSize,
                _width, _height, left, right, top, bottom, RequiredRight(), corner.x, corner.y);

            _camera.orthographicSize = framing.OrthographicSize;

            Vector3 position = transform.position;
            transform.position = new Vector3(framing.CameraX, framing.CameraY, position.z);
        }

        /// <summary>Pixels the parts list takes along the left edge, gap included.</summary>
        private float LeftInset()
        {
            float edge = _palette != null ? _palette.ScreenRightEdge : 0f;
            return edge > 0f ? edge + _insetGap : 0f;
        }

        /// <summary>Pixels free play's setup panel takes along the right edge, gap included.</summary>
        /// <remarks>
        /// Whichever right-hand panel reaches further in: the setup panel in free play, and the
        /// help panel while it is open. The help panel used to be left out, so opening it to read a
        /// level's truth table put the level's bins -- the thing the table describes -- under it.
        /// </remarks>
        private float RightInset()
        {
            float setup = _sandbox != null ? _sandbox.ScreenLeftEdge : 0f;
            float help = _help != null ? _help.ScreenLeftEdge : 0f;
            float edge = setup <= 0f ? help : help <= 0f ? setup : Mathf.Min(setup, help);

            return edge > 0f ? Screen.width - edge + _insetGap : 0f;
        }

        /// <summary>Pixels the banner takes along the top edge.</summary>
        /// <remarks>
        /// No gap added, unlike the sides: a part's outline is drawn inside its square with room to
        /// spare, so its square meeting the banner still leaves space between the two -- and a gap
        /// here would shrink every level whose goal runs to two lines, for nothing on screen.
        /// </remarks>
        private float TopInset()
        {
            float edge = _banner != null ? _banner.ScreenBottomEdge : 0f;
            return edge > 0f ? Screen.height - edge : 0f;
        }

        /// <summary>
        /// Pixels the timing diagram takes along the bottom while it is open, gap included.
        /// </summary>
        /// <remarks>
        /// Read by intent, as every inset here is: the strip being switched on, not it being drawn,
        /// so a full-screen panel over the board does not reframe the board behind it.
        /// </remarks>
        private float BottomInset()
        {
            float edge = _waveform != null ? _waveform.ScreenTopEdge : 0f;
            return edge > 0f ? edge + _insetGap : 0f;
        }

        /// <summary>
        /// Pixels the top-right corner takes -- in from the right, gap included, and down from the
        /// top -- while the level keeps a bin in the board's top-right cell, or zero.
        /// </summary>
        /// <remarks>
        /// Five levels keep one there, and free play keeps that cell for its first bin. On those the
        /// badges' keys were printed across the bin, and free play's folded tab across OUT 1.
        ///
        /// Decided by the level, never by what the player has placed: a gate dropped on that cell
        /// of another level would otherwise shrink the board under the player's hand, and it is
        /// their gate to move. Read by intent, as every inset here is -- the badges are part of the
        /// HUD and step aside for a full-screen panel, and the board must not reframe behind one.
        /// </remarks>
        private Vector2 Corner()
        {
            LevelDefinition level = _session != null ? _session.Level : null;

            if (level == null || _grid == null || _canvas == null)
                return Vector2.zero;

            Vector2Int cell = _grid.HalfExtents;

            if (level.FixtureAt(cell) == null && !level.IsReserved(cell))
                return Vector2.zero;

            Vector2 corner = UiRows.TopRightCorner(_sandbox != null && _sandbox.IsFolded) * _canvas.scaleFactor;
            return new Vector2(corner.x + _insetGap, corner.y);
        }

        /// <summary>How far right of the centre a part in the right-hand column reaches.</summary>
        private float RequiredRight()
        {
            if (_grid == null)
                return 0f;

            return _grid.HalfExtents.x * _grid.CellSize + PortGeometry.NodeSize * 0.5f;
        }

        /// <summary>
        /// The bottom row and the names under it. Nothing hangs above the top row, so the lower
        /// half is the taller -- but the banner hangs over it, which is <see cref="RequiredTop"/>.
        /// </summary>
        private float RequiredHalfHeight()
        {
            if (_grid == null)
                return 0f;

            return _grid.HalfExtents.y * _grid.CellSize + NodeRenderer.LabelReach + VerticalMargin;
        }

        /// <summary>World units of clearance below the bottom row's names.</summary>
        private const float VerticalMargin = 0.2f;

        /// <summary>How far above the centre a part on the top row reaches.</summary>
        private float RequiredTop()
        {
            if (_grid == null)
                return 0f;

            return _grid.HalfExtents.y * _grid.CellSize + PortGeometry.NodeSize * 0.5f;
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
