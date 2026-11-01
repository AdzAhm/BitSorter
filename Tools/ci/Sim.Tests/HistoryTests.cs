using System.Linq;
using BitSorter.SystemVerilog;
using BitSorter.SystemVerilog.Syntax;
using NUnit.Framework;

namespace BitSorter.Sim.Tests
{
    /// <summary>
    /// What a signal held before its last change (Part 15h-6e, <see cref="History.ValueBefore"/>): the state a machine came
    /// from, which a controller's diagram lights the arrow of.
    /// </summary>
    public class HistoryTests
    {
        private const string Steps = @"
module steps;
    logic [1:0] s = 2'd0;
    initial begin
        #10 s = 2'd1;
        #10 s = 2'd1;
        #10 s = 2'd3;
    end
endmodule
";

        [Test]
        public void ValueBefore_IsWhatTheSignalHeldBeforeItLastChanged()
        {
            SyntaxTree tree = SyntaxReader.ReadText(Steps, "steps.sv");
            Assert.IsEmpty(tree.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error), string.Join("\n", tree.Diagnostics));
            var sim = new Simulator(Elaborator.Elaborate(tree).Design);
            sim.RunTo(100);
            Assert.IsTrue(sim.Design.TryFind("steps.s", out SignalId s));

            Assert.AreEqual(0UL, Value(sim.History.ValueBefore(s, 5)), "never anything else: itself");
            Assert.AreEqual(0UL, Value(sim.History.ValueBefore(s, 10)), "1 since 10, 0 before");
            Assert.AreEqual(0UL, Value(sim.History.ValueBefore(s, 25)), "set to 1 again at 20 is no change: still 0 before");
            Assert.AreEqual(1UL, Value(sim.History.ValueBefore(s, 30)));
            Assert.AreEqual(1UL, Value(sim.History.ValueBefore(s, 99)));
        }

        private static ulong Value(LogicVec v)
        {
            Assert.IsTrue(v.TryGetUInt64(out ulong value), v.ToString());
            return value;
        }
    }
}
