using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BitSorter.Sim;
using BitSorter.SystemVerilog;
using BitSorter.SystemVerilog.Syntax;
using BitSorter.Ci;
using NUnit.Framework;

namespace BitSorter.Board.Tests
{
    /// <summary>
    /// Processes opened into cells (Part 15h-2): an <c>if</c> a mux, a <c>case</c> a chain of them, a loop with constant bounds
    /// unrolled, a function opened where it is called, an <c>always_ff</c> a register -- each held to the engine as every
    /// netlist is (<see cref="RtlNetlistTests.HoldToTheEngine"/>), registers at their clock's edges included -- and what is no
    /// circuit a box that says why.
    /// </summary>
    public class RtlProcessTests
    {
        private static Design Elaborate(string source, string top)
        {
            SyntaxTree tree = SyntaxReader.ReadText(source, top + ".sv");
            Assert.IsFalse(tree.HasErrors, string.Join("; ", tree.Diagnostics));
            Elaboration e = Elaborator.Elaborate(tree, top);
            Assert.IsFalse(e.HasErrors, string.Join("\n", e.Diagnostics));
            return e.Design;
        }

        private static RtlNetlist Opened(Design design, string top, int seed, int vectors = 12)
        {
            Assert.Greater(RtlNetlistTests.HoldToTheEngine(design, seed, top, vectors), 0, "sanity: something was compared");
            RtlNetlist netlist = RtlExtractor.Of(design, top);
            Assert.IsTrue(netlist.Processes.All(p => p.Opened || p.Name.StartsWith("initial@", StringComparison.Ordinal)),
                "every process opened: " + string.Join("; ", netlist.Processes));
            return netlist;
        }

        /// <summary>A design that is a box: held to the engine all the same, though a box's outputs are read off it and leave nothing to compare.</summary>
        private static RtlNetlist Boxed(Design design, string top, int seed)
        {
            RtlNetlistTests.HoldToTheEngine(design, seed, top);
            return RtlExtractor.Of(design, top);
        }

        private static RtlCell Box(RtlNetlist netlist) => netlist.Cells.Single(c => c.Kind == RtlKind.Process);

        [Test]
        public void AnIf_IsAMuxOnItsCondition()
        {
            RtlNetlist netlist = Opened(Elaborate(
                "module pick (input logic s, input logic [3:0] a, b, output logic [3:0] y);\n" +
                "    always_comb\n" +
                "        if (s) y = a;\n" +
                "        else y = b;\n" +
                "endmodule\n", "pick"), "pick", 1);

            Assert.AreEqual(0, netlist.Count(RtlKind.Process), "opened, not a box");
            RtlCell mux = netlist.Cells.Single(c => c.Kind == RtlKind.Mux);
            Assert.AreEqual(netlist.NetNamed("a"), mux.Inputs[1], "a when s holds");
            Assert.AreEqual(netlist.NetNamed("b"), mux.Inputs[2], "b when it does not");
        }

        [Test]
        public void ACase_IsAMuxForEachLabel_OnItsMatch()
        {
            RtlNetlist netlist = Opened(Elaborate(
                "module alu (input logic [1:0] op, input logic [3:0] a, b, output logic [3:0] y);\n" +
                "    always_comb\n" +
                "        case (op)\n" +
                "            2'b00: y = a + b;\n" +
                "            2'b01: y = a - b;\n" +
                "            2'b10: y = a & b;\n" +
                "            default: y = a | b;\n" +
                "        endcase\n" +
                "endmodule\n", "alu"), "alu", 2);

            Assert.AreEqual(0, netlist.Count(RtlKind.Process));
            Assert.AreEqual(3, netlist.Count(RtlKind.Match), "one a label; the default none");
            Assert.AreEqual(3, netlist.Count(RtlKind.Mux), "and a mux for each");
            Assert.AreEqual(4, netlist.Count(RtlKind.Binary), "+, -, & and |, once each");
        }

        /// <summary>
        /// A loop with constant bounds is run, its counter a constant each pass: <c>y[i]</c> written at its place, <c>a[i]</c> the
        /// bit itself -- four XORs and nothing to choose between, and y the bits they give it, nothing of its old value left.
        /// </summary>
        [Test]
        public void ALoopWithConstantBounds_Unrolls_EachPassItsOwnBits()
        {
            RtlNetlist netlist = Opened(Elaborate(
                "module mirror (input logic [3:0] a, b, output logic [3:0] y);\n" +
                "    always_comb\n" +
                "        for (int i = 0; i < 4; i++)\n" +
                "            y[i] = a[i] ^ b[3 - i];\n" +
                "endmodule\n", "mirror"), "mirror", 3);

            Assert.AreEqual(0, netlist.Count(RtlKind.Process));
            Assert.AreEqual(4, netlist.Count(RtlKind.Binary), "an XOR a pass");
            Assert.AreEqual(0, netlist.Count(RtlKind.Mux) + netlist.Count(RtlKind.Bit) + netlist.Count(RtlKind.Truth),
                "every choice and every place a constant");
            Assert.IsFalse(Reaches(netlist, netlist.NetNamed("y"), netlist.NetNamed("y")), "y written whole, from nothing of itself");
        }

        /// <summary>A ripple carry written in an always_comb, a bit at a time, is a ripple: each carry read the net the last pass gave it.</summary>
        [Test]
        public void ARippleWrittenABitAtATime_ReadsEachCarryWhereItWasMade()
        {
            RtlNetlist netlist = Opened(Elaborate(
                "module ripple (input logic [3:0] a, b, input logic cin, output logic [3:0] s, output logic cout);\n" +
                "    logic [4:0] c;\n" +
                "    always_comb begin\n" +
                "        c[0] = cin;\n" +
                "        for (int i = 0; i < 4; i++) begin\n" +
                "            s[i] = a[i] ^ b[i] ^ c[i];\n" +
                "            c[i + 1] = (a[i] & b[i]) | (c[i] & (a[i] ^ b[i]));\n" +
                "        end\n" +
                "        cout = c[4];\n" +
                "    end\n" +
                "endmodule\n", "ripple"), "ripple", 4);

            Assert.AreEqual(0, netlist.Count(RtlKind.Process));
            Assert.IsFalse(Reaches(netlist, netlist.NetNamed("c"), netlist.NetNamed("c")), "no loop through c: each carry is the last one's");
            Assert.IsTrue(Reaches(netlist, netlist.NetNamed("cin"), netlist.NetNamed("cout")), "the carry ripples from cin to cout");
        }

        /// <summary>A function is opened where it is called, in an assignment and in a process alike: no box for either.</summary>
        [Test]
        public void AFunction_IsOpenedWhereItIsCalled()
        {
            RtlNetlist netlist = Opened(Elaborate(
                "module parity (input logic [3:0] a, b, input logic c, output logic p, q);\n" +
                "    function automatic logic odd(input logic [3:0] v);\n" +
                "        logic r;\n" +
                "        r = 1'b0;\n" +
                "        for (int i = 0; i < 4; i++)\n" +
                "            r = r ^ v[i];\n" +
                "        return r;\n" +
                "    endfunction\n" +
                "    assign p = odd(a);\n" +
                "    always_comb q = odd(b) ^ c;\n" +
                "endmodule\n", "parity"), "parity", 5);

            Assert.AreEqual(0, netlist.Count(RtlKind.Process) + netlist.Count(RtlKind.Opaque), "opened, both calls");
            Assert.AreEqual(4 + 4 + 1, netlist.Count(RtlKind.Binary), "a chain of XORs a call, and q's own");
        }

        /// <summary>A recursion whose end the arguments fix is run to it: a constant, folded.</summary>
        [Test]
        public void ARecursionWithAConstantEnd_IsRunToIt()
        {
            RtlNetlist netlist = Opened(Elaborate(
                "module scale (input logic [7:0] a, output logic [15:0] y);\n" +
                "    function automatic int fact(input int n);\n" +
                "        if (n <= 1) return 1;\n" +
                "        return n * fact(n - 1);\n" +
                "    endfunction\n" +
                "    assign y = a * fact(4);\n" +
                "endmodule\n", "scale"), "scale", 6);

            Assert.AreEqual(0, netlist.Count(RtlKind.Opaque));
            RtlCell times = netlist.Cells.Single(c => c.Kind == RtlKind.Binary);
            RtlCell constant = netlist.Cells[netlist.Nets[times.Inputs[1]].Driver];
            Assert.AreEqual(RtlKind.Constant, constant.Kind, "fact(4) worked out");
            Assert.IsTrue(constant.Value.TryGetUInt64(out ulong value) && value == 24, "as 24");
        }

        /// <summary>One with no constant end has no fixed circuit: in an assignment the call is a box, and the engine works it out.</summary>
        [Test]
        public void ARecursionWithNoConstantEnd_IsABox()
        {
            RtlNetlist netlist = Boxed(Elaborate(
                "module count (input logic [7:0] a, output int y);\n" +
                "    function automatic int ones(input logic [7:0] v);\n" +
                "        if (v == 0) return 0;\n" +
                "        return v[0] + ones(v >> 1);\n" +
                "    endfunction\n" +
                "    assign y = ones(a);\n" +
                "endmodule\n", "count"), "count", 7);

            Assert.AreEqual(1, netlist.Count(RtlKind.Opaque), "the call, a box");
        }

        /// <summary>A static function that reads what its last call left is memory, not a circuit: the process calling it is a box, and says so.</summary>
        [Test]
        public void AStaticFunctionReadingWhatItKept_MakesItsProcessABox()
        {
            RtlNetlist netlist = Boxed(Elaborate(
                "module keep (input logic [3:0] a, output logic [3:0] y);\n" +
                "    function logic [3:0] total(input logic [3:0] v);\n" +
                "        logic [3:0] sum;\n" +
                "        sum = sum + v;\n" +
                "        return sum;\n" +
                "    endfunction\n" +
                "    always_comb y = total(a);\n" +
                "endmodule\n", "keep"), "keep", 8);

            StringAssert.Contains("before writing it", Box(netlist).Why);
        }

        /// <summary>
        /// A process that reads a signal it does not wait on goes stale when that signal changes alone, which no circuit does: a
        /// box, saying which.
        /// </summary>
        [Test]
        public void AProcessThatDoesNotWaitOnWhatItReads_IsABox_SayingWhich()
        {
            RtlNetlist netlist = Boxed(Elaborate(
                "module stale (input logic a, b, output logic y);\n" +
                "    always @(a) y = a & b;\n" +
                "endmodule\n", "stale"), "stale", 9);

            StringAssert.StartsWith("It reads b but does not wait on it", Box(netlist).Why);
        }

        /// <summary>
        /// An always_ff is a register: its D what the body gives it -- the reset, then the enable keeping its own value -- and
        /// its pins the edges it waits on, held to the engine at every clock edge.
        /// </summary>
        [Test]
        public void AnAlwaysFf_IsARegister_ItsDWhatTheBodyGivesIt()
        {
            RtlNetlist netlist = Opened(Elaborate(
                "module hold (input logic clk, rst_n, en, input logic [3:0] d, output logic [3:0] q);\n" +
                "    always_ff @(posedge clk or negedge rst_n)\n" +
                "        if (!rst_n) q <= 4'b0;\n" +
                "        else if (en) q <= d;\n" +
                "endmodule\n", "hold"), "hold", 10);

            RtlCell register = netlist.Cells.Single(c => c.Kind == RtlKind.Register);
            CollectionAssert.AreEqual(new[] { "D", "clk", "rst_n" }, register.InputNames);
            CollectionAssert.AreEqual(new[] { EdgeKind.Posedge, EdgeKind.Negedge }, register.Edges);
            Assert.AreEqual(1, register.Clock, "clk, the edge the body does not test; rst_n, tested, its reset");
            Assert.AreEqual(netlist.NetNamed("q"), register.Output, "its output is q");
            Assert.AreEqual(2, netlist.Count(RtlKind.Mux), "the reset's and the enable's");
            Assert.IsTrue(Reaches(netlist, netlist.NetNamed("q"), register.Inputs[0]), "the enable keeps q: q comes back into D");
        }

        /// <summary>A write at a place worked out as it runs is a demultiplexer, one cell: the bit sel names set, x in sel writing nothing.</summary>
        [Test]
        public void AWriteAtAPlaceWorkedOutAsItRuns_IsADemultiplexer()
        {
            RtlNetlist netlist = Opened(Elaborate(
                "module decode (input logic [2:0] sel, input logic d, output logic [7:0] y);\n" +
                "    always_comb begin\n" +
                "        y = 8'b0;\n" +
                "        y[sel] = d;\n" +
                "    end\n" +
                "endmodule\n", "decode"), "decode", 12);

            Assert.AreEqual(0, netlist.Count(RtlKind.Process));
            RtlCell put = netlist.Cells.Single(c => c.Kind == RtlKind.Put);
            Assert.IsTrue(Reaches(netlist, netlist.NetNamed("sel"), put.Inputs[2]), "put where sel says, sel widened for it");
        }

        /// <summary>An always_comb that leaves its output as it was on some path is a latch: its own value fed back.</summary>
        [Test]
        public void AnAlwaysCombThatKeepsAValue_FeedsItBack()
        {
            RtlNetlist netlist = Opened(Elaborate(
                "module latch (input logic en, input logic [3:0] d, output logic [3:0] q);\n" +
                "    always_comb\n" +
                "        if (en) q = d;\n" +
                "endmodule\n", "latch"), "latch", 11);

            Assert.AreEqual(0, netlist.Count(RtlKind.Process));
            Assert.IsTrue(Reaches(netlist, netlist.NetNamed("q"), netlist.NetNamed("q")), "q comes back to itself");
        }

        private static bool IsBox(RtlCell cell) => cell.Kind == RtlKind.Instance || cell.Kind == RtlKind.Process || cell.Kind == RtlKind.Register;

        // -----------------------------------------------------------------
        // What the review of #106 found
        // -----------------------------------------------------------------

        /// <summary>
        /// A case arm of several labels chooses that arm for each of them, wherever it stands and whatever the default -- in an
        /// always_comb and as a register's D. Joined by the shared splits alone, the path of two labels chose the wrong arm.
        /// </summary>
        [TestCase("2'b00: y = a; 2'b01, 2'b10: y = b; default: y = c;")]
        [TestCase("2'b00, 2'b11: y = a; 2'b01: y = b; default: y = c;")]
        [TestCase("default: y = c; 2'b01, 2'b10: y = b; 2'b00: y = a;")]
        [TestCase("2'b00: y = a; default: y = c; 2'b01, 2'b10: y = b;")]
        public void ACaseArmOfSeveralLabels_IsChosenForEachOfThem(string arms)
        {
            Opened(Elaborate(
                "module pick (input logic [1:0] op, input logic [3:0] a, b, c, output logic [3:0] y);\n" +
                "    always_comb\n" +
                "        case (op)\n" +
                "            " + arms + "\n" +
                "        endcase\n" +
                "endmodule\n", "pick"), "pick", 21, vectors: 48);

            Opened(Elaborate(
                "module held (input logic clk, input logic [1:0] op, input logic [3:0] a, b, c, output logic [3:0] y);\n" +
                "    always_ff @(posedge clk)\n" +
                "        case (op)\n" +
                "            " + arms.Replace(" = ", " <= ") + "\n" +
                "        endcase\n" +
                "endmodule\n", "held"), "held", 22, vectors: 48);
        }

        /// <summary>A function's early return, and a loop's break and continue beside an if that joins again, leave the right values.</summary>
        [Test]
        public void AnEarlyReturn_AndABreakAndAContinue_LeaveTheRightValues()
        {
            Opened(Elaborate(
                "module early (input logic a, b, input logic [3:0] p, q, output logic [3:0] y, z);\n" +
                "    function automatic logic [3:0] pick(input logic a, b, input logic [3:0] p, q);\n" +
                "        logic [3:0] x;\n" +
                "        if (a) x = p;\n" +
                "        else begin\n" +
                "            if (b) return q;\n" +
                "            x = ~p;\n" +
                "        end\n" +
                "        return x + 1;\n" +
                "    endfunction\n" +
                "    assign y = pick(a, b, p, q);\n" +
                "    always_comb z = pick(b, a, q, p);\n" +
                "endmodule\n", "early"), "early", 23, vectors: 48);

            Opened(Elaborate(
                "module leave (input logic [3:0] a, b, s, output logic [3:0] y);\n" +
                "    always_comb begin\n" +
                "        y = a;\n" +
                "        for (int i = 0; i < 3; i++) begin\n" +
                "            if (s[i]) y = y + b;\n" +
                "            else begin\n" +
                "                if (s[3]) break;\n" +
                "                y = y ^ b;\n" +
                "            end\n" +
                "            if (a[i]) continue;\n" +
                "            y = y - 1;\n" +
                "        end\n" +
                "    end\n" +
                "endmodule\n", "leave"), "leave", 24, vectors: 48);
        }

        /// <summary>
        /// Several processes each writing some of a signal's bits are each drawn, joined into the signal by their places: a
        /// generate loop's registers a bit each, and two always_comb a bit each with no loop through the signal.
        /// </summary>
        [Test]
        public void SeveralProcesses_EachWritingSomeBits_AreEachDrawn()
        {
            RtlNetlist shift = Opened(Elaborate(
                "module bits (input logic clk, input logic [3:0] d, output logic [3:0] q);\n" +
                "    for (genvar i = 0; i < 4; i++) begin : bit_\n" +
                "        always_ff @(posedge clk) q[i] <= d[i];\n" +
                "    end\n" +
                "endmodule\n", "bits"), "bits", 25);
            Assert.AreEqual(4, shift.Count(RtlKind.Register), "a register a bit");
            RtlCell join = shift.Cells.Single(c => c.Kind == RtlKind.Join && c.Name == "q");
            CollectionAssert.AreEquivalent(new[] { 0, 1, 2, 3 }, join.Lowests);

            RtlNetlist two = Opened(Elaborate(
                "module two (input logic a, b, c, output logic [1:0] y);\n" +
                "    always_comb y[0] = a & b;\n" +
                "    always_comb y[1] = b | c;\n" +
                "endmodule\n", "two"), "two", 26);
            Assert.IsFalse(Reaches(two, two.NetNamed("y"), two.NetNamed("y")), "no loop through y");
            Assert.AreEqual(2, two.Cells.Single(c => c.Kind == RtlKind.Join && c.Name == "y").Inputs.Count);
        }

        /// <summary>An initial written before the always_ff that also writes its signal leaves the register drawn, the box beside it.</summary>
        [Test]
        public void AnInitialBeforeItsAlwaysFf_LeavesTheRegister()
        {
            RtlNetlist netlist = Opened(Elaborate(
                "module count (input logic clk, output logic [3:0] q);\n" +
                "    initial q = 4'd0;\n" +
                "    always_ff @(posedge clk) q <= q + 1;\n" +
                "endmodule\n", "count"), "count", 27);
            RtlCell register = netlist.Cells.Single(c => c.Kind == RtlKind.Register);
            Assert.AreEqual(netlist.NetNamed("q"), register.Output, "the register drives q");
            Assert.AreEqual(1, netlist.Count(RtlKind.Process), "and the initial is a box beside it");
        }

        /// <summary>A pipeline written into an array a word at a time is a register a word.</summary>
        [Test]
        public void APipelineInAnArray_IsARegisterAWord()
        {
            RtlNetlist netlist = Opened(Elaborate(
                "module pipe (input logic clk, input logic [3:0] d, output logic [3:0] y);\n" +
                "    logic [3:0] st [0:2];\n" +
                "    always_ff @(posedge clk) begin\n" +
                "        st[0] <= d;\n" +
                "        st[1] <= st[0];\n" +
                "        st[2] <= st[1];\n" +
                "    end\n" +
                "    assign y = st[2];\n" +
                "endmodule\n", "pipe"), "pipe", 28, vectors: 24);
            Assert.AreEqual(3, netlist.Count(RtlKind.Register), "a register a word");
        }

        /// <summary>What sees an old value no circuit holds stays a box: a read of what it writes before writing it, a memory it does not wait on, and $time.</summary>
        [Test]
        public void WhatSeesAnOldValue_StaysABox()
        {
            StringAssert.Contains("before it writes it", Box(Boxed(Elaborate(
                "module late (input logic [3:0] a, output logic [3:0] y);\n" +
                "    logic [3:0] t;\n" +
                "    always_comb begin\n" +
                "        y = t;\n" +
                "        t = a;\n" +
                "    end\n" +
                "endmodule\n", "late"), "late", 29)).Why);

            RtlNetlist memory = Boxed(Elaborate(
                "module ram (input logic clk, we, input logic [1:0] wa, ra, input logic [3:0] wd, output logic [3:0] rd);\n" +
                "    logic [3:0] mem [0:3];\n" +
                "    always_ff @(posedge clk) if (we) mem[wa] <= wd;\n" +
                "    always @(ra) rd = mem[ra];\n" +
                "endmodule\n", "ram"), "ram", 30);
            StringAssert.Contains("reads mem but does not wait on it", memory.Processes.Single(p => p.Name.Contains("always@")).Why);

            StringAssert.Contains("$time", Box(Boxed(Elaborate(
                "module stamp (input logic clk, output logic [31:0] t);\n" +
                "    always_ff @(posedge clk) t <= $time;\n" +
                "endmodule\n", "stamp"), "stamp", 31)).Why);
        }

        /// <summary>A case's subject is one cell however many labels it is matched against.</summary>
        [Test]
        public void ACasesSubject_IsOneCell()
        {
            RtlNetlist netlist = Opened(Elaborate(
                "module sum (input logic [1:0] a, b, input logic [3:0] p, q, r, output logic [3:0] y);\n" +
                "    always_comb\n" +
                "        case (a + b)\n" +
                "            2'd0: y = p;\n" +
                "            2'd1: y = q;\n" +
                "            2'd2: y = r;\n" +
                "            default: y = 4'd0;\n" +
                "        endcase\n" +
                "endmodule\n", "sum"), "sum", 32, vectors: 48);
            Assert.AreEqual(1, netlist.Count(RtlKind.Binary), "a + b once");
        }

        /// <summary>A case of many labels -- a table in code -- is opened in time to draw it.</summary>
        [Test]
        public void ACaseOfManyLabels_IsOpenedQuickly()
        {
            var table = new System.Text.StringBuilder();
            for (int i = 0; i < 256; i++)
                table.Append("            8'd" + i + ": y = 8'd" + ((i * 37 + 11) & 255) + ";\n");
            var clock = System.Diagnostics.Stopwatch.StartNew();
            RtlNetlist netlist = Opened(Elaborate(
                "module rom (input logic [7:0] a, output logic [7:0] y);\n" +
                "    always_comb\n" +
                "        case (a)\n" + table +
                "            default: y = 8'd0;\n" +
                "        endcase\n" +
                "endmodule\n", "rom"), "rom", 33);
            clock.Stop();
            Assert.AreEqual(256, netlist.Count(RtlKind.Match));
            Assert.Less(clock.ElapsedMilliseconds, 3000, "a table of 256 opened in good time");
        }

        /// <summary>
        /// Random always_comb programs -- if and else, case arms of one label or several with a default or none, loops with
        /// break and continue, signed and not -- each opened and held to the engine. The joins are where an opening goes wrong,
        /// and a program a person writes finds only the ones they thought of.
        /// </summary>
        [Test]
        public void RandomPrograms_AreOpened_AndComputeWhatTheEngineDoes()
        {
            int tried = 0;
            for (int seed = 1; seed <= 240; seed++)
            {
                string source = RandomProgram(new Random(seed * 7919), signed: seed % 2 == 0);
                SyntaxTree tree = SyntaxReader.ReadText(source, "fz.sv");
                if (tree.HasErrors)
                    continue;
                Elaboration e = Elaborator.Elaborate(tree, "fz");
                if (e.HasErrors)
                    continue;
                tried++;
                RtlNetlist netlist = RtlExtractor.Of(e.Design, "fz");
                Assert.AreEqual(0, netlist.Count(RtlKind.Process), "opened:\n" + source + "\n" + string.Join("\n", netlist.Processes));
                RtlNetlistTests.HoldToTheEngine(e.Design, seed, "program " + seed + ":\n" + source, vectors: 24);
            }
            Assert.Greater(tried, 200, "sanity: the programs elaborate");
        }

        private static string RandomProgram(Random r, bool signed)
        {
            var text = new System.Text.StringBuilder();
            int loops = 0;
            string[] names = { "a", "b", "c", "d", "y", "z" };
            string Operand() => names[r.Next(names.Length)];
            string Value()
            {
                switch (r.Next(6))
                {
                    case 0: return Operand();
                    case 1: return Operand() + " + " + Operand();
                    case 2: return Operand() + " ^ " + Operand();
                    case 3: return "4'd" + r.Next(16);
                    case 4: return signed ? "(" + Operand() + " >>> 1)" : "~" + Operand();
                    default: return Operand() + " & " + Operand();
                }
            }
            string Condition()
            {
                string[] tests = { "s[0]", "s[1]", "s[2]", "s[3]", "a[0]", "b[1]", "(a > b)", "(c == d)", "(y < z)" };
                string c = tests[r.Next(tests.Length)];
                if (r.Next(4) == 0)
                    c = "!" + c;
                if (r.Next(5) == 0)
                    c += " && " + tests[r.Next(tests.Length)];
                return c;
            }
            void Block(int depth, string indent)
            {
                int n = r.Next(1, 4);
                for (int i = 0; i < n; i++)
                    Statement(depth, indent);
            }
            void Statement(int depth, string indent)
            {
                if (loops > 0 && r.Next(6) == 0)
                {
                    text.Append(indent + "if (" + Condition() + ") " + (r.Next(2) == 0 ? "break" : "continue") + ";\n");
                    return;
                }
                switch (depth >= 3 ? 0 : r.Next(7))
                {
                    case 1:
                    case 2:
                        text.Append(indent + "if (" + Condition() + ") begin\n");
                        Block(depth + 1, indent + "    ");
                        text.Append(indent + "end");
                        if (r.Next(2) == 0)
                        {
                            text.Append(" else begin\n");
                            Block(depth + 1, indent + "    ");
                            text.Append(indent + "end");
                        }
                        text.Append("\n");
                        break;
                    case 3:
                    case 4:
                    {
                        text.Append(indent + "case (s[1:0])\n");
                        var used = new List<int>();
                        int arms = r.Next(2, 4);
                        for (int arm = 0; arm < arms && used.Count < 4; arm++)
                        {
                            var labels = new List<string>();
                            int count = r.Next(2) == 0 ? 2 : 1;
                            for (int l = 0; l < count && used.Count < 4; l++)
                            {
                                int label;
                                do label = r.Next(4); while (used.Contains(label));
                                used.Add(label);
                                labels.Add("2'd" + label);
                            }
                            text.Append(indent + "    " + string.Join(", ", labels) + ": begin\n");
                            Block(depth + 1, indent + "        ");
                            text.Append(indent + "    end\n");
                        }
                        if (r.Next(2) == 0)
                        {
                            text.Append(indent + "    default: begin\n");
                            Block(depth + 1, indent + "        ");
                            text.Append(indent + "    end\n");
                        }
                        text.Append(indent + "endcase\n");
                        break;
                    }
                    case 5:
                        if (loops > 0)
                            goto default;
                        text.Append(indent + "for (int i = 0; i < " + r.Next(1, 4) + "; i++) begin\n");
                        loops++;
                        Block(depth + 1, indent + "    ");
                        loops--;
                        text.Append(indent + "end\n");
                        break;
                    default:
                        text.Append(indent + (r.Next(2) == 0 ? "y" : "z") + " = " + Value() + ";\n");
                        break;
                }
            }

            text.Append(signed
                ? "module fz (input logic signed [3:0] a, input logic [3:0] b, c, d, s, output logic signed [3:0] y, output bit [3:0] z);\n"
                : "module fz (input logic [3:0] a, b, c, d, s, output logic [3:0] y, z);\n");
            text.Append("    always_comb begin\n        y = a;\n        z = b;\n");
            int statements = r.Next(2, 5);
            for (int i = 0; i < statements; i++)
                Statement(0, "        ");
            text.Append("    end\nendmodule\n");
            return text.ToString();
        }

        /// <summary>Whether a net's value feeds <paramref name="to"/>, through the netlist's cells, a box's or a register's never.</summary>
        private static bool Reaches(RtlNetlist netlist, int from, int to)
        {
            var seen = new HashSet<int>();
            var stack = new Stack<int>();
            foreach (RtlCell cell in netlist.Cells)
                if (cell.Inputs.Contains(from) && !IsBox(cell))
                    stack.Push(cell.Output);
            while (stack.Count > 0)
            {
                int net = stack.Pop();
                if (net == to)
                    return true;
                if (!seen.Add(net))
                    continue;
                foreach (RtlCell cell in netlist.Cells)
                    if (cell.Inputs.Contains(net) && !IsBox(cell))
                        foreach (int output in cell.Outputs)
                            stack.Push(output);
            }
            return false;
        }

        /// <summary>
        /// Every process of every exercise's solution and of every corpus design is opened, but the ones listed: each initial,
        /// two testbench clocks, and one @* that calls a function reading what it does not wait on.
        /// </summary>
        [Test]
        public void EveryProcessThatDescribesACircuit_IsOpened()
        {
            var expected = new Dictionary<string, string>
            {
                ["behavioural/function_sensitivity.sv: function_sensitivity.always@16"] = "does not wait on it",
                ["behavioural/interfaces.sv: interfaces_tb.always@32"] = "A delay, #d",
                ["behavioural/multiplier.sv: multiplier_tb.always@139"] = "A delay, #d",
            };
            var found = new Dictionary<string, string>();
            int initials = 0, opened = 0;
            void Look(Design design, string where)
            {
                foreach (ScopeInfo scope in design.Scopes)
                {
                    if (scope.Kind != ScopeKind.Top && scope.Kind != ScopeKind.Instance)
                        continue;
                    RtlNetlist netlist = RtlExtractor.Of(design, scope.Path);
                    foreach (RtlCell cell in netlist.Cells.Where(c => c.Kind == RtlKind.Process))
                    {
                        if (cell.Name.StartsWith("initial@", StringComparison.Ordinal) || cell.Name.Contains(".initial@"))
                            initials++;
                        else
                            found[where + ": " + netlist.Module + "." + cell.Name] = cell.Why;
                    }
                    opened += netlist.Count(RtlKind.Register) + netlist.Count(RtlKind.Truth) + netlist.Count(RtlKind.Match);
                }
            }

            foreach (string id in ExerciseFiles.Ids())
            {
                Project solution = ExerciseFiles.Solution(ExerciseFiles.Load(id));
                Elaboration e = Elaborator.Elaborate(BoardCompiler.Compile(solution).Tree, solution.TopModule.Name);
                Look(e.Design, id);
            }
            foreach (string folder in new[] { "structural", "behavioural", "smoke" })
                foreach (string path in Directory.GetFiles(RepoRoot.Combine("Tools/ci/Oracle/corpus/" + folder), "*.sv"))
                {
                    var files = new MemorySourceFiles();
                    foreach (string other in Directory.GetFiles(Path.GetDirectoryName(path)))
                        files.Add(Path.GetFileName(other), File.ReadAllText(other));
                    SyntaxTree tree = SyntaxReader.ReadText(File.ReadAllText(path), Path.GetFileName(path), files);
                    foreach (ModuleDeclaration module in tree.Modules)
                    {
                        Elaboration e = Elaborator.Elaborate(tree, module.Name);
                        if (!e.HasErrors)
                            Look(e.Design, folder + "/" + Path.GetFileName(path));
                    }
                }

            CollectionAssert.AreEquivalent(expected.Keys, found.Keys,
                "the boxes:\n" + string.Join("\n", found.Select(f => f.Key + ": " + f.Value)));
            foreach (KeyValuePair<string, string> box in expected)
                StringAssert.Contains(box.Value, found[box.Key], box.Key);
            Assert.Greater(initials, 0, "sanity: the testbenches' initial blocks were seen, each a box");
            Assert.Greater(opened, 100, "sanity: processes were opened");
        }
    }
}
