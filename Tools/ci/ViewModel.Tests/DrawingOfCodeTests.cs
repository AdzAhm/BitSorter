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
