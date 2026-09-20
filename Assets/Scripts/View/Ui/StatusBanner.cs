using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace BitSorter.View
{
    /// <summary>
    /// The level's name, what it is asking for, and how the last run went.
    /// </summary>
    /// <remarks>
    /// **The goal only. The hint belongs to <see cref="HelpPanel"/>.**
    ///
    /// Both used to be here, and the help panel showed the hint as well -- the same sentence, at the
    /// same size, in the same colour, on screen twice at once. A player who read "press ? to see it",
    /// pressed it, and was handed a line already in front of them learned that the help button was
    /// not worth pressing. It also broke the rule the rest of this layer follows: a second copy of a
    /// fact is a second thing to drift.
    ///
    /// The help panel keeps it because asking for a nudge is a decision. The goal is the brief and
    /// is always readable; the hint is advice, and advice nobody asked for is noise on a strip that
    /// also carries first-time hints and the tutorial's instructions.
    ///
    /// Goal and hint remain separate *fields* for the reason they always were -- keeping them apart
    /// is what stopped hints drifting into stating their own answers, which `CurriculumTests` still
    /// enforces on the hint alone. See <see cref="LevelDefinition.Goal"/>.
    ///
    /// Everything here is polled. Every renderer in this project polls rather than subscribing, and
    /// a banner that subscribed would need to hear about run state, verdicts and refusals from three
    /// different places.
    /// </remarks>
    public sealed class StatusBanner : MonoBehaviour
    {
        [SerializeField] private LevelSession _session;
        [SerializeField] private SimulationRunner _runner;

        [Tooltip("Canvas the banner is built under. Found by type when left empty.")]
        [SerializeField] private Canvas _canvas;

        [Tooltip("Seconds a refusal stays on screen.")]
        [SerializeField] private float _rejectionSeconds = 2f;

        private RectTransform _root;
        private TextMeshProUGUI _title;
        private TextMeshProUGUI _goal;
        private TextMeshProUGUI _verdict;
        private Image _toastBackground;
        private TextMeshProUGUI _toast;

        // What the title and the verdict were last drawn from. Both are formatted strings, and
        // formatting them every frame handed TextMeshPro text equal to what it already had while
        // leaving garbage behind each time. They are redrawn only when one of these changes.
        private LevelDefinition _titleLevel;
        private int _titleIndex;
        private int _titleCount;

        /// <summary>The goal the banner was last sized for.</summary>
        private string _goalShown;

        private RunState? _verdictState;
        private string _verdictReason;
        private bool _verdictPaused;

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

            Image panel = UiTheme.Panel_("Status", _canvas.transform, UiTheme.Panel);
            var root = panel.GetComponent<RectTransform>();
            _root = root;
            // Taller and wider than it was: the goal and hint were sized for glanceability and ended
            // up needing a lean-in. Each row below is placed from the one above rather than from a
            // fixed offset, so a future size change moves the stack instead of overlapping it.
            UiTheme.Anchor(root, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -UiTheme.Margin),
                new Vector2(UiTheme.BannerWidth, UiTheme.BannerHeight));

            panel.raycastTarget = false;   // the banner is a readout, never a click target

            _title = UiTheme.Label("title", root, 24f, UiTheme.Text, TextAlignmentOptions.Center);
            UiTheme.Anchor(_title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -10f), new Vector2(UiTheme.BannerTextWidth, 30f));

            // Top-aligned, not centred: a goal longer than its box has to grow down into the room
            // below it rather than out of both ends and over the title.
            _goal = UiTheme.Label(
                "goal", root, UiTheme.BannerGoalFontSize, UiTheme.Accent, TextAlignmentOptions.Top);
            UiTheme.Anchor(_goal.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -UiTheme.BannerTitleBlock),
                new Vector2(UiTheme.BannerTextWidth, UiTheme.BannerGoalHeight));
            _goal.textWrappingMode = TextWrappingModes.Normal;

            _verdict = UiTheme.Label("verdict", root, 18f, UiTheme.Text, TextAlignmentOptions.Center);
            UiTheme.Anchor(_verdict.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 1f),
                new Vector2(0f, -6f), new Vector2(UiTheme.BannerTextWidth, 26f));

            _toastBackground = UiTheme.Panel_("Toast", _canvas.transform, UiTheme.Bad * 0.5f);
            var toastRect = _toastBackground.GetComponent<RectTransform>();

            // On its own row above the controls line, from the shared arithmetic in UiTheme. These
            // two used to be positioned independently and overlapped: "no port there" drew straight
            // over "drag a port to wire", at exactly the moment the player most needed to read both.
            UiTheme.Anchor(toastRect, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, UiTheme.ToastRow),
                new Vector2(520f, UiTheme.ToastHeight));
            _toastBackground.raycastTarget = false;

            _toast = UiTheme.Label("toast text", toastRect, 16f, UiTheme.Text, TextAlignmentOptions.Center);
            UiTheme.Stretch(_toast.rectTransform, 6f);
        }

        private void Update()
        {
            if (_session == null || _title == null)
                return;

            // Out of the way of a full-screen panel, whose own title would otherwise print over this.
            bool shown = UiModal.HudVisible;
            UiTheme.SetShown(_root, shown);

            if (!shown)
            {
                ShowToast(false);
                return;
            }

            if (!_session.IsLoaded)
            {
                _title.text = "LEVEL DID NOT LOAD";
                _title.color = UiTheme.Bad;
                _goal.text = _session.LoadError ?? string.Empty;
                _goalShown = _goal.text;
                FitToGoal();
                _verdict.text = string.Empty;
                ShowToast(false);

                // Both were just overwritten, so both are drawn afresh once a level does load.
                _titleLevel = null;
                _verdictState = null;
                return;
            }

            LevelDefinition level = _session.Level;

            _title.color = UiTheme.Text;

            // Where this level sits in the run, so Q and E have somewhere to land the eye. Omitted
            // when there is only one level, where "1 of 1" says nothing.
            int index = _session.LevelIndex;
            int count = _session.AvailableLevels.Count;

            if (level != _titleLevel || index != _titleIndex || count != _titleCount)
            {
                _title.text = count > 1 && index >= 0
                    ? $"{level.Name.ToUpperInvariant()}   {index + 1} / {count}"
                    : level.Name.ToUpperInvariant();

                _titleLevel = level;
                _titleIndex = index;
                _titleCount = count;
            }

            if (!string.Equals(_goalShown, level.Goal))
            {
                _goal.text = level.Goal;
                _goalShown = level.Goal;
                FitToGoal();
            }

            ShowVerdict();
            ShowToast(_runner != null && _runner.WasRecentlyRejected(_rejectionSeconds));

            if (_toast != null && _runner != null)
                _toast.text = _runner.LastRejectionReason ?? string.Empty;
        }

        /// <summary>
        /// Shrinks the banner to the goal it is showing, within the room UiTheme reserves for it.
        /// </summary>
        /// <remarks>
        /// The reserved height is the worst case, and most goals are one line -- drawn at the full
        /// height, the banner would be a box with forty empty pixels under most of them, which was
        /// invisible while panels faded out at their edges and is not now they have real ones.
        ///
        /// Only the drawn panel moves. Everything placed below the banner still measures from
        /// <see cref="UiTheme.BannerHeight"/>, so a short banner leaves a slightly wider gap and
        /// never a collision.
        /// </remarks>
        private void FitToGoal()
        {
            if (_root == null)
                return;

            float wanted = UiTheme.BannerTitleBlock + UiTheme.GoalHeight(_goalShown) + UiTheme.BannerPad;

            _root.sizeDelta = new Vector2(
                _root.sizeDelta.x, Mathf.Min(wanted, UiTheme.BannerHeight));
        }

        private void ShowVerdict()
        {
            RunState state = _session.State;
            string reason = _session.Verdict.Reason;
            bool paused = _runner != null && _runner.IsPaused;

            if (state == _verdictState && reason == _verdictReason && paused == _verdictPaused)
                return;

            _verdictState = state;
            _verdictReason = reason;
            _verdictPaused = paused;

            switch (state)
            {
                case RunState.Passed:
                    _verdict.color = UiTheme.Good;
                    _verdict.text = "PASS -- " + reason;
                    break;

                case RunState.Failed:
                    _verdict.color = UiTheme.Bad;
                    _verdict.text = "FAIL -- " + reason;
                    break;

                case RunState.Running:
                    _verdict.color = UiTheme.TextDim;
                    _verdict.text = paused ? "PAUSED" : "RUNNING";
                    break;

                // Neutral on purpose. Free play has no verdict, and colouring this Good or Bad would
                // invent one -- the sink readout is where the player looks for what happened.
                case RunState.Finished:
                    _verdict.color = UiTheme.TextDim;
                    _verdict.text = "DONE";
                    break;

                default:
                    _verdict.text = string.Empty;
                    break;
            }
        }

        private void ShowToast(bool visible)
        {
            if (_toastBackground != null && _toastBackground.gameObject.activeSelf != visible)
                _toastBackground.gameObject.SetActive(visible);
        }
    }
}
