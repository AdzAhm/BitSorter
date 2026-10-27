using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using BitSorter.Sim;
using BitSorter.SystemVerilog;
using NUnit.Framework;

namespace BitSorter.Board.Tests
{
    /// <summary>
    /// A course project brought in (Part 13b): which module is the testbench and which the design, what is drawn and what is
    /// written, how a file of several modules is cut, the project's other files, the unit, and what an import says when
    /// something is wrong. Every file here is written for these tests. That an imported project runs as Icarus runs the
    /// files it came from is <c>ImportOracleTests</c>'.
    /// </summary>
    public class ProjectImportTests
    {
        private const string Mux =
            "module mux2 (input logic a, input logic b, input logic s, output logic y);\n" +
            "    logic s_n, n0, n1;\n" +
            "    INV   #(.Tpdlh(2), .Tpdhl(1)) g0 (.A(s), .Z(s_n));\n" +
            "    NAND2 #(.Tpdlh(3), .Tpdhl(2)) g1 (.A(a), .B(s_n), .Z(n0));\n" +
            "    NAND2 #(.Tpdlh(3), .Tpdhl(2)) g2 (.A(b), .B(s), .Z(n1));\n" +
            "    NAND2 #(.Tpdlh(3), .Tpdhl(2)) g3 (.A(n0), .B(n1), .Z(y));\n" +
            "endmodule\n";

        private const string MuxBench =
            "module mux2_tb;\n" +
            "    logic a, b, s, y;\n" +
            "    mux2 dut (.a(a), .b(b), .s(s), .y(y));\n" +
            "    initial begin\n" +
            "        a = 0; b = 1; s = 0;\n" +
            "        #10 s = 1;\n" +
            "        #10 $stop;\n" +
            "    end\n" +
            "endmodule\n";

        private const string Register =
            "module reg4 (input logic clk, input logic [3:0] d, output logic [3:0] q);\n" +
            "    always_ff @(posedge clk) q <= q + d;\n" +
            "endmodule\n";

        private const string Pipeline =
            "module pipe (input logic clk, input logic [3:0] d, output logic [3:0] q);\n" +
            "    logic [3:0] mid;\n" +
            "    reg4 first (.clk(clk), .d(d), .q(mid));\n" +
            "    reg4 second (.clk(clk), .d(mid), .q(q));\n" +
            "endmodule\n";

        private static ProjectImport Import(params (string Path, string Text)[] files) =>
            ProjectImporter.Import(files.Select(f => new SourceFile(f.Path, f.Text)).ToList());

        private static string Notes(ProjectImport import) => string.Join("\n", import.Notes);

        /// <summary>
        /// A testbench that never prints the output it watches is said nothing of (the 14e review): it is the top, and what
        /// it sets is on the waves, so SV0403 is not its warning.
        /// </summary>
        [Test]
        public void ATestbenchsWatchedOutput_IsNoSignalSetAndNeverRead()
        {
            Project project = Import(("mux2.sv", Mux), ("mux2_tb.sv", MuxBench)).Project;
            BoardDiagnostic[] said = BoardRules.Check(project).ToArray();
            Assert.IsFalse(said.Any(d => d.Message.Contains("nothing reads it")), string.Join("; ", said.Select(d => d.Message)));
        }

        [Test]
        public void TheTestbench_IsFound_AndTheDesignIsWhatItInstantiates()
        {
            ProjectImport import = Import(("mux2_tb.sv", MuxBench), ("mux2.sv", Mux));

            Assert.IsTrue(import.Succeeded, Notes(import));
            Assert.AreEqual("mux2_tb", import.Project.Bench);
            Assert.AreEqual("mux2", import.Project.Top);
            Assert.AreEqual("mux2_tb", import.Project.Modules.Last().Name, "the testbench is listed last");
            Assert.AreEqual(20, import.Project.Stimulus.End, "the run is shown to where the testbench stops");
            Assert.IsEmpty(import.Project.Stimulus.Tracks, "the testbench drives the inputs, not a stimulus");
        }

        [Test]
        public void WithNoTestbench_TheDesignIsTheModuleNothingUses()
        {
            ProjectImport import = Import(("reg4.sv", Register), ("pipe.sv", Pipeline));

            Assert.IsTrue(import.Succeeded, Notes(import));
            Assert.AreEqual("pipe", import.Project.Top);
            Assert.IsNull(import.Project.Bench);
            Assert.IsNull(import.Project.Stimulus);
        }

        [Test]
        public void AStructuralModule_IsDrawn_AndATestbench_Written()
        {
            Project project = Import(("mux2.sv", Mux), ("mux2_tb.sv", MuxBench)).Project;

            Assert.IsFalse(project.Find("mux2").IsWritten);
            Assert.AreEqual(4, project.Find("mux2").Parts.OfType<GatePart>().Count());
            Assert.IsTrue(project.Find("mux2_tb").IsWritten);
            StringAssert.Contains("mux2 dut (", project.Find("mux2_tb").Source, "a written module is its own text");
        }

        [Test]
        public void AStructuralModule_OverAWrittenOne_IsDrawn_WithItAsABlock()
        {
            Project project = Import(("reg4.sv", Register), ("pipe.sv", Pipeline)).Project;

            Assert.IsTrue(project.Find("reg4").IsWritten, "a register that adds is not drawn");
            Module pipe = project.Find("pipe");
            Assert.IsFalse(pipe.IsWritten);
            CollectionAssert.AreEquivalent(new[] { "first", "second" }, pipe.Parts.OfType<BlockPart>().Select(b => b.Name));
            Assert.IsTrue(pipe.Parts.OfType<BlockPart>().All(b => b.Module == "reg4"));
            Assert.IsTrue(BoardCompiler.Compile(project).Succeeded);
        }

        /// <summary>A counter that takes its step from a file it includes inside itself, as the course's own files do.</summary>
        private const string Stepper =
            "module stepper (input logic clk, output logic [7:0] n);\n" +
            "    `include \"step.inc\"\n" +
            "    always_ff @(posedge clk) n <= n + STEP;\n" +
            "endmodule\n";

        [Test]
        public void ACourseIncludeFile_IsKeptAsAProjectFile_AndReadInsideTheModule()
        {
            ProjectImport import = Import(("stepper.sv", Stepper), ("step.inc", "localparam int STEP = 3;\n"));

            Assert.IsTrue(import.Succeeded, Notes(import));
            Project project = import.Project;
            Assert.AreEqual(new[] { "step.inc" }, project.Files.Select(f => f.Name).ToArray());
            StringAssert.Contains("`include \"step.inc\"", project.Find("stepper").Source, "the module's text is as written");
            Assert.IsEmpty(BoardRules.Check(project).Where(d => d.IsError), "the include is read, and STEP found");
        }

        [Test]
        public void ChangingAnIncludedFile_ChangesWhatTheModuleReadsAs()
        {
            Project project = Import(("stepper.sv", Stepper), ("step.inc", "localparam int STEP = 3;\n")).Project;
            Project emptied = project with { Files = new[] { new ProjectFile { Name = "step.inc", Text = "// nothing yet\n" } } };

            Assert.IsEmpty(BoardRules.Check(project).Where(d => d.IsError));
            Assert.That(BoardRules.Check(emptied).Any(d => d.IsError && d.Message.Contains("STEP")),
                "a reading is kept for its text and the files it names, so a changed file is read again");
            Assert.IsEmpty(BoardRules.Check(project).Where(d => d.IsError), "and the first reading is still the first file's");
        }

        [Test]
        public void AFileOfSeveralModules_IsCutAtEachEndmodule_EachWithTheSharedInclude()
        {
            const string both =
                "// Two modules sharing one set of constants.\n" +
                "`include \"codes.svh\"\n" +
                "\n" +
                "// The first.\n" +
                "module first (input logic [1:0] a, output logic y);\n" +
                "    assign y = (a == CODE_A);\n" +
                "endmodule\n" +
                "\n" +
                "// The second.\n" +
                "module second (input logic [1:0] a, output logic y);\n" +
                "    first f (.a(a), .y(y));\n" +
                "    initial if (CODE_A != 2'd1) $display(\"wrong\");\n" +
                "endmodule\n";
            const string codes =
                "`ifndef CODES_SVH\n" +
                "`define CODES_SVH\n" +
                "localparam logic [1:0] CODE_A = 2'd1;\n" +
                "`endif\n";

            ProjectImport import = Import(("both.sv", both), ("codes.svh", codes));

            Assert.IsTrue(import.Succeeded, Notes(import));
            Project project = import.Project;
            foreach (string name in new[] { "first", "second" })
            {
                string source = project.Find(name).Source;
                Assert.AreEqual(1, source.Split('\n').Count(l => l.TrimStart().StartsWith("module ")), name + ": one module each");
                StringAssert.Contains("    `include \"codes.svh\"", source, name + ": the include goes inside it");
                Assert.Less(source.IndexOf("module " + name), source.IndexOf("`include"), name + ": inside, after the header");
            }

            StringAssert.StartsWith("// Two modules sharing", project.Find("first").Source, "the first module takes the file's head");
            StringAssert.StartsWith("// The second.", project.Find("second").Source, "the comment above a module comes with it");
            StringAssert.DoesNotContain("ifndef", project.Files.Single().Text, "the guard is taken out");
            StringAssert.Contains("localparam", project.Files.Single().Text);
            Assert.That(import.Notes.Any(n => n.File == "codes.svh" && !n.IsError), Notes(import));
            Assert.IsTrue(BoardCompiler.Compile(project).Succeeded);
        }

        [Test]
        public void ATimescale_SetsTheUnit_AndIsTakenOutOfTheText()
        {
            ProjectImport import = Import(("mux2.sv", Mux), ("mux2_tb.sv", "`timescale 1ps/1ps\n" + MuxBench));

            Assert.IsTrue(import.Succeeded, Notes(import));
            Assert.AreEqual("ps", import.Project.Settings.TimeUnit);
            StringAssert.DoesNotContain("timescale", import.Project.Find("mux2_tb").Source);
        }

        [Test]
        public void AFileThatDoesNotRead_IsNamedWithItsLine_AndTheRestComesIn()
        {
            ProjectImport import = Import(("mux2.sv", Mux), ("mux2_tb.sv", MuxBench), ("broken.sv", "module broken (input a;\nendmodule\n"));

            Assert.IsTrue(import.Succeeded, Notes(import));
            Assert.IsNull(import.Project.Find("broken"));
            ImportNote note = import.Notes.First(n => n.IsError);
            Assert.AreEqual("broken.sv", note.File);
            Assert.AreEqual(1, note.Line);
        }

        [Test]
        public void ADesignThatDoesNotRead_IsRefused_AndSaysWhere()
        {
            ProjectImport import = Import(("mux2_tb.sv", MuxBench), ("mux2.sv", "module mux2 (input a\nendmodule\n"));

            Assert.IsFalse(import.Succeeded);
            Assert.That(import.Notes.Any(n => n.IsError && n.File == "mux2.sv" && n.Line > 0), Notes(import));
        }

        [Test]
        public void NoSourceAtAll_IsRefused()
        {
            Assert.IsFalse(ProjectImporter.Import(new SourceFile[0]).Succeeded);
            ProjectImport import = Import(("notes.txt", "nothing here"));
            Assert.IsFalse(import.Succeeded);
            StringAssert.Contains(".sv", Notes(import));
        }

        [Test]
        public void ACellOfTheLibrarysName_IsTheLibrarys_AndOneWithOtherPortsIsSaid()
        {
            const string gates =
                "module NAND2 (input logic A, input logic B, output logic Z);\n" +
                "    parameter Tpdlh = 1; parameter Tpdhl = 1;\n" +
                "    nand #(Tpdlh, Tpdhl) g (Z, A, B);\n" +
                "endmodule\n" +
                "module INV (input logic a, output logic y);\n" +
                "    not g (y, a);\n" +
                "endmodule\n";

            ProjectImport import = Import(("gates.sv", gates), ("mux2.sv", Mux));

            Assert.IsTrue(import.Succeeded, Notes(import));
            Assert.IsNull(import.Project.Find("NAND2"), "the library's cell is used");
            Assert.That(import.Notes.Any(n => !n.IsError && n.Message.StartsWith("NAND2 is one of BitSorter's own cells")), Notes(import));
            Assert.That(import.Notes.Any(n => n.IsError && n.Message.Contains("its ports are (a, y)")), Notes(import));
        }

        [Test]
        public void ATestbenchThatNeverStops_IsShownToWhereTheImportStoppedIt()
        {
            const string bench =
                "module free_tb;\n" +
                "    logic clk = 0;\n" +
                "    logic [3:0] d = 0, q;\n" +
                "    reg4 r (.clk(clk), .d(d), .q(q));\n" +
                "    always #5 clk = ~clk;\n" +
                "endmodule\n";

            ProjectImport import = Import(("reg4.sv", Register), ("free_tb.sv", bench));

            Assert.IsTrue(import.Succeeded, Notes(import));
            Assert.That(import.Notes.Any(n => !n.IsError && n.Message.Contains("$stop")), Notes(import));
            Assert.Greater(import.Project.Stimulus.End, 1000);
            Assert.LessOrEqual(import.Project.Stimulus.End, ProjectImporter.MaxRun);
        }

        [Test]
        public void AnImportedProject_IsSavedWithItsTestbenchAndFiles_AndReadBack()
        {
            // The bench reads the table, as a project's memory image is read: a file no module names is not the project's (13f).
            string bench = MuxBench.Replace("    initial begin\n", "    logic [7:0] table_ [0:1];\n    initial $readmemh(\"table.hex\", table_);\n    initial begin\n");
            Project project = Import(("mux2.sv", Mux), ("mux2_tb.sv", bench), ("table.hex", "0f\n1e\n")).Project;
            var fields = new JsonSerializerOptions { IncludeFields = true };
            string json = JsonSerializer.Serialize(ProjectRecord.From(project), fields);
            var problems = new List<string>();
            Project again = JsonSerializer.Deserialize<ProjectRecord>(json, fields).ToProject(problems);

            Assert.IsEmpty(problems);
            Assert.AreEqual("mux2_tb", again.Bench);
            Assert.AreEqual(project.Stimulus.End, again.Stimulus.End);
            Assert.AreEqual("table.hex", again.Files.Single().Name);
            Assert.AreEqual("0f\n1e\n", again.Files.Single().Text);
            CollectionAssert.AreEqual(BoardPrinter.Print(project).Select(p => p.Text), BoardPrinter.Print(again).Select(p => p.Text));
        }

        [Test]
        public void AnOlderSave_HasNoTestbenchAndNoFiles()
        {
            var fields = new JsonSerializerOptions { IncludeFields = true };
            string json = JsonSerializer.Serialize(ProjectRecord.From(SampleBoards.Build("half_adder")), fields)
                .Replace(",\"bench\":null", "").Replace("\"files\":[],", "").Replace(",\"files\":[]", "");
            StringAssert.DoesNotContain("bench", json);
            StringAssert.DoesNotContain("files", json);

            var problems = new List<string>();
            Project read = JsonSerializer.Deserialize<ProjectRecord>(json, fields).ToProject(problems);
            Assert.IsEmpty(problems);
            Assert.IsNull(read.Bench);
            Assert.IsEmpty(read.Files);
        }

        [Test]
        public void WhileATestbenchDrivesTheInputs_TheStimulusCannotBeSet()
        {
            Project project = Import(("mux2.sv", Mux), ("mux2_tb.sv", MuxBench)).Project;
            var editor = new BoardEditor(project);
            string why = BoardRules.BenchDrivesTheInputs(project);
            StringAssert.Contains("mux2_tb", why);

            Assert.AreEqual(why, editor.SetInput("a", 0, 10, LogicVec.FromBinary("1")).Refusal, "SET");
            Assert.AreEqual(why, editor.InvertInput("s", 5).Refusal, "invert");
            Assert.IsTrue(StimulusEdits.TrySet(project.Stimulus, "a", 0, 10, LogicVec.FromBinary("1"), out Stimulus edited, out string refusal), refusal);
            Assert.AreEqual(why, editor.SetStimulus(edited).Refusal, "and a stimulus made some other way");
            Assert.AreSame(project, editor.Project);
        }

        [Test]
        public void TheTestbench_KeepsItsPlace_WhenItOrTheDesignIsRenamed_AndTakingItOutFreesTheInputs()
        {
            var editor = new BoardEditor(Import(("mux2.sv", Mux), ("mux2_tb.sv", MuxBench)).Project);

            Assert.IsTrue(editor.RenameModule("mux2_tb", "bench").Done);
            Assert.AreEqual("bench", editor.Project.Bench);
            Assert.IsTrue(editor.RenameModule("mux2", "chooser").Done);
            Assert.AreEqual("chooser", editor.Project.Top);
            StringAssert.Contains("chooser dut (", editor.Project.Find("bench").Source, "the testbench's text says the new name");
            Assert.AreEqual("bench.dut", BoardCompiler.Compile(editor.Project).DesignPath);

            Assert.IsTrue(editor.RemoveModule("bench").Done);
            Assert.IsNull(editor.Project.Bench);
            Assert.IsTrue(editor.SetInput("a", 0, 10, LogicVec.FromBinary("1")).Done, "the inputs are the stimulus's again");
            Assert.IsTrue(editor.Undo() && editor.Undo());
            Assert.AreEqual("bench", editor.Project.Bench);
        }

        [Test]
        public void ATestbenchThatMakesTheDesignInsideAGenerateIf_IsToldWhy()
        {
            const string bench =
                "module mux2_tb;\n" +
                "    logic a, b, s, y;\n" +
                "    if (1) begin : g\n" +
                "        mux2 dut (.a(a), .b(b), .s(s), .y(y));\n" +
                "    end\n" +
                "    initial #10 $stop;\n" +
                "endmodule\n";

            ProjectImport import = Import(("mux2.sv", Mux), ("mux2_tb.sv", bench));

            Assert.That(import.Notes.Any(n => n.IsError && n.Message.Contains("inside a generate if or for")), Notes(import));
        }

        [Test]
        public void AProjectsFiles_AreBareNames_NoTwoAlike_AndNotTheExportsScript()
        {
            Project project = Project.New("top");
            string Why(params string[] names) =>
                BoardRules.Check(project with { Files = names.Select(n => new ProjectFile { Name = n }).ToArray() })
                    .FirstOrDefault(d => d.IsError)?.Message;

            Assert.IsNull(Why("program.hex", "defs.svh", "sizes.inc"));
            StringAssert.Contains("folder", Why("mem/program.hex"));
            StringAssert.Contains("two files", Why("a.hex", "A.HEX"));
            StringAssert.Contains("script", Why("run.do"));
            Assert.IsNull(Why("defines.sv"), "a .sv of nothing but defines may be one of the files");
            StringAssert.Contains("module of the project", Why("top.sv"), "but not one a module is written to");
            StringAssert.Contains("module of the project", Why("bitsorter_cells.sv"));
        }

        [Test]
        public void AMemoryImage_IsReadByTheRun()
        {
            const string rom =
                "module rom_tb;\n" +
                "    logic [7:0] words [0:1];\n" +
                "    initial begin\n" +
                "        $readmemh(\"table.hex\", words);\n" +
                "        #1 $display(\"%h %h\", words[0], words[1]);\n" +
                "        $stop;\n" +
                "    end\n" +
                "endmodule\n";
            Project project = Import(("rom_tb.sv", rom), ("table.hex", "0f\n1e\n")).Project;
            Assert.IsNotNull(project, "a module with no design under it is the design");

            CompiledProject compiled = BoardCompiler.Compile(project);
            Elaboration elaboration = Elaborator.Elaborate(compiled.Tree, compiled.Top);
            var sim = new Simulator(elaboration.Design, null, new SimOptions { Files = ProjectFiles.ForRun(project) });
            sim.RunTo(10);

            CollectionAssert.Contains(sim.History.Messages.Select(m => m.Text), "0f 1e");
        }
    }
}
