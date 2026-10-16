using System;
using System.Collections.Generic;

namespace BitSorter.ViewModel
{
    /// <summary>
    /// One stretch of a row's waveform as it is drawn: one change's value held across it, or -- where
    /// changes come closer together than the drawing can tell apart -- a busy block standing for all of
    /// them. Made by <see cref="WaveView.Spans"/>.
    /// </summary>
    /// <remarks>
    /// <see cref="First"/> and <see cref="Last"/> index the changes the spans were cut from. An ordinary
    /// span is one change, <c>First == Last</c>, whose value holds from <see cref="From"/> to
    /// <see cref="To"/>. A busy one is every change from <see cref="First"/> to <see cref="Last"/>, in
    /// force one after another inside it, so what it showed last is <see cref="Last"/>'s value and the
    /// next span begins with the change after it. Spans meet end to end, so one's <see cref="To"/> is
    /// the next one's <see cref="From"/>, exactly.
    /// </remarks>
    public struct WaveSpan
    {
        public double From, To;
        public int First, Last;
        public bool Busy;
    }

    /// <summary>
    /// The waveform's time axis: which stretch of the run is across the wave area, how wide a time unit
    /// is drawn, and the arithmetic between a moment and a pixel -- fitted to the run, zoomed with the
    /// wheel about the cursor, moved through time, and paged after the playhead.
    /// </summary>
    /// <remarks>
    /// In pixels from the wave area's left edge, and the engine's time units, whatever the document's
    /// unit names them (section 5: "an integer number of time units; the display unit only labels it").
    ///
    /// **4.x's timing strip, ported by its rules and not by its model** (<c>View/Ui/WaveformZoom</c>,
    /// and CLAUDE.md's "It is fitted to the run, and the wheel over it zooms"). 4.x counts whole ticks
    /// in an <c>int</c>, because a tick is the only time its board has. Here a moment sits anywhere
    /// between two units -- the reference circuit plays at 1.6 units a second, so a playhead is part of
    /// the way through one on most frames, and at the closest zoom a unit is 640 pixels wide -- so the
    /// start and every moment are doubles. What carried over: the fit is the whole run, zoom is
    /// measured from it and never goes out past it, a notch of the wheel is 1.25 about the moment under
    /// the cursor, a notch through time is an eighth of the view, a hair from the fit is the fit, the
    /// view follows the run until a gesture takes it elsewhere and follows again once it is brought
    /// back, and the axis's numbers thin out as units narrow.
    ///
    /// **What did not carry over is the fit's own limits.** 4.x fitted a tick between 8 and 160 pixels
    /// wide: its recorder held 128 ticks, so a long run had to scroll, and a short one stopped short of
    /// the strip's end. The history keeps the whole run (ADR-6), and changes too close to draw apart
    /// are drawn as a busy block (<see cref="Spans"/>) rather than refused a fit, so the whole run is
    /// always one fit away and a short run fills the width. The zoomed limit is 4.x's own,
    /// <see cref="MaxPixelsPerUnit"/>, unless the fit is already wider -- a run of two units across 1,660
    /// pixels -- when the fit wins and the wheel has nothing to zoom into: zoom is never under 1.
    ///
    /// **Doubles, and every pixel counted from the left edge.** The corpus processor runs to 27,160. A
    /// float holds that to 1/512 of a unit, which at 640 a unit is 1.25 pixels: a waveform that steps
    /// as it scrolls, and a cursor that lands beside the edge it snapped to. <see cref="X"/> takes the
    /// start away in double before anything becomes a float, so a pixel is as exact late in a long run
    /// as at its start, and a test holds <c>X(TimeAt(x))</c> to a hundredth of a pixel at 27,160.
    ///
    /// **Following** (<see cref="Following"/>) keeps the playhead in view while it plays: when it passes
    /// <see cref="FollowEdge"/> of the view, the view pages so it sits at <see cref="FollowLanding"/>, the
    /// rest of the page ahead of it. A gesture decides whether the view goes on following, and only a
    /// gesture: a pan or a zoom that leaves the playhead where following would keep it -- in view, short
    /// of the paging line -- follows on, one that takes it out of there stops, and panning back to it
    /// follows again. A playhead wandering into a view left elsewhere does not take it back; 4.x's strip
    /// was the same, pinned until the player brought it back to the latest ticks. A fitted view shows
    /// everything, so it never pages and always follows.
    /// </remarks>
    public sealed class WaveView
    {
        /// <summary>How much one notch of the wheel zooms, in or out: 4.x's step, and the schematic's.</summary>
        public const double NotchFactor = 1.25;

        /// <summary>
        /// The widest a time unit is drawn zoomed in: 4.x's widest zoomed tick, four of the widest it
        /// fitted, about two and a half units across a 1,660-pixel strip. Nothing in the engine is
        /// shorter than a unit, so closer than that would show one edge and empty space.
        /// </summary>
        public const double MaxPixelsPerUnit = 640.0;

        /// <summary>
        /// How near the fit a zoom must come to be the fit. In and back out again must land on it
        /// exactly, or floating point leaves the view a hair zoomed and never back at the whole run.
        /// </summary>
        public const double FitSnap = 1e-4;

        /// <summary>How far one notch moves the view through time, as a share of it: 4.x's eighth.</summary>
        public const double PanShare = 0.125;

        /// <summary>Where across the view a playhead being followed turns the page.</summary>
        public const double FollowEdge = 0.9;

        /// <summary>Where across the view a turned page puts the playhead: the rest of the page is ahead of it.</summary>
        public const double FollowLanding = 0.1;

        /// <summary>
        /// The finest step the time axis is numbered in. At the closest zoom a unit is 640 pixels, and
        /// numbered only at whole units the axis would carry two or three numbers; tenths, 64 apart, are
        /// the finest that still leave a number its room.
        /// </summary>
        public const double MinTickStep = 0.1;

        /// <summary>
        /// The least room, in pixels, the axis gives a number by default: about a number as long as
        /// "27,160" and a gap, where 4.x's tick numbers were never more than three digits and needed 28.
        /// </summary>
        public const float DefaultTickPixels = 60f;

        /// <summary>
        /// Changes closer together than this, in pixels, are one busy block: closer than that, two
        /// edges are one smudge however they are drawn, and every one costs a mesh its vertices.
        /// </summary>
        public const double BusyPixels = 1.5;

        /// <summary>What the playhead was last given to <see cref="Follow"/> as: what a gesture keeps in view or leaves.</summary>
        private double _playhead;

        /// <summary>A view of a wave area <paramref name="width"/> pixels wide, fitted to a run of one unit until it is told the run's.</summary>
        public WaveView(float width)
        {
            if (!(width > 0f) || float.IsInfinity(width))
                throw new ArgumentOutOfRangeException(nameof(width), width, "A wave area has some width to show time across.");

            Width = width;
            Fit(1);
        }

        /// <summary>The wave area's width, in pixels.</summary>
        public float Width { get; }

        /// <summary>The time the view is fitted to: the whole run, from 0.</summary>
        public double Span { get; private set; }

        /// <summary>The time at the left edge.</summary>
        public double Start { get; private set; }

        /// <summary>How far it is zoomed in from the fit: 1 is the whole run.</summary>
        public double Zoom { get; private set; }

        /// <summary>How many pixels one time unit takes.</summary>
        public double PixelsPerUnit { get; private set; }

        /// <summary>How much time the view shows across its width: the whole span when fitted.</summary>
        public double Shown => Span / Zoom;

        /// <summary>The time at the right edge.</summary>
        public double End => Start + Shown;

        /// <summary>The furthest the wheel zooms in: to <see cref="MaxPixelsPerUnit"/>, or nowhere if the fit is already wider.</summary>
        public double MaxZoom
        {
            get
            {
                double most = MaxPixelsPerUnit * Span / Width;
                return most > 1.0 ? most : 1.0;
            }
        }

        /// <summary>Whether it shows the whole run.</summary>
        public bool IsFitted => Zoom == 1.0;

        /// <summary>Whether the view pages after the playhead: see the remarks.</summary>
        public bool Following { get; private set; }

        /// <summary>Whether <paramref name="time"/> is in view, edges included.</summary>
        public bool Shows(double time) => time >= Start && time <= End;

        /// <summary>
        /// The whole run across the width: from 0 to <paramref name="span"/>, zoomed out, following.
        /// A span under one unit is one -- a run that ends where it starts still has a unit to show.
        /// </summary>
        public void Fit(long span)
        {
            Span = span < 1 ? 1.0 : span;
            Start = 0.0;
            SetZoom(1.0);
            Following = true;
        }

        /// <summary>
        /// Zooms by whole notches, in for more than zero, keeping the moment under
        /// <paramref name="x"/> under it. Never out past the fit and never in past
        /// <see cref="MaxZoom"/>; all the way out is the fit exactly, following. True when the view moved.
        /// </summary>
        /// <remarks>
        /// A cursor off the wave area zooms about the nearer edge. Near either end of the run the view is
        /// held inside it, so the moment under the cursor can slide as it zooms out: a view that went
        /// past 0 to keep it would show time before the run began.
        /// </remarks>
        public bool ZoomAbout(float x, int notches)
        {
            if (notches == 0)
                return false;

            float at = x > 0f ? (x < Width ? x : Width) : 0f;
            double under = TimeAt(at);

            double zoom = Zoom * Math.Pow(NotchFactor, notches);
            double most = MaxZoom;
            if (zoom > most)
                zoom = most;
            if (zoom < 1.0 + FitSnap)
                zoom = 1.0;

            if (zoom == Zoom)
                return false;

            SetZoom(zoom);

            if (zoom == 1.0)
            {
                // Back out to the whole run there is nothing to hold in view, so it follows again.
                Start = 0.0;
                Following = true;
            }
            else
            {
                Start = Clamp(under - at / PixelsPerUnit, 0.0, Span - Shown);
                Following = KeepsThePlayhead();
            }

            return true;
        }

        /// <summary>
        /// Moves the view through time by whole notches, later for more than zero, an eighth of it a
        /// notch and never past either end of the run. True when it moved.
        /// </summary>
        /// <remarks>
        /// It decides whether the view goes on following: on, if the playhead is where following would
        /// keep it, and off if not -- so panning away stops it and panning back starts it again.
        /// </remarks>
        public bool Pan(int notches)
        {
            if (notches == 0)
                return false;

            double start = Clamp(Start + notches * Shown * PanShare, 0.0, Span - Shown);
            if (start == Start)
                return false;

            Start = start;
            Following = KeepsThePlayhead();
            return true;
        }

        /// <summary>
        /// Tells the view where the playhead is now. While it is <see cref="Following"/>, a playhead
        /// past <see cref="FollowEdge"/> of the view, or behind its left edge -- a restart, a step back
        /// -- pages it so the playhead sits at <see cref="FollowLanding"/>, held inside the run. True when
        /// the view moved.
        /// </summary>
        /// <remarks>
        /// One landing whichever way the playhead left: playing forward is what following is for, and a
        /// page that put a playhead stepped back at 90% would turn forward again on the first frame it
        /// played. A fitted view shows everything, and never moves.
        /// </remarks>
        public bool Follow(double playhead)
        {
            _playhead = playhead;

            if (!Following || IsFitted || KeepsThePlayhead())
                return false;

            double start = Clamp(playhead - FollowLanding * Shown, 0.0, Span - Shown);
            if (start == Start)
                return false;

            Start = start;
            return true;
        }

        /// <summary>
        /// Where <paramref name="time"/> is drawn, in pixels from the left edge: the start taken away in
        /// double first, so a moment late in a long run is placed as exactly as one at its beginning.
        /// </summary>
        public float X(double time) => (float)((time - Start) * PixelsPerUnit);

        /// <summary>The moment under <paramref name="x"/> pixels from the left edge.</summary>
        public double TimeAt(float x) => Start + x / PixelsPerUnit;

        /// <summary>
        /// How far apart the time axis's numbers are: the finest step of the 1-2-5 ladder -- 0.1, 0.2,
        /// 0.5, 1, 2, 5, 10, 20 and on -- whose numbers are at least <paramref name="minPixels"/>
        /// apart, and never finer than <see cref="MinTickStep"/>.
        /// </summary>
        /// <remarks>
        /// 4.x doubled its step from every tick to every eighth, which only ever had to count whole ticks
        /// a few hundred long. A run here is tens of thousands of units at the fit and a few units zoomed
        /// in, and the ladder is the one plots and oscilloscopes number an axis with: each step reads as
        /// a round number, at any zoom. The first decade is written as it reads, so a step is 0.2 and
        /// not twice 0.1's rounding; past it every step is a whole number and exact.
        /// </remarks>
        public double TickStep(float minPixels = DefaultTickPixels)
        {
            if (!(minPixels > 0f))
                return MinTickStep;

            double ppu = PixelsPerUnit;
            if (0.1 * ppu >= minPixels) return 0.1;
            if (0.2 * ppu >= minPixels) return 0.2;
            if (0.5 * ppu >= minPixels) return 0.5;

            double decade = 1.0;
            for (; decade < 1e300; decade *= 10.0)
            {
                if (decade * ppu >= minPixels) return decade;
                if (2.0 * decade * ppu >= minPixels) return 2.0 * decade;
                if (5.0 * decade * ppu >= minPixels) return 5.0 * decade;
            }

            return decade;
        }

        /// <summary>
        /// The numbers of the time axis in view at <paramref name="step"/>, as counts of steps: tick
        /// <c>k</c> is at <c>k * step</c>, from <c>First</c> to <c>Last</c>, edges included. Counted,
        /// not added up, so the hundredth tick of 0.1 is at 10 and not at the sum of a hundred
        /// roundings; and a hair of slack each way, so a tick on an edge is not lost to the division.
        /// </summary>
        public (long First, long Last) TicksInView(double step)
        {
            if (!(step > 0.0))
                throw new ArgumentOutOfRangeException(nameof(step), step, "A step is some time long.");

            long first = (long)Math.Ceiling(Start / step - 1e-9);
            long last = (long)Math.Floor(End / step + 1e-9);
            return (first, last);
        }

        /// <summary>
        /// Cuts a signal's changes into the spans that draw it from <paramref name="from"/> to
        /// <paramref name="to"/> at this view's zoom, and adds them to <paramref name="into"/> (which it
        /// does not clear): an ordinary span for each change that holds at least
        /// <see cref="BusyPixels"/>, and one busy span for every run of changes closer together than
        /// that.
        /// </summary>
        /// <param name="view">The zoom the spans are cut at.</param>
        /// <param name="timeOf">
        /// The time of each change, by index: a signal's settled changes, strictly in order, as a
        /// <see cref="SignalTrack"/> holds them (<c>i =&gt; track[i].Time</c>) or a row's
        /// <see cref="WaveSegment"/>s do (<c>i =&gt; segments[i].From</c>). A function and not a list, so
        /// any of them is read where it lies and nothing is copied. Make it once and keep it: a lambda
        /// that captures a track is an allocation every time it is written.
        /// </param>
        /// <param name="count">How many changes there are.</param>
        /// <param name="from">Where the spans start: the change in force there begins the first one. Before the first change nothing is known, so they start at it.</param>
        /// <param name="to">Where they end: the view's right edge, a tile's, or the moment the run is revealed to.</param>
        /// <param name="into">The list they are added to.</param>
        /// <remarks>
        /// **Why there are busy spans at all.** The processor's clock changes 5,432 times in its 27,160
        /// units; fitted across 1,660 pixels that is three edges a pixel, and drawing each as an edge
        /// costs a mesh the vertices of thousands of marks nobody can tell apart, until a chunk of the
        /// waveform outgrows its mesh. A busy span is one block however many changes it holds, so a
        /// long run at the fit costs vertices by the pixel, not by the change.
        ///
        /// **Decided by how far apart the changes are, never by where the window cuts them.** A change
        /// that holds for 40 units is ordinary even where the window leaves a sliver of it, so a tile's
        /// edge cannot turn a quiet stretch busy, and a busy run cut by a tile is busy on both sides.
        ///
        /// **What it hands on is bounded by the pixel.** Every ordinary span but the two at the window's
        /// ends is at least <see cref="BusyPixels"/> wide, and two busy spans never meet, since a run
        /// ends only at a change that holds -- so there are at most about two spans for every 1.5 pixels.
        /// The walk itself reads each change in the window once, after a binary search for the first:
        /// the processor's clock at the fit is 5,432 reads, a fraction of a millisecond.
        ///
        /// It allocates nothing but what <paramref name="into"/> grows by.
        /// </remarks>
        public static void Spans(WaveView view, Func<int, double> timeOf, int count, double from, double to,
            List<WaveSpan> into)
        {
            if (view == null)
                throw new ArgumentNullException(nameof(view));
            if (timeOf == null)
                throw new ArgumentNullException(nameof(timeOf));
            if (into == null)
                throw new ArgumentNullException(nameof(into));

            if (count <= 0 || !(to > from))
                return;

            // Closer than this in time is closer than BusyPixels on screen.
            double close = BusyPixels / view.PixelsPerUnit;

            int i = LastAtOrBefore(timeOf, count, from);
            double start = from;
            if (i < 0)
            {
                i = 0;
                start = timeOf(0);
                if (!(start < to))
                    return;
            }

            double at = timeOf(i);
            double next = i + 1 < count ? timeOf(i + 1) : double.PositiveInfinity;

            while (true)
            {
                if (!(next - at < close))
                {
                    // Holds long enough to be drawn as it is.
                    if (next >= to)
                    {
                        into.Add(new WaveSpan { From = start, To = to, First = i, Last = i });
                        return;
                    }

                    into.Add(new WaveSpan { From = start, To = next, First = i, Last = i });
                    start = next;
                    i++;
                    at = next;
                    next = i + 1 < count ? timeOf(i + 1) : double.PositiveInfinity;
                    continue;
                }

                // A run of changes too close to tell apart, as far as the first that holds: one block.
                int first = i;
                while (next - at < close)
                {
                    if (next >= to)
                    {
                        into.Add(new WaveSpan { From = start, To = to, First = first, Last = i, Busy = true });
                        return;
                    }

                    i++;
                    at = next;
                    next = i + 1 < count ? timeOf(i + 1) : double.PositiveInfinity;
                }

                // Change i holds: the run was the ones before it, and ends where it begins.
                into.Add(new WaveSpan { From = start, To = at, First = first, Last = i - 1, Busy = true });
                start = at;
            }
        }

        /// <summary>Whether the playhead is where following keeps it: in view, short of the paging line.</summary>
        private bool KeepsThePlayhead() => _playhead >= Start && _playhead <= Start + FollowEdge * Shown;

        /// <summary>
        /// Sets the zoom and the width of a unit with it. Never wider than <see cref="MaxPixelsPerUnit"/>
        /// unless the fit is, not even by a rounding: the cap's zoom is worked out from the width and the
        /// span, and multiplied back it can come out a few parts in 10^16 over.
        /// </summary>
        private void SetZoom(double zoom)
        {
            Zoom = zoom;
            double fit = Width / Span;
            double ppu = Width * zoom / Span;
            double most = fit > MaxPixelsPerUnit ? fit : MaxPixelsPerUnit;
            PixelsPerUnit = ppu > most ? most : ppu;
        }

        /// <summary>The last change at or before <paramref name="time"/>, or -1 when every change is after it.</summary>
        private static int LastAtOrBefore(Func<int, double> timeOf, int count, double time)
        {
            int low = 0, high = count - 1, found = -1;
            while (low <= high)
            {
                int mid = low + ((high - low) >> 1);
                if (timeOf(mid) <= time) { found = mid; low = mid + 1; }
                else high = mid - 1;
            }

            return found;
        }

        private static double Clamp(double value, double min, double max) =>
            value < min ? min : value > max ? max : value;
    }
}
