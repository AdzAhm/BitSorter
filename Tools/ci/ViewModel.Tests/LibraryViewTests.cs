using System;
using System.Collections.Generic;
using System.Linq;
using BitSorter.Board;
using BitSorter.Sim;
using BitSorter.SystemVerilog.Syntax;
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

        /// <summary>
        /// A gate copied before the library changed is pasted at its cell's delays now, not refused for the old ones (the
        /// 14d review): the full adder's OR copied, COURSE CELLS set at 3, and the paste an OR2 at 3.
        /// </summary>
        [Test]
        public void AGateCopiedBeforeTheLibraryChanged_IsPastedAtItsCellsDelays()
        {
            var session = new WorkbenchSession(WorkbenchSamples.FullAdder(), Ticking(0.01));
            session.Finish();
            GatePart or = session.Project.TopModule.Parts.OfType<GatePart>().Single(g => g.Kind == GateKind.Or);
            Clip clip = Clipboard.Copy(session.Project, session.Module, new[] { or.Id });
            Assert.IsTrue(session.SetLibrary(GateLibrary.CourseCells(3)).Done);

            var parts = new List<int>();
            EditResult pasted = Clipboard.Paste(session.Editor, session.Module, clip, parts, new List<int>());
            Assert.IsTrue(pasted.Done, pasted.Refusal);
            GatePart copy = (GatePart)session.Project.TopModule.FindPart(parts.Single());
            Assert.AreEqual((3L, 3L), (copy.Rise, copy.Fall));
        }

        /// <summary>The library it already has, set again, is no step: nothing to undo, and the run not made again.</summary>
        [Test]
        public void TheSameLibrarySetAgain_IsNoStep()
        {
            WorkbenchSession session = Under(GateLibrary.CourseCells(1) with { GateBudget = 5 });
            int steps = 0;
            while (session.Undo())
                steps++;
            Assert.AreEqual(1, steps, "sanity: setting it was one step");
            Assert.IsTrue(session.Redo());

            Assert.IsTrue(session.SetLibrary(GateLibrary.CourseCells(1) with { GateBudget = 5 }).Done, "the same, made afresh");
            Assert.IsTrue(session.Undo());
            Assert.IsNull(session.Project.Settings?.Library, "one undo goes back past the library: setting it again made no step");
        }

        /// <summary>No gates yet, under a budget: one sentence, not "No gates yet.; ...".</summary>
        [Test]
        public void NoGatesUnderABudget_ReadsAsOneSentence()
        {
            WorkbenchSession session = new WorkbenchSession(WorkbenchSamples.Blank(), Ticking(0.01));
            Assert.IsTrue(session.SetLibrary(GateLibrary.CourseCells(1) with { GateBudget = 5 }).Done);
            Assert.AreEqual("No gates yet; the library's budget is 5", session.Timing.Lines.Single(l => l.Label == "GATE COUNT").Text);
        }

        /// <summary>
        /// Over its library's budget, the TRANSCRIPT says so as a warning too (Part 14g, Ahmad's choice), in the TIMING tab's
        /// own count; within the budget, or with none, the transcript has the document's diagnostics alone -- the same list.
        /// </summary>
        [Test]
        public void OverTheBudget_TheTranscriptSaysSo_InTheTimingTabsCount()
        {
            WorkbenchSession within = Under(GateLibrary.CourseCells(1) with { GateBudget = 5 });
            Assert.AreSame(within.Editor.Diagnostics, within.Diagnostics, "within the budget: nothing more");
            Assert.IsNull(within.OverBudget);

            WorkbenchSession session = Under(GateLibrary.CourseCells(1) with { GateBudget = 4 });
            Assert.AreEqual(5, session.Timing.Analysis.Gates, "sanity: the full adder's five gates, as the TIMING tab counts them");
            BoardDiagnostic over = session.Diagnostics.Last();
            Assert.AreEqual(DiagnosticSeverity.Warning, over.Severity);
            Assert.AreEqual("5 gates, over the library's budget of 4: the TIMING tab counts them by kind.", over.Message);
            Assert.AreEqual(over.Message, session.OverBudget);
            Assert.AreSame(session.Diagnostics, session.Diagnostics, "the same list until something changes");
            Assert.IsFalse(session.Editor.Diagnostics.Any(d => d.Message == over.Message), "a warning the transcript adds, never a rule's");

            var transcript = new TranscriptView();
            transcript.Update(session.Diagnostics, null);
            Assert.AreEqual(1, transcript.Lines.Count(l => l.Severity == TranscriptSeverity.Warning && l.Text == over.Message));

            Assert.IsTrue(session.SetLibrary(session.Project.Settings.Library with { GateBudget = -1 }).Done);
            Assert.AreSame(session.Editor.Diagnostics, session.Diagnostics, "no budget: the document's alone");
        }

        /// <summary>
        /// Written code under a library (Part 14g): a fault of the library's is marked at its line in the code editor, as any
        /// other is, and the text does not apply -- the document keeps the code that held to it.
        /// </summary>
        [Test]
        public void WrittenCode_UnderALibrary_IsMarkedWhereItBreaksIt_AndNotApplied()
        {
            var session = new WorkbenchSession(WorkbenchSamples.FullAdder(), Ticking(0.01));
            session.Finish();
            Assert.IsTrue(session.SetLibrary(GateLibrary.CourseCells(1)).Done);
            Assert.IsTrue(session.AddWrittenModule("code1").Done, "the template only connects, so it holds");
            int block = session.Place(PartsCatalog.For(session.Project, session.Module).Single(e => e.Module == "code1"), new Placement(20, 20)).Id;
            Assert.IsTrue(session.StepInto(block));

            CodeDocument code = session.Code;
            string held = code.Text;
            string broken = held.Replace("assign y = a;", "assign y = a & a;");
            code.Type(broken, 0);
            code.Frame(CodeDocument.PauseMilliseconds + 1);

            CodeMark mark = code.Marks.Single(m => m.IsError);
            StringAssert.Contains("This assignment works something out", mark.Message);
            Assert.AreEqual(broken.Substring(0, broken.IndexOf("assign")).Count(c => c == '\n') + 1, mark.Line, "at its line");
            Assert.AreEqual(held, code.Applied, "and not applied");
        }

        /// <summary>The budget changed and nothing else, the session's timing says the new one: no gate moved, but its line did.</summary>
        [Test]
        public void ABudgetChanged_IsSaidAtOnce()
        {
            WorkbenchSession session = Under(GateLibrary.CourseCells(1) with { GateBudget = 5 });
            StringAssert.EndsWith("the library's budget is 5", session.Timing.Lines.Single(l => l.Label == "GATE COUNT").Text);

            Assert.IsTrue(session.SetLibrary(session.Project.Settings.Library with { GateBudget = 4 }).Done);
            StringAssert.EndsWith("over the library's budget of 4", session.Timing.Lines.Single(l => l.Label == "GATE COUNT").Text);
        }
    }
}
