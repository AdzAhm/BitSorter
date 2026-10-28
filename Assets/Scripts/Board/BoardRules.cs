using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using BitSorter.Sim;
using BitSorter.SystemVerilog;
using BitSorter.SystemVerilog.Syntax;

namespace BitSorter.Board
{
    /// <summary>Something wrong with a document, or worth knowing: in plain words, and where.</summary>
    public sealed class BoardDiagnostic
    {
        public BoardDiagnostic(DiagnosticSeverity severity, string module, string message, int part = -1, int wire = -1, int line = 0, int column = 0)
        {
            Severity = severity;
            Module = module;
            Message = message;
            Part = part;
            Wire = wire;
            Line = line;
            Column = column;
        }

        public DiagnosticSeverity Severity { get; }

        /// <summary>The module it is in, or null for the project as a whole.</summary>
        public string Module { get; }

        public string Message { get; }

        /// <summary>The part it is about, or -1.</summary>
        public int Part { get; }

        /// <summary>The wire it is about, or -1.</summary>
        public int Wire { get; }

        /// <summary>Where it is in a written module's text (Part 12d), counted from 1; 0 when it is not about text.</summary>
        public int Line { get; }

        /// <inheritdoc cref="Line"/>
        public int Column { get; }

        public bool IsError => Severity == DiagnosticSeverity.Error;

        public override string ToString() => $"{Severity}: {(Module == null ? "" : Module + ": ")}{Message}";
    }

    /// <summary>
    /// What a document must be for it to compile, and what is worth a warning. <see cref="BoardEditor"/>
    /// refuses an edit that would add an error; a document read from elsewhere -- an old save, an
    /// import -- is checked with the same rules and may arrive with errors, each explained.
    /// </summary>
    public static class BoardRules
    {
        /// <summary>The widest a part or port may be.</summary>
        public const int MaxWidth = 1024;

        private static readonly Regex Identifier = new Regex("^[A-Za-z_][A-Za-z0-9_$]*$", RegexOptions.CultureInvariant);

        /// <summary>Whether <paramref name="name"/> can be a name in SystemVerilog: an identifier, and not a keyword.</summary>
        public static bool IsValidName(string name) =>
            !string.IsNullOrEmpty(name) && Identifier.IsMatch(name) && !Keywords.IsKeyword(name);

        /// <summary>Why <paramref name="name"/> cannot be a name, or null if it can.</summary>
        public static string WhyNotAName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return "A name cannot be empty.";
            if (Keywords.IsKeyword(name))
                return $"\"{name}\" is a SystemVerilog keyword, so it cannot be a name.";
            if (!Identifier.IsMatch(name))
                return $"\"{name}\" is not a name SystemVerilog accepts: start with a letter or _, then letters, digits, _ or $.";
            return null;
        }

        public static List<BoardDiagnostic> Check(Project project)
        {
            var found = new List<BoardDiagnostic>();
            void Error(string module, string message, int part = -1, int wire = -1) =>
                found.Add(new BoardDiagnostic(DiagnosticSeverity.Error, module, message, part, wire));

            // Each module is printed to a file of its name (Part 13a): no two may share one, nor share the cells' file, on a
            // disk that does not tell case apart either -- Windows, where the course's ModelSim runs.
            var moduleNames = new HashSet<string>(StringComparer.Ordinal);
            var fileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Module module in project.Modules)
            {
                string why = WhyNotAName(module.Name);
                if (why != null)
                    Error(null, $"Module {module.Name}: {why}");
                else if (!moduleNames.Add(module.Name))
                    Error(null, $"There are two modules called {module.Name}.");
                else if (BoardImporter.IsCell(module.Name))
                    Error(null, $"{module.Name} is the name of a cell of the library, so it cannot be a module's.");
                else if (string.Equals(module.Name + ".sv", CellLibrary.FileName, StringComparison.OrdinalIgnoreCase))
                    Error(null, $"{module.Name} is the name of the file the library's cells are written to, so it cannot be a module's.");
                else if (!fileNames.Add(module.Name))
                    Error(null, $"{module.Name} differs from another module's name only in case, so the two would be written to one file.");
            }

            if (project.Find(project.Top) == null)
                Error(null, $"The top module, {project.Top ?? "(none)"}, is not in the project.");
            CheckBench(project, found);
            CheckFiles(project, found);

            foreach (Module module in project.Modules)
                CheckModule(project, module, found);

            CheckRecursion(project, found);
            CheckStimulus(project, found);
            CheckSettings(project, found);
            CheckLibrary(project, found);
            return found;
        }

        /// <summary>
        /// The gate library (Part 14d), when the project has one: its cells BitSorter's, each once, at delays a gate could
        /// have; every drawn gate one of them at its cell's delays; no MUX, which is no cell; no constant where it takes none
        /// -- errors, so an edit that breaks the library is refused as any other is.
        /// </summary>
        /// <remarks>
        /// **A written module is held to it too** (Part 14g), as it is read (<see cref="WrittenModules"/>): every gate a cell at
        /// its delays, no logic written with operators, no constant where none is taken -- each fault at its line, which the
        /// code editor marks. The testbench is not held. **The budget is not a rule here**: the TIMING tab lays the design's
        /// gate count against it, and the TRANSCRIPT says when it is over in that count, never a second one (the 14d review
        /// found two).
        /// </remarks>
        private static void CheckLibrary(Project project, List<BoardDiagnostic> found)
        {
            GateLibrary library = project.Settings?.Library;
            if (library == null)
                return;

            void Error(string module, string message, int part = -1) =>
                found.Add(new BoardDiagnostic(DiagnosticSeverity.Error, module, message, part));

            var seen = new HashSet<(GateKind, int)>();
            foreach (LibraryCell cell in library.Cells)
            {
                if (!GateLibrary.CanHold(cell.Kind, cell.Inputs))
                    Error(null, GatePart.IsTriStateKind(cell.Kind)
                        ? $"The library's {cell.Kind.ToString().ToUpperInvariant()} of {cell.Inputs} inputs is no cell: a tri-state gate has a data input and an enable."
                        : $"The library's {cell.Name} is no cell BitSorter has.");
                else if (!seen.Add((cell.Kind, cell.Inputs)))
                    Error(null, $"The library has {cell.Name} twice.");
                if (cell.Rise < 0 || cell.Fall < 0)
                    Error(null, $"The library's {cell.Name} has a delay below 0.");
            }
            if (library.GateBudget < -1)
                Error(null, "The library's gate budget cannot be less than 0.");
            CheckCounts(project, library, found);

            // A block an exercise stocks is its part, insides and all (Part 15b): its module, and every module only it reaches,
            // is not held to the cells, as their gates are not counted against the stock.
            HashSet<string> inside = StockedInsides(project, library.Counted());
            foreach (Module module in project.Modules)
            {
                if (module.IsWritten || inside.Contains(module.Name))
                    continue;
                foreach (Part part in module.Parts)
                {
                    switch (part)
                    {
                        case GatePart gate:
                            LibraryCell cell = library.CellFor(gate.Kind, gate.Inputs);
                            if (cell == null)
                                Error(module.Name, $"{gate.Name} is a {new LibraryCell(gate.Kind, gate.Inputs, 0, 0).Name}, which the library has no cell for: it has {(library.Cells.Count == 0 ? "none" : library.Listed())}.", gate.Id);
                            else if (gate.Rise != cell.Rise || gate.Fall != cell.Fall)
                                Error(module.Name, $"{gate.Name} is a {cell.Name} at rise {gate.Rise} and fall {gate.Fall}, but the library's {cell.Name} is rise {cell.Rise} and fall {cell.Fall}: a gate keeps its cell's delays.", gate.Id);
                            break;
                        case MuxPart mux:
                            Error(module.Name, $"{mux.Name} is a MUX, which is no cell of the library: build it of the library's gates.", mux.Id);
                            break;
                        case ConstantPart constant when library.NoConstants:
                            Error(module.Name, $"{constant.Name} is a constant, and the library takes none: give the design its 0s and 1s from an input.", constant.Id);
                            break;
                    }
                }
            }

        }

        /// <summary>
        /// The parts an exercise stocks (Part 15b, <see cref="GateLibrary.Counts"/>): a design with more of one than its count
        /// is refused, saying how many there may be -- as 4.x's parts list ran out. The error is on the last of them drawn in
        /// the top, where the student put it, when there is one there.
        /// </summary>
        private static void CheckCounts(Project project, GateLibrary library, List<BoardDiagnostic> found)
        {
            if (library.Counts.Count == 0)
                return;

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (PartCount count in library.Counts)
            {
                if (string.IsNullOrEmpty(count.Part) || count.Most < -1)
                    found.Add(new BoardDiagnostic(DiagnosticSeverity.Error, null, $"The library's count of '{count.Part}' is no count a part can have."));
                else if (!seen.Add(count.Part))
                    found.Add(new BoardDiagnostic(DiagnosticSeverity.Error, null, $"The library counts {count.Said} twice."));
            }

            Dictionary<string, int> has = PartCounts.Of(project, library.Counted());
            Module top = project.TopModule;
            foreach (PartCount count in library.Counts)
            {
                // -1 is any number: stocked, with no cap.
                if (string.IsNullOrEmpty(count.Part) || count.Most < 0 || !has.TryGetValue(count.Part, out int n) || n <= count.Most)
                    continue;

                int last = -1;
                if (top != null && !top.IsWritten)
                    foreach (Part part in top.Parts)
                        if ((part is GatePart gate && PartCount.Of(gate.Kind) == count.Part) || (part is BlockPart block && block.Module == count.Part)
                            || (part is RegisterPart && count.Part == PartCount.Register))
                            last = part.Id;

                string most = count.Most == 0 ? "no " + count.Said : count.Most + " " + count.Said;
                found.Add(new BoardDiagnostic(DiagnosticSeverity.Error, top?.Name, $"The exercise stocks {most}, and the design has {n}.", last));
            }
        }

        /// <summary>
        /// The modules a stocked block's insides are made of (Part 15b): every module reached through a block of a module in
        /// <paramref name="stocked"/>, itself included, and not otherwise from the top. Those are the block's, and are held to
        /// neither the cells nor the counts; a module the top reaches some other way too is the design's, and is.
        /// </summary>
        public static HashSet<string> StockedInsides(Project project, ICollection<string> stocked)
        {
            var inside = new HashSet<string>(StringComparer.Ordinal);
            if (stocked == null || stocked.Count == 0)
                return inside;

            void Reach(string name, HashSet<string> reached, bool stopAtStocked)
            {
                if (!(project.Find(name) is Module module) || !reached.Add(name))
                    return;
                foreach (Part part in module.Parts)
                    if (part is BlockPart block && !string.IsNullOrEmpty(block.Module) && !(stopAtStocked && stocked.Contains(block.Module)))
                        Reach(block.Module, reached, stopAtStocked);
            }

            var design = new HashSet<string>(StringComparer.Ordinal);
            if (project.Top != null && !stocked.Contains(project.Top))
                Reach(project.Top, design, stopAtStocked: true);

            foreach (string block in stocked)
                Reach(block, inside, stopAtStocked: false);
            inside.ExceptWith(design);
            return inside;
        }

        /// <summary>
        /// The project's settings: a time unit SystemVerilog has, since the unit is printed as a
        /// <c>`timescale</c> (Part 9b-3), and a default gate delay a gate could have.
        /// </summary>
        private static void CheckSettings(Project project, List<BoardDiagnostic> found)
        {
            void Error(string message) => found.Add(new BoardDiagnostic(DiagnosticSeverity.Error, null, message));

            ProjectSettings settings = project.Settings;
            if (settings == null)
            {
                Error("The project has no settings.");
                return;
            }

            if (!TimeUnits.IsUnit(settings.TimeUnit))
                Error($"'{settings.TimeUnit}' is not a time unit: use {TimeUnits.Listed}.");
            if (settings.DefaultGateDelay < 0)
                Error("The default gate delay cannot be less than 0.");
        }

        /// <summary>The errors in <paramref name="after"/> that <paramref name="before"/> did not have: what an edit would add.</summary>
        public static List<BoardDiagnostic> NewErrors(List<BoardDiagnostic> before, List<BoardDiagnostic> after)
        {
            var had = new HashSet<string>(StringComparer.Ordinal);
            foreach (BoardDiagnostic d in before)
                if (d.IsError)
                    had.Add(d.Module + "\n" + d.Message);

            var added = new List<BoardDiagnostic>();
            foreach (BoardDiagnostic d in after)
                if (d.IsError && !had.Contains(d.Module + "\n" + d.Message))
                    added.Add(d);
            return added;
        }

        private static void CheckModule(Project project, Module module, List<BoardDiagnostic> found)
        {
            string m = module.Name;
            void Error(string message, int part = -1, int wire = -1) =>
                found.Add(new BoardDiagnostic(DiagnosticSeverity.Error, m, message, part, wire));
            void Warning(string message, int part = -1, int wire = -1) =>
                found.Add(new BoardDiagnostic(DiagnosticSeverity.Warning, m, message, part, wire));

            if (module.IsWritten)
            {
                CheckWritten(project, module, found);
                return;
            }

            var names = new HashSet<string>(StringComparer.Ordinal);
            var ids = new HashSet<int>();

            foreach (Part part in module.Parts)
            {
                if (!ids.Add(part.Id))
                    Error($"Two parts have the id {part.Id}.", part.Id);
                if (part.Id <= 0 || part.Id >= module.NextId)
                    Error($"{part.Name} has the id {part.Id}, which the module never gave out.", part.Id);

                string why = WhyNotAName(part.Name);
                if (why != null)
                    Error(why, part.Id);
                else if (!names.Add(part.Name))
                    Error($"Two parts are called {part.Name}.", part.Id);

                CheckPart(project, part, Error, Warning);
            }

            foreach (Wire wire in module.Wires)
            {
                if (!ids.Add(wire.Id))
                    Error($"Wire {wire.Id} has the same id as something else.", wire: wire.Id);
                if (wire.NetName != null)
                {
                    string why = WhyNotAName(wire.NetName);
                    if (why != null)
                        Error(why, wire: wire.Id);
                    else if (names.Contains(wire.NetName))
                        Error($"{wire.NetName} is already a part's name, so a wire cannot be called it.", wire: wire.Id);
                }
            }

            Netlist netlist = Netlist.Of(project, module);
            var partsById = new Dictionary<int, Part>();
            foreach (Part part in module.Parts)
                partsById[part.Id] = part;
            var wiresById = new Dictionary<int, Wire>();
            foreach (Wire wire in module.Wires)
                wiresById[wire.Id] = wire;
            foreach (string problem in netlist.Problems)
                Error(problem);

            foreach (Net net in netlist.Nets)
            {
                if (!net.WidthsAgree)
                {
                    var widths = new List<string>();
                    foreach (PinRef pin in net.Pins)
                    {
                        Part part = partsById[pin.Part];
                        part.TryPin(project, pin.Pin, out Pin p);
                        widths.Add($"{part.Name}.{pin.Pin} is {Bits(p.Width)}");
                    }

                    Error($"The wire joins pins of different widths: {string.Join(", ", widths)}. " +
                        "Take a bus apart with a splitter, or put bits together with a joiner.", wire: First(net.Wires));
                }

                // An inout is joined to the net it is on (Part 14g), and an input of the module carries a value in only: said
                // so, with the way out, rather than as one more driver of the wire.
                PortPart inputOnAnInout = null;
                if (net.InOuts.Count > 0)
                    foreach (PortPart port in net.Ports)
                        if (port.Direction == PortDirection.Input)
                        {
                            inputOnAnInout = port;
                            break;
                        }

                if (inputOnAnInout != null)
                {
                    PinRef inout = net.InOuts[0];
                    Error($"{inputOnAnInout.Name} is an input, on one wire with {partsById[inout.Part].Name}.{inout.Pin}, an inout: an " +
                        "input carries a value in only. Drive the bus from it through a BUFIF1.", inputOnAnInout.Id);
                }
                else if (net.Drivers.Count > 1 && !IsBus(net, module))
                {
                    var drivers = new List<string>();
                    foreach (PinRef pin in net.Drivers)
                        drivers.Add(partsById[pin.Part].Name + "." + pin.Pin);
                    Error($"{string.Join(" and ", drivers)} both drive one wire; a wire has one driver -- or, as a bus, only " +
                        "tri-state gates, BUFIF and NOTIF, and blocks' inout pins, each letting go while it does not drive.", wire: First(net.Wires));
                }

                // The names its wires were given: the lowest wire's is the one the net asks for.
                string wanted = null;
                int wantedBy = int.MaxValue;
                foreach (int id in net.Wires)
                {
                    string given = wiresById[id].NetName;
                    if (given != null && id < wantedBy)
                    {
                        wanted = given;
                        wantedBy = id;
                    }
                }

                if (wanted != null)
                {
                    if (net.Naming == NetNaming.Port)
                        Warning($"A wire of the port {net.Name} is named {wanted}; a port's wire keeps the port's name.", wire: wantedBy);
                    else if (wanted != net.Name)
                        Error($"Two wires that are not joined are both called {wanted}.", wire: wantedBy);

                    foreach (int id in net.Wires)
                    {
                        string given = wiresById[id].NetName;
                        if (given != null && given != wanted && net.Naming != NetNaming.Port)
                            Warning($"One wire of the net {net.Name} is named {given}; the net keeps {net.Name}.", wire: id);
                    }
                }

                if (net.Drivers.Count == 0 && net.Pins.Count > 0)
                {
                    PinRef first = net.Pins[0];
                    Part part = partsById[first.Part];
                    if (!(part is LampPart))
                        Warning(net.Wires.Count == 0
                            ? $"{part.Name}.{first.Pin} is not connected, so it reads x."
                            : $"Nothing drives {net.Name}, so it reads x.", part.Id);
                }

                // What a gate, a MUX or a register works out and nothing uses (Part 14e, as vlog warns of a signal set
                // and never read). A splitter's spare bit, an input left unused and a block's spare output are the
                // ordinary way of things, and said nothing of. An inout pin is a driver that reads the wire too (Part
                // 14g), so a wire one is on is read.
                if (net.Drivers.Count > 0 && net.Pins.Count == net.Drivers.Count && net.InOuts.Count == 0)
                {
                    PinRef driver = net.Drivers[0];
                    Part part = partsById[driver.Part];
                    if (part is GatePart || part is MuxPart || part is RegisterPart)
                        Warning($"{part.Name}.{driver.Pin} drives nothing, so what it works out is never used.", part.Id);
                }
            }
        }

        /// <summary>
        /// Whether <paramref name="net"/> is a bus (Part 14f): more than one driver, every one that can let go -- z -- while it
        /// does not drive: a tri-state gate's output, or a block's inout pin (Part 14g). Where two drive at once it reads x, as
        /// it does in any simulator, and the waves show it so. A block's output is no driver of a bus, even one a tri-state
        /// gate inside drives: what is inside a block is its own, and only an inout says it lets go.
        /// </summary>
        public static bool IsBus(Net net, Module module)
        {
            if (net == null || module == null || net.Drivers.Count < 2)
                return false;
            foreach (PinRef pin in net.Drivers)
                if (!(module.FindPart(pin.Part) is GatePart gate && gate.IsTriState) && !net.IsInOut(pin))
                    return false;
            return true;
        }

        /// <summary>
        /// Whether <paramref name="net"/> is printed as a <c>tri</c> net: a bus, or a wire an inout pin is on (Part 14g), which
        /// the elaborator joins the inout to -- a net of the module's own, never a variable, which takes one driver.
        /// </summary>
        public static bool IsTri(Net net, Module module) => net != null && (net.InOuts.Count > 0 || IsBus(net, module));

        private static void CheckPart(Project project, Part part, Action<string, int, int> error, Action<string, int, int> warning)
        {
            void Error(string message) => error(message, part.Id, -1);
            void Width(int width, string what)
            {
                if (width < 1 || width > MaxWidth)
                    Error($"{part.Name}'s {what} is {width} bits; it can be 1 to {MaxWidth}.");
            }

            switch (part)
            {
                case PortPart port:
                    Width(port.Width, "width");
                    if (port.Direction == PortDirection.Inout)
                        Error($"{port.Name} is an inout: a drawn port is an input or an output, so far. A written module may have one (Part 14f).");
                    break;

                case GatePart gate:
                    Width(gate.Width, "width");
                    if (gate.IsTriState)
                    {
                        if (gate.Inputs != 2)
                            Error($"{gate.Name} is a {Primitive(gate.Kind)} gate, which has two inputs, the data and the control, not {gate.Inputs}.");
                        if (gate.Width != 1)
                            Error($"{gate.Name} is a {Primitive(gate.Kind)} gate, which is one bit wide.");
                    }
                    else if (gate.Kind == GateKind.Not || gate.Kind == GateKind.Buf)
                    {
                        if (gate.Inputs != 1)
                            Error($"{gate.Name} is a {Primitive(gate.Kind)} gate, which has one input, not {gate.Inputs}.");
                    }
                    else if (gate.Inputs < 2 || gate.Inputs > GatePart.MaxInputs)
                        Error($"{gate.Name} has {gate.Inputs} inputs; {Primitive(gate.Kind)} takes 2 to {GatePart.MaxInputs}.");

                    if (gate.Rise < 0 || gate.Fall < 0)
                        Error($"{gate.Name}'s delay cannot be less than 0.");
                    else if (gate.Width > 1 && gate.Rise != gate.Fall)
                        warning($"{gate.Name} is {Bits(gate.Width)} wide with different rise and fall delays. Which one a vector takes is " +
                                "where simulators part: BitSorter follows the standard, Icarus does not, so the code it exports may run " +
                                "differently there. Equal delays mean the same everywhere.", part.Id, -1);
                    break;

                case ConstantPart constant:
                    Width(constant.Value.Width, "value");
                    break;

                case RegisterPart register:
                    Width(register.Width, "width");
                    if (register.ResetValue is LogicVec reset && reset.Width != register.Width)
                        Error($"{register.Name}'s reset value is {Bits(reset.Width)}; the register is {Bits(register.Width)}.");
                    if (register.Initial is LogicVec initial && initial.Width != register.Width)
                        Error($"{register.Name}'s starting value is {Bits(initial.Width)}; the register is {Bits(register.Width)}.");
                    if (register.Tcq < 0 || register.Tsetup < 0)
                        Error($"{register.Name}'s clock to Q and setup cannot be less than 0.");
                    break;

                case MuxPart mux:
                    Width(mux.Width, "width");
                    if (mux.Inputs != 2 && mux.Inputs != 4 && mux.Inputs != 8)
                        Error($"{mux.Name} has {mux.Inputs} inputs; a multiplexer has 2, 4 or 8.");
                    break;

                case SplitterPart splitter:
                    if (splitter.Width < 2 || splitter.Width > 64)
                        Error($"{splitter.Name} splits {splitter.Width} bits; a splitter takes 2 to 64.");
                    break;

                case JoinerPart joiner:
                    if (joiner.Width < 2 || joiner.Width > 64)
                        Error($"{joiner.Name} joins {joiner.Width} bits; a joiner takes 2 to 64.");
                    break;

                case LampPart lamp:
                    Width(lamp.Width, "width");
                    break;

                case BlockPart block:
                    if (project.Find(block.Module) is Module inner)
                    {
                        // An inout pin is joined to a whole net (Part 14g), and each copy of several would take a slice of one.
                        if (block.Copies > 1)
                            foreach (ModulePort port in WrittenModules.PortsOf(project, inner))
                                if (port.Direction == PortDirection.Inout)
                                {
                                    Error($"{block.Name} is {block.Copies} copies of {block.Module}, whose {port.Name} is an inout: an inout " +
                                        "joins a whole wire, and each copy would take a slice of one. Place the copies one by one.");
                                    break;
                                }
                    }
                    else
                        Error($"{block.Name} is a {block.Module}, and there is no module called that.");
                    if (block.Copies < 1 || block.Copies > BlockPart.MaxCopies)
                        Error($"{block.Name} has {block.Copies} copies; a block can be 1 to {BlockPart.MaxCopies}.");
                    else if (block.Copies > 1)
                        foreach (Pin pin in block.Pins(project))
                            Width(pin.Width, pin.Name + " of its copies side by side");
                    break;
            }
        }

        /// <summary>A written module (Part 12d): nothing drawn in it, and its text one module that elaborates, each fault at its line.</summary>
        private static void CheckWritten(Project project, Module module, List<BoardDiagnostic> found)
        {
            if (module.Parts.Count > 0 || module.Wires.Count > 0)
                found.Add(new BoardDiagnostic(DiagnosticSeverity.Error, module.Name, $"{module.Name} is written, so nothing is drawn in it."));

            // The testbench is the top, whose signals are watched on the waves, not read: what it sets and never reads is
            // what it shows (the 14e review found every output it did not print said).
            bool bench = module.Name == project.Bench;
            foreach (Diagnostic d in WrittenModules.Read(project, module).Diagnostics)
                if (d.Severity != DiagnosticSeverity.Info && !(bench && d.Code == ElaborationCodes.NeverRead))
                    found.Add(new BoardDiagnostic(d.Severity, module.Name, d.Message, line: d.Location.Line, column: d.Location.Column));
        }

        private static void CheckRecursion(Project project, List<BoardDiagnostic> found)
        {
            // Depth-first through what each module instantiates: a module met again on its own path
            // contains itself.
            var state = new Dictionary<string, int>(StringComparer.Ordinal);
            var reported = new HashSet<string>(StringComparer.Ordinal);

            void Visit(Module module, List<string> path)
            {
                state[module.Name] = 1;
                path.Add(module.Name);

                foreach (Module inner in WrittenModules.UsesOf(project, module))
                {
                    int block = -1;
                    foreach (Part part in module.Parts)
                        if (part is BlockPart b && b.Module == inner.Name)
                        {
                            block = part.Id;
                            break;
                        }

                    state.TryGetValue(inner.Name, out int seen);
                    if (seen == 1)
                    {
                        int from = path.IndexOf(inner.Name);
                        string loop = string.Join(" contains ", path.GetRange(from, path.Count - from)) + " contains " + inner.Name;
                        if (reported.Add(loop))
                            found.Add(new BoardDiagnostic(DiagnosticSeverity.Error, module.Name, $"{loop}: a module cannot contain itself.", block));
                    }
                    else if (seen == 0)
                    {
                        Visit(inner, path);
                    }
                }

                path.RemoveAt(path.Count - 1);
                state[module.Name] = 2;
            }

            foreach (Module module in project.Modules)
                if (!state.ContainsKey(module.Name))
                    Visit(module, new List<string>());
        }

        /// <summary>
        /// The written testbench, when there is one (Part 13b): a written module other than the top, which instantiates the
        /// top, while the stimulus drives nothing -- the bench does.
        /// </summary>
        private static void CheckBench(Project project, List<BoardDiagnostic> found)
        {
            if (project.Bench == null)
                return;

            void Error(string message) => found.Add(new BoardDiagnostic(DiagnosticSeverity.Error, project.Bench, message));
            Module bench = project.Find(project.Bench);
            if (bench == null)
            {
                Error($"The testbench, {project.Bench}, is not in the project.");
                return;
            }

            if (!bench.IsWritten)
                Error($"The testbench, {project.Bench}, is drawn: a testbench is written, as the course writes one.");
            else if (project.Bench == project.Top)
                Error($"{project.Bench} is the design and its testbench at once.");
            else
            {
                bool instantiates = false;
                foreach (Module used in WrittenModules.UsesOf(project, bench))
                    instantiates |= used.Name == project.Top;
                if (!instantiates)
                    Error($"The testbench, {project.Bench}, does not instantiate the design, {project.Top}.");
                else if (WrittenModules.Read(project, bench).Declaration is ModuleDeclaration declaration && BoardCompiler.InstanceOf(declaration, project.Top) == null)
                    Error($"The testbench, {project.Bench}, makes its {project.Top} inside a generate if or for: make it outside one, so the run knows which is the design.");
            }

            Stimulus stimulus = project.Stimulus;
            if (stimulus != null && (stimulus.Tracks.Count > 0 || stimulus.Clocks.Count > 0))
                Error(BenchDrivesTheInputs(project));
        }

        /// <summary>Why an input cannot be set while a written testbench drives them (Part 13b).</summary>
        public static string BenchDrivesTheInputs(Project project) =>
            $"The testbench, {project.Bench}, drives the inputs: CONVERT it, in the FILE menu, to set them here, or change them in its code.";

        /// <summary>The project's other files (Part 13b): each a bare name, no two alike, and none the export's own script or a module's file.</summary>
        private static void CheckFiles(Project project, List<BoardDiagnostic> found)
        {
            var printed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { CellLibrary.FileName };
            foreach (Module module in project.Modules)
                printed.Add(WrittenModules.FileOf(module.Name));
            if (project.Bench == null && project.Stimulus != null)
                printed.Add(WrittenModules.FileOf(project.Top + "_tb"));   // the testbench made from the stimulus, as Testbench names it

            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ProjectFile file in project.Files)
            {
                void Error(string message) => found.Add(new BoardDiagnostic(DiagnosticSeverity.Error, null, message));
                if (file == null)
                    continue;
                if (!ProjectFiles.IsValidName(file.Name, out string why))
                    Error(why);
                else if (!names.Add(file.Name))
                    Error($"There are two files called {file.Name}.");
                else if (string.Equals(file.Name, ProjectExport.RunScript, StringComparison.OrdinalIgnoreCase))
                    Error($"{file.Name} is the script an export writes: call the file something else.");
                else if (printed.Contains(file.Name))
                    Error($"{file.Name} is the name a module of the project is written to: call the file something else.");
            }
        }

        private static void CheckStimulus(Project project, List<BoardDiagnostic> found)
        {
            Stimulus stimulus = project.Stimulus;
            Module top = project.TopModule;
            if (stimulus == null || top == null)
                return;

            void Error(string message) => found.Add(new BoardDiagnostic(DiagnosticSeverity.Error, null, message));

            // A drawn top's port parts, or a written one's header (Part 13b).
            ModulePort? Input(string name)
            {
                foreach (ModulePort port in WrittenModules.PortsOf(project, top))
                    if (port.Direction == PortDirection.Input && string.Equals(port.Name, name, StringComparison.Ordinal))
                        return port;
                return null;
            }

            var driven = new HashSet<string>(StringComparer.Ordinal);

            foreach (ClockTrack clock in stimulus.Clocks)
            {
                ModulePort? port = Input(clock.Port);
                if (port == null)
                    Error($"The clock {clock.Port} is not an input of {top.Name}.");
                else if (port.Value.Width != 1)
                    Error($"The clock {clock.Port} is {Bits(port.Value.Width)}; a clock is one bit.");
                if (clock.Period < 2 || clock.Period % 2 != 0)
                    Error($"The clock {clock.Port}'s period is {clock.Period}; it has to be even, and at least 2, so each half is a whole number.");
                if (!driven.Add(clock.Port))
                    Error($"{clock.Port} is given twice.");
            }

            foreach (Track track in stimulus.Tracks)
            {
                ModulePort? port = Input(track.Port);
                if (port == null)
                    Error($"The stimulus drives {track.Port}, which is not an input of {top.Name}.");
                if (!driven.Add(track.Port))
                    Error($"{track.Port} is given twice.");
                if (port != null && track.Start != null && track.Start.Value.Width != port.Value.Width)
                    Error($"{track.Port} is {Bits(port.Value.Width)}, and holds {Bits(track.Start.Value.Width)} from the start.");

                long last = 0;
                foreach (Step step in track.Steps)
                {
                    if (step.Time < last)
                        Error($"{track.Port}'s steps go back in time, to {step.Time} after {last}.");
                    if (port != null && step.Value.Width != port.Value.Width)
                        Error($"{track.Port} is {Bits(port.Value.Width)}, and is given {Bits(step.Value.Width)} at {step.Time}.");
                    last = Math.Max(last, step.Time);
                }
            }

            if (stimulus.End < 0)
                Error("The run cannot end before it starts.");

            // What an output must read (Part 15b): an output of the top, as wide as the value wanted, at a moment of the run.
            foreach (Check check in stimulus.Checks)
            {
                ModulePort? port = null;
                foreach (ModulePort each in WrittenModules.PortsOf(project, top))
                    if (each.Direction != PortDirection.Input && string.Equals(each.Name, check.Port, StringComparison.Ordinal))
                        port = each;
                if (port == null)
                    Error($"The test checks {check.Port}, which is not an output of {top.Name}.");
                else if (check.Expected.Width != port.Value.Width)
                    Error($"{check.Port} is {Bits(port.Value.Width)}, and the test wants {Bits(check.Expected.Width)} of it at {check.Time}.");
                if (check.Time < 0)
                    Error($"The test checks {check.Port} at {check.Time}, before the run starts.");
            }
        }

        private static string Primitive(GateKind kind) => kind.ToString().ToLowerInvariant();

        private static string Bits(int width) => width == 1 ? "1 bit" : width + " bits";

        private static int First(IReadOnlyList<int> wires) => wires.Count > 0 ? wires[0] : -1;
    }
}
