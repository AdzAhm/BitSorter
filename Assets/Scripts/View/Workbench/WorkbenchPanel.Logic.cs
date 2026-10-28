using System.Collections.Generic;
using System.Globalization;
using System.Text;
using BitSorter.ViewModel;
using UnityEngine;
using UnityEngine.UIElements;
using ShellButton = UnityEngine.UIElements.Button;

namespace BitSorter.View
{
    /// <summary>
    /// The dock's LOGIC tab (Part 15g), beside TIMING: what the circuit as built does, for logic without registers -- its
    /// truth table, found by running the engine through every combination (<see cref="LogicTable"/>); each output as a
    /// minimal sum of products, the textbook's way and SystemVerilog's (<see cref="Minimizer"/>); and for four inputs or
    /// fewer, each output's K-map (<see cref="KarnaughLayout"/>). Ahmad asked for a way to know more about what is built
    /// (2026-10-28).
    /// </summary>
    /// <remarks>
    /// **Made while the tab is up, a slice a frame**, as the measured worst is, and made again once the document changes; the
    /// header says how far it has got, or why there is no table -- registers, a loop, too many inputs, and by how much.
    /// The wheel scrolls the table on the left and the expressions on the right, each where the pointer is.
    /// </remarks>
    public sealed partial class WorkbenchPanel
    {
        /// <summary>The tab's name, and its parts' names: each a name a test finds them by.</summary>
        public const string LogicTabName = "Workbench logic tab", LogicSaysName = "Workbench logic says",
            LogicExpressionName = "Workbench logic expression ", LogicVerilogName = "Workbench logic verilog ";

        private const float LogicTabLeft = TimingTabLeft + TimingTabWidth + 4f, LogicTabWidth = 84f, LogicRowHeight = 24f,
            LogicColumnGap = 10f, LogicCellWidth = 30f, LogicBlockWidth = 620f, LogicBlockGap = 30f;

        private ShellButton _logicTabButton;
        private VisualElement _logicTools, _logicList;
        private Label _logicSays;
        private LogicTable _logic;
        private string _logicWhy;
        private BitSorter.Board.Project _logicOf;
        private int _logicFirst;
        private float _logicRightScroll, _logicRightReach;
        private bool _logicDrawn, _logicRead, _logicFinishing;
        private Minimizer.Search _logicSearch;
        private readonly List<(LogicOutput Output, Minimizer.Result Expression, KarnaughLayout Map)> _logicOutputs =
            new List<(LogicOutput, Minimizer.Result, KarnaughLayout)>();

        /// <summary>Whether the dock shows LOGIC.</summary>
        public bool LogicShown => _dockTab == DockTab.Logic;

        /// <summary>The truth table as the tab has it; null when there is none, and until the tab has been shown.</summary>
        public LogicTable Logic => _logic;

        /// <summary>What the LOGIC tab's header says.</summary>
        public string LogicSays => _logicSays?.text;

        /// <summary>Shows LOGIC, as its tab does.</summary>
        public void ShowLogic() => ShowDockTab(DockTab.Logic);

        /// <summary>The rest of the table's run now: what a test and a reference shot do.</summary>
        public void FinishLogic()
        {
            _logicFinishing = true;
            try
            {
                RefreshLogic();
                _logic?.Finish();
                RefreshLogic();
            }
            finally
            {
                _logicFinishing = false;
            }
        }

        /// <summary>The LOGIC tab beside TIMING, and the line its header says how far the table has got on.</summary>
        private void BuildLogicTab(VisualElement header, SchematicPalette palette)
        {
            _logicTabButton = Tab(LogicTabName, "LOGIC", palette, () => ShowDockTab(DockTab.Logic));
            ShellTheme.Place(_logicTabButton, LogicTabLeft, 6f, LogicTabWidth, TabHeight);
            header.Add(_logicTabButton);

            _logicTools = new VisualElement { name = "logic tools", pickingMode = PickingMode.Ignore };
            ShellTheme.Place(_logicTools, 0f, 0f, -1f, ShellLayout.DockHeaderHeight);
            _logicTools.style.right = 0f;
            header.Add(_logicTools);

            _logicSays = ShellTheme.Label(LogicSaysName, "", UiType.Caption, palette.TextDim);
            ShellTheme.Place(_logicSays, ToolsLeft, 0f, -1f, ShellLayout.DockHeaderHeight);
            _logicSays.style.right = 14f;
            ShellTheme.Ellipsis(_logicSays);
            _logicTools.Add(_logicSays);
        }

        /// <summary>The tab's list, in the dock under its header, shut until its tab is chosen.</summary>
        private void BuildLogicList()
        {
            _logicList = new VisualElement { name = "logic" };
            _logicList.style.position = Position.Absolute;
            _logicList.style.overflow = Overflow.Hidden;
            _logicList.style.display = DisplayStyle.None;
            _logicList.RegisterCallback<WheelEvent>(e =>
            {
                // The table under the pointer on the left, three rows a notch; the expressions on the right, never past their foot
                // (the 15g-5 review found no end to them, and a few notches left the pane blank).
                if (e.localMousePosition.x < LogicTableWidth())
                    _logicFirst = Mathf.Clamp(_logicFirst + (e.delta.y > 0f ? 3 : -3), 0, Mathf.Max(0, (_logic?.Rows ?? 0) - LogicRowsShown));
                else
                    _logicRightScroll = Mathf.Clamp(_logicRightScroll + (e.delta.y > 0f ? 60f : -60f), 0f, LogicRightScrollMost);
                _logicDrawn = false;
                e.StopPropagation();
            });
            _dockPanel.Add(_logicList);
        }

        /// <summary>The list placed under the dock's header, where the transcript is.</summary>
        private void PlaceLogic()
        {
            if (_logicList == null)
                return;
            ShellTheme.Place(_logicList, 0f, ShellLayout.DockHeaderHeight, _regions.Dock.width,
                Mathf.Max(0f, _regions.Dock.height - ShellLayout.DockHeaderHeight));
            _logicDrawn = false;
        }

        // A frame's share of the expressions' search: tries a slice, and the time a frame spends on them.
        private const int LogicTriesASlice = 2000;
        private const double LogicBudgetMilliseconds = 4.0;

        /// <summary>What the header says while the expressions are being written: how many are.</summary>
        private string LogicWriting() => "Writing each output as a sum of products: "
            + _logicOutputs.Count.ToString(CultureInfo.InvariantCulture) + " of " + _logic.Outputs.Count.ToString(CultureInfo.InvariantCulture) + ".";

        /// <summary>How far the expressions scroll at most: their foot to the list's, as last drawn.</summary>
        private float LogicRightScrollMost =>
            Mathf.Max(0f, _logicRightReach - Mathf.Max(0f, _regions.Dock.height - ShellLayout.DockHeaderHeight));

        /// <summary>How many of the table's rows fit under its heads.</summary>
        private int LogicRowsShown => Mathf.Max(1, Mathf.FloorToInt((_regions.Dock.height - ShellLayout.DockHeaderHeight - 12f) / LogicRowHeight) - 1);

        /// <summary>
        /// Each frame while the tab is up: the table started again once the document has changed, a slice of its run made, its
        /// expressions and maps worked out once it is read, and the tab drawn when anything moved.
        /// </summary>
        private void RefreshLogic()
        {
            if (_session == null || _logicList == null || _dockTab != DockTab.Logic)
                return;

            // Made again only when the design has changed: a part moved, a wire rerouted or an input's steps edited leave the
            // table, and where it is scrolled to, as they were (the 15g-5 review). The design includes its files: a ROM's image
            // read with $readmemh is the design's. The old table goes while the new one is made.
            BitSorter.Board.Project project = _session.Project;
            if (!ReferenceEquals(project, _logicOf))
            {
                bool same = _logicOf != null && TimingView.SameDesign(_logicOf, project);
                _logicOf = project;
                if (!same)
                {
                    _logic = LogicTable.Start(project, _session.Timing, () => UnityEngine.Time.realtimeSinceStartupAsDouble * 1000.0, out _logicWhy);
                    _logicRead = false;
                    _logicFirst = 0;
                    _logicRightScroll = 0f;
                    _logicOutputs.Clear();
                    _logicList.Clear();
                    _logicDrawn = false;
                }
            }

            if (_logic != null && !_logic.Done)
            {
                _logic.Frame();
                SetText(_logicSays, "Running every combination of the inputs: "
                    + Mathf.FloorToInt((float)_logic.Progress * 100f).ToString(CultureInfo.InvariantCulture) + "%.");
                if (!_logic.Done)
                    return;
            }

            if (_logic != null && !_logicRead)
            {
                _logicRead = true;
                _logicOutputs.Clear();
                _logicSearch = null;
                _logicDrawn = false;
            }

            // Each output's expression, a few milliseconds a frame -- all at once for a test or a shot. An eight-input output with
            // no pattern to it takes the search's every try, a quarter of a second at once (the 15g-5 review).
            if (_logic != null && _logicRead && _logicOutputs.Count < _logic.Outputs.Count)
            {
                double start = UnityEngine.Time.realtimeSinceStartupAsDouble * 1000.0;
                bool spent = false;
                while (_logicOutputs.Count < _logic.Outputs.Count && !spent)
                {
                    LogicOutput output = _logic.Outputs[_logicOutputs.Count];
                    Minimizer.Result expression = null;
                    if (_logic.Variables.Count <= Minimizer.MostVariables)
                    {
                        _logicSearch ??= new Minimizer.Search(_logic.Variables.Count, output.Values);
                        bool done = _logicSearch.Run(_logicFinishing ? int.MaxValue : LogicTriesASlice);
                        spent = !_logicFinishing && UnityEngine.Time.realtimeSinceStartupAsDouble * 1000.0 - start >= LogicBudgetMilliseconds;
                        if (!done)
                            continue;
                        expression = _logicSearch.Result;
                        _logicSearch = null;
                    }
                    _logicOutputs.Add((output, expression, KarnaughLayout.Of(_logic.VariableNames, output.Values)));
                    _logicDrawn = false;
                }
                if (_logicDrawn)
                    SetText(_logicSays, LogicWriting());
            }

            if (!_logicDrawn)
                DrawLogic();
        }

        /// <summary>How wide the truth table is drawn: a column for each input and output port.</summary>
        private float LogicTableWidth()
        {
            if (_logic == null)
                return 0f;
            float width = 14f;
            foreach ((string _, int w, string head) in LogicPorts())
                width += LogicPortWidth(w, head) + LogicColumnGap;
            return width + 20f;
        }

        /// <summary>The table's ports: the inputs, then the outputs, each with its width and its head.</summary>
        private List<(string Port, int Width, string Head)> LogicPorts()
        {
            var ports = new List<(string, int, string)>();
            for (int v = 0; v < _logic.Variables.Count; v++)
                if (v == 0 || _logic.Variables[v].Port != _logic.Variables[v - 1].Port)
                {
                    BitSorter.Board.InputBit bit = _logic.Variables[v];
                    ports.Add((bit.Port, bit.Width, LogicHead(bit.Port, bit.Width, _logic.VariableNames[v], _logic.VariableNames[v + bit.Width - 1])));
                }
            for (int o = 0; o < _logic.Outputs.Count; o++)
                if (o == 0 || _logic.Outputs[o].Port != _logic.Outputs[o - 1].Port)
                {
                    int width = 0;
                    while (o + width < _logic.Outputs.Count && _logic.Outputs[o + width].Port == _logic.Outputs[o].Port)
                        width++;
                    ports.Add((_logic.Outputs[o].Port, width, LogicHead(_logic.Outputs[o].Port, width, _logic.Outputs[o].Name, _logic.Outputs[o + width - 1].Name)));
                }
            return ports;
        }

        /// <summary>A port's head: its name, and a bus's range as declared, read off its first and last bits' names -- a[7:6].</summary>
        private static string LogicHead(string port, int width, string first, string last)
        {
            static string Index(string name)
            {
                int open = name.LastIndexOf('[');
                return open >= 0 && name.EndsWith("]", System.StringComparison.Ordinal) ? name.Substring(open + 1, name.Length - open - 2) : null;
            }
            string high = Index(first), low = Index(last);
            return width > 1 && high != null && low != null ? port + "[" + high + ":" + low + "]" : port;
        }

        private static float LogicPortWidth(int width, string head) =>
            Mathf.Max(LogicCellWidth, Mathf.Max(head.Length, width <= 8 ? width : (width + 3) / 4) * 9f + 10f);

        /// <summary>A port's value in a row: binary up to 8 bits, hex past that, an x digit where any bit of it is x.</summary>
        private string LogicValue(string port, int width, int row, bool input)
        {
            var bits = new char[width];   // bits[0] the highest
            int b = 0;
            if (input)
            {
                for (int v = 0; v < _logic.Variables.Count; v++)
                    if (_logic.Variables[v].Port == port)
                        bits[b++] = _logic.ValueOf(v, row) == 1 ? '1' : '0';
            }
            else
            {
                foreach (LogicOutput output in _logic.Outputs)
                    if (output.Port == port)
                        bits[b++] = output.Values[row];
            }
            if (width <= 8)
                return new string(bits);

            var hex = new StringBuilder((width + 3) / 4);
            for (int end = width; end > 0; end -= 4)
            {
                int start = Mathf.Max(0, end - 4), value = 0;
                bool unknown = false;
                for (int i = start; i < end; i++)
                {
                    unknown |= bits[i] == 'x';
                    value = value << 1 | (bits[i] == '1' ? 1 : 0);
                }
                hex.Insert(0, unknown ? 'x' : "0123456789abcdef"[value]);
            }
            return hex.ToString();
        }

        private void DrawLogic()
        {
            _logicDrawn = true;
            _logicList.Clear();
            SchematicPalette palette = SchematicPalette.Current;

            if (_logic == null)
            {
                SetText(_logicSays, _logicWhy ?? "");
                return;
            }

            int inputs = _logic.Variables.Count;
            SetText(_logicSays, _logicOutputs.Count < _logic.Outputs.Count ? LogicWriting()
                : _logic.Problem ?? (_logic.Rows.ToString(CultureInfo.InvariantCulture) + " rows: every combination of "
                + inputs.ToString(CultureInfo.InvariantCulture) + " input bit" + (inputs == 1 ? "" : "s")
                + ", each output read once it has settled. The wheel scrolls the table, and the expressions beside it."));

            // The truth table: a head for each port, inputs then outputs, and the rows that fit from the first scrolled to.
            List<(string Port, int Width, string Head)> ports = LogicPorts();
            int inputPorts = 0;
            string lastInput = null;
            foreach (BitSorter.Board.InputBit bit in _logic.Variables)
                if (bit.Port != lastInput)
                {
                    lastInput = bit.Port;
                    inputPorts++;
                }

            int shown = LogicRowsShown;
            _logicFirst = Mathf.Clamp(_logicFirst, 0, Mathf.Max(0, _logic.Rows - shown));
            float x = 14f;
            for (int p = 0; p < ports.Count; p++)
            {
                float width = LogicPortWidth(ports[p].Width, ports[p].Head);
                bool input = p < inputPorts;
                Label head = ShellTheme.Label("head", ports[p].Head, UiType.Caption, input ? palette.TextDim : palette.Accent, bold: true);
                ShellTheme.Place(head, x, 6f, width, LogicRowHeight);
                _logicList.Add(head);
                for (int i = 0; i < shown && _logicFirst + i < _logic.Rows; i++)
                {
                    int row = _logicFirst + i;
                    Label cell = ShellTheme.Label("cell", LogicValue(ports[p].Port, ports[p].Width, row, input), UiType.Caption,
                        input ? palette.TextDim : palette.Text);
                    ShellTheme.Place(cell, x, 6f + (i + 1) * LogicRowHeight, width, LogicRowHeight);
                    _logicList.Add(cell);
                }
                x += width + LogicColumnGap;
                if (p == inputPorts - 1)
                {
                    // The line between what is given and what it gives.
                    var rule = new VisualElement { name = "rule", pickingMode = PickingMode.Ignore };
                    rule.style.backgroundColor = palette.CardEdge;
                    ShellTheme.Place(rule, x - LogicColumnGap / 2f - 1f, 6f, 1f, (Mathf.Min(shown, _logic.Rows) + 1) * LogicRowHeight);
                    _logicList.Add(rule);
                }
            }

            // Beside it, each output's expression, both ways, and its K-map.
            IReadOnlyList<string> names = _logic.VariableNames;
            // Each output a block, side by side in columns as many as fit, a row of them as tall as its tallest.
            float left = LogicTableWidth() + 20f, top = 6f - _logicRightScroll;
            float room = Mathf.Max(LogicBlockWidth, _regions.Dock.width - 14f - left);
            int across = Mathf.Max(1, Mathf.FloorToInt((room + LogicBlockGap) / (LogicBlockWidth + LogicBlockGap)));
            float rowTop = top, rowFoot = top;
            for (int index = 0; index < _logicOutputs.Count; index++)
            {
                (LogicOutput output, Minimizer.Result expression, KarnaughLayout map) = _logicOutputs[index];
                if (index % across == 0)
                    rowTop = rowFoot;
                float x0 = left + index % across * (LogicBlockWidth + LogicBlockGap), y = rowTop;
                float right = x0 + LogicBlockWidth;
                bool free = false;
                foreach (char value in output.Values)
                    free |= value == 'x';
                string textbook = expression == null
                    ? "no expression past " + Minimizer.MostVariables.ToString(CultureInfo.InvariantCulture) + " input bits"
                    : Minimizer.Textbook(expression.Terms, names) + (free ? "     (x read as don't care)" : "")
                        + (expression.Proven ? "" : "     (the smallest found, not proven the smallest there is)");
                Label says = ShellTheme.Label(LogicExpressionName + output.Name, output.Name + " = " + textbook, UiType.Body, palette.Text);
                ShellTheme.Place(says, x0, y, right - x0, 28f);
                ShellTheme.Ellipsis(says);
                _logicList.Add(says);
                y += 28f;
                if (expression != null)
                {
                    Label verilog = ShellTheme.Label(LogicVerilogName + output.Name,
                        "assign " + output.Name + " = " + Minimizer.SystemVerilog(expression.Terms, names) + ";", UiType.Caption, palette.TextDim);
                    ShellTheme.Place(verilog, x0 + 14f, y, right - x0 - 14f, 24f);
                    ShellTheme.Ellipsis(verilog);
                    _logicList.Add(verilog);
                    y += 26f;
                }
                if (map != null)
                    y = DrawMap(map, x0 + 14f, y + 4f, palette) + 8f;
                rowFoot = Mathf.Max(rowFoot, y + 18f);
            }
            _logicRightReach = rowFoot + _logicRightScroll;
        }

        /// <summary>A K-map at <paramref name="x"/>, <paramref name="y"/>: the variables in its corner, Gray codes along its edges, a cell for each row. Its foot.</summary>
        private float DrawMap(KarnaughLayout map, float x, float y, SchematicPalette palette)
        {
            const float cell = 34f, side = 70f;
            Label corner = ShellTheme.Label("corner", (map.Down.Length > 0 ? map.Down + " \\ " : "") + map.Across, UiType.Micro, palette.TextDim);
            ShellTheme.Place(corner, x, y, side + cell * map.ColumnLabels.Count, 20f);
            _logicList.Add(corner);
            y += 20f;
            for (int c = 0; c < map.ColumnLabels.Count; c++)
            {
                Label label = ShellTheme.Label("column", map.ColumnLabels[c], UiType.Micro, palette.TextDim, TextAnchor.MiddleCenter);
                ShellTheme.Place(label, x + side + c * cell, y, cell, 20f);
                _logicList.Add(label);
            }
            y += 20f;
            for (int r = 0; r < map.RowLabels.Count; r++)
            {
                Label label = ShellTheme.Label("row", map.RowLabels[r], UiType.Micro, palette.TextDim, TextAnchor.MiddleRight);
                ShellTheme.Place(label, x, y + r * cell, side - 8f, cell);
                _logicList.Add(label);
                for (int c = 0; c < map.ColumnLabels.Count; c++)
                {
                    char value = map.Cells[r, c];
                    var box = new VisualElement { name = "cell", pickingMode = PickingMode.Ignore };
                    ShellTheme.Outline(box, palette.CardEdge, value == '1' ? palette.Over(palette.Accent, 0.18f) : palette.Ground, 1f, 0f);
                    ShellTheme.Place(box, x + side + c * cell, y + r * cell, cell, cell);
                    Label text = ShellTheme.Label("value", value.ToString(), UiType.Caption, value == '1' ? palette.Text : palette.TextDim, TextAnchor.MiddleCenter);
                    ShellTheme.Place(text, 0f, 0f, cell, cell);
                    box.Add(text);
                    _logicList.Add(box);
                }
            }
            return y + map.RowLabels.Count * cell;
        }
    }
}
