using System;
using System.Collections.Generic;

namespace BitSorter.View
{
    /// <summary>
    /// Where the main menu's Continue goes, and what its label promises.
    /// </summary>
    /// <remarks>
    /// Split out from <see cref="MainMenu"/> so the walk can be tested without a canvas, a session or
    /// a progress file, the same way <see cref="PointerRules"/> is split from <see cref="PointerGate"/>.
    ///
    /// One walk, used twice. The label under the buttons names the level and the button opens it, so
    /// if these were computed separately the menu could promise one level and open another.
    /// </remarks>
    public static class MenuRules
    {
        /// <summary>
        /// The first level not yet solved, or the last one when everything is.
        /// </summary>
        /// <remarks>
        /// Furthest unsolved rather than last played, because the two differ exactly when it matters:
        /// a player who wandered into a late level from the list and closed the game should come back
        /// to the run, not to wherever they were browsing.
        ///
        /// Returns false for an empty catalogue rather than picking something. That case used to index
        /// <c>catalogue[Count - 1]</c> unconditionally after the loop found nothing, so a catalogue
        /// that discovered no levels threw <see cref="ArgumentOutOfRangeException"/> -- once per frame,
        /// from the menu's own refresh, on a screen with no way past it.
        /// </remarks>
        public static bool TryNextUp(
            IReadOnlyList<LevelEntry> catalogue, Predicate<string> isComplete, out LevelEntry next)
        {
            next = default;

            if (catalogue == null || catalogue.Count == 0)
                return false;

            foreach (LevelEntry entry in catalogue)
            {
                if (isComplete == null || !isComplete(entry.FileName))
                {
                    next = entry;
                    return true;
                }
            }

            // Everything solved, so the run has no frontier left. The last level is where Continue
            // goes, which keeps the button meaningful rather than dead on a finished save.
            next = catalogue[catalogue.Count - 1];
            return true;
        }

        /// <summary>
        /// What the progress line says.
        /// </summary>
        /// <remarks>
        /// An empty catalogue is a broken install, not a finished run. Reporting it as "0 of 0 solved"
        /// -- or worse, letting the all-solved branch congratulate the player -- would describe levels
        /// that never loaded as an accomplishment.
        /// </remarks>
        public static string DescribeProgress(int solved, int total)
        {
            if (total <= 0)
                return "no levels found";

            return solved == 0 ? $"{total} levels" : $"{solved} of {total} solved";
        }

        /// <summary>Whether every level in the run is solved. False when there are none.</summary>
        public static bool AllSolved(int solved, int total) => total > 0 && solved >= total;
    }
}
