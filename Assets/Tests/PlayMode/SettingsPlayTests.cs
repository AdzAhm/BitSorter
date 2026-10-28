using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using BitSorter.View;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using UnityEngine.UI;
using DisplayStyle = UnityEngine.UIElements.DisplayStyle;
using Label = UnityEngine.UIElements.Label;
using ShellButton = UnityEngine.UIElements.Button;
using VisualElement = UnityEngine.UIElements.VisualElement;
using Object = UnityEngine.Object;
using static UnityEngine.UIElements.UQueryExtensions;

namespace BitSorter.PlayMode.Tests
{
    /// <summary>
    /// The settings page, and above all its reset: that it asks, that saying no costs nothing, and
    /// that saying yes leaves a game a new player would recognise. A page of the workbench's own
    /// since Part 15g, in the shell, so its controls are found by name there.
    /// </summary>
    /// <remarks>
    /// "Like a new player" is more than an empty file. The tutorial remembers it has run this
    /// session, and free play holds its setup in memory, and neither reads the save again once it
    /// has read it once -- so a reset that only emptied the file would bring back neither the
    /// tutorial nor the opening setup until the game was restarted. Those are the two tests here
    /// that could not be written against the store alone.
    ///
    /// Everything runs under <see cref="SaveGuard"/>. A test that resets progress is the last test
    /// anyone wants pointed at the real save.
    /// </remarks>
    [TestFixture]
    public class SettingsPlayTests : InputTestFixture
    {
        private Keyboard _keyboard;

        [OneTimeSetUp]
        public void OneTimeSetup()
        {
            SaveGuard.Redirect();
            GameAnalytics.SetReporting(false);
        }

        [OneTimeTearDown]
        public void OneTimeCleanup()
        {
            SaveGuard.Release();
        }

        public override void Setup()
        {
            base.Setup();

            // As PanelPlayTests explains: the game never runs with read-value caching, and its
            // self-check fails a test on a queued key with every assertion passing.
            InputSystem.settings.SetInternalFeatureFlag("USE_READ_VALUE_CACHING", false);

            _keyboard = InputSystem.AddDevice<Keyboard>();
            InputSystem.AddDevice<Mouse>();
        }

        public override void TearDown()
        {
            SaveGuard.Clear();
            base.TearDown();
        }

        [UnityTearDown]
        public IEnumerator ClearTheScene()
        {
            yield return TestScene.Clear();
        }

        private static T Find<T>() where T : Object => Object.FindFirstObjectByType<T>();

        /// <summary>A button of Settings or the credits by its name in the shell; fails if it is not showing.</summary>
        private static ShellButton OnScreen(string name)
        {
            ShellButton found = ShellPanel.Find<ShellButton>(name);
            Assert.IsNotNull(found, $"no '{name}' button on screen");
            Assert.IsTrue(Shown(found), $"'{name}' is not showing");
            return found;
        }

        /// <summary>Whether <paramref name="element"/> is drawn: on a panel, with nothing it is in hidden.</summary>
        private static bool Shown(VisualElement element)
        {
            if (element == null || element.panel == null)
                return false;
            for (VisualElement e = element; e != null; e = e.parent)
                if (e.resolvedStyle.display == DisplayStyle.None)
                    return false;
            return true;
        }

        /// <summary>A button by its name: the shell's -- Settings, the credits -- or the 4.x menu's, which is still uGUI.</summary>
        private static IEnumerator Click(string name)
        {
            ShellButton shell = ShellPanel.Find<ShellButton>(name);
            if (Shown(shell))
            {
                ShellPanel.Submit(shell);
            }
            else
            {
                GameObject found = GameObject.Find(name);
                Assert.IsNotNull(found, $"no '{name}' button on screen");
                found.GetComponent<Button>().onClick.Invoke();
            }
            yield return null;
            yield return null;
        }

        /// <summary>The settings page, in the shell.</summary>
        private static VisualElement Page()
        {
            VisualElement page = ShellPanel.Find<VisualElement>(SettingsPanel.PageName);
            Assert.IsNotNull(page, "no settings page");
            return page;
        }

        private static Rect ScreenRect(VisualElement element) => ShellPanel.ScreenRectOf(element);

        /// <summary>A label of the settings page by its name in a row, the <paramref name="index"/>th of that name.</summary>
        private static Label PageLabel(string name, int index = 0)
        {
            List<Label> found = Page().Query<Label>(name).ToList();
            Assert.Greater(found.Count, index, $"no '{name}' label on the settings page");
            return found[index];
        }

        private static IEnumerator OpenSettings()
        {
            Assert.IsTrue(Find<MainMenu>().IsOpen, "sanity: the game should boot into the main menu");
            yield return Click("Settings");
            Assert.IsTrue(Find<SettingsPanel>().IsOpen, "the Settings row did not open the settings");
        }

        /// <summary>
        /// A player some way in: two levels solved, a circuit left on the second, the tutorial and
        /// the chapter card behind them and a hint already read -- and the second level on screen.
        /// </summary>
        private static IEnumerator APlayerWithProgress()
        {
            LevelSession session = Find<LevelSession>();
            ProgressStore store = Find<ProgressTracker>().Store;

            string first = session.AvailableLevels[0];
            string second = session.AvailableLevels[1];

            store.MarkComplete(first);
            store.MarkComplete(second);
            store.MarkMilestone(TutorialLevel.Key);
            store.MarkMilestone(ChapterCard.Milestone);
            store.MarkHintSeen(HintRules.Collision);

            session.LoadLevel(second);
            yield return null;
            yield return null;

            Assert.IsTrue(session.TryPlaceGate(session.Level.Budget[0].Kind, new Vector2Int(0, 0)),
                "sanity: could not put a gate on the second level");

            // Off the level and back, so the board is saved the way a player's would be.
            session.LoadLevel(first);
            yield return null;
            session.LoadLevel(second);
            yield return null;
            yield return null;

            Assert.IsNotNull(store.BoardFor(second), "sanity: the second level's board was not saved");
            Assert.IsFalse(session.Blueprint.IsEmpty, "sanity: the second level's board was not restored");
        }

        // -----------------------------------------------------------------
        // The screen
        // -----------------------------------------------------------------

        /// <summary>
        /// Settings is a row of the main menu, has its headed sections, and BACK returns to the menu
        /// rather than to the board. Opened from there it is the workbench's page, with the workbench
        /// brought up under it and taken down again with it (Part 15g).
        /// </summary>
        [UnityTest]
        public IEnumerator Settings_OpensFromTheMenu_WithItsSections_AndGoesBackToIt()
        {
            yield return TestScene.Load();
            yield return OpenSettings();

            Assert.IsFalse(Find<MainMenu>().IsOpen, "the menu stayed up under the settings");

            var shown = new HashSet<string>(Page().Query<Label>().ToList().Select(l => l.text));

            foreach (string heading in SettingsPanel.Headings)
                Assert.IsTrue(shown.Contains(heading), $"the {heading} section has no heading");

            yield return Click(SettingsPanel.BackButton);

            Assert.IsFalse(Find<SettingsPanel>().IsOpen, "BACK did not close the settings");
            Assert.IsTrue(Find<MainMenu>().IsOpen, "BACK left the player on the board instead of the menu");
            Assert.IsFalse(Find<WorkbenchPanel>().IsOpen, "BACK left the workbench up over the menu");
        }

        /// <summary>
        /// STYLE: choosing a look loads the game again in it, keeps the choice, and puts Settings back
        /// up where it was made; the workbench then draws in that look's canvas.
        /// </summary>
        [UnityTest]
        public IEnumerator ChoosingALook_LoadsTheGameAgainInIt_AndComesBackToSettings()
        {
            yield return TestScene.Load();
            yield return OpenSettings();

            try
            {
                Assert.AreSame(Look.Default, Look.Current, "sanity: the tests draw in the default look");

                yield return Click(SettingsPanel.LookButton(Looks.SchematicLight));
                Assert.IsTrue(LookSwitch.Busy, "choosing a look did nothing");

                // A cover up and down and a load between: generous, and loud if it never ends.
                for (int frame = 0; frame < 600 && LookSwitch.Busy; frame++)
                    yield return null;
                Assert.IsFalse(LookSwitch.Busy, "the switch never finished");

                Assert.AreSame(Looks.SchematicLight, Look.Current, "the game did not come back in the look chosen");
                Assert.AreEqual(Looks.SchematicLight.Code, Preferences.GetInt(Look.PreferenceKey, 0), "the choice was not kept");
                Assert.IsTrue(Find<SettingsPanel>().IsOpen, "the switch did not come back to Settings");
                Assert.IsFalse(Find<MainMenu>().IsOpen, "the menu came up over Settings");
                Assert.AreEqual(SchematicPalette.Current.Accent,
                    OnScreen(SettingsPanel.LookButton(Looks.SchematicLight)).resolvedStyle.borderLeftColor,
                    "the look in use should be the chosen button");
                Assert.AreEqual(SchematicPalette.Light.Ground, Page().resolvedStyle.backgroundColor,
                    "the page is not in the look chosen");

                yield return Click(SettingsPanel.BackButton);
                yield return Click(MainMenu.WorkbenchButton);
                WorkbenchPanel workbench = Find<WorkbenchPanel>();
                workbench.Seek(20.0);
                yield return null;
                yield return null;

                Assert.IsTrue(workbench.TryDrawnColour("a", out Color a), "the workbench drew no wire a");
                Assert.AreEqual(SchematicPalette.Light.One, a, "at 20 a is 1, drawn in the light canvas's 1");
            }
            finally
            {
                Look.Use(null);
            }
        }

        /// <summary>
        /// Analytics listens to the level session of the scene loaded now, and to the new one after a change of look loads
        /// the game again. It subscribed once, to the first scene loaded, so after a change of look -- or in any scene loaded
        /// after the first -- it heard no level started and none solved (the #84 review).
        /// </summary>
        [UnityTest]
        public IEnumerator Analytics_ListensToTheSceneLoaded_AndToTheNewOneAfterAChangeOfLook()
        {
            yield return TestScene.Load();
            Assert.IsTrue(GameAnalytics.Listening, "analytics does not hear the scene loaded");
            yield return OpenSettings();

            try
            {
                yield return Click(SettingsPanel.LookButton(Looks.SchematicLight));
                for (int frame = 0; frame < 600 && LookSwitch.Busy; frame++)
                    yield return null;
                Assert.IsFalse(LookSwitch.Busy, "sanity: the switch finished");
                Assert.IsTrue(GameAnalytics.Listening, "after a change of look analytics hears the old, unloaded scene");
            }
            finally
            {
                Look.Use(null);
            }
        }

        /// <summary>Sound moved from the menu to here, and still switches the sound.</summary>
        [UnityTest]
        public IEnumerator TheSoundSetting_SwitchesTheSound()
        {
            yield return TestScene.Load();
            yield return OpenSettings();

            bool before = GameAudio.Muted;

            yield return Click(SettingsPanel.SoundButton);
            Assert.AreNotEqual(before, GameAudio.Muted, "the sound setting did not switch the sound");

            yield return Click(SettingsPanel.SoundButton);
            Assert.AreEqual(before, GameAudio.Muted, "the sound setting did not switch the sound back");
        }

        /// <summary>The volume sits under the sound switch, which it depends on.</summary>
        [UnityTest]
        public IEnumerator TheVolume_SitsUnderTheSoundSwitch()
        {
            yield return TestScene.Load();
            yield return OpenSettings();

            Rect sound = ScreenRect(OnScreen(SettingsPanel.SoundButton));
            Rect volume = ScreenRect(VolumeSlider());

            Assert.LessOrEqual(volume.yMax, sound.yMin, "the volume is not under the sound switch");
            Assert.Less(Mathf.Abs(volume.center.x - sound.center.x), 400f, "the volume is not beside it either");
        }

        /// <summary>
        /// With the sound off the volume is greyed out and cannot be moved; with it back on, it can.
        /// </summary>
        [UnityTest]
        public IEnumerator WithTheSoundOff_TheVolumeIsGreyedOutAndLocked()
        {
            yield return TestScene.Load();
            yield return OpenSettings();

            ShellSlider volume = VolumeSlider();
            Assert.IsFalse(GameAudio.Muted, "sanity: a fresh machine starts with the sound on");
            Assert.IsTrue(volume.enabledSelf, "the volume is locked with the sound on");

            yield return Click(SettingsPanel.SoundButton);

            Assert.IsTrue(GameAudio.Muted, "sanity: the sound switch should have turned the sound off");
            Assert.IsFalse(volume.enabledSelf, "the volume can still be moved with the sound off");
            Assert.Less(volume.resolvedStyle.opacity, 1f, "the volume is not greyed out with the sound off");

            yield return Click(SettingsPanel.SoundButton);

            Assert.IsFalse(GameAudio.Muted);
            Assert.IsTrue(volume.enabledSelf, "the volume stayed locked once the sound was back on");
            Assert.AreEqual(1f, volume.resolvedStyle.opacity, "the volume stayed grey once the sound was back on");
        }

        /// <summary>
        /// MUSIC and EFFECTS each switch their own setting, and are greyed out and locked while the
        /// sound is off -- still saying what they are set to.
        /// </summary>
        [UnityTest]
        public IEnumerator TheMusicAndEffectsSwitches_WorkUnderTheSoundSwitch()
        {
            yield return TestScene.Load();
            yield return OpenSettings();

            Assert.IsFalse(GameAudio.MusicOn, "a fresh machine has the music off (Part 15a)");
            Assert.IsTrue(GameAudio.EffectsOn, "and the effects on");

            yield return Click(SettingsPanel.MusicButton);
            Assert.IsTrue(GameAudio.MusicOn, "MUSIC did not switch the music on");
            Assert.IsTrue(GameAudio.EffectsOn, "MUSIC switched the effects as well");

            yield return Click(SettingsPanel.EffectsButton);
            Assert.IsFalse(GameAudio.EffectsOn, "EFFECTS did not switch the effects off");

            yield return Click(SettingsPanel.SoundButton);
            Assert.IsTrue(GameAudio.Muted, "sanity: SOUND should have switched everything off");

            ShellButton music = OnScreen(SettingsPanel.MusicButton);
            ShellButton effects = OnScreen(SettingsPanel.EffectsButton);
            Assert.IsFalse(music.enabledSelf, "MUSIC can still be pressed with the sound off");
            Assert.IsFalse(effects.enabledSelf, "EFFECTS can still be pressed with the sound off");
            StringAssert.Contains("ON", music.text, "a greyed-out MUSIC stopped saying what it is set to");

            yield return Click(SettingsPanel.SoundButton);
            Assert.IsTrue(music.enabledSelf && effects.enabledSelf, "the switches stayed locked with the sound back on");

            // Put back: the fixture shares its scratch preferences between tests.
            yield return Click(SettingsPanel.MusicButton);
            yield return Click(SettingsPanel.EffectsButton);
            Assert.IsTrue(!GameAudio.MusicOn && GameAudio.EffectsOn, "sanity: both should be back as they started");
        }

        /// <summary>
        /// Moving the volume scales the music at once, and remembers where it was put.
        /// </summary>
        /// <remarks>
        /// Both readings are taken in the same frame, so the menu music's own fade cannot move
        /// between them: half the volume is half the music, whatever the music is doing.
        /// </remarks>
        [UnityTest]
        public IEnumerator TheVolume_ScalesTheMusic()
        {
            yield return TestScene.Load();
            yield return OpenSettings();

            ShellSlider volume = VolumeSlider();
            AudioSource music = Find<GameAudio>().MusicSource;

            volume.value = 100;
            float full = music.volume;
            Assert.Greater(full, 0f, "sanity: the music should be playing at some volume");

            volume.value = 50;
            Assert.AreEqual(50, GameAudio.Volume, "the slider did not set the volume");
            Assert.AreEqual(full * 0.5f, music.volume, 1e-4f, "half the volume is not half the music");

            volume.value = GameAudio.DefaultVolume;   // the fixture shares its scratch preferences
        }

        /// <summary>A desktop build, and the editor, offer the fullscreen switch under DISPLAY.</summary>
        [UnityTest]
        public IEnumerator TheDisplaySection_OffersFullscreen()
        {
            yield return TestScene.Load();
            yield return OpenSettings();

            ShellButton fullscreen = OnScreen(SettingsPanel.FullscreenButton);
            StringAssert.StartsWith("FULLSCREEN", fullscreen.text);
        }

        /// <summary>
        /// VSYNC sits beside FULLSCREEN under DISPLAY, says what it is set to, and switches both ways.
        /// </summary>
        [UnityTest]
        public IEnumerator TheDisplaySection_OffersVSync_BesideFullscreen()
        {
            yield return TestScene.Load();
            yield return OpenSettings();

            ShellButton fullscreen = OnScreen(SettingsPanel.FullscreenButton);
            ShellButton vSync = OnScreen(SettingsPanel.VSyncButton);

            Assert.IsTrue(FrameRate.VSync, "sanity: a fresh machine keeps up with its screen");
            Assert.AreEqual(SettingsPanel.VSyncCaption(true), vSync.text);

            Rect beside = ScreenRect(fullscreen);
            Rect sync = ScreenRect(vSync);
            Assert.AreEqual(beside.center.y, sync.center.y, 1f, "VSYNC is not on FULLSCREEN's row");
            Assert.Greater(sync.xMin, beside.xMax, "VSYNC overlaps FULLSCREEN");

            yield return Click(SettingsPanel.VSyncButton);
            Assert.IsFalse(FrameRate.VSync, "VSYNC did not switch vertical sync off");
            Assert.AreEqual(SettingsPanel.VSyncCaption(false), vSync.text, "the switch does not say it is off");

            // Back, which also puts the fixture's shared scratch preferences as they were.
            yield return Click(SettingsPanel.VSyncButton);
            Assert.IsTrue(FrameRate.VSync, "VSYNC did not switch back on");
        }

        /// <summary>
        /// The frame cap sits under VSYNC, is greyed out and locked while vertical sync is on -- when
        /// Unity would ignore it -- and once it is off, sets the cap and says what it is.
        /// </summary>
        [UnityTest]
        public IEnumerator TheFrameCap_IsLockedUntilVSyncIsOff_ThenSetsTheCap()
        {
            yield return TestScene.Load();
            yield return OpenSettings();

            ShellSlider cap = FrameCapSlider();
            Label readout = PageLabel("value", 1);   // the volume's first, the cap's under it
            Label label = PageLabel("caption", 1);

            Rect sync = ScreenRect(OnScreen(SettingsPanel.VSyncButton));
            Assert.LessOrEqual(ScreenRect(cap).yMax, sync.yMin, "the cap is not under VSYNC");
            Assert.AreEqual(ScreenRect(VolumeSlider()).xMin, ScreenRect(cap).xMin, 0.5f,
                "the cap's slider is not in line with the volume's");
            Assert.AreEqual(ScreenRect(PageLabel("value", 0)).xMin, ScreenRect(readout).xMin, 0.5f,
                "the cap's readout is not in line with the volume's");
            Assert.AreEqual("FRAME CAP", label.text);

            int before = FrameRate.CapStop;

            Assert.IsTrue(FrameRate.VSync, "sanity: a fresh machine has vertical sync on");
            Assert.IsFalse(cap.enabledSelf, "the cap can be moved while vertical sync would ignore it");
            Assert.Less(cap.resolvedStyle.opacity, 1f, "the cap is not greyed out with vertical sync on");
            Assert.AreEqual(SettingsPanel.CapCaption(FrameRate.CapStop), readout.text,
                "a greyed-out cap stopped saying what it is set to");

            yield return Click(SettingsPanel.VSyncButton);

            Assert.IsTrue(cap.enabledSelf, "the cap stayed locked with vertical sync off");
            Assert.AreEqual(1f, cap.resolvedStyle.opacity, "the cap stayed grey with vertical sync off");

            int top = FrameRate.Stops - 2;
            cap.value = top;
            yield return null;

            Assert.AreEqual(FrameRate.CapAt(top), FrameRate.Cap, "the slider did not set the cap");
            Assert.AreEqual(SettingsPanel.CapCaption(top), readout.text, "the readout does not say the new cap");

            // Put back: the fixture shares its scratch preferences between tests.
            cap.value = before;
            yield return Click(SettingsPanel.VSyncButton);
            Assert.IsTrue(FrameRate.VSync && FrameRate.CapStop == before, "sanity: both should be back");
        }

        /// <summary>
        /// FPS COUNTER puts a counter in the top-right corner: above every panel, clear of the
        /// buttons that corner already has, taking no clicks, and saying a real number. Off again,
        /// it goes.
        /// </summary>
        [UnityTest]
        public IEnumerator TheFpsSwitch_PutsACounterInTheTopRightCorner()
        {
            yield return TestScene.Load();
            yield return OpenSettings();

            FrameRateCounter counter = Find<FrameRateCounter>();
            Assert.IsNotNull(counter, "the scene has no frame-rate counter");
            Assert.IsFalse(counter.IsShowing, "a machine that never asked has a counter in the corner");

            ShellButton fps = OnScreen(SettingsPanel.CounterButton);
            Assert.AreEqual(ScreenRect(OnScreen(SettingsPanel.VSyncButton)).center.y, ScreenRect(fps).center.y, 1f,
                "FPS COUNTER is not on VSYNC's row");
            Assert.AreEqual(SettingsPanel.CounterCaption(false), fps.text);

            yield return Click(SettingsPanel.CounterButton);
            Assert.IsTrue(FrameRate.ShowsCounter, "FPS COUNTER did not switch the counter on");
            Assert.IsTrue(counter.IsShowing, "the counter did not come up");

            // Real seconds, which the clock TestScene drives does not reach: capped in unscaled
            // seconds, generous and loud, never in frames.
            float until = Time.unscaledTime + 30f;

            while (counter.Shown <= 0 && Time.unscaledTime < until)
                yield return null;

            Assert.Greater(counter.Shown, 0, "the counter never said a frame rate");
            TextMeshProUGUI number = counter.Box.GetComponentInChildren<TextMeshProUGUI>();
            number.ForceMeshUpdate();
            Assert.AreEqual(counter.Shown.ToString(), number.GetParsedText(), "the corner does not say the rate counted");

            Rect box = ScreenRect(counter.Box);
            Assert.Greater(box.xMax, Screen.width * 0.97f, "the counter is not at the right edge");
            Assert.Greater(box.yMax, Screen.height * 0.97f, "the counter is not at the top");
            Assert.LessOrEqual(box.xMax, Screen.width + 0.5f, "the counter runs off the right edge");
            Assert.LessOrEqual(box.yMax, Screen.height + 0.5f, "the counter runs off the top");
            Assert.IsFalse(box.Overlaps(ScreenRect(OnScreen(SettingsPanel.BackButton))), "the counter sits on BACK");

            Canvas own = counter.Box.GetComponent<Canvas>();
            Assert.IsTrue(own != null && own.overrideSorting, "the counter has no canvas of its own to sort above panels");
            Assert.Greater(own.sortingOrder, own.rootCanvas.sortingOrder, "a panel can cover the counter");

            foreach (Graphic graphic in counter.Box.GetComponentsInChildren<Graphic>(true))
                Assert.IsFalse(graphic.raycastTarget, $"'{graphic.name}' in the counter would take a click");

            // On a board, the corner also has the help badge under it.
            yield return Click(SettingsPanel.BackButton);
            Find<MainMenu>().Show(false);
            yield return null;
            yield return null;

            GameObject badge = GameObject.Find("Help badge");
            Assert.IsNotNull(badge, "sanity: the help badge should be on the board");
            Assert.IsFalse(ScreenRect(counter.Box).Overlaps(ScreenRect(badge.GetComponent<RectTransform>())),
                "the counter sits on the help badge");

            FrameRate.SetCounter(false);   // the fixture shares its scratch preferences
            yield return null;
            Assert.IsFalse(counter.IsShowing, "the counter stayed up once switched off");
        }

        /// <summary>
        /// SHORTCUT KEYS, beside REDUCED MOTION, hides every key drawn beside a button and shows them again: the caps over the
        /// run buttons and the keys under MENU and the help badge, and Q and E beside the level's counter (asked for
        /// 2026-10-26). Shown to begin with; the caption says which, and fits either way.
        /// </summary>
        [UnityTest]
        public IEnumerator ShortcutKeys_HidesEveryKeyBesideAButton_AndShowsThemAgain()
        {
            yield return TestScene.Load();
            yield return OpenSettings();

            ShellButton keys = OnScreen(SettingsPanel.KeysButton);
            Assert.AreEqual(ScreenRect(OnScreen(SettingsPanel.MotionButton)).center.y, ScreenRect(keys).center.y, 1f,
                "SHORTCUT KEYS is not on REDUCED MOTION's row");
            Assert.IsTrue(KeyCaps.Shown, "the keys are shown to begin with");
            AssertFits(SettingsPanel.KeysButton, SettingsPanel.KeysCaption(true));
            AssertFits(SettingsPanel.KeysButton, SettingsPanel.KeysCaption(false));

            yield return Click(SettingsPanel.KeysButton);
            Assert.IsFalse(KeyCaps.Shown, "SHORTCUT KEYS did not hide the keys");
            Assert.AreEqual(SettingsPanel.KeysCaption(false), keys.text);

            yield return Click(SettingsPanel.BackButton);
            Find<MainMenu>().Show(false);
            yield return null;
            yield return null;

            KeyCapView[] marked = Object.FindObjectsByType<KeyCapView>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            Assert.IsNotEmpty(marked, "sanity: the board has keys beside its buttons");
            foreach (KeyCapView cap in marked)
                Assert.IsFalse(cap.Drawn, $"'{cap.name}' under '{cap.transform.parent.name}' is still drawn");
            StringAssert.DoesNotContain("Q ", StatusBanner.Counter(3, 10, KeyCaps.Shown), "Q has gone from the counter");
            StringAssert.Contains("‹", StatusBanner.Counter(3, 10, KeyCaps.Shown), "and its arrow stays");

            KeyCaps.SetShown(true);   // the fixture shares its scratch preferences
            yield return null;
            foreach (KeyCapView cap in marked)
                Assert.IsTrue(cap.Drawn, $"'{cap.name}' did not come back");
        }

        /// <summary>
        /// Every DISPLAY switch's caption fits its button, whichever way it is set. They are three
        /// to a row, a switch's width rather than a button's, and the longest caption is set by the
        /// state the switch is not in.
        /// </summary>
        [UnityTest]
        public IEnumerator TheDisplaySwitches_CaptionsFitTheirButtons_EitherWay()
        {
            yield return TestScene.Load();
            yield return OpenSettings();

            AssertFits(SettingsPanel.FullscreenButton, "FULLSCREEN  OFF");

            foreach (bool on in new[] { true, false })
            {
                AssertFits(SettingsPanel.VSyncButton, SettingsPanel.VSyncCaption(on));
                AssertFits(SettingsPanel.CounterButton, SettingsPanel.CounterCaption(on));
            }
        }

        private static void AssertFits(string button, string caption)
        {
            ShellButton found = OnScreen(button);
            Vector2 size = found.MeasureTextSize(caption, 0f, VisualElement.MeasureMode.Undefined, 0f, VisualElement.MeasureMode.Undefined);
            float room = found.resolvedStyle.width - found.resolvedStyle.paddingLeft - found.resolvedStyle.paddingRight
                         - found.resolvedStyle.borderLeftWidth - found.resolvedStyle.borderRightWidth;
            Assert.LessOrEqual(size.x, room, $"'{caption}' runs out of its button");
        }

        // -----------------------------------------------------------------
        // Credits
        // -----------------------------------------------------------------

        private static IEnumerator OpenCredits()
        {
            yield return OpenSettings();
            yield return Click(SettingsPanel.CreditsButton);

            Assert.IsTrue(Find<CreditsPanel>().IsOpen, "CREDITS did not open the credits");
            Assert.IsFalse(Find<SettingsPanel>().IsOpen, "the settings stayed up under the credits");
        }

        /// <summary>CREDITS, under everything else in Settings, opens a roll that rises.</summary>
        [UnityTest]
        public IEnumerator Credits_OpenFromSettings_AndRollUpward()
        {
            yield return TestScene.Load();
            yield return OpenCredits();

            CreditsPanel credits = Find<CreditsPanel>();
            float start = credits.Risen;

            for (int frame = 0; frame < 300 && credits.Risen <= start; frame++)
                yield return null;

            Assert.Greater(credits.Risen, start, "the credits did not roll");
        }

        /// <summary>The CREDITS button is below every other setting.</summary>
        [UnityTest]
        public IEnumerator TheCreditsButton_IsBelowEverySetting()
        {
            yield return TestScene.Load();
            yield return OpenSettings();

            Rect credits = ScreenRect(OnScreen(SettingsPanel.CreditsButton));

            foreach (string name in new[] { SettingsPanel.SoundButton, SettingsPanel.DataButton, SettingsPanel.ResetButton })
                Assert.LessOrEqual(credits.yMax, ScreenRect(OnScreen(name)).yMin, $"CREDITS is not below {name}");
        }

        /// <summary>Any key goes back from the credits to the settings.</summary>
        [UnityTest]
        public IEnumerator AnyKey_GoesBackFromTheCreditsToTheSettings()
        {
            yield return TestScene.Load();
            yield return OpenCredits();
            yield return null;

            PressAndRelease(_keyboard.kKey);
            yield return null;
            yield return null;

            Assert.IsFalse(Find<CreditsPanel>().IsOpen, "a key did not close the credits");
            Assert.IsTrue(Find<SettingsPanel>().IsOpen, "a key left the credits for somewhere other than the settings");
        }

        /// <summary>
        /// Escape goes back to the settings too, and no further: the settings do not take the same
        /// press to go back to the menu.
        /// </summary>
        [UnityTest]
        public IEnumerator Escape_GoesBackFromTheCreditsToTheSettings_AndNoFurther()
        {
            yield return TestScene.Load();
            yield return OpenCredits();
            yield return null;

            PressAndRelease(_keyboard.escapeKey);
            yield return null;
            yield return null;

            Assert.IsFalse(Find<CreditsPanel>().IsOpen, "Escape did not close the credits");
            Assert.IsTrue(Find<SettingsPanel>().IsOpen, "one Escape went through the settings as well");
            Assert.IsFalse(Find<MainMenu>().IsOpen, "one Escape went all the way back to the menu");
        }

        /// <summary>
        /// The line saying how to leave the credits sits clear of the column the roll rises through.
        /// </summary>
        /// <remarks>
        /// Centred at the foot of the screen, every line of the roll passed through it on the way up.
        /// </remarks>
        [UnityTest]
        public IEnumerator TheCreditsHelpLine_IsClearOfTheRoll()
        {
            yield return TestScene.Load();
            yield return OpenCredits();
            yield return null;

            VisualElement page = ShellPanel.Find<VisualElement>(CreditsPanel.PageName);
            Assert.IsNotNull(page, "sanity: the credits should be on screen");
            Label help = page.Q<Label>("help");
            Assert.IsNotNull(help, "the credits do not say how to leave them");
            Assert.AreEqual(CreditsPanel.HelpText, help.text);

            float column = 0f;
            foreach (Label line in page.Q("roll").Query<Label>().ToList())
                column = Mathf.Max(column, ScreenRect(line).xMax);
            Assert.GreaterOrEqual(ScreenRect(help).xMin, column, "the help line sits in the column the roll rises through");
        }

        /// <summary>A click anywhere goes back from the credits to the settings.</summary>
        [UnityTest]
        public IEnumerator AClick_GoesBackFromTheCreditsToTheSettings()
        {
            yield return TestScene.Load();
            yield return OpenCredits();
            yield return null;

            Mouse mouse = Mouse.current;
            Assert.IsNotNull(mouse, "sanity: the fixture adds a mouse");

            PressAndRelease(mouse.leftButton);
            yield return null;
            yield return null;

            Assert.IsFalse(Find<CreditsPanel>().IsOpen, "a click did not close the credits");
            Assert.IsTrue(Find<SettingsPanel>().IsOpen, "a click left the credits for somewhere other than the settings");
        }

        /// <summary>
        /// A click leaves the credits when the button comes up, not when it goes down, so the press
        /// cannot also land on the settings that come back under the pointer.
        /// </summary>
        /// <remarks>
        /// Left on the press, the settings came back in the same frame, and if the interface's input
        /// ran after the credits did, the press went to whichever setting was now under the pointer
        /// and pressed it when the button came up -- DATA among them. Which ran first was Unity's
        /// choice, so it happened some of the time.
        /// </remarks>
        [UnityTest]
        public IEnumerator AClick_LeavesTheCreditsOnRelease_NotOnPress()
        {
            yield return TestScene.Load();
            yield return OpenCredits();
            yield return null;

            Mouse mouse = Mouse.current;
            Assert.IsNotNull(mouse, "sanity: the fixture adds a mouse");

            Press(mouse.leftButton);
            yield return null;

            Assert.IsTrue(Find<CreditsPanel>().IsOpen,
                "the credits closed while the button was still down, so the press could land on a setting");

            Release(mouse.leftButton);
            yield return null;
            yield return null;

            Assert.IsFalse(Find<CreditsPanel>().IsOpen, "letting go did not close the credits");
            Assert.IsTrue(Find<SettingsPanel>().IsOpen, "letting go left the credits for somewhere other than the settings");
        }

        /// <summary>
        /// The page scrolls in a short window, where the uGUI screen it replaced shrank to fit (Part 15g): its last control,
        /// CREDITS, is below the bottom of a page shorter than the sections, and scrolling brings it up.
        /// </summary>
        [UnityTest]
        public IEnumerator ThePage_Scrolls_WhereTheSectionsAreTallerThanIt()
        {
            yield return TestScene.Load();
            yield return OpenSettings();
            yield return null;

            var scroll = Page().Q<UnityEngine.UIElements.ScrollView>();
            Assert.IsNotNull(scroll, "the page does not scroll");
            Rect back = ScreenRect(OnScreen(SettingsPanel.BackButton));
            Rect title = ScreenRect(PageLabel("title"));
            Assert.IsFalse(back.Overlaps(title), "BACK sits on the title");
            Assert.AreEqual(UnityEngine.UIElements.Overflow.Hidden, scroll.contentViewport.style.overflow.value,
                "what scrolls above the page is drawn over the workbench's header");

            float tall = scroll.contentContainer.layout.height, room = scroll.layout.height;
            if (tall <= room)
                Assert.Pass("the window is tall enough for every section");
            scroll.scrollOffset = new Vector2(0f, tall);
            yield return null;
            yield return null;
            Assert.GreaterOrEqual(ScreenRect(OnScreen(SettingsPanel.CreditsButton)).yMin, ScreenRect(scroll).yMin - 0.5f,
                "scrolled to its foot, CREDITS is still below the page");
        }

        /// <summary>
        /// The volume and the cap keep to whole stops, the thumb with them, as uGUI's whole-number sliders did (the 15g-2
        /// review): the cap's thumb rested between two stops while its readout named one.
        /// </summary>
        [UnityTest]
        public IEnumerator TheSliders_KeepToWholeStops()
        {
            yield return TestScene.Load();
            yield return OpenSettings();

            ShellSlider volume = VolumeSlider(), cap = FrameCapSlider();
            volume.value = 40.4f;
            cap.value = 2.4f;
            yield return null;
            Assert.AreEqual(40f, volume.value, "the volume between two percents");
            Assert.AreEqual(2f, cap.value, "the cap between two stops");
            Assert.AreEqual(2, FrameRate.CapStop);
        }

        private static ShellSlider VolumeSlider()
        {
            ShellSlider found = ShellPanel.Find<ShellSlider>(SettingsPanel.VolumeSlider);
            Assert.IsTrue(Shown(found), "no volume slider on screen");
            return found;
        }

        private static ShellSlider FrameCapSlider()
        {
            ShellSlider found = ShellPanel.Find<ShellSlider>(SettingsPanel.FrameCapSlider);
            Assert.IsTrue(Shown(found), "no frame cap slider on screen");
            return found;
        }

        /// <summary>Each section's line wraps inside the column, never out of it.</summary>
        [UnityTest]
        public IEnumerator EverySectionsDescription_FitsTheColumn()
        {
            yield return TestScene.Load();
            yield return OpenSettings();
            yield return null;

            Rect column = ScreenRect(Page().Q("sections"));
            List<Label> descriptions = Page().Query<Label>("description").ToList();
            Assert.IsNotEmpty(descriptions, "sanity: the sections have descriptions");
            foreach (Label description in descriptions)
            {
                Rect at = ScreenRect(description);
                Assert.LessOrEqual(at.xMax, column.xMax + 0.5f, $"'{description.text}' runs out of the column");
                Assert.GreaterOrEqual(at.height, 1f, $"'{description.text}' has no room");
            }
        }

        // -----------------------------------------------------------------
        // Data
        // -----------------------------------------------------------------

        /// <summary>
        /// An event waiting for the analytics services to start is dropped when DATA is switched
        /// off, never sent once they are up.
        /// </summary>
        /// <remarks>
        /// Events queue until the services have started, which on a slow or blocked connection is a
        /// while -- long enough to open Settings and switch DATA off. The switch stopped new events
        /// and told the consent framework, and left the queue to be sent the moment the services
        /// came up: a promise not to collect, kept for everything but what was already collected.
        ///
        /// Driven through the private state rather than a real boot, because a real boot with
        /// reporting on is a real event from a test run. Reporting is switched on in the redirected
        /// preferences alone, so consent is never granted and nothing can leave the machine; and no
        /// frame passes while the queue holds anything, so the boot's own flush cannot run.
        /// </remarks>
        [Test]
        public void SwitchingDataOff_DropsWhatWasWaitingToBeSent()
        {
            const BindingFlags Hidden = BindingFlags.Static | BindingFlags.NonPublic;
            System.Type analytics = typeof(GameAnalytics);

            FieldInfo consentKey = analytics.GetField("ConsentKey", Hidden);
            FieldInfo collecting = analytics.GetField("_collecting", Hidden);
            FieldInfo unavailable = analytics.GetField("_unavailable", Hidden);
            FieldInfo pending = analytics.GetField("Pending", Hidden);
            FieldInfo sent = analytics.GetField("Sent", Hidden);
            MethodInfo record = analytics.GetMethod("Record", Hidden);

            Assert.IsNotNull(consentKey, "GameAnalytics no longer names its preference in ConsentKey");
            Assert.IsNotNull(collecting, "GameAnalytics no longer keeps _collecting");
            Assert.IsNotNull(unavailable, "GameAnalytics no longer keeps _unavailable");
            Assert.IsNotNull(pending, "GameAnalytics no longer queues in Pending");
            Assert.IsNotNull(sent, "GameAnalytics no longer remembers what it sent in Sent");
            Assert.IsNotNull(record, "GameAnalytics no longer records through Record");

            var queue = (List<KeyValuePair<string, string>>)pending.GetValue(null);
            var gone = (HashSet<string>)sent.GetValue(null);
            string key = AnalyticsRules.SessionKey(AnalyticsRules.LevelStarted, "route-the-bit");

            object wasCollecting = collecting.GetValue(null);
            object wasUnavailable = unavailable.GetValue(null);
            bool wasSent = gone.Contains(key);

            try
            {
                collecting.SetValue(null, false);
                unavailable.SetValue(null, false);
                gone.Remove(key);
                queue.Clear();

                Assert.IsNotNull(Preferences.Redirected,
                    "sanity: the preferences should be redirected, or this would write the player's own");
                Preferences.SetInt((string)consentKey.GetRawConstantValue(), 1);
                Assert.IsTrue(GameAnalytics.Reporting, "sanity: reporting should read as on");

                record.Invoke(null, new object[] { AnalyticsRules.LevelStarted, "route-the-bit" });
                Assert.AreEqual(1, queue.Count,
                    "sanity: with the services still starting, a level start should wait in the queue");

                GameAnalytics.SetReporting(false);

                Assert.AreEqual(0, queue.Count,
                    "DATA was switched off and an event was still waiting to be sent once the services were up");
            }
            finally
            {
                queue.Clear();
                GameAnalytics.SetReporting(false);
                collecting.SetValue(null, wasCollecting);
                unavailable.SetValue(null, wasUnavailable);

                if (wasSent)
                    gone.Add(key);
                else
                    gone.Remove(key);
            }
        }

        // -----------------------------------------------------------------
        // Asking
        // -----------------------------------------------------------------

        /// <summary>RESET PROGRESS asks first, and until it is answered nothing is forgotten.</summary>
        [UnityTest]
        public IEnumerator ResetProgress_AsksBeforeItForgetsAnything()
        {
            yield return TestScene.Load();
            yield return APlayerWithProgress();
            Find<MainMenu>().Show(true);
            yield return null;
            yield return OpenSettings();

            string saved = SaveGuard.Read();

            yield return Click(SettingsPanel.ResetButton);

            Assert.IsTrue(Find<SettingsPanel>().Confirming, "RESET PROGRESS did not ask first");
            Assert.AreEqual(2, Find<ProgressTracker>().Store.CompletedCount, "the progress went before the answer");
            Assert.AreEqual(saved, SaveGuard.Read(), "the save file changed before the answer");
            OnScreen(SettingsPanel.ConfirmButton);
            OnScreen(SettingsPanel.CancelButton);
        }

        /// <summary>
        /// A double-click on RESET PROGRESS cannot answer its own question: the question sits
        /// where the button was, and neither answer overlaps it.
        /// </summary>
        [UnityTest]
        public IEnumerator NeitherAnswer_SitsWhereTheResetButtonWas()
        {
            yield return TestScene.Load();
            yield return OpenSettings();

            Rect reset = ScreenRect(OnScreen(SettingsPanel.ResetButton));
            yield return Click(SettingsPanel.ResetButton);

            Assert.IsFalse(reset.Overlaps(ScreenRect(OnScreen(SettingsPanel.ConfirmButton))),
                "YES, RESET is under the pointer that pressed RESET PROGRESS -- a double-click resets");
            Assert.IsFalse(reset.Overlaps(ScreenRect(OnScreen(SettingsPanel.CancelButton))),
                "CANCEL is under the pointer that pressed RESET PROGRESS");
        }

        private static Rect ScreenRect(Component part)
        {
            var corners = new Vector3[4];
            part.GetComponent<RectTransform>().GetWorldCorners(corners);
            return Rect.MinMaxRect(corners[0].x, corners[0].y, corners[2].x, corners[2].y);
        }

        /// <summary>Saying no leaves everything exactly as it was, file included.</summary>
        [UnityTest]
        public IEnumerator CancellingTheQuestion_LeavesTheSaveAlone()
        {
            yield return TestScene.Load();
            yield return APlayerWithProgress();
            Find<MainMenu>().Show(true);
            yield return null;
            yield return OpenSettings();

            string saved = SaveGuard.Read();

            yield return Click(SettingsPanel.ResetButton);
            yield return Click(SettingsPanel.CancelButton);

            Assert.IsFalse(Find<SettingsPanel>().Confirming, "CANCEL left the question up");
            OnScreen(SettingsPanel.ResetButton);
            Assert.AreEqual(2, Find<ProgressTracker>().Store.CompletedCount, "CANCEL forgot progress");
            Assert.AreEqual(saved, SaveGuard.Read(), "CANCEL changed the save file");
            Assert.AreEqual(Find<LevelSession>().AvailableLevels[1], Find<LevelSession>().LevelName,
                "CANCEL moved the player off their level");
        }

        /// <summary>Escape backs out of the question first, and only then out of the screen.</summary>
        [UnityTest]
        public IEnumerator Escape_BacksOutOfTheQuestion_ThenOutOfTheScreen()
        {
            yield return TestScene.Load();
            yield return OpenSettings();
            yield return Click(SettingsPanel.ResetButton);

            PressAndRelease(_keyboard.escapeKey);
            yield return null;
            yield return null;

            SettingsPanel settings = Find<SettingsPanel>();
            Assert.IsFalse(settings.Confirming, "Escape did not take the question down");
            Assert.IsTrue(settings.IsOpen, "Escape took the whole screen down with the question");

            PressAndRelease(_keyboard.escapeKey);
            yield return null;
            yield return null;

            Assert.IsFalse(settings.IsOpen, "Escape did not leave the settings");
            Assert.IsTrue(Find<MainMenu>().IsOpen, "Escape did not go back to the menu");
            Assert.IsFalse(Find<LevelSelectPanel>().IsShowing,
                "the Escape that left the settings also opened the level list");
        }

        // -----------------------------------------------------------------
        // Starting over
        // -----------------------------------------------------------------

        /// <summary>
        /// Saying yes forgets the exercises -- the levels' solves, boards, milestones and hints -- on disk as well as in
        /// memory, and puts the player on the first level with nothing on it. Circuits are kept (Part 15g): see
        /// <c>WorkbenchCanvasPlayTests</c>.
        /// </summary>
        [UnityTest]
        public IEnumerator ConfirmingTheReset_EmptiesTheSave_AndStartsOnTheFirstLevel()
        {
            yield return TestScene.Load();
            yield return APlayerWithProgress();
            Find<MainMenu>().Show(true);
            yield return null;
            yield return OpenSettings();

            yield return Click(SettingsPanel.ResetButton);
            yield return Click(SettingsPanel.ConfirmButton);

            LevelSession session = Find<LevelSession>();
            ProgressStore store = Find<ProgressTracker>().Store;

            Assert.AreEqual(0, store.CompletedCount, "solved levels survived the reset");
            Assert.IsFalse(store.HasMilestone(TutorialLevel.Key), "the tutorial is still marked seen");
            Assert.IsFalse(store.HasMilestone(ChapterCard.Milestone), "the chapter card is still marked seen");
            Assert.IsFalse(store.HasSeenHint(HintRules.Collision), "a hint is still marked read");
            Assert.IsNull(store.BoardFor(session.AvailableLevels[1]), "the second level's circuit survived");

            ProgressFile file = JsonUtility.FromJson<ProgressFile>(SaveGuard.Read());
            Assert.IsEmpty(file.completed, "the file still lists solved levels");
            Assert.IsEmpty(file.boards, "the file still holds a board -- the one on screen, saved on the way out");
            Assert.IsEmpty(file.milestones, "the file still holds a milestone");
            Assert.IsEmpty(file.hintsSeen, "the file still lists a hint as read");

            Assert.AreEqual(session.AvailableLevels[0], session.LevelName, "the reset left the player off the first level");
            Assert.IsTrue(session.Blueprint.IsEmpty, "the first level came back with a circuit on it");

            Assert.IsTrue(Find<SettingsPanel>().IsOpen, "the settings closed without saying what happened");
            OnScreen(SettingsPanel.ResetButton);
        }

        /// <summary>
        /// After a reset the tutorial offers itself again, even though it already ran this session.
        /// </summary>
        /// <remarks>
        /// It offers itself once a session, so a player who walked away from it is not put back in
        /// it. A reset is the player asking to be new, and the milestone going is only half of that.
        /// </remarks>
        [UnityTest]
        public IEnumerator AfterAReset_TheTutorialOffersItselfAgain()
        {
            yield return TestScene.Load();

            TutorialDirector director = Find<TutorialDirector>();
            LevelSession session = Find<LevelSession>();

            // Seen this session and walked away from: the case that would otherwise never offer again.
            Find<MainMenu>().Show(false);
            yield return null;
            director.Begin();
            yield return null;
            session.LoadLevel(session.AvailableLevels[0]);
            yield return null;
            Find<ProgressTracker>().Store.MarkMilestone(TutorialLevel.Key);

            Find<MainMenu>().Show(true);
            yield return null;
            yield return OpenSettings();
            yield return Click(SettingsPanel.ResetButton);
            yield return Click(SettingsPanel.ConfirmButton);
            yield return Click(SettingsPanel.BackButton);

            Assert.AreEqual("START", StartLabel(), "the menu does not read like a new game's");

            yield return Click("Continue");

            for (int frame = 0; frame < 10 && !director.IsRunning; frame++)
                yield return null;

            Assert.IsTrue(director.IsRunning, "the tutorial did not offer itself after the reset");
            Assert.AreEqual(TutorialLevel.Key, session.LevelName, "the tutorial's board did not load");
        }

        private static string StartLabel()
        {
            GameObject button = GameObject.Find("Continue");
            Assert.IsNotNull(button, "the menu has no Continue row");
            return button.GetComponentInChildren<TextMeshProUGUI>().text;
        }

        /// <summary>
        /// After a reset free play opens on the setup it had: its boards are the student's, and a reset starts only the
        /// exercises over (Part 15g). It opened on the default setup while a reset emptied the whole save.
        /// </summary>
        [UnityTest]
        public IEnumerator AfterAReset_FreePlayKeepsItsSetup()
        {
            yield return TestScene.Load();

            LevelSession session = Find<LevelSession>();
            SandboxPanel sandbox = Find<SandboxPanel>();

            Find<MainMenu>().Show(false);
            yield return null;
            sandbox.Open();
            yield return null;
            yield return null;

            int opening = session.Level.Fixtures.Count;

            OnSetup("Sources", "+").onClick.Invoke();
            yield return null;
            yield return null;
            Assert.AreEqual(opening + 1, session.Level.Fixtures.Count, "sanity: the extra source did not appear");

            Assert.IsTrue(Find<ProgressTracker>().ResetProgress(), "sanity: the reset did not run");
            yield return null;

            sandbox.Open();
            yield return null;
            yield return null;

            Assert.AreEqual(SandboxLevel.Key, session.LevelName, "sanity: free play did not reopen");
            Assert.AreEqual(opening + 1, session.Level.Fixtures.Count,
                "free play lost its setup in the reset");
        }

        /// <summary>The - or + on a setup row, found by sitting at the height of its caption.</summary>
        private static Button OnSetup(string caption, string glyph)
        {
            GameObject root = GameObject.Find("Sandbox setup");
            Assert.IsNotNull(root, "sanity: the setup panel is not showing");

            float row = float.NaN;
            foreach (TextMeshProUGUI label in root.GetComponentsInChildren<TextMeshProUGUI>())
            {
                if (label.text == caption)
                    row = label.rectTransform.anchoredPosition.y;
            }

            foreach (Button button in root.GetComponentsInChildren<Button>())
            {
                if (button.name == $"step {glyph}"
                    && Mathf.Abs(button.GetComponent<RectTransform>().anchoredPosition.y - row) < 0.5f)
                {
                    return button;
                }
            }

            Assert.Fail($"no '{glyph}' on the {caption} row");
            return null;
        }
    }
}
