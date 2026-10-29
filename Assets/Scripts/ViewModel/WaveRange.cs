using System;

namespace BitSorter.ViewModel
{
    /// <summary>
    /// The waves' range bar (Part 15h-4d, Ahmad's drawing, 2026-10-29: "a line that you can drag its edges to zoom/unzoom
    /// and move it to move through the wave"): a line for the whole run, and on it the stretch the waves show. An end of
    /// the stretch dragged zooms -- the stretch made shorter or longer -- and its middle dragged moves through time; a press
    /// elsewhere on the line brings the stretch's middle there. Never a view of its own: it reads <see cref="WaveView"/>
    /// and moves it (<see cref="WaveView.ShowRange"/>), so the wheel and the keys move the stretch too.
    /// </summary>
    public static class WaveRange
    {
        /// <summary>What a press on the bar takes.</summary>
        public enum Grip { None, Start, End, Middle }

        /// <summary>The stretch shown, as fractions of the run from 0 to 1: the whole line while the view is out past the run.</summary>
        public static (float From, float To) Of(WaveView view)
        {
            if (view == null || !(view.Span > 0.0))
                return (0f, 1f);
            return (Fraction(view.Start / view.Span), Fraction(view.End / view.Span));
        }

        /// <summary>
        /// What a press at <paramref name="at"/> (0 to 1 along the bar) takes, with <paramref name="reach"/> the share of the
        /// bar either side of an end that takes it: an end, the stretch between them, or nothing. A stretch too narrow to
        /// hold its ends and a middle apart is dragged whole from anywhere within reach of it: zoomed far in, it is the
        /// view to be nudged, and its ends a pixel's drag from a jump through the run (the review of the zoom controls).
        /// </summary>
        public static Grip At(float at, float from, float to, float reach)
        {
            bool inside = at >= from && at <= to;
            if (to - from < 3f * reach)
                return at >= from - reach && at <= to + reach ? Grip.Middle : Grip.None;
            if (inside)
            {
                if (at - from <= reach)
                    return Grip.Start;
                if (to - at <= reach)
                    return Grip.End;
                return Grip.Middle;
            }
            if (at < from && from - at <= reach)
                return Grip.Start;
            if (at > to && at - to <= reach)
                return Grip.End;
            return Grip.None;
        }

        /// <summary>
        /// The stretch the press began on, <paramref name="from"/> to <paramref name="to"/> in the run's own units, dragged by
        /// <paramref name="by"/> -- a share of the bar -- through <paramref name="grip"/>: an end moved and the other kept,
        /// never closer than the most the waves zoom in, never past the run; the middle moved whole, never past either end
        /// of the run. True when the view moved.
        /// </summary>
        public static bool Drag(WaveView view, Grip grip, double from, double to, float by)
        {
            if (view == null)
                return false;
            double span = view.Span, least = span / view.MaxZoom, d = by * span;
            switch (grip)
            {
                case Grip.Middle:
                    if (view.Zoom < 1.0)
                        return false;   // out past the run, the stretch is the whole line: nothing to move, and no snap to the fit
                    d = Math.Max(-from, Math.Min(span - to, d));
                    return view.ShowRange(from + d, to + d);
                case Grip.Start:
                    return view.ShowRange(Math.Max(0.0, Math.Min(to - least, from + d)), to);
                case Grip.End:
                    return view.ShowRange(from, Math.Min(span, Math.Max(from + least, to + d)));
                default:
                    return false;
            }
        }

        /// <summary>The stretch, as long as it is, with its middle brought to <paramref name="at"/> (0 to 1 along the bar), inside the run.</summary>
        public static bool Centre(WaveView view, float at)
        {
            if (view == null)
                return false;
            double span = view.Span, shown = Math.Min(view.Shown, span);
            double from = Math.Max(0.0, Math.Min(span - shown, at * span - shown * 0.5));
            return view.ShowRange(from, from + shown);
        }

        private static float Fraction(double x) => (float)(x < 0.0 ? 0.0 : x > 1.0 ? 1.0 : x);
    }
}
