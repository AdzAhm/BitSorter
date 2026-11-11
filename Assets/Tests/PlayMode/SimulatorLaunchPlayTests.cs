using System.Collections;
using System.Collections.Generic;
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
            InputSystem.runInBackground = true;   // as the player runs (Run In Background): a click is still taken with the editor behind another window
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

        /// <summary>A document the rules refuse, as a save edited by hand may hold one: an AND gate of one input.</summary>
        private static Project Refused() => Project.New("broken") with
        {
            Modules = new[]
            {
                new Module
                {
                    Name = "broken", NextId = 2,
                    Parts = new Part[] { new GatePart { Id = 1, Name = "g1", Kind = BitSorter.Sim.GateKind.And, Inputs = 1 } },
                },
            },
        };

        /// <summary>
        /// A circuit the save keeps that the rules refuse -- edited by hand, or written by a newer BitSorter -- leaves the
        /// workbench open on the reference circuit, under the start page, saying why in the rules' own words, and the circuit
        /// kept in the save as it was: it threw from the workbench's Start, which never built, on every launch until the save
        /// was deleted (save-1).
        /// </summary>
        [UnityTest]
        public IEnumerator ACircuitTheSaveKeepsThatCannotOpen_LeavesTheWorkbenchOpen_SayingWhy()
        {
            Plant(store =>
            {
                store.SetProject(WorkbenchSamples.MyCircuitKey, Refused());
                store.LastOpen = WorkbenchSamples.MyCircuitKey;
            });
            yield return TestScene.LoadSimulator();
            WorkbenchPanel workbench = Find<WorkbenchPanel>();

            Assert.IsTrue(workbench.IsOpen, "the workbench, built");
            Assert.AreEqual(1, workbench.Sample, "on the reference circuit");
            Assert.IsTrue(workbench.StartPageShowing, "under the start page");
            StringAssert.Contains("could not be opened", workbench.StartPageNotice);
            StringAssert.Contains("2 to 8", workbench.StartPageNotice, "in the rules' own words");

            var saved = new ProgressStore(ProgressStore.DefaultPath);
            saved.Load();
            Assert.IsTrue(saved.TryGetProject(WorkbenchSamples.MyCircuitKey, out Project kept, new System.Collections.Generic.List<string>()));
            Assert.AreEqual(1, ((GatePart)kept.TopModule.Parts.Single()).Inputs, "kept in the save as it was");
        }

        /// <summary>
        /// An answer the save keeps for an exercise that the rules refuse: the exercise as it starts, its top given an AND gate
        /// of one input, as a save edited by hand may hold -- an answer to it still, so it is adopted, and then cannot open.
        /// </summary>
        private static (Exercise Exercise, Project Answer) AnAnswerThatCannotOpen()
        {
            foreach (Exercise exercise in ExerciseCatalog.Run)
            {
                Project start = exercise.ToProject(new List<string>());
                Module top = start?.TopModule;
                if (top == null || top.IsWritten)
                    continue;
                var parts = new List<Part>(top.Parts)
                {
                    new GatePart { Id = top.NextId, Name = "g_one", Kind = BitSorter.Sim.GateKind.And, Inputs = 1 },
                };
                var modules = new List<Module>(start.Modules);
                modules[modules.IndexOf(top)] = top with { Parts = parts, NextId = top.NextId + 1 };
                return (exercise, start with { Modules = modules });
            }
            Assert.Fail("sanity: a drawn exercise, to give an answer that cannot open");
            return default;
        }

        /// <summary>Every report the workbench asks GameAnalytics for while <paramref name="body"/> runs, as event and id.</summary>
        private static IEnumerator Listening(List<string> asked, IEnumerator body)
        {
            void Heard(string name, string id) => asked.Add(name + " " + id);
            GameAnalytics.Asked += Heard;
            try
            {
                while (body.MoveNext())
                    yield return body.Current;
            }
            finally
            {
                GameAnalytics.Asked -= Heard;
            }
        }

        /// <summary>
        /// An exercise open last whose answer cannot open is not reported started: the reference circuit opens in its place
        /// (save-1), and the launch reported a level started for it all the same -- the one thing analytics measures being
        /// which exercise people stop at (the save-1 fix's review). One that opens is reported, so the absence is the
        /// workbench's and not a listener that heard nothing.
        /// </summary>
        [UnityTest]
        public IEnumerator AnExerciseOpenLast_WhoseAnswerCannotOpen_IsNotReportedStarted()
        {
            (Exercise exercise, Project answer) = AnAnswerThatCannotOpen();
            Exercise other = ExerciseCatalog.Run.First(e => e.Id != exercise.Id);
            Plant(store =>
            {
                store.SetProject(exercise.Id, answer);
                store.LastOpen = Documents.ExerciseKey(exercise.Id);
            });
            var asked = new List<string>();
            WorkbenchPanel workbench = null;
            yield return Listening(asked, Launched());

            IEnumerator Launched()
            {
                yield return TestScene.LoadSimulator();
                workbench = Find<WorkbenchPanel>();
                Assert.IsNull(workbench.ExerciseOpen, "sanity: the exercise did not open");
                Assert.AreEqual(1, workbench.Sample, "sanity: the reference circuit in its place");
                StringAssert.Contains("could not be opened", workbench.StartPageNotice, "sanity: saying why");

                workbench.OpenExercise(other.Id);
                yield return null;
                Assert.AreSame(other, workbench.ExerciseOpen, "sanity: an exercise that opens");
            }

            CollectionAssert.DoesNotContain(asked, AnalyticsRules.LevelStarted + " " + exercise.Id, "an exercise that never opened, reported started");
            CollectionAssert.Contains(asked, AnalyticsRules.LevelStarted + " " + other.Id, "one that opened, reported");
        }

        /// <summary>
        /// An exercise opened from EXERCISES whose answer cannot open is not reported started either (the save-1 fix's review),
        /// and one opened after it that does open is.
        /// </summary>
        [UnityTest]
        public IEnumerator AnExerciseOpened_WhoseAnswerCannotOpen_IsNotReportedStarted()
        {
            (Exercise exercise, Project answer) = AnAnswerThatCannotOpen();
            Exercise other = ExerciseCatalog.Run.First(e => e.Id != exercise.Id);
            Plant(store => store.SetProject(exercise.Id, answer));
            var asked = new List<string>();
            yield return Listening(asked, Opened());

            IEnumerator Opened()
            {
                yield return TestScene.LoadSimulator();
                WorkbenchPanel workbench = Find<WorkbenchPanel>();

                workbench.OpenExercise(exercise.Id);
                yield return null;
                Assert.IsNull(workbench.ExerciseOpen, "sanity: the exercise did not open");
                Assert.AreEqual(1, workbench.Sample, "sanity: the reference circuit in its place");

                workbench.OpenExercise(other.Id);
                yield return null;
                Assert.AreSame(other, workbench.ExerciseOpen, "sanity: an exercise that opens");
            }

            CollectionAssert.DoesNotContain(asked, AnalyticsRules.LevelStarted + " " + exercise.Id, "an exercise that never opened, reported started");
            CollectionAssert.Contains(asked, AnalyticsRules.LevelStarted + " " + other.Id, "one that opened, reported");
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

        /// <summary>
        /// Code typed with an error is kept as a draft (Ahmad, 2026-11-03): the last session's opens back in its module's code,
        /// marked not applied, the document keeping the last code that worked; and what is typed is written as another
        /// circuit opens -- as quitting writes it -- and shown again back on the circuit, a sample opened between.
        /// </summary>
        [UnityTest]
        public IEnumerator CodeTypedWithAnError_IsKept_ThroughAnotherCircuitASampleAndTheNextLaunch()
        {
            string key = Documents.KeyOf("adder");
            string broken = WorkbenchSamples.AdderSource.Replace("= a + b;", "= a + ;");
            Plant(store =>
            {
                store.SetProject(key, WorkbenchSamples.WrittenAdder());
                store.SetDrafts(key, new System.Collections.Generic.Dictionary<string, string> { ["add4"] = broken });
                store.LastOpen = key;
            });
            yield return TestScene.LoadSimulator();
            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            Assert.AreEqual(key, workbench.DocumentKey, "sanity: the circuit open last");
            int add = workbench.Session.Project.TopModule.Parts.OfType<BlockPart>().Single().Id;

            Assert.IsTrue(workbench.StepInto(add));
            yield return null;
            Assert.IsTrue(workbench.Session.ShowsCode, "a module with code typed and not applied opens on its code");
            CodeDocument code = workbench.Session.Code;
            Assert.AreEqual(broken, code.Text, "the draft, as the last session left it");
            Assert.IsTrue(code.HasErrors);
            StringAssert.StartsWith("NOT APPLIED", workbench.CodeView.Status, "marked not applied");
            Assert.AreEqual(WorkbenchSamples.AdderSource, workbench.Session.Project.Find("add4").Source, "the document keeps the last code that worked");

            // Typed on -- a minute ahead of the game's clock, so its pause never runs out -- then another circuit, a sample, and back.
            string further = broken.Replace("// the first operand", "// a");
            Assert.AreNotEqual(broken, further, "sanity: typed on");
            code.Type(further, Time.timeAsDouble * 1000.0 + 60000.0);
            workbench.OpenDocument(WorkbenchSamples.MyCircuitKey);
            yield return null;
            var onDisk = new ProgressStore(ProgressStore.DefaultPath);
            onDisk.Load();
            Assert.AreEqual(further, onDisk.Drafts(key)["add4"], "written as another circuit opens, as quitting writes it");

            workbench.ShowSample(1);
            yield return null;
            workbench.OpenDocument(key);
            yield return null;
            Assert.IsTrue(workbench.StepInto(add));
            yield return null;
            Assert.AreEqual(further, workbench.Session.Code.Text, "and shown again, back on the circuit");
        }

        /// <summary>
        /// An answer kept under an exercise that no longer fits it is moved to a circuit of the student's own as the exercise
        /// opens -- and the code typed into it and not applied goes with it (the review of the 16d sweep's fixes: the move
        /// deleted the answer under the exercise's key, and its drafts with it).
        /// </summary>
        [UnityTest]
        public IEnumerator CodeTypedIntoAnAnswerThatNoLongerFits_GoesWithItToTheCircuitItIsKeptAs()
        {
            const string exercise = "half-adder";
            string broken = WorkbenchSamples.AdderSource.Replace("= a + b;", "= a + ;");
            Plant(store =>
            {
                store.SetProject(exercise, WorkbenchSamples.WrittenAdder());   // its top is no half adder: no answer to it
                store.SetDrafts(exercise, new System.Collections.Generic.Dictionary<string, string> { ["add4"] = broken });
            });
            yield return TestScene.LoadSimulator();
            WorkbenchPanel workbench = Find<WorkbenchPanel>();

            workbench.OpenExercise(exercise);
            yield return null;
            string moved = Documents.KeyOf(ExerciseCatalog.Run.Single(e => e.Id == exercise).Title + " old");
            var onDisk = new ProgressStore(ProgressStore.DefaultPath);
            onDisk.Load();
            Assert.IsTrue(onDisk.TryGetProject(moved, out Project kept, new System.Collections.Generic.List<string>()), "sanity: moved to a circuit of its own");
            Assert.AreEqual("adder", kept.Top);
            Assert.IsTrue(onDisk.Drafts(moved).TryGetValue("add4", out string draft), "what was typed into it went with it");
            Assert.AreEqual(broken, draft);
            CollectionAssert.IsEmpty(onDisk.Drafts(exercise), "and nothing is left under the exercise");
        }

        /// <summary>
        /// A launch that stopped while opening what was open last opens the start page instead, and says so (Part 16b): a
        /// browser that ran out of memory opening a big design would stop on every launch after. One that opens it marks
        /// that it is opening, and takes the mark off once it has stayed up.
        /// </summary>
        [UnityTest]
        public IEnumerator ALaunchThatStoppedWhileOpening_OpensTheStartPage_AndOneThatOpensTakesItsMarkOff()
        {
            Plant(store => store.LastOpen = Documents.SampleKey(WorkbenchSamples.All[2].Name));
            Preferences.SetInt(WorkbenchPanel.OpeningKey, 1);   // as a launch that stopped part way through its open left it
            yield return TestScene.LoadSimulator();
            yield return null;
            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            Assert.IsTrue(workbench.StartPageShowing, "the start page, not what stopped the last launch");
            Assert.AreEqual(WorkbenchPanel.StoppedOpening, workbench.ChipText, "and why");
            Assert.AreEqual(0, Preferences.GetInt(WorkbenchPanel.OpeningKey, 0), "the mark off: the next launch opens it again");
            yield return TestScene.Clear();

            Plant(store => store.LastOpen = Documents.SampleKey(WorkbenchSamples.All[2].Name));
            yield return TestScene.LoadSimulator();
            Assert.AreEqual(3, Find<WorkbenchPanel>().Sample, "opened as ever");
            Assert.AreEqual(1, Preferences.GetInt(WorkbenchPanel.OpeningKey, 0), "marked while it opens");
            for (int frame = 0; frame < 300 && Preferences.GetInt(WorkbenchPanel.OpeningKey, 0) == 1; frame++)
                yield return null;
            Assert.AreEqual(0, Preferences.GetInt(WorkbenchPanel.OpeningKey, 0), "the mark off once it has stayed up");
        }

        /// <summary>
        /// The launch after one that stopped while opening anything but MY CIRCUIT -- a sample, an exercise, a circuit of the
        /// student's -- opens the start page over MY CIRCUIT, as it did before save-6, and Escape takes it down onto MY
        /// CIRCUIT: only a stop while MY CIRCUIT opened puts the reference under it (the save-6 fix's review: every
        /// recovery did).
        /// </summary>
        [UnityTest]
        public IEnumerator ALaunchThatStoppedWhileOpeningSomethingElse_OpensTheStartPageOverMyCircuit()
        {
            Plant(store => store.LastOpen = Documents.SampleKey(WorkbenchSamples.All[2].Name));
            Preferences.SetInt(WorkbenchPanel.OpeningKey, WorkbenchPanel.OpeningOther);
            yield return TestScene.LoadSimulator();
            WorkbenchPanel workbench = Find<WorkbenchPanel>();

            Assert.IsTrue(workbench.StartPageShowing, "sanity: the start page, not what stopped the last launch");
            Assert.AreEqual(WorkbenchPanel.StoppedOpening, workbench.ChipText, "sanity: and why");
            Assert.AreEqual(0, workbench.Sample, "over MY CIRCUIT, which did not stop it");
            Assert.AreEqual(WorkbenchSamples.MyCircuitKey, workbench.DocumentKey);
            Assert.AreEqual("", Find<ProgressTracker>().Store.LastOpen, "the start page what opens next, as any time it is up");
        }

        /// <summary>
        /// The launch after one that stopped while opening MY CIRCUIT opens the start page over the reference circuit, never
        /// over MY CIRCUIT, and remembers the reference as what opens next: the recovery built MY CIRCUIT before its start
        /// page, so a MY CIRCUIT that stopped the page stopped it again, every launch (save-6). A launch onto the start page is
        /// marked while it builds MY CIRCUIT under it, as any other open is.
        /// </summary>
        [UnityTest]
        public IEnumerator ALaunchThatStoppedWhileOpeningMyCircuit_NeverOpensItAgainByItself()
        {
            var editor = new BoardEditor(WorkbenchSamples.Blank());
            Assert.IsTrue(editor.Add(editor.Project.Top, new GatePart { Kind = BitSorter.Sim.GateKind.Not, Inputs = 1 }, new Placement(4, 0)).Done);
            Project gate = editor.Project;

            // Onto the start page, MY CIRCUIT under it: marked while it opens, as a launch onto anything else is.
            Plant(store => store.SetProject(WorkbenchSamples.MyCircuitKey, gate));
            yield return TestScene.LoadSimulator();
            Assert.IsTrue(Find<WorkbenchPanel>().StartPageShowing, "sanity: nothing open last");
            Assert.AreEqual(0, Find<WorkbenchPanel>().Sample, "sanity: MY CIRCUIT under it");
            int opening = Preferences.GetInt(WorkbenchPanel.OpeningKey, 0);
            Assert.AreNotEqual(0, opening, "marked while it builds MY CIRCUIT under the page");
            yield return TestScene.Clear();

            // As that launch left it had MY CIRCUIT stopped the page.
            Plant(store =>
            {
                store.SetProject(WorkbenchSamples.MyCircuitKey, gate);
                store.LastOpen = WorkbenchSamples.MyCircuitKey;
            });
            Preferences.SetInt(WorkbenchPanel.OpeningKey, opening);
            yield return TestScene.LoadSimulator();
            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            Assert.IsTrue(workbench.StartPageShowing, "the start page");
            Assert.AreEqual(1, workbench.Sample, "over the reference circuit, never MY CIRCUIT, which stopped the last launch");
            Assert.AreEqual(Documents.SampleKey(WorkbenchSamples.Chip[1].Name), Find<ProgressTracker>().Store.LastOpen,
                "and the reference is what opens next, until MY CIRCUIT is opened on purpose");
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
            Assert.IsFalse(workbench.FileMenu.Offers(FileAction.ReloadSave), "no RELOAD THE SAVE while the save is this window's own");
        }

        /// <summary>
        /// A save a newer BitSorter wrote is said to be one as BitSorter opens on it, and copied beside itself as it was before
        /// this one first writes over it (Ahmad's decision, 2026-11-03).
        /// </summary>
        [UnityTest]
        public IEnumerator ASaveFromANewerBitSorter_IsSaidSoAsItOpens_AndCopiedBeforeTheFirstWrite()
        {
            const string Newer = "{\"format\":6,\"completed\":[\"half-adder\"],\"glow\":true}";
            SaveGuard.Plant(Newer);
            string copy = null;
            try
            {
                yield return TestScene.LoadSimulator();
                WorkbenchPanel workbench = Find<WorkbenchPanel>();
                ProgressStore store = Find<ProgressTracker>().Store;
                Assert.IsTrue(workbench.StartPageShowing, "sanity: nothing was open last");
                StringAssert.Contains(WorkbenchPanel.SaveFromANewerBitSorter, workbench.StartPageNotice, "said as it opens");
                Assert.IsTrue(store.IsComplete("half-adder"), "and what this BitSorter knows of it read");

                workbench.ShowSample(2);   // what is open, remembered: a write
                yield return null;
                copy = store.NewerCopy;
                Assert.IsNotNull(copy, "copied before the first write");
                Assert.AreEqual(Newer, System.IO.File.ReadAllText(copy), "as it was");
            }
            finally
            {
                if (copy != null && System.IO.File.Exists(copy))
                    System.IO.File.Delete(copy);
            }
        }

        /// <summary>
        /// A save that could not be read as BitSorter opened -- another program held it -- is said to be so, never written over,
        /// and FILE, RELOAD THE SAVE reads it again (save-7).
        /// </summary>
        [UnityTest]
        public IEnumerator ASaveNotReadAsItOpened_IsSaidSo_NeverWrittenOver_AndReloadReadsIt()
        {
            Plant(store => store.MarkComplete("half-adder"));
            WorkbenchPanel workbench;
            using (new System.IO.FileStream(ProgressStore.DefaultPath, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.None))
            {
                yield return TestScene.LoadSimulator();
                workbench = Find<WorkbenchPanel>();
                yield return null;
                Assert.AreEqual(SaveHold.CouldNotRead, Find<ProgressTracker>().Store.Held);
            }

            Assert.IsTrue(workbench.StartPageShowing);
            Assert.AreEqual(WorkbenchPanel.SaveNotRead, workbench.StartPageNotice, "said where it can be seen");
            Assert.AreEqual(WorkbenchPanel.ReadAgainQuestion, workbench.Ask.Question, "and asked to read it again");
            workbench.ShowSample(2);   // a write, which writes nothing
            yield return null;
            var saved = new ProgressStore(ProgressStore.DefaultPath);
            saved.Load();
            Assert.IsTrue(saved.IsComplete("half-adder"), "the save not read is not written over");

            workbench.OpenFileMenu();
            yield return null;
            Assert.IsTrue(workbench.FileMenu.Offers(FileAction.ReloadSave));
            yield return Click(FileMenu.ActionItem + FileAction.ReloadSave);
            WorkbenchPanel again = null;
            for (int frame = 0; frame < 300 && again == null; frame++)
            {
                yield return null;
                WorkbenchPanel found = Find<WorkbenchPanel>();
                if (found != null && found != workbench && found.IsOpen)
                    again = found;
            }
            Assert.IsNotNull(again, "BitSorter opened again");
            Assert.AreEqual(SaveHold.None, Find<ProgressTracker>().Store.Held);
            Assert.IsTrue(Find<ProgressTracker>().Store.IsComplete("half-adder"), "the save read, now nothing holds it");
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
