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
using Object = UnityEngine.Object;

namespace BitSorter.PlayMode.Tests
{
    /// <summary>
    /// BitSorter opening as a simulator (Part 15a): a fresh save on the start page, a save with a
    /// circuit or a sample open last on that again, the start page's lists, HOME and Escape, HELP, and SETTINGS over the
    /// workbench and back. It boots as the game does (<see cref="TestScene.LoadSimulator"/>); most fixtures begin on the
    /// reference circuit instead (<see cref="TestScene.Load"/>). Until Part 16a it also held the way into the 4.x game from
    /// Settings, and the ways back out of it.
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
            Assert.IsNotNull(shell, $"no '{name}' on screen");
            ShellPanel.Submit(shell);

            yield return null;
            yield return null;
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
        public IEnumerator AFreshSave_OpensTheSimulatorOnItsStartPage()
        {
            yield return TestScene.LoadSimulator();
            WorkbenchPanel workbench = Find<WorkbenchPanel>();

            Assert.IsTrue(workbench.IsOpen, "the simulator");
            Assert.IsTrue(workbench.StartPageShowing, "nothing was open last: the start page");
            CollectionAssert.AreEqual(new[] { WorkbenchSamples.MyCircuitKey }, workbench.RecentListed, "MY CIRCUIT, the one circuit there is");
            Assert.IsFalse(workbench.Playing, "nothing plays under it");
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
        }

        /// <summary>
        /// HELP has the keyboard map and ABOUT BITSORTER (Part 15h), which says what the simulator is and has the credits at its
        /// foot: they roll and come back to it, and Escape takes it down. ABOUT opened the credits roll until Ahmad asked for
        /// a page about the simulator (2026-10-28).
        /// </summary>
        [UnityTest]
        public IEnumerator Help_HasTheKeyboardMapAndAboutBitSorter_WithTheCreditsAtItsFoot()
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
            Assert.IsTrue(workbench.AboutShowing, "ABOUT BITSORTER: a page about the simulator");
            UnityEngine.UIElements.VisualElement about = ShellPanel.Find<UnityEngine.UIElements.VisualElement>(WorkbenchPanel.AboutPageName);
            string said = string.Join(" ", UnityEngine.UIElements.UQueryExtensions.Query<UnityEngine.UIElements.Label>(about).ToList().Select(l => l.text));
            StringAssert.Contains("Digital Systems and Computer Structure", said, "the course named in full, as Ahmad asked");
            StringAssert.Contains("4.0.0", said, "and where the game it began as is still played");
            Assert.IsNotNull(ShellPanel.Find<UnityEngine.UIElements.Button>(WorkbenchPanel.AboutGitHubButton), "the project's page at its foot");

            CreditsPanel credits = Find<CreditsPanel>();
            yield return Click(WorkbenchPanel.AboutCreditsButton);
            Assert.IsTrue(credits.IsShowing, "its CREDITS: the credits roll");
            Assert.IsFalse(workbench.AboutShowing, "in its place");
            yield return Tap(_keyboard.spaceKey);
            Assert.IsFalse(credits.IsShowing, "any key ends them");
            Assert.IsTrue(workbench.AboutShowing, "and comes back to ABOUT");
            Assert.IsFalse(Find<SettingsPanel>().IsShowing, "not to Settings, where the credits are also reached");

            yield return null;
            yield return Tap(_keyboard.escapeKey);
            Assert.IsFalse(workbench.AboutShowing, "Escape takes it down");
            Assert.IsTrue(workbench.IsOpen, "back on the workbench");
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

        /// <summary>A change of look comes back to Settings over the simulator, and BACK to what Settings went over.</summary>
        [UnityTest]
        public IEnumerator AChangeOfLook_ComesBackToSettings_OverTheSimulator()
        {
            yield return TestScene.LoadSimulator();
            try
            {
                yield return Click(WorkbenchPanel.SettingsButton);
                yield return Click(SettingsPanel.LookButton(Looks.SchematicLight));
                yield return UntilTheLookHasChanged();

                Assert.IsTrue(Find<SettingsPanel>().IsShowing, "back on Settings");
                Assert.IsTrue(Find<WorkbenchPanel>().IsOpen, "a page of the workbench (Part 15g)");
                yield return Click(SettingsPanel.BackButton);
                Assert.IsTrue(Find<WorkbenchPanel>().IsOpen, "BACK: the simulator");
                Assert.IsTrue(Find<WorkbenchPanel>().StartPageShowing, "and the start page Settings went over");
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

        /// <summary>
        /// SETTINGS is a page over the simulator, which BACK comes back to; nothing on it leads anywhere else (THE 4.x GAME,
        /// beside CREDITS until Part 16a, opened the 4.x title screen).
        /// </summary>
        [UnityTest]
        public IEnumerator Settings_GoesOverTheSimulator_AndBackComesBackToIt()
        {
            yield return TestScene.LoadSimulator();
            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            SettingsPanel settings = Find<SettingsPanel>();

            yield return Click(WorkbenchPanel.SettingsButton);
            Assert.IsTrue(settings.IsShowing, "SETTINGS");
            Assert.IsTrue(workbench.IsOpen, "a page of the workbench's own (Part 15g)");

            UnityEngine.UIElements.VisualElement page = ShellPanel.Find<UnityEngine.UIElements.VisualElement>(SettingsPanel.PageName);
            string said = string.Join(" ", UnityEngine.UIElements.UQueryExtensions.Query<UnityEngine.UIElements.TextElement>(page)
                .ToList().Select(l => l.text));
            StringAssert.DoesNotContain("4.x", said, "Settings no longer leads into the 4.x game");

            yield return Click(SettingsPanel.BackButton);
            Assert.IsTrue(workbench.IsOpen && workbench.StartPageShowing, "BACK: the start page it went over");
        }
    }
}
