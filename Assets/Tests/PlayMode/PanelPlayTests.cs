using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using BitSorter.View;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace BitSorter.PlayMode.Tests
{
    /// <summary>
    /// Full-screen panels: what one key press may do to them, and what shows through them.
    /// </summary>
    /// <remarks>
    /// Two panels reading one key in one frame each decide from what the other has done so far,
    /// and Unity does not define which of them updates first. The sandbox's setup closed on
    /// Escape and the level list opened on Escape "when nothing else is open" -- so whenever the
    /// setup updated first, one press closed it and opened the list, and the player's next click
    /// loaded a level. The tests here take both Updates out of Unity's hands and run them in the
    /// order that went wrong.
    ///
    /// The panel that closes is a stand-in rather than the sandbox's, so the rule is pinned for any
    /// panel that closes on a key, whatever the sandbox becomes.
    /// </remarks>
    [TestFixture]
    public class PanelPlayTests : InputTestFixture
    {
        /// <summary>The HUD pieces that must not show through a full-screen panel.</summary>
        private static readonly string[] HudRoots =
            { "Status", "Run controls", "Palette", "Help badge", MainMenu.HudButtonName };

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

            // InputTestFixture switches on the Input System's read-value caching, and a self-check
            // of that cache, which the game itself never runs with -- the project sets no input
            // feature flags. Once the game had already run earlier in the same play session, the
            // self-check fired on the queued Escape inside the Input System's own update, and the
            // error it logged failed both Escape tests with every assertion passing. Off, every read
            // comes straight from device state, which is how the shipped game reads it.
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

        /// <summary>The game boots into the main menu, which holds the screen until closed.</summary>
        private static IEnumerator CloseTheMainMenu()
        {
            Find<MainMenu>().Show(false);
            yield return null;
            yield return null;
        }

        // -----------------------------------------------------------------
        // One key, one panel
        // -----------------------------------------------------------------

        [UnityTest]
        public IEnumerator AnEscapeThatClosesOnePanel_DoesNotOpenAnother()
        {
            yield return TestScene.Load();
            yield return CloseTheMainMenu();

            Assert.IsFalse(UiModal.AnyOpen, "sanity: nothing should be open once the menu is closed");

            // The main menu is what Escape opens now; it was the level list until the keys swapped.
            MainMenu menu = Find<MainMenu>();
            menu.enabled = false;   // its Update is called by hand below

            var panel = new GameObject("Escape-closed panel").AddComponent<KeyClosedPanel>();
            panel.Open();

            yield return PressEscape();

            // The order Unity is free to choose, fixed to the one that went wrong: the panel that
            // closes looks first, so the menu finds nothing open.
            panel.Tick();
            FrameOf(menu)();

            Assert.IsFalse(panel.IsOpen, "sanity: Escape should have closed the stand-in panel");
            Assert.IsFalse(UiModal.AnyOpen,
                "one Escape closed a panel and opened the main menu as well");

            Release(_keyboard.escapeKey);
            menu.enabled = true;
        }

        /// <summary>
        /// The Enter that closes a panel does not also run the board under it.
        /// </summary>
        /// <remarks>
        /// The board's keys stood aside only while a panel was open, so "closed a moment ago" read
        /// the same as "never open". The chapter card closes on Enter, Enter is the run key, and the
        /// card sits on a level the player has not built anything on yet: whenever the card updated
        /// first, one press dismissed it and ran an empty board. The level list was taught this
        /// rule for Escape; the board was not.
        /// </remarks>
        [UnityTest]
        public IEnumerator AnEnterThatClosesAPanel_DoesNotAlsoRunTheBoard()
        {
            yield return TestScene.Load();
            yield return SkipTheTutorial();
            yield return CloseTheMainMenu();

            LevelSession session = Find<LevelSession>();
            SimulationInput keys = Find<SimulationInput>();
            keys.enabled = false;   // its Update is called by hand below

            Assert.AreNotEqual(RunState.Running, session.State, "sanity: nothing should be running yet");

            var panel = new GameObject("Enter-closed panel").AddComponent<KeyClosedPanel>();
            panel.ClosesOn = Key.Enter;
            panel.Open();

            yield return PressKey(_keyboard.enterKey);

            // The order that goes wrong: the panel looks first, so the board finds nothing open.
            panel.Tick();
            FrameOf(keys)();

            Assert.IsFalse(panel.IsOpen, "sanity: Enter should have closed the stand-in panel");
            Assert.AreNotEqual(RunState.Running, session.State,
                "one Enter closed a panel and ran the board under it as well");

            Release(_keyboard.enterKey);
            keys.enabled = true;
        }

        /// <summary>The positive control: with nothing open, Enter does run the board.</summary>
        [UnityTest]
        public IEnumerator AnEnterWithNothingOpen_StillRunsTheBoard()
        {
            yield return TestScene.Load();
            yield return SkipTheTutorial();
            yield return CloseTheMainMenu();

            LevelSession session = Find<LevelSession>();
            SimulationInput keys = Find<SimulationInput>();
            keys.enabled = false;

            yield return PressKey(_keyboard.enterKey);
            FrameOf(keys)();

            Assert.AreEqual(RunState.Running, session.State, "Enter with nothing open should run the board");

            Release(_keyboard.enterKey);
            keys.enabled = true;
        }

        /// <summary>
        /// Escape over the solved card closes the card and opens nothing, whichever of the card and
        /// the main menu Unity updates first.
        /// </summary>
        /// <remarks>
        /// Escape closes what is on top. Every card took it except the solved card, where it opened
        /// the level list over the card instead -- the main menu now, since the keys swapped. The
        /// card is not a modal, so the menu cannot hear about it from UiModal; both orders are run
        /// because each fails a different way if the two disagree about whose press it was.
        /// </remarks>
        [UnityTest]
        public IEnumerator AnEscapeOverTheSolvedCard_ClosesIt_WhenTheMenuLooksFirst() =>
            EscapeOverTheSolvedCard(menuFirst: true);

        /// <inheritdoc cref="AnEscapeOverTheSolvedCard_ClosesIt_WhenTheMenuLooksFirst"/>
        [UnityTest]
        public IEnumerator AnEscapeOverTheSolvedCard_ClosesIt_WhenTheCardLooksFirst() =>
            EscapeOverTheSolvedCard(menuFirst: false);

        private IEnumerator EscapeOverTheSolvedCard(bool menuFirst)
        {
            yield return TestScene.Load();
            yield return SkipTheTutorial();
            yield return CloseTheMainMenu();
            yield return SolveTheFirstLevel();

            WinPanel win = Find<WinPanel>();
            MainMenu menu = Find<MainMenu>();
            Assert.IsTrue(win.IsShowing, "sanity: solving the level should show the card");
            Assert.IsNotNull(GameObject.Find("Win"), "sanity: the card should be drawn");

            menu.enabled = false;   // both Updates are called by hand below
            win.enabled = false;

            yield return PressEscape();

            if (menuFirst)
            {
                FrameOf(menu)();
                FrameOf(win)();
            }
            else
            {
                FrameOf(win)();
                FrameOf(menu)();
            }

            Assert.IsFalse(win.IsShowing, "Escape did not close the solved card");
            Assert.IsFalse(menu.IsOpen, "one Escape closed the solved card and opened the main menu as well");

            Release(_keyboard.escapeKey);
            menu.enabled = true;
            win.enabled = true;
        }

        /// <summary>
        /// A card does not answer the key that was pressed before it was there.
        /// </summary>
        /// <remarks>
        /// Escape takes down the solved card, and at the end of the tutorial that raises the
        /// tutorial's own card in the same frame -- a card that finishes on Escape. Opened and then
        /// updated in that frame, it used to take the same press and finish the tutorial unseen.
        /// </remarks>
        [UnityTest]
        public IEnumerator ACardOpenedThisFrame_DoesNotTakeThisFramesEscape()
        {
            yield return TestScene.Load();
            yield return SkipTheTutorial();
            yield return CloseTheMainMenu();

            TutorialCard card = Find<TutorialCard>();
            card.enabled = false;

            yield return PressEscape();

            card.Show(true);
            FrameOf(card)();

            // Finishing is a flag the director collects, so that is what is read, not the card.
            Assert.IsFalse(card.ConsumeFinish(), "the card took the Escape that was pressed before it opened");

            Release(_keyboard.escapeKey);
            card.enabled = true;
            card.Show(false);
        }

        /// <summary>The positive control: a card that was already up does take Escape.</summary>
        [UnityTest]
        public IEnumerator ACardAlreadyUp_TakesEscape()
        {
            yield return TestScene.Load();
            yield return SkipTheTutorial();
            yield return CloseTheMainMenu();

            TutorialCard card = Find<TutorialCard>();
            card.enabled = false;
            card.Show(true);
            yield return null;

            yield return PressEscape();
            FrameOf(card)();

            Assert.IsTrue(card.ConsumeFinish(), "Escape did not finish a card that was already up");

            Release(_keyboard.escapeKey);
            card.enabled = true;
            card.Show(false);
        }

        /// <summary>
        /// The positive control: with nothing to close, Escape does open the main menu.
        /// </summary>
        /// <remarks>
        /// Without this the tests above could pass by the menu never reacting to a hand-made Escape
        /// at all.
        /// </remarks>
        [UnityTest]
        public IEnumerator AnEscapeWithNothingOpen_OpensTheMainMenu()
        {
            yield return TestScene.Load();
            yield return CloseTheMainMenu();

            MainMenu menu = Find<MainMenu>();
            menu.enabled = false;

            yield return PressEscape();
            FrameOf(menu)();

            Assert.IsTrue(menu.IsOpen, "Escape with nothing open should open the main menu");

            Release(_keyboard.escapeKey);
            menu.enabled = true;
        }

        /// <summary>Escape on the main menu closes it, back to the board -- what M used to do.</summary>
        [UnityTest]
        public IEnumerator AnEscapeOnTheMainMenu_ClosesIt()
        {
            yield return TestScene.Load();

            MainMenu menu = Find<MainMenu>();
            Assert.IsTrue(menu.IsOpen, "sanity: the game boots into the main menu");
            yield return null;   // not the frame it opened on

            HoldUpdate(menu);
            yield return PressEscape();
            FrameOf(menu)();

            Assert.IsFalse(menu.IsOpen, "Escape on the main menu did not close it");

            Release(_keyboard.escapeKey);
            menu.enabled = true;
        }

        /// <summary>M with nothing open opens the level list, and M on the list closes it again.</summary>
        [UnityTest]
        public IEnumerator M_OpensAndClosesTheLevelList()
        {
            yield return TestScene.Load();
            yield return CloseTheMainMenu();

            LevelSelectPanel levels = Find<LevelSelectPanel>();
            HoldUpdate(levels);

            yield return PressKey(_keyboard.mKey);
            FrameOf(levels)();
            Assert.IsTrue(levels.IsShowing, "M with nothing open should open the level list");
            Release(_keyboard.mKey);
            yield return null;

            yield return PressKey(_keyboard.mKey);
            FrameOf(levels)();
            Assert.IsFalse(levels.IsShowing, "M on the level list should close it");

            Release(_keyboard.mKey);
            levels.enabled = true;
        }

        /// <summary>M does not open the main menu any more, and does not stack the list on it.</summary>
        [UnityTest]
        public IEnumerator M_OnTheMainMenu_OpensNothing()
        {
            yield return TestScene.Load();
            yield return null;

            MainMenu menu = Find<MainMenu>();
            LevelSelectPanel levels = Find<LevelSelectPanel>();
            Assert.IsTrue(menu.IsOpen, "sanity: the game boots into the main menu");

            HoldUpdate(menu);
            HoldUpdate(levels);

            yield return PressKey(_keyboard.mKey);
            FrameOf(menu)();
            FrameOf(levels)();

            Assert.IsTrue(menu.IsOpen, "M closed the main menu, which is Escape's job now");
            Assert.IsFalse(levels.IsShowing, "M stacked the level list on top of the main menu");

            Release(_keyboard.mKey);
            menu.enabled = true;
            levels.enabled = true;
        }

        /// <summary>
        /// The Escape that closes the level list does not open the main menu behind it, whichever
        /// of the two Unity updates first.
        /// </summary>
        [UnityTest]
        public IEnumerator AnEscapeThatClosesTheLevelList_OpensNothing_WhenTheMenuLooksFirst() =>
            EscapeOutOfTheList(menuFirst: true);

        /// <inheritdoc cref="AnEscapeThatClosesTheLevelList_OpensNothing_WhenTheMenuLooksFirst"/>
        [UnityTest]
        public IEnumerator AnEscapeThatClosesTheLevelList_OpensNothing_WhenTheListLooksFirst() =>
            EscapeOutOfTheList(menuFirst: false);

        private IEnumerator EscapeOutOfTheList(bool menuFirst)
        {
            yield return TestScene.Load();
            yield return CloseTheMainMenu();

            MainMenu menu = Find<MainMenu>();
            LevelSelectPanel levels = Find<LevelSelectPanel>();
            levels.Open();
            yield return null;
            yield return null;
            Assert.IsTrue(levels.IsShowing, "sanity: the list should be open");

            HoldUpdate(menu);
            HoldUpdate(levels);

            yield return PressEscape();

            if (menuFirst)
            {
                FrameOf(menu)();
                FrameOf(levels)();
            }
            else
            {
                FrameOf(levels)();
                FrameOf(menu)();
            }

            Assert.IsFalse(levels.IsShowing, "Escape did not close the level list");
            Assert.IsFalse(menu.IsOpen, "one Escape closed the level list and opened the main menu as well");

            Release(_keyboard.escapeKey);
            menu.enabled = true;
            levels.enabled = true;
        }

        /// <summary>
        /// The Escape that leaves Settings lands on the main menu and leaves it up, whichever of the
        /// two Unity updates first.
        /// </summary>
        /// <remarks>
        /// Settings goes back to the menu on Escape, and the menu closes on Escape: whenever the
        /// menu looked after Settings had reopened it, the one press went back and straight out
        /// again, onto the board. The menu ignores Escape on the frame it opened.
        /// </remarks>
        [UnityTest]
        public IEnumerator AnEscapeOutOfSettings_LeavesTheMenuUp_WhenTheMenuLooksFirst() =>
            EscapeOutOfSettings(menuFirst: true);

        /// <inheritdoc cref="AnEscapeOutOfSettings_LeavesTheMenuUp_WhenTheMenuLooksFirst"/>
        [UnityTest]
        public IEnumerator AnEscapeOutOfSettings_LeavesTheMenuUp_WhenSettingsLooksFirst() =>
            EscapeOutOfSettings(menuFirst: false);

        private IEnumerator EscapeOutOfSettings(bool menuFirst)
        {
            yield return TestScene.Load();

            MainMenu menu = Find<MainMenu>();
            SettingsPanel settings = Find<SettingsPanel>();

            GameObject.Find("Settings").GetComponent<Button>().onClick.Invoke();
            yield return null;
            yield return null;
            Assert.IsTrue(settings.IsOpen, "sanity: the Settings row should open the settings");

            HoldUpdate(menu);
            HoldUpdate(settings);

            yield return PressEscape();

            if (menuFirst)
            {
                FrameOf(menu)();
                FrameOf(settings)();
            }
            else
            {
                FrameOf(settings)();
                FrameOf(menu)();
            }

            Assert.IsFalse(settings.IsOpen, "Escape did not leave the settings");
            Assert.IsTrue(menu.IsOpen, "one Escape left the settings and closed the main menu as well");

            Release(_keyboard.escapeKey);
            menu.enabled = true;
            settings.enabled = true;
        }

        // -----------------------------------------------------------------
        // What shows through
        // -----------------------------------------------------------------

        /// <summary>
        /// While a full-screen panel is up, the HUD is not drawn beside it.
        /// </summary>
        /// <remarks>
        /// The panels' backdrop never reached the edges of the screen, which is where the HUD
        /// lives, so the banner, the run buttons and the parts list stayed fully lit next to a
        /// "full-screen" panel -- and the panels' own titles and help lines printed straight over
        /// the banner's and the buttons'.
        /// </remarks>
        [UnityTest]
        public IEnumerator WhileAFullScreenPanelIsOpen_TheHudIsNotDrawn()
        {
            yield return TestScene.Load();

            // Up means faded in: the HUD stays under a panel until the panel covers it, and each
            // piece of it steps aside in its own next update.
            yield return UntilNoFadeIsMoving();
            yield return null;

            Assert.IsTrue(UiModal.AnyOpen, "sanity: the game boots into the main menu");

            foreach (string name in HudRoots)
                Assert.IsNull(GameObject.Find(name), $"'{name}' is drawn beside the main menu");
        }

        /// <summary>
        /// A panel opened over the board fades in over the HUD, and the HUD goes once it is covered.
        /// </summary>
        /// <remarks>
        /// The HUD used to vanish in the frame the panel opened, while the panel was still almost
        /// transparent, so the board showed bare for a moment. Frame time is fixed so one frame is a
        /// known step into the fade however fast the editor renders.
        /// </remarks>
        [UnityTest]
        public IEnumerator APanelFadingIn_CoversTheHudBeforeTheHudGoes()
        {
            yield return TestScene.Load();
            yield return CloseTheMainMenu();

            try
            {
                Time.captureDeltaTime = 1f / 120f;

                Find<LevelSelectPanel>().Open();
                yield return null;

                Assert.IsTrue(UiFade.AnyMoving, "sanity: the level list should still be fading in");
                Assert.IsNotNull(GameObject.Find("Run controls"),
                    "the HUD went while the panel was still fading in, leaving the board bare");

                yield return UntilNoFadeIsMoving();
                yield return null;

                Assert.IsNull(GameObject.Find("Run controls"), "the HUD stayed up once the panel covered it");
            }
            finally
            {
                Time.captureDeltaTime = 0f;
            }
        }

        /// <summary>
        /// Going from one panel to another never brings the HUD back between them.
        /// </summary>
        /// <remarks>
        /// The main menu closes itself and then opens the level list, so for a moment nothing is
        /// open. A panel that took that moment for the HUD being up would fade in over a HUD that
        /// had flashed back on.
        /// </remarks>
        [UnityTest]
        public IEnumerator FromTheMenuToTheLevelList_TheHudNeverComesBack()
        {
            yield return TestScene.Load();
            yield return UntilNoFadeIsMoving();

            try
            {
                Time.captureDeltaTime = 1f / 120f;

                PressLevelsOnTheMenu();

                for (int frame = 0; frame < 40; frame++)
                {
                    yield return null;

                    foreach (string name in HudRoots)
                        Assert.IsNull(GameObject.Find(name), $"'{name}' came back between the menu and the level list");
                }

                Assert.IsTrue(Find<LevelSelectPanel>().IsShowing, "sanity: the level list should be up");
            }
            finally
            {
                Time.captureDeltaTime = 0f;
            }
        }

        private static IEnumerator UntilNoFadeIsMoving()
        {
            for (int frame = 0; frame < 600 && UiFade.AnyMoving; frame++)
                yield return null;

            Assert.IsFalse(UiFade.AnyMoving, "a panel never finished fading in");
        }

        private static void PressLevelsOnTheMenu()
        {
            GameObject levels = GameObject.Find("Levels");
            Assert.IsNotNull(levels, "sanity: the main menu has no LEVELS button");
            levels.GetComponent<Button>().onClick.Invoke();
        }

        /// <summary>
        /// The board has a button back to the main menu, and it works.
        /// </summary>
        /// <remarks>
        /// There was no button to the menu or the level list anywhere on a board, only keys, so a
        /// player using the mouse could not leave a level -- free play least of all, whose setup panel
        /// offers nothing but collapsing. The menu is the way to everything else.
        /// </remarks>
        [UnityTest]
        public IEnumerator TheMenuButtonOnTheBoard_OpensTheMainMenu()
        {
            yield return TestScene.Load();
            yield return SkipTheTutorial();
            yield return CloseTheMainMenu();
            yield return UntilNoFadeIsMoving();

            MainMenu menu = Find<MainMenu>();
            Assert.IsFalse(menu.IsOpen, "sanity: the menu should be closed");

            GameObject button = GameObject.Find(MainMenu.HudButtonName);
            Assert.IsNotNull(button, "there is no menu button on the board");

            button.GetComponent<Button>().onClick.Invoke();
            yield return null;

            Assert.IsTrue(menu.IsOpen, "the board's menu button did not open the main menu");
        }

        /// <summary>
        /// The main menu says which version this is, and it is the build's own number.
        /// </summary>
        /// <remarks>
        /// Asked for so that a bug report can say which version it came from. Read from
        /// <c>Application.version</c>, never written into the menu, so a release cannot forget it.
        /// </remarks>
        [UnityTest]
        public IEnumerator TheMainMenu_SaysWhichVersionThisIs()
        {
            yield return TestScene.Load();
            yield return null;

            Assert.IsTrue(Find<MainMenu>().IsOpen, "sanity: the game boots into the main menu");

            GameObject label = GameObject.Find("version");
            Assert.IsNotNull(label, "the main menu shows no version");

            string shown = label.GetComponent<TMPro.TextMeshProUGUI>().text;
            Assert.AreEqual("v" + Application.version, shown, "the menu shows a version that is not this build's");
        }

        /// <summary>The positive control: the same lookups find the HUD once nothing is open.</summary>
        [UnityTest]
        public IEnumerator WithNothingOpen_TheHudIsDrawn()
        {
            yield return TestScene.Load();
            yield return CloseTheMainMenu();

            foreach (string name in HudRoots)
                Assert.IsNotNull(GameObject.Find(name), $"'{name}' is missing with nothing open");
        }

        // -----------------------------------------------------------------
        // What a panel's backdrop may be drawn on
        // -----------------------------------------------------------------

        /// <summary>
        /// The panel sprite is never drawn smaller than its two corners, on the menu or the HUD.
        /// </summary>
        /// <remarks>
        /// It is nine-sliced with ten-pixel corners, and anything shorter than two of them squeezes
        /// the corners together instead of repeating a middle -- so CLAUDE.md has that anything
        /// shorter replaces the sprite, as the help badge and the clock's pips do. The menu's rule
        /// under the title did not: two pixels tall, it drew as a squashed rounded slab, which read
        /// as a line only because a filled slab is one colour throughout. A panel with a drawn edge
        /// has an edge and a middle, and the rule vanished into two dots.
        /// </remarks>
        [UnityTest]
        public IEnumerator APanelBackdrop_IsNeverSmallerThanItsCorners()
        {
            yield return TestScene.Load();
            yield return null;

            int onTheMenu = PanelsTooSmallForTheirCorners(out string menuReport);
            Assert.Greater(onTheMenu, 0, "sanity: the main menu draws panels");
            Assert.IsEmpty(menuReport, "on the main menu");

            yield return CloseTheMainMenu();

            int onTheHud = PanelsTooSmallForTheirCorners(out string hudReport);
            Assert.Greater(onTheHud, 0, "sanity: the HUD draws panels");
            Assert.IsEmpty(hudReport, "on the HUD");
        }

        /// <summary>
        /// Checks every panel on screen against its corners; returns how many it checked, and names
        /// any that are too small.
        /// </summary>
        private static int PanelsTooSmallForTheirCorners(out string report)
        {
            // The sprite Panel_ draws with in the current look, filled or bordered.
            Sprite panel = ProceduralSprites.Panel(Look.Current.Panels);
            var failures = new System.Text.StringBuilder();
            int checkedPanels = 0;

            foreach (Image image in Object.FindObjectsByType<Image>(FindObjectsSortMode.None))
            {
                if (image.sprite != panel || !image.isActiveAndEnabled)
                    continue;

                checkedPanels++;
                Vector2 size = image.rectTransform.rect.size;
                float tall = (panel.border.y + panel.border.w) / image.pixelsPerUnit;
                float wide = (panel.border.x + panel.border.z) / image.pixelsPerUnit;

                if (size.y < tall || size.x < wide)
                    failures.Append($"'{image.name}' is {size.x} by {size.y}, and its corners need {wide} by {tall}. ");
            }

            report = failures.ToString();
            return checkedPanels;
        }

        // -----------------------------------------------------------------
        // What a button is for
        // -----------------------------------------------------------------

        /// <summary>
        /// The run row asks for RUN: it is the one solid button on the row, and CLEAR ALL is drawn
        /// as the one that throws work away.
        /// </summary>
        /// <remarks>
        /// Against the game's own look, whose panels are outlined. Under Classic every panel is
        /// solid and every button the same colour, so a row that had lost its roles would pass.
        /// </remarks>
        [UnityTest]
        public IEnumerator TheRunRow_DrawsEachButtonAsWhatItIsFor()
        {
            yield return TestScene.Load();
            yield return CloseTheMainMenu();

            Assume.That(Look.Current.Panels, Is.EqualTo(PanelStyle.Bordered),
                "this needs a look whose panels are outlined");

            Transform row = GameObject.Find("Run controls").transform;
            Sprite solid = ProceduralSprites.Panel(PanelStyle.Filled);

            foreach (string name in new[] { "Run", "Reset", "Undo", "Redo", "Clear" })
            {
                Image background = row.Find(name).GetComponent<Image>();
                bool primary = name == "Run";

                Assert.AreEqual(primary, background.sprite == solid,
                    primary ? "RUN is not drawn solid" : $"{name} is drawn solid, like the primary button");

                ButtonRole role = primary ? ButtonRole.Primary
                    : name == "Clear" ? ButtonRole.Destructive
                    : ButtonRole.Secondary;
                Assert.AreEqual(UiTheme.FillOf(role), background.color, $"{name} is not drawn as {role}");
            }
        }

        /// <summary>
        /// A button on the run row that cannot be pressed looks it: its caption dims with it.
        /// </summary>
        /// <remarks>
        /// The row switched buttons off with <c>interactable</c> alone, which tints the background
        /// and leaves the caption bright -- so UNDO with nothing to undo, and RUN in the middle of a
        /// run, still read as buttons to press. <see cref="UiTheme.SetEnabled"/> exists for exactly
        /// this and the row never called it.
        ///
        /// RESET is the pair: always live on a loaded level, so a caption dimmed regardless would
        /// fail here rather than pass.
        /// </remarks>
        [UnityTest]
        public IEnumerator ADeadButtonOnTheRunRow_DimsItsCaption()
        {
            yield return TestScene.Load();
            yield return CloseTheMainMenu();

            Transform row = GameObject.Find("Run controls").transform;
            Button undo = row.Find("Undo").GetComponent<Button>();
            Button reset = row.Find("Reset").GetComponent<Button>();

            Assume.That(undo.interactable, Is.False, "a board nothing has been done to has nothing to undo");
            Assume.That(reset.interactable, Is.True, "a loaded level can always be reset");

            Assert.AreEqual(UiTheme.TextDim, CaptionOf(undo).color,
                "UNDO cannot be pressed, and its caption says it can");
            Assert.AreEqual(UiTheme.Text, CaptionOf(reset).color, "RESET can be pressed, and is dimmed");
        }

        private static TMPro.TextMeshProUGUI CaptionOf(Button button) =>
            button.GetComponentInChildren<TMPro.TextMeshProUGUI>();

        // -----------------------------------------------------------------
        // A panel coming in
        // -----------------------------------------------------------------

        /// <summary>
        /// A full-screen panel fades in, and takes a click while it is still fading.
        /// </summary>
        /// <remarks>
        /// The promise that matters is the second one. The panel covers the board from its first
        /// frame, so if it refused clicks while it faded a press would reach nothing at all -- a
        /// click the player made and the game ignored, with nothing on screen to say why.
        ///
        /// Frame time is fixed for the length of the test, so one frame is a known step into the
        /// fade however fast the editor is rendering: in the background it renders few frames a
        /// second, and a single real frame could carry the whole fade.
        ///
        /// One frame is let through before the raycast because Unity only hit-tests a graphic its
        /// canvas has drawn at least once -- true of every panel, faded or not.
        /// </remarks>
        [UnityTest]
        public IEnumerator AFadingPanel_TakesAClickWhileItFades()
        {
            yield return TestScene.Load();
            yield return CloseTheMainMenu();

            try
            {
                Time.captureDeltaTime = 1f / 120f;

                Find<LevelSelectPanel>().Open();

                // The panel builds itself on the canvas, not under its own component.
                Transform list = GameObject.Find("Level select").transform;
                Assert.IsTrue(list.TryGetComponent(out UiFade fade), "the level list appeared without fading in");
                Assert.AreEqual(0f, fade.Alpha, "a panel appearing should start from nothing");

                yield return null;

                Assert.Less(fade.Alpha, 1f, "sanity: one fixed frame in, the panel should still be fading");

                Button row = FirstLevelRow(list);
                var pointer = new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current)
                {
                    position = RectTransformUtility.WorldToScreenPoint(null, row.transform.position),
                };
                var hits = new System.Collections.Generic.List<UnityEngine.EventSystems.RaycastResult>();
                UnityEngine.EventSystems.EventSystem.current.RaycastAll(pointer, hits);

                Assert.IsNotEmpty(hits, "a click on a fading panel reached nothing");
                Assert.IsTrue(hits[0].gameObject.transform.IsChildOf(row.transform),
                    $"a click on a fading level row landed on {hits[0].gameObject.name} instead");

                for (int frame = 0; frame < 240 && UiFade.AnyMoving; frame++)
                    yield return null;

                Assert.AreEqual(1f, fade.Alpha, "the panel never finished fading in");
            }
            finally
            {
                Time.captureDeltaTime = 0f;
            }
        }

        private static Button FirstLevelRow(Transform list)
        {
            foreach (Button button in list.GetComponentsInChildren<Button>())
            {
                if (button.name.StartsWith("Level "))
                    return button;
            }

            Assert.Fail("sanity: the level list has no level rows");
            return null;
        }

        // -----------------------------------------------------------------
        // What is already on screen when a panel opens
        // -----------------------------------------------------------------

        /// <summary>
        /// Escape over a first-time hint dismisses the hint and opens nothing, whichever of the hint
        /// and the main menu Unity updates first.
        /// </summary>
        /// <remarks>
        /// Escape closes what is on top, and a hint is on top of the board. It dismissed the hint
        /// and opened the level list in the same press -- the main menu, once the keys swapped,
        /// which takes the whole screen. The hint is not a modal, so the menu cannot hear about it
        /// from UiModal; the solved card had the same gap and closed it the same way.
        /// </remarks>
        [UnityTest]
        public IEnumerator AnEscapeOverAHint_DismissesItAndOpensNothing_WhenTheMenuLooksFirst() =>
            EscapeOverAHint(menuFirst: true);

        /// <inheritdoc cref="AnEscapeOverAHint_DismissesItAndOpensNothing_WhenTheMenuLooksFirst"/>
        [UnityTest]
        public IEnumerator AnEscapeOverAHint_DismissesItAndOpensNothing_WhenTheHintLooksFirst() =>
            EscapeOverAHint(menuFirst: false);

        private IEnumerator EscapeOverAHint(bool menuFirst)
        {
            yield return TestScene.Load();
            yield return SkipTheTutorial();
            yield return CloseTheMainMenu();

            HintBanner banner = Find<HintBanner>();
            MainMenu menu = Find<MainMenu>();

            banner.Show("Gates fire only when every input is holding a bit.");

            // Past the moment in which the press that raised a hint cannot dismiss it. Waited on,
            // not timed: a frame cap that fails loudly, never a number of seconds.
            for (int frame = 0; frame < 600 && !PastGrace(banner); frame++)
                yield return null;

            Assert.IsTrue(PastGrace(banner), "sanity: the hint never got past its grace period");
            Assert.IsTrue(banner.IsShowing, "sanity: the hint should still be up");

            HoldUpdate(menu);
            HoldUpdate(banner);

            yield return PressEscape();

            if (menuFirst)
            {
                FrameOf(menu)();
                FrameOf(banner)();
            }
            else
            {
                FrameOf(banner)();
                FrameOf(menu)();
            }

            Assert.IsFalse(banner.IsShowing, "Escape did not dismiss the hint");
            Assert.IsFalse(menu.IsOpen, "one Escape dismissed the hint and opened the main menu as well");

            Release(_keyboard.escapeKey);
            menu.enabled = true;
            banner.enabled = true;
        }

        /// <summary>Whether a hint has been up long enough for a press to dismiss it.</summary>
        private static bool PastGrace(HintBanner banner)
        {
            float Private(string name)
            {
                FieldInfo field = typeof(HintBanner).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.IsNotNull(field, $"HintBanner no longer has {name}");
                return (float)field.GetValue(banner);
            }

            return Private("_seconds") - Private("_remaining") > Private("_graceSeconds");
        }

        /// <summary>
        /// F2 pressed behind a full-screen panel toggles nothing; pressed on the board it toggles both
        /// readouts.
        /// </summary>
        /// <remarks>
        /// A panel over the board owns the keyboard -- every board key asks UiModal first -- and F2
        /// was the one that did not. Pressed on the main menu it did nothing visible, and the
        /// diagram and diagnostics then appeared from nowhere when the menu closed. The second half
        /// is the positive control, so the first cannot pass by F2 never being read at all.
        /// </remarks>
        [UnityTest]
        public IEnumerator F2BehindAFullScreenPanel_TogglesNothing()
        {
            yield return TestScene.Load();
            yield return SkipTheTutorial();
            yield return null;

            Assert.IsTrue(Find<MainMenu>().IsOpen, "sanity: the game boots into the main menu");

            ClockDiagram diagram = Find<ClockDiagram>();
            DiagnosticsPanel diagnostics = Find<DiagnosticsPanel>();

            yield return PressKey(_keyboard.f2Key);
            Release(_keyboard.f2Key);
            yield return null;

            Assert.IsFalse(Shown(diagram), "F2 behind the main menu switched the timing diagram on");
            Assert.IsFalse(Shown(diagnostics), "F2 behind the main menu switched diagnostics on");

            yield return CloseTheMainMenu();

            yield return PressKey(_keyboard.f2Key);
            Release(_keyboard.f2Key);
            yield return null;

            Assert.IsTrue(Shown(diagram), "F2 on the board did not switch the timing diagram on");
            Assert.IsTrue(Shown(diagnostics), "F2 on the board did not switch diagnostics on");
        }

        /// <summary>Whether a toggled readout is switched on, which is private to it.</summary>
        private static bool Shown(MonoBehaviour readout)
        {
            FieldInfo field = readout.GetType().GetField("_shown", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, $"{readout.GetType().Name} no longer keeps its state in _shown");
            return (bool)field.GetValue(readout);
        }

        /// <summary>
        /// Escape with the help panel open closes the help and opens nothing, whichever of the two
        /// Unity updates first.
        /// </summary>
        /// <remarks>
        /// Escape closes what is on top, and the open help panel is on top of the board. Once
        /// Escape became the main menu's key, pressing it to put the help away covered the whole
        /// screen with the menu instead.
        /// </remarks>
        [UnityTest]
        public IEnumerator AnEscapeWithTheHelpOpen_ClosesItAndOpensNothing_WhenTheMenuLooksFirst() =>
            EscapeOverTheHelp(menuFirst: true);

        /// <inheritdoc cref="AnEscapeWithTheHelpOpen_ClosesItAndOpensNothing_WhenTheMenuLooksFirst"/>
        [UnityTest]
        public IEnumerator AnEscapeWithTheHelpOpen_ClosesItAndOpensNothing_WhenTheHelpLooksFirst() =>
            EscapeOverTheHelp(menuFirst: false);

        private IEnumerator EscapeOverTheHelp(bool menuFirst)
        {
            yield return TestScene.Load();
            yield return SkipTheTutorial();
            yield return CloseTheMainMenu();

            HelpPanel help = Find<HelpPanel>();
            MainMenu menu = Find<MainMenu>();

            yield return PressKey(_keyboard.hKey);
            Release(_keyboard.hKey);
            yield return null;
            Assert.IsTrue(Shown(help), "sanity: H should open the help panel");

            HoldUpdate(menu);
            HoldUpdate(help);

            yield return PressEscape();

            if (menuFirst)
            {
                FrameOf(menu)();
                FrameOf(help)();
            }
            else
            {
                FrameOf(help)();
                FrameOf(menu)();
            }

            Assert.IsFalse(Shown(help), "Escape did not close the help panel");
            Assert.IsFalse(menu.IsOpen, "one Escape closed the help and opened the main menu as well");

            Release(_keyboard.escapeKey);
            menu.enabled = true;
            help.enabled = true;
        }

        /// <summary>
        /// The refusal toast grows to fit a long refusal, and keeps its width for a short one.
        /// </summary>
        /// <remarks>
        /// It was a fixed 520 wide with its text at Label. Set larger after a playtest, the longest
        /// refusal -- the tutorial asking for START or SKIP -- needs about 530, so the box is
        /// measured to the text instead of guessed at.
        /// </remarks>
        [UnityTest]
        public IEnumerator TheRefusalToast_FitsItsRefusal()
        {
            yield return TestScene.Load();
            yield return CloseTheMainMenu();

            SimulationRunner runner = Find<SimulationRunner>();

            runner.RejectEdit("A refusal written to be longer than any the game says, so the toast has to grow.");
            yield return null;
            yield return null;

            GameObject toast = GameObject.Find("Toast");
            Assert.IsNotNull(toast, "a refusal put no toast on screen");

            var text = toast.GetComponentInChildren<TMPro.TextMeshProUGUI>();
            float box = toast.GetComponent<RectTransform>().rect.width;

            Assert.LessOrEqual(text.preferredWidth, text.rectTransform.rect.width + 0.5f,
                $"the refusal needs {text.preferredWidth:F0}px and the toast gives it {text.rectTransform.rect.width:F0}px");
            Assert.Greater(box, UiTheme.ToastMinimumWidth, "sanity: a refusal this long should have made the toast grow");

            runner.RejectEdit("Off the board.");
            yield return null;
            yield return null;

            Assert.AreEqual(UiTheme.ToastMinimumWidth, toast.GetComponent<RectTransform>().rect.width, 0.5f,
                "a short refusal left the toast at the long one's width");
        }

        /// <summary>
        /// A hint up when a full-screen panel opens is held, not spent.
        /// </summary>
        /// <remarks>
        /// A first-time hint is marked seen the moment it is raised, not when it is read:
        /// FirstTimeHints.TryShow calls MarkHintSeen and only then calls Show. There are four of
        /// these hints in the whole game and each fires once ever per save, so nine seconds
        /// running out behind a level list is a mechanic the player is never told about again.
        ///
        /// FirstTimeHints already refuses to raise one while a panel is open. Nothing covered the
        /// other order: a hint already up when the panel opens.
        /// </remarks>
        [UnityTest]
        public IEnumerator AHintUpWhenAPanelOpens_IsHeldRatherThanSpent()
        {
            yield return TestScene.Load();
            yield return CloseTheMainMenu();

            HintBanner banner = Find<HintBanner>();
            banner.Show("Gates fire only when every input is holding a bit.");
            yield return null;

            Assert.IsTrue(banner.IsShowing, "sanity: the hint should be up");
            Assert.IsNotNull(GameObject.Find("Hint"), "sanity: the hint should be drawn");

            float before = Remaining(banner);

            var panel = new GameObject("stand-in").AddComponent<KeyClosedPanel>();
            panel.Open();

            // Several frames, so a countdown that is still running has time to show it.
            for (int frame = 0; frame < 5; frame++)
                yield return null;

            Assert.IsNull(GameObject.Find("Hint"),
                "the hint is drawn over by the panel rather than stepping aside for it");

            Assert.AreEqual(before, Remaining(banner), 0.0001f,
                "the hint spent its nine seconds behind a full-screen panel -- it is marked seen, " +
                "so the player never gets it again");

            Assert.IsTrue(banner.IsShowing,
                "the hint gave up while it was covered, which is the same loss by another route");

            Object.DestroyImmediate(panel.gameObject);
        }

        /// <summary>
        /// The solved card and the bins lighting up both step aside for a full-screen panel.
        /// </summary>
        /// <remarks>
        /// The win panel is deliberately not a modal -- it is a card on the board, not a takeover --
        /// but that settles what it does to other panels, not what they do to it. It hid from
        /// nothing: it merely skipped coming to the front, so a scrim at 0.78 went over a live
        /// panel and the card bled through it. A run that passed while the level list was open was
        /// worse, activating the card behind the scrim at whatever sibling index it happened to
        /// hold, with no BringToFront ever to correct it.
        ///
        /// The celebration is the same fact on the board rather than on the canvas: glows pulsing
        /// at 2.6 Hz behind a menu are motion where the player is reading.
        /// </remarks>
        [UnityTest]
        public IEnumerator OverASolvedBoard_TheCardAndTheCelebration_StepAsideForAPanel()
        {
            yield return TestScene.Load();
            yield return SkipTheTutorial();
            yield return CloseTheMainMenu();

            yield return SolveTheFirstLevel();

            WinPanel win = Find<WinPanel>();
            Assert.IsTrue(win.IsShowing, "sanity: solving the level should show the card");
            Assert.IsNotNull(GameObject.Find("Win"), "sanity: the card should be drawn");
            Assert.Greater(LitGlows(), 0, "sanity: the bins should be lit");

            var panel = new GameObject("stand-in").AddComponent<KeyClosedPanel>();
            panel.Open();
            yield return null;

            Assert.IsNull(GameObject.Find("Win"),
                "the solved card is still drawn under the panel's scrim");

            Assert.AreEqual(0, LitGlows(), "the bins keep pulsing behind the panel");

            // It stepped aside rather than gave up: the director and the music both read IsShowing
            // to know the card is still owed, and a card that forgot itself here would let the
            // tutorial's ending card through early.
            Assert.IsTrue(win.IsShowing,
                "the card forgot it was owed rather than stepping aside for the panel");

            Object.DestroyImmediate(panel.gameObject);
            yield return null;

            Assert.IsNotNull(GameObject.Find("Win"), "the card did not come back when the panel closed");
        }

        // -----------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------

        /// <summary>
        /// Marks the tutorial done on the scratch save, so closing the menu does not launch it.
        /// </summary>
        /// <remarks>
        /// SaveGuard hands every fixture a fresh save, and a fresh save is exactly the condition
        /// the tutorial offers itself on -- it adopts its own board the moment the menu closes,
        /// which takes the level out from under anything loaded afterwards.
        /// </remarks>
        private static IEnumerator SkipTheTutorial()
        {
            Find<ProgressTracker>().Store.MarkMilestone(TutorialLevel.Key);
            yield return null;
        }

        /// <summary>How long a hint has left, which is private because nothing but a test needs it.</summary>
        private static float Remaining(HintBanner banner)
        {
            FieldInfo field = typeof(HintBanner).GetField(
                "_remaining", BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.IsNotNull(field, "HintBanner no longer keeps its countdown in _remaining");
            return (float)field.GetValue(banner);
        }

        /// <summary>The celebration glows that are currently drawn.</summary>
        private static int LitGlows()
        {
            GameObject container = GameObject.Find("Sink celebration");

            if (container == null)
                return 0;

            int lit = 0;

            foreach (SpriteRenderer glow in container.GetComponentsInChildren<SpriteRenderer>())
            {
                if (glow.gameObject.activeInHierarchy && glow.enabled)
                    lit++;
            }

            return lit;
        }

        /// <summary>
        /// Solves route-the-bit the intended way -- in, through a NOT, into binOne -- and lets the
        /// pass settle.
        /// </summary>
        private static IEnumerator SolveTheFirstLevel()
        {
            LevelSession session = Find<LevelSession>();
            SimulationRunner runner = Find<SimulationRunner>();

            Assert.IsTrue(session.LoadLevel("route-the-bit"), "the level did not load");
            yield return null;

            var middle = new Vector2Int(0, 0);
            Assert.IsTrue(session.TryPlaceGate(GateKind.Not, middle), "could not place the NOT gate");

            int gate = -1;

            for (int id = 0; id < runner.View.NodeCount; id++)
            {
                if (runner.TryCellOf(id, out Vector2Int at) && at == middle)
                    gate = id;
            }

            Assert.AreNotEqual(-1, gate, "nothing on the middle cell");

            Assert.IsTrue(session.TryConnect(
                new PortAddress(runner.FixtureNodeIds["in"], false, 0),
                new PortAddress(gate, true, 0)), "could not wire the source to the gate");

            Assert.IsTrue(session.TryConnect(
                new PortAddress(gate, false, 0),
                new PortAddress(runner.FixtureNodeIds["binOne"], true, 0)),
                "could not wire the gate to the bin");

            session.Run();

            for (int tick = 0; tick < 100 && !runner.IsIdle(); tick++)
                runner.StepOneTick();

            Assert.IsTrue(runner.IsIdle(), "the run never came to a standstill");

            // Two frames: one for the session to settle the run, one for the panel to see it.
            yield return null;
            yield return null;

            Assert.AreEqual(RunState.Passed, session.State, "sanity: the intended answer should pass");
        }

        /// <summary>
        /// Presses Escape and returns inside the frame that processed it.
        /// </summary>
        /// <remarks>
        /// Queued rather than applied on the spot, so it arrives the way a real key does: in the
        /// input update at the start of a frame. The coroutine resumes after that frame's Updates,
        /// which is still "this frame" as far as wasPressedThisFrame is concerned.
        /// </remarks>
        private IEnumerator PressEscape() => PressKey(_keyboard.escapeKey);

        /// <summary>Presses a key and returns inside the frame that processed it, as above.</summary>
        private IEnumerator PressKey(UnityEngine.InputSystem.Controls.KeyControl key)
        {
            Press(key, queueEventOnly: true);
            yield return null;

            Assert.IsTrue(key.wasPressedThisFrame, $"sanity: {key.name} is this frame's press");
        }

        /// <summary>
        /// Takes a component's Update out of Unity's hands, so the test calls it in the order it
        /// chooses -- and keeps a panel that is up registered as up.
        /// </summary>
        /// <remarks>
        /// Switching a full-screen panel off tells <see cref="UiModal"/> it has closed, which is
        /// right for the game and wrong here: the panel is still on screen, only its Update is being
        /// driven by hand. Without this, M over the main menu was tested against a menu the rest of
        /// the game had been told was gone, and an Escape out of Settings passed for the wrong reason.
        /// </remarks>
        private static void HoldUpdate(MonoBehaviour component)
        {
            component.enabled = false;

            if (component is FullScreenPanel panel && panel.IsShowing)
                UiModal.Opened(panel);
        }

        /// <summary>A component's own Update, callable without Unity.</summary>
        private static Action FrameOf(MonoBehaviour component)
        {
            MethodInfo update = component.GetType().GetMethod(
                "Update", BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.IsNotNull(update, $"{component.GetType().Name} no longer has an Update to call");

            return (Action)update.CreateDelegate(typeof(Action), component);
        }
    }

    /// <summary>
    /// A full-screen panel that closes on a key -- Escape unless told otherwise -- driven by hand.
    /// </summary>
    /// <remarks>
    /// Its check is <see cref="Tick"/> rather than Update so Unity never runs it: the test decides
    /// when it looks at the key.
    /// </remarks>
    internal sealed class KeyClosedPanel : MonoBehaviour
    {
        public bool IsOpen { get; private set; }

        /// <summary>The key that closes it. The chapter card closes on Enter as well as Escape.</summary>
        public Key ClosesOn = Key.Escape;

        public void Open()
        {
            IsOpen = true;
            UiModal.Opened(this);
        }

        public void Tick()
        {
            Keyboard keyboard = Keyboard.current;

            if (!IsOpen || keyboard == null || !keyboard[ClosesOn].wasPressedThisFrame)
                return;

            IsOpen = false;
            UiModal.Closed(this);
        }

        private void OnDestroy() => UiModal.Closed(this);
    }
}
