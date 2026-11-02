using System.Linq;
using BitSorter.SystemVerilog.Syntax;
using NUnit.Framework;

namespace BitSorter.Sim.Tests
{
    /// <summary>
    /// The race warning (Part 15i-3, Ahmad's choice, 2026-11-02): a flip-flop and a blocking write of what it reads, at the
    /// moment of its clock's edge, from a process neither it nor the clock's -- the classic testbench race, which ModelSim
    /// and Icarus may settle either way. Said once for each pair, never for a nonblocking write, never for changes one
    /// process makes in order, and never at a clock's first edge out of x. Each design written for these tests.
    /// </summary>
    public class RaceWarningTests
    {
        private const string Flop =
            "module flop (input logic clk, d, output logic q);\n" +
            "    always_ff @(posedge clk) q <= d;\n" +
            "endmodule\n";

        private static string[] Warnings(string bench, long until = 100)
        {
            SyntaxTree tree = SyntaxReader.ReadText(Flop + bench, "race.sv");
            Assert.IsEmpty(tree.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error), string.Join("\n", tree.Diagnostics));
            Elaboration e = Elaborator.Elaborate(tree, "tb");
            Assert.IsFalse(e.HasErrors, string.Join("\n", e.Diagnostics));
            var sim = new Simulator(e.Design);
            sim.RunTo(until);
            return sim.History.Messages.Where(m => m.Severity == Severity.Warning && m.Text.Contains("blocking assignment at the edge"))
                .Select(m => m.Text).ToArray();
        }

        private const string Clock =
            "module tb;\n" +
            "    logic clk = 1'b0, d = 1'b0, q;\n" +
            "    flop dut (.clk(clk), .d(d), .q(q));\n" +
            "    always #5 clk = ~clk;\n";

        [Test]
        public void AnInputChangedOnTheEdgeItIsTakenAt_IsARace_SaidOnce()
        {
            string[] said = Warnings(Clock +
                "    initial begin\n" +
                "        repeat (4) begin @(posedge clk); d = ~d; end\n" +
                "        $finish;\n" +
                "    end\n" +
                "endmodule\n");
            Assert.AreEqual(1, said.Length, "four edges, one pair, one warning: " + string.Join("\n", said));
            StringAssert.Contains("tb.initial", said[0]);
            StringAssert.Contains("changes tb.d (which tb.dut.d follows) with a blocking assignment at the edge of", said[0]);
            StringAssert.Contains("tb.dut.always_ff", said[0]);
            StringAssert.Contains("or with <=", said[0]);
        }

        [Test]
        public void AnInputChangedByTimeAtTheMomentOfTheEdge_IsARace()
        {
            // clk rises at 5, 15, 25: the change at 15 lands on an edge, from another process.
            string[] said = Warnings(Clock + "    initial begin #15 d = 1'b1; #20 $finish; end\nendmodule\n");
            Assert.AreEqual(1, said.Length);
            StringAssert.StartsWith("At t = 15 ", said[0]);
        }

        [Test]
        public void TwoFlipFlopsWrittenWithBlockingAssignments_Race()
        {
            string[] said = Warnings(
                "module tb;\n" +
                "    logic clk = 1'b0, a = 1'b0, b, c;\n" +
                "    always #5 clk = ~clk;\n" +
                "    always @(posedge clk) b = a;\n" +
                "    always @(posedge clk) c = b;\n" +
                "    initial begin #2 a = 1'b1; #40 $finish; end\n" +
                "endmodule\n");
            Assert.AreEqual(1, said.Length, string.Join("\n", said));
            StringAssert.Contains("changes tb.b", said[0]);
        }

        [Test]
        public void ChangesOffTheEdge_OrNonblocking_OrInOneProcess_AreNoRace()
        {
            CollectionAssert.IsEmpty(Warnings(Clock +
                "    initial begin repeat (4) begin @(negedge clk); d = ~d; end $finish; end\nendmodule\n"), "on the other edge");
            CollectionAssert.IsEmpty(Warnings(Clock +
                "    initial begin repeat (4) begin @(posedge clk); #1 d = ~d; end $finish; end\nendmodule\n"), "a moment after it");
            CollectionAssert.IsEmpty(Warnings(Clock +
                "    initial begin repeat (4) begin @(posedge clk); d <= ~d; end $finish; end\nendmodule\n"), "with <=, after every process has run");
            CollectionAssert.IsEmpty(Warnings(
                "module tb;\n" +
                "    logic clk = 1'b0, d = 1'b0, q;\n" +
                "    flop dut (.clk(clk), .d(d), .q(q));\n" +
                "    initial begin repeat (4) begin #5 clk = 1'b1; d = ~d; #5 clk = 1'b0; end $finish; end\n" +
                "endmodule\n"), "the clock and the input from one process, in its order");
            CollectionAssert.IsEmpty(Warnings(
                "module tb;\n" +
                "    logic clk = 1'b0, d = 1'b0, q;\n" +
                "    flop dut (.clk(clk), .d(d), .q(q));\n" +
                "    always #5 clk = ~clk;\n" +
                "    always @(posedge clk) d <= ~d;\n" +
                "    initial #50 $finish;\n" +
                "endmodule\n"), "a flip-flop feeding another");
        }

        [Test]
        public void AClocksFirstEdgeOutOfX_IsNoRace()
        {
            // clk goes x to 1 at time 0 -- an edge -- as the bench sets d: no clock has ticked yet.
            CollectionAssert.IsEmpty(Warnings(
                "module tb;\n" +
                "    logic clk, d, q;\n" +
                "    flop dut (.clk(clk), .d(d), .q(q));\n" +
                "    initial clk = 1'b1;\n" +
                "    initial begin d = 1'b0; #20 $finish; end\n" +
                "endmodule\n"));
        }
    }
}
