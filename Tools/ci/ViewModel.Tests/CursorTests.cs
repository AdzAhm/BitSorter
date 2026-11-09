using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace BitSorter.ViewModel.Tests
{
    /// <summary>
    /// The waveform's cursors, its mouse and its keys (Part 9b-2b): the second cursor and its Δ, where
    /// a click lands (<see cref="WaveSnap"/>), every gesture from a scripted list of events
    /// (<see cref="WavePointer"/>), and the key table refusing what the game or a browser owns.
    /// </summary>
    public class CursorTests
    {
        // -----------------------------------------------------------------
        // The second cursor
        // -----------------------------------------------------------------

        /// <summary>
        /// The two cursors' times written apart (the 16d sweep): alone each to its line's right as before; a unit apart, the
        /// earlier to its line's left; both at the waves' right end, apart there; at the left end, both to the right in turn.
        /// </summary>
        [Test]
        public void TheCursorsTimes_AreWrittenApart_WhereTheyWouldMeet()
        {
            const float left = 100f, right = 900f, wide = 40f;
            bool Apart((float A, float B) at) => at.A + wide <= at.B || at.B + wide <= at.A;

            Assert.AreEqual((305f, 605f), WaveCursorLabels.Apart(300f, wide, 600f, wide, left, right), "far apart: each to its line's right, as alone");
            Assert.AreEqual(right - WaveCursorLabels.Room, WaveCursorLabels.Alone(895f, right), "alone at the right end: held inside it");

            (float p, float s) = WaveCursorLabels.Apart(500f, wide, 520f, wide, left, right);
            Assert.IsTrue(Apart((p, s)), $"a unit apart: {p} and {s}");
            Assert.AreEqual(500f - WaveCursorLabels.Gap - wide, p, "the earlier, the playhead, to its line's left");
            Assert.AreEqual(525f, s, "the later to its right");

            (p, s) = WaveCursorLabels.Apart(900f, wide, 890f, wide, left, right);
            Assert.IsTrue(Apart((p, s)), $"both at the right end: {p} and {s}");
            Assert.Less(s, p, "C2, the earlier, before the playhead");
            Assert.LessOrEqual(p + wide, right, "inside the waves");

            (p, s) = WaveCursorLabels.Apart(102f, wide, 104f, wide, left, right);
            Assert.IsTrue(Apart((p, s)), $"both at the left end: {p} and {s}");
            Assert.GreaterOrEqual(Math.Min(p, s), left, "inside the waves");
        }

        [Test]
        public void TheSecondCursor_IsPlaced_Moved_AndTaken_AndDeltaIsFromThePlayhead()
        {
            var cursors = new WaveCursors();
            Assert.IsNull(cursors.Second);
            Assert.IsNull(cursors.Delta(10));

            Assert.IsTrue(cursors.Place(19));
            Assert.AreEqual(9.0, cursors.Delta(10));
            Assert.AreEqual(9.0, cursors.Delta(28), "either side of the playhead, a time");
            int version = cursors.Version;
            Assert.IsFalse(cursors.Place(19), "placed where it is, it does not move");
            Assert.AreEqual(version, cursors.Version);

            Assert.IsTrue(cursors.Place(-3));
            Assert.AreEqual(0.0, cursors.Second, "never before the run");

            Assert.IsTrue(cursors.Clear());
            Assert.IsFalse(cursors.Clear());
            Assert.Throws<ArgumentOutOfRangeException>(() => cursors.Place(double.NaN));
        }

        // -----------------------------------------------------------------
        // Snapping
        // -----------------------------------------------------------------

        private static SignalTrack ATrack()
        {
            // The reference's a: x at 0, rises at 6.
            WorkbenchModel model = ReferenceCircuit.Model();
            int row = Enumerable.Range(0, model.RowCount).Single(r => model.Layout.Rows[r].Label == "a");
            SignalTrack track = model.RowTrack(row);
            Assert.Greater(track.Count, 1, "sanity: a changes");
            return track;
        }

        [Test]
        public void AClickNearAnEdge_LandsOnIt_WithinEightPixels_AndNoFurther()
        {
            SignalTrack track = ATrack();
            double edge = track[1].Time;

            // Every edge is at a whole unit, so the edge and the nearest unit part only when 8 px is more
            // than half a unit: zoomed out to 4 px a unit, 8 px is two units.
            Assert.AreEqual(edge, WaveSnap.Snap(edge + 1.6, 4, track), "6.4 px from the edge, where the nearest unit is two on");
            Assert.AreEqual(edge, WaveSnap.Snap(edge - 1.9, 4, track), "from either side, the nearer of the two edges either side");
            Assert.AreEqual(Math.Round(edge + 2.4), WaveSnap.Snap(edge + 2.4, 4, track), "9.6 px away: the nearest whole unit instead");

            // Zoomed in, the same 8 px is a smaller time.
            Assert.AreEqual(edge, WaveSnap.Snap(edge + 0.05, 100, track), "5 px at 100 a unit");
            Assert.AreEqual(edge + 1, WaveSnap.Snap(edge + 0.6, 100, track), "60 px at 100 a unit is not near: the nearest unit");
        }

        [Test]
        public void AClickWithNoEdgeNear_LandsOnAWholeUnit_AndNeverBeforeTheRun()
        {
            Assert.AreEqual(13.0, WaveSnap.Snap(12.6, 20));
            Assert.AreEqual(13.0, WaveSnap.Snap(12.5, 20), "half way, onwards");
            Assert.AreEqual(0.0, WaveSnap.Snap(-2.0, 20));

            // Zoomed far out, every unit is under a pixel: still a whole unit, the nearest.
            Assert.AreEqual(2716.0, WaveSnap.Snap(2716.3, 0.03));

            Assert.Throws<ArgumentOutOfRangeException>(() => WaveSnap.Snap(1, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => WaveSnap.Snap(double.NaN, 10));
        }

        // -----------------------------------------------------------------
        // Gestures
        // -----------------------------------------------------------------

        private static WavePress On(WaveRegion region, double time = 0, int row = -1, int button = 0, bool shift = false, bool ctrl = false,
            bool alt = false, float? fromPlayhead = null, bool mark = false, int clicks = 1) =>
            new WavePress { Region = region, Time = time, Row = row, Button = button, Shift = shift, Ctrl = ctrl, Alt = alt, FromPlayhead = fromPlayhead, OnGroupMark = mark, Clicks = clicks };

        private static string Run(WavePointer pointer, WavePress press, params double[] moves)
        {
            var commands = new List<WaveCommand>();
            pointer.Press(press, commands);
            foreach (double t in moves)
                pointer.Move(t, commands);
            pointer.Release();
            foreach (double t in moves)
                pointer.Move(t, commands);   // after the release, nothing
            return string.Join(", ", commands.Select(c => c.Gesture + (c.Gesture == WaveGesture.Seek || c.Gesture == WaveGesture.PlaceSecond || c.Gesture == WaveGesture.MoveSecond
                || c.Gesture == WaveGesture.TimeMenu
                ? " " + c.Time : c.Row >= 0 ? " " + c.Row + (c.Extend ? "+" : "") + (c.Toggle ? "^" : "") : "")));
        }

        /// <summary>
        /// A press on the waves places the second cursor and a drag moves it through them, until it is let go (Part 15h: a
        /// press sought the playhead, which hides the waves after it, until Ahmad asked for the two the other way round).
        /// </summary>
        [Test]
        public void APressOnTheWaves_PlacesTheCursor_AndADragMovesIt_UntilItIsLetGo()
        {
            var pointer = new WavePointer();
            Assert.AreEqual("PlaceSecond 4, MoveSecond 5, MoveSecond 6", Run(pointer, On(WaveRegion.Waves, 4), 5, 6));
            Assert.AreEqual("PlaceSecond 2", Run(pointer, On(WaveRegion.Axis, 2)), "the axis is the waves' too");
            Assert.IsFalse(pointer.Dragging);
        }

        /// <summary>Shift and a press seeks the playhead and a drag scrubs it, as a press on the playhead's own line does.</summary>
        [Test]
        public void ShiftAndAPress_SeeksThePlayhead_AndAPressOnItTakesHoldOfIt()
        {
            var pointer = new WavePointer();
            Assert.AreEqual("Seek 19, Seek 20", Run(pointer, On(WaveRegion.Waves, 19, shift: true), 20));
            Assert.AreEqual("Seek 18, Seek 17", Run(pointer, On(WaveRegion.Waves, 18, fromPlayhead: 4f), 17), "4 px from it");
            Assert.AreEqual("PlaceSecond 12", Run(pointer, On(WaveRegion.Waves, 12, fromPlayhead: WavePointer.GrabPixels + 1)),
                "7 px from it places the cursor");
        }

        [Test]
        public void APressOnARow_SelectsIt_OpensItsMenu_OrOpensItsGroup()
        {
            var pointer = new WavePointer();
            Assert.AreEqual("SelectRow 3", Run(pointer, On(WaveRegion.Names, row: 3)));
            Assert.AreEqual("SelectRow 3+", Run(pointer, On(WaveRegion.Values, row: 3, shift: true)));
            Assert.AreEqual("SelectRow 3^", Run(pointer, On(WaveRegion.Names, row: 3, ctrl: true)));
            Assert.AreEqual("RowMenu 2", Run(pointer, On(WaveRegion.Names, row: 2, button: 1)));
            Assert.AreEqual("ToggleGroup 0", Run(pointer, On(WaveRegion.Names, row: 0, mark: true)));
            Assert.AreEqual("", Run(pointer, On(WaveRegion.Names, row: -1)), "under the last row, nothing");
            Assert.AreEqual("SelectRow 3", Run(pointer, On(WaveRegion.Names, row: 3), 5, 6), "and a press on a row never scrubs");
        }

        [Test]
        public void AltAndAPressOnTheCircuit_AddsTheWiresSignal_AndWithoutAltItIsTheCircuits()
        {
            var pointer = new WavePointer();
            Assert.AreEqual("AddWireSignal", Run(pointer, On(WaveRegion.Schematic, alt: true)));
            Assert.AreEqual("", Run(pointer, On(WaveRegion.Schematic), 4, 5));
        }

        [Test]
        public void APressOnAControl_IsNeverAGesture_NorIsAnyDragFromIt()
        {
            var pointer = new WavePointer();
            Assert.AreEqual("", Run(pointer, On(WaveRegion.Control, 4, row: 2), 5, 6), "the slider dragged over the waves stays the slider's");
            Assert.AreEqual("", Run(pointer, On(WaveRegion.None, 4), 5));
        }

        [Test]
        public void ADoubleClickOnARowsWave_AsksToEditItsInput_AndScrubsNothing()
        {
            var pointer = new WavePointer();
            Assert.AreEqual("PlaceSecond 4", Run(pointer, On(WaveRegion.Waves, 4, row: 2)), "the first click places the cursor, as any does");
            Assert.AreEqual("EditInput 2", Run(pointer, On(WaveRegion.Waves, 4, row: 2, clicks: 2), 5, 6), "the second asks to edit row 2, and its drag scrubs nothing");
            Assert.IsFalse(pointer.Dragging);

            Assert.AreEqual("PlaceSecond 4", Run(pointer, On(WaveRegion.Waves, 4, clicks: 2)), "below the rows there is no input to edit");
            Assert.AreEqual("PlaceSecond 4", Run(pointer, On(WaveRegion.Axis, 4, row: 2, clicks: 2)), "nor on the axis");
            Assert.AreEqual("Seek 4", Run(pointer, On(WaveRegion.Waves, 4, row: 2, shift: true, clicks: 2), 5).Split(',')[0], "Shift seeks however quick");
        }

        [Test]
        public void TheRightButtonOnTheWavesOrTheAxis_OpensTheMenuForThatMoment_AndNeverScrubs()
        {
            var pointer = new WavePointer();
            Assert.AreEqual("TimeMenu 4", Run(pointer, On(WaveRegion.Waves, 4, button: 1), 5), "the right button on the waves");
            Assert.AreEqual("TimeMenu 7", Run(pointer, On(WaveRegion.Axis, 7, button: 1), 8), "and on the axis");
            Assert.IsFalse(pointer.Dragging);
        }

        // -----------------------------------------------------------------
        // Keys
        // -----------------------------------------------------------------

        [Test]
        public void NoKey_IsOneTheGameOrABrowserOwns()
        {
            foreach (KeyBinding binding in WorkbenchKeys.All)
                Assert.IsNull(WorkbenchKeys.WhyRefused(binding), binding.ToString());

            Assert.IsNull(WorkbenchKeys.WhyRefused(new KeyBinding("N", WorkbenchCommand.Names, "N", "x")), "N switched the sound, and is free now");
            Assert.IsNotNull(WorkbenchKeys.WhyRefused(new KeyBinding("N", WorkbenchCommand.Names, "N", "x", ctrl: true)), "Ctrl+N is a new window");
            Assert.IsNull(WorkbenchKeys.WhyRefused(new KeyBinding("M", WorkbenchCommand.Names, "M", "x")), "M was the 4.x level list, and is free now");
            Assert.IsNull(WorkbenchKeys.WhyRefused(new KeyBinding("H", WorkbenchCommand.Radix, "H", "x")), "H was the 4.x help, and is free now");
            Assert.AreEqual("Escape is BitSorter's, whatever is on screen",
                WorkbenchKeys.WhyRefused(new KeyBinding("Escape", WorkbenchCommand.Fit, "ESC", "x")));
            Assert.IsNotNull(WorkbenchKeys.WhyRefused(new KeyBinding("F3", WorkbenchCommand.Fit, "F3", "x")), "find in page");
            Assert.IsNull(WorkbenchKeys.WhyRefused(new KeyBinding("F2", WorkbenchCommand.Fit, "F2", "x")), "F2 was the 4.x diagnostics, and no browser takes it");
            Assert.IsNull(WorkbenchKeys.WhyRefused(new KeyBinding("F8", WorkbenchCommand.Fit, "F8", "x")), "F8 was the 4.x timing diagram, and no browser takes it");
            Assert.IsNotNull(WorkbenchKeys.WhyRefused(new KeyBinding("F5", WorkbenchCommand.Fit, "F5", "x")), "reload");
            Assert.IsNotNull(WorkbenchKeys.WhyRefused(new KeyBinding("W", WorkbenchCommand.Fit, "W", "x", ctrl: true)), "closes the tab");
            Assert.IsNull(WorkbenchKeys.WhyRefused(new KeyBinding("W", WorkbenchCommand.Fit, "W", "x")), "W alone is free");
            Assert.IsNull(WorkbenchKeys.WhyRefused(new KeyBinding("F", WorkbenchCommand.Fit, "F", "x")), "and F is not a function key");
        }

        [Test]
        public void EveryCommand_HasAKey_NoKeyDoesTwoThings_AndEachSaysWhatItDoes()
        {
            foreach (WorkbenchCommand command in Enum.GetValues(typeof(WorkbenchCommand)))
                Assert.AreEqual(command, WorkbenchKeys.For(command).Command, $"{command} has no key");

            var seen = new HashSet<string>();
            foreach (KeyBinding binding in WorkbenchKeys.All)
            {
                Assert.IsTrue(seen.Add((binding.Ctrl ? "c" : "") + (binding.Alt ? "a" : "") + (binding.Shift ? "s" : "") + binding.Key), $"{binding} is bound twice");
                Assert.IsFalse(string.IsNullOrWhiteSpace(binding.Says), $"{binding} says nothing");
                Assert.IsFalse(string.IsNullOrWhiteSpace(binding.Cap), $"{binding} has no cap");
                Assert.IsTrue(binding.Cap.All(c => c < 128), $"{binding}'s cap is in the font's own letters");
            }
        }
    }
}
