using System;
using System.Collections.Generic;
using BitSorter.Sim;
using BitSorter.SystemVerilog;
using BitSorter.SystemVerilog.Syntax;

namespace BitSorter.Board
{
    /// <summary>One module, compiled: its declaration and the nets its signals are named after.</summary>
    public sealed class CompiledModule
    {
        internal CompiledModule(Module module, Netlist nets, ModuleDeclaration declaration, IReadOnlyList<(string Signal, RegisterTiming Timing)> registers = null)
        {
            Module = module;
            Nets = nets;
            Declaration = declaration;
            Registers = registers ?? Array.Empty<(string, RegisterTiming)>();
        }

        public Module Module { get; }
        public Netlist Nets { get; }
        public ModuleDeclaration Declaration { get; }

        /// <summary>
        /// Each register's clock-to-Q and setup (Part 14a), by the name of the variable it writes in this module: what static
        /// timing needs and the code cannot say, since a setup is checked, never simulated.
        /// </summary>
        public IReadOnlyList<(string Signal, RegisterTiming Timing)> Registers { get; }
    }

    /// <summary>A whole project, compiled: one syntax tree, ready to print and to elaborate.</summary>
    public sealed class CompiledProject
    {
        internal CompiledProject(SyntaxTree tree, string top, string testbench, string designPath, IReadOnlyList<CompiledModule> modules,
            IReadOnlyList<string> cells, IReadOnlyList<BoardDiagnostic> diagnostics)
        {
            Tree = tree;
            Top = top;
            Testbench = testbench;
            DesignPath = designPath;
            Modules = modules;
            Cells = cells;
            Diagnostics = diagnostics;
        }

        /// <summary>The cells used, every module, and the testbench; null when the document has errors.</summary>
        public SyntaxTree Tree { get; }

        /// <summary>What to elaborate: the testbench when there is a stimulus, otherwise the top module.</summary>
        public string Top { get; }

        /// <summary>The testbench's name, or null when the project has no stimulus.</summary>
        public string Testbench { get; }

        /// <summary>
        /// Where the top module's own signals are in the elaborated design: <c>half_adder_tb.dut</c> under
        /// a testbench, <c>half_adder</c> without one. A net's signal is this, a dot, and the net's name.
        /// </summary>
        public string DesignPath { get; }

        /// <summary>Each module of the project, children before the modules that use them.</summary>
        public IReadOnlyList<CompiledModule> Modules { get; }

        /// <summary>The cells the board uses, by name.</summary>
        public IReadOnlyList<string> Cells { get; }

        public IReadOnlyList<BoardDiagnostic> Diagnostics { get; }

        public bool Succeeded => Tree != null;

        public CompiledModule Find(string module)
        {
            foreach (CompiledModule m in Modules)
                if (m.Module.Name == module)
                    return m;
            return null;
        }
    }

    /// <summary>
    /// A document made into SystemVerilog: a syntax tree, which is printed for the code panel and for
    /// export, and elaborated for the engine. One tree for both, so what the code shows is what runs.
    /// </summary>
    /// <remarks>
    /// In the course's style (section 7): a header with inputs first and outputs last, one commented
    /// port a line; the module's own nets as <c>logic</c>, one declaration a width; an ideal one-bit
    /// gate as a primitive, <c>and g1 (y, a, b);</c>, and one with delay as a cell of
    /// <see cref="CellLibrary"/> (more than four inputs, a primitive with the delay); anything wider
    /// than a bit as a continuous assignment; a register as <c>always_ff</c>; a block as an instance,
    /// connected by name. An output that shares its net with an input or another output is fed by an
    /// <c>assign</c>. Lamps print nothing. A written module (Part 12d) is its own text, parsed.
    ///
    /// A pin with nothing on it reads a net of its own, <c>g1_b</c>, which nothing drives, so it is
    /// x -- what a gate makes of an open input. A block's open pin is left open,
    /// <c>.b()</c>, as a port would be, and reads z inside.
    /// </remarks>
    public static class BoardCompiler
    {
        /// <summary>The name of the one instance inside an array's generate block (Part 12c): copy i is <c>name[i].u</c>.</summary>
        public const string ArrayInstance = "u";

        public static CompiledProject Compile(Project project)
        {
            List<BoardDiagnostic> diagnostics = BoardRules.Check(project);
            foreach (BoardDiagnostic d in diagnostics)
                if (d.IsError)
                    return new CompiledProject(null, null, null, null, Array.Empty<CompiledModule>(), Array.Empty<string>(), diagnostics);

            var cells = new SortedSet<string>(StringComparer.Ordinal);
            var compiled = new List<CompiledModule>();
            foreach (Module module in Ordered(project))
            {
                Netlist nets = Netlist.Of(project, module);
                if (module.IsWritten)
                {
                    // As written (Part 12d), with the cells it instantiates.
                    compiled.Add(new CompiledModule(module, nets, WrittenModules.Read(project, module).Declaration));
                    foreach (string cell in WrittenModules.CellsOf(project, module))
                        cells.Add(cell);
                    continue;
                }

                var compiler = new ModuleCompiler(project, module, nets, cells);
                compiled.Add(new CompiledModule(module, nets, compiler.Build(), compiler.Registers));
            }

            // The unit reaches the SystemVerilog as a `timescale, unless it is ps, which needs none
            // (TimeUnits; decision D7). It labels time and changes nothing the engine does.
            string unit = project.Settings?.TimeUnit ?? TimeUnits.Default;
            var tree = new SyntaxTree { Timescale = unit == TimeUnits.Default ? null : TimeUnits.Timescale(unit) };
            // A cell's delays default to the library's (Part 14g): a cell written without them takes them, here as when it is read.
            foreach (string cell in cells)
                tree.Modules.Add(CellLibrary.Build(cell, project.Settings?.Library));
            foreach (CompiledModule module in compiled)
                tree.Modules.Add(module.Declaration);

            string testbench = null, designPath = project.Top;
            if (project.Bench != null)
            {
                // The course's own testbench, written (Part 13b): already a module of the tree, and the run's top. The
                // design is the instance of the top in it.
                testbench = project.Bench;
                designPath = testbench + "." + (InstanceOf(WrittenModules.Read(project, project.Find(project.Bench)).Declaration, project.Top) ?? project.Top);
            }
            else if (project.Stimulus != null)
            {
                ModuleDeclaration bench = Testbench.Build(project, Taken(project), out string dut);
                tree.Modules.Add(bench);
                testbench = bench.Name;
                designPath = testbench + "." + dut;
            }

            return new CompiledProject(tree, testbench ?? project.Top, testbench, designPath, compiled, new List<string>(cells), diagnostics);
        }

        /// <summary>The modules, each after every module it uses.</summary>
        private static List<Module> Ordered(Project project)
        {
            var ordered = new List<Module>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            void Visit(Module module)
            {
                if (!seen.Add(module.Name))
                    return;
                foreach (Module inner in WrittenModules.UsesOf(project, module))
                    Visit(inner);
                ordered.Add(module);
            }

            foreach (Module module in project.Modules)
                Visit(module);
            return ordered;
        }

        /// <summary>
        /// Where the first instance of <paramref name="module"/> in <paramref name="declaration"/> is, below it: its name, after
        /// the label of a named generate block it is in; null with none. One in a generate construct that chooses or repeats
        /// is not looked for, since which one is the design is the elaborator's to say.
        /// </summary>
        internal static string InstanceOf(ModuleDeclaration declaration, string module)
        {
            string Find(IEnumerable<ModuleItem> items)
            {
                foreach (ModuleItem item in items)
                {
                    string found = item switch
                    {
                        ModuleInstantiation instantiation when instantiation.Module == module && instantiation.Instances.Count > 0
                            => instantiation.Instances[0].Name,
                        GenerateRegion region => Find(region.Items),
                        GenerateBlock block => Find(block.Items) is string inside && !string.IsNullOrEmpty(block.Label) ? block.Label + "." + inside : Find(block.Items),
                        _ => null,
                    };
                    if (found != null)
                        return found;
                }

                return null;
            }

            return declaration == null ? null : Find(declaration.Items);
        }

        private static HashSet<string> Taken(Project project)
        {
            var taken = new HashSet<string>(StringComparer.Ordinal);
            foreach (Module module in project.Modules)
                taken.Add(module.Name);
            return taken;
        }

        private sealed class ModuleCompiler
        {
            private readonly Project _project;
            private readonly Module _module;
            private readonly Netlist _nets;
            private readonly SortedSet<string> _cells;
            private readonly ModuleDeclaration _out;
            private readonly HashSet<string> _names = new HashSet<string>(StringComparer.Ordinal);

            /// <summary>Starting values for nets a register drives, by net.</summary>
            private readonly Dictionary<Net, LogicVec> _initial = new Dictionary<Net, LogicVec>();

            /// <summary>Variables made for a register whose q is a port and has a starting value: a port cannot be given one.</summary>
            private readonly List<(string Name, int Width, LogicVec Initial)> _made = new List<(string, int, LogicVec)>();

            private readonly List<ModuleItem> _items = new List<ModuleItem>();

            /// <summary>Each register's clock-to-Q and setup, by the variable it writes (<see cref="CompiledModule.Registers"/>).</summary>
            public List<(string Signal, RegisterTiming Timing)> Registers { get; } = new List<(string, RegisterTiming)>();

            public ModuleCompiler(Project project, Module module, Netlist nets, SortedSet<string> cells)
            {
                _project = project;
                _module = module;
                _nets = nets;
                _cells = cells;
                _out = new ModuleDeclaration { Name = module.Name, HasPortList = true, Comment = module.Comment };

                foreach (Net net in nets.Nets)
                    _names.Add(net.Name);
                foreach (Part part in module.Parts)
                    _names.Add(part.Name);
            }

            public ModuleDeclaration Build()
            {
                // A port that is a bus is a tri net (Part 14f), and so is one an inout pin is on (Part 14g): a logic variable
                // takes one driver, and an inout joins only a net.
                foreach (PortPart port in _module.Ports)
                    _out.Ports.Add(new PortDeclaration
                    {
                        Direction = port.Direction,
                        NetType = BoardRules.IsTri(NetOf(port, port.Direction == PortDirection.Input ? "out" : "in"), _module) ? "tri" : null,
                        Type = Sv.Logic(port.Width),
                        Name = port.Name,
                        Comment = port.Comment,
                    });

                foreach (Part part in _module.Parts)
                    Compile(part);

                // An output that shares a net with an input, or with an output named first, is fed from it.
                foreach (Net net in _nets.Nets)
                    for (int i = 1; i < net.Ports.Count; i++)
                        if (net.Ports[i].Direction == PortDirection.Output)
                            _items.Add(Sv.Assign(Sv.Name(net.Ports[i].Name), Sv.Name(net.Name)));

                Declare();
                _out.Items.AddRange(_items);
                return _out;
            }

            /// <summary>Every net that is not a port, one declaration a width, in the order the nets come.</summary>
            private void Declare()
            {
                var byWidth = new Dictionary<int, DataDeclaration>();
                var order = new List<DataDeclaration>();

                void Add(string name, int width, LogicVec? initial, bool bus = false)
                {
                    // A net with a starting value gets a declaration of its own, as the course writes one; so does a bus,
                    // a tri net, which several tri-state gates may drive (Part 14f).
                    if (initial != null || bus)
                    {
                        var own = new DataDeclaration { NetType = bus ? "tri" : null, Type = Sv.Logic(width) };
                        own.Declarators.Add(new Declarator { Name = name, Initializer = initial.HasValue ? Sv.Value(initial.Value) : null });
                        order.Add(own);
                        return;
                    }

                    if (!byWidth.TryGetValue(width, out DataDeclaration declaration))
                    {
                        declaration = new DataDeclaration { Type = Sv.Logic(width) };
                        byWidth[width] = declaration;
                        order.Add(declaration);
                    }

                    declaration.Declarators.Add(new Declarator { Name = name });
                }

                foreach (Net net in _nets.Nets)
                {
                    if (net.Ports.Count > 0 || IsOpenBlockPin(net) || OnlyLamps(net))
                        continue;
                    Add(net.Name, net.Width, _initial.TryGetValue(net, out LogicVec initial) ? initial : (LogicVec?)null, BoardRules.IsTri(net, _module));
                }

                foreach ((string name, int width, LogicVec initial) in _made)
                    Add(name, width, initial);

                _out.Items.AddRange(order);
            }

            private void Compile(Part part)
            {
                switch (part)
                {
                    case GatePart gate:
                        Gate(gate);
                        break;

                    case ConstantPart constant:
                        _items.Add(Sv.Assign(At(constant, "y"), Sv.Value(constant.Value)));
                        break;

                    case RegisterPart register:
                        Register(register);
                        break;

                    case MuxPart mux:
                        _items.Add(Sv.Assign(At(mux, "y"), Choose(mux, 0, mux.Inputs, mux.SelectWidth - 1)));
                        break;

                    case SplitterPart splitter:
                        for (int i = 0; i < splitter.Width; i++)
                            _items.Add(Sv.Assign(At(splitter, "o" + i), Sv.Bit(NetName(splitter, "in"), i)));
                        break;

                    case JoinerPart joiner:
                        var bits = new ConcatenationExpression();
                        for (int i = joiner.Width - 1; i >= 0; i--)
                            bits.Items.Add(At(joiner, "i" + i));
                        _items.Add(Sv.Assign(At(joiner, "out"), bits));
                        break;

                    case BlockPart block:
                        Block(block);
                        break;

                    // Ports are the header; lamps only look.
                }
            }

            private void Gate(GatePart gate)
            {
                var inputs = new List<Expression>();
                for (int i = 0; i < gate.Inputs; i++)
                    inputs.Add(At(gate, GatePart.InputPin(gate.Kind, i)));
                Expression output = At(gate, "y");

                if (gate.Width == 1)
                {
                    string cell = gate.IsIdeal ? null : CellLibrary.CellFor(gate.Kind, gate.Inputs);
                    if (cell != null)
                    {
                        _cells.Add(cell);
                        var instantiation = new ModuleInstantiation { Module = cell, HasParameterList = true };
                        instantiation.Parameters.Add(new ParameterValue { Name = CellLibrary.Rise, Value = Sv.Number(gate.Rise) });
                        instantiation.Parameters.Add(new ParameterValue { Name = CellLibrary.Fall, Value = Sv.Number(gate.Fall) });

                        var instance = new ModuleInstance { Name = gate.Name };
                        for (int i = 0; i < gate.Inputs; i++)
                            instance.Connections.Add(Sv.Connect(CellLibrary.Input(i), inputs[i]));
                        instance.Connections.Add(Sv.Connect(CellLibrary.Output, output));
                        instantiation.Instances.Add(instance);
                        _items.Add(instantiation);
                        return;
                    }

                    var primitive = new PrimitiveInstantiation
                    {
                        Primitive = gate.Kind.ToString().ToLowerInvariant(),
                        Delay = gate.IsIdeal ? null : PrimitiveDelay(gate),
                    };
                    var gateInstance = new PrimitiveInstance { Name = gate.Name };
                    gateInstance.Terminals.Add(output);
                    gateInstance.Terminals.AddRange(inputs);
                    primitive.Instances.Add(gateInstance);
                    _items.Add(primitive);
                    return;
                }

                // Wider than a bit: the gate's operator, bitwise.
                Expression value;
                switch (gate.Kind)
                {
                    case GateKind.And: value = Sv.Chain("&", inputs); break;
                    case GateKind.Or: value = Sv.Chain("|", inputs); break;
                    case GateKind.Xor: value = Sv.Chain("^", inputs); break;
                    case GateKind.Nand: value = Sv.Not(Sv.Chain("&", inputs)); break;
                    case GateKind.Nor: value = Sv.Not(Sv.Chain("|", inputs)); break;
                    case GateKind.Xnor: value = Sv.Not(Sv.Chain("^", inputs)); break;
                    case GateKind.Not: value = Sv.Not(inputs[0]); break;
                    default: value = inputs[0]; break;
                }

                _items.Add(Sv.Assign(output, value, Sv.Delay(gate.Rise, gate.Fall)));
            }

            private static DelayControl PrimitiveDelay(GatePart gate)
            {
                var delay = new DelayControl { Parenthesized = true };
                delay.Values.Add(Sv.Number(gate.Rise));
                delay.Values.Add(Sv.Number(gate.Fall));
                return delay;
            }

            private void Register(RegisterPart register)
            {
                Net q = NetOf(register, "q");
                string target = q.Name;

                if (register.Initial is LogicVec initial)
                {
                    if (q.Ports.Count > 0)
                    {
                        // A port cannot be given a starting value; a variable of the register's own can.
                        target = Unique(register.Name + "_q");
                        _made.Add((target, register.Width, initial));
                        _items.Add(Sv.Assign(Sv.Name(q.Name), Sv.Name(target)));
                    }
                    else
                    {
                        _initial[q] = initial;
                    }
                }

                // Its clock to Q is a delay on the write, q <= #Tcq d, so the run shows it (Part 14a); its setup is checked by
                // static timing alone, so it is handed over beside the code.
                Registers.Add((target, new RegisterTiming(register.Tcq, register.Tsetup)));
                AssignmentStatement Write(Expression value)
                {
                    AssignmentStatement write = Sv.Set(Sv.Name(target), value, nonBlocking: true);
                    if (register.Tcq > 0)
                        write.IntraAssignment = new DelayControl { Values = { Sv.Number(register.Tcq) } };
                    return write;
                }

                Statement body = Write(At(register, "d"));
                EventControl edge;

                if (register.HasReset)
                {
                    LogicVec reset = register.ResetValue ?? LogicVec.FromUInt64(0, register.Width);
                    body = new IfStatement
                    {
                        Condition = At(register, "rst"),
                        Then = Write(Sv.Value(reset)),
                        Else = body,
                    };
                    edge = Sv.On(("posedge", NetName(register, "clk")), ("posedge", NetName(register, "rst")));
                }
                else
                {
                    edge = Sv.On(("posedge", NetName(register, "clk")));
                }

                _items.Add(new ProceduralBlock { Kind = ProcessKind.AlwaysFf, Body = new TimedStatement { Control = edge, Body = body } });
            }

            /// <summary>d<i>low</i> to d<i>low+count-1</i>, chosen by s from bit <paramref name="bit"/> down.</summary>
            private Expression Choose(MuxPart mux, int low, int count, int bit)
            {
                if (count == 1)
                    return At(mux, "d" + low);

                Expression select = mux.SelectWidth == 1 ? At(mux, "s") : Sv.Bit(NetName(mux, "s"), bit);
                int half = count / 2;
                return new ConditionalExpression
                {
                    Condition = select,
                    WhenTrue = Sv.Paren(Choose(mux, low + half, half, bit - 1)),
                    WhenFalse = Sv.Paren(Choose(mux, low, half, bit - 1)),
                };
            }

            private void Block(BlockPart block)
            {
                if (block.Copies > 1)
                {
                    BlockArray(block);
                    return;
                }

                var instantiation = new ModuleInstantiation { Module = block.Module };
                var instance = new ModuleInstance { Name = block.Name };
                foreach (Pin pin in block.Pins(_project))
                {
                    Net net = NetOf(block, pin.Name);
                    instance.Connections.Add(Sv.Connect(pin.Name, IsOpenBlockPin(net) ? null : Sv.Name(net.Name)));
                }

                instantiation.Instances.Add(instance);
                _items.Add(instantiation);
            }

            /// <summary>
            /// An array of a block (Part 12c), in the course's style: a <c>generate for</c> labelled with the block's name,
            /// holding one instance, <c>u</c>, each of whose ports takes its own copy's bits of the bus on the block's pin --
            /// <c>.a(a[i])</c>, or <c>.a(a[i*4 +: 4])</c> for a port four bits wide. Its genvar is <c>i</c> unless the module
            /// already has something of that name.
            /// </summary>
            private void BlockArray(BlockPart block)
            {
                Module inner = _project.Find(block.Module);
                string genvar = Unique("i");
                var instance = new ModuleInstance { Name = ArrayInstance };
                foreach (ModulePort port in WrittenModules.PortsOf(_project, inner))
                {
                    Net net = NetOf(block, port.Name);
                    instance.Connections.Add(Sv.Connect(port.Name, IsOpenBlockPin(net) ? null : CopyOf(net.Name, port.Width, genvar)));
                }

                var instantiation = new ModuleInstantiation { Module = block.Module };
                instantiation.Instances.Add(instance);
                var body = new GenerateBlock { Label = block.Name, HasBeginEnd = true };
                body.Items.Add(instantiation);
                _items.Add(new LoopGenerate
                {
                    DeclaresGenvar = true,
                    Variable = genvar,
                    Start = Sv.Number(0),
                    Condition = new BinaryExpression { Operator = "<", Left = Sv.Name(genvar), Right = Sv.Number(block.Copies) },
                    StepOperator = "++",
                    Body = body,
                });
            }

            /// <summary>Copy <paramref name="genvar"/>'s <paramref name="width"/> bits of the bus <paramref name="net"/>.</summary>
            private static Expression CopyOf(string net, int width, string genvar)
            {
                if (width == 1)
                    return new IndexExpression { Target = Sv.Name(net), Index = Sv.Name(genvar) };
                return new RangeSelectExpression
                {
                    Target = Sv.Name(net),
                    Left = new BinaryExpression { Operator = "*", Left = Sv.Name(genvar), Right = Sv.Number(width) },
                    Right = Sv.Number(width),
                    Kind = RangeKind.IndexedUp,
                };
            }

            /// <summary>A block's pin with nothing on it: left open in the instance, and not declared.</summary>
            private bool IsOpenBlockPin(Net net) =>
                net.Wires.Count == 0 && net.Pins.Count == 1 && _module.FindPart(net.Pins[0].Part) is BlockPart;

            /// <summary>A net with nothing on it but lamps: nothing to print, and nothing to show but x.</summary>
            private bool OnlyLamps(Net net)
            {
                foreach (PinRef pin in net.Pins)
                    if (!(_module.FindPart(pin.Part) is LampPart))
                        return false;
                return net.Pins.Count > 0;
            }

            private Net NetOf(Part part, string pin) => _nets.NetOf(new PinRef(part.Id, pin));
            private string NetName(Part part, string pin) => NetOf(part, pin).Name;
            /// <summary>The net on a part's pin, as a name to read or write.</summary>
            private Expression At(Part part, string pin) => Sv.Name(NetName(part, pin));

            private string Unique(string name)
            {
                string unique = name;
                for (int n = 2; _names.Contains(unique); n++)
                    unique = name + "_" + n;
                _names.Add(unique);
                return unique;
            }
        }
    }
}
