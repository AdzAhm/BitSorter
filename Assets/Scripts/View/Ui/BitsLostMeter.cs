using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace BitSorter.View
{
    /// <summary>
    /// The live count of destroyed bits, in the game interface rather than behind the diagnostics key.
    /// </summary>
    /// <remarks>
    /// Corruption is not a diagnostic. It is the feedback loop that makes balance-the-paths teach
    /// anything: the player runs an unbalanced circuit, watches this climb 2, 4, 6, 8 while the bits
    /// are still moving, and works out from the timing of the increments which junction is at fault.
    /// Deferring that to the end-of-run verdict would leave them with a number and no way to connect
    /// it to what they saw.
    ///
    /// Polled against a cached count rather than driven by an event, which is the established idiom
    /// here -- EdgeRenderer.FireChangeSpark does the same against WireDelayController.ChangeCount.
    /// </remarks>
    public sealed class BitsLostMeter : MonoBehaviour
    {
        [SerializeField] private SimulationRunner _runner;
        [SerializeField] private LevelSession _session;

        [Tooltip("Canvas the meter is built under. Found by type when left empty.")]
        [SerializeField] private Canvas _canvas;

        [Tooltip("Seconds the scale pop takes to settle back.")]
        [SerializeField] private float _punchSeconds = 0.28f;

        [Tooltip("How much bigger the counter jumps on each increment.")]
        [SerializeField] private float _punchScale = 0.35f;

        private RectTransform _root;
        private Image _background;
        private TextMeshProUGUI _label;

        /// <summary>Last count this reacted to. Reset with the board, so a rerun punches again.</summary>
        private int _seen;

        private float _punch;

        private void Awake()
        {
            if (_runner == null) _runner = FindFirstObjectByType<SimulationRunner>();
            if (_session == null) _session = FindFirstObjectByType<LevelSession>();
            if (_canvas == null) _canvas = FindFirstObjectByType<Canvas>();
        }

        private void Start()
        {
            if (_canvas == null)
                return;

            _background = UiTheme.Panel_("Bits lost", _canvas.transform, UiTheme.Bad * 0.75f);
            _root = _background.GetComponent<RectTransform>();

            // Under the board's right shoulder: in the eye's path while watching bits move, without
            // covering the wires whose timing the player is trying to read.
            UiTheme.Anchor(_root, new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-UiTheme.Margin, -UiTheme.Margin), new Vector2(200f, 46f));

            _background.raycastTarget = false;

            _label = UiTheme.Label("count", _root, 22f, Color.white, TextAlignmentOptions.Center);
            UiTheme.Stretch(_label.rectTransform);

            _root.gameObject.SetActive(false);
        }

        private void Update()
        {
            if (_root == null || _runner == null || !_runner.IsReady)
                return;

            int destroyed = _runner.View.CorruptedCount;

            // A rebuild throws the graph away and the count with it, so the meter has to forget too --
            // otherwise a second run would never punch, having already "seen" a higher number.
            if (destroyed < _seen)
            {
                _seen = destroyed;
                _punch = 0f;
            }

            if (BitsLostReadout.Rose(_seen, destroyed))
            {
                _seen = destroyed;
                _punch = 1f;   // full pop, decayed below
            }

            bool visible = BitsLostReadout.IsVisible(destroyed);

            if (_root.gameObject.activeSelf != visible)
                _root.gameObject.SetActive(visible);

            if (!visible)
                return;

            // Set every frame rather than only on change: the text is the thing that must never lag
            // the simulation, and one assignment a frame is cheaper than reasoning about when it can
            // be skipped.
            _label.text = BitsLostReadout.Describe(destroyed);

            Animate();
        }

        /// <summary>
        /// Decays the pop.
        /// </summary>
        /// <remarks>
        /// Scale only, and it snaps to full size on the increment rather than easing into it. The
        /// count itself is never interpolated: 2, 4, 6, 8 has to read as four discrete events, and a
        /// number lerping through 3, 5, 7 would destroy the one signal the player is reading.
        /// </remarks>
        private void Animate()
        {
            if (_punchSeconds > 0f && _punch > 0f)
                _punch = Mathf.Max(0f, _punch - Time.deltaTime / _punchSeconds);

            float scale = 1f + _punch * _punchScale;
            _root.localScale = new Vector3(scale, scale, 1f);

            // Flashes towards white at the peak and settles back to the sink red used everywhere else
            // for a destroyed bit, so the colour means the same thing here as it does on the board.
            _background.color = Color.Lerp(UiTheme.Bad * 0.75f, Color.white, _punch * 0.6f);
        }
    }
}
