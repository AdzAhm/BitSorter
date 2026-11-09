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
            Survives("module m; assign y = " + string.Concat(Enumerable.Repeat("a + ", 100_000)) + "a; endmodule", "a run of binary operators");
            Survives("module m; assign y = a" + string.Concat(Enumerable.Repeat("[0]", 100_000)) + "; endmodule", "a run of selects");
            Survives("module m; assign y = s" + string.Concat(Enumerable.Repeat(".m()", 100_000)) + "; endmodule", "a run of method calls");
            Survives("module m; " + string.Concat(Enumerable.Repeat("generate ", 100_000)) + " endmodule", "generate regions inside each other");
            Survives("module m; typedef " + string.Concat(Enumerable.Repeat("enum ", 100_000)) + "{A} t; endmodule", "an enum based on an enum, over and over");
            Survives("module m; typedef " + string.Concat(Enumerable.Repeat("struct { ", 50_000)) + "logic a;" + string.Concat(Enumerable.Repeat(" } s;", 50_000)) + " t; endmodule", "structs inside structs");
        }

        /// <summary>
        /// The 16d sweep: a run of operators -- a + a + ... + a, a[0][0]..., s.m().m()... -- is read in a loop, but makes a
        /// tree as deep as it is long, and the printer and the elaborator walk it a call a level, so a long enough one
        /// overflowed the stack, which nothing can catch. Each operator in a row counts as a level now, as a bracket does.
        /// </summary>
        [TestCase("a + ", "a", "")]
        [TestCase("a | ", "a", "")]
        [TestCase("", "a[0]", "[0]")]
        [TestCase("", "s", ".m()")]
        [TestCase("", "s[0]", ".f")]
        public void ALongRunOfOperators_StopsWithAMessage(string before, string last, string after)
        {
            string chain = string.Concat(Enumerable.Repeat(before, 1_000)) + last + string.Concat(Enumerable.Repeat(after, 1_000));
            SyntaxTree tree = SyntaxReader.ReadText("module m; assign y = " + chain + "; endmodule", "chain.sv");
            Assert.Contains(DiagnosticCodes.TooDeep, Codes(tree));
        }

        /// <summary>
        /// The 16d sweep's review: a run past the limit whose operands have selects, as most do, stopped at a select's bracket
        /// with the brackets' message -- "nests more than 200 levels deep", at a bit index -- and never said the run's own, which
        /// says what to do about it.
        /// </summary>
        [TestCase("a[{0}]")]
        [TestCase("a")]
        [TestCase("(a[{0}])")]
        public void ARunPastTheLimit_SaysToSplitIt_WhateverItsOperands(string operand)
        {
            string chain = string.Join(" ^ ", Enumerable.Range(0, Parser.MaxHeight + 10).Select(i => string.Format(operand, i % 8)));
            SyntaxTree tree = SyntaxReader.ReadText("module m (input logic [7:0] a, output logic y);\n  assign y = " + chain + ";\nendmodule\n", "chain.sv");
            Diagnostic first = tree.Diagnostics.First(d => d.Severity == DiagnosticSeverity.Error);
            Assert.AreEqual(DiagnosticCodes.TooDeep, first.Code, first.ToString());
            StringAssert.Contains("Split it over a few assigns", first.Message);
        }

        /// <summary>
        /// The 16d sweep's review: a read stopped as too deep went on to fault the text it left unread -- a ']' or a ';'
        /// missing, the module never ending -- errors for what is not wrong, after the one that is.
        /// </summary>
        [TestCase("assign y = ", "(", "a[0]", ")", ";")]
        [TestCase("assign y = ", "a[0] ^ ", "a[1]", "", ";")]
        [TestCase("initial ", "begin ", "y = 1;", " end", "")]
        [TestCase("typedef ", "struct { ", "logic a;", " } s;", " t;")]
        public void AStoppedRead_SaysNothingOfWhatItLeftUnread(string start, string open, string middle, string close, string end)
        {
            int times = Parser.MaxHeight + 10;
            string deep = start + string.Concat(Enumerable.Repeat(open, times)) + middle + string.Concat(Enumerable.Repeat(close, times)) + end;
            string text = "module m (input logic [7:0] a, output logic y);\n  " + deep + "\n  assign y = a[2] ^ a[3];\nendmodule\n";
            SyntaxTree tree = SyntaxReader.ReadText(text, "deep.sv");
            Assert.AreEqual(new[] { DiagnosticCodes.TooDeep }, tree.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.Code).ToArray(),
                string.Join("\n", tree.Diagnostics));
        }

        /// <summary>
        /// The 16d sweep: generate regions inside each other, an enum based on an enum, and a struct inside a struct each
        /// recursed with no count of how deep, so enough of them overflowed the stack rather than stopping the read.
        /// </summary>
        [TestCase("module m; ", "generate ", "", " endgenerate", " endmodule")]
        [TestCase("module m; typedef ", "enum ", "{A}", "", " t; endmodule")]
        [TestCase("module m; typedef ", "struct { ", "logic a;", " } s;", " t; endmodule")]
        [TestCase("module m; typedef ", "union { ", "logic a;", " } u;", " t; endmodule")]
        public void DeepTypesAndGenerateRegions_StopWithAMessage(string start, string open, string middle, string close, string end)
        {
            string text = start + string.Concat(Enumerable.Repeat(open, 500)) + middle + string.Concat(Enumerable.Repeat(close, 500)) + end;
            SyntaxTree tree = SyntaxReader.ReadText(text, "deep.sv");
            Assert.Contains(DiagnosticCodes.TooDeep, Codes(tree));
        }

        [Test]
        public void ARunOfOperatorsWithinTheLimit_IsRead()
        {
            string chain = string.Join(" ^ ", Enumerable.Range(0, Parser.MaxDepth - 10).Select(i => $"a[{i}]"));
            SyntaxTree tree = Clean("module m (input logic [255:0] a, output logic y); assign y = " + chain + "; endmodule");
            Assert.AreEqual(1, tree.Modules.Count);
        }

        /// <summary>
        /// The 16d sweep: a sized literal is made at its full width as it is read, and kept on the tree, with a cap on one
        /// literal alone -- so 1048576'd0, eleven characters, holds 256 KB, and a hundred thousand bytes of them held 2 GB
        /// before the design's own size was ever asked: past a browser's whole heap.
        /// </summary>
        [Test]
        public void WideLiterals_AreHeldToABudgetForTheRead()
        {
            string text = "module m;\n" +
                string.Concat(Enumerable.Range(0, 1_000).Select(i => $"  localparam logic [1048575:0] P{i} = 1048576'd{i};\n")) +
                "endmodule\n";

            long before = GC.GetAllocatedBytesForCurrentThread();
            SyntaxTree tree = SyntaxReader.ReadText(text, "wide.sv");
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.IsTrue(tree.HasErrors, "a thousand numbers of a million bits are more than a read may hold");
            Assert.Contains(DiagnosticCodes.BadNumber, Codes(tree));
            Assert.AreEqual(1, Codes(tree).Count(c => c == DiagnosticCodes.BadNumber), "said once");
            Assert.Less(allocated, 64_000_000, $"{allocated:N0} bytes allocated reading them");
        }

        [Test]
        public void WideLiteralsWithinTheBudget_AreRead()
        {
            string text = "module m;\n" +
                string.Concat(Enumerable.Range(0, 60).Select(i => $"  localparam logic [1048575:0] P{i} = 1048576'd{i};\n")) +
                "endmodule\n";
            Clean(text);
        }

        /// <remarks>
        /// <c>a &amp; a &amp; a</c> is read in a loop, so it never deepened the reader's own stack, but its tree is as deep as
        /// the chain is long -- each operator over all the ones before it -- and every walk over the tree after the reader, the
        /// elaborator's, the importer's, the printer's and a run's, goes as deep on the machine's: on a thread of a megabyte,
        /// an import of six hundred of them overflowed it, which nothing can catch (2026-11-09).
        /// </remarks>
        [Test]
        public void ALongFlatChain_StopsWithAMessage_AsDeepNestingDoes()
        {
            string chain = "module m; assign y = a" + string.Concat(Enumerable.Repeat(" & a", 100_000)) + "; endmodule";
            SyntaxTree tree = SyntaxReader.ReadText(chain, "chain.sv");
            Assert.Contains(DiagnosticCodes.TooDeep, Codes(tree));
            Survives(chain, "a long flat chain");
            Survives("module m; assign y = a" + string.Concat(Enumerable.Repeat(" + (b", 50_000)) + new string(')', 50_000) + "; endmodule",
                "chains inside brackets");
        }

        /// <remarks>
        /// A chain of selects -- <c>a[0][0]...</c> -- or of members after one, <c>a[0].b.b...</c>, is read in a loop as an
        /// operator chain is, each select over all the ones before it, so its tree is as deep as it is long: on a thread of a
        /// megabyte, an import of two thousand selects overflowed the printer's stack, which nothing can catch (the review of
        /// Part 16d's fixes, 2026-11-09). Only the read is asked here, which goes no deeper for it.
        /// </remarks>
        [Test]
        public void ALongChainOfSelects_StopsWithAMessage_AsDeepNestingDoes()
        {
            string selects = "module m; assign y = a" + string.Concat(Enumerable.Repeat("[0]", 100_000)) + "; endmodule";
            Assert.Contains(DiagnosticCodes.TooDeep, Codes(SyntaxReader.ReadText(selects, "selects.sv")), "a hundred thousand selects");

            string members = "module m; assign y = a[0]" + string.Concat(Enumerable.Repeat(".b", 100_000)) + "; endmodule";
            Assert.Contains(DiagnosticCodes.TooDeep, Codes(SyntaxReader.ReadText(members, "members.sv")), "and as many members after one");
            Survives(selects, "a long chain of selects");
            Survives(members, "a long chain of members");
            Survives("module m; initial $display(q" + string.Concat(Enumerable.Repeat(".size()", 100_000)) + "); endmodule", "a long chain of methods");
            Survives("module m; assign v" + string.Concat(Enumerable.Repeat("[0]", 100_000)) + " = a; endmodule", "a long chain of selects assigned to");
            Survives("module m; assign y = a[0]" + string.Concat(Enumerable.Repeat("[0:0]", 100_000)) + "; endmodule", "a long chain of part-selects");
        }

        [Test]
        public void AChainOfAHundredSelects_IsRead()
        {
            // The pair of the one above: a memory of many dimensions, and a select of it as deep as anyone writes one and more.
            string text = "module m (input logic [3:0] i, output logic y);\n    logic [1:0][1:0] mem [0:1][0:1][0:1];\n" +
                "    assign y = mem[i[0]][i[1]][i[2]][1][0] ^ mem" + string.Concat(Enumerable.Repeat("[0]", 100)) + ";\nendmodule\n";
            SyntaxTree tree = SyntaxReader.ReadText(text, "selects.sv");
            Assert.IsEmpty(tree.Diagnostics);
        }

        /// <remarks>
        /// A struct inside a struct is read by the type's reader calling itself, a level of the machine's stack each, and none
        /// of the reader's depth: two thousand of them overflowed a megabyte's stack in the reader itself (the review of Part
        /// 16d's fixes). Three hundred are asked here, past the depth and short of the stack, so that it is the test that fails.
        /// </remarks>
        [Test]
        public void StructsNestedPastTheDepth_StopWithAMessage()
        {
            foreach (string kind in new[] { "struct", "union" })
            {
                string text = "module m; typedef " + string.Concat(Enumerable.Repeat(kind + " packed { ", 300)) + "logic x;" +
                    string.Concat(Enumerable.Repeat(" } f;", 299)) + " } t; endmodule";
                Assert.Contains(DiagnosticCodes.TooDeep, Codes(SyntaxReader.ReadText(text, "nested.sv")), kind);
                Survives("module m; typedef " + string.Concat(Enumerable.Repeat(kind + " packed { ", 100_000)) + "logic x;" +
                    string.Concat(Enumerable.Repeat(" } f;", 99_999)) + " } t; endmodule", $"{kind}s nested a hundred thousand deep");
            }
        }

        /// <remarks>
        /// Each packed dimension of a type holds the ones after it, <c>logic [3:0][7:0]</c> four bytes, so a type of ten thousand
        /// on one declaration overflowed a megabyte's stack where the elaborator measures it (the same review).
        /// </remarks>
        [Test]
        public void PackedDimensionsPastTheDepth_StopWithAMessage()
        {
            string text = "module m; logic " + string.Concat(Enumerable.Repeat("[0:0]", 100_000)) + " v; endmodule";
            Assert.Contains(DiagnosticCodes.TooDeep, Codes(SyntaxReader.ReadText(text, "packed.sv")));
            Survives(text, "a hundred thousand packed dimensions");
        }

        [Test]
        public void StructsInStructs_AndAHundredDimensions_AreRead()
        {
            // The pair of the two above.
            string text = "module m;\n    typedef " + string.Concat(Enumerable.Repeat("struct packed { logic a; ", 20)) + "logic x;" +
                string.Concat(Enumerable.Repeat(" } f;", 19)) + " } t;\n    t v;\n    logic " + string.Concat(Enumerable.Repeat("[1:0]", 100)) + " w;\nendmodule\n";
            SyntaxTree tree = SyntaxReader.ReadText(text, "nested.sv");
            Assert.IsEmpty(tree.Diagnostics);
        }

        [Test]
        public void AChainOfAHundredOperators_IsRead()
        {
            // The pair of the one above: a sum of products from an eight-input truth table runs to 128 terms, each of 8.
            var products = Enumerable.Range(0, 128).Select(p =>
                "(" + string.Join(" & ", Enumerable.Range(0, 8).Select(b => ((p >> b) & 1) == 1 ? $"x[{b}]" : $"~x[{b}]")) + ")");
            string text = "module m (input logic [7:0] x, output logic y);\n    assign y = " + string.Join(" | ", products) + ";\nendmodule\n";
            SyntaxTree tree = SyntaxReader.ReadText(text, "sop.sv");
            Assert.IsEmpty(tree.Diagnostics);
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
