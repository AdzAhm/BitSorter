using System;
using System.IO;
using System.Linq;
using BitSorter.SystemVerilog;
using BitSorter.SystemVerilog.Syntax;
using NUnit.Framework;

namespace BitSorter.Sim.Tests
{
    /// <summary>
    /// Processes: <c>initial</c>, <c>always</c>, <c>always_ff</c>, <c>always_comb</c>, read from
    /// SystemVerilog, compiled to instructions and run by the engine. Where the standard leaves an
    /// order open, the expectation is what Icarus does, probed by hand on 2026-10-13 and written down
    /// in Docs/oracle-notes.md.
    /// </summary>
    public class ProcessTests
    {
        // -----------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------

        private static Elaboration Elaborate(string text, string top = null)
        {
            SyntaxTree tree = SyntaxReader.ReadText(text, "test.sv");
            Assert.IsEmpty(tree.Diagnostics, "the read reported:\n" + string.Join("\n", tree.Diagnostics));
            return Elaborator.Elaborate(tree, top);
        }

        private static Design Clean(string text, string top = null)
        {
            Elaboration e = Elaborate(text, top);
            Assert.IsEmpty(e.Diagnostics, "the elaboration reported:\n" + string.Join("\n", e.Diagnostics));
            return e.Design;
        }

        private static Simulator Run(Design design, long until, SimOptions options = null)
        {
            var sim = new Simulator(design, null, options);
            sim.RunTo(until);
            return sim;
        }

        private static string[] Changes(Simulator sim, string name) =>
            Probes.Settled(sim.History, sim.Design.Find(sim.Design.Name + "." + name));

        private static string Now(Simulator sim, string name) =>
            sim.Value(sim.Design.Find(sim.Design.Name + "." + name)).Format(Radix.Binary);

        private static string[] Codes(Elaboration e) => e.Diagnostics.Select(d => d.Code).ToArray();

        // -----------------------------------------------------------------
        // Running
        // -----------------------------------------------------------------

        [Test]
        public void AnInitial_WritesAtItsDelays_AndEnds()
        {
            Simulator sim = Run(Clean(@"
module t;
    logic a;
    initial begin
        a = 0;
        #5 a = 1;
        #5 a = 0;
    end
endmodule"), 100);

            CollectionAssert.AreEqual(new[] { "0 0", "5 1", "10 0" }, Changes(sim, "a"));
        }

        [Test]
        public void AClock_FromForever_TogglesEveryHalfPeriod()
        {
            Simulator sim = Run(Clean(@"
module t;
    logic clk;
    initial begin
        clk = 1'b0;
        forever #5 clk = ~clk;
    end
endmodule"), 30);

            CollectionAssert.AreEqual(new[] { "0 0", "5 1", "10 0", "15 1", "20 0", "25 1", "30 0" }, Changes(sim, "clk"));
        }

        [Test]
        public void AnAlwaysFfCounter_ResetsAsynchronously_AndCountsOnRisingEdges()
        {
            Simulator sim = Run(Clean(@"
module counter (input logic clk, rst, output logic [3:0] q);
    always_ff @(posedge clk, posedge rst)
        if (rst) q <= 4'd0;
        else q <= q + 4'd1;
endmodule

module t;
    logic clk = 0, rst;
    logic [3:0] q;
    counter c (.clk(clk), .rst(rst), .q(q));
    always #5 clk = ~clk;
    initial begin
        rst = 1;
        #12 rst = 0;
        #30 rst = 1;
        #3 rst = 0;
    end
endmodule"), 60);

            // Reset at 0 (the instance's process started before the testbench raised it), then edges
            // at 15, 25, 35; reset again at 42; counting again from 45.
            CollectionAssert.AreEqual(new[] { "0 0000", "15 0001", "25 0010", "35 0011", "42 0000", "45 0001", "55 0010" }, Changes(sim, "q"));
        }

        [Test]
        public void Nonblocking_Swaps_ButBlocking_Copies()
        {
            Simulator sim = Run(Clean(@"
module t;
    logic clk = 0;
    logic sa = 0, sb = 1, ba = 0, bb = 1;
    always @(posedge clk) begin sa <= sb; sb <= sa; end
    always @(posedge clk) begin ba = bb; bb = ba; end
    initial #5 clk = 1;
endmodule"), 10);

            Assert.AreEqual("1", Now(sim, "sa"));
            Assert.AreEqual("0", Now(sim, "sb"));
            Assert.AreEqual("1", Now(sim, "ba"), "blocking: ba takes bb's 1 ...");
            Assert.AreEqual("1", Now(sim, "bb"), "... and bb then takes ba's new 1");
        }

        [Test]
        public void AlwaysComb_RunsOnceAtTimeZero_ButAlwaysStar_WaitsForAChange()
        {
            Simulator sim = Run(Clean(@"
module t;
    logic fin = 0, fout, gin = 0, gout;
    always_comb fout = ~fin;
    always @* gout = ~gin;
    initial #10 begin fin = 1; gin = 1; end
endmodule"), 20);

            CollectionAssert.AreEqual(new[] { "0 1", "10 0" }, Changes(sim, "fout"));
            CollectionAssert.AreEqual(new[] { "0 x", "10 0" }, Changes(sim, "gout"));
        }

        [Test]
        public void AtTimeZero_AnInstancesProcessesStartFirst_AndAModulesOwnRunAsWritten()
        {
            Simulator sim = Run(Clean(@"
module waiter (input logic r, output logic q);
    always @(posedge r) q = 1;
endmodule

module t;
    logic ra, qa;
    initial ra = 1;
    always @(posedge ra) qa = 1;     // starts too late to see ra rise

    logic rb, qb;
    always @(posedge rb) qb = 1;     // already waiting
    initial rb = 1;

    logic r2, q2;
    initial r2 = 1;
    waiter w (.r(r2), .q(q2));       // after the initial, but an instance: it starts first
endmodule"), 10);

            Assert.AreEqual("x", Now(sim, "qa"));
            Assert.AreEqual("1", Now(sim, "qb"));
            Assert.AreEqual("1", Now(sim, "q2"));
        }

        [Test]
        public void AtTimeZero_ProcessesStart_OnceWhatHasNoDelayHasSettled()
        {
            // As Icarus starts them: an initial reading a wire at time 0 sees it worked out from the
            // starting values, not the x it has before its assign has run.
            Simulator sim = Run(Clean(@"
module t;
    logic [3:0] a = 4'd5, seen;
    wire [3:0] y = a + 4'd1;
    initial seen = y;
endmodule"), 10);

            CollectionAssert.AreEqual(new[] { "0 0110" }, Changes(sim, "seen"));
        }

        [Test]
        public void HashZero_LetsTheRestOfTheInstantRunFirst()
        {
            Simulator sim = Run(Clean(@"
module t;
    logic h1, h2;
    initial begin #20 h1 = 1; #0 h2 = h1; end
    initial begin #20 h1 = 0; end
endmodule"), 30);

            CollectionAssert.AreEqual(new[] { "0 x", "20 0" }, Changes(sim, "h1"));
            CollectionAssert.AreEqual(new[] { "0 x", "20 0" }, Changes(sim, "h2"), "h2 = h1 ran after the second initial's write");
        }

        [Test]
        public void ACaseStatement_ComparesExactly_AndCasezAndCasexTreatTheirWildcards()
        {
            Design design = Clean(@"
module t;
    logic [1:0] s;
    logic [3:0] exact, z, x;
    always_comb begin
        case (s)
            2'b00: exact = 4'd1;
            2'b01, 2'b10: exact = 4'd2;
            default: exact = 4'd3;
        endcase
        casez (s)
            2'b1?: z = 4'd4;
            default: z = 4'd5;
        endcase
        casex (s)
            2'b0x: x = 4'd6;
            default: x = 4'd7;
        endcase
    end
    initial begin
        s = 2'b00; #1 s = 2'b10; #1 s = 2'b11; #1 s = 2'bx1; #1 s = 2'b01;
    end
endmodule");

            Simulator sim = Run(design, 10);
            CollectionAssert.AreEqual(new[] { "0 0001", "1 0010", "2 0011", "4 0010" }, Changes(sim, "exact"));
            CollectionAssert.AreEqual(new[] { "0 0101", "1 0100", "3 0101" }, Changes(sim, "z"), "x1 is not 1? -- an x is not a z");
            CollectionAssert.AreEqual(new[] { "0 0110", "1 0111", "3 0110" }, Changes(sim, "x"), "x1 matches 0x: an x is a wildcard in casex");
        }

        [Test]
        public void Loops_For_Repeat_While_AndBreak_RunAsWritten()
        {
            Design design = Clean(@"
module t;
    logic [7:0] a = 8'b1101_0010, reversed, ones;
    integer n;
    always_comb begin
        for (int i = 0; i < 8; i++)
            reversed[i] = a[7 - i];
    end
    initial begin
        ones = 0;
        repeat (3) ones = {ones[6:0], 1'b1};
        n = 0;
        while (1) begin
            n = n + 1;
            if (n == 5) break;
        end
    end
endmodule");

            Simulator sim = Run(design, 10);
            Assert.AreEqual("01001011", Now(sim, "reversed"));
            Assert.AreEqual("00000111", Now(sim, "ones"));
            Assert.AreEqual(LogicVec.FromInt64(5, 32).Format(Radix.Binary), Now(sim, "n"));
        }

        [Test]
        public void Wait_HoldsTheProcess_UntilItsConditionIsTrue()
        {
            Simulator sim = Run(Clean(@"
module t;
    logic go = 0, done;
    initial begin wait (go) done = 1; end
    initial #7 go = 1;
endmodule"), 20);

            CollectionAssert.AreEqual(new[] { "0 x", "7 1" }, Changes(sim, "done"));
        }

        [Test]
        public void IntraAssignmentDelays_TakeTheValueNow_AndWriteItLater()
        {
            Simulator sim = Run(Clean(@"
module t;
    logic b = 0, a, d = 0, q, after;
    initial begin
        a = #5 b;          // b's 0, written at 5; the process waits
        after = 1;         // at 5
    end
    initial begin
        q <= #3 d;         // d's 0, written at 3; the process goes straight on
        #1 b = 1;
        #1 d = 1;
    end
endmodule"), 20);

            CollectionAssert.AreEqual(new[] { "0 x", "5 0" }, Changes(sim, "a"));
            CollectionAssert.AreEqual(new[] { "0 x", "5 1" }, Changes(sim, "after"));
            CollectionAssert.AreEqual(new[] { "0 x", "3 0" }, Changes(sim, "q"));
        }

        [Test]
        public void Finish_EndsTheRun_AndSaysWhereFrom()
        {
            Design design = Clean(@"
module t;
    logic clk = 0;
    always #5 clk = ~clk;
    initial #23 $finish;
endmodule");

            var sim = new Simulator(design);
            RunResult result = sim.RunTo(1000);

            Assert.AreEqual(RunStatus.Finished, result.Status);
            Assert.AreEqual(23, result.Time);
            Assert.IsTrue(sim.Finished);
            StringAssert.Contains("$finish (test.sv:5)", sim.History.Messages.Single().Text);
            Assert.AreEqual(RunStatus.Finished, sim.RunTo(2000).Status, "a finished run stays finished");
        }

        [Test]
        public void AWriteToABitAtAPlaceWorkedOutAsItRuns_LandsThere_OrNowhere()
        {
            Simulator sim = Run(Clean(@"
module t;
    logic [3:0] y = 4'b0000;
    logic [2:0] i;
    initial begin
        i = 2; y[i] = 1'b1;
        #1 i = 3'bx; y[i] = 1'b1;       // an unknown place: nothing is written
        #1 i = 6; y[i] = 1'b1;          // past the top: nothing is written
        #1 i = 1; y[i +: 2] = 2'b11;    // bits 2 and 1
    end
endmodule"), 10);

            CollectionAssert.AreEqual(new[] { "0 0100", "3 0110" }, Changes(sim, "y"));
        }

        [Test]
        public void AVariableDeclaredWithAValue_HoldsItFromTheStart()
        {
            Simulator sim = Run(Clean(@"
module t (output logic [3:0] y);
    logic [3:0] seed = 4'd9;
    assign y = seed;
endmodule"), 1);

            CollectionAssert.AreEqual(new[] { "0 1001" }, Changes(sim, "seed"));
            Assert.AreEqual("1001", Now(sim, "y"));
        }

        [Test]
        public void ALoopsOwnVariable_IsNotRecorded_AndAVcdLeavesItOut()
        {
            Design design = Clean(@"
module t;
    logic [3:0] y;
    initial for (int i = 0; i < 4; i++) y[i] = 1'b1;
endmodule");

            SignalInfo local = design.Signals.Single(s => s.Local);
            StringAssert.StartsWith("t.i", local.Name);

            Simulator sim = Run(design, 10);
            Assert.AreEqual(1, sim.History.ChangeCount(design.Find(local.Name)), "only its starting value");

            var vcd = new StringWriter();
            VcdWriter.Write(vcd, design, sim.History, 10);
            StringAssert.DoesNotContain(" i ", vcd.ToString());
            StringAssert.Contains("$scope module t $end", vcd.ToString());
            Assert.AreEqual(1, vcd.ToString().Split("$scope module t ").Length - 1, "the top's scope once, not twice");
        }

        [Test]
        public void ABusyGivenADefaultAndThenSet_DoesNotDipAcrossAPort_SoNegedgeWaitsForTheRealFall()
        {
            // The course's own pattern (its SIM2 design and testbench, counted, not copied): busy is 0
            // by default in an always_comb and 1 in the busy state, and the testbench waits on
            // @(negedge busy). Every time the always_comb runs, busy is 0 for an instant inside it. A
            // port is a continuous assignment, so the testbench's busy never sees that instant -- as the
            // course's testbench needs in ModelSim. Icarus joins the ports into one net and lets it
            // through (Docs/oracle-notes.md).
            Simulator sim = Run(Clean(@"
module worker (input logic clk, start, output logic busy);
    logic [2:0] count = 0;
    logic running = 0;
    always_ff @(posedge clk)
        if (start) begin running <= 1; count <= 0; end
        else if (running) begin count <= count + 3'd1; if (count == 3'd4) running <= 0; end
    always_comb begin
        busy = 1'b0;
        if (running && count <= 3'd4) busy = 1'b1;   // reads count, so it runs again every cycle
    end
endmodule

module t;
    logic clk = 0, start = 0, busy;
    logic [7:0] released;
    worker w (.clk(clk), .start(start), .busy(busy));
    always #5 clk = ~clk;
    initial begin
        @(negedge clk) start = 1;
        @(negedge clk) start = 0;
        @(negedge busy) released = 8'd1;
    end
endmodule"), 200);

            CollectionAssert.AreEqual(new[] { "0 0", "15 1", "65 0" }, Changes(sim, "w.busy"));
            CollectionAssert.AreEqual(new[] { "0 0", "15 1", "65 0" }, Changes(sim, "busy"));
            CollectionAssert.AreEqual(new[] { "0 xxxxxxxx", "65 00000001" }, Changes(sim, "released"),
                "released when busy really fell; Icarus, given this file, releases at 25, the first time the always_comb runs again");
        }

        [Test]
        public void WithinOneModule_AWaitOnTheSameVariable_DoesSeeTheInstant()
        {
            // The standard's update event: busy did go to 0, if only for an instant. It is the port
            // in the test above that hides it, not the engine.
            Simulator sim = Run(Clean(@"
module t;
    logic clk = 0, running = 0, busy;
    logic [2:0] count = 0;
    logic [7:0] released;
    always #5 clk = ~clk;
    always_ff @(posedge clk) begin running <= 1; count <= count + 3'd1; end
    always_comb begin
        busy = 1'b0;
        if (running && count <= 3'd6) busy = 1'b1;
    end
    initial begin
        wait (busy);
        @(negedge busy) released = 8'd1;
    end
endmodule"), 100);

            CollectionAssert.AreEqual(new[] { "0 xxxxxxxx", "15 00000001" }, Changes(sim, "released"),
                "busy rose at 5; at 15 the always_comb ran again and busy was 0 for an instant inside it");
        }

        [Test]
        public void Finish_LetsItsTimeStepSettle_BeforeTheRunEnds()
        {
            // As Icarus does: the nonblocking write, the #0 and the continuous assignment of the
            // instant $finish runs in all still happen.
            Simulator sim = Run(Clean(@"
module t;
    logic clk = 0, q = 0, late = 0;
    wire w = ~clk;
    always #5 clk = ~clk;
    always @(posedge clk) q <= ~q;
    always @(posedge clk) #0 late = ~late;
    initial begin repeat (2) @(posedge clk); $finish; end
endmodule"), 100);

            Assert.IsTrue(sim.Finished);
            Assert.AreEqual(15, sim.Now);
            CollectionAssert.AreEqual(new[] { "0 0", "5 1", "15 0" }, Changes(sim, "q"));
            CollectionAssert.AreEqual(new[] { "0 0", "5 1", "15 0" }, Changes(sim, "late"));
            CollectionAssert.AreEqual(new[] { "0 1", "5 0", "10 1", "15 0" }, Changes(sim, "w"));
        }

        // -----------------------------------------------------------------
        // Runs that cannot go on
        // -----------------------------------------------------------------

        [Test]
        public void AProcessThatNeverWaits_AtRunTime_StopsWithAMessage()
        {
            Design design = Clean(@"
module t;
    logic x = 0;
    initial forever x = ~x;
endmodule");

            var sim = new Simulator(design, null, new SimOptions { MaxProcessSteps = 10_000 });
            RunResult result = sim.RunTo(100);

            Assert.AreEqual(RunStatus.Stopped, result.Status);
            StringAssert.Contains("t.initial@4", sim.History.Messages.Single().Text);
            StringAssert.Contains("without waiting", sim.History.Messages.Single().Text);
        }

        [Test]
        public void TwoProcessesWakingEachOther_ForeverInOneInstant_StopAtTheDeltaLimit()
        {
            Design design = Clean(@"
module t;
    logic a = 0, b = 0;
    always @(a) b = ~b;
    always @(b) a = ~a;
    initial #1 a = 1;
endmodule");

            var sim = new Simulator(design, null, new SimOptions { MaxDeltas = 500 });
            Assert.AreEqual(RunStatus.Stopped, sim.RunTo(10).Status);
            StringAssert.Contains("delta cycles", sim.History.Messages.Single().Text);
        }

        [Test]
        public void TheStimulus_MayNotWriteWhatAProcessWrites()
        {
            Design design = Clean(@"
module t (input logic a);
    logic b;
    initial b = 1;
endmodule");

            Assert.Throws<ArgumentException>(() => new Simulator(design, new Stimulus().Set(0, design.Find("t.b"), Logic4.One)));
            Assert.DoesNotThrow(() => new Simulator(design, new Stimulus().Set(0, design.Find("t.a"), Logic4.One)));
        }

        // -----------------------------------------------------------------
        // What the elaborator refuses, and what it only warns about
        // -----------------------------------------------------------------

        [TestCase("module t; wire w; initial w = 1; endmodule", ElaborationCodes.BadLValue)]
        [TestCase("module t (input logic a); logic y; assign y = a; initial y = 0; endmodule", ElaborationCodes.Drivers)]
        [TestCase("module t; logic y; always y = ~y; endmodule", ElaborationCodes.NeverWaits)]
        [TestCase("module t; logic y; always_comb #1 y = 1; endmodule", ElaborationCodes.NeverWaits)]
        [TestCase("module t; initial break; endmodule", ElaborationCodes.Misplaced)]
        [TestCase("module t; initial send(1); endmodule", ElaborationCodes.NotYet)]
        [TestCase("module t; initial $random; endmodule", ElaborationCodes.NotYet)]
        [TestCase("module t; logic [1:0] y; initial y[2] = 1; endmodule", ElaborationCodes.BadSelect)]
        [TestCase("module t; logic q, d; initial q <= @(d) 1; endmodule", ElaborationCodes.NotYet)]
        public void AnError_IsReported_AndThereIsNoDesign(string text, string code)
        {
            Elaboration e = Elaborate(text);
            CollectionAssert.Contains(Codes(e), code, string.Join("\n", e.Diagnostics));
            Assert.IsNull(e.Design);
        }

        [Test]
        public void Display_IsSkippedWithAWarningOnce_AndDumpingSilently()
        {
            Elaboration e = Elaborate(@"
module t;
    initial begin
        $dumpfile(""t.vcd"");
        $dumpvars(0, t);
        $display(""one"");
        $display(""two"");
        $finish;
    end
endmodule");

            CollectionAssert.AreEqual(new[] { ElaborationCodes.Ignored }, Codes(e));
            Assert.IsNotNull(e.Design);
        }

        [Test]
        public void TheCorpusTrafficLight_RunsToItsStop()
        {
            string text = File.ReadAllText(Ci.RepoRoot.Combine("Tools/ci/Oracle/corpus/behavioural/traffic_light.sv"));
            Elaboration e = Elaborate(text, "traffic_light_tb");
            Assert.IsEmpty(e.Diagnostics, string.Join("\n", e.Diagnostics));

            var sim = new Simulator(e.Design);
            RunResult result = sim.RunTo(10_000);

            Assert.AreEqual(RunStatus.Finished, result.Status);
            Assert.AreEqual(155, result.Time, "reset falls at 12; then eight rising edges (15 to 85), one more (95) and six (105 to 155)");

            // Out of reset the light goes red, red and amber, then green.
            string[] red = Changes(sim, "red");
            StringAssert.StartsWith("0 1", red[0]);
            Assert.Greater(Changes(sim, "green").Length, 1, "it reached green");
        }
    }
}
