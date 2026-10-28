using System;
using System.Collections.Generic;
using System.Globalization;
using BitSorter.Board;
using BitSorter.Sim;
using BitSorter.SystemVerilog;
using BitSorter.SystemVerilog.Syntax;

namespace BitSorter.ViewModel
{
    /// <summary>One output bit of the truth table: its name, and its value in each row -- 0, 1, or x.</summary>
    public sealed class LogicOutput
    {
        internal LogicOutput(string name, string port, int bit, char[] values)
        {
            Name = name;
            Port = port;
            Bit = bit;
            Values = values;
        }

        /// <summary><c>sum</c>, or <c>s[2]</c> of a bus.</summary>
        public string Name { get; }

        public string Port { get; }
        public int Bit { get; }

        /// <summary>Its value in row r, by <see cref="LogicTable.Variables"/>: '0', '1' or 'x' -- x for an x, a z, or no value.</summary>
        public IReadOnlyList<char> Values { get; }
    }

    /// <summary>
    /// The LOGIC tab's truth table (Part 15g): every combination of a circuit's input bits, and what each output reads once
    /// it has settled -- found by running the real engine, as nothing else would show what the circuit as built does. Ahmad
    /// asked for a way to know more about what is built (2026-10-28): the truth table, each output as an expression, and its
    /// K-map (<see cref="Minimizer"/>, <see cref="KarnaughLayout"/>).
    /// </summary>
    /// <remarks>
    /// **The run is a copy's, made a slice a frame** (<see cref="StepTests.Combinations"/>), every combination in Gray order,
    /// each held longer than the slowest path the timing found, as the measured worst is (<see cref="StepMeasure"/>): every
    /// output read at the end of a hold has settled. **Only logic without registers, of at most
    /// <see cref="StepTests.MostInputBits"/> input bits**: a circuit with registers gives what it holds as well as what it is
    /// given, and more bits than that are more rows than anyone reads. A loop of logic, or a delay the run works out, gives no
    /// settle time known to be long enough, and is refused as the measured worst is.
    ///
    /// **Rows count up, the first input most significant** -- a textbook's truth table: <see cref="Variables"/> are the input
    /// bits, ports in order and a bus's highest bit first, and in row r variable i is bit (n-1-i) of r.
    /// </remarks>
    public sealed class LogicTable
    {
        private readonly StepPlan _plan;
        private readonly int[] _variableOfInput;
        private readonly List<(SignalId Signal, int Bit)> _signals;
        private SimSession _session;
        private RunSlicer _slicer;
        private readonly char[][] _values;

        private LogicTable(Project project, StepPlan plan, IReadOnlyList<InputBit> variables, IReadOnlyList<string> variableNames,
            int[] variableOfInput, List<(string Name, string Port, int Bit)> outputs, List<(SignalId, int)> signals, SimSession session,
            RunSlicer slicer)
        {
            Project = project;
            _plan = plan;
            Variables = variables;
            VariableNames = variableNames;
            _variableOfInput = variableOfInput;
            _signals = signals;
            _session = session;
            _slicer = slicer;
            _values = new char[outputs.Count][];
            var made = new List<LogicOutput>(outputs.Count);
            for (int o = 0; o < outputs.Count; o++)
            {
                _values[o] = new char[1 << variables.Count];
                made.Add(new LogicOutput(outputs[o].Name, outputs[o].Port, outputs[o].Bit, _values[o]));
            }
            Outputs = made;
        }

        /// <summary>The document tabled.</summary>
        public Project Project { get; }

        /// <summary>The input bits, as the rows count them: ports in order, a bus's highest bit first.</summary>
        public IReadOnlyList<InputBit> Variables { get; }

        /// <summary>
        /// Each input bit's name as the design declares it, in <see cref="Variables"/>' order: an <c>input [7:6] a</c>'s bits are
        /// a[7] and a[6], where <see cref="InputBit.Name"/> counts from 0 (the 15g-5 review).
        /// </summary>
        public IReadOnlyList<string> VariableNames { get; }

        /// <summary>Each output bit, ports in order and a bus's highest bit first, with its value in every row once done.</summary>
        public IReadOnlyList<LogicOutput> Outputs { get; }

        /// <summary>How many rows: every combination of the input bits.</summary>
        public int Rows => 1 << Variables.Count;

        /// <summary>Whether the run is made and read, or found it could not be.</summary>
        public bool Done => _session == null;

        /// <summary>How much of the run is made, 0 to 1.</summary>
        public double Progress => _slicer == null || _slicer.Horizon == 0 ? 1 : Math.Min(1.0, (double)_session.Reached / _slicer.Horizon);

        /// <summary>Why, once done, the table is not to be trusted after all: the run ended short. Null when it is.</summary>
        public string Problem { get; private set; }

        /// <summary>Input variable <paramref name="variable"/>'s value in row <paramref name="row"/>: the first variable the most significant.</summary>
        public int ValueOf(int variable, int row) => (row >> (Variables.Count - 1 - variable)) & 1;

        /// <summary>
        /// How many combinations <paramref name="bits"/> bits make, written out -- 1,024 -- or, past what a 64-bit number holds,
        /// as a power of two: 2^131. A 64-bit 1 shifted by the count wraps round past 63, and a 64-bit ALU's 131 input bits read
        /// "8 rows" (the 15h browser test).
        /// </summary>
        internal static string PowerOfTwo(int bits) => bits < 63
            ? (1L << bits).ToString("N0", CultureInfo.InvariantCulture)
            : "2^" + bits.ToString(CultureInfo.InvariantCulture);

        /// <summary>
        /// The truth table of <paramref name="project"/>, its run's first slice made, each combination held longer than
        /// <paramref name="timing"/>'s slowest path -- or null, and why in <paramref name="why"/>.
        /// </summary>
        public static LogicTable Start(Project project, TimingView timing, Func<double> nowMilliseconds, out string why,
            double budgetMilliseconds = StepMeasure.BudgetMilliseconds)
        {
            why = null;
            TimingAnalysis analysis = timing?.Analysis;
            if (project == null || analysis == null || !analysis.Succeeded)
            {
                why = "No truth table: the circuit has to build first.";
                return null;
            }
            if (analysis.Graph.Registers.Count > 0)
            {
                why = "The truth table is for logic without registers: what a circuit with them gives depends on what it holds as well.";
                return null;
            }
            if (analysis.Graph.Loops.Count > 0)
            {
                why = "No truth table: a loop of logic need never settle, so no row can be read once it has.";
                return null;
            }
            if (analysis.Graph.UnknownDelays)
            {
                why = "No truth table: a delay here is worked out as the circuit runs, so no hold is known to be long enough.";
                return null;
            }

            IReadOnlyList<InputBit> inputs = StepTests.InputsOf(project);
            List<(string Name, string Port, int Bit)> outputs = OutputsOf(project);
            if (inputs.Count == 0 || outputs.Count == 0)
            {
                why = "No truth table yet: the circuit needs an input and an output.";
                return null;
            }
            if (inputs.Count > StepTests.MostInputBits)
            {
                why = inputs.Count.ToString(CultureInfo.InvariantCulture) + " input bits are " + PowerOfTwo(inputs.Count)
                    + " rows; the truth table goes to " + StepTests.MostInputBits.ToString(CultureInfo.InvariantCulture) + " bits, "
                    + (1 << StepTests.MostInputBits).ToString("N0", CultureInfo.InvariantCulture) + " rows.";
                return null;
            }

            StepPlan plan = StepTests.Combinations(project, StepTests.SettleFor(timing.Slowest));
            Project copy = project with { Stimulus = plan.Stimulus, Bench = null };
            CompiledProject compiled = BoardCompiler.Compile(copy);
            if (!compiled.Succeeded)
            {
                why = "No truth table: the circuit has to build first.";
                return null;
            }
            Elaboration elaboration = Elaborator.Elaborate(compiled.Tree, compiled.Top);
            if (elaboration.HasErrors)
            {
                why = "No truth table: the circuit has to build first.";
                return null;
            }
            Design design = elaboration.Design;

            // The variables: ports in order, a bus's highest bit first; and which variable each of the plan's inputs is.
            var variables = new List<InputBit>(inputs.Count);
            var ports = new List<(string Port, int Width)>();
            foreach (InputBit bit in inputs)
                if (ports.Count == 0 || ports[ports.Count - 1].Port != bit.Port)
                    ports.Add((bit.Port, bit.Width));
            foreach ((string port, int width) in ports)
                for (int b = width - 1; b >= 0; b--)
                    variables.Add(new InputBit(port, width, b));
            var variableOf = new int[inputs.Count];
            for (int k = 0; k < inputs.Count; k++)
                for (int v = 0; v < variables.Count; v++)
                    if (variables[v].Port == inputs[k].Port && variables[v].Bit == inputs[k].Bit)
                        variableOf[k] = v;

            // Every bit named as the timing names it, from the design, its range's index and all: a [0:1]'s high bit is y[0].
            string Named(string port, int bit, string otherwise) =>
                design.TryFind(compiled.DesignPath + "." + port, out SignalId id)
                    ? TimingView.Relative(TimingGraph.BitName(design.Signals[id.Index], bit), compiled.DesignPath)
                    : otherwise;
            var variableNames = new List<string>(variables.Count);
            foreach (InputBit variable in variables)
                variableNames.Add(Named(variable.Port, variable.Bit, variable.Name));
            for (int o = 0; o < outputs.Count; o++)
                outputs[o] = (Named(outputs[o].Port, outputs[o].Bit, outputs[o].Name), outputs[o].Port, outputs[o].Bit);

            var signals = new List<(SignalId, int)>(outputs.Count);
            foreach ((string _, string port, int bit) in outputs)
                signals.Add(design.TryFind(compiled.DesignPath + "." + port, out SignalId signal) ? (signal, bit) : (default, -1));

            var options = new SimOptions { Files = ProjectFiles.ForRun(copy), RecordTransitions = false };
            var session = new SimSession(design, null, options);
            var slicer = new RunSlicer(session, plan.Stimulus.StopTime, nowMilliseconds, budgetMilliseconds: budgetMilliseconds);
            var table = new LogicTable(project, plan, variables, variableNames, variableOf, outputs, signals, session, slicer);
            table.Frame();
            return table;
        }

        /// <summary>One frame's share of the run, and the outputs read once it is made. False once it is done.</summary>
        public bool Frame()
        {
            if (Done)
                return false;
            _slicer.Slice();
            if (_slicer.Done)
                Read();
            return true;
        }

        /// <summary>The rest of the run now, and the outputs read: what a test does.</summary>
        public void Finish()
        {
            if (Done)
                return;
            _slicer.Finish();
            Read();
        }

        /// <summary>The top's outputs as bits: ports in order, a bus's highest bit first.</summary>
        private static List<(string Name, string Port, int Bit)> OutputsOf(Project project)
        {
            var outputs = new List<(string, string, int)>();
            Module top = project.TopModule;
            if (top == null)
                return outputs;
            foreach (ModulePort port in WrittenModules.PortsOf(project, top))
                if (port.Direction == PortDirection.Output)
                    for (int b = port.Width - 1; b >= 0; b--)
                        outputs.Add((new InputBit(port.Name, port.Width, b).Name, port.Name, b));
            return outputs;
        }

        /// <summary>Each output bit at the end of each hold, in the row its combination is; the run let go.</summary>
        private void Read()
        {
            try
            {
                if (_slicer.Ended == RunStatus.Stopped || _session.Reached < _slicer.Horizon)
                    Problem = "The truth table's run stopped at " + _session.Reached.ToString(CultureInfo.InvariantCulture) + " of "
                        + _slicer.Horizon.ToString(CultureInfo.InvariantCulture) + ": its rows after that are not read.";

                foreach (StepWindow window in _plan.Windows)
                {
                    int row = 0;
                    for (int k = 0; k < _variableOfInput.Length; k++)
                        if ((window.Word >> k & 1) != 0)
                            row |= 1 << (Variables.Count - 1 - _variableOfInput[k]);

                    long at = window.At + _plan.Settle - 1;
                    for (int o = 0; o < _signals.Count; o++)
                    {
                        char value = 'x';
                        if (at <= _session.Reached && _signals[o].Bit >= 0)   // -1: a port the design has no signal for
                        {
                            LogicVec read = _session.ValueAt(_signals[o].Signal, at);
                            Logic4 bit = _signals[o].Bit < read.Width ? read[_signals[o].Bit] : Logic4.X;
                            value = bit == Logic4.Zero ? '0' : bit == Logic4.One ? '1' : 'x';
                        }
                        _values[o][row] = value;
                    }
                }
            }
            finally
            {
                _session = null;
                _slicer = null;
            }
        }
    }
}
