using System.Collections.Generic;
using System.Globalization;
using BitSorter.ViewModel;
using UnityEngine;
using UnityEngine.UIElements;
using ShellButton = UnityEngine.UIElements.Button;

namespace BitSorter.View
{
    /// <summary>
    /// The dock's INPUTS tab (Part 15g), beside WAVES: what the testbench gives the inputs, as a vector table to type in
    /// (<see cref="InputTable"/>) -- a column per input, a row per moment something changes, each cell what an input changes
    /// to then. Ahmad asked for a way to make input sequences and vectors without code, for buses as well as bits
    /// (2026-10-28).
    /// </summary>
    /// <remarks>
    /// **Its header**: + ROW, a row a STEP after the last; STEP, how far apart rows are put; ALL COMBINATIONS, COUNT and CLOCK,
    /// on the inputs chosen by a click on their columns' names; END, when the run ends. A column's radix -- BIN, HEX, DEC --
    /// changes with a click on it under the name. **Every edit is one undo step**, replayed from its moment, as SET's is
    /// (<see cref="WorkbenchSession.SetStimulus"/>). In an exercise the table shows the exercise's own test, and an edit is
    /// refused in the document's words.
    /// </remarks>
    public sealed partial class WorkbenchPanel
    {
        /// <summary>The tab's name, and its parts': each a name a test finds them by.</summary>
        public const string InputsTabName = "Workbench inputs tab", AddRowName = "Workbench inputs add row",
            StepFieldName = "Workbench inputs step", AllCombinationsName = "Workbench inputs all combinations",
            CountName = "Workbench inputs count", ClockName = "Workbench inputs clock", EndFieldName = "Workbench inputs end",
            InputsSaysName = "Workbench inputs says", InputColumnName = "Workbench input column ", InputRadixName = "Workbench input radix ",
            InputCellName = "Workbench input cell ", InputTimeName = "Workbench input time ", InputRemoveName = "Workbench input remove ",
            InputsConvertName = "Workbench inputs convert", InputsBenchName = "Workbench inputs bench";

        private const float InputsTabLeft = 104f, InputsTabWidth = 90f, InputRowHeight = 30f, TimeColumnWidth = 96f, RemoveWidth = 50f,
            KindWidth = 52f;

        private ShellButton _inputsTabButton;
        private VisualElement _inputsTools, _inputsList;
        private TextField _stepField, _endField;
        private Label _inputsSays;
        private ShellButton _inputsConvert;
        private float _inputsSaysLeft;
        private InputTable _inputs;
        private BitSorter.Board.Project _inputsOf;
        private int _inputsFirst, _inputsFirstColumn;
        private bool _inputsDrawn;

        /// <summary>Whether the dock shows the inputs' table.</summary>
        public bool InputsShown => _dockTab == DockTab.Inputs;

        /// <summary>The table as the tab shows it; null until the tab has been shown.</summary>
        public InputTable Inputs => _inputs;

        /// <summary>Shows the inputs' table, as its tab does.</summary>
        public void ShowInputs() => ShowDockTab(DockTab.Inputs);

        /// <summary>The INPUTS tab beside WAVES, and its header's tools.</summary>
        private void BuildInputsTab(VisualElement header, SchematicPalette palette)
        {
            _inputsTabButton = Tab(InputsTabName, "INPUTS", palette, () => ShowDockTab(DockTab.Inputs));
            ShellTheme.Place(_inputsTabButton, InputsTabLeft, 6f, InputsTabWidth, TabHeight);
            header.Add(_inputsTabButton);

            _inputsTools = new VisualElement { name = "inputs tools", pickingMode = PickingMode.Ignore };
            ShellTheme.Place(_inputsTools, 0f, 0f, -1f, ShellLayout.DockHeaderHeight);
            _inputsTools.style.right = 0f;
            header.Add(_inputsTools);

            float x = ToolsLeft;
            void Add(VisualElement element, float width)
            {
                ShellTheme.Place(element, x, 5f, width, 34f);
                _inputsTools.Add(element);
                x += width + 12f;
            }
            void Caption(string text, float width)
            {
                Label label = ShellTheme.Label("caption", text, UiType.Caption, palette.TextDim);
                ShellTheme.Place(label, x, 0f, width, ShellLayout.DockHeaderHeight);
                _inputsTools.Add(label);
                x += width;
            }

            Add(ShellTheme.Button(AddRowName, "+ ROW", palette, AddInputRow, UiType.Caption), 90f);
            Caption("STEP", 44f);
            _stepField = ShellTheme.Field(StepFieldName, "", palette, SetInputStep, type: UiType.Caption);
            Add(_stepField, 64f);
            Add(ShellTheme.Button(AllCombinationsName, "ALL COMBINATIONS", palette, AllCombinations, UiType.Caption), 190f);
            Add(ShellTheme.Button(CountName, "COUNT", palette, CountUp, UiType.Caption), 90f);
            Add(ShellTheme.Button(ClockName, "CLOCK", palette, MakeClock, UiType.Caption), 90f);
            Caption("END", 40f);
            _endField = ShellTheme.Field(EndFieldName, "", palette, SetInputEnd, type: UiType.Caption);
            Add(_endField, 80f);

            // Under a written testbench the tools above edit nothing (Part 15h): CONVERT in their place, which asks first.
            _inputsConvert = ShellTheme.Button(InputsConvertName, "CONVERT", palette, () => AskConvert(UnderTheConvertButton()), UiType.Caption);
            ShellTheme.Place(_inputsConvert, ToolsLeft, 5f, 120f, 34f);
            _inputsConvert.style.display = DisplayStyle.None;
            _inputsTools.Add(_inputsConvert);

            _inputsSaysLeft = x;
            _inputsSays = ShellTheme.Label(InputsSaysName, "", UiType.Caption, palette.TextDim);
            ShellTheme.Place(_inputsSays, x, 0f, -1f, ShellLayout.DockHeaderHeight);
            _inputsSays.style.right = 14f;
            ShellTheme.Ellipsis(_inputsSays);
            _inputsTools.Add(_inputsSays);
        }

        /// <summary>Where CONVERT's question goes up from INPUTS: under its button.</summary>
        private Vector2 UnderTheConvertButton()
        {
            Rect at = ShellPanel.ScreenRectOf(_inputsConvert);
            return new Vector2(at.xMin, at.yMin);
        }

        /// <summary>Whether a written testbench drives the inputs: the table then shows what it applies, and edits nothing.</summary>
        private static bool BenchDrives(InputTable table) => table != null && (table.AppliedBy != null || table.CannotConvert != null);

        /// <summary>
        /// The tools of the tab's header for <paramref name="table"/>: under a written testbench, CONVERT where the editing tools
        /// were, since nothing here edits the inputs; otherwise the editing tools.
        /// </summary>
        private void ShowInputTools(InputTable table)
        {
            bool bench = BenchDrives(table);
            foreach (VisualElement tool in _inputsTools.Children())
                if (tool != _inputsSays && tool != _inputsConvert)
                    tool.style.display = bench ? DisplayStyle.None : DisplayStyle.Flex;
            _inputsConvert.style.display = table?.AppliedBy != null ? DisplayStyle.Flex : DisplayStyle.None;
            float left = !bench ? _inputsSaysLeft : table.AppliedBy != null ? ToolsLeft + 132f : ToolsLeft;
            ShellTheme.Place(_inputsSays, left, 0f, -1f, ShellLayout.DockHeaderHeight);
            _inputsSays.style.right = 14f;
        }

        /// <summary>
        /// What the line over a testbench's table says in full: whose values the rows are and how to change them, or why the
        /// testbench cannot be converted.
        /// </summary>
        private static string BenchSays(InputTable table, string bench) => table.AppliedBy != null
            ? "These are the values " + bench + " applies to the inputs, read from a run of it"
              + (table.LeftOut > 0
                  ? ", the first " + table.Rows.Count.ToString("N0", CultureInfo.InvariantCulture) + " moments of "
                    + (table.Rows.Count + table.LeftOut).ToString("N0", CultureInfo.InvariantCulture)
                  : "")
              + ". To change them, CONVERT turns them into rows to edit here and takes " + bench
              + " out -- Ctrl+Z puts it back -- or change them in " + bench + "'s code."
            : bench + " drives the inputs, and cannot be turned into rows here: " + table.CannotConvert;

        /// <summary>The table's list, in the dock under its header, shut until its tab is chosen; the wheel scrolls it, Shift sideways.</summary>
        private void BuildInputsList()
        {
            _inputsList = new VisualElement { name = "inputs" };
            _inputsList.style.position = Position.Absolute;
            _inputsList.style.overflow = Overflow.Hidden;
            _inputsList.style.display = DisplayStyle.None;
            _inputsList.RegisterCallback<WheelEvent>(e =>
            {
                int by = e.delta.y > 0f || e.delta.x > 0f ? 1 : -1;
                if (e.shiftKey || Mathf.Abs(e.delta.x) > Mathf.Abs(e.delta.y))
                    _inputsFirstColumn = Mathf.Clamp(_inputsFirstColumn + by, 0, Mathf.Max(0, (_inputs?.Columns.Count ?? 1) - 1));
                else
                    _inputsFirst = Mathf.Clamp(_inputsFirst + by, 0, Mathf.Max(0, (_inputs?.Rows.Count ?? 0) - InputRowsShown));
                _inputsDrawn = false;
                e.StopPropagation();
            });
            _dockPanel.Add(_inputsList);
        }

        /// <summary>The list placed under the dock's header, where the transcript is.</summary>
        private void PlaceInputs()
        {
            if (_inputsList == null)
                return;
            ShellTheme.Place(_inputsList, 0f, ShellLayout.DockHeaderHeight, _regions.Dock.width,
                Mathf.Max(0f, _regions.Dock.height - ShellLayout.DockHeaderHeight));
            _inputsDrawn = false;
        }

        /// <summary>How many rows the list has room for under its column heads, and a testbench's line over them.</summary>
        private int InputRowsShown => Mathf.Max(1, Mathf.FloorToInt((_regions.Dock.height - ShellLayout.DockHeaderHeight - 8f) / InputRowHeight) - 1
            - (BenchDrives(_inputs) ? BenchLines : 0));

        // The rows a testbench's line takes, over the table: room for it to run on to a second line.
        private const int BenchLines = 2;

        /// <summary>
        /// Each frame: while the tab is shown, the table made again once the document has changed, and drawn when it has --
        /// but not under a mouse button held down. A cell let go by a press elsewhere commits, the document changes, and a
        /// redraw before the release took the button pressed out from under it, and the click with it (the 15g-4 review).
        /// </summary>
        private void RefreshInputs()
        {
            if (_session == null || _inputsList == null || _dockTab != DockTab.Inputs)
                return;

            SyncInputs();
            UnityEngine.InputSystem.Mouse mouse = UnityEngine.InputSystem.Mouse.current;
            bool held = mouse != null && (mouse.leftButton.isPressed || mouse.rightButton.isPressed || mouse.middleButton.isPressed);
            if (!_inputsDrawn && !held)
                DrawInputs();
        }

        /// <summary>
        /// The table made again if the document has changed since it was: before every edit as well as each frame, so an
        /// edit is made on the stimulus there is now. A cell let go by a click on DEL commits in the press, and DEL's edit,
        /// made on the table before it, threw that commit away (the 15g-4 review).
        /// </summary>
        private void SyncInputs()
        {
            if (_session == null || ReferenceEquals(_session.Project, _inputsOf))
                return;
            _inputsOf = _session.Project;
            _inputs = InputTable.Of(_session.Project, _inputs);
            _inputsDrawn = false;
        }

        /// <summary>A new document: its table starts from nothing of the last one's -- its choices, STEP, a row added (the 15g-4 review).</summary>
        private void ForgetInputs()
        {
            _inputs = null;
            _inputsOf = null;
            _inputsFirst = _inputsFirstColumn = 0;
            _inputsDrawn = false;
        }

        /// <summary>The row of the table now at <paramref name="time"/>, and the column of <paramref name="port"/>; -1 where there is none.</summary>
        private (int Row, int Column) InputAt(long time, string port)
        {
            SyncInputs();
            int row = -1, column = -1;
            if (_inputs == null)
                return (row, column);
            for (int i = 0; i < _inputs.Rows.Count; i++)
                if (_inputs.Rows[i].Time == time)
                    row = i;
            for (int c = 0; c < _inputs.Columns.Count; c++)
                if (port != null && _inputs.Columns[c].Port == port)
                    column = c;
            return (row, column);
        }

        /// <summary>The table drawn: its column heads, and the rows that fit from the first scrolled to.</summary>
        private void DrawInputs()
        {
            _inputsDrawn = true;
            _inputsList.Clear();
            if (_inputs == null)
                return;

            SchematicPalette palette = SchematicPalette.Current;
            InputTable table = _inputs;
            if (!IsFocused(_stepField))
                _stepField.SetValueWithoutNotify(table.Step.ToString(CultureInfo.InvariantCulture));
            if (!IsFocused(_endField))
                _endField.SetValueWithoutNotify(table.End.ToString(CultureInfo.InvariantCulture));
            bool bench = BenchDrives(table);
            ShowInputTools(table);
            string benchName = _session.Project.Bench;
            SetText(_inputsSays, bench
                ? (table.AppliedBy != null ? "Read only: what " + benchName + " applies, from a run of it." : "Read only: " + benchName + " drives the inputs.")
                : table.Why ?? (table.Columns.Count == 0
                    ? "The circuit has no inputs yet."
                    : "Click a column's name to choose it, and a bus's radix to change it."));

            // The whole of what a testbench's table is and how to change it, over the table, where it has the dock's width.
            float top = 0f;
            if (bench)
            {
                Label says = ShellTheme.Label(InputsBenchName, BenchSays(table, benchName), UiType.Caption, palette.Text);
                says.style.whiteSpace = WhiteSpace.Normal;
                ShellTheme.Place(says, 14f, 6f, _regions.Dock.width - 28f, BenchLines * InputRowHeight - 4f);
                _inputsList.Add(says);
                top = BenchLines * InputRowHeight;
            }

            if (table.Columns.Count == 0)
                return;

            _inputsFirstColumn = Mathf.Clamp(_inputsFirstColumn, 0, table.Columns.Count - 1);
            int shown = InputRowsShown;
            _inputsFirst = Mathf.Clamp(_inputsFirst, 0, Mathf.Max(0, table.Rows.Count - shown));

            // The column heads: TIME, then each column's name -- a click chooses it -- and its radix, a click changes it.
            Label time = ShellTheme.Label("time head", "TIME (" + _session.Unit + ")", UiType.Caption, palette.TextDim);
            ShellTheme.Place(time, 14f, top + 4f, TimeColumnWidth, InputRowHeight);
            _inputsList.Add(time);

            float x = 14f + TimeColumnWidth + 8f;
            for (int c = _inputsFirstColumn; c < table.Columns.Count && x < _regions.Dock.width - 40f; c++)
            {
                InputColumn column = table.Columns[c];
                float width = ColumnWidth(column);
                // A bit's radix has nothing to change, so its column's name has the room: the chip beside a bus's, a clock's
                // and an output's says what the column is.
                bool chip = column.Width > 1 || column.IsClock || !column.IsInput;
                string port = column.Port;
                ShellButton name = ShellTheme.Button(InputColumnName + column.Port, column.Name, palette, () => ChooseInput(InputAt(0, port).Column), UiType.Caption);
                if (column.Chosen)
                {
                    ShellTheme.Outline(name, palette.Accent, palette.Ground);
                    name.style.color = palette.Accent;
                }
                ShellTheme.Place(name, x, top + 4f, chip ? width - KindWidth - 4f : width, InputRowHeight - 4f);
                _inputsList.Add(name);

                if (chip)
                {
                    ShellButton kind = ShellTheme.Button(InputRadixName + column.Port, column.Kind, palette, () => NextInputRadix(InputAt(0, port).Column), UiType.Micro);
                    kind.style.color = palette.TextDim;
                    ShellTheme.Place(kind, x + width - KindWidth, top + 4f, KindWidth, InputRowHeight - 4f);
                    _inputsList.Add(kind);
                }
                x += width + 8f;
            }

            // The rows: the time, each column's cell, and the row taken away.
            for (int i = 0; i < shown && _inputsFirst + i < table.Rows.Count; i++)
            {
                int r = _inputsFirst + i;
                InputRow row = table.Rows[r];
                long moment = row.Time;   // what the controls find their row by, after an edit has renumbered the rows
                float y = top + 4f + (i + 1) * InputRowHeight;

                // A testbench's rows are read, never typed: its times and values are text, and no row has DEL.
                if (bench)
                {
                    Label at = ShellTheme.Label(InputTimeName + r, row.Time.ToString(CultureInfo.InvariantCulture), UiType.Caption, palette.Text);
                    ShellTheme.Place(at, 22f, y, TimeColumnWidth - 8f, InputRowHeight - 4f);
                    _inputsList.Add(at);
                }
                else
                {
                    TextField when = ShellTheme.Field(InputTimeName + r, "", palette, text => SetInputTime(InputAt(moment, null).Row, text), type: UiType.Caption);
                    when.SetValueWithoutNotify(row.Time.ToString(CultureInfo.InvariantCulture));
                    ShellTheme.Place(when, 14f, y, TimeColumnWidth, InputRowHeight - 4f);
                    _inputsList.Add(when);
                }

                x = 14f + TimeColumnWidth + 8f;
                for (int c = _inputsFirstColumn; c < table.Columns.Count && x < _regions.Dock.width - 40f; c++)
                {
                    InputColumn column = table.Columns[c];
                    float width = ColumnWidth(column);
                    if (column.IsInput && !column.IsClock && !bench)
                    {
                        string port = column.Port;
                        TextField cell = ShellTheme.Field(InputCellName + column.Port + " " + r, "", palette, text =>
                        {
                            (int atRow, int atColumn) = InputAt(moment, port);
                            SetInputCell(atRow, atColumn, text);
                        }, type: UiType.Caption);
                        cell.SetValueWithoutNotify(row.Cells[c]);
                        ShellTheme.Place(cell, x, y, width, InputRowHeight - 4f);
                        _inputsList.Add(cell);
                    }
                    else
                    {
                        // A clock ticks by itself, an output's cell is the value its test wants, and a testbench's is what it
                        // applies: read, never typed.
                        Label cell = ShellTheme.Label(InputCellName + column.Port + " " + r, column.IsClock ? "" : row.Cells[c], UiType.Caption,
                            column.IsInput && !bench ? palette.TextDim : palette.Text);
                        ShellTheme.Place(cell, x + 8f, y, width - 8f, InputRowHeight - 4f);
                        _inputsList.Add(cell);
                    }
                    x += width + 8f;
                }

                // DEL, never an x: an x in this table is a value.
                if (!bench)
                {
                    ShellButton remove = ShellTheme.Button(InputRemoveName + r, "DEL", palette, () => RemoveInputRow(InputAt(moment, null).Row), UiType.Micro);
                    ShellTheme.Place(remove, x, y, RemoveWidth, InputRowHeight - 4f);
                    _inputsList.Add(remove);
                }
            }
        }

        /// <summary>How wide a column is drawn: room for its name or its widest value, whichever is wider.</summary>
        private static float ColumnWidth(InputColumn column)
        {
            int digits = column.Radix == BitSorter.SystemVerilog.Radix.Hex ? (column.Width + 3) / 4
                : column.Radix == BitSorter.SystemVerilog.Radix.Unsigned ? Mathf.CeilToInt(column.Width * 0.302f) + 1
                : column.Width;
            int characters = Mathf.Max(column.Name.Length + 6, digits);
            return Mathf.Clamp(24f + characters * 9f, 110f, 300f);
        }

        private static bool IsFocused(TextField field) => field != null && field.focusController?.focusedElement == field;

        // -----------------------------------------------------------------
        // What the table's controls do
        // -----------------------------------------------------------------

        /// <summary>A cell typed in, and kept: the input given the value at the row's moment, or for nothing its step taken away.</summary>
        public void SetInputCell(int row, int column, string text)
        {
            SyncInputs();
            if (_inputs == null || row < 0 || row >= _inputs.Rows.Count || column < 0 || column >= _inputs.Columns.Count)
                return;
            if ((text ?? "").Trim() == _inputs.Rows[row].Cells[column])
                return;   // a field let go as it was -- by a redraw, or a click elsewhere -- is no edit
            bool done = _inputs.TrySetCell(row, column, text, out BitSorter.Board.Stimulus edited, out long from, out string refusal);
            Commit(done, edited, from, refusal);
        }

        /// <summary>A row's time typed again: its steps moved there.</summary>
        public void SetInputTime(int row, string text)
        {
            SyncInputs();
            if (_inputs == null || row < 0 || row >= _inputs.Rows.Count)
                return;
            if ((text ?? "").Trim() == _inputs.Rows[row].Time.ToString(CultureInfo.InvariantCulture))
                return;
            bool done = _inputs.TrySetTime(row, text, out BitSorter.Board.Stimulus edited, out long from, out InputTable table, out string refusal);
            _inputs = table;
            Commit(done, edited, from, refusal);
        }

        /// <summary>A row taken away: × at its end.</summary>
        public void RemoveInputRow(int row)
        {
            SyncInputs();
            if (_inputs == null)
                return;
            bool done = _inputs.TryRemoveRow(row, out BitSorter.Board.Stimulus edited, out long from, out InputTable table, out string refusal);
            _inputs = table;
            Commit(done, edited, from, refusal);
        }

        /// <summary>+ ROW: a row a step after the last, to type in.</summary>
        public void AddInputRow()
        {
            SyncInputs();
            if (_inputs == null)
                return;
            if (_inputs.ReadOnly)
            {
                Notice(_inputs.Why);
                return;
            }
            if (!_inputs.TryAddRow(out InputTable added, out string refusal))
            {
                Notice(refusal);
                return;
            }
            _inputs = added;
            _inputsFirst = Mathf.Max(0, _inputs.Rows.Count - InputRowsShown);   // the new row in sight
            _inputsDrawn = false;
        }

        /// <summary>STEP typed: how far apart + ROW, ALL COMBINATIONS and COUNT put rows.</summary>
        public void SetInputStep(string text)
        {
            SyncInputs();
            if (_inputs == null || (text ?? "").Trim() == _inputs.Step.ToString(CultureInfo.InvariantCulture))
                return;
            if (_inputs.TrySetStep(text, out InputTable table, out string refusal))
                _inputs = table;
            else
                Notice(refusal);
            _inputsDrawn = false;
        }

        /// <summary>END typed: when the run ends.</summary>
        public void SetInputEnd(string text)
        {
            SyncInputs();
            if (_inputs == null || (text ?? "").Trim() == _inputs.End.ToString(CultureInfo.InvariantCulture))
                return;
            bool done = _inputs.TrySetEnd(text, out BitSorter.Board.Stimulus edited, out string refusal);
            Commit(done, edited, 0, refusal);
        }

        /// <summary>ALL COMBINATIONS of the inputs chosen.</summary>
        public void AllCombinations()
        {
            SyncInputs();
            if (_inputs == null)
                return;
            bool done = _inputs.TryAllCombinations(out BitSorter.Board.Stimulus edited, out string refusal);
            Commit(done, edited, 0, refusal);
        }

        /// <summary>COUNT: the input chosen counting up.</summary>
        public void CountUp()
        {
            SyncInputs();
            if (_inputs == null)
                return;
            bool done = _inputs.TryCount(out BitSorter.Board.Stimulus edited, out string refusal);
            Commit(done, edited, 0, refusal);
        }

        /// <summary>CLOCK: the input chosen made a clock, or a clock made an input again.</summary>
        public void MakeClock()
        {
            SyncInputs();
            if (_inputs == null)
                return;
            bool done = _inputs.TryClock(out BitSorter.Board.Stimulus edited, out InputTable table, out string refusal);
            _inputs = table;   // the input let go of, so it stands in no fill's way
            Commit(done, edited, 0, refusal);
        }

        /// <summary>A column's name clicked: chosen, or not.</summary>
        public void ChooseInput(int column)
        {
            SyncInputs();
            if (_inputs == null)
                return;
            _inputs = _inputs.Choose(column);
            _inputsDrawn = false;
        }

        /// <summary>A column's radix clicked: its next.</summary>
        public void NextInputRadix(int column)
        {
            SyncInputs();
            if (_inputs == null)
                return;
            _inputs = _inputs.NextRadix(column);
            _inputsDrawn = false;
        }

        /// <summary>An edit of the table committed as one step and replayed, or the chip saying why not.</summary>
        private void Commit(bool done, BitSorter.Board.Stimulus edited, long from, string refusal)
        {
            _inputsDrawn = false;
            if (!done)
            {
                Notice(refusal);
                return;
            }
            if (edited != null && _session != null)
                Said(_session.SetStimulus(edited, from));
        }
    }
}
