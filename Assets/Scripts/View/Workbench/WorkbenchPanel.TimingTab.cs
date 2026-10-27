using System.Collections.Generic;
using BitSorter.Board;
using BitSorter.ViewModel;
using UnityEngine;
using UnityEngine.UIElements;
using ShellButton = UnityEngine.UIElements.Button;

namespace BitSorter.View
{
    /// <summary>
    /// The dock's TIMING tab (Part 14b), beside CODE: the document's timing as the course reckons it (<see cref="TimingView"/>)
    /// -- the critical path, and for a design with registers the minimum period and the frequency it gives, the gate count,
    /// and every path from an input to an output, the slowest first -- worked out again as it is drawn. A path picked is lit on
    /// the canvas (<see cref="TimingLight"/>), the critical path until another is; leaving the tab puts the light out.
    /// </summary>
    /// <remarks>
    /// **The measured worst** (Part 14c, <see cref="StepMeasure"/>) is made while the tab is up, a slice a frame on a copy of
    /// the document, and laid on the computed timing once it is: the slowest seen beside the critical path, a line for each
    /// input and output, and every computed path no step ever fires marked so. An edit starts it again. **STEP TEST**, in the
    /// tab's header, makes the document's own stimulus a step test (<see cref="WorkbenchSession.StepTest"/>), one undo step,
    /// so each delay can be read off the waves with the cursors.
    /// </remarks>
    public sealed partial class WorkbenchPanel
    {
        /// <summary>The tab's name, and a line's: this, and its place on screen; STEP TEST's, and the line beside it.</summary>
        public const string TimingTabName = "Workbench timing tab", TimingRowName = "Workbench timing row ",
            StepTestName = "Workbench step test", TimingSaysName = "Workbench timing says", LibraryButtonName = "Workbench library button",
            BudgetFieldName = "Workbench gate budget";

        private const float TimingTabLeft = CodeTabLeft + CodeTabWidth + 4f, TimingTabWidth = 100f;

        private ShellButton _timingTabButton;
        private VisualElement _timingTools, _timingList;
        private Label _timingEmpty;
        private readonly List<VisualElement> _timingRows = new List<VisualElement>();
        private TimingView _timingShown, _timingBase;
        private ShellButton _libraryButton;
        private LibraryMenu _libraryMenu;
        private WaveRenameField _budgetField;
        private GateLibrary _libraryShown;
        private StepMeasure _measure;
        private string _measureWhy;
        private Label _timingSays;
        private int _timingSaysShown = int.MinValue;
        private TimingLine _timingPickedLine;
        private int _timingPicked = -1, _timingFirst;
        private bool _timingDrawn;

        /// <summary>Whether the dock shows the timing.</summary>
        public bool TimingShown => _dockTab == DockTab.Timing;

        /// <summary>The timing as the tab shows it; null until the tab has been shown.</summary>
        public TimingView Timing => _timingShown;

        /// <summary>The measured worst being made, or made, for the document shown; null when it is not measured.</summary>
        public StepMeasure Measuring => _measure;

        /// <summary>What the TIMING tab's header says: how far the measuring has got, or why it is not measured.</summary>
        public string TimingSays => _timingSays?.text;

        /// <summary>The line picked, lit on the canvas, by its index in <see cref="Timing"/>; -1 for none.</summary>
        public int TimingPicked => _timingPicked;

        /// <summary>Shows the timing, as its tab does.</summary>
        public void ShowTiming() => ShowDockTab(DockTab.Timing);

        /// <summary>The TIMING tab beside CODE, STEP TEST, and the line its header says how far the measuring has got on.</summary>
        private void BuildTimingTab(VisualElement header, SchematicPalette palette)
        {
            _timingTabButton = Tab(TimingTabName, "TIMING", palette, () => ShowDockTab(DockTab.Timing));
            ShellTheme.Place(_timingTabButton, TimingTabLeft, 6f, TimingTabWidth, TabHeight);
            header.Add(_timingTabButton);

            _timingTools = new VisualElement { name = "timing tools", pickingMode = PickingMode.Ignore };
            ShellTheme.Place(_timingTools, 0f, 0f, -1f, ShellLayout.DockHeaderHeight);
            _timingTools.style.right = 0f;
            header.Add(_timingTools);

            ShellButton step = ShellTheme.Button(StepTestName, "STEP TEST", palette, StepTest, UiType.Caption);
            ShellTheme.Place(step, ToolsLeft, 5f, 140f, 34f);
            _timingTools.Add(step);

            // What the design is held to (Part 14d), and the menu that changes it.
            _libraryButton = ShellTheme.Button(LibraryButtonName, LibraryCaption(null), palette, OpenLibraryMenu, UiType.Caption);
            ShellTheme.Place(_libraryButton, ToolsLeft + 152f, 5f, 260f, 34f);
            _timingTools.Add(_libraryButton);
            _libraryMenu = LibraryMenu.Build(_shell, transform, palette, SetLibrary, AskBudget);
            _budgetField = WaveRenameField.Build(_shell, transform, palette, SetBudget, BudgetFieldName, "GATE BUDGET",
                "a number of gates, or empty for none");

            _timingSays = ShellTheme.Label(TimingSaysName, "", UiType.Caption, palette.TextDim);
            ShellTheme.Place(_timingSays, ToolsLeft + 428f, 0f, -1f, ShellLayout.DockHeaderHeight);
            _timingSays.style.right = 14f;
            ShellTheme.Ellipsis(_timingSays);
            _timingTools.Add(_timingSays);
        }

        /// <summary>The LIBRARY menu, as its button opens it.</summary>
        public LibraryMenu LibraryMenu => _libraryMenu;

        /// <summary>The field GATE BUDGET asks the budget in.</summary>
        public WaveRenameField BudgetField => _budgetField;

        private static string LibraryCaption(GateLibrary library) => "LIBRARY: " + (library == null ? "NONE" : library.Name);

        /// <summary>Opens the LIBRARY menu under its button, as a click on it does.</summary>
        public void OpenLibraryMenu()
        {
            if (_session == null)
                return;
            _libraryMenu.Open(UnderTheLibraryButton(), _session.Project.Settings?.Library, _session.Project.Settings?.DefaultGateDelay ?? 1);
        }

        private Vector2 UnderTheLibraryButton()
        {
            Rect at = _libraryButton.worldBound;
            float scale = _libraryButton.panel != null ? _libraryButton.panel.scaledPixelsPerPoint : 1f;
            return new Vector2(at.xMin * scale, Screen.height - at.yMax * scale);
        }

        /// <summary>The design held to <paramref name="library"/>, or to none: one step, refused in the rules' words when what is drawn breaks it.</summary>
        public void SetLibrary(GateLibrary library)
        {
            if (_session != null)
                Said(_session.SetLibrary(library));
        }

        private void AskBudget()
        {
            GateLibrary library = _session?.Project.Settings?.Library;
            if (library == null)
                return;
            _budgetField.Open(library.GateBudget >= 0 ? library.GateBudget.ToString(System.Globalization.CultureInfo.InvariantCulture) : "", UnderTheLibraryButton());
        }

        /// <summary>The budget typed: a number of gates, or nothing for none.</summary>
        public void SetBudget(string text)
        {
            GateLibrary library = _session?.Project.Settings?.Library;
            if (library == null)
                return;
            text = (text ?? "").Trim();
            if (text.Length == 0)
            {
                SetLibrary(library with { GateBudget = -1 });
                return;
            }
            if (!int.TryParse(text, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int budget))
            {
                Notice("A gate budget is a number of gates, 0 or more, or nothing for none.");
                return;
            }
            SetLibrary(library with { GateBudget = budget });
        }

        /// <summary>STEP TEST: the stimulus made a step test of every input, one step, as the button does.</summary>
        public void StepTest()
        {
            if (_session != null)
                Said(_session.StepTest());
        }

        /// <summary>The timing's list, in the dock under its header, shut until its tab is chosen.</summary>
        private void BuildTimingList(SchematicPalette palette)
        {
            _timingList = new VisualElement { name = "timing" };
            _timingList.style.position = Position.Absolute;
            _timingList.style.overflow = Overflow.Hidden;
            _timingList.style.display = DisplayStyle.None;
            _timingList.RegisterCallback<WheelEvent>(e =>
            {
                int lines = _timingShown?.Lines.Count ?? 0;
                _timingFirst = Mathf.Clamp(_timingFirst + (e.delta.y > 0f ? 1 : -1), 0, Mathf.Max(0, lines - TranscriptRowsShown));
                _timingDrawn = false;
                e.StopPropagation();
            });
            _dockPanel.Add(_timingList);

            _timingEmpty = ShellTheme.Label("empty", "Nothing to reckon: the circuit has no inputs or outputs yet.", UiType.Caption, palette.TextDim);
            ShellTheme.Place(_timingEmpty, 20f, 10f, -1f, 24f);
            _timingList.Add(_timingEmpty);
        }

        /// <summary>The list placed under the dock's header, where the transcript is.</summary>
        private void PlaceTiming()
        {
            if (_timingList == null)
                return;
            ShellTheme.Place(_timingList, 0f, ShellLayout.DockHeaderHeight, _regions.Dock.width,
                Mathf.Max(0f, _regions.Dock.height - ShellLayout.DockHeaderHeight));
            _timingDrawn = false;
        }

        /// <summary>
        /// Each frame: while the tab is shown, the timing worked out again once the document has changed, the line picked
        /// kept through it, and the canvas lighting that line's path; while it is not, nothing lit.
        /// </summary>
        private void RefreshTiming()
        {
            if (_session == null || _timingList == null)
                return;

            if (_dockTab != DockTab.Timing)
            {
                if (_editor?.Path != null)
                    _editor.Light(null);
                return;
            }

            GateLibrary library = _session.Project.Settings?.Library;
            if (!ReferenceEquals(library, _libraryShown))
            {
                _libraryShown = library;
                SetText(_libraryButton, LibraryCaption(library));
            }

            // The document changed: its timing worked out again, and measured again from the start -- unless the design is
            // the one measured, as after a move or STEP TEST, when the measuring carries on and is laid on the view as it is.
            TimingView view = _session.Timing;
            if (!ReferenceEquals(view, _timingBase))
            {
                bool alike = _timingBase != null && ReferenceEquals(view.Analysis, _timingBase.Analysis);
                _timingBase = view;
                if (!alike)
                {
                    _measure = StepMeasure.Start(_session.Project, view, () => UnityEngine.Time.realtimeSinceStartupAsDouble * 1000.0, out _measureWhy);
                    _timingSaysShown = int.MinValue;
                }
                ShowTimingView(_measure != null && _measure.Done ? view.With(_measure.Result) : view);
            }
            else if (_measure != null && !_measure.Done)
            {
                _measure.Frame();
                if (_measure.Done)
                    ShowTimingView(view.With(_measure.Result));
            }

            // Written again only when what it says moves: a percent, or the measuring done.
            int says = _measure == null ? -1 : _measure.Done ? 101 : Mathf.FloorToInt((float)_measure.Progress * 100f);
            if (says != _timingSaysShown)
            {
                _timingSaysShown = says;
                SetText(_timingSays, TimingHeader());
            }
            view = _timingShown;

            IReadOnlyList<IReadOnlyList<string>> lit = _timingPicked >= 0 && _timingPicked < view.Lines.Count ? view.Lines[_timingPicked].Paths : null;
            if (_editor != null && !ReferenceEquals(_editor.Path, lit))
                _editor.Light(lit);

            if (!_timingDrawn)
                DrawTiming();
        }

        /// <summary>The timing shown made <paramref name="view"/>, the line picked kept where it still is.</summary>
        private void ShowTimingView(TimingView view)
        {
            _timingShown = view;
            _timingPicked = view.Find(_timingPickedLine);
            // What is picked now is what an edit after this one keeps: a pick that fell back to the critical path stays there.
            _timingPickedLine = _timingPicked >= 0 && _timingPicked < view.Lines.Count ? view.Lines[_timingPicked] : null;
            _timingDrawn = false;
        }

        /// <summary>The header's line: the measuring's progress, why there is none, or how to use the list.</summary>
        private string TimingHeader()
        {
            const string how = "Click a path to light it on the circuit.";
            if (_measure != null && !_measure.Done)
                return "Measuring: every input stepped from every value of the others, "
                    + Mathf.FloorToInt((float)_measure.Progress * 100f).ToString(System.Globalization.CultureInfo.InvariantCulture) + "%.";
            if (_measure?.Problem != null)
                return _measure.Problem + " " + how;
            if (_measure != null)
                return "Measured: a path no step ever fires is marked. " + how;
            return _measureWhy != null ? _measureWhy + " " + how : how;
        }

        /// <summary>
        /// Picks line <paramref name="line"/> of the timing, as a click on it does: lit on the canvas if it is a path, and kept
        /// picked through an edit that leaves it there. A line that is no path picks nothing.
        /// </summary>
        public void PickTiming(int line)
        {
            if (_timingShown == null || line < 0 || line >= _timingShown.Lines.Count || !_timingShown.Lines[line].CanPick)
                return;
            _timingPicked = line;
            _timingPickedLine = _timingShown.Lines[line];
            _timingDrawn = false;
        }

        private void DrawTiming()
        {
            _timingDrawn = true;
            SchematicPalette palette = SchematicPalette.Current;
            IReadOnlyList<TimingLine> lines = _timingShown?.Lines ?? new TimingLine[0];
            int shown = TranscriptRowsShown;
            _timingFirst = Mathf.Clamp(_timingFirst, 0, Mathf.Max(0, lines.Count - shown));

            while (_timingRows.Count < shown)
                _timingRows.Add(TimingRow(_timingRows.Count, palette));

            for (int i = 0; i < _timingRows.Count; i++)
            {
                VisualElement row = _timingRows[i];
                int at = _timingFirst + i;
                bool used = i < shown && at < lines.Count;
                row.style.display = used ? DisplayStyle.Flex : DisplayStyle.None;
                if (!used)
                    continue;

                TimingLine l = lines[at];
                bool picked = at == _timingPicked;
                ShellTheme.Place(row, 0f, 4f + i * TranscriptRowHeight, _regions.Dock.width, TranscriptRowHeight);
                row.style.backgroundColor = picked ? palette.Over(palette.Cursor, 0.18f) : Color.clear;
                row[0].style.backgroundColor = picked ? palette.Cursor : l.CanPick ? palette.Muted : Color.clear;
                SetText((Label)row[1], l.Label);
                ((Label)row[1]).style.color = l.Kind == TimingLineKind.Problem ? palette.Unknown : palette.Text;
                SetText((Label)row[2], l.Text);
                ((Label)row[2]).style.color = l.Kind == TimingLineKind.Fact && !l.CanPick && l.Label.Length == 0 ? palette.TextDim : palette.Text;
                SetText((Label)row[3], l.Numbers);
            }

            _timingEmpty.style.display = lines.Count == 0 ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /// <summary>A line of the timing, the <paramref name="place"/>th on screen: a mark, its label, what it says, and its numbers.</summary>
        private VisualElement TimingRow(int place, SchematicPalette palette)
        {
            var row = new VisualElement { name = TimingRowName + place.ToString(System.Globalization.CultureInfo.InvariantCulture) };
            row.AddToClassList(ShellTheme.ControlClass);
            row.style.position = Position.Absolute;

            var mark = new VisualElement { name = "mark", pickingMode = PickingMode.Ignore };
            ShellTheme.Place(mark, 14f, 5f, 4f, 16f);
            row.Add(mark);
            Label label = ShellTheme.Label("label", "", UiType.Micro, palette.Text, bold: true);
            ShellTheme.Place(label, 26f, 0f, 190f, TranscriptRowHeight);
            row.Add(label);
            Label text = ShellTheme.Label("text", "", UiType.Caption, palette.Text);
            ShellTheme.Place(text, 222f, 0f, -1f, TranscriptRowHeight);
            text.style.right = 250f;
            ShellTheme.Ellipsis(text);
            row.Add(text);
            Label numbers = ShellTheme.Label("numbers", "", UiType.Caption, palette.Text, TextAnchor.MiddleRight);
            ShellTheme.PlaceRight(numbers, 14f, 0f, 230f, TranscriptRowHeight);
            row.Add(numbers);

            int at = place;
            row.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 0)
                    return;
                PickTiming(_timingFirst + at);
                e.StopPropagation();
            });
            _timingList.Add(row);
            return row;
        }
    }
}
