using UnityEngine;

namespace BitSorter.View
{
    /// <summary>
    /// A tiled circuit-board backdrop sized to cover the camera's view.
    /// </summary>
    /// <remarks>
    /// A tiled SpriteRenderer rather than a textured quad: a quad would need a mesh whose facing
    /// direction and material setup cannot be checked without running the scene, whereas a sprite
    /// is always camera-facing. Tiled draw mode needs the sprite built with SpriteMeshType.FullRect,
    /// which <see cref="ProceduralSprites.BoardTile"/> does.
    /// </remarks>
    public sealed class BoardBackground : MonoBehaviour
    {
        [SerializeField] private Camera _camera;
        [SerializeField] private Color _tint = new Color(1f, 1f, 1f, 1f);
        [SerializeField] private float _padding = 2f;

        private SpriteRenderer _renderer;
        private Vector2 _size;

        private void Start()
        {
            if (_camera == null)
                _camera = Camera.main;

            var host = new GameObject("Board");
            host.transform.SetParent(transform, false);
            host.transform.position = Vector3.zero;

            var renderer = host.AddComponent<SpriteRenderer>();
            renderer.sprite = ProceduralSprites.BoardTile();
            renderer.drawMode = SpriteDrawMode.Tiled;
            renderer.tileMode = SpriteTileMode.Continuous;
            renderer.color = _tint;
            renderer.sortingOrder = ViewLayers.Board;

            _renderer = renderer;
            _size = ViewSize();
            renderer.size = WholeTiles(_size);
        }

        /// <summary>
        /// Re-tiles when the view changes shape, which it now does: <see cref="CameraFit"/> zooms
        /// out on a narrow window, and a backdrop sized once at startup would leave bare space
        /// around the board.
        /// </summary>
        private void LateUpdate()
        {
            if (_renderer == null)
                return;

            Vector2 wanted = ViewSize();

            if ((wanted - _size).sqrMagnitude < 0.0001f)
                return;

            _size = wanted;
            _renderer.size = WholeTiles(wanted);
        }

        /// <summary>
        /// The size to draw the backdrop at: at least what the view needs, grown until each half of
        /// it is a whole number of tiles.
        /// </summary>
        /// <remarks>
        /// A tiled sprite repeats from its renderer's bottom-left corner, not from its centre. Sized
        /// straight to the view, that corner landed wherever the view's size put it, and the tile's
        /// lines ran off the cells -- by 1.2 units vertically at 1920 by 1080, unnoticed while they
        /// were half a unit apart. With whole tiles either side of the centre, the corner is a whole
        /// number of tiles from it, and the tile's edges and middle lines fall on the cells.
        /// </remarks>
        private Vector2 WholeTiles(Vector2 wanted)
        {
            Vector2 tile = _renderer.sprite.bounds.size;

            return new Vector2(
                Mathf.Ceil(wanted.x / (2f * tile.x)) * 2f * tile.x,
                Mathf.Ceil(wanted.y / (2f * tile.y)) * 2f * tile.y);
        }

        private Vector2 ViewSize()
        {
            if (_camera == null || !_camera.orthographic)
                return new Vector2(24f, 14f);

            float height = _camera.orthographicSize * 2f + _padding;

            // Centred on the board, so WholeTiles can keep its pattern on the cells, and grown instead
            // to cover a camera that has moved sideways to frame the board clear of the interface.
            float shift = Mathf.Abs(_camera.transform.position.x) * 2f;

            return new Vector2(height * _camera.aspect + _padding + shift, height);
        }
    }
}
