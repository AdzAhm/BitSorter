using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using NUnit.Framework;

namespace BitSorter.Ci.Oracle
{
    /// <summary>One run of a design under ModelSim: the library made, the compile, the simulation, and the folder it ran in.</summary>
    /// <remarks>Disposing it deletes the folder. Read anything the run wrote -- a VCD, a memory dump -- first.</remarks>
    public sealed class ModelSimRun : IDisposable
    {
        public ModelSimRun(string folder, ProcessResult compile, ProcessResult simulate)
        {
            Folder = folder;
            Compile = compile;
            Simulate = simulate;
        }

        public string Folder { get; }

        /// <summary>vlib, then vlog: a library failing to be made reads as a compile that failed.</summary>
        public ProcessResult Compile { get; }

        /// <summary>vsim, or a result with no output if the compile failed.</summary>
        public ProcessResult Simulate { get; }

        public bool Succeeded => Compile.Succeeded && Simulate.Succeeded && !Simulate.StandardOutput.Contains("# ** Error");

        public string Explain() =>
            !Compile.Succeeded
                ? "vlog " + (Compile.TimedOut ? "ran out of time" : "failed") + ":\n" + Compile.StandardError + Compile.StandardOutput
                : "vsim " + (Simulate.TimedOut ? "ran out of time" : "failed") + ":\n" + Simulate.StandardError + Simulate.StandardOutput;

        /// <summary>
        /// What the design printed, line by line: ModelSim's transcript has every line of its own begin with <c># </c>, and
        /// its own remarks -- what it loads, its notes, where a <c>$stop</c> broke -- are left out.
        /// </summary>
        public string[] Printed()
        {
            var lines = new List<string>();
            bool running = false;
            foreach (string raw in Simulate.StandardOutput.Replace("\r", "").Split('\n'))
            {
                if (!raw.StartsWith("#", StringComparison.Ordinal))
                    continue;
                string line = raw.Length > 1 && raw[1] == ' ' ? raw.Substring(2) : raw.Substring(1);
                if (line == " run -all")
                {
                    running = true;
                    continue;
                }
                if (!running)
                    continue;
                if (line.StartsWith("** ", StringComparison.Ordinal) || line.StartsWith("   Time: ", StringComparison.Ordinal)
                    || line.StartsWith("Break in ", StringComparison.Ordinal) || line.StartsWith("Stopped at ", StringComparison.Ordinal)
                    || line.StartsWith(" quit", StringComparison.Ordinal) || line.StartsWith("End time: ", StringComparison.Ordinal)
                    || line.StartsWith("Errors: ", StringComparison.Ordinal))
                    continue;
                lines.Add(line);
            }
            return lines.ToArray();
        }

        public void Dispose()
        {
            try { Directory.Delete(Folder, recursive: true); }
            catch (IOException) { /* a viewer left a file open; the temp folder will be cleared eventually */ }
            catch (UnauthorizedAccessException) { /* ModelSim's library still held for a moment; cleared with the temp folder */ }
        }
    }

    /// <summary>
    /// ModelSim, the course's own simulator, as a second oracle beside Icarus (Part 15i-4, Ahmad's choice, 2026-11-02):
    /// above all for what two simulators can settle differently, which one oracle alone cannot show.
    /// </summary>
    /// <remarks>
    /// **Where it is looked for**, in order: the folder <c>MODELSIM_HOME</c> names (or its <c>win32aloem</c> or <c>bin</c>),
    /// every folder on PATH, then the Intel FPGA Starter Edition's folders under <c>C:\intelFPGA</c>, where its installer
    /// puts it.
    ///
    /// **Missing is always a skip.** CI has no ModelSim -- it is licensed, not a package a runner installs -- so these tests
    /// run on a machine that has it, as the course's do, and Icarus remains the oracle CI requires.
    ///
    /// **How it is run**: <c>vlib work</c>, <c>vlog -sv</c> of the sources, then <c>vsim -c</c> with a do-script that dumps
    /// every signal to a VCD and runs to the end -- the commands a student's run.do gives it, without the wave window.
    /// </remarks>
    public sealed class ModelSim
    {
        private const string HomeVariable = "MODELSIM_HOME";
        private const string IntelRoot = @"C:\intelFPGA";

        private ModelSim(string folder)
        {
            Folder = folder;
        }

        /// <summary>The folder vlib, vlog and vsim are in.</summary>
        public string Folder { get; }

        private static string Exe(string name) =>
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? name + ".exe" : name;

        private string Tool(string name) => Path.Combine(Folder, Exe(name));

        /// <summary>ModelSim if it can be found, or null.</summary>
        public static ModelSim Find()
        {
            var folders = new List<string>();
            string home = Environment.GetEnvironmentVariable(HomeVariable);
            if (!string.IsNullOrEmpty(home))
            {
                folders.Add(home);
                folders.Add(Path.Combine(home, "win32aloem"));
                folders.Add(Path.Combine(home, "bin"));
            }
            string path = Environment.GetEnvironmentVariable("PATH") ?? "";
            folders.AddRange(path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries));
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && Directory.Exists(IntelRoot))
                foreach (string version in Directory.GetDirectories(IntelRoot).OrderByDescending(v => v, StringComparer.Ordinal))
                    folders.Add(Path.Combine(version, "modelsim_ase", "win32aloem"));

            foreach (string folder in folders)
            {
                string clean = folder.Trim('"');
                if (File.Exists(Path.Combine(clean, Exe("vsim"))) && File.Exists(Path.Combine(clean, Exe("vlog")))
                    && File.Exists(Path.Combine(clean, Exe("vlib"))))
                    return new ModelSim(clean);
            }
            return null;
        }

        /// <summary>ModelSim, or the test is ignored here: never required, since CI cannot have it.</summary>
        public static ModelSim Require()
        {
            ModelSim modelSim = Find();
            if (modelSim == null)
                Assert.Ignore("ModelSim was not found on PATH, in " + HomeVariable + " or under " + IntelRoot +
                    ". These tests run where it is installed, as the course's ModelSim is.");
            return modelSim;
        }

        /// <summary>
        /// Compiles <paramref name="sources"/> and runs <paramref name="top"/> to its end, every signal dumped to
        /// <c>oracle.vcd</c> -- or, given <paramref name="ownDump"/>, the design dumping itself.
        /// </summary>
        /// <param name="parameters">The top's parameters, as <c>NAME=value</c> each: vsim's <c>-G</c>.</param>
        public ModelSimRun Simulate(IEnumerable<string> sources, string top, IEnumerable<string> includeDirs = null, TimeSpan? timeout = null,
            IEnumerable<string> dataFiles = null, IEnumerable<string> parameters = null, string ownDump = null)
        {
            TimeSpan limit = timeout ?? TimeSpan.FromSeconds(60);
            string folder = Path.Combine(Path.GetTempPath(), "bitsorter-modelsim", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            foreach (string data in dataFiles ?? Enumerable.Empty<string>())
                File.Copy(data, Path.Combine(folder, Path.GetFileName(data)));

            ProcessResult library = ProcessRunner.Run(Tool("vlib"), new[] { "work" }, folder, limit);
            if (!library.Succeeded)
                return new ModelSimRun(folder, library, new ProcessResult(-1, "", "", timedOut: false));

            var compile = new List<string> { "-sv", "-work", "work" };
            foreach (string dir in includeDirs ?? Enumerable.Empty<string>())
                compile.Add("+incdir+" + dir);
            compile.AddRange(sources);
            ProcessResult vlog = ProcessRunner.Run(Tool("vlog"), compile, folder, limit);
            if (!vlog.Succeeded || vlog.StandardOutput.Contains("** Error"))
                return new ModelSimRun(folder, new ProcessResult(vlog.Succeeded ? 1 : vlog.ExitCode, vlog.StandardOutput, vlog.StandardError, vlog.TimedOut),
                    new ProcessResult(-1, "", "", timedOut: false));

            string script = (ownDump == null ? "vcd file oracle.vcd; vcd add -r /*; " : "") + "run -all; quit -f";
            var simulate = new List<string> { "-c", "-voptargs=+acc" };
            foreach (string given in parameters ?? Enumerable.Empty<string>())
                simulate.Add("-G" + given);
            simulate.AddRange(top.Split(',').Select(t => t.Trim()));
            simulate.Add("-do");
            simulate.Add(script);
            ProcessResult vsim = ProcessRunner.Run(Tool("vsim"), simulate, folder, limit);
            return new ModelSimRun(folder, vlog, vsim);
        }
    }
}
