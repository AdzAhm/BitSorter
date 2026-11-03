using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using BitSorter.View;
using BitSorter.ViewModel;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.TestTools;
using BoardPoint = BitSorter.Board.Point;
using Object = UnityEngine.Object;
using UiButton = UnityEngine.UIElements.Button;
using UiElement = UnityEngine.UIElements.VisualElement;

namespace BitSorter.PlayMode.Tests
{
    /// <summary>
    /// Frame sequences for the README's GIFs: a scenario each, driven through the workbench as a student would drive it, and
    /// recorded a frame at a time into <c>Captures/gifs/&lt;name&gt;</c> under <see cref="Application.persistentDataPath"/>.
    /// <c>Tools/gifs/make_gifs.py</c> turns each folder into <c>Docs/gifs/&lt;name&gt;.gif</c>.
    /// </summary>
    /// <remarks>
    /// Not a test of behaviour, and <see cref="ExplicitAttribute"/> so it never runs as part of the suite: it runs when asked
    /// for by name -- the fixture <c>BitSorter.PlayMode.Tests.ReadmeGifs</c>, or one scenario by its method -- from the
    /// PlayMode tab or through <c>TestRunnerApi</c> with a filter, as <c>BitSorter/Capture Reference Shots</c> runs
    /// <see cref="ReferenceShots"/>. It needs the Game view, so not batch mode.
    ///
    /// **Built on <see cref="ReferenceShots"/>' set-up and for the same reasons.** The fixture's own mouse and keyboard stand in
    /// for the real ones, <see cref="SaveGuard"/> keeps the player's save and files out of it, analytics is off, the first-time
    /// hints are seen, and <see cref="ViewTime"/> is pinned so a caret does not blink in and out of a GIF. The clock is driven:
    /// every frame is exactly <see cref="FrameSeconds"/> of game time, a twentieth, so a frame recorded is a twentieth of a
    /// second of the GIF whatever the editor took to draw it, and a run plays in the GIF as fast as it plays on a screen.
    ///
    /// **Every wait is a <see cref="Beat"/>.** A beat is one frame, captured at its end; a scenario that would
    /// <c>yield return null</c> yields a beat instead, so what it does between beats is what the GIF shows between frames. Work
    /// done between two beats takes no frame, so a long calculation -- a processor's run made whole -- is a cut in the GIF, not a
    /// stall in it.
    ///
    /// **A pointer is drawn into each frame** (<see cref="DrawPointer"/>): a screen capture holds no cursor, and a GIF of
    /// drags with nothing to drag them does not read. It is the fixture's mouse as it is, an arrow, filled orange while a button
    /// is down. Where a scenario presses a control through <see cref="ShellPanel.Submit"/> rather than through the mouse -- the
    /// shell reads the platform's pointer, not the Input System's -- the pointer is glided to the control first, so it is
    /// where the click is.
    /// </remarks>
    [TestFixture, Explicit("Records the README's GIFs. Run on purpose, never as part of the suite.")]
    public class ReadmeGifs : InputTestFixture
    {
        /// <summary>Frames a second: the GIFs are made from every second frame or so of these (<c>make_gifs.py</c>).</summary>
        private const int FramesPerSecond = 20;

        /// <summary>Game time per frame while recording.</summary>
        private const float FrameSeconds = 1f / FramesPerSecond;

        /// <summary>How wide a recorded frame is; <c>make_gifs.py</c> goes on to 800.</summary>
        private const int FrameWidth = 960;

        /// <summary>Where the sequences go, a folder each. Outside the repository, beside the reference shots.</summary>
        public static string Folder => Path.Combine(Application.persistentDataPath, "Captures", "gifs");

        /// <summary>Parked here, the pointer is off the screen and drawn nowhere.</summary>
        private static readonly Vector2 Off = new Vector2(-1000f, -1000f);

        /// <summary>
        /// The pointer as an arrow, its tip the top-left X: X is the outline and a full stop the fill. The classic arrow, 11
        /// cells across and 17 down, each cell as many pixels as <see cref="DrawPointer"/> scales it to.
        /// </summary>
        private static readonly string[] Arrow =
        {
            "X          ",
            "XX         ",
            "X.X        ",
            "X..X       ",
            "X...X      ",
            "X....X     ",
            "X.....X    ",
            "X......X   ",
            "X.......X  ",
            "X........X ",
            "X.....XXXXX",
            "X..X..X    ",
            "X.X X..X   ",
            "XX  X..X   ",
            "X    X..X  ",
            "     X..X  ",
            "      XX   ",
        };

        private static float PinnedMoment => 1f / (4f * 3.5f);

        private Mouse _mouse;
        private Vector2 _at = Off;
        private bool _recording;
        private string _sequence;
        private int _frame;

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

        /// <summary>A keyboard and a mouse of the fixture's own, the mouse parked off the screen (as <see cref="ReferenceShots"/> does).</summary>
        public override void Setup()
        {
            base.Setup();

            // As PanelPlayTests explains: InputTestFixture turns on a read-value cache and its self-check, which the game
            // itself never runs with.
            InputSystem.settings.SetInternalFeatureFlag("USE_READ_VALUE_CACHING", false);

            InputSystem.AddDevice<Keyboard>();
            _mouse = InputSystem.AddDevice<Mouse>();
            Set(_mouse.position, Off);
            _at = Off;
        }

        [SetUp]
        public void FixTheClock()
        {
            Time.captureDeltaTime = FrameSeconds;
            ViewTime.Pinned = PinnedMoment;
            _recording = false;

            // The simulator's first-time hints come up as a run shows what they explain, in a strip along the circuit's foot:
            // seen already, so each GIF is of what it is about.
            var store = new ProgressStore(ProgressStore.DefaultPath);
            store.Load();
            foreach (string hint in SimulatorHints.All)
                store.MarkHintSeen(hint);
        }

        [TearDown]
        public void ClearTheSave()
        {
            _recording = false;
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
        // The GIFs
        // -----------------------------------------------------------------

        /// <summary>
        /// draw-and-wire. MY CIRCUIT, blank: a half adder built with the mouse -- two inputs, an XOR and an AND, two outputs, each
        /// taken from the strip and put down with a click, then wired pin to pin by dragging, the AND's inputs tapped onto the
        /// XOR's wires -- and run, a going to 1 and then b, the values travelling the wires.
        /// </summary>
        /// <remarks>
        /// Simplified: the README says "NOT, AND and XOR gates", and a half adder has no NOT in it, so there is none here.
        /// The gestures are those of <c>AHalfAdder_BuiltFromNothingWithTheMouse_AddsOnTheWaveform</c>, which holds them.
        /// </remarks>
        [UnityTest, Timeout(600000)]
        public IEnumerator DrawAndWire()
        {
            yield return TestScene.Load();
            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            workbench.ShowSample(0);   // MY CIRCUIT, blank under SaveGuard
            yield return Frames(4);
            workbench.Pause();
            Assert.AreEqual(0, workbench.Sample, "sanity: MY CIRCUIT is open");
            Assert.IsEmpty(Top(workbench).Parts, "sanity: and blank");

            Begin("draw-and-wire");
            yield return Beats(8);

            var ids = new List<int>();
            yield return Put(workbench, "input", new BoardPoint(1, -1), ids);
            yield return Put(workbench, "input", new BoardPoint(1, -4), ids);
            yield return Put(workbench, "xor", new BoardPoint(5, -1), ids);
            yield return Put(workbench, "and", new BoardPoint(5, -4), ids);
            yield return Put(workbench, "output", new BoardPoint(9, -1), ids);
            yield return Put(workbench, "output", new BoardPoint(9, -4), ids);
            int a = ids[0], b = ids[1], xor = ids[2], and = ids[3], sum = ids[4], carry = ids[5];
            yield return Beats(4);

            // Pin to pin, a drag each.
            foreach ((int from, string fromPin, int to, string toPin) in new[] { (a, "out", xor, "a"), (b, "out", xor, "b"), (xor, "y", sum, "in"), (and, "y", carry, "in") })
            {
                int before = Top(workbench).Wires.Count;
                yield return DragBetween(workbench, PinOf(workbench, from, fromPin), PinOf(workbench, to, toPin));
                Assert.AreEqual(before + 1, Top(workbench).Wires.Count, $"no wire from {from}.{fromPin} to {to}.{toPin}: {workbench.Editor.Refusal}");
                Assert.IsFalse(workbench.Editor.Drafting, "made, and nothing left being drawn");
            }

            // The AND's inputs onto the XOR's wires: a tap each, where the drag let go.
            int aToXor = Top(workbench).Wires.Single(w => !w.From.IsTap && w.From.Pin.Part == a && !w.To.IsTap).Id;
            int bToXor = Top(workbench).Wires.Single(w => !w.From.IsTap && w.From.Pin.Part == b && !w.To.IsTap).Id;
            foreach ((string pin, int wire) in new[] { ("a", aToXor), ("b", bToXor) })
            {
                yield return DragBetween(workbench, PinOf(workbench, and, pin), OnWireAt(workbench, wire, 0.3));
                Assert.IsTrue(Top(workbench).Wires.Any(w => w.To.IsTap && w.To.TapOf == wire && w.From.Pin.Part == and && w.From.Pin.Pin == pin),
                    $"the AND's {pin} did not tap wire {wire}: {workbench.Editor.Refusal}");
            }

            Assert.AreEqual(6, Top(workbench).Wires.Count);
            yield return Beats(6);

            // Run it: a goes to 1 at 3, b at 9, the sum and the carry following each by a gate's delay.
            string nameA = Top(workbench).FindPart(a).Name, nameB = Top(workbench).FindPart(b).Name;
            WorkbenchSession session = workbench.Session;
            Assert.IsTrue(session.SetInput(nameA, 3, long.MaxValue, BitSorter.SystemVerilog.LogicVec.FromUInt64(1, 1)).Done);
            Assert.IsTrue(session.SetInput(nameB, 9, long.MaxValue, BitSorter.SystemVerilog.LogicVec.FromUInt64(1, 1)).Done);
            session.Finish();
            yield return Frames(2);
            workbench.SetSpeed(6);   // 6.4 ns a second: a gate's delay is a few frames
            workbench.Restart();
            yield return Beats(56);

            End();
        }

        /// <summary>
        /// waves. The counter running, the second cursor dragged through the waves, a signal added from the + SIGNAL list and its
        /// radix changed from its row's menu, and the waves zoomed with the range bar -- its end dragged in, then its middle
        /// dragged on.
        /// </summary>
        [UnityTest, Timeout(600000)]
        public IEnumerator Waves()
        {
            yield return TestScene.Load();
            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            workbench.ShowSample(SampleNamed("Counter"));
            yield return Frames(2);
            workbench.Pause();
            workbench.Session.Finish();
            yield return Frames(2);
            workbench.SetSpeed(7);   // 12.8 ns a second: the counter's nine periods in three
            workbench.Restart();

            Begin("waves");
            yield return Beats(64);   // the run, to its end
            workbench.Pause();

            // The cursor: a press on the waves places it, and a drag moves it on, the values beside the rows following.
            int count = RowStarting(workbench, "count");
            yield return Glide(workbench.WaveScreenPointOf(5.0, count), 6);
            Press(_mouse.leftButton);
            yield return Beat();
            yield return Glide(workbench.WaveScreenPointOf(29.0, count), 16);
            Release(_mouse.leftButton);
            yield return Beats(8);
            Assert.IsNotNull(workbench.Cursors.Second, "sanity: the cursor is placed");

            // A signal added: the + SIGNAL list opened, "next" typed into it, the first hit taken.
            workbench.OpenPicker();
            yield return Beats(6);
            foreach (string typed in new[] { "n", "ne", "nex", "next" })
            {
                workbench.Picker.Search(typed);
                UiTheme.Defocus();
                yield return Beats(2);
            }
            Assert.IsTrue(workbench.Picker.Found.Count > 0, "sanity: the list finds the counter's next value");
            yield return Beats(4);
            workbench.Picker.AddFirst();
            yield return Beats(4);
            workbench.Picker.Close();
            yield return Beats(4);

            // Its radix: a right-click on its row's name opens the row's menu, UNSIGNED is chosen there.
            int added = workbench.List.Lines.Count - 1;
            yield return Glide(workbench.RowNameOnScreen(added), 6);
            Press(_mouse.rightButton);
            yield return Beat();
            Release(_mouse.rightButton);
            yield return Beats(4);
            if (workbench.RowMenuOpen)
            {
                UiButton unsigned = ShellPanel.Find<UiButton>(WaveRowMenu.RadixItem + WaveRadix.Unsigned);
                if (unsigned != null && unsigned.panel != null)
                {
                    yield return Glide(ShellPanel.ScreenPointOf(unsigned), 6);
                    yield return Beats(4);
                    ShellPanel.Submit(unsigned);
                }
            }
            if (workbench.RowMenuOpen || workbench.Model.RowRadix(added) != WaveRadix.Unsigned)
                workbench.SetRadix(added, WaveRadix.Unsigned);   // the menu did not take it: the row is still unsigned in the GIF
            yield return Beats(10);
            Assert.AreEqual(WaveRadix.Unsigned, workbench.Model.RowRadix(added), "sanity: its radix is changed");

            // Zoom with the range bar: its end dragged in to two fifths of the run, then the stretch dragged on through it.
            WaveRangeBar range = workbench.WavesRange;
            Assert.AreEqual(UnityEngine.UIElements.DisplayStyle.Flex, range.resolvedStyle.display, "sanity: the range bar is up over the waves");
            yield return Glide(RangePoint(range, 0.99f), 8);
            range.PressAt(0.999f);   // the stretch's end
            yield return Beat();
            yield return DragRange(range, 0.999f, -0.6f, 14);
            range.Release();
            yield return Beats(8);
            Assert.Greater(workbench.Wave.Zoom, 2.0, "sanity: zoomed in");

            float middle = (range.From + range.To) * 0.5f;
            yield return Glide(RangePoint(range, middle), 6);
            range.PressAt(middle);   // the stretch's middle
            yield return Beat();
            yield return DragRange(range, middle, 0.3f, 18);
            range.Release();
            yield return Beats(14);

            End();
        }

        /// <summary>
        /// code. The written adder's code in the circuit's window: its expression deleted and retyped, left as <c>b + </c> -- an
        /// error, marked on its line and listed once the typing pauses -- then finished as <c>b + a</c>, which applies itself, and
        /// DRAWING pressed: the drawing of the code over the waves of the run made again.
        /// </summary>
        [UnityTest, Timeout(600000)]
        public IEnumerator Code()
        {
            yield return TestScene.Load();
            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            workbench.ShowSample(SampleNamed("Written adder"));
            yield return Frames(2);
            workbench.Pause();
            workbench.Session.Finish();
            workbench.Seek(4 * WorkbenchSamples.WrittenAdderStep + 4.5);
            int add = Top(workbench).Parts.First(p => p is BitSorter.Board.BlockPart).Id;
            Assert.IsTrue(workbench.StepInto(add), "sanity: the adder's block is stepped into");
            workbench.Session.DrawCode(false);   // a written block opens as its drawing (Part 15h-4d): its code
            yield return Frames(4);
            Assert.IsTrue(workbench.Session.ShowsCode, "sanity: its code is up");

            // The source with its expression taken out: head, the expression, tail. Typed into the editor's field, which is what a
            // keystroke does (its onValueChanged is the code document's Type); not focused, so no caret.
            string source = WorkbenchSamples.AdderSource;
            const string expression = "a + b";
            int at = source.LastIndexOf(expression, System.StringComparison.Ordinal);
            Assert.GreaterOrEqual(at, 0, "sanity: the adder adds a + b");
            string head = source.Substring(0, at), tail = source.Substring(at + expression.Length);
            CodeEditorView editor = workbench.CodeView;

            Begin("code");
            yield return Beats(16);   // the code, as it is

            // Deleted, a character a beat and a half...
            foreach (string left in new[] { "a + ", "a +", "a ", "a", "" })
            {
                editor.Field.text = head + left + tail;
                yield return Beats(2);
            }

            // ...and typed again, wrongly: an operator with nothing after it.
            foreach (string typed in new[] { "b", "b ", "b +", "b + " })
            {
                editor.Field.text = head + typed + tail;
                yield return Beats(2);
            }

            // The typing has paused: after the pause the text is checked, the fault marked on its line and listed.
            for (int beat = 0; beat < 120 && !editor.ListedText.Any(); beat++)
                yield return Beat();
            Assert.IsTrue(editor.ListedText.Any(), "the fault was never listed");
            yield return Beats(28);

            // Finished: b + a, the same sum the other way round, so the text differs from what is applied and applies itself.
            editor.Field.text = head + "b + a" + tail;
            yield return Beats(2);
            for (int beat = 0; beat < 120 && (editor.Code.Pending || editor.Code.HasErrors); beat++)
                yield return Beat();
            Assert.IsFalse(editor.Code.Pending || editor.Code.HasErrors, "the finished code never applied");
            yield return Beats(10);

            // DRAWING: the code as the drawing it builds, the run made again beside it.
            yield return Glide(CentreOf(workbench.DrawingSwitch.GetComponent<RectTransform>()), 8);
            yield return Beats(3);
            workbench.DrawingSwitch.onClick.Invoke();
            yield return Beats(2);
            Assert.IsTrue(workbench.Session.ShowsDrawingOfCode, "sanity: the drawing is up");
            for (int beat = 0; beat < 240 && workbench.Session.Replaying; beat++)
                yield return Beat();
            workbench.FitView();
            workbench.SetSpeed(7);
            workbench.Restart();
            yield return Beats(56);

            End();
        }

        /// <summary>
        /// timing. The counter's TIMING tab opened with the pointer: its critical path lit on the canvas, a second path clicked and
        /// lit in its place, and the minimum clock period pointed at.
        /// </summary>
        /// <remarks>
        /// The second path is the first line other than the critical path that TIMING lets be picked (the counter's register to
        /// its output, or its input to its register); a circuit with none has the GIF without that step.
        /// </remarks>
        [UnityTest, Timeout(600000)]
        public IEnumerator Timing()
        {
            yield return TestScene.Load();
            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            workbench.ShowSample(SampleNamed("Counter"));
            yield return Frames(2);
            workbench.Pause();
            workbench.Session.Finish();
            workbench.Seek(14.5);
            yield return Frames(4);

            Begin("timing");
            yield return Beats(14);
            yield return PressShell(WorkbenchPanel.TimingTabName);
            yield return Beats(2);
            Assert.IsTrue(workbench.TimingShown, "sanity: the TIMING tab is up");
            yield return Beats(26);   // the critical path, lit from the start
            TimingView timing = workbench.Timing;
            Assert.IsNotNull(timing, "sanity: the tab has its timing");
            Assert.AreEqual(timing.Critical, workbench.TimingPicked, "sanity: the critical path is picked to begin with");

            // A second path, clicked on its row.
            int second = -1;
            for (int line = 0; line < timing.Lines.Count && second < 0; line++)
                if (line != timing.Critical && timing.Lines[line].CanPick)
                    second = line;
            if (second >= 0)
            {
                yield return PressRow(workbench, second);
                if (workbench.TimingPicked != second)
                    workbench.PickTiming(second);   // the row was not under the pointer: picked all the same
                yield return Beats(30);
            }

            // The minimum clock period, read off its row.
            int period = -1;
            for (int line = 0; line < timing.Lines.Count && period < 0; line++)
                if (timing.Lines[line].Label == "MINIMUM PERIOD")
                    period = line;
            if (period >= 0)
            {
                int row = workbench.TimingRows.ToList().FindIndex(r => r.Line == period);
                UiElement element = row >= 0 ? ShellPanel.Find<UiElement>(WorkbenchPanel.TimingRowName + row) : null;
                if (element != null && element.panel != null)
                    yield return Glide(ShellPanel.ScreenPointOf(element), 10);
            }
            yield return Beats(36);

            End();
        }

        /// <summary>
        /// logic. "Is it prime?" -- four inputs, one output -- running; then the LOGIC tab, the engine run through every
        /// combination, the truth table, the output as a minimal sum of products and its K-map, the table and the expressions
        /// scrolled under the wheel.
        /// </summary>
        [UnityTest, Timeout(600000)]
        public IEnumerator Logic()
        {
            yield return TestScene.Load();
            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            workbench.ShowSample(SampleNamed("Is it prime?"));
            yield return Frames(2);
            workbench.Pause();
            workbench.Session.Finish();
            workbench.SetSpeed(8);   // 25.6 ns a second
            workbench.Restart();

            Begin("logic");
            yield return Beats(24);   // the circuit, running its sixteen numbers
            workbench.Pause();
            yield return PressShell(WorkbenchPanel.LogicTabName);
            for (int beat = 0; beat < 240 && (workbench.Logic == null || !workbench.Logic.Done); beat++)
                yield return Beat();   // the table made, a slice a frame, and the expressions after it
            Assert.IsTrue(workbench.Logic != null && workbench.Logic.Done, workbench.LogicSays);
            yield return Beats(40);

            // The wheel over the table, then over the expressions and the K-map.
            UiElement list = ShellPanel.Find<UiElement>("logic");
            if (list != null && list.panel != null)
            {
                Rect at = list.worldBound;
                for (int notch = 0; notch < 6; notch++)
                {
                    Wheel(list, new Vector2(at.xMin + 60f, at.center.y), 3f);
                    yield return Beats(2);
                }
                yield return Beats(10);
                for (int notch = 0; notch < 6; notch++)
                {
                    Wheel(list, new Vector2(at.xMax - 60f, at.center.y), 3f);
                    yield return Beats(2);
                }
            }
            yield return Beats(30);

            End();
        }

        /// <summary>
        /// check. The half adder Exercise opened, its brief and its hint read, the sum built alone -- an XOR, wired -- and CHECK
        /// pressed: it fails, a line in the TRANSCRIPT at the moment it failed; the carry built, an AND, wired, and CHECK pressed
        /// again: the solved card.
        /// </summary>
        /// <remarks>
        /// Simplified: the parts are placed and wired through the session, a beat apart, as <see cref="ReferenceShots"/>' CHECK
        /// shots build the answer, rather than with the mouse -- <see cref="DrawAndWire"/> is the mouse's GIF. CHECK itself is
        /// pressed with the pointer.
        /// </remarks>
        [UnityTest, Timeout(600000)]
        public IEnumerator Check()
        {
            yield return TestScene.LoadSimulator();
            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            workbench.OpenExercise("half-adder");
            yield return Frames(2);
            workbench.Pause();
            workbench.Seek(25);
            for (int frame = 0; frame < 240 && !workbench.ExerciseCard.IsOpen; frame++)
                yield return null;
            Assert.IsTrue(workbench.ExerciseCard.IsOpen, "sanity: the exercise's brief is up");

            Begin("check");
            yield return Beats(30);   // the brief
            workbench.ExerciseCard.ShowTheHint();
            yield return Beats(30);
            workbench.ExerciseCard.Close();
            yield return Beats(4);

            // The sum: an XOR, wired from a and b to the sum port.
            WorkbenchSession session = workbench.Session;
            int xor = PlaceIn(session, "xor", 8, -2);
            session.Finish();
            workbench.FitView();
            yield return Beats(8);
            WireIn(session, PortIn(session, "a"), "out", xor, "a");
            session.Finish();
            yield return Beats(5);
            WireIn(session, PortIn(session, "b"), "out", xor, "b");
            session.Finish();
            yield return Beats(5);
            WireIn(session, xor, "y", PortIn(session, "sum"), "in");
            session.Finish();
            workbench.Seek(25);
            yield return Beats(10);

            // CHECK: the carry is missing, so it fails, and the TRANSCRIPT says where.
            yield return PressShell(WorkbenchPanel.CheckButton);
            Assert.IsFalse(workbench.LastGrade.Solved, "sanity: the carry is not built");
            yield return Beats(44);

            // The fix: back to the waves, the carry built.
            yield return PressShell(WorkbenchPanel.WavesTabName);
            int and = PlaceIn(session, "and", 8, 2);
            session.Finish();
            yield return Beats(8);
            WireIn(session, PortIn(session, "a"), "out", and, "a");
            session.Finish();
            yield return Beats(5);
            WireIn(session, PortIn(session, "b"), "out", and, "b");
            session.Finish();
            yield return Beats(5);
            WireIn(session, and, "y", PortIn(session, "carry"), "in");
            session.Finish();
            workbench.Pause();
            workbench.Seek(25);
            workbench.FitView();
            yield return Beats(12);

            // CHECK again: solved.
            yield return PressShell(WorkbenchPanel.CheckButton);
            Assert.IsTrue(workbench.LastGrade.Solved, "sanity: built");
            yield return Beats(60);

            End();
        }

        /// <summary>
        /// program. The multicycle processor opened on SUM; the PROGRAM chip's menu opened and FIBONACCI chosen -- the run made again
        /// with it -- then the controller stepped into, drawn as the state diagram it is, and played, the state it is in lit.
        /// </summary>
        /// <remarks>
        /// Not the pipelined processor the README's placeholder names: it is the multicycle one whose controller is a state
        /// machine, and so drawn as a state diagram (Part 15h-6e, <see cref="ReferenceShots"/>' shot 71). The pipelined one is
        /// still the reference shot 74's, for the PROGRAM chip's menu alone.
        /// </remarks>
        [UnityTest, Timeout(600000)]
        public IEnumerator Program()
        {
            yield return TestScene.Load();
            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            workbench.OpenProcessor(SampleNamed("Multicycle RISC-V"), "SUM");
            yield return Frames(4);
            workbench.Pause();
            workbench.Session.Finish();
            yield return Frames(2);
            Assert.IsTrue(workbench.Session.ShowsDrawingOfCode, "sanity: the drawing of its code");
            Assert.IsNotNull(workbench.ProgramChoice, "sanity: the PROGRAM chip is up");
            workbench.FitView();
            workbench.Seek(300);
            yield return Frames(4);

            Begin("program");
            yield return Beats(24);   // the processor, running SUM

            // The PROGRAM chip, its menu, FIBONACCI.
            yield return PressShell(WorkbenchPanel.ProgramButton);
            Assert.IsTrue(workbench.ChoiceMenu.IsOpen, "sanity: the menu is open");
            yield return Beats(10);
            int member = workbench.ProgramChoice.Members.ToList().IndexOf("FIBONACCI");
            Assert.GreaterOrEqual(member, 0, "sanity: FIBONACCI is a program");
            yield return PressShell(ChoiceMenu.SlotName(member));

            // The run made again with it, made whole -- a cut in the GIF, not a stall: no frame is taken while it is made.
            workbench.Session.Finish();
            yield return Frames(2);
            workbench.FitView();
            workbench.Seek(300);
            yield return Beats(14);

            // The controller stepped into: its states as the textbook draws them.
            int control = workbench.Session.DrawnModule.Parts.First(p => p.Name == "control").Id;
            Assert.IsTrue(workbench.StepInto(control), "sanity: the controller is stepped into");
            workbench.Session.Finish();
            yield return Frames(2);
            workbench.FitView();
            workbench.Seek(1000);
            yield return Beats(20);

            // And played: a state a few frames, each lit as the run is in it.
            workbench.SetSpeed(9);   // 51.2 time units a second: a cycle in four frames
            workbench.TogglePlay();
            yield return Beats(72);

            End();
        }

        /// <summary>
        /// compare. A project imported from the Import folder through the FILE menu -- a mux of gates and its testbench -- and run;
        /// then the FILE menu again, the pointer on EXPORT PROJECT and then on COMPARE WITH MODELSIM'S RUN.
        /// </summary>
        /// <remarks>
        /// Simplified, because the end of this GIF needs a real ModelSim run: COMPARE reads the <c>modelsim.vcd</c> that the
        /// exported project's <c>run.do</c> has ModelSim write, and takes the waves to the first moment ModelSim's run and
        /// BitSorter's differ (<see cref="WorkbenchPanel.Compare"/>, <see cref="RunComparison"/>). This GIF stops with the
        /// pointer on the item. A real compare would need: ModelSim installed; the project exported (FILE, EXPORT PROJECT) and
        /// unzipped; its <c>run.do</c> run in ModelSim, which writes <c>modelsim.vcd</c>; that file put in the Import folder
        /// (a desktop) or picked (a browser); and then the item pressed. To show a difference, a circuit whose run ModelSim
        /// gives otherwise -- the cases <c>Docs/oracle-notes.md</c> lists -- or a dump edited by hand. Nothing here writes a
        /// dump of its own: a doctored dump in a GIF would show ModelSim saying what it never said.
        /// EXPORT PROJECT is pointed at and not pressed: the chip would say where the zip went, a path under the user's folder.
        /// </remarks>
        [UnityTest, Timeout(600000)]
        public IEnumerator Compare()
        {
            Assert.IsNotNull(FileImport.Redirected, "sanity: the Import folder is the recording's scratch one, never the player's");
            Directory.CreateDirectory(FileImport.Folder);
            foreach (string old in Directory.GetFiles(FileImport.Folder))
                File.Delete(old);
            File.WriteAllText(Path.Combine(FileImport.Folder, "mux2.sv"),
                "module mux2 (input logic a, input logic b, input logic s, output logic y);\n" +
                "    logic s_n, n0, n1;\n" +
                "    INV   #(.Tpdlh(2), .Tpdhl(1)) g0 (.A(s), .Z(s_n));\n" +
                "    NAND2 #(.Tpdlh(3), .Tpdhl(2)) g1 (.A(a), .B(s_n), .Z(n0));\n" +
                "    NAND2 #(.Tpdlh(3), .Tpdhl(2)) g2 (.A(b), .B(s), .Z(n1));\n" +
                "    NAND2 #(.Tpdlh(3), .Tpdhl(2)) g3 (.A(n0), .B(n1), .Z(y));\n" +
                "endmodule\n");
            File.WriteAllText(Path.Combine(FileImport.Folder, "mux2_tb.sv"),
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

            yield return TestScene.Load();
            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            yield return Frames(2);

            Begin("compare");
            yield return Beats(10);

            // FILE, IMPORT A PROJECT FROM THE IMPORT FOLDER.
            yield return PressShell(WorkbenchPanel.FileButton, 8);
            Assert.IsTrue(workbench.FileMenu.IsOpen, "sanity: the FILE menu is open");
            yield return Beats(10);
            yield return PressShell(FileMenu.ActionItem + FileAction.ImportFolder, 8);
            Assert.AreEqual("mux2_tb", workbench.Session.Project.Bench, "sanity: imported, run by its testbench");
            workbench.SetSpeed(7);   // 12.8 ns a second: its forty in three
            workbench.Restart();
            yield return Beats(66);
            workbench.Pause();

            // FILE again: EXPORT PROJECT, and COMPARE WITH MODELSIM'S RUN, the pointer on each.
            yield return PressShell(WorkbenchPanel.FileButton, 8);
            Assert.IsTrue(workbench.FileMenu.IsOpen, "sanity: the FILE menu is open again");
            UiButton export = ShellPanel.Find<UiButton>(FileMenu.ActionItem + FileAction.ExportProject);
            UiButton compare = ShellPanel.Find<UiButton>(FileMenu.ActionItem + FileAction.CompareModelSim);
            Assert.IsTrue(export != null && export.panel != null, "sanity: the menu offers EXPORT PROJECT");
            Assert.IsTrue(compare != null && compare.panel != null, "sanity: and COMPARE WITH MODELSIM'S RUN");
            yield return Glide(ShellPanel.ScreenPointOf(export), 10);
            yield return Beats(18);
            yield return Glide(ShellPanel.ScreenPointOf(compare), 8);
            yield return Beats(40);

            End();
        }

        // -----------------------------------------------------------------
        // Recording
        // -----------------------------------------------------------------

        /// <summary>Starts a sequence: its folder made empty, the next <see cref="Beat"/> its first frame.</summary>
        private void Begin(string name)
        {
            _sequence = Path.Combine(Folder, name);
            if (Directory.Exists(_sequence))
                Directory.Delete(_sequence, true);
            Directory.CreateDirectory(_sequence);
            _frame = 0;
            _recording = true;
        }

        /// <summary>Ends the sequence: nothing more is recorded, and the frames written are counted in the log.</summary>
        private void End()
        {
            _recording = false;
            Assert.Greater(_frame, 0, "nothing was recorded");
            Debug.Log($"[ReadmeGifs] {_frame} frames ({_frame / (float)FramesPerSecond:0.0} s) in {_sequence}");
        }

        /// <summary>
        /// One frame: waits for the end of the frame, writes what it drew while recording, and carries on in the next. A scenario
        /// waits on this where a test would yield null, so what it does between two beats is what the GIF shows between two frames.
        /// </summary>
        private IEnumerator Beat()
        {
            if (_recording)
            {
                yield return new WaitForEndOfFrame();
                WriteFrame();
            }

            yield return null;
        }

        private IEnumerator Beats(int count)
        {
            for (int i = 0; i < count; i++)
                yield return Beat();
        }

        /// <summary>Frames that are not recorded: for the game to catch up with something done to it, never for a GIF.</summary>
        private static IEnumerator Frames(int count)
        {
            for (int i = 0; i < count; i++)
                yield return null;
        }

        /// <summary>The Game view, the pointer drawn in, scaled to <see cref="FrameWidth"/> and written as the next numbered PNG.</summary>
        private void WriteFrame()
        {
            Texture2D shot = ScreenCapture.CaptureScreenshotAsTexture();
            Assert.IsNotNull(shot, "the Game view could not be captured -- is there a Game view?");
            try
            {
                DrawPointer(shot);
                Texture2D small = Downscale(shot, Mathf.Min(FrameWidth, shot.width));
                try
                {
                    File.WriteAllBytes(Path.Combine(_sequence, $"f{_frame:0000}.png"), small.EncodeToPNG());
                }
                finally
                {
                    Object.DestroyImmediate(small);
                }
            }
            finally
            {
                Object.DestroyImmediate(shot);
            }

            _frame++;
        }

        /// <summary>The arrow where the fixture's mouse is, drawn into the capture: orange while the left or right button is down.</summary>
        private void DrawPointer(Texture2D shot)
        {
            if (_at.x < 0f || _at.y < 0f || Screen.width <= 0 || _mouse == null)
                return;

            float scale = shot.width / (float)Screen.width;
            int cell = Mathf.Max(1, Mathf.RoundToInt(shot.height / 360f));   // 3 pixels a cell at 1080 high: 16 pixels across in the 960-wide frame
            int tipX = Mathf.RoundToInt(_at.x * scale), tipY = Mathf.RoundToInt(_at.y * scale);   // y up, as the texture's rows are
            bool down = _mouse.leftButton.isPressed || _mouse.rightButton.isPressed;
            Color fill = down ? new Color(1f, 0.62f, 0.12f) : Color.white;

            for (int row = 0; row < Arrow.Length; row++)
            {
                for (int column = 0; column < Arrow[row].Length; column++)
                {
                    char c = Arrow[row][column];
                    if (c == ' ')
                        continue;
                    Color colour = c == 'X' ? Color.black : fill;
                    for (int dy = 0; dy < cell; dy++)
                    {
                        for (int dx = 0; dx < cell; dx++)
                        {
                            int x = tipX + column * cell + dx, y = tipY - row * cell - dy - 1;
                            if (x >= 0 && x < shot.width && y >= 0 && y < shot.height)
                                shot.SetPixel(x, y, colour);
                        }
                    }
                }
            }

            shot.Apply();
        }

        /// <summary>
        /// A capture scaled down to <paramref name="width"/>, keeping its aspect: <c>FrameRecorder.Downscale</c>, which the 4.x
        /// README's animation was made with, down to the sRGB correction -- the project renders in linear colour space and the
        /// capture comes back already encoded, which a PNG would encode again.
        /// </summary>
        private static Texture2D Downscale(Texture2D source, int width)
        {
            int height = Mathf.Max(1, Mathf.RoundToInt(width * (float)source.height / source.width));

            RenderTexture rt = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
            rt.filterMode = FilterMode.Bilinear;
            source.filterMode = FilterMode.Bilinear;

            Graphics.Blit(source, rt);

            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = rt;

            var result = new Texture2D(width, height, TextureFormat.RGB24, false);
            result.ReadPixels(new Rect(0, 0, width, height), 0, 0);

            if (QualitySettings.activeColorSpace == ColorSpace.Linear)
            {
                Color[] pixels = result.GetPixels();

                for (int i = 0; i < pixels.Length; i++)
                    pixels[i] = pixels[i].linear;

                result.SetPixels(pixels);
            }

            result.Apply();

            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);

            return result;
        }

        // -----------------------------------------------------------------
        // The pointer
        // -----------------------------------------------------------------

        /// <summary>
        /// The pointer glided to <paramref name="to"/> over <paramref name="frames"/> beats, easing in and out. The first time it
        /// is used it appears there, for it has nowhere on the screen to come from.
        /// </summary>
        private IEnumerator Glide(Vector2 to, int frames)
        {
            _mouse.MakeCurrent();
            if (_at.x < 0f)
            {
                _at = to;
                Move(_mouse.position, _at);
                yield return Beat();
                yield break;
            }

            Vector2 start = _at;
            for (int i = 1; i <= frames; i++)
            {
                float t = i / (float)frames;
                _at = Vector2.Lerp(start, to, t * t * (3f - 2f * t));
                Move(_mouse.position, _at);
                yield return Beat();
            }
        }

        private IEnumerator GlideTo(WorkbenchPanel workbench, BoardPoint at, int frames) => Glide(workbench.ScreenPointOf(at), frames);

        /// <summary>A press and a release of <paramref name="button"/> (the left one for none), the frames between them.</summary>
        private IEnumerator TapMouse(ButtonControl button = null)
        {
            button ??= _mouse.leftButton;
            Press(button);
            yield return Beat();
            Release(button);
            yield return Beat();
            yield return Beat();
        }

        /// <summary>A drag on the board from <paramref name="start"/> to <paramref name="end"/>: the pointer glided to the start, pressed, glided on, let go.</summary>
        private IEnumerator DragBetween(WorkbenchPanel workbench, BoardPoint start, BoardPoint end)
        {
            yield return GlideTo(workbench, start, 4);
            Press(_mouse.leftButton);
            yield return Beat();
            yield return GlideTo(workbench, end, 5);
            Release(_mouse.leftButton);
            yield return Beat();
            yield return Beat();
        }

        /// <summary>
        /// A part taken from the strip -- the pointer to its button first, where it has one on screen -- and put down with a click at
        /// <paramref name="at"/>; its id is added to <paramref name="ids"/>.
        /// </summary>
        private IEnumerator Put(WorkbenchPanel workbench, string key, BoardPoint at, List<int> ids)
        {
            UiButton button = ShellPanel.Find<UiButton>(WorkbenchPanel.PartButtonName(key));
            if (button != null && button.panel != null && button.worldBound.width > 0f)
                yield return Glide(ShellPanel.ScreenPointOf(button), 4);

            int before = Top(workbench).Parts.Count;
            Assert.IsTrue(workbench.TakeFromStrip(key), key);
            yield return GlideTo(workbench, at, 6);
            yield return TapMouse();
            Assert.AreEqual(before + 1, Top(workbench).Parts.Count, $"the {key} was not placed at {at}: {workbench.Editor.Refusal}");
            Assert.AreEqual(1, workbench.Editor.Selection.Parts.Count, $"the {key} was not placed at {at}: {workbench.Editor.Refusal}");
            ids.Add(workbench.Editor.Selection.Parts.Single());
        }

        /// <summary>
        /// A shell control pressed: the pointer glided onto it, then pressed as Enter on it would (<see cref="ShellPanel.Submit"/>),
        /// which is what the tests do -- the shell reads the platform's pointer, not the Input System's.
        /// </summary>
        private IEnumerator PressShell(string name, int frames = 8)
        {
            UiButton button = ShellPanel.Find<UiButton>(name);
            Assert.IsNotNull(button, $"no '{name}' on screen");
            Assert.IsNotNull(button.panel, $"'{name}' is not on a panel");
            yield return Glide(ShellPanel.ScreenPointOf(button), frames);
            yield return Beat();
            ShellPanel.Submit(button);
            yield return Beat();
            yield return Beat();
        }

        /// <summary>TIMING's line <paramref name="line"/> clicked on its row: the pointer glided there and a press sent to it.</summary>
        private IEnumerator PressRow(WorkbenchPanel workbench, int line)
        {
            int row = workbench.TimingRows.ToList().FindIndex(r => r.Line == line);
            UiElement element = row >= 0 ? ShellPanel.Find<UiElement>(WorkbenchPanel.TimingRowName + row) : null;
            if (element == null || element.panel == null)
                yield break;

            yield return Glide(ShellPanel.ScreenPointOf(element), 10);
            yield return Beat();
            Press(element);
            yield return Beat();
            yield return Beat();
        }

        /// <summary>The range bar's <paramref name="share"/> of the way along, on the screen.</summary>
        private static Vector2 RangePoint(WaveRangeBar range, float share)
        {
            Rect on = ShellPanel.ScreenRectOf(range);
            return new Vector2(on.xMin + on.width * Mathf.Clamp01(share), on.center.y);
        }

        /// <summary>
        /// The range bar's press dragged <paramref name="by"/> of its width over <paramref name="frames"/> beats, the pointer going
        /// with it from <paramref name="origin"/>: each beat asks for how far the drag has got since the press, as the bar's own does.
        /// </summary>
        private IEnumerator DragRange(WaveRangeBar range, float origin, float by, int frames)
        {
            for (int i = 1; i <= frames; i++)
            {
                float t = i / (float)frames;
                float eased = t * t * (3f - 2f * t);
                range.MoveBy(by * eased);
                _at = RangePoint(range, origin + by * eased);
                Move(_mouse.position, _at);
                yield return Beat();
            }
        }

        private static Vector2 CentreOf(RectTransform rect)
        {
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);   // a screen-space canvas: world is the screen's pixels
            return (Vector2)((corners[0] + corners[2]) * 0.5f);
        }

        /// <summary>A pointer pressed on <paramref name="element"/>'s middle, as UI Toolkit hands it on.</summary>
        private static void Press(UiElement element)
        {
            var imgui = new Event { type = EventType.MouseDown, button = 0, clickCount = 1, mousePosition = element.worldBound.center };
            using (UnityEngine.UIElements.PointerDownEvent down = UnityEngine.UIElements.PointerDownEvent.GetPooled(imgui))
            {
                down.target = element;
                element.SendEvent(down);
            }
        }

        /// <summary>The wheel turned over <paramref name="element"/> at <paramref name="at"/>, as UI Toolkit hands it on.</summary>
        private static void Wheel(UiElement element, Vector2 at, float delta)
        {
            var imgui = new Event { type = EventType.ScrollWheel, delta = new Vector2(0f, delta), mousePosition = at };
            using (UnityEngine.UIElements.WheelEvent wheel = UnityEngine.UIElements.WheelEvent.GetPooled(imgui))
            {
                wheel.target = element;
                element.SendEvent(wheel);
            }
        }

        // -----------------------------------------------------------------
        // The document
        // -----------------------------------------------------------------

        private static BitSorter.Board.Module Top(WorkbenchPanel workbench) => workbench.Session.Project.TopModule;

        /// <summary>Where a part's pin is on the board, as the part stands now.</summary>
        private static BoardPoint PinOf(WorkbenchPanel workbench, int part, string pin)
        {
            BitSorter.Board.Module top = Top(workbench);
            return BitSorter.Board.PartGeometry.Placed(workbench.Session.Project, top.FindPart(part), top.Layout.PlacementOf(part)).Pins.Single(p => p.Pin == pin).At;
        }

        /// <summary>A point <paramref name="along"/> of the way along a wire, as it is drawn now.</summary>
        private static BoardPoint OnWireAt(WorkbenchPanel workbench, int wire, double along)
        {
            var points = BitSorter.Board.WireGeometry.Wires(workbench.Session.Project, Top(workbench))[wire];
            return BitSorter.Board.WireGeometry.PointAt(points, BitSorter.Board.WireGeometry.Length(points) * along);
        }

        private static int SampleNamed(string name)
        {
            for (int i = 0; i < WorkbenchSamples.Chip.Count; i++)
                if (WorkbenchSamples.Chip[i].Name == name)
                    return i;
            Assert.Fail($"no sample called {name}");
            return -1;
        }

        /// <summary>The first row of the waves whose label starts with <paramref name="prefix"/>.</summary>
        private static int RowStarting(WorkbenchPanel workbench, string prefix)
        {
            WaveList list = workbench.List;
            return Enumerable.Range(0, list.Lines.Count).First(i => list.LabelOf(list.Lines[i]).StartsWith(prefix, System.StringComparison.Ordinal));
        }

        private static int PlaceIn(WorkbenchSession session, string key, double x, double y)
        {
            BitSorter.Board.EditResult placed = session.Place(PartsCatalog.Find(session.Project, session.Module, key), new BitSorter.Board.Placement(x, y));
            Assert.IsTrue(placed.Done, placed.Refusal);
            return placed.Id;
        }

        private static void WireIn(WorkbenchSession session, int from, string fromPin, int to, string toPin) =>
            Assert.IsTrue(session.Connect(BitSorter.Board.Endpoint.At(from, fromPin), BitSorter.Board.Endpoint.At(to, toPin)).Done);

        private static int PortIn(WorkbenchSession session, string name) => session.Project.TopModule.Ports.Single(p => p.Name == name).Id;
    }
}
