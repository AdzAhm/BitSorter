namespace BitSorter.View
{
    /// <summary>
    /// Whether the player has asked for less motion: the workbench then leaves out the glow that
    /// trails a change on its wire, the halo round it and a gate's flash as its output flips.
    /// </summary>
    /// <remarks>
    /// What is left is what carries the meaning: a change still travels its wire as a ring holding
    /// its digit, because where a change has got to is the thing the workbench shows, and the run
    /// only moves when the player plays it. Read where it is drawn, every frame, so switching it in
    /// Settings needs no reload. The 4.x board is not changed by it; it is retired in Part 15.
    /// </remarks>
    public static class ReducedMotion
    {
        /// <summary>Where it is kept, beside the other settings.</summary>
        public const string Key = "bitsorter.reducedMotion";

        /// <summary>Whether motion is reduced. Off unless asked for.</summary>
        public static bool IsOn => Preferences.GetInt(Key, 0) != 0;

        /// <summary>Asks for less motion, or for all of it back.</summary>
        public static void Set(bool on) => Preferences.SetInt(Key, on ? 1 : 0);
    }
}
