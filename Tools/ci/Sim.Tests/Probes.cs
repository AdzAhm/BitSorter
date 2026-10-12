using System.Collections.Generic;
using BitSorter.SystemVerilog;

namespace BitSorter.Sim.Tests
{
    /// <summary>
    /// The delay probes, built in C#: the same circuits as Tools/ci/Oracle/corpus/delays/probes.sv,
    /// signal for signal, with the change lists Icarus printed for them (2026-10-12) and the
    /// standard predicts. The engine's tests hold it to these lists; the oracle runs the corpus file
    /// under Icarus and compares the two VCDs.
    /// </summary>
    internal static class Probes
    {
        public const string Module = "probes";

        /// <summary>Each probe's output, and the values it settles on: time 0's first, as "time value".</summary>
        public static readonly Dictionary<string, string[]> Expected = new Dictionary<string, string[]>
        {
            ["y1"] = new[] { "0 x", "3 0" },                          // a 1-unit pulse into AND #3: swallowed
            ["y2"] = new[] { "0 x", "3 0", "13 1", "16 0" },          // a pulse as long as the delay: passes
            ["y3"] = new[] { "0 x", "1 0" },                          // rise 5, fall 1, 2-unit high pulse: cancelled
            ["y4"] = new[] { "0 x", "1 1" },                          // rise 1, fall 5, 2-unit low pulse: cancelled
            ["y5"] = new[] { "0 x", "5 0", "12 x" },                  // to x takes the smaller delay
            ["y6"] = new[] { "0 x", "4 0", "15 x" },                  // a pending 1 replaced by x
            ["y7"] = new[] { "0 x", "2 1", "14 z" },                  // bufif1 turn-off
            ["y8"] = new[] { "0 x", "3 0" },                          // assign #3 swallows the pulse too
            ["y9"] = new[] { "0 x", "2 0", "12 1", "15 0" },          // the reference glitch: NOT 3, AND 2
            ["y10"] = new[] { "0 x", "2 0" },                         // NOT 1, AND 2: the glitch swallowed
            ["y11"] = new[] { "0 x", "4 0" },                         // a gate output declared logic
        };

        public static (Design Design, Stimulus Stimulus) Build()
        {
            var b = new NetlistBuilder(Module);
            var s = new Stimulus();
            LogicVec Zero = LogicVec.Fill(Logic4.Zero, 1), One = LogicVec.Fill(Logic4.One, 1), X = LogicVec.Fill(Logic4.X, 1);

            // P1
            SignalId a1 = b.Variable("a1"), b1 = b.Variable("b1"), y1 = b.Net("y1");
            b.Gate(GateKind.And, "g1", y1, Delay.Of(3), a1, b1);
            s.Set(0, a1, Zero).Set(0, b1, One).Set(10, a1, One).Set(11, a1, Zero);

            // P2
            SignalId a2 = b.Variable("a2"), y2 = b.Net("y2");
            b.Gate(GateKind.Buf, "g2", y2, Delay.Of(3), a2);
            s.Set(0, a2, Zero).Set(10, a2, One).Set(13, a2, Zero);

            // P3
            SignalId a3 = b.Variable("a3"), y3 = b.Net("y3");
            b.Gate(GateKind.Buf, "g3", y3, Delay.Of(5, 1), a3);
            s.Set(0, a3, Zero).Set(10, a3, One).Set(12, a3, Zero);

            // P4
            SignalId a4 = b.Variable("a4"), y4 = b.Net("y4");
            b.Gate(GateKind.Buf, "g4", y4, Delay.Of(1, 5), a4);
            s.Set(0, a4, One).Set(10, a4, Zero).Set(12, a4, One);

            // P5
            SignalId a5 = b.Variable("a5"), y5 = b.Net("y5");
            b.Gate(GateKind.Buf, "g5", y5, Delay.Of(2, 5), a5);
            s.Set(0, a5, Zero).Set(10, a5, X);

            // P6
            SignalId a6 = b.Variable("a6"), y6 = b.Net("y6");
            b.Gate(GateKind.Buf, "g6", y6, Delay.Of(4), a6);
            s.Set(0, a6, Zero).Set(10, a6, One).Set(11, a6, X);

            // P7
            SignalId d7 = b.Variable("d7"), e7 = b.Variable("e7"), y7 = b.Net("y7");
            b.Gate(GateKind.Bufif1, "g7", y7, Delay.Of(2, 3, 4), d7, e7);
            s.Set(0, d7, One).Set(0, e7, One).Set(10, e7, Zero);

            // P8
            SignalId a8 = b.Variable("a8"), b8 = b.Variable("b8"), y8 = b.Net("y8");
            b.Assign(y8, Expr.Apply(BinaryOp.And, b.Read(a8), b.Read(b8)), Delay.Of(3));
            s.Set(0, a8, Zero).Set(0, b8, One).Set(10, a8, One).Set(11, a8, Zero);

            // P9
            SignalId a9 = b.Variable("a9"), n9 = b.Net("n9"), y9 = b.Net("y9");
            b.Gate(GateKind.Not, "g9n", n9, Delay.Of(3), a9);
            b.Gate(GateKind.And, "g9a", y9, Delay.Of(2), a9, n9);
            s.Set(0, a9, Zero).Set(10, a9, One);

            // P10
            SignalId a10 = b.Variable("a10"), n10 = b.Net("n10"), y10 = b.Net("y10");
            b.Gate(GateKind.Not, "g10n", n10, Delay.Of(1), a10);
            b.Gate(GateKind.And, "g10a", y10, Delay.Of(2), a10, n10);
            s.Set(0, a10, Zero).Set(10, a10, One);

            // P11
            SignalId a11 = b.Variable("a11"), b11 = b.Variable("b11"), y11 = b.Variable("y11");
            b.Gate(GateKind.Xor, "g11", y11, Delay.Of(2, 3), a11, b11);
            s.Set(1, a11, Zero).Set(1, b11, Zero);

            return (b.Build(), s);
        }

        /// <summary>A signal's settled changes as "time value" lines, to compare with <see cref="Expected"/>.</summary>
        public static string[] Settled(History history, SignalId signal)
        {
            var lines = new List<string>();
            foreach (ValueChange change in history.SettledChanges(signal))
                lines.Add($"{change.Time} {change.Value.Format(Radix.Binary)}");
            return lines.ToArray();
        }
    }
}
