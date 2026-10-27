using System.Collections.Generic;
using System.Linq;
using BitSorter.Sim;
using BitSorter.SystemVerilog.Syntax;
using NUnit.Framework;

namespace BitSorter.Board.Tests
{
    /// <summary>
    /// Written code held to a gate library (Part 14g, Ahmad's choice: gates only): every gate one of the library's cells at
    /// its delays -- a primitive or the cell's module, a cell written without delays taking the library's -- no logic written
    /// with operators, a register only as an always_ff of plain writes, no constant where none is taken; each fault an
    /// error at its line. The testbench is not held. Setting a library over code that breaks it is refused, as it is over a
    /// drawing that does.
    /// </summary>
    public class LibraryWrittenTests
    {
        /// <summary>NAND2 and INV at rise 2, fall 3, and every other cell of BitSorter's at 1.</summary>
        private static GateLibrary Library(bool noConstants = false)
        {
            GateLibrary library = GateLibrary.CourseCells(1) with { NoConstants = noConstants };
            library = library.Retimed(library.CellFor(GateKind.Nand, 2), 2, 3);
            return library.Retimed(library.CellFor(GateKind.Not, 1), 2, 3);
        }

        /// <summary>A project whose written module <c>m</c> is <paramref name="body"/>, with inputs a, b, c, clk, rst_n and outputs y, q.</summary>
        private static Project With(string body)
        {
            var editor = new BoardEditor(Project.New("top"));
            string source = "module m (\n    input  logic a, b, c, clk, rst_n,\n    input  logic [3:0] v,\n    input  logic [1:0] s,\n    output logic y, q\n);\n" + body + "\nendmodule\n";
            Assert.IsTrue(editor.AddWrittenModule("m", source).Done, string.Join("; ", editor.Diagnostics));
            return editor.Project;
        }

        /// <summary>What reading <c>m</c> under <paramref name="library"/> says of the library, as "line: message".</summary>
        private static List<string> Faults(Project project, GateLibrary library)
        {
            project = project with { Settings = (project.Settings ?? new ProjectSettings()) with { Library = library } };
            WrittenModule read = WrittenModules.Read(project, project.Find("m"));
            return read.Diagnostics.Where(d => d.Code == ElaborationCodes.OffLibrary)
                .Select(d => d.Location.Line + ": " + d.Message).ToList();
        }

        private static void Holds(string body, bool noConstants = false) =>
            CollectionAssert.IsEmpty(Faults(With(body), Library(noConstants)), body);

        private static string Breaks(string body, string said, bool noConstants = false)
        {
            List<string> faults = Faults(With(body), Library(noConstants));
            Assert.AreEqual(1, faults.Count, body + "\n" + string.Join("\n", faults));
            StringAssert.Contains(said, faults[0]);
            return faults[0];
        }

        [Test]
        public void TheLibrarysCells_AtItsDelays_AreHeld_AsCellsOrAsPrimitives()
        {
            Holds("    logic n;\n    NAND2 g1 (.A(a), .B(b), .Z(n));\n    INV g2 (.A(n), .Z(y));");
            Holds("    logic n;\n    nand #(2, 3) g1 (n, a, b);\n    not #(2, 3) g2 (y, n);");
            Holds("    NAND2 #(.Tpdlh(2), .Tpdhl(3)) g1 (.A(a), .B(b), .Z(y));");
            Holds("    XOR2 g (.A(a), .B(b), .Z(y));", noConstants: true);
            Holds("    assign y = a;\n    assign q = v[2];", noConstants: true);
        }

        [Test]
        public void AGateOffItsCellsDelays_OrWithNoCell_IsRefused_AtItsLine()
        {
            StringAssert.StartsWith("8: ", Breaks("\n    nand g1 (y, a, b);", "rise 0 and fall 0, but the library's NAND2 is rise 2 and fall 3"));
            Breaks("    NAND2 #(.Tpdlh(5), .Tpdhl(5)) g1 (.A(a), .B(b), .Z(y));", "leave its delays out");
            Breaks("    bufif1 #1 g (y, a, b);", "no cell of the library");

            GateLibrary noNand3 = Library().Toggled(GateKind.Nand, 1);   // NAND taken out, every cell of it
            List<string> faults = Faults(With("    NAND3 g (.A(a), .B(b), .C(c), .Z(y));"), noNand3);
            Assert.AreEqual(1, faults.Count, string.Join("\n", faults));
            StringAssert.Contains("g is a NAND3, which the library has no cell for", faults[0]);
        }

        [Test]
        public void LogicWrittenWithOperators_IsRefused_AndAPlainConnectionIsNot()
        {
            Breaks("    assign y = a & b;", "This assignment works something out");
            Breaks("    assign y = s[0] ? a : b;", "works something out");
            Breaks("    assign y = v[s];", "a select by a signal");
            Holds("    assign y = v[3];\n    assign q = {a};");
            Breaks("    always_comb y = a | b;", "always_comb works logic out in code");
            Breaks("    always @* y = a;", "works logic out in code");
        }

        [Test]
        public void ARegister_OfPlainWrites_IsHeld_WithItsReset_AndOneThatWorksSomethingOutIsNot()
        {
            Holds("    always_ff @(posedge clk) q <= a;\n    assign y = q;");
            Holds("    always_ff @(posedge clk or negedge rst_n)\n        if (!rst_n) q <= 1'b0;\n        else q <= a;\n    assign y = q;", noConstants: true);
            Holds("    always @(posedge clk) q <= a;\n    assign y = q;");
            Breaks("    always_ff @(posedge clk) q <= q ^ a;\n    assign y = q;", "This register works something out");
            Breaks("    always_ff @(posedge clk)\n        case (s)\n            2'd0: q <= a;\n            default: q <= b;\n        endcase\n    assign y = q;", "A register's body is its writes");
        }

        /// <summary>
        /// A register's shape is the drawn register's (the 14g review found ifs nested to any function passing): an if is its
        /// reset -- a one-bit signal, its inverse, or one compared with a constant -- whose then loads constants and whose else
        /// loads D; a choice between signals, a wide condition, an enable that keeps the value, are logic.
        /// </summary>
        [Test]
        public void ARegistersIf_IsOnlyItsReset()
        {
            Holds("    always_ff @(posedge clk) if (rst_n == 1'b0) q <= 1'b0; else q <= a;\n    assign y = q;");
            Holds("    logic [3:0] r;\n    always_ff @(posedge clk) if (~rst_n) r <= '0; else r <= v;\n    assign y = r[0];\n    assign q = r[1];");
            Holds("    logic [3:0] r;\n    always_ff @(posedge clk) if (!rst_n) r <= {4{1'b0}}; else r <= v;\n    assign y = r[0];\n    assign q = r[1];");
            Breaks("    always_ff @(posedge clk) if (a) q <= b; else q <= c;\n    assign y = q;", "A reset loads constants");
            Breaks("    always_ff @(posedge clk) if (v) q <= 1'b0; else q <= c;\n    assign y = q;", "A register's if is its reset");
            Breaks("    always_ff @(posedge clk) if (a) q <= c;\n    assign y = q;", "loads its D otherwise");
            Breaks("    always_ff @(posedge clk)\n        if (a) begin if (b) q <= 1'b1; else q <= 1'b0; end\n        else q <= 1'b0;\n    assign y = q;", "A register's body is its writes");
            Breaks("    always @(posedge (a & b)) q <= c;\n    assign y = q;", "works logic out in code");
        }

        /// <summary>
        /// Logic hidden where a value is given rather than assigned -- a gate's input, a port's connection -- is held as an
        /// assignment is: it is a gate the library and the count never see (the 14g review).
        /// </summary>
        [Test]
        public void LogicInAGatesInput_OrAPortsConnection_IsRefused()
        {
            Breaks("    nand #(2, 3) g (y, a & b, c);", "This gate's input works something out");
            Breaks("    NAND2 g (.A(a & b), .B(c), .Z(y));", "This connection works something out");
            Breaks("    NAND2 g (.A(s[0] ? a : b), .B(c), .Z(y));", "This connection works something out");
            Holds("    NAND2 g (.A(v[1]), .B({c}), .Z(y));");
        }

        /// <summary>An initial gives starting values and nothing else, and a connection has no delay of its own: a delay is a gate's (the 14g review).</summary>
        [Test]
        public void AnInitial_GivesStartingValuesOnly_AndAConnectionHasNoDelay()
        {
            Holds("    initial q = 1'b0;\n    always_ff @(posedge clk) q <= a;\n    assign y = q;");
            Breaks("    initial y = a & b;", "only gives starting values");
            Breaks("    initial forever #1 y = a;", "only gives starting values");
            Breaks("    assign #3 y = a;", "A delay on a connection");
            Breaks("    wire #3 w = a;\n    assign y = w;", "A delay on a connection");
        }

        /// <summary>Under no constants, a constant by any name is refused: a parameter into a cell, a supply net, a widening's 0s (the 14g review).</summary>
        [Test]
        public void UnderNoConstants_AConstantByAnyName_IsRefused()
        {
            Breaks("    localparam ONE = 1'b1;\n    NAND2 g (.A(a), .B(ONE), .Z(y));", "A constant, and the library takes none", noConstants: true);
            Breaks("    NAND2 g (.A(a), .B(~1'b0), .Z(y));", "works something out", noConstants: true);
            Breaks("    supply1 vdd;\n    NAND2 g (.A(a), .B(vdd), .Z(y));", "A constant, and the library takes none", noConstants: true);
            Breaks("    logic [3:0] w;\n    assign w = a;\n    assign y = w[3];", "A constant, and the library takes none", noConstants: true);
            Breaks("    always_ff @(posedge clk) q <= 1'b1;\n    assign y = q;", "A constant loaded on every clock", noConstants: true);
        }

        /// <summary>A fault in a generate loop is said once, not once for every copy the loop makes (the 14g review).</summary>
        [Test]
        public void AFaultInAGenerateLoop_IsSaidOnce()
        {
            Breaks("    logic [3:0] n;\n    for (genvar i = 0; i < 4; i++) begin : g\n        nand #5 u (n[i], v[i], a);\n    end\n    assign y = n[0];", "rise 5 and fall 5");
        }

        [Test]
        public void UnderNoConstants_AConstantIntoAGateOrAnAssign_IsRefused()
        {
            Breaks("    NAND2 g (.A(a), .B(1'b1), .Z(y));", "A constant, and the library takes none", noConstants: true);
            Breaks("    nand #(2, 3) g (y, a, 1'b1);", "A constant, and the library takes none", noConstants: true);
            Breaks("    assign y = 1'b0;", "A constant, and the library takes none", noConstants: true);
            Holds("    assign y = 1'b0;");
        }

        [Test]
        public void ACellsDelays_AsAGenerateLoopWorksThemOut_AreWhatIsHeld()
        {
            Holds("    localparam R = 2;\n    logic [3:0] n;\n    for (genvar i = 0; i < 4; i++) begin : g\n        NAND2 #(.Tpdlh(R), .Tpdhl(R + 1)) u (.A(v[i]), .B(a), .Z(n[i]));\n    end\n    assign y = n[0];");
            // Rise i: only the copy where i is 2 is the library's NAND2.
            List<string> faults = Faults(With("    logic [3:0] n;\n    for (genvar i = 0; i < 4; i++) begin : g\n        NAND2 #(.Tpdlh(i), .Tpdhl(3)) u (.A(v[i]), .B(a), .Z(n[i]));\n    end\n    assign y = n[2];"), Library());
            CollectionAssert.AreEquivalent(new[] { 0, 1, 3 }, faults.Select(f => int.Parse(f.Substring(f.IndexOf("at rise ") + 8, 1))), string.Join("\n", faults));
        }

        /// <summary>A cell written without delays runs at the library's: the cells are built with them as their defaults.</summary>
        [Test]
        public void ACellWrittenWithoutDelays_RunsAtTheLibrarys()
        {
            var editor = new BoardEditor(With("    NAND2 g1 (.A(a), .B(b), .Z(y));\n    assign q = a;"));
            Assert.IsTrue(editor.SetTop("m").Done);
            Assert.IsTrue(editor.SetStimulus(new Stimulus { End = 10 }).Done);
            Assert.IsTrue(editor.SetLibrary(Library()).Done, string.Join("; ", editor.Diagnostics));

            CompiledProject compiled = BoardCompiler.Compile(editor.Project);
            Assert.IsTrue(compiled.Succeeded, string.Join("; ", compiled.Diagnostics));
            Elaboration elaboration = Elaborator.Elaborate(compiled.Tree, compiled.Top);
            GateDriver nand = elaboration.Design.Drivers.OfType<GateDriver>().Single(g => g.Kind == GateKind.Nand);
            Assert.AreEqual((2L, 3L), (nand.Delay.Rise, nand.Delay.Fall), "the library's NAND2, as its default");
        }

        /// <summary>The testbench is not held to the library: it sets the design going, and works out nothing of it.</summary>
        [Test]
        public void TheTestbench_IsNotHeld()
        {
            Project project = With("    assign y = a & b;\n    assign q = a;") with { Bench = "m" };
            CollectionAssert.IsEmpty(Faults(project, Library()), "the testbench works things out, and is not held");
            CollectionAssert.IsNotEmpty(Faults(With("    assign y = a & b;"), Library()), "sanity: held when it is no testbench");
            WrittenModule bench = WrittenModules.Read(project with { Settings = new ProjectSettings { Library = Library() } },
                project.Find("m") with { Source = project.Find("m").Source.Replace("assign q = a;", "assign q = a & b;") });
            Assert.IsFalse(bench.Diagnostics.Any(d => d.Code == ElaborationCodes.OffLibrary), "a testbench may work things out");
        }

        /// <summary>
        /// A library set over written code that breaks it is refused with the fault, as one over a drawing that breaks it is;
        /// and a cell retimed is refused while code pins it at other delays, but followed by code that leaves them out.
        /// </summary>
        [Test]
        public void ALibrary_OverCodeThatBreaksIt_IsRefused_AndARetimeFollowsCodeThatLeavesItsDelaysOut()
        {
            var breaking = new BoardEditor(With("    assign y = a & b;\n    assign q = a;"));
            EditResult refused = breaking.SetLibrary(Library());
            Assert.IsFalse(refused.Done);
            StringAssert.Contains("This assignment works something out", refused.Refusal);

            // With a block of it drawn: the code's fault is what is said, not a wire left on a pin the block lost (the 14g review).
            Bench.Must(breaking.Add("top", new BlockPart { Module = "m" }, new Placement(4, 0)));
            int block = breaking.Project.TopModule.Parts.OfType<BlockPart>().Single().Id;
            int input = Bench.Must(breaking.Add("top", new PortPart { Name = "x" }, new Placement(0, 0)));
            Bench.Must(breaking.Connect("top", Endpoint.At(input, "out"), Endpoint.At(block, "a")));
            EditResult withBlock = breaking.SetLibrary(Library());
            Assert.IsFalse(withBlock.Done);
            StringAssert.Contains("This assignment works something out", withBlock.Refusal);

            var pinned = new BoardEditor(With("    NAND2 #(.Tpdlh(2), .Tpdhl(3)) g1 (.A(a), .B(b), .Z(y));\n    assign q = a;"));
            Assert.IsTrue(pinned.SetLibrary(Library()).Done, string.Join("; ", pinned.Diagnostics));
            GateLibrary held = pinned.Project.Settings.Library;
            EditResult retimed = pinned.SetLibrary(held.Retimed(held.CellFor(GateKind.Nand, 2), 4, 4));
            Assert.IsFalse(retimed.Done, "the code pins the old delays");
            StringAssert.Contains("leave its delays out", retimed.Refusal);

            var following = new BoardEditor(With("    NAND2 g1 (.A(a), .B(b), .Z(y));\n    assign q = a;"));
            Assert.IsTrue(following.SetLibrary(Library()).Done);
            GateLibrary set = following.Project.Settings.Library;
            Assert.IsTrue(following.SetLibrary(set.Retimed(set.CellFor(GateKind.Nand, 2), 4, 4)).Done, "it leaves them out, so it follows");
        }
    }
}
