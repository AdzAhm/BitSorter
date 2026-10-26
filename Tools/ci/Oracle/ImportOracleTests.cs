using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BitSorter.Board;
using BitSorter.Sim;
using BitSorter.Sim.Tests;
using NUnit.Framework;

namespace BitSorter.Ci.Oracle
{
    /// <summary>
    /// A project imported (Part 13b) runs as Icarus runs the files it was imported from: each folder's files brought in
    /// with <see cref="ProjectImporter"/>, compiled and run in BitSorter with the project's own files, and every signal
    /// the two have in common compared, and every line the testbench prints.
    /// </summary>
    /// <remarks>
    /// What is compared is a signal of the same name in both. A drawn module is compiled from its board, so a net the board
    /// does not keep has no counterpart; the testbench's own signals, which are every input and output of the design, are
    /// in both always, and a test fails if one is not compared.
    /// </remarks>
    public class ImportOracleTests
    {
        [TestCase("import/alu", "alu_tb", "alu")]
        [TestCase("import/mux", "mux2_tb", "mux2")]
        [TestCase("import/counter", "counter_tb", "counter")]
        [TestCase("processor", "mcpu_tb", "mcpu")]
        public void AnImportedProject_RunsAsIcarusRunsItsFiles(string folder, string bench, string top)
        {
            Icarus icarus = Icarus.Require();
            string root = RepoRoot.Combine(Corpus.Folder + "/" + folder);
            string[] paths = Directory.GetFiles(root).OrderBy(p => p, StringComparer.Ordinal).ToArray();

            ProjectImport import = ProjectImporter.Import(paths.Select(p => new SourceFile(Path.GetFileName(p), File.ReadAllText(p))).ToList());
            Assert.IsTrue(import.Succeeded, string.Join("\n", import.Notes));
            Assert.IsEmpty(import.Notes.Where(n => n.IsError), string.Join("\n", import.Notes));
            Project project = import.Project;
            Assert.AreEqual(bench, project.Bench);
            Assert.AreEqual(top, project.Top);

            CompiledProject compiled = BoardCompiler.Compile(project);
            Assert.IsTrue(compiled.Succeeded, string.Join("\n", compiled.Diagnostics));
            Elaboration elaboration = Elaborator.Elaborate(compiled.Tree, compiled.Top);
            Assert.IsFalse(elaboration.HasErrors, string.Join("\n", elaboration.Diagnostics));
            Design design = elaboration.Design;
            var sim = new Simulator(design, null, new SimOptions { Files = ProjectFiles.ForRun(project) });
            RunResult result = sim.RunTo(1_000_000);
            Assert.That(result.Status, Is.EqualTo(RunStatus.Finished).Or.EqualTo(RunStatus.Suspended), string.Join("\n", sim.History.Messages));
            Assert.AreEqual(result.Time, project.Stimulus.End, "the import ran the testbench to where it stops");

            (Dictionary<string, VcdTrace> theirs, string printed) = RunIcarus(icarus, paths, bench);

            // A $stop ends vvp -n's run, and Icarus's dump loses that last time step (Docs/oracle-notes.md).
            long until = result.Status == RunStatus.Suspended ? result.Time - 1 : result.Time;
            var wrong = new List<string>();
            var notInIcarus = new List<string>();
            int compared = 0, benchSignals = 0;
            for (int s = 0; s < design.Signals.Count; s++)
            {
                SignalInfo info = design.Signals[s];
                if (info.Local || info.Memory != null || info.IsString || info.IsReal)
                    continue;
                bool ownSignal = info.Name.Count(c => c == '.') == 1 && info.Name.StartsWith(bench + ".", StringComparison.Ordinal);
                if (ownSignal)
                    benchSignals++;
                if (!theirs.TryGetValue(info.Name, out VcdTrace trace))
                {
                    (ownSignal ? wrong : notInIcarus).Add(info.Name + ": not in Icarus's dump");
                    continue;
                }

                string[] icarusChanges = trace.Changes.Select(c => $"{c.Time} {c.Bits}").ToArray();
                string[] bitSorterChanges = sim.History.SettledChanges(design.SignalAt(s))
                    .Where(c => c.Time <= until)
                    .Select(c => $"{c.Time} {c.Value.Format(SystemVerilog.Radix.Binary)}")
                    .ToArray();
                compared++;
                if (!icarusChanges.SequenceEqual(bitSorterChanges))
                    wrong.Add($"{info.Name}\n      BitSorter {string.Join(", ", bitSorterChanges)}\n      Icarus    {string.Join(", ", icarusChanges)}");
            }

            string[] icarusLines = DesignLines(printed);
            string[] bitSorterLines = sim.History.Messages
                .Where(m => m.Severity == Severity.Info && m.Text.Length > 0
                    && !m.Text.StartsWith("$finish", StringComparison.Ordinal) && !m.Text.StartsWith("$stop", StringComparison.Ordinal))
                .Select(m => m.Text)
                .ToArray();

            TestContext.WriteLine($"{folder}: {string.Join(", ", project.Modules.Select(m => m.Name + (m.IsWritten ? " (written)" : " (drawn)")))}; " +
                $"files {string.Join(", ", project.Files.Select(f => f.Name))}; stops at {result.Time}; {compared} signals compared, " +
                $"{notInIcarus.Count} BitSorter's alone, {bitSorterLines.Length} lines printed");
            Assert.IsEmpty(wrong, $"{wrong.Count} signals disagree with Icarus:\n  " + string.Join("\n  ", wrong.Take(16)));
            CollectionAssert.AreEqual(icarusLines, bitSorterLines, "what the testbench printed");
            Assert.Greater(benchSignals, 0);
        }

        [TestCase("import/alu", "alu_tb")]
        [TestCase("import/mux", "mux2_tb")]
        [TestCase("import/counter", "counter_tb")]
        [TestCase("processor", "mcpu_tb")]
        public void AnImportedProject_ExportedAgain_RunsInIcarusAsItsFilesDid(string folder, string bench)
        {
            Icarus icarus = Icarus.Require();
            string root = RepoRoot.Combine(Corpus.Folder + "/" + folder);
            string[] paths = Directory.GetFiles(root).OrderBy(p => p, StringComparer.Ordinal).ToArray();
            Project project = ProjectImporter.Import(paths.Select(p => new SourceFile(Path.GetFileName(p), File.ReadAllText(p))).ToList()).Project;
            Assert.IsNotNull(project);

            // The files an export writes, in a folder of their own: printed in the order its run.do compiles them, and the project's files.
            string exported = Path.Combine(Path.GetTempPath(), "bitsorter-oracle-generated", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(exported);
            try
            {
                var printed = new List<string>();
                foreach (PrintedSource source in BoardPrinter.Print(project))
                {
                    printed.Add(Path.Combine(exported, source.Path));
                    File.WriteAllText(printed[printed.Count - 1], source.Text);
                }

                var others = new List<string>();
                foreach (ProjectFile file in project.Files)
                {
                    others.Add(Path.Combine(exported, file.Name));
                    File.WriteAllText(others[others.Count - 1], file.Text);
                }

                (Dictionary<string, VcdTrace> original, string originalPrinted) = RunIcarus(icarus, paths, bench);
                (Dictionary<string, VcdTrace> again, string againPrinted) = RunIcarus(icarus, printed.Concat(others).ToArray(), bench);

                var wrong = new List<string>();
                int compared = 0;
                foreach (KeyValuePair<string, VcdTrace> signal in original)
                {
                    bool ownSignal = signal.Key.Count(c => c == '.') == 1;
                    if (!again.TryGetValue(signal.Key, out VcdTrace trace))
                    {
                        if (ownSignal)
                            wrong.Add(signal.Key + ": not in the export's dump");
                        continue;
                    }

                    string before = string.Join(", ", signal.Value.Changes.Select(c => $"{c.Time} {c.Bits}"));
                    string after = string.Join(", ", trace.Changes.Select(c => $"{c.Time} {c.Bits}"));
                    compared++;
                    if (before != after)
                        wrong.Add($"{signal.Key}\n      exported {after}\n      original {before}");
                }

                TestContext.WriteLine($"{folder}: {printed.Count} printed files and {others.Count} others; {compared} signals compared");
                Assert.IsEmpty(wrong, $"{wrong.Count} signals differ:\n  " + string.Join("\n  ", wrong.Take(16)));
                CollectionAssert.AreEqual(DesignLines(originalPrinted), DesignLines(againPrinted), "what the testbench printed");
                Assert.Greater(compared, 0);
            }
            finally
            {
                Directory.Delete(exported, recursive: true);
            }
        }

        /// <summary>The timing CONVERT has to keep (Part 13c): a declaration's value, a clock set at time 0, a change after an edge, a pulse no time wide.</summary>
        private static readonly (string Name, string Design, string Bench)[] Timings =
        {
            ("declared",
                "module dff (input logic clk, input logic rst, input logic d, output logic q);\n    always_ff @(posedge clk or posedge rst) if (rst) q <= 0; else q <= d;\nendmodule\n",
                "module dff_tb;\n    logic clk = 0, rst = 1, d = 0, q;\n    dff dut (.clk(clk), .rst(rst), .d(d), .q(q));\n    always #5 clk = ~clk;\n    initial begin #12 rst = 0; #10 d = 1; #30 $stop; end\nendmodule\n"),
            ("set-at-zero",
                "module ff (input logic clk, input logic d, output logic q);\n    always_ff @(posedge clk) q <= d;\nendmodule\n",
                "module ff_tb;\n    logic clk;\n    logic d = 1, q;\n    ff dut (.clk(clk), .d(d), .q(q));\n    initial clk = 1;\n    always #5 clk = ~clk;\n    initial #52 $stop;\nendmodule\n"),
            ("after-the-edge",
                "module ff (input logic clk, input logic d, output logic q);\n    always_ff @(posedge clk) q <= d;\nendmodule\n",
                "module ff_tb;\n    logic clk = 0, d = 0, q;\n    ff dut (.clk(clk), .d(d), .q(q));\n    always #5 clk = ~clk;\n    always @(posedge clk) d <= ~d;\n    initial #52 $stop;\nendmodule\n"),
            ("no-time-wide",
                "module pulses (input logic g, output logic [3:0] n);\n    initial n = 0;\n    always @(posedge g) n <= n + 1;\nendmodule\n",
                "module pulses_tb;\n    logic g = 0;\n    logic [3:0] n;\n    pulses dut (.g(g), .n(n));\n    initial begin #10 g = 1; #0 g = 0; #10 g = 1; #10 $stop; end\nendmodule\n"),
        };

        public static IEnumerable<string> Converted() =>
            new[] { "import/alu", "import/mux", "import/counter", "processor" }.Concat(Timings.Select(t => "timing:" + t.Name));

        [TestCaseSource(nameof(Converted))]
        public void AConvertedTestbench_Exported_RunsInIcarusAsTheOriginalDid(string source)
        {
            Icarus icarus = Icarus.Require();
            string scratch = Path.Combine(Path.GetTempPath(), "bitsorter-oracle-generated", Guid.NewGuid().ToString("N"));
            string originals = Path.Combine(scratch, "original"), exported = Path.Combine(scratch, "exported");
            Directory.CreateDirectory(originals);
            Directory.CreateDirectory(exported);
            try
            {
                string[] paths;
                if (source.StartsWith("timing:", StringComparison.Ordinal))
                {
                    var timing = Timings.Single(t => "timing:" + t.Name == source);
                    File.WriteAllText(Path.Combine(originals, "design.sv"), timing.Design);
                    File.WriteAllText(Path.Combine(originals, "bench.sv"), timing.Bench);
                    paths = new[] { Path.Combine(originals, "bench.sv"), Path.Combine(originals, "design.sv") };
                }
                else
                {
                    paths = Directory.GetFiles(RepoRoot.Combine(Corpus.Folder + "/" + source)).OrderBy(p => p, StringComparer.Ordinal).ToArray();
                }

                Project project = ProjectImporter.Import(paths.Select(p => new SourceFile(Path.GetFileName(p), File.ReadAllText(p))).ToList()).Project;
                Assert.IsNotNull(project);
                string originalPath = BoardCompiler.Compile(project).DesignPath;
                Conversion conversion = BenchConverter.Convert(project);
                Assert.IsTrue(conversion.Succeeded, conversion.Refusal);
                Project converted = conversion.Project;
                CompiledProject compiled = BoardCompiler.Compile(converted);

                var printed = new List<string>();
                foreach (PrintedSource file in BoardPrinter.Print(compiled))
                {
                    printed.Add(Path.Combine(exported, file.Path));
                    File.WriteAllText(printed[printed.Count - 1], file.Text);
                }

                foreach (ProjectFile file in converted.Files)
                {
                    printed.Add(Path.Combine(exported, file.Name));
                    File.WriteAllText(printed[printed.Count - 1], file.Text);
                }

                (Dictionary<string, VcdTrace> before, _) = RunIcarus(icarus, paths, project.Bench);
                (Dictionary<string, VcdTrace> after, _) = RunIcarus(icarus, printed.ToArray(), compiled.Testbench);

                // The design's own signals, by their names inside it: the testbench around it is another one now. Up to the
                // last moment, which Icarus's dump keeps after a $finish and loses after the $stop the stimulus ends on
                // (Docs/oracle-notes.md).
                long end = converted.Stimulus.End;
                Dictionary<string, string> Inside(Dictionary<string, VcdTrace> dump, string path) => dump
                    .Where(d => d.Key.StartsWith(path + ".", StringComparison.Ordinal))
                    .ToDictionary(d => d.Key.Substring(path.Length + 1),
                        d => string.Join(", ", d.Value.Changes.Where(c => c.Time < end).Select(c => $"{c.Time} {c.Bits}")));
                Dictionary<string, string> was = Inside(before, originalPath), now = Inside(after, compiled.DesignPath);

                Assert.Greater(was.Count, 0);
                var wrong = was.Where(w => !now.TryGetValue(w.Key, out string n) || n != w.Value)
                    .Select(w => $"{w.Key}\n      testbench {w.Value}\n      stimulus  {(now.TryGetValue(w.Key, out string n) ? n : "(missing)")}").ToList();
                Assert.IsEmpty(wrong, $"{wrong.Count} of {was.Count} signals differ in Icarus:\n  " + string.Join("\n  ", wrong.Take(8)));
            }
            finally
            {
                Directory.Delete(scratch, recursive: true);
            }
        }

        /// <summary>What a testbench printed, without Icarus's own remarks: where its dump went, its warnings, "$stop called".</summary>
        private static string[] DesignLines(string printed) =>
            printed.Replace("\r", "").Split('\n')
                .Where(line => line.Length > 0
                    && !line.StartsWith("VCD info:", StringComparison.Ordinal)
                    && !line.StartsWith("WARNING:", StringComparison.Ordinal)
                    && !line.Contains(": $finish called at ")
                    && !line.Contains(": $stop called at "))
                .ToArray();

        /// <summary>Icarus's dump of <paramref name="paths"/> -- the .sv files read, the rest there to include and to load -- and what it printed.</summary>
        private static (Dictionary<string, VcdTrace>, string) RunIcarus(Icarus icarus, string[] paths, string bench)
        {
            string folder = Path.Combine(Path.GetTempPath(), "bitsorter-oracle-generated", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            string dump = Path.Combine(folder, "oracle_dump.sv");
            File.WriteAllText(dump,
                "// Generated by ImportOracleTests; deleted after the run.\n" +
                "module oracle_dump;\n" +
                $"    initial begin $dumpfile(\"oracle.vcd\"); $dumpvars(0, {bench}); end\n" +
                "endmodule\n");

            try
            {
                string[] sources = paths.Where(p => p.EndsWith(".sv", StringComparison.Ordinal)).Append(dump).ToArray();
                string[] data = paths.Where(p => !p.EndsWith(".sv", StringComparison.Ordinal)).ToArray();
                using IcarusRun run = icarus.Simulate(sources, null, new[] { Path.GetDirectoryName(paths[0]) }, TimeSpan.FromMinutes(2), data);
                Assert.IsTrue(run.Succeeded, run.Explain());
                using var vcd = new StreamReader(Path.Combine(run.Folder, "oracle.vcd"));
                return (VcdReader.Read(vcd), run.Simulate.StandardOutput);
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }
}
