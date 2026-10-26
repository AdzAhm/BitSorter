using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using BitSorter.Sim;
using BitSorter.SystemVerilog;
using BitSorter.SystemVerilog.Syntax;

namespace BitSorter.Board
{
    /// <summary>One file brought in: its name and its text.</summary>
    public readonly struct SourceFile
    {
        public SourceFile(string path, string text)
        {
            Path = path;
            Text = text;
        }

        public string Path { get; }
        public string Text { get; }
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
    /// the project's unit; and every other file -- an include, a memory image -- kept as one of the project's
    /// <see cref="Project.Files"/>.
    /// </summary>
    /// <remarks>
    /// **The testbench** is the module with no ports that nothing instantiates and that instantiates the design. **The
    /// design** is what it instantiates -- of several, the one its instance calls dut or uut, else the one with the most
    /// ports. With no testbench, the design is the module nothing instantiates. **The run's end** is where the testbench
    /// stops: the project is run once here, to its <c>$stop</c> or <c>$finish</c>, or to when nothing is left to happen,
    /// or as far as <see cref="MaxRun"/> and <see cref="MaxEvents"/> let it go, and the time kept as the stimulus's end,
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

        /// <summary>How many events that run may take: a testbench with a free-running clock and no <c>$stop</c> ends here.</summary>
        public const long MaxEvents = 2_000_000;

        private static readonly Regex DutName = new Regex("dut|uut", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public static ProjectImport Import(IReadOnlyList<SourceFile> files)
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

            // The .sv and .v files read together, as a simulator compiles a list of files, with every file there for an
            // include to read.
            var sources = new MemorySourceFiles();
            var texts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var paths = new List<string>();
            foreach (SourceFile file in files)
            {
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

            // The other files, each by its bare name -- a .sv of nothing but defines among them.
            var kept = new List<ProjectFile>();
            foreach (SourceFile file in files)
            {
                string name = ModuleCutter.NameOf(file.Path);
                if (moduleFiles.Contains(name) || broken.Contains(SourcePaths.Normalise(file.Path)))
                    continue;
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

            // A file that declares something -- constants, types -- or includes one that does, goes inside each module
            // that includes it, where it means the same to that module. And without an include guard, which would leave it
            // empty in every module but the first once they are files of their own read together -- unless another of the
            // project's files includes it too, when the guard is what keeps it from being declared twice.
            var declaring = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (bool grew = true; grew;)
            {
                grew = false;
                foreach (ProjectFile file in kept)
                    if (!declaring.Contains(file.Name) && (Declares(file.Text) || IncludesAny(file.Text, declaring)) && declaring.Add(file.Name))
                        grew = true;
            }

            var includedByFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ProjectFile file in kept)
                foreach (string name in IncludedNames(file.Text))
                    includedByFiles.Add(name);
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

            // The testbench, and the design.
            ModuleDeclaration bench = null;
            foreach (ModuleDeclaration module in modules)
            {
                if (module.Ports.Count > 0 || used.Contains(module.Name) || !uses[module.Name].Exists(byName.ContainsKey))
                    continue;
                if (bench == null)
                    bench = module;
                else
                    Note(module.Location.File, module.Location.Line, module.Location.Column,
                        $"{module.Name} looks like a testbench too; {bench.Name} is the one run.", false);
            }

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
                    cutter = new ModuleCutter(ModuleCutter.NameOf(file), text, lines, moduleFiles, declaring, TextOf);
                    cutters[file] = cutter;
                }

                string source = cutter.Cut(module, out string problem);
                if (problem != null)
                {
                    Note(module.Location.File, module.Location.Line, module.Location.Column, problem, true);
                    source = cutter.Alone(module);
                }

                return new Module { Name = module.Name, Source = source };
            }

            var decided = new Dictionary<string, Module>(StringComparer.Ordinal);
            var written = new List<Module>();
            void Decide(ModuleDeclaration module, int depth)
            {
                if (decided.ContainsKey(module.Name) || depth > 64)
                    return;
                decided[module.Name] = null;   // a module that uses itself is the rules' to refuse; its text is kept
                foreach (string name in uses[module.Name])
                    if (byName.TryGetValue(name, out ModuleDeclaration inner))
                        Decide(inner, depth + 1);

                Module chosen = module == bench || lookedInto.Contains(module.Name) ? null : Drawn(tree, module.Name, written, kept);
                if (chosen == null)
                {
                    chosen = Written(module);
                    written.Add(chosen);
                }

                decided[module.Name] = chosen;
            }

            foreach (ModuleDeclaration module in modules)
                Decide(module, 0);

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
            Elaboration elaboration = Elaborator.Elaborate(compiled.Tree, compiled.Top);
            if (elaboration.HasErrors)
            {
                foreach (Diagnostic d in elaboration.Diagnostics)
                    if (d.Severity == DiagnosticSeverity.Error)
                        Note(d.Location.File, d.Location.Line, d.Location.Column, d.Message, true);
                return Refused();
            }

            var sim = new Simulator(elaboration.Design, null, new SimOptions { Files = ProjectFiles.ForRun(project) });
            RunResult run = sim.RunTo(MaxRun, MaxEvents);
            long end = run.Time;
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
                    Note(WrittenModules.FileOf(bench.Name), 0, 0,
                        $"The testbench had not stopped by {end.ToString(CultureInfo.InvariantCulture)} {unit}, so the run is shown to there: end it with $stop.", false);
                }
            }

            return new ProjectImport(project with { Stimulus = new Stimulus { End = Math.Max(1, end) } }, notes);
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
        private static HashSet<string> LookedInto(List<ModuleDeclaration> modules, Dictionary<string, Dictionary<string, string>> instances,
            Dictionary<string, string> texts, Dictionary<string, ModuleDeclaration> byName)
        {
            var looked = new HashSet<string>(StringComparer.Ordinal);
            foreach (ModuleDeclaration module in modules)
            {
                if (instances[module.Name].Count == 0)
                    continue;
                texts.TryGetValue(SourcePaths.Normalise(module.Location.File ?? ""), out string text);
                if (string.IsNullOrEmpty(text))
                    continue;

                foreach (KeyValuePair<string, string> instance in instances[module.Name])
                {
                    var path = new Regex(@"(?<![\w$.])" + Regex.Escape(instance.Key) + @"((?:\s*\.\s*[A-Za-z_]\w*)+)", RegexOptions.CultureInvariant);
                    foreach (Match match in path.Matches(text))
                    {
                        // Each name along the path that is an instance takes its module with it.
                        string inside = instance.Value;
                        foreach (string part in match.Groups[1].Value.Split('.'))
                        {
                            if (inside == null || !byName.ContainsKey(inside))
                                break;
                            looked.Add(inside);
                            string name = part.Trim();
                            if (name.Length == 0)
                                continue;
                            inside = instances.TryGetValue(inside, out Dictionary<string, string> within) && within.TryGetValue(name, out string next) ? next : null;
                        }
                    }
                }
            }

            return looked;
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

        /// <summary>The module drawn, if it is structural and every module it uses is drawn or <paramref name="written"/>; null otherwise.</summary>
        private static Module Drawn(SyntaxTree tree, string module, List<Module> written, List<ProjectFile> files)
        {
            ImportResult drawn = BoardImporter.Import(tree, module, written, files);
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

        private static bool IncludesAny(string text, HashSet<string> names)
        {
            foreach (string name in IncludedNames(text))
                if (names.Contains(name))
                    return true;
            return false;
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
