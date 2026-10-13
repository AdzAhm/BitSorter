using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BitSorter.SystemVerilog.Syntax;
using NUnit.Framework;
using static BitSorter.SystemVerilog.Tests.Syntax.SyntaxKit;

namespace BitSorter.SystemVerilog.Tests.Syntax
{
    /// <summary>
    /// The printer: the course's style, and the fixed point -- print a tree, read the text, print
    /// again, and nothing moves (Docs/architecture-5.md, invariant 4).
    /// </summary>
    public class PrinterTests
    {
        private static string Normalise(string text) => text.Replace("\r\n", "\n");

        /// <summary>Reads, prints, reads the print, prints again: the two prints must match, with nothing reported.</summary>
        private static string FixedPoint(string text, ISourceFiles files = null)
        {
            string first = Printer.Print(Clean(text, files));
            SyntaxTree again = SyntaxReader.ReadText(first, "printed.sv");
            Assert.IsEmpty(again.Diagnostics, "the printed text did not read back cleanly:\n" + string.Join("\n", again.Diagnostics) + "\n---\n" + first);
            string second = Printer.Print(again);
            Assert.AreEqual(first, second, "printing is not a fixed point");
            return first;
        }

        [Test]
        public void AStructuralModule_PrintsInTheCoursesStyle()
        {
            const string source =
                "module HA(output logic S, // the sum\n output logic C,input logic A,input logic B);\n" +
                "xor(S,A,B);   and(C,A,B);\nendmodule";

            const string expected =
                "module HA (\n" +
                "    output logic S, // the sum\n" +
                "    output logic C,\n" +
                "    input logic A,\n" +
                "    input logic B\n" +
                ");\n" +
                "    xor(S, A, B);\n" +
                "    and(C, A, B);\n" +
                "endmodule\n";

            Assert.AreEqual(expected, FixedPoint(source));
        }

        [Test]
        public void ParametersInstancesAndGenerate_Print()
        {
            const string source =
                "module top #(parameter N=4)(input logic [N-1:0] a, output logic [N-1:0] y);\n" +
                "genvar i;\n" +
                "generate for(i=0;i<N;i++) begin : bit_\n" +
                "AND2 #(.Tpdlh(2),.Tpdhl(3)) g(.A(a[i]),.B(a[(i+1)%N]),.Z(y[i]));\n" +
                "end endgenerate\n" +
                "endmodule";

            const string expected =
                "module top #(\n" +
                "    parameter N = 4\n" +
                ") (\n" +
                "    input logic [N-1:0] a,\n" +
                "    output logic [N-1:0] y\n" +
                ");\n" +
                "    genvar i;\n" +
                "    generate\n" +
                "        for (i = 0; i < N; i++) begin : bit_\n" +
                "            AND2 #(.Tpdlh(2), .Tpdhl(3)) g (.A(a[i]), .B(a[(i+1) % N]), .Z(y[i]));\n" +
                "        end\n" +
                "    endgenerate\n" +
                "endmodule\n";

            Assert.AreEqual(expected, FixedPoint(source));
        }

        [Test]
        public void AnInstanceWithManyConnections_PutsOnePerLine()
        {
            string printed = FixedPoint("module m; full_adder fa0 (.a(a), .b(b), .cin(c), .sum(s), .cout(co)); endmodule");
            StringAssert.Contains("    full_adder fa0 (\n        .a(a),\n        .b(b),", printed);
            StringAssert.Contains("        .cout(co)\n    );\n", printed);
        }

        [Test]
        public void ABehaviouralModule_Prints()
        {
            const string source =
                "module counter(input logic clk, rst, output logic [3:0] q);\n" +
                "always_ff @(posedge clk, posedge rst) begin\n" +
                "if (rst == 1'b1) begin q <= 4'b0; end\n" +
                "else begin q <= q + 1; end\n" +
                "end\n" +
                "endmodule";

            const string expected =
                "module counter (\n" +
                "    input logic clk,\n" +
                "    input logic rst,\n" +
                "    output logic [3:0] q\n" +
                ");\n" +
                "    always_ff @(posedge clk, posedge rst) begin\n" +
                "        if (rst == 1'b1) begin\n" +
                "            q <= 4'b0;\n" +
                "        end\n" +
                "        else begin\n" +
                "            q <= q + 1;\n" +
                "        end\n" +
                "    end\n" +
                "endmodule\n";

            Assert.AreEqual(expected, FixedPoint(source));
        }

        [Test]
        public void AnIfAfterAProcessHead_GoesOnALineOfItsOwn_SoItsElseLinesUp()
        {
            // Part 7a: a register with a reset, as the board prints one. It was "... @(...) if (rst)" on
            // one line and the else at the always_ff's own indent.
            const string source =
                "module r(input logic clk, rst, d, output logic q);\n" +
                "always_ff @(posedge clk, posedge rst) if (rst) q <= 1'b0; else q <= d;\n" +
                "endmodule";

            StringAssert.Contains(
                "    always_ff @(posedge clk, posedge rst)\n" +
                "        if (rst)\n" +
                "            q <= 1'b0;\n" +
                "        else\n" +
                "            q <= d;\n", FixedPoint(source));
        }

        [Test]
        public void StatementsOfEveryKind_ReachAFixedPoint()
        {
            FixedPoint(
                "module tb;\n" +
                "logic clk, rst; logic [7:0] mem [0:15]; integer i;\n" +
                "typedef enum logic [1:0] {IDLE, RUN = 2'd2} st_t; st_t s;\n" +
                "initial begin clk = 0; forever #5 clk = ~clk; end\n" +
                "initial begin : main\n" +
                "  rst = 1; #12 rst = 0; @(posedge clk); repeat (3) @(negedge clk);\n" +
                "  for (int k = 0; k < 4; k++) mem[k] = k[7:0];\n" +
                "  while (i < 3) i = i + 1;\n" +
                "  do i--; while (i > 0);\n" +
                "  case (s) IDLE: s = RUN; RUN, 2'd3: begin s = IDLE; end default: ; endcase\n" +
                "  casez (i[1:0]) 2'b1?: $display(\"%b\", i); endcase\n" +
                "  if (a) if (b) x = 1; else x = 2;\n" +
                "  wait (done) $finish;\n" +
                "  a = #3 b; q <= @(posedge clk) d;\n" +
                "  $readmemh(\"m.hex\", mem); $stop;\n" +
                "end\n" +
                "always @(a or b) y = a ? b : {2{a}};\n" +
                "function automatic int sq(input int v); return v * v; endfunction\n" +
                "task pulse(output logic p); p = 1; #1 p = 0; endtask\n" +
                "endmodule\n");
        }

        [Test]
        public void ADanglingElse_StaysWithTheNearestIf()
        {
            string printed = FixedPoint("module m; initial if (a) if (b) x = 1; else x = 2; endmodule");
            SyntaxTree reread = SyntaxReader.ReadText(printed, "p.sv");
            var outer = (IfStatement)((ProceduralBlock)reread.Modules[0].Items[0]).Body;
            Assert.IsNull(outer.Else, "the else belonged to the inner if, and still does");
            Assert.IsNotNull(((IfStatement)outer.Then).Else);
        }

        [Test]
        public void ATreeBuiltInCode_GetsTheParenthesesItsMeaningNeeds()
        {
            // (a | b) & c, with no ParenthesizedExpression: the printer must add them.
            var tree = new BinaryExpression
            {
                Operator = "&",
                Left = new BinaryExpression { Operator = "|", Left = Name("a"), Right = Name("b") },
                Right = Name("c"),
            };
            Assert.AreEqual("(a | b) & c", Printer.Print(tree));

            // a - (b - c): the right operand of a left-associative operator.
            var minus = new BinaryExpression { Operator = "-", Left = Name("a"), Right = new BinaryExpression { Operator = "-", Left = Name("b"), Right = Name("c") } };
            Assert.AreEqual("a - (b - c)", Printer.Print(minus));

            // Two unary minuses must not become the decrement operator.
            var negate = new UnaryExpression { Operator = "-", Operand = new UnaryExpression { Operator = "-", Operand = Name("a") } };
            Assert.AreEqual("-(-a)", Printer.Print(negate));
        }

        private static NameExpression Name(string name)
        {
            var e = new NameExpression();
            e.Path.Add(name);
            return e;
        }

        [Test]
        public void AnUnsupportedConstruct_IsPrintedAsItWasWritten()
        {
            SyntaxTree tree = Read("module m;\n  defparam u.N = 4;\nendmodule\n");
            StringAssert.Contains("defparam u . N = 4 ;", Printer.Print(tree));
        }

        // -----------------------------------------------------------------
        // The corpus
        // -----------------------------------------------------------------

        public static IEnumerable<string> Corpus() => CorpusFiles();

        [TestCaseSource(nameof(Corpus))]
        public void EveryCorpusFile_ReadsCleanly_AndPrintsToAFixedPoint(string file)
        {
            MemorySourceFiles sources = CorpusSources();
            SyntaxTree tree = SyntaxReader.Read(file, sources);
            Assert.IsEmpty(tree.Diagnostics, string.Join("\n", tree.Diagnostics));
            Assert.IsNotEmpty(tree.Modules);

            string first = Printer.Print(tree);
            SyntaxTree again = SyntaxReader.ReadText(first, "printed.sv");
            Assert.IsEmpty(again.Diagnostics, string.Join("\n", again.Diagnostics) + "\n---\n" + first);
            Assert.AreEqual(first, Printer.Print(again));
            Assert.AreEqual(tree.Modules.Select(m => m.Name), again.Modules.Select(m => m.Name));
        }

        [Test]
        public void TheCorpus_IsNotEmpty()
        {
            Assert.GreaterOrEqual(Corpus().Count(), 7, "sanity: the corpus files were not found");
        }
    }
}
