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
        /// <summary>An exercise was opened. Named for the 4.x game's levels, which the first thirty exercises are.</summary>
        public const string LevelStarted = "levelStarted";

        /// <summary>An exercise was solved.</summary>
        public const string LevelSolved = "levelSolved";

        /// <summary>The only parameter either event carries: the exercise's id, a 4.x level's file name for the thirty.</summary>
        public const string LevelNameParameter = "levelName";

        /// <summary>Every event the game itself sends. Unity's SDK sends its own; those are not ours.</summary>
        public static IReadOnlyList<string> Events { get; } = new[] { LevelStarted, LevelSolved };

        /// <summary>
        /// Whether an event about <paramref name="levelName"/> may be reported at all.
        /// </summary>
        /// <param name="levelName">An exercise's id, or a name that is none.</param>
        /// <param name="reporting">The player's choice, from the DATA switch in Settings.</param>
        /// <param name="unavailable">Initialisation failed, so there is nowhere to send anything.</param>
        /// <param name="alreadySentThisSession">This event, for this level, has gone once already.</param>
        /// <remarks>
        /// Consent is checked here as well as at the consent framework, so a player who has turned
        /// reporting off does not even accumulate a queue of events waiting for an upload that must
        /// not happen.
        ///
        /// **Only an exercise is reported** (<see cref="ExerciseCatalog"/>): the question these two events exist to answer
        /// is which exercise people stop at, and a name that is none would answer it wrongly. In the 4.x game free play and
        /// its tutorial were what this kept out: free play could never be solved, so a start from it looked like someone
        /// giving up, and the tutorial was solved by everybody who finished it.
        ///
        /// **Once per exercise per session, for each event.** Without this a student who reopens a hard exercise four times
        /// before giving up reports four starts and no solve, and reads as four people who quit. The bias is not random:
        /// exercises get reopened in proportion to how hard they are, so the apparent drop-off would be inflated most at
        /// exactly the ones this measurement exists to find. Counting each once a session makes starts "reached it",
        /// solves "beat it", and the difference "stopped here", which is the question.
        ///
        /// Solves are deduplicated too, and must be: a student who solved one twice in one session
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

            return !string.IsNullOrEmpty(levelName) && IsExercise(levelName);
        }

        private static bool IsExercise(string id)
        {
            foreach (BitSorter.Board.Exercise exercise in ExerciseCatalog.Run)
                if (exercise.Id == id)
                    return true;
            return false;
        }

        /// <summary>The key an event is remembered under, so each pair is counted once.</summary>
        public static string SessionKey(string eventName, string levelName) =>
            eventName + "|" + levelName;
    }
}
