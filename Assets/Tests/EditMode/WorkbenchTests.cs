using System.Linq;
using BitSorter.SystemVerilog;
using BitSorter.View;
using NUnit.Framework;
using UnityEngine;

namespace BitSorter.LogicCore.Tests
{
    /// <summary>
    /// The 5.0 workbench preview's view-model, its wire geometry and its colours: what the screen is
    /// told to draw at a moment, checked against the run the engine recorded.
    /// </summary>
    /// <remarks>
    /// Every expected value here is the reference circuit's own history (<see cref="ReferenceCircuit"/>):
    /// A rises at 6 and the NOT takes 3 to follow, so the AND's 2 ns rise puts Y at 1 from 8 until the
    /// NOT's fall reaches it, 3 ns after 9; B rises at 13 and its NOT takes 1, a glitch shorter than
    /// the second AND's 2 ns, which swallows it at 14.
    /// </remarks>
    public class WorkbenchTests
    {
        private static readonly WorkbenchModel Shared = ReferenceCircuit.Model();

        // Nets, the first AND and waveform rows, found by name in the document's layout rather than by
        // where they happen to fall in its lists.
        private static int A => Shared.Layout.NetNamed("a");
        private static int NotA => Shared.Layout.NetNamed("na");
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

        // -----------------------------------------------------------------
        // The run
        // -----------------------------------------------------------------

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
            Route wire = model.Layout.Nets[Y2].Branches[0];

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

        [Test]
        public void ARing_IsWhereItsChangeHasGot()
        {
            WorkbenchModel model = Model();
            model.Seek(7.5);

            ChangeMarker notA = model.Markers.Single(m => m.Net == NotA);
            Route wire = model.Layout.Nets[NotA].Branches[0];

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
            Assert.AreEqual("x", WorkbenchModel.TextOf(LogicVec.FromBinary("1x01")));
        }

        [Test]
        public void TheWaveform_EndsAtTheMomentShown()
        {
            WorkbenchModel model = Model();
            var segments = new System.Collections.Generic.List<WaveSegment>();

            model.Seek(9.0);
            model.RowSegments(YRow, segments);

            Assert.AreEqual(3, segments.Count, "x, then 0 from 3, then 1 from 8");
            Assert.AreEqual(9.0, segments[2].To, 1e-9);
        }

        // -----------------------------------------------------------------
        // Routes
        // -----------------------------------------------------------------

        [Test]
        public void AStraightRoute_IsAsLongAsTheLineBetweenItsEnds()
        {
            var route = new Route(new Vector2(0, 0), new Vector2(100, 0));

            Assert.AreEqual(100f, route.Length, 1e-3f);
            Assert.AreEqual(new Vector2(50, 0), route.PointAt(50));
        }

        [Test]
        public void ARoundedBend_IsShorterThanTheCorner_ButNotByMuch()
        {
            var route = new Route(new Vector2(0, 0), new Vector2(100, 0), new Vector2(100, 100));

            Assert.Less(route.Length, 200f, "the bend cuts the corner");
            Assert.Greater(route.Length, 195f, "by a few units, not a diagonal");
            Assert.AreEqual(new Vector2(0, 0), route.PointAt(0));
            Assert.AreEqual(new Vector2(100, 100), route.PointAt(route.Length));
            Assert.AreEqual(new Vector2(100, 100), route.PointAt(route.Length + 50f), "clamped past the end");
        }

        [Test]
        public void APointAlongARoute_MovesForwardAsTheDistanceGrows()
        {
            Route route = Shared.Layout.Nets[NotA].Branches[0];
            float previous = -1f;

            for (float d = 0f; d <= route.Length; d += route.Length / 40f)
            {
                Vector2 p = route.PointAt(d);
                Assert.GreaterOrEqual(p.x, previous - 1e-3f, "the NOT's output runs left to right throughout");
                previous = p.x;
            }
        }

        // -----------------------------------------------------------------
        // Colours
        // -----------------------------------------------------------------

        [Test]
        public void EveryValue_IsDrawnClearOfTheGround()
        {
            SchematicPalette p = SchematicPalette.Dark;

            foreach ((string what, Color colour) in new[] { ("0", p.Zero), ("1", p.One), ("x", p.Unknown), ("z", p.HighZ), ("a bus", p.Bus), ("an outline", p.Outline) })
                Assert.GreaterOrEqual(SchematicPalette.Contrast(colour, p.Ground), 3f, $"{what} is not 3:1 against the ground");

            foreach ((string what, Color colour) in new[] { ("text", p.Text), ("secondary text", p.TextDim), ("a delay", p.Muted), ("the playhead's time", p.Accent) })
                Assert.GreaterOrEqual(SchematicPalette.Contrast(colour, p.Ground), 4.5f, $"{what} is not 4.5:1 against the ground");
        }

        [Test]
        public void ZeroAndOne_DifferByMoreThanColour()
        {
            Assert.AreNotEqual(SchematicPalette.Dark.Zero, SchematicPalette.Dark.One);
            Assert.Greater(SchematicStyle.OneWidth, SchematicStyle.ZeroWidth, "a 1 is drawn a little heavier than a 0");
        }

        [Test]
        public void AFaintColour_TakesLessAlphaInLinearSpace_ThanABrowserWould()
        {
            SchematicPalette p = SchematicPalette.Dark;

            Assert.AreEqual(0f, p.Over(p.One, 0f).a, 1e-4f);
            Assert.AreEqual(1f, p.Over(p.One, 1f).a, 1e-4f);
            Assert.Less(p.Over(p.One, 0.35f).a, 0.35f,
                "a light colour over a dark ground blends brighter in linear space, so it needs less alpha to look the same");
            Assert.Greater(p.Over(p.One, 0.35f).a, 0f);
        }

        [Test]
        public void TheGlow_IsAhmadsChoice()
        {
            Assert.AreEqual(0.35f, SchematicStyle.GlowStrength, "chosen on the mock's slider, 2026-10-12");
        }
    }
}
