using System;
using System.Collections.Generic;
using System.Linq;
using BitSorter.Board;
using BitSorter.Sim;
using BitSorter.SystemVerilog;
using NUnit.Framework;

namespace BitSorter.ViewModel.Tests
{
    /// <summary>
    /// A written module shown as the drawing of its code (Part 15h-4): what its code builds, laid out as any circuit is, its
    /// wires the run's values -- those no signal holds worked out from the ones that are -- and no edit taken on it.
    /// </summary>
    public class DrawingOfCodeTests
    {
        private static Func<double> Ticking(double step)
        {
            double now = 0;
            return () => now += step;
        }

        private static WorkbenchSession InsideTheWrittenAdder()
        {
            var session = new WorkbenchSession(WorkbenchSamples.WrittenAdder(), Ticking(0.01));
            session.Finish();
            int add = session.Project.TopModule.Parts.OfType<BlockPart>().Single().Id;
            Assert.IsTrue(session.StepInto(add));
            Assert.IsTrue(session.ShowsDrawingOfCode, "a written block opens as its drawing (Part 15h-4d)");
            session.DrawCode(false);
            Assert.IsTrue(session.ShowsCode, "sanity: CODE, its code");
            return session;
        }

        [Test]
        public void AWrittenBlock_ShownAsItsDrawing_IsWhatItsCodeBuilds()
        {
            WorkbenchSession session = InsideTheWrittenAdder();
            session.DrawCode(true);

            Assert.IsTrue(session.ShowsDrawingOfCode);
            Assert.IsFalse(session.ShowsCode, "the drawing in place of the text");
            Assert.IsNull(session.Code);
            SchematicLayout layout = session.Run.Model.Layout;
            Assert.AreEqual(1, layout.Parts.Count(p => p.Symbol == PartSymbol.Operator && p.Name == "+"), "a + b is an adder");
            CollectionAssert.AreEquivalent(new[] { "a", "b", "s", "cout" }, layout.Ports.Select(p => p.Name.Split('[')[0]).ToArray());
        }

        [Test]
        public void ADrawingOfCode_HasTheRunsValues_EvenWhereNoSignalHoldsThem()
        {
            WorkbenchSession session = InsideTheWrittenAdder();
            session.DrawCode(true);
            session.Finish();
            WorkbenchModel model = session.Run.Model;
            model.Seek(model.End);

            SchematicLayout layout = model.Layout;
            int adder = layout.Parts.Single(p => p.Symbol == PartSymbol.Operator).Part.Id;
            int sum = Enumerable.Range(0, layout.Nets.Count).Single(i => layout.Nets[i].Source.Part == adder);
            Assert.IsTrue(layout.Drawn.Has(sum), "sanity: the sum is no signal");
            Assert.IsTrue(model.NetValue(sum).TryGetUInt64(out ulong value));
            Assert.AreEqual(15UL + 15UL, value, "the last operands, F and F, added");

            int a = Enumerable.Range(0, layout.Nets.Count).Single(i => layout.Nets[i].Name == "a");
            Assert.IsTrue(model.NetValue(a).TryGetUInt64(out ulong operand) && operand == 0xF, "a read off the run");
        }

        /// <summary>
        /// A part of the drawing of code is moved and turned by hand, kept with the module, the code untouched, and TIDY lays it
        /// out by itself again (Part 15h-4f); the adder's code, with its #3 and its parameter, is no code a drawing can write,
        /// so an edit of what it is is refused saying which -- with no question asked.
        /// </summary>
        [Test]
        public void ADrawingOfCode_IsArrangedByHand_AndTheAddersIsNotWrittenFrom()
        {
            WorkbenchSession session = InsideTheWrittenAdder();
            session.DrawCode(true);
            string code = session.Project.Find(session.Module).Source;
            int part = session.Run.Model.Layout.Parts.First().Part.Id;
            Assert.IsFalse(session.CanTidy, "nothing put by hand yet");

            EditResult moved = session.Move(new Dictionary<int, Placement> { [part] = new Placement(30, 3) });
            Assert.IsTrue(moved.Done, moved.Refusal);
            Assert.AreEqual(1, session.Project.Find(session.Module).Arrangement.Count, "kept with the module");
            Assert.AreEqual(code, session.Project.Find(session.Module).Source, "the code untouched");
            Assert.AreEqual(new Placement(30, 3), session.DrawnModule.Layout.PlacementOf(part), "drawn again where it was put");
            Assert.IsTrue(session.Turn(new[] { part }, 1).Done);
            Assert.AreEqual(1, session.DrawnModule.Layout.PlacementOf(part).QuarterTurns, "and turned");
            Assert.IsTrue(session.CanTidy);
            Assert.IsTrue(session.Tidy().Done);
            Assert.AreEqual(0, session.Project.Find(session.Module).Arrangement.Count, "TIDY: laid out by itself again");
            Assert.IsTrue(session.Undo());
            Assert.AreEqual(1, session.Project.Find(session.Module).Arrangement.Count, "one Ctrl+Z, the arrangement back");

            // With GATES on, the gates opened are laid out by themselves: the boxes' arrangement is not theirs, a move is
            // refused saying so, and TIDY is not up (the review of #119).
            session.DrawGates(true);
            Assert.IsTrue(session.ShowsGatesOfCode);
            Assert.IsFalse(session.CanTidy, "nothing of the gates put by hand");
            int gate = session.DrawnModule.Parts.First().Id;
            Assert.AreEqual(WorkbenchSession.GatesArrangeRefusal, session.Move(new Dictionary<int, Placement> { [gate] = new Placement(30, 3) }).Refusal);
            session.DrawGates(false);
            Assert.IsTrue(session.CanTidy, "BOXES again: the arrangement is theirs");

            EditResult removed = session.Remove(new[] { part });
            Assert.IsFalse(removed.Done);
            StringAssert.Contains("delay on the assign", removed.Refusal);
            Assert.IsFalse(session.AsksToRewrite, "nothing to ask: it cannot be written");
        }

        /// <summary>
        /// With GATES on, a drag that moves a part of the drawing of code is told what a move is told -- the gates lay
        /// themselves out -- and a wire drawn what an edit is (the 16d sweep: the drag was told its code is written from the
        /// boxes, though a move writes no code).
        /// </summary>
        [Test]
        public void WithGatesOn_ADragToMove_IsToldWhatAMoveIs_AndAWireWhatAnEditIs()
        {
            WorkbenchSession session = InsideTheWrittenAdder();
            session.DrawCode(true);
            session.DrawGates(true);
            Assert.IsTrue(session.ShowsGatesOfCode, "sanity: GATES on");
            var canvas = new CanvasEditor(session);
            Part gate = session.DrawnModule.Parts.First(p => p is GatePart);
            var target = new CanvasTarget(CanvasTargetKind.Part, part: gate.Id);
            Board.Point at = session.DrawnModule.Layout.PlacementOf(gate.Id).Position;

            EditResult? moved = canvas.Do(new CanvasCommand(CanvasGesture.BeginMove, from: at, target: target));
            Assert.AreEqual(WorkbenchSession.GatesArrangeRefusal, moved?.Refusal, "a move");
            EditResult? wired = canvas.Do(new CanvasCommand(CanvasGesture.BeginWire, from: at, target: target));
            Assert.AreEqual(WorkbenchSession.GatesRefusal, wired?.Refusal, "a wire");
        }

        /// <summary>
        /// An edit that changes the design's signals, undone and done again while the drawing is up: every wire still reads
        /// this run's signals by name, never another design's numbers (the review of #108: redo threw).
        /// </summary>
        [Test]
        public void ADrawingOfCode_ReadsItsRunsOwnSignals_AcrossAnUndoAndARedo()
        {
            WorkbenchSession session = InsideTheWrittenAdder();
            string widened = WorkbenchSamples.AdderSource.Replace("    assign #3 {cout, s} = a + b;",
                "    logic [N:0] sum;\n    assign sum = a + b;\n    assign #3 {cout, s} = sum;");
            Assert.AreNotEqual(WorkbenchSamples.AdderSource, widened, "sanity: the edit is made");
            EditResult applied = session.SetSource(widened);
            Assert.IsTrue(applied.Done, applied.Refusal);
            session.Finish();
            session.DrawCode(true);
            session.Finish();

            Assert.IsTrue(session.Undo());
            Assert.IsTrue(session.Redo());
            session.Finish();
            WorkbenchModel model = session.Run.Model;
            model.Seek(model.End);
            for (int net = 0; net < model.Layout.Nets.Count; net++)
                Assert.DoesNotThrow(() => model.NetValue(net));
            int sum = Enumerable.Range(0, model.Layout.Nets.Count).Single(i => model.Layout.Nets[i].Name == "sum");
            Assert.IsTrue(model.NetValue(sum).TryGetUInt64(out ulong value) && value == 30UL, "sum of the last F and F");
        }

        /// <summary>
        /// Text typed and not yet applied is still there after a look at the drawing (the review of #108), and is still the
        /// text EXPORT has to apply or refuse while the drawing is up (the review of #111).
        /// </summary>
        [Test]
        public void TextNotYetApplied_IsKept_AcrossALookAtTheDrawing()
        {
            WorkbenchSession session = InsideTheWrittenAdder();
            CodeDocument code = session.Code;
            Assert.IsNull(session.PendingCode, "nothing typed yet");
            string typed = code.Text + "// a note\n";
            code.Type(typed, 100);
            Assert.IsTrue(code.Pending, "sanity: typed, not applied");
            session.DrawCode(true);
            Assert.IsNull(session.Code);
            Assert.AreSame(code, session.PendingCode, "typed, then DRAWING: still pending for EXPORT");
            session.DrawCode(false);
            Assert.AreSame(code, session.Code);
            Assert.AreEqual(typed, session.Code.Text);
        }

        /// <summary>
        /// An undo with the drawing up leaves no code pending (the review of Part 16d's sweep): the editor's text, kept while the
        /// drawing is shown, is a step behind the document once the document moves under it, and the editor sets it aside as
        /// it next shows. It is not code typed and not applied, and applying it -- as leaving a circuit, CHECK, EXPORT and the
        /// lock do -- put the undone edit back.
        /// </summary>
        [Test]
        public void AnUndoWithTheDrawingUp_LeavesNoCodePending_AndApplyingPutsNothingBack()
        {
            WorkbenchSession session = InsideTheWrittenAdder();
            CodeDocument code = session.Code;
            string original = code.Text, edited = original + "// an edit that applied\n";
            code.Type(edited, 100);
            Assert.IsTrue(code.Apply().Done, "sanity: the edit applied");
            session.DrawCode(true);
            Assert.IsTrue(session.Undo());
            Assert.AreEqual(original, session.Project.Find(code.Module).Source, "sanity: undone");

            Assert.IsNull(session.PendingCode, "nothing typed, so nothing pending");
            Assert.IsFalse(code.Pending);
            Assert.IsTrue(code.Apply().Done);
            Assert.AreEqual(original, session.Project.Find(code.Module).Source, "applying the editor's text leaves the undo standing");

            session.DrawCode(false);
            Assert.AreEqual(original, session.Code.Text, "and the editor follows the document as it shows");
        }

        private const string HalfAdderCode =
            "module ha (input logic a, b, output logic s, c);\n" +
            "    assign s = a ^ b;\n" +
            "    assign c = a & b;\n" +
            "endmodule\n";

        private const string FullAdderCode =
            "module fa (input logic a, b, cin, output logic s, cout);\n" +
            "    logic s1, c1, c2;\n" +
            "    ha h1 (.a(a), .b(b), .s(s1), .c(c1));\n" +
            "    ha h2 (.a(s1), .b(cin), .s(s), .c(c2));\n" +
            "    assign cout = c1 | c2;\n" +
            "endmodule\n";

        /// <summary>A full adder written as code, of two half adders written as code: its top, fa, written too.</summary>
        private static Project WrittenFullAdder()
        {
            var editor = new BoardEditor(Project.New("top"));
            Assert.IsTrue(editor.AddWrittenModule("ha", HalfAdderCode).Done);
            Assert.IsTrue(editor.AddWrittenModule("fa", FullAdderCode).Done);
            EditResult top = editor.SetTop("fa");
            Assert.IsTrue(top.Done, top.Refusal);
            return editor.Project;
        }

        /// <summary>
        /// An instance inside written code is stepped into from its block on the drawing, and out again; and from the
        /// hierarchy, as a drawn block's is -- the breadcrumb's path each instance's name in the code.
        /// </summary>
        [Test]
        public void AnInstanceInsideWrittenCode_IsSteppedInto_AndOutAgain()
        {
            var session = new WorkbenchSession(WrittenFullAdder(), Ticking(0.01));
            session.Finish();
            Assert.IsTrue(session.ShowsDrawingOfCode, "fa is written, and shown as its drawing (Part 15h-4d)");

            SchematicLayout layout = session.Run.Model.Layout;
            Assert.AreEqual(2, layout.Parts.Count(p => p.Part is BlockPart b && b.Module == "ha"), "h1 and h2, each a block of ha");
            PartLayout h1 = layout.Parts.Single(p => p.Part is BlockPart b && b.Name == "h1");
            Assert.IsTrue(session.StepInto(h1.Part.Id));
            CollectionAssert.AreEqual(new[] { "h1" }, session.Path);
            Assert.AreEqual("ha", session.Module);
            Assert.IsTrue(session.ShowsDrawingOfCode, "inside, ha's drawing too");

            Assert.AreEqual(2, session.Run.Model.Layout.Parts.Count(p => p.Symbol == PartSymbol.Operator), "a ^ b and a & b");
            StringAssert.EndsWith(".h1", session.Run.Model.Layout.InstancePath, "its values read under h1");

            Assert.IsTrue(session.StepOut());
            Assert.AreEqual("fa", session.Module);
            Assert.IsTrue(session.ShowsDrawingOfCode, "fa's drawing as it was left");

            string design = session.Run.Model.Layout.DesignPath;
            Assert.IsTrue(session.StepToScope(design + ".h2"), "from the hierarchy");
            CollectionAssert.AreEqual(new[] { "h2" }, session.Path);
            Assert.AreEqual("ha", session.Module);
            Assert.IsTrue(session.StepToScope(design));
            Assert.IsEmpty(session.Path);
        }

        /// <summary>
        /// GATES on the written adder (Part 15h-4): its sum with its carry drawn as the 4-bit ripple of full adders, every wire
        /// valued at the end of the run -- those inside the ripple by its gates -- the choice kept for the module, and the box
        /// back without it.
        /// </summary>
        [Test]
        public void GatesOnTheWrittenAdder_DrawItsSumAsTheRipple_EveryWireValued()
        {
            WorkbenchSession session = InsideTheWrittenAdder();
            session.DrawCode(true);
            session.Finish();
            Assert.IsFalse(session.ShowsGatesOfCode);
            Assert.AreEqual(1, session.Run.Model.Layout.Parts.Count(p => p.Symbol == PartSymbol.Operator), "sanity: the sum a box");

            session.DrawGates(true);
            session.Finish();
            Assert.IsTrue(session.ShowsGatesOfCode);
            SchematicLayout layout = session.Run.Model.Layout;
            Assert.AreEqual(0, layout.Parts.Count(p => p.Symbol == PartSymbol.Operator), "the sum opened");
            Assert.AreEqual(7, layout.Parts.Count(p => p.Part is GatePart g && g.Kind == GateKind.Xor), "four bits' ripple");

            WorkbenchModel model = session.Run.Model;
            model.Seek(model.End);
            for (int net = 0; net < layout.Nets.Count; net++)
                Assert.IsTrue(model.NetValue(net).IsKnown, layout.Nets[net].Name + " valued: F + F");

            int add = session.Project.TopModule.Parts.OfType<BlockPart>().Single().Id;
            Assert.IsTrue(session.StepOut());
            Assert.IsTrue(session.StepInto(add));
            Assert.IsTrue(session.ShowsGatesOfCode, "kept for the module");
            session.DrawGates(false);
            Assert.AreEqual(1, session.Run.Model.Layout.Parts.Count(p => p.Symbol == PartSymbol.Operator), "the box again");
        }

        /// <summary>A block made with + CODE opens on its code, the template to type into, though a written block opens as its drawing.</summary>
        [Test]
        public void ANewCodeBlock_OpensOnItsCode()
        {
            var session = new WorkbenchSession(WorkbenchSamples.WrittenAdder(), Ticking(0.01));
            session.Finish();
            Assert.IsTrue(session.AddWrittenModule("code1").Done);
            session.ShowCodeOf("code1");
            int block = session.Place(PartsCatalog.For(session.Project, session.Module).Single(e => e.Module == "code1"), new Placement(20, 20)).Id;
            session.Finish();
            Assert.IsTrue(session.StepInto(block));
            Assert.IsTrue(session.ShowsCode, "its code");
            Assert.IsNotNull(session.Code);
        }

        [Test]
        public void TheChoiceOfCodeOrDrawing_IsKeptForEachModule()
        {
            WorkbenchSession session = InsideTheWrittenAdder();
            session.DrawCode(true);
            Assert.IsTrue(session.StepOut());
            Assert.IsFalse(session.ShowsWritten, "the top is drawn");

            int add = session.Project.TopModule.Parts.OfType<BlockPart>().Single().Id;
            Assert.IsTrue(session.StepInto(add));
            Assert.IsTrue(session.ShowsDrawingOfCode, "the drawing again");

            session.DrawCode(false);
            Assert.IsTrue(session.ShowsCode);
            Assert.IsNotNull(session.Code);
            Assert.IsEmpty(session.Run.Model.Layout.Parts, "the text again, nothing drawn");
        }

        /// <summary>
        /// A word of a memory read by its address, <c>mem[addr]</c>, on the drawing of code reads the word the address names --
        /// and the model reads that word and no other: every word of the memory was a signal it followed, a track each made with
        /// every model and every one of them read again at each new step, though the drawing shows one. A 65,536-word memory
        /// made 49 MB with each model, 37 MB past one of the module shown as its code, and took 0.8 to 1.3 milliseconds a frame
        /// playing (the 16d sweep, waves-14).
        /// </summary>
        [Test]
        public void AWordOfABigMemory_OnTheDrawingOfCode_IsReadAloneByItsAddress()
        {
            ProjectImport import = ProjectImporter.Import(new List<SourceFile>
            {
                new SourceFile("rom.sv", "module rom (input logic [15:0] addr, output logic [7:0] q);\n" +
                                         "    logic [7:0] mem [0:65535];\n" +
                                         "    assign q = ~mem[addr];\n" +
                                         "endmodule\n"),
                new SourceFile("rom_tb.sv", "module rom_tb;\n" +
                                            "    logic [15:0] addr; logic [7:0] q;\n" +
                                            "    rom dut (.addr(addr), .q(q));\n" +
                                            "    initial begin\n" +
                                            "        dut.mem[3] = 8'h5A;\n" +
                                            "        for (int i = 0; i < 100; i++) #1 addr = i;\n" +
                                            "        #1 dut.mem[3] = 8'hC3;\n" +
                                            "        #1 addr = 3;\n" +
                                            "        #1 $finish;\n" +
                                            "    end\n" +
                                            "endmodule\n"),
            });
            Assert.IsTrue(import.Succeeded, string.Join("\n", import.Notes));
            var session = new WorkbenchSession(import.Project, () => 0.0);
            session.Finish();
            Assert.IsTrue(session.ShowsDrawingOfCode, "sanity: the written top drawn");
            WorkbenchModel model = session.Run.Model;
            SchematicLayout layout = model.Layout;
            int read = layout.Parts.Single(p => p.Name.StartsWith("mem[", StringComparison.Ordinal)).Part.Id;
            int word = Enumerable.Range(0, layout.Nets.Count).Single(n => layout.Nets[n].Source.Part == read);
            Assert.IsTrue(layout.Drawn.Has(word), "sanity: mem[addr] is no signal");

            LogicVec At(WorkbenchModel m, double time)
            {
                m.Seek(time);
                return m.NetValue(word);
            }
            ulong Known(LogicVec value)
            {
                Assert.IsTrue(value.TryGetUInt64(out ulong bits), "a known word: " + value);
                return bits;
            }
            Assert.AreEqual(0x5AUL, Known(At(model, 4.5)), "addr = 3: the word written there");
            Assert.IsFalse(At(model, 5.5).IsKnown, "addr = 4: a word never written, x");
            Assert.AreEqual(0xC3UL, Known(At(model, 102.5)), "the word written again, read where the address comes back to it");
            Assert.AreEqual(0x5AUL, Known(At(model, 4.5)), "and as it was, back at 4");

            // What a model of the drawing makes, and a frame of it playing, a new step each frame.
            long before = GC.GetAllocatedBytesForCurrentThread();
            var again = new WorkbenchModel(model.Session, model.Layout, model.End);
            long made = GC.GetAllocatedBytesForCurrentThread() - before;
            double time = 1;
            void Frame()
            {
                again.Seek(time = time >= 90 ? 1 : time + 1);
                for (int i = 0; i < again.Layout.Nets.Count; i++)
                    _ = again.NetValue(i);
            }
            Frame();
            var watch = System.Diagnostics.Stopwatch.StartNew();
            for (int f = 0; f < 200; f++)
                Frame();
            double frame = watch.Elapsed.TotalMilliseconds / 200;
            Assert.AreEqual(0x5AUL, Known(At(again, 4.5)), "and it reads the word all the same");

            // What a model of the same run makes with the module shown as its code: a model's own for so many signals.
            session.DrawCode(false);
            session.Finish();
            WorkbenchModel code = session.Run.Model;
            Assert.IsNull(code.Layout.Drawn, "sanity: its code, nothing worked out");
            before = GC.GetAllocatedBytesForCurrentThread();
            _ = new WorkbenchModel(code.Session, code.Layout, code.End);
            long asCode = GC.GetAllocatedBytesForCurrentThread() - before;

            TestContext.WriteLine($"a model made: {made / 1024} KB, {asCode / 1024} KB as code; a frame at a new step: {frame * 1000:F1} us");
            Assert.Less(made, asCode + (1 << 20), "what a model of the drawing makes, past what one of its code does");
            Assert.Less(frame, 0.25, "a frame of it playing, in milliseconds");
        }
    }
}
