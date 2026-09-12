using NUnit.Framework;
using BitSorter.View;

namespace BitSorter.LogicCore.Tests
{
    /// <summary>
    /// <see cref="PortState"/>: what the board draws to say a gate is waiting, and that a collision
    /// is one tick away.
    /// </summary>
    /// <remarks>
    /// Built on real <see cref="Simulation"/> graphs rather than hand-made ports, the way
    /// <see cref="CorruptionSiteTests"/> is. The predictions here are only worth anything if they
    /// agree with what the tick loop actually does, so the tests run the tick loop and check the
    /// prediction against the outcome rather than against a second copy of the rule.
    /// </remarks>
    public class PortStateTests
    {
        /// <summary>
        /// Two sources into one AND, on wires of different length, so the fast one lands and waits.
        /// The shape every unbalanced-path level is made of.
        /// </summary>
        private static Simulation Unbalanced(Bit fast, Bit slow, int slowDelay, out Node gate)
        {
            var sim = new Simulation();
            var a = sim.Add(new SourceNode(new[] { fast }) { Name = "a" });
            var b = sim.Add(new SourceNode(new[] { slow }) { Name = "b" });
            gate = sim.Add(new AndGate { Name = "and" });
            var sink = sim.Add(new SinkNode { Name = "out" });

            sim.Connect(a.Out(0), gate.In(0), delay: 1);
            sim.Connect(b.Out(0), gate.In(1), delay: slowDelay);
            sim.Connect(gate.Out(0), sink.In(0), delay: 1);

            return sim;
        }

        // -----------------------------------------------------------------
        // IsStalled
        // -----------------------------------------------------------------

        [Test]
        public void NothingIsStalled_OnAnUntouchedBoard()
        {
            Unbalanced(Bit.One, Bit.One, slowDelay: 3, out Node gate);

            Assert.IsFalse(PortState.IsStalled(gate), "no bit has been delivered yet");
        }

        [Test]
        public void GateIsStalled_WhileOneInputWaitsForTheOther()
        {
            Simulation sim = Unbalanced(Bit.One, Bit.One, slowDelay: 3, out Node gate);

            // Tick 0 emits from both sources; the short wire delivers on tick 1.
            sim.Run(2);

            Assert.IsTrue(gate.In(0).IsOccupied, "the fast bit should have landed");
            Assert.IsFalse(gate.In(1).IsOccupied, "the slow bit should still be travelling");
            Assert.IsTrue(PortState.IsStalled(gate));
        }

        [Test]
        public void GateIsNotStalled_OnceItHasFired()
        {
            Simulation sim = Unbalanced(Bit.One, Bit.One, slowDelay: 3, out Node gate);

            // Long enough for the slow bit to arrive and the gate to consume both.
            sim.Run(5);

            Assert.IsFalse(gate.In(0).IsOccupied);
            Assert.IsFalse(gate.In(1).IsOccupied);
            Assert.IsFalse(PortState.IsStalled(gate));
        }

        [Test]
        public void BalancedPathsNeverStall()
        {
            Simulation sim = Unbalanced(Bit.One, Bit.Zero, slowDelay: 1, out Node gate);

            // Both bits share a delay, so the ports fill and empty inside one tick and the gate is
            // never caught holding one. Checked every tick rather than at the end, because a stall
            // that lasted a single tick would be exactly what this is meant to rule out.
            for (int i = 0; i < 6; i++)
            {
                sim.Tick();
                Assert.IsFalse(PortState.IsStalled(gate), $"stalled after tick {i}");
            }
        }

        [Test]
        public void SourcesAreNeverStalled()
        {
            var sim = new Simulation();
            var source = sim.Add(new SourceNode(new[] { Bit.One }) { Name = "a" });
            var sink = sim.Add(new SinkNode { Name = "out" });

            sim.Connect(source.Out(0), sink.In(0), delay: 1);
            sim.Run(3);

            Assert.IsFalse(PortState.IsStalled(source), "a node with no inputs is vacuously ready");
        }

        [Test]
        public void NullNodeIsNotStalled()
        {
            Assert.IsFalse(PortState.IsStalled(null));
        }

        // -----------------------------------------------------------------
        // WillCollide
        // -----------------------------------------------------------------

        /// <summary>
        /// Two sources into the same port on wires of different length: the first bit lands and
        /// waits, the second arrives later into the port it is still sitting in.
        /// </summary>
        /// <remarks>
        /// The target is a gate with its second input left unwired, not a sink. A sink has one
        /// input, so it is ready the moment a bit lands and consumes it in the same tick -- the bit
        /// would never be caught waiting. The gate can never fire, so its port keeps the bit for as
        /// long as the test needs it.
        /// </remarks>
        private static Simulation Converging(Bit first, Bit second, out Edge late)
        {
            var sim = new Simulation();
            var a = sim.Add(new SourceNode(new[] { first }) { Name = "a" });
            var b = sim.Add(new SourceNode(new[] { second }) { Name = "b" });
            var gate = sim.Add(new AndGate { Name = "and" });
            var sink = sim.Add(new SinkNode { Name = "out" });

            sim.Connect(a.Out(0), gate.In(0), delay: 1);
            late = sim.Connect(b.Out(0), gate.In(0), delay: 3);
            sim.Connect(gate.Out(0), sink.In(0), delay: 1);

            return sim;
        }

        [Test]
        public void NoCollisionForecast_WhileTheTargetPortIsEmpty()
        {
            Converging(Bit.One, Bit.Zero, out Edge late);

            Assert.IsFalse(PortState.WillCollide(late, out _), "nothing is waiting yet");
        }

        [Test]
        public void NoCollisionForecast_WhileTheBitIsMoreThanOneTickOut()
        {
            Simulation sim = Converging(Bit.One, Bit.Zero, out Edge late);

            // The fast bit has landed and is waiting, but the slow one is still two ticks away.
            sim.Run(2);

            Assert.IsTrue(late.Target.IsOccupied, "the first bit should be waiting");
            Assert.IsFalse(PortState.WillCollide(late, out _), "warning must not fire early");
        }

        [Test]
        public void ForecastsAMixedCollision_AndTheHeldBitDies()
        {
            Simulation sim = Converging(Bit.One, Bit.Zero, out Edge late);
            sim.Run(3);

            Assert.IsTrue(PortState.WillCollide(late, out bool heldBitDies));
            Assert.IsTrue(heldBitDies, "differing values destroy both bits");

            int before = sim.CorruptedCount;
            sim.Tick();

            Assert.AreEqual(before + 2, sim.CorruptedCount, "both bits should have been destroyed");
            Assert.IsFalse(late.Target.IsOccupied, "a mixed collision leaves no survivor");
        }

        [Test]
        public void ForecastsAMatchingCollision_AndTheHeldBitSurvives()
        {
            Simulation sim = Converging(Bit.One, Bit.One, out Edge late);
            sim.Run(3);

            Assert.IsTrue(PortState.WillCollide(late, out bool heldBitDies));
            Assert.IsFalse(heldBitDies, "matching values leave the port holding its value");

            int before = sim.CorruptedCount;
            sim.Tick();

            Assert.AreEqual(before + 1, sim.CorruptedCount, "only the arrival should be destroyed");
            Assert.IsTrue(late.Target.IsOccupied, "the waiting bit should have survived");
        }

        [Test]
        public void NullEdgeNeverCollides()
        {
            Assert.IsFalse(PortState.WillCollide(null, out bool heldBitDies));
            Assert.IsFalse(heldBitDies);
        }

        // -----------------------------------------------------------------
        // Showing a collision once
        // -----------------------------------------------------------------

        private static readonly PortAddress SomePort = new PortAddress(3, true, 1);

        /// <summary>
        /// One collision is one flash, however many frames look at it.
        /// </summary>
        /// <remarks>
        /// The view draws many frames per tick, so the same collision is looked at repeatedly
        /// before anything else can happen. Each look used to re-arm the flash, which was invisible
        /// while the clock kept moving and permanent as soon as it stopped -- see
        /// <see cref="ASettledRun_DoesNotFlashForever"/>.
        /// </remarks>
        [Test]
        public void OneCollision_IsNewsExactlyOnce()
        {
            var watch = new CollisionWatch();

            Assert.IsTrue(watch.IsNews(SomePort, collidedOnTick: 4, tickJustExecuted: 4),
                "the frame after a collision should flash it");

            for (int frame = 0; frame < 5; frame++)
            {
                Assert.IsFalse(watch.IsNews(SomePort, collidedOnTick: 4, tickJustExecuted: 4),
                    "the same collision was flashed again on a later frame of the same tick");
            }
        }

        /// <summary>
        /// A run that settles on a collision tick stops flashing.
        /// </summary>
        /// <remarks>
        /// The whole defect, in the shape it actually occurs. LevelSession.Settle stops the clock,
        /// so CurrentTick freezes and "the tick just executed" stops moving. A port whose last
        /// collision was on that very tick therefore satisfied the arming condition on every frame
        /// for the rest of the run: permanently swollen and red, and never repainted with what it
        /// was holding, because a port mid-flash is skipped by the resting pass.
        ///
        /// Reachable on any failing run whose last delivery is a mixed collision, which is the
        /// ordinary shape of the levels this mechanic teaches.
        /// </remarks>
        [Test]
        public void ASettledRun_DoesNotFlashForever()
        {
            var watch = new CollisionWatch();

            const int settledAt = 7;   // the clock stops here and never moves again

            Assert.IsTrue(watch.IsNews(SomePort, settledAt, settledAt), "the collision should flash once");

            int flashes = 0;

            // A couple of seconds of frames against a frozen clock.
            for (int frame = 0; frame < 120; frame++)
            {
                if (watch.IsNews(SomePort, settledAt, settledAt))
                    flashes++;
            }

            Assert.AreEqual(0, flashes,
                $"the flash was re-armed on {flashes} of 120 frames after the clock stopped, so the " +
                "port stays red and swollen until the board is rebuilt");
        }

        [Test]
        public void APortThatHasNeverCollided_IsNeverNews()
        {
            var watch = new CollisionWatch();

            Assert.IsFalse(watch.IsNews(SomePort, collidedOnTick: -1, tickJustExecuted: 0));
            Assert.IsFalse(watch.IsNews(SomePort, collidedOnTick: -1, tickJustExecuted: 9));
        }

        [Test]
        public void ASecondCollision_AtTheSamePort_IsNewsAgain()
        {
            // Two separate collisions are two separate events, and the second has to be shown.
            var watch = new CollisionWatch();

            Assert.IsTrue(watch.IsNews(SomePort, collidedOnTick: 2, tickJustExecuted: 2));
            Assert.IsFalse(watch.IsNews(SomePort, collidedOnTick: 2, tickJustExecuted: 2));

            Assert.IsTrue(watch.IsNews(SomePort, collidedOnTick: 5, tickJustExecuted: 5),
                "a later collision at the same port is a new event");
        }

        [Test]
        public void ClearingTheWatch_LetsTheSameTickFlashAgain()
        {
            // A rebuild replaces every port, and the next run starts its clock at zero again, so a
            // collision on the same tick number is a different collision.
            var watch = new CollisionWatch();

            Assert.IsTrue(watch.IsNews(SomePort, collidedOnTick: 1, tickJustExecuted: 1));
            Assert.IsFalse(watch.IsNews(SomePort, collidedOnTick: 1, tickJustExecuted: 1));

            watch.Clear();

            Assert.IsTrue(watch.IsNews(SomePort, collidedOnTick: 1, tickJustExecuted: 1),
                "after a rebuild the same tick number is a different run's collision");
        }

        [Test]
        public void TwoPorts_AreWatchedIndependently()
        {
            var watch = new CollisionWatch();
            var other = new PortAddress(3, true, 0);   // same node, the sibling port

            Assert.IsTrue(watch.IsNews(SomePort, collidedOnTick: 3, tickJustExecuted: 3));

            Assert.IsTrue(watch.IsNews(other, collidedOnTick: 3, tickJustExecuted: 3),
                "one port being shown must not swallow another port's collision on the same tick");
        }
    }
}
