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

        /// <summary>
        /// The review's cases (2026-11-02): an ascending range dumped a bit at a time reads as written, leftmost first; a
        /// dump with no $enddefinitions still has its split vectors; and a file made to break the reader reads as what can be
        /// read, never throwing.
        /// </summary>
        [Test]
        public void AnAscendingRange_ReadsAsWritten_AndAFileMadeToBreakTheReader_DoesNot()
        {
            string[] ascending =
            {
                "$scope module tb $end", "$scope module u $end",
                "$var wire 1 ! b [0] $end", "$var wire 1 \" b [1] $end", "$var wire 1 # b [2] $end", "$var wire 1 $ b [3] $end",
                "$upscope $end", "$upscope $end", "$enddefinitions $end",
                "#0", "1!", "0\"", "0#", "0$",
                "#5", "0!", "1#", "1$",
            };
            var traces = VcdReader.Read(new StringReader(string.Join("\n", ascending)));
            Assert.AreEqual("1000", traces["tb.u.b"].Changes[0].Bits, "b [0] is the leftmost bit of [0:3]");
            Assert.AreEqual("0011", traces["tb.u.b"].Changes[1].Bits);

            string noEnd = "$scope module tb $end\n$var wire 1 ! a [1] $end\n$var wire 1 \" a [0] $end\n#0\n1!\n0\"\n";
            Assert.AreEqual("10", VcdReader.Read(new StringReader(noEnd))["tb.a"].Changes[0].Bits, "no $enddefinitions");

            foreach (string broken in new[]
            {
                "$scope module t $end $var wire -1 ! a $end $enddefinitions $end #0 1!",
                "$scope module t $end $var wire 2000000000 ! a $end $enddefinitions $end #0 b1 !",
                "$scope module t $end $var wire 1 ! a [0] $end $var wire 1 \" a [2000000000] $end $enddefinitions $end #0 1! 1\"",
                "$scope module t $end $var wire 1 ! a [-5] $end $var wire 1 \" a [2147483647] $end $enddefinitions $end #0 1!",
                "#notanumber b", "$var", "b", "$scope", "$scope module",
            })
                Assert.DoesNotThrow(() => VcdReader.Read(new StringReader(broken)), broken);
        }

        /// <summary>
        /// The review of 16c: every change was made as wide as its variable as it was read, once for each <c>$var</c> of its id,
        /// so a dump well under the 16 MB a file may be could hold many GB. Four variables of 65,536 bits on one id, 500 changes
        /// written short, as a dump may write a vector.
        /// </summary>
        [Test]
        public void AWideVariableWrittenShort_IsKeptAsWritten_OnceForTheVariablesSharingItsId_AndWidenedWhereItIsRead()
        {
            var dump = new System.Text.StringBuilder("$scope module tb $end\n");
            for (int v = 0; v < 4; v++)
                dump.Append($"$var wire 65536 ! w{v} [65535:0] $end\n");
            dump.Append("$upscope $end\n$enddefinitions $end\n");
            for (int t = 0; t < 500; t++)
                dump.Append($"#{t}\nb{(t % 2 == 0 ? "1" : "10")} !\n");
            string text = dump.ToString();

            long before = System.GC.GetAllocatedBytesForCurrentThread();
            var traces = VcdReader.Read(new StringReader(text));
            long made = System.GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.AreEqual(500, traces["tb.w3"].Changes.Count);
            Assert.AreEqual(new string('0', 65534) + "10", traces["tb.w3"].Changes[1].Bits, "at the variable's width, as it is read");
            Assert.AreEqual(traces["tb.w0"].Changes[499].Bits, traces["tb.w3"].Changes[499].Bits);
            Assert.Less(made, 4L << 20, $"reading a dump of {text.Length:N0} characters made {made:N0} bytes");
        }

        /// <summary>
        /// The same review: a vector dumped a bit at a time is kept whole at each change, so one 65,536 bits wide changing a bit at
        /// a time would hold its width again for every line of the file. Past what BitSorter holds, the dump is refused, saying why.
        /// </summary>
        [Test]
        public void AWideVectorDumpedABitAtATime_ThatWouldHoldTooMuch_IsRefused_SayingWhy()
        {
            var dump = new System.Text.StringBuilder("$scope module tb $end\n$scope module dut $end\n");
            for (int b = 65535; b >= 0; b--)
                dump.Append($"$var wire 1 v{b} a [{b}] $end\n");
            dump.Append("$upscope $end\n$upscope $end\n$enddefinitions $end\n");
            for (int t = 0; t < 1000; t++)
                dump.Append($"#{t}\n{t % 2}v0\n");

            var refused = Assert.Throws<InvalidDataException>(() => VcdReader.Read(new StringReader(dump.ToString())));
            StringAssert.Contains("a bit at a time", refused.Message);
        }
    }
}
