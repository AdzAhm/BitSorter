using System.Linq;
using BitSorter.SystemVerilog.Syntax;
using NUnit.Framework;

namespace BitSorter.SystemVerilog.Tests.Syntax
{
    /// <summary>The lexer: what each kind of text becomes, where it was, and that nothing breaks it.</summary>
    public class LexerTests
    {
        private static Token[] Lex(string text, out DiagnosticBag diagnostics)
        {
            diagnostics = new DiagnosticBag();
            return Lexer.Lex(text, "t.sv", diagnostics).Where(t => t.Kind != TokenKind.EndOfFile).ToArray();
        }

        private static Token[] Lex(string text)
        {
            Token[] tokens = Lex(text, out DiagnosticBag diagnostics);
            Assert.IsEmpty(diagnostics.Items, string.Join("\n", diagnostics.Items));
            return tokens;
        }

        [Test]
        public void NamesKeywordsAndSystemNames_AreTold_Apart()
        {
            Token[] t = Lex("module adder logic $display `WIDTH a_1$x");
            Assert.AreEqual(new[] { TokenKind.Keyword, TokenKind.Identifier, TokenKind.Keyword, TokenKind.SystemName, TokenKind.Directive, TokenKind.Identifier },
                t.Select(x => x.Kind).ToArray());
            Assert.AreEqual("a_1$x", t[5].Text);
        }

        [TestCase("8'hA5", "8'hA5")]
        [TestCase("8 'h A5", "8'hA5")]
        [TestCase("4'b1x0z", "4'b1x0z")]
        [TestCase("12'b1010_1101_0001", "12'b1010_1101_0001")]
        [TestCase("'d7", "'d7")]
        [TestCase("16'sd5", "16'sd5")]
        [TestCase("'0", "'0")]
        [TestCase("'z", "'z")]
        [TestCase("32'd10", "32'd10")]
        [TestCase("42", "42")]
        public void ANumber_IsOneToken_HoweverItIsSpaced(string text, string joined)
        {
            Token[] t = Lex(text);
            Assert.AreEqual(1, t.Length, string.Join(" | ", t.Select(x => x.Text)));
            Assert.AreEqual(TokenKind.Number, t[0].Kind);
            Assert.AreEqual(joined, t[0].Text);
        }

        [Test]
        public void ASizeCast_IsANumberThenAnApostrophe()
        {
            Token[] t = Lex("8'(x)");
            Assert.AreEqual(new[] { "8", "'", "(", "x", ")" }, t.Select(x => x.Text).ToArray());
        }

        [Test]
        public void LongestSymbolsWin()
        {
            Token[] t = Lex("a >>> 2 === b <= c +: 4 ~^ d");
            Assert.AreEqual(new[] { "a", ">>>", "2", "===", "b", "<=", "c", "+:", "4", "~^", "d" }, t.Select(x => x.Text).ToArray());
        }

        [Test]
        public void EveryToken_KnowsItsLineAndColumn()
        {
            Token[] t = Lex("module m;\n  logic  a;\nendmodule");
            Token a = t.Single(x => x.Text == "a");
            Assert.AreEqual(2, a.Location.Line);
            Assert.AreEqual(10, a.Location.Column);
            Assert.AreEqual("t.sv", a.Location.File);
        }

        [Test]
        public void ALineComment_IsKeptOnTheTokenBeforeIt()
        {
            Token[] t = Lex("input logic a, // the first operand\ninput logic b");
            Assert.AreEqual("the first operand", t.Single(x => x.Text == ",").TrailingComment);
            Assert.IsNull(t.Last().TrailingComment);
        }

        [Test]
        public void BlockComments_AreSkipped_AndAnUnclosedOneIsReported()
        {
            Assert.AreEqual(new[] { "a", "b" }, Lex("a /* x\ny */ b").Select(x => x.Text).ToArray());

            Lex("a /* never closed", out DiagnosticBag diagnostics);
            Assert.AreEqual(DiagnosticCodes.UnterminatedComment, diagnostics.Items.Single().Code);
        }

        [Test]
        public void CharactersCopiedFromASlide_ReadAsWhatTheyLookLike_WithAWarning()
        {
            Token[] t = Lex("1’b1 – c", out DiagnosticBag diagnostics);
            Assert.AreEqual(new[] { "1'b1", "-", "c" }, t.Select(x => x.Text).ToArray());
            Assert.AreEqual(2, diagnostics.Items.Count);
            Assert.IsTrue(diagnostics.Items.All(d => d.Severity == DiagnosticSeverity.Warning && d.Code == DiagnosticCodes.LookalikeCharacter));
        }

        [Test]
        public void Strings_ReadTheirEscapes()
        {
            Token s = Lex("\"a=%d\\n\"").Single();
            Assert.AreEqual(TokenKind.String, s.Kind);
            Assert.AreEqual("a=%d\n", s.Text);
        }

        [Test]
        public void ABackslashAtALinesEnd_ContinuesTheLogicalLine()
        {
            Token[] t = Lex("`define X a \\\n b\nc");
            Assert.AreEqual(t[0].LogicalLine, t.Single(x => x.Text == "b").LogicalLine);
            Assert.AreNotEqual(t[0].LogicalLine, t.Single(x => x.Text == "c").LogicalLine);
        }

        [Test]
        public void AnUnexpectedCharacter_IsReportedAndSkipped()
        {
            Token[] t = Lex("a § b", out DiagnosticBag diagnostics);
            Assert.AreEqual(new[] { "a", "b" }, t.Select(x => x.Text).ToArray());
            Assert.AreEqual(DiagnosticCodes.UnexpectedCharacter, diagnostics.Items.Single().Code);
        }

        [Test]
        public void Reals_AreTheirOwnKind()
        {
            Assert.AreEqual(TokenKind.RealNumber, Lex("1.5").Single().Kind);
            Assert.AreEqual(TokenKind.RealNumber, Lex("2e-3").Single().Kind);
        }
    }
}
