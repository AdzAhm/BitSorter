namespace BitSorter.SystemVerilog.Syntax
{
    /// <summary>
    /// The front door to reading SystemVerilog: a file through <see cref="ISourceFiles"/>, or text,
    /// into a <see cref="SyntaxTree"/> with every diagnostic on it.
    /// </summary>
    public static class SyntaxReader
    {
        /// <summary>Reads the file at <paramref name="path"/>, and whatever it includes, through <paramref name="files"/>.</summary>
        public static SyntaxTree Read(string path, ISourceFiles files) => Read(path, files, null);

        private static SyntaxTree Read(string path, ISourceFiles files, ExpansionBudget budget)
        {
            if (files == null || !files.TryRead(path, out string text))
            {
                var missing = new SyntaxTree();
                missing.Diagnostics.Add(new Diagnostic(DiagnosticSeverity.Error, new SourceLocation(path, 1, 1),
                    DiagnosticCodes.IncludeNotFound, $"The file \"{path}\" was not found."));
                return missing;
            }

            return ReadText(text, path, files, budget);
        }

        /// <summary>
        /// Reads several files into one tree, as a simulator compiles a list of files: every module of
        /// each, in the order given, and every diagnostic. Each file is read on its own, so a
        /// <c>`define</c> in one does not reach the next.
        /// </summary>
        public static SyntaxTree ReadAll(System.Collections.Generic.IEnumerable<string> paths, ISourceFiles files)
        {
            var all = new SyntaxTree();
            var budget = new ExpansionBudget();   // what macros and includes make is held across the whole list (Part 13f)
            foreach (string path in paths)
            {
                SyntaxTree one = Read(path, files, budget);
                all.Modules.AddRange(one.Modules);
                all.UnitItems.AddRange(one.UnitItems);
                all.Packages.AddRange(one.Packages);
                all.Interfaces.AddRange(one.Interfaces);
                all.Diagnostics.AddRange(one.Diagnostics);
                all.Files.AddRange(one.Files);
                all.Timescale = all.Timescale ?? one.Timescale;
            }

            return all;
        }

        /// <summary>Reads <paramref name="text"/> as the file <paramref name="path"/>; its includes come from <paramref name="files"/>.</summary>
        public static SyntaxTree ReadText(string text, string path = "input.sv", ISourceFiles files = null) => ReadText(text, path, files, null);

        private static SyntaxTree ReadText(string text, string path, ISourceFiles files, ExpansionBudget budget)
        {
            var diagnostics = new DiagnosticBag();
            var tree = new SyntaxTree();

            var tokens = Preprocessor.Run(path, text ?? string.Empty, files, diagnostics, out Preprocessor done, budget);
            new Parser(tokens, diagnostics).ParseInto(tree);

            tree.Timescale = done.Timescale;
            tree.Files.AddRange(done.FilesRead);
            tree.Diagnostics.AddRange(diagnostics.Items);
            return tree;
        }
    }
}
