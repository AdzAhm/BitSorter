using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using BitSorter.Sim;
using BitSorter.SystemVerilog;
using BitSorter.SystemVerilog.Syntax;

namespace BitSorter.Board
{
    /// <summary>One file brought in: its name and its text -- or, for one a reader left unread, its name and its size alone.</summary>
    public readonly struct SourceFile
    {
        private readonly long _unreadSize;

        public SourceFile(string path, string text)
        {
            Path = path;
            Text = text;
            WasRead = true;
            _unreadSize = 0;
        }

        private SourceFile(string path, long size)
        {
            Path = path;
            Text = null;
            WasRead = false;
            _unreadSize = size;
        }

        /// <summary>
        /// A file a reader did not read, its size past <see cref="ProjectImporter.MaxFileSize"/>, or among more files or more
        /// in all than an import takes (<see cref="ProjectImporter.Reads"/>): the import says what it is from its size.
        /// </summary>
        public static SourceFile Unread(string path, long size) => new SourceFile(path, size);

        public string Path { get; }
        public string Text { get; }

        /// <summary>Whether its text was read; false for one left <see cref="Unread"/>.</summary>
        public bool WasRead { get; }

        /// <summary>How large it is: its text's length, or for one unread, its size as the reader found it.</summary>
        public long Size => WasRead ? Text?.Length ?? 0 : _unreadSize;
    }

    /// <summary>Something an import has to say: about a file and a line when it is about one, in words a student can act on.</summary>
    public sealed class ImportNote
    {
        public ImportNote(string file, int line, int column, string message, bool isError)
        {
            File = file;
            Line = line;
            Column = column;
            Message = message;
            IsError = isError;
        }

        public string File { get; }
        public int Line { get; }
        public int Column { get; }
        public string Message { get; }
        public bool IsError { get; }

        public override string ToString() =>
            File == null ? Message : Line > 0 ? $"{File}:{Line.ToString(CultureInfo.InvariantCulture)}: {Message}" : $"{File}: {Message}";
    }

    /// <summary>What an import came to: a project, or none, and everything it had to say.</summary>
    public sealed class ProjectImport
    {
        internal ProjectImport(Project project, IReadOnlyList<ImportNote> notes)
        {
            Project = project;
            Notes = notes;
        }

        /// <summary>The project, or null when nothing could be made of the files.</summary>
        public Project Project { get; }

        public IReadOnlyList<ImportNote> Notes { get; }

        public bool Succeeded => Project != null;
    }

    /// <summary>
    /// A course project brought in (Part 13b): its <c>.sv</c> files read together, each module drawn when it can be -- it and
    /// everything under it structural (<see cref="BoardImporter"/>) -- and written otherwise, as its own text (12d); the
    /// testbench found and kept as the project's <see cref="Project.Bench"/>, run as written; a <c>`timescale</c> taken as
    /// the project's unit; and every other file a module names -- an include, a memory image -- kept as one of the project's
    /// <see cref="Project.Files"/>, and what nothing names -- a handout, a zip, a simulator's logs -- left out (Part 13f).
    /// </summary>
    /// <remarks>
    /// **The testbench** is the module with no ports that nothing instantiates and that instantiates the design. **The
    /// design** is what it instantiates -- of several, the one its instance calls dut or uut, else the one with the most
    /// ports. With no testbench, the design is the module nothing instantiates. **The run's end** is where the testbench
    /// stops: the project is run once here, to its <c>$stop</c> or <c>$finish</c>, or to when nothing is left to happen,
    /// or as far as <see cref="MaxRun"/>, <see cref="MaxEvents"/> and <see cref="MaxSteps"/> let it go, and the time kept as the stimulus's end,
    /// which the timeline shows to.
    ///
    /// **A written module is its own text**, cut from its file by <see cref="ModuleCutter"/>. **A module a testbench looks
    /// inside** -- <c>dut.state</c> -- is written, as are the modules on the way, so what it names is there under the
    /// names it was given. **A module of the library's name** -- <c>NAND2</c>, a course's gate file -- is taken from
    /// BitSorter's own cells, which have the same ports and the same <c>Tpdlh</c> and <c>Tpdhl</c>.
    /// </remarks>
    public static class ProjectImporter
    {
        /// <summary>How far, in the project's unit, the testbench is run to find where it stops.</summary>
        public const long MaxRun = 10_000_000;

        /// <summary>
        /// The latest moment a run is ever shown to: <see cref="MaxRun"/> and a tenth more, where <see cref="RunEnd"/> shows a
        /// testbench that ran out of things to do just before MaxRun -- to its last change, and a tenth more. The rules hold a
        /// saved run's end, its steps and its checks to it (save-8), so what an import or a PROGRAM measures is always taken,
        /// and a time read from a save edited by hand is never later; an edit of the inputs goes no further than MaxRun.
        /// </summary>
        public const long LatestEnd = MaxRun + MaxRun / 10;

        /// <summary>How many events that run may take: a testbench with a free-running clock and no <c>$stop</c> ends here.</summary>
        public const long MaxEvents = 2_000_000;

        /// <summary>
        /// How many instructions that run may take, its processes' and its functions' together (<see cref="Simulator.Steps"/>):
        /// an event is one wait, and a testbench that works hard between its waits ends here (Part 16d). The corpus's
        /// hardest-working, the multicycle processor's, takes 365,775; this is about a second of the engine on a desktop.
        /// </summary>
        public const long MaxSteps = 20_000_000;

        /// <summary>
        /// How many files one import takes (Part 13f). An import is untrusted text, and the wrong folder -- one holding a
        /// project's every build, or a course's every year -- is the usual way in; the course's largest project has eleven.
        /// </summary>
        public const int MaxFiles = 200;

        /// <summary>
        /// How many modules one import makes (Part 16d). Files are no bound on them -- one holds any number -- and every written
        /// module is read among all the others (<see cref="WrittenModules"/>), so a check of a project costs the square of its
        /// modules, and past the reads it keeps the cube: one file of a thousand small modules made 35 GB in 55 s, in one
        /// frame. The corpus's pipelined processor has seventeen.
        /// </summary>
        public const int MaxModules = 200;

        /// <summary>
        /// How much of what its files say between their modules -- defines, includes, conditionals -- an import copies into the
        /// written modules after them, which saw it there: 4 MB, a file's worth, all its files' together (Part 16d). Each module
        /// carries every define above it, so a file of modules with defines between them carried the square of them, each
        /// parsed with its module; a table of a thousand defines over a hundred modules carries 1.9 MB.
        /// </summary>
        public const long MaxCarried = MaxFileSize;

        /// <summary>How large one file may be, in characters: 4 MB, where the course's largest file is ten thousand.</summary>
        public const long MaxFileSize = 4L << 20;

        /// <summary>How much an import's files may hold together: 16 MB.</summary>
        public const long MaxTotalSize = 16L << 20;

        /// <summary>
        /// Whether a reader should read the files it has found -- <paramref name="count"/> of them, <paramref name="total"/>
        /// in all of those no larger than <see cref="MaxFileSize"/> -- or leave every one <see cref="SourceFile.Unread"/>,
        /// since the import will refuse them anyway. Either way a file past <see cref="MaxFileSize"/> is left unread, and is
        /// no part of the total. The desktop's folder and the browser's picker both ask it, so
        /// the two cannot come to read different things.
        /// </summary>
        public static bool Reads(int count, long total) => count <= MaxFiles && total <= MaxTotalSize;

        private static readonly Regex DutName = new Regex("dut|uut", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>A string in quotes on one line, its text the group: where a module names a file it includes or reads.</summary>
        private static readonly Regex Quoted = new Regex("\"([^\"\\r\\n]*)\"", RegexOptions.CultureInvariant);

        /// <summary>
        /// The project <paramref name="files"/> make. With <paramref name="benchName"/>, the testbench run is the module of that
        /// name where it is one -- for files that hold two, as a sample's own and the corpus's does (Part 15h-6); without, the
        /// first found, in the files' order.
        /// </summary>
        public static ProjectImport Import(IReadOnlyList<SourceFile> files, string benchName = null)
        {
            var notes = new List<ImportNote>();
            var said = new HashSet<string>(StringComparer.Ordinal);
            void Note(string file, int line, int column, string message, bool error)
            {
                // A file several others include is read with each: what it says is said once.
                if (said.Add(file + "\n" + line + "\n" + column + "\n" + message))
                    notes.Add(new ImportNote(file, line, column, message, error));
            }

            ProjectImport Refused() => new ProjectImport(null, notes);

            if (files == null || files.Count == 0)
            {
                Note(null, 0, 0, "There are no files to import.", true);
                return Refused();
            }

            // What an import takes (Part 13f): so many files, so much in all, and so much a file.
            if (files.Count > MaxFiles)
            {
                Note(null, 0, 0, $"There are {files.Count.ToString(CultureInfo.InvariantCulture)} files here, and an import takes at most {MaxFiles}. " +
                    "Is this one project's folder?", true);
                return Refused();
            }

            // A file too large to read is left out on its own below, so it is no part of what the import holds together: a
            // folder with a 17 MB lecture recording beside its code imports the code (Part 13f review).
            long total = 0;
            foreach (SourceFile file in files)
                if (file.Size <= MaxFileSize)
                    total += file.Size;
            if (total > MaxTotalSize)
            {
                Note(null, 0, 0, $"The files hold {Megabytes(total)} together, and an import takes at most {Megabytes(MaxTotalSize)}. " +
                    "Is this one project's folder?", true);
                return Refused();
            }

            // The .sv and .v files read together, as a simulator compiles a list of files, with every file there for an
            // include to read.
            var sources = new MemorySourceFiles();
            var texts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var paths = new List<string>();
            foreach (SourceFile file in files)
            {
                if (!file.WasRead || file.Size > MaxFileSize)
                {
                    Note(file.Path, 0, 0, file.Size > MaxFileSize
                        ? $"It is {Megabytes(file.Size)}, and a file can be at most {Megabytes(MaxFileSize)}, so it was left out."
                        : "It was not read, so it was left out.", true);
                    continue;
                }

                sources.Add(file.Path, file.Text);
                texts[SourcePaths.Normalise(file.Path)] = file.Text ?? "";
                if (ProjectFiles.IsSource(ModuleCutter.NameOf(file.Path)))
                    paths.Add(file.Path);
            }

            if (paths.Count == 0)
            {
                Note(null, 0, 0, "There is no .sv file to import.", true);
                return Refused();
            }

            SyntaxTree tree = SyntaxReader.ReadAll(paths, sources);
            var broken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Diagnostic d in tree.Diagnostics)
            {
                Note(d.Location.File, d.Location.Line, d.Location.Column, d.Message, d.Severity == DiagnosticSeverity.Error);
                if (d.Severity == DiagnosticSeverity.Error && d.Location.File != null)
                    broken.Add(SourcePaths.Normalise(d.Location.File));
            }

            foreach (PackageDeclaration package in tree.Packages)
                Note(package.Location.File, package.Location.Line, package.Location.Column,
                    $"{package.Name} is a package, which a project cannot hold yet: what imports it will not read.", false);
            foreach (ModuleDeclaration declared in tree.Interfaces)
                Note(declared.Location.File, declared.Location.Line, declared.Location.Column,
                    $"{declared.Name} is an interface, which a project cannot hold yet: what uses it will not read.", false);

            // The files modules are in: they become the project's modules, so nothing includes them any more.
            var moduleFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ModuleDeclaration module in tree.Modules)
                moduleFiles.Add(ModuleCutter.NameOf(module.Location.File));

            // The modules to make: none from a file that did not read, none of a cell's name, no name twice -- and a
            // module read again, because more than one file includes its file, is the same module.
            var modules = new List<ModuleDeclaration>();
            var byName = new Dictionary<string, ModuleDeclaration>(StringComparer.Ordinal);
            foreach (ModuleDeclaration module in tree.Modules)
            {
                string file = module.Location.File;
                if (file != null && broken.Contains(SourcePaths.Normalise(file)))
                    continue;
                if (BoardImporter.IsCell(module.Name))
                {
                    string differs = CellDiffers(module);
                    if (differs == null)
                        Note(file, module.Location.Line, module.Location.Column,
                            $"{module.Name} is one of BitSorter's own cells, with the same ports and delays, so the library's is used.", false);
                    else
                        Note(file, module.Location.Line, module.Location.Column,
                            $"{module.Name} is the name of one of BitSorter's own cells, which is used in its place, but {differs}", true);
                    continue;
                }

                if (byName.TryGetValue(module.Name, out ModuleDeclaration first))
                {
                    if (first.Location.Line != module.Location.Line
                        || !string.Equals(SourcePaths.Normalise(first.Location.File), SourcePaths.Normalise(file), StringComparison.OrdinalIgnoreCase))
                        Note(file, module.Location.Line, module.Location.Column, $"There are two modules called {module.Name}: the first is kept.", true);
                    continue;
                }

                byName[module.Name] = module;
                modules.Add(module);
            }

            if (modules.Count == 0)
            {
                Note(null, 0, 0, "No module could be read from the files.", true);
                return Refused();
            }

            if (modules.Count > MaxModules)
            {
                Note(null, 0, 0, $"There are {modules.Count.ToString(CultureInfo.InvariantCulture)} modules here, and an import takes at most " +
                    $"{MaxModules}. Is this one project?", true);
                return Refused();
            }

            // What a drawing reads its modules from: the ones kept, each the first of its name -- a library cell's included, for
            // its default delays -- so a module is drawn from the declaration the project has, never one the note says was not
            // kept, nor one from a file that did not read.
            var drawable = new Dictionary<string, ModuleDeclaration>(StringComparer.Ordinal);
            foreach (ModuleDeclaration module in tree.Modules)
                if ((module.Location.File == null || !broken.Contains(SourcePaths.Normalise(module.Location.File))) && !drawable.ContainsKey(module.Name))
                    drawable[module.Name] = module;
            HashSet<string> interfaces = BoardImporter.InterfacesOf(tree);

            // What the modules name (Part 13f): a file is the project's when a source names it, in quotes and case aside -- by an
            // include, or as a file it reads, a memory image's name passed down as a string included -- or a file so named does.
            // Anything else in a folder imported whole -- a handout, a zip, a simulator's logs -- is not.
            // Each text is read for the names in its quotes once, and a file so named is read in its turn: one pass over the
            // text however the files name each other, where reading every file against every text was 20 s (Part 13f review).
            var namedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var nameable = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (SourceFile file in files)
            {
                string name = ModuleCutter.NameOf(file.Path);
                if (file.WasRead && file.Size <= MaxFileSize && !ProjectFiles.IsSource(name) && !nameable.ContainsKey(name))
                    nameable[name] = file.Text ?? "";
            }

            var naming = new Queue<string>();
            foreach (string path in paths)
                if (texts.TryGetValue(SourcePaths.Normalise(path), out string source))
                    naming.Enqueue(source);
            while (naming.Count > 0)
                foreach (Match quoted in Quoted.Matches(naming.Dequeue()))
                {
                    string name = ModuleCutter.NameOf(quoted.Groups[1].Value);
                    if (nameable.TryGetValue(name, out string text) && namedFiles.Add(name))
                        naming.Enqueue(text);
                }

            // The other files, each by its bare name -- a .sv of nothing but defines among them.
            var kept = new List<ProjectFile>();
            foreach (SourceFile file in files)
            {
                string name = ModuleCutter.NameOf(file.Path);
                if (!file.WasRead || file.Size > MaxFileSize)
                    continue;   // left out above, and said so
                if (moduleFiles.Contains(name) || broken.Contains(SourcePaths.Normalise(file.Path)))
                    continue;
                if (!ProjectFiles.IsSource(name) && !namedFiles.Contains(name))
                {
                    Note(file.Path, 0, 0, "No module names it, by an `include or as a file it reads, so it was left out.", false);
                    continue;
                }
                if (!ProjectFiles.IsValidName(name, out string why))
                {
                    Note(file.Path, 0, 0, why + " It is left out.", false);
                    continue;
                }

                if (kept.Exists(k => string.Equals(k.Name, name, StringComparison.OrdinalIgnoreCase)))
                {
                    Note(file.Path, 0, 0, $"There are two files called {name}: the first is kept.", false);
                    continue;
                }

                // The project keeps the unit: a `timescale in an include would be refused in every module that includes it.
                string text = file.Text ?? "";
                string untimed = WithoutTimescales(text);
                if (untimed != text)
                    Note(name, 0, 0, "Its `timescale is taken out: the project keeps the unit.", false);
                kept.Add(new ProjectFile { Name = name, Text = untimed });
            }

            // A name in quotes through a folder, in a file kept -- an include, a memory image's name in a define -- made the bare
            // name of the file it names, as a module's is cut (ModuleCutter.Flattened): the project keeps them side by side.
            bool IsKept(string name) => kept.Exists(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
            for (int i = 0; i < kept.Count; i++)
            {
                var renamed = new List<string>();
                string[] lines = ModuleCutter.LinesOf(kept[i].Text);
                for (int l = 0; l < lines.Length; l++)
                    lines[l] = ModuleCutter.Flattened(lines[l], IsKept, renamed);
                if (renamed.Count == 0)
                    continue;
                kept[i] = kept[i] with { Text = string.Join("\n", lines) };
                foreach (string path in renamed)
                    Note(kept[i].Name, 0, 0, ReadAsBare(path), false);
            }

            // A file that declares something -- constants, types -- or includes one that does, goes inside each module
            // that includes it, where it means the same to that module. And without an include guard, which would leave it
            // empty in every module but the first once they are files of their own read together -- unless another of the
            // project's files includes it too, when the guard is what keeps it from being declared twice.
            // Each file read once for what it declares and includes, and the declaring found from there (Part 16d): read again
            // every pass of a fixpoint, a chain of includes listed against its direction, adding a file a pass, made 2 GB.
            var declaring = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var includers = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var includedByFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var found = new Queue<string>();
            foreach (ProjectFile file in kept)
            {
                foreach (string name in IncludedNames(file.Text))
                {
                    includedByFiles.Add(name);
                    if (!includers.TryGetValue(name, out List<string> by))
                        includers[name] = by = new List<string>();
                    by.Add(file.Name);
                }

                if (Declares(file.Text) && declaring.Add(file.Name))
                    found.Enqueue(file.Name);
            }
            while (found.Count > 0)
                if (includers.TryGetValue(found.Dequeue(), out List<string> by))
                    foreach (string name in by)
                        if (declaring.Add(name))
                            found.Enqueue(name);
            for (int i = 0; i < kept.Count; i++)
            {
                if (!declaring.Contains(kept[i].Name) || includedByFiles.Contains(kept[i].Name))
                    continue;
                string unguarded = Unguarded(kept[i].Text);
                if (unguarded == kept[i].Text)
                    continue;
                kept[i] = kept[i] with { Text = unguarded };
                Note(kept[i].Name, 0, 0, "Its include guard is taken out: each module that uses it includes it inside itself now.", false);
            }

            // Who instantiates whom, among the modules made, and by which instance names.
            var uses = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            var instances = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
            var used = new HashSet<string>(StringComparer.Ordinal);
            foreach (ModuleDeclaration module in modules)
            {
                var list = new List<string>();
                var named = new Dictionary<string, string>(StringComparer.Ordinal);
                Instantiated(module.Items, list, named);
                uses[module.Name] = list;
                instances[module.Name] = named;
                foreach (string name in list)
                    if (byName.ContainsKey(name))
                        used.Add(name);
            }

            // The testbench, and the design: the one asked for, where it is one, else the first.
            bool IsBench(ModuleDeclaration module) =>
                module.Ports.Count == 0 && !used.Contains(module.Name) && uses[module.Name].Exists(byName.ContainsKey);
            ModuleDeclaration bench = benchName != null ? modules.Find(m => m.Name == benchName && IsBench(m)) : null;
            foreach (ModuleDeclaration module in modules)
            {
                if (!IsBench(module) || module == bench)
                    continue;
                if (bench == null)
                    bench = module;
                else
                    Note(module.Location.File, module.Location.Line, module.Location.Column,
                        $"{module.Name} looks like a testbench too; {bench.Name} is the one run.", false);
            }

            if (benchName != null && bench?.Name != benchName && files.Count > 0)
                Note(bench?.Location.File ?? files[0].Path, 0, 0, bench != null
                    ? $"{benchName} is no testbench here; {bench.Name} is the one run."
                    : $"{benchName} is no testbench here, and there is none.", false);
            string top = bench != null ? DesignOf(bench, uses, instances, byName, Note) : null;
            if (bench == null)
            {
                foreach (ModuleDeclaration module in modules)
                {
                    if (used.Contains(module.Name))
                        continue;
                    if (top == null)
                        top = module.Name;
                    else
                        Note(module.Location.File, module.Location.Line, module.Location.Column,
                            $"{module.Name} is not used by anything either; {top} is taken as the design.", false);
                }
            }

            if (top == null)
            {
                Note(null, 0, 0, "There is no design to run: every module is used by another.", true);
                return Refused();
            }

            // The unit.
            string unit = TimeUnits.Default;
            if (tree.Timescale != null)
            {
                if (TimeUnits.TryUnitOf(tree.Timescale, out string read))
                    unit = read;
                else
                    Note(null, 0, 0, $"The timescale {tree.Timescale} counts in more than one of a unit, which a project cannot; it counts in {unit}.", false);
            }

            // The modules something looks inside are written, so what it names is there.
            var lookedInto = LookedInto(modules, instances, texts, byName);

            // Each module drawn when it can be -- the modules it uses decided first, a written one placed in it as a block --
            // and written otherwise. In the files' order, the testbench last.
            var cutters = new Dictionary<string, ModuleCutter>(StringComparer.OrdinalIgnoreCase);
            var carry = new ModuleCutter.Carry(MaxCarried);
            string TextOf(string name) => kept.Find(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase))?.Text;
            Module Written(ModuleDeclaration module)
            {
                string file = SourcePaths.Normalise(module.Location.File ?? "");
                if (!cutters.TryGetValue(file, out ModuleCutter cutter))
                {
                    var lines = new List<int>();
                    foreach (ModuleDeclaration other in tree.Modules)
                        if (other.Location.File != null && string.Equals(SourcePaths.Normalise(other.Location.File), file, StringComparison.OrdinalIgnoreCase))
                            lines.Add(other.Location.Line);
                    texts.TryGetValue(file, out string text);
                    cutter = new ModuleCutter(ModuleCutter.NameOf(file), text, lines, moduleFiles, declaring, TextOf, carry);
                    cutters[file] = cutter;
                }

                // Once a cut has carried more than an import may, the import is refused (below), said once: the rest is not cut.
                var renamed = new List<string>();
                if (carry.Spent)
                    return new Module { Name = module.Name, Source = BoardPrinter.WithoutHeader(module.Name, cutter.Alone(module)) };
                string source = cutter.Cut(module, out string problem, renamed);
                if (problem != null)
                {
                    Note(module.Location.File, module.Location.Line, module.Location.Column, problem, true);
                    renamed.Clear();
                    source = cutter.Alone(module, renamed);
                }

                foreach (string path in renamed)
                    Note(module.Location.File, module.Location.Line, module.Location.Column, ReadAsBare(path), false);

                return new Module { Name = module.Name, Source = BoardPrinter.WithoutHeader(module.Name, source) };
            }

            var decided = new Dictionary<string, Module>(StringComparer.Ordinal);
            var written = new List<Module>();

            // Whether a file is a course template (Part 13e), read once a file: read for each of its modules, a file of many
            // read itself as many times.
            var templates = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            bool IsTemplate(string file, string text)
            {
                if (!templates.TryGetValue(file, out bool template))
                    templates[file] = template = CourseTemplate.TryRead(text, out _);
                return template;
            }

            // The written modules a module makes, all the way down: all a drawing of it can place as blocks. Handed every
            // module written so far, each edit of the attempt checked all of them again: 460 MB in one frame for the
            // pipelined processor, whose every module comes in written (Part 16b).
            List<Module> WrittenUnder(string name)
            {
                var under = new HashSet<string>(StringComparer.Ordinal);
                var stack = new Stack<string>();
                stack.Push(name);
                while (stack.Count > 0)
                    if (uses.TryGetValue(stack.Pop(), out List<string> made))
                        foreach (string inner in made)
                            if (under.Add(inner))
                                stack.Push(inner);
                return written.FindAll(m => under.Contains(m.Name));
            }
            void Decide(ModuleDeclaration module, int depth)
            {
                if (decided.ContainsKey(module.Name) || depth > 64)
                    return;
                decided[module.Name] = null;   // a module that uses itself is the rules' to refuse; its text is kept
                foreach (string name in uses[module.Name])
                    if (byName.TryGetValue(name, out ModuleDeclaration inner))
                        Decide(inner, depth + 1);

                // A course template (Part 13e) is written, its markers kept, so the student's code goes between them.
                string ownFile = SourcePaths.Normalise(module.Location.File ?? "");
                texts.TryGetValue(ownFile, out string fileText);
                bool template = IsTemplate(ownFile, fileText);

                Module chosen = module == bench || template || lookedInto.Contains(module.Name)
                    ? null
                    : Drawn(drawable, interfaces, tree.Timescale, module.Name, WrittenUnder(module.Name), kept);
                if (chosen == null)
                {
                    chosen = Written(module);
                    if (template && CourseTemplate.TryRead(chosen.Source, out _))
                        chosen = chosen with { Template = new ModuleTemplate { File = ModuleCutter.NameOf(module.Location.File), Text = fileText, Given = chosen.Source } };
                    written.Add(chosen);
                }

                decided[module.Name] = chosen;
            }

            foreach (ModuleDeclaration module in modules)
                Decide(module, 0);
            if (carry.Spent)
                return Refused();

            var made = new List<Module>();
            foreach (ModuleDeclaration module in modules)
                if (module != bench)
                    made.Add(decided[module.Name]);
            if (bench != null)
                made.Add(decided[bench.Name]);

            var project = new Project
            {
                Name = top,
                Modules = made,
                Top = top,
                Bench = bench?.Name,
                Settings = new ProjectSettings { TimeUnit = unit },
                Files = kept,
            };

            foreach (BoardDiagnostic d in BoardRules.Check(project))
            {
                if (!d.IsError)
                    continue;
                Module about = d.Module == null ? null : project.Find(d.Module);
                Note(about != null && about.IsWritten ? WrittenModules.FileOf(about.Name) : d.Module, d.Line, d.Column, d.Message, true);
            }

            CompiledProject compiled = BoardCompiler.Compile(project);
            if (!compiled.Succeeded)
            {
                if (!notes.Exists(n => n.IsError))
                    Note(null, 0, 0, "The project does not compile.", true);
                return Refused();
            }

            if (bench == null)
                return new ProjectImport(project, notes);

            // Where the testbench stops, which the run is shown to.
            Elaboration elaboration = compiled.Elaborate();
            if (elaboration.HasErrors)
            {
                foreach (Diagnostic d in elaboration.Diagnostics)
                    if (d.Severity == DiagnosticSeverity.Error)
                        Note(d.Location.File, d.Location.Line, d.Location.Column, d.Message, true);
                return Refused();
            }

            long end = RunEnd(project, elaboration, out bool stopped);
            if (!stopped)
                Note(WrittenModules.FileOf(bench.Name), 0, 0,
                    $"The testbench had not stopped by {end.ToString(CultureInfo.InvariantCulture)} {unit}, so the run is shown to there: end it with $stop.", false);

            return new ProjectImport(project with { Stimulus = new Stimulus { End = end } }, notes);
        }

        /// <summary>
        /// Where a written testbench's run is shown to, <paramref name="elaboration"/> run as far as <see cref="MaxRun"/>,
        /// <see cref="MaxEvents"/> and <see cref="MaxSteps"/> let it: where it stops; run out of things to do, as one with no $stop is, its last change
        /// and a tenth more; or, not stopped, there, <paramref name="stopped"/> false. Measured again as the testbench's PROGRAM
        /// is chosen (Part 15h-6c), since each program stops at its own time. Never past <see cref="LatestEnd"/>.
        /// </summary>
        internal static long RunEnd(Project project, Elaboration elaboration, out bool stopped)
        {
            var sim = new Simulator(elaboration.Design, null, new SimOptions { Files = ProjectFiles.ForRun(project) });
            RunResult run = sim.RunTo(MaxRun, MaxEvents, MaxSteps);
            long end = run.Time;
            stopped = true;
            if (run.Status != RunStatus.Suspended && run.Status != RunStatus.Finished)
            {
                if (run.Status == RunStatus.ReachedTime && sim.NextEventTime == null)
                {
                    // It ran out of things to do, as a testbench with no $stop does: shown to its last change, and a tenth more.
                    long last = LastChange(sim);
                    end = last + Math.Max(1, last / 10);
                }
                else
                {
                    stopped = false;
                }
            }
            return Math.Min(LatestEnd, Math.Max(1, end));
        }

        /// <summary>
        /// The design a testbench tests: what it instantiates of the project's modules -- of several, the one whose instance it
        /// calls dut or uut, or else the one with the most ports, which a clock generator or a model beside it has fewer of.
        /// </summary>
        private static string DesignOf(ModuleDeclaration bench, Dictionary<string, List<string>> uses, Dictionary<string, Dictionary<string, string>> instances,
            Dictionary<string, ModuleDeclaration> byName, Action<string, int, int, string, bool> note)
        {
            var candidates = uses[bench.Name].FindAll(byName.ContainsKey);
            if (candidates.Count <= 1)
                return candidates.Count == 1 ? candidates[0] : null;

            string chosen = null;
            foreach (KeyValuePair<string, string> instance in instances[bench.Name])
                if (byName.ContainsKey(instance.Value) && DutName.IsMatch(instance.Key))
                {
                    chosen = instance.Value;
                    break;
                }

            if (chosen == null)
                foreach (string candidate in candidates)
                    if (chosen == null || byName[candidate].Ports.Count > byName[chosen].Ports.Count)
                        chosen = candidate;

            note(bench.Location.File, bench.Location.Line, bench.Location.Column,
                $"{bench.Name} makes {string.Join(", ", candidates)}; {chosen} is taken as the design.", false);
            return chosen;
        }

        /// <summary>
        /// The modules something looks inside by a hierarchical name -- <c>dut.state</c>, <c>dut.u1.q</c> -- and so the ones a
        /// drawing would not keep the names of.
        /// </summary>
        /// <remarks>
        /// Each file is read once for every instance's name with a path after it (<see cref="PathsIn"/>), where a regex made for
        /// each instance read the whole of its module's file and copied every path it found, the instances times the file (Part
        /// 16d). A path is kept as where it starts and ends, and walked only as far as the instances along it go.
        /// </remarks>
        private static HashSet<string> LookedInto(List<ModuleDeclaration> modules, Dictionary<string, Dictionary<string, string>> instances,
            Dictionary<string, string> texts, Dictionary<string, ModuleDeclaration> byName)
        {
            var looked = new HashSet<string>(StringComparer.Ordinal);
            var wanted = new InstanceNames();
            foreach (ModuleDeclaration module in modules)
                foreach (string name in instances[module.Name].Keys)
                    wanted.Add(name);

            var pathsIn = new Dictionary<string, Dictionary<string, List<(int NameEnd, int PathEnd)>>>(StringComparer.Ordinal);
            foreach (ModuleDeclaration module in modules)
            {
                if (instances[module.Name].Count == 0)
                    continue;
                string file = SourcePaths.Normalise(module.Location.File ?? "");
                texts.TryGetValue(file, out string text);
                if (string.IsNullOrEmpty(text))
                    continue;
                if (!pathsIn.TryGetValue(file, out Dictionary<string, List<(int NameEnd, int PathEnd)>> paths))
                    pathsIn[file] = paths = PathsIn(text, wanted);

                foreach (KeyValuePair<string, string> instance in instances[module.Name])
                {
                    if (!paths.TryGetValue(instance.Key, out List<(int NameEnd, int PathEnd)> found))
                        continue;
                    foreach ((int nameEnd, int pathEnd) in found)
                    {
                        // The instance's module, and each one a name along the path but its last passes through -- the last is
                        // what the path names in the module it has reached. No module is inside itself, so a walk deeper than
                        // there are modules goes round one that makes itself, which the rules refuse.
                        string inside = instance.Value;
                        if (inside == null || !byName.ContainsKey(inside))
                            continue;
                        looked.Add(inside);
                        int at = nameEnd;
                        for (int depth = 0; depth < byName.Count; depth++)
                        {
                            int end = StepEnd(text, at, out int step);
                            if (end < 0 || end >= pathEnd)
                                break;
                            string name = text.Substring(step, end - step);
                            inside = instances.TryGetValue(inside, out Dictionary<string, string> within) && within.TryGetValue(name, out string next) ? next : null;
                            if (inside == null || !byName.ContainsKey(inside))
                                break;
                            looked.Add(inside);
                            at = end;
                        }
                    }
                }
            }

            return looked;
        }

        /// <summary>
        /// Every name of <paramref name="wanted"/> in <paramref name="text"/> with a path through dots after it -- <c>dut.state</c>,
        /// <c>dut . u1 . q</c> -- by the name: where the name ends and where its path does. They are what a regex made for each
        /// name, <c>(?&lt;![\w$.])name((?:\s*\.\s*[A-Za-z_]\w*)+)</c>, found, each match taking its path, in one reading of the
        /// text for all of them, making nothing for a name not wanted (Part 16d).
        /// </summary>
        /// <remarks>
        /// A name further along a path, after a space, starts a path too, as it did for its own regex. Its path is the rest of
        /// the one it is in, so where that ends is found once for every name along it: read again from each, a run of
        /// <c>a. a. a.</c> cost the square of its length. An escaped name that is no plain identifier, <c>\d+u</c>, is found
        /// where the code writes it, after its backslash and up to the white space that ends it, as the lexer reads one.
        /// </remarks>
        private static Dictionary<string, List<(int NameEnd, int PathEnd)>> PathsIn(string text, InstanceNames wanted)
        {
            var paths = new Dictionary<string, List<(int NameEnd, int PathEnd)>>(StringComparer.Ordinal);

            // Whether a name at start is inside the path its own last match took, as a regex taking each match skips it.
            bool Taken(string name, int start) =>
                paths.TryGetValue(name, out List<(int NameEnd, int PathEnd)> found) && start < found[found.Count - 1].PathEnd;

            void Found(string name, int nameEnd, int pathEnd)
            {
                if (!paths.TryGetValue(name, out List<(int NameEnd, int PathEnd)> found))
                    paths[name] = found = new List<(int NameEnd, int PathEnd)>(1);
                found.Add((nameEnd, pathEnd));
            }

            int readTo = 0;      // the end of the last path read through from a name
            int escapedTo = 0;   // the end of the last escaped name: a backslash inside one is part of it
            for (int i = 0; i < text.Length;)
            {
                char c = text[i];
                if (c == '\\' && i >= escapedTo && wanted.HasEscaped)
                {
                    int end = i + 1;
                    while (end < text.Length && !char.IsWhiteSpace(text[end]))
                        end++;
                    escapedTo = end;
                    string escaped = wanted.EscapedAt(text, i + 1, end - i - 1);
                    if (escaped != null && !Taken(escaped, i + 1))
                    {
                        int pathEnd = PathEnd(text, end);
                        if (pathEnd > end)
                            Found(escaped, end, pathEnd);
                    }
                }

                if (!IsNameChar(c))
                {
                    i++;
                    continue;
                }

                int start = i;
                while (i < text.Length && IsNameChar(text[i]))
                    i++;
                if (!IsNameStart(text[start]) || start > 0 && text[start - 1] == '.')
                    continue;
                string name = wanted.PlainAt(text, start, i - start);
                if (name == null || Taken(name, start))
                    continue;

                int to;
                if (start < readTo && i <= readTo)
                {
                    to = readTo;   // a name along the last path read: the rest of that path is its own
                }
                else
                {
                    to = PathEnd(text, i);
                    readTo = to;
                }

                if (to > i)
                    Found(name, i, to);
            }

            return paths;
        }

        /// <summary>Where the path through dots from <paramref name="at"/> ends: <paramref name="at"/> itself where there is none.</summary>
        private static int PathEnd(string text, int at)
        {
            for (int end = StepEnd(text, at, out _); end >= 0; end = StepEnd(text, at, out _))
                at = end;
            return at;
        }

        /// <summary>
        /// The end of one step of a path from <paramref name="at"/>, <c>\s*\.\s*[A-Za-z_]\w*</c>, its name from <paramref name="name"/>;
        /// -1 where there is none.
        /// </summary>
        private static int StepEnd(string text, int at, out int name)
        {
            name = -1;
            int i = at;
            while (i < text.Length && char.IsWhiteSpace(text[i]))
                i++;
            if (i >= text.Length || text[i] != '.')
                return -1;
            i++;
            while (i < text.Length && char.IsWhiteSpace(text[i]))
                i++;
            if (i >= text.Length || !IsNameStart(text[i]))
                return -1;
            name = i;
            for (i++; i < text.Length && IsWord(text[i]); i++)
            {
            }

            return i;
        }

        /// <summary>A regex's <c>\w</c>: a letter, a non-spacing mark, a decimal digit or a connector, <c>_</c> among them.</summary>
        private static bool IsWord(char c)
        {
            if (c < 128)
                return c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z' || c >= '0' && c <= '9' || c == '_';
            UnicodeCategory category = char.GetUnicodeCategory(c);
            return category <= UnicodeCategory.OtherLetter || category == UnicodeCategory.NonSpacingMark ||
                category == UnicodeCategory.DecimalDigitNumber || category == UnicodeCategory.ConnectorPunctuation;
        }

        private static bool IsNameChar(char c) => c == '$' || IsWord(c);

        private static bool IsNameStart(char c) => c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z' || c == '_';

        /// <summary>
        /// The instances' names, found by a stretch of a text without making a string of it -- in order, and searched by halves,
        /// so that no set of names a file is given can make the search slow -- the plain identifiers apart from the escaped names
        /// that are not, which are looked for only after a backslash.
        /// </summary>
        private sealed class InstanceNames
        {
            private readonly List<string> _plain = new List<string>();
            private readonly List<string> _escaped = new List<string>();
            private bool _sorted;

            public bool HasEscaped => _escaped.Count > 0;

            public void Add(string name)
            {
                if (string.IsNullOrEmpty(name))
                    return;
                bool plain = IsNameStart(name[0]);
                for (int i = 1; plain && i < name.Length; i++)
                    plain = IsNameChar(name[i]);
                (plain ? _plain : _escaped).Add(name);
                _sorted = false;
            }

            public string PlainAt(string text, int start, int length) => Find(_plain, text, start, length);

            public string EscapedAt(string text, int start, int length) => Find(_escaped, text, start, length);

            private string Find(List<string> names, string text, int start, int length)
            {
                if (!_sorted)
                {
                    _plain.Sort(StringComparer.Ordinal);
                    _escaped.Sort(StringComparer.Ordinal);
                    _sorted = true;
                }

                for (int low = 0, high = names.Count - 1; low <= high;)
                {
                    int middle = low + (high - low) / 2;
                    string name = names[middle];
                    int order = string.CompareOrdinal(text, start, name, 0, Math.Min(length, name.Length));
                    if (order == 0)
                        order = length - name.Length;
                    if (order == 0)
                        return name;
                    if (order < 0)
                        high = middle - 1;
                    else
                        low = middle + 1;
                }

                return null;
            }
        }

        /// <summary>When the run last changed anything: its last settled change, or the end of its last transition.</summary>
        private static long LastChange(Simulator sim)
        {
            long last = 0;
            Design design = sim.Design;
            for (int i = 0; i < design.Signals.Count; i++)
            {
                List<ValueChange> changes = sim.History.SettledChanges(design.SignalAt(i));
                if (changes.Count > 0)
                    last = Math.Max(last, changes[changes.Count - 1].Time);
            }

            for (int i = 0; i < sim.History.TransitionCount; i++)
            {
                Transition t = sim.History.TransitionAt(i);
                if (t.Outcome != TransitionOutcome.Pending)
                    last = Math.Max(last, t.EndedAt);
            }

            return last;
        }

        /// <summary>How a module of a cell's name is not the library's cell -- its ports, or what its delays default to -- or null when it is.</summary>
        private static string CellDiffers(ModuleDeclaration module)
        {
            ModuleDeclaration cell = CellLibrary.Build(module.Name);
            var ports = new List<string>();
            foreach (PortDeclaration port in module.Ports)
                ports.Add(port.Name);
            var theirs = new List<string>();
            foreach (PortDeclaration port in cell.Ports)
                theirs.Add(port.Name);
            if (string.Join(",", ports) != string.Join(",", theirs))
                return $"its ports are ({string.Join(", ", ports)}) where the library's are ({string.Join(", ", theirs)}): rename the module, and what uses it.";

            var parameters = new List<ParameterDeclaration>(module.HeaderParameters);
            foreach (ModuleItem item in module.Items)
                if (item is ParameterDeclaration parameter)
                    parameters.Add(parameter);
            foreach (ParameterDeclaration parameter in parameters)
                if ((parameter.Name == CellLibrary.Rise || parameter.Name == CellLibrary.Fall) && parameter.Value != null && Printer.Print(parameter.Value) != "1")
                    return $"its {parameter.Name} defaults to {Printer.Print(parameter.Value)} where the library's is 1: give every instance its delays, or rename the module.";
            return null;
        }

        /// <summary>What a name in quotes made bare says.</summary>
        private static string ReadAsBare(string path) =>
            $"{path} is read as {ModuleCutter.NameOf(path)}: a project keeps its files side by side, by their names.";

        /// <summary>The module drawn, if it is structural and every module it uses is drawn or <paramref name="written"/>; null otherwise.</summary>
        private static Module Drawn(Dictionary<string, ModuleDeclaration> modules, HashSet<string> interfaces, string timescale, string module,
            List<Module> written, List<ProjectFile> files)
        {
            ImportResult drawn = BoardImporter.Import(modules, interfaces, timescale, module, written, files);
            return drawn.Succeeded ? drawn.Project.Find(module) : null;
        }

        /// <summary>Whether a file declares anything: whether there is more to it than comments and directives, a long define's lines included.</summary>
        private static bool Declares(string text)
        {
            bool inComment = false, continued = false;
            foreach (string line in ModuleCutter.LinesOf(text))
            {
                string code = ModuleCutter.StripComments(line, ref inComment).Trim();
                bool carriedOn = continued;
                continued = line.TrimEnd().EndsWith("\\", StringComparison.Ordinal) && (carriedOn || ModuleCutter.DirectiveOf(code) != null);
                if (carriedOn || code.Length == 0)
                    continue;
                if (ModuleCutter.DirectiveOf(code) == null)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// A size in megabytes, to a tenth and rounded up, so a size past a limit never reads as the limit itself:
        /// <c>4.1 MB</c> for a byte past 4 MB.
        /// </summary>
        private static string Megabytes(long size) =>
            (Math.Ceiling(size / 1048576.0 * 10) / 10).ToString("0.#", CultureInfo.InvariantCulture) + " MB";

        /// <summary>The bare names a file's text includes.</summary>
        private static IEnumerable<string> IncludedNames(string text)
        {
            bool inComment = false;
            foreach (string line in ModuleCutter.LinesOf(text))
            {
                string code = ModuleCutter.StripComments(line, ref inComment);
                if (ModuleCutter.DirectiveOf(code) != "include")
                    continue;
                Match include = Regex.Match(code, "`include\\s*\"([^\"]*)\"");
                if (include.Success)
                    yield return ModuleCutter.NameOf(include.Groups[1].Value);
            }
        }

        /// <summary><paramref name="text"/> with every <c>`timescale</c> line left empty, so every other line stays where it was.</summary>
        private static string WithoutTimescales(string text)
        {
            string[] lines = ModuleCutter.LinesOf(text);
            bool inComment = false, changed = false;
            for (int l = 0; l < lines.Length; l++)
                if (ModuleCutter.DirectiveOf(ModuleCutter.StripComments(lines[l], ref inComment)) == "timescale")
                {
                    lines[l] = "";
                    changed = true;
                }

            return changed ? string.Join("\n", lines) : text;
        }

        /// <summary>
        /// <paramref name="text"/> without a guard round all of it -- <c>`ifndef X</c> and <c>`define X</c> its first code, and
        /// the <c>`endif</c> that closes them its last -- each of those lines left empty, so every other line stays where it was.
        /// </summary>
        private static string Unguarded(string text)
        {
            string[] lines = ModuleCutter.LinesOf(text);
            var code = new List<int>();
            var stripped = new string[lines.Length];
            bool inComment = false;
            for (int l = 0; l < lines.Length; l++)
            {
                stripped[l] = ModuleCutter.StripComments(lines[l], ref inComment).Trim();
                if (stripped[l].Length > 0)
                    code.Add(l);
            }

            if (code.Count < 3)
                return text;
            char[] spaces = { ' ', '\t' };
            string[] first = stripped[code[0]].Split(spaces, StringSplitOptions.RemoveEmptyEntries);
            string[] second = stripped[code[1]].Split(spaces, StringSplitOptions.RemoveEmptyEntries);
            if (first.Length != 2 || first[0] != "`ifndef" || second.Length < 2 || second[0] != "`define" || second[1] != first[1])
                return text;

            int depth = 0;
            for (int c = 0; c < code.Count; c++)
            {
                string directive = ModuleCutter.DirectiveOf(stripped[code[c]]);
                if (directive == "ifdef" || directive == "ifndef")
                    depth++;
                else if (directive == "endif" && --depth == 0)
                {
                    if (c != code.Count - 1)
                        return text;   // the guard closes before the end: it guards only a part
                    lines[code[0]] = "";
                    lines[code[1]] = "";
                    lines[code[c]] = "";
                    return string.Join("\n", lines);
                }
            }

            return text;
        }

        /// <summary>The modules <paramref name="items"/> instantiate, generate blocks included, each once, and each instance's module by its name.</summary>
        private static void Instantiated(IEnumerable<ModuleItem> items, List<string> into, Dictionary<string, string> named)
        {
            foreach (ModuleItem item in items)
            {
                switch (item)
                {
                    case ModuleInstantiation instantiation:
                        if (!into.Contains(instantiation.Module))
                            into.Add(instantiation.Module);
                        foreach (ModuleInstance instance in instantiation.Instances)
                            if (instance.Name != null)
                                named[instance.Name] = instantiation.Module;
                        break;
                    case GenerateRegion region:
                        Instantiated(region.Items, into, named);
                        break;
                    case GenerateBlock block:
                        Instantiated(block.Items, into, named);
                        break;
                    case LoopGenerate loop when loop.Body != null:
                        Instantiated(loop.Body.Items, into, named);
                        break;
                    case IfGenerate choice:
                        if (choice.Then != null)
                            Instantiated(choice.Then.Items, into, named);
                        if (choice.Else != null)
                            Instantiated(choice.Else.Items, into, named);
                        break;
                }
            }
        }
    }
}
