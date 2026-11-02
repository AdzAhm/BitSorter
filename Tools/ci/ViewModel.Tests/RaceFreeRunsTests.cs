using System.Collections.Generic;
using System.Linq;
using BitSorter.Board;
using BitSorter.Sim;
using NUnit.Framework;

namespace BitSorter.ViewModel.Tests
{
    /// <summary>
    /// None of BitSorter's own runs races (Part 15i-3): every sample on the start page, and every exercise's answer run by
    /// its own test as CHECK runs it, gives no race warning -- what the workbench shows a student first is never one.
    /// </summary>
    public class RaceFreeRunsTests
    {
        private static string[] RacesIn(Project project, long until)
        {
            CompiledProject compiled = BoardCompiler.Compile(project);
            Assert.IsTrue(compiled.Succeeded, string.Join("\n", compiled.Diagnostics));
            Elaboration elaboration = compiled.Elaborate();
            Assert.IsFalse(elaboration.HasErrors, string.Join("\n", elaboration.Diagnostics));
            var sim = new Simulator(elaboration.Design, null, new SimOptions { Files = ProjectFiles.ForRun(project) });
            RunResult result = sim.RunTo(until);
            Assert.That(result.Status, Is.EqualTo(RunStatus.Finished).Or.EqualTo(RunStatus.Suspended), "sanity: it ran to its end");
            return sim.History.Messages.Where(m => m.Severity == Severity.Warning && m.Text.Contains("blocking assignment at the edge"))
                .Select(m => m.Text).ToArray();
        }

        private static IEnumerable<string> Samples() => WorkbenchSamples.All.Select(s => s.Name);

        [TestCaseSource(nameof(Samples))]
        public void ASample_RunsWithNoRace(string name)
        {
            Project project = WorkbenchSamples.All.Single(s => s.Name == name).Build();
            long until = project.Stimulus != null ? project.Stimulus.StopTime + 1 : 1_000_000;
            CollectionAssert.IsEmpty(RacesIn(project, until));
        }

        private static IEnumerable<string> Exercises() => Board.Tests.ExerciseFiles.Ids();

        [TestCaseSource(nameof(Exercises))]
        public void AnExercisesAnswer_RunByItsTest_HasNoRace(string id)
        {
            Exercise exercise = Board.Tests.ExerciseFiles.Load(id);
            Project solution = Board.Tests.ExerciseFiles.Solution(exercise);
            Project graded = solution with
            {
                Stimulus = exercise.Test(),
                Settings = (solution.Settings ?? new ProjectSettings()) with { Library = exercise.Library() },
            };
            CollectionAssert.IsEmpty(RacesIn(graded, graded.Stimulus.StopTime + 1));
        }
    }
}
