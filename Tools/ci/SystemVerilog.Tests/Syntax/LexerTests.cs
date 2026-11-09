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

        /// <summary>
        /// The 16d sweep: only \n and \t were read, and any other escaped character was kept as itself -- so "\101" was the
        /// text 101 where Icarus and ModelSim print A, an ANSI colour's \033 lost its escape, and a string continued onto the
        /// next line with a backslash kept the line break, or in a file of CR LF lines was never closed. Every escape of
        /// IEEE 1800's table 5-1, and a backslash before a line's end dropped with it (5.9).
        /// </summary>
        [TestCase("\"\\101\\x42\\103\"", "41 42 43")]
        [TestCase("\"\\v\\f\\a\\033[1m\"", "0b 0c 07 1b 5b 31 6d")]
        [TestCase("\"\\0\\7\\x4\\xfF!\"", "00 07 04 ff 21")]
        [TestCase("\"\\1234\"", "53 34")]
        [TestCase("\"ab\\\ncd\"", "61 62 63 64")]
        [TestCase("\"ab\\\r\ncd\"", "61 62 63 64")]
        [TestCase("\"\\\\ \\\" \\q\"", "5c 20 22 20 71")]
        public void Strings_ReadEveryEscapeOfTheStandard(string source, string codes)
        {
            Token s = Lex(source).Single();
            Assert.AreEqual(TokenKind.String, s.Kind);
            Assert.AreEqual(codes, string.Join(" ", s.Text.Select(c => ((int)c).ToString("x2"))), "the characters, by their codes");
        }

        /// <summary>
        /// The 16d sweep: a string was ended by any of the three double quotes, however it began, so a curly quote typed
        /// inside an ordinary string -- text from a word processor -- cut it short, and the rest of the line became stray
        /// tokens. A string opened with a plain quote ends only at one; one opened with a curly quote, copied from a slide,
        /// still ends at either kind, with a warning.
        /// </summary>
        [Test]
        public void ACurlyQuoteInsideAPlainString_IsPartOfIt()
        {
            Token s = Lex("\"He said “hi” to us\"").Single();
            Assert.AreEqual(TokenKind.String, s.Kind);
            Assert.AreEqual("He said “hi” to us", s.Text);
            Assert.AreEqual("$display(\"“hi”\");", Lexer.Straightened("$display(\"“hi”\");"), "and left as written when straightened");

            Token[] curly = Lex("“hi” x", out DiagnosticBag diagnostics);
            Assert.AreEqual(new[] { "hi", "x" }, curly.Select(t => t.Text).ToArray(), "a curly string still ends at a curly quote");
            Assert.AreEqual(DiagnosticCodes.LookalikeCharacter, diagnostics.Items.Single().Code);
        }

        /// <summary>
        /// The 16d sweep's review: a string opened with a plain quote and closed with a curly one -- "mixed” -- read before
        /// ACurlyQuoteInsideAPlainString_IsPartOfIt, and since then was never closed, with errors down the next line, where
        /// ModelSim warns and goes on. A plain quote further along its line ends it, the curly ones before it text; with none,
        /// its last curly quote ends it, read as a plain one with a warning, as every look-alike is, and straightened so.
        /// </summary>
        [TestCase("$display(\"mixed”);", "mixed", "$display(\"mixed\");")]
        [TestCase("$display(\"mixed“);", "mixed", "$display(\"mixed\");")]
        [TestCase("$display(\"He said “hi”);", "He said “hi", "$display(\"He said “hi\");")]
        [TestCase("$display(\"a\\\"b”);", "a\"b", "$display(\"a\\\"b\");")]
        public void APlainStringClosedByACurlyQuote_EndsAtItsLastOne(string source, string text, string straightened)
        {
            Token[] tokens = Lex(source + "\n$display(\"next\");", out DiagnosticBag diagnostics);
            Assert.AreEqual(new[] { "$display", "(", text, ")", ";", "$display", "(", "next", ")", ";" }, tokens.Select(t => t.Text).ToArray());
            Assert.AreEqual(TokenKind.String, tokens[2].Kind);
            Assert.AreEqual(DiagnosticCodes.LookalikeCharacter, diagnostics.Items.Single().Code, string.Join("\n", diagnostics.Items));
            Assert.AreEqual(straightened + "\n$display(\"next\");", Lexer.Straightened(source + "\n$display(\"next\");"));
        }

        [Test]
        public void AStringOfEveryEscape_IsPrintedBackAsItReads()
        {
            const string written = "module m; initial $display(\"\\033[1m\\v\\f\\a\\001 \\\\ \\\" done\\n\\t\"); endmodule";
            SyntaxTree tree = SyntaxReader.ReadText(written, "t.sv");
            string printed = Printer.Print(tree);
            StringAssert.Contains("\\033[1m\\v\\f\\a\\001 \\\\ \\\" done\\n\\t", printed);

            SyntaxTree again = SyntaxReader.ReadText(printed, "t.sv");
            Assert.AreEqual(printed, Printer.Print(again), "a fixed point");
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
