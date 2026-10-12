namespace BitSorter.SystemVerilog.Syntax
{
    /// <summary>
    /// The front door to reading SystemVerilog: a file through <see cref="ISourceFiles"/>, or text,
    /// into a <see cref="SyntaxTree"/> with every diagnostic on it.
    /// </summary>
    public static class SyntaxReader
    {
        /// <summary>Reads the file at <paramref name="path"/>, and whatever it includes, through <paramref name="files"/>.</summary>
        public static SyntaxTree Read(string path, ISourceFiles files)
        {
            if (files == null || !files.TryRead(path, out string text))
            {
                var missing = new SyntaxTree();
                missing.Diagnostics.Add(new Diagnostic(DiagnosticSeverity.Error, new SourceLocation(path, 1, 1),
                    DiagnosticCodes.IncludeNotFound, $"The file \"{path}\" was not found."));
                return missing;
            }

            return ReadText(text, path, files);
        }

        /// <summary>Reads <paramref name="text"/> as the file <paramref name="path"/>; its includes come from <paramref name="files"/>.</summary>
        public static SyntaxTree ReadText(string text, string path = "input.sv", ISourceFiles files = null)
        {
            var diagnostics = new DiagnosticBag();
            var tree = new SyntaxTree();

            var tokens = Preprocessor.Run(path, text ?? string.Empty, files, diagnostics, out Preprocessor done);
            new Parser(tokens, diagnostics).ParseInto(tree);

            tree.Timescale = done.Timescale;
            tree.Files.AddRange(done.FilesRead);
            tree.Diagnostics.AddRange(diagnostics.Items);
            return tree;
        }
    }
}
