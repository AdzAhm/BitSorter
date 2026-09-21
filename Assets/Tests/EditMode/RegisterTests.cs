using NUnit.Framework;
using BitSorter.LogicCore;

namespace BitSorter.LogicCore.Tests
{
    /// <summary>
    /// The one node that remembers, and the rules that follow from it.
    /// </summary>
    /// <remarks>
    /// The state table is the first half. The second half is timing, which is where a register
    /// earns its remarks: it emits before it is given anything, it never holds a bit between ticks,
    /// and a loop built around it cannot keep up with a source that emits every tick. That last one
    /// is why sequential levels have a clock at all.
    /// </remarks>
    public class RegisterTests
    {
        private static SinkNode.Reception R(Bit value, int tick) => new SinkNode.Reception(value, tick);

        /// <summary>A register with a source feeding it and a sink reading it, every wire delay 1.</summary>
        private static SinkNode Chain(params Bit[] stream)
        {
            var sim = new Simulation();
            var source = sim.Add(new SourceNode(stream) { Name = "in" });
            var register = sim.Add(new RegisterNode { Name = "reg" });
            var sink = sim.Add(new SinkNode { Name = "out" });

            sim.Connect(source.Out(0), register.In(0), delay: 1);
            sim.Connect(register.Out(0), sink.In(0), delay: 1);

            sim.Run(stream.Length + 4);

            Assert.AreEqual(0, sim.CorruptedCount, "a plain chain should never collide");
            return sink;
        }

        // -----------------------------------------------------------------
        // The state table
        // -----------------------------------------------------------------

        /// <summary>
        /// Given the bit it holds and the bit it is handed: what it emits, and what it holds next.
        /// </summary>
        /// <remarks>
        /// A register's table is its state table rather than a truth table -- the output depends on
        /// what it was holding, which is the entire point of it. It emits the held bit first and the
        /// new one second, so both rows of the table appear as the two bits the sink receives.
        /// </remarks>
        [TestCase(Bit.Zero, Bit.Zero)]
        [TestCase(Bit.Zero, Bit.One)]
        [TestCase(Bit.One, Bit.Zero)]
        [TestCase(Bit.One, Bit.One)]
        public void TheStateTable(Bit held, Bit given)
        {
            var sim = new Simulation();
            var source = sim.Add(new SourceNode(new[] { given }) { Name = "in" });
            var register = sim.Add(new RegisterNode(held) { Name = "reg" });
            var sink = sim.Add(new SinkNode { Name = "out" });

            sim.Connect(source.Out(0), register.In(0), delay: 1);
            sim.Connect(register.Out(0), sink.In(0), delay: 1);

            Assert.AreEqual(held, register.State, "a register holds its starting bit before the run");
            Assert.IsFalse(register.HasStarted);

            sim.Run(4);

            CollectionAssert.AreEqual(new[] { R(held, 1), R(given, 2) }, sink.Received,
                "a register emits the bit it held, then the bit it was given");
            Assert.AreEqual(given, register.State, "it holds the bit it was given");
        }

        [Test]
        public void EveryRegisterStartsHoldingZero()
        {
            // No level, save file or palette entry can say otherwise. A machine that wants to start
            // somewhere else encodes its states so that the reset state is the zero one.
            var register = new RegisterNode();

            Assert.AreEqual(Bit.Zero, register.Initial);
            Assert.AreEqual(Bit.Zero, register.State);
        }

        // -----------------------------------------------------------------
        // What it does to a stream
        // -----------------------------------------------------------------

        [Test]
        public void ItHandsOnTheWholeStream_BehindItsOwnStartingBit()
        {
            SinkNode sink = Chain(Bit.One, Bit.Zero, Bit.One);

            CollectionAssert.AreEqual(
                new[] { R(Bit.Zero, 1), R(Bit.One, 2), R(Bit.Zero, 3), R(Bit.One, 4) },
                sink.Received,
                "the starting bit comes out first and every input follows it, one clock behind");
        }

        [Test]
        public void ARegisterEmitsOneBitMoreThanItIsGiven()
        {
            // The bit it was holding. A sink fed from a register therefore receives one more bit
            // than the level has vectors, which is what a level's expected values have to allow for.
            SinkNode sink = Chain(Bit.One, Bit.One, Bit.Zero, Bit.One);

            Assert.AreEqual(5, sink.Received.Count);
        }

        [Test]
        public void TwoInSeries_DelayTheStreamByTwoClocks()
        {
            var sim = new Simulation();
            var source = sim.Add(new SourceNode(new[] { Bit.One, Bit.Zero }) { Name = "in" });
            var first = sim.Add(new RegisterNode { Name = "reg1" });
            var second = sim.Add(new RegisterNode { Name = "reg2" });
            var sink = sim.Add(new SinkNode { Name = "out" });

            sim.Connect(source.Out(0), first.In(0), delay: 1);
            sim.Connect(first.Out(0), second.In(0), delay: 1);
            sim.Connect(second.Out(0), sink.In(0), delay: 1);

            sim.Run(8);

            CollectionAssert.AreEqual(
                new[] { R(Bit.Zero, 1), R(Bit.Zero, 2), R(Bit.One, 3), R(Bit.Zero, 4) },
                sink.Received,
                "two registers put two starting bits in front of the stream");
            Assert.AreEqual(0, sim.CorruptedCount);
        }

        // -----------------------------------------------------------------
        // Timing
        // -----------------------------------------------------------------

        [Test]
        public void ARegisterNeverHoldsABitBetweenTicks()
        {
            // It fires in the tick its bit arrives, so its port is empty whenever anything looks at
            // it. The view depends on this: an occupied input port means a stalled gate, and a
            // register that sat on a bit would be drawn as stuck while working perfectly.
            var sim = new Simulation();
            var source = sim.Add(new SourceNode(new[] { Bit.One, Bit.Zero, Bit.One }) { Name = "in" });
            var register = sim.Add(new RegisterNode { Name = "reg" });
            var sink = sim.Add(new SinkNode { Name = "out" });

            sim.Connect(source.Out(0), register.In(0), delay: 1);
            sim.Connect(register.Out(0), sink.In(0), delay: 1);

            for (int tick = 0; tick < 8; tick++)
            {
                sim.Tick();

                Assert.IsFalse(register.In(0).IsOccupied,
                    $"the register was still holding a bit after tick {tick}");
            }
        }

        [Test]
        public void ItEmitsItsStartingBitWithNothingWiredIn()
        {
            var sim = new Simulation();
            var register = sim.Add(new RegisterNode { Name = "reg" });
            var sink = sim.Add(new SinkNode { Name = "out" });

            sim.Connect(register.Out(0), sink.In(0), delay: 2);

            sim.Run(6);

            CollectionAssert.AreEqual(new[] { R(Bit.Zero, 2) }, sink.Received,
                "the starting bit leaves on the first tick, whether or not anything feeds the register");
            Assert.IsTrue(register.HasStarted);
        }

        // -----------------------------------------------------------------
        // Loops
        // -----------------------------------------------------------------

        /// <summary>
        /// A loop cannot keep up with a source that emits every tick, and this is why sequential
        /// levels space their vectors out.
        /// </summary>
        /// <remarks>
        /// The shortest loop is two wires, so the state takes two ticks to come back round while a
        /// dense source sends something every tick. The second input waits in the gate's port and
        /// the third lands on top of it. Nothing here is wired wrongly: the level's clock is what
        /// fixes it.
        /// </remarks>
        [Test]
        public void ALoopFedEveryTick_Collides()
        {
            var sim = new Simulation();
            var source = sim.Add(new SourceNode(new[] { Bit.One, Bit.One, Bit.One, Bit.One }) { Name = "in" });
            var xor = sim.Add(new XorGate { Name = "xor" });
            var register = sim.Add(new RegisterNode { Name = "reg" });

            sim.Connect(source.Out(0), xor.In(0), delay: 1);
            sim.Connect(register.Out(0), xor.In(1), delay: 1);
            sim.Connect(xor.Out(0), register.In(0), delay: 1);

            sim.Run(8);

            Assert.Greater(sim.CorruptedCount, 0,
                "a two-wire loop cannot keep up with a bit every tick");
        }

        /// <summary>
        /// The same loop, with its vectors a clock apart, toggles exactly as a T flip-flop should.
        /// </summary>
        /// <remarks>
        /// Two wires round the loop and a clock of two ticks: the state is back at the gate before
        /// the next input arrives. The output is the new state, so a stream of 1s toggles on every
        /// cycle and a 0 holds.
        /// </remarks>
        [Test]
        public void ALoopWithinItsClock_Toggles()
        {
            SinkNode sink = Toggle(loopDelay: 1, clock: 2, Bit.One, Bit.One, Bit.Zero, Bit.One);

            CollectionAssert.AreEqual(
                new[] { Bit.One, Bit.Zero, Bit.Zero, Bit.One },
                Values(sink),
                "1 toggles the state, 0 holds it");
        }

        /// <summary>
        /// A loop slower than its clock falls behind, and the inputs waiting for it collide.
        /// </summary>
        /// <remarks>
        /// This is the chapter's version of the game's oldest claim: the circuit computes the right
        /// thing and still fails, because the state cannot get back in time. It is the same fault a
        /// real design has when its logic does not settle within the clock period.
        /// </remarks>
        [Test]
        public void ALoopSlowerThanItsClock_Collides()
        {
            var sim = new Simulation();
            BuildToggle(sim, loopDelay: 2, clock: 2, out SinkNode _,
                Bit.One, Bit.One, Bit.One, Bit.One, Bit.One, Bit.One);

            sim.Run(30);

            Assert.Greater(sim.CorruptedCount, 0,
                "three wires round a two-tick clock should not survive");
        }

        private static SinkNode Toggle(int loopDelay, int clock, params Bit[] stream)
        {
            var sim = new Simulation();
            BuildToggle(sim, loopDelay, clock, out SinkNode sink, stream);

            sim.Run(stream.Length * clock + 8);

            Assert.AreEqual(0, sim.CorruptedCount, "a loop inside its clock should never collide");
            return sink;
        }

        /// <summary>A T flip-flop: the state XORed with the input becomes the next state.</summary>
        private static void BuildToggle(
            Simulation sim, int loopDelay, int clock, out SinkNode sink, params Bit[] stream)
        {
            var source = sim.Add(new SourceNode(Spaced(stream, clock)) { Name = "in" });
            var xor = sim.Add(new XorGate { Name = "xor" });
            var register = sim.Add(new RegisterNode { Name = "reg" });
            sink = sim.Add(new SinkNode { Name = "out" });

            sim.Connect(source.Out(0), xor.In(0), delay: 1);
            sim.Connect(register.Out(0), xor.In(1), delay: 1);
            sim.Connect(xor.Out(0), register.In(0), delay: loopDelay);
            sim.Connect(xor.Out(0), sink.In(0), delay: 1);
        }

        /// <summary>A stream with the clock's silent ticks between its bits, and none after.</summary>
        private static Bit?[] Spaced(Bit[] stream, int clock)
        {
            var spaced = new System.Collections.Generic.List<Bit?>();

            for (int i = 0; i < stream.Length; i++)
            {
                if (i > 0)
                {
                    for (int gap = 1; gap < clock; gap++)
                        spaced.Add(null);
                }

                spaced.Add(stream[i]);
            }

            return spaced.ToArray();
        }

        private static Bit[] Values(SinkNode sink)
        {
            var values = new Bit[sink.Received.Count];

            for (int i = 0; i < values.Length; i++)
                values[i] = sink.Received[i].Value;

            return values;
        }

        [Test]
        public void ALoopWithNothingFeedingIt_NeverSettles()
        {
            // A register into an inverter and back is a ring oscillator: it has no input to run out
            // of, so it runs until the level's tick limit and never settles. The verdict used to
            // call that "something feeds itself", which stopped being a diagnosis once a whole
            // chapter asked the player to build loops on purpose.
            var sim = new Simulation();
            var register = sim.Add(new RegisterNode { Name = "reg" });
            var not = sim.Add(new NotGate { Name = "not" });

            sim.Connect(register.Out(0), not.In(0), delay: 1);
            sim.Connect(not.Out(0), register.In(0), delay: 1);

            sim.Run(20);

            Assert.AreEqual(0, sim.CorruptedCount, "nothing collides; it simply never stops");
            Assert.Greater(InTransit(sim), 0, "the loop should still be carrying its bit");
        }

        private static int InTransit(Simulation sim)
        {
            int count = 0;

            for (int id = 0; id < sim.EdgeCount; id++)
            {
                Edge edge = sim.GetEdge(id);

                if (edge != null)
                    count += edge.InTransitCount;
            }

            return count;
        }

        // -----------------------------------------------------------------
        // The rule the whole simulator rests on
        // -----------------------------------------------------------------

        [Test]
        public void TheSameCircuit_BuiltInTheOppositeOrder_RunsTheSame()
        {
            // Registers fire on their own in the first tick, which is the one new way a node can
            // reach the evaluate phase. Order-independence has to survive it.
            SinkNode forwards = Forwards();
            SinkNode backwards = Backwards();

            Assert.AreEqual(2, forwards.Received.Count, "sanity: the circuit should produce two bits");
            CollectionAssert.AreEqual(forwards.Received, backwards.Received,
                "the order nodes were added in changed the result");
        }

        private static SinkNode Forwards()
        {
            var sim = new Simulation();
            var source = sim.Add(new SourceNode(new[] { Bit.One, Bit.Zero }) { Name = "in" });
            var register = sim.Add(new RegisterNode { Name = "reg" });
            var gate = sim.Add(new XorGate { Name = "xor" });
            var sink = sim.Add(new SinkNode { Name = "out" });

            Wire(sim, source, register, gate, sink);
            sim.Run(8);
            Assert.AreEqual(0, sim.CorruptedCount, "the forwards build collided");
            return sink;
        }

        private static SinkNode Backwards()
        {
            var sim = new Simulation();
            var sink = sim.Add(new SinkNode { Name = "out" });
            var gate = sim.Add(new XorGate { Name = "xor" });
            var register = sim.Add(new RegisterNode { Name = "reg" });
            var source = sim.Add(new SourceNode(new[] { Bit.One, Bit.Zero }) { Name = "in" });

            Wire(sim, source, register, gate, sink);
            sim.Run(8);
            Assert.AreEqual(0, sim.CorruptedCount, "the backwards build collided");
            return sink;
        }

        /// <summary>
        /// Compares each bit with the one before it: the shape every edge detector has.
        /// </summary>
        /// <remarks>
        /// Every wire is delay 1, including the one that skips the register, and that is the whole
        /// lesson of the shape. A register hands its bit on a clock **early** -- what leaves it on
        /// cycle k is what went in on cycle k-1 -- so the two paths meet with no padding at all.
        /// Padding the direct wire to 2, which looks like the obvious balancing, is what collides.
        /// </remarks>
        private static void Wire(Simulation sim, SourceNode source, RegisterNode register, XorGate gate, SinkNode sink)
        {
            sim.Connect(source.Out(0), register.In(0), delay: 1);
            sim.Connect(source.Out(0), gate.In(0), delay: 1);
            sim.Connect(register.Out(0), gate.In(1), delay: 1);
            sim.Connect(gate.Out(0), sink.In(0), delay: 1);
        }
    }
}
