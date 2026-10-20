using System.IO;
using System.Linq;
using BitSorter.SystemVerilog;
using BitSorter.SystemVerilog.Syntax;
using NUnit.Framework;

namespace BitSorter.Sim.Tests
{
    /// <summary>
    /// Part L7: reals, and delays in fractions of a unit. <c>corpus/behavioural/reals.sv</c> holds what Icarus
    /// runs as well; these hold BitSorter's own choices -- a shortreal held as a real, a real through a port, a
    /// fraction with no timescale, a VCD's real -- and what is refused.
    /// </summary>
    public class RealTests
    {
        private static Elaboration Elaborate(string text)
        {
            SyntaxTree tree = SyntaxReader.ReadText(text, "test.sv");
            Assert.IsEmpty(tree.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error), "the read reported:\n" + string.Join("\n", tree.Diagnostics));
            return Elaborator.Elaborate(tree, null);
        }

        private static Simulator Run(string text, long until = 100)
        {
            Elaboration e = Elaborate(text);
            Assert.IsFalse(e.HasErrors, "the elaboration reported:\n" + string.Join("\n", e.Diagnostics));
            var sim = new Simulator(e.Design, null, null);
            sim.RunTo(until);
            return sim;
        }

        private static string[] Printed(Simulator sim) =>
            sim.History.Messages.Where(m => m.Severity == Severity.Info && !m.Text.StartsWith("$")).Select(m => m.Text).ToArray();

        [Test]
        public void ARealPassesThroughAPort_AndAShortrealIsHeldAsOne()
        {
            CollectionAssert.AreEqual(new[] { "5.000000 0.100000" }, Printed(Run(@"
module halver (input real x, output real y);
    assign y = x / 2;
endmodule

module t;
    real in, out;
    shortreal tenth = 0.1;
    halver h (.x(in), .y(out));
    initial begin
        in = 10;
        #1 $display(""%f %f"", out, tenth);
    end
endmodule")));
        }

        [Test]
        public void AFractionalDelay_WithNoTimescale_RoundsToAWholeUnit()
        {
            // With no `timescale a tick is one unit, and a delay is rounded to it, half away from zero.
            CollectionAssert.AreEqual(new[] { "3", "4", "6" }, Printed(Run(@"
module t;
    initial begin
        #2.5 $display(""%0t"", $time);
        #1.4 $display(""%0t"", $time);
        #1.6 $display(""%0t"", $time);
    end
endmodule")));
        }

        [Test]
        public void AVcd_WritesARealAsAReal()
        {
            Simulator sim = Run(@"
module t;
    real level;
    logic bit0;
    initial begin
        level = 2.5;
        bit0 = 1;
        #1 level = -0.125;
    end
endmodule", until: 5);

            var text = new StringWriter();
            VcdWriter.Write(text, sim.Design, sim.History, 5);
            string vcd = text.ToString();
            StringAssert.Contains("$var real 64", vcd);
            StringAssert.Contains("r2.5 ", vcd);
            StringAssert.Contains("r-0.125 ", vcd);
            StringAssert.Contains("$var reg 1", vcd, "a whole-number signal beside it is as it was");
        }

        [Test]
        public void ATimescaleBitSorterCannotRead_IsSaidAndTheModuleCountsInWholeTicks()
        {
            Elaboration e = Elaborate("`timescale 1ns/10ns\nmodule t; initial #2 $display(\"%0t\", $time); endmodule");
            Assert.IsFalse(e.HasErrors, string.Join("\n", e.Diagnostics));
            StringAssert.Contains("precision no coarser than the unit", string.Join("\n", e.Diagnostics));
        }

        [TestCase("real r; logic [3:0] y; initial y = ~r;", ElaborationCodes.BadSelect)]
        [TestCase("real r; int y; initial y = r % 2;", ElaborationCodes.BadSelect)]
        [TestCase("real r; int y; initial y = r << 1;", ElaborationCodes.BadSelect)]
        [TestCase("real r; initial r = $sqrt(1.0, 2.0);", ElaborationCodes.BadCall)]
        [TestCase("logic y; and #(-1.5) g (y, y, y);", ElaborationCodes.BadDelay)]
        [TestCase("real [3:0] r;", ElaborationCodes.BadSelect)]
        public void WhatARealCannotDo_IsRefused_WithWhy(string items, string code)
        {
            Elaboration e = Elaborate("module t;\n    " + items + "\nendmodule");
            CollectionAssert.Contains(e.Diagnostics.Select(d => d.Code).ToArray(), code, string.Join("\n", e.Diagnostics));
        }
    }
}
