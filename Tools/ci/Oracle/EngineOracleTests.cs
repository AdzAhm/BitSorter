using System.Collections.Generic;
using System.IO;
using System.Linq;
using BitSorter.Sim;
using BitSorter.Sim.Tests;
using NUnit.Framework;

namespace BitSorter.Ci.Oracle
{
    /// <summary>
    /// BitSorter's engine and Icarus Verilog run the same circuits and their waveforms agree, signal
    /// by signal, change by change.
    /// </summary>
    /// <remarks>
    /// The engine checked without the front end: the circuits written in C# with
    /// <see cref="NetlistBuilder"/> (Probes.cs), against Icarus running their SystemVerilog
    /// (corpus/delays/probes.sv). Since Part 5 the front end reads that file itself too
    /// (<see cref="BehaviouralOracleTests"/>); the C# twin stays so a fault can be placed on one side of
    /// the front end or the other. Icarus dumps a VCD; <see cref="VcdReader"/> reads it into the value
    /// each time step settled on, which is exactly what the engine's history keeps.
    /// </remarks>
    public class EngineOracleTests
    {
        [Test]
        public void TheDelayProbes_AgreeWithIcarus_SignalBySignal()
        {
            Icarus icarus = Icarus.Require();

            using IcarusRun run = icarus.Simulate(new[] { Corpus.File("delays/probes.sv") });
            Assert.IsTrue(run.Succeeded, run.Explain());

            Dictionary<string, VcdTrace> icarusTraces;
            using (var vcd = new StreamReader(Path.Combine(run.Folder, "probes.vcd")))
                icarusTraces = VcdReader.Read(vcd);

            (Design design, Stimulus stimulus) = Probes.Build();
            var sim = new Simulator(design, stimulus);
            sim.RunTo(40);

            var wrong = new List<string>();

            for (int s = 0; s < design.Signals.Count; s++)
            {
                string name = $"{Probes.Module}.{design.Signals[s].Name}";
                if (!icarusTraces.TryGetValue(name, out VcdTrace trace))
                {
                    wrong.Add($"{name}: not in Icarus's dump");
                    continue;
                }

                string[] theirs = trace.Changes.Select(c => $"{c.Time} {c.Bits}").ToArray();
                string[] ours = Probes.Settled(sim.History, design.SignalAt(s));

                if (!theirs.SequenceEqual(ours))
                    wrong.Add($"{name}\n      BitSorter {string.Join(", ", ours)}\n      Icarus    {string.Join(", ", theirs)}");
            }

            Assert.IsEmpty(wrong, "the engine and Icarus disagree:\n  " + string.Join("\n  ", wrong));
        }
    }
}
