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
    }
}
