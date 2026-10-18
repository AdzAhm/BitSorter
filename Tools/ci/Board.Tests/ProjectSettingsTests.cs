using System.Collections.Generic;
using System.Linq;
using BitSorter.SystemVerilog;
using BitSorter.SystemVerilog.Syntax;
using NUnit.Framework;

namespace BitSorter.Board.Tests
{
    /// <summary>
    /// A project's settings (Part 9b-3): the time unit relabels time and rescales nothing, reaches the
    /// printed SystemVerilog as a <c>`timescale</c> when it is not ns, comes back on import, and a saved
    /// unit that is not one loads as ns with a word to say so.
    /// </summary>
    public class ProjectSettingsTests
    {
        private static Project InUnit(Project project, string unit) => project with { Settings = project.Settings with { TimeUnit = unit } };

        [Test]
        public void AUnit_IsSetThroughTheEditor_AndUndoes()
        {
            var editor = new BoardEditor(SampleBoards.Build("half_adder"));
            Project before = editor.Project;

            Bench.Must(editor.SetSettings(editor.Project.Settings with { TimeUnit = "ps" }));
            Assert.AreEqual("ps", editor.Project.Settings.TimeUnit);
            Assert.AreSame(before.Stimulus, editor.Project.Stimulus, "the stimulus's numbers are not rescaled");

            Assert.IsTrue(editor.Undo());
            Assert.AreSame(before, editor.Project);
        }

        [TestCase("weeks")]
        [TestCase("NS")]
        [TestCase("")]
        [TestCase(null)]
        public void AUnitSystemVerilogDoesNotHave_IsRefused(string unit)
        {
            var editor = new BoardEditor(SampleBoards.Build("half_adder"));
            Project before = editor.Project;

            EditResult result = editor.SetSettings(editor.Project.Settings with { TimeUnit = unit });
            Assert.IsFalse(result.Done);
            StringAssert.Contains("is not a time unit: use s, ms, us, ns, ps or fs", result.Refusal);
            Assert.AreSame(before, editor.Project);
        }

        [Test]
        public void ADefaultGateDelayBelowZero_IsRefused()
        {
            var editor = new BoardEditor(SampleBoards.Build("half_adder"));
            EditResult result = editor.SetSettings(editor.Project.Settings with { DefaultGateDelay = -1 });
            Assert.IsFalse(result.Done);
            StringAssert.Contains("cannot be less than 0", result.Refusal);
            Assert.IsFalse(editor.SetSettings(null).Done);
        }

        [Test]
        public void AProjectInNs_PrintsNoTimescale()
        {
            foreach (PrintedSource file in BoardPrinter.Print(SampleBoards.Build("half_adder_delayed")))
                StringAssert.DoesNotContain("`timescale", file.Text, file.Path);
        }

        [TestCase("ps")]
        [TestCase("us")]
        public void AProjectInAnotherUnit_SaysItAtTheTopOfEveryFile_AndOtherwisePrintsTheSame(string unit)
        {
            Project ns = SampleBoards.Build("half_adder_delayed");
            IReadOnlyList<PrintedSource> asNs = BoardPrinter.Print(ns);
            IReadOnlyList<PrintedSource> asOther = BoardPrinter.Print(InUnit(ns, unit));

            string head = "`timescale 1" + unit + "/1" + unit + "\n\n";
            CollectionAssert.AreEqual(asNs.Select(f => f.Path), asOther.Select(f => f.Path));
            Assert.Greater(asOther.Count, 2, "sanity: the cells, the design and the testbench");
            for (int i = 0; i < asNs.Count; i++)
            {
                StringAssert.StartsWith(head, asOther[i].Text, asOther[i].Path);
                Assert.AreEqual(asNs[i].Text, asOther[i].Text.Substring(head.Length), asOther[i].Path + ": every number as it was, nothing rescaled");
            }
        }

        [Test]
        public void APrintedProject_ImportsBackInItsUnit()
        {
            Project original = InUnit(SampleBoards.Build("full_adder"), "ps");
            CompiledProject compiled = BoardCompiler.Compile(original);
            (MemorySourceFiles files, List<string> paths) = BoardPrinter.AsSources(BoardPrinter.Print(original));
            SyntaxTree read = SyntaxReader.ReadAll(paths.Where(p => p != compiled.Testbench + ".sv"), files);

            ImportResult imported = BoardImporter.Import(read, original.Top);
            Assert.IsTrue(imported.Succeeded, string.Join("\n", imported.Problems));
            Assert.AreEqual("ps", imported.Project.Settings.TimeUnit);
        }

        [TestCase("`timescale 10ns/1ns\n")]
        [TestCase("")]
        public void ADesignWithNoOneUnitOfTime_ImportsInNs(string timescale)
        {
            SyntaxTree tree = SyntaxReader.ReadText(timescale + "module flip(input logic a, output logic y);\n  assign y = ~a;\nendmodule\n");
            ImportResult imported = BoardImporter.Import(tree, "flip");
            Assert.IsTrue(imported.Succeeded, string.Join("\n", imported.Problems));
            Assert.AreEqual(TimeUnits.Default, imported.Project.Settings.TimeUnit);
        }

        [Test]
        public void ASavedUnitThatIsNotOne_LoadsAsNs_AndSaysSo()
        {
            ProjectRecord file = ProjectRecord.From(SampleBoards.Build("half_adder"));
            file.timeUnit = "fortnights";

            var problems = new List<string>();
            Project read = file.ToProject(problems);

            Assert.AreEqual("ns", read.Settings.TimeUnit);
            Assert.AreEqual(1, problems.Count, string.Join("\n", problems));
            StringAssert.Contains("'fortnights' is not one SystemVerilog has", problems[0]);
            Assert.IsFalse(BoardRules.Check(read).Any(d => d.IsError), "and the project loads clean");
        }

        [Test]
        public void ASavedUnit_LoadsAsItWasSaved()
        {
            ProjectRecord file = ProjectRecord.From(InUnit(SampleBoards.Build("half_adder"), "us"));
            var problems = new List<string>();
            Assert.AreEqual("us", file.ToProject(problems).Settings.TimeUnit);
            CollectionAssert.IsEmpty(problems);
        }
    }
}
