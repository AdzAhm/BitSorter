using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using BitSorter.Sim;
using BitSorter.SystemVerilog;
using NUnit.Framework;

namespace BitSorter.Board.Tests
{
    /// <summary>
    /// A drawn project's timing (Part 14a): its registers' clock-to-Q and setup, saved, compiled into the run and handed to
    /// static timing by name in every instance, and the design analysed alone, never the testbench round it.
    /// </summary>
    public class TimingAnalysisTests
    {
        /// <summary>The Johnson counter, every register given a clock to Q of 2 and a setup of 3.</summary>
        private static Project Timed(long tcq, long tsetup)
        {
            Project project = SampleBoards.Build("johnson");
            Module top = project.TopModule;
            var parts = top.Parts.Select(p => p is RegisterPart r ? r with { Tcq = tcq, Tsetup = tsetup } : p).ToList();
            return project.WithModule(top with { Parts = parts });
        }

        /// <summary>r2's q through the NOT (1) to r1's d: the minimum period is 2 + 1 + 3.</summary>
        [Test]
        public void ADrawnCounter_ItsMinimumPeriod_IsClockToQ_TheNot_AndTheSetup()
        {
            TimingAnalysis analysis = TimingAnalysis.Of(Timed(2, 3));

            Assert.IsTrue(analysis.Succeeded, analysis.Problem);
            Assert.AreEqual(3, analysis.Graph.Registers.Count);
            Assert.AreEqual(6, analysis.Timing.MinimumPeriod);
            Assert.AreEqual(1, analysis.Timing.Critical.Logic);
            Assert.AreEqual(new RegisterTiming(2, 3), analysis.Timing.Critical.Launch.Timing);
            Assert.AreEqual(1, analysis.Timing.Critical.Arcs.Count(a => a.Gate == GateKind.Not));
            Assert.AreEqual("johnson", analysis.Graph.Design.Name, "the design, not its testbench");
        }

        [Test]
        public void AnIdealRegister_CostsNothing_AsBeforePartFourteen()
        {
            TimingAnalysis analysis = TimingAnalysis.Of(SampleBoards.Build("johnson"));

            Assert.AreEqual(1, analysis.Timing.MinimumPeriod, "the NOT alone");
            StringAssert.DoesNotContain("<= #", string.Concat(BoardPrinter.Print(SampleBoards.Build("johnson")).Select(p => p.Text)));
        }

        /// <summary>A register's clock to Q is a delay on its write, so the run shows q changing that long after the edge.</summary>
        [Test]
        public void AClockToQ_IsInTheRun_QChangingThatLongAfterTheEdge()
        {
            string printed = string.Concat(BoardPrinter.Print(Timed(2, 3)).Select(p => p.Text));
            StringAssert.Contains("<= #2", printed);

            long[] Changes(Project project)
            {
                CompiledProject compiled = BoardCompiler.Compile(project);
                Elaboration e = Elaborator.Elaborate(compiled.Tree, compiled.Top);
                var sim = new Simulator(e.Design, null, null);
                sim.RunTo(100);
                return sim.History.SettledChanges(e.Design.Find(compiled.DesignPath + ".q0")).Select(c => c.Time).ToArray();
            }

            long[] ideal = Changes(SampleBoards.Build("johnson")), timed = Changes(Timed(2, 0));
            Assert.AreEqual(ideal.Length, timed.Length);
            CollectionAssert.AreEqual(ideal.Skip(1).Select(t => t + 2), timed.Skip(1), "every change after the first edge, 2 later");
        }

        [Test]
        public void AClockToQAndASetup_AreSaved_AndReadBack()
        {
            Project project = Timed(2, 3);
            var fields = new JsonSerializerOptions { IncludeFields = true };
            var problems = new List<string>();
            Project again = JsonSerializer.Deserialize<ProjectRecord>(JsonSerializer.Serialize(ProjectRecord.From(project), fields), fields).ToProject(problems);

            Assert.IsEmpty(problems);
            CollectionAssert.AreEqual(project.TopModule.Parts.OfType<RegisterPart>().Select(r => (r.Tcq, r.Tsetup)),
                again.TopModule.Parts.OfType<RegisterPart>().Select(r => (r.Tcq, r.Tsetup)));
        }

        [Test]
        public void ANegativeClockToQ_IsRefused()
        {
            BoardDiagnostic error = BoardRules.Check(Timed(-1, 0)).First(d => d.IsError);

            StringAssert.Contains("clock to Q and setup cannot be less than 0", error.Message);
        }

        /// <summary>The full adder's paths, through its gates, worst first: the analysis a drawn design gets.</summary>
        [Test]
        public void ADrawnFullAdder_ItsPaths_AreFoundThroughItsGates()
        {
            TimingAnalysis analysis = TimingAnalysis.Of(SampleBoards.Build("full_adder"));

            Assert.IsTrue(analysis.Succeeded, analysis.Problem);
            Assert.Greater(analysis.Paths.Found, 0);
            Assert.That(analysis.Paths.Paths, Is.Ordered.Descending.By(nameof(TimingPath.Worst)));
            Assert.IsNull(analysis.Timing.Critical, "no registers");
            Assert.IsNotNull(analysis.Timing.InputToOutput);
            Assert.AreEqual(analysis.Paths.Worst.Worst, analysis.Timing.InputToOutput.Logic, "the two reckonings agree on the slowest path");
        }
    }
}
