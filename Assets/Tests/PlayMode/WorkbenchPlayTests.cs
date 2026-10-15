using System.Collections;
using NUnit.Framework;
using BitSorter.View;
using BitSorter.ViewModel;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace BitSorter.PlayMode.Tests
{
    /// <summary>
    /// The 5.0 workbench preview in the real game: reached from the main menu and back, and showing
    /// the moment the slider is dragged to, in that moment's colours.
    /// </summary>
    /// <remarks>
    /// What a wire "shows" is read from the drawing itself (<see cref="WorkbenchPanel.TryDrawnColour"/>),
    /// the colour the wires layer last stroked it in -- not worked out again from the model, which
    /// would pass whether or not the drag ever reached the screen.
    /// </remarks>
    [TestFixture]
    public class WorkbenchPlayTests : InputTestFixture
    {
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

            // As PanelPlayTests explains: the game never runs with read-value caching.
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

        private static IEnumerator Click(string name)
        {
            GameObject found = GameObject.Find(name);
            Assert.IsNotNull(found, $"no '{name}' on screen");
            found.GetComponent<Button>().onClick.Invoke();
            yield return null;
            yield return null;
        }

        private static IEnumerator OpenTheWorkbench()
        {
            Assert.IsTrue(Find<MainMenu>().IsOpen, "sanity: the game should boot into the main menu");
            yield return Click(MainMenu.WorkbenchButton);
            Assert.IsTrue(Find<WorkbenchPanel>().IsOpen, "the Workbench row did not open the workbench");
        }

        private static Color Drawn(string signal)
        {
            Assert.IsTrue(Find<WorkbenchPanel>().TryDrawnColour(signal, out Color colour), $"{signal} was never drawn");
            return colour;
        }

        private static IEnumerator DragTo(float time)
        {
            Slider slider = GameObject.Find(WorkbenchPanel.TimeSlider).GetComponent<Slider>();
            slider.value = time;   // as a drag does: through onValueChanged
            yield return null;
            yield return null;
        }

        [UnityTest]
        public IEnumerator TheWorkbench_OpensFromTheMenu_PlaysFromTheStart_AndGoesBackToIt()
        {
            yield return TestScene.Load();
            yield return OpenTheWorkbench();

            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            Assert.IsFalse(Find<MainMenu>().IsOpen, "the menu stayed up under the workbench");
            Assert.IsTrue(workbench.Playing, "the run should start playing as the workbench opens");

            double started = workbench.Time;
            for (int frame = 0; frame < 30; frame++)
                yield return null;
            Assert.Greater(workbench.Time, started, "the run did not move");

            yield return Click(WorkbenchPanel.BackButton);

            Assert.IsFalse(workbench.IsOpen, "BACK did not close the workbench");
            Assert.IsTrue(Find<MainMenu>().IsOpen, "BACK left the player on the board instead of the menu");
        }

        [UnityTest]
        public IEnumerator Escape_GoesBackToTheMenu()
        {
            yield return TestScene.Load();
            yield return OpenTheWorkbench();
            yield return null;

            Press(_keyboard.escapeKey);
            yield return null;
            Release(_keyboard.escapeKey);
            yield return null;

            Assert.IsFalse(Find<WorkbenchPanel>().IsOpen, "Escape did not leave the workbench");
            Assert.IsTrue(Find<MainMenu>().IsOpen, "one Escape left the workbench and closed the menu as well");
        }

        /// <summary>
        /// Dragging the slider back shows that moment: the glitch on Y comes and goes, and the
        /// playback stops where the slider was let go.
        /// </summary>
        [UnityTest]
        public IEnumerator DraggingTheSlider_ShowsThatMomentsColours()
        {
            yield return TestScene.Load();
            yield return OpenTheWorkbench();

            SchematicPalette palette = SchematicPalette.Current;
            WorkbenchPanel workbench = Find<WorkbenchPanel>();

            yield return DragTo(9f);
            Assert.IsFalse(workbench.Playing, "dragging should stop the playback");
            Assert.AreEqual(9.0, workbench.Time, 1e-4, "the workbench is not showing where the slider was dragged");
            Assert.AreEqual(palette.One, Drawn("y"), "at 9 the glitch is on Y");
            Assert.AreEqual(palette.One, Drawn("a"));

            yield return DragTo(5f);
            Assert.AreEqual(palette.Zero, Drawn("y"), "back at 5 the glitch has not happened yet");
            Assert.AreEqual(palette.Zero, Drawn("a"), "and A has not risen");

            yield return DragTo(0.5f);
            Assert.AreEqual(palette.Unknown, Drawn("na"), "at 0.5 NOT A is still x");
            Assert.AreEqual(palette.Unknown, Drawn("q"), "and so is Q, until the tri-state's 1 ns is up");

            yield return DragTo(5f);
            Assert.AreEqual(palette.HighZ, Drawn("q"), "the tri-state is off: z");

            yield return DragTo(13f);
            Assert.AreEqual(palette.Zero, Drawn("y"), "the glitch is over by 13");
        }

        [UnityTest]
        public IEnumerator Replay_StartsAgainFromNothing()
        {
            yield return TestScene.Load();
            yield return OpenTheWorkbench();

            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            yield return DragTo(20f);
            Assert.IsFalse(workbench.Playing, "sanity: the drag should have stopped it");

            yield return Click(WorkbenchPanel.ReplayButton);

            Assert.IsTrue(workbench.Playing, "REPLAY did not play");
            Assert.Less(workbench.Time, 1.0, "REPLAY did not go back to the start");
        }

        private static void AssertNear(Vector2 expected, Vector2 actual, string message)
        {
            Assert.AreEqual(expected.x, actual.x, 0.5f, message + $" (across: {expected} against {actual})");
            Assert.AreEqual(expected.y, actual.y, 0.5f, message + $" (up and down: {expected} against {actual})");
        }

        private IEnumerator Tap(KeyControl key)
        {
            Press(key);
            yield return null;
            Release(key);
            yield return null;
        }

        /// <summary>Every control has a key: Space, R, the arrows, plus and minus, F.</summary>
        [UnityTest]
        public IEnumerator TheKeys_PlayReplayStepZoomAndFit()
        {
            yield return TestScene.Load();
            yield return OpenTheWorkbench();
            yield return null;

            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            Assert.IsTrue(workbench.Playing, "sanity: it opens playing");

            yield return Tap(_keyboard.spaceKey);
            Assert.IsFalse(workbench.Playing, "Space did not pause");

            workbench.Seek(5.0);
            yield return Tap(_keyboard.rightArrowKey);
            Assert.AreEqual(6.0, workbench.Time, 1e-6, "the right arrow should step a nanosecond on");
            yield return Tap(_keyboard.leftArrowKey);
            yield return Tap(_keyboard.leftArrowKey);
            Assert.AreEqual(4.0, workbench.Time, 1e-6, "the left arrow should step a nanosecond back");

            yield return Tap(_keyboard.equalsKey);
            Assert.Greater(workbench.Zoom, 1f, "plus did not zoom in");
            yield return Tap(_keyboard.minusKey);
            Assert.IsTrue(workbench.IsFitted, "minus, as many times as plus, should be the whole circuit again");

            yield return Tap(_keyboard.equalsKey);
            yield return Tap(_keyboard.equalsKey);
            yield return Tap(_keyboard.fKey);
            Assert.IsTrue(workbench.IsFitted, "F did not fit");

            yield return Tap(_keyboard.rKey);
            Assert.IsTrue(workbench.Playing, "R did not replay");
            Assert.Less(workbench.Time, 1.0, "R did not go back to the start");
        }

        [UnityTest]
        public IEnumerator PointingAtAWire_LightsItsNet_AndSaysWhatDrivesIt()
        {
            yield return TestScene.Load();
            yield return OpenTheWorkbench();

            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            workbench.Pause();
            int notA = workbench.Model.Layout.NetNamed("na");
            StageRoute wire = workbench.Model.Layout.Nets[notA].Branches[0];

            var mouse = InputSystem.GetDevice<Mouse>();
            mouse.MakeCurrent();
            Move(mouse.position, workbench.ScreenPointOf(wire.PointAt(wire.Length * 0.5f)));
            yield return null;
            yield return null;

            Assert.AreEqual(notA, workbench.HoveredNet, "the pointer on NOT A's wire did not light it");
            Assert.AreEqual(workbench.Model.Layout.Nets[notA].Says, workbench.HoverText, "the chip should say what drives it and what reads it");

            Move(mouse.position, new Vector2(4f, 4f));
            yield return null;
            yield return null;

            Assert.AreEqual(-1, workbench.HoveredNet, "nothing should stay lit with the pointer off the circuit");
            Assert.IsNull(workbench.HoverText);
        }

        [UnityTest]
        public IEnumerator TheWheel_ZoomsAboutTheCursor_ADragMovesTheCircuit_AndFitPutsItBack()
        {
            yield return TestScene.Load();
            yield return OpenTheWorkbench();

            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            Assert.IsTrue(workbench.IsFitted, "the circuit should open whole");
            Assert.IsNull(GameObject.Find(WorkbenchPanel.FitButton), "FIT is for a circuit that is not whole");

            // A point of the drawing, and where it is on the screen.
            Vec2 point = workbench.Model.Layout.Size * 0.4f;
            Vector2 cursor = workbench.ScreenPointOf(point);

            // The wheel is handed over as the real one's is each frame: a fixture's own scroll is
            // cleared as the next frame's input arrives, before the panel could read it.
            Assert.IsTrue(workbench.TakeWheel(cursor, new Vector2(0f, 120f)), "a notch up over the circuit did not zoom");
            yield return null;
            Assert.Greater(workbench.Zoom, 1f);
            AssertNear(cursor, workbench.ScreenPointOf(point), "the point under the cursor moved as the circuit zoomed");
            Assert.IsNotNull(GameObject.Find(WorkbenchPanel.FitButton), "FIT should be up once the circuit is zoomed");

            // A drag on the circuit takes it with the pointer, through the real mouse.
            var mouse = InputSystem.GetDevice<Mouse>();
            mouse.MakeCurrent();
            var drag = new Vector2(60f, -40f);
            Move(mouse.position, cursor);
            yield return null;
            Press(mouse.leftButton);
            yield return null;
            Move(mouse.position, cursor + drag);
            yield return null;
            Release(mouse.leftButton);
            yield return null;
            AssertNear(cursor + drag, workbench.ScreenPointOf(point), "the circuit did not follow the drag");

            // A press on the slider, dragged on over the circuit, is the slider's and not a drag of the circuit.
            Vector2 dragged = workbench.ScreenPointOf(point);
            Vector2 slider = RectTransformUtility.WorldToScreenPoint(null, GameObject.Find(WorkbenchPanel.TimeSlider).transform.position);
            Move(mouse.position, slider);
            yield return null;
            Press(mouse.leftButton);
            yield return null;
            Move(mouse.position, workbench.ViewMiddleOnScreen);
            yield return null;
            Move(mouse.position, workbench.ViewMiddleOnScreen + new Vector2(50f, 50f));
            yield return null;
            Release(mouse.leftButton);
            yield return null;
            AssertNear(dragged, workbench.ScreenPointOf(point), "a press on the slider moved the circuit");

            yield return Click(WorkbenchPanel.FitButton);
            Assert.IsTrue(workbench.IsFitted, "FIT did not put the whole circuit back");
            AssertNear(cursor, workbench.ScreenPointOf(point), "FIT did not put the circuit back where it was");
            Assert.IsNull(GameObject.Find(WorkbenchPanel.FitButton), "FIT should go once the circuit is whole");
        }
    }
}
