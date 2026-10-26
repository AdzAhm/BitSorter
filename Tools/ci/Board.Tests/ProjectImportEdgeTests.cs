using System.Collections.Generic;
using System.Linq;
using BitSorter.Sim;
using BitSorter.SystemVerilog;
using NUnit.Framework;

namespace BitSorter.Board.Tests
{
    /// <summary>
    /// The import's harder cases (Part 13b), each first found by the review of its first version: testbenches that run out
    /// rather than stop, that look inside the design or make more than one module, and files cut into modules of their own
    /// -- conditionals, guards, comments, includes that declare, a fault in an included file, and names in any case.
    /// </summary>
    public class ProjectImportEdgeTests
    {
        private const string Mux =
            "module mux2 (input logic a, input logic b, input logic s, output logic y);\n" +
            "    logic s_n, n0, n1;\n" +
            "    INV   #(.Tpdlh(2), .Tpdhl(1)) g0 (.A(s), .Z(s_n));\n" +
            "    NAND2 #(.Tpdlh(3), .Tpdhl(2)) g1 (.A(a), .B(s_n), .Z(n0));\n" +
            "    NAND2 #(.Tpdlh(3), .Tpdhl(2)) g2 (.A(b), .B(s), .Z(n1));\n" +
            "    NAND2 #(.Tpdlh(3), .Tpdhl(2)) g3 (.A(n0), .B(n1), .Z(y));\n" +
            "endmodule\n";

        private static ProjectImport Import(params (string Path, string Text)[] files) =>
            ProjectImporter.Import(files.Select(f => new SourceFile(f.Path, f.Text)).ToList());

        private static string Notes(ProjectImport import) => string.Join("\n", import.Notes);

        private static int WidthOf(Project project, string module, string port) =>
            WrittenModules.PortsOf(project, project.Find(module)).Single(p => p.Name == port).Width;

        // -----------------------------------------------------------------
        // Testbenches
        // -----------------------------------------------------------------

        [Test]
        public void ATestbenchThatRunsOut_IsShownToItsLastChange_AndNotSaidToRunOn()
        {
            const string bench =
                "module mux2_tb;\n" +
                "    logic a, b, s, y;\n" +
                "    mux2 dut (.a(a), .b(b), .s(s), .y(y));\n" +
                "    initial begin a = 0; b = 1; s = 0; #10 s = 1; end\n" +
                "endmodule\n";

            ProjectImport import = Import(("mux2.sv", Mux), ("mux2_tb.sv", bench));

            Assert.IsTrue(import.Succeeded, Notes(import));
            Assert.That(import.Project.Stimulus.End, Is.InRange(13, 20), "its last change, at 13 when y has fallen, and a tenth more");
            Assert.IsFalse(import.Notes.Any(n => n.Message.Contains("had not stopped")), Notes(import));
        }

        [Test]
        public void ATestbenchThatLooksInsideTheDesign_HasItWritten_WithTheNamesItLooksFor()
        {
            const string bench =
                "module mux2_tb;\n" +
                "    logic a, b, s, y, peek;\n" +
                "    mux2 dut (.a(a), .b(b), .s(s), .y(y));\n" +
                "    assign peek = dut.s_n;\n" +
                "    initial begin a = 0; b = 1; s = 0; #10 s = 1; #10 $stop; end\n" +
                "endmodule\n";

            ProjectImport import = Import(("mux2.sv", Mux), ("mux2_tb.sv", bench));

            Assert.IsTrue(import.Succeeded, Notes(import));
            Assert.IsTrue(import.Project.Find("mux2").IsWritten, "written, so s_n is there under its own name");
            Assert.IsEmpty(import.Notes.Where(n => n.IsError), Notes(import));
        }

        [Test]
        public void ATestbenchThatMakesSeveralModules_TestsTheOneItCallsDut_OrElseTheOneWithMostPorts()
        {
            const string clock = "module clk_gen (output logic clk);\n    initial clk = 0;\n    always #5 clk = ~clk;\nendmodule\n";
            const string counter =
                "module cnt (input logic clk, input logic rst, output logic [3:0] q);\n" +
                "    always_ff @(posedge clk) q <= rst ? 4'd0 : q + 4'd1;\n" +
                "endmodule\n";
            string Bench(string instance) =>
                "module tb;\n" +
                "    logic clk, rst; logic [3:0] q;\n" +
                "    clk_gen cg (.clk(clk));\n" +
                "    cnt " + instance + " (.clk(clk), .rst(rst), .q(q));\n" +
                "    initial begin rst = 1; #12 rst = 0; #50 $stop; end\n" +
                "endmodule\n";

            ProjectImport named = Import(("clk.sv", clock), ("cnt.sv", counter), ("tb.sv", Bench("dut")));
            ProjectImport unnamed = Import(("clk.sv", clock), ("cnt.sv", counter), ("tb.sv", Bench("c")));

            Assert.AreEqual("cnt", named.Project?.Top, Notes(named));
            Assert.AreEqual("tb.dut", BoardCompiler.Compile(named.Project).DesignPath);
            Assert.AreEqual("cnt", unnamed.Project?.Top, "more ports than the clock generator: " + Notes(unnamed));
            Assert.That(named.Notes.Any(n => n.Message.Contains("cnt is taken as the design")), Notes(named));
        }

        // -----------------------------------------------------------------
        // Files cut into modules
        // -----------------------------------------------------------------

        [Test]
        public void ConditionalsAboveAModule_AreKeptAsWritten_SoTheyStillChoose()
        {
            const string text =
                "`ifndef NO_X\n" +
                "`define W 8\n" +
                "`else\n" +
                "`define W 4\n" +
                "`endif\n" +
                "module m (input logic [`W-1:0] a, output logic y);\n" +
                "    always_comb y = ^a;\n" +
                "endmodule\n";

            ProjectImport import = Import(("m.sv", text));

            Assert.IsTrue(import.Succeeded, Notes(import));
            Assert.AreEqual(8, WidthOf(import.Project, "m", "a"), "NO_X is not defined, so W is 8");
        }

        [Test]
        public void AnEndmoduleAfterCode_EndsItsModule()
        {
            const string library =
                "`ifndef LIB\n" +
                "`define LIB\n" +
                "module inv (input logic a, output logic y); always_comb y = ~a; endmodule\n" +
                "`define W 4\n" +
                "module wide (input logic [`W-1:0] x, output logic y); always_comb y = ^x; endmodule\n" +
                "`endif\n";

            ProjectImport import = Import(("lib.sv", library));

            Assert.IsEmpty(import.Notes.Where(n => n.IsError), Notes(import));
            Assert.AreEqual(4, WidthOf(import.Project, "wide", "x"), "the define between them is carried to the second");
            StringAssert.DoesNotContain("module wide", import.Project.Find("inv").Source);
        }

        [Test]
        public void AFileWhoseConditionalsChooseBetweenModules_SaysSo()
        {
            const string text =
                "`ifdef FAST\n" +
                "module add (input logic a, input logic b, output logic y);\n    always_comb y = a | b;\nendmodule\n" +
                "`else\n" +
                "module add (input logic a, input logic b, output logic y);\n    always_comb y = a ^ b;\nendmodule\n" +
                "`endif\n";

            ProjectImport import = Import(("add.sv", text));

            Assert.That(import.Notes.Any(n => n.IsError && n.Message.Contains("chooses between its modules")), Notes(import));
        }

        [Test]
        public void ACommentedOutDirective_BetweenModules_StaysOut()
        {
            const string text =
                "module a (input logic x, output logic y);\n    always_comb y = x;\nendmodule\n" +
                "/* disabled for now:\n" +
                "`include \"nothere.svh\"\n" +
                "`define W 99\n" +
                "*/\n" +
                "module b (input logic [3:0] x, output logic y);\n    a u (.x(x[0]), .y(y));\n    initial $display(\"b\");\nendmodule\n";

            ProjectImport import = Import(("m.sv", text));

            Assert.IsTrue(import.Succeeded, Notes(import));
            StringAssert.DoesNotContain("`define W 99\n`", import.Project.Find("b").Source);
            Assert.IsEmpty(import.Notes.Where(n => n.IsError), Notes(import));
        }

        [Test]
        public void AHeaderOfMacrosOnly_StaysAboveTheModule_EvenWithALongDefine()
        {
            const string defs = "`define W 8\n`define MAX(a,b) \\\n   ((a) > (b) ? (a) : (b))\n";
            const string text = "`include \"defs.svh\"\nmodule m (input logic [`W-1:0] a, output logic y);\n    always_comb y = ^a;\nendmodule\n";

            ProjectImport import = Import(("m.sv", text), ("defs.svh", defs));

            Assert.IsTrue(import.Succeeded, Notes(import));
            Assert.AreEqual(8, WidthOf(import.Project, "m", "a"));
            StringAssert.StartsWith("`include \"defs.svh\"", import.Project.Find("m").Source);
        }

        [Test]
        public void AHeaderThatIncludesDeclarations_GoesInsideTheModuleToo()
        {
            const string text =
                "`include \"common.svh\"\n" +
                "module m (input logic a, output logic y);\n" +
                "    always_comb y = a & (K == 1);\n" +
                "endmodule\n";

            ProjectImport import = Import(("m.sv", text), ("consts.svh", "localparam int K = 1;\n"), ("common.svh", "`include \"consts.svh\"\n"));

            Assert.IsTrue(import.Succeeded, Notes(import));
            Assert.IsEmpty(import.Notes.Where(n => n.IsError), Notes(import));
        }

        [Test]
        public void AHeaderAModulesPortsUse_CannotGoInside_AndTheImportSaysWhy()
        {
            const string text =
                "`include \"defs.svh\"\n" +
                "module m (input logic [W-1:0] a, output logic y);\n" +
                "    always_comb y = ^a;\n" +
                "endmodule\n";

            ProjectImport import = Import(("m.sv", text), ("defs.svh", "localparam int W = 8;\n"));

            Assert.That(import.Notes.Any(n => n.IsError && n.Message.Contains("m's header uses W")), Notes(import));
        }

        [Test]
        public void ADeclaringHeaderBeforeTwoModules_IsIncludedOnceInEach_AndItsGuardGoes()
        {
            const string parameters = "`ifndef PARAMS_SVH // the guard\n`define PARAMS_SVH\nlocalparam int N = 4;\n`endif\n";
            const string text =
                "`include \"params.svh\"\n" +
                "module a (input logic [3:0] x, output logic y);\n    always_comb y = (x == N);\nendmodule\n\n" +
                "`include \"params.svh\"\n" +
                "module b (input logic [3:0] x, output logic y);\n    a u (.x(x), .y(y));\n    initial if (N != 4) $display(\"no\");\nendmodule\n";

            ProjectImport import = Import(("m.sv", text), ("params.svh", parameters));

            Assert.IsTrue(import.Succeeded, Notes(import));
            Assert.IsEmpty(import.Notes.Where(n => n.IsError), Notes(import));
            Assert.AreEqual(1, import.Project.Find("b").Source.Split('\n').Count(l => l.Contains("`include")), "once, though the file says it twice");
            StringAssert.DoesNotContain("ifndef", import.Project.Files.Single().Text, "a guard with a comment after it is still a guard");
        }

        [Test]
        public void TwoGuardedSections_KeepTheirGuards()
        {
            const string defs = "`ifndef A_SVH\n`define A_SVH\nlocalparam int A = 1;\n`endif\n`ifndef B_SVH\n`define B_SVH\nlocalparam int B = 2;\n`endif\n";
            const string text = "module m (input logic x, output logic y);\n    `include \"defs.svh\"\n    always_comb y = x & (A == 1) & (B == 2);\nendmodule\n";

            ProjectImport import = Import(("m.sv", text), ("defs.svh", defs));

            Assert.IsTrue(import.Succeeded, Notes(import));
            Assert.AreEqual(defs, import.Project.Files.Single().Text, "no one guard is round the whole file, so nothing is taken out");
        }

        // -----------------------------------------------------------------
        // Files
        // -----------------------------------------------------------------

        [Test]
        public void ASvFileOfDefinesOnly_IsKeptAsAFile()
        {
            const string text = "`include \"defs.sv\"\nmodule m (input logic [`W-1:0] a, output logic y);\n    always_comb y = ^a;\nendmodule\n";

            ProjectImport import = Import(("m.sv", text), ("defs.sv", "`define W 8\n"));

            Assert.IsTrue(import.Succeeded, Notes(import));
            Assert.AreEqual("defs.sv", import.Project.Files.Single().Name);
            Assert.AreEqual(8, WidthOf(import.Project, "m", "a"));
        }

        [Test]
        public void ATimescaleInAnIncludedFile_SetsTheUnit_AndIsTakenOutOfIt()
        {
            const string text = "`include \"defs.svh\"\nmodule m (input logic [`W-1:0] a, output logic y);\n    always_comb y = ^a;\nendmodule\n";

            ProjectImport import = Import(("m.sv", text), ("defs.svh", "`timescale 1ps/1ps\n`define W 8\n"));

            Assert.IsTrue(import.Succeeded, Notes(import));
            Assert.AreEqual("ps", import.Project.Settings.TimeUnit);
            StringAssert.DoesNotContain("timescale", import.Project.Files.Single().Text);
        }

        [Test]
        public void AnIncludeThroughAFolder_IsNamedByTheFilesBareName()
        {
            const string text = "module m (input logic a, output logic y);\n    `include \"../inc/defs.svh\"\n    always_comb y = a & (K == 1);\nendmodule\n";

            ProjectImport import = Import(("src/m.sv", text), ("inc/defs.svh", "localparam int K = 1;\n"));

            Assert.IsTrue(import.Succeeded, Notes(import));
            StringAssert.Contains("`include \"defs.svh\"", import.Project.Find("m").Source);
        }

        [Test]
        public void AFileIncludedUnderAnotherCase_IsTheSameFile()
        {
            const string library = "module inv (input logic a, output logic y);\n    always_comb y = ~a;\nendmodule\n";
            const string top = "`include \"lib.sv\"\nmodule top (input logic a, output logic y);\n    inv u (.a(a), .y(y));\n    initial $display(\"x\");\nendmodule\n";

            ProjectImport import = Import(("Lib.sv", library), ("top.sv", top));

            Assert.IsTrue(import.Succeeded, Notes(import));
            Assert.IsEmpty(import.Notes.Where(n => n.IsError), Notes(import));
        }

        [Test]
        public void AnIncludeInAnotherCase_IsReadAgainWhenTheFileChanges()
        {
            var editor = new BoardEditor(Project.New("top") with
            {
                Files = new[] { new ProjectFile { Name = "defs.svh", Text = "`define W 8\n" } },
            });
            Assert.IsTrue(editor.AddWrittenModule("m",
                "`include \"DEFS.svh\"\nmodule m (input logic [`W-1:0] a, output logic [`W-1:0] y);\n    assign y = a;\nendmodule\n").Done);
            Project eight = editor.Project;
            Project four = eight with { Files = new[] { new ProjectFile { Name = "defs.svh", Text = "`define W 4\n" } } };

            Assert.AreEqual(8, WidthOf(eight, "m", "a"));
            Assert.AreEqual(4, WidthOf(four, "m", "a"), "the file is named in the text whatever its case, so the reading is kept for its text");
        }

        [Test]
        public void AFaultInAnIncludedFile_IsMarkedAtTheInclude_AndSaysWhere()
        {
            var project = Project.New("top") with
            {
                Files = new[] { new ProjectFile { Name = "types.svh", Text = "// 1\n// 2\n// 3\n// 4\n// 5\ntypedef enum logic [1:0] {A, B} t_e;\n" } },
            };
            project = project.WithModule(new Module
            {
                Name = "m",
                Source = "`include \"types.svh\"\nmodule m (input logic a, output logic y);\n    assign y = a;\nendmodule\n",
            });

            BoardDiagnostic fault = BoardRules.Check(project).First(d => d.IsError && d.Module == "m");

            Assert.AreEqual(1, fault.Line, "at the include, a line the module's text has");
            StringAssert.StartsWith("In types.svh, line 6:", fault.Message);
        }

        [Test]
        public void AMemoryImage_IsFoundWhateverTheCaseItIsNamedIn()
        {
            const string rom =
                "module rom_tb;\n" +
                "    logic [7:0] words [0:0];\n" +
                "    initial begin $readmemh(\"ROM.hex\", words); #1 $display(\"%h\", words[0]); $stop; end\n" +
                "endmodule\n";
            Project project = Import(("rom_tb.sv", rom), ("rom.hex", "5a\n")).Project;

            CompiledProject compiled = BoardCompiler.Compile(project);
            var sim = new Simulator(Elaborator.Elaborate(compiled.Tree, compiled.Top).Design, null, new SimOptions { Files = ProjectFiles.ForRun(project) });
            sim.RunTo(10);

            CollectionAssert.Contains(sim.History.Messages.Select(m => m.Text), "5a");
        }

        /// <summary>
        /// A course's project folder holds more than its code -- the handout's PDF, a zip, the simulator's waveform logs -- and
        /// a folder imported whole kept each as one of the project's files, read as text: binary in the save, and in every
        /// export for ModelSim (Part 13f). A file no module names, by an include or as a file it reads, is left out, said so.
        /// </summary>
        [Test]
        public void AFileNoModuleNames_IsLeftOut_AndOneAModuleReads_IsKept()
        {
            const string bench =
                "module rom_tb;\n" +
                "    logic [7:0] words [0:0];\n" +
                "    initial begin $readmemh(\"rom.hex\", words); #1 $display(\"%h\", words[0]); $stop; end\n" +
                "endmodule\n";
            ProjectImport import = Import(("rom_tb.sv", bench), ("rom.hex", "5a\n"), ("handout.pdf", "%PDF-1.7\n\u0000\u0001binary"),
                ("vsim.wlf", "\u0000\u0000LGWF"), ("notes.txt", "remember the reset\n"));

            Assert.IsTrue(import.Succeeded, Notes(import));
            CollectionAssert.AreEqual(new[] { "rom.hex" }, import.Project.Files.Select(f => f.Name), "only the image the bench reads");
            foreach (string left in new[] { "handout.pdf", "vsim.wlf", "notes.txt" })
                StringAssert.Contains("so it was left out", import.Notes.Single(n => n.File == left).Message, left);
        }
    }
}
