using System;
using System.Collections.Generic;
using System.Globalization;
using BitSorter.SystemVerilog;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace BitSorter.View
{
    /// <summary>
    /// The 5.0 workbench, as a preview: the look reference's circuit run on BitSorter's own engine,
    /// drawn in the Schematic look, with its waveform under it. Reached from the main menu.
    /// </summary>
    /// <remarks>
    /// Part 3 of 5.0 put the new engine on screen early, from a circuit written in code; since Part 8
    /// everything drawn comes from a board document -- <see cref="ReferenceCircuit"/> is one, drawn
    /// through the editor -- by way of <see cref="DocumentSchematic"/>, its parts as their symbols and
    /// its wires where the document's geometry routes them.
    ///
    /// **The run is made once, to its end, when the panel first opens**; playing, replaying and
    /// dragging the slider back are the model answering for another moment (ADR-6). Nothing
    /// re-runs, which is why rewinding is free.
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

        /// <summary>What the panel says it is, under its title.</summary>
        public const string Description =
            "The look reference's circuit as a board document, run on BitSorter's 5.0 engine: every value, and every change on its way, is the engine's.";

        /// <summary>The line along the bottom.</summary>
        public const string HelpLine = "drag the slider to go back in time, escape to go back";

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

        /// <summary>The waveform's width: the stage's, less its margin either side.</summary>
        private float WaveWidth => StageWidth - 2f * WaveformAt.x;

        private float StageHeight =>
            WaveformAt.y + WorkbenchDrawing.WaveHeight(_model == null ? 0 : _model.Layout.Rows.Count) + WorkbenchDrawing.LegendHeight;

        /// <summary>The legend's names for the four values, and what the rings and crosses mean.</summary>
        public const string LegendText = "A digit on a wire is a change travelling along it; a cross is one swallowed on the way.";

        private static readonly string[] LegendNames = { "0", "1", "x", "z" };

        /// <summary>Where the title, its line, the controls and the stage start, from the top of the screen.</summary>
        private const float TitleTop = 22f;

        /// <inheritdoc cref="TitleTop"/>
        private const float DescriptionTop = 64f;

        /// <inheritdoc cref="TitleTop"/>
        private const float ControlsTop = 102f;

        /// <inheritdoc cref="TitleTop"/>
        private const float StageTop = 160f;

        /// <summary>Room kept under the stage for the help line.</summary>
        private const float BottomRoom = 76f;

        private const float BackWidth = 120f;

        // -----------------------------------------------------------------
        // State
        // -----------------------------------------------------------------

        private WorkbenchModel _model;
        private bool _playing;

        private RectTransform _header;
        private RectTransform _controls;
        private RectTransform _stage;
        private RectTransform _schematic;
        private RectTransform _waveform;
        private RectTransform _rings;

        private WorkbenchGraphic _card, _wires, _parts, _timing;
        private Slider _slider;
        private TextMeshProUGUI _timeLabel, _chipLabel, _playLabel, _playheadLabel;

        private readonly List<TextMeshProUGUI> _digits = new List<TextMeshProUGUI>();
        private readonly List<TextMeshProUGUI> _swallowedLabels = new List<TextMeshProUGUI>();
        private TextMeshProUGUI[] _readouts;
        private TextMeshProUGUI[][] _busLabels;
        private readonly List<TextMeshProUGUI> _glitchLabels = new List<TextMeshProUGUI>();

        private readonly List<WaveSegment> _segments = new List<WaveSegment>(16);
        private readonly List<double> _swallowed = new List<double>(4);

        /// <summary>The scale the stage was last drawn at, so the edges' smoothing is redone only when it changes.</summary>
        private float _scale = -1f;

        // Text, made once: a frame of playback formats nothing.
        private string[] _timeTexts, _playheadTexts, _inFlightTexts;
        private string _settledText;
        private readonly Dictionary<string, string> _hexReadouts = new Dictionary<string, string>();

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

        /// <summary>The colour a signal's wire was drawn in last: what the screen shows.</summary>
        public bool TryDrawnColour(string signal, out Color colour)
        {
            colour = default;
            return _wires != null && _wires.TryDrawnColour(signal, out colour);
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
            Replay();
        }

        /// <summary>Starts the run again from 0, playing.</summary>
        public void Replay()
        {
            _playing = true;
            Seek(0.0);
        }

        /// <summary>Plays or pauses; from the end, plays again from the start.</summary>
        public void TogglePlay()
        {
            if (_model == null)
                return;

            if (!_playing && _model.Time >= _model.End)
                Seek(0.0);

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

            Keyboard keyboard = UiText.Keyboard;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame && !OpenedThisFrame)
            {
                Back();
                return;
            }

            if (_playing)
            {
                double next = _model.Time + UnityEngine.Time.deltaTime * SchematicStyle.NanosecondsPerSecond;
                if (next >= _model.End)
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

            _model = ReferenceCircuit.Model();
            MakeTexts();

            Image scrim = UiTheme.Scrim(RootName, _canvas.transform, palette.Ground);
            Root = scrim.GetComponent<RectTransform>();
            UiTheme.Stretch(Root);

            BuildHeader(palette);
            BuildControls(palette);
            BuildStage(palette);

            Button back = UiTheme.Button_(BackButton, Root, "BACK", out TextMeshProUGUI _, UiType.Body, ButtonRole.Quiet);
            UiTheme.Anchor(back.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-UiTheme.Margin, -UiTheme.Margin), new Vector2(BackWidth, UiTheme.ButtonHeight));
            back.onClick.AddListener(() => { Back(); UiTheme.Defocus(); });

            TextMeshProUGUI help = UiTheme.Label("help", Root, UiTheme.HelpLineType, palette.TextDim, TextAlignmentOptions.Center);
            UiTheme.Anchor(help.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 30f), new Vector2(900f, UiTheme.HelpLineHeight));
            help.text = HelpLine;

            Root.gameObject.SetActive(false);
        }

        private void BuildHeader(SchematicPalette palette)
        {
            _header = TopStrip("header", TitleTop, 80f);

            TextMeshProUGUI title = UiTheme.Label("title", _header, UiType.Heading, palette.Text);
            TopLeft(title.rectTransform, new Vector2(0f, 0f), new Vector2(240f, 36f));
            title.text = "WORKBENCH";
            title.fontStyle = FontStyles.Bold;

            float titleWidth = UiTheme.TextWidth(title.text, UiType.Heading) * 1.08f;

            Image chip = UiTheme.Panel_("preview chip", _header, palette.Chip);
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
            description.text = Description;
        }

        private void BuildControls(SchematicPalette palette)
        {
            _controls = TopStrip("controls", ControlsTop, UiTheme.ButtonHeight);

            Button replay = UiTheme.Button_(ReplayButton, _controls, "REPLAY", out TextMeshProUGUI _, UiType.Body, ButtonRole.Secondary);
            TopLeft(replay.GetComponent<RectTransform>(), Vector2.zero, new Vector2(120f, UiTheme.ButtonHeight));
            replay.onClick.AddListener(() => { Replay(); UiTheme.Defocus(); });

            Button play = UiTheme.Button_(PlayButton, _controls, "PAUSE", out _playLabel, UiType.Body, ButtonRole.Quiet);
            TopLeft(play.GetComponent<RectTransform>(), new Vector2(132f, 0f), new Vector2(110f, UiTheme.ButtonHeight));
            play.onClick.AddListener(() => { TogglePlay(); UiTheme.Defocus(); });

            _slider = UiTheme.Slider_(TimeSlider, _controls, 0, (int)_model.End);
            _slider.wholeNumbers = false;
            RectTransform sliderRect = _slider.GetComponent<RectTransform>();
            sliderRect.anchorMin = new Vector2(0f, 0f);
            sliderRect.anchorMax = new Vector2(1f, 1f);
            sliderRect.pivot = new Vector2(0.5f, 0.5f);
            sliderRect.offsetMin = new Vector2(262f, 0f);
            sliderRect.offsetMax = new Vector2(-370f, 0f);
            _slider.onValueChanged.AddListener(value => { Pause(); Seek(value); });

            _timeLabel = UiTheme.Label("time", _controls, UiType.Body, palette.Text, TextAlignmentOptions.MidlineRight);
            TopRight(_timeLabel.rectTransform, new Vector2(-224f, 0f), new Vector2(136f, UiTheme.ButtonHeight));

            Image chip = UiTheme.Panel_("chip", _controls, palette.Chip);
            TopRight(chip.rectTransform, new Vector2(0f, 9f), new Vector2(210f, 26f));
            _chipLabel = UiTheme.Label("text", chip.transform, UiType.Caption, palette.TextDim, TextAlignmentOptions.Center);
            UiTheme.Stretch(_chipLabel.rectTransform);
        }

        private void BuildStage(SchematicPalette palette)
        {
            _stage = UiTheme.Rect("stage", Root);
            _stage.anchorMin = _stage.anchorMax = new Vector2(0.5f, 1f);
            _stage.pivot = new Vector2(0.5f, 1f);
            _stage.anchoredPosition = new Vector2(0f, -StageTop);
            _stage.sizeDelta = new Vector2(StageWidth, StageHeight);

            _card = Layer("card", _stage, WorkbenchGraphic.Layer.Card, Vector2.zero, new Vector2(StageWidth, StageHeight));

            SchematicLayout layout = _model.Layout;
            _schematic = UiTheme.Rect("schematic", _stage);
            TopLeft(_schematic, SchematicAt, layout.Size);

            _wires = Layer("wires", _schematic, WorkbenchGraphic.Layer.Wires, Vector2.zero, layout.Size);
            _rings = UiTheme.Rect("ring digits", _schematic);
            TopLeft(_rings, Vector2.zero, layout.Size);
            _parts = Layer("parts", _schematic, WorkbenchGraphic.Layer.Parts, Vector2.zero, layout.Size);
            BuildSchematicLabels(palette);

            float waveHeight = WorkbenchDrawing.WaveHeight(layout.Rows.Count);
            _waveform = UiTheme.Rect("waveform", _stage);
            TopLeft(_waveform, WaveformAt, new Vector2(WaveWidth, waveHeight));
            _timing = Layer("timing", _waveform, WorkbenchGraphic.Layer.Timing, Vector2.zero, new Vector2(WaveWidth, waveHeight));
            BuildWaveLabels(palette);

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
                    StageLabel(_schematic, part.Name, part.ToStage(0.05, 0), UiType.Micro, palette.TextDim,
                        TextAlignmentOptions.Center, part.Cell);
                else if (PartLayout.NameAbove(part.Symbol))
                    StageLabel(_schematic, part.Name, new Vector2(bounds.center.x, bounds.yMin - 9f), UiType.Micro, palette.TextDim,
                        TextAlignmentOptions.Center, Mathf.Max(80f, bounds.width + 40f));

                // Its delay just past its output, over the wire leaving it: under or over the body it
                // lands on an enable wire or the part below.
                if (part.Delay.Length > 0)
                {
                    const float delayWidth = 160f;
                    Vector2 at = part.ToStage(0.55, 0.3);
                    bool leftward = (part.ToStage(1, 0) - part.ToStage(0, 0)).x < -0.5f * part.Cell;
                    StageLabel(_schematic, part.Delay, leftward ? at - new Vector2(delayWidth, 0f) : at, UiType.Micro, palette.Muted,
                        leftward ? TextAlignmentOptions.MidlineRight : TextAlignmentOptions.MidlineLeft, delayWidth);
                }

                if (part.Symbol == PartSymbol.Block)
                {
                    // A block's ports are named inside it, beside their pins.
                    foreach (BitSorter.Board.PinSpot pin in part.Local.Pins)
                    {
                        bool input = pin.Facing == BitSorter.Board.Facing.Left;
                        Vector2 at = part.ToStage(input ? -1.15 : 1.15, pin.At.Y);
                        StageLabel(_schematic, pin.Pin, at, UiType.Micro, palette.TextDim,
                            input ? TextAlignmentOptions.MidlineLeft : TextAlignmentOptions.MidlineRight, 60f);
                    }
                }
            }

            foreach (PortLayout port in layout.Ports)
                StageLabel(_schematic, port.Name, port.Box.center, UiType.Caption, palette.Text, TextAlignmentOptions.Center, port.Width);

            foreach (NetLayout net in layout.Nets)
            {
                if (net.Width <= 1)
                    continue;

                Vector2 at = net.Branches[0].PointAt(net.Branches[0].Length * 0.28f);
                StageLabel(_schematic, net.Width.ToString(CultureInfo.InvariantCulture), at + new Vector2(4f, -15f),
                    UiType.Micro, palette.Muted, TextAlignmentOptions.MidlineLeft, 30f);
            }
        }

        /// <summary>Where a part's outline is drawn on the stage, turned and all: what its labels are placed round.</summary>
        private static Rect StageBounds(PartLayout part)
        {
            Vector2 a = part.ToStage(part.Local.Left, part.Local.Bottom), b = part.ToStage(part.Local.Right, part.Local.Top);
            return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
        }

        /// <summary>The waveform's tick numbers and row names, and the labels that follow the run.</summary>
        private void BuildWaveLabels(SchematicPalette palette)
        {
            long end = _model.End;
            for (long t = 0; t <= end; t += WorkbenchDrawing.TickEvery(end))
                StageLabel(_waveform, t.ToString(CultureInfo.InvariantCulture),
                    new Vector2(WorkbenchDrawing.WaveX(t, end, WaveWidth), WorkbenchDrawing.WaveTop - 14f),
                    UiType.Micro, palette.Muted, TextAlignmentOptions.Center, 30f);

            int rows = _model.Layout.Rows.Count;
            _readouts = new TextMeshProUGUI[rows];
            _busLabels = new TextMeshProUGUI[rows][];

            for (int row = 0; row < rows; row++)
            {
                float middle = WorkbenchDrawing.RowMiddle(row);
                StageLabel(_waveform, _model.Layout.Rows[row].Label, new Vector2(WorkbenchDrawing.NameRight - 120f, middle),
                    UiType.Micro, palette.TextDim, TextAlignmentOptions.MidlineRight, 120f);

                _readouts[row] = StageLabel(_waveform, "", new Vector2(WorkbenchDrawing.ReadoutLeft, middle),
                    UiType.Micro, palette.Text, TextAlignmentOptions.MidlineLeft, 60f);

                if (_model.RowIsBus(row))
                {
                    _busLabels[row] = new TextMeshProUGUI[_model.RowChangeCount(row)];
                    for (int i = 0; i < _busLabels[row].Length; i++)
                        _busLabels[row][i] = StageLabel(_waveform, "", new Vector2(0f, middle), UiType.Micro, palette.Text,
                            TextAlignmentOptions.Center, 60f);
                }
            }

            _playheadLabel = StageLabel(_waveform, "", new Vector2(0f, WorkbenchDrawing.WaveTop + 4f), UiType.Micro, palette.Accent,
                TextAlignmentOptions.MidlineLeft, 80f);
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

            _settledText = "settled at t = " + _model.SettledAt.ToString(CultureInfo.InvariantCulture) + " ns";
        }

        // -----------------------------------------------------------------
        // Drawing a moment
        // -----------------------------------------------------------------

        private void Redraw()
        {
            if (_model == null || Root == null)
                return;

            _wires.SetVerticesDirty();
            _parts.SetVerticesDirty();
            _timing.SetVerticesDirty();

            if (_slider != null)
                _slider.SetValueWithoutNotify((float)_model.Time);

            int step = Mathf.Clamp(Mathf.RoundToInt((float)(_model.Time * 10.0)), 0, _timeTexts.Length - 1);
            SetText(_timeLabel, _timeTexts[step]);

            int inFlight = _model.InFlight;
            SetText(_chipLabel, _model.Time >= _model.SettledAt ? _settledText
                : _inFlightTexts[Mathf.Min(inFlight, _inFlightTexts.Length - 1)]);

            RefreshPlayLabel();
            PlaceDigits();
            PlaceWaveLabels(step);
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

        /// <summary>The waveform's readouts, the values inside its bus boxes, the glitch marks' words and the playhead's time.</summary>
        private void PlaceWaveLabels(int step)
        {
            SchematicPalette palette = SchematicPalette.Current;
            long end = _model.End;
            int glitches = 0;

            for (int row = 0; row < _readouts.Length; row++)
            {
                float middle = WorkbenchDrawing.RowMiddle(row);
                LogicVec value = _model.RowValue(row, out string text);
                bool bus = _model.RowIsBus(row);

                SetText(_readouts[row], bus ? HexReadout(text) : text);
                _readouts[row].color = palette.Of(value);

                if (bus)
                {
                    _model.RowSegments(row, _segments);
                    TextMeshProUGUI[] labels = _busLabels[row];

                    for (int i = 0; i < labels.Length; i++)
                    {
                        bool shown = i < _segments.Count && !_segments[i].Value.IsAllZ;
                        float x0 = shown ? WorkbenchDrawing.WaveX(_segments[i].From, end, WaveWidth) : 0f;
                        float x1 = shown ? WorkbenchDrawing.WaveX(_segments[i].To, end, WaveWidth) : 0f;
                        shown &= x1 - x0 > 22f;
                        Show(labels[i], shown);

                        if (!shown)
                            continue;

                        SetText(labels[i], _segments[i].Text);
                        labels[i].color = _segments[i].Value.IsKnown ? palette.Text : palette.Unknown;
                        labels[i].rectTransform.anchoredPosition = new Vector2((x0 + x1) * 0.5f, -middle);
                    }
                }

                _model.RowSwallowed(row, _swallowed);
                foreach (double at in _swallowed)
                {
                    if (glitches == _glitchLabels.Count)
                        _glitchLabels.Add(StageLabel(_waveform, "glitch swallowed", Vector2.zero, UiType.Micro, palette.TextDim,
                            TextAlignmentOptions.MidlineLeft, 140f));

                    TextMeshProUGUI label = _glitchLabels[glitches++];
                    Show(label, true);
                    label.rectTransform.anchoredPosition = new Vector2(WorkbenchDrawing.WaveX(at, end, WaveWidth) + 8f, -(middle - 7f));
                }
            }

            for (int i = glitches; i < _glitchLabels.Count; i++) Show(_glitchLabels[i], false);

            float x = Mathf.Min(WorkbenchDrawing.WaveX(_model.Time, end, WaveWidth) + 5f, WorkbenchDrawing.WaveRight(WaveWidth) - 52f);
            _playheadLabel.rectTransform.anchoredPosition = new Vector2(x, -(WorkbenchDrawing.WaveTop + 4f));
            SetText(_playheadLabel, _playheadTexts[step]);
        }

        /// <summary>A bus's readout: its hex digits marked as hex, "3h", as ModelSim marks them. Made once per value.</summary>
        private string HexReadout(string digits)
        {
            if (digits == "x" || digits == "z")
                return digits;

            if (!_hexReadouts.TryGetValue(digits, out string readout))
                _hexReadouts[digits] = readout = digits + "h";

            return readout;
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
            float roomWidth = Root.rect.width - 4f * UiTheme.Margin;
            float roomHeight = Root.rect.height - StageTop - BottomRoom;
            float scale = Mathf.Max(0.1f, Mathf.Min(roomWidth / StageWidth, roomHeight / StageHeight));

            if (Mathf.Approximately(scale, _scale))
                return;

            _scale = scale;
            _stage.localScale = new Vector3(scale, scale, 1f);
            _header.sizeDelta = new Vector2(StageWidth * scale, _header.sizeDelta.y);
            _controls.sizeDelta = new Vector2(StageWidth * scale, _controls.sizeDelta.y);

            // The edges' smoothing is a pixel wide, and how many units a pixel is just changed.
            _card.SetVerticesDirty();
            _wires.SetVerticesDirty();
            _parts.SetVerticesDirty();
            _timing.SetVerticesDirty();
        }

        // -----------------------------------------------------------------
        // Small helpers
        // -----------------------------------------------------------------

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
        private static TextMeshProUGUI StageLabel(RectTransform parent, string text, Vector2 at, UiType type, Color colour,
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
        private static void TopLeft(RectTransform rect, Vector2 at, Vector2 size)
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

        private static void SetText(TextMeshProUGUI label, string text)
        {
            if (!ReferenceEquals(label.text, text))
                label.text = text;
        }

        private static void Show(Component part, bool shown) => UiTheme.SetShown(part, shown);
    }
}
