using System;
using System.Collections.Generic;
using System.Text;
using BitSorter.Sim;
using BitSorter.SystemVerilog;
using BitSorter.SystemVerilog.Syntax;

namespace BitSorter.Board
{
    /// <summary>A 4.x board made into a document, and how its vectors now fall in time.</summary>
    public sealed class MigrationResult
    {
        internal MigrationResult(Project project, long cycle, int vectors, IReadOnlyDictionary<string, string> ports, IReadOnlyList<string> notes)
        {
            Project = project;
            Cycle = cycle;
            Vectors = vectors;
            PortOf = ports;
            Notes = notes;
        }

        public Project Project { get; }

        /// <summary>How long each vector has: its inputs change at the start, a clock rises half-way.</summary>
        public long Cycle { get; }

        /// <summary>How many vectors the stimulus runs: the streams, or the expectations if they run longer.</summary>
        public int Vectors { get; }

        /// <summary>Each fixture's port, by the fixture's 4.x id.</summary>
        public IReadOnlyDictionary<string, string> PortOf { get; }

        /// <summary>What did not come across as it was, in words.</summary>
        public IReadOnlyList<string> Notes { get; }

        /// <summary>
        /// When vector <paramref name="vector"/>'s outputs are read: the last moment before its clock
        /// edge, when everything since its inputs changed has settled -- what a 4.x bin received for it.
        /// </summary>
        public long SampleTime(int vector) => vector * Cycle + Cycle / 2 - 1;

        /// <summary>
        /// Runs the document and reads each bin at each vector against what was expected of it: every
        /// '0' and '1' (an 'x' or a '-' asks nothing). Returns what disagreed, in words, or why it could
        /// not run; empty when the board does what it did.
        /// </summary>
        public List<string> Mismatches(IEnumerable<LegacyExpectation> expected)
        {
            var wrong = new List<string>();
            CompiledProject compiled = BoardCompiler.Compile(Project);
            if (!compiled.Succeeded)
            {
                foreach (BoardDiagnostic d in compiled.Diagnostics)
                    if (d.IsError)
                        wrong.Add("it does not compile: " + d.Message);
                return wrong;
            }

            Elaboration elaboration = Elaborator.Elaborate(compiled.Tree, compiled.Top);
            if (elaboration.HasErrors)
            {
                foreach (Diagnostic d in elaboration.Diagnostics)
                    wrong.Add("it does not elaborate: " + d);
                return wrong;
            }

            var sim = new Simulator(elaboration.Design);
            sim.RunTo(Project.Stimulus.End + Cycle);

            foreach (LegacyExpectation expectation in expected ?? Array.Empty<LegacyExpectation>())
            {
                if (expectation?.values == null || !PortOf.TryGetValue(expectation.sink ?? "", out string port))
                    continue;

                SignalId signal = elaboration.Design.Find(compiled.Testbench + "." + port);
                for (int v = 0; v < expectation.values.Length; v++)
                {
                    char want = expectation.values[v];
                    if (want != '0' && want != '1')
                        continue;
                    string got = sim.History.ValueAt(signal, SampleTime(v)).Format(Radix.Binary);
                    if (got != want.ToString())
                        wrong.Add($"{expectation.sink} at vector {v}: {got}, not {want}");
                }
            }

            return wrong;
        }
    }

    /// <summary>
    /// A 4.x board made into a 5.0 document (section 8): what migration promises is every output's
    /// settled value after each vector, not the exact tick.
    /// </summary>
    /// <remarks>
    /// **Geometry moves one to one**: a cell is a position in cells. A source is an input port, its
    /// stream a track of the stimulus; a bin is an output port.
    ///
    /// **Delay moves by rule.** In 4.x a gate took no time and every wire at least one tick; in 5.0 a
    /// gate is a cell of delay 1 and a wire is ideal, so a wire of delay d above 1 becomes a buffer of
    /// d - 1 on its way.
    ///
    /// **A register is a flip-flop starting at 0**, as every 4.x register did, on a clock port the
    /// stimulus drives. Each vector has a cycle long enough for any path to settle -- twice the sum of
    /// every delay on the board, at least ten -- its inputs change at its start, its clock rises half-way,
    /// and its outputs are read just before that edge. So a register hands on, during a vector, the bit
    /// it was given in the one before: 4.x's "a register's k-th output is the bit it was given on cycle
    /// k-1". 4.x's own clock period was the spacing of tokens, and means nothing here.
    ///
    /// **A block is a module**, one for each different block, with its ports named as the block's were.
    ///
    /// A board that only worked by collision or stalling has no meaning in 5.0; it still comes across,
    /// and shows what it now does. Two wires into one input are two drivers on a net, an error the
    /// document will explain; the migration does not guess which to keep.
    /// </remarks>
    public static class Migrator
    {
        /// <summary>Every migrated gate's rise and fall: a library cell's default.</summary>
        public const long GateDelay = 1;

        public static MigrationResult Migrate(LegacyCircuit circuit)
        {
            if (circuit == null)
                throw new ArgumentNullException(nameof(circuit));
            return new Run(circuit).Migrate();
        }

        /// <summary>A name SystemVerilog takes, made from whatever 4.x called something.</summary>
        public static string NameFrom(string text, string fallback)
        {
            var name = new StringBuilder();
            foreach (char c in text ?? string.Empty)
                name.Append(char.IsLetterOrDigit(c) && c < 128 || c == '_' ? c : '_');

            string made = name.ToString().Trim('_');
            if (made.Length == 0)
                made = fallback;
            if (char.IsDigit(made[0]))
                made = fallback + "_" + made;
            if (Keywords.IsKeyword(made))
                made += "_";
            return made;
        }

        private sealed class Run
        {
            private readonly LegacyCircuit _circuit;
            private readonly List<string> _notes = new List<string>();
            private readonly Dictionary<string, Module> _blockModules = new Dictionary<string, Module>(StringComparer.Ordinal);
            private readonly HashSet<string> _moduleNames = new HashSet<string>(StringComparer.Ordinal);
            private long _delays;

            public Run(LegacyCircuit circuit) => _circuit = circuit;

            public MigrationResult Migrate()
            {
                string top = NameFrom(_circuit.name, "board");
                _moduleNames.Add(top);

                var b = new ModuleBuilder(top);
                var ports = new Dictionary<string, string>(StringComparer.Ordinal);
                var cells = new Dictionary<(int, int), (int Id, Func<int, bool, string> Pin)>();

                foreach (LegacyFixture fixture in _circuit.fixtures ?? new LegacyFixture[0])
                {
                    string name = b.Unique(NameFrom(fixture.id, fixture.source ? "in" : "out"));
                    int id = b.Add(new PortPart
                    {
                        Name = name,
                        Direction = fixture.source ? PortDirection.Input : PortDirection.Output,
                    }, fixture.x, fixture.y);
                    ports[fixture.id ?? name] = name;
                    bool source = fixture.source;
                    cells[(fixture.x, fixture.y)] = (id, (port, input) => input == source ? null : source ? "out" : "in");
                }

                var registers = new List<int>();
                foreach (LegacyGate gate in _circuit.gates ?? new LegacyGate[0])
                {
                    if (!TryGate(b, gate, out int id, out Func<int, bool, string> pins, out bool register))
                        continue;
                    if (register)
                        registers.Add(id);
                    cells[(gate.x, gate.y)] = (id, pins);
                }

                foreach (LegacyBlockPlacement placed in _circuit.blocks ?? new LegacyBlockPlacement[0])
                {
                    if (placed?.block == null)
                        continue;

                    Module module = BlockModule(placed.block);
                    int id = b.Add(new BlockPart { Module = module.Name }, placed.x, placed.y);
                    LegacyBlock block = placed.block;
                    cells[(placed.x, placed.y)] = (id, (port, input) =>
                    {
                        LegacyPort[] list = input ? block.inputs : block.outputs;
                        return list != null && port >= 0 && port < list.Length ? NameFrom(list[port].id, input ? "in" : "out") : null;
                    });
                    _delays += BlockDelay(module);
                }

                Wire(b, _circuit.wires, cells, "the board");

                // One clock for every register, the cycle long enough for anything to settle.
                string clock = null;
                if (registers.Count > 0)
                {
                    double x = double.MaxValue, y = double.MaxValue;
                    foreach (LegacyFixture fixture in _circuit.fixtures ?? new LegacyFixture[0])
                    {
                        if (!fixture.source)
                            continue;
                        x = Math.Min(x, fixture.x);
                        y = Math.Min(y, fixture.y);
                    }

                    clock = b.Unique("clk");
                    int clk = b.Add(new PortPart { Name = clock }, x == double.MaxValue ? 0 : x, y == double.MaxValue ? 0 : y - 1);
                    foreach (int register in registers)
                        b.Connect(Endpoint.At(clk, "out"), Endpoint.At(register, "clk"));
                }

                long cycle = Math.Max(10, 2 * (_delays + 2));
                int vectors = 0;
                foreach (LegacyFixture fixture in _circuit.fixtures ?? new LegacyFixture[0])
                    if (fixture.source)
                        vectors = Math.Max(vectors, (fixture.stream ?? "").Length);
                foreach (LegacyExpectation expectation in _circuit.expected ?? new LegacyExpectation[0])
                    vectors = Math.Max(vectors, (expectation.values ?? "").Length);

                var tracks = new List<Track>();
                foreach (LegacyFixture fixture in _circuit.fixtures ?? new LegacyFixture[0])
                {
                    if (!fixture.source || string.IsNullOrEmpty(fixture.stream))
                        continue;

                    var steps = new List<Step>();
                    char last = '\0';
                    for (int v = 0; v < fixture.stream.Length; v++)
                    {
                        char c = fixture.stream[v];
                        if (c != '0' && c != '1')
                        {
                            _notes.Add($"{fixture.id}'s stream has '{c}' at vector {v}; a source sends 0 or 1, so it holds its last value there.");
                            continue;
                        }

                        if (c != last)
                            steps.Add(new Step(v * cycle, LogicVec.FromUInt64(c == '1' ? 1UL : 0UL, 1)));
                        last = c;
                    }

                    tracks.Add(new Track { Port = ports[fixture.id], Steps = steps });
                }

                var stimulus = new Stimulus
                {
                    Tracks = tracks,
                    Clocks = clock == null ? Array.Empty<ClockTrack>() : new[] { new ClockTrack { Port = clock, Period = cycle } },
                    End = Math.Max(1, vectors) * cycle,
                };

                var modules = new List<Module>(_blockModules.Values);
                modules.Add(b.Build());
                var project = new Project { Name = top, Modules = modules, Top = top, Stimulus = stimulus };

                return new MigrationResult(project, cycle, vectors, ports, _notes);
            }

            private bool TryGate(ModuleBuilder b, LegacyGate gate, out int id, out Func<int, bool, string> pins, out bool register)
            {
                id = 0;
                pins = null;
                register = false;

                string kind = (gate.kind ?? "").ToLowerInvariant();
                if (kind == "register" || kind == "reg")
                {
                    register = true;
                    id = b.Add(new RegisterPart { Initial = LogicVec.FromUInt64(0, 1) }, gate.x, gate.y);
                    pins = (port, input) => port != 0 ? null : input ? "d" : "q";
                    return true;
                }

                GateKind primitive;
                switch (kind)
                {
                    case "not": primitive = GateKind.Not; break;
                    case "and": primitive = GateKind.And; break;
                    case "or": primitive = GateKind.Or; break;
                    case "xor": primitive = GateKind.Xor; break;
                    case "nand": primitive = GateKind.Nand; break;
                    case "nor": primitive = GateKind.Nor; break;
                    default:
                        _notes.Add($"A {gate.kind} at ({gate.x}, {gate.y}) is no kind of gate 5.0 knows; it was left out.");
                        return false;
                }

                int inputs = primitive == GateKind.Not ? 1 : 2;
                id = b.Add(new GatePart { Kind = primitive, Inputs = inputs, Rise = GateDelay, Fall = GateDelay }, gate.x, gate.y);
                _delays += GateDelay;
                pins = (port, input) => input ? port >= 0 && port < inputs ? GatePart.InputName(port) : null : port == 0 ? "y" : null;
                return true;
            }

            /// <summary>Each wire, joined straight or through a buffer of its delay less one.</summary>
            private void Wire(ModuleBuilder b, LegacyWire[] wires, Dictionary<(int, int), (int Id, Func<int, bool, string> Pin)> cells, string where)
            {
                foreach (LegacyWire wire in wires ?? new LegacyWire[0])
                {
                    if (!End(cells, wire.fromX, wire.fromY, wire.fromPort, false, out Endpoint from) ||
                        !End(cells, wire.toX, wire.toY, wire.toPort, true, out Endpoint to))
                    {
                        _notes.Add($"A wire on {where} from ({wire.fromX}, {wire.fromY}) to ({wire.toX}, {wire.toY}) joined nothing that is there; it was left out.");
                        continue;
                    }

                    if (wire.delay <= 1)
                    {
                        b.Connect(from, to);
                        continue;
                    }

                    long extra = wire.delay - 1;
                    int buffer = b.Add(new GatePart { Kind = GateKind.Buf, Inputs = 1, Rise = extra, Fall = extra },
                        (wire.fromX + wire.toX) / 2.0, (wire.fromY + wire.toY) / 2.0);
                    _delays += extra;
                    b.Connect(from, Endpoint.At(buffer, "a"));
                    b.Connect(Endpoint.At(buffer, "y"), to);
                }
            }

            private static bool End(Dictionary<(int, int), (int Id, Func<int, bool, string> Pin)> cells, int x, int y, int port, bool input, out Endpoint end)
            {
                end = default;
                if (!cells.TryGetValue((x, y), out (int Id, Func<int, bool, string> Pin) at))
                    return false;
                string pin = at.Pin(port, input);
                if (pin == null)
                    return false;
                end = Endpoint.At(at.Id, pin);
                return true;
            }

            /// <summary>The module for a block: one for each different block, by name and contents.</summary>
            private Module BlockModule(LegacyBlock block)
            {
                string key = Signature(block);
                if (_blockModules.TryGetValue(key, out Module made))
                    return made;

                string name = NameFrom(block.name, "block");
                string unique = name;
                for (int n = 2; _moduleNames.Contains(unique) || BoardImporter.IsCell(unique); n++)
                    unique = name + "_" + n;
                _moduleNames.Add(unique);

                var b = new ModuleBuilder(unique);
                var cells = new Dictionary<(int, int), (int Id, Func<int, bool, string> Pin)>();

                foreach (LegacyPort port in block.inputs ?? new LegacyPort[0])
                {
                    int id = b.Add(new PortPart { Name = b.Unique(NameFrom(port.id, "in")) }, port.x, port.y);
                    cells[(port.x, port.y)] = (id, (p, input) => input ? null : "out");
                }

                foreach (LegacyPort port in block.outputs ?? new LegacyPort[0])
                {
                    int id = b.Add(new PortPart { Name = b.Unique(NameFrom(port.id, "out")), Direction = PortDirection.Output }, port.x, port.y);
                    cells[(port.x, port.y)] = (id, (p, input) => input ? "in" : null);
                }

                long outside = _delays;
                foreach (LegacyGate gate in block.gates ?? new LegacyGate[0])
                {
                    if (TryGate(b, gate, out int id, out Func<int, bool, string> pins, out bool register))
                    {
                        if (register)
                            _notes.Add($"Block {block.name} holds a register, which 4.x never allowed; it has no clock here.");
                        cells[(gate.x, gate.y)] = (id, pins);
                    }
                }

                Wire(b, block.wires, cells, "block " + block.name);

                // A block's delay counts for every one placed, not once: put it aside and count it there.
                b.Delay = _delays - outside;
                _delays = outside;

                Module module = b.Build();
                _blockModules[key] = module;
                _blockDelays[module.Name] = b.Delay;
                return module;
            }

            private readonly Dictionary<string, long> _blockDelays = new Dictionary<string, long>(StringComparer.Ordinal);

            private long BlockDelay(Module module) => _blockDelays.TryGetValue(module.Name, out long delay) ? delay : 0;

            private static string Signature(LegacyBlock block)
            {
                var text = new StringBuilder(block.name ?? "");
                foreach (LegacyPort p in block.inputs ?? new LegacyPort[0]) text.Append("|i").Append(p.id).Append(',').Append(p.x).Append(',').Append(p.y);
                foreach (LegacyPort p in block.outputs ?? new LegacyPort[0]) text.Append("|o").Append(p.id).Append(',').Append(p.x).Append(',').Append(p.y);
                foreach (LegacyGate g in block.gates ?? new LegacyGate[0]) text.Append("|g").Append(g.kind).Append(',').Append(g.x).Append(',').Append(g.y);
                foreach (LegacyWire w in block.wires ?? new LegacyWire[0])
                    text.Append("|w").Append(w.fromX).Append(',').Append(w.fromY).Append(',').Append(w.fromPort).Append(',')
                        .Append(w.toX).Append(',').Append(w.toY).Append(',').Append(w.toPort).Append(',').Append(w.delay);
                return text.ToString();
            }
        }

        /// <summary>Assembles a module's parts, wires and placements, giving out ids and names as the editor would.</summary>
        private sealed class ModuleBuilder
        {
            private readonly string _name;
            private readonly List<Part> _parts = new List<Part>();
            private readonly List<Wire> _wires = new List<Wire>();
            private readonly Dictionary<int, Placement> _places = new Dictionary<int, Placement>();
            private readonly HashSet<string> _names = new HashSet<string>(StringComparer.Ordinal);
            private int _next = 1;

            public ModuleBuilder(string name) => _name = name;

            /// <summary>The sum of the delays inside, for a block.</summary>
            public long Delay { get; set; }

            public string Unique(string name)
            {
                string unique = name;
                for (int n = 2; _names.Contains(unique); n++)
                    unique = name + "_" + n;
                _names.Add(unique);
                return unique;
            }

            public int Add(Part part, double x, double y)
            {
                int id = _next++;
                string name = part.Name;
                if (name == null)
                {
                    string prefix = part is RegisterPart ? "r" : part is BlockPart ? "u" : "g";
                    for (int n = 1; ; n++)
                    {
                        if (!_names.Contains(prefix + n))
                        {
                            name = prefix + n;
                            break;
                        }
                    }

                    _names.Add(name);
                }

                _parts.Add(part with { Id = id, Name = name });
                _places[id] = new Placement(x, y);
                return id;
            }

            public void Connect(Endpoint a, Endpoint b) => _wires.Add(new Wire { Id = _next++, From = a, To = b });

            public Module Build() => new Module
            {
                Name = _name,
                Parts = _parts,
                Wires = _wires,
                NextId = _next,
                Layout = new Layout { Parts = _places },
            };
        }
    }
}
