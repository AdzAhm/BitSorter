using System.Collections;
using BitSorter.View;
using BitSorter.ViewModel;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BitSorter.PlayMode.Tests
{
    /// <summary>
    /// Drawing only when something changes (5.1): with nothing happening for a second the window is drawn one frame in four,
    /// and a key or PLAY draws every frame again at once. A desktop build's alone, so these turn it on themselves
    /// (<see cref="WorkbenchPanel.ThrottlesWhenIdle"/>); the editor, every other test and the reference shots draw every frame.
    /// The second counted is real time, which the tests' driven clock does not touch, so the waits cap in real seconds.
    /// </summary>
    [TestFixture]
    public class WorkbenchIdlePlayTests : InputTestFixture
    {
        private Keyboard _keyboard;

        [OneTimeSetUp]
        public void OneTimeSetup()
        {
            SaveGuard.Redirect();
            GameAnalytics.SetReporting(false);
        }

        [OneTimeTearDown]
        public void OneTimeCleanup() => SaveGuard.Release();

        public override void Setup()
        {
            base.Setup();
            InputSystem.settings.SetInternalFeatureFlag("USE_READ_VALUE_CACHING", false);
            InputSystem.runInBackground = true;   // as the player runs (Run In Background): a click is still taken with the editor behind another window
            _keyboard = InputSystem.AddDevice<Keyboard>();
            InputSystem.AddDevice<Mouse>().MakeCurrent();
            WorkbenchPanel.ThrottlesWhenIdle = true;
        }

        public override void TearDown()
        {
            WorkbenchPanel.ThrottlesWhenIdle = false;   // as the editor has it
            SaveGuard.Clear();
            base.TearDown();
        }

        [UnityTearDown]
        public IEnumerator ClearTheScene()
        {
            yield return TestScene.Clear();
            Assert.AreEqual(1, OnDemandRendering.renderFrameInterval, "the workbench gone, every frame is drawn again");
            Assert.IsFalse(FrameRate.Idle, "and run at the player's own rate");
        }

        private static T Find<T>() where T : Object => Object.FindFirstObjectByType<T>();

        /// <summary>The reference circuit, paused and its run made: nothing left to happen.</summary>
        private static IEnumerator Still()
        {
            yield return TestScene.Load();
            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            workbench.Pause();
            workbench.Session.Finish();
            yield return null;
        }

        /// <summary>Frames until one in four is drawn, capped at <paramref name="seconds"/> of real time.</summary>
        private static IEnumerator UntilIdle(WorkbenchPanel workbench, double seconds)
        {
            double until = Time.unscaledTimeAsDouble + seconds;
            while (workbench.DrawInterval != IdleRendering.IdleInterval && Time.unscaledTimeAsDouble < until)
                yield return null;
        }

        [UnityTest]
        public IEnumerator StillForASecond_OneFrameInTenIsDrawn_AndAKeyDrawsEveryFrameAtOnce()
        {
            yield return Still();
            WorkbenchPanel workbench = Find<WorkbenchPanel>();

            yield return UntilIdle(workbench, 5.0);
            Assert.AreEqual(IdleRendering.IdleInterval, workbench.DrawInterval, "a still second: one frame in four");
            Assert.AreEqual(IdleRendering.IdleInterval, OnDemandRendering.renderFrameInterval, "and Unity told so");
            Assert.IsTrue(FrameRate.Idle, "and the loop held to twenty a second, where the settings apply (5.1)");

            Press(_keyboard.shiftKey);
            yield return null;
            Assert.AreEqual(1, workbench.DrawInterval, "a key down: every frame drawn at once");
            Assert.AreEqual(1, OnDemandRendering.renderFrameInterval);
            Assert.IsFalse(FrameRate.Idle, "at the player's own rate at once");
            Release(_keyboard.shiftKey);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Playing_DrawsEveryFrame_AsLongAsItPlays()
        {
            yield return Still();
            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            yield return UntilIdle(workbench, 5.0);
            Assert.AreEqual(IdleRendering.IdleInterval, workbench.DrawInterval, "sanity: idle first");

            workbench.Restart();   // plays from the start, as RESTART does
            yield return null;
            Assert.IsTrue(workbench.Playing, "sanity: playing");
            Assert.AreEqual(1, workbench.DrawInterval, "PLAY draws every frame at once");
            Assert.IsFalse(FrameRate.Idle, "at the player's own rate");

            double until = Time.unscaledTimeAsDouble + 1.5;   // past the second it would linger anyway
            while (Time.unscaledTimeAsDouble < until && workbench.Playing)
            {
                Assert.AreEqual(1, workbench.DrawInterval, "every frame while it plays");
                yield return null;
            }
        }

        /// <summary>Off, as in the editor and a browser: every frame drawn, however still.</summary>
        [UnityTest]
        public IEnumerator Off_EveryFrameIsDrawn()
        {
            WorkbenchPanel.ThrottlesWhenIdle = false;
            yield return Still();
            double until = Time.unscaledTimeAsDouble + 1.5;
            while (Time.unscaledTimeAsDouble < until)
            {
                Assert.AreEqual(1, OnDemandRendering.renderFrameInterval);
                Assert.IsFalse(FrameRate.Idle, "nor run fewer");
                yield return null;
            }
        }
    }
}
