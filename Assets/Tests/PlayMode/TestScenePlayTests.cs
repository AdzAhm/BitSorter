using System.Collections;
using BitSorter.View;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BitSorter.PlayMode.Tests
{
    /// <summary>
    /// What <see cref="TestScene.Clear"/> leaves behind for the test after it.
    /// </summary>
    /// <remarks>
    /// Every fixture deletes the scratch save in its [TearDown] and unloads the game in its
    /// [UnityTearDown], and Unity Test Framework runs the first before the second -- its command
    /// builder wraps the plain set-up and tear-down inside the enumerable ones. So the save was
    /// deleted with the game still loaded, and anything the game wrote while it was being unloaded
    /// was there for the next test's load. <c>ProgressTracker</c> writes the board half a second
    /// after it last changed, by real time, and at once when the window loses focus: both can land
    /// in the frames an unload takes. <see cref="TestScene.Clear"/> deletes the save itself now,
    /// once the game is gone.
    ///
    /// The write here comes from <c>OnDestroy</c>, the last moment anything in the game can write
    /// at all, so the test does not depend on how long a frame takes.
    /// </remarks>
    public class TestScenePlayTests
    {
        [OneTimeSetUp]
        public void OneTimeSetup()
        {
            SaveGuard.Redirect();
            GameAnalytics.SetReporting(false);
        }

        [OneTimeTearDown]
        public void OneTimeCleanup() => SaveGuard.Release();

        [TearDown]
        public void ClearTheSave() => SaveGuard.Clear();

        [UnityTearDown]
        public IEnumerator ClearTheScene()
        {
            yield return TestScene.Clear();
        }

        [UnityTest]
        public IEnumerator ClearingTheGame_TakesWhatItWroteOnTheWayOut()
        {
            yield return TestScene.Load();

            // Where a fixture's [TearDown] leaves things: the save deleted, the game still loaded.
            SaveGuard.Clear();

            WritesTheSaveOnTheWayOut.Wrote = false;
            var probe = new GameObject("writes the save on the way out").AddComponent<WritesTheSaveOnTheWayOut>();
            probe.Store = Object.FindFirstObjectByType<ProgressTracker>().Store;

            yield return TestScene.Clear();

            Assert.IsTrue(WritesTheSaveOnTheWayOut.Wrote, "sanity: the game wrote nothing on its way out");
            Assert.IsNull(SaveGuard.Read(),
                "the game wrote its save on the way out, and clearing the game left it for the next test to load");
        }
    }

    /// <summary>
    /// Writes the game's save as the game is unloaded, the latest a write can come.
    /// </summary>
    internal sealed class WritesTheSaveOnTheWayOut : MonoBehaviour
    {
        /// <summary>Whether the write was made, for the test to know the save was there to clear.</summary>
        internal static bool Wrote;

        internal ProgressStore Store;

        private void OnDestroy()
        {
            Store.Save();
            Wrote = true;
        }
    }
}
