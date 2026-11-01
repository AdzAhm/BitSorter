using System;
using System.Collections.Generic;
using BitSorter.Board;

namespace BitSorter.ViewModel
{
    /// <summary>
    /// The SystemVerilog a document is (Part 12e): every file it prints as, in the order a simulator should read them --
    /// the cells it uses, each module after the modules it uses, the testbench last -- for the dock's CODE tab to show as it
    /// is drawn. A drawn module is its printed text, which is the text the run was made from; a written one is its own.
    /// </summary>
    public sealed class CodeListing
    {
        private CodeListing(Project project, IReadOnlyList<PrintedSource> files, string testbench)
        {
            Project = project;
            Files = files;
            Testbench = testbench;
        }

        /// <summary>A listing of nothing: what a document that does not compile prints as.</summary>
        public static readonly CodeListing Empty = new CodeListing(null, Array.Empty<PrintedSource>(), null);

        /// <summary>The document the listing was printed from.</summary>
        public Project Project { get; }

        /// <summary>The files, in the order a simulator should read them.</summary>
        public IReadOnlyList<PrintedSource> Files { get; }

        /// <summary>The testbench's module, or null when the document has no stimulus.</summary>
        public string Testbench { get; }

        /// <summary><paramref name="project"/> printed, as an export would print it.</summary>
        public static CodeListing Of(Project project)
        {
            if (project == null)
                return Empty;
            CompiledProject compiled = BoardCompiler.Compile(project);
            return compiled.Succeeded
                ? new CodeListing(project, BoardPrinter.Print(compiled), compiled.Testbench)
                : new CodeListing(project, Array.Empty<PrintedSource>(), null);   // nothing, for this document: not printed again until it changes
        }

        /// <summary>The file module <paramref name="module"/> is in, or -1.</summary>
        public int FileOf(string module)
        {
            for (int i = 0; i < Files.Count; i++)
                if (string.Equals(Files[i].Path, module + ".sv", StringComparison.Ordinal))
                    return i;
            return -1;
        }

        /// <summary>
        /// The line of file <paramref name="index"/> its module's own code begins on (Part 15h-5): past the lines BitSorter
        /// prints over a written module -- its unit, and the comment that says it was written here -- so the listing numbers
        /// it as its editor does, and a line of it is one line everywhere; the first for any other file.
        /// </summary>
        public int BodyLine(int index)
        {
            if (Project == null || index < 0 || index >= Files.Count)
                return 1;
            string path = Files[index].Path, text = Files[index].Text;
            Module module = path.EndsWith(".sv", StringComparison.Ordinal) ? Project.Find(path.Substring(0, path.Length - 3)) : null;
            if (module == null || !module.IsWritten || module.Source == null || text == null || !text.EndsWith(module.Source, StringComparison.Ordinal))
                return 1;
            int line = 1;
            for (int i = 0; i < text.Length - module.Source.Length; i++)
                if (text[i] == '\n')
                    line++;
            return line;
        }

        /// <summary>What file <paramref name="index"/> is called on its chip: CELLS, TESTBENCH, or its module's name.</summary>
        public string Caption(int index)
        {
            if (index < 0 || index >= Files.Count)
                return "";
            string path = Files[index].Path;
            if (path == CellLibrary.FileName)
                return "CELLS";
            if (Testbench != null && path == Testbench + ".sv")
                return "TESTBENCH";
            return path.EndsWith(".sv", StringComparison.Ordinal) ? path.Substring(0, path.Length - 3) : path;
        }
    }
}
