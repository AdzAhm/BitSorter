using System;
using System.Collections.Generic;
using System.Linq;
using BitSorter.Board;
using BitSorter.Sim;
using NUnit.Framework;

namespace BitSorter.ViewModel.Tests
{
    /// <summary>
    /// Gate-library mode in the workbench (Part 14d): PARTS offers only what the library allows, a gate placed takes its
    /// cell's delays, the inspector keeps them its cell's, and the TIMING tab counts gates against the budget.
    /// </summary>
    public class LibraryViewTests
    {
        private static Func<double> Ticking(double step)
        {
            double now = 0;
            return () => { double read = now; now += step; return read; };
        }

        private static WorkbenchSession Under(GateLibrary library)
        {
            var session = new WorkbenchSession(WorkbenchSamples.FullAdder(), Ticking(0.01));
            session.Finish();
            EditResult set = session.SetLibrary(library);
            Assert.IsTrue(set.Done, set.Refusal);
            return session;
        }

        [Test]
        public void PARTS_UnderTheCoursesCells_OffersItsGates_AndNoMuxNorTriState()
        {
            WorkbenchSession session = Under(GateLibrary.CourseCells(2));
            string[] keys = PartsCatalog.For(session.Project, session.Module).Select(e => e.Key).ToArray();

            CollectionAssert.IsSubsetOf(new[] { "and", "or", "xor", "nand", "nor", "xnor", "not", "buf", "constant", "register" }, keys);
            CollectionAssert.DoesNotContain(keys, "mux", "a MUX is no cell");
            CollectionAssert.DoesNotContain(keys, "bufif1", "no tri-state among the course's cells");

            Assert.IsTrue(session.SetLibrary(GateLibrary.CourseCells(2) with { NoConstants = true }).Done);
            CollectionAssert.DoesNotContain(PartsCatalog.For(session.Project, session.Module).Select(e => e.Key), "constant");
        }

        /// <summary>A NAND placed is the library's NAND2, at its delays; a library of only NAND3 places a NAND of three.</summary>
        [Test]
        public void AGatePlaced_TakesItsCellsDelays()
        {
            WorkbenchSession session = Under(GateLibrary.CourseCells(2) with
            {
                Cells = GateLibrary.AllCells(2).Select(c => c.Kind == GateKind.Nand && c.Inputs == 2 ? c with { Rise = 4, Fall = 3 } : c).ToList(),
            });
            var nand = (GatePart)PartsCatalog.Make(session.Project, session.Module, PartsCatalog.Find(session.Project, session.Module, "nand"));
            Assert.AreEqual((2, 4L, 3L), (nand.Inputs, nand.Rise, nand.Fall));

            Project threes = session.Project with
            {
                Settings = session.Project.Settings with
                {
                    Library = new GateLibrary { Cells = new[] { new LibraryCell(GateKind.Nand, 3, 5, 6) } },
                },
            };
            var three = (GatePart)PartsCatalog.Make(threes, session.Module, PartsCatalog.Find(threes, session.Module, "nand"));
            Assert.AreEqual((3, 5L, 6L), (three.Inputs, three.Rise, three.Fall));
        }

        /// <summary>
        /// The inspector keeps a gate's delays its cell's: a rise typed on the full adder's OR is the OR2's, and every OR2
        /// follows, one step; new inputs take the new cell's.
        /// </summary>
        [Test]
        public void TheInspector_KeepsAGatesDelaysItsCells()
        {
            WorkbenchSession session = Under(GateLibrary.CourseCells(2) with
            {
                Cells = GateLibrary.AllCells(2).Select(c => c.Kind == GateKind.Or && c.Inputs == 3 ? c with { Rise = 7, Fall = 8 } : c).ToList(),
            });
            GatePart or = session.Project.TopModule.Parts.OfType<GatePart>().Single(g => g.Kind == GateKind.Or);
            Inspection inspected = Inspector.Of(session.Project, session.Module, new CanvasTarget(CanvasTargetKind.Part, part: or.Id));
            StringAssert.Contains("the library's OR2", inspected.Says);

            EditResult rise = Inspector.Apply(session.Editor, session.Module, inspected, new Dictionary<string, string> { ["rise"] = "9" });
            Assert.IsTrue(rise.Done, rise.Refusal);
            Assert.AreEqual((9L, 2L), (session.Project.Settings.Library.CellFor(GateKind.Or, 2).Rise, session.Project.Settings.Library.CellFor(GateKind.Or, 2).Fall),
                "the cell's rise, its fall as it was");
            Assert.AreEqual(9L, ((GatePart)session.Project.TopModule.FindPart(or.Id)).Rise);
            Assert.AreEqual(GateLibrary.CustomName, session.Project.Settings.Library.Name, "no preset's any more");
            Assert.IsTrue(session.Undo());
            Assert.AreEqual(2L, session.Project.Settings.Library.CellFor(GateKind.Or, 2).Rise, "one step back");
            inspected = Inspector.Of(session.Project, session.Module, new CanvasTarget(CanvasTargetKind.Part, part: or.Id));

            EditResult inputs = Inspector.Apply(session.Editor, session.Module, inspected, new Dictionary<string, string> { ["inputs"] = "3" });
            Assert.IsTrue(inputs.Done, inputs.Refusal);
            GatePart now = (GatePart)session.Project.TopModule.FindPart(or.Id);
            Assert.AreEqual((3, 7L, 8L), (now.Inputs, now.Rise, now.Fall), "the OR3's delays");
        }

        [Test]
        public void TheGateCount_IsLaidAgainstTheBudget()
        {
            TimingLine within = TimingView.Of(Under(GateLibrary.CourseCells(1) with { GateBudget = 5 }).Project).Lines.Single(l => l.Label == "GATE COUNT");
            StringAssert.EndsWith("the library's budget is 5", within.Text);
            Assert.AreEqual(TimingLineKind.Fact, within.Kind);

            TimingLine over = TimingView.Of(Under(GateLibrary.CourseCells(1) with { GateBudget = 4 }).Project).Lines.Single(l => l.Label == "GATE COUNT");
            StringAssert.EndsWith("over the library's budget of 4", over.Text);
            Assert.AreEqual(TimingLineKind.Problem, over.Kind);
        }
    }
}
