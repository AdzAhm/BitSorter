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
        /// <summary>Every testbench of the corpus with its own top, the file it dumps itself to if it does, and its data.</summary>
        internal static IEnumerable<TestCaseData> Testbenches() => new[]
        {
            new TestCaseData("behavioural/scheduling.sv", "scheduling", null, null),
            new TestCaseData("behavioural/traffic_light.sv", "traffic_light_tb", null, null),
            new TestCaseData("delays/probes.sv", "probes", "probes.vcd", null),
            new TestCaseData("behavioural/multiplier.sv", "multiplier_tb", null, null),
            new TestCaseData("behavioural/two_state.sv", "two_state_tb", null, null),
            new TestCaseData("behavioural/subroutines.sv", "subroutines_tb", null, null),
            new TestCaseData("behavioural/automatic_tasks.sv", "automatic_tasks_tb", null, null),
            new TestCaseData("behavioural/function_sensitivity.sv", "function_sensitivity", null, null),
            new TestCaseData("behavioural/expressions.sv", "expressions_tb", null, null),
            new TestCaseData("behavioural/data_types.sv", "data_types_tb", null, null),
            new TestCaseData("behavioural/testbench_control.sv", "testbench_control_tb", null, null),
            new TestCaseData("behavioural/library.sv", "library_tb", null, null),
            new TestCaseData("behavioural/reals.sv", "reals_tb", null, null),
            new TestCaseData("behavioural/interfaces.sv", "interfaces_tb", null, null),
            new TestCaseData("behavioural/shared_bus.sv", "shared_bus_tb", null, null),
            new TestCaseData("behavioural/lexical.sv", "lexical_tb", null, null),
            new TestCaseData("system/printing.sv", "printing", null, null),
            new TestCaseData("system/memories.sv", "memories_tb", null, "system/memories.hex,system/memories.bin"),
            new TestCaseData("processor/mcpu.sv", "mcpu_tb", null, "processor/mcpu_program.hex,processor/mcpu_data.hex,processor/mcpu_expected.hex"),
        };

        [TestCaseSource(nameof(Testbenches))]
        public void TheTestbench_AgreesWithIcarus_OnEverySignal(string file, string top, string ownDump, string data) =>
            Agrees("Icarus", file, top, ownDump, data, null);

        /// <summary>
        /// The same testbenches under ModelSim, the course's own simulator (Part 15i-4): a second oracle, which a race --
        /// what two simulators may settle differently -- would show where Icarus alone could not. Skipped where ModelSim is
        /// not installed, as on CI.
        /// </summary>
        [TestCaseSource(nameof(Testbenches))]
        public void TheTestbench_AgreesWithModelSim_OnEverySignal(string file, string top, string ownDump, string data) =>
            Agrees("ModelSim", file, top, ownDump, data, null);

        /// <summary>
        /// M5's review: the corpus under ModelSim's start, held where CI runs, which has no ModelSim. Every testbench races in
        /// neither start and prints the same in both, but the one line that reads through the hierarchy at time 0 -- before the
        /// logic under it has settled, as ModelSim begins it.
        /// </summary>
        [TestCaseSource(nameof(Testbenches))]
        public void TheTestbench_RacesInNeitherStart_AndPrintsAlikeInBoth(string file, string top, string ownDump, string data)
        {
            string[] Lines(StartOrder start)
            {
                var files = new MemorySimFiles();
                foreach (string path in data == null ? new string[0] : data.Split(',').Select(Corpus.File))
                    files.Add(Path.GetFileName(path), File.ReadAllText(path));
                var sim = new Simulator(Elaborate(file, top), null, new SimOptions { Files = files, StartOrder = start });
                sim.RunTo(1_000_000);
                CollectionAssert.IsEmpty(sim.History.Messages.Where(m => m.Severity == Severity.Warning && m.Text.Contains("blocking assignment at the edge"))
                    .Select(m => m.Text), $"a race in {file} as {start} begins time 0");
                return sim.History.Messages.Where(m => m.Severity == Severity.Info).Select(m => m.Text).ToArray();
            }

            string[] icarus = Lines(StartOrder.Icarus), modelSim = Lines(StartOrder.ModelSim);
            Assert.AreEqual(icarus.Length, modelSim.Length, string.Join("\n", modelSim));
            int[] differs = top == "printing" ? new[] { 10 } : new int[0];
            for (int i = 0; i < icarus.Length; i++)
                if (Array.IndexOf(differs, i + 1) < 0)
                    Assert.AreEqual(icarus[i], modelSim[i], $"{file}, line {i + 1}");
                else
                    Assert.AreNotEqual(icarus[i], modelSim[i], $"{file}, line {i + 1}: read before the logic under it settles, as ModelSim begins");
        }

        /// <summary>
        /// The procedural continuous assignment (M4), held to ModelSim on every signal and every printed line: Icarus 12 reads
        /// an assign's right side once, as it runs, where the standard has it followed (Docs/oracle-notes.md, "Procedural
        /// assign"), so this design is not in the list both simulators run.
        /// </summary>
        [Test]
        public void TheProceduralAssign_AgreesWithModelSim_OnEverySignal() =>
            Agrees("ModelSim", "behavioural/procedural_assign.sv", "procedural_assign_tb", null, null, null);

        /// <summary>
        /// Icarus 12's difference, held so that a later Icarus that follows the standard is noticed -- and the design can join
        /// the list both run: it reads <c>assign y = a + b;</c> once, so y stays 3 when a changes, where the standard has 7.
        /// </summary>
        [Test]
        public void Icarus12_ReadsAProceduralAssignsRightSideOnce()
        {
            (_, string[] lines, _) = RunIcarus(Icarus.Require(), "behavioural/procedural_assign.sv", "procedural_assign_tb", null, new string[0]);
            CollectionAssert.Contains(lines, "1 assigned: y=3", string.Join("\n", lines));
            CollectionAssert.Contains(lines, "2 follows a: y=3", "Icarus 12 does not follow a; BitSorter and ModelSim give y=7");
        }

        /// <summary>
        /// The NBA region's updates landing in turn (Ahmad's decision of 2026-11-03), held to ModelSim on every signal and every
        /// printed line: which waits a value between wakes the standard leaves open, and Icarus 12 settles seven of the
        /// design's otherwise (Docs/oracle-notes.md, "Two nonblocking writes to one variable in one moment").
        /// </summary>
        [Test]
        public void TheNonblockingLanding_AgreesWithModelSim_OnEverySignal() =>
            Agrees("ModelSim", "behavioural/nba_landing.sv", "nba_landing_tb", null, null, null);

        /// <summary>
        /// Icarus 12's way with the same design, held so that a later Icarus that settles it as ModelSim does is noticed: no
        /// select is woken by a value between, and four expressions are -- a comparison, a memory's word by a constant and by
        /// a variable, and a cast. Where both agree, BitSorter's own run is held to both.
        /// </summary>
        [Test]
        public void Icarus12_WakesNoSelectOnANonblockingValueBetween_AndFourExpressions()
        {
            (_, string[] lines, _) = RunIcarus(Icarus.Require(), "behavioural/nba_landing.sv", "nba_landing_tb", null, new string[0], null);
            CollectionAssert.Contains(lines, "between: sel=0 selpos=0 part=0 whole=1 lsb=1 cat=1 qany=1 qneg=1", string.Join("\n", lines));
            CollectionAssert.Contains(lines, "left: vx=0 ab=0 eq=1 word=1 wordi=1 mixed=0 cast=1 inv=0 waited=0 pr=1", string.Join("\n", lines));
        }

        /// <summary>The images every processor's program needs: all three programs', each with what it should leave, and the data.</summary>
        private const string Programs =
            "processor/rv_sum.hex,processor/rv_sum_expected.hex,processor/rv_fib.hex,processor/rv_fib_expected.hex," +
            "processor/rv_isa.hex,processor/rv_isa_expected.hex,processor/rv_data.hex";

        /// <summary>
        /// Each processor running each of its three programs (Part 15h-6c), its testbench's PROGRAM given as a simulator's
        /// command line gives it -- here by <see cref="Elaborator.Elaborate(SyntaxTree, string, IReadOnlyDictionary{string, Expression}, GateLevelRules)"/>,
        /// Icarus's by -P with the member's value -- and held to Icarus on every signal and every printed line, its own PASS
        /// among them.
        /// </summary>
        [TestCase("processor/scpu.sv", "scpu_tb", "SUM", 0)]
        [TestCase("processor/scpu.sv", "scpu_tb", "FIBONACCI", 1)]
        [TestCase("processor/scpu.sv", "scpu_tb", "ISA", 2)]
        [TestCase("processor/mcpu.sv", "mcpu_demo_tb", "SUM", 0)]
        [TestCase("processor/mcpu.sv", "mcpu_demo_tb", "FIBONACCI", 1)]
        [TestCase("processor/mcpu.sv", "mcpu_demo_tb", "ISA", 2)]
        [TestCase("processor/pcpu.sv", "pcpu_tb", "SUM", 0)]
        [TestCase("processor/pcpu.sv", "pcpu_tb", "FIBONACCI", 1)]
        [TestCase("processor/pcpu.sv", "pcpu_tb", "ISA", 2)]
        public void EachProcessor_RunsEachProgram_AsIcarusDoes(string file, string top, string program, int value)
        {
            var name = new NameExpression();
            name.Path.Add(program);
            Agrees("Icarus", file, top, null, Programs, (new Dictionary<string, Expression> { ["PROGRAM"] = name }, $"{top}.PROGRAM={value}"));
        }

        /// <summary>Each processor and program under ModelSim, its PROGRAM given by -G as an exported run.do gives it.</summary>
        [TestCase("processor/scpu.sv", "scpu_tb", "SUM", 0)]
        [TestCase("processor/scpu.sv", "scpu_tb", "FIBONACCI", 1)]
        [TestCase("processor/scpu.sv", "scpu_tb", "ISA", 2)]
        [TestCase("processor/mcpu.sv", "mcpu_demo_tb", "SUM", 0)]
        [TestCase("processor/mcpu.sv", "mcpu_demo_tb", "FIBONACCI", 1)]
        [TestCase("processor/mcpu.sv", "mcpu_demo_tb", "ISA", 2)]
        [TestCase("processor/pcpu.sv", "pcpu_tb", "SUM", 0)]
        [TestCase("processor/pcpu.sv", "pcpu_tb", "FIBONACCI", 1)]
        [TestCase("processor/pcpu.sv", "pcpu_tb", "ISA", 2)]
        public void EachProcessor_RunsEachProgram_AsModelSimDoes(string file, string top, string program, int value)
        {
            var name = new NameExpression();
            name.Path.Add(program);
            Agrees("ModelSim", file, top, null, Programs, (new Dictionary<string, Expression> { ["PROGRAM"] = name }, $"/{top}/PROGRAM={value}"));
        }

        /// <summary>
        /// Where ModelSim settles what the standard leaves open another way than BitSorter, or prints a value in its own style
        /// (Part 15i-4): by testbench, the signals and the printed lines (counted from 1) not held to it, each written in
        /// Docs/oracle-notes.md, "ModelSim". Nothing else is let through. Since M5 BitSorter begins time 0 as ModelSim does when
        /// held to it, so the start order is in this list no more.
        /// </summary>
        private static readonly Dictionary<string, (string[] Signals, int[] Lines)> ModelSimDiffers =
            new Dictionary<string, (string[], int[])>(StringComparer.Ordinal)
            {
                // $random with no seed: each simulator's own first seed.
                ["expressions_tb"] = (new string[0], new[] { 13, 14, 15 }),
                // A real printed with no format, and %d's width beside one: each simulator's own style.
                ["reals_tb"] = (new string[0], new[] { 1, 2 }),
                // %05d, %3h and %8b's padding.
                ["printing"] = (new string[0], new[] { 7 }),
            };

        /// <summary>The testbench run here and under <paramref name="with"/> -- Icarus or ModelSim -- and every signal, every printed line and every file it writes compared.</summary>
        private static void Agrees(string with, string file, string top, string ownDump, string data,
            (Dictionary<string, Expression> Ours, string Theirs)? program)
        {
            Icarus icarus = with == "Icarus" ? Icarus.Require() : null;
            ModelSim modelSim = with == "ModelSim" ? ModelSim.Require() : null;
            string[] dataFiles = data == null ? new string[0] : data.Split(',').Select(Corpus.File).ToArray();

            var files = new MemorySimFiles();
            foreach (string path in dataFiles)
                files.Add(Path.GetFileName(path), File.ReadAllText(path));

            Design design = Elaborate(file, top, program?.Ours);
            var sim = new Simulator(design, null, new SimOptions { Files = files, StartOrder = icarus != null ? StartOrder.Icarus : StartOrder.ModelSim });
            RunResult result = sim.RunTo(1_000_000);
            Assert.That(result.Status, Is.EqualTo(RunStatus.Finished).Or.EqualTo(RunStatus.Suspended), string.Join("\n", sim.History.Messages));

            // None of the corpus's testbenches races (Part 15i-3): what the two simulators are held to agree on is never a
            // value either could settle another way.
            CollectionAssert.IsEmpty(sim.History.Messages.Where(m => m.Severity == Severity.Warning && m.Text.Contains("blocking assignment at the edge"))
                .Select(m => m.Text), "a race in the corpus");

            (Dictionary<string, VcdTrace> theirs, string[] icarusLines, Dictionary<string, string> theirFiles) = icarus != null
                ? RunIcarus(icarus, file, top, ownDump, dataFiles, program?.Theirs)
                : RunModelSim(modelSim, file, top, ownDump, dataFiles, program?.Theirs);

            // A $stop suspends the run here, and ends it under vvp -n, so the two agree up to it. Icarus's
            // dump loses the time step a $stop ends the run in (Docs/oracle-notes.md); with $finish it
            // keeps it. So after a $stop, that last step is BitSorter's alone. ModelSim's keeps what ran before the $stop in it --
            // which, before its NBA region, is the order the step ran in (Part 15i-4) -- so it is left out of both.
            bool stopped = result.Status == RunStatus.Suspended;
            long until = stopped ? result.Time - 1 : result.Time;
            (string[] Signals, int[] Lines) differs = modelSim != null && ModelSimDiffers.TryGetValue(top, out var known)
                ? known : (new string[0], new int[0]);

            var wrong = new List<string>();
            int compared = 0, changes = 0;

            for (int s = 0; s < design.Signals.Count; s++)
            {
                SignalInfo info = design.Signals[s];
                if (info.Local || info.Memory != null || info.IsString || info.IsReal)
                    continue;   // a string Icarus leaves out of its dump, and a real it dumps as a real: what the testbench prints of each is compared
                if (Array.IndexOf(differs.Signals, info.Name) >= 0)
                    continue;

                if (!theirs.TryGetValue(info.Name, out VcdTrace trace))
                {
                    wrong.Add($"{info.Name}: not in {with}'s dump");
                    continue;
                }

                string[] icarusChanges = trace.Changes.Where(c => c.Time <= until).Select(c => $"{c.Time} {c.Bits}").ToArray();
                string[] bitSorterChanges = sim.History.SettledChanges(design.SignalAt(s))
                    .Where(c => c.Time <= until)
                    .Select(c => $"{c.Time} {c.Value.Format(SystemVerilog.Radix.Binary)}")
                    .ToArray();
                compared++;
                changes += bitSorterChanges.Length;

                if (!icarusChanges.SequenceEqual(bitSorterChanges))
                    wrong.Add($"{info.Name}\n      BitSorter {string.Join(", ", bitSorterChanges)}\n      {with,-9} {string.Join(", ", icarusChanges)}");
            }

            // The transcript: what the testbench printed, line by line.
            string[] bitSorterLines = sim.History.Messages
                .Where(m => m.Severity == Severity.Info && !m.Text.StartsWith("$finish", StringComparison.Ordinal) && !m.Text.StartsWith("$stop", StringComparison.Ordinal))
                .Select(m => m.Text)
                .ToArray();

            // A processor's testbench checks its own results: agreeing on a FAIL or a TIMEOUT is no pass (the review of #121).
            if (file.StartsWith("processor/", StringComparison.Ordinal))
                CollectionAssert.Contains(bitSorterLines, "PASS: every result as expected", string.Join("\n", bitSorterLines));

            var misprinted = new List<string>();
            for (int i = 0; i < Math.Max(icarusLines.Length, bitSorterLines.Length); i++)
            {
                string ours = i < bitSorterLines.Length ? bitSorterLines[i] : "(nothing)";
                string theirsLine = i < icarusLines.Length ? icarusLines[i] : "(nothing)";
                if (ours != theirsLine && Array.IndexOf(differs.Lines, i + 1) < 0)
                    misprinted.Add($"line {i + 1}\n      BitSorter [{ours}]\n      {with,-9} [{theirsLine}]");
            }

            // The files the testbench wrote (Part L6), word for word.
            var given = new HashSet<string>(dataFiles.Select(Path.GetFileName), StringComparer.Ordinal);
            var miswritten = new List<string>();
            foreach (string path in files.Paths.Where(p => !given.Contains(p)))
            {
                files.TryRead(path, out string ours);
                if (!theirFiles.TryGetValue(path, out string icarusText))
                    miswritten.Add($"{path}: BitSorter wrote it, {with} did not");
                else if (ours.Replace("\r", "") != icarusText.Replace("\r", ""))
                    miswritten.Add($"{path}\n      BitSorter [{ours.Replace("\n", "\\n")}]\n      {with,-9} [{icarusText.Replace("\r", "").Replace("\n", "\\n")}]");
            }

            TestContext.WriteLine($"{top}: finished at {result.Time}; {compared} signals, {changes} settled changes, {bitSorterLines.Length} lines printed compared");
            Assert.IsEmpty(miswritten, $"{miswritten.Count} files differ from {with}'s:\n  " + string.Join("\n  ", miswritten));
            Assert.IsEmpty(wrong, $"{wrong.Count} signals disagree with {with}:\n  " + string.Join("\n  ", wrong.Take(16)));
            Assert.IsEmpty(misprinted, $"{misprinted.Count} printed lines differ from {with}'s:\n  " + string.Join("\n  ", misprinted.Take(16)));
            Assert.Greater(compared, 0);
        }

        private static Design Elaborate(string file, string top, Dictionary<string, Expression> parameters = null)
        {
            string root = RepoRoot.Combine(Corpus.Folder);
            var files = new MemorySourceFiles();
            foreach (string path in Directory.GetFiles(root, "*.sv*", SearchOption.AllDirectories))
                files.Add(Path.GetRelativePath(root, path).Replace('\\', '/'), File.ReadAllText(path));

            Elaboration elaboration = parameters != null
                ? Elaborator.Elaborate(SyntaxReader.Read(file, files), top, parameters)
                : Elaborator.Elaborate(SyntaxReader.Read(file, files), top);
            Assert.IsFalse(elaboration.HasErrors, string.Join("\n", elaboration.Diagnostics));
            return elaboration.Design;
        }

        /// <summary>
        /// Icarus's dump of the design -- the one it writes itself, named <paramref name="ownDump"/>, or one
        /// asked for beside it -- and what it printed.
        /// </summary>
        private static (Dictionary<string, VcdTrace>, string[], Dictionary<string, string>) RunIcarus(Icarus icarus, string file, string top, string ownDump, string[] dataFiles,
            string parameter = null)
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
                // Only the testbench asked for, and the dump: a file may hold another, as mcpu.sv does (Part 15h-6).
                string tops = ownDump != null ? top : top + ",oracle_dump";
                using IcarusRun run = icarus.Simulate(sources, tops, new[] { Path.GetDirectoryName(design) }, TimeSpan.FromMinutes(2), dataFiles,
                    parameter != null ? new[] { parameter } : null);
                Assert.IsTrue(run.Succeeded, run.Explain());

                var written = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (string path in Directory.GetFiles(run.Folder))
                    written[Path.GetFileName(path)] = File.ReadAllText(path);

                // Icarus's own remarks on its standard output (where its dump went, its warnings, "$finish called") are not the design's.
                string[] lines = run.Simulate.StandardOutput.Replace("\r", "").Split('\n')
                    .Where(line => !line.StartsWith("VCD info:", StringComparison.Ordinal)
                        && !line.StartsWith("WARNING:", StringComparison.Ordinal)
                        && !line.Contains(": $finish called at ")
                        && !line.Contains(": $stop called at "))
                    .ToArray();
                if (lines.Length > 0 && lines[lines.Length - 1].Length == 0)
                    lines = lines.Take(lines.Length - 1).ToArray();

                using var vcd = new StreamReader(Path.Combine(run.Folder, ownDump ?? "oracle.vcd"));
                return (VcdReader.Read(vcd), lines, written);
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
            }
        }

        /// <summary>ModelSim's dump of the design -- every signal, or the one the design writes itself -- and what it printed.</summary>
        private static (Dictionary<string, VcdTrace>, string[], Dictionary<string, string>) RunModelSim(ModelSim modelSim, string file, string top, string ownDump,
            string[] dataFiles, string parameter = null)
        {
            string design = Corpus.File(file);
            using ModelSimRun run = modelSim.Simulate(new[] { design }, top, new[] { Path.GetDirectoryName(design) }, TimeSpan.FromMinutes(3), dataFiles,
                parameter != null ? new[] { parameter } : null, ownDump);
            Assert.IsTrue(run.Succeeded, run.Explain());

            var written = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string path in Directory.GetFiles(run.Folder))
                written[Path.GetFileName(path)] = File.ReadAllText(path);

            using var vcd = new StreamReader(Path.Combine(run.Folder, ownDump ?? "oracle.vcd"));
            return (VcdReader.Read(vcd), run.Printed(), written);
        }
    }
}
