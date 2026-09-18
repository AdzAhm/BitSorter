using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using BitSorter.View;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
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
        // Helpers
        // -----------------------------------------------------------------

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
