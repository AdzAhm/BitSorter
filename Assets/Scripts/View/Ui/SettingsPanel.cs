using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;

namespace BitSorter.View
{
    /// <summary>
    /// The settings: sound and its volume, fullscreen on a desktop build, reporting, and starting
    /// over. Reached from the main menu, and goes back to it.
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
        [SerializeField] private CreditsPanel _credits;

        [Tooltip("Canvas the panel is built under. Found by type when left empty.")]
        [SerializeField] private Canvas _canvas;

        private GameAudio _audio;

        private TextMeshProUGUI _soundLabel;
        private TextMeshProUGUI _dataLabel;
        private TextMeshProUGUI _fullscreenLabel;
        private Slider _volume;
        private TextMeshProUGUI _volumeLabel;
        private TextMeshProUGUI _volumeValue;

        /// <summary>The volume the readout last said, so it is rewritten only when it changes.</summary>
        private int _shownVolume = -1;
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
        public const string VolumeSlider = "Volume setting";

        /// <inheritdoc cref="SoundButton"/>
        public const string FullscreenButton = "Fullscreen setting";

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

        /// <inheritdoc cref="SoundButton"/>
        public const string CreditsButton = "Credits";

        public const string AudioHeading = "AUDIO";
        public const string DisplayHeading = "DISPLAY";
        public const string PrivacyHeading = "PRIVACY";
        public const string ProgressHeading = "PROGRESS";
        public const string AboutHeading = "ABOUT";

        /// <summary>The section headings this build shows, in order down the screen.</summary>
        /// <remarks>DISPLAY only where <see cref="DisplayRules.Offered"/> says the switch works.</remarks>
        public static IReadOnlyList<string> Headings => DisplayRules.Offered ? WithDisplay : WithoutDisplay;

        private static readonly string[] WithDisplay =
            { AudioHeading, DisplayHeading, PrivacyHeading, ProgressHeading, AboutHeading };

        private static readonly string[] WithoutDisplay =
            { AudioHeading, PrivacyHeading, ProgressHeading, AboutHeading };

        public const string AudioText =
            "Music and sound effects, together. N switches the sound on and off from anywhere.";

        public const string DisplayText = "Fill the screen, or play in a window. Alt+Enter does the same.";

        public const string PrivacyText =
            "Reports which levels people get stuck on. The README lists exactly what is sent.";

        /// <remarks>Names only the settings this build has: a browser build has no DISPLAY section.</remarks>
        public static string ProgressText =>
            "Forget every solved level, saved board, personal best and hint already shown, and " +
            "start again from the tutorial. " +
            (DisplayRules.Offered ? "Sound, display and data settings" : "Sound and data settings") +
            " stay as they are.";

        /// <summary>The question RESET PROGRESS asks.</summary>
        public const string Question = "Reset all progress? This cannot be undone.";

        /// <summary>What the panel says once the save has been emptied.</summary>
        public const string Done = "Progress reset. The tutorial is waiting on the first level.";

        private void Awake()
        {
            if (_progress == null) _progress = FindFirstObjectByType<ProgressTracker>();
            if (_menu == null) _menu = FindFirstObjectByType<MainMenu>();
            if (_credits == null) _credits = FindFirstObjectByType<CreditsPanel>();
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

            Fit();
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

        /// <summary>
        /// Room kept clear above and below the sections: the BACK button at the top, the help line
        /// at the bottom.
        /// </summary>
        public const float FitMargin = 70f;

        private RectTransform _block;
        private float _blockHeight;

        /// <summary>
        /// The scale the sections are drawn at: whole, unless the window is too short for them.
        /// </summary>
        /// <remarks>
        /// The canvas scales halfway between the screen's width and its height, so a wide or short
        /// window has less height to give than the 1080 the sections are laid out against -- a
        /// browser tab 1920 by 800 has about 930. Settings has no scroll, and past that height it
        /// would run under the BACK button and off the bottom, so it shrinks to fit instead.
        /// </remarks>
        public static float FitScale(float room, float needed) =>
            needed <= 0f || room <= 0f || room >= needed ? 1f : room / needed;

        /// <summary>The scale the sections are drawn at now, for the tests.</summary>
        public float SectionsScale => _block != null ? _block.localScale.x : 1f;

        private void Fit()
        {
            if (_block == null || Root == null)
                return;

            float scale = FitScale(Root.rect.height - 2f * FitMargin, _blockHeight);

            if (!Mathf.Approximately(_block.localScale.x, scale))
                _block.localScale = new Vector3(scale, scale, 1f);
        }

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

        /// <summary>The volume row: as tall as a stepper needs to be to hit, with a caption each side.</summary>
        private const float VolumeHeight = 32f;
        private const float VolumeGap = 10f;
        private const float VolumeLabelWidth = 110f;
        private const float VolumeSliderWidth = 300f;
        private const float VolumeValueWidth = 70f;

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

            Section(block, column, AudioHeading, AudioText);
            Button sound = Control(block, column, SoundButton, out _soundLabel, ButtonRole.Secondary);
            sound.onClick.AddListener(() => Fire(ToggleSound));
            column.Space(VolumeGap);
            BuildVolume(block, column);
            column.Space(SectionGap);

            if (DisplayRules.Offered)
            {
                Section(block, column, DisplayHeading, DisplayText);
                Button fullscreen = Control(block, column, FullscreenButton, out _fullscreenLabel, ButtonRole.Secondary);
                fullscreen.onClick.AddListener(() => Fire(ToggleFullscreen));
                column.Space(SectionGap);
            }

            Section(block, column, PrivacyHeading, PrivacyText);
            Button data = Control(block, column, DataButton, out _dataLabel, ButtonRole.Secondary);
            data.onClick.AddListener(() => Fire(ToggleData));
            column.Space(SectionGap);

            Section(block, column, ProgressHeading, ProgressText);
            BuildReset(block, column);
            column.Space(SectionGap);

            // Last, below everything that changes how the game behaves.
            Section(block, column, AboutHeading, null);
            Button credits = Control(block, column, CreditsButton, out TextMeshProUGUI creditsLabel, ButtonRole.Quiet);
            creditsLabel.text = "CREDITS";
            credits.onClick.AddListener(() => Fire(OpenCredits));

            UiTheme.Anchor(block, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(ColumnWidth, column.Next));

            _block = block;
            _blockHeight = column.Next;

            // Where the level list keeps its CLOSE, so the two screens off the menu leave the same way.
            Button back = UiTheme.Button_(BackButton, Root, "BACK", out TextMeshProUGUI _, UiType.Body, ButtonRole.Quiet);
            UiTheme.Anchor(back.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-UiTheme.Margin, -UiTheme.Margin), new Vector2(BackWidth, UiTheme.ButtonHeight));
            back.onClick.AddListener(() => Fire(Back));

            TextMeshProUGUI help = UiTheme.Label(
                "help", Root, UiTheme.HelpLineType, UiTheme.TextDim, TextAlignmentOptions.Center);
            UiTheme.Anchor(help.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 36f), new Vector2(600f, UiTheme.HelpLineHeight));
            help.text = "escape to go back";
        }

        /// <summary>A heading with a rule under it, then the line saying what the section is for.</summary>
        private static void Section(RectTransform block, UiColumn column, string title, string text)
        {
            float top = column.Take(HeadingHeight, HeadingRuleGap);

            // A size up from the level list's chapter headings: those label a run of rows, these
            // label a screen's worth of sections, and there are only a handful of them.
            TextMeshProUGUI heading = UiTheme.Label(
                "heading", block, UiType.Label, UiTheme.Text, TextAlignmentOptions.BottomLeft);
            heading.fontStyle = FontStyles.Bold;
            heading.characterSpacing = 6f;
            Place(heading.rectTransform, top, ColumnWidth, HeadingHeight);
            heading.text = title;

            // Two pixels: shorter than a panel's corners, so a plain hairline like the menu's.
            Image rule = UiTheme.Panel_("rule", block, Palette.Current.Rule);
            rule.sprite = null;
            Place(rule.rectTransform, column.Take(2f, HeadingGap), ColumnWidth, 2f);

            // A section can be a heading over a single button that says what it does.
            if (string.IsNullOrEmpty(text))
                return;

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
        /// The volume, under the switch it depends on: a caption, the slider, and what it is set to.
        /// </summary>
        /// <remarks>
        /// Applied at every step of a drag, so the player hears it change, and written out once when
        /// the drag lets go. Greyed out and locked while the sound is off, because a volume for
        /// silence is a control that visibly does nothing -- see <see cref="Refresh"/>.
        /// </remarks>
        private void BuildVolume(RectTransform block, UiColumn column)
        {
            float top = column.Take(VolumeHeight);

            _volumeLabel = UiTheme.Label(
                "volume label", block, UiType.Label, UiTheme.Text, TextAlignmentOptions.MidlineLeft);
            PlaceLeft(_volumeLabel.rectTransform, top, 0f, VolumeLabelWidth, VolumeHeight);
            _volumeLabel.text = "VOLUME";

            _volume = UiTheme.Slider_(VolumeSlider, block, 0, 100);
            PlaceLeft(_volume.GetComponent<RectTransform>(), top, VolumeLabelWidth, VolumeSliderWidth, VolumeHeight);
            _volume.SetValueWithoutNotify(GameAudio.Volume);
            _volume.onValueChanged.AddListener(OnVolume);
            _volume.gameObject.AddComponent<PointerRelease>().Released += GameAudio.KeepVolume;

            _volumeValue = UiTheme.Label(
                "volume value", block, UiType.Label, UiTheme.Text, TextAlignmentOptions.MidlineRight);
            PlaceLeft(_volumeValue.rectTransform, top, VolumeLabelWidth + VolumeSliderWidth,
                VolumeValueWidth, VolumeHeight);
        }

        private void OnVolume(float value)
        {
            if (_audio != null)
                _audio.SetVolume(Mathf.RoundToInt(value), keep: false);
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
            bool sound = !GameAudio.Muted;

            _soundLabel.text = sound ? "SOUND  ON" : "SOUND  OFF";
            _dataLabel.text = GameAnalytics.Reporting ? "DATA  ON" : "DATA  OFF";

            if (_fullscreenLabel != null)
                _fullscreenLabel.text = Screen.fullScreen ? "FULLSCREEN  ON" : "FULLSCREEN  OFF";

            // Greyed out and locked with the sound off, and live again the moment it is back on --
            // N included, which is why this is asked every frame rather than on the button's click.
            UiTheme.SetEnabled(_volume, sound);
            _volumeLabel.color = sound ? UiTheme.Text : UiTheme.TextDim;
            _volumeValue.color = sound ? UiTheme.Text : UiTheme.TextDim;

            int volume = GameAudio.Volume;

            if (volume != _shownVolume)
            {
                _shownVolume = volume;
                _volumeValue.text = volume + "%";

                if (Mathf.RoundToInt(_volume.value) != volume)
                    _volume.SetValueWithoutNotify(volume);
            }
        }

        private void ToggleSound()
        {
            if (_audio != null)
                _audio.ToggleMute();
        }

        private static void ToggleData() => GameAnalytics.SetReporting(!GameAnalytics.Reporting);

        /// <summary>
        /// Fullscreen at the display's own size, or back to a window the size the game opens at.
        /// </summary>
        /// <remarks>
        /// Unity remembers the choice for the next launch itself. Changing the mode alone kept the
        /// fullscreen resolution on the way out -- a window the size of the display, title bar off
        /// the top -- so the size is always given with it (<see cref="DisplayRules.WindowedSize"/>).
        ///
        /// Measured on the display the window is on, not the main one: on a second monitor of a
        /// different size, the main display's size filled that monitor wrongly.
        /// </remarks>
        private static void ToggleFullscreen()
        {
            DisplayInfo display = Screen.mainWindowDisplayInfo;
            int width = display.width > 0 ? display.width : Display.main.systemWidth;
            int height = display.height > 0 ? display.height : Display.main.systemHeight;

            if (Screen.fullScreen)
            {
                Vector2Int window = DisplayRules.WindowedSize(width, height);
                Screen.SetResolution(window.x, window.y, FullScreenMode.Windowed);
            }
            else
            {
                Screen.SetResolution(width, height, FullScreenMode.FullScreenWindow);
            }
        }

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

        /// <summary>The credits roll, which comes back here.</summary>
        private void OpenCredits()
        {
            if (_credits == null)
                return;

            Show(false);
            _credits.Open();
        }

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
            _shownVolume = -1;
            Fit();
            Refresh();
        }

        /// <summary>
        /// A drag keeps its volume when it lets go; this keeps one that was somehow left staged.
        /// </summary>
        protected override void OnHidden() => GameAudio.KeepVolume();

        private static void Fire(System.Action action)
        {
            action();
            UiTheme.Defocus();
        }
    }
}
