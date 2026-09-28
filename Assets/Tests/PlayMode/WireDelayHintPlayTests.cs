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
    /// The first-time hint about wire delay stays up until the player lengthens a wire, and counts as
    /// seen only then.
    /// </summary>
    /// <remarks>
    /// From a playtest, 2026-09-28: re-timing a wire was "never introduced". It was -- for nine
    /// seconds, taken down by any click, and marked seen the moment it went up. It comes up as soon
    /// as the first wire is in, which is the middle of wiring, so the click that started the next
    /// wire took it down unread and the save never offered it again.
    ///
    /// The scroll here is the wheel, over the wire, through the Input System: the hint has to go on
    /// the gesture it teaches, not on a call a test can make and a player cannot.
    /// </remarks>
    [TestFixture]
    public class WireDelayHintPlayTests : InputTestFixture
    {
        /// <summary>The first level with a delay budget, which is where the hint comes up.</summary>
        private const string Level = "balance-the-paths";

        private Mouse _mouse;

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

            // As in PanelPlayTests: the read-value cache's self-check, which the shipped game never
            // runs with, logs an error on queued input once the game has run earlier in the session.
            InputSystem.settings.SetInternalFeatureFlag("USE_READ_VALUE_CACHING", false);

            InputSystem.AddDevice<Keyboard>();
            _mouse = InputSystem.AddDevice<Mouse>();
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

        private static ProgressStore Store => Find<ProgressTracker>().Store;

        /// <summary>
        /// The game on <see cref="Level"/> with a wire from A into the bin and the wire-delay hint up,
        /// its time on screen cut to half a second so running out is quick to reach.
        /// </summary>
        private IEnumerator TheHintUpOverAWire()
        {
            yield return TestScene.Load();

            // A fresh save is exactly what the tutorial offers itself on.
            Store.MarkMilestone(TutorialLevel.Key);

            Find<MainMenu>().Show(false);
            yield return null;
            yield return null;

            SetPrivate(Find<HintBanner>(), "_seconds", 0.5f);

            LevelSession session = Find<LevelSession>();
            SimulationRunner runner = Find<SimulationRunner>();

            Assert.IsTrue(session.LoadLevel(Level), $"sanity: '{Level}' did not load");
            yield return null;
            yield return null;

            Assert.IsFalse(Store.HasSeenHint(HintRules.WireDelay), "sanity: a fresh save has seen the hint");

            Assert.IsTrue(session.TryConnect(
                    new PortAddress(runner.FixtureNodeIds["a"], false, 0),
                    new PortAddress(runner.FixtureNodeIds["out"], true, 0)),
                "sanity: could not wire A into the bin");

            HintBanner banner = Find<HintBanner>();

            for (int frame = 0; frame < 120 && !Showing(HintRules.WireDelay); frame++)
                yield return null;

            Assert.IsTrue(Showing(HintRules.WireDelay), "sanity: the wire-delay hint did not come up");
            Assert.IsTrue(banner.IsShowing, "sanity: the banner says it is not showing a hint");
        }

        [UnityTest]
        public IEnumerator TheWireDelayHint_OutlastsItsTimeAndAClick_AndGoesWhenAWireIsScrolled()
        {
            yield return TheHintUpOverAWire();

            HintBanner banner = Find<HintBanner>();

            // Three times its time on screen. Waited on a condition, with a cap that fails loudly.
            float shownAt = Time.time;

            for (int frame = 0; frame < 1200 && Time.time - shownAt < 1.5f; frame++)
                yield return null;

            Assert.IsTrue(Showing(HintRules.WireDelay), "the wire-delay hint ran out before any wire was lengthened");

            // A click over the status banner, where it lands on nothing on the board.
            Set(_mouse.position, new Vector2(Screen.width * 0.5f, Screen.height - 40f));
            yield return null;
            Press(_mouse.leftButton);
            yield return null;
            Release(_mouse.leftButton);
            yield return null;

            Assert.IsTrue(Showing(HintRules.WireDelay), "a click took the wire-delay hint down before any wire was lengthened");
            Assert.IsFalse(Store.HasSeenHint(HintRules.WireDelay),
                "the wire-delay hint counts as seen before any wire was lengthened");

            yield return ScrollUpOverTheWire();

            Assert.AreEqual(2, Find<LevelSession>().Blueprint.Wires[0].Delay, "sanity: the scroll did not lengthen the wire");
            Assert.IsFalse(banner.IsShowing, "the wire-delay hint stayed up after a wire was lengthened");
            Assert.IsTrue(Store.HasSeenHint(HintRules.WireDelay), "lengthening a wire did not count the hint as seen");
        }

        /// <summary>
        /// Leaving the level without lengthening a wire leaves the hint for the next level that
        /// budgets delay.
        /// </summary>
        [UnityTest]
        public IEnumerator LeavingTheLevel_WithoutLengtheningAWire_KeepsTheHintForLater()
        {
            yield return TheHintUpOverAWire();

            LevelSession session = Find<LevelSession>();
            Assert.IsTrue(session.LoadLevel(session.AvailableLevels[0]), "sanity: the first level did not load");
            yield return null;
            yield return null;

            Assert.IsFalse(Find<HintBanner>().IsShowing, "sanity: the hint should go with the level it was about");
            Assert.IsFalse(Store.HasSeenHint(HintRules.WireDelay),
                "leaving the level without lengthening a wire used the wire-delay hint up");
        }

        /// <summary>
        /// A collision cuts in over the wire-delay hint, and the wire-delay hint comes back after it.
        /// </summary>
        /// <remarks>
        /// A collision is the most alarming thing on the board and is explained at once, whatever
        /// is up. The wire-delay hint has not been read to its end -- it is not seen until a wire is
        /// lengthened -- so it takes the line back once the collision's has had its time.
        /// </remarks>
        [UnityTest]
        public IEnumerator ACollision_CutsIn_AndTheWireDelayHintComesBack()
        {
            yield return TheHintUpOverAWire();

            LevelSession session = Find<LevelSession>();
            SimulationRunner runner = Find<SimulationRunner>();

            // B into the same port as A: the first vector's two bits arrive together and collide.
            Assert.IsTrue(session.TryConnect(
                    new PortAddress(runner.FixtureNodeIds["b"], false, 0),
                    new PortAddress(runner.FixtureNodeIds["out"], true, 0)),
                "sanity: could not wire B into the bin as well");

            session.Run();
            runner.SetPaused(true);

            for (int tick = 0; tick < 10 && runner.View.CorruptedCount == 0; tick++)
            {
                runner.StepOneTick();
                yield return null;
            }

            Assert.Greater(runner.View.CorruptedCount, 0, "sanity: the two wires into one port did not collide");

            for (int frame = 0; frame < 120 && !Showing(HintRules.Collision); frame++)
                yield return null;

            Assert.IsTrue(Showing(HintRules.Collision), "the collision was not explained while the wire-delay hint was up");

            // To the end of the run, where a wire can be scrolled again and the hint has a use.
            for (int tick = 0; tick < 80 && !session.RunIsOver; tick++)
            {
                runner.StepOneTick();
                yield return null;
            }

            Assert.IsTrue(session.RunIsOver, "sanity: the run did not finish");

            float shownAt = Time.time;

            for (int frame = 0; frame < 1200 && !Showing(HintRules.WireDelay) && Time.time - shownAt < 3f; frame++)
                yield return null;

            Assert.IsTrue(Showing(HintRules.WireDelay), "the wire-delay hint did not come back after the collision's");
            Assert.IsFalse(Store.HasSeenHint(HintRules.WireDelay),
                "the wire-delay hint counts as seen, though no wire was lengthened");
        }

        /// <summary>One notch of the wheel, up, over the middle of the first wire.</summary>
        private IEnumerator ScrollUpOverTheWire()
        {
            SimulationRunner runner = Find<SimulationRunner>();
            LevelSession session = Find<LevelSession>();

            int a = runner.FixtureNodeIds["a"];
            int bin = runner.FixtureNodeIds["out"];

            Vector2 middle = (PortGeometry.PositionOf(runner.PositionOf(a), false, 0, 1)
                              + PortGeometry.PositionOf(runner.PositionOf(bin), true, 0, 1)) * 0.5f;

            Assert.AreEqual(1, session.Blueprint.Wires[0].Delay, "sanity: the wire should start at one tick");

            Vector3 screen = Camera.main.WorldToScreenPoint(new Vector3(middle.x, middle.y, 0f));
            Set(_mouse.position, new Vector2(screen.x, screen.y));
            yield return null;
            yield return null;

            Set(_mouse.scroll, new Vector2(0f, 120f));
            yield return null;
            yield return null;
            yield return null;
        }

        /// <summary>Whether the hint banner is up and saying the hint called <paramref name="id"/>.</summary>
        private static bool Showing(string id)
        {
            HintBanner banner = Find<HintBanner>();

            if (banner == null || !banner.IsShowing)
                return false;

            GameObject text = GameObject.Find("hint text");
            return text != null && text.GetComponent<TMPro.TextMeshProUGUI>().text == HintRules.TextFor(id);
        }

        private static void SetPrivate(object target, string name, float value)
        {
            FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, $"{target.GetType().Name} no longer has {name}");
            field.SetValue(target, value);
        }
    }
}
