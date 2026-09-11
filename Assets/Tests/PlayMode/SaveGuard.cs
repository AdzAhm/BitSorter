using System.IO;
using BitSorter.View;
using UnityEngine;

namespace BitSorter.PlayMode.Tests
{
    /// <summary>
    /// Points the game at a scratch save for the duration of a fixture, and never touches the
    /// player's own.
    /// </summary>
    /// <remarks>
    /// These tests load the real scene, which means `ProgressTracker` opens whatever
    /// `ProgressStore.DefaultPath` returns and writes to it whenever anything is solved or any hint
    /// is marked seen. Two earlier designs handled that by moving the real file out of the way, and
    /// both were wrong in the same direction -- they made the player's data depend on a test run
    /// finishing cleanly.
    ///
    /// The first deleted a save outright: after a run that died before restoring, the stash held the
    /// real file and the live path held test debris, and the "clean up the stale stash" branch
    /// deleted exactly the wrong one. The second fixed that and still left the file *missing* when a
    /// run was interrupted, so the game opened looking like a fresh install.
    ///
    /// Redirecting removes the class instead of handling it. Nothing here opens, copies, moves or
    /// deletes `progress.json`. An interrupted run leaves it exactly as it was, because no step in
    /// this file ever refers to it.
    ///
    /// The one thing still worth doing defensively is a copy, once per session, in case a future
    /// change reintroduces a path that does write to the real file. It costs a few kilobytes and it
    /// is the thing that made the first incident recoverable.
    /// </remarks>
    internal static class SaveGuard
    {
        /// <summary>The player's real file. Read for the safety copy, and for nothing else.</summary>
        private static string Real =>
            Path.Combine(Application.persistentDataPath, "progress.json");

        /// <summary>The scratch file the tests actually use.</summary>
        private static string Scratch =>
            Path.Combine(Application.persistentDataPath, "progress.testrun.json");

        private static bool _archived;

        /// <summary>
        /// Sends the game's saving and loading to a scratch file.
        /// </summary>
        /// <remarks>
        /// Must run before the scene loads. `ProgressTracker` resolves the path in Awake and keeps
        /// the store it built, so a redirect set afterwards would apply to nothing.
        /// </remarks>
        internal static void Redirect()
        {
            ArchiveOnce();

            // Debris from a previous run. Deleting it here rather than in Release means an
            // interrupted run leaves something to look at.
            if (File.Exists(Scratch))
                File.Delete(Scratch);

            ProgressStore.Redirected = Scratch;
        }

        /// <summary>Hands saving and loading back to the player's own file.</summary>
        internal static void Release()
        {
            ProgressStore.Redirected = null;

            if (File.Exists(Scratch))
                File.Delete(Scratch);
        }

        /// <summary>
        /// Writes a save for the scene to find when it loads.
        /// </summary>
        /// <remarks>
        /// Only meaningful between <see cref="Redirect"/> and <see cref="Release"/>, and only before
        /// the scene loads -- `ProgressTracker` reads the file in Awake and never looks again.
        /// </remarks>
        internal static void Plant(string json)
        {
            Debug.Assert(ProgressStore.Redirected != null,
                "SaveGuard.Plant before Redirect would write to the player's own save");

            File.WriteAllText(Scratch, json);
        }

        /// <summary>Removes anything a previous test in the same fixture planted or the game wrote.</summary>
        internal static void Clear()
        {
            if (File.Exists(Scratch))
                File.Delete(Scratch);
        }

        /// <summary>What the game wrote during a test, or null.</summary>
        internal static string Read() => File.Exists(Scratch) ? File.ReadAllText(Scratch) : null;

        /// <summary>
        /// One dated copy of the real save per session, which nothing here ever deletes.
        /// </summary>
        /// <remarks>
        /// Belt as well as braces. The redirect above means no test should be able to reach the
        /// player's file at all -- but the last two designs were also believed safe, and the cost of
        /// being wrong is somebody's progress rather than a red test.
        /// </remarks>
        private static void ArchiveOnce()
        {
            if (_archived || !File.Exists(Real))
                return;

            _archived = true;

            string copy = Real + ".archive-" + System.DateTime.Now.ToString("yyyyMMdd-HHmmss");

            if (!File.Exists(copy))
                File.Copy(Real, copy);
        }
    }
}
