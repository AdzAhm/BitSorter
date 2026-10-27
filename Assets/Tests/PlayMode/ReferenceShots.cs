using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using BitSorter.LogicCore;
using BitSorter.View;
using BitSorter.ViewModel;
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

        /// <summary>
        /// The tutorial pointing at a button: the step that asks for RUN, with its ring round it.
        /// </summary>
        /// <remarks>
        /// Added after a ring on the interface turned out to be a filled slab drawn over RUN, its
        /// caption lost underneath, on exactly the step that asks for it -- and no shot had a
        /// tutorial ring on the interface in it, so all fourteen passed.
        /// </remarks>
        [UnityTest]
        public IEnumerator Shot15_TutorialRingOnAButton()
        {
            yield return LoadTheGame();
            FixTheSparks();
            Find<MainMenu>().Show(false);

            for (int frame = 0; frame < 120 && !TutorialDirector.HoldingTheBoard; frame++)
                yield return null;

            Assert.IsTrue(TutorialDirector.HoldingTheBoard, "sanity: the tutorial should be on its intro");

            Find<TutorialPanel>().Next();
            yield return Frames(2);

            LevelSession session = Find<LevelSession>();
            SimulationRunner runner = Find<SimulationRunner>();

            Assert.IsTrue(Find<PlacementController>().TrySelect(TutorialLevel.Part), "could not pick up the NOT");
            Assert.IsTrue(session.TryPlaceGate(TutorialLevel.Part, TutorialLevel.GateCell), "could not place the NOT");

            int gate = NodeOn(runner, TutorialLevel.GateCell);
            Wire(session, runner.FixtureNodeIds[TutorialLevel.SourceId], gate, 0);
            Wire(session, gate, runner.FixtureNodeIds[TutorialLevel.SinkId], 0);

            // Past the step that asks for a longer wire, whose ring is on the board, not the
            // interface: the wire from A into the NOT, scrolled up once at its middle.
            int source = runner.FixtureNodeIds[TutorialLevel.SourceId];
            Vector2 middle = (PortGeometry.PositionOf(runner.PositionOf(source), false, 0, 1)
                              + PortGeometry.PositionOf(runner.PositionOf(gate), true, 0, 1)) * 0.5f;
            Assert.IsTrue(session.TryChangeWireDelay(middle, 1), "could not lengthen the wire");

            // The step that asks for RUN, with a ring on the interface round the button.
            for (int frame = 0; frame < 120 && !RingOnTheInterface(); frame++)
                yield return null;

            Assert.IsTrue(RingOnTheInterface(), "no ring went up on the interface");

            yield return Frames(30);
            yield return Capture("15-tutorial-ring");
        }

        /// <summary>
        /// A collision one tick out that will take the waiting bit too: red, and the waiting bit
        /// crossed out.
        /// </summary>
        /// <remarks>
        /// The pair to the collision shot, which is the other outcome. The two were told apart by
        /// amber and red alone until the cross; this is the shot that shows it. The same board: an
        /// AND fed from A, whose 0 waits while a 1 comes up behind it.
        ///
        /// Waits for the flash of the collision before it to end, rather than for half a tick: a
        /// port mid-flash outranks its warning, and the flash is longer than half a tick.
        /// </remarks>
        [UnityTest]
        public IEnumerator Shot16_CollisionThatTakesBoth()
        {
            yield return OpenOnTheBoard();

            LevelSession session = Find<LevelSession>();
            SimulationRunner runner = Find<SimulationRunner>();

            Assert.IsTrue(session.TryPlaceGate(GateKind.And, Low), "could not place the AND");
            int and = NodeOn(runner, Low);
            Wire(session, runner.FixtureNodeIds["a"], and, 0);
            Wire(session, and, runner.FixtureNodeIds["carry"], 0);

            session.Run();

            for (int frame = 0; frame < 2000 && !AnyCollisionTakesBoth(runner); frame++)
                yield return null;

            Assert.IsTrue(AnyCollisionTakesBoth(runner), "no collision coming takes the waiting bit too");

            PortRenderer ports = Find<PortRenderer>();
            var port = new PortAddress(NodeOn(runner, Low), true, 0);

            // One frame first: the tick that brought this collision up also ended the one before,
            // and whether the port has started that one's flash yet depends on which of it and the
            // runner updated first this frame. "Not flashing" before then means "not yet".
            yield return null;

            for (int frame = 0; frame < 240 && ports.IsFlashing(port); frame++)
                yield return null;

            yield return Frames(2);

            Assert.IsTrue(AnyCollisionTakesBoth(runner), "the warning was over before the last collision's flash");
            Assert.IsTrue(ports.IsMarkedDoomed(port), "the waiting bit is not crossed out");

            yield return Capture("16-collision-both");
        }

        /// <summary>
        /// The settings, asking whether to reset: all three sections, and the one question in the
        /// game whose answer cannot be taken back.
        /// </summary>
        [UnityTest]
        public IEnumerator Shot17_SettingsAsking()
        {
            yield return LoadTheGame();
            yield return Frames(2);

            GameObject.Find("Settings").GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
            yield return Frames(2);

            GameObject.Find(SettingsPanel.ResetButton).GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
            yield return Frames(30);

            Assert.IsTrue(Find<SettingsPanel>().Confirming, "sanity: the settings should be asking");

            yield return Capture("17-settings");
        }

        /// <summary>
        /// The credits, part-way up: the title near the top of the screen and the roles under it.
        /// </summary>
        /// <remarks>
        /// Fourteen seconds of fixed frame time, so the same roll every capture: the roll counts on
        /// <see cref="Time.deltaTime"/> and nothing else. Taken in tenth-of-a-second steps rather
        /// than the capture's own, which is the same fourteen seconds in a twelfth of the frames --
        /// at the capture's own step this one shot took four minutes in a background editor.
        /// </remarks>
        [UnityTest]
        public IEnumerator Shot18_Credits()
        {
            yield return LoadTheGame();
            yield return Frames(2);

            GameObject.Find("Settings").GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
            yield return Frames(2);

            GameObject.Find(SettingsPanel.CreditsButton).GetComponent<UnityEngine.UI.Button>().onClick.Invoke();

            const float step = 0.1f;
            Time.captureDeltaTime = step;
            yield return Frames(Mathf.RoundToInt(14f / step));
            Time.captureDeltaTime = FrameSeconds;
            yield return Frames(2);

            Assert.IsTrue(Find<CreditsPanel>().IsOpen, "sanity: the credits should be rolling");

            yield return Capture("18-credits");
        }

        /// <summary>The help panel's K-map: the half adder's SUM map, the same panel as shot 8's table.</summary>
        [UnityTest]
        public IEnumerator Shot19_HelpKMap()
        {
            yield return OpenOnTheBoard();
            yield return BuildTheHalfAdder(wireEverything: true);

            Press("Help badge");
            yield return Frames(30);

            Press("Help map tab sum");
            yield return Frames(30);

            Assert.IsTrue(IsUp("Help"), "sanity: the help panel should be open");
            Assert.IsTrue(IsUp("Help map tab sum"), "sanity: the half adder should have a map for SUM");

            yield return Capture("19-help-kmap");
        }

        /// <summary>
        /// Four lanes built: the 4:1 mux as a tree of three, on the 11 by 7 board, with the waiting
        /// select lines' delays on their wires.
        /// </summary>
        [UnityTest]
        public IEnumerator Shot20_FourLanes()
        {
            yield return OpenOnTheBoard();

            // Every shot starts from a fresh save, where the first wire given a delay raises the
            // first-time hint about scrolling one -- over the board this shot is of. Shot 13 is the
            // picture of a first-time hint.
            Find<ProgressTracker>().Store.MarkHintSeen(HintRules.WireDelay);

            yield return LoadAndBuild("four-lanes");
            yield return BuildFourLanes();
            yield return Frames(30);

            Assert.AreEqual(new Vector2Int(5, 3), Find<PlacementGrid>().HalfExtents, "sanity: not on the level's own board");
            Assert.AreEqual(21, Find<LevelSession>().Blueprint.Wires.Count, "sanity: the tree is not fully wired");

            yield return Capture("20-four-lanes");
        }

        /// <summary>
        /// Free play on a second board, named through the naming panel, with a gate wired on it: the
        /// BOARD section with somewhere to step back to.
        /// </summary>
        [UnityTest]
        public IEnumerator Shot21_NamedBoard()
        {
            yield return OpenOnTheBoard();

            Find<SandboxPanel>().Open();
            yield return Frames(2);

            Press("Board new");
            yield return Frames(2);

            Press("Board rename");
            yield return Frames(2);

            NameBoardPanel namer = Find<NameBoardPanel>();
            namer.SetTyped("Half adder, my way");
            Press("Name ok");
            yield return Frames(2);

            LevelSession session = Find<LevelSession>();
            SimulationRunner runner = Find<SimulationRunner>();
            var cell = new Vector2Int(0, 2);

            Assert.IsTrue(session.TryPlaceGate(GateKind.Xor, cell), "could not place the XOR");
            int xor = NodeOn(runner, cell);
            Wire(session, runner.FixtureNodeIds["A"], xor, 0);
            Wire(session, runner.FixtureNodeIds["B"], xor, 1);
            Wire(session, xor, runner.FixtureNodeIds["OUT 1"], 0);

            yield return Frames(30);

            Assert.IsFalse(namer.IsShowing, "sanity: the naming panel should have closed");
            Assert.AreEqual("Half adder, my way",
                GameObject.Find("Board name").GetComponent<TMPro.TextMeshProUGUI>().text, "sanity: the name did not take");

            yield return Capture("21-named-board");
        }

        /// <summary>
        /// The three levels that open on a broken circuit, as they open: the start on the board and
        /// START OVER on the button, before anything has been touched.
        /// </summary>
        [UnityTest]
        public IEnumerator Shot22_OutOfStep() => OpensOnItsCircuit("out-of-step", "a", "22-out-of-step");

        /// <inheritdoc cref="Shot22_OutOfStep"/>
        [UnityTest]
        public IEnumerator Shot23_WrongPart() => OpensOnItsCircuit("wrong-part", "a", "23-wrong-part");

        /// <inheritdoc cref="Shot22_OutOfStep"/>
        [UnityTest]
        public IEnumerator Shot24_Miscount() => OpensOnItsCircuit("miscount", "in", "24-miscount");

        /// <summary>
        /// The timing diagram open mid-run, with W1 on a wire whose bits collide.
        /// </summary>
        /// <remarks>
        /// Shot 04's board: an AND with only A wired in, so A's bits pile up at its first input.
        /// A's second 0 lands on the first (only the arrival dies), then A's first 1 lands on the
        /// held 0 (both die) -- W1 shows 0, then 0 and 1 each crossed, then the 1 that got in. The
        /// sources run their streams above it and the bins stay empty, which is the lesson: nothing
        /// reaches CARRY while the AND waits on an input that never comes.
        /// </remarks>
        [UnityTest]
        public IEnumerator Shot25_TimingDiagram()
        {
            yield return OpenOnTheBoard();

            // The board and the diagram, not the lessons a first collision and stall bring up.
            ProgressStore store = Find<ProgressTracker>().Store;
            store.MarkHintSeen(HintRules.Collision);
            store.MarkHintSeen(HintRules.Stalled);

            LevelSession session = Find<LevelSession>();
            SimulationRunner runner = Find<SimulationRunner>();

            Assert.IsTrue(session.TryPlaceGate(GateKind.And, Low), "could not place the AND");
            int and = NodeOn(runner, Low);
            int a = runner.FixtureNodeIds["a"];
            Wire(session, a, and, 0);
            Wire(session, and, runner.FixtureNodeIds["carry"], 0);

            Edge aIntoTheAnd = null;

            for (int id = 0; id < runner.View.EdgeCount; id++)
            {
                Edge edge = runner.View.GetEdge(id);

                if (edge != null && edge.Source.Owner.Id == a && edge.Target.Owner.Id == and)
                    aIntoTheAnd = edge;
            }

            Assert.IsNotNull(aIntoTheAnd, "sanity: A's wire into the AND was not built");
            Assert.AreEqual(ProbeToggle.Added, Find<ProbeController>().Toggle(aIntoTheAnd), "W1 did not go in");

            WaveformPanel diagram = Find<WaveformPanel>();
            diagram.Badge.onClick.Invoke();
            yield return Frames(2);

            // Tick 4, with both collisions recorded and A's last bit still on its wire. By tick 5
            // it has landed, nothing is in flight, and the run is over with no middle to wait for.
            session.Run();
            yield return UntilTick(4);
            yield return HalfATick();

            int w1 = diagram.RowCount - WaveformPanel.WireRows;
            Assert.IsTrue(diagram.TryCell(w1, 2, out WaveCell second) && second.Collided,
                "W1 shows no collision at tick 2, so the shot would show none");
            Assert.IsTrue(diagram.TryCell(w1, 3, out WaveCell third) && third.Collided,
                "W1 shows no collision at tick 3");

            yield return Capture("25-timing-diagram");
        }

        /// <summary>
        /// Four at once built and running: four full adders in boxes down a staircase on the 13 by 9
        /// board, the carry handed from box to box and the upper columns' bits waiting on their
        /// wires for it.
        /// </summary>
        [UnityTest]
        public IEnumerator Shot26_FourAtOnce()
        {
            yield return OpenOnTheBoard();

            // The first wire given a delay raises the first-time hint about scrolling one, over the
            // board this shot is of, as in shot 20.
            Find<ProgressTracker>().Store.MarkHintSeen(HintRules.WireDelay);

            yield return LoadAndBuild("four-at-once");
            BuildFourAtOnce();
            yield return Frames(30);

            LevelSession session = Find<LevelSession>();
            Assert.AreEqual(new Vector2Int(6, 4), Find<PlacementGrid>().HalfExtents, "sanity: not on the level's own board");
            Assert.AreEqual(4, Find<SimulationRunner>().Circuit.Blocks.Count, "sanity: not four boxes");
            Assert.AreEqual(17, session.Blueprint.Wires.Count, "sanity: the adder is not fully wired");

            // Tick 6: bit 0's sums landing, carries between the boxes, A3 and B3 still on their way.
            session.Run();
            yield return UntilTick(6);
            yield return HalfATick();

            yield return Capture("26-four-at-once");
        }

        /// <summary>
        /// 5.0's workbench preview at 13.5 ns, two changes on their way: NOT B's fall, and the
        /// second AND's rise that the next nanosecond will swallow. The Schematic look's first shot.
        /// </summary>
        [UnityTest]
        public IEnumerator Shot27_Workbench()
        {
            yield return LoadTheGame();
            yield return Frames(2);

            GameObject.Find(MainMenu.WorkbenchButton).GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
            yield return Frames(2);

            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            workbench.Pause();
            workbench.Seek(13.5);
            yield return Frames(30);

            Assert.IsTrue(workbench.IsOpen, "sanity: the workbench should be open");
            Assert.AreEqual(2, workbench.Model.InFlight, "sanity: two changes should be on their way at 13.5");

            yield return Capture("27-workbench");
        }

        /// <summary>The same moment, zoomed in on the AND that swallows the short glitch: the window's clip, the edges at twice the size, and FIT.</summary>
        [UnityTest]
        public IEnumerator Shot28_WorkbenchZoomed()
        {
            yield return LoadTheGame();
            yield return Frames(2);

            GameObject.Find(MainMenu.WorkbenchButton).GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
            yield return Frames(2);

            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            workbench.Pause();
            workbench.Seek(13.5);

            PartLayout secondAnd = workbench.Model.Layout.Parts.Where(p => p.Symbol == PartSymbol.And).ElementAt(1);
            Vector2 cursor = workbench.ScreenPointOf(secondAnd.ToStage(0, 0));
            float fit = workbench.Zoom;
            for (int notch = 0; notch < 3; notch++)
                workbench.TakeWheel(cursor, new Vector2(0f, 120f));
            yield return Frames(30);

            Assert.Greater(workbench.Zoom, 1.9f * fit, "sanity: three notches in should be nearly twice the fit");

            yield return Capture("28-workbench-zoomed");
        }

        /// <summary>The same moment with the pointer on NOT B's wire: the whole net lit, and the chip saying what drives it and what reads it.</summary>
        [UnityTest]
        public IEnumerator Shot29_WorkbenchHover()
        {
            yield return LoadTheGame();
            yield return Frames(2);

            GameObject.Find(MainMenu.WorkbenchButton).GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
            yield return Frames(2);

            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            workbench.Pause();
            workbench.Seek(13.5);

            int notB = workbench.Model.Layout.NetNamed("nb");
            StageRoute wire = workbench.Model.Layout.Nets[notB].Branches[0];
            Mouse mouse = InputSystem.GetDevice<Mouse>();
            mouse.MakeCurrent();
            Set(mouse.position, workbench.ScreenPointOf(wire.PointAt(wire.Length * 0.3f)));
            yield return Frames(30);

            Assert.AreEqual(notB, workbench.HoveredNet, "sanity: the pointer should be on NOT B's wire");

            yield return Capture("29-workbench-hover");
        }

        /// <summary>
        /// The reference with s read as unsigned and out as signed, the playhead at 11 and the second
        /// cursor at 20: the radix in the readouts, the boxes and the rings -- out's change to A halfway
        /// down its wire as -6 -- C2 in its own colour, and 9 ns over the bracket between them (Part 9b-2b).
        /// </summary>
        [UnityTest]
        public IEnumerator Shot30_WorkbenchRadixAndCursors()
        {
            yield return LoadTheGame();
            yield return Frames(2);

            GameObject.Find(MainMenu.WorkbenchButton).GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
            yield return Frames(2);

            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            workbench.Pause();
            WaveList list = workbench.List;
            int s = Enumerable.Range(0, list.Lines.Count).Single(i => list.LabelOf(list.Lines[i]) == "s[3:0]");
            int o = Enumerable.Range(0, list.Lines.Count).Single(i => list.LabelOf(list.Lines[i]) == "out[3:0]");
            workbench.SetRadix(s, WaveRadix.Unsigned);
            workbench.SetRadix(o, WaveRadix.Signed);
            list.ClearSelection();
            workbench.Seek(11.0);
            workbench.PlaceSecond(20.0);

            // A breakpoint at a moment, flagged on the axis, and one on out going x, flagged by its row (Part 9b-3).
            workbench.Playback.Breakpoints.Add(Breakpoint.At(26));
            workbench.Playback.Breakpoints.Add(Breakpoint.OnX(workbench.Model.RowSignal(o)));
            workbench.Seek(11.0);
            yield return Frames(30);

            Assert.AreEqual("9 ps", workbench.SpanText, "sanity: the second cursor is 9 ps on");
            Assert.AreEqual(WaveRadix.Signed, workbench.Model.RowRadix(o), "sanity: out is signed");

            yield return Capture("30-workbench-radix-cursors");
        }

        /// <summary>
        /// The full adder with h1's sum and carry and h2's carry -- a block's inside -- grouped as
        /// carries, and every row named by its whole path: hierarchy, groups and full names (Part 9b-2b).
        /// </summary>
        [UnityTest]
        public IEnumerator Shot31_WorkbenchHierarchy()
        {
            yield return LoadTheGame();
            yield return Frames(2);

            GameObject.Find(MainMenu.WorkbenchButton).GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
            yield return Frames(2);

            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            workbench.ShowSample(2);   // the full adder
            yield return Frames(2);
            workbench.Pause();

            foreach (string key in new[] { "h1.s", "h1.c", "h2.c" })
            {
                Assert.IsTrue(workbench.Model.Signals.TryFind(key, out int signal), $"sanity: the full adder has {key}");
                workbench.AddSignal(signal);
            }

            WaveList list = workbench.List;
            int first = list.Lines.Count - 3;
            list.Select(first);
            list.Select(first + 2, extend: true);
            workbench.GroupRows();
            list.Rename(first, "carries");
            workbench.ToggleNames();
            list.ClearSelection();
            workbench.Seek(40.0);
            yield return Frames(30);

            Assert.IsTrue(list.Lines[first].IsHeader, "sanity: the three are under a header");

            yield return Capture("31-workbench-hierarchy");
        }

        /// <summary>The + SIGNAL list open over the full adder, h1 searched: h1's own signals, ranked, the cells' pins left out.</summary>
        [UnityTest]
        public IEnumerator Shot32_WorkbenchAddSignal()
        {
            yield return LoadTheGame();
            yield return Frames(2);

            GameObject.Find(MainMenu.WorkbenchButton).GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
            yield return Frames(2);

            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            workbench.ShowSample(2);   // the full adder
            yield return Frames(2);
            workbench.Pause();
            workbench.Seek(40.0);
            workbench.OpenPicker();
            workbench.Picker.Search("h1");
            UiTheme.Defocus();
            yield return Frames(30);

            Assert.IsTrue(workbench.Picker.IsOpen, "sanity: the list is up");
            Assert.Greater(workbench.Picker.Found.Count, 3, "sanity: h1 finds h1's signals");

            yield return Capture("32-workbench-add-signal");
        }

        /// <summary>
        /// MY CIRCUIT half built -- two inputs, an XOR and an AND, two outputs, no wires yet (they are Part 10b) -- the
        /// two gates selected and dragged three cells right: outlined where they are and where they are going, and a
        /// dotted guide through each middle they line up with (Part 10a).
        /// </summary>
        [UnityTest]
        public IEnumerator Shot33_WorkbenchEditing()
        {
            yield return LoadTheGame();
            yield return Frames(2);

            GameObject.Find(MainMenu.WorkbenchButton).GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
            yield return Frames(2);

            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            workbench.ShowSample(0);   // MY CIRCUIT, blank under SaveGuard
            yield return Frames(2);
            workbench.Pause();

            WorkbenchSession session = workbench.Session;
            int Place(string key, double x, double y)
            {
                BitSorter.Board.EditResult placed = session.Place(PartsCatalog.Find(session.Project, session.Module, key), new BitSorter.Board.Placement(x, y));
                Assert.IsTrue(placed.Done, placed.Refusal);
                return placed.Id;
            }

            Place("input", 1, -1);
            Place("input", 1, -3);
            int xor = Place("xor", 5, -1);
            int and = Place("and", 5, -3);
            Place("output", 13, -1);
            Place("output", 13, -3);
            session.Finish();
            yield return Frames(4);

            CanvasEditor editor = workbench.Editor;
            editor.Do(new CanvasCommand(CanvasGesture.Select, target: new CanvasTarget(CanvasTargetKind.Part, part: xor)));
            editor.Do(new CanvasCommand(CanvasGesture.Select, target: new CanvasTarget(CanvasTargetKind.Part, part: and), extend: true));
            editor.Do(new CanvasCommand(CanvasGesture.BeginMove, from: new BitSorter.Board.Point(5, -1)));
            editor.Do(new CanvasCommand(CanvasGesture.MoveTo, new BitSorter.Board.Point(8.05, -1.02), new BitSorter.Board.Point(5, -1)));
            workbench.Seek(0.0);
            yield return Frames(30);

            Assert.AreEqual(2, workbench.Overlay.Ghosts.Count, "sanity: the two gates are on their way");
            Assert.IsNotEmpty(workbench.Overlay.Guides, "sanity: and something lines up with them");

            yield return Capture("33-workbench-editing");
        }

        /// <summary>
        /// MY CIRCUIT's half adder half wired (Part 10b), a at 1 and b at 0, at 40: a and b into the XOR, its sum and the AND's carry out, a tapped
        /// off the XOR's wire into the AND with a junction dot -- and the AND's second input being drawn to b's wire, the
        /// pointer on it, the junction it would make ringed.
        /// </summary>
        [UnityTest]
        public IEnumerator Shot34_WorkbenchWiring()
        {
            yield return LoadTheGame();
            yield return Frames(2);

            GameObject.Find(MainMenu.WorkbenchButton).GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
            yield return Frames(2);

            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            workbench.ShowSample(0);   // MY CIRCUIT, blank under SaveGuard
            yield return Frames(2);
            workbench.Pause();

            WorkbenchSession session = workbench.Session;
            int Place(string key, double x, double y)
            {
                BitSorter.Board.EditResult placed = session.Place(PartsCatalog.Find(session.Project, session.Module, key), new BitSorter.Board.Placement(x, y));
                Assert.IsTrue(placed.Done, placed.Refusal);
                return placed.Id;
            }

            int Wire(BitSorter.Board.Endpoint from, BitSorter.Board.Endpoint to, BitSorter.Board.Route route = null)
            {
                BitSorter.Board.EditResult made = session.Connect(from, to, route);
                Assert.IsTrue(made.Done, made.Refusal);
                return made.Id;
            }

            BitSorter.Board.Endpoint Pin(int part, string pin) => BitSorter.Board.Endpoint.At(part, pin);

            int a = Place("input", 1, -1);
            int b = Place("input", 1, -4);
            int xor = Place("xor", 5, -1);
            int and = Place("and", 5, -4);
            int s = Place("output", 9, -1);
            int c = Place("output", 9, -4);
            int aToXor = Wire(Pin(a, "out"), Pin(xor, "a"));
            int bToXor = Wire(Pin(b, "out"), Pin(xor, "b"));
            Wire(Pin(xor, "y"), Pin(s, "in"));
            Wire(Pin(and, "y"), Pin(c, "in"));
            Wire(Pin(and, "a"), BitSorter.Board.Endpoint.OnWire(aToXor), new BitSorter.Board.Route { TapAt = 0.25 });

            // a goes to 1 at 10, so the sum is 1 on screen and the carry 0.
            string nameA = session.Project.TopModule.FindPart(a).Name;
            Assert.IsTrue(session.SetInput(nameA, 10, long.MaxValue, BitSorter.SystemVerilog.LogicVec.FromUInt64(1, 1)).Done);
            session.Finish();
            yield return Frames(4);

            // The AND's b being drawn: started on its pin, the pointer half-way along b's wire, clear of the crossing below.
            CanvasEditor editor = workbench.Editor;
            BitSorter.Board.Module top = session.Project.TopModule;
            BitSorter.Board.Point andB = BitSorter.Board.PartGeometry.Placed(session.Project, top.FindPart(and), top.Layout.PlacementOf(and)).Pins.Single(p => p.Pin == "b").At;
            editor.Do(new CanvasCommand(CanvasGesture.BeginWire, andB, target: new CanvasTarget(CanvasTargetKind.Pin, part: and, pin: "b")));
            var route = BitSorter.Board.WireGeometry.Wires(session.Project, top)[bToXor];
            BitSorter.Board.Point onB = BitSorter.Board.WireGeometry.PointAt(route, BitSorter.Board.WireGeometry.Length(route) * 0.5);

            Mouse mouse = InputSystem.GetDevice<Mouse>();
            mouse.MakeCurrent();
            Set(mouse.position, workbench.ScreenPointOf(onB));
            workbench.Seek(40.0);
            yield return Frames(30);

            Assert.IsTrue(editor.Drafting, "sanity: the AND's b is being drawn");
            Assert.IsTrue(workbench.Overlay.DraftEnd.HasValue, "sanity: and the pointer is on b's wire, which it would tap");
            Assert.IsFalse(workbench.Overlay.DraftRefused);

            yield return Capture("34-workbench-wiring");
        }

        /// <summary>
        /// MY CIRCUIT's half adder wired whole, b's wire selected -- a square on each of its corners -- and its rise being
        /// dragged a cell left, drawn where it is going (Part 10b-2).
        /// </summary>
        [UnityTest]
        public IEnumerator Shot35_WorkbenchReshaping()
        {
            yield return LoadTheGame();
            yield return Frames(2);

            GameObject.Find(MainMenu.WorkbenchButton).GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
            yield return Frames(2);

            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            workbench.ShowSample(0);   // MY CIRCUIT, blank under SaveGuard
            yield return Frames(2);
            workbench.Pause();

            WorkbenchSession session = workbench.Session;
            int Place(string key, double x, double y)
            {
                BitSorter.Board.EditResult placed = session.Place(PartsCatalog.Find(session.Project, session.Module, key), new BitSorter.Board.Placement(x, y));
                Assert.IsTrue(placed.Done, placed.Refusal);
                return placed.Id;
            }

            int Wire(BitSorter.Board.Endpoint from, BitSorter.Board.Endpoint to, BitSorter.Board.Route route = null)
            {
                BitSorter.Board.EditResult made = session.Connect(from, to, route);
                Assert.IsTrue(made.Done, made.Refusal);
                return made.Id;
            }

            BitSorter.Board.Endpoint Pin(int part, string pin) => BitSorter.Board.Endpoint.At(part, pin);

            int a = Place("input", 1, -1);
            int b = Place("input", 1, -4);
            int xor = Place("xor", 5, -1);
            int and = Place("and", 5, -4);
            int s = Place("output", 9, -1);
            int c = Place("output", 9, -4);
            int aToXor = Wire(Pin(a, "out"), Pin(xor, "a"));
            int bToXor = Wire(Pin(b, "out"), Pin(xor, "b"));
            Wire(Pin(xor, "y"), Pin(s, "in"));
            Wire(Pin(and, "y"), Pin(c, "in"));
            Wire(Pin(and, "a"), BitSorter.Board.Endpoint.OnWire(aToXor), new BitSorter.Board.Route { TapAt = 0.25 });
            Wire(Pin(and, "b"), BitSorter.Board.Endpoint.OnWire(bToXor), new BitSorter.Board.Route { TapAt = 1.0 / 5.25 });   // a cell along b's wire
            string nameA = session.Project.TopModule.FindPart(a).Name;
            Assert.IsTrue(session.SetInput(nameA, 10, long.MaxValue, BitSorter.SystemVerilog.LogicVec.FromUInt64(1, 1)).Done);
            session.Finish();
            yield return Frames(4);

            // b's wire selected, and its rise taken hold of half-way up and dragged a cell left.
            CanvasEditor editor = workbench.Editor;
            BitSorter.Board.Module top = session.Project.TopModule;
            var route = BitSorter.Board.WireGeometry.Wires(session.Project, top)[bToXor];
            int rise = Enumerable.Range(1, route.Count - 1).First(i => route[i].X == route[i - 1].X) - 1;
            var grab = new BitSorter.Board.Point(route[rise].X, (route[rise].Y + route[rise + 1].Y) / 2);
            editor.Do(new CanvasCommand(CanvasGesture.Select, target: new CanvasTarget(CanvasTargetKind.Wire, wire: bToXor, segment: rise)));
            editor.Do(new CanvasCommand(CanvasGesture.BeginReshape, from: grab, target: new CanvasTarget(CanvasTargetKind.Wire, wire: bToXor, segment: rise)));
            editor.Do(new CanvasCommand(CanvasGesture.ReshapeTo, new BitSorter.Board.Point(grab.X - 1.05, grab.Y), grab));
            workbench.Seek(40.0);
            yield return Frames(30);

            Assert.IsTrue(editor.Reshaping, "sanity: b's wire is being reshaped");
            Assert.IsNotEmpty(workbench.Overlay.Handles, "sanity: and its corners are drawn");

            yield return Capture("35-workbench-reshaping");
        }

        /// <summary>
        /// The reference's AND selected and inspected (Part 10c), docked (Part 11b-2): the bar with its name, inputs, rise,
        /// fall and width, and a width of 2000 refused in the document's words, still typed there to be put right; its
        /// rise and fall at hand in the side panel.
        /// </summary>
        [UnityTest]
        public IEnumerator Shot36_WorkbenchInspector()
        {
            yield return LoadTheGame();
            yield return Frames(2);

            GameObject.Find(MainMenu.WorkbenchButton).GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
            yield return Frames(2);

            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            workbench.ShowSample(1);   // the reference
            yield return Frames(2);
            workbench.Pause();
            workbench.Session.Finish();
            workbench.Seek(13.5);
            yield return Frames(4);

            WorkbenchSession session = workbench.Session;
            BitSorter.Board.GatePart and = session.Project.TopModule.Parts.OfType<BitSorter.Board.GatePart>()
                .Single(g => g.Kind == BitSorter.Sim.GateKind.And && g.Fall == 3);
            var target = new CanvasTarget(CanvasTargetKind.Part, part: and.Id);
            workbench.Editor.Do(new CanvasCommand(CanvasGesture.Inspect, target: target));
            yield return Frames(2);
            workbench.InspectorType("width", "2000");
            workbench.InspectorApply();   // no field holds the keyboard, so no caret blinks: the shot is the same every time
            yield return Frames(30);

            StringAssert.StartsWith("AND ", workbench.InspectorTitle, "sanity: the AND is inspected");
            StringAssert.Contains("1 to 1024", workbench.InspectorRefusal, "sanity: with the refusal in the bar");

            yield return Capture("36-workbench-inspector");
        }

        /// <summary>
        /// The reference zoomed in, its minimap in the window's corner with the window on it, the two ANDs selected and the
        /// canvas's menu open on them: align, middles, turn, mirror, copy, cut and delete (Part 10d).
        /// </summary>
        [UnityTest]
        public IEnumerator Shot37_WorkbenchArranging()
        {
            yield return LoadTheGame();
            yield return Frames(2);

            GameObject.Find(MainMenu.WorkbenchButton).GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
            yield return Frames(2);

            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            workbench.ShowSample(1);   // the reference
            yield return Frames(2);
            workbench.Pause();
            workbench.Session.Finish();
            workbench.Seek(13.5);
            yield return Frames(4);

            // Two notches in, about the middle of the window, as + does.
            for (int i = 0; i < 2; i++)
                workbench.ZoomAboutTheMiddleForTests(1);

            WorkbenchSession session = workbench.Session;
            int[] ands = session.Project.TopModule.Parts.OfType<BitSorter.Board.GatePart>()
                .Where(g => g.Kind == BitSorter.Sim.GateKind.And).Select(g => g.Id).ToArray();
            // Selected as clicks select, so the overlay draws them; the menu opened over the circuit's left, clear of the map.
            for (int i = 0; i < ands.Length; i++)
                workbench.Editor.Do(new CanvasCommand(CanvasGesture.Select, target: new CanvasTarget(CanvasTargetKind.Part, part: ands[i]), extend: i > 0));
            int b = session.Project.TopModule.FindPart("b").Id;
            BitSorter.Board.Point at = session.Project.TopModule.Layout.PlacementOf(b).Position;
            workbench.CanvasMenu.Open(workbench.ScreenPointOf(at) + new Vector2(40f, 40f), ands.Length, 0, canPaste: false);
            yield return Frames(30);

            Assert.IsNotNull(workbench.Map, "sanity: the minimap is up");
            Assert.IsTrue(workbench.CanvasMenu.IsOpen, "sanity: and the menu");

            yield return Capture("37-workbench-arranging");
        }

        /// <summary>
        /// The full adder made to its end (Part 11c): the parts panel showing the design's hierarchy, its two half adders
        /// under it, and the dock showing the transcript -- what its testbench said as it stopped.
        /// </summary>
        [UnityTest]
        public IEnumerator Shot38_WorkbenchTranscript()
        {
            yield return LoadTheGame();
            yield return Frames(2);

            GameObject.Find(MainMenu.WorkbenchButton).GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
            yield return Frames(2);

            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            int fullAdder = -1;
            for (int i = 0; i < WorkbenchSamples.Chip.Count; i++)
                if (WorkbenchSamples.Chip[i].Name.ToLowerInvariant().Contains("full adder"))
                    fullAdder = i;
            workbench.ShowSample(fullAdder);
            yield return Frames(2);
            workbench.Pause();
            workbench.Session.Finish();
            workbench.Seek(WorkbenchSamples.FullAdderEnd);
            workbench.ShowDesign();
            workbench.ShowTranscript();
            yield return Frames(30);

            Assert.IsTrue(workbench.Hierarchy.Rows.Count(r => r.Node.Kind == ScopeNodeKind.Block) == 2, "sanity: the two half adders");
            Assert.IsNotEmpty(workbench.Transcript.Lines, "sanity: the transcript says something");

            yield return Capture("38-workbench-transcript");
        }

        /// <summary>The keyboard map over the reference (Part 11d): every key under RUN, CIRCUIT, WAVES and VIEW, and the gestures.</summary>
        [UnityTest]
        public IEnumerator Shot39_WorkbenchKeys()
        {
            yield return LoadTheGame();
            yield return Frames(2);

            GameObject.Find(MainMenu.WorkbenchButton).GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
            yield return Frames(2);

            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            workbench.ShowSample(1);   // the reference
            yield return Frames(2);
            workbench.Pause();
            workbench.Session.Finish();
            workbench.Seek(13.5);
            workbench.KeyMapPopup.Toggle();
            yield return Frames(30);

            Assert.IsTrue(workbench.KeyMapPopup.IsOpen, "sanity: the map is up");

            yield return Capture("39-workbench-keys");
        }

        /// <summary>Part 12a: the full adder stepped inside h1 -- the half adder drawn with h1's values, the breadcrumb over it.</summary>
        [UnityTest]
        public IEnumerator Shot40_WorkbenchInside()
        {
            yield return LoadTheGame();
            yield return Frames(2);

            GameObject.Find(MainMenu.WorkbenchButton).GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
            yield return Frames(2);

            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            workbench.ShowSample(2);   // the full adder
            yield return Frames(2);
            workbench.Pause();
            workbench.Session.Finish();
            Assert.IsTrue(workbench.StepInto(workbench.Session.Project.TopModule.FindPart("h1").Id), "sanity: h1 is stepped into");
            workbench.Seek(26.5);
            yield return Frames(30);

            yield return Capture("40-workbench-inside");
        }

        /// <summary>Part 12b: the full adder's h2 and OR selected, MAKE BLOCK asking for the new block's name, block1 offered.</summary>
        [UnityTest]
        public IEnumerator Shot41_WorkbenchMakeBlock()
        {
            yield return LoadTheGame();
            yield return Frames(2);

            GameObject.Find(MainMenu.WorkbenchButton).GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
            yield return Frames(2);

            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            workbench.ShowSample(2);   // the full adder
            yield return Frames(2);
            workbench.Pause();
            workbench.Session.Finish();
            workbench.Seek(26.5);
            BitSorter.Board.Module top = workbench.Session.Project.TopModule;
            int h2 = top.FindPart("h2").Id, or = top.Parts.First(p => p is BitSorter.Board.GatePart).Id;
            workbench.Editor.Selection.Pick(new CanvasTarget(CanvasTargetKind.Part, part: h2), false, false);
            workbench.Editor.Selection.Pick(new CanvasTarget(CanvasTargetKind.Part, part: or), extend: true, toggle: false);
            yield return Frames(2);
            workbench.AskBlockName(workbench.ScreenPointOf(new BitSorter.Board.Point(1, -3.9)));   // under the circuit, clear of it
            yield return Frames(30);

            Assert.IsTrue(workbench.BlockNameField.IsOpen, "sanity: the name is asked for");

            yield return Capture("41-workbench-make-block");
        }

        /// <summary>Part 12c: four half adders as one array block, stacked and labelled with its count, COPIES in its inspector.</summary>
        [UnityTest]
        public IEnumerator Shot42_WorkbenchArray()
        {
            yield return LoadTheGame();
            yield return Frames(2);

            GameObject.Find(MainMenu.WorkbenchButton).GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
            yield return Frames(2);

            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            int four = -1;
            for (int i = 0; i < WorkbenchSamples.Chip.Count; i++)
                if (WorkbenchSamples.Chip[i].Name == "Four half adders")
                    four = i;
            workbench.ShowSample(four);
            yield return Frames(2);
            workbench.Pause();
            workbench.Session.Finish();
            workbench.Seek(4 * WorkbenchSamples.FourHalfAddersStep + 4.5);
            int ha = workbench.Session.Project.TopModule.Parts.First(p => p is BitSorter.Board.BlockPart).Id;
            workbench.Editor.Selection.Pick(new CanvasTarget(CanvasTargetKind.Part, part: ha), false, false);
            yield return Frames(30);

            Assert.AreEqual("4", workbench.InspectorTyped()["copies"], "sanity: the inspector shows its copies");

            yield return Capture("42-workbench-array");
        }

        /// <summary>Part 12d: the written adder's code opened in the circuit's window, a fault typed, marked at its line and listed.</summary>
        [UnityTest]
        public IEnumerator Shot43_WorkbenchCode()
        {
            yield return LoadTheGame();
            yield return Frames(2);

            GameObject.Find(MainMenu.WorkbenchButton).GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
            yield return Frames(2);

            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            int written = -1;
            for (int i = 0; i < WorkbenchSamples.Chip.Count; i++)
                if (WorkbenchSamples.Chip[i].Name == "Written adder")
                    written = i;
            workbench.ShowSample(written);
            yield return Frames(2);
            workbench.Pause();
            workbench.Session.Finish();
            workbench.Seek(4 * WorkbenchSamples.WrittenAdderStep + 4.5);
            int add = workbench.Session.Project.TopModule.Parts.First(p => p is BitSorter.Board.BlockPart).Id;
            Assert.IsTrue(workbench.StepInto(add));
            yield return Frames(2);

            // Not focused, so no caret blinks into the picture.
            workbench.CodeView.Field.text = WorkbenchSamples.AdderSource.Replace("= a + b;", "= a + ;");
            for (int i = 0; i < 240 && !workbench.CodeView.ListedText.Any(); i++)
                yield return null;
            yield return Frames(30);

            Assert.IsTrue(workbench.CodeView.ListedText.Any(), "sanity: the fault is listed");
            yield return Capture("43-workbench-code");
        }

        /// <summary>Part 12e: the dock's CODE tab, the full adder as it prints, a chip a file.</summary>
        [UnityTest]
        public IEnumerator Shot44_WorkbenchCodeTab()
        {
            yield return LoadTheGame();
            yield return Frames(2);

            GameObject.Find(MainMenu.WorkbenchButton).GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
            yield return Frames(2);

            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            workbench.ShowSample(2);   // the full adder
            yield return Frames(2);
            workbench.Pause();
            workbench.Session.Finish();
            workbench.Seek(26.5);
            workbench.ShowCode();
            yield return Frames(30);

            Assert.AreEqual("full_adder", workbench.ListingCaption, "sanity: the full adder's file");
            yield return Capture("44-workbench-code-tab");
        }

        /// <summary>Part 13a: the FILE menu open under its chip, over the full adder.</summary>
        [UnityTest]
        public IEnumerator Shot45_WorkbenchFileMenu()
        {
            yield return LoadTheGame();
            yield return Frames(2);

            GameObject.Find(MainMenu.WorkbenchButton).GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
            yield return Frames(2);

            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            workbench.ShowSample(2);   // the full adder
            yield return Frames(2);
            workbench.Pause();
            workbench.Session.Finish();
            workbench.Seek(26.5);
            workbench.OpenFileMenu();
            yield return Frames(30);

            Assert.IsTrue(workbench.FileMenu.IsOpen, "sanity: the menu is open");
            yield return Capture("45-workbench-file-menu");
        }

        /// <summary>
        /// Shot 46 (Part 13d): a project imported from the Import folder -- a mux of gates, drawn, and its testbench, written --
        /// running under its testbench, with what the import said in the TRANSCRIPT. Since Part 15a it opens as a circuit of
        /// its own, its name on the chip, where it went into MY CIRCUIT.
        /// </summary>
        [UnityTest]
        public IEnumerator Shot46_WorkbenchImportedProject()
        {
            Assert.IsNotNull(FileImport.Redirected, "sanity: the Import folder is the capture's scratch one, never the player's");
            System.IO.Directory.CreateDirectory(FileImport.Folder);
            foreach (string old in System.IO.Directory.GetFiles(FileImport.Folder))
                System.IO.File.Delete(old);
            System.IO.File.WriteAllText(System.IO.Path.Combine(FileImport.Folder, "mux2.sv"),
                "module mux2 (input logic a, input logic b, input logic s, output logic y);\n" +
                "    logic s_n, n0, n1;\n" +
                "    INV   #(.Tpdlh(2), .Tpdhl(1)) g0 (.A(s), .Z(s_n));\n" +
                "    NAND2 #(.Tpdlh(3), .Tpdhl(2)) g1 (.A(a), .B(s_n), .Z(n0));\n" +
                "    NAND2 #(.Tpdlh(3), .Tpdhl(2)) g2 (.A(b), .B(s), .Z(n1));\n" +
                "    NAND2 #(.Tpdlh(3), .Tpdhl(2)) g3 (.A(n0), .B(n1), .Z(y));\n" +
                "endmodule\n");
            System.IO.File.WriteAllText(System.IO.Path.Combine(FileImport.Folder, "mux2_tb.sv"),
                "module mux2_tb;\n" +
                "    logic a, b, s, y;\n" +
                "    mux2 dut (.a(a), .b(b), .s(s), .y(y));\n" +
                "    initial begin\n" +
                "        a = 0; b = 1; s = 0;\n" +
                "        #10 a = 1;\n" +
                "        #10 s = 1;\n" +
                "        #10 b = 0;\n" +
                "        #10 $display(\"y is %b\", y);\n" +
                "        $stop;\n" +
                "    end\n" +
                "endmodule\n");

            yield return LoadTheGame();
            yield return Frames(2);

            GameObject.Find(MainMenu.WorkbenchButton).GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
            yield return Frames(2);

            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            workbench.ImportFromFolder();
            yield return Frames(2);
            workbench.Pause();
            workbench.Session.Finish();
            workbench.Seek(23.5);
            workbench.ShowTranscript();
            yield return Frames(30);

            Assert.AreEqual("mux2_tb", workbench.Session.Project.Bench, "sanity: imported, run by its testbench");
            yield return Capture("46-workbench-imported-project");
        }

        /// <summary>Part 14b: the dock's TIMING tab over the full adder, its critical path -- a to cout -- lit on the canvas.</summary>
        [UnityTest]
        public IEnumerator Shot47_WorkbenchTimingTab()
        {
            yield return LoadTheGame();
            yield return Frames(2);

            GameObject.Find(MainMenu.WorkbenchButton).GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
            yield return Frames(2);

            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            workbench.ShowSample(2);   // the full adder
            yield return Frames(2);
            workbench.Pause();
            workbench.Session.Finish();
            workbench.Seek(26.5);
            workbench.ShowTiming();
            yield return Frames(30);

            Assert.AreEqual(workbench.Timing.Critical, workbench.TimingPicked, "sanity: the critical path picked");
            Assert.AreEqual(3, workbench.Overlay.PathParts.Count, "sanity: and lit");
            yield return Capture("47-workbench-timing-tab");
        }

        /// <summary>Part 14d: the full adder held to the course's cells, PARTS offering only them, the LIBRARY menu open over the TIMING tab.</summary>
        [UnityTest]
        public IEnumerator Shot48_WorkbenchLibrary()
        {
            yield return LoadTheGame();
            yield return Frames(2);

            GameObject.Find(MainMenu.WorkbenchButton).GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
            yield return Frames(2);

            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            workbench.ShowSample(2);   // the full adder
            yield return Frames(2);
            workbench.Pause();
            workbench.Session.Finish();
            workbench.Seek(26.5);
            workbench.ShowTiming();
            workbench.SetLibrary(BitSorter.Board.GateLibrary.CourseCells(workbench.Session.Project.Settings.DefaultGateDelay));
            yield return Frames(2);
            workbench.OpenLibraryMenu();
            yield return Frames(30);

            Assert.IsTrue(workbench.LibraryMenu.IsOpen, "sanity: the menu is open");
            yield return Capture("48-workbench-library");
        }

        /// <summary>
        /// Part 14g: two written devices on one wire through their inout pins -- drawn on the right, under each box's output
        /// -- the wire an output of MY CIRCUIT, and the first device driving a 1 onto it while the second lets it go.
        /// </summary>
        [UnityTest]
        public IEnumerator Shot49_WorkbenchDevicesOnABus()
        {
            yield return LoadTheGame();
            yield return Frames(2);

            GameObject.Find(MainMenu.WorkbenchButton).GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
            yield return Frames(2);

            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            workbench.ShowSample(0);   // MY CIRCUIT
            yield return Frames(2);
            WorkbenchSession s = workbench.Session;
            Assert.IsTrue(s.AddWrittenModule("dev").Done);
            Assert.IsTrue(s.Editor.SetSource("dev",
                "module dev (\n    input logic en,\n    input logic d,\n    inout wire io,\n    output logic seen\n);\n" +
                "    bufif1 #1 g (io, d, en);\n    assign seen = io;\nendmodule\n").Done);

            int Place(string key, int x, int y)
            {
                BitSorter.Board.EditResult placed = s.Place(PartsCatalog.Find(s.Project, s.Module, key), new BitSorter.Board.Placement(x, y));
                Assert.IsTrue(placed.Done, placed.Refusal);
                return placed.Id;
            }

            void Wire(int from, string fromPin, int to, string toPin) =>
                Assert.IsTrue(s.Connect(BitSorter.Board.Endpoint.At(from, fromPin), BitSorter.Board.Endpoint.At(to, toPin)).Done);

            int e1 = Place("input", 0, 0), d1 = Place("input", 0, 2), e2 = Place("input", 0, 5), d2 = Place("input", 0, 7);
            int u1 = Place("block:dev", 5, 1), u2 = Place("block:dev", 5, 6);
            int y = Place("output", 11, 4);
            Wire(e1, "out", u1, "en");
            Wire(d1, "out", u1, "d");
            Wire(e2, "out", u2, "en");
            Wire(d2, "out", u2, "d");
            Wire(u1, "io", y, "in");
            Wire(u2, "io", y, "in");

            string Name(int id) => s.Project.TopModule.FindPart(id).Name;
            foreach (int input in new[] { e1, d1 })
                Assert.IsTrue(s.SetInput(Name(input), 0, 40, BitSorter.SystemVerilog.LogicVec.FromUInt64(1, 1)).Done);
            yield return Frames(2);
            workbench.Pause();
            s.Finish();
            workbench.Seek(20.5);
            workbench.FitView();
            yield return Frames(30);

            Assert.IsFalse(s.Editor.Diagnostics.Any(d => d.IsError), string.Join("\n", s.Editor.Diagnostics));
            yield return Capture("49-workbench-devices-on-a-bus");
        }

        /// <summary>
        /// Part 15a: what BitSorter opens on with nothing open last -- the start page, the app's menus over it, and three
        /// circuits of the student's in RECENT, the one opened last first.
        /// </summary>
        [UnityTest]
        public IEnumerator Shot50_StartPage()
        {
            var store = new ProgressStore(ProgressStore.DefaultPath);
            store.Load();
            foreach (string name in new[] { "traffic light", "alu", "lab 3 adder" })
            {
                store.SetProject(Documents.KeyOf(name), BitSorter.Board.Project.New(Documents.ModuleNameFor(name)));
                store.Touch(Documents.KeyOf(name));
            }
            store.Save();

            yield return TestScene.LoadSimulator();
            Look.ApplyBloom();
            yield return Frames(30);

            Assert.IsTrue(Find<WorkbenchPanel>().StartPageShowing, "sanity: nothing was open last");
            yield return Capture("50-start-page");
        }

        private static IEnumerator OpensOnItsCircuit(string level, string fixture, string shot)
        {
            yield return LoadTheGame();
            FixTheSparks();

            // The board as it opens, not the lessons that come up over it on a first visit: the
            // held wire-delay hint, the register's hint and the chapter card.
            ProgressStore store = Find<ProgressTracker>().Store;
            store.MarkMilestone(TutorialLevel.Key);
            store.MarkMilestone(ChapterCard.Milestone);
            store.MarkHintSeen(HintRules.WireDelay);
            store.MarkHintSeen(HintRules.Register);
            Find<MainMenu>().Show(false);

            LevelSession session = Find<LevelSession>();
            SimulationRunner runner = Find<SimulationRunner>();

            Assert.IsTrue(session.LoadLevel(level), $"{level} did not load");

            for (int frame = 0; frame < 60 && !runner.FixtureNodeIds.ContainsKey(fixture); frame++)
                yield return null;

            Assert.IsTrue(session.HasStart && session.IsAtStart, $"sanity: {level} did not open on its circuit");

            yield return Frames(30);
            yield return Capture(shot);
        }

        private static bool AnyCollisionTakesBoth(SimulationRunner runner)
        {
            for (int id = 0; id < runner.View.EdgeCount; id++)
            {
                Edge edge = runner.View.GetEdge(id);

                if (edge != null && PortState.WillCollide(edge, out bool heldBitDies) && heldBitDies)
                    return true;
            }

            return false;
        }

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
        /// Whether the tutorial has a ring up on the interface -- round a button or a parts-list
        /// row -- rather than on the board, where its rings share the name.
        /// </summary>
        private static bool RingOnTheInterface()
        {
            foreach (Image ring in Object.FindObjectsByType<Image>(FindObjectsSortMode.None))
            {
                if (ring.name == "Tutorial ring" && ring.gameObject.activeInHierarchy)
                    return true;
            }

            return false;
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

        /// <summary>
        /// low = d0 s0' + d1 s0, high = d2 s0' + d3 s0, out = low s1' + high s1 -- the circuit
        /// FourLanesLevelTests solves the level with.
        /// </summary>
        private static IEnumerator BuildFourLanes()
        {
            LevelSession session = Find<LevelSession>();
            SimulationRunner runner = Find<SimulationRunner>();

            var notS1 = new Vector2Int(-3, -1);
            var notS0 = new Vector2Int(-3, -2);
            var andD0 = new Vector2Int(-1, 3);
            var andD1 = new Vector2Int(-1, 2);
            var andD2 = new Vector2Int(-1, 1);
            var andD3 = new Vector2Int(-1, 0);
            var orLow = new Vector2Int(1, 2);
            var orHigh = new Vector2Int(1, 0);
            var andLow = new Vector2Int(3, 2);
            var andHigh = new Vector2Int(3, -1);
            var orOut = new Vector2Int(4, 0);

            foreach (Vector2Int cell in new[] { notS1, notS0 })
                Assert.IsTrue(session.TryPlaceGate(GateKind.Not, cell), $"could not place a NOT on {cell}");

            foreach (Vector2Int cell in new[] { andD0, andD1, andD2, andD3, andLow, andHigh })
                Assert.IsTrue(session.TryPlaceGate(GateKind.And, cell), $"could not place an AND on {cell}");

            foreach (Vector2Int cell in new[] { orLow, orHigh, orOut })
                Assert.IsTrue(session.TryPlaceGate(GateKind.Or, cell), $"could not place an OR on {cell}");

            int Fixture(string id) => runner.FixtureNodeIds[id];
            int On(Vector2Int cell) => NodeOn(runner, cell);

            Wire(session, Fixture("s0"), On(notS0), 0);
            Wire(session, Fixture("d0"), On(andD0), 0, delay: 2);
            Wire(session, On(notS0), On(andD0), 1);
            Wire(session, Fixture("d2"), On(andD2), 0, delay: 2);
            Wire(session, On(notS0), On(andD2), 1);
            Wire(session, Fixture("d1"), On(andD1), 0);
            Wire(session, Fixture("s0"), On(andD1), 1);
            Wire(session, Fixture("d3"), On(andD3), 0);
            Wire(session, Fixture("s0"), On(andD3), 1);

            Wire(session, On(andD0), On(orLow), 0);
            Wire(session, On(andD1), On(orLow), 1, delay: 2);
            Wire(session, On(andD2), On(orHigh), 0);
            Wire(session, On(andD3), On(orHigh), 1, delay: 2);

            Wire(session, Fixture("s1"), On(notS1), 0);
            Wire(session, On(orLow), On(andLow), 0);
            Wire(session, On(notS1), On(andLow), 1, delay: 3);
            Wire(session, On(orHigh), On(andHigh), 0);
            Wire(session, Fixture("s1"), On(andHigh), 1, delay: 4);

            Wire(session, On(andLow), On(orOut), 0);
            Wire(session, On(andHigh), On(orOut), 1);
            Wire(session, On(orOut), Fixture("out"), 0);

            yield return null;
        }

        /// <summary>
        /// The ripple FourAtOnceLevelTests solves the level with: bit i's A and B wait 2i + 1 ticks,
        /// CIN two, and each box's carry out goes to the carry in of the box below.
        /// </summary>
        private static void BuildFourAtOnce()
        {
            LevelSession session = Find<LevelSession>();
            SimulationRunner runner = Find<SimulationRunner>();
            BlockDefinition fa = session.Level.BlockNamed("FA");

            foreach (Vector2Int cell in new[] { new Vector2Int(-4, 3), new Vector2Int(-2, 1), new Vector2Int(0, -1), new Vector2Int(2, -3) })
                Assert.IsTrue(session.TryPlaceBlock(fa, cell), $"could not place an FA on {cell}");

            // Each wire rebuilds the board, so a box is looked up afresh every time. Its anchors keep
            // their ids -- wires add edges, not nodes -- but nothing here needs to know that.
            int Fixture(string id) => runner.FixtureNodeIds[id];
            BuiltBlock Box(int bit) => runner.Circuit.Blocks[bit];

            for (int bit = 0; bit < 4; bit++)
            {
                Wire(session, Fixture("a" + bit), Box(bit).InputAnchors[0], 0, delay: 2 * bit + 1);
                Wire(session, Fixture("b" + bit), Box(bit).InputAnchors[1], 0, delay: 2 * bit + 1);
                Wire(session, Box(bit).OutputAnchors[0], Fixture("s" + bit), 0);
            }

            Wire(session, Fixture("cin"), Box(0).InputAnchors[2], 0, delay: 2);

            for (int bit = 0; bit < 3; bit++)
                Wire(session, Box(bit).OutputAnchors[1], Box(bit + 1).InputAnchors[2], 0);

            Wire(session, Box(3).OutputAnchors[1], Fixture("cout"), 0);
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
            bool fading = UiFade.AnyMoving;

            for (int frame = 0; frame < 240 && UiFade.AnyMoving; frame++)
                yield return null;

            Assert.IsFalse(UiFade.AnyMoving, $"'{name}' was about to be captured with a panel still fading in");

            // The HUD under a panel steps aside in the update after the panel has covered it. Only
            // when there was a fade to wait for: an extra frame on every shot would move every
            // animation in every shot, and the shots are compared pixel for pixel.
            if (fading)
                yield return null;

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

        private static void Wire(LevelSession session, int from, int to, int toPort, int delay = 1)
        {
            Assert.IsTrue(
                session.TryConnect(new PortAddress(from, false, 0), new PortAddress(to, true, toPort), delay),
                $"could not wire {from} to {to}");
        }
    }
}
