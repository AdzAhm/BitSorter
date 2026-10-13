using System.IO;
using System.Linq;
using BitSorter.SystemVerilog.Syntax;
using NUnit.Framework;

namespace BitSorter.Sim.Tests
{
    /// <summary>
    /// The latch check: an <c>always_comb</c> that assigns a variable on some paths and not on others
    /// is warned about, naming it; the course's "defaults first" style, a complete <c>case</c> and a
    /// loop over every bit are not.
    /// </summary>
    public class LintTests
    {
        private static Elaboration Elaborate(string body)
        {
            SyntaxTree tree = SyntaxReader.ReadText("module m (input logic a, b, input logic [1:0] s, output logic [3:0] y, z);\n" + body + "\nendmodule\n", "lint.sv");
            Assert.IsEmpty(tree.Diagnostics, string.Join("\n", tree.Diagnostics));
            return Elaborator.Elaborate(tree);
        }

        private static string[] Latches(Elaboration e) =>
            e.Diagnostics.Where(d => d.Code == ElaborationCodes.LatchProne).Select(d => d.Message).ToArray();

        [Test]
        public void AnIfWithNoElse_AndNoDefault_IsALatch_AndTheWarningNamesIt()
        {
            Elaboration e = Elaborate(@"
always_comb begin
    if (a) y = 4'd1;
end");

            string warning = Latches(e).Single();
            StringAssert.Contains("assigns y on some paths", warning);
            StringAssert.Contains("at the top of the block", warning, "and says the way out");
            Assert.IsNotNull(e.Design, "a warning, not an error: it still runs");
            Assert.AreEqual(3, e.Diagnostics.Single().Location.Line, "the always_comb's own line");
        }

        [Test]
        public void DefaultsFirst_IsNotALatch()
        {
            Assert.IsEmpty(Latches(Elaborate(@"
always_comb begin
    y = 4'd0;
    z = 4'd0;
    if (a) y = 4'd1;
    else if (b) z = 4'd2;
end")));
        }

        [Test]
        public void BothBranchesAssigning_IsNotALatch_ButOneOfTwoVariablesInEach_Is()
        {
            Assert.IsEmpty(Latches(Elaborate("always_comb if (a) y = 4'd1; else y = 4'd2;")));

            string warning = Latches(Elaborate("always_comb if (a) y = 4'd1; else z = 4'd2;")).Single();
            StringAssert.Contains("y and z", warning);
        }

        [Test]
        public void ACase_IsCompleteWithADefault_WithEveryValue_OrWhenUnique()
        {
            Assert.IsEmpty(Latches(Elaborate(@"
always_comb case (s) 2'd0: y = 4'd1; 2'd1: y = 4'd2; default: y = 4'd3; endcase")), "a default");

            Assert.IsEmpty(Latches(Elaborate(@"
always_comb case (s) 2'd0: y = 4'd1; 2'd1: y = 4'd2; 2'd2: y = 4'd3; 2'd3: y = 4'd4; endcase")), "all four values of a 2-bit subject");

            Assert.IsEmpty(Latches(Elaborate(@"
always_comb unique case (s) 2'd0: y = 4'd1; 2'd1: y = 4'd2; endcase")), "the writer says it is complete");

            Assert.IsNotEmpty(Latches(Elaborate(@"
always_comb case (s) 2'd0: y = 4'd1; 2'd1: y = 4'd2; 2'd2: y = 4'd3; endcase")), "the fourth value is missing");
        }

        [Test]
        public void AnEnumCase_MissingAState_IsALatch()
        {
            string warning = Latches(Elaborate(@"
typedef enum logic [1:0] {IDLE, LOAD, RUN} state_t;
state_t state;
assign state = state_t'(s);
always_comb case (state) IDLE: y = 4'd1; LOAD: y = 4'd2; RUN: y = 4'd3; endcase")).Single();

            StringAssert.Contains("assigns y", warning, "three states in two bits leave the fourth encoding holding y");
        }

        [Test]
        public void ALoopOverEveryBit_IsNotALatch()
        {
            Assert.IsEmpty(Latches(Elaborate(@"
always_comb for (int i = 0; i < 4; i++) y[i] = s[0] ^ a;")));
        }

        [Test]
        public void AlwaysStar_IsCheckedToo_ButAlwaysLatch_IsWhatItSays()
        {
            Assert.IsNotEmpty(Latches(Elaborate("always @* if (a) y = 4'd1;")));
            Assert.IsEmpty(Latches(Elaborate("always_latch if (a) y = 4'd1;")));
            Assert.IsEmpty(Latches(Elaborate("logic clk; always @(posedge clk) if (a) y <= 4'd1;")), "a flip-flop holds its value on purpose");
        }

        [Test]
        public void TheBehaviouralCorpus_HasNoLatches()
        {
            string root = Ci.RepoRoot.Combine("Tools/ci/Oracle/corpus/behavioural");
            foreach (string path in Directory.GetFiles(root, "*.sv"))
            {
                Elaboration e = Elaborator.Elaborate(SyntaxReader.ReadText(File.ReadAllText(path), Path.GetFileName(path)),
                    Path.GetFileName(path) switch
                    {
                        "traffic_light.sv" => "traffic_light_tb",
                        "multiplier.sv" => "multiplier_tb",
                        _ => null,
                    });

                Assert.IsEmpty(Latches(e), Path.GetFileName(path));
            }
        }
    }
}
