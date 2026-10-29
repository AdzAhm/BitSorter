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
    /// **A driver belongs to the scope it is written in** (<see cref="DriverInfo.WrittenIn"/>), not its target's: a port's
    /// connection, written in the parent, is an instance pin there and nothing in the instance, whose input ports are inputs;
    /// and a signal driven from more than one scope -- an inout bus, each device's driver written in its own -- has in each
    /// scope only what that scope gives it, as a net of its own, its resolved value read off the design.
    ///
    /// **Each expression node is one cell**, however many places use it: <c>{cout, sum} = a + b + cin</c> is sliced once for
    /// each, and is still one adder of two.
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
    internal sealed partial class RtlBuilder
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
        private readonly Dictionary<Expr, int> _made = new Dictionary<Expr, int>();
        private int _unnamed;

        // While a process is opened, what each signal written so far on the path being followed holds: what a read sees.
        private Dictionary<int, int> _overlay;

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
            // The signals driven from more than one module's instance: an inout bus, each device's driver written in its own.
            var writer = new Dictionary<int, int>();
            var shared = new HashSet<int>();
            foreach (DriverInfo d in _design.Drivers)
            {
                int module = ModuleOf(WrittenIn(d));
                if (writer.TryGetValue(d.Target.Index, out int first) && first != module)
                    shared.Add(d.Target.Index);
                else
                    writer[d.Target.Index] = module;
            }

            // Every driver written in the scope's own, by its target -- a connection to an instance's input apart, as its pin.
            var drivers = new Dictionary<int, List<DriverInfo>>();
            var targets = new List<int>();
            var connections = new Dictionary<int, Expr>();
            foreach (DriverInfo d in _design.Drivers)
            {
                if (!_own.Contains(WrittenIn(d)))
                    continue;
                int target = d.Target.Index;
                SignalInfo info = _design.Signals[target];
                if (d is AssignDriver a && _instances.Contains(info.Scope) && info.Port == PortDirection.Input
                    && d.Name.StartsWith("port ", StringComparison.Ordinal))
                {
                    connections[target] = a.Value;
                    continue;
                }
                if (!drivers.TryGetValue(target, out List<DriverInfo> list))
                {
                    drivers[target] = list = new List<DriverInfo>();
                    targets.Add(target);
                }
                list.Add(d);
            }
            _shared = shared;

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

        private HashSet<int> _shared = new HashSet<int>();

        /// <summary>The scope a driver is written in: as the elaborator recorded it, or, for a circuit built in code, its target's.</summary>
        private int WrittenIn(DriverInfo d) => d.WrittenIn >= 0 ? d.WrittenIn : _design.Signals[d.Target.Index].Scope;

        /// <summary>The instance -- or the top -- a scope belongs to: itself, or the nearest such around a generate or named block.</summary>
        private int ModuleOf(int scope)
        {
            while (scope >= 0 && _design.Scopes[scope].Kind != ScopeKind.Top && _design.Scopes[scope].Kind != ScopeKind.Instance)
                scope = _design.Scopes[scope].Parent;
            return scope;
        }

        /// <summary>The net of an expression's value, each node made once: two places using one value share its cell.</summary>
        public int Of(Expr e)
        {
            // On a process's path a node's value depends on what the path has written, so it is made again each time.
            if (_overlay != null)
                return e.ToRtl(this);
            if (_made.TryGetValue(e, out int net))
                return net;
            net = e.ToRtl(this);
            _made[e] = net;
            return net;
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
            if (_overlay != null)
            {
                if (_overlay.TryGetValue(signal, out int written))
                    return written;
                if (_unbound != null && _unbound.Contains(signal))
                    Unbound(signal);
                _readHere?.Add(signal);
            }
            if (_netOfSignal.TryGetValue(signal, out int net))
                return net;
            net = NewNet(_design.Signals[signal].Width, NameOf(signal), signal);
            _netOfSignal[signal] = net;
            return net;
        }

        /// <summary>
        /// A signal's name from this scope: its own, past the scope's path -- or, for a net of another scope this one reaches
        /// by an alias, an inout's own name, as the code here names it (<c>bus</c>, not <c>tb.bus</c>).
        /// </summary>
        private string NameOf(int signal)
        {
            string name = _design.Signals[signal].Name;
            if (name.StartsWith(_prefix, StringComparison.Ordinal))
                return name.Substring(_prefix.Length);
            foreach ((string alias, SignalId aliased) in _design.Aliases)
                if (aliased.Index == signal && alias.StartsWith(_prefix, StringComparison.Ordinal) && alias.IndexOf('.', _prefix.Length) < 0)
                    return alias.Substring(_prefix.Length);
            return name;
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
                        inputs[k] = Of(g.Input(k));
                    string name = g.Name.StartsWith(_prefix, StringComparison.Ordinal) ? g.Name.Substring(_prefix.Length) : g.Name;
                    RtlCell gate = NewCell(RtlKind.Gate, name, inputs, 1);
                    gate.Gate = g.Kind;
                    values[i] = gate.Output;
                }
                else if (d is AssignDriver a)
                {
                    values[i] = Of(a.Value);
                }
            }

            // A signal other scopes drive as well gets only what this one gives it, on a net of its own; its value, all of them
            // resolved, is the design's.
            SignalInfo info = _design.Signals[target];
            int net = _shared.Contains(target) ? NewNet(info.Width, NameOf(target) + " (driven here)", -1) : Read(target, info.Width);
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
                    inputs.Add(connections.TryGetValue(i, out Expr value) ? Of(value) : -1);
                    inputNames.Add(pin);
                }
            }

            // An inout joined to the net it is connected to is that net, by another name: a pin on the net.
            foreach ((string alias, SignalId signal) in _design.Aliases)
            {
                if (alias.Length <= scope.Path.Length + 1 || !alias.StartsWith(scope.Path + ".", StringComparison.Ordinal))
                    continue;
                string pin = alias.Substring(scope.Path.Length + 1);
                if (pin.IndexOf('.') >= 0)
                    continue;
                inputs.Add(Read(signal.Index, _design.Signals[signal.Index].Width));
                inputNames.Add(pin);
            }

            // Named from this scope, so a generate loop's are told apart: slice[3].s.
            string name = scope.Path.StartsWith(_prefix, StringComparison.Ordinal) ? scope.Path.Substring(_prefix.Length) : scope.Name;
            RtlCell cell = NewCellDriving(RtlKind.Instance, name, inputs.ToArray(), outputs.ToArray());
            cell.Module = scope.Module;
            cell.InputNames = inputNames.ToArray();
            cell.OutputNames = outputNames.ToArray();
        }

        /// <summary>A process opened into cells; one that is no hardware, a box -- every signal it reads in, every one it writes out.</summary>
        private void Process(ProcessInfo process)
        {
            int cellsBefore = _cells.Count, netsBefore = _nets.Count;
            if (OpenProcess(process, out string why))
                return;
            Rollback(cellsBefore, netsBefore);

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

            // Every signal it writes, each an output -- one written by several processes, a generate loop's always_ff q[i] say, an
            // output of each, its net driven by the first.
            var outputs = new List<int>();
            var outputNames = new List<string>();
            var writes = new SortedSet<int>(process.Writes);
            foreach (int signal in writes)
            {
                SignalInfo info = _design.Signals[signal];
                if (info.Local || info.Memory != null)
                    continue;
                outputs.Add(Read(signal, info.Width));
                outputNames.Add(NameOf(signal));
            }

            string name = process.Name.StartsWith(_prefix, StringComparison.Ordinal) ? process.Name.Substring(_prefix.Length) : process.Name;
            RtlCell cell = NewCellDriving(RtlKind.Process, name, inputs.ToArray(), outputs.ToArray());
            cell.InputNames = inputNames.ToArray();
            cell.OutputNames = outputNames.ToArray();
            cell.Why = why;
        }

        /// <summary>Nothing of a run that gave up is kept: the cells and nets made since the counts given go.</summary>
        private void Rollback(int cellsBefore, int netsBefore)
        {
            for (int c = _cells.Count - 1; c >= cellsBefore; c--)
                foreach (int output in _cells[c].OutputList)
                    if (output < netsBefore)
                        _nets[output].Driver = -1;
            _cells.RemoveRange(cellsBefore, _cells.Count - cellsBefore);
            for (int n = _nets.Count - 1; n >= netsBefore; n--)
                if (_nets[n].Signal >= 0)
                    _netOfSignal.Remove(_nets[n].Signal);
            _nets.RemoveRange(netsBefore, _nets.Count - netsBefore);
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

        /// <summary>Whether a net is a constant's, and its value: what a cell of constants is folded into, and a branch decided by.</summary>
        internal bool TryConstant(int net, out LogicVec value)
        {
            int driver = net >= 0 ? _nets[net].Driver : -1;
            if (driver >= 0 && _cells[driver].Kind == RtlKind.Constant)
            {
                value = _cells[driver].Value;
                return true;
            }
            value = default;
            return false;
        }

        public int Constant(LogicVec value)
        {
            RtlCell cell = NewCell(RtlKind.Constant, "", Array.Empty<int>(), value.Width);
            cell.Value = value;
            return cell.Output;
        }

        public int Unary(UnaryOp op, int a, int width)
        {
            if (TryConstant(a, out LogicVec va))
                return Constant(Expr.Apply(op, va));
            RtlCell cell = NewCell(RtlKind.Unary, "", new[] { a }, width);
            cell.Unary = op;
            return cell.Output;
        }

        public int Binary(BinaryOp op, int a, int b, int width)
        {
            if (TryConstant(a, out LogicVec va) && TryConstant(b, out LogicVec vb))
                return Constant(Expr.Apply(op, va, vb));
            RtlCell cell = NewCell(RtlKind.Binary, "", new[] { a, b }, width);
            cell.Binary = op;
            return cell.Output;
        }

        public int Mux(int condition, int whenTrue, int whenFalse, int width)
        {
            if (TryConstant(condition, out LogicVec c) && TryConstant(whenTrue, out LogicVec t) && TryConstant(whenFalse, out LogicVec f))
                return Constant(LogicVec.Conditional(c, t, f));
            return NewCell(RtlKind.Mux, "", new[] { condition, whenTrue, whenFalse }, width).Output;
        }

        public int Concat(int high, int low, int width)
        {
            if (TryConstant(high, out LogicVec h) && TryConstant(low, out LogicVec l))
                return Constant(LogicVec.Concat(h, l));
            return NewCell(RtlKind.Concat, "", new[] { high, low }, width).Output;
        }

        public int Replicate(int a, int count, int width)
        {
            if (TryConstant(a, out LogicVec va))
                return Constant(LogicVec.Replicate(va, count));
            RtlCell cell = NewCell(RtlKind.Replicate, "", new[] { a }, width);
            cell.Count = count;
            return cell.Output;
        }

        public int Slice(int a, int lowest, int width)
        {
            if (TryConstant(a, out LogicVec va))
                return Constant(va.Slice(lowest, width));

            // Through what keeps bits where they are, to the net the bits came from: a slice of a slice, of a concatenation's
            // part, of a sign changed or a value widened. Only inside the value: outside it a slice gives x.
            int whole = _nets[a].Width;
            if (lowest >= 0 && lowest + width <= whole)
            {
                if (lowest == 0 && width == whole && IsUnsigned(a))
                    return a;
                RtlCell driver = DriverOf(a);
                switch (driver?.Kind)
                {
                    case RtlKind.Slice:
                        return Slice(driver.InputList[0], driver.Lowest + lowest, width);
                    case RtlKind.Signedness:
                        return Slice(driver.InputList[0], lowest, width);
                    case RtlKind.Resize when lowest + width <= _nets[driver.InputList[0]].Width:
                        return Slice(driver.InputList[0], lowest, width);
                    case RtlKind.Concat:
                    {
                        int high = driver.InputList[0], low = driver.InputList[1];
                        int lowWidth = _nets[low].Width;
                        if (lowest + width <= lowWidth)
                            return Slice(low, lowest, width);
                        if (lowest >= lowWidth && lowest + width - lowWidth <= _nets[high].Width)
                            return Slice(high, lowest - lowWidth, width);
                        if (lowest + width - lowWidth <= _nets[high].Width)
                            return Concat(Slice(high, 0, lowest + width - lowWidth), Slice(low, lowest, lowWidth - lowest), width);
                        break;
                    }
                }
            }

            RtlCell cell = NewCell(RtlKind.Slice, "", new[] { a }, width);
            cell.Lowest = lowest;
            cell.Width = width;
            return cell.Output;
        }

        public int BitOf(int a, int index)
        {
            if (TryConstant(index, out LogicVec vi))
            {
                if (TryConstant(a, out LogicVec va))
                    return Constant(va.Bit(vi));

                // A place the code fixes -- a loop's counter, unrolled -- is the bit itself, as LogicVec.Bit reads it.
                int width = _nets[a].Width;
                if (!vi.IsKnown || (vi.IsSigned && vi[vi.Width - 1] == Logic4.One)
                    || !vi.TryGetUInt64(out ulong at) || at >= (ulong)width)
                    return Constant(LogicVec.AllX(1));
                return Slice(a, (int)at, 1);
            }
            return NewCell(RtlKind.Bit, "", new[] { a, index }, 1).Output;
        }

        public int PartOf(int a, int offset, int step, int width)
        {
            if (TryConstant(offset, out LogicVec vo))
            {
                LogicVec unknown = LogicVec.AllX(width);
                if (TryConstant(a, out LogicVec va))
                    return Constant(Expr.TryOffset(vo, out long at) ? Expr.PartAt(va, at, step, width, unknown) : unknown);

                // A place the code fixes is a slice, as Expr.PartAt takes one.
                int whole = _nets[a].Width;
                if (!Expr.TryOffset(vo, out long place))
                    return Constant(unknown);
                long reach = (whole + (long)width) / step + 1;
                if (place > reach || place < -reach)
                    return Constant(unknown);
                long lowest = place * step;
                if (lowest >= whole || lowest + width <= 0)
                    return Constant(unknown);
                return Slice(a, (int)lowest, width);
            }
            RtlCell cell = NewCell(RtlKind.Part, "", new[] { a, offset }, width);
            cell.Step = step;
            cell.Width = width;
            return cell.Output;
        }

        public int Resize(int a, int width, bool signExtend)
        {
            if (TryConstant(a, out LogicVec va))
                return Constant(va.Extend(width, signExtend));
            RtlCell cell = NewCell(RtlKind.Resize, "", new[] { a }, width);
            cell.Width = width;
            cell.Signed = signExtend;
            return cell.Output;
        }

        public int Signedness(int a, bool signed, int width)
        {
            if (TryConstant(a, out LogicVec va))
                return Constant(signed ? va.AsSigned() : va.AsUnsigned());
            if (!signed && IsUnsigned(a))
                return a;
            RtlCell driver = DriverOf(a);
            if (driver?.Kind == RtlKind.Signedness)
                return Signedness(driver.InputList[0], signed, width);
            RtlCell cell = NewCell(RtlKind.Signedness, "", new[] { a }, width);
            cell.Signed = signed;
            return cell.Output;
        }

        public int TwoState(int a, int width)
        {
            if (TryConstant(a, out LogicVec va))
                return Constant(va.ToTwoState());
            return NewCell(RtlKind.TwoState, "", new[] { a }, width).Output;
        }

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
            // Inside a process the box would be worked out from what the signals hold at the end, not on the path followed.
            if (_overlay != null)
                throw new NotHardware("It works with a string, a real or $random: behaviour, not a circuit.");
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

        private RtlCell DriverOf(int net) => net >= 0 && _nets[net].Driver >= 0 ? _cells[_nets[net].Driver] : null;

        /// <summary>
        /// Whether a net's value is always plain bits: a signal's as the signal is declared -- every net of one holds its value
        /// as the signal holds it -- and a slice's, a concatenation's and the rest the operators give unsigned.
        /// </summary>
        private bool IsUnsigned(int net)
        {
            if (_nets[net].Signal >= 0)
                return !_design.Signals[_nets[net].Signal].Signed;
            RtlCell driver = DriverOf(net);
            switch (driver?.Kind)
            {
                case RtlKind.Slice:
                case RtlKind.Concat:
                case RtlKind.Replicate:
                case RtlKind.Bit:
                case RtlKind.Part:
                case RtlKind.Truth:
                case RtlKind.Match:
                    return true;
                case RtlKind.Constant:
                    return !driver.Value.IsSigned;
                case RtlKind.Signedness:
                    return !driver.Signed;
                default:
                    return false;
            }
        }

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
                if (_nets[net].Driver < 0)
                    _nets[net].Driver = cell.Id;
            return cell;
        }
    }
}
