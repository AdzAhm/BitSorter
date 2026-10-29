using System;
using System.Collections.Generic;
using System.Globalization;
using BitSorter.Sim;
using BitSorter.SystemVerilog;
using BitSorter.SystemVerilog.Syntax;

namespace BitSorter.Board
{
    /// <summary>
    /// A module's code drawn (Part 15h-4): its netlist (<see cref="RtlNetlist"/>) made a drawn module of the board's own parts,
    /// laid out in columns (<see cref="LayeredLayout"/>), for the circuit's window to show as it shows any drawing -- every
    /// wire a signal of the design read as one, so its values are the run's.
    /// </summary>
    /// <remarks>
    /// **Each cell is the part that is the same thing**: a gate a gate, an operator an <see cref="OperatorPart"/>, a mux the
    /// board's mux, a register a register, an instance a block of its module, a constant a constant. **Anything else is a
    /// <see cref="BoxPart"/>** that says what it is -- a slice <c>[3:0]</c>, a join of bits <c>{ }</c>, a process that is no
    /// circuit by its name and why -- for showing only. A join of one driver over the whole signal is a wire.
    ///
    /// **It is for showing**: the module made is held beside the project, never in it, and nothing compiles it. Every wire on
    /// a signal's net is named for the signal, so the drawing finds its values by name; a value inside an expression, which
    /// no signal holds, is named <c>$</c> and its number.
    ///
    /// **With gates, the small ones are opened** (Part 15h-4, the GATES switch): an operator or a mux <see cref="RtlGates"/>
    /// opens is drawn as its gates in its place, wired to what the box was. A wire inside an opening is on no net of the
    /// netlist; it is worked out from them (<see cref="Inner"/>), each gate by the engine's own gate, so the gates show what
    /// real gates would -- the bits below an x of a sum still known -- while the wire out of them carries the code's value,
    /// which is what everything after them reads.
    /// </remarks>
    public sealed class RtlDrawing
    {
        private RtlDrawing(Project project, Module module, Builder builder)
        {
            Project = project;
            Module = module;
            _netOfWire = builder.NetOfWire;
            _instanceOf = builder.InstanceOf;
            _innerOfWire = builder.InnerOfWire;
            _inner = builder.Inner.ToArray();
            Opened = builder.Opened;
        }

        private readonly Dictionary<int, int> _innerOfWire;
        private readonly InnerNet[] _inner;

        /// <summary>How many operators and muxes are drawn as their gates.</summary>
        public int Opened { get; }

        /// <summary>The wires inside openings into gates: how many values <see cref="Inner"/> works out.</summary>
        public int InnerCount => _inner.Length;

        /// <summary>The wire inside an opening into gates a wire of the drawing is, by its id; -1 for one on a net of the netlist.</summary>
        public int InnerOfWire(int wire) => _innerOfWire.TryGetValue(wire, out int inner) ? inner : -1;

        /// <summary>
        /// The values of the wires inside the openings into gates, given the netlist's (<see cref="RtlEvaluator"/>), each by the
        /// engine's own gate, splitter, joiner and mux.
        /// </summary>
        public LogicVec[] Inner(IReadOnlyList<LogicVec> nets)
        {
            if (nets == null)
                throw new ArgumentNullException(nameof(nets));
            var values = new LogicVec[_inner.Length];
            for (int i = 0; i < values.Length; i++)
                values[i] = _inner[i].Value(nets, values);
            return values;
        }

        /// <summary>
        /// A wire inside an opening into gates: the part it comes out of, and what each of that part's inputs reads, in its
        /// pins' order -- a net of the netlist, or for <c>-1 - k</c> the k-th wire inside, which always comes before it.
        /// </summary>
        private sealed class InnerNet
        {
            public InnerNet(Part part, string pin, int[] from)
            {
                _part = part;
                _from = from;
                if (part is SplitterPart)
                    _bit = int.Parse(pin.Substring(1), NumberStyles.None, CultureInfo.InvariantCulture);
            }

            private readonly Part _part;
            private readonly int[] _from;
            private readonly int _bit;

            public LogicVec Value(IReadOnlyList<LogicVec> nets, LogicVec[] inner)
            {
                switch (_part)
                {
                    case GatePart gate:
                    {
                        var inputs = new LogicVec[_from.Length];
                        for (int i = 0; i < inputs.Length; i++)
                            inputs[i] = In(i, nets, inner);
                        return Gates.Evaluate(gate.Kind, inputs);
                    }
                    case SplitterPart _:
                        return In(0, nets, inner).Slice(_bit, 1);
                    case JoinerPart _:
                    {
                        LogicVec joined = In(0, nets, inner);
                        for (int i = 1; i < _from.Length; i++)
                            joined = LogicVec.Concat(In(i, nets, inner), joined);
                        return joined;
                    }
                    case ConstantPart constant:
                        return constant.Value;
                    case MuxPart mux:
                        return Choose(mux, 0, mux.Inputs, mux.SelectWidth - 1, nets, inner);
                    default:
                        return LogicVec.AllX(1);
                }
            }

            private LogicVec In(int i, IReadOnlyList<LogicVec> nets, LogicVec[] inner) => _from[i] >= 0 ? nets[_from[i]] : inner[-1 - _from[i]];

            /// <summary>A mux's inputs <paramref name="low"/> on, chosen as the board compiles it: a tree of <c>?:</c> from the select's top bit.</summary>
            private LogicVec Choose(MuxPart mux, int low, int count, int bit, IReadOnlyList<LogicVec> nets, LogicVec[] inner)
            {
                if (count == 1)
                    return In(low, nets, inner);
                LogicVec select = In(mux.Inputs, nets, inner);
                if (mux.SelectWidth > 1)
                    select = select.Slice(bit, 1);
                int half = count / 2;
                return LogicVec.Conditional(select, Choose(mux, low + half, half, bit - 1, nets, inner), Choose(mux, low, half, bit - 1, nets, inner));
            }
        }

        private readonly Dictionary<int, string> _instanceOf;

        /// <summary>
        /// The instance a block of the drawing is, by its part's id, named from the scope as the design names it -- <c>h1</c>,
        /// <c>gen[2].u</c> -- what stepping inside it steps to; null for a part that is no instance.
        /// </summary>
        public string InstanceOf(int part) => _instanceOf.TryGetValue(part, out string name) ? name : null;

        private readonly Dictionary<int, int> _netOfWire;

        /// <summary>The netlist's net a wire of the drawing carries, by its id; -1 for none.</summary>
        public int NetOfWire(int wire) => _netOfWire.TryGetValue(wire, out int net) ? net : -1;

        /// <summary>The project with the drawing's module in it beside the others, so a block of an instance finds its module.</summary>
        public Project Project { get; }

        /// <summary>The drawing: ports, parts and wires, laid out.</summary>
        public Module Module { get; }

        /// <summary>
        /// <paramref name="netlist"/> drawn as a module named <paramref name="name"/>, beside the modules of
        /// <paramref name="project"/>; with <paramref name="gates"/>, every operator and mux that opens into gates drawn as them.
        /// </summary>
        public static RtlDrawing Of(Project project, RtlNetlist netlist, string name, bool gates = false)
        {
            if (netlist == null)
                throw new ArgumentNullException(nameof(netlist));

            var drawing = new Builder(project, netlist, gates);
            Module module = drawing.Make(name);
            var modules = new List<Module>();
            foreach (Module m in project.Modules)
                if (m.Name != name)
                    modules.Add(m);
            modules.Add(module);
            return new RtlDrawing(project with { Modules = modules }, module, drawing);
        }

        private sealed class Builder
        {
            private readonly Project _project;
            private readonly RtlNetlist _netlist;
            private readonly bool _gates;
            private readonly List<Part> _parts = new List<Part>();
            private readonly List<Wire> _wires = new List<Wire>();
            private int _next = 1;

            /// <summary>The netlist's net each wire made carries, by the wire's id.</summary>
            public readonly Dictionary<int, int> NetOfWire = new Dictionary<int, int>();

            /// <summary>The instance each block made is, by the part's id.</summary>
            public readonly Dictionary<int, string> InstanceOf = new Dictionary<int, string>();

            /// <summary>The wires inside openings into gates, in the order they are worked out, and which each wire made is.</summary>
            public readonly List<InnerNet> Inner = new List<InnerNet>();
            public readonly Dictionary<int, int> InnerOfWire = new Dictionary<int, int>();

            /// <summary>How many parts were opened into gates.</summary>
            public int Opened;

            // The wires inside the openings, made once every part is placed: from a part's pin to another's, and which inner wire.
            private readonly List<(int From, string FromPin, int To, string ToPin, int Inner)> _innerWires =
                new List<(int, string, int, string, int)>();

            // A number split into its bits once, however many openings read it, so they tap the same bit lines: the splitter
            // of each net split, and the wire inside each of its bits is. Six openings of an ALU each splitting a and b made a
            // dozen splitters, every one fanned out to the same gates.
            private readonly Dictionary<int, int> _splitterOf = new Dictionary<int, int>();
            private readonly Dictionary<(int Net, int Bit), int> _bitOf = new Dictionary<(int, int), int>();

            // Where each net comes from -- a part's pin -- and each place it goes; and a join of one driver, a wire, merged.
            private readonly (int Part, string Pin)[] _source;
            private readonly List<(int Part, string Pin)>[] _loads;
            private readonly int[] _same;

            // The parts that stand first and last, and the registers, for the layout.
            private readonly HashSet<int> _first = new HashSet<int>();
            private readonly HashSet<int> _last = new HashSet<int>();
            private readonly HashSet<int> _registers = new HashSet<int>();

            public Builder(Project project, RtlNetlist netlist, bool gates)
            {
                _project = project;
                _netlist = netlist;
                _gates = gates;
                int nets = netlist.Nets.Count;
                _source = new (int, string)[nets];
                _loads = new List<(int, string)>[nets];
                _same = new int[nets];
                for (int i = 0; i < nets; i++)
                {
                    _source[i] = (-1, null);
                    _loads[i] = new List<(int, string)>();
                    _same[i] = i;
                }
            }

            public Module Make(string name)
            {
                // A join of one driver over the whole of its signal is a wire: the driver's net is the signal's. So is what only
                // changes a value's sign, widens it with 0s, or takes the truth of a bit that is a 1 or a 0 -- the wire they
                // stand on carries a value its reader reads as the same, as a synthesis tool's RTL view draws them. Nothing that
                // changes what a reader sees is merged: a sign extended, a 2-state hold, the truth of a bit that may be x (the
                // review of #108: the wire showed x where the mux took its else), nor one signal into another across glue.
                // The joins first, so a glue cell whose value is a signal's is seen to be one whichever came first.
                foreach (RtlCell cell in _netlist.Cells)
                    if (IsWire(cell))
                    {
                        Fold(cell.Inputs[0], cell.Output);
                        _merged.Add(cell.Id);
                    }
                foreach (RtlCell cell in _netlist.Cells)
                    if (IsGlue(cell) && _netlist.Nets[cell.Output].Signal < 0 && Root(cell.Output) == cell.Output)
                    {
                        Fold(cell.Output, Root(cell.Inputs[0]));
                        _merged.Add(cell.Id);
                    }

                // A case label's constant is said on its match, = 01, rather than drawn and wired to it.
                foreach (RtlCell cell in _netlist.Cells)
                    if (cell.Kind == RtlKind.Match && LabelOf(cell) != null)
                        _absorbed.Add((cell.Id, 1));
                var reads = new int[_netlist.Nets.Count];
                foreach (RtlCell cell in _netlist.Cells)
                    if (!_merged.Contains(cell.Id))
                        for (int i = 0; i < cell.Inputs.Count; i++)
                            if (cell.Inputs[i] >= 0 && !_absorbed.Contains((cell.Id, i)))
                                reads[Root(cell.Inputs[i])]++;
                foreach (int output in _netlist.Outputs)
                    reads[Root(output)]++;
                FindCases(reads);

                foreach (int input in _netlist.Inputs)
                {
                    RtlNet net = _netlist.Nets[input];
                    int id = Add(new PortPart { Name = Identifier(net.Name), Width = net.Width });
                    _first.Add(_parts.Count - 1);
                    _source[input] = (id, "out");
                }

                foreach (RtlCell cell in _netlist.Cells)
                {
                    if (_merged.Contains(cell.Id) || _swallowed.Contains(cell.Id))
                        continue;
                    if (_cases.TryGetValue(cell.Id, out (int Subject, int[] Data) choice))
                    {
                        // A case on one subject, one mux of as many inputs as the subject has values, the subject its select.
                        var chooser = new MuxPart { Inputs = choice.Data.Length, Width = WidthOf(cell.Output) };
                        if (_gates)
                        {
                            var feeds = new List<(string, int)> { ("s", choice.Subject) };
                            for (int i = 0; i < choice.Data.Length; i++)
                                feeds.Add(("d" + i.ToString(CultureInfo.InvariantCulture), choice.Data[i]));
                            if (Open(chooser, feeds, cell.Output))
                                continue;
                        }
                        int mux = Add(chooser);
                        _loads[Root(choice.Subject)].Add((mux, "s"));
                        for (int i = 0; i < choice.Data.Length; i++)
                            _loads[Root(choice.Data[i])].Add((mux, "d" + i.ToString(CultureInfo.InvariantCulture)));
                        _source[Root(cell.Output)] = (mux, "y");
                        continue;
                    }
                    if (cell.Kind == RtlKind.Constant && reads[Root(cell.Output)] == 0 && _netlist.Nets[cell.Output].Signal < 0)
                        continue;   // said on the matches that use it
                    Part part = PartFor(cell, out string[] ins, out string[] outs);
                    if (_gates && (part is OperatorPart || part is MuxPart) && cell.Outputs.Count == 1)
                    {
                        var feeds = new List<(string, int)>();
                        for (int i = 0; i < cell.Inputs.Count && i < ins.Length; i++)
                            if (cell.Inputs[i] >= 0 && ins[i] != null && !_absorbed.Contains((cell.Id, i)))
                                feeds.Add((ins[i], cell.Inputs[i]));
                        if (Open(part, feeds, cell.Output))
                            continue;
                    }
                    int id = Add(part);
                    if (cell.Kind == RtlKind.Instance)
                        InstanceOf[id] = cell.Name;
                    if (cell.Kind == RtlKind.Register)
                        _registers.Add(_parts.Count - 1);
                    for (int i = 0; i < cell.Inputs.Count && i < ins.Length; i++)
                        if (cell.Inputs[i] >= 0 && ins[i] != null && !_absorbed.Contains((cell.Id, i)))
                            _loads[Root(cell.Inputs[i])].Add((id, ins[i]));
                    for (int i = 0; i < cell.Outputs.Count && i < outs.Length; i++)
                        if (outs[i] != null)
                            _source[Root(cell.Outputs[i])] = (id, outs[i]);
                }

                foreach (int output in _netlist.Outputs)
                {
                    RtlNet net = _netlist.Nets[output];
                    int id = Add(new PortPart { Name = Identifier(net.Name), Direction = PortDirection.Output, Width = net.Width });
                    _last.Add(_parts.Count - 1);
                    _loads[Root(output)].Add((id, "in"));
                }

                // A net something reads that nothing here drives -- a signal given its value in another scope: a box of its name.
                for (int net = 0; net < _netlist.Nets.Count; net++)
                {
                    if (Root(net) != net || _loads[net].Count == 0 || _source[net].Part >= 0)
                        continue;
                    RtlNet from = _netlist.Nets[net];
                    int id = Add(new BoxPart { Label = from.Name, Ports = new[] { new Pin("y", PinDirection.Out, from.Width) } });
                    _first.Add(_parts.Count - 1);
                    _source[net] = (id, "y");
                }

                // A wire from each net's source to each place it goes, named for the signal the net is.
                var links = new List<(int From, int To)>();
                var index = new Dictionary<int, int>();
                for (int i = 0; i < _parts.Count; i++)
                    index[_parts[i].Id] = i;
                for (int net = 0; net < _netlist.Nets.Count; net++)
                {
                    if (Root(net) != net || _source[net].Part < 0)
                        continue;
                    string netName = NameOfNet(net);
                    foreach ((int part, string pin) in _loads[net])
                    {
                        NetOfWire[_next] = net;
                        _wires.Add(new Wire
                        {
                            Id = _next++,
                            From = Endpoint.At(_source[net].Part, _source[net].Pin),
                            To = Endpoint.At(part, pin),
                            NetName = netName,
                        });
                        links.Add((index[_source[net].Part], index[part]));
                    }
                }

                // The wires inside the openings into gates, on no net of the netlist: named for none, valued by Inner.
                foreach ((int from, string fromPin, int to, string toPin, int inner) in _innerWires)
                {
                    InnerOfWire[_next] = inner;
                    _wires.Add(new Wire { Id = _next++, From = Endpoint.At(from, fromPin), To = Endpoint.At(to, toPin) });
                    links.Add((index[from], index[to]));
                }

                Placement[] placements = LayeredLayout.Arrange(_project, _parts, links, _first, _last, _registers);
                var placed = new Dictionary<int, Placement>();
                for (int i = 0; i < _parts.Count; i++)
                    placed[_parts[i].Id] = placements[i];

                return new Module
                {
                    Name = name,
                    Parts = _parts.ToArray(),
                    Wires = _wires.ToArray(),
                    Layout = new Layout { Parts = placed },
                    NextId = _next,
                };
            }

            /// <summary>The net a net is drawn as: its own, or the signal's a join of it alone makes it.</summary>
            private int Root(int net)
            {
                while (_same[net] != net)
                    net = _same[net];
                return net;
            }

            /// <summary>The name a net's wires go by: the signal's it is, the one it is the same as, or none for a value inside an expression.</summary>
            private string NameOfNet(int net)
            {
                if (_names == null)
                {
                    // Once, for every net: the first signal folded into each (the review of #108: asked a net at a time, it was
                    // the square of the nets).
                    _names = new string[_same.Length];
                    for (int other = 0; other < _same.Length; other++)
                    {
                        int root = Root(other);
                        if (_netlist.Nets[other].Signal >= 0 && (_names[root] == null || other == root))
                            _names[root] = _netlist.Nets[other].Name;
                    }
                }
                return _names[net];
            }

            private string[] _names;

            private bool IsWire(RtlCell cell) =>
                cell.Kind == RtlKind.Join && cell.Inputs.Count == 1 && cell.Lowests.Count == 1 && cell.Lowests[0] == 0
                && _netlist.Nets[cell.Inputs[0]].Width == _netlist.Nets[cell.Output].Width
                && !cell.Signed && !cell.TwoStateTarget && _netlist.Nets[cell.Inputs[0]].Driver >= 0;

            /// <summary>
            /// What a drawing may draw as the wire it stands on, its reader seeing what the wire shows: a change of sign, a
            /// widening with 0s, the truth of a bit that is a 1 or a 0 -- a match's, another truth's, a constant's.
            /// </summary>
            private bool IsGlue(RtlCell cell)
            {
                if (cell.Inputs.Count != 1 || cell.Inputs[0] < 0 || cell.Outputs.Count != 1)
                    return false;
                switch (cell.Kind)
                {
                    case RtlKind.Signedness:
                        return true;
                    case RtlKind.Resize:
                        return !cell.Signed && _netlist.Nets[cell.Output].Width >= _netlist.Nets[cell.Inputs[0]].Width;
                    case RtlKind.Truth:
                        RtlKind? from = Driver(cell.Inputs[0])?.Kind;
                        return _netlist.Nets[cell.Inputs[0]].Width == 1 && (from == RtlKind.Match || from == RtlKind.Truth || from == RtlKind.Constant);
                    default:
                        return false;
                }
            }

            private readonly HashSet<int> _merged = new HashSet<int>();

            /// <summary><paramref name="drop"/> drawn as <paramref name="keep"/>: the net it was folded into is the one whose name and value it shows.</summary>
            private void Fold(int drop, int keep)
            {
                int rd = Root(drop), rk = Root(keep);
                if (rd != rk)
                    _same[rd] = rk;
            }

            private readonly HashSet<(int Cell, int Pin)> _absorbed = new HashSet<(int, int)>();

            // A case's chain of muxes on one subject, by its first mux: the subject, and what each of its values chooses; and the
            // muxes and matches inside a chain, which the one mux is drawn for.
            private readonly Dictionary<int, (int Subject, int[] Data)> _cases = new Dictionary<int, (int, int[])>();
            private readonly HashSet<int> _swallowed = new HashSet<int>();

            /// <summary>
            /// Every case drawn as the mux it is (Part 15h-4): a chain of muxes, each on whether one subject matches a constant
            /// label and otherwise the next, on a subject of up to three bits whose labels are values of it -- a mux of as many
            /// inputs as the subject has values, each the arm its label chooses, the rest the default. Every value it gives is
            /// the chain's; only an x or a z on the subject, which no label is, chooses otherwise.
            /// </summary>
            private void FindCases(int[] reads)
            {
                var inner = new HashSet<int>();
                foreach (RtlCell cell in _netlist.Cells)
                    if (cell.Kind == RtlKind.Mux && Arm(cell, out _, out _, out _) && Driver(cell.Inputs[2]) is RtlCell rest
                        && rest.Kind == RtlKind.Mux && reads[Root(cell.Inputs[2])] == 1 && Arm(rest, out _, out _, out _))
                        inner.Add(rest.Id);

                foreach (RtlCell head in _netlist.Cells)
                {
                    if (head.Kind != RtlKind.Mux || inner.Contains(head.Id))
                        continue;
                    var arms = new List<(ulong Label, int Data)>();
                    var chain = new List<int>();
                    int subject = -1, otherwise = -1;
                    RtlCell at = head;
                    var matches = new List<RtlCell>();
                    while (at != null && Arm(at, out int s, out ulong label, out RtlCell match)
                        && (subject < 0 || Root(s) == Root(subject)))
                    {
                        subject = s;
                        arms.Add((label, at.Inputs[1]));
                        matches.Add(match);
                        if (at != head)
                            chain.Add(at.Id);
                        otherwise = at.Inputs[2];
                        RtlCell next = Driver(otherwise);
                        at = next != null && next.Kind == RtlKind.Mux && inner.Contains(next.Id) ? next : null;
                    }

                    int width = subject >= 0 ? WidthOf(subject) : 0;
                    if (arms.Count < 2 || width < 1 || width > 3)
                        continue;
                    int values = 1 << width;
                    var data = new int[values];
                    for (int v = 0; v < values; v++)
                        data[v] = otherwise;
                    bool fits = true;
                    for (int k = arms.Count - 1; k >= 0; k--)
                    {
                        if (arms[k].Label >= (ulong)values)
                            fits = false;
                        else
                            data[arms[k].Label] = arms[k].Data;
                    }
                    if (!fits)
                        continue;
                    _cases[head.Id] = (subject, data);
                    foreach (int swallowed in chain)
                        _swallowed.Add(swallowed);
                    foreach (RtlCell match in matches)
                        _matchesOfCases.Add(match.Id);
                }

                // A match is drawn no more once every mux that reads it is a case's -- s and cout of one case share them.
                foreach (int id in _matchesOfCases)
                {
                    int output = _netlist.Cells[id].Output;
                    bool onlyCases = _netlist.Nets[output].Signal < 0;
                    foreach (RtlCell reader in _netlist.Cells)
                        if (Reads(reader, output) && !_cases.ContainsKey(reader.Id) && !_swallowed.Contains(reader.Id))
                            onlyCases = false;
                    if (onlyCases)
                        _swallowed.Add(id);
                }
            }

            private readonly HashSet<int> _matchesOfCases = new HashSet<int>();

            private static bool Reads(RtlCell cell, int net)
            {
                foreach (int input in cell.Inputs)
                    if (input == net)
                        return true;
                return false;
            }

            /// <summary>Whether a mux is a case's arm: on whether a subject matches a constant label, all of it known.</summary>
            private bool Arm(RtlCell mux, out int subject, out ulong label, out RtlCell match)
            {
                subject = -1;
                label = 0;
                match = Driver(mux.Inputs[0]);
                if (match == null || match.Kind != RtlKind.Match || LabelOf(match) == null)
                    return false;
                RtlCell constant = Driver(match.Inputs[1]);
                if (!constant.Value.IsKnown || !constant.Value.TryGetUInt64(out label))
                    return false;
                subject = match.Inputs[0];
                return true;
            }

            private RtlCell Driver(int net) => net >= 0 && _netlist.Nets[net].Driver >= 0 ? _netlist.Cells[_netlist.Nets[net].Driver] : null;

            /// <summary>A match's label when it is a constant, as written: 01; null when it is not.</summary>
            private string LabelOf(RtlCell match)
            {
                int label = match.Inputs.Count > 1 ? match.Inputs[1] : -1;
                int driver = label >= 0 ? _netlist.Nets[label].Driver : -1;
                if (driver < 0 || _netlist.Cells[driver].Kind != RtlKind.Constant)
                    return null;
                return _netlist.Cells[driver].Value.Format(Radix.Binary);
            }

            /// <summary>
            /// <paramref name="part"/> drawn as its gates (<see cref="RtlGates.Open"/>) in its place: each pin's net,
            /// <paramref name="feeds"/>, wired to what reads that port inside, and what drives the opening's output to
            /// <paramref name="output"/>'s readers. False, with nothing added, for a part that stays a box, or whose pins'
            /// widths are not its nets'.
            /// </summary>
            private bool Open(Part part, List<(string Pin, int Net)> feeds, int output)
            {
                // A sum or a difference of two numbers each widened by a bit with 0s, {cout, s} = a + b: the ripple of their own
                // width, its carry the top bit, wired from the numbers as they were.
                Project opening;
                if (part is OperatorPart carried && (carried.Op == OperatorKind.Add || carried.Op == OperatorKind.Subtract) && !carried.Signed
                    && feeds.Count == 2 && WidenedByABit(feeds[0].Net, carried.Width, out int a) && WidenedByABit(feeds[1].Net, carried.Width, out int b))
                {
                    opening = RtlGates.OpenWithCarry(carried.Op, carried.Width - 1, "gates", out _);
                    feeds = new List<(string, int)> { (feeds[0].Pin, a), (feeds[1].Pin, b) };
                }
                else
                    opening = RtlGates.Open(part, "gates", out _);
                Module inside = opening?.TopModule;
                if (inside == null || output < 0)
                    return false;

                // Its ports: each input the net of the pin of its name, the output's width the net's.
                var inputs = new Dictionary<int, int>();
                int y = -1;
                foreach (Part p in inside.Parts)
                {
                    if (!(p is PortPart port))
                        continue;
                    if (port.Direction == PortDirection.Output)
                    {
                        if (port.Width != WidthOf(output))
                            return false;
                        y = port.Id;
                        continue;
                    }
                    int net = -1;
                    foreach ((string pin, int fed) in feeds)
                        if (pin == port.Name)
                            net = fed;
                    if (net < 0 || WidthOf(net) != port.Width)
                        return false;
                    inputs[port.Id] = net;
                }
                if (y < 0)
                    return false;

                // What each pin inside reads, and what drives the output; every input of every part inside read.
                var into = new Dictionary<PinRef, PinRef>();
                PinRef? last = null;
                foreach (Wire wire in inside.Wires)
                {
                    if (wire.From.IsTap || wire.To.IsTap || inputs.ContainsKey(wire.From.Pin.Part) && wire.To.Pin.Part == y)
                        return false;
                    into[wire.To.Pin] = wire.From.Pin;
                    if (wire.To.Pin.Part == y)
                        last = wire.From.Pin;
                }
                if (last == null)
                    return false;
                foreach (Part p in inside.Parts)
                    if (!(p is PortPart))
                        foreach (Pin pin in p.Pins(null))
                            if (pin.Direction == PinDirection.In && !into.ContainsKey(new PinRef(p.Id, pin.Name)))
                                return false;

                // The opening's splitters of its ports, each the drawing's own splitter of that net where it has one.
                var splits = new Dictionary<int, int>();
                foreach (Wire wire in inside.Wires)
                    if (inputs.TryGetValue(wire.From.Pin.Part, out int split) && inside.FindPart(wire.To.Pin.Part) is SplitterPart)
                        splits[wire.To.Pin.Part] = split;
                var ids = new Dictionary<int, int>();
                var reused = new HashSet<int>();
                foreach (Part p in inside.Parts)
                {
                    if (p is PortPart)
                        continue;
                    if (splits.TryGetValue(p.Id, out int split) && _splitterOf.TryGetValue(split, out int splitter))
                    {
                        ids[p.Id] = splitter;
                        reused.Add(p.Id);
                        continue;
                    }
                    ids[p.Id] = Add(p with { Name = null });
                    if (splits.TryGetValue(p.Id, out split))
                        _splitterOf[split] = ids[p.Id];
                }
                var inner = new Dictionary<PinRef, int>();

                // A place a wire inside starts from: the netlist's net of a port, the code's value where it drives the output,
                // or a wire inside, worked out after every wire inside it reads.
                int Ref(PinRef from) =>
                    inputs.TryGetValue(from.Part, out int net) ? net : from.Equals(last.Value) ? output : -1 - InnerOf(from);

                int InnerOf(PinRef from)
                {
                    if (inner.TryGetValue(from, out int k))
                        return k;
                    Part p = inside.FindPart(from.Part);

                    // A bit of a number split: the same wire inside whichever opening split it first.
                    if (splits.TryGetValue(from.Part, out int split))
                    {
                        int bit = int.Parse(from.Pin.Substring(1), NumberStyles.None, CultureInfo.InvariantCulture);
                        if (!_bitOf.TryGetValue((split, bit), out k))
                        {
                            k = Inner.Count;
                            Inner.Add(new InnerNet(p, from.Pin, new[] { split }));
                            _bitOf[(split, bit)] = k;
                        }
                        inner[from] = k;
                        return k;
                    }
                    var reads = new List<int>();
                    foreach (Pin pin in p.Pins(null))
                        if (pin.Direction == PinDirection.In)
                            reads.Add(Ref(into[new PinRef(p.Id, pin.Name)]));
                    k = Inner.Count;
                    Inner.Add(new InnerNet(p, from.Pin, reads.ToArray()));
                    inner[from] = k;
                    return k;
                }

                foreach (Wire wire in inside.Wires)
                {
                    PinRef from = wire.From.Pin, to = wire.To.Pin;
                    if (to.Part == y)
                        _source[Root(output)] = (ids[from.Part], from.Pin);
                    else if (inputs.TryGetValue(from.Part, out int net))
                    {
                        if (!reused.Contains(to.Part))   // a splitter shared is wired from its net once
                            _loads[Root(net)].Add((ids[to.Part], to.Pin));
                    }
                    else if (from.Equals(last.Value))
                        _loads[Root(output)].Add((ids[to.Part], to.Pin));
                    else
                        _innerWires.Add((ids[from.Part], from.Pin, ids[to.Part], to.Pin, InnerOf(from)));
                }
                Opened++;
                return true;
            }

            /// <summary>Whether <paramref name="net"/> is a number widened by one bit with 0s into <paramref name="width"/>, and the number.</summary>
            private bool WidenedByABit(int net, int width, out int narrow)
            {
                RtlCell resize = Driver(net);
                narrow = resize != null && resize.Kind == RtlKind.Resize && !resize.Signed && resize.Inputs.Count == 1 ? resize.Inputs[0] : -1;
                return narrow >= 0 && WidthOf(narrow) == width - 1;
            }

            private int Add(Part part)
            {
                int id = _next++;
                // A part with no name of its own is named as no signal can be, so a signal p3 keeps its name on its net (the
                // review of #108: a carry-lookahead's p3 became p3_2, read by no name).
                _parts.Add(part with { Id = id, Name = part.Name ?? "$p" + id.ToString(CultureInfo.InvariantCulture) });
                return id;
            }

            private int WidthOf(int net) => net >= 0 ? _netlist.Nets[net].Width : 1;

            /// <summary>The board's part for a cell, and the pins its inputs and outputs are, in the cell's order.</summary>
            private Part PartFor(RtlCell cell, out string[] ins, out string[] outs)
            {
                int width = cell.Output >= 0 ? WidthOf(cell.Output) : 1;
                outs = new[] { "y" };
                switch (cell.Kind)
                {
                    case RtlKind.Constant:
                        ins = Array.Empty<string>();
                        return new ConstantPart { Value = cell.Value };

                    case RtlKind.Gate when cell.Inputs.Count >= 1 && cell.Inputs.Count <= GatePart.MaxInputs:
                    {
                        ins = new string[cell.Inputs.Count];
                        for (int i = 0; i < ins.Length; i++)
                            ins[i] = GatePart.InputPin(cell.Gate, i);
                        return new GatePart { Name = Identifier(cell.Name), Kind = cell.Gate, Inputs = cell.Inputs.Count, Width = width };
                    }

                    case RtlKind.Unary when Operators.TryFrom(cell.Unary, out OperatorKind unary):
                        ins = new[] { "a" };
                        return new OperatorPart { Op = unary, Width = WidthOf(cell.Inputs[0]) };

                    case RtlKind.Binary when Operators.TryFrom(cell.Binary, out OperatorKind binary):
                    {
                        ins = new[] { "a", "b" };
                        int a = WidthOf(cell.Inputs[0]), b = WidthOf(cell.Inputs[1]);
                        return new OperatorPart { Op = binary, Width = a, BWidth = Operators.BOwnWidth(binary) && b != a ? b : 0 };
                    }

                    case RtlKind.Mux:
                        ins = new[] { "s", "d1", "d0" };
                        return new MuxPart { Inputs = 2, Width = width };

                    case RtlKind.Register when cell.Clock > 0 && (cell.Inputs.Count == 2 || cell.Inputs.Count == 3):
                    {
                        // The clock on clk, whichever edge it waits on first; the edge the body tests, its reset (the review of
                        // #108: the two were swapped).
                        ins = new string[cell.Inputs.Count];
                        ins[0] = "d";
                        ins[cell.Clock] = "clk";
                        if (cell.Inputs.Count == 3)
                            ins[3 - cell.Clock] = "rst";
                        outs = new[] { "q" };
                        return new RegisterPart { Width = width, HasReset = cell.Inputs.Count == 3 };   // its name is its signal's, which is the net's
                    }

                    case RtlKind.Instance:
                    {
                        // One of the gate library's cells is the gate it is, its pins its own (A, B, Z); a module of the
                        // project a block of it; any other -- a module only the compiled code has -- a box of its pins.
                        if (CellLibrary.TryKindOf(cell.Module, out GateKind kind, out int inputs) && cell.InputNames.Count == inputs)
                        {
                            ins = new string[inputs];
                            for (int i = 0; i < inputs; i++)
                                ins[i] = GatePart.InputPin(kind, i);
                            int at = -1;
                            for (int i = 0; i < cell.OutputNames.Count; i++)
                                if (cell.OutputNames[i] == CellLibrary.Output)
                                    at = i;
                            outs = new string[cell.OutputNames.Count];
                            if (at >= 0)
                                outs[at] = "y";
                            return new GatePart { Name = Identifier(cell.Name), Kind = kind, Inputs = inputs, Width = 1 };
                        }
                        ins = Copy(cell.InputNames);
                        outs = Copy(cell.OutputNames);
                        if (_project.Find(cell.Module) != null)
                            return new BlockPart { Name = Identifier(cell.Name), Module = cell.Module };
                        var pins = new List<Pin>();
                        for (int i = 0; i < ins.Length; i++)
                            pins.Add(new Pin(ins[i], PinDirection.In, WidthOf(cell.Inputs[i])));
                        for (int i = 0; i < outs.Length; i++)
                            pins.Add(new Pin(outs[i], PinDirection.Out, WidthOf(cell.Outputs[i])));
                        return new BoxPart { Name = Identifier(cell.Name), Label = cell.Module, Ports = pins.ToArray() };
                    }

                    default:
                        return Box(cell, out ins, out outs);
                }
            }

            /// <summary>A box saying what the cell is, a pin for each of its inputs and outputs.</summary>
            private Part Box(RtlCell cell, out string[] ins, out string[] outs)
            {
                string label;
                string[] inNames = null, outNames = null;
                switch (cell.Kind)
                {
                    case RtlKind.Slice:
                        label = cell.Width == 1 ? "[" + cell.Lowest + "]" : "[" + (cell.Lowest + cell.Width - 1) + ":" + cell.Lowest + "]";
                        break;
                    case RtlKind.Concat:
                        label = "{ , }";
                        inNames = new[] { "hi", "lo" };
                        break;
                    case RtlKind.Replicate:
                        label = "{" + cell.Count + "{ }}";
                        break;
                    case RtlKind.Bit:
                        label = "[i]";
                        inNames = new[] { "a", "i" };
                        break;
                    case RtlKind.Part:
                        label = "[i +: " + cell.Width + "]";
                        inNames = new[] { "a", "i" };
                        break;
                    case RtlKind.Resize:
                        label = cell.Signed ? "sign ext" : "ext";
                        break;
                    case RtlKind.Signedness:
                        label = cell.Signed ? "$signed" : "$unsigned";
                        break;
                    case RtlKind.TwoState:
                        label = "2-state";
                        break;
                    case RtlKind.Truth:
                        label = "true";
                        break;
                    case RtlKind.Match:
                        label = LabelOf(cell) is string literal ? "= " + literal : "match";
                        inNames = new[] { "a", "label" };
                        break;
                    case RtlKind.Put:
                        label = "[i] =";
                        inNames = new[] { "old", "v", "i" };
                        break;
                    case RtlKind.Word:
                        label = cell.Name + "[ ]";
                        inNames = new[] { "addr" };
                        break;
                    case RtlKind.Join:
                        label = cell.Name;
                        inNames = new string[cell.Inputs.Count];
                        for (int i = 0; i < inNames.Length; i++)
                        {
                            int lowest = cell.Lowests[i], pieceWidth = i < cell.PieceWidths.Count ? cell.PieceWidths[i] : WidthOf(cell.Inputs[i]);
                            string range = pieceWidth == 1 ? "[" + lowest + "]" : "[" + (lowest + pieceWidth - 1) + ":" + lowest + "]";
                            inNames[i] = range + (Count(cell.Lowests, lowest) > 1 ? "." + i : "");
                        }
                        break;
                    case RtlKind.Process:
                    {
                        // A signal it reads and writes -- a clock it turns over, q = q + d -- is a pin each way, the way out primed.
                        label = cell.Name;
                        inNames = Copy(cell.InputNames);
                        outNames = Copy(cell.OutputNames);
                        var taken = new HashSet<string>(inNames);
                        for (int i = 0; i < outNames.Length; i++)
                            while (!taken.Add(outNames[i]))
                                outNames[i] += "'";
                        break;
                    }
                    default:
                        label = cell.Kind == RtlKind.Opaque ? "f( )" : cell.Kind.ToString().ToLowerInvariant();
                        break;
                }

                ins = inNames ?? Numbered("a", cell.Inputs.Count);
                outs = outNames ?? Numbered("y", cell.Outputs.Count);
                var pins = new List<Pin>();
                for (int i = 0; i < cell.Inputs.Count && i < ins.Length; i++)
                    pins.Add(new Pin(ins[i], PinDirection.In, WidthOf(cell.Inputs[i])));
                for (int i = 0; i < cell.Outputs.Count && i < outs.Length; i++)
                    pins.Add(new Pin(outs[i], PinDirection.Out, WidthOf(cell.Outputs[i])));
                return new BoxPart { Label = label, Ports = pins.ToArray(), Says = cell.Why };
            }

            private static int Count(IReadOnlyList<int> list, int value)
            {
                int n = 0;
                foreach (int v in list)
                    if (v == value)
                        n++;
                return n;
            }

            private static string[] Numbered(string stem, int count)
            {
                if (count == 1)
                    return new[] { stem };
                var names = new string[count];
                for (int i = 0; i < count; i++)
                    names[i] = stem + i.ToString(CultureInfo.InvariantCulture);
                return names;
            }

            private static string[] Copy(IReadOnlyList<string> names)
            {
                var copy = new string[names.Count];
                for (int i = 0; i < copy.Length; i++)
                    copy[i] = names[i];
                return copy;
            }

            /// <summary>A name a part may have: the signal's, an instance's or a gate's, with what is no identifier's made one.</summary>
            private static string Identifier(string name)
            {
                if (string.IsNullOrEmpty(name))
                    return null;
                var text = new System.Text.StringBuilder(name.Length);
                foreach (char c in name)
                    text.Append(char.IsLetterOrDigit(c) || c == '_' ? c : '_');
                return text.ToString();
            }
        }
    }
}
