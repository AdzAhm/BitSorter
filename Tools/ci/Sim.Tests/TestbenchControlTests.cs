using System.Linq;
using BitSorter.SystemVerilog;
using BitSorter.SystemVerilog.Syntax;
using NUnit.Framework;

namespace BitSorter.Sim.Tests
{
    /// <summary>
    /// Part L5's testbench control. <c>corpus/behavioural/testbench_control.sv</c> holds what Icarus runs as
    /// well; these hold, by the standard, what Icarus 12 runs otherwise or not at all (Docs/oracle-notes.md):
    /// the order of branches started in one moment, <c>-&gt;&gt;</c> and <c>.triggered</c>, a force that follows
    /// its expression, an assertion's own message -- and what is refused.
    /// </summary>
    public class TestbenchControlTests
    {
        private static Elaboration Elaborate(string text)
        {
            SyntaxTree tree = SyntaxReader.ReadText(text, "test.sv");
            Assert.IsEmpty(tree.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error), "the read reported:\n" + string.Join("\n", tree.Diagnostics));
            return Elaborator.Elaborate(tree, null);
        }

        private static string[] Printed(string text, long until = 200, Severity severity = Severity.Info)
        {
            Elaboration e = Elaborate(text);
            Assert.IsFalse(e.HasErrors, "the elaboration reported:\n" + string.Join("\n", e.Diagnostics));
            var sim = new Simulator(e.Design, null, null);
            sim.RunTo(until);
            return sim.History.Messages.Where(m => m.Severity == severity && !m.Text.StartsWith("$")).Select(m => m.Text).ToArray();
        }

        // -----------------------------------------------------------------
        // fork
        // -----------------------------------------------------------------

        [Test]
        public void BranchesStartedTogether_RunInTheOrderWritten_AfterAJoinNonesProcessWaits()
        {
            // The standard leaves the order open; ModelSim keeps the order written, Icarus 12 the reverse.
            CollectionAssert.AreEqual(new[] { "A", "B", "after join", "E", "C", "D" }, Printed(@"
module t;
    initial begin
        fork
            $display(""A"");
            $display(""B"");
        join
        $display(""after join"");
        fork
            $display(""C"");
            $display(""D"");
        join_none
        $display(""E"");
        #0;
    end
endmodule"));
        }

        [Test]
        public void ABranchForkedAgainWhileItRuns_StopsTheRun_SayingHowToWaitOrEndIt()
        {
            Elaboration e = Elaborate(@"
module t;
    initial begin
        repeat (2) begin
            fork
                #5 $display(""slow"");
                #1 $display(""quick"");
            join_any
        end
    end
endmodule");
            Assert.IsFalse(e.HasErrors, string.Join("\n", e.Diagnostics));
            var sim = new Simulator(e.Design, null, null);
            RunResult result = sim.RunTo(100);

            Assert.AreEqual(RunStatus.Stopped, result.Status);
            Assert.AreEqual(1, result.Time);
            StringAssert.Contains("again while it was still running", sim.History.Messages.Last().Text);
        }

        [Test]
        public void DisableFork_EndsAForksGrandchildren_AsWellAsItsChildren()
        {
            CollectionAssert.AreEqual(new[] { "1 child ends", "2 disabled", "after wait fork at 2" }, Printed(@"
module t;
    initial begin
        fork
            begin
                fork
                    #10 $display(""%0t grandchild"", $time);
                join_none
                #1 $display(""%0t child ends"", $time);
            end
        join_none
        #2 disable fork;
        $display(""%0t disabled"", $time);
        wait fork;
        $display(""after wait fork at %0t"", $time);
    end
endmodule"));
        }

        // -----------------------------------------------------------------
        // disable
        // -----------------------------------------------------------------

        [Test]
        public void DisablingATask_OrABlock_FromAnotherProcess_SendsWhateverIsInsideOnPastIt()
        {
            CollectionAssert.AreEqual(new[] { "3 after the task, count 1", "6 after the block" }, Printed(@"
module t;
    int count = 0;
    task wait_a_while;
        repeat (10) #2 count++;
        $display(""never: the task ran out"");
    endtask
    initial begin
        wait_a_while;
        $display(""%0t after the task, count %0d"", $time, count);
        begin : idle
            #100 $display(""never: the block ran out"");
        end
        $display(""%0t after the block"", $time);
    end
    initial begin
        #3 disable wait_a_while;
        #3 disable idle;
    end
endmodule"));
        }

        // -----------------------------------------------------------------
        // Events
        // -----------------------------------------------------------------

        [Test]
        public void Triggered_HoldsFromATriggerToTheEndOfItsTimeStep_AndANonblockingTriggerComesInTheNbaRegion()
        {
            CollectionAssert.AreEqual(new[] { "a 1", "b 0", "c 0", "waited at 1", "d 0" }, Printed(@"
module t;
    event e;
    initial begin
        -> e;
        $display(""a %b"", e.triggered);
        #1 $display(""b %b"", e.triggered);
        ->> e;
        $display(""c %b"", e.triggered());
        #1 $display(""d %b"", e.triggered);
    end
    initial begin
        #1 wait (e.triggered) $display(""waited at %0t"", $time);
    end
endmodule"));
        }

        // -----------------------------------------------------------------
        // force and release
        // -----------------------------------------------------------------

        [Test]
        public void AForce_FollowsItsExpression_AsAContinuousAssignmentDoes()
        {
            // IEEE 1800, 10.6.2; Icarus 12 evaluates it once, when the force runs.
            CollectionAssert.AreEqual(new[] { "w=0010", "w=0110", "q=0110", "q=0100", "q=0100 kept", "q=1111 written" }, Printed(@"
module t;
    logic [3:0] a = 4'b1111, b = 4'b0010, q;
    wire [3:0] w = a ^ b;
    initial begin
        force w = a & b;
        #1 $display(""w=%b"", w);
        b = 4'b0110;
        #1 $display(""w=%b"", w);
        force q = a & b;
        #1 $display(""q=%b"", q);
        a = 4'b0101;
        #1 $display(""q=%b"", q);
        release q;
        a = 4'b1111;
        #1 $display(""q=%b kept"", q);
        q = a;
        #1 $display(""q=%b written"", q);
    end
endmodule"));
        }

        // -----------------------------------------------------------------
        // Assertions
        // -----------------------------------------------------------------

        [Test]
        public void AnAssertion_RunsItsPassOrFailAction_AndFailsOnXWithAMessageOfItsOwn()
        {
            const string text = @"
module t;
    logic [1:0] v = 2'b01;
    logic u;
    initial begin
        assert (v == 1) $display(""passes"");
        check_v: assert (v == 2);
        assert (u) $display(""never"");
        else $display(""x fails"");
        assume (v != 0);
        cover (v == 1) $display(""covered"");
        cover (v == 3) $display(""never covered"");
    end
endmodule";
            CollectionAssert.AreEqual(new[] { "passes", "x fails", "covered" }, Printed(text));
            CollectionAssert.AreEqual(new[] { "check_v: assert (v == 2) failed (test.sv:7)" }, Printed(text, severity: Severity.Error));
        }

        // -----------------------------------------------------------------
        // What is refused
        // -----------------------------------------------------------------

        [TestCase("logic y; always_comb fork y = 1; join", ElaborationCodes.NeverWaits)]
        [TestCase("logic y; function logic f(input logic a); fork join_none return a; endfunction assign y = f(1'b1);", ElaborationCodes.BadCall)]
        [TestCase("logic [3:0] v; initial force v[1] = 1'b1;", ElaborationCodes.BadLValue)]
        [TestCase("initial disable nowhere;", ElaborationCodes.UnknownName)]
        [TestCase("function logic f(input logic a); return a; endfunction initial disable f;", ElaborationCodes.UnknownName)]
        [TestCase("event e; logic y; initial y = e;", ElaborationCodes.BadSelect)]
        [TestCase("logic y; initial -> y;", ElaborationCodes.BadLValue)]
        [TestCase("initial begin event e; end", ElaborationCodes.NotYet)]
        [TestCase("event e; initial force e = 1'b1;", ElaborationCodes.BadLValue)]
        public void WhatTestbenchControlCannotDo_IsRefused_WithWhy(string items, string code)
        {
            Elaboration e = Elaborate("module t;\n    " + items + "\nendmodule");
            CollectionAssert.Contains(e.Diagnostics.Select(d => d.Code).ToArray(), code, string.Join("\n", e.Diagnostics));
        }

        [TestCase("initial assert property (a);")]
        [TestCase("initial assert #0 (a);")]
        [TestCase("initial assert final (a);")]
        [TestCase("initial deassign a;")]
        public void AConcurrentOrDeferredAssertion_AndDeassign_AreRefusedAsTheyAreRead(string items)
        {
            SyntaxTree tree = SyntaxReader.ReadText("module t;\n    logic a;\n    " + items + "\nendmodule", "test.sv");
            Assert.IsTrue(tree.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error && d.Code == DiagnosticCodes.Unsupported), string.Join("\n", tree.Diagnostics));
        }
    }
}
