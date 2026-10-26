using System.Linq;
using BitSorter.SystemVerilog;
using BitSorter.SystemVerilog.Syntax;
using NUnit.Framework;

namespace BitSorter.Sim.Tests
{
    /// <summary>
    /// Functions and tasks (Part L2): a function called inside an expression runs where the expression is
    /// evaluated; a task, or a function called as a statement, is written into the calling process. The
    /// oracle compares <c>corpus/behavioural/subroutines.sv</c> and <c>function_sensitivity.sv</c> with Icarus;
    /// these hold each rule on its own.
    /// </summary>
    public class SubroutineTests
    {
        private static Elaboration Elaborate(string text)
        {
            SyntaxTree tree = SyntaxReader.ReadText(text, "test.sv");
            Assert.IsEmpty(tree.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error), "the read reported:\n" + string.Join("\n", tree.Diagnostics));
            return Elaborator.Elaborate(tree, null);
        }

        private static Simulator Run(string text, long until)
        {
            Elaboration e = Elaborate(text);
            Assert.IsFalse(e.HasErrors, "the elaboration reported:\n" + string.Join("\n", e.Diagnostics));
            var sim = new Simulator(e.Design, null, null);
            sim.RunTo(until);
            return sim;
        }

        private static string Now(Simulator sim, string name) => sim.Value(sim.Design.Find(sim.Design.Name + "." + name)).Format(Radix.Binary);

        private static string[] Changes(Simulator sim, string name) =>
            Probes.Settled(sim.History, sim.Design.Find(sim.Design.Name + "." + name));

        private static string[] Printed(Simulator sim) =>
            sim.History.Messages.Where(m => m.Severity == Severity.Info && !m.Text.StartsWith("$")).Select(m => m.Text).ToArray();

        // -----------------------------------------------------------------
        // Functions inside expressions
        // -----------------------------------------------------------------

        [Test]
        public void AFunctionInAnAssign_IsWorkedOutAgain_WhenItsArgumentChanges()
        {
            Simulator sim = Run(@"
module t;
    logic [3:0] a, y;
    function logic [3:0] twice(input logic [3:0] x);
        return x << 1;
    endfunction
    assign y = twice(a) + 1;
    initial begin a = 3; #5 a = 6; end
endmodule", 10);

            CollectionAssert.AreEqual(new[] { "0 0111", "5 1101" }, Changes(sim, "y"));
        }

        [Test]
        public void TheResult_IsGivenByReturn_OrByTheFunctionsOwnName_AndOldStyleArgumentsWork()
        {
            Simulator sim = Run(@"
module t;
    logic [7:0] a = 8'b1011_0001;
    logic p, q;
    function odd;
        input [7:0] v;
        odd = ^v;
    endfunction
    function logic even(input logic [7:0] v);
        return ~^v;
    endfunction
    assign p = odd(a);
    assign q = even(a);
endmodule", 1);

            Assert.AreEqual("0", Now(sim, "p"), "four ones: even");
            Assert.AreEqual("1", Now(sim, "q"));
        }

        [Test]
        public void ALoopWithLocals_AnEarlyReturn_AndADefaultArgument()
        {
            Simulator sim = Run(@"
module t;
    logic [7:0] a = 8'b0010_1100;
    logic [3:0] ones, first, scaled;
    function automatic logic [3:0] count(input logic [7:0] v);
        logic [3:0] n = 0;
        for (int i = 0; i < 8; i++) n += v[i];
        return n;
    endfunction
    function automatic logic [3:0] lowest_one(input logic [7:0] v);
        for (int i = 0; i < 8; i++)
            if (v[i]) return i;
        return 4'hF;
    endfunction
    function automatic logic [3:0] times(input logic [3:0] v, input int by = 3);
        return v * by;
    endfunction
    always_comb ones = count(a);
    always_comb first = lowest_one(a);
    assign scaled = times(4'd2);
endmodule", 1);

            Assert.AreEqual("0011", Now(sim, "ones"));
            Assert.AreEqual("0010", Now(sim, "first"), "the loop left by its return at bit 2");
            Assert.AreEqual("0110", Now(sim, "scaled"), "the default, 3, taken for the argument left out");
        }

        [Test]
        public void AnAutomaticFunction_CallsItself_WithAFrameForEachCall()
        {
            Simulator sim = Run(@"
module t;
    int f, g;
    function automatic int factorial(input int n);
        if (n <= 1) return 1;
        return n * factorial(n - 1);
    endfunction
    function automatic int fib(input int n);
        if (n < 2) return n;
        return fib(n - 1) + fib(n - 2);
    endfunction
    initial begin
        f = factorial(6);
        g = fib(12);
        $display(""%0d %0d"", f, g);
    end
endmodule", 1);

            CollectionAssert.AreEqual(new[] { "720 144" }, Printed(sim));
        }

        /// <summary>
        /// A call runs on the machine's own stack, so a function that calls itself with no way out would overflow it --
        /// which nothing can catch: the editor, a desktop build or a browser's page would end there (Part 13f). So calls nest
        /// a hundred deep at most, and one that would go a hundred and fifty is stopped, and the run says why.
        /// </summary>
        [Test]
        public void AFunctionCalledTooDeep_IsStopped_AndTheRunSaysWhy()
        {
            Simulator sim = Run(@"
module t;
    integer d;
    function automatic integer down(input integer n);
        if (n == 0) return 0;
        return 1 + down(n - 1);
    endfunction
    initial d = down(150);
endmodule", 1);

            Assert.IsTrue(sim.History.Messages.Any(m => m.Severity == Severity.Error && m.Text.Contains(" deep, each call inside the last")),
                "the run says the call was stopped: " + string.Join("\n", sim.History.Messages.Select(m => m.Text)));
        }

        /// <summary>
        /// The slips that would have ended the process: a function with no base case, and two that call each other forever
        /// -- the nesting counted across both. Each gives x, the run carries on, and says why once, however often it is called.
        /// </summary>
        [TestCase("function automatic integer f(input integer n); return f(n + 1); endfunction\n    assign y = f(a);", "f was called ")]
        [TestCase("function automatic integer f(input integer n); return g(n); endfunction\n    function automatic integer g(input integer n); return f(n); endfunction\n    assign y = f(a);", " deep, each call inside the last")]
        public void AFunctionWithNoWayOut_GivesX_AndTheRunSaysWhyOnce(string functions, string said)
        {
            Simulator sim = Run(@"
module t;
    integer a, y;
    " + functions + @"
    initial begin a = 1; #5 a = 2; #5 a = 3; end
endmodule", 20);

            Assert.AreEqual(new string('x', 32), Now(sim, "y"), "the call past the limit gave x");
            Assert.AreEqual(1, sim.History.Messages.Count(m => m.Severity == Severity.Error && m.Text.Contains(said)),
                "said once, though called three times: " + string.Join("\n", sim.History.Messages.Select(m => m.Text)));
        }

        /// <summary>A loop in a function that never ends stops at the step limit, as it always did -- and now the run says so.</summary>
        [Test]
        public void AFunctionLoopingForever_IsStopped_AndTheRunSaysWhy()
        {
            Simulator sim = Run(@"
module t;
    integer y;
    function automatic integer spin(input integer n);
        while (1) n = n + 1;
        return n;
    endfunction
    initial y = spin(0);
endmodule", 1);

            Assert.IsTrue(sim.History.Messages.Any(m => m.Severity == Severity.Error && m.Text.Contains("steps in one call")),
                string.Join("\n", sim.History.Messages.Select(m => m.Text)));
        }

        /// <summary>
        /// Calls are held to a stack's worth of frames, not a count of calls (Part 13f review): each operator a call nests is
        /// an evaluation a frame deeper, so a recursion whose body nests 190 of them overflowed a megabyte's stack within a
        /// hundred calls -- the process ending, which nothing can catch. Run on a thread of a megabyte, the smallest a host
        /// gives, it must stop with a message and leave the thread standing.
        /// </summary>
        [Test]
        public void ARecursionWithADeepBody_StopsWithinAStacksWorth_OnASmallStack()
        {
            string body = string.Concat(Enumerable.Repeat("1 + (", 190)) + "f(n + 1)" + new string(')', 190);
            string text = "module t;\n    integer y;\n    function automatic integer f(input integer n); return " + body + "; endfunction\n    initial y = f(0);\nendmodule";
            Simulator sim = null;
            System.Exception thrown = null;
            var thread = new System.Threading.Thread(() =>
            {
                try { sim = Run(text, 1); }
                catch (System.Exception e) { thrown = e; }
            }, 1 << 20);
            thread.Start();
            thread.Join();

            Assert.IsNull(thrown, thrown?.ToString());
            Assert.IsTrue(sim.History.Messages.Any(m => m.Severity == Severity.Error && m.Text.Contains(" deep, each call inside the last")),
                string.Join("\n", sim.History.Messages.Select(m => m.Text)));
        }

        /// <summary>
        /// A call stopped where only the postponed region reaches it -- a <c>$strobe</c> -- is said by that run, at its time;
        /// and never by the next run, of another design, as it once was (Part 13f review).
        /// </summary>
        [Test]
        public void ACallStoppedInAStrobe_IsSaidByItsRun_AndNeverByTheNext()
        {
            Simulator strobing = Run(@"
module t;
    function automatic integer f(input integer n); return f(n + 1); endfunction
    initial $strobe(""%0d"", f(0));
endmodule", 10);

            var said = strobing.History.Messages.Single(m => m.Severity == Severity.Error);
            StringAssert.StartsWith("At t = 0 ", said.Text);
            StringAssert.Contains(" deep, each call inside the last", said.Text);

            Simulator next = Run("module u;\n    logic a = 1;\nendmodule", 10);
            Assert.IsEmpty(next.History.Messages.Where(m => m.Severity == Severity.Error), "nothing of the last run's");
        }

        [Test]
        public void AStaticFunction_KeepsItsLocal_AndAnAutomaticOneStartsAfresh()
        {
            Simulator sim = Run(@"
module t;
    function int kept();
        int n;
        n = n + 1;
        return n;
    endfunction
    function automatic int fresh();
        int n;
        n = n + 1;
        return n;
    endfunction
    initial $display(""%0d %0d %0d / %0d %0d"", kept(), kept(), kept(), fresh(), fresh());
endmodule", 1);

            CollectionAssert.AreEqual(new[] { "1 2 3 / 1 1" }, Printed(sim));
        }

        [Test]
        public void AFunctionCallingAFunction_AndOneGivingAnInt()
        {
            Simulator sim = Run(@"
module t;
    logic [7:0] a = 8'd200;
    logic [7:0] y;
    int signed_sum;
    function automatic logic [7:0] inc(input logic [7:0] v); return v + 1; endfunction
    function automatic logic [7:0] inc2(input logic [7:0] v); return inc(inc(v)); endfunction
    function automatic int minus(input int x); return -x; endfunction
    assign y = inc2(a);
    initial #1 signed_sum = minus(5) + 2;
endmodule", 2);

            Assert.AreEqual("11001010", Now(sim, "y"), "202");
            Assert.AreEqual(new string('1', 30) + "01", Now(sim, "signed_sum"), "-3, an int's 32 bits");
        }

        // -----------------------------------------------------------------
        // Tasks, and functions called as statements
        // -----------------------------------------------------------------

        [Test]
        public void ATask_WaitsOnTheCallersTime_AndGivesItsOutputsBack()
        {
            Simulator sim = Run(@"
module t;
    logic clk = 0;
    logic [3:0] bus;
    logic [3:0] seen;
    logic [3:0] count = 0;
    always #5 clk = ~clk;
    task automatic put(input logic [3:0] value, output logic [3:0] back, inout logic [3:0] tally);
        @(posedge clk);
        bus = value;
        #1 back = bus + 1;
        tally = tally + 1;
    endtask
    initial begin
        put(4'd3, seen, count);
        $display(""%0t %0d %0d"", $time, seen, count);
        put(4'd9, seen, count);
        $display(""%0t %0d %0d"", $time, seen, count);
    end
endmodule", 40);

            CollectionAssert.AreEqual(new[] { "6 4 1", "16 10 2" }, Printed(sim));
            CollectionAssert.AreEqual(new[] { "0 xxxx", "5 0011", "15 1001" }, Changes(sim, "bus"), "a task writes the module's signals");
        }

        [Test]
        public void AVoidFunction_CalledAsAStatement_Prints_AndAValueCalledSoIsDropped()
        {
            Simulator sim = Run(@"
module t;
    logic [3:0] a = 4'd5;
    function void show(input logic [3:0] v);
        $display(""v=%0d"", v);
    endfunction
    function int bump(input int v);
        return v + 1;
    endfunction
    initial begin
        show(a);
        bump(1);
        show(a + 1);
    end
endmodule", 1);

            CollectionAssert.AreEqual(new[] { "v=5", "v=6" }, Printed(sim));
        }

        [Test]
        public void ATaskWithANamedBlock_IsCalledAgain_AndFromAnotherProcess_SharingTheBlocksStatics()
        {
            // Every call writes the same task in again, and with it the same named block: its name and its
            // variables are the ones the first call made, as a task's statics are.
            Simulator sim = Run(@"
module t;
    int n = 0;
    task bump;
        begin : step
            int seen;
            n++;
            seen = n;
            $display(""%0d"", seen);
        end
    endtask
    initial begin
        bump;
        bump;
        #1 bump;
    end
    initial #2 bump;
endmodule", 5);

            CollectionAssert.AreEqual(new[] { "1", "2", "3", "4" }, Printed(sim));
        }

        [Test]
        public void ATaskInAnAlwaysComb_IsPartOfIt_AndWhatItReadsWakesIt()
        {
            Simulator sim = Run(@"
module t;
    logic a, b, y;
    task set_y(input logic v);
        y = v & b;
    endtask
    always_comb set_y(a);
    initial begin a = 1; b = 0; #5 b = 1; #5 a = 0; end
endmodule", 20);

            CollectionAssert.AreEqual(new[] { "0 0", "5 1", "10 0" }, Changes(sim, "y"));
        }

        // -----------------------------------------------------------------
        // What is refused, and why
        // -----------------------------------------------------------------

        [TestCase("module t; logic a, y; function logic f(input logic x); f <= x; endfunction assign y = f(a); endmodule", "nonblocking")]
        [TestCase("module t; logic a, y; function logic f(input logic x); @(x) f = x; endfunction assign y = f(a); endmodule", "waits")]
        [TestCase("module t; logic a, y, z; function logic f(input logic x); z = x; f = x; endfunction assign y = f(a); endmodule", "which is not its own")]
        [TestCase("module t; logic a, y, z; function logic f(input logic x, output logic o); o = x; f = x; endfunction assign y = f(a, z); endmodule", "output argument")]
        [TestCase("module t; logic a, y; function logic f(input logic x); f = x; endfunction assign y = f(a, a); endmodule", "takes 1 argument")]
        [TestCase("module t; logic y; function logic f(input logic x); f = x; endfunction assign y = f(); endmodule", "needs a value for x")]
        [TestCase("module t; logic y; function void f(input logic x); endfunction assign y = f(1); endmodule", "void function")]
        [TestCase("module t; task w(input logic x); return x; endtask initial w(1); endmodule", "its return takes none")]
        [TestCase("module t; logic y; function logic f(input logic x); return; endfunction assign y = f(1); endmodule", "its return needs one")]
        [TestCase("module t; task w(input logic x); w(x); endtask initial w(1); endmodule", "calls itself")]
        [TestCase("module t; initial return; endmodule", "return belongs inside")]
        [TestCase("module t; function int f(input int x); return x; endfunction localparam P = f(1); endmodule", "before the run")]
        [TestCase("module t; logic y; function logic f(input logic x); $display(x); return x; endfunction assign y = f(1); endmodule", "prints")]
        public void WhatCannotRun_IsRefused_SayingWhy(string text, string said)
        {
            Elaboration e = Elaborate(text);
            Assert.IsTrue(e.HasErrors, "it should have been refused");
            StringAssert.Contains(said, string.Join("\n", e.Diagnostics.Select(d => d.Message)));
        }
    }
}
