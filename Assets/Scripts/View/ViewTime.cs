using UnityEngine;

namespace BitSorter.View
{
    /// <summary>
    /// The clock the board's ambient animations keep time to.
    /// </summary>
    /// <remarks>
    /// Some animations follow the game clock on purpose, because they have to agree with each
    /// other: a collision one tick away throbs on its port, its wire and its bit at once, stalled
    /// gates breathe together, the grid shimmers as one field. Those read the time from here. An
    /// animation that marks an event -- a win, a scorch, a wire just re-timed -- counts from its own
    /// start instead, and does not.
    ///
    /// <see cref="Pinned"/> exists for the reference screenshots. Every other state in them is
    /// fixed, but these pulses followed whatever the clock had reached by the time a scene finished
    /// loading, which varies run to run: the grid swelled by a pixel here and there, and a diff of
    /// two captures of identical code flagged three thousand pixels on the plainest board. Pinned,
    /// every capture shows the same moment of every pulse. The game never sets it.
    /// </remarks>
    public static class ViewTime
    {
        /// <summary>When set, every ambient animation shows this moment instead of the game clock.</summary>
        public static float? Pinned { get; set; }

        /// <summary>The moment ambient animation is drawn at.</summary>
        public static float Now => Pinned ?? Time.time;
    }
}
