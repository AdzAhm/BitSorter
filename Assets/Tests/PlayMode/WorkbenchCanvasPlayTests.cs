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
