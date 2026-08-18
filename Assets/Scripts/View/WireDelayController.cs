using BitSorter.LogicCore;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BitSorter.View
{
    /// <summary>
    /// Hover a wire and scroll to change how many ticks a bit spends travelling it. The bracket keys
    /// do the same, for pointers without a usable wheel.
    /// </summary>
    /// <remarks>
    /// Scroll rather than click-to-select because the wheel is otherwise unused in this project, so
    /// this needs no priority rule against anything. Left click already goes to both
    /// <see cref="PlacementController"/> and <see cref="WiringController"/>, and a third consumer
    /// would have had to be ordered against them.
    ///
    /// Publishes what it is pointing at rather than drawing anything. <see cref="EdgeRenderer"/> reads
    /// <see cref="HoveredEdgeId"/> and the change stamp to highlight and flash, the same way
    /// <see cref="GatePaletteView"/> reads PlacementController.Selected.
    ///
    /// Tracking by edge id works because re-timing a wire edits the blueprint in place, so the rebuild
    /// it triggers hands back the same ids in the same order. Were the wire removed and re-appended,
    /// the ids would shift and the highlight would jump to a different wire mid-scroll.
    /// </remarks>
    public sealed class WireDelayController : MonoBehaviour
    {
        [SerializeField] private LevelSession _session;
        [SerializeField] private SimulationRunner _runner;

        [Tooltip("Consulted so re-timing is suppressed while a new wire is being dragged.")]
        [SerializeField] private WiringController _wiring;

        [SerializeField] private Camera _camera;

        [Tooltip("Asked before hovering or re-timing, so the scroll wheel over a panel is not ours.")]
        [SerializeField] private PointerGate _pointer;

        [Tooltip("Seconds a wire stays flashed after its delay changes.")]
        [SerializeField] private float _flashSeconds = 0.35f;

        /// <summary>Edge id under the cursor, or -1. Only ever set while the board is editable.</summary>
        public int HoveredEdgeId { get; private set; } = -1;

        /// <summary>The edge whose delay changed most recently, or -1.</summary>
        public int ChangedEdgeId { get; private set; } = -1;

        /// <summary>
        /// Successful changes so far. Monotonic, so a one-shot reaction can tell "changed again" from
        /// "still showing the last change" -- which <see cref="ChangedEdgeId"/> alone cannot when the
        /// same wire is scrolled twice.
        /// </summary>
        public int ChangeCount { get; private set; }

        private float _changedTime = float.NegativeInfinity;

        /// <summary>How far through its flash the given edge is, 1 down to 0. Zero when not flashing.</summary>
        public float FlashStrengthFor(int edgeId)
        {
            if (edgeId < 0 || edgeId != ChangedEdgeId || _flashSeconds <= 0f)
                return 0f;

            float elapsed = Time.time - _changedTime;
            return elapsed < 0f || elapsed > _flashSeconds ? 0f : 1f - elapsed / _flashSeconds;
        }

        private void Awake()
        {
            if (_session == null)
                _session = FindFirstObjectByType<LevelSession>();

            if (_runner == null)
                _runner = FindFirstObjectByType<SimulationRunner>();

            if (_wiring == null)
                _wiring = FindFirstObjectByType<WiringController>();

            if (_camera == null)
                _camera = Camera.main;
        }

        private void Update()
        {
            if (_session == null || _runner == null || _camera == null || !_runner.IsReady)
            {
                HoveredEdgeId = -1;
                return;
            }

            // Nothing is re-timable outside Editing, and a stale highlight during a run would suggest
            // otherwise. The pointer gate covers the rest: a wire drag, a palette drag, or the cursor
            // sitting over a panel all mean this scroll is not ours to act on. The highlight clears
            // too, so a wire never looks hoverable while something else owns the cursor.
            if (!_session.CanEdit || (_pointer != null && !_pointer.MayAct(PointerUser.WireDelay)))
            {
                HoveredEdgeId = -1;
                return;
            }

            Mouse mouse = Mouse.current;
            if (mouse == null)
            {
                HoveredEdgeId = -1;
                return;
            }

            Vector2 world = ScreenToWorld(mouse.position.ReadValue());

            Edge hovered = _runner.NearestEdge(world);
            HoveredEdgeId = hovered != null ? hovered.Id : -1;

            int steps = ReadSteps(mouse, Keyboard.current);

            if (steps == 0 || HoveredEdgeId < 0)
                return;

            int target = HoveredEdgeId;

            if (_session.TryChangeWireDelay(world, steps))
            {
                ChangedEdgeId = target;
                _changedTime = Time.time;
                ChangeCount++;
            }
        }

        /// <summary>
        /// One step per wheel notch or bracket press, never more, so a flick of the wheel cannot
        /// overshoot several ticks at once.
        /// </summary>
        private static int ReadSteps(Mouse mouse, Keyboard keyboard)
        {
            if (keyboard != null)
            {
                if (keyboard.rightBracketKey.wasPressedThisFrame) return 1;
                if (keyboard.leftBracketKey.wasPressedThisFrame) return -1;
            }

            // A notch is 120 on Windows and 1 elsewhere, so the magnitude is not portable -- only the
            // sign is worth reading.
            float scroll = mouse.scroll.ReadValue().y;

            if (scroll > 0.01f) return 1;
            if (scroll < -0.01f) return -1;

            return 0;
        }

        private Vector2 ScreenToWorld(Vector2 screenPosition)
        {
            // Distance from the camera to the z=0 plane the graph is drawn on.
            float depth = -_camera.transform.position.z;
            return _camera.ScreenToWorldPoint(new Vector3(screenPosition.x, screenPosition.y, depth));
        }
    }
}
