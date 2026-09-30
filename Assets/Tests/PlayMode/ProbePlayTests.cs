using System.Collections;
using NUnit.Framework;
using BitSorter.LogicCore;
using BitSorter.View;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;

namespace BitSorter.PlayMode.Tests
{
    /// <summary>
    /// Alt+click on a wire puts it in the timing diagram, and does nothing else to the board.
    /// </summary>
    /// <remarks>
    /// On Out of step, which opens with eight wires in and every part spent -- so a plain click on
    /// the board is refused out loud, and a refusal that does not come is proof the click was the
    /// wire picker's. The pair test shows the plain click is refused, or the other half would pass
    /// by the board never hearing the click at all.
    /// </remarks>
    [TestFixture]
    public class ProbePlayTests : InputTestFixture
    {
        private const string Level = "out-of-step";

        private Mouse _mouse;
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

            // As in PanelPlayTests: the read-value cache's self-check, which the shipped game never
            // runs with, logs an error on queued input once the game has run earlier in the session.
            InputSystem.settings.SetInternalFeatureFlag("USE_READ_VALUE_CACHING", false);

            _keyboard = InputSystem.AddDevice<Keyboard>();
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

        private static IEnumerator OnTheBoard()
        {
            yield return TestScene.Load();

            Find<MainMenu>().Show(false);
            yield return null;

            Assert.IsTrue(Find<LevelSession>().LoadLevel(Level), "the level did not load");
            yield return null;
            yield return null;
        }

        /// <summary>The middle of a wire on screen: between its two ends, where a click finds it.</summary>
        private static Vector2 MiddleOf(int edgeId)
        {
            SimulationRunner runner = Find<SimulationRunner>();
            Edge edge = runner.View.GetEdge(edgeId);

            Vector2 from = PortGeometry.EndpointOf(edge.Source, runner.PositionOf(edge.Source.Owner.Id));
            Vector2 to = PortGeometry.EndpointOf(edge.Target, runner.PositionOf(edge.Target.Owner.Id));
            Vector2 middle = (from + to) * 0.5f;

            Vector3 screen = Camera.main.WorldToScreenPoint(new Vector3(middle.x, middle.y, 0f));
            return new Vector2(screen.x, screen.y);
        }

        private IEnumerator ClickOnWire(int edgeId, bool alt)
        {
            Set(_mouse.position, MiddleOf(edgeId));
            yield return null;

            if (alt)
                Press(_keyboard.leftAltKey);

            yield return null;

            Press(_mouse.leftButton);
            yield return null;
            Release(_mouse.leftButton);

            if (alt)
                Release(_keyboard.leftAltKey);

            yield return null;
            yield return null;
        }

        /// <summary>
        /// An Alt+click on a wire makes it W1 -- and places nothing, starts no wire and draws no
        /// refusal. A second one takes it out again.
        /// </summary>
        [UnityTest]
        public IEnumerator AltClickOnAWire_PutsItInTheDiagram_AndDoesNothingElse()
        {
            yield return OnTheBoard();

            ProbeController controller = Find<ProbeController>();
            SimulationRunner runner = Find<SimulationRunner>();
            LevelSession session = Find<LevelSession>();

            int parts = session.Blueprint.Placements.Count;
            int wires = session.Blueprint.Wires.Count;
            float refusedAt = runner.LastRejectionTime;

            yield return ClickOnWire(0, alt: true);

            Assert.IsTrue(controller.Probes.IsUsed(0), "the Alt+click did not put the wire in the diagram");
            Assert.AreEqual(0, controller.Probes.EdgeIdAt(0), "W1 is not the wire that was clicked");
            Assert.AreEqual(parts, session.Blueprint.Placements.Count, "the Alt+click placed a part as well");
            Assert.AreEqual(wires, session.Blueprint.Wires.Count, "the Alt+click changed the wiring as well");
            Assert.IsFalse(Find<WiringController>().IsDragging, "the Alt+click started a wire as well");
            Assert.AreEqual(refusedAt, runner.LastRejectionTime, "the Alt+click drew a refusal as well");

            yield return ClickOnWire(0, alt: true);

            Assert.IsFalse(controller.Probes.IsUsed(0), "a second Alt+click did not take the wire out");
        }

        /// <summary>The same click without Alt is still the board's: refused, since every part is spent.</summary>
        [UnityTest]
        public IEnumerator APlainClickOnTheSameSpot_IsStillTheBoards()
        {
            yield return OnTheBoard();

            SimulationRunner runner = Find<SimulationRunner>();
            LevelSession session = Find<LevelSession>();
            ProbeController controller = Find<ProbeController>();

            int parts = session.Blueprint.Placements.Count;
            float refusedAt = runner.LastRejectionTime;

            yield return ClickOnWire(0, alt: false);

            bool boardHeardIt = runner.LastRejectionTime != refusedAt
                                || session.Blueprint.Placements.Count != parts
                                || Find<WiringController>().IsDragging;

            Assert.IsTrue(boardHeardIt, "a plain click on the wire's middle reached nothing on the board");
            Assert.IsFalse(controller.Probes.IsUsed(0), "a plain click put the wire in the diagram");
        }

        /// <summary>A fifth wire is refused with the way out, and the four stay as they were.</summary>
        [UnityTest]
        public IEnumerator AFifthWire_IsRefused_WithTheWayOut()
        {
            yield return OnTheBoard();

            ProbeController controller = Find<ProbeController>();
            SimulationRunner runner = Find<SimulationRunner>();

            for (int id = 0; id < WireProbes.Slots; id++)
                Assert.AreEqual(ProbeToggle.Added, controller.Toggle(runner.View.GetEdge(id)));

            yield return ClickOnWire(WireProbes.Slots, alt: true);

            Assert.AreEqual(ProbeController.FullRefusal, runner.LastRejectionReason, "a fifth wire was not refused");
            Assert.AreEqual(-1, controller.Probes.SlotOf(KeyOf(WireProbes.Slots)), "the fifth wire went in anyway");
        }

        private static WireKey KeyOf(int edgeId)
        {
            SimulationRunner runner = Find<SimulationRunner>();
            Assert.IsTrue(WireProbes.TryKeyOf(runner.View.GetEdge(edgeId), runner.NodeCells, out WireKey key));
            return key;
        }

        /// <summary>A picked wire's row shows what arrived at its far end, tick by tick.</summary>
        [UnityTest]
        public IEnumerator APickedWire_ShowsItsBitsInTheDiagram()
        {
            yield return OnTheBoard();

            ProbeController controller = Find<ProbeController>();
            SimulationRunner runner = Find<SimulationRunner>();
            LevelSession session = Find<LevelSession>();
            WaveformPanel diagram = Find<WaveformPanel>();

            Assert.AreEqual(ProbeToggle.Added, controller.Toggle(runner.View.GetEdge(0)));

            session.Run();
            runner.SetPaused(true);

            for (int i = 0; i < 4; i++)
                runner.StepOneTick();

            yield return null;

            // Edge 0 is A into the AND, one tick long: A's bit from tick t arrives on tick t + 1.
            int wireRow = diagram.RowCount - WaveformPanel.WireRows;

            Assert.IsTrue(diagram.TryCell(wireRow, 1, out WaveCell arrived), "W1's row holds nothing at tick 1");
            Assert.AreEqual(diagram.Recorder.SourceCell(0, 0).Value, arrived.Value,
                "what arrived at the AND is not what A sent the tick before");
        }

        /// <summary>A picked wire that is deleted takes its row with it.</summary>
        [UnityTest]
        public IEnumerator DeletingAPickedWire_TakesItOutOfTheDiagram()
        {
            yield return OnTheBoard();

            ProbeController controller = Find<ProbeController>();
            SimulationRunner runner = Find<SimulationRunner>();
            LevelSession session = Find<LevelSession>();

            WireKey key = KeyOf(0);
            Assert.AreEqual(ProbeToggle.Added, controller.Toggle(runner.View.GetEdge(0)));

            Vector2 world = Camera.main.ScreenToWorldPoint(MiddleOf(0));
            Assert.IsTrue(session.TryDeleteWireAt(world), "sanity: the wire was not deleted");
            yield return null;

            Assert.AreEqual(-1, controller.Probes.SlotOf(key), "the deleted wire is still in the diagram");
        }

        /// <summary>A new level starts with no wires in the diagram.</summary>
        [UnityTest]
        public IEnumerator ANewLevel_StartsWithNoWiresInTheDiagram()
        {
            yield return OnTheBoard();

            ProbeController controller = Find<ProbeController>();
            SimulationRunner runner = Find<SimulationRunner>();

            Assert.AreEqual(ProbeToggle.Added, controller.Toggle(runner.View.GetEdge(0)));

            Assert.IsTrue(Find<LevelSession>().LoadLevel("half-adder"), "the level did not load");
            yield return null;

            for (int slot = 0; slot < WireProbes.Slots; slot++)
                Assert.IsFalse(controller.Probes.IsUsed(slot), $"W{slot + 1} came with the player to a new level");
        }
    }
}
