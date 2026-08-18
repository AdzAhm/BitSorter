using System.Collections.Generic;
using NUnit.Framework;
using BitSorter.LogicCore;
using BitSorter.View;
using UnityEngine;

namespace BitSorter.LogicCore.Tests
{
    /// <summary>
    /// <see cref="Simulation.CorruptionSites"/>: the companion to CorruptedCount that says where,
    /// not just how much.
    /// </summary>
    /// <remarks>
    /// Exists so the board can show the player the junction that destroyed their bits instead of a
    /// number they have to interpret. The count was already watchable; what it never said is which
    /// of half a dozen gates went wrong.
    /// </remarks>
    public class CorruptionSiteTests
    {
        /// <summary>
        /// Two sources into one port, arriving on the same tick. The smallest collision there is.
        /// </summary>
        private static Simulation Collide(Bit first, Bit second, int ticks = 3)
        {
            var sim = new Simulation();
            var a = sim.Add(new SourceNode(new[] { first }) { Name = "a" });
            var b = sim.Add(new SourceNode(new[] { second }) { Name = "b" });
            var sink = sim.Add(new SinkNode { Name = "sink" });

            sim.Connect(a.Out(0), sink.In(0), delay: 1);
            sim.Connect(b.Out(0), sink.In(0), delay: 1);

            sim.Run(ticks);
            return sim;
        }

        // -----------------------------------------------------------------
        // What gets recorded
        // -----------------------------------------------------------------

        [Test]
        public void ACleanRun_RecordsNoSites()
        {
            var sim = new Simulation();
            var source = sim.Add(new SourceNode(new[] { Bit.One, Bit.Zero }));
            var sink = sim.Add(new SinkNode());
            sim.Connect(source.Out(0), sink.In(0), delay: 1);

            sim.Run(5);

            Assert.AreEqual(0, sim.CorruptedCount);
            CollectionAssert.IsEmpty(sim.CorruptionSites);
        }

        [Test]
        public void ACollision_RecordsThePortItHappenedAt()
        {
            Simulation sim = Collide(Bit.Zero, Bit.One);

            Assert.AreEqual(1, sim.CorruptionSites.Count);

            InputPort site = sim.CorruptionSites[0];

            Assert.AreEqual("sink", site.Owner.Name, "the site should be the port that was written");
            Assert.AreEqual(0, site.Index);
        }

        /// <summary>
        /// A matching-value collision destroys one bit rather than two, and is still a site. The
        /// port keeps its value, so nothing downstream reports a problem -- this is the quietest
        /// way to lose a bit, and the one most worth marking.
        /// </summary>
        [Test]
        public void AMatchingValueCollision_IsStillASite()
        {
            Simulation sim = Collide(Bit.One, Bit.One);

            Assert.AreEqual(1, sim.CorruptedCount, "one bit destroyed, not two");
            Assert.AreEqual(1, sim.CorruptionSites.Count);
        }

        [Test]
        public void APortThatCollidesEveryTick_IsRecordedOnce()
        {
            var sim = new Simulation();
            var a = sim.Add(new SourceNode(new[] { Bit.Zero, Bit.Zero, Bit.Zero, Bit.Zero }));
            var b = sim.Add(new SourceNode(new[] { Bit.One, Bit.One, Bit.One, Bit.One }));
            var sink = sim.Add(new SinkNode());

            sim.Connect(a.Out(0), sink.In(0), delay: 1);
            sim.Connect(b.Out(0), sink.In(0), delay: 1);

            sim.Run(6);

            Assert.Greater(sim.CorruptedCount, 2, "several ticks should have collided");
            Assert.AreEqual(1, sim.CorruptionSites.Count,
                "the same port colliding repeatedly is one place on the board, not several");
        }

        [Test]
        public void TwoSeparatePorts_AreRecordedSeparately()
        {
            var sim = new Simulation();
            var a = sim.Add(new SourceNode(new[] { Bit.Zero }));
            var b = sim.Add(new SourceNode(new[] { Bit.One }));
            var left = sim.Add(new SinkNode { Name = "left" });
            var right = sim.Add(new SinkNode { Name = "right" });

            sim.Connect(a.Out(0), left.In(0), delay: 1);
            sim.Connect(b.Out(0), left.In(0), delay: 1);
            sim.Connect(a.Out(0), right.In(0), delay: 2);
            sim.Connect(b.Out(0), right.In(0), delay: 2);

            sim.Run(4);

            var names = new List<string>();
            foreach (InputPort port in sim.CorruptionSites)
                names.Add(port.Owner.Name);

            CollectionAssert.AreEquivalent(new[] { "left", "right" }, names);
        }

        // -----------------------------------------------------------------
        // Lifetime
        // -----------------------------------------------------------------

        /// <summary>
        /// Sites accumulate across the run exactly as the count does, rather than being cleared
        /// per tick.
        /// </summary>
        /// <remarks>
        /// This is what makes the marks drawable. Several ticks can elapse inside one frame, so
        /// anything cleared per tick would be invisible to a renderer polling once a frame.
        /// </remarks>
        [Test]
        public void SitesSurviveLaterTicks()
        {
            var sim = new Simulation();
            var a = sim.Add(new SourceNode(new[] { Bit.Zero }));
            var b = sim.Add(new SourceNode(new[] { Bit.One }));
            var sink = sim.Add(new SinkNode());

            sim.Connect(a.Out(0), sink.In(0), delay: 1);
            sim.Connect(b.Out(0), sink.In(0), delay: 1);

            sim.Run(2);
            Assert.AreEqual(1, sim.CorruptionSites.Count, "the collision should have been recorded");

            sim.Run(20);

            Assert.AreEqual(1, sim.CorruptionSites.Count,
                "quiet ticks afterwards must not erase where the mistake was");
        }

        /// <summary>
        /// Removing a node drops its sites, because the mark has nowhere left to be drawn and the
        /// node's id is about to be retired.
        /// </summary>
        [Test]
        public void RemovingTheNode_DropsItsSitesButNotTheCount()
        {
            Simulation sim = Collide(Bit.Zero, Bit.One);

            int destroyed = sim.CorruptedCount;
            Assert.AreEqual(1, sim.CorruptionSites.Count);

            Node sink = sim.CorruptionSites[0].Owner;
            sim.Remove(sink);

            CollectionAssert.IsEmpty(sim.CorruptionSites,
                "a site on a node that no longer exists cannot be pointed at");
            Assert.AreEqual(destroyed, sim.CorruptedCount,
                "the count records what the run destroyed and an edit must not revise it");
        }

        // -----------------------------------------------------------------
        // The site is where the mistake actually is
        // -----------------------------------------------------------------

        /// <summary>
        /// The whole point, on the shipped level built to produce this failure: the mark lands on
        /// the AND whose two inputs disagree about timing, not on the sources or the XOR.
        /// </summary>
        [Test]
        public void OnBalanceThePaths_TheSiteIsTheAndGateNotTheSources()
        {
            LevelLoadResult result = LevelLoader.Load("balance-the-paths", LevelTestFixtures.Board);
            Assert.IsTrue(result.IsValid, result.Error);

            LevelDefinition level = result.Level;

            var andCell = new Vector2Int(0, -1);

            var blueprint = new CircuitBlueprint();
            blueprint.Place(new Vector2Int(0, 1), GateKind.Xor);
            blueprint.Place(andCell, GateKind.And);

            LevelTestFixtures.Wire(blueprint, new Vector2Int(-3, 1), new Vector2Int(0, 1), toPort: 0);
            LevelTestFixtures.Wire(blueprint, new Vector2Int(-3, -1), new Vector2Int(0, 1), toPort: 1);
            LevelTestFixtures.Wire(blueprint, new Vector2Int(0, 1), andCell, toPort: 0);
            LevelTestFixtures.Wire(blueprint, new Vector2Int(-3, -1), andCell, toPort: 1);
            LevelTestFixtures.Wire(blueprint, andCell, new Vector2Int(3, 0));

            BuiltCircuit built = CircuitBuilder.Build(level, blueprint);
            LevelGrader.RunToCompletion(built.Simulation, level, built.FixtureNodeIds);

            Assert.Greater(built.Simulation.CorruptedCount, 0, "the naive wiring should corrupt");
            CollectionAssert.IsNotEmpty(built.Simulation.CorruptionSites);

            // Both of the AND's inputs end up as sites, and the order they break in is the lesson.
            // The late path collides first; that jams the gate, so the early path's own port is
            // never drained and collides on the following tick. Marking both is right -- the fault
            // is the pair of inputs disagreeing, not either one of them alone.
            foreach (InputPort site in built.Simulation.CorruptionSites)
            {
                Assert.AreEqual(andCell, built.Cells[site.Owner.Id],
                    "every mark belongs on the gate whose inputs disagree, not on the XOR or the " +
                    "sources -- the player should be pointed at the junction they got wrong");
            }
        }
    }
}
