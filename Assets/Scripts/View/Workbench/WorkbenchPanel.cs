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
    /// The 5.0 workbench, as a preview: a circuit run on BitSorter's own engine, drawn in the
    /// Schematic look, with its waveform under it -- the look reference's circuit, or one of the
    /// samples the SAMPLE chip steps through (<see cref="WorkbenchSamples"/>). Reached from the main menu.
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
    /// a browser owns: Space plays or pauses, R replays, S steps to the next sample, the arrows step
    /// through time, plus and minus zoom, F fits, up and down move the page, X and Delete the rows, C
    /// the second cursor.
    ///
    /// **The controls are two rows**: the buttons, then the time -- the slider and where it is. The
    /// run controls of Part 9b-3 go on the first row, beside the ones there now.
    ///
    /// **The run is made once, a slice a frame from when its circuit is shown** (<see cref="RunSession"/>);
    /// playing, replaying and dragging the slider back are the model answering for another moment
    /// (ADR-6). Nothing re-runs, which is why rewinding is free.
    ///
    /// A full-screen panel like Settings, and for the same reasons: it covers the 4.x board, which
    /// stays exactly as it was, takes Escape back to the menu, and hides the HUD while it is up.
    /// Everything on it is built here the first time it opens, so the 4.x game pays nothing for it
    /// until then.
    /// </remarks>
    public sealed class WorkbenchPanel : FullScreenPanel
    {
        [SerializeField] private MainMenu _menu;

        [Tooltip("Canvas the panel is built under. Found by type when left empty.")]
        [SerializeField] private Canvas _canvas;

        /// <summary>The objects' names, which the tests look for.</summary>
        public const string RootName = "Workbench panel";

        /// <inheritdoc cref="RootName"/>
        public const string BackButton = "Workbench back";

        /// <inheritdoc cref="RootName"/>
        public const string ReplayButton = "Workbench replay";

        /// <inheritdoc cref="RootName"/>
        public const string PlayButton = "Workbench play";

        /// <inheritdoc cref="RootName"/>
        public const string TimeSlider = "Workbench time";

        /// <inheritdoc cref="RootName"/>
        public const string FitButton = "Workbench fit";

        /// <inheritdoc cref="RootName"/>
        public const string SampleButton = "Workbench sample";

        /// <summary>The line along the bottom.</summary>
        public const string HelpLine =
            "scroll to zoom, shift and scroll to move through time, click the waves to seek, shift-click for a second cursor, right-click a row for its radix, escape to go back";

        /// <summary>What the SAMPLE chip says for a sample: its name, under the word for what the chip steps through.</summary>
        public static string SampleCaption(int sample) => "SAMPLE: " + WorkbenchSamples.All[sample].Name.ToUpperInvariant();

        // -----------------------------------------------------------------
        // The stage: the card holding the schematic and the waveform, in its own units
        // -----------------------------------------------------------------

        /// <summary>The narrowest the stage is, and where the schematic sits on it.</summary>
        public const float MinStageWidth = 760f;

        /// <inheritdoc cref="MinStageWidth"/>
        public static readonly Vector2 SchematicAt = new Vector2(20f, 14f);

        /// <summary>The stage's width: room for the schematic as its document lays it out, and never less than the waveform's.</summary>
        private float StageWidth => _model == null ? MinStageWidth : Mathf.Max(MinStageWidth, _model.Layout.Size.x + 2f * SchematicAt.x);

        /// <summary>Where the waveform sits: under the schematic, however tall that is.</summary>
        private Vector2 WaveformAt => new Vector2(5f, SchematicAt.y + (_model == null ? 360f : _model.Layout.Size.y) + 14f);

        /// <summary>The window the schematic is seen through: the card above the waveform, inside its edge.</summary>
        private static readonly Vector2 ViewAt = new Vector2(4f, 4f);

        /// <inheritdoc cref="ViewAt"/>
        private Vector2 ViewSize => new Vector2(StageWidth - 2f * ViewAt.x, WaveformAt.y - 2f * ViewAt.y);

        /// <summary>The waveform's width: the stage's, less its margin either side.</summary>
        private float WaveWidth => StageWidth - 2f * WaveformAt.x;

        private float StageHeight =>
            WaveformAt.y + WorkbenchDrawing.WaveHeight(_model == null ? 0 : _model.RowCount) + WorkbenchDrawing.LegendHeight;

        /// <summary>The legend's names for the four values, and what the rings and crosses mean.</summary>
        public const string LegendText = "A digit on a wire is a change travelling along it; a cross is one swallowed on the way.";

        private static readonly string[] LegendNames = { "0", "1", "x", "z" };

        /// <summary>Where the title, its line, the controls and the stage start, from the top of the screen.</summary>
        private const float TitleTop = 22f;

        /// <inheritdoc cref="TitleTop"/>
        private const float DescriptionTop = 64f;

        /// <inheritdoc cref="TitleTop"/>
        /// <remarks>Low enough under the description for a key's cap to sit over each button.</remarks>
        private const float ControlsTop = 122f;

        /// <inheritdoc cref="TitleTop"/>
        /// <remarks>The second row of the controls: the time, under the buttons.</remarks>
        private const float TimelineTop = ControlsTop + UiTheme.ButtonHeight + 12f;

        /// <inheritdoc cref="TitleTop"/>
        private const float StageTop = TimelineTop + UiTheme.ButtonHeight + 14f;

        /// <summary>
        /// The least the stage is drawn at. Its words are sized for 1, and a browser tab 1920 by 800,
        /// fitted, took every delay and row name to under ten units; shorter than that, the page scrolls.
        /// </summary>
        public const float MinStageScale = 1f;

        /// <summary>How far one notch of the wheel, or one press of up or down, moves a page that scrolls.</summary>
        public const float ScrollStep = 60f;

        /// <summary>Room kept under the stage for the help line.</summary>
        private const float BottomRoom = 76f;

        private const float BackWidth = 120f;

        // -----------------------------------------------------------------
        // State
        // -----------------------------------------------------------------

        private WorkbenchModel _model;

        /// <summary>The run being shown, made a slice a frame until it is done.</summary>
        private RunSession _run;
        private bool _playing;

        /// <summary>Which of <see cref="WorkbenchSamples.All"/> is shown.</summary>
        private int _sample;

        private RectTransform _header;
        private RectTransform _controls;
        private RectTransform _timeline;
        private RectTransform _stage;
        private RectTransform _page;
        private Image _scrollThumb;
        private float _overflow, _pageScroll;
        private RectTransform _view;
        private RectTransform _schematic;
        private WaveformView _wave;

        /// <summary>The waveform's rows as the student arranges them, and what the model and the stage were last given of them.</summary>
        private WaveList _list;
        private int[] _appliedSignals = new int[0];
        private WaveRadix[] _appliedRadixes = new WaveRadix[0];
        private string[] _appliedNames = new string[0];
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
        private WorkbenchControlSample _controlSample;

        /// <summary>Each binding's key, as the Input System has it, and whether a binding of the same key needs Shift.</summary>
        private Key[] _keys;
        private bool[] _keyHasShifted;
        private RectTransform _rings;

        private WorkbenchGraphic _card;
        private WorkbenchLayers _layers;

        /// <summary>The zoom the schematic's chunks were last drawn at.</summary>
        private float _drawnZoom = 1f;
        private Slider _slider;
        private Button _fit;

        private SchematicView _viewState;
        private Image _hoverChip;
        private TextMeshProUGUI _hoverLabel;
        private int _hovered = -1;
        private bool _dragging, _wasHeld;
        private Vector2 _dragFrom;
        private TextMeshProUGUI _timeLabel, _chipLabel, _playLabel, _description, _sampleLabel;

        private readonly List<TextMeshProUGUI> _digits = new List<TextMeshProUGUI>();
        private readonly List<TextMeshProUGUI> _swallowedLabels = new List<TextMeshProUGUI>();

        /// <summary>The scale the stage was last drawn at, so the edges' smoothing is redone only when it changes.</summary>
        private float _scale = -1f;

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
        public bool Playing => _playing;

        /// <summary>The run being shown; null until the panel has first opened.</summary>
        public WorkbenchModel Model => _model;

        /// <summary>The net the pointer is on, or -1.</summary>
        public int HoveredNet => _hovered;

        /// <summary>What the chip beside the pointer says, or null when it is not up.</summary>
        public string HoverText => _hoverChip != null && _hoverChip.gameObject.activeSelf ? _hoverLabel.text : null;

        /// <summary>How far the circuit is zoomed in: 1 is the whole of it.</summary>
        public float Zoom => _viewState != null ? _viewState.Zoom : 1f;

        /// <summary>Whether the circuit is shown whole, where it belongs.</summary>
        public bool IsFitted => _viewState == null || _viewState.IsFitted;

        /// <summary>Which sample is shown, as its place in <see cref="WorkbenchSamples.All"/>.</summary>
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
            if (_menu == null) _menu = FindFirstObjectByType<MainMenu>();
            if (_canvas == null) _canvas = FindFirstObjectByType<Canvas>();
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
            FitView();
            Replay();
        }

        /// <summary>
        /// The workbench's keys, read from <see cref="WorkbenchKeys"/>: each binding fires when its key
        /// goes down with its modifiers held -- Shift only where another binding of the key needs it, so
        /// plus is plus with or without Shift, and Shift and an arrow is a tenth of a step.
        /// </summary>
        private void TakeKeys(Keyboard keyboard)
        {
            bool shift = keyboard.shiftKey.isPressed, ctrl = keyboard.ctrlKey.isPressed;
            IReadOnlyList<KeyBinding> all = WorkbenchKeys.All;

            for (int i = 0; i < all.Count; i++)
            {
                KeyBinding binding = all[i];
                if (binding.Ctrl != ctrl || (binding.Shift != shift && (binding.Shift || _keyHasShifted[i])))
                    continue;
                if (!keyboard[_keys[i]].wasPressedThisFrame)
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
                case WorkbenchCommand.Replay: Replay(); break;
                case WorkbenchCommand.NextSample: ShowSample(_sample + 1); break;
                case WorkbenchCommand.StepForward: StepBy(1.0); break;
                case WorkbenchCommand.StepBack: StepBy(-1.0); break;
                case WorkbenchCommand.StepForwardFine: StepBy(0.1); break;
                case WorkbenchCommand.StepBackFine: StepBy(-0.1); break;
                case WorkbenchCommand.ZoomIn: ZoomAboutTheMiddle(1); break;
                case WorkbenchCommand.ZoomOut: ZoomAboutTheMiddle(-1); break;
                case WorkbenchCommand.Fit: FitView(); break;
                case WorkbenchCommand.PageUp: ScrollPageBy(-ScrollStep); break;
                case WorkbenchCommand.PageDown: ScrollPageBy(ScrollStep); break;
                case WorkbenchCommand.Radix: StepRadix(); break;
                case WorkbenchCommand.ClearCursor:
                    if (_cursors.Clear())
                        Redraw();
                    break;
                case WorkbenchCommand.RemoveRows: RemoveRows(); break;

                // The add list, groups and names come in Part 9b-2b's second half.
                case WorkbenchCommand.AddSignal:
                case WorkbenchCommand.Group:
                case WorkbenchCommand.Names:
                    break;
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
            if (_viewState != null && _viewState.ZoomAbout((ViewSize * 0.5f).ToStage(), notches))
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
        /// The mouse's buttons this frame: a press on the circuit's window starts a drag that moves the
        /// circuit with the pointer until the button is let go.
        /// </summary>
        /// <remarks>
        /// Only a press that lands on the window starts one, so the slider dragged down over the
        /// circuit stays a drag of the slider, and FIT over it stays a click.
        /// </remarks>
        private void TakeDrag(Vector2 screenPoint, bool held)
        {
            bool pressed = held && !_wasHeld;
            _wasHeld = held;

            if (!held)
            {
                _dragging = false;
                return;
            }

            if (pressed)
            {
                Keyboard keyboard = UiText.Keyboard;
                bool alt = keyboard != null && keyboard.altKey.isPressed;
                _dragging = InView(screenPoint, out _) && !OverFit(screenPoint) && !_controlSample.OverControl && !alt;
                _dragFrom = screenPoint;
                return;
            }

            if (!_dragging || screenPoint == _dragFrom)
                return;

            InView(_dragFrom, out Vector2 from);
            InView(screenPoint, out Vector2 to);
            _dragFrom = screenPoint;

            if (_viewState.Pan((to - from).ToStage()))
                ApplyView();
        }

        /// <summary>
        /// Lights the net under the pointer and puts the chip naming it beside the pointer; none while
        /// the circuit is being dragged, or with the pointer off it.
        /// </summary>
        private void Hover(Vector2 screenPoint)
        {
            int net = -1;
            if (!_dragging && InView(screenPoint, out Vector2 inView) &&
                RectTransformUtility.ScreenPointToLocalPointInRectangle(_schematic, screenPoint, null, out Vector2 local))
            {
                net = _model.Layout.NetAt(new Vec2(local.x, -local.y), SchematicStyle.HoverReach / _viewState.Zoom);

                if (net >= 0)
                {
                    // Below and right of the pointer, kept inside the window.
                    RectTransform chip = _hoverChip.rectTransform;
                    float x = Mathf.Min(inView.x + 14f, ViewSize.x - chip.sizeDelta.x - 4f);
                    chip.anchoredPosition = new Vector2(Mathf.Max(4f, x), -(inView.y + 18f));
                }
            }

            if (net == _hovered)
                return;

            _hovered = net;
            Show(_hoverChip, net >= 0);
            if (net >= 0)
            {
                _hoverLabel.text = _model.Layout.Nets[net].Says;
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
                var press = new WavePress { Button = left ? 0 : 1, Shift = shift, Ctrl = ctrl, Alt = alt, Row = -1 };
                if (overControl)
                    press.Region = WaveRegion.Control;
                else if (InView(pointer, out _))
                    press.Region = WaveRegion.Schematic;
                else if (_wave.RegionAt(pointer, out WaveRegion region, out int row, out double time))
                {
                    press.Region = region;
                    press.Row = row;
                    press.Time = Snapped(time, row);
                    if (_cursors.Second.HasValue)
                        press.FromSecond = _wave.PixelsBetween(time, _cursors.Second.Value);
                }

                _movedTo = double.NaN;
                _pointer.Press(press, _commands);
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
                    _rowMenu.Open(pointer, SelectedRowsAreEnums());
                    break;

                // Groups and adding a wire's signal come in Part 9b-2b's second half.
                case WaveGesture.ToggleGroup:
                case WaveGesture.AddWireSignal:
                    break;
            }
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

            // The edges' smoothing is a pixel wide, and a zoom changes how many units a pixel is; a pan
            // changes neither, and every frame of a drag is one.
            if (_viewState.Zoom != _drawnZoom)
            {
                _drawnZoom = _viewState.Zoom;
                _layers.RefreshSchematic();
            }
        }

        /// <summary>Starts the run again from 0, playing, with a zoomed waveform following it again.</summary>
        public void Replay()
        {
            _playing = true;
            _wave?.Restart(0.0);
            Seek(0.0);
        }

        /// <summary>Plays or pauses; from the end, plays again from the start, as a replay does, a zoomed waveform following it again.</summary>
        public void TogglePlay()
        {
            if (_model == null)
                return;

            if (!_playing && _model.Time >= _model.End)
            {
                _wave?.Restart(0.0);
                Seek(0.0);
            }

            _playing = !_playing;
            RefreshPlayLabel();
        }

        /// <summary>Stops playing where it is.</summary>
        public void Pause()
        {
            _playing = false;
            RefreshPlayLabel();
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

            // Escape closes what is on top of the workbench first -- the row menu -- and the workbench only
            // when nothing is.
            Keyboard keyboard = UiText.Keyboard;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame && !OpenedThisFrame && !UiEscape.AnyHolds)
            {
                Back();
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

                TakePointer(mouse, pointer, keyboard, overControl);
                if (_wave == null)
                    return;
                TakeDrag(pointer, mouse.leftButton.isPressed || mouse.middleButton.isPressed || mouse.rightButton.isPressed);
                Hover(pointer);
            }

            Light();

            // The run is made a slice a frame until it is done; a moment past where it has got waits.
            if (!_run.Done && _run.Frame())
            {
                _layers.Grew();
                if (_run.Done)
                    _settledText = SettledText();
                Redraw();
            }

            if (_playing)
            {
                double next = _model.Time + UnityEngine.Time.deltaTime * SchematicStyle.NanosecondsPerSecond;
                if (next >= _model.End && _model.Complete)
                {
                    next = _model.End;
                    _playing = false;
                }

                Seek(next);
            }
        }

        private void Back()
        {
            SetShowing(false);

            if (_menu != null)
                _menu.Show(true);
        }

        protected override void OnShown() => _scale = -1f;

        // -----------------------------------------------------------------
        // Building
        // -----------------------------------------------------------------

        private void Build()
        {
            SchematicPalette palette = SchematicPalette.Current;

            OpenRun();

            Image scrim = UiTheme.Scrim(RootName, _canvas.transform, palette.Ground);
            Root = scrim.GetComponent<RectTransform>();
            UiTheme.Stretch(Root);
            _controlSample = Root.gameObject.AddComponent<WorkbenchControlSample>();

            IReadOnlyList<KeyBinding> bindings = WorkbenchKeys.All;
            _keys = new Key[bindings.Count];
            _keyHasShifted = new bool[bindings.Count];
            for (int i = 0; i < bindings.Count; i++)
            {
                _keys[i] = (Key)Enum.Parse(typeof(Key), bindings[i].Key);
                for (int j = 0; j < bindings.Count; j++)
                    _keyHasShifted[i] |= bindings[j].Key == bindings[i].Key && bindings[j].Shift && bindings[j].Ctrl == bindings[i].Ctrl;
            }

            BuildHeader(palette);
            BuildControls(palette);
            BuildTimeline(palette);
            BuildStage(palette);

            Button back = CanvasButton(BackButton, Root, "BACK", out TextMeshProUGUI _, UiType.Body, palette);
            UiTheme.Anchor(back.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-UiTheme.Margin, -UiTheme.Margin), new Vector2(BackWidth, UiTheme.ButtonHeight));
            back.onClick.AddListener(() => { Back(); UiTheme.Defocus(); });

            TextMeshProUGUI help = UiTheme.Label("help", Root, UiTheme.HelpLineType, palette.TextDim, TextAlignmentOptions.Center);
            UiTheme.Anchor(help.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 30f), new Vector2(1300f, UiTheme.HelpLineHeight));
            help.text = HelpLine;

            // Over everything else on the workbench, when it is up.
            _rowMenu = WaveRowMenu.Build(Root, palette, ChooseRadix, RemoveRows);

            Root.gameObject.SetActive(false);
        }

        /// <summary>
        /// The run of the sample shown, made a slice a frame and timed by real time: the reference
        /// circuit is made in its first slice, inside this call, so it opens exactly as it did when it
        /// was made in one go.
        /// </summary>
        private void OpenRun()
        {
            _run = RunSession.Open(WorkbenchSamples.All[_sample].Build(), () => UnityEngine.Time.realtimeSinceStartupAsDouble * 1000.0);
            _model = _run.Model;
            MakeTexts();

            // Its rows start as the layout's, one per port, each in its own radix, and no second cursor.
            _list = DefaultList(_model);
            _cursors.Clear();
            _appliedNames = new string[0];
            ApplyRows();
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
            bool same = _rowSignals.Count == _appliedSignals.Length && _appliedNames.Length == lines.Count;
            for (int i = 0; same && i < _rowSignals.Count; i++)
                same = _rowSignals[i] == _appliedSignals[i] && _rowRadixes[i] == _appliedRadixes[i] && _list.LabelOf(lines[i]) == _appliedNames[i];
            if (same)
                return false;

            _appliedSignals = _rowSignals.ToArray();
            _appliedRadixes = _rowRadixes.ToArray();
            _appliedNames = new string[lines.Count];
            for (int i = 0; i < lines.Count; i++)
                _appliedNames[i] = _list.LabelOf(lines[i]);

            _model.SetRows(_appliedSignals, _appliedRadixes);
            _bandsVersion = -1;
            return true;
        }

        /// <summary>The list changed: the rows are given to the model, and the stage built again round them if they are new.</summary>
        private void RowsChanged()
        {
            if (ApplyRows() && _page != null)
                RebuildStage(keepViews: true);
            else
                Redraw();
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

            int count = WorkbenchSamples.All.Count;
            _sample = ((sample % count) + count) % count;

            _rowMenu.Close();
            OpenRun();
            RebuildStage(keepViews: false);

            _description.text = WorkbenchSamples.All[_sample].Says;
            _sampleLabel.text = SampleCaption(_sample);
            _slider.maxValue = _model.End;
            FitView();
            Replay();
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
            int place = _page.GetSiblingIndex();
            _page.gameObject.SetActive(false);
            if (Application.isPlaying)
                Destroy(_page.gameObject);
            else
                DestroyImmediate(_page.gameObject);

            _digits.Clear();
            _swallowedLabels.Clear();
            _hovered = -1;
            _lit = -1;
            _bandsVersion = -1;
            _dragging = false;
            if (!keepViews)
                _pageScroll = 0f;
            _overflow = 0f;

            BuildStage(SchematicPalette.Current, view, wave);
            _page.SetSiblingIndex(place);

            // The stage may be another size: fitted again, header and controls with it.
            _scale = -1f;
            Fit();
            ScrollPageBy(0f);
            ApplyView();
            Redraw();
        }

        private void BuildHeader(SchematicPalette palette)
        {
            _header = TopStrip("header", TitleTop, 80f);

            TextMeshProUGUI title = UiTheme.Label("title", _header, UiType.Heading, palette.Text);
            TopLeft(title.rectTransform, new Vector2(0f, 0f), new Vector2(240f, 36f));
            title.text = "WORKBENCH";
            title.fontStyle = FontStyles.Bold;

            float titleWidth = UiTheme.TextWidth(title.text, UiType.Heading) * 1.08f;

            Image chip = CanvasChip("preview chip", _header, palette);
            TopLeft(chip.rectTransform, new Vector2(titleWidth + 14f, 8f), new Vector2(86f, 24f));
            TextMeshProUGUI chipText = UiTheme.Label("text", chip.transform, UiType.Micro, palette.Muted, TextAlignmentOptions.Center);
            UiTheme.Stretch(chipText.rectTransform);
            chipText.text = "PREVIEW";

            // As wide as the header, which is as wide as the stage is drawn.
            TextMeshProUGUI description = UiTheme.Label("description", _header, UiType.Label, palette.TextDim);
            RectTransform line = description.rectTransform;
            line.anchorMin = new Vector2(0f, 1f);
            line.anchorMax = new Vector2(1f, 1f);
            line.pivot = new Vector2(0f, 1f);
            line.anchoredPosition = new Vector2(0f, -(DescriptionTop - TitleTop));
            line.sizeDelta = new Vector2(0f, 24f);
            description.text = WorkbenchSamples.All[_sample].Says;
            _description = description;
        }

        /// <summary>The first row of the controls: the buttons, and the chip saying what the run is doing.</summary>
        private void BuildControls(SchematicPalette palette)
        {
            _controls = TopStrip("controls", ControlsTop, UiTheme.ButtonHeight);

            Button replay = CanvasButton(ReplayButton, _controls, "REPLAY", out TextMeshProUGUI _, UiType.Body, palette);
            KeyOver(replay, "R", palette);
            TopLeft(replay.GetComponent<RectTransform>(), Vector2.zero, new Vector2(120f, UiTheme.ButtonHeight));
            replay.onClick.AddListener(() => { Replay(); UiTheme.Defocus(); });

            Button play = CanvasButton(PlayButton, _controls, "PAUSE", out _playLabel, UiType.Body, palette);
            KeyOver(play, "SPACE", palette);
            TopLeft(play.GetComponent<RectTransform>(), new Vector2(132f, 0f), new Vector2(110f, UiTheme.ButtonHeight));
            play.onClick.AddListener(() => { TogglePlay(); UiTheme.Defocus(); });

            // As wide as the longest sample's caption, so stepping through them never resizes it.
            float sampleWidth = 0f;
            for (int i = 0; i < WorkbenchSamples.All.Count; i++)
                sampleWidth = Mathf.Max(sampleWidth, UiTheme.TextWidth(SampleCaption(i), UiType.Body));

            Button sample = CanvasButton(SampleButton, _controls, SampleCaption(_sample), out _sampleLabel, UiType.Body, palette);
            KeyOver(sample, "S", palette);
            TopLeft(sample.GetComponent<RectTransform>(), new Vector2(254f, 0f), new Vector2(sampleWidth + 48f, UiTheme.ButtonHeight));
            sample.onClick.AddListener(() => { ShowSample(_sample + 1); UiTheme.Defocus(); });

            Image chip = CanvasChip("chip", _controls, palette);
            TopRight(chip.rectTransform, new Vector2(0f, 9f), new Vector2(210f, 26f));
            _chipLabel = UiTheme.Label("text", chip.transform, UiType.Caption, palette.TextDim, TextAlignmentOptions.Center);
            UiTheme.Stretch(_chipLabel.rectTransform);
        }

        /// <summary>The second row of the controls: the slider through the run, and the moment it is at.</summary>
        private void BuildTimeline(SchematicPalette palette)
        {
            _timeline = TopStrip("timeline", TimelineTop, UiTheme.ButtonHeight);

            _slider = UiTheme.Slider_(TimeSlider, _timeline, 0, (int)_model.End);
            InCanvasColours(_slider, palette);
            _slider.wholeNumbers = false;
            RectTransform sliderRect = _slider.GetComponent<RectTransform>();
            sliderRect.anchorMin = new Vector2(0f, 0f);
            sliderRect.anchorMax = new Vector2(1f, 1f);
            sliderRect.pivot = new Vector2(0.5f, 0.5f);
            sliderRect.offsetMin = new Vector2(0f, 0f);
            sliderRect.offsetMax = new Vector2(-160f, 0f);
            _slider.onValueChanged.AddListener(value => { Pause(); Seek(value); });

            _timeLabel = UiTheme.Label("time", _timeline, UiType.Body, palette.Text, TextAlignmentOptions.MidlineRight);
            TopRight(_timeLabel.rectTransform, Vector2.zero, new Vector2(136f, UiTheme.ButtonHeight));
        }

        private void BuildStage(SchematicPalette palette, SchematicView keepView = null, WaveView keepWave = null)
        {
            // The page the stage is seen through, under the controls: it clips the stage and scrolls it
            // when the window is too short for it at its own size (Fit).
            _page = UiTheme.Rect("page", Root);
            _page.anchorMin = Vector2.zero;
            _page.anchorMax = Vector2.one;
            _page.offsetMin = new Vector2(0f, BottomRoom);
            _page.offsetMax = new Vector2(0f, -StageTop);
            _page.gameObject.AddComponent<RectMask2D>();

            _stage = UiTheme.Rect("stage", _page);
            _stage.anchorMin = _stage.anchorMax = new Vector2(0.5f, 1f);
            _stage.pivot = new Vector2(0.5f, 1f);
            _stage.anchoredPosition = Vector2.zero;
            _stage.sizeDelta = new Vector2(StageWidth, StageHeight);

            // Where a page that scrolls is: a thin bar down the right, there only when it scrolls.
            _scrollThumb = UiTheme.Panel_("scroll", _page, palette.Muted);
            _scrollThumb.sprite = null;
            _scrollThumb.raycastTarget = false;
            _scrollThumb.rectTransform.anchorMin = _scrollThumb.rectTransform.anchorMax = new Vector2(1f, 1f);
            _scrollThumb.rectTransform.pivot = new Vector2(1f, 1f);
            Show(_scrollThumb, false);

            _card = Layer("card", _stage, WorkbenchGraphic.Layer.Card, Vector2.zero, new Vector2(StageWidth, StageHeight));

            SchematicLayout layout = _model.Layout;

            // The window the circuit is seen through, which clips whatever a zoom or a drag takes
            // past its edges.
            _view = UiTheme.Rect("schematic view", _stage);
            TopLeft(_view, ViewAt, ViewSize);
            _view.gameObject.AddComponent<RectMask2D>();
            _viewState = keepView ?? new SchematicView(ViewSize.ToStage(), (SchematicAt - ViewAt).ToStage(), layout.Size);

            _schematic = UiTheme.Rect("schematic", _view);
            TopLeft(_schematic, _viewState.Offset.ToUnity(), layout.Size.ToUnity());

            // Every layer of the drawing cut into chunks a mesh each can hold (WorkbenchLayers).
            _layers = new WorkbenchLayers(_model, _cursors);
            _drawnZoom = _viewState.Zoom;
            _layers.BuildWires(_schematic, layout.Size.ToUnity());
            _rings = UiTheme.Rect("ring digits", _schematic);
            TopLeft(_rings, Vector2.zero, layout.Size.ToUnity());
            _layers.BuildParts(_schematic, layout.Size.ToUnity());
            BuildSchematicLabels(palette);

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

            _wave = new WaveformView(_model, _layers, _stage, WaveformAt, WaveWidth, palette, _appliedNames, keepWave);

            // The legend's words; its swatches are the card's.
            float legend = WorkbenchDrawing.LegendMiddle(StageHeight);
            for (int i = 0; i < LegendNames.Length; i++)
                StageLabel(_stage, LegendNames[i], new Vector2(WorkbenchDrawing.LegendNameX(i), legend),
                    UiType.Micro, palette.TextDim, TextAlignmentOptions.MidlineLeft, 20f);
            StageLabel(_stage, LegendText, new Vector2(WorkbenchDrawing.LegendTextX, legend),
                UiType.Micro, palette.TextDim, TextAlignmentOptions.MidlineLeft, StageWidth - WorkbenchDrawing.LegendTextX - 16f);
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
                string ns = (i / 10.0).ToString("0.0", CultureInfo.InvariantCulture);
                _timeTexts[i] = "t = " + ns + " ns";
                _playheadTexts[i] = ns + " ns";
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
            _model.Complete ? "settled at t = " + _model.SettledAt.ToString(CultureInfo.InvariantCulture) + " ns" : "";

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

            int inFlight = _model.InFlight;
            SetText(_chipLabel, _model.Time >= _model.SettledAt ? _settledText
                : _inFlightTexts[Mathf.Min(inFlight, _inFlightTexts.Length - 1)]);

            RefreshPlayLabel();
            PlaceDigits();
            _wave.Place(_playheadTexts[step], _cursors);
        }

        private void RefreshPlayLabel()
        {
            if (_playLabel != null)
                SetText(_playLabel, _playing ? "PAUSE" : "PLAY");
        }

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
        // Fitting the stage to the window
        // -----------------------------------------------------------------

        /// <summary>
        /// Scales the stage to the room the window leaves under the controls, and lines the header
        /// and controls up with it.
        /// </summary>
        private void Fit()
        {
            var room = new Vector2(Root.rect.width - 4f * UiTheme.Margin, _page.rect.height);
            float scale = StageScale(room, new Vector2(StageWidth, StageHeight));

            float overflow = Overflow(room.y, StageHeight, scale);
            if (!Mathf.Approximately(overflow, _overflow))
            {
                _overflow = overflow;
                ScrollPageBy(0f);
            }

            if (Mathf.Approximately(scale, _scale))
                return;

            _scale = scale;
            _stage.localScale = new Vector3(scale, scale, 1f);
            _header.sizeDelta = new Vector2(StageWidth * scale, _header.sizeDelta.y);
            _controls.sizeDelta = new Vector2(StageWidth * scale, _controls.sizeDelta.y);
            _timeline.sizeDelta = new Vector2(StageWidth * scale, _timeline.sizeDelta.y);

            // The edges' smoothing is a pixel wide, and how many units a pixel is just changed.
            _card.SetVerticesDirty();
            _layers.Refresh(all: true);
        }

        /// <summary>
        /// The scale the stage is drawn at in <paramref name="room"/>: as large as fits, but never under
        /// <see cref="MinStageScale"/> for the height -- a page too short for the stage scrolls instead.
        /// </summary>
        public static float StageScale(Vector2 room, Vector2 stage) =>
            Mathf.Max(0.1f, Mathf.Min(room.x / stage.x, Mathf.Max(MinStageScale, room.y / stage.y)));

        /// <summary>How much of a stage <paramref name="stageHeight"/> tall, drawn at <paramref name="scale"/>, a page <paramref name="room"/> tall cannot show.</summary>
        public static float Overflow(float room, float stageHeight, float scale) => Mathf.Max(0f, stageHeight * scale - room);

        /// <summary>How far the page is scrolled, and how far it can be.</summary>
        public float PageScroll => _pageScroll;

        /// <inheritdoc cref="PageScroll"/>
        public float PageOverflow => _overflow;

        /// <summary>A turn of the wheel off the circuit: moves a page that scrolls, a notch at a time. True when it moved.</summary>
        public bool TakeScroll(Vector2 screenPoint, Vector2 scroll)
        {
            if (_overflow <= 0f || Mathf.Abs(scroll.y) < 0.01f || _page == null ||
                !RectTransformUtility.RectangleContainsScreenPoint(_page, screenPoint, null))
            {
                return false;
            }

            return ScrollPageBy(scroll.y > 0f ? -ScrollStep : ScrollStep);
        }

        /// <summary>Moves the page by <paramref name="units"/>, within what it can scroll, and its bar with it. True when it moved.</summary>
        private bool ScrollPageBy(float units)
        {
            if (_page == null)
                return false;

            float scroll = Mathf.Clamp(_pageScroll + units, 0f, _overflow);
            bool moved = !Mathf.Approximately(scroll, _pageScroll);
            _pageScroll = scroll;
            _stage.anchoredPosition = new Vector2(0f, _pageScroll);

            bool scrolls = _overflow > 0.5f;
            Show(_scrollThumb, scrolls);
            if (scrolls)
            {
                float room = _page.rect.height;
                float thumb = Mathf.Max(24f, room * room / (room + _overflow));
                _scrollThumb.rectTransform.sizeDelta = new Vector2(4f, thumb);
                _scrollThumb.rectTransform.anchoredPosition = new Vector2(-UiTheme.Margin, -(room - thumb) * (_pageScroll / _overflow));
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

        /// <summary>A button's key in a cap just over it, in the canvas's colours, as the 4.x HUD draws RUN's.</summary>
        private static void KeyOver(Button button, string key, SchematicPalette palette)
        {
            RectTransform cap = KeyCap(button, key, palette);
            UiTheme.Anchor(cap, new Vector2(0.5f, 1f), new Vector2(0.5f, 0f), new Vector2(0f, 4f), cap.sizeDelta);
        }

        /// <summary>A button's key in a cap just left of it, for one with no room over it.</summary>
        private static void KeyBeside(Button button, string key, SchematicPalette palette)
        {
            RectTransform cap = KeyCap(button, key, palette);
            UiTheme.Anchor(cap, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-6f, 0f), cap.sizeDelta);
        }

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

        /// <summary>The time slider as the reference draws it: the playhead's colour up to the moment shown, an outline's grey after, a thumb in the text colour.</summary>
        private static void InCanvasColours(Slider slider, SchematicPalette palette)
        {
            slider.fillRect.GetComponent<Image>().color = palette.Playhead;
            slider.handleRect.GetComponent<Image>().color = palette.Text;
            slider.transform.Find("track").GetComponent<Image>().color = palette.Outline;
        }

        /// <summary>A strip across the top of the screen, as wide as the stage is drawn, for the header and the controls.</summary>
        private RectTransform TopStrip(string name, float top, float height)
        {
            RectTransform strip = UiTheme.Rect(name, Root);
            strip.anchorMin = strip.anchorMax = new Vector2(0.5f, 1f);
            strip.pivot = new Vector2(0.5f, 1f);
            strip.anchoredPosition = new Vector2(0f, -top);
            strip.sizeDelta = new Vector2(StageWidth, height);
            return strip;
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
