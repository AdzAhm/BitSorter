using System.Collections;
using System.Linq;
using BitSorter.Board;
using BitSorter.View;
using BitSorter.ViewModel;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace BitSorter.PlayMode.Tests
{
    /// <summary>
    /// BitSorter opening as a simulator (Part 15a): a fresh save on the start page with the music off, a save with a
    /// circuit or a sample open last on that again, the start page's lists, HOME and Escape, HELP, SETTINGS over the
    /// workbench and back, and the 4.x game from Settings with SIMULATOR back from it. Every fixture written before Part 15a
    /// boots as 4.x did (<see cref="TestScene.Load"/>); this one boots as the game does (<see cref="TestScene.LoadSimulator"/>).
    /// </summary>
    [TestFixture]
    public class SimulatorLaunchPlayTests : InputTestFixture
    {
        private Keyboard _keyboard;
        private Mouse _mouse;

        [OneTimeSetUp]
        public void OneTimeSetup()
        {
            SaveGuard.Redirect();
            GameAnalytics.SetReporting(false);
        }

        [OneTimeTearDown]
        public void OneTimeCleanup() => SaveGuard.Release();

        public override void Setup()
        {
            base.Setup();
            InputSystem.settings.SetInternalFeatureFlag("USE_READ_VALUE_CACHING", false);
            _keyboard = InputSystem.AddDevice<Keyboard>();
            _mouse = InputSystem.AddDevice<Mouse>();   // as every fixture adds one: the UI's input module wants a pointer
            _mouse.MakeCurrent();
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

        private static IEnumerator Click(string name)
        {
            UnityEngine.UIElements.Button shell = ShellPanel.Find<UnityEngine.UIElements.Button>(name);
            if (shell != null)
            {
                ShellPanel.Submit(shell);
            }
            else
            {
                GameObject found = GameObject.Find(name);
                Assert.IsNotNull(found, $"no '{name}' on screen");
                found.GetComponent<Button>().onClick.Invoke();
            }

            yield return null;
            yield return null;
        }

        /// <summary>A uGUI button pressed as a player presses it: the pointer moved over it, pressed and let go.</summary>
        private IEnumerator PointAndClick(string name)
        {
            GameObject found = GameObject.Find(name);
            Assert.IsNotNull(found, $"no '{name}' on screen");
            var corners = new Vector3[4];
            found.GetComponent<RectTransform>().GetWorldCorners(corners);   // an overlay canvas: world is screen
            Set(_mouse.position, (Vector2)((corners[0] + corners[2]) / 2f));
            yield return null;
            Press(_mouse.leftButton);
            yield return null;
            Release(_mouse.leftButton);
            yield return null;
            yield return null;
        }

        private static string State(string at)
        {
            return at + ": menu " + Find<MainMenu>().IsOpen + ", workbench " + Find<WorkbenchPanel>().IsOpen
                + ", settings " + Find<SettingsPanel>().IsShowing + ", level " + Find<LevelSession>().LevelName;
        }

        private IEnumerator IntoThe4xGame()
        {
            yield return Click(WorkbenchPanel.SettingsButton);
            yield return Click(SettingsPanel.GameButton);
            Assert.IsTrue(Find<MainMenu>().IsOpen, State("THE 4.x GAME opens its title screen"));
        }

        /// <summary>
        /// The way back from the 4.x game is on its board and first on its menu (Part 15g): Ahmad, inside it, found none
        /// (2026-10-28). Pressed as a player presses them, with the pointer, after a level has been played.
        /// </summary>
        [UnityTest]
        public IEnumerator TheFourXGame_HasAWayBack_FromItsBoardAndFromItsMenu()
        {
            yield return TestScene.LoadSimulator();
            WorkbenchPanel workbench = Find<WorkbenchPanel>();

            // The menu: its way back, first and largest.
            yield return IntoThe4xGame();
            GameObject back = GameObject.Find(MainMenu.WorkbenchButton);
            Assert.AreEqual(MainMenu.WorkbenchCaption, back.GetComponentInChildren<TMPro.TextMeshProUGUI>().text);
            Rect backRect = back.GetComponent<RectTransform>().rect;
            foreach (string row in new[] { "Continue", "Levels", "Sandbox", "Settings" })
            {
                RectTransform other = GameObject.Find(row).GetComponent<RectTransform>();
                Assert.Greater(backRect.width, other.rect.width, row + " is as wide as the way back");
                Assert.Greater(backRect.height, other.rect.height, row + " is as tall as the way back");
                Assert.Greater(back.transform.position.y, other.position.y, row + " is above the way back");
            }

            // On a board, played: its SIMULATOR beside MENU.
            yield return PointAndClick("Continue");
            for (int i = 0; i < 10; i++)
                yield return null;
            Assert.IsFalse(Find<MainMenu>().IsOpen, State("CONTINUE: a board"));
            Find<SimulationRunner>().StepOneTick();
            yield return null;
            yield return PointAndClick(MainMenu.HudSimulatorButtonName);
            Assert.IsTrue(workbench.IsOpen, State("the board's SIMULATOR comes back"));

            // Escape on the title screen goes back, not on to the board under it.
            yield return IntoThe4xGame();
            yield return Tap(_keyboard.escapeKey);
            Assert.IsTrue(workbench.IsOpen, State("Escape on the title screen comes back"));
            Assert.IsFalse(Find<MainMenu>().IsOpen);

            // From a board, Escape is the menu, a pause: Escape again closes it onto the board (the 15g-1 review), and its way
            // back is the big button.
            yield return IntoThe4xGame();
            yield return PointAndClick("Continue");
            yield return Tap(_keyboard.escapeKey);
            Assert.IsTrue(Find<MainMenu>().IsOpen, State("Escape on a board opens the menu"));
            Assert.IsFalse(workbench.IsOpen);
            yield return Tap(_keyboard.escapeKey);
            Assert.IsFalse(Find<MainMenu>().IsOpen, State("Escape again closes it"));
            Assert.IsFalse(workbench.IsOpen, State("onto the board, not out of the game"));
            yield return Tap(_keyboard.escapeKey);
            Assert.IsTrue(Find<MainMenu>().IsOpen, State("sanity: the menu again"));
            yield return PointAndClick(MainMenu.WorkbenchButton);
            Assert.IsTrue(workbench.IsOpen, State("BACK TO THE SIMULATOR comes back"));
        }

        /// <summary>
        /// S on the start page shows the next sample, as the chip it is drawn beside does (Part 15g): the page held every
        /// workbench key, so the one key shown over it did nothing (found in the browser, 2026-10-28).
        /// </summary>
        [UnityTest]
        public IEnumerator S_OnTheStartPage_ShowsTheNextSample()
        {
            yield return TestScene.LoadSimulator();
            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            Assert.IsTrue(workbench.StartPageShowing, "sanity: a fresh save opens on the start page");
            int before = workbench.Sample;

            yield return Tap(_keyboard.sKey);
            Assert.IsFalse(workbench.StartPageShowing, "S left the start page");
            Assert.AreEqual((before + 1) % WorkbenchSamples.All.Count, workbench.Sample, "for the next sample");
        }

        /// <summary>The 4.x title screen's SETTINGS is the simulator's, over the simulator, which BACK comes back to (Part 15g).</summary>
        [UnityTest]
        public IEnumerator TheFourXMenusSettings_IsTheSimulators()
        {
            yield return TestScene.LoadSimulator();
            yield return IntoThe4xGame();
            yield return PointAndClick("Settings");
            Assert.IsTrue(Find<SettingsPanel>().IsShowing && Find<SettingsPanel>().OverTheSimulator, State("SETTINGS"));
            yield return Click(SettingsPanel.BackButton);
            Assert.IsTrue(Find<WorkbenchPanel>().IsOpen, State("BACK: the simulator"));
            Assert.IsFalse(Find<MainMenu>().IsOpen);
        }

        private IEnumerator Tap(KeyControl key)
        {
            Press(key);
            yield return null;
            Release(key);
            yield return null;
        }

        /// <summary>The save as the last session left it, written before the game loads.</summary>
        private static void Plant(System.Action<ProgressStore> write)
        {
            var store = new ProgressStore(ProgressStore.DefaultPath);
            store.Load();
            write(store);
            store.Save();
        }

        [UnityTest]
        public IEnumerator AFreshSave_OpensTheSimulatorOnItsStartPage_WithTheMusicOff()
        {
            yield return TestScene.LoadSimulator();
            WorkbenchPanel workbench = Find<WorkbenchPanel>();

            Assert.IsTrue(workbench.IsOpen, "the simulator, not the 4.x title screen");
            Assert.IsFalse(Find<MainMenu>().IsOpen);
            Assert.IsTrue(workbench.StartPageShowing, "nothing was open last: the start page");
            CollectionAssert.AreEqual(new[] { WorkbenchSamples.MyCircuitKey }, workbench.RecentListed, "MY CIRCUIT, the one circuit there is");
            Assert.IsFalse(workbench.Playing, "nothing plays under it");
            Assert.IsFalse(GameAudio.MusicOn, "the music starts off");
            Assert.IsTrue(GameAudio.EffectsOn, "the effects on");
            Assert.IsFalse(AppLaunch.GameShown, "the 4.x game is not on screen, so no level of it has started");
            Assert.IsFalse(Find<TutorialDirector>().IsRunning, "nor has the 4.x tutorial, behind the workbench");
        }

        /// <summary>
        /// A circuit in another unit, opened from FILE, says its own unit on the speed and RUN TO at once: they were written
        /// again only after an edit, and a circuit in ns opened from MY CIRCUIT in ps read "1.6 ps/s" (the 15d shots).
        /// </summary>
        [UnityTest]
        public IEnumerator ACircuitInAnotherUnit_SaysItsUnit_AsItOpens()
        {
            Project nano = Project.New("nano");
            Plant(store => store.SetProject(Documents.KeyOf("nano"), nano with { Settings = nano.Settings with { TimeUnit = "ns" } }));
            yield return TestScene.LoadSimulator();
            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            StringAssert.EndsWith(" ps/s", workbench.SpeedText, "sanity: MY CIRCUIT counts in ps");

            workbench.OpenDocument(Documents.KeyOf("nano"));
            yield return null;
            Assert.AreEqual("ns", workbench.Session.Unit, "sanity: the circuit counts in ns");
            StringAssert.EndsWith(" ns/s", workbench.SpeedText, "the speed in its unit");
        }

        [UnityTest]
        public IEnumerator ACircuitOpenLast_IsWhatItOpensOnAgain()
        {
            Plant(store =>
            {
                store.SetProject(Documents.KeyOf("adder"), Project.New("adder"));
                store.LastOpen = Documents.KeyOf("adder");
            });
            yield return TestScene.LoadSimulator();
            WorkbenchPanel workbench = Find<WorkbenchPanel>();

            Assert.IsFalse(workbench.StartPageShowing);
            Assert.AreEqual(0, workbench.Sample);
            Assert.AreEqual(Documents.KeyOf("adder"), workbench.DocumentKey);
            Assert.AreEqual("adder", workbench.Session.Project.Top, "the circuit as it was kept");
        }

        [UnityTest]
        public IEnumerator ASampleOpenLast_IsWhatItOpensOnAgain_AndOneSinceGoneIsTheStartPage()
        {
            Plant(store =>
            {
                store.SetProject(Documents.KeyOf("adder"), Project.New("adder"));
                store.Touch(Documents.KeyOf("adder"));
                store.LastOpen = Documents.SampleKey(WorkbenchSamples.All[2].Name);
            });
            yield return TestScene.LoadSimulator();
            Assert.AreEqual(3, Find<WorkbenchPanel>().Sample, "the counter, as it was left");
            Assert.IsFalse(Find<WorkbenchPanel>().StartPageShowing);
            Assert.AreEqual(Documents.KeyOf("adder"), Find<WorkbenchPanel>().DocumentKey,
                "the SAMPLE chip goes back to the circuit opened last before the sample");
            yield return TestScene.Clear();

            Plant(store => store.LastOpen = Documents.KeyOf("deleted since"));
            yield return TestScene.LoadSimulator();
            Assert.IsTrue(Find<WorkbenchPanel>().StartPageShowing, "what was open is gone: the start page");
        }

        [UnityTest]
        public IEnumerator TheStartPage_OpensWhatItLists_HomeComesBack_AndEscapeLeavesItForTheCircuitUnderIt()
        {
            Plant(store => store.SetProject(Documents.KeyOf("alu"), Project.New("alu")));
            yield return TestScene.LoadSimulator();
            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            ProgressStore store = Find<ProgressTracker>().Store;
            Assert.IsTrue(workbench.StartPageShowing);

            int at = workbench.RecentListed.ToList().IndexOf(Documents.KeyOf("alu"));
            Assert.GreaterOrEqual(at, 0, "RECENT lists every circuit, as many as it shows");
            yield return Click(WorkbenchPanel.RecentItem(at));
            Assert.IsFalse(workbench.StartPageShowing, "a circuit opened from RECENT takes the page down");
            Assert.AreEqual(Documents.KeyOf("alu"), workbench.DocumentKey);
            Assert.AreEqual(Documents.KeyOf("alu"), store.LastOpen, "and is what opens next time");

            yield return Click(WorkbenchPanel.HomeButton);
            Assert.IsTrue(workbench.StartPageShowing, "HOME");
            Assert.AreEqual("", store.LastOpen, "the start page, next time");
            yield return Click(WorkbenchPanel.SampleItem(1));
            Assert.AreEqual(2, workbench.Sample, "the full adder, from SAMPLES");
            Assert.IsFalse(workbench.StartPageShowing);

            yield return Click(WorkbenchPanel.HomeButton);
            yield return Tap(_keyboard.escapeKey);
            Assert.IsFalse(workbench.StartPageShowing, "Escape takes the page down");
            Assert.AreEqual(2, workbench.Sample, "onto the circuit under it");
            Assert.AreEqual(Documents.SampleKey(WorkbenchSamples.Chip[2].Name), store.LastOpen);

            yield return Tap(_keyboard.escapeKey);
            Assert.IsTrue(workbench.IsOpen, "and never leaves the workbench");
            Assert.IsFalse(Find<MainMenu>().IsOpen);
        }

        [UnityTest]
        public IEnumerator Help_HasTheKeyboardMapAndTheCredits_WhichComeBackToTheWorkbench()
        {
            Plant(store => store.LastOpen = WorkbenchSamples.MyCircuitKey);
            yield return TestScene.LoadSimulator();
            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            Assert.IsFalse(workbench.StartPageShowing, "sanity: MY CIRCUIT, open last");

            yield return Click(WorkbenchPanel.HelpButton);
            Assert.IsTrue(workbench.HelpMenu.IsOpen);
            yield return Click(HelpMenu.ActionItem + HelpAction.KeyboardMap);
            Assert.IsTrue(workbench.KeyMapPopup.IsOpen, "KEYBOARD MAP");
            yield return Tap(_keyboard.escapeKey);
            Assert.IsFalse(workbench.KeyMapPopup.IsOpen);

            yield return Click(WorkbenchPanel.HelpButton);
            yield return Click(HelpMenu.ActionItem + HelpAction.About);
            CreditsPanel credits = Find<CreditsPanel>();
            Assert.IsTrue(credits.IsShowing, "ABOUT: the credits");
            Assert.IsFalse(workbench.IsOpen);
            yield return Tap(_keyboard.spaceKey);
            Assert.IsFalse(credits.IsShowing, "any key ends them");
            Assert.IsTrue(workbench.IsOpen, "back on the workbench");
            Assert.IsFalse(Find<SettingsPanel>().IsShowing, "not on Settings, where the credits are also reached");
        }

        [UnityTest]
        public IEnumerator TheStartPage_SaysWhatItsActionsSay_AndQuestionMarkOpensTheKeyMap_AndFileLeavesTheHiddenCircuitAlone()
        {
            yield return TestScene.LoadSimulator();
            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            Assert.IsTrue(workbench.StartPageShowing);

            // The chip that says what happened is under the page: the page says it too (the #84 review).
            workbench.NewCircuit("My Circuit");
            StringAssert.Contains("called my circuit already", workbench.StartPageNotice, "a name refused, said where it can be seen");
            Assert.IsTrue(workbench.StartPageShowing);

            yield return Tap(_keyboard.slashKey);
            Assert.IsTrue(workbench.KeyMapPopup.IsOpen, "? opens the keyboard map on the start page");
            yield return Tap(_keyboard.slashKey);
            Assert.IsFalse(workbench.KeyMapPopup.IsOpen, "and closes it, as the map says");

            yield return Click(WorkbenchPanel.FileButton);
            Assert.IsTrue(workbench.FileMenu.IsOpen);
            Assert.IsTrue(workbench.FileMenu.Offers(FileAction.NewCircuit) && workbench.FileMenu.Offers(FileAction.OpenCircuit));
            Assert.IsFalse(workbench.FileMenu.Offers(FileAction.ExportProject), "nothing that acts on the circuit the page hides");
            Assert.IsFalse(workbench.FileMenu.Offers(FileAction.RenameCircuit) || workbench.FileMenu.Offers(FileAction.DeleteCircuit));
        }

        [UnityTest]
        public IEnumerator AChangeOfLook_ComesBackToSettings_OverWhicheverItWasOpenedOver()
        {
            yield return TestScene.LoadSimulator();
            try
            {
                // Over the 4.x title screen: the reload opens the simulator, which has to go under (the #84 review).
                yield return Click(WorkbenchPanel.SettingsButton);
                yield return Click(SettingsPanel.GameButton);
                Find<MainMenu>().Show(false);
                Find<SettingsPanel>().Open();   // as the title screen's own SETTINGS does
                yield return null;
                yield return Click(SettingsPanel.LookButton(Looks.SchematicLight));
                yield return UntilTheLookHasChanged();

                Assert.IsTrue(Find<SettingsPanel>().IsShowing, "back on Settings");
                Assert.IsFalse(Find<SettingsPanel>().OverTheSimulator);
                Assert.IsFalse(Find<WorkbenchPanel>().IsOpen, "with the simulator under it, not over it");
                Assert.IsFalse(Find<WorkbenchPanel>().Shell.Visible, "its shell, drawn over the canvas, out of sight");
                yield return Click(SettingsPanel.BackButton);
                Assert.IsTrue(Find<MainMenu>().IsOpen, "BACK: the title screen it was opened over");
                Assert.IsFalse(Find<WorkbenchPanel>().IsOpen);

                // Over the simulator.
                yield return Click(MainMenu.WorkbenchButton);
                yield return Click(WorkbenchPanel.SettingsButton);
                yield return Click(SettingsPanel.LookButton(Look.Default));
                yield return UntilTheLookHasChanged();

                Assert.IsTrue(Find<SettingsPanel>().IsShowing && Find<SettingsPanel>().OverTheSimulator, "back on Settings, over the simulator");
                Assert.IsFalse(Find<WorkbenchPanel>().IsOpen);
                yield return Click(SettingsPanel.BackButton);
                Assert.IsTrue(Find<WorkbenchPanel>().IsOpen, "BACK: the simulator");
                Assert.IsFalse(Find<MainMenu>().IsOpen);
            }
            finally
            {
                Look.Use(null);
            }
        }

        private static IEnumerator UntilTheLookHasChanged()
        {
            Assert.IsTrue(LookSwitch.Busy, "sanity: choosing a look loads the game again");
            for (int frame = 0; frame < 600 && LookSwitch.Busy; frame++)
                yield return null;
            Assert.IsFalse(LookSwitch.Busy, "the switch never finished");
        }

        [UnityTest]
        public IEnumerator Settings_GoesOverTheSimulator_AndTheFourXGameIsAtItsFoot_WithTheWayBack()
        {
            yield return TestScene.LoadSimulator();
            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            SettingsPanel settings = Find<SettingsPanel>();

            yield return Click(WorkbenchPanel.SettingsButton);
            Assert.IsTrue(settings.IsShowing && settings.OverTheSimulator, "SETTINGS, over the simulator");
            Assert.IsFalse(workbench.IsOpen);
            yield return Click(SettingsPanel.BackButton);
            Assert.IsTrue(workbench.IsOpen && workbench.StartPageShowing, "BACK: the start page it went over");

            yield return Click(WorkbenchPanel.SettingsButton);
            yield return Click(SettingsPanel.GameButton);
            Assert.IsTrue(Find<MainMenu>().IsOpen, "THE 4.x GAME opens its title screen");
            Assert.IsFalse(settings.IsShowing || workbench.IsOpen);
            Assert.IsTrue(AppLaunch.GameShown);

            yield return Click(MainMenu.WorkbenchButton);
            Assert.IsTrue(workbench.IsOpen, "SIMULATOR comes back");
            Assert.IsFalse(Find<MainMenu>().IsOpen);
            Assert.IsTrue(workbench.StartPageShowing, "to what it was showing");
        }
    }
}
