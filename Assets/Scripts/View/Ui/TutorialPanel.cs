using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

namespace BitSorter.View
{
    /// <summary>
    /// What the tutorial is currently asking for, with a way out of it.
    /// </summary>
    /// <remarks>
    /// A readout and two buttons. It renders whatever <see cref="TutorialDirector"/> hands it and
    /// decides nothing about the sequence.
    ///
    /// Sits directly under the first-time hint line, from <see cref="UiTheme"/>'s shared arithmetic
    /// rather than a fresh offset, so the two can never draw over each other — the mistake the
    /// refusal toast and the controls line made before those rows were worked out in one place.
    ///
    /// The panel itself is not a raycast target, but its buttons are: Skip and Next have to be
    /// clickable, and everything else must not be, or the panel would sit between the player and the
    /// board it is telling them to use.
    /// </remarks>
    public sealed class TutorialPanel : MonoBehaviour
    {
        [Tooltip("Canvas the panel is built under. Found by type when left empty.")]
        [SerializeField] private Canvas _canvas;

        private RectTransform _root;
        private TextMeshProUGUI _text;
        private Button _skip;
        private Button _next;
        private TextMeshProUGUI _nextLabel;

        private bool _skipPressed;
        private bool _nextPressed;

        /// <summary>
        /// Takes the pending Skip press, if there is one, and clears it.
        /// </summary>
        /// <remarks>
        /// Consumed by the reader rather than expiring on a timer, and that is deliberate. A flag
        /// cleared in LateUpdate would depend on the EventSystem happening to process the click
        /// before the director's Update -- they are on different GameObjects, so nothing guarantees
        /// it -- and on the wrong side of that coin the press would be cleared before it was ever
        /// seen. A press waits here until somebody takes it.
        /// </remarks>
        public bool ConsumeSkip()
        {
            bool pressed = _skipPressed;
            _skipPressed = false;
            return pressed;
        }

        /// <inheritdoc cref="ConsumeSkip"/>
        public bool ConsumeNext()
        {
            bool pressed = _nextPressed;
            _nextPressed = false;
            return pressed;
        }

        /// <summary>Drops any press nobody took, for a phase change that makes it meaningless.</summary>
        public void ForgetPresses()
        {
            _skipPressed = false;
            _nextPressed = false;
        }

        private void Awake()
        {
            if (_canvas == null) _canvas = FindFirstObjectByType<Canvas>();
        }

        private void Start()
        {
            if (_canvas == null)
                return;

            Image background = UiTheme.Panel_("Tutorial", _canvas.transform, UiTheme.Panel);
            _root = background.GetComponent<RectTransform>();

            UiTheme.Anchor(_root, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -UiRows.Tutorial.Offset),
                new Vector2(UiTheme.BannerWidth, UiRows.Tutorial.Height));

            background.raycastTarget = false;

            _text = UiTheme.Label("tutorial text", _root, 17f, UiTheme.Text,
                TextAlignmentOptions.Left);
            UiTheme.Anchor(_text.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(14f, 0f), new Vector2(UiTheme.BannerWidth - 210f, UiTheme.TutorialHeight - 12f));
            _text.textWrappingMode = TextWrappingModes.Normal;
            _text.raycastTarget = false;

            _next = Corner("Next", "NEXT", 1f);
            _skip = Corner("Skip", "SKIP", 2f);

            // Attached here and not in OnEnable, which runs before Start and so before these
            // buttons exist -- the listener was silently never added, and every press did nothing.
            // Nothing detaches them: the buttons are children of a panel this component owns for
            // the whole scene, so they die together.
            _next.onClick.AddListener(Next);
            _skip.onClick.AddListener(Skip);

            Show(false);
        }

        /// <summary>A small button on the right-hand end, counted in from the edge.</summary>
        private Button Corner(string name, string caption, float slot)
        {
            const float width = 88f;
            const float height = 30f;

            Button button = UiTheme.Button_(name, _root, caption, out TextMeshProUGUI label);

            UiTheme.Anchor(button.GetComponent<RectTransform>(),
                new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(-10f - (slot - 1f) * (width + 8f), 0f), new Vector2(width, height));

            var navigation = button.navigation;
            navigation.mode = Navigation.Mode.None;
            button.navigation = navigation;

            if (name == "Next")
                _nextLabel = label;

            return button;
        }

        // Focus is dropped on every press. A Button that keeps it swallows Space and Enter, both of
        // which this game binds -- and Space is a control the last step actively recommends.

        /// <summary>Presses SKIP. Public so a test can drive the tutorial without a mouse.</summary>
        /// <remarks>
        /// The same seam <see cref="TutorialCard.Finish"/> is, and for the same reason: clicking the
        /// real button needs a canvas, an EventSystem and a pointer, and the phases either side of
        /// these presses are where the tutorial's initialisation bugs have all lived.
        /// </remarks>
        public void Skip()
        {
            _skipPressed = true;
            UiTheme.Defocus();
        }

        /// <inheritdoc cref="Skip"/>
        public void Next()
        {
            _nextPressed = true;
            UiTheme.Defocus();
        }

        /// <summary>Puts a line up, optionally offering a continue button.</summary>
        public void Show(string message, bool showNext = false, string nextCaption = "NEXT")
        {
            if (_root == null)
                return;

            _text.text = message ?? string.Empty;

            if (_next != null)
                _next.gameObject.SetActive(showNext);

            if (_nextLabel != null && showNext)
                _nextLabel.text = nextCaption;

            Show(true);
        }

        public void Show(bool visible)
        {
            if (_root == null || _root.gameObject.activeSelf == visible)
                return;

            _root.gameObject.SetActive(visible);

            if (visible)
                UiTheme.BringToFront(_root);
        }
    }
}
