using System;
using System.Collections.Generic;
using BitSorter.SystemVerilog;

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

    /// <summary>One signal: its hierarchical name, width, kind and signedness.</summary>
    public sealed class SignalInfo
    {
        internal SignalInfo(string name, int width, SignalKind kind, bool signed, bool local = false, LogicVec? initial = null, MemoryInfo memory = null)
        {
            Name = name;
            Width = width;
            Kind = kind;
            Signed = signed;
            Local = local;
            Initial = initial;
            Memory = memory;
        }

        /// <summary>Its full name, dot-separated through the hierarchy: <c>uut.sum</c>.</summary>
        public string Name { get; }

        public int Width { get; }
        public SignalKind Kind { get; }
        public bool Signed { get; }

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
            List<MemoryInfo> memories, SignalId? time)
        {
            Name = name;
            Signals = signals.ToArray();
            Drivers = drivers.ToArray();
            Processes = processes.ToArray();
            Memories = memories.ToArray();
            Time = time;

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
        public MemoryInfo Memory(string name, int width, bool signed, int left, int right)
        {
            if (Math.Abs((long)left - right) + 1 > MaxMemoryWords)
                throw new ArgumentOutOfRangeException(nameof(right), $"{name} would have more than {MaxMemoryWords} words.");

            var memory = new MemoryInfo(name, left, right, width, signed);
            SignalId first = default;
            for (int i = memory.Lowest; i <= memory.Highest; i++)
            {
                SignalId word = Add($"{name}[{i}]", width, SignalKind.Variable, signed, memory: memory);
                if (i == memory.Lowest)
                    first = word;
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

        public Design Build() => new Design(_name, _signals, _drivers, _processes, _memories, _time);

        private SignalId Add(string name, int width, SignalKind kind, bool signed, bool local = false, LogicVec? initial = null, MemoryInfo memory = null)
        {
            if (string.IsNullOrEmpty(name))
                throw new ArgumentException("A signal needs a name.", nameof(name));
            if (!_names.Add(name))
                throw new ArgumentException($"There is already a signal called '{name}'.", nameof(name));
            if (width < 1 || width > LogicVec.MaxWidth)
                throw new ArgumentOutOfRangeException(nameof(width), width, $"A signal's width must be from 1 to {LogicVec.MaxWidth}.");

            _signals.Add(new SignalInfo(name, width, kind, signed, local, initial, memory));
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
