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

        // The instances stepped through from the top, outermost first (Part 12a).
        private readonly List<string> _path = new List<string>();

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
        public string Module => ModuleOn(Project, _path, out _);

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
            Module module = Project.Find(Module);
            if (!(module?.FindPart(part) is BlockPart block) || string.IsNullOrEmpty(block.Name) || Project.Find(block.Module) == null)
                return false;
            _path.Add(block.Copies > 1 ? block.Name + "[0]" : block.Name);   // an array is stepped into at its first copy (Part 12c)
            Stepped();
            return true;
        }

        /// <summary>Steps <paramref name="levels"/> out, no further than the top: false at the top already.</summary>
        public bool StepOut(int levels = 1)
        {
            if (_path.Count == 0 || levels < 1)
                return false;
            levels = Math.Min(levels, _path.Count);
            _path.RemoveRange(_path.Count - levels, levels);
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

            ModuleOn(Project, names, out int valid);
            if (valid < names.Count)
                return false;
            _path.Clear();
            _path.AddRange(names);
            Stepped();
            return true;
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
        public static string ModuleOn(Project project, IReadOnlyList<string> path, out int valid)
        {
            Module module = project.TopModule;
            valid = 0;
            if (module == null)
                return project.Top;
            foreach (string step in path)
            {
                if (!TryCopy(step, out string name, out int copy) || !(module.FindPart(name) is BlockPart block)
                    || !(project.Find(block.Module) is Module inner) || (block.Copies > 1) != (copy >= 0) || copy >= block.Copies)
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
            foreach (string step in _path)
                path += step.EndsWith("]", StringComparison.Ordinal) ? "." + step + "." + BoardCompiler.ArrayInstance : "." + step;
            return path;
        }

        /// <summary>The drawing of the module stepped into, its signals read under the instance stepped through.</summary>
        private SchematicLayout LayoutOf(Project project, CompiledProject compiled) =>
            DocumentSchematic.Build(project, compiled, ModuleOn(project, _path, out _), InstancePathUnder(compiled.DesignPath));

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

        /// <summary>Whether the module shown is written: the circuit's window shows its text rather than a drawing.</summary>
        public bool ShowsCode => Project.Find(Module)?.IsWritten == true;

        /// <summary>The text of the written module shown, as its editor has it; null while the module shown is drawn.</summary>
        public CodeDocument Code
        {
            get
            {
                if (!ShowsCode)
                    return _code = null;
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
                    _timing = TimingView.SameDesign(_timing.Project, Project) ? _timing.For(Project) : TimingView.Of(Project);
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
        public EditResult Place(CatalogEntry entry, Placement at) => Edited(PartsCatalog.Place(Editor, Module, entry, at));

        /// <summary>
        /// The parts of the module shown made into a block called <paramref name="name"/> (<see cref="BlockMaker.Make"/>, Part
        /// 12b): drawn now, and replayed -- the circuit doing exactly what it did. The result's id is the new instance's.
        /// </summary>
        public EditResult MakeBlock(IReadOnlyCollection<int> parts, string name) => Edited(BlockMaker.Make(Editor, Module, parts, name));

        /// <summary>Takes parts and wires away (<see cref="BoardEditor.RemoveParts"/>): drawn now, and replayed.</summary>
        public EditResult Remove(IEnumerable<int> parts, IEnumerable<int> wires = null) => Edited(Editor.RemoveParts(Module, parts, wires));

        /// <summary>Draws a wire between two ends, a pin or a tap each, through <paramref name="route"/>'s bends (<see cref="BoardEditor.Connect"/>; Part 10b): drawn now, and replayed.</summary>
        public EditResult Connect(Endpoint from, Endpoint to, Route route = null) => Edited(Editor.Connect(Module, from, to, route));

        /// <summary>What the inspector's fields were changed to (<see cref="Inspector.Apply"/>; Part 10c), as one step: drawn now, and replayed.</summary>
        public EditResult Inspect(Inspection inspection, IReadOnlyDictionary<string, string> typed)
        {
            Project before = Project;
            EditResult result = Inspector.Apply(Editor, Module, inspection, typed);
            return ReferenceEquals(before, Project) ? result : Edited(result);
        }

        /// <summary><paramref name="clip"/> pasted (<see cref="Clipboard.Paste"/>; Part 10d), one step: drawn now, and replayed.</summary>
        public EditResult Paste(Clip clip, List<int> parts, List<int> wires) => Edited(Clipboard.Paste(Editor, Module, clip, parts, wires));

        /// <summary>
        /// <paramref name="wires"/> put back on the automatic route, their bends forgotten and a tap's place along its wire
        /// kept (Part 10d), as one step: drawn again, and nothing replayed.
        /// </summary>
        public EditResult AutoRoute(IReadOnlyCollection<int> wires)
        {
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
        public EditResult Reroute(int wire, Route route) => Laid(Editor.Reroute(Module, wire, route));

        /// <summary>Moves, turns or mirrors parts (<see cref="BoardEditor.MoveParts"/>): drawn again, and nothing replayed.</summary>
        public EditResult Move(IReadOnlyDictionary<int, Placement> to, bool continuing = false) => Laid(Editor.MoveParts(Module, to, continuing));

        /// <summary>
        /// Turns the parts <paramref name="parts"/> by <paramref name="quarterTurns"/>, counter-clockwise, about their
        /// middle -- or, <paramref name="mirror"/>, mirrors them left to right about it -- as one: each part turned
        /// on its own place, and the places turned round the middle, which is kept on the grid so that every
        /// place on it stays there. Drawn again; nothing replayed.
        /// </summary>
        public EditResult Turn(IReadOnlyCollection<int> parts, int quarterTurns, bool mirror = false)
        {
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
            ModuleOn(Project, _path, out int valid);
            if (valid < _path.Count)
            {
                _path.RemoveRange(valid, _path.Count - valid);
                StepVersion++;
            }

            CompiledProject compiled = BoardCompiler.Compile(Project);
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
