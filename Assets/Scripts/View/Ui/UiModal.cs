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
    /// </remarks>
    public static class UiModal
    {
        private static readonly List<Object> Open = new List<Object>();

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
            Open.Remove(panel);
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
