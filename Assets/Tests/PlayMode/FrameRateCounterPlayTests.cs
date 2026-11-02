using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using BitSorter.View;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;
using Object = UnityEngine.Object;

namespace BitSorter.PlayMode.Tests
{
    /// <summary>
    /// The frame-rate counter, which redraws by design, makes no garbage it does not have to.
    /// </summary>
    /// <remarks>
    /// Its own Update is called directly, through a delegate, so the measurement covers that method and nothing else Unity
    /// does in a frame. Until Part 16a this was <c>HudAllocationPlayTests</c>, and held the 4.x game's HUD -- the status
    /// banner, the parts list, the level list, the bits-lost meter -- to drawing nothing on a quiet frame as well.
    /// </remarks>
    [TestFixture]
    public class FrameRateCounterPlayTests
    {
        [OneTimeSetUp]
        public void OneTimeSetup()
        {
            SaveGuard.Redirect();
            GameAnalytics.SetReporting(false);
        }

        [OneTimeTearDown]
        public void OneTimeCleanup()
        {
            SaveGuard.Release();
        }

        [TearDown]
        public void ClearTheSave() => SaveGuard.Clear();

        [UnityTearDown]
        public IEnumerator ClearTheScene()
        {
            yield return TestScene.Clear();
        }

        private static T Find<T>() where T : Object => Object.FindFirstObjectByType<T>();

        /// <summary>
        /// The frame-rate counter allocates nothing counting, and its redraw -- twice a second, by
        /// design -- makes no garbage of its own.
        /// </summary>
        /// <remarks>
        /// One real count first, so TextMeshPro has drawn a number once; then off and on, which
        /// starts the count again from nothing, and the frames of one count are called directly at
        /// this frame's delta.
        ///
        /// **A redraw is allowed two allocations, and only because this is the editor.** The
        /// counter hands TextMeshPro its digits with <c>SetCharArray</c>, which fills a buffer
        /// TextMeshPro reuses -- except that under <c>UNITY_EDITOR</c> it also keeps a string copy
        /// of the text for the inspector, built as a char array and then a string from it
        /// (<c>TMP_Text.InternalTextBackingArrayToString</c>). A player has no such line. So the
        /// counting frames are held to nothing at all, and the redraw to that copy.
        ///
        /// **It measures on a short frame.** Every call adds this frame's length to the count, and
        /// at least one quiet call is made, so on a frame longer than a quarter of the window the
        /// count finished inside the quiet stretch and the counter redrew -- garbage it is allowed
        /// to make, failing a test about frames that make none. And the frame the first count ends
        /// on is more likely than most to be a long one, because a long frame is what pushes a
        /// count over the line. It failed twice running on 2026-10-05, on changes that did not
        /// touch the counter, while the editor drew slowly -- the runs took 177 seconds where they
        /// had taken 150 -- and passed once it waited for a short frame. That run was faster too,
        /// so a long frame is the likely cause and not a proven one; the failure now says how long
        /// the frame was.
        /// </remarks>
        [UnityTest]
        public IEnumerator TheFrameRateCounter_AllocatesNothingCounting_AndOnlyTheEditorsCopyRedrawing()
        {
            yield return TestScene.Load();

            FrameRateCounter counter = Find<FrameRateCounter>();
            FrameRate.SetCounter(true);

            // The counter counts unscaled time, which the clock TestScene drives does not reach:
            // capped in unscaled seconds, generous and loud, never in frames.
            float until = Time.unscaledTime + 30f;

            while (counter.Shown <= 0 && Time.unscaledTime < until)
                yield return null;

            Assert.Greater(counter.Shown, 0, "sanity: the counter never counted");

            for (int frame = 0; frame < 600 && Time.unscaledDeltaTime > FrameRateCounter.Window / 8f; frame++)
                yield return null;

            Assert.LessOrEqual(Time.unscaledDeltaTime, FrameRateCounter.Window / 8f,
                "sanity: the editor never drew a frame short enough to count quietly on");

            Action update = FrameOf(counter);
            FrameRate.SetCounter(false);
            update();
            FrameRate.SetCounter(true);
            update();
            Assert.AreEqual(-1, counter.Shown, "sanity: switching the counter off and on should start its count again");

            float delta = Mathf.Max(Time.unscaledDeltaTime, 1e-4f);
            int quiet = Mathf.Max(1, Mathf.FloorToInt(FrameRateCounter.Window / delta) - 2);

            Assert.That(() =>
            {
                for (int call = 0; call < quiet; call++)
                    update();
            }, Is.Not.AllocatingGCMemory(), $"the counter made garbage counting {quiet} frames of {delta:F4}s");

            Assert.AreEqual(-1, counter.Shown, "sanity: the quiet frames should have stayed inside one count");

            int allocations = AllocationsIn(() =>
            {
                for (int call = 0; call < 8 && counter.Shown < 0; call++)
                    update();
            });

            Assert.Greater(counter.Shown, 0, "sanity: the count should have finished inside the measurement");
            Assert.LessOrEqual(allocations, EditorCopyOfTheText,
                "drawing the number made garbage beyond TextMeshPro's editor-only copy of the text");

            FrameRate.SetCounter(false);
        }

        /// <summary>
        /// What TextMeshPro allocates in the editor alone each time text is set from a char array: a
        /// char array, and a string made from it.
        /// </summary>
        private const int EditorCopyOfTheText = 2;

        /// <summary>How many managed allocations one call makes, on this thread.</summary>
        /// <remarks>What <c>Is.Not.AllocatingGCMemory</c> measures, as a count rather than a yes or no.</remarks>
        private static int AllocationsIn(Action action)
        {
            Recorder recorder = Recorder.Get("GC.Alloc");
            recorder.enabled = false;
            recorder.FilterToCurrentThread();
            recorder.enabled = true;

            try
            {
                action();
            }
            finally
            {
                recorder.enabled = false;
                recorder.CollectFromAllThreads();
            }

            return recorder.sampleBlockCount;
        }

        /// <summary>A component's own Update, callable without Unity.</summary>
        private static Action FrameOf(MonoBehaviour component)
        {
            Assert.IsNotNull(component, "the component is not in the scene");

            MethodInfo update = component.GetType().GetMethod(
                "Update", BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.IsNotNull(update, $"{component.GetType().Name} no longer has an Update to call");

            return (Action)update.CreateDelegate(typeof(Action), component);
        }
    }
}
