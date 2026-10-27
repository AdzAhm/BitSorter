using System.IO;
using System.Linq;
using BitSorter.SystemVerilog.Syntax;
using NUnit.Framework;

namespace BitSorter.Sim.Tests
{
    /// <summary>
    /// The design's lint (Part 14e), what ModelSim's <c>vlog</c> warns of: a signal read that nothing drives (SV0402), one
    /// set inside a block that nothing reads (SV0403), a net with drivers that cannot all let go (SV0404), and a plain value
    /// given to something narrower (SV0405). Each a warning at its line, never stopping the run; each raised by a design
    /// written here to raise it, and by none that should not.
    /// </summary>
    public class DesignLintTests
    {
        private static Elaboration Elaborate(string text)
        {
            SyntaxTree tree = SyntaxReader.ReadText(text, "lint.sv");
            Assert.IsEmpty(tree.Diagnostics, string.Join("\n", tree.Diagnostics));
            Elaboration e = Elaborator.Elaborate(tree);
            Assert.IsFalse(e.HasErrors, string.Join("\n", e.Diagnostics));
            Assert.IsNotNull(e.Design, "a warning, not an error: it still runs");
            return e;
        }

        private static Diagnostic[] Said(Elaboration e, string code) => e.Diagnostics.Where(d => d.Code == code).ToArray();

        private static readonly string[] DesignCodes =
        {
            ElaborationCodes.Undriven, ElaborationCodes.NeverRead, ElaborationCodes.SeveralDrivers, ElaborationCodes.Truncated,
        };

        [Test]
        public void ASignalReadThatNothingDrives_IsSaid_AtItsDeclaration_WithWhatItReads()
        {
            Elaboration e = Elaborate(@"module top (input logic a, output logic y, z);
    wire w;
    logic v;
    assign y = a & w;
    assign z = a | v;
endmodule");

            Diagnostic net = Said(e, ElaborationCodes.Undriven).Single(d => d.Message.StartsWith("w "));
            Assert.AreEqual("w is read, but nothing drives or sets it, so it reads z.", net.Message);
            Assert.AreEqual(2, net.Location.Line, "where it is declared");
            Assert.AreEqual(DiagnosticSeverity.Warning, net.Severity);
            StringAssert.EndsWith("so it reads x.", Said(e, ElaborationCodes.Undriven).Single(d => d.Message.StartsWith("v ")).Message, "a variable reads x");
        }

        [Test]
        public void ASignalSetThatNothingReads_IsSaidInsideABlock_ButNotOnTheTop_WhoseSignalsAreWatched()
        {
            Elaboration e = Elaborate(@"module half (input logic a, b, output logic s);
    logic spare;
    assign spare = a & b;
    assign s = a ^ b;
endmodule

module top (input logic a, b, output logic s);
    logic seen;
    assign seen = a | b;
    half h1 (.a(a), .b(b), .s(s));
    half h2 (.a(b), .b(a), .s());
endmodule");

            Diagnostic spare = Said(e, ElaborationCodes.NeverRead).Single();
            Assert.AreEqual("spare is set, but nothing reads it.", spare.Message, "once, though placed twice");
            Assert.AreEqual(2, spare.Location.Line);
        }

        [Test]
        public void SeveralDriversOnANet_AreSaid_UnlessEveryOneCanLetGo()
        {
            Elaboration clash = Elaborate(@"module top (input logic a, b, output wire y);
    wire w;
    assign w = a;
    assign w = b;
    assign y = w;
endmodule");
            Diagnostic said = Said(clash, ElaborationCodes.SeveralDrivers).Single();
            StringAssert.StartsWith("w has 2 drivers on the same bits", said.Message);
            StringAssert.Contains("bufif1", said.Message, "and says the way a bus is driven");

            Assert.IsEmpty(Said(Elaborate(@"module top (input logic a, b, en, output wire y);
    bufif1 g1 (y, a, en);
    bufif0 g2 (y, b, en);
endmodule"), ElaborationCodes.SeveralDrivers), "two tri-state gates");

            Assert.IsEmpty(Said(Elaborate(@"module top (input logic a, b, en, output wire y);
    assign y = en ? a : 1'bz;
    assign y = en ? 1'bz : b;
endmodule"), ElaborationCodes.SeveralDrivers), "two assignments that let go");

            Assert.IsEmpty(Said(Elaborate(@"module top (input logic a, b, output wire [1:0] y);
    assign y[0] = a;
    assign y[1] = b;
endmodule"), ElaborationCodes.SeveralDrivers), "each on bits of its own");
        }

        [Test]
        public void APlainValueGivenToSomethingNarrower_IsSaidTruncated_ButNotASumNorANumberNorAnInt()
        {
            Elaboration e = Elaborate(@"module top (input logic [3:0] x, output logic [1:0] n, m, k);
    assign n = x;
    assign m = x + 4'd1;
    assign k = 4'd3;
    logic [1:0] w;
    always_comb begin
        w = 2'd0;
        for (int i = 0; i < 3; i++)
            w = i;
    end
endmodule");

            Diagnostic cut = Said(e, ElaborationCodes.Truncated).Single();
            Assert.AreEqual("n is 2 bits, and x is 4: its top 2 bits are dropped.", cut.Message);
            Assert.AreEqual(2, cut.Location.Line);
        }

        [Test]
        public void ATruncationInAProcess_IsSaidToo()
        {
            Elaboration e = Elaborate(@"module top (input logic clk, input logic [7:0] d, output logic [3:0] q);
    always_ff @(posedge clk) q <= {d[7], d[2:0], d[0]};
endmodule");

            StringAssert.StartsWith("q is 4 bits, and {d[7], d[2:0], d[0]} is 5: its top 1 bit is dropped.", Said(e, ElaborationCodes.Truncated).Single().Message);
        }

        /// <summary>
        /// The oracle's corpus -- gates, behaviour, the processor and its testbenches -- raises none of these: every one of
        /// its signals is driven, read, driven once and sized to fit, as the course's style has them.
        /// </summary>
        [Test]
        public void TheCorpus_RaisesNone()
        {
            string root = Ci.RepoRoot.Combine("Tools/ci/Oracle/corpus");
            var corpus = new MemorySourceFiles();
            foreach (string path in Directory.GetFiles(root, "*.sv*", SearchOption.AllDirectories))
                corpus.Add(Path.GetRelativePath(root, path).Replace('\\', '/'), File.ReadAllText(path));

            foreach (string path in Directory.GetFiles(root, "*.sv", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(root, path).Replace('\\', '/');
                SyntaxTree tree = SyntaxReader.Read(relative, corpus);
                if (tree.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
                    continue;
                Elaboration e = Elaborator.Elaborate(tree);
                string[] said = e.Diagnostics.Where(d => DesignCodes.Contains(d.Code)).Select(d => d.Location.Line + ": " + d.Message).ToArray();
                Assert.IsEmpty(said, relative);
            }
        }
    }
}
