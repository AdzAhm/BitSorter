using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using BitSorter.View;
using UnityEngine;
using UnityEngine.TestTools;

namespace BitSorter.PlayMode.Tests
{
    /// <summary>
    /// The runner says when it ticks and when it rebuilds, from inside the call that does it.
    /// </summary>
    /// <remarks>
    /// The timing diagram records what happened on every tick, and a tick's facts are gone by the
    /// next one -- so a tick the runner ran without saying so is a hole in the diagram, and one it
    /// reported twice is a bit drawn twice. The timed loop can run several ticks in one frame, and
    /// that is the case a once-a-frame poll would get wrong, so the first test makes sure it happens.
    /// </remarks>
    [TestFixture]
    public class SimulationRunnerPlayTests
    {
        /// <summary>Eight vectors and no clock, so a run is eight ticks of sources at least.</summary>
        private const string Level = "carry-the-one";

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

        private static IEnumerator OnTheBoard()
        {
            yield return TestScene.Load();

            Find<MainMenu>().Show(false);
            yield return null;

            Assert.IsTrue(Find<LevelSession>().LoadLevel(Level), "the level did not load");
            yield return null;
        }

        /// <summary>
        /// Every tick of a run is reported once, in order, including when the timed loop runs
        /// several of them in one frame.
        /// </summary>
        [UnityTest]
        public IEnumerator EveryTick_IsReportedOnceAndInOrder_EvenSeveralToAFrame()
        {
            yield return OnTheBoard();

            LevelSession session = Find<LevelSession>();
            SimulationRunner runner = Find<SimulationRunner>();

            var ticks = new List<int>();
            var frames = new List<int>();

            void Heard(int tick)
            {
                ticks.Add(tick);
                frames.Add(Time.frameCount);
            }

            runner.Ticked += Heard;

            try
            {
                session.Run();

                // Far faster than any frame, so the loop has to catch up several ticks at a time.
                runner.Speed = 400;

                for (int frame = 0; frame < 600 && session.State == RunState.Running; frame++)
                    yield return null;
            }
            finally
            {
                runner.Ticked -= Heard;
            }

            Assert.AreNotEqual(RunState.Running, session.State, "sanity: the run never ended");
            Assert.Greater(ticks.Count, 1, "sanity: the run reported no ticks");

            for (int i = 0; i < ticks.Count; i++)
                Assert.AreEqual(i, ticks[i], $"tick {i} was reported as {ticks[i]}");

            Assert.AreEqual(runner.View.CurrentTick, ticks.Count, "a tick the runner ran was never reported");

            bool severalInOneFrame = false;

            for (int i = 1; i < frames.Count; i++)
                severalInOneFrame |= frames[i] == frames[i - 1];

            Assert.IsTrue(severalInOneFrame,
                "sanity: no frame ran two ticks, so this test never saw the case it is about");
        }

        /// <summary>A step is reported as it is taken, however many are taken in one frame.</summary>
        [UnityTest]
        public IEnumerator EachStep_IsReportedAsItIsTaken()
        {
            yield return OnTheBoard();

            LevelSession session = Find<LevelSession>();
            SimulationRunner runner = Find<SimulationRunner>();

            session.Run();
            runner.SetPaused(true);
            yield return null;

            var ticks = new List<int>();
            void Heard(int tick) => ticks.Add(tick);

            int first = runner.View.CurrentTick;
            runner.Ticked += Heard;

            try
            {
                runner.StepOneTick();
                Assert.AreEqual(new[] { first }, ticks.ToArray(), "the step was not reported before it returned");

                runner.StepOneTick();
                runner.StepOneTick();
            }
            finally
            {
                runner.Ticked -= Heard;
            }

            Assert.AreEqual(new[] { first, first + 1, first + 2 }, ticks.ToArray());
        }

        /// <summary>
        /// A rebuild is reported from inside the call, by which time the new graph is in place at
        /// tick 0 -- on RUN and on an edit alike.
        /// </summary>
        [UnityTest]
        public IEnumerator ARebuild_IsReportedWithTheNewGraphAtTickZero()
        {
            yield return OnTheBoard();

            LevelSession session = Find<LevelSession>();
            SimulationRunner runner = Find<SimulationRunner>();

            int heard = 0;

            void Heard()
            {
                heard++;
                Assert.AreEqual(0, runner.View.CurrentTick, "the rebuild was reported before the new graph was in");
            }

            runner.Rebuilt += Heard;

            try
            {
                int before = runner.GraphRevision;
                Assert.IsTrue(session.TryPlaceGate(session.Level.Budget[0].Kind, new Vector2Int(0, 0)),
                    "sanity: could not place a gate");

                Assert.Greater(heard, 0, "an edit rebuilt the graph without saying so");
                Assert.Greater(runner.GraphRevision, before, "sanity: the edit did not rebuild");

                heard = 0;
                session.Run();
                Assert.Greater(heard, 0, "RUN rebuilt the graph without saying so");
            }
            finally
            {
                runner.Rebuilt -= Heard;
            }

            yield return null;
        }
    }
}
