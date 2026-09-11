using System.IO;
using BitSorter.View;
using UnityEngine;

namespace BitSorter.PlayMode.Tests
{
    /// <summary>
    /// Puts the player's real progress file out of reach for the duration of a fixture, and puts it
    /// back afterwards.
    /// </summary>
    /// <remarks>
    /// These tests load the real scene, which means `ProgressTracker` opens the real save at
    /// `Application.persistentDataPath` and writes to it whenever anything is solved or any hint is
    /// marked seen. That has already gone wrong twice by hand: play-testing the tutorial wrote a
    /// `tutorial` milestone and a saved board into a real save, and both had to be unpicked
    /// afterwards. A test suite doing it automatically, on every run, would be worse.
    ///
    /// Moving the file rather than pointing the game somewhere else is deliberate. `ProgressTracker`
    /// has a `_pathOverride` field but it is private and serialized, so the only way to set it from
    /// here is reflection -- and it would have to happen between the scene loading and `Awake`
    /// running, which is not a moment a test gets to act in. Moving the file needs no seam, no
    /// production change, and works no matter how the store is constructed.
    ///
    /// The stash is a sibling file rather than a temp directory, so a run that dies without
    /// restoring leaves the original next to where it belongs, under an obvious name, rather than
    /// somewhere the player would never look.
    /// </remarks>
    internal static class SaveGuard
    {
        private static string Real => ProgressStore.DefaultPath;

        private static string Stashed => Real + ".testbackup";

        /// <summary>Moves the real save aside, leaving the game to start from nothing.</summary>
        /// <remarks>
        /// **A stash that already exists is the player's save, not debris.** This method had that
        /// backwards once and destroyed a real save with it: after a run that died before restoring,
        /// the stash holds the player's file and the thing sitting at the normal path is whatever the
        /// tests wrote afterwards. Deleting "the stale stash" therefore deleted the only real copy
        /// and kept an empty one. Completed levels, personal bests and seen hints, all gone, by the
        /// guard written to prevent exactly that.
        ///
        /// So an existing stash is never touched. The file at the live path is the disposable one.
        /// </remarks>
        internal static void Stash()
        {
            Archive();

            if (File.Exists(Stashed))
            {
                // A previous run died before restoring. The stash is the save; whatever is at the
                // live path now is test debris, and that is the one that goes.
                if (File.Exists(Real))
                    File.Delete(Real);

                return;
            }

            if (File.Exists(Real))
                File.Move(Real, Stashed);
        }

        /// <summary>
        /// Keeps a dated copy that nothing here ever deletes.
        /// </summary>
        /// <remarks>
        /// Belt as well as braces. The move-and-restore above is correct now, but it was also
        /// "correct" when it deleted a save, and the cost of being wrong is somebody's progress
        /// rather than a red test. These copies are never cleaned up by this class -- a few hundred
        /// bytes each is a trade worth making every time.
        /// </remarks>
        private static void Archive()
        {
            if (!File.Exists(Real))
                return;

            string stamp = System.DateTime.Now.ToString("yyyyMMdd-HHmmss");
            string copy = Real + ".archive-" + stamp;

            if (!File.Exists(copy))
                File.Copy(Real, copy);
        }

        /// <summary>Deletes whatever the tests wrote and puts the player's file back.</summary>
        internal static void Restore()
        {
            if (File.Exists(Real))
                File.Delete(Real);

            if (File.Exists(Stashed))
                File.Move(Stashed, Real);
        }

        /// <summary>
        /// Writes a save for the scene to find when it loads.
        /// </summary>
        /// <remarks>
        /// Only valid between <see cref="Stash"/> and <see cref="Restore"/>, and only before the
        /// scene loads -- `ProgressTracker` reads the file in `Awake` and never looks again.
        /// </remarks>
        internal static void Plant(string json)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Real));
            File.WriteAllText(Real, json);
        }

        /// <summary>Removes any save a previous test in the same fixture planted.</summary>
        internal static void Clear()
        {
            if (File.Exists(Real))
                File.Delete(Real);
        }

        /// <summary>What the game actually wrote, or null. For asserting a test did not persist.</summary>
        internal static string Read() => File.Exists(Real) ? File.ReadAllText(Real) : null;
    }
}
