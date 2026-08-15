using System;
using NUnit.Framework;
using BitSorter.LogicCore;

namespace BitSorter.LogicCore.Tests
{
    /// <summary>
    /// Removal tombstones: a removed slot becomes null and its id is retired forever, so every
    /// surviving id keeps meaning the same node. Anything keyed by id -- a layout table, a
    /// renderer's visuals -- stays correct across an edit.
    /// </summary>
    public class RemovalTests
    {
        // -----------------------------------------------------------------
        // Ids
        // -----------------------------------------------------------------

        [Test]
        public void RemovingFromTheMiddle_LeavesSurvivorIdsUntouched_AndRetiresTheHole()
        {
            var sim = new Simulation();
            var first = sim.Add(new PassThroughNode { Name = "first" });
            var middle = sim.Add(new PassThroughNode { Name = "middle" });
            var last = sim.Add(new PassThroughNode { Name = "last" });
            var tail = sim.Add(new SinkNode() { Name = "tail" });

            Assert.AreEqual(0, first.Id);
            Assert.AreEqual(1, middle.Id);
            Assert.AreEqual(2, last.Id);
            Assert.AreEqual(3, tail.Id);

            sim.Remove(middle);

            // Survivors keep the ids they were issued -- especially the ones *after* the hole,
            // which is exactly what compaction would have broken.
            Assert.AreEqual(0, first.Id, "id before the hole moved");
            Assert.AreEqual(2, last.Id, "id after the hole moved");
            Assert.AreEqual(3, tail.Id, "id after the hole moved");

            Assert.AreSame(first, sim.GetNode(0));
            Assert.IsNull(sim.GetNode(1), "removed id should read as null");
            Assert.AreSame(last, sim.GetNode(2));
            Assert.AreSame(tail, sim.GetNode(3));

            Assert.AreEqual(-1, middle.Id, "a removed node should report itself unregistered");

            // NodeCount is an id bound, LiveNodeCount is the population.
            Assert.AreEqual(4, sim.NodeCount, "id bound should not shrink");
            Assert.AreEqual(3, sim.LiveNodeCount, "population should shrink");
        }

        [Test]
        public void IteratingOverATombstone_SkipsItWithoutThrowing()
        {
            var sim = new Simulation();
            sim.Add(new PassThroughNode { Name = "a" });
            var doomed = sim.Add(new PassThroughNode { Name = "b" });
            sim.Add(new PassThroughNode { Name = "c" });

            sim.Remove(doomed);

            int seen = 0;
            Assert.DoesNotThrow(() =>
            {
                SimulationView view = sim.View;
                for (int id = 0; id < view.NodeCount; id++)
                {
                    Node node = view.GetNode(id);
                    if (node == null)
                        continue;

                    seen++;
                    Assert.AreEqual(id, node.Id);
                }
            });

            Assert.AreEqual(2, seen);
            Assert.AreEqual(sim.LiveNodeCount, seen, "live count should match what iteration finds");
        }

        [Test]
        public void AddingAfterARemoval_TakesAFreshId_NeverTheRetiredOne()
        {
            var sim = new Simulation();
            sim.Add(new PassThroughNode { Name = "a" });
            var doomed = sim.Add(new PassThroughNode { Name = "b" });
            sim.Add(new PassThroughNode { Name = "c" });

            sim.Remove(doomed);
            var added = sim.Add(new AndGate { Name = "new" });

            // Recycling id 1 would silently hand the newcomer the removed node's layout entry and
            // whatever visuals were keyed to it.
            Assert.AreEqual(3, added.Id, "a retired id must never be reissued");
            Assert.IsNull(sim.GetNode(1), "the hole stays a hole");
            Assert.AreSame(added, sim.GetNode(3));
            Assert.AreEqual(4, sim.NodeCount);
            Assert.AreEqual(3, sim.LiveNodeCount);
        }

        [Test]
        public void RemovingTwice_Throws()
        {
            var sim = new Simulation();
            var node = sim.Add(new PassThroughNode());

            sim.Remove(node);

            Assert.Throws<InvalidOperationException>(() => sim.Remove(node));
        }

        [Test]
        public void RemovingANodeFromAnotherSimulation_Throws()
        {
            var owner = new Simulation();
            var stranger = new Simulation();
            var node = owner.Add(new PassThroughNode());
            stranger.Add(new PassThroughNode());

            Assert.Throws<InvalidOperationException>(() => stranger.Remove(node));
        }

        // -----------------------------------------------------------------
        // Edges
        // -----------------------------------------------------------------

        [Test]
        public void RemovingANode_TakesEveryEdgeTouchingIt_FromEitherSide()
        {
            var sim = new Simulation();
            var source = sim.Add(new SourceNode(new[] { Bit.One }) { Name = "source" });
            var middle = sim.Add(new PassThroughNode { Name = "middle" });
            var sink = sim.Add(new SinkNode() { Name = "sink" });
            var bystander = sim.Add(new SourceNode(new[] { Bit.One }) { Name = "bystander" });
            var bystanderSink = sim.Add(new SinkNode() { Name = "bystanderSink" });

            Edge incoming = sim.Connect(source.Out(0), middle.In(0), 1);   // middle as target
            Edge outgoing = sim.Connect(middle.Out(0), sink.In(0), 1);     // middle as source
            Edge untouched = sim.Connect(bystander.Out(0), bystanderSink.In(0), 1);

            // Capture ids first: a removed edge reports Id -1, so reading it back off the object
            // afterwards would be looking up the wrong thing.
            int incomingId = incoming.Id;
            int outgoingId = outgoing.Id;

            sim.Remove(middle);

            Assert.AreEqual(-1, incoming.Id, "a removed edge should report itself unregistered");
            Assert.IsNull(sim.GetEdge(incomingId), "edge into the removed node survived");
            Assert.IsNull(sim.GetEdge(outgoingId), "edge out of the removed node survived");
            Assert.AreSame(untouched, sim.GetEdge(untouched.Id), "unrelated edge was removed");
            Assert.AreEqual(2, untouched.Id, "surviving edge id drifted");

            Assert.AreEqual(3, sim.EdgeCount, "id bound should not shrink");
            Assert.AreEqual(1, sim.LiveEdgeCount);
        }

        [Test]
        public void DisconnectDetachesFromTheOutputPort_SoNothingIsEmittedOntoADeadEdge()
        {
            var sim = new Simulation();
            var source = sim.Add(new SourceNode(new[] { Bit.One, Bit.One }));
            var sink = sim.Add(new SinkNode());
            Edge edge = sim.Connect(source.Out(0), sink.In(0), 1);

            // Without Detach the port would keep pushing bits onto the removed edge forever.
            sim.Disconnect(edge);
            Assert.AreEqual(0, source.Out(0).Edges.Count, "port still holds the dead edge");

            sim.Run(4);

            CollectionAssert.IsEmpty(sink.Received);
            Assert.AreEqual(0, sim.CorruptedCount);
        }

        // -----------------------------------------------------------------
        // Bits lost to an edit are not corruption
        // -----------------------------------------------------------------

        [Test]
        public void BitsInTransitOnARemovedEdge_VanishWithoutCountingAsCorruption()
        {
            var sim = new Simulation();
            var source = sim.Add(new SourceNode(new[] { Bit.One, Bit.Zero, Bit.One }));
            var sink = sim.Add(new SinkNode());
            Edge edge = sim.Connect(source.Out(0), sink.In(0), 5);

            sim.Run(3);   // three bits mid-flight, none delivered
            Assert.AreEqual(3, edge.InTransitCount);
            Assert.AreEqual(0, sim.CorruptedCount);

            sim.Disconnect(edge);
            sim.Run(6);

            CollectionAssert.IsEmpty(sink.Received);

            // CorruptedCount means collisions. Folding player edits into it would wreck the one
            // number the game uses to tell a mis-timed circuit from a working one.
            Assert.AreEqual(0, sim.CorruptedCount, "an edit must not register as corruption");
        }

        [Test]
        public void DeletingALoadedWire_LeavesAnAlreadyNonZeroCorruptedCountUnchanged()
        {
            // The sibling test above starts from zero, so it can only show that a deletion does not
            // add to zero. This drives the count to a real value first, then deletes a wire with
            // bits on it, so "unchanged" is actually distinguishable from "still happens to be 0".
            var sim = new Simulation();
            var left = sim.Add(new SourceNode(new[] { Bit.One }) { Name = "left" });
            var right = sim.Add(new SourceNode(new[] { Bit.Zero }) { Name = "right" });
            var collisionSink = sim.Add(new SinkNode() { Name = "collisionSink" });

            // Differing values into one port on the same tick: both destroyed, count goes to 2.
            sim.Connect(left.Out(0), collisionSink.In(0), 1);
            sim.Connect(right.Out(0), collisionSink.In(0), 1);

            var loadedSource = sim.Add(new SourceNode(new[] { Bit.One, Bit.One, Bit.One }) { Name = "loaded" });
            var loadedSink = sim.Add(new SinkNode() { Name = "loadedSink" });
            Edge loaded = sim.Connect(loadedSource.Out(0), loadedSink.In(0), 6);

            sim.Run(3);

            int corruptedBefore = sim.CorruptedCount;
            Assert.AreEqual(2, corruptedBefore, "expected the collision to have registered first");
            Assert.AreEqual(3, loaded.InTransitCount, "expected bits still travelling the doomed wire");

            sim.Disconnect(loaded);
            sim.Run(8);

            Assert.AreEqual(corruptedBefore, sim.CorruptedCount,
                "deleting a wire must not change the corruption tally, in either direction");
            CollectionAssert.IsEmpty(loadedSink.Received, "bits on a deleted wire should not arrive");
        }

        [Test]
        public void ABitWaitingInARemovedNodesPort_VanishesWithoutCountingAsCorruption()
        {
            var sim = new Simulation();
            var fast = sim.Add(new SourceNode(new[] { Bit.One }));
            var gate = sim.Add(new AndGate { Name = "gate" });   // port 1 never fed
            sim.Connect(fast.Out(0), gate.In(0), 1);

            sim.Run(2);
            Assert.IsTrue(gate.In(0).IsOccupied, "expected a bit parked in the port");
            Assert.AreEqual(0, sim.CorruptedCount);

            sim.Remove(gate);
            sim.Run(4);

            Assert.AreEqual(0, sim.CorruptedCount, "an edit must not register as corruption");
        }

        // -----------------------------------------------------------------
        // The rest of the graph keeps working
        // -----------------------------------------------------------------

        [Test]
        public void AfterARemoval_TheSurvivingGraphStillTicksCorrectly()
        {
            // The surviving half is the delay-3 timing that
            // SimulationTests.BitOnDelayThreeEdge_ArrivesAtTickThree pins independently.
            var sim = new Simulation();
            var doomedSource = sim.Add(new SourceNode(new[] { Bit.One }) { Name = "doomed" });
            var doomedSink = sim.Add(new SinkNode() { Name = "doomedSink" });
            var source = sim.Add(new SourceNode(new[] { Bit.One }) { Name = "survivor" });
            var sink = sim.Add(new SinkNode() { Name = "survivorSink" });

            sim.Connect(doomedSource.Out(0), doomedSink.In(0), 1);
            sim.Connect(source.Out(0), sink.In(0), 3);

            sim.Remove(doomedSource);
            sim.Remove(doomedSink);

            sim.Run(3);
            CollectionAssert.IsEmpty(sink.Received, "arrived earlier than tick 3");

            sim.Tick();

            Assert.AreEqual(1, sink.Received.Count);
            Assert.AreEqual(Bit.One, sink.Received[0].Value);
            Assert.AreEqual(3, sink.Received[0].Tick);
            Assert.AreEqual(0, sim.CorruptedCount);
        }

        [Test]
        public void RemovingOneBranch_LeavesTheOtherBranchOfAFanOutIntact()
        {
            var sim = new Simulation();
            var source = sim.Add(new SourceNode(new[] { Bit.One }));
            var keep = sim.Add(new SinkNode() { Name = "keep" });
            var drop = sim.Add(new SinkNode() { Name = "drop" });

            sim.Connect(source.Out(0), keep.In(0), 1);
            sim.Connect(source.Out(0), drop.In(0), 1);

            sim.Remove(drop);
            sim.Run(3);

            Assert.AreEqual(1, keep.Received.Count, "surviving fan-out branch stopped working");
            Assert.AreEqual(Bit.One, keep.Received[0].Value);
            Assert.AreEqual(1, keep.Received[0].Tick);
            Assert.AreEqual(0, sim.CorruptedCount);
        }
    }
}
