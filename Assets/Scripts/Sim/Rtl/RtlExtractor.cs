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
                if (d is AssignDriver && !d.Delay.Equals(Delay.Zero))
                    _unshown.Add("the delay on the assign to " + NameOf(target));
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

            // Every process that describes a circuit opened first, then what they drive, then the boxes: a box made first took
            // the net a register drives, an initial q = 0 before its always_ff (the review of #106).
            var opened = new List<Opened>();
            var boxed = new List<(ProcessInfo Process, string Why)>();
            for (int p = 0; p < _design.Processes.Count; p++)
            {
                ProcessInfo process = _design.Processes[p];
                if (process.Dormant || !_own.Contains(ScopeOf(process.Name)))
                    continue;
                int cellsBefore = _cells.Count, netsBefore = _nets.Count;
                Opened o = OpenProcess(process, out string why);
                if (o != null)
                {
                    opened.Add(o);
                    var written = new List<int>();
                    foreach (var write in o.Writes)
                        if (!written.Contains(write.Signal))
                            written.Add(write.Signal);
                    _processes.Add(new RtlProcessRecord(Relative(process.Name), true, null, written.ToArray()));
                }
                else
                {
                    Rollback(cellsBefore, netsBefore);
                    boxed.Add((process, why));
                }
            }

            DriveOpened(opened);
            foreach ((ProcessInfo process, string why) in boxed)
                Box(process, why);
        }

        private readonly List<RtlProcessRecord> _processes = new List<RtlProcessRecord>();

        /// <summary>What the code does that no cell says (<see cref="RtlNetlist.Unshown"/>).</summary>
        private readonly List<string> _unshown = new List<string>();

        private string Relative(string name) => name.StartsWith(_prefix, StringComparison.Ordinal) ? name.Substring(_prefix.Length) : name;

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
            // On a process's path a node's value depends on what the path has written: kept with the path, until it writes.
            if (_overlay != null)
            {
                if (_pathMade != null && _pathMade.TryGetValue(e, out int made))
                    return made;
                made = e.ToRtl(this);
                if (_pathMade != null)
                    _pathMade[e] = made;
                return made;
            }
            if (_made.TryGetValue(e, out int net))
                return net;
            net = e.ToRtl(this);
            _made[e] = net;
            return net;
        }

        public RtlNetlist Finish()
        {
            Prune();
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

            // A starting value is a register's, which the drawing keeps on it; on any other signal the code says something no
            // cell does.
            var held = new HashSet<int>();
            foreach (RtlCell cell in _cells)
                if (cell.Kind == RtlKind.Register && cell.Signal >= 0)
                {
                    // Its own bits of it: a generate loop's flops each hold one of the signal's (the review of #117).
                    LogicVec? initial = _design.Signals[cell.Signal].Initial;
                    int width = _nets[cell.Output].Width;
                    cell.Initial = initial is LogicVec whole && (cell.Lowest != 0 || whole.Width != width) && cell.Lowest + width <= whole.Width
                        ? whole.Slice(cell.Lowest, width) : initial;
                    held.Add(cell.Signal);
                }
            for (int i = 0; i < _design.Signals.Count; i++)
            {
                SignalInfo info = _design.Signals[i];
                if (info.Initial != null && _own.Contains(info.Scope) && !held.Contains(i) && !info.Local && info.Memory == null)
                    _unshown.Add("the starting value of " + NameOf(i));
            }

            ScopeInfo scope = _design.Scopes[_scope];
            var netlist = new RtlNetlist(scope.Path, scope.Module, _nets.ToArray(), _cells.ToArray(), inputs.ToArray(), outputs.ToArray(),
                _processes.ToArray(), _unshown);

            // What each asynchronous reset or set puts in, worked out on the netlist itself (Part 15h-4g).
            foreach (RtlCell cell in _cells)
            {
                if (cell.Kind != RtlKind.Register || cell.Clock <= 0 || cell.Inputs.Count != 3)
                    continue;
                int at = 3 - cell.Clock, net = cell.Inputs[at];
                EdgeKind edge = cell.Edges[at - 1];
                int signal = net >= 0 ? _nets[net].Signal : -1;
                cell.Reset = signal >= 0 && (edge == EdgeKind.Posedge || edge == EdgeKind.Negedge)
                    ? ResetValue(netlist, cell, signal, edge == EdgeKind.Posedge) : (LogicVec?)null;
                if (cell.Reset == null)
                    _unshown.Add("the asynchronous reset of " + (cell.Signal >= 0 ? NameOf(cell.Signal) : cell.Name) + ", which puts in no one value");
            }
            return netlist;
        }

        /// <summary>
        /// A register's D with <paramref name="signal"/> -- its reset -- held at its active value and every other signal at random,
        /// four times over: the one known value it is each time, or null.
        /// </summary>
        private LogicVec? ResetValue(RtlNetlist netlist, RtlCell register, int signal, bool active)
        {
            var random = new Random(register.Id * 7919 + 17);
            LogicVec? value = null;
            for (int round = 0; round < 4; round++)
            {
                var signals = new LogicVec[_design.Signals.Count];
                for (int i = 0; i < signals.Length; i++)
                {
                    SignalInfo info = _design.Signals[i];
                    var digits = new char[Math.Max(1, info.Width)];
                    for (int k = 0; k < digits.Length; k++)
                        digits[k] = random.Next(2) == 0 ? '0' : '1';
                    LogicVec v = LogicVec.FromBinary(new string(digits));
                    signals[i] = info.Signed ? v.AsSigned() : v;
                }
                signals[signal] = LogicVec.FromUInt64(active ? 1UL : 0UL, Math.Max(1, _design.Signals[signal].Width));
                LogicVec d = RtlEvaluator.Evaluate(netlist, signals)[register.Inputs[0]];
                if (!d.IsKnown || value is LogicVec seen && !seen.Equals(d))
                    return null;
                value = d;
            }
            return value;
        }

        /// <summary>
        /// Every cell nothing uses taken out, and the nets only they drove: a loop unrolled or a function opened leaves the
        /// constants and values of every pass it folded away (the review of #106: a 4-bit loop left 45 of its 65 cells). A cell
        /// is used when it is a box -- an instance, a process, a register -- or a join, what the scope gives a signal, or drives
        /// a signal, or feeds one that is used.
        /// Ids are given again in order, so a net's or a cell's id is still its place.
        /// </summary>
        private void Prune()
        {
            var used = new bool[_cells.Count];
            var stack = new Stack<int>();
            foreach (RtlCell cell in _cells)
            {
                bool root = RtlEvaluator.IsBox(cell) || cell.Kind == RtlKind.Join;
                foreach (int output in cell.OutputList)
                    root |= _nets[output].Signal >= 0;
                if (root)
                {
                    used[cell.Id] = true;
                    stack.Push(cell.Id);
                }
            }
            while (stack.Count > 0)
            {
                foreach (int input in _cells[stack.Pop()].InputList)
                {
                    int driver = input >= 0 ? _nets[input].Driver : -1;
                    if (driver >= 0 && !used[driver])
                    {
                        used[driver] = true;
                        stack.Push(driver);
                    }
                }
            }

            var keptNet = new bool[_nets.Count];
            foreach (RtlNet net in _nets)
                keptNet[net.Id] = net.Signal >= 0;
            foreach (RtlCell cell in _cells)
            {
                if (!used[cell.Id])
                    continue;
                foreach (int input in cell.InputList)
                    if (input >= 0)
                        keptNet[input] = true;
                foreach (int output in cell.OutputList)
                    keptNet[output] = true;
            }

            var netId = new int[_nets.Count];
            var nets = new List<RtlNet>();
            foreach (RtlNet net in _nets)
            {
                netId[net.Id] = keptNet[net.Id] ? nets.Count : -1;
                if (keptNet[net.Id])
                    nets.Add(net);
            }
            var cellId = new int[_cells.Count];
            var cells = new List<RtlCell>();
            foreach (RtlCell cell in _cells)
            {
                cellId[cell.Id] = used[cell.Id] ? cells.Count : -1;
                if (used[cell.Id])
                    cells.Add(cell);
            }

            foreach (RtlCell cell in cells)
            {
                for (int i = 0; i < cell.InputList.Length; i++)
                    cell.InputList[i] = cell.InputList[i] >= 0 ? netId[cell.InputList[i]] : -1;
                for (int i = 0; i < cell.OutputList.Length; i++)
                    cell.OutputList[i] = netId[cell.OutputList[i]];
                cell.Id = cellId[cell.Id];
            }
            foreach (RtlNet net in nets)
            {
                net.Driver = net.Driver >= 0 ? cellId[net.Driver] : -1;
                net.Id = netId[net.Id];
            }
            var bySignal = new Dictionary<int, int>(_netOfSignal.Count);
            foreach (KeyValuePair<int, int> entry in _netOfSignal)
                if (netId[entry.Value] >= 0)
                    bySignal[entry.Key] = netId[entry.Value];
            _netOfSignal.Clear();
            foreach (KeyValuePair<int, int> entry in bySignal)
                _netOfSignal[entry.Key] = entry.Value;
            _nets.Clear();
            _nets.AddRange(nets);
            _cells.Clear();
            _cells.AddRange(cells);
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
                if (_design.Time is SignalId time && time.Index == signal)
                    throw new NotHardware("It reads $time, the simulation's own clock: behaviour, not a circuit.");
                _readHere?.Add(signal);
                if (!_keeping)
                    _readInExpr?.Add(signal);
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
                    gate.Delay = g.Delay;
                    if (IsTriState(g.Kind) && g.Delay.TurnOff != Math.Min(g.Delay.Rise, g.Delay.Fall))
                        _unshown.Add("the delay to z of " + name);
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
            cell.Delay = OneGatesDelay(instance);
        }

        /// <summary>
        /// The delay of the one gate an instance is, when it is nothing but one gate -- a cell of a library, <c>AND2 #(2, 3)</c>,
        /// whose delays are its parameters: what a drawing keeps on the gate it draws for it. Zero for anything else.
        /// </summary>
        private Delay OneGatesDelay(int instance)
        {
            if (_oneGate == null)
            {
                _oneGate = new Dictionary<int, Delay>();
                var count = new Dictionary<int, int>();
                foreach (DriverInfo d in _design.Drivers)
                {
                    int scope = WrittenIn(d);
                    count[scope] = count.TryGetValue(scope, out int n) ? n + 1 : 1;
                    if (d is GateDriver)
                        _oneGate[scope] = d.Delay;
                }
                foreach (KeyValuePair<int, int> scope in count)
                    if (scope.Value != 1)
                        _oneGate.Remove(scope.Key);
                for (int p = 0; p < _design.Processes.Count; p++)
                    _oneGate.Remove(ScopeOf(_design.Processes[p].Name));
            }
            return _oneGate.TryGetValue(instance, out Delay delay) ? delay : Delay.Zero;
        }

        private Dictionary<int, Delay> _oneGate;

        private static bool IsTriState(GateKind kind) =>
            kind == GateKind.Bufif0 || kind == GateKind.Bufif1 || kind == GateKind.Notif0 || kind == GateKind.Notif1;

        /// <summary>A process opened into cells; one that is no hardware, a box -- every signal it reads in, every one it writes out.</summary>
        private void Box(ProcessInfo process, string why)
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

            string name = Relative(process.Name);
            RtlCell cell = NewCellDriving(RtlKind.Process, name, inputs.ToArray(), outputs.ToArray());
            cell.InputNames = inputNames.ToArray();
            cell.OutputNames = outputNames.ToArray();
            cell.Why = why;

            var written = new List<int>();
            foreach (int output in outputs)
                written.Add(_nets[output].Signal);
            _processes.Add(new RtlProcessRecord(name, false, why, written.ToArray()));
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

            // Whether it may be the signed operation, as far as the netlist can tell: a value's sign is the engine's to
            // know as it runs, so what is not shown to be unsigned may be signed (the review of #110: a signed a < b was
            // opened into an unsigned comparator's gates, which disagreed with the code's value out of them).
            bool shift = op == BinaryOp.ShiftLeft || op == BinaryOp.ShiftRight || op == BinaryOp.ArithmeticShiftLeft || op == BinaryOp.ArithmeticShiftRight;
            cell.Signed = shift ? !IsUnsigned(a) : !IsUnsigned(a) && !IsUnsigned(b);
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
            if (_overlay != null && memory != null)
                _memoriesRead?.Add(memory);
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
