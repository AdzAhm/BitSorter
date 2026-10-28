using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using BitSorter.Board;
using BitSorter.Board.Tests;
using NUnit.Framework;

namespace BitSorter.Ci.Oracle
{
    /// <summary>
    /// Part 15c: every exercise's reference solution, exported with the exercise's own self-checking testbench, passes it
    /// under Icarus as it does in BitSorter -- so what the course's ModelSim would say of an exported answer is what the
    /// workbench says.
    /// </summary>
    public class ExerciseOracleTests
    {
        private static IEnumerable<string> Ids() => ExerciseFiles.Ids();

        [TestCaseSource(nameof(Ids))]
        public void ASolutionsSelfCheckingTestbench_PassesUnderIcarus(string id)
        {
            Icarus icarus = Icarus.Require();
            Exercise exercise = ExerciseFiles.Load(id);
            Project solution = ExerciseFiles.Solution(exercise);

            // As the grader runs it: the exercise's test and library, whatever the solution's own file said.
            Project graded = solution with
            {
                Stimulus = exercise.Test(),
                Settings = (solution.Settings ?? new ProjectSettings()) with { Library = exercise.Library() },
            };
            CompiledProject compiled = BoardCompiler.Compile(graded);
            Assert.IsTrue(compiled.Succeeded, string.Join("\n", compiled.Diagnostics));

            string folder = Path.Combine(Path.GetTempPath(), "bitsorter-exercise-oracle", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                var sources = new List<string>();
                foreach (PrintedSource source in BoardPrinter.Print(compiled))
                {
                    string path = Path.Combine(folder, source.Path);
                    File.WriteAllText(path, source.Text);
                    sources.Add(path);
                }

                using IcarusRun run = icarus.Simulate(sources, null, null, TimeSpan.FromMinutes(2));
                Assert.IsTrue(run.Succeeded, run.Explain());
                string[] said = Regex.Split(run.Simulate.StandardOutput, "\r?\n")
                    .Where(line => line.StartsWith("FAIL ", StringComparison.Ordinal) || line.EndsWith("checks failed", StringComparison.Ordinal))
                    .ToArray();
                CollectionAssert.AreEqual(new[] { $"0 of {exercise.Test().Checks.Count} checks failed" }, said, $"{id} under Icarus:\n  " + string.Join("\n  ", said));
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }
}
