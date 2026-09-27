using UnityEngine;

namespace BitSorter.View
{
    /// <summary>
    /// Grid geometry and its dot markers. Converts between world space and whole cells so
    /// placement snaps.
    /// </summary>
    /// <remarks>
    /// Cells are the coordinate system levels are authored in: a fixture's position in a level JSON
    /// file is a cell, not a world point, and <see cref="HalfExtents"/> is what the level loader
    /// checks those positions against. World units only appear when something is drawn.
    ///
    /// The cell size is therefore free to change without breaking a level, unlike when the fixture
    /// positions were hardcoded in world units -- but it does resize the board, so the extents and the
    /// camera's orthographic size want checking alongside it.
    /// </remarks>
    public sealed class PlacementGrid : MonoBehaviour
    {
        [Tooltip("World units per cell. Keep at 2 so the hardcoded half adder stays cell-aligned.")]
        [SerializeField] private float _cellSize = 2f;

        [Tooltip("Cells either side of the origin, horizontally.")]
        [SerializeField] private int _halfColumns = 4;

        [Tooltip("Cells either side of the origin, vertically.")]
        [SerializeField] private int _halfRows = 2;

        [SerializeField] private GameObject _dotPrefab;
        [SerializeField] private float _dotSize = 0.14f;

        /// <summary>Serialized, so it is readable from another component's Awake.</summary>
        public float CellSize => _cellSize <= 0f ? 1f : _cellSize;

        /// <summary>
        /// Cells either side of the origin, on the board the level in play is played on. The board
        /// edge, for the placement rules and everything drawn to it.
        /// </summary>
        /// <remarks>
        /// A level can name its own board (<see cref="LevelDefinition.BoardHalfExtents"/>), and the
        /// session sets it with <see cref="Resize"/> before anything hears the level has loaded.
        /// </remarks>
        public Vector2Int HalfExtents => _sized ? _current : DefaultHalfExtents;

        /// <summary>
        /// The scene's own board: every level that does not name one is played on it, and the
        /// level loader checks such a level's fixtures against it.
        /// </summary>
        /// <remarks>
        /// Kept apart from <see cref="HalfExtents"/> on purpose. Loading a level against the board
        /// in play would check the next level against the last one's size -- a level on the
        /// standard board, loaded straight after a wide one, would pass fixtures the standard
        /// board has no room for.
        /// </remarks>
        public Vector2Int DefaultHalfExtents => new Vector2Int(_halfColumns, _halfRows);

        /// <summary>
        /// Raised when the board changes size, from the call that changed it, after its dots are
        /// the new ones. The camera refits to it, and the grid's shimmer re-collects the dots.
        /// </summary>
        public event System.Action Resized;

        private Vector2Int _current;
        private bool _sized;
        private Transform _dots;

        public Vector2Int WorldToCell(Vector2 world) => new Vector2Int(
            Mathf.RoundToInt(world.x / CellSize),
            Mathf.RoundToInt(world.y / CellSize));

        public Vector2 CellToWorld(Vector2Int cell) =>
            new Vector2(cell.x * CellSize, cell.y * CellSize);

        public bool Contains(Vector2Int cell) =>
            Mathf.Abs(cell.x) <= HalfExtents.x && Mathf.Abs(cell.y) <= HalfExtents.y;

        /// <summary>
        /// Makes the board this many cells either side of the origin; zero or less means the
        /// scene's own. Does nothing if the board is that size already.
        /// </summary>
        /// <remarks>
        /// Safe before <see cref="Start"/>, which is when a level first loads: the dots are built
        /// then, at whatever size the board has been given by that time.
        /// </remarks>
        public void Resize(Vector2Int halfExtents)
        {
            if (halfExtents.x <= 0 || halfExtents.y <= 0)
                halfExtents = DefaultHalfExtents;

            bool changed = halfExtents != HalfExtents;

            _current = halfExtents;
            _sized = true;

            if (!changed)
                return;

            if (_dots != null)
                BuildDots();

            Resized?.Invoke();
        }

        private void Start()
        {
            if (_dots == null)
                BuildDots();
        }

        /// <summary>
        /// A dot on every cell of the board as it now is, in the one container, replacing any
        /// already there.
        /// </summary>
        /// <remarks>
        /// The old dots are taken out of the container before they are destroyed, because
        /// destruction waits for the end of the frame and anything that walks the container in the
        /// meantime -- the grid's shimmer, on <see cref="Resized"/> -- would find both sets.
        /// </remarks>
        private void BuildDots()
        {
            if (_dots == null)
            {
                var container = new GameObject("Grid dots");
                container.transform.SetParent(transform, false);
                _dots = container.transform;
            }
            else
            {
                var old = new System.Collections.Generic.List<Transform>();

                foreach (Transform dot in _dots)
                    old.Add(dot);

                foreach (Transform dot in old)
                {
                    dot.SetParent(null, false);
                    Destroy(dot.gameObject);
                }
            }

            // A look may leave the cells unmarked and let the board tile's own lines be the grid.
            // Only the grid's shimmer reads these objects, and it copes with there being none.
            GridStyle style = Look.Current.Grid;

            if (style == GridStyle.None)
                return;

            Vector2Int half = HalfExtents;

            for (int x = -half.x; x <= half.x; x++)
            {
                for (int y = -half.y; y <= half.y; y++)
                {
                    var cell = new Vector2Int(x, y);

                    GameObject dot = ViewSprites.Spawn(_dotPrefab, _dots, $"Cell {x},{y}");
                    dot.transform.position = CellToWorld(cell);
                    dot.transform.localScale = Vector3.one * (_dotSize * Look.Current.GridMarkSize);

                    var renderer = dot.GetComponent<SpriteRenderer>();
                    renderer.color = Palette.Current.Grid;

                    if (style == GridStyle.Dots)
                        renderer.sprite = ProceduralSprites.Dot();

                    renderer.sortingOrder = ViewLayers.Grid;
                }
            }
        }
    }
}
