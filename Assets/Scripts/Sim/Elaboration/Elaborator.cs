using System;
using System.Collections.Generic;
using BitSorter.SystemVerilog;
using BitSorter.SystemVerilog.Syntax;

namespace BitSorter.Sim
{
    /// <summary>
    /// Turns a syntax tree into a flattened <see cref="Design"/>: every instance expanded, every
    /// parameter worked out, every generate loop unrolled, every port joined up -- what Part 4b adds,
    /// for the structural subset (Tier A of <c>Docs/architecture-5.md</c>, section 7).
    /// </summary>
    /// <remarks>
    /// **A signal's name is its place in the hierarchy**: <c>alu64.slice[3].s.fa.sum</c>, as Icarus
    /// writes it in a VCD, so the oracle compares the two by name.
    ///
    /// **The hierarchy is kept as well as flattened** (Part 9b): each scope is recorded as it is made --
    /// the top, an instance, a generate block, a named block in a process -- in
    /// <see cref="Design.Scopes"/>, and each signal says which it was declared in, with its declared
    /// range, its enum and its port direction, so the waveform lists and names signals from what the
    /// source said rather than by taking their names apart.
    ///
    /// **A port is a signal of its own, joined by a zero-delay assignment.** An input is a wire in the
    /// child driven from the parent's expression; an output drives the parent's signal from the
    /// child's port. Icarus joins them into one net instead, which takes no delta cycle; the values
    /// settled at every time step are the same, and those are what the oracle compares.
    ///
    /// **The top module's inputs are variables**, undriven, so a stimulus -- and later the
    /// workbench's switches -- can write them. Every other input is a wire, as the standard makes an
    /// <c>input logic</c> port.
    ///
    /// **Processes** (<c>initial</c> and every kind of <c>always</c>) are compiled to instructions
    /// (<c>Elaborator.Processes.cs</c>) and start in the order Icarus starts them: an instance's
    /// processes before its parent's own, and a module's own as written. So a testbench that raises
    /// reset at time 0 is seen by the flip-flops of the design it instantiates.
    ///
    /// What is not simulated yet -- a function, a task, a memory -- is an error naming the part it
    /// arrives in, because simulating the design without it would be simulating something else.
    /// </remarks>
    public static partial class Elaborator
    {
        /// <summary>How deep instances may nest before the design is taken to be runaway.</summary>
        public const int MaxDepth = 64;

        /// <summary>How many times one generate loop may go round.</summary>
        public const int MaxIterations = 65536;

        /// <summary>How many instances a whole design may have.</summary>
        public const int MaxInstances = 200000;

        /// <summary>
        /// How many bits a whole design may hold, every signal and every memory's words together: 64 million, which a run
        /// keeps in a few times 16 MB, where the course's processor holds under a hundred thousand. Each piece is bounded
        /// alone, a signal by <see cref="LogicVec.MaxWidth"/> and a memory by its words, but not the whole, which a run
        /// allocates the moment it starts (Part 13f).
        /// </summary>
        public const long MaxBits = 1L << 26;

        /// <summary>
        /// How many signals a whole design may have, each word of a memory one of them: 262,144, where the course's
        /// processor has two memories of 1,024 words. A signal costs its name and its record whatever its width -- hundreds
        /// of bytes, measured, and a run keeps more of each -- so a memory of a million one-bit words, inside
        /// <see cref="MaxBits"/>, still asked for most of a gigabyte (Part 13f review).
        /// </summary>
        public const long MaxSignals = 1L << 18;

        /// <summary>
        /// Elaborates <paramref name="tree"/> from <paramref name="top"/>, or from the one module that
        /// nothing instantiates when no top is named.
        /// </summary>
        public static Elaboration Elaborate(SyntaxTree tree, string top = null)
        {
            if (tree == null) throw new ArgumentNullException(nameof(tree));
            return new Run(tree, null).Elaborate(top);
        }

        /// <summary>
        /// <paramref name="tree"/> elaborated with <paramref name="top"/> as the top, the modules <paramref name="rules"/> names
        /// held to its gate library as they are built (Part 14g): each fault an error at its line.
        /// </summary>
        public static Elaboration Elaborate(SyntaxTree tree, string top, GateLevelRules rules)
        {
            if (tree == null) throw new ArgumentNullException(nameof(tree));
            return new Run(tree, rules).Elaborate(top);
        }

        private readonly struct Shape
        {
            public readonly int Width;
            public readonly int Msb;
            public readonly int Lsb;
            public readonly bool Signed;

            /// <summary>The enum, when the type is one, in place or through a typedef: what a signal of it carries.</summary>
            public readonly EnumInfo Enum;

            /// <summary>Whether the type holds only 0 and 1: <c>bit</c>, <c>byte</c>, <c>shortint</c>, <c>int</c>, <c>longint</c>.</summary>
            public readonly bool TwoState;

            /// <summary>
            /// What its bits are: a vector, a packed array, a struct (Part L4). For a packed array or a struct,
            /// <see cref="Msb"/> and <see cref="Lsb"/> are its bits as one flat vector, <c>[Width-1:0]</c>, which
            /// is how a waveform and the engine see it; the dimensions themselves are here.
            /// </summary>
            public readonly PackedType Type;

            public Shape(int msb, int lsb, bool signed, EnumInfo @enum = null, bool twoState = false, PackedType type = null)
            {
                Msb = msb;
                Lsb = lsb;
                Width = Math.Abs(msb - lsb) + 1;
                Signed = signed;
                Enum = @enum;
                TwoState = twoState;
                Type = type ?? PackedType.Bits(msb, lsb, signed, twoState, @enum);
            }

            /// <summary>A packed array's or a struct's shape: flat bits, with the type that maps them.</summary>
            public static Shape Of(PackedType type) =>
                new Shape(type.Width - 1, 0, type.Signed, type.Enum, type.TwoState, type);
        }

        private sealed partial class Run
        {
            private readonly SyntaxTree _tree;
            private readonly DiagnosticBag _diagnostics = new DiagnosticBag();
            private readonly Dictionary<string, ModuleDeclaration> _modules = new Dictionary<string, ModuleDeclaration>(StringComparer.Ordinal);

            /// <summary>The interfaces (Part L8), by name, which an instantiation may make as it makes a module.</summary>
            private readonly Dictionary<string, ModuleDeclaration> _interfaces = new Dictionary<string, ModuleDeclaration>(StringComparer.Ordinal);

            /// <summary>The net each inout port is joined to, by the port's full name (Part 14f, <see cref="JoinInouts"/>).</summary>
            private readonly Dictionary<string, SignalEntry> _joined = new Dictionary<string, SignalEntry>(StringComparer.Ordinal);

            /// <summary>The interfaces whose modports have been checked, so a fault in one is said once, not once an instance.</summary>
            private readonly HashSet<ModuleDeclaration> _modportsChecked = new HashSet<ModuleDeclaration>();
            private readonly HashSet<string> _active = new HashSet<string>(StringComparer.Ordinal);
            /// <summary>
            /// Every enum whose members are declared, by the scope they are declared in, with what a signal
            /// of it carries: one type may be met twice in a scope (a non-ANSI port's, given again), and
            /// both uses are the same enum. Null where a member's value could not be worked out.
            /// </summary>
            private readonly Dictionary<(Scope, EnumType), EnumInfo> _enumsDeclared = new Dictionary<(Scope, EnumType), EnumInfo>();
            private NetlistBuilder _builder;
            private Scope _unit;
            private int _instances;
            private long _bits;
            private long _signals;
            private int _unnamed;
            private bool _stopped;

            /// <summary>
            /// What waits for the instance being elaborated to be complete: its processes, and the
            /// assigns that name something through the hierarchy, which may reach into an instance
            /// written after them. Done in the order written, once everything else is in.
            /// </summary>
            private List<Action> _deferred;

            /// <summary>The processes of the instance being elaborated, and of the instances inside it, kept apart to be ordered.</summary>
            private List<ProcessInfo> _ownProcesses;
            private List<ProcessInfo> _childProcesses;
            private readonly List<ProcessInfo> _allProcesses = new List<ProcessInfo>();
            private readonly Dictionary<ProcessInfo, SourceLocation> _processAt = new Dictionary<ProcessInfo, SourceLocation>();

            /// <summary>The gate library written code is held to as it is built (Part 14g), or null for none.</summary>
            private readonly GateLevelRules _rules;

            public Run(SyntaxTree tree, GateLevelRules rules)
            {
                _tree = tree;
                _rules = rules;
            }

            public Elaboration Elaborate(string top)
            {
                var diagnostics = new List<Diagnostic>(_tree.Diagnostics);

                if (_tree.HasErrors)
                    return new Elaboration(null, top, diagnostics, new List<ElaboratedPort>());

                foreach (ModuleDeclaration module in _tree.Modules)
                {
                    if (_modules.ContainsKey(module.Name))
                        Error(module.Location, ElaborationCodes.Duplicate, $"There are two modules called {module.Name}; rename one.");
                    else
                        _modules[module.Name] = module;
                }

                foreach (ModuleDeclaration declared in _tree.Interfaces)
                {
                    if (_interfaces.ContainsKey(declared.Name) || _modules.ContainsKey(declared.Name))
                        Error(declared.Location, ElaborationCodes.Duplicate, $"There are two modules or interfaces called {declared.Name}; rename one.");
                    else
                        _interfaces[declared.Name] = declared;
                }

                ModuleDeclaration root = ChooseTop(top);
                var ports = new List<ElaboratedPort>();
                Design design = null;

                if (root != null)
                {
                    _unit = new Scope(root.Name, null, null);
                    _unit.TimeSource = () => Expr.Read(_builder.Time(), 64);
                    _unit.Packages = FindPackage;
                    FindTicks();
                    Timescale(_unit, _defaultTimescale);
                    DeclareUnitItems();

                    _builder = new NetlistBuilder(root.Name);
                    Scope scope = ElaborateModule(root, root.Name, new Dictionary<string, Expression>(), null, root.Location, isTop: true, depth: 0, within: null);

                    // What each disable names, now that every process it may reach is compiled.
                    ResolveDisables();

                    // Last, so every continuous driver is in when the builder checks what each writes.
                    foreach (ProcessInfo process in _allProcesses)
                    {
                        try
                        {
                            _builder.Process(process);
                        }
                        catch (InvalidOperationException ex)
                        {
                            Error(_processAt[process], ElaborationCodes.Drivers, ex.Message);
                        }
                    }

                    if (scope != null)
                    {
                        foreach (string name in PortOrder(root))
                        {
                            if (scope.Own(name) is SignalEntry s && s.Port.HasValue)
                                ports.Add(new ElaboratedPort(name, s.Port.Value, s.Signal, s.Width, s.Signed));
                        }
                    }

                    if (!_diagnostics.HasErrors)
                    {
                        design = _builder.Build();
                        LintDesign(design);
                    }
                }

                diagnostics.AddRange(_diagnostics.Items);
                return new Elaboration(design, root?.Name ?? top, diagnostics, ports);
            }

            // -----------------------------------------------------------------
            // Diagnostics
            // -----------------------------------------------------------------

            private void Error(SourceLocation at, string code, string message) => _diagnostics.Error(at, code, message);

            /// <summary>
            /// Whether <paramref name="bits"/> more, in <paramref name="signals"/> more signals, for <paramref name="name"/>,
            /// keep the design within <see cref="MaxBits"/> and <see cref="MaxSignals"/>. Past either the design is refused,
            /// said once, and the elaboration stops, as it does past <see cref="MaxInstances"/>.
            /// </summary>
            private bool Fits(long bits, long signals, SourceLocation at, string name)
            {
                if (_stopped)
                    return false;
                _bits += bits;
                _signals += signals;
                if (_bits <= MaxBits && _signals <= MaxSignals)
                    return true;

                Error(at, ElaborationCodes.TooLarge, _bits > MaxBits
                    ? $"With {name}, the design holds more than {MaxBits >> 20} million bits, more than BitSorter will simulate. " +
                      "Is a width, or a memory's number of words, larger than meant?"
                    : $"With {name}, the design has more than {MaxSignals.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)} signals and memory words, " +
                      "more than BitSorter will simulate. Is a memory's number of words larger than meant?");
                _stopped = true;
                return false;
            }
            private void Warning(SourceLocation at, string code, string message) => _diagnostics.Warning(at, code, message);

            private void NotYet(SourceLocation at, string message) => Error(at, ElaborationCodes.NotYet, message);

            // -----------------------------------------------------------------
            // The top
            // -----------------------------------------------------------------

            /// <summary>
            /// The named module, or the one nothing instantiates. A file that includes a cell library
            /// leaves the unused cells uninstantiated too, so among several such modules the one that
            /// instantiates something is the top, as a testbench is.
            /// </summary>
            private ModuleDeclaration ChooseTop(string top)
            {
                if (top != null)
                {
                    if (_modules.TryGetValue(top, out ModuleDeclaration named))
                        return named;

                    if (_interfaces.ContainsKey(top))
                    {
                        Error(default, ElaborationCodes.NoTop, $"{top} is an interface, which only a module instantiates; start from a module that makes one.");
                        return null;
                    }

                    Error(default, ElaborationCodes.UnknownModule, $"There is no module called {top} to start from.");
                    return null;
                }

                if (_modules.Count == 0)
                {
                    Error(default, ElaborationCodes.NoTop, "There is no module to simulate.");
                    return null;
                }

                var instantiated = new HashSet<string>(StringComparer.Ordinal);
                var parents = new HashSet<string>(StringComparer.Ordinal);

                foreach (ModuleDeclaration module in _tree.Modules)
                {
                    foreach (string child in Instantiated(module.Items))
                    {
                        instantiated.Add(child);
                        parents.Add(module.Name);
                    }
                }

                var candidates = new List<ModuleDeclaration>();
                foreach (ModuleDeclaration module in _tree.Modules)
                    if (!instantiated.Contains(module.Name) && _modules[module.Name] == module)
                        candidates.Add(module);

                if (candidates.Count > 1)
                {
                    var withChildren = candidates.FindAll(m => parents.Contains(m.Name));
                    if (withChildren.Count >= 1)
                        candidates = withChildren;
                }

                if (candidates.Count == 1)
                    return candidates[0];

                if (candidates.Count == 0)
                {
                    Error(_tree.Modules[0].Location, ElaborationCodes.NoTop,
                        "Every module is instantiated by another, so none of them is the top. Is one instantiating itself?");
                    return null;
                }

                Error(candidates[1].Location, ElaborationCodes.NoTop,
                    $"Any of {string.Join(", ", candidates.ConvertAll(m => m.Name))} could be the top, since nothing instantiates them; say which to start from.");
                return null;
            }

            private static IEnumerable<string> Instantiated(List<ModuleItem> items)
            {
                foreach (ModuleItem item in items)
                {
                    switch (item)
                    {
                        case ModuleInstantiation mi:
                            yield return mi.Module;
                            break;
                        case GenerateRegion region:
                            foreach (string name in Instantiated(region.Items)) yield return name;
                            break;
                        case GenerateBlock block:
                            foreach (string name in Instantiated(block.Items)) yield return name;
                            break;
                        case LoopGenerate loop when loop.Body != null:
                            foreach (string name in Instantiated(loop.Body.Items)) yield return name;
                            break;
                        case IfGenerate g:
                            if (g.Then != null) foreach (string name in Instantiated(g.Then.Items)) yield return name;
                            if (g.Else != null) foreach (string name in Instantiated(g.Else.Items)) yield return name;
                            break;
                    }
                }
            }

            /// <summary>Typedefs and parameters written outside every module, which every module sees.</summary>
            private void DeclareUnitItems()
            {
                foreach (ModuleItem item in _tree.UnitItems)
                {
                    switch (item)
                    {
                        case ParameterDeclaration p:
                            DeclareParameter(_unit, p, null, null);
                            break;
                        case TypedefDeclaration t:
                            DeclareTypedef(_unit, t);
                            break;
                        case SubroutineDeclaration s:
                            DeclareSubroutine(_unit, s);
                            break;
                        case ImportDeclaration import:
                            Import(_unit, import);
                            break;
                        case UnsupportedItem _:
                            break;
                        default:
                            NotYet(item.Location, "Only typedefs and parameters can be declared outside a module in BitSorter.");
                            break;
                    }
                }
            }

            // -----------------------------------------------------------------
            // Time (Part L7)
            // -----------------------------------------------------------------

            /// <summary>A tick, as a power of ten of a second: the finest time any module's <c>`timescale</c> names; null for none.</summary>
            private int? _tick;

            /// <summary>The <c>`timescale</c> a module, package or the compilation unit without one counts in: the first one written.</summary>
            private string _defaultTimescale;

            /// <summary>
            /// The tick (Part L7): the finest unit or precision of every module's <c>`timescale</c>, so each module's
            /// delays are whole ticks once scaled and rounded to its own precision (IEEE 1800, 22.7). With none, a tick
            /// is the one unit everything counts in, as it always was.
            /// </summary>
            private void FindTicks()
            {
                var declared = new List<ModuleDeclaration>(_tree.Modules);
                declared.AddRange(_tree.Interfaces);
                foreach (ModuleDeclaration module in declared)
                {
                    if (module.Timescale == null)
                        continue;

                    if (!TimeUnits.TryTimescale(module.Timescale, out int unit, out int precision))
                    {
                        Warning(module.Location, ElaborationCodes.Ignored,
                            $"`timescale {module.Timescale} is not a unit and a precision BitSorter reads -- 1ns/1ps, say, the precision no coarser than the unit -- so module {module.Name} counts in whole ticks.");
                        continue;
                    }

                    _defaultTimescale = _defaultTimescale ?? module.Timescale;
                    _tick = Math.Min(_tick ?? int.MaxValue, Math.Min(unit, precision));
                }
            }

            /// <summary>Gives a scope the ticks of its timescale's unit and precision.</summary>
            private void Timescale(Scope scope, string timescale)
            {
                if (_tick == null || !TimeUnits.TryTimescale(timescale, out int unit, out int precision))
                    return;

                scope.TickScale = Pow10(unit - _tick.Value);
                scope.TickRounding = Pow10(precision - _tick.Value);
            }

            private static long Pow10(int n)
            {
                long result = 1;
                for (int i = 0; i < n; i++)
                    result *= 10;
                return result;
            }

            // -----------------------------------------------------------------
            // Packages (Part L6)
            // -----------------------------------------------------------------

            private Dictionary<string, PackageDeclaration> _packages;
            private readonly Dictionary<string, Scope> _packageScopes = new Dictionary<string, Scope>(StringComparer.Ordinal);
            private readonly HashSet<string> _packagesBuilding = new HashSet<string>(StringComparer.Ordinal);

            /// <summary>
            /// A package's scope (IEEE 1800, 26.2): its parameters, typedefs -- an enum's names among them --
            /// functions and tasks, declared the first time anything imports from it or names into it. A
            /// package sees what it imports itself, and not the compilation unit. Null when there is no
            /// package of that name, or it imports from itself.
            /// </summary>
            private Scope FindPackage(string name)
            {
                if (_packageScopes.TryGetValue(name, out Scope done))
                    return done;

                if (_packages == null)
                {
                    _packages = new Dictionary<string, PackageDeclaration>(StringComparer.Ordinal);
                    foreach (PackageDeclaration declared in _tree.Packages)
                    {
                        if (_packages.ContainsKey(declared.Name))
                            Error(declared.Location, ElaborationCodes.Duplicate, $"There are two packages called {declared.Name}.");
                        else
                            _packages[declared.Name] = declared;
                    }
                }

                if (!_packages.TryGetValue(name, out PackageDeclaration package))
                    return null;

                if (!_packagesBuilding.Add(name))
                {
                    Error(package.Location, ElaborationCodes.Recursion, $"Package {name} imports from itself, through the packages it imports.");
                    return null;
                }

                var scope = new Scope(name, null, null) { TimeSource = _unit.TimeSource, Packages = FindPackage };
                Timescale(scope, _defaultTimescale);
                foreach (ModuleItem item in package.Items)
                {
                    switch (item)
                    {
                        case ImportDeclaration import:
                            Import(scope, import);
                            break;
                        case ParameterDeclaration p:
                            DeclareParameter(scope, p, null, null);
                            break;
                        case TypedefDeclaration t:
                            DeclareTypedef(scope, t);
                            break;
                        case SubroutineDeclaration s:
                            DeclareSubroutine(scope, s);
                            break;
                        case UnsupportedItem _:
                            break;
                        default:
                            NotYet(item.Location, $"Package {name} declares something BitSorter keeps in a package only as a parameter, a typedef, a function or a task. Declare it in the module that uses it.");
                            break;
                    }
                }

                _packagesBuilding.Remove(name);
                _packageScopes[name] = scope;
                return scope;
            }

            /// <summary>
            /// <c>import p::*;</c> makes every name of p visible here, after this scope's own; <c>import p::name;</c>
            /// declares that one name here (IEEE 1800, 26.3).
            /// </summary>
            private void Import(Scope scope, ImportDeclaration import)
            {
                foreach (ImportItem item in import.Items)
                {
                    Scope package = FindPackage(item.Package);
                    if (package == null)
                    {
                        if (!_packagesBuilding.Contains(item.Package))
                            Error(item.Location, ElaborationCodes.UnknownName,
                                $"There is no package called {item.Package} to import from. Is its file read, with the package written before what imports it?");
                        continue;
                    }

                    if (item.Name == null)
                    {
                        if (scope.Imports == null)
                            scope.Imports = new List<Scope>();
                        if (!scope.Imports.Contains(package))
                            scope.Imports.Add(package);
                        continue;
                    }

                    Entry entry = package.Own(item.Name);
                    if (entry == null)
                    {
                        Error(item.Location, ElaborationCodes.UnknownName, $"Package {item.Package} has nothing called {item.Name} to import.");
                        continue;
                    }

                    if (!scope.TryDeclare(item.Name, entry) && scope.Own(item.Name) != entry)
                        Error(item.Location, ElaborationCodes.Duplicate, $"{item.Name} is declared here already, so it cannot be imported from {item.Package} too.");
                }
            }

            // -----------------------------------------------------------------
            // A module, as one instance of it
            // -----------------------------------------------------------------

            private static List<string> PortOrder(ModuleDeclaration module)
            {
                if (module.Ports.Count > 0)
                    return module.Ports.ConvertAll(p => p.Name);
                return new List<string>(module.PortNames);
            }

            /// <summary>
            /// One instance of <paramref name="module"/> at <paramref name="path"/>, made in
            /// <paramref name="within"/> (null for the top): its parameters (with
            /// <paramref name="overrides"/>, worked out in <paramref name="overrideScope"/>), its signals,
            /// and everything inside. The instance's scope, or null when it could not be made.
            /// </summary>
            /// <remarks>
            /// <paramref name="within"/> is its parent in the hierarchy, which a scope's own parent is not:
            /// names inside a module are never looked up in the instance around it, so an instance's scope
            /// leads up to the compilation unit instead.
            /// </remarks>
            private Scope ElaborateModule(ModuleDeclaration module, string path, Dictionary<string, Expression> overrides,
                Scope overrideScope, SourceLocation at, bool isTop, int depth, Scope within, Dictionary<string, InstanceEntry> bindings = null)
            {
                if (_stopped)
                    return null;

                if (_active.Contains(module.Name))
                {
                    Error(at, ElaborationCodes.Recursion, $"{module.Name} instantiates itself, through {path}; that would never end.");
                    return null;
                }

                if (depth > MaxDepth)
                {
                    Error(at, ElaborationCodes.Recursion, $"Instances nest more than {MaxDepth} deep at {path}.");
                    return null;
                }

                if (++_instances > MaxInstances)
                {
                    Error(at, ElaborationCodes.TooLarge, $"The design has more than {MaxInstances} instances, more than BitSorter will simulate.");
                    _stopped = true;
                    return null;
                }

                _active.Add(module.Name);
                List<ProcessInfo> outerOwn = _ownProcesses, outerChildren = _childProcesses;
                List<Action> outerDeferred = _deferred;
                _ownProcesses = new List<ProcessInfo>();
                _childProcesses = new List<ProcessInfo>();
                _deferred = new List<Action>();

                try
                {
                    var scope = new Scope(path, _unit, module);
                    Timescale(scope, module.Timescale ?? _defaultTimescale);
                    if (isTop)
                        _unit.Top = scope;

                    // Recorded before anything inside it, so every scope comes after its parent.
                    scope.Index = _builder.Scope(path, module.Name, isTop ? ScopeKind.Top : ScopeKind.Instance, isTop ? -1 : within.Recorded);

                    // What the header imports, which its parameters and ports may use (Part L6).
                    foreach (ImportDeclaration import in module.HeaderImports)
                        Import(scope, import);

                    // Header parameters first, in order: each may use the ones before it.
                    var used = new HashSet<string>(StringComparer.Ordinal);
                    foreach (ParameterDeclaration p in module.HeaderParameters)
                        DeclareParameter(scope, p, p.IsLocal ? null : Override(p, overrides, used), overrideScope);

                    // Then the ANSI ports, whose widths may use only those (IEEE 1800, 23.2.2.2).
                    foreach (PortDeclaration port in module.Ports)
                        DeclareAnsiPort(scope, port, isTop, module, bindings);

                    // Then the body's parameters and typedefs, which a non-ANSI port's width may use.
                    foreach (ModuleItem item in Flatten(module.Items))
                    {
                        if (item is ParameterDeclaration p)
                        {
                            // With a #( ) list in the header, a body parameter is a localparam (IEEE 1800, 6.20.1).
                            bool overridable = !p.IsLocal && !module.HasParameterList;
                            DeclareParameter(scope, p, overridable ? Override(p, overrides, used) : null, overrideScope);
                        }
                        else if (item is TypedefDeclaration t)
                        {
                            DeclareTypedef(scope, t);
                        }
                        else if (item is ImportDeclaration import)
                        {
                            Import(scope, import);
                        }
                    }

                    foreach (KeyValuePair<string, Expression> o in overrides)
                    {
                        if (!used.Contains(o.Key))
                            Error(o.Value?.Location ?? at, ElaborationCodes.BadParameter, IsLocalParameter(module, o.Key)
                                ? $"{o.Key} is a localparam of {module.Name}, so an instance cannot set it."
                                : $"{module.Name} has no parameter called {o.Key}.");
                    }

                    // Every other declaration, then everything that drives something.
                    DeclarePass(scope, module.Items, module, isTop);

                    foreach (string name in module.PortNames)
                    {
                        if (!(scope.Own(name) is SignalEntry s && s.Port.HasValue))
                            Error(module.Location, ElaborationCodes.BadPort,
                                $"Port {name} of {module.Name} is in its port list but never declared input or output.");
                    }

                    BuildPass(scope, module.Items, depth);

                    foreach (Action later in _deferred)
                        later();

                    return scope;
                }
                finally
                {
                    _active.Remove(module.Name);
                    _deferred = outerDeferred;

                    // An instance's processes start before its parent's own, as Icarus starts them.
                    List<ProcessInfo> ordered = _childProcesses;
                    ordered.AddRange(_ownProcesses);
                    _ownProcesses = outerOwn;
                    _childProcesses = outerChildren;
                    (outerChildren ?? _allProcesses).AddRange(ordered);
                }
            }

            /// <summary>The module's items with every <c>generate ... endgenerate</c> region opened up.</summary>
            private static IEnumerable<ModuleItem> Flatten(List<ModuleItem> items)
            {
                foreach (ModuleItem item in items)
                {
                    if (item is GenerateRegion region)
                    {
                        foreach (ModuleItem inner in Flatten(region.Items))
                            yield return inner;
                    }
                    else
                    {
                        yield return item;
                    }
                }
            }

            private static bool IsLocalParameter(ModuleDeclaration module, string name)
            {
                foreach (ParameterDeclaration p in module.HeaderParameters)
                    if (p.Name == name)
                        return p.IsLocal;
                foreach (ModuleItem item in Flatten(module.Items))
                    if (item is ParameterDeclaration p && p.Name == name)
                        return p.IsLocal || module.HasParameterList;
                return false;
            }

            private static Expression Override(ParameterDeclaration p, Dictionary<string, Expression> overrides, HashSet<string> used)
            {
                if (overrides != null && overrides.TryGetValue(p.Name, out Expression value))
                {
                    used.Add(p.Name);
                    return value;
                }

                return null;
            }

            /// <summary>An instantiation's <c>#( )</c>, by name: positional values matched to the parameters an instance may set.</summary>
            private Dictionary<string, Expression> Overrides(ModuleDeclaration module, ModuleInstantiation mi)
            {
                var result = new Dictionary<string, Expression>(StringComparer.Ordinal);
                var settable = new List<string>();

                foreach (ParameterDeclaration p in module.HeaderParameters)
                    if (!p.IsLocal)
                        settable.Add(p.Name);

                if (!module.HasParameterList)
                    foreach (ModuleItem item in Flatten(module.Items))
                        if (item is ParameterDeclaration p && !p.IsLocal)
                            settable.Add(p.Name);

                int position = 0;
                bool named = false, positional = false;

                foreach (ParameterValue value in mi.Parameters)
                {
                    if (value.Name == null)
                    {
                        positional = true;
                        if (position >= settable.Count)
                        {
                            Error(value.Location, ElaborationCodes.BadParameter,
                                $"{module.Name} has {settable.Count} parameter{(settable.Count == 1 ? "" : "s")} an instance can set, and this is value {position + 1}.");
                            continue;
                        }

                        result[settable[position++]] = value.Value;
                    }
                    else
                    {
                        named = true;
                        if (result.ContainsKey(value.Name))
                            Error(value.Location, ElaborationCodes.Duplicate, $"{value.Name} is given twice.");
                        else if (value.Value != null)
                            result[value.Name] = value.Value;
                    }
                }

                if (named && positional)
                    Error(mi.Location, ElaborationCodes.BadParameter, "Give the parameters all by name or all in order, not some of each.");

                // A positional value left empty keeps the default.
                var drop = new List<string>();
                foreach (KeyValuePair<string, Expression> kv in result)
                    if (kv.Value == null)
                        drop.Add(kv.Key);
                foreach (string key in drop)
                    result.Remove(key);

                return result;
            }

            // -----------------------------------------------------------------
            // Parameters and types
            // -----------------------------------------------------------------

            private void DeclareParameter(Scope scope, ParameterDeclaration p, Expression overrideValue, Scope overrideScope)
            {
                // A parameter with a type takes it; one with none keeps the value's own (IEEE 1800, 6.20.2).
                // The type comes first: it is the context the value is sized in, so '1 fills it.
                DataType type = p.Type;
                bool typed = type != null && !(type.IsImplicit && type.Packed.Count == 0);
                Shape shape = default;
                if (typed && !TryShape(type, scope, out shape))
                    return;

                LogicVec value;

                // A real, by its type -- or, with none, by its value (Part L7).
                Expression given = overrideValue ?? p.Value;
                bool real = typed ? shape.Type.IsReal : given != null && ExpressionBuilder.IsReal(given, overrideValue != null ? overrideScope ?? scope : scope);
                PackedType target = typed ? shape.Type : real ? PackedType.Real : null;

                if (overrideValue != null)
                {
                    if (!ExpressionBuilder.TryConstantFor(overrideValue, target, overrideScope ?? scope, _diagnostics, typed ? shape.Width : 0, out value))
                        return;
                }
                else if (p.Value != null)
                {
                    if (!ExpressionBuilder.TryConstantFor(p.Value, target, scope, _diagnostics, typed ? shape.Width : 0, out value))
                        return;
                }
                else
                {
                    Error(p.Location, ElaborationCodes.BadParameter, $"Parameter {p.Name} has no value: give it a default, or set it at every instance.");
                    return;
                }

                if (typed)
                {
                    value = value.Extend(shape.Width, value.IsSigned);
                    value = shape.Signed ? value.AsSigned() : value.AsUnsigned();
                }
                else if (type != null && type.Signed)
                {
                    value = value.AsSigned();
                }

                if (!scope.TryDeclare(p.Name, new ConstantEntry { Value = value, IsReal = real, Declared = p.Location }))
                    Error(p.Location, ElaborationCodes.Duplicate, $"{p.Name} is declared twice here.");
            }

            private void DeclareTypedef(Scope scope, TypedefDeclaration t)
            {
                if (!TryShape(t.Type, scope, out Shape shape, t.Name))
                    return;

                if (!scope.TryDeclare(t.Name, new TypeEntry { Width = shape.Width, Signed = shape.Signed, Msb = shape.Msb, Lsb = shape.Lsb, Enum = shape.Enum, TwoState = shape.TwoState, Type = shape.Type, Declared = t.Location }))
                    Error(t.Location, ElaborationCodes.Duplicate, $"{t.Name} is declared twice here.");
            }

            /// <summary>
            /// The width, range and signedness a type gives: <c>logic [7:0]</c>, <c>int</c>, a typedef's
            /// name, an enum (whose names it declares as constants in <paramref name="scope"/>, and which
            /// the shape carries). <paramref name="typedefName"/> is the name a typedef gives the type, which
            /// an enum declared by it is known by.
            /// </summary>
            private bool TryShape(DataType type, Scope scope, out Shape shape, string typedefName = null)
            {
                shape = new Shape(0, 0, false);

                if (type == null)
                    return true;

                if (type.Enum != null)
                    return TryEnum(type.Enum, scope, typedefName, out shape);

                if (type.Struct != null)
                {
                    // A struct, and maybe a packed array of them: struct packed {...} [1:0] pair.
                    if (!TryStruct(type.Struct, scope, out PackedType layout) ||
                        !TryDimensions(type.Packed, scope, layout, false, layout.TwoState, null, out PackedType whole))
                        return false;

                    shape = Shape.Of(whole);
                    return true;
                }

                bool signed = type.Signed;
                int baseWidth = 1;
                bool integerType = false;

                if (type.Name != null)
                {
                    Entry found = type.Package != null ? scope.Package(type.Package)?.Own(type.Name) : scope.Lookup(type.Name);
                    if (!(found is TypeEntry named))
                    {
                        Error(type.Location, ElaborationCodes.UnknownName, type.Package != null
                            ? $"There is no type called {type.Name} in package {type.Package}."
                            : $"There is no type called {type.Name} here. Is its typedef above, or its package imported?");
                        return false;
                    }

                    if (type.Packed.Count > 0)
                    {
                        // A packed array of the type: instr_t [3:0] program.
                        if (!TryDimensions(type.Packed, scope, named.Type, type.Signed, named.TwoState, null, out PackedType array))
                            return false;

                        shape = Shape.Of(array);
                        return true;
                    }

                    if (named.Type != null && (named.Type.Struct != null || named.Type.Element != null))
                    {
                        shape = Shape.Of(named.Type);
                        return true;
                    }

                    shape = new Shape(named.Msb, named.Lsb, type.Unsigned ? false : named.Signed || type.Signed, named.Enum, named.TwoState);
                    return true;
                }

                // bit, byte, shortint, int and longint hold only 0 and 1; logic, reg, integer and time are
                // 4-state (IEEE 1800, 6.11).
                bool twoState = false;
                switch (type.Keyword)
                {
                    case null:
                    case "logic":
                    case "reg":
                        break;
                    case "bit":
                        twoState = true;
                        break;
                    case "int":
                        baseWidth = 32; signed = !type.Unsigned; integerType = true; twoState = true;
                        break;
                    case "integer":
                        baseWidth = 32; signed = !type.Unsigned; integerType = true;
                        break;
                    case "shortint":
                        baseWidth = 16; signed = !type.Unsigned; integerType = true; twoState = true;
                        break;
                    case "longint":
                        baseWidth = 64; signed = !type.Unsigned; integerType = true; twoState = true;
                        break;
                    case "byte":
                        baseWidth = 8; signed = !type.Unsigned; integerType = true; twoState = true;
                        break;
                    case "time":
                        baseWidth = 64; signed = type.Signed; integerType = true;
                        break;
                    case "string":
                        // A string (Part L6): its characters at the bottom of a fixed number of bytes.
                        if (type.Packed.Count > 0)
                        {
                            Error(type.Packed[0].Location, ElaborationCodes.BadSelect, "A string has its own length and takes no range.");
                            return false;
                        }

                        shape = Shape.Of(PackedType.String);
                        return true;
                    case "real":
                    case "realtime":
                    case "shortreal":
                        // A real (Part L7): a double's 64 bits. A shortreal is held as one too.
                        if (type.Packed.Count > 0)
                        {
                            Error(type.Packed[0].Location, ElaborationCodes.BadSelect, $"A {type.Keyword} takes no range.");
                            return false;
                        }

                        shape = Shape.Of(PackedType.Real);
                        return true;
                    default:
                        NotYet(type.Location, $"A {type.Keyword} is not supported by BitSorter yet.");
                        return false;
                }

                if (integerType)
                {
                    if (type.Packed.Count > 0)
                    {
                        Error(type.Packed[0].Location, ElaborationCodes.BadSelect, $"An {type.Keyword} has its own width and takes no range.");
                        return false;
                    }

                    shape = new Shape(baseWidth - 1, 0, signed, null, twoState);
                    return true;
                }

                if (type.Packed.Count == 0)
                {
                    shape = new Shape(0, 0, signed, null, twoState);
                    return true;
                }

                if (type.Packed.Count > 1)
                {
                    // A packed array: logic [3:0][7:0] is four bytes, the first range the outermost.
                    if (!TryDimensions(type.Packed, scope, null, signed, twoState, null, out PackedType array))
                        return false;

                    shape = Shape.Of(array);
                    return true;
                }

                Dimension d = type.Packed[0];
                if (d.Right == null)
                {
                    Error(d.Location, ElaborationCodes.BadSelect, "A vector's range needs both ends: [7:0].");
                    return false;
                }

                if (!ExpressionBuilder.TryInteger(d.Left, scope, _diagnostics, "a range's bound", out long msb) ||
                    !ExpressionBuilder.TryInteger(d.Right, scope, _diagnostics, "a range's bound", out long lsb))
                    return false;

                if (Math.Abs(msb - lsb) + 1 > LogicVec.MaxWidth || Math.Abs(msb) > int.MaxValue / 2 || Math.Abs(lsb) > int.MaxValue / 2)
                {
                    Error(d.Location, ElaborationCodes.TooLarge, $"[{msb}:{lsb}] is wider than BitSorter allows ({LogicVec.MaxWidth} bits).");
                    return false;
                }

                shape = new Shape((int)msb, (int)lsb, signed, null, twoState);
                return true;
            }

            /// <summary>
            /// A packed struct or union's layout (IEEE 1800, 7.2.1 and 7.3.1): a struct's members one after
            /// another, the first at the top; a union's all from bit 0, all the same width. 2-state only when
            /// every member is. An unpacked struct is refused: BitSorter keeps every value one vector.
            /// </summary>
            private bool TryStruct(StructType s, Scope scope, out PackedType type)
            {
                type = null;
                string kind = s.IsUnion ? "union" : "struct";

                if (!s.Packed)
                {
                    NotYet(s.Location, $"An unpacked {kind} is not supported by BitSorter yet; write {kind} packed, which keeps its members in one vector.");
                    return false;
                }

                var members = new List<(string Name, PackedType Type, SourceLocation At)>();
                var names = new HashSet<string>(StringComparer.Ordinal);
                bool ok = true, twoState = true;

                foreach (StructMember member in s.Members)
                {
                    if (!TryShape(member.Type, scope, out Shape memberShape))
                    {
                        ok = false;
                        continue;
                    }

                    if (!names.Add(member.Name))
                    {
                        Error(member.Location, ElaborationCodes.Duplicate, $"The {kind} has two members called {member.Name}.");
                        ok = false;
                        continue;
                    }

                    members.Add((member.Name, memberShape.Type, member.Location));
                    twoState &= memberShape.TwoState;
                }

                if (!ok)
                    return false;

                if (members.Count == 0)
                {
                    Error(s.Location, ElaborationCodes.BadSelect, $"A {kind} needs at least one member.");
                    return false;
                }

                var fields = new List<StructField>(members.Count);
                int width;

                if (s.IsUnion)
                {
                    width = members[0].Type.Width;
                    foreach ((string name, PackedType memberType, SourceLocation at) in members)
                    {
                        if (memberType.Width != width)
                        {
                            Error(at, ElaborationCodes.WidthMismatch,
                                $"Every member of a packed union is the same width, and {name} is {memberType.Width} bits where {members[0].Name} is {width}.");
                            return false;
                        }

                        fields.Add(new StructField(name, 0, memberType));
                    }
                }
                else
                {
                    width = 0;
                    foreach ((string _, PackedType memberType, SourceLocation _) in members)
                        width += memberType.Width;

                    if (width > LogicVec.MaxWidth)
                    {
                        Error(s.Location, ElaborationCodes.TooLarge, $"The struct is wider than BitSorter allows ({LogicVec.MaxWidth} bits).");
                        return false;
                    }

                    int lowest = width;
                    foreach ((string name, PackedType memberType, SourceLocation _) in members)
                    {
                        lowest -= memberType.Width;
                        fields.Add(new StructField(name, lowest, memberType));
                    }
                }

                type = new PackedType(width - 1, 0, s.Signed, null, new StructLayout(s.IsUnion, fields), null, twoState);
                return true;
            }

            /// <summary>
            /// Packed dimensions around an element, the first the outermost: <c>[3:0][7:0]</c> of bits is four
            /// bytes. The whole is signed as declared; its elements are not, unless they are of a signed type
            /// of their own (IEEE 1800, 7.4.1). No dimensions gives the element itself, or a bit.
            /// </summary>
            private bool TryDimensions(List<Dimension> dims, Scope scope, PackedType element, bool signed, bool twoState, EnumInfo @enum, out PackedType type)
            {
                type = element;
                if (dims.Count == 0)
                {
                    type = element ?? PackedType.Bits(0, 0, signed, twoState, @enum);
                    return true;
                }

                var bounds = new List<(int Msb, int Lsb)>(dims.Count);
                foreach (Dimension d in dims)
                {
                    if (d.Right == null)
                    {
                        Error(d.Location, ElaborationCodes.BadSelect, "A packed range needs both ends: [7:0].");
                        return false;
                    }

                    if (!ExpressionBuilder.TryInteger(d.Left, scope, _diagnostics, "a range's bound", out long msb) ||
                        !ExpressionBuilder.TryInteger(d.Right, scope, _diagnostics, "a range's bound", out long lsb))
                        return false;

                    if (Math.Abs(msb) > int.MaxValue / 2 || Math.Abs(lsb) > int.MaxValue / 2)
                    {
                        Error(d.Location, ElaborationCodes.TooLarge, $"[{msb}:{lsb}] is wider than BitSorter allows ({LogicVec.MaxWidth} bits).");
                        return false;
                    }

                    bounds.Add(((int)msb, (int)lsb));
                }

                long width = element?.Width ?? 1;
                foreach ((int msb, int lsb) in bounds)
                    width *= Math.Abs((long)msb - lsb) + 1;

                if (width > LogicVec.MaxWidth)
                {
                    Error(dims[0].Location, ElaborationCodes.TooLarge, $"The packed array is wider than BitSorter allows ({LogicVec.MaxWidth} bits).");
                    return false;
                }

                PackedType inner = element;
                for (int k = bounds.Count - 1; k >= 0; k--)
                {
                    bool outermost = k == 0;
                    inner = new PackedType(bounds[k].Msb, bounds[k].Lsb, outermost && signed, inner, null, outermost && inner == null ? @enum : null, twoState);
                }

                type = inner;
                return true;
            }

            /// <summary>
            /// An enum's shape -- its base type's, <c>int</c> when it names none -- with its members
            /// declared as constants in <paramref name="scope"/>, each the one before plus one unless it is
            /// given a value, and gathered into the <see cref="EnumInfo"/> a signal of the type carries,
            /// known by <paramref name="typedefName"/> (null in place).
            /// </summary>
            private bool TryEnum(EnumType e, Scope scope, string typedefName, out Shape shape)
            {
                // With no base type, an enum is an int: 32 bits, signed, 2-state (IEEE 1800, 6.19).
                shape = new Shape(31, 0, true, null, twoState: true);

                if (e.BaseType != null && !TryShape(e.BaseType, scope, out shape))
                    return false;

                // Met again in the same scope: the members are in already, and it is the same enum.
                if (_enumsDeclared.TryGetValue((scope, e), out EnumInfo known))
                {
                    shape = new Shape(shape.Msb, shape.Lsb, shape.Signed, known, shape.TwoState);
                    return true;
                }

                // Marked before the members are worked out, so a member that fails says so once.
                _enumsDeclared[(scope, e)] = null;

                LogicVec next = LogicVec.FromInt64(0, shape.Width, shape.Signed);
                LogicVec one = LogicVec.FromInt64(1, shape.Width, shape.Signed);
                var members = new List<EnumMemberInfo>(e.Members.Count);

                foreach (EnumMember member in e.Members)
                {
                    LogicVec value = next;
                    if (member.Value != null)
                    {
                        if (!ExpressionBuilder.TryConstant(member.Value, scope, _diagnostics, out LogicVec given))
                            return false;
                        value = given.Extend(shape.Width, given.IsSigned);
                        value = shape.Signed ? value.AsSigned() : value.AsUnsigned();
                    }

                    if (!scope.TryDeclare(member.Name, new ConstantEntry { Value = value, Declared = member.Location }))
                        Error(member.Location, ElaborationCodes.Duplicate, $"{member.Name} is declared twice here.");

                    members.Add(new EnumMemberInfo(member.Name, value));
                    next = LogicVec.Add(value, one);
                }

                // The base type's own enum, if it named one, is not this one: the shape carries only this.
                var info = new EnumInfo(typedefName, shape.Width, members);
                _enumsDeclared[(scope, e)] = info;
                shape = new Shape(shape.Msb, shape.Lsb, shape.Signed, info, shape.TwoState);
                return true;
            }

            // -----------------------------------------------------------------
            // Signals
            // -----------------------------------------------------------------

            private SignalEntry DeclareSignal(Scope scope, string name, SourceLocation at, Shape shape, SignalKind kind, PortDirection? port,
                LogicVec? initial = null, bool local = false)
            {
                if (scope.Own(name) != null)
                {
                    Error(at, ElaborationCodes.Duplicate, $"{name} is declared twice here.");
                    return null;
                }

                // An inout joined to its net (Part 14f) is that net, under the port's name: what drives it inside and what
                // drives it outside are drivers of one signal, resolved together, as Icarus joins them.
                // One of the wrong width is said, and then declared a net of its own below, so nothing that reads it says more.
                SignalEntry net = null;
                if (port == PortDirection.Inout)
                    _joined.TryGetValue(scope.Path + "." + name, out net);
                if (net != null && net.Width != shape.Width)
                    Error(at, ElaborationCodes.BadPort,
                        $"Inout {name} is {shape.Width} bit{(shape.Width == 1 ? "" : "s")}, and {LastName(net)}, the net it is joined to, is {net.Width}: an inout joins a net of its own width.");
                else if (net != null)
                {
                    var joined = new SignalEntry
                    {
                        Signal = net.Signal,
                        FullName = net.FullName,
                        Width = net.Width,
                        Msb = shape.Msb,
                        Lsb = shape.Lsb,
                        Signed = shape.Signed,
                        Kind = SignalKind.Net,
                        Port = port,
                        Enum = shape.Enum,
                        TwoState = shape.TwoState,
                        Type = shape.Type,
                        Declared = at,
                    };
                    scope.TryDeclare(name, joined);
                    _builder.Alias(scope.Path + "." + name, net.Signal);
                    return joined;
                }

                string fullName = local ? $"{scope.Path}.{name}#{++_unnamed}" : scope.Path + "." + name;
                if (!Fits(shape.Width, 1, at, name))
                    return null;
                SignalId id;

                try
                {
                    // Everything the declaration said goes with it, for a waveform to list and name it by.
                    id = _builder.Declare(fullName, shape.Width, kind, shape.Signed, local, initial,
                        shape.Msb, shape.Lsb, shape.Enum, port, scope.Recorded, shape.TwoState);
                    if (shape.Type.IsString)
                        _builder.MarkString(id);
                    if (shape.Type.IsReal)
                        _builder.MarkReal(id);
                }
                catch (ArgumentException ex)
                {
                    Error(at, ElaborationCodes.Duplicate, ex.Message);
                    return null;
                }

                var entry = new SignalEntry
                {
                    Signal = id,
                    FullName = fullName,
                    Width = shape.Width,
                    Msb = shape.Msb,
                    Lsb = shape.Lsb,
                    Signed = shape.Signed,
                    Kind = kind,
                    Port = port,
                    Enum = shape.Enum,
                    TwoState = shape.TwoState,
                    Type = shape.Type,
                    Declared = at,
                };

                scope.TryDeclare(name, entry);
                _declared.Add(entry);
                return entry;
            }

            /// <summary>
            /// What kind of signal a port is: the top's inputs are variables, for the stimulus to write;
            /// every other input is a wire; an output is a variable when it has a data type
            /// (<c>output logic</c>) and a wire when it has a net type or none.
            /// </summary>
            private bool TryPortKind(PortDirection direction, string netType, DataType type, bool isTop, SourceLocation at, out SignalKind kind)
            {
                kind = SignalKind.Net;

                // Driven from inside and out, so a net, which takes more than one driver (Part 14f).
                if (direction == PortDirection.Inout)
                    return true;

                if (direction == PortDirection.Input)
                {
                    kind = isTop ? SignalKind.Variable : SignalKind.Net;
                    return true;
                }

                kind = netType == null && type != null && !type.IsImplicit ? SignalKind.Variable : SignalKind.Net;
                return true;
            }

            private void DeclareAnsiPort(Scope scope, PortDeclaration port, bool isTop, ModuleDeclaration module, Dictionary<string, InstanceEntry> bindings)
            {
                if (port.Interface != null)
                {
                    if (IsInterfacePort(port))
                    {
                        DeclareInterfacePort(scope, port, isTop, module, bindings);
                        return;
                    }

                    // bus_if bus where bus_if is no interface: a port of a typedef's type after all, its direction the last one written.
                    if (!(scope.Lookup(port.Interface) is TypeEntry))
                    {
                        Error(port.Location, ElaborationCodes.UnknownModule, $"There is no interface or type called {port.Interface}, which port {port.Name} is declared as. Is its file read?");
                        return;
                    }

                    bool directed = false;
                    for (int i = 0; module.Ports[i] != port; i++)
                        directed |= module.Ports[i].Interface == null;

                    if (!directed)
                    {
                        Error(port.Location, ElaborationCodes.BadPort, $"Port {port.Name} needs a direction -- input, output or inout -- before its type, {port.Interface}.");
                        return;
                    }
                }

                if (port.Unpacked.Count > 0)
                {
                    NotYet(port.Location, $"Port {port.Name} is an unpacked array, which BitSorter does not support yet; a packed one, logic [3:0][7:0], can be a port.");
                    return;
                }

                if (!CheckNetType(port.NetType, port.Location))
                    return;

                if (!TryPortKind(port.Direction, port.NetType, port.Type, isTop, port.Location, out SignalKind kind))
                    return;

                if (!TryShape(port.Type, scope, out Shape shape))
                    return;

                DeclareSignal(scope, port.Name, port.Location, shape, kind, port.Direction);
            }

            private bool CheckNetType(string netType, SourceLocation at)
            {
                switch (netType)
                {
                    case null:
                    case "wire":
                    case "tri":
                    case "uwire":
                    case "supply0":
                    case "supply1":
                        return true;
                    default:
                        NotYet(at, $"A {netType} net resolves its drivers its own way, which BitSorter does not support yet; use wire.");
                        return false;
                }
            }

            /// <summary>Declarations: signals, non-ANSI ports, genvars. Parameters and typedefs are already in.</summary>
            private void DeclarePass(Scope scope, List<ModuleItem> items, ModuleDeclaration module, bool isTop)
            {
                foreach (ModuleItem item in Flatten(items))
                {
                    switch (item)
                    {
                        case DataDeclaration d:
                            DeclareData(scope, d, module);
                            break;

                        case PortItemDeclaration p:
                            DeclarePortItem(scope, p, module, isTop);
                            break;

                        case GenvarDeclaration g:
                            foreach (string name in g.Names)
                                if (!scope.TryDeclare(name, new GenvarEntry { Declared = g.Location }))
                                    Error(g.Location, ElaborationCodes.Duplicate, $"{name} is declared twice here.");
                            break;

                        case SubroutineDeclaration s:
                            DeclareSubroutine(scope, s);
                            break;
                    }
                }
            }

            private void DeclareData(Scope scope, DataDeclaration d, ModuleDeclaration module)
            {
                if (d.IsConst)
                {
                    NotYet(d.Location, "A const variable is not supported by BitSorter yet; use a localparam.");
                    return;
                }

                if (!CheckNetType(d.NetType, d.Location))
                    return;

                if (d.Type != null && d.Type.Keyword == "event")
                {
                    foreach (Declarator declarator in d.Declarators)
                        DeclareEvent(scope, declarator);
                    return;
                }

                if (!TryShape(d.Type, scope, out Shape shape))
                    return;

                SignalKind kind = d.NetType != null ? SignalKind.Net : SignalKind.Variable;

                foreach (Declarator declarator in d.Declarators)
                {
                    // A non-ANSI port's type given again (output y; reg y;) was taken with the port.
                    if (module != null && module.PortNames.Contains(declarator.Name))
                        continue;

                    if (declarator.Unpacked.Count > 0)
                    {
                        DeclareMemory(scope, d, declarator, shape, kind);
                        continue;
                    }

                    if (d.Delay != null && declarator.Initializer == null)
                    {
                        NotYet(d.Location, "A delay on a net with no assignment is not supported by BitSorter yet; put it on the assign.");
                        continue;
                    }

                    LogicVec? initial = null;
                    if (declarator.Initializer != null && kind == SignalKind.Variable)
                    {
                        if (!TryStartingValue(scope, declarator, shape, out LogicVec value))
                            continue;
                        initial = value;
                    }

                    DeclareSignal(scope, declarator.Name, declarator.Location, shape, kind, null, initial);
                }
            }

            /// <summary>
            /// <c>event e;</c> (Part L5): a bit of the design's own that every <c>-&gt; e</c> turns over, so <c>@(e)</c>
            /// wakes on a trigger as on any change, and the time of the last trigger plus one, for <c>e.triggered</c>.
            /// Neither is recorded: a trigger is a moment, not a value.
            /// </summary>
            private void DeclareEvent(Scope scope, Declarator declarator)
            {
                if (declarator.Unpacked.Count > 0 || declarator.Initializer != null)
                {
                    NotYet(declarator.Location, $"{declarator.Name} is an array of events, or an event given another; BitSorter supports a single event e; so far.");
                    return;
                }

                var bit = new Shape(0, 0, false, null, twoState: true);
                SignalEntry e = DeclareSignal(scope, declarator.Name, declarator.Location, bit, SignalKind.Variable, null, LogicVec.FromUInt64(0, 1), local: true);
                if (e == null)
                    return;

                e.IsEvent = true;
                e.Triggered = _builder.Local($"{e.FullName}.triggered", 64, signed: false, LogicVec.FromUInt64(0, 64));
            }

            /// <summary>
            /// <c>logic [7:0] mem [0:255];</c> or <c>[256]</c>: a memory, one variable a word. More than one
            /// dimension, <c>[0:3][0:7]</c>, is one memory of every word, row by row (Part L4).
            /// </summary>
            private void DeclareMemory(Scope scope, DataDeclaration d, Declarator declarator, Shape shape, SignalKind kind)
            {
                if (kind != SignalKind.Variable)
                {
                    NotYet(declarator.Location, $"{declarator.Name} is an array of wires, which BitSorter does not support yet; declare it logic.");
                    return;
                }

                if (declarator.Initializer != null)
                {
                    NotYet(declarator.Initializer.Location,
                        $"Giving the memory {declarator.Name} starting values in its declaration is not supported yet; load it with $readmemh, or in an initial block.");
                    return;
                }

                var dims = new (int Left, int Right)[declarator.Unpacked.Count];
                long words = 1;

                for (int k = 0; k < dims.Length; k++)
                {
                    Dimension range = declarator.Unpacked[k];
                    long left, right;

                    if (range.Right == null)
                    {
                        if (!ExpressionBuilder.TryInteger(range.Left, scope, _diagnostics, "a memory's size", out long size))
                            return;
                        if (size < 1)
                        {
                            Error(range.Location, ElaborationCodes.BadSelect, $"A memory needs at least one word, and {declarator.Name} is given {size}.");
                            return;
                        }

                        left = 0;
                        right = size - 1;
                    }
                    else if (!ExpressionBuilder.TryInteger(range.Left, scope, _diagnostics, "a memory's bound", out left) ||
                             !ExpressionBuilder.TryInteger(range.Right, scope, _diagnostics, "a memory's bound", out right))
                    {
                        return;
                    }

                    words *= Math.Min(Math.Abs(left - right) + 1, (long)NetlistBuilder.MaxMemoryWords + 1);
                    if (words > NetlistBuilder.MaxMemoryWords || Math.Abs(left) > int.MaxValue / 2 || Math.Abs(right) > int.MaxValue / 2)
                    {
                        Error(range.Location, ElaborationCodes.TooLarge, $"{declarator.Name} would have more than {NetlistBuilder.MaxMemoryWords} words.");
                        return;
                    }

                    dims[k] = ((int)left, (int)right);
                }

                if (scope.Own(declarator.Name) != null)
                {
                    Error(declarator.Location, ElaborationCodes.Duplicate, $"{declarator.Name} is declared twice here.");
                    return;
                }

                string fullName = scope.Path + "." + declarator.Name;
                if (!Fits(words * shape.Width, words, declarator.Location, declarator.Name))
                    return;
                MemoryInfo memory;
                try
                {
                    memory = _builder.Memory(fullName, shape.Width, shape.Signed, dims, shape.Msb, shape.Lsb, shape.Enum, scope.Recorded, shape.TwoState);
                }
                catch (ArgumentException ex)
                {
                    Error(declarator.Location, ElaborationCodes.Duplicate, ex.Message);
                    return;
                }

                scope.TryDeclare(declarator.Name, new MemoryEntry
                {
                    Memory = memory,
                    FullName = fullName,
                    Msb = shape.Msb,
                    Lsb = shape.Lsb,
                    Type = shape.Type,
                    Dims = dims,
                    Declared = declarator.Location,
                });
            }

            private void DeclarePortItem(Scope scope, PortItemDeclaration p, ModuleDeclaration module, bool isTop)
            {
                if (!CheckNetType(p.NetType, p.Location))
                    return;

                foreach (Declarator declarator in p.Declarators)
                {
                    if (!module.PortNames.Contains(declarator.Name))
                    {
                        Error(declarator.Location, ElaborationCodes.BadPort,
                            $"{declarator.Name} is declared {p.Direction.ToString().ToLowerInvariant()}, but it is not in {module.Name}'s port list.");
                        continue;
                    }

                    if (declarator.Unpacked.Count > 0)
                    {
                        NotYet(declarator.Location, $"Port {declarator.Name} is an unpacked array, which BitSorter does not support yet; a packed one, logic [3:0][7:0], can be a port.");
                        continue;
                    }

                    // output [3:0] y; reg [3:0] y; -- the second declaration says what kind it is.
                    DataDeclaration again = Redeclaration(module, declarator.Name);
                    string netType = p.NetType;
                    DataType type = p.Type;

                    if (again != null)
                    {
                        netType = again.NetType ?? netType;
                        if (type == null || (type.IsImplicit && type.Packed.Count == 0 && !type.Signed))
                            type = again.Type;
                        else if (again.NetType == null && type.IsImplicit)
                            type = WithKeyword(type, again.Type?.Keyword ?? "logic", type.Signed || (again.Type?.Signed ?? false));
                    }

                    if (!TryPortKind(p.Direction, netType, type, isTop, declarator.Location, out SignalKind kind))
                        continue;

                    if (!TryShape(type, scope, out Shape shape))
                        continue;

                    DeclareSignal(scope, declarator.Name, declarator.Location, shape, kind, p.Direction);
                }
            }

            /// <summary><paramref name="from"/>'s range and signedness on a data type: <c>output [3:0] y; reg y;</c> is a <c>reg [3:0]</c>.</summary>
            private static DataType WithKeyword(DataType from, string keyword, bool signed)
            {
                var type = new DataType { Location = from.Location, Keyword = keyword, Signed = signed };
                type.Packed.AddRange(from.Packed);
                return type;
            }

            private static DataDeclaration Redeclaration(ModuleDeclaration module, string name)
            {
                foreach (ModuleItem item in Flatten(module.Items))
                    if (item is DataDeclaration d)
                        foreach (Declarator declarator in d.Declarators)
                            if (declarator.Name == name)
                                return d;
                return null;
            }

            /// <summary>An undeclared name used where a net may be implied: a one-bit wire, with a warning (IEEE 1800, 6.10).</summary>
            private SignalEntry ImplicitNet(Scope scope, NameExpression name)
            {
                Warning(name.Location, ElaborationCodes.ImplicitNet,
                    $"{name.Name} is not declared, so it is taken to be a one-bit wire. Declare it; a misspelt name ends up here too.");
                return DeclareSignal(scope, name.Name, name.Location, new Shape(0, 0, false), SignalKind.Net, null);
            }

            // -----------------------------------------------------------------
            // Everything that drives something
            // -----------------------------------------------------------------

            private void BuildPass(Scope scope, List<ModuleItem> items, int depth, bool interfacesBuilt = false)
            {
                // The interfaces first (Part L8), so a module given one through a port finds it made, wherever it is written.
                if (!interfacesBuilt)
                    foreach (ModuleItem item in Flatten(items))
                        if (item is ModuleInstantiation mi && _interfaces.ContainsKey(mi.Module) && !_modules.ContainsKey(mi.Module))
                            BuildInstances(scope, mi, depth);

                foreach (ModuleItem item in items)
                {
                    if (_stopped)
                        return;

                    switch (item)
                    {
                        case GenerateRegion region:
                            BuildPass(scope, region.Items, depth, interfacesBuilt: true);
                            break;

                        case ContinuousAssign assign:
                            foreach (Assignment a in assign.Assignments)
                            {
                                if (Hierarchical(a.Target) || Hierarchical(a.Value))
                                    _deferred.Add(() => BuildAssign(scope, a.Target, a.Value, assign.Delay, a.Location));
                                else
                                    BuildAssign(scope, a.Target, a.Value, assign.Delay, a.Location);
                            }
                            break;

                        case DataDeclaration d when d.NetType != null:
                            foreach (Declarator declarator in d.Declarators)
                            {
                                if (declarator.Initializer != null && scope.Own(declarator.Name) is SignalEntry)
                                    BuildAssign(scope, new NameExpression { Location = declarator.Location, Path = { declarator.Name } }, declarator.Initializer, d.Delay, declarator.Location);

                                if ((d.NetType == "supply0" || d.NetType == "supply1") && scope.Own(declarator.Name) is SignalEntry supply)
                                {
                                    HoldSupplyToLibrary(scope, declarator.Location);
                                    Drive(scope, new List<ExpressionBuilder.Part> { new ExpressionBuilder.Part(supply, 0, supply.Width) },
                                        Expr.Constant(LogicVec.Fill(d.NetType == "supply1" ? Logic4.One : Logic4.Zero, supply.Width)),
                                        Delay.Zero, false, declarator.Location, d.NetType + " " + supply.FullName);
                                }
                            }
                            break;

                        case PrimitiveInstantiation p:
                            BuildPrimitives(scope, p);
                            break;

                        case ModuleInstantiation mi when !_interfaces.ContainsKey(mi.Module) || _modules.ContainsKey(mi.Module):
                            BuildInstances(scope, mi, depth);
                            break;

                        case LoopGenerate loop:
                            BuildLoop(scope, loop, depth);
                            break;

                        case IfGenerate g:
                            BuildIf(scope, g, ++scope.GenerateConstructs, depth);
                            break;

                        case GenerateBlock block:
                        {
                            int number = ++scope.GenerateConstructs;
                            BuildBlock(scope, block, block.Label ?? "genblk" + number, depth);
                            break;
                        }

                        case ProceduralBlock process:
                            // Compiled once the module's instances are in, so dut.ctl.state can be reached.
                            _deferred.Add(() =>
                            {
                                List<ProcessInfo> compiled = CompileProcess(scope, process);
                                if (compiled != null)
                                {
                                    HoldProcessToLibrary(scope, process, compiled);

                                    // The process, then its forks' branches (Part L5).
                                    foreach (ProcessInfo info in compiled)
                                    {
                                        _ownProcesses.Add(info);
                                        _processAt[info] = process.Location;
                                    }
                                }
                            });
                            break;

                        case SubroutineDeclaration _:
                            // Declared in the first pass; compiled where it is called.
                            break;
                    }
                }
            }

            /// <summary>Whether an expression names anything through the hierarchy: <c>dut.ctl.state</c>, <c>slice[3].s.y</c>.</summary>
            private static bool Hierarchical(Expression e)
            {
                switch (e)
                {
                    case NameExpression n: return n.Path.Count > 1;
                    case MemberExpression _: return true;
                    case IndexExpression i: return Hierarchical(i.Target) || Hierarchical(i.Index);
                    case RangeSelectExpression r: return Hierarchical(r.Target) || Hierarchical(r.Left) || Hierarchical(r.Right);
                    case ConcatenationExpression c: return c.Items.Exists(Hierarchical);
                    case ReplicationExpression r: return Hierarchical(r.Count) || Hierarchical(r.Value);
                    case UnaryExpression u: return Hierarchical(u.Operand);
                    case BinaryExpression b: return Hierarchical(b.Left) || Hierarchical(b.Right);
                    case ConditionalExpression c: return Hierarchical(c.Condition) || Hierarchical(c.WhenTrue) || Hierarchical(c.WhenFalse);
                    case ParenthesizedExpression p: return Hierarchical(p.Inner);
                    case CallExpression c: return c.Arguments.Exists(a => a != null && Hierarchical(a));
                    case CastExpression c: return Hierarchical(c.Value);
                    // A method may be a function of an instance written later (Part L8): u.f(), bus.fire().
                    case MethodCallExpression _: return true;
                    default: return false;
                }
            }

            private static string ProcessName(ProcessKind kind)
            {
                switch (kind)
                {
                    case ProcessKind.Initial: return "initial";
                    case ProcessKind.Final: return "final";
                    case ProcessKind.AlwaysComb: return "always_comb";
                    case ProcessKind.AlwaysFf: return "always_ff";
                    case ProcessKind.AlwaysLatch: return "always_latch";
                    default: return "always";
                }
            }

            private bool TryDelay(DelayControl control, Scope scope, out Delay delay)
            {
                delay = Delay.Zero;
                if (control == null)
                    return true;

                if (control.Values.Count < 1 || control.Values.Count > 3)
                {
                    Error(control.Location, ElaborationCodes.BadDelay, "A delay has one, two or three values: #d, #(rise, fall) or #(rise, fall, turn-off).");
                    return false;
                }

                // In ticks: scaled by the module's timescale, a fraction of a unit rounded to its precision (Part L7).
                var values = new long[control.Values.Count];
                for (int i = 0; i < values.Length; i++)
                    if (!ExpressionBuilder.TryDelayTicks(control.Values[i], scope, _diagnostics, out values[i]))
                        return false;

                delay = values.Length == 1 ? Delay.Of(values[0])
                    : values.Length == 2 ? Delay.Of(values[0], values[1])
                    : Delay.Of(values[0], values[1], values[2]);
                return true;
            }

            private void BuildAssign(Scope scope, Expression target, Expression value, DelayControl delayControl, SourceLocation at)
            {
                if (!TryDelay(delayControl, scope, out Delay delay))
                    return;

                var builder = new ExpressionBuilder(scope, _diagnostics, constantOnly: false);
                var parts = new List<ExpressionBuilder.Part>();
                if (!builder.TryTarget(target, parts, name => ImplicitNet(scope, name)))
                    return;

                int width = 0;
                foreach (ExpressionBuilder.Part part in parts)
                    width += part.Width;

                // assign pkt = '{...} lays the pattern out on the target's type (Part L4).
                ExpressionBuilder.TypeInfo type = builder.TypeOf(value);
                Expr built = builder.BuildAssigned(value, parts.Count == 1 ? builder.TypeAt(target) : null, width);
                if (builder.Failed)
                    return;

                LintTruncation(type, target, value, scope, width, at);
                HoldAssignToLibrary(scope, built, delay, at);
                Drive(scope, parts, built, delay, type.Signed, at, null);
            }

            /// <summary>
            /// Drives each piece of a target from its bits of <paramref name="value"/>: the last piece
            /// from the lowest bits, as a concatenation on the left of an assignment takes them.
            /// </summary>
            /// <summary>Drives the parts with the value's bits, each driver told the scope it is written in, <paramref name="scope"/>.</summary>
            private void Drive(Scope scope, List<ExpressionBuilder.Part> parts, Expr value, Delay delay, bool signExtend, SourceLocation at, string name)
            {
                _builder.WrittenIn = scope.Recorded;
                try
                {
                    Drive(parts, value, delay, signExtend, at, name);
                }
                finally
                {
                    _builder.WrittenIn = -1;
                }
            }

            private void Drive(List<ExpressionBuilder.Part> parts, Expr value, Delay delay, bool signExtend, SourceLocation at, string name)
            {
                int total = 0;
                foreach (ExpressionBuilder.Part part in parts)
                    total += part.Width;

                if (value.Width < total)
                    value = Expr.Resize(value, total, signExtend);

                int offset = total;
                foreach (ExpressionBuilder.Part part in parts)
                {
                    offset -= part.Width;
                    Expr piece = offset == 0 && value.Width == part.Width ? value : Expr.Slice(value, offset, part.Width);

                    try
                    {
                        _builder.AssignSlice(part.Signal.Signal, part.Lowest, part.Width, piece, delay, false, name);
                    }
                    catch (InvalidOperationException ex)
                    {
                        Error(at, ElaborationCodes.Drivers, ex.Message);
                    }
                    catch (ArgumentException ex)
                    {
                        Error(at, ElaborationCodes.BadSelect, ex.Message);
                    }
                }
            }

            // -----------------------------------------------------------------
            // Gate primitives
            // -----------------------------------------------------------------

            private static bool TryGateKind(string primitive, out GateKind kind)
            {
                switch (primitive)
                {
                    case "and": kind = GateKind.And; return true;
                    case "nand": kind = GateKind.Nand; return true;
                    case "or": kind = GateKind.Or; return true;
                    case "nor": kind = GateKind.Nor; return true;
                    case "xor": kind = GateKind.Xor; return true;
                    case "xnor": kind = GateKind.Xnor; return true;
                    case "buf": kind = GateKind.Buf; return true;
                    case "not": kind = GateKind.Not; return true;
                    case "bufif0": kind = GateKind.Bufif0; return true;
                    case "bufif1": kind = GateKind.Bufif1; return true;
                    case "notif0": kind = GateKind.Notif0; return true;
                    case "notif1": kind = GateKind.Notif1; return true;
                    default: kind = default; return false;
                }
            }

            private void BuildPrimitives(Scope scope, PrimitiveInstantiation p)
            {
                if (!TryGateKind(p.Primitive, out GateKind kind))
                {
                    NotYet(p.Location, $"The {p.Primitive} primitive is not supported by BitSorter yet.");
                    return;
                }

                if (!TryDelay(p.Delay, scope, out Delay delay))
                    return;

                foreach (PrimitiveInstance instance in p.Instances)
                {
                    if (instance.Array != null)
                    {
                        NotYet(instance.Location, "An array of gates is not supported by BitSorter yet; use a generate loop.");
                        continue;
                    }

                    string name = instance.Name != null ? scope.Path + "." + instance.Name : $"{scope.Path}.{p.Primitive}${++_unnamed}";

                    // buf and not may drive several outputs from their one input; every other gate has one output.
                    bool manyOutputs = kind == GateKind.Buf || kind == GateKind.Not;
                    int outputs = manyOutputs ? instance.Terminals.Count - 1 : 1;
                    int inputs = instance.Terminals.Count - outputs;

                    if (outputs < 1 || !Gates.AcceptsInputCount(kind, inputs))
                    {
                        Error(instance.Location, ElaborationCodes.BadPort, kind == GateKind.Buf || kind == GateKind.Not
                            ? $"A {p.Primitive} gate is written ({p.Primitive} (out, in)), with an output and then its input."
                            : kind >= GateKind.Bufif1
                                ? $"A {p.Primitive} gate is written ({p.Primitive} (out, data, control)): three terminals, and this has {instance.Terminals.Count}."
                                : $"A {p.Primitive} gate needs an output and at least two inputs, and this has {instance.Terminals.Count} terminal{(instance.Terminals.Count == 1 ? "" : "s")}.");
                        continue;
                    }

                    var builder = new ExpressionBuilder(scope, _diagnostics, constantOnly: false);
                    var inputExprs = new Expr[inputs];
                    for (int i = 0; i < inputs; i++)
                    {
                        Expression terminal = instance.Terminals[outputs + i];
                        if (terminal is NameExpression n && n.Path.Count == 1 && scope.Lookup(n.Name) == null)
                            ImplicitNet(scope, n);

                        Expr e = builder.Build(terminal, 1);
                        if (e.Width != 1)
                        {
                            if (!(terminal is NumberLiteral))
                                Warning(terminal.Location, ElaborationCodes.WidthMismatch,
                                    $"{Printer.Print(terminal)} is {e.Width} bits, and a gate's input is one: only its lowest bit is used.");
                            e = Expr.Slice(e, 0, 1);
                        }

                        inputExprs[i] = e;
                    }

                    if (builder.Failed)
                        continue;

                    HoldGateToLibrary(scope, kind, inputs, delay, inputExprs, p.Primitive, instance.Location);

                    for (int o = 0; o < outputs; o++)
                    {
                        var parts = new List<ExpressionBuilder.Part>();
                        if (!builder.TryTarget(instance.Terminals[o], parts, n => ImplicitNet(scope, n)))
                            continue;

                        if (parts.Count != 1 || parts[0].Width != 1)
                        {
                            Error(instance.Terminals[o].Location, ElaborationCodes.BadPort,
                                $"A gate's output is one bit, and {Printer.Print(instance.Terminals[o])} is wider. Connect it to one bit, such as y[0].");
                            continue;
                        }

                        try
                        {
                            _builder.WrittenIn = scope.Recorded;
                            try
                            {
                                _builder.Gate(kind, name, parts[0].Signal.Signal, parts[0].Lowest, delay, inputExprs);
                            }
                            finally
                            {
                                _builder.WrittenIn = -1;
                            }
                        }
                        catch (InvalidOperationException ex)
                        {
                            Error(instance.Location, ElaborationCodes.Drivers, ex.Message);
                        }
                        catch (ArgumentException ex)
                        {
                            Error(instance.Location, ElaborationCodes.BadPort, ex.Message);
                        }
                    }
                }
            }

            // -----------------------------------------------------------------
            // Instances
            // -----------------------------------------------------------------

            private void BuildInstances(Scope scope, ModuleInstantiation mi, int depth)
            {
                if (!_modules.TryGetValue(mi.Module, out ModuleDeclaration module) && !_interfaces.TryGetValue(mi.Module, out module))
                {
                    Error(mi.Location, ElaborationCodes.UnknownModule,
                        $"There is no module called {mi.Module}. Is its file included, and the name spelt the same?");
                    return;
                }

                Dictionary<string, Expression> overrides = Overrides(module, mi);

                foreach (ModuleInstance instance in mi.Instances)
                {
                    if (instance.Array != null)
                    {
                        NotYet(instance.Location, "An array of instances is not supported by BitSorter yet; use a generate loop.");
                        continue;
                    }

                    string path = scope.Path + "." + instance.Name;

                    var entry = new InstanceEntry { Declared = instance.Location, Interface = module.IsInterface ? module : null };
                    if (!scope.TryDeclare(instance.Name, entry))
                    {
                        Error(instance.Location, ElaborationCodes.Duplicate, $"{instance.Name} is the name of something else here already.");
                        continue;
                    }

                    // What each interface port is given (Part L8), worked out before the instance is made, since what is
                    // inside it reaches through them. Nothing is made of an instance whose interface ports cannot be.
                    Dictionary<string, InstanceEntry> bindings = null;
                    if (module.Ports.Exists(IsInterfacePort))
                    {
                        bindings = BindInterfaces(scope, module, instance);
                        if (bindings == null)
                            continue;
                    }

                    JoinInouts(scope, module, instance, path);
                    Scope child = ElaborateModule(module, path, overrides, scope, instance.Location, isTop: false, depth: depth + 1, within: scope, bindings);
                    if (child == null)
                        continue;

                    entry.Scope = child;
                    if (module.IsInterface)
                        child.Modports = Modports(child, module);
                    HoldInstanceToLibrary(scope, mi.Module, child, instance);

                    Connect(scope, child, module, instance);
                }
            }

            /// <summary>
            /// The net each inout port of <paramref name="instance"/> is joined to (Part 14f), worked out before the instance is
            /// made, so its port is declared as that net (<see cref="DeclareSignal"/>). An inout is driven from both sides, so
            /// it joins a whole net -- a wire or a tri, <c>.io(bus)</c> -- and nothing else: a one-way assignment each way
            /// would hold the bus at whatever it last was. One left unconnected floats, as a net of its own.
            /// </summary>
            private void JoinInouts(Scope scope, ModuleDeclaration module, ModuleInstance instance, string path)
            {
                var inouts = new List<string>();
                foreach (PortDeclaration port in module.Ports)
                    if (port.Direction == PortDirection.Inout && port.Interface == null)
                        inouts.Add(port.Name);
                foreach (ModuleItem item in module.Items)
                    if (item is PortItemDeclaration declared && declared.Direction == PortDirection.Inout)
                        foreach (Declarator d in declared.Declarators)
                            inouts.Add(d.Name);
                if (inouts.Count == 0)
                    return;

                Dictionary<string, PortConnection> connections = MatchConnections(module, instance, report: false, out bool wildcard);
                foreach (string port in inouts)
                {
                    Expression given = null;
                    SourceLocation at = instance.Location;
                    if (connections.TryGetValue(port, out PortConnection c))
                    {
                        at = c.Location;
                        given = c.IsShorthand ? new NameExpression { Location = c.Location, Path = { port } } : c.Value;
                    }
                    else if (wildcard)
                    {
                        given = new NameExpression { Location = instance.Location, Path = { port } };
                    }
                    if (given == null)
                        continue;

                    if (Bare(given) is NameExpression n && n.Path.Count == 1)
                    {
                        if (scope.Lookup(n.Name) == null)
                            ImplicitNet(scope, n);
                        // A net of this module's own, or an inout passed on -- never an input, which carries a value in only.
                        if (scope.Lookup(n.Name) is SignalEntry net && net.Kind == SignalKind.Net && !net.IsEvent && net.Port != PortDirection.Input)
                        {
                            _joined[path + "." + port] = net;
                            continue;
                        }
                    }

                    Error(at, ElaborationCodes.BadPort,
                        $"Inout {port} of {instance.Name} is given {Printer.Print(given)}; an inout is driven from both sides, so it joins a whole net -- a wire or a tri of this module, or an inout passed on, as .{port}(bus) -- and nothing else, an input included.");
                }
            }

            // -----------------------------------------------------------------
            // Interfaces (Part L8)
            // -----------------------------------------------------------------

            /// <summary>Whether a header port is an interface port: one naming a modport, any interface, or an interface the design declares.</summary>
            private bool IsInterfacePort(PortDeclaration port) =>
                port.Interface != null && (port.Modport != null || port.Interface == "interface" || _interfaces.ContainsKey(port.Interface));

            /// <summary>
            /// An interface port, declared as what it was given: the instance, under the port's name, through the modport
            /// it names. On the top, or where nothing could be given, it is declared reaching nothing, having said why.
            /// </summary>
            private void DeclareInterfacePort(Scope scope, PortDeclaration port, bool isTop, ModuleDeclaration module, Dictionary<string, InstanceEntry> bindings)
            {
                InstanceEntry bound = null;
                string wanted = port.Interface == "interface" ? "an interface" : "a " + port.Interface;

                if (port.Unpacked.Count > 0)
                    NotYet(port.Location, $"Port {port.Name} is an array of interfaces, which BitSorter does not support yet; give each its own port.");
                else if (port.Interface != "interface" && !_interfaces.ContainsKey(port.Interface))
                    Error(port.Location, ElaborationCodes.UnknownModule, $"There is no interface called {port.Interface}, which port {port.Name} takes. Is its file read?");
                else if (isTop)
                    Error(port.Location, ElaborationCodes.BadPort,
                        $"{module.Name} is where the simulation starts, so nothing gives its interface port {port.Name} {wanted} to work on. " +
                        $"Start from a testbench that makes {wanted} and an instance of {module.Name} connected to it.");
                else
                    bindings?.TryGetValue(port.Name, out bound);

                if (!scope.TryDeclare(port.Name, bound ?? new InstanceEntry { Declared = port.Location, Unbound = true }))
                    Error(port.Location, ElaborationCodes.Duplicate, $"{port.Name} is the name of something else here already.");
            }

            /// <summary>
            /// What an instance gives each of its module's interface ports: an instance of the interface, here or through
            /// the hierarchy -- <c>.bus(hs)</c>, <c>.bus(hs.source)</c>, <c>.bus(bus)</c> passing its own on, <c>.bus</c> or
            /// <c>.*</c> -- with the modport the port or the connection names. Null, having said why, when any cannot be.
            /// </summary>
            private Dictionary<string, InstanceEntry> BindInterfaces(Scope scope, ModuleDeclaration module, ModuleInstance instance)
            {
                Dictionary<string, PortConnection> connections = MatchConnections(module, instance, report: false, out bool wildcard);
                var bindings = new Dictionary<string, InstanceEntry>(StringComparer.Ordinal);
                bool ok = true;

                foreach (PortDeclaration port in module.Ports)
                {
                    if (!IsInterfacePort(port) || port.Unpacked.Count > 0 || port.Interface != "interface" && !_interfaces.ContainsKey(port.Interface))
                        continue;   // said where the port is declared

                    Expression given = null;
                    SourceLocation at = instance.Location;
                    if (connections.TryGetValue(port.Name, out PortConnection c))
                    {
                        at = c.Location;
                        given = c.IsShorthand ? new NameExpression { Location = c.Location, Path = { port.Name } } : c.Value;
                    }
                    else if (wildcard)
                    {
                        given = new NameExpression { Location = instance.Location, Path = { port.Name } };
                    }

                    if (given == null)
                    {
                        Error(at, ElaborationCodes.BadPort,
                            $"Interface port {port.Name} of {instance.Name} is not connected; give it {(port.Interface == "interface" ? "an instance of an interface" : "an instance of " + port.Interface)}, as .{port.Name}(x).");
                        ok = false;
                        continue;
                    }

                    InstanceEntry alias = Bind(scope, port, given, instance.Name, at);
                    if (alias == null)
                        ok = false;
                    else
                        bindings[port.Name] = alias;
                }

                return ok ? bindings : null;
            }

            /// <summary>One interface port's instance and modport, as <paramref name="given"/> names them in <paramref name="scope"/>.</summary>
            private InstanceEntry Bind(Scope scope, PortDeclaration port, Expression given, string instance, SourceLocation at)
            {
                var builder = new ExpressionBuilder(scope, _diagnostics, constantOnly: false);
                InstanceEntry target = builder.InterfaceNamed(given, out string named);
                string text = Printer.Print(given);

                if (target == null)
                {
                    Error(at, ElaborationCodes.BadPort,
                        $"Port {port.Name} of {instance} takes {(port.Interface == "interface" ? "an interface" : "a " + port.Interface)}, and {text} is not an instance of one.");
                    return null;
                }

                if (port.Interface != "interface" && target.Interface.Name != port.Interface)
                {
                    Error(at, ElaborationCodes.BadPort, $"Port {port.Name} of {instance} takes a {port.Interface}, and {text} is a {target.Interface.Name}.");
                    return null;
                }

                if (port.Modport != null && named != null && port.Modport != named)
                {
                    Error(at, ElaborationCodes.BadPort, $"Port {port.Name} of {instance} is modport {port.Modport}, and {text} names modport {named}.");
                    return null;
                }

                // A port given on through one already limited keeps that limit; it can be named again, not changed.
                string chosen = port.Modport ?? named;
                ModportInfo modport = target.Modport;
                if (chosen != null && modport != null && modport.Name != chosen)
                {
                    Error(at, ElaborationCodes.BadPort, $"{text} is modport {modport.Name} of {target.Interface.Name} here, so it cannot be given on as modport {chosen}.");
                    return null;
                }

                if (chosen != null && modport == null && (target.Scope.Modports == null || !target.Scope.Modports.TryGetValue(chosen, out modport)))
                {
                    Error(at, ElaborationCodes.UnknownName, $"Interface {target.Interface.Name} has no modport called {chosen}.");
                    return null;
                }

                return new InstanceEntry { Declared = port.Location, Scope = target.Scope, Interface = target.Interface, Modport = modport };
            }

            /// <summary>
            /// An interface instance's modports, each name checked against what the interface declares: a signal for a
            /// direction, a task or function for an import. A fault is said for the first instance of the interface only.
            /// </summary>
            private Dictionary<string, ModportInfo> Modports(Scope scope, ModuleDeclaration declaration)
            {
                bool report = _modportsChecked.Add(declaration);
                var modports = new Dictionary<string, ModportInfo>(StringComparer.Ordinal);

                foreach (ModuleItem item in Flatten(declaration.Items))
                {
                    if (!(item is ModportDeclaration d))
                        continue;

                    foreach (Modport m in d.Modports)
                    {
                        if (modports.ContainsKey(m.Name) || scope.Own(m.Name) != null)
                        {
                            if (report)
                                Error(m.Location, ElaborationCodes.Duplicate, $"{m.Name} is the name of something else in {declaration.Name} already.");
                            continue;
                        }

                        var info = new ModportInfo { Name = m.Name, Interface = declaration.Name };
                        foreach (ModportItem i in m.Items)
                        {
                            Entry entry = scope.Own(i.Name);
                            bool fits = i.IsImport ? entry is SubroutineEntry : entry is SignalEntry || entry is MemoryEntry;
                            if (!fits)
                            {
                                if (report)
                                    Error(i.Location, ElaborationCodes.UnknownName, i.IsImport
                                        ? $"Modport {m.Name} imports {i.Name}, and {declaration.Name} has no task or function called that."
                                        : $"Modport {m.Name} lists {i.Name}, and {declaration.Name} has no signal called that.");
                            }
                            else if (info.Items.ContainsKey(i.Name))
                            {
                                if (report)
                                    Error(i.Location, ElaborationCodes.Duplicate, $"Modport {m.Name} lists {i.Name} twice.");
                            }
                            else
                            {
                                info.Items[i.Name] = i;
                            }
                        }

                        modports[m.Name] = info;
                    }
                }

                return modports;
            }

            /// <summary>Which connection of an instance goes to which port, by name or in order; what is wrong is said if <paramref name="report"/>.</summary>
            private Dictionary<string, PortConnection> MatchConnections(ModuleDeclaration module, ModuleInstance instance, bool report, out bool wildcard)
            {
                List<string> ports = PortOrder(module);
                var connections = new Dictionary<string, PortConnection>(StringComparer.Ordinal);
                bool named = false, positional = false;
                int position = 0;
                wildcard = false;

                foreach (PortConnection c in instance.Connections)
                {
                    if (c.IsWildcard)
                    {
                        wildcard = true;
                        continue;
                    }

                    if (c.Name == null)
                    {
                        positional = true;
                        if (position >= ports.Count)
                        {
                            if (c.Value != null && report)
                                Error(c.Location, ElaborationCodes.BadPort,
                                    $"{module.Name} has {ports.Count} port{(ports.Count == 1 ? "" : "s")}, and {instance.Name} connects more.");
                            position++;
                            continue;
                        }

                        connections[ports[position++]] = c;
                        continue;
                    }

                    named = true;
                    if (!ports.Contains(c.Name))
                    {
                        if (report)
                            Error(c.Location, ElaborationCodes.BadPort, $"{module.Name} has no port called {c.Name}.");
                    }
                    else if (connections.ContainsKey(c.Name))
                    {
                        if (report)
                            Error(c.Location, ElaborationCodes.Duplicate, $"Port {c.Name} is connected twice.");
                    }
                    else
                    {
                        connections[c.Name] = c;
                    }
                }

                if (named && positional && report)
                    Error(instance.Location, ElaborationCodes.BadPort, "Connect the ports all by name or all in order, not some of each.");

                return connections;
            }

            private void Connect(Scope scope, Scope child, ModuleDeclaration module, ModuleInstance instance)
            {
                List<string> ports = PortOrder(module);
                Dictionary<string, PortConnection> connections = MatchConnections(module, instance, report: true, out bool wildcard);

                foreach (string port in ports)
                {
                    // An inout is joined to its net as the instance is made (JoinInouts), or left floating.
                    if (!(child.Own(port) is SignalEntry childPort) || !childPort.Port.HasValue || childPort.Port == PortDirection.Inout)
                        continue;

                    Expression expression;
                    SourceLocation at;

                    if (connections.TryGetValue(port, out PortConnection c))
                    {
                        at = c.Location;
                        if (c.IsShorthand)
                        {
                            if (!(scope.Lookup(port) is SignalEntry))
                            {
                                Error(c.Location, ElaborationCodes.UnknownName, $".{port} connects to a signal called {port}, and there is none here.");
                                continue;
                            }

                            expression = new NameExpression { Location = c.Location, Path = { port } };
                        }
                        else
                        {
                            expression = c.Value;
                        }
                    }
                    else if (wildcard)
                    {
                        at = instance.Location;
                        if (!(scope.Lookup(port) is SignalEntry))
                        {
                            Error(instance.Location, ElaborationCodes.UnknownName,
                                $".* connects port {port} of {instance.Name} to a signal called {port}, and there is none here.");
                            continue;
                        }

                        expression = new NameExpression { Location = instance.Location, Path = { port } };
                    }
                    else
                    {
                        at = instance.Location;
                        expression = null;
                    }

                    if (expression == null)
                    {
                        if (childPort.Port == PortDirection.Input)
                            Warning(at, ElaborationCodes.Unconnected,
                                $"Input {port} of {instance.Name} is not connected, so it reads z.");
                        continue;
                    }

                    if (childPort.Port == PortDirection.Input)
                        ConnectInput(scope, childPort, expression, instance.Name, at);
                    else
                        ConnectOutput(scope, childPort, expression, instance.Name, at);
                }
            }

            private void ConnectInput(Scope scope, SignalEntry port, Expression expression, string instance, SourceLocation at)
            {
                if (expression is NameExpression n && n.Path.Count == 1 && scope.Lookup(n.Name) == null)
                    ImplicitNet(scope, n);

                var builder = new ExpressionBuilder(scope, _diagnostics, constantOnly: false);
                // Given as an assignment gives a value to the port's type: a real stays one (Part L7).
                ExpressionBuilder.TypeInfo type = builder.TypeOf(expression);
                Expr value = builder.BuildAssigned(expression, port.Type, port.Width);
                if (builder.Failed)
                    return;

                HoldConnectionToLibrary(scope, value, at);
                if (type.Width != port.Width && !(Bare(expression) is NumberLiteral))
                    Warning(at, ElaborationCodes.WidthMismatch,
                        $"Input {LastName(port)} of {instance} is {port.Width} bit{(port.Width == 1 ? "" : "s")}, and {Printer.Print(expression)} is {type.Width}.");

                Drive(scope, new List<ExpressionBuilder.Part> { new ExpressionBuilder.Part(port, 0, port.Width) }, value, Delay.Zero, type.Signed, at, "port " + port.FullName);
            }

            private void ConnectOutput(Scope scope, SignalEntry port, Expression expression, string instance, SourceLocation at)
            {
                var builder = new ExpressionBuilder(scope, _diagnostics, constantOnly: false);
                var parts = new List<ExpressionBuilder.Part>();
                if (!builder.TryTarget(expression, parts, n => ImplicitNet(scope, n)))
                    return;

                int width = 0;
                foreach (ExpressionBuilder.Part part in parts)
                    width += part.Width;

                if (width != port.Width)
                    Warning(at, ElaborationCodes.WidthMismatch,
                        $"Output {LastName(port)} of {instance} is {port.Width} bit{(port.Width == 1 ? "" : "s")}, and {Printer.Print(expression)} is {width}.");

                Drive(scope, parts, Expr.Read(port.Signal, port.Width), Delay.Zero, port.Signed, at, "port " + port.FullName);
            }

            private static Expression Bare(Expression e)
            {
                while (e is ParenthesizedExpression p)
                    e = p.Inner;
                return e;
            }

            private static string LastName(SignalEntry s)
            {
                int dot = s.FullName.LastIndexOf('.');
                return dot < 0 ? s.FullName : s.FullName.Substring(dot + 1);
            }

            // -----------------------------------------------------------------
            // Generate
            // -----------------------------------------------------------------

            private void BuildBlock(Scope scope, GenerateBlock block, string name, int depth, string genvar = null, LogicVec genvarValue = default)
            {
                var entry = new InstanceEntry { Declared = block.Location };
                if (!scope.TryDeclare(name, entry))
                {
                    Error(block.Location, ElaborationCodes.Duplicate, $"{name} is the name of something else here already.");
                    return;
                }

                var inner = new Scope(scope.Path + "." + name, scope, scope.Module);
                inner.Index = _builder.Scope(inner.Path, null, ScopeKind.Generate, scope.Recorded);
                entry.Scope = inner;
                if (genvar != null)
                    inner.Set(genvar, new GenvarEntry { Value = genvarValue, Declared = block.Location });

                foreach (ModuleItem item in Flatten(block.Items))
                {
                    if (item is ParameterDeclaration p)
                        DeclareParameter(inner, p, null, null);
                    else if (item is TypedefDeclaration t)
                        DeclareTypedef(inner, t);
                }

                DeclarePass(inner, block.Items, null, isTop: false);
                BuildPass(inner, block.Items, depth);
            }

            private void BuildIf(Scope scope, IfGenerate g, int number, int depth)
            {
                if (!ExpressionBuilder.TryConstant(g.Condition, scope, _diagnostics, out LogicVec condition))
                    return;

                GenerateBlock chosen = LogicVec.Truth(condition) == Logic4.One ? g.Then : g.Else;
                if (chosen == null)
                    return;

                // else if (...): the chain is one construct, and the block chosen in it takes its number.
                if (!chosen.HasBeginEnd && chosen.Items.Count == 1 && chosen.Items[0] is IfGenerate next)
                {
                    BuildIf(scope, next, number, depth);
                    return;
                }

                BuildBlock(scope, chosen, chosen.Label ?? "genblk" + number, depth);
            }

            private void BuildLoop(Scope scope, LoopGenerate loop, int depth)
            {
                int number = ++scope.GenerateConstructs;
                string genvar = loop.Variable;

                if (!loop.DeclaresGenvar && !(scope.Lookup(genvar) is GenvarEntry))
                {
                    Error(loop.Location, scope.Lookup(genvar) == null ? ElaborationCodes.UnknownName : ElaborationCodes.BadParameter,
                        $"A generate loop counts with a genvar: declare genvar {genvar}; first, or write for (genvar {genvar} = ...).");
                    return;
                }

                if (loop.Body == null)
                    return;

                if (!ExpressionBuilder.TryInteger(loop.Start, scope, _diagnostics, "the loop's start", out long value))
                    return;

                string name = loop.Body.Label ?? "genblk" + number;
                var seen = new HashSet<long>();

                for (int iterations = 0; ; iterations++)
                {
                    if (_stopped)
                        return;

                    var here = new Scope(scope.Path, scope, scope.Module);
                    here.Set(genvar, new GenvarEntry { Value = LogicVec.FromInt64(value, 32, true), Declared = loop.Location });

                    if (!ExpressionBuilder.TryConstant(loop.Condition, here, _diagnostics, out LogicVec condition))
                        return;

                    if (LogicVec.Truth(condition) != Logic4.One)
                        return;

                    if (iterations >= MaxIterations)
                    {
                        Error(loop.Location, ElaborationCodes.TooLarge, $"This generate loop goes round more than {MaxIterations} times.");
                        return;
                    }

                    if (!seen.Add(value))
                    {
                        Error(loop.Location, ElaborationCodes.BadParameter, $"This generate loop comes back to {genvar} = {value}, so it would never end.");
                        return;
                    }

                    BuildBlock(scope, loop.Body, $"{name}[{value}]", depth, genvar, LogicVec.FromInt64(value, 32, true));

                    if (!TryStep(loop, here, value, out value))
                        return;
                }
            }

            private bool TryStep(LoopGenerate loop, Scope here, long value, out long next)
            {
                next = value;
                switch (loop.StepOperator)
                {
                    case "++": next = value + 1; return true;
                    case "--": next = value - 1; return true;
                }

                if (loop.StepValue == null)
                    return false;

                if (!ExpressionBuilder.TryInteger(loop.StepValue, here, _diagnostics, "the loop's step", out long step))
                    return false;

                switch (loop.StepOperator)
                {
                    case "=": next = step; return true;
                    case "+=": next = value + step; return true;
                    case "-=": next = value - step; return true;
                    case "*=": next = value * step; return true;
                    default:
                        NotYet(loop.Location, $"A generate loop stepping with {loop.StepOperator} is not supported by BitSorter yet.");
                        return false;
                }
            }
        }
    }
}
