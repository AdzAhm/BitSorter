using NUnit.Framework;
using BitSorter.LogicCore;

namespace BitSorter.LogicCore.Tests
{
    /// <summary>
    /// A half adder assembled purely by wiring existing components -- sum is A XOR B, carry is
    /// A AND B. There is no HalfAdder class and there should not be one: the circuit is the
    /// composition, which is the thing worth testing.
    /// </summary>
    public class HalfAdderTests
    {
        [TestCase(Bit.Zero, Bit.Zero, Bit.Zero, Bit.Zero)]
        [TestCase(Bit.Zero, Bit.One, Bit.One, Bit.Zero)]
        [TestCase(Bit.One, Bit.Zero, Bit.One, Bit.Zero)]
        [TestCase(Bit.One, Bit.One, Bit.Zero, Bit.One)]
        public void HalfAdder_TruthTable(Bit a, Bit b, Bit expectedSum, Bit expectedCarry)
        {
            var sim = new Simulation();

            var sourceA = sim.Add(new SourceNode(new[] { a }) { Name = "a" });
            var sourceB = sim.Add(new SourceNode(new[] { b }) { Name = "b" });
            var sum = sim.Add(new XorGate { Name = "sum" });
            var carry = sim.Add(new AndGate { Name = "carry" });
            var sumSink = sim.Add(new SinkNode() { Name = "sumSink" });
            var carrySink = sim.Add(new SinkNode() { Name = "carrySink" });

            // Each source fans out from its single output port to both gates, so the two gates
            // see the same pair of bits rather than two independently scripted copies.
            sim.Connect(sourceA.Out(0), sum.In(0), delay: 1);
            sim.Connect(sourceA.Out(0), carry.In(0), delay: 1);
            sim.Connect(sourceB.Out(0), sum.In(1), delay: 1);
            sim.Connect(sourceB.Out(0), carry.In(1), delay: 1);

            sim.Connect(sum.Out(0), sumSink.In(0), delay: 1);
            sim.Connect(carry.Out(0), carrySink.In(0), delay: 1);

            sim.Run(4);

            string row = $"{a} + {b}";
            Assert.AreEqual(1, sumSink.Received.Count, $"{row}: sum output count");
            Assert.AreEqual(1, carrySink.Received.Count, $"{row}: carry output count");

            Assert.AreEqual(expectedSum, sumSink.Received[0].Value, $"{row}: sum");
            Assert.AreEqual(expectedCarry, carrySink.Received[0].Value, $"{row}: carry");

            // Both halves cross the same number of edges with the same delays, so the two results
            // must surface together. A half adder whose carry lagged its sum would silently
            // corrupt any full adder stacked on top of it.
            Assert.AreEqual(sumSink.Received[0].Tick, carrySink.Received[0].Tick,
                $"{row}: sum and carry landed on different ticks");
            Assert.AreEqual(2, sumSink.Received[0].Tick, $"{row}: expected both outputs on tick 2");

            Assert.AreEqual(0, sim.CorruptedCount, row);
        }
    }
}
