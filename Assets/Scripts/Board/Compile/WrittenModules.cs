using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using BitSorter.Sim;
using BitSorter.SystemVerilog.Syntax;

namespace BitSorter.Board
{
    /// <summary>A module's port as a block of it sees it: a drawn module's port part, or a port of a written module's header.</summary>
    public readonly struct ModulePort
    {
        public ModulePort(string name, PortDirection direction, int width, bool isNet = false)
        {
            Name = name;
            Direction = direction;
            Width = width;
            IsNet = isNet;
        }

        public string Name { get; }
        public PortDirection Direction { get; }
        public int Width { get; }

        /// <summary>
        /// Whether a written module's port is a net -- a wire, a tri, an inout, or an output with no data type, however it
        /// was declared -- as the elaboration made it (Part 14f); a drawn port's is the compiler's to say.
        /// </summary>
        public bool IsNet { get; }
    }

    /// <summary>
    /// What a written module's text came to (Part 12d): the module it declares, its ports, the modules it uses, and
    /// everything wrong with it, each at a line and a column of the text.
    /// </summary>
    public sealed class WrittenModule
    {
        internal WrittenModule(ModuleDeclaration declaration, IReadOnlyList<ModulePort> ports, IReadOnlyList<string> uses,
            IReadOnlyList<Diagnostic> diagnostics)
        {
            Declaration = declaration;
            Ports = ports;
            Uses = uses;
            Diagnostics = diagnostics;
        }

        /// <summary>The module as written; null when the text does not read as one module.</summary>
        public ModuleDeclaration Declaration { get; }

        /// <summary>Its ports, inputs and outputs in the order its header gives them; none when it has errors.</summary>
        public IReadOnlyList<ModulePort> Ports { get; }

        /// <summary>The modules and cells it instantiates, each once, in the order first met.</summary>
        public IReadOnlyList<string> Uses { get; }

        /// <summary>Its errors and warnings, each where it is in the module's own text.</summary>
        public IReadOnlyList<Diagnostic> Diagnostics { get; }

        public bool HasErrors => FirstError != null;

        public Diagnostic FirstError
        {
            get
            {
                foreach (Diagnostic d in Diagnostics)
                    if (d.Severity == DiagnosticSeverity.Error)
                        return d;
                return null;
            }
        }
    }

    /// <summary>
    /// Modules written as SystemVerilog (Part 12d): a block whose inside is code rather than a drawing. Its ports are its
    /// header's, so a block of it has pins like any block's, and it is compiled as written.
    /// </summary>
    /// <remarks>
    /// **The text is elaborated on its own to be read**, among the library's cells, the project's other written modules and,
    /// for each drawn module, its header alone: so its ports' widths are what the language says they are -- a parameter,
    /// a typedef, a struct -- and an error in it is found here, at its line, rather than when the whole design is run. A
    /// result is kept for the same text in the same surroundings, since the rules ask on every edit.
    /// </remarks>
    public static class WrittenModules
    {
        private const int KeptResults = 128;
        private static readonly Dictionary<string, WrittenModule> Kept = new Dictionary<string, WrittenModule>(StringComparer.Ordinal);
        private static readonly Dictionary<string, (SyntaxTree Tree, List<SourceLocation> TimeDeclarations)> Parsed =
            new Dictionary<string, (SyntaxTree, List<SourceLocation>)>(StringComparer.Ordinal);

        /// <summary>The top a written module is checked under as an instance: not an identifier, so no module can be called it.</summary>
        private const string HostName = "written check";

        /// <summary>The file a written module's text is read as, which its diagnostics' locations name.</summary>
        public static string FileOf(string module) => module + ".sv";

        /// <summary>What a new written module starts as: one input, one output, and the output following the input.</summary>
        public static string Template(string name) =>
            "module " + name + " (\n" +
            "    input  logic a,  // an input\n" +
            "    output logic y   // an output\n" +
            ");\n" +
            "    assign y = a;\n" +
            "endmodule\n";

        /// <summary>
        /// <paramref name="module"/>'s text with the module <paramref name="from"/> called <paramref name="to"/>: in its own
        /// header when it is that module, and wherever it instantiates it. Only those names change -- a signal or an instance
        /// of the same name stays as it is -- and the rest of the text, comments and spacing included, is left as written.
        /// </summary>
        public static string Renamed(Project project, Module module, string from, string to)
        {
            if (module == null || !module.IsWritten)
                return module?.Source;

            // Where the text instantiates the module: an instantiation's place is its module's name -- in this text, not a file it includes.
            var instantiated = new HashSet<(int Line, int Column)>();
            ModuleDeclaration declaration = ParseChecked(project, module).Declaration;
            string own = FileOf(module.Name);
            void Visit(IEnumerable<ModuleItem> items)
            {
                foreach (ModuleItem item in items)
                {
                    switch (item)
                    {
                        case ModuleInstantiation instantiation when instantiation.Module == from && instantiation.Location.File == own:
                            instantiated.Add((instantiation.Location.Line, instantiation.Location.Column));
                            break;
                        case GenerateRegion region:
                            Visit(region.Items);
                            break;
                        case GenerateBlock block:
                            Visit(block.Items);
                            break;
                        case LoopGenerate loop when loop.Body != null:
                            Visit(loop.Body.Items);
                            break;
                        case IfGenerate choice:
                            if (choice.Then != null)
                                Visit(choice.Then.Items);
                            if (choice.Else != null)
                                Visit(choice.Else.Items);
                            break;
                    }
                }
            }

            if (declaration != null)
                Visit(declaration.Items);

            string source = module.Source;
            List<Token> tokens = Lexer.Lex(source, FileOf(module.Name), new DiagnosticBag());
            var at = new List<int>();
            for (int i = 0; i < tokens.Count; i++)
            {
                Token token = tokens[i];
                if (token.Kind != TokenKind.Identifier || token.Text != from)
                    continue;

                int before = i - 1;
                while (before >= 0 && (tokens[before].IsKeyword("automatic") || tokens[before].IsKeyword("static")))
                    before--;
                bool header = module.Name == from && before >= 0 && tokens[before].IsKeyword("module");
                if (header || instantiated.Contains((token.Location.Line, token.Location.Column)))
                    at.Add(OffsetOf(source, token.Location.Line, token.Location.Column));
            }

            var renamed = new StringBuilder(source);
            for (int i = at.Count - 1; i >= 0; i--)
                renamed.Remove(at[i], from.Length).Insert(at[i], to);
            return renamed.ToString();
        }

        /// <summary>Where line <paramref name="line"/>, column <paramref name="column"/> is in <paramref name="text"/>, both counted from 1 as the reader counts them.</summary>
        private static int OffsetOf(string text, int line, int column)
        {
            int offset = 0;
            for (int l = 1; l < line; l++)
                offset = text.IndexOf('\n', offset) + 1;
            return offset + column - 1;
        }

        /// <summary>The ports a block of <paramref name="module"/> has: a drawn module's port parts, or a written one's header.</summary>
        public static IReadOnlyList<ModulePort> PortsOf(Project project, Module module)
        {
            if (module == null)
                return Array.Empty<ModulePort>();
            if (module.IsWritten)
                return Read(project, module).Ports;

            var ports = new List<ModulePort>();
            foreach (PortPart port in module.Ports)
                ports.Add(new ModulePort(port.Name, port.Direction, port.Width));
            return ports;
        }

        /// <summary>The project's modules that <paramref name="module"/> instantiates: its blocks', or what its text instantiates.</summary>
        public static IEnumerable<Module> UsesOf(Project project, Module module)
        {
            if (module.IsWritten)
            {
                foreach (string name in ParseChecked(project, module).Uses)
                    if (project.Find(name) is Module used)
                        yield return used;
                yield break;
            }

            foreach (Part part in module.Parts)
                if (part is BlockPart block && project.Find(block.Module) is Module used)
                    yield return used;
        }

        /// <summary>The cells of the library a written module instantiates.</summary>
        public static IEnumerable<string> CellsOf(Project project, Module module)
        {
            if (!module.IsWritten)
                yield break;
            foreach (string name in ParseChecked(project, module).Uses)
                if (BoardImporter.IsCell(name))
                    yield return name;
        }

        /// <summary>A written module's text read and elaborated in <paramref name="project"/>.</summary>
        public static WrittenModule Read(Project project, Module module)
        {
            if (module == null || !module.IsWritten)
                throw new ArgumentException("Only a written module is read.", nameof(module));

            string key = Surroundings(project, module);
            if (Kept.TryGetValue(key, out WrittenModule kept))
                return kept;

            WrittenModule read = ReadAnew(project, module);
            if (Kept.Count >= KeptResults)
                Kept.Clear();
            Kept[key] = read;
            return read;
        }

        private static WrittenModule ReadAnew(Project project, Module module)
        {
            (_, ModuleDeclaration declaration, List<Diagnostic> diagnostics, List<string> uses) = ParseChecked(project, module);
            if (declaration == null || HasError(diagnostics))
                return new WrittenModule(declaration, Array.Empty<ModulePort>(), uses, diagnostics);

            // Elaborated on its own, among what it could instantiate -- the cells with the library's delays, when there is one.
            GateLibrary library = project.Settings?.Library;
            var alone = new SyntaxTree();
            var named = new HashSet<string>(StringComparer.Ordinal);
            foreach (string cell in uses)
                if (BoardImporter.IsCell(cell) && named.Add(cell))
                    alone.Modules.Add(CellLibrary.Build(cell, library));
            foreach (Module other in project.Modules)
            {
                if (other == module || !named.Add(other.Name))
                    continue;
                if (!other.IsWritten)
                {
                    alone.Modules.Add(HeaderOf(other));
                }
                else if (ParseChecked(project, other) is var parsed && parsed.Declaration != null && !HasError(parsed.Diagnostics))
                {
                    // With the cells it uses in turn, as the whole design would have them.
                    alone.Modules.Add(parsed.Declaration);
                    foreach (string cell in parsed.Uses)
                        if (BoardImporter.IsCell(cell) && named.Add(cell))
                            alone.Modules.Add(CellLibrary.Build(cell, library));
                }
            }
            alone.Modules.Add(declaration);

            // As the top, for its ports' widths -- every parameter at its default, which is what a block of it has. Under a
            // gate library it is held to it as it is built, every module but the testbench (Part 14g).
            GateLevelRules rules = library != null && module.Name != project.Bench ? library.RulesFor(module.Name) : null;
            Elaboration elaboration = Elaborator.Elaborate(alone, module.Name, rules);
            string file = FileOf(module.Name);
            Take(elaboration, warnings: true);
            if (elaboration.HasErrors)
                return new WrittenModule(declaration, Array.Empty<ModulePort>(), uses, diagnostics);

            // And as an instance, which is all a written module ever is in the design: its inputs are wires there, where
            // at the top they are variables a process may write, and its own name roots no path. Under a top whose name
            // is no identifier, so no module can take it.
            var host = new ModuleDeclaration { Name = HostName, HasPortList = true };
            var instantiation = new ModuleInstantiation { Module = module.Name };
            instantiation.Instances.Add(new ModuleInstance { Name = "u" });
            host.Items.Add(instantiation);
            alone.Modules.Add(host);
            Elaboration asInstance = Elaborator.Elaborate(alone, HostName);
            Take(asInstance, warnings: false, only: ElaborationCodes.NeverRead);
            if (asInstance.HasErrors)
                return new WrittenModule(declaration, Array.Empty<ModulePort>(), uses, diagnostics);

            // Its own faults where they are; another module's -- one it instantiates -- at its first line.
            // As an instance it is linted for what it sets and never reads (Part 14e), which is said only inside one.
            void Take(Elaboration run, bool warnings, string only = null)
            {
                foreach (Diagnostic d in run.Diagnostics)
                {
                    bool lint = only != null && d.Code == only && d.Location.File == file;
                    if (d.Severity != DiagnosticSeverity.Error && !(warnings && d.Location.File == file) && !lint)
                        continue;
                    diagnostics.Add(d.Location.File == file ? d : new Diagnostic(d.Severity, new SourceLocation(file, 1, 1), d.Code, d.Message));
                }
            }

            var ports = new List<ModulePort>();
            foreach (ElaboratedPort port in elaboration.Ports)
            {
                // An inout is kept (Part 14f): the top may have one, and a block of a module with one is the rules' to refuse.
                if (port.Width > BoardRules.MaxWidth)
                    diagnostics.Add(new Diagnostic(DiagnosticSeverity.Error, PortLocation(declaration, port.Name, file), ElaborationCodes.TooLarge,
                        $"{port.Name} is {port.Width} bits wide; a block's pin can be {BoardRules.MaxWidth} at most."));
                else
                    ports.Add(new ModulePort(port.Name, port.Direction, port.Width,
                        elaboration.Design.Signals[port.Signal.Index].Kind == SignalKind.Net));
            }

            return HasError(diagnostics)
                ? new WrittenModule(declaration, Array.Empty<ModulePort>(), uses, diagnostics)
                : new WrittenModule(declaration, ports, uses, diagnostics);
        }

        /// <summary>The text parsed, and held to being one module of the module's own name: what it says, and what it instantiates.</summary>
        private static (SyntaxTree Tree, ModuleDeclaration Declaration, List<Diagnostic> Diagnostics, List<string> Uses) ParseChecked(Project project, Module module)
        {
            string file = FileOf(module.Name);
            (SyntaxTree tree, List<SourceLocation> timeDeclarations) = ParseText(module.Source, file, project.Files);
            var diagnostics = new List<Diagnostic>(tree.Diagnostics);
            var uses = new List<string>();
            void Error(SourceLocation at, string message) =>
                diagnostics.Add(new Diagnostic(DiagnosticSeverity.Error, at, DiagnosticCodes.NotInModule, message));

            if (tree.Modules.Count == 0)
            {
                if (!HasError(diagnostics))
                    Error(new SourceLocation(file, 1, 1), $"There is no module here: write module {module.Name} (...); ... endmodule.");
                return (tree, null, diagnostics, uses);
            }

            ModuleDeclaration declaration = tree.Modules[0];
            if (tree.Modules.Count > 1)
                Error(tree.Modules[1].Location, "Write one module here; another module is a block of its own.");
            foreach (ModuleDeclaration other in tree.Interfaces)
                Error(other.Location, "An interface cannot be written in a block, so far: only the module.");
            foreach (PackageDeclaration package in tree.Packages)
                Error(package.Location, "A package cannot be written in a block, so far: only the module.");
            foreach (ModuleItem item in tree.UnitItems)
                Error(item.Location, "Write everything inside the module: nothing goes before module or after endmodule.");
            if (tree.Timescale != null)
                Error(new SourceLocation(file, 1, 1), "A block keeps the project's time unit: set it with UNIT, not a `timescale.");
            foreach (SourceLocation at in timeDeclarations)
                Error(at, "A block keeps the project's time unit: set it with UNIT, not a timeunit or a timeprecision.");
            if (!string.Equals(declaration.Name, module.Name, StringComparison.Ordinal))
                Error(declaration.Location, $"This block is the module {module.Name}, so its code is module {module.Name}, not {declaration.Name}.");

            var seen = new HashSet<string>(StringComparer.Ordinal);
            void Visit(IEnumerable<ModuleItem> items)
            {
                foreach (ModuleItem item in items)
                {
                    switch (item)
                    {
                        case ModuleInstantiation instantiation when seen.Add(instantiation.Module):
                            uses.Add(instantiation.Module);
                            break;
                        case GenerateRegion region:
                            Visit(region.Items);
                            break;
                        case GenerateBlock block:
                            Visit(block.Items);
                            break;
                        case LoopGenerate loop when loop.Body != null:
                            Visit(loop.Body.Items);
                            break;
                        case IfGenerate choice:
                            if (choice.Then != null)
                                Visit(choice.Then.Items);
                            if (choice.Else != null)
                                Visit(choice.Else.Items);
                            break;
                    }
                }
            }

            Visit(declaration.Items);
            Located(diagnostics, module.Source, file);
            return (tree, declaration, diagnostics, uses);
        }

        /// <summary>
        /// A fault in a file the text includes (Part 13b), placed at the include in the text -- where the code editor can mark
        /// it -- and saying where in that file it is.
        /// </summary>
        private static void Located(List<Diagnostic> diagnostics, string source, string file)
        {
            for (int i = 0; i < diagnostics.Count; i++)
            {
                Diagnostic d = diagnostics[i];
                if (d.Location.File == null || string.Equals(d.Location.File, file, StringComparison.OrdinalIgnoreCase))
                    continue;

                var at = new SourceLocation(file, 1, 1);
                string[] lines = (source ?? "").Replace("\r\n", "\n").Split('\n');
                for (int l = 0; l < lines.Length; l++)
                {
                    int include = lines[l].IndexOf("`include", StringComparison.Ordinal);
                    if (include >= 0 && lines[l].IndexOf(d.Location.File, StringComparison.OrdinalIgnoreCase) > include)
                    {
                        at = new SourceLocation(file, l + 1, include + 1);
                        break;
                    }
                }

                diagnostics[i] = new Diagnostic(d.Severity, at, d.Code,
                    $"In {d.Location.File}, line {d.Location.Line.ToString(CultureInfo.InvariantCulture)}: {d.Message}");
            }
        }

        /// <summary>
        /// The text read, kept for the same text: the rules ask on every edit, of every written module. With where it says
        /// timeunit or timeprecision, which the parser passes over without a trace. An <c>`include</c> reads the project's
        /// files (Part 13b), and the ones the text names are part of what is kept.
        /// </summary>
        private static (SyntaxTree Tree, List<SourceLocation> TimeDeclarations) ParseText(string text, string file, IReadOnlyList<ProjectFile> files)
        {
            string key = file + "\n" + text + ProjectFiles.KeyOf(files, text);
            if (Parsed.TryGetValue(key, out (SyntaxTree, List<SourceLocation>) kept))
                return kept;

            MemorySourceFiles sources = files.Count == 0 ? null : ProjectFiles.Sources(files);
            SyntaxTree tree = SyntaxReader.ReadText(text, file, sources);
            var times = new List<SourceLocation>();
            foreach (Token token in Preprocessor.Run(file, text ?? string.Empty, sources, new DiagnosticBag(), out _))
                if (token.IsKeyword("timeunit") || token.IsKeyword("timeprecision"))
                    times.Add(token.Location);

            if (Parsed.Count >= KeptResults)
                Parsed.Clear();
            Parsed[key] = (tree, times);
            return (tree, times);
        }

        /// <summary>A drawn module's header alone: its ports, and nothing inside.</summary>
        private static ModuleDeclaration HeaderOf(Module module)
        {
            var header = new ModuleDeclaration { Name = module.Name, HasPortList = true };
            foreach (PortPart port in module.Ports)
                header.Ports.Add(new PortDeclaration { Direction = port.Direction, Type = Sv.Logic(port.Width), Name = port.Name });
            return header;
        }

        /// <summary>What a reading depends on: the text, and the headers and texts of every other module of the project.</summary>
        private static string Surroundings(Project project, Module module)
        {
            var key = new StringBuilder();
            key.Append(module.Name).Append('\u0001').Append(module.Source).Append(ProjectFiles.KeyOf(project.Files, module.Source));
            // The library it is held to, and whether it is the testbench, which is not (Part 14g).
            if (project.Settings?.Library is GateLibrary library)
                key.Append('\u0005').Append(module.Name == project.Bench ? "bench" : library.Key());
            foreach (Module other in project.Modules)
            {
                if (other == module)
                    continue;
                key.Append('\u0002').Append(other.Name);
                if (other.IsWritten)
                {
                    key.Append('\u0003').Append(other.Source).Append(ProjectFiles.KeyOf(project.Files, other.Source));
                    continue;
                }

                foreach (PortPart port in other.Ports)
                    key.Append('\u0004').Append(port.Name).Append(' ').Append((int)port.Direction).Append(' ').Append(port.Width);
            }

            return key.ToString();
        }

        private static SourceLocation PortLocation(ModuleDeclaration declaration, string port, string file)
        {
            foreach (PortDeclaration p in declaration.Ports)
                if (p.Name == port)
                    return p.Location;
            return new SourceLocation(file, 1, 1);
        }

        private static bool HasError(List<Diagnostic> diagnostics)
        {
            foreach (Diagnostic d in diagnostics)
                if (d.Severity == DiagnosticSeverity.Error)
                    return true;
            return false;
        }
    }
}
