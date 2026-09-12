using System.Collections.Generic;
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

            // This graph has no removals, so the id range is dense and the loops below can
            // dereference every slot. Stated rather than assumed: if a removal is ever added to
            // this test it should fail here, not with a null dereference two lines down.
            // RemovalTests covers the sparse case.
            Assert.AreEqual(view.NodeCount, view.LiveNodeCount, "unexpected node tombstone");
            Assert.AreEqual(view.EdgeCount, view.LiveEdgeCount, "unexpected edge tombstone");

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
            // arriving, and remaining counts are strictly increasing along the edge.
            //
            // Note what that does and does not establish. It separates the bits on this edge *at
            // this instant*, which is all a single frame's drawing needs. It says nothing about
            // whether the count still names the same bit next tick -- it does not, and
            // ABitsIdentity_IsNeverReusedByAnotherBit is the test for that. This comment used to
            // claim TicksRemaining was "a unique handle on a bit", which is how a renderer came to
            // follow bits by it.
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
        // Following one bit between frames
        // -----------------------------------------------------------------

        /// <summary>
        /// The handle <see cref="BitInTransit"/> tells a renderer to follow a bit by.
        /// </summary>
        /// <remarks>
        /// Written once, so the tests below assert a property of the documented contract rather
        /// than of an expression copied out four times. BitRenderer packs exactly this pair into a
        /// long and keys its sprite pool on it.
        /// </remarks>
        private static long IdentityOf(Edge edge, BitInTransit bit) =>
            ((long)edge.Id << 32) | (uint)bit.Serial;

        /// <summary>
        /// A bit's handle belongs to that bit alone, for as long as the run lasts.
        /// </summary>
        /// <remarks>
        /// <see cref="SeveralBitsOnOneEdge_AreReportedNearestTargetFirst"/> establishes that no two
        /// bits on one edge share a handle **at one instant**, which is a weaker claim than the one
        /// renderers are handed. Following a bit *between frames* needs the handle to be unique over
        /// time as well, and nothing asserted that -- so a handle that aliases across a tick
        /// boundary read as correct and had a green test apparently backing it.
        ///
        /// Counted as distinct handles over the whole run: a source emits one bit per tick, so an
        /// edge carrying the whole stream should hand out exactly as many handles as there were bits.
        /// </remarks>
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void ABitsIdentity_IsNeverReusedByAnotherBit(int delay)
        {
            Bit[] stream = { Bit.One, Bit.Zero, Bit.One, Bit.Zero };

            var sim = new Simulation();
            var source = sim.Add(new SourceNode(stream));
            var sink = sim.Add(new SinkNode());
            Edge edge = sim.Connect(source.Out(0), sink.In(0), delay);

            var seen = new HashSet<long>();

            // Long enough for every bit to be emitted and delivered.
            for (int tick = 0; tick < stream.Length + delay + 2; tick++)
            {
                sim.Tick();

                for (int i = 0; i < edge.InTransitCount; i++)
                    seen.Add(IdentityOf(edge, edge.GetBitInTransit(i)));
            }

            Assert.AreEqual(stream.Length, sink.Received.Count, "every bit should have arrived");

            Assert.AreEqual(stream.Length, seen.Count,
                $"a delay-{delay} edge carried {stream.Length} bits but handed out {seen.Count} " +
                "distinct handles, so two different bits share one -- anything following a bit " +
                "between frames will mistake the second for the first");
        }

        /// <summary>
        /// Following bits by their handle sees every emission and every arrival.
        /// </summary>
        /// <remarks>
        /// The consequence the handle exists for, stated as the diff a renderer actually performs: a
        /// handle that was not there last frame is a bit that has just been emitted, and one that has
        /// gone with a single tick left is a bit that has just arrived. Those two events are what
        /// drive the spark bursts and the gate and landing cues.
        ///
        /// Written against the observation API alone -- no renderer, no scene -- because the defect
        /// is in what the API promises rather than in who consumes it.
        /// </remarks>
        [TestCase(1)]
        [TestCase(3)]
        public void FollowingBitsByIdentity_SeesEveryEmissionAndEveryArrival(int delay)
        {
            Bit[] stream = { Bit.One, Bit.Zero, Bit.One, Bit.Zero };

            var sim = new Simulation();
            var source = sim.Add(new SourceNode(stream));
            var sink = sim.Add(new SinkNode());
            Edge edge = sim.Connect(source.Out(0), sink.In(0), delay);

            var live = new Dictionary<long, int>();
            int emissions = 0;
            int arrivals = 0;

            for (int tick = 0; tick < stream.Length + delay + 2; tick++)
            {
                sim.Tick();

                var next = new Dictionary<long, int>();

                for (int i = 0; i < edge.InTransitCount; i++)
                {
                    BitInTransit bit = edge.GetBitInTransit(i);
                    long handle = IdentityOf(edge, bit);

                    if (!live.Remove(handle))
                        emissions++;   // not there last frame, so it has just been emitted

                    next[handle] = bit.TicksRemaining;
                }

                // Whatever is left was on the edge last frame and is not now. A single tick from the
                // target means it was delivered rather than lost to an edit.
                foreach (KeyValuePair<long, int> gone in live)
                {
                    if (gone.Value == 1)
                        arrivals++;
                }

                live = next;
            }

            Assert.AreEqual(stream.Length, emissions,
                $"a delay-{delay} edge carried {stream.Length} bits but only {emissions} looked new");

            Assert.AreEqual(stream.Length, arrivals,
                $"{stream.Length} bits reached the sink but only {arrivals} were seen arriving");
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
            Assert.AreEqual(1, sink.In(0).LastCollisionTick, "and a bit was destroyed on tick 1");
            AssertPortEmpty(sink.In(0));
            Assert.AreEqual(2, view.CorruptedCount, "both bits destroyed");
            CollectionAssert.IsEmpty(sink.Received);
        }

        /// <summary>
        /// A matching-value collision is as observable as a mixed one.
        /// </summary>
        /// <remarks>
        /// Only mixed collisions empty a port, so only they set the poison flag -- correctly, since
        /// the tick loop reads it to refuse later arrivals. But a matching collision still destroys
        /// a bit: CorruptedCount rises, a CorruptionSite is registered, and the bits-lost meter
        /// counts it. The one thing missing was a tick to hang it on, and the port renderer keys its
        /// flash on exactly that -- so half of all collisions scorched the board and moved the meter
        /// without the port itself reacting at all.
        ///
        /// Paired with MixedCollision_IsVisibleThroughLastCorruptedTick above, which is the case
        /// that always worked. Together they say the two collisions differ in what they do to the
        /// port and agree in being visible.
        /// </remarks>
        [Test]
        public void MatchingCollision_IsVisibleThroughLastCollisionTick()
        {
            // Two identical values land on one port during tick 1. The port keeps its value and
            // the arrival is destroyed, so nothing is poisoned and nothing is emptied.
            var sim = new Simulation();
            var first = sim.Add(new SourceNode(new[] { Bit.One }));
            var second = sim.Add(new SourceNode(new[] { Bit.One }));
            var gate = sim.Add(new AndGate());   // never fires: its other input is unwired

            sim.Connect(first.Out(0), gate.In(0), delay: 1);
            sim.Connect(second.Out(0), gate.In(0), delay: 1);

            SimulationView view = sim.View;
            Assert.AreEqual(-1, gate.In(0).LastCollisionTick, "nothing has collided yet");

            sim.Run(2);

            Assert.AreEqual(1, view.CorruptedCount, "one bit destroyed, the arrival");
            AssertPortHolds(gate.In(0), Bit.One, "the port keeps the value it was holding");

            Assert.AreEqual(-1, gate.In(0).LastCorruptedTick,
                "nothing was emptied, so the poison flag must stay clear");

            Assert.AreEqual(1, gate.In(0).LastCollisionTick,
                "a bit was destroyed at this port on tick 1, and the view has no other way to know");
        }

        /// <summary>
        /// Recording a matching collision does not poison the port.
        /// </summary>
        /// <remarks>
        /// The reason this needed a second field rather than a wider meaning for the first. Set the
        /// poison flag on a matching collision and a third arrival in the same tick takes the
        /// poison branch -- one bit destroyed, port still holding -- instead of being a mixed
        /// collision, which destroys two and clears it. Different CorruptedCount, different port
        /// state, from what looks like a reporting change.
        /// </remarks>
        [Test]
        public void AMatchingCollision_LeavesAThirdArrivalToCollideNormally()
        {
            var sim = new Simulation();
            var a = sim.Add(new SourceNode(new[] { Bit.One }));
            var b = sim.Add(new SourceNode(new[] { Bit.One }));
            var c = sim.Add(new SourceNode(new[] { Bit.Zero }));
            var gate = sim.Add(new AndGate());   // never fires

            sim.Connect(a.Out(0), gate.In(0), delay: 1);
            sim.Connect(b.Out(0), gate.In(0), delay: 1);
            sim.Connect(c.Out(0), gate.In(0), delay: 1);

            sim.Run(2);

            // One lands. The matching one is destroyed. The differing one then meets a port that is
            // still holding, so both of those die too: 1 + 2 = 3.
            Assert.AreEqual(3, sim.CorruptedCount, "the third arrival must still collide properly");
            AssertPortEmpty(gate.In(0));
            Assert.AreEqual(1, gate.In(0).LastCorruptedTick, "the mixed collision poisoned it");
            Assert.AreEqual(1, gate.In(0).LastCollisionTick);
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

        private static void AssertPortHolds(InputPort port, Bit value, string because = null)
        {
            Assert.IsTrue(port.IsOccupied, because ?? $"{port} should be occupied");
            Assert.IsTrue(port.Pending.HasValue);
            Assert.AreEqual(value, port.Pending.Value, because);
        }

        private static void AssertPortEmpty(InputPort port)
        {
            Assert.IsFalse(port.IsOccupied, $"{port} should be empty");
            Assert.IsFalse(port.Pending.HasValue);
        }
    }
}
