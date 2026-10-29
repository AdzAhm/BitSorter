using System;
using System.Collections.Generic;
using BitSorter.SystemVerilog;

namespace BitSorter.Sim
{
    /// <summary>What a cell of an <see cref="RtlNetlist"/> is.</summary>
    public enum RtlKind
    {
        /// <summary>A value written in the code: <see cref="RtlCell.Value"/>.</summary>
        Constant,

        /// <summary>A gate primitive, <see cref="RtlCell.Gate"/>, its inputs in the order written.</summary>
        Gate,

        /// <summary>An operator of one operand, <see cref="RtlCell.Unary"/>: <c>~a</c>, <c>-a</c>, <c>&amp;a</c>.</summary>
        Unary,

        /// <summary>An operator of two, <see cref="RtlCell.Binary"/>: <c>a + b</c>, <c>a &lt; b</c>, <c>a &lt;&lt; b</c>.</summary>
        Binary,

        /// <summary><c>c ? t : f</c>: inputs the condition, then the value when true, then when false.</summary>
        Mux,

        /// <summary><c>{high, low}</c>: inputs the high part, then the low.</summary>
        Concat,

        /// <summary><c>{n{a}}</c>, n being <see cref="RtlCell.Count"/>.</summary>
        Replicate,

        /// <summary>The bits <see cref="RtlCell.Lowest"/> up, <see cref="RtlCell.Width"/> of them: a select at a fixed place.</summary>
        Slice,

        /// <summary><c>a[i]</c> with <c>i</c> a signal: inputs the value, then the index.</summary>
        Bit,

        /// <summary>
        /// <c>a[i +: w]</c>, or an element of a packed array, with <c>i</c> a signal: inputs the value, then the offset, counted
        /// in steps of <see cref="RtlCell.Step"/> bits.
        /// </summary>
        Part,

        /// <summary>The value at another width, <see cref="RtlCell.Width"/>, sign-extended when <see cref="RtlCell.Signed"/>.</summary>
        Resize,

        /// <summary><c>$signed(a)</c> when <see cref="RtlCell.Signed"/>, <c>$unsigned(a)</c> otherwise: the same bits.</summary>
        Signedness,

        /// <summary>The value as a 2-state type holds it: x and z as 0.</summary>
        TwoState,

        /// <summary>
        /// What one signal is given by its drivers (<see cref="RtlCell.Signal"/>): each input one driver's output, put at its
        /// place (<see cref="RtlCell.Lowests"/>) -- several resolved as a wire resolves them -- and the result held as the
        /// signal holds it, its width, sign and 2-state type. One driver of the whole signal is a wire, as a drawing shows it.
        /// </summary>
        Join,

        /// <summary>
        /// A word of a memory picked by an address -- <c>mem[addr]</c>, a memory's read port: input the address, the words
        /// those of <see cref="RtlCell.Memory"/>.
        /// </summary>
        Word,

        /// <summary>An instance of a module, <see cref="RtlCell.Module"/>: its pins named in <see cref="RtlCell.InputNames"/> and <see cref="RtlCell.OutputNames"/>.</summary>
        Instance,

        /// <summary>
        /// A process that is no hardware -- an <c>initial</c>, one with a delay or a wait inside it -- as a box: what it reads
        /// in, what it writes out, and <see cref="RtlCell.Why"/> it is a box. Every other process is opened into cells (Part
        /// 15h-2).
        /// </summary>
        Process,

        /// <summary>Whether a value is true as an <c>if</c> takes it: 1 or 0, an x or a z as 0.</summary>
        Truth,

        /// <summary>Whether a <c>case</c>'s subject matches a label, 1 or 0, by <see cref="RtlCell.Case"/>: inputs the subject, then the label.</summary>
        Match,

        /// <summary>
        /// A write at a place worked out as it runs, <c>y[i] = v</c> -- a demultiplexer: inputs the signal's value before it, the
        /// part written, and the place; the part put where the place says, clipped to the signal, and nothing written where the
        /// place is unknown, the result held as <see cref="RtlCell.Signal"/> holds it.
        /// </summary>
        Put,

        /// <summary>
        /// A register, what an <c>always_ff</c> writes: inputs its D -- the value the process would give it -- then each signal
        /// it waits on, by <see cref="RtlCell.Edges"/>. Its output is the signal, <see cref="RtlCell.Signal"/>; or where several
        /// processes each write some of its bits, those bits -- <see cref="RtlCell.Width"/> of them from
        /// <see cref="RtlCell.Lowest"/> -- joined into the signal with the others'.
        /// </summary>
        Register,

        /// <summary>
        /// An expression that is no hardware -- a call of a function, a string, a real, <c>$random</c> -- as a box: the
        /// signals it reads in, its value out.
        /// </summary>
        Opaque,
    }

    /// <summary>A net of an <see cref="RtlNetlist"/>: a value of some width, carried from the cell that drives it to the ones that read it.</summary>
    public sealed class RtlNet
    {
        internal RtlNet(int id, int width, string name, int signal)
        {
            Id = id;
            Width = width;
            Name = name;
            Signal = signal;
        }

        public int Id { get; internal set; }

        public int Width { get; }

        /// <summary>
        /// Its name: the design's signal it is, named from the netlist's scope -- <c>sum</c>, <c>h1.a</c>, <c>slice[3].c</c> --
        /// or, for a value inside an expression no signal holds, <c>$</c> and its number.
        /// </summary>
        public string Name { get; internal set; }

        /// <summary>The design's signal it is, by index into <see cref="Design.Signals"/>; -1 for a value inside an expression.</summary>
        public int Signal { get; }

        /// <summary>The cell that drives it, by <see cref="RtlCell.Id"/>; -1 for one nothing in the netlist drives -- an input.</summary>
        public int Driver { get; internal set; } = -1;

        public override string ToString() => $"{Name} [{Width}]";
    }

    /// <summary>A cell of an <see cref="RtlNetlist"/>: an operator, a gate, a mux, a register, an instance -- a box with pins.</summary>
    public sealed class RtlCell
    {
        internal RtlCell(int id, RtlKind kind, string name, int[] inputs, int[] outputs)
        {
            Id = id;
            Kind = kind;
            Name = name;
            InputList = inputs;
            OutputList = outputs;
        }

        internal int[] InputList;
        internal int[] OutputList;

        public int Id { get; internal set; }
        public RtlKind Kind { get; }

        /// <summary>
        /// Its name: a gate's or an instance's as written, a process's place in the source (<c>always_ff@21</c>), a driver's
        /// target for a <see cref="RtlKind.Join"/>, a memory's for a <see cref="RtlKind.Word"/>; empty for an operator.
        /// </summary>
        public string Name { get; internal set; }

        /// <summary>The nets it reads, by id, in the order of its pins; -1 for a pin connected to nothing.</summary>
        public IReadOnlyList<int> Inputs => InputList;

        /// <summary>The nets it drives, by id: one, but for an instance or a process.</summary>
        public IReadOnlyList<int> Outputs => OutputList;

        /// <summary>The one net it drives, or the first.</summary>
        public int Output => OutputList.Length > 0 ? OutputList[0] : -1;

        public GateKind Gate { get; internal set; }
        public UnaryOp Unary { get; internal set; }
        public BinaryOp Binary { get; internal set; }

        /// <summary>A <see cref="RtlKind.Constant"/>'s value.</summary>
        public LogicVec Value { get; internal set; }

        /// <summary>A slice's lowest bit.</summary>
        public int Lowest { get; internal set; }

        /// <summary>A slice's, a part's or a resize's width.</summary>
        public int Width { get; internal set; }

        /// <summary>A replication's count.</summary>
        public int Count { get; internal set; }

        /// <summary>A part's step, in bits.</summary>
        public int Step { get; internal set; }

        /// <summary>A resize that extends the sign, or a <see cref="RtlKind.Signedness"/> to signed.</summary>
        public bool Signed { get; internal set; }

        /// <summary>A <see cref="RtlKind.Join"/>'s signal, by index into <see cref="Design.Signals"/>; -1 for any other cell.</summary>
        public int Signal { get; internal set; } = -1;

        /// <summary>A <see cref="RtlKind.Join"/>'s input pieces' lowest bits in its signal, one for each input.</summary>
        public IReadOnlyList<int> Lowests { get; internal set; } = Array.Empty<int>();

        /// <summary>A <see cref="RtlKind.Join"/>'s input pieces' widths: how many bits of its signal each driver drives.</summary>
        public IReadOnlyList<int> PieceWidths { get; internal set; } = Array.Empty<int>();

        /// <summary>Whether a <see cref="RtlKind.Join"/>'s signal is a wire, whose drivers resolve and whose undriven bits are z.</summary>
        public bool Wire { get; internal set; }

        /// <summary>Whether a <see cref="RtlKind.Join"/>'s signal is 2-state, holding x and z as 0.</summary>
        public bool TwoStateTarget { get; internal set; }

        /// <summary>A <see cref="RtlKind.Word"/>'s memory.</summary>
        public MemoryInfo Memory { get; internal set; }

        /// <summary>An instance's module.</summary>
        public string Module { get; internal set; }

        /// <summary>An instance's or a process's input pins' names, one for each input.</summary>
        public IReadOnlyList<string> InputNames { get; internal set; } = Array.Empty<string>();

        /// <summary>An instance's or a process's output pins' names, one for each output.</summary>
        public IReadOnlyList<string> OutputNames { get; internal set; } = Array.Empty<string>();

        /// <summary>A <see cref="RtlKind.Match"/>'s way of comparing.</summary>
        public CaseKind Case { get; internal set; }

        /// <summary>A <see cref="RtlKind.Register"/>'s edges, one for each input after its D.</summary>
        public IReadOnlyList<EdgeKind> Edges { get; internal set; } = Array.Empty<EdgeKind>();

        /// <summary>
        /// A <see cref="RtlKind.Register"/>'s clock, by its place in <see cref="Inputs"/>: the one edge its body does not test --
        /// the others, tested, its asynchronous resets and sets; -1 where no one edge is that.
        /// </summary>
        public int Clock { get; internal set; } = -1;

        /// <summary>Why a <see cref="RtlKind.Process"/> is a box and not cells; null for any other cell.</summary>
        public string Why { get; internal set; }

        /// <summary>An opaque cell's expression, which the evaluator works out as the engine does.</summary>
        internal Expr Source { get; set; }

        public override string ToString() => Name.Length > 0 ? $"{Kind} {Name}" : Kind.ToString();
    }

    /// <summary>What became of one process of a netlist's scope (Part 15h-2): opened into cells, or a box, and why.</summary>
    public sealed class RtlProcessRecord
    {
        internal RtlProcessRecord(string name, bool opened, string why, int[] writes)
        {
            Name = name;
            Opened = opened;
            Why = why;
            Writes = writes;
        }

        /// <summary>Its place in the source from the scope: <c>always_ff@21</c>, <c>gen[2].always_ff@9</c>.</summary>
        public string Name { get; }

        /// <summary>Whether it was opened into cells; false for a box.</summary>
        public bool Opened { get; }

        /// <summary>Why it is a box; null for one opened.</summary>
        public string Why { get; }

        /// <summary>The design's signals it writes, but its own working ones, by index into <see cref="Design.Signals"/>.</summary>
        public IReadOnlyList<int> Writes { get; }

        public override string ToString() => Opened ? Name : Name + " (a box: " + Why + ")";
    }

    /// <summary>
    /// What one scope of an elaborated design builds, as cells and the nets between them (Part 15h): a module's code as a
    /// circuit -- word-level, an adder one cell and a mux another -- the way a synthesis tool's RTL view draws it.
    /// <see cref="RtlExtractor"/> makes it from the design, and <see cref="RtlEvaluator"/> works out every net from the values
    /// of its inputs, as the engine does.
    /// </summary>
    public sealed class RtlNetlist
    {
        internal RtlNetlist(string scope, string module, RtlNet[] nets, RtlCell[] cells, int[] inputs, int[] outputs, RtlProcessRecord[] processes)
        {
            Processes = processes;
            Scope = scope;
            Module = module;
            Nets = nets;
            Cells = cells;
            Inputs = inputs;
            Outputs = outputs;
            _byName = new Dictionary<string, int>(StringComparer.Ordinal);
            _bySignal = new Dictionary<int, int>();
            foreach (RtlNet net in nets)
            {
                if (!_byName.ContainsKey(net.Name))
                    _byName[net.Name] = net.Id;
                if (net.Signal >= 0 && !_bySignal.ContainsKey(net.Signal))
                    _bySignal[net.Signal] = net.Id;
            }
        }

        private readonly Dictionary<string, int> _byName;
        private readonly Dictionary<int, int> _bySignal;

        /// <summary>The scope's path in the design: <c>alu_tb.dut</c>.</summary>
        public string Scope { get; }

        /// <summary>The module it is an instance of.</summary>
        public string Module { get; }

        public IReadOnlyList<RtlNet> Nets { get; }
        public IReadOnlyList<RtlCell> Cells { get; }

        /// <summary>The nets of the scope's input ports, in the order declared.</summary>
        public IReadOnlyList<int> Inputs { get; }

        /// <summary>The nets of its output ports.</summary>
        public IReadOnlyList<int> Outputs { get; }

        /// <summary>Every process written in the scope, in the order written: opened into cells, or drawn as a box and why.</summary>
        public IReadOnlyList<RtlProcessRecord> Processes { get; }

        /// <summary>The net of this name, as <see cref="RtlNet.Name"/> has it; -1 for none.</summary>
        public int NetNamed(string name) => _byName.TryGetValue(name, out int id) ? id : -1;

        /// <summary>The net of the design's signal <paramref name="signal"/>; -1 when the netlist has none.</summary>
        public int NetOf(int signal) => _bySignal.TryGetValue(signal, out int id) ? id : -1;

        /// <summary>How many cells of each kind: what a test, or a size cap, counts.</summary>
        public int Count(RtlKind kind)
        {
            int n = 0;
            foreach (RtlCell cell in Cells)
                if (cell.Kind == kind)
                    n++;
            return n;
        }
    }
}
