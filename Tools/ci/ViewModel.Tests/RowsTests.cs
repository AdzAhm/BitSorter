using System;
using System.Collections.Generic;
using System.Linq;
using BitSorter.Board;
using BitSorter.Sim;
using BitSorter.SystemVerilog;
using NUnit.Framework;

namespace BitSorter.ViewModel.Tests
{
    /// <summary>
    /// The waveform's rows as any signals of the design (Part 9b-2b): which names are one node
    /// (<see cref="SignalAliases"/>), a row of a block's inside with its values and its glitch marks,
    /// a row's radix in its words and on its wire's rings, and rows set while the run is still made.
    /// </summary>
    public class RowsTests
    {
        private static int Signal(WorkbenchModel model, string name)
        {
            Assert.IsTrue(model.Session.Design.TryFind(name, out SignalId id), $"the design has no {name}");
            return id.Index;
        }

        private static string Path(Project project) => BoardCompiler.Compile(project).DesignPath;

        private static Func<double> Ticking(double step)
        {
            double now = 0;
            return () => { double read = now; now += step; return read; };
        }

        // -----------------------------------------------------------------
        // Nodes
        // -----------------------------------------------------------------

        [Test]
        public void AFullAddersNodes_RunThroughItsBlocks_AndStopAtEveryGate()
        {
            Project project = WorkbenchSamples.FullAdder();
            WorkbenchModel model = WorkbenchModel.Of(project);
            string dut = Path(project), tb = dut.Substring(0, dut.LastIndexOf('.'));
            SignalAliases aliases = model.Aliases;
            bool Same(string a, string b) => aliases.SameNode(Signal(model, a), Signal(model, b));

            // h1's sum: its XOR's output, h1's port, the parent's net, and h2's input and its XOR's.
            string[] sum = { dut + ".h1.g1.Z", dut + ".h1.s", dut + ".h1_s", dut + ".h2.a", dut + ".h2.g1.A" };
            foreach (string name in sum)
                Assert.IsTrue(Same(sum[0], name), $"{name} is h1's sum, one node with its XOR's output");

            // The full adder's s, from h2's XOR out to the testbench.
            Assert.IsTrue(Same(dut + ".h2.g1.Z", dut + ".s"));
            Assert.IsTrue(Same(dut + ".s", tb + ".s"), "an output port and the testbench's net it drives are one");

            // A gate is never a join: its inputs and its output are nodes apart.
            Assert.IsFalse(Same(dut + ".h1.a", dut + ".h1.s"), "a XOR's input is not its output");
            Assert.IsFalse(Same(dut + ".h1.g1.A", dut + ".h1.g1.Z"));
            Assert.IsFalse(Same(dut + ".h1_s", dut + ".h1_c"), "two outputs of one block are two nodes");

            Assert.AreEqual(-1, aliases.NodeOf(-1));
            Assert.IsFalse(aliases.SameNode(-1, -1), "a signal the design does not have is no node");
        }

        [Test]
        public void TheReferencesNodes_AreItsNetsAndTheCellsDrivingThem_AsTheLayoutSaid()
        {
            WorkbenchModel model = ReferenceCircuit.Model();
            foreach (NetLayout net in model.Layout.Nets)
            {
                int signal = Signal(model, net.Signal);
                foreach (string alias in net.Aliases)
                    Assert.IsTrue(model.Aliases.SameNode(signal, Signal(model, alias)), $"{alias} is a name of {net.Name}, as the layout's aliases say");
            }
        }

        // -----------------------------------------------------------------
        // Rows of any signal
        // -----------------------------------------------------------------

        [Test]
        public void TheDefaultRows_AreTheLayoutsPorts_InTheirDefaultRadix()
        {
            WorkbenchModel model = ReferenceCircuit.Model();
            Assert.AreEqual(model.Layout.Rows.Count, model.RowCount);
            for (int row = 0; row < model.RowCount; row++)
            {
                Assert.AreEqual(Signal(model, model.Layout.Rows[row].Signal), model.RowSignal(row));
                Assert.AreEqual(model.RowIsBus(row) ? WaveRadix.Hex : WaveRadix.Binary, model.RowRadix(row));
            }
        }

        [Test]
        public void ARowInsideABlock_ReadsItsValues()
        {
            Project project = WorkbenchSamples.FullAdder();
            WorkbenchModel model = WorkbenchModel.Of(project);
            string dut = Path(project);
            int inner = Signal(model, dut + ".h1.s");
            int version = model.RowsVersion;

            model.SetRows(new[] { inner, -1 });
            Assert.AreEqual(2, model.RowCount);
            Assert.Greater(model.RowsVersion, version, "a new set of rows is a new version");
            Assert.IsNull(model.RowTrack(1), "a row with no signal, as a group's header has, has no track");
            Assert.AreEqual(0, model.RowChangeCount(1));

            // h1's sum is a XOR b, a the top bit of the combination stepped every 6 ns: 001 from 6,
            // 010 from 12, 110 from 36 -- each settled 2 ns in, through the XOR.
            model.Seek(11.0);
            Assert.AreEqual("0", Text(model, 0), "a XOR b with a = 0 and b = 0");
            model.Seek(17.0);
            Assert.AreEqual("1", Text(model, 0), "with a = 0 and b = 1");
            model.Seek(41.0);
            Assert.AreEqual("0", Text(model, 0), "and with a = 1 and b = 1");
        }

        private static string Text(WorkbenchModel model, int row)
        {
            model.RowValue(row, out string text);
            return text;
        }

        [Test]
        public void ARowInsideTheCounter_GetsTheGlitchMarksSwallowedOnItsWire()
        {
            Project project = WorkbenchSamples.Counter();
            WorkbenchModel model = WorkbenchModel.Of(project);
            Assert.AreEqual(0, Enumerable.Range(0, model.RowCount).Sum(model.RowSwallowedCount),
                "sanity: no port of the counter shows a change swallowed");

            NetLayout d2 = model.Layout.Nets.Single(n => n.Name == "d2");
            model.SetRows(new[] { Signal(model, d2.Signal) });

            var marks = new List<double>();
            model.RowSwallowed(0, marks, model.End);
            CollectionAssert.AreEqual(new[] { 19.0, 35.0 }, marks, "the two changes swallowed on d2's wire, where they died");

            model.Seek(20.0);
            Assert.AreEqual(1, model.RowSwallowedSoFar(0), "one of them by 20");
        }

        [Test]
        public void RowsSetWhileTheRunIsMade_AreMarkedAsRowsSetAfterIt()
        {
            Project project = WorkbenchSamples.Counter();
            WorkbenchModel whole = WorkbenchModel.Of(project);
            NetLayout d2 = whole.Layout.Nets.Single(n => n.Name == "d2");
            int[] rows = { Signal(whole, d2.Signal), Signal(whole, whole.Layout.Rows[0].Signal) };
            whole.SetRows(rows);

            RunSession run = RunSession.Open(project, Ticking(0.5), chunkEvents: 3);
            Assert.IsFalse(run.Done, "sanity: the run is made in slices");
            run.Model.SetRows(rows);
            int frames = 0;
            while (!run.Done && frames++ < 100000)
                run.Frame();

            var a = new List<double>();
            var b = new List<double>();
            for (int row = 0; row < rows.Length; row++)
            {
                whole.RowSwallowed(row, a, whole.End);
                run.Model.RowSwallowed(row, b, whole.End);
                CollectionAssert.AreEqual(a, b, $"row {row}'s marks");
                Assert.AreEqual(whole.RowChangeCount(row), run.Model.RowChangeCount(row), $"row {row}'s changes");
            }
        }

        [Test]
        public void SetRows_RefusesASignalTheDesignDoesNotHave_AndRadixesOfAnotherCount()
        {
            WorkbenchModel model = ReferenceCircuit.Model();
            Assert.Throws<ArgumentOutOfRangeException>(() => model.SetRows(new[] { model.Session.Design.Signals.Count }));
            Assert.Throws<ArgumentOutOfRangeException>(() => model.SetRows(new[] { -2 }));
            Assert.Throws<ArgumentException>(() => model.SetRows(new[] { 0, 1 }, new[] { WaveRadix.Hex }));
            Assert.Throws<ArgumentNullException>(() => model.SetRows(null));
        }

        // -----------------------------------------------------------------
        // Radix
        // -----------------------------------------------------------------

        [Test]
        public void ARowsRadix_WritesItsReadout_ItsBoxes_AndTheRingsOnItsWire()
        {
            WorkbenchModel model = ReferenceCircuit.Model();
            int s = Enumerable.Range(0, model.RowCount).Single(r => model.Layout.Rows[r].Label == "s[3:0]");
            int @out = Enumerable.Range(0, model.RowCount).Single(r => model.Layout.Rows[r].Label == "out[3:0]");
            int[] signals = Enumerable.Range(0, model.RowCount).Select(model.RowSignal).ToArray();
            WaveRadix[] radixes = Enumerable.Range(0, model.RowCount).Select(model.RowRadix).ToArray();

            // The ring on out's wire carrying A, found while it is still hex.
            int outNet = model.NetOfRow(@out);
            Assert.GreaterOrEqual(outNet, 0, "sanity: out's row shows a drawn net");
            double ringAt = -1;
            for (double t = 0; t <= model.End && ringAt < 0; t += 0.05)
            {
                model.Seek(t);
                if (model.Markers.Any(m => m.Net == outNet && m.Text == "A"))
                    ringAt = t;
            }

            Assert.GreaterOrEqual(ringAt, 0, "sanity: an A travels out's wire in hex");

            radixes[s] = WaveRadix.Unsigned;
            radixes[@out] = WaveRadix.Signed;
            model.SetRows(signals, radixes);

            model.Seek(13.5);
            Assert.AreEqual("10", Text(model, s), "s is A: unsigned, 10");
            var segments = new List<WaveSegment>();
            model.RowSegments(s, segments, model.End);
            CollectionAssert.Contains(segments.Select(g => g.Text).ToList(), "3", "its boxes in the row's radix too");
            CollectionAssert.Contains(segments.Select(g => g.Text).ToList(), "10");

            model.Seek(ringAt);
            Assert.IsTrue(model.Markers.Any(m => m.Net == outNet && m.Text == "-6"), "out's ring reads 4'b1010 as signed, -6, as its row does");
            int sNet = model.NetOfRow(s);
            Assert.IsFalse(model.Markers.Any(m => m.Net != outNet && m.Net != sNet && m.Text == "-6"), "no other wire took the radix");
        }

        [Test]
        public void SelectingARow_FindsItsNet_AndANetFindsItsRows()
        {
            WorkbenchModel model = ReferenceCircuit.Model();
            for (int row = 0; row < model.RowCount; row++)
            {
                int net = model.NetOfRow(row);
                Assert.GreaterOrEqual(net, 0, $"{model.Layout.Rows[row].Label}'s row shows a drawn net");
                Assert.IsTrue(model.RowShowsNet(row, net));
                Assert.IsTrue(model.Aliases.SameNode(model.RowSignal(row), Signal(model, model.Layout.Nets[net].Signal)));
            }

            int a = Enumerable.Range(0, model.RowCount).Single(r => model.Layout.Rows[r].Label == "a");
            int na = model.Layout.NetNamed("na");
            Assert.IsFalse(model.RowShowsNet(a, na), "a's row is not NOT A's wire");
            Assert.IsFalse(model.RowShowsNet(a, -1));
        }
    }
}
