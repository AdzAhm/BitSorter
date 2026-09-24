using NUnit.Framework;
using BitSorter.View;

namespace BitSorter.LogicCore.Tests
{
    /// <summary>
    /// <see cref="HintRules"/> and <see cref="StallClock"/>: when a first-time hint has been earned.
    /// </summary>
    /// <remarks>
    /// The stall trigger is the one worth testing hard. Firing it on the first stalled frame would
    /// teach a player about a mistake on a circuit that is working correctly, since a gate whose two
    /// inputs are a tick apart is briefly stalled on the way to firing.
    /// </remarks>
    public class FirstTimeHintsTests
    {
        /// <summary>
        /// Two sources into one AND on wires of the given lengths. A gap of one is an ordinary
        /// imbalance; leaving the second unwired stalls the gate for good.
        /// </summary>
        private static Simulation Gate(int firstDelay, int secondDelay, out Node gate)
        {
            var sim = new Simulation();
            var a = sim.Add(new SourceNode(new[] { Bit.One }) { Name = "a" });
            var b = sim.Add(new SourceNode(new[] { Bit.One }) { Name = "b" });
            gate = sim.Add(new AndGate { Name = "and" });
            var sink = sim.Add(new SinkNode { Name = "out" });

            sim.Connect(a.Out(0), gate.In(0), firstDelay);

            if (secondDelay > 0)
                sim.Connect(b.Out(0), gate.In(1), secondDelay);

            sim.Connect(gate.Out(0), sink.In(0), 1);
            return sim;
        }

        // -----------------------------------------------------------------
        // StallEarnsAHint
        // -----------------------------------------------------------------

        [Test]
        public void NothingStalled_NeverEarnsAHint()
        {
            Assert.IsFalse(HintRules.StallEarnsAHint(
                settled: true, anyStalled: false, longestStallTicks: 99, threshold: 4));
        }

        [Test]
        public void ABriefStallMidRun_DoesNotEarnAHint()
        {
            // One tick of waiting is what an ordinary unbalanced pair looks like on the way to
            // firing. Explaining it would be explaining a circuit that works.
            Assert.IsFalse(HintRules.StallEarnsAHint(
                settled: false, anyStalled: true, longestStallTicks: 1, threshold: 4));
        }

        [Test]
        public void ALongStallMidRun_EarnsAHint()
        {
            Assert.IsTrue(HintRules.StallEarnsAHint(
                settled: false, anyStalled: true, longestStallTicks: 4, threshold: 4));
        }

        [Test]
        public void AStallThatSurvivesTheRun_EarnsAHintWithNoWaiting()
        {
            // Once the board is idle nothing further can arrive, so a gate still holding is stuck
            // for good and there is nothing to wait and see about.
            Assert.IsTrue(HintRules.StallEarnsAHint(
                settled: true, anyStalled: true, longestStallTicks: 0, threshold: 4));
        }

        // -----------------------------------------------------------------
        // StallClock
        // -----------------------------------------------------------------

        [Test]
        public void TheClockStaysAtZero_WhilePathsAreBalanced()
        {
            Simulation sim = Gate(1, 1, out Node _);
            var clock = new StallClock();

            for (int i = 0; i < 8; i++)
            {
                sim.Tick();
                int longest = clock.Observe(sim.View, sim.View.CurrentTick);

                Assert.AreEqual(0, longest, $"stalled after tick {i} on balanced paths");
                Assert.IsFalse(clock.AnyStalled);
            }
        }

        [Test]
        public void AOneTickImbalance_NeverReachesTheThreshold()
        {
            Simulation sim = Gate(1, 2, out Node _);
            var clock = new StallClock();
            int worst = 0;

            for (int i = 0; i < 10; i++)
            {
                sim.Tick();
                int longest = clock.Observe(sim.View, sim.View.CurrentTick);

                if (longest > worst)
                    worst = longest;
            }

            Assert.Less(worst, HintRules.StallTicks,
                "a circuit that works should never look like one that is stuck");
        }

        [Test]
        public void AGateThatCanNeverFire_PassesTheThreshold()
        {
            Simulation sim = Gate(1, 0, out Node gate);   // second input never wired
            var clock = new StallClock();
            int longest = 0;

            for (int i = 0; i < 12; i++)
            {
                sim.Tick();
                longest = clock.Observe(sim.View, sim.View.CurrentTick);
            }

            Assert.IsTrue(PortState.IsStalled(gate));
            Assert.IsTrue(clock.AnyStalled);
            Assert.GreaterOrEqual(longest, HintRules.StallTicks);
        }

        [Test]
        public void AStallThatResolves_DoesNotCountTowardsTheNextOne()
        {
            // The short wire fills input 0 and the gate waits; the long one arrives and it fires;
            // then the long wire's second bit leaves it waiting again. Two separate waits with the
            // gate running in between, which is the case the per-node clock exists for.
            var sim = new Simulation();
            var a = sim.Add(new SourceNode(new[] { Bit.One }) { Name = "a" });
            var b = sim.Add(new SourceNode(new[] { Bit.One, Bit.One }) { Name = "b" });
            var gate = sim.Add(new AndGate { Name = "and" });
            var sink = sim.Add(new SinkNode { Name = "out" });

            sim.Connect(a.Out(0), gate.In(0), 1);
            sim.Connect(b.Out(0), gate.In(1), 2);
            sim.Connect(gate.Out(0), sink.In(0), 1);

            var clock = new StallClock();
            int firstStallTick = -1;
            int longest = 0;
            bool recovered = false;

            for (int i = 0; i < 10; i++)
            {
                sim.Tick();
                int tick = sim.View.CurrentTick;
                longest = clock.Observe(sim.View, tick);

                if (clock.AnyStalled)
                {
                    if (firstStallTick < 0)
                        firstStallTick = tick;
                }
                else if (firstStallTick >= 0)
                {
                    recovered = true;
                }
            }

            Assert.GreaterOrEqual(firstStallTick, 0, "the gate should have waited at least once");
            Assert.IsTrue(recovered, "and should have fired in between");

            // What the age would be had the two waits been run together.
            int accumulated = sim.View.CurrentTick - firstStallTick;

            Assert.Less(longest, accumulated,
                "the second wait must be counted from when it began, not from the first one");
        }

        [Test]
        public void ClearForgetsEverything()
        {
            Simulation sim = Gate(1, 0, out Node _);
            var clock = new StallClock();

            for (int i = 0; i < 6; i++)
            {
                sim.Tick();
                clock.Observe(sim.View, sim.View.CurrentTick);
            }

            Assert.IsTrue(clock.AnyStalled);

            clock.Clear();
            Assert.IsFalse(clock.AnyStalled, "a new level starts with no stall history");
        }

        // -----------------------------------------------------------------
        // Text
        // -----------------------------------------------------------------

        [Test]
        public void EveryHint_HasText()
        {
            foreach (string id in HintRules.All)
            {
                Assert.IsFalse(string.IsNullOrWhiteSpace(HintRules.TextFor(id)),
                    $"'{id}' has no text");
            }
        }

        /// <summary>
        /// Every first-time hint fits inside the banner that shows it.
        /// </summary>
        /// <remarks>
        /// The goals are measured against the status banner and the level hints against the help
        /// panel; these were measured against nothing, in a strip 46 pixels tall. A hint one line
        /// longer would print over the rows above and below it, and it is the one text the player
        /// sees once ever.
        /// </remarks>
        [Test]
        public void EveryHint_FitsItsBanner()
        {
            foreach (string id in HintRules.All)
            {
                float needed = UiTheme.TextHeight(HintRules.TextFor(id), HintBanner.TextType, HintBanner.TextWidth);

                Assert.LessOrEqual(needed, UiTheme.HintHeight,
                    $"'{id}' wraps to {needed:F0}px and its banner is {UiTheme.HintHeight}px");
            }
        }

        [Test]
        public void AnUnknownId_HasNoText()
        {
            Assert.IsNull(HintRules.TextFor("not-a-hint"));
        }
    }
}
