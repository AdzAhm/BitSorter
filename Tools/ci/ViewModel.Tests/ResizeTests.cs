using System;
using System.Collections.Generic;
using System.Linq;
using BitSorter.Board;
using NUnit.Framework;
using BoardPoint = BitSorter.Board.Point;

namespace BitSorter.ViewModel.Tests
{
    /// <summary>
    /// A selected block resized by the handles round it (Part 16, asked 2026-11-02): which sides each handle moves, by half
    /// cells, never below natural nor above the cap; the handles only round one block; the drag drawn and made once, on
    /// release; refused where a move is; the pointer's gestures; and RESET SIZE -- on a drawn circuit and the drawing of code.
    /// </summary>
    public class ResizeTests
    {
        private static Func<double> Ticking(double step)
        {
            double now = 0;
            return () => now += step;
        }

        /// <summary>The full adder sample, its h1 a block of half_adder at (5, 0), with h2 beside it and an OR gate.</summary>
        private sealed class Bench
        {
            public Bench()
            {
                Session = new WorkbenchSession(WorkbenchSamples.FullAdder(), Ticking(0.01));
                Session.Finish();
                Editor = new CanvasEditor(Session);
                H1 = Module.Parts.Single(p => p.Name == "h1").Id;
                H2 = Module.Parts.Single(p => p.Name == "h2").Id;
                Or = Module.Parts.OfType<GatePart>().Single().Id;
            }

            public readonly WorkbenchSession Session;
            public readonly CanvasEditor Editor;
            public readonly int H1, H2, Or;
            public Module Module => Session.Project.TopModule;
            public Part Block => Module.FindPart(H1);
            public Placement Start => Module.Layout.PlacementOf(H1);
            public (double Width, double Height) Natural => PartGeometry.NaturalSize(Session.Project, Block);

            public void Select(params int[] parts) => Editor.Selection.Box(parts, new int[0], extend: false);

            public BoardPoint HandleAt(int handle) =>
                BlockResize.At(PartGeometry.Placed(Session.Project, Block, Start), handle, CanvasEditor.SelectedRoom);

            public EditResult? Do(CanvasGesture gesture, BoardPoint at = default, BoardPoint from = default, CanvasTarget target = default) =>
                Editor.Do(new CanvasCommand(gesture, at, from, target: target));
        }

        // -----------------------------------------------------------------
        // Where a drag of each handle leaves the block
        // -----------------------------------------------------------------

        [Test]
        public void TheRightHandle_MovesTheRightSide_ByHalfCells_TheLeftStayingPut()
        {
            var b = new Bench();
            (double w, double h) = b.Natural;
            Assert.AreEqual(3.0, w, "sanity: a block is three cells wide");
            PartOutline was = PartGeometry.Placed(b.Session.Project, b.Block, b.Start);

            Placement wider = BlockResize.Resized(b.Session.Project, b.Block, b.Start, 3, 2.2, 0.7);
            Assert.AreEqual(5.0, wider.Width, "2.2 cells, to the half cell");
            Assert.AreEqual(0.0, wider.Height, "the height untouched: still its natural one");
            PartOutline now = PartGeometry.Placed(b.Session.Project, b.Block, wider);
            Assert.AreEqual(was.Left, now.Left, 1e-9, "the left side stays put");
            Assert.AreEqual(was.Right + 2.0, now.Right, 1e-9);
            Assert.AreEqual((was.Bottom, was.Top), (now.Bottom, now.Top));
        }

        [Test]
        public void EverySide_AndACorner_MovesOnlyWhatItIsOn()
        {
            var b = new Bench();
            Project project = b.Session.Project;
            PartOutline was = PartGeometry.Placed(project, b.Block, b.Start);

            PartOutline left = PartGeometry.Placed(project, b.Block, BlockResize.Resized(project, b.Block, b.Start, 7, -1, 0));
            Assert.AreEqual((was.Left - 1, was.Right), (left.Left, left.Right), "the left handle: the left side, out by a cell");

            PartOutline top = PartGeometry.Placed(project, b.Block, BlockResize.Resized(project, b.Block, b.Start, 1, 0, 1.6));
            Assert.AreEqual((was.Bottom, was.Top + 1.5), (top.Bottom, top.Top), "the top handle: the top, up by 1.6 to the half cell");
            Assert.AreEqual((was.Left, was.Right), (top.Left, top.Right));

            PartOutline corner = PartGeometry.Placed(project, b.Block, BlockResize.Resized(project, b.Block, b.Start, 4, 1, -1));
            Assert.AreEqual((was.Left, was.Top), (corner.Left, corner.Top), "the bottom right corner: the left and the top stay put");
            Assert.AreEqual((was.Right + 1, was.Bottom - 1), (corner.Right, corner.Bottom));
        }

        [Test]
        public void ADrag_NeverMakesTheBlockSmallerThanNatural_NorLargerThanTheCap_AndNaturalIsKeptAsNoSize()
        {
            var b = new Bench();
            Project project = b.Session.Project;

            Placement shrunk = BlockResize.Resized(project, b.Block, b.Start, 3, -5, 0);
            Assert.AreEqual(b.Start, shrunk, "no smaller than natural, and a natural size is no size at all");
            Assert.IsFalse(shrunk.HasSize);

            Placement back = BlockResize.Resized(project, b.Block, BlockResize.Resized(project, b.Block, b.Start, 3, 2, 0), 3, -2, 0);
            Assert.AreEqual(b.Start, back, "dragged out and back: the block it was");

            Placement huge = BlockResize.Resized(project, b.Block, b.Start, 2, 500, 500);
            Assert.AreEqual((PartGeometry.MaxSize, PartGeometry.MaxSize), (huge.Width, huge.Height), "no larger than the cap");
        }

        [Test]
        public void ATurnedBlock_IsResizedAlongTheBoardsAxes()
        {
            var b = new Bench();
            Project project = b.Session.Project;
            (double w, double h) = b.Natural;
            Placement turned = b.Start.Turned(b.Start.Position, 1, false);
            PartOutline was = PartGeometry.Placed(project, b.Block, turned);
            Assert.AreEqual(h, was.Right - was.Left, 1e-9, "sanity: turned a quarter, its height runs across the board");

            Placement wider = BlockResize.Resized(project, b.Block, turned, 3, 2, 0);
            Assert.AreEqual((0.0, h + 2), (wider.Width, wider.Height), "the right handle: its own height, across the board");
            Assert.AreEqual(1, wider.QuarterTurns);
            PartOutline now = PartGeometry.Placed(project, b.Block, wider);
            Assert.AreEqual(was.Left, now.Left, 1e-9);
            Assert.AreEqual(was.Right + 2, now.Right, 1e-9);
            Assert.AreEqual((was.Bottom, was.Top), (now.Bottom, now.Top));
        }

        // -----------------------------------------------------------------
        // The handles
        // -----------------------------------------------------------------

        [Test]
        public void TheHandles_AreRoundOneSelectedBlock_AndNothingElse()
        {
            var b = new Bench();
            var overlay = new CanvasOverlay();

            b.Select(b.H1);
            Assert.AreEqual(b.H1, b.Editor.HandledPart);
            b.Editor.Overlay(overlay);
            Assert.AreEqual(BlockResize.Handles, overlay.ResizeHandles.Count, "four corners and four sides");

            b.Select(b.H1, b.H2);
            Assert.AreEqual(-1, b.Editor.HandledPart, "two blocks: a resize nothing does");
            overlay.Clear();
            b.Editor.Overlay(overlay);
            Assert.AreEqual(0, overlay.ResizeHandles.Count);

            b.Select(b.Or);
            Assert.AreEqual(-1, b.Editor.HandledPart, "a gate keeps its own size");
            b.Select();
            Assert.AreEqual(-1, b.Editor.HandledPart);
        }

        [Test]
        public void APressOnAHandle_IsTheHandle_BeforeTheBlockOrItsPins()
        {
            var b = new Bench();
            SchematicLayout.Frame frame = b.Session.Run.Model.Layout.Board;
            Assert.AreEqual(CanvasTargetKind.Pin, b.Editor.TargetAt(frame.ToStage(PinOf(b, "a")), 1f).Kind, "sanity: unselected, a pin");

            b.Select(b.H1);
            for (int i = 0; i < BlockResize.Handles; i++)
            {
                CanvasTarget at = b.Editor.TargetAt(frame.ToStage(b.HandleAt(i)), 1f);
                Assert.AreEqual(CanvasTargetKind.Handle, at.Kind, $"handle {i}");
                Assert.AreEqual((b.H1, i), (at.Part, at.Segment));
            }
            Assert.AreEqual("part " + b.H1 + " handle 3", b.Editor.TargetAt(frame.ToStage(b.HandleAt(3)), 1f).ToString());
            Assert.AreEqual(CanvasTargetKind.Part, b.Editor.TargetAt(frame.ToStage(b.Start.Position), 1f).Kind, "its middle still moves it");
        }

        private static BoardPoint PinOf(Bench b, string pin) =>
            PartGeometry.Placed(b.Session.Project, b.Block, b.Start).Pins.Single(p => p.Pin == pin).At;

        // -----------------------------------------------------------------
        // The drag
        // -----------------------------------------------------------------

        [Test]
        public void ADragOfAHandle_IsDrawnWhereItIsGoing_AndMadeOnce_OnRelease()
        {
            var b = new Bench();
            Project before = b.Session.Project;
            Placement start = b.Start;
            int wires = b.Module.Wires.Count(w => (!w.From.IsTap && w.From.Pin.Part == b.H1) || (!w.To.IsTap && w.To.Pin.Part == b.H1));
            Assert.AreEqual(4, wires, "sanity: a, b, s and c");
            b.Select(b.H1);
            BoardPoint from = b.HandleAt(3);
            var target = new CanvasTarget(CanvasTargetKind.Handle, part: b.H1, segment: 3);

            Assert.IsNull(b.Do(CanvasGesture.BeginResize, from: from, target: target));
            Assert.IsTrue(b.Editor.Resizing);
            b.Do(CanvasGesture.ResizeTo, new BoardPoint(from.X + 2.2, from.Y + 0.4), from);
            var overlay = new CanvasOverlay();
            b.Editor.Overlay(overlay);
            Assert.AreSame(before, b.Session.Project, "nothing is resized until the handle is let go");
            Assert.AreEqual(1, overlay.Ghosts.Count, "its outline, drawn where it is going");
            Assert.AreEqual(overlay.Selected[0].xMax + 2 * DocumentSchematic.Cell, overlay.Ghosts[0].xMax, 1e-3f, "two cells wider, on the half cell");
            Assert.AreEqual(overlay.Selected[0].xMin, overlay.Ghosts[0].xMin, 1e-3f);
            Assert.AreEqual(wires, overlay.GhostWires.Count, "and its wires follow it");
            Assert.AreEqual(BlockResize.Handles, overlay.ResizeHandles.Count, "the handles round where it is going");

            EditResult? made = b.Do(CanvasGesture.EndResize, new BoardPoint(from.X + 2.2, from.Y), from);
            Assert.IsTrue(made.HasValue && made.Value.Done, made?.Refusal);
            Assert.IsFalse(b.Editor.Resizing);
            Assert.AreEqual(new Placement(new BoardPoint(start.Position.X + 1, start.Position.Y), 0, false, 5, 0), b.Start);
            PartOutline now = PartGeometry.Placed(b.Session.Project, b.Block, b.Start);
            foreach (Wire wire in b.Module.Wires.Where(w => !w.To.IsTap && w.To.Pin.Part == b.H1))
                Assert.AreEqual(now.Left, b.Session.RoutesOf(b.Session.Project, b.Module)[wire.Id].Last().X, 1e-9, $"{wire.To.Pin.Pin}'s wire meets its pin");

            Assert.IsTrue(b.Session.Undo());
            Assert.AreEqual(start, b.Start, "one step");
        }

        [Test]
        public void AHandleLetGoWhereItWasTaken_ChangesNothing_AndEscapeLetsADragGo()
        {
            var b = new Bench();
            Project before = b.Session.Project;
            b.Select(b.H1);
            BoardPoint from = b.HandleAt(5);
            var target = new CanvasTarget(CanvasTargetKind.Handle, part: b.H1, segment: 5);

            b.Do(CanvasGesture.BeginResize, from: from, target: target);
            Assert.IsNull(b.Do(CanvasGesture.EndResize, new BoardPoint(from.X, from.Y + 0.1), from), "no change, no edit");
            Assert.AreSame(before, b.Session.Project);

            b.Do(CanvasGesture.BeginResize, from: from, target: target);
            b.Do(CanvasGesture.ResizeTo, new BoardPoint(from.X, from.Y - 3), from);
            b.Editor.Abandon();
            Assert.IsFalse(b.Editor.Resizing);
            Assert.IsNull(b.Do(CanvasGesture.EndResize, new BoardPoint(from.X, from.Y - 3), from));
            Assert.AreSame(before, b.Session.Project, "let go of: nothing resized");
        }

        [Test]
        public void AResize_IsRefusedOnALockedCircuit_AsAMoveIs()
        {
            var b = new Bench();
            b.Select(b.H1);
            b.Session.Locked = true;
            EditResult? refused = b.Do(CanvasGesture.BeginResize, from: b.HandleAt(3), target: new CanvasTarget(CanvasTargetKind.Handle, part: b.H1, segment: 3));
            Assert.IsTrue(refused.HasValue && !refused.Value.Done);
            Assert.AreEqual(WorkbenchSession.LockedRefusal, refused.Value.Refusal);
            Assert.IsFalse(b.Editor.Resizing);
            Assert.IsFalse(b.Session.ResetSize(new[] { b.H1 }).Done, "RESET SIZE too");
        }

        [Test]
        public void TurningAndCopyingAResizedBlock_KeepItsSize()
        {
            var b = new Bench();
            Assert.IsTrue(b.Session.Move(new Dictionary<int, Placement> { [b.H1] = b.Start.WithSize(6, 4) }).Done);
            Assert.IsTrue(b.Session.Turn(new[] { b.H1 }, 1).Done);
            Assert.AreEqual((1, 6.0, 4.0), (b.Start.QuarterTurns, b.Start.Width, b.Start.Height), "turned, the same size");

            b.Select(b.H1);
            Clip clip = b.Editor.Copy();
            Assert.IsNotNull(clip, b.Editor.Refusal);
            EditResult pasted = b.Editor.Paste(clip);
            Assert.IsTrue(pasted.Done, pasted.Refusal);
            int copy = b.Editor.Selection.Parts.Single();
            Assert.AreNotEqual(b.H1, copy);
            Placement of = b.Module.Layout.PlacementOf(copy);
            Assert.AreEqual((6.0, 4.0), (of.Width, of.Height), "pasted, the same size");
        }

        // -----------------------------------------------------------------
        // RESET SIZE
        // -----------------------------------------------------------------

        [Test]
        public void ResetSize_PutsAResizedBlockBackToNatural_WhereItStands_InOneStep()
        {
            var b = new Bench();
            Assert.IsFalse(b.Session.AnyResized(new[] { b.H1, b.H2 }));
            Assert.AreEqual(WorkbenchSession.NothingResized, b.Session.ResetSize(new[] { b.H1, b.H2 }).Refusal);

            Placement sized = b.Start.Moved(1, 0).WithSize(6, 4);
            Assert.IsTrue(b.Session.Move(new Dictionary<int, Placement> { [b.H1] = sized }).Done);
            Assert.IsTrue(b.Session.AnyResized(new[] { b.H1, b.H2 }));

            EditResult reset = b.Session.ResetSize(new[] { b.H1, b.H2 });
            Assert.IsTrue(reset.Done, reset.Refusal);
            Assert.AreEqual(sized.WithSize(0, 0), b.Start, "natural, where it stands");
            Assert.IsFalse(b.Session.AnyResized(new[] { b.H1 }));
            Assert.IsTrue(b.Session.Undo());
            Assert.AreEqual(sized, b.Start, "one step");
        }

        // -----------------------------------------------------------------
        // The pointer
        // -----------------------------------------------------------------

        private static string Run(CanvasPointer pointer, CanvasPress press, params float[] moves)
        {
            var commands = new List<CanvasCommand>();
            pointer.Press(press, commands);
            float last = 0;
            foreach (float px in moves)
            {
                last = px;
                pointer.Move(press.Window + new Vec2(px, 0), new BoardPoint(4 + px / 48.0, 0), false, commands);
            }

            pointer.Release(new BoardPoint(4 + last / 48.0, 0), CanvasTarget.Nothing, false, commands);
            Assert.IsFalse(pointer.Held, "let go");
            return string.Join(", ", commands);
        }

        private static CanvasPress OnHandle(bool shift = false, bool ctrl = false) => new CanvasPress
        {
            Button = 0, Window = new Vec2(100, 100), Board = new BoardPoint(4, 0), OnSelected = true, Shift = shift, Ctrl = ctrl,
            Target = new CanvasTarget(CanvasTargetKind.Handle, part: 3, segment: 2),
        };

        [Test]
        public void APressOnAHandle_DraggedResizes_ClickedDoesNothing_AndWithShiftIsItsBlocks()
        {
            var pointer = new CanvasPointer();
            Assert.AreEqual("BeginResize part 3 handle 2, ResizeTo 5,0, ResizeTo 6,0, EndResize 6,0", Run(pointer, OnHandle(), 48f, 96f));
            Assert.AreEqual("", Run(pointer, OnHandle()), "a click: nothing");
            Assert.AreEqual("", Run(pointer, OnHandle(), 2f), "nor a nudge short of a drag");
            StringAssert.StartsWith("BeginMove", Run(pointer, OnHandle(shift: true), 48f), "Shift: its block, as on its body -- a drag moves it");
            Assert.AreEqual("Select part 3 ^", Run(pointer, OnHandle(ctrl: true)));
        }

        [Test]
        public void TheStatusLine_OverAHandle_SaysItResizes()
        {
            StringAssert.StartsWith("block:  drag to resize", KeyMap.StatusFor(PointerArea.Handle));
            Assert.IsTrue(KeyMap.CircuitGestures.Any(g => g.Says.Contains("resize")), "the keyboard map lists it");
        }

        // -----------------------------------------------------------------
        // On the drawing of code
        // -----------------------------------------------------------------

        private const string Inner = "module inner (input logic a, b, output logic y);\n    assign y = a & b;\nendmodule\n";

        private const string Outer = "module m (input logic a, b, c, output logic y, z);\n    inner u (.a(a), .b(b), .y(y));\n    assign z = a | c;\nendmodule\n";

        private static WorkbenchSession WrittenTop()
        {
            var editor = new BoardEditor(Project.New("scratch"));
            Assert.IsTrue(editor.AddWrittenModule("inner", Inner).Done);
            Assert.IsTrue(editor.AddWrittenModule("m", Outer).Done);
            var session = new WorkbenchSession(editor.Project with { Top = "m" }, Ticking(0.01));
            session.Finish();
            Assert.IsTrue(session.ShowsDrawingOfCode, "sanity: a written top opens as its drawing");
            return session;
        }

        [Test]
        public void OnTheDrawingOfCode_AnInstancesBlock_IsResized_AndKeptWithTheModule_TheCodeUntouched()
        {
            WorkbenchSession session = WrittenTop();
            var editor = new CanvasEditor(session);
            string code = session.Project.Find("m").Source;
            int u = session.DrawnModule.Parts.OfType<BlockPart>().Single().Id;
            editor.Selection.Box(new[] { u }, new int[0], extend: false);
            Assert.AreEqual(u, editor.HandledPart);

            Placement start = session.DrawnModule.Layout.PlacementOf(u);
            BoardPoint from = BlockResize.At(PartGeometry.Placed(session.DrawnProject, session.DrawnModule.FindPart(u), start), 5, CanvasEditor.SelectedRoom);
            editor.Do(new CanvasCommand(CanvasGesture.BeginResize, from: from, target: new CanvasTarget(CanvasTargetKind.Handle, part: u, segment: 5)));
            EditResult? made = editor.Do(new CanvasCommand(CanvasGesture.EndResize, new BoardPoint(from.X, from.Y - 3), from));
            Assert.IsTrue(made.HasValue && made.Value.Done, made?.Refusal);

            Module m = session.Project.Find("m");
            Assert.AreEqual(code, m.Source, "the code untouched");
            Assert.AreEqual(1, m.Arrangement.Count, "kept with the module, by its key");
            int again = session.DrawnModule.Parts.OfType<BlockPart>().Single().Id;
            Placement now = session.DrawnModule.Layout.PlacementOf(again);
            Assert.AreEqual(PartGeometry.NaturalSize(session.DrawnProject, session.DrawnModule.FindPart(again)).Height + 3, now.Height, 1e-9, "three cells taller");
            Assert.IsTrue(session.AnyResized(new[] { again }));
            Assert.IsTrue(session.ResetSize(new[] { again }).Done);
            Assert.IsFalse(session.DrawnModule.Layout.PlacementOf(session.DrawnModule.Parts.OfType<BlockPart>().Single().Id).HasSize, "RESET SIZE: natural again");

            session.DrawGates(true);
            Assert.IsTrue(session.ShowsGatesOfCode);
            int gated = session.DrawnModule.Parts.OfType<BlockPart>().Single().Id;
            Assert.AreEqual(WorkbenchSession.GatesArrangeRefusal,
                session.Move(new Dictionary<int, Placement> { [gated] = session.DrawnModule.Layout.PlacementOf(gated).WithSize(8, 4) }).Refusal,
                "with GATES on, refused as a move is");
        }
    }
}
