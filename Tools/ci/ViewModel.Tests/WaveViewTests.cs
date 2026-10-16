using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NUnit.Framework;

namespace BitSorter.ViewModel.Tests
{
    /// <summary>
    /// <see cref="WaveView"/> (Part 9b-2a): the waveform's time axis fits the run, zooms in from the
    /// fit about the cursor and back out to it exactly, moves through time inside the run, follows
    /// the playhead a page at a time until a gesture takes it elsewhere, numbers itself on a 1-2-5
    /// ladder, places a moment late in a long run to the hundredth of a pixel, and draws changes too
    /// close to tell apart as one busy span.
    /// </summary>
    public class WaveViewTests
    {
        /// <summary>The 4.x strip's room for ticks at 1080: the width the rules were first tuned on.</summary>
        private const float Strip = 1660f;

        /// <summary>How long the corpus processor runs: the longest run the course's work asks for.</summary>
        private const long ProcessorRun = 27160;

        private static WaveView Fitted(long span, float width = 1000f)
        {
            var view = new WaveView(width);
            view.Fit(span);
            return view;
        }

        // -----------------------------------------------------------------
        // Fit, zoom and pan
        // -----------------------------------------------------------------

        [Test]
        public void AFit_PutsTheWholeRunAcrossTheWidth()
        {
            WaveView view = Fitted(40);

            Assert.IsTrue(view.IsFitted);
            Assert.IsTrue(view.Following, "a fitted view follows");
            Assert.AreEqual(0.0, view.Start);
            Assert.AreEqual(1.0, view.Zoom);
            Assert.AreEqual(40.0, view.Span);
            Assert.AreEqual(40.0, view.End);
            Assert.AreEqual(25.0, view.PixelsPerUnit);
            Assert.AreEqual(0f, view.X(0));
            Assert.AreEqual(1000f, view.X(40), 1e-4f);
            Assert.AreEqual(20.0, view.TimeAt(500f), 1e-9);
            Assert.IsTrue(view.Shows(0) && view.Shows(40) && !view.Shows(40.01));

            view.ZoomAbout(300f, 3);
            view.Pan(2);
            view.Fit(80);
            Assert.IsTrue(view.IsFitted, "fitting again is the whole run again, wherever it had been");
            Assert.AreEqual(0.0, view.Start);
            Assert.AreEqual(12.5, view.PixelsPerUnit);

            view.Fit(0);
            Assert.AreEqual(1.0, view.Span, "a run that ends where it starts still has a unit to show");

            Assert.AreEqual(1.0, new WaveView(500f).Span, "before it is told a run, one unit");
            Assert.Throws<ArgumentOutOfRangeException>(() => new WaveView(0f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new WaveView(float.NaN));
        }

        [Test]
        public void TheWheel_ZoomsInAboutTheCursor_KeepingTheMomentUnderIt()
        {
            WaveView view = Fitted(40);
            double under = view.TimeAt(300f);

            Assert.IsTrue(view.ZoomAbout(300f, 2));
            Assert.AreEqual(1.5625, view.Zoom, 1e-12);
            Assert.AreEqual(under, view.TimeAt(300f), 1e-9, "the moment under the cursor moved");
            Assert.AreEqual(300f, view.X(under), 1e-3f);
            Assert.IsFalse(view.IsFitted);

            under = view.TimeAt(820f);
            Assert.IsTrue(view.ZoomAbout(820f, 5));
            Assert.AreEqual(under, view.TimeAt(820f), 1e-9, "and again, somewhere else, further in");

            under = view.TimeAt(1000f);
            Assert.IsTrue(view.ZoomAbout(5000f, 1));
            Assert.AreEqual(under, view.TimeAt(1000f), 1e-9, "a cursor off the wave area zooms about the nearer edge");

            Assert.IsFalse(view.ZoomAbout(500f, 0), "no notches, no zoom");
        }

        [Test]
        public void ZoomingIn_StopsAt640PixelsAUnit()
        {
            WaveView view = Fitted(ProcessorRun, Strip);

            Assert.IsTrue(view.ZoomAbout(800f, 1000));
            Assert.AreEqual(WaveView.MaxPixelsPerUnit, view.PixelsPerUnit, 1e-9);
            Assert.IsFalse(view.ZoomAbout(800f, 1), "a notch past the limit changes nothing");

            // Never past it, not even by a rounding, whatever the run and the width -- unless the fit is
            // wider already, when the fit is where it stays.
            foreach (long span in new long[] { 3, 40, 999, ProcessorRun, 1_000_000_007 })
            {
                foreach (float width in new[] { 100f, 937.5f, Strip, 2533.3f })
                {
                    double fit = width / (double)span;
                    WaveView stepped = Fitted(span, width);
                    for (int n = 0; n < 200 && stepped.ZoomAbout(width * 0.37f, 1); n++)
                        Assert.LessOrEqual(stepped.PixelsPerUnit, WaveView.MaxPixelsPerUnit, $"{span} across {width}, a notch at a time");

                    if (fit > WaveView.MaxPixelsPerUnit)
                    {
                        Assert.IsTrue(stepped.IsFitted, $"{span} across {width} is wider than the limit at the fit, and zoomed in anyway");
                        Assert.AreEqual(fit, stepped.PixelsPerUnit);
                    }
                    else
                    {
                        Assert.AreEqual(WaveView.MaxPixelsPerUnit, stepped.PixelsPerUnit, 1e-9, $"{span} across {width} stopped short");
                        Assert.LessOrEqual(stepped.PixelsPerUnit, WaveView.MaxPixelsPerUnit);
                    }
                }
            }

            // The cap's zoom is worked out from the width and the span, and multiplied back it can come
            // out a rounding over -- 640.00000000000011 for 4,661 units across 2,407 pixels, before the
            // width was held to it. Across many runs and widths, it never is.
            var random = new Random(640);
            for (int trial = 0; trial < 5000; trial++)
            {
                WaveView capped = Fitted(random.Next(3, 1_000_000), (float)(100 + random.NextDouble() * 2500));
                capped.ZoomAbout(capped.Width / 2, 1000);
                Assert.LessOrEqual(capped.PixelsPerUnit, WaveView.MaxPixelsPerUnit, $"{capped.Span} across {capped.Width}");
            }

            // A run already wider than that at the fit: the fit wins, and the wheel has nothing to zoom into.
            WaveView twoUnits = Fitted(2, Strip);
            Assert.AreEqual(830.0, twoUnits.PixelsPerUnit);
            Assert.AreEqual(1.0, twoUnits.MaxZoom);
            Assert.IsFalse(twoUnits.ZoomAbout(400f, 1));
            Assert.IsTrue(twoUnits.IsFitted);
        }

        [Test]
        public void ZoomingOut_StopsAtTheFit_AndLandsOnItExactly()
        {
            WaveView view = Fitted(ProcessorRun, Strip);
            view.ZoomAbout(1200f, 7);
            view.Pan(-3);
            view.ZoomAbout(100f, -2);
            Assert.IsFalse(view.IsFitted);

            Assert.IsTrue(view.ZoomAbout(900f, -5));
            Assert.AreEqual(1.0, view.Zoom, "as many notches out as in is the fit, exactly");
            Assert.AreEqual(0.0, view.Start, "and the whole run, from its start");
            Assert.IsTrue(view.IsFitted);
            Assert.IsFalse(view.ZoomAbout(900f, -1), "and no further");

            // Past 22 notches 1.25 to the power is no longer exact in a double, and in at once and back
            // out a notch at a time comes to a few parts in 10^16 over the fit: only the snap brings
            // it home. In a notch at a time and out at once comes to a hair under, which the floor does.
            for (int notches = 1; notches <= 40; notches++)
            {
                WaveView stepped = Fitted(ProcessorRun, Strip);
                for (int k = 0; k < notches; k++)
                    stepped.ZoomAbout(37f * k % Strip, 1);

                stepped.ZoomAbout(500f, -notches);
                Assert.AreEqual(1.0, stepped.Zoom, $"{notches} in one by one and out together missed the fit");
                Assert.AreEqual(0.0, stepped.Start);

                stepped.ZoomAbout(1100f, notches);
                for (int k = 0; k < notches; k++)
                    stepped.ZoomAbout(53f * k % Strip, -1);

                Assert.AreEqual(1.0, stepped.Zoom, $"{notches} in together and out one by one missed the fit");
                Assert.IsTrue(stepped.IsFitted && stepped.Start == 0.0 && stepped.Following);
            }

            view.ZoomAbout(800f, 3);
            Assert.IsTrue(view.ZoomAbout(800f, -50), "out further than it went in");
            Assert.AreEqual(1.0, view.Zoom);
        }

        [Test]
        public void APan_MovesAnEighthOfTheView_AndStopsAtBothEndsOfTheRun()
        {
            WaveView view = Fitted(1000);
            Assert.IsFalse(view.Pan(1), "a fitted view shows everything: there is nowhere to move it");
            Assert.IsFalse(view.Pan(-1));

            view.ZoomAbout(500f, 4);
            double start = view.Start, shown = view.Shown;

            Assert.IsTrue(view.Pan(1));
            Assert.AreEqual(start + shown / 8, view.Start, 1e-9, "an eighth of the view a notch");
            Assert.IsTrue(view.Pan(-2));
            Assert.AreEqual(start - shown / 8, view.Start, 1e-9, "and back, earlier");

            Assert.IsTrue(view.Pan(-100));
            Assert.AreEqual(0.0, view.Start, "never before the run began");
            Assert.IsFalse(view.Pan(-1));

            Assert.IsTrue(view.Pan(100));
            Assert.AreEqual(1000.0, view.End, 1e-9, "never past its end");
            Assert.IsFalse(view.Pan(1));

            Assert.AreEqual(shown, view.Shown, "moving through time is not a zoom");
            Assert.IsFalse(view.Pan(0));
        }

        // -----------------------------------------------------------------
        // Following the playhead
        // -----------------------------------------------------------------

        /// <summary>A view of a run of 1000 at a pixel a unit, zoomed in at its start: about 107 units across.</summary>
        private static WaveView AtTheStart()
        {
            WaveView view = Fitted(1000);
            view.ZoomAbout(0f, 10);
            Assert.AreEqual(0.0, view.Start);
            return view;
        }

        [Test]
        public void Following_PagesAtNinetyPercent_SoThePlayheadSitsAtTen()
        {
            WaveView view = AtTheStart();
            double shown = view.Shown, t = 0;

            for (; t <= 0.9 * shown; t += 0.5)
                Assert.IsFalse(view.Follow(t), $"paged at {t}, short of 90%");

            Assert.IsTrue(view.Follow(t), "passing 90% turns the page");
            Assert.AreEqual(0.1f * view.Width, view.X(t), 1e-3f, "and puts the playhead at 10%");
            Assert.IsFalse(view.Follow(t + 0.5), "the page turned, the next moment is in it");

            for (; t < 880; t += 0.37)
            {
                view.Follow(t);
                Assert.IsTrue(view.Shows(t), $"lost the playhead at {t}");
                Assert.LessOrEqual(view.X(t), 0.9f * view.Width + 1e-3f, $"the playhead past 90% at {t}, unpaged");
            }

            view.Follow(999);
            Assert.AreEqual(1000.0, view.End, 1e-9, "the last page is held inside the run");
            Assert.IsTrue(view.Shows(999));

            Assert.IsTrue(view.Follow(0), "a restart, behind the left edge, pages back");
            Assert.AreEqual(0.0, view.Start);

            view.Follow(500);
            Assert.AreEqual(0.1f * view.Width, view.X(500), 1e-3f, "a jump ahead lands at 10% too");
            Assert.IsTrue(view.Follow(480), "a step back past the left edge");
            Assert.AreEqual(0.1f * view.Width, view.X(480), 1e-3f, "lands at 10%, as playing forward does");
        }

        [Test]
        public void APanAway_StopsFollowing_AndPanningBackToThePlayheadFollowsAgain()
        {
            WaveView view = AtTheStart();
            double t;
            for (t = 0; t < 300; t += 1)
                view.Follow(t);
            double playhead = t - 1;
            Assert.IsTrue(view.Following);

            Assert.IsTrue(view.Pan(-1));
            Assert.IsTrue(view.Following, "a notch back still has the playhead in view, short of 90%: it follows on");

            Assert.IsTrue(view.Pan(-16));
            Assert.IsFalse(view.Following, "panned two views back, away from the playhead");

            double held = view.Start;
            for (; t < 500; t += 1)
                Assert.IsFalse(view.Follow(t), $"a view panned away paged at {t}");
            playhead = t - 1;
            Assert.AreEqual(held, view.Start, "a view panned away stays where it was put");

            int notches = 0;
            while (!view.Following)
            {
                Assert.IsTrue(view.Pan(1), "ran out of run before reaching the playhead");
                Assert.Less(++notches, 100);
            }

            Assert.IsTrue(view.Shows(playhead), "following again with the playhead out of sight");
            Assert.LessOrEqual(view.X(playhead), 0.9f * view.Width + 1e-3f, "or past where it pages");

            double before = view.Start;
            for (; view.Start == before && t < 1000; t += 1)
                view.Follow(t);
            Assert.Greater(view.Start, before, "brought back, it pages again");
            Assert.AreEqual(0.1f * view.Width, view.X(t - 1), 1e-3f);

            Assert.IsTrue(view.Pan(1));
            Assert.IsFalse(view.Following, "a notch ahead leaves a playhead at 10% behind the left edge");
        }

        [Test]
        public void ARestart_FollowsAgain_FromAViewPannedAway()
        {
            WaveView view = AtTheStart();
            for (double t = 0; t < 300; t += 1)
                view.Follow(t);
            Assert.IsTrue(view.Pan(-16));
            Assert.IsFalse(view.Following, "sanity: panned away from the playhead");

            // Panned to the start already, where a run started again would be in view: it follows, no page.
            Assert.IsTrue(view.Pan(-1000) || view.Start == 0.0);
            Assert.IsFalse(view.Restart(0.0), "the start is in view: nothing to page to");
            Assert.IsTrue(view.Following, "but it follows the new run");

            double held = view.Start;
            for (double t = 0; t < 300; t += 1)
                view.Follow(t);
            Assert.Greater(view.Start, held, "and pages after it as it plays");

            Assert.IsTrue(view.Pan(1000));
            Assert.IsFalse(view.Following, "sanity: panned to the end, away from the playhead");
            Assert.IsTrue(view.Restart(0.0), "a restart out of view pages back to it");
            Assert.AreEqual(0.0, view.Start, "held inside the run, the playhead at its very start");
            Assert.IsTrue(view.Following);

            WaveView fitted = Fitted(1000);
            Assert.IsFalse(fitted.Restart(500.0), "a fitted view shows everything and never moves");
            Assert.IsTrue(fitted.Following);
        }

        [Test]
        public void AZoomAwayFromThePlayhead_StopsFollowing_AndZoomingOutToTheFitFollowsAgain()
        {
            WaveView view = Fitted(1000);
            view.ZoomAbout(0f, 4);
            view.Follow(100);
            Assert.IsTrue(view.Following);

            view.ZoomAbout(100f, 1);
            Assert.IsTrue(view.Following, "a zoom that keeps the playhead in view follows on");

            view.ZoomAbout(990f, 6);
            Assert.IsFalse(view.Shows(100));
            Assert.IsFalse(view.Following, "a zoom about a moment far from the playhead leaves it behind");
            Assert.IsFalse(view.Follow(150));

            view.ZoomAbout(500f, -100);
            Assert.IsTrue(view.IsFitted);
            Assert.IsTrue(view.Following, "all the way out, it follows again");

            Assert.IsFalse(view.Follow(999), "a fitted view shows everything, and never pages");
            Assert.IsFalse(view.Follow(5000));
            Assert.AreEqual(0.0, view.Start);
        }

        // -----------------------------------------------------------------
        // The axis's numbers
        // -----------------------------------------------------------------

        /// <summary>The 1-2-5 ladder, written out as it reads.</summary>
        private static List<double> Ladder()
        {
            var ladder = new List<double> { 0.1, 0.2, 0.5 };
            foreach (string decade in new[] { "1", "10", "100", "1000", "10000", "100000", "1000000", "10000000", "100000000" })
            {
                foreach (char m in "125")
                    ladder.Add(double.Parse(m + decade.Substring(1), CultureInfo.InvariantCulture));
            }

            return ladder;
        }

        [Test]
        public void TickSteps_ClimbOneTwoFive_AndAreNeverCloserThanAsked()
        {
            List<double> ladder = Ladder();

            foreach (float min in new[] { 28f, WaveView.DefaultTickPixels, 90f, 150f })
            {
                WaveView view = Fitted(ProcessorRun, Strip);
                do
                {
                    double step = view.TickStep(min);
                    CollectionAssert.Contains(ladder, step, $"{step} is not on the ladder");
                    Assert.GreaterOrEqual(step * view.PixelsPerUnit, min, $"{step} apart at {view.PixelsPerUnit} a unit is closer than {min}");

                    int k = ladder.IndexOf(step);
                    if (k > 0)
                        Assert.Less(ladder[k - 1] * view.PixelsPerUnit, min, $"{ladder[k - 1]} would have done at {view.PixelsPerUnit} a unit");
                }
                while (view.ZoomAbout(800f, 1));
            }

            Assert.AreEqual(1000.0, Fitted(ProcessorRun, Strip).TickStep(), "the processor fitted: 500 would be 31 pixels apart");
            Assert.AreEqual(5.0, Fitted(40).TickStep(), "25 a unit: 2 would be 50 apart");

            WaveView closest = Fitted(ProcessorRun, Strip);
            closest.ZoomAbout(800f, 1000);
            Assert.AreEqual(0.1, closest.TickStep(), "at 640 a unit, tenths are 64 apart");
            Assert.AreEqual(0.2, closest.TickStep(100f));
            Assert.AreEqual(WaveView.MinTickStep, closest.TickStep(1f), "never finer than a tenth");
            Assert.AreEqual(WaveView.MinTickStep, closest.TickStep(0f));
        }

        [Test]
        public void TheTicksInView_AreCountedFromEdgeToEdge()
        {
            WaveView fitted = Fitted(40);
            Assert.AreEqual((0L, 8L), fitted.TicksInView(5.0), "0 to 40 in fives, both edges included");

            WaveView closest = Fitted(ProcessorRun, Strip);
            closest.ZoomAbout(Strip, 1000);

            foreach (double step in new[] { 0.1, 0.2, 0.5, 1.0 })
            {
                (long first, long last) = closest.TicksInView(step);
                Assert.GreaterOrEqual(first * step, closest.Start - 1e-9, $"a tick of {step} before the view");
                Assert.Less((first - 1) * step, closest.Start, $"a tick of {step} left out at the left");
                Assert.LessOrEqual(last * step, closest.End + 1e-9, $"a tick of {step} after the view");
                Assert.Greater((last + 1) * step, closest.End, $"a tick of {step} left out at the right");
            }

            Assert.AreEqual(271600L, closest.TicksInView(0.1).Last, "the run's last moment, counted in tenths");
            Assert.Throws<ArgumentOutOfRangeException>(() => closest.TicksInView(0));
        }

        // -----------------------------------------------------------------
        // Precision
        // -----------------------------------------------------------------

        [Test]
        public void AMomentLateInTheProcessorsRun_RoundTripsToAHundredthOfAPixel_AtTheClosestZoom()
        {
            WaveView view = Fitted(ProcessorRun, Strip);
            view.ZoomAbout(Strip, 1000);

            Assert.AreEqual(WaveView.MaxPixelsPerUnit, view.PixelsPerUnit, 1e-9);
            Assert.IsTrue(view.Shows(ProcessorRun));
            Assert.AreEqual(Strip, view.X(ProcessorRun), 0.01f, "the run's last moment, at the right edge");

            for (float x = 0f; x <= Strip; x += 0.37f)
                Assert.AreEqual(x, view.X(view.TimeAt(x)), 0.01f, $"{x} pixels in, at {view.TimeAt(x)}");

            Assert.AreEqual(640f, view.X(27160) - view.X(27159), 0.01f, "a unit late in the run is as wide as one at its start");
            Assert.AreEqual(27159.7, view.TimeAt(view.X(27159.7)), 1e-4);

            // What it would be in floats: 27,159.7 held to 1/512 of a unit, half a pixel at this zoom.
            double t = 27159.7;
            Assert.Greater(Math.Abs((float)t - t) * WaveView.MaxPixelsPerUnit, 0.1, "the test has no teeth: a float would do");
        }

        // -----------------------------------------------------------------
        // Busy spans
        // -----------------------------------------------------------------

        /// <summary>Spans written as they read: "0-10 #0" for an ordinary span, "10-11.2 busy #1-3" for a busy one.</summary>
        private static string[] Read(List<WaveSpan> spans) =>
            spans.Select(s => string.Format(CultureInfo.InvariantCulture, s.Busy ? "{0}-{1} busy #{2}-{3}" : "{0}-{1} #{2}",
                s.From, s.To, s.First, s.Last)).ToArray();

        private static List<WaveSpan> SpansOf(WaveView view, double[] times, double from, double to)
        {
            var spans = new List<WaveSpan>();
            WaveView.Spans(view, i => times[i], times.Length, from, to, spans);
            return spans;
        }

        [Test]
        public void ChangesCloserThanAPixelAndAHalf_AreOneBusySpan()
        {
            WaveView view = Fitted(100, 100f);
            double[] times = { 0, 10, 10.5, 10.9, 11.2, 20, 22 };

            CollectionAssert.AreEqual(
                new[] { "0-10 #0", "10-11.2 busy #1-3", "11.2-20 #4", "20-22 #5", "22-100 #6" },
                Read(SpansOf(view, times, 0, 100)),
                "at a pixel a unit, three changes half a pixel apart");

            CollectionAssert.AreEqual(
                new[] { "10.7-11.2 busy #2-3", "11.2-20 #4", "20-21 #5" },
                Read(SpansOf(view, times, 10.7, 21)),
                "a window that starts inside the busy run, and ends inside a change");

            view.ZoomAbout(50f, 8);
            Assert.IsFalse(SpansOf(view, times, 0, 100).Any(s => s.Busy),
                $"at {view.PixelsPerUnit} a unit, 0.3 apart is far enough apart to draw as they are");

            CollectionAssert.AreEqual(new[] { "5-9 #0" }, Read(SpansOf(Fitted(100, 100f), new double[] { 5, 9.5 }, 0, 9)),
                "before the first change nothing is known, so the spans start at it");
            CollectionAssert.IsEmpty(SpansOf(view, times, 30, 30), "an empty window");
            CollectionAssert.IsEmpty(SpansOf(view, new double[0], 0, 100), "no changes");
            CollectionAssert.IsEmpty(SpansOf(view, new double[] { 50 }, 0, 40), "a window over before the first change");
        }

        [Test]
        public void ChangesTwoPixelsApart_AreNeverMerged()
        {
            for (int notches = 0; notches <= 40; notches += 3)
            {
                WaveView view = Fitted(ProcessorRun, Strip);
                view.ZoomAbout(Strip / 2, notches);

                double gap = 2.0 / view.PixelsPerUnit;
                double[] times = Enumerable.Range(0, 900).Select(i => view.Start + i * gap).ToArray();
                List<WaveSpan> spans = SpansOf(view, times, view.Start, view.End);

                Assert.IsFalse(spans.Any(s => s.Busy), $"changes 2 pixels apart merged at {view.PixelsPerUnit} a unit");
                Assert.AreEqual(spans.Count, spans.Select(s => s.First).Distinct().Count());
            }

            CollectionAssert.AreEqual(new[] { "0-1.5 #0", "1.5-3 #1", "3-10 #2" },
                Read(SpansOf(Fitted(100, 100f), new[] { 0, 1.5, 3 }, 0, 10)),
                "exactly a pixel and a half apart is far enough");
        }

        [Test]
        public void TheSpans_CoverTheWindow_WithoutGapsOrOverlaps()
        {
            var random = new Random(9);
            var spans = new List<WaveSpan>();

            for (int trial = 0; trial < 400; trial++)
            {
                // Settled changes are at whole units, some in bursts, some far apart.
                int count = random.Next(1, 400);
                var times = new double[count];
                long t = random.Next(0, 3);
                for (int i = 0; i < count; i++)
                {
                    times[i] = t;
                    t += random.Next(3) == 0 ? random.Next(1, 4) : random.Next(1, 80);
                }

                WaveView view = Fitted(t + 10, random.Next(200, 2000));
                view.ZoomAbout((float)(random.NextDouble() * view.Width), random.Next(0, 40));
                view.Pan(random.Next(-20, 20));

                double from = random.Next(4) == 0 ? view.Start : random.NextDouble() * (t + 10) - 5;
                double to = random.Next(4) == 0 ? view.End : from + random.NextDouble() * (t + 10) * 0.5;

                spans.Clear();
                WaveView.Spans(view, i => times[i], count, from, to, spans);
                string trialName = $"trial {trial}: {count} changes, {from:0.###} to {to:0.###} at {view.PixelsPerUnit:0.####} a unit";

                double begins = Math.Max(from, times[0]);
                if (!(to > begins))
                {
                    CollectionAssert.IsEmpty(spans, trialName + ": nothing to cover");
                    continue;
                }

                double close = WaveView.BusyPixels / view.PixelsPerUnit;
                double SpacingAfter(int i) => i + 1 < count ? times[i + 1] - times[i] : double.PositiveInfinity;

                Assert.IsNotEmpty(spans, trialName);
                Assert.AreEqual(begins, spans[0].From, trialName + ": the first span starts late or early");
                Assert.AreEqual(Array.FindLastIndex(times, x => x <= begins), spans[0].First, trialName + ": the first span is not the change in force");
                Assert.AreEqual(to, spans[spans.Count - 1].To, trialName + ": the last span stops short or runs on");

                int last = spans[spans.Count - 1].Last;
                Assert.IsTrue(times[last] < to && (last + 1 == count || times[last + 1] >= to), trialName + ": the last span is not the change in force at the end");

                for (int k = 0; k < spans.Count; k++)
                {
                    WaveSpan span = spans[k];
                    Assert.Less(span.From, span.To, $"{trialName}: span {k} is empty or backwards");
                    Assert.LessOrEqual(span.First, span.Last, $"{trialName}: span {k}");

                    if (span.Busy)
                    {
                        for (int i = span.First; i <= span.Last; i++)
                            Assert.Less(SpacingAfter(i), close, $"{trialName}: busy span {k} holds change {i}, which holds long enough to draw");
                    }
                    else
                    {
                        Assert.AreEqual(span.First, span.Last, $"{trialName}: ordinary span {k} holds more than one change");
                        Assert.GreaterOrEqual(SpacingAfter(span.First), close, $"{trialName}: ordinary span {k} is too close to its next");
                    }

                    if (k + 1 < spans.Count)
                    {
                        Assert.AreEqual(span.To, spans[k + 1].From, $"{trialName}: a gap or an overlap after span {k}");
                        Assert.AreEqual(span.Last + 1, spans[k + 1].First, $"{trialName}: a change skipped or repeated after span {k}");
                        Assert.IsFalse(span.Busy && spans[k + 1].Busy, $"{trialName}: two busy spans meet after span {k}");
                    }
                }

                double pixels = (to - begins) * view.PixelsPerUnit;
                Assert.LessOrEqual(spans.Count, 2 * pixels / WaveView.BusyPixels + 5, trialName + ": more spans than the pixels have room for");
            }
        }

        [Test]
        public void ALongRunAtTheFit_CostsSpansByThePixel_NotByTheChange()
        {
            WaveView view = Fitted(ProcessorRun, Strip);
            double[] clock = Enumerable.Range(0, 5432).Select(i => 5.0 * i).ToArray();

            CollectionAssert.AreEqual(new[] { "0-27155 busy #0-5430", "27155-27160 #5431" },
                Read(SpansOf(view, clock, 0, ProcessorRun)),
                "the processor's clock fitted: 5,432 edges, three a pixel, are one block and the last half period");

            view.ZoomAbout(Strip, 1000);
            List<WaveSpan> closest = SpansOf(view, clock, view.Start, ProcessorRun);
            Assert.AreEqual(1, closest.Count, "zoomed in to its end, one change in view");
            Assert.AreEqual((view.Start, (double)ProcessorRun, 5431, 5431, false),
                (closest[0].From, closest[0].To, closest[0].First, closest[0].Last, closest[0].Busy));
        }

        [Test]
        public void CuttingSpans_AllocatesNothing_ButWhatTheListGrowsBy()
        {
            WaveView view = Fitted(ProcessorRun, Strip);
            var random = new Random(4);
            var times = new double[20000];
            for (int i = 1; i < times.Length; i++)
                times[i] = times[i - 1] + (random.Next(5) == 0 ? random.Next(20, 200) : 1);

            Func<int, double> timeOf = i => times[i];
            var spans = new List<WaveSpan>(times.Length);
            WaveView.Spans(view, timeOf, times.Length, 0, ProcessorRun, spans);

            WaveView closer = Fitted(ProcessorRun, Strip);
            closer.ZoomAbout(400f, 12);

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int k = 0; k < 20; k++)
            {
                spans.Clear();
                WaveView.Spans(view, timeOf, times.Length, 0, times[times.Length - 1], spans);
                spans.Clear();
                WaveView.Spans(closer, timeOf, times.Length, closer.Start, closer.End, spans);
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.AreEqual(0, allocated, "cutting spans into a list with room allocated");
            Assert.IsNotEmpty(spans);
        }

        [Test]
        public void CuttingSpans_RefusesWhatItCannotRead()
        {
            WaveView view = Fitted(100);
            var spans = new List<WaveSpan>();

            Assert.Throws<ArgumentNullException>(() => WaveView.Spans(null, i => i, 1, 0, 1, spans));
            Assert.Throws<ArgumentNullException>(() => WaveView.Spans(view, null, 1, 0, 1, spans));
            Assert.Throws<ArgumentNullException>(() => WaveView.Spans(view, i => i, 1, 0, 1, null));

            spans.Add(new WaveSpan { From = -1, To = 0 });
            WaveView.Spans(view, i => 10.0 * i, 3, 0, 100, spans);
            Assert.AreEqual(4, spans.Count, "it adds to the list rather than clearing it");
        }
    }
}
