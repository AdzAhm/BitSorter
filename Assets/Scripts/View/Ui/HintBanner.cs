using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;

namespace BitSorter.View
{
    /// <summary>
    /// One line of teaching, directly under the status banner, shown when
    /// <see cref="FirstTimeHints"/> decides a mechanic has just been met for the first time.
    /// </summary>
    /// <remarks>
    /// A readout and nothing else. It decides *how* a hint is shown and for how long; whether there
    /// is a hint to show at all belongs to <see cref="FirstTimeHints"/>, and whether it has been
    /// shown before belongs to <see cref="ProgressStore"/>.
    ///
    /// Never a raycast target, so the board and every button underneath keep working while it is up.
    /// A lesson that ate a click on Run would be a lesson in the wrong thing.
    ///
    /// It never pauses the game. The moment being explained is happening on the board right now, and
    /// stopping the board to talk about it would turn a hint into a cutscene.
    /// </remarks>
    public sealed class HintBanner : MonoBehaviour
    {
        [Tooltip("Canvas the line is built under. Found by type when left empty.")]
        [SerializeField] private Canvas _canvas;

        [Tooltip("Seconds a hint stays up. Long: this is a sentence to read, not a buzz.")]
        [SerializeField] private float _seconds = 9f;

        [Tooltip("Ignore dismissal input for this long, so the click that caused it does not eat it.")]
        [SerializeField] private float _graceSeconds = 0.35f;

        private Image _background;
        private TextMeshProUGUI _text;
        private float _remaining;

        /// <summary>Whether a hint is on screen. Kept so hints queue rather than cut each other off.</summary>
        public bool IsShowing => _remaining > 0f;

        private void Awake()
        {
            if (_canvas == null) _canvas = FindFirstObjectByType<Canvas>();
        }

        private void Start()
        {
            if (_canvas == null)
                return;

            _background = UiTheme.Panel_("Hint", _canvas.transform, UiTheme.Accent * 0.5f);
            var rect = _background.GetComponent<RectTransform>();

            UiTheme.Anchor(rect, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -UiRows.Hint.Offset),
                new Vector2(UiTheme.BannerWidth, UiRows.Hint.Height));

            _background.raycastTarget = false;

            _text = UiTheme.Label("hint text", rect, UiType.Label, UiTheme.Text, TextAlignmentOptions.Center);
            UiTheme.Stretch(_text.rectTransform, 10f);
            _text.textWrappingMode = TextWrappingModes.Normal;
            _text.raycastTarget = false;

            _background.gameObject.SetActive(false);
        }

        private void Update()
        {
            if (!IsShowing)
                return;

            // Held, not spent, while a full-screen panel has the screen. FirstTimeHints marks a
            // hint seen the moment it raises one, so a countdown that ran out behind a level list
            // would not be a hint delayed -- it would be one of the game's four mechanic
            // explanations that this save never shows again. The producer already refuses to raise
            // one while a panel is open; this is the other order, a hint already up when the panel
            // arrives.
            if (!UiModal.HudVisible)
            {
                UiTheme.SetShown(_background, false);
                return;
            }

            UiTheme.SetShown(_background, true);

            _remaining -= Time.deltaTime;

            // Dismissable early, but not by the very click or keypress that triggered it -- placing
            // the last gate is exactly the kind of action that both causes a hint and would
            // otherwise dismiss it in the same frame.
            //
            // Nor by the press that closed the panel it was waiting behind: by then the grace
            // period is long past, so the one Escape that dismissed a level list would take the
            // held hint with it on the very frame it came back.
            if (_seconds - _remaining > _graceSeconds && !UiModal.OpenOrJustClosed && Dismissed())
                _remaining = 0f;

            if (!IsShowing)
                Hide();
        }

        /// <summary>
        /// Whether the player has asked for the hint to go away.
        /// </summary>
        /// <remarks>
        /// A click anywhere counts, and so do the two keys that mean "I have read it" -- Escape and
        /// Space. Every other key does not, which is the whole point: this used to take any key at
        /// all, so Q and E stepping through levels, R resetting the board and the arrow key walking
        /// a run all silently threw away a hint the player had not finished reading. None of those
        /// presses were aimed at it.
        ///
        /// Space is deliberately included even though it pauses a run: a player reaching for pause
        /// mid-hint is looking at the board, which is exactly when the hint has done its job.
        /// </remarks>
        private static bool Dismissed()
        {
            Mouse mouse = Mouse.current;

            if (mouse != null && (mouse.leftButton.wasPressedThisFrame ||
                                  mouse.rightButton.wasPressedThisFrame))
            {
                return true;
            }

            Keyboard keyboard = Keyboard.current;

            return keyboard != null
                   && (keyboard.escapeKey.wasPressedThisFrame || keyboard.spaceKey.wasPressedThisFrame);
        }

        /// <summary>Puts a hint up. Ignores anything empty, so a missing id shows no empty bar.</summary>
        public void Show(string message)
        {
            if (_background == null || string.IsNullOrWhiteSpace(message))
                return;

            _text.text = message;
            _remaining = _seconds;

            _background.gameObject.SetActive(true);
            UiTheme.BringToFront(_background.rectTransform);
        }

        /// <summary>Takes it down early, for a level change that makes it irrelevant.</summary>
        public void Hide()
        {
            _remaining = 0f;

            if (_background != null && _background.gameObject.activeSelf)
                _background.gameObject.SetActive(false);
        }
    }
}
