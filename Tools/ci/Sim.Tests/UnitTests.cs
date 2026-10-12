using System;
using BitSorter.SystemVerilog;
using NUnit.Framework;

namespace BitSorter.Sim.Tests
{
    /// <summary>The engine's parts one at a time: resolution, the primitives, choosing a delay, and the builder's refusals.</summary>
    public class UnitTests
    {
        private static LogicVec B(char c) => LogicVec.FromBinary(c.ToString());

        // -----------------------------------------------------------------
        // Resolution
        // -----------------------------------------------------------------

        [TestCase('0', "0xx0")]
        [TestCase('1', "x1x1")]
        [TestCase('x', "xxxx")]
        [TestCase('z', "01xz")]
        public void TwoDriversOfAWire_ResolveByTheStandardsTable(char p, string againstZeroOneXZ)
        {
            const string order = "01xz";
            for (int q = 0; q < 4; q++)
            {
                LogicVec result = LogicVec.ResolveWire(B(p), B(order[q]));
                Assert.AreEqual(againstZeroOneXZ[q].ToString(), result.Format(Radix.Binary), $"{p} with {order[q]}");
            }
        }

        [Test]
        public void Resolution_GoesBitByBit_AcrossAWordBoundary()
        {
            // Bit 69: z gives way to 1. Bit 68: two 1s agree. Below: 0 against z, which gives way.
            LogicVec p = LogicVec.FromBinary("z1" + new string('0', 68));
            LogicVec q = LogicVec.FromBinary("11" + new string('z', 68));
            Assert.AreEqual("11" + new string('0', 68), LogicVec.ResolveWire(p, q).Format(Radix.Binary));
            Assert.AreEqual("x", LogicVec.ResolveWire(p, LogicVec.FromBinary("10" + new string('z', 68))).Slice(68, 1).Format(Radix.Binary),
                "1 against 0 is x");
        }

        // -----------------------------------------------------------------
        // Primitives
        // -----------------------------------------------------------------

        private static string Gate(GateKind kind, string inputs)
        {
            var values = new LogicVec[inputs.Length];
            var indices = new int[inputs.Length];
            for (int i = 0; i < inputs.Length; i++)
            {
                values[i] = B(inputs[i]);
                indices[i] = i;
            }

            return Gates.Evaluate(kind, values, indices).Format(Radix.Binary);
        }

        [TestCase(GateKind.And, "0x", "0")]
        [TestCase(GateKind.And, "1z", "x")]
        [TestCase(GateKind.And, "111", "1")]
        [TestCase(GateKind.Nand, "0x", "1")]
        [TestCase(GateKind.Or, "1x", "1")]
        [TestCase(GateKind.Nor, "00", "1")]
        [TestCase(GateKind.Xor, "101", "0")]
        [TestCase(GateKind.Xnor, "1x", "x")]
        [TestCase(GateKind.Buf, "z", "x")]
        [TestCase(GateKind.Not, "z", "x")]
        [TestCase(GateKind.Bufif1, "11", "1")]
        [TestCase(GateKind.Bufif1, "z1", "x")]
        [TestCase(GateKind.Bufif1, "10", "z")]
        [TestCase(GateKind.Bufif1, "1x", "x")]
        [TestCase(GateKind.Bufif0, "00", "0")]
        [TestCase(GateKind.Notif1, "01", "1")]
        [TestCase(GateKind.Notif0, "01", "z")]
        public void APrimitive_GivesWhatTheStandardSays(GateKind kind, string inputs, string output) =>
            Assert.AreEqual(output, Gate(kind, inputs));

        // -----------------------------------------------------------------
        // Choosing a delay
        // -----------------------------------------------------------------

        [TestCase('0', '1', 2)]
        [TestCase('1', '0', 5)]
        [TestCase('1', 'z', 7)]
        [TestCase('0', 'x', 2)]
        [TestCase('x', '0', 5)]
        public void AOneBitChange_TakesTheDelayForWhereItIsGoing(char from, char to, long expected) =>
            Assert.AreEqual(expected, Delay.Of(2, 5, 7).For(B(from), B(to)));

        [Test]
        public void WithoutATurnOffDelay_GoingToZ_TakesTheSmallerOfRiseAndFall() =>
            Assert.AreEqual(3, Delay.Of(4, 3).For(B('1'), B('z')));

        [TestCase("0101", "0000", 5)]
        [TestCase("0101", "0100", 2)]
        [TestCase("0000", "zzzz", 7)]
        [TestCase("zzzz", "1z00", 2)]
        [TestCase("xxxx", "0000", 5)]
        public void AVectorChange_TakesFallOnlyToAllZeros_AndTurnOffOnlyToAllZ(string from, string to, long expected) =>
            Assert.AreEqual(expected, Delay.Of(2, 5, 7).For(LogicVec.FromBinary(from), LogicVec.FromBinary(to)));

        [Test]
        public void ADelayCannotBeNegative() => Assert.Throws<ArgumentOutOfRangeException>(() => Delay.Of(-1));

        // -----------------------------------------------------------------
        // The builder's refusals
        // -----------------------------------------------------------------

        [Test]
        public void ASecondDriverOnAVariable_IsRefused_AndSaysToUseAWire()
        {
            var b = new NetlistBuilder();
            SignalId a = b.Variable("a"), y = b.Variable("y");
            b.Gate(GateKind.Buf, "g1", y, Delay.Zero, a);

            var e = Assert.Throws<InvalidOperationException>(() => b.Gate(GateKind.Not, "g2", y, Delay.Zero, a));
            StringAssert.Contains("wire", e.Message);
        }

        [Test]
        public void AGateTerminalWiderThanOneBit_IsRefused()
        {
            var b = new NetlistBuilder();
            SignalId a = b.Variable("a", 4), y = b.Net("y");
            Assert.Throws<ArgumentException>(() => b.Gate(GateKind.Buf, "g", y, Delay.Zero, a));
        }

        [Test]
        public void TheWrongNumberOfInputs_IsRefused()
        {
            var b = new NetlistBuilder();
            SignalId a = b.Variable("a"), y = b.Net("y");
            Assert.Throws<ArgumentException>(() => b.Gate(GateKind.And, "g", y, Delay.Zero, a));
            Assert.Throws<ArgumentException>(() => b.Gate(GateKind.Not, "g", y, Delay.Zero, a, a));
        }

        [Test]
        public void AStimulusWriteToADrivenSignal_IsRefused()
        {
            var b = new NetlistBuilder();
            SignalId a = b.Variable("a"), y = b.Net("y");
            b.Gate(GateKind.Buf, "g", y, Delay.Zero, a);
            Assert.Throws<ArgumentException>(() => new Simulator(b.Build(), new Stimulus().Set(0, y, Logic4.One)));
        }

        [Test]
        public void AnAssignment_IsSizedToItsTarget()
        {
            var b = new NetlistBuilder();
            SignalId a = b.Variable("a", 4, signed: true), wide = b.Net("wide", 8), narrow = b.Net("narrow", 2);
            b.Assign(wide, b.Read(a), Delay.Zero, signExtend: true);
            b.Assign(narrow, b.Read(a), Delay.Zero);

            var sim = new Simulator(b.Build(), new Stimulus().Set(0, a, LogicVec.FromBinary("1010", true)));
            sim.RunTo(1);

            Assert.AreEqual("11111010", sim.Value(wide).Format(Radix.Binary));
            Assert.AreEqual("10", sim.Value(narrow).Format(Radix.Binary));
        }
    }
}
