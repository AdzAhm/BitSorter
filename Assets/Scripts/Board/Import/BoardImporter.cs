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
    /// the continuous assignments and registers a board prints -- and builds the board through
    /// <see cref="BoardEditor"/>, so what it makes has passed every rule a drawn board has.
    /// </summary>
    /// <remarks>
    /// Anything else -- <c>always_comb</c>, an <c>initial</c>, a delay given by a parameter -- is not
    /// drawable, and the import says what and where rather than drawing something else. A module like
    /// that will come in as written code (Part 12). Nets keep their names: each becomes the name of
    /// the wires that make it. Parts are laid out in columns by how far they are from the inputs.
    /// </remarks>
    public static class BoardImporter
    {
        public static ImportResult Import(SyntaxTree tree, string top)
        {
            var problems = new List<string>();
            var modules = new Dictionary<string, ModuleDeclaration>(StringComparer.Ordinal);
            foreach (ModuleDeclaration module in tree.Modules)
                modules[module.Name] = module;

            if (top == null || !modules.TryGetValue(top, out ModuleDeclaration topModule))
                return new ImportResult(null, new[] { $"There is no module called {top ?? "(none)"} to draw." });
            if (IsCell(top))
                return new ImportResult(null, new[] { $"{top} is a library cell: it is drawn as a gate, inside a module." });

            // Every module reachable from the top, children first, so a block's pins are known when it is placed.
            var order = new List<ModuleDeclaration>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            void Visit(ModuleDeclaration module, int depth)
            {
                if (!seen.Add(module.Name) || depth > 64)
                    return;
                foreach (ModuleItem item in module.Items)
                    if (item is ModuleInstantiation inst && !IsCell(inst.Module) && modules.TryGetValue(inst.Module, out ModuleDeclaration inner))
                        Visit(inner, depth + 1);
                order.Add(module);
            }

            Visit(topModule, 0);

            var editor = new BoardEditor(new Project { Name = top, Top = top });
            foreach (ModuleDeclaration module in order)
                new ModuleReader(editor, module, modules, problems).Read();

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
            private readonly List<string> _problems;
            private readonly string _name;

            /// <summary>Each net by name: its width and starting value, and the pins found on it.</summary>
            private readonly Dictionary<string, NetFound> _nets = new Dictionary<string, NetFound>(StringComparer.Ordinal);
            private readonly Dictionary<string, long> _parameters = new Dictionary<string, long>(StringComparer.Ordinal);

            /// <summary>Each splitter made, by the bus it splits.</summary>
            private readonly Dictionary<string, int> _splitters = new Dictionary<string, int>(StringComparer.Ordinal);

            private sealed class NetFound
            {
                public int Width = 1;
                public LogicVec? Initial;
                public bool IsPort;
                public readonly List<Endpoint> Pins = new List<Endpoint>();
            }

            public ModuleReader(BoardEditor editor, ModuleDeclaration source, Dictionary<string, ModuleDeclaration> modules, List<string> problems)
            {
                _editor = editor;
                _source = source;
                _modules = modules;
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

                if (_problems.Count > before)
                    return;

                Join();
                Lay();
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
                if (port.Direction == PortDirection.Inout)
                {
                    Problem(port, $"{port.Name} is an inout; two-way ports arrive with tri-state (Part 14).");
                    return;
                }

                int width = Width(port.Type, port);
                int id = Add(new PortPart { Name = port.Name, Direction = port.Direction, Width = width, Comment = port.Comment }, port);
                if (id == 0)
                    return;

                NetFound net = Net(port.Name);
                net.Width = width;
                net.IsPort = true;
                net.Pins.Add(Endpoint.At(id, port.Direction == PortDirection.Input ? "out" : "in"));
            }

            private void Declare(DataDeclaration declaration)
            {
                int width = Width(declaration.Type, declaration);
                foreach (Declarator declarator in declaration.Declarators)
                {
                    if (declarator.Unpacked.Count > 0)
                    {
                        Problem(declarator, $"{declarator.Name} is a memory; the board has no memory part yet.");
                        continue;
                    }

                    NetFound net = Net(declarator.Name);
                    net.Width = width;
                    if (declarator.Initializer != null)
                    {
                        if (declarator.Initializer is NumberLiteral literal)
                            net.Initial = Resized(literal.Value, width);
                        else
                            Problem(declarator, $"{declarator.Name} starts at a value that is not a plain number.");
                    }
                }
            }

            private int Width(DataType type, SyntaxNode at)
            {
                if (type == null || type.Packed.Count == 0)
                    return 1;
                if (type.Packed.Count > 1)
                {
                    Problem(at, "a vector of more than one dimension cannot be drawn.");
                    return 1;
                }

                Dimension range = type.Packed[0];
                if (TryConstant(range.Left, out long left) && TryConstant(range.Right, out long right))
                    return (int)Math.Abs(left - right) + 1;

                Problem(at, "a width that is not a plain number.");
                return 1;
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
                if (!Enum.TryParse(primitive.Primitive, true, out GateKind kind) || kind > GateKind.Not)
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
                    Net(inputs[i]).Pins.Add(Endpoint.At(id, GatePart.InputName(i)));
            }

            private void Instances(ModuleInstantiation instantiation)
            {
                if (CellKind(instantiation.Module, out GateKind kind, out int inputs))
                {
                    Cells(instantiation, kind, inputs);
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
                    int id = Add(new ConstantPart { Value = Resized(constant.Value, Net(to).Width) }, assignment);
                    if (id != 0)
                        Net(to).Pins.Add(Endpoint.At(id, "y"));
                    return;
                }

                if (TryGate(value, out GateKind kind, out List<string> inputs))
                {
                    Gate(kind, null, to, inputs, rise, fall, assignment);
                    return;
                }

                if (!delayed && value is ConditionalExpression && TryMux(value, out List<string> data, out string select))
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
                    if (!_splitters.TryGetValue(bus.Name, out int splitter))
                    {
                        splitter = Add(new SplitterPart { Width = Net(bus.Name).Width }, assignment);
                        if (splitter == 0)
                            return;
                        _splitters[bus.Name] = splitter;
                        Net(bus.Name).Pins.Add(Endpoint.At(splitter, "in"));
                    }

                    Net(to).Pins.Add(Endpoint.At(splitter, "o" + index));
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
            private static bool TryMux(Expression value, out List<string> data, out string select)
            {
                data = null;
                select = null;

                // The condition at the root names the select: s, or s[top].
                Expression root = Strip(((ConditionalExpression)value).Condition);
                int bits;
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
                LogicVec? resetValue = null;
                bool ok = clock != null && (events.Events.Count == 1 || reset != null);

                if (ok && reset == null)
                {
                    ok = Nonblocking(inner, out q, out d);
                }
                else if (ok)
                {
                    ok = false;
                    if (inner is IfStatement choice && Strip(choice.Condition) is NameExpression r && r.Name == reset &&
                        Nonblocking(Single(choice.Then), out string resetTarget, out _, true, out LogicVec v) &&
                        Nonblocking(Single(choice.Else), out q, out d) && resetTarget == q)
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
                var register = new RegisterPart
                {
                    Width = qNet.Width,
                    HasReset = reset != null,
                    ResetValue = resetValue == null ? (LogicVec?)null : Resized(resetValue.Value, qNet.Width),
                    Initial = qNet.Initial,
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

            private static bool Nonblocking(Statement s, out string target, out string source) =>
                Nonblocking(s, out target, out source, false, out _);

            private static bool Nonblocking(Statement s, out string target, out string source, bool constant, out LogicVec value)
            {
                target = source = null;
                value = default;
                if (!(s is AssignmentStatement a) || !a.IsNonBlocking || a.IntraAssignment != null ||
                    !(a.Target is NameExpression t) || t.Path.Count != 1)
                    return false;

                target = t.Name;
                Expression from = Strip(a.Value);
                if (constant)
                {
                    if (!(from is NumberLiteral n))
                        return false;
                    value = n.Value;
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
                        EditResult wire = _editor.Connect(_name, pins[0], pins[i]);
                        if (!wire.Done)
                        {
                            Problem(_source, wire.Refusal);
                            return;
                        }

                        if (i == 1 && named != null && !IsMade(named, pins))
                            _editor.NameWire(_name, wire.Id, named);
                    }
                }
            }

            /// <summary>Whether a name is the one the board would make anyway -- <c>g1_y</c> -- so it need not be given.</summary>
            private bool IsMade(string name, List<Endpoint> pins)
            {
                Module module = _editor.Project.Find(_name);
                foreach (Endpoint pin in pins)
                {
                    Part part = module.FindPart(pin.Pin.Part);
                    if (part != null && part.TryPin(_editor.Project, pin.Pin.Pin, out Pin p) && p.Direction == PinDirection.Out)
                        return name == part.Name + "_" + pin.Pin.Pin;
                }

                return false;
            }

            /// <summary>Inputs in the first column, outputs in the last, everything else by how many parts lie between it and an input.</summary>
            private void Lay()
            {
                Module module = _editor.Project.Find(_name);
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
                    _editor.Move(_name, part.Id, new Placement(column * 3, -row * 2));
                }
            }

            // -----------------------------------------------------------------
            // Small things
            // -----------------------------------------------------------------

            private int Add(Part part, SyntaxNode at)
            {
                EditResult added = _editor.Add(_name, part, default);
                if (!added.Done)
                {
                    Problem(at, added.Refusal);
                    return 0;
                }

                return added.Id;
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

            private static LogicVec Resized(LogicVec value, int width) => value.Width == width ? value : value.Extend(width, false);

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
