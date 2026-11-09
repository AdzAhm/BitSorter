using System.Runtime.InteropServices;

namespace BitSorter.View
{
    /// <summary>
    /// The save in a browser with BitSorter open in two tabs (browser-1; Ahmad's decision, 2026-11-03: warn and reload):
    /// what <see cref="ProgressStore"/> says to the page, through <c>Assets/Plugins/WebGL/BitSorterSave.jslib</c>, so each
    /// tab hears of a save another writes. Everywhere else -- the editor, a desktop build, the tests -- nothing is called, and
    /// the stamp in the file itself says it, a second BitSorter writing the same file.
    /// </summary>
    /// <remarks>
    /// **A tab's file system is its own.** Unity keeps a browser build's persistent data path in memory, read from IndexedDB
    /// once as the page loads and written back whole whenever a file in it changes, so the file a tab reads is the one it
    /// loaded with, whatever another tab has written since -- and anything it writes, a setting included, carries that older
    /// save back over the newer. The page tells every tab of each save once it is in IndexedDB; a tab that hears of one not
    /// its own takes IndexedDB's files into memory at once and says so here, and its store writes nothing over the save until
    /// the page is loaded again (<see cref="ReloadPage"/>).
    /// </remarks>
    internal static class BrowserSave
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] private static extern void BitSorterSaveRead(string stamp, string path);
        [DllImport("__Internal")] private static extern int BitSorterSaveWrittenElsewhere();
        [DllImport("__Internal")] private static extern void BitSorterSaveWritten(string stamp, string path);
        [DllImport("__Internal")] private static extern void BitSorterSaveReload();

        /// <summary>Whether this is a browser, whose page has the tabs to hear of.</summary>
        public static bool Available => true;
#else
        private static void BitSorterSaveRead(string stamp, string path) { }
        private static int BitSorterSaveWrittenElsewhere() => 0;
        private static void BitSorterSaveWritten(string stamp, string path) { }
        private static void BitSorterSaveReload() { }

        /// <inheritdoc cref="Available"/>
        public static bool Available => false;
#endif

        /// <summary>
        /// The save at <paramref name="path"/> read, with <paramref name="stamp"/> (null for none): from now on the page hears
        /// of every other tab's save, and holds this one's for a save of the same path -- another build's on the same origin
        /// is another file, in the same IndexedDB (the browser-1 fix's review).
        /// </summary>
        public static void Read(string stamp, string path) => BitSorterSaveRead(stamp ?? "", path ?? "");

        /// <summary>Whether another tab has written the save since this one read it, or last wrote it.</summary>
        public static bool WrittenElsewhere() => BitSorterSaveWrittenElsewhere() != 0;

        /// <summary>This tab's save at <paramref name="path"/> written, with <paramref name="stamp"/>: told to the other tabs once it is in IndexedDB.</summary>
        public static void Written(string stamp, string path) => BitSorterSaveWritten(stamp ?? "", path ?? "");

        /// <summary>The page loaded again, and so the save read again from IndexedDB, as another tab left it.</summary>
        public static void ReloadPage() => BitSorterSaveReload();
    }
}
