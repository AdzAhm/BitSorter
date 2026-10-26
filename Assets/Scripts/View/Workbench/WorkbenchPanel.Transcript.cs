using System.Collections.Generic;
using System.Globalization;
using BitSorter.ViewModel;
using UnityEngine;
using UnityEngine.UIElements;
using ShellButton = UnityEngine.UIElements.Button;

namespace BitSorter.View
{
    /// <summary>
    /// The transcript and the hierarchy (Part 11c): the dock's TRANSCRIPT tab beside WAVES, and the parts panel's DESIGN
    /// tab beside PARTS.
    /// </summary>
    /// <remarks>
    /// - **The transcript** (<see cref="TranscriptView"/>) is the document's diagnostics and the run's messages, one line
    ///   each, under a filter -- ALL, ERRORS, WARNINGS, MESSAGES -- and a search. Each line says its severity in a word as
    ///   well as a colour; a message says its moment. **A click on a message seeks to its moment; a click on a diagnostic
    ///   about a part or a wire selects it** (Ahmad's choices, 2026-10-25), and the inspector follows the selection. The
    ///   tab says how many errors there are. Only the rows the dock has room for are made, and the wheel moves them.
    /// - **The hierarchy** (<see cref="HierarchyTree"/>) is the testbench, the design and its blocks as rows that open and
    ///   shut on their + and -. **A click on a scope adds its ports to the waveform as one group named by its path**, and
    ///   the dock shows the waves.
    /// </remarks>
    public sealed partial class WorkbenchPanel
    {
        /// <summary>The tabs' and the transcript's names, which the tests look for.</summary>
        public const string TranscriptTabName = "Workbench transcript tab", PartsTabName = "Workbench parts tab",
            DesignTabName = "Workbench design tab", TranscriptSearchName = "Workbench transcript search";

        /// <summary>A filter's button's name: this, and the filter.</summary>
        public const string TranscriptFilterName = "Workbench transcript ";

        /// <summary>A transcript row's name, and a hierarchy row's: this, and its place on screen.</summary>
        public const string TranscriptRowName = "Workbench transcript row ", ScopeRowName = "Workbench scope ";

        /// <summary>The + or - opening or shutting a scope's row: this, and its place on screen.</summary>
        public const string ScopeToggleName = "Workbench scope toggle ";

        private const float TabHeight = 30f, TranscriptRowHeight = 26f, ScopeRowHeight = 26f, ScopeIndent = 14f, ToolsLeft = 330f;

        private enum DockTab { Waves, Transcript }

        private DockTab _dockTab = DockTab.Waves;
        private bool _designTab;
        private ShellButton _wavesTabButton, _transcriptTabButton, _partsTabButton, _designTabButton;
        private VisualElement _dockUnderline, _leftUnderline, _wavesTools, _transcriptTools, _transcriptList, _hierarchyList;
        private Label _transcriptEmpty;
        private readonly TranscriptView _transcript = new TranscriptView();
        private readonly List<VisualElement> _transcriptRows = new List<VisualElement>();
        private int _transcriptFirst, _transcriptDrawn = -1, _transcriptErrorsShown = -1;
        private readonly Dictionary<TranscriptFilter, ShellButton> _filterButtons = new Dictionary<TranscriptFilter, ShellButton>();
        private HierarchyTree _hierarchy;
        private SignalIndex _hierarchyOf;
        private float _hierarchyScroll;

        /// <summary>The transcript as it stands: its lines, filter, search and counts.</summary>
        public TranscriptView Transcript => _transcript;

        /// <summary>Whether the dock shows the transcript rather than the waves.</summary>
        public bool TranscriptShown => _dockTab == DockTab.Transcript;

        /// <summary>The hierarchy as it is drawn in the DESIGN tab; null until the workbench has opened.</summary>
        public HierarchyTree Hierarchy => _hierarchy;

        // -----------------------------------------------------------------
        // The dock's tabs
        // -----------------------------------------------------------------

        /// <summary>The dock's tabs, WAVES and TRANSCRIPT, and an underline under the one shown.</summary>
        private void BuildDockTabs(VisualElement header, SchematicPalette palette)
        {
            _wavesTabButton = Tab(WavesTabName, "WAVES", palette, () => ShowDockTab(DockTab.Waves));
            ShellTheme.Place(_wavesTabButton, 8f, 6f, 92f, TabHeight);
            header.Add(_wavesTabButton);
            _transcriptTabButton = Tab(TranscriptTabName, "TRANSCRIPT", palette, () => ShowDockTab(DockTab.Transcript));
            ShellTheme.Place(_transcriptTabButton, 104f, 6f, 210f, TabHeight);
            header.Add(_transcriptTabButton);

            _dockUnderline = new VisualElement { name = "underline", pickingMode = PickingMode.Ignore };
            _dockUnderline.style.backgroundColor = palette.Accent;
            header.Add(_dockUnderline);

            _wavesTools = new VisualElement { name = "waves tools", pickingMode = PickingMode.Ignore };
            ShellTheme.Place(_wavesTools, 0f, 0f, -1f, ShellLayout.DockHeaderHeight);
            _wavesTools.style.right = 0f;
            header.Add(_wavesTools);

            _transcriptTools = new VisualElement { name = "transcript tools", pickingMode = PickingMode.Ignore };
            ShellTheme.Place(_transcriptTools, 0f, 0f, -1f, ShellLayout.DockHeaderHeight);
            _transcriptTools.style.right = 0f;
            header.Add(_transcriptTools);
            BuildTranscriptTools(palette);
        }

        /// <summary>A tab: a caption with no edge, in the accent while it is the one shown.</summary>
        private static ShellButton Tab(string name, string caption, SchematicPalette palette, System.Action chosen)
        {
            ShellButton tab = ShellTheme.Button(name, caption, palette, chosen, UiType.Caption);
            tab.style.borderLeftWidth = tab.style.borderRightWidth = tab.style.borderTopWidth = tab.style.borderBottomWidth = 0f;
            tab.style.backgroundColor = Color.clear;
            tab.AddToClassList(ShellTheme.BoldClass);
            tab.style.unityFontDefinition = ShellFonts.Bold;
            return tab;
        }

        /// <summary>The dock shows <paramref name="tab"/>: the waves and their buttons, or the transcript and its.</summary>
        private void ShowDockTab(DockTab tab)
        {
            _dockTab = tab;
            bool waves = tab == DockTab.Waves;
            _wavesTools.style.display = waves ? DisplayStyle.Flex : DisplayStyle.None;
            _transcriptTools.style.display = waves ? DisplayStyle.None : DisplayStyle.Flex;
            _transcriptList.style.display = waves ? DisplayStyle.None : DisplayStyle.Flex;
            if (_wavesClip != null)
            {
                _wavesClip.gameObject.SetActive(waves);
                _legend.gameObject.SetActive(waves);
                if (!waves)
                    Show(_wavesThumb, false);
                else
                    ScrollWavesBy(0f);
            }

            SchematicPalette palette = SchematicPalette.Current;
            ShellButton shown = waves ? _wavesTabButton : _transcriptTabButton;
            _wavesTabButton.style.color = waves ? palette.Text : palette.TextDim;
            _transcriptTabButton.style.color = waves ? palette.TextDim : palette.Text;
            ShellTheme.Place(_dockUnderline, shown.style.left.value.value + 10f, 36f, shown.style.width.value.value - 20f, 2f);
            _transcriptDrawn = -1;
        }

        /// <summary>Shows the transcript, as its tab does.</summary>
        public void ShowTranscript() => ShowDockTab(DockTab.Transcript);

        /// <summary>Shows the waves, as their tab does.</summary>
        public void ShowWaves() => ShowDockTab(DockTab.Waves);

        // -----------------------------------------------------------------
        // The transcript
        // -----------------------------------------------------------------

        private void BuildTranscriptTools(SchematicPalette palette)
        {
            float x = ToolsLeft;
            foreach ((TranscriptFilter filter, string caption, float width) in new[]
                     {
                         (TranscriptFilter.All, "ALL", 70f), (TranscriptFilter.Errors, "ERRORS", 110f),
                         (TranscriptFilter.Warnings, "WARNINGS", 130f), (TranscriptFilter.Messages, "MESSAGES", 130f),
                     })
            {
                TranscriptFilter chosen = filter;
                ShellButton button = ShellTheme.Button(TranscriptFilterName + filter, caption, palette, () =>
                {
                    _transcript.Filter = chosen;
                    _transcriptFirst = 0;
                    ShowFilter();
                }, UiType.Caption);
                ShellTheme.Place(button, x, 5f, width, 34f);
                _transcriptTools.Add(button);
                _filterButtons[filter] = button;
                x += width + 10f;
            }

            TextField search = ShellTheme.Field(TranscriptSearchName, "search the transcript", palette, null, submitOnLeave: false, type: UiType.Caption);
            ShellTheme.Place(search, x + 10f, 5f, 300f, 34f);
            search.RegisterValueChangedCallback(e =>
            {
                _transcript.Search = e.newValue;
                _transcriptFirst = 0;
            });
            _transcriptTools.Add(search);
            ShowFilter();
        }

        /// <summary>The filter's button for the one in use in the accent, the rest as they were.</summary>
        private void ShowFilter()
        {
            SchematicPalette palette = SchematicPalette.Current;
            foreach (KeyValuePair<TranscriptFilter, ShellButton> pair in _filterButtons)
                pair.Value.style.color = pair.Key == _transcript.Filter ? palette.Accent : palette.Text;
        }

        /// <summary>The transcript's list, in the dock under its header, shut until its tab is chosen.</summary>
        private void BuildTranscriptList(SchematicPalette palette)
        {
            _transcriptList = new VisualElement { name = "transcript" };
            _transcriptList.style.position = Position.Absolute;
            _transcriptList.style.overflow = Overflow.Hidden;
            _transcriptList.style.display = DisplayStyle.None;
            _transcriptList.RegisterCallback<WheelEvent>(e =>
            {
                _transcriptFirst = Mathf.Clamp(_transcriptFirst + (e.delta.y > 0f ? 1 : -1), 0, Mathf.Max(0, _transcript.Lines.Count - TranscriptRowsShown));
                _transcriptDrawn = -1;
                e.StopPropagation();
            });
            _dockPanel.Add(_transcriptList);

            _transcriptEmpty = ShellTheme.Label("empty", "", UiType.Caption, palette.TextDim);
            ShellTheme.Place(_transcriptEmpty, 20f, 10f, -1f, 24f);
            _transcriptList.Add(_transcriptEmpty);
        }

        /// <summary>How many lines the dock has room for.</summary>
        private int TranscriptRowsShown => Mathf.Max(1, Mathf.FloorToInt((_regions.Dock.height - ShellLayout.DockHeaderHeight - 8f) / TranscriptRowHeight));

        /// <summary>The list placed under the dock's header, as tall as the waves and the legend together.</summary>
        private void PlaceTranscript()
        {
            if (_transcriptList == null)
                return;
            ShellTheme.Place(_transcriptList, 0f, ShellLayout.DockHeaderHeight, _regions.Dock.width,
                Mathf.Max(0f, _regions.Dock.height - ShellLayout.DockHeaderHeight));
            _transcriptDrawn = -1;
        }

        /// <summary>The transcript read again from the document and the run, its tab's count, and its rows when it is shown.</summary>
        private void RefreshTranscript()
        {
            if (_session == null || _model == null || _transcriptList == null)
                return;

            _transcript.Update(_session.Editor.Diagnostics, _model.Session.History.Messages);
            if (_transcript.Errors != _transcriptErrorsShown)
            {
                _transcriptErrorsShown = _transcript.Errors;
                int errors = _transcript.Errors;
                SetText(_transcriptTabButton, errors == 0 ? "TRANSCRIPT"
                    : "TRANSCRIPT · " + errors.ToString(CultureInfo.InvariantCulture) + (errors == 1 ? " ERROR" : " ERRORS"));
            }

            if (_dockTab != DockTab.Transcript || _transcriptDrawn == _transcript.Version)
                return;
            _transcriptDrawn = _transcript.Version;
            DrawTranscript();
        }

        private void DrawTranscript()
        {
            SchematicPalette palette = SchematicPalette.Current;
            IReadOnlyList<TranscriptLine> lines = _transcript.Lines;
            int shown = TranscriptRowsShown;
            _transcriptFirst = Mathf.Clamp(_transcriptFirst, 0, Mathf.Max(0, lines.Count - shown));

            while (_transcriptRows.Count < shown)
                _transcriptRows.Add(TranscriptRow(_transcriptRows.Count, palette));

            for (int i = 0; i < _transcriptRows.Count; i++)
            {
                VisualElement row = _transcriptRows[i];
                int line = _transcriptFirst + i;
                bool used = i < shown && line < lines.Count;
                row.style.display = used ? DisplayStyle.Flex : DisplayStyle.None;
                if (!used)
                    continue;

                TranscriptLine l = lines[line];
                ShellTheme.Place(row, 0f, 4f + i * TranscriptRowHeight, _regions.Dock.width, TranscriptRowHeight);
                row[0].style.backgroundColor = l.Severity == TranscriptSeverity.Error ? palette.Unknown
                    : l.Severity == TranscriptSeverity.Warning ? palette.Cursor : palette.Muted;
                SetText((Label)row[1], TranscriptView.Mark(l.Severity));
                SetText((Label)row[2], l.IsMessage ? l.Time.ToString(CultureInfo.InvariantCulture) + " " + _model.Unit : "");
                SetText((Label)row[3], l.Text);
            }

            _transcriptEmpty.style.display = lines.Count == 0 ? DisplayStyle.Flex : DisplayStyle.None;
            SetText(_transcriptEmpty, _transcript.Errors + _transcript.Warnings + _transcript.Infos == 0
                ? "Nothing to read: the circuit is whole and the run has said nothing."
                : "Nothing here: another filter, or another search.");
        }

        /// <summary>A row of the transcript, the <paramref name="place"/>th on screen: a mark, the severity's word, the moment and the text.</summary>
        private VisualElement TranscriptRow(int place, SchematicPalette palette)
        {
            var row = new VisualElement { name = TranscriptRowName + place.ToString(CultureInfo.InvariantCulture) };
            row.AddToClassList(ShellTheme.ControlClass);
            row.style.position = Position.Absolute;

            var mark = new VisualElement { name = "mark", pickingMode = PickingMode.Ignore };
            ShellTheme.Place(mark, 14f, 5f, 4f, 16f);
            row.Add(mark);
            Label word = ShellTheme.Label("severity", "", UiType.Micro, palette.Text, bold: true);
            ShellTheme.Place(word, 26f, 0f, 84f, TranscriptRowHeight);
            row.Add(word);
            Label when = ShellTheme.Label("time", "", UiType.Caption, palette.TextDim);
            ShellTheme.Place(when, 114f, 0f, 90f, TranscriptRowHeight);
            row.Add(when);
            Label text = ShellTheme.Label("text", "", UiType.Caption, palette.Text);
            ShellTheme.Place(text, 210f, 0f, -1f, TranscriptRowHeight);
            text.style.right = 14f;
            ShellTheme.Ellipsis(text);
            row.Add(text);

            int at = place;
            row.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 0)
                    return;
                ActOnTranscript(_transcriptFirst + at);
                e.StopPropagation();
            });
            _transcriptList.Add(row);
            return row;
        }

        /// <summary>
        /// What a click on line <paramref name="line"/> of the transcript does: a message seeks to its moment, paused there;
        /// a diagnostic about a part or a wire of the circuit shown selects it alone.
        /// </summary>
        public void ActOnTranscript(int line)
        {
            IReadOnlyList<TranscriptLine> lines = _transcript.Lines;
            if (line < 0 || line >= lines.Count)
                return;

            TranscriptLine l = lines[line];
            if (l.IsMessage)
            {
                Pause();
                Seek(System.Math.Min(l.Time, _model.End));
                return;
            }

            if (_editor == null || (l.Module != null && l.Module != _session.Module))
                return;
            if (l.Part >= 0)
                _editor.Selection.Pick(new CanvasTarget(CanvasTargetKind.Part, part: l.Part), false, false);
            else if (l.Wire >= 0)
                _editor.Selection.Pick(new CanvasTarget(CanvasTargetKind.Wire, wire: l.Wire), false, false);
        }

        // -----------------------------------------------------------------
        // The parts panel's tabs, and the hierarchy
        // -----------------------------------------------------------------

        /// <summary>The parts panel's tabs, PARTS and DESIGN, and the hierarchy's list, shut until its tab is chosen.</summary>
        private void BuildLeftTabs(SchematicPalette palette)
        {
            _partsTabButton = Tab(PartsTabName, "PARTS", palette, () => ShowLeftTab(design: false));
            ShellTheme.Place(_partsTabButton, 4f, 4f, 84f, TabHeight);
            _partsPanel.Add(_partsTabButton);
            _designTabButton = Tab(DesignTabName, "DESIGN", palette, () => ShowLeftTab(design: true));
            ShellTheme.Place(_designTabButton, 92f, 4f, 92f, TabHeight);
            _partsPanel.Add(_designTabButton);
            _leftUnderline = new VisualElement { name = "underline", pickingMode = PickingMode.Ignore };
            _leftUnderline.style.backgroundColor = palette.Accent;
            _partsPanel.Add(_leftUnderline);

            _hierarchyList = new VisualElement { name = "hierarchy" };
            _hierarchyList.style.position = Position.Absolute;
            _hierarchyList.RegisterCallback<WheelEvent>(e =>
            {
                ScrollHierarchy(e.delta.y > 0f ? ScopeRowHeight : -ScopeRowHeight);
                e.StopPropagation();
            });
            _partsPanel.Add(_hierarchyList);
            _hierarchyOf = null;
            ShowLeftTab(_designTab);
        }

        /// <summary>The parts panel shows the parts, or the design's hierarchy.</summary>
        private void ShowLeftTab(bool design)
        {
            _designTab = design;
            _partsList.style.display = design ? DisplayStyle.None : DisplayStyle.Flex;
            _partsThumb.style.display = DisplayStyle.None;
            _hierarchyList.style.display = design ? DisplayStyle.Flex : DisplayStyle.None;
            if (!design)
                ScrollParts(0f);

            SchematicPalette palette = SchematicPalette.Current;
            ShellButton shown = design ? _designTabButton : _partsTabButton;
            _partsTabButton.style.color = design ? palette.TextDim : palette.Text;
            _designTabButton.style.color = design ? palette.Text : palette.TextDim;
            ShellTheme.Place(_leftUnderline, shown.style.left.value.value + 10f, 34f, shown.style.width.value.value - 20f, 2f);
            _hierarchyOf = null;
        }

        /// <summary>Shows the design's hierarchy in the parts panel, as its tab does.</summary>
        public void ShowDesign() => ShowLeftTab(design: true);

        /// <summary>The hierarchy made again when the design shown is another, and drawn when its tab is up.</summary>
        private void RefreshHierarchy()
        {
            if (_hierarchyList == null || _model == null || ReferenceEquals(_hierarchyOf, _model.Signals))
                return;
            _hierarchyOf = _model.Signals;
            if (_hierarchy == null || !ReferenceEquals(_hierarchyIndex, _model.Signals))
            {
                _hierarchy = _model.Signals != null ? new HierarchyTree(_model.Signals) : null;
                _hierarchyIndex = _model.Signals;
            }

            DrawHierarchy();
        }

        private SignalIndex _hierarchyIndex;

        private void DrawHierarchy()
        {
            SchematicPalette palette = SchematicPalette.Current;
            _hierarchyList.Clear();
            if (_hierarchy == null)
                return;

            float inside = ShellLayout.PartsWidth - 2f * PanelPad;
            for (int i = 0; i < _hierarchy.Rows.Count; i++)
            {
                HierarchyRow row = _hierarchy.Rows[i];
                float indent = row.Depth * ScopeIndent, y = i * ScopeRowHeight;
                int at = i;

                if (row.Opens)
                {
                    ShellButton toggle = ShellTheme.Button(ScopeToggleName + i.ToString(CultureInfo.InvariantCulture), row.Open ? "-" : "+",
                        palette, () => { if (_hierarchy.Toggle(at)) DrawHierarchy(); }, UiType.Caption);
                    ShellTheme.Place(toggle, PanelPad + indent, y + 3f, 20f, ScopeRowHeight - 6f);
                    _hierarchyList.Add(toggle);
                }

                ShellButton scope = ShellTheme.Button(ScopeRowName + i.ToString(CultureInfo.InvariantCulture), row.Label, palette,
                    () => ScopeClicked(at), UiType.Caption);
                scope.style.borderLeftWidth = scope.style.borderRightWidth = scope.style.borderTopWidth = scope.style.borderBottomWidth = 0f;
                scope.style.backgroundColor = Color.clear;
                scope.style.unityTextAlign = TextAnchor.MiddleLeft;
                ShellTheme.Ellipsis(scope);
                ShellTheme.Place(scope, PanelPad + indent + 24f, y, Mathf.Max(20f, inside - indent - 24f), ScopeRowHeight);
                _hierarchyList.Add(scope);
            }

            ScrollHierarchy(0f);
        }

        /// <summary>Moves the hierarchy's rows by <paramref name="units"/>, within what they can scroll.</summary>
        private void ScrollHierarchy(float units)
        {
            if (_hierarchyList == null)
                return;
            int rows = _hierarchy?.Rows.Count ?? 0;
            float overflow = Mathf.Max(0f, PartsTop + rows * ScopeRowHeight + PanelPad - _regions.Parts.height);
            _hierarchyScroll = Mathf.Clamp(_hierarchyScroll + units, 0f, overflow);
            ShellTheme.Place(_hierarchyList, 0f, PartsTop - _hierarchyScroll, ShellLayout.PartsWidth, rows * ScopeRowHeight);
        }

        // The hierarchy's row last clicked, and when: a second click on it soon after is a double-click (Part 12a).
        private int _scopeClicked = -1;
        private double _scopeClickedAt = double.NegativeInfinity;

        /// <summary>
        /// A click on row <paramref name="row"/> of the hierarchy adds its ports to the waveform (<see cref="AddScope"/>); a
        /// second one soon after steps inside it, when it is a block of the design (Part 12a). Counted on the game's clock,
        /// as the canvas's double-click is.
        /// </summary>
        private void ScopeClicked(int row)
        {
            double now = UnityEngine.Time.timeAsDouble;
            bool second = row == _scopeClicked && now - _scopeClickedAt <= CanvasPointer.DoubleClickSeconds;
            _scopeClicked = second ? -1 : row;
            _scopeClickedAt = now;
            if (second && _hierarchy != null && row >= 0 && row < _hierarchy.Rows.Count && StepToScope(_hierarchy.Rows[row].Node.Path))
                return;
            if (!second)
                AddScope(row);
        }

        /// <summary>
        /// What a click on row <paramref name="row"/> of the hierarchy does: the scope's ports added to the waveform as one
        /// group named by its path, and the dock showing the waves.
        /// </summary>
        public void AddScope(int row)
        {
            if (_hierarchy == null || row < 0 || row >= _hierarchy.Rows.Count || _list == null)
                return;
            ScopeNode node = _hierarchy.Rows[row].Node;
            if (_list.AddGroup(_hierarchy.GroupNameOf(node), _hierarchy.SignalsOf(node)) == null)
                return;
            ShowDockTab(DockTab.Waves);
            RowsChanged();
        }
    }
}
