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
            Assert.IsNull(measure.Problem, measure.Problem);
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

        /// <summary>
        /// y = (a | b) &amp; b, OR and AND 1 each: a's way through both and b's are the same gates, one line, 2 -- but only b's
        /// fires, since a passes the OR only with b at 0 and the AND only with b at 1. The line is two, the part that fires
        /// first, the measured worst under it, then a's, which never does (the 14c review).
        /// </summary>
        [Test]
        public void ACriticalPathPartlyFalse_HasTheMeasuredWorstUnderThePartThatFires()
        {
            var editor = new BoardEditor(Project.New("half_false"));
            var b = new SampleBoards.Builder(editor, "half_false");
            int a = b.In("a"), bb = b.In("b"), y = b.Out("y");
            int or = b.Gate(Sim.GateKind.Or, 1, 1), and = b.Gate(Sim.GateKind.And, 1, 1);
            b.Wire(a, "out", or, "a");
            b.Wire(bb, "out", or, "b");
            b.Wire(or, "y", and, "a");
            b.Wire(bb, "out", and, "b");
            b.Wire(and, "y", y, "in");
            (TimingView timing, MeasuredTiming measured) = Measure(editor.Project);

            TimingLine computed = timing.Lines[timing.Critical];
            Assert.AreEqual(2, computed.Ends.Count, "sanity: a's and b's are one line");
            Assert.IsNull(measured.For("a", "y"));
            Assert.AreEqual(2, measured.For("b", "y").Worst);

            TimingView both = timing.With(measured);
            TimingLine critical = both.Lines[both.Critical];
            Assert.IsFalse(critical.NeverFires);
            CollectionAssert.AreEqual(new[] { ("b", "y") }, critical.Ends, "the part that fires is the critical path");
            Assert.AreEqual("MEASURED WORST", both.Lines[both.Critical + 1].Label, "the measured worst right under it");
            TimingLine never = both.Lines[both.Critical + 2];
            Assert.IsTrue(never.NeverFires);
            CollectionAssert.AreEqual(new[] { ("a", "y") }, never.Ends, "and then the part that never fires");
        }

        /// <summary>y = a &amp; 0: a path from a to y, but no step of a ever moves y -- the run saw nothing, so no path is said never to fire.</summary>
        [Test]
        public void NoStepMovingAnything_MarksNoPath()
        {
            var editor = new BoardEditor(Project.New("stuck"));
            var b = new SampleBoards.Builder(editor, "stuck");
            int a = b.In("a"), y = b.Out("y");
            int zero = b.Add(new ConstantPart()), and = b.Gate(Sim.GateKind.And, 1, 1);
            b.Wire(a, "out", and, "a");
            b.Wire(zero, "y", and, "b");
            b.Wire(and, "y", y, "in");
            (TimingView timing, MeasuredTiming measured) = Measure(editor.Project);

            Assert.IsEmpty(measured.Delays);
            TimingView both = timing.With(measured);
            Assert.IsFalse(both.Lines.Any(l => l.NeverFires), "nothing marked");
            Assert.AreEqual("No step of any input ever moved an output.", both.Lines.Single(l => l.Label == "MEASURED").Text);
        }

        /// <summary>Two NORs crossed, a latch: a loop of logic need never settle, so no step is measured, and it says why.</summary>
        [Test]
        public void ALoopOfLogic_IsNotMeasured_AndSaysWhy()
        {
            var editor = new BoardEditor(Project.New("latch"));
            var b = new SampleBoards.Builder(editor, "latch");
            int s = b.In("s"), r = b.In("r"), q = b.Out("q");
            int top = b.Gate(Sim.GateKind.Nor, 1, 1), bottom = b.Gate(Sim.GateKind.Nor, 1, 1);
            b.Wire(r, "out", top, "a");
            b.Wire(bottom, "y", top, "b");
            b.Wire(s, "out", bottom, "a");
            b.Wire(top, "y", bottom, "b");
            b.Wire(top, "y", q, "in");

            TimingView timing = TimingView.Of(editor.Project);
            Assert.IsNotNull(timing.Analysis, "sanity: timed");
            Assert.IsNull(StepMeasure.Start(editor.Project, timing, Clock, out string why));
            StringAssert.Contains("loop", why);
        }

        /// <summary>A run that grows past what it may keep stops short: no measure is given, and it says why.</summary>
        [Test]
        public void ARunThatStopsShort_IsNoMeasure_AndSaysWhy()
        {
            Project project = WorkbenchSamples.FullAdder();
            StepMeasure measure = StepMeasure.Start(project, TimingView.Of(project), Clock, out _, maxRecorded: 20);
            measure.Finish();

            Assert.IsTrue(measure.Done);
            Assert.IsNull(measure.Result);
            StringAssert.StartsWith("Not measured: the measuring run stopped at ", measure.Problem);
            Assert.AreEqual(1.0, measure.Progress, "and the run let go");
        }

        /// <summary>A move, or STEP TEST, leaves the design as it was: its timing is the same, so what was measured of it stands.</summary>
        [Test]
        public void AMoveOrSTEPTEST_KeepsTheAnalysis()
        {
            var session = new WorkbenchSession(WorkbenchSamples.FullAdder(), Clock);
            session.Finish();
            TimingAnalysis analysis = session.Timing.Analysis;

            Assert.IsTrue(session.StepTest().Done);
            Assert.AreSame(analysis, session.Timing.Analysis, "STEP TEST");
            GatePart or = session.Project.TopModule.Parts.OfType<GatePart>().First();
            Placement was = session.Project.TopModule.Layout.PlacementOf(or.Id);
            Assert.IsTrue(session.Move(new System.Collections.Generic.Dictionary<int, Placement>
            {
                [or.Id] = new Placement(new BitSorter.Board.Point(was.Position.X, was.Position.Y - 1)),
            }).Done);
            Assert.AreSame(analysis, session.Timing.Analysis, "a move");
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
