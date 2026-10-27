using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using BitSorter.Board;
using BitSorter.Sim;
using BitSorter.SystemVerilog;
using BitSorter.SystemVerilog.Syntax;

namespace BitSorter.ViewModel
{
    /// <summary>
    /// The slowest one output bit was seen to change after one input bit stepped (Part 14c): rising and falling apart, and the
    /// inputs as they stood before the step that showed the slowest.
    /// </summary>
    public sealed class MeasuredDelay
    {
        internal MeasuredDelay(string input, string output, long rising, long falling, string when, bool worstRising)
        {
            Input = input;
            Output = output;
            Rising = rising;
            Falling = falling;
            When = when;
            WorstRising = worstRising;
        }

        /// <summary>The input bit, named from the design's top: <c>a</c>, <c>a[2]</c>.</summary>
        public string Input { get; }

        /// <summary>The output bit.</summary>
        public string Output { get; }

        /// <summary>The latest the output changed after the input rose; <see cref="PathDelays.Never"/> when no rise moved it.</summary>
        public long Rising { get; }

        /// <summary>The same, after it fell.</summary>
        public long Falling { get; }

        public long Worst => Math.Max(Rising, Falling);

        /// <summary>Whether the slowest came from the input rising.</summary>
        public bool WorstRising { get; }

        /// <summary>Every input as it stood just before the slowest step: <c>a = 0, b = 1, cin = 0</c>.</summary>
        public string When { get; }

        public override string ToString() => Input + " -> " + Output + " " + Rising + "/" + Falling + " (" + When + ")";
    }

    /// <summary>What a measuring run saw (Part 14c): for every input bit and output bit one stepping moved, its slowest, the slowest first.</summary>
    public sealed class MeasuredTiming
    {
        private readonly Dictionary<(string, string), MeasuredDelay> _by;

        internal MeasuredTiming(List<MeasuredDelay> delays, int steps)
        {
            delays.Sort((a, b) =>
            {
                int byWorst = b.Worst.CompareTo(a.Worst);
                if (byWorst != 0)
                    return byWorst;
                int byInput = string.CompareOrdinal(a.Input, b.Input);
                return byInput != 0 ? byInput : string.CompareOrdinal(a.Output, b.Output);
            });
            Delays = delays;
            Steps = steps;
            _by = new Dictionary<(string, string), MeasuredDelay>();
            foreach (MeasuredDelay delay in delays)
                _by[(delay.Input, delay.Output)] = delay;
        }

        public IReadOnlyList<MeasuredDelay> Delays { get; }

        /// <summary>How many steps were taken: each input bit up and down from every value of the rest.</summary>
        public int Steps { get; }

        /// <summary>The slowest seen from <paramref name="input"/> to <paramref name="output"/>; null when no step of it moved the output.</summary>
        public MeasuredDelay For(string input, string output) => _by.TryGetValue((input, output), out MeasuredDelay d) ? d : null;
    }

    /// <summary>
    /// The measured worst (Part 14c): a document's inputs stepped up and down from every value of the rest
    /// (<see cref="StepTests.Exhaustive"/>), on a copy whose stimulus is that and nothing else, run a slice a frame as a run
    /// is (<see cref="RunSlicer"/>), and the waves read: after each step, the last change of each output before the next.
    /// </summary>
    /// <remarks>
    /// **What static timing cannot know.** A computed path is the gates in a row; whether any values of the other inputs let
    /// a change through all of them is a question only trying them answers, and a path longer than anything measured from
    /// its input to its output is one no step ever fires (<see cref="TimingView.With"/>). A glitch counts: a change that
    /// comes and goes is a change, as it is on the waves.
    ///
    /// **Only a circuit without registers, of at most <see cref="StepTests.MostInputBits"/> input bits**: one with registers
    /// is timed by its clock, and more bits than that would take longer than a moment to step through. **And only where the
    /// computed slowest is a bound** -- no loop of logic, no delay the run works out -- because each step is left to settle
    /// for longer than it, and a change later than that would be taken for the next step's. A change seen later all the
    /// same, or a run that ends short, says so (<see cref="Problem"/>) rather than giving a measure that may be wrong.
    /// </remarks>
    public sealed class StepMeasure
    {
        private readonly StepPlan _plan;
        private readonly long _slowest;
        private readonly string[] _inputs;
        private readonly List<(string Name, SignalId Signal, int Bit)> _outputs;

        // The run, let go once it is read: its history is the largest thing the workbench would otherwise keep.
        private SimSession _session;
        private RunSlicer _slicer;

        private StepMeasure(Project project, StepPlan plan, long slowest, SimSession session, RunSlicer slicer, string[] inputs,
            List<(string, SignalId, int)> outputs)
        {
            Project = project;
            _plan = plan;
            _slowest = slowest;
            _session = session;
            _slicer = slicer;
            _inputs = inputs;
            _outputs = outputs;
        }

        /// <summary>The document measured.</summary>
        public Project Project { get; }

        /// <summary>Whether the run is made and read, or found it could not be.</summary>
        public bool Done => Result != null || Problem != null;

        /// <summary>How much of the run is made, 0 to 1.</summary>
        public double Progress => _slicer == null || _slicer.Horizon == 0 ? 1 : Math.Min(1.0, (double)_session.Reached / _slicer.Horizon);

        /// <summary>What it saw, once done; null until then, and when it could not be measured.</summary>
        public MeasuredTiming Result { get; private set; }

        /// <summary>Why, once done, nothing was measured after all: the run ended short, or saw a change it could not place.</summary>
        public string Problem { get; private set; }

        /// <summary>Milliseconds of a frame a slice of the measuring run may take: half a run's, since the run shown is made beside it.</summary>
        public const double BudgetMilliseconds = RunSlicer.BudgetMilliseconds / 2;

        /// <summary>
        /// A measuring run of <paramref name="project"/>, its first slice made, settling each step for longer than
        /// <paramref name="timing"/>'s slowest path -- or null, and why in <paramref name="why"/> (null when the timing already
        /// says it, as it does a circuit with errors).
        /// </summary>
        public static StepMeasure Start(Project project, TimingView timing, Func<double> nowMilliseconds, out string why,
            double budgetMilliseconds = BudgetMilliseconds, long? maxRecorded = null)
        {
            why = null;
            TimingAnalysis analysis = timing?.Analysis;
            if (project == null || analysis == null || !analysis.Succeeded)
                return null;
            if (analysis.Graph.Registers.Count > 0)
            {
                why = "The measured worst is for logic without registers: a circuit with them is timed by its clock.";
                return null;
            }
            if (analysis.Graph.Loops.Count > 0)
            {
                why = "Not measured: a loop of logic need never settle, so no step can be left long enough.";
                return null;
            }
            if (analysis.Graph.UnknownDelays)
            {
                why = "Not measured: a delay here is worked out as the circuit runs, so no settle time is known to be long enough.";
                return null;
            }

            IReadOnlyList<InputBit> bits = StepTests.InputsOf(project);
            if (bits.Count == 0 || analysis.Graph.Outputs.Count == 0)
            {
                why = "Nothing to measure: the circuit needs an input to step and an output to watch.";
                return null;
            }
            if (bits.Count > StepTests.MostInputBits)
            {
                why = "Not measured: stepping " + bits.Count.ToString(CultureInfo.InvariantCulture) + " input bits from every value of the rest would take too long; "
                    + StepTests.MostInputBits.ToString(CultureInfo.InvariantCulture) + " is the most.";
                return null;
            }

            StepPlan plan = StepTests.Exhaustive(project, StepTests.SettleFor(timing.Slowest));
            Project copy = project with { Stimulus = plan.Stimulus, Bench = null };
            CompiledProject compiled = BoardCompiler.Compile(copy);
            if (!compiled.Succeeded)
                return null;
            Elaboration elaboration = Elaborator.Elaborate(compiled.Tree, compiled.Top);
            if (elaboration.HasErrors)
                return null;
            Design design = elaboration.Design;

            // Each bit named as the timing names it, from the design's top, its range's index and all.
            string Named(string port, int bit) =>
                design.TryFind(compiled.DesignPath + "." + port, out SignalId id)
                    ? TimingView.Relative(TimingGraph.BitName(design.Signals[id.Index], bit), compiled.DesignPath)
                    : port;

            var inputs = new string[plan.Inputs.Count];
            for (int i = 0; i < inputs.Length; i++)
                inputs[i] = Named(plan.Inputs[i].Port, plan.Inputs[i].Bit);

            var outputs = new List<(string, SignalId, int)>();
            foreach ((string port, int width) in OutputsOf(project))
                if (design.TryFind(compiled.DesignPath + "." + port, out SignalId signal))
                    for (int b = 0; b < width; b++)
                        outputs.Add((Named(port, b), signal, b));

            // Nothing drawn from it, so no transitions in flight are kept: only the settled changes are read.
            var options = new SimOptions { Files = ProjectFiles.ForRun(copy), RecordTransitions = false };
            if (maxRecorded.HasValue)
                options.MaxRecorded = maxRecorded.Value;
            var session = new SimSession(design, null, options);
            var slicer = new RunSlicer(session, plan.Stimulus.StopTime, nowMilliseconds, budgetMilliseconds: budgetMilliseconds);
            var measure = new StepMeasure(project, plan, timing.Slowest, session, slicer, inputs, outputs);
            measure.Frame();
            return measure;
        }

        /// <summary>One frame's share of the run, and the waves read once it is made. False once it is done.</summary>
        public bool Frame()
        {
            if (Done)
                return false;
            _slicer.Slice();
            if (_slicer.Done)
                Read();
            return true;
        }

        /// <summary>The rest of the run now, and the waves read: what a test does.</summary>
        public void Finish()
        {
            if (Done)
                return;
            _slicer.Finish();
            Read();
        }

        private static List<(string Port, int Width)> OutputsOf(Project project)
        {
            var outputs = new List<(string, int)>();
            Module top = project.TopModule;
            if (top.IsWritten)
            {
                foreach (ModulePort port in WrittenModules.PortsOf(project, top))
                    if (port.Direction == PortDirection.Output)
                        outputs.Add((port.Name, port.Width));
                return outputs;
            }

            foreach (PortPart port in top.Ports)
                if (port.Direction == PortDirection.Output)
                    outputs.Add((port.Name, port.Width));
            return outputs;
        }

        /// <summary>
        /// After each step, each output's last change before the step after it: the slowest kept for each input and output --
        /// or, when the run ended short or a change came later than the slowest path allows, why nothing is. The run let go.
        /// </summary>
        private void Read()
        {
            try
            {
                Problem = Short();
                if (Problem == null)
                    Measure();
            }
            finally
            {
                _session = null;
                _slicer = null;
            }
        }

        /// <summary>
        /// Why the run is no measure, when it ended before its end: it had to stop, or the circuit ended it first. The step
        /// test's own testbench ends it at its end, which is the run made.
        /// </summary>
        private string Short()
        {
            if (_slicer.Ended != RunStatus.Stopped && _session.Reached >= _slicer.Horizon)
                return null;
            string unit = Project.Settings?.TimeUnit ?? TimeUnits.Default;
            string reason = _slicer.Ended == RunStatus.Stopped
                ? "it grew too large to keep, or a loop never settled"
                : "the circuit ended it";
            return "Not measured: the measuring run stopped at " + _session.Reached.ToString(CultureInfo.InvariantCulture) + " of "
                + _slicer.Horizon.ToString(CultureInfo.InvariantCulture) + " " + unit + ", because " + reason + ".";
        }

        private void Measure()
        {
            var rising = new Dictionary<(int, int), (long Delay, ulong Word)>();
            var falling = new Dictionary<(int, int), (long Delay, ulong Word)>();
            IReadOnlyList<StepWindow> windows = _plan.Windows;
            long settle = _plan.Settle;

            for (int o = 0; o < _outputs.Count; o++)
            {
                // The times the bit changed, from the run's settled changes of its signal.
                var times = new List<long>();
                Logic4? was = null;
                foreach (ValueChange change in _session.History.SettledChanges(_outputs[o].Signal))
                {
                    Logic4 now = _outputs[o].Bit < change.Value.Width ? change.Value[_outputs[o].Bit] : Logic4.X;
                    if (was.HasValue && now != was.Value)
                        times.Add(change.Time);
                    was = now;
                }

                int p = 0;
                foreach (StepWindow w in windows)
                {
                    while (p < times.Count && times[p] < w.At)
                        p++;
                    long last = -1;
                    while (p < times.Count && times[p] < w.At + settle)
                        last = times[p++] - w.At;
                    if (last < 0)
                        continue;

                    // Later than the slowest path is later than the settle time was worked out for: the next step's change
                    // could have been this one's, so no measure is given at all.
                    if (last > _slowest)
                    {
                        string unit = Project.Settings?.TimeUnit ?? TimeUnits.Default;
                        Problem = "Not measured: " + _outputs[o].Name + " changed " + last.ToString(CultureInfo.InvariantCulture) + " " + unit
                            + " after a step of " + _inputs[w.Input] + ", later than the slowest path reckoned, "
                            + _slowest.ToString(CultureInfo.InvariantCulture) + ".";
                        return;
                    }

                    Dictionary<(int, int), (long Delay, ulong Word)> into = w.Rising ? rising : falling;
                    var key = (w.Input, o);
                    if (!into.TryGetValue(key, out (long Delay, ulong Word) best) || last > best.Delay)
                        into[key] = (last, w.Word);
                }
            }

            var delays = new List<MeasuredDelay>();
            var pairs = new HashSet<(int, int)>(rising.Keys);
            pairs.UnionWith(falling.Keys);
            foreach ((int input, int output) in pairs)
            {
                bool rose = rising.TryGetValue((input, output), out (long Delay, ulong Word) up);
                bool fell = falling.TryGetValue((input, output), out (long Delay, ulong Word) down);
                bool worstRising = rose && (!fell || up.Delay >= down.Delay);
                ulong word = worstRising ? up.Word : down.Word;
                delays.Add(new MeasuredDelay(_inputs[input], _outputs[output].Name, rose ? up.Delay : PathDelays.Never, fell ? down.Delay : PathDelays.Never,
                    When(word), worstRising));
            }

            Result = new MeasuredTiming(delays, windows.Count);
        }

        /// <summary>Every input as <paramref name="word"/> has it, a port at a time: <c>a = 0, b = 1, sel = 2'b01</c>.</summary>
        private string When(ulong word)
        {
            var text = new StringBuilder();
            IReadOnlyList<InputBit> bits = _plan.Inputs;
            for (int k = 0; k < bits.Count; k++)
            {
                if (k > 0 && bits[k].Port == bits[k - 1].Port)
                    continue;
                ulong value = 0;
                for (int j = k; j < bits.Count && bits[j].Port == bits[k].Port; j++)
                    if ((word >> j & 1) != 0)
                        value |= 1UL << bits[j].Bit;
                if (text.Length > 0)
                    text.Append(", ");
                text.Append(bits[k].Port).Append(" = ");
                text.Append(bits[k].Width == 1
                    ? value.ToString(CultureInfo.InvariantCulture)
                    : LogicVec.FromUInt64(value, bits[k].Width).ToLiteral(Radix.Binary));
            }
            return text.ToString();
        }
    }
}
