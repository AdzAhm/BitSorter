using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using ShellButton = UnityEngine.UIElements.Button;

namespace BitSorter.View
{
    /// <summary>
    /// The settings: sound and its volume; fullscreen, vertical sync, a frame cap and a frame-rate counter on a desktop
    /// build; the look; reporting; starting over; the credits and the 4.x game. A page of the workbench's own since Part
    /// 15g, over what is open and under the header, in the style's own colours -- it was a uGUI screen over everything, in
    /// the 4.x board's colours whatever the style, and Ahmad asked that it look like the start page and that a change of
    /// style through it change everything (2026-10-28). BACK and Escape take it down again.
    /// </summary>
    /// <remarks>
    /// A section is a heading, a line saying what the setting does, and the control. Sound and data used to be rows of the
    /// main menu itself, which had room for a caption and nothing else -- the data switch could say ON or OFF but not what
    /// it was switching. Starting over could not be a menu row at all: it needs a sentence saying what goes, and a question
    /// before it goes.
    ///
    /// **Resetting asks first, and the question cannot be answered by accident.** Pressing RESET PROGRESS puts the
    /// question where the button was and the two answers under it, so a double-click lands on the question, which is not a
    /// button. Enter answers nothing; Escape backs out of the question before it backs out of the page.
    ///
    /// The page scrolls in a short window. As a uGUI screen it had no scroll and shrank to fit instead (<see cref="FitScale"/>,
    /// which the help panel and free play's setup still use).
    /// </remarks>
    public sealed class SettingsPanel : MonoBehaviour, IHoldsEscape
    {
        [SerializeField] private ProgressTracker _progress;
        [SerializeField] private MainMenu _menu;
        [SerializeField] private CreditsPanel _credits;
        [SerializeField] private WorkbenchPanel _workbench;

        private GameAudio _audio;
        private VisualElement _page;
        private ShellButton _sound, _music, _effects, _data, _fullscreen, _vSync, _counter, _motion, _keys, _reset;
        private ShellSlider _volume, _cap;
        private Label _volumeLabel, _volumeValue, _capLabel, _capValue, _status;
        private VisualElement _question;
        private SchematicPalette _palette;

        /// <summary>The volume the readout last said, so it is rewritten only when it changes.</summary>
        private int _shownVolume = -1;

        /// <summary>The cap's stop the readout last said, for the same reason.</summary>
        private int _shownCap = -1;

        /// <summary>The frames it last came up and went down in: neither answers that frame's keys twice.</summary>
        private int _openedFrame = -1, _closedFrame = -1;

        /// <summary>Whether the page is up.</summary>
        public bool IsShowing => _page != null && _page.style.display == DisplayStyle.Flex;

        /// <inheritdoc cref="IsShowing"/>
        public bool IsOpen => IsShowing;

        /// <summary>Whether RESET PROGRESS has been pressed and is waiting for an answer.</summary>
        public bool Confirming { get; private set; }

        /// <summary>The page's name in the shell.</summary>
        public const string PageName = "Workbench settings page";

        /// <summary>The controls' names in the shell, which the tests look for.</summary>
        public const string SoundButton = "Sound setting";

        /// <inheritdoc cref="SoundButton"/>
        public const string MusicButton = "Music setting";

        /// <inheritdoc cref="SoundButton"/>
        public const string EffectsButton = "Effects setting";

        /// <inheritdoc cref="SoundButton"/>
        public const string VolumeSlider = "Volume setting";

        /// <inheritdoc cref="SoundButton"/>
        public const string FullscreenButton = "Fullscreen setting";

        /// <inheritdoc cref="SoundButton"/>
        public const string VSyncButton = "VSync setting";

        /// <inheritdoc cref="SoundButton"/>
        public const string FrameCapSlider = "Frame cap setting";

        /// <inheritdoc cref="SoundButton"/>
        public const string CounterButton = "FPS counter setting";

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

        /// <inheritdoc cref="SoundButton"/>
        public const string GameButton = "The 4.x game";

        /// <inheritdoc cref="SoundButton"/>
        public const string MotionButton = "Motion setting";

        /// <inheritdoc cref="SoundButton"/>
        public const string KeysButton = "Keys setting";

        /// <summary>The line RESET PROGRESS's answer is said in, and the question's.</summary>
        public const string StatusName = "Settings status", QuestionName = "Settings question";

        /// <summary>What SHORTCUT KEYS says for its setting.</summary>
        public static string KeysCaption(bool shown) => shown ? "SHORTCUT KEYS  SHOWN" : "SHORTCUT KEYS  HIDDEN";

        /// <summary>The name of the button that chooses a look.</summary>
        public static string LookButton(Look look) => "Look " + look.Name;

        public const string AudioHeading = "AUDIO";
        public const string DisplayHeading = "DISPLAY";
        public const string StyleHeading = "STYLE";
        public const string PrivacyHeading = "PRIVACY";
        public const string ProgressHeading = "PROGRESS";
        public const string AboutHeading = "ABOUT";

        /// <summary>The section headings, in order down the page.</summary>
        /// <remarks>
        /// DISPLAY in every build: a browser has the FPS counter, and only the counter (<see cref="DisplaySwitches"/>). It
        /// used to leave the section out there altogether.
        /// </remarks>
        public static IReadOnlyList<string> Headings => Sections;

        private static readonly string[] Sections =
            { AudioHeading, DisplayHeading, StyleHeading, PrivacyHeading, ProgressHeading, AboutHeading };

        /// <summary>
        /// The switches DISPLAY holds, left to right: on a desktop build fullscreen, vertical sync and the counter, and in a
        /// browser the counter alone.
        /// </summary>
        /// <remarks>
        /// A browser keeps fullscreen and the frame rate to itself (<see cref="DisplayRules.Offered"/>), so neither switch
        /// would do anything there; the counter only reads what the frame rate is, which is worth seeing anywhere. Asked for
        /// after the 4.0.0 browser playtest (2026-10-05). A question of the build rather than of the editor, so the build
        /// passes <see cref="DisplayRules.Offered"/> and a test can ask for either.
        /// </remarks>
        public static IReadOnlyList<string> DisplaySwitches(bool desktop) => desktop ? DesktopSwitches : BrowserSwitches;

        private static readonly string[] DesktopSwitches = { FullscreenButton, VSyncButton, CounterButton };

        private static readonly string[] BrowserSwitches = { CounterButton };

        /// <summary>DISPLAY's line, for a desktop build or a browser.</summary>
        public static string DisplayTextFor(bool desktop) => desktop ? DisplayText : BrowserDisplayText;

        /// <remarks>
        /// Each section says only what its buttons cannot: they already say what they switch, and the screen once read as
        /// a wall of words (2026-09-30).
        /// </remarks>
        public const string AudioText = "N switches the sound from anywhere.";

        public const string DisplayText =
            "Alt+Enter also switches fullscreen. With VSYNC off, a lower frame cap saves power.";

        /// <summary>DISPLAY's line in a browser, where the counter is the section's one switch.</summary>
        public const string BrowserDisplayText =
            "The browser sets the frame rate here. The counter shows what it is.";

        /// <summary>STYLE's line: what a look changes, that choosing one loads the game again, and what reduced motion leaves out.</summary>
        public const string StyleText =
            "How the workbench is drawn; the game loads again to switch. Reduced motion drops its glow and flashes.";

        public const string PrivacyText =
            "Reports which exercises people get stuck on. The README lists exactly what is sent.";

        /// <summary>What RESET PROGRESS forgets, and what it keeps (Part 15g: it emptied the whole save, circuits included).</summary>
        public const string ProgressText =
            "Start the exercises over: every solved exercise, its saved answer and best, the tutorial and the " +
            "first-time hints are forgotten. Your circuits and settings are kept.";

        /// <summary>The question RESET PROGRESS asks.</summary>
        public const string Question = "Start the exercises over? This cannot be undone.";

        /// <summary>What the page says once the exercises have been forgotten.</summary>
        public const string Done = "The exercises start over. Your circuits are kept.";

        /// <summary>The line at the page's foot saying how to leave it.</summary>
        public const string HelpText = "escape to go back";

        private void Awake()
        {
            if (_progress == null) _progress = FindFirstObjectByType<ProgressTracker>();
            if (_menu == null) _menu = FindFirstObjectByType<MainMenu>();
            if (_credits == null) _credits = FindFirstObjectByType<CreditsPanel>();
            if (_audio == null) _audio = FindFirstObjectByType<GameAudio>();
            if (_workbench == null) _workbench = FindFirstObjectByType<WorkbenchPanel>();
            UiEscape.Join(this);
        }

        private void OnDestroy() => UiEscape.Leave(this);

        /// <inheritdoc/>
        public bool HoldsEscapeNow => IsShowing || _closedFrame == Time.frameCount;

        private void Update()
        {
            if (!IsShowing)
                return;

            Refresh();

            Keyboard keyboard = UiText.Keyboard;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame && _openedFrame != Time.frameCount && UiEscape.TryTake(this))
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
        /// The scale a panel <paramref name="needed"/> tall is drawn at in <paramref name="room"/>: whole, unless the window
        /// is too short for it. The help panel and free play's setup shrink to fit by it; Settings scrolls since Part 15g.
        /// </summary>
        public static float FitScale(float room, float needed) =>
            needed <= 0f || room <= 0f || room >= needed ? 1f : room / needed;

        /// <summary>How wide the column of sections is.</summary>
        public const float ColumnWidth = 600f;

        /// <summary>The room between the two columns.</summary>
        public const float ColumnGap = 80f;

        private const float SwitchWidth = 190f, SwitchGap = 15f, ControlHeight = 44f, ButtonWidth = 240f, AnswerWidth = 200f;
        private const float LookGap = 12f, LookWidth = (ColumnWidth - LookGap) / 2f;
        private const float SliderLabelWidth = 110f, SliderWidth = 300f, SliderHeight = 32f, ReadoutGap = 20f;
        private const float SectionGap = 26f, RowGap = 10f;

        private void Build()
        {
            _page = _workbench != null ? _workbench.AddPage(PageName, () => Show(false)) : null;
            if (_page == null)
                return;
            _palette = SchematicPalette.Current;

            var scroll = new ScrollView(ScrollViewMode.Vertical) { name = "settings scroll" };
            scroll.style.position = Position.Absolute;
            scroll.style.left = scroll.style.right = scroll.style.top = scroll.style.bottom = 0f;
            scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            // The default theme clips a scroll view's viewport, and the shell has no theme: scrolled down, the sections
            // above the page were drawn over the workbench's header.
            scroll.contentViewport.style.overflow = Overflow.Hidden;
            _page.Add(scroll);

            // Two columns under the title (Part 15h): what the game sounds and looks like on the left, and what it keeps and
            // where it came from on the right. One column ran past the foot of a 1080 window, and CREDITS, last, was found
            // only by scrolling (Ahmad, 2026-10-28).
            var page = new VisualElement { name = "sections", pickingMode = PickingMode.Ignore };
            page.style.alignSelf = Align.Center;
            page.style.paddingTop = 28f;
            page.style.paddingBottom = 28f;
            page.style.flexShrink = 0f;
            scroll.Add(page);

            Label title = ShellTheme.Label("title", "SETTINGS", UiType.Title, _palette.Accent, TextAnchor.MiddleCenter, bold: true);
            title.style.marginBottom = 24f;
            title.style.flexShrink = 0f;
            page.Add(title);

            var columns = new VisualElement { name = "columns", pickingMode = PickingMode.Ignore };
            columns.style.flexDirection = FlexDirection.Row;
            columns.style.alignItems = Align.FlexStart;
            columns.style.flexShrink = 0f;
            page.Add(columns);
            VisualElement column = Column(columns), right = Column(columns);
            right.style.marginLeft = ColumnGap;

            Section(column, AudioHeading, AudioText);
            VisualElement sound = Row(column);
            _sound = Switch(sound, SoundButton, ToggleSound);
            _music = Switch(sound, MusicButton, ToggleMusic);
            _effects = Switch(sound, EffectsButton, ToggleEffects);
            _volume = Slider(column, VolumeSlider, "VOLUME", 0, 100, out _volumeLabel, out _volumeValue);
            _volume.SetValueWithoutNotify(GameAudio.Volume);
            _volume.Changed += value => { if (_audio != null) _audio.SetVolume(Mathf.RoundToInt(value), keep: false); };
            _volume.Released += GameAudio.KeepVolume;
            Gap(column);

            bool desktop = DisplayRules.Offered;
            Section(column, DisplayHeading, DisplayTextFor(desktop));
            VisualElement display = Row(column);
            foreach (string name in DisplaySwitches(desktop))
            {
                switch (name)
                {
                    case FullscreenButton: _fullscreen = Switch(display, FullscreenButton, ToggleFullscreen); break;
                    case VSyncButton: _vSync = Switch(display, VSyncButton, ToggleVSync); break;
                    case CounterButton: _counter = Switch(display, CounterButton, ToggleCounter); break;
                }
            }

            // The cap under the switch it depends on, as the volume sits under the sound's, and only where there is a
            // vertical sync for it to depend on.
            if (desktop)
            {
                _cap = Slider(column, FrameCapSlider, "FRAME CAP", 0, FrameRate.Stops - 1, out _capLabel, out _capValue);
                _cap.SetValueWithoutNotify(FrameRate.CapStop);
                _cap.Changed += value => FrameRate.SetCapStop(Mathf.RoundToInt(value), keep: false);
                _cap.Released += FrameRate.KeepCap;
            }
            Gap(column);

            BuildStyle(column);
            Gap(column);

            Section(right, PrivacyHeading, PrivacyText);
            _data = Control(Row(right), DataButton, "", ToggleData, ButtonWidth);
            Gap(right);

            Section(right, ProgressHeading, ProgressText);
            BuildReset(right);
            Gap(right);

            // Last, below everything that changes how the game behaves. The 4.x game, kept until Part 16 deletes it, beside
            // CREDITS and as quiet (Ahmad, 2026-10-27: "not deep but not plainly").
            Section(right, AboutHeading, null);
            VisualElement about = Row(right);
            Control(about, CreditsButton, "CREDITS", OpenCredits, ButtonWidth).style.marginRight = LookGap;
            Control(about, GameButton, "THE 4.x GAME", OpenTheGame, ButtonWidth);

            Label help = ShellTheme.Label("help", HelpText, UiType.Caption, _palette.Muted, TextAnchor.MiddleCenter);
            help.style.marginTop = 20f;
            help.style.flexShrink = 0f;
            page.Add(help);

            // Nothing in a column gives up its height to fit the page: the page scrolls instead. UI Toolkit shrinks a
            // column's children by default, and every row came out ten units short, the volume over the sound's switches.
            foreach (VisualElement child in column.Children())
                child.style.flexShrink = 0f;
            foreach (VisualElement child in right.Children())
                child.style.flexShrink = 0f;
            foreach (VisualElement child in _question.Children())
                child.style.flexShrink = 0f;

            // Where the start page has nothing, at the page's top right: the way back.
            ShellButton back = ShellTheme.Button(BackButton, "BACK", _palette, Back);
            back.style.position = Position.Absolute;
            back.style.top = 16f;
            back.style.right = 24f;
            back.style.width = 120f;
            back.style.height = ControlHeight;
            _page.Add(back);
        }

        /// <summary>
        /// STYLE: a button for every look, two to a row, the one in use chosen; then reduced motion and the shortcut keys.
        /// Choosing a look loads the game again in it (<see cref="LookSwitch"/>), so its buttons are coloured once, as they
        /// are built.
        /// </summary>
        private void BuildStyle(VisualElement column)
        {
            Section(column, StyleHeading, StyleText);
            IReadOnlyList<Look> looks = Look.All;
            VisualElement row = null;
            for (int i = 0; i < looks.Count; i++)
            {
                if (i % 2 == 0)
                    row = Row(column);
                Look look = looks[i];
                ShellButton button = Control(row, LookButton(look), look.Title, () => LookSwitch.To(look), LookWidth);
                if (i % 2 == 0)
                    button.style.marginRight = LookGap;
                if (look == Look.Current)
                {
                    ShellTheme.Outline(button, _palette.Accent, _palette.Ground);
                    button.style.color = _palette.Accent;
                }
            }

            VisualElement switches = Row(column);
            _motion = Control(switches, MotionButton, "", () => ReducedMotion.Set(!ReducedMotion.IsOn), LookWidth);
            _motion.style.marginRight = LookGap;
            _keys = Control(switches, KeysButton, "", () => KeyCaps.SetShown(!KeyCaps.Shown), LookWidth);
        }

        /// <summary>The reset button, and in its place once pressed the question it asks and the two answers under it.</summary>
        private void BuildReset(VisualElement column)
        {
            VisualElement row = Row(column);
            _reset = Control(row, ResetButton, "RESET PROGRESS", Ask, ButtonWidth);
            Destructive(_reset);

            // The question takes exactly the button's height, so the answers start below where the button was. Shorter,
            // and the answers came up under the pointer that had just pressed it -- a double-click would have reset.
            _question = new VisualElement { name = QuestionName, pickingMode = PickingMode.Ignore };
            Label text = ShellTheme.Label("text", Question, UiType.Body, _palette.Text);
            text.style.height = ControlHeight;
            _question.Add(text);
            VisualElement answers = Row(_question);
            Control(answers, CancelButton, "CANCEL", Cancel, AnswerWidth).style.marginRight = LookGap * 2f;
            Destructive(Control(answers, ConfirmButton, "YES, RESET", Confirm, AnswerWidth));
            _question.style.display = DisplayStyle.None;
            column.Add(_question);

            _status = ShellTheme.Label(StatusName, "", UiType.Body, _palette.Accent);
            _status.style.height = 24f;
            column.Add(_status);
        }

        /// <summary>A column of sections, the column's width, side by side with the other.</summary>
        private static VisualElement Column(VisualElement columns)
        {
            var column = new VisualElement { name = "column", pickingMode = PickingMode.Ignore };
            column.style.width = ColumnWidth;
            column.style.flexShrink = 0f;
            columns.Add(column);
            return column;
        }

        /// <summary>A heading with a rule under it, then the line saying what the section is for.</summary>
        private void Section(VisualElement column, string heading, string text)
        {
            Label label = ShellTheme.Label("heading", heading, UiType.Label, _palette.Text, TextAnchor.LowerLeft, bold: true);
            label.style.letterSpacing = 6f;
            column.Add(label);

            var rule = new VisualElement { name = "rule", pickingMode = PickingMode.Ignore };
            rule.style.height = 1f;
            rule.style.marginTop = 4f;
            rule.style.marginBottom = 8f;
            rule.style.backgroundColor = _palette.Outline;
            column.Add(rule);

            // A section can be a heading over a single button that says what it does.
            if (string.IsNullOrEmpty(text))
                return;
            Label description = ShellTheme.Label("description", text, UiType.Body, _palette.Text, TextAnchor.UpperLeft);
            description.style.whiteSpace = WhiteSpace.Normal;
            description.style.marginBottom = 12f;
            column.Add(description);
        }

        private static VisualElement Row(VisualElement column)
        {
            var row = new VisualElement { name = "row", pickingMode = PickingMode.Ignore };
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginBottom = RowGap;
            column.Add(row);
            return row;
        }

        private static void Gap(VisualElement column)
        {
            var gap = new VisualElement { name = "gap", pickingMode = PickingMode.Ignore };
            gap.style.height = SectionGap - RowGap;
            column.Add(gap);
        }

        private ShellButton Control(VisualElement row, string name, string caption, System.Action clicked, float width)
        {
            ShellButton button = ShellTheme.Button(name, caption, _palette, clicked);
            button.style.width = width;
            button.style.height = ControlHeight;
            button.style.marginLeft = button.style.marginTop = button.style.marginBottom = 0f;
            row.Add(button);
            return button;
        }

        private ShellButton Switch(VisualElement row, string name, System.Action clicked)
        {
            ShellButton button = Control(row, name, "", clicked, SwitchWidth);
            button.style.marginRight = SwitchGap;
            return button;
        }

        /// <summary>A button that destroys something, in the colour the waves draw an x in.</summary>
        private void Destructive(ShellButton button)
        {
            ShellTheme.Outline(button, _palette.Unknown, _palette.Ground);
            button.style.color = _palette.Unknown;
        }

        /// <summary>
        /// A slider row: its caption, the slider, and what it is set to, the same for the volume and the frame cap so the
        /// two read as one column: "100%" over "60 FPS". Applied at every step of a drag, and written out once it lets go.
        /// </summary>
        private ShellSlider Slider(VisualElement column, string name, string caption, int low, int high, out Label label, out Label value)
        {
            VisualElement row = Row(column);
            row.style.alignItems = Align.Center;
            label = ShellTheme.Label("caption", caption, UiType.Body, _palette.Text);
            label.style.width = SliderLabelWidth;
            row.Add(label);

            ShellSlider slider = ShellTheme.Slider(name, _palette);
            slider.style.width = SliderWidth;
            slider.style.height = SliderHeight;
            slider.lowValue = low;
            slider.highValue = high;
            slider.wholeNumbers = true;   // a percent or a stop, never between two
            row.Add(slider);

            value = ShellTheme.Label("value", "", UiType.Body, _palette.Text);
            value.style.marginLeft = ReadoutGap;
            row.Add(value);
            return slider;
        }

        // -----------------------------------------------------------------
        // Contents
        // -----------------------------------------------------------------

        /// <summary>
        /// Every frame the page is up, because N mutes from anywhere and the button has to agree. The captions are
        /// constants, and a text the same as it was is not set again.
        /// </summary>
        private void Refresh()
        {
            bool sound = !GameAudio.Muted;

            Caption(_sound, sound ? "SOUND  ON" : "SOUND  OFF");
            Caption(_music, GameAudio.MusicOn ? "MUSIC  ON" : "MUSIC  OFF");
            Caption(_effects, GameAudio.EffectsOn ? "EFFECTS  ON" : "EFFECTS  OFF");

            // Each keeps saying what it is set to while greyed out, so turning the sound back on brings back exactly what
            // the player had.
            Enable(_music, sound);
            Enable(_effects, sound);
            Caption(_data, GameAnalytics.Reporting ? "DATA  ON" : "DATA  OFF");
            Caption(_motion, ReducedMotion.IsOn ? "REDUCED MOTION  ON" : "REDUCED MOTION  OFF");
            Caption(_keys, KeysCaption(KeyCaps.Shown));
            if (_fullscreen != null)
                Caption(_fullscreen, Screen.fullScreen ? "FULLSCREEN  ON" : "FULLSCREEN  OFF");

            // On its own, because a browser has the counter and not the vertical sync it sits beside.
            if (_counter != null)
                Caption(_counter, CounterCaption(FrameRate.ShowsCounter));
            if (_vSync != null)
                RefreshFrameRate();

            // Greyed out and locked with the sound off, and live again the moment it is back on -- N included, which is
            // why this is asked every frame rather than on the button's click.
            Lock(_volume, _volumeLabel, _volumeValue, !sound);
            int volume = GameAudio.Volume;
            if (volume != _shownVolume)
            {
                _shownVolume = volume;
                _volumeValue.text = volume + "%";
                if (Mathf.RoundToInt(_volume.value) != volume)
                    _volume.SetValueWithoutNotify(volume);
            }
        }

        /// <summary>
        /// Vertical sync's caption, and the cap greyed out and locked while it is on -- still saying what it is set to, so
        /// turning vertical sync off brings back exactly the cap the player had. Unity ignores a cap with vertical sync on.
        /// </summary>
        private void RefreshFrameRate()
        {
            bool vSync = FrameRate.VSync;
            Caption(_vSync, VSyncCaption(vSync));
            Lock(_cap, _capLabel, _capValue, vSync);

            int stop = FrameRate.CapStop;
            if (stop == _shownCap)
                return;
            _shownCap = stop;
            _capValue.text = CapCaption(stop);
            if (Mathf.RoundToInt(_cap.value) != stop)
                _cap.SetValueWithoutNotify(stop);
        }

        private static void Caption(ShellButton button, string caption)
        {
            if (button != null && button.text != caption)
                button.text = caption;
        }

        /// <summary>A switch that can or cannot be pressed: a dim caption is how a button says it cannot.</summary>
        private void Enable(ShellButton button, bool enabled)
        {
            if (button == null || button.enabledSelf == enabled)
                return;
            button.SetEnabled(enabled);
            button.style.color = enabled ? _palette.Text : _palette.Muted;
        }

        /// <summary>A slider row greyed out and locked, still showing what it is set to, or live.</summary>
        private void Lock(ShellSlider slider, Label label, Label value, bool locked)
        {
            if (slider == null || slider.enabledSelf == !locked)
                return;
            slider.SetEnabled(!locked);
            slider.style.opacity = locked ? 0.4f : 1f;
            label.style.color = value.style.color = locked ? _palette.Muted : _palette.Text;
        }

        private void ToggleMusic()
        {
            if (_audio != null)
                _audio.SetMusic(!GameAudio.MusicOn);
        }

        private void ToggleEffects()
        {
            if (_audio != null)
                _audio.SetEffects(!GameAudio.EffectsOn);
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
        /// Unity remembers the choice for the next launch itself. Changing the mode alone kept the fullscreen resolution on
        /// the way out -- a window the size of the display, title bar off the top -- so the size is always given with it
        /// (<see cref="DisplayRules.WindowedSize"/>). Measured on the display the window is on, not the main one: on a
        /// second monitor of a different size, the main display's size filled that monitor wrongly.
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

        /// <summary>What the vertical sync switch says.</summary>
        public static string VSyncCaption(bool on) => on ? "VSYNC  ON" : "VSYNC  OFF";

        /// <summary>What the cap's readout says at a stop of the slider: "144 FPS", or NONE.</summary>
        /// <remarks>Built once, so the readout allocates nothing however often it changes.</remarks>
        public static string CapCaption(int stop)
        {
            if (_capCaptions == null)
            {
                _capCaptions = new string[FrameRate.Stops];
                for (int i = 0; i < _capCaptions.Length; i++)
                {
                    int cap = FrameRate.CapAt(i);
                    _capCaptions[i] = cap == FrameRate.NoCap ? "NONE" : cap + " FPS";
                }
            }

            return _capCaptions[Mathf.Clamp(stop, 0, _capCaptions.Length - 1)];
        }

        private static string[] _capCaptions;

        private static void ToggleVSync() => FrameRate.SetVSync(!FrameRate.VSync);

        /// <summary>What the counter's switch says.</summary>
        /// <remarks>It names the counter, not the frame rate: "FPS OFF" read as though the frames themselves could be switched off.</remarks>
        public static string CounterCaption(bool on) => on ? "FPS COUNTER  ON" : "FPS COUNTER  OFF";

        private static void ToggleCounter() => FrameRate.SetCounter(!FrameRate.ShowsCounter);

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
            _reset.parent.style.display = confirming ? DisplayStyle.None : DisplayStyle.Flex;
            _question.style.display = confirming ? DisplayStyle.Flex : DisplayStyle.None;
        }

        // -----------------------------------------------------------------
        // Showing
        // -----------------------------------------------------------------

        /// <summary>
        /// Opens the settings from the 4.x title screen, in the 4.x boot the tests use (<see cref="AppLaunch.Classic"/>):
        /// the workbench is brought up under them, and BACK goes back to the title screen.
        /// </summary>
        public void Open()
        {
            _over = null;
            _overTheSimulator = false;
            if (_workbench != null)
                _workbench.Resume();
            Show(true);
        }

        /// <summary>Opens the settings over the simulator, as it is, which BACK goes back to (Part 15a; a page of it since 15g).</summary>
        public void OpenOver(WorkbenchPanel workbench)
        {
            _over = workbench;
            _overTheSimulator = workbench != null;
            Show(true);
        }

        /// <summary>
        /// Opens the settings again where they were, after a change of look has loaded the game again (<see cref="LookSwitch"/>):
        /// over the simulator, which the reload opened, or as from the title screen.
        /// </summary>
        public void OpenAgain()
        {
            if (_menu != null)
                _menu.Show(false);
            if (_overTheSimulator && _workbench != null)
            {
                _workbench.Resume();
                OpenOver(_workbench);
            }
            else
            {
                Open();
            }
        }

        /// <summary>The simulator Settings went over, which BACK goes back to; null for the title screen.</summary>
        private WorkbenchPanel _over;

        /// <summary>Whether Settings was last opened over the simulator: kept through the reload a change of look makes.</summary>
        private static bool _overTheSimulator;

        /// <summary>Whether Settings was opened over the simulator, rather than from the 4.x title screen.</summary>
        public bool OverTheSimulator => _over != null;

        /// <summary>Settings again, as it was: the credits roll coming back.</summary>
        public void Reopen() => Show(true);

        /// <summary>THE 4.x GAME: its title screen, which BACK TO THE SIMULATOR leaves (Part 15a).</summary>
        private void OpenTheGame()
        {
            Show(false);
            _over = null;
            _overTheSimulator = false;
            if (_workbench != null)
                _workbench.GoUnder();
            AppLaunch.ShowTheGame();
            if (_menu != null)
                _menu.OpenFromTheSimulator();   // so Escape on it comes back (Part 15g)
        }

        /// <summary>The credits roll, which comes back here.</summary>
        private void OpenCredits()
        {
            if (_credits == null)
                return;
            Show(false);
            _credits.Open();
        }

        /// <summary>Back to what Settings was opened over: the simulator, or the 4.x title screen.</summary>
        private void Back()
        {
            Show(false);

            if (_over != null)
            {
                WorkbenchPanel over = _over;
                _over = null;
                over.Resume();   // a reset made from here is shown on what is under it
                return;
            }

            if (_workbench != null)
                _workbench.GoUnder();
            if (_menu != null)
                _menu.Show(true);
        }

        /// <summary>
        /// The page up or down. Every visit starts with no question pending and nothing reported, so a reset reported last
        /// time is not mistaken for one just made.
        /// </summary>
        private void Show(bool visible)
        {
            if (visible)
            {
                if (_page == null)
                    Build();
                if (_page == null || _workbench == null)
                    return;
                _workbench.ShowPage(_page);
                SetConfirming(false);
                _status.text = string.Empty;
                _shownVolume = -1;
                _shownCap = -1;
                _openedFrame = Time.frameCount;
                UiEscape.Opened(this);
                Refresh();
                return;
            }

            if (!IsShowing)
                return;
            _workbench.HidePage(_page);
            _closedFrame = Time.frameCount;

            // A drag keeps its volume or its cap when it lets go; this keeps one that was somehow left staged.
            GameAudio.KeepVolume();
            FrameRate.KeepCap();
        }
    }
}
