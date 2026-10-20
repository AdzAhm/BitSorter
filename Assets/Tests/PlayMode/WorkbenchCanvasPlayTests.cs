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
    /// with a drag panning; Escape putting a part in hand down before it leaves. And wires (Part 10b): a half adder
    /// built from nothing with the mouse, its inputs flipped on the waveform, and a wire being drawn let go with
    /// Escape. What each gesture decides is held under dotnet (<c>CanvasTests</c>, <c>WiringTests</c>); these hold
    /// that the mouse reaches it.
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

        /// <summary>Where a part's pin is on the board, as the part stands now.</summary>
        private static BoardPoint PinOf(WorkbenchPanel workbench, int part, string pin)
        {
            Module top = Top(workbench);
            return PartGeometry.Placed(workbench.Session.Project, top.FindPart(part), top.Layout.PlacementOf(part)).Pins.Single(p => p.Pin == pin).At;
        }

        /// <summary>A point <paramref name="along"/> of the way along a wire, as it is drawn now.</summary>
        private static BoardPoint OnWireAt(WorkbenchPanel workbench, int wire, double along)
        {
            var points = WireGeometry.Wires(workbench.Session.Project, Top(workbench))[wire];
            return WireGeometry.PointAt(points, WireGeometry.Length(points) * along);
        }

        /// <summary>A part taken from the strip and placed with a click at <paramref name="at"/>; its id is the one the click selected.</summary>
        private IEnumerator Place(WorkbenchPanel workbench, string key, BoardPoint at, System.Collections.Generic.List<int> ids)
        {
            Assert.IsTrue(workbench.TakeFromStrip(key), key);
            yield return ClickAt(workbench, at);
            Assert.AreEqual(1, workbench.Editor.Selection.Parts.Count, $"the {key} was not placed at {at}: {workbench.Editor.Refusal}");
            ids.Add(workbench.Editor.Selection.Parts.Single());
        }

        /// <summary>A row of the waveform by what it is labelled.</summary>
        private static int Row(WorkbenchPanel workbench, string label)
        {
            WaveList list = workbench.List;
            return Enumerable.Range(0, list.Lines.Count).Single(i => list.LabelOf(list.Lines[i]) == label);
        }

        /// <summary>What a row shows at <paramref name="time"/>, in binary.</summary>
        private static string ValueOf(WorkbenchPanel workbench, string label, double time)
        {
            SignalTrack track = workbench.Model.RowTrack(Row(workbench, label));
            int index = track.IndexAt(time);
            return index < 0 ? "none" : track[index].Value.Format(BitSorter.SystemVerilog.Radix.Binary);
        }

        /// <summary>Two clicks at a point of the screen, as close together as a double-click.</summary>
        private IEnumerator DoubleClick(Vector2 point)
        {
            Move(_mouse.position, point);
            yield return null;
            for (int click = 0; click < 2; click++)
            {
                Press(_mouse.leftButton);
                yield return null;
                Release(_mouse.leftButton);
                yield return null;
            }

            yield return null;
        }

        /// <summary>
        /// The plan's exit for Part 10b: a half adder built from nothing with the fixture's own mouse -- two inputs, an
        /// XOR and an AND, two outputs, wired by dragging pin to pin, the AND's inputs dragged onto the XOR's wires as
        /// taps -- then its inputs flipped by double-clicks on the waveform, and its sum and carry read there.
        /// </summary>
        [UnityTest]
        public IEnumerator AHalfAdder_BuiltFromNothingWithTheMouse_AddsOnTheWaveform()
        {
            yield return TestScene.Load();
            yield return OpenMyCircuit();
            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            workbench.Pause();

            var ids = new System.Collections.Generic.List<int>();
            yield return Place(workbench, "input", new BoardPoint(1, -1), ids);
            yield return Place(workbench, "input", new BoardPoint(1, -4), ids);
            yield return Place(workbench, "xor", new BoardPoint(5, -1), ids);
            yield return Place(workbench, "and", new BoardPoint(5, -4), ids);
            yield return Place(workbench, "output", new BoardPoint(9, -1), ids);
            yield return Place(workbench, "output", new BoardPoint(9, -4), ids);
            int a = ids[0], b = ids[1], xor = ids[2], and = ids[3], sum = ids[4], carry = ids[5];

            // Pin to pin, a drag each.
            foreach ((int from, string fromPin, int to, string toPin) in new[] { (a, "out", xor, "a"), (b, "out", xor, "b"), (xor, "y", sum, "in"), (and, "y", carry, "in") })
            {
                int before = Top(workbench).Wires.Count;
                yield return Drag(workbench, PinOf(workbench, from, fromPin), PinOf(workbench, to, toPin));
                Assert.AreEqual(before + 1, Top(workbench).Wires.Count, $"no wire from {from}.{fromPin} to {to}.{toPin}: {workbench.Editor.Refusal}");
                Assert.IsFalse(workbench.Editor.Drafting, "made, and nothing left being drawn");
            }

            // The AND's inputs onto the XOR's: a tap each, where the drag let go.
            int aToXor = Top(workbench).Wires.Single(w => !w.From.IsTap && w.From.Pin.Part == a && !w.To.IsTap).Id;
            int bToXor = Top(workbench).Wires.Single(w => !w.From.IsTap && w.From.Pin.Part == b && !w.To.IsTap).Id;
            foreach ((string pin, int wire) in new[] { ("a", aToXor), ("b", bToXor) })
            {
                yield return Drag(workbench, PinOf(workbench, and, pin), OnWireAt(workbench, wire, 0.3));
                Assert.IsTrue(Top(workbench).Wires.Any(w => w.To.IsTap && w.To.TapOf == wire && w.From.Pin.Part == and && w.From.Pin.Pin == pin),
                    $"the AND's {pin} did not tap wire {wire}: {workbench.Editor.Refusal}");
            }

            Assert.AreEqual(6, Top(workbench).Wires.Count);
            Assert.IsEmpty(workbench.Session.Editor.Diagnostics.Where(d => d.IsError), "a half adder with no errors");

            // Flipped on the waveform: a, then b, each a double-click on its row.
            string nameA = Top(workbench).FindPart(a).Name, nameB = Top(workbench).FindPart(b).Name;
            string nameS = Top(workbench).FindPart(sum).Name, nameC = Top(workbench).FindPart(carry).Name;
            workbench.Session.Finish();
            yield return null;
            workbench.Seek(60.0);
            yield return null;
            Assert.AreEqual(("0", "0"), (ValueOf(workbench, nameS, 50), ValueOf(workbench, nameC, 50)), "0 + 0");

            yield return DoubleClick(workbench.WaveScreenPointOf(30.0, Row(workbench, nameA)));
            workbench.Session.Finish();
            yield return null;
            Assert.AreEqual("1", ValueOf(workbench, nameA, 50), "a flipped");
            Assert.AreEqual(("1", "0"), (ValueOf(workbench, nameS, 50), ValueOf(workbench, nameC, 50)), "1 + 0");

            yield return DoubleClick(workbench.WaveScreenPointOf(30.0, Row(workbench, nameB)));
            workbench.Session.Finish();
            yield return null;
            Assert.AreEqual(("0", "1"), (ValueOf(workbench, nameS, 50), ValueOf(workbench, nameC, 50)), "1 + 1: sum 0, carry 1");
        }

        /// <summary>A wire started with a click on a pin is drawn after the pointer, says why it may not end on another output, and Escape lets it go before the workbench.</summary>
        [UnityTest]
        public IEnumerator AWireStartedWithAClick_FollowsThePointer_AndEscapeLetsItGo_BeforeTheWorkbench()
        {
            yield return TestScene.Load();
            yield return OpenMyCircuit();
            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            var ids = new System.Collections.Generic.List<int>();
            yield return Place(workbench, "not", new BoardPoint(4, -2), ids);
            yield return Place(workbench, "not", new BoardPoint(4, -5), ids);

            yield return ClickAt(workbench, PinOf(workbench, ids[0], "y"));
            Assert.IsTrue(workbench.Editor.Drafting, "a click on a pin starts a wire");
            yield return PointAt(workbench, new BoardPoint(7, -3));
            yield return null;
            Assert.IsNotNull(workbench.Overlay.Draft, "drawn after the pointer");
            Assert.IsNull(workbench.HoverSays, "over nothing, the chip is down");

            yield return PointAt(workbench, PinOf(workbench, ids[1], "y"));
            yield return null;
            Assert.IsTrue(workbench.Overlay.DraftRefused, "an output onto an output is drawn as an end it cannot take");
            StringAssert.Contains("both drive one wire", workbench.HoverSays, "and the chip says why, before the click");

            yield return Tap(_keyboard.escapeKey);
            Assert.IsFalse(workbench.Editor.Drafting, "Escape let the wire go");
            Assert.IsTrue(workbench.IsOpen, "and only that");
            Assert.IsEmpty(Top(workbench).Wires, "making nothing");
        }

        private static Rect WorldRect(RectTransform rect)
        {
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            return Rect.MinMaxRect(corners[0].x, corners[0].y, corners[2].x, corners[2].y);
        }
    }
}
