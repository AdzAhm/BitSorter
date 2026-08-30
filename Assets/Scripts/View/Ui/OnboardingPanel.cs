using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;

namespace BitSorter.View
{
    /// <summary>
    /// First-run walkthrough shown once before the first level.
    /// </summary>
    public sealed class OnboardingPanel : MonoBehaviour
    {
        [SerializeField] private LevelSession _session;
        [SerializeField] private ProgressTracker _progress;

        [Tooltip("Canvas the panel is built under. Found by type when left empty.")]
        [SerializeField] private Canvas _canvas;

        private RectTransform _root;
        private TextMeshProUGUI _title;
        private TextMeshProUGUI _body;
        private TextMeshProUGUI _stepLabel;
        private TextMeshProUGUI _nextLabel;
        private Button _back;
        private Button _next;

        private bool _armed;
        private bool _shown;
        private int _step;

        private void Awake()
        {
            if (_session == null) _session = FindFirstObjectByType<LevelSession>();
            if (_progress == null) _progress = FindFirstObjectByType<ProgressTracker>();
            if (_canvas == null) _canvas = FindFirstObjectByType<Canvas>();
        }

        private void OnEnable()
        {
            if (_session != null)
                _session.LevelLoaded += OnLevelLoaded;
        }

        private void OnDisable()
        {
            if (_session != null)
                _session.LevelLoaded -= OnLevelLoaded;

            UiModal.Closed(this);
        }

        private void Start()
        {
            if (_canvas == null)
                return;

            Build();
            Show(false);

            if (_session != null && _session.IsLoaded)
                OnLevelLoaded(_session.Level);
        }

        private void Update()
        {
            if (_armed && !_shown && _session != null && _session.CanEdit && !UiModal.AnyOpen)
                Show(true);

            Keyboard keyboard = Keyboard.current;
            if (!_shown || keyboard == null)
                return;

            if (keyboard.escapeKey.wasPressedThisFrame)
                Close();
            else if (keyboard.rightArrowKey.wasPressedThisFrame && OnboardingRules.CanStepForward(_step))
                Move(+1);
            else if (keyboard.leftArrowKey.wasPressedThisFrame && OnboardingRules.CanStepBack(_step))
                Move(-1);
        }

        private void OnLevelLoaded(LevelDefinition level)
        {
            bool shownAlready = PlayerPrefs.GetInt(OnboardingRules.SeenKey, 0) != 0;
            bool solved = _progress != null && _progress.IsComplete(_session.LevelName);

            _armed = OnboardingRules.ShouldShow(_session.LevelName, shownAlready, solved);
            _step = 0;

            if (!_armed)
                Show(false);
        }

        private void Build()
        {
            Image scrim = UiTheme.Panel_("Onboarding scrim", _canvas.transform, new Color(0f, 0f, 0f, 0.84f));
            _root = scrim.GetComponent<RectTransform>();
            UiTheme.Stretch(_root);

            Image panel = UiTheme.Panel_("Onboarding panel", _root, UiTheme.Panel);
            RectTransform body = panel.GetComponent<RectTransform>();
            UiTheme.Anchor(body, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(680f, 360f));

            _title = UiTheme.Label("title", body, 28f, UiTheme.Accent, TextAlignmentOptions.Center);
            UiTheme.Anchor(_title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -24f), new Vector2(620f, 38f));

            _body = UiTheme.Label("body", body, 20f, UiTheme.Text, TextAlignmentOptions.TopLeft);
            UiTheme.Anchor(_body.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -78f), new Vector2(620f, 190f));
            _body.textWrappingMode = TextWrappingModes.Normal;

            _stepLabel = UiTheme.Label("step", body, 14f, UiTheme.TextDim, TextAlignmentOptions.Left);
            UiTheme.Anchor(_stepLabel.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(24f, 24f), new Vector2(240f, 20f));

            _back = UiTheme.Button_("Back", body, "BACK", out TextMeshProUGUI _);
            UiTheme.Anchor(_back.GetComponent<RectTransform>(), new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-302f, 16f), new Vector2(130f, 44f));
            _back.onClick.AddListener(() => Fire(() => Move(-1)));

            _next = UiTheme.Button_("Next", body, "NEXT", out _nextLabel);
            UiTheme.Anchor(_next.GetComponent<RectTransform>(), new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-160f, 16f), new Vector2(140f, 44f));
            _next.onClick.AddListener(() => Fire(AdvanceOrClose));
        }

        private void Move(int delta)
        {
            _step = OnboardingRules.ClampStep(_step + delta);
            Refresh();
        }

        private void AdvanceOrClose()
        {
            if (OnboardingRules.CanStepForward(_step))
            {
                Move(+1);
                return;
            }

            Close();
        }

        private void Close()
        {
            _armed = false;
            PlayerPrefs.SetInt(OnboardingRules.SeenKey, 1);
            PlayerPrefs.Save();
            Show(false);
        }

        private void Show(bool visible)
        {
            _shown = visible;

            if (_root != null && _root.gameObject.activeSelf != visible)
                _root.gameObject.SetActive(visible);

            if (visible)
            {
                UiTheme.BringToFront(_root);
                UiModal.Opened(this);
                Refresh();
            }
            else
            {
                UiModal.Closed(this);
            }
        }

        private void Refresh()
        {
            if (_title == null || _body == null)
                return;

            _title.text = OnboardingRules.TitleAt(_step);
            _body.text = OnboardingRules.BodyAt(_step);
            _stepLabel.text = $"STEP {_step + 1} OF {OnboardingRules.StepCount}";

            _back.interactable = OnboardingRules.CanStepBack(_step);
            bool hasNext = OnboardingRules.CanStepForward(_step);
            _nextLabel.text = hasNext ? "NEXT" : "LET'S BUILD";
        }

        private static void Fire(System.Action action)
        {
            action();

            if (EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(null);
        }
    }
}
