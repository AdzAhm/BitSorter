using System.IO;
using System.Linq;
using BitSorter.Board;
using BitSorter.Board.Tests;
using BitSorter.Sim;
using NUnit.Framework;

namespace BitSorter.ViewModel.Tests
{
    /// <summary>
    /// The start page's three RV32I processors (Part 15h-6): their text the corpus's to the character, which the oracle
    /// holds to Icarus on every signal; each running the demo program to PASS in the cycles its line on the start page
    /// says; the multicycle one without the oracle's own testbench; and each opening as the drawing of its code.
    /// </summary>
    public class ProcessorSampleTests
    {
        private static string Corpus(string file) =>
            File.ReadAllText(Path.Combine(ExerciseFiles.Root, "Tools", "ci", "Oracle", "corpus", "processor", file)).Replace("\r\n", "\n");

        [Test]
        public void EveryFile_IsTheCorpusFile_ToTheCharacter()
        {
            Assert.AreEqual(7, WorkbenchProcessors.Files.Count);
            foreach ((string file, string text) in WorkbenchProcessors.Files)
                Assert.AreEqual(Corpus(file), text.Replace("\r\n", "\n"),
                    $"{file} differs from the corpus's: copy the corpus file into WorkbenchProcessors.cs");
        }

        [TestCase("Single-cycle RISC-V", "scpu_tb", 323)]
        [TestCase("Multicycle RISC-V", "mcpu_demo_tb", 1244)]
        [TestCase("Pipelined RISC-V", "pcpu_tb", 386)]
        public void AProcessor_RunsTheDemo_ToPass_InTheCyclesItsLineSays(string name, string bench, int cycles)
        {
            (string Name, string Says, System.Func<Project> Build) sample = WorkbenchSamples.All.Single(s => s.Name == name);
            StringAssert.Contains(cycles.ToString("N0", System.Globalization.CultureInfo.InvariantCulture), sample.Says,
                "the line under its name says how many cycles it takes");
            Project project = sample.Build();
            Assert.AreEqual(bench, project.Bench);
            Assert.IsNull(project.Find("mcpu_tb"), "the oracle's own testbench is left out");

            CompiledProject compiled = BoardCompiler.Compile(project);
            Assert.IsTrue(compiled.Succeeded, string.Join("\n", compiled.Diagnostics));
            Elaboration elaboration = Elaborator.Elaborate(compiled.Tree, compiled.Top);
            Assert.IsFalse(elaboration.HasErrors, string.Join("\n", elaboration.Diagnostics));
            var sim = new Simulator(elaboration.Design, null, new SimOptions { Files = ProjectFiles.ForRun(project) });
            RunResult result = sim.RunTo(1_000_000);
            string said = string.Join("\n", sim.History.Messages.Select(m => m.Text));
            Assert.AreEqual(RunStatus.Finished, result.Status, said);
            StringAssert.Contains($"done after {cycles} cycles", said);
            StringAssert.Contains("the sum of 1 to 10: 55", said);
            StringAssert.Contains("ten Fibonacci numbers: 0 1 1 2 3 5 8 13 21 34", said);
            StringAssert.Contains("every instruction once: 31 of 31 checks held", said);
            StringAssert.Contains("PASS: every result as expected", said);
            Assert.AreEqual(result.Time, project.Stimulus.End, "the run is shown to where the testbench stops");
        }
    }
}
