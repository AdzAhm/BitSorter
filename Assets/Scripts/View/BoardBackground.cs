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
            renderer.sortingOrder = -10;   // behind the grid dots, which sit at -2

            _renderer = renderer;
            _size = ViewSize();
            renderer.size = _size;
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
            _renderer.size = wanted;
        }

        private Vector2 ViewSize()
        {
            if (_camera == null || !_camera.orthographic)
                return new Vector2(24f, 14f);

            float height = _camera.orthographicSize * 2f + _padding;
            return new Vector2(height * _camera.aspect + _padding, height);
        }
    }
}
