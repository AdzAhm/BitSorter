using UnityEngine;

namespace BitSorter.View
{
    /// <summary>
    /// Grid geometry and its dot markers. Converts between world space and whole cells so
    /// placement snaps.
    /// </summary>
    /// <remarks>
    /// The default cell size of 2 is not arbitrary. Every hardcoded half adder position is
    /// (plus or minus 6, or 0) by (plus or minus 2), so on a 2-unit grid the fixture lands exactly
    /// on cell centres and occupies whole cells. Change the cell size and the fixture stops
    /// aligning, which would let the player place a gate on top of it.
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
        [SerializeField] private Color _dotColour = new Color(0.26f, 0.28f, 0.34f);

        /// <summary>Serialized, so it is readable from another component's Awake.</summary>
        public float CellSize => _cellSize <= 0f ? 1f : _cellSize;

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

            for (int x = -_halfColumns; x <= _halfColumns; x++)
            {
                for (int y = -_halfRows; y <= _halfRows; y++)
                {
                    var cell = new Vector2Int(x, y);

                    GameObject dot = ViewSprites.Spawn(_dotPrefab, container.transform, $"Cell {x},{y}");
                    dot.transform.position = CellToWorld(cell);
                    dot.transform.localScale = Vector3.one * _dotSize;

                    var renderer = dot.GetComponent<SpriteRenderer>();
                    renderer.color = _dotColour;
                    renderer.sortingOrder = -2;   // behind wires, nodes and bits
                }
            }
        }
    }
}
