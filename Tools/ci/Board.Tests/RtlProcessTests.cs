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

        private static RtlNetlist Opened(Design design, string top, int seed)
        {
            Assert.Greater(RtlNetlistTests.HoldToTheEngine(design, seed, top), 0, "sanity: something was compared");
            return RtlExtractor.Of(design, top);
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
