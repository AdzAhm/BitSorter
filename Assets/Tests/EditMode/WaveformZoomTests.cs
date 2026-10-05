using NUnit.Framework;
using BitSorter.View;

namespace BitSorter.LogicCore.Tests
{
    /// <summary>
    /// <see cref="WaveformZoom"/>: the timing diagram fits the level's run across the strip, zooms in
    /// from there and back out to it, and keeps the tick under the cursor where it was.
    /// </summary>
    public class WaveformZoomTests
    {
        /// <summary>Room for ticks at 1080: the strip from the parts list to the margin, less its names.</summary>
        private const float Room = 1660f;

        [Test]
        public void TheExpectedRun_IsTheStreamsAPeriodApart_AndRoomToArrive()
        {
            Assert.AreEqual(4 + 4, WaveformZoom.ExpectedRun(4, 1, 4), "the half adder, on the 9 by 5 board");
            Assert.AreEqual(22 + 4, WaveformZoom.ExpectedRun(8, 3, 4),
                "eight vectors three ticks apart: the last goes out on tick 21");
            Assert.AreEqual(1 + 1, WaveformZoom.ExpectedRun(0, 0, 0), "nothing to stream still takes a tick");
        }

        [Test]
        public void TheFit_HoldsTheWholeRun_UpToWhatIsRecorded()
        {
            Assert.AreEqual(8, WaveformZoom.FitTicks(8, -1, 128), "before a run, the run expected");
            Assert.AreEqual(8, WaveformZoom.FitTicks(8, 5, 128), "a run shorter than expected");
            Assert.AreEqual(13, WaveformZoom.FitTicks(8, 12, 128), "a run that outlasts the estimate");
            Assert.AreEqual(128, WaveformZoom.FitTicks(8, 400, 128), "older ticks are no longer held");
        }

        /// <summary>
        /// Fitted, a short run fills the strip rather than a sliver at its left -- what the zoom was
        /// asked for -- and a run of any length shows every one of its ticks.
        /// </summary>
        [Test]
        public void AFittedRun_FillsTheStrip_AndShowsEveryTick()
        {
            float half = WaveformZoom.FitWidth(Room, 8);
            Assert.AreEqual(WaveformZoom.MaxTickWidth, half, "a run of eight is as wide as a tick is drawn");
            Assert.GreaterOrEqual(WaveformZoom.Visible(Room, half), 8);

            for (float room = 900f; room <= 1800f; room += 7f)
            {
                for (int ticks = 1; ticks <= 128; ticks++)
                {
                    float width = WaveformZoom.FitWidth(room, ticks);

                    if (width > WaveformZoom.MinTickWidth && width < WaveformZoom.MaxTickWidth)
                    {
                        Assert.AreEqual(ticks, WaveformZoom.Visible(room, width),
                            $"{ticks} ticks fitted across {room} show some other number");
                    }
                }
            }
        }

        [Test]
        public void TheWheel_ZoomsFromTheFit_AndBackOutOnlyAsFarAsIt()
        {
            Assert.AreEqual(1.25f, WaveformZoom.Zoomed(1f, 1, 50f), 1e-5f, "one notch in");
            Assert.AreEqual(1f, WaveformZoom.Zoomed(1f, -1, 50f), "out past the whole run");
            Assert.AreEqual(1f, WaveformZoom.Zoomed(3.2f, -100, 50f), "all the way back out");
            Assert.AreEqual(WaveformZoom.MaxTickWidth / 50f, WaveformZoom.Zoomed(1f, 100, 50f), 1e-4f,
                "in past the widest tick");
            Assert.AreEqual(1f, WaveformZoom.Zoomed(1f, 1, WaveformZoom.MaxTickWidth),
                "a run already as wide as a tick is drawn has nothing to zoom into");

            Assert.AreEqual(62.5f, WaveformZoom.TickWidth(50f, 1.25f), 1e-4f);
            Assert.AreEqual(50f, WaveformZoom.TickWidth(50f, 0.5f), "a zoom under the fit");
        }

        /// <summary>
        /// A short run, fitted already at the widest a fitted tick is drawn, still zooms in.
        /// </summary>
        /// <remarks>
        /// From a playtest, 2026-10-05: on a short level neither the wheel nor Shift with it did
        /// anything to the strip, whose header says both do. The zoom stopped at the widest fitted
        /// tick, so a run fitted there had nothing to zoom into -- and with the whole run in view,
        /// nothing to move to either.
        /// </remarks>
        [Test]
        public void AShortRun_FittedAtTheWidest_StillZoomsIn()
        {
            float zoomed = WaveformZoom.Zoomed(1f, 1, WaveformZoom.MaxTickWidth);

            Assert.Greater(zoomed, 1f, "one notch in on a short run did nothing");
            Assert.Greater(WaveformZoom.TickWidth(WaveformZoom.MaxTickWidth, zoomed), WaveformZoom.MaxTickWidth,
                "zoomed in on a short run, the ticks are no wider");
        }

        /// <summary>The tick under the cursor stays under it as the strip zooms.</summary>
        [Test]
        public void AZoom_KeepsTheTickUnderTheCursor()
        {
            // Tick 8 is under the cursor, 400 units in at 50 a tick; at 100 a tick it is 4 ticks in.
            int start = WaveformZoom.StartAfterZoom(0, 400f, 50f, 100f);
            Assert.AreEqual(4, start);
            Assert.AreEqual(8f, start + 400f / 100f, 1e-4f, "the tick under the cursor moved");

            Assert.AreEqual(7, WaveformZoom.StartAfterZoom(7, 0f, 50f, 100f), "zoomed at the left edge");
        }

        [Test]
        public void TheView_StaysBetweenTheOldestTickHeldAndTheLatest()
        {
            Assert.AreEqual(5, WaveformZoom.ClampStart(5, 0, 20));
            Assert.AreEqual(0, WaveformZoom.ClampStart(-3, 0, 20), "before the first tick");
            Assert.AreEqual(20, WaveformZoom.ClampStart(30, 0, 20), "past the latest ticks");
            Assert.AreEqual(12, WaveformZoom.ClampStart(10, 12, 4), "older than what is still held");

            Assert.AreEqual(5, WaveformZoom.PanTicks(40), "an eighth of the view");
            Assert.AreEqual(1, WaveformZoom.PanTicks(5), "always at least a tick");
        }

        /// <summary>
        /// Tick numbers are on every tick while ticks are wide, further apart as they narrow, and
        /// never close enough to touch.
        /// </summary>
        [Test]
        public void TickNumbers_ThinOutAsTicksNarrow_AndNeverTouch()
        {
            Assert.AreEqual(1, WaveformZoom.NumberStep(160f));
            Assert.AreEqual(2, WaveformZoom.NumberStep(24f));
            Assert.AreEqual(4, WaveformZoom.NumberStep(WaveformZoom.MinTickWidth));
            Assert.AreEqual(WaveformZoom.MaxNumberStep, WaveformZoom.NumberStep(1f), "no further apart than the most");

            for (float width = WaveformZoom.MinTickWidth; width <= WaveformZoom.MaxTickWidth; width += 0.25f)
            {
                Assert.GreaterOrEqual(WaveformZoom.NumberStep(width) * width, WaveformZoom.LabelSpacing,
                    $"numbers {width} units apart touch");
            }
        }
    }
}
