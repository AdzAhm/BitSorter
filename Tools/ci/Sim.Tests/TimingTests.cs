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

        private static string Name(TimingGraph graph, int signal) => graph.Design.Signals[signal].Name;

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
