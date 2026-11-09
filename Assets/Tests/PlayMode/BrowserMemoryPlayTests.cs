using System;
using System.Collections;
using System.IO;
using BitSorter.Board;
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
    /// the page ran out on the first click, and on every launch after, since BitSorter opens on the last thing open. 15h-6d
    /// brought it to 601 MB, most of what was left the sample's import trying to draw each of its modules with every module
    /// written so far; Part 16b hands each attempt only the modules it makes, and holds the open to 256 MB.
    /// </summary>
    [TestFixture]
    public class BrowserMemoryPlayTests
    {
        /// <summary>What a frame opening the processor may make: the plan's target for Part 16b, an eighth of what a page may grow to.</summary>
        private const long Cap = 256L << 20;

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
        public IEnumerator OpeningThePipelinedProcessor_MakesUnder256MBInAFrame()
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
                Assert.Greater(worst, 1_000_000L, "sanity: the counter saw the open at all -- a processor is tens of MB to make");
                Assert.AreEqual("FIBONACCI", workbench.Session.Choices[0].Chosen, "sanity: opened on the program asked for");
                TestContext.WriteLine($"the worst frame opening the pipelined processor made {worst / 1_000_000} MB");
                Assert.Less(worst, Cap,
                    $"the frames opening it made up to {worst / 1_000_000} MB; a browser holds all of a frame's at once");
            }
        }

        /// <summary>
        /// Reading a large save makes little more in its frame than Unity's reader makes of it (the newer-save review). The
        /// save is read in the first frame, beside the launch's open, for its stamp and for what of it this BitSorter does not
        /// know as well, and that reading built again every string the save held and every key's path each time it was met:
        /// 105 MB more for a 5 MB save, in the scanner alone (<c>SaveFileTests</c>, under dotnet). What is left is the save
        /// written back once in memory to set what it read beside -- a string no longer than the file, as every write makes.
        /// </summary>
        [UnityTest]
        public IEnumerator ReadingALargeSave_MakesLittleMoreInItsFrameThanUnitysReaderDoes()
        {
            string folder = Path.Combine(Application.temporaryCachePath, "large-save-" + Guid.NewGuid().ToString("N"));
            string path = Path.Combine(folder, "progress.json");
            try
            {
                Project processor = null;
                for (int i = 0; i < WorkbenchSamples.All.Count; i++)
                    if (WorkbenchSamples.All[i].Name == "Pipelined RISC-V")
                        processor = WorkbenchSamples.All[i].Build();
                Assert.IsNotNull(processor, "sanity: the pipelined processor to fill a save with");
                var writer = new ProgressStore(path);
                for (int i = 0; i < 60; i++)
                    writer.SetProject(Documents.Prefix + "large " + i, processor);
                writer.Save();
                Assert.IsNull(writer.LastError, "sanity: the large save written");
                long chars = File.ReadAllText(path).Length;
                Assert.Greater(chars, 3_000_000L, "sanity: a save of megabytes, sixty processors in it");

                using (var recorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame"))
                {
                    yield return null;
                    yield return null;

                    // What Unity's reader makes of it, as the store's read made before it read the save's own text too.
                    ProgressFile read = JsonUtility.FromJson<ProgressFile>(File.ReadAllText(path));
                    long reader = 0;
                    for (int f = 0; f < 3; f++)
                    {
                        yield return null;
                        reader = Math.Max(reader, recorder.LastValue);
                    }

                    var store = new ProgressStore(path);
                    store.Load();
                    long load = 0;
                    for (int f = 0; f < 3; f++)
                    {
                        yield return null;
                        load = Math.Max(load, recorder.LastValue);
                    }

                    Assert.IsTrue(recorder.Valid, "sanity: the editor counts what a frame allocates");
                    Assert.IsNotNull(read, "sanity: Unity's reader read it");
                    Assert.IsNull(store.LastError, "sanity: the store read it");
                    Assert.IsNull(store.FromANewerVersion, "sanity: this BitSorter's own save, nothing in it unknown");
                    Assert.AreEqual(60, store.ProjectKeys().Length, "sanity: every circuit read");
                    Assert.Greater(reader, 2 * chars, "sanity: the counter saw the read -- its text alone is two bytes a character");
                    TestContext.WriteLine($"a save of {chars:N0} characters: Unity's reader made {reader:N0} bytes in its frame, the store's read {load:N0}");
                    Assert.Less(load - reader, 3L * sizeof(char) * chars,
                        $"the store's read of a save of {chars:N0} characters made {load - reader:N0} bytes more than Unity's reader did");
                }
            }
            finally
            {
                try
                {
                    Directory.Delete(folder, true);
                }
                catch (IOException)
                {
                    // A scratch folder of the editor's own; left for the next to clear.
                }
            }
        }
    }
}
