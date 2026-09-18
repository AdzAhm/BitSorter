using System;

namespace BitSorter.View
{
    /// <summary>
    /// When the background track changes, and which one comes next.
    /// </summary>
    /// <remarks>
    /// Pure, so the answer can be tested without a scene, an AudioSource or a frame loop -- the same
    /// reason <see cref="HintRules"/> and <see cref="MenuRules"/> exist. <see cref="GameAudio"/> is
    /// left with the part that genuinely needs Unity: the fade and the clip swap.
    ///
    /// **The track changes only when the level does.** Three things follow from that, and each one
    /// is a way this could have gone wrong:
    ///
    /// It never switches mid-level, so a track cannot change under a player halfway through working
    /// something out. There is no timer here at all -- a level load is the only event that moves it.
    ///
    /// It never switches on a retry. Reset and re-entering the same level both re-fire
    /// <c>LevelLoaded</c>, so cycling on every load would change the music each time a player failed
    /// a level -- drawing attention to exactly the level they are already stuck on.
    ///
    /// It never switches immediately after boot. The first level loads during <c>Start</c>, a moment
    /// after the music began, and cycling there would cross-fade the opening a second after the
    /// player first heard it.
    ///
    /// Cycling rather than shuffling: a session works through every track whatever order the levels are
    /// played in, and a test can say which one should be playing. The one thing that is random is
    /// where in the cycle a session starts, which is picked once in <see cref="GameAudio"/>'s Awake
    /// -- enough that two evenings on the same levels are not the same evening, while everything
    /// after it stays deterministic and assertable.
    /// </remarks>
    public static class MusicRules
    {
        /// <summary>
        /// Whether a level load should move the music on.
        /// </summary>
        /// <param name="playingUnder">Level the current track was chosen for, or null at boot.</param>
        /// <param name="loading">Level being loaded now.</param>
        public static bool ChangesTrack(string playingUnder, string loading)
        {
            // Nothing has played under a level yet. The track picked at startup belongs to the first
            // level rather than to the menu, so the first load adopts it instead of replacing it.
            if (string.IsNullOrEmpty(playingUnder))
                return false;

            // A retry, a reset, or stepping back into the level already on screen.
            return !string.Equals(playingUnder, loading, StringComparison.Ordinal);
        }

        /// <summary>The track after this one, wrapping at the end.</summary>
        /// <remarks>
        /// Tolerates a nonsense current index rather than throwing. A bad index here would silence
        /// the music, and music failing loudly is worse than music failing quietly.
        /// </remarks>
        public static int NextTrack(int current, int count)
        {
            if (count <= 0)
                return 0;

            return (int)(((long)current + 1) % count + count) % count;
        }
    }
}
