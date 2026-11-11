using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using BitSorter.View;
using BitSorter.ViewModel;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BitSorter.PlayMode.Tests
{
    /// <summary>
    /// Screenshots of the simulator in a fixed set of states, for judging how it looks.
    /// </summary>
    /// <remarks>
    /// Not a test of behaviour, and <see cref="ExplicitAttribute"/> so it never runs as part of the
    /// suite: it runs when asked for by name, from <c>BitSorter/Capture Reference Shots</c>.
    ///
    /// It is a Play Mode fixture rather than a menu item that drives the game, because driving the
    /// game is using it. Opening an exercise, placing gates and checking a circuit go through
    /// <see cref="ProgressTracker"/> exactly as a student's would -- and the first screenshots of
    /// the 4.x redesign were taken that way, by hand, and marked half-adder solved on the
    /// developer's own save with a stranger's circuit as its best. A fixture gets
    /// <see cref="SaveGuard"/>, analytics switched off and a clean scene for nothing.
    ///
    /// Shots 1 to 26 but 17 and 18 were of the 4.x game, and went with it in Part 16a; the numbers of the rest stand, so a
    /// shot keeps its name across captures.
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
    /// during a run put a refusal toast into two shots of the 4.x board, and a pointer resting over
    /// its help badge tinted it in two more -- both from someone using the editor while it ran.
    /// </remarks>
    [TestFixture, Explicit("Captures reference screenshots. Run on purpose, never as part of the suite.")]
    public class ReferenceShots : InputTestFixture
    {
        /// <summary>Game time per frame while capturing.</summary>
        private const float FrameSeconds = 1f / 120f;

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

        /// <summary>The real scene on the reference circuit (<see cref="TestScene.Load"/>).</summary>
        private static IEnumerator LoadTheGame()
        {
            yield return TestScene.Load();
        }

        /// <summary>
        /// The moment every ambient pulse is shown at -- a caret's blink, a hint's breath.
        /// </summary>
        /// <remarks>
        /// The top of the 4.x board's collision warning's throb, at 3.5 Hz, so the collision shot showed the warning at its
        /// clearest. The warning went with the 4.x game in Part 16a; the moment stays, so no shot moved with it.
        /// </remarks>
        private static float PinnedMoment => 1f / (4f * 3.5f);

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
            InputSystem.runInBackground = true;   // as the player runs (Run In Background): a click is still taken with the editor behind another window

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

            // The simulator's first-time hints (Part 15f) come up as a run shows what they explain, a moment after the
            // workbench opens: seen already, so each shot is of what it is about. Shot 56 is of one, and leaves it unseen.
            var store = new ProgressStore(ProgressStore.DefaultPath);
            store.Load();
            bool hintShot = TestContext.CurrentContext.Test.MethodName == nameof(Shot56_FirstTimeHint);
            foreach (string hint in SimulatorHints.All)
                if (!(hintShot && hint == SimulatorHints.GateDelay))
                    store.MarkHintSeen(hint);
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

        /// <summary>
        /// The settings, over the reference circuit, asking whether to reset: the one question in the game whose answer
        /// cannot be taken back.
        /// </summary>
        [UnityTest]
        public IEnumerator Shot17_SettingsAsking()
        {
            yield return LoadTheGame();
            yield return Frames(2);

            ShellPanel.Submit(ShellPanel.Find<UnityEngine.UIElements.Button>(WorkbenchPanel.SettingsButton));
            yield return Frames(2);

            ShellPanel.Submit(ShellPanel.Find<UnityEngine.UIElements.Button>(SettingsPanel.ResetButton));   // a page of the shell's (Part 15g)
            yield return Frames(2);

            // The page scrolls since Part 15g: down to the question, the thing this shot is of.
            ShellPanel.Find<UnityEngine.UIElements.ScrollView>("settings scroll")
                .ScrollTo(ShellPanel.Find<UnityEngine.UIElements.VisualElement>(SettingsPanel.StatusName));
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

            ShellPanel.Submit(ShellPanel.Find<UnityEngine.UIElements.Button>(WorkbenchPanel.SettingsButton));
            yield return Frames(2);

            ShellPanel.Submit(ShellPanel.Find<UnityEngine.UIElements.Button>(SettingsPanel.CreditsButton));

            const float step = 0.1f;
            Time.captureDeltaTime = step;
            yield return Frames(Mathf.RoundToInt(14f / step));
            Time.captureDeltaTime = FrameSeconds;
            yield return Frames(2);

            Assert.IsTrue(Find<CreditsPanel>().IsOpen, "sanity: the credits should be rolling");

            yield return Capture("18-credits");
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
            workbench.Session.DrawCode(false);   // a written block opens as its drawing (Part 15h-4d): its code
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

            yield return Frames(2);

            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            workbench.ImportFrom(FileImport.Folder);
            yield return Frames(2);
            workbench.Pause();
            workbench.Session.Finish();
            workbench.Seek(23.5);
            workbench.ShowTranscript();
            yield return Frames(30);

            Assert.AreEqual("mux2_tb", workbench.Session.Project.Bench, "sanity: imported, run by its testbench");
            yield return Capture("46-workbench-imported-project");
        }

        /// <summary>
        /// Part 15h-4d: an imported written design shown as its code, locked -- the padlock shut beside the DRAWING switch, the
        /// strip keeping it room clear of REVERT, the code read only.
        /// </summary>
        [UnityTest]
        public IEnumerator Shot66_WorkbenchLockedCode()
        {
            Assert.IsNotNull(FileImport.Redirected, "sanity: the Import folder is the capture's scratch one, never the player's");
            System.IO.Directory.CreateDirectory(FileImport.Folder);
            foreach (string old in System.IO.Directory.GetFiles(FileImport.Folder))
                System.IO.File.Delete(old);
            System.IO.File.WriteAllText(System.IO.Path.Combine(FileImport.Folder, "mux2.sv"),
                "// A 2-to-1 multiplexer, written for BitSorter's reference shots.\n" +
                "module mux2 (input logic a, input logic b, input logic s, output logic y);\n" +
                "    always_comb y = s ? b : a;\n" +
                "endmodule\n");

            yield return LoadTheGame();
            yield return Frames(2);

            yield return Frames(2);

            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            workbench.ImportFrom(FileImport.Folder);
            yield return Frames(2);
            workbench.Pause();
            workbench.Session.Finish();
            workbench.Session.DrawCode(false);   // its code, which opens as its drawing
            yield return Frames(30);

            Assert.IsTrue(workbench.Session.Locked && workbench.Session.ShowsCode, "sanity: imported, locked, its code shown");
            yield return Capture("66-workbench-locked-code");
        }

        /// <summary>
        /// Part 15h-4d: the waves zoomed in about their middle, the range bar's thumb a third of its trough and the circuit's
        /// zoom slider at its fit.
        /// </summary>
        [UnityTest]
        public IEnumerator Shot67_WorkbenchWavesZoomed()
        {
            yield return LoadTheGame();
            yield return Frames(2);

            yield return Frames(2);

            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            workbench.Pause();
            workbench.Session.Finish();
            yield return Frames(2);
            Vector2 middle = workbench.WaveScreenPointOf(workbench.Wave.Span * 0.5);
            for (int i = 0; i < 5; i++)
                workbench.TakeWaveWheel(middle, new Vector2(0f, 120f), false);
            yield return Frames(30);

            Assert.Greater(workbench.Wave.Zoom, 2.5, "sanity: zoomed in");
            yield return Capture("67-workbench-waves-zoomed");
        }

        /// <summary>
        /// Part 15h-6: the single-cycle RV32I running its demo, stepped inside its datapath -- the PC, the register file, the
        /// immediate generator, the branch comparator and the ALU as blocks, a mux for each choice between them.
        /// </summary>
        [UnityTest]
        public IEnumerator Shot68_WorkbenchSingleCycleDatapath() => Processor("Single-cycle RISC-V", 1500, "datapath", "68-workbench-single-cycle-datapath");

        /// <summary>
        /// Part 15h-6: the multicycle RV32I mid-run, stepped inside its datapath -- the registers between the steps (IR and the
        /// instruction's PC, A, B, ALU out, MDR) beside the blocks the single-cycle one has.
        /// </summary>
        [UnityTest]
        public IEnumerator Shot69_WorkbenchMulticycleDatapath() => Processor("Multicycle RISC-V", 6000, "datapath", "69-workbench-multicycle-datapath");

        /// <summary>Part 15h-6: the pipelined RV32I drawn as the textbook draws one -- a block a stage, a block a register between -- mid-run.</summary>
        [UnityTest]
        public IEnumerator Shot70_WorkbenchPipelinedProcessor() => Processor("Pipelined RISC-V", 1800, null, "70-workbench-pipelined");

        /// <summary>
        /// Part 15h-6e: the multicycle RV32I's controller stepped into, mid-run -- its eleven states as the textbook draws them,
        /// FETCH on the left, each a box of what it sets and every arrow labelled in the code's words, the state it is in lit
        /// and the arrow it came by; its ALU decoder's table beside it. (Until 15h-6e, its 224 parts drawn literally.)
        /// </summary>
        [UnityTest]
        public IEnumerator Shot71_WorkbenchMulticycleControlWrapped() => Processor("Multicycle RISC-V", 6000, "control", "71-workbench-multicycle-control");

        /// <summary>
        /// Part 15h-6e: the single-cycle RV32I's controller stepped into, mid-run -- its two decoders as control tables, a row
        /// an opcode and a column a signal, the row of the instruction running lit.
        /// </summary>
        [UnityTest]
        public IEnumerator Shot72_WorkbenchSingleCycleControlTables() => Processor("Single-cycle RISC-V", 1500, "control", "72-workbench-single-cycle-control");

        /// <summary>A processor sample opened, stepped inside <paramref name="inside"/> where one is named, run to <paramref name="at"/> and fitted.</summary>
        /// <remarks>
        /// Each runs ISA, every instruction (Part 15h-6c): the moments these shots show were chosen on the one demo program,
        /// and the sum, which a processor opens on, has stopped by them.
        /// </remarks>
        private IEnumerator Processor(string sample, double at, string inside, string shot, string program = "ISA")
        {
            yield return LoadTheGame();
            yield return Frames(2);

            yield return Frames(2);

            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            int index = -1;
            for (int i = 0; i < WorkbenchSamples.Chip.Count; i++)
                if (WorkbenchSamples.Chip[i].Name == sample)
                    index = i;
            Assert.GreaterOrEqual(index, 0, sample);
            workbench.ShowSample(index);
            yield return Frames(2);
            workbench.Pause();
            workbench.Choose("PROGRAM", program);   // as the chip's menu chooses: the waves shown whole once it runs
            workbench.Session.Finish();
            yield return Frames(2);
            Assert.IsTrue(workbench.Session.ShowsDrawingOfCode, "sanity: the drawing of its code");
            if (inside != null)
            {
                Assert.IsTrue(workbench.StepInto(workbench.Session.DrawnModule.Parts.First(p => p.Name == inside).Id), inside);
                workbench.Session.Finish();
                yield return Frames(2);
            }
            workbench.FitView();
            workbench.Seek(at);
            yield return Frames(30);
            yield return Capture(shot);
        }

        /// <summary>Part 14b: the dock's TIMING tab over the full adder, its critical path -- a to cout -- lit on the canvas.</summary>
        [UnityTest]
        public IEnumerator Shot47_WorkbenchTimingTab()
        {
            yield return LoadTheGame();
            yield return Frames(2);

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
            yield return Frames(30);

            Assert.IsTrue(Find<WorkbenchPanel>().StartPageShowing, "sanity: nothing was open last");
            yield return Capture("50-start-page");
        }

        /// <summary>Part 15d: the EXERCISES page, chapter by chapter, the first five solved and the sixth next.</summary>
        [UnityTest]
        public IEnumerator Shot51_ExercisesPage()
        {
            var store = new ProgressStore(ProgressStore.DefaultPath);
            store.Load();
            for (int i = 0; i < 5; i++)
                store.MarkComplete(ExerciseCatalog.Run[i].Id);
            store.Save();

            yield return TestScene.LoadSimulator();
            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            workbench.ShowExercises();
            yield return Frames(30);

            Assert.IsTrue(workbench.ExercisesShowing);
            Assert.AreEqual("NEXT", workbench.ExerciseStatus(ExerciseCatalog.Run[5].Id), "sanity: the sixth is next");
            yield return Capture("51-exercises-page");
        }

        /// <summary>Part 15d: the half adder opened, its goal in the header and its brief up, the hint asked for.</summary>
        [UnityTest]
        public IEnumerator Shot52_ExerciseBrief()
        {
            yield return OpenTheHalfAdder();
            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            workbench.ExerciseCard.ShowTheHint();
            yield return Frames(30);

            Assert.IsTrue(workbench.ExerciseCard.IsOpen, "sanity: the brief");
            yield return Capture("52-exercise-brief");
        }

        /// <summary>Part 15d: the half adder with its sum alone built, CHECKed: the failed checks in the TRANSCRIPT, the run at the first.</summary>
        [UnityTest]
        public IEnumerator Shot53_ExerciseCheckFailed()
        {
            yield return OpenTheHalfAdder();
            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            workbench.ExerciseCard.Close();
            BuildHalfOfTheHalfAdder(workbench.Session, withCarry: false);
            yield return Frames(2);
            workbench.Check();
            workbench.FitView();
            yield return Frames(30);

            Assert.IsFalse(workbench.LastGrade.Solved, "sanity: the carry is not built");
            yield return Capture("53-exercise-check-failed");
        }

        /// <summary>Part 15d: the half adder built and CHECKed: the solved card, what it took and NEXT.</summary>
        [UnityTest]
        public IEnumerator Shot54_ExerciseSolved()
        {
            yield return OpenTheHalfAdder();
            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            workbench.ExerciseCard.Close();
            BuildHalfOfTheHalfAdder(workbench.Session, withCarry: true);
            yield return Frames(2);
            workbench.Pause();
            workbench.Seek(25);
            workbench.Check();
            workbench.FitView();
            yield return Frames(30);

            Assert.IsTrue(workbench.LastGrade.Solved, "sanity: built");
            yield return Capture("54-exercise-solved");
        }

        /// <summary>Part 15f: the guided tutorial on the first exercise, the NOT put down and the wire into it asked for.</summary>
        [UnityTest]
        public IEnumerator Shot55_Tutorial()
        {
            yield return TestScene.LoadSimulator();
            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            workbench.StartTutorial();
            yield return Frames(2);
            WorkbenchSession session = workbench.Session;
            BitSorter.Board.EditResult placed = session.Place(PartsCatalog.Find(session.Project, session.Module, "not"), new BitSorter.Board.Placement(6, 0));
            Assert.IsTrue(placed.Done, placed.Refusal);
            session.Finish();
            workbench.Pause();
            workbench.Seek(0.5);
            workbench.FitView();
            yield return Frames(30);

            Assert.AreEqual(GuidedTutorial.Step.WireIn, workbench.TutorialStep, "sanity: the wire in asked for");
            yield return Capture("55-tutorial");
        }

        /// <summary>Part 15f: a first-time hint up -- the half adder built, its gates' delay seen on the waves.</summary>
        [UnityTest]
        public IEnumerator Shot56_FirstTimeHint()
        {
            yield return OpenTheHalfAdder();   // the gate-delay hint left unseen by FixTheClock
            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            workbench.ExerciseCard.Close();
            BuildHalfOfTheHalfAdder(workbench.Session, withCarry: true);
            workbench.Restart();
            for (int frame = 0; frame < 600 && workbench.HintShown == null; frame++)
                yield return null;
            workbench.Pause();
            workbench.Seek(25);
            workbench.FitView();
            yield return Frames(30);

            Assert.AreEqual(SimulatorHints.GateDelay, workbench.HintShown, "sanity: the hint up");
            yield return Capture("56-first-time-hint");
        }

        /// <summary>Part 15g: the INPUTS tab on the reference -- its bus written in hex, a and b chosen for ALL COMBINATIONS.</summary>
        [UnityTest]
        public IEnumerator Shot57_WorkbenchInputsTab()
        {
            yield return LoadTheGame();
            yield return Frames(2);

            yield return Frames(2);

            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            workbench.ShowSample(1);   // the reference
            yield return Frames(2);
            workbench.Pause();
            workbench.Session.Finish();
            workbench.Seek(13.5);
            workbench.ShowInputs();
            yield return Frames(2);
            int s = workbench.Inputs.Columns.ToList().FindIndex(c => c.Port == "s");
            workbench.NextInputRadix(s);
            workbench.ChooseInput(workbench.Inputs.Columns.ToList().FindIndex(c => c.Port == "a"));
            workbench.ChooseInput(workbench.Inputs.Columns.ToList().FindIndex(c => c.Port == "b"));
            yield return Frames(30);

            Assert.IsTrue(workbench.InputsShown, "sanity: the tab up");
            yield return Capture("57-workbench-inputs-tab");
        }

        /// <summary>Part 15g: the LOGIC tab on the full adder -- its truth table, each output's expression both ways, and their K-maps.</summary>
        [UnityTest]
        public IEnumerator Shot58_WorkbenchLogicTab()
        {
            yield return LoadTheGame();
            yield return Frames(2);

            yield return Frames(2);

            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            workbench.ShowSample(2);   // the full adder
            yield return Frames(2);
            workbench.Pause();
            workbench.Session.Finish();
            workbench.Seek(26.5);
            workbench.ShowLogic();
            yield return Frames(2);
            workbench.FinishLogic();
            yield return Frames(30);

            Assert.IsTrue(workbench.Logic != null && workbench.Logic.Done, "sanity: the table made");
            yield return Capture("58-workbench-logic-tab");
        }

        /// <summary>
        /// Part 15h-4d: the LOGIC tab on the Two-bit ALU, written as code -- its long sums of products and their assign lines
        /// whole, each on as many rows as it needs and none over the next (they ended in "...", and wrapped over each other in
        /// a browser on the first try).
        /// </summary>
        [UnityTest]
        public IEnumerator Shot65_WorkbenchLogicTabLongExpressions()
        {
            yield return LoadTheGame();
            yield return Frames(2);

            yield return Frames(2);

            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            int alu = -1;
            for (int i = 0; i < WorkbenchSamples.Chip.Count; i++)
                if (WorkbenchSamples.Chip[i].Name == "Two-bit ALU")
                    alu = i;
            workbench.ShowSample(alu);
            yield return Frames(2);
            workbench.Pause();
            workbench.Session.Finish();
            workbench.ShowLogic();
            yield return Frames(2);
            workbench.FinishLogic();
            yield return Frames(30);

            Assert.IsTrue(workbench.Logic != null && workbench.Logic.Done, "sanity: the table made");
            yield return Capture("65-workbench-logic-long-expressions");
        }

        /// <summary>Part 15g: the STATES tab on the counter -- its eight states on a ring, each arrow one count on, and the next-state table.</summary>
        [UnityTest]
        public IEnumerator Shot59_WorkbenchStatesTab()
        {
            yield return LoadTheGame();
            yield return Frames(2);

            yield return Frames(2);

            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            workbench.ShowSample(3);   // the counter
            yield return Frames(2);
            workbench.Pause();
            workbench.Session.Finish();
            workbench.Seek(26.5);
            workbench.ShowStates();
            yield return Frames(2);
            workbench.FinishStates();
            yield return Frames(30);

            Assert.IsTrue(workbench.StateMachine != null && workbench.StateMachine.States.Count == 8, "sanity: the machine found");
            yield return Capture("59-workbench-states-tab");
        }

        /// <summary>Part 15h: ABOUT BITSORTER, from HELP -- what the simulator is and does, and where it came from.</summary>
        [UnityTest]
        public IEnumerator Shot60_AboutBitSorter()
        {
            yield return TestScene.LoadSimulator();
            yield return Frames(4);
            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            workbench.ShowAbout();
            yield return Frames(30);

            Assert.IsTrue(workbench.AboutShowing, "sanity: the page up");
            yield return Capture("60-about-bitsorter");
        }

        /// <summary>
        /// Part 15h-3: operator parts on MY CIRCUIT, wired from a, b and s -- an adder, a signed comparison, a shifter with its
        /// amount coming in on top, a reduction -- and a subtractor and a shifter turned a quarter, each wire coming in the way
        /// its pin faces.
        /// </summary>
        [UnityTest]
        public IEnumerator Shot61_WorkbenchOperatorParts()
        {
            yield return LoadTheGame();
            yield return Frames(2);

            yield return Frames(2);

            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            workbench.ShowSample(0);   // MY CIRCUIT, blank under SaveGuard
            yield return Frames(2);
            workbench.Pause();

            WorkbenchSession session = workbench.Session;
            int Add(BitSorter.Board.Part part, double x, double y, int turns = 0)
            {
                BitSorter.Board.EditResult added = session.Editor.Add(session.Module, part,
                    new BitSorter.Board.Placement(new BitSorter.Board.Point(x, y), turns));
                Assert.IsTrue(added.Done, added.Refusal);
                return added.Id;
            }

            void Wire(int from, string fromPin, int to, string toPin)
            {
                BitSorter.Board.EditResult wired = session.Connect(BitSorter.Board.Endpoint.At(from, fromPin), BitSorter.Board.Endpoint.At(to, toPin));
                Assert.IsTrue(wired.Done, wired.Refusal);
            }

            int a = Add(new BitSorter.Board.PortPart { Name = "a", Width = 4 }, 1, -2);
            int b = Add(new BitSorter.Board.PortPart { Name = "b", Width = 4 }, 1, -6);
            int s = Add(new BitSorter.Board.PortPart { Name = "s", Width = 2 }, 1, -11);
            int sum = Add(new BitSorter.Board.OperatorPart { Op = BitSorter.Board.OperatorKind.Add }, 8, -1.75);
            int less = Add(new BitSorter.Board.OperatorPart { Op = BitSorter.Board.OperatorKind.Less, Signed = true }, 8, -4.75);
            int shift = Add(new BitSorter.Board.OperatorPart { Op = BitSorter.Board.OperatorKind.ShiftLeft, BWidth = 2 }, 8, -9);
            int odd = Add(new BitSorter.Board.OperatorPart { Op = BitSorter.Board.OperatorKind.ReduceXor }, 8, -12);
            int diff = Add(new BitSorter.Board.OperatorPart { Op = BitSorter.Board.OperatorKind.Subtract }, 18.25, -3, turns: 3);
            int right = Add(new BitSorter.Board.OperatorPart { Op = BitSorter.Board.OperatorKind.ShiftRight, BWidth = 2 }, 18, -10, turns: 1);
            int y = Add(new BitSorter.Board.PortPart { Name = "y", Direction = BitSorter.SystemVerilog.Syntax.PortDirection.Output, Width = 4 }, 14, -1.75);
            int lt = Add(new BitSorter.Board.PortPart { Name = "lt", Direction = BitSorter.SystemVerilog.Syntax.PortDirection.Output }, 14, -4.75);
            int sh = Add(new BitSorter.Board.PortPart { Name = "sh", Direction = BitSorter.SystemVerilog.Syntax.PortDirection.Output, Width = 4 }, 14, -9);
            int p = Add(new BitSorter.Board.PortPart { Name = "p", Direction = BitSorter.SystemVerilog.Syntax.PortDirection.Output }, 14, -12);
            int d = Add(new BitSorter.Board.PortPart { Name = "d", Direction = BitSorter.SystemVerilog.Syntax.PortDirection.Output, Width = 4 }, 24, -6);
            int r = Add(new BitSorter.Board.PortPart { Name = "r", Direction = BitSorter.SystemVerilog.Syntax.PortDirection.Output, Width = 4 }, 24, -13);

            Wire(a, "out", sum, "a");
            Wire(b, "out", sum, "b");
            Wire(a, "out", less, "a");
            Wire(b, "out", less, "b");
            Wire(a, "out", shift, "a");
            Wire(s, "out", shift, "b");
            Wire(b, "out", odd, "a");
            Wire(sum, "y", y, "in");
            Wire(less, "y", lt, "in");
            Wire(shift, "y", sh, "in");
            Wire(odd, "y", p, "in");
            Wire(sum, "y", diff, "a");
            Wire(b, "out", diff, "b");
            Wire(diff, "y", d, "in");
            Wire(shift, "y", right, "a");
            Wire(s, "out", right, "b");
            Wire(right, "y", r, "in");
            session.Finish();
            yield return Frames(2);
            workbench.FitView();
            workbench.Seek(0.0);
            yield return Frames(30);

            Assert.AreEqual(6, workbench.Model.Layout.Parts.Count(part => part.Symbol == PartSymbol.Operator), "sanity: six operators drawn");
            yield return Capture("61-workbench-operator-parts");
        }

        /// <summary>
        /// Part 15h-3: the full adder with h1 turned a quarter -- its pins on its top and bottom edges, each named along its pin
        /// inside the box, and its wires coming in from above and below as its pins face.
        /// </summary>
        [UnityTest]
        public IEnumerator Shot62_WorkbenchTurnedBlock()
        {
            yield return LoadTheGame();
            yield return Frames(2);

            yield return Frames(2);

            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            workbench.ShowSample(2);   // the full adder
            yield return Frames(2);
            workbench.Pause();
            WorkbenchSession session = workbench.Session;
            int h1 = session.Project.Find(session.Module).Parts.Single(part => part.Name == "h1").Id;
            BitSorter.Board.EditResult turned = session.Turn(new[] { h1 }, 1);
            Assert.IsTrue(turned.Done, turned.Refusal);
            session.Finish();
            yield return Frames(2);
            workbench.FitView();
            workbench.Seek(26.5);
            yield return Frames(30);

            yield return Capture("62-workbench-turned-block");
        }

        /// <summary>
        /// Part 15h-4: written code shown as the drawing of what it builds -- the written adder's block, its code made a small
        /// ALU, a case on a's top bits choosing a + b, a - b, a &amp; b or a | b: its operators, the muxes the case is, and the
        /// run's values on every wire, those inside the expressions included.
        /// </summary>
        [UnityTest]
        public IEnumerator Shot63_WorkbenchDrawingOfCode()
        {
            yield return LoadTheGame();
            yield return Frames(2);

            yield return Frames(2);

            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            int written = -1;
            for (int i = 0; i < WorkbenchSamples.Chip.Count; i++)
                if (WorkbenchSamples.Chip[i].Name == "Written adder")
                    written = i;
            workbench.ShowSample(written);
            yield return Frames(2);
            workbench.Pause();
            int add = workbench.Session.Project.TopModule.Parts.First(p => p is BitSorter.Board.BlockPart).Id;
            Assert.IsTrue(workbench.StepInto(add));
            yield return Frames(2);

            BitSorter.Board.EditResult applied = workbench.Session.SetSource(DrawnAlu);
            Assert.IsTrue(applied.Done, applied.Refusal);
            workbench.Session.Finish();   // a written block opens as its drawing (Part 15h-4d)
            yield return Frames(2);
            workbench.FitView();
            workbench.Seek(4 * WorkbenchSamples.WrittenAdderStep + 4.5);
            yield return Frames(30);

            Assert.IsTrue(workbench.Session.ShowsDrawingOfCode, "sanity: the drawing");
            yield return Capture("63-workbench-drawing-of-code");
        }

        /// <summary>
        /// Part 15h-4: the written adder's own code, <c>{cout, s} = a + b</c>, with GATES -- the textbook's 4-bit ripple, a half
        /// adder and three full adders, its carry on top -- every wire inside it valued by its gates at the last vector, 3 + F.
        /// </summary>
        [UnityTest]
        public IEnumerator Shot64_WorkbenchDrawingOfCodeWithGates()
        {
            yield return LoadTheGame();
            yield return Frames(2);

            yield return Frames(2);

            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            int written = -1;
            for (int i = 0; i < WorkbenchSamples.Chip.Count; i++)
                if (WorkbenchSamples.Chip[i].Name == "Written adder")
                    written = i;
            workbench.ShowSample(written);
            yield return Frames(2);
            workbench.Pause();
            int add = workbench.Session.Project.TopModule.Parts.First(p => p is BitSorter.Board.BlockPart).Id;
            Assert.IsTrue(workbench.StepInto(add));
            yield return Frames(2);

            workbench.Session.Finish();   // a written block opens as its drawing (Part 15h-4d)
            yield return Frames(2);
            workbench.GatesSwitch.onClick.Invoke();
            workbench.Session.Finish();
            yield return Frames(2);
            workbench.FitView();
            workbench.Seek(4 * WorkbenchSamples.WrittenAdderStep + 4.5);
            yield return Frames(30);

            Assert.IsTrue(workbench.Session.ShowsGatesOfCode, "sanity: the gates");
            Assert.AreEqual(1, workbench.Session.GatesOpened, "the sum and its carry");
            yield return Capture("64-workbench-drawing-with-gates");
        }

        /// <summary>The written adder's block made a small ALU for the drawing of code's shot: its ports as they were.</summary>
        /// <summary>
        /// Part 15h-5: the drawing of code and its code linked -- shot 63's small ALU with its subtracter selected on the
        /// drawing, and the line it comes from marked in the dock's CODE tab, numbered as the code's editor numbers it.
        /// </summary>
        [UnityTest]
        public IEnumerator Shot73_WorkbenchDrawingAndCodeLinked()
        {
            yield return LoadTheGame();
            yield return Frames(2);

            yield return Frames(2);

            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            int written = -1;
            for (int i = 0; i < WorkbenchSamples.Chip.Count; i++)
                if (WorkbenchSamples.Chip[i].Name == "Written adder")
                    written = i;
            workbench.ShowSample(written);
            yield return Frames(2);
            workbench.Pause();
            int add = workbench.Session.Project.TopModule.Parts.First(p => p is BitSorter.Board.BlockPart).Id;
            Assert.IsTrue(workbench.StepInto(add));
            yield return Frames(2);

            BitSorter.Board.EditResult applied = workbench.Session.SetSource(DrawnAlu);
            Assert.IsTrue(applied.Done, applied.Refusal);
            workbench.Session.Finish();
            yield return Frames(2);
            workbench.ShowCode();
            workbench.FitView();
            workbench.Seek(4 * WorkbenchSamples.WrittenAdderStep + 4.5);
            yield return Frames(2);

            BitSorter.Board.OperatorPart minus = workbench.Session.DrawnModule.Parts.OfType<BitSorter.Board.OperatorPart>()
                .Single(p => p.Op == BitSorter.Board.OperatorKind.Subtract);
            workbench.Editor.Selection.Pick(new CanvasTarget(CanvasTargetKind.Part, part: minus.Id), extend: false, toggle: false);
            yield return Frames(30);

            Assert.AreEqual(1, workbench.ListingView.Marked.Count, "sanity: its line marked");
            yield return Capture("73-workbench-drawing-and-code-linked");
        }

        /// <summary>
        /// Part 15h-6c: the pipelined processor opened from the start page's RISC-V PROCESSORS on Fibonacci, its PROGRAM chip's
        /// menu open over the run, the three programs listed and the one running marked.
        /// </summary>
        [UnityTest]
        public IEnumerator Shot74_WorkbenchProgramChooser()
        {
            yield return LoadTheGame();
            yield return Frames(2);

            yield return Frames(2);

            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            int index = -1;
            for (int i = 0; i < WorkbenchSamples.Chip.Count; i++)
                if (WorkbenchSamples.Chip[i].Name == "Pipelined RISC-V")
                    index = i;
            workbench.OpenProcessor(index, "FIBONACCI");
            yield return Frames(2);
            workbench.Pause();
            workbench.Session.Finish();
            workbench.FitView();
            workbench.Seek(450);
            yield return Frames(30);
            workbench.OpenChoiceMenu();
            yield return Frames(30);

            Assert.IsTrue(workbench.ChoiceMenu.IsOpen, "sanity: the menu open");
            yield return Capture("74-workbench-program-chooser");
        }

        /// <summary>
        /// Part 16: the full adder's h1 made four cells wide and three tall by its handles and selected -- the eight handles on
        /// its selection's outline, its pins spread down its sides, its port names given the room and its wires run to them.
        /// </summary>
        [UnityTest]
        public IEnumerator Shot75_WorkbenchResizedBlock()
        {
            yield return LoadTheGame();
            yield return Frames(2);

            yield return Frames(2);

            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            workbench.ShowSample(2);   // the full adder
            yield return Frames(2);
            workbench.Pause();
            workbench.Session.Finish();
            BitSorter.Board.Module top = workbench.Session.Project.TopModule;
            int h1 = top.FindPart("h1").Id;
            BitSorter.Board.Placement was = top.Layout.PlacementOf(h1);
            Assert.IsTrue(workbench.Session.Move(new System.Collections.Generic.Dictionary<int, BitSorter.Board.Placement>
            {
                [h1] = was.Moved(0.5, 0).WithSize(4, 3),   // its left side where it was, clear of h2 and of cin's wire
            }).Done, "sanity: resized");
            workbench.Session.Finish();
            yield return Frames(2);
            workbench.Editor.Do(new CanvasCommand(CanvasGesture.Select, target: new CanvasTarget(CanvasTargetKind.Part, part: h1)));   // as a click selects
            workbench.FitView();
            workbench.Seek(26.5);
            yield return Frames(30);

            Assert.AreEqual(h1, workbench.Editor.HandledPart, "sanity: its handles up");
            yield return Capture("75-workbench-resized-block");
        }

        /// <summary>
        /// Part 16d: the reference circuit with all three panels folded away -- PARTS, the side panel and the dock each a strip
        /// at the window's edge holding its arrow, and the circuit fitted to the room they gave it.
        /// </summary>
        [UnityTest]
        public IEnumerator Shot76_WorkbenchPanelsFolded()
        {
            yield return LoadTheGame();
            yield return Frames(2);

            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            workbench.Pause();
            workbench.Seek(13.5);
            workbench.TogglePanels();
            yield return Frames(30);

            Assert.IsTrue(workbench.Folds.All, "sanity: all three folded");
            Assert.IsTrue(workbench.IsFitted, "sanity: the circuit fitted to its window");
            yield return Capture("76-workbench-panels-folded");

            // Opened again: the folds are kept between launches, so a shot after this one would find them folded.
            workbench.TogglePanels();
            yield return Frames(2);
            Assert.IsFalse(workbench.Folds.All, "the panels open again for the shots after");
        }

        /// <summary>
        /// 5.1: code folded -- the written adder's port list folded to one line from the margin's marker, the margin's numbers
        /// passing over what it hides and its marker a +, and the code in its colours.
        /// </summary>
        [UnityTest]
        public IEnumerator Shot77_WorkbenchCodeFolded()
        {
            yield return LoadTheGame();
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
            int add = workbench.Session.Project.TopModule.Parts.First(p => p is BitSorter.Board.BlockPart).Id;
            Assert.IsTrue(workbench.StepInto(add));
            workbench.Session.DrawCode(false);
            yield return Frames(2);

            string source = WorkbenchSamples.AdderSource;
            int ports = source.Substring(0, source.IndexOf(") (", System.StringComparison.Ordinal)).Count(c => c == '\n');
            workbench.CodeView.ToggleFold(ports);   // the port list's ( on the line that closes the parameters
            yield return Frames(30);

            Assert.IsTrue(workbench.CodeView.Folding.IsFolded(ports), "sanity: the port list folded");
            yield return Capture("77-workbench-code-folded");
        }

        /// <summary>
        /// 5.1: QUIT at the window's bottom right, at the status line's right end, asking first -- its question above it, never
        /// under the pointer, so a double-click cannot answer it.
        /// </summary>
        [UnityTest]
        public IEnumerator Shot78_WorkbenchQuitAsking()
        {
            yield return LoadTheGame();
            yield return Frames(2);

            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            workbench.Pause();
            workbench.Seek(13.5);
            yield return Frames(2);
            workbench.AskToQuit();
            yield return Frames(30);

            Assert.IsTrue(workbench.Ask.IsOpen, "sanity: QUIT asking");
            Assert.AreEqual(WorkbenchPanel.QuitQuestion, workbench.Ask.Question);
            yield return Capture("78-workbench-quit-asking");
        }

        /// <summary>
        /// 5.1: a row dragged by its name -- the reference's last row held over its second, the line showing where it would
        /// go, and the row selected as the press selected it.
        /// </summary>
        [UnityTest]
        public IEnumerator Shot79_WorkbenchRowDragged()
        {
            yield return LoadTheGame();
            yield return Frames(2);

            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            workbench.Pause();
            workbench.Seek(13.5);
            yield return Frames(2);

            int last = workbench.List.Lines.Count - 1;
            Mouse mouse = InputSystem.GetDevice<Mouse>();
            mouse.MakeCurrent();
            Vector2 from = workbench.RowNameOnScreen(last);
            Vector2 up = workbench.RowNameOnScreen(0) - workbench.RowNameOnScreen(1);
            Vector2 to = workbench.RowNameOnScreen(1) + up * 0.4f;   // just over the second row
            Set(mouse.position, from);
            yield return Frames(2);
            Press(mouse.leftButton);
            yield return Frames(2);
            for (int step = 1; step <= 4; step++)
            {
                Set(mouse.position, Vector2.Lerp(from, to, step / 4f));
                yield return Frames(1);
            }

            yield return Frames(30);
            Assert.IsTrue(workbench.DraggingARow, "sanity: a row held by its name");
            Assert.AreEqual(1, workbench.RowDropShown, "sanity: the line over the second row");
            yield return Capture("79-workbench-row-dragged");

            Release(mouse.leftButton);
            yield return Frames(2);
        }

        private const string DrawnAlu =
            "module add4 #(\n" +
            "    parameter N = 4\n" +
            ") (\n" +
            "    input  logic [N-1:0] a,\n" +
            "    input  logic [N-1:0] b,\n" +
            "    output logic [N-1:0] s,\n" +
            "    output logic         cout\n" +
            ");\n" +
            "    always_comb\n" +
            "        case (a[3:2])\n" +
            "            2'b00:   {cout, s} = a + b;\n" +
            "            2'b01:   {cout, s} = a - b;\n" +
            "            2'b10:   {cout, s} = {1'b0, a & b};\n" +
            "            default: {cout, s} = {1'b0, a | b};\n" +
            "        endcase\n" +
            "endmodule\n";

        private static IEnumerator OpenTheHalfAdder()
        {
            yield return TestScene.LoadSimulator();
            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            workbench.OpenExercise("half-adder");
            yield return Frames(2);
            workbench.Pause();
            workbench.Seek(25);
        }

        /// <summary>The half adder's sum, an XOR, and with <paramref name="withCarry"/> its carry, an AND, on the exercise open.</summary>
        private static void BuildHalfOfTheHalfAdder(WorkbenchSession session, bool withCarry)
        {
            int Place(string key, double x, double y)
            {
                BitSorter.Board.EditResult placed = session.Place(PartsCatalog.Find(session.Project, session.Module, key), new BitSorter.Board.Placement(x, y));
                Assert.IsTrue(placed.Done, placed.Refusal);
                return placed.Id;
            }

            int Port(string name) => session.Project.TopModule.Ports.Single(p => p.Name == name).Id;

            void Wire(int from, string fromPin, int to, string toPin) =>
                Assert.IsTrue(session.Connect(BitSorter.Board.Endpoint.At(from, fromPin), BitSorter.Board.Endpoint.At(to, toPin)).Done);

            int xor = Place("xor", 8, -2);
            Wire(Port("a"), "out", xor, "a");
            Wire(Port("b"), "out", xor, "b");
            Wire(xor, "y", Port("sum"), "in");
            if (withCarry)
            {
                int and = Place("and", 8, 2);
                Wire(Port("a"), "out", and, "a");
                Wire(Port("b"), "out", and, "b");
                Wire(and, "y", Port("carry"), "in");
            }
            session.Finish();
        }

        // -----------------------------------------------------------------
        // Time
        // -----------------------------------------------------------------

        private static IEnumerator Frames(int count)
        {
            for (int i = 0; i < count; i++)
                yield return null;
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
    }
}
