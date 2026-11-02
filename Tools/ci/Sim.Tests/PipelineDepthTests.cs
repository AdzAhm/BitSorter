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

            // A counter's enable and reset only steer it, as the course counts a stage: no input reaches its output's value.
            PipelineDepth counter = Depth(
                "module m (input logic clk, rst, en, output logic [3:0] count);\n" +
                "    always_ff @(posedge clk) if (rst) count <= 0; else if (en) count <= count + 1;\n" +
                "endmodule\n");
            Assert.IsFalse(counter.Reaches);
            Assert.IsNull(counter.Stages);
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
