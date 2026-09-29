using System;
using UnityEngine;

namespace BitSorter.View
{
    /// <summary>
    /// The frame-rate settings -- vertical sync, a cap for when it is off, and whether the counter
    /// is in the corner: what each does to the player, and remembering them.
    /// </summary>
    /// <remarks>
    /// The game shipped with vertical sync on and no cap, so a 165 Hz laptop drew 165 frames a
    /// second, and that is still what a machine with no settings gets. The first version of this
    /// (2026-09-28) offered one way down, held at 60; asked for again, the cap became the player's
    /// to choose and vertical sync a switch of its own (2026-09-30). Nothing in the game runs
    /// better or worse for either: the simulation keeps its own clock and every animation counts
    /// time rather than frames.
    ///
    /// **A cap does nothing while vertical sync is on** -- Unity ignores the target frame rate then
    /// -- so Settings greys the cap out and locks it until vertical sync is off, still saying what
    /// it is set to, as the volume is while the sound is off. With vertical sync off the desktop
    /// compositor still presents a window without tearing; fullscreen can tear on something moving
    /// fast, and a board that mostly sits still has little that does.
    ///
    /// Desktop only, like fullscreen (<see cref="DisplayRules.Offered"/>): in a browser the page
    /// decides how often the game is drawn, and a cap there only makes it worse.
    /// </remarks>
    public static class FrameRate
    {
        /// <summary>Where vertical sync is kept, beside the sound settings.</summary>
        public const string VSyncKey = "bitsorter.vsync";

        /// <summary>Where the cap is kept: the frames a second, never the slider's stop.</summary>
        /// <remarks>
        /// A stop stored would silently mean a different rate the day a stop was added below it.
        /// </remarks>
        public const string CapKey = "bitsorter.frameCap";

        /// <summary>Where the frame-rate counter's switch is kept.</summary>
        public const string CounterKey = "bitsorter.fpsCounter";

        /// <summary>No cap: Unity's own spelling of it.</summary>
        public const int NoCap = -1;

        /// <summary>The cap a machine starts at, for when vertical sync is first turned off.</summary>
        /// <remarks>
        /// Not <see cref="NoCap"/>: a board that sits still, drawn as fast as the machine can go, is
        /// thousands of frames a second of fan noise for nothing. The slider shows it before it
        /// applies, greyed out beside vertical sync.
        /// </remarks>
        public const int DefaultCap = 60;

        /// <summary>
        /// The caps the slider stops at, lowest first. One stop past the last is no cap.
        /// </summary>
        /// <remarks>The rates screens come in, and 30 under them for a laptop with little battery left.</remarks>
        private static readonly int[] Caps = { 30, 60, 75, 90, 120, 144, 165, 240 };

        /// <summary>How many stops the slider has: every cap, then none.</summary>
        public static int Stops => Caps.Length + 1;

        /// <summary>The cap at a stop of the slider; the last stop, or any past it, is no cap.</summary>
        public static int CapAt(int stop) => stop >= 0 && stop < Caps.Length ? Caps[stop] : NoCap;

        /// <summary>Whether vertical sync is on: on for a machine that has never said otherwise.</summary>
        public static bool VSync => Preferences.GetInt(VSyncKey, 1) != 0;

        /// <summary>
        /// The stored cap; <see cref="DefaultCap"/> on a machine that has none, or one this build
        /// does not offer.
        /// </summary>
        /// <remarks>
        /// A value this build does not know reads as the default rather than as no cap, which is the
        /// one reading that could cost the player something.
        /// </remarks>
        public static int Cap
        {
            get
            {
                int stored = Preferences.GetInt(CapKey, DefaultCap);
                return stored == NoCap || Array.IndexOf(Caps, stored) >= 0 ? stored : DefaultCap;
            }
        }

        /// <summary>The stop the slider shows for the stored cap.</summary>
        public static int CapStop
        {
            get
            {
                int stop = Array.IndexOf(Caps, Cap);
                return stop >= 0 ? stop : Caps.Length;
            }
        }

        /// <summary>What a pair of settings sets: vertical sync, and the target frame rate.</summary>
        /// <remarks>
        /// With vertical sync on there is no cap to set -- it would be ignored -- so the target goes
        /// back to none, and <c>FrameRateTests</c> holds vertical sync on to being the project's own
        /// setting, the game as it shipped.
        /// </remarks>
        public static (int vSyncCount, int targetFrameRate) SettingsFor(bool vSync, int cap) =>
            vSync ? (1, NoCap) : (0, cap);

        /// <summary>
        /// Whether the settings are put into effect here: a desktop player, not a browser and not the
        /// editor.
        /// </summary>
        /// <remarks>
        /// The editor's Game view has its own VSync switch, and a cap there would slow every Play Mode
        /// test to it. The settings are still stored and shown in the editor.
        /// </remarks>
        public static bool Applies => DisplayRules.Offered && !Application.isEditor;

        /// <summary>Stores vertical sync on or off and puts it into effect.</summary>
        public static void SetVSync(bool on)
        {
            Preferences.SetInt(VSyncKey, on ? 1 : 0);
            Apply();
        }

        /// <summary>
        /// Stores the cap at a stop of the slider and puts it into effect. <paramref name="keep"/>
        /// writes it out; a drag passes false for every stop and keeps it once, on release.
        /// </summary>
        public static void SetCapStop(int stop, bool keep)
        {
            int cap = CapAt(Mathf.Clamp(stop, 0, Stops - 1));

            if (keep)
                Preferences.SetInt(CapKey, cap);
            else
                Preferences.Stage(CapKey, cap);

            Apply();
        }

        /// <summary>Writes out a cap staged during a drag.</summary>
        public static void KeepCap() => Preferences.Save();

        /// <summary>
        /// Whether <see cref="FrameRateCounter"/> is in the corner: off until the player asks for it.
        /// </summary>
        /// <remarks>
        /// Only a switch to remember -- it changes nothing about how the game is drawn, so unlike
        /// the two above it is honoured in the editor as well.
        /// </remarks>
        public static bool ShowsCounter => Preferences.GetInt(CounterKey, 0) != 0;

        /// <summary>Stores whether the counter is shown. The counter itself notices on its next frame.</summary>
        public static void SetCounter(bool on) => Preferences.SetInt(CounterKey, on ? 1 : 0);

        private static void Apply()
        {
            if (!Applies)
                return;

            (int vSyncCount, int targetFrameRate) settings = SettingsFor(VSync, Cap);
            QualitySettings.vSyncCount = settings.vSyncCount;
            Application.targetFrameRate = settings.targetFrameRate;
        }

        /// <summary>The stored settings, from the first frame of every launch.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Boot() => Apply();
    }
}
