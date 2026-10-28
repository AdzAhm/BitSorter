using System;
using System.Collections.Generic;
using System.Linq;
using BitSorter.Board;
using BitSorter.SystemVerilog;
using BitSorter.SystemVerilog.Syntax;
using NUnit.Framework;
using BoardPoint = BitSorter.Board.Point;
using EngineGate = BitSorter.Sim.GateKind;

namespace BitSorter.ViewModel.Tests
{
    /// <summary>
    /// Wires drawn on the canvas (Part 10b), all of it Unity-free: the gestures that start, bend and end one, from a
    /// scripted list of pointer events; and whole wires made through the editor and the session -- pin to pin, through
    /// bends clicked, onto a wire as a tap and out of one -- with what the document refuses shown while the pointer is
    /// on it and said when it is clicked.
    /// </summary>
    public class WiringTests
    {
        // -----------------------------------------------------------------
        // Gestures
        // -----------------------------------------------------------------

        private static readonly CanvasTarget OnY = new CanvasTarget(CanvasTargetKind.Pin, part: 3, pin: "y");
        private static readonly CanvasTarget OnIn = new CanvasTarget(CanvasTargetKind.Pin, part: 5, pin: "in");
        private static readonly CanvasTarget OnWire = new CanvasTarget(CanvasTargetKind.Wire, wire: 7, segment: 0, along: 0.5);

        private static CanvasPress At(CanvasTarget target, int button = 0, bool shift = false, bool ctrl = false, bool alt = false, bool drafting = false,
            bool selected = false) => new CanvasPress
        {
            Button = button, Window = new Vec2(100, 100), Board = new BoardPoint(4, 0), Target = target, OnSelected = selected,
            Shift = shift, Ctrl = ctrl, Alt = alt, Drafting = drafting,
        };

        /// <summary>A press, moves to <paramref name="moves"/> pixels right of it (a cell each 48), and the release over <paramref name="release"/>, as the commands they made.</summary>
        private static string Run(CanvasPointer pointer, CanvasPress press, CanvasTarget release = default, params float[] moves)
        {
            var commands = new List<CanvasCommand>();
            pointer.Press(press, commands);
            float last = 0;
            foreach (float px in moves)
            {
                last = px;
                pointer.Move(press.Window + new Vec2(px, 0), new BoardPoint(4 + px / 48.0, 0), false, commands);
            }

            pointer.Release(new BoardPoint(4 + last / 48.0, 0), release, false, commands);
            Assert.IsFalse(pointer.Held, "let go");
            return string.Join(", ", commands);
        }

        [Test]
        public void APressOnAPin_StartsAWire_LetGoOnAPinItEndsThere_AndAnywhereElseItBends()
        {
            var pointer = new CanvasPointer();
            Assert.AreEqual("BeginWire pin 3.y, EndWire pin 5.in", Run(pointer, At(OnY), release: OnIn, moves: 48f), "dragged pin to pin");
            Assert.AreEqual("BeginWire pin 3.y, EndWire wire 7 segment 0", Run(pointer, At(OnY), release: OnWire, moves: 48f), "onto a wire: a tap");
            Assert.AreEqual("BeginWire pin 3.y, WireBend 5,0", Run(pointer, At(OnY), moves: 48f), "let go on nothing: a bend, and the wire goes on");
            Assert.AreEqual("BeginWire pin 3.y", Run(pointer, At(OnY), release: OnY), "a click: the wire goes on being drawn, a click at a time");
            Assert.AreEqual("Select pin 3.y +", Run(pointer, At(OnY, shift: true)), "Shift picks the part instead, as on its body");
        }

        [Test]
        public void WhileAWireIsDrawn_AClickOnNothingBendsIt_OnAPinOrAWireEndsIt_AndTheRightButtonLetsItGo()
        {
            var pointer = new CanvasPointer();
            Assert.AreEqual("WireBend 4,0", Run(pointer, At(CanvasTarget.Nothing, drafting: true)));
            Assert.AreEqual("WireBend 4,0 free", Run(pointer, At(CanvasTarget.Nothing, drafting: true, alt: true)), "Alt: exactly where it is");
            Assert.AreEqual("WireBend 4,0", Run(pointer, At(new CanvasTarget(CanvasTargetKind.Part, part: 3), drafting: true)), "over a part's body too");
            Assert.AreEqual("EndWire pin 5.in", Run(pointer, At(OnIn, drafting: true)));
            Assert.AreEqual("EndWire wire 7 segment 0", Run(pointer, At(OnWire, drafting: true)));
            Assert.AreEqual("Cancel", Run(pointer, At(CanvasTarget.Nothing, button: 1, drafting: true)));
        }

        [Test]
        public void ADragOutOfAWire_DrawsANewWireTappingIt_AndAClickSelectsIt()
        {
            var pointer = new CanvasPointer();
            Assert.AreEqual("BeginWire wire 7 segment 0, EndWire pin 5.in", Run(pointer, At(OnWire), release: OnIn, moves: 48f));
            Assert.AreEqual("Select wire 7 segment 0", Run(pointer, At(OnWire)), "a click on it, on release");
            Assert.AreEqual("Select wire 7 segment 0", Run(pointer, At(OnWire), moves: 2f), "a hand that shakes still clicks");
        }

        // -----------------------------------------------------------------
        // Wires made
        // -----------------------------------------------------------------

        private static Func<double> Ticking(double step)
        {
            double now = 0;
            return () => { double read = now; now += step; return read; };
        }

        /// <summary>The half adder's parts and none of its wires: a and b, an XOR above an AND, and their outputs.</summary>
        private sealed class Sketch
        {
            public Sketch()
            {
                var editor = new BoardEditor(Project.New("adder"));
                int Must(EditResult r) { Assert.IsTrue(r.Done, r.Refusal); return r.Id; }
                // The inputs as the strip places them, each given 0 from the start.
                CatalogEntry input = PartsCatalog.Find(editor.Project, "adder", "input");
                A = Must(PartsCatalog.Place(editor, "adder", input, new Placement(1, 1)));
                B = Must(PartsCatalog.Place(editor, "adder", input, new Placement(1, -2)));
                NameA = editor.Project.TopModule.FindPart(A).Name;
                NameB = editor.Project.TopModule.FindPart(B).Name;
                Xor = Must(editor.Add("adder", new GatePart { Kind = EngineGate.Xor }, new Placement(6, 1)));
                And = Must(editor.Add("adder", new GatePart { Kind = EngineGate.And }, new Placement(6, -2)));
                S = Must(editor.Add("adder", new PortPart { Name = "s", Direction = PortDirection.Output }, new Placement(10, 1)));
                C = Must(editor.Add("adder", new PortPart { Name = "c", Direction = PortDirection.Output }, new Placement(10, -2)));
                Session = new WorkbenchSession(editor.Project, Ticking(0.01));
                Session.Finish();
                Editor = new CanvasEditor(Session);
            }

            public int A, B, Xor, And, S, C;
            public string NameA, NameB;
            public WorkbenchSession Session;
            public CanvasEditor Editor;
            public readonly CanvasOverlay Overlay = new CanvasOverlay();

            public Module Module => Session.Project.TopModule;

            public CanvasTarget Pin(int part, string pin) => new CanvasTarget(CanvasTargetKind.Pin, part: part, pin: pin);

            /// <summary>A pin's place on the board, as the part stands now.</summary>
            public BoardPoint PinAt(int part, string pin) =>
                PartGeometry.Placed(Session.Project, Module.FindPart(part), Module.Layout.PlacementOf(part)).Pins.Single(p => p.Pin == pin).At;

            /// <summary>The pointer at <paramref name="board"/>, as the view says it each frame.</summary>
            public void Point(BoardPoint board, bool free = false) => Editor.Point(Session.Run.Model.Layout.Board.ToStage(board), free);

            public EditResult? Do(CanvasGesture gesture, CanvasTarget target = default, BoardPoint at = default, bool free = false) =>
                Editor.Do(new CanvasCommand(gesture, at, target: target, free: free));

            /// <summary>A wire drawn and made, pin to pin, as a drag makes it: its id.</summary>
            public int Wire(int from, string fromPin, int to, string toPin)
            {
                Do(CanvasGesture.BeginWire, Pin(from, fromPin), PinAt(from, fromPin));
                Point(PinAt(to, toPin));
                EditResult? made = Do(CanvasGesture.EndWire, Pin(to, toPin), PinAt(to, toPin));
                Assert.IsTrue(made.HasValue && made.Value.Done, made?.Refusal);
                Editor.Relaid();
                return made.Value.Id;
            }

            public string NetOf(int part, string pin) => Netlist.Of(Session.Project, Module).NetOf(new PinRef(part, pin)).Name;
        }

        /// <summary>
        /// A wire whose automatic route would run down another net's wire is drawn round it while it is drawn, and made
        /// exactly as it was drawn (Part 10e): the drawing and the document keep clear of the same things.
        /// </summary>
        [Test]
        public void AWireWhoseAutomaticRouteWouldRunAlongAnotherNet_IsDrawnRoundIt_AndMadeAsDrawn()
        {
            var b = new Sketch();
            int bWire = b.Wire(b.B, "out", b.Xor, "b");
            IReadOnlyList<BoardPoint> theirs = WireGeometry.Wires(b.Session.Project, b.Module)[bWire];
            BoardPoint from = b.PinAt(b.A, "out"), to = b.PinAt(b.And, "b");
            Assert.IsTrue(RunsAlong(WireGeometry.Orthogonal(from, Facing.Right, to, Facing.Left), theirs),
                "sanity: a's automatic route to the AND runs down b's wire to the XOR");

            b.Do(CanvasGesture.BeginWire, b.Pin(b.A, "out"), from);
            b.Point(to);
            b.Editor.Overlay(b.Overlay);
            Vec2[] shown = b.Overlay.Draft;
            EditResult? made = b.Do(CanvasGesture.EndWire, b.Pin(b.And, "b"), to);
            Assert.IsTrue(made.HasValue && made.Value.Done, made?.Refusal);
            b.Editor.Relaid();

            IReadOnlyList<BoardPoint> route = WireGeometry.Wires(b.Session.Project, b.Module)[made.Value.Id];
            Assert.IsFalse(RunsAlong(route, theirs), $"along b's wire: {string.Join(" ", route)}");
            CollectionAssert.AreEqual(shown, route.Select(p => b.Session.Run.Model.Layout.Board.ToStage(p)).ToArray(), "made where it was drawn");
        }

        /// <summary>Whether a piece of one route lies on a piece of another, along a length.</summary>
        private static bool RunsAlong(IReadOnlyList<BoardPoint> mine, IReadOnlyList<BoardPoint> theirs)
        {
            for (int i = 1; i < mine.Count; i++)
                for (int j = 1; j < theirs.Count; j++)
                {
                    BoardPoint a = mine[i - 1], b = mine[i], c = theirs[j - 1], d = theirs[j];
                    if (a.Y == b.Y && c.Y == d.Y && a.Y == c.Y &&
                        Math.Min(Math.Max(a.X, b.X), Math.Max(c.X, d.X)) - Math.Max(Math.Min(a.X, b.X), Math.Min(c.X, d.X)) > 1e-6)
                        return true;
                    if (a.X == b.X && c.X == d.X && a.X == c.X &&
                        Math.Min(Math.Max(a.Y, b.Y), Math.Max(c.Y, d.Y)) - Math.Max(Math.Min(a.Y, b.Y), Math.Min(c.Y, d.Y)) > 1e-6)
                        return true;
                }

            return false;
        }

        [Test]
        public void AWireDrawnPinToPin_IsMadeInOneStep_RoutedAutomatically_AndSelected()
        {
            var b = new Sketch();
            b.Do(CanvasGesture.BeginWire, b.Pin(b.A, "out"), b.PinAt(b.A, "out"));
            Assert.IsTrue(b.Editor.Drafting);

            b.Point(new BoardPoint(3.1, 0.4));
            b.Editor.Overlay(b.Overlay);
            Assert.IsNotNull(b.Overlay.Draft, "drawn to where the pointer is");
            Assert.IsNull(b.Overlay.DraftEnd, "over nothing it could end on");

            b.Point(b.PinAt(b.Xor, "a"));
            b.Editor.Overlay(b.Overlay);
            Assert.IsTrue(b.Overlay.DraftEnd.HasValue, "the XOR's input lit as where it would end");
            Assert.IsFalse(b.Overlay.DraftRefused);
            Vec2[] shown = b.Overlay.Draft;

            EditResult? made = b.Do(CanvasGesture.EndWire, b.Pin(b.Xor, "a"), b.PinAt(b.Xor, "a"));
            Assert.IsTrue(made.HasValue && made.Value.Done, made?.Refusal);
            Assert.IsFalse(b.Editor.Drafting);
            Wire wire = b.Module.FindWire(made.Value.Id);
            Assert.AreEqual(Endpoint.At(b.A, "out"), wire.From);
            Assert.AreEqual(Endpoint.At(b.Xor, "a"), wire.To);
            Assert.IsFalse(b.Module.Layout.Routes.ContainsKey(wire.Id), "no bends clicked: no route kept, so it follows its parts wherever they go");
            CollectionAssert.AreEqual(new[] { wire.Id }, b.Editor.Selection.Wires);
            Assert.AreEqual(b.NetOf(b.A, "out"), b.NetOf(b.Xor, "a"), "joined");
            Assert.IsTrue(b.Session.Replaying, "a wire changes what the circuit does: replayed");

            b.Editor.Relaid();
            IReadOnlyList<BoardPoint> made_ = WireGeometry.Wires(b.Session.Project, b.Module)[wire.Id];
            Vec2[] drawn = made_.Select(p => b.Session.Run.Model.Layout.Board.ToStage(p)).ToArray();
            CollectionAssert.AreEqual(shown, drawn, "made where it was shown");

            Assert.IsTrue(b.Session.Undo());
            Assert.IsEmpty(b.Session.Project.TopModule.Wires, "one step");
        }

        [Test]
        public void ABendClicked_IsKept_TheWireMadeThroughItAsShown_AndItStaysSquareWhenItsPartMoves()
        {
            var b = new Sketch();
            b.Do(CanvasGesture.BeginWire, b.Pin(b.A, "out"), b.PinAt(b.A, "out"));
            b.Do(CanvasGesture.WireBend, at: new BoardPoint(3.1, 2.4));
            CollectionAssert.AreEqual(new[] { new BoardPoint(3, 2.5) }, b.Editor.Draft.Bends, "on the quarter grid");
            b.Do(CanvasGesture.WireBend, at: new BoardPoint(3.1, 2.4));
            Assert.AreEqual(1, b.Editor.Draft.Bends.Count, "a second click on the same spot adds nothing");

            b.Point(b.PinAt(b.Xor, "a"));
            b.Editor.Overlay(b.Overlay);
            Vec2[] shown = b.Overlay.Draft;
            int wire = b.Do(CanvasGesture.EndWire, b.Pin(b.Xor, "a"), b.PinAt(b.Xor, "a")).Value.Id;

            CollectionAssert.AreEqual(new[] { new BoardPoint(3, 2.5) }, b.Module.Layout.Routes[wire].Bends, "only the bend clicked is kept");
            b.Editor.Relaid();
            Vec2[] drawn = WireGeometry.Wires(b.Session.Project, b.Module)[wire].Select(p => b.Session.Run.Model.Layout.Board.ToStage(p)).ToArray();
            CollectionAssert.AreEqual(shown, drawn, "made where it was shown");

            Assert.IsTrue(b.Session.Move(new Dictionary<int, Placement> { [b.A] = new Placement(1, 0.5) }).Done);
            IReadOnlyList<BoardPoint> moved = WireGeometry.Wires(b.Session.Project, b.Session.Project.TopModule)[wire];
            for (int i = 1; i < moved.Count; i++)
                Assert.IsTrue(moved[i].X == moved[i - 1].X || moved[i].Y == moved[i - 1].Y, $"right angles only, after a's move: {string.Join(" ", moved)}");
        }

        [Test]
        public void AWireEndedOnAWire_TapsItThere_OnTheGrid_AndJoinsItsNet()
        {
            var b = new Sketch();
            int toXor = b.Wire(b.A, "out", b.Xor, "a");
            IReadOnlyList<BoardPoint> along = WireGeometry.Wires(b.Session.Project, b.Module)[toXor];

            b.Do(CanvasGesture.BeginWire, b.Pin(b.And, "a"), b.PinAt(b.And, "a"));
            var onIt = new CanvasTarget(CanvasTargetKind.Wire, wire: toXor, segment: 0, along: 0.3);
            EditResult? made = b.Do(CanvasGesture.EndWire, onIt, new BoardPoint(2, 1));
            Assert.IsTrue(made.HasValue && made.Value.Done, made?.Refusal);

            Wire tap = b.Module.FindWire(made.Value.Id);
            Assert.AreEqual(Endpoint.OnWire(toXor), tap.To);
            BoardPoint junction = WireDraft.TapPoint(along, b.Module.Layout.Routes[tap.Id].TapAt, out double _);
            Assert.AreEqual(CanvasSnap.Snap(junction), junction, "the junction is on the quarter grid");
            Assert.AreEqual(b.NetOf(b.A, "out"), b.NetOf(b.And, "a"), "a reaches the AND through the tap");
        }

        [Test]
        public void ARefusedEnd_IsShownWhileThePointerIsOnIt_SaidWhenClicked_AndTheWireGoesOnBeingDrawn()
        {
            var b = new Sketch();
            b.Do(CanvasGesture.BeginWire, b.Pin(b.Xor, "y"), b.PinAt(b.Xor, "y"));

            b.Point(b.PinAt(b.And, "y"));
            b.Editor.Overlay(b.Overlay);
            Assert.IsTrue(b.Overlay.DraftRefused, "an output onto an output: drawn as an end it cannot take");
            StringAssert.Contains("both drive one wire", b.Editor.DraftRefusal);

            EditResult? clicked = b.Do(CanvasGesture.EndWire, b.Pin(b.And, "y"), b.PinAt(b.And, "y"));
            Assert.IsTrue(clicked.HasValue && !clicked.Value.Done);
            StringAssert.Contains("both drive one wire", b.Editor.Refusal, "said, in the document's words");
            Assert.IsTrue(b.Editor.Drafting, "and the wire is still being drawn, to end somewhere else");
            Assert.IsEmpty(b.Module.Wires);

            b.Point(b.PinAt(b.S, "in"));
            b.Editor.Overlay(b.Overlay);
            Assert.IsFalse(b.Overlay.DraftRefused, "s's pin it can take");
            Assert.IsNull(b.Editor.DraftRefusal);

            b.Do(CanvasGesture.Cancel);
            Assert.IsFalse(b.Editor.Drafting, "the right button, or Escape, lets it go");
            Assert.IsEmpty(b.Module.Wires, "making nothing");
        }

        [Test]
        public void AWireDrawnOutOfAWire_EndsOnAPin_NotOnAnotherWire_AndBackOnItsStartItGoesOn()
        {
            var b = new Sketch();
            int toXor = b.Wire(b.A, "out", b.Xor, "a");
            int toAnd = b.Wire(b.B, "out", b.And, "b");

            b.Do(CanvasGesture.BeginWire, new CanvasTarget(CanvasTargetKind.Wire, wire: toXor, along: 0.5), new BoardPoint(3, 1));
            Assert.IsTrue(b.Editor.Draft.From.IsTap);

            EditResult? onAnother = b.Do(CanvasGesture.EndWire, new CanvasTarget(CanvasTargetKind.Wire, wire: toAnd, along: 0.5));
            Assert.IsTrue(onAnother.HasValue && !onAnother.Value.Done, "a wire out of a wire onto another wire");
            StringAssert.Contains("ends on a pin", onAnother.Value.Refusal);

            Assert.IsNull(b.Do(CanvasGesture.EndWire, new CanvasTarget(CanvasTargetKind.Wire, wire: toXor, along: 0.8), new BoardPoint(4.1, 1.6)),
                "back on the wire it came out of: no wire, a bend");
            Assert.AreEqual(1, b.Editor.Draft.Bends.Count);

            EditResult? made = b.Do(CanvasGesture.EndWire, b.Pin(b.And, "a"), b.PinAt(b.And, "a"));
            Assert.IsTrue(made.HasValue && made.Value.Done, made?.Refusal);
            Assert.AreEqual(Endpoint.OnWire(toXor), b.Module.FindWire(made.Value.Id).From, "the tap is where the wire came out");
            Assert.AreEqual(b.NetOf(b.A, "out"), b.NetOf(b.And, "a"));

            b.Do(CanvasGesture.BeginWire, b.Pin(b.Xor, "y"), b.PinAt(b.Xor, "y"));
            Assert.IsNull(b.Do(CanvasGesture.EndWire, b.Pin(b.Xor, "y"), b.PinAt(b.Xor, "y")), "back on the pin it started from: nothing");
            Assert.IsTrue(b.Editor.Drafting, "a short drag that came back is a click, and the wire goes on");
        }

        [Test]
        public void AWireBeingDrawn_IsLetGo_WhenAPartIsTakenFromTheStrip_OrAnUndoTakesItsStartAway()
        {
            var b = new Sketch();
            b.Do(CanvasGesture.BeginWire, b.Pin(b.A, "out"), b.PinAt(b.A, "out"));
            b.Editor.Take(PartsCatalog.Find(b.Session.Project, b.Session.Module, "not"));
            Assert.IsFalse(b.Editor.Drafting, "a part in hand, and no wire");

            EditResult placed = b.Session.Place(PartsCatalog.Find(b.Session.Project, b.Session.Module, "not"), new Placement(3, -4));
            Assert.IsTrue(placed.Done, placed.Refusal);
            b.Editor.PutDown();
            b.Editor.Relaid();
            b.Do(CanvasGesture.BeginWire, b.Pin(placed.Id, "y"), b.PinAt(placed.Id, "y"));
            Assert.IsTrue(b.Editor.Drafting);

            Assert.IsTrue(b.Session.Undo());
            b.Editor.Relaid();
            Assert.IsFalse(b.Editor.Drafting, "the NOT it started from is gone");
        }

        [Test]
        public void TheHalfAdder_DrawnFromScratch_RunsAsOne()
        {
            var b = new Sketch();
            int aToXor = b.Wire(b.A, "out", b.Xor, "a");
            int bToXor = b.Wire(b.B, "out", b.Xor, "b");
            b.Wire(b.Xor, "y", b.S, "in");
            b.Wire(b.And, "y", b.C, "in");

            // a and b reach the AND through a tap each, as a student draws them: from its input onto the XOR's wire.
            foreach ((string pin, int wire) in new[] { ("a", aToXor), ("b", bToXor) })
            {
                b.Do(CanvasGesture.BeginWire, b.Pin(b.And, pin), b.PinAt(b.And, pin));
                EditResult? tapped = b.Do(CanvasGesture.EndWire, new CanvasTarget(CanvasTargetKind.Wire, wire: wire, along: 0.4));
                Assert.IsTrue(tapped.HasValue && tapped.Value.Done, tapped?.Refusal);
                b.Editor.Relaid();
            }

            Assert.IsEmpty(BoardRules.Check(b.Session.Project).Where(d => d.IsError), "a document with no errors");
            Assert.AreEqual(6, b.Module.Wires.Count);

            // Each input at 1 from 10 and 20, so the four combinations come in turn.
            Assert.IsTrue(b.Session.SetInput(b.NameA, 10, 30, LogicVec.FromUInt64(1, 1)).Done);
            Assert.IsTrue(b.Session.SetInput(b.NameB, 20, 40, LogicVec.FromUInt64(1, 1)).Done);
            b.Session.Finish();

            WorkbenchModel model = b.Session.Run.Model;
            string Read(string port, double time)
            {
                int row = model.Layout.Rows.ToList().FindIndex(r => r.Label == port);
                Assert.GreaterOrEqual(row, 0, $"no row for {port}");
                model.Seek(time);
                model.RowValue(row, out string text);
                return text;
            }

            Assert.AreEqual(("0", "0"), (Read("s", 5), Read("c", 5)), "0 + 0");
            Assert.AreEqual(("1", "0"), (Read("s", 15), Read("c", 15)), "1 + 0");
            Assert.AreEqual(("0", "1"), (Read("s", 25), Read("c", 25)), "1 + 1");
            Assert.AreEqual(("1", "0"), (Read("s", 35), Read("c", 35)), "0 + 1");
        }

        // -----------------------------------------------------------------
        // Wires reshaped (Part 10b-2)
        // -----------------------------------------------------------------

        private static BoardPoint P(double x, double y) => new BoardPoint(x, y);

        [Test]
        public void APieceDragged_MovesAcrossItself_AndOneAtAnEnd_KeepsItsStubBeforeItTurns()
        {
            BoardPoint[] z = { P(0, 0), P(2, 0), P(2, 2), P(4, 2) };   // across, up at x = 2, across

            CollectionAssert.AreEqual(new[] { P(0, 0), P(3, 0), P(3, 2), P(4, 2) }, WireShape.MoveSegment(z, 1, 1, 5),
                "the rise moves sideways, and only sideways");
            CollectionAssert.AreEqual(new[] { P(0, 0), P(0.5, 0), P(0.5, -1), P(2, -1), P(2, 2), P(4, 2) }, WireShape.MoveSegment(z, 0, 3, -1),
                "the first piece moves down, its end's stub kept: the wire still leaves the pin the way it faces");
            CollectionAssert.AreEqual(new[] { P(0, 0), P(0.5, 0), P(0.5, 1), P(3.5, 1), P(3.5, 0), P(4, 0) },
                WireShape.MoveSegment(new[] { P(0, 0), P(4, 0) }, 0, 0, 1), "a straight wire pulled up: a stub out of each end, and round");
            CollectionAssert.AreEqual(new[] { P(0, 0), P(3, 0), P(3, 1), P(3.5, 1), P(3.5, 2), P(4, 2) }, WireShape.MoveCorner(z, 2, 1, -1),
                "a corner moved: the rise sideways, the last piece down, and into its end by its stub");
            CollectionAssert.AreEqual(new[] { P(0, 0), P(2, 0), P(2, 1), P(3, 1), P(3, 0), P(4, 0) },
                WireShape.MoveSegment(new[] { P(0, 0), P(2, 0), P(2, 1), P(3, 1), P(3, 0), P(4, 0) }, 1, 0, 0), "a move of nothing changes nothing");
            CollectionAssert.AreEqual(new[] { P(0, 0), P(4, 0) },
                WireShape.MoveSegment(new[] { P(0, 0), P(1, 0), P(1, 1), P(3, 1), P(3, 0), P(4, 0) }, 2, 0, -1), "a step dragged back into line is gone");
            CollectionAssert.AreEqual(new[] { P(2, 0), P(2, 2) }, WireShape.Bends(z));
        }

        [Test]
        public void ADragOnASelectedWire_ReshapesIt_OnItsCornerToo_AndOnAnUnselectedOne_TapsIt()
        {
            var pointer = new CanvasPointer();
            Assert.AreEqual("BeginReshape wire 7 segment 0, ReshapeTo 5,0, EndReshape 5,0", Run(pointer, At(OnWire, selected: true), moves: 48f),
                "the piece the drag began on");
            var corner = new CanvasTarget(CanvasTargetKind.Bend, wire: 7, segment: 2);
            Assert.AreEqual("BeginReshape wire 7 corner 2, ReshapeTo 5,0, ReshapeTo 6,0, EndReshape 6,0", Run(pointer, At(corner), moves: new[] { 48f, 96f }));
            Assert.AreEqual("", Run(pointer, At(corner)), "a click on a corner does nothing");
            Assert.AreEqual("Select wire 7 segment 0", Run(pointer, At(OnWire, selected: true)), "a click on a selected wire picks it alone");
            Assert.AreEqual("BeginWire wire 7 segment 0, WireBend 5,0", Run(pointer, At(OnWire), moves: 48f), "not selected: a wire drawn out of it");
            Assert.AreEqual("EndWire wire 7 corner 2", Run(pointer, At(corner, drafting: true)), "a wire being drawn ends on a corner as on the wire");
        }

        [Test]
        public void ASelectedWiresPiece_DraggedAndLetGo_IsReroutedThroughItsNewBends_InOneStep_AndNothingReplays()
        {
            var b = new Sketch();
            int wire = b.Wire(b.A, "out", b.Xor, "a");
            b.Session.Finish();
            IReadOnlyList<BoardPoint> before = WireGeometry.Wires(b.Session.Project, b.Module)[wire];
            Assert.AreEqual(4, before.Count, $"sanity: across, a step up to the XOR's input, across: {string.Join(" ", before)}");

            b.Do(CanvasGesture.Select, new CanvasTarget(CanvasTargetKind.Wire, wire: wire, segment: 1));
            b.Editor.Overlay(b.Overlay);
            Assert.AreEqual(2, b.Overlay.Handles.Count, "its two corners are handles");

            // The step, a rise at x = mid, dragged a cell and a bit right: it lands on the grid.
            BoardPoint grab = new BoardPoint(before[1].X, (before[1].Y + before[2].Y) / 2);
            b.Do(CanvasGesture.BeginReshape, new CanvasTarget(CanvasTargetKind.Wire, wire: wire, segment: 1), grab);
            b.Editor.Do(new CanvasCommand(CanvasGesture.ReshapeTo, P(grab.X + 1.1, grab.Y + 3), grab));
            Assert.IsTrue(b.Editor.Reshaping);
            b.Editor.Overlay(b.Overlay);
            Assert.AreEqual(1, b.Overlay.GhostWires.Count, "drawn where it is going");
            Assert.IsFalse(b.Module.Layout.Routes.ContainsKey(wire), "nothing changes until it is let go");

            EditResult? made = b.Editor.Do(new CanvasCommand(CanvasGesture.EndReshape, P(grab.X + 1.1, grab.Y + 3), grab));
            Assert.IsTrue(made.HasValue && made.Value.Done, made?.Refusal);
            Assert.IsFalse(b.Editor.Reshaping);
            Assert.IsFalse(b.Session.Replaying, "a wire's shape changes nothing the circuit does");
            CollectionAssert.AreEqual(new[] { P(before[1].X + 1, before[1].Y), P(before[2].X + 1, before[2].Y) }, b.Module.Layout.Routes[wire].Bends,
                "the rise a cell on, kept as the wire's bends");

            Assert.IsTrue(b.Session.Undo());
            Assert.IsFalse(b.Session.Project.TopModule.Layout.Routes.ContainsKey(wire), "one step, back to the automatic route");
        }

        [Test]
        public void ASelectedWiresCorner_IsWhatThePointerIsOn_ADragMovesIt_AndEscapeLetsTheDragGo()
        {
            var b = new Sketch();
            int wire = b.Wire(b.A, "out", b.Xor, "a");
            b.Session.Finish();
            IReadOnlyList<BoardPoint> before = WireGeometry.Wires(b.Session.Project, b.Module)[wire];
            Vec2 onCorner = b.Session.Run.Model.Layout.Board.ToStage(before[1]);

            b.Do(CanvasGesture.Select, CanvasTarget.Nothing);   // a wire is selected as it is made
            Assert.AreEqual(CanvasTargetKind.Wire, b.Editor.TargetAt(onCorner, 1f).Kind, "not selected: the wire");
            b.Do(CanvasGesture.Select, new CanvasTarget(CanvasTargetKind.Wire, wire: wire, segment: 0));
            CanvasTarget corner = b.Editor.TargetAt(onCorner, 1f);
            Assert.AreEqual(new CanvasTarget(CanvasTargetKind.Bend, wire: wire, segment: 1), corner, "selected: its corner");

            b.Do(CanvasGesture.BeginReshape, corner, before[1]);
            b.Editor.Do(new CanvasCommand(CanvasGesture.ReshapeTo, P(before[1].X - 0.9, before[1].Y - 1.1), before[1]));
            b.Editor.Abandon();
            Assert.IsFalse(b.Editor.Reshaping, "Escape mid-drag");
            Assert.IsFalse(b.Module.Layout.Routes.ContainsKey(wire), "and nothing changed");

            b.Do(CanvasGesture.BeginReshape, corner, before[1]);
            EditResult? made = b.Editor.Do(new CanvasCommand(CanvasGesture.EndReshape, P(before[1].X - 0.9, before[1].Y - 1.1), before[1]));
            Assert.IsTrue(made.HasValue && made.Value.Done, made?.Refusal);
            IReadOnlyList<BoardPoint> after = WireGeometry.Wires(b.Session.Project, b.Session.Project.TopModule)[wire];
            CollectionAssert.Contains(after, P(before[1].X - 1, before[1].Y - 1), "the corner a cell left and down, on the grid");
            for (int i = 1; i < after.Count; i++)
                Assert.IsTrue(after[i].X == after[i - 1].X || after[i].Y == after[i - 1].Y, $"right angles only: {string.Join(" ", after)}");
            Assert.AreEqual(before[0], after[0]);
            Assert.AreEqual(before[before.Count - 1], after[after.Count - 1], "its two ends where they were");
        }

        // -----------------------------------------------------------------
        // A lead on every pin (Part 15g)
        // -----------------------------------------------------------------

        /// <summary>The pins the wire being drawn shows it may end on, on the board.</summary>
        private static List<BoardPoint> Ends(Sketch b)
        {
            b.Editor.Overlay(b.Overlay);
            return b.Overlay.PinEnds.Select(at => b.Editor.ToBoard(at)).ToList();
        }

        private static bool Has(List<BoardPoint> ends, BoardPoint p) =>
            ends.Exists(e => Math.Abs(e.X - p.X) + Math.Abs(e.Y - p.Y) < 1e-6);

        /// <summary>
        /// From an input port's output, a driver: every input no wire reaches shows it may end there, and no output -- two
        /// drivers on one wire, which the document refuses -- nor the wire's own start.
        /// </summary>
        [Test]
        public void AWireFromADriver_ShowsTheInputsNothingDrivesYet_AndNoOutput()
        {
            var b = new Sketch();
            b.Wire(b.B, "out", b.Xor, "b");
            b.Do(CanvasGesture.BeginWire, b.Pin(b.A, "out"), b.PinAt(b.A, "out"));
            b.Point(new BoardPoint(4, 4));
            List<BoardPoint> ends = Ends(b);

            foreach ((int part, string pin) in new[] { (b.Xor, "a"), (b.And, "a"), (b.And, "b"), (b.S, "in"), (b.C, "in") })
                Assert.IsTrue(Has(ends, b.PinAt(part, pin)), $"{pin} of {part}, an input nothing drives, may end it");
            Assert.IsFalse(Has(ends, b.PinAt(b.Xor, "b")), "an input a wire already drives may not");
            Assert.IsFalse(Has(ends, b.PinAt(b.B, "out")), "another driver may not");
            Assert.IsFalse(Has(ends, b.PinAt(b.Xor, "y")), "nor a gate's output");
            Assert.IsFalse(Has(ends, b.PinAt(b.A, "out")), "nor its own start");
        }

        /// <summary>
        /// An edit made while a wire is drawn moves where it may end, and the circles move with it (the 15g-3 review): they
        /// were found once a draft, and stayed where the XOR had stood after it turned.
        /// </summary>
        [Test]
        public void WhereAWireMayEnd_IsFoundAgain_AfterAnEditWhileItIsDrawn()
        {
            var b = new Sketch();
            b.Do(CanvasGesture.BeginWire, b.Pin(b.A, "out"), b.PinAt(b.A, "out"));
            b.Point(new BoardPoint(4, 4));
            BoardPoint stood = b.PinAt(b.Xor, "a");
            Assert.IsTrue(Has(Ends(b), stood), "sanity: the XOR's input, where it stands");

            Assert.IsTrue(b.Session.Turn(new[] { b.Xor }, 1).Done);
            b.Editor.Relaid();
            Assert.IsTrue(b.Editor.Drafting, "sanity: the wire is still being drawn");
            List<BoardPoint> ends = Ends(b);
            Assert.IsTrue(Has(ends, b.PinAt(b.Xor, "a")), "where the XOR's input is now");
            Assert.IsFalse(Has(ends, stood), "and not where it was");
        }

        /// <summary>
        /// A second driver may join a bus, where every driver is a tri-state gate's (Part 14f): from one BUFIF's output, the
        /// other's shows it may end there, and a plain gate's output does not (the 15g-3 review: the guide left the bus out).
        /// </summary>
        [Test]
        public void AWireFromATriStateOutput_ShowsAnotherTriStateOutput_AndNotAPlainOne()
        {
            var b = new Sketch();
            var editor = new BoardEditor(b.Session.Project);
            EditResult one = editor.Add("adder", new GatePart { Kind = EngineGate.Bufif1 }, new Placement(6, 5));
            EditResult two = editor.Add("adder", new GatePart { Kind = EngineGate.Bufif1 }, new Placement(6, 8));
            Assert.IsTrue(one.Done && two.Done, one.Refusal ?? two.Refusal);
            var session = new WorkbenchSession(editor.Project, Ticking(0.01));
            session.Finish();
            var canvas = new CanvasEditor(session);
            var overlay = new CanvasOverlay();
            BoardPoint PinAt(int part, string pin) =>
                PartGeometry.Placed(session.Project, session.Project.TopModule.FindPart(part), session.Project.TopModule.Layout.PlacementOf(part))
                    .Pins.Single(p => p.Pin == pin).At;

            canvas.Do(new CanvasCommand(CanvasGesture.BeginWire, PinAt(one.Id, "y"), target: new CanvasTarget(CanvasTargetKind.Pin, part: one.Id, pin: "y")));
            canvas.Point(session.Run.Model.Layout.Board.ToStage(new BoardPoint(12, 12)), free: false);
            canvas.Overlay(overlay);
            List<BoardPoint> ends = overlay.PinEnds.Select(at => canvas.ToBoard(at)).ToList();

            Assert.IsTrue(Has(ends, PinAt(two.Id, "y")), "the other tri-state output, a second driver on a bus");
            Assert.IsFalse(Has(ends, PinAt(b.Xor, "y")), "not a plain gate's output");
            Assert.IsTrue(Has(ends, PinAt(b.Xor, "a")), "and an input, as from any driver");
        }

        /// <summary>
        /// The pin under the pointer, asked again only when the pointer, the zoom or the drawing changes (the 15g-3 review):
        /// the same point after an edit that moved the pin away from it is on nothing, and the view can say it is on none.
        /// </summary>
        [Test]
        public void ThePinUnderThePointer_IsAskedAgainWhenTheDrawingChanges_AndNotWhereTheViewSaysNo()
        {
            var b = new Sketch();
            BoardPoint on = b.PinAt(b.A, "out");
            b.Point(on);
            Assert.AreEqual(new CanvasTarget(CanvasTargetKind.Pin, part: b.A, pin: "out"), b.Editor.PinOver);
            b.Editor.Overlay(b.Overlay);
            Assert.IsNotNull(b.Overlay.PinLit, "its circle lit");

            b.Point(on);
            Assert.AreEqual(CanvasTargetKind.Pin, b.Editor.PinOver.Kind, "still there, asked or not");

            Assert.IsTrue(b.Session.Turn(new[] { b.A }, 1).Done);
            b.Editor.Relaid();
            b.Point(on);
            Assert.AreEqual(CanvasTargetKind.Nothing, b.Editor.PinOver.Kind, "the pin turned away from under the pointer");

            BoardPoint moved = b.PinAt(b.A, "out");
            b.Editor.Point(b.Session.Run.Model.Layout.Board.ToStage(moved), free: false, zoom: 1f, pins: false);
            Assert.AreEqual(CanvasTargetKind.Nothing, b.Editor.PinOver.Kind, "over FIT, the minimap or a pan, the view says none");
        }
    }
}
