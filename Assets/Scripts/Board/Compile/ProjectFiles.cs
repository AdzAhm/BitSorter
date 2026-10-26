using System;
using System.Collections.Generic;
using System.Text;
using BitSorter.Sim;
using BitSorter.SystemVerilog.Syntax;

namespace BitSorter.Board
{
    /// <summary>
    /// A project's other files (<see cref="Project.Files"/>, Part 13b) as the reader and the engine take them: what an
    /// <c>`include</c> reads, and what a run's <c>$readmemh</c> reads and <c>$writememh</c> writes.
    /// </summary>
    public static class ProjectFiles
    {
        /// <summary>The longest name a file may have.</summary>
        public const int MaxName = 64;

        /// <summary>What an <c>`include</c> in the project's code reads.</summary>
        public static MemorySourceFiles Sources(IReadOnlyList<ProjectFile> files)
        {
            var sources = new MemorySourceFiles();
            foreach (ProjectFile file in files)
                sources.Add(file.Name, file.Text);
            return sources;
        }

        /// <summary>
        /// What a run reads and writes: a copy of the files for each run, so what one writes stays with that run. A name is
        /// found whatever its case, as an include finds it.
        /// </summary>
        public static ISimFiles ForRun(Project project)
        {
            var files = new RunFiles();
            foreach (ProjectFile file in project.Files)
                files.Write(file.Name, file.Text ?? "");
            return files;
        }

        /// <summary>
        /// Whether <paramref name="name"/> can be a file's: a bare name -- letters, digits, <c>.</c>, <c>_</c> and <c>-</c>. A
        /// <c>.sv</c> of nothing but defines may be one; whether it is a module's file is the rules' to say.
        /// </summary>
        public static bool IsValidName(string name, out string why)
        {
            why = null;
            if (string.IsNullOrEmpty(name))
                why = "A file needs a name.";
            else if (name.Length > MaxName)
                why = $"{name} is longer than a file's name can be, {MaxName} characters.";
            else if (name[0] == '.')
                why = $"{name} starts with a dot: a file's name starts with a letter or a digit.";
            else
                foreach (char c in name)
                    if (!(c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z' || c >= '0' && c <= '9' || c == '.' || c == '_' || c == '-'))
                    {
                        why = $"{name} has a {c} in it: a file's name is letters, digits, dots, _ and -, with no folder.";
                        break;
                    }

            return why == null;
        }

        /// <summary>Whether a file of this name is SystemVerilog to read as modules (<c>.sv</c> or <c>.v</c>) rather than a file the code uses.</summary>
        public static bool IsSource(string name) =>
            name.EndsWith(".sv", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".v", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// What reading <paramref name="text"/> depends on among the files: each it names, and each those name in turn -- an
        /// <c>`include</c> names its file. Part of a cache's key, so that a long memory image no code names costs nothing.
        /// </summary>
        internal static string KeyOf(IReadOnlyList<ProjectFile> files, string text)
        {
            if (files.Count == 0 || string.IsNullOrEmpty(text))
                return "";

            var key = new StringBuilder();
            var taken = new bool[files.Count];
            var reading = new List<string> { text };
            for (int r = 0; r < reading.Count; r++)
            {
                for (int i = 0; i < files.Count; i++)
                {
                    if (taken[i] || string.IsNullOrEmpty(files[i].Name) || reading[r].IndexOf(files[i].Name, StringComparison.OrdinalIgnoreCase) < 0)
                        continue;
                    taken[i] = true;
                    key.Append('\u0005').Append(files[i].Name).Append('\u0006').Append(files[i].Text);
                    reading.Add(files[i].Text ?? "");
                }
            }

            return key.ToString();
        }

        /// <summary>A run's files by name, whatever the case: <c>$readmemh("ROM.hex")</c> finds rom.hex, as an include would.</summary>
        private sealed class RunFiles : ISimFiles
        {
            private readonly Dictionary<string, string> _files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            public bool TryRead(string path, out string text) => _files.TryGetValue(SourcePaths.Normalise(path), out text);

            public void Write(string path, string text) => _files[SourcePaths.Normalise(path)] = text ?? "";
        }
    }
}
