using System;
using BitSorter.SystemVerilog;
using NUnit.Framework;

namespace BitSorter.Sim.Tests
{
    /// <summary>
    /// Drivers of part of a signal: <c>assign y[3] = a;</c>, a gate whose output is one bit of a
    /// vector, and a module output connected to part of one -- what the course writes in its
    /// exam questions and what flattening a hierarchy produces.
    /// </summary>
    public class SliceDriverTests
    {
        private static LogicVec B(string bits) => LogicVec.FromBinary(bits);

        private static string At(Simulator sim, SignalId signal) => sim.Value(signal).Format(Radix.Binary);

        [Test]
        public void SlicesOfAWire_EachDriveTheirOwnBits_AndTheRestIsUndriven()
        {
            var b = new NetlistBuilder();
            SignalId a = b.Variable("a"), c = b.Variable("c"), y = b.Net("y", 4);
            b.AssignSlice(y, 0, 1, b.Read(a), Delay.Of(1));
            b.AssignSlice(y, 2, 1, b.Read(c), Delay.Of(2));

            var sim = new Simulator(b.Build(), new Stimulus().Set(0, a, Logic4.One).Set(0, c, Logic4.Zero));

            sim.RunTo(0);
            Assert.AreEqual("zxzx", At(sim, y), "before their delays, each driven bit is x and the others are z");
            sim.RunTo(1);
            Assert.AreEqual("zxz1", At(sim, y));
            sim.RunTo(2);
            Assert.AreEqual("z0z1", At(sim, y));
        }

        [Test]
        public void SlicesOfAVariable_Compose_AndABitNoDriverReachesIsX()
        {
            var b = new NetlistBuilder();
            SignalId lo = b.Variable("lo", 2), hi = b.Variable("hi"), z = b.Variable("z", 4);
            b.AssignSlice(z, 0, 2, b.Read(lo), Delay.Zero);
            b.AssignSlice(z, 3, 1, b.Read(hi), Delay.Zero);

            var sim = new Simulator(b.Build(), new Stimulus().Set(0, lo, B("10")).Set(0, hi, Logic4.One));
            sim.RunTo(0);

            Assert.AreEqual("1x10", At(sim, z), "assign z[3] = hi; assign z[1:0] = lo; and nothing drives z[2]");
        }

        [Test]
        public void OverlappingSlicesOfAVariable_AreRefused_AndSaySoByBit()
        {
            var b = new NetlistBuilder();
            SignalId a = b.Variable("a", 2), z = b.Variable("z", 4);
            b.AssignSlice(z, 0, 2, b.Read(a), Delay.Zero);

            var e = Assert.Throws<InvalidOperationException>(() => b.AssignSlice(z, 1, 2, b.Read(a), Delay.Zero));
            StringAssert.Contains("bit 1", e.Message);
            StringAssert.Contains("wire", e.Message, "and it says the way out");
        }

        [Test]
        public void OverlappingSlicesOfAWire_Resolve_BitByBit()
        {
            var b = new NetlistBuilder();
            SignalId p = b.Variable("p", 2), q = b.Variable("q", 2), w = b.Net("w", 3);
            b.AssignSlice(w, 0, 2, b.Read(p), Delay.Zero);
            b.AssignSlice(w, 1, 2, b.Read(q), Delay.Zero);

            var sim = new Simulator(b.Build(), new Stimulus().Set(0, p, B("01")).Set(0, q, B("10")));
            sim.RunTo(0);

            // w[0] = p[0] = 1; w[1] = p[1] = 0 against q[0] = 0, agreeing; w[2] = q[1] = 1.
            Assert.AreEqual("101", At(sim, w));
        }

        [Test]
        public void AGate_ReadsBitsOfVectors_AndDrivesOneBitOfAnother()
        {
            var b = new NetlistBuilder();
            SignalId bus = b.Variable("bus", 2), sel = b.Variable("sel"), y = b.Variable("y", 3);
            b.Gate(GateKind.And, "a1", y, 2, Delay.Of(2), Expr.Slice(b.Read(bus), 0, 1), b.Read(sel));

            var sim = new Simulator(b.Build(), new Stimulus().Set(0, bus, B("01")).Set(0, sel, Logic4.One).Set(5, bus, B("10")));

            sim.RunTo(2);
            Assert.AreEqual("1xx", At(sim, y), "and a1 (y[2], bus[0], sel): 1 after its delay; y[1:0] have no driver");
            sim.RunTo(7);
            Assert.AreEqual("0xx", At(sim, y), "bus[0] fell at 5");
        }

        [Test]
        public void ASliceDriver_StillHasInertialDelay()
        {
            var b = new NetlistBuilder();
            SignalId a = b.Variable("a"), w = b.Net("w", 2);
            b.AssignSlice(w, 1, 1, b.Read(a), Delay.Of(3));

            var sim = new Simulator(b.Build(), new Stimulus().Set(0, a, Logic4.Zero).Set(10, a, Logic4.One).Set(11, a, Logic4.Zero));
            sim.RunTo(20);

            Assert.AreEqual("0z", At(sim, w), "a 1-unit pulse into a 3-unit delay is swallowed");

            bool swallowed = false;
            for (int i = 0; i < sim.History.TransitionCount; i++)
                swallowed |= sim.History.TransitionAt(i).Outcome == TransitionOutcome.Swallowed;
            Assert.IsTrue(swallowed, "and the history says so");
        }

        [Test]
        public void ASliceOutsideItsTarget_IsRefused()
        {
            var b = new NetlistBuilder();
            SignalId a = b.Variable("a"), w = b.Net("w", 2);
            Assert.Throws<ArgumentOutOfRangeException>(() => b.AssignSlice(w, 2, 1, b.Read(a), Delay.Zero));
        }

        [Test]
        public void AGateInputWiderThanOneBit_IsRefused()
        {
            var b = new NetlistBuilder();
            SignalId bus = b.Variable("bus", 2), y = b.Net("y");
            Assert.Throws<ArgumentException>(() => b.Gate(GateKind.Not, "n", y, 0, Delay.Zero, b.Read(bus)));
        }
    }
}
