using System.Collections;
using System.Linq;
using NUnit.Framework;
using BitSorter.View;
using BitSorter.ViewModel;
using TMPro;
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
            Assert.AreEqual(palette.Unknown, Drawn("q"), "and so is Q, until the tri-state's 1 ps is up");

            yield return DragTo(5f);
            Assert.AreEqual(palette.HighZ, Drawn("q"), "the tri-state is off: z");

            yield return DragTo(13f);
            Assert.AreEqual(palette.Zero, Drawn("y"), "the glitch is over by 13");
        }

        [UnityTest]
        public IEnumerator Restart_StartsAgainFromNothing()
        {
            yield return TestScene.Load();
            yield return OpenTheWorkbench();

            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            yield return DragTo(20f);
            Assert.IsFalse(workbench.Playing, "sanity: the drag should have stopped it");

            yield return Click(WorkbenchPanel.RestartButton);

            Assert.IsTrue(workbench.Playing, "RESTART did not play");
            Assert.Less(workbench.Time, 1.0, "RESTART did not go back to the start");
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

            // A drag with the middle button takes the circuit with the pointer, through the real mouse; the left
            // button's is the canvas's own since Part 10a, a box or a move.
            var mouse = InputSystem.GetDevice<Mouse>();
            mouse.MakeCurrent();
            var drag = new Vector2(60f, -40f);
            Move(mouse.position, cursor);
            yield return null;
            Press(mouse.middleButton);
            yield return null;
            Move(mouse.position, cursor + drag);
            yield return null;
            Release(mouse.middleButton);
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

        /// <summary>
        /// The wheel over the waveform zooms time about the cursor, as far in as the cap and back out
        /// to the whole run, and with Shift, or sideways, moves through it; the axis's numbers follow,
        /// in fractions of a nanosecond once a tick is one; and the circuit's own zoom is left alone.
        /// </summary>
        [UnityTest]
        public IEnumerator TheWheelOverTheWaveform_ZoomsTimeAboutTheCursor_AndShiftMovesThroughIt()
        {
            yield return TestScene.Load();
            yield return OpenTheWorkbench();

            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            workbench.Pause();
            workbench.Seek(4.0);
            yield return null;

            WaveView wave = workbench.Wave;
            Assert.IsTrue(wave.IsFitted, "the waveform should open on the whole run");
            string[] fitted = workbench.TickTexts.ToArray();
            Assert.AreEqual("0", fitted.FirstOrDefault(), $"the fitted axis should start at 0, not read {string.Join(" ", fitted)}");
            Assert.IsFalse(fitted.Any(t => t.Contains(".")), "fitted, the reference's run is ticked in whole units");

            // The wheel is handed over as the real one's is each frame, as for the circuit's.
            Vector2 cursor = workbench.WaveScreenPointOf(13.0);
            Assert.IsTrue(workbench.TakeWaveWheel(cursor, new Vector2(0f, 120f), false), "a notch up over the waveform did not zoom time");
            yield return null;
            Assert.Greater(wave.Zoom, 1.0);
            AssertNear(cursor, workbench.WaveScreenPointOf(13.0), "the moment under the cursor moved as time zoomed");
            Assert.IsTrue(workbench.IsFitted, "a zoom of the waveform should leave the circuit as it was");

            for (int i = 0; i < 60 && workbench.TakeWaveWheel(cursor, new Vector2(0f, 120f), false); i++)
                yield return null;
            Assert.AreEqual(WaveView.MaxPixelsPerUnit, wave.PixelsPerUnit, 1e-6, "the wheel should zoom in as far as the cap, and stop");
            AssertNear(cursor, workbench.WaveScreenPointOf(13.0), "the moment under the cursor moved as time zoomed in all the way");
            string[] zoomed = workbench.TickTexts.ToArray();
            Assert.IsTrue(zoomed.Any(t => t.Contains(".")), $"zoomed in, the axis should be ticked in fractions of a unit, not {string.Join(" ", zoomed)}");

            double start = wave.Start;
            Assert.IsTrue(workbench.TakeWaveWheel(cursor, new Vector2(0f, -120f), true), "Shift and the wheel down should move through time");
            Assert.Greater(wave.Start, start, "Shift and the wheel down should move later");
            Assert.IsTrue(workbench.TakeWaveWheel(cursor, new Vector2(-120f, 0f), false), "a sideways scroll should move through time");
            Assert.AreEqual(start, wave.Start, 1e-9, "a notch later and a sideways notch earlier should come back where they were");

            for (int i = 0; i < 80 && workbench.TakeWaveWheel(cursor, new Vector2(0f, -120f), false); i++)
                yield return null;
            Assert.IsTrue(wave.IsFitted, "the wheel down, as far as it goes, should be the whole run again");
            CollectionAssert.AreEqual(fitted, workbench.TickTexts.ToArray(), "with the fitted axis's numbers");
            Assert.IsTrue(workbench.IsFitted, "and the circuit still whole");
        }

        /// <summary>
        /// Zoomed in on a run that plays, the waveform pages on after the playhead and never loses it;
        /// moved away from it, it stays where it is put; and a replay follows the playhead again.
        /// </summary>
        [UnityTest]
        public IEnumerator AZoomedWaveform_PagesOnAfterThePlayhead_AndAReplayFollowsItAgain()
        {
            yield return TestScene.Load();
            yield return OpenTheWorkbench();

            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            workbench.Pause();
            workbench.Seek(0.0);
            WaveView wave = workbench.Wave;
            for (int i = 0; i < 6; i++)
                workbench.TakeWaveWheel(workbench.WaveScreenPointOf(0.0), new Vector2(0f, 120f), false);
            Assert.Greater(wave.Zoom, 2.0, "sanity: zoomed in on the start");
            Assert.IsTrue(wave.Following, "sanity: zoomed in about the playhead, it still follows it");

            for (double t = 0; t <= workbench.Model.End; t += 0.25)
            {
                workbench.Seek(t);
                Assert.IsTrue(wave.Shows(t), $"at {t} ps the playhead went out of view");
            }

            Assert.Greater(wave.Start, 0.0, "the view should have paged on after the playhead");
            yield return null;

            for (int i = 0; i < 100 && workbench.TakeWaveWheel(workbench.WaveScreenPointOf(wave.Start + wave.Shown * 0.5), new Vector2(0f, 120f), true); i++)
            {
            }

            Assert.AreEqual(0.0, wave.Start, "sanity: Shift and the wheel up moved back to the start");
            Assert.IsFalse(wave.Following, "moved away from the playhead, it should stop following it");
            workbench.Seek(workbench.Model.End - 1.0);
            Assert.AreEqual(0.0, wave.Start, "a view moved away from the playhead should stay where it was put");

            yield return Click(WorkbenchPanel.RestartButton);
            Assert.IsTrue(wave.Following, "a restart should follow the playhead again");
            Assert.IsTrue(wave.Shows(workbench.Time), "with the playhead in view");

            // PLAY at the end of the run -- where its testbench's $stop suspended it -- runs the run on
            // (Part 9b-3), and like RESTART it follows the playhead again however the view was moved.
            workbench.Seek(0.0);
            for (int i = 0; i < 200 && workbench.TakeWaveWheel(workbench.WaveScreenPointOf(wave.Start + wave.Shown * 0.5), new Vector2(0f, -120f), true); i++)
            {
            }

            workbench.Pause();
            workbench.Seek(workbench.Model.End);
            Assert.IsFalse(wave.Shows(0.0), "sanity: moved on to the end, the start is out of view");
            Assert.IsFalse(wave.Following, "sanity: and the view is not following the playhead");
            long end = workbench.Model.End;
            yield return Click(WorkbenchPanel.PlayButton);
            Assert.IsTrue(workbench.Playing, "sanity: PLAY at the end plays");
            Assert.AreEqual(2 * end, workbench.Model.End, "sanity: on past the $stop, as long again");
            Assert.IsTrue(wave.Following, "PLAY from the end should follow the playhead again, as RESTART does");
            Assert.IsTrue(wave.Shows(workbench.Time), "with the playhead in view");
        }

        private static bool Captioned(string button, string caption) =>
            GameObject.Find(button).GetComponentsInChildren<TextMeshProUGUI>().Any(label => label.text == caption);

        /// <summary>
        /// SAMPLE, and S, step through the reference, the full adder and the counter and round again:
        /// each its own run, played from the start, with the slider its length and the waveform its
        /// whole run, and nothing left on the stage from the circuit before.
        /// </summary>
        [UnityTest]
        public IEnumerator TheSampleChip_StepsThroughTheSamples_EachPlayedFromTheStart()
        {
            yield return TestScene.Load();
            yield return OpenTheWorkbench();

            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            Assert.AreEqual(1, workbench.Sample, "with nothing in MY CIRCUIT, the workbench should open on the reference circuit");
            Assert.IsTrue(Captioned(WorkbenchPanel.SampleButton, WorkbenchPanel.SampleCaption(1)), "the chip should say it is the reference");
            WorkbenchModel reference = workbench.Model;

            yield return Click(WorkbenchPanel.SampleButton);
            Assert.AreEqual(2, workbench.Sample, "SAMPLE should step to the full adder");
            Assert.AreNotSame(reference, workbench.Model, "a new sample is a run of its own");
            Assert.AreEqual(WorkbenchSamples.FullAdderEnd, workbench.Model.End, "the full adder's run");
            Slider slider = GameObject.Find(WorkbenchPanel.TimeSlider).GetComponent<Slider>();
            Assert.AreEqual(WorkbenchSamples.FullAdderEnd, slider.maxValue, 1e-6, "the slider should run the length of the new run");
            Assert.IsTrue(workbench.Playing, "a new sample should play");
            Assert.Less(workbench.Time, 1.0, "from the start");
            Assert.IsTrue(workbench.Wave.IsFitted, "its waveform should show the whole of its run");
            Assert.IsTrue(Captioned(WorkbenchPanel.SampleButton, WorkbenchPanel.SampleCaption(2)), "the chip should say which sample");

            foreach (WorkbenchGraphic graphic in Object.FindObjectsByType<WorkbenchGraphic>(FindObjectsSortMode.None))
                if (graphic.Model != null)
                    Assert.AreSame(workbench.Model, graphic.Model, $"'{graphic.name}' is left over from the circuit before");

            yield return Tap(_keyboard.sKey);
            Assert.AreEqual(3, workbench.Sample, "S should step to the counter");
            Assert.AreEqual(WorkbenchSamples.CounterEnd, workbench.Model.End, "the counter's run");

            yield return DragTo(20f);
            Assert.AreEqual(20.0, workbench.Time, 1e-6, "the counter's slider should show the moment it is dragged to");
            Assert.IsFalse(workbench.Playing, "and pause there");

            yield return Tap(_keyboard.sKey);
            Assert.AreEqual(0, workbench.Sample, "past the last sample, S should come back to the first, MY CIRCUIT");
            Assert.IsTrue(Captioned(WorkbenchPanel.SampleButton, "MY CIRCUIT"));
            Assert.IsEmpty(workbench.Model.Layout.Parts, "a blank circuit, the first time");

            yield return Tap(_keyboard.sKey);
            Assert.AreEqual(1, workbench.Sample);
            Assert.AreEqual(reference.End, workbench.Model.End, "the reference's run again");
            Assert.IsTrue(workbench.Playing, "played from the start");
        }

        /// <summary>
        /// A block's port names are written inside its box, each beside its pin -- its outputs' as well
        /// as its inputs'. Read from what TextMeshPro laid out, against the part's outline.
        /// </summary>
        /// <remarks>
        /// The full adder's two half adders are the first blocks a sample shows. Their outputs' names
        /// were placed by the label box's right edge where the label is given by its left one, and came
        /// out past the box, over the wire leaving it.
        /// </remarks>
        [UnityTest]
        public IEnumerator ABlocksPortNames_AreWrittenInsideItsBox()
        {
            yield return TestScene.Load();
            yield return OpenTheWorkbench();

            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            workbench.ShowSample(2);   // the full adder
            workbench.Pause();
            yield return null;
            yield return null;

            TextMeshProUGUI[] labels = Object.FindObjectsByType<TextMeshProUGUI>(FindObjectsSortMode.None)
                .Where(label => label.transform.parent != null && label.transform.parent.name == "schematic")
                .ToArray();
            Assert.IsNotEmpty(labels, "sanity: the circuit's labels are under its drawing");
            RectTransform schematic = (RectTransform)labels[0].transform.parent;

            // Where a point of a label is in the drawing's own units, y down.
            Vec2 InDrawing(TextMeshProUGUI label, Vector3 local)
            {
                Vector3 at = schematic.InverseTransformPoint(label.rectTransform.TransformPoint(local));
                return new Vec2(at.x, -at.y);
            }

            int pins = 0;
            foreach (PartLayout part in workbench.Model.Layout.Parts.Where(p => p.Symbol == PartSymbol.Block))
            {
                Vec2 corner = part.ToStage(part.Local.Left, part.Local.Bottom), across = part.ToStage(part.Local.Right, part.Local.Top);
                float left = Mathf.Min(corner.x, across.x), right = Mathf.Max(corner.x, across.x);

                foreach (BitSorter.Board.PinSpot pin in part.Local.Pins)
                {
                    Vec2 spot = part.ToStage(pin.At.X, pin.At.Y);
                    TextMeshProUGUI label = labels.Where(l => l.text == pin.Pin)
                        .OrderBy(l => (InDrawing(l, Vector3.zero) - spot).magnitude).First();

                    label.ForceMeshUpdate();
                    Bounds text = label.textBounds;
                    float from = InDrawing(label, text.min).x, to = InDrawing(label, text.max).x;

                    Assert.GreaterOrEqual(from, left - 0.5f, $"{part.Name}'s '{pin.Pin}' starts left of its box ({from:0.#} against {left:0.#})");
                    Assert.LessOrEqual(to, right + 0.5f, $"{part.Name}'s '{pin.Pin}' runs on past its box ({to:0.#} against {right:0.#})");
                    pins++;
                }
            }

            Assert.AreEqual(8, pins, "sanity: two half adders, four ports each");
        }

        // -----------------------------------------------------------------
        // Cursors, rows and their menu (Part 9b-2b)
        // -----------------------------------------------------------------

        private static int RowLabelled(WorkbenchPanel workbench, string label)
        {
            WaveList list = workbench.List;
            int row = Enumerable.Range(0, list.Lines.Count).Single(i => list.LabelOf(list.Lines[i]) == label);
            return row;
        }

        private IEnumerator ClickAt(Vector2 point, ButtonControl button = null)
        {
            var mouse = InputSystem.GetDevice<Mouse>();
            mouse.MakeCurrent();
            Move(mouse.position, point);
            yield return null;
            Press(button ?? mouse.leftButton);
            yield return null;
            Release(button ?? mouse.leftButton);
            yield return null;
        }

        /// <summary>Every key the workbench's table names is one the Input System has, by that name: the panel reads its keys from it as it opens.</summary>
        [Test]
        public void EveryKeyInTheTable_IsAnInputSystemKey()
        {
            foreach (KeyBinding binding in WorkbenchKeys.All)
                Assert.IsTrue(System.Enum.TryParse(binding.Key, out Key key) && key != Key.None, $"{binding.Key} is not a key the Input System knows");
        }

        /// <summary>
        /// A click on the waves moves the playhead there and pauses: onto an edge of the row under it
        /// within 8 px, or else onto a whole unit.
        /// </summary>
        [UnityTest]
        public IEnumerator AClickOnTheWaves_SeeksThePlayhead_SnappedToAnEdgeOrAWholeUnit()
        {
            yield return TestScene.Load();
            yield return OpenTheWorkbench();

            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            int a = RowLabelled(workbench, "a");
            SignalTrack track = workbench.Model.RowTrack(a);
            double edge = track[1].Time;
            Assert.Greater(workbench.Wave.PixelsPerUnit, 10.0, "sanity: a unit is wide enough for 0.2 of one to be within 8 px");

            yield return ClickAt(workbench.WaveScreenPointOf(edge + 0.2, a));
            Assert.IsFalse(workbench.Playing, "a click on the waves pauses");
            Assert.AreEqual(edge, workbench.Time, 1e-9, "a click just past a's edge lands on it");

            yield return ClickAt(workbench.WaveScreenPointOf(20.4, a));
            Assert.AreEqual(20.0, workbench.Time, 1e-9, "and with no edge near, on the nearest whole unit");
        }

        /// <summary>Shift and a click places the second cursor; its time and the span from the playhead are written; C takes it away.</summary>
        [UnityTest]
        public IEnumerator ShiftAndAClick_PlacesTheSecondCursor_AndTheSpanIsWritten()
        {
            yield return TestScene.Load();
            yield return OpenTheWorkbench();

            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            workbench.Pause();
            workbench.Seek(10.0);
            int en = RowLabelled(workbench, "en");
            Assert.IsNull(workbench.SecondText, "sanity: no second cursor yet");

            Press(_keyboard.shiftKey);
            yield return null;
            yield return ClickAt(workbench.WaveScreenPointOf(19.2, en));
            Release(_keyboard.shiftKey);
            yield return null;

            Assert.AreEqual(19.0, workbench.Cursors.Second, "placed on the whole unit nearest the click");
            Assert.AreEqual(10.0, workbench.Time, 1e-9, "and the playhead left where it was");
            Assert.AreEqual("19 ps", workbench.SecondText);
            Assert.AreEqual("9 ps", workbench.SpanText, "the time from the playhead to it");

            yield return Tap(_keyboard.cKey);
            Assert.IsNull(workbench.Cursors.Second, "C takes it away");
            Assert.IsNull(workbench.SecondText);
            Assert.IsNull(workbench.SpanText);
        }

        /// <summary>
        /// A right-click on a row opens its menu; a radix chosen there rewrites the row's value, and X
        /// steps it to the next; REMOVE takes the row out.
        /// </summary>
        [UnityTest]
        public IEnumerator ARowsMenu_SetsItsRadix_XStepsIt_AndRemoveTakesItOut()
        {
            yield return TestScene.Load();
            yield return OpenTheWorkbench();

            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            workbench.Pause();
            workbench.Seek(13.5);
            int s = RowLabelled(workbench, "s[3:0]");
            yield return null;
            Assert.AreEqual("Ah", workbench.ReadoutOf(s), "sanity: s is A, in hex");

            var mouse = InputSystem.GetDevice<Mouse>();
            yield return ClickAt(workbench.RowNameOnScreen(s), mouse.rightButton);
            Assert.IsTrue(workbench.RowMenuOpen, "a right-click on a row's name opens its menu");
            Assert.IsTrue(workbench.List.Lines[s].Selected, "and selects the row");

            yield return Click(WaveRowMenu.RadixItem + WaveRadix.Unsigned);
            Assert.IsFalse(workbench.RowMenuOpen, "choosing closes it");
            Assert.AreEqual(WaveRadix.Unsigned, workbench.Model.RowRadix(s));
            Assert.AreEqual("10", workbench.ReadoutOf(s), "A, unsigned");

            yield return Tap(_keyboard.xKey);
            Assert.AreEqual(WaveRadix.Signed, workbench.Model.RowRadix(s), "X steps the selected row on to signed");
            Assert.AreEqual("-6", workbench.ReadoutOf(s), "4'b1010, signed");

            int rows = workbench.Model.RowCount;
            yield return ClickAt(workbench.RowNameOnScreen(s), mouse.rightButton);
            yield return Click(WaveRowMenu.RemoveItem);
            Assert.AreEqual(rows - 1, workbench.Model.RowCount, "REMOVE takes the row out");
            Assert.IsFalse(workbench.List.AllSignals.Any(r => r.Key == "s"));
        }

        /// <summary>Escape closes the row menu first, and only then the workbench; and the click that closes the menu never seeks.</summary>
        [UnityTest]
        public IEnumerator Escape_ClosesTheRowMenuFirst_AndAClickOffItNeverSeeks()
        {
            yield return TestScene.Load();
            yield return OpenTheWorkbench();

            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            workbench.Pause();
            workbench.Seek(5.0);
            int y = RowLabelled(workbench, "y");
            var mouse = InputSystem.GetDevice<Mouse>();

            yield return ClickAt(workbench.RowNameOnScreen(y), mouse.rightButton);
            Assert.IsTrue(workbench.RowMenuOpen, "sanity: the menu is up");
            yield return Tap(_keyboard.escapeKey);
            Assert.IsFalse(workbench.RowMenuOpen, "Escape closes the menu");
            Assert.IsTrue(workbench.IsOpen, "and leaves the workbench up");

            yield return ClickAt(workbench.RowNameOnScreen(y), mouse.rightButton);
            Assert.IsTrue(workbench.RowMenuOpen);
            yield return ClickAt(workbench.WaveScreenPointOf(20.0, y + 2));
            Assert.IsFalse(workbench.RowMenuOpen, "a click off the menu closes it");
            Assert.AreEqual(5.0, workbench.Time, 1e-9, "and is the menu's: the waves under it were not clicked");

            yield return Tap(_keyboard.escapeKey);
            Assert.IsFalse(workbench.IsOpen, "with nothing over it, Escape closes the workbench");
        }

        /// <summary>Selecting a row lights its wire; pointing at a wire lights its row; Delete takes a selected row out.</summary>
        [UnityTest]
        public IEnumerator ARowAndItsWire_LightEachOther_AndDeleteTakesTheRowOut()
        {
            yield return TestScene.Load();
            yield return OpenTheWorkbench();

            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            workbench.Pause();
            int a = RowLabelled(workbench, "a");
            yield return ClickAt(workbench.RowNameOnScreen(a));
            Assert.IsTrue(workbench.List.Lines[a].Selected, "a click on a row's name selects it");
            Assert.IsTrue(workbench.RowBandShown(a), "and puts up its band");
            Assert.AreEqual(workbench.Model.NetOfRow(a), workbench.LitNet, "and lights its wire");

            int y = RowLabelled(workbench, "y");
            int yNet = workbench.Model.NetOfRow(y);
            StageRoute wire = workbench.Model.Layout.Nets[yNet].Branches[0];
            var mouse = InputSystem.GetDevice<Mouse>();
            Move(mouse.position, workbench.ScreenPointOf(wire.PointAt(wire.Length * 0.5f)));
            yield return null;
            yield return null;
            Assert.AreEqual(yNet, workbench.LitNet, "pointing at y's wire lights it, over the selected row's");
            Assert.IsTrue(workbench.RowBandShown(y), "and puts up y's row's band");

            int rows = workbench.Model.RowCount;
            yield return Tap(_keyboard.deleteKey);
            Assert.AreEqual(rows - 1, workbench.Model.RowCount, "Delete takes the selected row out");
            Assert.IsFalse(workbench.List.AllSignals.Any(r => r.Key == "a"));
        }

        // -----------------------------------------------------------------
        // Adding signals, groups and names (Part 9b-2b, second half)
        // -----------------------------------------------------------------

        private static int SignalKeyed(WorkbenchPanel workbench, string key)
        {
            Assert.IsTrue(workbench.Model.Signals.TryFind(key, out int signal), $"the design has no {key}");
            return signal;
        }

        /// <summary>
        /// A searches every signal the design has; the hit chosen is added at the end of the rows, a
        /// block's inside as readily as a port, selected and showing its values; and a click off the list closes it.
        /// </summary>
        [UnityTest]
        public IEnumerator PlusSignal_SearchesEverySignal_AndAddsTheOneChosen()
        {
            yield return TestScene.Load();
            yield return OpenTheWorkbench();

            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            workbench.ShowSample(2);   // the full adder
            yield return null;
            workbench.Pause();
            workbench.Seek(17.0);
            int rows = workbench.Model.RowCount;

            yield return Tap(_keyboard.aKey);
            Assert.IsTrue(workbench.Picker.IsOpen, "A opens the + SIGNAL list");

            workbench.Picker.Search("h1.s");
            yield return null;
            Assert.AreEqual("h1.s", workbench.Picker.Found.First().Key, "h1's sum first, as its whole name");

            workbench.Picker.AddFirst();
            yield return null;
            Assert.AreEqual(rows + 1, workbench.Model.RowCount, "added as a row");
            int added = rows;
            Assert.AreEqual("h1.s", workbench.NameOf(added));
            Assert.IsTrue(workbench.List.Lines[added].Selected, "selected");
            Assert.AreEqual("1", workbench.ReadoutOf(added), "and reading its value: a XOR b with a = 0 and b = 1");
            Assert.IsTrue(workbench.Picker.IsOpen, "the list stays up to add another");

            yield return ClickAt(workbench.WaveScreenPointOf(30.0, 0));
            Assert.IsFalse(workbench.Picker.IsOpen, "a click off it closes it");
            Assert.AreEqual(17.0, workbench.Time, 1e-9, "and is the list's: the waves under it were not clicked");

            yield return Click(WaveformView.AddButton);
            Assert.IsTrue(workbench.Picker.IsOpen, "+ SIGNAL on the waveform's toolbar opens it too");
            workbench.Picker.Search("Z");
            yield return null;
            Assert.IsFalse(workbench.Picker.Found.Any(h => h.Key.EndsWith(".Z")), "a cell's pins are hidden");
            yield return Click(WaveSignalPicker.CellsButton);
            Assert.IsTrue(workbench.Picker.Found.Any(h => h.Key == "h1.g1.Z"), "until CELLS is on");
            workbench.Picker.Close();
        }

        /// <summary>Alt and a click on a wire adds its signal -- an inside net no port shows -- selected, its wire lit.</summary>
        [UnityTest]
        public IEnumerator AltAndAClickOnAWire_AddsItsSignal()
        {
            yield return TestScene.Load();
            yield return OpenTheWorkbench();

            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            workbench.Pause();
            int rows = workbench.Model.RowCount;
            int na = workbench.Model.Layout.NetNamed("na");
            StageRoute wire = workbench.Model.Layout.Nets[na].Branches[0];

            var mouse = InputSystem.GetDevice<Mouse>();
            mouse.MakeCurrent();
            Move(mouse.position, workbench.ScreenPointOf(wire.PointAt(wire.Length * 0.5f)));
            yield return null;
            yield return null;
            Assert.AreEqual(na, workbench.HoveredNet, "sanity: the pointer is on NOT A's wire");

            Press(_keyboard.altKey);
            yield return null;
            Press(mouse.leftButton);
            yield return null;
            Release(mouse.leftButton);
            yield return null;
            Release(_keyboard.altKey);
            yield return null;

            Assert.AreEqual(rows + 1, workbench.Model.RowCount, "Alt and a click on the wire adds a row");
            Assert.AreEqual("na", workbench.NameOf(rows), "of NOT A's output, which no port shows");
            Assert.IsTrue(workbench.List.Lines[rows].Selected);
            Assert.IsTrue(workbench.IsFitted, "and the circuit did not move with the click");
        }

        /// <summary>G puts the selected rows under a header; a click on the header shuts and opens it; UNGROUP puts them back.</summary>
        [UnityTest]
        public IEnumerator Grouping_PutsRowsUnderAHeader_AClickShutsIt_AndUngroupPutsThemBack()
        {
            yield return TestScene.Load();
            yield return OpenTheWorkbench();

            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            workbench.ShowSample(2);   // the full adder
            yield return null;
            workbench.Pause();
            foreach (string key in new[] { "h1.s", "h1.c", "h2.c" })
                workbench.AddSignal(SignalKeyed(workbench, key));
            int ports = workbench.Model.RowCount - 3;
            workbench.List.Select(ports);
            workbench.List.Select(ports + 2, extend: true);

            yield return Tap(_keyboard.gKey);
            Assert.IsTrue(workbench.List.Lines[ports].IsHeader, "G puts them under a header, where the first was");
            Assert.AreEqual(-1, workbench.Model.RowSignal(ports), "a header is a row with no signal");
            Assert.AreEqual("- group 1", workbench.NameOf(ports), "named, and marked open");
            Assert.AreEqual(ports + 4, workbench.Model.RowCount);

            yield return ClickAt(workbench.RowNameOnScreen(ports));
            Assert.AreEqual("+ group 1", workbench.NameOf(ports), "a click on the header shuts it");
            Assert.AreEqual(ports + 1, workbench.Model.RowCount, "and its rows leave the waveform");

            yield return ClickAt(workbench.RowNameOnScreen(ports));
            Assert.AreEqual(ports + 4, workbench.Model.RowCount, "and again opens it");

            var mouse = InputSystem.GetDevice<Mouse>();
            yield return ClickAt(workbench.RowNameOnScreen(ports), mouse.rightButton);
            yield return Click(WaveRowMenu.ActionItem + WaveRowAction.Ungroup);
            Assert.IsFalse(workbench.List.Lines.Any(l => l.IsHeader), "UNGROUP puts the rows back");
            Assert.AreEqual(ports + 3, workbench.Model.RowCount);
        }

        /// <summary>L names every row by its whole path, as ModelSim does, and the names column widens to take them; L again puts the short names back.</summary>
        [UnityTest]
        public IEnumerator FullNames_NameEveryRowByItsPath_AndWidenTheColumn()
        {
            yield return TestScene.Load();
            yield return OpenTheWorkbench();

            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            workbench.Pause();
            Assert.AreEqual(0f, workbench.NamesExtra, "sanity: the short names fit the reference's column");
            string shortName = workbench.NameOf(0);

            yield return Tap(_keyboard.lKey);
            StringAssert.StartsWith("/", workbench.NameOf(0), "L names the row by its path");
            StringAssert.EndsWith("/dut/" + shortName, workbench.NameOf(0));
            Assert.Greater(workbench.NamesExtra, 0f, "and the column widens for it");

            yield return Click(WaveformView.NamesButton);
            Assert.AreEqual(shortName, workbench.NameOf(0), "NAMES on the toolbar puts the short names back");
            Assert.AreEqual(0f, workbench.NamesExtra);
        }

        /// <summary>RENAME on a row's menu gives it a name of its own; an empty name gives it its own back.</summary>
        [UnityTest]
        public IEnumerator Rename_GivesARowANameOfItsOwn()
        {
            yield return TestScene.Load();
            yield return OpenTheWorkbench();

            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            workbench.Pause();
            int a = RowLabelled(workbench, "a");
            var mouse = InputSystem.GetDevice<Mouse>();

            yield return ClickAt(workbench.RowNameOnScreen(a), mouse.rightButton);
            yield return Click(WaveRowMenu.ActionItem + WaveRowAction.Rename);
            Assert.IsTrue(workbench.RenameField.IsOpen, "RENAME opens the field");
            workbench.RenameField.Confirm("in a");
            yield return null;
            Assert.AreEqual("in a", workbench.NameOf(a));

            yield return ClickAt(workbench.RowNameOnScreen(a), mouse.rightButton);
            yield return Click(WaveRowMenu.ActionItem + WaveRowAction.Rename);
            workbench.RenameField.Confirm("");
            yield return null;
            Assert.AreEqual("a", workbench.NameOf(a), "empty gives it its own back");
        }

        // -----------------------------------------------------------------
        // Running: $stop, the speed, STEP, RUN TO and breakpoints (Part 9b-3)
        // -----------------------------------------------------------------

        /// <summary>Frames until playing stops by itself, or the cap: what a test waits on, never a count of seconds.</summary>
        private static IEnumerator UntilItPauses(WorkbenchPanel workbench, int cap = 600)
        {
            for (int i = 0; i < cap && workbench.Playing; i++)
                yield return null;
            Assert.IsFalse(workbench.Playing, "playing never stopped by itself");
        }

        /// <summary>
        /// Played to the end, the run pauses at its testbench's $stop and the chip says so; PLAY there runs
        /// the run on, as long again, and the timeline, the slider and the waveform grow with it.
        /// </summary>
        [UnityTest]
        public IEnumerator PlayingToTheTestbenchsStop_PausesThere_AndPlayRunsTheRunOn()
        {
            yield return TestScene.Load();
            yield return OpenTheWorkbench();

            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            workbench.SetSpeed(Playback.InstantSpeed);
            long end = workbench.Model.End;

            yield return Tap(_keyboard.rKey);
            yield return UntilItPauses(workbench);
            Assert.AreEqual(end, workbench.Time, 1e-9, "paused at the end");
            Assert.AreEqual(PauseReason.Suspended, workbench.Playback.Reason);
            StringAssert.StartsWith("$stop at " + end + " ps", workbench.ChipText, "and the chip says why");

            yield return Click(WorkbenchPanel.PlayButton);
            Assert.AreEqual(2 * end, workbench.Model.End, "PLAY at the $stop runs the run on, as long again");
            Assert.AreEqual(2 * end, GameObject.Find(WorkbenchPanel.TimeSlider).GetComponent<Slider>().maxValue, 1e-6, "the slider with it");
            Assert.AreEqual(2.0 * end, workbench.Wave.Span, 1e-9, "and the waveform");

            yield return UntilItPauses(workbench);
            Assert.AreEqual(2 * end, workbench.Time, 1e-9, "played on to the new end");
            Assert.AreEqual(PauseReason.End, workbench.Playback.Reason);
        }

        /// <summary>The brackets and SLOWER and FASTER step the speed along its ladder, and the label says where it is.</summary>
        [UnityTest]
        public IEnumerator TheSpeed_StepsAlongItsLadder_ByKeyAndByButton()
        {
            yield return TestScene.Load();
            yield return OpenTheWorkbench();

            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            Assert.AreEqual("1.6 ps/s", workbench.SpeedText, "the reference's speed to begin with");

            yield return Tap(_keyboard.rightBracketKey);
            Assert.AreEqual("3.2 ps/s", workbench.SpeedText, "] plays faster");
            Assert.AreEqual(3.2, workbench.Playback.UnitsPerSecond, 1e-9);

            yield return Click(WorkbenchPanel.SlowerButton);
            yield return Click(WorkbenchPanel.SlowerButton);
            Assert.AreEqual("0.8 ps/s", workbench.SpeedText, "SLOWER, twice");

            yield return Tap(_keyboard.leftBracketKey);
            Assert.AreEqual("0.4 ps/s", workbench.SpeedText, "[ plays slower");

            workbench.SetSpeed(Playback.InstantSpeed - 1);
            yield return Click(WorkbenchPanel.FasterButton);
            Assert.AreEqual("INSTANT", workbench.SpeedText, "and past the fastest, INSTANT");
        }

        /// <summary>Period and STEP go to the next edge of the selected row, comma and STEP BACK to the one before, paused.</summary>
        [UnityTest]
        public IEnumerator Step_GoesFromEdgeToEdge_OfTheSelectedRow()
        {
            yield return TestScene.Load();
            yield return OpenTheWorkbench();

            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            workbench.ShowSample(3);   // the counter
            yield return null;
            workbench.Pause();
            workbench.Seek(0.0);
            int count = RowLabelled(workbench, "count[2:0]");
            SignalTrack track = workbench.Model.RowTrack(count);

            yield return ClickAt(workbench.RowNameOnScreen(count));
            Assert.IsTrue(workbench.List.Lines[count].Selected, "sanity: count is selected");

            yield return Tap(_keyboard.periodKey);
            Assert.AreEqual(track[1].Time, workbench.Time, 1e-9, "period goes to count's first edge");
            yield return Click(WorkbenchPanel.StepButton);
            Assert.AreEqual(track[2].Time, workbench.Time, 1e-9, "STEP to its next");
            Assert.IsFalse(workbench.Playing, "a step pauses");

            yield return Tap(_keyboard.commaKey);
            Assert.AreEqual(track[1].Time, workbench.Time, 1e-9, "comma back to the one before");
            yield return Click(WorkbenchPanel.StepBackButton);
            Assert.AreEqual(track[1].Time, workbench.Time, 1e-9, "and STEP BACK goes nowhere before the first");
        }

        /// <summary>RUN TO pauses the playhead where it says, and refuses what is not a whole number of ns.</summary>
        [UnityTest]
        public IEnumerator RunTo_PausesThePlayheadThere()
        {
            yield return TestScene.Load();
            yield return OpenTheWorkbench();

            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            workbench.SetSpeed(Playback.InstantSpeed);
            workbench.SetRunTo("10");
            Assert.AreEqual(10L, workbench.Playback.RunTo);

            workbench.Restart();
            yield return UntilItPauses(workbench);
            Assert.AreEqual(10.0, workbench.Time, 1e-9);
            Assert.AreEqual("ran to 10 ps", workbench.ChipText);

            workbench.SetRunTo("ten");
            StringAssert.StartsWith("'ten' is not a time", workbench.ChipText, "a refusal, said");
            Assert.AreEqual(10L, workbench.Playback.RunTo, "and RUN TO left as it was");

            workbench.SetRunTo("");
            Assert.IsNull(workbench.Playback.RunTo, "empty is nowhere");
        }

        /// <summary>
        /// The RUN TO field gives the keyboard back once Enter or Escape is done with it. It stays on
        /// screen, unlike every other field, and TextMesh Pro leaves a finished field selected, so in the
        /// browser smoke test (2026-10-18) every workbench key was dead after Enter until a click
        /// somewhere else. The Escape that cancels it must not close the workbench as well.
        /// </summary>
        [UnityTest]
        public IEnumerator TheRunToField_GivesTheKeysBack_AfterEnterAndAfterEscape()
        {
            yield return TestScene.Load();
            yield return OpenTheWorkbench();

            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            workbench.Pause();
            TMP_InputField field = GameObject.Find(WorkbenchPanel.RunToField).GetComponent<TMP_InputField>();
            RectTransform box = field.GetComponent<RectTransform>();
            Canvas canvas = field.GetComponentInParent<Canvas>().rootCanvas;
            Camera camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            Vector2 centre = RectTransformUtility.WorldToScreenPoint(camera, box.TransformPoint(box.rect.center));

            yield return ClickAt(centre);
            Assert.IsTrue(UiText.Typing, "sanity: a click on RUN TO should give it the keyboard");

            field.text = "12";
            yield return Tap(_keyboard.enterKey);
            yield return null;
            Assert.AreEqual(12L, workbench.Playback.RunTo, "sanity: Enter should set RUN TO");
            Assert.IsFalse(UiText.Typing, "RUN TO kept the keyboard after Enter, and every workbench key with it");

            yield return Tap(_keyboard.spaceKey);
            Assert.IsTrue(workbench.Playing, "Space after RUN TO did not play");
            workbench.Pause();

            yield return ClickAt(centre);
            Assert.IsTrue(UiText.Typing, "sanity: a second click on RUN TO should give it the keyboard");
            field.text = "30";
            yield return Tap(_keyboard.escapeKey);
            yield return null;
            Assert.IsTrue(workbench.IsOpen, "the Escape that cancelled RUN TO closed the workbench as well");
            Assert.AreEqual(12L, workbench.Playback.RunTo, "Escape set what was typed");
            Assert.IsFalse(UiText.Typing, "RUN TO kept the keyboard after Escape");
        }

        /// <summary>
        /// RUN TO shows the moment it was given from its start, after a longer time was typed for it: the
        /// field scrolls its text left to keep the caret in view while one is typed, and in the browser
        /// smoke test (2026-10-19) "12" came back clipped to ".2" after "0.012 ns" was entered.
        /// </summary>
        [UnityTest]
        public IEnumerator TheRunToField_ShowsItsMomentFromItsStart_AfterALongerTimeWasTyped()
        {
            yield return TestScene.Load();
            yield return OpenTheWorkbench();

            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            workbench.Pause();
            TMP_InputField field = GameObject.Find(WorkbenchPanel.RunToField).GetComponent<TMP_InputField>();
            RectTransform box = field.GetComponent<RectTransform>();
            Canvas canvas = field.GetComponentInParent<Canvas>().rootCanvas;
            Camera camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            yield return ClickAt(RectTransformUtility.WorldToScreenPoint(camera, box.TransformPoint(box.rect.center)));

            // As typing leaves it: a time longer than the field, the caret after its last character.
            field.text = "12.0000 ps";
            field.caretPosition = field.text.Length;
            field.ForceLabelUpdate();
            yield return null;
            yield return Tap(_keyboard.enterKey);
            yield return null;
            yield return null;

            Assert.AreEqual(12L, workbench.Playback.RunTo, "sanity: Enter should set RUN TO");
            Assert.AreEqual("12", field.text, "sanity: the field shows the moment as a number");

            TMP_Text text = field.textComponent;
            text.ForceMeshUpdate();
            Vector3 firstDigit = text.rectTransform.TransformPoint(text.textInfo.characterInfo[0].bottomLeft);
            var viewport = new Vector3[4];
            field.textViewport.GetWorldCorners(viewport);
            Assert.GreaterOrEqual(firstDigit.x, viewport[0].x - 0.5f,
                "the moment's first digit is cut off at the field's left edge: the field is still scrolled for the longer time typed");
        }

        // -----------------------------------------------------------------
        // Editing an input, the unit, undo (Part 9b-3)
        // -----------------------------------------------------------------

        /// <summary>Frames until an edit's replay has taken over the run shown.</summary>
        private static IEnumerator UntilReplayed(WorkbenchPanel workbench, int cap = 900)
        {
            for (int i = 0; i < cap && workbench.Session.Replaying; i++)
                yield return null;
            Assert.IsFalse(workbench.Session.Replaying, "the replay never took over");
        }

        /// <summary>What row <paramref name="row"/> shows at <paramref name="time"/>, in binary, as the model shown has it.</summary>
        private static string ValueAt(WorkbenchPanel workbench, int row, double time)
        {
            SignalTrack track = workbench.Model.RowTrack(row);
            int index = track.IndexAt(time);
            return index < 0 ? "none" : track[index].Value.Format(BitSorter.SystemVerilog.Radix.Binary);
        }

        /// <summary>Two clicks in quick succession, as the fixture's own mouse makes them.</summary>
        private IEnumerator DoubleClickAt(Vector2 point)
        {
            yield return ClickAt(point);
            yield return ClickAt(point);
        }

        /// <summary>
        /// A double-click on a one-bit input's wave flips it over the stretch it is in; the run is played
        /// again beside the one shown, takes over without losing the rows, and shows the input flipped.
        /// </summary>
        [UnityTest]
        public IEnumerator ADoubleClickOnABitInput_FlipsIt_AndTheRunIsPlayedAgain()
        {
            yield return TestScene.Load();
            yield return OpenTheWorkbench();

            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            workbench.Pause();
            workbench.Session.Finish();
            int a = RowLabelled(workbench, "a");
            int s = RowLabelled(workbench, "s[3:0]");
            workbench.SetRadix(s, WaveRadix.Unsigned);
            WorkbenchModel before = workbench.Model;
            Assert.AreEqual("1", ValueAt(workbench, a, 10.0), "sanity: a rises at 6 and stays up");

            yield return DoubleClickAt(workbench.WaveScreenPointOf(10.3, a));
            Assert.IsTrue(workbench.Session.Replaying || workbench.Model != before, "the double-click edited nothing");
            yield return UntilReplayed(workbench);

            Assert.AreNotSame(before, workbench.Model, "the run shown is the one played again");
            Assert.AreEqual("0", ValueAt(workbench, a, 10.0), "a is 0 over the stretch the double-click was in");
            Assert.AreEqual("0", ValueAt(workbench, a, 3.0), "and was before it, as it always was");
            Assert.AreEqual(RowLabelled(workbench, "s[3:0]"), s, "the rows are the ones shown before");
            Assert.AreEqual(WaveRadix.Unsigned, workbench.List.Lines[s].Signal.Radix, "in the radix they were in");
            StringAssert.DoesNotContain("not an input", workbench.ChipText ?? "", "and nothing was refused");
        }

        /// <summary>
        /// A double-click on a bus asks for its value over the stretch, in a field that says which; Enter
        /// sets it, and the run played again shows it there and what it had after.
        /// </summary>
        [UnityTest]
        public IEnumerator ADoubleClickOnABus_AsksForItsValue_AndSetsItOverTheStretch()
        {
            yield return TestScene.Load();
            yield return OpenTheWorkbench();

            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            workbench.Pause();
            workbench.Session.Finish();
            int s = RowLabelled(workbench, "s[3:0]");
            string after = ValueAt(workbench, s, 20.0);

            yield return DoubleClickAt(workbench.WaveScreenPointOf(12.4, s));
            WaveRenameField field = workbench.SetValueField;
            Assert.IsTrue(field.IsOpen, "a double-click on a bus did not ask for its value");
            Assert.IsTrue(UiText.Typing, "and the field has the keyboard");
            StringAssert.Contains("SET s FROM 10 ps TO 19 ps", field.GetComponentsInChildren<TMP_Text>(true).First(t => t.name == "title").text);

            field.GetComponentInChildren<TMP_InputField>().text = "4'b1111";
            yield return Tap(_keyboard.enterKey);
            yield return null;
            Assert.IsFalse(field.IsOpen);
            yield return UntilReplayed(workbench);

            Assert.AreEqual("1111", ValueAt(workbench, s, 12.0), "s is 1111 over the stretch");
            Assert.AreEqual(after, ValueAt(workbench, s, 20.0), "and what it was from 19 on");
        }

        /// <summary>A double-click on an output, or on a clock, changes nothing, and the chip says why.</summary>
        [UnityTest]
        public IEnumerator ADoubleClickOnAnOutputOrAClock_IsRefused_AndTheChipSaysWhy()
        {
            yield return TestScene.Load();
            yield return OpenTheWorkbench();

            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            workbench.Pause();
            workbench.Session.Finish();

            yield return DoubleClickAt(workbench.WaveScreenPointOf(10.3, RowLabelled(workbench, "y")));
            Assert.IsFalse(workbench.Session.Replaying, "an output was edited");
            StringAssert.Contains("y is not an input", workbench.ChipText);

            workbench.ShowSample(WorkbenchSamples.Chip.ToList().FindIndex(sample => sample.Name.Contains("ounter")));
            workbench.Pause();
            workbench.Session.Finish();
            yield return null;
            yield return DoubleClickAt(workbench.WaveScreenPointOf(12.3, RowLabelled(workbench, "clk")));
            Assert.IsFalse(workbench.Session.Replaying, "a clock was edited");
            StringAssert.Contains("clk is a clock", workbench.ChipText);
        }

        /// <summary>
        /// UNIT counts time in the next unit, and U the one after: nothing is rescaled, and every time on
        /// screen -- the speed, the moment, the gates' delays -- is written in it. RUN TO reads a time in any.
        /// </summary>
        [UnityTest]
        public IEnumerator TheUnit_ChangesByButtonAndByKey_AndEveryTimeIsWrittenInIt()
        {
            yield return TestScene.Load();
            yield return OpenTheWorkbench();

            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            workbench.Pause();
            workbench.Seek(11.0);
            Assert.AreEqual("1.6 ps/s", workbench.SpeedText, "sanity: in ps to begin with, as the course's ModelSim counts");

            yield return Click(WaveformView.UnitButton);
            yield return UntilReplayed(workbench);
            Assert.AreEqual("fs", workbench.Model.Unit);
            Assert.AreEqual("1.6 fs/s", workbench.SpeedText);
            Assert.AreEqual("t = 11.0 fs", workbench.MomentText, "the moment, where it was");
            StringAssert.EndsWith(" fs", workbench.Model.Layout.Parts.First(p => p.Delay.Length > 0).Delay, "and the gates' delays");
            Assert.AreEqual("UNIT: fs", GameObject.Find(WaveformView.UnitButton).GetComponentInChildren<TMP_Text>().text);

            yield return Tap(_keyboard.uKey);
            yield return UntilReplayed(workbench);
            Assert.AreEqual("s", workbench.Model.Unit, "U, the unit after, round from fs to s");

            workbench.SetRunTo("12000 ms");
            Assert.AreEqual(12L, workbench.Playback.RunTo, "RUN TO reads a time in another unit, in the run's");
        }

        /// <summary>Ctrl+Z takes an edit back and Ctrl+Y makes it again, each played again.</summary>
        [UnityTest]
        public IEnumerator UndoAndRedo_ByKey_TakeAnEditBackAndMakeItAgain()
        {
            yield return TestScene.Load();
            yield return OpenTheWorkbench();

            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            workbench.Pause();
            workbench.Session.Finish();
            int a = RowLabelled(workbench, "a");

            yield return DoubleClickAt(workbench.WaveScreenPointOf(10.3, a));
            yield return UntilReplayed(workbench);
            Assert.AreEqual("0", ValueAt(workbench, a, 10.0), "sanity: flipped");

            Press(_keyboard.ctrlKey);
            yield return Tap(_keyboard.zKey);
            Release(_keyboard.ctrlKey);
            yield return UntilReplayed(workbench);
            Assert.AreEqual("1", ValueAt(workbench, a, 10.0), "Ctrl+Z took it back");

            Press(_keyboard.ctrlKey);
            yield return Tap(_keyboard.yKey);
            Release(_keyboard.ctrlKey);
            yield return UntilReplayed(workbench);
            Assert.AreEqual("0", ValueAt(workbench, a, 10.0), "Ctrl+Y made it again");

            Press(_keyboard.ctrlKey);
            yield return Tap(_keyboard.yKey);
            Release(_keyboard.ctrlKey);
            Assert.AreEqual("nothing to redo", workbench.ChipText);
        }

        /// <summary>
        /// E offers the exports and VCD saves the run -- in a test, into the scratch folder SaveGuard points
        /// exports at, never the player's -- and the chip says where; read back, it holds the run's inputs
        /// and outputs, in the run's unit. EXPORT on the toolbar offers the same.
        /// </summary>
        [UnityTest]
        public IEnumerator E_SavesTheRunAsAVcd_AndTheChipSaysWhere()
        {
            yield return TestScene.Load();
            yield return OpenTheWorkbench();

            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            workbench.Pause();
            workbench.Session.Finish();
            StringAssert.StartsWith(System.IO.Path.GetFullPath(SaveGuard.ScratchExports), System.IO.Path.GetFullPath(FileSaver.Folder),
                "sanity: a test's exports go to the scratch folder");

            yield return Tap(_keyboard.eKey);
            Assert.IsTrue(workbench.ExportMenu.IsOpen, "E did not offer the exports");
            yield return Click(WaveExportMenu.KindItem + WaveExportKind.Vcd);
            Assert.IsFalse(workbench.ExportMenu.IsOpen);
            string file = System.IO.Path.Combine(FileSaver.Folder, "reference.vcd");
            Assert.IsTrue(System.IO.File.Exists(file), "E saved no VCD");
            StringAssert.StartsWith("saved reference.vcd in ", workbench.ChipText);

            string vcd = System.IO.File.ReadAllText(file);
            StringAssert.Contains("$timescale 1ps $end", vcd);
            var traces = BitSorter.Sim.VcdReader.Read(new System.IO.StringReader(vcd));
            string path = workbench.Model.Layout.DesignPath;
            Assert.IsTrue(traces.ContainsKey(path + ".a") && traces.ContainsKey(path + ".y"), "the run's input and output are in it");

            System.IO.File.Delete(file);
            yield return Click(WaveformView.ExportButton);
            yield return Click(WaveExportMenu.KindItem + WaveExportKind.Vcd);
            Assert.IsTrue(System.IO.File.Exists(file), "EXPORT saved no VCD");
        }

        /// <summary>
        /// The PNG is the waveform drawn again in Schematic Light, whatever the look on screen: as wide as
        /// a picture always is, as tall as its rows, its ground the paper's and its rows' 1s in the paper's
        /// own colour -- and the screen's look is untouched after it.
        /// </summary>
        [UnityTest]
        public IEnumerator ThePng_IsTheWaveformInSchematicLight_WhateverLookIsOnScreen()
        {
            yield return TestScene.Load();
            yield return OpenTheWorkbench();

            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            workbench.Pause();
            workbench.Session.Finish();
            workbench.Seek(20.0);
            SchematicPalette screen = SchematicPalette.Current;
            Assert.AreNotSame(SchematicPalette.Light, screen, "sanity: the screen is not in Schematic Light");

            yield return Click(WaveformView.ExportButton);
            Assert.IsTrue(workbench.ExportMenu.IsOpen, "EXPORT did not offer the exports");
            yield return Click(WaveExportMenu.KindItem + WaveExportKind.Png);

            string file = System.IO.Path.Combine(FileSaver.Folder, "reference.png");
            Assert.IsTrue(System.IO.File.Exists(file), "PNG saved nothing");
            StringAssert.StartsWith("saved reference.png in ", workbench.ChipText);
            Assert.AreSame(screen, SchematicPalette.Current, "the screen's look is its own again");

            var image = new Texture2D(2, 2);
            Assert.IsTrue(image.LoadImage(System.IO.File.ReadAllBytes(file)));
            Assert.AreEqual(WaveformPng.Width, image.width);
            Assert.AreEqual(WaveformPng.HeightFor(workbench.Model.RowCount), image.height);

            Color corner = image.GetPixel(3, image.height - 4);
            Color ground = SchematicPalette.Light.Ground;
            Assert.AreEqual(ground.r, corner.r, 2f / 255f, "its ground is the paper's: " + corner);
            Assert.AreEqual(ground.g, corner.g, 2f / 255f, "its ground is the paper's: " + corner);
            Assert.AreEqual(ground.b, corner.b, 2f / 255f, "its ground is the paper's: " + corner);

            Color one = SchematicPalette.Light.One;
            int ones = image.GetPixels().Count(p => Mathf.Abs(p.r - one.r) < 0.02f && Mathf.Abs(p.g - one.g) < 0.02f && Mathf.Abs(p.b - one.b) < 0.02f);
            Assert.Greater(ones, 200, "the rows' 1s, in the paper's colour for a 1");
            Object.Destroy(image);
        }

        /// <summary>
        /// BREAK ON VALUE on a row's menu sets a breakpoint on its signal: the playhead pauses where the
        /// signal changes into the value, and the chip names both; NO BREAKS takes it away.
        /// </summary>
        [UnityTest]
        public IEnumerator BreakOnValue_FromARowsMenu_PausesWhereTheSignalTakesIt()
        {
            yield return TestScene.Load();
            yield return OpenTheWorkbench();

            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            workbench.Pause();
            workbench.SetSpeed(Playback.InstantSpeed);
            int y = RowLabelled(workbench, "y");
            SignalTrack track = workbench.Model.RowTrack(y);
            long rises = Enumerable.Range(1, track.Count - 1).Select(i => track[i]).First(c => c.Value.Format(BitSorter.SystemVerilog.Radix.Binary) == "1").Time;
            var mouse = InputSystem.GetDevice<Mouse>();

            yield return ClickAt(workbench.RowNameOnScreen(y), mouse.rightButton);
            yield return Click(WaveRowMenu.ActionItem + WaveRowAction.BreakOnValue);
            Assert.IsTrue(workbench.BreakValueField.IsOpen, "BREAK ON VALUE asks for the value");
            workbench.BreakValueField.Confirm("1");
            yield return null;
            Assert.AreEqual(1, workbench.Playback.Breakpoints.Count);

            workbench.Restart();
            yield return UntilItPauses(workbench);
            Assert.AreEqual(rises, workbench.Time, 1e-9, "paused where y first goes to 1");
            Assert.AreEqual("break: y = 1 at " + rises + " ps", workbench.ChipText);

            yield return ClickAt(workbench.RowNameOnScreen(y), mouse.rightButton);
            yield return Click(WaveRowMenu.ActionItem + WaveRowAction.NoBreaks);
            Assert.AreEqual(0, workbench.Playback.Breakpoints.Count, "NO BREAKS takes it away");

            yield return ClickAt(workbench.RowNameOnScreen(y), mouse.rightButton);
            yield return Click(WaveRowMenu.ActionItem + WaveRowAction.BreakOnValue);
            workbench.BreakValueField.Confirm("16");
            yield return null;
            Assert.AreEqual(0, workbench.Playback.Breakpoints.Count, "a value that does not fit is refused");
            StringAssert.Contains("does not fit", workbench.ChipText, "and the chip says why");
        }

        /// <summary>A right-click on the waves offers a breakpoint at that moment; set, the playhead pauses there; again, it is taken away.</summary>
        [UnityTest]
        public IEnumerator TheMenuForAMoment_SetsABreakpointThere_AndTakesItAway()
        {
            yield return TestScene.Load();
            yield return OpenTheWorkbench();

            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            workbench.Pause();
            workbench.SetSpeed(Playback.InstantSpeed);
            var mouse = InputSystem.GetDevice<Mouse>();
            int a = RowLabelled(workbench, "a");

            yield return ClickAt(workbench.WaveScreenPointOf(17.0, a), mouse.rightButton);
            Assert.IsTrue(workbench.TimeMenu.IsOpen, "a right-click on the waves opens the menu for that moment");
            Assert.IsTrue(Captioned(WaveTimeMenu.ActionItem + WaveTimeAction.ToggleBreak, "BREAK AT 17 ps"));
            yield return Click(WaveTimeMenu.ActionItem + WaveTimeAction.ToggleBreak);
            Assert.IsTrue(workbench.Playback.Breakpoints.HasAt(17));

            workbench.Restart();
            yield return UntilItPauses(workbench);
            Assert.AreEqual(17.0, workbench.Time, 1e-9);
            Assert.AreEqual("break at 17 ps", workbench.ChipText);

            yield return ClickAt(workbench.WaveScreenPointOf(17.0, a), mouse.rightButton);
            Assert.IsTrue(Captioned(WaveTimeMenu.ActionItem + WaveTimeAction.ToggleBreak, "NO BREAK AT 17 ps"));
            yield return Click(WaveTimeMenu.ActionItem + WaveTimeAction.ToggleBreak);
            Assert.IsFalse(workbench.Playback.Breakpoints.HasAt(17), "and taken away again");
        }
    }
}
