using System;
using System.Collections.Generic;
using System.Linq;
using BitSorter.SystemVerilog.Syntax;
using NUnit.Framework;
using static BitSorter.SystemVerilog.Tests.Syntax.SyntaxKit;

namespace BitSorter.SystemVerilog.Tests.Syntax
{
    /// <summary>Includes, macros, conditionals and the timescale, through the whole read.</summary>
    public class PreprocessorTests
    {
        [Test]
        public void AnInclude_SplicesTheFileIn_AndItsTokensKeepTheirOwnFile()
        {
            var files = new MemorySourceFiles()
                .Add("lib/cells.sv", "module inv(input logic a, output logic z);\n  not g(z, a);\nendmodule\n")
                .Add("lib/top.sv", "`include \"cells.sv\"\nmodule top; endmodule\n");

            SyntaxTree tree = SyntaxReader.Read("lib/top.sv", files);

            Assert.IsEmpty(tree.Diagnostics, string.Join("\n", tree.Diagnostics));
            Assert.AreEqual(new[] { "inv", "top" }, tree.Modules.Select(m => m.Name).ToArray());
            Assert.AreEqual("lib/cells.sv", tree.Modules[0].Location.File, "an included module points into the file it is in");
            Assert.AreEqual(new[] { "lib/top.sv", "lib/cells.sv" }, tree.Files.ToArray());
        }

        [Test]
        public void SeveralFiles_ReadAsOneDesign_EachOnItsOwn()
        {
            // As a simulator compiles a list of files: every module of each, in order, and a `define in
            // one does not reach the next.
            var files = new MemorySourceFiles()
                .Add("cells.sv", "`define WIDTH 4\nmodule INV (input logic A, output logic Z); not (Z, A); endmodule\n")
                .Add("top.sv", "module top; logic a, y; INV g1 (.A(a), .Z(y)); logic [`WIDTH-1:0] w; endmodule\n");

            SyntaxTree tree = SyntaxReader.ReadAll(new[] { "cells.sv", "top.sv", "nowhere.sv" }, files);

            CollectionAssert.AreEqual(new[] { "INV", "top" }, tree.Modules.Select(m => m.Name));
            CollectionAssert.AreEqual(new[] { "cells.sv", "top.sv" }, tree.Files);
            Assert.IsTrue(tree.Diagnostics.Any(d => d.Message.Contains("WIDTH")), "the macro stayed in cells.sv");
            Assert.IsTrue(tree.Diagnostics.Any(d => d.Message.Contains("nowhere.sv")), "and a file that is not there is said");
        }

        [Test]
        public void AMissingInclude_SaysWhichFile()
        {
            SyntaxTree tree = Read("`include \"nowhere.sv\"\nmodule m; endmodule");
            Diagnostic d = tree.Diagnostics.Single();
            Assert.AreEqual(DiagnosticCodes.IncludeNotFound, d.Code);
            StringAssert.Contains("nowhere.sv", d.Message);
            Assert.AreEqual(1, tree.Modules.Count, "the rest of the file is still read");
        }

        [Test]
        public void AnIncludeCycle_IsReported_NotFollowedForever()
        {
            var files = new MemorySourceFiles().Add("a.sv", "`include \"b.sv\"").Add("b.sv", "`include \"a.sv\"");
            SyntaxTree tree = SyntaxReader.Read("a.sv", files);
            Assert.AreEqual(DiagnosticCodes.IncludeCycle, tree.Diagnostics.Single().Code);
        }

        [Test]
        public void IncludeGuards_KeepAFileFromBeingReadTwice()
        {
            var files = new MemorySourceFiles()
                .Add("cells.sv", "`ifndef CELLS\n`define CELLS\nmodule inv(input logic a, output logic z); endmodule\n`endif\n")
                .Add("top.sv", "`include \"cells.sv\"\n`include \"cells.sv\"\nmodule top; endmodule\n");

            SyntaxTree tree = SyntaxReader.Read("top.sv", files);
            Assert.IsEmpty(tree.Diagnostics);
            Assert.AreEqual(new[] { "inv", "top" }, tree.Modules.Select(m => m.Name).ToArray());
        }

        [Test]
        public void AMacro_StandsForItsTokens()
        {
            var assign = (ContinuousAssign)Items("`define WIDTH 8\n`define MASK (`WIDTH - 1)\nassign y = `MASK;").Single();
            Assert.AreEqual("(8 - 1)", Printer.Print(assign.Assignments[0].Value));
        }

        [Test]
        public void AMacroWithArguments_PutsInWhatAUseGives_ItsDefaults_JoinedNames_AndStrings()
        {
            var items = Items(
                "`define MAX(a, b = 0) ((a) > (b) ? (a) : (b))\n" +
                "`define REG(name, w = 4) logic [w-1:0] name``_q;\n" +
                "`define SAY(x) $display(`\"x is %0d`\", x)\n" +
                "`REG(count)\n" +
                "assign y = `MAX(p,\n    q + 1);\n" +
                "assign z = `MAX(`MAX(r), {s, t});\n" +
                "initial `SAY(count_q);");

            var reg = (DataDeclaration)items[0];
            Assert.AreEqual("count_q", reg.Declarators.Single().Name, "`` joins the argument to _q");
            Assert.AreEqual("logic [4-1:0] count_q;", Printer.Print(Clean(Printer.Print(OnlyModule("module m;\n`define REG(name, w = 4) logic [w-1:0] name``_q;\n`REG(count)\nendmodule\n")))).Split('\n')[1].Trim());

            Assert.AreEqual("((p) > (q + 1) ? (p) : (q + 1))", Printer.Print(((ContinuousAssign)items[1]).Assignments[0].Value), "an argument may run over lines");
            Assert.AreEqual("((((r) > (0) ? (r) : (0))) > ({s, t}) ? (((r) > (0) ? (r) : (0))) : ({s, t}))",
                Printer.Print(((ContinuousAssign)items[2]).Assignments[0].Value), "a use inside a use, and a comma inside braces");

            var say = (CallStatement)((ProceduralBlock)items[3]).Body;
            Assert.AreEqual("count_q is %0d", ((StringLiteral)say.Arguments[0]).Value, "`\"...`\" is a string with the argument put in");
        }

        [Test]
        public void EachModule_CountsInTheTimescaleWrittenBeforeIt()
        {
            SyntaxTree tree = Read("module first; endmodule\n`timescale 1ns/1ps\nmodule second; endmodule\nmodule third; endmodule\n`timescale 10ns / 1ns\nmodule fourth; endmodule");
            Assert.IsEmpty(tree.Diagnostics, string.Join("\n", tree.Diagnostics));
            CollectionAssert.AreEqual(new[] { null, "1ns/1ps", "1ns/1ps", "10ns/1ns" }, tree.Modules.Select(m => m.Timescale));
            Assert.AreEqual("10ns/1ns", tree.Timescale, "the tree keeps the last, as it always has");

            Assert.IsTrue(TimeUnits.TryTimescale("10ns/1ns", out int unit, out int precision));
            Assert.AreEqual((-8, -9), (unit, precision));
            Assert.IsTrue(TimeUnits.TryTimescale("100 ps / 10 fs", out unit, out precision));
            Assert.AreEqual((-10, -14), (unit, precision));
            Assert.IsFalse(TimeUnits.TryTimescale("1ns/10ns", out _, out _), "a precision coarser than the unit");
            Assert.IsFalse(TimeUnits.TryTimescale("2ns/1ns", out _, out _), "only 1, 10 and 100 of a unit");
        }

        [TestCase("`define TWO(a, b) a + b\nmodule m; assign y = `TWO(1, 2, 3); endmodule")]
        [TestCase("`define TWO(a, b) a + b\nmodule m; assign y = `TWO(1); endmodule")]
        [TestCase("`define TWO(a, b) a + b\nmodule m; assign y = `TWO; endmodule")]
        [TestCase("`define TWO(a, b a + b\nmodule m; endmodule")]
        public void AMacroUsedWithTheWrongArguments_IsReportedPlainly(string text)
        {
            SyntaxTree tree = Read(text);
            Assert.Contains(DiagnosticCodes.MacroArguments, Codes(tree), string.Join("\n", tree.Diagnostics));
        }

        [Test]
        public void AnUndefinedMacro_IsReported()
        {
            SyntaxTree tree = Read("module m; assign y = `NOPE; endmodule");
            Assert.Contains(DiagnosticCodes.UndefinedMacro, Codes(tree));
        }

        [Test]
        public void IfdefElseEndif_KeepOneBranch()
        {
            const string text =
                "`define FAST\n" +
                "module m;\n" +
                "`ifdef FAST\n assign y = 1;\n`elsif SLOW\n assign y = 2;\n`else\n assign y = 3;\n`endif\n" +
                "`ifndef FAST\n assign z = 4;\n`endif\n" +
                "endmodule\n";

            ModuleDeclaration m = Clean(text).Modules.Single();
            var assign = (ContinuousAssign)m.Items.Single();
            Assert.AreEqual("1", Printer.Print(assign.Assignments[0].Value));
        }

        [Test]
        public void AnUnclosedIfdef_IsReported()
        {
            Assert.Contains(DiagnosticCodes.UnbalancedConditional, Codes(Read("`ifdef X\nmodule m; endmodule\n")));
            Assert.Contains(DiagnosticCodes.UnbalancedConditional, Codes(Read("module m; endmodule\n`endif\n")));
        }

        [Test]
        public void TheTimescale_IsNoted()
        {
            Assert.AreEqual("1ns/1ps", Clean("`timescale 1ns/1ps\nmodule m; endmodule").Timescale);
        }

        /// <summary>
        /// The 16d sweep: a backslash ending a // comment continued the line whatever the line was, so a `timescale or an
        /// `include whose comment ended in a Windows path's backslash took the next line into its arguments, and a module
        /// written there was gone without a word. Icarus and ModelSim continue only a `define through such a comment -- its
        /// body goes on -- and that one still does.
        /// </summary>
        [Test]
        public void ABackslashEndingAComment_ContinuesOnlyADefine()
        {
            SyntaxTree timescale = Clean("`timescale 1ns/1ps // C:\\work\\\nmodule m; endmodule\n");
            Assert.AreEqual(new[] { "m" }, timescale.Modules.Select(m => m.Name).ToArray(), "the module after a `timescale");
            Assert.AreEqual("1ns/1ps", timescale.Timescale);

            var files = new MemorySourceFiles()
                .Add("cells.sv", "module inv; endmodule\n")
                .Add("top.sv", "`include \"cells.sv\" // from C:\\work\\\r\nmodule top; endmodule\r\n");
            SyntaxTree include = SyntaxReader.Read("top.sv", files);
            Assert.IsEmpty(include.Diagnostics, string.Join("\n", include.Diagnostics));
            Assert.AreEqual(new[] { "inv", "top" }, include.Modules.Select(m => m.Name).ToArray(), "the module after an `include");

            ModuleDeclaration define = OnlyModule("`define X 1 // goes on \\\n+ 2\nmodule m; assign y = `X; endmodule\n");
            Assert.AreEqual("1 + 2", Printer.Print(((ContinuousAssign)define.Items.Single()).Assignments[0].Value),
                "a `define's body goes on, as Icarus and ModelSim read it");
        }

        [Test]
        public void AMacroThatExpandsIntoItself_StopsWithAMessage()
        {
            SyntaxTree tree = Read("`define LOOP `LOOP\nmodule m; assign y = `LOOP; endmodule");
            Assert.Contains(DiagnosticCodes.MacroTooDeep, Codes(tree));
        }

        /// <summary>
        /// Macros that each use the one before twice ask for two to the power of the chain's length -- here two million
        /// tokens, or two million uses of an empty macro, from a few lines and well inside the depth limit. A chain of 31
        /// asks for two thousand million, which would run a browser out of memory, or a desktop out of time: the reader
        /// stops it with a message instead of making them all (an import is untrusted text, Part 13f).
        /// </summary>
        [TestCase("x")]
        [TestCase("")]
        public void MacrosThatDoubleOverAndOver_StopWithAMessage(string bottom)
        {
            var text = new System.Text.StringBuilder("`define M0 " + bottom + "\n");
            for (int i = 1; i <= 21; i++)
                text.Append("`define M").Append(i).Append(" `M").Append(i - 1).Append(" `M").Append(i - 1).Append('\n');
            text.Append("module m; wire y = `M21; endmodule\n");

            SyntaxTree tree = Read(text.ToString());

            Assert.IsTrue(tree.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error && d.Message.Contains("expand to more than")),
                "stopped, with a message: " + string.Join("\n", tree.Diagnostics.Take(3)));
        }

        /// <summary>
        /// A macro of many <c>``</c> joins is expanded in time that grows with its length, not its square (Part 13f's review):
        /// each join took the two tokens out of a list one at a time, moving everything after them, so 300,000 joins -- a
        /// 1.4 MB file, inside an import's limits -- took 18 seconds. Two hundred thousand stay inside what macros may make.
        /// </summary>
        [Test]
        public void AMacroOfManyJoins_IsExpandedInLinearTime()
        {
            var text = new System.Text.StringBuilder("`define J(x)");
            for (int i = 0; i < 200_000; i++)
                text.Append(" a``b");
            text.Append("\nmodule m; localparam P = `J(1); endmodule\n");

            var clock = System.Diagnostics.Stopwatch.StartNew();
            SyntaxTree tree = Read(text.ToString());
            double seconds = clock.Elapsed.TotalSeconds;

            Assert.IsFalse(tree.Diagnostics.Any(d => d.Message.Contains("expand to more than")), "joined, not stopped");
            Assert.Less(seconds, 2, "joined in a moment");
        }

        /// <summary>
        /// The 16d sweep: every directive token -- each use of a macro among them -- had the rest of its line copied out for
        /// it before it was looked at, though a use never reads it: a line of N uses, or a macro body of N uses (a body is one
        /// line), cost N squared over two copies, uncharged by the expansion budget.
        /// </summary>
        [TestCase("line")]
        [TestCase("body")]
        public void ManyMacroUsesOnOneLine_AreWorkedInLinearTime(string where)
        {
            string uses = string.Concat(Enumerable.Repeat("`A ", 20_000));
            string text = where == "line"
                ? "`define A x\n" + uses + "\n"
                : "`define A x\n`define B " + uses + "\nmodule m; `B endmodule\n";
            var diagnostics = new DiagnosticBag();
            Preprocessor.Run("test.sv", text, null, diagnostics, out _);

            long before = GC.GetAllocatedBytesForCurrentThread();
            List<Token> tokens = Preprocessor.Run("test.sv", text, null, diagnostics, out _);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.AreEqual(20_000, tokens.Count(t => t.Text == "x"), "every use expanded");
            Assert.Less(allocated, 40_000_000, $"{allocated:N0} bytes allocated working 20,000 uses on one line");
        }

        /// <summary>
        /// The 16d sweep: a use was charged one and each token it put out one, but a token of its body in a dropped stretch
        /// put out nothing and was charged nothing, though every use still copied the whole body and walked it -- so macros
        /// doubling over a body that is mostly `ifdef'd away walked it hundreds of thousands of times before the cap.
        /// </summary>
        [Test]
        public void ABodyMostlyDroppedByAnIfdef_IsStillPaidFor()
        {
            string text = Doubling(16, "`define M0 `ifdef NOPE " + string.Concat(Enumerable.Repeat("a ", 2_000)) + "`endif\n",
                "`define M{i} `M{j} `M{j}\n", "module m; `M{n} endmodule\n");
            Read(text);

            long before = GC.GetAllocatedBytesForCurrentThread();
            SyntaxTree tree = Read(text);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.AreEqual(1, tree.Diagnostics.Count(d => d.Code == DiagnosticCodes.MacroTooMuch),
                "65,536 uses of a body of 2,003 tokens walk more than a million: " + string.Join("\n", tree.Diagnostics.Take(3)));
            Assert.Less(allocated, 50_000_000, $"{allocated:N0} bytes allocated before it was stopped");
        }

        private static string Doubling(int length, string bottom, string step, string use)
        {
            var text = new System.Text.StringBuilder(bottom);
            for (int i = 1; i <= length; i++)
                text.Append(step.Replace("{i}", i.ToString()).Replace("{j}", (i - 1).ToString()));
            return text.Append(use.Replace("{n}", length.ToString())).ToString();
        }

        /// <summary>
        /// What the review of Part 13f found the cap did not yet pay for, each charged before it is made now: arguments put
        /// in -- a chain that doubles its argument, or one macro using a large argument thousands of times -- and a string a
        /// <c>`"...`"</c> spells out. Each is stopped in moments, at the use, and said once.
        /// </summary>
        [TestCase("argument doubling")]
        [TestCase("one wide use")]
        [TestCase("stringify")]
        public void WhatAMacroCopiesIn_IsPaidForBeforeItIsMade(string how)
        {
            string text = how switch
            {
                "argument doubling" => Doubling(31, "`define L0(x) x\n", "`define L{i}(x) `L{j}(x x)\n", "module m; wire y = `L{n}(1); endmodule\n"),
                "one wide use" => "`define X(a) " + string.Join(" ", Enumerable.Repeat("a", 4000)) + "\nmodule m; wire y = `X(" + string.Join(" + ", Enumerable.Repeat("1", 2000)) + "); endmodule\n",
                _ => "`define S(a) `\"" + string.Join(" ", Enumerable.Repeat("a", 4000)) + "`\"\nmodule m; string s = `S(" + string.Join(" ", Enumerable.Repeat("w", 4000)) + "); endmodule\n",
            };

            var clock = System.Diagnostics.Stopwatch.StartNew();
            SyntaxTree tree = Read(text);

            Assert.Less(clock.Elapsed.TotalSeconds, 5, "stopped before it was made");
            Assert.AreEqual(1, tree.Diagnostics.Count(d => d.Code == DiagnosticCodes.MacroTooMuch), string.Join("\n", tree.Diagnostics.Take(3)));
        }

        /// <summary>
        /// Files that each include the next twice make two to the power of the chain's length -- twenty-five tiny files, inside
        /// an import's limits and the depth of 32, ask for thirty million splices (Part 13f review): each file included is paid
        /// for by its tokens, and the chain stopped at an include.
        /// </summary>
        [Test]
        public void IncludesThatDoubleOverAndOver_StopWithAMessage()
        {
            var files = new MemorySourceFiles();
            for (int i = 0; i < 25; i++)
                files.Add($"f{i}.svh", $"`include \"f{i + 1}.svh\"\n`include \"f{i + 1}.svh\"\n");
            files.Add("f25.svh", "wire leaf;\n");
            files.Add("top.sv", "module top;\n`include \"f0.svh\"\nendmodule\n");

            var clock = System.Diagnostics.Stopwatch.StartNew();
            SyntaxTree tree = SyntaxReader.Read("top.sv", files);

            Assert.Less(clock.Elapsed.TotalSeconds, 10, "stopped, not thirty million files spliced");
            Diagnostic stopped = tree.Diagnostics.Single(d => d.Code == DiagnosticCodes.MacroTooMuch);
            StringAssert.EndsWith(".svh", stopped.Location.File, "at an include");
        }

        /// <summary>
        /// The 16d sweep: an included file was read and lexed whole before it was charged, and nothing asked whether the
        /// budget was already spent -- so once it was, every later include of a large file was still lexed in full, only for
        /// its tokens to be refused.
        /// </summary>
        [Test]
        public void IncludesAfterTheBudgetIsSpent_AreNotReadAtAll()
        {
            var files = new MemorySourceFiles();
            files.Add("big.svh", string.Concat(Enumerable.Repeat("a ", 50_000)));
            string top = string.Concat(Enumerable.Repeat("`include \"big.svh\"\n", 200));
            Preprocessor.Run("top.sv", "`include \"big.svh\"\n", files, new DiagnosticBag(), out _);

            var diagnostics = new DiagnosticBag();
            long before = GC.GetAllocatedBytesForCurrentThread();
            Preprocessor.Run("top.sv", top, files, diagnostics, out Preprocessor done);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.AreEqual(1, diagnostics.Items.Count(d => d.Code == DiagnosticCodes.MacroTooMuch), "stopped, said once");
            Assert.LessOrEqual(done.FilesRead.Count, 1 + Preprocessor.MaxExpansion / 50_000 + 1,
                "the top, the includes the million paid for, and the one that spent it -- not two hundred");
            Assert.Less(allocated, 400_000_000, $"{allocated:N0} bytes allocated");
        }

        /// <summary>One import is one budget: a bomb split across files, each under it alone, is stopped as the whole would be.</summary>
        [Test]
        public void ABombSplitAcrossTheFilesOfOneImport_IsStoppedAsOne()
        {
            var files = new MemorySourceFiles();
            var paths = new System.Collections.Generic.List<string>();
            for (int f = 0; f < 4; f++)
            {
                string path = $"part{f}.sv";
                // Seventeen doublings, 655,359 each with every body token paid for as it is used (the 16d sweep; eighteen
                // were 786,431 when a use paid one and only what it put out).
                files.Add(path, Doubling(17, "`define M0 x\n", "`define M{i} `M{j} `M{j}\n", "module m" + f + "; wire y = `M{n}; endmodule\n"));
                paths.Add(path);
            }

            SyntaxTree alone = SyntaxReader.Read("part0.sv", files);
            Assert.IsFalse(alone.Diagnostics.Any(d => d.Code == DiagnosticCodes.MacroTooMuch), "sanity: one part alone is within it");

            SyntaxTree all = SyntaxReader.ReadAll(paths, files);
            Assert.AreEqual(1, all.Diagnostics.Count(d => d.Code == DiagnosticCodes.MacroTooMuch), "the four together are not, said once");
        }

        /// <summary>The whole of it: a chain of 31, two thousand million tokens asked for, stopped in moments, said once, at the use.</summary>
        [Test]
        public void AChainOfThirtyOneDoublingMacros_IsStoppedInMoments()
        {
            var text = new System.Text.StringBuilder("`define M0 x\n");
            for (int i = 1; i <= 31; i++)
                text.Append("`define M").Append(i).Append(" `M").Append(i - 1).Append(" `M").Append(i - 1).Append('\n');
            text.Append("module m; wire y = `M31; endmodule\n");

            var clock = System.Diagnostics.Stopwatch.StartNew();
            SyntaxTree tree = Read(text.ToString());

            Assert.Less(clock.Elapsed.TotalSeconds, 5, "stopped at a million, not after making them all");
            Diagnostic stopped = tree.Diagnostics.Single(d => d.Code == DiagnosticCodes.MacroTooMuch);
            Assert.AreEqual(33, stopped.Location.Line, "at the use in the file, not inside a macro");
        }
    }
}
