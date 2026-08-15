using NUnit.Framework;
using BitSorter.LogicCore;

namespace BitSorter.LogicCore.Tests
{
    public class GateTests
    {
        private static SinkNode.Reception R(Bit value, int tick) => new SinkNode.Reception(value, tick);

        // -----------------------------------------------------------------
        // Truth tables
        // -----------------------------------------------------------------

        [TestCase(Bit.Zero, Bit.One)]
        [TestCase(Bit.One, Bit.Zero)]
        public void NotGate_TruthTable(Bit input, Bit expected)
        {
            var sim = new Simulation();
            var source = sim.Add(new SourceNode(new[] { input }) { Name = "source" });
            var gate = sim.Add(new NotGate { Name = "not" });
            var sink = sim.Add(new SinkNode() { Name = "sink" });

            sim.Connect(source.Out(0), gate.In(0), delay: 1);  // arrives tick 1, gate fires tick 1
            sim.Connect(gate.Out(0), sink.In(0), delay: 1);    // result reaches the sink at tick 2

            sim.Run(4);

            CollectionAssert.AreEqual(new[] { R(expected, 2) }, sink.Received);
            Assert.AreEqual(0, sim.CorruptedCount);
        }

        [TestCase(Bit.Zero, Bit.Zero, Bit.Zero)]
        [TestCase(Bit.Zero, Bit.One, Bit.Zero)]
        [TestCase(Bit.One, Bit.Zero, Bit.Zero)]
        [TestCase(Bit.One, Bit.One, Bit.One)]
        public void AndGate_TruthTable(Bit a, Bit b, Bit expected)
        {
            var sim = new Simulation();
            var sourceA = sim.Add(new SourceNode(new[] { a }) { Name = "a" });
            var sourceB = sim.Add(new SourceNode(new[] { b }) { Name = "b" });
            var gate = sim.Add(new AndGate { Name = "and" });
            var sink = sim.Add(new SinkNode() { Name = "sink" });

            // Equal delays, so both inputs land on tick 1 and the gate fires that same tick.
            sim.Connect(sourceA.Out(0), gate.In(0), delay: 1);
            sim.Connect(sourceB.Out(0), gate.In(1), delay: 1);
            sim.Connect(gate.Out(0), sink.In(0), delay: 1);

            sim.Run(4);

            CollectionAssert.AreEqual(new[] { R(expected, 2) }, sink.Received);
            Assert.AreEqual(0, sim.CorruptedCount);
        }

        // -----------------------------------------------------------------
        // Gating on input readiness
        // -----------------------------------------------------------------

        [Test]
        public void AndGate_DoesNotFire_WhileOnlyOneInputHasArrived()
        {
            var sim = new Simulation();
            var source = sim.Add(new SourceNode(new[] { Bit.One }) { Name = "a" });
            var gate = sim.Add(new AndGate { Name = "and" });
            var sink = sim.Add(new SinkNode() { Name = "sink" });

            sim.Connect(source.Out(0), gate.In(0), delay: 1);
            sim.Connect(gate.Out(0), sink.In(0), delay: 1);
            // In(1) is deliberately left unwired, so it can never be filled.

            sim.Run(10);

            CollectionAssert.IsEmpty(sink.Received, "gate fired with only one input filled");
            Assert.IsFalse(gate.IsReadyToEvaluate);
            Assert.IsTrue(gate.In(0).IsOccupied, "the arrived bit should still be waiting in port 0");
            Assert.IsFalse(gate.In(1).IsOccupied);
            Assert.AreEqual(0, sim.CorruptedCount);
        }

        [Test]
        public void AndGate_FiresOnTheExactTickTheSecondInputArrives()
        {
            var sim = new Simulation();
            var early = sim.Add(new SourceNode(new[] { Bit.One }) { Name = "early" });
            var late = sim.Add(new SourceNode(new[] { Bit.One }) { Name = "late" });
            var gate = sim.Add(new AndGate { Name = "and" });
            var sink = sim.Add(new SinkNode() { Name = "sink" });

            sim.Connect(early.Out(0), gate.In(0), delay: 1);  // arrives tick 1
            sim.Connect(late.Out(0), gate.In(1), delay: 5);   // arrives tick 5
            sim.Connect(gate.Out(0), sink.In(0), delay: 1);

            // Ticks 0-4: the early bit sits in port 0 and the gate stays idle.
            sim.Run(5);
            Assert.IsTrue(gate.In(0).IsOccupied, "early bit should still be latched through tick 4");
            Assert.IsFalse(gate.IsReadyToEvaluate);
            CollectionAssert.IsEmpty(sink.Received);

            // Tick 5: the second bit lands and the gate consumes both in that same tick.
            sim.Tick();
            Assert.IsFalse(gate.In(0).IsOccupied, "port 0 should have been consumed on tick 5");
            Assert.IsFalse(gate.In(1).IsOccupied, "port 1 should have been consumed on tick 5");

            // Tick 6: the result lands, one edge-delay after the tick the gate fired on.
            sim.Tick();
            CollectionAssert.AreEqual(new[] { R(Bit.One, 6) }, sink.Received);
            Assert.AreEqual(0, sim.CorruptedCount);
        }
    }
}
