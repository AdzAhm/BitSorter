using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using BitSorter.Board;
using BitSorter.SystemVerilog;
using BitSorter.SystemVerilog.Syntax;
using BitSorter.View;
using BitSorter.ViewModel;
using NUnit.Framework;
using UnityEngine;
using BoardStimulus = BitSorter.Board.Stimulus;
using BoardTrack = BitSorter.Board.Track;
using EngineGate = BitSorter.Sim.GateKind;
using Object = UnityEngine.Object;

namespace BitSorter.LogicCore.Tests
{
    /// <summary>
    /// The plan's budget for Part 8: the canvas at 60 frames a second with 300 wires and 100 changes
    /// in flight -- and, first, drawn at all: in one mesh a layer that size was over uGUI's 65,000
    /// vertices, and threw.
    /// </summary>
    /// <remarks>
    /// What scales with the circuit is a frame of playback: moving to the moment, deciding which chunks
    /// changed, and rebuilding those (<see cref="WorkbenchLayers"/>). It is timed through real
    /// <see cref="WorkbenchGraphic"/>s on a canvas, rebuilt by uGUI's own update. A frame at 60 is
    /// 16.7 ms and the canvas is not all a frame does, so its share is held to <see cref="Budget"/>, by
    /// the median of many frames after a warm-up, so that one slow frame on a busy machine is not a
    /// failure -- and the best of three runs of them, so that a busy stretch is not one either: what
    /// is measured is what the drawing costs, not what else the machine was doing.
    ///
    /// The board is harder than the plan's: about 200 changes in flight rather than 100, and every
    /// wire, its gates two cells apart, routed backwards round a loop of four bends -- 38 points
    /// where a wire on a real schematic has two to twenty.
    /// </remarks>
    public class WorkbenchPerformanceTests
    {
        /// <summary>Thirty chains of nine NOTs, each from an input to an output: 300 wires.</summary>
        private const int Chains = 30, Gates = 9;

        /// <summary>The canvas's share of a 60 Hz frame, in milliseconds.</summary>
        private const double Budget = 6.0;

        private Dictionary<string, int> _preferences;

        /// <summary>
        /// Settings of the fixture's own, with motion as it ships: the machine's REDUCED MOTION would
        /// otherwise leave out every trail, halo and flash, and the budget would be met by a cheaper
        /// picture than the one players see.
        /// </summary>
        [SetUp]
        public void OwnSettings()
        {
            _preferences = Preferences.Redirected;
            Preferences.Redirected = new Dictionary<string, int>();
            ReducedMotion.Set(false);
        }

        [TearDown]
        public void PutSettingsBack() => Preferences.Redirected = _preferences;

        private static Project Board()
        {
            var editor = new BoardEditor(Project.New("chains"));
            const string m = "chains";

            int Add(Part part, double x, double y)
            {
                EditResult added = editor.Add(m, part, new Placement(x, y));
                Assert.IsTrue(added.Done, added.Refusal);
                return added.Id;
            }

            void Wire(int from, string fromPin, int to, string toPin)
            {
                EditResult wired = editor.Connect(m, Endpoint.At(from, fromPin), Endpoint.At(to, toPin));
                Assert.IsTrue(wired.Done, wired.Refusal);
            }

            var tracks = new List<BoardTrack>();
            for (int c = 0; c < Chains; c++)
            {
                double y = -1.5 * c;
                int input = Add(new PortPart { Name = "i" + c, Width = 1 }, 1, y);
                int output = Add(new PortPart { Name = "o" + c, Direction = PortDirection.Output, Width = 1 }, 3 + 2 * Gates, y);

                int previous = input;
                string pin = "out";
                for (int g = 0; g < Gates; g++)
                {
                    int not = Add(new GatePart { Kind = EngineGate.Not, Inputs = 1, Rise = 3, Fall = 3 }, 3 + 2 * g, y);
                    Wire(previous, pin, not, "a");
                    previous = not;
                    pin = "y";
                }

                Wire(previous, pin, output, "in");

                // Every input toggling every 4 ns, the chains out of step, so changes are always on their way.
                var steps = new List<Step>();
                for (long t = 1 + c % 4, v = 0; t < 60; t += 4, v ^= 1)
                    steps.Add(new Step(t, LogicVec.FromUInt64((ulong)v, 1)));
                tracks.Add(new BoardTrack { Port = "i" + c, Steps = steps.ToArray() });
            }

            EditResult set = editor.SetStimulus(new BoardStimulus { Tracks = tracks.ToArray(), End = 60 });
            Assert.IsTrue(set.Done, set.Refusal);
            return editor.Project;
        }

        /// <summary>The model, and its picture in chunks on a canvas of its own.</summary>
        private static (WorkbenchModel Model, WorkbenchLayers Layers, GameObject Canvas) Bench() => Bench(WorkbenchModel.Of(Board()));

        /// <inheritdoc cref="Bench()"/>
        private static (WorkbenchModel Model, WorkbenchLayers Layers, GameObject Canvas) Bench(WorkbenchModel model)
        {
            Assert.GreaterOrEqual(model.Layout.Nets.Sum(n => n.Branches.Count), 300, "sanity: 300 wires");

            var canvas = new GameObject("performance canvas", typeof(Canvas));
            canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var schematic = new GameObject("schematic", typeof(RectTransform)).GetComponent<RectTransform>();
            schematic.SetParent(canvas.transform, false);
            var waveform = new GameObject("waveform", typeof(RectTransform)).GetComponent<RectTransform>();
            waveform.SetParent(canvas.transform, false);

            var layers = new WorkbenchLayers(model);
            layers.BuildWires(schematic, model.Layout.Size.ToUnity());
            layers.BuildParts(schematic, model.Layout.Size.ToUnity());
            layers.BuildTiming(waveform, new Vector2(1000f, 40f + 30f * model.Layout.Rows.Count));
            return (model, layers, canvas);
        }

        private static void Show(WorkbenchModel model, WorkbenchLayers layers, double t, bool all = false)
        {
            model.Seek(t);
            layers.Refresh(all);
            Canvas.ForceUpdateCanvases();
        }

        /// <remarks>
        /// Met on 2026-10-14, a step at a time, each measured here -- the first five in the editor, the
        /// next three in batch runs, which are slower: 61 ms with every layer one mesh redrawn whole; 35
        /// with the parts drawn once and the waveform revealed; 22 with the meshes built in a buffer of
        /// two channels rather than uGUI's helper of eight; 13 with reduced motion read once rather than
        /// from PlayerPrefs for every part; 8.6 with no corner bent where a wire runs straight and the
        /// ring drawn from a table; then 6.1 with every shape written straight into the buffer, 5.9 with
        /// each point's normal taken from its route, and 5.4 to 5.6 with each chunk finding its own
        /// changes without copying every one out -- which is 3.3 in the editor. At a canvas scale of 1:
        /// a bigger canvas cuts every curve finer. It logs where the time goes.
        /// </remarks>
        [Test]
        public void AFrameOfPlayback_With300WiresAnd100ChangesInFlight_FitsInItsShareOfA60HzFrame()
        {
            (WorkbenchModel model, WorkbenchLayers layers, GameObject canvas) = Bench();
            try
            {
                Show(model, layers, 19.9, all: true);
                for (int i = 0; i < 30; i++)
                    Show(model, layers, 20 + i * 0.1);

                var times = new List<double>();
                var seeking = new List<double>();
                var refreshing = new List<double>();
                var medians = new List<double>();
                int most = 0;
                var clock = new Stopwatch();
                WorkbenchGraphic.ResetSpent();
                const int runs = 3, frames = 240;
                for (int run = 0; run < runs; run++)
                {
                    var runTimes = new List<double>();
                    for (int i = 0; i < frames; i++)
                    {
                        // Timed whole, and by phase: the moment, deciding what changed, and uGUI rebuilding it.
                        clock.Restart();
                        model.Seek(23 + i * 0.1);
                        double seek = clock.Elapsed.TotalMilliseconds;
                        layers.Refresh();
                        double refresh = clock.Elapsed.TotalMilliseconds;
                        Canvas.ForceUpdateCanvases();
                        clock.Stop();

                        runTimes.Add(clock.Elapsed.TotalMilliseconds);
                        seeking.Add(seek);
                        refreshing.Add(refresh - seek);
                        most = Mathf.Max(most, model.Markers.Count);
                    }

                    times.AddRange(runTimes);
                    runTimes.Sort();
                    medians.Add(runTimes[runTimes.Count / 2]);
                }

                times.Sort();
                seeking.Sort();
                refreshing.Sort();
                double median = medians.Min();
                const int all = runs * frames;
                UnityEngine.Debug.Log($"[WorkbenchPerformance] {layers.All.Count()} chunks, median {median:0.00} ms, the best of " +
                    $"{string.Join(", ", medians.Select(m => m.ToString("0.00")))} (moment " +
                    $"{seeking[seeking.Count / 2]:0.00}, deciding {refreshing[refreshing.Count / 2]:0.00}, rebuilding the rest -- on average " +
                    $"{WorkbenchGraphic.DrawingMilliseconds / all:0.00} drawing meshes and {WorkbenchGraphic.HandingOverMilliseconds / all:0.00} " +
                    $"handing them over), 90th " +
                    $"{times[times.Count * 9 / 10]:0.00} ms, slowest {times[times.Count - 1]:0.00} ms, at most {most} changes in flight, " +
                    $"largest chunk {layers.MostVertices} vertices, code optimization {UnityEditor.Compilation.CompilationPipeline.codeOptimization}");

                var byLayer = new System.Text.StringBuilder();
                foreach (WorkbenchGraphic.Layer layer in System.Enum.GetValues(typeof(WorkbenchGraphic.Layer)))
                    byLayer.Append($" {layer} {WorkbenchGraphic.DrawingByLayer[(int)layer] / all:0.00} ms {WorkbenchGraphic.VerticesByLayer[(int)layer] / all} vertices;");
                UnityEngine.Debug.Log("[WorkbenchPerformance] drawing a frame, by layer:" + byLayer);

                Assert.Greater(layers.MostVertices, 0, "sanity: nothing was drawn");
                Assert.Greater(CanvasMotion.Glow, 0f, "sanity: drawn with its glow, as it ships");
                Assert.Greater(WorkbenchGraphic.VerticesByLayer[(int)WorkbenchGraphic.Layer.Changes], 0, "sanity: the changes in flight were redrawn");
                Assert.GreaterOrEqual(most, 100, "sanity: 100 changes in flight");
                Assert.LessOrEqual(median, Budget, $"a frame of playback took {median:0.00} ms at best, over the canvas's {Budget} ms");
            }
            finally
            {
                Object.DestroyImmediate(canvas);
            }
        }

        /// <summary>
        /// A frame while the run is still being made (Part 9b), as the panel has one: a slice of the run,
        /// the rows cut again if they must be, and the moment moved on as playback moves it -- from the
        /// same busy moment as the playback test above, a tenth of a nanosecond a frame. The slice has its
        /// own 8 ms; the rest is held to the canvas's share by the best of three runs' medians, as the
        /// playback test is, and as the rows grow no chunk outgrows a mesh.
        /// </summary>
        [Test]
        public void AFrameWhileTheRunIsStillBeingMade_FitsInItsShare_AndNoChunkOutgrowsAMesh()
        {
            var medians = new List<double>();
            int frames = 0;
            double growing = 0;

            for (int attempt = 0; attempt < 3; attempt++)
            {
                // A clock a hundred milliseconds on at every read: one chunk a slice, so the run takes many frames.
                double now = 0;
                RunSession run = RunSession.Open(Board(), () => now += 100.0, chunkEvents: 100);

                // Made as far as the moment playback starts from, so that moment can be shown.
                while (!run.Done && run.Model.Reached < 24)
                    run.Frame();
                Assert.IsFalse(run.Done, "sanity: the rest of the run is still to be made");

                (WorkbenchModel model, WorkbenchLayers layers, GameObject canvas) = Bench(run.Model);
                try
                {
                    Show(model, layers, 19.9, all: true);

                    var times = new List<double>();
                    var clock = new Stopwatch();
                    double t = 20;
                    while (!run.Done && times.Count < 5000)
                    {
                        run.Frame();

                        clock.Restart();
                        layers.Grew();
                        growing += clock.Elapsed.TotalMilliseconds;
                        model.Seek(t);
                        layers.Refresh();
                        Canvas.ForceUpdateCanvases();
                        clock.Stop();

                        times.Add(clock.Elapsed.TotalMilliseconds);
                        t += 0.1;
                        Assert.LessOrEqual(layers.MostVertices, WorkbenchGraphic.MeshLimit, $"at {model.Reached} ns a chunk outgrew a mesh");
                    }

                    Assert.IsTrue(run.Done, "the run was made");
                    Assert.Greater(times.Count, 5, "sanity: made over many frames");

                    frames += times.Count;
                    times.Sort();
                    medians.Add(times[times.Count / 2]);
                }
                finally
                {
                    Object.DestroyImmediate(canvas);
                }
            }

            double median = medians.Min();
            UnityEngine.Debug.Log($"[WorkbenchPerformance] while the run is made: median {median:0.00} ms, the best of " +
                $"{string.Join(", ", medians.Select(m => m.ToString("0.00")))} over {frames} frames (cutting the rows again " +
                $"{growing / frames:0.00} ms on average)");
            Assert.LessOrEqual(median, Budget, $"a frame while the run is made took {median:0.00} ms at best, over the canvas's {Budget} ms");
        }

        /// <summary>
        /// Before the inputs first change nothing is on its way and nothing flashes, so moving the moment
        /// redraws the playhead and nothing else: the still layers stay as they were drawn.
        /// </summary>
        [Test]
        public void AMomentWithNothingMoving_RedrawsOnlyThePlayhead()
        {
            (WorkbenchModel model, WorkbenchLayers layers, GameObject canvas) = Bench();
            try
            {
                Show(model, layers, 0.2, all: true);
                Assert.AreEqual(0, model.Markers.Count, "sanity: nothing is on its way before the inputs first change");

                Dictionary<WorkbenchGraphic, int> before = layers.All.ToDictionary(g => g, g => g.Rebuilds);
                Show(model, layers, 0.6);

                string[] redrawn = layers.All.Where(g => g.Rebuilds != before[g]).Select(g => g.Draws.ToString()).ToArray();
                CollectionAssert.AreEqual(new[] { "Playhead" }, redrawn, "only the playhead moved");
            }
            finally
            {
                Object.DestroyImmediate(canvas);
            }
        }

        /// <summary>
        /// A moment in the middle of the run redraws the wire chunks holding a net whose value it
        /// changed and no other, the changes chunks holding a change then or now and no other, and none
        /// of the waveform: its rows were drawn once, for the whole run.
        /// </summary>
        [Test]
        public void AMoment_RedrawsTheWiresWhoseValuesChanged_AndNoneOfTheWaveform()
        {
            (WorkbenchModel model, WorkbenchLayers layers, GameObject canvas) = Bench();
            try
            {
                SchematicPalette palette = SchematicPalette.Current;
                int nets = model.Layout.Nets.Count;

                // Across 23 ns, where a quarter of the inputs flip and changes land along the chains.
                Show(model, layers, 22.9, all: true);
                Color[] then = Enumerable.Range(0, nets).Select(i => palette.Of(model.NetValue(i))).ToArray();
                HashSet<int> travelledThen = new HashSet<int>(model.MarkerNets);
                Dictionary<WorkbenchGraphic, int> before = layers.All.ToDictionary(g => g, g => g.Rebuilds);

                Show(model, layers, 23.1);
                int[] changed = Enumerable.Range(0, nets).Where(i => palette.Of(model.NetValue(i)) != then[i]).ToArray();
                HashSet<int> travelling = new HashSet<int>(model.MarkerNets);
                Assert.IsNotEmpty(changed, "sanity: some net changed value across 23 ns");
                Assert.IsNotEmpty(travelling, "sanity: changes are on their way at 23.1 ns");

                foreach (WorkbenchGraphic chunk in layers.All)
                {
                    bool redrawn = chunk.Rebuilds != before[chunk];
                    switch (chunk.Draws)
                    {
                        case WorkbenchGraphic.Layer.Wires:
                            bool holdsOne = changed.Any(i => i >= chunk.From && i < chunk.From + chunk.Count);
                            Assert.AreEqual(holdsOne, redrawn, $"the wires of nets {chunk.From} to {chunk.From + chunk.Count - 1}");
                            break;

                        case WorkbenchGraphic.Layer.Changes:
                            bool carries = Enumerable.Range(chunk.From, chunk.Count).Any(i => travelledThen.Contains(i) || travelling.Contains(i));
                            Assert.AreEqual(carries, redrawn, $"the changes on nets {chunk.From} to {chunk.From + chunk.Count - 1}");
                            break;

                        case WorkbenchGraphic.Layer.Parts:
                        case WorkbenchGraphic.Layer.Grid:
                        case WorkbenchGraphic.Layer.Timing:
                            Assert.IsFalse(redrawn, $"a still chunk ({chunk.Draws}) was redrawn");
                            break;
                    }
                }
            }
            finally
            {
                Object.DestroyImmediate(canvas);
            }
        }

        /// <summary>
        /// The waveform's rows are behind a mask that shows them up to the moment, further as it moves
        /// on and never past their own width; the glitch marks are not behind it.
        /// </summary>
        [Test]
        public void TheWaveform_IsRevealedAsTheMomentMovesOn()
        {
            (WorkbenchModel model, WorkbenchLayers layers, GameObject canvas) = Bench();
            try
            {
                RectTransform reveal = canvas.GetComponentsInChildren<RectTransform>(true).Single(r => r.name == "reveal");
                float wide = canvas.GetComponentsInChildren<RectTransform>(true).Single(r => r.name == "playhead").sizeDelta.x;

                Assert.IsNotNull(reveal.GetComponent<UnityEngine.UI.RectMask2D>(), "the reveal is a mask");
                foreach (WorkbenchGraphic chunk in layers.All)
                {
                    if (chunk.Draws == WorkbenchGraphic.Layer.Timing)
                        Assert.AreSame(reveal, chunk.transform.parent, "a row of the waveform is behind the reveal");
                    else if (chunk.Draws == WorkbenchGraphic.Layer.Glitches)
                        Assert.AreNotSame(reveal, chunk.transform.parent, "a glitch mark is drawn up to the moment, not revealed");
                }

                float previous = -1f;
                foreach (double t in new[] { 0.0, 15.0, 30.0, 45.0, model.End })
                {
                    Show(model, layers, t);
                    float shown = reveal.sizeDelta.x;
                    Assert.Greater(shown, previous, $"at {t} ns the waveform is shown further than before");
                    Assert.LessOrEqual(shown, wide, $"at {t} ns, no further than the waveform is wide");
                    previous = shown;
                }
            }
            finally
            {
                Object.DestroyImmediate(canvas);
            }
        }

        /// <summary>
        /// A glitch's mark on the waveform appears when the glitch is swallowed and not before -- not
        /// even the arm of its cross that reaches back before that moment.
        /// </summary>
        [Test]
        public void AGlitchMark_AppearsWhenItsGlitchIsSwallowed_AndNotBefore()
        {
            WorkbenchModel model = ReferenceCircuit.Model();
            int row = Enumerable.Range(0, model.Layout.Rows.Count).FirstOrDefault(r => model.RowSwallowedCount(r) > 0);
            Assert.Greater(model.RowSwallowedCount(row), 0, "sanity: the reference circuit swallows a glitch on a row it shows");

            var swallowed = new List<double>();
            model.RowSwallowed(row, swallowed, model.End);
            double at = swallowed.Min();

            GameObject canvas = OwnCanvas(out RectTransform waveform);
            try
            {
                var layers = new WorkbenchLayers(model);
                layers.BuildTiming(waveform, new Vector2(1000f, 40f + 30f * model.Layout.Rows.Count));
                WorkbenchGraphic marks = layers.All.Single(g => g.Draws == WorkbenchGraphic.Layer.Glitches && g.From <= row && row < g.From + g.Count);

                Show(model, layers, 0.0, all: true);
                int none = marks.VertexCount;
                Show(model, layers, at - 0.05);
                Assert.AreEqual(none, marks.VertexCount, $"just before {at} ns the glitch is still on its way");
                Show(model, layers, at);
                Assert.Greater(marks.VertexCount, none, $"at {at} ns it is swallowed, and marked");
            }
            finally
            {
                Object.DestroyImmediate(canvas);
            }
        }

        /// <summary>A lamp's chunk is redrawn when the lamp's value changes colour, and not for a moment that leaves it be.</summary>
        [Test]
        public void ALamp_IsRedrawnWhenItsValueChanges_AndOnlyThen()
        {
            var editor = new BoardEditor(Project.New("lamp"));
            int input = editor.Add("lamp", new PortPart { Name = "a", Width = 1 }, new Placement(1, 0)).Id;
            int lamp = editor.Add("lamp", new LampPart(), new Placement(4, 0)).Id;
            Assert.IsTrue(editor.Connect("lamp", Endpoint.At(input, "out"), Endpoint.At(lamp, "in")).Done);
            Assert.IsTrue(editor.SetStimulus(new BoardStimulus
            {
                Tracks = new[] { new BoardTrack { Port = "a", Steps = new[] { new Step(1, LogicVec.FromUInt64(0, 1)), new Step(5, LogicVec.FromUInt64(1, 1)) } } },
                End = 10,
            }).Done);

            WorkbenchModel model = WorkbenchModel.Of(editor.Project);
            GameObject canvas = OwnCanvas(out RectTransform schematic);
            try
            {
                var layers = new WorkbenchLayers(model);
                layers.BuildParts(schematic, model.Layout.Size.ToUnity());
                int part = model.Layout.Parts.ToList().FindIndex(p => p.Symbol == PartSymbol.Lamp);
                WorkbenchGraphic lit = layers.All.Single(g => g.Draws == WorkbenchGraphic.Layer.Flashes && g.From <= part && part < g.From + g.Count);

                Show(model, layers, 2.0, all: true);
                int drawn = lit.Rebuilds;
                Show(model, layers, 3.0);
                Assert.AreEqual(drawn, lit.Rebuilds, "a moment that leaves the lamp at 0 leaves its chunk as it was");
                Show(model, layers, 6.0);
                Assert.AreEqual(drawn + 1, lit.Rebuilds, "the lamp lit at 5 ns, and its chunk was redrawn");
            }
            finally
            {
                Object.DestroyImmediate(canvas);
            }
        }

        /// <summary>A zoom redraws what it scales -- the wires, the changes, the parts -- and nothing of the waveform, which no zoom reaches.</summary>
        [Test]
        public void AZoom_RedrawsTheSchematic_AndNoneOfTheWaveform()
        {
            (WorkbenchModel model, WorkbenchLayers layers, GameObject canvas) = Bench();
            try
            {
                Show(model, layers, 23.0, all: true);
                Dictionary<WorkbenchGraphic, int> before = layers.All.ToDictionary(g => g, g => g.Rebuilds);

                layers.RefreshSchematic();
                Canvas.ForceUpdateCanvases();

                foreach (WorkbenchGraphic chunk in layers.All)
                {
                    bool redrawn = chunk.Rebuilds != before[chunk];
                    bool schematic = chunk.Draws == WorkbenchGraphic.Layer.Wires || chunk.Draws == WorkbenchGraphic.Layer.Changes ||
                        chunk.Draws == WorkbenchGraphic.Layer.Parts || chunk.Draws == WorkbenchGraphic.Layer.Flashes;
                    Assert.AreEqual(schematic, redrawn, $"a {chunk.Draws} chunk");
                }
            }
            finally
            {
                Object.DestroyImmediate(canvas);
            }
        }

        /// <summary>A canvas of its own, and a rect on it to build under.</summary>
        private static GameObject OwnCanvas(out RectTransform under)
        {
            var canvas = new GameObject("test canvas", typeof(Canvas));
            canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            under = new GameObject("under", typeof(RectTransform)).GetComponent<RectTransform>();
            under.SetParent(canvas.transform, false);
            return canvas;
        }

        /// <summary>
        /// No chunk reaches a mesh's limit at the worst moments: everything still x and hatched, the
        /// most changes in flight, a net lit, and the closest zoom, where every circle is cut finest.
        /// </summary>
        [Test]
        public void NoChunk_OutgrowsAMesh_AtTheWorstMoments()
        {
            (WorkbenchModel model, WorkbenchLayers layers, GameObject canvas) = Bench();
            try
            {
                canvas.transform.localScale = new Vector3(SchematicView.MaxZoom, SchematicView.MaxZoom, 1f);
                layers.Highlight(0);

                foreach (double t in new[] { 0.5, 12.0, 30.0, 45.5 })
                {
                    Show(model, layers, t, all: true);
                    Assert.LessOrEqual(layers.MostVertices, WorkbenchGraphic.MeshLimit, $"at {t} ns a chunk outgrew a mesh");
                }

                UnityEngine.Debug.Log($"[WorkbenchPerformance] at {SchematicView.MaxZoom}x the largest chunk took {layers.MostVertices} vertices");
            }
            finally
            {
                Object.DestroyImmediate(canvas);
            }
        }

        /// <summary>
        /// The waveform zoomed or moved in time draws its rows, its grid and its glitch marks again for
        /// the window it now shows, and none of the schematic's wires or parts: a zoom of the waveform
        /// reaches no wire. Left where it is, the next frame draws none of them again.
        /// </summary>
        /// <remarks>
        /// At a moment inside the window. A moment before it has the rows masked off whole, and uGUI
        /// leaves a graphic its mask hides wholly undrawn until it shows -- which is right, and is not
        /// what this asks. The changes in flight and the flashing parts are not asked about: they
        /// follow the moment, and are drawn again while anything moves whatever the waveform does.
        /// </remarks>
        [Test]
        public void AMoveOfTheWaveformsWindow_RedrawsTheWaveform_AndNoneOfTheSchematic()
        {
            (WorkbenchModel model, WorkbenchLayers layers, GameObject canvas) = Bench();
            try
            {
                const double moment = 30.0;
                Show(model, layers, moment, all: true);

                var waveform = new[] { WorkbenchGraphic.Layer.Grid, WorkbenchGraphic.Layer.Timing, WorkbenchGraphic.Layer.Glitches };
                var schematic = new[] { WorkbenchGraphic.Layer.Wires, WorkbenchGraphic.Layer.Parts };

                HashSet<WorkbenchGraphic.Layer> Redrawn()
                {
                    Dictionary<WorkbenchGraphic, int> before = layers.All.ToDictionary(g => g, g => g.Rebuilds);
                    Show(model, layers, moment);
                    return new HashSet<WorkbenchGraphic.Layer>(layers.All.Where(g => g.Rebuilds != before[g]).Select(g => g.Draws));
                }

                // About the middle of the axis, which is 30 ns fitted: the moment stays in the window.
                Assert.IsTrue(layers.Wave.ZoomAbout(layers.Wave.Width * 0.5f, 4), "sanity: the waveform zooms");
                Assert.IsTrue(layers.Wave.Shows(moment), "sanity: the moment is still in view");
                HashSet<WorkbenchGraphic.Layer> zoomed = Redrawn();
                foreach (WorkbenchGraphic.Layer layer in waveform)
                    Assert.IsTrue(zoomed.Contains(layer), $"a zoom should redraw the waveform's {layer}");
                foreach (WorkbenchGraphic.Layer layer in schematic)
                    Assert.IsFalse(zoomed.Contains(layer), $"a zoom of the waveform redrew the schematic's {layer}");

                Assert.IsTrue(layers.Wave.Pan(1), "sanity: a zoomed waveform pans");
                Assert.IsTrue(layers.Wave.Shows(moment), "sanity: the moment is still in view");
                HashSet<WorkbenchGraphic.Layer> panned = Redrawn();
                foreach (WorkbenchGraphic.Layer layer in waveform)
                    Assert.IsTrue(panned.Contains(layer), $"a pan should redraw the waveform's {layer}");
                foreach (WorkbenchGraphic.Layer layer in schematic)
                    Assert.IsFalse(panned.Contains(layer), $"a pan of the waveform redrew the schematic's {layer}");

                HashSet<WorkbenchGraphic.Layer> still = Redrawn();
                foreach (WorkbenchGraphic.Layer layer in waveform.Concat(schematic))
                    Assert.IsFalse(still.Contains(layer), $"with the window left where it is, {layer} was drawn again");
            }
            finally
            {
                Object.DestroyImmediate(canvas);
            }
        }

        /// <summary>
        /// A clock far faster than the waveform's pixels -- twenty thousand edges across 860 of them --
        /// is drawn as a busy block in a few vertices, where an edge for each would be over five times
        /// what a mesh holds; and zoomed in to the most the wheel allows, the window shows its edges
        /// one by one. No zoom between outgrows a mesh.
        /// </summary>
        [Test]
        public void AClockFasterThanThePixels_IsDrawnAsABusyBlock_AndEveryZoomFitsAMesh()
        {
            var editor = new BoardEditor(Project.New("fast"));
            const string m = "fast";
            int Add(Part part, double x, double y)
            {
                EditResult added = editor.Add(m, part, new Placement(x, y));
                Assert.IsTrue(added.Done, added.Refusal);
                return added.Id;
            }

            int clk = Add(new PortPart { Name = "clk", Width = 1 }, 1, 0);
            int not = Add(new GatePart { Kind = EngineGate.Not, Inputs = 1, Rise = 1, Fall = 1 }, 3, 0);
            int y = Add(new PortPart { Name = "y", Direction = PortDirection.Output, Width = 1 }, 5, 0);
            Assert.IsTrue(editor.Connect(m, Endpoint.At(clk, "out"), Endpoint.At(not, "a")).Done);
            Assert.IsTrue(editor.Connect(m, Endpoint.At(not, "y"), Endpoint.At(y, "in")).Done);
            EditResult set = editor.SetStimulus(new BoardStimulus
            {
                Clocks = new[] { new ClockTrack { Port = "clk", Period = 2 } },
                End = 20000,
            });
            Assert.IsTrue(set.Done, set.Refusal);

            WorkbenchModel model = WorkbenchModel.Of(editor.Project);
            GameObject canvas = OwnCanvas(out RectTransform waveform);
            try
            {
                var layers = new WorkbenchLayers(model);
                layers.BuildTiming(waveform, new Vector2(1000f, 40f + 30f * model.Layout.Rows.Count));
                int rows = model.Layout.Rows.Count;
                Assert.Greater(Enumerable.Range(0, rows).Min(r => model.RowChangeCount(r)), 19000, "sanity: every row changes every nanosecond");

                Show(model, layers, model.End, all: true);
                int fitted = layers.All.Where(g => g.Draws == WorkbenchGraphic.Layer.Timing).Sum(g => g.VertexCount);
                Assert.Less(fitted, 2000, $"fitted, the fast rows should be a busy block each, not {fitted} vertices of edges");

                int most = 0;
                for (int notch = 0; notch < 60 && layers.Wave.ZoomAbout(layers.Wave.Width * 0.5f, 1); notch++)
                {
                    Show(model, layers, model.End);
                    most = Mathf.Max(most, layers.MostVertices);
                    Assert.LessOrEqual(layers.MostVertices, WorkbenchGraphic.MeshLimit, $"at {layers.Wave.PixelsPerUnit:0.###} px/ns a chunk outgrew a mesh");
                }

                Assert.AreEqual(WaveView.MaxPixelsPerUnit, layers.Wave.PixelsPerUnit, 1e-6, "sanity: zoomed in as far as the wheel goes");
                Assert.Less(layers.Wave.Shown, 2.0, "sanity: about a nanosecond and a half in view");
                int zoomed = layers.All.Where(g => g.Draws == WorkbenchGraphic.Layer.Timing).Sum(g => g.VertexCount);
                Assert.Greater(zoomed, 0, "zoomed in, the window still draws its edges");
                UnityEngine.Debug.Log($"[WorkbenchPerformance] a 20,000-edge clock: {fitted} vertices fitted, at most {most} in a chunk while zooming in");
            }
            finally
            {
                Object.DestroyImmediate(canvas);
            }
        }
    }
}
