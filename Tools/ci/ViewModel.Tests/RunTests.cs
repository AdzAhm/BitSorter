using System;
using System.Collections.Generic;
using System.Linq;
using BitSorter.Board;
using BitSorter.Sim;
using BitSorter.SystemVerilog;
using BitSorter.SystemVerilog.Syntax;
using NUnit.Framework;
using BoardStimulus = BitSorter.Board.Stimulus;
using BoardTrack = BitSorter.Board.Track;
using EngineGate = BitSorter.Sim.GateKind;

namespace BitSorter.ViewModel.Tests
{
    /// <summary>
    /// A run made a slice at a time (Part 9b-1): the slicer's budget and its ends, and a model that
    /// follows the run as it is made answering every moment as one made in one go.
    /// </summary>
    public class RunTests
    {
        /// <summary>Chains of NOTs from an input to an output, the inputs toggling out of step: changes always on their way, and many events.</summary>
        private static Project Chains(int chains = 6, int gates = 5, long end = 60)
        {
            var editor = new BoardEditor(Project.New("chains"));
            const string m = "chains";

            int Add(Part part, double x, double y)
            {
                EditResult added = editor.Add(m, part, new Placement(x, y));
                Assert.IsTrue(added.Done, added.Refusal);
                return added.Id;
            }

            void Wire(int from, string fromPin, int to, string toPin) =>
                Assert.IsTrue(editor.Connect(m, Endpoint.At(from, fromPin), Endpoint.At(to, toPin)).Done);

            var tracks = new List<BoardTrack>();
            for (int c = 0; c < chains; c++)
            {
                double y = -1.5 * c;
                int input = Add(new PortPart { Name = "i" + c, Width = 1 }, 1, y);
                int output = Add(new PortPart { Name = "o" + c, Direction = PortDirection.Output, Width = 1 }, 3 + 2 * gates, y);

                int previous = input;
                string pin = "out";
                for (int g = 0; g < gates; g++)
                {
                    // Uneven delays, so some changes are cancelled on their way: swallowed ones to read.
                    long delay = 1 + (g + c) % 3;
                    int not = Add(new GatePart { Kind = EngineGate.Not, Inputs = 1, Rise = delay, Fall = delay + 1 }, 3 + 2 * g, y);
                    Wire(previous, pin, not, "a");
                    previous = not;
                    pin = "y";
                }

                Wire(previous, pin, output, "in");

                var steps = new List<Step>();
                for (long t = 1 + c % 4, v = 0; t < end; t += 1 + c % 3, v ^= 1)
                    steps.Add(new Step(t, LogicVec.FromUInt64((ulong)v, 1)));
                tracks.Add(new BoardTrack { Port = "i" + c, Steps = steps.ToArray() });
            }

            Assert.IsTrue(editor.SetStimulus(new BoardStimulus { Tracks = tracks.ToArray(), End = end }).Done);
            return editor.Project;
        }

        /// <summary>A clock that moves on by <paramref name="step"/> milliseconds every time it is read.</summary>
        private static Func<double> Ticking(double step)
        {
            double now = 0;
            return () => { double read = now; now += step; return read; };
        }

        // -----------------------------------------------------------------
        // The slicer
        // -----------------------------------------------------------------

        [Test]
        public void ASlice_RunsChunksWhileItsBudgetLasts()
        {
            (Design design, SchematicLayout _, long horizon) = WorkbenchModel.Prepare(Chains());
            var slicer = new RunSlicer(new SimSession(design, null), horizon, Ticking(3.0), chunkEvents: 5);

            // Read at 0 to start; 3 and 6 are inside 8 ms, so a chunk follows each; 9 is past it.
            Assert.AreEqual(3, slicer.Slice());
            Assert.IsFalse(slicer.Done, "sanity: five events a chunk is far from the whole run");
        }

        [Test]
        public void ASlice_RunsAtLeastOneChunk_EvenWithNoTimeLeft()
        {
            (Design design, SchematicLayout _, long horizon) = WorkbenchModel.Prepare(Chains());
            var slicer = new RunSlicer(new SimSession(design, null), horizon, Ticking(100.0), chunkEvents: 5);

            Assert.AreEqual(1, slicer.Slice(), "a frame already over its time still moves the run on");
            Assert.AreEqual(1, slicer.Slice());
            Assert.AreEqual(2, slicer.Chunks);
        }

        [Test]
        public void ARunWithNothingToDo_StillEnds_AtItsHorizon()
        {
            // Nothing is ever queued and nothing calls $finish: asked for long.MaxValue, the engine would never return.
            (Design design, _) = Elaborate("module quiet; logic a; endmodule", "quiet");
            var slicer = new RunSlicer(new SimSession(design, null), 100, Ticking(1.0));

            slicer.Slice();

            Assert.IsTrue(slicer.Done);
            Assert.AreEqual(RunStatus.ReachedTime, slicer.Ended);
            Assert.AreEqual(100, slicer.Reached);
            Assert.Throws<ArgumentOutOfRangeException>(() => new RunSlicer(new SimSession(design, null), long.MaxValue, Ticking(1.0)),
                "an endless horizon is refused rather than hanging the page");
        }

        [Test]
        public void ARunThatCannotGoOn_EndsOnce_AsStopped()
        {
            // A process that never waits: the engine stops it rather than hang.
            (Design design, _) = Elaborate("module loop; logic x = 0; initial forever x = ~x; endmodule", "loop");
            var slicer = new RunSlicer(new SimSession(design, null), 100, Ticking(1.0), chunkEvents: 50);

            int slices = 0;
            while (!slicer.Done && slices < 10000)
            {
                slicer.Slice();
                slices++;
            }

            Assert.IsTrue(slicer.Done);
            Assert.AreEqual(RunStatus.Stopped, slicer.Ended);
            Assert.AreEqual(0, slicer.Slice(), "and a finished run runs nothing more");
        }

        // -----------------------------------------------------------------
        // Growing with the run
        // -----------------------------------------------------------------

        [TestCase(1)]
        [TestCase(7)]
        [TestCase(1000)]
        public void ASignalsTrack_GrownAfterEverySlice_IsWhatTheFinishedRunSettledOn(int chunk)
        {
            (Design design, SchematicLayout _, long horizon) = WorkbenchModel.Prepare(Chains());
            var session = new SimSession(design, null);
            session.RunTo(horizon, chunk);

            var tracks = new SignalTrack[design.Signals.Count];
            for (int i = 0; i < tracks.Length; i++)
            {
                tracks[i] = new SignalTrack(session.History, design.SignalAt(i), WorkbenchModel.TextOf);
                tracks[i].Grow();
            }

            while (session.RunTo(horizon, chunk).Status == RunStatus.Paused)
                foreach (SignalTrack track in tracks)
                    track.Grow();
            foreach (SignalTrack track in tracks)
                track.Grow();

            for (int i = 0; i < tracks.Length; i++)
            {
                List<ValueChange> settled = session.History.SettledChanges(design.SignalAt(i));
                Assert.AreEqual(settled.Count, tracks[i].Count, $"{design.Signals[i].Name}: how many settled changes");
                for (int k = 0; k < settled.Count; k++)
                {
                    Assert.AreEqual(settled[k].Time, tracks[i][k].Time, $"{design.Signals[i].Name}, change {k}");
                    Assert.IsTrue(settled[k].Value.Equals(tracks[i][k].Value), $"{design.Signals[i].Name}, change {k}");
                }
            }
        }

        [Test]
        public void ATrack_FindsTheEdgesEitherSideOfAMoment()
        {
            (Design design, SchematicLayout _, long horizon) = WorkbenchModel.Prepare(Chains(chains: 1, gates: 1));
            var session = new SimSession(design, null);
            session.RunTo(horizon);
            Assert.IsTrue(design.TryFind(design.Signals.First(s => s.Name.EndsWith(".i0", StringComparison.Ordinal)).Name, out SignalId input));

            var track = new SignalTrack(session.History, input, WorkbenchModel.TextOf);
            track.Grow();

            // The input steps at 1, 2, 3 ... (one chain: every unit from 1).
            Assert.AreEqual(1, track.NextEdgeAfter(0));
            Assert.AreEqual(3, track.NextEdgeAfter(2));
            Assert.AreEqual(2, track.LastEdgeBefore(3));
            Assert.IsNull(track.LastEdgeBefore(1), "the value at 0 is where it starts, not an edge");
            Assert.AreEqual(track.Count - 1, track.IndexAt(horizon));
            Assert.AreEqual(track.IndexAt(2), track.IndexAt(2.999), "a moment part-way through a step reads what the step settled on");
        }

        /// <summary>
        /// A model that follows the run slice by slice ends up answering every moment exactly as one
        /// made in one go: values, flashes, every marker, the waveform and its swallowed changes. At a
        /// slice of one event, changes are still on their way when the model reads them and are
        /// cancelled after, which is the case growing has to get right.
        /// </summary>
        [TestCase(1)]
        [TestCase(7)]
        [TestCase(1000)]
        public void AModelGrownInSlices_AnswersEveryMomentAsOneMadeInOneGo(int chunk)
        {
            foreach (Project project in new[] { ReferenceCircuit.Document(), Chains() })
            {
                WorkbenchModel whole = WorkbenchModel.Of(project);
                RunSession run = RunSession.Open(project, Ticking(0.5), chunkEvents: chunk);
                int frames = 0;
                while (!run.Done && frames++ < 100000)
                    run.Frame();

                WorkbenchModel grown = run.Model;
                Assert.IsTrue(grown.Complete);
                Assert.Greater(run.Slicer.Chunks, chunk == 1000 ? 0 : 3, "sanity: the run was made in several slices");
                Assert.AreEqual(whole.End, grown.End);
                Assert.AreEqual(whole.SettledAt, grown.SettledAt);

                SchematicLayout layout = whole.Layout;
                var a = new List<WaveSegment>();
                var b = new List<WaveSegment>();
                var sa = new List<double>();
                var sb = new List<double>();

                for (double t = 0; t <= whole.End; t += 0.05)
                {
                    whole.Seek(t);
                    grown.Seek(t);
                    string at = $"{project.TopModule.Name} at {t:0.00}, chunk {chunk}";

                    for (int n = 0; n < layout.Nets.Count; n++)
                        Assert.IsTrue(whole.NetValue(n).Equals(grown.NetValue(n)), $"net {n}, {at}");

                    for (int p = 0; p < layout.Parts.Count; p++)
                    {
                        Assert.AreEqual(whole.GateFlash(p, out LogicVec wv), grown.GateFlash(p, out LogicVec gv), $"part {p}'s flash, {at}");
                        Assert.IsTrue(wv.Equals(gv), $"part {p}'s flash value, {at}");
                        Assert.IsTrue(whole.PartValue(p).Equals(grown.PartValue(p)), $"part {p}, {at}");
                    }

                    Assert.AreEqual(whole.InFlight, grown.InFlight, $"in flight, {at}");
                    Assert.AreEqual(whole.Markers.Count, grown.Markers.Count, $"markers, {at}");
                    for (int k = 0; k < whole.Markers.Count; k++)
                    {
                        ChangeMarker w = whole.Markers[k], g = grown.Markers[k];
                        Assert.AreEqual(w.Net, g.Net, $"marker {k}'s net, {at}");
                        Assert.AreEqual(w.Distance, g.Distance, $"marker {k}'s distance, {at}");
                        Assert.IsTrue(w.Point.Equals(g.Point), $"marker {k}'s point, {at}");
                        Assert.AreEqual(w.Text, g.Text, $"marker {k}'s text, {at}");
                        Assert.AreEqual(w.Alpha, g.Alpha, $"marker {k}'s alpha, {at}");
                        Assert.AreEqual(w.Swallowed, g.Swallowed, $"marker {k} swallowed, {at}");
                    }

                    for (int r = 0; r < layout.Rows.Count; r++)
                    {
                        whole.RowValue(r, out string wt);
                        grown.RowValue(r, out string gt);
                        Assert.AreEqual(wt, gt, $"row {r}'s value, {at}");

                        whole.RowSegments(r, a, whole.End);
                        grown.RowSegments(r, b, grown.End);
                        Assert.AreEqual(a.Count, b.Count, $"row {r}'s segments, {at}");

                        whole.RowSwallowed(r, sa);
                        grown.RowSwallowed(r, sb);
                        CollectionAssert.AreEqual(sa, sb, $"row {r}'s swallowed changes, {at}");
                        Assert.AreEqual(whole.RowSwallowedSoFar(r), grown.RowSwallowedSoFar(r), $"row {r}'s swallowed so far, {at}");
                        Assert.AreEqual(whole.RowChangeCount(r), grown.RowChangeCount(r), $"row {r}'s changes, {at}");
                    }
                }
            }
        }

        [Test]
        public void AModelStillBeingMade_ShowsNoMomentPastWhereTheRunHasGot()
        {
            RunSession run = RunSession.Open(Chains(), Ticking(100.0), chunkEvents: 20);
            WorkbenchModel model = run.Model;

            Assert.IsFalse(model.Complete, "sanity: twenty events is a sliver of the run");
            Assert.AreEqual(long.MaxValue, model.SettledAt, "not known until the run is made");

            model.Seek(model.End);
            Assert.AreEqual(model.Reached, model.Time, 1e-9, "held where the run has got");

            run.Finish();
            Assert.IsTrue(model.Complete);
            Assert.Less(model.SettledAt, long.MaxValue);
            model.Seek(model.End);
            Assert.AreEqual(model.End, model.Time, 1e-9);
        }

        [Test]
        public void TheReferenceCircuit_IsMadeInItsFirstSlice()
        {
            RunSession run = RunSession.Open(ReferenceCircuit.Document(), Ticking(0.001));

            Assert.IsTrue(run.Done, "the workbench opens on it made, exactly as when it was made in one go");
            Assert.AreEqual(1, run.Slicer.Chunks);
        }

        private static (Design Design, Elaboration Elaboration) Elaborate(string source, string top)
        {
            SyntaxTree tree = SyntaxReader.ReadText(source, "test.sv", null);
            Assert.IsEmpty(tree.Diagnostics, string.Join("; ", tree.Diagnostics));
            Elaboration elaboration = Elaborator.Elaborate(tree, top);
            Assert.IsFalse(elaboration.HasErrors, string.Join("; ", elaboration.Diagnostics));
            return (elaboration.Design, elaboration);
        }
    }
}
