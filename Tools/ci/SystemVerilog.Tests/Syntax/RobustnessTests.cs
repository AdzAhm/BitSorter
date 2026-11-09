using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using BitSorter.SystemVerilog.Syntax;
using NUnit.Framework;
using static BitSorter.SystemVerilog.Tests.Syntax.SyntaxKit;

namespace BitSorter.SystemVerilog.Tests.Syntax
{
    /// <summary>
    /// Whatever it is given, the reader never throws and never hangs (Docs/architecture-5.md,
    /// invariant 2): a student's half-written file, a file cut off anywhere, random tokens, and
    /// nesting deep enough to blow a browser's stack all end in diagnostics.
    /// </summary>
    public class RobustnessTests
    {
        private static void Survives(string text, string why)
        {
            var timer = Stopwatch.StartNew();

            try
            {
                SyntaxTree tree = SyntaxReader.ReadText(text, "fuzz.sv");
                Printer.Print(tree);
            }
            catch (Exception e)
            {
                Assert.Fail($"{why}: the reader threw {e.GetType().Name}: {e.Message}\n---\n{text}");
            }

            Assert.Less(timer.ElapsedMilliseconds, 5000, $"{why}: the read took too long -- a loop that does not advance?");
        }

        [Test]
        public void EveryCorpusFile_CutOffAtEveryPoint_IsSurvived()
        {
            string root = CorpusFolder("");
            foreach (string relative in CorpusFiles())
            {
                string text = File.ReadAllText(Path.Combine(root, relative));
                int step = Math.Max(1, text.Length / 300);

                for (int cut = 0; cut < text.Length; cut += step)
                    Survives(text.Substring(0, cut), $"{relative} cut at {cut}");
            }
        }

        [Test]
        public void RandomTokenSoup_IsSurvived()
        {
            string[] words =
            {
                "module", "endmodule", "begin", "end", "if", "else", "case", "endcase", "assign", "always_ff", "always_comb",
                "initial", "for", "generate", "endgenerate", "genvar", "logic", "input", "output", "(", ")", "[", "]", "{", "}",
                ";", ",", ":", "=", "<=", "#", "@", "posedge", "a", "b", "clk", "8'hFF", "1", "+", "-", "?", ".", "function",
                "endfunction", "typedef", "enum", "`define", "`ifdef", "`endif", "`X", "\"s\"", "$display", "repeat", "'{",
            };

            var random = new Random(4242);
            for (int round = 0; round < 400; round++)
            {
                var text = new StringBuilder();
                int length = random.Next(1, 80);
                for (int i = 0; i < length; i++)
                    text.Append(words[random.Next(words.Length)]).Append(random.Next(4) == 0 ? "\n" : " ");

                Survives(text.ToString(), $"soup round {round}");
            }
        }

        [Test]
        public void RandomCharacters_AreSurvived()
        {
            var random = new Random(7);
            const string alphabet = "abcxyz019_'\"`$#@()[]{};:,.=<>+-*/%&|^~!? \n\t\\";

            for (int round = 0; round < 300; round++)
            {
                var text = new StringBuilder();
                int length = random.Next(1, 200);
                for (int i = 0; i < length; i++)
                    text.Append(alphabet[random.Next(alphabet.Length)]);

                Survives(text.ToString(), $"characters round {round}");
            }
        }

        [Test]
        public void DeepNesting_StopsWithAMessage_NotAStackOverflow()
        {
            string parens = "module m; assign y = " + new string('(', 100_000) + "a" + new string(')', 100_000) + "; endmodule";
            SyntaxTree tree = SyntaxReader.ReadText(parens, "deep.sv");
            Assert.Contains(DiagnosticCodes.TooDeep, Codes(tree));

            var blocks = new StringBuilder("module m; initial ");
            for (int i = 0; i < 50_000; i++) blocks.Append("begin ");
            Survives(blocks.ToString(), "nested begins");

            Survives("module m; assign y = " + string.Concat(Enumerable.Repeat("~", 100_000)) + "a; endmodule", "a chain of unary operators");
            Survives("module m; assign y = " + new string('{', 50_000) + "a" + new string('}', 50_000) + "; endmodule", "nested braces");
        }

        [Test]
        public void ALongFlatFile_IsReadQuickly()
        {
            var text = new StringBuilder("module big;\n");
            for (int i = 0; i < 20_000; i++)
                text.Append("  assign w").Append(i).Append(" = a").Append(i).Append(" & b").Append(i).Append(";\n");
            text.Append("endmodule\n");

            var timer = Stopwatch.StartNew();
            SyntaxTree tree = SyntaxReader.ReadText(text.ToString(), "big.sv");
            Assert.IsEmpty(tree.Diagnostics);
            Assert.AreEqual(20_000, tree.Modules.Single().Items.Count);
            Assert.Less(timer.ElapsedMilliseconds, 3000, "20,000 assigns should read in well under a second or two");
        }

        [Test]
        public void TooManyErrors_AreCapped()
        {
            string nonsense = string.Concat(Enumerable.Repeat("§ ", 5000));
            SyntaxTree tree = SyntaxReader.ReadText(nonsense, "n.sv");
            Assert.LessOrEqual(tree.Diagnostics.Count, DiagnosticBag.MaxItems);
            Assert.AreEqual(DiagnosticBag.MaxItems, tree.Diagnostics.Count, "capped, not empty: five thousand faults give the cap's worth");
            Assert.IsTrue(tree.HasErrors);
        }

        /// <summary>
        /// The 16d sweep: the cap was one for every kind, so code pasted from a slide -- every indent a no-break space, each
        /// one a warning -- filled it, and a fault after them was dropped: the tree had no error, and the assign it lost was
        /// silently missing from the run.
        /// </summary>
        [Test]
        public void AnErrorAfterTheCapOfWarnings_IsStillAnError()
        {
            string pasted = string.Concat(Enumerable.Repeat(" ", 600)) + "module m (output logic y); assign y = ; endmodule";
            SyntaxTree tree = SyntaxReader.ReadText(pasted, "pasted.sv");

            Assert.Greater(Codes(tree).Count(c => c == DiagnosticCodes.LookalikeCharacter), DiagnosticBag.MaxItems / 2, "the warnings that fill the cap");
            Assert.IsTrue(tree.HasErrors, "the fault after them");
            Assert.Contains(DiagnosticCodes.Expected, Codes(tree));
        }

        [Test]
        public void TheBag_CapsWarningsAndErrorsApart()
        {
            var bag = new DiagnosticBag();
            var at = new SourceLocation("a.sv", 1, 1);
            for (int i = 0; i < DiagnosticBag.MaxItems * 2; i++)
                bag.Warning(at, DiagnosticCodes.LookalikeCharacter, "a warning");
            Assert.IsFalse(bag.HasErrors);

            bag.Error(at, DiagnosticCodes.Expected, "an error after them");
            Assert.IsTrue(bag.HasErrors);
            Assert.AreEqual(DiagnosticSeverity.Error, bag.Items[bag.Items.Count - 1].Severity, "kept, after the warnings it followed");

            for (int i = 0; i < DiagnosticBag.MaxItems * 2; i++)
                bag.Error(at, DiagnosticCodes.Expected, "more");
            Assert.AreEqual(DiagnosticBag.MaxItems, bag.Items.Count(d => d.Severity == DiagnosticSeverity.Error), "errors capped on their own");
            Assert.AreEqual(DiagnosticBag.MaxItems, bag.Items.Count(d => d.Severity != DiagnosticSeverity.Error), "warnings capped on their own");
        }
    }
}
