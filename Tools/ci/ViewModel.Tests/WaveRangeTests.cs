using NUnit.Framework;

namespace BitSorter.ViewModel.Tests
{
    /// <summary>
    /// The waves' range bar (Part 15h-4d): the stretch shown on a line for the whole run, an end dragged zooming and the
    /// middle dragged moving through time, every move the waves' own view's.
    /// </summary>
    public class WaveRangeTests
    {
        private static WaveView Fitted()
        {
            var view = new WaveView(600f);
            view.Fit(100);
            return view;
        }

        [Test]
        public void TheStretch_IsWhatTheWavesShow()
        {
            WaveView view = Fitted();
            Assert.AreEqual((0f, 1f), WaveRange.Of(view), "the whole run at the fit");
            Assert.IsTrue(view.ShowRange(20.0, 70.0));
            Assert.AreEqual(2.0, view.Zoom, 1e-9);
            Assert.AreEqual(20.0, view.Start, 1e-9);
            (float from, float to) = WaveRange.Of(view);
            Assert.AreEqual(0.2f, from, 1e-6f);
            Assert.AreEqual(0.7f, to, 1e-6f);
            view.ZoomAbout(0f, -40);
            Assert.AreEqual((0f, 1f), WaveRange.Of(view), "out past the run, the whole line");
        }

        [Test]
        public void AnEndDragged_Zooms_AndTheMiddleDragged_MovesThroughTime()
        {
            WaveView view = Fitted();
            Assert.IsTrue(WaveRange.Drag(view, WaveRange.Grip.End, 0.0, 100.0, -0.5f));
            Assert.AreEqual(2.0, view.Zoom, 1e-9, "its end in to the half: twice in");
            Assert.AreEqual(0.0, view.Start, 1e-9);

            Assert.IsTrue(WaveRange.Drag(view, WaveRange.Grip.Middle, 0.0, 50.0, 0.25f));
            Assert.AreEqual(25.0, view.Start, 1e-9, "the middle a quarter on: a quarter of the run later");
            Assert.AreEqual(2.0, view.Zoom, 1e-9, "at the same zoom");

            WaveRange.Drag(view, WaveRange.Grip.Middle, 25.0, 75.0, 0.9f);
            Assert.AreEqual(50.0, view.Start, 1e-9, "never past the run's end");

            Assert.IsFalse(view.Following, "sanity: the playhead at 0 is off the view");
            Assert.IsTrue(WaveRange.Drag(view, WaveRange.Grip.Start, 50.0, 100.0, -0.5f));
            Assert.AreEqual(1.0, view.Zoom, 1e-9, "its start out to the run's: the whole run, the fit");
            Assert.IsTrue(view.Following);

            WaveRange.Drag(view, WaveRange.Grip.End, 0.0, 100.0, -2f);
            Assert.AreEqual(view.MaxZoom, view.Zoom, 1e-6, "no further in than the waves go");

            view.ZoomAbout(0f, -40);   // stops on the fit, as a notch passing it does
            view.ZoomAbout(0f, -40);   // and on out past the run
            Assert.Less(view.Zoom, 1.0, "sanity: out past the run");
            Assert.IsFalse(WaveRange.Drag(view, WaveRange.Grip.Middle, 0.0, 100.0, 0.01f), "its middle stays, never snapping to the fit");
            Assert.Less(view.Zoom, 1.0);
        }

        [Test]
        public void APress_TakesAnEnd_TheMiddle_OrBringsTheStretchToIt()
        {
            Assert.AreEqual(WaveRange.Grip.Start, WaveRange.At(0.21f, 0.2f, 0.7f, 0.02f));
            Assert.AreEqual(WaveRange.Grip.End, WaveRange.At(0.71f, 0.2f, 0.7f, 0.02f), "just outside an end takes it");
            Assert.AreEqual(WaveRange.Grip.Middle, WaveRange.At(0.45f, 0.2f, 0.7f, 0.02f));
            Assert.AreEqual(WaveRange.Grip.None, WaveRange.At(0.9f, 0.2f, 0.7f, 0.02f));
            Assert.AreEqual(WaveRange.Grip.Middle, WaveRange.At(0.505f, 0.5f, 0.51f, 0.02f), "a narrow stretch is dragged whole");
            Assert.AreEqual(WaveRange.Grip.Middle, WaveRange.At(0.49f, 0.5f, 0.51f, 0.02f), "from within reach of it");
            Assert.AreEqual(WaveRange.Grip.None, WaveRange.At(0.4f, 0.5f, 0.51f, 0.02f));

            WaveView view = Fitted();
            view.ShowRange(0.0, 20.0);
            Assert.IsTrue(WaveRange.Centre(view, 0.5f));
            Assert.AreEqual(40.0, view.Start, 1e-9, "its middle brought to the press");
            WaveRange.Centre(view, 1f);
            Assert.AreEqual(80.0, view.Start, 1e-9, "held inside the run");
        }
    }
}
