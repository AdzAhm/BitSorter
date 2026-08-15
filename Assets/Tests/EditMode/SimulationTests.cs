using System;
using NUnit.Framework;
using BitSorter.LogicCore;

namespace BitSorter.LogicCore.Tests
{
    public class SimulationTests
    {
        private static readonly SinkNode.Reception[] Empty = new SinkNode.Reception[0];

        private static SinkNode.Reception R(Bit value, int tick) => new SinkNode.Reception(value, tick);

        // -----------------------------------------------------------------
        // Timing
        // -----------------------------------------------------------------

        [Test]
        public void BitOnDelayThreeEdge_ArrivesAtTickThree()
        {
            var sim = new Simulation();
            var source = sim.Add(new SourceNode(new[] { Bit.One }) { Name = "source" });
            var sink = sim.Add(new SinkNode() { Name = "sink" });
            sim.Connect(source.Out(0), sink.In(0), delay: 3);

            // Ticks 0, 1, 2: the bit is emitted at tick 0 and is still travelling.
            sim.Run(3);
            CollectionAssert.AreEqual(Empty, sink.Received, "bit arrived earlier than tick 3");

            // Tick 3: it lands.
            sim.Tick();

            CollectionAssert.AreEqual(new[] { R(Bit.One, 3) }, sink.Received);
            Assert.AreEqual(0, sim.CorruptedCount);
        }

        [Test]
        public void SourceEmitsOneBitPerTick_InSequenceOrder()
        {
            var sim = new Simulation();
            var source = sim.Add(new SourceNode(new[] { Bit.One, Bit.Zero }));
            var sink = sim.Add(new SinkNode());
            sim.Connect(source.Out(0), sink.In(0), delay: 1);

            sim.Run(4);

            CollectionAssert.AreEqual(new[] { R(Bit.One, 1), R(Bit.Zero, 2) }, sink.Received);
            Assert.IsTrue(source.IsExhausted);
            Assert.AreEqual(0, sim.CorruptedCount);
        }

        [Test]
        public void ChainedDelaysAccumulate()
        {
            // Emitted at tick 0, reaches the pass-through at tick 2, which re-emits the same tick,
            // reaching the sink at tick 5.
            var sim = new Simulation();
            var source = sim.Add(new SourceNode(new[] { Bit.One }));
            var pass = sim.Add(new PassThroughNode());
            var sink = sim.Add(new SinkNode());
            sim.Connect(source.Out(0), pass.In(0), delay: 2);
            sim.Connect(pass.Out(0), sink.In(0), delay: 3);

            sim.Run(8);

            CollectionAssert.AreEqual(new[] { R(Bit.One, 5) }, sink.Received);
            Assert.AreEqual(0, sim.CorruptedCount);
        }

        // -----------------------------------------------------------------
        // Collisions
        // -----------------------------------------------------------------

        [Test]
        public void TwoBitsIntoSamePortSameTick_ProduceExactlyOneCorruption()
        {
            var sim = new Simulation();
            var left = sim.Add(new SourceNode(new[] { Bit.One }) { Name = "left" });
            var right = sim.Add(new SourceNode(new[] { Bit.One }) { Name = "right" });
            var sink = sim.Add(new SinkNode() { Name = "sink" });

            // Equal delays into the same port: both land during tick 1.
            sim.Connect(left.Out(0), sink.In(0), delay: 1);
            sim.Connect(right.Out(0), sink.In(0), delay: 1);

            sim.Run(2);

            Assert.AreEqual(1, sim.CorruptedCount, "expected exactly one dropped bit");
            CollectionAssert.AreEqual(new[] { R(Bit.One, 1) }, sink.Received);
        }

        [Test]
        public void SinkWithTwoInputs_WaitsForBothPorts_AndLatchedPortCorrupts()
        {
            var sim = new Simulation();
            var fast = sim.Add(new SourceNode(new[] { Bit.One, Bit.One }) { Name = "fast" });
            var slow = sim.Add(new SourceNode(new[] { Bit.Zero }) { Name = "slow" });
            var sink = sim.Add(new SinkNode(inputCount: 2) { Name = "sink" });

            sim.Connect(fast.Out(0), sink.In(0), delay: 1);  // arrives at ticks 1 and 2
            sim.Connect(slow.Out(0), sink.In(1), delay: 3);  // arrives at tick 3

            // Tick 1 fills port 0; tick 2's bit finds it still occupied and is dropped.
            sim.Run(3);
            CollectionAssert.AreEqual(Empty, sink.Received, "fired before both ports were filled");
            Assert.AreEqual(1, sim.CorruptedCount);

            // Tick 3 fills port 1, so the node finally fires with the bit latched since tick 1.
            sim.Tick();

            CollectionAssert.AreEqual(new[] { R(Bit.One, 3), R(Bit.Zero, 3) }, sink.Received);
            Assert.AreEqual(1, sim.CorruptedCount);
        }

        // -----------------------------------------------------------------
        // Construction order independence
        // -----------------------------------------------------------------

        [Test]
        public void ReversedBuildOrder_ProducesIdenticalResults()
        {
            Harness forward = BuildOrderGraph(reversed: false);
            Harness reverse = BuildOrderGraph(reversed: true);

            forward.Sim.Run(8);
            reverse.Sim.Run(8);

            CollectionAssert.AreEqual(forward.Sink1.Received, reverse.Sink1.Received, "sink1 differs");
            CollectionAssert.AreEqual(forward.Sink2.Received, reverse.Sink2.Received, "sink2 differs");
            CollectionAssert.AreEqual(forward.Sink3.Received, reverse.Sink3.Received, "sink3 differs");
            Assert.AreEqual(forward.Sim.CorruptedCount, reverse.Sim.CorruptedCount, "corruption differs");

            // Guard against the graph silently degenerating into something that proves nothing.
            Assert.AreEqual(1, forward.Sim.CorruptedCount, "graph should exercise one collision");
            CollectionAssert.IsNotEmpty(forward.Sink1.Received);
            CollectionAssert.IsNotEmpty(forward.Sink2.Received);
            CollectionAssert.IsNotEmpty(forward.Sink3.Received);
        }

        [Test]
        public void CollidingBitsWithDifferentValues_LeaveNoSurvivor_WhicheverEdgeCameFirst()
        {
            // A mixed collision is ambiguous, so neither bit survives and both are counted. The
            // equal-value counterpart -- where the port does keep its value, for a tally of one --
            // is TwoBitsIntoSamePortSameTick_ProduceExactlyOneCorruption above.
            SinkNode oneFirst = BuildCollision(Bit.One, Bit.Zero, out int oneFirstCorrupted);
            SinkNode zeroFirst = BuildCollision(Bit.Zero, Bit.One, out int zeroFirstCorrupted);

            CollectionAssert.IsEmpty(oneFirst.Received, "a bit survived a mixed collision");
            CollectionAssert.IsEmpty(zeroFirst.Received, "a bit survived a mixed collision");

            Assert.AreEqual(2, oneFirstCorrupted, "both bits should be counted as destroyed");
            Assert.AreEqual(2, zeroFirstCorrupted, "both bits should be counted as destroyed");
        }

        [Test]
        public void ThreeArrivalsWithMixedValues_LeavePortEmpty_InEveryEdgeOrder()
        {
            // Clearing alone is not enough: without poisoning the port for the rest of the
            // delivery phase, the third bit in {One, Zero, One} would refill the port the mixed
            // collision had just emptied, while {One, One, Zero} would end empty -- reintroducing
            // the edge-order dependence that clearing exists to remove.
            var orders = new[]
            {
                new[] { Bit.One, Bit.One, Bit.Zero },
                new[] { Bit.One, Bit.Zero, Bit.One },
                new[] { Bit.Zero, Bit.One, Bit.One },
            };

            foreach (Bit[] order in orders)
            {
                string label = string.Join(",", order);
                var sim = new Simulation();
                var sink = sim.Add(new SinkNode() { Name = "sink" });

                foreach (Bit value in order)
                {
                    var source = sim.Add(new SourceNode(new[] { value }));
                    sim.Connect(source.Out(0), sink.In(0), delay: 1);
                }

                sim.Run(2);

                CollectionAssert.IsEmpty(sink.Received, $"order [{label}] left a survivor");
                Assert.AreEqual(3, sim.CorruptedCount, $"order [{label}] lost the wrong bit count");
            }
        }

        // -----------------------------------------------------------------
        // Validation
        // -----------------------------------------------------------------

        [Test]
        public void EdgeWithDelayBelowOne_Throws([Values(0, -1)] int delay)
        {
            var sim = new Simulation();
            var source = sim.Add(new SourceNode(new[] { Bit.One }));
            var sink = sim.Add(new SinkNode());

            Assert.Throws<ArgumentOutOfRangeException>(
                () => sim.Connect(source.Out(0), sink.In(0), delay));
        }

        // -----------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------

        private sealed class Harness
        {
            public Simulation Sim;
            public SinkNode Sink1;
            public SinkNode Sink2;
            public SinkNode Sink3;
        }

        /// <summary>
        /// Builds one graph exercising chaining, fan-out, fan-in, a multi-input sink and a
        /// collision. The colliding pair carries the same value so that the collision leaves a
        /// survivor to compare -- a mixed collision would correctly empty the port under both
        /// orderings, which is covered by
        /// <see cref="CollidingBitsWithDifferentValues_LeaveNoSurvivor_WhicheverEdgeCameFirst"/>.
        /// </summary>
        private static Harness BuildOrderGraph(bool reversed)
        {
            var sim = new Simulation();

            var srcA = new SourceNode(new[] { Bit.One, Bit.One }) { Name = "srcA" };
            var srcB = new SourceNode(new[] { Bit.One, Bit.One }) { Name = "srcB" };
            var srcC = new SourceNode(new[] { Bit.Zero, Bit.One }) { Name = "srcC" };
            var srcD = new SourceNode(new[] { Bit.One, Bit.Zero }) { Name = "srcD" };
            var pass1 = new PassThroughNode { Name = "pass1" };
            var pass2 = new PassThroughNode { Name = "pass2" };
            var sink1 = new SinkNode() { Name = "sink1" };
            var sink2 = new SinkNode(inputCount: 2) { Name = "sink2" };
            var sink3 = new SinkNode() { Name = "sink3" };

            var nodes = new Node[] { srcA, srcB, srcC, srcD, pass1, pass2, sink1, sink2, sink3 };

            var links = new[]
            {
                new Link(srcA.Out(0), pass1.In(0), 1),   // chain into sink1, arriving ticks 3 and 4
                new Link(pass1.Out(0), sink1.In(0), 2),
                new Link(srcB.Out(0), sink1.In(0), 4),   // fan-in: collides at tick 4, both One
                new Link(srcC.Out(0), sink2.In(0), 1),   // multi-input sink, mixed values
                new Link(srcD.Out(0), sink2.In(1), 1),
                new Link(srcD.Out(0), pass2.In(0), 2),   // fan-out from srcD's single output port
                new Link(pass2.Out(0), sink3.In(0), 1),
            };

            if (reversed)
            {
                Array.Reverse(nodes);
                Array.Reverse(links);
            }

            foreach (Node node in nodes)
                sim.Add(node);

            foreach (Link link in links)
                sim.Connect(link.From, link.To, link.Delay);

            return new Harness { Sim = sim, Sink1 = sink1, Sink2 = sink2, Sink3 = sink3 };
        }

        private readonly struct Link
        {
            public readonly OutputPort From;
            public readonly InputPort To;
            public readonly int Delay;

            public Link(OutputPort from, InputPort to, int delay)
            {
                From = from;
                To = to;
                Delay = delay;
            }
        }

        /// <summary>Two sources colliding on one port, wired in the given order.</summary>
        private static SinkNode BuildCollision(Bit firstEdgeValue, Bit secondEdgeValue, out int corrupted)
        {
            var sim = new Simulation();
            var first = sim.Add(new SourceNode(new[] { firstEdgeValue }));
            var second = sim.Add(new SourceNode(new[] { secondEdgeValue }));
            var sink = sim.Add(new SinkNode());

            sim.Connect(first.Out(0), sink.In(0), delay: 1);
            sim.Connect(second.Out(0), sink.In(0), delay: 1);

            sim.Run(2);

            corrupted = sim.CorruptedCount;
            return sink;
        }
    }
}
