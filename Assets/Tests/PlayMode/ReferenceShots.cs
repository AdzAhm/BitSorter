using System.Collections;
using System.IO;
using NUnit.Framework;
using BitSorter.LogicCore;
using BitSorter.View;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using UnityEngine.UI;
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
    ///
    /// An <see cref="InputTestFixture"/>, so the real mouse and keyboard are not in the picture.
    /// The game reads whatever pointer there is, and so did the capture: a click on the Game view
    /// during a run put a refusal toast into two shots, and a pointer resting over the help badge
    /// tinted it in two more -- both from someone using the editor while it ran.
    /// </remarks>
    [TestFixture, Explicit("Captures reference screenshots. Run on purpose, never as part of the suite.")]
    public class ReferenceShots : InputTestFixture
    {
        /// <summary>Game time per frame while capturing.</summary>
        private const float FrameSeconds = 1f / 120f;

        /// <summary>The level most shots are staged on: two sources, two sinks, two gates.</summary>
        private const string Level = "half-adder";

        private static readonly Vector2Int High = new Vector2Int(0, 1);
        private static readonly Vector2Int Low = new Vector2Int(0, -1);

        /// <summary>
        /// The look asked for, by <see cref="Look.Name"/>. "current" means whatever the game draws
        /// in now, and names the folder for a capture that is not about a look at all.
        /// </summary>
        private static string LookName
        {
            get
            {
#if UNITY_EDITOR
                return UnityEditor.SessionState.GetString("BitSorter.Capture.Look", "current");
#else
                return "current";
#endif
            }
        }

        /// <summary>Where the pictures go. Outside the repository, beside Unity's own test results.</summary>
        public static string Folder => Path.Combine(Application.persistentDataPath, "Captures", LookName);

        [OneTimeSetUp]
        public void OneTimeSetup()
        {
            SaveGuard.Redirect();
            GameAnalytics.SetReporting(false);
            Directory.CreateDirectory(Folder);

            // Chosen before any scene loads, because renderers take their colours when they build.
            // A name that matches nothing fails here rather than capturing the game as it is into a
            // folder that says otherwise.
            if (LookName != "current")
            {
                Look look = Look.Named(LookName);
                Assert.IsNotNull(look, $"there is no look called '{LookName}'");
                Look.Use(look);
            }
        }

        [OneTimeTearDown]
        public void OneTimeCleanup()
        {
            Time.captureDeltaTime = 0f;
            ViewTime.Pinned = null;
            Look.Use(null);
            SaveGuard.Release();
        }

        /// <summary>The real scene, with the board's bloom set for the look being captured.</summary>
        private static IEnumerator LoadTheGame()
        {
            yield return TestScene.Load();
            Look.ApplyBloom();
        }

        /// <summary>
        /// The moment every ambient pulse is shown at: the top of a collision warning's throb, so
        /// the collision shot shows the warning at its clearest.
        /// </summary>
        private static float PinnedMoment => 1f / (4f * PortState.WarningHz);

        /// <summary>A keyboard and a mouse of the fixture's own, the mouse parked off the screen.</summary>
        /// <remarks>
        /// Off the screen rather than at the origin a new mouse starts at, which is the corner of
        /// the Game view and on the board: a part in hand draws its ghost under the pointer.
        /// </remarks>
        public override void Setup()
        {
            base.Setup();

            // As PanelPlayTests explains: InputTestFixture turns on a read-value cache and its
            // self-check, which the game itself never runs with.
            InputSystem.settings.SetInternalFeatureFlag("USE_READ_VALUE_CACHING", false);

            InputSystem.AddDevice<Keyboard>();
            Mouse mouse = InputSystem.AddDevice<Mouse>();
            Set(mouse.position, new Vector2(-1000f, -1000f));
        }

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
            yield return LoadTheGame();
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

        /// <summary>The help panel, open over a built board: the hint and the level's truth table.</summary>
        [UnityTest]
        public IEnumerator Shot08_Help()
        {
            yield return OpenOnTheBoard();
            yield return BuildTheHalfAdder(wireEverything: true);

            Press("Help badge");
            yield return Frames(30);

            Assert.IsTrue(IsUp("Help"), "sanity: the help panel should be open");

            yield return Capture("08-help");
        }

        /// <summary>The chapter card, on the first level whose parts list holds a register.</summary>
        [UnityTest]
        public IEnumerator Shot09_ChapterCard()
        {
            yield return OpenOnTheBoard();
            yield return LoadAndBuild(FirstRegisterLevel);
            yield return Frames(30);

            Assert.IsTrue(Find<ChapterCard>().IsShowing, "sanity: the chapter card should be up");

            yield return Capture("09-chapter");
        }

        /// <summary>A clocked level being built: the register in the parts list, and the clock's pips.</summary>
        [UnityTest]
        public IEnumerator Shot10_Clocked()
        {
            yield return OpenOnTheBoard();
            Find<ProgressTracker>().Store.MarkMilestone(ChapterCard.Milestone);

            yield return LoadAndBuild(ClockedLevel);
            yield return Frames(30);

            Assert.IsFalse(Find<ChapterCard>().IsShowing, "sanity: the chapter card should have been seen");
            Assert.IsTrue(IsUp("Clock"), "sanity: a clocked level shows its clock");

            yield return Capture("10-clocked");
        }

        /// <summary>The tutorial's intro strip, holding the board until START or SKIP.</summary>
        /// <remarks>
        /// On a save with no tutorial milestone, closing the menu starts the tutorial by itself --
        /// which is the way almost every player first meets it.
        /// </remarks>
        [UnityTest]
        public IEnumerator Shot11_TutorialIntro()
        {
            yield return LoadTheGame();
            FixTheSparks();
            Find<MainMenu>().Show(false);

            for (int frame = 0; frame < 120 && !TutorialDirector.HoldingTheBoard; frame++)
                yield return null;

            Assert.IsTrue(TutorialDirector.HoldingTheBoard, "sanity: the tutorial should be on its intro");

            yield return Frames(30);
            yield return Capture("11-tutorial");
        }

        /// <summary>The card the tutorial ends on: every control, in two columns.</summary>
        [UnityTest]
        public IEnumerator Shot12_TutorialCard()
        {
            yield return OpenOnTheBoard();

            Find<TutorialCard>().Show(true);
            yield return Frames(30);

            Assert.IsTrue(Find<TutorialCard>().IsShowing, "sanity: the tutorial's card should be up");

            yield return Capture("12-tutorial-card");
        }

        /// <summary>
        /// A first-time hint, on the row under the banner, raised by what the board did.
        /// </summary>
        /// <remarks>
        /// The board from the collision shot: an AND fed on one input only, so its first bit waits
        /// for a partner that never comes and the bits behind it pile up. Whichever lesson that
        /// raises first -- a stall or a collision -- is the one pictured; both are drawn the same way.
        /// </remarks>
        [UnityTest]
        public IEnumerator Shot13_FirstTimeHint()
        {
            yield return OpenOnTheBoard();

            LevelSession session = Find<LevelSession>();
            SimulationRunner runner = Find<SimulationRunner>();

            Assert.IsTrue(session.TryPlaceGate(GateKind.And, Low), "could not place the AND");
            int and = NodeOn(runner, Low);
            Wire(session, runner.FixtureNodeIds["a"], and, 0);
            Wire(session, and, runner.FixtureNodeIds["carry"], 0);

            session.Run();

            HintBanner hint = Find<HintBanner>();

            for (int frame = 0; frame < 3000 && !hint.IsShowing; frame++)
                yield return null;

            Assert.IsTrue(hint.IsShowing, "no first-time hint came up");

            yield return Frames(30);
            yield return Capture("13-hint");
        }

        /// <summary>
        /// A register holding a 1 mid-run, with the 0 it started with on its way out: the one
        /// shot of a machine's state.
        /// </summary>
        /// <remarks>
        /// Added when bits started saying their value by shape. None of the other shots has a
        /// register in it, so nothing pictured the bit a sequential level is about -- and a change
        /// that made it unreadable would have passed every capture.
        ///
        /// Its first-time hint is marked seen, so the banner does not cover the board it explains.
        /// </remarks>
        [UnityTest]
        public IEnumerator Shot14_RegisterHolding()
        {
            yield return OpenOnTheBoard();

            ProgressStore store = Find<ProgressTracker>().Store;
            store.MarkMilestone(ChapterCard.Milestone);
            store.MarkHintSeen(HintRules.Register);

            yield return LoadAndBuild(FirstRegisterLevel);

            LevelSession session = Find<LevelSession>();
            SimulationRunner runner = Find<SimulationRunner>();
            var middle = new Vector2Int(0, 0);

            Assert.IsTrue(session.TryPlaceGate(GateKind.Register, middle), "could not place the register");
            int register = NodeOn(runner, middle);
            Wire(session, runner.FixtureNodeIds["in"], register, 0);
            Wire(session, register, runner.FixtureNodeIds["out"], 0);

            session.Run();

            for (int frame = 0; frame < 2000 && !Holds(runner, register, Bit.One); frame++)
                yield return null;

            Assert.IsTrue(Holds(runner, register, Bit.One), "the register never took a 1");

            yield return HalfATick();

            Assert.IsTrue(Holds(runner, register, Bit.One), "the register let go of its 1 within half a tick");
            Assert.Greater(BitsInFlight(), 0, "the register shot has nothing in flight beside it");

            yield return Capture("14-register");
        }

        private static bool Holds(SimulationRunner runner, int id, Bit value) =>
            runner.View.GetNode(id) is RegisterNode register && register.State == value;

        // -----------------------------------------------------------------
        // Staging
        // -----------------------------------------------------------------

        /// <summary>The first level with a register in its parts list, where the chapter card fires.</summary>
        private const string FirstRegisterLevel = "one-clock-late";

        /// <summary>The first level with a clock -- a register alone needs none; a loop does.</summary>
        private const string ClockedLevel = "flip-on-one";

        /// <summary>Loads a level and waits until the board has been built from it.</summary>
        private static IEnumerator LoadAndBuild(string level)
        {
            LevelSession session = Find<LevelSession>();
            SimulationRunner runner = Find<SimulationRunner>();
            int before = runner.GraphRevision;

            Assert.IsTrue(session.LoadLevel(level), $"{level} did not load");

            for (int frame = 0; frame < 60 && runner.GraphRevision == before; frame++)
                yield return null;

            Assert.AreNotEqual(before, runner.GraphRevision, $"{level} loaded but was never built");
        }

        /// <summary>Clicks a button on the canvas by its object's name.</summary>
        private static void Press(string name)
        {
            GameObject target = GameObject.Find(name);
            Assert.IsNotNull(target, $"sanity: there is no '{name}' on screen to press");

            Button button = target.GetComponent<Button>();
            Assert.IsNotNull(button, $"sanity: '{name}' is not a button");

            button.onClick.Invoke();
        }

        /// <summary>Whether a piece of interface with this name is on screen.</summary>
        private static bool IsUp(string name)
        {
            GameObject target = GameObject.Find(name);
            return target != null && target.activeInHierarchy;
        }

        /// <summary>
        /// Past the menu and onto <see cref="Level"/>, with the tutorial marked as done.
        /// </summary>
        /// <remarks>
        /// SaveGuard hands every test a fresh save, and a fresh save is exactly what the tutorial
        /// offers itself on -- it would adopt its own board the moment the menu closed.
        /// </remarks>
        private static IEnumerator OpenOnTheBoard()
        {
            yield return LoadTheGame();
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
            // A panel still fading in is a picture of a moment, not of the panel. Waited on, not
            // counted in frames, with a cap that fails rather than capturing it half-drawn.
            for (int frame = 0; frame < 240 && UiFade.AnyMoving; frame++)
                yield return null;

            Assert.IsFalse(UiFade.AnyMoving, $"'{name}' was about to be captured with a panel still fading in");

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
