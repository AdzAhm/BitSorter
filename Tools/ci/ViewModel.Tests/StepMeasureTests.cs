using System;
using System.Linq;
using BitSorter.Board;
using BitSorter.Board.Tests;
using BitSorter.SystemVerilog;
using NUnit.Framework;

namespace BitSorter.ViewModel.Tests
{
    /// <summary>
    /// The measured worst (Part 14c): every input stepped both ways from every value of the rest, and the slowest each output
    /// changed -- the full adder's four, and a false path the gates make but no step ever fires. Each worked out by hand.
    /// </summary>
    public class StepMeasureTests
    {
        private static double _now;

        private static double Clock() => _now += 0.01;

        private static (TimingView Timing, MeasuredTiming Measured) Measure(Project project)
        {
            TimingView timing = TimingView.Of(project);
            StepMeasure measure = StepMeasure.Start(project, timing, Clock, out string why);
            Assert.IsNotNull(measure, why);
            measure.Finish();
            Assert.IsTrue(measure.Done);
            return (timing, measure.Result);
        }

        private static string Unit(Project project) => project.Settings?.TimeUnit ?? TimeUnits.Default;

        /// <summary>
        /// The workbench's full adder, XOR 2, AND 1, OR 1: a rising with b at 0 and cin at 1 makes h1's sum rise at 2, h2's
        /// carry at 3 and cout at 4 -- the computed 4, seen. cin reaches cout through h2's AND and the OR, 2.
        /// </summary>
        [Test]
        public void TheFullAdder_IsSeenAtItsComputedWorst()
        {
            Project project = WorkbenchSamples.FullAdder();
            (TimingView timing, MeasuredTiming measured) = Measure(project);

            Assert.AreEqual(4, measured.For("a", "cout").Worst);
            Assert.AreEqual(4, measured.For("a", "s").Worst);
            Assert.AreEqual(2, measured.For("cin", "cout").Worst);
            Assert.AreEqual(2, measured.For("cin", "s").Worst);
            Assert.AreEqual(3 * 4 * 2, measured.Steps);

            TimingView both = timing.With(measured);
            Assert.IsFalse(both.Lines.Any(l => l.NeverFires), "every computed path of a full adder fires");
            TimingLine worst = both.Lines.Single(l => l.Label == "MEASURED WORST");
            Assert.AreEqual("4 " + Unit(project), worst.Numbers);
            Assert.AreEqual(both.Lines.ToList().IndexOf(worst) - 1, both.Critical, "beside the critical path");
            Assert.AreEqual(timing.Lines[timing.Critical].Text, both.Lines[both.Critical].Text, "the critical path where it was");
        }

        /// <summary>
        /// y = s ? (s ? a : b) : c, of gates (AND and OR 2, the NOT 1). b's path to y is computed at 8 and never fires: no
        /// step of b ever moves y. a's does, with s at 1: 8. s's slowest is a glitch -- s rising from a = 0, b = 1, c = 0 lets
        /// the outer AND through at 2, so y rises at 4, and falls at 9 once the NOT, the inner AND and OR have closed it --
        /// the computed 9, seen, because a change that comes and goes is a change.
        /// </summary>
        [Test]
        public void AFalsePath_IsMarkedAsOneThatNeverFires()
        {
            Project project = SampleBoards.Build("false_path_steps");
            (TimingView timing, MeasuredTiming measured) = Measure(project);

            Assert.IsNull(measured.For("b", "y"), "no step of b ever moved y");
            Assert.AreEqual(8, measured.For("a", "y").Worst);
            Assert.AreEqual(4, measured.For("c", "y").Worst);
            MeasuredDelay s = measured.For("s", "y");
            Assert.AreEqual(9, s.Worst);
            Assert.IsTrue(s.WorstRising);
            Assert.AreEqual("s = 0, a = 0, b = 1, c = 0", s.When);

            TimingView both = timing.With(measured);
            TimingLine fromB = both.Lines.Single(l => l.Kind == TimingLineKind.Path && l.Label.Length == 0 && l.Ends.Any(e => e.From == "b"));
            Assert.IsTrue(fromB.NeverFires);
            StringAssert.EndsWith(" · never fires", fromB.Numbers);
            Assert.IsFalse(both.Lines.Single(l => l.Label.Length == 0 && l.Ends.Any(e => e.From == "a")).NeverFires, "a's does");
            Assert.AreEqual(both.Find(timing.Lines[timing.Critical]), both.Critical, "a line picked is found again");
        }

        /// <summary>
        /// STEP TEST on the full adder, its slowest path 4: each step left 6 to settle, a, b and cin up and down in turn from
        /// all 0, the last fall settled by 42 -- one step for undo, and the run played again with it.
        /// </summary>
        [Test]
        public void STEPTEST_MakesTheStimulus_InOneStep_AndTheRunIsPlayedAgainWithIt()
        {
            var session = new WorkbenchSession(WorkbenchSamples.FullAdder(), Clock);
            session.Finish();
            Stimulus before = session.Project.Stimulus;

            EditResult made = session.StepTest();
            Assert.IsTrue(made.Done, made.Refusal);
            Assert.AreEqual(42, session.Project.Stimulus.End);
            CollectionAssert.AreEqual(new[] { 0L, 6L, 12L }, session.Project.Stimulus.Tracks.Single(t => t.Port == "a").Steps.Select(s => s.Time));
            Assert.IsTrue(session.Replaying, "played again");

            Assert.IsTrue(session.Undo());
            Assert.AreEqual(before.End, session.Project.Stimulus.End, "one step back is the stimulus there was");
        }

        [Test]
        public void STEPTEST_IsRefused_ForACircuitWithNoInput()
        {
            var session = new WorkbenchSession(WorkbenchSamples.Blank(), Clock);
            StringAssert.Contains("no input to step", session.StepTest().Refusal);
        }

        [Test]
        public void ACircuitWithRegisters_IsNotMeasured_AndSaysWhy()
        {
            Project project = WorkbenchSamples.Counter();
            Assert.IsNull(StepMeasure.Start(project, TimingView.Of(project), Clock, out string why));
            StringAssert.Contains("registers", why);
        }

        [Test]
        public void TheMeasuringRunIsItsOwn_TheDocumentsStimulusUntouched()
        {
            Project project = WorkbenchSamples.FullAdder();
            Stimulus before = project.Stimulus;
            StepMeasure measure = StepMeasure.Start(project, TimingView.Of(project), Clock, out _);
            measure.Finish();
            Assert.AreSame(before, measure.Project.Stimulus, "measured on a copy");
            Assert.AreEqual(1.0, measure.Progress);
        }
    }
}
