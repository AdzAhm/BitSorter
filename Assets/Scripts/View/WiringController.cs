using BitSorter.LogicCore;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BitSorter.View
{
    /// <summary>
    /// Drag between two ports to create an edge, with a preview line following the cursor.
    /// Right-click deletes a wire. Editing is only allowed while the level session is in its Editing
    /// state.
    /// </summary>
    /// <remarks>
    /// Holds both references on purpose: the session owns edits, and the runner owns the layout and
    /// the read-only view that hit testing and the live preview need.
    ///
    /// Hit testing is geometric rather than collider based: it walks live nodes and compares
    /// distances against <see cref="PortGeometry.PositionOf"/>, the same function that placed the
    /// stubs on screen. Colliders would have meant dragging the physics system into the
    /// interaction layer, against the project rule that physics stays decorative, and keeping them
    /// rebuilt in step with GraphRevision. This runs only on click frames and while dragging, and
    /// is O(nodes x ports) -- a few dozen distance checks.
    /// </remarks>
    public sealed class WiringController : MonoBehaviour
    {
        [SerializeField] private LevelSession _session;
        [SerializeField] private SimulationRunner _runner;
        [SerializeField] private Camera _camera;
        [SerializeField] private float _previewWidth = 0.07f;
        [SerializeField] private Color _previewNeutral = new Color(0.70f, 0.72f, 0.80f, 0.85f);
        [SerializeField] private Color _previewValid = new Color(0.40f, 0.90f, 0.50f, 0.95f);
        [SerializeField] private Color _previewInvalid = new Color(0.95f, 0.40f, 0.36f, 0.95f);

        private LineRenderer _preview;
        private PortAddress _dragFrom = PortAddress.None;

        public bool IsDragging => _dragFrom.IsValid;

        private void Awake()
        {
            if (_session == null)
                _session = FindFirstObjectByType<LevelSession>();

            if (_runner == null)
                _runner = FindFirstObjectByType<SimulationRunner>();

            if (_camera == null)
                _camera = Camera.main;

            CreatePreview();
        }

        private void Update()
        {
            Mouse mouse = Mouse.current;
            if (mouse == null || _session == null || _runner == null || !_runner.IsReady || _camera == null)
                return;

            Vector2 world = ScreenToWorld(mouse.position.ReadValue());

            if (mouse.leftButton.wasPressedThisFrame)
                BeginDrag(world);

            if (IsDragging)
                UpdateDrag(world);

            if (mouse.leftButton.wasReleasedThisFrame && IsDragging)
                EndDrag(world);

            // Right-click is deliberately not handled here. PlacementController owns the whole
            // right-click chain -- node first, then wire -- so the priority lives in one place
            // instead of two components racing to act on the same click.
        }

        private void BeginDrag(Vector2 world)
        {
            PortAddress port = FindPort(world);
            if (!port.IsValid)
                return;

            // The run-state check lives here rather than earlier so that clicking empty space while
            // a run is going stays silent -- only an attempt to actually grab a port is worth refusing.
            if (_session.RefuseIfNotEditing())
                return;

            _dragFrom = port;
        }

        private void UpdateDrag(Vector2 world)
        {
            Vector2 start = _runner.PositionOf(_dragFrom);

            _preview.enabled = true;
            _preview.SetPosition(0, start);
            _preview.SetPosition(1, world);

            // Live feedback: the wire turns green only where a release would actually connect.
            PortAddress hovered = FindPort(world);
            Color colour = _previewNeutral;

            if (hovered.IsValid && hovered != _dragFrom)
            {
                colour = WiringRules.Validate(_runner.View, _dragFrom, hovered).IsValid
                    ? _previewValid
                    : _previewInvalid;
            }

            _preview.startColor = colour;
            _preview.endColor = colour;
        }

        private void EndDrag(Vector2 world)
        {
            PortAddress from = _dragFrom;
            _dragFrom = PortAddress.None;
            _preview.enabled = false;

            PortAddress to = FindPort(world);

            // TryConnect validates and reports its own reason, including "no port there".
            _session.TryConnect(from, to);
        }

        /// <summary>
        /// Nearest port within <see cref="PortGeometry.HitRadius"/>, or None. Nearest-wins keeps
        /// the result deterministic where two hit zones could ever overlap.
        /// </summary>
        private PortAddress FindPort(Vector2 world)
        {
            SimulationView view = _runner.View;

            PortAddress best = PortAddress.None;
            float bestDistance = PortGeometry.HitRadius;

            // Any port of this node is within half a node plus the radius of its centre.
            float nodeReach = PortGeometry.NodeSize + PortGeometry.HitRadius;
            float nodeReachSquared = nodeReach * nodeReach;

            for (int id = 0; id < view.NodeCount; id++)
            {
                Node node = view.GetNode(id);
                if (node == null)
                    continue;   // retired id

                Vector2 centre = _runner.PositionOf(id);
                if ((centre - world).sqrMagnitude > nodeReachSquared)
                    continue;

                Consider(centre, id, true, node.InputCount, world, ref best, ref bestDistance);
                Consider(centre, id, false, node.OutputCount, world, ref best, ref bestDistance);
            }

            return best;
        }

        private static void Consider(
            Vector2 centre, int nodeId, bool isInput, int count,
            Vector2 world, ref PortAddress best, ref float bestDistance)
        {
            for (int i = 0; i < count; i++)
            {
                float distance = Vector2.Distance(world, PortGeometry.PositionOf(centre, isInput, i, count));
                if (distance > bestDistance)
                    continue;

                bestDistance = distance;
                best = new PortAddress(nodeId, isInput, i);
            }
        }

        private void CreatePreview()
        {
            var host = new GameObject("Wire preview");
            host.transform.SetParent(transform, false);

            _preview = host.AddComponent<LineRenderer>();
            _preview.useWorldSpace = true;
            _preview.positionCount = 2;
            _preview.widthMultiplier = _previewWidth;
            _preview.numCapVertices = 4;
            _preview.sortingOrder = 3;   // above everything, it is a cursor
            _preview.material = new Material(Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color"));
            _preview.enabled = false;
        }

        private Vector2 ScreenToWorld(Vector2 screenPosition)
        {
            float depth = -_camera.transform.position.z;
            return _camera.ScreenToWorldPoint(new Vector3(screenPosition.x, screenPosition.y, depth));
        }
    }
}
