using System;
using System.Collections.Generic;
using System.Globalization;
using BitSorter.Board;
using BitSorter.SystemVerilog;
using BitSorter.SystemVerilog.Syntax;

namespace BitSorter.ViewModel
{
    /// <summary>A column of the INPUTS table: an input of the top, or, in an exercise, an output its test checks.</summary>
    public sealed class InputColumn
    {
        internal InputColumn(string port, int width, bool input, long clock, Radix radix, bool chosen)
        {
            Port = port;
            Width = width;
            IsInput = input;
            ClockPeriod = clock;
            Radix = radix;
            Chosen = chosen;
        }

        public string Port { get; }
        public int Width { get; }

        /// <summary>An input; false for an output an exercise's test checks, its values the ones it wants.</summary>
        public bool IsInput { get; }

        /// <summary>Its period when it is a clock, which has no steps of its own; 0 for none.</summary>
        public long ClockPeriod { get; }

        public bool IsClock => ClockPeriod > 0;

        /// <summary>The radix its values are written and read in.</summary>
        public Radix Radix { get; }

        /// <summary>Whether it is chosen, for ALL COMBINATIONS, COUNT and CLOCK.</summary>
        public bool Chosen { get; }

        /// <summary>What its head says: its name, its range when it is a bus.</summary>
        public string Name => Width > 1 ? Port + "[" + (Width - 1).ToString(CultureInfo.InvariantCulture) + ":0]" : Port;

        /// <summary>What its head says under its name: its radix, or that it is a clock, or the value an output must have.</summary>
        public string Kind => IsClock ? "CLOCK " + ClockPeriod.ToString(CultureInfo.InvariantCulture)
            : !IsInput ? "EXPECTED"
            : Radix == Radix.Hex ? "HEX" : Radix == Radix.Unsigned ? "DEC" : "BIN";
    }

    /// <summary>A row of the INPUTS table: a moment, and what each column changes to then -- empty for nothing.</summary>
    public sealed class InputRow
    {
        internal InputRow(long time, string[] cells, bool pending)
        {
            Time = time;
            Cells = cells;
            Pending = pending;
        }

        public long Time { get; }
        public IReadOnlyList<string> Cells { get; }

        /// <summary>Added with + ROW and not given a value yet: nothing in the document says it is there.</summary>
        public bool Pending { get; }
    }

    /// <summary>
    /// The INPUTS tab's table (Part 15g): a vector table of what the testbench gives the top's inputs, made from the
    /// stimulus and giving edits of it back. A column per input, in the radix it is written in; a row per moment something
    /// changes, each cell what an input changes to then, or empty for nothing. Ahmad asked for a way to make input sequences
    /// and vectors without code, for buses as well as bits (2026-10-28): a double-click on the waves flipped one bit or asked
    /// for one value, which was all there was.
    /// </summary>
    /// <remarks>
    /// **The document is the stimulus; the rest is the table's own** -- each column's radix, what is chosen, the STEP that +
    /// ROW, ALL COMBINATIONS and COUNT space their rows by, and a row added and not filled yet, which nothing in the stimulus
    /// says is there. <see cref="Of"/> carries them over from the table before. Each edit gives back the stimulus to commit
    /// and the moment the run changes from; the session commits it as one undo step and replays it, as SET does.
    ///
    /// **In an exercise it is read only**, and shows the exercise's own test: its rows, and in a column of each output it
    /// checks, the value it wants. An edit is refused in the document's own words -- the exercise's test drives the inputs
    /// -- as SET is; and with a written testbench it says the testbench drives them.
    /// </remarks>
    public sealed class InputTable
    {
        /// <summary>How far apart + ROW, ALL COMBINATIONS and COUNT put their rows, until STEP says otherwise.</summary>
        public const long DefaultStep = 10;

        private readonly Dictionary<string, Radix> _radixes;
        private readonly HashSet<string> _chosen;
        private readonly List<long> _pending;
        private readonly Stimulus _stimulus;

        private InputTable(Stimulus stimulus, List<InputColumn> columns, List<InputRow> rows, string why, long step,
            Dictionary<string, Radix> radixes, HashSet<string> chosen, List<long> pending)
        {
            _stimulus = stimulus;
            Columns = columns;
            Rows = rows;
            Why = why;
            Step = step;
            _radixes = radixes;
            _chosen = chosen;
            _pending = pending;
        }

        public IReadOnlyList<InputColumn> Columns { get; }
        public IReadOnlyList<InputRow> Rows { get; }

        /// <summary>Why it cannot be changed -- an exercise's test, or a written testbench, drives the inputs -- or null when it can.</summary>
        public string Why { get; }

        public bool ReadOnly => Why != null;

        /// <summary>How far apart + ROW, ALL COMBINATIONS and COUNT put their rows.</summary>
        public long Step { get; }

        /// <summary>When the run is meant to end.</summary>
        public long End => _stimulus.End;

        /// <summary>The table of <paramref name="project"/>'s stimulus, keeping what <paramref name="before"/> had of its own.</summary>
        public static InputTable Of(Project project, InputTable before = null)
        {
            Stimulus stimulus = project.Stimulus ?? new Stimulus();
            string why = project.Settings?.Exercise != null ? BoardEditor.ExerciseDrivesTheInputs
                : project.Bench != null ? BoardRules.BenchDrivesTheInputs(project) : null;

            var radixes = before != null ? new Dictionary<string, Radix>(before._radixes) : new Dictionary<string, Radix>(StringComparer.Ordinal);
            var chosen = before != null ? new HashSet<string>(before._chosen) : new HashSet<string>(StringComparer.Ordinal);
            var pending = before != null ? new List<long>(before._pending) : new List<long>();
            long step = before?.Step ?? DefaultStep;

            var columns = new List<InputColumn>();
            Module top = project.TopModule;
            if (top != null)
            {
                foreach (ModulePort port in WrittenModules.PortsOf(project, top))
                {
                    bool input = port.Direction == PortDirection.Input;
                    if (!input && !Checked(stimulus, port.Name))
                        continue;
                    if (!radixes.TryGetValue(port.Name, out Radix radix))
                        radix = port.Width > 4 ? Radix.Hex : Radix.Binary;
                    columns.Add(new InputColumn(port.Name, port.Width, input, input ? ClockOf(stimulus, port.Name) : 0, radix,
                        input && chosen.Contains(port.Name)));
                }
            }

            // Every moment something changes, an exercise's checks among them, and the rows added and not filled yet.
            var times = new SortedSet<long>();
            foreach (Track track in stimulus.Tracks)
                foreach (Step each in track.Steps)
                    times.Add(each.Time);
            foreach (Check check in stimulus.Checks)
                times.Add(check.Time);
            pending.RemoveAll(time => times.Contains(time));
            if (why == null && times.Count == 0 && pending.Count == 0)
                pending.Add(0);   // somewhere to start typing
            foreach (long time in pending)
                times.Add(time);

            return new InputTable(stimulus, columns, RowsOf(stimulus, columns, times, pending), why, step, radixes, chosen, pending);
        }

        /// <summary>A value as a cell writes it: its digits in <paramref name="radix"/>, a bit as 0, 1, x or z.</summary>
        public static string Text(LogicVec value, Radix radix) =>
            value.Width == 1 ? value.Format(Radix.Binary) : !value.IsKnown && radix == Radix.Unsigned ? value.Format(Radix.Hex) : value.Format(radix);

        /// <summary>
        /// What is typed in a column of <paramref name="radix"/>, read as a value <paramref name="width"/> bits wide: digits in
        /// the column's radix, or any literal -- <c>5</c>, <c>'hA</c>, <c>4'b0101</c> -- or a bare x or z.
        /// </summary>
        public static bool TryRead(string text, int width, Radix radix, out LogicVec value, out string error)
        {
            string typed = (text ?? "").Replace(" ", "").Replace("_", "").Trim();
            bool literal = typed.IndexOf('\'') >= 0 || (typed.Length == 1 && "xXzZ".IndexOf(typed[0]) >= 0);
            if (!literal && typed.Length > 0)
                typed = radix == Radix.Hex ? "'h" + typed : radix == Radix.Binary ? "'b" + typed : typed;
            return Breakpoints.TryParseValue(typed, width, out value, out error);
        }

        // -----------------------------------------------------------------
        // The table's own
        // -----------------------------------------------------------------

        /// <summary>A row a <see cref="Step"/> after the last, or at 0, to type into: the table's until it is given a value.</summary>
        public InputTable AddRow()
        {
            long last = -Step;
            foreach (InputRow row in Rows)
                last = Math.Max(last, row.Time);
            var pending = new List<long>(_pending) { last + Step };
            return With(pending: pending);
        }

        /// <summary>STEP typed: a whole number of the project's unit, 1 or more.</summary>
        public bool TrySetStep(string text, out InputTable table, out string refusal)
        {
            table = this;
            if (!TryTime(text, out long step, out refusal) || step < 1)
            {
                refusal ??= "A step is a moment at least: 1 or more.";
                return false;
            }
            table = With(step: step);
            return true;
        }

        /// <summary>Column <paramref name="column"/> chosen, or not, for ALL COMBINATIONS, COUNT and CLOCK: an input's alone.</summary>
        public InputTable Choose(int column)
        {
            if (column < 0 || column >= Columns.Count || !Columns[column].IsInput)
                return this;
            var chosen = new HashSet<string>(_chosen, StringComparer.Ordinal);
            string port = Columns[column].Port;
            if (!chosen.Remove(port))
                chosen.Add(port);
            return With(chosen: chosen);
        }

        /// <summary>Column <paramref name="column"/> written in its next radix: binary, hex, decimal, and round again.</summary>
        public InputTable NextRadix(int column)
        {
            if (column < 0 || column >= Columns.Count || Columns[column].Width == 1)
                return this;
            Radix radix = Columns[column].Radix;
            var radixes = new Dictionary<string, Radix>(_radixes, StringComparer.Ordinal);
            radixes[Columns[column].Port] = radix == Radix.Binary ? Radix.Hex : radix == Radix.Hex ? Radix.Unsigned : Radix.Binary;
            return With(radixes: radixes);
        }

        // -----------------------------------------------------------------
        // Edits of the stimulus: what to commit, and from when the run changes
        // -----------------------------------------------------------------

        /// <summary>
        /// A cell typed in: the input given that value at the row's moment, or for nothing, its step there taken away. A
        /// pending row given a value becomes the stimulus's own.
        /// </summary>
        public bool TrySetCell(int row, int column, string text, out Stimulus edited, out long from, out string refusal)
        {
            edited = null;
            from = 0;
            if (!CanEdit(out refusal) || !TryInputColumn(column, out InputColumn input, out refusal) || !TryRow(row, out InputRow at, out refusal))
                return false;

            LogicVec? value = null;
            if (!string.IsNullOrWhiteSpace(text))
            {
                if (!TryRead(text, input.Width, input.Radix, out LogicVec read, out refusal))
                    return false;
                value = read;
            }

            from = at.Time;
            return StimulusEdits.TrySetStep(_stimulus, input.Port, at.Time, value, out edited, out refusal);
        }

        /// <summary>
        /// A row's time typed again: its steps moved there. A pending row just moves, the stimulus as it was --
        /// <paramref name="edited"/> null, and true.
        /// </summary>
        public bool TrySetTime(int row, string text, out Stimulus edited, out long from, out InputTable table, out string refusal)
        {
            edited = null;
            from = 0;
            table = this;
            if (!CanEdit(out refusal) || !TryRow(row, out InputRow at, out refusal) || !TryTime(text, out long to, out refusal))
                return false;

            if (at.Pending)
            {
                var pending = new List<long>(_pending);
                pending.Remove(at.Time);
                pending.Add(to);
                table = With(pending: pending);
                return true;
            }

            from = Math.Min(at.Time, to);
            return StimulusEdits.TryMoveRow(_stimulus, at.Time, to, out edited, out refusal);
        }

        /// <summary>A row taken away: its steps, or a pending row, which leaves the stimulus as it was -- <paramref name="edited"/> null.</summary>
        public bool TryRemoveRow(int row, out Stimulus edited, out long from, out InputTable table, out string refusal)
        {
            edited = null;
            from = 0;
            table = this;
            if (!CanEdit(out refusal) || !TryRow(row, out InputRow at, out refusal))
                return false;

            if (at.Pending)
            {
                var pending = new List<long>(_pending);
                pending.Remove(at.Time);
                table = With(pending: pending);
                return true;
            }

            from = at.Time;
            return StimulusEdits.TryRemoveRow(_stimulus, at.Time, out edited, out refusal);
        }

        /// <summary>ALL COMBINATIONS: every combination of the chosen inputs, a <see cref="Step"/> apart from 0, the first chosen the most significant.</summary>
        public bool TryAllCombinations(out Stimulus edited, out string refusal)
        {
            edited = null;
            if (!CanEdit(out refusal))
                return false;
            var ports = new List<(string, int)>();
            foreach (InputColumn column in Columns)
                if (column.Chosen)
                    ports.Add((column.Port, column.Width));
            return StimulusEdits.TryFillCombinations(_stimulus, ports, 0, Step, out edited, out refusal);
        }

        /// <summary>COUNT: the one input chosen counting up from 0, a <see cref="Step"/> apart.</summary>
        public bool TryCount(out Stimulus edited, out string refusal)
        {
            edited = null;
            if (!CanEdit(out refusal) || !TryOneChosen("count", out InputColumn column, out refusal))
                return false;
            return StimulusEdits.TryFillCount(_stimulus, column.Port, column.Width, 0, Step, out edited, out refusal);
        }

        /// <summary>CLOCK: the one input chosen made a clock that changes every <see cref="Step"/>, or a clock made an input again.</summary>
        public bool TryClock(out Stimulus edited, out string refusal)
        {
            edited = null;
            if (!CanEdit(out refusal) || !TryOneChosen("make a clock of", out InputColumn column, out refusal))
                return false;
            return column.IsClock
                ? StimulusEdits.TryUnmakeClock(_stimulus, column.Port, out edited, out refusal)
                : StimulusEdits.TryMakeClock(_stimulus, column.Port, 2 * Step, out edited, out refusal);
        }

        /// <summary>END typed: when the run is meant to end.</summary>
        public bool TrySetEnd(string text, out Stimulus edited, out string refusal)
        {
            edited = null;
            if (!CanEdit(out refusal) || !TryTime(text, out long end, out refusal))
                return false;
            return StimulusEdits.TrySetEnd(_stimulus, end, out edited, out refusal);
        }

        // -----------------------------------------------------------------

        /// <summary>
        /// The table again from its own stimulus with some of its own changed -- a radix, a choice, the step or a pending row --
        /// each in a copy, so the table before is as it was. The columns are kept: the ports and what drives them have not changed.
        /// </summary>
        private InputTable With(Dictionary<string, Radix> radixes = null, HashSet<string> chosen = null, List<long> pending = null, long? step = null)
        {
            radixes ??= new Dictionary<string, Radix>(_radixes, StringComparer.Ordinal);
            chosen ??= new HashSet<string>(_chosen, StringComparer.Ordinal);
            pending ??= new List<long>(_pending);

            var columns = new List<InputColumn>(Columns.Count);
            foreach (InputColumn column in Columns)
            {
                Radix radix = radixes.TryGetValue(column.Port, out Radix r) ? r : column.Radix;
                columns.Add(new InputColumn(column.Port, column.Width, column.IsInput, column.ClockPeriod, radix,
                    column.IsInput && chosen.Contains(column.Port)));
            }

            var times = new SortedSet<long>();
            foreach (InputRow row in Rows)
                if (!row.Pending)
                    times.Add(row.Time);
            pending.RemoveAll(time => times.Contains(time));
            foreach (long time in pending)
                times.Add(time);

            return new InputTable(_stimulus, columns, RowsOf(_stimulus, columns, times, pending), Why, step ?? Step, radixes, chosen, pending);
        }

        /// <summary>A row for each of <paramref name="times"/>: each column's value then, or nothing.</summary>
        private static List<InputRow> RowsOf(Stimulus stimulus, List<InputColumn> columns, SortedSet<long> times, List<long> pending)
        {
            var rows = new List<InputRow>(times.Count);
            foreach (long time in times)
            {
                var cells = new string[columns.Count];
                for (int c = 0; c < columns.Count; c++)
                {
                    LogicVec? value = columns[c].IsInput ? StepAt(stimulus, columns[c].Port, time) : CheckAt(stimulus, columns[c].Port, time);
                    cells[c] = value.HasValue ? Text(value.Value, columns[c].Radix) : "";
                }
                rows.Add(new InputRow(time, cells, pending.Contains(time)));
            }
            return rows;
        }

        private bool CanEdit(out string refusal)
        {
            refusal = Why;
            return Why == null;
        }

        private bool TryInputColumn(int column, out InputColumn input, out string refusal)
        {
            input = column >= 0 && column < Columns.Count ? Columns[column] : null;
            refusal = input == null ? "There is no such column."
                : !input.IsInput ? $"{input.Port} is an output: the test reads it, and nothing gives it a value."
                : input.IsClock ? $"{input.Port} is a clock: it ticks by itself. Choose it and press CLOCK to give it steps instead."
                : null;
            return refusal == null;
        }

        private bool TryRow(int row, out InputRow at, out string refusal)
        {
            at = row >= 0 && row < Rows.Count ? Rows[row] : null;
            refusal = at == null ? "There is no such row." : null;
            return at != null;
        }

        private bool TryOneChosen(string doing, out InputColumn column, out string refusal)
        {
            column = null;
            int count = 0;
            foreach (InputColumn each in Columns)
            {
                if (!each.Chosen)
                    continue;
                column = each;
                count++;
            }
            refusal = count == 1 ? null : $"Choose one input to {doing}: click its column's name.";
            return count == 1;
        }

        private static bool TryTime(string text, out long time, out string refusal)
        {
            refusal = null;
            if (long.TryParse((text ?? "").Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out time))
                return true;
            refusal = $"\"{(text ?? "").Trim()}\" is no moment: a whole number of the circuit's unit, 0 or more.";
            return false;
        }

        private static long ClockOf(Stimulus stimulus, string port)
        {
            foreach (ClockTrack clock in stimulus.Clocks)
                if (string.Equals(clock.Port, port, StringComparison.Ordinal))
                    return clock.Period;
            return 0;
        }

        private static bool Checked(Stimulus stimulus, string port)
        {
            foreach (Check check in stimulus.Checks)
                if (string.Equals(check.Port, port, StringComparison.Ordinal))
                    return true;
            return false;
        }

        private static LogicVec? StepAt(Stimulus stimulus, string port, long time)
        {
            foreach (Track track in stimulus.Tracks)
                if (string.Equals(track.Port, port, StringComparison.Ordinal))
                    foreach (Step step in track.Steps)
                        if (step.Time == time)
                            return step.Value;
            return null;
        }

        private static LogicVec? CheckAt(Stimulus stimulus, string port, long time)
        {
            foreach (Check check in stimulus.Checks)
                if (check.Time == time && string.Equals(check.Port, port, StringComparison.Ordinal))
                    return check.Expected;
            return null;
        }
    }
}
