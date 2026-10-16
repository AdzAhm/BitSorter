using System.Collections.Generic;
using System.Linq;
using BitSorter.Board;
using BitSorter.SystemVerilog;
using BoardStimulus = BitSorter.Board.Stimulus;
using BoardTrack = BitSorter.Board.Track;
using NUnit.Framework;

namespace BitSorter.ViewModel.Tests
{
    /// <summary>
    /// The workbench's view-model read at a moment: what the screen is told to draw, checked against
    /// the run the engine recorded.
    /// </summary>
    /// <remarks>
    /// Every expected value here is the reference circuit's own history (<see cref="ReferenceCircuit"/>):
    /// A rises at 6 and the NOT takes 3 to follow, so the AND's 2 ns rise puts Y at 1 from 8 until the
    /// NOT's fall reaches it, 3 ns after 9; B rises at 13 and its NOT takes 1, a glitch shorter than
    /// the second AND's 2 ns, which swallows it at 14. These ran in Edit Mode until Part 9 moved the
    /// view-model out of Unity.
    /// </remarks>
    public class WorkbenchModelTests
    {
        internal static readonly WorkbenchModel Shared = ReferenceCircuit.Model();

        // Nets, the first AND and waveform rows, found by name in the document's layout rather than by
        // where they happen to fall in its lists.
        internal static int A => Shared.Layout.NetNamed("a");
        internal static int NotA => Shared.Layout.NetNamed("na");
        private static int Y => Shared.Layout.NetNamed("y");
        private static int Y2 => Shared.Layout.NetNamed("y2");
        private static int AndOne => Shared.Layout.Parts.ToList().FindIndex(p => p.Symbol == PartSymbol.And);
        private static int YRow => Row("y");
        private static int SRow => Row("s[3:0]");
        private static int QRow => Row("q");

        private static int Row(string label) => Shared.Layout.Rows.ToList().FindIndex(r => r.Label == label);

        private static WorkbenchModel Model() => ReferenceCircuit.Model();

        private static Logic4 Bit(WorkbenchModel model, int net, double time)
        {
            model.Seek(time);
            return model.NetValue(net)[0];
        }

        [Test]
        public void TheReferenceCircuit_RunsToTheEnd_AndSettlesAt21()
        {
            WorkbenchModel model = Model();

            Assert.AreEqual(ReferenceCircuit.EndTime, model.End);
            Assert.GreaterOrEqual(model.Session.Reached, ReferenceCircuit.EndTime, "the engine stopped short of the end");
            Assert.AreEqual(21, model.SettledAt, "the last thing that happens is OUT taking 5, at 21");
        }

        [Test]
        public void TheGlitch_PassesTheFirstAnd()
        {
            WorkbenchModel model = Model();

            Assert.AreEqual(Logic4.Zero, Bit(model, Y, 7.9));
            Assert.AreEqual(Logic4.One, Bit(model, Y, 8.0), "A rose at 6 and the AND rises in 2");
            Assert.AreEqual(Logic4.One, Bit(model, Y, 11.9));
            Assert.AreEqual(Logic4.Zero, Bit(model, Y, 12.0), "NOT A fell at 9 and the AND falls in 3");
        }

        [Test]
        public void EverythingDriven_IsUnknownUntilItsDriverHasHadItsDelay()
        {
            WorkbenchModel model = Model();

            Assert.AreEqual(Logic4.X, Bit(model, NotA, 2.9), "NOT A is x before its 3 ns are up");
            Assert.AreEqual(Logic4.One, Bit(model, NotA, 3.0));
            Assert.AreEqual(Logic4.Zero, Bit(model, A, 0.0), "an input is what the testbench wrote at once");
        }

        [Test]
        public void TheShortGlitch_IsSwallowed_AndDiesOnItsWire()
        {
            WorkbenchModel model = Model();
            StageRoute wire = model.Layout.Nets[Y2].Branches[0];

            model.Seek(13.5);
            ChangeMarker travelling = model.Markers.Single(m => m.Net == Y2);
            Assert.IsFalse(travelling.Swallowed, "at 13.5 the change is still on its way");
            Assert.AreEqual(0.25f * wire.Length, travelling.Distance, 1e-3f, "half a ns into its 2 ns");
            Assert.AreEqual("1", travelling.Text);
            Assert.AreEqual(2, model.InFlight, "NOT B's fall and the AND's doomed rise");

            model.Seek(14.6);
            ChangeMarker dying = model.Markers.Single(m => m.Net == Y2);
            Assert.IsTrue(dying.Swallowed, "the AND's input fell at 14 and cancelled it");
            Assert.AreEqual(0.5f * wire.Length, dying.Distance, 1e-3f, "it stopped where it was at 14");
            Assert.AreEqual(0.5f, dying.Alpha, 1e-3f, "and fades over 1.2 ns");

            model.Seek(15.3);
            Assert.IsFalse(model.Markers.Any(m => m.Net == Y2), "gone once it has faded");

            foreach (double time in new[] { 13.5, 14.6, 15.3, 20.0 })
                Assert.AreEqual(Logic4.Zero, Bit(model, Y2, time), $"Y2 never moved, at {time}");
        }

        /// <summary>
        /// The glitch the AND swallows is marked on y2's row of the waveform, where it would have
        /// arrived: a gate with delay prints as a library cell, so the change was on its way to the
        /// cell's own output, which is one of the net's names rather than the port's.
        /// </summary>
        [Test]
        public void TheShortGlitch_IsMarkedOnItsRow_WhenItIsSwallowed()
        {
            WorkbenchModel model = Model();
            var swallowed = new List<double>();
            int row = Row("y2");

            model.RowSwallowed(row, swallowed, model.End);
            CollectionAssert.AreEqual(new[] { 14.0 }, swallowed, "the AND's input fell at 14 and cancelled the change");

            model.RowSwallowed(row, swallowed, 13.9);
            CollectionAssert.IsEmpty(swallowed, "not before it was swallowed");
            Assert.AreEqual(1, model.RowSwallowedCount(row));
        }

        [Test]
        public void ARing_IsWhereItsChangeHasGot()
        {
            WorkbenchModel model = Model();
            model.Seek(7.5);

            ChangeMarker notA = model.Markers.Single(m => m.Net == NotA);
            StageRoute wire = model.Layout.Nets[NotA].Branches[0];

            Assert.AreEqual(0.5f * wire.Length, notA.Distance, 1e-3f, "1.5 ns into the NOT's 3");
            Assert.AreEqual(wire.PointAt(notA.Distance), notA.Point);
            Assert.AreEqual("0", notA.Text);

            ChangeMarker y = model.Markers.Single(m => m.Net == Y);
            Assert.AreEqual(0.75f * model.Layout.Nets[Y].Branches[0].Length, y.Distance, 1e-3f,
                "1.5 ns into the AND's 2 ns rise");
        }

        [Test]
        public void AGate_FlashesForAMoment_AsItsOutputFlips()
        {
            WorkbenchModel model = Model();

            model.Seek(8.2);
            Assert.Greater(model.GateFlash(AndOne, out LogicVec value), 0f, "Y flipped at 8");
            Assert.AreEqual(Logic4.One, value[0]);

            model.Seek(8.75);
            Assert.AreEqual(0f, model.GateFlash(AndOne, out _), "the flash is over after 0.7 ns");
        }

        [Test]
        public void NothingIsOnItsWay_OnceTheRunHasSettled()
        {
            WorkbenchModel model = Model();
            model.Seek(22);

            Assert.AreEqual(0, model.InFlight);
            Assert.IsEmpty(model.Markers);
        }

        [Test]
        public void TheWaveform_WritesABusInHex_AndUndrivenAsZ()
        {
            WorkbenchModel model = Model();

            model.Seek(0.5);
            model.RowValue(SRow, out string before);
            Assert.AreEqual("x", before, "S is unknown until the testbench writes it at 1");

            model.Seek(10.5);
            model.RowValue(SRow, out string written);
            Assert.AreEqual("A", written);

            model.Seek(5);
            model.RowValue(QRow, out string q);
            Assert.AreEqual("z", q, "the tri-state is off until its enable rises at 17");

            Assert.AreEqual("A", WorkbenchModel.TextOf(LogicVec.FromUInt64(0xA, 4)));
            Assert.AreEqual("X", WorkbenchModel.TextOf(LogicVec.FromBinary("1x01")), "a digit partly unknown, as $display writes it");
            Assert.AreEqual("0X", WorkbenchModel.TextOf(LogicVec.FromBinary("00001x01")), "and the known digits still say so");
            Assert.AreEqual("x", WorkbenchModel.TextOf(LogicVec.AllX(4)));
        }

        [Test]
        public void TheWaveform_EndsAtTheMomentShown()
        {
            WorkbenchModel model = Model();
            var segments = new List<WaveSegment>();

            model.Seek(9.0);
            model.RowSegments(YRow, segments);

            Assert.AreEqual(3, segments.Count, "x, then 0 from 3, then 1 from 8");
            Assert.AreEqual(9.0, segments[2].To, 1e-9);
        }

        /// <summary>
        /// A step after the stimulus's end is played: the testbench stops at its last step, so the
        /// workbench's run goes on to it rather than cutting it off at the end.
        /// </summary>
        [Test]
        public void ARunGoesOnToAStepAfterTheEnd_AsTheTestbenchDoes()
        {
            var editor = new BoardEditor(ReferenceCircuit.Document());
            BoardStimulus stimulus = editor.Project.Stimulus;
            var tracks = stimulus.Tracks.ToList();
            int a = tracks.FindIndex(t => t.Port == "a");
            tracks[a] = tracks[a] with { Steps = tracks[a].Steps.Concat(new[] { new Step(30, LogicVec.FromUInt64(0, 1)) }).ToArray() };
            Assert.IsTrue(editor.SetStimulus(stimulus with { Tracks = tracks.ToArray() }).Done, "sanity: a step after the end is allowed");

            WorkbenchModel model = WorkbenchModel.Of(editor.Project);

            Assert.AreEqual(30, model.End, "the testbench stops at its last step, not at End (26)");
            int row = model.Layout.Rows.ToList().FindIndex(r => r.Label == "a");
            model.Seek(30);
            model.RowValue(row, out string late);
            Assert.AreEqual("0", late, "the step at 30 is shown, not cut off");
        }

        [Test]
        public void TheGlow_IsAhmadsChoice()
        {
            Assert.AreEqual(0.35f, SchematicStyle.GlowStrength, "chosen on the mock's slider, 2026-10-12");
        }
    }
}
