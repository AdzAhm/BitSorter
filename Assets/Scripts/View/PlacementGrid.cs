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
        /// Cells either side of the origin. The board edge, for the level loader and the placement
        /// rules -- both of which need it without holding a reference to a MonoBehaviour.
        /// </summary>
        public Vector2Int HalfExtents => new Vector2Int(_halfColumns, _halfRows);

        public Vector2Int WorldToCell(Vector2 world) => new Vector2Int(
            Mathf.RoundToInt(world.x / CellSize),
            Mathf.RoundToInt(world.y / CellSize));

        public Vector2 CellToWorld(Vector2Int cell) =>
            new Vector2(cell.x * CellSize, cell.y * CellSize);

        public bool Contains(Vector2Int cell) =>
            Mathf.Abs(cell.x) <= _halfColumns && Mathf.Abs(cell.y) <= _halfRows;

        private void Start()
        {
            var container = new GameObject("Grid dots");
            container.transform.SetParent(transform, false);

            // A look may leave the cells unmarked and let the board tile's own lines be the grid.
            // Only the grid's shimmer reads these objects, and it copes with there being none.
            GridStyle style = Look.Current.Grid;

            if (style == GridStyle.None)
                return;

            for (int x = -_halfColumns; x <= _halfColumns; x++)
            {
                for (int y = -_halfRows; y <= _halfRows; y++)
                {
                    var cell = new Vector2Int(x, y);

                    GameObject dot = ViewSprites.Spawn(_dotPrefab, container.transform, $"Cell {x},{y}");
                    dot.transform.position = CellToWorld(cell);
                    dot.transform.localScale = Vector3.one * _dotSize;

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
