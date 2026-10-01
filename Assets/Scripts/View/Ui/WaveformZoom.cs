using UnityEngine;

namespace BitSorter.View
{
    /// <summary>
    /// How wide the timing diagram draws a tick, and which ticks it shows: the level's run fitted
    /// across the strip, zoomed and moved with the wheel over it.
    /// </summary>
    /// <remarks>
    /// Pure arithmetic, so the rules are testable without a canvas, as <see cref="CameraFraming"/>'s
    /// are.
    ///
    /// **A tick used to be a fixed 24 units**, about seventy across the strip at 1080: a half
    /// adder's run of eight ticks was a sliver at the left of an empty strip, and a long sequential
    /// run scrolled its own start away. Ahmad asked for the strip to fit the run, and to zoom in and
    /// back out (2026-10-01).
    ///
    /// **Fitted, the strip holds the whole run**: the run the level is expected to take, or as much
    /// as has been recorded if that is longer, so a run that outlasts the estimate narrows its ticks
    /// as it goes rather than scrolling. Past <see cref="WaveformRecorder.Capacity"/> it scrolls,
    /// because older ticks are no longer held.
    ///
    /// **Zoom is measured from the fit**, so zooming back out stops at the whole run and there is no
    /// separate way back to it.
    /// </remarks>
    public static class WaveformZoom
    {
        /// <summary>The narrowest tick: a step, a dash and a cross still read.</summary>
        public const float MinTickWidth = 8f;

        /// <summary>The widest tick, zoomed in or fitted to a short run.</summary>
        public const float MaxTickWidth = 160f;

        /// <summary>How much one notch of the wheel zooms in or out.</summary>
        public const float Step = 1.25f;

        /// <summary>The least room a tick number is given, so neighbouring numbers never touch.</summary>
        public const float LabelSpacing = 28f;

        /// <summary>The most ticks between two numbers, however narrow the ticks.</summary>
        public const int MaxNumberStep = 8;

        /// <summary>
        /// How many ticks a run on a level is expected to take: its vectors a clock period apart,
        /// and room after the last for the circuit to carry it to the bins.
        /// </summary>
        /// <remarks>
        /// The room is half the board's width in ticks. A path from a source to a bin crosses the
        /// board, each wire costs at least a tick, and most circuits here are two to four gates deep.
        /// An estimate only: a run that takes longer narrows the ticks as it goes
        /// (<see cref="FitTicks"/>).
        /// </remarks>
        public static int ExpectedRun(int vectors, int clockPeriod, int boardHalfWidth)
        {
            int period = clockPeriod < 1 ? 1 : clockPeriod;
            int streams = vectors < 1 ? 1 : (vectors - 1) * period + 1;
            return streams + (boardHalfWidth < 1 ? 1 : boardHalfWidth);
        }

        /// <summary>
        /// How many ticks the strip fits across: the expected run, or the whole run recorded so far
        /// if that is longer, and never more than the recorder holds.
        /// </summary>
        public static int FitTicks(int expected, int lastTick, int capacity)
        {
            int ticks = expected > lastTick + 1 ? expected : lastTick + 1;

            if (ticks > capacity)
                ticks = capacity;

            return ticks < 1 ? 1 : ticks;
        }

        /// <summary>The tick width that fits this many ticks across this much room.</summary>
        public static float FitWidth(float room, int ticks) =>
            Mathf.Clamp(room / (ticks < 1 ? 1 : ticks), MinTickWidth, MaxTickWidth);

        /// <summary>The tick width at a zoom over the fit.</summary>
        public static float TickWidth(float fitWidth, float zoom) =>
            Mathf.Clamp(fitWidth * (zoom < 1f ? 1f : zoom), MinTickWidth, MaxTickWidth);

        /// <summary>How many whole ticks fit across.</summary>
        /// <remarks>
        /// A hair of slack, so a width worked out as room divided by twelve fits twelve and not
        /// eleven when the division rounds down.
        /// </remarks>
        public static int Visible(float room, float tickWidth) =>
            room <= 0f || tickWidth <= 0f ? 0 : Mathf.FloorToInt(room / tickWidth + 1e-3f);

        /// <summary>
        /// The zoom after this many notches of the wheel: never under the fit, and never past the
        /// widest tick.
        /// </summary>
        public static float Zoomed(float zoom, int notches, float fitWidth)
        {
            float most = fitWidth > 0f ? MaxTickWidth / fitWidth : 1f;

            if (most < 1f)
                most = 1f;

            float next = zoom * Mathf.Pow(Step, notches);

            // Within a hair of the fit is the fit: in and back out again must land on it exactly, or
            // floating point leaves the view a millionth zoomed and never back at the whole run.
            if (next < 1f + 1e-3f)
                next = 1f;

            return Mathf.Clamp(next, 1f, most);
        }

        /// <summary>
        /// The first tick in view after a zoom, keeping the tick under the cursor under it.
        /// </summary>
        /// <param name="start">The first tick in view before.</param>
        /// <param name="cursor">How far into the ticks the cursor is, in canvas units.</param>
        /// <param name="widthBefore">The tick width before the zoom.</param>
        /// <param name="widthAfter">The tick width after it.</param>
        public static int StartAfterZoom(int start, float cursor, float widthBefore, float widthAfter)
        {
            if (widthBefore <= 0f || widthAfter <= 0f)
                return start;

            float under = start + cursor / widthBefore;
            return Mathf.RoundToInt(under - cursor / widthAfter);
        }

        /// <summary>
        /// The first tick in view, held between the oldest tick recorded and the start that shows
        /// the latest.
        /// </summary>
        public static int ClampStart(int start, int firstHeld, int following)
        {
            int latest = following > firstHeld ? following : firstHeld;
            return start < firstHeld ? firstHeld : start > latest ? latest : start;
        }

        /// <summary>How many ticks one notch moves the view through time: an eighth of it, at least one.</summary>
        public static int PanTicks(int visible) => visible / 8 > 1 ? visible / 8 : 1;

        /// <summary>
        /// How many ticks apart the numbers above the strip are, and the faint marks under them:
        /// every tick while ticks are wide, every second, fourth or eighth as they narrow.
        /// </summary>
        public static int NumberStep(float tickWidth)
        {
            int step = 1;

            while (step < MaxNumberStep && step * tickWidth < LabelSpacing)
                step *= 2;

            return step;
        }
    }
}
