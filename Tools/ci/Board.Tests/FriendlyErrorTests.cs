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
    }
}
