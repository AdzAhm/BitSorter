using NUnit.Framework;
using BitSorter.LogicCore;

namespace BitSorter.LogicCore.Tests
{
    /// <summary>
    /// What a source says it will send next, which the board draws above it.
    /// </summary>
    /// <remarks>
    /// Its emitting is covered where it is wired to something, in <c>SimulationTests</c>. These
    /// cover the one thing it reports about itself.
    /// </remarks>
    public class SourceNodeTests
    {
        [Test]
        public void NextBit_IsEachBitOfTheStream_InTurn_ThenNothing()
        {
            var sim = new Simulation();
            var source = sim.Add(new SourceNode(new[] { Bit.One, Bit.Zero, Bit.One }));

            Assert.AreEqual(Bit.One, source.NextBit, "before the first tick");

            sim.Tick();
            Assert.AreEqual(Bit.Zero, source.NextBit, "after the first bit left");

            sim.Tick();
            Assert.AreEqual(Bit.One, source.NextBit, "after the second");

            sim.Tick();
            Assert.IsNull(source.NextBit, "the stream is spent, so nothing is next");
            Assert.IsTrue(source.IsExhausted);
        }

        /// <summary>
        /// On a clock the next bit is the next vector's, not the silent tick before it.
        /// </summary>
        [Test]
        public void NextBit_SkipsTheSilentTicksBetweenVectors()
        {
            var sim = new Simulation();
            var source = sim.Add(new SourceNode(new Bit?[] { Bit.Zero, null, null, Bit.One }));

            Assert.AreEqual(Bit.Zero, source.NextBit);

            sim.Tick();
            Assert.AreEqual(Bit.One, source.NextBit, "the gap is not a bit");

            sim.Tick();
            Assert.AreEqual(Bit.One, source.NextBit, "still waiting for the second vector");

            sim.Tick();
            sim.Tick();
            Assert.IsNull(source.NextBit);
        }

        [Test]
        public void AnEmptyStream_HasNothingNext()
        {
            var source = new SourceNode(new Bit[0]);

            Assert.IsNull(source.NextBit);
        }

        /// <summary>Asking is free: it does not move the source on.</summary>
        [Test]
        public void AskingForTheNextBit_SendsNothing()
        {
            var sim = new Simulation();
            var source = sim.Add(new SourceNode(new[] { Bit.One, Bit.Zero }));
            var sink = sim.Add(new SinkNode());
            sim.Connect(source.Out(0), sink.In(0), delay: 1);

            for (int i = 0; i < 5; i++)
                Assert.AreEqual(Bit.One, source.NextBit);

            sim.Run(3);

            Assert.AreEqual(2, sink.Received.Count, "reading NextBit changed what was sent");
            Assert.AreEqual(Bit.One, sink.Received[0].Value);
            Assert.AreEqual(Bit.Zero, sink.Received[1].Value);
        }
    }
}
