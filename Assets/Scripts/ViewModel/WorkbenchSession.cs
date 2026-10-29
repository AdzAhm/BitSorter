using System;
using System.Collections.Generic;
using BitSorter.Board;
using BitSorter.Sim;
using BitSorter.SystemVerilog;

namespace BitSorter.ViewModel
{
    /// <summary>What a frame of a <see cref="WorkbenchSession"/> did.</summary>
    public enum SessionFrame
    {
        /// <summary>Nothing new to show.</summary>
        Nothing,

        /// <summary>The run shown has more to read: the same run, further on.</summary>
        Grew,

        /// <summary>
        /// A replay took over: <see cref="WorkbenchSession.Run"/> is a new run, of the document as edited,
        /// and what shows the run is built again round it.
        /// </summary>
        Switched,
    }

    /// <summary>
    /// The document the workbench shows, kept for editing and undo, its run, and the run of it as edited
    /// while that is being made (Part 9b-3).
    /// </summary>
    /// <remarks>
    /// **An edit is replayed beside the run it replaces.** The run shown is the document's before the
    /// edit; the edited document is compiled and run a slice a frame (<see cref="RunSession"/>), as the
    /// first run was, and takes over only once it has reached the moment shown -- run on as far as the
    /// shown run had been, past a <c>$stop</c> -- so the waveform never goes blank and the playhead
    /// never jumps. A replay is the whole run again rather than a run picked up from the edit, since the
    /// testbench is one <c>initial</c> block that drives every input (a replay means compiling again);
    /// an input changes nothing before it changes, so what comes before the edit is the same history,
    /// and <c>WorkbenchSessionTests</c> holds every replay to that.
    ///
    /// **The latest edit wins**: one made while a replay is still being made drops that replay and
    /// starts its own, of the document with both edits in. Undo and redo are edits like any other. A
    /// change of unit is one too: it rescales nothing, but every time on screen and every delay drawn is
    /// written in it, so the run that writes them is made again. **RESTART takes a replay at once**
    /// (<see cref="TakeReplay"/>): the moment it goes to, 0, is made already.
    ///
    /// **The canvas's edits (Part 10a) are drawn at once.** A part placed or taken away replays as an input
    /// does, and is drawn over the run there is straight away (<see cref="RunSession.Relayout"/>) rather than
    /// when its replay takes over. A part moved, turned or mirrored changes nothing the circuit does, so it
    /// is only drawn again: no replay, and the run goes on. <see cref="LayoutVersion"/> counts each.
    /// </remarks>
    public sealed class WorkbenchSession
    {
        private const double Epsilon = 1e-9;

        private readonly Func<double> _now;
        private readonly int _chunkEvents;
        private readonly double _budgetMilliseconds;

        /// <summary>Whether the replay has been asked to run on as far as the shown run's timeline.</summary>
        private bool _replayRanOn;

        // The instances stepped through from the top, outermost first (Part 12a); and beside each, the module of a step taken
        // through written code (Part 15h-4), which no block says -- null for a step into a block.
        private readonly List<string> _path = new List<string>();
        private readonly List<string> _codeSteps = new List<string>();

        /// <summary>
        /// <paramref name="project"/>, compiled and its first slice run, each slice timed by
        /// <paramref name="nowMilliseconds"/>. Throws when the document does not compile.
        /// </summary>
        public WorkbenchSession(Project project, Func<double> nowMilliseconds,
            int chunkEvents = RunSlicer.ChunkEvents, double budgetMilliseconds = RunSlicer.BudgetMilliseconds)
        {
            _now = nowMilliseconds ?? throw new ArgumentNullException(nameof(nowMilliseconds));
            _chunkEvents = chunkEvents;
            _budgetMilliseconds = budgetMilliseconds;
            Editor = new BoardEditor(project);
            Run = RunSession.Open(project, _now, _chunkEvents, _budgetMilliseconds, LayoutOf);
        }

        /// <summary>The document, and its undo; every change goes through it, and through this session's own edits for a replay.</summary>
        public BoardEditor Editor { get; }

        public Project Project => Editor.Project;

        /// <summary>The unit the document counts its time in.</summary>
        public string Unit => Project.Settings.TimeUnit;

        /// <summary>The run shown: the document's, or, while <see cref="Replay"/> is being made, the document's before its latest edit.</summary>
        public RunSession Run { get; private set; }

        /// <summary>The run of the document as edited, being made, which takes over <see cref="Run"/> once it has reached the moment shown; null when there is none.</summary>
        public RunSession Replay { get; private set; }

        /// <summary>Whether a replay is being made.</summary>
        public bool Replaying => Replay != null;

        /// <summary>The moment the latest edit changes the run from: before it, the replay's history is the shown run's.</summary>
        public long ChangedFrom { get; private set; }

        /// <summary>How many replays have taken over: what is built round a run checks this to know when to build again.</summary>
        public int Version { get; private set; }

        /// <summary>How many times the run shown has been drawn again round an edit (Part 10a): what tells the view the drawing changed under it.</summary>
        public int LayoutVersion { get; private set; }

        /// <summary>The module the canvas edits: the design's top, or the module of the instance stepped into last (Part 12a).</summary>
        public string Module => ModuleOn(Project, _path, out _, _codeSteps);

        // -----------------------------------------------------------------
        // Stepping inside a block (Part 12a)
        // -----------------------------------------------------------------

        /// <summary>
        /// The instances stepped through from the top, outermost first -- <c>h1</c>, then whatever is inside it -- and
        /// empty at the top. The canvas draws and edits the last one's module, its wires showing that instance's
        /// values; the run is still the whole design's.
        /// </summary>
        public IReadOnlyList<string> Path => _path;

        /// <summary>How many times the canvas has stepped in or out: what tells the view it shows another module, rather than the same one edited.</summary>
        public int StepVersion { get; private set; }

        /// <summary>
        /// Steps inside the block <paramref name="part"/> of the module shown: false for anything that is not a block
        /// of a module the document has. **Editing inside edits the module**, so every instance of it changes, as in
        /// SystemVerilog.
        /// </summary>
        public bool StepInto(int part)
        {
            if (!CanStepInto(part))
                return false;

            // A block of the drawing of written code (Part 15h-4): the instance its code makes, which no block of the module is.
            if (ShownDrawing != null)
            {
                _path.Add(ShownDrawing.InstanceOf(part));
                _codeSteps.Add(((BlockPart)ShownDrawing.Module.FindPart(part)).Module);
                Stepped();
                return true;
            }

            var block = (BlockPart)Project.Find(Module).FindPart(part);
            _path.Add(block.Copies > 1 ? block.Name + "[0]" : block.Name);   // an array is stepped into at its first copy (Part 12c)
            _codeSteps.Add(null);
            Stepped();
            return true;
        }

        /// <summary>
        /// Whether <paramref name="part"/> of what the circuit's window draws is a block <see cref="StepInto"/> steps inside:
        /// one of a module the document has -- on the drawing of written code (Part 15h-4), an instance its code makes.
        /// </summary>
        public bool CanStepInto(int part)
        {
            if (ShowsDrawingOfCode)
                return ShownDrawing?.Module.FindPart(part) is BlockPart inCode && Project.Find(inCode.Module) != null
                    && ShownDrawing.InstanceOf(part) != null;
            return Project.Find(Module)?.FindPart(part) is BlockPart block && !string.IsNullOrEmpty(block.Name) && Project.Find(block.Module) != null;
        }

        /// <summary>
        /// The project and module the circuit's window draws, what a point of it is on: the module shown, or over a written
        /// module, the drawing of its code (Part 15h-4) -- and none while its code is shown.
        /// </summary>
        public Project DrawnProject => ShownDrawing?.Project ?? Project;

        /// <inheritdoc cref="DrawnProject"/>
        public Module DrawnModule => ShowsDrawingOfCode ? ShownDrawing?.Module : Project.Find(Module);

        /// <summary>Steps <paramref name="levels"/> out, no further than the top: false at the top already.</summary>
        public bool StepOut(int levels = 1)
        {
            if (_path.Count == 0 || levels < 1)
                return false;
            levels = Math.Min(levels, _path.Count);
            _path.RemoveRange(_path.Count - levels, levels);
            _codeSteps.RemoveRange(_codeSteps.Count - levels, levels);
            Stepped();
            return true;
        }

        /// <summary>
        /// Steps to the instance at <paramref name="scope"/>, a path of the design -- <c>full_adder_tb.dut.h1</c>, as the
        /// hierarchy lists it -- or to the top for the design's own path. False for a scope that is not the design or a
        /// block inside it: the testbench, or a cell a gate with a delay is printed as.
        /// </summary>
        public bool StepToScope(string scope)
        {
            CompiledProject compiled = BoardCompiler.Compile(Project);
            if (!compiled.Succeeded || scope == null)
                return false;
            if (scope == compiled.DesignPath)
                return _path.Count == 0 || StepOut(_path.Count);
            if (!scope.StartsWith(compiled.DesignPath + ".", StringComparison.Ordinal))
                return false;

            // A copy of an array is its generate block, name[i], and the one instance in it (Part 12c): one step either way.
            string[] parts = scope.Substring(compiled.DesignPath.Length + 1).Split('.');
            var names = new List<string>();
            for (int i = 0; i < parts.Length; i++)
            {
                names.Add(parts[i]);
                if (parts[i].EndsWith("]", StringComparison.Ordinal) && i + 1 < parts.Length && parts[i + 1] == BoardCompiler.ArrayInstance)
                    i++;
            }

            // Blocks all the way, as before code could be stepped through; past written code, the design says the way.
            var codeSteps = new List<string>();
            ModuleOn(Project, names, out int valid);
            if (valid == names.Count)
                codeSteps.AddRange(new string[names.Count]);
            else if (!WayThroughCode(compiled, scope, names, codeSteps))
                return false;
            _path.Clear();
            _path.AddRange(names);
            _codeSteps.Clear();
            _codeSteps.AddRange(codeSteps);
            Stepped();
            return true;
        }

        /// <summary>
        /// The steps from the top to the instance at <paramref name="scope"/>, found in the elaboration of the code as it is now
        /// (Part 15h-4): in a drawn module, a block -- an array's copy <c>name[i]</c>, and the one instance in it, one step --
        /// and in a written one, the outermost instance on the way, named from where it stands as the design names it,
        /// <c>h1</c> or <c>gen[2].u</c>, with the module it is of. False when there is no such instance.
        /// </summary>
        private bool WayThroughCode(CompiledProject compiled, string scope, List<string> steps, List<string> codeSteps)
        {
            steps.Clear();
            codeSteps.Clear();
            Design design = DesignOf(Project, compiled);
            if (design == null || !_scopes.TryGetValue(scope, out int goal) || design.Scopes[goal].Kind != ScopeKind.Instance)
                return false;

            string at = compiled.DesignPath;
            Module module = Project.TopModule;
            while (at != scope)
            {
                if (module == null || !scope.StartsWith(at + ".", StringComparison.Ordinal))
                    return false;
                if (module.IsWritten)
                {
                    ScopeInfo next = null;
                    for (int s = goal; s >= 0 && design.Scopes[s].Path != at; s = design.Scopes[s].Parent)
                        if (design.Scopes[s].Kind == ScopeKind.Instance)
                            next = design.Scopes[s];
                    if (next == null || !next.Path.StartsWith(at + ".", StringComparison.Ordinal) || !(Project.Find(next.Module) is Module of))
                        return false;
                    steps.Add(next.Path.Substring(at.Length + 1));
                    codeSteps.Add(next.Module);
                    at = next.Path;
                    module = of;
                    continue;
                }

                string rest = scope.Substring(at.Length + 1);
                int dot = rest.IndexOf('.');
                string step = dot < 0 ? rest : rest.Substring(0, dot);
                if (!BlockStep(Project, module, step, out Module inner, out bool copy))
                    return false;
                steps.Add(step);
                codeSteps.Add(null);
                at += copy ? "." + step + "." + BoardCompiler.ArrayInstance : "." + step;
                module = inner;
            }
            return true;
        }

        /// <summary>
        /// Each step's module where it was taken through written code (Part 15h-4) -- null for a step into a block -- side by
        /// side with <see cref="Path"/>, and checked against the code again whenever the document is drawn again.
        /// </summary>
        public IReadOnlyList<string> CodeSteps => _codeSteps;

        /// <summary>
        /// How many of the path's steps still lead somewhere: each block a block of the module before it, each step through
        /// code an instance the code as it is now makes -- its module read again from the design, for an edit, an undo or a redo
        /// may have changed which module the code makes it of (the review of #109).
        /// </summary>
        private int ValidSteps(CompiledProject compiled)
        {
            ModuleOn(Project, _path, out int valid, _codeSteps);
            if (!compiled.Succeeded || !_codeSteps.Exists(m => m != null) && valid == _path.Count)
                return valid;

            Design design = DesignOf(Project, compiled);
            string at = compiled.DesignPath;
            Module module = Project.TopModule;
            for (int i = 0; i < _path.Count; i++)
            {
                if (module == null)
                    return i;
                if (module.IsWritten)
                {
                    if (design == null || !_scopes.TryGetValue(at + "." + _path[i], out int s) || design.Scopes[s].Kind != ScopeKind.Instance
                        || !(Project.Find(design.Scopes[s].Module) is Module of))
                        return i;
                    _codeSteps[i] = of.Name;
                    at = design.Scopes[s].Path;
                    module = of;
                    continue;
                }

                if (!BlockStep(Project, module, _path[i], out Module inner, out bool copy))
                    return i;
                _codeSteps[i] = null;
                at += copy ? "." + _path[i] + "." + BoardCompiler.ArrayInstance : "." + _path[i];
                module = inner;
            }
            return _path.Count;
        }

        /// <summary>Whether <paramref name="step"/> is a block of <paramref name="module"/>'s -- an array's at one of its copies -- and the module inside.</summary>
        private static bool BlockStep(Project project, Module module, string step, out Module inner, out bool copy)
        {
            inner = null;
            copy = false;
            if (!TryCopy(step, out string name, out int index) || !(module.FindPart(name) is BlockPart block)
                || !(project.Find(block.Module) is Module of) || (block.Copies > 1) != (index >= 0) || index >= block.Copies)
                return false;
            inner = of;
            copy = index >= 0;
            return true;
        }

        // The code elaborated as it is now, made once for the document's modules, and its scopes by their paths: what steps
        // through code are found and checked in, and what the drawing of code is drawn from.
        private IReadOnlyList<Module> _designModules;
        private Design _design;
        private readonly Dictionary<string, int> _scopes = new Dictionary<string, int>(StringComparer.Ordinal);

        private Design DesignOf(Project project, CompiledProject compiled)
        {
            if (ReferenceEquals(_designModules, project.Modules))
                return _design;
            Elaboration elaboration = Elaborator.Elaborate(compiled.Tree, compiled.Top);
            _design = elaboration.HasErrors ? null : elaboration.Design;
            _designModules = project.Modules;
            _scopes.Clear();
            if (_design != null)
                for (int i = 0; i < _design.Scopes.Count; i++)
                    _scopes[_design.Scopes[i].Path] = i;
            return _design;
        }

        /// <summary>How many instances of <paramref name="module"/> the design has: what an edit inside it changes.</summary>
        public int InstancesOf(string module)
        {
            int count = 0;
            foreach (ScopeInfo scope in Run.Model.Session.Design.Scopes)
                if (scope.Kind == ScopeKind.Instance && scope.Module == module)
                    count++;
            return count;
        }

        /// <summary>
        /// The module at the end of <paramref name="path"/> from the top of <paramref name="project"/>, and how many of
        /// its steps were blocks of modules the document has: the module of the last of those.
        /// </summary>
        public static string ModuleOn(Project project, IReadOnlyList<string> path, out int valid, IReadOnlyList<string> codeSteps = null)
        {
            Module module = project.TopModule;
            valid = 0;
            if (module == null)
                return project.Top;
            for (int i = 0; i < path.Count; i++)
            {
                // Inside written code a step is an instance its code makes (Part 15h-4), of the module noted beside it -- asked
                // every frame, so read, not found (the review of #109: it allocated a key a step).
                if (module.IsWritten)
                {
                    if (codeSteps == null || i >= codeSteps.Count || codeSteps[i] == null || !(project.Find(codeSteps[i]) is Module of))
                        break;
                    module = of;
                    valid++;
                    continue;
                }

                if (!BlockStep(project, module, path[i], out Module inner, out _))
                    break;
                module = inner;
                valid++;
            }

            return module.Name;
        }

        /// <summary>A step of a path: a block's name, or an array's and the copy, <c>fa[3]</c> (Part 12c); -1 for no copy.</summary>
        private static bool TryCopy(string step, out string name, out int copy)
        {
            copy = -1;
            name = step;
            int open = step.IndexOf('[');
            if (open < 0)
                return true;
            name = step.Substring(0, open);
            return step.EndsWith("]", StringComparison.Ordinal)
                && int.TryParse(step.Substring(open + 1, step.Length - open - 2), System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out copy);
        }

        /// <summary>The design path of the instance stepped into, under the design's <paramref name="designPath"/>.</summary>
        private string InstancePathUnder(string designPath)
        {
            string path = designPath;
            for (int i = 0; i < _path.Count; i++)
            {
                string step = _path[i];
                bool inCode = i < _codeSteps.Count && _codeSteps[i] != null;
                path += step.EndsWith("]", StringComparison.Ordinal) && !inCode ? "." + step + "." + BoardCompiler.ArrayInstance : "." + step;
            }
            return path;
        }

        /// <summary>The drawing of the module stepped into, its signals read under the instance stepped through.</summary>
        private SchematicLayout LayoutOf(Project project, CompiledProject compiled)
        {
            double started = _now();
            string shown = ModuleOn(project, _path, out _, _codeSteps);
            SchematicLayout layout = null;
            _shownDrawing = null;
            if (project.Find(shown)?.IsWritten == true && _drawnCode.Contains(shown))
                layout = DrawingLayout(project, compiled, InstancePathUnder(compiled.DesignPath), shown);
            layout ??= DocumentSchematic.Build(project, compiled, shown, InstancePathUnder(compiled.DesignPath),
                m => RoutesOf(project, m));
            _slowest = Math.Max(_slowest, _now() - started);
            return layout;
        }

        private double _slowest;

        /// <summary>
        /// The slowest the document took to draw, or its timing to work out, since this was last asked -- by the session's
        /// clock, in milliseconds -- and forgotten as it is told: what tells a browser it is slow (<see cref="DesktopHint"/>).
        /// The slowest, not the last: an edit draws the document twice, the second time from routes the first made, and the
        /// last alone said 6 ms of an edit that took 500 (the 15g-7 review).
        /// </summary>
        public double TakeSlowestMilliseconds()
        {
            double slowest = _slowest;
            _slowest = 0;
            return slowest;
        }

        private IReadOnlyList<Module> _routedModules;
        private Module _routedModule;
        private Dictionary<int, IReadOnlyList<Point>> _routes;

        /// <summary>
        /// Every wire of <paramref name="module"/> routed, once a document (Part 15g): the drawing, the canvas's hit map and a
        /// wire being drawn each routed the whole module for themselves, two and three times an edit. Kept by the module and
        /// the project's modules -- a block's outline is its module's -- so an edit to anything else, an input's steps, keeps
        /// them. Shared: nothing that asks may change what it is given.
        /// </summary>
        public IReadOnlyDictionary<int, IReadOnlyList<Point>> RoutesOf(Project project, Module module)
        {
            if (!ReferenceEquals(module, _routedModule) || !ReferenceEquals(project.Modules, _routedModules))
            {
                _routes = WireGeometry.Wires(project, module);
                _routedModule = module;
                _routedModules = project.Modules;
            }
            return _routes;
        }

        private void Stepped()
        {
            Relayout();
            StepVersion++;
        }

        // -----------------------------------------------------------------
        // The canvas's edits (Part 10a)
        // -----------------------------------------------------------------

        // -----------------------------------------------------------------
        // Written blocks (Part 12d)
        // -----------------------------------------------------------------

        private CodeDocument _code;

        /// <summary>Whether the module shown is written: the circuit's window shows its text rather than a drawing, unless it shows the drawing of its code.</summary>
        public bool ShowsCode => ShowsWritten && !_drawnCode.Contains(Module);

        /// <summary>Whether the module shown is written, as its code or as the drawing of its code (Part 15h-4).</summary>
        public bool ShowsWritten => Project.Find(Module)?.IsWritten == true;

        /// <summary>
        /// Whether the written module shown is shown as the drawing of its code (Part 15h-4): what it builds, as the circuit's
        /// window draws any circuit, its values the run's. Remembered for each module.
        /// </summary>
        public bool ShowsDrawingOfCode => ShowsWritten && _drawnCode.Contains(Module);

        private readonly HashSet<string> _drawnCode = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>
        /// Whether the drawing of code shown draws its small operators and muxes as their gates (Part 15h-4, GATES): a sum of
        /// 4-bit numbers as the textbook's ripple of full adders. Remembered for each module, as the drawing is.
        /// </summary>
        public bool ShowsGatesOfCode => ShowsDrawingOfCode && _gatesOfCode.Contains(Module);

        private readonly HashSet<string> _gatesOfCode = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>How many operators and muxes the drawing of code shown draws as their gates: none while GATES is off, or none is small enough.</summary>
        public int GatesOpened => ShowsGatesOfCode ? ShownDrawing?.Opened ?? 0 : 0;

        /// <summary>Draws the drawing of code shown with its small operators and muxes as their gates, or as their boxes again.</summary>
        public void DrawGates(bool gates)
        {
            if (!ShowsDrawingOfCode)
                return;
            if (gates ? !_gatesOfCode.Add(Module) : !_gatesOfCode.Remove(Module))
                return;
            Stepped();   // drawn afresh and fitted, as DRAWING is
        }

        /// <summary>Shows the written module shown as the drawing of its code, or as its code again.</summary>
        public void DrawCode(bool drawn)
        {
            if (!ShowsWritten)
                return;
            if (drawn ? !_drawnCode.Add(Module) : !_drawnCode.Remove(Module))
                return;
            Stepped();   // as a step inside: drawn afresh and fitted, not kept in place (the review of #108)
        }

        /// <summary>What an edit on the drawing of code is told: it is the code's, and changes with it.</summary>
        public const string DrawingRefusal = "This is the drawing of the code: change the code to change it.";

        private EditResult? OnTheDrawing => ShowsDrawingOfCode ? EditResult.Refused(DrawingRefusal) : (EditResult?)null;

        private IReadOnlyList<Module> _drawnModules;
        private string _drawnPath;
        private bool _drawnGates;
        private RtlDrawing _drawing;

        /// <summary>The drawing of code the circuit's window shows now; null for none (the review of #109: a stale one was stepped through).</summary>
        private RtlDrawing ShownDrawing => ShowsDrawingOfCode ? _shownDrawing : null;

        private RtlDrawing _shownDrawing;

        /// <summary>
        /// The drawing of the written module at <paramref name="path"/>, made once for the document's modules and the instance
        /// stepped into: its code elaborated as the run elaborates it, its netlist drawn -- with <paramref name="gates"/>, its
        /// small operators and muxes as their gates, from the same netlist.
        /// </summary>
        private RtlDrawing DrawingOf(Project project, CompiledProject compiled, string path, string module, bool gates)
        {
            bool same = ReferenceEquals(_drawnModules, project.Modules) && _drawnPath == path && _drawing?.Module.Name == module;
            if (same && _drawnGates == gates)
                return _drawing;
            if (!same)
            {
                _drawing = null;
                _drawnModules = null;
                Design design = DesignOf(project, compiled);
                if (design == null)
                    return null;
                RtlNetlist netlist = RtlExtractor.Of(design, path);
                if (netlist == null)
                    return null;
                _drawnNetlist = netlist;
                _drawnDesign = design;
            }
            _drawing = RtlDrawing.Of(project, _drawnNetlist, module, gates);
            _drawnGates = gates;
            _drawnModules = project.Modules;
            _drawnPath = path;
            return _drawing;
        }

        private RtlNetlist _drawnNetlist;
        private Design _drawnDesign;

        /// <summary>The drawing of code laid out, its wires no signal holds valued from the netlist.</summary>
        private SchematicLayout DrawingLayout(Project project, CompiledProject compiled, string path, string module)
        {
            RtlDrawing drawing = DrawingOf(project, compiled, path, module, _gatesOfCode.Contains(module));
            if (drawing == null)
                return null;
            Netlist nets = Netlist.Of(drawing.Project, drawing.Module);
            SchematicLayout layout = DocumentSchematic.Build(drawing.Project, drawing.Module, nets, path, compiled.DesignPath, compiled.Cells,
                m => RoutesOf(drawing.Project, m));
            var rtlNetOf = new int[layout.Nets.Count];
            for (int i = 0; i < rtlNetOf.Length; i++)
            {
                Net net = nets.Named(layout.Nets[i].Name);
                int wire = net != null && net.Wires.Count > 0 ? net.Wires[0] : -1;
                int inner = wire >= 0 ? drawing.InnerOfWire(wire) : -1;
                rtlNetOf[i] = wire < 0 ? -1 : inner >= 0 ? -2 - inner : drawing.NetOfWire(wire);
            }
            layout.Drawn = new DrawnValues(_drawnNetlist, rtlNetOf, _drawnDesign, drawing);
            _shownDrawing = drawing;
            return layout;
        }

        /// <summary>The text of the written module shown, as its editor has it; null while the module shown is drawn.</summary>
        public CodeDocument Code
        {
            get
            {
                if (!ShowsCode)
                    return ShowsDrawingOfCode && _code?.Module == Module ? null : _code = null;   // its text kept while its drawing is shown
                if (_code == null || _code.Module != Module)
                    _code = new CodeDocument(this, Module);
                return _code;
            }
        }

        private CodeListing _listing = CodeListing.Empty;

        /// <summary>
        /// The SystemVerilog the document prints as (Part 12e), for the dock's CODE tab: printed again only once the document
        /// has changed, so asking every frame costs nothing between edits.
        /// </summary>
        public CodeListing Listing
        {
            get
            {
                if (!ReferenceEquals(_listing.Project, Project))
                    _listing = CodeListing.Of(Project);
                return _listing;
            }
        }

        private TimingView _timing = TimingView.Empty;

        private IReadOnlyList<BoardDiagnostic> _diagnostics = Array.Empty<BoardDiagnostic>(), _diagnosticsFrom;
        private string _overBudgetFrom;

        /// <summary>
        /// What the TRANSCRIPT lists of the document (Part 14g): its diagnostics, and while its library has a gate budget the
        /// design is over, one warning more saying so, from the TIMING tab's own count (<see cref="TimingView.OverBudget"/>).
        /// The same list until either changes, so the transcript is made again only then; and the timing is worked out for it
        /// only while there is a budget.
        /// </summary>
        public IReadOnlyList<BoardDiagnostic> Diagnostics
        {
            get
            {
                IReadOnlyList<BoardDiagnostic> document = Editor.Diagnostics;
                string over = OverBudget;
                if (over == null)
                    return document;
                if (!ReferenceEquals(document, _diagnosticsFrom) || over != _overBudgetFrom)
                {
                    _diagnostics = new List<BoardDiagnostic>(document) { new BoardDiagnostic(BitSorter.SystemVerilog.Syntax.DiagnosticSeverity.Warning, null, over) };
                    _diagnosticsFrom = document;
                    _overBudgetFrom = over;
                }

                return _diagnostics;
            }
        }

        /// <summary>The warning that the design is over its library's gate budget, or null (<see cref="TimingView.OverBudget"/>).</summary>
        public string OverBudget => (Project.Settings?.Library?.GateBudget ?? -1) < 0 ? null : Timing.OverBudget;

        /// <summary>
        /// The document's timing (Part 14b), for the dock's TIMING tab: worked out again only once the document has changed,
        /// as <see cref="Listing"/> is printed, so asking every frame costs nothing between edits.
        /// </summary>
        public TimingView Timing
        {
            get
            {
                // A part moved or a stimulus edited times as before, and costs nothing: the design is worked out again only
                // once it has changed (the 14b review timed a processor at 200 ms a time).
                if (!ReferenceEquals(_timing.Project, Project))
                {
                    if (TimingView.SameDesign(_timing.Project, Project))
                        _timing = _timing.For(Project);
                    else
                    {
                        double started = _now();
                        _timing = TimingView.Of(Project);
                        _slowest = Math.Max(_slowest, _now() - started);
                    }
                }
                return _timing;
            }
        }

        /// <summary>The written module shown given <paramref name="source"/> (<see cref="BoardEditor.SetSource"/>): one step, drawn now, and replayed.</summary>
        public EditResult SetSource(string source) => Edited(Editor.SetSource(Module, source));

        /// <summary>
        /// A new written module called <paramref name="name"/>, from the template (<see cref="BoardEditor.AddWrittenModule"/>):
        /// one step. Nothing uses it yet, so the run is as it was; PARTS offers a block of it.
        /// </summary>
        public EditResult AddWrittenModule(string name) => Editor.AddWrittenModule(name);

        /// <summary>A module put in whole in place of the one of its name (<see cref="BoardEditor.ReplaceModule"/>, Part 15e), and replayed.</summary>
        public EditResult ReplaceModule(Module module) => Edited(Editor.ReplaceModule(module));

        /// <summary>
        /// The module <paramref name="name"/> taken out of the project, its code or its drawing with it
        /// (<see cref="BoardEditor.RemoveModule"/>): one step. Nothing the run makes uses it, or it could not go, so the run
        /// is made again only for the written testbench, whose going hands the inputs back to the stimulus.
        /// </summary>
        public EditResult RemoveModule(string name)
        {
            bool bench = name == Project.Bench;
            EditResult result = Editor.RemoveModule(name);
            return bench ? Edited(result) : Laid(result);
        }

        /// <summary>Why <paramref name="name"/> cannot be taken out -- something uses it -- or null when it can (<see cref="BoardEditor.WhyNotRemoveModule"/>).</summary>
        public string WhyNotRemoveModule(string name) => Editor.WhyNotRemoveModule(name);

        /// <summary>Places what <paramref name="entry"/> offers at <paramref name="at"/> (<see cref="PartsCatalog.Place"/>): drawn now, and replayed.</summary>
        public EditResult Place(CatalogEntry entry, Placement at) => OnTheDrawing ?? Edited(PartsCatalog.Place(Editor, Module, entry, at));

        /// <summary>
        /// The parts of the module shown made into a block called <paramref name="name"/> (<see cref="BlockMaker.Make"/>, Part
        /// 12b): drawn now, and replayed -- the circuit doing exactly what it did. The result's id is the new instance's.
        /// </summary>
        public EditResult MakeBlock(IReadOnlyCollection<int> parts, string name) => OnTheDrawing ?? Edited(BlockMaker.Make(Editor, Module, parts, name));

        /// <summary>Takes parts and wires away (<see cref="BoardEditor.RemoveParts"/>): drawn now, and replayed.</summary>
        public EditResult Remove(IEnumerable<int> parts, IEnumerable<int> wires = null) => OnTheDrawing ?? Edited(Editor.RemoveParts(Module, ExerciseKeeps(parts), wires));

        /// <summary>
        /// <paramref name="parts"/> less an exercise's ports, at its top (Part 15d): they are its test's, so a select-all and
        /// DEL, or a cut, takes everything round them and leaves them where they were (the #88 review: it was refused whole).
        /// </summary>
        private IEnumerable<int> ExerciseKeeps(IEnumerable<int> parts)
        {
            if (parts == null || Project.Settings?.Exercise == null || Module != Project.Top)
                return parts;
            Module top = Project.TopModule;
            var kept = new List<int>();
            foreach (int id in parts)
                if (!(top?.FindPart(id) is PortPart))
                    kept.Add(id);
            return kept;
        }

        /// <summary>Draws a wire between two ends, a pin or a tap each, through <paramref name="route"/>'s bends (<see cref="BoardEditor.Connect"/>; Part 10b): drawn now, and replayed.</summary>
        public EditResult Connect(Endpoint from, Endpoint to, Route route = null) => OnTheDrawing ?? Edited(Editor.Connect(Module, from, to, route));

        /// <summary>What the inspector's fields were changed to (<see cref="Inspector.Apply"/>; Part 10c), as one step: drawn now, and replayed.</summary>
        public EditResult Inspect(Inspection inspection, IReadOnlyDictionary<string, string> typed)
        {
            if (OnTheDrawing is EditResult refused)
                return refused;
            Project before = Project;
            EditResult result = Inspector.Apply(Editor, Module, inspection, typed);
            return ReferenceEquals(before, Project) ? result : Edited(result);
        }

        /// <summary><paramref name="clip"/> pasted (<see cref="Clipboard.Paste"/>; Part 10d), one step: drawn now, and replayed.</summary>
        public EditResult Paste(Clip clip, List<int> parts, List<int> wires) => OnTheDrawing ?? Edited(Clipboard.Paste(Editor, Module, clip, parts, wires));

        /// <summary>
        /// <paramref name="wires"/> put back on the automatic route, their bends forgotten and a tap's place along its wire
        /// kept (Part 10d), as one step: drawn again, and nothing replayed.
        /// </summary>
        public EditResult AutoRoute(IReadOnlyCollection<int> wires)
        {
            if (OnTheDrawing is EditResult refused)
                return refused;
            Module module = Project.Find(Module);
            if (module == null || wires == null || wires.Count == 0)
                return EditResult.Refused("There is no wire selected to route again.");

            return Laid(Editor.Group(() =>
            {
                foreach (int wire in wires)
                {
                    double tapAt = module.Layout.Routes.TryGetValue(wire, out Route kept) ? kept.TapAt : 0.5;
                    EditResult done = Editor.Reroute(Module, wire, new Route { TapAt = tapAt });
                    if (!done.Done)
                        return done;
                }

                return EditResult.Ok();
            }));
        }

        /// <summary>Changes how a wire runs, its bends (<see cref="BoardEditor.Reroute"/>; Part 10b-2): drawn again, and nothing replayed.</summary>
        public EditResult Reroute(int wire, Route route) => OnTheDrawing ?? Laid(Editor.Reroute(Module, wire, route));

        /// <summary>Moves, turns or mirrors parts (<see cref="BoardEditor.MoveParts"/>): drawn again, and nothing replayed.</summary>
        public EditResult Move(IReadOnlyDictionary<int, Placement> to, bool continuing = false) => OnTheDrawing ?? Laid(Editor.MoveParts(Module, to, continuing));

        /// <summary>
        /// Turns the parts <paramref name="parts"/> by <paramref name="quarterTurns"/>, counter-clockwise, about their
        /// middle -- or, <paramref name="mirror"/>, mirrors them left to right about it -- as one: each part turned
        /// on its own place, and the places turned round the middle, which is kept on the grid so that every
        /// place on it stays there. Drawn again; nothing replayed.
        /// </summary>
        public EditResult Turn(IReadOnlyCollection<int> parts, int quarterTurns, bool mirror = false)
        {
            if (OnTheDrawing is EditResult refused)
                return refused;
            Module module = Project.Find(Module);
            if (module == null || parts == null || parts.Count == 0)
                return EditResult.Refused("There is nothing selected to turn.");

            double left = double.MaxValue, right = double.MinValue, bottom = double.MaxValue, top = double.MinValue;
            foreach (int part in parts)
            {
                Board.Point p = module.Layout.PlacementOf(part).Position;
                left = Math.Min(left, p.X);
                right = Math.Max(right, p.X);
                bottom = Math.Min(bottom, p.Y);
                top = Math.Max(top, p.Y);
            }

            double cx = CanvasSnap.Snap((left + right) / 2), cy = CanvasSnap.Snap((bottom + top) / 2);
            var to = new Dictionary<int, Placement>();
            foreach (int part in parts)
            {
                Placement was = module.Layout.PlacementOf(part);
                double x = was.Position.X - cx, y = was.Position.Y - cy;
                if (mirror)
                {
                    // Mirrored on screen, however it was turned: the document mirrors before it turns.
                    to[part] = new Placement(new Board.Point(cx - x, cy + y), 4 - was.QuarterTurns, !was.Flipped);
                    continue;
                }

                int turns = ((quarterTurns % 4) + 4) % 4;
                for (int i = 0; i < turns; i++)
                {
                    double turned = -y;
                    y = x;
                    x = turned;
                }

                to[part] = new Placement(new Board.Point(cx + x, cy + y), was.QuarterTurns + quarterTurns, was.Flipped);
            }

            return Laid(Editor.MoveParts(Module, to));
        }

        /// <summary>A circuit edit made: drawn over the run there is, and replayed from <paramref name="from"/>.</summary>
        private EditResult Edited(EditResult result, long from = 0)
        {
            if (!result.Done)
                return result;
            Relayout();
            StartReplay(from);
            return result;
        }

        /// <summary>An edit to where things are drawn made: drawn again, the run and any replay going on.</summary>
        private EditResult Laid(EditResult result)
        {
            if (result.Done)
                Relayout();
            return result;
        }

        /// <summary>
        /// The run shown, and any replay, drawn again round the document as it is now. An instance stepped through that
        /// the edit took away -- or an undo did -- is stepped out of, to the last one still there.
        /// </summary>
        private void Relayout()
        {
            CompiledProject compiled = BoardCompiler.Compile(Project);
            int valid = ValidSteps(compiled);
            if (valid < _path.Count)
            {
                _path.RemoveRange(valid, _path.Count - valid);
                _codeSteps.RemoveRange(valid, _codeSteps.Count - valid);
                StepVersion++;
            }

            if (!compiled.Succeeded)
                return;   // a document the rules let through compiles; this is only never-throw

            SchematicLayout layout = LayoutOf(Project, compiled);
            Run.Relayout(layout);
            Replay?.Relayout(layout);
            LayoutVersion++;
        }

        // -----------------------------------------------------------------
        // Edits, each replayed
        // -----------------------------------------------------------------

        /// <summary>An input given <paramref name="value"/> from <paramref name="from"/> until <paramref name="until"/> (<see cref="BoardEditor.SetInput"/>), and replayed.</summary>
        public EditResult SetInput(string port, long from, long until, LogicVec value) =>
            Replayed(Editor.SetInput(port, from, until, value), from);

        /// <summary>A one-bit input flipped over the stretch <paramref name="at"/> is in (<see cref="BoardEditor.InvertInput(string, long, out long)"/>), and replayed.</summary>
        public EditResult InvertInput(string port, long at)
        {
            EditResult result = Editor.InvertInput(port, at, out long from);
            return Replayed(result, from);
        }

        /// <summary>
        /// The stimulus made <paramref name="stimulus"/> -- an edit from the INPUTS table (Part 15g, <see cref="InputTable"/>) --
        /// one step, replayed from <paramref name="from"/>, as SET's is. Refused with a written testbench, which drives the
        /// inputs itself, and in an exercise, whose own test does (<see cref="BoardEditor.ExerciseDrivesTheInputs"/>).
        /// </summary>
        public EditResult SetStimulus(BitSorter.Board.Stimulus stimulus, long from)
        {
            if (Project.Bench != null)
                return EditResult.Refused(BoardRules.BenchDrivesTheInputs(Project));
            return Replayed(Editor.SetStimulus(stimulus), from);
        }

        /// <summary>
        /// The written testbench turned into the stimulus (<see cref="BoardEditor.ConvertBench"/>, Part 13c), drawn at once and
        /// run again: the design's signals move from under the testbench to under the one the stimulus makes.
        /// </summary>
        public EditResult ConvertBench()
        {
            EditResult result = Editor.ConvertBench();
            if (result.Done)
            {
                Relayout();
                StartReplay(0);
            }

            return result;
        }

        /// <summary>
        /// The stimulus made a step test (Part 14c, <see cref="StepTests.Readable"/>): every input bit in turn rises, settles,
        /// falls and settles, each left longer than the slowest path the timing found -- one step, and run again. Refused for
        /// a design a written testbench drives, which a stimulus does not.
        /// </summary>
        public EditResult StepTest()
        {
            if (Project.Bench != null)
                return EditResult.Refused("A written testbench drives this circuit, not a stimulus: convert it first, and then step its inputs.");
            StepPlan plan = StepTests.Readable(Project, StepTests.SettleFor(Timing.Slowest));
            if (plan.Inputs.Count == 0)
                return EditResult.Refused("There is no input to step: give the circuit an input first.");
            return Replayed(Editor.SetStimulus(plan.Stimulus), 0);
        }

        /// <summary>
        /// The design held to <paramref name="library"/>, or to none for null (Part 14d, <see cref="BoardEditor.SetLibrary"/>):
        /// every gate of a cell given the cell's delays, one step, drawn at once and run again.
        /// </summary>
        public EditResult SetLibrary(GateLibrary library) => Edited(Editor.SetLibrary(library));

        /// <summary>The document counted in <paramref name="unit"/> (<see cref="BoardEditor.SetSettings"/>), and replayed: nothing is rescaled, everything is relabelled.</summary>
        public EditResult SetUnit(string unit) =>
            unit == Unit ? EditResult.Ok() : Replayed(Editor.SetSettings(Project.Settings with { TimeUnit = unit }), 0);

        /// <summary>The last edit taken back, drawn at once, and replayed. False when there is none.</summary>
        public bool Undo()
        {
            if (!Editor.Undo())
                return false;
            Relayout();
            StartReplay(0);
            return true;
        }

        /// <summary>The last edit taken back, made again, drawn at once, and replayed. False when there is none.</summary>
        public bool Redo()
        {
            if (!Editor.Redo())
                return false;
            Relayout();
            StartReplay(0);
            return true;
        }

        private EditResult Replayed(EditResult result, long from)
        {
            if (result.Done)
                StartReplay(from);
            return result;
        }

        private void StartReplay(long from)
        {
            ChangedFrom = Math.Max(0, from);
            Replay = RunSession.Open(Project, _now, _chunkEvents, _budgetMilliseconds, LayoutOf);
            _replayRanOn = false;
        }

        // -----------------------------------------------------------------
        // Frames
        // -----------------------------------------------------------------

        /// <summary>
        /// One frame's share of the work with the moment <paramref name="shown"/> on screen: a slice of
        /// the replay, which takes over once it has reached that moment, or, with none, a slice of the
        /// run shown.
        /// </summary>
        public SessionFrame Frame(double shown)
        {
            if (Replay == null)
                return Run.Frame() ? SessionFrame.Grew : SessionFrame.Nothing;

            Replay.Frame();

            // The shown run may have been run on past its end at a $stop; the replay goes as far, once.
            if (!_replayRanOn && Replay.Done && Replay.Model.End < Run.Model.End)
            {
                _replayRanOn = true;
                Replay.RunOn(Run.Model.End);
            }

            double target = Math.Min(Math.Max(0.0, shown), Run.Model.End);
            bool caughtUp = Made(Replay) >= target - Epsilon && (Replay.Model.End >= Run.Model.End || Replay.Done);
            bool cannotGoOn = Replay.Done && (_replayRanOn || Replay.Model.End >= Run.Model.End);

            if (caughtUp || cannotGoOn)
            {
                TakeOver();
                return SessionFrame.Switched;
            }

            return SessionFrame.Nothing;
        }

        /// <summary>The replay takes over now, made as far as it is: RESTART's, since 0 is made already. False when there is none.</summary>
        public bool TakeReplay()
        {
            if (Replay == null)
                return false;
            TakeOver();
            return true;
        }

        /// <summary>Makes the whole of what is being made, and lets a replay take over: what a capture or a test that needs all of it does.</summary>
        public void Finish()
        {
            if (Replay != null)
            {
                Replay.Finish();
                if (Replay.Model.End < Run.Model.End && Replay.RunOn(Run.Model.End))
                    Replay.Finish();
                TakeOver();
            }

            Run.Finish();
        }

        private void TakeOver()
        {
            Run = Replay;
            Replay = null;
            Version++;
        }

        /// <summary>How far a run is made: what it can show.</summary>
        private static double Made(RunSession run) =>
            run.Model.Complete ? run.Model.End : Math.Min(run.Model.End, run.Model.Reached);
    }
}
