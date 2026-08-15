using UnityEngine;
using UnityEngine.InputSystem;

namespace BitSorter.View
{
    /// <summary>
    /// Mouse-driven placement: number keys 1-6 pick a gate, left click places on an empty cell,
    /// right click removes whatever occupies a cell. Editing is only allowed while paused.
    /// </summary>
    /// <remarks>
    /// Reads the Input System package directly, as the project has Active Input Handling set to
    /// the new package and the old UnityEngine.Input class throws under it. Pause and step live in
    /// <see cref="SimulationInput"/>; this component owns only the editing controls.
    /// </remarks>
    public sealed class PlacementController : MonoBehaviour
    {
        [SerializeField] private SimulationRunner _runner;
        [SerializeField] private PlacementGrid _grid;
        [SerializeField] private Camera _camera;

        /// <summary>The palette entry a left click will place.</summary>
        public GateKind Selected { get; private set; } = GateKind.Not;

        /// <summary>When the player last tried to edit while running. Negative means never.</summary>
        public float LastRejectedTime { get; private set; } = float.NegativeInfinity;

        public bool WasRecentlyRejected(float seconds) => Time.time - LastRejectedTime < seconds;

        private void Awake()
        {
            if (_runner == null)
                _runner = FindFirstObjectByType<SimulationRunner>();

            if (_grid == null)
                _grid = FindFirstObjectByType<PlacementGrid>();

            if (_camera == null)
                _camera = Camera.main;
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
                ReadPalette(keyboard);

            Mouse mouse = Mouse.current;
            if (mouse == null || _runner == null || _grid == null || _camera == null)
                return;

            bool place = mouse.leftButton.wasPressedThisFrame;
            bool remove = mouse.rightButton.wasPressedThisFrame;

            if (!place && !remove)
                return;

            // Editing a running graph would mean adding and removing nodes mid-stream, so it is
            // refused rather than queued. The HUD surfaces this.
            if (!_runner.IsPaused)
            {
                LastRejectedTime = Time.time;
                return;
            }

            Vector2Int cell = _grid.WorldToCell(ScreenToWorld(mouse.position.ReadValue()));
            if (!_grid.Contains(cell))
                return;

            if (place)
                _runner.TryPlaceGate(Selected, cell);
            else
                _runner.TryRemoveAt(cell);
        }

        private void ReadPalette(Keyboard keyboard)
        {
            if (keyboard.digit1Key.wasPressedThisFrame) Selected = GateKind.Not;
            else if (keyboard.digit2Key.wasPressedThisFrame) Selected = GateKind.And;
            else if (keyboard.digit3Key.wasPressedThisFrame) Selected = GateKind.Or;
            else if (keyboard.digit4Key.wasPressedThisFrame) Selected = GateKind.Xor;
            else if (keyboard.digit5Key.wasPressedThisFrame) Selected = GateKind.Nand;
            else if (keyboard.digit6Key.wasPressedThisFrame) Selected = GateKind.Nor;
        }

        private Vector2 ScreenToWorld(Vector2 screenPosition)
        {
            // Distance from the camera to the z=0 plane the graph is drawn on.
            float depth = -_camera.transform.position.z;
            return _camera.ScreenToWorldPoint(new Vector3(screenPosition.x, screenPosition.y, depth));
        }
    }
}
