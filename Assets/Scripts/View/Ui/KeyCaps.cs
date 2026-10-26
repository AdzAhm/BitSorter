using System;

namespace BitSorter.View
{
    /// <summary>
    /// Whether the keys drawn beside the buttons are shown: the caps over the run buttons and the workbench's, the keys
    /// under the help badge, MENU and the timing diagram's badge, and Q and E beside the level's counter. SHORTCUT KEYS in
    /// Settings switches them; on to begin with (asked for 2026-10-26, the whole game, shown by default).
    /// </summary>
    /// <remarks>
    /// **Only where a key stands beside what it works.** The places that exist to list keys keep them: the workbench's
    /// keyboard map (<c>?</c>), the tutorial's card and the help panel's text. Every key still works with them hidden.
    /// **It changes what is on screen at once**: a key is told when the setting changes (<see cref="Changed"/>) rather than
    /// built again, so nothing needs a reload.
    /// </remarks>
    public static class KeyCaps
    {
        /// <summary>The setting it is kept under: 1 shown, 0 hidden.</summary>
        public const string PreferenceKey = "keys.shown";

        /// <summary>Whether the keys beside the buttons are shown.</summary>
        public static bool Shown => Preferences.GetInt(PreferenceKey, 1) != 0;

        /// <summary>Raised when the setting changes, for every key on screen to show or hide.</summary>
        public static event Action Changed;

        /// <summary>Shows the keys beside the buttons, or hides them, and keeps the choice.</summary>
        public static void SetShown(bool shown)
        {
            if (shown == Shown)
                return;
            Preferences.SetInt(PreferenceKey, shown ? 1 : 0);
            Changed?.Invoke();
        }
    }
}
