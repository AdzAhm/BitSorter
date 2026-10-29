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
            Assert.IsTrue(session.ShowsCode, "sanity: inside, the code");
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
