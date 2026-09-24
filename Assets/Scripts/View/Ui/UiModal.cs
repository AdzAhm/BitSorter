using System.Collections.Generic;
using UnityEngine;

namespace BitSorter.View
{
    /// <summary>
    /// Which full-screen panels are currently open, so the board underneath knows to hold still.
    /// </summary>
    /// <remarks>
    /// A scrim stops clicks reaching the board, because <see cref="PointerGate"/> sees the pointer
    /// over an interface. Keys are the gap: without this, Q behind an open menu would change level
    /// under it, and space would start the clock on a board nobody can see.
    ///
    /// Membership is a set of objects, checked for life on every query, rather than a counter. The
    /// same reasoning as PointerGate's drag owner: a panel destroyed while open -- which is exactly
    /// what a level switch does -- would leave a counter permanently above zero and the whole
    /// keyboard silently dead, with no error and nothing the player could do. Unity reports a
    /// destroyed object as null, so a panel that vanishes closes itself.
    ///
    /// **Anything that acts on a key asks <see cref="OpenOrJustClosed"/>, not
    /// <see cref="AnyOpen"/>.** Two readers of one key in one frame each see what the other has
    /// done so far, and Unity does not define which updates first. The sandbox's setup closed on
    /// Escape and the level list opened on Escape when nothing was open, so whenever the setup went
    /// first a single press did both. Treating "closed earlier this frame" as still open means the
    /// press that closed one panel can never also open another, whichever order they run in -- nor
    /// reach the board: the chapter card closes on Enter, and the board's keys used to ask
    /// AnyOpen, so the same press could run an empty board as well.
    /// </remarks>
    public static class UiModal
    {
        private static readonly List<Object> Open = new List<Object>();

        /// <summary>The frame a panel last closed on, or -1.</summary>
        private static int _lastClosedFrame = -1;

        /// <summary>
        /// Whether the HUD -- banner, run buttons, parts list, help badge -- should be drawn.
        /// </summary>
        /// <remarks>
        /// Not while a full-screen panel is up. They used to stay lit beside one, and the panels'
        /// own titles and help lines printed straight over the banner and the buttons. The one rule,
        /// asked by each HUD piece, so they cannot disagree about when to step aside.
        ///
        /// "Up" is <see cref="FullScreenPanel.CoversTheHud"/>: a panel fading in over the HUD
        /// covers it before the HUD goes, rather than leaving the board bare for a moment.
        /// </remarks>
        public static bool HudVisible
        {
            get
            {
                Prune();

                foreach (Object panel in Open)
                {
                    if (!(panel is FullScreenPanel screen) || screen.CoversTheHud)
                        return false;
                }

                return true;
            }
        }

        /// <summary>
        /// Whether anything is covering the board, or was until earlier in this frame.
        /// </summary>
        /// <remarks>
        /// The guard for a panel that opens on a key press. See the class remarks.
        /// </remarks>
        public static bool OpenOrJustClosed => OpenOrClosedOn(Time.frameCount);

        /// <summary>
        /// <see cref="OpenOrJustClosed"/> against a frame of the caller's choosing, so the rule can
        /// be checked without a running player loop.
        /// </summary>
        public static bool OpenOrClosedOn(int frame) => AnyOpen || _lastClosedFrame == frame;

        /// <summary>Whether anything is covering the board.</summary>
        public static bool AnyOpen
        {
            get
            {
                Prune();
                return Open.Count > 0;
            }
        }

        public static void Opened(Object panel)
        {
            Prune();

            if (panel != null && !Open.Contains(panel))
                Open.Add(panel);
        }

        public static void Closed(Object panel)
        {
            Prune();

            if (Open.Remove(panel))
                _lastClosedFrame = Time.frameCount;
        }

        /// <summary>
        /// Drops anything destroyed since the last look.
        /// </summary>
        /// <remarks>
        /// Backwards, so removing one entry does not skip the next. Runs on every query, which is
        /// affordable because the list never holds more than a handful of panels.
        /// </remarks>
        private static void Prune()
        {
            for (int i = Open.Count - 1; i >= 0; i--)
            {
                if (Open[i] == null)
                    Open.RemoveAt(i);
            }
        }
    }
}
