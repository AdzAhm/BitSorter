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
        /// <c>?:</c> works out only the choice its condition makes, and <c>&amp;&amp;</c> and <c>||</c> stop once the
        /// first operand decides (IEEE 1800, 11.4.11 and 11.3.5; the 16d sweep, engine-2): a recursion whose base case is a
        /// <c>?:</c> ends there, and a function in the arm not chosen does not run -- its static count stays where it was.
        /// Every operand was worked out, so fact's last call went on calling down to the nesting guard, and fib's branched
        /// twice at every level. Icarus and ModelSim print these lines.
        /// </summary>
        [Test]
        public void ConditionalAndLogicalOperators_WorkOutOnlyWhatTheyNeed()
        {
            Simulator sim = Run(@"
module t;
    function automatic int fact(input int n);
        return (n <= 1) ? 1 : n * fact(n - 1);
    endfunction
    function automatic int fib(input int n);
        return n < 2 ? n : fib(n - 1) + fib(n - 2);
    endfunction
    function int bump(input int by);
        int count;
        count = count + by;
        return count;
    endfunction
    int a, b, c, d;
    initial begin
        $display(""%0d %0d"", fact(5), fib(10));
        a = 0 ? bump(1) : 7;
        b = (1 == 0) && bump(10);
        c = (1 == 1) || bump(100);
        d = bump(0);
        $display(""%0d %0d %0d %0d"", a, b, c, d);
    end
endmodule", 1);

            CollectionAssert.AreEqual(new[] { "120 55", "7 0 1 0" }, Printed(sim));
            CollectionAssert.IsEmpty(sim.History.Messages.Where(m => m.Severity == Severity.Error).Select(m => m.Text));
        }

        /// <summary>
        /// The same of a <c>?:</c> of reals, and of <c>$random(seed)</c> in an operand passed over (the review of the 16d engine
        /// batch). A real ?: was built without the context's signedness, so it worked out both arms and its recursion ran
        /// down to the nesting guard, saying so; and <c>$random</c>, worked out just before its statement, moved the seed on
        /// in an arm not chosen and in the right of a decided <c>&amp;&amp;</c> or <c>||</c>. Icarus 12 and ModelSim 2020.1 print
        /// these lines: the seed moves on only where the call is worked out -- in both arms of an x condition, too.
        /// </summary>
        [Test]
        public void ARealConditional_AndARandomInAnOperandPassedOver_WorkOutOnlyWhatTheyNeed()
        {
            Simulator sim = Run(@"
module t;
    int seed = 7, a, b, c, d, e, f, g, x;
    real r;
    logic on = 1, off = 0, unk = 1'bx;
    function automatic real pw(input int n);
        return (n == 0) ? 1.0 : 2.0 * pw(n - 1);
    endfunction
    initial begin
        a = off ? $random(seed) : 5;
        b = $random(seed);
        c = on ? 6 : $random(seed);
        d = (off && $random(seed)) ? 1 : 2;
        e = (on || $random(seed)) ? 3 : 4;
        f = on ? $random(seed) : 9;
        g = $random(seed);
        r = pw(5);
        $display(""a=%0d b=%0d c=%0d d=%0d e=%0d f=%0d g=%0d r=%0.1f"", a, b, c, d, e, f, g, r);
        seed = 7;
        x = unk ? $random(seed) : 1;
        $display(""x=%0d seed=%0d"", x, seed);
    end
endmodule", 1);

            CollectionAssert.AreEqual(new[]
            {
                "a=5 b=-2146999808 c=6 d=2 e=3 f=1181502348 g=760705370 r=32.0",
                "x=0 seed=483484",
            }, Printed(sim));
            CollectionAssert.IsEmpty(sim.History.Messages.Where(m => m.Severity == Severity.Error).Select(m => m.Text));
        }

        /// <summary>
        /// A call whose later argument comes back into the same call gives each call its own arguments (the 16d sweep,
        /// engine-1): Ackermann's <c>ack(m - 1, ack(m, n - 1))</c> works out its second argument through the same call, and
        /// one array of arguments for the call let the inner call put its m where the outer one's was -- ack(0, 3) ran for
        /// ack(1, 3), and 4 was printed for 5, with no word.
        /// </summary>
        [Test]
        public void ACallWhoseArgumentCallsBackIntoIt_KeepsItsOwnArguments()
        {
            Simulator sim = Run(@"
module t;
    function automatic int ack(input int m, input int n);
        if (m == 0) return n + 1;
        else if (n == 0) return ack(m - 1, 1);
        else return ack(m - 1, ack(m, n - 1));
    endfunction
    initial $display(""%0d %0d"", ack(2, 1), ack(2, 3));
endmodule", 1);

            CollectionAssert.AreEqual(new[] { "5 9" }, Printed(sim));
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
        /// A function that never ends stops the run, as a process that never waits does (the 16d sweep, engine-3): the step
        /// budget began again at every call and the run went on, so each change of its argument cost another ten million
        /// steps -- seconds in a browser, with no frame drawn -- and a testbench changing it a hundred times froze the page
        /// for minutes. Here the first call stops it, at t = 0, and the changes after never run it again.
        /// </summary>
        [Test]
        public void AFunctionThatNeverEnds_StopsTheRun_AsAProcessThatNeverWaitsDoes()
        {
            Elaboration e = Elaborate(@"
module t;
    integer a, y;
    function automatic integer spin(input integer n);
        while (1) n = n + 1;
        return n;
    endfunction
    assign y = spin(a);
    initial begin a = 1; #5 a = 2; #5 a = 3; end
endmodule");
            Assert.IsFalse(e.HasErrors, string.Join("\n", e.Diagnostics));
            var sim = new Simulator(e.Design, null, null);
            RunResult result = sim.RunTo(20);

            Assert.AreEqual(RunStatus.Stopped, result.Status, "the run stops: " + string.Join("\n", sim.History.Messages.Select(m => m.Text)));
            Assert.AreEqual(0, result.Time, "at the first call");
            Assert.AreEqual(1, sim.History.Messages.Count(m => m.Severity == Severity.Error && m.Text.Contains("steps in one call")));
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

        /// <summary>
        /// Everything an automatic function or task declares is made afresh as it is entered (IEEE 1800, 6.21): its own,
        /// with an initializer that may read its arguments, and a block's inside it, each time round its loop. Icarus 12
        /// and ModelSim print the line, and <c>corpus/behavioural/subroutines.sv</c> holds the same to both. BitSorter
        /// refused <c>int total = n * 2</c>, its initializer no constant, and gave a block's hits the last time round's
        /// value -- a block's variables were static wherever they were (the Part 16d sweep).
        /// </summary>
        [Test]
        public void AnAutomaticRoutine_MakesWhatItDeclaresAfresh_ItsBlocksEachTimeRound()
        {
            Elaboration e = Elaborate(@"
module t;
    function automatic int doubled_sum(input int n);
        int total = n * 2;
        for (int k = 0; k < 3; k++) begin
            int hits = 0;
            hits++;
            total += hits;
        end
        return total;
    endfunction

    task automatic tally(input int from, output int r);
        int base = from + 1;
        r = 0;
        for (int k = 0; k < 2; k++) begin
            int hits = base;
            hits++;
            r += hits;
        end
    endtask

    int t1, t2;
    initial begin
        tally(9, t1);
        tally(4, t2);
        $display(""automatic %0d %0d %0d %0d"", doubled_sum(4), doubled_sum(5), t1, t2);
    end
endmodule");
            Assert.IsEmpty(e.Diagnostics, string.Join("\n", e.Diagnostics));
            var sim = new Simulator(e.Design, null, null);
            sim.RunTo(1);
            CollectionAssert.AreEqual(new[] { "automatic 11 13 22 12" }, Printed(sim), "Icarus's and ModelSim's line");
        }

        /// <summary>
        /// A lifetime written on a variable is its own, whatever its routine's (IEEE 1800, 6.21): <c>automatic</c> in a
        /// static task starts each call afresh, and <c>static</c> in an automatic function or task keeps its value from
        /// one call to the next -- called in an expression or as a statement. ModelSim's line (probed); Icarus 12 reads no
        /// lifetime written on a variable. BitSorter dropped the word as it read it (the Part 16d sweep).
        /// </summary>
        [Test]
        public void ALifetimeWrittenOnAVariable_IsItsOwn_WhateverItsRoutinesIs()
        {
            Elaboration e = Elaborate(@"
module t;
    task bump(output int r);
        automatic int c = 0;
        c++;
        r = c;
    endtask

    function automatic int counted();
        static int calls = 0;
        int mine = 0;
        calls++;
        mine++;
        return calls * 10 + mine;
    endfunction

    task automatic counted_task(output int r);
        static int calls = 0;
        int mine = 0;
        calls++;
        mine++;
        r = calls * 10 + mine;
    endtask

    int r1, r2, r3, r4;
    initial begin
        bump(r1); bump(r2);
        counted_task(r3); counted_task(r4);
        $display(""%0d %0d %0d %0d %0d %0d"", r1, r2, counted(), counted(), r3, r4);
    end
endmodule");
            Assert.IsEmpty(e.Diagnostics, string.Join("\n", e.Diagnostics));
            var sim = new Simulator(e.Design, null, null);
            sim.RunTo(1);
            CollectionAssert.AreEqual(new[] { "1 1 11 21 11 21" }, Printed(sim), "ModelSim's line, probed");
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

        /// <summary>
        /// An automatic task gives each call variables of its own (IEEE 1800, 13.3.1; Ahmad's decision, 2026-11-03): two
        /// branches of a fork running it at once each keep theirs across their waits, where a static one's are shared -- the
        /// first call printed the second's id and wait. Icarus 12 and ModelSim print these lines; which branch's values the
        /// static task's two lines show is the start order of a fork's branches, ModelSim's here (Docs/oracle-notes.md).
        /// </summary>
        [Test]
        public void AnAutomaticTask_GivesEachCallItsOwnVariables_WhenAForkRunsItTwiceAtOnce()
        {
            Simulator sim = Run(@"
module t;
    task automatic hold(input int id, input int wait_for);
        int seen;
        seen = id * 10;
        #(wait_for);
        $display(""%0t id=%0d seen=%0d wait_for=%0d"", $time, id, seen, wait_for);
    endtask
    task static shared(input int id, input int wait_for);
        int seen;
        seen = id * 10;
        #(wait_for);
        $display(""%0t static id=%0d seen=%0d"", $time, id, seen);
    endtask
    initial begin
        fork
            hold(1, 5);
            hold(2, 3);
        join
        fork
            shared(1, 5);
            shared(2, 3);
        join
    end
endmodule", 100);

            CollectionAssert.AreEqual(new[]
            {
                "3 id=2 seen=20 wait_for=3",
                "5 id=1 seen=10 wait_for=5",
                "8 static id=2 seen=20",
                "10 static id=2 seen=20",
            }, Printed(sim));
        }

        /// <summary>
        /// An automatic task may call itself, each call with variables of its own, waiting as it goes (IEEE 1800, 13.3.1;
        /// Ahmad's decision, 2026-11-03): it was refused, since a task is written into its caller and written in place it
        /// would never end. Icarus 12 and ModelSim print 4 total=10. A static task calling itself is still refused.
        /// </summary>
        [Test]
        public void AnAutomaticTask_CallsItself_EachCallWithItsOwnVariables()
        {
            Elaboration e = Elaborate(@"
module t;
    task automatic count_down(input int n, output int total);
        int below;
        if (n == 0) total = 0;
        else begin
            #1 count_down(n - 1, below);
            total = below + n;
        end
    endtask
    int t;
    initial begin
        count_down(4, t);
        $display(""%0t total=%0d"", $time, t);
    end
endmodule");
            Assert.IsFalse(e.HasErrors, "the elaboration reported:\n" + string.Join("\n", e.Diagnostics));
            var sim = new Simulator(e.Design, null, null);
            sim.RunTo(100);

            CollectionAssert.AreEqual(new[] { "4 total=10" }, Printed(sim));
        }

        /// <summary>
        /// An automatic task that calls itself with no way out, waiting as it goes, stops the run once its calls are
        /// <see cref="Simulator.MaxCallDepth"/> deep -- each call keeps what it put aside, so the run would grow without end --
        /// and a disable of the task leaves every call of it under way.
        /// </summary>
        [Test]
        public void AnAutomaticTaskCallingItselfWithNoWayOut_StopsTheRun_AndADisableLeavesEveryCall()
        {
            Elaboration e = Elaborate(@"
module t;
    task automatic down(input int n);
        #1 down(n + 1);
    endtask
    initial down(0);
endmodule");
            Assert.IsFalse(e.HasErrors, string.Join("\n", e.Diagnostics));
            var sim = new Simulator(e.Design, null, null);
            Assert.AreEqual(RunStatus.Stopped, sim.RunTo(50_000).Status);
            StringAssert.Contains("down 10,000 deep", sim.History.Messages.Last().Text);

            Simulator disabled = Run(@"
module t;
    int depth = 0;
    task automatic deeper(input int n);
        depth = n;
        if (n == 3) disable deeper;
        #1 deeper(n + 1);
        $display(""never back at %0d"", n);
    endtask
    initial begin
        deeper(0);
        $display(""%0t out at depth %0d"", $time, depth);
    end
endmodule", 100);
            CollectionAssert.AreEqual(new[] { "3 out at depth 3" }, Printed(disabled));
        }

        /// <summary>
        /// A <c>disable</c> of a named block inside an automatic task that has called itself ends every call of the block --
        /// and every call made from inside one, which runs within it -- so only the outermost call inside it goes on, after
        /// it, with its own variables (IEEE 1800, 9.6.2). Icarus 12 and ModelSim 2020.1 print just these lines, the second
        /// task's middle call outside the block among them (corpus/behavioural/automatic_tasks.sv). Only the innermost call
        /// left the block, and every call above it went on inside it (the review of the 16d engine batch).
        /// </summary>
        [Test]
        public void ADisableOfABlockInsideACallingTask_EndsEveryCallInsideIt_TheOutermostGoingOnAfterIt()
        {
            Simulator sim = Run(@"
module t;
    int depth = 0, mid = 0;
    task automatic r(input int n);
        begin : body
            depth = n;
            if (n == 2) disable body;
            #1 r(n + 1);
            $display(""%0t back at %0d"", $time, n);
        end
        $display(""%0t after body at %0d"", $time, n);
    endtask
    task automatic s(input int n);
        if (n == 1) begin
            #1 s(n + 1);
            $display(""%0t s back at %0d"", $time, n);
        end
        else begin : held
            mid = n;
            if (n == 2) disable held;
            #1 s(n + 1);
            $display(""%0t s held back at %0d"", $time, n);
        end
        $display(""%0t s after at %0d"", $time, n);
    endtask
    initial begin
        r(0);
        $display(""%0t out depth=%0d"", $time, depth);
        #10 s(0);
        $display(""%0t s out mid=%0d"", $time, mid);
    end
endmodule", 100);

            CollectionAssert.AreEqual(new[]
            {
                "2 after body at 0",
                "2 out depth=2",
                "14 s after at 0",
                "14 s out mid=2",
            }, Printed(sim));
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

        /// <summary>The tasks t0 to t<paramref name="last"/>, each calling the one below <paramref name="fanOut"/> times, and an initial calling the last.</summary>
        private static string Doubling(int last, int fanOut, string body)
        {
            var text = new System.Text.StringBuilder($"module t; reg x = 1'b0; task t0; {body} endtask\n");
            for (int i = 1; i <= last; i++)
                text.Append($"task t{i}; {string.Concat(Enumerable.Repeat($"t{i - 1}; ", fanOut))}endtask\n");
            return text.Append($"initial t{last}; endmodule").ToString();
        }

        /// <summary>
        /// A task is written in place wherever it is called, so tasks each calling the one below twice write two to the power of
        /// their number of copies: from a few hundred bytes, gigabytes and minutes, then out of memory, with nothing said (the
        /// Part 16d sweep). The whole design's processes are held to a number of instructions between them (Elaborator.MaxInstructions),
        /// every task's call one, written in place or not -- an empty body too.
        /// </summary>
        [TestCase("x = ~x;")]
        [TestCase("")]
        public void TasksCallingTasks_AreHeldToTheDesignsInstructions(string body)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            Elaboration e = Elaborate(Doubling(19, 2, body));
            Assert.Less(watch.Elapsed.TotalSeconds, 20, "stopped where it passes, not after");
            Diagnostic said = e.Diagnostics.Single(d => d.Code == ElaborationCodes.TooLarge);
            StringAssert.Contains("instructions", said.Message);
            Assert.IsNull(e.Design);
        }

        /// <summary>
        /// A generate loop's rounds each compile its process again, and are held to the same instructions: 2,048 rounds of a
        /// 300-line always, some 614,000 instructions, were built before the limit. Added with the limit's fix rather than ahead
        /// of it; run against that fix's parent, it fails there on the count of refusals, 0 (the review of the Part 16d sweep).
        /// </summary>
        [Test]
        public void AProcessInAGenerateLoop_IsHeldToTheDesignsInstructions()
        {
            string body = string.Concat(Enumerable.Repeat("y[i] = ~y[i]; ", 300));
            Elaboration e = Elaborate("module t (input logic [2047:0] a); logic [2047:0] y; for (genvar i = 0; i < 2048; i++) begin : g " +
                $"always @(a[i]) begin {body}end end endmodule");
            Diagnostic[] tooLarge = e.Diagnostics.Where(d => d.Code == ElaborationCodes.TooLarge).ToArray();
            Assert.AreEqual(1, tooLarge.Length, string.Join("\n", e.Diagnostics));
            StringAssert.Contains("instructions", tooLarge[0].Message);
            Assert.IsNull(e.Design);
        }

        /// <summary>The pair of the refusal: the same tasks, a thousand copies deep, are written in and run.</summary>
        [Test]
        public void TasksCallingTasks_AThousandCopies_AreWrittenInAndRun()
        {
            Simulator sim = Run(Doubling(10, 2, "x = ~x;"), 10);
            Assert.AreEqual("0", Now(sim, "x"), "1,024 turns over, an even number");
        }
    }
}
