using System.Linq;
using BitSorter.Sim;
using BitSorter.SystemVerilog;
using BitSorter.SystemVerilog.Syntax;
using NUnit.Framework;

namespace BitSorter.Board.Tests
{
    /// <summary>
    /// Where each cell of a netlist is written (Part 15h-5): what the drawing of code points at in the code, and what a line of
    /// the code selects on the drawing. An assign's cells take its line, a gate's and an instance's theirs, a statement of a
    /// process its own -- the mux that joins an if's two ways the if's -- a register its always_ff's, and a port its declaration.
    /// </summary>
    public class RtlLinesTests
    {
        private const string Code =
            "module child (input logic a, output logic y);\n" +          // 1
            "    assign y = ~a;\n" +                                     // 2
            "endmodule\n" +                                              // 3
            "module m (input logic clk, rst,\n" +                        // 4
            "          input logic [1:0] sel,\n" +                       // 5
            "          input logic a, b,\n" +                            // 6
            "          output logic y, q, z, n, w);\n" +                 // 7
            "    assign y = a & b;\n" +                                  // 8
            "    always_comb begin\n" +                                  // 9
            "        z = 0;\n" +                                         // 10
            "        if (sel == 2'd1)\n" +                               // 11
            "            z = a;\n" +                                     // 12
            "        else\n" +                                           // 13
            "            z = b ^ a;\n" +                                 // 14
            "    end\n" +                                                // 15
            "    always_ff @(posedge clk)\n" +                           // 16
            "        if (rst) q <= 0;\n" +                               // 17
            "        else q <= y;\n" +                                   // 18
            "    child u (.a(b), .y(n));\n" +                            // 19
            "    or g1 (w, a, b);\n" +                                   // 20
            "endmodule\n";                                               // 21

        private static RtlNetlist Netlist()
        {
            SyntaxTree tree = SyntaxReader.ReadText(Code, "m.sv");
            Assert.IsFalse(tree.HasErrors, string.Join("; ", tree.Diagnostics));
            Elaboration e = Elaborator.Elaborate(tree, "m");
            Assert.IsFalse(e.HasErrors, string.Join("\n", e.Diagnostics));
            return RtlExtractor.Of(e.Design, "m");
        }

        private static RtlCell Driver(RtlNetlist netlist, string net) => netlist.Cells[netlist.Nets[netlist.NetNamed(net)].Driver];

        [Test]
        public void AnAssignsCells_TakeItsLine()
        {
            RtlNetlist netlist = Netlist();
            RtlCell and = netlist.Cells.Single(c => c.Kind == RtlKind.Binary && c.Binary == BinaryOp.And);
            Assert.AreEqual(8, and.At.Line, "assign y = a & b");
            Assert.AreEqual("m.sv", and.At.File);
            Assert.AreEqual(8, Driver(netlist, "y").At.Line, "and the join onto y");
        }

        [Test]
        public void AProcesssCells_TakeTheirStatementsLines_AndTheMuxThatJoinsAnIf_TheIfs()
        {
            RtlNetlist netlist = Netlist();
            Assert.AreEqual(14, netlist.Cells.Single(c => c.Kind == RtlKind.Binary && c.Binary == BinaryOp.Xor).At.Line, "z = b ^ a");
            Assert.AreEqual(11, netlist.Cells.Single(c => c.Kind == RtlKind.Binary && c.Binary == BinaryOp.Equal).At.Line, "the if's test");
            RtlCell mux = netlist.Cells.Single(c => c.Kind == RtlKind.Mux && c.Outputs.Any(o => netlist.Nets[o].Name == "z")
                || c.Kind == RtlKind.Mux && netlist.Cells.Any(j => j.Inputs.Contains(c.Output) && j.Outputs.Any(o => netlist.Nets[o].Name == "z")));
            Assert.AreEqual(11, mux.At.Line, "the mux joining the if's ways, the if's");
        }

        [Test]
        public void ARegister_TakesItsAlwaysFfsLine()
        {
            RtlNetlist netlist = Netlist();
            RtlCell register = netlist.Cells.Single(c => c.Kind == RtlKind.Register);
            Assert.AreEqual(16, register.At.Line);
        }

        [Test]
        public void AnInstanceAndAGate_TakeTheirLines()
        {
            RtlNetlist netlist = Netlist();
            Assert.AreEqual(19, netlist.Cells.Single(c => c.Kind == RtlKind.Instance).At.Line, "child u");
            Assert.AreEqual(20, netlist.Cells.Single(c => c.Kind == RtlKind.Gate).At.Line, "or g1");
        }

        /// <summary>The review of 15h-5: a register written by two processes took the first's line for both of its pieces.</summary>
        [Test]
        public void EachPieceOfARegisterWrittenByTwoProcesses_TakesItsOwnProcesssLine()
        {
            SyntaxTree tree = SyntaxReader.ReadText(
                "module two (input logic clk, input logic [1:0] d, output logic [1:0] q);\n" +   // 1
                "    always_ff @(posedge clk)\n" +                                                // 2
                "        q[0] <= d[0];\n" +                                                       // 3
                "    always_ff @(posedge clk)\n" +                                                // 4
                "        q[1] <= d[1];\n" +                                                       // 5
                "endmodule\n", "two.sv");
            Assert.IsFalse(tree.HasErrors, string.Join("; ", tree.Diagnostics));
            Elaboration e = Elaborator.Elaborate(tree, "two");
            Assert.IsFalse(e.HasErrors, string.Join("\n", e.Diagnostics));
            RtlNetlist netlist = RtlExtractor.Of(e.Design, "two");
            CollectionAssert.AreEquivalent(new[] { 2, 4 }, netlist.Cells.Where(c => c.Kind == RtlKind.Register).Select(c => c.At.Line).ToArray());
        }

        [Test]
        public void APortsNet_IsDeclaredOnItsLine()
        {
            RtlNetlist netlist = Netlist();
            Assert.AreEqual(4, netlist.Nets[netlist.NetNamed("rst")].Declared.Line);
            Assert.AreEqual(5, netlist.Nets[netlist.NetNamed("sel")].Declared.Line);
            Assert.AreEqual(7, netlist.Nets[netlist.NetNamed("w")].Declared.Line);
        }
    }
}
