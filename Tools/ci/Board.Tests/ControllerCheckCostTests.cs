using System.Linq;
using System.Text;
using BitSorter.Sim;
using BitSorter.SystemVerilog;
using BitSorter.SystemVerilog.Syntax;
using NUnit.Framework;

namespace BitSorter.Board.Tests
{
    /// <summary>
    /// What a controller's check costs (the review of the 16d fixes). A reading is checked again whenever its module is drawn
    /// again -- after any edit of the project while it is up -- so a check that runs long is a freeze on every edit, seconds of
    /// it in a browser. It is held here in trials, each the netlist worked out once, and in values made at random for them,
    /// never in time: <see cref="ControllerCheck.Why(ControllerReading, Design, RtlNetlist, string, out (int Trials, long Values))"/>.
    /// </summary>
    public class ControllerCheckCostTests
    {
        /// <summary>
        /// A machine of <paramref name="states"/> states, each with an arrow taken where <c>go</c> and <c>a</c> say, one where
        /// <paramref name="other"/> holds, and one on <c>stop</c>. Its inputs are four of one bit, two of eight and one of four:
        /// a state's first look at them, each at every value, is <see cref="FirstLook"/> trials.
        /// </summary>
        private static string Machine(int states, string other)
        {
            var names = Enumerable.Range(0, states).Select(i => "S" + i).ToList();
            var text = new StringBuilder();
            text.Append("module m (input logic clk, rst, go, stop, input logic [7:0] a, b, input logic [3:0] count, output logic busy);\n");
            text.Append("    typedef enum logic [4:0] {" + string.Join(", ", names) + "} st_t;\n    st_t state, nxt;\n");
            text.Append("    always_ff @(posedge clk) if (rst) state <= S0; else state <= nxt;\n");
            text.Append("    always_comb begin\n        nxt = state;\n        busy = 1'b0;\n        case (state)\n");
            for (int i = 0; i < states; i++)
                text.Append($"            S{i}: begin busy = 1'b{i % 2}; if (go && a == 8'd{i + 3}) nxt = S{(i + 1) % states}; "
                    + $"else if ({other}) nxt = S{(i + 2) % states}; else if (stop) nxt = S0; end\n");
            text.Append("        endcase\n    end\nendmodule\n");
            return text.ToString();
        }

        /// <summary>A state's first look at <see cref="Machine"/>'s inputs: clk, rst, go and stop at 2 values, a and b at 256, count at 16.</summary>
        private const int FirstLook = 4 * 2 + 2 * 256 + 16;

        private static (string Why, (int Trials, long Values) Cost) Check(string text)
        {
            SyntaxTree tree = SyntaxReader.ReadText(text, "m.sv");
            Assert.IsFalse(tree.HasErrors, string.Join("\n", tree.Diagnostics));
            ControllerReading reading = ControllerReader.Read(tree, tree.Modules.Single(m => m.Name == "m"), out string notRead);
            Assert.IsNotNull(reading, notRead);
            Design design = Elaborator.Elaborate(tree, "m").Design;
            string top = design.Scopes.Single(s => s.Kind == ScopeKind.Top).Path;
            string why = ControllerCheck.Why(reading, design, RtlExtractor.Of(design, top), top, out (int Trials, long Values) cost);
            TestContext.WriteLine($"{cost.Trials} trials, {cost.Values} values -> {why ?? "shown"}");
            return (why, cost);
        }

        /// <summary>
        /// A machine with an arrow no input takes out of every state -- <c>count == 5'd20</c> on a count of four bits, a slip a
        /// student makes -- is refused after a first look at each state and no more than <see cref="ControllerCheck.SecondLookTrials"/>
        /// trials of second looks in all (the review of the 16d fixes: the cap was a state's, each state looked at again up to
        /// it, 76,136 trials for sixteen states where origin/v5 made 8,576 and the architecture said 16,384 at most).
        /// </summary>
        [Test]
        public void AMachineWithAnArrowNoInputTakesOutOfEveryState_IsRefused_InFewerTrialsThanAFirstLookAtEachAndOneBudgetOfSecondLooks()
        {
            const int states = 16;
            (string why, (int trials, long _)) = Check(Machine(states, "count == 5'd20"));
            StringAssert.StartsWith("No input takes", why);
            Assert.LessOrEqual(trials, states * FirstLook + ControllerCheck.SecondLookTrials);
        }

        /// <summary>The same machine with every arrow one an input takes is shown: what the test above refuses is the arrow alone.</summary>
        [Test]
        public void TheSameMachine_WithEveryArrowTaken_IsShown()
        {
            const int states = 16;
            (string why, (int trials, long _)) = Check(Machine(states, "count == 4'd9"));
            Assert.IsNull(why);
            Assert.GreaterOrEqual(trials, states * FirstLook, "sanity: every state looked at");
        }

        /// <summary>A machine beside a memory of <paramref name="depth"/> words it never reads into its states.</summary>
        private static string BesideAMemory(int depth) => @"
module m (input logic clk, rst, go, input logic [7:0] data, input logic [13:0] addr, output logic busy, output logic [31:0] out);
    typedef enum logic [1:0] {IDLE, RUN, WAIT, DONE} st_t;
    st_t state, nxt;
    logic [31:0] mem [0:DEPTH];
    assign out = mem[addr];
    always_ff @(posedge clk) if (rst) state <= IDLE; else state <= nxt;
    always_comb begin
        nxt = state;
        busy = 1'b0;
        case (state)
            IDLE: if (go) nxt = RUN;
            RUN: begin busy = 1'b1; if (data == 8'd7) nxt = WAIT; end
            WAIT: begin busy = 1'b1; if (go) nxt = DONE; else nxt = IDLE; end
            DONE: nxt = IDLE;
        endcase
    end
endmodule
".Replace("DEPTH", (depth - 1).ToString());

        /// <summary>
        /// A machine beside a memory of 16,384 words is shown, its words given values once and a few of them again each trial,
        /// never every word every trial (the review of the 16d fixes: 16,384 words made again for each of 1,112 trials, 18 million
        /// values and a third of a second, on every edit).
        /// </summary>
        [Test]
        public void AMachineBesideABigMemory_IsShown_ItsWordsGivenValuesOnceAndAFewATrial()
        {
            const int depth = 16384;
            (string why, (int trials, long values)) = Check(BesideAMemory(depth));
            Assert.IsNull(why);
            Assert.LessOrEqual(values, depth + (long)trials * 100, "a trial gives the memory a few of its words, not all");
        }

        /// <summary>A machine beside a memory of a few words: every word given a value every trial, as before.</summary>
        [Test]
        public void AMachineBesideASmallMemory_IsShown_EveryWordGivenAValueEveryTrial()
        {
            const int depth = 4;
            (string why, (int trials, long values)) = Check(BesideAMemory(depth));
            Assert.IsNull(why);
            Assert.GreaterOrEqual(values, (long)trials * depth, "sanity: each word made again every trial");
        }
    }
}
