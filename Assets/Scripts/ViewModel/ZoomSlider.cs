using System;

namespace BitSorter.ViewModel
{
    /// <summary>
    /// A zoom slider's scale (Part 15h-4d, asked 2026-10-29): from 0 at the furthest out to 1 at the furthest in, even in the
    /// zoom's ratio rather than its value -- a stretch of the slider multiplies the zoom by the same anywhere -- so the
    /// slider moves as the wheel does, a notch the same distance wherever it is.
    /// </summary>
    public static class ZoomSlider
    {
        /// <summary>Where <paramref name="zoom"/> stands between <paramref name="low"/> and <paramref name="high"/>, from 0 to 1.</summary>
        public static float ToSlider(double zoom, double low, double high)
        {
            if (!(low > 0.0) || !(high > low) || !(zoom > 0.0))
                return 0f;
            double at = Math.Log(zoom / low) / Math.Log(high / low);
            return (float)(at < 0.0 ? 0.0 : at > 1.0 ? 1.0 : at);
        }

        /// <summary>The zoom at <paramref name="at"/>, from 0 to 1, between <paramref name="low"/> and <paramref name="high"/>.</summary>
        public static double FromSlider(float at, double low, double high)
        {
            if (!(low > 0.0) || !(high > low))
                return low;
            double t = at < 0f ? 0.0 : at > 1f ? 1.0 : at;
            return low * Math.Pow(high / low, t);
        }
    }
}
