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
        /// <param name="alreadySentThisSession">This event, for this level, has gone once already.</param>
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
        ///
        /// **Once per level per session, for each event.** Re-entering a level from the level list
        /// fires `LevelLoaded` again, so without this a player who reopens a hard level four times
        /// before giving up reports four starts and no solve, and reads as four people who quit. The
        /// bias is not random: levels get reopened in proportion to how hard they are, so the
        /// apparent drop-off was inflated most at exactly the levels this measurement exists to find.
        ///
        /// The old number was not merely noisy, it was incoherent. `LevelLoaded` fires on opening a
        /// level but not on RESET, so it counted neither attempts nor players -- it counted which key
        /// somebody happened to retry with. Counting each level once a session makes starts "reached
        /// it", solves "beat it", and the difference "stopped here", which is the question.
        ///
        /// Solves are deduplicated too, and must be: a player who solved a level twice in one session
        /// against a start counted once would report more solves than starts.
        ///
        /// This changes when an event fires, never what is collected. Both events, and the single
        /// `levelName` parameter, are exactly as they were.
        /// </remarks>
        public static bool ShouldReport(string levelName, bool reporting, bool unavailable,
                                        bool alreadySentThisSession = false)
        {
            if (unavailable || !reporting || alreadySentThisSession)
                return false;

            if (string.IsNullOrEmpty(levelName))
                return false;

            return !LevelCatalog.IsOffCatalogue(levelName);
        }

        /// <summary>The key an event is remembered under, so each pair is counted once.</summary>
        public static string SessionKey(string eventName, string levelName) =>
            eventName + "|" + levelName;
    }
}
