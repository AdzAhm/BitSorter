using TMPro;
using UnityEngine;

namespace BitSorter.View
{
    /// <summary>
    /// The frame-rate counter: frames drawn a second, as a small number in the top-right corner,
    /// while Settings' FPS COUNTER is on -- the kind a game launcher's overlay puts over a game.
    /// </summary>
    /// <remarks>
    /// Asked for with the frame cap (2026-09-30), so that what the cap and vertical sync do can be
    /// seen rather than taken on trust.
    ///
    /// **It sits in the margin every top-right control keeps**, above BACK, CLOSE and the help
    /// badge rather than over them: each of those is <see cref="UiTheme.Margin"/> in from the
    /// corner, and the counter is shorter than that, so the two cannot meet at any window size --
    /// the canvas scales both alike. Everywhere else it draws over everything, a full-screen panel
    /// included, on a canvas of its own sorted above the interface; the frame rate is as much a
    /// fact behind the main menu as on a board. It takes no clicks.
    ///
    /// Counted over half a second of real time, not game time, and written without allocating: it
    /// runs every frame, and a counter that made garbage would in the end be measuring its own
    /// collections. The digits go to TextMeshPro in a reused buffer; in the editor alone,
    /// TextMeshPro keeps a string copy of them as well.
    /// </remarks>
    public sealed class FrameRateCounter : MonoBehaviour
    {
        [Tooltip("Canvas the counter is built under. Found by type when left empty.")]
        [SerializeField] private Canvas _canvas;

        /// <summary>How long each count runs before the number is redrawn, in real seconds.</summary>
        public const float Window = 0.5f;

        /// <summary>Where the counter's own canvas sorts: above the interface's, which is 0.</summary>
        public const int SortingOrder = 100;

        /// <summary>The counter's box: wide enough for four digits, and inside the corner's margin.</summary>
        public const float Width = 64f;

        /// <inheritdoc cref="Width"/>
        public const float Height = UiTheme.Margin - 2f;

        /// <summary>How far in from the right edge the number ends.</summary>
        public const float Inset = 6f;

        private RectTransform _root;
        private TextMeshProUGUI _label;

        /// <summary>The digits of the number shown, handed to TextMeshPro without making a string.</summary>
        private readonly char[] _digits = new char[10];

        private int _frames;
        private float _elapsed;

        /// <summary>The number on screen, or -1 before the first count has finished.</summary>
        public int Shown { get; private set; } = -1;

        /// <summary>Whether the counter is on screen.</summary>
        public bool IsShowing => _root != null && _root.gameObject.activeSelf;

        /// <summary>The counter's box, for the tests.</summary>
        public RectTransform Box => _root;

        private void Awake()
        {
            if (_canvas == null) _canvas = FindFirstObjectByType<Canvas>();
        }

        private void Start()
        {
            if (_canvas == null)
                return;

            Build();
            SetShowing(FrameRate.ShowsCounter);
        }

        private void Update()
        {
            if (_root == null)
                return;

            // Asked every frame, as the settings' own switches are, so the switch and the corner
            // cannot disagree whatever turned it on.
            bool on = FrameRate.ShowsCounter;

            if (on != _root.gameObject.activeSelf)
                SetShowing(on);

            if (!on)
                return;

            _frames++;
            _elapsed += Time.unscaledDeltaTime;

            if (_elapsed < Window)
                return;

            Show(Rate(_frames, _elapsed));
            _frames = 0;
            _elapsed = 0f;
        }

        /// <summary>Frames a second over a stretch of <paramref name="seconds"/>, rounded.</summary>
        public static int Rate(int frames, float seconds) =>
            seconds > 0f && frames > 0 ? Mathf.RoundToInt(frames / seconds) : 0;

        /// <summary>
        /// Writes <paramref name="value"/>'s decimal digits into <paramref name="into"/> and says how
        /// many there are. Nothing below zero is a rate, so it reads as 0.
        /// </summary>
        public static int Digits(int value, char[] into)
        {
            if (value <= 0)
            {
                into[0] = '0';
                return 1;
            }

            int length = 0;
            for (int rest = value; rest > 0; rest /= 10)
                length++;

            for (int i = length - 1; i >= 0; i--)
            {
                into[i] = (char)('0' + value % 10);
                value /= 10;
            }

            return length;
        }

        private void Show(int rate)
        {
            if (rate == Shown)
                return;

            Shown = rate;
            _label.SetCharArray(_digits, 0, Digits(rate, _digits));
        }

        /// <summary>
        /// On or off. Either way the count starts again, so a number shown is never one counted
        /// partly while the counter was hidden.
        /// </summary>
        private void SetShowing(bool on)
        {
            _root.gameObject.SetActive(on);
            _frames = 0;
            _elapsed = 0f;
            Shown = -1;
            _label.SetText(string.Empty);
        }

        private void Build()
        {
            _root = UiTheme.Rect("Frame rate counter", _canvas.transform);

            // A canvas of its own, sorted above the interface's, so that no panel brought to the
            // front can cover it. It has no raycaster: nothing on it is ever clicked.
            Canvas own = _root.gameObject.AddComponent<Canvas>();
            own.overrideSorting = true;
            own.sortingOrder = SortingOrder;

            UiTheme.Anchor(_root, new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-Inset, -1f), new Vector2(Width, Height));

            _label = UiTheme.Label("fps", _root, UiType.Micro, UiTheme.Text, TextAlignmentOptions.MidlineRight);
            UiTheme.Stretch(_label.rectTransform);
            _label.raycastTarget = false;
        }
    }
}
