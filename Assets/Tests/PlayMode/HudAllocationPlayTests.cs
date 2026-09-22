using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using BitSorter.View;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;
using Object = UnityEngine.Object;

namespace BitSorter.PlayMode.Tests
{
    /// <summary>
    /// The two HUD components that run every frame draw nothing new when nothing has changed.
    /// </summary>
    /// <remarks>
    /// The status banner and the parts list used to format their text every frame -- a level title,
    /// a verdict, a count per row -- and hand TextMeshPro a string equal to the one it already had.
    /// Nothing on screen changed, but every frame left garbage behind, and a browser build is where
    /// the collections that garbage causes are felt.
    ///
    /// Each component's own Update is called directly, through a delegate, so the measurement
    /// covers that method and nothing else Unity does in a frame.
    ///
    /// **An assertion that nothing allocates is only worth something next to proof that the
    /// measurement can see an allocation** -- a component whose Update had quietly stopped doing
    /// anything would pass it. The redraw tests at the bottom are that proof: they change what the
    /// component shows and require the very next call to allocate.
    /// </remarks>
    [TestFixture]
    public class HudAllocationPlayTests
    {
        /// <summary>A level with a delay budget, so the parts list draws its delay line as well.</summary>
        private const string Level = "balance-the-paths";

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

        private static IEnumerator LoadLevel()
        {
            yield return TestScene.Load();

            // The HUD steps aside for the main menu the game boots into, and a HUD that is not drawn
            // has nothing to measure.
            Find<MainMenu>().Show(false);
            yield return null;

            Assert.IsTrue(Find<LevelSession>().LoadLevel(Level), "the level did not load");

            // One frame for the parts list to build its rows, one for everything to draw them.
            yield return null;
            yield return null;
        }

        // -----------------------------------------------------------------
        // Quiet frames
        // -----------------------------------------------------------------

        [UnityTest]
        public IEnumerator WhileEditing_TheStatusBannerAllocatesNothing()
        {
            yield return LoadLevel();

            AssertQuiet(FrameOf(Find<StatusBanner>()));
        }

        [UnityTest]
        public IEnumerator WhileEditing_ThePartsListAllocatesNothing()
        {
            yield return LoadLevel();

            AssertQuiet(FrameOf(Find<GatePaletteView>()));
        }

        /// <summary>A verdict on the banner is drawn once, not once a frame.</summary>
        [UnityTest]
        public IEnumerator AfterARun_NeitherAllocates()
        {
            yield return LoadLevel();

            LevelSession session = Find<LevelSession>();
            SimulationRunner runner = Find<SimulationRunner>();

            // An empty board fails, which is what puts a verdict on the banner.
            session.Run();

            for (int tick = 0; tick < 100 && !runner.IsIdle(); tick++)
                runner.StepOneTick();

            yield return null;   // the session settles the run
            yield return null;   // the banner draws the verdict

            Assert.AreEqual(RunState.Failed, session.State, "sanity: an empty board should fail");

            AssertQuiet(FrameOf(Find<StatusBanner>()));
            AssertQuiet(FrameOf(Find<GatePaletteView>()));
        }

        // -----------------------------------------------------------------
        // The measurement can see a redraw
        // -----------------------------------------------------------------

        [UnityTest]
        public IEnumerator APlacedGate_IsRedrawnOnce()
        {
            yield return LoadLevel();

            Action frame = FrameOf(Find<GatePaletteView>());
            frame();

            Assert.IsTrue(Find<LevelSession>().TryPlaceGate(GateKind.Xor, new Vector2Int(0, 0)),
                "could not place a gate");

            Assert.That(() => frame(), Is.AllocatingGCMemory(),
                "the XOR count changed and its row was not redrawn -- or the measurement cannot see " +
                "an allocation, in which case the quiet-frame tests above prove nothing");

            Assert.That(() => frame(), Is.Not.AllocatingGCMemory(),
                "once redrawn, the parts list should be quiet again");
        }

        [UnityTest]
        public IEnumerator ANewLevel_IsRedrawnOnce()
        {
            yield return LoadLevel();

            Action frame = FrameOf(Find<StatusBanner>());
            frame();

            Assert.IsTrue(Find<LevelSession>().LoadLevel("half-adder"), "could not change level");

            Assert.That(() => frame(), Is.AllocatingGCMemory(),
                "the level changed and the banner's title was not redrawn -- or the measurement cannot " +
                "see an allocation, in which case the quiet-frame tests above prove nothing");

            Assert.That(() => frame(), Is.Not.AllocatingGCMemory(),
                "once redrawn, the banner should be quiet again");
        }

        // -----------------------------------------------------------------
        // The level list
        // -----------------------------------------------------------------

        /// <summary>
        /// The level list is quiet once drawn, however many levels are solved.
        /// </summary>
        /// <remarks>
        /// It refreshes every frame it is open, and formatted a record per solved row every time:
        /// seventeen rows asked about their completion, their gate count and their latency, and
        /// every solved one handed TextMeshPro a newly built string equal to the one it already
        /// had. The cost grows with how much of the game the player has finished, so it is worst
        /// on the save that has played the most.
        ///
        /// The panel is the only one that stays open while the player reads it, which is exactly
        /// the case where a per-frame allocation has time to add up.
        /// </remarks>
        [UnityTest]
        public IEnumerator WhileOpen_TheLevelListAllocatesNothing()
        {
            yield return LoadLevel();
            yield return SolveRouteTheBit();

            LevelSelectPanel list = Find<LevelSelectPanel>();
            list.Open();

            yield return null;
            yield return null;

            Assert.IsTrue(list.IsShowing, "sanity: the list should be open");

            AssertQuiet(FrameOf(list));
        }

        /// <summary>
        /// The measurement can see the list redraw, so the quiet test above is worth something.
        /// </summary>
        [UnityTest]
        public IEnumerator ANewlySolvedLevel_IsRedrawnOnceInTheList()
        {
            yield return LoadLevel();

            LevelSelectPanel list = Find<LevelSelectPanel>();
            list.Open();

            yield return null;
            yield return null;

            Action frame = FrameOf(list);
            frame();

            yield return SolveRouteTheBit();

            Assert.That(() => frame(), Is.AllocatingGCMemory(),
                "a level was solved and its row was not redrawn -- or the measurement cannot see an " +
                "allocation, in which case the quiet test above proves nothing");

            Assert.That(() => frame(), Is.Not.AllocatingGCMemory(),
                "once redrawn, the list should be quiet again");
        }

        // -----------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------

        /// <summary>
        /// Solves the first level the intended way, so the list has a record to draw, and puts
        /// the measured level back.
        /// </summary>
        /// <remarks>
        /// Solved for real rather than written into the store, because the store deliberately keeps
        /// whatever record it already holds -- so a best set by hand is a best that may not take.
        /// </remarks>
        private static IEnumerator SolveRouteTheBit()
        {
            LevelSession session = Find<LevelSession>();
            SimulationRunner runner = Find<SimulationRunner>();

            Assert.IsTrue(session.LoadLevel("route-the-bit"), "route-the-bit did not load");

            // Waited on rather than counted in frames: how many a rebuild takes is not this test's
            // business, and one frame is enough only sometimes -- which is a flake, not a pass.
            for (int frame = 0; frame < 30 && !runner.FixtureNodeIds.ContainsKey("in"); frame++)
                yield return null;

            Assert.IsTrue(runner.FixtureNodeIds.ContainsKey("in"),
                "the level loaded but its fixtures were never built");

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

            yield return null;
            yield return null;

            Assert.AreEqual(RunState.Passed, session.State, "sanity: the intended answer should pass");
            Assert.Greater(Find<ProgressTracker>().BestGates("route-the-bit"), 0,
                "sanity: solving should leave a gate record for the list to draw");
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

        /// <summary>
        /// One call to absorb anything left over from the last real frame, then one measured call.
        /// </summary>
        private static void AssertQuiet(Action frame)
        {
            frame();

            Assert.That(() => frame(), Is.Not.AllocatingGCMemory(),
                "nothing changed since the last frame, so nothing should have been drawn");
        }
    }
}
