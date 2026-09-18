using System.Collections.Generic;
using BitSorter.LogicCore;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

namespace BitSorter.View
{
    /// <summary>
    /// Free play's setup and readout, docked down the right edge: how many sources, what each emits,
    /// how many sinks, what they caught, and how fast the clock runs.
    /// </summary>
    /// <remarks>
    /// Docked rather than full-screen, and that is the whole shape of it. The setup used to take the
    /// screen, so editing a stream meant covering the board it feeds -- the player changed a bit,
    /// pressed Escape to see what happened, and pressed something to get back. Beside the board, the
    /// change and its effect are on screen together.
    ///
    /// It is not a <see cref="UiModal"/> and it reads no keys. A panel that closed on Escape was a
    /// panel racing the level list for the same press, and this one has nothing to close: it is
    /// present exactly while free play is loaded, and collapses to a tab when the player wants the
    /// width back.
    ///
    /// Owns the <see cref="SandboxConfig"/> as well as drawing it, the way
    /// <see cref="LevelSelectPanel"/> both draws the level list and switches level. There is no
    /// separate controller because there is no second caller.
    ///
    /// Every edit rebuilds the level, because changing a source changes the graph, and hands it to
    /// <see cref="LevelSession.Reconfigure"/>, which swaps the definition under the board the player
    /// has built. A wire into a fixture that has just been counted away stays in the blueprint, wired
    /// to nothing, and comes back if the fixture does.
    ///
    /// Entering free play still goes through <see cref="LevelSession.Adopt"/>, which is a real level
    /// switch and is what makes <see cref="ProgressTracker"/> save the level being left and restore
    /// the sandbox board.
    ///
    /// Bits are toggled rather than typed. A text field would need focus handling, and this game
    /// binds Space, Enter, R and Q/E, all of which a focused field would swallow.
    /// </remarks>
    public sealed class SandboxPanel : MonoBehaviour
    {
        /// <summary>One sink's row of caught bits, redrawn as they land.</summary>
        private sealed class CaughtRow
        {
            public string SinkId;
            public TextMeshProUGUI[] Cells;
            public TextMeshProUGUI Extra;

            /// <summary>What the row was last drawn from, so a frame that changed nothing draws nothing.</summary>
            public string Drawn;
        }

        [SerializeField] private LevelSession _session;
        [SerializeField] private ProgressTracker _progress;
        [SerializeField] private SimulationRunner _runner;

        [Tooltip("Canvas the panel is built under. Found by type when left empty.")]
        [SerializeField] private Canvas _canvas;

        private readonly List<GameObject> _body = new List<GameObject>();
        private readonly List<CaughtRow> _caught = new List<CaughtRow>();
        private readonly Vector3[] _corners = new Vector3[4];

        private SandboxConfig _config;
        private RectTransform _root;
        private RectTransform _bodyRoot;
        private RectTransform _tab;
        private bool _expanded = true;
        private int _speed = SimulationRunner.DefaultSpeed;

        // -----------------------------------------------------------------
        // Layout
        // -----------------------------------------------------------------

        private const float Pad = 12f;
        private const float Inner = UiTheme.SetupWidth - Pad * 2f;

        /// <summary>The name column, wide enough for a sink's id beside its bits.</summary>
        private const float NameWidth = 60f;

        /// <summary>One vector column. Eight of them fill the panel beside the name.</summary>
        private const float CellPitch = (Inner - NameWidth) / SandboxConfig.MaxVectors;

        private const float CellWidth = CellPitch - 2f;
        private const float RowHeight = 24f;
        private const float StepHeight = 28f;

        private void Awake()
        {
            if (_session == null) _session = FindFirstObjectByType<LevelSession>();
            if (_progress == null) _progress = FindFirstObjectByType<ProgressTracker>();
            if (_runner == null) _runner = FindFirstObjectByType<SimulationRunner>();
            if (_canvas == null) _canvas = FindFirstObjectByType<Canvas>();
        }

        private void Start()
        {
            if (_canvas == null || _session == null)
                return;

            Build();
        }

        private void Update()
        {
            if (_root == null)
                return;

            // Part of the HUD now, so it steps aside for a full-screen panel exactly as the parts
            // list and the banner do.
            bool hud = UiModal.HudVisible;

            UiTheme.SetShown(_root, IsOpen && _expanded && hud);
            UiTheme.SetShown(_tab, IsOpen && !_expanded && hud);

            if (!IsOpen || !_expanded || !hud)
                return;

            RefreshCaught();
        }

        /// <summary>Whether free play is the level currently loaded.</summary>
        public bool IsOpen => _session != null && _session.LevelName == SandboxLevel.Key;

        /// <summary>
        /// The panel's left edge in screen pixels, or zero when it is taking no width.
        /// </summary>
        /// <remarks>
        /// Read by <see cref="CameraFit"/>, which frames the board clear of it. The canvas is a
        /// screen-space overlay, so world corners are screen pixels.
        ///
        /// Measured whether or not it is showing, but only while it is expanded: stepping aside for a
        /// full-screen panel must not re-frame the board behind it, whereas collapsing the panel is
        /// the player asking for that width back.
        /// </remarks>
        public float ScreenLeftEdge
        {
            get
            {
                if (_root == null || !IsOpen || !_expanded)
                    return 0f;

                _root.GetWorldCorners(_corners);
                return _corners[0].x;
            }
        }

        // -----------------------------------------------------------------
        // Entry
        // -----------------------------------------------------------------

        /// <summary>
        /// Switches to free play, restoring the setup last used, and shows the panel.
        /// </summary>
        public void Open()
        {
            if (_session == null)
                return;

            if (_config == null)
                _config = Stored() ?? SandboxLevel.Default(Extents());

            if (!IsOpen)
                Adopt();

            _expanded = true;

            ApplySpeed();
            Rebuild();
        }

        /// <remarks>
        /// A board saved before fixtures had fixed slots is moved onto them here, before free play is
        /// built, because the restore that follows reads the same stored board. The store hands out
        /// the board it holds, so the move is staged simply by making it, and written with the next
        /// save of this board.
        /// </remarks>
        private SandboxConfig Stored()
        {
            SavedBoard board = _progress != null && _progress.Store != null
                ? _progress.Store.BoardFor(SandboxLevel.Key)
                : null;

            SandboxLevel.MigrateLegacyBoard(board, Extents());

            return board?.sandbox;
        }

        private Vector2Int Extents() =>
            _runner != null ? _runner.HalfExtents : new Vector2Int(4, 2);

        private void Adopt()
        {
            // Staged before the level is swapped, so the save that swapping triggers writes the
            // setup along with the board. The other order cost two whole-file writes per click.
            Stage();

            _session.Adopt(SandboxLevel.Build(_config, Extents()), SandboxLevel.Key);
        }

        /// <summary>
        /// Records the setup in memory, for the next save of this board to write.
        /// </summary>
        /// <remarks>
        /// Deliberately not a write. Entering free play raises LevelUnloading, which makes
        /// <see cref="ProgressTracker"/> save the sandbox board, and
        /// <see cref="ProgressStore.SaveBoard"/> carries a staged setup forward into it -- so one
        /// click produces one write instead of two. CLAUDE.md rejects a file write per click for
        /// boards, and a setup is no different.
        ///
        /// Nothing is lost by never writing directly. Leaving the sandbox and quitting both save the
        /// open board, and a setup that survives neither was never worth a file write: it is the
        /// opening default, which is rebuilt identically next time.
        ///
        /// Reads the stored board back rather than making a fresh one, because a fresh one has no
        /// placements and would stage away the circuit it belongs to.
        /// </remarks>
        private void Stage()
        {
            if (_progress == null || _progress.Store == null)
                return;

            SavedBoard board = _progress.Store.BoardFor(SandboxLevel.Key) ?? new SavedBoard();
            board.sandbox = _config.Clone();

            _progress.Store.StageBoard(SandboxLevel.Key, board);
        }

        // -----------------------------------------------------------------
        // Building
        // -----------------------------------------------------------------

        private void Build()
        {
            Image panel = UiTheme.Panel_("Sandbox setup", _canvas.transform, UiTheme.Panel);
            _root = panel.GetComponent<RectTransform>();

            // Stretched down the right edge between the rows it must not cover: the bits-lost meter
            // and help badge above, the refusal toast below.
            _root.anchorMin = new Vector2(1f, 0f);
            _root.anchorMax = new Vector2(1f, 1f);
            _root.pivot = new Vector2(1f, 1f);
            _root.offsetMin = new Vector2(-(UiTheme.Margin + UiTheme.SetupWidth), UiTheme.SetupBottom);
            _root.offsetMax = new Vector2(-UiTheme.Margin, -UiTheme.SetupTop);

            TextMeshProUGUI title = UiTheme.Label(
                "title", _root, 14f, UiTheme.TextDim, TextAlignmentOptions.Left);
            UiTheme.Anchor(title.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(Pad, -10f), new Vector2(160f, 20f));
            title.text = "SETUP";

            Button collapse = UiTheme.Button_("collapse", _root, "»", out TextMeshProUGUI _);
            UiTheme.Anchor(collapse.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-Pad, -8f), new Vector2(26f, 24f));
            collapse.onClick.AddListener(() => { Expand(false); Defocus(); });

            _bodyRoot = UiTheme.Rect("body", _root);
            UiTheme.Anchor(_bodyRoot, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(Pad, -36f), new Vector2(Inner, 10f));

            BuildTab();

            _root.gameObject.SetActive(false);
            _tab.gameObject.SetActive(false);
        }

        /// <summary>
        /// The collapsed state: a tab where the panel was.
        /// </summary>
        /// <remarks>
        /// Collapsing has to leave something behind. A panel that vanished would leave the player
        /// with a board, no setup, and nothing on screen saying free play has one.
        /// </remarks>
        private void BuildTab()
        {
            Button tab = UiTheme.Button_("Setup tab", _canvas.transform, "« SETUP", out TextMeshProUGUI label);
            _tab = tab.GetComponent<RectTransform>();

            UiTheme.Anchor(_tab, new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-UiTheme.Margin, -UiTheme.SetupTop), new Vector2(96f, 28f));

            label.fontSize = 13f;
            tab.onClick.AddListener(() => { Expand(true); Defocus(); });
        }

        private void Expand(bool expanded)
        {
            _expanded = expanded;

            if (expanded)
                Rebuild();
        }

        /// <summary>
        /// Rebuilds every row. The counts decide the layout, so there is nothing stable to refresh --
        /// and this runs on a click, never per frame.
        /// </summary>
        private void Rebuild()
        {
            for (int i = 0; i < _body.Count; i++)
            {
                if (_body[i] != null)
                    Destroy(_body[i]);
            }

            _body.Clear();
            _caught.Clear();

            if (_config == null)
                return;

            int capacity = SandboxLevel.Capacity(Extents());
            float y = 0f;

            // The same sentence the status banner carries. The panel no longer covers the banner, but
            // the moment a count reaches zero is the moment it needs saying next to the count.
            string warning = SandboxLevel.Warning(_config);

            if (warning != null)
                y = Wrapped(y, warning, UiTheme.Bad);

            y = Heading(y, "INPUTS");
            y = Stepper(y, "Sources", _config.sources.Length, SandboxRules.Sources(capacity), SetSources);

            if (_config.sources.Length > 0)
                y = Columns(y);

            for (int i = 0; i < _config.sources.Length; i++)
                y = StreamRow(y, i);

            y -= 6f;
            y = Stepper(y, "Vectors", _config.vectors, SandboxRules.Vectors(), SetVectors);
            y = TableRow(y);

            y = Heading(y - 8f, "OUTPUTS");
            y = Stepper(y, "Sinks", _config.sinks, SandboxRules.Sinks(capacity), SetSinks);

            if (_config.sinks > 0)
                y = Columns(y);

            for (int i = 0; i < _config.sinks; i++)
                y = CaughtRowFor(y, i);

            y = Heading(y - 8f, "SPEED");
            SpeedRow(y);
        }

        private float Heading(float y, string caption)
        {
            TextMeshProUGUI label = UiTheme.Label(
                caption, _bodyRoot, 12f, UiTheme.Accent * 0.85f, TextAlignmentOptions.Left);

            UiTheme.Anchor(label.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(0f, y), new Vector2(Inner, 18f));

            label.text = caption;
            _body.Add(label.gameObject);

            return y - 22f;
        }

        /// <summary>A line of text that may be longer than the panel is wide.</summary>
        private float Wrapped(float y, string text, Color colour)
        {
            TextMeshProUGUI label = UiTheme.Label(
                "note", _bodyRoot, 12f, colour, TextAlignmentOptions.TopLeft);

            UiTheme.Anchor(label.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(0f, y), new Vector2(Inner, 34f));

            label.text = text;
            _body.Add(label.gameObject);

            return y - 40f;
        }

        /// <summary>The vector numbers, over the columns the bits below them sit in.</summary>
        private float Columns(float y)
        {
            for (int v = 0; v < _config.vectors; v++)
            {
                TextMeshProUGUI label = UiTheme.Label(
                    $"column {v}", _bodyRoot, 10f, UiTheme.TextDim, TextAlignmentOptions.Center);

                UiTheme.Anchor(label.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                    new Vector2(NameWidth + v * CellPitch, y), new Vector2(CellWidth, 14f));

                label.text = (v + 1).ToString();
                _body.Add(label.gameObject);
            }

            return y - 16f;
        }

        private float Stepper(
            float y, string caption, int value, StepRange range, System.Action<int> set)
        {
            TextMeshProUGUI label = UiTheme.Label(
                caption, _bodyRoot, 14f, UiTheme.Text, TextAlignmentOptions.Left);
            UiTheme.Anchor(label.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(0f, y), new Vector2(120f, StepHeight));
            label.text = caption;
            _body.Add(label.gameObject);

            Step(y, Inner - 88f, "-", range.CanDecrease(value), () => set(value - 1));

            TextMeshProUGUI count = UiTheme.Label(
                $"{caption} count", _bodyRoot, 15f, UiTheme.Accent, TextAlignmentOptions.Center);
            UiTheme.Anchor(count.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(Inner - 58f, y), new Vector2(32f, StepHeight));
            count.text = value.ToString();
            _body.Add(count.gameObject);

            Step(y, Inner - 24f, "+", range.CanIncrease(value), () => set(value + 1));

            return y - (StepHeight + 4f);
        }

        private void Step(float y, float x, string glyph, bool enabled, UnityEngine.Events.UnityAction go)
        {
            Button button = UiTheme.Button_($"step {glyph}", _bodyRoot, glyph, out TextMeshProUGUI label);

            UiTheme.Anchor(button.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(x, y), new Vector2(24f, 24f));

            button.interactable = enabled;
            label.color = enabled ? UiTheme.Text : UiTheme.TextDim;
            button.onClick.AddListener(() => { go(); Defocus(); });

            _body.Add(button.gameObject);
        }

        private float StreamRow(float y, int index)
        {
            TextMeshProUGUI id = UiTheme.Label(
                $"source {index}", _bodyRoot, 14f, UiTheme.TextDim, TextAlignmentOptions.Left);
            UiTheme.Anchor(id.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(0f, y), new Vector2(NameWidth, RowHeight));
            id.text = SandboxLevel.SourceId(index);
            _body.Add(id.gameObject);

            string stream = _config.sources[index];

            // Only the vectors in use. The rest of the stream is still there, waiting for the count
            // to come back up.
            for (int v = 0; v < _config.vectors && v < stream.Length; v++)
            {
                int vector = v;
                bool one = stream[v] == '1';

                Button bit = UiTheme.Button_(
                    $"bit {index} {v}", _bodyRoot, one ? "1" : "0", out TextMeshProUGUI label);

                UiTheme.Anchor(bit.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(0f, 1f),
                    new Vector2(NameWidth + v * CellPitch, y), new Vector2(CellWidth, RowHeight));

                // A one reads as lit and a zero as unlit, the same way BitVisuals colours a bit on
                // the board, so the stream looks like what it will emit.
                label.color = one ? UiTheme.Accent : UiTheme.TextDim;

                bit.onClick.AddListener(() => { Flip(index, vector); Defocus(); });
                _body.Add(bit.gameObject);
            }

            return y - (RowHeight + 3f);
        }

        /// <summary>The two fill buttons, and why the table one is dead when it is.</summary>
        private float TableRow(float y)
        {
            bool canFill = SandboxRules.CanFillTable(_config.sources.Length);

            Button table = UiTheme.Button_("truth table", _bodyRoot, "Truth table", out TextMeshProUGUI caption);
            UiTheme.Anchor(table.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(0f, y), new Vector2(Inner * 0.58f, 26f));

            caption.fontSize = 13f;
            caption.color = canFill ? UiTheme.Text : UiTheme.TextDim;
            table.interactable = canFill;
            table.onClick.AddListener(() => { FillTable(); Defocus(); });
            _body.Add(table.gameObject);

            Button zeros = UiTheme.Button_("all zero", _bodyRoot, "All 0", out TextMeshProUGUI zeroCaption);
            UiTheme.Anchor(zeros.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(Inner, y), new Vector2(Inner * 0.38f, 26f));

            zeroCaption.fontSize = 13f;
            zeros.interactable = _config.sources.Length > 0;
            zeros.onClick.AddListener(() => { FillZeros(); Defocus(); });
            _body.Add(zeros.gameObject);

            y -= 30f;

            // Said rather than left to be guessed: a dead button with no reason beside it reads as a
            // broken one.
            if (!canFill)
            {
                y = Wrapped(y,
                    _config.sources.Length == 0
                        ? "A table needs a source."
                        : $"A table of every combination needs {SandboxRules.MaxTableSources} sources or fewer.",
                    UiTheme.TextDim);
            }

            return y;
        }

        private float CaughtRowFor(float y, int index)
        {
            string id = SandboxLevel.SinkId(index);

            TextMeshProUGUI name = UiTheme.Label(
                $"sink {index}", _bodyRoot, 13f, UiTheme.TextDim, TextAlignmentOptions.Left);
            UiTheme.Anchor(name.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(0f, y), new Vector2(NameWidth, RowHeight));
            name.text = id;
            _body.Add(name.gameObject);

            var row = new CaughtRow { SinkId = id, Cells = new TextMeshProUGUI[_config.vectors] };

            for (int v = 0; v < _config.vectors; v++)
            {
                TextMeshProUGUI cell = UiTheme.Label(
                    $"caught {index} {v}", _bodyRoot, 14f, UiTheme.Text, TextAlignmentOptions.Center);

                UiTheme.Anchor(cell.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                    new Vector2(NameWidth + v * CellPitch, y), new Vector2(CellWidth, RowHeight));

                row.Cells[v] = cell;
                _body.Add(cell.gameObject);
            }

            // Sits past the last column, where it is only ever drawn over empty panel.
            row.Extra = UiTheme.Label(
                $"extra {index}", _bodyRoot, 11f, UiTheme.Bad, TextAlignmentOptions.Left);
            UiTheme.Anchor(row.Extra.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(NameWidth + _config.vectors * CellPitch + 2f, y), new Vector2(34f, RowHeight));
            _body.Add(row.Extra.gameObject);

            _caught.Add(row);

            return y - (RowHeight + 3f);
        }

        private void SpeedRow(float y)
        {
            for (int i = 0; i < SandboxRules.Speeds.Length; i++)
            {
                int speed = SandboxRules.Speeds[i];

                Button button = UiTheme.Button_(
                    $"speed {speed}", _bodyRoot, $"{speed}x", out TextMeshProUGUI label);

                UiTheme.Anchor(button.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(0f, 1f),
                    new Vector2(i * 52f, y), new Vector2(46f, 26f));

                bool chosen = speed == _speed;

                label.fontSize = 13f;
                label.color = chosen ? UiTheme.Text : UiTheme.TextDim;
                button.GetComponent<Image>().color = chosen ? UiTheme.Accent * 0.55f : UiTheme.PanelEdge;

                button.onClick.AddListener(() => { SetSpeed(speed); Defocus(); });
                _body.Add(button.gameObject);
            }
        }

        // -----------------------------------------------------------------
        // Reading what came out
        // -----------------------------------------------------------------

        /// <summary>
        /// Redraws each sink's row from what the simulation recorded.
        /// </summary>
        /// <remarks>
        /// Derived every frame from <see cref="SinkNode.Received"/> rather than accumulated here. The
        /// simulation already records each bit as it lands, and a second copy kept in the interface
        /// would be a second thing to drift -- and would have to be told about resets.
        ///
        /// Updated while running, not only at the end, because watching bits arrive one tick at a
        /// time is how the delay lesson reads.
        /// </remarks>
        private void RefreshCaught()
        {
            if (_runner == null || !_runner.IsReady)
                return;

            for (int i = 0; i < _caught.Count; i++)
            {
                CaughtRow row = _caught[i];
                IReadOnlyList<SinkNode.Reception> caught =
                    TryFindSink(row.SinkId, out SinkNode sink) ? sink.Received : null;

                int count = caught != null ? caught.Count : 0;
                string signature = $"{count}:{(count > 0 ? (int)caught[count - 1].Value : -1)}";

                // Formatting every cell every frame hands TextMeshPro text equal to what it already
                // has and leaves the garbage behind anyway.
                if (signature == row.Drawn)
                    continue;

                row.Drawn = signature;

                for (int v = 0; v < row.Cells.Length; v++)
                {
                    string text = SandboxRules.Cell(caught, v);
                    bool landed = v < count;

                    row.Cells[v].text = text;
                    row.Cells[v].color = landed ? UiTheme.Text : UiTheme.TextDim;
                }

                int extra = SandboxRules.Extra(caught, row.Cells.Length);
                row.Extra.text = extra > 0 ? $"+{extra}" : string.Empty;
            }
        }

        private bool TryFindSink(string sinkId, out SinkNode sink)
        {
            sink = null;

            if (!_runner.FixtureNodeIds.TryGetValue(sinkId, out int nodeId))
                return false;

            SimulationView view = _runner.View;

            if (nodeId < 0 || nodeId >= view.NodeCount)
                return false;

            sink = view.GetNode(nodeId) as SinkNode;
            return sink != null;
        }

        // -----------------------------------------------------------------
        // Edits
        // -----------------------------------------------------------------

        private void SetSources(int count)
        {
            count = SandboxRules.Sources(SandboxLevel.Capacity(Extents())).Clamp(count);

            var next = new string[count];

            for (int i = 0; i < count; i++)
            {
                next[i] = i < _config.sources.Length
                    ? _config.sources[i]
                    : SandboxConfig.NormaliseStream(string.Empty, SandboxConfig.MaxVectors);
            }

            _config.sources = next;
            Changed();
        }

        private void SetSinks(int count)
        {
            _config.sinks = SandboxRules.Sinks(SandboxLevel.Capacity(Extents())).Clamp(count);
            Changed();
        }

        private void SetVectors(int count)
        {
            _config.vectors = SandboxRules.Vectors().Clamp(count);
            Changed();
        }

        private void Flip(int index, int vector)
        {
            if (index < 0 || index >= _config.sources.Length)
                return;

            _config.sources[index] = SandboxRules.Flip(_config.sources[index], vector);
            Changed();
        }

        /// <summary>
        /// Fills the sources with every combination of their values, and sets the vector count to
        /// match.
        /// </summary>
        private void FillTable()
        {
            int sources = _config.sources.Length;

            if (!SandboxRules.CanFillTable(sources))
                return;

            string[] table = SandboxRules.Table(sources);

            for (int i = 0; i < sources; i++)
                _config.sources[i] = table[i];

            _config.vectors = SandboxRules.VectorsForTable(sources);
            Changed();
        }

        private void FillZeros()
        {
            for (int i = 0; i < _config.sources.Length; i++)
                _config.sources[i] = SandboxConfig.NormaliseStream(string.Empty, SandboxConfig.MaxVectors);

            Changed();
        }

        private void SetSpeed(int speed)
        {
            _speed = SandboxRules.ClampSpeed(speed);

            ApplySpeed();
            Rebuild();
        }

        /// <remarks>
        /// Reapplied on the way in because installing a level puts the clock back to the authored
        /// rate, which is what keeps a fast sandbox from following the player into a taught level.
        /// </remarks>
        private void ApplySpeed()
        {
            if (_runner != null)
                _runner.Speed = _speed;
        }

        /// <remarks>
        /// Reconfigures rather than adopts. Adopting is how free play is *entered*, and it is a level
        /// switch: it emptied the undo history, reset the part in hand, cancelled any run and wrote
        /// the save file, once per click on one bit. The setup is still staged, so the next ordinary
        /// save of this board carries it.
        /// </remarks>
        private void Changed()
        {
            _config.Normalise(SandboxLevel.Capacity(Extents()), SandboxLevel.Capacity(Extents()));

            Stage();
            _session.Reconfigure(SandboxLevel.Build(_config, Extents()));

            Rebuild();
        }

        private static void Defocus()
        {
            // Or the clicked button keeps focus and swallows Space and Enter, which run the board.
            if (EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(null);
        }
    }
}
