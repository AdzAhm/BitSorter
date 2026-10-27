using System.Collections.Generic;
using System.Linq;
using BitSorter.Sim;
using BitSorter.SystemVerilog;
using BitSorter.SystemVerilog.Syntax;
using NUnit.Framework;

namespace BitSorter.Board.Tests
{
    /// <summary>
    /// Modules written as code (Part 12d): a block of one takes its pins from its header, widths worked out as the language
    /// works them out; its text is compiled as written and runs; each fault in it is refused at its line; and the save keeps
    /// it. Icarus runs the written sample as printed (<c>BoardOracleTests</c>).
    /// </summary>
    public class WrittenModuleTests
    {
        private static BoardEditor WithWritten(string name, string source)
        {
            var editor = new BoardEditor(Project.New("top"));
            Assert.IsTrue(editor.AddWrittenModule(name, source).Done);
            return editor;
        }

        private static string Module(string name, string header, string body) =>
            $"module {name} (\n{header}\n);\n{body}\nendmodule\n";

        [Test]
        public void AWrittenBlocksPins_AreItsHeaders_WidthsAsTheLanguageWorksThemOut()
        {
            Project project = SampleBoards.Build("written");
            BlockPart add = project.TopModule.Parts.OfType<BlockPart>().Single();
            Dictionary<string, Pin> pins = add.Pins(project).ToDictionary(p => p.Name);
            CollectionAssert.AreEqual(new[] { "a", "b", "s", "cout" }, add.Pins(project).Select(p => p.Name), "in the header's order");
            Assert.AreEqual(4, pins["a"].Width, "[N-1:0] with N's default of 4");
            Assert.AreEqual(1, pins["cout"].Width);
            Assert.AreEqual(PinDirection.Out, pins["s"].Direction);

            Module add4 = project.Find("add4");
            Assert.IsTrue(add4.IsWritten);
            Assert.IsEmpty(add4.Parts);
        }

        /// <summary>
        /// A written block's lint (Part 14e) is said in the transcript at its own lines, as warnings: what it reads that
        /// nothing drives, and -- since it is only ever an instance -- what it sets and never reads.
        /// </summary>
        [Test]
        public void AWrittenBlocksLint_IsSaidAtItsLines_AsWarnings()
        {
            BoardEditor editor = WithWritten("odd", Module("odd", "    input logic a,\n    output logic y",
                "    wire w;\n    logic spare;\n    assign spare = a;\n    assign y = a & w;"));
            BoardDiagnostic[] said = BoardRules.Check(editor.Project).Where(d => d.Module == "odd").ToArray();

            Assert.IsFalse(said.Any(d => d.IsError), string.Join("; ", said.Select(d => d.Message)));
            BoardDiagnostic undriven = said.Single(d => d.Message.StartsWith("w is read"));
            Assert.AreEqual(5, undriven.Line, "w's declaration");
            Assert.AreEqual(6, said.Single(d => d.Message == "spare is set, but nothing reads it.").Line);
        }

        [Test]
        public void AWrittenBlock_IsCompiledAsWritten_AndAdds()
        {
            Project project = SampleBoards.Build("written");
            CompiledProject compiled = BoardCompiler.Compile(project);
            Assert.IsTrue(compiled.Succeeded, string.Join("; ", compiled.Diagnostics));
            StringAssert.Contains("assign #3 {cout, s} = a + b;", BoardPrinter.Print(compiled).Single(p => p.Path == "add4.sv").Text,
                "printed as its own text");

            Elaboration elaboration = Elaborator.Elaborate(compiled.Tree, compiled.Top);
            Assert.IsFalse(elaboration.HasErrors, string.Join("; ", elaboration.Diagnostics));
            var sim = new Simulator(elaboration.Design);
            sim.RunTo(1_000_000);

            ulong At(string port, long time)
            {
                Assert.IsTrue(sim.Design.TryFind(compiled.DesignPath + "." + port, out SignalId signal), port);
                Assert.IsTrue(sim.History.ValueAt(signal, time).TryGetUInt64(out ulong value), $"{port} known at {time}");
                return value;
            }

            int vectors = 0;
            foreach (Step step in project.Stimulus.Tracks.Single(t => t.Port == "a").Steps)
            {
                long settled = step.Time + 9;
                Assert.AreEqual(At("a", settled) + At("b", settled), At("s", settled) | (At("cout", settled) << 4), $"at {settled}");
                vectors++;
            }

            Assert.AreEqual(12, vectors);
        }

        [Test]
        public void AFaultInTheText_IsAnErrorAtItsLine_AndAnEditThatMakesOne_IsRefused()
        {
            string good = Module("pass", "    input logic a,\n    output logic y", "    assign y = a;");
            BoardEditor editor = WithWritten("pass", good);

            string typo = Module("pass", "    input logic a,\n    output logic y", "    assign y = a +;");
            EditResult refused = editor.SetSource("pass", typo);
            Assert.IsFalse(refused.Done, "a text that does not read is refused");
            Assert.AreEqual(good, editor.Project.Find("pass").Source, "and the module keeps what it had");

            Project broken = editor.Project.WithModule(editor.Project.Find("pass") with { Source = typo });
            BoardDiagnostic error = BoardRules.Check(broken).First(d => d.IsError);
            Assert.AreEqual("pass", error.Module);
            Assert.AreEqual(5, error.Line, "the line of the assign: a header of four lines, then the body");
            Assert.Greater(error.Column, 0);

            string unknown = Module("pass", "    input logic a,\n    output logic y", "    assign y = b;");
            Assert.IsFalse(editor.SetSource("pass", unknown).Done, "a name nothing declares is found by elaborating it");
            Assert.AreEqual(5, BoardRules.Check(editor.Project.WithModule(editor.Project.Find("pass") with { Source = unknown }))
                .First(d => d.IsError).Line);

            string longer = Module("pass", "    input logic a,\n    output logic y", "    assign y = ~a;");
            Assert.IsTrue(editor.SetSource("pass", longer).Done);
            Assert.IsTrue(editor.Undo());
            Assert.AreEqual(good, editor.Project.Find("pass").Source, "one step, undone as any edit is");
        }

        [TestCase("module other (input logic a, output logic y);\n  assign y = a;\nendmodule\n", "module pass")]
        [TestCase("module pass (input logic a, output logic y);\n  assign y = a;\nendmodule\nmodule more (input logic a);\nendmodule\n", "one module")]
        [TestCase("`timescale 1ns/1ps\nmodule pass (input logic a, output logic y);\n  assign y = a;\nendmodule\n", "UNIT")]
        [TestCase("module pass (inout wire a, output logic y);\n  assign y = a;\nendmodule\n", "inout")]
        [TestCase("// nothing yet\n", "no module")]
        [TestCase("module pass (input logic a, output logic y);\n  timeunit 1ns;\n  assign y = a;\nendmodule\n", "UNIT")]
        public void WhatABlocksTextMustBe_IsSaidInItsOwnWords(string source, string said)
        {
            BoardEditor editor = WithWritten("pass", null);
            EditResult refused = editor.SetSource("pass", source);
            Assert.IsFalse(refused.Done);
            StringAssert.Contains(said, refused.Refusal);
        }

        [Test]
        public void AWrittenModule_UsesCellsAndOtherModules_AndCannotHoldItself()
        {
            var editor = new BoardEditor(SampleBoards.Build("half_adder") with { Stimulus = null });
            string nand = Module("nanded", "    input logic a, b,\n    output logic y, s, c",
                "    NAND2 #(.Tpdlh(2), .Tpdhl(3)) g1 (.A(a), .B(b), .Z(y));\n    half_adder h (.a(a), .b(b), .s(s), .c(c));");
            Assert.IsTrue(editor.AddWrittenModule("nanded", nand).Done, string.Join("; ", editor.Diagnostics));

            WrittenModule read = WrittenModules.Read(editor.Project, editor.Project.Find("nanded"));
            CollectionAssert.AreEqual(new[] { "NAND2", "half_adder" }, read.Uses);
            Assert.AreEqual(5, read.Ports.Count);

            // The half adder holding the module that holds it.
            EditResult loop = editor.Add("half_adder", new BlockPart { Module = "nanded" }, new Placement(9, 9));
            Assert.IsFalse(loop.Done);
            StringAssert.Contains("cannot contain itself", loop.Refusal);
        }

        [Test]
        public void WhatIsLegalOnlyAtTheTop_IsRefused_ForAWrittenBlockIsAlwaysAnInstance()
        {
            BoardEditor editor = WithWritten("pass", null);
            string writesItsInput = "module pass (input logic a, output logic y);\n  initial a = 1'b0;\n  assign y = a;\nendmodule\n";
            Assert.IsFalse(editor.SetSource("pass", writesItsInput).Done,
                "at the top an input is a variable a process may write; in an instance, which a block always is, it is a wire");
            BoardDiagnostic error = BoardRules.Check(editor.Project.WithModule(editor.Project.Find("pass") with { Source = writesItsInput }))
                .First(d => d.IsError);
            Assert.AreEqual(2, error.Line, "at the write");
        }

        /// <summary>Two written modules, one using the other, which uses a cell; and a drawn top around them.</summary>
        private static BoardEditor Nested()
        {
            var editor = new BoardEditor(Project.New("top"));
            Assert.IsTrue(editor.AddWrittenModule("nander", Module("nander", "    input logic a, b,\n    output logic y",
                "    NAND2 #(.Tpdlh(2), .Tpdhl(2)) g (.A(a), .B(b), .Z(y));")).Done, string.Join("; ", editor.Diagnostics));
            Assert.IsTrue(editor.AddWrittenModule("twice", Module("twice", "    input logic a, b,\n    output logic y",
                "    logic n;  // nander's output, and its own name: only the module's name changes when it does\n" +
                "    nander first (.a(a), .b(b), .y(n));\n    nander second (.a(n), .b(n), .y(y));")).Done,
                string.Join("; ", editor.Diagnostics));

            var b = new SampleBoards.Builder(editor, "top");
            int a = b.In("a"), bb = b.In("b"), y = b.Out("y");
            int twice = b.Add(new BlockPart { Module = "twice" });
            b.Wire(a, "out", twice, "a");
            b.Wire(bb, "out", twice, "b");
            b.Wire(twice, "y", y, "in");
            return editor;
        }

        [Test]
        public void AWrittenModule_ThatUsesAnotherWhichUsesACell_IsRead_AndTheDesignRuns()
        {
            BoardEditor editor = Nested();
            CollectionAssert.AreEqual(new[] { "nander" }, WrittenModules.Read(editor.Project, editor.Project.Find("twice")).Uses);
            Project project = SampleBoards.Exhaustive(editor, new[] { "a", "b" }, 10);

            CompiledProject compiled = BoardCompiler.Compile(project);
            Assert.IsTrue(compiled.Succeeded, string.Join("; ", compiled.Diagnostics));
            CollectionAssert.Contains(compiled.Cells, "NAND2", "the cell a module it uses uses");
            Elaboration elaboration = Elaborator.Elaborate(compiled.Tree, compiled.Top);
            Assert.IsFalse(elaboration.HasErrors, string.Join("; ", elaboration.Diagnostics));

            var sim = new Simulator(elaboration.Design);
            sim.RunTo(1_000_000);
            ulong At(string port, long time)
            {
                Assert.IsTrue(sim.Design.TryFind(compiled.DesignPath + "." + port, out SignalId signal), port);
                Assert.IsTrue(sim.History.ValueAt(signal, time).TryGetUInt64(out ulong value), $"{port} known at {time}");
                return value;
            }

            foreach (Step step in project.Stimulus.Tracks.Single(t => t.Port == "a").Steps)
                Assert.AreEqual(At("a", step.Time + 9) & At("b", step.Time + 9), At("y", step.Time + 9), "a NAND of a NAND of itself is an AND");
        }

        [Test]
        public void RenamingAModule_RenamesItInEveryTextThatSaysIt_AndNothingElse()
        {
            BoardEditor editor = Nested();
            Assert.IsTrue(editor.RenameModule("nander", "nand_gate").Done, string.Join("; ", editor.Diagnostics));
            StringAssert.StartsWith("module nand_gate (", editor.Project.Find("nand_gate").Source, "its own header");
            string twice = editor.Project.Find("twice").Source;
            StringAssert.Contains("nand_gate first (", twice, "and where another instantiates it");
            StringAssert.Contains("nand_gate second (", twice);
            StringAssert.Contains("its own name: only the module's name", twice, "a comment as written");

            Assert.IsTrue(editor.RenameModule("twice", "and2").Done, string.Join("; ", editor.Diagnostics));
            StringAssert.StartsWith("module and2 (", editor.Project.Find("and2").Source);
            Assert.AreEqual("and2", editor.Project.TopModule.Parts.OfType<BlockPart>().Single().Module, "the drawn block of it too");
            Assert.IsTrue(BoardCompiler.Compile(editor.Project).Succeeded);
        }

        [Test]
        public void ChangingTheHeader_UnderAWire_IsRefused_AndAddingAPort_GivesTheBlockAPin()
        {
            Project project = SampleBoards.Build("written");
            var editor = new BoardEditor(project);
            string noCarry = SampleBoards.WrittenAdder.Replace(",\n    output logic cout", "").Replace("{cout, s}", "s");
            Assert.IsFalse(editor.SetSource("add4", noCarry).Done, "cout is wired");

            string withZero = SampleBoards.WrittenAdder.Replace("output logic cout\n", "output logic cout,\n    output logic zero\n")
                .Replace("endmodule", "    assign zero = s == 0;\nendmodule");
            Assert.IsTrue(editor.SetSource("add4", withZero).Done, string.Join("; ", editor.Diagnostics));
            BlockPart add = editor.Project.TopModule.Parts.OfType<BlockPart>().Single();
            CollectionAssert.Contains(add.Pins(editor.Project).Select(p => p.Name).ToList(), "zero", "a new pin, open");
            Assert.IsTrue(BoardCompiler.Compile(editor.Project).Succeeded);
        }

        [Test]
        public void TheTopMayBeWritten_AndNoModuleTakesACellsName()
        {
            BoardEditor editor = WithWritten("pass", null);
            Assert.IsTrue(editor.SetTop("pass").Done, "a written top, as an imported design may be (Part 13b)");
            Assert.IsTrue(BoardCompiler.Compile(editor.Project).Succeeded);
            Assert.IsTrue(editor.SetTop("top").Done);
            Assert.IsFalse(editor.AddWrittenModule("NAND2").Done);
            Assert.IsFalse(editor.AddModule("XOR3").Done);
            Assert.IsFalse(editor.SetSource("top", "module top; endmodule").Done, "a drawn module has no text");
        }

        [Test]
        public void TheTemplate_IsABlockOfOneInputAndOneOutput()
        {
            BoardEditor editor = WithWritten("fresh", null);
            Module fresh = editor.Project.Find("fresh");
            Assert.AreEqual(WrittenModules.Template("fresh"), fresh.Source);
            CollectionAssert.AreEqual(new[] { "a", "y" }, WrittenModules.PortsOf(editor.Project, fresh).Select(p => p.Name));
            Assert.IsEmpty(BoardRules.Check(editor.Project).Where(d => d.IsError));
        }
    }
}
