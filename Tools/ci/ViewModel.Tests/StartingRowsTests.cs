using System.Collections.Generic;
using System.Linq;
using BitSorter.Board;
using NUnit.Framework;

namespace BitSorter.ViewModel.Tests
{
    /// <summary>
    /// The rows a sample opens on beyond its top's ports (5.1, Ahmad: every RISC-V program showed only clk and rst). A processor's
    /// top has only those two ports, its memories inside it, so the rows that show it working are named by the sample
    /// (<see cref="WorkbenchSamples.StartingRows"/>) and found in the design by their keys.
    /// </summary>
    public class StartingRowsTests
    {
        private static readonly string[] Processors = { "Single-cycle RISC-V", "Multicycle RISC-V", "Pipelined RISC-V" };

        private static WorkbenchModel ModelOf(string sample)
        {
            double now = 0;
            Project project = WorkbenchSamples.All.Single(s => s.Name == sample).Build();
            return new WorkbenchSession(project, () => now += 0.01).Run.Model;
        }

        /// <summary>Every row a processor names is a signal of its design: a rename in the code can never drop one without a word.</summary>
        [TestCaseSource(nameof(Processors))]
        public void EveryStartingRow_IsASignalOfTheDesign(string sample)
        {
            IReadOnlyList<string> rows = WorkbenchSamples.StartingRows(sample);
            Assert.GreaterOrEqual(rows.Count, 6, "sanity: a processor names the rows that show it working");
            WorkbenchModel model = ModelOf(sample);
            foreach (string key in rows)
                Assert.IsTrue(model.Signals.TryFind(key, out _), $"{sample} names {key}, which its design does not have");
        }

        /// <summary>A processor opens on its ports, clk and rst, then the rows it names, in their order.</summary>
        [TestCaseSource(nameof(Processors))]
        public void AProcessor_OpensOnItsPorts_ThenTheRowsItNames(string sample)
        {
            WorkbenchModel model = ModelOf(sample);
            WaveList ports = WaveList.OfPorts(model);
            WaveList list = WaveList.OfPorts(model, WorkbenchSamples.StartingRows(sample));

            string[] keys = list.AllSignals.Select(s => s.Key).ToArray();
            string[] portKeys = ports.AllSignals.Select(s => s.Key).ToArray();
            CollectionAssert.AreEqual(portKeys, keys.Take(portKeys.Length).ToArray(), "the ports first, as any circuit opens");
            CollectionAssert.AreEqual(WorkbenchSamples.StartingRows(sample).ToArray(), keys.Skip(portKeys.Length).ToArray(),
                "then the rows it names, in their order");
            Assert.IsTrue(list.AllSignals.All(s => s.Signal >= 0), "every row found in the run");
        }

        /// <summary>A sample that names none opens as before, on its ports alone; and a name the design lacks is passed over.</summary>
        [Test]
        public void ASampleNamingNone_OpensOnItsPorts_AndAMissingKeyIsPassedOver()
        {
            Assert.IsEmpty(WorkbenchSamples.StartingRows("Full adder"));
            WorkbenchModel model = ModelOf("Single-cycle RISC-V");
            WaveList list = WaveList.OfPorts(model, new[] { "pc", "no_such_signal" });
            CollectionAssert.DoesNotContain(list.AllSignals.Select(s => s.Key).ToArray(), "no_such_signal");
            CollectionAssert.Contains(list.AllSignals.Select(s => s.Key).ToArray(), "pc");
        }
    }
}
