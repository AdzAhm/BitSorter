using System;
using System.Collections.Generic;
using System.Linq;
using BitSorter.Sim;
using BitSorter.SystemVerilog;
using BitSorter.SystemVerilog.Syntax;
using NUnit.Framework;

namespace BitSorter.Board.Tests
{
    /// <summary>
    /// What a board compiles to: the SystemVerilog it prints, in the course's style, and what that does
    /// when it runs. And invariant 4: printed, read back and run, it gives the same history.
    /// </summary>
    public class BoardCompilerTests
    {
        private static string Printed(Project project, string file) =>
            BoardPrinter.Print(project).Single(p => p.Path == file).Text.Replace("\r", "");

        private static Simulator Run(CompiledProject compiled, long until = 1_000_000)
        {
            Elaboration elaboration = Elaborator.Elaborate(compiled.Tree, compiled.Top);
            Assert.IsFalse(elaboration.HasErrors, string.Join("\n", elaboration.Diagnostics));
            var sim = new Simulator(elaboration.Design);
            sim.RunTo(until);
            return sim;
        }

        private static Simulator RunPrinted(Project project, long until = 1_000_000)
        {
            (MemorySourceFiles files, List<string> paths) = BoardPrinter.AsSources(BoardPrinter.Print(project));
            SyntaxTree tree = SyntaxReader.ReadAll(paths, files);
            Assert.IsEmpty(tree.Diagnostics, string.Join("\n", tree.Diagnostics));
            Elaboration elaboration = Elaborator.Elaborate(tree, BoardCompiler.Compile(project).Top);
            Assert.IsFalse(elaboration.HasErrors, string.Join("\n", elaboration.Diagnostics));
            var sim = new Simulator(elaboration.Design);
            sim.RunTo(until);
            return sim;
        }

        private static string[] Settled(Simulator sim, string name) =>
            sim.History.SettledChanges(sim.Design.Find(name)).Select(c => $"{c.Time} {c.Value.Format(Radix.Binary)}").ToArray();

        [Test]
        public void AnIdealHalfAdder_PrintsAsPrimitives_InTheCoursesStyle()
        {
            string text = Printed(SampleBoards.Build("half_adder"), "half_adder.sv");
            TestContext.WriteLine(text);

            Assert.AreEqual(
                "// half_adder: drawn in BitSorter.\n\n" +
                "module half_adder (\n" +
                "    input logic a,\n" +
                "    input logic b,\n" +
                "    output logic s,\n" +
                "    output logic c\n" +
                ");\n" +
                "    xor g1 (s, a, b);\n" +
                "    and g2 (c, a, b);\n" +
                "endmodule\n", text);
        }

        [Test]
        public void GatesWithADelay_PrintAsCells_AndTheCellsGoInAFileOfTheirOwn()
        {
            Project project = SampleBoards.Build("half_adder_delayed");
            string text = Printed(project, "half_adder.sv");
            string cells = Printed(project, CellLibrary.FileName);
            TestContext.WriteLine(text);
            TestContext.WriteLine(cells);

            StringAssert.Contains("XOR2 #(.Tpdlh(2), .Tpdhl(3)) g1 (", text);
            StringAssert.Contains(".A(a)", text);
            StringAssert.Contains(".Z(s)", text);
            StringAssert.Contains("module XOR2", cells);
            StringAssert.Contains("module AND2", cells);
            StringAssert.DoesNotContain("module OR2", cells, "only the cells the board uses");
        }

        [Test]
        public void TheTestbench_DrivesTheInputs_StepByStep_AndStops()
        {
            string text = Printed(SampleBoards.Build("half_adder"), "half_adder_tb.sv");
            TestContext.WriteLine(text);

            StringAssert.Contains("module half_adder_tb;", text);
            StringAssert.Contains("half_adder dut (", text);
            StringAssert.Contains("#10 a = 1'b0;", text);
            StringAssert.Contains("#10 $stop;", text);
        }

        /// <summary>
        /// A step after the stimulus's end is played, and the testbench stops on it: the run is made to
        /// <see cref="Stimulus.StopTime"/>, which the workbench reads too, so what it shows and what
        /// the printed testbench does are the same run (Part 9b).
        /// </summary>
        [Test]
        public void AStepAfterTheEnd_IsPlayed_AndTheTestbenchStopsOnIt()
        {
            var stimulus = new Stimulus
            {
                Tracks = new[] { new Track { Port = "a", Steps = new[] { new Step(5, LogicVec.FromUInt64(1, 1)), new Step(40, LogicVec.FromUInt64(0, 1)) } } },
                End = 30,
            };

            Assert.AreEqual(40, stimulus.StopTime, "the last step is later than the end");
            Assert.AreEqual(30, (stimulus with { Tracks = new[] { stimulus.Tracks[0] with { Steps = new[] { stimulus.Tracks[0].Steps[0] } } } }).StopTime,
                "without one, it is the end");
        }

        [Test]
        public void TheDesignPath_IsTheDesignUnderItsTestbench_OrTheTopModuleWithoutOne()
        {
            Project project = SampleBoards.Build("half_adder_delayed");
            CompiledProject compiled = BoardCompiler.Compile(project);
            Assert.AreEqual("half_adder_tb.dut", compiled.DesignPath);

            Simulator sim = Run(compiled);
            Assert.IsTrue(sim.Design.TryFind(compiled.DesignPath + ".s", out _), "a net's signal is the path, a dot and the net's name");
            Assert.IsTrue(sim.Design.TryFind(compiled.DesignPath + ".g1." + CellLibrary.Output, out _),
                "and a gate printed as a cell has its own output under the gate's name");

            Assert.AreEqual("half_adder", BoardCompiler.Compile(project with { Stimulus = null }).DesignPath);
        }

        [Test]
        public void AHalfAdderWithDelays_RunsToItsTruthTable_EachChangeLateByItsDelay()
        {
            Simulator sim = Run(BoardCompiler.Compile(SampleBoards.Build("half_adder_delayed")));

            // a b at 0, 10, 20, 30: 00, 01, 10, 11. s = a ^ b rises 2 after, falls 3 after; c = a & b likewise.
            CollectionAssert.AreEqual(new[] { "0 x", "3 0", "12 1", "33 0" }, Settled(sim, "half_adder_tb.s"));
            CollectionAssert.AreEqual(new[] { "0 x", "3 0", "32 1" }, Settled(sim, "half_adder_tb.c"));
        }

        [Test]
        public void ARegisterOntoAPort_StartsWhereItWasTold_ThroughAVariableOfItsOwn()
        {
            Project project = SampleBoards.Build("johnson");
            string text = Printed(project, "johnson.sv");
            TestContext.WriteLine(text);

            StringAssert.Contains("logic r1_q = 1'b0;", text, "the register's own variable, starting at 0");
            StringAssert.Contains("assign q0 = r1_q;", text, "and the port fed from it");
            StringAssert.Contains("always_ff @(posedge clk, posedge rst)", text);

            Simulator sim = Run(BoardCompiler.Compile(project));
            CollectionAssert.AreEqual(new[] { "0 0", "5 1", "25 0", "45 1", "65 0", "85 1" }, Settled(sim, "johnson_tb.q0"));
        }

        [Test]
        public void ATriStateGate_PrintsAsItsPrimitive_AndIsZWhileItsControlSaysSo()
        {
            Project project = SampleBoards.Build("tristate");
            string text = Printed(project, "tristate.sv");
            TestContext.WriteLine(text);

            StringAssert.Contains("bufif1 #(1, 2) g1 (q, d, en);", text, "no cell: a primitive with its delay");
            StringAssert.Contains("notif0 g2 (nq, d, en);", text);

            Simulator sim = Run(BoardCompiler.Compile(project));
            string At(string signal, long time) => sim.History.ValueAt(sim.Design.Find("tristate_tb." + signal), time).Format(Radix.Binary);
            Assert.AreEqual("z", At("q", 3), "en is 0: q is let go");
            Assert.AreEqual("1", At("q", 7), "en is 1: q is d, a rise of 1 after");
            Assert.AreEqual("0", At("nq", 3), "notif0 drives while en is 0, inverted");
            Assert.AreEqual("z", At("nq", 7));
        }

        [Test]
        public void ABlockPinWithNothingOnIt_IsLeftOpen()
        {
            var b = new Bench("top");
            Bench.Must(b.Editor.AddModule("inner"));
            b.Module = "inner";
            b.Input("x");
            b.Output("y");
            b.Module = "top";
            b.Add(new BlockPart { Module = "inner" });

            string text = Printed(b.Project, "top.sv");
            StringAssert.Contains(".x()", text);
            StringAssert.Contains(".y()", text);
        }

        [Test]
        public void ALamp_PrintsNothing_AndALampOnItsOwnLeavesNoNetBehind()
        {
            var b = new Bench("top");
            int a = b.Input("a"), lit = b.Add(new LampPart());
            b.Wire(a, "out", lit, "in");
            b.Add(new LampPart());

            string text = Printed(b.Project, "top.sv");
            StringAssert.DoesNotContain("lamp", text);
        }

        [TestCaseSource(typeof(SampleBoards), nameof(SampleBoards.Names))]
        public void PrintedReadAndRun_GivesTheSameHistory_AsCompiledAndRun(string board)
        {
            Project project = SampleBoards.Build(board);
            Simulator compiled = Run(BoardCompiler.Compile(project));
            Simulator printed = RunPrinted(project);

            Assert.AreEqual(compiled.Design.Signals.Count, printed.Design.Signals.Count);
            int changes = 0;
            for (int s = 0; s < compiled.Design.Signals.Count; s++)
            {
                SignalId id = compiled.Design.SignalAt(s);
                string name = compiled.Design.Signals[s].Name;
                SignalId theirs = printed.Design.Find(name);

                string[] ours = Enumerable.Range(0, compiled.History.ChangeCount(id)).Select(i => compiled.History.Change(id, i).ToString()).ToArray();
                string[] again = Enumerable.Range(0, printed.History.ChangeCount(theirs)).Select(i => printed.History.Change(theirs, i).ToString()).ToArray();
                CollectionAssert.AreEqual(ours, again, name);
                changes += ours.Length;
            }

            TestContext.WriteLine($"{board}: {compiled.Design.Signals.Count} signals, {changes} changes, the same both ways");
        }

        [TestCaseSource(typeof(SampleBoards), nameof(SampleBoards.Names))]
        public void PrintingWhatWasPrinted_GivesItBack(string board)
        {
            foreach (PrintedSource source in BoardPrinter.Print(SampleBoards.Build(board)))
            {
                SyntaxTree read = SyntaxReader.ReadText(source.Text, source.Path);
                Assert.IsEmpty(read.Diagnostics, source.Path + "\n" + string.Join("\n", read.Diagnostics));

                string body = source.Text.Substring(source.Text.IndexOf("\nmodule ", StringComparison.Ordinal) + 1).Replace("\r", "");
                string again = string.Join("\n", read.Modules.Select(Printer.Print)).Replace("\r", "");
                Assert.AreEqual(body.Replace("\n\n", "\n"), again.Replace("\n\n", "\n"), source.Path);
            }
        }
    }
}
