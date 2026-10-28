using System;
using System.Collections.Generic;
using System.Globalization;
using BitSorter.Board;
using BitSorter.Sim;
using BitSorter.SystemVerilog;
using BitSorter.SystemVerilog.Syntax;
using BoardStimulus = BitSorter.Board.Stimulus;

namespace BitSorter.ViewModel
{
    /// <summary>One arrow of a state diagram: from a state, on an input, to a state, with what the outputs read before the edge.</summary>
    public readonly struct StateTransition
    {
        public StateTransition(int from, int to, string input, string output)
        {
            From = from;
            To = to;
            Input = input;
            Output = output;
        }

        public int From { get; }
        public int To { get; }

        /// <summary>The inputs, a character a bit, in <see cref="StateMachine.InputBits"/>' order.</summary>
        public string Input { get; }

        /// <summary>The outputs just before the edge, a character a bit, in <see cref="StateMachine.OutputBits"/>' order.</summary>
        public string Output { get; }
    }

    /// <summary>A circuit's states as running it found them: each a code of its register bits, and every arrow between them.</summary>
    public sealed class StateMachine
    {
        internal StateMachine(IReadOnlyList<string> stateBits, IReadOnlyList<string> inputBits, IReadOnlyList<string> outputBits,
            IReadOnlyList<string> states, IReadOnlyList<StateTransition> transitions, bool cut)
        {
            StateBits = stateBits;
            InputBits = inputBits;
            OutputBits = outputBits;
            States = states;
            Transitions = transitions;
            Cut = cut;

            // Moore: every output a function of the state alone -- the same on every arrow out of each state.
            bool moore = true;
            foreach (StateTransition t in transitions)
                foreach (StateTransition u in transitions)
                    if (t.From == u.From && t.Output != u.Output)
                        moore = false;
            Moore = moore;
        }

        /// <summary>The register bits a state's code is made of, the first the leftmost.</summary>
        public IReadOnlyList<string> StateBits { get; }

        /// <summary>The input bits an arrow's input is written in -- the clock and a reset left out.</summary>
        public IReadOnlyList<string> InputBits { get; }

        public IReadOnlyList<string> OutputBits { get; }

        /// <summary>Each state's code -- '0', '1' or 'x' a register bit -- in the order running reached them, the start first.</summary>
        public IReadOnlyList<string> States { get; }

        public IReadOnlyList<StateTransition> Transitions { get; }

        /// <summary>Whether every output depends on the state alone: a Moore machine, its outputs written in its states.</summary>
        public bool Moore { get; }

        /// <summary>Whether more states were reachable than <see cref="StateExplorer.MostStates"/>, and the rest not explored.</summary>
        public bool Cut { get; }

        /// <summary>What the outputs read in <paramref name="state"/>, for a Moore machine: the output of any arrow out of it; null for none.</summary>
        public string OutputIn(int state)
        {
            foreach (StateTransition t in Transitions)
                if (t.From == state)
                    return t.Output;
            return null;
        }
    }

    /// <summary>
    /// The STATES tab's state diagram (Part 15g): a clocked circuit's states and every way between them, found by running the
    /// real engine -- for a drawn circuit or a written one alike. Ahmad asked for a state diagram (2026-10-28).
    /// </summary>
    /// <remarks>
    /// **Breadth first from the start**: the start is the state after its reset, held for the first clock, or with no reset
    /// the registers as they start. Each state reached is run again from the start with every combination of the inputs for
    /// one more clock -- the inputs that first reached it, then the combination -- and the registers read after the edge are
    /// the next state; the outputs are read just before it. **One short run each**, on a copy of the document with a
    /// stimulus of its own, as many a frame as fit a few milliseconds.
    ///
    /// **A machine small enough to draw**: at most <see cref="MostRegisterBits"/> register bits, <see cref="MostInputBits"/>
    /// input bits besides the clock and a reset, and <see cref="MostStates"/> states reached; past that the rest are not
    /// explored, and the machine says it was cut. **A state with an x in it is run on from like any other**: it is what a
    /// register holds from power-up until something is written to it, and the next state may well be known.
    ///
    /// **The clock** is an input called clk or clock, or else the stimulus's clock; **a reset** is another input called rst
    /// or reset, held for the first clock and inactive after it. It is taken as active low when its name says so the usual
    /// ways (<c>rst_n</c>, <c>resetn</c>, <c>nrst</c>, <c>rst_b</c>, <c>reset_l</c>); and since a name is only a guess, a
    /// machine that comes out as one state, or starting on an x, is explored again with the reset the other way round, and
    /// the fuller of the two kept (the 15g-6 review found rst_btn taken as active low, and the machine held in reset).
    /// </remarks>
    public sealed class StateExplorer
    {
        public const int MostRegisterBits = 6, MostInputBits = 4, MostStates = 16;

        /// <summary>Milliseconds of a frame the runs may take.</summary>
        public const double BudgetMilliseconds = 4;

        private readonly Project _project;
        private readonly string _clock, _reset;
        private bool _resetLow, _flipped;
        private (List<string> States, List<int[]> Paths, List<StateTransition> Transitions, bool Cut)? _firstWay;
        private double _progress;
        private readonly long _period;
        private readonly List<InputBit> _inputs;
        private readonly List<string> _inputNames = new List<string>();
        private readonly List<(string Name, string Port, int Bit)> _outputs;
        private readonly List<(string Name, string Signal, int Bit)> _registers;
        private readonly Func<double> _now;
        private readonly Queue<(int From, int[] Path, int Combination)> _work = new Queue<(int, int[], int)>();
        private readonly List<string> _states = new List<string>();
        private readonly List<int[]> _paths = new List<int[]>();
        private readonly List<StateTransition> _transitions = new List<StateTransition>();
        private bool _cut;
        private int _runs;

        private StateExplorer(Project project, string clock, string reset, bool resetLow, long period, List<InputBit> inputs,
            List<(string, string, int)> outputs, List<(string, string, int)> registers, Func<double> now)
        {
            _project = project;
            _clock = clock;
            _reset = reset;
            _resetLow = resetLow;
            _period = period;
            _inputs = inputs;
            _outputs = outputs;
            _registers = registers;
            _now = now;
            for (int c = 0; c < 1 << inputs.Count; c++)
                _work.Enqueue((0, Array.Empty<int>(), c));
        }

        /// <summary>The machine once every reachable state is explored; null until then, and when it could not be.</summary>
        public StateMachine Machine { get; private set; }

        /// <summary>Why, once done, there is no machine after all: a run would not build, or ended short.</summary>
        public string Problem { get; private set; }

        public bool Done => Machine != null || Problem != null;

        /// <summary>How far it has got, 0 to 1: the runs made of those known to be needed so far.</summary>
        /// <remarks>Never back down as more states turn up to be run, and short of 1 until the machine is made.</remarks>
        public double Progress => Done ? 1 : _progress;

        /// <summary>
        /// The exploration of <paramref name="project"/>'s states, nothing run yet -- or null, and why in <paramref name="why"/>:
        /// no registers, no clock, or a machine too big to draw.
        /// </summary>
        public static StateExplorer Start(Project project, TimingView timing, Func<double> nowMilliseconds, out string why)
        {
            why = null;
            TimingAnalysis analysis = timing?.Analysis;
            if (project == null || analysis == null || !analysis.Succeeded)
            {
                why = "No state diagram: the circuit has to build first.";
                return null;
            }
            if (analysis.Graph.Registers.Count == 0)
            {
                why = "The state diagram is for circuits with registers: one without has no states, and LOGIC tables it.";
                return null;
            }

            // The register bits, counted from the timing's own graph before anything is compiled: a design too big to draw is
            // told so at once (the 15g-6 review). A testbench's processes could be counted with them, so only without one.
            if (project.Bench == null)
            {
                var launched = new HashSet<int>();
                foreach (TimingRegister register in analysis.Graph.Registers)
                    foreach (int node in register.Launches)
                        launched.Add(node);
                if (launched.Count > MostRegisterBits)
                {
                    why = TooManyRegisterBits(launched.Count);
                    return null;
                }
            }

            // The clock: an input called clk or clock, which is what the registers' edges are written against, or else the
            // stimulus's -- an input clocked in INPUTS may be some other toggling input (the 15g-6 review). A reset: another
            // input called rst or reset.
            IReadOnlyList<InputBit> all = StepTests.InputsOf(project);
            string clock = null;
            foreach (InputBit bit in all)
                if (clock == null && bit.Width == 1 && (bit.Port.Equals("clk", StringComparison.OrdinalIgnoreCase) || bit.Port.Equals("clock", StringComparison.OrdinalIgnoreCase)))
                    clock = bit.Port;
            foreach (ClockTrack each in project.Stimulus?.Clocks ?? Array.Empty<ClockTrack>())
                clock ??= each.Port;
            string reset = null;
            foreach (InputBit bit in all)
            {
                string name = bit.Port.ToLowerInvariant();
                if (reset == null && bit.Width == 1 && bit.Port != clock && (name.StartsWith("rst", StringComparison.Ordinal)
                    || name.StartsWith("reset", StringComparison.Ordinal) || name == "nrst" || name == "nreset"))
                    reset = bit.Port;
            }
            if (clock == null)
            {
                why = "No state diagram: nothing clocks the registers. Make an input a clock in INPUTS, or call it clk.";
                return null;
            }
            bool resetLow = reset != null && ActiveLow(reset);

            var inputs = new List<InputBit>();
            string lastPort = null;
            var ports = new List<(string Port, int Width)>();
            foreach (InputBit bit in all)
                if (bit.Port != clock && bit.Port != reset && bit.Port != lastPort)
                {
                    lastPort = bit.Port;
                    ports.Add((bit.Port, bit.Width));
                }
            foreach ((string port, int width) in ports)
                for (int b = width - 1; b >= 0; b--)
                    inputs.Add(new InputBit(port, width, b));
            if (inputs.Count > MostInputBits)
            {
                why = "No state diagram: " + inputs.Count.ToString(CultureInfo.InvariantCulture) + " input bits besides the clock are "
                    + LogicTable.PowerOfTwo(inputs.Count) + " arrows out of every state; "
                    + MostInputBits.ToString(CultureInfo.InvariantCulture) + " is the most drawn.";
                return null;
            }

            var outputs = new List<(string, string, int)>();
            foreach (ModulePort port in WrittenModules.PortsOf(project, project.TopModule))
                if (port.Direction == PortDirection.Output)
                    for (int b = port.Width - 1; b >= 0; b--)
                        outputs.Add((new InputBit(port.Name, port.Width, b).Name, port.Name, b));

            // The clock slow enough for everything to settle in each half of it, and no faster than the design asks.
            long half = StepTests.SettleFor(timing.Slowest);
            long period = Math.Max(2 * half, analysis.Timing?.ClockPeriod ?? 0);
            period += period % 2;

            // The register bits, named in a copy compiled as each run will be: the timing's own reckoning of what each writes.
            var explorer = new StateExplorer(project, clock, reset, resetLow, period, inputs, outputs, new List<(string, string, int)>(), nowMilliseconds);
            if (!explorer.TryCompile(Array.Empty<int>(), 0, out Design design, out string designPath, out why))
                return null;
            // Each input and output bit named as the timing names it, its range's index and all: an input [7:6] a's bits are
            // a[7] and a[6], never a[1] and a[0] (the 15g-5 review, of LOGIC's names).
            string Named(string port, int bit, string otherwise) =>
                design.TryFind(designPath + "." + port, out SignalId id)
                    ? TimingView.Relative(TimingGraph.BitName(design.Signals[id.Index], bit), designPath)
                    : otherwise;
            foreach (InputBit bit in inputs)
                explorer._inputNames.Add(Named(bit.Port, bit.Bit, bit.Name));
            for (int i = 0; i < outputs.Count; i++)
                outputs[i] = (Named(outputs[i].Item2, outputs[i].Item3, outputs[i].Item1), outputs[i].Item2, outputs[i].Item3);

            TimingGraph graph = TimingGraph.Of(design);
            var seen = new HashSet<(int, int)>();
            foreach (TimingRegister register in graph.Registers)
                foreach (int node in register.Launches)
                {
                    int signal = graph.SignalOf(node);
                    if (signal < 0)
                        continue;
                    int bit = node - graph.NodeOf(signal);
                    if (!seen.Add((signal, bit)))
                        continue;
                    string full = design.Signals[signal].Name;
                    explorer._registers.Add((TimingView.Relative(TimingGraph.BitName(design.Signals[signal], bit), designPath), full, bit));
                }

            // A state reads as its buses do, high bit first: launches come bit 0 first. The registers keep the order met in.
            var met = new Dictionary<string, int>();
            foreach ((string _, string full, int _) in explorer._registers)
                if (!met.ContainsKey(full))
                    met[full] = met.Count;
            explorer._registers.Sort((a, b) => met[a.Signal] != met[b.Signal] ? met[a.Signal].CompareTo(met[b.Signal]) : b.Bit.CompareTo(a.Bit));
            if (explorer._registers.Count > MostRegisterBits)
            {
                why = TooManyRegisterBits(explorer._registers.Count);
                return null;
            }

            return explorer;
        }

        private static string TooManyRegisterBits(int bits) =>
            "No state diagram: " + bits.ToString(CultureInfo.InvariantCulture) + " register bits are up to "
            + LogicTable.PowerOfTwo(bits) + " states; "
            + MostRegisterBits.ToString(CultureInfo.InvariantCulture) + " bits is the most drawn.";

        /// <summary>Whether a reset's name says it is active low, the usual ways: rst_n, resetn, nrst, rst_b, reset_l, rst_bar.</summary>
        private static bool ActiveLow(string name)
        {
            string n = name.ToLowerInvariant();
            return n.EndsWith("_n", StringComparison.Ordinal) || n.EndsWith("_b", StringComparison.Ordinal) || n.EndsWith("_l", StringComparison.Ordinal)
                || n.EndsWith("_bar", StringComparison.Ordinal) || n.EndsWith("_ni", StringComparison.Ordinal)
                || n == "rstn" || n == "resetn" || n == "rstb" || n == "resetb" || n == "nrst" || n == "nreset";
        }

        /// <summary>A machine that says little: one state, or a start with an x in it -- as one held in its reset comes out.</summary>
        private static bool SaysLittle(List<string> states) => states.Count <= 1 || states[0].IndexOf('x') >= 0;

        /// <summary>Whether <paramref name="a"/> says more than <paramref name="b"/>: more states, or as many and a start with no x in it.</summary>
        private static bool Fuller(List<string> a, List<string> b) =>
            a.Count > b.Count || a.Count == b.Count && b[0].IndexOf('x') >= 0 && a[0].IndexOf('x') < 0;

        /// <summary>As many runs as fit the frame's budget. False once done.</summary>
        public bool Frame(double budgetMilliseconds = BudgetMilliseconds)
        {
            if (Done)
                return false;
            double start = _now();
            do
                Step();
            while (!Done && _now() - start < budgetMilliseconds);
            return true;
        }

        /// <summary>Every run left, now: what a test does.</summary>
        public void Finish()
        {
            while (!Done)
                Step();
        }

        /// <summary>One run: a state and a combination of the inputs, one clock on.</summary>
        private void Step()
        {
            if (_work.Count == 0)
            {
                // The reset's way round is a guess from its name: a machine that says little is explored again the other way,
                // and the fuller of the two kept -- the more states, then a start with no x in it.
                if (_reset != null && !_flipped && SaysLittle(_states))
                {
                    _firstWay = (new List<string>(_states), new List<int[]>(_paths), new List<StateTransition>(_transitions), _cut);
                    _states.Clear();
                    _paths.Clear();
                    _transitions.Clear();
                    _cut = false;
                    _resetLow = !_resetLow;
                    _flipped = true;
                    for (int c = 0; c < 1 << _inputs.Count; c++)
                        _work.Enqueue((0, Array.Empty<int>(), c));
                    return;
                }
                if (_firstWay.HasValue && Fuller(_firstWay.Value.States, _states))
                {
                    (List<string> states, List<int[]> paths, List<StateTransition> transitions, bool cut) = _firstWay.Value;
                    _states.Clear();
                    _states.AddRange(states);
                    _paths.Clear();
                    _paths.AddRange(paths);
                    _transitions.Clear();
                    _transitions.AddRange(transitions);
                    _cut = cut;
                    _resetLow = !_resetLow;
                }

                var stateBits = new List<string>();
                foreach ((string name, string _, int _) in _registers)
                    stateBits.Add(name);
                var inputBits = new List<string>(_inputNames);
                var outputBits = new List<string>();
                foreach ((string name, string _, int _) in _outputs)
                    outputBits.Add(name);
                Machine = new StateMachine(stateBits, inputBits, outputBits, _states, _transitions, _cut);
                return;
            }

            (int from, int[] path, int combination) = _work.Dequeue();
            _runs++;
            _progress = Math.Max(_progress, Math.Min(0.99, (double)_runs / (_runs + _work.Count)));
            if (!TryCompile(path, combination, out Design design, out string designPath, out string why))
            {
                Problem = why;
                return;
            }

            var session = new SimSession(design, null, new SimOptions { Files = ProjectFiles.ForRun(_project), RecordTransitions = false });
            long cycles = Offset + path.Length + 1;
            var slicer = new RunSlicer(session, cycles * _period, _now);
            slicer.Finish();
            if (slicer.Ended == RunStatus.Stopped || session.Reached < cycles * _period - 1)
            {
                Problem = "No state diagram: a run stopped at " + session.Reached.ToString(CultureInfo.InvariantCulture) + " of "
                    + (cycles * _period).ToString(CultureInfo.InvariantCulture) + ".";
                return;
            }

            if (_states.Count == 0)
            {
                // The start: after the reset's clock, or the registers as they start, before the first edge.
                _states.Add(Read(session, design, _registers, Offset == 1 ? _period - 1 : _period / 2 - 1));
                _paths.Add(Array.Empty<int>());
            }

            long edge = (Offset + path.Length) * _period + _period / 2;
            string output = Read(session, design, Outputs(designPath), edge - 1);
            string next = Read(session, design, _registers, edge + _period / 2 - 1);

            int to = _states.IndexOf(next);
            if (to < 0)
            {
                if (_states.Count >= MostStates)
                {
                    _cut = true;
                    return;
                }
                to = _states.Count;
                _states.Add(next);
                var reached = new int[path.Length + 1];
                Array.Copy(path, reached, path.Length);
                reached[path.Length] = combination;
                _paths.Add(reached);
                for (int c = 0; c < 1 << _inputs.Count; c++)
                    _work.Enqueue((to, reached, c));
            }

            _transitions.Add(new StateTransition(from, to, Code(combination, _inputs.Count), output));
        }

        /// <summary>The first clock a reset's, when there is one.</summary>
        private int Offset => _reset != null ? 1 : 0;

        private List<(string, string, int)> Outputs(string designPath)
        {
            var named = new List<(string, string, int)>(_outputs.Count);
            foreach ((string name, string port, int bit) in _outputs)
                named.Add((name, designPath + "." + port, bit));
            return named;
        }

        private static string Code(int value, int bits)
        {
            var text = new char[bits];
            for (int i = 0; i < bits; i++)
                text[i] = (value >> (bits - 1 - i) & 1) != 0 ? '1' : '0';
            return new string(text);
        }

        /// <summary>The bits named, each '0', '1' or 'x', at <paramref name="time"/>.</summary>
        private static string Read(SimSession session, Design design, List<(string Name, string Signal, int Bit)> bits, long time)
        {
            var text = new char[bits.Count];
            for (int i = 0; i < bits.Count; i++)
            {
                char value = 'x';
                if (design.TryFind(bits[i].Signal, out SignalId signal))
                {
                    LogicVec read = session.ValueAt(signal, time);
                    Logic4 bit = bits[i].Bit < read.Width ? read[bits[i].Bit] : Logic4.X;
                    value = bit == Logic4.Zero ? '0' : bit == Logic4.One ? '1' : 'x';
                }
                text[i] = value;
            }
            return new string(text);
        }

        /// <summary>
        /// A copy of the document whose stimulus runs a reset's clock if there is one, then <paramref name="path"/>'s inputs a clock
        /// each, then <paramref name="combination"/>'s for one more -- each set at a clock's fall, half a clock before its rise.
        /// </summary>
        private bool TryCompile(int[] path, int combination, out Design design, out string designPath, out string why)
        {
            design = null;
            designPath = null;
            why = null;
            long cycles = Offset + path.Length + 1;

            var tracks = new List<Track>();
            if (_reset != null)
                tracks.Add(new Track
                {
                    Port = _reset,
                    Steps = new[]
                    {
                        new Step(0, LogicVec.FromUInt64(_resetLow ? 0UL : 1UL, 1)),
                        new Step(_period, LogicVec.FromUInt64(_resetLow ? 1UL : 0UL, 1)),
                    },
                });

            // Each input port's value a clock: 0 through the reset's, then the path's, then the combination's.
            string lastPort = null;
            foreach (InputBit first in _inputs)
            {
                if (first.Port == lastPort)
                    continue;
                lastPort = first.Port;
                var steps = new List<Step> { new Step(0, LogicVec.FromUInt64(0, first.Width)) };
                for (int j = 0; j <= path.Length; j++)
                {
                    int word = j < path.Length ? path[j] : combination;
                    ulong value = 0;
                    for (int i = 0; i < _inputs.Count; i++)
                        if (_inputs[i].Port == first.Port && (word >> (_inputs.Count - 1 - i) & 1) != 0)
                            value |= 1UL << _inputs[i].Bit;
                    steps.Add(new Step((Offset + j) * _period, LogicVec.FromUInt64(value, first.Width)));
                }
                tracks.Add(new Track { Port = first.Port, Steps = steps });
            }

            var stimulus = new BoardStimulus
            {
                Tracks = tracks,
                Clocks = new[] { new ClockTrack { Port = _clock, Period = _period } },
                End = cycles * _period,
            };
            Project copy = _project with { Stimulus = stimulus, Bench = null };
            CompiledProject compiled = BoardCompiler.Compile(copy);
            if (!compiled.Succeeded)
            {
                why = "No state diagram: the circuit has to build first.";
                return false;
            }
            Elaboration elaboration = Elaborator.Elaborate(compiled.Tree, compiled.Top);
            if (elaboration.HasErrors)
            {
                why = "No state diagram: the circuit has to build first.";
                return false;
            }
            design = elaboration.Design;
            designPath = compiled.DesignPath;
            return true;
        }
    }
}
