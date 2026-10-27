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

        /// <summary>The bit it reads, by its node in the graph (<see cref="TimingGraph.NameOf"/>).</summary>
        public int From { get; }

        /// <summary>The bit it drives.</summary>
        public int To { get; }

        /// <summary>The gate's instance name, an assignment's, or a process's: what drives it.</summary>
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

        /// <summary>The bits its edge takes in: what its statements read, its clock and asynchronous reset left out.</summary>
        public IReadOnlyList<int> Captures { get; }

        /// <summary>The bits it writes: where a path starts at the clock's edge, <see cref="RegisterTiming.Tcq"/> after it.</summary>
        public IReadOnlyList<int> Launches { get; }

        public RegisterTiming Timing { get; }
    }

    /// <summary>
    /// A design's timing, as the course reckons it (Part 14a): every bit of every signal a node, and every way a change
    /// travels from one to another an arc -- a gate at its <c>Tpdlh</c> and <c>Tpdhl</c>, an assignment at its delay, a
    /// combinational process at none -- with each register an edge-triggered process where paths start and end. Built from
    /// the flattened <see cref="Design"/>, so a drawn module and a written one are reckoned alike.
    /// </summary>
    /// <remarks>
    /// **Bits, not signals.** A carry passed from <c>c[0]</c> to <c>c[1]</c> through a gate is one bit to another; were a
    /// vector one node, a ripple adder's carries -- a bus fed bit by bit from itself -- would be a loop, and its gates no
    /// part of any path. Which bit of a value depends on which, and which way round a change passes, is the expressions'
    /// own (<see cref="BitDeps"/>): <c>~a</c> inverts, <c>a[0]</c> selects, <c>a + b</c> carries upwards.
    ///
    /// **A junction** stands between many bits and many where every one depends on every one -- a combinational process,
    /// a value too wide to follow -- so they cost an arc each rather than one for every pair. It is a node of no signal,
    /// named for its driver. A loop of logic -- a latch made of two gates -- is found (<see cref="Loops"/>) and its arcs
    /// left out of the paths, since a change around it never ends.
    /// </remarks>
    public sealed class TimingGraph
    {
        /// <summary>The most bits a design's timing is reckoned for: one wider is refused, rather than taking all the memory.</summary>
        public const int MaxBits = 1 << 20;

        /// <summary>The most arcs a driver gives its bits one by one: past it they meet at a junction.</summary>
        private const int MostArcsApart = 4096;

        private readonly List<TimingArc> _arcs = new List<TimingArc>();
        private readonly List<List<int>> _out = new List<List<int>>();
        private readonly Dictionary<(int, int, string), int> _arcAt = new Dictionary<(int, int, string), int>();
        private readonly List<string> _junctions = new List<string>();
        private readonly int[] _first;
        private bool[] _inLoop;

        private TimingGraph(Design design)
        {
            Design = design;
            _first = new int[design.Signals.Count + 1];
            for (int s = 0; s < design.Signals.Count; s++)
                _first[s + 1] = _first[s] + design.Signals[s].Width;
            for (int n = 0; n < _first[design.Signals.Count]; n++)
                _out.Add(null);
        }

        public Design Design { get; }

        /// <summary>How many nodes there are: every bit of every signal, and the junctions after them.</summary>
        public int NodeCount => _out.Count;

        /// <summary>Every bit of the top's input ports, by node.</summary>
        public IReadOnlyList<int> Inputs { get; private set; } = Array.Empty<int>();

        /// <summary>Every bit of the top's output ports.</summary>
        public IReadOnlyList<int> Outputs { get; private set; } = Array.Empty<int>();

        public IReadOnlyList<TimingArc> Arcs => _arcs;

        public IReadOnlyList<TimingRegister> Registers { get; private set; } = Array.Empty<TimingRegister>();

        /// <summary>Each loop of logic found, as its nodes' names; its arcs are no part of any path.</summary>
        public IReadOnlyList<IReadOnlyList<string>> Loops { get; private set; } = Array.Empty<IReadOnlyList<string>>();

        /// <summary>
        /// Whether a delay on some path is the run's to know -- a process's <c>#</c> that reads a signal or calls a function --
        /// so the path may take longer than reckoned, which a measuring run must not trust its settle time to (the 14c review).
        /// </summary>
        public bool UnknownDelays { get; private set; }

        /// <summary>How many gates of each primitive and inputs: <c>nand2</c>, <c>not</c>.</summary>
        public IReadOnlyDictionary<string, int> GateCounts { get; private set; } = new Dictionary<string, int>();

        /// <summary>Every gate, of every kind.</summary>
        public int Gates { get; private set; }

        /// <summary>The bits <paramref name="design"/>'s signals have together: what <see cref="MaxBits"/> is counted against.</summary>
        public static long BitsOf(Design design)
        {
            long bits = 0;
            foreach (SignalInfo signal in design.Signals)
                bits += signal.Width;
            return bits;
        }

        /// <summary>The signal <paramref name="node"/> is a bit of, by index; -1 for a junction.</summary>
        public int SignalOf(int node)
        {
            int signals = Design.Signals.Count;
            if (node < 0 || node >= _first[signals])
                return -1;
            int lo = 0, hi = signals - 1;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (_first[mid] <= node)
                    lo = mid;
                else
                    hi = mid - 1;
            }
            return lo;
        }

        /// <summary>The node of bit <paramref name="bit"/> of <paramref name="signal"/>, counted from its value's bit 0.</summary>
        public int NodeOf(int signal, int bit = 0) => _first[signal] + bit;

        /// <summary>
        /// What a node is called: its signal's full name, with the bit as the source counts it when the signal is a vector --
        /// <c>adder.c[1]</c> -- or, for a junction, its driver's.
        /// </summary>
        public string NameOf(int node)
        {
            int signal = SignalOf(node);
            if (signal < 0)
                return node >= 0 && node < NodeCount ? _junctions[node - _first[Design.Signals.Count]] : "";
            return BitName(Design.Signals[signal], node - _first[signal]);
        }

        /// <summary>
        /// Bit <paramref name="bit"/> of <paramref name="info"/>'s value as the source names it: its name alone for a one-bit
        /// signal, and with the index its range gives that bit otherwise -- <c>a[3]</c> is bit 3 of <c>logic [3:0] a</c>'s
        /// value, but bit 0 of <c>logic [0:3] a</c>'s. What a step test names its bits by too, so the two reckonings agree.
        /// </summary>
        public static string BitName(SignalInfo info, int bit)
        {
            if (info.Width == 1)
                return info.Name;
            int index = info.Msb >= info.Lsb ? info.Lsb + bit : info.Lsb - bit;
            return info.Name + "[" + index.ToString(System.Globalization.CultureInfo.InvariantCulture) + "]";
        }

        /// <summary>The arcs leaving <paramref name="node"/>, by their index in <see cref="Arcs"/>, loops' left out.</summary>
        internal IReadOnlyList<int> Leaving(int node) => (IReadOnlyList<int>)_out[node] ?? Array.Empty<int>();

        /// <summary>
        /// The timing of <paramref name="design"/>, its top's ports where paths start and end. <paramref name="registers"/>
        /// gives a register's Tcq and Tsetup by the full name of a signal it writes; one not given takes its Tcq from a delayed
        /// nonblocking write (<c>q &lt;= #2 d</c>) and no setup. A design of more than <see cref="MaxBits"/> bits is refused.
        /// </summary>
        public static TimingGraph Of(Design design, IReadOnlyDictionary<string, RegisterTiming> registers = null)
        {
            if (design == null)
                throw new ArgumentNullException(nameof(design));
            if (BitsOf(design) > MaxBits)
                throw new ArgumentException("A design of more than " + MaxBits + " bits is too wide to reckon bit by bit.", nameof(design));

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
                List<int> into = info.Port == PortDirection.Input ? inputs : info.Port == PortDirection.Output ? outputs : null;
                for (int b = 0; into != null && b < info.Width; b++)
                    into.Add(_first[s] + b);
            }

            Inputs = inputs;
            Outputs = outputs;
        }

        private void AddDrivers()
        {
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (DriverInfo driver in Design.Drivers)
            {
                int to = _first[driver.Target.Index] + driver.TargetLowest;
                if (driver is GateDriver gate)
                {
                    Gates++;
                    string key = gate.Kind.ToString().ToLowerInvariant() + (gate.InputCount > 1 && !IsTriState(gate.Kind) ? gate.InputCount.ToString(System.Globalization.CultureInfo.InvariantCulture) : "");
                    counts[key] = counts.TryGetValue(key, out int n) ? n + 1 : 1;

                    for (int i = 0; i < gate.InputCount; i++)
                    {
                        // A tri-state's control can turn its output off: that change takes the turn-off delay, to z.
                        long rise = gate.Delay.Rise, fall = gate.Delay.Fall;
                        if (IsTriState(gate.Kind) && i == 1)
                        {
                            rise = Math.Max(rise, gate.Delay.TurnOff);
                            fall = Math.Max(fall, gate.Delay.TurnOff);
                        }

                        Sense through = SenseOf(gate.Kind, i);
                        SourceSet sources = gate.Input(i).Depends(_first).Of(0);
                        for (int k = 0; k < sources.Count; k++)
                            Add(new TimingArc(sources[k].Node, to, gate.Name, gate.Kind, gate.InputCount, rise, fall,
                                SourceSet.Compose(through, sources[k].Sense), wire: false));
                    }
                }
                else if (driver is AssignDriver assign)
                {
                    // A value that can be z lets go after the turn-off delay, as a tri-state's control does: the latest of them.
                    BitDeps value = assign.Value.Depends(_first);
                    long rise = Math.Max(assign.Delay.Rise, assign.Delay.TurnOff), fall = Math.Max(assign.Delay.Fall, assign.Delay.TurnOff);
                    bool wire = value.Routing && rise == 0 && fall == 0;
                    Spread(value, to, driver.TargetWidth, assign.Name, rise, fall, wire);
                }
            }

            GateCounts = counts;
        }

        /// <summary>
        /// Arcs from what each bit of <paramref name="value"/> depends on to the <paramref name="width"/> bits from
        /// <paramref name="to"/> on, through <paramref name="through"/> -- one by one, or through a junction where every bit
        /// depends on so much that pairing them would cost more.
        /// </summary>
        private void Spread(BitDeps value, int to, int width, string through, long rise, long fall, bool wire)
        {
            long apart = 0;
            for (int i = 0; i < width && apart <= MostArcsApart; i++)
                apart += value.Of(i).Count;

            if (apart > MostArcsApart && (long)value.Union.Count + width < apart)
            {
                int junction = Junction(through);
                SourceSet all = value.Union;
                for (int k = 0; k < all.Count; k++)
                    Add(new TimingArc(all[k].Node, junction, through, null, 0, rise, fall, all[k].Sense, wire));
                for (int i = 0; i < width; i++)
                    Add(new TimingArc(junction, to + i, through, null, 0, 0, 0, Sense.Positive, wire: true));
                return;
            }

            for (int i = 0; i < width; i++)
            {
                SourceSet sources = value.Of(i);
                for (int k = 0; k < sources.Count; k++)
                    Add(new TimingArc(sources[k].Node, to + i, through, null, 0, rise, fall, sources[k].Sense, wire));
            }
        }

        private int Junction(string name)
        {
            _junctions.Add(name);
            _out.Add(null);
            return _out.Count - 1;
        }

        /// <summary>
        /// An edge-triggered process is a register; one that waits on no edge -- <c>always_comb</c>, <c>always @*</c> -- is
        /// logic at no delay, every bit it reads from outside reaching every bit it writes, through a junction. What it both
        /// writes and reads is its own working -- <c>y = y ^ a[i]</c> -- and no loop, and a loop's own variable no part of
        /// anything. An <c>initial</c> is a testbench's, and no part.
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
                long waited = 0, later = 0;
                bool unknown = false;
                foreach (Instruction ins in process.Code)
                {
                    if (ins.Op != OpCode.Delay)
                        ins.Value?.CollectReads(reads);
                    else if (TryConstantOf(ins.Value, out long wait))
                        waited += wait;
                    else
                        unknown = true;
                    if (ins.Op != OpCode.AssignLater)
                        ins.Other?.CollectReads(reads);
                    else if (TryConstantOf(ins.Other, out long after))
                        later = Math.Max(later, after);
                    else
                        unknown = true;
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

                var writes = new List<int>();
                foreach (int w in process.Writes)
                    if (!Design.Signals[w].Local)
                        writes.Add(w);

                if (!edgeTriggered)
                {
                    AddLogic(process, writes, waited + later, unknown);
                    continue;
                }
                UnknownDelays |= unknown;
                long tcq = waited + later;

                var captures = new List<int>();
                foreach (int r in reads)
                    if (!edges.Contains(r) && !Design.Signals[r].Local)
                        for (int b = _first[r]; b < _first[r + 1]; b++)
                            captures.Add(b);
                captures.Sort();

                var launches = new List<int>();
                foreach (int w in writes)
                    for (int b = _first[w]; b < _first[w + 1]; b++)
                        launches.Add(b);

                RegisterTiming timing = new RegisterTiming(tcq, 0);
                if (given != null)
                    foreach (int w in process.Writes)
                        if (given.TryGetValue(Design.Signals[w].Name, out RegisterTiming named))
                        {
                            timing = named;
                            break;
                        }

                registers.Add(new TimingRegister(process.Name, captures.ToArray(), launches.ToArray(), timing));
            }

            Registers = registers;
        }

        /// <summary>
        /// A combinational process: each signal it writes reached from what its own assignments read and what its decisions
        /// -- its <c>if</c>s, <c>case</c>s and loop conditions -- read, through a junction, at no delay. What it writes and
        /// then reads is followed through to what that was worked out from, so <c>t = a &amp; b; y = t | c;</c> gives y a, b
        /// and c; what it reads of its own before writing it -- <c>y = y ^ a[i]</c> -- is its own working, and a loop's
        /// variable is no part of anything.
        /// </summary>
        /// <remarks>
        /// **Its delay is every <c>#</c> it waits and its latest <c>&lt;= #</c>** (<paramref name="delay"/>), on the way in:
        /// <c>always @* #2 y = a &amp; b;</c> takes 2 from a to y. A delay the run is to know is flagged
        /// (<see cref="UnknownDelays"/>) when anything here is reached through it.
        ///
        /// **Per signal, not the process whole.** The course's processor sets its ALU's operands in one <c>always_comb</c>
        /// and reads the ALU's result in the same block for another output: every read to every write made the operands
        /// depend on the result -- a loop through the ALU, 194 bits of it, and its minimum period 0 (the 14b review). Signals
        /// whose writes read the same things share one junction.
        /// </remarks>
        private void AddLogic(ProcessInfo process, List<int> writes, long delay, bool unknown)
        {
            var own = new HashSet<int>(process.Writes);
            var direct = new Dictionary<int, HashSet<int>>();
            var decisions = new HashSet<int>();
            var reads = new List<int>();
            foreach (Instruction ins in process.Code)
            {
                switch (ins.Op)
                {
                    case OpCode.Assign:
                    case OpCode.AssignLater:
                    case OpCode.Force:
                        reads.Clear();
                        ins.Value?.CollectReads(reads);
                        foreach (LPiece piece in ins.Target ?? Array.Empty<LPiece>())
                        {
                            // Where a write lands is read as well: a word picked by an address depends on the address.
                            piece.Offset?.CollectReads(reads);
                            piece.Word?.CollectReads(reads);
                        }
                        foreach (LPiece piece in ins.Target ?? Array.Empty<LPiece>())
                            for (int w = 0; w < Math.Max(1, piece.Word != null ? piece.Depth : 1); w++)
                            {
                                int target = piece.Signal + w;
                                if (!direct.TryGetValue(target, out HashSet<int> from))
                                    direct[target] = from = new HashSet<int>();
                                from.UnionWith(reads);
                            }
                        break;

                    case OpCode.JumpIf:
                    case OpCode.JumpUnless:
                    case OpCode.JumpIfMatch:
                    case OpCode.WaitUntil:
                        reads.Clear();
                        ins.Value?.CollectReads(reads);
                        ins.Other?.CollectReads(reads);
                        decisions.UnionWith(reads);
                        break;
                }
            }

            // What each write is worked out from, from outside: its own reads and the decisions', what it read of the
            // process's own writes followed through to theirs.
            var junctions = new Dictionary<string, int>(StringComparer.Ordinal);
            var seen = new HashSet<int>();
            var todo = new Stack<int>();
            var outside = new List<int>();
            foreach (int w in writes)
            {
                seen.Clear();
                outside.Clear();
                todo.Clear();
                seen.Add(w);
                if (direct.TryGetValue(w, out HashSet<int> first))
                    foreach (int r in first)
                        todo.Push(r);
                foreach (int r in decisions)
                    todo.Push(r);
                while (todo.Count > 0)
                {
                    int r = todo.Pop();
                    if (!seen.Add(r))
                        continue;
                    if (own.Contains(r))
                    {
                        if (direct.TryGetValue(r, out HashSet<int> through))
                            foreach (int deeper in through)
                                todo.Push(deeper);
                        continue;
                    }
                    if (!Design.Signals[r].Local)
                        outside.Add(r);
                }
                if (outside.Count == 0)
                    continue;

                outside.Sort();
                string key = string.Join(",", outside);
                if (!junctions.TryGetValue(key, out int junction))
                {
                    junctions[key] = junction = Junction(process.Name);
                    foreach (int r in outside)
                        for (int b = _first[r]; b < _first[r + 1]; b++)
                            Add(new TimingArc(b, junction, process.Name, null, 0, delay, delay, Sense.Either, wire: false));
                    UnknownDelays |= unknown;
                }
                for (int b = _first[w]; b < _first[w + 1]; b++)
                    Add(new TimingArc(junction, b, process.Name, null, 0, 0, 0, Sense.Positive, wire: true));
            }
        }

        /// <summary>
        /// A delay's value when it is a constant, as a register's <c>#2</c> is; 0 when it reads anything or calls a function,
        /// whose value is the run's to know.
        /// </summary>
        private static long ConstantOf(Expr delay) => TryConstantOf(delay, out long value) ? value : 0;

        /// <summary>Whether <paramref name="delay"/> is a constant, and its value when it is.</summary>
        private static bool TryConstantOf(Expr delay, out long constant)
        {
            constant = 0;
            if (delay == null)
                return true;
            var reads = new List<int>();
            delay.CollectReads(reads);
            var calls = new List<FunctionInfo>();
            delay.CollectCalls(calls);
            if (reads.Count > 0 || calls.Count > 0)
                return false;

            LogicVec value;
            try
            {
                value = delay.Evaluate(Array.Empty<LogicVec>());
            }
            catch (Exception e) when (e is IndexOutOfRangeException || e is InvalidOperationException || e is NullReferenceException)
            {
                // Whatever else it reads -- a system function's state -- is no constant either.
                return false;
            }

            if (!value.TryGetUInt64(out ulong at) || at > long.MaxValue)
                return false;
            constant = (long)at;
            return true;
        }

        /// <summary>
        /// An arc, or -- when the same driver already takes the same bit to the same bit, as <c>and g (y, a, a)</c> does --
        /// that arc made the latest of both, and either way round if they differ, so no path is walked twice.
        /// </summary>
        private void Add(TimingArc arc)
        {
            var key = (arc.From, arc.To, arc.Through);
            if (_arcAt.TryGetValue(key, out int at))
            {
                TimingArc was = _arcs[at];
                Sense sense = was.Sense == arc.Sense ? was.Sense : Sense.Either;
                _arcs[at] = new TimingArc(was.From, was.To, was.Through, was.Gate, was.Inputs, Math.Max(was.Rise, arc.Rise),
                    Math.Max(was.Fall, arc.Fall), sense, was.IsWire && arc.IsWire);
                return;
            }

            _arcAt[key] = _arcs.Count;
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
            int count = NodeCount;
            _inLoop = new bool[count];
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
                if (index[root] >= 0 || _out[root] == null)
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
                                names.Add(NameOf(m));
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
