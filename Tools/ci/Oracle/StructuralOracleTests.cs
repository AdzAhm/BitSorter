using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BitSorter.Sim;
using BitSorter.Sim.Tests;
using BitSorter.SystemVerilog;
using BitSorter.SystemVerilog.Syntax;
using BitSorter.SystemVerilog.Tests;
using NUnit.Framework;

namespace BitSorter.Ci.Oracle
{
    /// <summary>
    /// The structural corpus, read and elaborated by BitSorter and compiled by Icarus, driven by the
    /// same random stimulus in both, agrees on every signal of the design -- every port of every
    /// instance down to the cells' gates -- change by change.
    /// </summary>
    /// <remarks>
    /// Icarus needs a testbench and BitSorter (until Part 5 runs <c>initial</c> blocks) needs a
    /// <see cref="Stimulus"/>, so both are generated here from one list of writes: the testbench goes
    /// to a temporary folder and is deleted after, so nothing generated lands in the repository.
    ///
    /// The writes are a mixture of long gaps, which let the circuit settle, and gaps shorter than its
    /// slowest path, so pulses reach gates mid-flight and inertial delay is exercised too. The values
    /// come from <see cref="RandomValues"/>: all x, all z, a stray unknown bit, all ones -- the 4-state
    /// cases where two simulators are most likely to part ways.
    /// </remarks>
    public class StructuralOracleTests
    {
        private const string Bench = "oracle_tb";

        [TestCase("structural/mux2.sv", "mux2", 300, 8, 101)]
        [TestCase("structural/full_adder.sv", "full_adder", 300, 10, 102)]
        [TestCase("structural/alu_slice.sv", "alu_slice", 300, 8, 103)]
        [TestCase("structural/alu64.sv", "alu64", 60, 160, 104)]
        public void TheDesign_AgreesWithIcarus_OnEverySignal(string file, string top, int vectors, int maxGap, int seed)
        {
            Icarus icarus = Icarus.Require();

            Elaboration elaboration = ElaborateCorpus(file, top);
            Design design = elaboration.Design;

            List<(long Time, ElaboratedPort Port, LogicVec Value)> writes = RandomWrites(elaboration, vectors, maxGap, seed, out long end);

            var stimulus = new Stimulus();
            foreach ((long time, ElaboratedPort port, LogicVec value) in writes)
                stimulus.Set(time, port.Signal, value);

            var sim = new Simulator(design, stimulus);
            sim.RunTo(end);

            Dictionary<string, VcdTrace> theirs = RunIcarus(icarus, file, top, elaboration, writes, end);

            var wrong = new List<string>();
            int compared = 0, changes = 0;

            for (int s = 0; s < design.Signals.Count; s++)
            {
                string ours = design.Signals[s].Name;
                string name = Bench + ".dut" + ours.Substring(design.Name.Length);

                if (!theirs.TryGetValue(name, out VcdTrace trace))
                {
                    wrong.Add($"{ours}: not in Icarus's dump as {name}");
                    continue;
                }

                string[] icarusChanges = trace.Changes.Select(c => $"{c.Time} {c.Bits}").ToArray();
                string[] bitSorterChanges = Probes.Settled(sim.History, design.SignalAt(s));
                compared++;
                changes += bitSorterChanges.Length;

                if (!icarusChanges.SequenceEqual(bitSorterChanges))
                    wrong.Add($"{ours}\n      BitSorter {Describe(bitSorterChanges, icarusChanges)}\n      Icarus    {Describe(icarusChanges, bitSorterChanges)}");
            }

            TestContext.WriteLine($"{top}: {writes.Count} writes, {compared} signals, {changes} settled changes compared");
            Assert.IsEmpty(wrong.Take(12), $"{wrong.Count} of {design.Signals.Count} signals disagree with Icarus:\n  " + string.Join("\n  ", wrong.Take(12)));
            Assert.AreEqual(design.Signals.Count, compared);
            Assert.Greater(design.Signals.Count, 0);
        }

        // -----------------------------------------------------------------

        private static Elaboration ElaborateCorpus(string file, string top)
        {
            string root = RepoRoot.Combine(Corpus.Folder);
            var files = new MemorySourceFiles();
            foreach (string path in Directory.GetFiles(root, "*.sv", SearchOption.AllDirectories))
                files.Add(Path.GetRelativePath(root, path).Replace('\\', '/'), File.ReadAllText(path));

            SyntaxTree tree = SyntaxReader.Read(file, files);
            Elaboration elaboration = Elaborator.Elaborate(tree, top);
            Assert.IsEmpty(elaboration.Diagnostics, string.Join("\n", elaboration.Diagnostics));
            Assert.IsNotNull(elaboration.Design);
            return elaboration;
        }

        /// <summary>
        /// Every input written at 0, then a vector at a time, each changing at least one input: mostly
        /// a gap of up to <paramref name="maxGap"/>, sometimes one of a few units only.
        /// </summary>
        private static List<(long, ElaboratedPort, LogicVec)> RandomWrites(Elaboration elaboration, int vectors, int maxGap, int seed, out long end)
        {
            var random = new RandomValues(seed);
            var inputs = elaboration.Ports.Where(p => p.Direction == PortDirection.Input).ToList();
            var writes = new List<(long, ElaboratedPort, LogicVec)>();
            long time = 0;

            for (int v = 0; v < vectors; v++)
            {
                if (v > 0)
                    time += random.Next(4) == 0 ? 1 + random.Next(4) : 1 + random.Next(maxGap);

                int changed = 0;
                foreach (ElaboratedPort port in inputs)
                {
                    if (v > 0 && random.Next(2) == 0)
                        continue;
                    writes.Add((time, port, random.Value(port.Width, signed: false)));
                    changed++;
                }

                if (changed == 0)
                {
                    ElaboratedPort port = inputs[random.Next(inputs.Count)];
                    writes.Add((time, port, random.Value(port.Width, signed: false)));
                }
            }

            end = time + 4 * maxGap;
            return writes;
        }

        private static Dictionary<string, VcdTrace> RunIcarus(Icarus icarus, string file, string top, Elaboration elaboration,
            List<(long Time, ElaboratedPort Port, LogicVec Value)> writes, long end)
        {
            var bench = new StringBuilder();
            bench.AppendLine($"// Generated by StructuralOracleTests for {file}; deleted after the run.");
            bench.AppendLine($"module {Bench};");

            foreach (ElaboratedPort port in elaboration.Ports)
            {
                string kind = port.Direction == PortDirection.Input ? "logic" : "wire";
                string range = port.Width > 1 ? $" [{port.Width - 1}:0]" : "";
                bench.AppendLine($"    {kind}{range} {port.Name};");
            }

            bench.AppendLine($"    {top} dut ({string.Join(", ", elaboration.Ports.Select(p => $".{p.Name}({p.Name})"))});");
            bench.AppendLine("    initial begin");
            bench.AppendLine("        $dumpfile(\"oracle.vcd\");");
            bench.AppendLine($"        $dumpvars(0, {Bench});");

            long now = 0;
            foreach ((long time, ElaboratedPort port, LogicVec value) in writes)
            {
                if (time > now)
                {
                    bench.AppendLine($"        #{time - now};");
                    now = time;
                }

                bench.AppendLine($"        {port.Name} = {port.Width}'b{value.Format(Radix.Binary)};");
            }

            bench.AppendLine($"        #{end - now} $finish;");
            bench.AppendLine("    end");
            bench.AppendLine("endmodule");

            string folder = Path.Combine(Path.GetTempPath(), "bitsorter-oracle-generated", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            string benchFile = Path.Combine(folder, Bench + ".sv");
            File.WriteAllText(benchFile, bench.ToString());

            try
            {
                string design = Corpus.File(file);
                using IcarusRun run = icarus.Simulate(new[] { design, benchFile }, Bench, new[] { Path.GetDirectoryName(design) }, TimeSpan.FromMinutes(2));
                Assert.IsTrue(run.Succeeded, run.Explain());

                using var vcd = new StreamReader(Path.Combine(run.Folder, "oracle.vcd"));
                return VcdReader.Read(vcd);
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
            }
        }

        /// <summary>A change list, shortened to where it first differs from the other, so a long run stays readable.</summary>
        private static string Describe(string[] changes, string[] other)
        {
            int first = 0;
            while (first < changes.Length && first < other.Length && changes[first] == other[first])
                first++;

            int from = Math.Max(0, first - 2);
            string shown = string.Join(", ", changes.Skip(from).Take(6));
            return (from > 0 ? $"... ({from} agree) " : "") + shown + (from + 6 < changes.Length ? $" ... ({changes.Length} in all)" : "");
        }
    }
}
