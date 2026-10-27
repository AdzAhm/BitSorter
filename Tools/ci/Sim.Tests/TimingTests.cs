using System.Collections.Generic;
using System.Linq;
using BitSorter.SystemVerilog.Syntax;
using NUnit.Framework;

namespace BitSorter.Sim.Tests
{
    /// <summary>
    /// Path delays and static timing (Part 14a), each worked out by hand by the course's rule: a gate adds its Tpdlh
    /// (written first, <c>#(rise, fall)</c>) where its output rises and its Tpdhl where it falls, a change flips at NOT,
    /// NAND and NOR, and XOR passes it either way. Every circuit here is written for these tests.
    /// </summary>
    public class TimingTests
    {
        private static TimingGraph Graph(string text, IReadOnlyDictionary<string, RegisterTiming> registers = null)
        {
            SyntaxTree tree = SyntaxReader.ReadText(text, "test.sv");
            Assert.IsEmpty(tree.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error), string.Join("\n", tree.Diagnostics));
            Elaboration e = Elaborator.Elaborate(tree, null);
            Assert.IsFalse(e.HasErrors, string.Join("\n", e.Diagnostics));
            return TimingGraph.Of(e.Design, registers);
        }

        private static string Name(TimingGraph graph, int node) => graph.NameOf(node);

        private static string[] Gates(TimingGraph graph, IEnumerable<TimingArc> arcs) => arcs.Where(a => !a.IsWire).Select(a => a.Through.Substring(a.Through.LastIndexOf('.') + 1)).ToArray();

        /// <summary>a rises: g1 falls (3), g2 rises (2), g3 falls (3), 8. a falls: 2 + 3 + 2, 7.</summary>
        [Test]
        public void AnInverterChain_AddsTpdhlWhereAGateFalls_AndTpdlhWhereItRises()
        {
            TimingGraph graph = Graph(@"
module chain (input logic a, output logic y);
    logic b, c;
    not #(2, 3) g1 (b, a);
    not #(2, 3) g2 (c, b);
    not #(2, 3) g3 (y, c);
endmodule");

            PathReport report = PathDelays.Find(graph);

            TimingPath path = report.Paths.Single();
            Assert.AreEqual(8, path.Rising);
            Assert.AreEqual(7, path.Falling);
            CollectionAssert.AreEqual(new[] { "g1", "g2", "g3" }, Gates(graph, path.Steps));
        }

        /// <summary>
        /// A mux of gates. a and b each reach y through an AND and the OR: 2 + 4 rising, 3 + 5 falling -- and so does s
        /// through a1, the same gates at the same delays: three identical paths, one counted three times. s through the NOT
        /// is the slowest: s rising, n falls (1), a0 falls (3), o falls (5), 9.
        /// </summary>
        [Test]
        public void AMuxOfGates_FindsEveryPath_GroupsTheIdentical_AndRanksTheWorstFirst()
        {
            TimingGraph graph = Graph(@"
module mux (input logic a, b, s, output logic y);
    logic sn, t0, t1;
    not #(1, 1) n (sn, s);
    and #(2, 3) a0 (t0, a, sn);
    and #(2, 3) a1 (t1, b, s);
    or  #(4, 5) o (y, t0, t1);
endmodule");

            PathReport report = PathDelays.Find(graph);

            Assert.AreEqual(4, report.Found);
            Assert.AreEqual(2, report.Paths.Count);
            TimingPath worst = report.Worst;
            Assert.AreEqual("mux.s", Name(graph, worst.From));
            Assert.AreEqual(9, worst.Rising);
            Assert.AreEqual(7, worst.Falling);
            CollectionAssert.AreEqual(new[] { "n", "a0", "o" }, Gates(graph, worst.Steps));

            TimingPath same = report.Paths[1];
            Assert.AreEqual(3, same.Count, "a, b and s through a1");
            Assert.AreEqual((6, 8), (same.Rising, same.Falling));
            CollectionAssert.AreEquivalent(new[] { "mux.a", "mux.b", "mux.s" }, same.Alike.Select(e => Name(graph, e.From)));
        }

        /// <summary>
        /// XOR passes a change either way, so both are followed and the later kept: a rising, x1 rises at 3 or falls at 4,
        /// and x2 from the later, 4, rises at 7 or falls at 8 -- 8, whichever way a went.
        /// </summary>
        [Test]
        public void AnXor_PassesAChangeEitherWay_SoBothAreFollowed()
        {
            TimingGraph graph = Graph(@"
module sum (input logic a, b, c, output logic s);
    logic h;
    xor #(3, 4) x1 (h, a, b);
    xor #(3, 4) x2 (s, h, c);
endmodule");

            PathReport report = PathDelays.Find(graph);

            TimingPath fromA = report.Paths.First(p => p.Alike.Any(e => Name(graph, e.From) == "sum.a"));
            Assert.AreEqual((8, 8), (fromA.Rising, fromA.Falling));
            Assert.AreEqual(2, fromA.Count, "a and b alike");
            TimingPath fromC = report.Paths.Single(p => Name(graph, p.From) == "sum.c");
            Assert.AreEqual((4, 4), (fromC.Rising, fromC.Falling));
        }

        /// <summary>A latch of two NANDs is a loop: found, named, and left out of every path rather than followed forever.</summary>
        [Test]
        public void ALoopOfGates_IsFound_AndNoPathGoesRoundIt()
        {
            TimingGraph graph = Graph(@"
module latch (input logic s, r, output logic q);
    logic qn;
    nand #(1, 1) g1 (q, s, qn);
    nand #(1, 1) g2 (qn, r, q);
endmodule");

            CollectionAssert.AreEquivalent(new[] { "latch.q", "latch.qn" }, graph.Loops.Single());
            PathReport report = PathDelays.Find(graph);
            Assert.AreEqual(1, report.Paths.Single().Worst, "s through g1 only, never round to g2 and back");
            Assert.IsNotNull(StaticTiming.Analyse(graph));
        }

        /// <summary>A path through two instances: the ports join names at no delay, so only the gates are listed and added.</summary>
        [Test]
        public void APathThroughInstances_ListsTheirGates_NotTheirPorts()
        {
            TimingGraph graph = Graph(@"
module inv (input logic a, output logic y);
    not #(2, 3) g (y, a);
endmodule
module two (input logic a, output logic y);
    logic m;
    inv i1 (.a(a), .y(m));
    inv i2 (.a(m), .y(y));
endmodule");

            TimingPath path = PathDelays.Find(graph).Paths.Single();
            Assert.AreEqual(5, path.Worst);
            CollectionAssert.AreEqual(new[] { "g", "g" }, Gates(graph, path.Steps));
        }

        /// <summary>
        /// A pipeline: two registers into an AND into a third. Launched at their clock-to-Q (1), the AND falls at 3 after, so
        /// the logic is 3, and y's register needs 2 of setup: the minimum period is 1 + 3 + 2, 6. The registers' own
        /// <c>#1</c> is their clock-to-Q; y's Tsetup is given by name.
        /// </summary>
        [Test]
        public void APipeline_ItsMinimumPeriod_IsClockToQ_PlusTheLogic_PlusTheSetup()
        {
            TimingGraph graph = Graph(@"
module pipe (input logic clk, a, b, output logic y);
    logic ra, rb, t;
    always_ff @(posedge clk) begin ra <= #1 a; rb <= #1 b; end
    and #(2, 3) g (t, ra, rb);
    always_ff @(posedge clk) y <= #1 t;
endmodule", new Dictionary<string, RegisterTiming> { ["pipe.y"] = new RegisterTiming(1, 2) });

            TimingReport report = StaticTiming.Analyse(graph);

            Assert.AreEqual(2, graph.Registers.Count);
            Assert.AreEqual(6, report.MinimumPeriod);
            Assert.AreEqual(3, report.Critical.Logic);
            Assert.AreEqual(1, report.Critical.Launch.Timing.Tcq, "the register's own #1");
            Assert.AreEqual(2, report.Critical.Capture.Timing.Tsetup, "y's register, given by name");
            CollectionAssert.AreEqual(new[] { "g" }, Gates(graph, report.Critical.Arcs));
            Assert.AreEqual(1, report.RegisterToOutput.Total, "y launched at its clock-to-Q, nothing after");
            Assert.IsNull(report.InputToOutput, "no input reaches an output but through a register");
        }

        /// <summary>A counter's register feeds itself through logic inside its own process: no gate between, so the period is its clock-to-Q.</summary>
        [Test]
        public void ACounter_FeedsItself_AndItsPeriodIsItsClockToQ()
        {
            TimingGraph graph = Graph(@"
module count (input logic clk, output logic [3:0] q);
    always_ff @(posedge clk) q <= #2 q + 4'd1;
endmodule");

            TimingReport report = StaticTiming.Analyse(graph);

            Assert.AreEqual(2, report.MinimumPeriod);
            Assert.AreSame(report.Critical.Launch, report.Critical.Capture);
        }

        // -----------------------------------------------------------------
        // Bits, not signals (the 14a review)
        // -----------------------------------------------------------------

        /// <summary>
        /// A chain of NOTs along the bits of one vector: v[0] to v[1] to v[2] to v[3]. Signal by signal it was v to v, a loop,
        /// and the gates were dropped from the path; bit by bit it is the inverter chain again, 8 rising and 7 falling.
        /// </summary>
        [Test]
        public void AChainAlongTheBitsOfOneVector_IsNoLoop_AndKeepsItsGates()
        {
            TimingGraph graph = Graph(@"
module chain (input logic a, output logic y);
    wire [3:0] v;
    assign v[0] = a;
    not #(2, 3) g1 (v[1], v[0]);
    not #(2, 3) g2 (v[2], v[1]);
    not #(2, 3) g3 (v[3], v[2]);
    assign y = v[3];
endmodule");

            Assert.IsEmpty(graph.Loops);
            TimingPath path = PathDelays.Find(graph).Paths.Single();
            Assert.AreEqual((8, 7), (path.Rising, path.Falling));
            CollectionAssert.AreEqual(new[] { "g1", "g2", "g3" }, Gates(graph, path.Steps), "the assignments are wires, the gates the steps");
        }

        /// <summary>The same chain between two registers: the minimum period is its 8, where signal by signal it was 0.</summary>
        [Test]
        public void AChainAlongAVector_BetweenRegisters_IsTheMinimumPeriod()
        {
            TimingGraph graph = Graph(@"
module piped (input logic clk, a, output logic q);
    logic r;
    wire [3:0] v;
    always_ff @(posedge clk) r <= a;
    assign v[0] = r;
    not #(2, 3) g1 (v[1], v[0]);
    not #(2, 3) g2 (v[2], v[1]);
    not #(2, 3) g3 (v[3], v[2]);
    always_ff @(posedge clk) q <= v[3];
endmodule");

            Assert.AreEqual(8, StaticTiming.Analyse(graph).MinimumPeriod);
        }

        /// <summary>
        /// A ripple adder of four full adders, its carries a vector each passes to the next: no loop, and the slowest path
        /// is the carry rippling from cin up to cout through every adder's AND and OR -- 1 + 4 x (2 + 2), 17 -- not one adder's.
        /// </summary>
        [Test]
        public void ARippleAdder_ItsCarriesAVector_IsNoLoop_AndItsWorstPathRipples()
        {
            TimingGraph graph = Graph(@"
module fa (input logic a, b, ci, output logic s, co);
    logic p, g, t;
    xor #(3, 3) x1 (p, a, b);
    xor #(3, 3) x2 (s, p, ci);
    and #(2, 2) a1 (g, a, b);
    and #(2, 2) a2 (t, p, ci);
    or  #(2, 2) o1 (co, g, t);
endmodule
module ripple (input logic [3:0] a, b, input logic cin, output logic [3:0] s, output logic cout);
    wire [4:0] c;
    assign c[0] = cin;
    fa f0 (.a(a[0]), .b(b[0]), .ci(c[0]), .s(s[0]), .co(c[1]));
    fa f1 (.a(a[1]), .b(b[1]), .ci(c[1]), .s(s[1]), .co(c[2]));
    fa f2 (.a(a[2]), .b(b[2]), .ci(c[2]), .s(s[2]), .co(c[3]));
    fa f3 (.a(a[3]), .b(b[3]), .ci(c[3]), .s(s[3]), .co(c[4]));
    assign cout = c[4];
endmodule");

            Assert.IsEmpty(graph.Loops);
            TimingPath worst = PathDelays.Find(graph).Worst;
            // a[0] through x1 (3), then a2 and o1 in each adder (4 x 4): 19, the slowest of all.
            Assert.AreEqual("ripple.a[0]", Name(graph, worst.Alike.Select(e => e.From).OrderBy(n => Name(graph, n), System.StringComparer.Ordinal).First()));
            Assert.AreEqual(19, worst.Worst);
            Assert.AreEqual("ripple.cout", Name(graph, worst.To));
        }

        /// <summary>
        /// Paths that end at a register, never an output, are not walked: 40 stages that each split and meet again have 2^40
        /// ways through, and before the walk looked ahead it went down every one before finding none led out.
        /// </summary>
        [Test]
        public void ManyPathsThatLeadToNoOutput_AreNeverWalked()
        {
            var text = new System.Text.StringBuilder("module deep (input logic clk, a, output logic q);\n    logic x0;\n    assign x0 = a;\n");
            for (int i = 0; i < 40; i++)
                text.Append($"    logic n{i}, b{i}, x{i + 1};\n    not (n{i}, x{i});\n    buf (b{i}, x{i});\n    or (x{i + 1}, n{i}, b{i});\n");
            text.Append("    always_ff @(posedge clk) q <= x40;\nendmodule\n");
            TimingGraph graph = Graph(text.ToString());

            var watch = System.Diagnostics.Stopwatch.StartNew();
            PathReport report = PathDelays.Find(graph);
            Assert.Less(watch.ElapsedMilliseconds, 5000);
            Assert.AreEqual(0, report.Found);
        }

        /// <summary>The same 2^20 paths reaching an output: the walk stops at the cap, and says so.</summary>
        [Test]
        public void ManyPathsToAnOutput_StopAtTheCap_AndSaySo()
        {
            var text = new System.Text.StringBuilder("module wide (input logic a, output logic y);\n    logic x0;\n    assign x0 = a;\n");
            for (int i = 0; i < 20; i++)
                text.Append($"    logic n{i}, b{i}, x{i + 1};\n    not (n{i}, x{i});\n    buf (b{i}, x{i});\n    or (x{i + 1}, n{i}, b{i});\n");
            text.Append("    assign y = x20;\nendmodule\n");
            TimingGraph graph = Graph(text.ToString());

            var watch = System.Diagnostics.Stopwatch.StartNew();
            PathReport report = PathDelays.Find(graph);
            Assert.Less(watch.ElapsedMilliseconds, 5000);
            Assert.IsTrue(report.Capped);
            Assert.AreEqual(PathDelays.MaxPaths, report.Found);
        }

        /// <summary>
        /// A bit-select is a wire: it passes a change the same way round, and is no step of the path. Taken either way, as it
        /// was, a's rise reached y at the AND's fall delay, and a and b were two different paths through the same gate.
        /// </summary>
        [Test]
        public void ABitSelect_IsAWire_AndKeepsTheWayAChangeGoes()
        {
            TimingGraph graph = Graph(@"
module pick (input logic [1:0] a, input logic b, output logic y);
    logic t;
    assign t = a[0];
    and #(1, 2) g (y, t, b);
endmodule");

            PathReport report = PathDelays.Find(graph);
            TimingPath path = report.Paths.Single();
            Assert.AreEqual((1, 2), (path.Rising, path.Falling));
            CollectionAssert.AreEqual(new[] { "g" }, Gates(graph, path.Steps));
            CollectionAssert.AreEquivalent(new[] { "pick.a[0]", "pick.b" }, path.Alike.Select(e => Name(graph, e.From)), "a[1] reaches nothing");
        }

        /// <summary>A delayed assignment of a plain read passes a rise as a rise: 2 rising, 3 falling, never 3 for both.</summary>
        [Test]
        public void ADelayedAssignment_RisesAtItsRise_AndFallsAtItsFall()
        {
            TimingGraph graph = Graph(@"
module late (input logic a, output logic y);
    assign #(2, 3) y = a;
endmodule");

            TimingPath path = PathDelays.Find(graph).Paths.Single();
            Assert.AreEqual((2, 3), (path.Rising, path.Falling));
        }

        /// <summary>
        /// A slow rise then a slow fall, a bit-select between: a rising reaches t at 10 and y rises 1 later, 11; a falling
        /// reaches t at 1 and y falls 10 later, 11. Taken either way at the select, a rise would have met the slow fall: 20.
        /// </summary>
        [Test]
        public void ASelectMidPath_IsNotTakenEitherWay()
        {
            TimingGraph graph = Graph(@"
module mid (input logic a, output logic y);
    wire [1:0] v;
    logic t;
    buf #(10, 1) g1 (v[0], a);
    assign v[1] = 1'b0;
    assign t = v[0];
    buf #(1, 10) g2 (y, t);
endmodule");

            Assert.AreEqual(11, PathDelays.Find(graph).Paths.Single().Worst);
        }

        /// <summary>
        /// A combinational process that reads what it writes -- a parity worked out bit by bit, y = y ^ a[i] -- is no loop:
        /// what it writes and reads is its own working. Its loop's variable is no part of any path either.
        /// </summary>
        [Test]
        public void AProcessThatAccumulates_IsNoLoop()
        {
            TimingGraph graph = Graph(@"
module parity (input logic [3:0] a, output logic y);
    always_comb begin
        y = 1'b0;
        for (int i = 0; i < 4; i++)
            y = y ^ a[i];
    end
endmodule");

            Assert.IsEmpty(graph.Loops);
            PathReport report = PathDelays.Find(graph);
            CollectionAssert.AreEquivalent(new[] { "parity.a[0]", "parity.a[1]", "parity.a[2]", "parity.a[3]" },
                report.Paths.SelectMany(p => p.Alike).Select(e => Name(graph, e.From)));
        }

        /// <summary>A process copying one memory into another meets at one junction: its arcs are its bits, not their pairs.</summary>
        [Test]
        public void AProcessOverAMemory_CostsAnArcABit()
        {
            TimingGraph graph = Graph(@"
module copy (input logic clk);
    logic [7:0] m1 [0:255];
    logic [7:0] m2 [0:255];
    always_comb
        for (int i = 0; i < 256; i++)
            m2[i] = m1[i];
endmodule");

            Assert.Less(graph.Arcs.Count, 3 * 2 * 256 * 8, "about one arc for each bit read and each bit written");
        }

        /// <summary>A register's delay that calls a function is the run's to know: taken as 0, never a crash.</summary>
        [Test]
        public void ARegisterDelayCallingAFunction_IsNoConstant_AndNoCrash()
        {
            TimingGraph graph = Graph(@"
module odd (input logic clk, d, output logic q);
    logic [3:0] k = 4'd2;
    function automatic logic [3:0] f();
        return k;
    endfunction
    always_ff @(posedge clk) q <= #(f()) d;
endmodule");

            Assert.AreEqual(0, graph.Registers.Single().Timing.Tcq);
        }

        /// <summary>A tri-state's control can turn it off, which takes the turn-off delay: en reaches y at 9, d at its own 1 and 2.</summary>
        [Test]
        public void ATriStatesControl_TakesItsTurnOffDelay()
        {
            TimingGraph graph = Graph(@"
module drive (input logic d, en, output wire y);
    bufif1 #(1, 2, 9) g (y, d, en);
endmodule");

            PathReport report = PathDelays.Find(graph);
            Assert.AreEqual(9, report.Paths.Single(p => Name(graph, p.From) == "drive.en").Worst);
            TimingPath data = report.Paths.Single(p => Name(graph, p.From) == "drive.d");
            Assert.AreEqual((1, 2), (data.Rising, data.Falling));
        }

        /// <summary>Two processes writing one register launch it at the later of their clock-to-Q: 3, then the NOT's 2.</summary>
        [Test]
        public void TwoProcessesWritingOneRegister_LaunchItAtTheLaterClockToQ()
        {
            TimingGraph graph = Graph(@"
module both (input logic clk, a, b, output logic r);
    logic q, t;
    always @(posedge clk) q <= #1 a;
    always @(posedge clk) q <= #3 b;
    not #(2, 2) g (t, q);
    always @(posedge clk) r <= t;
endmodule");

            TimingReport report = StaticTiming.Analyse(graph);
            Assert.AreEqual(5, report.MinimumPeriod);
            Assert.AreEqual(3, report.Critical.Launch.Timing.Tcq);
        }

        [Test]
        public void TheGates_AreCountedByKindAndInputs()
        {
            TimingGraph graph = Graph(@"
module mux (input logic a, b, s, output logic y);
    logic sn, t0, t1;
    not n (sn, s);
    and a0 (t0, a, sn);
    and a1 (t1, b, s);
    or o (y, t0, t1);
endmodule");

            Assert.AreEqual(4, graph.Gates);
            CollectionAssert.AreEquivalent(new Dictionary<string, int> { ["not"] = 1, ["and2"] = 2, ["or2"] = 1 }, graph.GateCounts);
        }
    }
}
