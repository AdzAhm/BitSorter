using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;

namespace BitSorter.View
{
    /// <summary>
    /// The card between the two halves of the game: combinational logic ends, and circuits start
    /// to remember.
    /// </summary>
    /// <remarks>
    /// Nine levels arrive as a flat list, and the roadmap has wanted a card at the syllabus
    /// boundary since the first playthrough read as a tool rather than a game. This is that card,
    /// and the boundary it marks is a real one: everything before it forgets each bit the moment it
    /// has used it, and everything after keeps one.
    ///
    /// **Fired by what the level stocks, not by its number.** It shows the first time a level with
    /// a register on its parts list is opened, so inserting or reordering levels cannot leave it on
    /// the wrong one. Once shown it is a milestone in the save, beside the tutorial -- not a hint,
    /// whose ids are held to hint rules by tests, and not a completion, which would make the level
    /// count wrong.
    ///
    /// It says what changes rather than how to build anything. The level's own goal is where the
    /// first machine is explained; a card that taught a mechanic would be a third voice saying what
    /// the goal and the first-time hint already say.
    /// </remarks>
    public sealed class ChapterCard : FullScreenPanel
    {
        /// <summary>The save's record that this card has been shown.</summary>
        public const string Milestone = "chapter.sequential";

        [SerializeField] private LevelSession _session;
        [SerializeField] private ProgressTracker _progress;

        [Tooltip("Canvas the card is built under. Found by type when left empty.")]
        [SerializeField] private Canvas _canvas;

        /// <summary>From <see cref="LevelCatalog"/>, so the card and the level list agree.</summary>
        private const string Title = LevelCatalog.SequentialChapter;

        private const string Body =
            "Every gate so far has forgotten each bit the moment it used it. " +
            "From here you have a part that keeps one.\n\n" +
            "That changes the clock as well. A kept bit has to travel back round to meet the next " +
            "one, so vectors stop arriving every tick and start arriving on a beat -- and " +
            "everything your circuit does has to fit inside it.";

        private bool _due;

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

        protected override void OnDisable()
        {
            if (_session != null)
                _session.LevelLoaded -= OnLevelLoaded;

            base.OnDisable();
        }

        private void Start()
        {
            if (_canvas == null)
                return;

            Build();
            Show(false);
        }

        /// <summary>
        /// Noted on the load and shown a frame later, from Update.
        /// </summary>
        /// <remarks>
        /// A level load is a busy frame -- the board is rebuilt, the banner and the parts list
        /// redraw -- and a full-screen panel going up inside it would be racing all of that. The
        /// flag is set here and read where every other panel reads its state.
        /// </remarks>
        private void OnLevelLoaded(LevelDefinition level)
        {
            if (IsShowing)
                return;

            // Whatever loads now is what the card is or is not owed for. Left standing, a card
            // made due by one level would open over the next one: the flag is set on load and read
            // a frame or more later, and anything that loads a level while a panel is up -- the
            // level list is open the whole time it is being chosen from -- puts a load in between.
            _due = false;

            if (level == null || !LevelCatalog.IsSequential(level))
                return;

            // Free play and the tutorial are not levels in the run, and free play stocks every
            // part -- including a register -- so without this the card takes the screen the first
            // time anyone opens the sandbox, and takes the setup panel down with the rest of the
            // HUD while it is there. A chapter boundary belongs to the chapters.
            if (LevelCatalog.IsOffCatalogue(_session.LevelName))
                return;

            ProgressStore store = _progress != null ? _progress.Store : null;

            if (store == null || store.HasMilestone(Milestone))
                return;

            _due = true;
        }

        private void Update()
        {
            if (_due && !IsShowing && !UiModal.AnyOpen)
            {
                _due = false;
                Show(true);
                return;
            }

            if (!IsShowing)
                return;

            Keyboard keyboard = Keyboard.current;

            if (keyboard != null &&
                (keyboard.escapeKey.wasPressedThisFrame || keyboard.enterKey.wasPressedThisFrame))
            {
                Dismiss();
            }
        }

        private void Build()
        {
            Image scrim = UiTheme.Scrim("Chapter card", _canvas.transform, Palette.Current.CardScrim);
            Root = scrim.GetComponent<RectTransform>();
            UiTheme.Stretch(Root);

            TextMeshProUGUI title = UiTheme.Label(
                "title", Root, 40f, UiTheme.Accent, TextAlignmentOptions.Center);
            UiTheme.Anchor(title.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, 120f), new Vector2(760f, 56f));
            title.text = Title;

            TextMeshProUGUI body = UiTheme.Label(
                "body", Root, 19f, UiTheme.Text, TextAlignmentOptions.Center);
            UiTheme.Anchor(body.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, 10f), new Vector2(620f, 150f));
            body.textWrappingMode = TextWrappingModes.Normal;
            body.text = Body;

            Button go = UiTheme.Button_("Go on", Root, "GO ON", out TextMeshProUGUI _);
            UiTheme.Anchor(go.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(0f, -120f),
                new Vector2(260f, UiTheme.ButtonHeight + 6f));

            go.onClick.AddListener(Dismiss);
        }

        private void Show(bool visible) => SetShowing(visible);

        /// <summary>
        /// Takes the card down and records it, so it is shown once ever rather than once a session.
        /// </summary>
        private void Dismiss()
        {
            if (!IsShowing)
                return;

            Show(false);

            ProgressStore store = _progress != null ? _progress.Store : null;
            store?.MarkMilestone(Milestone);
        }
    }
}
