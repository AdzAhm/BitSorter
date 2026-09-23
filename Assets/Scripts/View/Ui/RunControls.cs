using UnityEngine;
using UnityEngine.InputSystem;
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

        [Tooltip("Seconds a CLEAR ALL press waits for its confirming second press.")]
        [SerializeField] private float _confirmSeconds = 3f;

        private Button _run;

        /// <summary>Where the Run button sits, for the tutorial to point at.</summary>
        public RectTransform RunButton =>
            _run != null ? _run.GetComponent<RectTransform>() : null;
        private Button _reset;
        private Button _undo;
        private Button _redo;
        private Button _clear;
        private TextMeshProUGUI _runLabel;
        private TextMeshProUGUI _resetLabel;
        private TextMeshProUGUI _undoLabel;
        private TextMeshProUGUI _redoLabel;
        private TextMeshProUGUI _clearLabel;
        private RectTransform _root;
        private TextMeshProUGUI _controlsLine;

        /// <summary>When the pending CLEAR ALL confirmation lapses, or zero when none is pending.</summary>
        private float _confirmUntil;

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

            // Widened from 430 to fit undo and redo between Reset and CLEAR ALL. They belong on this
            // row rather than near the palette: this is the row of things done *to* the board, and
            // undo is the counterpart of the most destructive button on it.
            RectTransform root = UiTheme.Rect("Run controls", _canvas.transform);
            _root = root;
            UiTheme.Anchor(root, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, UiRows.Buttons.Offset), new Vector2(600f, UiRows.Buttons.Height));

            _run = UiTheme.Button_("Run", root, "RUN", out _runLabel, role: ButtonRole.Primary);
            UiTheme.Anchor(_run.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(0f, 0f),
                Vector2.zero, new Vector2(150f, UiTheme.ButtonHeight));

            _reset = UiTheme.Button_("Reset", root, "RESET", out _resetLabel);
            UiTheme.Anchor(_reset.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(158f, 0f), new Vector2(110f, UiTheme.ButtonHeight));

            _undo = UiTheme.Button_("Undo", root, "UNDO", out _undoLabel);
            UiTheme.Anchor(_undo.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(276f, 0f), new Vector2(76f, UiTheme.ButtonHeight));

            _redo = UiTheme.Button_("Redo", root, "REDO", out _redoLabel);
            UiTheme.Anchor(_redo.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(356f, 0f), new Vector2(76f, UiTheme.ButtonHeight));

            _clear = UiTheme.Button_("Clear", root, "CLEAR ALL", out _clearLabel, role: ButtonRole.Destructive);
            UiTheme.Anchor(_clear.GetComponent<RectTransform>(), new Vector2(1f, 0f), new Vector2(1f, 0f),
                Vector2.zero, new Vector2(150f, UiTheme.ButtonHeight));

            _run.onClick.AddListener(() => Fire(Run));
            _reset.onClick.AddListener(() => Fire(ResetBoard));
            _undo.onClick.AddListener(() => Fire(Undo));
            _redo.onClick.AddListener(() => Fire(Redo));
            _clear.onClick.AddListener(() => Fire(AskToClear));

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
                "controls", _canvas.transform, UiType.Label, UiTheme.TextDim, TextAlignmentOptions.Center);

            UiTheme.Anchor(line.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, UiRows.Controls.Offset),
                new Vector2(1000f, UiRows.Controls.Height));

            // From ControlsReference, not spelled out here. The tutorial's card lists the same
            // controls, and two copies would disagree the first time a binding changed.
            line.text = ControlsReference.Line;
            _controlsLine = line;
        }

        private void Update()
        {
            if (_session == null || _run == null)
                return;

            // Out of the way of a full-screen panel: its help line sits on this row.
            bool shown = UiModal.HudVisible;
            UiTheme.SetShown(_root, shown);
            UiTheme.SetShown(_controlsLine, shown);

            if (!shown)
                return;

            // Run stays available after a verdict -- LevelSession.Run rebuilds first, so it works
            // straight after a failure without needing a Reset in between, and the label says so.
            bool settled = _session.State == RunState.Passed ||
                           _session.State == RunState.Failed ||
                           _session.State == RunState.Finished;
            _runLabel.text = settled ? "RUN AGAIN" : "RUN";

            // Through SetEnabled, so a dead button's caption dims with it. interactable alone tints
            // only the background, and UNDO with nothing to undo still read as a button to press.
            UiTheme.SetEnabled(_run, _runLabel, _session.IsLoaded && _session.State != RunState.Running);
            UiTheme.SetEnabled(_reset, _resetLabel, _session.IsLoaded);

            // Dead when there is nothing to step through, which is how the player finds out the
            // history is empty without pressing anything.
            UiTheme.SetEnabled(_undo, _undoLabel, _session.CanUndo);
            UiTheme.SetEnabled(_redo, _redoLabel, _session.CanRedo);

            UpdateClear();
        }

        /// <summary>
        /// Keeps the CLEAR ALL button in step with its pending confirmation, and reads the shortcut.
        /// </summary>
        /// <remarks>
        /// Shift+R rather than a key of its own, so it reads as the heavier sibling of R. It goes
        /// through exactly the same confirmation as the button: a shortcut that wiped the board on
        /// one press would be the most destructive key in the game and the easiest to hit by mistake.
        /// </remarks>
        private void UpdateClear()
        {
            Keyboard keyboard = Keyboard.current;

            bool shortcut = keyboard != null
                            && !UiModal.AnyOpen
                            && keyboard.rKey.wasPressedThisFrame
                            && keyboard.shiftKey.isPressed;

            if (shortcut)
                AskToClear();

            bool pending = _confirmUntil > Time.unscaledTime;

            // Lapsing quietly is deliberate. A confirmation that waited forever would eventually be
            // answered by a click meant for something else.
            if (!pending && _confirmUntil != 0f)
                _confirmUntil = 0f;

            // Nothing to clear on an untouched board, and nothing to clear mid-run.
            bool clearable = _session.IsLoaded && _session.CanEdit && !_session.Blueprint.IsEmpty;

            // SetEnabled's rule with a third state, written out rather than called and then
            // overridden: two colours set in one frame would rebuild the caption every frame the
            // question stands.
            _clearLabel.text = pending ? "SURE?" : "CLEAR ALL";
            _clearLabel.color = !clearable ? UiTheme.TextDim : pending ? UiTheme.Bad : UiTheme.Text;
            _clear.interactable = clearable;
        }

        /// <summary>First press arms, second press within the window clears.</summary>
        private void AskToClear()
        {
            if (_session == null || !_session.CanEdit || _session.Blueprint.IsEmpty)
                return;

            if (_confirmUntil > Time.unscaledTime)
            {
                _confirmUntil = 0f;
                _session.ClearBoard();
                return;
            }

            _confirmUntil = Time.unscaledTime + _confirmSeconds;
        }

        private void Run() => _session.Run();

        private void Undo() => _session.Undo();

        private void Redo() => _session.Redo();

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

        /// <summary>Runs an action and drops focus: see <see cref="UiTheme.Defocus"/>.</summary>
        private void Fire(System.Action action)
        {
            action();
            UiTheme.Defocus();
        }
    }
}
