using UnityEngine;

namespace BitSorter.View
{
    /// <summary>The two frame rates Settings offers.</summary>
    public enum FrameRateChoice
    {
        /// <summary>As fast as the screen refreshes: the game as it ships.</summary>
        Screen = 0,

        /// <summary>Held at <see cref="FrameRate.Held"/>, to use less power.</summary>
        Sixty = 1,
    }

    /// <summary>
    /// The frame-rate setting: what each choice does to the player, and remembering it.
    /// </summary>
    /// <remarks>
    /// Asked for after a question about a 165 Hz laptop (2026-09-28). The game already ran at the
    /// screen's own rate -- vertical sync on, no cap -- so SCREEN is exactly what shipped, and the
    /// one thing to add was a way down: a board that mostly sits still needs nothing like 165
    /// frames a second, and a laptop on battery pays for every one. A raw number to type in was
    /// not offered; nothing in the game runs better or worse for it, since the simulation keeps its
    /// own clock and every animation counts time rather than frames.
    ///
    /// Desktop only, like fullscreen (<see cref="DisplayRules.Offered"/>): in a browser the page
    /// decides how often the game is drawn.
    /// </remarks>
    public static class FrameRate
    {
        /// <summary>What <see cref="FrameRateChoice.Sixty"/> holds the game to.</summary>
        public const int Held = 60;

        /// <summary>Where the choice is kept, beside the sound settings.</summary>
        public const string PrefKey = "bitsorter.frameRate";

        /// <summary>The stored choice; SCREEN on a machine that has none, or one this build does not know.</summary>
        public static FrameRateChoice Choice =>
            Preferences.GetInt(PrefKey, (int)FrameRateChoice.Screen) == (int)FrameRateChoice.Sixty
                ? FrameRateChoice.Sixty
                : FrameRateChoice.Screen;

        /// <summary>What a choice sets: vertical sync, and the frame-rate cap (-1 for none).</summary>
        /// <remarks>
        /// A cap is ignored while vertical sync is on, so holding at 60 has to turn it off. In a
        /// window the desktop compositor still presents without tearing; fullscreen can tear on
        /// something moving fast, and a board that mostly sits still has little that does. SCREEN
        /// puts back vertical sync every frame, which <c>FrameRateTests</c> holds to being the
        /// project's own setting.
        /// </remarks>
        public static (int vSyncCount, int targetFrameRate) SettingsFor(FrameRateChoice choice) =>
            choice == FrameRateChoice.Sixty ? (0, Held) : (1, -1);

        /// <summary>
        /// Whether a choice is put into effect here: a desktop player, not a browser and not the editor.
        /// </summary>
        /// <remarks>
        /// The editor's Game view has its own VSync switch, and a cap there would slow every Play Mode
        /// test to sixty frames a second. The choice is still stored and shown in the editor.
        /// </remarks>
        public static bool Applies => DisplayRules.Offered && !Application.isEditor;

        /// <summary>Stores a choice and puts it into effect.</summary>
        public static void Set(FrameRateChoice choice)
        {
            Preferences.SetInt(PrefKey, (int)choice);
            Apply(choice);
        }

        private static void Apply(FrameRateChoice choice)
        {
            if (!Applies)
                return;

            (int vSyncCount, int targetFrameRate) settings = SettingsFor(choice);
            QualitySettings.vSyncCount = settings.vSyncCount;
            Application.targetFrameRate = settings.targetFrameRate;
        }

        /// <summary>The stored choice, from the first frame of every launch.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Boot() => Apply(Choice);
    }
}
