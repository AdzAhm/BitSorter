using System;
using System.Collections;
using BitSorter.View;
using BitSorter.ViewModel;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BitSorter.PlayMode.Tests
{
    /// <summary>
    /// What a browser can hold (Part 15h-6d). A browser frees memory only between frames, so whatever one frame makes is
    /// memory the page must have, and the build may grow to 2 GB. Opening the pipelined processor on FIBONACCI made 1.8 GB
    /// in a frame -- most of it keys made of the whole project's text, made again on every read of a written module -- and
    /// the page ran out on the first click, and on every launch after, since BitSorter opens on the last thing open. Held
    /// here to half of it.
    /// </summary>
    [TestFixture]
    public class BrowserMemoryPlayTests
    {
        private const long HalfTheBrowsersMemory = 1L << 30;

        [OneTimeSetUp]
        public void OneTimeSetup()
        {
            SaveGuard.Redirect();
            GameAnalytics.SetReporting(false);
        }

        [OneTimeTearDown]
        public void OneTimeCleanup() => SaveGuard.Release();

        [TearDown]
        public void TearDown() => SaveGuard.Clear();

        [UnityTearDown]
        public IEnumerator ClearTheScene()
        {
            yield return TestScene.Clear();
        }

        [UnityTest]
        public IEnumerator OpeningThePipelinedProcessor_MakesLessThanHalfTheBrowsersMemoryInAFrame()
        {
            yield return TestScene.LoadSimulator();
            yield return null;

            WorkbenchPanel workbench = Object.FindFirstObjectByType<WorkbenchPanel>();
            int index = -1;
            for (int i = 0; i < WorkbenchSamples.Chip.Count; i++)
                if (WorkbenchSamples.Chip[i].Name == "Pipelined RISC-V")
                    index = i;
            Assert.GreaterOrEqual(index, 0, "sanity: the pipelined processor on the start page");

            using (var recorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame"))
            {
                yield return null;
                workbench.OpenProcessor(index, "FIBONACCI");

                long worst = 0;
                for (int f = 0; f < 10; f++)
                {
                    yield return null;
                    worst = Math.Max(worst, recorder.LastValue);
                }

                Assert.IsTrue(recorder.Valid, "sanity: the editor counts what a frame allocates");
                Assert.AreEqual("FIBONACCI", workbench.Session.Choices[0].Chosen, "sanity: opened on the program asked for");
                Assert.Less(worst, HalfTheBrowsersMemory,
                    $"the frames opening it made up to {worst / 1_000_000} MB; a browser holds all of a frame's at once");
            }
        }
    }
}
