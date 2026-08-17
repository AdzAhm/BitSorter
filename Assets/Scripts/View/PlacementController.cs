using UnityEngine;
using UnityEngine.InputSystem;

namespace BitSorter.View
{
    /// <summary>
    /// Mouse-driven placement: number keys 1-6 pick a gate the level stocks, left click places on an
    /// empty cell, right click removes whatever occupies a cell. Editing is only allowed while the
    /// level session is in its Editing state.
    /// </summary>
    /// <remarks>
    /// Reads the Input System package directly, as the project has Active Input Handling set to
    /// the new package and the old UnityEngine.Input class throws under it. Run and reset live in
    /// <see cref="SimulationInput"/>; this component owns only the editing controls.
    ///
    /// Every refusal message comes from <see cref="LevelRules"/> by way of the session, not from here.
    /// A click that is illegal for several reasons at once -- during a run, off the board, on a
    /// fixture -- then gets one consistent explanation instead of whichever component happened to
    /// check first.
    /// </remarks>
    public sealed class PlacementController : MonoBehaviour
    {
        [SerializeField] private LevelSession _session;
        [SerializeField] private PlacementGrid _grid;
        [SerializeField] private Camera _camera;

        /// <summary>
        /// The palette entry a left click will place. Only ever a kind the current level stocks; the
        /// initial value here holds only until the first level announces itself.
        /// </summary>
        public GateKind Selected { get; private set; } = GateKind.Not;

        private void Awake()
        {
            if (_session == null)
                _session = FindFirstObjectByType<LevelSession>();

            if (_grid == null)
                _grid = FindFirstObjectByType<PlacementGrid>();

            if (_camera == null)
                _camera = Camera.main;
        }

        // Awake has already resolved the session, and every OnEnable runs before the Start that
        // loads the first level, so the opening level is announced to this like any other.
        private void OnEnable()
        {
            if (_session != null)
                _session.LevelLoaded += SelectFirstOffered;
        }

        private void OnDisable()
        {
            if (_session != null)
                _session.LevelLoaded -= SelectFirstOffered;
        }

        /// <summary>
        /// Puts the selection on the new level's first parts row.
        /// </summary>
        /// <remarks>
        /// Without this the selection survives a level change, which means it can name a gate the
        /// new level does not stock -- the HUD reading "palette NOT" on a level whose budget is XOR
        /// and AND, and every click refused until the player guesses that a number key fixes it.
        /// </remarks>
        private void SelectFirstOffered(LevelDefinition level)
        {
            // A wires-only level stocks nothing, so there is no selection to make. Leaving the old
            // one in place is harmless: there is no cell it could legally be placed on.
            if (level != null && level.TryFirstBudgetKind(out GateKind first))
                Selected = first;
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
                ReadPalette(keyboard);

            Mouse mouse = Mouse.current;
            if (mouse == null || _session == null || _grid == null || _camera == null)
                return;

            bool place = mouse.leftButton.wasPressedThisFrame;
            bool remove = mouse.rightButton.wasPressedThisFrame;

            if (!place && !remove)
                return;

            Vector2 world = ScreenToWorld(mouse.position.ReadValue());
            Vector2Int cell = _grid.WorldToCell(world);

            if (place)
            {
                // A press on a port starts a wire drag instead; that cell is already occupied by
                // the port's own node, so placement refuses it without needing to know about it.
                _session.TryPlaceGate(Selected, cell);
                return;
            }

            // The whole right-click chain lives here so its priority is defined in one place. A cell
            // holding a removable node removes it; a cell holding a fixture reports that it is fixed
            // and stops. Only a genuinely empty cell falls through to the nearest wire -- which is why
            // deleting a wire means clicking a stretch of it that does not lie over a node's cell.
            if (_session.TryRemoveAt(cell))
                return;

            _session.TryDeleteWireAt(world);
        }

        private void ReadPalette(Keyboard keyboard)
        {
            if (!TryReadKind(keyboard, out GateKind kind))
                return;

            // A key for a gate the level does not stock is ignored rather than obeyed. Selecting it
            // could only lead to a refusal at the next click, and it would leave the HUD advertising
            // a part the player does not have.
            if (_session != null && _session.Level != null && !_session.Level.Offers(kind))
                return;

            Selected = kind;
        }

        private static bool TryReadKind(Keyboard keyboard, out GateKind kind)
        {
            if (keyboard.digit1Key.wasPressedThisFrame) kind = GateKind.Not;
            else if (keyboard.digit2Key.wasPressedThisFrame) kind = GateKind.And;
            else if (keyboard.digit3Key.wasPressedThisFrame) kind = GateKind.Or;
            else if (keyboard.digit4Key.wasPressedThisFrame) kind = GateKind.Xor;
            else if (keyboard.digit5Key.wasPressedThisFrame) kind = GateKind.Nand;
            else if (keyboard.digit6Key.wasPressedThisFrame) kind = GateKind.Nor;
            else
            {
                kind = default;
                return false;
            }

            return true;
        }

        private Vector2 ScreenToWorld(Vector2 screenPosition)
        {
            // Distance from the camera to the z=0 plane the graph is drawn on.
            float depth = -_camera.transform.position.z;
            return _camera.ScreenToWorldPoint(new Vector3(screenPosition.x, screenPosition.y, depth));
        }
    }
}
