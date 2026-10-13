using System;
using System.Diagnostics;
using System.Linq;
using BitSorter.SystemVerilog;
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
            (Design design, Stimulus stimulus, _) = Ring(Delay.Zero);
            var sim = new Simulator(design, stimulus, new SimOptions { MaxDeltas = 1000 });

            var clock = Stopwatch.StartNew();
            RunResult result = sim.RunTo(100);

            Assert.Less(clock.Elapsed.TotalSeconds, 5, "the loop took too long to be caught");
            Assert.AreEqual(RunStatus.Stopped, result.Status);
            Assert.AreEqual(5, result.Time, "it stops in the time step the loop started in");
            Assert.AreEqual(1, sim.History.Messages.Count);
            StringAssert.Contains("y", sim.History.Messages[0].Text);
            Assert.AreEqual(RunStatus.Stopped, sim.RunTo(200).Status, "a stopped run stays stopped");
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
                Design design = Elaborator.Elaborate(SystemVerilog.Syntax.SyntaxReader.Read("processor/mcpu.sv", sources)).Design;
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
