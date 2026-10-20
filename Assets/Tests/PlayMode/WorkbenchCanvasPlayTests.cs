using System.Collections;
using System.Linq;
using BitSorter.Board;
using BitSorter.View;
using BitSorter.ViewModel;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.TestTools;
using UnityEngine.UI;
using BoardPoint = BitSorter.Board.Point;
using Object = UnityEngine.Object;

namespace BitSorter.PlayMode.Tests
{
    /// <summary>
    /// The workbench's canvas edited in the real game (Part 10a), one real gesture each through the fixture's own
    /// mouse and keyboard: a part taken from the strip and placed with a click, dragged on the grid, a box drawn
    /// round two parts, Delete, and Ctrl+Z; MY CIRCUIT kept across reopening; Space still playing and pausing, and
    /// with a drag panning; Escape putting a part in hand down before it leaves. What each gesture decides is held
    /// under dotnet (<c>CanvasTests</c>); these hold that the mouse reaches it.
    /// </summary>
    [TestFixture]
    public class WorkbenchCanvasPlayTests : InputTestFixture
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
            _mouse = InputSystem.AddDevice<Mouse>();
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
            GameObject found = GameObject.Find(name);
            Assert.IsNotNull(found, $"no '{name}' on screen");
            found.GetComponent<Button>().onClick.Invoke();
            yield return null;
            yield return null;
        }

        /// <summary>The workbench open on MY CIRCUIT, blank, its run made.</summary>
        private static IEnumerator OpenMyCircuit()
        {
            yield return Click(MainMenu.WorkbenchButton);
            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            Assert.IsTrue(workbench.IsOpen);
            if (workbench.Sample != 0)
                workbench.ShowSample(0);
            yield return null;
            yield return null;
            Assert.AreEqual(0, workbench.Sample);
        }

        private IEnumerator Tap(KeyControl key)
        {
            Press(key);
            yield return null;
            Release(key);
            yield return null;
        }

        /// <summary>The pointer to <paramref name="at"/> on the board, a frame for the panel to see it.</summary>
        private IEnumerator PointAt(WorkbenchPanel workbench, BoardPoint at)
        {
            Move(_mouse.position, workbench.ScreenPointOf(at));
            yield return null;
        }

        /// <summary>A left press at <paramref name="from"/>, dragged to <paramref name="to"/> in a few steps, and let go.</summary>
        private IEnumerator Drag(WorkbenchPanel workbench, BoardPoint from, BoardPoint to, ButtonControl button = null)
        {
            button ??= _mouse.leftButton;
            Vector2 a = workbench.ScreenPointOf(from), b = workbench.ScreenPointOf(to);
            Move(_mouse.position, a);
            yield return null;
            Press(button);
            yield return null;
            for (int step = 1; step <= 4; step++)
            {
                Move(_mouse.position, Vector2.Lerp(a, b, step / 4f));
                yield return null;
            }

            Release(button);
            yield return null;
            yield return null;
        }

        /// <summary>A left click at <paramref name="at"/> on the board.</summary>
        private IEnumerator ClickAt(WorkbenchPanel workbench, BoardPoint at)
        {
            yield return PointAt(workbench, at);
            Press(_mouse.leftButton);
            yield return null;
            Release(_mouse.leftButton);
            yield return null;
            yield return null;
        }

        private static Module Top(WorkbenchPanel workbench) => workbench.Session.Project.TopModule;

        /// <summary>Frames until the session's replay has taken over, with a cap that fails loudly.</summary>
        private static IEnumerator UntilReplayed(WorkbenchPanel workbench)
        {
            for (int frame = 0; frame < 600 && workbench.Session.Replaying; frame++)
                yield return null;
            Assert.IsFalse(workbench.Session.Replaying, "the replay never took over");
            yield return null;
        }

        [UnityTest]
        public IEnumerator APartFromTheStrip_IsPlacedWithAClick_DraggedOnTheGrid_BoxedWithAnother_AndTakenAwayWithDelete()
        {
            yield return TestScene.Load();
            yield return OpenMyCircuit();
            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            Assert.IsEmpty(Top(workbench).Parts);

            // An input, taken from the strip and placed with a click, on the grid.
            yield return Click(WorkbenchPanel.PartButtonName("input"));
            Assert.IsNotNull(workbench.Editor.InHand, "the strip's button should put an input in hand");
            yield return ClickAt(workbench, new BoardPoint(2.1, -2.05));
            Part input = Top(workbench).Parts.Single();
            Assert.IsInstanceOf<PortPart>(input);
            Assert.AreEqual(new Placement(2, -2), Top(workbench).Layout.PlacementOf(input.Id), "placed on the grid under the click");
            Assert.IsNull(workbench.Editor.InHand, "placed, and put down");
            yield return UntilReplayed(workbench);
            CollectionAssert.Contains(workbench.List.AllSignals.Select(s => s.Key).ToArray(), input.Name, "the input placed has its row in the waveform");

            // An AND, placed and then dragged a cell to the right.
            Assert.IsTrue(workbench.TakeFromStrip("and"));
            yield return ClickAt(workbench, new BoardPoint(6, -2));
            int and = Top(workbench).Parts.Single(p => p is GatePart).Id;
            yield return Drag(workbench, new BoardPoint(6, -2), new BoardPoint(7.1, -2.08));
            Assert.AreEqual(new Placement(7, -2), Top(workbench).Layout.PlacementOf(and), "dragged a cell on, landing on the grid");

            // A box round both, from empty space inside the circuit's window.
            yield return Drag(workbench, new BoardPoint(0.5, -0.5), new BoardPoint(9, -4));
            CollectionAssert.AreEquivalent(new[] { input.Id, and }, workbench.Editor.Selection.Parts, "the box took both");

            yield return Tap(_keyboard.deleteKey);
            Assert.IsEmpty(Top(workbench).Parts, "Delete took the selection away");

            Press(_keyboard.leftCtrlKey);
            yield return Tap(_keyboard.zKey);
            Release(_keyboard.leftCtrlKey);
            yield return null;
            Assert.AreEqual(2, Top(workbench).Parts.Count, "Ctrl+Z brought both back, in one step");
        }

        [UnityTest]
        public IEnumerator MyCircuit_IsKept_AndTheWorkbenchOpensOnItWhenItHasAnythingInIt()
        {
            yield return TestScene.Load();
            yield return OpenMyCircuit();
            WorkbenchPanel workbench = Find<WorkbenchPanel>();

            Assert.IsTrue(workbench.TakeFromStrip("xor"));
            yield return ClickAt(workbench, new BoardPoint(4, -2));
            Assert.AreEqual(1, Top(workbench).Parts.Count);

            // To the counter and back: MY CIRCUIT is what it was.
            workbench.ShowSample(3);
            yield return null;
            workbench.ShowSample(0);
            yield return null;
            Assert.AreEqual(1, Top(workbench).Parts.Count, "MY CIRCUIT was kept while another sample was shown");

            yield return Click(WorkbenchPanel.BackButton);
            Assert.IsFalse(workbench.IsOpen);
            yield return Click(MainMenu.WorkbenchButton);
            Assert.AreEqual(0, workbench.Sample, "opened on MY CIRCUIT, now that it has something in it");
            Assert.AreEqual(BitSorter.Sim.GateKind.Xor, ((GatePart)Top(workbench).Parts.Single()).Kind);
        }

        [UnityTest]
        public IEnumerator Space_PlaysAndPausesOnItsRelease_AndWithADragPansInstead()
        {
            yield return TestScene.Load();
            yield return Click(MainMenu.WorkbenchButton);
            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            yield return null;

            bool playing = workbench.Playing;
            yield return Tap(_keyboard.spaceKey);
            Assert.AreNotEqual(playing, workbench.Playing, "Space alone still plays or pauses");
            playing = workbench.Playing;

            // Space held, and a drag: the circuit moves with the pointer, and nothing plays or pauses.
            Vec2 point = workbench.Model.Layout.Size * 0.5f;
            Vector2 before = workbench.ScreenPointOf(point);
            Press(_keyboard.spaceKey);
            yield return null;
            Move(_mouse.position, before);
            yield return null;
            Press(_mouse.leftButton);
            yield return null;
            Move(_mouse.position, before + new Vector2(40f, -30f));
            yield return null;
            Release(_mouse.leftButton);
            yield return null;
            Release(_keyboard.spaceKey);
            yield return null;

            Vector2 after = workbench.ScreenPointOf(point);
            Assert.AreEqual(before.x + 40f, after.x, 2f, "Space and a drag pans the circuit");
            Assert.AreEqual(before.y - 30f, after.y, 2f);
            Assert.AreEqual(playing, workbench.Playing, "and its release, having panned, plays and pauses nothing");
            Assert.IsTrue(workbench.Editor.Selection.IsEmpty, "nor did the drag draw a box");
        }

        [UnityTest]
        public IEnumerator Escape_PutsThePartInHandDown_BeforeItLeavesTheWorkbench()
        {
            yield return TestScene.Load();
            yield return OpenMyCircuit();
            WorkbenchPanel workbench = Find<WorkbenchPanel>();

            Assert.IsTrue(workbench.TakeFromStrip("not"));
            yield return PointAt(workbench, new BoardPoint(3, -2));
            Assert.IsTrue(workbench.Overlay.Hand.HasValue, "the part in hand is drawn where a click would place it");

            yield return Tap(_keyboard.escapeKey);
            Assert.IsNull(workbench.Editor.InHand, "Escape put it down");
            Assert.IsTrue(workbench.IsOpen, "and left the workbench open");

            yield return Tap(_keyboard.escapeKey);
            Assert.IsFalse(workbench.IsOpen, "a second Escape leaves");
        }

        /// <summary>
        /// MY CIRCUIT blank stands on the narrowest stage there is, and the waveform's buttons and the legend's sentence
        /// still fit on it: how narrow a stage may be is set by them, never by the circuit.
        /// </summary>
        [UnityTest]
        public IEnumerator OnTheNarrowestStage_TheWaveformsButtonsAndTheLegend_StayOnIt()
        {
            yield return TestScene.Load();
            yield return OpenMyCircuit();

            RectTransform stage = GameObject.Find(WorkbenchPanel.RootName).GetComponentsInChildren<RectTransform>(true).First(r => r.name == "stage");
            Rect on = WorldRect(stage);
            Button[] buttons = stage.GetComponentsInChildren<Button>();
            Assert.IsNotEmpty(buttons, "sanity: the waveform's buttons are on the stage");
            foreach (Button button in buttons)
                Assert.LessOrEqual(WorldRect(button.GetComponent<RectTransform>()).xMax, on.xMax + 0.5f, $"'{button.name}' runs past the stage's right edge");

            TMP_Text legend = stage.GetComponentsInChildren<TMP_Text>().First(t => t.text == WorkbenchPanel.LegendText);
            Assert.LessOrEqual(legend.preferredWidth, legend.rectTransform.rect.width + 0.5f, "the legend's sentence runs past its box");
        }

        private static Rect WorldRect(RectTransform rect)
        {
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            return Rect.MinMaxRect(corners[0].x, corners[0].y, corners[2].x, corners[2].y);
        }
    }
}
