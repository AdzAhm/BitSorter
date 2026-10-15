using System.Collections.Generic;

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
    ///
    /// **Kept once read**, because it is asked for every part and every chunk of every frame, and
    /// reading PlayerPrefs is not free: in the editor each read goes to the registry, and with 300 wires
    /// a frame took 22 ms reading it some five hundred times and 13 reading it once
    /// (<c>WorkbenchPerformanceTests</c>, in the editor, 2026-10-14). The answer is kept with the store
    /// it came from, so a test that points <see cref="Preferences.Redirected"/> somewhere else reads
    /// again rather than inheriting the machine's own setting, and <see cref="Set"/> keeps the answer
    /// it wrote.
    /// </remarks>
    public static class ReducedMotion
    {
        /// <summary>Where it is kept, beside the other settings.</summary>
        public const string Key = "bitsorter.reducedMotion";

        private static bool _known, _on;
        private static Dictionary<string, int> _readFrom;

        /// <summary>Whether motion is reduced. Off unless asked for.</summary>
        public static bool IsOn
        {
            get
            {
                Dictionary<string, int> store = Preferences.Redirected;
                if (!_known || !ReferenceEquals(store, _readFrom))
                {
                    _on = Preferences.GetInt(Key, 0) != 0;
                    _readFrom = store;
                    _known = true;
                }

                return _on;
            }
        }

        /// <summary>Asks for less motion, or for all of it back.</summary>
        public static void Set(bool on)
        {
            Preferences.SetInt(Key, on ? 1 : 0);
            _on = on;
            _readFrom = Preferences.Redirected;
            _known = true;
        }
    }
}
