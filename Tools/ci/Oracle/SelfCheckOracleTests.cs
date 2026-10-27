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
