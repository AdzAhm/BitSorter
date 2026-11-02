using System.Linq;
using BitSorter.SystemVerilog.Syntax;
using NUnit.Framework;

namespace BitSorter.Sim.Tests
{
    /// <summary>
    /// How many registers a design's input-to-output paths cross (Part 15i-1): a K-stage pipeline's latency is K periods, as
    /// the course counts it; a state machine's loop and paths of different depths are no pipeline. Each circuit is written
    /// for these tests.
    /// </summary>
    public class PipelineDepthTests
    {
        private static PipelineDepth Depth(string text)
        {
            SyntaxTree tree = SyntaxReader.ReadText(text, "test.sv");
            Assert.IsEmpty(tree.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error), string.Join("\n", tree.Diagnostics));
            Elaboration e = Elaborator.Elaborate(tree, null);
            Assert.IsFalse(e.HasErrors, string.Join("\n", e.Diagnostics));
            return PipelineDepth.Of(TimingGraph.Of(e.Design));
        }

        [Test]
        public void Logic_WithNoRegister_IsZeroStages()
        {
            PipelineDepth depth = Depth("module m (input logic a, b, output logic y); assign y = a & b; endmodule");
            Assert.AreEqual(0, depth.Stages);
        }

        [Test]
        public void ARegisterOnEveryPath_IsThatManyStages()
        {
            PipelineDepth one = Depth(
                "module m (input logic clk, input logic [3:0] a, b, output logic [3:0] y);\n" +
                "    always_ff @(posedge clk) y <= a + b;\n" +
                "endmodule\n");
            Assert.AreEqual(1, one.Stages);

            PipelineDepth three = Depth(
                "module m (input logic clk, input logic [3:0] a, b, output logic [3:0] y);\n" +
                "    logic [3:0] ra, rb, s;\n" +
                "    always_ff @(posedge clk) begin\n" +
                "        ra <= a; rb <= b;\n" +
                "        s <= ra + rb;\n" +
                "        y <= s;\n" +
                "    end\n" +
                "endmodule\n");
            Assert.AreEqual(3, three.Stages, "registered in, added, registered out");
        }

        [Test]
        public void EachRegistersOwnProcess_IsRead_HoweverTheyAreNamed()
        {
            // A drawn module prints a process for each register, all on one line -- always_ff@0, always_ff@0 -- so a register
            // matched to its process by name took the last one's flow, and no input reached the output.
            PipelineDepth depth = Depth(
                "module m (input logic clk, input logic a, output logic y); logic r1 = 1'b0; logic r2 = 1'b0; " +
                "always_ff @(posedge clk) r1 <= ~a; always_ff @(posedge clk) r2 <= ~r1; assign y = r2; endmodule\n");
            Assert.AreEqual(2, depth.Stages);
        }

        [Test]
        public void PathsOfDifferentDepths_AreNoPipeline()
        {
            PipelineDepth depth = Depth(
                "module m (input logic clk, input logic a, b, output logic y);\n" +
                "    logic ra;\n" +
                "    always_ff @(posedge clk) ra <= a;\n" +
                "    assign y = ra & b;   // a through a register, b straight\n" +
                "endmodule\n");
            Assert.IsNull(depth.Stages);
            Assert.AreEqual(0, depth.Fewest);
            Assert.AreEqual(1, depth.Most);
            Assert.IsFalse(depth.Loop);
        }

        [Test]
        public void ALoopOnAnInputsWay_IsNoPipeline()
        {
            PipelineDepth accumulator = Depth(
                "module m (input logic clk, input logic [3:0] x, output logic [3:0] acc);\n" +
                "    always_ff @(posedge clk) acc <= acc + x;   // x's way to acc runs round acc\n" +
                "endmodule\n");
            Assert.IsTrue(accumulator.Loop);
            Assert.IsNull(accumulator.Stages);

            // A counter's enable decides when it counts: it reaches the count that way, and the count goes round itself.
            PipelineDepth counter = Depth(
                "module m (input logic clk, rst, en, output logic [3:0] count);\n" +
                "    always_ff @(posedge clk) if (rst) count <= 0; else if (en) count <= count + 1;\n" +
                "endmodule\n");
            Assert.IsTrue(counter.Reaches);
            Assert.IsTrue(counter.Loop);
            Assert.IsNull(counter.Stages);
        }

        // -----------------------------------------------------------------
        // The first review's cases (2026-11-02), each written for these tests
        // -----------------------------------------------------------------

        private const string States = "typedef enum logic [1:0] {S0, S1, S2} state_t;\n";

        [Test]
        public void AStateMachineInOneProcess_IsALoop_MooreOrMealy()
        {
            const string machine =
                "    state_t s;\n" +
                "    always_ff @(posedge clk)\n" +
                "        if (rst) s <= S0;\n" +
                "        else case (s)\n" +
                "            S0: if (x) s <= S1;\n" +
                "            S1: if (x) s <= S2; else s <= S0;\n" +
                "            S2: s <= S0;\n" +
                "            default: s <= S0;\n" +
                "        endcase\n";
            PipelineDepth mealy = Depth(States + "module m (input logic clk, rst, x, output logic z);\n" + machine +
                "    assign z = (s == S2) & x;\nendmodule\n");
            Assert.IsTrue(mealy.Loop, "x decides s, which goes round itself");
            Assert.IsNull(mealy.Stages);

            PipelineDepth moore = Depth(States + "module m (input logic clk, rst, x, output logic z);\n" + machine +
                "    assign z = (s == S2);\nendmodule\n");
            Assert.IsTrue(moore.Reaches, "x reaches z by deciding s");
            Assert.IsTrue(moore.Loop);
        }

        [Test]
        public void AMulticycleDatapath_IsSequenced_NeverAPipeline()
        {
            PipelineDepth depth = Depth(States +
                "module m (input logic clk, rst, input logic [3:0] x, output logic [3:0] y);\n" +
                "    state_t state;\n" +
                "    logic [3:0] a, b;\n" +
                "    always_ff @(posedge clk)\n" +
                "        if (rst) state <= S0;\n" +
                "        else case (state)\n" +
                "            S0: begin a <= x; state <= S1; end\n" +
                "            S1: begin b <= a + 1; state <= S2; end\n" +
                "            default: begin y <= b + 1; state <= S0; end\n" +
                "        endcase\n" +
                "endmodule\n");
            Assert.IsTrue(depth.Sequenced, "x, a and b are taken only as state says");
            Assert.IsNull(depth.Stages, "three registers from x to y, and still no 3-stage pipeline");
        }

        [Test]
        public void AToggle_IsALoop()
        {
            PipelineDepth depth = Depth("module m (input logic clk, t, output logic q); always_ff @(posedge clk) if (t) q <= ~q; endmodule\n");
            Assert.IsTrue(depth.Loop);
        }

        [Test]
        public void AShiftRegister_IsItsLengthInStages_BitByBit()
        {
            PipelineDepth depth = Depth(
                "module m (input logic clk, d, output logic y);\n" +
                "    logic [7:0] sr;\n" +
                "    always_ff @(posedge clk) sr <= {sr[6:0], d};\n" +
                "    assign y = sr[7];\n" +
                "endmodule\n");
            Assert.IsFalse(depth.Loop, "bit i is bit i - 1's, never its own");
            Assert.AreEqual(8, depth.Stages);
        }

        /// <summary>
        /// An enable is one stage however it is written (Ahmad, 2026-11-02: the ?: form read as a loop, so a student writing
        /// the same flip-flop the other way lost its latency): a ?: arm that is the register itself, whole, is its hold, as
        /// an else that gives it back is. Anything else of the register in its own value still goes round.
        /// </summary>
        [Test]
        public void AnEnable_HoweverWritten_IsOneStage_AndOnlyAValueThatGoesRoundIsALoop()
        {
            const string Head = "module m (input logic clk, en, sel, d, e, output logic q); always_ff @(posedge clk) ";
            Assert.AreEqual(1, Depth(Head + "if (en) q <= d; endmodule\n").Stages);
            Assert.AreEqual(1, Depth(Head + "if (en) q <= d; else q <= q; endmodule\n").Stages,
                "q given its own value back is kept, as with no else");
            Assert.AreEqual(1, Depth(Head + "q <= en ? d : q; endmodule\n").Stages, "the same flip-flop written through ?:");
            Assert.AreEqual(1, Depth(Head + "q <= en ? q : d; endmodule\n").Stages, "the hold on either arm");
            Assert.AreEqual(1, Depth(Head + "q <= en ? (sel ? d : e) : q; endmodule\n").Stages, "a choice of inputs behind the enable");
            Assert.AreEqual(1, Depth(Head + "q <= en ? d : (sel ? q : e); endmodule\n").Stages, "a hold nested inside");

            Assert.IsTrue(Depth(Head + "q <= en ? d : ~q; endmodule\n").Loop, "q worked on, not held: it goes round");
            Assert.IsTrue(Depth("module m (input logic clk, en, input logic [1:0] d, output logic [1:0] q); always_ff @(posedge clk) q <= en ? d : q + 2'd1; endmodule\n").Loop,
                "a counter written with ?: is a loop");
            Assert.IsTrue(Depth("module m (input logic clk, en, input logic [1:0] d, output logic [1:0] q); always_ff @(posedge clk) q <= en ? d : {q[0], q[1]}; endmodule\n").Loop,
                "q's bits swapped are not q held: a value that goes round");
        }

        [Test]
        public void ABlockingVariable_IsFollowedInTheOrderWritten()
        {
            PipelineDepth accumulator = Depth(
                "module m (input logic clk, input logic [3:0] x, output logic [3:0] y);\n" +
                "    logic [3:0] acc;\n" +
                "    always_ff @(posedge clk) begin acc = acc + x; y <= acc; end\n" +
                "endmodule\n");
            Assert.IsTrue(accumulator.Loop, "acc goes round itself, kept from edge to edge");

            PipelineDepth reversed = Depth(
                "module m (input logic clk, a, output logic y);\n" +
                "    logic r;\n" +
                "    always_ff @(posedge clk) begin y = r; r = a; end\n" +
                "endmodule\n");
            Assert.AreEqual(2, reversed.Stages, "y takes r's last value, before r takes a");

            PipelineDepth temporary = Depth(
                "module m (input logic clk, a, b, output logic y);\n" +
                "    logic t;\n" +
                "    always_ff @(posedge clk) begin t = a & b; y <= t; end\n" +
                "endmodule\n");
            Assert.AreEqual(1, temporary.Stages);
        }

        [Test]
        public void AWordWrittenAtAPlaceFoundAsItRuns_IsNotFollowed()
        {
            PipelineDepth depth = Depth(
                "module m (input logic clk, d, output logic y);\n" +
                "    logic r [0:3];\n" +
                "    always_ff @(posedge clk) begin\n" +
                "        r[0] <= d;\n" +
                "        for (int i = 1; i < 4; i++) r[i] <= r[i-1];\n" +
                "    end\n" +
                "    assign y = r[3];\n" +
                "endmodule\n");
            Assert.IsTrue(depth.Unfollowed);
            Assert.IsNull(depth.Stages);
        }

        [Test]
        public void AClock_StartsNoPath_AndAResetDecidesNoStage()
        {
            Assert.AreEqual(1, Depth("module m (input logic clk, d, output logic y); logic q; always_ff @(posedge clk) q <= d; assign y = q & clk; endmodule\n").Stages,
                "the clock read as a value is still the clock");

            PipelineDepth reset = Depth(
                "module m (input logic clk, rst, a, output logic y);\n" +
                "    logic r1, r2;\n" +
                "    always_ff @(posedge clk)\n" +
                "        if (rst) begin r1 <= 0; r2 <= 0; y <= 0; end\n" +
                "        else begin r1 <= a; r2 <= r1; y <= r2; end\n" +
                "endmodule\n");
            Assert.AreEqual(3, reset.Stages, "a synchronous reset decides every stage and is none");
        }

        [Test]
        public void RegistersOnDifferentEdges_AreNoPipeline_ButAnAsynchronousResetListedFirstIsNoOtherClock()
        {
            PipelineDepth mixed = Depth(
                "module m (input logic clk, a, output logic y);\n" +
                "    logic r;\n" +
                "    always_ff @(posedge clk) r <= a;\n" +
                "    always_ff @(negedge clk) y <= r;\n" +
                "endmodule\n");
            Assert.IsTrue(mixed.MixedClocks);
            Assert.IsNull(mixed.Stages);

            PipelineDepth resets = Depth(
                "module m (input logic clk, rst, a, output logic y);\n" +
                "    logic r;\n" +
                "    always_ff @(posedge rst or posedge clk) if (rst) r <= 0; else r <= a;\n" +
                "    always_ff @(posedge clk or posedge rst) if (rst) y <= 0; else y <= r;\n" +
                "endmodule\n");
            Assert.IsFalse(resets.MixedClocks);
            Assert.AreEqual(2, resets.Stages);
        }

        [Test]
        public void ADesignWithNoOutputs_SaysSo()
        {
            PipelineDepth depth = Depth("module m (input logic clk, d); logic q; always_ff @(posedge clk) q <= d; endmodule\n");
            Assert.IsFalse(depth.HasOutputs);
            Assert.IsNull(depth.Stages);
        }

        [Test]
        public void ALoopBesideThePipeline_LeavesItAPipeline()
        {
            PipelineDepth depth = Depth(
                "module m (input logic clk, input logic [3:0] a, output logic [3:0] y, output logic [3:0] ticks);\n" +
                "    logic [3:0] r;\n" +
                "    always_ff @(posedge clk) begin\n" +
                "        r <= a;\n" +
                "        y <= r;\n" +
                "        ticks <= ticks + 1;   // a counter of its own, no input on its way\n" +
                "    end\n" +
                "endmodule\n");
            Assert.IsFalse(depth.Loop);
            Assert.AreEqual(2, depth.Stages);
        }
    }
}
