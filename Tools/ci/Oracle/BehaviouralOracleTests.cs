using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BitSorter.Sim;
using BitSorter.Sim.Tests;
using BitSorter.SystemVerilog.Syntax;
using NUnit.Framework;

namespace BitSorter.Ci.Oracle
{
    /// <summary>
    /// Behavioural designs with their own testbenches -- <c>initial</c> blocks, clocks, <c>always_ff</c>,
    /// <c>always_comb</c>, memories and printing -- run in both simulators from the same file, and every
    /// signal compared, and every line the testbench prints.
    /// </summary>
    /// <remarks>
    /// The testbench is the top in both. Icarus needs to be told to dump, so a second top module that
    /// only calls <c>$dumpvars</c> is compiled beside it, from a temporary folder deleted after; the
    /// corpus file itself is untouched. BitSorter records everything anyway. A process's own working
    /// variables (a loop's <c>int i</c>) are BitSorter's alone, and a memory's words are left out of a
    /// dump by Icarus, so neither is compared as a signal -- the words are, through what the testbench
    /// prints of them. Icarus's own remarks on its standard output (where its dump went, its warnings,
    /// "$finish called") are not the design's, and are left out of the transcript compared.
    /// </remarks>
    public class BehaviouralOracleTests
    {
        [TestCase("behavioural/scheduling.sv", "scheduling", null, null)]
        [TestCase("behavioural/traffic_light.sv", "traffic_light_tb", null, null)]
        [TestCase("delays/probes.sv", "probes", "probes.vcd", null)]
        [TestCase("behavioural/multiplier.sv", "multiplier_tb", null, null)]
        [TestCase("behavioural/two_state.sv", "two_state_tb", null, null)]
        [TestCase("behavioural/subroutines.sv", "subroutines_tb", null, null)]
        [TestCase("behavioural/function_sensitivity.sv", "function_sensitivity", null, null)]
        [TestCase("behavioural/expressions.sv", "expressions_tb", null, null)]
        [TestCase("behavioural/data_types.sv", "data_types_tb", null, null)]
        [TestCase("behavioural/testbench_control.sv", "testbench_control_tb", null, null)]
        [TestCase("behavioural/library.sv", "library_tb", null, null)]
        [TestCase("behavioural/reals.sv", "reals_tb", null, null)]
        [TestCase("behavioural/interfaces.sv", "interfaces_tb", null, null)]
        [TestCase("system/printing.sv", "printing", null, null)]
        [TestCase("system/memories.sv", "memories_tb", null, "system/memories.hex,system/memories.bin")]
        [TestCase("processor/mcpu.sv", "mcpu_tb", null, "processor/mcpu_program.hex,processor/mcpu_data.hex,processor/mcpu_expected.hex")]
        public void TheTestbench_AgreesWithIcarus_OnEverySignal(string file, string top, string ownDump, string data)
        {
            Icarus icarus = Icarus.Require();
            string[] dataFiles = data == null ? new string[0] : data.Split(',').Select(Corpus.File).ToArray();

            var files = new MemorySimFiles();
            foreach (string path in dataFiles)
                files.Add(Path.GetFileName(path), File.ReadAllText(path));

            Design design = Elaborate(file, top);
            var sim = new Simulator(design, null, new SimOptions { Files = files });
            RunResult result = sim.RunTo(1_000_000);
            Assert.That(result.Status, Is.EqualTo(RunStatus.Finished).Or.EqualTo(RunStatus.Suspended), string.Join("\n", sim.History.Messages));

            (Dictionary<string, VcdTrace> theirs, string printed, Dictionary<string, string> theirFiles) = RunIcarus(icarus, file, top, ownDump, dataFiles);

            // A $stop suspends the run here, and ends it under vvp -n, so the two agree up to it. Icarus's
            // dump loses the time step a $stop ends the run in (Docs/oracle-notes.md); with $finish it
            // keeps it. So after a $stop, that last step is BitSorter's alone.
            bool stopped = result.Status == RunStatus.Suspended;
            long until = stopped ? result.Time - 1 : result.Time;

            var wrong = new List<string>();
            int compared = 0, changes = 0;

            for (int s = 0; s < design.Signals.Count; s++)
            {
                SignalInfo info = design.Signals[s];
                if (info.Local || info.Memory != null || info.IsString || info.IsReal)
                    continue;   // a string Icarus leaves out of its dump, and a real it dumps as a real: what the testbench prints of each is compared

                if (!theirs.TryGetValue(info.Name, out VcdTrace trace))
                {
                    wrong.Add($"{info.Name}: not in Icarus's dump");
                    continue;
                }

                string[] icarusChanges = trace.Changes.Select(c => $"{c.Time} {c.Bits}").ToArray();
                string[] bitSorterChanges = sim.History.SettledChanges(design.SignalAt(s))
                    .Where(c => c.Time <= until)
                    .Select(c => $"{c.Time} {c.Value.Format(SystemVerilog.Radix.Binary)}")
                    .ToArray();
                compared++;
                changes += bitSorterChanges.Length;

                if (!icarusChanges.SequenceEqual(bitSorterChanges))
                    wrong.Add($"{info.Name}\n      BitSorter {string.Join(", ", bitSorterChanges)}\n      Icarus    {string.Join(", ", icarusChanges)}");
            }

            // The transcript: what the testbench printed, line by line.
            string[] icarusLines = printed.Replace("\r", "").Split('\n')
                .Where(line => !line.StartsWith("VCD info:", StringComparison.Ordinal)
                    && !line.StartsWith("WARNING:", StringComparison.Ordinal)
                    && !line.Contains(": $finish called at ")
                    && !line.Contains(": $stop called at "))
                .ToArray();
            if (icarusLines.Length > 0 && icarusLines[icarusLines.Length - 1].Length == 0)
                icarusLines = icarusLines.Take(icarusLines.Length - 1).ToArray();

            string[] bitSorterLines = sim.History.Messages
                .Where(m => m.Severity == Severity.Info && !m.Text.StartsWith("$finish", StringComparison.Ordinal) && !m.Text.StartsWith("$stop", StringComparison.Ordinal))
                .Select(m => m.Text)
                .ToArray();

            var misprinted = new List<string>();
            for (int i = 0; i < Math.Max(icarusLines.Length, bitSorterLines.Length); i++)
            {
                string ours = i < bitSorterLines.Length ? bitSorterLines[i] : "(nothing)";
                string theirsLine = i < icarusLines.Length ? icarusLines[i] : "(nothing)";
                if (ours != theirsLine)
                    misprinted.Add($"line {i + 1}\n      BitSorter [{ours}]\n      Icarus    [{theirsLine}]");
            }

            // The files the testbench wrote (Part L6), word for word.
            var given = new HashSet<string>(dataFiles.Select(Path.GetFileName), StringComparer.Ordinal);
            var miswritten = new List<string>();
            foreach (string path in files.Paths.Where(p => !given.Contains(p)))
            {
                files.TryRead(path, out string ours);
                if (!theirFiles.TryGetValue(path, out string icarusText))
                    miswritten.Add($"{path}: BitSorter wrote it, Icarus did not");
                else if (ours.Replace("\r", "") != icarusText.Replace("\r", ""))
                    miswritten.Add($"{path}\n      BitSorter [{ours.Replace("\n", "\\n")}]\n      Icarus    [{icarusText.Replace("\r", "").Replace("\n", "\\n")}]");
            }

            TestContext.WriteLine($"{top}: finished at {result.Time}; {compared} signals, {changes} settled changes, {bitSorterLines.Length} lines printed compared");
            Assert.IsEmpty(miswritten, $"{miswritten.Count} files differ from Icarus's:\n  " + string.Join("\n  ", miswritten));
            Assert.IsEmpty(wrong, $"{wrong.Count} signals disagree with Icarus:\n  " + string.Join("\n  ", wrong.Take(16)));
            Assert.IsEmpty(misprinted, $"{misprinted.Count} printed lines differ from Icarus's:\n  " + string.Join("\n  ", misprinted.Take(16)));
            Assert.Greater(compared, 0);
        }

        private static Design Elaborate(string file, string top)
        {
            string root = RepoRoot.Combine(Corpus.Folder);
            var files = new MemorySourceFiles();
            foreach (string path in Directory.GetFiles(root, "*.sv*", SearchOption.AllDirectories))
                files.Add(Path.GetRelativePath(root, path).Replace('\\', '/'), File.ReadAllText(path));

            Elaboration elaboration = Elaborator.Elaborate(SyntaxReader.Read(file, files), top);
            Assert.IsFalse(elaboration.HasErrors, string.Join("\n", elaboration.Diagnostics));
            return elaboration.Design;
        }

        /// <summary>
        /// Icarus's dump of the design -- the one it writes itself, named <paramref name="ownDump"/>, or one
        /// asked for beside it -- and what it printed.
        /// </summary>
        private static (Dictionary<string, VcdTrace>, string, Dictionary<string, string>) RunIcarus(Icarus icarus, string file, string top, string ownDump, string[] dataFiles)
        {
            string folder = Path.Combine(Path.GetTempPath(), "bitsorter-oracle-generated", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            string dump = Path.Combine(folder, "oracle_dump.sv");
            File.WriteAllText(dump,
                "// Generated by BehaviouralOracleTests; deleted after the run.\n" +
                "module oracle_dump;\n" +
                $"    initial begin $dumpfile(\"oracle.vcd\"); $dumpvars(0, {top}); end\n" +
                "endmodule\n");

            try
            {
                string design = Corpus.File(file);
                string[] sources = ownDump != null ? new[] { design } : new[] { design, dump };
                using IcarusRun run = icarus.Simulate(sources, null, new[] { Path.GetDirectoryName(design) }, TimeSpan.FromMinutes(2), dataFiles);
                Assert.IsTrue(run.Succeeded, run.Explain());

                var written = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (string path in Directory.GetFiles(run.Folder))
                    written[Path.GetFileName(path)] = File.ReadAllText(path);

                using var vcd = new StreamReader(Path.Combine(run.Folder, ownDump ?? "oracle.vcd"));
                return (VcdReader.Read(vcd), run.Simulate.StandardOutput, written);
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }
}
