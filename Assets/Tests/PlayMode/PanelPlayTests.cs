using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using BitSorter.View;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace BitSorter.PlayMode.Tests
{
    /// <summary>
    /// Full-screen panels: what one key press may do to them, and what shows through them.
    /// </summary>
    /// <remarks>
    /// Two panels reading one key in one frame each decide from what the other has done so far,
    /// and Unity does not define which of them updates first. The sandbox's setup closed on
    /// Escape and the level list opened on Escape "when nothing else is open" -- so whenever the
    /// setup updated first, one press closed it and opened the list, and the player's next click
    /// loaded a level. The tests here take both Updates out of Unity's hands and run them in the
    /// order that went wrong.
    ///
    /// The panel that closes is a stand-in rather than the sandbox's, so the rule is pinned for any
    /// panel that closes on a key, whatever the sandbox becomes.
    /// </remarks>
    [TestFixture]
    public class PanelPlayTests : InputTestFixture
    {
        /// <summary>The HUD pieces that must not show through a full-screen panel.</summary>
        private static readonly string[] HudRoots = { "Status", "Run controls", "Palette", "Help badge" };

        private Keyboard _keyboard;

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

        public override void Setup()
        {
            base.Setup();

            // InputTestFixture switches on the Input System's read-value caching, and a self-check
            // of that cache, which the game itself never runs with -- the project sets no input
            // feature flags. Once the game had already run earlier in the same play session, the
            // self-check fired on the queued Escape inside the Input System's own update, and the
            // error it logged failed both Escape tests with every assertion passing. Off, every read
            // comes straight from device state, which is how the shipped game reads it.
            InputSystem.settings.SetInternalFeatureFlag("USE_READ_VALUE_CACHING", false);

            _keyboard = InputSystem.AddDevice<Keyboard>();
            InputSystem.AddDevice<Mouse>();
        }

        public override void TearDown()
        {
            SaveGuard.Clear();
            base.TearDown();
        }

        [UnityTearDown]
        public IEnumerator ClearTheScene()
        {
            yield return TestScene.Clear();
        }

        private static T Find<T>() where T : Object => Object.FindFirstObjectByType<T>();

        /// <summary>The game boots into the main menu, which holds the screen until closed.</summary>
        private static IEnumerator CloseTheMainMenu()
        {
            Find<MainMenu>().Show(false);
            yield return null;
            yield return null;
        }

        // -----------------------------------------------------------------
        // One key, one panel
        // -----------------------------------------------------------------

        [UnityTest]
        public IEnumerator AnEscapeThatClosesOnePanel_DoesNotOpenAnother()
        {
            yield return TestScene.Load();
            yield return CloseTheMainMenu();

            Assert.IsFalse(UiModal.AnyOpen, "sanity: nothing should be open once the menu is closed");

            LevelSelectPanel levels = Find<LevelSelectPanel>();
            levels.enabled = false;   // its Update is called by hand below

            var panel = new GameObject("Escape-closed panel").AddComponent<EscapeClosedPanel>();
            panel.Open();

            yield return PressEscape();

            // The order Unity is free to choose, fixed to the one that went wrong: the panel that
            // closes looks first, so the level list finds nothing open.
            panel.Tick();
            FrameOf(levels)();

            Assert.IsFalse(panel.IsOpen, "sanity: Escape should have closed the stand-in panel");
            Assert.IsFalse(UiModal.AnyOpen,
                "one Escape closed a panel and opened the level list as well");

            Release(_keyboard.escapeKey);
            levels.enabled = true;
        }

        /// <summary>
        /// The positive control: with nothing to close, Escape does open the list.
        /// </summary>
        /// <remarks>
        /// Without this the test above could pass by the level list never reacting to a hand-made
        /// Escape at all.
        /// </remarks>
        [UnityTest]
        public IEnumerator AnEscapeWithNothingOpen_StillOpensTheLevelList()
        {
            yield return TestScene.Load();
            yield return CloseTheMainMenu();

            LevelSelectPanel levels = Find<LevelSelectPanel>();
            levels.enabled = false;

            yield return PressEscape();
            FrameOf(levels)();

            Assert.IsTrue(UiModal.AnyOpen, "Escape with nothing open should open the level list");

            Release(_keyboard.escapeKey);
            levels.enabled = true;
        }

        // -----------------------------------------------------------------
        // What shows through
        // -----------------------------------------------------------------

        /// <summary>
        /// While a full-screen panel is up, the HUD is not drawn beside it.
        /// </summary>
        /// <remarks>
        /// The panels' backdrop never reached the edges of the screen, which is where the HUD
        /// lives, so the banner, the run buttons and the parts list stayed fully lit next to a
        /// "full-screen" panel -- and the panels' own titles and help lines printed straight over
        /// the banner's and the buttons'.
        /// </remarks>
        [UnityTest]
        public IEnumerator WhileAFullScreenPanelIsOpen_TheHudIsNotDrawn()
        {
            yield return TestScene.Load();

            Assert.IsTrue(UiModal.AnyOpen, "sanity: the game boots into the main menu");

            foreach (string name in HudRoots)
                Assert.IsNull(GameObject.Find(name), $"'{name}' is drawn beside the main menu");
        }

        /// <summary>The positive control: the same lookups find the HUD once nothing is open.</summary>
        [UnityTest]
        public IEnumerator WithNothingOpen_TheHudIsDrawn()
        {
            yield return TestScene.Load();
            yield return CloseTheMainMenu();

            foreach (string name in HudRoots)
                Assert.IsNotNull(GameObject.Find(name), $"'{name}' is missing with nothing open");
        }

        // -----------------------------------------------------------------
        // What a panel's backdrop may be drawn on
        // -----------------------------------------------------------------

        /// <summary>
        /// The panel sprite is never drawn smaller than its two corners, on the menu or the HUD.
        /// </summary>
        /// <remarks>
        /// It is nine-sliced with ten-pixel corners, and anything shorter than two of them squeezes
        /// the corners together instead of repeating a middle -- so CLAUDE.md has that anything
        /// shorter replaces the sprite, as the help badge and the clock's pips do. The menu's rule
        /// under the title did not: two pixels tall, it drew as a squashed rounded slab, which read
        /// as a line only because a filled slab is one colour throughout. A panel with a drawn edge
        /// has an edge and a middle, and the rule vanished into two dots.
        /// </remarks>
        [UnityTest]
        public IEnumerator APanelBackdrop_IsNeverSmallerThanItsCorners()
        {
            yield return TestScene.Load();
            yield return null;

            int onTheMenu = PanelsTooSmallForTheirCorners(out string menuReport);
            Assert.Greater(onTheMenu, 0, "sanity: the main menu draws panels");
            Assert.IsEmpty(menuReport, "on the main menu");

            yield return CloseTheMainMenu();

            int onTheHud = PanelsTooSmallForTheirCorners(out string hudReport);
            Assert.Greater(onTheHud, 0, "sanity: the HUD draws panels");
            Assert.IsEmpty(hudReport, "on the HUD");
        }

        /// <summary>
        /// Checks every panel on screen against its corners; returns how many it checked, and names
        /// any that are too small.
        /// </summary>
        private static int PanelsTooSmallForTheirCorners(out string report)
        {
            Sprite panel = ProceduralSprites.Panel();
            var failures = new System.Text.StringBuilder();
            int checkedPanels = 0;

            foreach (Image image in Object.FindObjectsByType<Image>(FindObjectsSortMode.None))
            {
                if (image.sprite != panel || !image.isActiveAndEnabled)
                    continue;

                checkedPanels++;
                Vector2 size = image.rectTransform.rect.size;
                float tall = (panel.border.y + panel.border.w) / image.pixelsPerUnit;
                float wide = (panel.border.x + panel.border.z) / image.pixelsPerUnit;

                if (size.y < tall || size.x < wide)
                    failures.Append($"'{image.name}' is {size.x} by {size.y}, and its corners need {wide} by {tall}. ");
            }

            report = failures.ToString();
            return checkedPanels;
        }

        // -----------------------------------------------------------------
        // What is already on screen when a panel opens
        // -----------------------------------------------------------------

        /// <summary>
        /// A hint up when a full-screen panel opens is held, not spent.
        /// </summary>
        /// <remarks>
        /// A first-time hint is marked seen the moment it is raised, not when it is read:
        /// FirstTimeHints.TryShow calls MarkHintSeen and only then calls Show. There are four of
        /// these hints in the whole game and each fires once ever per save, so nine seconds
        /// running out behind a level list is a mechanic the player is never told about again.
        ///
        /// FirstTimeHints already refuses to raise one while a panel is open. Nothing covered the
        /// other order: a hint already up when the panel opens.
        /// </remarks>
        [UnityTest]
        public IEnumerator AHintUpWhenAPanelOpens_IsHeldRatherThanSpent()
        {
            yield return TestScene.Load();
            yield return CloseTheMainMenu();

            HintBanner banner = Find<HintBanner>();
            banner.Show("Gates fire only when every input is holding a bit.");
            yield return null;

            Assert.IsTrue(banner.IsShowing, "sanity: the hint should be up");
            Assert.IsNotNull(GameObject.Find("Hint"), "sanity: the hint should be drawn");

            float before = Remaining(banner);

            var panel = new GameObject("stand-in").AddComponent<EscapeClosedPanel>();
            panel.Open();

            // Several frames, so a countdown that is still running has time to show it.
            for (int frame = 0; frame < 5; frame++)
                yield return null;

            Assert.IsNull(GameObject.Find("Hint"),
                "the hint is drawn over by the panel rather than stepping aside for it");

            Assert.AreEqual(before, Remaining(banner), 0.0001f,
                "the hint spent its nine seconds behind a full-screen panel -- it is marked seen, " +
                "so the player never gets it again");

            Assert.IsTrue(banner.IsShowing,
                "the hint gave up while it was covered, which is the same loss by another route");

            Object.DestroyImmediate(panel.gameObject);
        }

        /// <summary>
        /// The solved card and the bins lighting up both step aside for a full-screen panel.
        /// </summary>
        /// <remarks>
        /// The win panel is deliberately not a modal -- it is a card on the board, not a takeover --
        /// but that settles what it does to other panels, not what they do to it. It hid from
        /// nothing: it merely skipped coming to the front, so a scrim at 0.78 went over a live
        /// panel and the card bled through it. A run that passed while the level list was open was
        /// worse, activating the card behind the scrim at whatever sibling index it happened to
        /// hold, with no BringToFront ever to correct it.
        ///
        /// The celebration is the same fact on the board rather than on the canvas: glows pulsing
        /// at 2.6 Hz behind a menu are motion where the player is reading.
        /// </remarks>
        [UnityTest]
        public IEnumerator OverASolvedBoard_TheCardAndTheCelebration_StepAsideForAPanel()
        {
            yield return TestScene.Load();
            yield return SkipTheTutorial();
            yield return CloseTheMainMenu();

            yield return SolveTheFirstLevel();

            WinPanel win = Find<WinPanel>();
            Assert.IsTrue(win.IsShowing, "sanity: solving the level should show the card");
            Assert.IsNotNull(GameObject.Find("Win"), "sanity: the card should be drawn");
            Assert.Greater(LitGlows(), 0, "sanity: the bins should be lit");

            var panel = new GameObject("stand-in").AddComponent<EscapeClosedPanel>();
            panel.Open();
            yield return null;

            Assert.IsNull(GameObject.Find("Win"),
                "the solved card is still drawn under the panel's scrim");

            Assert.AreEqual(0, LitGlows(), "the bins keep pulsing behind the panel");

            // It stepped aside rather than gave up: the director and the music both read IsShowing
            // to know the card is still owed, and a card that forgot itself here would let the
            // tutorial's ending card through early.
            Assert.IsTrue(win.IsShowing,
                "the card forgot it was owed rather than stepping aside for the panel");

            Object.DestroyImmediate(panel.gameObject);
            yield return null;

            Assert.IsNotNull(GameObject.Find("Win"), "the card did not come back when the panel closed");
        }

        // -----------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------

        /// <summary>
        /// Marks the tutorial done on the scratch save, so closing the menu does not launch it.
        /// </summary>
        /// <remarks>
        /// SaveGuard hands every fixture a fresh save, and a fresh save is exactly the condition
        /// the tutorial offers itself on -- it adopts its own board the moment the menu closes,
        /// which takes the level out from under anything loaded afterwards.
        /// </remarks>
        private static IEnumerator SkipTheTutorial()
        {
            Find<ProgressTracker>().Store.MarkMilestone(TutorialLevel.Key);
            yield return null;
        }

        /// <summary>How long a hint has left, which is private because nothing but a test needs it.</summary>
        private static float Remaining(HintBanner banner)
        {
            FieldInfo field = typeof(HintBanner).GetField(
                "_remaining", BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.IsNotNull(field, "HintBanner no longer keeps its countdown in _remaining");
            return (float)field.GetValue(banner);
        }

        /// <summary>The celebration glows that are currently drawn.</summary>
        private static int LitGlows()
        {
            GameObject container = GameObject.Find("Sink celebration");

            if (container == null)
                return 0;

            int lit = 0;

            foreach (SpriteRenderer glow in container.GetComponentsInChildren<SpriteRenderer>())
            {
                if (glow.gameObject.activeInHierarchy && glow.enabled)
                    lit++;
            }

            return lit;
        }

        /// <summary>
        /// Solves route-the-bit the intended way -- in, through a NOT, into binOne -- and lets the
        /// pass settle.
        /// </summary>
        private static IEnumerator SolveTheFirstLevel()
        {
            LevelSession session = Find<LevelSession>();
            SimulationRunner runner = Find<SimulationRunner>();

            Assert.IsTrue(session.LoadLevel("route-the-bit"), "the level did not load");
            yield return null;

            var middle = new Vector2Int(0, 0);
            Assert.IsTrue(session.TryPlaceGate(GateKind.Not, middle), "could not place the NOT gate");

            int gate = -1;

            for (int id = 0; id < runner.View.NodeCount; id++)
            {
                if (runner.TryCellOf(id, out Vector2Int at) && at == middle)
                    gate = id;
            }

            Assert.AreNotEqual(-1, gate, "nothing on the middle cell");

            Assert.IsTrue(session.TryConnect(
                new PortAddress(runner.FixtureNodeIds["in"], false, 0),
                new PortAddress(gate, true, 0)), "could not wire the source to the gate");

            Assert.IsTrue(session.TryConnect(
                new PortAddress(gate, false, 0),
                new PortAddress(runner.FixtureNodeIds["binOne"], true, 0)),
                "could not wire the gate to the bin");

            session.Run();

            for (int tick = 0; tick < 100 && !runner.IsIdle(); tick++)
                runner.StepOneTick();

            Assert.IsTrue(runner.IsIdle(), "the run never came to a standstill");

            // Two frames: one for the session to settle the run, one for the panel to see it.
            yield return null;
            yield return null;

            Assert.AreEqual(RunState.Passed, session.State, "sanity: the intended answer should pass");
        }

        /// <summary>
        /// Presses Escape and returns inside the frame that processed it.
        /// </summary>
        /// <remarks>
        /// Queued rather than applied on the spot, so it arrives the way a real key does: in the
        /// input update at the start of a frame. The coroutine resumes after that frame's Updates,
        /// which is still "this frame" as far as wasPressedThisFrame is concerned.
        /// </remarks>
        private IEnumerator PressEscape()
        {
            Press(_keyboard.escapeKey, queueEventOnly: true);
            yield return null;

            Assert.IsTrue(_keyboard.escapeKey.wasPressedThisFrame, "sanity: Escape is this frame's press");
        }

        /// <summary>A component's own Update, callable without Unity.</summary>
        private static Action FrameOf(MonoBehaviour component)
        {
            MethodInfo update = component.GetType().GetMethod(
                "Update", BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.IsNotNull(update, $"{component.GetType().Name} no longer has an Update to call");

            return (Action)update.CreateDelegate(typeof(Action), component);
        }
    }

    /// <summary>
    /// A full-screen panel that closes on Escape, driven by hand.
    /// </summary>
    /// <remarks>
    /// Its check is <see cref="Tick"/> rather than Update so Unity never runs it: the test decides
    /// when it looks at the key.
    /// </remarks>
    internal sealed class EscapeClosedPanel : MonoBehaviour
    {
        public bool IsOpen { get; private set; }

        public void Open()
        {
            IsOpen = true;
            UiModal.Opened(this);
        }

        public void Tick()
        {
            Keyboard keyboard = Keyboard.current;

            if (!IsOpen || keyboard == null || !keyboard.escapeKey.wasPressedThisFrame)
                return;

            IsOpen = false;
            UiModal.Closed(this);
        }

        private void OnDestroy() => UiModal.Closed(this);
    }
}
