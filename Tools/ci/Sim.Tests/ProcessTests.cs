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

        /// <summary>
        /// Two nonblocking writes to one variable in one time step land one after the other, each an update of its own
        /// (IEEE 1800, 10.4.2 and 4.9.4; Ahmad's decision, 2026-11-03): a process waiting on the edge the value between
        /// makes is woken, and -- as Icarus and ModelSim run it -- reads the value the last one left, since every update of
        /// the region lands before anything it wakes runs. A continuous assignment reads only that last value, as ModelSim's
        /// does (Icarus passes the value between on through it); writes to bits apart land together, so a wait on an
        /// expression of them sees no value between, in both. They landed together, and the edge between was lost.
        /// </summary>
        [Test]
        public void TwoNonblockingWritesInOneStep_LandInTurn_AndTheEdgeBetweenWakes()
        {
            Simulator sim = Run(Clean(@"
module t;
    logic a = 1, neg = 0, pos = 0, any = 0, saw;
    logic [1:0] v = 2'b01;
    int across = 0, last = 0;
    wire y;
    logic yfell = 0;
    assign y = a;
    always @(negedge a) begin neg = 1; saw = a; end
    always @(posedge a) pos = 1;
    always @(a) any = 1;
    always @(negedge y) yfell = 1;
    always @(v[1] ^ v[0]) across = across + 1;
    initial begin
        #10 a <= 0; a <= 1;
        #10 v[0] <= 0; v[1] <= 1;
        #10 for (int i = 1; i <= 20000; i++) last <= i;
    end
endmodule"), 40);

            Assert.AreEqual("1", Now(sim, "neg"), "the fall between woke @(negedge a)");
            Assert.AreEqual("1", Now(sim, "pos"), "and the rise after it @(posedge a)");
            Assert.AreEqual("1", Now(sim, "any"));
            Assert.AreEqual("1", Now(sim, "saw"), "it read the value the last write left");
            Assert.AreEqual("0", Now(sim, "yfell"), "an assign reads the last value only, as ModelSim's");
            Assert.AreEqual("0", sim.Value(sim.Design.Find("t.across")).Format(Radix.Unsigned), "bits apart land together: no value between");
            Assert.AreEqual("10", Now(sim, "v"));
            Assert.AreEqual("20000", sim.Value(sim.Design.Find("t.last")).Format(Radix.Unsigned), "twenty thousand in turn are no zero-delay loop");
            Assert.IsFalse(sim.Stopped, string.Join("\n", sim.History.Messages.Select(m => m.Text)));

            // The value between is in the history, in a delta of its own; the waves show what the step settled on.
            SignalId a = sim.Design.Find("t.a");
            var at10 = Enumerable.Range(0, sim.History.ChangeCount(a)).Select(i => sim.History.Change(a, i)).Where(c => c.Time == 10).ToArray();
            CollectionAssert.AreEqual(new[] { "0", "1" }, at10.Select(c => c.Value.Format(Radix.Binary)).ToArray());
            Assert.Less(at10[0].Delta, at10[1].Delta);
            CollectionAssert.AreEqual(new[] { "0 1" }, Changes(sim, "a"));
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

        private const string StartingTogether = @"
module waiter (input logic r, output logic q);
    always @(posedge r) q = 1;
endmodule

module t;
    logic ra, qa;
    initial ra = 1;
    always @(posedge ra) qa = 1;     // written after the initial

    logic rb, qb;
    always @(posedge rb) qb = 1;     // written before it

    initial rb = 1;
    logic r2, q2;
    initial r2 = 1;
    waiter w (.r(r2), .q(q2));       // after the initial, in an instance
endmodule";

        /// <summary>
        /// M5, as ModelSim begins time 0 (Docs/oracle-notes.md, "Time 0"): every always that begins with an event control is
        /// waiting before anything runs, so each sees the edge an initial makes, wherever either is written.
        /// </summary>
        [Test]
        public void AtTimeZero_AsModelSimBegins_EveryAlwaysWaitingOnAnEdge_SeesIt()
        {
            Simulator sim = Run(Clean(StartingTogether), 10);
            Assert.AreEqual("1", Now(sim, "qa"), "written after the initial, and waiting all the same");
            Assert.AreEqual("1", Now(sim, "qb"));
            Assert.AreEqual("1", Now(sim, "q2"));
        }

        /// <summary>M5: the other processes start as written, depth first -- an instance's at its instance statement's place.</summary>
        [Test]
        public void AtTimeZero_AsModelSimBegins_InitialsRunAsWritten_AnInstancesInItsPlace()
        {
            CollectionAssert.AreEqual(new[] { "top before", "child before", "grandchild", "child after", "top after" },
                Run(Clean(@"
module g;
    initial $display(""grandchild"");
endmodule
module c;
    initial $display(""child before"");
    g v ();
    initial $display(""child after"");
endmodule
module t;
    initial $display(""top before"");
    c u ();
    initial $display(""top after"");
endmodule", "t"), 10).History.Messages.Where(m => !m.Text.StartsWith("$")).Select(m => m.Text).ToArray());
        }

        private static string[] Printed(Simulator sim) =>
            sim.History.Messages.Where(m => m.Severity == Severity.Info && !m.Text.StartsWith("$")).Select(m => m.Text).ToArray();

        /// <summary>
        /// M5, as ModelSim runs them (measured): a generate loop's processes iteration by iteration, an instance's among them in
        /// its place, a generate if's where it is written, an include's where the include is.
        /// </summary>
        [Test]
        public void AtTimeZero_AsModelSimBegins_ALoopRunsIterationByIteration_AndAnIfAndAnIncludeInTheirPlaces()
        {
            Simulator loop = Run(Clean(@"
module c;
    initial $display(""instance"");
endmodule
module t;
    for (genvar i = 0; i < 3; i++) begin : g
        initial $display(""A%0d"", i);
        c u ();
        initial $display(""B%0d"", i);
    end
    localparam bit ON = 1;
    if (ON) begin
        initial $display(""in the if"");
    end
    initial $display(""last"");
endmodule", "t"), 10);
            CollectionAssert.AreEqual(new[] { "A0", "instance", "B0", "A1", "instance", "B1", "A2", "instance", "B2", "in the if", "last" },
                Printed(loop));

            var files = new MemorySourceFiles();
            files.Add("inc.svh", "initial $display(\"included 1\");\ninitial $display(\"included 2\");\n");
            files.Add("t.sv", "module t;\n    initial $display(\"before\");\n    `include \"inc.svh\"\n    initial $display(\"after\");\nendmodule\n");
            Elaboration included = Elaborator.Elaborate(SyntaxReader.Read("t.sv", files), "t");
            Assert.IsFalse(included.HasErrors, string.Join("\n", included.Diagnostics));
            CollectionAssert.AreEqual(new[] { "before", "included 1", "included 2", "after" }, Printed(Run(included.Design, 10)),
                "an include's processes where the include is, whatever their lines in their own file (the M5 review)");
        }

        /// <summary>M5: an always_latch makes its first run after the initials, as an always_comb does.</summary>
        [Test]
        public void AtTimeZero_AsModelSimBegins_AnAlwaysLatchsFirstRun_ComesAfterTheInitials()
        {
            Simulator sim = Run(Clean(@"
module t;
    logic en = 1, d = 1, l, seen;
    always_latch if (en) l = d;
    initial seen = l;
endmodule"), 10);
            CollectionAssert.AreEqual(new[] { "0 x" }, Changes(sim, "seen"));
            Assert.AreEqual("1", Now(sim, "l"));
        }

        /// <summary>
        /// M5: a stimulus's writes at time 0 are a testbench's -- what the exported testbench's initial makes -- so a flip-flop
        /// waiting on an asynchronous reset sees the reset a stimulus raises at time 0, as it would under ModelSim.
        /// </summary>
        [TestCase(StartOrder.ModelSim, "0")]
        [TestCase(StartOrder.Icarus, "x")]
        public void AtTimeZero_AStimulussWrite_IsSeenAsTheStartSays(StartOrder start, string q)
        {
            Design design = Clean(@"
module t (input logic clk, rst, output logic q);
    always_ff @(posedge clk, posedge rst)
        if (rst) q <= 0;
        else q <= ~q;
endmodule", "t");
            var stimulus = new Stimulus();
            stimulus.Set(0, design.Find("t.rst"), Logic4.One);
            stimulus.Set(0, design.Find("t.clk"), Logic4.Zero);
            var sim = new Simulator(design, stimulus, new SimOptions { StartOrder = start });
            sim.RunTo(1);
            Assert.AreEqual(q, Now(sim, "q"));
        }

        /// <summary>
        /// M5's review: working out where 2,048 copies of a generate loop are written costs about what building the run does
        /// anyway, where the first version's search for each block's first place grew with the square of the design.
        /// </summary>
        [Test]
        public void ModelSimsStart_OfABigGenerateLoop_CostsAboutWhatIcarussDoes()
        {
            Design design = Clean(@"
module inv1 (input logic a, output logic y);
    always_comb y = ~a;
endmodule
module t;
    logic [2047:0] a, y;
    for (genvar i = 0; i < 2048; i++) begin : g
        inv1 u (.a(a[i]), .y(y[i]));
        initial a[i] = 1'b0;
    end
endmodule", "t");
            double Build(StartOrder start)
            {
                var watch = System.Diagnostics.Stopwatch.StartNew();
                new Simulator(design, null, new SimOptions { StartOrder = start }).RunTo(0);
                return watch.Elapsed.TotalMilliseconds;
            }
            Build(StartOrder.Icarus);   // warmed
            double icarus = Build(StartOrder.Icarus), modelSim = Build(StartOrder.ModelSim);
            Assert.Less(modelSim, 3 * icarus + 100, $"ModelSim's start {modelSim:F0} ms, Icarus's {icarus:F0} ms");
        }

        [Test]
        public void AtTimeZero_AsIcarusBegins_AnInstancesProcessesStartFirst_AndAModulesOwnRunAsWritten()
        {
            Simulator icarus = Run(Clean(StartingTogether), 10, new SimOptions { StartOrder = StartOrder.Icarus });
            Assert.AreEqual("x", Now(icarus, "qa"), "starts too late to see ra rise");
            Assert.AreEqual("1", Now(icarus, "qb"), "already waiting");
            Assert.AreEqual("1", Now(icarus, "q2"), "an instance: it starts first");
        }

        private const string ReadingAWireAtZero = @"
module t;
    logic [3:0] a = 4'd5, seen, later;
    wire [3:0] y = a + 4'd1;
    initial begin seen = y; #0 later = y; end
    always @(y) $display(""y changed to %0d"", y);
endmodule";

        [Test]
        public void AtTimeZero_AsIcarusBegins_ProcessesStart_OnceWhatHasNoDelayHasSettled()
        {
            // As Icarus starts them: an initial reading a wire at time 0 sees it worked out from the
            // starting values, not the x it has before its assign has run.
            Simulator sim = Run(Clean(ReadingAWireAtZero), 10, new SimOptions { StartOrder = StartOrder.Icarus });
            CollectionAssert.AreEqual(new[] { "0 0110" }, Changes(sim, "seen"));
            CollectionAssert.IsEmpty(sim.History.Messages.Where(m => m.Text.StartsWith("y changed")), "and no always sees it settle");
        }

        /// <summary>
        /// M5, as ModelSim begins: continuous logic settles after the processes start, as changes of its own -- an initial
        /// reading a wire at once sees x and after #0 the value, and an always waiting on it sees it change.
        /// </summary>
        [Test]
        public void AtTimeZero_AsModelSimBegins_AWireSettlesAfterTheProcessesStart_AsAChange()
        {
            Simulator sim = Run(Clean(ReadingAWireAtZero), 10);
            CollectionAssert.AreEqual(new[] { "0 xxxx" }, Changes(sim, "seen"));
            CollectionAssert.AreEqual(new[] { "0 0110" }, Changes(sim, "later"));
            CollectionAssert.AreEqual(new[] { "y changed to 6" }, sim.History.Messages.Where(m => m.Text.StartsWith("y changed")).Select(m => m.Text));
        }

        /// <summary>M5: an always_comb makes its first run after the other processes have started, as the standard has it (9.2.2.2.2).</summary>
        [Test]
        public void AtTimeZero_AsModelSimBegins_AnAlwaysCombsFirstRun_ComesAfterTheInitials()
        {
            Simulator sim = Run(Clean(@"
module t;
    logic a = 1, y, seen;
    always_comb y = a;
    initial seen = y;
endmodule"), 10);
            CollectionAssert.AreEqual(new[] { "0 x" }, Changes(sim, "seen"));
            Assert.AreEqual("1", Now(sim, "y"));
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
        public void ADelayThatWorksOutNegative_IsAnUnsignedTime_SoLongItNeverComesDue()
        {
            // IEEE 1800, 9.4.1: a negative delay is read as a two's-complement unsigned number as wide as a time -- 2^64 less
            // its size -- and an x or z one as no delay. Icarus 12 waits 2^64 - 5 for an integer's -5; ModelSim makes a
            // literal one 0, and an integer's 2^32 - 5 (Docs/oracle-notes.md, "Processes").
            Simulator sim = Run(Clean(@"
module t;
    integer d = -5;
    logic signed [3:0] s = -4'sd1;
    real r = -2.5;
    logic a, b, c, e, f, g, h, unknown;
    initial #d a = 1;
    initial #(-1) b = 1;
    initial #s c = 1;
    initial #r e = 1;
    initial f = #(-2) 1'b1;
    initial g <= #(-3) 1'b1;
    initial begin #(d + 10) h = 1; end
    initial #(1'bx) unknown = 1;
endmodule"), 1_000_000);

            foreach (string never in new[] { "a", "b", "c", "e", "f", "g" })
                CollectionAssert.AreEqual(new[] { "0 x" }, Changes(sim, never), never + " waits past any run");
            CollectionAssert.AreEqual(new[] { "0 x", "5 1" }, Changes(sim, "h"), "-5 + 10 is no negative delay");
            CollectionAssert.AreEqual(new[] { "0 1" }, Changes(sim, "unknown"), "an unknown delay is no delay");
            Assert.IsFalse(sim.Stopped);

            // The same in a module with a timescale, where a delay is counted in ticks.
            Simulator scaled = Run(Clean(@"
`timescale 1ns/1ps
module t;
    real r = -2.5;
    logic a, b;
    initial #r a = 1;
    initial #(-1) b = 1;
endmodule"), 1_000_000_000);

            CollectionAssert.AreEqual(new[] { "0 x" }, Changes(scaled, "a"));
            CollectionAssert.AreEqual(new[] { "0 x" }, Changes(scaled, "b"));
        }

        /// <summary>
        /// A gate's or an assign's delay past what a run can count never comes due either, as a process's does (the 16d
        /// sweep, elaborator-7 and engine-4), and the elaborator says so. Counted in ticks it was held at the largest, and
        /// a change after t = 0 added the time to it unchecked: it came due at once, at a negative time, the run's clock
        /// went back past 0, and the history showed y's new value from the start.
        /// </summary>
        [TestCase("assign #(64'd9223372036854775807) y = a;")]
        [TestCase("assign #1e19 y = a;")]
        [TestCase("buf #(64'hFFFFFFFFFFFFFFFF) g (y, a);")]
        public void AGateOrAssignDelayPastWhatARunCanCount_NeverComesDue_AndIsSaid(string driver)
        {
            Elaboration e = Elaborate(@"
module t;
    logic a;
    wire y;
    " + driver + @"
    initial begin a = 0; #5 a = 1; #5 a = 0; end
endmodule");

            Assert.IsFalse(e.HasErrors, string.Join("\n", e.Diagnostics));
            Assert.IsTrue(e.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Warning && d.Message.Contains("never comes due")),
                "the elaborator says so: " + string.Join("\n", e.Diagnostics));

            Simulator sim = Run(e.Design, 20);
            Assert.AreEqual(20, sim.Now, "the run's clock goes forward only");
            CollectionAssert.AreEqual(new[] { "0 x" }, Changes(sim, "y"), "y waits past any run");
            for (int i = 0; i < sim.History.ChangeCount(sim.Design.Find("t.y")); i++)
                Assert.GreaterOrEqual(sim.History.Change(sim.Design.Find("t.y"), i).Time, 0);
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
        public void Stop_SuspendsTheRun_AtTheEndOfItsTimeStep_AndRunningOnCarriesOnAfterIt()
        {
            Design design = Clean(@"
module t;
    logic clk = 0;
    logic [3:0] n = 0;
    logic after;
    always #5 clk = ~clk;
    initial begin
        #12 n <= 4'd3;
        $stop;
        after = 1;
        #8 n = 4'd7;
        #5 $finish;
    end
endmodule");

            var sim = new Simulator(design);
            RunResult result = sim.RunTo(1000);

            Assert.AreEqual(RunStatus.Suspended, result.Status);
            Assert.AreEqual(12, result.Time);
            Assert.IsTrue(sim.Suspended);
            Assert.IsFalse(sim.Stopped, "suspended is not stopped: the run can go on");
            Assert.IsFalse(sim.Finished);
            CollectionAssert.AreEqual(new long[] { 12 }, sim.History.Suspensions);
            StringAssert.Contains("$stop (test.sv:9)", sim.History.Messages.Single().Text);
            CollectionAssert.AreEqual(new[] { "0 0000", "12 0011" }, Changes(sim, "n"), "the rest of its step happened: the nonblocking write is in");
            Assert.AreEqual("x", Now(sim, "after"), "and the statement after the $stop has not run");

            result = sim.RunTo(1000);

            Assert.AreEqual(RunStatus.Finished, result.Status, "running on carries on after the $stop");
            Assert.AreEqual(25, result.Time);
            CollectionAssert.AreEqual(new[] { "0 x", "12 1" }, Changes(sim, "after"), "in the same time step");
            CollectionAssert.AreEqual(new[] { "0 0000", "12 0011", "20 0111" }, Changes(sim, "n"));
            CollectionAssert.AreEqual(new[] { "0 0", "5 1", "10 0", "15 1", "20 0", "25 1" }, Changes(sim, "clk"));
            CollectionAssert.AreEqual(new long[] { 12 }, sim.History.Suspensions);

            // The step it carried on is numbered on from the one it suspended, so the last change of the
            // time is still the one the time settled on.
            SignalId nId = design.Find("t.n"), afterId = design.Find("t.after");
            int nAt12 = sim.History.ChangeCount(nId) - 2;
            int afterAt12 = sim.History.ChangeCount(afterId) - 1;
            Assert.AreEqual(12, sim.History.Change(nId, nAt12).Time, "sanity: n's change at 12");
            Assert.Greater(sim.History.Change(afterId, afterAt12).Delta, sim.History.Change(nId, nAt12).Delta);
        }

        [Test]
        public void Stop_EachTime_SuspendsTheRunAgain_AndARunWithNoneLeftReachesItsTime()
        {
            Design design = Clean(@"
module t;
    logic c = 0;
    always #1 c = ~c;
    initial begin
        #5 $stop;
        #5 $stop;
    end
endmodule");

            var sim = new Simulator(design);
            Assert.AreEqual(RunStatus.Suspended, sim.RunTo(100).Status);
            Assert.AreEqual(5, sim.Now);
            Assert.AreEqual(RunStatus.Suspended, sim.RunTo(100).Status);
            Assert.AreEqual(10, sim.Now);

            RunResult result = sim.RunTo(100);
            Assert.AreEqual(RunStatus.ReachedTime, result.Status);
            Assert.AreEqual(100, result.Time);
            CollectionAssert.AreEqual(new long[] { 5, 10 }, sim.History.Suspensions);
        }

        [Test]
        public void Stop_InTheTimeStepAFinishRunsIn_Finishes()
        {
            Design design = Clean(@"
module t;
    initial #5 $stop;
    initial #5 $finish;
endmodule");

            var sim = new Simulator(design);
            RunResult result = sim.RunTo(100);

            Assert.AreEqual(RunStatus.Finished, result.Status, "$finish ends the run whatever else that step did");
            Assert.AreEqual(5, result.Time);
            Assert.IsFalse(sim.Suspended);
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

            // y alone: the local is named t.i#1, so refusing " i " held nothing (the 16d sweep, engine-16).
            Assert.AreEqual(1, vcd.ToString().Split("$var ").Length - 1, "the one variable of the design's own, y: " + vcd);
            StringAssert.Contains(" y ", vcd.ToString());
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
        [TestCase("module t; initial send(1); endmodule", ElaborationCodes.UnknownName)]
        [TestCase("module t; task send(input logic v); send(v); endtask initial send(1); endmodule", ElaborationCodes.Recursion)]
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
        public void Display_PrintsIntoTheTranscript_AndDumpingIsSkippedSilently()
        {
            Elaboration e = Elaborate(@"
module t;
    initial begin
        $dumpfile(""t.vcd"");
        $dumpvars(0, t);
        $display(""one"");
        #5 $display(""two"");
        $finish;
    end
endmodule");

            Assert.IsEmpty(e.Diagnostics, string.Join("\n", e.Diagnostics));
            Simulator sim = Run(e.Design, 100);
            CollectionAssert.AreEqual(new[] { "0 one", "5 two" },
                sim.History.Messages.Where(m => !m.Text.StartsWith("$finish", StringComparison.Ordinal)).Select(m => $"{m.Time} {m.Text}"));
        }

        [Test]
        public void TheCorpusTrafficLight_RunsToItsStop()
        {
            string text = File.ReadAllText(Ci.RepoRoot.Combine("Tools/ci/Oracle/corpus/behavioural/traffic_light.sv"));
            Elaboration e = Elaborate(text, "traffic_light_tb");
            Assert.IsEmpty(e.Diagnostics, string.Join("\n", e.Diagnostics));

            var sim = new Simulator(e.Design);
            RunResult result = sim.RunTo(10_000);

            Assert.AreEqual(RunStatus.Suspended, result.Status, "its testbench ends on $stop, which suspends the run");
            Assert.AreEqual(155, result.Time, "reset falls at 12; then eight rising edges (15 to 85), one more (95) and six (105 to 155)");

            // Out of reset the light goes red, red and amber, then green.
            string[] red = Changes(sim, "red");
            StringAssert.StartsWith("0 1", red[0]);
            Assert.Greater(Changes(sim, "green").Length, 1, "it reached green");
        }

        [Test]
        public void NextEventTime_IsWhenSomethingIsDue_AndNullOnceNothingIs()
        {
            Design ending = Clean(
                "module t;\n" +
                "    logic a = 0;\n" +
                "    initial begin #10 a = 1; #5 a = 0; end\n" +
                "    always @(a) ;\n" +
                "endmodule\n");
            var sim = new Simulator(ending);

            Assert.AreEqual(0, sim.NextEventTime, "before the run starts");
            sim.RunTo(12);
            Assert.AreEqual(15, sim.NextEventTime);
            RunResult run = sim.RunTo(1000);
            Assert.AreEqual(RunStatus.ReachedTime, run.Status);
            Assert.IsNull(sim.NextEventTime, "the initial has ended and the always waits on a change that never comes");

            Design ticking = Clean("module t; logic clk = 0; always #5 clk = ~clk; endmodule\n");
            var clock = new Simulator(ticking);
            clock.RunTo(1000);
            Assert.AreEqual(1005, clock.NextEventTime, "a free-running clock always has a next edge");
        }

        [TestCase(StartOrder.ModelSim)]
        [TestCase(StartOrder.Icarus)]
        public void ProcessesStartedIn_IsTheRoundOfTimeZeroAnInitialsFirstAssignmentIsMadeIn(StartOrder start)
        {
            Design design = Clean(
                "module t;\n" +
                "    logic a = 1, b, c, x;\n" +
                "    assign b = ~a;\n" +
                "    assign c = ~b;\n" +
                "    initial x = 1;\n" +
                "    initial x <= 0;\n" +
                "endmodule\n");
            var sim = new Simulator(design, null, new SimOptions { StartOrder = start });
            Assert.AreEqual(-1, sim.ProcessesStartedIn, "before the run");

            sim.RunTo(1);
            SignalId x = design.Find("t.x");
            if (start == StartOrder.Icarus)
                Assert.Greater(sim.ProcessesStartedIn, 0, "after the two assigns had settled from a's starting value");
            else
                Assert.AreEqual(0, sim.ProcessesStartedIn, "in the first round, before the assigns' first values arrive (M5)");
            Assert.AreEqual(sim.ProcessesStartedIn, sim.History.Change(x, 1).Delta, "x = 1, made as the processes start");
            Assert.Greater(sim.History.Change(x, 2).Delta, sim.ProcessesStartedIn, "x <= 0, made after them");
        }
    }
}
