using NUnit.Framework;
using BitSorter.LogicCore;

namespace BitSorter.LogicCore.Tests
{
    /// <summary>
    /// The read-only observation surface a renderer polls. Each test cross-checks it against a
    /// timing the simulator tests already establish independently, so the observation API is
    /// verified against known-true behaviour rather than against itself.
    /// </summary>
    public class ObservationTests
    {
        // -----------------------------------------------------------------
        // Stable ids
        // -----------------------------------------------------------------

        [Test]
        public void NodesAndEdges_GetSequentialIds_ThatSurviveTicking()
        {
            var sim = new Simulation();
            var source = sim.Add(new SourceNode(new[] { Bit.One }) { Name = "source" });
            var pass = sim.Add(new PassThroughNode { Name = "pass" });
            var sink = sim.Add(new SinkNode() { Name = "sink" });

            Edge first = sim.Connect(source.Out(0), pass.In(0), delay: 1);
            Edge second = sim.Connect(pass.Out(0), sink.In(0), delay: 1);

            Assert.AreEqual(0, source.Id);
            Assert.AreEqual(1, pass.Id);
            Assert.AreEqual(2, sink.Id);
            Assert.AreEqual(0, first.Id);
            Assert.AreEqual(1, second.Id);

            SimulationView view = sim.View;
            Assert.AreEqual(3, view.NodeCount);
            Assert.AreEqual(2, view.EdgeCount);

            sim.Run(6);

            // Ids are what a renderer keys its visuals to, so they must not drift as bits move.
            for (int id = 0; id < view.NodeCount; id++)
                Assert.AreEqual(id, view.GetNode(id).Id, $"node {id} id drifted");

            for (int id = 0; id < view.EdgeCount; id++)
                Assert.AreEqual(id, view.GetEdge(id).Id, $"edge {id} id drifted");

            Assert.AreSame(source, view.GetNode(0));
            Assert.AreSame(second, view.GetEdge(1));
        }

        [Test]
        public void UnregisteredNodeAndEdge_ReportIdMinusOne()
        {
            var orphan = new PassThroughNode();
            Assert.AreEqual(-1, orphan.Id);
        }

        // -----------------------------------------------------------------
        // Bits in transit
        // -----------------------------------------------------------------

        [Test]
        public void BitOnDelayThreeEdge_IsObservableAtEachStepOfItsJourney()
        {
            // SimulationTests.BitOnDelayThreeEdge_ArrivesAtTickThree proves this bit is emitted on
            // tick 0 and delivered on tick 3. Here the observation API must agree, step by step.
            var sim = new Simulation();
            var source = sim.Add(new SourceNode(new[] { Bit.One }));
            var sink = sim.Add(new SinkNode());
            Edge edge = sim.Connect(source.Out(0), sink.In(0), delay: 3);

            SimulationView view = sim.View;
            Assert.AreEqual(0, edge.InTransitCount, "nothing should be moving before tick 0");

            // Tick 0: emitted, sitting at the far end with its full delay ahead of it.
            sim.Tick();
            AssertSingleBitInTransit(view.GetEdge(edge.Id), Bit.One, ticksRemaining: 3, progress: 0f);

            // Ticks 1 and 2: one third and two thirds of the way along.
            sim.Tick();
            AssertSingleBitInTransit(view.GetEdge(edge.Id), Bit.One, ticksRemaining: 2, progress: 1f / 3f);

            sim.Tick();
            AssertSingleBitInTransit(view.GetEdge(edge.Id), Bit.One, ticksRemaining: 1, progress: 2f / 3f);

            // Tick 3: delivered, so it is no longer in transit anywhere.
            sim.Tick();
            Assert.AreEqual(0, edge.InTransitCount, "delivered bit should have left the edge");
            Assert.AreEqual(1, sink.Received.Count);
            Assert.AreEqual(3, sink.Received[0].Tick);
        }

        [Test]
        public void SeveralBitsOnOneEdge_AreReportedNearestTargetFirst()
        {
            // A source emits one bit per tick, so a delay-3 edge holds three at once.
            var sim = new Simulation();
            var source = sim.Add(new SourceNode(new[] { Bit.One, Bit.Zero, Bit.One }));
            var sink = sim.Add(new SinkNode());
            Edge edge = sim.Connect(source.Out(0), sink.In(0), delay: 3);

            sim.Run(3); // ticks 0, 1, 2 -- three emissions, none delivered yet

            Assert.AreEqual(3, edge.InTransitCount);

            // Emission order is preserved, so index 0 is the oldest bit and the closest to
            // arriving. Remaining counts are strictly increasing along the edge, which is what
            // makes TicksRemaining a unique handle on a bit within one edge.
            BitInTransit nearest = edge.GetBitInTransit(0);
            BitInTransit middle = edge.GetBitInTransit(1);
            BitInTransit furthest = edge.GetBitInTransit(2);

            Assert.AreEqual(1, nearest.TicksRemaining);
            Assert.AreEqual(2, middle.TicksRemaining);
            Assert.AreEqual(3, furthest.TicksRemaining);

            Assert.AreEqual(Bit.One, nearest.Value);
            Assert.AreEqual(Bit.Zero, middle.Value);
            Assert.AreEqual(Bit.One, furthest.Value);

            for (int i = 0; i < edge.InTransitCount; i++)
                Assert.AreEqual(3, edge.GetBitInTransit(i).TotalDelay, $"bit {i} total delay");
        }

        // -----------------------------------------------------------------
        // Port occupancy
        // -----------------------------------------------------------------

        [Test]
        public void InputPortOccupancy_TracksTheWaitingBit_UntilItIsConsumed()
        {
            // SimulationTests.SinkWithTwoInputs_WaitsForBothPorts establishes that a two-input
            // node holds an early bit until its partner lands. That waiting bit is exactly what a
            // renderer needs to draw, so the observation API has to report it.
            var sim = new Simulation();
            var fast = sim.Add(new SourceNode(new[] { Bit.One }) { Name = "fast" });
            var slow = sim.Add(new SourceNode(new[] { Bit.Zero }) { Name = "slow" });
            var sink = sim.Add(new SinkNode(inputCount: 2) { Name = "sink" });

            sim.Connect(fast.Out(0), sink.In(0), delay: 1);
            sim.Connect(slow.Out(0), sink.In(1), delay: 3);

            SimulationView view = sim.View;
            Node observed = view.GetNode(sink.Id);
            Assert.AreEqual(2, observed.InputCount);

            // Before anything lands both ports read empty.
            AssertPortEmpty(observed.In(0));
            AssertPortEmpty(observed.In(1));

            // Tick 1: the fast bit lands and waits. Ticks 2 onwards it is still sitting there.
            sim.Run(2);
            AssertPortHolds(observed.In(0), Bit.One);
            AssertPortEmpty(observed.In(1));

            sim.Tick(); // tick 2, still waiting
            AssertPortHolds(observed.In(0), Bit.One);
            AssertPortEmpty(observed.In(1));

            // Tick 3: the slow bit lands, the node fires, and both ports are consumed.
            sim.Tick();
            AssertPortEmpty(observed.In(0));
            AssertPortEmpty(observed.In(1));
            Assert.AreEqual(2, sink.Received.Count);
        }

        [Test]
        public void MixedCollision_IsVisibleThroughLastCorruptedTick()
        {
            // Two differing values land on one port during tick 1, destroying both.
            var sim = new Simulation();
            var one = sim.Add(new SourceNode(new[] { Bit.One }));
            var zero = sim.Add(new SourceNode(new[] { Bit.Zero }));
            var sink = sim.Add(new SinkNode());

            sim.Connect(one.Out(0), sink.In(0), delay: 1);
            sim.Connect(zero.Out(0), sink.In(0), delay: 1);

            SimulationView view = sim.View;
            Assert.AreEqual(-1, sink.In(0).LastCorruptedTick, "nothing corrupted yet");

            sim.Run(2);

            Assert.AreEqual(1, sink.In(0).LastCorruptedTick, "the collision happened on tick 1");
            AssertPortEmpty(sink.In(0));
            Assert.AreEqual(2, view.CorruptedCount, "both bits destroyed");
            CollectionAssert.IsEmpty(sink.Received);
        }

        // -----------------------------------------------------------------
        // Read-only and allocation-free shape
        // -----------------------------------------------------------------

        [Test]
        public void ObservationTypes_AreValueTypes_SoPollingNeedNotAllocate()
        {
            // The zero-allocation guarantee rests on these being structs. If either is ever
            // changed to a class, every frame of polling starts allocating.
            Assert.IsTrue(typeof(BitInTransit).IsValueType, "BitInTransit must stay a struct");
            Assert.IsTrue(typeof(SimulationView).IsValueType, "SimulationView must stay a struct");
        }

        [Test]
        public void View_ReportsTheSameLiveStateAsTheSimulation()
        {
            var sim = new Simulation();
            var source = sim.Add(new SourceNode(new[] { Bit.One }));
            var sink = sim.Add(new SinkNode());
            sim.Connect(source.Out(0), sink.In(0), delay: 2);

            SimulationView view = sim.View;

            // The view is a handle, not a snapshot: one fetched before ticking still reads
            // current values afterwards.
            Assert.AreEqual(0, view.CurrentTick);

            sim.Run(3);

            Assert.AreEqual(sim.CurrentTick, view.CurrentTick);
            Assert.AreEqual(3, view.CurrentTick);
            Assert.AreEqual(sim.CorruptedCount, view.CorruptedCount);
            Assert.AreEqual(sim.NodeCount, view.NodeCount);
            Assert.AreEqual(sim.EdgeCount, view.EdgeCount);
        }

        // -----------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------

        private static void AssertSingleBitInTransit(Edge edge, Bit value, int ticksRemaining, float progress)
        {
            Assert.AreEqual(1, edge.InTransitCount, "expected exactly one bit on the edge");

            BitInTransit bit = edge.GetBitInTransit(0);
            Assert.AreEqual(value, bit.Value);
            Assert.AreEqual(ticksRemaining, bit.TicksRemaining);
            Assert.AreEqual(edge.Delay, bit.TotalDelay);
            Assert.AreEqual(progress, bit.Progress, 0.0001f, "progress along the edge");
        }

        private static void AssertPortHolds(InputPort port, Bit value)
        {
            Assert.IsTrue(port.IsOccupied, $"{port} should be occupied");
            Assert.IsTrue(port.Pending.HasValue);
            Assert.AreEqual(value, port.Pending.Value);
        }

        private static void AssertPortEmpty(InputPort port)
        {
            Assert.IsFalse(port.IsOccupied, $"{port} should be empty");
            Assert.IsFalse(port.Pending.HasValue);
        }
    }
}
