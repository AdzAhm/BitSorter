using System.Diagnostics;
using System.Linq;
using System.Text;
using NUnit.Framework;

namespace BitSorter.ViewModel.Tests
{
    /// <summary>
    /// What each character of SystemVerilog is, for its colour (5.1): the browser's Verilog mode's reading, token for token, so
    /// a line reads alike in both builds -- and never thrown off by code half typed.
    /// </summary>
    public class CodeColouringTests
    {
        /// <summary>
        /// The kinds of <paramref name="text"/> as one letter a character -- P plain, K keyword, N number, S string, C comment,
        /// Y system name, O operator -- to compare with what the browser's mode gives each.
        /// </summary>
        private static string Kinds(string text)
        {
            var letters = new StringBuilder(text.Length);
            foreach (CodeKind kind in CodeColouring.Classify(text))
                letters.Append("PKNSCYO"[(int)kind]);
            return letters.ToString();
        }

        [Test]
        public void Keywords_AreTheSimulatorsOwn_AndAWordContainingOneIsAName()
        {
            Assert.AreEqual("KKKKKPPPPPPPP", Kinds("logic logical"));
            Assert.AreEqual("KKKKKKPKKKKKPP", Kinds("module input a"));
            Assert.AreEqual("PPPPPPPPP", Kinds("modulexyz"), "a name, not a keyword with letters after it");
            Assert.AreEqual("KKKKKKKKK", Kinds("always_ff"));
        }

        [Test]
        public void AKeywordInAComment_OrAString_IsTheCommentsOrTheStrings()
        {
            Assert.AreEqual(new string('C', 18) + "P" + "KKKK", Kinds("// module and wire\nwire"));
            Assert.AreEqual("SSSSSSSSSSSSSS", Kinds("\"module input\""));
            Assert.AreEqual("SSSSSSSS", Kinds("\"a\\\"b c\""), "an escaped quote does not end it");
        }

        [Test]
        public void ABlockComment_RunsAcrossLines_ToItsClose()
        {
            string text = "/* one\nmodule two */ wire";
            Assert.AreEqual(new string('C', 20) + "PKKKK", Kinds(text));
        }

        [Test]
        public void CodeHalfTyped_IsNeverThrownOff_AnUnclosedCommentOrStringRunsOn()
        {
            Assert.AreEqual(new string('C', 9), Kinds("/* module"));
            Assert.AreEqual("SSSSSSS" + "P" + "KKKK", Kinds("\"module\nwire"), "a string ends at its line's end where it has no close");
            Assert.AreEqual("P", Kinds("'"));
            Assert.AreEqual("NPN", Kinds("4'1"), "4' with no base after it: its size, a quote, and digits");
            Assert.DoesNotThrow(() => CodeColouring.Classify("$"));
            Assert.DoesNotThrow(() => CodeColouring.Classify("`"));
            Assert.DoesNotThrow(() => CodeColouring.Classify("8'h"));
        }

        [Test]
        public void Numbers_SizedAndBased_WithXAndZ_RealsAndDecimals_AreOneNumberEach()
        {
            Assert.AreEqual("NNNNNNN", Kinds("4'b10x1"));
            Assert.AreEqual("NNNN", Kinds("'hFF"));
            Assert.AreEqual("NNNNN", Kinds("8'sd5"));
            Assert.AreEqual("NNNNNNNN", Kinds("16'hz_?F"));
            Assert.AreEqual("NNN", Kinds("1.5"));
            Assert.AreEqual("NNNN", Kinds("2e-3"));
            Assert.AreEqual("NNNNN", Kinds("1_000"));
            Assert.AreEqual("PP", Kinds("a1"), "a digit in a name is the name's");
        }

        [Test]
        public void SystemNames_AndOperators_AreTheModesMeta_BackTickNamesAndDelaysPlain()
        {
            Assert.AreEqual("YYYYYYYY", Kinds("$display"));
            Assert.AreEqual("POPOP", Kinds("a+b&c"));
            Assert.AreEqual("PPPPPPPPPP", Kinds("`timescale"));
            Assert.AreEqual("PP", Kinds("#5"));
            Assert.AreEqual("PPP", Kinds("a:b"), "a colon is a separator, as the mode reads it");
            Assert.AreEqual("PPP", Kinds("a<b"), "and so is a less-than");
        }

        [Test]
        public void AModule_ReadsAsTheBrowsersModeReadsIt()
        {
            const string Code = "module and2 (input logic a, b, output logic y); // two\n    assign y = a & b; $display(\"%b\", 4'hF);\nendmodule";
            CodeKind[] kinds = CodeColouring.Classify(Code);
            CodeKind At(string word, int from = 0) => kinds[Code.IndexOf(word, from, System.StringComparison.Ordinal)];

            Assert.AreEqual(CodeKind.Keyword, At("module"));
            Assert.AreEqual(CodeKind.Plain, At("and2"), "a name with a keyword's letters in it");
            Assert.AreEqual(CodeKind.Keyword, At("input"));
            Assert.AreEqual(CodeKind.Keyword, At("logic"));
            Assert.AreEqual(CodeKind.Comment, At("// two"));
            Assert.AreEqual(CodeKind.Keyword, At("assign"));
            Assert.AreEqual(CodeKind.Operator, At("="));
            Assert.AreEqual(CodeKind.Operator, At("&"));
            Assert.AreEqual(CodeKind.System, At("$display"));
            Assert.AreEqual(CodeKind.String, At("\"%b\""));
            Assert.AreEqual(CodeKind.Number, At("4'hF"));
            Assert.AreEqual(CodeKind.Keyword, At("endmodule"));
            Assert.AreEqual(CodeKind.Plain, At(";"));
        }

        [Test]
        public void ALargeModule_IsReadInWellUnderAFrame()
        {
            const string Line = "    assign y[3] = (a & b) | 4'b10x1; // a comment with module in it\n";
            string text = string.Concat(Enumerable.Repeat(Line, 5000));
            var kinds = new CodeKind[text.Length];
            CodeColouring.Classify(text, kinds);   // warmed

            var clock = Stopwatch.StartNew();
            CodeColouring.Classify(text, kinds);
            clock.Stop();
            Assert.Less(clock.Elapsed.TotalMilliseconds, 50, $"5,000 lines took {clock.Elapsed.TotalMilliseconds:0.0} ms");
        }
    }
}
