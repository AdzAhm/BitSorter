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
    }
}
