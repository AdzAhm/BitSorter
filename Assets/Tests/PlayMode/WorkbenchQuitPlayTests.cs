using System.Collections;
using System.Collections.Generic;
using BitSorter.Board;
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
    /// QUIT (5.1; Ahmad: "I couldn't exit the simulator unless I exited full screen"): at the window's bottom right, at the
    /// status line's right end, over the start page as well as a circuit; it asks first, above itself, and CANCEL, Escape
    /// and a second click leave the simulator running; its answer keeps the circuit and the code typed and not applied
    /// before it quits. The quitter is the fixture's own, which counts and quits nothing.
    /// </summary>
    [TestFixture]
    public class WorkbenchQuitPlayTests : InputTestFixture
    {
        private Mouse _mouse;
        private Keyboard _keyboard;
        private int _quits;

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
            _mouse = InputSystem.AddDevice<Mouse>();
            _mouse.MakeCurrent();
            _quits = 0;
            WorkbenchPanel.Quitter = () => _quits++;
        }

        public override void TearDown()
        {
            WorkbenchPanel.Quitter = () => { };   // as SaveGuard leaves it, for the next fixture
            SaveGuard.Clear();
            base.TearDown();
        }

        [UnityTearDown]
        public IEnumerator ClearTheScene()
        {
            yield return TestScene.Clear();
        }

        private static T Find<T>() where T : Object => Object.FindFirstObjectByType<T>();

        private static VisualElement QuitButton => ShellPanel.Find<VisualElement>(WorkbenchPanel.QuitButtonName);

        /// <summary>
        /// Waits until QUIT stands where the layout puts it, three frames running -- the shell laid out, the window still --
        /// with a cap that fails loudly. One frame after the load was not always enough after a domain reload: the click went
        /// in before QUIT was laid out, and opened nothing.
        /// </summary>
        private static IEnumerator Settle(WorkbenchPanel workbench)
        {
            int still = 0;
            for (int frame = 0; frame < 300 && still < 3; frame++)
            {
                yield return null;
                VisualElement quit = QuitButton;
                Rect2 expected = ShellLayout.QuitIn(workbench.Regions.Status);
                bool placed = quit != null && quit.layout.width > 0f && Mathf.Abs(quit.layout.x - expected.x) < 1f
                    && Mathf.Abs(quit.layout.y - expected.y) < 1f;
                still = placed ? still + 1 : 0;
            }
            Assert.AreEqual(3, still, "QUIT never stood where the layout puts it");
        }

        private IEnumerator ClickOn(VisualElement element)
        {
            Assert.IsNotNull(element, "nothing to click");
            Vector2 at = ShellPanel.ScreenPointOf(element);
            Move(_mouse.position, at);
            yield return null;
            Assert.IsTrue(ShellPanel.OverControlAt(at), $"{element.name} is a control there, nothing over it");
            Press(_mouse.leftButton);
            yield return null;
            Release(_mouse.leftButton);
            yield return null;
            yield return null;
        }

        private static void AssertColour(Color expected, Color actual, string what) =>
            Assert.Less(((Vector4)expected - (Vector4)actual).magnitude, 0.01f, $"{what}: {expected} expected, {actual} drawn");

        private IEnumerator Tap(KeyControl key)
        {
            Press(key);
            yield return null;
            Release(key);
            yield return null;
        }

        /// <summary>
        /// QUIT is at the status line's right end, where the layout says (<see cref="ShellLayout.QuitIn"/>), and the status
        /// line's text keeps clear of it; a click on it asks first, above it, and quits nothing yet.
        /// </summary>
        [UnityTest]
        public IEnumerator Quit_StandsAtTheBottomRight_AndAsksFirst_AboveItself()
        {
            yield return TestScene.Load();
            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            yield return Settle(workbench);

            VisualElement quit = QuitButton;
            Assert.IsNotNull(quit, "a desktop build, and the editor, has QUIT");
            Assert.AreEqual(DisplayStyle.Flex, quit.resolvedStyle.display);
            Rect2 expected = ShellLayout.QuitIn(workbench.Regions.Status);
            Assert.AreEqual(expected.x, quit.layout.x, 1f, "at the status line's right end");
            Assert.AreEqual(expected.y, quit.layout.y, 1f, "in its row");
            Assert.AreEqual(expected.width, quit.layout.width, 1f);
            VisualElement status = ShellPanel.Find<VisualElement>(WorkbenchPanel.StatusName);
            Assert.LessOrEqual(status.layout.xMax, quit.layout.xMin, "the status line's text keeps clear of it");

            yield return ClickOn(quit);
            Assert.IsTrue(workbench.Ask.IsOpen, "it asks first");
            Assert.AreEqual(WorkbenchPanel.QuitQuestion, workbench.Ask.Question);
            Rect asked = ShellPanel.ScreenRectOf(ShellPanel.Find<VisualElement>(AskMenu.QuestionName).parent);
            Rect button = ShellPanel.ScreenRectOf(quit);
            Assert.GreaterOrEqual(asked.yMin, button.yMax - 0.5f, "the question opens above QUIT, never under the pointer");
            Assert.AreEqual(0, _quits, "nothing is quit by the click alone");

            // Red, as RESET PROGRESS is (Ahmad: "make it red"): the button and its answer, in the colour every error is written in.
            SchematicPalette palette = SchematicPalette.Current;
            AssertColour(palette.Unknown, quit.resolvedStyle.borderTopColor, "QUIT's edge is the red");
            AssertColour(palette.Legible(palette.Unknown, palette.Ground), quit.resolvedStyle.color, "and its caption, made to read");
            Assert.IsTrue(workbench.Ask.AnswerIsDestructive, "its answer red too");
            VisualElement answer = ShellPanel.Find<VisualElement>(AskMenu.YesItem);
            yield return null;
            AssertColour(palette.Unknown, answer.resolvedStyle.borderTopColor, "the answer's edge is the red");
        }

        /// <summary>CANCEL, Escape and a second click on QUIT -- a double-click -- each leave the simulator running.</summary>
        [UnityTest]
        public IEnumerator Cancel_Escape_AndADoubleClick_LeaveItRunning()
        {
            yield return TestScene.Load();
            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            yield return Settle(workbench);

            yield return ClickOn(QuitButton);
            Assert.IsTrue(workbench.Ask.IsOpen, "sanity: asked");
            yield return ClickOn(ShellPanel.Find<VisualElement>(AskMenu.NoItem));
            Assert.IsFalse(workbench.Ask.IsOpen, "CANCEL takes the question down");

            yield return ClickOn(QuitButton);
            Assert.IsTrue(workbench.Ask.IsOpen, "sanity: asked again");
            yield return Tap(_keyboard.escapeKey);
            Assert.IsFalse(workbench.Ask.IsOpen, "Escape takes it down");
            Assert.IsTrue(workbench.IsOpen, "and nothing else: the workbench is still up");

            yield return ClickOn(QuitButton);
            yield return ClickOn(QuitButton);
            Assert.AreEqual(0, _quits, "a double-click on QUIT never answers its own question");
        }

        /// <summary>
        /// QUIT's own answer, pressed: the quitter called once. The pair to the test above, so its count of none is about
        /// presses that could have quit and did not.
        /// </summary>
        [UnityTest]
        public IEnumerator TheAnswer_Quits()
        {
            yield return TestScene.Load();
            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            yield return Settle(workbench);

            yield return ClickOn(QuitButton);
            Assert.IsTrue(workbench.Ask.IsOpen, "sanity: asked");
            yield return ClickOn(ShellPanel.Find<VisualElement>(AskMenu.YesItem));
            Assert.AreEqual(1, _quits, "QUIT quits");
        }

        /// <summary>
        /// QUIT stands over the start page too, which covers the status line: BitSorter opens there with nothing open, and
        /// there was no way out of a fullscreen window from it.
        /// </summary>
        [UnityTest]
        public IEnumerator OverTheStartPage_QuitIsThere_AndAsks()
        {
            yield return TestScene.LoadSimulator();
            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            yield return Settle(workbench);
            Assert.IsTrue(workbench.StartPageShowing, "sanity: it opens on the start page");

            yield return ClickOn(QuitButton);   // a control there: nothing of the page over it
            Assert.IsTrue(workbench.Ask.IsOpen, "QUIT asks over the start page");
            Assert.AreEqual(WorkbenchPanel.QuitQuestion, workbench.Ask.Question);
        }

        /// <summary>
        /// The answer keeps the circuit at its last edit and the code typed and not applied, as its draft, before it quits --
        /// in the same frame as the edit, before the half second the save otherwise waits for, so only the answer can have
        /// written them.
        /// </summary>
        [UnityTest]
        public IEnumerator TheAnswer_KeepsTheCircuitAndTheTypedCode_BeforeItQuits()
        {
            yield return TestScene.Load();
            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            workbench.ShowSample(0);   // MY CIRCUIT, blank under SaveGuard
            yield return null;
            yield return null;

            // All in one frame: an edit, then MY CIRCUIT's top as code (5.1's CODE switch) with text typed into it.
            Assert.IsTrue(workbench.Session.AddWrittenModule("kept").Done, "sanity: an edit");
            workbench.Session.DrawCode(false);   // false: not the drawing -- its code
            CodeDocument code = workbench.Session.Code;
            Assert.IsNotNull(code, "sanity: its code to type into");
            string typed = code.Text + "\nassign\n";   // never applies: a fault after endmodule
            code.Type(typed, Time.timeAsDouble * 1000.0 + 60000.0);   // a minute ahead, so its pause never runs out

            bool keptTheEdit = false;
            string keptDraft = null;
            WorkbenchPanel.Quitter = () =>
            {
                _quits++;
                var onDisk = new ProgressStore(ProgressStore.DefaultPath);
                onDisk.Load();
                keptTheEdit = onDisk.TryGetProject(WorkbenchSamples.MyCircuitKey, out Project project, new List<string>())
                    && project.Find("kept") != null;
                keptDraft = onDisk.Drafts(WorkbenchSamples.MyCircuitKey).TryGetValue(workbench.Session.Project.Top, out string draft) ? draft : null;
            };
            workbench.Quit();

            Assert.AreEqual(1, _quits);
            Assert.IsTrue(keptTheEdit, "the circuit at its last edit was in the save before it quit");
            Assert.AreEqual(typed, keptDraft, "and the code typed and not applied, as its draft");
        }
    }
}
