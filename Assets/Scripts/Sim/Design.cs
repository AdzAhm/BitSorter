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
        internal SignalInfo(string name, int width, SignalKind kind, bool signed)
        {
            Name = name;
            Width = width;
            Kind = kind;
            Signed = signed;
        }

        /// <summary>Its full name, dot-separated through the hierarchy: <c>uut.sum</c>.</summary>
        public string Name { get; }

        public int Width { get; }
        public SignalKind Kind { get; }
        public bool Signed { get; }

        public override string ToString() => $"{Kind} {Name} [{Width}]";
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

        internal Design(string name, List<SignalInfo> signals, List<DriverInfo> drivers)
        {
            Name = name;
            Signals = signals.ToArray();
            Drivers = drivers.ToArray();

            _byName = new Dictionary<string, SignalId>(StringComparer.Ordinal);
            for (int i = 0; i < Signals.Count; i++)
                _byName[Signals[i].Name] = new SignalId(i);
        }

        /// <summary>The top module's name, which a VCD scopes everything under.</summary>
        public string Name { get; }

        public IReadOnlyList<SignalInfo> Signals { get; }

        public IReadOnlyList<DriverInfo> Drivers { get; }

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
    /// variable (only a <c>wire</c> may have several, IEEE 1800 6.5 -- Icarus refuses it too), a gate
    /// input or output that is not one bit, the wrong number of inputs, a duplicate name.
    /// </remarks>
    public sealed class NetlistBuilder
    {
        private readonly string _name;
        private readonly List<SignalInfo> _signals = new List<SignalInfo>();
        private readonly List<DriverInfo> _drivers = new List<DriverInfo>();
        private readonly HashSet<string> _names = new HashSet<string>(StringComparer.Ordinal);
        /// <summary>Which bits of each variable a continuous driver already drives: a second may not overlap them.</summary>
        private readonly Dictionary<int, bool[]> _drivenBits = new Dictionary<int, bool[]>();

        public NetlistBuilder(string name = "top")
        {
            _name = name ?? throw new ArgumentNullException(nameof(name));
        }

        /// <summary>A <c>wire</c>.</summary>
        public SignalId Net(string name, int width = 1, bool signed = false) => Add(name, width, SignalKind.Net, signed);

        /// <summary>A <c>logic</c>.</summary>
        public SignalId Variable(string name, int width = 1, bool signed = false) => Add(name, width, SignalKind.Variable, signed);

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

        public Design Build() => new Design(_name, _signals, _drivers);

        private SignalId Add(string name, int width, SignalKind kind, bool signed)
        {
            if (string.IsNullOrEmpty(name))
                throw new ArgumentException("A signal needs a name.", nameof(name));
            if (!_names.Add(name))
                throw new ArgumentException($"There is already a signal called '{name}'.", nameof(name));
            if (width < 1 || width > LogicVec.MaxWidth)
                throw new ArgumentOutOfRangeException(nameof(width), width, $"A signal's width must be from 1 to {LogicVec.MaxWidth}.");

            _signals.Add(new SignalInfo(name, width, kind, signed));
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
