using System.Collections;
using System.IO;
using NUnit.Framework;
using BitSorter.LogicCore;
using BitSorter.View;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BitSorter.PlayMode.Tests
{
    /// <summary>
    /// Screenshots of the game in a fixed set of states, for judging how it looks.
    /// </summary>
    /// <remarks>
    /// Not a test of behaviour, and <see cref="ExplicitAttribute"/> so it never runs as part of the
    /// suite: it runs when asked for by name, from <c>BitSorter/Capture Reference Shots</c>.
    ///
    /// It is a Play Mode fixture rather than a menu item that drives the game, because driving the
    /// game is playing it. Loading a level, placing gates and running a circuit go through
    /// <see cref="ProgressTracker"/> exactly as a player's would -- and the first screenshots of
    /// this redesign were taken that way, by hand, and marked half-adder solved on the
    /// developer's own save with a stranger's circuit as its best. A fixture gets
    /// <see cref="SaveGuard"/>, analytics switched off and a clean scene for nothing.
    ///
    /// Every frame is exactly <see cref="FrameSeconds"/> of game time, so each shot lands on the
    /// same animation phase however fast the editor happens to render. That is what makes two
    /// captures comparable -- the same board before and after a change, or the same moment under
    /// three different looks -- rather than two pictures of whenever the clock had got to.
    ///
    /// Each shot also asserts the moment it claims to show. A collision screenshot of a board
    /// with nothing about to collide is the failure this guards against: it would look fine, and
    /// it would be wrong.
    ///
    /// Needs a Game view, so it runs in the editor proper and not in batch mode, where there is
    /// no view for a screen capture to read.
    /// </remarks>
    [TestFixture, Explicit("Captures reference screenshots. Run on purpose, never as part of the suite.")]
    public class ReferenceShots
    {
        /// <summary>Game time per frame while capturing.</summary>
        private const float FrameSeconds = 1f / 120f;

        /// <summary>The level most shots are staged on: two sources, two sinks, two gates.</summary>
        private const string Level = "half-adder";

        private static readonly Vector2Int High = new Vector2Int(0, 1);
        private static readonly Vector2Int Low = new Vector2Int(0, -1);

        /// <summary>Where the pictures go. Outside the repository, beside Unity's own test results.</summary>
        public static string Folder
        {
            get
            {
                string look = "current";
#if UNITY_EDITOR
                look = UnityEditor.SessionState.GetString("BitSorter.Capture.Look", "current");
#endif
                return Path.Combine(Application.persistentDataPath, "Captures", look);
            }
        }

        [OneTimeSetUp]
        public void OneTimeSetup()
        {
            SaveGuard.Redirect();
            GameAnalytics.SetReporting(false);
            Directory.CreateDirectory(Folder);
        }

        [OneTimeTearDown]
        public void OneTimeCleanup()
        {
            Time.captureDeltaTime = 0f;
            ViewTime.Pinned = null;
            SaveGuard.Release();
        }

        /// <summary>
        /// The moment every ambient pulse is shown at: the top of a collision warning's throb, so
        /// the collision shot shows the warning at its clearest.
        /// </summary>
        private static float PinnedMoment => 1f / (4f * PortState.WarningHz);

        [SetUp]
        public void FixTheClock()
        {
            Time.captureDeltaTime = FrameSeconds;

            // The game clock at capture depends on how long the scene took to load, so the pulses
            // that follow it -- the grid's shimmer, a warning's throb -- landed somewhere different
            // in every run. Pinned, two captures of the same code are the same picture.
            ViewTime.Pinned = PinnedMoment;
        }

        [TearDown]
        public void ClearTheSave()
        {
            Time.captureDeltaTime = 0f;
            ViewTime.Pinned = null;
            SaveGuard.Clear();
        }

        [UnityTearDown]
        public IEnumerator ClearTheScene()
        {
            yield return TestScene.Clear();
        }

        private static T Find<T>() where T : Object => Object.FindFirstObjectByType<T>();

        // -----------------------------------------------------------------
        // The shots
        // -----------------------------------------------------------------

        /// <summary>The first thing anyone sees.</summary>
        [UnityTest]
        public IEnumerator Shot01_MainMenu()
        {
            yield return TestScene.Load();
            yield return Frames(30);

            Assert.IsTrue(UiModal.AnyOpen, "sanity: the game boots into the main menu");

            yield return Capture("01-menu");
        }

        /// <summary>A finished circuit that has not been run: the parts list, the controls, the board.</summary>
        [UnityTest]
        public IEnumerator Shot02_Building()
        {
            yield return OpenOnTheBoard();
            yield return BuildTheHalfAdder(wireEverything: true);
            yield return Frames(30);

            yield return Capture("02-building");
        }

        /// <summary>Bits part-way along their wires, on both stages of the circuit.</summary>
        [UnityTest]
        public IEnumerator Shot03_Running()
        {
            yield return OpenOnTheBoard();
            yield return BuildTheHalfAdder(wireEverything: true);

            Find<LevelSession>().Run();
            yield return UntilTick(2);
            yield return HalfATick();

            Assert.Greater(BitsInFlight(), 0, "the run shot has no bits in flight");

            yield return Capture("03-running");
        }

        /// <summary>
        /// A bit one tick from an occupied port: the warning throb on port, wire and bit at once.
        /// </summary>
        /// <remarks>
        /// An AND with only one input wired. The first bit lands and waits, because the gate has
        /// nothing on its other input; the second is on the wire behind it with nowhere to go.
        /// </remarks>
        [UnityTest]
        public IEnumerator Shot04_CollisionOneTickOut()
        {
            yield return OpenOnTheBoard();

            LevelSession session = Find<LevelSession>();
            SimulationRunner runner = Find<SimulationRunner>();

            Assert.IsTrue(session.TryPlaceGate(GateKind.And, Low), "could not place the AND");
            int and = NodeOn(runner, Low);
            Wire(session, runner.FixtureNodeIds["a"], and, 0);
            Wire(session, and, runner.FixtureNodeIds["carry"], 0);

            session.Run();

            // Advanced until the warning is up rather than to a tick worked out by hand, then held
            // half a tick so the throb is caught between beats.
            for (int frame = 0; frame < 2000 && !AnyCollisionImminent(runner); frame++)
                yield return null;

            Assert.IsTrue(AnyCollisionImminent(runner), "nothing on the board is about to collide");

            yield return HalfATick();

            Assert.IsTrue(AnyCollisionImminent(runner),
                "the collision warning was gone half a tick later, so the shot would show nothing");

            yield return Capture("04-collision");
        }

        /// <summary>The solved card over the board it was solved on.</summary>
        [UnityTest]
        public IEnumerator Shot05_Solved()
        {
            yield return OpenOnTheBoard();
            yield return BuildTheHalfAdder(wireEverything: true);

            LevelSession session = Find<LevelSession>();
            SimulationRunner runner = Find<SimulationRunner>();

            session.Run();

            for (int tick = 0; tick < 100 && !runner.IsIdle(); tick++)
                runner.StepOneTick();

            yield return Frames(30);

            Assert.AreEqual(RunState.Passed, session.State, "sanity: the half adder should pass");
            Assert.IsTrue(Find<WinPanel>().IsShowing, "sanity: the solved card should be up");

            yield return Capture("05-solved");
        }

        /// <summary>The level list, with both chapters and a few levels already solved.</summary>
        [UnityTest]
        public IEnumerator Shot06_LevelList()
        {
            yield return OpenOnTheBoard();

            // Solved on the scratch save, so the list shows both kinds of row.
            ProgressStore store = Find<ProgressTracker>().Store;
            foreach (string level in new[] { "route-the-bit", "balance-the-paths", "the-long-way-round" })
                store.MarkComplete(level);

            Find<LevelSelectPanel>().Open();
            yield return Frames(30);

            Assert.IsTrue(Find<LevelSelectPanel>().IsShowing, "sanity: the list should be open");

            yield return Capture("06-levels");
        }

        /// <summary>Free play, with its setup panel docked down the right.</summary>
        [UnityTest]
        public IEnumerator Shot07_FreePlay()
        {
            yield return OpenOnTheBoard();

            Find<SandboxPanel>().Open();
            yield return Frames(30);

            Assert.AreEqual(SandboxLevel.Key, Find<LevelSession>().LevelName, "sanity: free play did not load");

            yield return Capture("07-freeplay");
        }

        // -----------------------------------------------------------------
        // Staging
        // -----------------------------------------------------------------

        /// <summary>
        /// Past the menu and onto <see cref="Level"/>, with the tutorial marked as done.
        /// </summary>
        /// <remarks>
        /// SaveGuard hands every test a fresh save, and a fresh save is exactly what the tutorial
        /// offers itself on -- it would adopt its own board the moment the menu closed.
        /// </remarks>
        private static IEnumerator OpenOnTheBoard()
        {
            yield return TestScene.Load();
            FixTheSparks();

            Find<ProgressTracker>().Store.MarkMilestone(TutorialLevel.Key);
            Find<MainMenu>().Show(false);

            LevelSession session = Find<LevelSession>();
            SimulationRunner runner = Find<SimulationRunner>();

            Assert.IsTrue(session.LoadLevel(Level), $"{Level} did not load");

            for (int frame = 0; frame < 60 && !runner.FixtureNodeIds.ContainsKey("a"); frame++)
                yield return null;

            Assert.IsTrue(runner.FixtureNodeIds.ContainsKey("a"), "the level loaded but was never built");
        }

        /// <summary>
        /// Gives every particle system the same seed, so a spark flies the same way in every run.
        /// </summary>
        /// <remarks>
        /// A particle system seeds itself afresh each time it plays unless told otherwise, so two
        /// captures of unchanged code differed by up to 129 levels in every shot with sparks in it.
        /// Fixing the frame time made everything else repeatable; this makes the sparks repeatable
        /// too, which is what lets a before-and-after diff mean something. The game keeps its
        /// random sparks -- only the pictures need to repeat.
        /// </remarks>
        private static void FixTheSparks()
        {
            foreach (ParticleSystem system in Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None))
            {
                // A seed may only be set on a system that is stopped and empty.
                system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                system.useAutoRandomSeed = false;
                system.randomSeed = 20260922;
            }
        }

        /// <summary>An XOR for the sum and an AND for the carry, each fed by both sources.</summary>
        private static IEnumerator BuildTheHalfAdder(bool wireEverything)
        {
            LevelSession session = Find<LevelSession>();
            SimulationRunner runner = Find<SimulationRunner>();

            Assert.IsTrue(session.TryPlaceGate(GateKind.Xor, High), "could not place the XOR");
            Assert.IsTrue(session.TryPlaceGate(GateKind.And, Low), "could not place the AND");

            int xor = NodeOn(runner, High);
            int and = NodeOn(runner, Low);
            int a = runner.FixtureNodeIds["a"];
            int b = runner.FixtureNodeIds["b"];

            Wire(session, a, xor, 0);
            Wire(session, b, xor, 1);
            Wire(session, a, and, 0);
            Wire(session, b, and, 1);

            if (wireEverything)
            {
                Wire(session, xor, runner.FixtureNodeIds["sum"], 0);
                Wire(session, and, runner.FixtureNodeIds["carry"], 0);
            }

            yield return null;
        }

        // -----------------------------------------------------------------
        // Time
        // -----------------------------------------------------------------

        private static IEnumerator Frames(int count)
        {
            for (int i = 0; i < count; i++)
                yield return null;
        }

        /// <summary>Until the run has executed this many ticks, with a cap that fails loudly.</summary>
        private static IEnumerator UntilTick(int tick)
        {
            SimulationRunner runner = Find<SimulationRunner>();

            for (int frame = 0; frame < 2000 && runner.View.CurrentTick < tick; frame++)
                yield return null;

            Assert.GreaterOrEqual(runner.View.CurrentTick, tick, $"the run never reached tick {tick}");
        }

        /// <summary>
        /// On to the middle of the current tick, so bits sit part-way along their wires.
        /// </summary>
        /// <remarks>
        /// Waits on the runner's own progress rather than counting frames against an interval
        /// written down here, which would be a second copy of the runner's number. Stops at the
        /// tick boundary rather than wrapping into the next tick.
        /// </remarks>
        private static IEnumerator HalfATick()
        {
            SimulationRunner runner = Find<SimulationRunner>();
            int tick = runner.View.CurrentTick;

            for (int frame = 0; frame < 2000 && runner.TickProgress < 0.5f; frame++)
            {
                yield return null;

                Assert.AreEqual(tick, runner.View.CurrentTick,
                    "a whole tick went by while waiting for the middle of one");
            }

            Assert.GreaterOrEqual(runner.TickProgress, 0.5f, "the run never reached the middle of a tick");
        }

        // -----------------------------------------------------------------
        // Capture
        // -----------------------------------------------------------------

        /// <summary>Writes the Game view to a file and waits until it is really there.</summary>
        /// <remarks>
        /// A capture is written at the end of the frame, asynchronously, so the file is waited on
        /// rather than assumed. A screenshot that silently did not happen is the failure worth
        /// catching here: every other state in this fixture is checked, and a missing picture would
        /// otherwise read as a successful run.
        /// </remarks>
        private static IEnumerator Capture(string name)
        {
            string path = Path.Combine(Folder, name + ".png");

            if (File.Exists(path))
                File.Delete(path);

            ScreenCapture.CaptureScreenshot(path);

            for (int frame = 0; frame < 120 && !(File.Exists(path) && new FileInfo(path).Length > 0); frame++)
                yield return null;

            Assert.IsTrue(File.Exists(path) && new FileInfo(path).Length > 0,
                $"the screenshot '{name}' was never written -- is there a Game view to capture?");

            Debug.Log($"[ReferenceShots] {path}");
        }

        // -----------------------------------------------------------------
        // Reading the board
        // -----------------------------------------------------------------

        private static int BitsInFlight()
        {
            SimulationRunner runner = Find<SimulationRunner>();
            int count = 0;

            for (int id = 0; id < runner.View.EdgeCount; id++)
            {
                Edge edge = runner.View.GetEdge(id);

                if (edge != null)
                    count += edge.InTransitCount;
            }

            return count;
        }

        private static bool AnyCollisionImminent(SimulationRunner runner)
        {
            for (int id = 0; id < runner.View.EdgeCount; id++)
            {
                Edge edge = runner.View.GetEdge(id);

                if (edge != null && PortState.WillCollide(edge, out bool _))
                    return true;
            }

            return false;
        }

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

        private static void Wire(LevelSession session, int from, int to, int toPort)
        {
            Assert.IsTrue(
                session.TryConnect(new PortAddress(from, false, 0), new PortAddress(to, true, toPort)),
                $"could not wire {from} to {to}");
        }
    }
}
