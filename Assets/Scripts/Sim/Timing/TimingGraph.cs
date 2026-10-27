using System;
using System.Collections.Generic;
using BitSorter.SystemVerilog;
using BitSorter.SystemVerilog.Syntax;

namespace BitSorter.Sim
{
    /// <summary>How a driver passes a change on (Part 14a): the same way, the other way, or either, as its other inputs decide.</summary>
    public enum Sense
    {
        /// <summary>A rise in gives a rise out: AND, OR, BUF.</summary>
        Positive,

        /// <summary>A rise in gives a fall out: NOT, NAND, NOR.</summary>
        Negative,

        /// <summary>Either, as the other inputs stand: XOR, XNOR, a tri-state's control, an assignment.</summary>
        Either,
    }

    /// <summary>
    /// One way a change travels: from a signal a driver reads to the signal it drives, at the driver's rise (<c>Tpdlh</c>,
    /// the output going to 1) or fall (<c>Tpdhl</c>, to 0).
    /// </summary>
    public sealed class TimingArc
    {
        internal TimingArc(int from, int to, string through, GateKind? gate, int inputs, long rise, long fall, Sense sense, bool wire)
        {
            From = from;
            To = to;
            Through = through;
            Gate = gate;
            Inputs = inputs;
            Rise = rise;
            Fall = fall;
            Sense = sense;
            IsWire = wire;
        }

        /// <summary>The signal it reads, by its index in <see cref="Design.Signals"/>.</summary>
        public int From { get; }

        /// <summary>The signal it drives.</summary>
        public int To { get; }

        /// <summary>The gate's instance name, an assignment's, or a process's.</summary>
        public string Through { get; }

        /// <summary>The gate's primitive; null for an assignment or a process.</summary>
        public GateKind? Gate { get; }

        /// <summary>How many inputs the gate has; 0 for anything else.</summary>
        public int Inputs { get; }

        public long Rise { get; }
        public long Fall { get; }
        public Sense Sense { get; }

        /// <summary>
        /// Whether it only joins two names of one net -- a port to what it is connected to, at no delay. A path goes through
        /// it, but lists the gates it passes, not this.
        /// </summary>
        public bool IsWire { get; }

        /// <summary>The delay the change takes when the output goes to <paramref name="one"/>.</summary>
        public long Delay(bool one) => one ? Rise : Fall;
    }

    /// <summary>What a register costs a path besides its logic (Part 14a): the clock to Q, and the setup its input needs.</summary>
    public readonly struct RegisterTiming
    {
        public RegisterTiming(long tcq, long tsetup)
        {
            Tcq = tcq;
            Tsetup = tsetup;
        }

        /// <summary>From the clock's edge to the output's change.</summary>
        public long Tcq { get; }

        /// <summary>How long before the edge the input must be still.</summary>
        public long Tsetup { get; }
    }

    /// <summary>A register as the timing sees it: an edge-triggered process, what it captures and what it launches.</summary>
    public sealed class TimingRegister
    {
        internal TimingRegister(string name, int[] captures, int[] launches, RegisterTiming timing)
        {
            Name = name;
            Captures = captures;
            Launches = launches;
            Timing = timing;
        }

        /// <summary>The process's name: <c>counter.always_ff@4</c>.</summary>
        public string Name { get; }

        /// <summary>The signals its edge takes in: what its statements read, its clock and asynchronous reset left out.</summary>
        public IReadOnlyList<int> Captures { get; }

        /// <summary>The signals it writes: where a path starts at the clock's edge, <see cref="RegisterTiming.Tcq"/> after it.</summary>
        public IReadOnlyList<int> Launches { get; }

        public RegisterTiming Timing { get; }
    }

    /// <summary>
    /// A design's timing, as the course reckons it (Part 14a): every signal a node, and every way a change travels from one
    /// to another an arc -- a gate at its <c>Tpdlh</c> and <c>Tpdhl</c>, an assignment at its delay, a combinational
    /// process at none -- with each register an edge-triggered process where paths start and end. Built from the flattened
    /// <see cref="Design"/>, so a drawn module and a written one are reckoned alike.
    /// </summary>
    /// <remarks>
    /// Signals, not bits: a vector is one node, which is how the course draws a bus. A loop of logic -- a latch made of two
    /// gates -- is found (<see cref="Loops"/>) and its arcs left out of the paths, since a change around it never ends.
    /// </remarks>
    public sealed class TimingGraph
    {
        private readonly List<TimingArc> _arcs = new List<TimingArc>();
        private readonly List<int>[] _out;
        private readonly bool[] _inLoop;

        private TimingGraph(Design design)
        {
            Design = design;
            _out = new List<int>[design.Signals.Count];
            _inLoop = new bool[design.Signals.Count];
        }

        public Design Design { get; }

        /// <summary>The top's input ports, by signal index.</summary>
        public IReadOnlyList<int> Inputs { get; private set; } = Array.Empty<int>();

        /// <summary>The top's output ports.</summary>
        public IReadOnlyList<int> Outputs { get; private set; } = Array.Empty<int>();

        public IReadOnlyList<TimingArc> Arcs => _arcs;

        public IReadOnlyList<TimingRegister> Registers { get; private set; } = Array.Empty<TimingRegister>();

        /// <summary>Each loop of logic found, as its signals' names; its arcs are no part of any path.</summary>
        public IReadOnlyList<IReadOnlyList<string>> Loops { get; private set; } = Array.Empty<IReadOnlyList<string>>();

        /// <summary>How many gates of each primitive and inputs: <c>nand2</c>, <c>not</c>.</summary>
        public IReadOnlyDictionary<string, int> GateCounts { get; private set; } = new Dictionary<string, int>();

        /// <summary>Every gate, of every kind.</summary>
        public int Gates { get; private set; }

        /// <summary>The arcs leaving <paramref name="signal"/>, by their index in <see cref="Arcs"/>, loops' left out.</summary>
        internal IReadOnlyList<int> Leaving(int signal) => (IReadOnlyList<int>)_out[signal] ?? Array.Empty<int>();

        /// <summary>
        /// The timing of <paramref name="design"/>, its top's ports where paths start and end. <paramref name="registers"/>
        /// gives a register's Tcq and Tsetup by the full name of a signal it writes; one not given takes its Tcq from a delayed
        /// nonblocking write (<c>q &lt;= #2 d</c>) and no setup.
        /// </summary>
        public static TimingGraph Of(Design design, IReadOnlyDictionary<string, RegisterTiming> registers = null)
        {
            if (design == null)
                throw new ArgumentNullException(nameof(design));

            var graph = new TimingGraph(design);
            graph.FindPorts();
            graph.AddDrivers();
            graph.AddProcesses(registers);
            graph.FindLoops();
            return graph;
        }

        private void FindPorts()
        {
            var inputs = new List<int>();
            var outputs = new List<int>();
            int top = Design.Scopes.Count > 0 ? 0 : -1;
            for (int s = 0; s < Design.Signals.Count; s++)
            {
                SignalInfo info = Design.Signals[s];
                if (info.Port == null || (top >= 0 && info.Scope != top))
                    continue;
                if (info.Port == PortDirection.Input)
                    inputs.Add(s);
                else if (info.Port == PortDirection.Output)
                    outputs.Add(s);
            }

            Inputs = inputs;
            Outputs = outputs;
        }

        private void AddDrivers()
        {
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            var reads = new List<int>();
            foreach (DriverInfo driver in Design.Drivers)
            {
                int to = driver.Target.Index;
                if (driver is GateDriver gate)
                {
                    Gates++;
                    string key = gate.Kind.ToString().ToLowerInvariant() + (gate.InputCount > 1 && !IsTriState(gate.Kind) ? gate.InputCount.ToString(System.Globalization.CultureInfo.InvariantCulture) : "");
                    counts[key] = counts.TryGetValue(key, out int n) ? n + 1 : 1;

                    for (int i = 0; i < gate.InputCount; i++)
                    {
                        reads.Clear();
                        gate.Input(i).CollectReads(reads);
                        foreach (int from in reads)
                            Add(new TimingArc(from, to, gate.Name, gate.Kind, gate.InputCount, gate.Delay.Rise, gate.Delay.Fall, SenseOf(gate.Kind, i), wire: false));
                    }
                }
                else if (driver is AssignDriver assign)
                {
                    bool wire = assign.Delay.Rise == 0 && assign.Delay.Fall == 0 && assign.Value.TryRead(out _);
                    reads.Clear();
                    assign.Value.CollectReads(reads);
                    foreach (int from in reads)
                        Add(new TimingArc(from, to, assign.Name, null, 0, assign.Delay.Rise, assign.Delay.Fall, wire ? Sense.Positive : Sense.Either, wire));
                }
            }

            GateCounts = counts;
        }

        /// <summary>
        /// An edge-triggered process is a register; one that waits on no edge -- <c>always_comb</c>, <c>always @*</c> -- is
        /// logic at no delay, each signal it reads reaching each it writes. An <c>initial</c> is a testbench's, and no part.
        /// </summary>
        private void AddProcesses(IReadOnlyDictionary<string, RegisterTiming> given)
        {
            var registers = new List<TimingRegister>();
            var reads = new HashSet<int>();
            var edges = new HashSet<int>();
            foreach (ProcessInfo process in Design.Processes)
            {
                if (process.Kind == "initial" || process.Writes.Length == 0)
                    continue;

                reads.Clear();
                edges.Clear();
                bool edgeTriggered = false;
                long tcq = 0;
                foreach (Instruction ins in process.Code)
                {
                    ins.Value?.CollectReads(reads);
                    if (ins.Op != OpCode.AssignLater)
                        ins.Other?.CollectReads(reads);
                    else if (ins.Other != null)
                        tcq = Math.Max(tcq, ConstantOf(ins.Other));
                    foreach (LPiece piece in ins.Target ?? Array.Empty<LPiece>())
                    {
                        piece.Offset?.CollectReads(reads);
                        piece.Word?.CollectReads(reads);
                    }

                    if (ins.Wait == null)
                        continue;
                    foreach (EventItem item in ins.Wait.Items)
                    {
                        if (item.Edge == EdgeKind.Any)
                            continue;
                        edgeTriggered = true;
                        item.Value.CollectReads(edges);
                    }
                }

                if (!edgeTriggered)
                {
                    foreach (int to in process.Writes)
                        foreach (int from in reads)
                            if (from != to)
                                Add(new TimingArc(from, to, process.Name, null, 0, 0, 0, Sense.Either, wire: false));
                    continue;
                }

                var captures = new List<int>();
                foreach (int r in reads)
                    if (!edges.Contains(r))
                        captures.Add(r);
                captures.Sort();

                RegisterTiming timing = new RegisterTiming(tcq, 0);
                if (given != null)
                    foreach (int w in process.Writes)
                        if (given.TryGetValue(Design.Signals[w].Name, out RegisterTiming named))
                        {
                            timing = named;
                            break;
                        }

                registers.Add(new TimingRegister(process.Name, captures.ToArray(), (int[])process.Writes.Clone(), timing));
            }

            Registers = registers;
        }

        /// <summary>A delay's value when it is a constant, as a register's <c>#2</c> is; 0 when it reads anything.</summary>
        private static long ConstantOf(Expr delay)
        {
            var reads = new List<int>();
            delay.CollectReads(reads);
            if (reads.Count > 0)
                return 0;
            return delay.Evaluate(Array.Empty<LogicVec>()).TryGetUInt64(out ulong value) && value <= long.MaxValue ? (long)value : 0;
        }

        private void Add(TimingArc arc)
        {
            _out[arc.From] ??= new List<int>();
            _out[arc.From].Add(_arcs.Count);
            _arcs.Add(arc);
        }

        /// <summary>
        /// Loops of logic, by Tarjan's strongly connected components over the arcs, walked without recursion so a long chain
        /// of gates cannot take the stack. An arc inside a loop is taken out of what <see cref="Leaving"/> gives.
        /// </summary>
        private void FindLoops()
        {
            int count = Design.Signals.Count;
            var index = new int[count];
            var low = new int[count];
            var onStack = new bool[count];
            for (int i = 0; i < count; i++)
                index[i] = -1;
            var stack = new Stack<int>();
            var work = new Stack<(int Node, int Next)>();
            var loops = new List<IReadOnlyList<string>>();
            var component = new int[count];
            int next = 0;

            for (int root = 0; root < count; root++)
            {
                if (index[root] >= 0)
                    continue;
                work.Push((root, 0));
                while (work.Count > 0)
                {
                    (int node, int at) = work.Pop();
                    if (at == 0)
                    {
                        index[node] = low[node] = next++;
                        stack.Push(node);
                        onStack[node] = true;
                    }

                    IReadOnlyList<int> leaving = Leaving(node);
                    bool descended = false;
                    for (int k = at; k < leaving.Count; k++)
                    {
                        int to = _arcs[leaving[k]].To;
                        if (index[to] < 0)
                        {
                            work.Push((node, k + 1));
                            work.Push((to, 0));
                            descended = true;
                            break;
                        }

                        if (onStack[to])
                            low[node] = Math.Min(low[node], index[to]);
                    }

                    if (descended)
                        continue;

                    if (low[node] == index[node])
                    {
                        var members = new List<int>();
                        int member;
                        do
                        {
                            member = stack.Pop();
                            onStack[member] = false;
                            members.Add(member);
                        }
                        while (member != node);

                        bool selfLoop = members.Count == 1 && Exists(Leaving(node), a => _arcs[a].To == node);
                        if (members.Count > 1 || selfLoop)
                        {
                            members.Sort();
                            var names = new List<string>(members.Count);
                            foreach (int m in members)
                            {
                                _inLoop[m] = true;
                                component[m] = loops.Count + 1;
                                names.Add(Design.Signals[m].Name);
                            }

                            loops.Add(names);
                        }
                    }

                    if (work.Count > 0)
                    {
                        int parent = work.Peek().Node;
                        low[parent] = Math.Min(low[parent], low[node]);
                    }
                }
            }

            Loops = loops;
            if (loops.Count == 0)
                return;

            // Inside one loop an arc goes round it: those are left out. An arc into or out of a loop stays.
            for (int s = 0; s < count; s++)
            {
                if (_out[s] == null || !_inLoop[s])
                    continue;
                _out[s].RemoveAll(a => _inLoop[_arcs[a].To] && component[_arcs[a].To] == component[s]);
            }
        }

        private static bool Exists(IReadOnlyList<int> items, Func<int, bool> test)
        {
            foreach (int item in items)
                if (test(item))
                    return true;
            return false;
        }

        private static bool IsTriState(GateKind kind) =>
            kind == GateKind.Bufif0 || kind == GateKind.Bufif1 || kind == GateKind.Notif0 || kind == GateKind.Notif1;

        /// <summary>How a change at a gate's <paramref name="input"/>th input reaches its output.</summary>
        internal static Sense SenseOf(GateKind kind, int input)
        {
            switch (kind)
            {
                case GateKind.And:
                case GateKind.Or:
                case GateKind.Buf:
                    return Sense.Positive;
                case GateKind.Nand:
                case GateKind.Nor:
                case GateKind.Not:
                    return Sense.Negative;
                case GateKind.Bufif0:
                case GateKind.Bufif1:
                    return input == 0 ? Sense.Positive : Sense.Either;
                case GateKind.Notif0:
                case GateKind.Notif1:
                    return input == 0 ? Sense.Negative : Sense.Either;
                default:
                    return Sense.Either;
            }
        }
    }
}
