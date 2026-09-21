using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

namespace BitSorter.View
{
    /// <summary>
    /// What a solved level looks like: the name, what it cost, and somewhere to go next.
    /// </summary>
    /// <remarks>
    /// Shows what the player built rather than a score. CLAUDE.md keeps scoring under "Not yet", and
    /// this deliberately stays on the right side of that line -- gates used and ticks taken are facts
    /// about the circuit, not a rank, and they are the two figures the roadmap identifies as the only
    /// meaningful ones. Throughput is not among them, because a balanced circuit always manages one
    /// vector per tick and an unbalanced one fails outright.
    /// </remarks>
    public sealed class WinPanel : MonoBehaviour
    {
        [SerializeField] private LevelSession _session;
        [SerializeField] private SimulationRunner _runner;
        [SerializeField] private ProgressTracker _progress;

        [Tooltip("Canvas the panel is built under. Found by type when left empty.")]
        [SerializeField] private Canvas _canvas;

        /// <summary>
        /// The two buttons along the bottom, sized from the panel rather than stated.
        /// </summary>
        /// <remarks>
        /// They were a fixed 160 wide, which fits "NEXT LEVEL" and does not fit "PLAY THE FIRST
        /// LEVEL" -- the caption the panel grew when it learned to send a player on from the
        /// tutorial. The label is stretched across its button and does not wrap, so the extra
        /// characters simply printed out of the button and over the one beside it.
        ///
        /// Half the panel each, less the margins, so a caption has as much room as there is.
        /// </remarks>
        private const float PanelWidth = 500f;

        /// <inheritdoc cref="PanelWidth"/>
        private const float ButtonGap = 16f;

        /// <inheritdoc cref="PanelWidth"/>
        private const float SideMargin = 20f;

        /// <inheritdoc cref="PanelWidth"/>
        private const float ButtonWidth = (PanelWidth - SideMargin * 2f - ButtonGap) / 2f;

        /// <inheritdoc cref="PanelWidth"/>
        private const float ButtonOffset = (ButtonWidth + ButtonGap) / 2f;

        /// <inheritdoc cref="PanelWidth"/>
        private const float ButtonRow = 28f;

        private RectTransform _root;
        private Button _stay;
        private TextMeshProUGUI _title;
        private TextMeshProUGUI _detail;
        private Button _next;
        private TextMeshProUGUI _nextLabel;

        private RunState _state = RunState.Editing;
        private bool _shown;

        /// <summary>
        /// Whether the solved panel is on screen.
        /// </summary>
        /// <remarks>
        /// Exposed for the tutorial, which shows its own ending card and must wait for this one to
        /// be gone first. It cannot ask <see cref="UiModal"/>, because this panel deliberately never
        /// registers there -- it is a card in the middle of the board, not a full-screen takeover,
        /// and the board behind it stays live so the player can keep editing.
        /// </remarks>
        public bool IsShowing => _shown;

        private void Awake()
        {
            if (_session == null) _session = FindFirstObjectByType<LevelSession>();
            if (_runner == null) _runner = FindFirstObjectByType<SimulationRunner>();
            if (_progress == null) _progress = FindFirstObjectByType<ProgressTracker>();
            if (_canvas == null) _canvas = FindFirstObjectByType<Canvas>();
        }

        private void Start()
        {
            if (_canvas == null)
                return;

            Image panel = UiTheme.Panel_("Win", _canvas.transform, UiTheme.Panel);
            _root = panel.GetComponent<RectTransform>();
            // Sized for the most it ever has to say: name, gate breakdown, delay spend, a blank
            // line, the record, and a line about beating it. Six lines were overflowing a panel
            // built for three, straight over the buttons.
            UiTheme.Anchor(_root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(PanelWidth, 330f));

            _title = UiTheme.Label("title", _root, 32f, UiTheme.Good, TextAlignmentOptions.Center);
            UiTheme.Anchor(_title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -26f), new Vector2(460f, 42f));

            _detail = UiTheme.Label("detail", _root, 17f, UiTheme.Text, TextAlignmentOptions.Top);
            UiTheme.Anchor(_detail.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -80f), new Vector2(440f, 150f));
            _detail.alignment = TextAlignmentOptions.Center;
            _detail.textWrappingMode = TextWrappingModes.Normal;

            _next = UiTheme.Button_("Next", _root, "NEXT LEVEL", out _nextLabel);
            UiTheme.Anchor(_next.GetComponent<RectTransform>(), new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f), new Vector2(-ButtonOffset, ButtonRow),
                new Vector2(ButtonWidth, UiTheme.ButtonHeight));
            _next.onClick.AddListener(NextLevel);

            _stay = UiTheme.Button_("Stay", _root, "KEEP TINKERING", out TextMeshProUGUI _);
            UiTheme.Anchor(_stay.GetComponent<RectTransform>(), new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f), new Vector2(ButtonOffset, ButtonRow),
                new Vector2(ButtonWidth, UiTheme.ButtonHeight));
            _stay.onClick.AddListener(Dismiss);

            _root.gameObject.SetActive(false);
        }

        private void Update()
        {
            if (_session == null || _root == null)
                return;

            // Every frame, not only on a state change: the panel that covers this one can open and
            // close without the run changing at all.
            Draw();

            RunState now = _session.State;

            if (now != _state)
            {
                // The last level with everything else already solved belongs to EndingPanel. The
                // condition lives there so the two cannot disagree and show both or neither.
                if (now == RunState.Passed && !EndingPanel.IsTheEnd(_session, _progress))
                    Present();
                else if (_shown)
                    Show(false);   // reset or a new run takes the panel away

                _state = now;
            }
        }

        /// <summary>Fills in what the solved circuit cost and shows the panel.</summary>
        private void Present()
        {
            LevelDefinition level = _session.Level;

            _title.text = "SOLVED";

            int gates = 0;
            var parts = new List<string>();

            foreach (LevelBudgetEntry entry in level.Budget)
            {
                int placed = _session.PlacedCountOf(entry.Kind);

                if (placed <= 0)
                    continue;

                gates += placed;
                parts.Add($"{placed} {GatePalette.Label(entry.Kind)}");
            }

            string built = parts.Count > 0 ? string.Join(", ", parts.ToArray()) : "no gates at all";
            string plural = gates == 1 ? "gate" : "gates";

            var detail = new System.Text.StringBuilder();
            detail.AppendLine(level.Name);
            detail.Append($"{gates} {plural}  -  {built}");

            if (level.HasDelayBudget)
                detail.Append($"\n{_session.SpentDelay} of {level.DelayBudget} delay spent");

            AppendRecords(detail);

            _detail.text = detail.ToString();

            int index = _session.LevelIndex;
            int count = _session.AvailableLevels.Count;

            bool onward = HasSomewhereToGo(index, count);

            _next.gameObject.SetActive(onward);
            _nextLabel.text = index < 0 ? "PLAY THE FIRST LEVEL" : "NEXT LEVEL";

            // On the last level there is no pair to balance, so the one button that is left takes
            // the middle rather than sitting where its other half used to be.
            _stay.GetComponent<RectTransform>().anchoredPosition =
                new Vector2(onward ? ButtonOffset : 0f, ButtonRow);

            Show(true);
        }

        /// <summary>
        /// Whether the panel can offer a way onward from a level at this index.
        /// </summary>
        /// <remarks>
        /// The last level of the run has nowhere to go, and says so by having no button rather than
        /// wrapping silently back to the first.
        ///
        /// **A level off the rotation has somewhere to go, and it is the first level.** The guided
        /// tutorial is deliberately not in `AvailableLevels`, so its index is -1, and an index of -1
        /// used to fail the same test the last level fails -- leaving KEEP TINKERING as the only
        /// button on the panel. The closing card normally carries a player onward from there, but it
        /// only comes to someone who did not press skip; skip, then solve the level anyway, and the
        /// game had no way forward that was not a keyboard shortcut. Stepping one from -1 lands on
        /// the first level, which is where that player wants to be.
        ///
        /// Static so the three cases can be tested without a scene: off the rotation, mid-run, and
        /// the last level.
        /// </remarks>
        public static bool HasSomewhereToGo(int levelIndex, int levelCount)
        {
            if (levelCount <= 0)
                return false;

            return levelIndex < 0 || levelIndex < levelCount - 1;
        }

        /// <summary>
        /// The player's own best on this level, and whether this run beat it.
        /// </summary>
        /// <remarks>
        /// A personal best, not a rank. It is the same kind of fact as the gate count already on
        /// this panel -- a description of the circuit in front of them, measured against the circuit
        /// they built last time. Nobody else's number appears anywhere.
        ///
        /// Gates and latency are reported separately because they trade against each other, so
        /// beating one while losing the other is a real and interesting outcome rather than a
        /// contradiction to be collapsed.
        /// </remarks>
        private void AppendRecords(System.Text.StringBuilder detail)
        {
            if (_progress == null)
                return;

            string level = _session.LevelName;

            int bestGates = _progress.BestGates(level);
            int bestLatency = _progress.BestLatency(level);

            if (bestGates <= 0 && bestLatency <= 0)
                return;

            detail.Append($"\n\nbest so far   {bestGates} gates   {bestLatency} ticks");

            if (_progress.BeatGateRecord && _progress.BeatLatencyRecord)
                detail.Append("\nsmaller and faster than last time");
            else if (_progress.BeatGateRecord)
                detail.Append("\nsmaller than last time");
            else if (_progress.BeatLatencyRecord)
                detail.Append("\nfaster than last time");
        }

        private void Show(bool visible)
        {
            _shown = visible;
            Draw();
        }

        /// <summary>
        /// Puts the card on screen when it is both owed and not covered.
        /// </summary>
        /// <remarks>
        /// This card is deliberately not a modal -- it belongs on the board, not over the whole
        /// screen -- but that settles what it does to other panels, not what they do to it. It used
        /// to hide from nothing and merely skip coming to the front, which laid a scrim over a live
        /// panel; and a run that passed while a panel was open activated it behind that scrim at
        /// whatever sibling index it held, with no BringToFront ever to correct it.
        ///
        /// <see cref="IsShowing"/> stays the intent rather than what is drawn, because the tutorial
        /// director and the music both read it to know the card is still owed. A card that forgot
        /// itself here would let the tutorial's ending card through early.
        /// </remarks>
        private void Draw()
        {
            bool wanted = _shown && UiModal.HudVisible;

            if (_root.gameObject.activeSelf == wanted)
                return;

            _root.gameObject.SetActive(wanted);

            // Forward as it appears, including when it comes back after the panel that covered it
            // has closed.
            if (wanted)
                UiTheme.BringToFront(_root);
        }

        private void NextLevel()
        {
            Show(false);
            _session.CycleLevel(1);
            Deselect();
        }

        /// <summary>
        /// Dismisses without leaving the level.
        /// </summary>
        /// <remarks>
        /// Deliberately does not reset the board. A player who wants to try a smaller circuit should
        /// find their solved one still sitting there to edit, not an empty grid.
        /// </remarks>
        private void Dismiss()
        {
            Show(false);
            Deselect();
        }

        private static void Deselect()
        {
            if (EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(null);
        }
    }
}
