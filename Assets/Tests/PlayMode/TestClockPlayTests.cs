using System.Collections;
using BitSorter.View;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BitSorter.PlayMode.Tests
{
    /// <summary>
    /// Every test that loads the game runs on a driven clock: each frame is exactly a sixtieth of a
    /// second of game time, however fast the machine draws frames.
    /// </summary>
    /// <remarks>
    /// A wait capped in frames only meant seconds at the editor's frame rate. Headless, in batch mode,
    /// frames are drawn as fast as the machine can, and on 2026-10-12 the 1200 frames
    /// <see cref="WireDelayHintPlayTests"/> allowed for 1.5 seconds of a hint came to between 0.57 and
    /// 1.40 seconds -- red on main as well as on the branch being checked. Worse than red: two tests
    /// ran a fixed 240 frames for bits to travel, which headless was less than one tick of the
    /// simulation, so no bit was ever born and what they asserted about bits held of none.
    ///
    /// Driving the clock is CLAUDE.md's "drive the clock, do not wait for it" applied to the whole
    /// suite at once, where the game is loaded, so no test has to remember it.
    /// </remarks>
    public class TestClockPlayTests
    {
        private const float Sixtieth = 1f / 60f;

        [OneTimeSetUp]
        public void OneTimeSetup()
        {
            SaveGuard.Redirect();
            GameAnalytics.SetReporting(false);
        }

        [OneTimeTearDown]
        public void OneTimeCleanup() => SaveGuard.Release();

        [TearDown]
        public void PutTheClockBack()
        {
            SaveGuard.Clear();
            Time.captureDeltaTime = 0f;
        }

        [UnityTearDown]
        public IEnumerator ClearTheScene()
        {
            yield return TestScene.Clear();
        }

        [UnityTest]
        public IEnumerator TheGame_LoadsOnADrivenClock()
        {
            Assert.AreEqual(0f, Time.captureDeltaTime,
                "sanity: something that ran before this left a step on the clock");

            yield return TestScene.Load();

            Assert.AreEqual(Sixtieth, Time.captureDeltaTime, 1e-7f, "the game loaded on a free-running clock");

            float before = Time.time;

            for (int frame = 0; frame < 60; frame++)
                yield return null;

            Assert.AreEqual(1f, Time.time - before, 1e-3f, "sixty frames were not one second of game time");

            // Not asserted: whether the driven step reaches the unscaled clock as well. Written out so
            // a run says, for the code that counts on unscaled time.
            TestContext.WriteLine($"unscaledDeltaTime on a driven clock: {Time.unscaledDeltaTime}");
        }

        /// <summary>The reference shots run at a 120th of a second a frame, and loading must not undo it.</summary>
        [UnityTest]
        public IEnumerator AStepChosenBeforeTheLoad_IsKept()
        {
            Time.captureDeltaTime = 1f / 120f;

            yield return TestScene.Load();

            Assert.AreEqual(1f / 120f, Time.captureDeltaTime, 1e-7f, "loading the game replaced the fixture's own step");
        }

        [UnityTest]
        public IEnumerator ClearingTheGame_GivesRealTimeBack()
        {
            yield return TestScene.Load();

            // Whatever step the test was left at.
            Time.captureDeltaTime = 0.1f;

            yield return TestScene.Clear();

            Assert.AreEqual(0f, Time.captureDeltaTime,
                "clearing the game left the clock driven for whatever runs next without it");
        }
    }
}
