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

        [Test]
        public void ADrawingOfCode_TakesNoEdit_AndSaysHowToChangeIt()
        {
            WorkbenchSession session = InsideTheWrittenAdder();
            session.DrawCode(true);
            int part = session.Run.Model.Layout.Parts.First().Part.Id;

            EditResult moved = session.Move(new Dictionary<int, Placement> { [part] = new Placement(3, 3) });
            Assert.IsFalse(moved.Done);
            Assert.AreEqual(WorkbenchSession.DrawingRefusal, moved.Refusal);
            Assert.IsFalse(session.Remove(new[] { part }).Done);
            Assert.IsFalse(session.Turn(new[] { part }, 1).Done);
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

        /// <summary>Text typed and not yet applied is still there after a look at the drawing (the review of #108).</summary>
        [Test]
        public void TextNotYetApplied_IsKept_AcrossALookAtTheDrawing()
        {
            WorkbenchSession session = InsideTheWrittenAdder();
            CodeDocument code = session.Code;
            string typed = code.Text + "// a note\n";
            code.Type(typed, 100);
            Assert.IsTrue(code.Pending, "sanity: typed, not applied");
            session.DrawCode(true);
            Assert.IsNull(session.Code);
            session.DrawCode(false);
            Assert.AreSame(code, session.Code);
            Assert.AreEqual(typed, session.Code.Text);
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
    }
}
