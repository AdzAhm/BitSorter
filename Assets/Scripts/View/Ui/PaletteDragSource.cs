using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BitSorter.View
{
    /// <summary>
    /// Makes a palette row draggable: pull a gate out of the menu and drop it on the board.
    /// </summary>
    /// <remarks>
    /// Coexists with clicking rather than replacing it. Unity only raises the drag handlers once the
    /// pointer has moved past the drag threshold, so a press that does not move is still a click and
    /// still reaches the row's Button -- select-then-place and drag-to-place are both available
    /// without either having to know about the other.
    ///
    /// The whole drag owns the pointer through <see cref="PointerGate"/>, so nothing else acts on
    /// the press: no gate is placed under the cursor as the drag begins, and no wire starts if the
    /// drag happens to pass over a port.
    /// </remarks>
    public sealed class PaletteDragSource : MonoBehaviour,
        IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        private GateKind _kind;

        /// <summary>The block this row drags, or null for a gate's row.</summary>
        private BlockDefinition _block;

        private LevelSession _session;
        private PointerGate _pointer;
        private PlacementGrid _grid;
        private Camera _camera;
        private Canvas _canvas;

        private RectTransform _ghost;

        /// <summary>Whether the drag under way was taken up when it began.</summary>
        /// <remarks>
        /// The release asks this, not whether the board is editable now. A drag begun mid-run is
        /// refused -- no ghost, no claim on the pointer -- and if the run ended or was reset while
        /// the button was still held, asking only "editable now?" turned it into a part dropped under
        /// the cursor that the player had never seen in hand.
        /// </remarks>
        private bool _dragging;

        /// <summary>Wired by the palette as it builds each row.</summary>
        public void Configure(
            GateKind kind, LevelSession session, PointerGate pointer,
            PlacementGrid grid, Camera camera, Canvas canvas)
        {
            _kind = kind;
            _block = null;
            _session = session;
            _pointer = pointer;
            _grid = grid;
            _camera = camera;
            _canvas = canvas;
        }

        /// <summary>Wired by the palette for a block's row: the drop places the block, top on the cell.</summary>
        public void Configure(
            BlockDefinition block, LevelSession session, PointerGate pointer,
            PlacementGrid grid, Camera camera, Canvas canvas)
        {
            Configure(default(GateKind), session, pointer, grid, camera, canvas);
            _block = block;
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (_session == null || !_session.CanEdit)
                return;

            _dragging = true;

            if (_pointer != null)
                _pointer.BeginPaletteDrag(this);

            CreateGhost();
            MoveGhost(eventData.position);
        }

        public void OnDrag(PointerEventData eventData)
        {
            MoveGhost(eventData.position);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            DestroyGhost();

            // Released before the placement is attempted. TryPlaceGate rebuilds the graph, and doing
            // that while the pointer still reads as owned would leave the board briefly inert.
            if (_pointer != null)
                _pointer.EndPaletteDrag(this);

            if (!_dragging)
                return;

            _dragging = false;

            if (_session == null || !_session.CanEdit || _grid == null || _camera == null)
                return;

            // Dropped back onto the interface: no placement, no complaint. Letting go over the menu
            // you just dragged from is a cancel, not a mistake worth a message.
            if (_pointer != null && _pointer.PointerOverUi)
                return;

            Vector2 world = ScreenToWorld(eventData.position);
            if (_block != null)
                _session.TryPlaceBlock(_block, _grid.WorldToCell(world));
            else
                _session.TryPlaceGate(_kind, _grid.WorldToCell(world));
        }

        /// <summary>Belt and braces: if this row is torn down mid-drag, let go of the pointer.</summary>
        /// <remarks>
        /// <see cref="PointerGate"/> already survives this on its own, because it reports a destroyed
        /// owner as no owner. This is the explicit half of the same guarantee, for the case where the
        /// component is merely disabled rather than destroyed.
        /// </remarks>
        private void OnDisable()
        {
            _dragging = false;
            DestroyGhost();

            if (_pointer != null)
                _pointer.EndPaletteDrag(this);
        }

        private void CreateGhost()
        {
            if (_canvas == null)
                return;

            var host = new GameObject(_block != null ? $"Dragging {_block.Name}" : $"Dragging {_kind}", typeof(RectTransform));
            host.transform.SetParent(_canvas.transform, false);

            _ghost = host.GetComponent<RectTransform>();
            _ghost.sizeDelta = new Vector2(UiTheme.PaletteButton, UiTheme.PaletteButton);

            var image = host.AddComponent<Image>();
            image.sprite = _block != null ? NodeShapes.BlockSprite() : NodeShapes.SpriteFor(_kind);
            image.color = (_block != null ? NodeShapes.BlockColour() : NodeShapes.ColourFor(_kind)) * 0.85f;

            // Never a raycast target, or it would sit under the cursor and report the pointer as
            // being over the interface for the whole drag -- which would refuse every drop.
            image.raycastTarget = false;

            _ghost.SetAsLastSibling();
        }

        private void MoveGhost(Vector2 screenPosition)
        {
            if (_ghost == null || _canvas == null)
                return;

            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                (RectTransform)_canvas.transform, screenPosition,
                _canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : _canvas.worldCamera,
                out Vector2 local);

            _ghost.localPosition = local;
        }

        private void DestroyGhost()
        {
            if (_ghost == null)
                return;

            Destroy(_ghost.gameObject);
            _ghost = null;
        }

        private Vector2 ScreenToWorld(Vector2 screenPosition)
        {
            float depth = -_camera.transform.position.z;
            return _camera.ScreenToWorldPoint(new Vector3(screenPosition.x, screenPosition.y, depth));
        }
    }
}
