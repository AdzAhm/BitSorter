using System;
using System.Collections.Generic;
using System.Linq;
using BitSorter.Sim;
using BitSorter.SystemVerilog;
using BitSorter.SystemVerilog.Syntax;
using NUnit.Framework;

namespace BitSorter.ViewModel.Tests
{
    /// <summary>
    /// Playing a run (Part 9b-3): a run a <c>$stop</c> suspended, run on; the breakpoints and where they
    /// fire; and the playhead playing, pausing, stepping and running the run on.
    /// </summary>
    public class PlaybackTests
    {
        private static Design Elaborate(string source, string top)
        {
            SyntaxTree tree = SyntaxReader.ReadText(source, "test.sv", null);
            Assert.IsEmpty(tree.Diagnostics, string.Join("; ", tree.Diagnostics));
            Elaboration elaboration = Elaborator.Elaborate(tree, top);
            Assert.IsFalse(elaboration.HasErrors, string.Join("; ", elaboration.Diagnostics));
            return elaboration.Design;
        }

        private static Func<double> Ticking(double step)
        {
            double now = 0;
            return () => { double read = now; now += step; return read; };
        }

        private static string[] Read(SignalTrack track) =>
            Enumerable.Range(0, track.Count).Select(i => track[i].Time + " " + track[i].Value.Format(Radix.Binary)).ToArray();

        private static string[] Read(List<ValueChange> changes) =>
            changes.Select(c => c.Time + " " + c.Value.Format(Radix.Binary)).ToArray();

        /// <summary>The counter sample, made to its end: where its testbench's $stop suspends it.</summary>
        private static Playback Counter()
        {
            RunSession run = RunSession.Open(WorkbenchSamples.Counter(), Ticking(0.001));
            run.Finish();
            return new Playback(run);
        }

        private static readonly int[] CountRow = { 0 };

        // -----------------------------------------------------------------
        // A run suspended, run on
        // -----------------------------------------------------------------

        [Test]
        public void ATrack_GrownOverATimeStepCarriedOnAfterAStop_IsTheOneShotList()
        {
            Design design = Elaborate(@"
module t;
    logic [3:0] n = 0;
    logic b = 0;
    initial begin
        #5 n = 1; b = 1;
        $stop;
        n = 2; b = 0;
        #5 n = 3;
        $stop;
        n = 3;
        #5 $finish;
    end
endmodule", "t");

            var sim = new Simulator(design);
            SignalId n = design.Find("t.n"), b = design.Find("t.b");
            var nTrack = new SignalTrack(sim.History, n, WorkbenchModel.TextOf);
            var bTrack = new SignalTrack(sim.History, b, WorkbenchModel.TextOf);

            Assert.AreEqual(RunStatus.Suspended, sim.RunTo(100).Status);
            nTrack.Grow();
            bTrack.Grow();
            CollectionAssert.AreEqual(new[] { "0 0000", "5 0001" }, Read(nTrack), "as the run stood when it was suspended");
            CollectionAssert.AreEqual(new[] { "0 0", "5 1" }, Read(bTrack));

            Assert.AreEqual(RunStatus.Suspended, sim.RunTo(100).Status);
            Assert.IsTrue(nTrack.Grow(), "the step carried on changed what it settled on");
            bTrack.Grow();
            CollectionAssert.AreEqual(Read(sim.History.SettledChanges(n)), Read(nTrack));
            CollectionAssert.AreEqual(new[] { "0 0000", "5 0010", "10 0011" }, Read(nTrack));
            CollectionAssert.AreEqual(new[] { "0 0" }, Read(bTrack), "b went back to 0 in the same step, so at 5 it never changed");

            Assert.AreEqual(RunStatus.Finished, sim.RunTo(100).Status);
            nTrack.Grow();
            CollectionAssert.AreEqual(Read(sim.History.SettledChanges(n)), Read(nTrack));
            CollectionAssert.AreEqual(new long[] { 5, 10 }, sim.History.Suspensions);
        }

        [Test]
        public void TheSlicer_RunsOn_OnlyARunSuspendedOrAtItsHorizon()
        {
            Design design = Elaborate(@"
module t;
    logic c = 0;
    always #1 c = ~c;
    initial #5 $stop;
endmodule", "t");

            var slicer = new RunSlicer(new SimSession(design, null), 20, Ticking(1.0), chunkEvents: 2);
            Assert.IsFalse(slicer.RunOn(40), "not while it is still being made");

            slicer.Finish();
            Assert.AreEqual(RunStatus.Suspended, slicer.Ended);
            Assert.AreEqual(5, slicer.Reached);

            Assert.IsTrue(slicer.RunOn(10), "a suspended run runs on");
            Assert.AreEqual(20, slicer.Horizon, "to its own horizon, which is later");
            Assert.IsFalse(slicer.Done);
            slicer.Finish();
            Assert.AreEqual(RunStatus.ReachedTime, slicer.Ended);
            Assert.AreEqual(20, slicer.Reached);

            Assert.IsFalse(slicer.RunOn(20), "at its horizon, it needs a later one");
            Assert.IsTrue(slicer.RunOn(30));
            slicer.Finish();
            Assert.AreEqual(30, slicer.Reached);

            Design loop = Elaborate("module loop; logic x = 0; initial forever x = ~x; endmodule", "loop");
            var stopped = new RunSlicer(new SimSession(loop, null), 100, Ticking(1.0));
            stopped.Finish();
            Assert.AreEqual(RunStatus.Stopped, stopped.Ended);
            Assert.IsFalse(stopped.RunOn(200), "a run that had to stop cannot go on");
        }

        [Test]
        public void ASessionRunOn_GrowsTheTimeline_AndTheRunGoesOnPastTheStop()
        {
            RunSession run = RunSession.Open(WorkbenchSamples.Counter(), Ticking(0.001));
            run.Finish();
            WorkbenchModel model = run.Model;
            long end = WorkbenchSamples.CounterEnd;

            Assert.IsTrue(run.Suspended, "the board's testbench ends on $stop");
            Assert.IsTrue(model.Complete, "suspended at the timeline's end, the timeline is made");
            int version = model.RunVersion;

            Assert.IsTrue(run.RunOn(2 * end));
            Assert.AreEqual(2 * end, model.End);
            Assert.IsFalse(model.Complete, "the longer timeline is still to be made");
            Assert.AreNotEqual(version, model.RunVersion);

            run.Finish();
            Assert.IsTrue(model.Complete);
            Assert.AreEqual(RunStatus.ReachedTime, run.Ended, "nothing in the testbench is left to stop it again");
            SignalTrack count = model.RowTrack(0);
            Assert.Greater(count[count.Count - 1].Time, end, "and the counter went on counting past the $stop");
        }

        [Test]
        public void AWaveView_GrowsWithTheRun_FittedOrZoomed()
        {
            var fitted = new WaveView(1000f);
            fitted.Fit(36);
            Assert.IsTrue(fitted.Extend(72));
            Assert.IsTrue(fitted.IsFitted);
            Assert.AreEqual(72.0, fitted.Span);
            Assert.AreEqual(1000.0 / 72.0, fitted.PixelsPerUnit, 1e-9);
            Assert.IsFalse(fitted.Extend(50), "a run never shrinks");

            var zoomed = new WaveView(1000f);
            zoomed.Fit(36);
            zoomed.ZoomAbout(500f, 4);
            double ppu = zoomed.PixelsPerUnit, start = zoomed.Start;
            Assert.IsTrue(zoomed.Extend(72));
            Assert.AreEqual(ppu, zoomed.PixelsPerUnit, 1e-9, "a zoomed view keeps the width of a unit");
            Assert.AreEqual(start, zoomed.Start, 1e-9, "and where it starts");
            Assert.AreEqual(72.0, zoomed.Span);
        }

        // -----------------------------------------------------------------
        // Breakpoints
        // -----------------------------------------------------------------

        [TestCase("1", 1, "1")]
        [TestCase("4'b1010", 4, "1010")]
        [TestCase("'hA", 4, "1010")]
        [TestCase("10", 4, "1010")]
        [TestCase("x", 3, "xxx")]
        [TestCase("Z", 2, "zz")]
        [TestCase("'1", 4, "1111")]
        [TestCase("8'hx", 4, "xxxx")]
        [TestCase("4'b10", 8, "00000010")]
        [TestCase(" 3 ", 2, "11")]
        public void AValueTyped_IsReadAsALiteral_FittedToTheSignal(string text, int width, string expected)
        {
            Assert.IsTrue(Breakpoints.TryParseValue(text, width, out LogicVec value, out string error), error);
            Assert.AreEqual(width, value.Width);
            Assert.AreEqual(expected, value.Format(Radix.Binary));
        }

        [TestCase("16", 4)]
        [TestCase("4'b1010", 2)]
        [TestCase("abc", 4)]
        [TestCase("", 4)]
        public void AValueThatIsNoneOrDoesNotFit_IsRefused_WithWhy(string text, int width)
        {
            Assert.IsFalse(Breakpoints.TryParseValue(text, width, out _, out string error));
            Assert.IsNotEmpty(error);
        }

        [Test]
        public void Breakpoints_FireStrictlyAfter_OnAMoment_AValue_AndAnX()
        {
            Design design = Elaborate(@"
module t;
    logic [1:0] v = 0;
    logic a = 0;
    initial begin
        #2 v = 1;
        #2 v = 2;
        #2 v = 1;
        #1 a = 1'bx;
        #2 a = 1;
        #2 v = 2'bx1;
    end
endmodule", "t");

            var sim = new Simulator(design);
            sim.RunTo(100);
            int v = design.Find("t.v").Index, a = design.Find("t.a").Index;
            var tracks = new Dictionary<int, SignalTrack>
            {
                [v] = new SignalTrack(sim.History, design.Find("t.v"), WorkbenchModel.TextOf),
                [a] = new SignalTrack(sim.History, design.Find("t.a"), WorkbenchModel.TextOf),
            };
            foreach (SignalTrack track in tracks.Values)
                track.Grow();
            Func<int, SignalTrack> trackOf = s => tracks.TryGetValue(s, out SignalTrack t) ? t : null;

            var points = new Breakpoints();
            Assert.IsTrue(Breakpoints.TryParseValue("1", 2, out LogicVec one, out _));
            Assert.IsTrue(points.Add(Breakpoint.OnValue(v, one)));
            Assert.IsFalse(points.Add(Breakpoint.OnValue(v, one)), "set once");

            Assert.AreEqual(2, points.Next(trackOf, 0).Value.Time, "on the change into the value");
            Assert.AreEqual(6, points.Next(trackOf, 2).Value.Time, "strictly after: from where it fired, the next one");
            Assert.IsNull(points.Next(trackOf, 6), "and none after the last");

            Assert.IsTrue(points.Add(Breakpoint.OnX(a)));
            Assert.AreEqual(7, points.Next(trackOf, 6).Value.Time, "a going x");
            Assert.AreEqual(1, points.Next(trackOf, 6).Value.Index);

            Assert.IsTrue(points.Add(Breakpoint.OnX(v)));
            Assert.AreEqual(11, points.Next(trackOf, 7).Value.Time, "a bus with one x bit in it goes x");

            Assert.IsTrue(points.ToggleAt(3), "a moment, set");
            Assert.AreEqual(3, points.Next(trackOf, 2).Value.Time);
            Assert.IsTrue(points.HasAt(3));
            Assert.IsFalse(points.ToggleAt(3), "and taken away");
            Assert.IsFalse(points.HasAt(3));

            int version = points.Version;
            Assert.AreEqual(2, points.RemoveOn(v));
            Assert.AreNotEqual(version, points.Version);
            Assert.IsFalse(points.HasOn(v));
            Assert.IsTrue(points.HasOn(a));
            Assert.AreEqual(1, points.KeepOnly(s => s != a));
            Assert.AreEqual(0, points.Count);
        }

        [Test]
        public void ABreakpointOnX_WaitsPastTimeZero_WhereEverythingStartsX()
        {
            Design design = Elaborate(@"
module t;
    logic a;
    initial begin
        #4 a = 0;
        #4 a = 1'bx;
    end
endmodule", "t");

            var sim = new Simulator(design);
            sim.RunTo(100);
            var track = new SignalTrack(sim.History, design.Find("t.a"), WorkbenchModel.TextOf);
            track.Grow();

            var points = new Breakpoints();
            points.Add(Breakpoint.OnX(design.Find("t.a").Index));
            Assert.AreEqual(8, points.Next(_ => track, 0).Value.Time);
        }

        // -----------------------------------------------------------------
        // Playing
        // -----------------------------------------------------------------

        [Test]
        public void Playing_MovesAtTheSpeed_OnTheLadder()
        {
            Playback playback = Counter();
            Assert.AreEqual(3.2, playback.UnitsPerSecond, "3.2 a second to begin with, as Ahmad asked");
            Assert.AreEqual(2 * SchematicStyle.NanosecondsPerSecond, playback.UnitsPerSecond, "twice the reference's");

            playback.Play();
            playback.Frame(1.0);
            Assert.AreEqual(3.2, playback.Model.Time, 1e-9);

            Assert.IsTrue(playback.Faster());
            playback.Frame(1.0);
            Assert.AreEqual(3.2 + 6.4, playback.Model.Time, 1e-9);
            Assert.AreEqual(6.4, playback.Model.UnitsPerSecond, "and the model is told it");

            playback.SetSpeed(0);
            Assert.IsFalse(playback.Slower(), "nothing slower than the slowest");
            playback.SetSpeed(Playback.InstantSpeed);
            Assert.IsFalse(playback.Faster(), "nothing faster than INSTANT");
            Assert.IsTrue(playback.Instant);
            Assert.AreEqual(SchematicStyle.NanosecondsPerSecond, playback.Model.UnitsPerSecond, "INSTANT flashes as the reference does");
        }

        [Test]
        public void Playing_PausesAtTheTestbenchsStop_AndPlayRunsTheRunOn()
        {
            Playback playback = Counter();
            long end = WorkbenchSamples.CounterEnd;

            playback.Play();
            playback.Frame(1000.0);
            Assert.AreEqual(end, playback.Model.Time, 1e-9);
            Assert.IsFalse(playback.Playing);
            Assert.AreEqual(PauseReason.Suspended, playback.Reason, "the testbench's $stop");
            Assert.AreEqual(end, playback.ReasonTime);

            playback.Play();
            Assert.AreEqual(2 * end, playback.Model.End, "PLAY at the $stop runs on as long again");
            Assert.AreEqual(PauseReason.None, playback.Reason);
            Assert.IsTrue(playback.Playing);

            playback.Frame(1.0);
            Assert.AreEqual(end, playback.Model.Time, 1e-9, "a moment not yet made waits for the run");

            playback.Run.Finish();
            playback.Frame(1000.0);
            Assert.AreEqual(2 * end, playback.Model.Time, 1e-9);
            Assert.AreEqual(PauseReason.End, playback.Reason, "nothing stops it again but the end");

            playback.Play();
            Assert.AreEqual(3 * end, playback.Model.End, "PLAY at an end the run reached runs it on again");
        }

        [Test]
        public void Restart_GoesBackToTheStart_AndPausesAtTheStopAgain()
        {
            Playback playback = Counter();
            long end = WorkbenchSamples.CounterEnd;
            playback.Play();
            playback.Frame(1000.0);
            playback.Play();
            playback.Run.Finish();
            playback.Frame(1000.0);

            playback.Restart();
            Assert.AreEqual(0.0, playback.Model.Time);
            Assert.IsTrue(playback.Playing);
            playback.Frame(1000.0);
            Assert.AreEqual(end, playback.Model.Time, 1e-9, "the $stop is in the history, and pauses it where the run was suspended");
            Assert.AreEqual(PauseReason.Suspended, playback.Reason);
        }

        [Test]
        public void RunTo_PausesOnTheWay_AndRunsTheRunOnToReachIt()
        {
            Playback playback = Counter();
            long end = WorkbenchSamples.CounterEnd;

            playback.RunTo = 10;
            playback.Play();
            playback.Frame(1000.0);
            Assert.AreEqual(10.0, playback.Model.Time, 1e-9);
            Assert.AreEqual(PauseReason.RunTo, playback.Reason);

            playback.Play();
            playback.Frame(1000.0);
            Assert.AreEqual(end, playback.Model.Time, 1e-9, "PLAY again goes on past it");

            playback.RunTo = 5 * end;
            playback.Play();
            Assert.AreEqual(5 * end, playback.Model.End, "run on as far as RUN TO when that is further");
            playback.Run.Finish();
            playback.Frame(1000.0);
            Assert.AreEqual(5 * end, playback.Model.Time, 1e-9);
            Assert.AreEqual(PauseReason.RunTo, playback.Reason, "the RUN TO moment, which is also the end");
        }

        [Test]
        public void ABreakpoint_PausesThePlayhead_AndPlayGoesOnPastIt()
        {
            Playback playback = Counter();
            int count = playback.Model.RowSignal(0);
            SignalTrack track = playback.Model.TrackOf(count);
            Assert.IsTrue(Breakpoints.TryParseValue("3", 3, out LogicVec three, out _));
            long becomesThree = Enumerable.Range(0, track.Count).Select(i => track[i]).First(c => c.Value.Equals(three)).Time;

            playback.Breakpoints.Add(Breakpoint.OnValue(count, three));
            playback.Breakpoints.Add(Breakpoint.At(becomesThree + 2));
            playback.RunTo = becomesThree + 2;

            playback.Play();
            playback.Frame(1000.0);
            Assert.AreEqual(becomesThree, playback.Model.Time, 1e-9);
            Assert.AreEqual(PauseReason.Breakpoint, playback.Reason);
            Assert.AreEqual(0, playback.ReasonBreak);

            playback.Play();
            playback.Frame(1000.0);
            Assert.AreEqual(becomesThree + 2, playback.Model.Time, 1e-9);
            Assert.AreEqual(PauseReason.Breakpoint, playback.Reason, "a breakpoint before RUN TO on one moment");
            Assert.AreEqual(1, playback.ReasonBreak);
        }

        [Test]
        public void Instant_GoesStraightToWhereItWouldPause()
        {
            Playback playback = Counter();
            playback.SetSpeed(Playback.InstantSpeed);
            playback.RunTo = 21;

            playback.Play();
            playback.Frame(0.001);
            Assert.AreEqual(21.0, playback.Model.Time, 1e-9);
            Assert.AreEqual(PauseReason.RunTo, playback.Reason);
        }

        [Test]
        public void Step_GoesFromEdgeToEdge_OfTheRowsGiven()
        {
            Playback playback = Counter();
            SignalTrack count = playback.Model.RowTrack(0);
            long first = count[1].Time, second = count[2].Time;

            Assert.IsTrue(playback.Step(CountRow));
            Assert.AreEqual(first, playback.Model.Time, 1e-9);
            Assert.IsTrue(playback.Step(CountRow));
            Assert.AreEqual(second, playback.Model.Time, 1e-9);
            Assert.IsFalse(playback.Playing, "a step pauses");

            playback.Model.Seek(second + 0.5);
            Assert.IsTrue(playback.StepBack(CountRow));
            Assert.AreEqual(second, playback.Model.Time, 1e-9, "back from part-way, to the edge just passed");
            Assert.IsTrue(playback.StepBack(CountRow));
            Assert.AreEqual(first, playback.Model.Time, 1e-9);
            Assert.IsFalse(playback.StepBack(CountRow), "nothing before the first edge");
            Assert.AreEqual(first, playback.Model.Time, 1e-9);
        }

        [Test]
        public void AGatesFlash_LastsTheSameSeconds_AtAnySpeed()
        {
            Playback playback = Counter();
            playback.SetSpeed(Playback.ReferenceSpeed);
            WorkbenchModel model = playback.Model;

            int gate = -1;
            long at = 0;
            for (int p = 0; p < model.Layout.Parts.Count && gate < 0; p++)
            {
                string output = model.Layout.Parts[p].Output;
                if (output == null || !model.Session.Design.TryFind(output, out SignalId id))
                    continue;
                SignalTrack track = model.TrackOf(id.Index);
                if (track != null && track.Count > 1 && track[1].Time > 0)
                {
                    gate = p;
                    at = track[1].Time;
                }
            }

            Assert.GreaterOrEqual(gate, 0, "sanity: a gate that flips");

            model.Seek(at + SchematicStyle.FlashTime / 2);
            float atTheReference = model.GateFlash(gate, out _);
            Assert.AreEqual(0.5f, atTheReference, 1e-4, "sanity: half-way through its flash");
            model.Seek(at + SchematicStyle.FlashTime);
            Assert.AreEqual(0f, model.GateFlash(gate, out _), "over after FlashTime at the reference's speed, exactly as before");

            playback.SetSpeed(Playback.ReferenceSpeed + 1);
            model.Seek(at + SchematicStyle.FlashTime);
            Assert.AreEqual(atTheReference, model.GateFlash(gate, out _), 1e-6, "twice the speed, twice the time units: the same seconds");
        }
    }
}
