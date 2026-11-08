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

        // -----------------------------------------------------------------
        // The review's cases (2026-11-02), each written for these tests
        // -----------------------------------------------------------------

        [Test]
        public void AReadAMomentAfterTheEdge_IsNoRace()
        {
            // The checker wakes on the edge but reads a one unit later, after the change: what one wake reads, not its code.
            CollectionAssert.IsEmpty(Warnings(
                "module tb;\n" +
                "    logic clk = 1'b0, a = 1'b0, ok;\n" +
                "    always #5 clk = ~clk;\n" +
                "    always @(posedge clk) a = ~a;\n" +
                "    always @(posedge clk) begin #1; ok = (a == 1'b1); end\n" +
                "    initial #40 $finish;\n" +
                "endmodule\n"));
        }

        [Test]
        public void AForksBranches_AndTheProcessThatForksThem_AreNoRace()
        {
            CollectionAssert.IsEmpty(Warnings(
                "module tb;\n" +
                "    logic clk = 1'b0, a = 1'b0, b = 1'b0, r;\n" +
                "    always #5 clk = ~clk;\n" +
                "    initial begin\n" +
                "        @(posedge clk);\n" +
                "        fork begin a = 1'b1; end begin b = 1'b1; end join\n" +
                "        r = a & b;\n" +
                "        #20 $finish;\n" +
                "    end\n" +
                "endmodule\n"));
        }

        /// <summary>
        /// A fork that does not wait -- join_none -- is on a process's way to its next wait, so what it reads after it is read
        /// on that wake (the 16d sweep, engine-18): the reads stopped at every fork, so a read after a join_none of what another
        /// process changed on the same edge went unsaid.
        /// </summary>
        [Test]
        public void AReadAfterAForkThatDoesNotWait_IsReadOnTheSameWake_SoItsRaceIsSaid()
        {
            string[] said = Warnings(
                "module tb;\n" +
                "    logic clk = 1'b0, a = 1'b0, r;\n" +
                "    always #5 clk = ~clk;\n" +
                "    always @(posedge clk) a = ~a;\n" +
                "    initial forever begin\n" +
                "        @(posedge clk);\n" +
                "        fork #1; join_none\n" +
                "        r = a;\n" +
                "    end\n" +
                "    initial #40 $finish;\n" +
                "endmodule\n");

            Assert.AreEqual(1, said.Length, string.Join("\n", said));
            StringAssert.Contains("changes tb.a", said[0]);
        }

        /// <summary>
        /// A branch that writes what the process that forked it reads on the same wake is no race: one forked the other, and
        /// the branch runs once its parent waits (the 16d sweep, engine-18). The test above reads only after its join, past a
        /// wait, so Caused()'s fork clause was never asked, and taking it out left every test green; this one fails without it.
        /// </summary>
        [Test]
        public void ABranchWritingWhatItsParentReadsOnTheSameWake_IsNoRace()
        {
            CollectionAssert.IsEmpty(Warnings(
                "module tb;\n" +
                "    logic clk = 1'b0, a = 1'b0, r;\n" +
                "    always #5 clk = ~clk;\n" +
                "    initial forever begin\n" +
                "        @(posedge clk);\n" +
                "        fork a = ~a; join_none\n" +
                "        r = a;\n" +
                "    end\n" +
                "    initial #40 $finish;\n" +
                "endmodule\n"));
        }

        [Test]
        public void AWriterThatMadeTheEdge_OrAReaderThatSetTheWriterGoing_IsNoRace()
        {
            // d, then go, then the clock: the flip-flop must see d's new value.
            CollectionAssert.IsEmpty(Warnings(
                "module tb;\n" +
                "    logic clk = 1'b0, go = 1'b0, d = 1'b0, q;\n" +
                "    flop dut (.clk(clk), .d(d), .q(q));\n" +
                "    initial begin #10 d = 1'b1; go = 1'b1; #20 $finish; end\n" +
                "    always @(posedge go) clk = 1'b1;\n" +
                "endmodule\n"), "the writer made the edge, through another process");

            // The flip-flop reads r, then toggles x, which wakes the process that writes r.
            CollectionAssert.IsEmpty(Warnings(
                "module tb;\n" +
                "    logic clk = 1'b0, x = 1'b0, r = 1'b0, y;\n" +
                "    always #5 clk = ~clk;\n" +
                "    always @(posedge clk) begin y <= r; x = ~x; end\n" +
                "    always @(posedge x) r = ~r;\n" +
                "    initial #40 $finish;\n" +
                "endmodule\n"), "the reader set the writer going");
        }

        [Test]
        public void AWakeIsTheItemThatFired_NotTheChangeUnderWay()
        {
            // The clock falls as the reset rises, from two processes in one delta: the flip-flop is woken by the reset, whose
            // own process set d -- no race. Taken for the clock's wake, it was one, in the start-from-nothing exercise's test.
            CollectionAssert.IsEmpty(Warnings(
                "module tb;\n" +
                "    logic clk = 1'b0, rst = 1'b0, d = 1'b0, q;\n" +
                "    always #5 clk = ~clk;\n" +
                "    always_ff @(posedge clk, posedge rst) if (rst) q <= 1'b0; else q <= d;\n" +
                "    initial begin #10 rst = 1'b1; d = 1'b1; #20 $finish; end\n" +
                "endmodule\n"));
        }

        [Test]
        public void AnAsynchronousResetReleasedOnTheClocksEdge_IsARace()
        {
            // Its body tests the reset, so a release from another process on the edge races the flip-flop's test of it.
            string[] said = Warnings(
                "module tb;\n" +
                "    logic clk = 1'b0, rst_n = 1'b0, d = 1'b1, q;\n" +
                "    always #5 clk = ~clk;\n" +
                "    always_ff @(posedge clk or negedge rst_n) if (!rst_n) q <= 1'b0; else q <= d;\n" +
                "    initial begin @(posedge clk); rst_n = 1'b1; #20 $finish; end\n" +
                "endmodule\n");
            Assert.AreEqual(1, said.Length, string.Join("\n", said));
            StringAssert.Contains("changes tb.rst_n", said[0]);
        }
    }
}
