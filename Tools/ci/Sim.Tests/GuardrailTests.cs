using System;
using System.Diagnostics;
using BitSorter.SystemVerilog;
using NUnit.Framework;

namespace BitSorter.Sim.Tests
{
    /// <summary>
    /// What keeps the engine safe to run in a browser tab: a loop that never settles stops with a
    /// message rather than hanging, a run allocates nothing once warm, and it is fast enough.
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
