using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using BitSorter.SystemVerilog.Syntax;
using NUnit.Framework;

namespace BitSorter.Board.Tests
{
    /// <summary>
    /// The classic slips answered in words a student can act on (Part 15i-2), each a rewording of what BitSorter already
    /// refused or warned of, never a new refusal: the answer on the slip's own line, and the code mended taken without a word.
    /// That Icarus refuses the same slips, and takes the mended code, is <c>FriendlyErrorOracleTests</c>'.
    /// </summary>
    public class FriendlyErrorTests
    {
        private static IEnumerable<FriendlyErrorCase> Cases() => FriendlyErrorCases.All();

        /// <summary><paramref name="m"/> as the written module m, with the modules beside it, as the CODE editor reads it.</summary>
        private static WrittenModule Read(string m, string[] beside)
        {
            Project project = Project.New("scratch");
            foreach (string other in beside)
                project = project.WithModule(new Module { Name = Regex.Match(other, @"module\s+(\w+)").Groups[1].Value, Source = other });
            project = project.WithModule(new Module { Name = "m", Source = m });
            return WrittenModules.Read(project, project.Find("m"));
        }

        [TestCaseSource(nameof(Cases))]
        public void ASlip_IsAnsweredOnItsLine_InWordsThatSayWhatToWrite(FriendlyErrorCase slip)
        {
            WrittenModule read = Read(slip.Typed, slip.Beside);
            DiagnosticSeverity severity = slip.Warning ? DiagnosticSeverity.Warning : DiagnosticSeverity.Error;
            Diagnostic answer = read.Diagnostics.FirstOrDefault(d => d.Severity == severity);
            Assert.IsNotNull(answer, string.Join("\n", read.Diagnostics));
            StringAssert.Contains(slip.Says, answer.Message);
            Assert.AreEqual(slip.Line, answer.Location.Line, answer.Message);
            if (slip.Warning)
                Assert.IsFalse(read.HasErrors, "read as what it looks like, and run: " + read.FirstError);
        }

        [TestCaseSource(nameof(Cases))]
        public void TheSlipMended_IsTakenWithoutAWord(FriendlyErrorCase slip)
        {
            WrittenModule read = Read(slip.Mended, slip.Beside);
            Assert.IsFalse(read.HasErrors, read.FirstError?.ToString());
            Assert.IsFalse(read.Diagnostics.Any(d => d.Code == DiagnosticCodes.LookalikeCharacter), string.Join("\n", read.Diagnostics));
        }

        [Test]
        public void ALookalike_IsStraightenedToTheMendedCode_AndNothingElseIs()
        {
            foreach (FriendlyErrorCase slip in Cases().Where(c => c.Warning))
                Assert.AreEqual(slip.Mended, Lexer.Straightened(slip.Typed), slip.Name);
            foreach (FriendlyErrorCase slip in Cases().Where(c => !c.Warning))
                Assert.AreEqual(slip.Typed, Lexer.Straightened(slip.Typed), slip.Name);

            const string inComments = "// it’s “fine” – here\n/* and ‘here’ */\nmodule m; initial $display(\"it’s\"); endmodule\n";
            Assert.AreSame(inComments, Lexer.Straightened(inComments), "a comment and a string's own text are not code");
        }

        // -----------------------------------------------------------------
        // The review's cases (2026-11-02), each written for these tests
        // -----------------------------------------------------------------

        [Test]
        public void ACurlyApostropheBeforeABrace_IsSaid_AndStraightened()
        {
            const string typed =
                "module m (output logic [1:0] y);\n" +
                "    typedef struct packed { logic hi; logic lo; } pair_t;\n" +
                "    pair_t p;\n" +
                "    assign p = ’{hi: 1'b1, lo: 1'b0};\n" +
                "    assign y = p;\n" +
                "endmodule\n";
            WrittenModule read = Read(typed, new string[0]);
            Assert.IsFalse(read.HasErrors, read.FirstError?.ToString());
            Assert.AreEqual(4, read.Diagnostics.Single(d => d.Code == DiagnosticCodes.LookalikeCharacter).Location.Line);
            StringAssert.Contains("    assign p = '{hi: 1'b1, lo: 1'b0};\n", Lexer.Straightened(typed));
        }

        [Test]
        public void ANoBreakSpaceInCode_IsSaid_AndWrittenAsASpace_ButNotInAComment()
        {
            const string typed = "module m (input logic a, b, output logic y);\n    assign y = a & b;   // a note\nendmodule\n";
            WrittenModule read = Read(typed, new string[0]);
            Assert.IsFalse(read.HasErrors, read.FirstError?.ToString());
            StringAssert.Contains("no-break space", read.Diagnostics.Single(d => d.Code == DiagnosticCodes.LookalikeCharacter).Message);
            Assert.AreEqual("module m (input logic a, b, output logic y);\n    assign y = a & b;   // a note\nendmodule\n",
                Lexer.Straightened(typed));
        }

        [Test]
        public void ADashBesideAMinus_IsKeptApart_SoTheExportReadsAsBitSorterDid()
        {
            Assert.AreEqual("assign y = a - -b;", Lexer.Straightened("assign y = a –-b;"), "a - -b, never a --b");
            Assert.AreEqual("assign y = a - b;", Lexer.Straightened("assign y = a – b;"), "one for one where nothing joins");
        }

        [Test]
        public void AnAssignInAnAlwaysWithNoBegin_IsOneError_AndAMissingEndIsNamed()
        {
            WrittenModule read = Read("module m (input logic a, b, output logic y);\n    always_comb\n        assign y = a & b;\nendmodule\n", new string[0]);
            Diagnostic[] errors = read.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
            Assert.AreEqual(1, errors.Length, "no second error saying the opposite: " + string.Join("\n", errors.Select(e => e.Message)));
            StringAssert.Contains("an end above it may be missing", errors[0].Message);
        }

        [Test]
        public void TheMemoryHint_IsAConventionalBus_AndOnlyForOneRangeOfPlainBits()
        {
            WrittenModule sized = Read(
                "module m (input logic a, output logic [3:0] y);\n    logic s[4];\n    assign y = s;\nendmodule\n", new string[0]);
            StringAssert.Contains("for a 4-bit bus, put the range before the name: logic [3:0] s.", sized.FirstError.Message, "never the [0:3] it was not written as");

            WrittenModule eight = Read(
                "module m (input logic a, output logic [7:0] y);\n    logic s[7:0];\n    assign y = s;\nendmodule\n", new string[0]);
            StringAssert.Contains("for an 8-bit bus", eight.FirstError.Message);

            WrittenModule rows = Read(
                "module m (input logic a, output logic [7:0] y);\n    logic s[3:0][1:0];\n    assign y = s;\nendmodule\n", new string[0]);
            StringAssert.DoesNotContain("bus", rows.FirstError.Message, "rows of words: no single bus to suggest");
        }
    }
}
