using System.IO;
using System.Text.RegularExpressions;
using BitSorter.SystemVerilog;
using BitSorter.SystemVerilog.Syntax;
using NUnit.Framework;

namespace BitSorter.Sim.Tests
{
    /// <summary>
    /// A VCD puts every signal under one scope for the top module. An elaborated design's names
    /// already start with it (<c>half_adder.s</c>); a circuit built in code may name its signals bare
    /// (<c>a</c>). Both must come out as one top scope, and read back as the design's own names.
    /// </summary>
    public class VcdNamingTests
    {
        private static string Vcd(Design design)
        {
            var sim = new Simulator(design);
            sim.RunTo(1);
            var output = new StringWriter();
            VcdWriter.Write(output, design, sim.History, 1);
            return output.ToString();
        }

        private static int Count(string text, string what) => Regex.Matches(text, Regex.Escape(what)).Count;

        [Test]
        public void AnElaboratedDesign_HasItsTopScopeOnce()
        {
            Design design = Elaborator.Elaborate(SyntaxReader.ReadText(
                "module half_adder (input logic a, b, output logic s); assign s = a ^ b; endmodule", "ha.sv")).Design;

            string text = Vcd(design);

            Assert.AreEqual(1, Count(text, "$scope module half_adder $end"), text);
            CollectionAssert.Contains(VcdReader.Read(new StringReader(text)).Keys, "half_adder.s");
        }

        [Test]
        public void ACircuitBuiltInCode_WithBareNames_IsPutUnderItsTop()
        {
            var b = new NetlistBuilder("top");
            SignalId a = b.Variable("a"), y = b.Net("y");
            b.Gate(GateKind.Not, "g", y, Delay.Of(1), a);

            string text = Vcd(b.Build());

            Assert.AreEqual(1, Count(text, "$scope module top $end"), text);
            CollectionAssert.Contains(VcdReader.Read(new StringReader(text)).Keys, "top.y");
        }

        /// <summary>
        /// ModelSim dumps a module's vector input a bit at a time -- a [3] to a [0] -- and they read back as one vector, a
        /// [3] its leftmost (Part 15i-4): read by name alone they were four signals called a, bit 0 standing for all of them.
        /// </summary>
        [Test]
        public void AVectorDumpedABitAtATime_ReadsBackWhole()
        {
            string[] dump =
            {
                "$timescale 1ps $end",
                "$scope module tb $end",
                "$var reg 4 ! a [3:0] $end",
                "$scope module dut $end",
                "$var wire 1 \" a [3] $end",
                "$var wire 1 # a [2] $end",
                "$var wire 1 $ a [1] $end",
                "$var wire 1 % a [0] $end",
                "$upscope $end",
                "$upscope $end",
                "$enddefinitions $end",
                "#0",
                "$dumpvars",
                "b0000 !",
                "0\"", "0#", "0$", "0%",
                "$end",
                "#5",
                "b1010 !",
                "1\"", "1$",
                "#9",
                "b1011 !",
                "1%",
            };
            var traces = VcdReader.Read(new StringReader(string.Join("\n", dump)));
            Assert.AreEqual(4, traces["tb.dut.a"].Width);
            CollectionAssert.AreEqual(traces["tb.a"].Changes, traces["tb.dut.a"].Changes, "the port reads as what drives it");
            Assert.AreEqual("1011", traces["tb.dut.a"].Changes[2].Bits);
        }
    }
}
