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
    /// The canvas's editing logic (Part 10a), all of it Unity-free: what a point is on, the grid and the
    /// alignment guides, the selection, every gesture from a scripted list of pointer events, the parts strip's
    /// catalogue, and the session's edits -- drawn at once, replayed only when they change the circuit.
    /// </summary>
    public class CanvasTests
    {
        private static Func<double> Ticking(double step)
        {
            double now = 0;
            return () => { double read = now; now += step; return read; };
        }

        /// <summary>a and b into an AND at (4, 0), and its output to y.</summary>
        private static (Project Project, int A, int B, int And, int Y, int ToY) Small()
        {
            var editor = new BoardEditor(Project.New("sketch"));
            int Must(EditResult r) { Assert.IsTrue(r.Done, r.Refusal); return r.Id; }
            int a = Must(editor.Add("sketch", new PortPart { Name = "a" }, new Placement(1, 1)));
            int b = Must(editor.Add("sketch", new PortPart { Name = "b" }, new Placement(1, -1)));
            int and = Must(editor.Add("sketch", new GatePart { Kind = EngineGate.And }, new Placement(4, 0)));
            int y = Must(editor.Add("sketch", new PortPart { Name = "y", Direction = PortDirection.Output }, new Placement(7, 0)));
            Must(editor.Connect("sketch", Endpoint.At(a, "out"), Endpoint.At(and, "a")));
            Must(editor.Connect("sketch", Endpoint.At(b, "out"), Endpoint.At(and, "b")));
            int toY = Must(editor.Connect("sketch", Endpoint.At(and, "y"), Endpoint.At(y, "in")));
            return (editor.Project, a, b, and, y, toY);
        }

        // -----------------------------------------------------------------
        // What a point is on
        // -----------------------------------------------------------------

        [Test]
        public void APoint_IsOnAPinBeforeABody_ABodyBeforeAWire_OrOnNothing()
        {
            (Project project, int _, int _, int and, int _, int toY) = Small();
            var hit = new CanvasHit(project, project.TopModule);

            CanvasTarget pin = hit.At(new BoardPoint(3.03, 0.25), 0.15);
            Assert.AreEqual(CanvasTargetKind.Pin, pin.Kind, pin.ToString());
            Assert.AreEqual((and, "a"), (pin.Part, pin.Pin), "the AND's first input, a cell left of its middle and a quarter up");

            Assert.AreEqual(new CanvasTarget(CanvasTargetKind.Part, part: and), hit.At(new BoardPoint(4.1, -0.1), 0.15), "inside its outline");

            CanvasTarget wire = hit.At(new BoardPoint(5.75, 0.05), 0.15);
            Assert.AreEqual(CanvasTargetKind.Wire, wire.Kind, wire.ToString());
            Assert.AreEqual(toY, wire.Wire);
            Assert.AreEqual(0.5, wire.Along, 0.05, "half-way along it, from the AND's y to y's pin");

            Assert.AreEqual(CanvasTargetKind.Nothing, hit.At(new BoardPoint(20, 20), 0.15).Kind);
        }

        [Test]
        public void ABox_TakesEveryPartItMeets_AndEveryWireWithAPieceInIt()
        {
            (Project project, int _, int _, int and, int _, int toY) = Small();
            var hit = new CanvasHit(project, project.TopModule);
            var parts = new List<int>();
            var wires = new List<int>();

            hit.In(3.5, -1, 4.5, 1, parts, wires);
            CollectionAssert.AreEqual(new[] { and }, parts);
            Assert.IsEmpty(wires, "the AND's own wires stop at its pins, outside the box");

            parts.Clear();
            hit.In(6, 0.2, 5.5, -0.2, parts, wires);   // drawn right to left, bottom to top
            Assert.IsEmpty(parts);
            CollectionAssert.AreEqual(new[] { toY }, wires);
        }

        [Test]
        public void TheFrame_TakesAStagePointBackToTheBoard()
        {
            var frame = new SchematicLayout.Frame(48f, -2.5, 3.25, 30f);
            foreach (BoardPoint p in new[] { new BoardPoint(0, 0), new BoardPoint(-2.5, 3.25), new BoardPoint(7.75, -4) })
            {
                BoardPoint back = frame.ToBoard(frame.ToStage(p));
                Assert.AreEqual(p.X, back.X, 1e-5);
                Assert.AreEqual(p.Y, back.Y, 1e-5);
            }

            var moved = new SchematicLayout.Frame(48f, -3.5, 4.25, 30f);
            Assert.AreEqual(new Vec2(48f, 48f), moved.ShiftFrom(frame), "the board's corner a cell further out: everything a cell further in");
        }

        // -----------------------------------------------------------------
        // The grid and the guides
        // -----------------------------------------------------------------

        [Test]
        public void AMove_LandsTheGrabbedPartOnTheGrid_AndAltMovesItExactly()
        {
            Assert.AreEqual(new BoardPoint(1.25, -0.5), CanvasSnap.Snap(new BoardPoint(1.13, -0.38)));
            Assert.AreEqual((0.25, 0.0), CanvasSnap.Offset(new BoardPoint(1, 0), 0.3, 0.1, free: false));
            Assert.AreEqual((0.375, 0.0), CanvasSnap.Offset(new BoardPoint(1.125, 0), 0.3, 0.1, free: false),
                "a part placed off the grid lands on it, and the rest of the selection keeps its place beside it");
            Assert.AreEqual((0.3, 0.1), CanvasSnap.Offset(new BoardPoint(1, 0), 0.3, 0.1, free: true));
        }

        [Test]
        public void AFreeMove_IsCaughtToALineUp_AndAMoveOnTheGridOnlyShowsIt()
        {
            Project project = Project.New("g");
            PartOutline Placed(Part part, double x, double y) => PartGeometry.Placed(project, part, new Placement(x, y));
            var not = new GatePart { Kind = EngineGate.Not, Inputs = 1 };
            var and = new GatePart { Kind = EngineGate.And };
            var guides = new List<Guide>();

            // The AND placed freely, a little high: a free move of the NOT is caught to its middle.
            (double dx, double dy) = AlignmentGuides.Align(new[] { Placed(not, 0, 0) }, 0, 0.05, new[] { Placed(and, 4, 0.13) }, catchUp: true, guides);
            Assert.AreEqual(0.0, dx, 1e-9);
            Assert.AreEqual(0.13, dy, 1e-9);
            Guide across = guides.Single(g => !g.Vertical);
            Assert.AreEqual(0.13, across.At, 1e-9);

            // On the grid, the same move is not caught -- and nothing lines up.
            (_, dy) = AlignmentGuides.Align(new[] { Placed(not, 0, 0) }, 0, 0, new[] { Placed(and, 4, 0.13) }, catchUp: false, guides);
            Assert.AreEqual(0.0, dy);
            Assert.IsEmpty(guides);

            // An AND on the grid in line: one guide, through the NOT's pins and middle and the AND's y and middle.
            AlignmentGuides.Align(new[] { Placed(not, 0, 0) }, 0, 0, new[] { Placed(and, 4, 0) }, catchUp: false, guides);
            Guide line = guides.Single();
            Assert.IsFalse(line.Vertical);
            Assert.AreEqual((0.0, -1.0, 5.0), (line.At, line.From, line.To), "joined into one line, from the NOT's input to the AND's output");
        }

        // -----------------------------------------------------------------
        // The selection
        // -----------------------------------------------------------------

        [Test]
        public void AClickPicks_ShiftAdds_CtrlToggles_ABoxReplacesOrAdds_AndWhatAnEditTookIsLetGo()
        {
            (Project project, int a, int b, int and, int _, int toY) = Small();
            var selection = new CanvasSelection();
            CanvasTarget Part(int id) => new CanvasTarget(CanvasTargetKind.Part, part: id);

            selection.Pick(Part(a), extend: false, toggle: false);
            selection.Pick(Part(b), extend: true, toggle: false);
            CollectionAssert.AreEquivalent(new[] { a, b }, selection.Parts);
            selection.Pick(Part(a), extend: false, toggle: true);
            CollectionAssert.AreEquivalent(new[] { b }, selection.Parts, "Ctrl takes it out");
            selection.Pick(new CanvasTarget(CanvasTargetKind.Pin, part: and, pin: "a"), extend: false, toggle: false);
            CollectionAssert.AreEquivalent(new[] { and }, selection.Parts, "a pin picks its part");

            selection.Box(new[] { a }, new[] { toY }, extend: true);
            CollectionAssert.AreEquivalent(new[] { and, a }, selection.Parts);
            CollectionAssert.AreEquivalent(new[] { toY }, selection.Wires);
            selection.Pick(CanvasTarget.Nothing, extend: true, toggle: false);
            Assert.IsFalse(selection.IsEmpty, "a click on nothing with Shift keeps the selection");
            selection.Box(new[] { b }, new int[0], extend: false);
            CollectionAssert.AreEquivalent(new[] { b }, selection.Parts);
            Assert.IsEmpty(selection.Wires);

            selection.All(project.TopModule);
            Assert.AreEqual(4, selection.Parts.Count);
            int version = selection.Version;
            var editor = new BoardEditor(project);
            Assert.IsTrue(editor.Remove("sketch", and).Done);
            selection.Keep(editor.Project.TopModule);
            Assert.IsFalse(selection.HasPart(and));
            Assert.IsFalse(selection.HasWire(toY), "its wires went with it");
            Assert.Greater(selection.Version, version);
        }

        // -----------------------------------------------------------------
        // Gestures
        // -----------------------------------------------------------------

        private static CanvasPress At(CanvasTarget target, int button = 0, bool selected = false, bool hand = false, bool shift = false, bool ctrl = false,
            bool alt = false, bool space = false, bool drafting = false) => new CanvasPress
        {
            Button = button, Window = new Vec2(100, 100), Board = new BoardPoint(4, 0), Target = target, OnSelected = selected, InHand = hand,
            Shift = shift, Ctrl = ctrl, Alt = alt, Space = space, Drafting = drafting,
        };

        /// <summary>A press, moves to <paramref name="moves"/> pixels right of it (a cell each 48), and the release -- over <paramref name="release"/> -- as the commands they made.</summary>
        private static string Run(CanvasPointer pointer, CanvasPress press, bool alt = false, CanvasTarget release = default, params float[] moves)
        {
            var commands = new List<CanvasCommand>();
            pointer.Press(press, commands);
            float last = 0;
            foreach (float px in moves)
            {
                last = px;
                pointer.Move(press.Window + new Vec2(px, 0), new BoardPoint(4 + px / 48.0, 0), alt, commands);
            }

            pointer.Release(new BoardPoint(4 + last / 48.0, 0), release, alt, commands);
            Assert.IsFalse(pointer.Held, "let go");
            return string.Join(", ", commands);
        }

        private static readonly CanvasTarget OnAnd = new CanvasTarget(CanvasTargetKind.Part, part: 3);

        [Test]
        public void APressOnAPart_SelectsIt_AndADragPastAFewPixelsMovesTheSelection()
        {
            var pointer = new CanvasPointer();
            Assert.AreEqual("Select part 3", Run(pointer, At(OnAnd), moves: 2f), "a hand that shakes still clicks");
            Assert.AreEqual("Select part 3, BeginMove 4,0, MoveTo 5,0, MoveTo 6,0, EndMove 6,0", Run(pointer, At(OnAnd), moves: new[] { 48f, 96f }));
            Assert.AreEqual("Select part 3 +, BeginMove 4,0, MoveTo 5,0 free, EndMove 5,0 free", Run(pointer, At(OnAnd, shift: true), alt: true, moves: 48f),
                "Shift adds it; Alt held places freely");
        }

        [Test]
        public void APressOnASelectedPart_KeepsTheSelectionToDrag_OrPicksItAloneOnRelease()
        {
            var pointer = new CanvasPointer();
            Assert.AreEqual("BeginMove 4,0, MoveTo 5,0, EndMove 5,0", Run(pointer, At(OnAnd, selected: true), moves: 48f), "the whole selection moves");
            Assert.AreEqual("Select part 3", Run(pointer, At(OnAnd, selected: true)), "a click on it alone, on release");
            Assert.AreEqual("Select part 3 ^", Run(pointer, At(OnAnd, selected: true, ctrl: true), moves: 48f), "Ctrl takes it out, and moves nothing");
        }

        [Test]
        public void ADragOnNothing_DrawsABox_AndAClickOnNothingClears()
        {
            var pointer = new CanvasPointer();
            Assert.AreEqual("Box 4,0..5,0, Box 4,0..6,0, EndBox 4,0..6,0", Run(pointer, At(CanvasTarget.Nothing), moves: new[] { 48f, 96f }));
            Assert.AreEqual("Box 4,0..5,0 +, EndBox 4,0..5,0 +", Run(pointer, At(CanvasTarget.Nothing, shift: true), moves: 48f));
            Assert.AreEqual("Select nothing", Run(pointer, At(CanvasTarget.Nothing)));
        }

        [Test]
        public void TheMiddleButton_OrSpaceAndTheLeft_Pans_WhereverThePressIs()
        {
            var pointer = new CanvasPointer();
            Assert.AreEqual("Pan 10,0, Pan 5,0", Run(pointer, At(OnAnd, button: 2), moves: new[] { 10f, 15f }));
            Assert.AreEqual("Pan 48,0", Run(pointer, At(CanvasTarget.Nothing, space: true), moves: 48f));
        }

        [Test]
        public void WithAPartInHand_TheLeftPlacesIt_AndTheRightPutsItDown()
        {
            var pointer = new CanvasPointer();
            Assert.AreEqual("Place 4,0", Run(pointer, At(OnAnd, hand: true)), "placed even over a part: the document says if it may");
            Assert.AreEqual("Place 4,0 free", Run(pointer, At(CanvasTarget.Nothing, hand: true, alt: true)));
            Assert.AreEqual("Cancel", Run(pointer, At(CanvasTarget.Nothing, button: 1, hand: true)));
        }

        [Test]
        public void APressOnAWire_SelectsIt_AndAltAddsItsSignal()
        {
            var pointer = new CanvasPointer();
            var wire = new CanvasTarget(CanvasTargetKind.Wire, wire: 7, segment: 0);
            Assert.AreEqual("Select wire 7 segment 0", Run(pointer, At(wire)));
            Assert.AreEqual("AddWireSignal wire 7 segment 0", Run(pointer, At(wire, alt: true)));
        }

        // -----------------------------------------------------------------
        // The parts strip
        // -----------------------------------------------------------------

        [Test]
        public void TheStrip_OffersEveryPart_AndABlockOfEveryOtherModule()
        {
            Project adder = WorkbenchSamples.FullAdder();
            string[] keys = PartsCatalog.For(adder, adder.Top).Select(e => e.Key).ToArray();

            CollectionAssert.IsSubsetOf(new[] { "input", "output", "clock", "reset", "constant", "and", "or", "xor", "nand", "nor", "xnor", "not", "buf",
                "bufif1", "mux", "register", "splitter", "joiner", "lamp" }, keys);
            CollectionAssert.Contains(keys, "block:half_adder");
            CollectionAssert.DoesNotContain(keys, "block:full_adder", "not a block of itself");

            Project counter = WorkbenchSamples.Counter();
            var gate = (GatePart)PartsCatalog.Make(counter, counter.Top, PartsCatalog.Find(counter, counter.Top, "not"));
            Assert.AreEqual((1, counter.Settings.DefaultGateDelay, counter.Settings.DefaultGateDelay), (gate.Inputs, gate.Rise, gate.Fall),
                "one input, and the project's delay, rise and fall");
        }

        [Test]
        public void ThePanel_FilesEachEntry_UnderPortsGatesMoreOrBlocks_InTheStripsOrder()
        {
            Project adder = WorkbenchSamples.FullAdder();
            CatalogGroup GroupOf(string key) => PartsCatalog.Find(adder, adder.Top, key).Group;

            foreach (string key in new[] { "input", "output", "clock", "reset", "constant" })
                Assert.AreEqual(CatalogGroup.Ports, GroupOf(key), key);
            foreach (string key in new[] { "and", "or", "xor", "nand", "nor", "xnor", "not", "buf", "bufif1" })
                Assert.AreEqual(CatalogGroup.Gates, GroupOf(key), key);
            foreach (string key in new[] { "mux", "register", "splitter", "joiner", "lamp" })
                Assert.AreEqual(CatalogGroup.More, GroupOf(key), key);
            Assert.AreEqual(CatalogGroup.Blocks, GroupOf("block:half_adder"));

            CatalogGroup[] groups = PartsCatalog.For(adder, adder.Top).Select(e => e.Group).ToArray();
            for (int i = 1; i < groups.Length; i++)
                Assert.GreaterOrEqual(groups[i], groups[i - 1], "each heading's entries come together, in the headings' order");
        }

        [Test]
        public void AnInputPlaced_IsGiven0_AClockItsClock_AndAResetItsPulse_EachInOneStep()
        {
            var editor = new BoardEditor(Project.New("c"));
            Project blank = editor.Project;
            CatalogEntry Entry(string key) => PartsCatalog.Find(editor.Project, "c", key);

            Assert.IsTrue(PartsCatalog.Place(editor, "c", Entry("input"), new Placement(1, 0)).Done);
            Assert.IsTrue(PartsCatalog.Place(editor, "c", Entry("clock"), new Placement(1, -1)).Done);
            Assert.IsTrue(PartsCatalog.Place(editor, "c", Entry("reset"), new Placement(1, -2)).Done);

            Stimulus stimulus = editor.Project.Stimulus;
            Track a = stimulus.Tracks.Single(t => t.Port == "a");
            Assert.AreEqual("0@0", string.Join(" ", a.Steps.Select(s => s.Value.Format(Radix.Binary) + "@" + s.Time)));
            ClockTrack clock = stimulus.Clocks.Single();
            Assert.AreEqual(("clk", PartsCatalog.ClockPeriod), (clock.Port, clock.Period));
            Track rst = stimulus.Tracks.Single(t => t.Port == "rst");
            Assert.AreEqual("1@0 0@" + PartsCatalog.ResetUntil, string.Join(" ", rst.Steps.Select(s => s.Value.Format(Radix.Binary) + "@" + s.Time)));

            Assert.IsTrue(editor.Undo() && editor.Undo() && editor.Undo());
            Assert.AreSame(blank, editor.Project, "three placements, three steps, each part with what drives it");

            // Inside a module that is not the top, an input is a port of a block: nothing drives it.
            Assert.IsTrue(editor.AddModule("inner").Done);
            Assert.IsTrue(PartsCatalog.Place(editor, "inner", PartsCatalog.Find(editor.Project, "inner", "input"), new Placement(0, 0)).Done);
            Assert.IsNull(editor.Project.Stimulus);
        }

        // -----------------------------------------------------------------
        // The session's edits
        // -----------------------------------------------------------------

        [Test]
        public void AMove_IsDrawnAgainWithoutAReplay_AndAPlace_IsDrawnAtOnceAndReplayed()
        {
            var session = new WorkbenchSession(WorkbenchSamples.FullAdder(), Ticking(0.01));
            session.Finish();
            int layouts = session.LayoutVersion;
            int or = session.Project.TopModule.Parts.Single(p => p is GatePart).Id;

            Assert.IsTrue(session.Move(new Dictionary<int, Placement> { [or] = new Placement(14, -4) }).Done);
            Assert.AreEqual(layouts + 1, session.LayoutVersion);
            Assert.IsFalse(session.Replaying, "moving a part changes nothing the circuit does");
            Assert.AreEqual(new Placement(14, -4), session.Run.Model.Layout.Parts.Single(p => p.Part.Id == or).Placement, "the run shown is drawn with it moved");

            RunSession before = session.Run;
            int drawn = session.Run.Model.Layout.Parts.Count;
            Assert.IsTrue(session.Place(PartsCatalog.Find(session.Project, session.Module, "not"), new Placement(3, 3)).Done);
            Assert.IsTrue(session.Replaying, "a part placed is a circuit changed");
            Assert.AreSame(before, session.Run, "the run there was is still the one shown ...");
            Assert.AreEqual(drawn + 1, session.Run.Model.Layout.Parts.Count, "... drawn with the new gate at once");
            Assert.AreEqual(layouts + 2, session.LayoutVersion);

            session.Finish();
            Assert.AreNotSame(before, session.Run, "and the replay takes over");
            Assert.IsTrue(session.Undo());
            Assert.AreEqual(drawn, session.Run.Model.Layout.Parts.Count, "an undo is drawn at once too");
        }

        [Test]
        public void ASelectionTurned_TurnsAboutItsMiddle_AndMirrored_MirrorsOnScreenHoweverItWasTurned()
        {
            var editor = new BoardEditor(Project.New("t"));
            int not = editor.Add("t", new GatePart { Kind = EngineGate.Not, Inputs = 1 }, new Placement(0, 0)).Id;
            int buf = editor.Add("t", new GatePart { Kind = EngineGate.Buf, Inputs = 1 }, new Placement(2, 0)).Id;
            var session = new WorkbenchSession(editor.Project, Ticking(0.01));
            Placement Of(int part) => session.Project.TopModule.Layout.PlacementOf(part);

            Assert.IsTrue(session.Turn(new[] { not, buf }, 1).Done);
            Assert.AreEqual(new Placement(new BoardPoint(1, -1), 1), Of(not), "a quarter turn counter-clockwise about (1, 0)");
            Assert.AreEqual(new Placement(new BoardPoint(1, 1), 1), Of(buf));
            Assert.IsFalse(session.Replaying);

            Assert.IsTrue(session.Turn(new[] { not }, 0, mirror: true).Done);
            Assert.AreEqual(new Placement(new BoardPoint(1, -1), 3, true), Of(not), "turned a quarter, mirrored left to right on screen: three quarters, mirrored");
            Point input = PartGeometry.Placed(session.Project, session.Project.TopModule.FindPart(not), Of(not)).Pins.Single(p => p.Pin == "a").At;
            Assert.AreEqual((1.0, -2.0), (input.X, input.Y), "its input still at the bottom: a mirror left to right does not move a vertical part's ends");

            Assert.IsTrue(session.Turn(new[] { not, buf }, -1).Done);
            Assert.AreEqual(2, Of(not).QuarterTurns);
            Assert.IsFalse(session.Turn(new int[0], 1).Done, "nothing selected, nothing turned");
        }

        // -----------------------------------------------------------------
        // The editor between them
        // -----------------------------------------------------------------

        private static (WorkbenchSession Session, CanvasEditor Editor, int A, int B, int And, int Y) Editing()
        {
            (Project project, int a, int b, int and, int y, int _) = Small();
            var session = new WorkbenchSession(project, Ticking(0.01));
            session.Finish();
            return (session, new CanvasEditor(session), a, b, and, y);
        }

        [Test]
        public void ADragOfTheSelection_IsDrawnWhereItIsGoing_AndMovedOnce_OnRelease()
        {
            (WorkbenchSession session, CanvasEditor editor, int _, int _, int and, int _) = Editing();
            var overlay = new CanvasOverlay();
            Project before = session.Project;

            editor.Do(new CanvasCommand(CanvasGesture.Select, target: new CanvasTarget(CanvasTargetKind.Part, part: and)));
            editor.Do(new CanvasCommand(CanvasGesture.BeginMove, from: new BoardPoint(4, 0)));
            editor.Do(new CanvasCommand(CanvasGesture.MoveTo, new BoardPoint(5.1, 0.04), new BoardPoint(4, 0)));
            editor.Overlay(overlay);

            Assert.AreSame(before, session.Project, "nothing is moved until the drag is let go");
            Assert.AreEqual(1, overlay.Ghosts.Count);
            Assert.AreEqual(overlay.Selected[0].xMin + DocumentSchematic.Cell, overlay.Ghosts[0].xMin, 1e-3f, "drawn a cell on, on the grid");
            Assert.AreEqual(3, overlay.GhostWires.Count, "and its three wires follow it");

            EditResult? moved = editor.Do(new CanvasCommand(CanvasGesture.EndMove, new BoardPoint(5.1, 0.04), new BoardPoint(4, 0)));
            Assert.IsTrue(moved.HasValue && moved.Value.Done);
            Assert.AreEqual(new Placement(5, 0), session.Project.TopModule.Layout.PlacementOf(and));
            Assert.IsFalse(editor.Moving);
            Assert.IsTrue(session.Undo());
            Assert.AreEqual(new Placement(4, 0), session.Project.TopModule.Layout.PlacementOf(and), "one step");
        }

        [Test]
        public void ABoxLetGo_SelectsWhatItTouches_AndIsDrawnWhileItIsDrawn()
        {
            (WorkbenchSession _, CanvasEditor editor, int a, int b, int _, int _) = Editing();
            var overlay = new CanvasOverlay();

            editor.Do(new CanvasCommand(CanvasGesture.Box, new BoardPoint(1.5, 1.5), new BoardPoint(0, -1.5)));
            editor.Overlay(overlay);
            Assert.IsTrue(overlay.Box.HasValue);

            editor.Do(new CanvasCommand(CanvasGesture.EndBox, new BoardPoint(1.5, 1.5), new BoardPoint(0, -1.5)));
            CollectionAssert.AreEquivalent(new[] { a, b }, editor.Selection.Parts);
            editor.Overlay(overlay);
            Assert.IsFalse(overlay.Box.HasValue);
            Assert.AreEqual(2, overlay.Selected.Count);
        }

        [Test]
        public void APartInHand_IsDrawnWhereItWouldLand_AndPlacedOnTheGrid_AndShiftKeepsItInHand()
        {
            (WorkbenchSession session, CanvasEditor editor, int _, int _, int _, int _) = Editing();
            var overlay = new CanvasOverlay();
            CatalogEntry and = PartsCatalog.Find(session.Project, session.Module, "and");
            SchematicLayout.Frame frame = session.Run.Model.Layout.Board;

            editor.Take(and);
            editor.Point(frame.ToStage(new BoardPoint(2.1, 3.1)), free: false);
            editor.Overlay(overlay);
            Assert.IsTrue(overlay.Hand.HasValue, "the gate, drawn where a click would place it");

            EditResult? placed = editor.Do(new CanvasCommand(CanvasGesture.Place, new BoardPoint(2.1, 3.1), extend: true));
            Assert.IsTrue(placed.HasValue && placed.Value.Done);
            Assert.AreEqual(new Placement(2, 3), session.Project.TopModule.Layout.PlacementOf(placed.Value.Id), "on the grid");
            CollectionAssert.AreEqual(new[] { placed.Value.Id }, editor.Selection.Parts, "and selected");
            Assert.AreSame(and, editor.InHand, "Shift held: still in hand, for the next");

            placed = editor.Do(new CanvasCommand(CanvasGesture.Place, new BoardPoint(6.1, 3.1), free: true));
            Assert.AreEqual(new Placement(6.1, 3.1), session.Project.TopModule.Layout.PlacementOf(placed.Value.Id), "Alt: exactly there");
            Assert.IsNull(editor.InHand, "placed, and put down");

            editor.Take(and);
            editor.Do(new CanvasCommand(CanvasGesture.Cancel));
            Assert.IsNull(editor.InHand);
        }

        [Test]
        public void TheSelection_IsTakenAway_Turned_AndMirrored_AndARefusalIsKeptForTheViewToSay()
        {
            (WorkbenchSession session, CanvasEditor editor, int a, int b, int and, int _) = Editing();

            Assert.IsFalse(editor.Delete().Done);
            StringAssert.Contains("Nothing is selected", editor.Refusal);

            editor.Do(new CanvasCommand(CanvasGesture.Select, target: new CanvasTarget(CanvasTargetKind.Part, part: and)));
            Assert.IsTrue(editor.Turn(1).Done);
            Assert.AreEqual(1, session.Project.TopModule.Layout.PlacementOf(and).QuarterTurns);
            Assert.IsTrue(editor.Mirror().Done);
            Assert.IsTrue(session.Project.TopModule.Layout.PlacementOf(and).Flipped);
            Assert.IsNull(editor.Refusal);

            editor.SelectAll();
            Assert.AreEqual(4, editor.Selection.Parts.Count);
            Assert.IsTrue(editor.Delete().Done);
            Assert.IsEmpty(session.Project.TopModule.Parts);
            Assert.IsTrue(editor.Selection.IsEmpty);
            Assert.IsTrue(session.Undo());
            editor.Relaid();
            Assert.IsNotNull(session.Project.TopModule.FindPart(a), "one step brings all four back");
            Assert.IsNotNull(session.Project.TopModule.FindPart(b));
        }

        /// <summary>
        /// The last block of a module deleted leaves the module, which is the player's work, and says so and how to take it
        /// out; taken out, it is gone from the CODE tab's files and from PARTS, and undo puts it back (the 14g report: code1
        /// stayed in both after its block was deleted, with nothing in the workbench to take it out).
        /// </summary>
        [Test]
        public void TheLastBlockOfAModule_Deleted_SaysTheModuleStays_AndItsModuleCanThenBeTakenOut()
        {
            (WorkbenchSession session, CanvasEditor editor, int _, int _, int _, int _) = Editing();
            Assert.IsTrue(session.AddWrittenModule("code1").Done);
            CatalogEntry entry = PartsCatalog.For(session.Project, session.Module).Single(e => e.Module == "code1");
            EditResult placed = session.Place(entry, new Placement(8, 4));
            Assert.IsTrue(placed.Done);
            int second = session.Place(entry, new Placement(8, 7)).Id;
            editor.Relaid();

            editor.Do(new CanvasCommand(CanvasGesture.Select, target: new CanvasTarget(CanvasTargetKind.Part, part: placed.Id)));
            Assert.IsTrue(editor.Delete().Done);
            Assert.IsNull(editor.Note, "another block of code1 is still placed");
            Assert.IsNotNull(session.WhyNotRemoveModule("code1"));

            editor.Do(new CanvasCommand(CanvasGesture.Select, target: new CanvasTarget(CanvasTargetKind.Part, part: second)));
            Assert.IsTrue(editor.Delete().Done);
            Assert.AreEqual("code1 stays in the project, as its code does: right-click it in PARTS to delete it.", editor.Note);
            Assert.AreNotEqual(-1, session.Listing.FileOf("code1"), "sanity: still printed, as Ahmad found");

            Assert.IsNull(session.WhyNotRemoveModule("code1"));
            Assert.IsTrue(session.RemoveModule("code1").Done);
            Assert.AreEqual(-1, session.Listing.FileOf("code1"), "gone from the CODE tab's files");
            Assert.IsFalse(PartsCatalog.For(session.Project, session.Module).Any(e => e.Module == "code1"), "and from PARTS");

            Assert.IsTrue(session.Undo());
            Assert.AreNotEqual(-1, session.Listing.FileOf("code1"), "one step: undo puts it back");
        }

        [Test]
        public void ABlankDocument_OpensAndRuns_AndTakesItsFirstPart()
        {
            var session = new WorkbenchSession(Project.New("my_circuit"), Ticking(0.01));
            session.Finish();
            Assert.IsEmpty(session.Run.Model.Layout.Parts);

            Assert.IsTrue(session.Place(PartsCatalog.Find(session.Project, session.Module, "input"), new Placement(0, 0)).Done);
            session.Finish();
            Assert.AreEqual(1, session.Run.Model.Layout.Ports.Count);
            Assert.AreEqual("0", session.Run.Model.Session.History.ValueAt(
                session.Run.Model.Session.Design.Find(session.Run.Model.Layout.Ports[0].Signal), 1).Format(Radix.Binary), "the input placed reads 0 at once");
        }
    }
}
