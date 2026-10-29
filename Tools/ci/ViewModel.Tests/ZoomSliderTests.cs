using NUnit.Framework;

namespace BitSorter.ViewModel.Tests
{
    /// <summary>
    /// The zoom sliders (Part 15h-4d): a scale even in the zoom's ratio, its ends the view's own limits, and the views zoomed
    /// to a value as the wheel zooms them a notch at a time -- about a point that stays put, the fit a stop.
    /// </summary>
    public class ZoomSliderTests
    {
        [Test]
        public void TheScale_IsEvenInTheRatio_AndGoesBothWays()
        {
            Assert.AreEqual(0f, ZoomSlider.ToSlider(0.25, 0.25, 4.0), 1e-6f);
            Assert.AreEqual(1f, ZoomSlider.ToSlider(4.0, 0.25, 4.0), 1e-6f);
            Assert.AreEqual(0.5f, ZoomSlider.ToSlider(1.0, 0.25, 4.0), 1e-6f, "the middle of 1/4 to 4 is 1, not 2.125");
            Assert.AreEqual(0f, ZoomSlider.ToSlider(0.1, 0.25, 4.0), "held at its ends");
            Assert.AreEqual(1f, ZoomSlider.ToSlider(9.0, 0.25, 4.0));
            for (float at = 0f; at <= 1f; at += 0.125f)
                Assert.AreEqual(at, ZoomSlider.ToSlider(ZoomSlider.FromSlider(at, 0.25, 4.0), 0.25, 4.0), 1e-5f);
            Assert.AreEqual(0f, ZoomSlider.ToSlider(1.0, 1.0, 1.0), "a scale of nothing is its start");
        }

        [Test]
        public void TheCircuit_ZoomedToAValue_KeepsThePointWhereItWas_AndHoldsToItsLimits()
        {
            var view = new SchematicView(new Vec2(800f, 600f), new Vec2(0f, 0f), new Vec2(1600f, 1200f));
            view.Fit();
            Vec2 point = new Vec2(200f, 150f);
            Vec2 under = (point - view.Offset) * (1f / view.Zoom);

            Assert.IsTrue(view.ZoomTo(point, view.Zoom * 2f));
            Vec2 after = (point - view.Offset) * (1f / view.Zoom);
            Assert.AreEqual(under.x, after.x, 1e-3f, "the point of the drawing under it stays under it");
            Assert.AreEqual(under.y, after.y, 1e-3f);

            view.ZoomTo(point, 1000f);
            Assert.AreEqual(SchematicView.MaxZoom, view.Zoom, "no further in than the wheel goes");
            view.ZoomTo(point, 1e-6f);
            Assert.AreEqual(view.Floor, view.Zoom, "nor further out");
        }

        [Test]
        public void TheWaves_ZoomedToAValue_StopAtTheFit_AndHoldToTheirLimits()
        {
            var view = new WaveView(600f);
            view.Fit(100);
            double under = view.TimeAt(300f);
            Assert.IsTrue(view.ZoomTo(300f, 3.0));
            Assert.AreEqual(3.0, view.Zoom, 1e-9);
            Assert.AreEqual(under, view.TimeAt(300f), 1e-9, "the moment under the point stays under it");
            Assert.IsFalse(view.Following, "sanity: the playhead at 0 is off the view");
            view.ZoomTo(300f, 1.00001);
            Assert.AreEqual(1.0, view.Zoom, "a hair from the fit is the fit");
            Assert.IsTrue(view.Following, "and follows the run again, as the wheel's fit does");
            view.ZoomTo(300f, 1e9);
            Assert.AreEqual(view.MaxZoom, view.Zoom, 1e-9);
            view.ZoomTo(300f, 1e-9);
            Assert.AreEqual(WaveView.MinZoom, view.Zoom);
        }
    }
}
