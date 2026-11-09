using System;
using System.Diagnostics;
using System.Linq;
using BitSorter.SystemVerilog;
using BitSorter.SystemVerilog.Syntax;
using NUnit.Framework;

namespace BitSorter.Sim.Tests
{
    /// <summary>
    /// What keeps the engine safe to run in a browser tab: a loop that never settles stops with a
    /// message rather than hanging, so does a run that would keep more than the tab can hold, a run
    /// allocates nothing once warm, and it is fast enough.
    /// </summary>
    public class GuardrailTests
    {
        /// <summary>y = NOR(enable, y): stable while enable is 1, oscillating once it falls -- every delta with no delay, every tick with one.</summary>
        private static (Design, Stimulus, SignalId) Ring(Delay delay)
        {
            var b = new NetlistBuilder("ring");
            SignalId enable = b.Variable("enable"), y = b.Net("y");
            b.Gate(GateKind.Nor, "g", y, delay, enable, y);
            var s = new Stimulus().Set(0, enable, Logic4.One).Set(5, enable, Logic4.Zero);
            return (b.Build(), s, y);
        }

        [Test]
        public void AZeroDelayLoop_StopsWithAMessageNamingIt_InsteadOfHanging()
        {
            (Design design, Stimulus stimulus, SignalId y) = Ring(Delay.Zero);
            var sim = new Simulator(design, stimulus, new SimOptions { MaxDeltas = 1000 });

            var clock = Stopwatch.StartNew();
            RunResult result = sim.RunTo(100);

            Assert.Less(clock.Elapsed.TotalSeconds, 5, "the loop took too long to be caught");
            Assert.AreEqual(RunStatus.Stopped, result.Status);
            Assert.AreEqual(5, result.Time, "it stops in the time step the loop started in");
            Assert.AreEqual(1, sim.History.Messages.Count);
            StringAssert.Contains("Still changing: " + design[y].Name + ".", sim.History.Messages[0].Text, "names the gate's output, and nothing else");
            Assert.AreEqual(RunStatus.Stopped, sim.RunTo(200).Status, "a stopped run stays stopped");
        }

        /// <summary>
        /// A zero-delay loop's message names what was still changing (the 16d sweep, engine-17): the test above asked only for
        /// the letter y, which the message's own words have, so emptying the names held nothing -- and a loop through a process
        /// and an assign, whose assign waited to be worked out again as the delta began, was named by nothing at all: "Still
        /// changing: ." A force that follows itself says so, and at most eight names are given.
        /// </summary>
        [TestCase("logic [3:0] x = '0; wire [3:0] y; assign y = ~x; always @(y) x = y;", "t.y", false)]
        [TestCase("wire w; assign w = 1'b0; initial #1 force w = ~w;", "t.w", true)]
        public void AZeroDelayLoop_NamesWhatWasStillChanging(string design, string named, bool forced)
        {
            SyntaxTree tree = SyntaxReader.ReadText("module t; " + design + " endmodule", "test.sv");
            Elaboration e = Elaborator.Elaborate(tree, null);
            Assert.IsFalse(e.HasErrors, string.Join("\n", e.Diagnostics));
            var sim = new Simulator(e.Design, null, new SimOptions { MaxDeltas = 500 });

            Assert.AreEqual(RunStatus.Stopped, sim.RunTo(10).Status);
            string said = sim.History.Messages.Last(m => m.Severity == Severity.Error).Text;
            StringAssert.Contains("Still changing: " + named, said);
            Assert.AreEqual(forced, said.Contains("A force follows what it reads"), said);
        }

        [Test]
        public void AZeroDelayLoopOfManyParts_NamesEightOfThem()
        {
            var b = new NetlistBuilder("rings");
            SignalId enable = b.Variable("enable");
            for (int i = 0; i < 10; i++)
            {
                SignalId y = b.Net("y" + i);
                b.Gate(GateKind.Nor, "g" + i, y, Delay.Zero, enable, y);
            }
            var sim = new Simulator(b.Build(), new Stimulus().Set(0, enable, Logic4.One).Set(5, enable, Logic4.Zero), new SimOptions { MaxDeltas = 500 });

            Assert.AreEqual(RunStatus.Stopped, sim.RunTo(10).Status);
            string said = sim.History.Messages.Single().Text;
            string names = said.Substring(said.IndexOf("Still changing: ", StringComparison.Ordinal) + "Still changing: ".Length);
            names = names.Substring(0, names.IndexOf(". ", StringComparison.Ordinal));
            Assert.AreEqual(8, names.Split(", ").Length, said);
            StringAssert.Contains("y0", names);
        }

        [Test]
        public void ARunThatWouldKeepTooMuch_StopsWithAMessage_InsteadOfFillingTheTab()
        {
            // An oscillator with nothing to end it, run far: a change and a transition every tick.
            (Design design, Stimulus stimulus, _) = Ring(Delay.Of(1));
            var sim = new Simulator(design, stimulus, new SimOptions { MaxRecorded = 1000 });

            RunResult result = sim.RunTo(1_000_000);

            Assert.AreEqual(RunStatus.Stopped, result.Status);
            Assert.Less(result.Time, 1000, "it stops near where the history filled, not at the end time");
            Assert.LessOrEqual(sim.History.Recorded, 1010, "a time step's worth over, and the message");
            StringAssert.Contains("more than the 1,000 a run may keep", sim.History.Messages.Last().Text);
            StringAssert.Contains("$finish", sim.History.Messages.Last().Text, "and says the way out");
            Assert.AreEqual(RunStatus.Stopped, sim.RunTo(2_000_000).Status, "a stopped run stays stopped");
        }

        /// <summary>
        /// The bound is on what the history holds, not only on how many things it holds (the 16d sweep, engine-7): a change of
        /// a 65,536-bit vector costs some 16 KB, two hundred times a small one's, so counting at 80 bytes each let wide values
        /// fill a tab long before the count was reached. And it is asked after every delta, not only once a time step has
        /// settled: a zero-delay loop of a wide value kept a delta's worth for every one of its ten thousand deltas first.
        /// </summary>
        [TestCase("logic [65535:0] w = '0; initial forever #1 w = ~w;", 1000)]
        [TestCase("logic [65535:0] x = '0; wire [65535:0] y; assign y = ~x; always @(y) x = y;", 0)]
        public void AHistoryOfWideValues_IsBoundByItsSize_AsItsCountIs(string design, long before)
        {
            SyntaxTree tree = SyntaxReader.ReadText("module t; " + design + " endmodule", "test.sv");
            Elaboration e = Elaborator.Elaborate(tree, null);
            Assert.IsFalse(e.HasErrors, string.Join("\n", e.Diagnostics));

            // 10,000 things at about 80 bytes each: some 800 KB, or fifty of these changes.
            var sim = new Simulator(e.Design, null, new SimOptions { MaxRecorded = 10_000, MaxDeltas = 2000 });
            RunResult result = sim.RunTo(5000);

            Assert.AreEqual(RunStatus.Stopped, result.Status);
            Assert.LessOrEqual(result.Time, before, "it stops near where the history filled");
            StringAssert.Contains("a run may keep", sim.History.Messages.Last().Text, "the history's bound, not a loop's: " + sim.History.Messages.Last().Text);
            Assert.Less(sim.History.ChangeCount(sim.Design.Find(sim.Design.Signals.First(s => s.Width == 65536).Name)), 100);
        }

        /// <remarks>
        /// A change keeps its value, and a value wider than 64 bits keeps both its planes, 16 bytes for every 64 bits: a
        /// 65,536-bit counter keeps 16 KB a change, two hundred times the 80 bytes the limit counts each record as, and a
        /// million-bit one filled a browser's 2 GB in about 8,000 changes of the 4 million allowed (Part 16d sweep).
        /// </remarks>
        [Test]
        public void ARunOfAWideValue_StopsWhenWhatItKeepsIsFull_NotWhenItHasKeptAsManyChanges()
        {
            Design design = Elaborator.Elaborate(SystemVerilog.Syntax.SyntaxReader.ReadText(@"
module wide_tb;
    logic [65535:0] v = 0;
    always #1 v = v + 1;
endmodule", "wide.sv")).Design;
            var sim = new Simulator(design, null, new SimOptions { MaxRecorded = 10_000 });

            long before = GC.GetTotalMemory(true);
            RunResult result = sim.RunTo(1_000_000);
            long kept = GC.GetTotalMemory(true) - before;
            TestContext.WriteLine($"stopped at t = {result.Time}, keeping {kept:N0} bytes");

            Assert.AreEqual(RunStatus.Stopped, result.Status);
            Assert.Less(kept, 10_000 * 80 * 2, "a full history holds about what the limit says, 80 bytes a record");
            StringAssert.Contains("more than the 10,000 a run may keep", sim.History.Messages.Last().Text);
            GC.KeepAlive(sim);
        }

        /// <remarks>
        /// An event is one wait, and between two a process may take up to <see cref="SimOptions.MaxProcessSteps"/> and a call
        /// from an expression ten million more, so so many events says little of what a run costs: what the import's run is
        /// held to besides (Part 16d sweep). Each wake here is two instructions of the process and some three thousand of
        /// the function's.
        /// </remarks>
        [Test]
        public void ARunGivenSoManySteps_PausesOnceItHasTakenThem_FunctionsIncluded_AndCarriesOn()
        {
            Design design = Elaborator.Elaborate(SystemVerilog.Syntax.SyntaxReader.ReadText(@"
module busy_tb;
    integer k = 0;
    function automatic integer spin(input integer n);
        integer i;
        for (i = 0; i < n; i++) spin = i;
    endfunction
    initial forever begin k = spin(1000); #1; end
endmodule", "busy.sv")).Design;
            var sim = new Simulator(design);

            RunResult first = sim.RunTo(1_000_000, maxSteps: 100_000);

            Assert.AreEqual(RunStatus.Paused, first.Status);
            Assert.Less(first.Time, 100, "the function's steps counted, not the process's two a wake alone");
            Assert.That(sim.Steps, Is.InRange(100_000, 110_000), "paused between time steps once it had taken them");
            Assert.AreEqual(RunStatus.ReachedTime, sim.RunTo(first.Time + 10).Status, "and carries on where it was");
            Assert.Greater(sim.Steps, 110_000);
        }

        [Test]
        public void ALoopWithADelay_OscillatesForAsLongAsItIsRun()
        {
            (Design design, Stimulus stimulus, SignalId y) = Ring(Delay.Of(1));
            var sim = new Simulator(design, stimulus);

            Assert.AreEqual(RunStatus.ReachedTime, sim.RunTo(100).Status);
            Assert.Greater(sim.History.SettledChanges(y).Count, 90, "it should have toggled about every tick");
        }

        [Test]
        public void ARunOnceWarm_AllocatesNothing_WithRecordingOff()
        {
            (Design design, Stimulus stimulus, _) = Ring(Delay.Of(1));
            var sim = new Simulator(design, stimulus, new SimOptions { RecordChanges = false, RecordTransitions = false });
            sim.RunTo(2000);

            long before = GC.GetAllocatedBytesForCurrentThread();
            sim.RunTo(20000);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.AreEqual(0, allocated, "the engine allocated per event once warm");
        }

        [Test]
        public void ProcessesOnceWarm_AllocateNothingEither()
        {
            // A clock from an always, a counter in always_ff with its NBA, a decoder in always_comb
            // with a case: every kind of instruction the scheduler runs on every edge.
            Design design = Elaborator.Elaborate(SystemVerilog.Syntax.SyntaxReader.ReadText(@"
module counter;
    logic clk = 0;
    logic [7:0] q = 0;
    logic [1:0] phase;
    always #5 clk = ~clk;
    always_ff @(posedge clk) q <= q + 8'd1;
    always_comb
        case (q[1:0])
            2'd0: phase = 2'd3;
            2'd1: phase = 2'd2;
            default: phase = q[1:0] - 2'd2;
        endcase
endmodule", "counter.sv")).Design;

            var sim = new Simulator(design, null, new SimOptions { RecordChanges = false, RecordTransitions = false });
            sim.RunTo(5000);
            LogicVec counted = sim.Value(design.Find("counter.q"));

            long before = GC.GetAllocatedBytesForCurrentThread();
            sim.RunTo(50000);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.AreEqual(0, allocated, "running processes allocated once warm");
            Assert.AreNotEqual(counted, sim.Value(design.Find("counter.q")), "and it really ran: 4,500 more edges");
        }

        /// <summary>
        /// A function calling itself costs each call what it puts aside of the call under way -- an array of its input and its
        /// result -- and no more (the review of the 16d engine batch). 595cf504 kept a call busy for the whole of the function,
        /// not only while its arguments were worked out, so every call made through it from inside took an argument array
        /// of its own: 968,000 bytes over two thousand evaluations became 1,472,000. Measured once the runtime has compiled
        /// what runs, on a simulator of its own, as <see cref="RecordingAllocationTests"/> is.
        /// </summary>
        [Test]
        public void AFunctionCallingItself_CostsEachCallWhatItPutsAside_AndNoArgumentArray()
        {
            const string Text = @"
module t;
    function automatic int fact(input int n);
        if (n <= 1) return 1;
        else return n * fact(n - 1);
    endfunction
    int a = 3, y;
    always @(a) y = fact(a);
    initial forever #1 a = (a == 3) ? 10 : 3;
endmodule";
            Design design = Elaborator.Elaborate(SyntaxReader.ReadText(Text, "t.sv"), "t").Design;
            var options = new SimOptions { RecordChanges = false, RecordTransitions = false };
            new Simulator(design, null, options).RunTo(4000);

            var sim = new Simulator(design, null, options);
            sim.RunTo(2000);
            long before = GC.GetAllocatedBytesForCurrentThread();
            sim.RunTo(4000);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            // Two thousand evaluations, fact(10) and fact(3) by turns: nine calls and two from inside, a thousand times over.
            const int calls = 11_000;
            long aside = 3 * IntPtr.Size + 2 * System.Runtime.CompilerServices.Unsafe.SizeOf<LogicVec>();
            Assert.AreEqual("6", sim.Value(design.Find("t.y")).Format(Radix.Unsigned), "and it really ran");
            Assert.LessOrEqual(allocated, calls * aside + 16 * 1024,
                $"{allocated:N0} bytes, {allocated / (double)calls:F1} a call, where what a call puts aside is {aside}");
        }

        [Test]
        public void TheCounter_SeesRecordingAllocate()
        {
            (Design design, Stimulus stimulus, _) = Ring(Delay.Of(1));
            var sim = new Simulator(design, stimulus);
            sim.RunTo(10);

            long before = GC.GetAllocatedBytesForCurrentThread();
            sim.RunTo(20000);
            Assert.Greater(GC.GetAllocatedBytesForCurrentThread() - before, 0);
        }

        /// <summary>
        /// The corpus processor, read, elaborated and run to its end with everything recorded, as the
        /// workbench will run it: the best of five. The floor is generous, for a slow CI machine; the
        /// measured rate is printed, and Docs/architecture-5.md keeps the browser's.
        /// </summary>
        [Test]
        public void TheCorpusProcessor_RunsFastEnough()
        {
            string root = Ci.RepoRoot.Combine("Tools/ci/Oracle/corpus");
            var sources = new SystemVerilog.Syntax.MemorySourceFiles();
            foreach (string path in System.IO.Directory.GetFiles(root, "*.sv*", System.IO.SearchOption.AllDirectories))
                sources.Add(System.IO.Path.GetRelativePath(root, path).Replace('\\', '/'), System.IO.File.ReadAllText(path));

            var files = new MemorySimFiles();
            foreach (string image in new[] { "mcpu_program.hex", "mcpu_data.hex", "mcpu_expected.hex" })
                files.Add(image, System.IO.File.ReadAllText(System.IO.Path.Combine(root, "processor", image)));

            double best = double.MaxValue, bestElaborate = double.MaxValue, bestSimulate = double.MaxValue;
            long events = 0;
            Simulator sim = null;

            for (int run = 0; run < 5; run++)
            {
                var clock = Stopwatch.StartNew();
                // mcpu_tb by name: mcpu.sv holds mcpu_demo_tb too, the start page's sample (Part 15h-6).
                Design design = Elaborator.Elaborate(SystemVerilog.Syntax.SyntaxReader.Read("processor/mcpu.sv", sources), "mcpu_tb").Design;
                double elaborated = clock.Elapsed.TotalSeconds;
                sim = new Simulator(design, null, new SimOptions { Files = files });
                RunResult result = sim.RunTo(1_000_000);
                clock.Stop();

                Assert.AreEqual(RunStatus.Finished, result.Status);
                best = Math.Min(best, clock.Elapsed.TotalSeconds);
                bestElaborate = Math.Min(bestElaborate, elaborated);
                bestSimulate = Math.Min(bestSimulate, clock.Elapsed.TotalSeconds - elaborated);
                events = result.Events;
            }

            const int Cycles = 2716;
            TestContext.WriteLine($"mcpu: {Cycles} cycles, {events} events, best {best * 1000:F0} ms: " +
                $"{Cycles / best:F0} cycles and {events / best:F0} events a second, reading and elaborating included; " +
                $"best read and elaborate {bestElaborate * 1000:F0} ms, best simulate {bestSimulate * 1000:F0} ms: " +
                $"{Cycles / bestSimulate:F0} cycles and {events / bestSimulate:F0} events a second simulating");

            Assert.IsTrue(sim.History.Messages.Any(m => m.Text.StartsWith("PASS", StringComparison.Ordinal)), "sanity: the program ran to its end and passed");
            Assert.Greater(Cycles / best, 2000, "the engine is far slower than it should be on the processor");
        }

        /// <summary>
        /// A 64-bit ripple-carry adder of gates -- 320 of them -- run through 200 random additions.
        /// The floor is generous, to hold on a slow CI machine; the measured rate is printed.
        /// </summary>
        [Test]
        public void AGateLevelAdder_RunsFastEnough()
        {
            const int Bits = 64;
            var b = new NetlistBuilder("adder");
            var a = new SignalId[Bits];
            var bb = new SignalId[Bits];
            SignalId cin = b.Variable("cin");
            SignalId carry = cin;

            for (int i = 0; i < Bits; i++)
            {
                a[i] = b.Variable($"a{i}");
                bb[i] = b.Variable($"b{i}");
                SignalId half = b.Net($"h{i}"), sum = b.Net($"s{i}"), both = b.Net($"ab{i}"), through = b.Net($"hc{i}"), cout = b.Net($"c{i}");
                b.Gate(GateKind.Xor, $"x1_{i}", half, Delay.Of(2, 3), a[i], bb[i]);
                b.Gate(GateKind.Xor, $"x2_{i}", sum, Delay.Of(2, 3), half, carry);
                b.Gate(GateKind.And, $"a1_{i}", both, Delay.Of(1, 2), a[i], bb[i]);
                b.Gate(GateKind.And, $"a2_{i}", through, Delay.Of(1, 2), half, carry);
                b.Gate(GateKind.Or, $"o_{i}", cout, Delay.Of(2, 2), both, through);
                carry = cout;
            }

            var random = new Random(64);
            var s = new Stimulus().Set(0, cin, Logic4.Zero);
            for (int v = 0; v < 200; v++)
            {
                for (int i = 0; i < Bits; i++)
                {
                    s.Set(v * 400, a[i], LogicVec.FromUInt64((ulong)random.Next(2), 1));
                    s.Set(v * 400, bb[i], LogicVec.FromUInt64((ulong)random.Next(2), 1));
                }
            }

            var sim = new Simulator(b.Build(), s, new SimOptions { RecordTransitions = false });
            var clock = Stopwatch.StartNew();
            RunResult result = sim.RunTo(200 * 400);
            clock.Stop();

            double rate = result.Events / Math.Max(clock.Elapsed.TotalSeconds, 1e-6);
            TestContext.WriteLine($"{result.Events} events in {clock.Elapsed.TotalMilliseconds:F0} ms: {rate:F0} events a second");

            Assert.AreEqual(RunStatus.ReachedTime, result.Status);
            Assert.Greater(result.Events, 10000, "sanity: the additions really ran");
            Assert.Greater(rate, 100000, "the engine is far slower than it should be");
        }
    }
}
