using System.Collections.Generic;

namespace BitSorter.View
{
    /// <summary>
    /// What analytics reports, and when it stays quiet.
    /// </summary>
    /// <remarks>
    /// Split out of <see cref="GameAnalytics"/> so the one thing in this game that sends data
    /// anywhere can be tested without a scene, a network or a linked Unity project. The bootstrap
    /// there is all consent, async initialisation and SDK calls; this is the part with rules in it.
    ///
    /// CLAUDE.md says adding a third event, or a new parameter, is a change to what players were
    /// told is collected and has to be asked about first. <see cref="Events"/> being a list with a
    /// test around it is what makes that promise something a build can check rather than something
    /// a reviewer has to notice.
    /// </remarks>
    public static class AnalyticsRules
    {
        /// <summary>A level was opened.</summary>
        public const string LevelStarted = "levelStarted";

        /// <summary>A level was solved.</summary>
        public const string LevelSolved = "levelSolved";

        /// <summary>The only parameter either event carries: the level's file name.</summary>
        public const string LevelNameParameter = "levelName";

        /// <summary>Every event the game itself sends. Unity's SDK sends its own; those are not ours.</summary>
        public static IReadOnlyList<string> Events { get; } = new[] { LevelStarted, LevelSolved };

        /// <summary>
        /// Whether an event about <paramref name="levelName"/> may be reported at all.
        /// </summary>
        /// <param name="levelName">The level's file name, or a key that is not a level.</param>
        /// <param name="reporting">The player's choice, from the main menu's Data item.</param>
        /// <param name="unavailable">Initialisation failed, so there is nowhere to send anything.</param>
        /// <remarks>
        /// Consent is checked here as well as at the consent framework, so a player who has turned
        /// reporting off does not even accumulate a queue of events waiting for an upload that must
        /// not happen.
        ///
        /// Free play and the tutorial are refused because neither is a level in the run, and the
        /// question these two events exist to answer is which level people stop at. Free play can
        /// never be solved, so a start from it would look exactly like someone giving up; the
        /// tutorial is solved by everybody who finishes it, so it would look like a level nobody
        /// ever stops at. Both would answer the question wrongly, in opposite directions.
        /// </remarks>
        public static bool ShouldReport(string levelName, bool reporting, bool unavailable)
        {
            if (unavailable || !reporting)
                return false;

            if (string.IsNullOrEmpty(levelName))
                return false;

            return !LevelCatalog.IsOffCatalogue(levelName);
        }
    }
}
