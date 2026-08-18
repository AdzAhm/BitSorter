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

        private RectTransform _root;
        private TextMeshProUGUI _title;
        private TextMeshProUGUI _detail;
        private Button _next;
        private TextMeshProUGUI _nextLabel;

        private RunState _state = RunState.Editing;
        private bool _shown;

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
            UiTheme.Anchor(_root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(460f, 210f));

            _title = UiTheme.Label("title", _root, 30f, UiTheme.Good, TextAlignmentOptions.Center);
            UiTheme.Anchor(_title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -28f), new Vector2(420f, 40f));

            _detail = UiTheme.Label("detail", _root, 16f, UiTheme.Text, TextAlignmentOptions.Center);
            UiTheme.Anchor(_detail.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -78f), new Vector2(420f, 70f));
            _detail.textWrappingMode = TextWrappingModes.Normal;

            _next = UiTheme.Button_("Next", _root, "NEXT LEVEL", out _nextLabel);
            UiTheme.Anchor(_next.GetComponent<RectTransform>(), new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f), new Vector2(-72f, 24f), new Vector2(150f, UiTheme.ButtonHeight));
            _next.onClick.AddListener(NextLevel);

            Button stay = UiTheme.Button_("Stay", _root, "KEEP TINKERING", out TextMeshProUGUI _);
            UiTheme.Anchor(stay.GetComponent<RectTransform>(), new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f), new Vector2(80f, 24f), new Vector2(170f, UiTheme.ButtonHeight));
            stay.onClick.AddListener(Dismiss);

            _root.gameObject.SetActive(false);
        }

        private void Update()
        {
            if (_session == null || _root == null)
                return;

            RunState now = _session.State;

            if (now != _state)
            {
                if (now == RunState.Passed)
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

            // The last level has nowhere to go, so the button says so rather than wrapping silently
            // back to the tutorial.
            bool hasNext = _session.LevelIndex >= 0
                           && _session.LevelIndex < _session.AvailableLevels.Count - 1;

            _next.gameObject.SetActive(hasNext);
            _nextLabel.text = "NEXT LEVEL";

            Show(true);
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

            detail.Append($"\n\nyour best   {bestGates} gates   {bestLatency} ticks");

            if (_progress.BeatGateRecord && _progress.BeatLatencyRecord)
                detail.Append("\nsmaller AND faster than last time");
            else if (_progress.BeatGateRecord)
                detail.Append("\nsmaller than last time");
            else if (_progress.BeatLatencyRecord)
                detail.Append("\nfaster than last time");
        }

        private void Show(bool visible)
        {
            _shown = visible;

            if (_root.gameObject.activeSelf != visible)
                _root.gameObject.SetActive(visible);
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
