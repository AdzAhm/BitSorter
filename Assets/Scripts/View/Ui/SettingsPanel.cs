using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;

namespace BitSorter.View
{
    /// <summary>
    /// The settings: sound, reporting, and starting over. Reached from the main menu, and goes back
    /// to it.
    /// </summary>
    /// <remarks>
    /// A section is a heading, a line saying what the setting does, and the control. Sound and data
    /// used to be rows of the main menu itself, which had room for a caption and nothing else -- the
    /// data switch could say ON or OFF but not what it was switching. Starting over could not be a
    /// menu row at all: it needs a sentence saying what goes, and a question before it goes.
    ///
    /// **Resetting asks first, and the question cannot be answered by accident.** Pressing RESET
    /// PROGRESS puts the question where the button was and the two answers under it, so a
    /// double-click lands on the question, which is not a button. Enter answers nothing; Escape
    /// backs out of the question before it backs out of the screen.
    /// </remarks>
    public sealed class SettingsPanel : FullScreenPanel
    {
        [SerializeField] private ProgressTracker _progress;
        [SerializeField] private MainMenu _menu;

        [Tooltip("Canvas the panel is built under. Found by type when left empty.")]
        [SerializeField] private Canvas _canvas;

        private GameAudio _audio;

        private TextMeshProUGUI _soundLabel;
        private TextMeshProUGUI _dataLabel;
        private TextMeshProUGUI _status;
        private RectTransform _resetButton;
        private RectTransform _question;

        /// <summary>Whether the panel is up.</summary>
        public bool IsOpen => IsShowing;

        /// <summary>Whether RESET PROGRESS has been pressed and is waiting for an answer.</summary>
        public bool Confirming { get; private set; }

        /// <summary>The buttons' object names, which the tests look for.</summary>
        public const string SoundButton = "Sound setting";

        /// <inheritdoc cref="SoundButton"/>
        public const string DataButton = "Data setting";

        /// <inheritdoc cref="SoundButton"/>
        public const string ResetButton = "Reset progress";

        /// <inheritdoc cref="SoundButton"/>
        public const string ConfirmButton = "Confirm reset";

        /// <inheritdoc cref="SoundButton"/>
        public const string CancelButton = "Cancel reset";

        /// <inheritdoc cref="SoundButton"/>
        public const string BackButton = "Settings back";

        /// <summary>The section headings, in order down the screen.</summary>
        public static readonly string[] Headings = { "AUDIO", "PRIVACY", "PROGRESS" };

        /// <summary>What each section says it does, beside its heading's index.</summary>
        public static readonly string[] Descriptions =
        {
            "Music and sound effects, together. N does the same from anywhere.",
            "Reports which levels people get stuck on. The README lists exactly what is sent.",
            "Forget every solved level, saved board, personal best and hint already shown, and " +
            "start again from the tutorial. Sound and data settings stay as they are.",
        };

        /// <summary>The question RESET PROGRESS asks.</summary>
        public const string Question = "Reset all progress? This cannot be undone.";

        /// <summary>What the panel says once the save has been emptied.</summary>
        public const string Done = "Progress reset. The tutorial is waiting on the first level.";

        private void Awake()
        {
            if (_progress == null) _progress = FindFirstObjectByType<ProgressTracker>();
            if (_menu == null) _menu = FindFirstObjectByType<MainMenu>();
            if (_audio == null) _audio = FindFirstObjectByType<GameAudio>();
            if (_canvas == null) _canvas = FindFirstObjectByType<Canvas>();
        }

        private void Start()
        {
            if (_canvas == null)
                return;

            Build();
            Show(false);
        }

        private void Update()
        {
            if (!IsShowing)
                return;

            Refresh();

            Keyboard keyboard = Keyboard.current;

            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame && !OpenedThisFrame)
            {
                if (Confirming)
                    Cancel();
                else
                    Back();
            }
        }

        // -----------------------------------------------------------------
        // Building
        // -----------------------------------------------------------------

        /// <summary>How wide the column of sections is.</summary>
        public const float ColumnWidth = 600f;

        /// <summary>The type a section's description is set in.</summary>
        public const UiType DescriptionType = UiType.Label;

        private const float TitleHeight = 56f;
        private const float TitleGap = 28f;
        private const float HeadingHeight = 24f;
        private const float HeadingRuleGap = 4f;
        private const float HeadingGap = 8f;
        private const float DescriptionGap = 12f;
        private const float SectionGap = 34f;
        private const float ButtonWidth = 240f;
        private const float AnswerWidth = 200f;

        /// <summary>
        /// The question takes exactly the reset button's height, so the answers start below where
        /// the button was. Shorter, and the answers came up under the pointer that had just pressed
        /// it -- a double-click would have reset.
        /// </summary>
        private const float QuestionHeight = UiTheme.ButtonHeight;

        private const float QuestionGap = 8f;
        private const float StatusHeight = 24f;
        private const float BackWidth = 120f;

        private void Build()
        {
            Image scrim = UiTheme.Scrim("Settings", _canvas.transform, Palette.Current.MenuScrim);
            Root = scrim.GetComponent<RectTransform>();
            UiTheme.Stretch(Root);

            // The sections are one block, centred as a whole. Its height is only known once the
            // descriptions have been measured, so the block is laid out first and placed after.
            RectTransform block = UiTheme.Rect("sections", Root);
            var column = new UiColumn();

            TextMeshProUGUI title = UiTheme.Label(
                "title", block, UiType.Title, UiTheme.Accent, TextAlignmentOptions.Center);
            Place(title.rectTransform, column.Take(TitleHeight, TitleGap), ColumnWidth, TitleHeight);
            title.text = "SETTINGS";

            Section(block, column, 0);
            Button sound = Control(block, column, SoundButton, out _soundLabel, ButtonRole.Secondary);
            sound.onClick.AddListener(() => Fire(ToggleSound));
            column.Space(SectionGap);

            Section(block, column, 1);
            Button data = Control(block, column, DataButton, out _dataLabel, ButtonRole.Secondary);
            data.onClick.AddListener(() => Fire(ToggleData));
            column.Space(SectionGap);

            Section(block, column, 2);
            BuildReset(block, column);

            UiTheme.Anchor(block, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(ColumnWidth, column.Next));

            // Where the level list keeps its CLOSE, so the two screens off the menu leave the same way.
            Button back = UiTheme.Button_(BackButton, Root, "BACK", out TextMeshProUGUI _, UiType.Label, ButtonRole.Quiet);
            UiTheme.Anchor(back.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-UiTheme.Margin, -UiTheme.Margin), new Vector2(BackWidth, UiTheme.ButtonHeight));
            back.onClick.AddListener(() => Fire(Back));

            TextMeshProUGUI help = UiTheme.Label(
                "help", Root, UiType.Caption, UiTheme.TextDim, TextAlignmentOptions.Center);
            UiTheme.Anchor(help.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 36f), new Vector2(600f, 20f));
            help.text = "escape to go back";
        }

        /// <summary>A heading with a rule under it, then the line saying what the section is for.</summary>
        private static void Section(RectTransform block, UiColumn column, int index)
        {
            float top = column.Take(HeadingHeight, HeadingRuleGap);

            // A size up from the level list's chapter headings: those label a run of rows, these
            // label a screen's worth of sections, and there are only three of them.
            TextMeshProUGUI heading = UiTheme.Label(
                "heading", block, UiType.Label, UiTheme.Text, TextAlignmentOptions.BottomLeft);
            heading.fontStyle = FontStyles.Bold;
            heading.characterSpacing = 6f;
            Place(heading.rectTransform, top, ColumnWidth, HeadingHeight);
            heading.text = Headings[index];

            // Two pixels: shorter than a panel's corners, so a plain hairline like the menu's.
            Image rule = UiTheme.Panel_("rule", block, Palette.Current.Rule);
            rule.sprite = null;
            Place(rule.rectTransform, column.Take(2f, HeadingGap), ColumnWidth, 2f);

            string text = Descriptions[index];
            float height = DescriptionHeight(text);

            TextMeshProUGUI description = UiTheme.Label(
                "description", block, DescriptionType, UiTheme.TextDim, TextAlignmentOptions.TopLeft);
            description.textWrappingMode = TextWrappingModes.Normal;
            Place(description.rectTransform, column.Take(height, DescriptionGap), ColumnWidth, height);
            description.text = text;
        }

        /// <summary>How tall a description is once wrapped to the column: measured, never counted.</summary>
        public static float DescriptionHeight(string text) =>
            Mathf.Ceil(UiTheme.TextHeight(text, DescriptionType, ColumnWidth));

        private static Button Control(
            RectTransform block, UiColumn column, string name, out TextMeshProUGUI label, ButtonRole role)
        {
            Button button = UiTheme.Button_(name, block, string.Empty, out label, UiType.Body, role);
            PlaceLeft(button.GetComponent<RectTransform>(), column.Take(UiTheme.ButtonHeight),
                0f, ButtonWidth, UiTheme.ButtonHeight);
            return button;
        }

        /// <summary>
        /// The reset button, and in the same place the question it asks and the two answers under it.
        /// </summary>
        private void BuildReset(RectTransform block, UiColumn column)
        {
            float top = column.Next;

            Button reset = UiTheme.Button_(ResetButton, block, "RESET PROGRESS", out TextMeshProUGUI _,
                UiType.Body, ButtonRole.Destructive);
            _resetButton = reset.GetComponent<RectTransform>();
            PlaceLeft(_resetButton, top, 0f, ButtonWidth, UiTheme.ButtonHeight);
            reset.onClick.AddListener(() => Fire(Ask));

            // The question and its answers, one group so they appear and go together.
            _question = UiTheme.Rect("question", block);
            float groupHeight = QuestionHeight + QuestionGap + UiTheme.ButtonHeight;
            Place(_question, top, ColumnWidth, groupHeight);

            TextMeshProUGUI question = UiTheme.Label(
                "text", _question, UiType.Body, UiTheme.Text, TextAlignmentOptions.MidlineLeft);
            PlaceLeft(question.rectTransform, 0f, 0f, ColumnWidth, QuestionHeight);
            question.text = Question;

            float answers = QuestionHeight + QuestionGap;

            // CANCEL first, where the eye lands, and quiet; the answer that destroys something is
            // the one further away.
            Button cancel = UiTheme.Button_(CancelButton, _question, "CANCEL", out TextMeshProUGUI _,
                UiType.Body, ButtonRole.Quiet);
            PlaceLeft(cancel.GetComponent<RectTransform>(), answers, 0f, AnswerWidth, UiTheme.ButtonHeight);
            cancel.onClick.AddListener(() => Fire(Cancel));

            Button confirm = UiTheme.Button_(ConfirmButton, _question, "YES, RESET", out TextMeshProUGUI _,
                UiType.Body, ButtonRole.Destructive);
            PlaceLeft(confirm.GetComponent<RectTransform>(), answers, AnswerWidth + UiTheme.Gap * 2f,
                AnswerWidth, UiTheme.ButtonHeight);
            confirm.onClick.AddListener(() => Fire(Confirm));

            column.Take(groupHeight, QuestionGap);

            _status = UiTheme.Label(
                "status", block, UiType.Label, UiTheme.Good, TextAlignmentOptions.MidlineLeft);
            Place(_status.rectTransform, column.Take(StatusHeight), ColumnWidth, StatusHeight);

            _question.gameObject.SetActive(false);
        }

        /// <summary>Across the column, <paramref name="top"/> below the block's top edge.</summary>
        private static void Place(RectTransform rect, float top, float width, float height) =>
            UiTheme.Anchor(rect, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -top), new Vector2(width, height));

        /// <summary>From the left edge of whatever holds it, <paramref name="left"/> in.</summary>
        private static void PlaceLeft(RectTransform rect, float top, float left, float width, float height) =>
            UiTheme.Anchor(rect, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(left, -top), new Vector2(width, height));

        // -----------------------------------------------------------------
        // Contents
        // -----------------------------------------------------------------

        /// <summary>
        /// Every frame the panel is up, because N mutes from anywhere and the button has to agree.
        /// The captions are constants, so this allocates nothing.
        /// </summary>
        private void Refresh()
        {
            _soundLabel.text = GameAudio.Muted ? "SOUND  OFF" : "SOUND  ON";
            _dataLabel.text = GameAnalytics.Reporting ? "DATA  ON" : "DATA  OFF";
        }

        private void ToggleSound()
        {
            if (_audio != null)
                _audio.ToggleMute();
        }

        private static void ToggleData() => GameAnalytics.SetReporting(!GameAnalytics.Reporting);

        // -----------------------------------------------------------------
        // Starting over
        // -----------------------------------------------------------------

        private void Ask()
        {
            SetConfirming(true);
            _status.text = string.Empty;
        }

        private void Cancel() => SetConfirming(false);

        private void Confirm()
        {
            if (!Confirming)
                return;

            SetConfirming(false);

            bool reset = _progress != null && _progress.ResetProgress();
            _status.text = reset ? Done : "Nothing to reset.";
        }

        private void SetConfirming(bool confirming)
        {
            Confirming = confirming;
            _resetButton.gameObject.SetActive(!confirming);
            _question.gameObject.SetActive(confirming);
        }

        // -----------------------------------------------------------------
        // Showing
        // -----------------------------------------------------------------

        /// <summary>Opens the settings. Used by the main menu's Settings item.</summary>
        public void Open() => Show(true);

        /// <summary>Back to the main menu, which is the only way here.</summary>
        private void Back()
        {
            Show(false);

            if (_menu != null)
                _menu.Show(true);
        }

        private void Show(bool visible) => SetShowing(visible);

        /// <summary>
        /// Every visit starts with no question pending and nothing reported, so a reset reported
        /// last time is not mistaken for one just made.
        /// </summary>
        protected override void OnShown()
        {
            if (_resetButton == null)
                return;

            SetConfirming(false);
            _status.text = string.Empty;
            Refresh();
        }

        private static void Fire(System.Action action)
        {
            action();
            UiTheme.Defocus();
        }
    }
}
