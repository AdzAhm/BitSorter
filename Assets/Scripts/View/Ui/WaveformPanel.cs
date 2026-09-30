using System.Collections.Generic;
using BitSorter.LogicCore;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace BitSorter.View
{
    /// <summary>
    /// The timing diagram: a strip along the bottom of the screen showing, tick by tick, what every
    /// source sent and every bin received. Behind F2, and the badge beside the help badge.
    /// </summary>
    /// <remarks>
    /// The course's own notation for what the board already teaches: a bit that arrives a tick late
    /// is a step in the wrong place, and a collision is a cross where a bit arrived. On a clocked
    /// level the clock is its first row. It replaced the small clock diagram that sat in the bottom
    /// left corner, which showed the clock and nothing else.
    ///
    /// **Along the bottom, and the board moves up and shrinks to make room** -- Ahmad's choice, over
    /// a column on the right (2026-09-30). The bottom of the screen is the run buttons, the key lines
    /// and the toast, so the strip starts above them, on <see cref="UiRows.PanelFloor"/>, and runs
    /// from the parts list to the right margin or to the edge of an open right-hand panel. Its height
    /// is fixed per level -- four wire rows are reserved whether or not they are used -- so the board
    /// is framed once when it opens and never again while it is open. <see cref="CameraFit"/> reads
    /// <see cref="ScreenTopEdge"/>, by intent: the flag, not whether the strip is drawn, so a
    /// full-screen panel over the board does not reframe it behind itself.
    ///
    /// **Recorded from tick 0 whether it is open or not**, by <see cref="WaveformRecorder"/>, from
    /// the runner's own tick and rebuild events. Opening it mid-run shows the run so far.
    ///
    /// **One F2 flag, and it is this one.** The developer numbers in the bottom-right corner come up
    /// with it -- <see cref="DiagnosticsPanel"/> follows <see cref="IsOpen"/> rather than reading F2
    /// itself -- so the key and the badge can never leave the two out of step.
    /// </remarks>
    public sealed class WaveformPanel : MonoBehaviour
    {
        [SerializeField] private SimulationRunner _runner;
        [SerializeField] private LevelSession _session;
        [SerializeField] private GatePaletteView _palette;
        [SerializeField] private SandboxPanel _sandbox;
        [SerializeField] private HelpPanel _help;

        [Tooltip("Canvas the strip is built under. Found by type when left empty.")]
        [SerializeField] private Canvas _canvas;

        /// <summary>A row's height, in canvas units.</summary>
        public const float RowHeight = 20f;

        /// <summary>The line of tick numbers above the rows.</summary>
        public const float HeaderHeight = 16f;

        /// <summary>Space inside the strip's edges.</summary>
        public const float Padding = 8f;

        /// <summary>The column the rows' names sit in, left of the ticks.</summary>
        public const float LabelWidth = 64f;

        /// <summary>How wide one tick is drawn.</summary>
        /// <remarks>
        /// Wide enough for a step and a cross to read -- at 14 a whole half adder's run was a sliver
        /// at the left of the strip -- and narrow enough that most runs still fit without scrolling.
        /// </remarks>
        public const float TickWidth = 24f;

        /// <summary>A tick number every this many ticks.</summary>
        public const int NumberEvery = 4;

        /// <summary>Rows kept for wires the player picks, whether or not any is picked.</summary>
        public const int WireRows = 4;

        /// <summary>Space between the badge and the help badge beside it.</summary>
        private const float BadgeGap = 24f;

        private readonly WaveformRecorder _recorder = new WaveformRecorder();

        private bool _shown;
        private int _recordedRevision = -1;

        private RectTransform _root;
        private WaveformGraphic _graphic;
        private RectTransform _labels;
        private Image _badge;

        private readonly List<TextMeshProUGUI> _rowLabels = new List<TextMeshProUGUI>();
        private readonly List<TextMeshProUGUI> _tickLabels = new List<TextMeshProUGUI>();
        private readonly List<int> _tickLabelShows = new List<int>();
        private readonly char[] _digits = new char[10];

        private LevelDefinition _rowsFor;
        private int _rowsSources = -1;
        private int _rowsSinks = -1;

        private float _leftInset = -1f;
        private float _rightInset = -1f;
        private bool _dirty = true;

        /// <summary>What the strip draws from.</summary>
        public WaveformRecorder Recorder => _recorder;

        /// <summary>Whether F2 has the strip up -- the intent, drawn or not.</summary>
        public bool IsOpen => _shown;

        /// <summary>Whether the strip is on screen now.</summary>
        public bool IsShowing => _root != null && _root.gameObject.activeSelf;

        /// <summary>How tall the strip is on this level, in canvas units.</summary>
        public float Height { get; private set; }

        /// <summary>How many rows there are on this level: the clock, sources, bins and wires.</summary>
        public int RowCount { get; private set; }

        /// <summary>How many ticks fit across the strip.</summary>
        public int VisibleTicks { get; private set; }

        /// <summary>The first tick drawn: 0 until a run outgrows the strip, then it scrolls.</summary>
        public int WindowStart { get; private set; }

        /// <summary>The latest tick recorded, or -1 before a run.</summary>
        public int LastTick => _recorder.LastTick;

        /// <summary>The strip, for the tests.</summary>
        public RectTransform Root => _root;

        /// <summary>The badge, for the tests.</summary>
        public Button Badge => _badge != null ? _badge.GetComponent<Button>() : null;

        /// <summary>
        /// The strip's top edge in screen pixels up from the bottom while F2 has it open, or zero.
        /// </summary>
        /// <remarks>
        /// What <see cref="CameraFit"/> frames the board above. Worked out from the flag and the
        /// level's rows rather than read off the drawn strip, which is hidden behind a full-screen
        /// panel -- and a board reframed behind the level list would move under the player's eyes as
        /// the list closed.
        /// </remarks>
        public float ScreenTopEdge =>
            _shown && _canvas != null && Height > 0f ? (UiRows.PanelFloor + Height) * _canvas.scaleFactor : 0f;

        private int ClockPeriod => _recordedLevel != null && _recordedLevel.HasClock ? _recordedLevel.ClockPeriod : 0;

        private LevelDefinition _recordedLevel;

        private void Awake()
        {
            if (_runner == null) _runner = FindFirstObjectByType<SimulationRunner>();
            if (_session == null) _session = FindFirstObjectByType<LevelSession>();
            if (_palette == null) _palette = FindFirstObjectByType<GatePaletteView>();
            if (_sandbox == null) _sandbox = FindFirstObjectByType<SandboxPanel>();
            if (_help == null) _help = FindFirstObjectByType<HelpPanel>();
            if (_canvas == null) _canvas = FindFirstObjectByType<Canvas>();
        }

        private void OnEnable()
        {
            if (_runner == null)
                return;

            _runner.Ticked += OnTicked;
            _runner.Rebuilt += OnRebuilt;

            if (_runner.IsReady)
                OnRebuilt();
        }

        private void OnDisable()
        {
            if (_runner == null)
                return;

            _runner.Ticked -= OnTicked;
            _runner.Rebuilt -= OnRebuilt;
        }

        private void Start()
        {
            if (_canvas == null)
                return;

            BuildStrip();
            BuildBadge();
            _root.gameObject.SetActive(false);
        }

        // -----------------------------------------------------------------
        // Recording
        // -----------------------------------------------------------------

        private void OnRebuilt()
        {
            _recordedLevel = _runner.BuiltLevel;
            _recorder.Reset(_runner.View, _recordedLevel, _runner.FixtureNodeIds);
            _recordedRevision = _runner.GraphRevision;
            _dirty = true;
        }

        private void OnTicked(int tick)
        {
            // A rebuild that slipped past unheard -- one made before this listened -- is caught here,
            // before the new graph's first tick is recorded against the old one's rows.
            if (_recordedRevision != _runner.GraphRevision)
                OnRebuilt();

            _recorder.AfterTick(_runner.View, tick);
            _dirty = true;
        }

        // -----------------------------------------------------------------
        // What each row holds
        // -----------------------------------------------------------------

        private bool HasClockRow => ClockPeriod > 1;

        /// <summary>The cell a row holds on a tick; false for a tick outside what is recorded.</summary>
        public bool TryCell(int row, int tick, out WaveCell cell)
        {
            cell = default;

            if (tick < _recorder.FirstTick || tick > _recorder.LastTick)
                return false;

            if (HasClockRow)
            {
                if (row == 0)
                {
                    cell = new WaveCell(WaveformRecorder.ClockOn(tick, ClockPeriod) ? WaveCell.OneBit : WaveCell.ZeroBit);
                    return true;
                }

                row--;
            }

            if (row < _recorder.SourceCount)
            {
                cell = _recorder.SourceCell(row, tick);
                return true;
            }

            row -= _recorder.SourceCount;

            if (row < _recorder.SinkCount)
            {
                cell = _recorder.SinkCell(row, tick);
                return true;
            }

            // The wire rows, which nothing fills yet.
            return false;
        }

        /// <summary>The colour a row's line is drawn in: the clock quieter than the signals.</summary>
        public Color RowColour(int row, Palette palette) => HasClockRow && row == 0 ? palette.TextDim : palette.Text;

        // -----------------------------------------------------------------
        // Each frame
        // -----------------------------------------------------------------

        private void Update()
        {
            Keyboard keyboard = UiText.Keyboard;

            // Not behind a full-screen panel, where every board key stands aside: pressed on the main
            // menu it would switch the strip on unseen, to appear once the menu closed.
            if (keyboard != null && keyboard.f2Key.wasPressedThisFrame && !UiModal.OpenOrJustClosed)
                Toggle();

            if (_root == null)
                return;

            // Kept current open or not: the height is what the board is framed above, and a level
            // changed behind a menu must not leave the frame for the last level's rows.
            if (_recordedLevel != null && RowsChanged())
                BuildRows();

            bool hud = UiModal.HudVisible;
            UiTheme.SetShown(_badge, hud);

            bool wanted = _shown && hud && _recordedLevel != null;

            if (_root.gameObject.activeSelf != wanted)
                _root.gameObject.SetActive(wanted);

            if (!wanted)
                return;

            if (Fit())
                _dirty = true;

            int start = WindowStartFor(_recorder.LastTick, VisibleTicks, _recorder.FirstTick);

            if (start != WindowStart)
            {
                WindowStart = start;
                _dirty = true;
            }

            if (!_dirty)
                return;

            _dirty = false;
            NumberTheTicks();
            _graphic.SetVerticesDirty();
        }

        /// <summary>
        /// The first tick drawn: 0 while the run fits, and once it does not, the latest ticks with the
        /// newest at the right -- never earlier than the oldest the recorder still holds.
        /// </summary>
        public static int WindowStartFor(int lastTick, int visible, int firstHeld)
        {
            if (visible <= 0 || lastTick < visible)
                return 0;

            int start = lastTick - visible + 1;
            return start < firstHeld ? firstHeld : start;
        }

        private void Toggle()
        {
            _shown = !_shown;
            _dirty = true;
        }

        private bool RowsChanged() =>
            _rowsFor != _recordedLevel || _rowsSources != _recorder.SourceCount || _rowsSinks != _recorder.SinkCount;

        /// <summary>
        /// Lays the strip out across the room between the parts list and whatever is on the right,
        /// and works out how many ticks fit. True when anything moved.
        /// </summary>
        private bool Fit()
        {
            float scale = _canvas.scaleFactor > 0f ? _canvas.scaleFactor : 1f;

            float paletteEdge = _palette != null ? _palette.ScreenRightEdge : 0f;
            float left = paletteEdge > 0f ? paletteEdge / scale + UiTheme.Gap : UiTheme.Margin;

            float setup = _sandbox != null ? _sandbox.ScreenLeftEdge : 0f;
            float help = _help != null ? _help.ScreenLeftEdge : 0f;
            float edge = setup <= 0f ? help : help <= 0f ? setup : Mathf.Min(setup, help);
            float right = edge > 0f ? (Screen.width - edge) / scale + UiTheme.Gap : UiTheme.Margin;

            if (Mathf.Approximately(left, _leftInset) && Mathf.Approximately(right, _rightInset))
                return false;

            _leftInset = left;
            _rightInset = right;

            _root.offsetMin = new Vector2(left, UiRows.PanelFloor);
            _root.offsetMax = new Vector2(-right, UiRows.PanelFloor + Height);

            float width = _root.rect.width - 2f * Padding - LabelWidth;
            VisibleTicks = width > 0f ? Mathf.FloorToInt(width / TickWidth) : 0;
            return true;
        }

        // -----------------------------------------------------------------
        // Building
        // -----------------------------------------------------------------

        private void BuildStrip()
        {
            Image backing = UiTheme.Panel_("Timing diagram", _canvas.transform, UiTheme.Panel);
            _root = backing.GetComponent<RectTransform>();
            _root.anchorMin = new Vector2(0f, 0f);
            _root.anchorMax = new Vector2(1f, 0f);
            _root.pivot = new Vector2(0.5f, 0f);

            // Takes clicks, so one on the strip never reaches the board it covers.
            backing.raycastTarget = true;

            var drawing = new GameObject("waves", typeof(RectTransform), typeof(CanvasRenderer));
            drawing.transform.SetParent(_root, false);
            _graphic = drawing.AddComponent<WaveformGraphic>();
            _graphic.raycastTarget = false;
            _graphic.Panel = this;
            UiTheme.Stretch(_graphic.rectTransform);

            _labels = UiTheme.Rect("labels", _root);
            UiTheme.Stretch(_labels);
        }

        /// <summary>A name for each row, down the left: CLOCK, then the fixtures, then the wires.</summary>
        /// <remarks>Only when the level changes, so the text it allocates is never per frame.</remarks>
        private void BuildRows()
        {
            _rowsFor = _recordedLevel;
            _rowsSources = _recorder.SourceCount;
            _rowsSinks = _recorder.SinkCount;

            RowCount = (HasClockRow ? 1 : 0) + _rowsSources + _rowsSinks + WireRows;
            Height = 2f * Padding + HeaderHeight + RowCount * RowHeight;

            for (int i = 0; i < _rowLabels.Count; i++)
                Destroy(_rowLabels[i].gameObject);

            _rowLabels.Clear();

            int row = 0;

            if (HasClockRow)
                RowLabel(row++, "CLOCK", UiTheme.TextDim);

            for (int i = 0; i < _rowsSources; i++)
                RowLabel(row++, NameOf(_recorder.SourceNodeId(i)), UiTheme.Text);

            for (int i = 0; i < _rowsSinks; i++)
                RowLabel(row++, NameOf(_recorder.SinkNodeId(i)), UiTheme.Text);

            for (int i = 0; i < WireRows; i++)
                RowLabel(row++, "W" + (i + 1), UiTheme.TextDim);

            _leftInset = -1f;   // the height changed, so lay the strip out again
            _dirty = true;
        }

        private string NameOf(int nodeId)
        {
            SimulationView view = _runner.View;
            Node node = nodeId >= 0 && nodeId < view.NodeCount ? view.GetNode(nodeId) : null;
            return node != null && !string.IsNullOrEmpty(node.Name) ? node.Name.ToUpperInvariant() : "?";
        }

        private void RowLabel(int row, string text, Color colour)
        {
            TextMeshProUGUI label = UiTheme.Label("row " + row, _labels, UiType.Micro, colour, TextAlignmentOptions.MidlineLeft);
            label.raycastTarget = false;
            UiTheme.Anchor(label.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(Padding, -(Padding + HeaderHeight + row * RowHeight)),
                new Vector2(LabelWidth - 4f, RowHeight));
            label.rectTransform.pivot = new Vector2(0f, 1f);
            label.text = text;
            _rowLabels.Add(label);
        }

        /// <summary>
        /// A number over every fourth tick in the window. Each label is rewritten only when the tick it
        /// shows changes, into a reused buffer, so scrolling makes no garbage in a player.
        /// </summary>
        private void NumberTheTicks()
        {
            int first = (WindowStart + NumberEvery - 1) / NumberEvery * NumberEvery;
            int needed = VisibleTicks <= 0 ? 0 : (WindowStart + VisibleTicks - 1 - first) / NumberEvery + 1;

            if (needed < 0)
                needed = 0;

            while (_tickLabels.Count < needed)
            {
                TextMeshProUGUI label = UiTheme.Label("tick", _labels, UiType.Micro, UiTheme.TextDim, TextAlignmentOptions.MidlineLeft);
                label.raycastTarget = false;
                label.rectTransform.anchorMin = new Vector2(0f, 1f);
                label.rectTransform.anchorMax = new Vector2(0f, 1f);
                label.rectTransform.pivot = new Vector2(0f, 1f);
                label.rectTransform.sizeDelta = new Vector2(TickWidth * NumberEvery, HeaderHeight);
                _tickLabels.Add(label);
                _tickLabelShows.Add(-1);
            }

            for (int i = 0; i < _tickLabels.Count; i++)
            {
                TextMeshProUGUI label = _tickLabels[i];
                bool used = i < needed;

                if (label.gameObject.activeSelf != used)
                    label.gameObject.SetActive(used);

                if (!used)
                    continue;

                int tick = first + i * NumberEvery;
                label.rectTransform.anchoredPosition =
                    new Vector2(Padding + LabelWidth + (tick - WindowStart) * TickWidth + 2f, -Padding);

                if (_tickLabelShows[i] == tick)
                    continue;

                _tickLabelShows[i] = tick;
                label.SetCharArray(_digits, 0, FrameRateCounter.Digits(tick, _digits));
            }
        }

        /// <summary>
        /// The badge beside the help badge: a square wave, with F2 under it as H is under the help's.
        /// </summary>
        /// <remarks>
        /// The strip is useful on every level and F2 alone is named nowhere a player looks -- the
        /// clock strip named it on the five levels with a clock, and nowhere else. A button beside the
        /// other button that opens something is where a player finds it.
        /// </remarks>
        private void BuildBadge()
        {
            _badge = UiTheme.Panel_("Timing badge", _canvas.transform, Palette.Current.BadgeBackdrop);
            var rect = _badge.GetComponent<RectTransform>();

            UiTheme.Anchor(rect, new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-(UiTheme.Margin + UiTheme.BadgeSize + BadgeGap), -UiRows.Badge.Offset),
                new Vector2(UiTheme.BadgeSize, UiRows.Badge.Height));

            _badge.sprite = ProceduralSprites.Circle();

            var button = _badge.gameObject.AddComponent<Button>();
            button.targetGraphic = _badge;

            var navigation = button.navigation;
            navigation.mode = Navigation.Mode.None;
            button.navigation = navigation;

            button.onClick.AddListener(() =>
            {
                Toggle();
                UiTheme.Defocus();
            });

            // A square wave: low, up, high, down, low.
            const float w = 20f, h = 10f, t = 2f;
            WaveBar(rect, -w * 0.5f, -h * 0.5f, w * 0.3f, t);
            WaveBar(rect, -w * 0.2f - t * 0.5f, 0f, t, h + t);
            WaveBar(rect, -w * 0.2f, h * 0.5f, w * 0.4f, t);
            WaveBar(rect, w * 0.2f - t * 0.5f, 0f, t, h + t);
            WaveBar(rect, w * 0.2f, -h * 0.5f, w * 0.3f, t);

            TextMeshProUGUI key = UiTheme.Label(
                "key", rect, UiTheme.KeyCaptionType, UiTheme.TextDim, TextAlignmentOptions.Center);
            key.raycastTarget = false;
            UiTheme.Anchor(key.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 1f),
                new Vector2(0f, -4f), new Vector2(60f, UiRows.BadgeKey.Height - 2f));
            key.text = ControlsReference.At(ControlSpot.TimingBadge);
        }

        private static void WaveBar(RectTransform badge, float x, float y, float width, float height)
        {
            RectTransform bar = UiTheme.Rect("wave", badge);
            bar.anchorMin = bar.anchorMax = new Vector2(0.5f, 0.5f);
            bar.pivot = new Vector2(0f, 0.5f);
            bar.anchoredPosition = new Vector2(x, y);
            bar.sizeDelta = new Vector2(width, height);

            var image = bar.gameObject.AddComponent<Image>();
            image.color = UiTheme.Text;
            image.raycastTarget = false;
        }
    }
}
