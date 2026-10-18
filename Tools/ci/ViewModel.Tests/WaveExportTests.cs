using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BitSorter.Sim;
using BitSorter.SystemVerilog;
using NUnit.Framework;

namespace BitSorter.ViewModel.Tests
{
    /// <summary>
    /// The waveform's VCD (Part 9b-4): read back, it is the run the waveform shows, in the run's unit, as
    /// far as the run is made; and an export's file name is safe on every system.
    /// </summary>
    public class WaveExportTests
    {
        private static Func<double> Ticking(double step)
        {
            double now = 0;
            return () => { double read = now; now += step; return read; };
        }

        private static Dictionary<string, VcdTrace> ReadBack(string vcd) => VcdReader.Read(new StringReader(vcd));

        /// <summary>A signal's settled changes as the model's history has them, up to <paramref name="end"/>, as the VCD writes them.</summary>
        private static string[] Expected(WorkbenchModel model, string name, long end)
        {
            Design design = model.Session.Design;
            SignalId id = design.Find(name);
            var changes = new List<string> { "0=" + model.Session.History.ValueAt(id, 0).Format(Radix.Binary) };
            foreach (ValueChange change in model.Session.History.SettledChanges(id))
                if (change.Time > 0 && change.Time <= end)
                    changes.Add(change.Time + "=" + change.Value.Format(Radix.Binary));
            return changes.ToArray();
        }

        [Test]
        public void TheVcd_ReadBack_IsTheRunTheWaveformShows()
        {
            WorkbenchModel model = ReferenceCircuit.Model();
            string vcd = WaveExport.Vcd(model);
            Dictionary<string, VcdTrace> traces = ReadBack(vcd);

            StringAssert.Contains("$timescale 1ns $end", vcd);
            string path = model.Layout.DesignPath;
            foreach (string port in new[] { "a", "b", "s", "y", "out", "q" })
            {
                string name = path + "." + port;
                Assert.IsTrue(traces.ContainsKey(name), name + " is not in the VCD");
                CollectionAssert.AreEqual(Expected(model, name, model.End),
                    traces[name].Changes.Select(c => c.Time + "=" + c.Bits).ToArray(), name);
            }

            StringAssert.EndsWith("#" + model.End + Environment.NewLine, vcd, "and it runs to the end of the timeline");
        }

        [Test]
        public void ARunInAnotherUnit_HasThatTimescale_AndTheSameNumbers()
        {
            var ns = new WorkbenchSession(ReferenceCircuit.Document(), Ticking(1.0));
            ns.Finish();
            string inNs = WaveExport.Vcd(ns.Run.Model);

            var ps = new WorkbenchSession(ReferenceCircuit.Document(), Ticking(1.0));
            Assert.IsTrue(ps.SetUnit("ps").Done);
            ps.Finish();
            string inPs = WaveExport.Vcd(ps.Run.Model);

            StringAssert.Contains("$timescale 1ps $end", inPs);
            Assert.AreEqual(inNs.Replace("$timescale 1ns $end", "$timescale 1ps $end"), inPs, "every number as it was: the unit only labels them");
        }

        [Test]
        public void ARunStillBeingMade_IsExportedAsFarAsItHasGot()
        {
            RunSession run = RunSession.Open(ReferenceCircuit.Document(), Ticking(5.0), chunkEvents: 3, budgetMilliseconds: 8.0);
            Assume.That(run.Done, Is.False, "sanity: a few events a slice leaves the reference unmade after the first");

            long made = WaveExport.MadeTo(run.Model);
            Assert.Less(made, run.Model.End);
            Dictionary<string, VcdTrace> part = ReadBack(WaveExport.Vcd(run.Model));
            Assert.IsTrue(part.Values.All(t => t.Changes.All(c => c.Time <= made)), "nothing past where the run has got");

            run.Finish();
            Assert.AreEqual(run.Model.End, WaveExport.MadeTo(run.Model));
            Assert.IsTrue(ReadBack(WaveExport.Vcd(run.Model)).Values.Any(t => t.Changes.Any(c => c.Time > made)),
                "sanity: the whole run has changes after it, which the first export left out");
        }

        [Test]
        public void ARunCarriedOnPastItsStop_IsExportedWithTheTimelineItGrewTo()
        {
            RunSession run = RunSession.Open(ReferenceCircuit.Document(), Ticking(1.0));
            run.Finish();
            long end = run.Model.End;
            Assert.IsTrue(run.RunOn(end * 2));
            run.Finish();

            StringAssert.EndsWith("#" + (end * 2) + Environment.NewLine, WaveExport.Vcd(run.Model));
        }

        [TestCase("Reference", "vcd", "reference.vcd")]
        [TestCase("Full adder", "vcd", "full_adder.vcd")]
        [TestCase("3-bit counter!", "png", "3_bit_counter.png")]
        [TestCase("  half / adder  ", ".VCD", "half_adder.vcd")]
        [TestCase("", "vcd", "waveform.vcd")]
        [TestCase(null, "png", "waveform.png")]
        [TestCase("éé", "vcd", "waveform.vcd")]
        public void AnExportsFileName_IsSafeOnEverySystem(string name, string extension, string file) =>
            Assert.AreEqual(file, WaveExport.FileName(name, extension));

        [Test]
        public void AFileName_NeedsAnExtension() => Assert.Throws<ArgumentException>(() => WaveExport.FileName("x", ""));
    }
}
