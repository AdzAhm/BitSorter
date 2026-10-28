using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using BitSorter.Board;
using BitSorter.Board.Tests;
using BitSorter.Sim;
using NUnit.Framework;

namespace BitSorter.Ci.Oracle
{
    /// <summary>
    /// Part 15b: a self-checking testbench, printed, says under Icarus exactly what it says in BitSorter -- each check that
    /// fails, with its moment, what was read and what was wanted, and how many failed. What an exercise exports checks itself
    /// in ModelSim as it does on the workbench.
    /// </summary>
    public class SelfCheckOracleTests
    {
        [TestCase("0110", "0001", TestName = "a right design")]
        [TestCase("0010", "0000", TestName = "two wrong expectations")]
        [TestCase("xxxx", "0x0x", TestName = "bits left free")]
        [TestCase("1001", "1110", TestName = "every check failing")]
        public void APrintedSelfCheck_SaysUnderIcarus_WhatItSaysHere(string sums, string carries)
        {
            Icarus icarus = Icarus.Require();
            Project project = SampleBoards.CheckedHalfAdder(sums, carries);
            CompiledProject compiled = BoardCompiler.Compile(project);
            Assert.IsTrue(compiled.Succeeded, string.Join("\n", compiled.Diagnostics));

            Elaboration elaboration = Elaborator.Elaborate(compiled.Tree, compiled.Top);
            Assert.IsFalse(elaboration.HasErrors, string.Join("\n", elaboration.Diagnostics));
            var sim = new Simulator(elaboration.Design);
            sim.RunTo(project.Stimulus.StopTime + 10);
            string[] ours = sim.History.Messages.Select(m => m.Text).Where(t => !t.StartsWith("$stop", StringComparison.Ordinal)).ToArray();

            string[] theirs = RunIcarus(icarus, BoardPrinter.Print(compiled));
            CollectionAssert.AreEqual(ours, theirs, "BitSorter printed\n  " + string.Join("\n  ", ours) + "\nIcarus printed\n  " + string.Join("\n  ", theirs));
            Assert.That(ours.Last(), Does.EndWith("checks failed"));
        }

        /// <summary>An output read as x where a value is wanted fails in both (the #85 review): a driven to x at the last vector.</summary>
        [Test]
        public void AnOutputReadAsX_FailsUnderIcarusAsItFailsHere()
        {
            Icarus icarus = Icarus.Require();
            Project project = SampleBoards.CheckedHalfAdder("0110", "0001");
            Track a = project.Stimulus.Tracks.Single(t => t.Port == "a");
            Track unknown = a with { Steps = a.Steps.Select(s => s.Time == 30 ? new Step(30, SystemVerilog.LogicVec.FromBinary("x")) : s).ToList() };
            project = project with { Stimulus = project.Stimulus with { Tracks = project.Stimulus.Tracks.Select(t => t.Port == "a" ? unknown : t).ToList() } };

            CompiledProject compiled = BoardCompiler.Compile(project);
            Elaboration elaboration = Elaborator.Elaborate(compiled.Tree, compiled.Top);
            var sim = new Simulator(elaboration.Design);
            sim.RunTo(project.Stimulus.StopTime + 10);
            string[] ours = sim.History.Messages.Select(m => m.Text).Where(t => !t.StartsWith("$stop", StringComparison.Ordinal)).ToArray();

            CollectionAssert.AreEqual(ours, RunIcarus(icarus, BoardPrinter.Print(compiled)));
            CollectionAssert.Contains(ours, "FAIL s at 39: x, wanted 0");
        }

        /// <summary>
        /// An exercise graded (Part 15b): the half adder's sum taken from an AND, so wrong at three vectors. What the grader
        /// says failed is what the exported testbench says under Icarus, line for line.
        /// </summary>
        [Test]
        public void AnExercisesGrade_IsWhatItsExportedTestbenchSaysUnderIcarus()
        {
            Icarus icarus = Icarus.Require();
            var problems = new List<string>();
            Exercise exercise = Exercise.From(new ExerciseRecord
            {
                id = "half-adder",
                title = "Half adder",
                inputs = new[] { new SignalRecord { name = "a", values = "0011" }, new SignalRecord { name = "b", values = "0101" } },
                outputs = new[] { new SignalRecord { name = "sum", values = "0110" }, new SignalRecord { name = "carry", values = "0001" } },
                parts = new[] { new StockRecord { kind = "And", count = 2 } },
            }, problems);
            Assert.IsNotNull(exercise, string.Join("\n", problems));
            Project project = exercise.ToProject(problems);
            Assert.IsNotNull(project, string.Join("\n", problems));

            var editor = new BoardEditor(project);
            string top = project.Top;
            int Port(string name) => editor.Project.TopModule.Ports.Single(p => p.Name == name).Id;
            foreach (string output in new[] { "sum", "carry" })
            {
                EditResult and = editor.Add(top, new GatePart { Kind = GateKind.And, Inputs = 2, Rise = 1, Fall = 1 }, new Placement(8, output == "sum" ? 0 : 4));
                Assert.IsTrue(and.Done, and.Refusal);
                Assert.IsTrue(editor.Connect(top, Endpoint.At(Port("a"), "out"), Endpoint.At(and.Id, "a")).Done);
                Assert.IsTrue(editor.Connect(top, Endpoint.At(Port("b"), "out"), Endpoint.At(and.Id, "b")).Done);
                Assert.IsTrue(editor.Connect(top, Endpoint.At(and.Id, "y"), Endpoint.At(Port(output), "in")).Done);
            }

            Grade grade = ExerciseGrader.Grade(exercise, editor.Project);
            Assert.AreEqual(3, grade.Failures.Count, "sanity: wrong at three vectors");

            string[] theirs = RunIcarus(icarus, BoardPrinter.Print(BoardCompiler.Compile(editor.Project)));
            CollectionAssert.AreEqual(grade.Failures.Select(f => "FAIL " + f).Append("3 of 8 checks failed"), theirs);
        }

        /// <summary>The lines the testbench printed under Icarus: its own, not vvp's note of where <c>$stop</c> stopped it.</summary>
        private static string[] RunIcarus(Icarus icarus, IReadOnlyList<PrintedSource> printed)
        {
            string folder = Path.Combine(Path.GetTempPath(), "bitsorter-selfcheck-oracle", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                var sources = new List<string>();
                foreach (PrintedSource source in printed)
                {
                    string path = Path.Combine(folder, source.Path);
                    File.WriteAllText(path, source.Text);
                    sources.Add(path);
                }

                using IcarusRun run = icarus.Simulate(sources, null, null, TimeSpan.FromMinutes(2));
                Assert.IsTrue(run.Succeeded, run.Explain());
                return Regex.Split(run.Simulate.StandardOutput, "\r?\n")
                    .Where(line => line.StartsWith("FAIL ", StringComparison.Ordinal) || line.EndsWith("checks failed", StringComparison.Ordinal))
                    .ToArray();
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }
}
