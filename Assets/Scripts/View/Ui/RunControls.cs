using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

namespace BitSorter.View
{
    /// <summary>
    /// Run and Reset, as buttons. The keyboard bindings they mirror keep working untouched.
    /// </summary>
    /// <remarks>
    /// These are the first real buttons in the project, and the reason they can exist now is
    /// <see cref="PointerGate"/>. The old note in <see cref="SimulationInput"/> explains why they
    /// could not before: IMGUI's GUI.Button does not consume Input System mouse events, so a Run
    /// button drawn in the hud would fire *and* let the same click reach the board.
    ///
    /// Both buttons call exactly the methods the keys call, rather than reimplementing anything, so
    /// there is no second definition of what Run means.
    /// </remarks>
    public sealed class RunControls : MonoBehaviour
    {
        [SerializeField] private LevelSession _session;
        [SerializeField] private SimulationRunner _runner;

        [Tooltip("Canvas the controls are built under. Found by type when left empty.")]
        [SerializeField] private Canvas _canvas;

        private Button _run;
        private Button _reset;
        private TextMeshProUGUI _runLabel;

        private void Awake()
        {
            if (_session == null) _session = FindFirstObjectByType<LevelSession>();
            if (_runner == null) _runner = FindFirstObjectByType<SimulationRunner>();
            if (_canvas == null) _canvas = FindFirstObjectByType<Canvas>();
        }

        private void Start()
        {
            if (_canvas == null)
                return;

            RectTransform root = UiTheme.Rect("Run controls", _canvas.transform);
            UiTheme.Anchor(root, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, UiTheme.Margin), new Vector2(280f, UiTheme.ButtonHeight));

            _run = UiTheme.Button_("Run", root, "RUN", out _runLabel);
            UiTheme.Anchor(_run.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(0f, 0f),
                Vector2.zero, new Vector2(160f, UiTheme.ButtonHeight));

            _reset = UiTheme.Button_("Reset", root, "RESET", out TextMeshProUGUI _);
            UiTheme.Anchor(_reset.GetComponent<RectTransform>(), new Vector2(1f, 0f), new Vector2(1f, 0f),
                Vector2.zero, new Vector2(112f, UiTheme.ButtonHeight));

            _run.onClick.AddListener(() => Fire(Run));
            _reset.onClick.AddListener(() => Fire(ResetBoard));

            BuildControlsLine();
        }

        /// <summary>
        /// The actions that have no button.
        /// </summary>
        /// <remarks>
        /// Permanent rather than behind the diagnostics key, because these are the only way to do
        /// several things -- there is no button for drawing a wire, deleting one, or re-timing it.
        /// Hiding them would leave a player who never presses F3 unable to finish a delay level.
        ///
        /// Buttons cover run and reset, and the palette covers selection, so those are left out. What
        /// remains is exactly what the interface cannot yet express.
        /// </remarks>
        private void BuildControlsLine()
        {
            // Parented to the canvas and placed on a shared row rather than hung off the button
            // block, so its position and the toast's come from the same arithmetic. When each owned
            // half of it, they landed on the same line and drew over each other.
            TextMeshProUGUI line = UiTheme.Label(
                "controls", _canvas.transform, 18f, UiTheme.TextDim, TextAlignmentOptions.Center);

            UiTheme.Anchor(line.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, UiTheme.ControlsRow),
                new Vector2(1000f, UiTheme.ControlsHeight));

            line.text =
                "drag a port to wire     right click to delete     scroll a wire to re-time     " +
                "H for help     ESC for levels";
        }

        private void Update()
        {
            if (_session == null || _run == null)
                return;

            // Run stays available after a verdict -- LevelSession.Run rebuilds first, so it works
            // straight after a failure without needing a Reset in between, and the label says so.
            bool settled = _session.State == RunState.Passed || _session.State == RunState.Failed;
            _runLabel.text = settled ? "RUN AGAIN" : "RUN";

            _run.interactable = _session.IsLoaded && _session.State != RunState.Running;
            _reset.interactable = _session.IsLoaded;
        }

        private void Run() => _session.Run();

        /// <summary>
        /// Named ResetBoard, not Reset, for the same reason <see cref="LevelSession.ResetBoard"/> is.
        /// </summary>
        /// <remarks>
        /// MonoBehaviour.Reset is an editor callback Unity fires when a component is added or reset
        /// from the inspector -- including from the scene builder's AddComponent, long before Start
        /// has resolved anything. A method called Reset here threw a NullReferenceException during
        /// every scene rebuild, which is exactly the trap LevelSession already documents.
        /// </remarks>
        private void ResetBoard() => _session.ResetBoard();

        /// <summary>
        /// Runs an action and immediately drops focus.
        /// </summary>
        /// <remarks>
        /// Without the deselect, the clicked Button keeps focus and swallows Space and Enter -- both
        /// of which this game binds. Pressing Space to pause would re-activate Run instead, which is
        /// the single most confusing thing a first canvas can do.
        /// </remarks>
        private void Fire(System.Action action)
        {
            action();

            if (EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(null);
        }
    }
}
