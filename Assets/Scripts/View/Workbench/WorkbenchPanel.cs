using System;
using System.Collections.Generic;
using System.Globalization;
using BitSorter.Sim;
using BitSorter.SystemVerilog;
using BitSorter.ViewModel;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace BitSorter.View
{
    /// <summary>
    /// The 5.0 workbench: a circuit run on BitSorter's own engine, drawn in the Schematic look, with
    /// its waveform under it -- the student's own, or one of the samples the SAMPLE chip steps through
    /// (<see cref="WorkbenchSamples"/>). What BitSorter opens on since Part 15a.
    /// </summary>
    /// <remarks>
    /// Part 3 of 5.0 put the new engine on screen early, from a circuit written in code; since Part 8
    /// everything drawn comes from a board document -- <see cref="ReferenceCircuit"/> is one, drawn
    /// through the editor -- by way of <see cref="DocumentSchematic"/>, its parts as their symbols and
    /// its wires where the document's geometry routes them.
    ///
    /// **The circuit zooms and moves** (<see cref="SchematicView"/>): the wheel over the circuit
    /// zooms in about the cursor, a drag moves it, and FIT -- shown only while it is anything but
    /// whole -- puts it back. Its window clips it, so a zoomed circuit never runs over the waveform or
    /// the card's edge. **The waveform zooms and moves in time** (<see cref="WaveformView"/>, Part
    /// 9b-2a): the wheel over it zooms about the cursor, and with Shift, or sideways, moves through
    /// the run.
    ///
    /// **The waveform's rows are a list the student arranges** (<see cref="WaveList"/>, Part 9b-2b):
    /// a click on a row's name selects it -- and lights its wire, as pointing at a wire lights its rows
    /// -- and a right-click opens its menu, the radix its value is written in or taking it out; X steps
    /// the selected rows through the radixes and Delete takes them out. A change of rows builds the
    /// stage again round them, the circuit's zoom and the waveform's kept.
    ///
    /// **Any signal can be a row**, a block's inside included: + SIGNAL, or A, opens a search over every
    /// signal the design has (<see cref="WaveSignalPicker"/>), and Alt and a click on a wire adds its
    /// signal. GROUP, or G, puts the selected rows under a header a click opens and shuts; NAMES, or L,
    /// names every row by its whole path; a row's menu renames it. The waveform's own toolbar holds
    /// those three buttons.
    ///
    /// **The playhead is cursor 1** (Ahmad's choice, 2026-10-16): a click on the waves moves it there,
    /// snapped to an edge of the row under the pointer within 8 px or else to a whole unit
    /// (<see cref="WaveSnap"/>), and a drag scrubs. Shift and a click places C2, a drag near it moves
    /// it, C takes it away, and the time between them is written over a bracket under the rows. The
    /// mouse is read as gestures (<see cref="WavePointer"/>), and a press on a control is never one: the
    /// controls are sampled before anything reacts to the frame (<see cref="WorkbenchControlSample"/>),
    /// so the click that closes the row menu never seeks.
    ///
    /// **The SAMPLE chip steps through the circuits it can show** -- the reference, a full adder of
    /// two half-adder blocks, a clocked counter -- and S with it. Each is a document of its own, so a
    /// step builds the stage again round it and plays its run from the start.
    ///
    /// **A wire pointed at lights its whole net** and a chip beside the pointer says what drives
    /// it and what reads it (<see cref="NetLayout.Says"/>).
    ///
    /// **Every control has a key**, drawn over its button as the 4.x HUD draws RUN's, and every key is
    /// read from one table (<see cref="WorkbenchKeys"/>), which refuses a key the rest of the game or
    /// a browser owns: Space plays or pauses, R plays again from the start, period and comma step to
    /// the next edge and the one before, the brackets play slower and faster, S steps to the next
    /// sample, the arrows step through time, plus and minus zoom, F fits, up and down scroll the waves, X
    /// and Delete the rows, C the second cursor.
    ///
    /// **The controls are two rows** (Part 9b-3): RESTART, PLAY, STEP BACK and STEP, with the chip that
    /// says why playing stopped; then the time -- the slider, the speed between SLOWER and FASTER, RUN TO
    /// and where it is. SAMPLE is beside the title, over the line that describes the sample.
    ///
    /// **The run is made once, a slice a frame from when its circuit is shown** (<see cref="RunSession"/>);
    /// playing, replaying and dragging the slider back are the model answering for another moment
    /// (ADR-6). Nothing re-runs, which is why rewinding is free. **Playing it is <see cref="Playback"/>'s**:
    /// it pauses by itself at a <c>$stop</c>, a breakpoint, RUN TO and the end, and PLAY at a <c>$stop</c>
    /// runs the run on, as ModelSim does, the timeline growing with it. A right-click on a row sets a
    /// breakpoint on its signal; one on the waves or the axis, at that moment. Each is flagged in the
    /// cursor's colour.
    ///
    /// **The circuit is edited where it is drawn** (Part 10a, <c>WorkbenchPanel.Canvas.cs</c>): a part taken from
    /// the strip under the controls and placed with a click, a click or a box selecting, a drag moving the
    /// selection, the middle button or Space with a drag panning. MY CIRCUIT, first in the SAMPLE chip, is kept.
    ///
    /// A full-screen panel like Settings, and for the same reasons: it covers the 4.x board, which
    /// stays exactly as it was, and hides the HUD while it is up. Since Part 15a BitSorter opens on it
    /// (<c>WorkbenchPanel.Home.cs</c>), and Escape never leaves it. Everything on it is built the
    /// first time it opens.
    /// </remarks>
    public sealed partial class WorkbenchPanel : FullScreenPanel
    {
        [Tooltip("Canvas the panel is built under. Found by type when left empty.")]
        [SerializeField] private Canvas _canvas;

        /// <summary>The objects' names, which the tests look for.</summary>
        public const string RootName = "Workbench panel";

        /// <inheritdoc cref="RootName"/>
        public const string HomeButton = "Workbench home";

        /// <inheritdoc cref="RootName"/>
        public const string HelpButton = "Workbench help";

        /// <inheritdoc cref="RootName"/>
        public const string SettingsButton = "Workbench settings";

        /// <inheritdoc cref="RootName"/>
        public const string RestartButton = "Workbench restart";

        /// <inheritdoc cref="RootName"/>
        public const string StepButton = "Workbench step";

        /// <inheritdoc cref="RootName"/>
        public const string StepBackButton = "Workbench step back";

        /// <inheritdoc cref="RootName"/>
        public const string SlowerButton = "Workbench slower";

        /// <inheritdoc cref="RootName"/>
        public const string FasterButton = "Workbench faster";

        /// <inheritdoc cref="RootName"/>
        public const string RunToField = "Workbench run to";

        /// <inheritdoc cref="RootName"/>
        public const string PlayButton = "Workbench play";

        /// <inheritdoc cref="RootName"/>
        public const string TimeSlider = "Workbench time";

        /// <inheritdoc cref="RootName"/>
        public const string FitButton = "Workbench fit";

        /// <inheritdoc cref="RootName"/>
        public const string SampleButton = "Workbench sample";

        /// <summary>
        /// What the SAMPLE chip says for its place in <see cref="WorkbenchSamples.Chip"/>: MY CIRCUIT for the student's
        /// own, and a sample's name under the word for what the chip steps through.
        /// </summary>
        public static string SampleCaption(int sample) =>
            sample == 0 ? "MY CIRCUIT" : "SAMPLE: " + WorkbenchSamples.Chip[sample].Name.ToUpperInvariant();

        /// <summary>What the speed between SLOWER and FASTER says for a place on the ladder.</summary>
        public static string SpeedCaption(int speed, string unit = TimeUnits.Default) =>
            speed >= Playback.InstantSpeed ? "INSTANT" : Playback.Speeds[speed].ToString("0.###", CultureInfo.InvariantCulture) + " " + unit + "/s";

        // -----------------------------------------------------------------
        // The drawings, each in its own panel of the docked layout (Part 11b)
        // -----------------------------------------------------------------

        /// <summary>
        /// The least room the fit leaves round the circuit's drawing in its window, which centres it: what its corner sat at
        /// when the window was sized round it (Part 8 to Part 10).
        /// </summary>
        private static readonly Vec2 CircuitHome = new Vec2(16f, 10f);

        /// <summary>Where the waveform sits in the waves' window: a little in from its left edge, and from its top.</summary>
        private static readonly Vector2 WavesAt = new Vector2(5f, 4f);

        /// <summary>The legend's names for the four values, and what the rings and crosses mean.</summary>
        public const string LegendText = "A digit on a wire is a change travelling along it; a cross is one swallowed on the way. Double-click an input to change it.";

        private static readonly string[] LegendNames = { "0", "1", "x", "z" };

        /// <summary>
        /// Where the title and its line, the controls and the docked panels start, from the top of the screen. The line
        /// beside the title, since the inspector's bar took a row under the toolbar (Part 11b-2).
        /// </summary>
        private const float TitleTop = 16f;

        /// <inheritdoc cref="TitleTop"/>
        /// <remarks>Low enough under the title for a key's cap to sit over each button.</remarks>
        private const float ControlsTop = 84f;

        /// <inheritdoc cref="TitleTop"/>
        /// <remarks>The second row of the controls: the time, under the buttons.</remarks>
        private const float TimelineTop = ControlsTop + UiTheme.ButtonHeight + 12f;

        /// <inheritdoc cref="TitleTop"/>
        private const float StageTop = TimelineTop + UiTheme.ButtonHeight + 14f;

        // -----------------------------------------------------------------
        // State
        // -----------------------------------------------------------------

        private WorkbenchModel _model;

        /// <summary>The run being shown, made a slice a frame until it is done.</summary>
        private RunSession _run;

        /// <summary>The document shown, kept for editing and undo, its run, and the run of it as edited while that is made (Part 9b-3).</summary>
        private WorkbenchSession _session;

        /// <summary>Playing the run: where the playhead goes each frame, and why it paused.</summary>
        private Playback _playback;

        /// <summary>The speed's place on the ladder, kept from one sample to the next.</summary>
        private int _speed = Playback.DefaultSpeed;

        /// <summary>The end of the timeline the texts, the slider and the waveform were made for.</summary>
        private long _shownEnd;

        /// <summary>Why playing last stopped, as the chip was last told it, and what the chip says for it.</summary>
        private PauseReason _reasonShown;
        private long _reasonShownAt = -1;
        private string _reasonText;

        /// <summary>A refusal the chip says until the next thing is done: a value or a time that did not read.</summary>
        private string _notice;

        /// <summary>The moment the menu for a moment was opened for.</summary>
        private long _menuTime;
        private readonly List<int> _stepRows = new List<int>();

        /// <summary>Which of <see cref="WorkbenchSamples.Chip"/> is shown: 0 for MY CIRCUIT.</summary>
        private int _sample;

        /// <summary>The drawings under the shell's panels, at their own size: the whole window, never scaled (Part 11b).</summary>
        private RectTransform _stage;

        /// <summary>The waves' window, which clips them, what scrolls inside it, the bar that says how far, and how far it is.</summary>
        private RectTransform _wavesClip, _wavesContent;
        private Image _wavesThumb;
        private float _wavesScroll, _wavesWidth = -1f;

        private RectTransform _view;
        private RectTransform _schematic;
        private WaveformView _wave;

        /// <summary>The waveform's rows as the student arranges them, and what the model and the stage were last given of them.</summary>
        private WaveList _list;
        private int[] _appliedSignals = new int[0];
        private WaveRadix[] _appliedRadixes = new WaveRadix[0];
        private string[] _appliedNames = new string[0];
        private bool[] _appliedHeaders = new bool[0];
        private WaveNames _appliedStyle = WaveNames.Short;
        private readonly List<int> _rowSignals = new List<int>();
        private readonly List<WaveRadix> _rowRadixes = new List<WaveRadix>();
        private readonly List<bool> _rowsSelected = new List<bool>();
        private int _bandsVersion = -1, _bandsLit = -2, _lit = -1;

        /// <summary>The second cursor, the mouse as gestures, and what this frame's gestures ask for.</summary>
        private readonly WaveCursors _cursors = new WaveCursors();
        private readonly WavePointer _pointer = new WavePointer();
        private readonly List<WaveCommand> _commands = new List<WaveCommand>(4);
        private double _movedTo = double.NaN;

        private WaveRowMenu _rowMenu;
        private WaveTimeMenu _timeMenu;
        private WaveExportMenu _exportMenu;
        private WaveSignalPicker _picker;
        private WaveRenameField _rename, _breakField;

        /// <summary>The input SET is asking a value for, how wide it is, and the stretch the value is for (Part 9b-3).</summary>
        private string _setPort;
        private int _setWidth;
        private long _setFrom, _setUntil;
        private WaveRenameField _setField;

        /// <summary>
        /// A double-click: a second left press this soon after the first and this near it. Counted on the
        /// game's clock, which a test drives, so a test's double-click is one whatever the machine's load.
        /// </summary>
        private const double DoubleClickSeconds = 0.4;
        private const float DoubleClickPixels = 6f;
        private double _pressedAt = double.NegativeInfinity;
        private Vector2 _pressedWhere;
        private int _clicks;
        private int _renaming = -1;
        private WorkbenchControlSample _controlSample;

        /// <summary>Each binding's key, as the Input System has it, and whether a binding of the same key needs Shift.</summary>
        private Key[] _keys;
        private bool[] _keyHasShifted;

        // Whether another binding of the same key needs Alt: Up scrolls the waves, Alt+Up steps out of a block.
        private bool[] _keyHasAlt;
        private RectTransform _rings;

        private WorkbenchGraphic _legend;
        private WorkbenchLayers _layers;

        /// <summary>The zoom the schematic's chunks were last drawn at.</summary>
        private float _drawnZoom = 1f;
        private Button _fit;

        private SchematicView _viewState;
        private Image _hoverChip;
        private TextMeshProUGUI _hoverLabel;
        private int _hovered = -1;

        /// <summary>What the hover chip says now; null while it is down.</summary>
        private string _hoverSays;

        /// <summary>Whether the canvas is being dragged: its hover chip stays down meanwhile.</summary>
        private bool _dragging;

        private readonly List<TextMeshProUGUI> _digits = new List<TextMeshProUGUI>();
        private readonly List<TextMeshProUGUI> _swallowedLabels = new List<TextMeshProUGUI>();

        // Text, made once: a frame of playback formats nothing.
        private string[] _timeTexts, _playheadTexts, _inFlightTexts;
        private string _settledText;

        // -----------------------------------------------------------------
        // What the tests and the reference shots ask
        // -----------------------------------------------------------------

        /// <summary>Whether the panel is up.</summary>
        public bool IsOpen => IsShowing;

        /// <summary>The moment being shown, in nanoseconds.</summary>
        public double Time => _model != null ? _model.Time : 0.0;

        /// <summary>Whether the run is playing.</summary>
        public bool Playing => _playback != null && _playback.Playing;

        /// <summary>Playing the run -- its speed, RUN TO, breakpoints and why it paused; null until the panel has first opened.</summary>
        public Playback Playback => _playback;

        /// <summary>The menu EXPORT opens; null until the panel has first opened.</summary>
        public WaveExportMenu ExportMenu => _exportMenu;

        /// <summary>The menu for a moment; null until the panel has first opened.</summary>
        public WaveTimeMenu TimeMenu => _timeMenu;

        /// <summary>The field BREAK ON VALUE asks in; null until the panel has first opened.</summary>
        public WaveRenameField BreakValueField => _breakField;

        /// <summary>The document shown and its runs; null until the panel has first opened.</summary>
        public WorkbenchSession Session => _session;

        /// <summary>The field a double-click on a bus, or on an x, asks the input's value in; null until the panel has first opened.</summary>
        public WaveRenameField SetValueField => _setField;

        /// <summary>The run being shown; null until the panel has first opened.</summary>
        public WorkbenchModel Model => _model;

        /// <summary>The net the pointer is on, or -1.</summary>
        public int HoveredNet => _hovered;

        /// <summary>What the hover chip says now -- a net's name, or why the wire being drawn may not end where the pointer is; null while it is down.</summary>
        public string HoverSays => _hoverSays;

        /// <summary>What the chip beside the pointer says, or null when it is not up.</summary>
        public string HoverText => _hoverChip != null && _hoverChip.gameObject.activeSelf ? _hoverLabel.text : null;

        /// <summary>How far the circuit is zoomed in: 1 is the whole of it.</summary>
        public float Zoom => _viewState != null ? _viewState.Zoom : 1f;

        /// <summary>Whether the circuit is shown whole, where it belongs.</summary>
        public bool IsFitted => _viewState == null || _viewState.IsFitted;

        /// <summary>Which sample is shown, as its place in <see cref="WorkbenchSamples.Chip"/>: 0 for MY CIRCUIT, the reference 1.</summary>
        public int Sample => _sample;

        /// <summary>The waveform's rows as arranged; null until the panel has first opened.</summary>
        public WaveList List => _list;

        /// <summary>The second cursor.</summary>
        public WaveCursors Cursors => _cursors;

        /// <summary>What the second cursor's time, and the span from the playhead to it, say; null while there is none.</summary>
        public string SecondText => _wave?.SecondText;

        /// <inheritdoc cref="SecondText"/>
        public string SpanText => _wave?.SpanText;

        /// <summary>What row <paramref name="row"/>'s value says now, as the screen shows it.</summary>
        public string ReadoutOf(int row) => _wave.ReadoutOf(row);

        /// <summary>Whether row <paramref name="row"/>'s band is up: selected, or its wire pointed at.</summary>
        public bool RowBandShown(int row) => _wave.BandShown(row);

        /// <summary>The net lit on the circuit -- the one pointed at, or else a selected row's -- or -1.</summary>
        public int LitNet => _lit;

        /// <summary>Whether a row's menu is up.</summary>
        public bool RowMenuOpen => _rowMenu != null && _rowMenu.IsOpen;

        /// <summary>The + SIGNAL list, and the rename field; null until the panel has first opened.</summary>
        public WaveSignalPicker Picker => _picker;

        /// <inheritdoc cref="Picker"/>
        public WaveRenameField RenameField => _rename;

        /// <summary>What row <paramref name="row"/>'s name says, as drawn.</summary>
        public string NameOf(int row) => _wave.NameOf(row);

        /// <summary>How much wider than the reference's the names column is drawn.</summary>
        public float NamesExtra => _wave.NamesExtra;

        /// <summary>Where a row's name is on the screen: somewhere to click it.</summary>
        public Vector2 RowNameOnScreen(int row) =>
            RectTransformUtility.WorldToScreenPoint(null, _wave.Rect.TransformPoint(new Vector3(WorkbenchDrawing.NameRight - 20f, -WorkbenchDrawing.RowMiddle(row), 0f)));

        /// <summary>Where a moment is on the screen, over row <paramref name="row"/>'s wave.</summary>
        public Vector2 WaveScreenPointOf(double time, int row) => _wave.ScreenPointOf(time, row);

        /// <summary>The time the waveform shows; null until the panel has first opened.</summary>
        public WaveView Wave => _wave != null ? _wave.View : null;

        /// <summary>The time axis's numbers shown now, left to right.</summary>
        public IEnumerable<string> TickTexts => _wave != null ? _wave.TickTexts : Array.Empty<string>();

        /// <summary>Where a moment of the run is on the screen, over the waveform's first row: somewhere the wheel can zoom time.</summary>
        public Vector2 WaveScreenPointOf(double time) => _wave.ScreenPointOf(time);

        /// <summary>Where a point of the circuit's drawing, in its own units (y down), is on the screen now.</summary>
        public Vector2 ScreenPointOf(Vec2 drawingPoint) =>
            RectTransformUtility.WorldToScreenPoint(null, _schematic.TransformPoint(new Vector3(drawingPoint.x, -drawingPoint.y, 0f)));

        /// <summary>The middle of the circuit's window, on the screen: somewhere a zoom or a drag can start.</summary>
        public Vector2 ViewMiddleOnScreen =>
            RectTransformUtility.WorldToScreenPoint(null, _view.TransformPoint(_view.rect.center));

        /// <summary>The colour a signal's wire was drawn in last: what the screen shows.</summary>
        public bool TryDrawnColour(string signal, out Color colour)
        {
            colour = default;
            return _layers != null && _layers.TryDrawnColour(signal, out colour);
        }

        private void Awake()
        {
            if (_canvas == null) _canvas = FindFirstObjectByType<Canvas>();
            _tracker = FindFirstObjectByType<ProgressTracker>();
            if (_tracker != null)
                _tracker.ProgressReset += ProgressWasReset;
        }

        private void OnDestroy()
        {
            if (_tracker != null)
                _tracker.ProgressReset -= ProgressWasReset;
        }

        /// <summary>Opens the workbench, building it the first time, and plays the run from the start.</summary>
        public void Open()
        {
            if (_canvas == null)
                return;

            if (Root == null)
                Build();

            SetShowing(true);
            Fit();
            if (_forgotten)
            {
                _forgotten = false;
                OpenAsNew();
                return;
            }

            FitView();
            Restart();
        }

        private ProgressTracker _tracker;

        /// <summary>Whether RESET PROGRESS has emptied the save since the workbench last opened: it then opens MY CIRCUIT, blank.</summary>
        private bool _forgotten;

        /// <summary>
        /// RESET PROGRESS (Settings) has emptied the save, the student's circuits with it: the circuit open is let go without
        /// being written back -- its next keep would put it into the save just emptied -- and MY CIRCUIT opens, blank, the next
        /// time the workbench does. Not at once: the workbench is out of sight under Settings, and its stage is built where it
        /// is seen.
        /// </summary>
        private void ProgressWasReset()
        {
            if (_session != null)
                _keptProject = _session.Project;
            _keepAt = -1;
            _documentKey = WorkbenchSamples.MyCircuitKey;
            _exercise = null;   // its solve and its answer went with the save
            _forgotten = Root != null;
        }

        /// <summary>
        /// The workbench's keys, read from <see cref="WorkbenchKeys"/>: each binding fires when its key
        /// goes down with its modifiers held -- Shift only where another binding of the key needs it, so
        /// plus is plus with or without Shift, and Shift and an arrow is a tenth of a step.
        /// </summary>
        private void TakeKeys(Keyboard keyboard)
        {
            bool shift = keyboard.shiftKey.isPressed, ctrl = keyboard.ctrlKey.isPressed, alt = keyboard.altKey.isPressed;
            IReadOnlyList<KeyBinding> all = WorkbenchKeys.All;

            // Space with a drag pans the circuit (Part 10a), so Space plays or pauses on its release, and only
            // when it panned nothing.
            if (keyboard.spaceKey.wasPressedThisFrame)
                _spacePanned = false;

            for (int i = 0; i < all.Count; i++)
            {
                KeyBinding binding = all[i];
                if (binding.Ctrl != ctrl || (binding.Shift != shift && (binding.Shift || _keyHasShifted[i]))
                    || (binding.Alt != alt && (binding.Alt || _keyHasAlt[i])))
                    continue;
                bool onRelease = _keys[i] == Key.Space;
                if (onRelease ? !keyboard[_keys[i]].wasReleasedThisFrame || _spacePanned : !keyboard[_keys[i]].wasPressedThisFrame)
                    continue;

                Do(binding.Command);
                if (binding.Command == WorkbenchCommand.NextSample)
                    return;
            }
        }

        /// <summary>What a key does.</summary>
        private void Do(WorkbenchCommand command)
        {
            switch (command)
            {
                case WorkbenchCommand.PlayPause: TogglePlay(); break;
                case WorkbenchCommand.Restart: Restart(); break;
                case WorkbenchCommand.NextSample: ShowSample(_sample + 1); break;
                case WorkbenchCommand.NextEdge: StepEdge(forward: true); break;
                case WorkbenchCommand.PreviousEdge: StepEdge(forward: false); break;
                case WorkbenchCommand.Slower: ChangeSpeed(-1); break;
                case WorkbenchCommand.Faster: ChangeSpeed(1); break;
                case WorkbenchCommand.StepForward: StepBy(1.0); break;
                case WorkbenchCommand.StepBack: StepBy(-1.0); break;
                case WorkbenchCommand.StepForwardFine: StepBy(0.1); break;
                case WorkbenchCommand.StepBackFine: StepBy(-0.1); break;
                case WorkbenchCommand.ZoomIn: ZoomAboutTheMiddle(1); break;
                case WorkbenchCommand.ZoomOut: ZoomAboutTheMiddle(-1); break;
                case WorkbenchCommand.Fit: FitView(); break;
                case WorkbenchCommand.PageUp: ScrollWavesBy(-WorkbenchDrawing.RowHeight); break;
                case WorkbenchCommand.PageDown: ScrollWavesBy(WorkbenchDrawing.RowHeight); break;
                case WorkbenchCommand.Radix: StepRadix(); break;
                case WorkbenchCommand.ClearCursor:
                    if (_cursors.Clear())
                        Redraw();
                    break;
                case WorkbenchCommand.RemoveRows: RemoveSelected(); break;
                case WorkbenchCommand.TurnLeft: CanvasEdit(_editor.Turn(1)); break;
                case WorkbenchCommand.TurnRight: CanvasEdit(_editor.Turn(-1)); break;
                case WorkbenchCommand.Mirror: CanvasEdit(_editor.Mirror()); break;
                case WorkbenchCommand.SelectAll: _editor.SelectAll(); break;
                case WorkbenchCommand.Inspect: InspectSelected(); break;
                case WorkbenchCommand.Copy: CopySelected(); break;
                case WorkbenchCommand.Cut: CutSelected(); break;
                case WorkbenchCommand.Paste: CanvasEdit(_editor.Paste(_clip)); break;

                case WorkbenchCommand.AddSignal: OpenPicker(); break;
                case WorkbenchCommand.Group: GroupRows(); break;
                case WorkbenchCommand.Names: ToggleNames(); break;
                case WorkbenchCommand.Unit: NextUnit(); break;
                case WorkbenchCommand.Undo: UndoEdit(); break;
                case WorkbenchCommand.Redo: RedoEdit(); break;
                case WorkbenchCommand.Export: OpenExportMenu(); break;
                case WorkbenchCommand.Keys: _keyMap.Toggle(); break;
                case WorkbenchCommand.StepOut: StepOut(); break;
            }
        }

        /// <summary>Pauses, and moves the moment shown by <paramref name="nanoseconds"/>, within the run.</summary>
        public void StepBy(double nanoseconds)
        {
            if (_model == null)
                return;

            Pause();
            Seek(Math.Max(0.0, Math.Min(_model.End, _model.Time + nanoseconds)));
        }

        private void ZoomAboutTheMiddle(int notches)
        {
            if (_viewState != null && _viewState.ZoomAbout(_viewState.Window * 0.5f, notches))
                ApplyView();
        }

        /// <summary>Shows the whole circuit again, where it belongs.</summary>
        public void FitView()
        {
            if (_viewState == null)
                return;

            _viewState.Fit();
            ApplyView();
        }

        /// <summary>
        /// A turn of the wheel at a point on the screen: over the circuit, up zooms in about the point
        /// under the cursor and down zooms back out, as far as the whole circuit. True when the view
        /// changed. Only the sign is read, as everywhere the wheel is: a notch is 120 on Windows and 1
        /// elsewhere.
        /// </summary>
        /// <remarks>
        /// Public so a test can turn the wheel without a mouse: a wheel's movement is cleared as each
        /// frame's input arrives, before a fixture's own could be read. Each frame the panel hands it
        /// the real one's.
        /// </remarks>
        public bool TakeWheel(Vector2 screenPoint, Vector2 scroll)
        {
            if (_viewState == null || Mathf.Abs(scroll.y) < 0.01f || !InView(screenPoint, out Vector2 at))
                return false;

            if (!_viewState.ZoomAbout(at.ToStage(), scroll.y > 0f ? 1 : -1))
                return false;

            ApplyView();
            return true;
        }

        /// <summary>
        /// A turn of the wheel at a point on the screen: over the waveform's time axis, up zooms in about
        /// the time under the cursor and down zooms back out, as far as the whole run; with
        /// <paramref name="shift"/>, or scrolled sideways, it moves through time. True when the view
        /// changed. Public, as <see cref="TakeWheel"/> is, so a test can turn it.
        /// </summary>
        public bool TakeWaveWheel(Vector2 screenPoint, Vector2 scroll, bool shift)
        {
            if (_wave == null || !_wave.TakeWheel(screenPoint, scroll, shift))
                return false;

            Redraw();
            return true;
        }

        /// <summary>
        /// Lights the net under the pointer and puts the chip naming it beside the pointer; none while
        /// the circuit is being dragged or a part is in hand, or with the pointer off it. While a wire is
        /// being drawn (Part 10b) the pointer is on a wire to end there, not to read it: nothing is lit, and the
        /// chip says only why the document would refuse the end it is on, before the click that would be refused.
        /// </summary>
        private void Hover(Vector2 screenPoint)
        {
            int net = -1;
            string refused = null;
            bool drafting = _editor.Drafting;
            if ((!_dragging || drafting) && _editor.InHand == null && InView(screenPoint, out Vector2 inView) &&
                RectTransformUtility.ScreenPointToLocalPointInRectangle(_schematic, screenPoint, null, out Vector2 local))
            {
                if (drafting)
                    refused = _editor.DraftRefusal;
                else
                    net = _model.Layout.NetAt(new Vec2(local.x, -local.y), SchematicStyle.HoverReach / _viewState.Zoom);

                if (net >= 0 || refused != null)
                {
                    // Below and right of the pointer, kept inside the window.
                    RectTransform chip = _hoverChip.rectTransform;
                    float x = Mathf.Min(inView.x + 14f, _view.rect.width - chip.sizeDelta.x - 4f);
                    chip.anchoredPosition = new Vector2(Mathf.Max(4f, x), -(inView.y + 18f));
                }
            }

            string says = refused ?? (net >= 0 ? _model.Layout.Nets[net].Says : null);
            if (net == _hovered && says == _hoverSays)
                return;

            _hovered = net;
            _hoverSays = says;
            Show(_hoverChip, says != null);
            if (says != null)
            {
                _hoverLabel.text = says;
                _hoverChip.rectTransform.sizeDelta = new Vector2(_hoverLabel.preferredWidth + 20f, 26f);
            }
        }

        /// <summary>
        /// The mouse's buttons as gestures (<see cref="WavePointer"/>): a press says where it landed --
        /// a control, the circuit, a row's name or value, the waves or the axis -- with the moment
        /// there snapped (<see cref="WaveSnap"/>), and a drag goes on until the button is let go.
        /// </summary>
        private void TakePointer(Mouse mouse, Vector2 pointer, Keyboard keyboard, bool overControl)
        {
            bool shift = keyboard != null && keyboard.shiftKey.isPressed;
            bool ctrl = keyboard != null && keyboard.ctrlKey.isPressed;
            bool alt = keyboard != null && keyboard.altKey.isPressed;
            bool left = mouse.leftButton.wasPressedThisFrame, right = mouse.rightButton.wasPressedThisFrame;

            if (left || right)
            {
                // A double-click is a second left press soon after the first and near it; the count starts
                // again after one, so a third press is a click of its own and flips nothing back.
                if (left)
                {
                    double now = UnityEngine.Time.timeAsDouble;
                    bool quick = now - _pressedAt <= DoubleClickSeconds && (pointer - _pressedWhere).sqrMagnitude <= DoubleClickPixels * DoubleClickPixels;
                    _clicks = quick && _clicks < 2 ? _clicks + 1 : 1;
                    _pressedAt = now;
                    _pressedWhere = pointer;
                }

                var press = new WavePress { Button = left ? 0 : 1, Shift = shift, Ctrl = ctrl, Alt = alt, Row = -1, Clicks = left ? _clicks : 1 };
                if (overControl)
                    press.Region = WaveRegion.Control;
                else if (InView(pointer, out _))
                    press.Region = WaveRegion.Schematic;   // the canvas's own (TakeCanvas): the waveform's gestures pass it by
                else if (_wave.RegionAt(pointer, out WaveRegion region, out int row, out double time))
                {
                    press.Region = region;
                    press.Row = row;
                    press.Time = Snapped(time, row);
                    // A press on a group's header opens or shuts it; its menu is the right button's.
                    press.OnGroupMark = row >= 0 && row < _list.Lines.Count && _list.Lines[row].IsHeader;
                    if (_cursors.Second.HasValue)
                        press.FromSecond = _wave.PixelsBetween(time, _cursors.Second.Value);
                }

                _movedTo = double.NaN;
                if (press.Region != WaveRegion.Schematic)
                {
                    if (press.Region != WaveRegion.Control && press.Region != WaveRegion.None)
                        _canvasFocused = false;   // the waveform was clicked last: Delete is its rows'
                    _pointer.Press(press, _commands);
                }
            }
            else if (_pointer.Dragging && mouse.leftButton.isPressed)
            {
                _wave.RegionAt(pointer, out _, out int row, out _);
                double time = Snapped(_wave.TimeAtScreen(pointer), row);
                if (time != _movedTo)
                {
                    _movedTo = time;
                    _pointer.Move(time, _commands);
                }
            }

            if (!mouse.leftButton.isPressed && !left)
                _pointer.Release();

            for (int i = 0; i < _commands.Count; i++)
            {
                Do(_commands[i], pointer);
                if (_wave == null)
                    break;
            }

            _commands.Clear();
        }

        /// <summary>A moment as a click there means it: on an edge of the row under the pointer within 8 px, else a whole unit, within the run.</summary>
        private double Snapped(double time, int row)
        {
            SignalTrack track = row >= 0 && row < _model.RowCount ? _model.RowTrack(row) : null;
            return Math.Min(_model.End, WaveSnap.Snap(time, _wave.View.PixelsPerUnit, track));
        }

        /// <summary>What a gesture asks for.</summary>
        private void Do(WaveCommand command, Vector2 pointer)
        {
            switch (command.Gesture)
            {
                case WaveGesture.Seek:
                    Pause();
                    Seek(command.Time);
                    break;

                case WaveGesture.PlaceSecond:
                case WaveGesture.MoveSecond:
                    if (_cursors.Place(command.Time))
                        Redraw();
                    break;

                case WaveGesture.SelectRow:
                    _list.Select(command.Row, command.Extend, command.Toggle);
                    break;

                case WaveGesture.RowMenu:
                    if (!_list.Lines[command.Row].Selected)
                        _list.Select(command.Row);
                    _renaming = command.Row;
                    bool breakable = RowSignalAt(command.Row, out int signal);
                    _rowMenu.Open(pointer, SelectedRowsAreEnums(), _list.SelectedSignals().Count > 0, SelectedGroup() != null,
                        breakable, breakable && _playback.Breakpoints.HasOn(signal));
                    break;

                case WaveGesture.TimeMenu:
                    _menuTime = (long)Math.Round(command.Time);
                    _timeMenu.Open(pointer, Moment(_menuTime), _playback.Breakpoints.HasAt(_menuTime), _playback.Breakpoints.Count > 0);
                    break;

                case WaveGesture.EditInput:
                    EditInputAt(command.Row, _wave.TimeAtScreen(pointer), pointer);
                    break;

                case WaveGesture.ToggleGroup:
                    _list.Toggle(_list.Lines[command.Row].Group);
                    RowsChanged();
                    break;

                case WaveGesture.AddWireSignal:
                    AddWire();
                    break;
            }
        }

        /// <summary>The first group whose header is selected, or null: what UNGROUP takes apart.</summary>
        private WaveGroup SelectedGroup()
        {
            foreach (WaveLine line in _list.Lines)
                if (line.IsHeader && line.Selected)
                    return line.Group;
            return null;
        }

        /// <summary>What a row's menu does besides a radix.</summary>
        private void RowAction(WaveRowAction action)
        {
            switch (action)
            {
                case WaveRowAction.Group:
                    GroupRows();
                    break;

                case WaveRowAction.Ungroup:
                    WaveGroup group = SelectedGroup();
                    if (group != null)
                    {
                        _list.Ungroup(group);
                        RowsChanged();
                    }

                    break;

                case WaveRowAction.Rename:
                    if (_renaming >= 0 && _renaming < _list.Lines.Count)
                    {
                        WaveLine line = _list.Lines[_renaming];
                        _rename.Open(line.IsHeader ? line.Group.Name : line.Signal.Rename ?? "", RowNameOnScreen(_renaming));
                    }

                    break;

                case WaveRowAction.Remove:
                    RemoveRows();
                    break;

                case WaveRowAction.BreakOnX:
                    if (RowSignalAt(_renaming, out int x) && _playback.Breakpoints.Add(Breakpoint.OnX(x)))
                        Redraw();
                    break;

                case WaveRowAction.BreakOnValue:
                    if (RowSignalAt(_renaming, out _))
                        _breakField.Open("", RowNameOnScreen(_renaming));
                    break;

                case WaveRowAction.NoBreaks:
                    if (RowSignalAt(_renaming, out int none) && _playback.Breakpoints.RemoveOn(none) > 0)
                        Redraw();
                    break;
            }
        }

        /// <summary>The signal row <paramref name="row"/> shows, if it is a signal's row and not a group's header.</summary>
        private bool RowSignalAt(int row, out int signal)
        {
            signal = row >= 0 && row < _model.RowCount ? _model.RowSignal(row) : -1;
            return signal >= 0;
        }

        /// <summary>
        /// Sets a breakpoint on the signal of the row the menu was opened on, for the value typed: a
        /// SystemVerilog literal fitted to the signal (<see cref="Breakpoints.TryParseValue"/>). One that
        /// does not read is refused, and the chip says why.
        /// </summary>
        public void BreakOnValue(string text)
        {
            if (!RowSignalAt(_renaming, out int signal))
                return;

            if (Breakpoints.TryParseValue(text, _model.Session.Design.Signals[signal].Width, out LogicVec value, out string error))
            {
                _playback.Breakpoints.Add(Breakpoint.OnValue(signal, value));
                _notice = null;
            }
            else
            {
                _notice = error;
            }

            Redraw();
        }

        /// <summary>What the menu for a moment does: a breakpoint there set or taken away, or every one taken away.</summary>
        private void TimeAction(WaveTimeAction action)
        {
            if (action == WaveTimeAction.ToggleBreak)
                _playback.Breakpoints.ToggleAt(_menuTime);
            else
                _playback.Breakpoints.Clear();
            Redraw();
        }

        /// <summary>
        /// Where playing pauses on its way, from the RUN TO field: a whole number of nanoseconds, or nothing
        /// for nowhere. Anything else is refused, and the chip says why.
        /// </summary>
        public void SetRunTo(string text)
        {
            string typed = (text ?? "").Trim();
            if (typed.Length == 0)
            {
                _playback.RunTo = null;
                _notice = null;
            }
            else if (ViewModel.TimeText.TryParse(typed, _model.Unit, out long at, out string why))
            {
                _playback.RunTo = at;
                _notice = null;
            }
            else
            {
                _notice = why;
            }

            ShowRunTo(_playback.RunTo?.ToString(CultureInfo.InvariantCulture) ?? "");
            Redraw();
        }

        // -----------------------------------------------------------------
        // Editing the document: an input, the unit, undo (Part 9b-3)
        // -----------------------------------------------------------------

        /// <summary>
        /// Changes the input row <paramref name="row"/> shows, at <paramref name="time"/>, as a double-click
        /// on its wave does: a one-bit input that is 0 or 1 there is flipped over the stretch the moment is
        /// in, and any other -- a bus, an x -- is asked for a value for that stretch. What is not an input
        /// of the design, and a clock, are refused, and the chip says why. The run is replayed beside the
        /// one shown, and takes over once it reaches the moment shown.
        /// </summary>
        public void EditInputAt(int row, double time, Vector2 screenPoint)
        {
            if (_session == null || !RowSignalAt(row, out int signal))
                return;

            string port = InputOf(signal);
            if (port == null)
            {
                Notice(_list.LabelOf(_list.Lines[row]) + " is not an input of the design, and only an input can be changed");
                return;
            }

            BitSorter.Board.Stimulus stimulus = _session.Project.Stimulus;
            long at = Math.Max(0L, (long)Math.Floor(time + 1e-9));
            LogicVec? value = BitSorter.Board.StimulusEdits.ValueAt(stimulus, port, at);
            int width = _model.Session.Design.Signals[signal].Width;

            if (IsClock(stimulus, port) || (width == 1 && value.HasValue && value.Value.IsKnown))
            {
                Said(_session.InvertInput(port, at));
                return;
            }

            (long from, long until) = BitSorter.Board.StimulusEdits.StretchAt(stimulus, port, at);
            _setPort = port;
            _setWidth = width;
            _setFrom = from;
            _setUntil = until;
            _setField.Open("", screenPoint, "SET " + port + " FROM " + Moment(from) + (until == long.MaxValue ? " ON" : " TO " + Moment(until)));
        }

        /// <summary>
        /// Gives the input SET was opened for the value typed, over its stretch: a SystemVerilog literal
        /// fitted to the input, as BREAK ON VALUE reads one. One that does not read is refused, and the
        /// chip says why.
        /// </summary>
        public void SetInputValue(string text)
        {
            if (_session == null || _setPort == null)
                return;

            if (!Breakpoints.TryParseValue(text, _setWidth, out LogicVec value, out string error))
            {
                Notice(error);
                return;
            }

            Said(_session.SetInput(_setPort, _setFrom, _setUntil, value));
        }

        /// <summary>Counts time in the next unit, s to fs and round, as UNIT does: everything relabelled once the replay takes over, nothing rescaled.</summary>
        public void NextUnit()
        {
            if (_session == null)
                return;

            IReadOnlyList<string> units = TimeUnits.All;
            int at = 0;
            for (int i = 0; i < units.Count; i++)
                if (units[i] == _session.Unit)
                    at = i;
            Said(_session.SetUnit(units[(at + 1) % units.Count]));
        }

        /// <summary>Takes back the last edit -- to an input, or the unit -- and replays the run without it.</summary>
        public void UndoEdit()
        {
            if (_session == null)
                return;
            if (_session.Undo())
                Said(BitSorter.Board.EditResult.Ok());
            else
                Notice("nothing to undo");
        }

        /// <summary>Makes the edit last taken back again, and replays the run with it.</summary>
        public void RedoEdit()
        {
            if (_session == null)
                return;
            if (_session.Redo())
                Said(BitSorter.Board.EditResult.Ok());
            else
                Notice("nothing to redo");
        }

        /// <summary>Opens the export menu under EXPORT, as the button and E do.</summary>
        public void OpenExportMenu()
        {
            if (_exportMenu == null || _shell == null)
                return;

            Vector2 at = Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
            UnityEngine.UIElements.VisualElement button = UnityEngine.UIElements.UQueryExtensions.Q(_shell.Root, WaveformView.ExportButton);
            if (button != null && button.panel != null)
                at = ShellPanel.ScreenRectOf(button).min;

            _exportMenu.Open(at);
        }

        /// <summary>What the export menu saves.</summary>
        private void Export(WaveExportKind kind)
        {
            if (kind == WaveExportKind.Png)
                ExportPng();
            else
                ExportVcd();
        }

        /// <summary>
        /// Saves the waveform as a PNG (<see cref="WaveformPng"/>): every row shown, at the moment and over
        /// the time shown, in Schematic Light whatever the look, <see cref="WaveformPng.Width"/> wide. A
        /// download in a browser, a file in the Exports folder elsewhere; the chip says where. Returns the
        /// file's name.
        /// </summary>
        public string ExportPng()
        {
            if (_model == null || _wave == null)
                return null;

            string name = WaveExport.FileName(ExportName(), "png");
            int step = Mathf.Clamp(Mathf.RoundToInt((float)(_model.Time * 10.0)), 0, _playheadTexts.Length - 1);
            byte[] bytes = WaveformPng.Render(_model, _appliedNames, _appliedHeaders, _wave.View, _playheadTexts[step],
                _cursors, _playback.Breakpoints);
            Notice("saved " + name + " " + FileSaver.Save(name, bytes, "image/png"));
            return name;
        }

        /// <summary>
        /// Saves the run, as far as it is made, as a VCD in its unit (<see cref="WaveExport"/>), as the
        /// export menu's VCD does: a download in a browser, a file in the Exports folder elsewhere
        /// (<see cref="FileSaver"/>). The chip says what was saved and where. Returns the file's name.
        /// </summary>
        public string ExportVcd()
        {
            if (_model == null)
                return null;

            string name = WaveExport.FileName(ExportName(), "vcd");
            byte[] bytes = new System.Text.UTF8Encoding(false).GetBytes(WaveExport.Vcd(_model));
            Notice("saved " + name + " " + FileSaver.Save(name, bytes, "text/plain"));
            return name;
        }

        /// <summary>The chip says why an edit was refused, or nothing more once one is done.</summary>
        private void Said(BitSorter.Board.EditResult result)
        {
            _notice = result.Done ? null : result.Refusal;
            Redraw();
        }

        private void Notice(string words)
        {
            _notice = words;
            if (ExercisesShowing && _exercisesNotice != null)
                _exercisesNotice.text = words ?? "";   // the chip is under the page, as under the start page
            else if (StartPageShowing)
                ShowOnStartPage(words);   // the chip is under the start page (the #84 review)
            Redraw();
        }

        /// <summary>
        /// The design's input <paramref name="signal"/> is, by its name, or null: the port inside the design,
        /// or the testbench's signal of the same name that drives it.
        /// </summary>
        private string InputOf(int signal)
        {
            string name = _model.Session.Design.Signals[signal].Name;
            string design = _model.Layout.DesignPath;
            int dot = design == null ? -1 : design.LastIndexOf('.');
            string bench = dot > 0 ? design.Substring(0, dot) : null;

            foreach (PortLayout port in _model.Layout.Ports)
            {
                if (port.Output)
                    continue;
                string own = port.Signal.Substring(port.Signal.LastIndexOf('.') + 1);
                if (name == port.Signal || (bench != null && name == bench + "." + own))
                    return own;
            }

            return null;
        }

        private static bool IsClock(BitSorter.Board.Stimulus stimulus, string port)
        {
            if (stimulus == null)
                return false;
            foreach (BitSorter.Board.ClockTrack clock in stimulus.Clocks)
                if (clock.Port == port)
                    return true;
            return false;
        }

        /// <summary>Gives the row the menu was opened on the name typed, from the rename field.</summary>
        private void RenameRow(string name)
        {
            if (_renaming < 0 || _renaming >= _list.Lines.Count)
                return;
            _list.Rename(_renaming, name);
            RowsChanged();
        }

        /// <summary>Puts the selected rows under a header of their own, named for how many groups there are.</summary>
        public void GroupRows()
        {
            int groups = 0;
            foreach (WaveLine line in _list.Lines)
                if (line.IsHeader)
                    groups++;
            if (_list.GroupSelected("group " + (groups + 1).ToString(CultureInfo.InvariantCulture)) != null)
                RowsChanged();
        }

        /// <summary>Names every row by its whole path, or by its short name again.</summary>
        public void ToggleNames()
        {
            _list.Names = _list.Names == WaveNames.Short ? WaveNames.Full : WaveNames.Short;
            RowsChanged();
        }

        /// <summary>Opens the + SIGNAL list over the waveform's toolbar.</summary>
        public void OpenPicker()
        {
            Vector2 at = RectTransformUtility.WorldToScreenPoint(null, _wave.Rect.TransformPoint(new Vector3(-_wave.NamesExtra, 0f, 0f)));
            _picker.Open(_model.Signals, at);
        }

        /// <summary>Adds a signal to the waveform, from the list or a wire: at the end, selected, or selected where it is already.</summary>
        public void AddSignal(int signal)
        {
            if (_list.Add(signal) >= 0)
                RowsChanged();
        }

        /// <summary>Adds the signal of the wire under the pointer, as Alt and a click there does.</summary>
        private void AddWire()
        {
            if (_hovered < 0)
                return;
            if (_model.Session.Design.TryFind(_model.Layout.Nets[_hovered].Signal, out SignalId id))
                AddSignal(id.Index);
        }

        /// <summary>Whether every selected row is of an enum's type: what offers NAMES on its menu.</summary>
        private bool SelectedRowsAreEnums()
        {
            List<WaveSignal> selected = _list.SelectedSignals();
            if (selected.Count == 0)
                return false;
            foreach (WaveSignal row in selected)
                if (row.Signal < 0 || _model.Session.Design.Signals[row.Signal].Enum == null)
                    return false;
            return true;
        }

        /// <summary>
        /// Lights the wire pointed at, or else the first selected row's, and puts up the bands of the
        /// selected rows and of the rows showing the lit wire: each only when it changed.
        /// </summary>
        private void Light()
        {
            int lit = _hovered;
            if (lit < 0)
            {
                IReadOnlyList<WaveLine> lines = _list.Lines;
                for (int row = 0; row < lines.Count && row < _model.RowCount && lit < 0; row++)
                    if (lines[row].Selected)
                        lit = _model.NetOfRow(row);
            }

            if (lit != _lit)
            {
                _lit = lit;
                _layers.Highlight(lit);
            }

            if (_list.Version == _bandsVersion && _hovered == _bandsLit)
                return;

            _bandsVersion = _list.Version;
            _bandsLit = _hovered;
            _rowsSelected.Clear();
            foreach (WaveLine line in _list.Lines)
                _rowsSelected.Add(line.Selected);
            _wave.SetBands(_rowsSelected, _hovered);
        }

        /// <summary>Where a point on the screen is in the circuit's window, y down; true when it is inside it.</summary>
        private bool InView(Vector2 screenPoint, out Vector2 at)
        {
            at = default;
            if (_view == null || !RectTransformUtility.ScreenPointToLocalPointInRectangle(_view, screenPoint, null, out Vector2 local))
                return false;

            // The window's pivot is its top-left corner, so local x runs right and y up from there.
            at = new Vector2(local.x, -local.y);
            return _view.rect.Contains(local);
        }

        private bool OverFit(Vector2 screenPoint) =>
            _fit != null && _fit.gameObject.activeSelf &&
            RectTransformUtility.RectangleContainsScreenPoint(_fit.GetComponent<RectTransform>(), screenPoint, null);

        /// <summary>Puts the schematic where the view says, at its zoom, and FIT up while it is anything but whole.</summary>
        private void ApplyView()
        {
            _schematic.anchoredPosition = new Vector2(_viewState.Offset.x, -_viewState.Offset.y);
            _schematic.localScale = new Vector3(_viewState.Zoom, _viewState.Zoom, 1f);
            Show(_fit, !_viewState.IsFitted);
            PlaceMinimap();

            // The edges' smoothing is a pixel wide, and a zoom changes how many units a pixel is; a pan
            // changes neither, and every frame of a drag is one.
            if (_viewState.Zoom != _drawnZoom)
            {
                _drawnZoom = _viewState.Zoom;
                _layers.RefreshSchematic();
                if (_overlay != null)
                    _overlay.SetVerticesDirty();
            }
        }

        /// <summary>Starts the run again from 0, playing, with a zoomed waveform following it again.</summary>
        public void Restart()
        {
            if (_playback == null)
                return;

            _notice = null;
            if (_session.TakeReplay())
                SwitchRun();
            _wave?.Restart(0.0);
            _playback.Restart();
            Redraw();
        }

        /// <summary>
        /// Plays or pauses. PLAY at a <c>$stop</c> runs the run on, and at an end the design gave plays it
        /// again from the start (<see cref="Playback.Play"/>); either way a zoomed waveform follows it again.
        /// </summary>
        public void TogglePlay()
        {
            if (_playback == null)
                return;

            _notice = null;
            bool fromTheEnd = !_playback.Playing && _model.Time >= _playback.Made - 1e-9;
            _playback.Toggle();
            if (fromTheEnd && _playback.Playing)
                _wave?.Restart(_model.Time);
            if (_model.End != _shownEnd)
                TimelineGrew();
            Redraw();
        }

        /// <summary>Stops playing where it is.</summary>
        public void Pause()
        {
            if (_playback == null)
                return;

            _playback.Pause();
            RefreshPlayLabel();
        }

        /// <summary>
        /// To the next edge of the selected rows -- every row's, with none selected -- or to the one
        /// before, paused, as STEP and STEP BACK do.
        /// </summary>
        public void StepEdge(bool forward)
        {
            if (_playback == null)
                return;

            _notice = null;
            _stepRows.Clear();
            IReadOnlyList<WaveLine> lines = _list.Lines;
            for (int row = 0; row < lines.Count && row < _model.RowCount; row++)
                if (lines[row].Selected && _model.RowSignal(row) >= 0)
                    _stepRows.Add(row);
            if (_stepRows.Count == 0)
                for (int row = 0; row < _model.RowCount; row++)
                    if (_model.RowSignal(row) >= 0)
                        _stepRows.Add(row);

            if (forward)
                _playback.Step(_stepRows);
            else
                _playback.StepBack(_stepRows);
            Redraw();
        }

        /// <summary>A speed by its place on the ladder (<see cref="Playback.Speeds"/>, then INSTANT), as SLOWER and FASTER pressed until it shows.</summary>
        public void SetSpeed(int speed)
        {
            if (_playback == null)
                return;

            _playback.SetSpeed(speed);
            _speed = speed;
            SetText(_speedLabel, SpeedCaption(_speed, _model.Unit));
            Redraw();
        }

        /// <summary>A step along the speed's ladder: faster for more than zero. The speed is kept for the next sample.</summary>
        public void ChangeSpeed(int steps)
        {
            if (_playback == null || steps == 0)
                return;
            if (!(steps > 0 ? _playback.Faster() : _playback.Slower()))
                return;

            _speed = _playback.Speed;
            SetText(_speedLabel, SpeedCaption(_speed, _model.Unit));
            Redraw();
        }

        /// <summary>
        /// The run was run on past the timeline's end: the texts, the slider and the waveform are made for
        /// the longer one, and the waveform's rows drawn again.
        /// </summary>
        private void TimelineGrew()
        {
            _shownEnd = _model.End;
            MakeTexts();
            SliderEnd(_model.End);
            if (_wave != null)
                _wave.View.Extend(_model.End);
            _layers?.Extended();
        }

        /// <summary>Shows another moment of the run.</summary>
        public void Seek(double time)
        {
            if (_model == null)
                return;

            _model.Seek(time);
            Redraw();
        }

        private void Update()
        {
            if (!IsShowing || _model == null)
                return;

            Fit();
            _shell.Opacity = Root.TryGetComponent(out CanvasGroup fading) ? fading.alpha : 1f;

            OpenPendingGoal();   // an exercise's brief, once GOAL has its place (Part 15d)
            TickLessons();   // the tutorial's step, or a first-time hint the run has earned (Part 15f)

            // Files the player picked in the page (Part 13d), once it has read them.
            if (FileImport.Picks && FileImport.TryTake(out List<BitSorter.Board.SourceFile> picked))
                Import(picked);

            // Escape closes what is on top of the workbench -- a popup first, then the start page, a part in hand or a drag
            // (Part 10a) -- and nothing more: nothing is behind the workbench to go back to (Part 15a).
            Keyboard keyboard = UiText.Keyboard;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame && !OpenedThisFrame && !UiEscape.AnyHolds)
            {
                if (ExercisesShowing)
                    HideExercises();
                else if (StartPageShowing)
                    LeaveHome();
                else
                    LetGoOnCanvas();
                return;
            }

            // A page -- the start page, EXERCISES -- covers the circuit: nothing under it is played, scrolled, picked or
            // deleted. ? still opens and closes the keyboard map, as the map says it does.
            if (PageShowing)
            {
                if (keyboard != null && keyboard.slashKey.wasPressedThisFrame && !OpenedThisFrame)
                    _keyMap.Toggle();
                KeepMyCircuit(now: false);
                return;
            }

            if (keyboard != null && !OpenedThisFrame)
                TakeKeys(keyboard);

            Mouse mouse = Mouse.current;
            if (mouse != null && !OpenedThisFrame)
            {
                // Over the circuit the wheel zooms it, over the waveform's time axis it zooms time, and
                // anywhere else on the stage it moves a page that scrolls.
                Vector2 pointer = mouse.position.ReadValue();
                Vector2 scroll = mouse.scroll.ReadValue();
                bool overControl = _controlSample.OverControl;
                if (overControl)
                {
                    // The wheel over a control -- the row menu over the waves -- is the control's.
                }
                else if (InView(pointer, out _))
                    TakeWheel(pointer, scroll);
                else if (_wave.Over(pointer, out _))
                    TakeWaveWheel(pointer, scroll, keyboard != null && keyboard.shiftKey.isPressed);
                else
                    TakeScroll(pointer, scroll);

                TakeCanvas(mouse, pointer, keyboard, overControl);
                TakePointer(mouse, pointer, keyboard, overControl);
                TakeBlockRowMenu(mouse, pointer);
                ShowStatus(pointer);
                if (_wave == null)
                    return;
                _dragging = _canvasPointer.Dragging;
                Hover(pointer);
            }

            Light();
            RefreshTiming();
            DrawOverlay();
            RefreshInspector();
            RefreshTranscript();
            RefreshHierarchy();
            RefreshPartsPanel();
            TickCode();
            RefreshCode();

            // The run is made a slice a frame until it is done, and a moment past where it has got waits;
            // an edit's replay is made beside it and takes over once it reaches the moment shown.
            SessionFrame made = _session.Frame(_model.Time);
            if (made == SessionFrame.Switched || _session.Run != _run)
            {
                // A replay that took over in a frame, or outside one: Finish, as a capture or a test makes the
                // whole of a run, lets it take over without a frame saying so.
                SwitchRun();
            }
            else if (_session.StepVersion != _drawnStep)
            {
                SteppedInOrOut();
            }
            else if (_session.LayoutVersion != _drawnLayout)
            {
                Relaid();
            }
            else if (made == SessionFrame.Grew)
            {
                _layers.Grew();
                if (_run.Done)
                    _settledText = SettledText();
                Redraw();
            }

            if (_playback.Playing)
            {
                _playback.Frame(UnityEngine.Time.deltaTime);
                Redraw();
            }

            if (_model.End != _shownEnd)
            {
                TimelineGrew();
                Redraw();
            }

            KeepMyCircuit(now: false);
        }

        protected override void OnShown()
        {
            _laidWindow = new Vector2(-1f, -1f);
            if (_shell != null)
                _shell.Visible = true;
        }

        protected override void OnHidden()
        {
            if (_shell != null)
                _shell.Visible = false;
            _codeView?.Bind(null);
        }

        // -----------------------------------------------------------------
        // Building
        // -----------------------------------------------------------------

        private void Build()
        {
            SchematicPalette palette = SchematicPalette.Current;

            // What the launch asked for (Part 15a); else MY CIRCUIT when there is anything in it, the reference circuit until
            // there is.
            _sample = _launchSample >= 0 ? _launchSample : MyCircuitHasAnything() ? 0 : 1;
            OpenRun();

            Image scrim = UiTheme.Scrim(RootName, _canvas.transform, palette.Ground);
            Root = scrim.GetComponent<RectTransform>();
            UiTheme.Stretch(Root);
            _controlSample = Root.gameObject.AddComponent<WorkbenchControlSample>();

            IReadOnlyList<KeyBinding> bindings = WorkbenchKeys.All;
            _keys = new Key[bindings.Count];
            _keyHasShifted = new bool[bindings.Count];
            _keyHasAlt = new bool[bindings.Count];
            for (int i = 0; i < bindings.Count; i++)
            {
                _keys[i] = (Key)Enum.Parse(typeof(Key), bindings[i].Key);
                for (int j = 0; j < bindings.Count; j++)
                {
                    _keyHasShifted[i] |= bindings[j].Key == bindings[i].Key && bindings[j].Shift && bindings[j].Ctrl == bindings[i].Ctrl;
                    _keyHasAlt[i] |= bindings[j].Key == bindings[i].Key && bindings[j].Alt && bindings[j].Ctrl == bindings[i].Ctrl;
                }
            }

            BuildToolbar(palette);
            BuildDock(palette);
            BuildInspector(palette);
            BuildPartsPanel(palette);
            BuildBreadcrumb();
            BuildLesson(palette);   // the tutorial's steps and the first-time hints, along the circuit window's foot (Part 15f)
            Fit();
            BuildStage(palette);
            BuildCodeView(palette);
            BuildListingView(palette);
            RefreshDockButtons();
            BuildHome(palette);   // over the docked panels, under the popups, which come to the front as they open (Part 15a)
            BuildExercises(palette);   // over the start page, which it can be opened from (Part 15d)

            // Over everything else on the workbench, when it is up: in the shell, over its docked panels (Part 11b-3).
            _rowMenu = WaveRowMenu.Build(_shell, transform, palette, ChooseRadix, RowAction);
            _timeMenu = WaveTimeMenu.Build(_shell, transform, palette, TimeAction);
            _exportMenu = WaveExportMenu.Build(_shell, transform, palette, Export);
            _fileMenu = FileMenu.Build(_shell, transform, palette, FileChosen);
            BuildDocuments(palette);
            _ask = AskMenu.Build(_shell, transform, palette);
            _picker = WaveSignalPicker.Build(_shell, transform, palette, AddSignal);
            _rename = WaveRenameField.Build(_shell, transform, palette, RenameRow);
            _breakField = WaveRenameField.Build(_shell, transform, palette, BreakOnValue, WaveRenameField.BreakValueName, "BREAK ON VALUE",
                "1, 4'b1010, 'hA or x");
            _setField = WaveRenameField.Build(_shell, transform, palette, SetInputValue, WaveRenameField.SetValueName, "SET",
                "1, 4'b1010, 'hA or x");
            _canvasMenu = CanvasMenu.Build(_shell, transform, palette, CanvasMenuDo);
            _blockName = WaveRenameField.Build(_shell, transform, palette, MakeBlockNamed, WaveRenameField.BlockNameName, "NAME THE NEW BLOCK",
                "a module's name, half_adder");
            _keyMap = WorkbenchKeyMap.Build(_shell, transform, palette);

            Root.gameObject.SetActive(false);
        }

        /// <summary>
        /// The run of the sample shown, made a slice a frame and timed by real time: the reference
        /// circuit is made in its first slice, inside this call, so it opens exactly as it did when it
        /// was made in one go.
        /// </summary>
        private void OpenRun()
        {
            _session = new WorkbenchSession(_sample == 0 ? MyCircuit() : WorkbenchSamples.Chip[_sample].Build(), () => UnityEngine.Time.realtimeSinceStartupAsDouble * 1000.0);
            _run = _session.Run;
            _model = _run.Model;
            _playback = new Playback(_run, _speed);
            _shownEnd = _model.End;
            _reasonText = null;
            _notice = null;
            _shownListing = null;   // a new document's CODE tab starts on its module's file (Part 12e)
            _listingModule = null;
            ShowRunTo("");
            MakeTexts();

            // Its rows start as the layout's, one per port, each in its own radix, and no second cursor.
            _list = DefaultList(_model);
            _cursors.Clear();
            _appliedNames = new string[0];
            _appliedHeaders = new bool[0];
            ApplyRows();
            OpenCanvas();
        }

        /// <summary>
        /// An edit's replay took over (<see cref="WorkbenchSession"/>): playing moves onto it where it was,
        /// the rows are found again in it by their keys, every time is written again in its unit, and the
        /// stage is built round it with the circuit's zoom and the waveform's kept.
        /// </summary>
        private void SwitchRun()
        {
            SchematicLayout.Frame before = _model.Layout.Board;
            _run = _session.Run;
            _model = _run.Model;
            _playback.Switch(_run);
            _list.Resolve(_model.Signals);
            FollowPorts();
            KeepInPlace(before);
            _appliedSignals = new int[0];
            ApplyRows();
            _shownEnd = _model.End;
            _reasonText = null;
            MakeTexts();
            Relabel();
            SliderEnd(_model.End);
            RebuildStage(keepViews: true);
        }

        /// <summary>A model's default rows as a list: its layout's ports, by their keys.</summary>
        private static WaveList DefaultList(WorkbenchModel model)
        {
            var keys = new List<string>();
            foreach (WaveRow row in model.Layout.Rows)
                if (model.Session.Design.TryFind(row.Signal, out SignalId id))
                    keys.Add(model.Signals.KeyOf(id.Index));
            return new WaveList(model.Signals, keys);
        }

        /// <summary>
        /// Gives the model the list's rows, if they are not what it was last given: which signals, in
        /// which radix, under which names. True when they had changed.
        /// </summary>
        private bool ApplyRows()
        {
            _list.Rows(_rowSignals, _rowRadixes, _model.DefaultRadix);
            IReadOnlyList<WaveLine> lines = _list.Lines;
            bool same = _rowSignals.Count == _appliedSignals.Length && _appliedNames.Length == lines.Count && _appliedStyle == _list.Names;
            for (int i = 0; same && i < _rowSignals.Count; i++)
                same = _rowSignals[i] == _appliedSignals[i] && _rowRadixes[i] == _appliedRadixes[i] && Shown(lines[i]) == _appliedNames[i];
            if (same)
                return false;

            _appliedSignals = _rowSignals.ToArray();
            _appliedRadixes = _rowRadixes.ToArray();
            _appliedStyle = _list.Names;
            _appliedNames = new string[lines.Count];
            _appliedHeaders = new bool[lines.Count];
            for (int i = 0; i < lines.Count; i++)
            {
                _appliedNames[i] = Shown(lines[i]);
                _appliedHeaders[i] = lines[i].IsHeader;
            }

            _model.SetRows(_appliedSignals, _appliedRadixes);
            _bandsVersion = -1;

            // A breakpoint on a signal no row shows would pause the run with nothing on screen saying why.
            int[] shown = _appliedSignals;
            _playback.Breakpoints.KeepOnly(signal => Array.IndexOf(shown, signal) >= 0);
            return true;
        }

        /// <summary>A line's name as its row shows it: a group's header marked - while it is open and + while it is shut.</summary>
        private string Shown(WaveLine line) =>
            line.IsHeader ? (line.Group.Expanded ? "- " : "+ ") + _list.LabelOf(line) : _list.LabelOf(line);

        /// <summary>
        /// The list changed by the waveform's own controls: the rows are given to the model, and the stage built again
        /// round them if they are new.
        /// </summary>
        private void RowsChanged()
        {
            if (ApplyRows() && _stage != null)
            {
                RebuildStage(keepViews: true);
            }
            else
            {
                Redraw();
            }
        }

        /// <summary>Selects row <paramref name="row"/> alone and writes it in <paramref name="radix"/>, as its menu does.</summary>
        public void SetRadix(int row, WaveRadix radix)
        {
            _list.Select(row);
            ChooseRadix(radix);
        }

        /// <summary>Places the second cursor at <paramref name="time"/>, as Shift and a click there does.</summary>
        public void PlaceSecond(double time)
        {
            if (_cursors.Place(time))
                Redraw();
        }

        /// <summary>Sets the selected rows' radix, from the row menu.</summary>
        private void ChooseRadix(WaveRadix radix)
        {
            _list.SetRadix(radix);
            RowsChanged();
        }

        /// <summary>Steps the selected rows to the radix after the first one's: binary, octal, hex, unsigned, signed, and an enum's names for one.</summary>
        private void StepRadix()
        {
            List<WaveSignal> selected = _list.SelectedSignals();
            if (selected.Count == 0 || selected[0].Signal < 0)
                return;

            WaveSignal first = selected[0];
            WaveRadix current = first.Radix ?? _model.DefaultRadix(first.Signal);
            bool names = _model.Session.Design.Signals[first.Signal].Enum != null;
            WaveRadix next = current == WaveRadix.Signed ? (names ? WaveRadix.Symbolic : WaveRadix.Binary)
                : current == WaveRadix.Symbolic ? WaveRadix.Binary
                : current + 1;
            ChooseRadix(next);
        }

        /// <summary>Takes the selected rows out of the waveform.</summary>
        private void RemoveRows()
        {
            if (_list.RemoveSelected())
                RowsChanged();
        }

        /// <summary>
        /// Shows sample <paramref name="sample"/> of <see cref="WorkbenchSamples.All"/>, counted round:
        /// the stage is built again round its document and its run plays from the start. The title,
        /// the controls and the help line stay as they are.
        /// </summary>
        public void ShowSample(int sample)
        {
            if (Root == null)
                return;

            KeepMyCircuit(now: true);
            int count = WorkbenchSamples.Chip.Count;
            _sample = ((sample % count) + count) % count;

            HideHome();
            HideExercises();   // a circuit opened from FILE or the SAMPLE chip while the page is up (the #88 review)
            CloseThePopups();
            _transcript.Notes = null;   // what an import said is about the import, which this leaves (an import sets them again)
            _notesAbout = null;
            OpenRun();
            Relabel();   // the speed and RUN TO in this circuit's unit, which may not be the last one's
            RebuildStage(keepViews: false);
            BuildPartsPanel(SchematicPalette.Current);

            SetText(_description, Says());
            SetText(_sampleButton, ChipCaption(_sample));
            ShowExerciseControls();
            SliderEnd(_model.End);
            FitView();
            Restart();
            RememberWhatIsOpen(OpenKey());
        }

        /// <summary>Every popup of the workbench shut: what showing another circuit, or the start page, does first.</summary>
        private void CloseThePopups()
        {
            _rowMenu.Close();
            _timeMenu.Close();
            _exportMenu.Close();
            _fileMenu.Close();
            _openMenu?.Close();
            _circuitName?.Close();
            _helpMenu?.Close();
            _exerciseCard?.Close();
            _ask.Close();
            _libraryMenu?.Close();
            _budgetField?.Close();
            _picker.Close();
            _rename.Close();
            _blockName.Close();
            _breakField.Close();
            _setField.Close();
        }

        /// <summary>
        /// Builds the stage again round the model as it is now -- another circuit, or other rows --
        /// keeping the circuit's zoom and the waveform's when <paramref name="keepViews"/>.
        /// </summary>
        private void RebuildStage(bool keepViews)
        {
            SchematicView view = keepViews ? _viewState : null;
            WaveView wave = keepViews && _wave != null ? _wave.View : null;

            // Out of sight at once, though it is destroyed only at the end of the frame, so nothing
            // finds two of anything meanwhile.
            int place = _stage.GetSiblingIndex();
            _stage.gameObject.SetActive(false);
            if (Application.isPlaying)
                Destroy(_stage.gameObject);
            else
                DestroyImmediate(_stage.gameObject);

            _digits.Clear();
            _swallowedLabels.Clear();
            _hovered = -1;
            _hoverSays = null;
            _lit = -1;
            _bandsVersion = -1;
            _dragging = false;
            if (!keepViews)
                _wavesScroll = 0f;

            BuildStage(SchematicPalette.Current, view, wave);
            _stage.SetSiblingIndex(place);
            ShowDockTab(_dockTab);
            RefreshDockButtons();
            ScrollWavesBy(0f);
            ApplyView();
            Redraw();
        }

        /// <summary>How tall SAMPLE is: short of the description line under it.</summary>
        private const float SampleHeight = 34f;

        /// <summary>Where the chip starts: past STEP, the last of the row's buttons, and its key's room; and the most it takes.</summary>
        private const float ChipLeft = 500f, ChipMostWidth = 640f, ChipLineHeight = 26f;

        /// <summary>The second row's right-hand pieces: the speed between SLOWER and FASTER, and RUN TO's caption and field.</summary>
        private const float SpeedButtonWidth = 36f, SpeedLabelWidth = 96f, RunToCaptionWidth = 64f, RunToFieldWidth = 84f, TimeWidth = 136f, RowGap = 12f;

        private void BuildStage(SchematicPalette palette, SchematicView keepView = null, WaveView keepWave = null)
        {
            // The drawings, each under the panel that frames it (PlaceStage), at their own size: a small window gives
            // them less room, never smaller words.
            _stage = UiTheme.Rect("stage", Root);
            UiTheme.Stretch(_stage);

            SchematicLayout layout = _model.Layout;

            // The window the circuit is seen through, which clips whatever a zoom or a drag takes
            // past its edges.
            Rect2 circuit = _regions.Circuit;
            _view = UiTheme.Rect("schematic view", _stage);
            TopLeft(_view, CornerOf(circuit), SizeOf(circuit));
            _view.gameObject.AddComponent<RectMask2D>();
            var window = new Vec2(circuit.width, circuit.height);
            _viewState = keepView ?? new SchematicView(window, CircuitHome, layout.Size, Blank(layout));
            _viewState.Blank = Blank(layout);
            _viewState.Resize(window);

            _schematic = UiTheme.Rect("schematic", _view);
            TopLeft(_schematic, _viewState.Offset.ToUnity(), layout.Size.ToUnity());

            // Every layer of the drawing cut into chunks a mesh each can hold (WorkbenchLayers).
            _layers = new WorkbenchLayers(_model, _cursors, _playback.Breakpoints);
            _drawnZoom = _viewState.Zoom;
            _layers.BuildWires(_schematic, layout.Size.ToUnity());
            _rings = UiTheme.Rect("ring digits", _schematic);
            TopLeft(_rings, Vector2.zero, layout.Size.ToUnity());
            _layers.BuildParts(_schematic, layout.Size.ToUnity());
            BuildSchematicLabels(palette);
            BuildOverlay(layout);
            BuildMinimap();

            // FIT, in the window's top-right corner, for whenever the circuit is not whole.
            _fit = CanvasButton(FitButton, _view, "FIT", out TextMeshProUGUI _, UiType.Body, palette);
            KeyBeside(_fit, "F", palette);
            TopRight(_fit.GetComponent<RectTransform>(), new Vector2(-8f, 8f), new Vector2(72f, 32f));
            _fit.onClick.AddListener(() => { FitView(); UiTheme.Defocus(); });
            Show(_fit, false);

            // The chip that names the net under the pointer, over everything else in the window.
            _hoverChip = CanvasChip("hover", _view, palette);
            TopLeft(_hoverChip.rectTransform, Vector2.zero, new Vector2(200f, 26f));
            _hoverChip.raycastTarget = false;
            _hoverLabel = UiTheme.Label("text", _hoverChip.transform, UiType.Caption, palette.Text, TextAlignmentOptions.MidlineLeft);
            UiTheme.Stretch(_hoverLabel.rectTransform, 10f);
            Show(_hoverChip, false);

            // The waves' window, which clips them, and the rows inside it, which the wheel over the names scrolls.
            Rect2 waves = _regions.Waves;
            _wavesClip = UiTheme.Rect("waves", _stage);
            TopLeft(_wavesClip, CornerOf(waves), SizeOf(waves));
            _wavesClip.gameObject.AddComponent<RectMask2D>();
            _wavesContent = UiTheme.Rect("rows", _wavesClip);
            TopLeft(_wavesContent, Vector2.zero, new Vector2(waves.width, WavesAt.y + WorkbenchDrawing.WaveHeight(_model.RowCount)));
            _wavesWidth = waves.width;
            _wave = new WaveformView(_model, _layers, _wavesContent, WavesAt, waves.width - 2f * WavesAt.x, palette, _appliedNames, keepWave,
                _appliedHeaders, clip: _wavesClip);

            // Where waves that scroll are: a thin bar down the window's right, there only when they do.
            _wavesThumb = UiTheme.Panel_("scroll", _stage, palette.Muted);
            _wavesThumb.sprite = null;
            _wavesThumb.raycastTarget = false;
            Show(_wavesThumb, false);

            // The legend at the dock's foot: its swatches drawn, its words over them.
            Rect2 legend = _regions.Legend;
            _legend = Layer("legend", _stage, WorkbenchGraphic.Layer.Legend, CornerOf(legend), SizeOf(legend));
            var words = (RectTransform)_legend.transform;
            float middle = legend.height * 0.5f;
            for (int i = 0; i < LegendNames.Length; i++)
                StageLabel(words, LegendNames[i], new Vector2(WorkbenchDrawing.LegendNameX(i), middle),
                    UiType.Micro, palette.TextDim, TextAlignmentOptions.MidlineLeft, 20f);
            StageLabel(words, LegendText, new Vector2(WorkbenchDrawing.LegendTextX, middle),
                UiType.Micro, palette.TextDim, TextAlignmentOptions.MidlineLeft, Mathf.Max(0f, legend.width - WorkbenchDrawing.LegendTextX - 16f));
            PlaceCodeView();
            PlaceListing();
        }

        /// <summary>
        /// The drawings moved under the panels as they are laid out now (<see cref="Fit"/>), nothing built again: the
        /// circuit's window made another size and the view with it, the waves' window, the legend and the minimap.
        /// </summary>
        private void PlaceStage()
        {
            Rect2 circuit = _regions.Circuit, waves = _regions.Waves, legend = _regions.Legend;
            TopLeft(_view, CornerOf(circuit), SizeOf(circuit));
            _viewState.Resize(new Vec2(circuit.width, circuit.height));
            TopLeft(_wavesClip, CornerOf(waves), SizeOf(waves));
            TopLeft((RectTransform)_legend.transform, CornerOf(legend), SizeOf(legend));
            PlaceCodeView();
            PlaceListing();
            ScrollWavesBy(0f);
            ApplyView();
            Redraw();
        }

        private WorkbenchGraphic Layer(string name, RectTransform parent, WorkbenchGraphic.Layer draws, Vector2 at, Vector2 size)
        {
            RectTransform rect = UiTheme.Rect(name, parent);
            TopLeft(rect, at, size);

            var graphic = rect.gameObject.AddComponent<WorkbenchGraphic>();
            graphic.Draws = draws;
            graphic.Model = _model;
            graphic.raycastTarget = false;
            return graphic;
        }

        /// <summary>The labels that never change: the parts' names and delays, the ports, the buses' widths.</summary>
        private void BuildSchematicLabels(SchematicPalette palette)
        {
            SchematicLayout layout = _model.Layout;

            foreach (PartLayout part in layout.Parts)
            {
                Rect bounds = StageBounds(part);

                if (part.Symbol == PartSymbol.Constant)
                {
                    // A constant's value is its label, inside its box.
                    StageLabel(_schematic, part.Name, bounds.center, UiType.Micro, palette.Text, TextAlignmentOptions.Center, bounds.width);
                    continue;
                }

                // A gate's kind inside its body, as the reference writes it; other names over the part.
                if (PartLayout.NamedInside(part.Symbol))
                    StageLabel(_schematic, part.Name, part.ToStage(0.05, 0).ToUnity(), UiType.Micro, palette.TextDim,
                        TextAlignmentOptions.Center, part.Cell);
                else if (PartLayout.NameAbove(part.Symbol))
                {
                    // Over what is drawn. A triangle is a third of a cell tall where its outline is half,
                    // and placed from the outline its name floated as near the row above as its own
                    // part (Part 8's review); over the triangle's taller back it is plainly the part's.
                    Vector2 over = Triangle(part.Symbol) ? part.ToStage(-0.2, 0.32).ToUnity() : new Vector2(bounds.center.x, bounds.yMin);
                    if (part.Part is BitSorter.Board.BlockPart { Copies: > 1 })
                        over.y -= (float)WorkbenchDrawing.ArrayBehind * part.Cell;
                    StageLabel(_schematic, part.Name, new Vector2(over.x, over.y - 9f), UiType.Micro, palette.TextDim,
                        TextAlignmentOptions.Center, Mathf.Max(80f, bounds.width + 40f));
                }

                // Its delay just past its output, over the wire leaving it: under or over the body it
                // lands on an enable wire or the part below.
                if (part.Delay.Length > 0)
                {
                    const float delayWidth = 160f;
                    Vector2 at = part.ToStage(0.55, 0.3).ToUnity();
                    bool leftward = (part.ToStage(1, 0) - part.ToStage(0, 0)).x < -0.5f * part.Cell;
                    StageLabel(_schematic, part.Delay, leftward ? at - new Vector2(delayWidth, 0f) : at, UiType.Micro, palette.Muted,
                        leftward ? TextAlignmentOptions.MidlineRight : TextAlignmentOptions.MidlineLeft, delayWidth);
                }

                if (part.Symbol == PartSymbol.Block)
                {
                    // A block's ports are named inside it, beside their pins: an output's name ends
                    // at its pin, so its label, given by its left edge, starts a label's width before.
                    const float pinWidth = 60f;
                    foreach (BitSorter.Board.PinSpot pin in part.Local.Pins)
                    {
                        bool input = pin.Facing == BitSorter.Board.Facing.Left;
                        Vector2 at = part.ToStage(input ? -1.15 : 1.15, pin.At.Y).ToUnity();
                        StageLabel(_schematic, pin.Pin, input ? at : at - new Vector2(pinWidth, 0f), UiType.Micro, palette.TextDim,
                            input ? TextAlignmentOptions.MidlineLeft : TextAlignmentOptions.MidlineRight, pinWidth);
                    }
                }
            }

            foreach (PortLayout port in layout.Ports)
                StageLabel(_schematic, port.Name, port.Box.center.ToUnity(), UiType.Caption, palette.Text, TextAlignmentOptions.Center, port.Width);

            foreach (NetLayout net in layout.Nets)
            {
                if (net.Width <= 1)
                    continue;

                Vector2 at = net.Branches[0].PointAt(net.Branches[0].Length * 0.28f).ToUnity();
                StageLabel(_schematic, net.Width.ToString(CultureInfo.InvariantCulture), at + new Vector2(4f, -15f),
                    UiType.Micro, palette.Muted, TextAlignmentOptions.MidlineLeft, 30f);
            }
        }

        private static bool Triangle(PartSymbol symbol) =>
            symbol == PartSymbol.Not || symbol == PartSymbol.Buffer || symbol == PartSymbol.TriBuffer || symbol == PartSymbol.TriInverter;

        /// <summary>Where a part's outline is drawn on the stage, turned and all: what its labels are placed round.</summary>
        private static Rect StageBounds(PartLayout part)
        {
            Vector2 a = part.ToStage(part.Local.Left, part.Local.Bottom).ToUnity(), b = part.ToStage(part.Local.Right, part.Local.Top).ToUnity();
            return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
        }

        private void MakeTexts()
        {
            int steps = (int)(_model.End * 10) + 1;
            _timeTexts = new string[steps];
            _playheadTexts = new string[steps];

            for (int i = 0; i < steps; i++)
            {
                string moment = (i / 10.0).ToString("0.0", CultureInfo.InvariantCulture);
                _timeTexts[i] = "t = " + moment + " " + _model.Unit;
                _playheadTexts[i] = moment + " " + _model.Unit;
            }

            _inFlightTexts = new string[33];
            _inFlightTexts[0] = "waiting";
            _inFlightTexts[1] = "1 change in flight";
            for (int i = 2; i < _inFlightTexts.Length; i++)
                _inFlightTexts[i] = i.ToString(CultureInfo.InvariantCulture) + " changes in flight";

            _settledText = SettledText();
        }

        /// <summary>What the chip says once nothing more happens: known once the run is made.</summary>
        private string SettledText() =>
            _model.Complete ? "settled at t = " + _model.SettledAt.ToString(CultureInfo.InvariantCulture) + " " + _model.Unit : "";

        // -----------------------------------------------------------------
        // Drawing a moment
        // -----------------------------------------------------------------

        private void Redraw()
        {
            if (_model == null || Root == null)
                return;

            // A zoomed waveform pages on after the playhead before anything is drawn for the moment.
            _wave.Follow(_model.Time);
            _layers.Refresh();

            if (_slider != null)
                _slider.SetValueWithoutNotify((float)_model.Time);

            int step = Mathf.Clamp(Mathf.RoundToInt((float)(_model.Time * 10.0)), 0, _timeTexts.Length - 1);
            SetText(_timeLabel, _timeTexts[step]);

            SetText(_chipLabel, ChipLine());

            RefreshPlayLabel();
            PlaceDigits();
            _wave.Place(_playheadTexts[step], _cursors);
        }

        private void RefreshPlayLabel()
        {
            SetText(_playButton, Playing ? "PAUSE" : "PLAY");
        }

        /// <summary>
        /// What the chip says: a refusal, until the next thing is done; else why playing stopped, while it
        /// is stopped for a reason; else how many changes are on their way, or when the run settled. A
        /// reason's words are made once, as it is first shown.
        /// </summary>
        private string ChipLine()
        {
            if (_notice != null)
                return _notice;

            PauseReason reason = _playback.Reason;
            if (reason != PauseReason.None && reason != PauseReason.End)
            {
                if (_reasonText == null || reason != _reasonShown || _playback.ReasonTime != _reasonShownAt)
                {
                    _reasonShown = reason;
                    _reasonShownAt = _playback.ReasonTime;
                    _reasonText = ReasonText();
                }

                return _reasonText;
            }

            return _model.Time >= _model.SettledAt ? _settledText
                : _inFlightTexts[Mathf.Min(_model.InFlight, _inFlightTexts.Length - 1)];
        }

        /// <summary>Why playing stopped, in words: where, and what stopped it.</summary>
        private string ReasonText()
        {
            string at = Moment(_playback.ReasonTime);
            switch (_playback.Reason)
            {
                case PauseReason.Suspended: return "$stop at " + at + ": PLAY runs on";
                case PauseReason.Breakpoint: return BreakText(_playback.ReasonBreak) + " at " + at;
                case PauseReason.RunTo: return "ran to " + at;
                case PauseReason.Finished: return "$finish at " + at;
                case PauseReason.Stopped: return "the run stopped at " + at;
                default: return "";
            }
        }

        /// <summary>What a breakpoint waited for, as the chip says it fired.</summary>
        private string BreakText(int index)
        {
            Breakpoints breakpoints = _playback.Breakpoints;
            if (index < 0 || index >= breakpoints.Count || breakpoints[index].Kind == BreakKind.Time)
                return "break";

            Breakpoint b = breakpoints[index];
            string name = _model.Signals != null ? _model.Signals.ShortName(b.Signal) : _model.Session.Design.Signals[b.Signal].Name;
            return b.Kind == BreakKind.AnyX ? "break: " + name + " went x"
                : "break: " + name + " = " + ValueText.Of(b.Value, _model.DefaultRadix(b.Signal));
        }

        /// <summary>A whole moment as the workbench writes it, in the run's unit.</summary>
        private string Moment(long time) => time.ToString(CultureInfo.InvariantCulture) + " " + _model.Unit;

        /// <summary>Each change's digit on its ring, and "swallowed" over one that never arrived.</summary>
        private void PlaceDigits()
        {
            SchematicPalette palette = SchematicPalette.Current;
            IReadOnlyList<ChangeMarker> markers = _model.Markers;
            int swallowed = 0;

            for (int i = 0; i < markers.Count; i++)
            {
                ChangeMarker m = markers[i];

                if (i == _digits.Count)
                    _digits.Add(StageLabel(_rings, "", Vector2.zero, UiType.Micro, palette.Text, TextAlignmentOptions.Center, 30f));

                TextMeshProUGUI digit = _digits[i];
                Show(digit, true);
                SetText(digit, m.Text);
                Color colour = palette.Of(m.Value);
                digit.color = new Color(colour.r, colour.g, colour.b, m.Alpha);
                digit.rectTransform.anchoredPosition = new Vector2(m.Point.x, -m.Point.y);

                if (m.Swallowed)
                {
                    if (swallowed == _swallowedLabels.Count)
                        _swallowedLabels.Add(StageLabel(_rings, "swallowed", Vector2.zero, UiType.Micro, palette.TextDim,
                            TextAlignmentOptions.Center, 90f));

                    TextMeshProUGUI label = _swallowedLabels[swallowed++];
                    Show(label, true);
                    label.alpha = m.Alpha;
                    label.rectTransform.anchoredPosition = new Vector2(m.Point.x, -(m.Point.y - 20f));
                }
            }

            for (int i = markers.Count; i < _digits.Count; i++) Show(_digits[i], false);
            for (int i = swallowed; i < _swallowedLabels.Count; i++) Show(_swallowedLabels[i], false);
        }

        // -----------------------------------------------------------------
        // The waves' rows, scrolled
        // -----------------------------------------------------------------

        /// <summary>How far the waves' rows are scrolled, and how far they can be: their height past their window's.</summary>
        public float WavesScroll => _wavesScroll;

        /// <inheritdoc cref="WavesScroll"/>
        public float WavesOverflow => _wavesClip == null ? 0f : Mathf.Max(0f, _wavesContent.rect.height - _wavesClip.rect.height);

        /// <summary>
        /// A turn of the wheel over the waves' window off the time axis -- the names and the values -- scrolls the rows a row
        /// a notch, when there are more than the window shows. True when they moved.
        /// </summary>
        public bool TakeScroll(Vector2 screenPoint, Vector2 scroll)
        {
            if (Mathf.Abs(scroll.y) < 0.01f || _wavesClip == null ||
                !RectTransformUtility.RectangleContainsScreenPoint(_wavesClip, screenPoint, null))
            {
                return false;
            }

            return ScrollWavesBy(scroll.y > 0f ? -WorkbenchDrawing.RowHeight : WorkbenchDrawing.RowHeight);
        }

        /// <summary>Moves the rows by <paramref name="units"/>, within what they can scroll, and the bar with them. True when they moved.</summary>
        private bool ScrollWavesBy(float units)
        {
            if (_wavesClip == null)
                return false;

            float overflow = WavesOverflow;
            float scroll = Mathf.Clamp(_wavesScroll + units, 0f, overflow);
            bool moved = !Mathf.Approximately(scroll, _wavesScroll);
            _wavesScroll = scroll;
            _wavesContent.anchoredPosition = new Vector2(0f, _wavesScroll);

            bool scrolls = overflow > 0.5f;
            Show(_wavesThumb, scrolls);
            if (scrolls)
            {
                Rect2 waves = _regions.Waves;
                float room = waves.height;
                float thumb = Mathf.Max(24f, room * room / (room + overflow));
                TopLeft(_wavesThumb.rectTransform, new Vector2(waves.xMax - 8f, waves.y + (room - thumb) * (_wavesScroll / overflow)),
                    new Vector2(4f, thumb));
            }

            return moved;
        }

        // -----------------------------------------------------------------
        // Small helpers
        // -----------------------------------------------------------------

        // -----------------------------------------------------------------
        // The workbench's own controls, in the canvas's colours
        // -----------------------------------------------------------------

        /// <summary>How wide an outlined control's edge is.</summary>
        private const float EdgeWidth = 1.5f;

        /// <summary>
        /// A button as the reference draws REPLAY: a thin outline round the ground, its caption in the
        /// text colour. In the canvas's colours rather than the game's, which in Neon Board's
        /// bordered style darkens a button's body whatever colour it is given -- on the light canvas
        /// every button came out a dark slab.
        /// </summary>
        internal static Button CanvasButton(string name, Transform parent, string caption, out TextMeshProUGUI label, UiType type,
            SchematicPalette palette)
        {
            Button button = UiTheme.Button_(name, parent, caption, out label, type, ButtonRole.Secondary);
            Outline((Image)button.targetGraphic, palette.Outline, palette.Ground);
            label.color = palette.Text;
            label.transform.SetAsLastSibling();
            return button;
        }

        /// <summary>A button's key in a cap just left of it, for one with no room over it.</summary>
        internal static void KeyBeside(Button button, string key, SchematicPalette palette)
        {
            RectTransform cap = KeyCap(button, key, palette);
            UiTheme.Anchor(cap, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-6f, 0f), cap.sizeDelta);
        }

        /// <summary>The room a key's cap beside a button takes, gap included.</summary>
        private const float KeyRoom = 34f;

        private static RectTransform KeyCap(Button button, string key, SchematicPalette palette)
        {
            RectTransform cap = UiTheme.KeyCap("key", button.transform, key);
            TextMeshProUGUI label = cap.GetComponentInChildren<TextMeshProUGUI>();
            Outline(cap.GetComponent<Image>(), palette.ChipEdge, palette.Chip);
            label.color = palette.TextDim;
            label.transform.SetAsLastSibling();
            return cap;
        }

        /// <summary>A small rounded chip in the canvas's colours: its edge, and its fill inside.</summary>
        internal static Image CanvasChip(string name, Transform parent, SchematicPalette palette)
        {
            Image chip = UiTheme.Panel_(name, parent, palette.ChipEdge);
            Outline(chip, palette.ChipEdge, palette.Chip);
            return chip;
        }

        /// <summary>
        /// Makes <paramref name="image"/> a filled rounded shape in <paramref name="edge"/> with one a
        /// hair smaller in <paramref name="fill"/> over it: an outline that is the same on any ground.
        /// </summary>
        private static void Outline(Image image, Color edge, Color fill)
        {
            image.sprite = ProceduralSprites.Panel(PanelStyle.Filled);
            image.type = Image.Type.Sliced;
            image.color = edge;

            Image inside = UiTheme.Panel_("fill", image.transform, fill);
            inside.sprite = ProceduralSprites.Panel(PanelStyle.Filled);
            inside.raycastTarget = false;
            UiTheme.Stretch(inside.rectTransform, EdgeWidth);
        }

        /// <summary>A label on the stage, its pivot at <paramref name="at"/> (stage units, y down) as its alignment says.</summary>
        internal static TextMeshProUGUI StageLabel(RectTransform parent, string text, Vector2 at, UiType type, Color colour,
            TextAlignmentOptions alignment, float width)
        {
            TextMeshProUGUI label = UiTheme.Label(text.Length > 0 ? text : "label", parent, type, colour, alignment);
            RectTransform rect = label.rectTransform;
            float pivotX = alignment == TextAlignmentOptions.MidlineRight ? 1f
                : alignment == TextAlignmentOptions.MidlineLeft ? 0f : 0.5f;

            // A right-aligned label is given by its box's left edge, as the others are by their anchor
            // point, and ends at that edge plus its width.
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(pivotX, 0.5f);
            rect.anchoredPosition = new Vector2(pivotX >= 1f ? at.x + width : at.x, -at.y);
            rect.sizeDelta = new Vector2(width, UiTheme.SizeOf(type) + 6f);

            label.text = text;
            return label;
        }

        /// <summary>Places a rect by its top-left corner, <paramref name="at"/> in y-down units from its parent's top-left.</summary>
        internal static void TopLeft(RectTransform rect, Vector2 at, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(at.x, -at.y);
            rect.sizeDelta = size;
        }

        /// <summary>Places a rect by its top-right corner, <paramref name="offset"/> left and down from its parent's.</summary>
        private static void TopRight(RectTransform rect, Vector2 offset, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.anchoredPosition = new Vector2(offset.x, -offset.y);
            rect.sizeDelta = size;
        }

        internal static void SetText(TextMeshProUGUI label, string text)
        {
            if (!ReferenceEquals(label.text, text))
                label.text = text;
        }

        internal static void Show(Component part, bool shown) => UiTheme.SetShown(part, shown);
    }
}
