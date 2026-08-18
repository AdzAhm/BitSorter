namespace BitSorter.View
{
    /// <summary>
    /// What the corruption counter says, and when it says anything at all.
    /// </summary>
    /// <remarks>
    /// Split out from the component so the wording and the appear/disappear rule can be tested
    /// without a canvas, the same way <see cref="PointerRules"/> is split from
    /// <see cref="PointerGate"/>.
    ///
    /// This is not a debug readout, and the distinction drives every decision here. Watching the
    /// number climb 2, 4, 6, 8 during a run is how a player diagnoses the timing failure in
    /// balance-the-paths -- if it only appeared in the end-of-run verdict, that level would stop
    /// teaching. So it reads as prose rather than as a stat, and it is absent rather than zero.
    /// </remarks>
    public static class BitsLostReadout
    {
        /// <summary>
        /// Whether the counter should be on screen at all.
        /// </summary>
        /// <remarks>
        /// Nothing until the first bit dies. A permanent "0 bits lost" would be a debug stat the eye
        /// learns to ignore, which is exactly what this must not become -- the whole point is that
        /// its appearance is an event.
        /// </remarks>
        public static bool IsVisible(int destroyed) => destroyed > 0;

        /// <summary>
        /// The counter's text.
        /// </summary>
        /// <remarks>
        /// Counts bits, not collisions, matching what CorruptedCount means: a mixed-value collision
        /// destroys two bits and adds two. That cadence -- 2, 4, 6, 8 rather than 1, 2, 3, 4 -- is
        /// itself the diagnostic, because it tells the player the arrivals disagreed.
        /// </remarks>
        public static string Describe(int destroyed)
        {
            if (destroyed <= 0)
                return string.Empty;

            return destroyed == 1 ? "1 BIT LOST" : $"{destroyed} BITS LOST";
        }

        /// <summary>Whether the count rose since it was last seen, which is what triggers the punch.</summary>
        public static bool Rose(int previous, int current) => current > previous;
    }
}
