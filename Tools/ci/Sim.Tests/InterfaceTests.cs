using System.Linq;
using BitSorter.SystemVerilog;
using BitSorter.SystemVerilog.Syntax;
using NUnit.Framework;

namespace BitSorter.Sim.Tests
{
    /// <summary>
    /// Part L8: interfaces and modports. <c>corpus/behavioural/interfaces.sv</c> holds what Icarus runs of them, and
    /// <c>InterfaceOracleTests</c> holds interface ports to Icarus through the same design spelled out; these hold
    /// what Icarus 12 cannot judge -- what a modport refuses, how a port is given its instance -- and calls into a
    /// module's instance, which come with them.
    /// </summary>
    public class InterfaceTests
    {
        /// <summary>An interface with a modport for each side, which the cases below build on.</summary>
        private const string Bus = @"
interface bus_if (input logic clk);
    logic [7:0] data;
    logic valid, ready;
    int sent;
    function automatic logic parity();
        return ^data;
    endfunction
    task automatic send(input logic [7:0] v);
        data = v;
        valid = 1;
        sent++;
    endtask
    modport source (output data, valid, input ready, clk, import send);
    modport sink (input data, valid, clk, output ready, import parity);
    modport shared (ref data, input valid);
endinterface

interface other_if;
    logic x;
endinterface
";

        private static Elaboration Elaborate(string text, string top = null)
        {
            SyntaxTree tree = SyntaxReader.ReadText(Bus + text, "test.sv");
            Assert.IsEmpty(tree.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error), "the read reported:\n" + string.Join("\n", tree.Diagnostics));
            return Elaborator.Elaborate(tree, top);
        }

        private static string[] Run(string text, long until = 100)
        {
            Elaboration e = Elaborate(text);
            Assert.IsFalse(e.HasErrors, "the elaboration reported:\n" + string.Join("\n", e.Diagnostics));
            var sim = new Simulator(e.Design, null, null);
            sim.RunTo(until);
            return sim.History.Messages.Where(m => m.Severity == Severity.Info && !m.Text.StartsWith("$")).Select(m => m.Text).ToArray();
        }

        [Test]
        public void AnInterfacePort_IsGivenByName_ByShorthand_OrByStar_AndWorksTheInstanceItIsGiven()
        {
            CollectionAssert.AreEqual(new[] { "5a 1 1 4", "3c 0 1 2" }, Run(@"
module source_side (bus_if.source bus, input logic [7:0] v);
    always @(v) bus.send(v);
endmodule

module sink_side (bus_if.sink bus, output logic odd);
    initial bus.ready = 1;
    always_comb odd = bus.parity();   // wakes on what the function reads, as always_comb does
endmodule

module top;
    logic clk = 0;
    logic [7:0] v;
    logic odd1, odd2;
    bus_if bus (.clk(clk));
    source_side s (.bus, .v(v));
    sink_side k1 (.*, .odd(odd1));
    sink_side k2 (.bus(bus.sink), .odd(odd2));
    initial begin
        v = 8'h5a;
        #2 v = 8'h3c;
        #2 v = 8'h12;
    end
    initial begin
        // Between the changes, never on one.
        #1 $display(""%h %b %b %0d"", bus.data, bus.valid, bus.ready, s.bus.sent + 3);
        #2 $display(""%h %b %b %0d"", bus.data, odd1 ^ odd2, k2.bus.ready, bus.sent);
    end
endmodule"));
        }

        [Test]
        public void AFunctionOrTaskOfAModulesInstance_IsCalledThroughTheHierarchy()
        {
            CollectionAssert.AreEqual(new[] { "7 9" }, Run(@"
module counter;
    int n;
    function automatic int plus(input int k);
        return n + k;
    endfunction
    task automatic bump(input int by);
        n = n + by;
    endtask
endmodule

module top;
    counter c ();
    initial begin
        c.n = 2;
        c.bump(5);
        $display(""%0d %0d"", c.n, c.plus(2));
    end
endmodule"));
        }

        [Test]
        public void ARefInAModport_IsWrittenAsWellAsRead()
        {
            CollectionAssert.AreEqual(new[] { "99" }, Run(@"
module user (bus_if.shared bus);
    initial #1 bus.data = 8'h99;
endmodule

module top;
    logic clk;
    bus_if bus (.clk(clk));
    user u (.bus(bus));
    initial #2 $display(""%h"", bus.data);
endmodule"));
        }

        [Test]
        public void APortOfATypedefsType_FromAnotherFile_IsStillAPort()
        {
            // nib_t is no type the second file's reader has met, so 'nib_t b' reads as an interface port would; the
            // elaborator finds the typedef, and b is an input, the direction written before it.
            var files = new MemorySourceFiles()
                .Add("pkg.sv", "package p;\n    typedef logic [3:0] nib_t;\nendpackage\n")
                .Add("top.sv", "module m import p::*; (input logic a, nib_t b, output logic [3:0] y);\n    assign y = a ? b : 4'd0;\nendmodule\n" +
                               "module top;\n    logic [3:0] y;\n    m u (.a(1'b1), .b(4'd9), .y(y));\n    initial #1 $display(\"%0d\", y);\nendmodule\n");
            SyntaxTree tree = SyntaxReader.ReadAll(new[] { "pkg.sv", "top.sv" }, files);
            Assert.AreEqual("nib_t", tree.Modules[0].Ports[1].Interface, "read as an interface port");

            Elaboration e = Elaborator.Elaborate(tree, "top");
            Assert.IsFalse(e.HasErrors, string.Join("\n", e.Diagnostics));
            var sim = new Simulator(e.Design, null, null);
            sim.RunTo(10);
            CollectionAssert.Contains(sim.History.Messages.Select(m => m.Text).ToArray(), "9");
        }

        [Test]
        public void AnInterfacesSignals_AreTheInstancesOwn_UnderItsName()
        {
            Elaboration e = Elaborate(@"
module top;
    logic clk;
    bus_if hs (.clk(clk));
    other_if o ();
endmodule");
            Assert.IsFalse(e.HasErrors, string.Join("\n", e.Diagnostics));
            string[] names = e.Design.Signals.Select(s => s.Name).ToArray();
            CollectionAssert.IsSubsetOf(new[] { "top.hs.data", "top.hs.valid", "top.hs.ready", "top.hs.clk", "top.o.x" }, names);
        }

        [TestCase("module m (bus_if.sink bus); initial bus.data = 1; endmodule", "is an input of modport sink", ElaborationCodes.BadLValue)]
        [TestCase("module m (bus_if.sink bus); assign bus.valid = 1; endmodule", "is an input of modport sink", ElaborationCodes.BadLValue)]
        [TestCase("module m (bus_if.source bus); initial $display(bus.sent); endmodule", "does not list sent", ElaborationCodes.UnknownName)]
        [TestCase("module m (bus_if.sink bus); initial bus.send(1); endmodule", "does not import send", ElaborationCodes.UnknownName)]
        [TestCase("module m (bus_if bus); initial bus.nothing(1); endmodule", "has no task or function called nothing", ElaborationCodes.UnknownName)]
        [TestCase("module m (bus_if bus); initial $display(bus); endmodule", "is an interface, not a signal", ElaborationCodes.UnknownName)]
        public void WhatAModportOrAnInterfaceRefuses_IsSaid(string module, string message, string code)
        {
            Elaboration e = Elaborate(module + "\nmodule top;\n    logic clk;\n    bus_if hs (.clk(clk));\n    m u (.bus(hs));\nendmodule", "top");
            Assert.IsTrue(e.Diagnostics.Any(d => d.Code == code && d.Message.Contains(message)), string.Join("\n", e.Diagnostics));
        }

        [TestCase("module m (bus_if bus); endmodule\nmodule top; other_if o (); m u (.bus(o)); endmodule", "takes a bus_if, and o is a other_if")]
        [TestCase("module m (bus_if bus); endmodule\nmodule top; logic clk; m u (.bus(clk)); endmodule", "is not an instance of one")]
        [TestCase("module m (bus_if bus); endmodule\nmodule top; m u (); endmodule", "is not connected")]
        [TestCase("module m (bus_if.sink bus); endmodule\nmodule top; logic clk; bus_if hs (.clk(clk)); m u (.bus(hs.source)); endmodule", "is modport sink, and hs.source names modport source")]
        [TestCase("module inner (bus_if.source b); endmodule\nmodule m (bus_if.sink bus); inner i (.b(bus)); endmodule\nmodule top; logic clk; bus_if hs (.clk(clk)); m u (.bus(hs)); endmodule", "is modport sink of bus_if here, so it cannot be given on as modport source")]
        [TestCase("module m (bus_if.nope bus); endmodule\nmodule top; logic clk; bus_if hs (.clk(clk)); m u (.bus(hs)); endmodule", "has no modport called nope")]
        [TestCase("module m (nothing_if.mp bus); endmodule\nmodule top; m u (); endmodule", "There is no interface called nothing_if")]
        [TestCase("module m (input logic a, nothing_t b); endmodule\nmodule top; m u (); endmodule", "There is no interface or type called nothing_t")]
        [TestCase("module m (bus_if bus [1:0]); endmodule\nmodule top; m u (); endmodule", "an array of interfaces")]
        [TestCase("module top (bus_if bus); endmodule", "is where the simulation starts")]
        public void AnInterfacePortThatCannotBeGiven_SaysWhy(string text, string message)
        {
            Elaboration e = Elaborate(text, "top");
            Assert.IsTrue(e.HasErrors);
            Assert.IsTrue(e.Diagnostics.Any(d => d.Message.Contains(message)), string.Join("\n", e.Diagnostics));
        }

        [Test]
        public void AModportNamingWhatTheInterfaceLacks_IsSaidOnce_HoweverManyInstances()
        {
            SyntaxTree tree = SyntaxReader.ReadText(@"
interface bad_if;
    logic a;
    modport mp (input a, b, import go);
endinterface
module top;
    bad_if one ();
    bad_if two ();
endmodule", "test.sv");
            Elaboration e = Elaborator.Elaborate(tree, "top");
            Assert.AreEqual(1, e.Diagnostics.Count(d => d.Message.Contains("lists b")), string.Join("\n", e.Diagnostics));
            Assert.AreEqual(1, e.Diagnostics.Count(d => d.Message.Contains("imports go")), string.Join("\n", e.Diagnostics));
        }

        [Test]
        public void AnInterface_IsNeverTheTop()
        {
            Elaboration e = Elaborate("module top; endmodule", "bus_if");
            StringAssert.Contains("is an interface", string.Join("\n", e.Diagnostics));
            Assert.AreEqual("top", Elaborate("module top; endmodule").Top, "with none named, the module is the top");
        }
    }
}
