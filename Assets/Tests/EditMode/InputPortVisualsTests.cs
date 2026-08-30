using NUnit.Framework;
using UnityEngine;
using BitSorter.LogicCore;
using BitSorter.View;

namespace BitSorter.LogicCore.Tests
{
    public class InputPortVisualsTests
    {
        [Test]
        public void AHalfFilledTwoInputGateCountsAsWaiting()
        {
            var simulation = new Simulation();

            var source = new SourceNode(new[] { Bit.One });
            var gate = new AndGate();

            simulation.AddNode(source);
            simulation.AddNode(gate);
            simulation.AddEdge(new Edge(source.Out(0), gate.In(0), 1));

            simulation.Tick();
            simulation.Tick();

            Assert.IsTrue(gate.In(0).Pending.HasValue);
            Assert.IsFalse(gate.In(1).Pending.HasValue);
            Assert.IsTrue(InputPortVisuals.IsWaiting(gate.In(0)));
        }

        [Test]
        public void AnEmptyPortIsNeverWaiting()
        {
            var gate = new AndGate();
            Assert.IsFalse(InputPortVisuals.IsWaiting(gate.In(0)));
        }

        [Test]
        public void PulseStaysWithinZeroToOne()
        {
            foreach (float time in new[] { 0f, 0.1f, 0.35f, 0.7f, 1.4f })
            {
                float pulse = InputPortVisuals.Pulse01(time, 0.7f);
                Assert.GreaterOrEqual(pulse, 0f);
                Assert.LessOrEqual(pulse, 1f);
            }
        }

        [Test]
        public void PulseRepeatsEachCycle()
        {
            float a = InputPortVisuals.Pulse01(0.12f, 0.7f);
            float b = InputPortVisuals.Pulse01(0.82f, 0.7f);
            Assert.AreEqual(a, b, 0.0001f);
        }

        [Test]
        public void WaitingColourUsesBitValue()
        {
            Color zero = new Color(0.2f, 0.3f, 0.4f);
            Color one = new Color(0.8f, 0.9f, 0.1f);

            Color zeroColour = InputPortVisuals.WaitingColour(Bit.Zero, zero, one, 0f);
            Color oneColour = InputPortVisuals.WaitingColour(Bit.One, zero, one, 0f);

            Assert.AreEqual(zero, zeroColour);
            Assert.AreEqual(one, oneColour);
        }
    }
}
