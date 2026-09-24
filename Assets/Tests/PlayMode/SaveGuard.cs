using System.Collections.Generic;
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
    /// Redirecting removes the class instead of handling it. Nothing here writes, moves or deletes
    /// `progress.json`. An interrupted run leaves it exactly as it was, because no step in this file
    /// ever writes to it.
    ///
    /// The one thing still done to it is read it, for a defensive copy beside it whenever it has
    /// changed since the last copy, in case a future change reintroduces a path that does write to
    /// the real file. It costs a few kilobytes and it is the thing that made the first incident
    /// recoverable.
    /// </remarks>
    internal static class SaveGuard
    {
        /// <summary>The player's real file. Read for the safety copy, and never written.</summary>
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

            // The mute and the reporting setting live in PlayerPrefs, not in the save file, and the
            // fixtures need reporting off. They used to read the real value, overwrite it, and put
            // it back in OneTimeTearDown -- which made the developer's own settings depend on a run
            // finishing cleanly, the exact failure the paragraphs above describe for the save file.
            // An interrupted run left the game muted with analytics off and nothing to say why.
            //
            // Redirected before the scene loads, because GameAnalytics applies consent from its
            // AfterSceneLoad bootstrap and GameAudio reads the mute in Start.
            Preferences.Redirected = new Dictionary<string, int>();
        }

        /// <summary>Hands saving, loading and the machine's settings back to the real ones.</summary>
        internal static void Release()
        {
            ProgressStore.Redirected = null;
            Preferences.Redirected = null;

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
        /// A dated copy of the real save when it has changed since the last one, which nothing here
        /// ever deletes.
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
            Archive(Real, System.DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        }

        /// <summary>
        /// Copies <paramref name="save"/> beside itself, stamped, and returns the copy's path -- or
        /// null when there was nothing new to copy.
        /// </summary>
        /// <remarks>
        /// Takes the file and the stamp rather than reading the clock and the real path, so a test
        /// can run it against a scratch folder and a second "session" of its own choosing.
        ///
        /// **Only when the save has changed since the newest copy.** "Once per session" was a static
        /// flag, and entering play mode reloads the domain, so it was once per test run -- 210
        /// identical copies of an unchanged save had piled up by 2026-09-24. A copy with nothing new
        /// in it protects nothing. The stamps sort by time, so the newest is the last by name.
        /// </remarks>
        internal static string Archive(string save, string stamp)
        {
            if (!File.Exists(save))
                return null;

            string copy = save + ".archive-" + stamp;

            if (File.Exists(copy))
                return null;

            string[] earlier = Directory.GetFiles(
                Path.GetDirectoryName(save), Path.GetFileName(save) + ".archive-*");

            if (earlier.Length > 0)
            {
                System.Array.Sort(earlier, System.StringComparer.Ordinal);

                if (SameBytes(save, earlier[earlier.Length - 1]))
                    return null;
            }

            File.Copy(save, copy);
            return copy;
        }

        private static bool SameBytes(string a, string b)
        {
            byte[] left = File.ReadAllBytes(a);
            byte[] right = File.ReadAllBytes(b);

            if (left.Length != right.Length)
                return false;

            for (int i = 0; i < left.Length; i++)
            {
                if (left[i] != right[i])
                    return false;
            }

            return true;
        }
    }
}
