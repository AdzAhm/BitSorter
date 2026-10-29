using System.Collections;
using System.Linq;
using BitSorter.Board;
using BitSorter.SystemVerilog;
using BitSorter.View;
using BitSorter.ViewModel;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace BitSorter.PlayMode.Tests
{
    /// <summary>
    /// The exercises on the workbench (Part 15d): the start page's column and the EXERCISES page, opening one on its brief
    /// and its hint, its test locked and saying why, CHECK failing with every check at its moment and then solving it, the
    /// solved card with the student's best and NEXT, an exercise open last opening again as it was left, and a circuit of
    /// the student's own leaving exercise mode.
    /// </summary>
    [TestFixture]
    public class ExercisePlayTests : InputTestFixture
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

        private static WorkbenchPanel Workbench => Object.FindFirstObjectByType<WorkbenchPanel>();

        private static ProgressStore Store => Object.FindFirstObjectByType<ProgressTracker>().Store;

        private static IEnumerator Click(string name)
        {
            UnityEngine.UIElements.Button shell = ShellPanel.Find<UnityEngine.UIElements.Button>(name);
            Assert.IsNotNull(shell, $"no '{name}' on screen");
            ShellPanel.Submit(shell);
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

        private static bool Shown(string name)
        {
            VisualElement element = ShellPanel.Find<VisualElement>(name);
            return element != null && element.resolvedStyle.display == DisplayStyle.Flex;
        }

        /// <summary>The save as the last session left it, written before the game loads.</summary>
        private static void Plant(System.Action<ProgressStore> write)
        {
            var store = new ProgressStore(ProgressStore.DefaultPath);
            store.Load();
            write(store);
            store.Save();
        }

        /// <summary>The half adder built on the exercise open: an XOR for sum, an AND for carry, both from a and b.</summary>
        private static void BuildTheHalfAdder(WorkbenchSession session)
        {
            int Place(string key, double x, double y)
            {
                EditResult placed = session.Place(PartsCatalog.Find(session.Project, session.Module, key), new Placement(x, y));
                Assert.IsTrue(placed.Done, placed.Refusal);
                return placed.Id;
            }

            int Port(string name) => session.Project.TopModule.Ports.Single(p => p.Name == name).Id;

            void Wire(int from, string fromPin, int to, string toPin)
            {
                EditResult wired = session.Connect(Endpoint.At(from, fromPin), Endpoint.At(to, toPin));
                Assert.IsTrue(wired.Done, wired.Refusal);
            }

            int xor = Place("xor", 8, -2), and = Place("and", 8, 2);
            foreach (int gate in new[] { xor, and })
            {
                Wire(Port("a"), "out", gate, "a");
                Wire(Port("b"), "out", gate, "b");
            }
            Wire(xor, "y", Port("sum"), "in");
            Wire(and, "y", Port("carry"), "in");
            session.Finish();
        }

        [UnityTest]
        public IEnumerator TheStartPage_CarriesOnWithTheExercises_AndTheirPageOpensOne()
        {
            yield return TestScene.LoadSimulator();
            WorkbenchPanel workbench = Workbench;
            Exercise first = ExerciseCatalog.Run[0];
            Assert.IsTrue(workbench.StartPageShowing, "sanity: a fresh save opens on the start page");
            StringAssert.StartsWith("START: 1  " + first.Title.ToUpperInvariant(), workbench.StartContinueCaption, "nothing solved: the first");

            yield return Click(WorkbenchPanel.StartExercisesItem);
            Assert.IsTrue(workbench.ExercisesShowing, "ALL EXERCISES opens the page");
            Assert.AreEqual("NEXT", workbench.ExerciseStatus(first.Id));
            Assert.AreEqual("", workbench.ExerciseStatus("half-adder"));
            Assert.AreEqual(ExerciseCatalog.Run.Count, ExerciseCatalog.Run.Select(e => e.Id).Count(id => workbench.ExerciseStatus(id) != null),
                "every exercise has its item");

            yield return Click(WorkbenchPanel.ExerciseItem("half-adder"));
            Assert.IsFalse(workbench.ExercisesShowing);
            Assert.IsFalse(workbench.StartPageShowing, "an exercise open takes both pages down");
            Assert.AreEqual("half-adder", workbench.ExerciseOpen?.Id);
            Assert.AreEqual("half-adder", workbench.Session.Project.Settings.Exercise, "its own document, locked");
            Assert.IsTrue(Shown(WorkbenchPanel.GoalButton) && Shown(WorkbenchPanel.CheckButton), "GOAL and CHECK");
            Assert.AreEqual(Documents.ExerciseKey("half-adder"), Store.LastOpen, "remembered as what is open");

            ExerciseCard card = workbench.ExerciseCard;
            for (int frame = 0; frame < 10 && workbench.GoalPending; frame++)
                yield return null;
            Assert.IsTrue(card.IsOpen, "its brief, as it opens");
            yield return null;
            yield return null;
            Assert.Greater(ShellPanel.ScreenRectOf(ShellPanel.Find<VisualElement>(ExerciseCard.HeadingName)).xMin, Screen.width / 2f,
                "under GOAL, at the window's right, not its left edge");
            StringAssert.Contains("HALF ADDER", card.Heading);
            Assert.AreEqual(workbench.ExerciseOpen.Goal, card.Goal);
            Assert.AreEqual("", card.Hint, "the hint is asked for");
            yield return Click(ExerciseCard.HintItem);
            Assert.AreEqual("HINT: " + workbench.ExerciseOpen.Hint, card.Hint);

            yield return Tap(_keyboard.escapeKey);
            Assert.IsFalse(card.IsOpen, "Escape closes the card");
            Assert.AreEqual("half-adder", workbench.ExerciseOpen?.Id, "and nothing more");
        }

        [UnityTest]
        public IEnumerator AnExercisesTest_IsRefusedAnyEdit_SayingWhy_AndWhatTheStudentBuildsIsKept()
        {
            yield return TestScene.LoadSimulator();
            WorkbenchPanel workbench = Workbench;
            workbench.OpenExercise("half-adder");
            yield return null;
            Assert.IsNull(PartsCatalog.Find(workbench.Session.Project, workbench.Session.Module, "input"), "no port in PARTS: its ports are its test's");
            Assert.AreEqual("XOR 1", PartsCatalog.Caption(workbench.Session.Project, PartsCatalog.Find(workbench.Session.Project, workbench.Session.Module, "xor")));

            EditResult set = workbench.Session.SetInput("a", 0, 10, LogicVec.FromBinary("1"));
            Assert.IsFalse(set.Done);
            StringAssert.Contains("The exercise's own test drives the inputs", set.Refusal);

            BuildTheHalfAdder(workbench.Session);
            workbench.OpenDocument(WorkbenchSamples.MyCircuitKey);
            yield return null;
            Assert.IsNull(workbench.ExerciseOpen, "a circuit of the student's own leaves the exercise");
            Assert.IsFalse(Shown(WorkbenchPanel.CheckButton), "and CHECK with it");
            Assert.IsTrue(Store.TryGetProject("half-adder", out Project kept, new System.Collections.Generic.List<string>()), "the answer kept under its id");
            Assert.AreEqual(2, kept.TopModule.Parts.OfType<GatePart>().Count());

            workbench.OpenExercise("half-adder");
            yield return null;
            Assert.AreEqual(2, workbench.Session.Project.TopModule.Parts.OfType<GatePart>().Count(), "and opened again as it was left");
        }

        [UnityTest]
        public IEnumerator Check_ThatFails_ListsEachCheckAtItsMoment_ThenSolvesIt_AndOffersTheNext()
        {
            yield return TestScene.LoadSimulator();
            WorkbenchPanel workbench = Workbench;
            workbench.OpenExercise("half-adder");
            yield return null;

            yield return Click(WorkbenchPanel.CheckButton);
            Grade failed = workbench.LastGrade;
            Assert.IsNotNull(failed);
            Assert.IsFalse(failed.Solved, "nothing built");
            StringAssert.Contains("checks failed", workbench.ChipText);
            Assert.IsTrue(workbench.TranscriptShown, "the TRANSCRIPT, which lists them");
            Assert.AreEqual(failed.Failures[0].Time, workbench.Time, 1e-9, "the run taken to the first");

            // Each failed check is the testbench's own line, at its moment, once the run has been made that far.
            int FailLines() => workbench.Transcript.Lines.Count(l => l.IsMessage && l.Text.StartsWith("FAIL"));
            for (int frame = 0; frame < 600 && FailLines() < failed.Failures.Count; frame++)
                yield return null;
            Assert.AreEqual(failed.Failures.Count, FailLines(), "each failed check, at its moment, once");
            Assert.AreEqual(ExerciseRun.Verdict(workbench.ExerciseOpen, failed, "ns"), workbench.Transcript.Notes[0].Text, "the verdict at the head");
            Assert.IsFalse(Store.IsComplete("half-adder"));

            BuildTheHalfAdder(workbench.Session);
            yield return Click(WorkbenchPanel.CheckButton);
            Grade solved = workbench.LastGrade;
            Assert.IsTrue(solved.Solved, string.Join("\n", solved.Faults.Concat(solved.Failures.Select(f => f.ToString()))));
            StringAssert.StartsWith("CHECK: solved", workbench.ChipText);
            Assert.IsTrue(Store.IsComplete("half-adder"), "marked solved");
            Assert.IsTrue(Store.TryGetExerciseBest("half-adder", out int gates, out _), "and its best kept");
            Assert.AreEqual(2, gates);

            ExerciseCard card = workbench.ExerciseCard;
            Assert.IsTrue(card.IsOpen && card.Solved, "the solved card");
            StringAssert.Contains("It took 2 gates", card.Goal);
            Assert.AreEqual("Your first solve of it.", card.Hint);
            Exercise next = ExerciseRun.Next(ExerciseCatalog.Run, Store.IsComplete, after: "half-adder");
            StringAssert.StartsWith("NEXT: ", card.NextCaption);
            StringAssert.EndsWith(next.Title.ToUpperInvariant(), card.NextCaption);

            yield return Click(ExerciseCard.NextItem);
            Assert.AreEqual(next.Id, workbench.ExerciseOpen?.Id, "NEXT opens it");
            workbench.ShowExercises();
            yield return null;
            Assert.AreEqual("SOLVED", workbench.ExerciseStatus("half-adder"));
        }

        [UnityTest]
        public IEnumerator WriteIt_TurnsTheAnswerToCode_ThatChecks_AndDrawIt_AsksAndTurnsItBack()
        {
            yield return TestScene.LoadSimulator();
            WorkbenchPanel workbench = Workbench;
            workbench.OpenExercise("half-adder");
            yield return null;
            workbench.ExerciseCard.Close();
            Assert.IsTrue(Shown(WorkbenchPanel.CodeButton), "an exercise that may be answered in code offers it");

            yield return Click(WorkbenchPanel.CodeButton);
            WorkbenchSession session = workbench.Session;
            Assert.IsTrue(session.Project.TopModule.IsWritten, "the top written");
            Assert.IsTrue(session.ShowsCode, "and the circuit's window shows its code");
            StringAssert.Contains("answered in code", session.Project.TopModule.Source);
            Assert.AreEqual(WorkbenchPanel.DrawItCaption, ShellPanel.Find<UnityEngine.UIElements.Button>(WorkbenchPanel.CodeButton).text);

            string header = session.Project.TopModule.Source.Substring(0, session.Project.TopModule.Source.IndexOf("module "));
            EditResult typed = session.SetSource(header + "module half_adder (input logic a, input logic b, output logic sum, output logic carry);\n" +
                "    xor #1 g1 (sum, a, b);\n    and #1 g2 (carry, a, b);\nendmodule\n");
            Assert.IsTrue(typed.Done, typed.Refusal);
            yield return Click(WorkbenchPanel.CheckButton);
            Assert.IsTrue(workbench.LastGrade.Solved, string.Join("\n", workbench.LastGrade.Faults.Concat(workbench.LastGrade.Problems)));
            Assert.AreEqual(2, workbench.LastGrade.Gates, "its gates counted from the code");
            workbench.ExerciseCard.Close();

            // Left and opened again: an answer written in code comes back on its code, not its drawing (the review of #111).
            workbench.OpenExercise("balance-the-paths");
            yield return null;
            workbench.OpenExercise("half-adder");
            yield return null;
            session = workbench.Session;
            Assert.IsTrue(session.Project.TopModule.IsWritten, "sanity: the written answer kept under the exercise's id");
            Assert.IsTrue(session.ShowsCode, "reopened on its code");
            if (workbench.ExerciseCard.IsOpen)
                workbench.ExerciseCard.Close();

            yield return Click(WorkbenchPanel.CodeButton);
            Assert.IsTrue(session.Project.TopModule.IsWritten, "DRAW IT asks first");
            yield return Click(AskMenu.YesItem);
            Assert.IsFalse(workbench.Session.Project.TopModule.IsWritten, "drawn again");
            Assert.AreEqual(WorkbenchPanel.WriteItCaption, ShellPanel.Find<UnityEngine.UIElements.Button>(WorkbenchPanel.CodeButton).text);
        }

        [UnityTest]
        public IEnumerator TheTutorial_FollowsTheDocument_LightsWhatItMeans_AndEndsOnCheck()
        {
            yield return TestScene.LoadSimulator();
            WorkbenchPanel workbench = Workbench;
            yield return Click(WorkbenchPanel.StartTutorialItem);
            Assert.IsTrue(workbench.TutorialRunning);
            Assert.AreEqual(GuidedTutorial.ExerciseId, workbench.ExerciseOpen?.Id, "on the first exercise");
            Assert.IsFalse(workbench.StartPageShowing);
            Assert.AreEqual(GuidedTutorial.TextFor(GuidedTutorial.Step.TakeTheNot), workbench.LessonText);
            Assert.AreEqual(WorkbenchPanel.PartButtonName("not"), workbench.Lit?.name, "the NOT in PARTS lit");
            UnityEngine.UIElements.Button notButton = ShellPanel.Find<UnityEngine.UIElements.Button>(WorkbenchPanel.PartButtonName("not"));
            Color before = SchematicPalette.Current.Outline;

            yield return Click(WorkbenchPanel.PartButtonName("not"));
            Assert.AreEqual(GuidedTutorial.Step.PutItDown, workbench.TutorialStep, "in hand");
            yield return null;
            Assert.AreEqual(before, notButton.resolvedStyle.borderLeftColor, "its edge put back as it was once the step moves on");

            WorkbenchSession session = workbench.Session;
            EditResult placed = session.Place(PartsCatalog.Find(session.Project, session.Module, "not"), new Placement(6, 0));
            Assert.IsTrue(placed.Done, placed.Refusal);
            int Port(string name) => session.Project.TopModule.Ports.Single(p => p.Name == name).Id;
            Assert.IsTrue(session.Connect(Endpoint.At(Port(GuidedTutorial.In), "out"), Endpoint.At(placed.Id, "a")).Done);
            yield return null;
            Assert.AreEqual(GuidedTutorial.Step.WireOut, workbench.TutorialStep);
            Assert.IsNull(workbench.Lit, "a wire is drawn on the canvas: nothing to light");
            Assert.IsTrue(session.Connect(Endpoint.At(placed.Id, "y"), Endpoint.At(Port(GuidedTutorial.Out), "in")).Done);
            session.Finish();
            yield return null;
            Assert.AreEqual(GuidedTutorial.Step.Check, workbench.TutorialStep);
            Assert.AreEqual(WorkbenchPanel.CheckButton, workbench.Lit?.name, "CHECK lit");

            yield return Click(WorkbenchPanel.CheckButton);
            Assert.IsTrue(workbench.LastGrade.Solved);
            yield return null;
            Assert.AreEqual(GuidedTutorial.Step.Done, workbench.TutorialStep);
            Assert.AreEqual(WorkbenchPanel.CloseLesson, ShellPanel.Find<UnityEngine.UIElements.Button>(WorkbenchPanel.LessonButtonName).text);
            yield return Click(WorkbenchPanel.LessonButtonName);
            Assert.IsFalse(workbench.TutorialRunning);
            Assert.IsNull(workbench.LessonText);
        }

        [UnityTest]
        public IEnumerator TheTutorial_IsSkipped_FromHELP_AsItWasStarted()
        {
            yield return TestScene.LoadSimulator();
            WorkbenchPanel workbench = Workbench;
            workbench.OpenHelpMenu();
            yield return null;
            yield return Click(HelpMenu.ActionItem + HelpAction.Tutorial);
            Assert.IsTrue(workbench.TutorialRunning, "HELP, TUTORIAL");
            yield return Click(WorkbenchPanel.LessonButtonName);
            Assert.IsFalse(workbench.TutorialRunning, "SKIP stops it");
            Assert.IsNull(workbench.Lit, "and lights nothing");
            Assert.AreEqual(GuidedTutorial.ExerciseId, workbench.ExerciseOpen?.Id, "the exercise stays open");
        }

        [UnityTest]
        public IEnumerator AFirstTimeHint_ComesUpOnceTheRunShowsIt_AndOnlyOnce()
        {
            yield return TestScene.LoadSimulator();
            WorkbenchPanel workbench = Workbench;
            workbench.OpenExercise("half-adder");
            yield return null;
            workbench.ExerciseCard.Close();
            BuildTheHalfAdder(workbench.Session);
            workbench.Restart();

            for (int frame = 0; frame < 600 && workbench.HintShown == null; frame++)
                yield return null;
            Assert.AreEqual(SimulatorHints.GateDelay, workbench.HintShown, "a gate's output moved after its input");
            Assert.AreEqual(SimulatorHints.TextFor(SimulatorHints.GateDelay), workbench.LessonText);
            Assert.IsTrue(Store.HasSeenHint(SimulatorHints.GateDelay), "kept as seen as it went up");

            yield return Click(WorkbenchPanel.LessonButtonName);
            Assert.IsNull(workbench.LessonText, "GOT IT takes it down");
            workbench.Restart();
            for (int frame = 0; frame < 120; frame++)
                yield return null;
            Assert.IsNull(workbench.HintShown, "and it does not come back");
        }

        /// <summary>
        /// The exercise open, while it is unsolved, is the one to do next: the page marks it NEXT and CONTINUE offers it. It
        /// marked the one after it, while the student was still on it (the 15f browser smoke test).
        /// </summary>
        [UnityTest]
        public IEnumerator TheExerciseOpen_IsNext_WhileItIsUnsolved()
        {
            yield return TestScene.LoadSimulator();
            WorkbenchPanel workbench = Workbench;
            Exercise second = ExerciseCatalog.Run[1];
            Store.MarkComplete(ExerciseCatalog.Run[0].Id);
            workbench.OpenExercise(second.Id);
            yield return null;
            workbench.ShowExercises();
            yield return null;
            Assert.AreEqual("NEXT", workbench.ExerciseStatus(second.Id), "the one open, unsolved");
            StringAssert.EndsWith(second.Title.ToUpperInvariant(), workbench.StartContinueCaption, "and CONTINUE offers it");
        }

        [UnityTest]
        public IEnumerator AnExerciseOpenLast_OpensAgain_AsItWasLeft()
        {
            Exercise exercise = ExerciseCatalog.Run.Single(e => e.Id == "half-adder");
            Plant(store =>
            {
                var problems = new System.Collections.Generic.List<string>();
                Project opened = exercise.ToProject(problems);
                var editor = new BoardEditor(opened);
                Assert.IsTrue(editor.Add(opened.Top, new GatePart { Kind = BitSorter.Sim.GateKind.Xor, Inputs = 2, Rise = 1, Fall = 1 }, new Placement(8, 0)).Done);
                store.SetProject("half-adder", editor.Project);
                store.LastOpen = Documents.ExerciseKey("half-adder");
            });
            yield return TestScene.LoadSimulator();
            WorkbenchPanel workbench = Workbench;

            Assert.IsFalse(workbench.StartPageShowing);
            Assert.AreEqual("half-adder", workbench.ExerciseOpen?.Id);
            Assert.AreEqual(1, workbench.Session.Project.TopModule.Parts.OfType<GatePart>().Count(), "its XOR, as it was left");
            Assert.IsTrue(Shown(WorkbenchPanel.CheckButton));
            Assert.AreEqual(workbench.ExerciseOpen.Goal, workbench.Description, "the header says its goal");
        }

        [UnityTest]
        public IEnumerator EXERCISES_InTheHeader_IsAPage_ThatEscapeTakesDown()
        {
            yield return TestScene.LoadSimulator();
            WorkbenchPanel workbench = Workbench;
            yield return Tap(_keyboard.escapeKey);
            Assert.IsFalse(workbench.StartPageShowing, "sanity: MY CIRCUIT under it");

            yield return Click(WorkbenchPanel.ExercisesButton);
            Assert.IsTrue(workbench.ExercisesShowing);
            Assert.AreEqual(WorkbenchPanel.ExercisesSays, workbench.Description);
            yield return Tap(_keyboard.spaceKey);
            Assert.IsFalse(workbench.Playing, "nothing under it plays");

            yield return Tap(_keyboard.escapeKey);
            Assert.IsFalse(workbench.ExercisesShowing, "Escape takes it down");
            Assert.IsTrue(workbench.IsOpen, "and nothing more");
            Assert.IsNull(workbench.ExerciseOpen);

            workbench.ShowExercises();
            yield return null;
            workbench.NewCircuit("under the page");
            yield return null;
            Assert.IsFalse(workbench.ExercisesShowing, "a circuit made from FILE under the page takes it down");
        }
    }
}
