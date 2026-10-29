using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BitSorter.Ci;
using BitSorter.Sim;
using BitSorter.SystemVerilog;
using BitSorter.SystemVerilog.Syntax;
using NUnit.Framework;

namespace BitSorter.Board.Tests
{
    /// <summary>
    /// A drawing of code written as code again (Part 15h-4g): every written module of every exercise answer and corpus design,
    /// drawn and written again unedited, does what its code did -- or says why it cannot be written, for a reason the drawing
    /// truly cannot show -- and an edit of a drawing is written into the code.
    /// </summary>
    public class DrawingPrinterTests
    {
        // The reasons a drawing may give for not being written as code: what it does not show, and nothing else.
        private static readonly string[] Reasons =
        {
            "Its ",                          // a box no expression is: a process, a call, a 2-state value, a bus
            "It has a parameter",            // the drawing is of one instance
            "The drawing does not show",     // a delay on an assign, a $display, a starting value
            "It reads ",                     // a signal another scope gives its value
        };

        private static IEnumerable<(string What, Project Project)> Projects()
        {
            foreach (string id in ExerciseFiles.Ids())
            {
                Exercise exercise = ExerciseFiles.Load(id);
                Project solution = ExerciseFiles.Solution(exercise);
                yield return ("exercise " + id, solution.WithModule(exercise.Written(solution)));   // as WRITE IT writes it
            }

            string[] structural = Directory.GetFiles(RepoRoot.Combine("Tools/ci/Oracle/corpus/structural"), "*.sv").OrderBy(f => f, StringComparer.Ordinal).ToArray();
            ProjectImport whole = ProjectImporter.Import(structural.Select(f => new SourceFile(Path.GetFileName(f), File.ReadAllText(f))).ToList());
            Assert.IsTrue(whole.Succeeded, "sanity: the structural corpus imports whole");
            yield return ("structural", whole.Project);

            foreach (string path in Directory.GetFiles(RepoRoot.Combine("Tools/ci/Oracle/corpus/behavioural"), "*.sv").OrderBy(f => f, StringComparer.Ordinal))
            {
                ProjectImport import = ProjectImporter.Import(new[] { new SourceFile(Path.GetFileName(path), File.ReadAllText(path)) });
                if (import.Succeeded)
                    yield return ("behavioural " + Path.GetFileName(path), import.Project);
            }
        }

        /// <summary>Each written module of <paramref name="project"/>, at its first instance: its netlist, its design, its drawing.</summary>
        private static IEnumerable<(string Module, RtlNetlist Netlist, Design Design, RtlDrawing Drawing)> WrittenModules(Project project)
        {
            CompiledProject compiled = BoardCompiler.Compile(project);
            Assert.IsTrue(compiled.Succeeded);
            Elaboration e = Elaborator.Elaborate(compiled.Tree, compiled.Top);
            Assert.IsFalse(e.HasErrors);
            var seen = new HashSet<string>();
            foreach (ScopeInfo scope in e.Design.Scopes)
            {
                if (scope.Kind != ScopeKind.Top && scope.Kind != ScopeKind.Instance)
                    continue;
                Module module = project.Find(scope.Module);
                if (module == null || !module.IsWritten || !seen.Add(scope.Module))
                    continue;
                RtlNetlist netlist = RtlExtractor.Of(e.Design, scope.Path);
                yield return (scope.Module, netlist, e.Design, RtlDrawing.Of(project, netlist, scope.Module));
            }
        }

        [Test]
        public void EveryWrittenModulesDrawing_WrittenAgain_DoesWhatItsCodeDid_OrSaysWhatItDoesNotShow()
        {
            int written = 0, refused = 0;
            foreach ((string what, Project project) in Projects())
                foreach ((string module, RtlNetlist netlist, Design design, RtlDrawing drawing) in WrittenModules(project))
                {
                    string why = DrawingPrinter.Differs(project, netlist, design, drawing);
                    if (why == null)
                    {
                        written++;
                        continue;
                    }
                    refused++;
                    TestContext.Out.WriteLine($"{what} {module}: {why}");
                    Assert.IsTrue(Reasons.Any(r => why.StartsWith(r, StringComparison.Ordinal)), $"{what} {module}: {why}");
                }
            TestContext.Out.WriteLine($"{written} written again as they were, {refused} refused");
            // 36 on the day it was built (2026-10-29); 35 since the review of #117 found alu_slice's instance given a parameter,
            // which the drawing had dropped.
            Assert.GreaterOrEqual(written, 35, "as many as on the day, every exercise's among them");
        }

        private static (Project Project, RtlNetlist Netlist, Design Design, RtlDrawing Drawing) Drawn(string source)
        {
            // Written, whatever it is: an import would draw code of gates as a board.
            int at = source.IndexOf("module ", StringComparison.Ordinal) + "module ".Length;
            string name = source.Substring(at, source.IndexOfAny(new[] { ' ', '(' }, at) - at);
            var editor = new BoardEditor(Project.New("scratch"));
            Assert.IsTrue(editor.AddWrittenModule(name, source).Done);
            Project project = editor.Project with { Top = name };
            (string _, RtlNetlist netlist, Design design, RtlDrawing drawing) = WrittenModules(project).First();
            return (project, netlist, design, drawing);
        }

        private static string Written((Project Project, RtlNetlist Netlist, Design Design, RtlDrawing Drawing) d)
        {
            string differs = DrawingPrinter.Differs(d.Project, d.Netlist, d.Design, d.Drawing);
            Assert.IsNull(differs, "written again, it does what it did: " + differs + Environment.NewLine + DrawingPrinter.Print(d.Project, d.Drawing, d.Drawing.Module, out _));
            string source = DrawingPrinter.Print(d.Project, d.Drawing, d.Drawing.Module, out string why);
            Assert.IsNotNull(source, why);
            return source;
        }

        [Test]
        public void ARegisterWithAnActiveLowReset_KeepsItsEdgesAndItsResetValue()
        {
            string source = Written(Drawn(
                "module counter (input logic clk, input logic rst_n, output logic [3:0] n);\n" +
                "    always_ff @(posedge clk or negedge rst_n)\n" +
                "        if (!rst_n) n <= 4'd9;\n" +
                "        else n <= n + 4'd1;\n" +
                "endmodule\n"));
            StringAssert.Contains("negedge rst_n", source);
            StringAssert.Contains("posedge clk", source);
        }

        [Test]
        public void AGatesDelay_AndACellsDelays_AreKept()
        {
            string source = Written(Drawn(
                "module pair (input logic a, b, output logic y, z);\n" +
                "    and #(2, 3) g1 (y, a, b);\n" +
                "    or #4 g2 (z, a, b);\n" +
                "endmodule\n"));
            StringAssert.Contains("Tpdlh(2)", source);
            StringAssert.Contains("Tpdhl(3)", source);
            StringAssert.Contains("Tpdlh(4)", source);
        }

        [Test]
        public void ACaseOnAWideSubject_IsTheChoiceItIs()
        {
            Written(Drawn(
                "module pick (input logic [3:0] sel, input logic [7:0] a, b, output logic [7:0] y);\n" +
                "    always_comb\n" +
                "        case (sel)\n" +
                "            4'd0, 4'd5: y = a;\n" +
                "            4'd9: y = b;\n" +
                "            4'd12: y = a ^ b;\n" +
                "            default: y = 8'h00;\n" +
                "        endcase\n" +
                "endmodule\n"));
        }

        [Test]
        public void SlicesJoinsAndASignedComparison_AreWrittenAsTheyAre()
        {
            Written(Drawn(
                "module mix (input logic [7:0] a, b, output logic [3:0] hi, output logic [9:0] wide, output logic less);\n" +
                "    assign hi = a[7:4] ^ b[3:0];\n" +
                "    assign wide = {a[1:0], b};\n" +
                "    assign less = $signed(a) < $signed(b);\n" +
                "endmodule\n"));
        }

        [Test]
        public void TheCommentsOverAModule_AndItsPortsOrder_AreKept()
        {
            string source = Written(Drawn(
                "// A chooser, written for this test.\n" +
                "// Its second line.\n" +
                "module choose (input logic s, output logic y, input logic a, b);\n" +
                "    assign y = s ? b : a;   // this comment goes\n" +
                "endmodule\n"));
            StringAssert.StartsWith("// A chooser, written for this test.\n// Its second line.\n", source);
            Assert.Less(source.IndexOf("input logic s", StringComparison.Ordinal), source.IndexOf("output logic y", StringComparison.Ordinal));
            Assert.Less(source.IndexOf("output logic y", StringComparison.Ordinal), source.IndexOf("input logic a", StringComparison.Ordinal));
        }

        [Test]
        public void AnAssignWithADelay_IsNotWritten_AndSaysSo()
        {
            var d = Drawn("module late (input logic a, output logic y);\n    assign #2 y = ~a;\nendmodule\n");
            StringAssert.StartsWith("The drawing does not show the delay on the assign to y", DrawingPrinter.Differs(d.Project, d.Netlist, d.Design, d.Drawing));
        }

        // -----------------------------------------------------------------
        // The review of #117
        // -----------------------------------------------------------------

        private const string Counter =
            "module cnt (input logic clk, rst_n, en, output logic [3:0] q);\n" +
            "    always_ff @(posedge clk or negedge rst_n)\n" +
            "        if (!rst_n) q <= 4'd0;\n" +
            "        else if (en) q <= q + 4'd1;\n" +
            "endmodule\n";

        private const string CounterBench =
            "module tb;\n" +
            "    logic clk = 1'b0, rst_n = 1'b1, en = 1'b1;\n" +
            "    logic [3:0] q;\n" +
            "    cnt dut (.clk(clk), .rst_n(rst_n), .en(en), .q(q));\n" +
            "    always #5 clk = ~clk;\n" +
            "    initial begin\n" +
            "        #17 rst_n = 1'b0;\n" +
            "        #1 $display(\"%0d\", q);\n" +
            "        #1 rst_n = 1'b1;\n" +
            "        #20 $display(\"%0d\", q);\n" +
            "        $finish;\n" +
            "    end\n" +
            "endmodule\n";

        private static string[] Printed(string design, string bench)
        {
            var files = new MemorySourceFiles();
            SyntaxTree tree = SyntaxReader.ReadText(design + bench, "run.sv", files);
            Elaboration e = Elaborator.Elaborate(tree, "tb");
            Assert.IsFalse(e.HasErrors, string.Join("\n", e.Diagnostics));
            var sim = new Simulator(e.Design, null, null);
            sim.RunTo(200);
            return sim.History.Messages.Where(m => m.Severity == Severity.Info && !m.Text.StartsWith("$")).Select(m => m.Text).ToArray();
        }

        /// <summary>
        /// An asynchronous reset is put in inside the process, as written: on its edge the process reads the reset itself. Written
        /// as d alone, worked out by an assign it woke before, the reset never applied and the counter stayed x (the review of #117).
        /// </summary>
        [Test]
        public void AnAsynchronousReset_IsPutInInsideItsProcess_AndApplies()
        {
            string source = Written(Drawn(Counter));
            StringAssert.Contains("if (!rst_n)", source);
            CollectionAssert.AreEqual(new[] { "0", "2" }, Printed(Counter, CounterBench), "sanity: the code");
            CollectionAssert.AreEqual(new[] { "0", "2" }, Printed(source, CounterBench), "the code written from its drawing");
        }

        /// <summary>The code written from a drawing is drawn and written again: a register with an asynchronous reset is still one.</summary>
        [Test]
        public void TheCodeWrittenFromADrawing_IsWrittenAgain_ItsAsynchronousResetStillARegister()
        {
            string once = Written(Drawn(Counter));
            string twice = Written(Drawn(once));
            StringAssert.Contains("negedge rst_n", twice);
        }

        [Test]
        public void AResetNamedFirst_IsTheSameRegister()
        {
            Written(Drawn(
                "module r (input logic clk, rst_n, input logic [1:0] d, output logic [1:0] q);\n" +
                "    always_ff @(negedge rst_n or posedge clk)\n" +
                "        if (!rst_n) q <= 2'b10;\n" +
                "        else q <= d;\n" +
                "endmodule\n"));
        }

        /// <summary>A sum wider than its operands is a widening drawn as a wire, written as a box of its own with a name (the review of #117).</summary>
        [Test]
        public void ASumWithItsCarry_IsWritten()
        {
            Written(Drawn(
                "module add (input logic [3:0] a, b, input logic cin, output logic [3:0] s, output logic cout);\n" +
                "    assign {cout, s} = a + b + cin;\n" +
                "endmodule\n"));
        }

        [Test]
        public void AChildGivenParameters_IsNotWritten_AndSaysSo()
        {
            var editor = new BoardEditor(Project.New("scratch"));
            Assert.IsTrue(editor.AddWrittenModule("child", "module child #(parameter INVERT = 0) (input logic a, output logic y);\n    assign y = INVERT ? ~a : a;\nendmodule\n").Done);
            Assert.IsTrue(editor.AddWrittenModule("top1", "module top1 (input logic x, output logic z);\n    child #(.INVERT(1)) c (.a(x), .y(z));\nendmodule\n").Done);
            Project project = editor.Project with { Top = "top1" };
            var (module, netlist, design, drawing) = WrittenModules(project).First(m => m.Module == "top1");
            StringAssert.StartsWith("Its instance c is given parameters", DrawingPrinter.Differs(project, netlist, design, drawing));
        }

        [Test]
        public void AnInoutPort_IsNotWritten_AndSaysSo()
        {
            var d = Drawn("module dev (input logic en, a, inout wire p, output logic y);\n    assign p = en ? a : 1'bz;\n    assign y = p;\nendmodule\n");
            StringAssert.StartsWith("Its inout port p", DrawingPrinter.Differs(d.Project, d.Netlist, d.Design, d.Drawing));
        }

        /// <summary>Names the code gives are its own: an instance u keeps its name beside parts the code names nothing, and d and d_ stay two.</summary>
        [Test]
        public void TheNamesTheCodeGives_AreKept()
        {
            var editor = new BoardEditor(Project.New("scratch"));
            Assert.IsTrue(editor.AddWrittenModule("pass", "module pass (input logic a, output logic y);\n    assign y = a;\nendmodule\n").Done);
            Assert.IsTrue(editor.AddWrittenModule("named",
                "module named (input logic x, w, output logic z, output logic d, d_);\n" +
                "    logic t;\n" +
                "    assign t = x & w;\n" +
                "    pass u (.a(t), .y(z));\n" +
                "    assign d = x | w;\n" +
                "    assign d_ = x ^ w;\n" +
                "endmodule\n").Done);
            Project project = editor.Project with { Top = "named" };
            var (module, netlist, design, drawing) = WrittenModules(project).First(m => m.Module == "named");
            Assert.IsNull(DrawingPrinter.Differs(project, netlist, design, drawing));
            string source = DrawingPrinter.Print(project, drawing, drawing.Module, out string why);
            StringAssert.Contains("pass u (", source, why);
            StringAssert.Contains("d_", source);
        }

        /// <summary>A generate loop's flops each take their own bit of the signal's starting value (the review of #117).</summary>
        [Test]
        public void AGenerateLoopsFlops_EachStartAtTheirOwnBit()
        {
            Written(Drawn(
                "module shift (input logic clk, input logic [3:0] d, output logic [3:0] q);\n" +
                "    logic [3:0] r = 4'b1010;\n" +
                "    genvar i;\n" +
                "    for (i = 0; i < 4; i++) begin : g\n" +
                "        always_ff @(posedge clk) r[i] <= d[i];\n" +
                "    end\n" +
                "    assign q = r;\n" +
                "endmodule\n"));
        }

        /// <summary>An edit of the drawing -- its OR taken out, an AND put in and wired -- is the code, and the code does it.</summary>
        [Test]
        public void AnEditedDrawing_IsWrittenAsTheCodeOfWhatItNowIs()
        {
            var d = Drawn("module both (input logic a, b, output logic y);\n    assign y = a | b;\nendmodule\n");
            var editor = new BoardEditor(d.Drawing.Project);
            Module drawn = d.Drawing.Module;
            int or = drawn.Parts.OfType<OperatorPart>().Single().Id;
            EditResult removed = editor.RemoveParts(drawn.Name, new[] { or });
            Assert.IsTrue(removed.Done, removed.Refusal);
            EditResult and = editor.Add(drawn.Name, new GatePart { Kind = GateKind.And }, new Placement(4, 0));
            Assert.IsTrue(and.Done, and.Refusal);
            int a = drawn.Parts.OfType<PortPart>().Single(p => p.Name == "a").Id, b = drawn.Parts.OfType<PortPart>().Single(p => p.Name == "b").Id;
            int y = drawn.Parts.OfType<PortPart>().Single(p => p.Name == "y").Id;
            Assert.IsTrue(editor.Connect(drawn.Name, Endpoint.At(a, "out"), Endpoint.At(and.Id, "a")).Done, editor.Connect(drawn.Name, Endpoint.At(a, "out"), Endpoint.At(and.Id, "a")).Refusal);
            Assert.IsTrue(editor.Connect(drawn.Name, Endpoint.At(b, "out"), Endpoint.At(and.Id, "b")).Done, editor.Connect(drawn.Name, Endpoint.At(b, "out"), Endpoint.At(and.Id, "b")).Refusal);
            Assert.IsTrue(editor.Connect(drawn.Name, Endpoint.At(and.Id, "y"), Endpoint.At(y, "in")).Done, editor.Connect(drawn.Name, Endpoint.At(and.Id, "y"), Endpoint.At(y, "in")).Refusal);

            string source = DrawingPrinter.Print(d.Project, d.Drawing, editor.Project.Find(drawn.Name), out string why);
            Assert.IsNotNull(source, why);
            StringAssert.DoesNotContain("|", source);

            var written = new BoardEditor(d.Project);
            Assert.IsTrue(written.SetSource("both", source).Done);
            CompiledProject compiled = BoardCompiler.Compile(written.Project);
            Elaboration e = Elaborator.Elaborate(compiled.Tree, compiled.Top);
            Assert.IsFalse(e.HasErrors);
            RtlNetlist netlist = RtlExtractor.Of(e.Design, e.Design.Scopes[0].Path);
            foreach ((ulong av, ulong bv) in new[] { (0UL, 0UL), (0UL, 1UL), (1UL, 0UL), (1UL, 1UL) })
            {
                var signals = e.Design.Signals.Select(s => s.Name == "both.a" ? LogicVec.FromUInt64(av, 1) : s.Name == "both.b" ? LogicVec.FromUInt64(bv, 1) : LogicVec.AllX(s.Width)).ToArray();
                LogicVec[] nets = RtlEvaluator.Evaluate(netlist, signals);
                Assert.IsTrue(nets[netlist.Outputs[0]].TryGetUInt64(out ulong value));
                Assert.AreEqual(av & bv, value, $"y for a={av} b={bv}: the AND drawn in");
            }
        }

        /// <summary>A gate placed and not yet wired leaves its pins open, written and drawn again: the next wire to it is no second driver.</summary>
        [Test]
        public void AGatePlacedAndNotWired_IsDrawnAgainWithItsPinsOpen()
        {
            var d = Drawn("module one (input logic a, output logic y);\n    assign y = a;\nendmodule\n");
            var editor = new BoardEditor(d.Drawing.Project);
            Assert.IsTrue(editor.Add(d.Drawing.Module.Name, new GatePart { Kind = GateKind.Not, Inputs = 1 }, new Placement(4, 2)).Done);
            string source = DrawingPrinter.Print(d.Project, d.Drawing, editor.Project.Find(d.Drawing.Module.Name), out string why);
            Assert.IsNotNull(source, why);

            var written = new BoardEditor(d.Project);
            Assert.IsTrue(written.SetSource("one", source).Done);
            CompiledProject compiled = BoardCompiler.Compile(written.Project);
            Elaboration e = Elaborator.Elaborate(compiled.Tree, compiled.Top);
            RtlDrawing again = RtlDrawing.Of(written.Project, RtlExtractor.Of(e.Design, e.Design.Scopes[0].Path), "one");
            GatePart not = again.Module.Parts.OfType<GatePart>().Single();
            Assert.IsFalse(again.Module.Wires.Any(w => !w.To.IsTap && w.To.Pin.Part == not.Id), "nothing drawn into its input");
            Assert.IsFalse(again.Module.Parts.OfType<BoxPart>().Any(b => b.Undriven || b.Label == "g1_a"), "no box standing in for its open pin");
        }
    }
}
