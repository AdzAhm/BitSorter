using System.Collections.Generic;
using UnityEngine;

namespace BitSorter.View
{
    /// <summary>
    /// The settings that describe this machine rather than the player's progress: whether the game
    /// is muted, and whether it reports anything.
    /// </summary>
    /// <remarks>
    /// A seam over PlayerPrefs, for exactly the reason <see cref="ProgressStore.Redirected"/> is a
    /// seam over the save file, and arrived at the same way. Play Mode tests load the real scene,
    /// so they reach the real settings -- and the fixtures that needed reporting off were reading
    /// the value, overwriting it, and putting it back in OneTimeTearDown. That makes the player's
    /// own settings depend on a test run finishing cleanly, which is precisely the failure SaveGuard
    /// exists to remove: an interrupted run left the developer's game muted and its analytics off,
    /// with nothing on screen to say why.
    ///
    /// Redirecting removes the class instead of handling it. While <see cref="Redirected"/> is set,
    /// nothing here opens PlayerPrefs at all -- so there is no state to put back, and an interrupted
    /// run leaves the real settings untouched because they were never read.
    ///
    /// Not a general settings system, and not worth becoming one. Two keys, both ints, both
    /// booleans in practice. Progress lives in <see cref="ProgressStore"/>, which is a file, because
    /// it travels with the player; these two do not.
    /// </remarks>
    public static class Preferences
    {
        /// <summary>
        /// Sends every read and write somewhere else. Null in the game.
        /// </summary>
        /// <remarks>
        /// Must be set before anything reads a setting. <see cref="GameAnalytics"/> applies consent
        /// from its AfterSceneLoad bootstrap and <see cref="GameAudio"/> reads the mute in Start, so
        /// a test has to redirect before it loads the scene -- the same requirement the save file's
        /// redirect has, and for the same reason.
        /// </remarks>
        public static Dictionary<string, int> Redirected { get; set; }

        /// <summary>The stored value, or <paramref name="fallback"/> if this machine has none.</summary>
        public static int GetInt(string key, int fallback)
        {
            Dictionary<string, int> scratch = Redirected;

            if (scratch != null)
                return scratch.TryGetValue(key, out int value) ? value : fallback;

            return PlayerPrefs.GetInt(key, fallback);
        }

        /// <summary>
        /// Stores a value, and writes it out.
        /// </summary>
        /// <remarks>
        /// Saved immediately rather than left to Unity's own shutdown write. Both of these change
        /// only on an explicit player action -- reaching for mute, or turning reporting off -- and a
        /// setting lost to a crash is one that appears not to have worked.
        /// </remarks>
        public static void SetInt(string key, int value)
        {
            Dictionary<string, int> scratch = Redirected;

            if (scratch != null)
            {
                scratch[key] = value;
                return;
            }

            PlayerPrefs.SetInt(key, value);
            PlayerPrefs.Save();
        }

        /// <summary>
        /// Stores a value without writing it out, for a setting that changes many times in one
        /// gesture.
        /// </summary>
        /// <remarks>
        /// A volume slider moves through dozens of values in one drag, and <see cref="SetInt"/>
        /// writes the whole of PlayerPrefs out every time -- to the registry on Windows, and to the
        /// browser's storage in a web build. The value is live at once and read back like any
        /// other; <see cref="Save"/> writes it when the gesture ends.
        /// </remarks>
        public static void Stage(string key, int value)
        {
            Dictionary<string, int> scratch = Redirected;

            if (scratch != null)
            {
                scratch[key] = value;
                return;
            }

            PlayerPrefs.SetInt(key, value);
        }

        /// <summary>Writes out anything staged. Nothing to write while redirected.</summary>
        public static void Save()
        {
            if (Redirected == null)
                PlayerPrefs.Save();
        }
    }
}
