using System;

namespace BitSorter.ViewModel
{
    /// <summary>
    /// The waveform's second cursor. The playhead is the first: the values column and the schematic
    /// follow it, and the second is placed beside it to measure from one to the other.
    /// </summary>
    /// <remarks>
    /// Ahmad's choice (2026-10-16): the playhead is cursor 1, C2 is placed beside it, and Δ is the time
    /// between them -- one moment the whole workbench shows, and one to measure against, where two
    /// cursors beside the playhead would leave the values column to choose between three.
    ///
    /// **A click places C2, and Shift and a click moves the playhead** (Part 15h, Ahmad, 2026-10-28):
    /// the waves are drawn only up to the playhead, so a click that moved it back hid the rest of the
    /// run, where C2 moves through the waves and hides nothing. It was Shift and a click until then.
    /// </remarks>
    public sealed class WaveCursors
    {
        /// <summary>Where C2 is; null while it is not placed.</summary>
        public double? Second { get; private set; }

        /// <summary>Counts every move of C2: what tells the view to draw it again.</summary>
        public int Version { get; private set; }

        /// <summary>Places C2, or moves it, at <paramref name="time"/>, held at or after 0. True when it moved.</summary>
        public bool Place(double time)
        {
            if (double.IsNaN(time) || double.IsInfinity(time))
                throw new ArgumentOutOfRangeException(nameof(time), time, "A cursor is at a moment of the run.");

            double at = Math.Max(0.0, time);
            if (Second == at)
                return false;
            Second = at;
            Version++;
            return true;
        }

        /// <summary>Takes C2 away. True when there was one.</summary>
        public bool Clear()
        {
            if (Second == null)
                return false;
            Second = null;
            Version++;
            return true;
        }

        /// <summary>The time from the playhead to C2, never negative; null while C2 is not placed.</summary>
        public double? Delta(double playhead) => Second.HasValue ? Math.Abs(Second.Value - playhead) : (double?)null;
    }

    /// <summary>Where a click on the waveform lands: on an edge near it, or on a whole time unit.</summary>
    public static class WaveSnap
    {
        /// <summary>How near an edge, in pixels, a click is taken to mean it.</summary>
        public const float SnapPixels = 8f;

        /// <summary>
        /// <paramref name="time"/> as a click there means it: the nearest change of <paramref name="track"/>
        /// within <paramref name="snapPixels"/> at <paramref name="pixelsPerUnit"/> -- the row under the
        /// pointer -- or, with none so near, the nearest whole unit, at or after 0.
        /// </summary>
        /// <remarks>
        /// The run's time is whole units and every edge is at one, so a cursor between two is never
        /// somewhere anything happens; measured from a whole unit, Δ is a whole number of them. A
        /// change near enough wins over the whole unit, so a click a pixel or two off an edge is on it.
        /// </remarks>
        public static double Snap(double time, double pixelsPerUnit, SignalTrack track = null, float snapPixels = SnapPixels)
        {
            if (double.IsNaN(time) || double.IsInfinity(time))
                throw new ArgumentOutOfRangeException(nameof(time), time, "A click is at a moment of the run.");
            if (!(pixelsPerUnit > 0))
                throw new ArgumentOutOfRangeException(nameof(pixelsPerUnit), pixelsPerUnit, "A view lays time out at some pixels a unit.");

            if (track != null && track.Count > 0)
            {
                double reach = snapPixels / pixelsPerUnit;
                int before = track.LastAtOrBefore(time);
                double best = double.NaN, distance = double.MaxValue;

                if (before >= 0)
                {
                    best = track[before].Time;
                    distance = time - best;
                }

                if (before + 1 < track.Count && track[before + 1].Time - time < distance)
                {
                    best = track[before + 1].Time;
                    distance = best - time;
                }

                if (!double.IsNaN(best) && distance <= reach)
                    return best;
            }

            return Math.Max(0.0, Math.Round(time, MidpointRounding.AwayFromZero));
        }
    }
}
