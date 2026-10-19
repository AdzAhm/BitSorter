using System;
using System.Collections.Generic;
using BitSorter.SystemVerilog;
using BitSorter.SystemVerilog.Syntax;

namespace BitSorter.Sim
{
    /// <summary>A signal in a design, by its index there.</summary>
    public readonly struct SignalId : IEquatable<SignalId>
    {
        public readonly int Index;

        internal SignalId(int index) => Index = index;

        public bool Equals(SignalId other) => Index == other.Index;
        public override bool Equals(object obj) => obj is SignalId other && Equals(other);
        public override int GetHashCode() => Index;
        public override string ToString() => $"signal {Index}";
    }

    /// <summary>
    /// A net or a variable, as SystemVerilog has both: they read the same but are driven differently.
    /// </summary>
    public enum SignalKind
    {
        /// <summary>
        /// A <c>wire</c>: its value is what its drivers resolve to, z when nothing drives it, and any
        /// number may drive it.
        /// </summary>
        Net,

        /// <summary>
        /// A <c>logic</c> (or <c>reg</c>): it holds what was last written, x until then, and at most one
        /// continuous assignment or gate may drive it (IEEE 1800, 6.5).
        /// </summary>
        Variable,
    }

    /// <summary>
    /// One signal: its hierarchical name, width, kind and signedness -- and what its declaration said,
    /// which a waveform needs to list it and write its values: the range, the enum, whether it is a
    /// port, and the scope it was declared in.
    /// </summary>
    /// <remarks>
    /// A circuit built in code (<see cref="NetlistBuilder"/>'s public methods) declares nothing beyond its
    /// width, so its signals read as <c>[Width-1:0]</c>, with no enum, no port and no scope. The
    /// elaborator fills them all in from the source.
    /// </remarks>
    public sealed class SignalInfo
    {
        internal SignalInfo(string name, int width, SignalKind kind, bool signed, bool local = false, LogicVec? initial = null, MemoryInfo memory = null,
            int? msb = null, int? lsb = null, EnumInfo @enum = null, PortDirection? port = null, int scope = -1, bool twoState = false)
        {
            TwoState = twoState;
            Name = name;
            Width = width;
            Kind = kind;
            Signed = signed;
            Local = local;
            Initial = initial;
            Memory = memory;
            Msb = msb ?? width - 1;
            Lsb = lsb ?? 0;
            Enum = @enum;
            Port = port;
            Scope = scope;
        }

        /// <summary>Its full name, dot-separated through the hierarchy: <c>uut.sum</c>.</summary>
        public string Name { get; }

        public int Width { get; }
        public SignalKind Kind { get; }
        public bool Signed { get; }

        /// <summary>
        /// The range it was declared with, <c>[Msb:Lsb]</c>, as written: <c>[7:0]</c> is 7 and 0, <c>[0:7]</c>
        /// is 0 and 7, a scalar 0 and 0, an <c>int</c> 31 and 0. Always <see cref="Width"/> bits apart, end
        /// to end. The value itself does not know the range: its bit 0 is the bit <see cref="Lsb"/> names,
        /// whichever way the range runs. This is what a waveform labels a bus with, and what <c>s[3]</c>
        /// in the source is counted against.
        /// </summary>
        public int Msb { get; }

        /// <inheritdoc cref="Msb"/>
        public int Lsb { get; }

        /// <summary>
        /// The enum it was declared with -- <c>state_t s;</c> through a typedef, or <c>enum {A, B} s;</c> in
        /// place -- or null for any other type. A memory of an enum type gives it to every word.
        /// </summary>
        public EnumInfo Enum { get; }

        /// <summary>
        /// Its direction when it is a port of the module it is declared in -- <c>dut.a</c> of an instance,
        /// or the top's own -- and null for anything else, including the parent's signal a port is
        /// connected to: that is the parent's, and a port is a signal of its own (see <see cref="Elaborator"/>).
        /// </summary>
        public PortDirection? Port { get; }

        /// <summary>
        /// The scope it was declared in, an index into <see cref="Design.Scopes"/>; its name is that
        /// scope's <see cref="ScopeInfo.Path"/>, a dot, and its own. -1 for a signal no scope of the
        /// source declares: <c>$time</c>, the engine's own working variables, and every signal of a
        /// circuit built in code.
        /// </summary>
        public int Scope { get; }

        /// <summary>
        /// Whether it holds only 0 and 1 -- a <c>bit</c>, <c>byte</c>, <c>shortint</c>, <c>int</c> or
        /// <c>longint</c>, or an enum with no base type, which is an <c>int</c>. It starts at 0 rather
        /// than x, and an x or a z written to it is held as 0 (IEEE 1800, 6.11.2), as Icarus and the
        /// course's ModelSim hold it: the course's <c>typedef enum {IDLE, ...}</c> reads IDLE before its
        /// reset. <c>logic</c>, <c>integer</c> and <c>time</c> are 4-state.
        /// </summary>
        public bool TwoState { get; }

        /// <summary>
        /// A process's own working variable -- a <c>for</c> loop's <c>int i</c>, a <c>repeat</c>'s count
        /// -- which nothing outside it reads. Its changes are not recorded and a VCD leaves it out.
        /// </summary>
        public bool Local { get; }

        /// <summary>The value a variable is declared with, <c>logic a = 0;</c>, which it holds from before time 0.</summary>
        public LogicVec? Initial { get; }

        /// <summary>The memory this is one word of, or null. Words are left out of a VCD, as Icarus leaves them.</summary>
        public MemoryInfo Memory { get; }

        public override string ToString() => $"{Kind} {Name} [{Width}]";
    }

    /// <summary>
    /// A memory, <c>logic [31:0] mem [0:1023];</c>: an unpacked array of words, each a variable of its
    /// own in <see cref="Design.Signals"/>, side by side, named <c>mem[0]</c>, <c>mem[1]</c> and so on.
    /// </summary>
    public sealed class MemoryInfo
    {
        internal MemoryInfo(string name, int left, int right, int width, bool signed)
        {
            Name = name;
            Left = left;
            Right = right;
            Width = width;
            Signed = signed;
        }

        /// <summary>Its full name: <c>cpu.ram</c>.</summary>
        public string Name { get; }

        /// <summary>The declared range, <c>[Left:Right]</c>; <c>[1024]</c> is <c>[0:1023]</c>.</summary>
        public int Left { get; }

        /// <inheritdoc cref="Left"/>
        public int Right { get; }

        public int Lowest => Math.Min(Left, Right);
        public int Highest => Math.Max(Left, Right);
        public int Depth => Highest - Lowest + 1;

        /// <summary>Each word's width and signedness.</summary>
        public int Width { get; }

        /// <inheritdoc cref="Width"/>
        public bool Signed { get; }

        /// <summary>The signal of the lowest word; word <c>i</c> is the one <c>i - Lowest</c> after it.</summary>
        public SignalId First { get; internal set; }

        /// <summary>The signal holding word <paramref name="index"/>, counted as declared.</summary>
        public SignalId Word(int index)
        {
            if (index < Lowest || index > Highest)
                throw new ArgumentOutOfRangeException(nameof(index), index, $"{Name} has words {Lowest} to {Highest}.");
            return new SignalId(First.Index + index - Lowest);
        }

        public override string ToString() => $"{Name} [{Left}:{Right}] of {Width} bits";
    }

    /// <summary>What made a scope of the hierarchy.</summary>
    public enum ScopeKind
    {
        /// <summary>The top module: the testbench, or the design itself when it is simulated alone.</summary>
        Top,

        /// <summary>An instance of a module: <c>dut</c>, <c>dut.h1</c>.</summary>
        Instance,

        /// <summary>
        /// A generate block, named or not: <c>slice[3]</c> for each pass of a loop, <c>genblk1</c> for an
        /// unnamed one, the block an <c>if</c> chose -- named as Icarus names them.
        /// </summary>
        Generate,

        /// <summary>
        /// A named block in a process, <c>begin : check ... end</c>. What it declares is recorded under its
        /// name, as a generate block's is, so it is a scope as well (IEEE 1800, 9.3.4).
        /// </summary>
        Block,
    }

    /// <summary>
    /// One scope of the elaborated hierarchy -- the top, an instance, a generate block, a named block --
    /// so a waveform can list the signals as the design is built, not as one long list of dotted names.
    /// </summary>
    /// <remarks>
    /// **Its path is exactly its signals' prefix.** A signal declared in it is named <see cref="Path"/>, a
    /// dot, and its own name, and <see cref="SignalInfo.Scope"/> says which scope that is, so nothing
    /// has to split names on dots to find it -- a name that splits wrongly would be found in the wrong
    /// place, and a generate block's <c>slice[3]</c> is one level, not two.
    ///
    /// **Every scope comes after its parent** in <see cref="Design.Scopes"/>, the top first: they are
    /// recorded as the elaboration makes them, and a scope is made inside its parent's elaboration. So
    /// the tree can be built in one pass, parents first. A scope may hold no signals at all -- a
    /// generate block holding only an instance, say -- and is still one, since the instance's path runs
    /// through it.
    /// </remarks>
    public sealed class ScopeInfo
    {
        internal ScopeInfo(string path, string name, string module, ScopeKind kind, int parent)
        {
            Path = path;
            Name = name;
            Module = module;
            Kind = kind;
            Parent = parent;
        }

        /// <summary>Its full, dot-separated path: <c>full_adder_tb.dut.h1</c>, <c>alu64.slice[3]</c>.</summary>
        public string Path { get; }

        /// <summary>
        /// Its own part of <see cref="Path"/>, after its parent's: <c>h1</c>, <c>slice[3]</c>; the whole
        /// path for the top. What a tree of scopes labels it with.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// The module it is an instance of, for <see cref="ScopeKind.Top"/> and <see cref="ScopeKind.Instance"/>;
        /// null for a generate block or a named block, which belong to their module's instance.
        /// </summary>
        public string Module { get; }

        public ScopeKind Kind { get; }

        /// <summary>The scope it is inside, an index into <see cref="Design.Scopes"/> and always below its own; -1 for the top.</summary>
        public int Parent { get; }

        public override string ToString() => Module != null ? $"{Kind} {Path} ({Module})" : $"{Kind} {Path}";
    }

    /// <summary>Something that drives a signal continuously: a gate primitive or a continuous assignment.</summary>
    public abstract class DriverInfo
    {
        internal DriverInfo(string name, SignalId target, int targetLowest, int targetWidth, Delay delay)
        {
            Name = name;
            Target = target;
            TargetLowest = targetLowest;
            TargetWidth = targetWidth;
            Delay = delay;
        }

        /// <summary>Its instance name, <c>g1</c>, or a generated one for an assignment.</summary>
        public string Name { get; }

        /// <summary>The signal it drives.</summary>
        public SignalId Target { get; }

        /// <summary>
        /// The bits of <see cref="Target"/> it drives: from <see cref="TargetLowest"/>, this many. The whole
        /// signal for most drivers; a slice for <c>assign y[3] = a;</c>, for a gate whose output is one bit
        /// of a vector, and for a module output connected to part of one.
        /// </summary>
        public int TargetWidth { get; }

        /// <inheritdoc cref="TargetWidth"/>
        public int TargetLowest { get; }

        public Delay Delay { get; }

        /// <summary>Its output for the signals' current values.</summary>
        internal abstract LogicVec Evaluate(LogicVec[] values);

        /// <summary>The signals whose changes make it evaluate again.</summary>
        internal abstract void CollectReads(ICollection<int> reads);
    }

    /// <summary>A gate primitive: <c>and #(2, 3) g1 (y, a, b);</c>.</summary>
    /// <remarks>
    /// Each input is a one-bit expression, so <c>and g (y, in[0], sel)</c> reads a bit of a vector
    /// directly. The inputs' values are gathered into an array the gate keeps, so evaluating one
    /// allocates nothing.
    /// </remarks>
    public sealed class GateDriver : DriverInfo
    {
        private readonly Expr[] _inputs;
        private readonly LogicVec[] _scratch;

        internal GateDriver(string name, GateKind kind, SignalId output, int outputBit, Delay delay, Expr[] inputs)
            : base(name, output, outputBit, 1, delay)
        {
            Kind = kind;
            _inputs = inputs;
            _scratch = new LogicVec[inputs.Length];
        }

        public GateKind Kind { get; }

        public int InputCount => _inputs.Length;

        /// <summary>What drives one of its inputs.</summary>
        public Expr Input(int i) => _inputs[i];

        internal override LogicVec Evaluate(LogicVec[] values)
        {
            for (int i = 0; i < _inputs.Length; i++)
                _scratch[i] = _inputs[i].Evaluate(values);

            return Gates.Evaluate(Kind, _scratch);
        }

        internal override void CollectReads(ICollection<int> reads)
        {
            foreach (Expr input in _inputs)
                input.CollectReads(reads);
        }
    }

    /// <summary>A continuous assignment: <c>assign #3 y = a &amp; b;</c>.</summary>
    public sealed class AssignDriver : DriverInfo
    {
        internal AssignDriver(string name, SignalId target, int targetLowest, int targetWidth, Delay delay, Expr value)
            : base(name, target, targetLowest, targetWidth, delay)
        {
            Value = value;
        }

        /// <summary>The expression, already sized to the width it drives.</summary>
        public Expr Value { get; }

        internal override LogicVec Evaluate(LogicVec[] values) => Value.Evaluate(values);
        internal override void CollectReads(ICollection<int> reads) => Value.CollectReads(reads);
    }

    /// <summary>
    /// A flattened design ready to simulate: every signal and every continuous driver. Immutable, so
    /// one design can be simulated any number of times -- a replay is a fresh simulator on the same
    /// design.
    /// </summary>
    public sealed class Design
    {
        private readonly Dictionary<string, SignalId> _byName;

        internal Design(string name, List<SignalInfo> signals, List<DriverInfo> drivers, List<ProcessInfo> processes,
            List<MemoryInfo> memories, SignalId? time, List<ScopeInfo> scopes = null)
        {
            Name = name;
            Signals = signals.ToArray();
            Drivers = drivers.ToArray();
            Processes = processes.ToArray();
            Memories = memories.ToArray();
            Time = time;
            Scopes = scopes != null ? scopes.ToArray() : Array.Empty<ScopeInfo>();

            _byName = new Dictionary<string, SignalId>(StringComparer.Ordinal);
            for (int i = 0; i < Signals.Count; i++)
                _byName[Signals[i].Name] = new SignalId(i);
        }

        /// <summary>The top module's name, which a VCD scopes everything under.</summary>
        public string Name { get; }

        public IReadOnlyList<SignalInfo> Signals { get; }

        public IReadOnlyList<DriverInfo> Drivers { get; }

        /// <summary>The processes, in the order they start at time 0: an instance's before its parent's own, then as written.</summary>
        public IReadOnlyList<ProcessInfo> Processes { get; }

        public IReadOnlyList<MemoryInfo> Memories { get; }

        /// <summary>
        /// Every scope the elaboration made -- the top, each instance, each generate block, each named
        /// block in a process -- the top first and every scope after its parent (see <see cref="ScopeInfo"/>).
        /// Empty for a circuit built in code, which has a name for its top and nothing else.
        /// </summary>
        public IReadOnlyList<ScopeInfo> Scopes { get; }

        /// <summary>
        /// The signal <c>$time</c> reads, if the design reads it: the engine sets it as time moves, with
        /// no event, so only what runs anyway sees it change -- as the standard has it.
        /// </summary>
        public SignalId? Time { get; }

        public SignalInfo this[SignalId signal] => Signals[signal.Index];

        /// <summary>The signal at <paramref name="index"/> in <see cref="Signals"/>: how to walk them all.</summary>
        public SignalId SignalAt(int index)
        {
            if (index < 0 || index >= Signals.Count)
                throw new ArgumentOutOfRangeException(nameof(index), index, $"The design has {Signals.Count} signals.");

            return new SignalId(index);
        }

        /// <summary>The signal of this full name, if there is one.</summary>
        public bool TryFind(string name, out SignalId signal) => _byName.TryGetValue(name, out signal);

        /// <summary>The signal of this full name.</summary>
        public SignalId Find(string name) =>
            _byName.TryGetValue(name, out SignalId signal) ? signal : throw new KeyNotFoundException($"There is no signal called '{name}'.");
    }

    /// <summary>
    /// Builds a <see cref="Design"/> signal by signal and driver by driver: how Part 2's tests make
    /// circuits before the SystemVerilog front end exists, and what the elaborator calls from Part 4.
    /// </summary>
    /// <remarks>
    /// It refuses what SystemVerilog refuses, with the reason: a second continuous driver on a
    /// variable (only a <c>wire</c> may have several, IEEE 1800 6.5 -- Icarus refuses it too), a
    /// variable written both by a process and continuously, a process writing a wire, a gate input or
    /// output that is not one bit, the wrong number of inputs, a duplicate name.
    /// </remarks>
    public sealed class NetlistBuilder
    {
        private readonly string _name;
        private readonly List<SignalInfo> _signals = new List<SignalInfo>();
        private readonly List<DriverInfo> _drivers = new List<DriverInfo>();
        private readonly HashSet<string> _names = new HashSet<string>(StringComparer.Ordinal);
        /// <summary>Which bits of each variable a continuous driver already drives: a second may not overlap them.</summary>
        private readonly Dictionary<int, bool[]> _drivenBits = new Dictionary<int, bool[]>();
        private readonly List<ProcessInfo> _processes = new List<ProcessInfo>();
        private readonly List<MemoryInfo> _memories = new List<MemoryInfo>();
        private readonly List<ScopeInfo> _scopes = new List<ScopeInfo>();
        private SignalId? _time;

        /// <summary>The variables a process writes, and which: none of them may also be driven continuously.</summary>
        private readonly Dictionary<int, ProcessInfo> _procedural = new Dictionary<int, ProcessInfo>();

        /// <summary>The signals with at least one continuous driver.</summary>
        private readonly HashSet<int> _driven = new HashSet<int>();

        public NetlistBuilder(string name = "top")
        {
            _name = name ?? throw new ArgumentNullException(nameof(name));
        }

        /// <summary>A <c>wire</c>.</summary>
        public SignalId Net(string name, int width = 1, bool signed = false) => Add(name, width, SignalKind.Net, signed);

        /// <summary>A <c>logic</c>.</summary>
        public SignalId Variable(string name, int width = 1, bool signed = false) => Add(name, width, SignalKind.Variable, signed);

        /// <summary>A <c>logic</c> declared with a value, <c>logic a = 0;</c>: it holds it from before time 0.</summary>
        public SignalId Variable(string name, int width, bool signed, LogicVec initial)
        {
            if (initial.IsEmpty)
                throw new ArgumentException("A starting value needs a value.", nameof(initial));
            return Add(name, width, SignalKind.Variable, signed, local: false, initial);
        }

        /// <summary>
        /// A memory: <c>logic [width-1:0] name [left:right]</c>, one variable a word. Every word starts at x
        /// until something writes it -- a process, or <c>$readmemh</c>.
        /// </summary>
        public MemoryInfo Memory(string name, int width, bool signed, int left, int right) =>
            Memory(name, width, signed, left, right, width - 1, 0, null, -1);

        /// <summary>
        /// A memory as the elaborator declares it: each word with the range and enum its type gives,
        /// <c>state_t trail [0:3]</c>, in the scope it is declared in.
        /// </summary>
        internal MemoryInfo Memory(string name, int width, bool signed, int left, int right, int msb, int lsb, EnumInfo @enum, int scope, bool twoState = false)
        {
            if (Math.Abs((long)left - right) + 1 > MaxMemoryWords)
                throw new ArgumentOutOfRangeException(nameof(right), $"{name} would have more than {MaxMemoryWords} words.");

            var memory = new MemoryInfo(name, left, right, width, signed);
            SignalId first = default;
            for (int i = memory.Lowest; i <= memory.Highest; i++)
            {
                SignalId word = Add($"{name}[{i}]", width, SignalKind.Variable, signed, memory: memory, msb: msb, lsb: lsb, @enum: @enum, scope: scope, twoState: twoState);
                if (i == memory.Lowest)
                    first = word;
            }

            memory.First = first;
            _memories.Add(memory);
            return memory;
        }

        /// <summary>
        /// A memory of more than one unpacked dimension, <c>logic [7:0] grid [0:3][0:7]</c> (Part L4): its
        /// words row by row, the last dimension the fastest, each named as it is written --
        /// <c>grid[0][0]</c>, <c>grid[0][1]</c>... -- and counted from 0 as one memory of them all.
        /// </summary>
        internal MemoryInfo Memory(string name, int width, bool signed, (int Left, int Right)[] dims, int msb, int lsb, EnumInfo @enum, int scope, bool twoState = false)
        {
            if (dims.Length == 1)
                return Memory(name, width, signed, dims[0].Left, dims[0].Right, msb, lsb, @enum, scope, twoState);

            long words = 1;
            foreach ((int left, int right) in dims)
                words *= Math.Abs((long)left - right) + 1;
            if (words > MaxMemoryWords)
                throw new ArgumentOutOfRangeException(nameof(dims), $"{name} would have more than {MaxMemoryWords} words.");

            var memory = new MemoryInfo(name, 0, (int)words - 1, width, signed);
            var index = new int[dims.Length];
            for (int k = 0; k < dims.Length; k++)
                index[k] = Math.Min(dims[k].Left, dims[k].Right);

            var text = new System.Text.StringBuilder();
            SignalId first = default;
            for (int w = 0; w < words; w++)
            {
                text.Clear().Append(name);
                foreach (int i in index)
                    text.Append('[').Append(i).Append(']');

                SignalId word = Add(text.ToString(), width, SignalKind.Variable, signed, memory: memory, msb: msb, lsb: lsb, @enum: @enum, scope: scope, twoState: twoState);
                if (w == 0)
                    first = word;

                // The next word: the last index moves on, and carries into the one before it.
                for (int k = dims.Length - 1; k >= 0; k--)
                {
                    if (index[k] < Math.Max(dims[k].Left, dims[k].Right))
                    {
                        index[k]++;
                        break;
                    }

                    index[k] = Math.Min(dims[k].Left, dims[k].Right);
                }
            }

            memory.First = first;
            _memories.Add(memory);
            return memory;
        }

        /// <summary>The most words one memory may have: a million, four times the course's largest.</summary>
        public const int MaxMemoryWords = 1 << 20;

        /// <summary>The signal <c>$time</c> reads, made the first time it is asked for.</summary>
        public SignalId Time()
        {
            if (_time == null)
                _time = Add("$time", 64, SignalKind.Variable, false, local: true);
            return _time.Value;
        }

        /// <summary>A process's own working variable: not recorded, not in a VCD (see <see cref="SignalInfo.Local"/>).</summary>
        public SignalId Local(string name, int width = 1, bool signed = false, LogicVec? initial = null) =>
            Add(name, width, SignalKind.Variable, signed, local: true, initial);

        /// <summary>
        /// A signal as the elaborator declares it, with everything its declaration said: what
        /// <see cref="Net"/>, <see cref="Variable(string, int, bool)"/> and <see cref="Local"/> make, and
        /// its range, enum, port direction and scope besides. A wire takes neither a starting value nor
        /// <paramref name="local"/>, as <see cref="Net"/> takes neither.
        /// </summary>
        internal SignalId Declare(string name, int width, SignalKind kind, bool signed, bool local, LogicVec? initial,
            int msb, int lsb, EnumInfo @enum, PortDirection? port, int scope, bool twoState = false)
        {
            bool net = kind == SignalKind.Net;
            if (!net && !local && initial.HasValue && initial.Value.IsEmpty)
                throw new ArgumentException("A starting value needs a value.", nameof(initial));

            return Add(name, width, kind, signed, local: !net && local, initial: net ? null : initial, memory: null,
                msb: msb, lsb: lsb, @enum: @enum, port: port, scope: scope, twoState: twoState);
        }

        /// <summary>
        /// A scope of the hierarchy, at <paramref name="path"/> inside <paramref name="parent"/> (an index
        /// into the scopes made so far, -1 for the top): the elaborator records one as it makes each, so
        /// every scope comes after its parent. The first is the top and the only one without a parent,
        /// and a child's path is its parent's, a dot, and its own name. Its index, for
        /// <see cref="SignalInfo.Scope"/> and the scopes inside it.
        /// </summary>
        internal int Scope(string path, string module, ScopeKind kind, int parent)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentException("A scope needs a path.", nameof(path));

            string name;
            if (_scopes.Count == 0)
            {
                if (parent != -1 || kind != ScopeKind.Top)
                    throw new ArgumentException($"The first scope is the top, and {path} is not.", nameof(kind));
                name = path;
            }
            else
            {
                if (parent < 0 || parent >= _scopes.Count || kind == ScopeKind.Top)
                    throw new ArgumentOutOfRangeException(nameof(parent), parent, $"{path} needs a parent among the {_scopes.Count} scopes made so far, and only the first is the top.");

                string prefix = _scopes[parent].Path + ".";
                if (!path.StartsWith(prefix, StringComparison.Ordinal) || path.Length == prefix.Length)
                    throw new ArgumentException($"{path} is not inside {_scopes[parent].Path}.", nameof(path));
                name = path.Substring(prefix.Length);
            }

            _scopes.Add(new ScopeInfo(path, name, module, kind, parent));
            return _scopes.Count - 1;
        }

        /// <summary>
        /// A process. Every signal it writes must be a variable that nothing drives continuously: a
        /// variable takes its value from processes or from one continuous driver, never both (IEEE
        /// 1800, 6.5).
        /// </summary>
        public void Process(ProcessInfo process)
        {
            if (process == null) throw new ArgumentNullException(nameof(process));

            foreach (int s in process.Writes)
            {
                SignalInfo info = _signals[s];
                if (info.Kind != SignalKind.Variable)
                    throw new InvalidOperationException(
                        $"{process.Name} writes {info.Name}, which is a wire; a process can only write a variable. Declare it logic.");

                if (_driven.Contains(s))
                    throw new InvalidOperationException(
                        $"{info.Name} is written by {process.Name} and driven continuously as well; a variable takes one or the other. " +
                        "Give the assign or the gate a wire of its own.");
            }

            foreach (int s in process.Writes)
                if (!_procedural.ContainsKey(s))
                    _procedural[s] = process;

            _processes.Add(process);
        }

        /// <summary>A gate primitive driving <paramref name="output"/> from <paramref name="inputs"/>.</summary>
        public void Gate(GateKind kind, string name, SignalId output, Delay delay, params SignalId[] inputs)
        {
            if (inputs == null || !Gates.AcceptsInputCount(kind, inputs.Length))
                throw new ArgumentException($"A {kind} gate cannot take {inputs?.Length ?? 0} inputs.", nameof(inputs));

            RequireOneBit(output, $"the output of {name}");
            foreach (SignalId input in inputs)
                RequireOneBit(input, $"an input of {name}");

            Gate(kind, name, output, 0, delay, Array.ConvertAll(inputs, Read));
        }

        /// <summary>
        /// A gate primitive driving bit <paramref name="outputBit"/> of <paramref name="output"/>, each
        /// input a one-bit expression: <c>and g (y[2], in[0], sel)</c>.
        /// </summary>
        public void Gate(GateKind kind, string name, SignalId output, int outputBit, Delay delay, params Expr[] inputs)
        {
            if (inputs == null || !Gates.AcceptsInputCount(kind, inputs.Length))
                throw new ArgumentException($"A {kind} gate cannot take {inputs?.Length ?? 0} inputs.", nameof(inputs));

            foreach (Expr input in inputs)
                if (input == null || input.Width != 1)
                    throw new ArgumentException($"A gate primitive's terminals are one bit, and an input of {name} is {input?.Width ?? 0}.");

            RequireBits(output, outputBit, 1, $"the output of {name}");
            CountDriver(output, outputBit, 1, name);
            _drivers.Add(new GateDriver(name, kind, output, outputBit, delay, inputs));
        }

        /// <summary>
        /// <c>assign #delay target = value;</c>. The value is sized to the target as an assignment
        /// sizes it: widened (sign-extended only if the expression is signed, which the caller says) or
        /// truncated.
        /// </summary>
        public void Assign(SignalId target, Expr value, Delay delay, bool signExtend = false, string name = null)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));

            int width = _signals[target.Index].Width;
            Expr sized = value.Width < width ? Expr.Resize(value, width, signExtend)
                : value.Width > width ? Expr.Slice(value, 0, width)
                : value;

            string driverName = name ?? $"assign {_signals[target.Index].Name}";
            CountDriver(target, 0, width, driverName);
            _drivers.Add(new AssignDriver(driverName, target, 0, width, delay, sized));
        }

        /// <summary>
        /// <c>assign #delay target[lowest +: width] = value;</c>: drives only those bits, sizing the value
        /// to them as an assignment does.
        /// </summary>
        public void AssignSlice(SignalId target, int lowest, int width, Expr value, Delay delay, bool signExtend = false, string name = null)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));

            RequireBits(target, lowest, width, "the assignment's target");

            Expr sized = value.Width < width ? Expr.Resize(value, width, signExtend)
                : value.Width > width ? Expr.Slice(value, 0, width)
                : value;

            string driverName = name ?? $"assign {_signals[target.Index].Name}[{lowest + width - 1}:{lowest}]";
            CountDriver(target, lowest, width, driverName);
            _drivers.Add(new AssignDriver(driverName, target, lowest, width, delay, sized));
        }

        /// <summary>An expression reading a signal of this design, at its own width.</summary>
        public Expr Read(SignalId signal) => Expr.Read(signal, _signals[signal.Index].Width);

        /// <summary>A signal's declaration as it stands while the design is being built.</summary>
        internal SignalInfo InfoOf(SignalId signal) => _signals[signal.Index];

        public Design Build() => new Design(_name, _signals, _drivers, _processes, _memories, _time, _scopes);

        private SignalId Add(string name, int width, SignalKind kind, bool signed, bool local = false, LogicVec? initial = null, MemoryInfo memory = null,
            int? msb = null, int? lsb = null, EnumInfo @enum = null, PortDirection? port = null, int scope = -1, bool twoState = false)
        {
            if (string.IsNullOrEmpty(name))
                throw new ArgumentException("A signal needs a name.", nameof(name));
            if (!_names.Add(name))
                throw new ArgumentException($"There is already a signal called '{name}'.", nameof(name));
            if (width < 1 || width > LogicVec.MaxWidth)
                throw new ArgumentOutOfRangeException(nameof(width), width, $"A signal's width must be from 1 to {LogicVec.MaxWidth}.");
            if ((msb.HasValue || lsb.HasValue) && Math.Abs((long)(msb ?? 0) - (lsb ?? 0)) + 1 != width)
                throw new ArgumentOutOfRangeException(nameof(msb), $"[{msb}:{lsb}] is not {width} bits, the width given for {name}.");
            if (scope < -1 || scope >= _scopes.Count)
                throw new ArgumentOutOfRangeException(nameof(scope), scope, $"There are {_scopes.Count} scopes for {name} to be declared in.");

            _signals.Add(new SignalInfo(name, width, kind, signed, local, initial, memory, msb, lsb, @enum, port, scope, twoState));
            return new SignalId(_signals.Count - 1);
        }

        private void RequireOneBit(SignalId signal, string what)
        {
            if (_signals[signal.Index].Width != 1)
                throw new ArgumentException($"A gate primitive's terminals are one bit, and {what}, {_signals[signal.Index].Name}, is {_signals[signal.Index].Width}.");
        }

        private void RequireBits(SignalId signal, int lowest, int width, string what)
        {
            SignalInfo info = _signals[signal.Index];
            if (lowest < 0 || width < 1 || lowest + width > info.Width)
                throw new ArgumentOutOfRangeException(nameof(lowest),
                    $"{what} drives bits {lowest + width - 1}..{lowest} of {info.Name}, which has {info.Width}.");
        }

        /// <summary>
        /// A variable's bits may each have one continuous driver (IEEE 1800, 6.5): different drivers may
        /// share a variable only by driving different bits of it. A wire may have any number.
        /// </summary>
        private void CountDriver(SignalId target, int lowest, int width, string driver)
        {
            SignalInfo info = _signals[target.Index];

            if (_procedural.TryGetValue(target.Index, out ProcessInfo writer))
                throw new InvalidOperationException(
                    $"{info.Name} is written by {writer.Name}, so {driver} cannot drive it too; a variable takes one or the other. " +
                    "Give the assign or the gate a wire of its own.");

            _driven.Add(target.Index);

            if (info.Kind != SignalKind.Variable)
                return;

            if (!_drivenBits.TryGetValue(target.Index, out bool[] driven))
                _drivenBits[target.Index] = driven = new bool[info.Width];

            for (int bit = lowest; bit < lowest + width; bit++)
            {
                if (driven[bit])
                    throw new InvalidOperationException(
                        $"{info.Name} is a variable (logic), and only one continuous assignment or gate may drive each of its bits; " +
                        $"{driver} would be the second on bit {bit}. Declare it as a wire to give it several drivers.");
                driven[bit] = true;
            }
        }
    }
}
