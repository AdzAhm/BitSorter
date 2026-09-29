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

        [Tooltip("Seconds a CLEAR ALL or START OVER press waits for its confirming second press.")]
        [SerializeField] private float _confirmSeconds = 3f;

        /// <summary>The clear button's caption on a level that opens on an empty board.</summary>
        public const string ClearCaption = "CLEAR ALL";

        /// <summary>
        /// Its caption on a level that opens on a circuit: it puts that circuit back rather than
        /// leaving an empty board, so it says so.
        /// </summary>
        public const string StartOverCaption = "START OVER";

        /// <summary>How wide the clear button is, which both captions have to fit.</summary>
        public const float ClearButtonWidth = 150f;

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
                new Vector2(0f, UiRows.Buttons.Offset), new Vector2(RowWidth, UiRows.Buttons.Height));

            _run = UiTheme.Button_("Run", root, "RUN", out _runLabel, role: ButtonRole.Primary);
            UiTheme.Anchor(_run.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(0f, 0f),
                Vector2.zero, new Vector2(150f, UiTheme.ButtonHeight));

            _reset = UiTheme.Button_("Reset", root, "RESET", out _resetLabel);
            UiTheme.Anchor(_reset.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(158f, 0f), new Vector2(110f, UiTheme.ButtonHeight));

            _undo = UiTheme.Button_("Undo", root, "UNDO", out _undoLabel);
            UiTheme.Anchor(_undo.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(276f, 0f), new Vector2(SmallButtonWidth, UiTheme.ButtonHeight));

            _redo = UiTheme.Button_("Redo", root, "REDO", out _redoLabel);
            UiTheme.Anchor(_redo.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(356f, 0f), new Vector2(SmallButtonWidth, UiTheme.ButtonHeight));

            _clear = UiTheme.Button_("Clear", root, ClearCaption, out _clearLabel, role: ButtonRole.Destructive);
            UiTheme.Anchor(_clear.GetComponent<RectTransform>(), new Vector2(1f, 0f), new Vector2(1f, 0f),
                Vector2.zero, new Vector2(ClearButtonWidth, UiTheme.ButtonHeight));

            _run.onClick.AddListener(() => Fire(Run));
            _reset.onClick.AddListener(() => Fire(ResetBoard));
            _undo.onClick.AddListener(() => Fire(Undo));
            _redo.onClick.AddListener(() => Fire(Redo));
            _clear.onClick.AddListener(() => Fire(AskToClear));

            BuildTips();
        }

        /// <summary>
        /// The board's controls, each as near as it will go to what it works: a button's key over
        /// the button, and what works nothing on screen in two blocks either side of the row.
        /// </summary>
        /// <remarks>
        /// Permanent rather than behind the diagnostics key, because some are the only way to do
        /// what they do -- there is no button for drawing a wire, deleting one, or re-timing it.
        /// Hiding them would leave a player who never presses F2 unable to finish a delay level.
        ///
        /// They were one line along the bottom, in list order, and a playtester had to read the
        /// whole of it to find the key for the button under their hand (2026-09-27). The words all
        /// come from <see cref="ControlsReference"/>, which says where each goes; the tutorial's
        /// card lists the same controls, and two copies would disagree the first time a binding
        /// changed.
        ///
        /// None of them takes a click. A key over a button would otherwise press it, and a block
        /// beside the row would stop a click reaching the board behind it.
        /// </remarks>
        private void BuildTips()
        {
            KeyOver(_run, ControlSpot.RunButton);
            KeyOver(_reset, ControlSpot.ResetButton);
            KeyOver(_undo, ControlSpot.UndoButton);
            KeyOver(_redo, ControlSpot.RedoButton);
            KeyOver(_clear, ControlSpot.ClearButton);

            Beside(ControlSpot.LeftUpper, left: true, upper: true);
            Beside(ControlSpot.LeftLower, left: true, upper: false);
            Beside(ControlSpot.RightUpper, left: false, upper: true);
            Beside(ControlSpot.RightLower, left: false, upper: false);
        }

        /// <summary>
        /// How far the upper line sits above the row of buttons: the controls row, which is where
        /// the whole line used to be, so the toast above it still clears it.
        /// </summary>
        private static float UpperRise => UiRows.Controls.Offset - UiRows.Buttons.Offset;

        /// <summary>Between the row of buttons and a block beside it.</summary>
        public const float BlockGap = 28f;

        /// <summary>The widest a block beside the buttons may be; measured by a test, never wrapped.</summary>
        public const float BlockWidth = 360f;

        /// <summary>
        /// The widest an upper line may be. It is level with the names under the board's bottom
        /// row, which sit in the edge columns, so it stays clear of them by staying short.
        /// </summary>
        public const float UpperLineWidth = 200f;

        /// <summary>How wide the row of buttons is, which the blocks sit either side of.</summary>
        public const float RowWidth = 600f;

        /// <summary>UNDO and REDO, the narrowest buttons on the row, which the keys over them must fit.</summary>
        public const float SmallButtonWidth = 76f;

        /// <summary>A button's key, just over it, in the controls row.</summary>
        private void KeyOver(Button button, ControlSpot spot)
        {
            string key = ControlsReference.At(spot);

            if (button == null || string.IsNullOrEmpty(key))
                return;

            var rect = button.GetComponent<RectTransform>();
            RectTransform cap = UiTheme.KeyCap("key", rect, key);

            UiTheme.Anchor(cap, new Vector2(0.5f, 1f), new Vector2(0.5f, 0f),
                new Vector2(0f, UpperRise - UiRows.Buttons.Height), cap.sizeDelta);
        }

        /// <summary>One line of a block beside the row: upper, level with the keys, or lower, level with the buttons.</summary>
        private void Beside(ControlSpot spot, bool left, bool upper)
        {
            string text = ControlsReference.At(spot);

            if (string.IsNullOrEmpty(text))
                return;

            TextMeshProUGUI label = UiTheme.Label(
                $"controls {spot}", _root, UiTheme.ControlsType, UiTheme.Text,
                left
                    ? (upper ? TextAlignmentOptions.BottomRight : TextAlignmentOptions.MidlineRight)
                    : (upper ? TextAlignmentOptions.BottomLeft : TextAlignmentOptions.MidlineLeft));
            label.raycastTarget = false;

            float x = left ? 0f : 1f;

            UiTheme.Anchor(label.rectTransform, new Vector2(x, 0f), new Vector2(1f - x, 0f),
                new Vector2(left ? -BlockGap : BlockGap, upper ? UpperRise : 0f),
                new Vector2(BlockWidth, upper ? UiRows.Controls.Height : UiRows.Buttons.Height));

            label.text = text;
        }

        private void Update()
        {
            if (_session == null || _run == null)
                return;

            // Out of the way of a full-screen panel: its help line sits on this row.
            bool shown = UiModal.HudVisible;
            UiTheme.SetShown(_root, shown);   // the keys and the blocks beside the row go with it

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
            Keyboard keyboard = UiText.Keyboard;

            bool shortcut = keyboard != null
                            && !UiModal.OpenOrJustClosed
                            && keyboard.rKey.wasPressedThisFrame
                            && keyboard.shiftKey.isPressed;

            if (shortcut)
                AskToClear();

            bool pending = _confirmUntil > Time.unscaledTime;

            // Lapsing quietly is deliberate. A confirmation that waited forever would eventually be
            // answered by a click meant for something else.
            if (!pending && _confirmUntil != 0f)
                _confirmUntil = 0f;

            // Nothing to clear on an untouched board -- empty, or still the level's own start -- and
            // nothing to clear mid-run.
            bool clearable = _session.IsLoaded && _session.CanEdit && !_session.IsAtStart;

            // SetEnabled's rule with a third state, written out rather than called and then
            // overridden: two colours set in one frame would rebuild the caption every frame the
            // question stands. The captions are constants, so choosing one allocates nothing.
            _clearLabel.text = pending ? "SURE?" : _session.HasStart ? StartOverCaption : ClearCaption;
            _clearLabel.color = !clearable ? UiTheme.TextDim : pending ? UiTheme.Bad : UiTheme.Text;
            _clear.interactable = clearable;
        }

        /// <summary>First press arms, second press within the window clears.</summary>
        private void AskToClear()
        {
            if (_session == null || !_session.CanEdit || _session.IsAtStart)
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
