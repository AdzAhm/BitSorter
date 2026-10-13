using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using NUnit.Framework;

namespace BitSorter.Ci.Oracle
{
    /// <summary>One run of a design under Icarus: the compile, the simulation, and the folder it ran in.</summary>
    /// <remarks>
    /// Disposing it deletes the folder. Read anything the run wrote -- a VCD, a memory dump -- first.
    /// </remarks>
    public sealed class IcarusRun : IDisposable
    {
        public IcarusRun(string folder, ProcessResult compile, ProcessResult simulate)
        {
            Folder = folder;
            Compile = compile;
            Simulate = simulate;
        }

        /// <summary>Where the run happened, and where anything it wrote is.</summary>
        public string Folder { get; }

        public ProcessResult Compile { get; }

        /// <summary>The simulation, or a result with no output if the compile failed.</summary>
        public ProcessResult Simulate { get; }

        public bool Succeeded => Compile.Succeeded && Simulate.Succeeded;

        /// <summary>What went wrong, in the tools' own words, for a test's failure message.</summary>
        public string Explain() =>
            !Compile.Succeeded
                ? "iverilog " + (Compile.TimedOut ? "ran out of time" : "failed") + ":\n" + Compile.StandardError + Compile.StandardOutput
                : "vvp " + (Simulate.TimedOut ? "ran out of time" : "failed") + ":\n" + Simulate.StandardError + Simulate.StandardOutput;

        public void Dispose()
        {
            try { Directory.Delete(Folder, recursive: true); }
            catch (IOException) { /* a viewer left a file open; the temp folder will be cleared eventually */ }
        }
    }

    /// <summary>
    /// Icarus Verilog, the second simulator BitSorter's own is checked against.
    /// </summary>
    /// <remarks>
    /// **Why Icarus.** It is 4-state and event-driven, with gate delays, as the course's ModelSim is;
    /// Verilator is 2-state and ignores delays, so it would agree with a wrong answer. Where Icarus and
    /// the language standard disagree, the standard wins and the difference is written down in
    /// Docs/oracle-notes.md -- the oracle is a second opinion, not the authority.
    ///
    /// **Where it is looked for**, in order: the folder <c>ICARUS_HOME</c> names (or its <c>bin</c>),
    /// every folder on PATH, then <c>C:\iverilog\bin</c>, where the Windows installer puts it -- and
    /// does not add it to PATH.
    ///
    /// **Missing is a skip, except where it must not be.** A developer without Icarus can still run
    /// everything else. CI sets <c>BITSORTER_REQUIRE_ICARUS=1</c>, which turns the skip into a failure,
    /// so a green CI run always means the oracle actually ran.
    /// </remarks>
    public sealed class Icarus
    {
        private const string HomeVariable = "ICARUS_HOME";
        private const string RequireVariable = "BITSORTER_REQUIRE_ICARUS";
        private const string WindowsDefault = @"C:\iverilog\bin";

        private Icarus(string iverilog, string vvp)
        {
            Iverilog = iverilog;
            Vvp = vvp;
        }

        /// <summary>The compiler's full path.</summary>
        public string Iverilog { get; }

        /// <summary>The simulator's full path.</summary>
        public string Vvp { get; }

        private static string Exe(string name) =>
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? name + ".exe" : name;

        /// <summary>Icarus if it can be found, or null.</summary>
        public static Icarus Find()
        {
            var folders = new List<string>();

            string home = Environment.GetEnvironmentVariable(HomeVariable);
            if (!string.IsNullOrEmpty(home))
            {
                folders.Add(home);
                folders.Add(Path.Combine(home, "bin"));
            }

            string path = Environment.GetEnvironmentVariable("PATH") ?? "";
            folders.AddRange(path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries));

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                folders.Add(WindowsDefault);

            foreach (string folder in folders)
            {
                string iverilog = Path.Combine(folder.Trim('"'), Exe("iverilog"));
                string vvp = Path.Combine(folder.Trim('"'), Exe("vvp"));

                if (File.Exists(iverilog) && File.Exists(vvp))
                    return new Icarus(iverilog, vvp);
            }

            return null;
        }

        /// <summary>
        /// Icarus, or the test ends here: ignored on a machine without it, failed where it is required.
        /// </summary>
        public static Icarus Require()
        {
            Icarus icarus = Find();
            if (icarus != null)
                return icarus;

            const string missing =
                "Icarus Verilog was not found on PATH, in " + HomeVariable + " or in " + WindowsDefault + ".";

            if (Environment.GetEnvironmentVariable(RequireVariable) == "1")
                Assert.Fail(missing + " " + RequireVariable + " is set, so the oracle must run here.");

            Assert.Ignore(missing + " Install it to run the oracle tests.");
            return null;
        }

        /// <summary>What <c>iverilog -V</c> says: the version, on its first line.</summary>
        public ProcessResult Version() =>
            ProcessRunner.Run(Iverilog, new[] { "-V" }, Path.GetTempPath(), TimeSpan.FromSeconds(30));

        /// <summary>
        /// Compiles <paramref name="sources"/> as SystemVerilog and simulates the result, in a fresh
        /// folder of its own.
        /// </summary>
        /// <param name="sources">Full paths to the source files, in the order the compiler should read them.</param>
        /// <param name="top">The top module, or null to let Icarus pick the one nothing instantiates.</param>
        /// <param name="includeDirs">Folders searched for <c>`include</c> files.</param>
        /// <param name="timeout">For each of the two steps. Thirty seconds if not given.</param>
        /// <param name="dataFiles">
        /// Files the design opens as it runs -- <c>$readmemh</c>'s images -- copied into the run's folder,
        /// where a name with no folder is looked for.
        /// </param>
        public IcarusRun Simulate(
            IEnumerable<string> sources, string top = null, IEnumerable<string> includeDirs = null, TimeSpan? timeout = null,
            IEnumerable<string> dataFiles = null)
        {
            TimeSpan limit = timeout ?? TimeSpan.FromSeconds(30);
            string folder = Path.Combine(Path.GetTempPath(), "bitsorter-oracle", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);

            // -g2012 is the SystemVerilog the course writes. -o names the compiled design.
            var compileArguments = new List<string> { "-g2012", "-o", "design.vvp" };
            if (top != null)
                compileArguments.AddRange(new[] { "-s", top });
            foreach (string dir in includeDirs ?? Enumerable.Empty<string>())
                compileArguments.Add("-I" + dir);
            compileArguments.AddRange(sources);

            foreach (string data in dataFiles ?? Enumerable.Empty<string>())
                File.Copy(data, Path.Combine(folder, Path.GetFileName(data)));

            ProcessResult compile = ProcessRunner.Run(Iverilog, compileArguments, folder, limit);
            if (!compile.Succeeded)
                return new IcarusRun(folder, compile, new ProcessResult(-1, "", "", timedOut: false));

            // -n makes $stop end the run as $finish does, instead of dropping into vvp's prompt: the
            // course's testbenches end on $stop, and a prompt would wait for input that never comes.
            ProcessResult simulate = ProcessRunner.Run(Vvp, new[] { "-n", "design.vvp" }, folder, limit);
            return new IcarusRun(folder, compile, simulate);
        }
    }
}
