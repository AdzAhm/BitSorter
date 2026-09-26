using System.Collections;
using NUnit.Framework;
using BitSorter.View;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace BitSorter.PlayMode.Tests
{
    /// <summary>
    /// The settings screen, and above all its reset: that it asks, that saying no costs nothing, and
    /// that saying yes leaves a game a new player would recognise.
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

        /// <summary>A button on screen by its object name; fails if it is not showing.</summary>
        private static Button OnScreen(string name)
        {
            GameObject found = GameObject.Find(name);
            Assert.IsNotNull(found, $"no '{name}' button on screen");
            return found.GetComponent<Button>();
        }

        private static IEnumerator Click(string name)
        {
            OnScreen(name).onClick.Invoke();
            yield return null;
            yield return null;
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
        /// Settings is a row of the main menu, has its three headed sections, and BACK returns to
        /// the menu rather than to the board.
        /// </summary>
        [UnityTest]
        public IEnumerator Settings_OpensFromTheMenu_WithItsSections_AndGoesBackToIt()
        {
            yield return TestScene.Load();
            yield return OpenSettings();

            Assert.IsFalse(Find<MainMenu>().IsOpen, "the menu stayed up under the settings");

            var shown = new System.Collections.Generic.HashSet<string>();
            foreach (TextMeshProUGUI label in GameObject.Find("Settings").GetComponentsInChildren<TextMeshProUGUI>())
                shown.Add(label.text);

            foreach (string heading in SettingsPanel.Headings)
                Assert.IsTrue(shown.Contains(heading), $"the {heading} section has no heading");

            yield return Click(SettingsPanel.BackButton);

            Assert.IsFalse(Find<SettingsPanel>().IsOpen, "BACK did not close the settings");
            Assert.IsTrue(Find<MainMenu>().IsOpen, "BACK left the player on the board instead of the menu");
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

            Slider volume = VolumeSlider();
            Assert.IsFalse(GameAudio.Muted, "sanity: a fresh machine starts with the sound on");
            Assert.IsTrue(volume.interactable, "the volume is locked with the sound on");

            yield return Click(SettingsPanel.SoundButton);

            Assert.IsTrue(GameAudio.Muted, "sanity: the sound switch should have turned the sound off");
            Assert.IsFalse(volume.interactable, "the volume can still be moved with the sound off");
            Assert.AreEqual(UiTheme.TextDim, volume.handleRect.GetComponent<Image>().color,
                "the volume's handle is not greyed out with the sound off");

            yield return Click(SettingsPanel.SoundButton);

            Assert.IsFalse(GameAudio.Muted);
            Assert.IsTrue(volume.interactable, "the volume stayed locked once the sound was back on");
            Assert.AreEqual(UiTheme.Text, volume.handleRect.GetComponent<Image>().color,
                "the volume's handle stayed grey once the sound was back on");
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

            Slider volume = VolumeSlider();
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

            Button fullscreen = OnScreen(SettingsPanel.FullscreenButton);
            StringAssert.StartsWith("FULLSCREEN", fullscreen.GetComponentInChildren<TextMeshProUGUI>().text);
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

            GameObject roll = GameObject.Find("Credits/roll");
            GameObject help = GameObject.Find("Credits/help");
            Assert.IsNotNull(roll, "sanity: the roll should be on screen");
            Assert.IsNotNull(help, "the credits do not say how to leave them");

            Assert.GreaterOrEqual(ScreenRect(help.GetComponent<RectTransform>()).xMin,
                ScreenRect(roll.GetComponent<RectTransform>()).xMax,
                "the help line sits in the column the roll rises through");
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
        /// The sections fit between the BACK button and the help line, whatever the window gives
        /// them.
        /// </summary>
        /// <remarks>
        /// The canvas scales halfway between width and height, so a wide or short window has less
        /// than 1080 to give, and Settings has no scroll. It shrinks to fit rather than running under
        /// the BACK button and off the bottom.
        /// </remarks>
        [UnityTest]
        public IEnumerator TheSections_FitBetweenTheBackButtonAndTheHelpLine()
        {
            yield return TestScene.Load();
            yield return OpenSettings();
            yield return null;

            Transform root = GameObject.Find("Settings").transform;
            Rect title = ScreenRect(root.Find("sections/title").GetComponent<RectTransform>());
            Rect help = ScreenRect(root.Find("help").GetComponent<RectTransform>());
            Rect back = ScreenRect(OnScreen(SettingsPanel.BackButton));
            Rect credits = ScreenRect(OnScreen(SettingsPanel.CreditsButton));

            Assert.LessOrEqual(title.yMax, back.yMin + 1f, "the sections run up under the BACK button");
            Assert.GreaterOrEqual(credits.yMin, help.yMax - 1f, "the sections run down over the help line");
        }

        private static Slider VolumeSlider()
        {
            GameObject found = GameObject.Find(SettingsPanel.VolumeSlider);
            Assert.IsNotNull(found, "no volume slider on screen");
            return found.GetComponent<Slider>();
        }

        /// <summary>
        /// Each section's line fits its box. The box is measured from the text, so this guards the
        /// measuring being bypassed rather than any one line's length.
        /// </summary>
        [UnityTest]
        public IEnumerator EverySectionsDescription_FitsItsBox()
        {
            yield return TestScene.Load();
            yield return OpenSettings();

            foreach (TextMeshProUGUI label in GameObject.Find("Settings").GetComponentsInChildren<TextMeshProUGUI>())
            {
                if (label.name != "description")
                    continue;

                label.ForceMeshUpdate();
                Assert.IsFalse(label.isTextOverflowing, $"'{label.text}' runs out of its box");
                Assert.LessOrEqual(label.preferredHeight, label.rectTransform.rect.height + 0.5f,
                    $"'{label.text}' is taller than its box");
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
        /// Saying yes empties the save, on disk as well as in memory, and puts the player on the
        /// first level with nothing on it.
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
        /// After a reset free play opens on the default setup, not the one held from before.
        /// </summary>
        [UnityTest]
        public IEnumerator AfterAReset_FreePlayOpensOnItsDefaultSetup()
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
            Assert.AreEqual(opening, session.Level.Fixtures.Count,
                "free play came back with the setup from before the reset");
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
