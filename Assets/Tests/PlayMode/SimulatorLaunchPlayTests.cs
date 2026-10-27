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
            Plant(store => store.LastOpen = Documents.SampleKey(WorkbenchSamples.All[2].Name));
            yield return TestScene.LoadSimulator();
            Assert.AreEqual(3, Find<WorkbenchPanel>().Sample, "the counter, as it was left");
            Assert.IsFalse(Find<WorkbenchPanel>().StartPageShowing);
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
