using System.Collections;
using System.Linq;
using BitSorter.View;
using BitSorter.ViewModel;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Button = UnityEngine.UIElements.Button;
using Object = UnityEngine.Object;

namespace BitSorter.PlayMode.Tests
{
    /// <summary>
    /// The workbench's docked layout in the real game (Part 11b): the shell's panels frame the drawings under them --
    /// the circuit's window and the waves are exactly where their panels are -- the splitter dragged with the fixture's
    /// own mouse makes the dock taller and the circuit shorter and is kept, the wheel over the waves' names scrolls rows
    /// a short dock cannot show, the waveform's buttons and the legend stay inside the dock, and the parts panel offers
    /// every part under its heading, a click taking one in hand. Where each panel goes is held under dotnet
    /// (<c>ShellLayoutTests</c>); these hold that the drawings follow it.
    /// </summary>
    [TestFixture]
    public class WorkbenchDockPlayTests : InputTestFixture
    {
        private Mouse _mouse;
        private Keyboard _keyboard;

        [OneTimeSetUp]
        public void OneTimeSetup()
        {
            SaveGuard.Redirect();
            GameAnalytics.SetReporting(false);
        }

        [OneTimeTearDown]
        public void OneTimeCleanup() => SaveGuard.Release();

        public override void Setup()
        {
            base.Setup();
            InputSystem.settings.SetInternalFeatureFlag("USE_READ_VALUE_CACHING", false);
            _keyboard = InputSystem.AddDevice<Keyboard>();
            _mouse = InputSystem.AddDevice<Mouse>();
            _mouse.MakeCurrent();
        }

        public override void TearDown()
        {
            SaveGuard.Clear();
            base.TearDown();
        }

        [UnityTearDown]
        public IEnumerator ClearTheScene()
        {
            // A dragged splitter is kept in the fixture's own settings: the next test opens on the dock's own height.
            Preferences.Redirected?.Remove(WorkbenchPanel.DockKey);
            yield return TestScene.Clear();
        }

        private static T Find<T>() where T : Object => Object.FindFirstObjectByType<T>();

        /// <summary>The workbench open on the reference circuit, paused, its run made.</summary>
        private static IEnumerator OpenTheReference()
        {
            yield return TestScene.Load();
            GameObject.Find(MainMenu.WorkbenchButton).GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
            yield return null;
            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            Assert.IsTrue(workbench.IsOpen);
            workbench.ShowSample(1);
            yield return null;
            workbench.Pause();
            workbench.Session.Finish();
            yield return null;
            yield return null;
        }

        private static Rect WorldRect(string name)
        {
            RectTransform rect = GameObject.Find(WorkbenchPanel.RootName).GetComponentsInChildren<RectTransform>().Single(r => r.name == name);
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            return Rect.MinMaxRect(corners[0].x, corners[0].y, corners[2].x, corners[2].y);
        }

        private static Rect PanelRect(string name) => ShellPanel.ScreenRectOf(ShellPanel.Find<VisualElement>(name));

        /// <summary>How many pixels of the screen a unit of the shell is.</summary>
        private static float Scale => ShellPanel.Find<VisualElement>(WorkbenchPanel.DockName).panel.scaledPixelsPerPoint;

        private static void AssertSame(Rect expected, Rect actual, string what)
        {
            Assert.AreEqual(expected.xMin, actual.xMin, 1f, what + ": its left");
            Assert.AreEqual(expected.xMax, actual.xMax, 1f, what + ": its right");
            Assert.AreEqual(expected.yMin, actual.yMin, 1f, what + ": its foot");
            Assert.AreEqual(expected.yMax, actual.yMax, 1f, what + ": its top");
        }

        private static void AssertInside(Rect outer, Rect inner, string what)
        {
            Assert.IsTrue(inner.xMin >= outer.xMin - 0.5f && inner.xMax <= outer.xMax + 0.5f && inner.yMin >= outer.yMin - 0.5f &&
                          inner.yMax <= outer.yMax + 0.5f, $"{what}, {inner}, is not inside {outer}");
        }

        [UnityTest]
        public IEnumerator TheCircuitsWindowAndTheWaves_AreWhereTheirPanelsAre()
        {
            yield return OpenTheReference();
            WorkbenchPanel workbench = Find<WorkbenchPanel>();

            AssertSame(PanelRect(WorkbenchPanel.CircuitPanelName), WorldRect("schematic view"), "the circuit's window");

            Rect dock = PanelRect(WorkbenchPanel.DockName);
            float header = ShellLayout.DockHeaderHeight * Scale, legend = ShellLayout.LegendHeight * Scale;
            AssertSame(Rect.MinMaxRect(dock.xMin, dock.yMin + legend, dock.xMax, dock.yMax - header), WorldRect("waves"), "the waves");
            AssertSame(Rect.MinMaxRect(dock.xMin, dock.yMin, dock.xMax, dock.yMin + legend), WorldRect("legend"), "the legend");

            Rect parts = PanelRect(WorkbenchPanel.PartsPanelName), side = PanelRect(WorkbenchPanel.SidePanelName);
            Rect circuit = PanelRect(WorkbenchPanel.CircuitPanelName);
            Assert.IsFalse(parts.Overlaps(circuit) || side.Overlaps(circuit), "the side panels stand beside the circuit, never on it");
            Assert.IsTrue(workbench.IsFitted, "and the circuit opens fitted to its window");
        }

        [UnityTest]
        public IEnumerator TheSplitter_DraggedUp_MakesTheDockTaller_TheCircuitShorter_AndIsKept()
        {
            yield return OpenTheReference();
            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            float dock = workbench.Regions.Dock.height, circuit = workbench.Regions.Circuit.height;
            Rect windowBefore = WorldRect("schematic view");

            Vector2 at = ShellPanel.ScreenPointOf(ShellPanel.Find<VisualElement>(WorkbenchPanel.SplitterName));
            Move(_mouse.position, at);
            yield return null;
            Assert.IsTrue(ShellPanel.OverControlAt(at), "the splitter is a control: a press on it is not the circuit's");
            Press(_mouse.leftButton);
            yield return null;
            for (int step = 1; step <= 4; step++)
            {
                Move(_mouse.position, at + new Vector2(0f, 25f * step * Scale));
                yield return null;
            }

            Release(_mouse.leftButton);
            yield return null;
            yield return null;

            Assert.AreEqual(dock + 100f, workbench.Regions.Dock.height, 1f, "the dock's top followed the pointer up");
            Assert.AreEqual(circuit - 100f, workbench.Regions.Circuit.height, 1f, "and the circuit gave it the room");
            AssertSame(PanelRect(WorkbenchPanel.CircuitPanelName), WorldRect("schematic view"), "the circuit's window, after");
            Assert.Less(WorldRect("schematic view").height, windowBefore.height, "sanity: it is shorter");
            Assert.IsTrue(workbench.IsFitted, "a circuit fitted to its window is fitted to the new one");
            Assert.AreEqual(Mathf.RoundToInt(workbench.Regions.Dock.height), Preferences.GetInt(WorkbenchPanel.DockKey, -1),
                "the height is kept for next time");
            Assert.AreEqual(0, workbench.Editor.Selection.Parts.Count, "and the drag selected nothing on the circuit");
        }

        [UnityTest]
        public IEnumerator InAShortDock_TheWheelOverTheNames_ScrollsTheRowsItCannotShow()
        {
            yield return OpenTheReference();
            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            workbench.SetDockHeight(0f);
            yield return null;
            Assert.AreEqual(ShellLayout.MinDock, workbench.Regions.Dock.height, 1e-3f, "sanity: the shortest dock there is");
            Assert.Greater(workbench.WavesOverflow, 0f, "sanity: more rows than it shows");

            Rect waves = WorldRect("waves");
            var names = new Vector2(waves.xMin + 30f * Scale, waves.center.y);
            Vector2 rowBefore = workbench.RowNameOnScreen(0);

            Assert.IsTrue(workbench.TakeScroll(names, new Vector2(0f, -120f)), "the wheel down over the names scrolls the rows");
            float row = workbench.WavesScroll;
            Assert.Greater(row, 0f, "a row a notch");
            Assert.AreEqual(rowBefore.y + row * Scale, workbench.RowNameOnScreen(0).y, 1f, "the rows moved up by it");

            for (int i = 0; i < 50; i++)
                workbench.TakeScroll(names, new Vector2(0f, -120f));
            Assert.AreEqual(workbench.WavesOverflow, workbench.WavesScroll, 1e-3f, "as far as the last row and no further");

            Assert.IsTrue(workbench.TakeScroll(names, new Vector2(0f, 120f)), "and up scrolls back");
            Assert.IsFalse(workbench.TakeScroll(PanelRect(WorkbenchPanel.CircuitPanelName).center, new Vector2(0f, -120f)),
                "the wheel over the circuit is the circuit's");
        }

        [UnityTest]
        public IEnumerator TheWaveformsButtons_AndTheLegend_StayInsideTheDock()
        {
            yield return OpenTheReference();
            Rect dock = PanelRect(WorkbenchPanel.DockName);

            foreach (string name in new[] { WaveformView.AddButton, WaveformView.GroupButton, WaveformView.NamesButton, WaveformView.UnitButton,
                         WaveformView.ExportButton })
            {
                Button button = ShellPanel.Find<Button>(name);
                Assert.IsNotNull(button, $"no '{name}' in the dock");
                AssertInside(dock, ShellPanel.ScreenRectOf(button), name);
            }

            TMP_Text legend = GameObject.Find(WorkbenchPanel.RootName).GetComponentsInChildren<TMP_Text>().First(t => t.text == WorkbenchPanel.LegendText);
            Assert.LessOrEqual(legend.preferredWidth, legend.rectTransform.rect.width + 0.5f, "the legend's sentence runs past its box");
            Assert.AreEqual("UNIT: " + Find<WorkbenchPanel>().Model.Unit, ShellPanel.Find<Button>(WaveformView.UnitButton).text);
        }

        [UnityTest]
        public IEnumerator ThePartsPanel_OffersEveryPart_InsideIt_AndAClickTakesOneInHand()
        {
            yield return OpenTheReference();
            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            Rect panel = PanelRect(WorkbenchPanel.PartsPanelName);

            foreach (CatalogEntry entry in PartsCatalog.For(workbench.Session.Project, workbench.Session.Module))
            {
                Button button = ShellPanel.Find<Button>(WorkbenchPanel.PartButtonName(entry.Key));
                Assert.IsNotNull(button, $"no button for {entry.Key}");
                AssertInside(panel, ShellPanel.ScreenRectOf(button), entry.Key);
            }

            Button and = ShellPanel.Find<Button>(WorkbenchPanel.PartButtonName("and"));
            Move(_mouse.position, ShellPanel.ScreenPointOf(and));
            yield return null;
            Press(_mouse.leftButton);
            yield return null;
            Release(_mouse.leftButton);
            yield return null;
            yield return null;

            Assert.AreEqual("and", workbench.Editor.InHand?.Key, "a click on AND took it in hand");
            Assert.AreEqual(SchematicPalette.Current.Accent, and.resolvedStyle.color, "and its button says so");
        }

        [UnityTest]
        public IEnumerator APartsPanelShorterThanItsEntries_ScrollsThemUnderTheWheel()
        {
            yield return OpenTheReference();
            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            Assert.AreEqual(0f, workbench.PartsOverflow, "sanity: at its own height the panel shows every part");

            workbench.SetDockHeight(5000f);
            yield return null;
            Assert.Greater(workbench.PartsOverflow, 0f, "sanity: the tallest dock leaves the panel shorter than its parts");

            float zoom = workbench.Zoom;
            Move(_mouse.position, PanelRect(WorkbenchPanel.PartsPanelName).center);
            yield return null;
            Set(_mouse.scroll, new Vector2(0f, -120f));
            yield return null;
            yield return null;
            Assert.Greater(workbench.PartsScroll, 0f, "the wheel down over the parts scrolled them");
            Assert.AreEqual(zoom, workbench.Zoom, "and nothing else: the circuit did not zoom");
        }

        // -----------------------------------------------------------------
        // The transcript and the hierarchy (Part 11c)
        // -----------------------------------------------------------------

        /// <summary>A click with the fixture's own mouse on a shell element called <paramref name="name"/>.</summary>
        private IEnumerator ClickOn(string name)
        {
            VisualElement element = ShellPanel.Find<VisualElement>(name);
            Assert.IsNotNull(element, $"no '{name}' in the shell");
            Move(_mouse.position, ShellPanel.ScreenPointOf(element));
            yield return null;
            Press(_mouse.leftButton);
            yield return null;
            Release(_mouse.leftButton);
            yield return null;
            yield return null;
        }

        /// <summary>
        /// A lone AND on MY CIRCUIT: the transcript lists what the document says of its pins not connected, each line
        /// a warning in a word, and a click on one selects the AND, which the inspector then shows.
        /// </summary>
        [UnityTest]
        public IEnumerator TheTranscript_ListsWhatTheDocumentSays_AndAClickOnALineSelectsItsPart()
        {
            yield return OpenTheReference();
            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            workbench.ShowSample(0);
            yield return null;
            WorkbenchSession session = workbench.Session;
            BitSorter.Board.EditResult placed = session.Place(PartsCatalog.Find(session.Project, session.Module, "and"),
                new BitSorter.Board.Placement(3, -2));
            Assert.IsTrue(placed.Done, placed.Refusal);
            yield return null;
            yield return null;

            yield return ClickOn(WorkbenchPanel.TranscriptTabName);
            Assert.IsTrue(workbench.TranscriptShown, "a click on the tab shows the transcript");
            Assert.IsNotEmpty(workbench.Transcript.Lines);
            TranscriptLine first = workbench.Transcript.Lines[0];
            Assert.AreEqual(TranscriptSeverity.Warning, first.Severity, "the AND's open input is a warning: " + first.Text);
            StringAssert.Contains("not connected", first.Text);
            Assert.AreEqual(placed.Id, first.Part, "about the AND");
            Assert.AreEqual("WARNING", UnityEngine.UIElements.UQueryExtensions.Q<Label>(
                ShellPanel.Find<VisualElement>(WorkbenchPanel.TranscriptRowName + "0"), "severity").text, "said in a word on its row");

            yield return ClickOn(WorkbenchPanel.TranscriptRowName + "0");
            Assert.IsTrue(workbench.Editor.Selection.HasPart(placed.Id), "a click on the line selects the AND");
            yield return null;
            StringAssert.StartsWith("AND ", workbench.InspectorTitle, "and the inspector shows it");

            yield return ClickOn(WorkbenchPanel.TranscriptFilterName + TranscriptFilter.Errors);
            Assert.IsEmpty(workbench.Transcript.Lines, "ERRORS: there are none");
            yield return ClickOn(WorkbenchPanel.WavesTabName);
            Assert.IsFalse(workbench.TranscriptShown, "and WAVES puts the waves back");
        }

        /// <summary>The reference's run stops on its testbench's $stop: the transcript says so at that moment, and a click on the line seeks there.</summary>
        [UnityTest]
        public IEnumerator AClickOnAMessage_SeeksToItsMoment()
        {
            yield return OpenTheReference();
            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            workbench.Seek(0.0);
            workbench.ShowTranscript();
            yield return null;
            yield return null;

            int line = -1;
            for (int i = 0; i < workbench.Transcript.Lines.Count; i++)
                if (workbench.Transcript.Lines[i].IsMessage)
                {
                    line = i;
                    break;
                }

            Assert.GreaterOrEqual(line, 0, "sanity: the run said something -- its $stop");
            long when = workbench.Transcript.Lines[line].Time;
            Assert.Greater(when, 0L);

            yield return ClickOn(WorkbenchPanel.TranscriptRowName + line);
            Assert.AreEqual(when, workbench.Time, 1e-9, "the click sought to the moment the message was said");
            Assert.IsFalse(workbench.Playing, "and left it paused there");
        }

        /// <summary>
        /// The full adder's hierarchy in the DESIGN tab: its two half adders under the design; a click on h1 adds h1's four
        /// ports to the waveform as one group called h1, and - shuts the design's row.
        /// </summary>
        [UnityTest]
        public IEnumerator AClickOnABlockInTheHierarchy_AddsItsPortsAsAGroup()
        {
            yield return OpenTheReference();
            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            int fullAdder = -1;
            for (int i = 0; i < WorkbenchSamples.Chip.Count; i++)
                if (WorkbenchSamples.Chip[i].Name.ToLowerInvariant().Contains("full adder"))
                    fullAdder = i;
            Assert.GreaterOrEqual(fullAdder, 0, "sanity: the full adder is a sample");
            workbench.ShowSample(fullAdder);
            workbench.Pause();
            workbench.Session.Finish();
            yield return null;
            workbench.ShowTranscript();
            yield return ClickOn(WorkbenchPanel.DesignTabName);
            yield return null;

            HierarchyTree tree = workbench.Hierarchy;
            Assert.IsNotNull(tree);
            int h1 = -1;
            for (int i = 0; i < tree.Rows.Count; i++)
                if (tree.Rows[i].Label == "h1 (half_adder)")
                    h1 = i;
            Assert.GreaterOrEqual(h1, 0, "h1 is in the tree: " + string.Join(", ", tree.Rows.Select(r => r.Label)));

            yield return ClickOn(WorkbenchPanel.ScopeRowName + h1);
            WaveGroup group = workbench.List.Lines.Where(l => l.IsHeader).Select(l => l.Group).FirstOrDefault(g => g.Name == "h1");
            Assert.IsNotNull(group, "a group called h1");
            Assert.AreEqual(4, group.Signals.Count, "of its four ports");
            Assert.IsFalse(workbench.TranscriptShown, "and the dock shows the waves it was added to");

            int design = tree.Rows.ToList().FindIndex(r => r.Node.Kind == ScopeNodeKind.Design);
            yield return ClickOn(WorkbenchPanel.ScopeToggleName + design);
            Assert.IsFalse(workbench.Hierarchy.Rows.Any(r => r.Node.Kind == ScopeNodeKind.Block), "- shut the design's row");
        }

        // -----------------------------------------------------------------
        // The keyboard map and the status line (Part 11d)
        // -----------------------------------------------------------------

        private IEnumerator Tap(UnityEngine.InputSystem.Controls.KeyControl key)
        {
            Press(key);
            yield return null;
            Release(key);
            yield return null;
        }

        /// <summary>? opens the keyboard map, listing every key; Escape shuts it and leaves the workbench up; ? again opens and shuts it.</summary>
        [UnityTest]
        public IEnumerator QuestionMark_OpensTheKeyboardMap_AndEscapeShutsItBeforeTheWorkbench()
        {
            yield return OpenTheReference();
            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            Assert.IsFalse(workbench.KeyMapPopup.IsOpen);

            yield return Tap(_keyboard.slashKey);
            Assert.IsTrue(workbench.KeyMapPopup.IsOpen, "? opens the map");
            VisualElement map = ShellPanel.Find<VisualElement>(WorkbenchKeyMap.RootName);
            foreach (KeyBinding binding in WorkbenchKeys.All)
                Assert.IsTrue(map.Query<Label>().ToList().Any(l => l.text == binding.Says), $"'{binding.Says}' is on the map");

            yield return Tap(_keyboard.escapeKey);
            Assert.IsFalse(workbench.KeyMapPopup.IsOpen, "Escape shuts it");
            Assert.IsTrue(workbench.IsOpen, "and only it");

            yield return Tap(_keyboard.slashKey);
            Assert.IsTrue(workbench.KeyMapPopup.IsOpen);
            yield return Tap(_keyboard.slashKey);
            Assert.IsFalse(workbench.KeyMapPopup.IsOpen, "? again shuts it");
        }

        /// <summary>SHORTCUT KEYS hidden: the workbench's caps beside its buttons go at once, and the keyboard map keeps its own.</summary>
        [UnityTest]
        public IEnumerator HiddenShortcutKeys_LeaveTheButtonsBare_AndTheKeyboardMapWhole()
        {
            yield return OpenTheReference();
            WorkbenchPanel workbench = Find<WorkbenchPanel>();
            VisualElement root = workbench.Shell.Root;
            Assert.IsTrue(root.Query(className: ShellTheme.KeyCapClass).ToList().Count > 5, "sanity: the toolbar and the dock have caps");

            try
            {
                KeyCaps.SetShown(false);
                yield return null;
                Assert.IsTrue(root.Query(className: ShellTheme.KeyCapClass).ToList().All(cap => !cap.visible), "a cap beside a button is still up");

                workbench.KeyMapPopup.Toggle();
                yield return null;
                VisualElement map = ShellPanel.Find<VisualElement>(WorkbenchKeyMap.RootName);
                Assert.IsTrue(map.Query<Label>(name: "key").ToList().Count > 10 && map.Query<Label>(name: "key").ToList().All(l => l.parent.visible),
                    "the keyboard map lists the keys whatever the setting");
                workbench.KeyMapPopup.Close();
            }
            finally
            {
                KeyCaps.SetShown(true);
            }

            yield return null;
            Assert.IsTrue(root.Query(className: ShellTheme.KeyCapClass).ToList().All(cap => cap.visible), "and back again");
        }

        /// <summary>The status line names the circuit's gestures over the circuit and the waves' over the waves, in one line.</summary>
        [UnityTest]
        public IEnumerator TheStatusLine_NamesTheGesturesWhereThePointerIs()
        {
            yield return OpenTheReference();
            WorkbenchPanel workbench = Find<WorkbenchPanel>();

            Move(_mouse.position, workbench.ViewMiddleOnScreen);
            yield return null;
            yield return null;
            Assert.AreEqual(KeyMap.StatusFor(PointerArea.Circuit), workbench.StatusText, "over the circuit");

            Move(_mouse.position, workbench.WaveScreenPointOf(5.0, 0));
            yield return null;
            yield return null;
            Assert.AreEqual(KeyMap.StatusFor(PointerArea.Waves), workbench.StatusText, "over the waves");

            Move(_mouse.position, PanelRect(WorkbenchPanel.SidePanelName).center);
            yield return null;
            yield return null;
            Assert.AreEqual(KeyMap.StatusFor(PointerArea.Elsewhere), workbench.StatusText, "and elsewhere, ? for every key");
        }
    }
}
