using System.Collections.Generic;
using System.Linq;
using BitSorter.Sim;
using NUnit.Framework;

namespace BitSorter.Board.Tests
{
    /// <summary>
    /// A testbench's choice (Part 15h-6c): its parameter of an enum type read as a choice of members, chosen as one step of the
    /// document, kept in the save -- an older file reading none -- the run measured again to where the chosen value stops it,
    /// and an exported run.do giving ModelSim the member's value, never its name.
    /// </summary>
    public class BenchChoicesTests
    {
        private const string Design =
            "module inv (input logic a, output logic y);\n" +
            "    assign y = ~a;\n" +
            "endmodule\n";

        // Each program stops at a time of its own, so the run's end has to follow the choice.
        private const string Bench =
            "module tb;\n" +
            "    typedef enum {SUM, FIBONACCI, TEST} program_t;\n" +
            "    parameter program_t PROGRAM = SUM;\n" +
            "    logic a = 1'b0, y;\n" +
            "    inv dut (.a(a), .y(y));\n" +
            "    initial begin\n" +
            "        #(PROGRAM == SUM ? 10 : PROGRAM == FIBONACCI ? 20 : 30);\n" +
            "        $display(\"program %0d\", PROGRAM);\n" +
            "        $finish;\n" +
            "    end\n" +
            "endmodule\n";

        private static Project Imported()
        {
            ProjectImport import = ProjectImporter.Import(new[] { new SourceFile("inv.sv", Design), new SourceFile("tb.sv", Bench) }, "tb");
            Assert.IsTrue(import.Succeeded, string.Join("; ", import.Notes));
            return import.Project;
        }

        private static BenchChoice ChoiceOf(Project project) => BenchChoices.Of(BoardCompiler.Compile(project), project.Settings).Single();

        [Test]
        public void ATestbenchsEnumParameter_IsAChoiceOfItsMembers_TheDeclaredOneChosen()
        {
            Project project = Imported();
            BenchChoice choice = ChoiceOf(project);
            Assert.AreEqual("PROGRAM", choice.Name);
            CollectionAssert.AreEqual(new[] { "SUM", "FIBONACCI", "TEST" }, choice.Members.ToArray());
            CollectionAssert.AreEqual(new long?[] { 0, 1, 2 }, choice.Values.ToArray());
            Assert.AreEqual("SUM", choice.Declared);
            Assert.AreEqual("SUM", choice.Chosen);
            Assert.AreEqual(10, project.Stimulus.End, "the import ran it to where SUM stops");
            Assert.AreEqual("Fibonacci", BenchChoice.Said("FIBONACCI"));
            Assert.AreEqual("Bit serial", BenchChoice.Said("BIT_SERIAL"));
        }

        [Test]
        public void Choosing_IsOneStep_RunsTheChoice_AndShowsTheRunToWhereItStops()
        {
            var editor = new BoardEditor(Imported());
            Assert.IsTrue(editor.SetBenchParameter("PROGRAM", "TEST").Done);
            Assert.AreEqual("TEST", ChoiceOf(editor.Project).Chosen);
            Assert.AreEqual(30, editor.Project.Stimulus.End, "measured again: TEST stops later");

            CompiledProject compiled = BoardCompiler.Compile(editor.Project);
            var sim = new Simulator(compiled.Elaborate().Design, null, null);
            sim.RunTo(1000);
            CollectionAssert.Contains(sim.History.Messages.Select(m => m.Text).ToArray(), "program 2");

            Assert.IsTrue(editor.SetBenchParameter("PROGRAM", "TEST").Done);
            Assert.IsTrue(editor.Undo());
            Assert.AreEqual("SUM", ChoiceOf(editor.Project).Chosen, "the same choice again is no step: one undo takes it back");
            Assert.AreEqual(10, editor.Project.Stimulus.End);
        }

        [Test]
        public void AChoiceTheDesignCannotBeMadeWith_IsRefused_InTheCodesWords_AndLeavesNoStep()
        {
            // Only the second member widens past what the design takes: refused before it is a step, never a run that throws.
            const string bench =
                "module tb;\n" +
                "    typedef enum {NARROW, TOO_WIDE} size_t;\n" +
                "    parameter size_t SIZE = NARROW;\n" +
                "    logic a = 1'b0, y;\n" +
                "    if (SIZE == TOO_WIDE) begin : wide\n" +
                "        nowhere u (.a(a));\n" +
                "    end\n" +
                "    inv dut (.a(a), .y(y));\n" +
                "    initial #10 $finish;\n" +
                "endmodule\n";
            ProjectImport import = ProjectImporter.Import(new[] { new SourceFile("inv.sv", Design), new SourceFile("tb.sv", bench) }, "tb");
            Assert.IsTrue(import.Succeeded, string.Join("; ", import.Notes));
            var editor = new BoardEditor(import.Project);

            EditResult result = editor.SetBenchParameter("SIZE", "TOO_WIDE");
            Assert.IsFalse(result.Done);
            StringAssert.StartsWith("With SIZE = TOO_WIDE", result.Refusal);
            StringAssert.Contains("nowhere", result.Refusal, "the elaborator's own words");
            Assert.IsFalse(editor.CanUndo);
            Assert.AreEqual("NARROW", ChoiceOf(editor.Project).Chosen);
        }

        [Test]
        public void TheChoice_IsKeptInTheSave_AndAnOlderFileReadsNone()
        {
            var editor = new BoardEditor(Imported());
            Assert.IsTrue(editor.SetBenchParameter("PROGRAM", "FIBONACCI").Done);
            var problems = new List<string>();
            Project read = ProjectRecord.From(editor.Project).ToProject(problems);
            Assert.IsEmpty(problems);
            Assert.AreEqual("FIBONACCI", read.Settings.BenchParameters["PROGRAM"]);
            Assert.AreEqual("FIBONACCI", ChoiceOf(read).Chosen);

            ProjectRecord older = ProjectRecord.From(editor.Project);
            older.benchParameters = null;   // a file written before Part 15h-6c has no such key
            Assert.IsNull(older.ToProject(problems).Settings.BenchParameters);
        }

        [Test]
        public void AChoiceTheTestbenchNoLongerHas_IsLeftOut_AndTheRunTakesItsOwnValue()
        {
            Project project = Imported();
            project = project with { Settings = project.Settings with { BenchParameters = new Dictionary<string, string> { ["PROGRAM"] = "GONE", ["SPEED"] = "3" } } };
            CompiledProject compiled = BoardCompiler.Compile(project);
            Assert.IsEmpty(compiled.TopParameters, "neither a member nor a parameter it has");
            Assert.IsFalse(compiled.Elaborate().HasErrors);
            Assert.AreEqual("SUM", ChoiceOf(project).Chosen);
        }

        [Test]
        public void AnExportedRunDo_GivesModelSimTheMembersValue_NeverItsName()
        {
            var editor = new BoardEditor(Imported());
            CompiledProject plain = BoardCompiler.Compile(editor.Project);
            StringAssert.DoesNotContain("-G", ProjectExport.RunDo(editor.Project, plain, BoardPrinter.Print(plain)), "nothing chosen, nothing given");

            Assert.IsTrue(editor.SetBenchParameter("PROGRAM", "FIBONACCI").Done);
            CompiledProject compiled = BoardCompiler.Compile(editor.Project);
            string script = ProjectExport.RunDo(editor.Project, compiled, BoardPrinter.Print(compiled));
            // The testbench's own: -GPROGRAM=1 sets every parameter of that name in the design, and a processor's memory takes
            // its image's file name by one, so ModelSim opened "" and the program never ran (found by the ModelSim oracle,
            // Part 15i-4). Given by the testbench's path, it is the testbench's alone.
            StringAssert.Contains("vsim -voptargs=+acc -G/tb/PROGRAM=1 work.tb", script);
            StringAssert.Contains("# PROGRAM is FIBONACCI, given as its value", script);
        }

        [Test]
        public void AMemberWhoseValueTheCodeWorksOut_IsSaidInTheRunDo_NotGivenAsAnotherNumber()
        {
            const string bench =
                "module tb;\n" +
                "    localparam int BASE = 4;\n" +
                "    typedef enum int {LOW = BASE, HIGH = BASE * 2} level_t;\n" +
                "    parameter level_t LEVEL = LOW;\n" +
                "    logic a = 1'b0, y;\n" +
                "    inv dut (.a(a), .y(y));\n" +
                "    initial #5 $finish;\n" +
                "endmodule\n";
            ProjectImport import = ProjectImporter.Import(new[] { new SourceFile("inv.sv", Design), new SourceFile("tb.sv", bench) }, "tb");
            Assert.IsTrue(import.Succeeded, string.Join("; ", import.Notes));
            var editor = new BoardEditor(import.Project);
            Assert.IsTrue(editor.SetBenchParameter("LEVEL", "HIGH").Done);
            Assert.IsNull(ChoiceOf(editor.Project).ChosenValue, "sanity: a value the code works out");

            CompiledProject compiled = BoardCompiler.Compile(editor.Project);
            string script = ProjectExport.RunDo(editor.Project, compiled, BoardPrinter.Print(compiled));
            StringAssert.Contains("vsim -voptargs=+acc work.tb", script, "nothing given for it");
            StringAssert.Contains("# LEVEL is HIGH, whose value the code works out: add -G/tb/LEVEL=<its value> to vsim", script);
        }

        [Test]
        public void AnEnumsOwnValues_AreTheValuesGiven_AndOneOnFromTheLast()
        {
            const string bench =
                "module tb;\n" +
                "    typedef enum logic [3:0] {SLOW = 4'd2, FAST, ODD = 4'd9} speed_t;\n" +
                "    parameter speed_t SPEED = FAST;\n" +
                "    logic a = 1'b0, y;\n" +
                "    inv dut (.a(a), .y(y));\n" +
                "    initial #5 $finish;\n" +
                "endmodule\n";
            ProjectImport import = ProjectImporter.Import(new[] { new SourceFile("inv.sv", Design), new SourceFile("tb.sv", bench) }, "tb");
            Assert.IsTrue(import.Succeeded, string.Join("; ", import.Notes));
            BenchChoice choice = ChoiceOf(import.Project);
            CollectionAssert.AreEqual(new long?[] { 2, 3, 9 }, choice.Values.ToArray());
            Assert.AreEqual("FAST", choice.Chosen);
            Assert.AreEqual(3, choice.ChosenValue);
        }
    }
}
