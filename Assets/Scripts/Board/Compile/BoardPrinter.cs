using System.Collections.Generic;
using System.Text;
using BitSorter.SystemVerilog.Syntax;

namespace BitSorter.Board
{
    /// <summary>One printed file: its name and its text.</summary>
    public readonly struct PrintedSource
    {
        public PrintedSource(string path, string text)
        {
            Path = path;
            Text = text;
        }

        public readonly string Path;
        public readonly string Text;

        public override string ToString() => Path;
    }

    /// <summary>
    /// A project as SystemVerilog files, a module a file as the course writes them: the cells first,
    /// then each module after the modules it uses, then the testbench -- the order a simulator should
    /// read them in. Printed from the same tree the engine runs (<see cref="BoardCompiler"/>), so
    /// reading the files back and running them gives the same history (invariant 4). A written module
    /// is its own text, which is what that tree was read from.
    /// </summary>
    public static class BoardPrinter
    {
        public static IReadOnlyList<PrintedSource> Print(Project project)
        {
            CompiledProject compiled = BoardCompiler.Compile(project);
            return compiled.Succeeded ? Print(compiled) : new PrintedSource[0];
        }

        public static IReadOnlyList<PrintedSource> Print(CompiledProject compiled)
        {
            var printed = new List<PrintedSource>();
            var tree = compiled.Tree;

            // A project not in ns says its unit at the top of every file, as the course's files do, so
            // each file is read in it whichever order a simulator is given them in.
            string head = tree.Timescale == null ? "" : "`timescale " + tree.Timescale + "\n\n";

            if (compiled.Cells.Count > 0)
            {
                var cells = new StringBuilder(head);
                cells.Append("// BitSorter's cell library: one module a gate, each with a rise delay (Tpdlh, output\n");
                cells.Append("// low to high) and a fall delay (Tpdhl) that an instance sets. Only the cells the\n");
                cells.Append("// design uses are here.\n");
                foreach (ModuleDeclaration module in tree.Modules)
                {
                    if (!Contains(compiled.Cells, module.Name))
                        continue;
                    cells.Append('\n').Append(Printer.Print(module));
                }

                printed.Add(new PrintedSource(CellLibrary.FileName, cells.ToString()));
            }

            foreach (CompiledModule module in compiled.Modules)
            {
                var text = new StringBuilder(head);
                if (module.Module.IsWritten)
                {
                    // Its own text, as written (Part 12d): the tree the engine runs was read from it.
                    text.Append("// ").Append(module.Module.Name).Append(": written in BitSorter.\n\n").Append(module.Module.Source);
                    printed.Add(new PrintedSource(module.Module.Name + ".sv", text.ToString()));
                    continue;
                }

                text.Append("// ").Append(module.Module.Name).Append(": drawn in BitSorter.");
                if (compiled.Cells.Count > 0)
                    text.Append(" Gates with a delay are cells of ").Append(CellLibrary.FileName).Append('.');
                text.Append("\n\n").Append(Printer.Print(module.Declaration));
                printed.Add(new PrintedSource(module.Module.Name + ".sv", text.ToString()));
            }

            if (compiled.Testbench != null)
            {
                foreach (ModuleDeclaration module in tree.Modules)
                {
                    if (module.Name != compiled.Testbench)
                        continue;
                    printed.Add(new PrintedSource(module.Name + ".sv", head + "// " + module.Name + ": made by BitSorter from the stimulus.\n\n" + Printer.Print(module)));
                }
            }

            return printed;
        }

        /// <summary>Printed files as sources a reader can take, and the paths in the order to read them.</summary>
        public static (MemorySourceFiles Files, List<string> Paths) AsSources(IReadOnlyList<PrintedSource> printed)
        {
            var files = new MemorySourceFiles();
            var paths = new List<string>();
            foreach (PrintedSource source in printed)
            {
                files.Add(source.Path, source.Text);
                paths.Add(source.Path);
            }

            return (files, paths);
        }

        private static bool Contains(IReadOnlyList<string> names, string name)
        {
            foreach (string each in names)
                if (each == name)
                    return true;
            return false;
        }
    }
}
