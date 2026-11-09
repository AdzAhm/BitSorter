using System;
using System.Collections.Generic;
using BitSorter.Sim;
using BitSorter.SystemVerilog;
using BitSorter.SystemVerilog.Syntax;

namespace BitSorter.Board
{
    /// <summary>What an import came to: a project, or the reasons there is none.</summary>
    public sealed class ImportResult
    {
        internal ImportResult(Project project, IReadOnlyList<string> problems)
        {
            Project = project;
            Problems = problems;
        }

        /// <summary>The drawn project, or null when a module could not be drawn.</summary>
        public Project Project { get; }

        /// <summary>Why not, each with where: in words the student can act on.</summary>
        public IReadOnlyList<string> Problems { get; }

        public bool Succeeded => Project != null;
    }

    /// <summary>
    /// Structural SystemVerilog drawn as a board: the first importer (Part 7a). It reads what the
    /// printer writes and what the course writes by hand at gate level -- ports, nets, primitives, the
    /// course's cells (<c>NAND2 #(.Tpdlh(5), .Tpdhl(5)) g1 (...)</c>), instances of other modules, and
    /// the continuous assignments and registers a board prints -- and puts each module it draws on the board through
    /// <see cref="BoardEditor"/>, so what it makes has passed every rule a drawn board has.
    /// </summary>
    /// <remarks>
    /// Anything else -- <c>always_comb</c>, an <c>initial</c>, a delay given by a parameter -- is not
    /// drawable, and the import says what and where rather than drawing something else. A module like
    /// that will come in as written code (Part 12). Nets keep their names: each becomes the name of
    /// the wires that make it. Parts are laid out in columns by how far they are from the inputs.
    ///
    /// **A module is drawn whole, then checked once** (Part 16d): its parts and wires are made here, as
    /// <see cref="BoardEditor.Add"/> and <see cref="BoardEditor.Connect"/> would make them -- the same ids, names and
    /// order -- and the module goes on the board as one edit, held to the rules as every edit is. Made an edit at a time,
    /// each edit checked the whole project again: a flat module of a thousand gates made 13.5 GB in 14 s, in one frame.
    /// </remarks>
    public static class BoardImporter
    {
        public static ImportResult Import(SyntaxTree tree, string top) => Import(tree, top, null, null);

        /// <summary>
        /// The same, with modules that are already <paramref name="written"/> (Part 13b): an instance of one is placed as a
        /// block of it, pins from its header, rather than drawn, so a structural module over written ones is drawn. The
        /// project's <paramref name="files"/> are what their includes read.
        /// </summary>
        public static ImportResult Import(SyntaxTree tree, string top, IReadOnlyList<Module> written, IReadOnlyList<ProjectFile> files) =>
            Import(ModulesOf(tree), InterfacesOf(tree), tree.Timescale, top, written, files);

        /// <summary>
        /// The modules of <paramref name="tree"/> by name: of two of one name, the first, as a project keeps it
        /// (<see cref="ProjectImporter"/>, which says so).
        /// </summary>
        internal static Dictionary<string, ModuleDeclaration> ModulesOf(SyntaxTree tree)
        {
            var modules = new Dictionary<string, ModuleDeclaration>(StringComparer.Ordinal);
            foreach (ModuleDeclaration module in tree.Modules)
                if (!modules.ContainsKey(module.Name))
                    modules[module.Name] = module;
            return modules;
        }

        /// <summary>
        /// The names of the interfaces of <paramref name="tree"/> (Part L8): no part a board draws, so a module that uses one
        /// comes in as written code.
        /// </summary>
        internal static HashSet<string> InterfacesOf(SyntaxTree tree)
        {
            var interfaces = new HashSet<string>(StringComparer.Ordinal);
            foreach (ModuleDeclaration declared in tree.Interfaces)
                interfaces.Add(declared.Name);
            return interfaces;
        }

        /// <summary>
        /// The same, from <paramref name="modules"/> by name -- the ones an import keeps, so a drawing is of the module the
        /// project has (Part 16d) -- and <paramref name="interfaces"/>, with the <paramref name="timescale"/> the files gave.
        /// </summary>
        internal static ImportResult Import(Dictionary<string, ModuleDeclaration> modules, HashSet<string> interfaces, string timescale,
            string top, IReadOnlyList<Module> written, IReadOnlyList<ProjectFile> files)
        {
            var writtenNames = new HashSet<string>(StringComparer.Ordinal);
            if (written != null)
                foreach (Module module in written)
                    writtenNames.Add(module.Name);

            var problems = new List<string>();

            if (top == null || !modules.TryGetValue(top, out ModuleDeclaration topModule))
                return new ImportResult(null, new[] { $"There is no module called {top ?? "(none)"} to draw." });
            if (IsCell(top))
                return new ImportResult(null, new[] { $"{top} is a library cell: it is drawn as a gate, inside a module." });

            // Every module reachable from the top, children first, so a block's pins are known when it is placed.
            var order = new List<ModuleDeclaration>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            void Visit(ModuleDeclaration module, int depth)
            {
                if (!seen.Add(module.Name) || depth > 64 || writtenNames.Contains(module.Name))
                    return;
                foreach (ModuleItem item in module.Items)
                    VisitItem(item, depth);
                order.Add(module);
            }

            // An instance, and one inside a generate construct -- an array of a block (Part 12c).
            void VisitItem(ModuleItem item, int depth)
            {
                switch (item)
                {
                    case ModuleInstantiation inst when !IsCell(inst.Module) && modules.TryGetValue(inst.Module, out ModuleDeclaration inner):
                        Visit(inner, depth + 1);
                        break;
                    case GenerateRegion region:
                        foreach (ModuleItem inside in region.Items)
                            VisitItem(inside, depth);
                        break;
                    case LoopGenerate loop when loop.Body != null:
                        foreach (ModuleItem inside in loop.Body.Items)
                            VisitItem(inside, depth);
                        break;
                }
            }

            Visit(topModule, 0);

            if (writtenNames.Contains(top))
                return new ImportResult(null, new[] { $"{top} is written." });
            var editor = new BoardEditor(new Project
            {
                Name = top,
                Top = top,
                Modules = written != null ? new List<Module>(written) : new List<Module>(),
                Files = files ?? Array.Empty<ProjectFile>(),
            });
            foreach (ModuleDeclaration module in order)
            {
                // A module's parts and wires are made as one group, the rules asked once of the module it ends on (the 16d sweep:
                // asked of the whole project for every part and wire, a 64-bit adder made 2.5 GB). One the group refuses is read
                // again an element at a time, as it always was, so each fault is said where it is.
                int had = problems.Count;
                EditResult read = editor.Group(() =>
                {
                    new ModuleReader(editor, module, modules, interfaces, problems).Read();
                    return problems.Count > had ? EditResult.Refused(problems[had]) : EditResult.Ok();
                });
                if (!read.Done)
                {
                    problems.RemoveRange(had, problems.Count - had);
                    new ModuleReader(editor, module, modules, interfaces, problems).Read();
                }
            }

            // A `timescale of one whole unit is the project's unit, as printing one wrote it. One in tens
            // of a unit (10ns/1ns) has none a project can count in, and the delays are read as written.
            if (TimeUnits.TryUnitOf(timescale, out string unit))
                editor.SetSettings(editor.Project.Settings with { TimeUnit = unit });

            return problems.Count > 0 ? new ImportResult(null, problems) : new ImportResult(editor.Project, problems);
        }

        /// <summary>Whether a module name is one of the cells the board draws as a gate.</summary>
        public static bool IsCell(string name) => CellKind(name, out _, out _);

        private static bool CellKind(string name, out GateKind kind, out int inputs)
        {
            kind = GateKind.And;
            inputs = 0;
            if (string.IsNullOrEmpty(name))
                return false;

            if (name == "INV") { kind = GateKind.Not; inputs = 1; return true; }
            if (name == "BUF") { kind = GateKind.Buf; inputs = 1; return true; }

            int digits = name.Length - 1;
            if (digits < 2 || name[digits] < '2' || name[digits] > '0' + CellLibrary.MaxInputs)
                return false;

            switch (name.Substring(0, digits))
            {
                case "AND": kind = GateKind.And; break;
                case "OR": kind = GateKind.Or; break;
                case "NAND": kind = GateKind.Nand; break;
                case "NOR": kind = GateKind.Nor; break;
                case "XOR": kind = GateKind.Xor; break;
                case "XNOR": kind = GateKind.Xnor; break;
                default: return false;
            }

            inputs = name[digits] - '0';
            return true;
        }

        private sealed class ModuleReader
        {
            private readonly BoardEditor _editor;
            private readonly ModuleDeclaration _source;
            private readonly Dictionary<string, ModuleDeclaration> _modules;
            private readonly HashSet<string> _interfaces;
            private readonly List<string> _problems;
            private readonly string _name;

            /// <summary>Each net by name: its width and starting value, and the pins found on it.</summary>
            private readonly Dictionary<string, NetFound> _nets = new Dictionary<string, NetFound>(StringComparer.Ordinal);
            private readonly Dictionary<string, long> _parameters = new Dictionary<string, long>(StringComparer.Ordinal);

            /// <summary>Each splitter made, by the bus it splits.</summary>
            private readonly Dictionary<string, int> _splitters = new Dictionary<string, int>(StringComparer.Ordinal);

            // The module as it is drawn, made here and put on the board whole, as one edit, once it is (Part 16d).
            private Module _start;
            private int _nextId;
            private readonly List<Part> _parts = new List<Part>();
            private readonly Dictionary<int, Part> _byId = new Dictionary<int, Part>();
            private readonly List<Wire> _wires = new List<Wire>();
            private readonly Dictionary<int, Placement> _placements = new Dictionary<int, Placement>();

            /// <summary>Each part's code, for where a refusal of it is.</summary>
            private readonly Dictionary<int, SyntaxNode> _madeAt = new Dictionary<int, SyntaxNode>();

            /// <summary>The names the module's parts and wires have, and for each kind's name the first number that may be free.</summary>
            private readonly HashSet<string> _taken = new HashSet<string>(StringComparer.Ordinal);
            private readonly Dictionary<string, int> _firstFree = new Dictionary<string, int>(StringComparer.Ordinal);

            /// <summary>Which pins the wires so far have joined: a union-find, every pin its own net until a wire joins it.</summary>
            private readonly Dictionary<PinRef, PinRef> _joinedTo = new Dictionary<PinRef, PinRef>();

            /// <summary>The names given the nets, each by the first wire of its net, given once the module is on the board.</summary>
            private readonly List<(int Wire, string Name)> _wireNames = new List<(int, string)>();

            private sealed class NetFound
            {
                public int Width = 1;

                /// <summary>Its range as declared, <c>[Msb:Lsb]</c>: what a select's index is counted by. <c>[0:0]</c> for a bit.</summary>
                public long Msb, Lsb;

                public LogicVec? Initial;
                public bool IsPort;

                /// <summary>Whether a register's q is on it, which takes its starting value as the register's.</summary>
                public bool IsRegister;

                public readonly List<Endpoint> Pins = new List<Endpoint>();
            }

            /// <summary>What declarations drive, each with where: a supply's level, and a net's declaration assignment.</summary>
            private readonly List<(string Net, LogicVec Value, SyntaxNode At)> _declaredDrivers = new List<(string, LogicVec, SyntaxNode)>();

            /// <summary>Each variable given a starting value, with where: a register's, or held for good where nothing writes it.</summary>
            private readonly List<(string Net, SyntaxNode At)> _started = new List<(string, SyntaxNode)>();

            public ModuleReader(BoardEditor editor, ModuleDeclaration source, Dictionary<string, ModuleDeclaration> modules, HashSet<string> interfaces, List<string> problems)
            {
                _editor = editor;
                _source = source;
                _modules = modules;
                _interfaces = interfaces;
                _problems = problems;
                _name = source.Name;
            }

            private void Problem(SyntaxNode at, string message) =>
                _problems.Add(at?.Location.File != null ? $"{at.Location.File}:{at.Location.Line}: {_name}: {message}" : $"{_name}: {message}");

            public void Read()
            {
                if (_editor.Project.Find(_name) == null)
                {
                    EditResult added = _editor.AddModule(_name);
                    if (!added.Done)
                    {
                        Problem(_source, added.Refusal);
                        return;
                    }
                }

                _start = _editor.Project.Find(_name);
                _nextId = _start.NextId;
                foreach (Part part in _start.Parts)
                {
                    _parts.Add(part);
                    _byId[part.Id] = part;
                    if (part.Name != null)
                        _taken.Add(part.Name);
                }

                foreach (Wire wire in _start.Wires)
                {
                    _wires.Add(wire);
                    if (wire.NetName != null)
                        _taken.Add(wire.NetName);
                }

                foreach (KeyValuePair<int, Placement> placed in _start.Layout.Parts)
                    _placements[placed.Key] = placed.Value;

                if (_source.HeaderParameters.Count > 0 || _source.PortNames.Count > 0)
                {
                    Problem(_source, _source.PortNames.Count > 0
                        ? "its ports are declared inside the module; the board reads a header with the ports in it, as the course writes them."
                        : "it has parameters in its header; a drawn module has none yet.");
                    return;
                }

                int before = _problems.Count;

                foreach (ModuleItem item in _source.Items)
                    if (item is ParameterDeclaration parameter)
                        Parameter(parameter);

                foreach (PortDeclaration port in _source.Ports)
                    Port(port);

                foreach (ModuleItem item in _source.Items)
                    if (item is DataDeclaration declaration)
                        Declare(declaration);

                foreach (ModuleItem item in _source.Items)
                    Item(item);

                // What the declarations drive: a supply, a net's assignment, and a variable's starting value no register takes,
                // each a constant on its net -- beside anything else that drives it, two drivers, which the rules refuse.
                foreach ((string name, LogicVec value, SyntaxNode at) in _declaredDrivers)
                    Constant(name, value, at);
                foreach ((string name, SyntaxNode at) in _started)
                    if (!_nets[name].IsRegister && _nets[name].Initial is LogicVec start)
                        Constant(name, start, at);

                if (_problems.Count > before)
                    return;

                Join();
                Lay();
                if (_problems.Count == before)
                    PutOnTheBoard();
            }

            /// <summary>The module as drawn so far, wires unnamed.</summary>
            private Module Drawn() => _start with
            {
                Parts = _parts.ToArray(),
                Wires = _wires.ToArray(),
                NextId = _nextId,
                Layout = _start.Layout with { Parts = new Dictionary<int, Placement>(_placements) },
            };

            /// <summary>
            /// The module drawn, put on the board as one edit, checked as every edit is; then its nets' names, as one more --
            /// or, where that is refused, one at a time, each kept where it is not refused, as naming them always was.
            /// </summary>
            private void PutOnTheBoard()
            {
                Module drawn = Drawn();
                EditResult made = _editor.ReplaceModule(drawn);
                if (!made.Done)
                {
                    Problem(Refused(drawn), made.Refusal);
                    return;
                }

                if (_wireNames.Count == 0)
                    return;
                var named = new Dictionary<int, string>();
                foreach ((int wire, string name) in _wireNames)
                    named[wire] = name;
                var wires = new Wire[_wires.Count];
                for (int i = 0; i < wires.Length; i++)
                    wires[i] = named.TryGetValue(_wires[i].Id, out string name) ? _wires[i] with { NetName = name } : _wires[i];
                if (_editor.ReplaceModule(drawn with { Wires = wires }).Done)
                    return;
                foreach ((int wire, string name) in _wireNames)
                    _editor.NameWire(_name, wire, name);
            }

            /// <summary>Where a refusal of <paramref name="drawn"/> is: the code of the part its first new error is about, or the module.</summary>
            private SyntaxNode Refused(Module drawn)
            {
                List<BoardDiagnostic> added = BoardRules.NewErrors(new List<BoardDiagnostic>(_editor.Diagnostics),
                    BoardRules.Check(_editor.Project.WithModule(drawn)));
                return added.Count > 0 && _madeAt.TryGetValue(added[0].Part, out SyntaxNode at) ? at : _source;
            }

            // -----------------------------------------------------------------
            // Declarations
            // -----------------------------------------------------------------

            private void Parameter(ParameterDeclaration parameter)
            {
                if (TryConstant(parameter.Value, out long value))
                    _parameters[parameter.Name] = value;
                else
                    Problem(parameter, $"the parameter {parameter.Name} is not a plain number.");
            }

            private void Port(PortDeclaration port)
            {
                if (port.Interface != null && (port.Modport != null || port.Interface == "interface" || _interfaces.Contains(port.Interface)))
                {
                    Problem(port, $"{port.Name} is an interface port; a module that uses an interface comes in as written code (Part 12).");
                    return;
                }

                if (port.Direction == PortDirection.Inout)
                {
                    Problem(port, $"{port.Name} is an inout; a module with one comes in as written code, where an inout is joined to its net (Part 14f).");
                    return;
                }

                if (port.Unpacked.Count > 0)
                {
                    Problem(port, $"{port.Name} is an array port; a module with one comes in as written code (Part 12).");
                    return;
                }

                if (port.NetType != null && port.NetType != "wire" && port.NetType != "tri" && port.NetType != "uwire")
                {
                    Problem(port, $"{port.Name} is a {port.NetType} port, which a board does not draw; it comes in as written code (Part 12).");
                    return;
                }

                int width = Width(port.Type, port, out long msb, out long lsb);
                int id = Add(new PortPart { Name = port.Name, Direction = port.Direction, Width = width, Comment = port.Comment }, port);
                if (id == 0)
                    return;

                NetFound net = Net(port.Name);
                net.Width = width;
                net.Msb = msb;
                net.Lsb = lsb;
                net.IsPort = true;
                net.Pins.Add(Endpoint.At(id, port.Direction == PortDirection.Input ? "out" : "in"));
            }

            /// <summary>
            /// A declaration's nets: their width, and what the declaration says of them -- a supply drives its level, a net's
            /// declaration assignment drives it (IEEE 1800, 10.3.1), a variable's is where it starts. A net type that resolves
            /// its drivers its own way, and a net's own delay, are no board's, as they are no engine's yet
            /// (<c>Elaborator.CheckNetType</c>): refused, not drawn as a plain wire.
            /// </summary>
            private void Declare(DataDeclaration declaration)
            {
                int width = Width(declaration.Type, declaration, out long msb, out long lsb);
                string netType = declaration.NetType;
                bool supply = netType == "supply0" || netType == "supply1";
                if (netType != null && netType != "wire" && netType != "tri" && netType != "uwire" && !supply)
                {
                    Problem(declaration, $"a {netType} net resolves its drivers its own way, which a board does not draw; it comes in as written code (Part 12).");
                    return;
                }

                if (declaration.Delay != null)
                {
                    Problem(declaration, "a net with a delay of its own cannot be drawn, a board's delays being its gates'; it comes in as written code (Part 12).");
                    return;
                }

                foreach (Declarator declarator in declaration.Declarators)
                {
                    if (declarator.Unpacked.Count > 0)
                    {
                        Problem(declarator, $"{declarator.Name} is a memory; the board has no memory part yet.");
                        continue;
                    }

                    NetFound net = Net(declarator.Name);
                    net.Width = width;
                    net.Msb = msb;
                    net.Lsb = lsb;
                    if (supply)
                        _declaredDrivers.Add((declarator.Name, LogicVec.Fill(netType == "supply1" ? Logic4.One : Logic4.Zero, width), declarator));
                    if (declarator.Initializer == null)
                        continue;
                    if (!(declarator.Initializer is NumberLiteral literal))
                    {
                        Problem(declarator, $"{declarator.Name} starts at a value that is not a plain number.");
                        continue;
                    }

                    if (netType != null)
                    {
                        _declaredDrivers.Add((declarator.Name, Resized(literal, width), declarator));
                    }
                    else
                    {
                        net.Initial = Resized(literal, width);
                        _started.Add((declarator.Name, declarator));
                    }
                }
            }

            /// <summary>
            /// A net's width, from a type a board's nets are: <c>logic</c> or <c>reg</c>, or none written, with at most one
            /// packed range. Any other -- 2-state <c>bit</c> and <c>int</c>, <c>integer</c>, an enum, a struct, a typedef's
            /// name -- is refused, the module coming in as written code, rather than drawn one bit wide.
            /// </summary>
            private int Width(DataType type, SyntaxNode at, out long msb, out long lsb)
            {
                msb = lsb = 0;
                if (type != null && NotALogicVector(type) is string what)
                {
                    Problem(at, $"{what} is no net a board draws, a board's nets being logic vectors; it comes in as written code (Part 12).");
                    return 1;
                }

                if (type == null || type.Packed.Count == 0)
                    return 1;
                if (type.Packed.Count > 1)
                {
                    Problem(at, "a vector of more than one dimension cannot be drawn.");
                    return 1;
                }

                Dimension range = type.Packed[0];
                if (TryConstant(range.Left, out long left) && TryConstant(range.Right, out long right))
                {
                    msb = left;
                    lsb = right;
                    return (int)Math.Abs(left - right) + 1;
                }

                Problem(at, "a width that is not a plain number.");
                return 1;
            }

            /// <summary>What <paramref name="type"/> is, in words, when it is no logic vector; null when it is one.</summary>
            private static string NotALogicVector(DataType type)
            {
                if (type.Enum != null)
                    return "an enum";
                if (type.Struct != null)
                    return type.Struct.IsUnion ? "a union" : "a struct";
                if (type.Name != null)
                    return $"a {type.Name}, a typedef's type,";
                if (type.Keyword != null && type.Keyword != "logic" && type.Keyword != "reg")
                    return ("aeiou".IndexOf(type.Keyword[0]) >= 0 ? "an " : "a ") + type.Keyword;
                return null;
            }

            // -----------------------------------------------------------------
            // Items
            // -----------------------------------------------------------------

            private void Item(ModuleItem item)
            {
                switch (item)
                {
                    case ParameterDeclaration _:
                    case DataDeclaration _:
                        return;

                    case PrimitiveInstantiation primitive:
                        Primitive(primitive);
                        return;

                    case ModuleInstantiation instantiation:
                        Instances(instantiation);
                        return;

                    // An array of a block, as the board prints one (Part 12c); its genvar declared on its own, or a
                    // generate region round it, as the course writes them.
                    case GenvarDeclaration _:
                        return;

                    case GenerateRegion region:
                        foreach (ModuleItem inner in region.Items)
                            Item(inner);
                        return;

                    case LoopGenerate loop when IsArray(loop):
                        Array(loop);
                        return;

                    case ContinuousAssign assign:
                        foreach (Assignment a in assign.Assignments)
                            Assign(a, assign.Delay);
                        return;

                    case ProceduralBlock block when block.Kind == ProcessKind.AlwaysFf:
                        Register(block);
                        return;

                    case ProceduralBlock block:
                        Problem(block, $"it has an {Keyword(block.Kind)} block; a module with behaviour comes in as written code (Part 12).");
                        return;

                    default:
                        Problem(item, "it has something a board cannot draw yet; it comes in as written code (Part 12).");
                        return;
                }
            }

            private void Primitive(PrimitiveInstantiation primitive)
            {
                if (!Enum.TryParse(primitive.Primitive, true, out GateKind kind))
                {
                    Problem(primitive, $"a {primitive.Primitive} primitive cannot be drawn yet.");
                    return;
                }

                if (!TryDelay(primitive.Delay, primitive, out long rise, out long fall))
                    return;

                foreach (PrimitiveInstance instance in primitive.Instances)
                {
                    if (instance.Array != null || instance.Terminals.Count < 2)
                    {
                        Problem(instance, "an array of gates, or one with no inputs, cannot be drawn.");
                        continue;
                    }

                    var names = new List<string>();
                    foreach (Expression terminal in instance.Terminals)
                    {
                        if (terminal is NameExpression name && name.Path.Count == 1)
                            names.Add(name.Name);
                        else
                        {
                            Problem(instance, "a gate's terminals have to be nets by name.");
                            names = null;
                            break;
                        }
                    }

                    if (names != null)
                        Gate(kind, instance.Name, names[0], names.GetRange(1, names.Count - 1), rise, fall, instance);
                }
            }

            private void Gate(GateKind kind, string name, string output, List<string> inputs, long rise, long fall, SyntaxNode at)
            {
                int width = Net(output).Width;
                int id = Add(new GatePart { Kind = kind, Name = name, Inputs = inputs.Count, Width = width, Rise = rise, Fall = fall }, at);
                if (id == 0)
                    return;

                Net(output).Pins.Add(Endpoint.At(id, "y"));
                for (int i = 0; i < inputs.Count; i++)
                    Net(inputs[i]).Pins.Add(Endpoint.At(id, GatePart.InputPin(kind, i)));
            }

            private void Instances(ModuleInstantiation instantiation)
            {
                if (CellKind(instantiation.Module, out GateKind kind, out int inputs))
                {
                    Cells(instantiation, kind, inputs);
                    return;
                }

                if (_interfaces.Contains(instantiation.Module) && !_modules.ContainsKey(instantiation.Module))
                {
                    Problem(instantiation, $"it makes an instance of the interface {instantiation.Module}; a module that uses an interface comes in as written code (Part 12).");
                    return;
                }

                if (!_modules.ContainsKey(instantiation.Module))
                {
                    Problem(instantiation, $"it uses {instantiation.Module}, which is not among the files read.");
                    return;
                }

                if (instantiation.Parameters.Count > 0)
                {
                    Problem(instantiation, $"its {instantiation.Module} is given parameters; a block takes none yet.");
                    return;
                }

                foreach (ModuleInstance instance in instantiation.Instances)
                {
                    int id = Add(new BlockPart { Module = instantiation.Module, Name = instance.Name }, instance);
                    if (id == 0)
                        continue;

                    foreach (PortConnection connection in instance.Connections)
                    {
                        if (connection.Name == null || connection.IsShorthand || connection.IsWildcard)
                        {
                            Problem(instance, "a block's connections have to be by name: .a(x).");
                            break;
                        }

                        if (connection.Value == null)
                            continue;   // .a(): left open
                        if (connection.Value is NameExpression net && net.Path.Count == 1)
                            Net(net.Name).Pins.Add(Endpoint.At(id, connection.Name));
                        else
                            Problem(instance, $".{connection.Name} is connected to something other than a net by name.");
                    }
                }
            }

            /// <summary>
            /// Whether <paramref name="loop"/> is an array of a block as the board prints one (Part 12c): from 0 while below a
            /// plain count, one at a time, a labelled block holding one instance of a module read, each of its ports either
            /// left open or given its own copy's bits of a net -- <c>.a(a[i])</c> or <c>.a(a[i*w +: w])</c>.
            /// </summary>
            private bool IsArray(LoopGenerate loop)
            {
                string i = loop.Variable;
                if (!TryConstant(loop.Start, out long start) || start != 0 || loop.StepOperator != "++" || loop.Body?.Label == null)
                    return false;
                if (!(loop.Condition is BinaryExpression below) || below.Operator != "<" || !IsName(below.Left, i)
                    || !TryConstant(below.Right, out long copies) || copies < 2 || copies > BlockPart.MaxCopies)
                    return false;
                if (loop.Body.Items.Count != 1 || !(loop.Body.Items[0] is ModuleInstantiation instantiation)
                    || instantiation.Instances.Count != 1 || instantiation.Parameters.Count > 0 || !_modules.ContainsKey(instantiation.Module))
                    return false;

                foreach (PortConnection connection in instantiation.Instances[0].Connections)
                {
                    if (connection.Name == null || connection.IsShorthand || connection.IsWildcard)
                        return false;
                    if (connection.Value != null && !(CopyNet(connection.Value, i) is string net && CountsFromZero(net)))
                        return false;
                }

                return true;
            }

            /// <summary>An array of a block made a block of that many copies, each pin on the net its copies' bits are of.</summary>
            private void Array(LoopGenerate loop)
            {
                var instantiation = (ModuleInstantiation)loop.Body.Items[0];
                TryConstant(((BinaryExpression)loop.Condition).Right, out long copies);
                int id = Add(new BlockPart { Module = instantiation.Module, Name = loop.Body.Label, Copies = (int)copies }, loop);
                if (id == 0)
                    return;
                foreach (PortConnection connection in instantiation.Instances[0].Connections)
                    if (connection.Value != null)
                        Net(CopyNet(connection.Value, loop.Variable)).Pins.Add(Endpoint.At(id, connection.Name));
            }

            /// <summary>The net a copy's bits are taken from -- <c>a</c> of <c>a[i]</c> or <c>a[i*w +: w]</c> -- or null for anything else.</summary>
            private string CopyNet(Expression value, string i)
            {
                switch (value)
                {
                    case IndexExpression bit when bit.Target is NameExpression net && net.Path.Count == 1 && IsName(bit.Index, i):
                        return net.Name;

                    case RangeSelectExpression range when range.Kind == RangeKind.IndexedUp && range.Target is NameExpression net
                        && net.Path.Count == 1 && range.Left is BinaryExpression times && times.Operator == "*" && IsName(times.Left, i)
                        && TryConstant(times.Right, out long width) && TryConstant(range.Right, out long count) && width == count:
                        return net.Name;

                    default:
                        return null;
                }
            }

            private static bool IsName(Expression e, string name) => e is NameExpression n && n.Path.Count == 1 && n.Name == name;

            /// <summary>Whether a net's index is its offset -- declared <c>[w-1:0]</c>, or a bit -- as an array's copies take it.</summary>
            private bool CountsFromZero(string net) => !_nets.TryGetValue(net, out NetFound found) || found.Lsb == 0 && found.Msb >= 0;

            private void Cells(ModuleInstantiation instantiation, GateKind kind, int inputs)
            {
                long rise = CellDefault(instantiation.Module, CellLibrary.Rise), fall = CellDefault(instantiation.Module, CellLibrary.Fall);
                for (int i = 0; i < instantiation.Parameters.Count; i++)
                {
                    ParameterValue p = instantiation.Parameters[i];
                    string which = p.Name ?? (i == 0 ? CellLibrary.Rise : CellLibrary.Fall);
                    if (p.Value == null)
                        continue;
                    if (!TryConstant(p.Value, out long value))
                    {
                        Problem(instantiation, $"{which} is not a plain number.");
                        return;
                    }

                    if (which == CellLibrary.Rise) rise = value;
                    else if (which == CellLibrary.Fall) fall = value;
                }

                foreach (ModuleInstance instance in instantiation.Instances)
                {
                    var byPort = new Dictionary<string, string>(StringComparer.Ordinal);
                    int position = 0;
                    foreach (PortConnection connection in instance.Connections)
                    {
                        string port = connection.Name ?? (position < inputs ? CellLibrary.Input(position) : CellLibrary.Output);
                        position++;
                        if (connection.Value is NameExpression net && net.Path.Count == 1)
                            byPort[port] = net.Name;
                        else if (connection.Value != null)
                            Problem(instance, $".{port} is connected to something other than a net by name.");
                    }

                    if (!byPort.TryGetValue(CellLibrary.Output, out string output))
                        output = Unconnected(instance.Name, "y");

                    var names = new List<string>();
                    for (int i = 0; i < inputs; i++)
                        names.Add(byPort.TryGetValue(CellLibrary.Input(i), out string input) ? input : Unconnected(instance.Name, GatePart.InputName(i)));

                    Gate(kind, instance.Name, output, names, rise, fall, instance);
                }
            }

            /// <summary>A cell's own default delay, when the files read define it; the course's 1 otherwise.</summary>
            private long CellDefault(string cell, string parameter)
            {
                if (_modules.TryGetValue(cell, out ModuleDeclaration module))
                    foreach (ModuleItem item in module.Items)
                        if (item is ParameterDeclaration p && p.Name == parameter && TryConstant(p.Value, out long value))
                            return value;
                return 1;
            }

            /// <summary>A net no one else will name, for a cell's pin left open.</summary>
            private string Unconnected(string part, string pin) => "\u0001" + part + "." + pin;

            /// <summary>A constant part driving <paramref name="net"/> with <paramref name="value"/>.</summary>
            private void Constant(string net, LogicVec value, SyntaxNode at)
            {
                int id = Add(new ConstantPart { Value = value }, at);
                if (id != 0)
                    Net(net).Pins.Add(Endpoint.At(id, "y"));
            }

            private void Assign(Assignment assignment, DelayControl delay)
            {
                if (!(assignment.Target is NameExpression target) || target.Path.Count != 1)
                {
                    Problem(assignment, "an assign has to drive a net by name.");
                    return;
                }

                string to = target.Name;
                Expression value = Strip(assignment.Value);

                if (!TryDelay(delay, assignment, out long rise, out long fall))
                    return;
                bool delayed = rise != 0 || fall != 0;

                // A net to a net: a wire.
                if (value is NameExpression from && from.Path.Count == 1 && !delayed)
                {
                    _joins.Add((to, from.Name));
                    return;
                }

                if (value is NumberLiteral constant && !delayed)
                {
                    int id = Add(new ConstantPart { Value = Resized(constant, Net(to).Width) }, assignment);
                    if (id != 0)
                        Net(to).Pins.Add(Endpoint.At(id, "y"));
                    return;
                }

                if (TryGate(value, out GateKind kind, out List<string> inputs))
                {
                    Gate(kind, null, to, inputs, rise, fall, assignment);
                    return;
                }

                if (!delayed && value is ConditionalExpression && TryMux(value, out List<string> data, out string select, out int selectBits, out bool indexed)
                    && (!indexed || Net(select).Msb == selectBits - 1 && Net(select).Lsb == 0))
                {
                    int id = Add(new MuxPart { Inputs = data.Count, Width = Net(to).Width }, assignment);
                    if (id == 0)
                        return;
                    for (int i = 0; i < data.Count; i++)
                        Net(data[i]).Pins.Add(Endpoint.At(id, "d" + i));
                    Net(select).Pins.Add(Endpoint.At(id, "s"));
                    Net(to).Pins.Add(Endpoint.At(id, "y"));
                    return;
                }

                if (!delayed && value is IndexExpression bit && Strip(bit.Target) is NameExpression bus && bus.Path.Count == 1 && TryConstant(bit.Index, out long index))
                {
                    // The bit the index names, counted by the bus's range: a[2] of [4:1] is offset 1, b[0] of [0:3] offset 3.
                    NetFound busNet = Net(bus.Name);
                    long offset = busNet.Msb >= busNet.Lsb ? index - busNet.Lsb : busNet.Lsb - index;
                    if (offset < 0 || offset >= busNet.Width)
                    {
                        Problem(assignment, $"{bus.Name}[{index}] is outside {bus.Name}, which is [{busNet.Msb}:{busNet.Lsb}].");
                        return;
                    }

                    if (!_splitters.TryGetValue(bus.Name, out int splitter))
                    {
                        splitter = Add(new SplitterPart { Width = Net(bus.Name).Width }, assignment);
                        if (splitter == 0)
                            return;
                        _splitters[bus.Name] = splitter;
                        Net(bus.Name).Pins.Add(Endpoint.At(splitter, "in"));
                    }

                    Net(to).Pins.Add(Endpoint.At(splitter, "o" + offset));
                    return;
                }

                if (!delayed && value is ConcatenationExpression concatenation && concatenation.Items.Count >= 2)
                {
                    var bits = new List<string>();
                    foreach (Expression item in concatenation.Items)
                    {
                        if (Strip(item) is NameExpression name && name.Path.Count == 1)
                            bits.Add(name.Name);
                        else
                        {
                            bits = null;
                            break;
                        }
                    }

                    if (bits != null)
                    {
                        int id = Add(new JoinerPart { Width = bits.Count }, assignment);
                        if (id == 0)
                            return;
                        for (int i = 0; i < bits.Count; i++)
                            Net(bits[bits.Count - 1 - i]).Pins.Add(Endpoint.At(id, "i" + i));
                        Net(to).Pins.Add(Endpoint.At(id, "out"));
                        return;
                    }
                }

                Problem(assignment, $"assign {to} = {Printer.Print(assignment.Value)} is not something the board draws; it comes in as written code (Part 12).");
            }

            /// <summary><c>a &amp; b &amp; c</c>, <c>~(a | b)</c>, <c>~a</c>, <c>a</c>: one operator over nets by name.</summary>
            private static bool TryGate(Expression value, out GateKind kind, out List<string> inputs)
            {
                bool inverted = false;
                if (value is UnaryExpression unary && unary.Operator == "~")
                {
                    inverted = true;
                    value = Strip(unary.Operand);
                }

                inputs = new List<string>();
                if (value is NameExpression single && single.Path.Count == 1)
                {
                    inputs.Add(single.Name);
                    kind = inverted ? GateKind.Not : GateKind.Buf;
                    return true;
                }

                kind = GateKind.And;
                if (!(value is BinaryExpression binary))
                    return false;

                // The course's first workshop's NAND, X ~& Y (not the standard's; the reader warns): two
                // operands and never a chain, since a ~& b ~& c is (a ~& b) ~& c, not a 3-input NAND.
                if (binary.Operator == "~&" || binary.Operator == "~|")
                {
                    if (Strip(binary.Left) is NameExpression left && left.Path.Count == 1 &&
                        Strip(binary.Right) is NameExpression right && right.Path.Count == 1)
                    {
                        inputs.Add(left.Name);
                        inputs.Add(right.Name);
                        kind = binary.Operator == "~&"
                            ? inverted ? GateKind.And : GateKind.Nand
                            : inverted ? GateKind.Or : GateKind.Nor;
                        return true;
                    }

                    return false;
                }

                string op = binary.Operator;
                if (!Operands(binary, op, inputs) || inputs.Count < 2 || inputs.Count > GatePart.MaxInputs)
                    return false;

                switch (op)
                {
                    case "&": kind = inverted ? GateKind.Nand : GateKind.And; return true;
                    case "|": kind = inverted ? GateKind.Nor : GateKind.Or; return true;
                    case "^": kind = inverted ? GateKind.Xnor : GateKind.Xor; return true;
                    default: return false;
                }
            }

            private static bool Operands(Expression e, string op, List<string> into)
            {
                e = Strip(e);
                if (e is BinaryExpression b && b.Operator == op)
                    return Operands(b.Left, op, into) && Operands(b.Right, op, into);
                if (e is NameExpression name && name.Path.Count == 1)
                {
                    into.Add(name.Name);
                    return true;
                }

                return false;
            }

            /// <summary>
            /// The printer's mux: <c>s ? d1 : d0</c>, and for four or eight inputs the same nested by the
            /// select's bits from the top one down.
            /// </summary>
            /// <remarks>
            /// <paramref name="indexed"/> says the select's bits are named by index -- <c>s[1]</c>, <c>s[0]</c> -- which the
            /// mux's select reads from the top of the net down: so only of a select declared <c>[bits-1:0]</c>, whose index is
            /// its offset.
            /// </remarks>
            private static bool TryMux(Expression value, out List<string> data, out string select, out int bits, out bool indexed)
            {
                data = null;
                select = null;
                bits = 0;
                indexed = false;

                // The condition at the root names the select: s, or s[top].
                Expression root = Strip(((ConditionalExpression)value).Condition);
                if (root is NameExpression s && s.Path.Count == 1)
                {
                    select = s.Name;
                    bits = 1;
                }
                else if (root is IndexExpression index && Strip(index.Target) is NameExpression bus && bus.Path.Count == 1 &&
                         index.Index is NumberLiteral top && top.Value.TryGetUInt64(out ulong topBit) && topBit <= 2)
                {
                    select = bus.Name;
                    bits = (int)topBit + 1;
                    indexed = true;
                }
                else
                {
                    return false;
                }

                var leaves = new string[1 << bits];
                if (!Leaves(value, select, bits, bits - 1, 0, leaves))
                    return false;

                data = new List<string>(leaves);
                return true;
            }

            private static bool Leaves(Expression e, string select, int bits, int bit, int low, string[] into)
            {
                e = Strip(e);
                if (bit < 0)
                {
                    if (!(e is NameExpression leaf) || leaf.Path.Count != 1)
                        return false;
                    into[low] = leaf.Name;
                    return true;
                }

                if (!(e is ConditionalExpression c))
                    return false;

                Expression condition = Strip(c.Condition);
                bool matches = bits == 1
                    ? condition is NameExpression n && n.Name == select
                    : condition is IndexExpression i && Strip(i.Target) is NameExpression t && t.Name == select &&
                      i.Index is NumberLiteral k && k.Value.TryGetUInt64(out ulong at) && (int)at == bit;
                if (!matches)
                    return false;

                int half = 1 << bit;
                return Leaves(c.WhenTrue, select, bits, bit - 1, low + half, into) && Leaves(c.WhenFalse, select, bits, bit - 1, low, into);
            }

            /// <summary>
            /// The printer's register: <c>always_ff @(posedge clk) q &lt;= d;</c>, or with an asynchronous
            /// reset, <c>@(posedge clk, posedge rst) if (rst) q &lt;= v; else q &lt;= d;</c>.
            /// </summary>
            private void Register(ProceduralBlock block)
            {
                Statement body = block.Body;
                if (!(body is TimedStatement timed) || !(timed.Control is EventControl events) || events.Implicit)
                {
                    Problem(block, "this always_ff is not a register the board draws; it comes in as written code (Part 12).");
                    return;
                }

                Statement inner = Single(timed.Body);
                string clock = Edge(events, 0), reset = events.Events.Count == 2 ? Edge(events, 1) : null;

                string q = null, d = null;
                NumberLiteral resetValue = null;
                long tcq = 0;
                bool ok = clock != null && (events.Events.Count == 1 || reset != null);

                if (ok && reset == null)
                {
                    ok = Nonblocking(inner, out q, out d, out tcq);
                }
                else if (ok)
                {
                    ok = false;
                    if (inner is IfStatement choice && Strip(choice.Condition) is NameExpression r && r.Name == reset &&
                        Nonblocking(Single(choice.Then), out string resetTarget, out _, true, out NumberLiteral v, out long resetTcq) &&
                        Nonblocking(Single(choice.Else), out q, out d, out tcq) && resetTarget == q && resetTcq == tcq)
                    {
                        ok = true;
                        resetValue = v;
                    }
                }

                if (!ok)
                {
                    Problem(block, "this always_ff is not a register the board draws; it comes in as written code (Part 12).");
                    return;
                }

                NetFound qNet = Net(q);
                qNet.IsRegister = true;
                var register = new RegisterPart
                {
                    Width = qNet.Width,
                    HasReset = reset != null,
                    ResetValue = resetValue == null ? (LogicVec?)null : Resized(resetValue, qNet.Width),
                    Initial = qNet.Initial,
                    Tcq = tcq,   // its setup is not in the code, so it comes in as 0
                };

                int id = Add(register, block);
                if (id == 0)
                    return;

                qNet.Pins.Add(Endpoint.At(id, "q"));
                Net(d).Pins.Add(Endpoint.At(id, "d"));
                Net(clock).Pins.Add(Endpoint.At(id, "clk"));
                if (reset != null)
                    Net(reset).Pins.Add(Endpoint.At(id, "rst"));
            }

            private static string Edge(EventControl events, int index)
            {
                if (index >= events.Events.Count)
                    return null;
                EventExpression e = events.Events[index];
                return e.Edge == "posedge" && Strip(e.Value) is NameExpression n && n.Path.Count == 1 ? n.Name : null;
            }

            private static bool Nonblocking(Statement s, out string target, out string source, out long delay) =>
                Nonblocking(s, out target, out source, false, out _, out delay);

            /// <summary>
            /// A register's write, <c>q &lt;= d;</c> -- or <c>q &lt;= #2 d;</c>, its clock to Q (Part 14a), a constant delay
            /// being all a register's write may carry.
            /// </summary>
            private static bool Nonblocking(Statement s, out string target, out string source, bool constant, out NumberLiteral value, out long delay)
            {
                target = source = null;
                value = null;
                delay = 0;
                if (!(s is AssignmentStatement a) || !a.IsNonBlocking || !(a.Target is NameExpression t) || t.Path.Count != 1)
                    return false;
                if (a.IntraAssignment != null)
                {
                    if (!(a.IntraAssignment is DelayControl after) || after.Values.Count != 1 || !(Strip(after.Values[0]) is NumberLiteral tcq) ||
                        !tcq.Value.TryGetUInt64(out ulong d) || d > long.MaxValue)
                        return false;
                    delay = (long)d;
                }

                target = t.Name;
                Expression from = Strip(a.Value);
                if (constant)
                {
                    if (!(from is NumberLiteral n))
                        return false;
                    value = n;
                    return true;
                }

                if (!(from is NameExpression f) || f.Path.Count != 1)
                    return false;
                source = f.Name;
                return true;
            }

            private static Statement Single(Statement s) => s is BlockStatement b && b.Statements.Count == 1 && b.Declarations.Count == 0 ? b.Statements[0] : s;

            // -----------------------------------------------------------------
            // Wiring and laying out
            // -----------------------------------------------------------------

            /// <summary>Nets joined by <c>assign a = b;</c>: one net once drawn.</summary>
            private readonly List<(string To, string From)> _joins = new List<(string, string)>();

            private void Join()
            {
                // Union the joined names, so each drawn net gathers every pin of all its names.
                var parent = new Dictionary<string, string>(StringComparer.Ordinal);
                string Find(string x)
                {
                    while (parent.TryGetValue(x, out string up) && up != x)
                        x = up;
                    return x;
                }

                foreach (string name in _nets.Keys)
                    parent[name] = name;
                foreach ((string to, string from) in _joins)
                {
                    Net(to);
                    Net(from);
                    parent.TryAdd(to, to);
                    parent.TryAdd(from, from);
                    string a = Find(to), b = Find(from);
                    if (a != b)
                        parent[b] = a;
                }

                var groups = new Dictionary<string, List<string>>(StringComparer.Ordinal);
                foreach (string name in _nets.Keys)
                {
                    string root = Find(name);
                    if (!groups.TryGetValue(root, out List<string> group))
                        groups[root] = group = new List<string>();
                    group.Add(name);
                }

                foreach (List<string> group in groups.Values)
                {
                    var pins = new List<Endpoint>();
                    string named = null;
                    foreach (string name in group)
                    {
                        NetFound net = _nets[name];
                        pins.AddRange(net.Pins);
                        if (!net.IsPort && name[0] != '\u0001' && named == null)
                            named = name;
                    }

                    for (int i = 1; i < pins.Count; i++)
                    {
                        string refused = Connect(pins[0], pins[i], out int wire);
                        if (refused != null)
                        {
                            Problem(_source, refused);
                            return;
                        }

                        if (i == 1 && named != null && !IsMade(named, pins))
                            _wireNames.Add((wire, named));
                    }
                }
            }

            /// <summary>
            /// A wire between two pins, as <see cref="BoardEditor.Connect"/> makes one -- the module's next id -- and refused as it
            /// refuses one: of a pin to itself, or of two a wire already joins. Null, or why not.
            /// </summary>
            private string Connect(Endpoint from, Endpoint to, out int id)
            {
                id = 0;
                if (from.Equals(to))
                    return "A wire needs two different ends.";
                PinRef a = Root(from.Pin), b = Root(to.Pin);
                if (a.Equals(b))
                    return "Those are already joined.";

                _joinedTo[b] = a;
                id = _nextId++;
                _wires.Add(new Wire { Id = id, From = from, To = to });
                return null;
            }

            private PinRef Root(PinRef pin)
            {
                PinRef root = pin;
                while (_joinedTo.TryGetValue(root, out PinRef up))
                    root = up;
                while (_joinedTo.TryGetValue(pin, out PinRef up) && !up.Equals(root))
                {
                    _joinedTo[pin] = root;
                    pin = up;
                }

                return root;
            }

            /// <summary>Whether a name is the one the board would make anyway -- <c>g1_y</c> -- so it need not be given.</summary>
            private bool IsMade(string name, List<Endpoint> pins)
            {
                foreach (Endpoint pin in pins)
                {
                    _byId.TryGetValue(pin.Pin.Part, out Part part);
                    if (part != null && part.TryPin(_editor.Project, pin.Pin.Pin, out Pin p) && p.Direction != PinDirection.In)
                        return name == part.Name + "_" + pin.Pin.Pin;
                }

                return false;
            }

            /// <summary>Inputs in the first column, outputs in the last, everything else by how many parts lie between it and an input.</summary>
            private void Lay()
            {
                Module module = Drawn();
                Netlist nets = Netlist.Of(_editor.Project, module);
                var depth = new Dictionary<int, int>();
                foreach (Part part in module.Parts)
                    depth[part.Id] = part is PortPart port && port.Direction == PortDirection.Input ? 0 : 1;

                // Relax: a part is one further than whatever drives its inputs. A loop (through a
                // register) stops it after as many rounds as there are parts.
                for (int round = 0; round < module.Parts.Count; round++)
                {
                    bool moved = false;
                    foreach (Net net in nets.Nets)
                    {
                        if (net.Drivers.Count == 0)
                            continue;
                        int from = depth[net.Drivers[0].Part];
                        foreach (PinRef pin in net.Pins)
                        {
                            Part reader = module.FindPart(pin.Part);
                            if (pin.Equals(net.Drivers[0]) || reader is PortPart)
                                continue;
                            if (depth[pin.Part] < from + 1)
                            {
                                depth[pin.Part] = from + 1;
                                moved = true;
                            }
                        }
                    }

                    if (!moved)
                        break;
                }

                int last = 1;
                foreach (int d in depth.Values)
                    last = Math.Max(last, d + 1);

                var rows = new Dictionary<int, int>();
                foreach (Part part in module.Parts)
                {
                    int column = part is PortPart port && port.Direction == PortDirection.Output ? last : depth[part.Id];
                    rows.TryGetValue(column, out int row);
                    rows[column] = row + 1;
                    _placements[part.Id] = new Placement(column * 3, -row * 2);
                }
            }

            // -----------------------------------------------------------------
            // Small things
            // -----------------------------------------------------------------

            /// <summary>
            /// Places <paramref name="part"/> as <see cref="BoardEditor.Add"/> places one -- the module's next id, a name of its
            /// kind where it has none, a gate under a library at its cell's delays -- and keeps where in the code it is from.
            /// </summary>
            private int Add(Part part, SyntaxNode at)
            {
                if (part is GatePart gate && _editor.Project.Settings?.Library?.CellFor(gate.Kind, gate.Inputs) is LibraryCell cell)
                    part = gate with { Rise = cell.Rise, Fall = cell.Fall };

                int id = _nextId++;
                Part placed = part with { Id = id, Name = part.Name ?? DefaultName(part) };
                _parts.Add(placed);
                _byId[id] = placed;
                _taken.Add(placed.Name);
                _placements[id] = default;
                _madeAt[id] = at;
                return id;
            }

            /// <summary>
            /// The first free name of the part's kind, as <see cref="BoardEditor"/> gives one: a, b ... and y, z for a port, else
            /// g1, r1 .... Names are only added while a module is read, so the search for each kind goes on from where it stopped.
            /// </summary>
            private string DefaultName(Part part)
            {
                if (part is PortPart port)
                    foreach (string name in BoardEditor.FirstPortNames(port.Direction))
                        if (!_taken.Contains(name) && BoardRules.IsValidName(name))
                            return name;

                string prefix = BoardEditor.NamePrefixOf(part);
                _firstFree.TryGetValue(prefix, out int n);
                n = Math.Max(n, 1);
                while (_taken.Contains(prefix + n))
                    n++;
                _firstFree[prefix] = n;
                return prefix + n;
            }

            private NetFound Net(string name)
            {
                if (!_nets.TryGetValue(name, out NetFound net))
                    _nets[name] = net = new NetFound();
                return net;
            }

            private bool TryDelay(DelayControl delay, SyntaxNode at, out long rise, out long fall)
            {
                rise = fall = 0;
                if (delay == null)
                    return true;
                if (delay.Values.Count == 0 || !TryConstant(delay.Values[0], out rise) ||
                    (delay.Values.Count > 1 && !TryConstant(delay.Values[1], out fall)))
                {
                    Problem(at, "a delay that is not a plain number.");
                    return false;
                }

                if (delay.Values.Count == 1)
                    fall = rise;
                return true;
            }

            private bool TryConstant(Expression e, out long value)
            {
                value = 0;
                switch (Strip(e))
                {
                    case NumberLiteral n when n.Value.TryGetUInt64(out ulong v):
                        value = (long)v;
                        return true;
                    case NameExpression name when name.Path.Count == 1 && _parameters.TryGetValue(name.Name, out long p):
                        value = p;
                        return true;
                    case BinaryExpression b when TryConstant(b.Left, out long l) && TryConstant(b.Right, out long r):
                        switch (b.Operator)
                        {
                            case "+": value = l + r; return true;
                            case "-": value = l - r; return true;
                            case "*": value = l * r; return true;
                        }
                        return false;
                    default:
                        return false;
                }
            }

            private static Expression Strip(Expression e)
            {
                while (e is ParenthesizedExpression p)
                    e = p.Inner;
                return e;
            }

            /// <summary>
            /// A literal's value on a net <paramref name="width"/> wide, as an assignment gives it (IEEE 1800, 10.7 and 11.8.2):
            /// <c>'0</c>, <c>'1</c>, <c>'x</c> and <c>'z</c> fill it; any other is truncated, or extended -- with its sign
            /// when it is signed, as <c>4'sb1000</c> is, with 0s otherwise.
            /// </summary>
            private static LogicVec Resized(NumberLiteral literal, int width)
            {
                LogicVec value = literal.Value;
                if (literal.FillsContext)
                    return LogicVec.Fill(value[0], width);
                return value.Width == width ? value : value.Extend(width, value.IsSigned);
            }

            private static string Keyword(ProcessKind kind) => kind switch
            {
                ProcessKind.Initial => "initial",
                ProcessKind.Final => "final",
                ProcessKind.AlwaysComb => "always_comb",
                ProcessKind.AlwaysLatch => "always_latch",
                _ => "always",
            };
        }
    }
}
