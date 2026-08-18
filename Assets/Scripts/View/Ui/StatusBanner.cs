using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace BitSorter.View
{
    /// <summary>
    /// The level's name, what it is asking for, and how the last run went.
    /// </summary>
    /// <remarks>
    /// Goal and hint are separate lines because they are separate things, and keeping them apart is
    /// what stopped hints drifting into stating their own answers -- see
    /// <see cref="LevelDefinition.Goal"/>. The goal is the brief and is always readable; the hint is
    /// a nudge and is deliberately quieter.
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

        private TextMeshProUGUI _title;
        private TextMeshProUGUI _goal;
        private TextMeshProUGUI _hint;
        private TextMeshProUGUI _verdict;
        private Image _toastBackground;
        private TextMeshProUGUI _toast;

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
            // Taller and wider than it was: the goal and hint were sized for glanceability and ended
            // up needing a lean-in. Each row below is placed from the one above rather than from a
            // fixed offset, so a future size change moves the stack instead of overlapping it.
            UiTheme.Anchor(root, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -UiTheme.Margin), new Vector2(780f, 122f));

            panel.raycastTarget = false;   // the banner is a readout, never a click target

            _title = UiTheme.Label("title", root, 24f, UiTheme.Text, TextAlignmentOptions.Center);
            UiTheme.Anchor(_title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -10f), new Vector2(760f, 30f));

            _goal = UiTheme.Label("goal", root, 19f, UiTheme.Accent, TextAlignmentOptions.Center);
            UiTheme.Anchor(_goal.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -44f), new Vector2(760f, 28f));
            _goal.textWrappingMode = TextWrappingModes.Normal;

            _hint = UiTheme.Label("hint", root, 15f, UiTheme.TextDim, TextAlignmentOptions.Center);
            UiTheme.Anchor(_hint.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -78f), new Vector2(760f, 24f));

            _verdict = UiTheme.Label("verdict", root, 18f, UiTheme.Text, TextAlignmentOptions.Center);
            UiTheme.Anchor(_verdict.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 1f),
                new Vector2(0f, -6f), new Vector2(760f, 26f));

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

            if (!_session.IsLoaded)
            {
                _title.text = "LEVEL DID NOT LOAD";
                _title.color = UiTheme.Bad;
                _goal.text = _session.LoadError ?? string.Empty;
                _hint.text = string.Empty;
                _verdict.text = string.Empty;
                ShowToast(false);
                return;
            }

            LevelDefinition level = _session.Level;

            _title.color = UiTheme.Text;

            // Where this level sits in the run, so Q and E have somewhere to land the eye. Omitted
            // when there is only one level, where "1 of 1" says nothing.
            int index = _session.LevelIndex;
            int count = _session.AvailableLevels.Count;

            _title.text = count > 1 && index >= 0
                ? $"{level.Name.ToUpperInvariant()}   {index + 1} / {count}"
                : level.Name.ToUpperInvariant();

            _goal.text = level.Goal;

            // The hint steps aside once the run is over: at that point the verdict is the thing to
            // read, and two lines of advice under it just competes for attention.
            bool editing = _session.State == RunState.Editing;
            _hint.text = editing ? level.Hint : string.Empty;

            ShowVerdict();
            ShowToast(_runner != null && _runner.WasRecentlyRejected(_rejectionSeconds));

            if (_toast != null && _runner != null)
                _toast.text = _runner.LastRejectionReason ?? string.Empty;
        }

        private void ShowVerdict()
        {
            switch (_session.State)
            {
                case RunState.Passed:
                    _verdict.color = UiTheme.Good;
                    _verdict.text = "PASS -- " + _session.Verdict.Reason;
                    break;

                case RunState.Failed:
                    _verdict.color = UiTheme.Bad;
                    _verdict.text = "FAIL -- " + _session.Verdict.Reason;
                    break;

                case RunState.Running:
                    _verdict.color = UiTheme.TextDim;
                    _verdict.text = _runner != null && _runner.IsPaused ? "RUNNING (paused)" : "RUNNING";
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
