using System.Collections.Generic;
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
    public sealed partial class WorkbenchPanel
    {
        /// <summary>The tab's name, and a line's: this, and its place on screen.</summary>
        public const string TimingTabName = "Workbench timing tab", TimingRowName = "Workbench timing row ";

        private const float TimingTabLeft = CodeTabLeft + CodeTabWidth + 4f, TimingTabWidth = 100f;

        private ShellButton _timingTabButton;
        private VisualElement _timingTools, _timingList;
        private Label _timingEmpty;
        private readonly List<VisualElement> _timingRows = new List<VisualElement>();
        private TimingView _timingShown;
        private TimingLine _timingPickedLine;
        private int _timingPicked = -1, _timingFirst;
        private bool _timingDrawn;

        /// <summary>Whether the dock shows the timing.</summary>
        public bool TimingShown => _dockTab == DockTab.Timing;

        /// <summary>The timing as the tab shows it; null until the tab has been shown.</summary>
        public TimingView Timing => _timingShown;

        /// <summary>The line picked, lit on the canvas, by its index in <see cref="Timing"/>; -1 for none.</summary>
        public int TimingPicked => _timingPicked;

        /// <summary>Shows the timing, as its tab does.</summary>
        public void ShowTiming() => ShowDockTab(DockTab.Timing);

        /// <summary>The TIMING tab beside CODE, and the line its header says how to use it on.</summary>
        private void BuildTimingTab(VisualElement header, SchematicPalette palette)
        {
            _timingTabButton = Tab(TimingTabName, "TIMING", palette, () => ShowDockTab(DockTab.Timing));
            ShellTheme.Place(_timingTabButton, TimingTabLeft, 6f, TimingTabWidth, TabHeight);
            header.Add(_timingTabButton);

            _timingTools = new VisualElement { name = "timing tools", pickingMode = PickingMode.Ignore };
            ShellTheme.Place(_timingTools, 0f, 0f, -1f, ShellLayout.DockHeaderHeight);
            _timingTools.style.right = 0f;
            header.Add(_timingTools);

            Label how = ShellTheme.Label("how", "Click a path to light it on the circuit. Times are in the project's unit.", UiType.Caption, palette.TextDim);
            ShellTheme.Place(how, ToolsLeft, 0f, -1f, ShellLayout.DockHeaderHeight);
            how.style.right = 14f;
            ShellTheme.Ellipsis(how);
            _timingTools.Add(how);
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

            TimingView view = _session.Timing;
            if (!ReferenceEquals(view, _timingShown))
            {
                _timingShown = view;
                _timingPicked = view.Find(_timingPickedLine);
                // What is picked now is what an edit after this one keeps: a pick that fell back to the critical path stays there.
                _timingPickedLine = _timingPicked >= 0 && _timingPicked < view.Lines.Count ? view.Lines[_timingPicked] : null;
                _timingDrawn = false;
            }

            IReadOnlyList<IReadOnlyList<string>> lit = _timingPicked >= 0 && _timingPicked < view.Lines.Count ? view.Lines[_timingPicked].Paths : null;
            if (_editor != null && !ReferenceEquals(_editor.Path, lit))
                _editor.Light(lit);

            if (!_timingDrawn)
                DrawTiming();
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
