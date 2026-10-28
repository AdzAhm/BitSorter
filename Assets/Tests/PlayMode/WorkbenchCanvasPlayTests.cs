using System.Collections;
using System.Linq;
using BitSorter.Board;
using BitSorter.SystemVerilog;
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
    /// built from nothing with the mouse, its inputs flipped on the waveform, a wire being drawn let go with
    /// Escape, and a selected wire reshaped by a drag on its piece (Part 10b-2); and the inspector (Part 10c), a
    /// gate's delay changed and a net named through it; and copy, paste, the right-click menu and the minimap (Part
    /// 10d). What each gesture decides is held under dotnet (<c>CanvasTests</c>, <c>WiringTests</c>); these hold
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
            // The workbench's toolbar is the shell's (Part 11a): a button there is pressed as Enter on it would.
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

            yield return Click(WorkbenchPanel.SettingsButton);
            Assert.IsTrue(Find<SettingsPanel>().IsShowing, "Settings, a page over the workbench (Part 15g)");
            yield return Click(SettingsPanel.BackButton);
            Assert.IsTrue(workbench.IsOpen, "back from it");
            Assert.AreEqual(0, workbench.Sample, "on MY CIRCUIT, as it was left");
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
        public IEnumerator Escape_PutsThePartInHandDown_AndNeverLeavesTheWorkbench()
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
            Assert.IsTrue(workbench.IsOpen, "nor does a second: nothing is behind the workbench (Part 15a)");
        }

        /// <summary>
        /// Placing a port puts a row on the waveform, and the circuit stays where it was, so the next click lands where the
        /// pointer is. The stage was fitted to its height as it grew, so every port placed shrank the whole page by a
        /// row, the board under the pointer with it (the 10d browser smoke test); docked (Part 11b), the row goes into the
        /// waves and the circuit's window keeps its size.
        /// </summary>
        [UnityTest]
        public IEnumerator PlacingAPort_AddsARow_AndLeavesThePageWhereItWas()
        {
            yield return TestScene.Load();
            yield return OpenMyCircuit();
            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            workbench.Pause();
            var under = new BoardPoint(6, -3);
            Vector2 before = workbench.ScreenPointOf(under);
            int rows = workbench.Model.RowCount;

            var ids = new System.Collections.Generic.List<int>();
            yield return Place(workbench, "input", new BoardPoint(1, -1), ids);
            yield return Place(workbench, "output", new BoardPoint(9, -1), ids);
            yield return UntilReplayed(workbench);

            Assert.AreEqual(rows + 2, workbench.Model.RowCount, "sanity: a row for each port");
            Vector2 after = workbench.ScreenPointOf(under);
            Assert.Less(Vector2.Distance(before, after), 0.5f, $"the board moved under the pointer, from {before} to {after}");
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
            // Counted, not only selected: a click that places nothing leaves the last part placed selected, and a point off
            // the circuit's window once passed here placing nothing at all.
            int before = workbench.Session.Project.Find(workbench.Session.Module).Parts.Count;
            Assert.IsTrue(workbench.TakeFromStrip(key), key);
            yield return ClickAt(workbench, at);
            Assert.AreEqual(before + 1, workbench.Session.Project.Find(workbench.Session.Module).Parts.Count,
                $"the {key} was not placed at {at}: {workbench.Editor.Refusal}");
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

        /// <summary>
        /// A lead on every pin (Part 15g): the pointer near enough to a pin to wire from it lights the pin's circle and the
        /// status line says to drag; while a wire is drawn, the pins it may end on show their circles -- the other NOT's input,
        /// and not the outputs, its own start among them.
        /// </summary>
        [UnityTest]
        public IEnumerator APin_LightsUnderThePointer_AndAWireBeingDrawn_ShowsWhereItMayEnd()
        {
            yield return TestScene.Load();
            yield return OpenMyCircuit();
            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            var ids = new System.Collections.Generic.List<int>();
            yield return Place(workbench, "not", new BoardPoint(4, -2), ids);
            yield return Place(workbench, "not", new BoardPoint(4, -5), ids);
            yield return ClickAt(workbench, new BoardPoint(8, 0));   // nothing chosen, nothing in hand

            BoardPoint start = PinOf(workbench, ids[0], "y");
            yield return PointAt(workbench, start);
            yield return null;
            Assert.AreEqual(CanvasTargetKind.Pin, workbench.Editor.PinOver.Kind, "the pointer on a pin");
            Assert.AreEqual("y", workbench.Editor.PinOver.Pin);
            Assert.IsNotNull(workbench.Overlay.PinLit, "its circle lit");
            StringAssert.Contains("drag to wire", workbench.StatusText, "and the status line says what a press there does");

            yield return PointAt(workbench, new BoardPoint(8, 0));
            yield return null;
            Assert.IsNull(workbench.Overlay.PinLit, "off the pin, nothing lit");

            yield return ClickAt(workbench, start);
            Assert.IsTrue(workbench.Editor.Drafting, "sanity: a click on the pin starts a wire");
            yield return PointAt(workbench, new BoardPoint(7, -3));
            yield return null;
            Assert.IsNull(workbench.Overlay.PinLit, "nothing lit as a start while a wire is drawn");
            var ends = new System.Collections.Generic.List<BoardPoint>();
            foreach (Vec2 at in workbench.Overlay.PinEnds)
                ends.Add(workbench.Editor.ToBoard(at));
            bool Has(BoardPoint p) => ends.Exists(e => System.Math.Abs(e.X - p.X) + System.Math.Abs(e.Y - p.Y) < 1e-3);
            Assert.IsTrue(Has(PinOf(workbench, ids[1], "a")), "the other NOT's input shows where the wire may end");
            Assert.IsFalse(Has(start), "not the wire's own start");
            Assert.IsFalse(Has(PinOf(workbench, ids[1], "y")), "nor another output, which two drivers on one wire would refuse");

            yield return Tap(_keyboard.escapeKey);
        }

        /// <summary>
        /// A wire clicked is selected, its corners drawn as handles, and a drag on one of its pieces moves the piece
        /// across itself -- one step, layout only, nothing replayed (Part 10b-2).
        /// </summary>
        [UnityTest]
        public IEnumerator ASelectedWire_DraggedByAPiece_IsReshaped_AndNothingReplays()
        {
            yield return TestScene.Load();
            yield return OpenMyCircuit();
            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            workbench.Pause();

            var ids = new System.Collections.Generic.List<int>();
            yield return Place(workbench, "input", new BoardPoint(1, -1), ids);
            yield return Place(workbench, "xor", new BoardPoint(6, -3), ids);
            yield return Drag(workbench, PinOf(workbench, ids[0], "out"), PinOf(workbench, ids[1], "a"));
            int wire = Top(workbench).Wires.Single().Id;
            workbench.Session.Finish();
            yield return null;

            var route = WireGeometry.Wires(workbench.Session.Project, Top(workbench))[wire];
            Assert.AreEqual(4, route.Count, $"sanity: across, down half-way, across: {string.Join(" ", route)}");

            yield return ClickAt(workbench, new BoardPoint(3, -5));
            Assert.IsFalse(workbench.Editor.Selection.HasWire(wire), "sanity: a click on nothing lets the new wire go");
            yield return ClickAt(workbench, OnWireAt(workbench, wire, 0.15));
            Assert.IsTrue(workbench.Editor.Selection.HasWire(wire), "a click on the wire selects it");
            Assert.AreEqual(2, workbench.Overlay.Handles.Count, "its two corners are handles");

            // Its fall, half-way across, dragged a cell right.
            BoardPoint grab = new BoardPoint(route[1].X, (route[1].Y + route[2].Y) / 2);
            yield return Drag(workbench, grab, new BoardPoint(grab.X + 1.05, grab.Y));
            Assert.IsTrue(Top(workbench).Layout.Routes.ContainsKey(wire), "reshaped: the wire keeps bends of its own");
            Assert.AreEqual(route[1].X + 1, Top(workbench).Layout.Routes[wire].Bends[0].X, 1e-9, "its fall a cell right, on the grid");
            Assert.IsFalse(workbench.Session.Replaying, "and nothing replays: a wire's shape changes nothing the circuit does");
        }

        /// <summary>The workbench open on the reference circuit, paused, its run made.</summary>
        private static IEnumerator OpenTheReference()
        {
            yield return Click(MainMenu.WorkbenchButton);
            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            workbench.ShowSample(1);
            yield return null;
            workbench.Pause();
            workbench.Session.Finish();
            yield return null;
            yield return null;
        }

        /// <summary>The middle of a part's outline, as it is drawn now.</summary>
        private static BoardPoint MiddleOf(WorkbenchPanel workbench, int part)
        {
            Module top = Top(workbench);
            PartOutline outline = PartGeometry.Placed(workbench.Session.Project, top.FindPart(part), top.Layout.PlacementOf(part));
            return new BoardPoint((outline.Left + outline.Right) / 2, (outline.Bottom + outline.Top) / 2);
        }

        /// <summary>
        /// The plan's exit for Part 10c, its first half, docked (Part 11b-2): a double-click on the reference's AND puts
        /// the keyboard in the inspector's bar, a new rise typed and Enter make it -- one step -- and the run is played
        /// again with it. A width the document forbids is refused in the bar, in its words, and REVERT puts it back.
        /// </summary>
        [UnityTest]
        public IEnumerator ADoubleClickOnAGate_OpensItsInspector_AndANewDelayIsPlayedAgain()
        {
            yield return TestScene.Load();
            yield return OpenTheReference();
            WorkbenchPanel workbench = Find<WorkbenchPanel>();

            GatePart and = Top(workbench).Parts.OfType<GatePart>().Single(g => g.Kind == BitSorter.Sim.GateKind.And && g.Fall == 3);
            WorkbenchModel before = workbench.Model;
            yield return DoubleClick(workbench.ScreenPointOf(MiddleOf(workbench, and.Id)));

            Assert.IsNotNull(workbench.Inspection, "a double-click on the AND inspects it");
            StringAssert.StartsWith("AND ", workbench.Inspection.Title);
            Assert.AreEqual("2 ps", workbench.InspectorTyped()["rise"]);
            Assert.IsTrue(UiText.Typing, "the bar's first field has the keyboard");
            UnityEngine.UIElements.Label quickSays = ShellPanel.Find<UnityEngine.UIElements.Label>(WorkbenchPanel.QuickSaysName);
            Assert.IsNotNull(quickSays, "the quick panel says what its fields mean");
            StringAssert.Contains("Tpdlh", quickSays.text, "rise, a change to 1");
            StringAssert.Contains("Tpdhl", quickSays.text, "and fall, a change to 0");

            Assert.IsTrue(workbench.InspectorType("rise", "5"));
            yield return EnterIn(WorkbenchPanel.InspectorFieldName("rise"));
            Assert.AreEqual(5, ((GatePart)Top(workbench).FindPart(and.Id)).Rise, "Enter made it");
            Assert.IsNull(workbench.InspectorRefusal);

            yield return UntilReplayed(workbench);
            Assert.AreNotSame(before, workbench.Model, "the run shown is the one played again, with the AND's new rise");
            Assert.IsTrue(workbench.IsOpen, "Enter did not leave the workbench");
            Assert.AreEqual("5 ps", workbench.InspectorTyped()["rise"], "and the bar reads the AND again");

            // A width the document forbids is refused in the bar, which keeps what was typed for it to be put right.
            workbench.InspectorType("width", "2000");
            workbench.InspectorApply();
            yield return null;
            StringAssert.Contains("1 to 1024", workbench.InspectorRefusal, "with the document's words in it");
            Assert.AreEqual("2000", workbench.InspectorTyped()["width"]);
            yield return Click(WorkbenchPanel.InspectorRevertName);
            Assert.AreEqual("1", workbench.InspectorTyped()["width"], "REVERT puts it back");
            Assert.IsNull(workbench.InspectorRefusal, "and the refusal with it");
        }

        /// <summary>Enter in a shell field, as a press of it does: UI Toolkit hands the field the key, the game's keyboard has it too.</summary>
        private IEnumerator EnterIn(string fieldName)
        {
            UnityEngine.UIElements.TextField field = ShellPanel.Find<UnityEngine.UIElements.TextField>(fieldName);
            Assert.IsNotNull(field, $"no '{fieldName}' in the shell");
            Press(_keyboard.enterKey);
            ShellPanel.SendKey(field, KeyCode.Return);
            yield return null;
            Release(_keyboard.enterKey);
            yield return null;
            yield return null;
        }

        /// <summary>
        /// The side panel's quick fields (Part 11b-2): a gate selected has its rise and fall there, a rise typed and Enter
        /// make it on its own, and the bar reads it; with nothing selected, and with two parts, the bar says so.
        /// </summary>
        [UnityTest]
        public IEnumerator AQuickField_MakesItsOwnChange_AndTheBarSaysWhatIsSelected()
        {
            yield return TestScene.Load();
            yield return OpenTheReference();
            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            Assert.AreEqual("NOTHING SELECTED", workbench.InspectorTitle);
            Assert.IsNull(ShellPanel.Find<UnityEngine.UIElements.TextField>(WorkbenchPanel.QuickFieldName("rise")), "no quick fields for nothing");

            GatePart and = Top(workbench).Parts.OfType<GatePart>().Single(g => g.Kind == BitSorter.Sim.GateKind.And && g.Fall == 3);
            yield return ClickAt(workbench, MiddleOf(workbench, and.Id));
            Assert.IsTrue(workbench.Editor.Selection.HasPart(and.Id), "sanity: the click selects the AND");
            StringAssert.StartsWith("AND ", workbench.InspectorTitle, "the bar follows the selection");
            UnityEngine.UIElements.TextField rise = ShellPanel.Find<UnityEngine.UIElements.TextField>(WorkbenchPanel.QuickFieldName("rise"));
            Assert.IsNotNull(rise, "a gate's rise is at hand");
            Assert.IsNotNull(ShellPanel.Find<UnityEngine.UIElements.TextField>(WorkbenchPanel.QuickFieldName("fall")), "and its fall");
            Assert.IsNull(ShellPanel.Find<UnityEngine.UIElements.TextField>(WorkbenchPanel.QuickFieldName("width")), "and nothing else");
            Assert.AreEqual("2 ps", rise.value);

            rise.value = "4";
            yield return EnterIn(WorkbenchPanel.QuickFieldName("rise"));
            Assert.AreEqual(4, ((GatePart)Top(workbench).FindPart(and.Id)).Rise, "Enter in a quick field makes it");
            Assert.AreEqual("4 ps", workbench.InspectorTyped()["rise"], "and the bar reads it");

            GatePart other = Top(workbench).Parts.OfType<GatePart>().Single(g => g.Kind == BitSorter.Sim.GateKind.And && g.Id != and.Id);
            Press(_keyboard.leftShiftKey);
            yield return ClickAt(workbench, MiddleOf(workbench, other.Id));
            Release(_keyboard.leftShiftKey);
            yield return null;
            Assert.AreEqual("2 PARTS SELECTED", workbench.InspectorTitle, "two parts are said, not inspected");
            Assert.IsNull(workbench.Inspection);
        }

        /// <summary>
        /// The plan's exit for Part 10c, its second half: NOT B's net, added to the waveform with Alt and a click, then
        /// named through its inspector -- opened with Enter on the wire selected -- says its new name on the hover chip
        /// and on its row, which keeps its place.
        /// </summary>
        [UnityTest]
        public IEnumerator ANetNamedInTheInspector_IsNamedSoOnTheHoverChip_AndOnItsRow()
        {
            yield return TestScene.Load();
            yield return OpenTheReference();
            WorkbenchPanel workbench = Find<WorkbenchPanel>();

            Netlist nets = Netlist.Of(workbench.Session.Project, Top(workbench));
            int wire = nets.Named("nb").Wires[0];
            BoardPoint on = OnWireAt(workbench, wire, 0.5);

            // Its row, as Alt and a click add it.
            yield return PointAt(workbench, on);
            Press(_keyboard.leftAltKey);
            yield return ClickAt(workbench, on);
            Release(_keyboard.leftAltKey);
            yield return null;
            int row = Row(workbench, "nb");

            // Selected with a click, and Enter puts the keyboard in its inspector.
            yield return ClickAt(workbench, on);
            Assert.IsTrue(workbench.Editor.Selection.HasWire(wire), "the click selects NOT B's wire");
            StringAssert.StartsWith("WIRE nb", workbench.InspectorTitle, "the bar inspects the wire selected");
            yield return Tap(_keyboard.enterKey);
            Assert.IsTrue(UiText.Typing, "Enter put the keyboard in the bar's first field");

            workbench.InspectorType("name", "not_b");
            yield return Click(WorkbenchPanel.InspectorApplyName);
            Assert.IsNull(workbench.InspectorRefusal, "APPLY made it");
            yield return UntilReplayed(workbench);

            Assert.AreEqual(row, Row(workbench, "not_b"), "its row says its new name, where it was");
            Assert.GreaterOrEqual(workbench.List.Lines[row].Signal.Signal, 0, "and finds it in the run played again");

            yield return PointAt(workbench, new BoardPoint(on.X, on.Y + 3));
            yield return PointAt(workbench, OnWireAt(workbench, wire, 0.5));
            yield return null;
            StringAssert.Contains("not_b", workbench.HoverSays, "the hover chip says it");
        }

        /// <summary>
        /// Enter gives the inspector's first field the keyboard without being the field's Enter as well. The platform
        /// hands a key to UI Toolkit too, and in the browser that half can come after the game has read the key: the field
        /// given the keyboard in Enter's frame took the Enter, submitted what it held and let go (the Part 11d smoke test).
        /// </summary>
        [UnityTest]
        public IEnumerator Enter_GivesTheInspectorTheKeyboard_AndIsNotItsEnterToo()
        {
            yield return TestScene.Load();
            yield return OpenTheReference();
            WorkbenchPanel workbench = Find<WorkbenchPanel>();

            GatePart and = Top(workbench).Parts.OfType<GatePart>().Single(g => g.Kind == BitSorter.Sim.GateKind.And && g.Fall == 3);
            yield return ClickAt(workbench, MiddleOf(workbench, and.Id));
            StringAssert.StartsWith("AND ", workbench.InspectorTitle, "sanity: the click selects the AND");
            UnityEngine.UIElements.TextField first = ShellPanel.Find<UnityEngine.UIElements.TextField>(WorkbenchPanel.InspectorFieldName("name"));
            Assert.IsNotNull(first, "sanity: the AND's inspector has a name field");

            // Enter, its UI Toolkit half arriving in the frame the game read it, after the game.
            Press(_keyboard.enterKey);
            yield return null;
            using (UnityEngine.UIElements.KeyDownEvent down = UnityEngine.UIElements.KeyDownEvent.GetPooled('\0', KeyCode.Return, EventModifiers.None))
            {
                first.panel.visualTree.SendEvent(down);
            }

            Release(_keyboard.enterKey);
            yield return null;
            yield return null;
            yield return null;

            Assert.IsTrue(UiText.Typing, "the Enter that asked for the inspector was its field's Enter too, and let it go");
            Assert.IsNull(workbench.InspectorRefusal, "and submitted nothing");
        }

        /// <summary>
        /// Part 12a: a double-click on the full adder's h1 steps inside it -- the canvas draws the half adder, its line
        /// saying an edit there changes both -- and the breadcrumb's top crumb steps back out; STEP INSIDE in h2's quick
        /// panel steps into h2, and Alt+Up out, where Up alone still scrolls the waves.
        /// </summary>
        [UnityTest]
        public IEnumerator ADoubleClickStepsInsideABlock_AndTheBreadcrumbAndAltUp_StepOut()
        {
            yield return TestScene.Load();
            yield return Click(MainMenu.WorkbenchButton);
            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            workbench.ShowSample(2);   // the full adder
            yield return null;
            workbench.Pause();
            workbench.Session.Finish();
            yield return null;
            yield return null;
            int h1 = Top(workbench).FindPart("h1").Id, h2 = Top(workbench).FindPart("h2").Id;

            yield return DoubleClick(workbench.ScreenPointOf(MiddleOf(workbench, h1)));
            CollectionAssert.AreEqual(new[] { "h1" }, workbench.SteppedPath, "a double-click on h1 steps inside it");
            Assert.AreEqual("half_adder", workbench.ShownModule, "the canvas draws the half adder");
            Assert.IsNull(workbench.Inspection, "and inspects nothing");
            StringAssert.Contains("all 2 of them", workbench.InspectorSays, "an edit inside changes both half adders, and the line says so");
            UnityEngine.UIElements.VisualElement crumbs = ShellPanel.Find<UnityEngine.UIElements.VisualElement>(WorkbenchPanel.BreadcrumbName);
            Assert.AreEqual(UnityEngine.UIElements.DisplayStyle.Flex, crumbs.resolvedStyle.display, "the breadcrumb says where the canvas is");

            yield return Click(WorkbenchPanel.CrumbName + "0");
            Assert.IsEmpty(workbench.SteppedPath, "the top's crumb steps back out");
            Assert.AreEqual("full_adder", workbench.ShownModule);
            Assert.AreEqual(UnityEngine.UIElements.DisplayStyle.None, crumbs.resolvedStyle.display, "and at the top there is no breadcrumb");

            yield return ClickAt(workbench, MiddleOf(workbench, h2));
            StringAssert.StartsWith("BLOCK h2", workbench.InspectorTitle, "sanity: the click selects h2");
            yield return Click(WorkbenchPanel.StepInsideName);
            CollectionAssert.AreEqual(new[] { "h2" }, workbench.SteppedPath, "its quick panel's STEP INSIDE");

            yield return Tap(_keyboard.upArrowKey);
            CollectionAssert.AreEqual(new[] { "h2" }, workbench.SteppedPath, "Up alone scrolls the waves and steps nowhere");
            // Alt held first, as a hand holds it, and Up pressed with it.
            Press(_keyboard.leftAltKey);
            yield return null;
            yield return Tap(_keyboard.upArrowKey);
            Release(_keyboard.leftAltKey);
            yield return null;
            Assert.IsEmpty(workbench.SteppedPath, "Alt+Up steps out");
        }

        private IEnumerator Chord(KeyControl key)
        {
            Press(_keyboard.leftCtrlKey);
            yield return Tap(key);
            Release(_keyboard.leftCtrlKey);
            yield return null;
        }

        /// <summary>A right-click at <paramref name="at"/> on the board.</summary>
        private IEnumerator RightClickAt(WorkbenchPanel workbench, BoardPoint at)
        {
            yield return PointAt(workbench, at);
            Press(_mouse.rightButton);
            yield return null;
            Release(_mouse.rightButton);
            yield return null;
            yield return null;
        }

        /// <summary>A NOT and an AND boxed and copied with Ctrl+C, pasted a cell on with Ctrl+V as one step, which Ctrl+Z takes back (Part 10d).</summary>
        [UnityTest]
        public IEnumerator CtrlCAndCtrlV_PasteTheSelection_ACellOn_AsOneStep()
        {
            yield return TestScene.Load();
            yield return OpenMyCircuit();
            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            workbench.Pause();

            var ids = new System.Collections.Generic.List<int>();
            yield return Place(workbench, "not", new BoardPoint(3, -2), ids);
            yield return Place(workbench, "and", new BoardPoint(6, -2), ids);
            yield return Drag(workbench, PinOf(workbench, ids[0], "y"), PinOf(workbench, ids[1], "a"));
            Assert.AreEqual(1, Top(workbench).Wires.Count, "sanity: the NOT into the AND");

            yield return Drag(workbench, new BoardPoint(1, 0), new BoardPoint(9, -4));
            CollectionAssert.AreEquivalent(ids, workbench.Editor.Selection.Parts, "sanity: both boxed");
            yield return Chord(_keyboard.cKey);
            yield return Chord(_keyboard.vKey);

            Assert.AreEqual(4, Top(workbench).Parts.Count, "two more parts");
            Assert.AreEqual(2, Top(workbench).Wires.Count, "and the wire between them");
            CollectionAssert.IsEmpty(workbench.Editor.Selection.Parts.Intersect(ids), "what the paste made is selected");
            Part pastedNot = Top(workbench).Parts.Single(p => !ids.Contains(p.Id) && p is GatePart g && g.Kind == BitSorter.Sim.GateKind.Not);
            Assert.AreEqual(new Placement(4, -3), Top(workbench).Layout.PlacementOf(pastedNot.Id), "a cell right and a cell down");

            yield return Chord(_keyboard.zKey);
            Assert.AreEqual(2, Top(workbench).Parts.Count, "Ctrl+Z took the paste back, whole");
        }

        /// <summary>Two parts selected, a right-click on one opens the canvas's menu, and ALIGN TOP lines them up (Part 10d).</summary>
        [UnityTest]
        public IEnumerator ARightClickOnTheSelection_OpensTheMenu_AndAlignTopLinesItUp()
        {
            yield return TestScene.Load();
            yield return OpenMyCircuit();
            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            workbench.Pause();

            var ids = new System.Collections.Generic.List<int>();
            yield return Place(workbench, "and", new BoardPoint(3, -2), ids);
            yield return Place(workbench, "or", new BoardPoint(7, -3.5), ids);
            yield return Drag(workbench, new BoardPoint(1, 0), new BoardPoint(9, -6));
            CollectionAssert.AreEquivalent(ids, workbench.Editor.Selection.Parts, "sanity: both boxed");

            yield return RightClickAt(workbench, MiddleOf(workbench, ids[1]));
            Assert.IsTrue(workbench.CanvasMenu.IsOpen, "a right-click on the selection opens the menu");
            CollectionAssert.AreEquivalent(ids, workbench.Editor.Selection.Parts, "and keeps the selection for it");
            UnityEngine.UIElements.Button Offered(CanvasMenuAction action)
            {
                UnityEngine.UIElements.Button item = ShellPanel.Find<UnityEngine.UIElements.Button>(CanvasMenu.ActionItem + action);
                return item != null && item.style.display.value != UnityEngine.UIElements.DisplayStyle.None ? item : null;
            }

            UnityEngine.UIElements.Button alignTop = Offered(CanvasMenuAction.AlignTop);
            Assert.IsNotNull(alignTop, "ALIGN TOP is offered for two parts");
            Assert.IsNull(Offered(CanvasMenuAction.SpreadAcross), "SPREAD needs three");

            ShellPanel.Submit(alignTop);
            yield return null;
            Assert.IsFalse(workbench.CanvasMenu.IsOpen);
            Module top = Top(workbench);
            PartOutline a = PartGeometry.Placed(workbench.Session.Project, top.FindPart(ids[0]), top.Layout.PlacementOf(ids[0]));
            PartOutline b = PartGeometry.Placed(workbench.Session.Project, top.FindPart(ids[1]), top.Layout.PlacementOf(ids[1]));
            Assert.AreEqual(a.Top, b.Top, 1e-9, "their tops in line");
            Assert.IsFalse(workbench.Session.Replaying, "and nothing replays");
        }

        /// <summary>
        /// Part 12b: h2 and the OR selected, the canvas's menu's MAKE BLOCK asks for a name -- block1 offered -- and Enter makes
        /// the block, selected, in PARTS, the waves' rows as they were; Escape in the name asks nothing and makes nothing.
        /// </summary>
        [UnityTest]
        public IEnumerator MakeBlock_AsksForAName_AndEnterMakesTheBlock_WhereEscapeMakesNothing()
        {
            yield return TestScene.Load();
            yield return Click(MainMenu.WorkbenchButton);
            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            workbench.ShowSample(2);   // the full adder
            yield return null;
            workbench.Pause();
            workbench.Session.Finish();
            yield return null;
            int h2 = Top(workbench).FindPart("h2").Id, or = Top(workbench).Parts.Single(p => p is GatePart).Id;
            int rows = workbench.Model.RowCount;

            yield return ClickAt(workbench, MiddleOf(workbench, h2));
            Press(_keyboard.leftShiftKey);
            yield return ClickAt(workbench, MiddleOf(workbench, or));
            Release(_keyboard.leftShiftKey);
            yield return null;
            CollectionAssert.AreEquivalent(new[] { h2, or }, workbench.Editor.Selection.Parts, "sanity: h2 and the OR selected");

            yield return RightClickAt(workbench, MiddleOf(workbench, or));
            UnityEngine.UIElements.Button make = ShellPanel.Find<UnityEngine.UIElements.Button>(CanvasMenu.ActionItem + CanvasMenuAction.MakeBlock);
            Assert.AreNotEqual(UnityEngine.UIElements.DisplayStyle.None, make.style.display.value, "MAKE BLOCK is offered");
            ShellPanel.Submit(make);
            yield return null;
            yield return null;
            WaveRenameField name = workbench.BlockNameField;
            Assert.IsTrue(name.IsOpen, "it asks for a name first");
            Assert.AreEqual("block1", name.Field.value, "offering a free one");

            name.Field.value = "tail";
            Press(_keyboard.enterKey);
            ShellPanel.SendKey(name.Field, KeyCode.Return);
            yield return null;
            Release(_keyboard.enterKey);
            yield return null;
            yield return null;

            Assert.IsFalse(name.IsOpen);
            Assert.IsNotNull(workbench.Session.Project.Find("tail"), "Enter made the block");
            int block = Top(workbench).Parts.Single(p => p is BlockPart b && b.Module == "tail").Id;
            CollectionAssert.AreEquivalent(new[] { block }, workbench.Editor.Selection.Parts, "and selected it");
            Assert.IsNull(Top(workbench).FindPart(h2), "h2 has gone into it");
            Assert.AreEqual(rows, workbench.Model.RowCount, "the waves' rows as they were");
            Assert.IsNotNull(ShellPanel.Find<UnityEngine.UIElements.Button>(WorkbenchPanel.PartButtonName("block:tail")), "PARTS offers it");

            // Asked again, and Escape: nothing made.
            int modules = workbench.Session.Project.Modules.Count;
            yield return RightClickAt(workbench, MiddleOf(workbench, block));
            ShellPanel.Submit(ShellPanel.Find<UnityEngine.UIElements.Button>(CanvasMenu.ActionItem + CanvasMenuAction.MakeBlock));
            yield return null;
            Assert.IsTrue(name.IsOpen, "sanity: asked again");

            // Escape as the browser hands it over (the 11d smoke test's logs): the game's half and UI Toolkit's in one frame.
            Press(_keyboard.escapeKey);
            yield return null;
            ShellPanel.SendKey(name.Field, KeyCode.Escape);
            Release(_keyboard.escapeKey);
            yield return null;
            yield return null;
            Assert.IsFalse(name.IsOpen, "Escape put the question away");
            Assert.AreEqual(modules, workbench.Session.Project.Modules.Count, "and made nothing");
            Assert.IsTrue(workbench.IsOpen, "and was the field's alone");
        }

        /// <summary>
        /// Part 12c: the four half adders' block shows COPIES 4 in its inspector, and a double-click steps inside its first
        /// copy; a wired array refuses another count in the document's words, and a block not yet wired takes one, its pins
        /// widening with it.
        /// </summary>
        [UnityTest]
        public IEnumerator AnArraysCopies_AreInItsInspector_AndItIsSteppedIntoAtItsFirstCopy()
        {
            yield return TestScene.Load();
            yield return Click(MainMenu.WorkbenchButton);
            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            int four = -1;
            for (int i = 0; i < WorkbenchSamples.Chip.Count; i++)
                if (WorkbenchSamples.Chip[i].Name == "Four half adders")
                    four = i;
            workbench.ShowSample(four);
            yield return null;
            workbench.Pause();
            workbench.Session.Finish();
            yield return null;
            int ha = Top(workbench).Parts.OfType<BlockPart>().Single().Id;

            yield return ClickAt(workbench, MiddleOf(workbench, ha));
            StringAssert.StartsWith("BLOCK ha", workbench.InspectorTitle);
            Assert.AreEqual("4", workbench.InspectorTyped()["copies"], "its copies in the bar");

            // Wired to four-bit buses, two copies would leave its pins narrower than its wires.
            Assert.IsTrue(workbench.InspectorType("copies", "2"));
            workbench.InspectorApply();
            yield return null;
            Assert.IsNotNull(workbench.InspectorRefusal, "refused in the document's words");
            Assert.AreEqual(4, ((BlockPart)Top(workbench).FindPart(ha)).Copies, "and still four");
            yield return Click(WorkbenchPanel.InspectorRevertName);

            // A double-click steps inside the first copy.
            yield return DoubleClick(workbench.ScreenPointOf(MiddleOf(workbench, ha)));
            CollectionAssert.AreEqual(new[] { "ha[0]" }, workbench.SteppedPath, "the first copy");
            Assert.AreEqual("half_adder", workbench.ShownModule);
            StringAssert.Contains("all 4 of them", workbench.InspectorSays, "an edit inside changes every copy");
            Assert.IsTrue(workbench.StepOut());

            // A new block, not yet wired, takes a count, and its pins widen with it.
            var ids = new System.Collections.Generic.List<int>();
            yield return Place(workbench, "block:half_adder", new BoardPoint(6, -4), ids);
            Assert.IsTrue(workbench.InspectorType("copies", "3"));
            workbench.InspectorApply();
            yield return null;
            Assert.IsNull(workbench.InspectorRefusal);
            BlockPart three = (BlockPart)Top(workbench).FindPart(ids[0]);
            Assert.AreEqual(3, three.Copies);
            Assert.IsTrue(three.Pins(workbench.Session.Project).All(p => p.Width == 3), "every pin three bits");
        }

        /// <summary>The chip's index of the sample called <paramref name="name"/>.</summary>
        private static int SampleNamed(string name)
        {
            for (int i = 0; i < WorkbenchSamples.Chip.Count; i++)
                if (WorkbenchSamples.Chip[i].Name == name)
                    return i;
            Assert.Fail($"no sample called {name}");
            return -1;
        }

        private static string AdderText(WorkbenchPanel workbench) => workbench.Session.Project.Find("add4").Source;

        /// <summary>
        /// A written block (Part 12d): a double-click opens its code in the circuit's window; typing applies itself once it
        /// pauses; a fault is marked and listed, the document keeping the code that worked, and a click on it puts the caret
        /// at its line; Escape lets the code go and leaves the workbench up; Alt+Up steps back out to the drawing.
        /// </summary>
        [UnityTest]
        public IEnumerator AWrittenBlock_OpensItsCode_TypingApplies_AndAFaultIsMarkedAndListed()
        {
            yield return TestScene.Load();
            yield return Click(MainMenu.WorkbenchButton);
            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            workbench.ShowSample(SampleNamed("Written adder"));
            yield return null;
            workbench.Pause();
            workbench.Session.Finish();
            yield return null;
            int add = Top(workbench).Parts.OfType<BlockPart>().Single().Id;
            CodeEditorView code = workbench.CodeView;
            Assert.IsFalse(code.gameObject.activeInHierarchy, "no code at the top, which is drawn");

            yield return DoubleClick(workbench.ScreenPointOf(MiddleOf(workbench, add)));
            yield return null;
            CollectionAssert.AreEqual(new[] { "add" }, workbench.SteppedPath);
            Assert.IsTrue(code.gameObject.activeInHierarchy, "its code in the circuit's window");
            Assert.AreEqual(WorkbenchSamples.AdderSource, code.Field.text);
            StringAssert.StartsWith("APPLIED", code.Status);
            Assert.IsNotNull(ShellPanel.Find<UnityEngine.UIElements.Label>(WorkbenchPanel.CodeNoteName), "PARTS says a written block has no parts");
            Assert.IsNull(ShellPanel.Find<UnityEngine.UIElements.Button>(WorkbenchPanel.NewCodeBlockName), "nor offers a code block inside code");

            // Typed: applied once the typing pauses, and the run made again.
            string xor = WorkbenchSamples.AdderSource.Replace("= a + b;", "= {1'b0, a ^ b};");
            code.Field.text = xor;
            yield return null;
            Assert.AreEqual(WorkbenchSamples.AdderSource, AdderText(workbench), "not while the typing goes on");
            StringAssert.StartsWith("TYPING", code.Status);
            for (int i = 0; i < 240 && AdderText(workbench) != xor; i++)
                yield return null;
            Assert.AreEqual(xor, AdderText(workbench), "applied once the typing paused");
            yield return null;
            StringAssert.StartsWith("APPLIED", code.Status);
            Assert.IsTrue(workbench.Session.Replaying || workbench.Session.Version > 0, "and the run made again");

            // A fault: marked, listed, and kept from the document.
            string broken = xor.Replace("a ^ b", "a ^ ");
            int line = broken.Split('\n').ToList().FindIndex(l => l.Contains("assign")) + 1;
            code.Field.text = broken;
            for (int i = 0; i < 240 && !code.ListedText.Any(); i++)
                yield return null;
            StringAssert.StartsWith($"line {line}: ", code.ListedText.First(), "listed under the code, at its line");
            StringAssert.StartsWith("NOT APPLIED", code.Status);
            Assert.AreEqual(xor, AdderText(workbench), "the document keeps the code that worked");

            // A click on the fault: the keyboard in the code, its caret on that line.
            yield return Click(CodeEditorView.ProblemName + "0");
            Assert.IsTrue(code.Field.isFocused, "the click puts the keyboard in the code");
            Assert.AreEqual(line, broken.Substring(0, code.Field.stringPosition).Count(c => c == '\n') + 1, "at the fault's line");

            // Escape lets the code go, and holds the press: the workbench stays up.
            yield return Tap(_keyboard.escapeKey);
            yield return null;
            Assert.IsFalse(code.Field.isFocused, "Escape let the code go");
            Assert.IsTrue(workbench.IsShowing, "and did not close the workbench as well");
            Assert.AreEqual(broken, code.Field.text, "nor took back what was typed");

            // Out again: the drawing, and no code.
            Assert.IsTrue(workbench.StepOut());
            yield return null;
            yield return null;
            Assert.IsFalse(code.gameObject.activeInHierarchy, "the code goes with the step out");
        }

        /// <summary>
        /// The 14g report: deleting code1's block left code1 in CODE and PARTS, with nothing to take it out. Its last block
        /// deleted says the module stays and how it goes; over its row in PARTS the status line says the same; a right-click
        /// there asks first, CANCEL keeps it, and the answer takes it out of PARTS and the CODE tab -- which Ctrl+Z undoes.
        /// </summary>
        [UnityTest]
        public IEnumerator AModuleWhoseLastBlockWent_IsDeletedFromPARTS_AfterAQuestion_AndCtrlZBringsItBack()
        {
            yield return TestScene.Load();
            yield return OpenMyCircuit();
            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            Assert.IsTrue(workbench.Session.AddWrittenModule("code1").Done);
            CatalogEntry entry = PartsCatalog.For(workbench.Session.Project, workbench.Session.Module).Single(e => e.Module == "code1");
            int block = workbench.Session.Place(entry, new Placement(4, 0)).Id;
            yield return null;
            yield return null;
            string row = WorkbenchPanel.PartButtonName("block:code1");
            Assert.IsNotNull(ShellPanel.Find<UnityEngine.UIElements.Button>(row), "sanity: PARTS offers code1");

            // The last block of it deleted on the canvas: the module stays, and the bar's line says how it goes.
            yield return ClickAt(workbench, MiddleOf(workbench, block));
            CollectionAssert.AreEqual(new[] { block }, workbench.Editor.Selection.Parts.ToArray(), "sanity: the click selected the block");
            yield return Tap(_keyboard.deleteKey);
            Assert.IsEmpty(Top(workbench).Parts.OfType<BlockPart>(), "Delete took the block away");
            Assert.IsNotNull(workbench.Session.Project.Find("code1"), "and left its module, the player's code");
            StringAssert.Contains("right-click it in PARTS to delete it", workbench.ChipText);

            // Over its row, the status line names the gesture.
            Vector2 onRow = ShellPanel.ScreenPointOf(ShellPanel.Find<UnityEngine.UIElements.Button>(row));
            Move(_mouse.position, onRow);
            yield return null;
            yield return null;
            StringAssert.StartsWith("blocks:", workbench.StatusText);

            // A right-click asks first; CANCEL keeps it.
            Press(_mouse.rightButton);
            yield return null;
            Release(_mouse.rightButton);
            yield return null;
            Assert.IsTrue(workbench.Ask.IsOpen, "a right-click on the row asks");
            StringAssert.Contains("Delete the module code1? Its code goes with it", workbench.Ask.Question);
            yield return Click(AskMenu.NoItem);
            Assert.IsFalse(workbench.Ask.IsOpen);
            Assert.IsNotNull(workbench.Session.Project.Find("code1"), "CANCEL keeps it");

            // The answer takes it out of PARTS and the CODE tab.
            Move(_mouse.position, onRow);
            yield return null;
            Press(_mouse.rightButton);
            yield return null;
            Release(_mouse.rightButton);
            yield return null;
            yield return Click(AskMenu.YesItem);
            yield return null;
            Assert.IsNull(workbench.Session.Project.Find("code1"), "the answer deletes it");
            Assert.IsNull(ShellPanel.Find<UnityEngine.UIElements.Button>(row), "gone from PARTS");
            Assert.AreEqual(-1, workbench.Session.Listing.FileOf("code1"), "and from the CODE tab");
            StringAssert.Contains("Ctrl+Z brings it back", workbench.ChipText);

            Press(_keyboard.leftCtrlKey);
            yield return Tap(_keyboard.zKey);
            Release(_keyboard.leftCtrlKey);
            yield return null;
            yield return null;
            Assert.IsNotNull(workbench.Session.Project.Find("code1"), "Ctrl+Z brings it back");
            Assert.IsNotNull(ShellPanel.Find<UnityEngine.UIElements.Button>(row), "and PARTS offers it again");
        }

        /// <summary>A module in use is not deleted from PARTS: the right-click says where it is used instead of asking.</summary>
        [UnityTest]
        public IEnumerator AModuleInUse_RightClickedInPARTS_SaysWhereInsteadOfAsking()
        {
            yield return TestScene.Load();
            yield return OpenMyCircuit();
            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            Assert.IsTrue(workbench.Session.AddWrittenModule("code1").Done);
            CatalogEntry entry = PartsCatalog.For(workbench.Session.Project, workbench.Session.Module).Single(e => e.Module == "code1");
            Assert.IsTrue(workbench.Session.Place(entry, new Placement(4, 0)).Done);
            yield return null;
            yield return null;

            Move(_mouse.position, ShellPanel.ScreenPointOf(ShellPanel.Find<UnityEngine.UIElements.Button>(WorkbenchPanel.PartButtonName("block:code1"))));
            yield return null;
            Press(_mouse.rightButton);
            yield return null;
            Release(_mouse.rightButton);
            yield return null;
            Assert.IsFalse(workbench.Ask.IsOpen, "nothing to ask: it cannot go");
            StringAssert.Contains("my_circuit has a block of it", workbench.ChipText);
            Assert.IsNotNull(workbench.Session.Project.Find("code1"));
        }

        /// <summary>
        /// Every key of the workbench, each with its modifiers, pressed while a field has the keyboard: none of them does
        /// anything -- not to the document, the run, the view, the rows, or what is open -- since each is the field's.
        /// </summary>
        private IEnumerator EveryWorkbenchKeyDoesNothing(WorkbenchPanel workbench, string field)
        {
            Assert.IsTrue(UiText.Typing, $"sanity: {field} has the keyboard");
            Project before = workbench.Session.Project;
            int sample = workbench.Sample, rows = workbench.List.Lines.Count;
            double time = workbench.Time;
            float zoom = workbench.Zoom;
            string[] path = workbench.SteppedPath.ToArray();

            foreach (KeyBinding binding in WorkbenchKeys.All)
            {
                KeyControl key = _keyboard[(Key)System.Enum.Parse(typeof(Key), binding.Key)];
                if (binding.Ctrl)
                    Press(_keyboard.leftCtrlKey);
                if (binding.Shift)
                    Press(_keyboard.leftShiftKey);
                if (binding.Alt)
                    Press(_keyboard.leftAltKey);
                yield return null;   // the modifiers held first, as a hand holds them, so the key would act if it could
                yield return Tap(key);
                if (binding.Alt)
                    Release(_keyboard.leftAltKey);
                if (binding.Shift)
                    Release(_keyboard.leftShiftKey);
                if (binding.Ctrl)
                    Release(_keyboard.leftCtrlKey);
                yield return null;

                string said = $"{binding.Cap}, pressed while {field} had the keyboard, ";
                Assert.AreSame(before, workbench.Session.Project, said + "changed the document");
                Assert.IsTrue(workbench.IsOpen, said + "closed the workbench");
                Assert.AreEqual(sample, workbench.Sample, said + "changed the sample");
                CollectionAssert.AreEqual(path, workbench.SteppedPath, said + "stepped in or out");
                Assert.IsFalse(workbench.Playing, said + "played");
                Assert.AreEqual(time, workbench.Time, 1e-9, said + "moved the time");
                Assert.AreEqual(zoom, workbench.Zoom, said + "zoomed");
                Assert.AreEqual(rows, workbench.List.Lines.Count, said + "changed the rows");
                Assert.IsFalse(workbench.KeyMapPopup.IsOpen, said + "opened the keyboard map");
                Assert.IsFalse(workbench.Picker.IsOpen, said + "opened the add list");
                Assert.IsTrue(UiText.Typing, said + "took the keyboard from it");
            }
        }

        /// <summary>
        /// Ahmad's question (2026-10-27): is Ctrl+V the workbench's while code is typed? No -- while the code, or any field,
        /// has the keyboard, no key of the workbench does anything, Ctrl+V, C, X, A, Z and Y, Delete, Space and the arrows
        /// included; and the same Ctrl+V, with the code let go, pastes the parts copied, on the circuit.
        /// </summary>
        [UnityTest]
        public IEnumerator WhileAFieldHasTheKeyboard_NoWorkbenchKeyActs_AndCtrlVPastesPartsOnlyOnTheCircuit()
        {
            yield return TestScene.Load();
            yield return Click(MainMenu.WorkbenchButton);
            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            workbench.ShowSample(SampleNamed("Written adder"));
            yield return null;
            workbench.Pause();
            workbench.Session.Finish();
            yield return null;
            int add = Top(workbench).Parts.OfType<BlockPart>().Single().Id;

            // Something to paste: the block, copied on the circuit.
            yield return ClickAt(workbench, MiddleOf(workbench, add));
            Press(_keyboard.leftCtrlKey);
            yield return Tap(_keyboard.cKey);
            Release(_keyboard.leftCtrlKey);
            yield return null;

            // Inside it, the keyboard in its code: every key is the code's.
            yield return DoubleClick(workbench.ScreenPointOf(MiddleOf(workbench, add)));
            yield return null;
            CodeEditorView code = workbench.CodeView;
            code.GoTo(1, 1);
            yield return null;
            Assert.IsTrue(code.Field.isFocused, "sanity: the code has the keyboard");
            yield return EveryWorkbenchKeyDoesNothing(workbench, "the code");

            // And in a field of the shell's, RUN TO.
            yield return ClickAtScreen(ShellPanel.ScreenPointOf(ShellPanel.Find<UnityEngine.UIElements.TextField>(WorkbenchPanel.RunToField)));
            yield return EveryWorkbenchKeyDoesNothing(workbench, "RUN TO");
            Press(_keyboard.escapeKey);
            ShellPanel.SendKey(ShellPanel.Find<UnityEngine.UIElements.TextField>(WorkbenchPanel.RunToField), KeyCode.Escape);
            yield return null;
            Release(_keyboard.escapeKey);
            // The field lets go a frame or two later (ShellTheme.Field); waited for, not counted, with a cap that fails loudly.
            for (int i = 0; i < 30 && UiText.Typing; i++)
                yield return null;
            Assert.IsFalse(UiText.Typing, "sanity: Escape let RUN TO go");

            // The same Ctrl+V on the circuit, nothing typed: the block copied is pasted.
            Press(_keyboard.leftAltKey);
            yield return null;
            yield return Tap(_keyboard.upArrowKey);
            Release(_keyboard.leftAltKey);
            yield return null;
            Assert.IsEmpty(workbench.SteppedPath, "sanity: stepped back out to the circuit");
            Press(_keyboard.leftCtrlKey);
            yield return null;
            yield return Tap(_keyboard.vKey);
            Release(_keyboard.leftCtrlKey);
            yield return null;
            Assert.AreEqual(2, Top(workbench).Parts.OfType<BlockPart>().Count(), "Ctrl+V on the circuit pastes the block copied");
        }

        /// <summary>
        /// The code editor answers the keys a writer reaches for, as the browser's does (Part 14g): Ctrl+Z through the typing,
        /// Tab indenting the line by four spaces rather than typing a tab, Ctrl+/ commenting it, and Ctrl+S applying at once.
        /// TextMesh Pro reads the platform's keys, which a test's keyboard never reaches, so the Tab is handed to the field as
        /// it hands one over, with the key held on the test's keyboard in the same frame.
        /// </summary>
        [UnityTest]
        public IEnumerator TheCodeEditor_AnswersUndoTabCommentAndSave()
        {
            yield return TestScene.Load();
            yield return Click(MainMenu.WorkbenchButton);
            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            workbench.ShowSample(SampleNamed("Written adder"));
            yield return null;
            workbench.Pause();
            workbench.Session.Finish();
            yield return null;
            yield return DoubleClick(workbench.ScreenPointOf(MiddleOf(workbench, Top(workbench).Parts.OfType<BlockPart>().Single().Id)));
            yield return null;
            CodeEditorView code = workbench.CodeView;
            string source = WorkbenchSamples.AdderSource;
            int line = source.Substring(0, source.IndexOf("assign")).Count(c => c == '\n') + 1;
            code.GoTo(line, 1);
            yield return null;
            Assert.IsTrue(code.Field.isFocused, "sanity: the code has the keyboard");

            // Typed, then Ctrl+Z: the typing goes back.
            code.Field.text = source + "// typed";
            yield return null;
            Press(_keyboard.leftCtrlKey);
            yield return Tap(_keyboard.zKey);
            Release(_keyboard.leftCtrlKey);
            yield return null;
            Assert.AreEqual(source, code.Field.text, "Ctrl+Z took the typing back");
            Assert.AreEqual(source, code.Code.Text);

            // Tab, as the field hands it over: the line indented by four spaces, no tab typed.
            code.GoTo(line, 1);
            yield return null;
            Press(_keyboard.tabKey);
            yield return null;
            char typed = code.Field.onValidateInput(code.Field.text, code.Field.stringPosition, '\t');
            yield return null;
            Release(_keyboard.tabKey);
            yield return null;
            Assert.AreEqual('\0', typed, "no tab character typed");
            StringAssert.Contains("\n        assign #3", code.Field.text, "the line indented four spaces more");
            Assert.IsFalse(code.Field.text.Contains("\t"));

            // Ctrl+/: commented out, where the line's text starts.
            Press(_keyboard.leftCtrlKey);
            yield return Tap(_keyboard.slashKey);
            Release(_keyboard.leftCtrlKey);
            yield return null;
            StringAssert.Contains("\n        // assign #3", code.Field.text, "Ctrl+/ commented the line");

            // Ctrl+S: applied at once, without waiting for the typing to pause.
            Press(_keyboard.leftCtrlKey);
            yield return Tap(_keyboard.sKey);
            Release(_keyboard.leftCtrlKey);
            yield return null;
            Assert.AreEqual(code.Field.text, AdderText(workbench), "Ctrl+S applied the code");
        }

        /// <summary>+ CODE (Part 12d) asks for a name, makes the module from its template, and puts a block of it in hand to place.</summary>
        [UnityTest]
        public IEnumerator PlusCode_AsksForAName_AndPutsABlockOfItInHand()
        {
            yield return TestScene.Load();
            yield return OpenMyCircuit();
            WorkbenchPanel workbench = Find<WorkbenchPanel>();

            yield return Click(WorkbenchPanel.NewCodeBlockName);
            yield return null;
            WaveRenameField name = workbench.CodeNameField;
            Assert.IsTrue(name.IsOpen, "it asks for a name first");
            Assert.AreEqual("code1", name.Field.value, "offering a free one");

            name.Field.value = "adder";
            Press(_keyboard.enterKey);
            ShellPanel.SendKey(name.Field, KeyCode.Return);
            yield return null;
            Release(_keyboard.enterKey);
            yield return null;
            yield return null;

            Assert.IsFalse(name.IsOpen);
            Module adder = workbench.Session.Project.Find("adder");
            Assert.IsNotNull(adder, "Enter made the module");
            Assert.AreEqual(WrittenModules.Template("adder"), adder.Source, "written, from the template");
            Assert.AreEqual("adder", workbench.Editor.InHand?.Module, "and a block of it is in hand");
            Assert.IsNotNull(ShellPanel.Find<UnityEngine.UIElements.Button>(WorkbenchPanel.PartButtonName("block:adder")), "PARTS offers it");

            yield return ClickAt(workbench, new BoardPoint(4, 0));
            BlockPart placed = Top(workbench).Parts.OfType<BlockPart>().SingleOrDefault();
            Assert.IsNotNull(placed, "a click places it");
            CollectionAssert.AreEqual(new[] { "a", "y" }, placed.Pins(workbench.Session.Project).Select(p => p.Name), "its pins its header's");
        }

        /// <summary>
        /// The dock's CODE tab (Part 12e): the module shown as it prints, a chip a file; a gate placed is in it at once; a chip
        /// shows another file; stepping inside a block, the code follows the module shown; WAVES puts it away.
        /// </summary>
        [UnityTest]
        public IEnumerator TheCodeTab_ShowsTheModuleAsItPrints_AndFollowsTheDrawing()
        {
            yield return TestScene.Load();
            yield return Click(MainMenu.WorkbenchButton);
            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            workbench.ShowSample(SampleNamed("Full adder"));
            yield return null;
            workbench.Pause();
            workbench.Session.Finish();
            yield return null;

            yield return Click(WorkbenchPanel.CodeTabName);
            Assert.IsTrue(workbench.CodeShown);
            Assert.IsTrue(workbench.ListingView.gameObject.activeInHierarchy, "the code over the dock");
            Assert.AreEqual("full_adder", workbench.ListingCaption, "the module the circuit's window shows");
            StringAssert.Contains("module full_adder", workbench.ListingView.Text);
            Assert.AreEqual(4, workbench.ListingChips, "the cells, the half adder, the full adder, the testbench");

            var ids = new System.Collections.Generic.List<int>();
            yield return Place(workbench, "and", new BoardPoint(6, -4), ids);
            yield return null;
            StringAssert.Contains(Top(workbench).FindPart(ids[0]).Name, workbench.ListingView.Text, "a gate placed is in the code at once");

            yield return Click(WorkbenchPanel.CodeFileChip("TESTBENCH"));
            Assert.AreEqual("TESTBENCH", workbench.ListingCaption);
            StringAssert.Contains("initial", workbench.ListingView.Text, "the testbench made from the stimulus");

            // An edit draws the stage again, and its replay taking over draws it again: the file chosen stays.
            yield return Place(workbench, "or", new BoardPoint(10, -4), ids);
            workbench.Session.Finish();
            yield return null;
            yield return null;
            Assert.AreEqual("TESTBENCH", workbench.ListingCaption, "an edit and its replay keep the file chosen");

            Assert.IsTrue(workbench.StepInto(Top(workbench).FindPart("h1").Id));
            yield return null;
            Assert.AreEqual("half_adder", workbench.ListingCaption, "stepping inside, the code follows the module shown");
            Assert.IsTrue(workbench.StepOut());

            yield return Click(WorkbenchPanel.WavesTabName);
            Assert.IsFalse(workbench.CodeShown);
            Assert.IsFalse(workbench.ListingView.gameObject.activeInHierarchy, "WAVES puts the code away");
        }

        /// <summary>
        /// Over its library's gate budget, the TRANSCRIPT says so as a warning (Part 14g, Ahmad's choice), in the TIMING tab's
        /// own count; a click on the warning opens the TIMING tab, where the gates are counted by kind.
        /// </summary>
        [UnityTest]
        public IEnumerator OverTheGateBudget_TheTranscriptWarns_AndAClickOpensTiming()
        {
            yield return TestScene.Load();
            yield return Click(MainMenu.WorkbenchButton);
            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            workbench.ShowSample(SampleNamed("Full adder"));
            yield return null;
            workbench.Pause();
            workbench.Session.Finish();
            Assert.IsTrue(workbench.Session.SetLibrary(GateLibrary.CourseCells(1) with { GateBudget = 4 }).Done);
            yield return null;
            yield return null;

            workbench.ShowTranscript();
            yield return null;
            string over = workbench.Session.OverBudget;
            Assert.IsNotNull(over, "sanity: the full adder's five gates are over a budget of four");
            int line = -1;
            for (int i = 0; i < workbench.Transcript.Lines.Count; i++)
                if (workbench.Transcript.Lines[i].Text == over)
                    line = i;
            Assert.GreaterOrEqual(line, 0, "the TRANSCRIPT lists the warning");
            Assert.AreEqual(TranscriptSeverity.Warning, workbench.Transcript.Lines[line].Severity);

            workbench.ActOnTranscript(line);
            yield return null;
            Assert.IsTrue(workbench.TimingShown, "a click on it opens the TIMING tab");
        }

        /// <summary>
        /// The dock's TIMING tab (Part 14b): the full adder's critical path picked and lit from the start -- a to cout through
        /// both blocks and the OR -- another path picked lighting its own, the light following the drawing inside a block, and
        /// another tab putting it out. Then the counter: its minimum period, 3, moves to 5 with its register's clock to Q of 2,
        /// typed in the register's quick field.
        /// </summary>
        [UnityTest]
        public IEnumerator TheTimingTab_LightsThePathPicked_AndFollowsARegistersClockToQ()
        {
            yield return TestScene.Load();
            yield return Click(MainMenu.WorkbenchButton);
            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            workbench.ShowSample(SampleNamed("Full adder"));
            yield return null;
            workbench.Pause();
            workbench.Session.Finish();
            yield return null;
            Assert.AreEqual(0, workbench.Overlay.PathParts.Count, "sanity: nothing lit before the tab");

            yield return Click(WorkbenchPanel.TimingTabName);
            Assert.IsTrue(workbench.TimingShown);
            TimingView timing = workbench.Timing;
            Assert.AreEqual(timing.Critical, workbench.TimingPicked, "the critical path, picked to begin with");
            StringAssert.StartsWith("a → cout", timing.Lines[timing.Critical].Text);
            Assert.AreEqual(3, workbench.Overlay.PathParts.Count, "h1, h2 and the OR lit");
            Assert.AreEqual(3, workbench.Overlay.PathPorts.Count, "and a, b and cout: a to cout and b to cout are one line, both lit");
            Assert.IsNotNull(ShellPanel.Find<UnityEngine.UIElements.VisualElement>(WorkbenchPanel.TimingRowName + "0"), "its lines on screen");

            int toS = timing.Lines.ToList().FindIndex(l => l.CanPick && l.Text.StartsWith("a → s"));
            Assert.Greater(toS, 0, "sanity: a path to s is listed");
            workbench.PickTiming(toS);
            yield return null;
            yield return null;
            Assert.AreEqual(2, workbench.Overlay.PathParts.Count, "a to s is the two blocks, through both XORs");

            Assert.IsTrue(workbench.StepInto(Top(workbench).FindPart("h1").Id));
            yield return null;
            yield return null;
            Assert.AreEqual(1, workbench.Overlay.PathParts.Count, "inside h1, the XOR it passes through");
            Assert.IsTrue(workbench.StepOut());
            yield return null;

            yield return Click(WorkbenchPanel.WavesTabName);
            yield return null;
            Assert.AreEqual(0, workbench.Overlay.PathParts.Count, "another tab puts the light out");
            Assert.IsNull(workbench.Editor.Path);

            workbench.ShowSample(SampleNamed("Counter"));
            yield return null;
            workbench.Pause();
            workbench.Session.Finish();
            yield return null;
            yield return Click(WorkbenchPanel.TimingTabName);
            string unit = workbench.Session.Project.Settings?.TimeUnit ?? BitSorter.SystemVerilog.TimeUnits.Default;
            Assert.AreEqual("3 " + unit, workbench.Timing.Lines.Single(l => l.Label == "MINIMUM PERIOD").Numbers);

            RegisterPart register = Top(workbench).Parts.OfType<RegisterPart>().Single();
            yield return DoubleClick(workbench.ScreenPointOf(MiddleOf(workbench, register.Id)));
            Assert.IsNull(workbench.ApplyQuick("tcq", "2"));
            yield return UntilReplayed(workbench);
            yield return null;
            Assert.AreEqual(2, ((RegisterPart)Top(workbench).FindPart(register.Id)).Tcq);
            Assert.AreEqual("5 " + unit, workbench.Timing.Lines.Single(l => l.Label == "MINIMUM PERIOD").Numbers, "clock to Q 2 + logic 3");
        }

        /// <summary>
        /// The measured worst and STEP TEST (Part 14c): the TIMING tab measures the full adder while it is up, a slice a frame,
        /// and lays the slowest seen beside its critical path -- 4, as computed; STEP TEST makes the stimulus a step test, one
        /// undo step.
        /// </summary>
        [UnityTest]
        public IEnumerator TheTimingTab_MeasuresTheWorst_AndSTEPTESTMakesTheStimulus()
        {
            yield return TestScene.Load();
            yield return Click(MainMenu.WorkbenchButton);
            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            workbench.ShowSample(SampleNamed("Full adder"));
            yield return null;
            workbench.Pause();
            workbench.Session.Finish();
            yield return null;

            yield return Click(WorkbenchPanel.TimingTabName);
            Assert.IsNotNull(workbench.Measuring, "a full adder is measured");
            for (int frame = 0; frame < 600 && !workbench.Measuring.Done; frame++)
                yield return null;
            Assert.IsTrue(workbench.Measuring.Done, "measured within the cap");
            yield return null;

            string unit = workbench.Session.Project.Settings?.TimeUnit ?? BitSorter.SystemVerilog.TimeUnits.Default;
            Assert.AreEqual("4 " + unit, workbench.Timing.Lines.Single(l => l.Label == "MEASURED WORST").Numbers);
            StringAssert.StartsWith("Measured", workbench.TimingSays);

            StepMeasure measured = workbench.Measuring;
            yield return Click(WorkbenchPanel.StepTestName);
            Assert.AreEqual(42, workbench.Session.Project.Stimulus.End, "each input up and down, 6 to settle");
            yield return UntilReplayed(workbench);
            Assert.AreSame(measured, workbench.Measuring, "STEP TEST changes no gate: what was measured stands");
            Assert.AreEqual("4 " + unit, workbench.Timing.Lines.Single(l => l.Label == "MEASURED WORST").Numbers, "and is still shown");
            workbench.UndoEdit();
            yield return null;
            Assert.AreEqual(WorkbenchSamples.FullAdderEnd, workbench.Session.Project.Stimulus.End, "and one undo puts the stimulus back");
        }

        /// <summary>
        /// Gate-library mode (Part 14d): COURSE CELLS from the LIBRARY menu holds the full adder to the course's cells --
        /// PARTS offers no MUX nor BUFIF1, a gate placed is its cell, and taking out XOR, which the design is built of, is
        /// refused with the rules' words on the chip, the library as it was.
        /// </summary>
        [UnityTest]
        public IEnumerator TheLibrary_HoldsTheDesignToItsCells()
        {
            yield return TestScene.Load();
            yield return Click(MainMenu.WorkbenchButton);
            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            workbench.ShowSample(SampleNamed("Full adder"));
            yield return null;
            workbench.Pause();
            workbench.Session.Finish();
            yield return null;
            Assert.IsNotNull(ShellPanel.Find<UnityEngine.UIElements.Button>(WorkbenchPanel.PartButtonName("mux")), "sanity: a MUX in PARTS before");

            yield return Click(WorkbenchPanel.TimingTabName);
            yield return Click(WorkbenchPanel.LibraryButtonName);
            Assert.IsTrue(workbench.LibraryMenu.IsOpen);
            yield return Click(LibraryMenu.CourseItem);
            yield return null;
            Assert.AreEqual(GateLibrary.CourseCellsName, workbench.Session.Project.Settings.Library.Name);
            Assert.IsNull(ShellPanel.Find<UnityEngine.UIElements.Button>(WorkbenchPanel.PartButtonName("mux")), "no MUX in PARTS: no cell");
            Assert.IsNull(ShellPanel.Find<UnityEngine.UIElements.Button>(WorkbenchPanel.PartButtonName("bufif1")), "nor a tri-state");
            Assert.IsNotNull(ShellPanel.Find<UnityEngine.UIElements.Button>(WorkbenchPanel.PartButtonName("nand")), "a NAND is");

            var ids = new System.Collections.Generic.List<int>();
            yield return Place(workbench, "nand", new BoardPoint(10, -4), ids);
            GatePart nand = (GatePart)Top(workbench).FindPart(ids[0]);
            long delay = workbench.Session.Project.Settings.DefaultGateDelay;
            Assert.AreEqual((2, delay, delay), (nand.Inputs, nand.Rise, nand.Fall), "the library's NAND2");

            yield return Click(WorkbenchPanel.LibraryButtonName);
            yield return Click(LibraryMenu.KindItem + BitSorter.Sim.GateKind.Xor);
            StringAssert.Contains("which the library has no cell for", workbench.ChipText, "refused, in the rules' words");
            Assert.IsTrue(workbench.Session.Project.Settings.Library.Allows(BitSorter.Sim.GateKind.Xor), "and the library as it was");
        }

        /// <summary>
        /// A notice too long for the chip's one line is shown whole, on the lines it needs, over the timeline's row. COURSE
        /// CELLS refused on the reference, which has a BUFIF1, names every cell the library has, and the chip cut it off
        /// after NAND2 (the Part 14g browser smoke test). A line that fits keeps the chip as it was.
        /// </summary>
        [UnityTest]
        public IEnumerator ANoticeTooLongForOneLine_IsShownWhole_OnTheLinesItNeeds()
        {
            const float OneLine = 26f;
            yield return TestScene.Load();
            yield return Click(MainMenu.WorkbenchButton);
            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            workbench.ShowSample(SampleNamed("Reference"));
            yield return null;
            workbench.Pause();
            workbench.Session.Finish();
            yield return null;
            UnityEngine.UIElements.VisualElement chip = workbench.Chip;
            Assert.AreEqual(OneLine, chip.layout.height, 0.5f, "sanity: one line, as it was");

            yield return Click(WorkbenchPanel.TimingTabName);
            yield return Click(WorkbenchPanel.LibraryButtonName);
            yield return Click(LibraryMenu.CourseItem);
            yield return null;
            yield return null;
            StringAssert.Contains("which the library has no cell for: it has INV, BUF", workbench.ChipText, "sanity: COURSE CELLS refused, the cells named");

            Assert.Greater(chip.layout.height, OneLine + 10f, "the whole refusal, on the lines it needs, where it was cut off");
            var words = (UnityEngine.UIElements.TextElement)chip[0];
            Vector2 needs = words.MeasureTextSize(words.text, words.contentRect.width, UnityEngine.UIElements.VisualElement.MeasureMode.Exactly,
                0f, UnityEngine.UIElements.VisualElement.MeasureMode.Undefined);
            Assert.LessOrEqual(needs.y, words.contentRect.height + 0.5f, "every line of it inside the chip");
            UnityEngine.UIElements.VisualElement root = chip.parent.parent;
            Assert.Greater(root.IndexOf(chip.parent), root.IndexOf(UnityEngine.UIElements.UQueryExtensions.Q(root, "timeline")), "drawn over the timeline's row");
            Assert.AreEqual(UnityEngine.UIElements.PickingMode.Ignore, chip.pickingMode, "and taking no clicks, so the slider under it still does");
        }

        /// <summary>
        /// Two code fields at once (Part 12e): a plain click in the read-only listing leaves the workbench its keys, since it
        /// draws no caret to say it has them; a drag selects and keeps them for Ctrl+C; and a click from there into a written
        /// block's code leaves the keyboard with the code -- the listing lets go of itself alone.
        /// </summary>
        [UnityTest]
        public IEnumerator TheListingKeepsTheKeysOnlyWhileTextIsSelected_AndGivesThemToTheCodeClicked()
        {
            yield return TestScene.Load();
            yield return Click(MainMenu.WorkbenchButton);
            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            workbench.ShowSample(SampleNamed("Written adder"));
            yield return null;
            workbench.Pause();
            workbench.Session.Finish();
            yield return null;
            Assert.IsTrue(workbench.StepInto(Top(workbench).Parts.OfType<BlockPart>().Single().Id));
            workbench.ShowCode();
            yield return null;
            yield return null;
            CodeListingView listing = workbench.ListingView;
            CodeEditorView code = workbench.CodeView;
            Assert.AreEqual("add4", workbench.ListingCaption, "sanity: the written block's own file");
            Rect text = WorldRect(listing.Field.textViewport);

            yield return ClickAtScreen(new Vector2(text.xMin + 40f, text.yMax - 12f));
            yield return null;
            Assert.IsFalse(UiText.Typing, "a plain click in the read-only listing leaves the workbench its keys");

            Move(_mouse.position, new Vector2(text.xMin + 10f, text.yMax - 12f));
            yield return null;
            Press(_mouse.leftButton);
            yield return null;
            Move(_mouse.position, new Vector2(text.xMin + 300f, text.yMax - 70f));
            yield return null;
            yield return null;
            Release(_mouse.leftButton);
            yield return null;
            yield return null;
            Assert.AreNotEqual(listing.Field.selectionStringAnchorPosition, listing.Field.selectionStringFocusPosition, "a drag selected some of the file");
            Assert.IsTrue(UiText.Typing, "and keeps the keys while it is selected, for Ctrl+C");

            yield return ClickAtScreen(WorldRect((RectTransform)code.Field.transform).center);
            yield return null;
            yield return null;
            Assert.IsTrue(code.Field.isFocused, "the code clicked has the keyboard");
            Assert.IsTrue(UiText.Typing, "and the workbench's keys stay off while it does");
        }

        /// <summary>
        /// The code is set in IBM Plex Mono, which ships with the game beside Plex Sans (Part 12f), rather than in a face the
        /// computer may not have; and a shell popup open is known, for the browser's code editor to hide under.
        /// </summary>
        [UnityTest]
        public IEnumerator TheCodesFace_ShipsWithTheGame_AndAPopupOpenIsKnown()
        {
            yield return TestScene.Load();
            yield return OpenTheReference();
            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            Assert.IsNotNull(ShellFonts.Mono, "the scene holds IBM Plex Mono, so every build ships it");
            Assert.AreEqual("IBMPlexMono-Regular", CodeFont.Asset.sourceFontFile.name, "and the code is set in it");

            Assert.IsFalse(ShellPopup.AnyOpen, "sanity: no popup up");
            yield return RightClickAt(workbench, MiddleOf(workbench, Top(workbench).Parts.First(p => p is GatePart).Id));
            Assert.IsTrue(ShellPopup.AnyOpen, "the canvas's menu is a popup open");
            yield return Tap(_keyboard.escapeKey);
            yield return null;
            Assert.IsFalse(ShellPopup.AnyOpen, "and closed, none is");
        }

        /// <summary>
        /// FILE (Part 13a): its menu's EXPORT PROJECT saves the document as a zip of every file it prints as and a run.do, in
        /// the Exports folder -- redirected while a test runs, as every export is.
        /// </summary>
        [UnityTest]
        public IEnumerator FileExportProject_SavesAZipOfTheFilesAndARunScript()
        {
            yield return TestScene.Load();
            yield return Click(MainMenu.WorkbenchButton);
            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            workbench.ShowSample(SampleNamed("Full adder"));
            yield return null;
            workbench.Pause();
            workbench.Session.Finish();
            yield return null;

            yield return Click(WorkbenchPanel.FileButton);
            Assert.IsTrue(workbench.FileMenu.IsOpen, "FILE opens its menu");
            yield return Click(FileMenu.ActionItem + FileAction.ExportProject);
            Assert.IsFalse(workbench.FileMenu.IsOpen, "and a choice closes it");

            string path = System.IO.Path.Combine(FileSaver.Folder, WaveExport.FileName("Full adder", "zip"));
            Assert.IsTrue(System.IO.File.Exists(path), path);
            byte[] zip = System.IO.File.ReadAllBytes(path);
            Assert.AreEqual(new byte[] { 0x50, 0x4B, 0x03, 0x04 }, zip.Take(4).ToArray(), "a zip");
            string bytes = System.Text.Encoding.UTF8.GetString(zip);
            StringAssert.Contains("full_adder/" + ProjectExport.RunScript, bytes, "with the script, stored as it reads");
            StringAssert.Contains("vsim -voptargs=+acc work.full_adder_tb", bytes);
            StringAssert.Contains("module full_adder", bytes);
        }

        /// <summary>
        /// EXPORT PROJECT hands over the code on screen (Part 13a). Code typed into a written block and not yet applied is
        /// applied first, as the editor would after its pause, and code that cannot apply is refused with what is wrong with
        /// it -- never exported as the last text that worked, which is older than what the student is looking at.
        /// </summary>
        [UnityTest]
        public IEnumerator FileExportProject_ExportsTheCodeOnScreen_AndRefusesCodeThatDoesNotApply()
        {
            yield return TestScene.Load();
            yield return Click(MainMenu.WorkbenchButton);
            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            workbench.ShowSample(SampleNamed("Written adder"));
            yield return null;
            workbench.Pause();
            workbench.Session.Finish();
            yield return null;
            Assert.IsTrue(workbench.StepInto(Top(workbench).Parts.OfType<BlockPart>().Single().Id));
            yield return null;
            CodeDocument code = workbench.Session.Code;
            Assert.IsNotNull(code, "sanity: the written block's code is on screen");
            Assert.IsFalse(code.Pending, "sanity: and is the document's");
            string path = System.IO.Path.Combine(FileSaver.Folder, WaveExport.FileName("Written adder", "zip"));
            Assert.IsNotNull(FileSaver.Redirected, "sanity: the Exports folder is the test's scratch one, never the player's");
            if (System.IO.File.Exists(path))
                System.IO.File.Delete(path);   // an earlier run's, in the scratch folder

            // The editor's clock is the game's in milliseconds. Typed a minute ahead of it, the text is "just typed" however
            // many frames the menu takes, so the pause never runs out and applies it before the export looks.
            double justTyped = Time.timeAsDouble * 1000.0 + 60000.0;
            string typed = WorkbenchSamples.AdderSource.Replace("= a + b;", "= {1'b0, a ^ b};");
            string broken = typed.Replace("a ^ b", "a ^ ");

            // Typed with a fault, and not applied: refused, and nothing saved.
            code.Type(broken, justTyped);
            Assert.IsTrue(code.Pending, "sanity: on screen, not the document's");
            yield return Click(WorkbenchPanel.FileButton);
            yield return Click(FileMenu.ActionItem + FileAction.ExportProject);
            yield return null;
            Assert.IsFalse(System.IO.File.Exists(path), "code on screen that does not apply is not exported as the last that did");
            StringAssert.Contains("does not apply", workbench.ChipText, "and the chip says so");
            Assert.AreEqual(WorkbenchSamples.AdderSource, AdderText(workbench), "the document keeps the last code that worked");

            // Typed and not applied yet: applied, then exported.
            code.Type(typed, justTyped);
            Assert.AreEqual(WorkbenchSamples.AdderSource, AdderText(workbench), "sanity: not applied yet");
            yield return Click(WorkbenchPanel.FileButton);
            yield return Click(FileMenu.ActionItem + FileAction.ExportProject);

            Assert.IsTrue(System.IO.File.Exists(path), path);
            string bytes = System.Text.Encoding.UTF8.GetString(System.IO.File.ReadAllBytes(path));
            StringAssert.Contains("assign #3 {cout, s} = {1'b0, a ^ b};", bytes, "the code on screen is in the file");
            StringAssert.DoesNotContain("= a + b;", bytes, "not the last code that applied before it");
            Assert.AreEqual(typed, AdderText(workbench), "applied on the way out");
            Assert.IsFalse(workbench.Session.Code.Pending);
        }

        private const string ImportedMux =
            "module mux2 (input logic a, input logic b, input logic s, output logic y);\n" +
            "    logic s_n, n0, n1;\n" +
            "    INV   #(.Tpdlh(2), .Tpdhl(1)) g0 (.A(s), .Z(s_n));\n" +
            "    NAND2 #(.Tpdlh(3), .Tpdhl(2)) g1 (.A(a), .B(s_n), .Z(n0));\n" +
            "    NAND2 #(.Tpdlh(3), .Tpdhl(2)) g2 (.A(b), .B(s), .Z(n1));\n" +
            "    NAND2 #(.Tpdlh(3), .Tpdhl(2)) g3 (.A(n0), .B(n1), .Z(y));\n" +
            "endmodule\n";

        private const string ImportedMuxBench =
            "module mux2_tb;\n" +
            "    logic a, b, s, y;\n" +
            "    mux2 dut (.a(a), .b(b), .s(s), .y(y));\n" +
            "    initial begin\n" +
            "        a = 0; b = 1; s = 0;\n" +
            "        #10 s = 1;\n" +
            "        #10 $display(\"y is %b\", y);\n" +
            "        $stop;\n" +
            "    end\n" +
            "endmodule\n";

        /// <summary>The files of a project put in the Import folder -- redirected while a test runs -- which is emptied first.</summary>
        private static void PutInTheImportFolder(params (string Name, string Text)[] files)
        {
            Assert.IsNotNull(FileImport.Redirected, "sanity: the Import folder is the test's scratch one, never the player's");
            if (System.IO.Directory.Exists(FileImport.Folder))
                System.IO.Directory.Delete(FileImport.Folder, true);
            System.IO.Directory.CreateDirectory(FileImport.Folder);
            foreach ((string name, string text) in files)
            {
                string path = System.IO.Path.Combine(FileImport.Folder, name);
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
                System.IO.File.WriteAllText(path, text);
            }
        }

        /// <summary>
        /// FILE, IMPORT reads the Import folder as a circuit of its own, named after its project (Part 13d, 15a): the testbench
        /// runs the design as written, what the import said is in the TRANSCRIPT with its file and line, and a file that does
        /// not read is named there.
        /// </summary>
        [UnityTest]
        public IEnumerator FileImport_BringsTheProjectInAsACircuit_RunByItsTestbench()
        {
            PutInTheImportFolder(("mux2.sv", ImportedMux), ("mux2_tb.sv", ImportedMuxBench), ("broken.sv", "module broken (input a;\nendmodule\n"),
                (".git/HEAD", "ref: refs/heads/main\n"), (".git/config", "[core]\n"));
            yield return TestScene.Load();
            yield return Click(MainMenu.WorkbenchButton);
            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            yield return null;

            yield return Click(WorkbenchPanel.FileButton);
            yield return Click(FileMenu.ActionItem + FileAction.ImportFolder);
            Assert.IsFalse(workbench.Ask.IsOpen, "an import asks nothing: it touches no circuit already there");
            yield return null;
            workbench.Pause();
            workbench.Session.Finish();
            yield return null;

            Assert.AreEqual(0, workbench.Sample, "a circuit, not a sample");
            Assert.AreEqual(BitSorter.ViewModel.Documents.KeyOf("mux2"), workbench.DocumentKey, "named after its project");
            Assert.AreEqual("mux2_tb", workbench.Session.Project.Bench);
            Assert.AreEqual("mux2", workbench.Session.Project.Top);
            Assert.AreEqual("mux2_tb.dut", workbench.Session.Run.Model.Layout.DesignPath);
            Assert.AreEqual(20, workbench.Session.Run.Model.End, "shown to where the testbench stops");

            workbench.ShowTranscript();
            yield return null;
            Assert.IsTrue(workbench.Transcript.Lines.Any(l => l.Severity == TranscriptSeverity.Error && l.Text.StartsWith("broken.sv:1:")),
                "the file that did not read, named with its line");
            Assert.IsTrue(workbench.Transcript.Lines.Any(l => l.Text == "y is 1"), "and what the testbench printed: s is 1 by then, so y is b");
            Assert.IsEmpty(workbench.Session.Project.Files, "a hidden folder's files, .git's, are not the project's");

            Assert.IsTrue(workbench.Session.SetUnit("ns").Done);
            yield return null;
            Assert.IsFalse(workbench.Transcript.Lines.Any(l => l.Text.StartsWith("broken.sv:")), "what the import said goes with the first edit");
        }

        /// <summary>
        /// An import never replaces a circuit (Part 15a, where it asked before replacing MY CIRCUIT): the same project imported
        /// twice is two circuits, the second numbered, MY CIRCUIT as it was, and FILE, OPEN lists all three, the latest first.
        /// </summary>
        [UnityTest]
        public IEnumerator FileImport_IsACircuitOfItsOwn_EveryTime_AndMyCircuitIsLeftAlone()
        {
            PutInTheImportFolder(("mux2.sv", ImportedMux), ("mux2_tb.sv", ImportedMuxBench));
            yield return TestScene.Load();
            yield return Click(MainMenu.WorkbenchButton);
            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            workbench.ShowSample(0);
            yield return null;
            Assert.AreEqual(WorkbenchSamples.MyCircuitKey, workbench.DocumentKey, "sanity: MY CIRCUIT open");
            yield return Place(workbench, "and", new BoardPoint(4, 0), new System.Collections.Generic.List<int>());
            Project mine = workbench.Session.Project;

            workbench.ImportFromFolder();
            yield return null;
            Assert.AreEqual("mux2", workbench.Session.Project.Top, "sanity: imported");
            Assert.AreEqual(BitSorter.ViewModel.Documents.KeyOf("mux2"), workbench.DocumentKey);

            workbench.ImportFromFolder();
            yield return null;
            Assert.IsFalse(workbench.Ask.IsOpen, "nothing to ask");
            Assert.AreEqual(BitSorter.ViewModel.Documents.KeyOf("mux2 2"), workbench.DocumentKey, "the same project again, numbered");

            workbench.OpenOpenMenu();
            yield return null;
            CollectionAssert.AreEqual(new[] { BitSorter.ViewModel.Documents.KeyOf("mux2 2"), BitSorter.ViewModel.Documents.KeyOf("mux2"),
                WorkbenchSamples.MyCircuitKey }, workbench.OpenMenu.Listed, "every circuit, the latest first");
            yield return Click(BitSorter.View.DocumentMenu.SlotName(2));
            yield return null;
            Assert.AreEqual(WorkbenchSamples.MyCircuitKey, workbench.DocumentKey);
            Assert.AreEqual(mine.TopModule.Parts.Count, workbench.Session.Project.TopModule.Parts.Count, "MY CIRCUIT as it was left, its AND and all");
        }

        /// <summary>
        /// FILE's circuits (Part 15a): NEW asks a name, offering a free one, and opens a blank circuit of that name, kept apart
        /// from MY CIRCUIT; RENAME and DELETE are offered for it, never for MY CIRCUIT; a name taken is refused, saying so; DELETE
        /// asks first and opens MY CIRCUIT; and an old free-play board, which the save's migration made a document, is listed.
        /// </summary>
        [UnityTest]
        public IEnumerator FileNewRenameDeleteAndOpen_KeepEachCircuitApart()
        {
            yield return TestScene.Load();
            yield return Click(MainMenu.WorkbenchButton);
            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            workbench.ShowSample(0);
            yield return null;
            ProgressStore store = Find<ProgressTracker>().Store;
            string board = Documents.FreePlayPrefix + "My board";
            store.SetProject(board, Project.New("My_board"));

            yield return Click(WorkbenchPanel.FileButton);
            Assert.IsFalse(workbench.FileMenu.Offers(FileAction.RenameCircuit), "MY CIRCUIT is not renamed");
            Assert.IsFalse(workbench.FileMenu.Offers(FileAction.DeleteCircuit), "nor deleted");
            yield return Click(FileMenu.ActionItem + FileAction.NewCircuit);
            yield return null;
            Assert.IsTrue(workbench.CircuitNameField.IsOpen, "NEW asks a name");
            Assert.AreEqual("circuit1", workbench.CircuitNameField.Field.value, "offering a free one");
            workbench.CircuitNameField.Confirm("adder");
            yield return null;
            Assert.AreEqual(Documents.KeyOf("adder"), workbench.DocumentKey);
            Assert.AreEqual("adder", workbench.Session.Project.Top, "a blank circuit, its module named for it");
            yield return Place(workbench, "xor", new BoardPoint(4, 0), new System.Collections.Generic.List<int>());
            yield return Click(WorkbenchPanel.FileButton);
            Assert.IsTrue(workbench.FileMenu.Offers(FileAction.RenameCircuit), "a circuit of their own is renamed");
            workbench.FileMenu.Close();
            yield return null;

            workbench.RenameCircuit("My Circuit");
            StringAssert.Contains("called my circuit already", workbench.ChipText, "a name taken, by any case, refused");
            workbench.RenameCircuit("half adder");
            yield return null;
            Assert.AreEqual(Documents.KeyOf("half adder"), workbench.DocumentKey);
            Assert.IsTrue(store.HasProject(Documents.KeyOf("half adder")) && !store.HasProject(Documents.KeyOf("adder")), "kept under its new name");
            Assert.AreEqual(WaveExport.FileName("half adder", "vcd"), workbench.ExportVcd(), "and exported under it, not MY CIRCUIT's");

            workbench.OpenOpenMenu();
            yield return null;
            CollectionAssert.AreEquivalent(new[] { Documents.KeyOf("half adder"), WorkbenchSamples.MyCircuitKey, board }, workbench.OpenMenu.Listed,
                "every circuit, the old free-play board among them");
            Assert.AreEqual(Documents.KeyOf("half adder"), workbench.OpenMenu.Listed[0], "the one open first");
            workbench.OpenMenu.Close();
            yield return null;

            workbench.AskDeleteCircuit();
            Assert.IsTrue(workbench.Ask.IsOpen, "DELETE asks first");
            StringAssert.Contains("Delete the circuit half adder? It cannot be brought back.", workbench.Ask.Question);
            yield return Click(AskMenu.NoItem);
            Assert.AreEqual(Documents.KeyOf("half adder"), workbench.DocumentKey, "NO keeps it");
            workbench.AskDeleteCircuit();
            yield return Click(AskMenu.YesItem);
            yield return null;
            Assert.AreEqual(WorkbenchSamples.MyCircuitKey, workbench.DocumentKey, "and MY CIRCUIT opens in its place");
            Assert.IsFalse(store.HasProject(Documents.KeyOf("half adder")));
            Assert.IsEmpty(workbench.Session.Project.TopModule.Parts.OfType<GatePart>(), "with nothing of the deleted circuit's in it");
        }

        /// <summary>FILE, OPEN pages through the circuits twelve at a time (Part 15a), so none is out of reach.</summary>
        [UnityTest]
        public IEnumerator FileOpen_PagesThroughEveryCircuit()
        {
            yield return TestScene.Load();
            yield return Click(MainMenu.WorkbenchButton);
            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            workbench.ShowSample(0);
            yield return null;
            ProgressStore store = Find<ProgressTracker>().Store;
            for (int i = 1; i <= 15; i++)
                store.SetProject(Documents.KeyOf("c" + i), Project.New("c" + i));

            workbench.OpenOpenMenu();
            yield return null;
            Assert.AreEqual(DocumentMenu.Slots, workbench.OpenMenu.Listed.Count, "a page at a time");
            Assert.IsTrue(workbench.OpenMenu.OffersOlder && !workbench.OpenMenu.OffersNewer, "the first page, the rest older");
            yield return Click(DocumentMenu.OlderItem);
            yield return null;
            Assert.IsTrue(workbench.OpenMenu.IsOpen, "OLDER keeps the list up");
            Assert.AreEqual(16 - DocumentMenu.Slots, workbench.OpenMenu.Listed.Count, "the rest on the next page");
            Assert.IsTrue(workbench.OpenMenu.OffersNewer && !workbench.OpenMenu.OffersOlder, "the last page, the rest newer");

            int at = workbench.OpenMenu.Listed.ToList().FindIndex(key => key != WorkbenchSamples.MyCircuitKey);
            string chosen = workbench.OpenMenu.Listed[at];
            yield return Click(DocumentMenu.SlotName(at));
            yield return null;
            Assert.AreEqual(chosen, workbench.DocumentKey, "and one there opens");
        }

        /// <summary>
        /// RESET PROGRESS starts the exercises over and keeps the student's circuits (Part 15g): the circuit open stays open,
        /// as it was, and is still in the save. It used to empty the save, circuits and all (Part 15a), and Ahmad asked for
        /// it to forget the exercises only (2026-10-28).
        /// </summary>
        [UnityTest]
        public IEnumerator ResetProgress_KeepsTheCircuits_AndTheOneOpen()
        {
            yield return TestScene.Load();
            yield return Click(MainMenu.WorkbenchButton);
            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            workbench.ShowSample(0);
            yield return null;
            workbench.NewCircuit("foo");
            yield return null;
            yield return Place(workbench, "xor", new BoardPoint(4, 0), new System.Collections.Generic.List<int>());
            yield return Click(WorkbenchPanel.SettingsButton);

            ProgressStore store = Find<ProgressTracker>().Store;
            Assert.IsTrue(store.HasProject(Documents.KeyOf("foo")), "sanity: going under Settings kept it");
            Assert.IsTrue(Find<ProgressTracker>().ResetProgress());
            yield return Click(SettingsPanel.BackButton);
            yield return null;

            Assert.IsTrue(store.HasProject(Documents.KeyOf("foo")), "the circuit is kept");
            Assert.AreEqual(Documents.KeyOf("foo"), workbench.DocumentKey, "the circuit open stays open");
            Assert.AreEqual(1, workbench.Session.Project.TopModule.Parts.OfType<GatePart>().Count(), "as it was");
            Assert.IsFalse(workbench.StartPageShowing);
        }

        /// <summary>
        /// RESET PROGRESS with the start page up (the 15g-1 review): coming back from Settings, its EXERCISES column says the
        /// exercises start over -- it went on saying what had been solved until HOME.
        /// </summary>
        [UnityTest]
        public IEnumerator ResetProgress_WithTheStartPageUp_ShowsTheExercisesStartingOver()
        {
            yield return TestScene.LoadSimulator();
            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            Assert.IsTrue(workbench.StartPageShowing, "sanity: a fresh save opens on the start page");
            ProgressStore store = Find<ProgressTracker>().Store;
            store.MarkComplete(ExerciseCatalog.Run[0].Id);
            workbench.ShowHome();
            yield return null;
            StringAssert.Contains(ExerciseCatalog.Run[1].Title.ToUpperInvariant(), workbench.StartContinueCaption, "sanity: the next after the first solved");

            yield return Click(WorkbenchPanel.SettingsButton);
            Assert.IsTrue(Find<ProgressTracker>().ResetProgress());
            yield return Click(SettingsPanel.BackButton);
            yield return null;
            Assert.IsTrue(workbench.StartPageShowing);
            StringAssert.Contains(ExerciseCatalog.Run[0].Title.ToUpperInvariant(), workbench.StartContinueCaption, "the first again");
        }

        /// <summary>
        /// RESET PROGRESS with an exercise open (Part 15g): its solve and its answer are forgotten, the workbench lets it go
        /// without writing it back, and the start page opens over MY CIRCUIT, which is kept.
        /// </summary>
        [UnityTest]
        public IEnumerator ResetProgress_WithAnExerciseOpen_ForgetsItsAnswer_AndOpensTheStartPage()
        {
            yield return TestScene.Load();
            yield return Click(MainMenu.WorkbenchButton);
            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            workbench.ShowSample(0);
            yield return null;
            yield return Place(workbench, "and", new BoardPoint(2, 0), new System.Collections.Generic.List<int>());
            workbench.OpenExercise("half-adder");
            yield return null;
            workbench.ExerciseCard.Close();   // its brief opens over the circuit
            yield return null;
            yield return Place(workbench, "xor", new BoardPoint(0, 0), new System.Collections.Generic.List<int>());
            yield return Click(WorkbenchPanel.SettingsButton);

            ProgressStore store = Find<ProgressTracker>().Store;
            store.MarkComplete("half-adder");
            Assert.IsTrue(store.HasProject("half-adder"), "sanity: its answer was kept");
            Assert.IsTrue(Find<ProgressTracker>().ResetProgress());
            Assert.IsFalse(store.HasProject("half-adder"), "its answer is forgotten");
            Assert.IsFalse(store.IsComplete("half-adder"), "and its solve");
            Assert.IsTrue(store.HasProject(WorkbenchSamples.MyCircuitKey), "MY CIRCUIT is kept");

            yield return Click(SettingsPanel.BackButton);
            yield return null;
            Assert.IsTrue(workbench.StartPageShowing, "the start page, over MY CIRCUIT");
            Assert.AreEqual(WorkbenchSamples.MyCircuitKey, workbench.DocumentKey);
            Assert.AreEqual(1, workbench.Session.Project.TopModule.Parts.OfType<GatePart>().Count(), "MY CIRCUIT as it was");
            yield return Click(WorkbenchPanel.SettingsButton);
            Assert.IsFalse(store.HasProject("half-adder"), "nothing of the exercise written back");
        }

        /// <summary>
        /// RESET PROGRESS on Settings with an exercise open, Settings then left by the header's HOME rather than BACK (the
        /// 15g-2 review). Settings is a page over the workbench, which stays in sight, so MY CIRCUIT takes the exercise's
        /// place at once: left on the stage, the exercise's circuit was MY CIRCUIT's to keep, and its next edit wrote it over
        /// the student's own. And the start page the reset owes comes up once, not again on a later BACK.
        /// </summary>
        [UnityTest]
        public IEnumerator ResetProgress_OnSettings_LeftByHome_LeavesNothingOfTheExerciseOpen()
        {
            yield return TestScene.LoadSimulator();
            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            workbench.ShowSample(0);
            yield return null;
            yield return Place(workbench, "and", new BoardPoint(2, 0), new System.Collections.Generic.List<int>());
            workbench.OpenExercise("half-adder");
            yield return null;
            workbench.ExerciseCard.Close();   // its brief opens over the circuit
            yield return null;
            yield return Place(workbench, "xor", new BoardPoint(0, 0), new System.Collections.Generic.List<int>());
            yield return Click(WorkbenchPanel.SettingsButton);

            Assert.IsTrue(Find<ProgressTracker>().ResetProgress());
            Assert.IsTrue(Find<SettingsPanel>().IsShowing, "sanity: still on Settings");
            Assert.AreEqual(WorkbenchSamples.MyCircuitKey, workbench.DocumentKey);
            Assert.AreEqual(BitSorter.Sim.GateKind.And, Top(workbench).Parts.OfType<GatePart>().Single().Kind,
                "MY CIRCUIT's AND on the stage at once, not the exercise's XOR");

            yield return Click(WorkbenchPanel.HomeButton);
            Assert.IsFalse(Find<SettingsPanel>().IsShowing, "HOME took Settings down");
            Assert.IsTrue(workbench.StartPageShowing);
            yield return Tap(_keyboard.escapeKey);
            Assert.IsFalse(workbench.StartPageShowing, "sanity: Escape took the start page down");
            Assert.AreEqual(BitSorter.Sim.GateKind.And, Top(workbench).Parts.OfType<GatePart>().Single().Kind, "MY CIRCUIT under it");

            yield return Click(WorkbenchPanel.SettingsButton);
            yield return Click(SettingsPanel.BackButton);
            Assert.IsFalse(workbench.StartPageShowing, "the start page the reset owed was seen to by HOME, and does not come up again");
        }

        /// <summary>
        /// While Settings is up the workbench takes no keys (the 15g-2 review): S, the sample chip's key on the start page,
        /// changed the circuit under Settings unseen. The chip itself, in the header over Settings, still shows the next
        /// sample, and takes Settings down as HOME does.
        /// </summary>
        [UnityTest]
        public IEnumerator UnderSettings_TheWorkbenchTakesNoKeys_AndTheSampleChipTakesSettingsDown()
        {
            yield return TestScene.LoadSimulator();
            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            workbench.ShowSample(0);
            yield return null;
            yield return Click(WorkbenchPanel.SettingsButton);
            Assert.IsTrue(Find<SettingsPanel>().IsShowing, "sanity: Settings is up");

            yield return Tap(_keyboard.sKey);
            Assert.AreEqual(0, workbench.Sample, "S on Settings is not the sample chip's");
            Assert.IsTrue(Find<SettingsPanel>().IsShowing);

            yield return Click(WorkbenchPanel.SampleButton);
            Assert.AreNotEqual(0, workbench.Sample, "the chip over Settings shows a sample");
            Assert.IsFalse(Find<SettingsPanel>().IsShowing, "and takes Settings down");
        }

        /// <summary>
        /// The key that takes the credits down is theirs alone (the 15g-2 review): from HELP's ABOUT, over a circuit with a
        /// part chosen, Delete takes the roll down and deletes nothing. Under a uGUI panel the workbench came back "opened
        /// this frame", which held the key; as a page over it, whichever updated second took it too.
        /// </summary>
        [UnityTest]
        public IEnumerator TheKeyThatTakesTheCreditsDown_DoesNothingElse()
        {
            yield return TestScene.LoadSimulator();
            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            workbench.ShowSample(0);
            yield return null;
            yield return Place(workbench, "and", new BoardPoint(2, 0), new System.Collections.Generic.List<int>());
            Assert.AreEqual(1, workbench.Editor.Selection.Parts.Count, "sanity: the AND is chosen");

            Find<CreditsPanel>().Open(workbench.Resume);   // as HELP's ABOUT opens it
            yield return null;
            yield return null;
            Assert.IsTrue(Find<CreditsPanel>().IsShowing, "sanity: the roll is up");

            yield return Tap(_keyboard.deleteKey);
            Assert.IsFalse(Find<CreditsPanel>().IsShowing, "a key takes the roll down");
            Assert.AreEqual(1, Top(workbench).Parts.Count, "and deletes nothing");
        }

        /// <summary>
        /// FILE, CONVERT is offered only while a testbench drives the inputs (Part 13d); it asks, saying what the stimulus will
        /// not keep, and then the inputs are steps SET can change.
        /// </summary>
        [UnityTest]
        public IEnumerator FileConvert_AsksWhatItLoses_ThenTheInputsAreSteps()
        {
            PutInTheImportFolder(("mux2.sv", ImportedMux), ("mux2_tb.sv", ImportedMuxBench));
            yield return TestScene.Load();
            yield return Click(MainMenu.WorkbenchButton);
            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            yield return null;
            workbench.ImportFromFolder();
            yield return null;
            workbench.Pause();
            workbench.Session.Finish();
            Assert.IsFalse(workbench.Session.SetInput("a", 0, 5, LogicVec.FromBinary("1")).Done, "sanity: the testbench drives the inputs");

            yield return Click(WorkbenchPanel.FileButton);
            yield return Click(FileMenu.ActionItem + FileAction.ConvertTestbench);
            Assert.IsTrue(workbench.Ask.IsOpen, "CONVERT asks first");
            StringAssert.Contains("$display, in one place", workbench.Ask.Question, "saying what will not be kept");
            yield return Click(AskMenu.YesItem);
            yield return null;
            workbench.Session.Finish();
            yield return null;

            Assert.IsNull(workbench.Session.Project.Bench);
            Assert.IsTrue(workbench.Session.SetInput("a", 0, 5, LogicVec.FromBinary("1")).Done, "the inputs are the stimulus's now");

            yield return Click(WorkbenchPanel.FileButton);
            Assert.IsFalse(workbench.FileMenu.Offers(FileAction.ConvertTestbench), "and CONVERT is not offered without a testbench");
            workbench.FileMenu.Close();
        }

        /// <summary>
        /// A double-click on the wave of a written design's input, which its testbench drives, says the testbench drives it and
        /// how to change it -- never that it is no input of the design, which a 64-bit ALU imported with its testbench was told
        /// (Ahmad, 2026-10-28): a written top's inputs are its header's, and none is drawn.
        /// </summary>
        [UnityTest]
        public IEnumerator AWrittenDesignsInput_DrivenByItsTestbench_IsSaidToBe()
        {
            PutInTheImportFolder(("mux2.sv", "module mux2 (input logic a, input logic b, input logic s, output logic y);\n" +
                "    always_comb y = s ? b : a;\nendmodule\n"), ("mux2_tb.sv", ImportedMuxBench));
            yield return TestScene.Load();
            yield return Click(MainMenu.WorkbenchButton);
            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            yield return null;
            workbench.ImportFromFolder();
            yield return null;
            workbench.Pause();
            workbench.Session.Finish();
            Assert.IsTrue(workbench.Session.Project.TopModule.IsWritten, "sanity: the design is written code");

            int row = workbench.List.Lines.ToList().FindIndex(line => workbench.List.LabelOf(line) == "a");
            Assert.GreaterOrEqual(row, 0, "sanity: a has a row");
            workbench.EditInputAt(row, 5, new Vector2(Screen.width * 0.5f, Screen.height * 0.3f));
            yield return null;

            StringAssert.DoesNotContain("not an input", workbench.ChipText);
            StringAssert.Contains("testbench", workbench.ChipText, "the testbench drives it");
            StringAssert.Contains("CONVERT", workbench.ChipText, "and how to change it");
        }

        /// <summary>A homework file in the course's style, written for this test: the student's code goes between its markers.</summary>
        private const string TemplateMux =
            "// Homework: a 2-to-1 multiplexer. Written for BitSorter's tests, in the course's style.\n" +
            "module mux2 (input logic a, input logic b, input logic s, output logic y);\n" +
            "    // Put your code here\n" +
            "\n" +
            "    // End of your code\n" +
            "endmodule\n";

        /// <summary>
        /// FILE, EXPORT FOR SUBMISSION (Part 13e) is offered once a module came from a course template, and saves the template
        /// as given with the code on screen between its markers, in the Exports folder -- redirected while a test runs. Code
        /// typed and not yet applied is applied first, and code that cannot apply is refused, never handed in as the last text
        /// that did.
        /// </summary>
        [UnityTest]
        public IEnumerator FileExportForSubmission_SavesTheTemplateWithTheCodeOnScreenBetweenItsMarkers()
        {
            PutInTheImportFolder(("mux2.sv", TemplateMux), ("mux2_tb.sv", ImportedMuxBench));
            yield return TestScene.Load();
            yield return Click(MainMenu.WorkbenchButton);
            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            yield return null;

            yield return Click(WorkbenchPanel.FileButton);
            Assert.IsFalse(workbench.FileMenu.Offers(FileAction.ExportSubmission), "not offered while nothing came from a template");
            workbench.FileMenu.Close();
            yield return null;

            workbench.ImportFromFolder();
            yield return null;
            workbench.Pause();
            workbench.Session.Finish();
            yield return null;
            Module mux = workbench.Session.Project.Find("mux2");
            Assert.AreEqual("mux2.sv", mux.Template?.File, "sanity: the import remembered the template");
            Assert.AreEqual("mux2", workbench.Session.Module, "sanity: the code shown is the design's");
            Assert.AreEqual(Documents.KeyOf("mux2"), workbench.DocumentKey, "sanity: the import is a circuit of its own (Part 15a)");
            string path = System.IO.Path.Combine(FileSaver.Folder, WaveExport.FileName("mux2 submission", "zip"));   // named after it
            Assert.IsNotNull(FileSaver.Redirected, "sanity: the Exports folder is the test's scratch one, never the player's");
            if (System.IO.File.Exists(path))
                System.IO.File.Delete(path);   // an earlier run's, in the scratch folder

            // Typed with a fault, and not applied: refused, and nothing saved. Typed on the code editor's clock, in
            // milliseconds (CodeEditorView), or the view would find the pause long over and apply clean code itself.
            CodeDocument code = workbench.Session.Code;
            code.Type(mux.Source.Replace("    // Put your code here\n", "    // Put your code here\n    assign y = s ? b : a\n"), Time.timeAsDouble * 1000.0);
            yield return Click(WorkbenchPanel.FileButton);
            Assert.IsTrue(workbench.FileMenu.Offers(FileAction.ExportSubmission), "offered: the design came from a template");
            yield return Click(FileMenu.ActionItem + FileAction.ExportSubmission);
            Assert.IsFalse(System.IO.File.Exists(path), "code on screen that does not apply is not handed in as the last that did");
            Assert.AreEqual(mux.Source, workbench.Session.Project.Find("mux2").Source, "and the document keeps the last code that worked");

            // Typed and not applied yet: applied, then handed in.
            code.Type(mux.Source.Replace("    // Put your code here\n", "    // Put your code here\n    assign y = s ? b : a;\n"), Time.timeAsDouble * 1000.0);
            yield return Click(WorkbenchPanel.FileButton);
            Assert.IsTrue(code.Pending, "sanity: still typed and not applied, inside the editor's pause, as the export is chosen");
            yield return Click(FileMenu.ActionItem + FileAction.ExportSubmission);

            Assert.IsTrue(System.IO.File.Exists(path), path);
            string bytes = System.Text.Encoding.UTF8.GetString(System.IO.File.ReadAllBytes(path));
            StringAssert.Contains("-submission/mux2.sv", bytes, "under the template's own name");
            StringAssert.Contains("// Homework: a 2-to-1 multiplexer.", bytes, "the template as given");
            StringAssert.Contains("    // Put your code here\n    assign y = s ? b : a;\n", bytes, "with the code on screen between its markers");
            Assert.IsFalse(workbench.Session.Code.Pending, "applied on the way out");
        }

        /// <summary>
        /// Every item the FILE menu shows fits it: a caption wider than its item runs out past the menu's edge. Shown on a
        /// template run by its testbench, so CONVERT (Part 13d) and EXPORT FOR SUBMISSION (13e) are both up.
        /// </summary>
        [UnityTest]
        public IEnumerator TheFileMenu_ShowsEveryItemsCaptionWhole()
        {
            PutInTheImportFolder(("mux2.sv", TemplateMux), ("mux2_tb.sv", ImportedMuxBench));
            yield return TestScene.Load();
            yield return Click(MainMenu.WorkbenchButton);
            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            yield return null;
            workbench.ImportFromFolder();
            yield return null;

            yield return Click(WorkbenchPanel.FileButton);
            Assert.IsTrue(workbench.FileMenu.Offers(FileAction.ConvertTestbench) && workbench.FileMenu.Offers(FileAction.ExportSubmission),
                "sanity: every item that can be is up");
            var tooWide = new System.Collections.Generic.List<string>();
            int measured = 0;
            foreach (FileAction action in System.Enum.GetValues(typeof(FileAction)))
            {
                UnityEngine.UIElements.Button item = ShellPanel.Find<UnityEngine.UIElements.Button>(FileMenu.ActionItem + action);
                if (item == null || item.resolvedStyle.display == UnityEngine.UIElements.DisplayStyle.None)
                    continue;
                Vector2 caption = item.MeasureTextSize(item.text, 0, UnityEngine.UIElements.VisualElement.MeasureMode.Undefined,
                    0, UnityEngine.UIElements.VisualElement.MeasureMode.Undefined);
                measured++;
                if (caption.x > item.contentRect.width + 0.5f)
                    tooWide.Add($"{item.text}: {caption.x:0} wide in {item.contentRect.width:0}");
            }

            workbench.FileMenu.Close();
            Assert.Greater(measured, 3, "sanity: the items were measured");
            Assert.IsEmpty(tooWide, "captions wider than their items");
        }

        /// <summary>The wheel over a written block's line numbers scrolls its code, as it does over the code itself (Part 12d).</summary>
        [UnityTest]
        public IEnumerator TheWheelOverTheCodesLineNumbers_ScrollsTheCode()
        {
            yield return TestScene.Load();
            yield return Click(MainMenu.WorkbenchButton);
            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            workbench.ShowSample(SampleNamed("Written adder"));
            yield return null;
            workbench.Pause();
            workbench.Session.Finish();
            yield return null;
            Assert.IsTrue(workbench.StepInto(Top(workbench).Parts.OfType<BlockPart>().Single().Id));
            yield return null;
            CodeEditorView code = workbench.CodeView;

            // Long enough to scroll: forty lines of comment under the module.
            code.Field.text = WorkbenchSamples.AdderSource + string.Concat(Enumerable.Repeat("// more\n", 40));
            yield return null;
            yield return null;
            float before = code.Field.textComponent.rectTransform.anchoredPosition.y;

            Rect editor = WorldRect((RectTransform)code.transform);
            Move(_mouse.position, new Vector2(editor.xMin + 24f, editor.center.y));
            yield return null;
            Set(_mouse.scroll, new Vector2(0f, -120f));
            yield return null;
            yield return null;
            Assert.Greater(code.Field.textComponent.rectTransform.anchoredPosition.y, before, "the wheel over the line numbers scrolled the code down");
        }

        /// <summary>Zoomed in, the minimap shows; a click on its left side moves the view there; FIT puts it away (Part 10d).</summary>
        [UnityTest]
        public IEnumerator TheMinimap_ShowsOffTheFit_AClickOnItMovesTheView_AndFitPutsItAway()
        {
            yield return TestScene.Load();
            yield return OpenTheReference();
            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            Assert.IsNull(workbench.Map, "at the fit, no map");

            for (int i = 0; i < 4; i++)
                yield return Tap(_keyboard.equalsKey);
            Minimap map = workbench.Map;
            Assert.IsNotNull(map, "zoomed in, the map is up");
            Rect2 before = map.Window;

            // Beside the circuit's window, never on it (Part 10e): in its corner it hid what was there. In the side panel,
            // at its foot (Part 11b).
            RectTransform[] parts = GameObject.Find(WorkbenchPanel.RootName).GetComponentsInChildren<RectTransform>();
            Rect onMap = WorldRect(parts.Single(r => r.name == "minimap")), window = WorldRect(parts.Single(r => r.name == "schematic view"));
            Assert.IsFalse(onMap.Overlaps(window), $"the map, {onMap}, is over the circuit's window, {window}");
            Rect side = ShellPanel.ScreenRectOf(ShellPanel.Find<UnityEngine.UIElements.VisualElement>(WorkbenchPanel.SidePanelName));
            Assert.IsTrue(side.Contains(onMap.min) && side.Contains(onMap.max), $"the map, {onMap}, is not in the side panel, {side}");
            Assert.Less(onMap.yMin - side.yMin, side.yMax - onMap.yMax, "and at its foot");

            yield return ClickAtScreen(workbench.ScreenPointOfMap(new Vec2(map.Size.x * 0.1f, map.Size.y * 0.5f)));
            Assert.Less(workbench.Map.Window.center.x, before.center.x, "a click on the map's left moved the window left");
            Assert.AreEqual(0, Top(workbench).Parts.Count(p => workbench.Editor.Selection.HasPart(p.Id)), "and selected nothing under it");

            yield return Tap(_keyboard.fKey);
            Assert.IsNull(workbench.Map, "FIT puts it away");
        }

        private IEnumerator ClickAtScreen(Vector2 point)
        {
            Move(_mouse.position, point);
            yield return null;
            Press(_mouse.leftButton);
            yield return null;
            Release(_mouse.leftButton);
            yield return null;
            yield return null;
        }

        private static Rect WorldRect(RectTransform rect)
        {
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            return Rect.MinMaxRect(corners[0].x, corners[0].y, corners[2].x, corners[2].y);
        }
    }
}
