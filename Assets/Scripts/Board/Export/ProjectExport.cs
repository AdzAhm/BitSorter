using System;
using System.Collections.Generic;
using System.Text;

namespace BitSorter.Board
{
    /// <summary>
    /// A project as a zip ModelSim can open (Part 13a): a folder named for it holding every file the project prints as
    /// (<see cref="BoardPrinter"/> -- the cells it uses, each module after the modules it uses, the testbench last) and a
    /// <c>run.do</c> that compiles them in that order, starts the simulation, adds every signal to the wave window and
    /// runs it. Unzipped, the student changes ModelSim's directory to the folder and types <c>do run.do</c>.
    /// </summary>
    public static class ProjectExport
    {
        /// <summary>The script's name, as ModelSim's own examples call it.</summary>
        public const string RunScript = "run.do";

        /// <summary>The zip of <paramref name="project"/>, its files dated <paramref name="when"/>; null for a document that does not compile.</summary>
        public static byte[] Zip(Project project, DateTime when)
        {
            CompiledProject compiled = BoardCompiler.Compile(project);
            if (!compiled.Succeeded)
                return null;

            IReadOnlyList<PrintedSource> files = BoardPrinter.Print(compiled);
            string folder = FolderOf(project);
            var utf8 = new UTF8Encoding(false);
            var entries = new List<ZipEntry>();
            foreach (PrintedSource file in files)
                entries.Add(new ZipEntry(folder + "/" + file.Path, utf8.GetBytes(file.Text)));
            entries.Add(new ZipEntry(folder + "/" + RunScript, utf8.GetBytes(RunDo(project, compiled, files))));
            return ZipWriter.Write(entries, when);
        }

        /// <summary>The folder inside the zip: the project's name, which is a SystemVerilog name and so a safe one for a folder.</summary>
        public static string FolderOf(Project project) =>
            string.IsNullOrEmpty(project?.Name) ? "project" : project.Name;

        /// <summary>
        /// The script: make the work library, compile every file in the order printed, simulate the top -- the testbench
        /// when there is one -- with every signal kept visible, put them all in the wave window, and run to the end.
        /// </summary>
        public static string RunDo(Project project, CompiledProject compiled, IReadOnlyList<PrintedSource> files)
        {
            var script = new StringBuilder();
            script.Append("# ").Append(FolderOf(project)).Append(", exported from BitSorter. In ModelSim, change the directory to this\n");
            script.Append("# folder (File > Change Directory), then type: do ").Append(RunScript).Append('\n');
            script.Append("vlib work\n");
            script.Append("vlog -sv");
            foreach (PrintedSource file in files)
                script.Append(' ').Append(file.Path);
            script.Append('\n');
            script.Append("vsim -voptargs=+acc work.").Append(compiled.Top).Append('\n');
            script.Append("add wave -r /*\n");
            script.Append("run -all\n");
            return script.ToString();
        }
    }
}
