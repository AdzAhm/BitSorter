using System;
using System.Collections.Generic;
using System.Text;
using BitSorter.SystemVerilog.Syntax;

namespace BitSorter.Board
{
    /// <summary>
    /// A project as a zip ModelSim can open (Part 13a): a folder named for it holding every file the project prints as
    /// (<see cref="BoardPrinter"/> -- the cells it uses, each module after the modules it uses, the testbench last) and a
    /// <c>run.do</c> that compiles them in that order, starts the simulation, adds every signal to the wave window and
    /// runs it. Unzipped, the student changes ModelSim's directory to the folder and types <c>do run.do</c>. The project's
    /// other files -- includes, memory images -- are in the folder too.
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

            // Its other files beside them (Part 13b), where an include and a $readmemh of a bare name find them.
            foreach (ProjectFile file in project.Files)
                entries.Add(new ZipEntry(folder + "/" + file.Name, utf8.GetBytes(IsCode(file.Name) ? Lexer.Straightened(file.Text ?? "") : file.Text ?? "")));
            entries.Add(new ZipEntry(folder + "/" + RunScript, utf8.GetBytes(RunDo(project, compiled, files))));
            return ZipWriter.Write(entries, when);
        }

        /// <summary>
        /// The folder inside the zip: the project's name when it is a SystemVerilog name, and so a safe one for a folder, and
        /// "project" otherwise -- a save edited by hand could say <c>../outside</c>, and no path may leave the folder.
        /// </summary>
        /// <summary>Whether a project file is read as code -- a header or a source an include reads -- not data, as an image is.</summary>
        private static bool IsCode(string name) =>
            name.EndsWith(".sv", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".svh", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".v", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".vh", StringComparison.OrdinalIgnoreCase);

        public static string FolderOf(Project project) =>
            BoardRules.WhyNotAName(project?.Name) == null ? project.Name : "project";

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
                script.Append(' ').Append(Tcl(file.Path));
            script.Append('\n');
            // The testbench's parameters chosen (Part 15h-6c), each as its member's value: ModelSim's -G reads a member's name as
            // a string of characters, and would run another program without a word (measured, 2026-11-02). Each by the
            // testbench's own path, -G/tb/PROGRAM: a bare -GPROGRAM sets every parameter of the name in the design, and a
            // processor's memory takes its image's file name by one (found by the ModelSim oracle, Part 15i-4).
            string bench = "/" + compiled.Top + "/";
            var given = new StringBuilder();
            foreach (BenchChoice choice in BenchChoices.Of(compiled, project.Settings))
            {
                if (project.Settings?.BenchParameters == null || !project.Settings.BenchParameters.ContainsKey(choice.Name))
                    continue;
                if (choice.ChosenValue is long value)
                {
                    script.Append("# ").Append(choice.Name).Append(" is ").Append(choice.Chosen).Append(", given as its value\n");
                    given.Append(' ').Append(Tcl("-G" + bench + choice.Name + "=" + value.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                }
                else
                {
                    // Its value only elaboration works out: said, rather than ModelSim running another program unseen (the review).
                    script.Append("# ").Append(choice.Name).Append(" is ").Append(choice.Chosen)
                        .Append(", whose value the code works out: add -G").Append(bench).Append(choice.Name).Append("=<its value> to vsim\n");
                }
            }
            script.Append("vsim -voptargs=+acc").Append(given).Append(' ').Append(Tcl("work." + compiled.Top)).Append('\n');
            script.Append("add wave -r /*\n");
            script.Append("run -all\n");
            return script.ToString();
        }

        /// <summary>
        /// A word of the script as Tcl reads it: in braces when it holds a <c>$</c>, which a SystemVerilog name may, and Tcl
        /// would otherwise take for a variable. A name holds no brace, so braces always close where they should.
        /// </summary>
        private static string Tcl(string word) => word.IndexOf('$') >= 0 ? "{" + word + "}" : word;
    }
}
