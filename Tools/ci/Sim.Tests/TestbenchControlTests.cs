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

        [Test]
        public void ANamedForkDisabledFromAnotherProcess_LetsAWaitForkGoOn_WhenItsBranchesEnd()
        {
            // IEEE 1800, 9.6.2: disable ends every process executing the block, its branches; and 9.6.1, wait fork waits
            // until every child has ended -- so the parent goes on as they end. ModelSim and Icarus 12 both leave it
            // waiting (Docs/oracle-notes.md, "Testbench control").
            CollectionAssert.AreEqual(new[] { "joined at 50" }, Printed(@"
module t;
    initial begin
        fork : f
            #100 $display(""slow"");
        join_none
        #10;
        wait fork;
        $display(""joined at %0t"", $time);
    end
    initial #50 disable f;
endmodule"));

            // The watchdog, ended by another process once the work is done.
            CollectionAssert.AreEqual(new[] { "all ended at 7" }, Printed(@"
module t;
    event finished;
    initial begin
        fork : watchdog
            #1000 $display(""timed out"");
        join_none
        #5 -> finished;
        wait fork;
        $display(""all ended at %0t"", $time);
    end
    initial begin
        @(finished) #2 disable watchdog;
    end
endmodule", until: 2000));
        }

        [Test]
        public void ABlockDisabledWhileItWaitsAtAJoin_GoesOnPastTheBlock_NeverPastTheJoin()
        {
            // Its branches end with it, and the join they end is not where it goes on: Icarus and ModelSim agree. The two go
            // on in one moment, woken by one disable, in an order the standard leaves open.
            CollectionAssert.AreEquivalent(new[] { "after blk at 50", "any at 50" }, Printed(@"
module t;
    initial begin
        begin : blk
            fork
                #100 $display(""slow"");
            join
            $display(""never: after the join, inside blk"");
        end
        $display(""after blk at %0t"", $time);
    end
    initial begin
        #1;
        fork : f
            #100 $display(""slow"");
            #120 $display(""slower"");
        join_any
        $display(""any at %0t"", $time);
    end
    initial #50 begin disable blk; disable f; end
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

        [Test]
        public void AWireNothingDrives_IsZAgainOnceReleased_AllOfItOrABitOfIt()
        {
            // IEEE 1800, 10.6.2: a released net's value is its drivers' -- with none, z. Icarus 12 and ModelSim agree; the
            // corpus's testbench_control.sv asks it of both.
            CollectionAssert.AreEqual(new[] { "forced 1 zz10", "released z zzzz" }, Printed(@"
module t;
    wire w;
    wire [3:0] v;
    initial begin
        force w = 1'b1;
        force v[1:0] = 2'b10;
        #1 $display(""forced %b %b"", w, v);
        #4 release w;
        release v[1:0];
        #1 $display(""released %b %b"", w, v);
    end
endmodule"));
        }

        /// <summary>
        /// A force or a release of part of an earlier, wider force takes those bits alone (IEEE 1800, 10.6.2; the 16d sweep,
        /// engine-10): the earlier force was taken away whole, so its bits outside went back to their drivers -- zz00 for
        /// 1100 -- and releasing one bit released four. Icarus 12 and ModelSim print these lines.
        /// </summary>
        [Test]
        public void AForceOrReleaseOfPartOfAWiderForce_TakesThoseBitsAlone()
        {
            CollectionAssert.AreEqual(new[] { "1100 1000" }, Printed(@"
module t;
    wire [3:0] v;
    wire [3:0] w;
    assign w = 4'h5;
    initial begin
        force v = 4'b1111;
        force w = 4'hA;
        #1 force v[1:0] = 2'b00;
        release w[1];
        #1 $display(""%b %b"", v, w);
    end
endmodule"));
        }

        // -----------------------------------------------------------------
        // assign and deassign inside a process (M4)
        // -----------------------------------------------------------------

        /// <summary>
        /// IEEE 1800, 10.6.1, as ModelSim runs <c>corpus/behavioural/procedural_assign.sv</c> -- these lines are its, to the
        /// character. Icarus 12 reads an assign's right side once, as it runs, so the oracle holds the design to ModelSim, and
        /// this holds it here, where CI runs.
        /// </summary>
        [Test]
        public void AProceduralAssign_FollowsItsValue_OverEveryWrite_UntilDeassigned_UnderAnyForce()
        {
            string text = System.IO.File.ReadAllText(Ci.RepoRoot.Combine("Tools/ci/Oracle/corpus/behavioural/procedural_assign.sv"));
            CollectionAssert.AreEqual(new[]
            {
                "1 assigned: y=3",
                "2 follows a: y=7",
                "3 a write ignored: y=7",
                "4 assigned again: y=3",
                "5 forced: y=15",
                "6 still forced: y=15",
                "7 released, the assign again: y=6",
                "9 deassigned, kept: y=6",
                "10 written: y=2",
                "12 a concatenation: r=1 s=3",
                "13 an assign under a force: y=4",
                "14 shown on release: y=7",
                "15 deassigned under a force: y=1",
                "16 released, kept: y=1",
                "17 deassigned: r=1 s=3",
                "21 u=3 v=3 f=3 fw=3",
                "21 at once: u=7, a force not yet: f=3 fw=3",
                "21 deassigned: u=7, a write ignored: v=7",
                "22 next: u=7 v=7 f=7 fw=7",
                "23 two that read each other: g=3 h=2",
                "53 after the first edge: q=7 count=1",
                "63 after the second edge: q=3 count=2",
            }, Printed(text));
        }

        [TestCase("logic [3:0] a, y; assign y = a; initial assign y = 4'd1;")]
        [TestCase("logic [3:0] a, y; initial assign y = 4'd1; assign y = a;")]
        public void AnAssignOntoAVariableDrivenContinuously_IsRefused_AsAWriteIs(string items)
        {
            Elaboration e = Elaborate("module t;\n    " + items + "\nendmodule");
            Diagnostic refused = e.Diagnostics.FirstOrDefault(d => d.Code == ElaborationCodes.Drivers);
            Assert.IsNotNull(refused, string.Join("\n", e.Diagnostics));
            StringAssert.Contains("a variable takes one or the other", refused.Message);
        }

        [Test]
        public void AnAssignDeassignedInPart_KeepsThatPart_AndAReleaseWithNoForceLeavesItFollowing()
        {
            CollectionAssert.AreEqual(new[] { "r=1 s=3", "y=6" }, Printed(@"
module t;
    logic [3:0] a = 1, b = 2, r, s, y;
    initial begin
        assign {r, s} = {a, b};
        deassign r;
        a = 4'd5; b = 4'd3;
        #1 $display(""r=%0d s=%0d"", r, s);
        assign y = a;
        release y;
        a = 4'd6;
        #1 $display(""y=%0d"", y);
    end
endmodule"));
        }

        [Test]
        public void AnAssignInAnAlwaysComb_FollowsWhatItReads()
        {
            CollectionAssert.AreEqual(new[] { "y=1", "y=0" }, Printed(@"
module t;
    logic a = 1, y;
    always_comb assign y = a;
    initial begin
        #1 $display(""y=%0d"", y);
        a = 0;
        #1 $display(""y=%0d"", y);
    end
endmodule"));
        }

        [Test]
        public void TheSlipsWarning_IsSaidInEveryKindOfAlways_AndOnceHoweverManyTimesItsModuleIsPlaced()
        {
            Elaboration e = Elaborate(@"
module c (input logic clk, a, output logic y);
    always_ff @(posedge clk) assign y = a;
endmodule
module t;
    logic clk, a, y1, y2, y3, l, f, k;
    c u1 (.clk(clk), .a(a), .y(y1));
    c u2 (.clk(clk), .a(a), .y(y2));
    c u3 (.clk(clk), .a(a), .y(y3));
    task automatic set(); assign k = a; endtask
    always_latch if (a) assign l = a;
    always @(posedge clk) fork assign f = a; join_none
    always @(negedge clk) set();
endmodule");
            Assert.IsFalse(e.HasErrors, string.Join("\n", e.Diagnostics));
            int[] lines = e.Diagnostics.Where(d => d.Code == ElaborationCodes.ProceduralAssign).Select(d => d.Location.Line).OrderBy(l => l).ToArray();
            CollectionAssert.AreEqual(new[] { 3, 10, 11, 12 }, lines, "the always_ff's once for three placings, the latch's, the fork's and the task's");
        }

        [TestCase("logic [0:0] v; initial assign v[0] = 1'b1;", "v[0] is bits of a variable")]
        [TestCase("logic [3:0] y; initial assign y[3:0] = 4'd1;", "y[3:0] is bits of a variable")]
        [TestCase("logic [0:0][7:0] p; initial assign p[0] = 8'd1;", "p[0] is bits of a variable")]
        [TestCase("logic [3:0] m [4]; initial assign m[1] = 4'd1;", "m[1] is a word of an array")]
        public void ASelectWholeOfItsSignal_IsSaidAsWhatItSelects(string items, string says)
        {
            Elaboration e = Elaborate("module t;\n    " + items + "\nendmodule");
            Diagnostic refused = e.Diagnostics.FirstOrDefault(d => d.Code == ElaborationCodes.BadLValue);
            Assert.IsNotNull(refused, string.Join("\n", e.Diagnostics));
            StringAssert.Contains(says, refused.Message);
        }

        [Test]
        public void AnAssignInAnAlways_IsSaid_AndOneInAnInitialIsNot()
        {
            Elaboration e = Elaborate(@"
module t;
    logic a, y, z, clk;
    always @(posedge clk) assign y = a;
    initial assign z = a;
endmodule");
            Assert.IsFalse(e.HasErrors, string.Join("\n", e.Diagnostics));
            Diagnostic[] said = e.Diagnostics.Where(d => d.Code == ElaborationCodes.ProceduralAssign).ToArray();
            Assert.AreEqual(1, said.Length, string.Join("\n", e.Diagnostics));
            Assert.AreEqual(4, said[0].Location.Line, "the always's, not the initial's: a testbench means it");
            Assert.AreEqual(DiagnosticSeverity.Warning, said[0].Severity);
            StringAssert.Contains("y follows a and ignores every other write to it, until a deassign", said[0].Message);
        }

        [TestCase("wire w; initial assign w = 1'b1;", "a wire, which force overrides")]
        [TestCase("logic [3:0] v; initial assign v[1] = 1'b1;", "bits of a variable")]
        [TestCase("logic [3:0] v; initial deassign v[3:2];", "bits of a variable")]
        [TestCase("event e; initial assign e = 1'b1;", "an event")]
        public void AProceduralAssign_TakesWholeVariables_AndSaysWhatElseItWasGiven(string items, string says)
        {
            Elaboration e = Elaborate("module t;\n    " + items + "\nendmodule");
            Diagnostic refused = e.Diagnostics.FirstOrDefault(d => d.Code == ElaborationCodes.BadLValue);
            Assert.IsNotNull(refused, string.Join("\n", e.Diagnostics));
            StringAssert.Contains("takes a whole variable, or a concatenation of whole variables", refused.Message);
            StringAssert.Contains(says, refused.Message);
        }

        [Test]
        public void AMemorysWord_AndAFunction_AreRefusedAProceduralAssign()
        {
            Elaboration word = Elaborate("module t;\n    logic [3:0] m [4];\n    initial assign m[1] = 4'd1;\nendmodule");
            Diagnostic said = word.Diagnostics.FirstOrDefault(d => d.Code == ElaborationCodes.BadLValue);
            Assert.IsNotNull(said, "a word of an array, as ModelSim refuses one: " + string.Join("\n", word.Diagnostics));
            StringAssert.Contains("m[1] is a word of an array", said.Message);

            Elaboration function = Elaborate(
                "module t;\n    logic y, q;\n    function logic f(input logic a); assign q = a; return a; endfunction\n    assign y = f(1'b1);\nendmodule");
            Diagnostic refused = function.Diagnostics.FirstOrDefault(d => d.Code == ElaborationCodes.BadCall);
            Assert.IsNotNull(refused, string.Join("\n", function.Diagnostics));
            StringAssert.Contains("which a function cannot make. To give a variable a value there, write the assignment alone", refused.Message);
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
        public void AConcurrentOrDeferredAssertion_IsRefusedAsItIsRead(string items)
        {
            SyntaxTree tree = SyntaxReader.ReadText("module t;\n    logic a;\n    " + items + "\nendmodule", "test.sv");
            Assert.IsTrue(tree.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error && d.Code == DiagnosticCodes.Unsupported), string.Join("\n", tree.Diagnostics));
        }
    }
}
