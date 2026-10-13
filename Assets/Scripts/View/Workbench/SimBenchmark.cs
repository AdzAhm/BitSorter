using System.Collections;
using System.Collections.Generic;
using System.Text;
using BitSorter.Sim;
using BitSorter.SystemVerilog.Syntax;
using UnityEngine;

namespace BitSorter.View.Workbench
{
    /// <summary>
    /// Times the engine on the corpus processor in whatever player it is built into: read, elaborated
    /// and run to its end with everything recorded, as the workbench will run it, five times; then once
    /// more in slices of about <see cref="SliceMilliseconds"/> a frame, as the workbench will run it
    /// without holding the page. The performance gate of Part 6 (<c>Docs/architecture-5.md</c>) reads
    /// its result from a browser build.
    /// </summary>
    /// <remarks>
    /// Only the benchmark build puts it in a scene (<c>BitSorter/Benchmark</c>, <c>BenchmarkBuild</c>);
    /// the game never does. It draws nothing -- the view draws on the canvas alone -- and reports to
    /// the log, which a browser build writes to its console. Each run gives the frame back before the
    /// next, so a browser tab is never held for more than one run.
    /// </remarks>
    public sealed class SimBenchmark : MonoBehaviour
    {
        /// <summary>The slice the sliced run aims at: half a 60 Hz frame, leaving the rest for drawing.</summary>
        public const double SliceMilliseconds = 8;

        [SerializeField] private string _design = "processor/mcpu.sv";
        [SerializeField] private int _cycles;
        [SerializeField] private int _runs = 5;
        [SerializeField] private string[] _sourcePaths = new string[0];
        [SerializeField] private string[] _sourceTexts = new string[0];
        [SerializeField] private string[] _imageNames = new string[0];
        [SerializeField] private string[] _imageTexts = new string[0];

        /// <summary>What to run: the design's file and every file it reads, set by the build that makes the scene.</summary>
        public void Load(string design, int cycles, IReadOnlyList<(string Path, string Text)> sources, IReadOnlyList<(string Name, string Text)> images)
        {
            _design = design;
            _cycles = cycles;
            _sourcePaths = new string[sources.Count];
            _sourceTexts = new string[sources.Count];
            for (int i = 0; i < sources.Count; i++)
                (_sourcePaths[i], _sourceTexts[i]) = sources[i];

            _imageNames = new string[images.Count];
            _imageTexts = new string[images.Count];
            for (int i = 0; i < images.Count; i++)
                (_imageNames[i], _imageTexts[i]) = images[i];
        }

        private void Awake()
        {
            // The game's analytics start on every scene, this one included (GameAnalytics.Boot, after
            // the scene's Awakes), and a fresh browser has reporting on: opening the benchmark would
            // count a player who never played. Denied before the services start, nothing is sent.
            GameAnalytics.SetReporting(false);
        }

        private IEnumerator Start()
        {
            // The first frame drawn, so the page is up before the work starts.
            yield return null;

            var sources = new MemorySourceFiles();
            for (int i = 0; i < _sourcePaths.Length; i++)
                sources.Add(_sourcePaths[i], _sourceTexts[i]);

            var files = new MemorySimFiles();
            for (int i = 0; i < _imageNames.Length; i++)
                files.Add(_imageNames[i], _imageTexts[i]);

            var runs = new StringBuilder();
            double bestElaborate = double.MaxValue, bestSimulate = double.MaxValue, bestTotal = double.MaxValue;
            long events = 0;
            bool passed = false;
            Design design = null;

            for (int run = 1; run <= _runs; run++)
            {
                double start = Time.realtimeSinceStartupAsDouble;
                Elaboration elaboration = Elaborator.Elaborate(SyntaxReader.Read(_design, sources));
                double elaborated = Time.realtimeSinceStartupAsDouble;

                if (elaboration.Design == null)
                {
                    Debug.LogError("BitSorter benchmark: " + _design + " did not elaborate: " + string.Join("; ", elaboration.Diagnostics));
                    yield break;
                }

                design = elaboration.Design;
                var simulator = new Simulator(design, null, new SimOptions { Files = files });
                RunResult result = simulator.RunTo(long.MaxValue / 4);
                double ran = Time.realtimeSinceStartupAsDouble;

                events = result.Events;
                passed = Passed(simulator);

                double elaborate = (elaborated - start) * 1000, simulate = (ran - elaborated) * 1000;
                bestElaborate = System.Math.Min(bestElaborate, elaborate);
                bestSimulate = System.Math.Min(bestSimulate, simulate);
                bestTotal = System.Math.Min(bestTotal, elaborate + simulate);
                runs.Append($" | run {run}: read and elaborate {elaborate:F0} ms, simulate {simulate:F0} ms");

                yield return null;
            }

            // The sliced run: as many events a frame as the best run did in a slice's time. A budget
            // only pauses between time steps, so a slice can run a little over it.
            long slice = System.Math.Max(1, (long)(events / bestSimulate * SliceMilliseconds));
            var sliced = new Simulator(design, null, new SimOptions { Files = files });
            double slicedStart = Time.realtimeSinceStartupAsDouble, longest = 0;
            int slices = 0;

            for (RunStatus status = RunStatus.Paused; status == RunStatus.Paused; )
            {
                double before = Time.realtimeSinceStartupAsDouble;
                status = sliced.RunTo(long.MaxValue / 4, slice).Status;
                longest = System.Math.Max(longest, (Time.realtimeSinceStartupAsDouble - before) * 1000);
                slices++;
                yield return null;
            }

            double slicedWhole = (Time.realtimeSinceStartupAsDouble - slicedStart) * 1000;
            bool slicedPassed = Passed(sliced);

            Debug.Log($"BitSorter benchmark: {_design}, {_cycles} cycles, {events} events, {(passed && slicedPassed ? "passed" : "FAILED")}; " +
                      $"best read and elaborate {bestElaborate:F0} ms, best simulate {bestSimulate:F0} ms, best whole {bestTotal:F0} ms: " +
                      $"{_cycles / (bestSimulate / 1000):F0} cycles and {events / (bestSimulate / 1000):F0} events a second simulating; " +
                      $"sliced at {slice} events: {slices} frames, longest slice {longest:F1} ms, {slicedWhole:F0} ms in all; " +
                      $"managed heap {System.GC.GetTotalMemory(false) / (1024.0 * 1024.0):F1} MB" + runs);
        }

        private static bool Passed(Simulator simulator)
        {
            foreach (SimMessage message in simulator.History.Messages)
                if (message.Text.StartsWith("PASS", System.StringComparison.Ordinal))
                    return true;
            return false;
        }
    }
}
