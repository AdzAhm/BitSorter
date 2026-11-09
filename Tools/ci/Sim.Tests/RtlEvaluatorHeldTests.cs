using System;
using System.Linq;
using BitSorter.SystemVerilog;
using BitSorter.SystemVerilog.Syntax;
using NUnit.Framework;

namespace BitSorter.Sim.Tests
{
    /// <summary>
    /// <see cref="RtlEvaluator"/> with one net held at a value whatever drives it (the review of the 16d fixes): how
    /// <c>ControllerCheck</c> tries a decoder on a signal the module works out itself at each of its rows, however seldom the
    /// module's inputs bring the signal there.
    /// </summary>
    public class RtlEvaluatorHeldTests
    {
        private const string Chain = @"
module chain (input logic [1:0] a, output logic [1:0] k, output logic [3:0] y);
    assign k = ~a;
    assign y = {k, k};
endmodule
";

        private static (Design Design, RtlNetlist Netlist, LogicVec[] Signals) Read()
        {
            SyntaxTree tree = SyntaxReader.ReadText(Chain, "chain.sv");
            Assert.IsEmpty(tree.Diagnostics, "the read reported:\n" + string.Join("\n", tree.Diagnostics));
            Elaboration e = Elaborator.Elaborate(tree, "chain");
            Assert.IsEmpty(e.Diagnostics, "the elaboration reported:\n" + string.Join("\n", e.Diagnostics));
            Design design = e.Design;
            RtlNetlist netlist = RtlExtractor.Of(design, design.Scopes.Single(s => s.Kind == ScopeKind.Top).Path);
            Assert.IsNotNull(netlist, "sanity: the module's netlist");

            var signals = new LogicVec[design.Signals.Count];
            for (int i = 0; i < signals.Length; i++)
                signals[i] = LogicVec.AllX(design.Signals[i].Width);
            signals[SignalOf(design, "a")] = LogicVec.FromUInt64(0, 2);   // so k works out as 3
            return (design, netlist, signals);
        }

        private static int SignalOf(Design design, string name)
        {
            for (int i = 0; i < design.Signals.Count; i++)
                if (design.Signals[i].Name == name || design.Signals[i].Name.EndsWith("." + name, StringComparison.Ordinal))
                    return i;
            Assert.Fail("no signal " + name);
            return -1;
        }

        /// <summary>The net something drives that is <paramref name="name"/>'s whole value.</summary>
        private static int NetOf(Design design, RtlNetlist netlist, string name)
        {
            int signal = SignalOf(design, name);
            return netlist.Nets.Single(n => n.Signal == signal && n.Driver >= 0).Id;
        }

        [Test]
        public void ANetHeld_IsReadAtTheValueHeld_ByEveryCellReadingIt_WhateverDrivesIt()
        {
            (Design design, RtlNetlist netlist, LogicVec[] signals) = Read();
            int k = NetOf(design, netlist, "k"), y = NetOf(design, netlist, "y");
            var values = new LogicVec[netlist.Nets.Count];

            RtlEvaluator.Evaluate(netlist, signals, values);
            Assert.AreEqual("11", values[k].Format(Radix.Binary), "sanity: k is ~a");
            Assert.AreEqual("1111", values[y].Format(Radix.Binary), "sanity: y is {k, k}");

            RtlEvaluator.Evaluate(netlist, signals, values, k, LogicVec.FromUInt64(1, 2));
            Assert.AreEqual("01", values[k].Format(Radix.Binary), "k held at 1, whatever ~a gives");
            Assert.AreEqual("0101", values[y].Format(Radix.Binary), "y reads the k held");

            RtlEvaluator.Evaluate(netlist, signals, values, -1, default);
            Assert.AreEqual("1111", values[y].Format(Radix.Binary), "-1 holds none");
        }

        [Test]
        public void ANetHeld_AtAWidthNotItsOwn_OrPastTheNets_IsRefused()
        {
            (Design design, RtlNetlist netlist, LogicVec[] signals) = Read();
            int k = NetOf(design, netlist, "k");
            var values = new LogicVec[netlist.Nets.Count];
            Assert.Throws<ArgumentException>(() => RtlEvaluator.Evaluate(netlist, signals, values, k, LogicVec.FromUInt64(1, 3)));
            Assert.Throws<ArgumentOutOfRangeException>(() => RtlEvaluator.Evaluate(netlist, signals, values, netlist.Nets.Count, LogicVec.FromUInt64(1, 2)));
        }
    }
}
