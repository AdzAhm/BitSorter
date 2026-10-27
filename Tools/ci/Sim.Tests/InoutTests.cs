using System.Linq;
using BitSorter.SystemVerilog;
using BitSorter.SystemVerilog.Syntax;
using NUnit.Framework;

namespace BitSorter.Sim.Tests
{
    /// <summary>
    /// Inout ports (Part 14f): one is joined to the whole net it is connected to -- the two are one signal, driven from
    /// both sides and resolved together -- and nothing else will do, since a one-way assignment each way would hold a bus at
    /// whatever it last was. The corpus's shared bus holds the rest to Icarus (<c>BehaviouralOracleTests</c>).
    /// </summary>
    public class InoutTests
    {
        private static Elaboration Elaborate(string text)
        {
            SyntaxTree tree = SyntaxReader.ReadText(text, "inout.sv");
            Assert.IsEmpty(tree.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error), string.Join("\n", tree.Diagnostics));
            return Elaborator.Elaborate(tree);
        }

        private static string At(Simulator sim, string name, long time)
        {
            Assert.IsTrue(sim.Design.TryFind(name, out SignalId signal), name);
            string value = sim.History.ValueAt(signal, time).ToString();   // 1'bz
            return value.Substring(value.IndexOf('b') + 1);
        }

        private const string Device = @"
module device (input logic en, d, inout wire io, output logic seen);
    bufif1 g (io, d, en);
    assign seen = io;
endmodule
";

        /// <summary>
        /// The device drives the bus while its enable is high, and the top while its own is: each reads what the other
        /// drives, x where both drive and disagree, z where neither does -- one net, found by either name.
        /// </summary>
        [Test]
        public void AnInout_IsTheNetItIsJoinedTo_DrivenFromBothSides()
        {
            Elaboration e = Elaborate(Device + @"
module top;
    logic en, d, mine, value;
    wire bus;
    logic seen;
    device u (.en(en), .d(d), .io(bus), .seen(seen));
    assign bus = mine ? value : 1'bz;
    initial begin
        en = 0; d = 1; mine = 0; value = 0;
        #10 en = 1;
        #10 mine = 1;
        #10 en = 0;
        #10 mine = 0;
        #10 $finish;
    end
endmodule");
            Assert.IsFalse(e.HasErrors, string.Join("\n", e.Diagnostics));
            var sim = new Simulator(e.Design);
            sim.RunTo(100);

            Assert.AreEqual(e.Design.Find("top.bus"), e.Design.Find("top.u.io"), "one signal, by either name");
            Assert.AreEqual("z", At(sim, "top.bus", 5), "nobody drives it");
            Assert.AreEqual("1", At(sim, "top.u.seen", 15), "the device drives it");
            Assert.AreEqual("x", At(sim, "top.bus", 25), "both, and they disagree");
            Assert.AreEqual("0", At(sim, "top.u.seen", 35), "the top drives it, and the device reads it");
            Assert.AreEqual("z", At(sim, "top.bus", 45));
        }

        [Test]
        public void AnInoutLeftUnconnected_FloatsOnItsOwn()
        {
            Elaboration e = Elaborate(Device + @"
module top;
    logic seen;
    device u (.en(1'b0), .d(1'b1), .io(), .seen(seen));
endmodule");
            Assert.IsFalse(e.HasErrors, string.Join("\n", e.Diagnostics));
            var sim = new Simulator(e.Design);
            sim.RunTo(10);
            Assert.AreEqual("z", At(sim, "top.u.io", 5));
        }

        [TestCase("wire [1:0] bus; logic seen;", ".io(bus[0])", "joins a whole net")]
        [TestCase("logic v; logic seen;", ".io(v)", "joins a whole net")]
        [TestCase("wire [1:0] bus; logic seen;", ".io(bus)", "an inout joins a net of its own width")]
        [TestCase("logic seen; wire w; mid m (.pin(w), .seen(seen));", null, "an input included")]
        public void AnInoutGivenAnythingButAWholeNetOfItsWidth_IsRefused(string declarations, string connection, string said)
        {
            // A module's input carries a value in only, so an inout joined to one would be half connected (the 14f review).
            const string Mid = @"
module mid (input wire pin, output logic seen);
    device u (.en(1'b0), .d(1'b1), .io(pin), .seen(seen));
endmodule
";
            Elaboration e = Elaborate(Device + (connection == null
                ? Mid + $"module top;\n    {declarations}\nendmodule"
                : $"module top;\n    {declarations}\n    device u (.en(1'b0), .d(1'b1), {connection}, .seen(seen));\nendmodule"));
            Assert.IsTrue(e.HasErrors);
            StringAssert.Contains(said, string.Join("\n", e.Diagnostics.Select(d => d.Message)));
        }

        /// <summary>An inout of the wrong width is said once, and nothing that reads it says more (the 14f review).</summary>
        [Test]
        public void AnInoutOfTheWrongWidth_IsSaidOnce()
        {
            Elaboration e = Elaborate(Device + @"
module top;
    wire [1:0] bus;
    logic seen;
    device u (.en(1'b0), .d(1'b1), .io(bus), .seen(seen));
endmodule");
            Assert.AreEqual(1, e.Diagnostics.Count, string.Join("\n", e.Diagnostics));
        }
    }
}
