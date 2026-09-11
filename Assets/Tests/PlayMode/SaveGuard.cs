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
        internal static void Stash()
        {
            // A stash already sitting there is the debris of a run that crashed before restoring.
            // The real file is the one to keep, so the stale stash goes rather than the save.
            if (File.Exists(Stashed) && File.Exists(Real))
                File.Delete(Stashed);

            if (File.Exists(Real))
                File.Move(Real, Stashed);
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
