using System.Collections;
using System.Reflection;
using NUnit.Framework;
using BitSorter.View;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace BitSorter.PlayMode.Tests
{
    /// <summary>
    /// What happens on the frame a run passes, whatever order Unity happens to update things in.
    /// </summary>
    /// <remarks>
    /// Unity does not define the order in which scripts with no execution order get their Update.
    /// Every test here takes one component's Update out of Unity's hands -- by disabling it, or by
    /// calling it directly -- and checks that the others still agree about what just happened.
    ///
    /// The win panel read the personal best on the frame the run passed, and the tracker recorded
    /// that best in an Update of its own. Whichever ran first decided whether the panel showed this
    /// solve's record or the previous one. The tutorial's ending card had the same shape: it went up
    /// whenever the win panel was not showing, which on the frame a run passes can simply mean the
    /// win panel had not updated yet.
    /// </remarks>
    [TestFixture]
    public class RunEndPlayTests
    {
        private const string Level = "route-the-bit";

        private static readonly Vector2Int Middle = new Vector2Int(0, 0);

        [OneTimeSetUp]
        public void OneTimeSetup()
        {
            // Redirect first: SetReporting writes to the real machine otherwise.
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

        // -----------------------------------------------------------------
        // The record
        // -----------------------------------------------------------------

        /// <summary>
        /// By the time anything can see a run has passed, the solve is already on record.
        /// </summary>
        /// <remarks>
        /// The session is disabled so the test, not Unity, decides when it looks at the run, and its
        /// own Update is then called directly -- that call is the frame the run passes. Nothing else
        /// gets an Update in between, so whatever the tracker has recorded by the end of it is all a
        /// win panel updating next in the same frame could ever have seen.
        /// </remarks>
        [UnityTest]
        public IEnumerator ASolveIsRecordedBeforeAnythingCanSeeThePass()
        {
            yield return TestScene.Load();

            LevelSession session = Find<LevelSession>();
            SimulationRunner runner = Find<SimulationRunner>();
            ProgressTracker progress = Find<ProgressTracker>();

            Assert.IsTrue(session.LoadLevel(Level), "the level did not load");
            yield return null;

            // The intended answer: in -> NOT -> binOne.
            Assert.IsTrue(session.TryPlaceGate(GateKind.Not, Middle), "could not place the NOT gate");
            Wire(session, runner.FixtureNodeIds["in"], NodeOn(runner, Middle));
            Wire(session, NodeOn(runner, Middle), runner.FixtureNodeIds["binOne"]);

            session.enabled = false;
            session.Run();
            RunToAStandstill(runner);

            Assert.AreEqual(RunState.Running, session.State,
                "sanity: nothing should have settled the run while the session was disabled");
            Assert.IsFalse(progress.IsComplete(Level),
                "sanity: the scratch save should start with this level unsolved");

            SessionFrame(session);

            Assert.AreEqual(RunState.Passed, session.State, "sanity: the intended answer should pass");

            Assert.IsTrue(progress.IsComplete(Level),
                "the run has passed but the solve is not recorded yet -- a win panel updating before " +
                "the tracker would show the previous record");
            Assert.AreEqual(1, progress.BestGates(Level), "one NOT gate is the whole circuit");
            Assert.AreEqual(2, progress.BestLatency(Level),
                "two delay-1 wires: the bit reaches the bin two ticks after it was emitted");

            session.enabled = true;
        }

        // -----------------------------------------------------------------
        // The tutorial's ending
        // -----------------------------------------------------------------

        /// <summary>
        /// The ending card waits until the solved panel has been shown and dismissed.
        /// </summary>
        /// <remarks>
        /// The win panel is disabled across the pass, which is the order in which it updates after
        /// the director, stretched over several frames so the director has every chance to jump the
        /// queue. The second half is the positive control: with the win panel back, the card does
        /// appear once that panel is dismissed, so the first half cannot pass by the card never
        /// coming up at all.
        /// </remarks>
        [UnityTest]
        public IEnumerator TheEndingCardWaitsForTheSolvedPanel()
        {
            yield return TestScene.Load();

            LevelSession session = Find<LevelSession>();
            SimulationRunner runner = Find<SimulationRunner>();
            TutorialDirector director = Find<TutorialDirector>();
            TutorialPanel panel = Find<TutorialPanel>();
            TutorialCard card = Find<TutorialCard>();
            WinPanel win = Find<WinPanel>();
            PlacementController placement = Find<PlacementController>();

            // The director waits behind the main menu, which is open at boot.
            Find<MainMenu>().Show(false);

            director.Begin();
            yield return null;
            yield return null;

            panel.Next();
            yield return null;
            yield return null;

            Assert.AreEqual(0, director.CurrentStep, "sanity: the tutorial should be on its first step");

            // Every step, done through the same entry points the clicks use.
            Assert.IsTrue(placement.TrySelect(TutorialLevel.Part), "could not pick up the NOT gate");
            Assert.IsTrue(session.TryPlaceGate(TutorialLevel.Part, TutorialLevel.GateCell),
                "could not place the NOT gate");

            int gate = NodeOn(runner, TutorialLevel.GateCell);
            Wire(session, runner.FixtureNodeIds[TutorialLevel.SourceId], gate);
            Wire(session, gate, runner.FixtureNodeIds[TutorialLevel.SinkId]);
            yield return null;

            win.enabled = false;

            session.Run();
            RunToAStandstill(runner);

            for (int frame = 0; frame < 5; frame++)
                yield return null;

            Assert.AreEqual(RunState.Passed, session.State, "sanity: the tutorial's answer should pass");
            Assert.AreEqual(TutorialScript.Count, director.CurrentStep,
                "sanity: every step should read as done");

            Assert.IsFalse(card.IsShowing,
                "the ending card went up before the solved panel had had its turn");

            // Positive control.
            win.enabled = true;
            yield return null;
            yield return null;

            Assert.IsTrue(win.IsShowing, "the solved panel should present once it gets an Update");
            Assert.IsFalse(card.IsShowing,
                "the ending card must not share the screen with the solved panel");

            // At the end of the tutorial the solved panel's one way on is CONTINUE, which
            // dismisses it; KEEP TINKERING would have led to the same card, so it is not offered.
            Button onward = WayOnButton();
            Assert.IsNotNull(onward, "could not find the solved panel's CONTINUE button");

            onward.onClick.Invoke();
            yield return null;
            yield return null;

            Assert.IsFalse(win.IsShowing, "CONTINUE should have dismissed the solved panel");
            Assert.IsTrue(card.IsShowing,
                "with the solved panel dismissed, the ending card should have come up");
        }

        // -----------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------

        /// <summary>The node sitting on a cell in the current build, or -1.</summary>
        private static int NodeOn(SimulationRunner runner, Vector2Int cell)
        {
            for (int id = 0; id < runner.View.NodeCount; id++)
            {
                if (runner.TryCellOf(id, out Vector2Int at) && at == cell)
                    return id;
            }

            Assert.Fail($"nothing on {cell}");
            return -1;
        }

        private static void Wire(LevelSession session, int fromNode, int toNode)
        {
            Assert.IsTrue(
                session.TryConnect(
                    new PortAddress(fromNode, false, 0),
                    new PortAddress(toNode, true, 0)),
                $"could not wire node {fromNode} to node {toNode}");
        }

        /// <summary>Ticks the clock by hand until the run cannot change any more.</summary>
        private static void RunToAStandstill(SimulationRunner runner)
        {
            for (int tick = 0; tick < 100 && !runner.IsIdle(); tick++)
                runner.StepOneTick();

            Assert.IsTrue(runner.IsIdle(), "the run never came to a standstill");
        }

        /// <summary>One of the session's own frames, called directly rather than by Unity.</summary>
        private static void SessionFrame(LevelSession session)
        {
            MethodInfo update = typeof(LevelSession).GetMethod(
                "Update", BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.IsNotNull(update, "LevelSession no longer has an Update to call");
            update.Invoke(session, null);
        }

        /// <summary>The solved panel's way on, found by the names WinPanel builds it with.</summary>
        private static Button WayOnButton()
        {
            foreach (Button button in Object.FindObjectsByType<Button>(FindObjectsSortMode.None))
            {
                Transform parent = button.transform.parent;

                if (button.name == "Next" && parent != null && parent.name == "Win")
                    return button;
            }

            return null;
        }
    }
}
