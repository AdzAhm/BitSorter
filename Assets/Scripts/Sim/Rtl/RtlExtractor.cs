using System;
using System.Collections.Generic;
using BitSorter.SystemVerilog;
using BitSorter.SystemVerilog.Syntax;

namespace BitSorter.Sim
{
    /// <summary>
    /// The netlist one scope of an elaborated design builds (Part 15h): its gates, the cells of its continuous assignments'
    /// expressions, its instances, its processes and its memories' read ports, joined by nets.
    /// </summary>
    /// <remarks>
    /// **The elaborated design is the source**, where parameters, generate loops and widths are already worked out. A scope's
    /// own logic is what drives its own signals and those of its generate blocks and named blocks; an instance inside it is
    /// one cell, its pins the instance's ports, the values on them read off the zero-delay assignments the elaborator joins
    /// a port with (<c>port dut.h1.a</c>).
    ///
    /// **Every driven signal is the output of a <see cref="RtlKind.Join"/>**: one driver of the whole signal is a wire, and
    /// slices or several drivers are put together as the engine puts them -- so a netlist computes exactly what the design
    /// does, its signals' sign and 2-state type included, and a drawing collapses the wires.
    ///
    /// **A process is a box here.** Part 15h-2 opens each that describes hardware into muxes and registers.
    /// </remarks>
    public static class RtlExtractor
    {
        /// <summary>The netlist of the scope at <paramref name="scopePath"/>; null when the design has no such scope.</summary>
        public static RtlNetlist Of(Design design, string scopePath)
        {
            if (design == null)
                throw new ArgumentNullException(nameof(design));

            int scope = -1;
            for (int i = 0; i < design.Scopes.Count; i++)
                if (design.Scopes[i].Path == scopePath)
                {
                    scope = i;
                    break;
                }
            if (scope < 0)
                return null;

            var b = new RtlBuilder(design, scope);
            b.Build();
            return b.Finish();
        }
    }

    /// <summary>Builds one scope's <see cref="RtlNetlist"/>: what <see cref="Expr.ToRtl"/> calls back, node by node.</summary>
    internal sealed class RtlBuilder
    {
        private readonly Design _design;
        private readonly int _scope;
        private readonly string _prefix;

        // The scope and its generate and named blocks, which are its module's own; and the instances directly in them.
        private readonly HashSet<int> _own = new HashSet<int>();
        private readonly List<int> _instances = new List<int>();

        private readonly List<RtlNet> _nets = new List<RtlNet>();
        private readonly List<RtlCell> _cells = new List<RtlCell>();
        private readonly Dictionary<int, int> _netOfSignal = new Dictionary<int, int>();
        private int _unnamed;

        public RtlBuilder(Design design, int scope)
        {
            _design = design;
            _scope = scope;
            _prefix = design.Scopes[scope].Path + ".";

            _own.Add(scope);
            for (int i = scope + 1; i < design.Scopes.Count; i++)
            {
                ScopeInfo s = design.Scopes[i];
                if (!_own.Contains(s.Parent))
                    continue;
                if (s.Kind == ScopeKind.Instance)
                    _instances.Add(i);
                else
                    _own.Add(i);
            }
        }

        public void Build()
        {
            // Every driver of a signal of the scope's own, by its target; and the connections of each instance's inputs.
            var drivers = new Dictionary<int, List<DriverInfo>>();
            var targets = new List<int>();
            var connections = new Dictionary<int, Expr>();
            foreach (DriverInfo d in _design.Drivers)
            {
                int target = d.Target.Index;
                int at = _design.Signals[target].Scope;
                if (_own.Contains(at))
                {
                    if (!drivers.TryGetValue(target, out List<DriverInfo> list))
                    {
                        drivers[target] = list = new List<DriverInfo>();
                        targets.Add(target);
                    }
                    list.Add(d);
                }
                else if (d is AssignDriver a && _instances.Contains(at) && _design.Signals[target].Port == PortDirection.Input
                         && d.Name.StartsWith("port ", StringComparison.Ordinal))
                {
                    connections[target] = a.Value;
                }
            }

            // The scope's ports first, so they are its first nets, in the order declared.
            for (int i = 0; i < _design.Signals.Count; i++)
                if (_design.Signals[i].Scope == _scope && _design.Signals[i].Port != null)
                    Read(i, _design.Signals[i].Width);

            foreach (int target in targets)
                Drive(target, drivers[target]);

            foreach (int instance in _instances)
                Instance(instance, connections);

            for (int p = 0; p < _design.Processes.Count; p++)
            {
                ProcessInfo process = _design.Processes[p];
                if (!process.Dormant && _own.Contains(ScopeOf(process.Name)))
                    Process(process);
            }
        }

        public RtlNetlist Finish()
        {
            var inputs = new List<int>();
            var outputs = new List<int>();
            for (int i = 0; i < _design.Signals.Count; i++)
            {
                SignalInfo info = _design.Signals[i];
                if (info.Scope != _scope || info.Port == null || !_netOfSignal.TryGetValue(i, out int net))
                    continue;
                if (info.Port == PortDirection.Output)
                    outputs.Add(net);
                else
                    inputs.Add(net);
            }

            ScopeInfo scope = _design.Scopes[_scope];
            return new RtlNetlist(scope.Path, scope.Module, _nets.ToArray(), _cells.ToArray(), inputs.ToArray(), outputs.ToArray());
        }

        // -----------------------------------------------------------------
        // Signals, drivers, instances and processes
        // -----------------------------------------------------------------

        /// <summary>The net of signal <paramref name="signal"/>, made the first time it is asked for.</summary>
        public int Read(int signal, int width)
        {
            if (_netOfSignal.TryGetValue(signal, out int net))
                return net;
            net = NewNet(_design.Signals[signal].Width, NameOf(signal), signal);
            _netOfSignal[signal] = net;
            return net;
        }

        private string NameOf(int signal)
        {
            string name = _design.Signals[signal].Name;
            return name.StartsWith(_prefix, StringComparison.Ordinal) ? name.Substring(_prefix.Length) : name;
        }

        /// <summary>A signal's drivers, each its value's cells, joined into the signal's net.</summary>
        private void Drive(int target, List<DriverInfo> drivers)
        {
            var values = new int[drivers.Count];
            var lowests = new int[drivers.Count];
            var widths = new int[drivers.Count];
            for (int i = 0; i < drivers.Count; i++)
            {
                DriverInfo d = drivers[i];
                lowests[i] = d.TargetLowest;
                widths[i] = d.TargetWidth;
                if (d is GateDriver g)
                {
                    var inputs = new int[g.InputCount];
                    for (int k = 0; k < inputs.Length; k++)
                        inputs[k] = g.Input(k).ToRtl(this);
                    RtlCell gate = NewCell(RtlKind.Gate, g.Name, inputs, 1);
                    gate.Gate = g.Kind;
                    values[i] = gate.Output;
                }
                else if (d is AssignDriver a)
                {
                    values[i] = a.Value.ToRtl(this);
                }
            }

            SignalInfo info = _design.Signals[target];
            int net = Read(target, info.Width);
            RtlCell join = NewCellDriving(RtlKind.Join, NameOf(target), values, net);
            join.Signal = target;
            join.Lowests = lowests;
            join.PieceWidths = widths;
            join.Wire = info.Kind == SignalKind.Net;
            join.Signed = info.Signed;
            join.TwoStateTarget = info.TwoState;
        }

        /// <summary>An instance: its inputs the values its parent connects, its outputs its own output ports' nets.</summary>
        private void Instance(int instance, Dictionary<int, Expr> connections)
        {
            ScopeInfo scope = _design.Scopes[instance];
            var inputs = new List<int>();
            var inputNames = new List<string>();
            var outputs = new List<int>();
            var outputNames = new List<string>();
            for (int i = 0; i < _design.Signals.Count; i++)
            {
                SignalInfo info = _design.Signals[i];
                if (info.Scope != instance || info.Port == null)
                    continue;
                string pin = info.Name.Substring(scope.Path.Length + 1);
                if (info.Port == PortDirection.Output)
                {
                    outputs.Add(Read(i, info.Width));
                    outputNames.Add(pin);
                }
                else
                {
                    inputs.Add(connections.TryGetValue(i, out Expr value) ? value.ToRtl(this) : -1);
                    inputNames.Add(pin);
                }
            }

            // Named from this scope, so a generate loop's are told apart: slice[3].s.
            string name = scope.Path.StartsWith(_prefix, StringComparison.Ordinal) ? scope.Path.Substring(_prefix.Length) : scope.Name;
            RtlCell cell = NewCellDriving(RtlKind.Instance, name, inputs.ToArray(), outputs.ToArray());
            cell.Module = scope.Module;
            cell.InputNames = inputNames.ToArray();
            cell.OutputNames = outputNames.ToArray();
        }

        /// <summary>A process as a box: every signal it reads in, every one it writes out.</summary>
        private void Process(ProcessInfo process)
        {
            var reads = new SortedSet<int>();
            process.CollectAllReads(reads);
            var inputs = new List<int>();
            var inputNames = new List<string>();
            foreach (int signal in reads)
            {
                SignalInfo info = _design.Signals[signal];
                if (info.Local || (_design.Time is SignalId time && time.Index == signal))
                    continue;
                inputs.Add(Read(signal, info.Width));
                inputNames.Add(NameOf(signal));
            }

            var outputs = new List<int>();
            var outputNames = new List<string>();
            var writes = new SortedSet<int>(process.Writes);
            foreach (int signal in writes)
            {
                SignalInfo info = _design.Signals[signal];
                if (info.Local || info.Memory != null)
                    continue;
                int net = Read(signal, info.Width);
                if (_nets[net].Driver >= 0)
                    continue;
                outputs.Add(net);
                outputNames.Add(NameOf(signal));
            }

            string name = process.Name.StartsWith(_prefix, StringComparison.Ordinal) ? process.Name.Substring(_prefix.Length) : process.Name;
            RtlCell cell = NewCellDriving(RtlKind.Process, name, inputs.ToArray(), outputs.ToArray());
            cell.InputNames = inputNames.ToArray();
            cell.OutputNames = outputNames.ToArray();
        }

        /// <summary>The scope a process is in, by its name: its scope's path, a dot, and where it is written.</summary>
        private int ScopeOf(string processName)
        {
            int best = -1, length = -1;
            for (int i = 0; i < _design.Scopes.Count; i++)
            {
                string path = _design.Scopes[i].Path;
                if (path.Length > length && processName.Length > path.Length && processName[path.Length] == '.'
                    && processName.StartsWith(path, StringComparison.Ordinal))
                {
                    best = i;
                    length = path.Length;
                }
            }
            return best;
        }

        // -----------------------------------------------------------------
        // What Expr.ToRtl calls, a cell a node
        // -----------------------------------------------------------------

        public int Constant(LogicVec value)
        {
            RtlCell cell = NewCell(RtlKind.Constant, "", Array.Empty<int>(), value.Width);
            cell.Value = value;
            return cell.Output;
        }

        public int Unary(UnaryOp op, int a, int width)
        {
            RtlCell cell = NewCell(RtlKind.Unary, "", new[] { a }, width);
            cell.Unary = op;
            return cell.Output;
        }

        public int Binary(BinaryOp op, int a, int b, int width)
        {
            RtlCell cell = NewCell(RtlKind.Binary, "", new[] { a, b }, width);
            cell.Binary = op;
            return cell.Output;
        }

        public int Mux(int condition, int whenTrue, int whenFalse, int width) =>
            NewCell(RtlKind.Mux, "", new[] { condition, whenTrue, whenFalse }, width).Output;

        public int Concat(int high, int low, int width) => NewCell(RtlKind.Concat, "", new[] { high, low }, width).Output;

        public int Replicate(int a, int count, int width)
        {
            RtlCell cell = NewCell(RtlKind.Replicate, "", new[] { a }, width);
            cell.Count = count;
            return cell.Output;
        }

        public int Slice(int a, int lowest, int width)
        {
            RtlCell cell = NewCell(RtlKind.Slice, "", new[] { a }, width);
            cell.Lowest = lowest;
            cell.Width = width;
            return cell.Output;
        }

        public int BitOf(int a, int index) => NewCell(RtlKind.Bit, "", new[] { a, index }, 1).Output;

        public int PartOf(int a, int offset, int step, int width)
        {
            RtlCell cell = NewCell(RtlKind.Part, "", new[] { a, offset }, width);
            cell.Step = step;
            cell.Width = width;
            return cell.Output;
        }

        public int Resize(int a, int width, bool signExtend)
        {
            RtlCell cell = NewCell(RtlKind.Resize, "", new[] { a }, width);
            cell.Width = width;
            cell.Signed = signExtend;
            return cell.Output;
        }

        public int Signedness(int a, bool signed, int width)
        {
            RtlCell cell = NewCell(RtlKind.Signedness, "", new[] { a }, width);
            cell.Signed = signed;
            return cell.Output;
        }

        public int TwoState(int a, int width) => NewCell(RtlKind.TwoState, "", new[] { a }, width).Output;

        public int Word(int first, int depth, int width, int offset)
        {
            RtlCell cell = NewCell(RtlKind.Word, "", new[] { offset }, width);
            MemoryInfo memory = _design.Signals[first].Memory;
            cell.Memory = memory;
            cell.Name = memory != null && memory.Name.StartsWith(_prefix, StringComparison.Ordinal) ? memory.Name.Substring(_prefix.Length) : memory?.Name ?? "";
            return cell.Output;
        }

        /// <summary>An expression that is no hardware: a box reading every signal it reads, which the evaluator works out as the engine does.</summary>
        public int Opaque(Expr e)
        {
            var reads = new SortedSet<int>();
            e.CollectReads(reads);
            var inputs = new List<int>();
            foreach (int signal in reads)
                inputs.Add(Read(signal, _design.Signals[signal].Width));
            RtlCell cell = NewCell(RtlKind.Opaque, "", inputs.ToArray(), e.Width);
            cell.Source = e;
            return cell.Output;
        }

        // -----------------------------------------------------------------
        // Nets and cells
        // -----------------------------------------------------------------

        private int NewNet(int width, string name, int signal)
        {
            var net = new RtlNet(_nets.Count, width, name ?? "$" + (_unnamed++).ToString(System.Globalization.CultureInfo.InvariantCulture), signal);
            _nets.Add(net);
            return net.Id;
        }

        /// <summary>A cell driving a new net of <paramref name="width"/> bits, its value inside an expression.</summary>
        private RtlCell NewCell(RtlKind kind, string name, int[] inputs, int width) =>
            NewCellDriving(kind, name, inputs, NewNet(width, null, -1));

        private RtlCell NewCellDriving(RtlKind kind, string name, int[] inputs, params int[] outputs)
        {
            var cell = new RtlCell(_cells.Count, kind, name, inputs, outputs);
            _cells.Add(cell);
            foreach (int net in outputs)
                _nets[net].Driver = cell.Id;
            return cell;
        }
    }
}
