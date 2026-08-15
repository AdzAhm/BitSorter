using NUnit.Framework;
using BitSorter.LogicCore;

namespace BitSorter.LogicCore.Tests
{
    /// <summary>
    /// A full adder assembled by wiring existing components -- two half adders plus an OR for
    /// carry-out. There is no FullAdder class and there should not be one.
    /// </summary>
    public class FullAdderTests
    {
        /// <summary>The second-stage Cin delay that keeps every path in step.</summary>
        private const int BalancedCinDelay = 2;

        // -----------------------------------------------------------------
        // Correctness
        // -----------------------------------------------------------------

        [TestCase(Bit.Zero, Bit.Zero, Bit.Zero, Bit.Zero, Bit.Zero)]
        [TestCase(Bit.Zero, Bit.Zero, Bit.One, Bit.One, Bit.Zero)]
        [TestCase(Bit.Zero, Bit.One, Bit.Zero, Bit.One, Bit.Zero)]
        [TestCase(Bit.Zero, Bit.One, Bit.One, Bit.Zero, Bit.One)]
        [TestCase(Bit.One, Bit.Zero, Bit.Zero, Bit.One, Bit.Zero)]
        [TestCase(Bit.One, Bit.Zero, Bit.One, Bit.Zero, Bit.One)]
        [TestCase(Bit.One, Bit.One, Bit.Zero, Bit.Zero, Bit.One)]
        [TestCase(Bit.One, Bit.One, Bit.One, Bit.One, Bit.One)]
        public void FullAdder_TruthTable(Bit a, Bit b, Bit cin, Bit expectedSum, Bit expectedCout)
        {
            Circuit circuit = Build(new[] { a }, new[] { b }, new[] { cin }, BalancedCinDelay);

            circuit.Sim.Run(10);

            string row = $"{a} + {b} + {cin}";
            Assert.AreEqual(1, circuit.SumSink.Received.Count, $"{row}: sum output count");
            Assert.AreEqual(1, circuit.CoutSink.Received.Count, $"{row}: cout output count");

            Assert.AreEqual(expectedSum, circuit.SumSink.Received[0].Value, $"{row}: sum");
            Assert.AreEqual(expectedCout, circuit.CoutSink.Received[0].Value, $"{row}: cout");

            // Sum crosses two gates and Cout crosses three, so the wiring stretches the short
            // path to compensate. If that compensation were wrong the two would separate here.
            Assert.AreEqual(circuit.CoutSink.Received[0].Tick, circuit.SumSink.Received[0].Tick,
                $"{row}: sum and cout landed on different ticks");
            Assert.AreEqual(4, circuit.SumSink.Received[0].Tick, $"{row}: expected both on tick 4");

            Assert.AreEqual(0, circuit.Sim.CorruptedCount, row);
        }

        [Test]
        public void BalancedFullAdder_HandlesAStream_WithoutCorruption()
        {
            Circuit circuit = Build(StreamA, StreamB, StreamCin, BalancedCinDelay);

            circuit.Sim.Run(16);

            Assert.AreEqual(0, circuit.Sim.CorruptedCount);
            Assert.AreEqual(StreamA.Length, circuit.SumSink.Received.Count, "sum outputs");
            Assert.AreEqual(StreamA.Length, circuit.CoutSink.Received.Count, "cout outputs");

            // 1+0+0 = 01, 0+1+1 = 10, 1+1+1 = 11
            CollectionAssert.AreEqual(new[] { Bit.One, Bit.Zero, Bit.One }, Values(circuit.SumSink));
            CollectionAssert.AreEqual(new[] { Bit.Zero, Bit.One, Bit.One }, Values(circuit.CoutSink));

            for (int i = 0; i < StreamA.Length; i++)
            {
                Assert.AreEqual(circuit.CoutSink.Received[i].Tick, circuit.SumSink.Received[i].Tick,
                    $"result {i}: sum and cout separated");
            }
        }

        // -----------------------------------------------------------------
        // Unbalanced path delays
        // -----------------------------------------------------------------

        [Test]
        public void UnbalancedSecondStageDelays_DestroyBits_RatherThanProduceWrongAnswers()
        {
            // The natural wiring mistake: feeding Cin straight into the second stage on a delay
            // of 1, forgetting it has to wait a tick for the first half adder to produce its sum.
            // Cin then reaches the second-stage ports before the first stage's output does.
            //
            // With a single bit in flight that would merely fire late. With a stream it corrupts:
            // the early bit sits in its port, and the following bit collides with the one still
            // waiting there. This is the property the whole model is built around -- a mis-timed
            // circuit loses bits and says so, instead of quietly emitting a full set of wrong
            // answers that looks like a logic error.
            Circuit balanced = Build(StreamA, StreamB, StreamCin, BalancedCinDelay);
            Circuit unbalanced = Build(StreamA, StreamB, StreamCin, cinDelay: 1);

            balanced.Sim.Run(16);
            unbalanced.Sim.Run(16);

            Assert.AreEqual(0, balanced.Sim.CorruptedCount, "the balanced control should be clean");
            Assert.Greater(unbalanced.Sim.CorruptedCount, 0, "unbalanced paths should corrupt bits");

            // Bits are destroyed, so results go missing rather than coming out wrong. This is the
            // assertion that distinguishes corruption from a wrong answer: strictly fewer outputs
            // than the same input stream produces when the paths line up.
            Assert.Less(unbalanced.SumSink.Received.Count, balanced.SumSink.Received.Count,
                "unbalanced sum outputs should be lost, not merely incorrect");
            Assert.Less(unbalanced.CoutSink.Received.Count, balanced.CoutSink.Received.Count,
                "unbalanced cout outputs should be lost, not merely incorrect");
        }

        // -----------------------------------------------------------------
        // Wiring
        // -----------------------------------------------------------------

        private static readonly Bit[] StreamA = { Bit.One, Bit.Zero, Bit.One };
        private static readonly Bit[] StreamB = { Bit.Zero, Bit.One, Bit.One };
        private static readonly Bit[] StreamCin = { Bit.Zero, Bit.One, Bit.One };

        private sealed class Circuit
        {
            public Simulation Sim;
            public SinkNode SumSink;
            public SinkNode CoutSink;
        }

        /// <summary>
        /// Two half adders plus an OR. Sum is A xor B xor Cin; Cout is (A and B) or ((A xor B) and Cin).
        /// </summary>
        /// <param name="cinDelay">
        /// Delay on both edges carrying Cin into the second stage. <see cref="BalancedCinDelay"/>
        /// matches the first stage's latency; anything else deliberately unbalances the circuit.
        /// </param>
        private static Circuit Build(Bit[] a, Bit[] b, Bit[] cin, int cinDelay)
        {
            var sim = new Simulation();

            var sourceA = sim.Add(new SourceNode(a) { Name = "A" });
            var sourceB = sim.Add(new SourceNode(b) { Name = "B" });
            var sourceCin = sim.Add(new SourceNode(cin) { Name = "Cin" });

            var sum1 = sim.Add(new XorGate { Name = "sum1" });     // first half adder
            var carry1 = sim.Add(new AndGate { Name = "carry1" });
            var sum2 = sim.Add(new XorGate { Name = "sum2" });     // second half adder
            var carry2 = sim.Add(new AndGate { Name = "carry2" });
            var coutOr = sim.Add(new OrGate { Name = "cout" });

            var sumSink = sim.Add(new SinkNode() { Name = "sumSink" });
            var coutSink = sim.Add(new SinkNode() { Name = "coutSink" });

            // First half adder: A and B fan out to both of its gates, which fire on tick 1.
            sim.Connect(sourceA.Out(0), sum1.In(0), 1);
            sim.Connect(sourceB.Out(0), sum1.In(1), 1);
            sim.Connect(sourceA.Out(0), carry1.In(0), 1);
            sim.Connect(sourceB.Out(0), carry1.In(1), 1);

            // Second half adder: its gates fire on tick 2, so Cin is held back to arrive then too.
            sim.Connect(sum1.Out(0), sum2.In(0), 1);
            sim.Connect(sourceCin.Out(0), sum2.In(1), cinDelay);
            sim.Connect(sum1.Out(0), carry2.In(0), 1);
            sim.Connect(sourceCin.Out(0), carry2.In(1), cinDelay);

            // carry1 fires on tick 1 and carry2 on tick 2, so carry1's edge is a tick longer.
            sim.Connect(carry1.Out(0), coutOr.In(0), 2);
            sim.Connect(carry2.Out(0), coutOr.In(1), 1);

            // Sum crosses two gates, Cout three, so the sum edge is a tick longer.
            sim.Connect(sum2.Out(0), sumSink.In(0), 2);
            sim.Connect(coutOr.Out(0), coutSink.In(0), 1);

            return new Circuit { Sim = sim, SumSink = sumSink, CoutSink = coutSink };
        }

        private static Bit[] Values(SinkNode sink)
        {
            var values = new Bit[sink.Received.Count];
            for (int i = 0; i < values.Length; i++)
                values[i] = sink.Received[i].Value;

            return values;
        }
    }
}
