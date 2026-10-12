using System.Collections.Generic;
using BitSorter.SystemVerilog;
using NUnit.Framework;

namespace BitSorter.Sim.Tests
{
    /// <summary>
    /// Hand-computed waveforms: the engine's output, change by change, for circuits whose answer is
    /// known -- worked out from the standard, and for the probes, printed by Icarus as well.
    /// </summary>
    public class WaveformTests
    {
        private static IEnumerable<string> ProbeOutputs => Probes.Expected.Keys;

        [TestCaseSource(nameof(ProbeOutputs))]
        public void AProbe_SettlesExactlyAsIcarusPrinted(string output)
        {
            (Design design, Stimulus stimulus) = Probes.Build();
            var sim = new Simulator(design, stimulus);

            Assert.AreEqual(RunStatus.ReachedTime, sim.RunTo(40).Status);
            Assert.That(Probes.Settled(sim.History, design.Find(output)), Is.EqualTo(Probes.Expected[output]));
        }

        [Test]
        public void TheReferenceGlitch_IsAPulseThatTravelsAndArrives()
        {
            // The look's reference circuit: y = a AND NOT a, NOT 3, AND 2, a rising at 10. The AND sees
            // a = 1 before NOT a falls, so a 3-unit pulse appears at its output, from 12 to 15.
            (Design design, Stimulus stimulus) = Probes.Build();
            var sim = new Simulator(design, stimulus);
            sim.RunTo(40);

            SignalId y9 = design.Find("y9");
            var rises = new List<Transition>();
            for (int i = 0; i < sim.History.TransitionCount; i++)
            {
                Transition t = sim.History.TransitionAt(i);
                if (t.Target.Equals(y9) && t.ScheduledAt >= 10)
                    rises.Add(t);
            }

            Assert.AreEqual(2, rises.Count, "two changes leave the AND: up, then down");
            Assert.AreEqual((10L, 12L, TransitionOutcome.Applied), (rises[0].ScheduledAt, rises[0].DueAt, rises[0].Outcome));
            Assert.AreEqual((13L, 15L, TransitionOutcome.Applied), (rises[1].ScheduledAt, rises[1].DueAt, rises[1].Outcome));
        }

        [Test]
        public void ASwallowedPulse_IsRecordedAsSwallowed_WhereItWasCancelled()
        {
            // P1: the AND's output starts towards 1 at 10, due at 13, and is called back at 11.
            (Design design, Stimulus stimulus) = Probes.Build();
            var sim = new Simulator(design, stimulus);
            sim.RunTo(40);

            SignalId y1 = design.Find("y1");
            for (int i = 0; i < sim.History.TransitionCount; i++)
            {
                Transition t = sim.History.TransitionAt(i);
                if (!t.Target.Equals(y1) || t.ScheduledAt != 10)
                    continue;

                Assert.AreEqual(13, t.DueAt);
                Assert.AreEqual(TransitionOutcome.Swallowed, t.Outcome);
                Assert.AreEqual(11, t.EndedAt);
                return;
            }

            Assert.Fail("no transition was scheduled at 10 for y1");
        }

        [Test]
        public void AHalfAdder_WithRiseAndFallDelays()
        {
            // xor #(2, 3) and and #(1, 4), a and b stepping through 00, 01, 10, 11 every 20.
            var b = new NetlistBuilder("ha");
            SignalId a = b.Variable("a"), bb = b.Variable("b"), sum = b.Variable("sum"), carry = b.Variable("carry");
            b.Gate(GateKind.Xor, "g1", sum, Delay.Of(2, 3), a, bb);
            b.Gate(GateKind.And, "g2", carry, Delay.Of(1, 4), a, bb);

            LogicVec Bit(int v) => LogicVec.FromUInt64((ulong)v, 1);
            var s = new Stimulus();
            for (int v = 0; v < 4; v++)
                s.Set(20 * v, a, Bit(v >> 1)).Set(20 * v, bb, Bit(v & 1));

            var sim = new Simulator(b.Build(), s);
            sim.RunTo(100);

            Assert.That(Probes.Settled(sim.History, sum), Is.EqualTo(new[] { "0 x", "3 0", "22 1", "63 0" }));
            Assert.That(Probes.Settled(sim.History, carry), Is.EqualTo(new[] { "0 x", "4 0", "61 1" }));
        }

        [Test]
        public void AVectorAssignment_ChoosesItsDelayByTheWholeValue_AsTheStandardSays()
        {
            // Icarus departs from this (Docs/oracle-notes.md): nonzero to nonzero takes the rise delay
            // here, where Icarus took the fall for 0101 -> 0100 and for zzzz -> 1z00.
            var b = new NetlistBuilder("vector");
            SignalId a = b.Variable("a", 4), y = b.Net("y", 4);
            b.Assign(y, b.Read(a), Delay.Of(2, 5, 7));

            var s = new Stimulus();
            string[] steps = { "0000", "0101", "0100", "0110", "0000", "0x00", "zzzz", "1z00" };
            for (int i = 0; i < steps.Length; i++)
                s.Set(i == 0 ? 0 : 10 + 20 * (i - 1), a, LogicVec.FromBinary(steps[i]));

            var sim = new Simulator(b.Build(), s);
            sim.RunTo(160);

            Assert.That(Probes.Settled(sim.History, y), Is.EqualTo(new[]
            {
                "0 xxxx", "5 0000", "12 0101", "32 0100", "52 0110", "75 0000", "92 0x00", "117 zzzz", "132 1z00",
            }));
        }

        [Test]
        public void TwoDriversOnAWire_DisagreeingReadX_AndAZDriverGivesWay()
        {
            var b = new NetlistBuilder("two");
            SignalId p = b.Variable("p"), q = b.Variable("q"), w = b.Net("w");
            b.Assign(w, b.Read(p), Delay.Zero);
            b.Assign(w, b.Read(q), Delay.Zero);

            var s = new Stimulus()
                .Set(0, p, Logic4.Zero).Set(0, q, Logic4.One)
                .Set(5, p, Logic4.Z)
                .Set(10, q, Logic4.Z);

            var sim = new Simulator(b.Build(), s);
            sim.RunTo(20);

            Assert.That(Probes.Settled(sim.History, w), Is.EqualTo(new[] { "0 x", "5 1", "10 z" }));
        }

        [Test]
        public void ZeroDelayLogic_SettlesWithinTheTimeStep()
        {
            // A chain of four zero-delay inverters: the end changes in the same time step, a few deltas on.
            var b = new NetlistBuilder("chain");
            SignalId a = b.Variable("a");
            SignalId previous = a;
            for (int i = 0; i < 4; i++)
            {
                SignalId next = b.Net($"n{i}");
                b.Gate(GateKind.Not, $"g{i}", next, Delay.Zero, previous);
                previous = next;
            }

            var sim = new Simulator(b.Build(), new Stimulus().Set(0, a, Logic4.Zero).Set(7, a, Logic4.One));
            sim.RunTo(10);

            Assert.That(Probes.Settled(sim.History, previous), Is.EqualTo(new[] { "0 0", "7 1" }));
            Assert.Greater(sim.History.Change(previous, sim.History.ChangeCount(previous) - 1).Delta, 0,
                "the change came through delta cycles");
        }
    }
}
